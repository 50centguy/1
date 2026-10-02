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

namespace BorderRepair.FirstOrder.EditorTools.DustFiberQA
{
    /// <summary>
    /// 进气积尘明度 / 色相与细纤维闪烁试验（带界面的编辑器 Play 模式，布局 B 测试副本，qa/unit07-fault-art-scene 的灯光调整开着）。
    /// 状态由验收驱动推进到“已断电检查、进气口还堵着”，之后：
    /// - sweep 模式：积尘颜色候选（材质属性块乘底色，不改材质资源）× 纤维候选（换 sharedMesh）——量左引擎镜头 / 站位 / 总览 / 近看下“堵塞 vs 清理后”的画面差，
    ///   以及三种镜头运动下纤维像素的闪烁，出运动帧对比图；
    /// - final 模式：修改前（原底色 + 原纤维网格）和修改后（当前资源）同机位、同曝光截图、闪烁、三角面 / Draw Call / 耗时。
    /// 截图只渲染 Unity 游戏镜头。后处理曝光固定（没有自动曝光），所以前后同曝光。
    /// </summary>
    [InitializeOnLoad]
    public static class DustFiberRunner
    {
        const string KeyMode = "DustFiber.Mode";
        static string OutDir => Path.GetFullPath(FiberVariants.OutDir);

        public static readonly (string id, string label, Color tint)[] Tints =
        {
            ("D0", "原色（底色 × 1）", Color.white),
            ("D1", "略暗、偏灰褐", new Color(0.80f, 0.74f, 0.66f)),
            ("D2", "暗灰褐（油泥）", new Color(0.64f, 0.58f, 0.50f)),
            ("D4", "很暗的褐", new Color(0.48f, 0.42f, 0.36f)),
            ("D5", "浅冷灰（棉絮）", new Color(0.90f, 0.92f, 0.96f)),
            ("D6", "中等灰褐", new Color(0.72f, 0.66f, 0.58f)),
            ("D7", "暖褐（旧油泥，偏黄不偏红）", new Color(0.70f, 0.56f, 0.40f)),
        };

        static DustFiberRunner() { EditorApplication.playModeStateChanged += OnPlayMode; }

        public static void Sweep() => Begin("sweep");
        public static void Final() => Begin("final");

