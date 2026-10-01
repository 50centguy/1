using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.FirstOrder.EditorTools
{
    /// <summary>
    /// 程序验收（单独运行，带界面的编辑器 Play 模式；不要 -batchmode）：
    /// 打开首单测试场景，用 FirstOrderAcceptanceDriver 按玩家的点法走完整单，每一步截图（相机渲染到贴图，不读屏幕），
    /// 并在开始和结束时把故障美术包开 / 关各渲染一次，记下 Game 视图的三角面、Draw Call 增量；
    /// 写出 Docs/Integration/Unit07FirstOrder/acceptance_report.md（每一步的实际对象路径、预期 / 实际、成功 / 失败、零件去向、渲染开销）。
    /// 命令行：Unity.exe -projectPath &lt;项目&gt; -executeMethod BorderRepair.FirstOrder.EditorTools.FirstOrderAcceptanceRunner.Begin
    /// 退出码：0 全部通过，3 有失败步骤，4 没跑完。
    /// </summary>
    [InitializeOnLoad]
    public static class FirstOrderAcceptanceRunner
    {
        const string KeyActive = "FirstOrderAcceptance.Active";
        const string KeyExit = "FirstOrderAcceptance.Exit";
        static string OutDir => Path.GetFullPath(FirstOrderAudit.OutDir);

        static FirstOrderAcceptanceRunner() { EditorApplication.playModeStateChanged += OnPlayMode; }

        [MenuItem("Border Repair/Unit07 First Order/Run Program Acceptance")]
        public static void Begin()
        {
            Directory.CreateDirectory(Path.Combine(OutDir, "Screenshots"));
            foreach (var f in Directory.GetFiles(Path.Combine(OutDir, "Screenshots"), "A*.png")) File.Delete(f);
            SessionState.SetBool(KeyActive, true);
            SessionState.SetInt(KeyExit, 4);
            EditorSceneManager.OpenScene(FirstOrderSceneBuilder.ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (!SessionState.GetBool(KeyActive, false)) return;
            if (s == PlayModeStateChange.EnteredPlayMode)
            {
                var host = new GameObject("FirstOrderAcceptanceHost").AddComponent<Host>();
                host.outDir = OutDir;
            }
            if (s == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(KeyActive, false);
                if (!Application.isBatchMode && Environment.GetCommandLineArgs().Contains("-executeMethod"))
                    EditorApplication.Exit(SessionState.GetInt(KeyExit, 4));
            }
        }

        class Host : MonoBehaviour
        {
            public string outDir;
            readonly List<string> console = new List<string>();

            IEnumerator Start()
            {
                Application.logMessageReceived += (m, st, type) => { if (type != LogType.Log) console.Add($"{type}: {m.Split('\n')[0]}"); };
                for (int i = 0; i < 10; i++) yield return null;
                var flow = Object.FindFirstObjectByType<FirstOrderFlow>();
                var input = Object.FindFirstObjectByType<FirstOrderInput>();
                var cam = flow.Rig.Cam;
                var rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
                IEnumerator Shot(string name)
                {
                    yield return null;
                    var prev = cam.targetTexture;
                    cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev;
                    var a = RenderTexture.active; RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                    RenderTexture.active = a;
                    File.WriteAllBytes(Path.Combine(outDir, "Screenshots", Safe(name) + ".png"), tex.EncodeToPNG());
                    Destroy(tex);
                }
                var driver = new FirstOrderAcceptanceDriver(flow, input, Shot) { Timeout = 90f };   // 编辑器窗口在后台时 Play 模式会降帧，搬运动作按真实时间可能超过默认的 20 s
                // 渲染开销：美术包开 / 关各测一次（关 = 接入前的样子：原轴承渲染器打开、美术件全部隐藏；占位圆柱已不在场景里，按静态面数单独说明）
                var cost = new List<string>();
                yield return MeasureKitCost(flow, cost, "开始（七号悬停，堵塞在进气口，新轴承在轴承盒上）");
                yield return Shot("A00_start");
                yield return driver.RunFullOrder();
                yield return Shot("A99_end_dock");
                flow.Rig.Go(FirstOrderCameraRig.Bench, true);
                yield return Shot("A99_end_bench_parts");
                yield return MeasureKitCost(flow, cost, "结束（新轴承在原位，旧轴承在托盘，进气口已清理）");
                WriteReport(driver, flow, cam, cost);
                SessionState.SetInt(KeyExit, driver.AllPassed && !console.Any(c => c.StartsWith("Error") || c.StartsWith("Exception")) ? 0 : 3);
                rt.Release();
                EditorApplication.ExitPlaymode();
            }

            /// <summary>
            /// 在几个固定镜头下读 Game 视图的渲染统计（UnityStats，和 Game 视图 Stats 面板同一来源，不含 Scene 视图），美术包开 / 关各取 10 帧的最大值。
            /// </summary>
            IEnumerator MeasureKitCost(FirstOrderFlow flow, List<string> rows, string when)
            {
                var kit = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                                .Where(r => r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null && AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh).StartsWith("Assets/BorderRepair/Art/Unit07FaultKit/")).ToList();
                var shown = kit.Where(r => r.enabled).ToList();
                var orig = flow.OriginalBearingRenderer;
                foreach (var shot in new[] { FirstOrderCameraRig.EngineL, FirstOrderCameraRig.Bench, FirstOrderCameraRig.Overview })
                {
                    flow.Rig.Go(shot, true);
                    (int tris, int draws, int batches, int setpass) Sample() => (UnityStats.triangles, UnityStats.drawCalls, UnityStats.batches, UnityStats.setPassCalls);
                    IEnumerator Read(Action<(int, int, int, int)> done)
                    {
                        for (int i = 0; i < 5; i++) yield return null;
                        var best = (0, 0, 0, 0);
                        for (int i = 0; i < 10; i++)
                        {
                            yield return new WaitForEndOfFrame();
                            var s = Sample();
                            best = (Math.Max(best.Item1, s.tris), Math.Max(best.Item2, s.draws), Math.Max(best.Item3, s.batches), Math.Max(best.Item4, s.setpass));
                        }
                        done(best);
                    }
                    (int, int, int, int) on = default, off = default;
                    yield return Read(v => on = v);
                    foreach (var r in shown) r.enabled = false;
                    if (orig != null) orig.enabled = true;
                    yield return Read(v => off = v);
                    foreach (var r in shown) r.enabled = true;
                    if (orig != null) orig.enabled = false;
                    rows.Add($"| {when} | {FirstOrderCameraRig.Labels[shot]} | {shown.Count} | {off.Item1:N0} → {on.Item1:N0} | **{on.Item1 - off.Item1:+#,0;-#,0;0}** | {off.Item2} → {on.Item2} | **{on.Item2 - off.Item2:+0;-0;0}** | {off.Item3} → {on.Item3} | {off.Item4} → {on.Item4} |");
                }
            }

            static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());

            void WriteReport(FirstOrderAcceptanceDriver d, FirstOrderFlow flow, Camera cam, List<string> cost)
            {
                var sb = new StringBuilder();
                sb.AppendLine("# 七号首单 · 可玩原型（接入故障美术包）· 程序验收报告");
                sb.AppendLine();
                sb.AppendLine($"- 运行：{DateTime.Now:yyyy-MM-dd HH:mm}，Unity {Application.unityVersion}，带界面的编辑器 Play 模式，Game 视图 {cam.pixelWidth}×{cam.pixelHeight}，{SystemInfo.graphicsDeviceName}");
                sb.AppendLine($"- 场景：`{FirstOrderSceneBuilder.ScenePath}`");
                sb.AppendLine("- 点击方式：每一步切到该步镜头，在目标点选范围内找一个**真实点选规则（含遮挡）会选中它**的屏幕点，再从这个屏幕点点击；找不到就判失败，不直接调用接口。“查看”步骤只切镜头、核对画面条件并截图。");
                sb.AppendLine($"- 结果：**{d.Records.Count(r => r.pass)}/{d.Records.Count} 步通过**，{(d.AllPassed ? "全部通过" : "有失败步骤")}；被拒绝的操作共 {flow.RejectedCount} 次（都是有意穿插的错误操作）；控制台警告 / 错误 {console.Count} 条{(console.Count > 0 ? "：" + string.Join(" | ", console.Distinct().Take(10)) : "")}");
                sb.AppendLine($"- 离座复测（占位判定）：{(flow.RetestPassed ? "通过" : "未通过")}——{flow.RetestDetail}");
                sb.AppendLine("- **这是程序点击走查，不是真人鼠标试玩**；没有验证 VR 和目标硬件。截图是相机渲染到贴图，不读屏幕。");
                sb.AppendLine();
                sb.AppendLine("| # | 步骤 | 镜头 | 目标 | 实际对象路径 | 预期 | 实际 | 结果 | 反馈 | 之后的步骤 / 维修座状态 |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
                foreach (var r in d.Records)
                {
                    string actual = r.target == "（等待）" ? (r.accepted ? "成立" : "超时") : r.target == "（查看）" ? (r.accepted ? "成立" : "不成立") : !r.clickable ? "点不到" : r.accepted ? "接受" : "拒绝";
                    sb.AppendLine($"| {r.index} | {r.label} | {r.camera} | {r.target} | `{r.targetPath}` | {r.expect} | {actual} | {(r.pass ? "✅ 通过" : "❌ 失败")} | {r.message?.Replace("|", "/")} | {r.stepAfter} / {r.dockState} |");
                }
                sb.AppendLine();
                sb.AppendLine("## 零件最终去向");
                sb.AppendLine();
                sb.AppendLine("| 零件 | 实际对象路径 | 最终位置 | 世界坐标 |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var p in flow.TrackedParts)
                    sb.AppendLine($"| {(p.isPlaceholder ? "【占位】" : "")}{p.displayName} | `{p.realPath}` | {p.LocationDetail} | {p.transform.position:F3} |");
                sb.AppendLine();
                sb.AppendLine("## 渲染开销：故障美术包开 / 关（Game 视图实测）");
                sb.AppendLine();
                sb.AppendLine("“关”= 接入前的样子：美术件全部隐藏、RobotV4 原轴承渲染器打开。数值取 Game 视图 Stats 同一来源（UnityStats），每种状态 10 帧取最大值；含阴影、深度等所有渲染通道，所以一个网格可能计入多次。");
                sb.AppendLine();
                sb.AppendLine("| 时刻 | 镜头 | 显示中的美术件 | 三角面 关 → 开 | 三角面增量 | Draw Call 关 → 开 | Draw Call 增量 | Batches 关 → 开 | SetPass 关 → 开 |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
                foreach (var row in cost) sb.AppendLine(row);
                sb.AppendLine();
                sb.AppendLine("## 占位与未实现");
                sb.AppendLine();
                foreach (var s in FirstOrderFlow.Placeholders) sb.AppendLine("- " + s);
                File.WriteAllText(Path.Combine(outDir, "acceptance_report.md"), sb.ToString(), new UTF8Encoding(false));
            }
        }
    }
}
