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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
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

        /// <summary>
        /// 三种窗口尺寸下的布局与输入（程序鼠标事件，虚拟鼠标设备；不是真人试玩）：按钮在画面内、互不重叠；文字没被截；
        /// 手册展开时后方 3D 不悬停、不点；点“关闭”后恢复；悬停提示扫点不压按钮、不出画面。窗口尺寸以 Screen 实际得到的为准。
        /// </summary>
        IEnumerator LayoutCheck(SliceView view, FirstOrderInput input, FirstOrderFlow flow)
        {
            var mouse = FirstOrderAcceptanceDriver.CreateVirtualMouse(out var cleanup);
            input.enabled = true;
            Line($"布局检查：显示器 {Screen.currentResolution.width}×{Screen.currentResolution.height}，当前窗口 {Screen.width}×{Screen.height}（{Screen.fullScreenMode}）");
            foreach (var (w, h, label) in new[] { (1600, 900, "1600x900"), (1920, 1080, "1920x1080"), (1280, 960, "narrow_1280x960") })
            {
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
                for (int k = 0; k < 20; k++) yield return null;
                if ((Screen.width != w || Screen.height != h) && Screen.currentResolution.width >= w && Screen.currentResolution.height >= h)
                {
                    Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);       // 窗口模式放不下（标题栏 / 任务栏）时改无边框全屏
                    for (int k = 0; k < 20; k++) yield return null;
                }
                string got = $"{Screen.width}×{Screen.height}（{Screen.fullScreenMode}）";
                Canvas.ForceUpdateCanvases();
                var buttons = FindObjectsByType<Button>(FindObjectsSortMode.None).Where(b => b.isActiveAndEnabled).ToList();
                Rect R(RectTransform rt) { var c = new Vector3[4]; rt.GetWorldCorners(c); return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y); }
                int outside = buttons.Count(b => { var r = R((RectTransform)b.transform); return r.xMin < -1 || r.yMin < -1 || r.xMax > Screen.width + 1 || r.yMax > Screen.height + 1; });
                int overlap = 0; var pairs = new StringBuilder();
                for (int i = 0; i < buttons.Count; i++)
                    for (int j = i + 1; j < buttons.Count; j++)
                    {
                        var a = R((RectTransform)buttons[i].transform); var b = R((RectTransform)buttons[j].transform);
                        if (Rect.MinMaxRect(a.xMin + 1, a.yMin + 1, a.xMax - 1, a.yMax - 1).Overlaps(b)) { overlap++; if (pairs.Length < 120) pairs.Append($"{buttons[i].name}/{buttons[j].name} "); }
                    }
                var texts = FindObjectsByType<Text>(FindObjectsSortMode.None).Where(t => t.isActiveAndEnabled && !string.IsNullOrEmpty(t.text)).ToList();
                var over = texts.Where(t => t.verticalOverflow == VerticalWrapMode.Truncate && t.preferredHeight > ((RectTransform)t.transform).rect.height + 4f).Select(t => t.name).ToList();
                bool sizeOk = Screen.width == w && Screen.height == h;
                Check(sizeOk, sizeOk ? $"{label}：窗口尺寸达到请求 {w}×{h}" : $"{label}：请求 {w}×{h}，实际只有 {got}（显示器 {Screen.currentResolution.width}×{Screen.currentResolution.height}）——下面各项量的是实际尺寸，不能算作 {label} 的结果");
                Check(outside == 0 && overlap == 0 && over.Count == 0,
                      $"{label}：实际 {got}；按钮 {buttons.Count} 个，出画面 {outside}，互相重叠 {overlap}{(overlap > 0 ? "（" + pairs + "）" : "")}；截断文字 {over.Count}{(over.Count > 0 ? "（" + string.Join(",", over.Take(6)) + "）" : "")}");
                if (!view.ManualOpen) view.ToggleManual();
                yield return null;
                var cam = flow.Rig.Cam;
                string camBefore = flow.Rig.Current, camUsed = null;
                Vector2? behind = null;
                // 当前镜头下手册外没有可指的 3D（窄窗口时手册占了大半个画面）：换维修座、总览镜头再找
                foreach (var shotId in new[] { camBefore, FirstOrderCameraRig.Dock, FirstOrderCameraRig.Overview })
                {
                    if (shotId != flow.Rig.Current) { flow.Rig.Go(shotId, true); yield return null; yield return null; }
                    for (int i = 2; i < 40 && behind == null; i++)
                        for (int j = 2; j < 24 && behind == null; j++)
                        {
                            var p = new Vector2(Screen.width * i / 41f, Screen.height * j / 25f);
                            if (!FirstOrderInput.IsOverUI(p) && FirstOrderInput.Pick(cam.ScreenPointToRay(p)) != null) behind = p;
                        }
                    if (behind != null) { camUsed = shotId; break; }
                }
                bool acted = false; void OnActed(string t, bool ok, string m) => acted = true;
                if (behind != null)
                {
                    flow.Acted += OnActed;
                    mouse.MakeCurrent();
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = behind.Value }); yield return null; yield return null;
                    bool noHover = input.Hovered == null && !view.TooltipVisible;
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = behind.Value }.WithButton(MouseButton.Left, true)); yield return null;
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = behind.Value }.WithButton(MouseButton.Left, false)); yield return null; yield return null;
                    flow.Acted -= OnActed;
                    yield return Shot($"layout_{label}_manual_open");
                    Check(noHover && !acted && view.ManualOpen, $"{label}：手册展开时手册外 3D（镜头 {camUsed}，{behind.Value.x:F0}, {behind.Value.y:F0}：{FirstOrderInput.NameOf(FirstOrderInput.Pick(cam.ScreenPointToRay(behind.Value)))}）不悬停、无提示、点击无效");
                    var close = view.GetButton("manual:close"); var cc = R((RectTransform)close.transform).center;
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = cc }); yield return null; yield return null;
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = cc }.WithButton(MouseButton.Left, true)); yield return null;
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = cc }.WithButton(MouseButton.Left, false)); yield return null; yield return null;
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = behind.Value + Vector2.right }); yield return null;
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = behind.Value }); yield return null; yield return null;
                    Check(!view.ManualOpen && input.Hovered != null, $"{label}：点“关闭”收起手册后恢复悬停（{FirstOrderInput.NameOf(input.Hovered)}）");
                }
                else { yield return Shot($"layout_{label}_manual_open"); Check(false, $"{label}：三个镜头下手册外都找不到可指的 3D 对象"); }
                if (view.ManualOpen) view.ToggleManual();
                if (flow.Rig.Current != camBefore) { flow.Rig.Go(camBefore, true); yield return null; yield return null; }
                int shown = 0, tipOver = 0, tipOut = 0;
                var canvas = (RectTransform)view.transform;
                for (int i = 1; i < 24; i++)
                    for (int j = 1; j < 14; j++)
                    {
                        var p = new Vector2(Screen.width * i / 24f, Screen.height * j / 14f);
                        if (FirstOrderInput.IsOverUI(p) || FirstOrderInput.Pick(cam.ScreenPointToRay(p)) == null) continue;
                        InputSystem.QueueStateEvent(mouse, new MouseState { position = p }); yield return null; yield return null;
                        if (!view.TooltipVisible) continue;
                        shown++;
                        var c = new Vector3[4]; view.TooltipRect.GetWorldCorners(c);
                        var size = canvas.rect.size;
                        Vector2 a = (Vector2)canvas.InverseTransformPoint(c[0]) + size * 0.5f, b = (Vector2)canvas.InverseTransformPoint(c[2]) + size * 0.5f;
                        var tr = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
                        if (tr.xMin < 0 || tr.yMin < 0 || tr.xMax > size.x || tr.yMax > size.y) tipOut++;
                        if (view.TooltipAvoidRects().Any(x => x.Overlaps(tr))) tipOver++;
                    }
                yield return Shot($"layout_{label}_manual_closed");
                Check(shown > 0 && tipOver == 0 && tipOut == 0, $"{label}：悬停提示扫点显示 {shown} 次，压按钮 / 状态栏 {tipOver}、出画面 {tipOut}");
                if (!view.ManualOpen) view.ToggleManual();
                yield return null;
            }
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            for (int k = 0; k < 10; k++) yield return null;
            cleanup();
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
            var input = FindFirstObjectByType<FirstOrderInput>();
            yield return LayoutCheck(view, input, flow);
            if (view.ManualOpen) view.ToggleManual();
            yield return Shot("night2_open");
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
