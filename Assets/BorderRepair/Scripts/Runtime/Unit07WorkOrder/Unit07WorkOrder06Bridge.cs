using System.Collections.Generic;
using BorderRepair.Dock;
using BorderRepair.RobotRepair;
using UnityEngine;

namespace BorderRepair.Unit07WorkOrder
{
    /// <summary>
    /// 场景桥接：把工单 06 接到 UNIT 07 维修座。
    /// - 作为维修座的“允许结束维修”接口（<see cref="IDockServiceCompletionGate"/>），转给 <see cref="Unit07WorkOrder06Session"/>。
    /// - 每帧在维修座之后读取维修座真实状态（夹具、供电、转子、零件盘），推进工单。
    /// - IMGUI 工单面板：检查、拆卸、维修选择、装回、通电前检查、复测。面板上没有供电按钮，供电只在维修座的断电开关上操作。
    /// </summary>
    [DefaultExecutionOrder(100)]   // 在 Unit07DockController.Update 之后读状态
    public class Unit07WorkOrder06Bridge : MonoBehaviour, IDockServiceCompletionGate, IDockClickBlocker
    {
        [SerializeField] Unit07DockController dock;
        [SerializeField] Transform robot;
        [Tooltip("零件盘、磁性零件盒：通电前检查要求都已放回原位。")]
        [SerializeField] DockPickable[] trays = new DockPickable[0];
        [SerializeField] bool showPanel = true;

        const float MaxPanelWidth = 400f, MaxPanelHeight = 420f, Margin = 12f;
        Vector2 scroll;

        public Unit07WorkOrder06Session Session { get; private set; }
        public Unit07DockController DockController => dock;
        public Transform Robot => robot;

        public void Configure(Unit07DockController dockController, Transform robotRoot, DockPickable[] stowables)
        {
            dock = dockController;
            robot = robotRoot;
            trays = stowables ?? new DockPickable[0];
        }

        void Awake()
        {
            EnsureSession();
            if (dock != null) dock.SetServiceCompletionGate(this);
        }

        public void EnsureSession()
        {
            if (Session == null && dock != null) Session = new Unit07WorkOrder06Session(ReadDock);
        }

        public Unit07DockSnapshot ReadDock()
        {
            bool stowed = true;
            foreach (var t in trays) if (t != null && t.Taken) stowed = false;
            return new Unit07DockSnapshot
            {
                State = dock.State,
                PowerOn = dock.PowerOn,
                RotorsAtIdle = dock.Rotors != null && dock.Rotors.IsAtIdleSpeed,
                TraysStowed = stowed,
            };
        }

        void Update() => Session?.Sync();

        public bool CanFinishService(out string reason)
        {
            EnsureSession();
            if (Session == null) { reason = "工单没有接上维修座。"; return false; }
            return Session.CanFinishService(out reason);
        }

        /// <summary>工单步骤用到、但七号模型里找不到的节点名（应为空）。</summary>
        public List<string> MissingModelAnchors()
        {
            var missing = new List<string>();
            EnsureSession();
            if (robot == null || Session == null) { missing.Add("UNIT07 robot"); return missing; }
            var names = new HashSet<string>();
            foreach (var t in robot.GetComponentsInChildren<Transform>(true)) names.Add(t.name);
            foreach (var anchor in Session.Plan.InspectionAnchors)
                if (!names.Contains(anchor)) missing.Add(anchor);
            foreach (var step in Session.Plan.RemovalSteps)
                foreach (var anchor in step.RequiredModelAnchors)
                    if (!names.Contains(anchor) && !missing.Contains(anchor)) missing.Add(anchor);
            return missing;
        }

        // ------------------------------------------------------------------ 面板

        // 面板在右上角，最多占屏幕宽度的 27%、高度的 45%（放不下时滚动），不挡住画面中下部的夹具握把、断电开关，以及七号的左引擎
        Rect PanelRect
        {
            get
            {
                float w = Mathf.Min(MaxPanelWidth, Screen.width * 0.27f), h = Mathf.Min(MaxPanelHeight, Screen.height * 0.45f);
                return new Rect(Screen.width - w - Margin, Margin, w, h);
            }
        }