        static void Begin(string mode)
        {
            Directory.CreateDirectory(Path.Combine(OutDir, mode == "sweep" ? "Sweep" : "Final"));
            foreach (var f in Directory.GetFiles(Path.Combine(OutDir, mode == "sweep" ? "Sweep" : "Final"), "*.png")) File.Delete(f);
            SessionState.SetString(KeyMode, mode);
            EditorSceneManager.OpenScene(LayoutABScenes.SceneB, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            var mode = SessionState.GetString(KeyMode, "");
            if (mode.Length == 0) return;
            if (s == PlayModeStateChange.EnteredPlayMode) new GameObject("DustFiberHost").AddComponent<Host>().mode = mode;
            if (s == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetString(KeyMode, "");
                if (!Application.isBatchMode && Environment.GetCommandLineArgs().Contains("-executeMethod")) EditorApplication.Exit(0);
            }
        }

        class Host : MonoBehaviour
        {
            public string mode;
            FirstOrderFlow flow; Camera cam; RenderTexture rt; FaultArtLookFix fix;
            Renderer[] clog; MeshFilter fiberMf; Mesh fiberOriginal;
            readonly StringBuilder md = new StringBuilder();
            readonly List<string> console = new List<string>();
            const int W = 1600, H = 900;
            string Sub => mode == "sweep" ? "Sweep" : "Final";

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
                clog = flow.ClogLayers.ToArray();
                fiberMf = clog.Select(r => r.GetComponent<MeshFilter>()).First(m => m.sharedMesh.name.Contains("Fibers"));
                fiberOriginal = fiberMf.sharedMesh;
                var vol = Object.FindFirstObjectByType<Volume>();
                string exposure = vol != null && vol.sharedProfile.TryGet<ColorAdjustments>(out var ca) ? $"后处理曝光固定 {ca.postExposure.value:+0.00}（{(vol.sharedProfile.Has<ColorAdjustments>() ? "ColorAdjustments" : "")}），没有自动曝光" : "没有后处理曝光";
                md.AppendLine($"# 进气积尘 / 细纤维 {(mode == "sweep" ? "试验" : "修改前后对比")}（布局 B）");
                md.AppendLine();
                md.AppendLine($"- {DateTime.Now:yyyy-MM-dd HH:mm}，Unity {Application.unityVersion}，{SystemInfo.graphicsDeviceName}，{W}×{H}，MSAA {rt.antiAliasing}x，游戏镜头抗锯齿 {cam.GetUniversalAdditionalCameraData().antialiasing}；{exposure}");
                md.AppendLine($"- 灯光：qa/unit07-fault-art-scene 的观感调整（反射探针、两盏补光、FXAA）{(fix != null && fix.Applied ? "开着" : "**没开**")}");
                md.AppendLine("- 状态由验收驱动推进（只摆状态，不算点击验收）。“清理后”用与流程清理相同的方式得到：把堵塞的四层渲染器关掉。");
                md.AppendLine();

                var driver = new FirstOrderAcceptanceDriver(flow, input) { Timeout = 90f };
                var it = driver.RunFullOrder();
                bool done = false;
                while (!done && it.MoveNext())
                {
                    yield return it.Current;
                    if (flow.Step == FoStep.ReleaseLatches && !flow.ClogCleared)
                    {
                        done = true;
                        yield return null; yield return null;
                        if (mode == "sweep") yield return SweepAll(); else yield return FinalCompare();
                    }
                }
                if (mode == "final")
                {
                    // 走完整单（流程回归的一部分），再在 S4 测工作台一带的开销
                    while (it.MoveNext())
                    {
                        yield return it.Current;
                        if (flow.Step == FoStep.FetchNewBearing && !costS4) { costS4 = true; yield return Cost("S4（已清理，旧件在托盘、新件在轴承盒）", false); }
                    }
                    md.AppendLine();
                    md.AppendLine($"- 整单推进 {driver.Records.Count(r => r.pass)}/{driver.Records.Count} 步通过，复测 {(flow.RetestPassed ? "通过" : "未通过")}");
                }
                md.AppendLine($"- 控制台警告 / 错误：{console.Count} 条{(console.Count > 0 ? "：" + string.Join(" | ", console.Distinct().Take(8)) : "")}");
                File.WriteAllText(Path.Combine(OutDir, mode == "sweep" ? "sweep.md" : "final.md"), md.ToString(), new UTF8Encoding(false));
                rt.Release();
                EditorApplication.ExitPlaymode();
            }
            bool costS4;

            // ---------------------------------------------------------------- 工具

            void Tint(Color c)
            {
                var b = new MaterialPropertyBlock();
                foreach (var r in clog)
                {
                    r.GetPropertyBlock(b);
                    if (c == Color.white && mode == "sweep") b.Clear(); else b.SetColor("_BaseColor", c);
                    r.SetPropertyBlock(b);
                }
            }
            void ClearTint() { foreach (var r in clog) r.SetPropertyBlock(null); }
            void ClogOn(bool on) { foreach (var r in clog) r.enabled = on; }

            RenderTexture rtRef;
            /// <summary>reference = true：8× MSAA、不加 FXAA 的参考渲染（近似“没有锯齿”的画面），用来把锯齿闪烁和正常的运动变化分开。</summary>
            Texture2D Render(Pose pose, float fov, bool reference = false)
            {
                var rig = flow.Rig; bool rigOn = rig.enabled; rig.enabled = false;
                var (p0, r0, f0) = (cam.transform.position, cam.transform.rotation, cam.fieldOfView);
                cam.transform.SetPositionAndRotation(pose.position, pose.rotation); cam.fieldOfView = fov;
                var ad = cam.GetUniversalAdditionalCameraData(); var aa = ad.antialiasing;
                if (reference) { if (rtRef == null) rtRef = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 }; ad.antialiasing = AntialiasingMode.None; }
                var target = reference ? rtRef : rt;
                cam.targetTexture = target; cam.Render(); cam.targetTexture = null;
                ad.antialiasing = aa;
                cam.transform.SetPositionAndRotation(p0, r0); cam.fieldOfView = f0; rig.enabled = rigOn;
                var a = RenderTexture.active; RenderTexture.active = target;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = a;
                return tex;
            }

