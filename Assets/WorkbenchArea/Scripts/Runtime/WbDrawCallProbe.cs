using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorkbenchArea
{
    /// <summary>
    /// 独立构建里的 Draw Call 拆解（测量工具，不参与玩法）。只有命令行带 -wbProbe &lt;输出文件&gt; 时才启动，测完自动退出。
    /// 逐项关闭：IMGUI HUD → 阴影 → SSAO 渲染特性 → 后处理 → 杂物 → 各分组 → 整个区域，每一步 120 帧取中位数，差值就是该项的 Draw Call 来源。
    /// URP 的渲染特性与相机后处理开关用反射访问，避免运行时程序集依赖 URP。
    /// </summary>
    public class WbDrawCallProbe : MonoBehaviour
    {
        string outPath;
        readonly StringBuilder sb = new StringBuilder();
        ProfilerRecorder rDraw, rBatch, rSetPass, rTris, rShadow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-wbProbe");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("WbDrawCallProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<WbDrawCallProbe>().outPath = args[i + 1];
        }

        IEnumerator Start()
        {
            rDraw = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            rBatch = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            rSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            rTris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            rShadow = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            sb.AppendLine($"工作台区域 · 独立构建 Draw Call 拆解  Unity {Application.unityVersion}  {(Debug.isDebugBuild ? "Development" : "Release")} 构建");
            sb.AppendLine($"屏幕 {Screen.width}×{Screen.height}  图形 API {SystemInfo.graphicsDeviceType}  GPU {SystemInfo.graphicsDeviceName}  质量档 {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            sb.AppendLine($"渲染管线 {GraphicsSettings.currentRenderPipeline?.name}");
            for (int k = 0; k < 90; k++) yield return null;   // 预热：着色器、阴影图集

            var rig = FindFirstObjectByType<WbCameraRig>();
            var demo = FindFirstObjectByType<WbPlaceholderDemo>();
            var log = FindFirstObjectByType<WbInteractionLog>();
            var area = GameObject.Find("WorkbenchArea");
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            var shadowsOrig = lights.ToDictionary(l => l, l => l.shadows);
            var features = RendererFeatures();
            var cam = rig.Cam;
            var camData = cam.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name == "UniversalAdditionalCameraData");
            var ppProp = camData?.GetType().GetProperty("renderPostProcessing");
            var groups = area.transform.Cast<Transform>().ToList();
            var clutter = area.GetComponentsInChildren<WbPartProperties>(true).Where(p => p.Role == "clutter").Select(p => p.gameObject).ToList();
            sb.AppendLine($"渲染特性：{(features.Count == 0 ? "（未找到）" : string.Join(", ", features.Select(f => ((UnityEngine.Object)f).name)))}；相机后处理开关：{(ppProp != null ? ppProp.GetValue(camData) : "未找到")}");
            sb.AppendLine($"区域内 Renderer {area.GetComponentsInChildren<Renderer>(true).Length} 个，材质槽 {area.GetComponentsInChildren<Renderer>(true).Sum(r => r.sharedMaterials.Length)} 个，" +
                          $"静态批处理的 Renderer {area.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.isPartOfStaticBatch)} 个");
            sb.AppendLine();
            sb.AppendLine("步骤\tDraw Calls\tBatches\tSetPass\t三角面\t阴影投射体\t帧时间中位 ms\t可见 Renderer");

            var results = new Dictionary<string, long>();
            IEnumerator M(string label)
            {
                for (int k = 0; k < 20; k++) yield return null;
                var d = new List<long>(); var b = new List<long>(); var s = new List<long>(); var t = new List<long>(); var sh = new List<long>(); var f = new List<float>();
                for (int k = 0; k < 120; k++)
                {
                    yield return null;
                    d.Add(rDraw.LastValue); b.Add(rBatch.LastValue); s.Add(rSetPass.LastValue); t.Add(rTris.LastValue); sh.Add(rShadow.LastValue); f.Add(Time.unscaledDeltaTime * 1000f);
                }
                int visible = area.GetComponentsInChildren<Renderer>(false).Count(r => r.isVisible);
                results[label] = Med(d);
                sb.AppendLine($"{label}\t{Med(d)}\t{Med(b)}\t{Med(s)}\t{Med(t)}\t{Med(sh)}\t{Med(f):F2}\t{visible}");
            }

            void Hud(bool on) { rig.enabled = on; demo.enabled = on; if (log) log.enabled = on; }
            void Shadows(bool on) { foreach (var l in lights) l.shadows = on ? shadowsOrig[l] : LightShadows.None; }
            void Features(bool on) { foreach (var f in features) f.GetType().GetMethod("SetActive")?.Invoke(f, new object[] { on }); }
            void Post(bool on) { if (ppProp != null) ppProp.SetValue(camData, on); }

            // 游戏镜头
            rig.SetView(WbCameraRig.View.Game);
            yield return M("A 游戏镜头 · 完整（含 HUD）");
            Hud(false);
            yield return M("B 去掉 IMGUI HUD");
            Shadows(false);
            yield return M("C 再关阴影");
            Features(false);
            yield return M("D 再关 SSAO 渲染特性");
            Post(false);
            yield return M("E 再关后处理（只剩几何主通道）");
            foreach (var g in clutter) g.SetActive(false);
            yield return M("F 在 E 基础上去掉杂物");
            foreach (var g in clutter) g.SetActive(true);
            foreach (var g in groups)
            {
                g.gameObject.SetActive(false);
                yield return M($"G 在 E 基础上去掉 {g.name}");
                g.gameObject.SetActive(true);
            }
            area.SetActive(false);
            yield return M("H 在 E 基础上去掉整个区域（管线固定开销）");
            area.SetActive(true);
            // 恢复后测近距镜头
            Post(true); Features(true); Shadows(true); Hud(true);
            rig.SetView(WbCameraRig.View.CloseUp);
            yield return M("I 近距镜头 · 完整（含 HUD）");

            sb.AppendLine();
            sb.AppendLine("拆解（游戏镜头，Draw Calls 中位数差值）：");
            long A = results["A 游戏镜头 · 完整（含 HUD）"], B = results["B 去掉 IMGUI HUD"], C = results["C 再关阴影"], D = results["D 再关 SSAO 渲染特性"],
                 E = results["E 再关后处理（只剩几何主通道）"], F = results["F 在 E 基础上去掉杂物"], H = results["H 在 E 基础上去掉整个区域（管线固定开销）"];
            sb.AppendLine($"  IMGUI HUD：{A - B}");
            sb.AppendLine($"  阴影（工作灯阴影图）：{B - C}");
            sb.AppendLine($"  SSAO（深度法线预通道 + AO 通道）：{C - D}");
            sb.AppendLine($"  后处理：{D - E}");
            sb.AppendLine($"  几何主通道：{E - H}（其中杂物 {E - F}）");
            foreach (var g in groups) { var k = $"G 在 E 基础上去掉 {g.name}"; sb.AppendLine($"    {g.name}：{E - results[k]}"); }
            sb.AppendLine($"  管线固定开销（没有任何物体时）：{H}");
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Application.Quit(0);
        }

        static List<object> RendererFeatures()
        {
            var list = new List<object>();
            var asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null) return list;
            var field = asset.GetType().GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
            if (!(field?.GetValue(asset) is Array datas)) return list;
            foreach (var d in datas)
            {
                if (d == null) continue;
                if (d.GetType().GetProperty("rendererFeatures")?.GetValue(d) is IEnumerable feats)
                    foreach (var f in feats) if (f != null && !list.Contains(f)) list.Add(f);
            }
            return list;
        }

        static long Med(List<long> v) { var s = v.OrderBy(x => x).ToList(); return s.Count == 0 ? 0 : s[s.Count / 2]; }
        static float Med(List<float> v) { var s = v.OrderBy(x => x).ToList(); return s.Count == 0 ? 0 : s[s.Count / 2]; }

        void OnDestroy() { rDraw.Dispose(); rBatch.Dispose(); rSetPass.Dispose(); rTris.Dispose(); rShadow.Dispose(); }
    }
}
