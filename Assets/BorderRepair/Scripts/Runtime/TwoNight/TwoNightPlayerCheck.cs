using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Dock;
using BorderRepair.FirstOrder;
using BorderRepair.FirstOrder.Slice;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.TwoNight
{
    /// <summary>
    /// 独立 Player 两段自检（只在命令行带 -twoNightSelfCheck &lt;输出目录&gt; -twoNightPhase 1|2 时运行；平时不存在）：
    /// - 第 1 段（新进程）：主菜单 → 新游戏 → 柜台修通讯器（会话 API，与按钮同一入口）→ 账本 → 七号端盘演出 → 程序点击停靠 / 夹紧 / 断电 → 等停稳 → 登记 → 存档 → 退出；
    /// - 第 2 段（另一个新进程）：主菜单 → 继续 → 第二晚：核对现金、待修工单、维修座安全状态 → 打开手册 → 开始检查 → 退出。
    /// 两段之间进程真的退出再启动，验证的是磁盘上的存档，不是同一进程里的内存。截图用 ScreenCapture（游戏画面）。程序操作，不是真人试玩。
    /// </summary>
    public class TwoNightPlayerCheck : MonoBehaviour
    {
        string outDir; int phase; int shot;
        readonly StringBuilder log = new StringBuilder();
        bool allOk = true;

        public static void StartIfRequested()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-twoNightSelfCheck");
            if (i < 0 || FindFirstObjectByType<TwoNightPlayerCheck>() != null) return;
            var go = new GameObject("TwoNightPlayerCheck");
            DontDestroyOnLoad(go);
            var c = go.AddComponent<TwoNightPlayerCheck>();
            c.outDir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Path.Combine(Application.persistentDataPath, "TwoNightCheck");
            int p = Array.IndexOf(args, "-twoNightPhase");
            c.phase = p >= 0 && p + 1 < args.Length && int.TryParse(args[p + 1], out var v) ? v : 1;
            Directory.CreateDirectory(c.outDir);
        }

        void Start() => StartCoroutine(phase == 2 ? Phase2() : Phase1());

        void Line(string s) { log.AppendLine(s); Debug.Log("[TwoNightCheck] " + s); }
        void Check(bool ok, string s) { if (!ok) allOk = false; Line((ok ? "通过 " : "失败 ") + s); }

        IEnumerator Shot(string name)
        {
            for (int k = 0; k < 3; k++) yield return null;
            yield return new WaitForEndOfFrame();
            var file = $"p{phase}_{++shot:00}_{name}.png";
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, file));
            for (int k = 0; k < 3; k++) yield return null;
            Line("截图（游戏画面）：" + file);
        }

        IEnumerator WaitScene(string n, float sec = 30f)
        {
            float until = Time.realtimeSinceStartup + sec;
            while (SceneManager.GetActiveScene().name != n && Time.realtimeSinceStartup < until) yield return null;
            for (int k = 0; k < 10; k++) yield return null;
            Check(SceneManager.GetActiveScene().name == n, "进入场景 " + n);
        }

        void Singles()
        {
            int es = FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length;
            int al = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a => a.enabled);
            Check(es == 1 && al <= 1, $"{SceneManager.GetActiveScene().name}：EventSystem {es} 个，启用的 AudioListener {al} 个");
        }

        void Glyphs()
        {
            int missing = 0, texts = 0;
            foreach (var t in FindObjectsByType<Text>(FindObjectsSortMode.None).Where(t => t.isActiveAndEnabled && !string.IsNullOrEmpty(t.text)))
            {
                texts++;
                var plain = System.Text.RegularExpressions.Regex.Replace(t.text, "<[^>]+>", "");
                t.font.RequestCharactersInTexture(plain, t.fontSize, t.fontStyle);
                foreach (var ch in plain) if (!char.IsWhiteSpace(ch) && !t.font.HasCharacter(ch)) missing++;
            }
            Check(missing == 0, $"{SceneManager.GetActiveScene().name}：显示中的文字 {texts} 段，没有字形的字符 {missing} 个");
        }

        void Finish()
        {
            Line(allOk ? "结果：全部通过" : "结果：有失败项");
            File.WriteAllText(Path.Combine(outDir, $"selfcheck_phase{phase}.txt"), log.ToString(), new UTF8Encoding(false));
            Application.Quit(allOk ? 0 : 1);
        }

        IEnumerator Phase1()
        {
            Line($"两晚自检第 1 段 {DateTime.Now:yyyy-MM-dd HH:mm:ss}，{SystemInfo.operatingSystem}，{SystemInfo.graphicsDeviceName}，{Screen.width}×{Screen.height}，存档 {TwoNightSave.FilePath}");
            yield return WaitScene(TwoNightScenes.Menu);
            var menu = FindFirstObjectByType<TwoNightMenu>();
            Check(!menu.ContinueButton.interactable, "新存档目录：继续不可用（" + menu.StatusText.text + "）");
            Glyphs(); Singles();
            yield return Shot("menu");
            menu.NewButton.onClick.Invoke();

            yield return WaitScene(TwoNightScenes.Counter);
            Singles(); Glyphs();
            var st = FindFirstObjectByType<RepairStationController>();
            var cd = FindFirstObjectByType<TwoNightCounterDirector>();
            yield return Shot("counter_collector");
            for (int k = 0; k < 3; k++) cd.DialogueButton.onClick.Invoke();
            var s = st.Session;
            s.AcceptItem(); s.SetScanMode(true);
            foreach (var p in s.CurrentCase.inspectionPoints.Where(p => p.requiredForDiagnosis)) st.ScanPoint(p.pointId);
            s.TryBeginDiagnosis(); s.SubmitDiagnosis(s.CurrentCase.correctDiagnosisId);
            s.SubmitDecision(RepairDecision.Repair);
            yield return Shot("counter_result");
            s.NextCase();
            yield return null;
            Check(cd.LedgerVisible && TwoNightRun.Current.Cash == 800 && TwoNightRun.Current.RentShortfall == 400, $"账本：现金 {TwoNightRun.Current.Cash}，房租尚差 {TwoNightRun.Current.RentShortfall}");
            Glyphs();
            yield return Shot("ledger");
            cd.ConfirmButton.onClick.Invoke();

            yield return WaitScene(TwoNightScenes.Robot);
            Singles(); Glyphs();
            var director = FindFirstObjectByType<TwoNightRobotDirector>();
            var inc = director.Incident; var flow = director.Flow; var dock = flow.Dock;
            var input = FindFirstObjectByType<FirstOrderInput>();
            Check(inc.Playing, "账本确认后七号端盘演出开始");
            var shotTaken = new System.Collections.Generic.HashSet<string>();
            float until = Time.realtimeSinceStartup + 120f;
            while (!inc.Done && Time.realtimeSinceStartup < until)
            {
                string key = inc.Phase == "lean" ? (inc.LeanNow > inc.LeanDegrees * 0.95f ? "lean" : null)
                           : inc.Phase == "carry" || inc.Phase == "release" || inc.Phase == "away" ? inc.Phase : null;
                if (key != null && shotTaken.Add(key)) yield return Shot("incident_" + key);
                else yield return null;
            }
            Check(inc.Done && !inc.Tray.Taken && Vector3.Angle(dock.RobotRoot.up, Vector3.up) < 0.01f, "演出结束：托盘放回、机身回正");
            yield return Shot("after_incident");

            var drv = new FirstOrderAcceptanceDriver(flow, input);
            input.enabled = false;   // 不读桌面上的真实鼠标；程序按屏幕坐标点
            Component Grip()
            {
                flow.Rig.Go(FirstOrderCameraRig.Dock, true);
                var gs = new[] { "Dock_Clamp_L_Grip", "Dock_Clamp_R_Grip" }.Select(n => (Component)GameObject.Find(n).GetComponent<DockInteractable>()).ToList();
                return gs.FirstOrDefault(g => drv.FindClickPoint(g, out _)) ?? gs[0];
            }
            IEnumerator Click(string cam, Component target, bool expect, string label)
            {
                flow.Rig.Go(cam, true); yield return null;
                bool found = drv.FindClickPoint(target, out var sp);
                var (hit, ok) = found ? input.ClickAt(sp) : (null, false);
                Check(found && hit == target && ok == expect, $"{label}：{(ok ? "接受" : "拒绝")} —— {flow.Message}");
                float u = Time.realtimeSinceStartup + 20f; while (flow.Busy && Time.realtimeSinceStartup < u) yield return null;
            }
            var grip = Grip();
            yield return Click(FirstOrderCameraRig.Dock, grip, true, "点夹具握把：张开、落座");
            until = Time.realtimeSinceStartup + 20f; while (dock.State != DockState.SeatedOpen && Time.realtimeSinceStartup < until) yield return null;
            yield return Click(FirstOrderCameraRig.Dock, grip, true, "点夹具握把：夹紧");
            until = Time.realtimeSinceStartup + 20f; while (dock.State != DockState.Clamped && Time.realtimeSinceStartup < until) yield return null;
            var lever = GameObject.Find("Dock_PowerSwitch_LeverGrip").GetComponent<DockInteractable>();
            yield return Click(FirstOrderCameraRig.Dock, lever, true, "点断电开关");
            if (dock.State == DockState.SpinningDown) yield return Click(FirstOrderCameraRig.EngineL, flow.LatchOuter, false, "叶轮减速中碰锁扣（应拒绝）");
            until = Time.realtimeSinceStartup + 20f; while (dock.State != DockState.RotorsStopped && Time.realtimeSinceStartup < until) yield return null;
            yield return null;
            yield return Click(FirstOrderCameraRig.EngineL, flow.Cover, false, "停稳后碰上盖（第一晚不拆，应拒绝）");
            flow.Rig.Go(FirstOrderCameraRig.Dock, true);
            yield return Shot("docked_ready_to_register");
            Check(director.RegisterVisible, "停稳后出现“登记内部维修单”");
            director.RegisterButton.onClick.Invoke();
            yield return null;
            var r = TwoNightSave.Read();
            Check(r.status == SaveStatus.Ok && r.state.Cash == 800 && r.state.unit07.IsSafe, $"夜末存档：{r.status}，现金 {(r.state != null ? r.state.Cash : -1)}，{director.LastSaveMessage}");
            Glyphs();
            yield return Shot("night1_end_saved");
            Finish();
        }

        IEnumerator Phase2()
        {
            Line($"两晚自检第 2 段（新进程）{DateTime.Now:yyyy-MM-dd HH:mm:ss}，存档 {TwoNightSave.FilePath}");
            yield return WaitScene(TwoNightScenes.Menu);
            var menu = FindFirstObjectByType<TwoNightMenu>();
            Check(menu.ContinueButton.interactable, "继续可用：" + menu.StatusText.text);
            Glyphs();
            yield return Shot("menu_continue");
            var before = File.ReadAllText(TwoNightSave.FilePath);
            menu.ContinueButton.onClick.Invoke();
            yield return WaitScene(TwoNightScenes.Robot);
            Singles(); Glyphs();
            var director = FindFirstObjectByType<TwoNightRobotDirector>();
            var flow = director.Flow; var dock = flow.Dock; var s = TwoNightRun.Current;
            Check(s.night == 2 && s.Cash == 800 && s.RentShortfall == 400, $"第二晚：第 {s.night} 晚，现金 {s.Cash}，房租尚差 {s.RentShortfall}，工单 {s.unit07WorkOrderId}");
            Check(dock.State == DockState.RotorsStopped && !dock.PowerOn && dock.Rotors.SpeedDegPerSec <= 0f && dock.ClampOpenFraction <= 0f, $"维修座：{dock.State}，通电 {dock.PowerOn}，转速 {dock.Rotors.SpeedDegPerSec}");
            Check(flow.Step == FoStep.InspectLeftEngine && !flow.ClogCleared && !flow.BearingReplaced && flow.Bearing.Location == PartLocation.Installed, $"七号未修：步骤 {flow.Step}，进气口已清理 {flow.ClogCleared}，轴承已换 {flow.BearingReplaced}");
            Check(!director.Incident.Playing, "第二晚不重放事故");
            var view = FindFirstObjectByType<SliceView>();
            Check(view.ManualOpen && !(view.ManualTitleText + view.ManualBodyText).Contains("磨损"), "入口手册已打开，不写诊断答案");
            yield return Shot("night2_manual");
            view.ToggleManual();
            yield return Shot("night2_open");
            var input = FindFirstObjectByType<FirstOrderInput>();
            input.enabled = false;
            var drv = new FirstOrderAcceptanceDriver(flow, input);
            flow.Rig.Go(FirstOrderCameraRig.EngineL, true); yield return null;
            bool found = drv.FindClickPoint(flow.Cover, out var sp);
            var (hit, ok) = found ? input.ClickAt(sp) : (null, false);
            Check(found && ok && flow.Step == FoStep.ReleaseLatches, "可以开始检查：点左上盖 → " + flow.Message);
            yield return Shot("night2_inspect_started");
            Check(File.ReadAllText(TwoNightSave.FilePath) == before, "继续没有改存档");
            Finish();
        }
    }
}