            RectInt Region(Pose pose, float fov)
            {
                var rig = flow.Rig; bool rigOn = rig.enabled; rig.enabled = false;
                var (p0, r0, f0) = (cam.transform.position, cam.transform.rotation, cam.fieldOfView);
                cam.transform.SetPositionAndRotation(pose.position, pose.rotation); cam.fieldOfView = fov;
                var b = flow.Cover.members.First(m => m.name.Contains("IntakeGuard")).GetComponent<Renderer>().bounds;
                float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1));
                    var vp = cam.WorldToViewportPoint(c);
                    x0 = Mathf.Min(x0, vp.x * W); x1 = Mathf.Max(x1, vp.x * W); y0 = Mathf.Min(y0, vp.y * H); y1 = Mathf.Max(y1, vp.y * H);
                }
                cam.transform.SetPositionAndRotation(p0, r0); cam.fieldOfView = f0; rig.enabled = rigOn;
                int ix0 = Mathf.Clamp((int)x0, 0, W), iy0 = Mathf.Clamp((int)y0, 0, H);
                return new RectInt(ix0, iy0, Mathf.Clamp((int)x1, 0, W) - ix0, Mathf.Clamp((int)y1, 0, H) - iy0);
            }

            static float L(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

            (int changed, float meanDiff, float dustL, float coverL, float deMean, int deBig) Detect(Texture2D on, Texture2D off, RectInt r)
            {
                int n = 0, big = 0, deBig = 0; float sum = 0f, dl = 0f, cl = 0f, deSum = 0f; int dn = 0;
                for (int y = r.yMin; y < r.yMax; y++)
                    for (int x = r.xMin; x < r.xMax; x++)
                    {
                        var p = on.GetPixel(x, y); var q = off.GetPixel(x, y);
                        float d = Mathf.Max(Mathf.Abs(p.r - q.r), Mathf.Abs(p.g - q.g), Mathf.Abs(p.b - q.b));
                        sum += d; n++;
                        if (d > 0.08f) { big++; dl += L(p); dn++; float de = DeltaE(p, q); deSum += de; if (de > 10f) deBig++; }
                    }
                // 进气口外圈一圈上盖的亮度（判断积尘和上盖拉不拉得开）
                int cn = 0;
                for (int y = r.yMin - 12; y < r.yMax + 12; y += 3)
                    for (int x = r.xMin - 12; x < r.xMax + 12; x += 3)
                    {
                        if (x < 0 || y < 0 || x >= W || y >= H || (x >= r.xMin && x < r.xMax && y >= r.yMin && y < r.yMax)) continue;
                        cl += L(on.GetPixel(x, y)); cn++;
                    }
                return (big, n == 0 ? 0 : sum / n, dn == 0 ? 0 : dl / dn, cn == 0 ? 0 : cl / cn, dn == 0 ? 0 : deSum / dn, deBig);
            }

            /// <summary>
            /// 进气口开口里（区域四边各收 25%）的观感：堵塞前后亮度起伏（标准差）、平均亮度、色调（R − B）。
            /// 干净的进气口是暗的护栅条加透出来的亮风道壁，起伏大；堵住以后被一层东西盖平，起伏变小、色调变了——这就是“看起来堵了”。
            /// </summary>
            (float stdClean, float stdClog, float lClean, float lClog, float hueClean, float hueClog) Opening(Texture2D on, Texture2D off, RectInt r)
            {
                var inner = new RectInt(r.xMin + r.width / 4, r.yMin + r.height / 4, r.width / 2, r.height / 2);
                (float std, float mean, float hue) S(Texture2D t)
                {
                    var ls = new List<float>(); float h = 0f;
                    for (int y = inner.yMin; y < inner.yMax; y++) for (int x = inner.xMin; x < inner.xMax; x++) { var c = t.GetPixel(x, y); ls.Add(L(c)); h += c.r - c.b; }
                    if (ls.Count == 0) return (0, 0, 0);
                    float m = ls.Average();
                    return (Mathf.Sqrt(ls.Sum(v => (v - m) * (v - m)) / ls.Count), m, h / ls.Count);
                }
                var a = S(off); var b = S(on);
                return (a.std, b.std, a.mean, b.mean, a.hue, b.hue);
            }

            void Save(Texture2D t, string name) => File.WriteAllBytes(Path.Combine(OutDir, Sub, name.Replace("|", "-") + ".png"), t.EncodeToPNG());

            /// <summary>把若干张图的同一区域（放大 5/3 倍取景）拼成网格：rows × cols。</summary>
            void Grid(string name, List<List<(Texture2D t, RectInt r)>> rows)
            {
                int oh = 200, ow = 260, gap = 4;
                int cols = rows.Max(r => r.Count);
                var outT = new Texture2D(cols * (ow + gap), rows.Count * (oh + gap), TextureFormat.RGB24, false);
                var px = Enumerable.Repeat(new Color(0.08f, 0.08f, 0.08f), outT.width * outT.height).ToArray();
                for (int ri = 0; ri < rows.Count; ri++)
                    for (int ci = 0; ci < rows[ri].Count; ci++)
                    {
                        var (t, r) = rows[ri][ci];
                        float cx = r.center.x, cy = r.center.y, hw = Mathf.Max(r.width, r.height * ow / (float)oh) * 0.85f, hh = hw * oh / ow;
                        int oy = (rows.Count - 1 - ri) * (oh + gap);
                        for (int y = 0; y < oh; y++)
                            for (int x = 0; x < ow; x++)
                                px[(oy + y) * outT.width + ci * (ow + gap) + x] = t.GetPixelBilinear((cx - hw + (x + 0.5f) * 2 * hw / ow) / W, (cy - hh + (y + 0.5f) * 2 * hh / oh) / H);
                    }
                outT.SetPixels(px); outT.Apply();
                Save(outT, name);
                Destroy(outT);
            }

            (string, Pose, float) Cam(string id) { var s = flow.Rig.Get(id); return ($"cam{Array.IndexOf(FirstOrderCameraRig.Order, id) + 1}_{id}", new Pose(s.pose.position, s.pose.rotation), s.fov); }
            (string, Pose, float) Eye()
            {
                var (stand, _) = LayoutABScenes.Stand("B");
                var eye = stand + Vector3.up * LayoutPlanner.EyeHeight;
                return ("eye_intake", new Pose(eye, Quaternion.LookRotation(flow.ClogLayers.Last().bounds.center - eye)), 60f);
            }

            List<(string name, Func<int, (Pose, float)> at, int frames)> Paths()
            {
                var center = flow.ClogLayers.Last().bounds.center;
                var engL = flow.Rig.Get(FirstOrderCameraRig.EngineL); var dockS = flow.Rig.Get(FirstOrderCameraRig.Dock); var closeS = flow.Rig.Get(FirstOrderCameraRig.EngineClose);
                return new List<(string, Func<int, (Pose, float)>, int)>
                {
                    ("镜头切换 维修座→左引擎", i => { float k = Mathf.SmoothStep(0, 1, i / 20f); return (new Pose(Vector3.Lerp(dockS.pose.position, engL.pose.position, k), Quaternion.Slerp(dockS.pose.rotation, engL.pose.rotation, k)), Mathf.Lerp(dockS.fov, engL.fov, k)); }, 21),
                    ("左引擎镜头绕进气口慢转 ±8°", i => { float a = 8f * Mathf.Sin(i / 119f * Mathf.PI * 2f); var p = center + Quaternion.AngleAxis(a, Vector3.up) * (engL.pose.position - center); return (new Pose(p, Quaternion.LookRotation(center - p)), engL.fov); }, 120),
                    ("近看镜头慢推 10 cm", i => { var d = (center - closeS.pose.position).normalized; var p = closeS.pose.position + d * 0.10f * i / 89f; return (new Pose(p, Quaternion.LookRotation(center - p)), closeS.fov); }, 90),
                };
            }

            const int G = 96;
            /// <summary>沿一条运动路径渲染，每帧把进气口区域重采样到 G×G 亮度。keep 里的帧号同时保留整张图（做运动帧对比）。</summary>
            (List<float[]> seq, Dictionary<int, (Texture2D, RectInt)> kept) RunPath(Func<int, (Pose, float)> at, int frames, int[] keep, bool reference = false)
            {
                var seq = new List<float[]>(); var kept = new Dictionary<int, (Texture2D, RectInt)>();
                for (int i = 0; i < frames; i++)
                {
                    var (pose, fov) = at(i);
                    var t = Render(pose, fov, reference);
                    var reg = Region(pose, fov);
                    var g = new float[G * G];
                    for (int y = 0; y < G; y++) for (int x = 0; x < G; x++) g[y * G + x] = L(t.GetPixelBilinear((reg.xMin + (x + 0.5f) * reg.width / (float)G) / W, (reg.yMin + (y + 0.5f) * reg.height / (float)G) / H));
                    seq.Add(g);
                    if (keep != null && keep.Contains(i)) kept[i] = (t, reg); else Destroy(t);
                }
                return (seq, kept);
            }

            /// <summary>两种显示颜色（sRGB）在 CIE Lab 里的色差 ΔE76：≈ 2 刚能分辨，> 10 一眼看得出。</summary>
            static float DeltaE(Color a, Color b)
            {
                Vector3 Lab(Color c)
                {
                    float Lin(float u) => u <= 0.04045f ? u / 12.92f : Mathf.Pow((u + 0.055f) / 1.055f, 2.4f);
                    float r = Lin(c.r), g = Lin(c.g), bl = Lin(c.b);
                    float x = (0.4124f * r + 0.3576f * g + 0.1805f * bl) / 0.95047f, y = 0.2126f * r + 0.7152f * g + 0.0722f * bl, z = (0.0193f * r + 0.1192f * g + 0.9505f * bl) / 1.08883f;
                    float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;
                    return new Vector3(116f * F(y) - 16f, 500f * (F(x) - F(y)), 200f * (F(y) - F(z)));
                }
                return (Lab(a) - Lab(b)).magnitude;
            }

            /// <summary>
            /// 锯齿闪烁：每帧“游戏渲染 − 8× MSAA 参考渲染”的误差图，误差随时间的二阶差分（只在纤维像素上）。
            /// 模糊、颜色偏差这类稳定的误差不算，只有一帧一帧忽隐忽现的锯齿才算。
            /// </summary>
            static float FlickErr(List<float[]> game, List<float[]> reference, List<float[]> baseSeq)
            {
                int frames = game.Count; double e = 0; int n = 0;
                var mask = new bool[frames][];
                for (int i = 0; i < frames; i++) { mask[i] = new bool[G * G]; for (int k = 0; k < G * G; k++) mask[i][k] = Mathf.Abs(game[i][k] - baseSeq[i][k]) > 0.03f; }
                for (int i = 1; i + 1 < frames; i++)
                    for (int k = 0; k < G * G; k++)
                        if (mask[i][k] || mask[i - 1][k] || mask[i + 1][k])
                        {
                            float a = game[i + 1][k] - reference[i + 1][k], b = game[i][k] - reference[i][k], c = game[i - 1][k] - reference[i - 1][k];
                            e += Mathf.Abs(a - 2f * b + c); n++;
                        }
                return (float)(e / Math.Max(1, n)) * 1000f;
            }

            /// <summary>纤维像素上的闪烁能量：mask = 这个纤维版本和“没纤维”同一帧亮度差 > 0.03 的格子（及前后一帧）。</summary>
            static (float energy, float cover) Flick(List<float[]> s, List<float[]> baseSeq, List<float[]> maskSeq)
            {
                int frames = s.Count; double e = 0; int n = 0, cov = 0;
                var mask = new bool[frames][];
                for (int i = 0; i < frames; i++) { mask[i] = new bool[G * G]; for (int k = 0; k < G * G; k++) if (Mathf.Abs(maskSeq[i][k] - baseSeq[i][k]) > 0.03f) { mask[i][k] = true; cov++; } }
                for (int i = 1; i + 1 < frames; i++)
                    for (int k = 0; k < G * G; k++)
                        if (mask[i][k] || mask[i - 1][k] || mask[i + 1][k]) { e += Mathf.Abs(s[i + 1][k] - 2f * s[i][k] + s[i - 1][k]); n++; }
                return ((float)(e / Math.Max(1, n)) * 1000f, cov / (float)(frames * G * G));
            }

            // ---------------------------------------------------------------- 试验

            IEnumerator SweepAll()
            {
                var cams = new[] { Cam(FirstOrderCameraRig.EngineL), Eye(), Cam(FirstOrderCameraRig.Overview), Cam(FirstOrderCameraRig.EngineClose) };
                var variants = FiberVariants.Variants.Select(v => (v.id, v.label, mesh: AssetDatabase.LoadAssetAtPath<Mesh>($"{FiberVariants.Dir}/Fibers_{v.id}.asset"))).ToList();
                // 清理后的参照（与积尘颜色、纤维无关）
                ClogOn(false);
                var off = cams.ToDictionary(c => c.Item1, c => Render(c.Item2, c.Item3));
                ClogOn(true);
                md.AppendLine("## 1. 进气积尘颜色：左引擎常用镜头下能不能察觉堵塞（纤维 F0 原样）");
                md.AppendLine();
                md.AppendLine("| 积尘颜色 | 乘底色 | 镜头 | 进气口区域 px | 明显变化像素（差 > 0.08） | 其中色差 ΔE > 10（一眼看得出）的像素 / 平均 ΔE | 积尘亮度 / 周围上盖亮度 | 开口起伏 干净 → 堵塞（盖平程度） | 开口亮度 干净 → 堵塞 | 开口色调 R−B 干净 → 堵塞 |");
                md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
                fiberMf.sharedMesh = variants[0].mesh;
                foreach (var (tid, tlabel, tint) in Tints)
                {
                    Tint(tint);
                    yield return null;
                    var row = new List<(Texture2D, RectInt)>();
                    foreach (var (cid, pose, fov) in cams)
                    {
                        var on = Render(pose, fov); var r = Region(pose, fov);
                        var d = Detect(on, off[cid], r);
                        var o = Opening(on, off[cid], r);
                        md.AppendLine($"| {tid} {tlabel} | ({tint.r:F2}, {tint.g:F2}, {tint.b:F2}) | {cid} | {r.width}×{r.height} | **{d.changed}** | **{d.deBig}** / {d.deMean:F1} | {d.dustL:F2} / {d.coverL:F2} | " +
                                      $"{o.stdClean:F3} → {o.stdClog:F3}（{(1f - o.stdClog / Mathf.Max(1e-4f, o.stdClean)):P0}） | {o.lClean:F2} → {o.lClog:F2} | {o.hueClean:+0.000;-0.000} → {o.hueClog:+0.000;-0.000} |");
                        Save(on, $"{tid}_F0_{cid}");
                        row.Add((on, r));
                    }
                    Grid($"grid_dust_{tid}_[cam2|eye|cam5|cam9]", new List<List<(Texture2D, RectInt)>> { row });
                    foreach (var (t, _) in row) Destroy(t);
                }
                // 清理后的同机位参照也留一组
                var offRow = cams.Select(c => (off[c.Item1], Region(c.Item2, c.Item3))).ToList();
                Grid("grid_cleaned_[cam2|eye|cam5|cam9]", new List<List<(Texture2D, RectInt)>> { offRow });

                md.AppendLine();
                md.AppendLine("## 2. 细纤维：减少 / 加粗 × 积尘颜色，左引擎镜头察觉度与运动闪烁");
                md.AppendLine();
                md.AppendLine("闪烁指标只在“纤维像素”（这个版本和“没纤维”同一帧亮度差 > 0.03 的格子）上算相邻三帧亮度二阶差分；");
                md.AppendLine("“锯齿闪烁”= 每帧（游戏渲染 MSAA 1x + FXAA − 同一帧 8× MSAA 参考渲染）的误差图，在纤维像素上随时间的二阶差分 ×1000：稳定的模糊 / 偏色不算，只有一帧一帧忽隐忽现的锯齿算；0 = 和参考一样稳。");
                md.AppendLine("“闪烁总量”= 纤维占比 × 锯齿闪烁，表示整块进气口里有多少忽闪。");
                md.AppendLine();
                md.AppendLine("| 积尘颜色 | 纤维 | 三角面 | 左引擎镜头 明显变化 / ΔE > 10 像素 | 站位 明显变化 / ΔE > 10 像素 | 镜头切换：占比 / 锯齿闪烁 / 总量 | 绕进气口慢转：占比 / 锯齿闪烁 / 总量 | 近看慢推：占比 / 锯齿闪烁 / 总量 |");
                md.AppendLine("|---|---|---|---|---|---|---|---|");
                var paths = Paths();
                int[] keepFrames = { 0, 20, 40, 60, 80, 100 };
                foreach (var (tid, tlabel, tint) in Tints)
                {
                    Tint(tint);
                    // 去掉纤维的基准（每条路径一次）
                    fiberMf.GetComponent<Renderer>().enabled = false;
                    yield return null;
                    var baseSeqs = paths.Select(p => RunPath(p.at, p.frames, null).seq).ToList();
                    fiberMf.GetComponent<Renderer>().enabled = true;
                    var gridRows = new List<List<(Texture2D, RectInt)>>();
                    foreach (var (vid, vlabel, mesh) in variants)
                    {
                        fiberMf.sharedMesh = mesh;
                        yield return null;
                        var det = cams.Take(2).Select(c => Detect(Render(c.Item2, c.Item3), off[c.Item1], Region(c.Item2, c.Item3))).ToList();
                        var cells = new List<string>();
                        for (int pi = 0; pi < paths.Count; pi++)
                        {
                            var (seq, kept) = RunPath(paths[pi].at, paths[pi].frames, pi == 1 ? keepFrames : null);
                            var refSeq = RunPath(paths[pi].at, paths[pi].frames, null, reference: true).seq;
                            var (_, cover) = Flick(seq, baseSeqs[pi], seq);
                            float fe = FlickErr(seq, refSeq, baseSeqs[pi]);
                            cells.Add($"{cover:P1} / {fe:F1} / {cover * fe:F2}");
                            if (pi == 1) gridRows.Add(keepFrames.Select(f => kept[f]).ToList());
                        }
                        md.AppendLine($"| {tid} | {vid} {vlabel} | {mesh.GetIndexCount(0) / 3} | {det[0].changed} / {det[0].deBig} | {det[1].changed} / {det[1].deBig} | {string.Join(" | ", cells)} |");
                    }
                    Grid($"motion_{tid}_rows[F0..F4]_cols[frame0,20,40,60,80,100]", gridRows);
                    foreach (var row in gridRows) foreach (var (t, _) in row) Destroy(t);
                }
                fiberMf.sharedMesh = fiberOriginal;
                ClearTint();
                md.AppendLine();
                md.AppendLine("运动帧对比图 `Sweep/motion_*.png`：每张一个积尘颜色，行 = 纤维 F0–F4，列 = 左引擎镜头绕进气口慢转的第 0、20、40、60、80、100 帧（60 帧/秒）。");
            }

            // ---------------------------------------------------------------- 修改前后

            Color BeforeTint => Color.white;   // 修改前：材质底色 (1, 1, 1)
            Mesh BeforeFibers => AssetDatabase.LoadAssetAtPath<Mesh>($"{FiberVariants.Dir}/Fibers_F0.asset");   // 修改前的纤维网格（原 FBX 里导出来的副本）

            void ApplyBefore(bool before)
            {
                if (before) { Tint(BeforeTint); fiberMf.sharedMesh = BeforeFibers; }
                else { ClearTint(); fiberMf.sharedMesh = fiberOriginal; }
            }

            IEnumerator FinalCompare()
            {
                var mat = clog[0].sharedMaterial;
                md.AppendLine($"- 修改后 = 当前资源：`{AssetDatabase.GetAssetPath(mat)}` 底色 {mat.GetColor("_BaseColor")}；纤维网格 `{AssetDatabase.GetAssetPath(fiberOriginal)}` / {fiberOriginal.name}（{fiberOriginal.GetIndexCount(0) / 3} 三角面）");
                md.AppendLine($"- 修改前 = 用材质属性块把底色设回 (1, 1, 1)，纤维换回原网格副本 `Fibers_F0`（{BeforeFibers.GetIndexCount(0) / 3} 三角面）");
                md.AppendLine();
                var cams = new[] { Cam(FirstOrderCameraRig.Dock), Cam(FirstOrderCameraRig.EngineL), Eye(), Cam(FirstOrderCameraRig.Overview), Cam(FirstOrderCameraRig.EngineClose) };
                ClogOn(false);
                yield return null;
                var off = cams.ToDictionary(c => c.Item1, c => Render(c.Item2, c.Item3));
                foreach (var c in cams) Save(off[c.Item1], $"cleaned_{c.Item1}");
                ClogOn(true);
                md.AppendLine("## 1. 同机位、同曝光：堵塞能不能察觉");
                md.AppendLine();
                md.AppendLine("| 镜头 | 进气口区域 px | 修改前：明显变化像素 / ΔE > 10 像素 / 平均 ΔE / 积尘亮度 | 修改后 |");
                md.AppendLine("|---|---|---|---|");
                var rows = new List<List<(Texture2D, RectInt)>> { new List<(Texture2D, RectInt)>(), new List<(Texture2D, RectInt)>(), new List<(Texture2D, RectInt)>() };
                foreach (var (cid, pose, fov) in cams)
                {
                    var r = Region(pose, fov);
                    ApplyBefore(true); yield return null;
                    var b = Render(pose, fov); var db = Detect(b, off[cid], r);
                    ApplyBefore(false); yield return null;
                    var a = Render(pose, fov); var da = Detect(a, off[cid], r);
                    Save(b, $"before_{cid}"); Save(a, $"after_{cid}");
                    md.AppendLine($"| {cid} | {r.width}×{r.height} | {db.changed} / {db.deBig} / {db.deMean:F1} / {db.dustL:F2} | **{da.changed} / {da.deBig} / {da.deMean:F1}** / {da.dustL:F2} |");
                    rows[0].Add((b, r)); rows[1].Add((a, r)); rows[2].Add((off[cid], r));
                }
                Grid("compare_rows[before,after,cleaned]_cols[cam1,cam2,eye,cam5,cam9]", rows);
                md.AppendLine();
                md.AppendLine("并排图 `Final/compare_*.png`：行 = 修改前 / 修改后 / 清理后，列 = 维修座、左引擎、玩家站位、总览、左引擎近看；整张截图 `Final/before_*.png`、`after_*.png`、`cleaned_*.png`。");
                md.AppendLine();

                md.AppendLine("## 2. 运动闪烁（纤维像素上）");
                md.AppendLine();
                md.AppendLine("| 运动 | 修改前：纤维占比 / 锯齿闪烁 / 闪烁总量 | 修改后 |");
                md.AppendLine("|---|---|---|");
                var paths = Paths();
                int[] keepFrames = { 0, 20, 40, 60, 80, 100 };
                var motionRows = new List<List<(Texture2D, RectInt)>>();
                foreach (var (pname, at, frames) in paths)
                {
                    var cells = new List<string>();
                    foreach (var before in new[] { true, false })
                    {
                        ApplyBefore(before);
                        fiberMf.GetComponent<Renderer>().enabled = false; yield return null;
                        var bs = RunPath(at, frames, null).seq;
                        fiberMf.GetComponent<Renderer>().enabled = true; yield return null;
                        bool keepIt = pname.StartsWith("左引擎");
                        var (s, kept) = RunPath(at, frames, keepIt ? keepFrames : null);
                        var rs = RunPath(at, frames, null, reference: true).seq;
                        var (_, cover) = Flick(s, bs, s);
                        float fe = FlickErr(s, rs, bs);
                        cells.Add($"{cover:P1} / {fe:F1} / {cover * fe:F2}");
                        if (keepIt) motionRows.Add(keepFrames.Select(f => kept[f]).ToList());
                    }
                    md.AppendLine($"| {pname} | {cells[0]} | **{cells[1]}** |");
                }
                Grid("motion_rows[before,after]_cols[frame0,20,40,60,80,100]", motionRows);
                foreach (var row in motionRows) foreach (var (t, _) in row) Destroy(t);
                md.AppendLine();
                md.AppendLine("运动帧对比 `Final/motion_*.png`：上行修改前、下行修改后，左引擎镜头绕进气口慢转第 0–100 帧（每 20 帧一张）。");
                md.AppendLine();
                ApplyBefore(false);
                yield return Cost("S1（进气口堵着，8 个美术件都显示）", true);
            }

            IEnumerator Cost(string state, bool beforeAfter)
            {
                var kit = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.GetComponent<MeshFilter>() != null && AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh).StartsWith("Assets/BorderRepair/Art/Unit07FaultKit/")).ToList();
                var orig = flow.OriginalBearingRenderer;
                md.AppendLine($"## 三角面 / Draw Call / 耗时 · {state}");
                md.AppendLine();
                md.AppendLine("| 镜头 | 条件 | 三角面 | Draw Call | Batches | SetPass | 单次渲染耗时 ms（连续 200 次、最后同步一次，3 轮中位，编辑器内） |");
                md.AppendLine("|---|---|---|---|---|---|---|");
                var conds = new List<(string, Action)> { ("美术件关（接入前）", () => { foreach (var r in kit) r.enabled = false; if (orig != null) orig.enabled = true; }) };
                if (beforeAfter) conds.Add(("美术件开 · 修改前（原积尘色 + 原纤维）", () => { foreach (var r in kit) r.enabled = true; if (orig != null) orig.enabled = false; ApplyBefore(true); }));
                conds.Add(("美术件开 · 修改后（当前资源）", () => { foreach (var r in kit) r.enabled = true; if (orig != null) orig.enabled = false; ApplyBefore(false); }));
                foreach (var id in new[] { FirstOrderCameraRig.EngineL, FirstOrderCameraRig.Bench, FirstOrderCameraRig.Overview, FirstOrderCameraRig.EngineClose })
                {
                    flow.Rig.Go(id, true);
                    foreach (var (label, apply) in conds)
                    {
                        apply();
                        for (int i = 0; i < 5; i++) yield return null;
                        int tri = 0, dc = 0, bat = 0, sp = 0;
                        for (int i = 0; i < 10; i++) { yield return new WaitForEndOfFrame(); tri = Math.Max(tri, UnityStats.triangles); dc = Math.Max(dc, UnityStats.drawCalls); bat = Math.Max(bat, UnityStats.batches); sp = Math.Max(sp, UnityStats.setPassCalls); }
                        var s = flow.Rig.Get(id);
                        var times = new List<double>();
                        for (int rep = 0; rep < 3; rep++)
                        {
                            var rig = flow.Rig; rig.enabled = false;
                            cam.transform.SetPositionAndRotation(s.pose.position, s.pose.rotation); cam.fieldOfView = s.fov;
                            cam.targetTexture = rt;
                            var sw = Stopwatch.StartNew();
                            for (int i = 0; i < 200; i++) cam.Render();
                            var a = RenderTexture.active; RenderTexture.active = rt;
                            var one = new Texture2D(1, 1, TextureFormat.RGB24, false); one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); one.Apply(); Destroy(one);
                            RenderTexture.active = a;
                            sw.Stop(); times.Add(sw.Elapsed.TotalMilliseconds / 200.0);
                            cam.targetTexture = null; rig.enabled = true;
                        }
                        times.Sort();
                        md.AppendLine($"| {FirstOrderCameraRig.Labels[id]} | {label} | {tri:N0} | {dc} | {bat} | {sp} | {times[1]:F2} |");
                    }
                }
                foreach (var r in kit) r.enabled = true;
                if (orig != null) orig.enabled = false;
                ApplyBefore(false);
                md.AppendLine();
            }
        }
    }
}