        public bool BlocksClick(Vector2 screenPos)
        {
            if (!showPanel || Session == null) return false;
            return PanelRect.Contains(new Vector2(screenPos.x, Screen.height - screenPos.y));
        }

        /// <summary>安全故障锁定时只显示故障和维修人员复位，不显示任何工单操作按钮。</summary>
        void DrawFault(Unit07WorkOrder06Session s)
        {
            var prev = GUI.color;
            GUI.color = new Color(1f, 0.55f, 0.45f);
            GUILayout.Label("安全故障（工单已锁定）：" + s.SafetyFault);
            GUI.color = prev;
            GUILayout.Label("维修人员工号：");
            technicianId = GUILayout.TextField(technicianId ?? string.Empty, 32);
            if (GUILayout.Button("维修人员复位安全故障（需断电、叶轮停稳）")) s.ResetSafetyFault(technicianId);
        }

        string technicianId = string.Empty;

        void OnGUI()
        {
            if (!showPanel || Session == null) return;
            var s = Session;
            var r = PanelRect;
            GUI.Box(r, GUIContent.none);
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 6, r.width - 20, r.height - 12));
            GUILayout.Label(Unit07WorkOrder06Session.Title);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("下一步：" + s.NextHint());
            GUILayout.Label(s.LastFeedback);
            GUILayout.Space(4);
            if (s.IsLocked) DrawFault(s);
            else switch (s.Stage)
            {
                case RobotRepairStage.Inspect:
                    foreach (var a in s.Plan.InspectionAnchors)
                        if (!s.IsInspected(a) && GUILayout.Button("检查 " + a)) s.Inspect(a);
                    break;
                case RobotRepairStage.Disassemble:
                    var rm = s.Inner.NextRemoval;
                    if (GUILayout.Button($"拆卸 {rm.Id}：{rm.Label}")) s.Remove(rm.Id);
                    break;
                case RobotRepairStage.RepairChoice:
                    if (GUILayout.Button(Unit07WorkOrder06Session.ChoiceLabel(RobotRepairChoice.RebalanceRotor))) s.ChooseRepair(RobotRepairChoice.RebalanceRotor);
                    if (GUILayout.Button(Unit07WorkOrder06Session.ChoiceLabel(RobotRepairChoice.ReplaceMotorCore))) s.ChooseRepair(RobotRepairChoice.ReplaceMotorCore);
                    break;
                case RobotRepairStage.Reassemble:
                    var ins = s.Inner.NextInstallation;
                    if (GUILayout.Button($"装回 {ins.Id}：{ins.Label}")) s.Install(ins.Id);
                    break;
                case RobotRepairStage.PowerOn:
                    if (!s.PrePowerChecked && GUILayout.Button("通电前检查")) s.RunPrePowerCheck();
                    break;
                case RobotRepairStage.HoverRetest:
                case RobotRepairStage.LoadRetest:
                    bool hover = s.Stage == RobotRepairStage.HoverRetest;
                    GUILayout.Label("复测结果由测试者判定（占位：没有失衡仿真）");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(hover ? "悬停复测：通过" : "负载复测：通过")) { if (hover) s.HoverRetest(true); else s.LoadRetest(true); }
                    if (GUILayout.Button(hover ? "悬停复测：不通过" : "负载复测：不通过")) { if (hover) s.HoverRetest(false); else s.LoadRetest(false); }
                    GUILayout.EndHorizontal();
                    break;
            }
            GUILayout.Space(4);
            GUILayout.Label($"机器人 {Unit07WorkOrder06Session.RobotId} · 工单 {s.CaseId} · 第 {s.RepairCycle + 1} 轮");
            GUILayout.Label($"工单阶段：{Unit07WorkOrder06Session.StageLabel(s.Stage)} · 维修座：{Unit07WorkOrder06Session.DockStateLabel(s.Dock.State)}");
            GUILayout.Label("供电只由维修座的断电开关控制；工单只决定是否放行恢复供电。");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
