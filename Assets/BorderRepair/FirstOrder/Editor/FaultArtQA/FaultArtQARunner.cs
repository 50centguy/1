using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.FirstOrder.EditorTools.LayoutAB;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace BorderRepair.FirstOrder.EditorTools.FaultArtQA
{
    /// <summary>
    /// 故障美术真实场景验收（带界面的编辑器 Play 模式；B 为主、A 同条件对照）。
    /// 用现有验收驱动把首单推进到几个状态（只是摆状态，不是点击验收；真人鼠标试玩由用户另行验收），每个状态在同一帧、同一机位渲染“调整前 / 调整后”：
    /// 常用镜头 1–5（维修座、左引擎、背面、工作台、总览）、近景镜头 7–9（保养记录、新旧对比、左引擎近看）和玩家站位视线。
    /// 量：目标在画面里有多大、两种状态之间画面差多少（能否凭外观区分）、保养标记的过曝 / 对比度、轴承区亮度；
    /// 细纤维在运动镜头下的闪烁；故障美术件开 / 关、调整开 / 关的三角面、Draw Call 和渲染耗时。
    /// 截图只渲染 Unity 相机（游戏镜头本身：同样的后处理、抗锯齿、MSAA 设置），不读屏幕。
    /// </summary>
    [InitializeOnLoad]
    public static class FaultArtQARunner
    {
        const string KeyQueue = "FaultArtQA.Queue";
        static string OutDir => Path.GetFullPath(FaultArtAudit.OutDir);

        static FaultArtQARunner() { EditorApplication.playModeStateChanged += OnPlayMode; }

        public static void Begin()
        {
            Directory.CreateDirectory(Path.Combine(OutDir, "Screenshots"));
            Directory.CreateDirectory(Path.Combine(OutDir, "Crops"));
            foreach (var d in new[] { "Screenshots", "Crops" }) foreach (var f in Directory.GetFiles(Path.Combine(OutDir, d), "*.png")) File.Delete(f);
            SessionState.SetString(KeyQueue, "B,A");
            Next();
        }

        static void Next()
        {
            var q = SessionState.GetString(KeyQueue, "");
            if (q.Length == 0) { if (!Application.isBatchMode && Environment.GetCommandLineArgs().Contains("-executeMethod")) EditorApplication.Exit(0); return; }
            EditorSceneManager.OpenScene(q.Split(',')[0] == "A" ? LayoutABScenes.SceneA : LayoutABScenes.SceneB, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            var q = SessionState.GetString(KeyQueue, "");
            if (q.Length == 0) return;
            if (s == PlayModeStateChange.EnteredPlayMode) new GameObject("FaultArtQAHost").AddComponent<Host>().layout = q.Split(',')[0];
            if (s == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetString(KeyQueue, string.Join(",", q.Split(',').Skip(1)));
                EditorApplication.delayCall += Next;
            }
        }

        class Host : MonoBehaviour
        {
            public string layout;
            FirstOrderFlow flow; FaultArtLookFix fix; Camera cam; RenderTexture rt;
            readonly StringBuilder md = new StringBuilder();
            readonly List<string> console = new List<string>();
            readonly Dictionary<string, Texture2D> shots = new Dictionary<string, Texture2D>();
            const int W = 1600, H = 900;

            IEnumerator Start()
            {
                Application.logMessageReceived += (m, st, type) => { if (type != LogType.Log) console.Add($"{type}: {m.Split('\n')[0]}"); };
                for (int i = 0; i < 10; i++) yield return null;
                flow = Object.FindFirstObjectByType<FirstOrderFlow>();
                fix = Object.FindFirstObjectByType<FaultArtLookFix>();
                var input = Object.FindFirstObjectByType<FirstOrderInput>();
                input.enabled = false;
                cam = flow.Rig.Cam;
                var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = Mathf.Max(1, urp != null ? urp.msaaSampleCount : 1) };
                md.AppendLine($"# 布局 {layout} · 故障美术场景验收量测");
                md.AppendLine();
                md.AppendLine($"- {DateTime.Now:yyyy-MM-dd HH:mm}，Unity {Application.unityVersion}，{SystemInfo.graphicsDeviceName}，渲染 {W}×{H}，MSAA {rt.antiAliasing}x（与 URP 设置一致），游戏镜头抗锯齿（调整前）{cam.GetUniversalAdditionalCameraData().antialiasing}");
                md.AppendLine($"- 观感调整对象：{(fix != null ? "有" : "**没有**")}");
                md.AppendLine("- 状态由验收驱动推进（只是摆状态，不算点击验收）。");
                md.AppendLine();

                var driver = new FirstOrderAcceptanceDriver(flow, input) { Timeout = 90f };
                var it = driver.RunFullOrder();
                var done = new HashSet<string>();
                while (it.MoveNext())
                {
                    yield return it.Current;
                    if (!done.Contains("clog") && flow.Step == FoStep.ReleaseLatches && !flow.ClogCleared) { done.Add("clog"); yield return State("S1_clogged"); yield return Flicker(); yield return Cost("S1"); }
                    if (!done.Contains("clean") && flow.ClogCleared && flow.Cover.Location == PartLocation.Installed && flow.Step == FoStep.ReleaseLatches) { done.Add("clean"); yield return State("S2_cleaned"); }
                    if (!done.Contains("worn") && flow.Step == FoStep.LocateBearing) { done.Add("worn"); yield return State("S3_worn_in_seat"); }
                    if (!done.Contains("bench") && flow.Step == FoStep.FetchNewBearing) { done.Add("bench"); yield return State("S4_bench_old_new"); yield return Cost("S4"); }
                    if (!done.Contains("new") && flow.Step == FoStep.ReinstallCover) { done.Add("new"); yield return State("S5_new_in_seat"); }
                }
                Analyse();
                md.AppendLine();
                md.AppendLine($"- 控制台警告 / 错误：{console.Count} 条{(console.Count > 0 ? "：" + string.Join(" | ", console.Distinct().Take(8)) : "")}；状态推进 {driver.Records.Count(r => r.pass)}/{driver.Records.Count} 步通过");
                File.WriteAllText(Path.Combine(OutDir, $"measure_{layout}.md"), md.ToString(), new UTF8Encoding(false));
                rt.Release();
                EditorApplication.ExitPlaymode();
            }

            // ---------------------------------------------------------------- 相机与渲染

            (string id, Pose pose, float fov)[] Cameras(string state)
            {
                var rig = flow.Rig;
                (string, Pose, float) R(string id) { var s = rig.Get(id); return ($"cam{Array.IndexOf(FirstOrderCameraRig.Order, id) + 1}_{id}", new Pose(s.pose.position, s.pose.rotation), s.fov); }
                var (stand, _) = LayoutABScenes.Stand(layout);
                var eye = stand + Vector3.up * LayoutPlanner.EyeHeight;
                (string, Pose, float) Eye(string what, Vector3 at) => ($"eye_{what}", new Pose(eye, Quaternion.LookRotation(at - eye)), 60f);
                var clog = flow.ClogLayers.Last().bounds.center;
                var seat = flow.Bearing.HomeWorldPose().position;
                switch (state)
                {
                    case "S1_clogged": case "S2_cleaned":
                        return new[] { R(FirstOrderCameraRig.Dock), R(FirstOrderCameraRig.EngineL), R(FirstOrderCameraRig.Overview), R(FirstOrderCameraRig.EngineClose), Eye("intake", clog) };
                    case "S3_worn_in_seat":
                        return new[] { R(FirstOrderCameraRig.EngineL), R(FirstOrderCameraRig.Overview), R(FirstOrderCameraRig.EngineClose), Eye("seat", seat),
                                       R(FirstOrderCameraRig.Record), R(FirstOrderCameraRig.Bench), Eye("label", flow.CoverLabel.bounds.center) };
                    case "S5_new_in_seat":
                        return new[] { R(FirstOrderCameraRig.EngineL), R(FirstOrderCameraRig.Overview), R(FirstOrderCameraRig.EngineClose), Eye("seat", seat) };
                    case "S4_bench_old_new":
                        return new[] { R(FirstOrderCameraRig.Bench), R(FirstOrderCameraRig.Overview), R(FirstOrderCameraRig.Compare), Eye("compare", (flow.Bearing.WorldBounds().center + flow.NewBearing.WorldBounds().center) / 2f) };
                }
                return new (string, Pose, float)[0];
            }

            Texture2D Render(Pose pose, float fov)
            {
                var rig = flow.Rig; bool rigOn = rig.enabled; rig.enabled = false;
                var (p0, r0, f0) = (cam.transform.position, cam.transform.rotation, cam.fieldOfView);
                cam.transform.SetPositionAndRotation(pose.position, pose.rotation); cam.fieldOfView = fov;
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                cam.transform.SetPositionAndRotation(p0, r0); cam.fieldOfView = f0; rig.enabled = rigOn;
                var a = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = a;
                return tex;
            }

            /// <summary>只渲染不读回（测耗时用）：连续渲染 n 次，最后读 1 个像素让 CPU 等 GPU 做完。</summary>
            void RenderOnly(Pose pose, float fov, int n)
            {
                var rig = flow.Rig; bool rigOn = rig.enabled; rig.enabled = false;
                var (p0, r0, f0) = (cam.transform.position, cam.transform.rotation, cam.fieldOfView);
                cam.transform.SetPositionAndRotation(pose.position, pose.rotation); cam.fieldOfView = fov;
                cam.targetTexture = rt;
                for (int i = 0; i < n; i++) cam.Render();
                cam.targetTexture = null;
                var a = RenderTexture.active; RenderTexture.active = rt;
                var one = new Texture2D(1, 1, TextureFormat.RGB24, false); one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); one.Apply(); Destroy(one);
                RenderTexture.active = a;
                cam.transform.SetPositionAndRotation(p0, r0); cam.fieldOfView = f0; rig.enabled = rigOn;
            }

            void Save(Texture2D t, string dir, string name) => File.WriteAllBytes(Path.Combine(OutDir, dir, name + ".png"), t.EncodeToPNG());

            IEnumerator State(string state)
            {
                yield return null;
                foreach (var on in new[] { false, true })
                {
                    if (fix != null) fix.Apply(on);
                    yield return null; yield return null;          // 反射探针开关要到下一帧才生效
                    foreach (var (id, pose, fov) in Cameras(state))
                    {
                        var t = Render(pose, fov);
                        string key = $"{state}|{id}|{(on ? "after" : "before")}";
                        shots[key] = t;
                        Save(t, "Screenshots", $"{layout}_{state}_{id}_{(on ? "after" : "before")}");
                        regions[key] = Regions(pose, fov);
                    }
                }
                if (fix != null) fix.Apply(true);
                RedCheck(state);
            }

            // ---------------------------------------------------------------- 画面区域

            readonly Dictionary<string, Dictionary<string, RectInt>> regions = new Dictionary<string, Dictionary<string, RectInt>>();

            Dictionary<string, RectInt> Regions(Pose pose, float fov)
            {
                var rig = flow.Rig; bool rigOn = rig.enabled; rig.enabled = false;
                var (p0, r0, f0) = (cam.transform.position, cam.transform.rotation, cam.fieldOfView);
                cam.transform.SetPositionAndRotation(pose.position, pose.rotation); cam.fieldOfView = fov;
                var res = new Dictionary<string, RectInt>();
                RectInt Rect(IEnumerable<Bounds> bs)
                {
                    float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue; bool any = false;
                    foreach (var b in bs)
                        for (int i = 0; i < 8; i++)
                        {
                            var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1));
                            var v = cam.WorldToViewportPoint(c);
                            if (v.z <= 0) continue;
                            any = true; x0 = Mathf.Min(x0, v.x * W); x1 = Mathf.Max(x1, v.x * W); y0 = Mathf.Min(y0, v.y * H); y1 = Mathf.Max(y1, v.y * H);
                        }
                    if (!any) return new RectInt(0, 0, 0, 0);
                    int ix0 = Mathf.Clamp((int)x0, 0, W), iy0 = Mathf.Clamp((int)y0, 0, H), ix1 = Mathf.Clamp((int)x1, 0, W), iy1 = Mathf.Clamp((int)y1, 0, H);
                    return new RectInt(ix0, iy0, Mathf.Max(0, ix1 - ix0), Mathf.Max(0, iy1 - iy0));
                }
                res["intake"] = Rect(new[] { flow.Cover.members.First(m => m.name.Contains("IntakeGuard")).GetComponent<Renderer>().bounds });
                res["seat"] = Rect(new[] { flow.OriginalBearingRenderer.bounds });     // 原轴承渲染器关着，但包围盒就是轴承位
                res["tray"] = Rect(flow.Bearing.Renderers().Where(r => r != flow.OriginalBearingRenderer).Select(r => r.bounds));
                res["box"] = Rect(flow.NewBearing.Renderers().Select(r => r.bounds));
                res["label"] = Rect(new[] { flow.CoverLabel.bounds });
                cam.transform.SetPositionAndRotation(p0, r0); cam.fieldOfView = f0; rig.enabled = rigOn;
                return res;
            }

            static float L(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

            (float mean, float p2, float p50, float clip, float dark, float chroma, float std, int n) Stats(Texture2D t, RectInt r)
            {
                var ls = new List<float>(); float ch = 0f;
                for (int y = r.yMin; y < r.yMax; y++)
                    for (int x = r.xMin; x < r.xMax; x++) { var c = t.GetPixel(x, y); ls.Add(L(c)); ch += c.r - c.b; }
                if (ls.Count == 0) return (0, 0, 0, 0, 0, 0, 0, 0);
                ls.Sort();
                float mean = ls.Average();
                float std = Mathf.Sqrt(ls.Sum(v => (v - mean) * (v - mean)) / ls.Count);
                return (mean, ls[(int)(ls.Count * 0.02f)], ls[(int)(ls.Count * 0.50f)], ls.Count(v => v >= 0.97f) / (float)ls.Count, ls.Count(v => v <= 0.04f) / (float)ls.Count, ch / ls.Count, std, ls.Count);
            }

            (float meanDiff, float frac) Diff(Texture2D a, Texture2D b, RectInt r)
            {
                float sum = 0f; int n = 0, big = 0;
                for (int y = r.yMin; y < r.yMax; y++)
                    for (int x = r.xMin; x < r.xMax; x++)
                    {
                        var p = a.GetPixel(x, y); var q = b.GetPixel(x, y);
                        float d = Mathf.Max(Mathf.Abs(p.r - q.r), Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b));
                        sum += d; n++; if (d > 0.08f) big++;
                    }
                return n == 0 ? (0f, 0f) : (sum / n, big / (float)n);
            }

            /// <summary>把若干张图的同一区域裁下来（放大到同样高度）横向拼成一张，方便并排看。</summary>
            void SaveCrops(string name, RectInt r, params Texture2D[] ts)
            {
                if (r.width < 2 || r.height < 2) return;
                var e = new RectInt(Mathf.Max(0, r.xMin - r.width / 3), Mathf.Max(0, r.yMin - r.height / 3), 0, 0);
                e.width = Mathf.Min(W - e.xMin, r.width * 5 / 3); e.height = Mathf.Min(H - e.yMin, r.height * 5 / 3);
                int oh = 320, ow = Mathf.Max(8, Mathf.RoundToInt(oh * e.width / (float)e.height)), gap = 6;
                var outT = new Texture2D(ow * ts.Length + gap * (ts.Length - 1), oh, TextureFormat.RGB24, false);
                var px = Enumerable.Repeat(new Color(0.1f, 0.1f, 0.1f), outT.width * oh).ToArray();
                for (int k = 0; k < ts.Length; k++)
                    for (int y = 0; y < oh; y++)
                        for (int x = 0; x < ow; x++)
                            px[y * outT.width + k * (ow + gap) + x] = ts[k].GetPixelBilinear((e.xMin + (x + 0.5f) * e.width / ow) / W, (e.yMin + (y + 0.5f) * e.height / oh) / H);
                outT.SetPixels(px); outT.Apply();
                Save(outT, "Crops", name);
                Destroy(outT);
            }

            // ---------------------------------------------------------------- 红色高亮检查

            readonly List<string> red = new List<string>();
            void RedCheck(string state)
            {
                var block = new MaterialPropertyBlock();
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (!r.HasPropertyBlock()) continue;
                    r.GetPropertyBlock(block);
                    if (block.HasColor("_BaseColor")) { var c = block.GetColor("_BaseColor"); if (c.r > 0.6f && c.g < 0.4f && c.b < 0.4f) red.Add($"{state}:{r.name}"); }
                }
            }

            // ---------------------------------------------------------------- 细纤维闪烁

            readonly StringBuilder flick = new StringBuilder();

            IEnumerator Flicker()
            {
                yield return null;
                var center = flow.ClogLayers.Last().bounds.center;
                var engL = flow.Rig.Get(FirstOrderCameraRig.EngineL); var dockS = flow.Rig.Get(FirstOrderCameraRig.Dock); var closeS = flow.Rig.Get(FirstOrderCameraRig.EngineClose);
                var paths = new List<(string name, Func<int, (Pose, float)> at, int frames)>
                {
                    ("镜头切换：维修座 → 左引擎（原型 0.35 s 平滑过渡，60 帧/秒 21 帧）", i => { float k = Mathf.SmoothStep(0, 1, i / 20f); return (new Pose(Vector3.Lerp(dockS.pose.position, engL.pose.position, k), Quaternion.Slerp(dockS.pose.rotation, engL.pose.rotation, k)), Mathf.Lerp(dockS.fov, engL.fov, k)); }, 21),
                    ("左引擎镜头绕进气口慢转 ±8°（模拟头部 / 自由视角，60 帧/秒 120 帧）", i => { float a = 8f * Mathf.Sin(i / 119f * Mathf.PI * 2f); var p = center + Quaternion.AngleAxis(a, Vector3.up) * (engL.pose.position - center); return (new Pose(p, Quaternion.LookRotation(center - p)), engL.fov); }, 120),
                    ("近看镜头慢推 10 cm（60 帧/秒 90 帧）", i => { var d = (center - closeS.pose.position).normalized; var p = closeS.pose.position + d * 0.10f * i / 89f; return (new Pose(p, Quaternion.LookRotation(center - p)), closeS.fov); }, 90),
                };
                var ad = cam.GetUniversalAdditionalCameraData();
                var aa0 = ad.antialiasing;
                var fibers = flow.ClogLayers.First(r => r.GetComponent<MeshFilter>().sharedMesh.name.Contains("Fibers"));
                flick.AppendLine("| 运动 | 条件 | 进气口区域像素 | 纤维覆盖的像素比例 | 纤维像素上的闪烁能量（时间二阶差分，×1000） | 相对“去掉纤维” |");
                flick.AppendLine("|---|---|---|---|---|---|");
                const int G = 96;
                foreach (var (name, at, frames) in paths)
                {
                    var conds = new (string label, bool fib, AntialiasingMode aa)[] { ("去掉细纤维（基准）", false, AntialiasingMode.None), ("有纤维，不抗锯齿（当前设置）", true, AntialiasingMode.None), ("有纤维，FXAA", true, AntialiasingMode.FastApproximateAntialiasing), ("有纤维，SMAA 高", true, AntialiasingMode.SubpixelMorphologicalAntiAliasing) };
                    var seqs = new List<List<float[]>>(); int px = 0;
                    foreach (var (label, fib, aa) in conds)
                    {
                        fibers.enabled = fib; ad.antialiasing = aa; ad.antialiasingQuality = AntialiasingQuality.High;
                        var seq = new List<float[]>();
                        for (int i = 0; i < frames; i++)
                        {
                            var (pose, fov) = at(i);
                            var t = Render(pose, fov);
                            var reg = Regions(pose, fov)["intake"]; px = reg.width * reg.height;
                            var g = new float[G * G];
                            for (int y = 0; y < G; y++) for (int x = 0; x < G; x++) g[y * G + x] = L(t.GetPixelBilinear((reg.xMin + (x + 0.5f) * reg.width / (float)G) / W, (reg.yMin + (y + 0.5f) * reg.height / (float)G) / H));
                            seq.Add(g);
                            if (fib && aa == AntialiasingMode.None && i % Mathf.Max(1, frames / 5) == 0) SaveCrops($"{layout}_flicker_{name.Substring(0, 4)}_f{i:000}", reg, t);
                            Destroy(t);
                        }
                        seqs.Add(seq);
                    }
                    // 纤维像素：同一帧、同一机位，有纤维和没纤维的画面亮度差 > 0.03 的格子
                    var mask = new bool[frames][];
                    int cover = 0;
                    for (int i = 0; i < frames; i++)
                    {
                        mask[i] = new bool[G * G];
                        for (int k = 0; k < G * G; k++) if (Mathf.Abs(seqs[1][i][k] - seqs[0][i][k]) > 0.03f) { mask[i][k] = true; cover++; }
                    }
                    float baseE = 0f;
                    for (int c = 0; c < conds.Length; c++)
                    {
                        double e = 0; int n = 0;
                        for (int i = 1; i + 1 < frames; i++)
                            for (int k = 0; k < G * G; k++)
                                if (mask[i][k] || mask[i - 1][k] || mask[i + 1][k]) { e += Mathf.Abs(seqs[c][i + 1][k] - 2f * seqs[c][i][k] + seqs[c][i - 1][k]); n++; }
                        float E = (float)(e / Math.Max(1, n)) * 1000f;
                        if (c == 0) baseE = E;
                        flick.AppendLine($"| {name} | {conds[c].label} | {px} | {cover / (float)(frames * G * G):P1} | {E:F1} | {(baseE > 0 ? (E / baseE).ToString("F2") + "×" : "—")} |");
                    }
                }
                fibers.enabled = true; ad.antialiasing = aa0;
            }

            // ---------------------------------------------------------------- 渲染开销

            readonly StringBuilder cost = new StringBuilder();

            IEnumerator Cost(string state)
            {
                var kit = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.enabled && AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>()?.sharedMesh).StartsWith("Assets/BorderRepair/Art/Unit07FaultKit/")).ToList();
                var orig = flow.OriginalBearingRenderer;
                var rig = flow.Rig;
                cost.AppendLine($"状态 {state}：显示中的故障美术件 {kit.Count} 个（{string.Join("、", kit.Select(k => k.name).Distinct())}）");
                cost.AppendLine();
                cost.AppendLine("| 镜头 | 条件 | 三角面 | Draw Call | Batches | SetPass | 单次渲染耗时（ms：连续渲染 200 次、最后同步一次，取 3 轮中位；含 CPU 提交 + GPU，编辑器内） |");
                cost.AppendLine("|---|---|---|---|---|---|---|");
                foreach (var id in new[] { FirstOrderCameraRig.EngineL, FirstOrderCameraRig.Bench, FirstOrderCameraRig.Overview, FirstOrderCameraRig.Compare })
                {
                    rig.Go(id, true);
                    foreach (var (label, kitOn, fixOn) in new[] { ("美术件关、调整关（接入前）", false, false), ("美术件开、调整关（接入后，修改前）", true, false), ("美术件开、调整开（修改后）", true, true) })
                    {
                        foreach (var r in kit) r.enabled = kitOn;
                        if (orig != null) orig.enabled = !kitOn;   // 接入前：RobotV4 原轴承显示（此时在托盘里）
                        if (fix != null) fix.Apply(fixOn);
                        for (int i = 0; i < 5; i++) yield return null;
                        int tri = 0, dc = 0, bat = 0, sp = 0;
                        for (int i = 0; i < 10; i++) { yield return new WaitForEndOfFrame(); tri = Math.Max(tri, UnityStats.triangles); dc = Math.Max(dc, UnityStats.drawCalls); bat = Math.Max(bat, UnityStats.batches); sp = Math.Max(sp, UnityStats.setPassCalls); }
                        var s = rig.Get(id);
                        var times = new List<double>();
                        for (int rep = 0; rep < 3; rep++)
                        {
                            var sw = Stopwatch.StartNew();
                            RenderOnly(new Pose(s.pose.position, s.pose.rotation), s.fov, 200);
                            sw.Stop(); times.Add(sw.Elapsed.TotalMilliseconds / 200.0);
                        }
                        times.Sort();
                        cost.AppendLine($"| {FirstOrderCameraRig.Labels[id]} | {label} | {tri:N0} | {dc} | {bat} | {sp} | {times[1]:F2} |");
                    }
                }
                foreach (var r in kit) r.enabled = true;
                if (orig != null) orig.enabled = false;
                if (fix != null) fix.Apply(true);
                cost.AppendLine();
                cost.AppendLine("注：渲染耗时是编辑器里离屏渲染的吞吐量（不读回画面，只在最后读 1 个像素同步 GPU），只用来比较“开 / 关”的差别，不等于游戏帧耗时；三角面 / Draw Call 取 Game 视图统计（含阴影等所有通道）。");
            }

            // ---------------------------------------------------------------- 汇总

            void Analyse()
            {
                md.AppendLine("## 1. 红色故障高亮");
                md.AppendLine();
                md.AppendLine(red.Count == 0 ? "- 各状态逐个检查所有渲染器的材质属性块：**没有任何红色 _BaseColor 覆盖**（红色高亮在接入故障美术包时已从流程里删掉，这里复核仍是关的）。" : "- **发现红色覆盖**：" + string.Join("、", red));
                md.AppendLine();
                Pair("2. 进气口：堵塞 vs 清理后", "S1_clogged", "S2_cleaned", "intake");
                Pair("3. 轴承位：磨损件 vs 新件（上盖拆下）", "S3_worn_in_seat", "S5_new_in_seat", "seat");
                BenchCompare();
                Label();
                Dark();
                md.AppendLine("## 7. 细纤维运动闪烁（状态 S1，调整后的灯光）");
                md.AppendLine();
                md.Append(flick);
                md.AppendLine();
                md.AppendLine("闪烁能量 = 进气口区域重采样到 96×96，只在“纤维像素”（同一帧有纤维和没纤维亮度差 > 0.03 的格子，及其前后一帧）上，算相邻三帧亮度的二阶差分绝对值平均：平滑运动贡献很小，像素来回跳动（闪烁）贡献大。“相对去掉纤维”> 1 的部分就是纤维带来的闪烁。");
                md.AppendLine();
                md.AppendLine("## 8. 渲染开销");
                md.AppendLine();
                md.Append(cost);
            }

            // 按“明显变了的像素”有多少个来分（1600×900 画面）：≥ 2000 px（约 45×45）明显；400–2000 px（约 20×20 到 45×45）能看出、要留意；100–400 px 只是很小一块；< 100 px 看不出
            string Cls(RectInt r, float frac)
            {
                float n = r.width * r.height * frac;
                return n >= 2000 ? $"明显（约 {n:F0} px）" : n >= 400 ? $"能看出，要留意（约 {n:F0} px）" : n >= 100 ? $"很小一块（约 {n:F0} px）" : $"看不出（约 {n:F0} px）";
            }

            void Pair(string title, string sa, string sb, string region)
            {
                md.AppendLine($"## {title}");
                md.AppendLine();
                md.AppendLine("| 镜头 | 目标在画面里（px） | 调整前：平均差 / 明显变化像素 | 调整后：平均差 / 明显变化像素 | 判定（调整后） |");
                md.AppendLine("|---|---|---|---|---|");
                foreach (var key in shots.Keys.Where(k => k.StartsWith(sa + "|") && k.EndsWith("|after")))
                {
                    var id = key.Split('|')[1];
                    string K(string s, string v) => $"{s}|{id}|{v}";
                    if (!shots.ContainsKey(K(sb, "after"))) continue;
                    var r = regions[K(sa, "after")][region];
                    var d0 = Diff(shots[K(sa, "before")], shots[K(sb, "before")], r);
                    var d1 = Diff(shots[K(sa, "after")], shots[K(sb, "after")], r);
                    md.AppendLine($"| {id} | {r.width}×{r.height} | {d0.meanDiff:F3} / {d0.frac:P0} | {d1.meanDiff:F3} / {d1.frac:P0} | {Cls(r, d1.frac)} |");
                    SaveCrops($"{layout}_{region}_{id}_[{sa}-before][{sb}-before][{sa}-after][{sb}-after]", r, shots[K(sa, "before")], shots[K(sb, "before")], shots[K(sa, "after")], shots[K(sb, "after")]);
                }
                md.AppendLine();
                md.AppendLine("平均差 = 区域内两种状态像素 RGB 最大通道差的平均；明显变化像素 = 差 > 0.08 的像素比例。区域是目标包围盒在画面上的矩形（含少量背景）。判定按“明显变化的像素个数”（区域面积 × 比例）：≥ 2000 px 明显；400–2000 px 能看出、要留意；100–400 px 很小一块；< 100 px 看不出。并排裁图见 `Crops/`。");
                md.AppendLine();
            }

            void BenchCompare()
            {
                md.AppendLine("## 4. 工作台上的旧件（托盘）与新件（轴承盒）——同一画面里比");
                md.AppendLine();
                md.AppendLine("| 镜头 | 旧件 px / 新件 px | 调整前 旧件：亮度 / 暖色偏 / 斑驳度 | 调整前 新件 | 调整后 旧件 | 调整后 新件 |");
                md.AppendLine("|---|---|---|---|---|---|");
                foreach (var key in shots.Keys.Where(k => k.StartsWith("S4_bench_old_new|") && k.EndsWith("|after")))
                {
                    var id = key.Split('|')[1];
                    var rg = regions[key];
                    string S(string v, string reg) { var s = Stats(shots[$"S4_bench_old_new|{id}|{v}"], rg[reg]); return $"{s.mean:F2} / {s.chroma:+0.000;-0.000} / {s.std:F3}"; }
                    md.AppendLine($"| {id} | {rg["tray"].width}×{rg["tray"].height} / {rg["box"].width}×{rg["box"].height} | {S("before", "tray")} | {S("before", "box")} | {S("after", "tray")} | {S("after", "box")} |");
                    SaveCrops($"{layout}_benchOld_{id}_[before][after]", rg["tray"], shots[$"S4_bench_old_new|{id}|before"], shots[$"S4_bench_old_new|{id}|after"]);
                    SaveCrops($"{layout}_benchNew_{id}_[before][after]", rg["box"], shots[$"S4_bench_old_new|{id}|before"], shots[$"S4_bench_old_new|{id}|after"]);
                }
                md.AppendLine();
                md.AppendLine("亮度 = 区域平均亮度（0–1，显示值）；暖色偏 = 平均 (R − B)，锈斑会让它变大；斑驳度 = 亮度标准差，磨痕 / 碎屑会让它变大。");
                md.AppendLine();
            }

            void Label()
            {
                md.AppendLine("## 5. 上盖内侧保养标记（状态 S3：上盖翻面放在操作垫上）");
                md.AppendLine();
                md.AppendLine("| 镜头 | 标记在画面里（px） | 调整前：纸面亮度（中位）/ 过曝像素（≥ 0.97）/ 字与纸的亮度差（中位 − 最暗 2%） | 调整后 |");
                md.AppendLine("|---|---|---|---|");
                foreach (var key in shots.Keys.Where(k => k.StartsWith("S3_worn_in_seat|") && k.EndsWith("|after")))
                {
                    var id = key.Split('|')[1];
                    var r = regions[key]["label"];
                    if (r.width < 2) continue;
                    var b = Stats(shots[$"S3_worn_in_seat|{id}|before"], r); var a = Stats(shots[key], r);
                    md.AppendLine($"| {id} | {r.width}×{r.height} | {b.p50:F2} / {b.clip:P0} / {b.p50 - b.p2:F2} | {a.p50:F2} / {a.clip:P0} / {a.p50 - a.p2:F2} |");
                    SaveCrops($"{layout}_label_{id}_[before][after]", r, shots[$"S3_worn_in_seat|{id}|before"], shots[key]);
                }
                md.AppendLine();
                md.AppendLine("标记 46×30 mm、四行字，字高约 4 mm：画面里标记高度 ≥ 120 px 时字大约 ≥ 12 px，才算能读；更小只能看出“有张标签”。");
                md.AppendLine();
            }

            void Dark()
            {
                md.AppendLine("## 6. 轴承区亮度（上盖拆下后）");
                md.AppendLine();
                md.AppendLine("| 状态 | 镜头 | 轴承位在画面里（px） | 调整前：平均亮度 / 暗部（≤ 0.04） | 调整后 |");
                md.AppendLine("|---|---|---|---|---|");
                foreach (var st in new[] { "S3_worn_in_seat", "S5_new_in_seat" })
                    foreach (var key in shots.Keys.Where(k => k.StartsWith(st + "|") && k.EndsWith("|after")))
                    {
                        var id = key.Split('|')[1];
                        if (id.Contains("Record") || id.Contains("Bench") || id.Contains("label")) continue;
                        var r = regions[$"S3_worn_in_seat|{id}|after"]["seat"];   // 轴承位区域按磨损件在位时算（S5 时原轴承已在托盘里）
                        var b = Stats(shots[$"{st}|{id}|before"], r); var a = Stats(shots[key], r);
                        md.AppendLine($"| {st} | {id} | {r.width}×{r.height} | {b.mean:F2} / {b.dark:P0} | {a.mean:F2} / {a.dark:P0} |");
                    }
                md.AppendLine();
            }
        }
    }
}
