using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BorderRepair.FirstOrder.EditorTools.LayoutAB
{
    /// <summary>
    /// 布局 A/B 实测（带界面的编辑器 Play 模式，依次跑 A、B 两个场景副本）：
    /// - 用虚拟鼠标设备经 Input System 走完整单：每一步把鼠标移到目标上、等悬停、按下、松开，由游戏自己的 FirstOrderInput.Update 读鼠标并点击。
    ///   这是程序生成的鼠标事件，不是真人试玩。
    /// - 每次点击前，从该布局的玩家站位（眼高 1.60 m）量：转向角、按真实点选规则（含遮挡）能点到目标的比例；以及该步镜头下能点到的比例。
    /// - 同机位截图：两个布局用完全相同的一组相机位姿（俯视、房间角落、两个站位各看几个方向），在开始、上盖翻面后、新旧轴承对比时、离座复测时各拍一组。
    ///   截图只渲染 Unity 相机，不读屏幕。
    /// - 逐帧运动监视（单帧位移 / 转角、搬运途中凸包穿入）与新旧轴承显隐。
    /// 输出 Docs/Integration/Unit07FirstOrder/LayoutAB/run_A.md、run_B.md、layout_A.svg、layout_B.svg、Screenshots/。
    /// 命令行：Unity.exe -projectPath &lt;项目&gt; -executeMethod BorderRepair.FirstOrder.EditorTools.LayoutAB.LayoutABRunner.Begin
    /// </summary>
    [InitializeOnLoad]
    public static class LayoutABRunner
    {
        const string KeyQueue = "LayoutAB.Queue", KeyExit = "LayoutAB.Exit";
        static string OutDir => Path.GetFullPath(LayoutABScenes.OutDir);

        static LayoutABRunner() { EditorApplication.playModeStateChanged += OnPlayMode; }

        [MenuItem("Border Repair/Unit07 First Order/Layout A-B/Run Mouse Walkthrough (A then B)")]
        public static void Begin()
        {
            Directory.CreateDirectory(Path.Combine(OutDir, "Screenshots"));
            foreach (var f in Directory.GetFiles(Path.Combine(OutDir, "Screenshots"), "*.png")) File.Delete(f);
            SessionState.SetString(KeyQueue, "A,B");
            SessionState.SetInt(KeyExit, 0);
            Next();
        }

        static void Next()
        {
            var q = SessionState.GetString(KeyQueue, "");
            if (q.Length == 0)
            {
                if (!Application.isBatchMode && Environment.GetCommandLineArgs().Contains("-executeMethod")) EditorApplication.Exit(SessionState.GetInt(KeyExit, 4));
                return;
            }
            var id = q.Split(',')[0];
            EditorSceneManager.OpenScene(id == "A" ? LayoutABScenes.SceneA : LayoutABScenes.SceneB, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            var q = SessionState.GetString(KeyQueue, "");
            if (q.Length == 0) return;
            var id = q.Split(',')[0];
            if (s == PlayModeStateChange.EnteredPlayMode)
                new GameObject("LayoutABHost").AddComponent<Host>().layout = id;
            if (s == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetString(KeyQueue, string.Join(",", q.Split(',').Skip(1)));
                EditorApplication.delayCall += Next;
            }
        }

        static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());

        class Host : MonoBehaviour
        {
            public string layout;
            readonly List<string> console = new List<string>();
            RenderTexture rt;
            Camera shotCam;

            IEnumerator Start()
            {
                Application.logMessageReceived += (m, st, type) => { if (type != LogType.Log) console.Add($"{type}: {m.Split('\n')[0]}"); };
                for (int i = 0; i < 10; i++) yield return null;
                var flow = Object.FindFirstObjectByType<FirstOrderFlow>();
                var input = Object.FindFirstObjectByType<FirstOrderInput>();
                input.enabled = true;
                var mouse = FirstOrderAcceptanceDriver.CreateVirtualMouse(out var cleanup);
                var mon = new GameObject("LayoutAB_Monitor").AddComponent<FirstOrderMotionMonitor>();
                mon.flow = flow;
                rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
                shotCam = new GameObject("LayoutAB_ShotCamera").AddComponent<Camera>();
                shotCam.CopyFrom(flow.Rig.Cam);
                shotCam.enabled = false;

                var own = LayoutABScenes.Stand(layout);
                var ownEye = own.pos + Vector3.up * LayoutPlanner.EyeHeight;
                var stepCams = new Dictionary<int, string>();
                var driver = new FirstOrderAcceptanceDriver(flow, input) { Timeout = 90f, VirtualMouse = mouse };
                driver.BeforeClick = (rec, target) =>
                {
                    if (target == null) return;
                    var c = Center(target);
                    rec.extra["yaw"] = LayoutPlanner.Yaw(ownEye, c).ToString("F1", CultureInfo.InvariantCulture);
                    rec.extra["dist"] = new Vector2(c.x - own.pos.x, c.z - own.pos.z).magnitude.ToString("F2", CultureInfo.InvariantCulture);
                    rec.extra["eyeVis"] = PickFraction(target, _ => ownEye).ToString("F2", CultureInfo.InvariantCulture);
                    var cam = flow.Rig.Cam;
                    rec.extra["camVis"] = PickFraction(target, w => cam.transform.position, cam).ToString("F2", CultureInfo.InvariantCulture);
                };

                // “查看”步骤：玩家看的地方（读翻面上盖的保养记录、对比新旧轴承、看故障原位）
                Vector3? Focus(string label) =>
                    label.Contains("保养记录") ? flow.CoverLabel.bounds.center :
                    label.Contains("新旧轴承对比") ? (flow.Bearing.WorldBounds().center + flow.NewBearing.WorldBounds().center) / 2f :
                    label.Contains("进气口") ? flow.ClogLayers.Last().bounds.center :
                    label.Contains("轴承") ? flow.Bearing.HomeWorldPose().position :
                    label.StartsWith("装回") ? flow.Cover.WorldBounds().center : (Vector3?)null;
                void Measure(FirstOrderAcceptanceDriver.Record rec, Vector3 c)
                {
                    rec.extra["yaw"] = LayoutPlanner.Yaw(ownEye, c).ToString("F1", CultureInfo.InvariantCulture);
                    rec.extra["dist"] = new Vector2(c.x - own.pos.x, c.z - own.pos.z).magnitude.ToString("F2", CultureInfo.InvariantCulture);
                }
                driver.OnView = rec => { if (Focus(rec.label) is Vector3 c) Measure(rec, c); };

                var shots = new List<string>();
                IEnumerator Moment(string tag)
                {
                    yield return null;
                    foreach (var (name, pose, fov, ortho) in LayoutABScenes.SharedShots(flow, tag))
                    {
                        Render($"{tag}_{name}", pose, fov, ortho, name == "overhead");
                        shots.Add($"{tag}_{name}");
                    }
                }

                yield return Moment("M1_start");
                var it = driver.RunFullOrder();
                bool m2 = false, m3 = false;
                while (it.MoveNext())
                {
                    yield return it.Current;
                    if (!m2 && flow.Step == FoStep.LocateBearing) { m2 = true; yield return Moment("M2_cover_on_mat"); }
                    if (!m3 && flow.Step == FoStep.FetchNewBearing) { m3 = true; yield return Moment("M3_compare"); }
                }
                // 离座复测：玩家看着七号离座悬停
                var retest = new FirstOrderAcceptanceDriver.Record { index = driver.Records.Count + 1, label = "离座复测：看七号悬停", camera = "-", target = "（查看）", expect = "画面条件成立", accepted = flow.RetestPassed, pass = flow.RetestPassed, clickable = true, message = flow.RetestDetail };
                Measure(retest, flow.Dock.RobotRoot.position + Vector3.up * 0.45f);
                driver.Records.Add(retest);
                yield return Moment("M4_retest");

                var turns = TurnSummary(driver.Records, out var turnRows);
                WriteRun(driver, flow, mon, turns, turnRows, shots);
                WriteSvg(flow, own, driver.Records);
                bool ok = driver.AllPassed && mon.travelHits.Count == 0 && mon.jumpViolations.Count == 0 && mon.bearingViolations.Count == 0 &&
                          !console.Any(c => c.StartsWith("Error") || c.StartsWith("Exception"));
                if (!ok) SessionState.SetInt(KeyExit, 3);
                cleanup();
                rt.Release();
                EditorApplication.ExitPlaymode();
            }

            static Vector3 Center(Component c) => c is FirstOrderPart p ? p.WorldBounds().center :
                c is FirstOrderDropZone z ? z.landing.position : c.GetComponentInChildren<Renderer>() != null ? c.GetComponentInChildren<Renderer>().bounds.center : c.transform.position;

            /// <summary>目标点选范围里 5×5×5 个点，从某个视点按真实点选规则（含遮挡）能选中它的比例。给了相机时只算在画面内的点。</summary>
            public static float PickFraction(Component target, Func<Vector3, Vector3> origin, Camera cam = null)
            {
                var cols = target.GetComponents<Collider>().Where(c => c.enabled).ToList();
                if (target is FirstOrderPart fp) cols.AddRange(fp.members.SelectMany(m => m.GetComponents<Collider>()).Where(c => c.enabled));
                Physics.SyncTransforms();
                int n = 0, hit = 0;
                foreach (var col in cols)
                {
                    var b = col.bounds;
                    for (int i = 0; i < 125; i++)
                    {
                        var f = new Vector3(i % 5, (i / 5) % 5, i / 25) / 4f;
                        var w = b.min + Vector3.Scale(b.size, Vector3.one * 0.5f + (f - Vector3.one * 0.5f) * 0.9f);
                        n++;
                        if (cam != null)
                        {
                            var sp = cam.WorldToViewportPoint(w);
                            if (sp.z <= 0 || sp.x < 0 || sp.x > 1 || sp.y < 0 || sp.y > 1) continue;
                        }
                        var o = origin(w);
                        if (FirstOrderInput.Pick(new Ray(o, w - o)) == target) hit++;
                    }
                }
                return n == 0 ? 0f : hit / (float)n;
            }

            void Render(string name, Pose pose, float fov, bool ortho, bool markers)
            {
                var temp = markers ? LayoutABScenes.OverheadMarkers() : new List<GameObject>();
                shotCam.transform.SetPositionAndRotation(pose.position, pose.rotation);
                shotCam.orthographic = ortho;
                if (ortho) { shotCam.orthographicSize = fov; shotCam.nearClipPlane = 0.05f; shotCam.farClipPlane = 5f; }
                else { shotCam.fieldOfView = fov; shotCam.nearClipPlane = 0.02f; shotCam.farClipPlane = 20f; }
                shotCam.targetTexture = rt; shotCam.Render(); shotCam.targetTexture = null;
                var a = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                RenderTexture.active = a;
                File.WriteAllBytes(Path.Combine(Path.GetFullPath(LayoutABScenes.OutDir), "Screenshots", $"{layout}_{Safe(name)}.png"), tex.EncodeToPNG());
                Destroy(tex);
                foreach (var g in temp) DestroyImmediate(g);
            }

            /// <summary>按正确动作的顺序（接受的点击 + 查看步骤）算站位处的转向：相邻两步方位角差。</summary>
            (int over90, int over60, float total, float max, int count) TurnSummary(List<FirstOrderAcceptanceDriver.Record> recs, out List<string> rows)
            {
                rows = new List<string>();
                float? prev = null; string prevLabel = null;
                int o90 = 0, o60 = 0, n = 0; float tot = 0f, mx = 0f;
                foreach (var r in recs.Where(r => r.accepted && (r.expect == "接受" || r.target == "（查看）") && r.extra.ContainsKey("yaw")))
                {
                    float y = float.Parse(r.extra["yaw"], CultureInfo.InvariantCulture);
                    if (prev != null)
                    {
                        float d = Mathf.Abs(Mathf.DeltaAngle(prev.Value, y));
                        tot += d; mx = Mathf.Max(mx, d); n++;
                        if (d > 90f) o90++;
                        if (d > 60f) o60++;
                        rows.Add($"| {prevLabel} → {r.label} | {d:F0}° | {(d > 90f ? "**大角度**" : d > 60f ? "较大" : "")} |");
                    }
                    prev = y; prevLabel = r.label;
                }
                return (o90, o60, tot, mx, n);
            }

            void WriteRun(FirstOrderAcceptanceDriver d, FirstOrderFlow flow, FirstOrderMotionMonitor mon, (int over90, int over60, float total, float max, int count) t, List<string> turnRows, List<string> shots)
            {
                var own = LayoutABScenes.Stand(layout);
                var sb = new StringBuilder();
                sb.AppendLine($"# 布局 {layout} · 鼠标设备走查记录");
                sb.AppendLine();
                sb.AppendLine($"- 运行：{DateTime.Now:yyyy-MM-dd HH:mm}，Unity {Application.unityVersion}，带界面的编辑器 Play 模式，Game 视图 {flow.Rig.Cam.pixelWidth}×{flow.Rig.Cam.pixelHeight}，{SystemInfo.graphicsDeviceName}");
                sb.AppendLine($"- 场景：`{(layout == "A" ? LayoutABScenes.SceneA : LayoutABScenes.SceneB)}`");
                sb.AppendLine("- 点击方式：**虚拟鼠标设备**——每一步把 Input System 的鼠标移到目标上、等两帧确认悬停、按下、松开，由游戏的 `FirstOrderInput.Update` 自己读鼠标、自己点。是程序生成的鼠标事件，**不是真人试玩**。");
                sb.AppendLine($"- 结果：**{d.Records.Count(r => r.pass)}/{d.Records.Count} 步通过**；悬停确认 {d.Records.Count(r => r.hovered)}/{d.Records.Count(r => r.screenPoint != null)}；被拒绝 {flow.RejectedCount} 次（有意穿插的错误操作）；控制台警告 / 错误 {console.Count} 条{(console.Count > 0 ? "：" + string.Join(" | ", console.Distinct().Take(8)) : "")}");
                sb.AppendLine($"- 离座复测（占位判定）：{(flow.RetestPassed ? "通过" : "未通过")}——{flow.RetestDetail}");
                sb.AppendLine($"- 玩家站位 ({own.pos.x:F2}, {own.pos.z:F2})，朝向 {own.yaw:F0}°（0° = 面朝工作台 −Z，正值向左），眼高 {LayoutPlanner.EyeHeight} m");
                sb.AppendLine($"- **站位处的转向**（按正确动作顺序，{t.count} 次视线切换）：大于 90° 的 **{t.over90}** 次，大于 60° 的 {t.over60} 次，总转角 {t.total:F0}°，单次最大 {t.max:F0}°");
                sb.AppendLine($"- 逐帧运动（{mon.frames} 帧）：单帧最大位移 {mon.maxStep * 1000:F1} mm（{mon.maxStepWhere}），最大转角 {mon.maxTurn:F1}°；瞬移 {mon.jumpViolations.Count} 处；搬运途中检查 {mon.travelFrames} 帧，凸包穿入 {mon.travelHits.Count} 处" +
                              (mon.travelHits.Count > 0 ? "：" + string.Join("；", mon.travelHits.Take(6)) : "") + $"；新旧轴承显隐违规 {mon.bearingViolations.Count} 条");
                sb.AppendLine($"- 搬运路线：{string.Join("；", mon.carries)}");
                sb.AppendLine($"- 没有碰撞、只能比包围盒、搬运时包围盒有交叠的物件：{(mon.aabbOnly.Count == 0 ? "无" : string.Join("、", mon.aabbOnly))}");
                sb.AppendLine();
                sb.AppendLine("## 每一步");
                sb.AppendLine();
                sb.AppendLine("| # | 步骤 | 镜头 | 目标 | 预期 | 实际 | 悬停 | 结果 | 站位方位角 | 站位水平距离 | 站位眼睛可点比例 | 本步镜头可点比例 | 反馈 |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
                foreach (var r in d.Records)
                {
                    string actual = r.target == "（等待）" ? (r.accepted ? "成立" : "超时") : r.target == "（查看）" ? (r.accepted ? "成立" : "不成立") : !r.clickable ? "点不到" : r.accepted ? "接受" : "拒绝";
                    string E(string k, string fmt = null) => r.extra.TryGetValue(k, out var v) ? (fmt == "%" ? float.Parse(v, CultureInfo.InvariantCulture).ToString("P0") : v) : "-";
                    sb.AppendLine($"| {r.index} | {r.label} | {r.camera} | {r.target} | {r.expect} | {actual} | {(r.screenPoint != null ? (r.hovered ? "是" : "否") : "-")} | {(r.pass ? "✅" : "❌")} | {E("yaw")}° | {E("dist")} m | {E("eyeVis", "%")} | {E("camVis", "%")} | {r.message?.Replace("|", "/")} |");
                }
                sb.AppendLine();
                sb.AppendLine("## 站位处的视线切换（正确动作顺序）");
                sb.AppendLine();
                sb.AppendLine("| 从 → 到 | 转角 | |");
                sb.AppendLine("|---|---|---|");
                foreach (var row in turnRows) sb.AppendLine(row);
                sb.AppendLine();
                sb.AppendLine("## 截图（同机位，只渲染 Unity 相机）");
                sb.AppendLine();
                foreach (var s in shots) sb.AppendLine($"- `Screenshots/{layout}_{Safe(s)}.png`");
                File.WriteAllText(Path.Combine(OutDir, $"run_{layout}.md"), sb.ToString(), new UTF8Encoding(false));
            }

            /// <summary>俯视示意图（SVG）：房间、工作台物件、维修架和七号的包围盒，玩家站位与朝向，按正确动作顺序的视线。</summary>
            void WriteSvg(FirstOrderFlow flow, (Vector3 pos, float yaw) own, List<FirstOrderAcceptanceDriver.Record> recs)
            {
                const float S = 220f, X0 = 1.75f, Z0 = 1.2f;   // 1 m = 220 px；左上角 = (x 1.75, z −1.2)：图上向右 = −X，向下 = +Z（站在敞开一侧看向工作台）
                float PX(float x) => (X0 - x) * S; float PZ(float z) => (z + Z0) * S;
                var sb = new StringBuilder();
                sb.AppendLine($"<svg xmlns='http://www.w3.org/2000/svg' width='{3.5f * S:F0}' height='{3.05f * S:F0}' font-family='sans-serif' font-size='13'>");
                sb.AppendLine($"<rect width='100%' height='100%' fill='#f4f1ea'/>");
                void Rect(Bounds b, string fill, string stroke, string title = null)
                {
                    sb.AppendLine($"<rect x='{PX(b.max.x):F1}' y='{PZ(b.min.z):F1}' width='{b.size.x * S:F1}' height='{b.size.z * S:F1}' fill='{fill}' stroke='{stroke}' stroke-width='1'>{(title != null ? $"<title>{title}</title>" : "")}</rect>");
                }
                var bench = GameObject.Find("WorkbenchArea");
                foreach (var r in bench.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy))
                {
                    if (r.name.StartsWith("Room_Floor")) Rect(r.bounds, "#e8e2d6", "#bbb");
                    else if (r.name.StartsWith("Room_Wall")) Rect(r.bounds, "#7a746a", "none", r.name);
                    else if (!r.name.StartsWith("Room_") && r.bounds.max.y > 0.05f && r.bounds.size.x < 2.5f) Rect(r.bounds, "rgba(120,100,70,0.25)", "rgba(90,70,40,0.5)", r.name);
                }
                foreach (var root in new[] { GameObject.Find("Unit07ServiceDock"), GameObject.Find("UNIT07_RobotV4_DockReady") })
                    foreach (var r in root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.bounds.size.magnitude > 0.03f))
                        Rect(r.bounds, root.name.StartsWith("UNIT07") ? "rgba(40,110,200,0.18)" : "rgba(200,120,30,0.30)", "none", r.name);
                // 视线
                var eye = own.pos + Vector3.up * LayoutPlanner.EyeHeight;
                foreach (var r in recs.Where(r => r.accepted && (r.expect == "接受" || r.target == "（查看）") && r.extra.ContainsKey("yaw")))
                {
                    float y = float.Parse(r.extra["yaw"], CultureInfo.InvariantCulture) * Mathf.Deg2Rad;
                    float dist = float.Parse(r.extra["dist"], CultureInfo.InvariantCulture);
                    var tgt = own.pos + new Vector3(Mathf.Sin(y), 0f, -Mathf.Cos(y)) * dist;
                    sb.AppendLine($"<line x1='{PX(own.pos.x):F1}' y1='{PZ(own.pos.z):F1}' x2='{PX(tgt.x):F1}' y2='{PZ(tgt.z):F1}' stroke='#c0392b' stroke-opacity='0.35' stroke-width='1.5'/>");
                    sb.AppendLine($"<circle cx='{PX(tgt.x):F1}' cy='{PZ(tgt.z):F1}' r='4' fill='#c0392b'><title>{r.label}</title></circle>");
                }
                sb.AppendLine($"<circle cx='{PX(own.pos.x):F1}' cy='{PZ(own.pos.z):F1}' r='{LayoutPlanner.BodyRadius * S:F1}' fill='rgba(30,150,90,0.25)' stroke='#1e965a' stroke-width='2'/>");
                float fy = own.yaw * Mathf.Deg2Rad;
                var tip = own.pos + new Vector3(Mathf.Sin(fy), 0f, -Mathf.Cos(fy)) * 0.55f;
                sb.AppendLine($"<line x1='{PX(own.pos.x):F1}' y1='{PZ(own.pos.z):F1}' x2='{PX(tip.x):F1}' y2='{PZ(tip.z):F1}' stroke='#1e965a' stroke-width='4'/>");
                sb.AppendLine($"<text x='10' y='20'>布局 {layout}：橙 = 维修架，蓝 = 七号，棕 = 工作台与房间物件，绿圈 = 玩家站位（半径 0.30 m）与朝向，红点 = 每个正确动作的目标。图上方 = 工作台 / 后墙，下方 = 敞开一侧。</text>");
                sb.AppendLine("</svg>");
                File.WriteAllText(Path.Combine(OutDir, $"layout_{layout}.svg"), sb.ToString(), new UTF8Encoding(false));
            }
        }
    }
}
