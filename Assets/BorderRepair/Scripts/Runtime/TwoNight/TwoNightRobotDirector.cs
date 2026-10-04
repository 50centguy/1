using System.Collections;
using BorderRepair.Dock;
using BorderRepair.FirstOrder;
using BorderRepair.FirstOrder.Slice;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.TwoNight
{
    /// <summary>
    /// 七号场景的两晚协调：只管夜、阶段、登记、存档和界面文字。
    /// - 七号的维修步骤仍由 FirstOrderFlow 管（第一晚用 InspectionLocked 只许停靠、登记；第二晚用 ResumeAtInspection 从安全检查阶段开始）；
    /// - 夹具、供电、转子仍由 Unit07DockController 管（第二晚用 RestoreRotorsStopped 恢复“已断电、叶轮停稳”）；
    /// - 端盘演出由 TrayIncident 管。本组件不写七号或托盘的 Transform。
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class TwoNightRobotDirector : MonoBehaviour
    {
        [SerializeField] FirstOrderFlow flow;
        [SerializeField] FirstOrderInput input;
        [SerializeField] SliceView view;
        [SerializeField] TrayIncident incident;
        [SerializeField] TwoNightEconomy economy;
        [SerializeField] Font font;

        public const string ManualTitle = "维修手册 · 七号（内部工单：端盘时左侧下沉）";
        public const string ManualBody =
            "安全：七号落座、夹紧、断电，并且叶轮停稳之后，才能检查和拆卸。转动和减速中只能看，不能动手。\n" +
            "现象：打烊后七号单手端零件盘时，机身慢慢往左边沉，盘面跟着斜；屏幕显示 TRAY UNSTABLE。右侧没有异常表现。\n" +
            "建议检查：先看右引擎作对照；再看左引擎外观和进气口；需要时扳开锁扣、取下上盖检查里面，并翻看上盖内侧的保养记录。\n" +
            "鼠标：左键操作，右键观察（或在右下角切到“观察”）。底部镜头栏切换视角，“返回”回到上一个镜头。\n" +
            "可选调试：数字键 1–9 切镜头，F3 开关调试面板（文字输入时不响应）。";

        public TwoNightState State => TwoNightRun.Current;
        public bool RegisterVisible => registerBtn != null && registerBtn.gameObject.activeSelf;
        public bool EndPanelVisible => endPanel != null && endPanel.activeSelf;
        public Button RegisterButton => registerBtn;
        public Button MenuButton { get; private set; }
        public Button QuitButton { get; private set; }
        public Button RetrySaveButton { get; private set; }
        public string LastSaveMessage { get; private set; }
        public string LastRegisterMessage { get; private set; }
        public Text HudText => hud;
        public Text CaptionText => caption;
        public TrayIncident Incident => incident;
        public FirstOrderFlow Flow => flow;
        public Vector3 TrayHomePosition => trayHomePos;
        public Quaternion TrayHomeRotation => trayHomeRot;

        Text hud, caption, endText;
        Button registerBtn;
        GameObject endPanel;
        Vector3 trayHomePos; Quaternion trayHomeRot;

        public void Configure(FirstOrderFlow f, FirstOrderInput i, SliceView v, TrayIncident t, TwoNightEconomy e, Font fnt)
        { flow = f; input = i; view = v; incident = t; economy = e; font = fnt; }

        void Start()
        {
            BuildUi();
            var tr = incident.Tray.transform;
            trayHomePos = tr.position; trayHomeRot = tr.rotation;
            view.SetManual(ManualTitle, ManualBody);
            var s = TwoNightRun.Current;
            if (s == null)
            {
                // 直接打开本场景（编辑器调试）：按“账本已确认”的第一晚开始，不经过柜台
                s = TwoNightRun.NewGame(economy);
                TwoNightRun.SettleCommunicator(s, true, "debug", economy);
                TwoNightRun.ConfirmLedger(s);
                Debug.LogWarning("[TwoNight] 没有两晚进度，按调试模式从第一晚端盘开始。");
            }
            switch (s.phase)
            {
                case TwoNightPhase.Night1Incident: StartCoroutine(Night1()); break;
                case TwoNightPhase.Night1Docking: BeginDocking(); break;
                case TwoNightPhase.Night2Open: Night2(); break;
                case TwoNightPhase.Night1Ended: ShowEnd(true, "第一晚已经结束。"); break;
                default: Debug.LogError("[TwoNight] 七号场景不能从阶段 " + s.phase + " 开始", this); break;
            }
            RefreshHud();
        }

        IEnumerator Night1()
        {
            view.SetTitle("第一晚 · 打烊后 · 七号");
            flow.InspectionLocked = true;
            input.enabled = false;                                   // 演出期间不接受 3D 点击
            SetSliceHud(false);                                      // 演出期间不显示停靠步骤 / 镜头按钮（只留字幕和账目）
            flow.Rig.Go(FirstOrderCameraRig.Overview);
            incident.Caption += ShowCaption;
            yield return incident.Play();
            while (!incident.Done) yield return null;
            incident.Caption -= ShowCaption;
            TwoNightRun.FinishIncident(TwoNightRun.Current);
            BeginDocking();
        }

        void SetSliceHud(bool on) { var c = view.GetComponent<Canvas>(); if (c != null) c.enabled = on; }

        void BeginDocking()
        {
            SetSliceHud(true);
            flow.InspectionLocked = true;
            input.enabled = true;
            flow.Rig.Go(FirstOrderCameraRig.Dock);
            ShowCaption("七号放回零件盘，机身回正了。用鼠标让它停靠：张开夹具落座 → 夹紧 → 断电，等叶轮停稳后登记。");
            RefreshHud();
        }

        void Night2()
        {
            view.SetTitle("第二晚 · 七号内部工单 " + TwoNightRun.Current.unit07WorkOrderId);
            flow.InspectionLocked = false;
            if (!flow.Dock.RestoreRotorsStopped()) Debug.LogError("[TwoNight] 维修座恢复安全状态失败：" + flow.Dock.LastMessage, this);
            if (!flow.ResumeAtInspection("第二晚。七号昨晚已停靠、夹紧、断电，叶轮停稳，还没修。可以开始检查左引擎。"))
                Debug.LogError("[TwoNight] 七号流程没能从检查阶段开始（Step " + flow.Step + "，维修座 " + flow.Dock.State + "）", this);
            ShowCaption("第二晚开店前。先看手册，再开始检查七号。");
            view.ToggleManual();                                     // 打开入口手册一次；玩家可以关掉
            RefreshHud();
        }

        void Update()
        {
            var s = TwoNightRun.Current;
            if (s == null) return;
            bool canRegister = s.phase == TwoNightPhase.Night1Docking && flow.Step == FoStep.InspectLeftEngine && flow.Dock.State == DockState.RotorsStopped && !incident.Playing;
            if (registerBtn.gameObject.activeSelf != canRegister) registerBtn.gameObject.SetActive(canRegister);
        }

        /// <summary>从维修座、七号、托盘的真实状态收集“安全停靠”结论（存档只存这些结论）。</summary>
        public Unit07SafeState CollectSafeState()
        {
            var dock = flow.Dock;
            var root = dock.RobotRoot;
            var tr = incident.Tray.transform;
            return new Unit07SafeState
            {
                seated = dock.IsSeated && (root.position - dock.RobotAnchor.position).sqrMagnitude < 1e-6f,
                clamped = dock.State >= DockState.Clamped && dock.ClampOpenFraction <= 1e-4f,
                powerOff = !dock.PowerOn,
                rotorsStopped = dock.State == DockState.RotorsStopped && dock.Rotors.SpeedDegPerSec <= 0f,
                trayStowed = !incident.Tray.Taken && (tr.position - trayHomePos).sqrMagnitude < 1e-6f && Quaternion.Angle(tr.rotation, trayHomeRot) < 0.1f,
                robotUpright = !incident.Playing && Vector3.Angle(root.up, Vector3.up) < 0.1f,
            };
        }

        public void Register()
        {
            var s = TwoNightRun.Current;
            if (!TwoNightRun.RegisterUnit07(s, CollectSafeState(), out var why))
            {
                LastRegisterMessage = why;
                ShowCaption("不能登记：" + why);
                return;
            }
            LastRegisterMessage = "已登记内部维修单 " + s.unit07WorkOrderId + "。";
            input.enabled = false;
            SaveNow();
        }

        void SaveNow()
        {
            bool ok = TwoNightSave.Write(TwoNightRun.Current, out var msg);
            LastSaveMessage = msg;
            ShowEnd(ok, msg);
        }

        void ShowEnd(bool saved, string msg)
        {
            var s = TwoNightRun.Current;
            endText.text =
                "<b>第一晚结束</b>\n\n" +
                $"现金 {TwoNightUi.Money(s.Cash)}。房租 {TwoNightUi.Money(s.rent)}，第 {s.rentDueNight} 晚打烊前到期，还差 {TwoNightUi.Money(s.RentShortfall)}。\n" +
                $"七号：已停靠、夹紧、断电，叶轮停稳。内部维修单 {s.unit07WorkOrderId} 已登记，明天开盖检查。\n\n" +
                (saved ? $"<color=#9FD49F>{msg}</color>（{TwoNightSave.FilePath}）\n下次从主菜单选“继续”，进入第二晚。"
                       : $"<color=#F2B95A>{msg}</color>\n可以再试一次保存；不保存的话，下次要从第一晚重新开始。");
            RetrySaveButton.gameObject.SetActive(!saved && s.phase == TwoNightPhase.Night1Ended);
            endPanel.SetActive(true);
        }

        void ShowCaption(string line) { if (caption != null) caption.text = line; }

        public void RefreshHud()
        {
            var s = TwoNightRun.Current;
            if (s == null || hud == null) return;
            string task = s.phase == TwoNightPhase.Night2Open ? $"待修：七号内部工单 {s.unit07WorkOrderId}（未修）" :
                          s.phase == TwoNightPhase.Night1Incident ? "打烊后" :
                          s.phase == TwoNightPhase.Night1Docking ? "让七号停靠、断电，登记内部维修单" : s.phase.ToString();
            hud.text = $"第 {s.night} 晚　现金 <b>{TwoNightUi.Money(s.Cash)}</b>\n房租 {TwoNightUi.Money(s.rent)}（第 {s.rentDueNight} 晚到期）还差 {TwoNightUi.Money(s.RentShortfall)}\n{task}";
        }

        void BuildUi()
        {
            var root = new GameObject("TwoNightRobotUI");
            root.transform.SetParent(transform, false);
            TwoNightUi.Canvas(root, 40);
            var hp = TwoNightUi.Panel(root.transform, "Hud", new Vector2(1, 1), new Vector2(-16 - 470, -16), new Vector2(470, 120));
            hp.GetComponent<Image>().raycastTarget = false;   // 状态条和字幕不挡 3D 点击
            hud = TwoNightUi.Label(hp, "HudText", "", font, 17, new Vector2(14, -10), new Vector2(442, 100), TwoNightUi.TextMain);
            var cp = TwoNightUi.Panel(root.transform, "Caption", new Vector2(0.5f, 0f), new Vector2(-480, 170), new Vector2(960, 70));
            cp.GetComponent<Image>().raycastTarget = false;
            caption = TwoNightUi.Label(cp, "CaptionText", "", font, 19, new Vector2(16, -10), new Vector2(928, 54), TwoNightUi.TextMain, TextAnchor.MiddleCenter);
            registerBtn = TwoNightUi.Button(root.transform, "Register", "登记内部维修单（今晚不拆）", font, Vector2.zero, new Vector2(320, 48), Register, true);
            var rrt = (RectTransform)registerBtn.transform; rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0f); rrt.anchoredPosition = new Vector2(-160, 240);
            registerBtn.gameObject.SetActive(false);

            endPanel = TwoNightUi.Panel(root.transform, "NightEnd", new Vector2(0.5f, 0.5f), new Vector2(-400, 220), new Vector2(800, 440)).gameObject;
            endText = TwoNightUi.Label(endPanel.transform, "Body", "", font, 20, new Vector2(30, -26), new Vector2(740, 300), TwoNightUi.TextMain);
            MenuButton = TwoNightUi.Button(endPanel.transform, "Menu", "回到主菜单", font, new Vector2(330, -370), new Vector2(200, 46), () => SceneManager.LoadScene(TwoNightScenes.Menu), true);
            QuitButton = TwoNightUi.Button(endPanel.transform, "Quit", "退出游戏", font, new Vector2(550, -370), new Vector2(200, 46), Application.Quit);
            RetrySaveButton = TwoNightUi.Button(endPanel.transform, "RetrySave", "再试一次保存", font, new Vector2(30, -370), new Vector2(200, 46), SaveNow);
            endPanel.SetActive(false);
        }
    }
}
