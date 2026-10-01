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
    /// 打开首单测试场景，用 FirstOrderAcceptanceDriver 按玩家的点法走完整单，每一步截图，
    /// 写出 Docs/Integration/Unit07FirstOrder/acceptance_report.md（每一步的实际对象路径、预期 / 实际、成功 / 失败、零件去向）。
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
                var driver = new FirstOrderAcceptanceDriver(flow, input, Shot);
                yield return Shot("A00_start");
                yield return driver.RunFullOrder();
                yield return Shot("A99_end_dock");
                flow.Rig.Go(FirstOrderCameraRig.Bench, true);
                yield return Shot("A99_end_bench_parts");
                WriteReport(driver, flow, cam);
                SessionState.SetInt(KeyExit, driver.AllPassed && !console.Any(c => c.StartsWith("Error") || c.StartsWith("Exception")) ? 0 : 3);
                rt.Release();
                EditorApplication.ExitPlaymode();
            }

            static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());

            void WriteReport(FirstOrderAcceptanceDriver d, FirstOrderFlow flow, Camera cam)
            {
                var sb = new StringBuilder();
                sb.AppendLine("# 七号首单 · 可玩原型 · 程序验收报告");
                sb.AppendLine();
                sb.AppendLine($"- 运行：{DateTime.Now:yyyy-MM-dd HH:mm}，Unity {Application.unityVersion}，带界面的编辑器 Play 模式，Game 视图 {cam.pixelWidth}×{cam.pixelHeight}，{SystemInfo.graphicsDeviceName}");
                sb.AppendLine($"- 场景：`{FirstOrderSceneBuilder.ScenePath}`");
                sb.AppendLine("- 点击方式：每一步切到该步镜头，在目标点选范围内找一个**真实点选规则（含遮挡）会选中它**的屏幕点，再从这个屏幕点点击；找不到就判失败，不直接调用接口。");
                sb.AppendLine($"- 结果：**{d.Records.Count(r => r.pass)}/{d.Records.Count} 步通过**，{(d.AllPassed ? "全部通过" : "有失败步骤")}；被拒绝的操作共 {flow.RejectedCount} 次（都是有意穿插的错误操作）；控制台警告 / 错误 {console.Count} 条{(console.Count > 0 ? "：" + string.Join(" | ", console.Distinct().Take(10)) : "")}");
                sb.AppendLine($"- 离座复测（占位判定）：{(flow.RetestPassed ? "通过" : "未通过")}——{flow.RetestDetail}");
                sb.AppendLine("- 这是程序走查，不是真人操作；没有验证 VR 和目标硬件。");
                sb.AppendLine();
                sb.AppendLine("| # | 步骤 | 镜头 | 目标 | 实际对象路径 | 预期 | 实际 | 结果 | 反馈 | 之后的步骤 / 维修座状态 |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
                foreach (var r in d.Records)
                {
                    string actual = r.target == "（等待）" ? (r.accepted ? "成立" : "超时") : !r.clickable ? "点不到" : r.accepted ? "接受" : "拒绝";
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
                sb.AppendLine("## 占位与未实现");
                sb.AppendLine();
                foreach (var s in FirstOrderFlow.Placeholders) sb.AppendLine("- " + s);
                File.WriteAllText(Path.Combine(outDir, "acceptance_report.md"), sb.ToString(), new UTF8Encoding(false));
            }
        }
    }
}
