using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Dock;
using BorderRepair.RobotRepair;
using BorderRepair.Unit07WorkOrder;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 带界面的编辑器 Play 模式采集（第三阶段：工单 06 × 维修座）：按真实状态逐步推进完整往返（含一次悬停复测失败、重新停靠），
    /// 维修座部件用镜头射线点击，Game 视图截图（含维修座 HUD 和工单面板），记录每一步的结果和控制台警告 / 错误。
    /// 命令行（不要 -batchmode）：Unity.exe -projectPath &lt;项目&gt; -executeMethod BorderRepair.EditorTools.Unit07WorkOrder06PlayCapture.Begin
    /// 结果写到 Docs/Integration/Unit07WorkOrder06/。只读取场景，不保存场景、不改资源。
    /// </summary>
    [InitializeOnLoad]
    public static class Unit07WorkOrder06PlayCapture
    {
        const string KeyActive = "Unit07WorkOrder06PlayCapture.Active";
        const string KeyExit = "Unit07WorkOrder06PlayCapture.Exit";
        public const string ReportDir = "Docs/Integration/Unit07WorkOrder06";
        static string OutDir => Path.GetFullPath(ReportDir);

        static Unit07WorkOrder06PlayCapture() { EditorApplication.playModeStateChanged += OnPlayMode; }

        public static void Begin()
        {
            Directory.CreateDirectory(Path.Combine(OutDir, "Screenshots"));
            SessionState.SetBool(KeyActive, true);
            EditorSceneManager.OpenScene(Unit07WorkOrder06SceneBuilder.ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (!SessionState.GetBool(KeyActive, false)) return;
            if (s == PlayModeStateChange.EnteredPlayMode) Runner.Start(OutDir);
            if (s == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(KeyActive, false);
                EditorApplication.Exit(SessionState.GetInt(KeyExit, 4));
            }
        }

        static class Runner
        {
            const float StepTimeout = 20f;   // 编辑器失去焦点时 Play 可能变慢；超时就结束，不挂住
            static string outDir;
            static readonly StringBuilder log = new StringBuilder();
            static readonly List<string> console = new List<string>();
            static readonly List<(Func<bool> ready, Action act, string what)> steps = new List<(Func<bool>, Action, string)>();
            static int next;
            static float stepStart;
            static Unit07DockController dock;
            static Unit07DockInput input;
            static Unit07WorkOrder06Bridge bridge;
            static Unit07WorkOrder06Session wo;
            static Quaternion measureRotor;
            static float measureT;
            static int shots;

            static void Note(string s) { log.AppendLine(s); Debug.Log("[Unit07WorkOrder06Capture] " + s); }

            public static void Start(string dir)
            {
                outDir = dir;
                Application.runInBackground = true;   // 编辑器窗口没有焦点时 Play 模式也继续推进（只影响本次运行）
                Application.logMessageReceived += (m, st, type) =>
                {
                    if (m.StartsWith("[Unit07WorkOrder06Capture]")) return;
                    if (type != LogType.Log) console.Add($"{type}: {m.Split('\n')[0]}");
                };
                dock = Object.FindFirstObjectByType<Unit07DockController>();
                input = Object.FindFirstObjectByType<Unit07DockInput>();
                bridge = Object.FindFirstObjectByType<Unit07WorkOrder06Bridge>();
                wo = bridge.Session;
                Note($"进入 Play 模式（runInBackground={Application.runInBackground}，帧 {Time.frameCount}）：Unity {Application.unityVersion}，Game 视图 {Screen.width}×{Screen.height}，图形 API {SystemInfo.graphicsDeviceType}，GPU {SystemInfo.graphicsDeviceName}");
                Note($"工单 {wo.CaseId}，机器人 {Unit07WorkOrder06Session.RobotId}；维修座的“允许结束维修”= {dock.ServiceGate?.GetType().Name}；" +
                     $"场景里 ManualServiceCompletionGate {Object.FindObjectsByType<ManualServiceCompletionGate>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length} 个；" +
                     $"模型缺少的工单节点：{(bridge.MissingModelAnchors().Count == 0 ? "无" : string.Join(", ", bridge.MissingModelAnchors()))}");

                bool Dock(DockState s) => dock.State == s;
                void Do(Func<bool> ready, Action act, string what) => steps.Add((ready, act, what));
                void Now(Action act, string what) => Do(() => true, act, what);

                Do(() => Time.timeSinceLevelLoad > 1f, () => { Panels("W01_hovering_work_order_waits_dock"); CheckPanelClear(); }, "初始");
                Now(() => Work("悬停时检查", () => wo.Inspect(wo.Plan.InspectionAnchors[0])), "悬停时检查");
                Now(() => Click(DockAction.Clamps), "张开夹具");
                Do(() => Dock(DockState.SeatedOpen), () => Click(DockAction.Clamps), "夹紧");
                Do(() => Dock(DockState.Clamped), () => { Stage(); Click(DockAction.PowerSwitch); }, "断电");
                Do(() => Dock(DockState.SpinningDown), () => { Work("减速中检查", () => wo.Inspect(wo.Plan.InspectionAnchors[0])); Click(DockAction.PowerSwitch); }, "减速中检查 / 恢复供电");
                Do(() => Dock(DockState.RotorsStopped) && wo.Stage == RobotRepairStage.Inspect, () => { Stage(); Panels("W02_rotors_stopped_inspect"); }, "停转");
                Now(() => { foreach (var a in wo.Plan.InspectionAnchors) Work("检查 " + a, () => wo.Inspect(a)); }, "检查");
                Now(() => { Click(DockAction.PowerSwitch); Work("越序拆卸 2", () => wo.Remove("2")); }, "拆卸前恢复供电 / 越序");
                Now(() => { foreach (var st in wo.Plan.RemovalSteps.Take(6)) Work("拆卸 " + st.Id, () => wo.Remove(st.Id)); Panels("W03_disassembly_in_progress"); }, "拆卸 1-6");
                Now(() => { foreach (var st in wo.Plan.RemovalSteps.Skip(6)) Work("拆卸 " + st.Id, () => wo.Remove(st.Id)); Click(DockAction.Clamps); Panels("W04_repair_choice"); }, "拆卸 7-10Lc");
                Now(() => { Work("选择 " + RobotRepairChoice.RebalanceRotor, () => wo.ChooseRepair(RobotRepairChoice.RebalanceRotor));
                            foreach (var st in wo.Plan.RemovalSteps.Reverse()) Work("装回 " + st.Id, () => wo.Install(st.Id)); }, "维修 / 装回");
                Now(() => { Click(DockAction.PowerSwitch); Panels("W05_reassembled_power_refused_before_check"); }, "检查前恢复供电");
                Now(() => { Work("通电前检查", wo.RunPrePowerCheck); Panels("W06_pre_power_check_passed"); }, "通电前检查");
                Now(() => { Click(DockAction.PowerSwitch); Stage(); }, "恢复供电");
                Do(() => Dock(DockState.SpinningUp), () => Work("加速中复测", () => wo.HoverRetest(true)), "加速中复测");
                Do(() => Dock(DockState.Clamped), () => Click(DockAction.Clamps), "松开夹具");
                Do(() => Dock(DockState.SeatedOpen), () => { Work("未离座复测", () => wo.HoverRetest(true)); Act(DockAction.LiftOff); }, "离座（L）");
                Do(() => Dock(DockState.Undocked), () => { Stage(); measureT = Time.time; }, "离座");
                Do(() => Time.time - measureT > 0.5f, () => { measureRotor = dock.Rotors.Rotors[0].localRotation; measureT = Time.time; }, "等 Animator 过渡（0.2 s）结束");
                Do(() => Time.time - measureT > 0.1f, () =>
                {
                    Note($"离座后转子：维修座接管={dock.Rotors.Driven}，Animator Idle_Hover={dock.RobotAnimator.GetCurrentAnimatorStateInfo(0).IsName("Idle_Hover")}，" +
                         $"实测 {Quaternion.Angle(measureRotor, dock.Rotors.Rotors[0].localRotation) / Mathf.Max(1e-4f, Time.time - measureT):F0}°/s（约 0.1 s 窗口，按骨骼实际旋转；窗口不能接近 0.5 s，否则转角接近 180° 会回绕）");
                    Panels("W07_undocked_hover_retest");
                }, "离座转速");
                Now(() => { Work("悬停复测：不通过", () => wo.HoverRetest(false)); Panels("W08_hover_retest_failed_redock_prompt"); }, "悬停复测失败");
                Do(() => Time.time - measureT > 1.2f, () => Click(DockAction.Clamps), "重新落座");
                Do(() => Dock(DockState.SeatedOpen), () => Click(DockAction.Clamps), "重新夹紧");
                Do(() => Dock(DockState.Clamped), () => { Stage(); Click(DockAction.PowerSwitch); }, "再次断电");
                Do(() => Dock(DockState.RotorsStopped) && wo.Stage == RobotRepairStage.Disassemble, () => { Stage(); Panels("W09_second_cycle_disassemble"); }, "第二轮");
                Now(() =>
                {
                    foreach (var st in wo.Plan.RemovalSteps) Work("拆卸 " + st.Id, () => wo.Remove(st.Id));
                    Work("选择 " + RobotRepairChoice.ReplaceMotorCore, () => wo.ChooseRepair(RobotRepairChoice.ReplaceMotorCore));
                    foreach (var st in wo.Plan.RemovalSteps.Reverse()) Work("装回 " + st.Id, () => wo.Install(st.Id));
                    Work("通电前检查", wo.RunPrePowerCheck);
                    Click(DockAction.PowerSwitch);
                }, "第二轮维修");
                Do(() => Dock(DockState.Clamped), () => Click(DockAction.Clamps), "松开夹具");
                Do(() => Dock(DockState.SeatedOpen), () => Act(DockAction.LiftOff), "离座（L）");
                Do(() => Dock(DockState.Undocked), () => { Work("悬停复测：通过", () => wo.HoverRetest(true)); Work("负载复测：通过", () => wo.LoadRetest(true)); Stage(); Panels("W10_work_order_complete"); }, "复测通过");
                Now(() => Act(DockAction.PowerSwitch), "离座后断电");
                Do(() => Time.time - measureT > 0f, Finish, "结束");
                next = 0;
                stepStart = Time.realtimeSinceStartup;
                EditorApplication.update += Tick;
            }

            static void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                if (next >= steps.Count) return;
                var (ready, act, what) = steps[next];
                if (!ready())
                {
                    if (Time.realtimeSinceStartup - stepStart > StepTimeout)
                    {
                        Note($"步骤 {next}（{what}）等待超时：维修座 {dock.State}，工单 {wo.Stage}，Time.time {Time.time:F2}，帧 {Time.frameCount}");
                        console.Add("Exception: 步骤超时 " + what);
                        next = steps.Count - 1;   // 直接结束
                        steps[next].act();
                    }
                    return;
                }
                try { act(); }
                catch (Exception e) { Note($"步骤 {next}（{what}）异常：{e.Message}"); console.Add("Exception: " + e.Message); }
                next++;
                stepStart = Time.realtimeSinceStartup;
            }

            static void Stage() => Note($"  · 维修座 {dock.State}（供电 {(dock.PowerOn ? "ON" : "OFF")}，转速 {dock.Rotors.SpeedDegPerSec:F0}°/s）→ 工单阶段 {wo.Stage}，第 {wo.RepairCycle + 1} 轮");

            static void Work(string what, Func<bool> op)
            {
                bool pw = dock.PowerOn; var st = dock.State;
                bool ok = op();
                string side = pw != dock.PowerOn || st != dock.State ? "【维修座状态被工单改变！】" : "";
                Note($"工单 {what}：{(ok ? "执行" : "拒绝")} — {wo.LastFeedback}{side}");
                if (side.Length > 0) console.Add("Error: 工单操作改变了维修座状态");
            }

            static void Act(DockAction a)
            {
                bool ok = dock.Interact(a);
                Note($"维修座 {a}：{(ok ? "执行" : "拒绝")} — {dock.LastMessage}（状态 {dock.State}）");
            }

            /// <summary>用镜头射线点击维修座部件（与玩家鼠标点击同一条路径）。</summary>
            static void Click(DockAction a)
            {
                var cam = input.ViewCamera;
                foreach (var di in Object.FindObjectsByType<DockInteractable>(FindObjectsSortMode.None).Where(d => d.action == a))
                {
                    var col = di.GetComponent<Collider>();
                    if (col == null) continue;
                    Vector2 sp = cam.WorldToScreenPoint(col.bounds.center);
                    if (input.Pick(sp) != di || bridge.BlocksClick(sp)) continue;
                    bool ok = input.TryClick(sp, out _);
                    Note($"点击 {a}（屏幕 {sp.x:F0},{sp.y:F0}）：{(ok ? "执行" : "拒绝")} — {dock.LastMessage}（状态 {dock.State}）");
                    return;
                }
                Note($"点击 {a}：镜头点不到（被遮挡或被工单面板挡住）");
                console.Add("Error: 点不到 " + a);
            }

            static void CheckPanelClear()
            {
                var cam = input.ViewCamera;
                foreach (var di in Object.FindObjectsByType<DockInteractable>(FindObjectsSortMode.None).OrderBy(d => d.action))
                {
                    var col = di.GetComponent<Collider>();
                    if (col == null) continue;
                    Vector2 sp = cam.WorldToScreenPoint(col.bounds.center);
                    Note($"  部件 {di.name}（{di.action}）屏幕 {sp.x:F0},{sp.y:F0}：被工单面板挡住={bridge.BlocksClick(sp)}");
                }
            }

            static void Panels(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "Screenshots", name + ".png"));
                shots++;
                Note($"截图 {name}：工单阶段 {wo.Stage}，下一步“{wo.NextHint()}”");
            }

            static void Finish()
            {
                EditorApplication.update -= Tick;
                next = steps.Count;
                Note($"工单最终阶段 {wo.Stage}，轮次 {wo.RepairCycle + 1}，工单事件 {wo.Inner.Events.Count} 条，工单记录 {wo.Log.Count} 条（拒绝 {wo.Log.Count(e => !e.Accepted)}），安全故障：{wo.SafetyFault ?? "无"}");
                Note($"截图 {shots} 张；控制台警告 / 错误 {console.Count} 条" + (console.Count > 0 ? "：" + string.Join(" | ", console.Distinct().Take(20)) : ""));
                File.WriteAllText(Path.Combine(outDir, "play_capture.txt"), log.ToString(), new UTF8Encoding(false));
                bool ok = wo.Stage == RobotRepairStage.Complete && console.Count == 0;
                SessionState.SetInt(KeyExit, ok ? 0 : 3);
                // 等最后一张截图写完再退出
                int frames = 5;
                EditorApplication.update += Wait;
                void Wait() { if (--frames > 0) return; EditorApplication.update -= Wait; EditorApplication.ExitPlaymode(); }
            }
        }
    }
}
