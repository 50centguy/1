using System;
using System.Collections.Generic;
using BorderRepair.Dock;
using BorderRepair.RobotRepair;

namespace BorderRepair.Unit07WorkOrder
{
    /// <summary>某一时刻维修座的真实状态（由场景桥接从 Unit07DockController 读取；测试可以直接构造）。</summary>
    public struct Unit07DockSnapshot
    {
        public DockState State;
        public bool PowerOn;
        /// <summary>通电且转子在悬停转速（维修座驱动或已交还 Animator）。</summary>
        public bool RotorsAtIdle;
        /// <summary>零件盘、磁性零件盒都在原位（没有被取下拿在手上）。</summary>
        public bool TraysStowed;
    }

    public struct Unit07WorkOrderLogEntry
    {
        public string RobotId;
        public string CaseId;
        public int RepairCycle;
        public string Action;
        public string Target;
        public bool Accepted;
        public string Message;
        public DockState DockState;
        public bool DockPowerOn;
        public RobotRepairStage StageAfter;
    }

    /// <summary>
    /// 工单 06“七号机器人左引擎失衡”接入 UNIT 07 维修座。
    /// 分工：
    /// - 维修座（<see cref="Unit07DockController"/>）是供电的唯一决定者：停靠、夹具、断电 / 恢复供电、转子、离座都只在维修座上操作。
    /// - 本工单负责检查、拆卸顺序、维修选择、装回、通电前检查、悬停 / 负载复测；规则沿用原样导入的 <see cref="RobotRepairSession"/>。
    ///   工单从不调用维修座的供电接口，只通过 <see cref="IDockServiceCompletionGate"/> 回答“能不能恢复供电”。
    /// 工单里的“停靠 / 断电 / 恢复供电”三步不由玩家点按钮，而是由 <see cref="Sync"/> 从维修座的真实状态同步：
    /// - 停靠：维修座夹紧（Clamped / SpinningDown / RotorsStopped）；
    /// - 断电：维修座断电且叶轮停稳（RotorsStopped）；
    /// - 恢复供电：通电前检查已完成，维修座经本接口放行后通电。
    /// 安全规则：检查、拆卸、维修选择、装回、通电前检查只在维修座 RotorsStopped 时允许；
    /// 悬停 / 负载复测只在七号离座悬停（Undocked、转子在悬停转速）后允许；复测失败后回到停靠，重新落座、夹紧、断电后重新拆卸。
    /// 安全故障（<see cref="SafetyFault"/>）：从检查到通电前检查通过之前，看到维修座通电（只可能是绕过了本接口）。故障期间锁定：
    /// 工单阶段冻结（不再从维修座同步推进）；所有工单操作被拒绝；不放行恢复供电；已通过的通电前检查作废。
    /// 只有维修人员能复位（<see cref="ResetSafetyFault"/>）：填写工号，且维修座已断电、叶轮停稳。复位后从原阶段继续，通电前检查必须重做。
    /// </summary>
    public sealed class Unit07WorkOrder06Session : IDockServiceCompletionGate
    {
        public const string RobotId = "UNIT07";
        public const string ExpectedCaseId = "case_06_left_engine_imbalance";
        public const string Title = "工单 06：七号机器人左引擎失衡";

        readonly Func<Unit07DockSnapshot> readDock;
        readonly HashSet<string> inspected = new HashSet<string>();
        readonly List<Unit07WorkOrderLogEntry> log = new List<Unit07WorkOrderLogEntry>();
        Unit07DockSnapshot dock;

        public RobotRepairSession Inner { get; }
        public RobotRepairPlan Plan => Inner.Plan;
        public string CaseId => Inner.Plan.CaseId;
        public RobotRepairStage Stage => Inner.Stage;
        public int RepairCycle => Inner.RepairCycle;
        public bool PrePowerChecked { get; private set; }
        public string LastFeedback { get; private set; }
        /// <summary>检测到维修座绕过本接口恢复了供电（接线错误）。不为 null 时工单锁定，见类说明。正常流程中始终为 null。</summary>
        public string SafetyFault { get; private set; }
        public bool IsLocked => SafetyFault != null;
        /// <summary>本工单累计发生过的安全故障次数（复位后不清零）。</summary>
        public int FaultCount { get; private set; }
        /// <summary>最近一次复位安全故障的维修人员工号。</summary>
        public string LastResetBy { get; private set; }
        public Unit07DockSnapshot Dock => dock;
        public IReadOnlyList<Unit07WorkOrderLogEntry> Log => log;
        public bool IsInspected(string anchor) => inspected.Contains(anchor);

        public Unit07WorkOrder06Session(Func<Unit07DockSnapshot> readDock)
        {
            this.readDock = readDock ?? throw new ArgumentNullException(nameof(readDock));
            Inner = new RobotRepairSession(RobotRepairPlan.SixLeftEngine());
            if (Inner.Plan.CaseId != ExpectedCaseId) throw new InvalidOperationException("工单 ID 不是 " + ExpectedCaseId);
            // 原样导入的会话初始提示写的是“六号”；七号接入不改导入文件，由这里给出七号的提示
            LastFeedback = "工单 06：七号左引擎失衡。请让七号落座并夹紧。";
            dock = readDock();
        }

        // ------------------------------------------------------------------ 从维修座同步

        /// <summary>读取维修座的真实状态，推进工单里的“停靠 / 断电 / 恢复供电”。场景每帧调用；每个玩家操作前也会调用。</summary>
        public void Sync()
        {
            dock = readDock();
            if (IsLocked) return;   // 故障锁定：只读维修座状态，工单阶段冻结
            if (Stage == RobotRepairStage.Dock && IsClampedOnDock(dock.State))
                Advance(RobotRepairAction.Dock, "七号已夹紧在维修座上。拨断电开关 OFF，等叶轮停稳后开始。");
            if (Stage == RobotRepairStage.PowerOff && dock.State == DockState.RotorsStopped && !dock.PowerOn)
                Advance(RobotRepairAction.PowerOff, RepairCycle > 0
                    ? "已断电、叶轮停稳。第 " + (RepairCycle + 1) + " 轮：按部件表顺序重新拆卸左引擎。"
                    : "已断电、叶轮停稳。检查左引擎的 " + Plan.InspectionAnchors.Length + " 个部位。");
            if (dock.PowerOn && IsWorkOpen(Stage))
            {
                if (Stage == RobotRepairStage.PowerOn && PrePowerChecked)
                    Advance(RobotRepairAction.PowerOn, "已恢复供电。转子回到正常转速后松开夹具、按 L 离座，再做悬停复测。");
                else
                    Fault("维修座在工单放行前恢复了供电（工单阶段：" + StageLabel(Stage) + "）。检查“允许结束维修”接口的接线。");
            }
        }

        static bool IsClampedOnDock(DockState s) => s == DockState.Clamped || s == DockState.SpinningDown || s == DockState.RotorsStopped;

        /// <summary>工单处在“必须断电”的阶段：从检查到通电前。</summary>
        static bool IsWorkOpen(RobotRepairStage s) => s == RobotRepairStage.Inspect || s == RobotRepairStage.Disassemble ||
                                                      s == RobotRepairStage.RepairChoice || s == RobotRepairStage.Reassemble ||
                                                      s == RobotRepairStage.PowerOn;

        void Advance(RobotRepairAction action, string message)
        {
            bool ok = Inner.Apply(RobotRepairCommand.For(action));
            Record(action.ToString(), null, ok, ok ? message : Inner.LastFeedback);
        }

        void Fault(string message)
        {
            if (IsLocked) return;
            SafetyFault = message;
            FaultCount++;
            PrePowerChecked = false;   // 意外通电过，之前的通电前检查作废
            Record("SafetyFault", null, false, message);
        }

        string LockReason => "安全故障锁定：" + SafetyFault + " 先拨断电开关 OFF、等叶轮停稳，再由维修人员复位。";

        /// <summary>
        /// 维修人员复位安全故障。条件：确有故障、填写了工号、维修座已断电且叶轮停稳。
        /// 复位后工单从原阶段继续（已完成的检查 / 拆装步骤保留），通电前检查必须重做；复位记入工单记录（Target = 工号）。
        /// </summary>
        public bool ResetSafetyFault(string technicianId)
        {
            const string act = "SafetyFaultReset";
            dock = readDock();
            if (!IsLocked) return Reject(act, technicianId, "没有安全故障，不需要复位。");
            if (string.IsNullOrWhiteSpace(technicianId)) return Reject(act, technicianId, "复位安全故障需要填写维修人员工号。");
            if (dock.State != DockState.RotorsStopped || dock.PowerOn)
                return Reject(act, technicianId, "维修座还没到“断电、叶轮停稳”（现在：" + DockStateLabel(dock.State) + "），不能复位。先拨断电开关 OFF。");
            SafetyFault = null;
            LastResetBy = technicianId.Trim();
            PrePowerChecked = false;
            Accept(act, LastResetBy, "维修人员 " + LastResetBy + " 已复位安全故障。从“" + StageLabel(Stage) + "”继续；通电前检查需要重做。");
            Sync();
            return true;
        }

        // ------------------------------------------------------------------ 玩家操作（鼠标面板 / 以后的 VR / 测试）

        public bool Inspect(string anchor)
        {
            if (!BeginStopped("检查左引擎")) return false;
            if (!Forward(RobotRepairCommand.For(RobotRepairAction.Inspect, anchor), "Inspect", anchor)) return false;
            inspected.Add(anchor);
            return true;
        }

        public bool Remove(string stepId)
        {
            if (!BeginStopped("拆卸")) return false;
            return Forward(RobotRepairCommand.For(RobotRepairAction.Remove, stepId), "Remove", stepId);
        }

        public bool ChooseRepair(RobotRepairChoice choice)
        {
            if (!BeginStopped("维修")) return false;
            return Forward(RobotRepairCommand.Choose(choice), "ChooseRepair", choice.ToString());
        }

        public bool Install(string stepId)
        {
            if (!BeginStopped("装回")) return false;
            return Forward(RobotRepairCommand.For(RobotRepairAction.Install, stepId), "Install", stepId);
        }

        /// <summary>通电前检查：装回全部完成、叶轮仍停稳、零件盘和磁性零件盒已放回原位。通过后本接口才放行恢复供电。</summary>
        public bool RunPrePowerCheck()
        {
            const string act = "PrePowerCheck";
            if (!BeginStopped("做通电前检查")) return false;
            if (Stage != RobotRepairStage.PowerOn) return Reject(act, null, "按逆序装回全部 " + Plan.RemovalSteps.Length + " 步后才能做通电前检查。");
            if (PrePowerChecked) return Reject(act, null, "通电前检查已经通过。拨断电开关 ON 恢复供电。");
            if (!dock.TraysStowed) return Reject(act, null, "零件盘或磁性零件盒没有放回原位，不能通电。");
            PrePowerChecked = true;
            return Accept(act, null, "通电前检查通过：" + Plan.RemovalSteps.Length + " 步已按逆序装回，零件盘、磁性零件盒已放回。可以拨断电开关 ON 恢复供电。");
        }

        /// <summary>悬停复测。passed 由测试者判定（占位：没有失衡仿真）。</summary>
        public bool HoverRetest(bool passed) => Retest(RobotRepairAction.HoverRetest, passed, "悬停复测");

        /// <summary>负载复测。passed 由测试者判定（占位：没有失衡仿真）。</summary>
        public bool LoadRetest(bool passed) => Retest(RobotRepairAction.LoadRetest, passed, "负载复测");

        bool Retest(RobotRepairAction action, bool passed, string what)
        {
            Sync();
            if (IsLocked) return Reject(action.ToString(), passed ? "pass" : "fail", LockReason);
            if (dock.State != DockState.Undocked || !dock.PowerOn || !dock.RotorsAtIdle)
                return Reject(action.ToString(), passed ? "pass" : "fail",
                    what + "要在七号离座悬停后进行（维修座：" + DockStateLabel(dock.State) + "）。");
            if (!Forward(RobotRepairCommand.Retest(action, passed), action.ToString(), passed ? "pass" : "fail")) return false;
            if (!passed)
            {
                PrePowerChecked = false;
                LastFeedback = Inner.LastFeedback + "点夹具握把让七号重新落座。";
            }
            return true;
        }

        bool BeginStopped(string what)
        {
            Sync();
            if (IsLocked) return Reject(what, null, LockReason);
            if (dock.State == DockState.RotorsStopped && !dock.PowerOn) return true;
            string why = dock.State == DockState.SpinningDown ? "叶轮还在减速转动，禁止" + what + "。"
                       : "维修座没有到“断电、叶轮停稳”（现在：" + DockStateLabel(dock.State) + "），不能" + what + "。";
            return Reject(what, null, why);
        }

        bool Forward(RobotRepairCommand cmd, string act, string target)
        {
            bool ok = Inner.Apply(cmd);
            Record(act, target, ok, Inner.LastFeedback);
            return ok;
        }

        bool Accept(string act, string target, string message) { Record(act, target, true, message); return true; }
        bool Reject(string act, string target, string message) { Record(act, target, false, message); return false; }

        void Record(string act, string target, bool ok, string message)
        {
            LastFeedback = message;
            log.Add(new Unit07WorkOrderLogEntry
            {
                RobotId = RobotId, CaseId = CaseId, RepairCycle = RepairCycle, Action = act, Target = target,
                Accepted = ok, Message = message, DockState = dock.State, DockPowerOn = dock.PowerOn, StageAfter = Stage
            });
        }

        // ------------------------------------------------------------------ 维修座的“允许结束维修”

        public bool CanFinishService(out string reason)
        {
            Sync();
            reason = string.Empty;
            if (IsLocked) { reason = "工单 06 " + LockReason; return false; }
            switch (Stage)
            {
                case RobotRepairStage.HoverRetest:
                case RobotRepairStage.LoadRetest:
                case RobotRepairStage.Complete:
                    return true;   // 装回和通电前检查已完成，期间没有再拆
                case RobotRepairStage.PowerOn:
                    if (PrePowerChecked) return true;
                    reason = "工单 06：装回已完成，还没做通电前检查。";
                    return false;
                default:
                    reason = "工单 06 未完成（" + StageLabel(Stage) + "）：完成拆卸、维修、按逆序装回并做通电前检查后才能恢复供电。";
                    return false;
            }
        }

        // ------------------------------------------------------------------ 提示

        public string NextHint()
        {
            if (IsLocked)
                return dock.State == DockState.RotorsStopped && !dock.PowerOn
                    ? "安全故障锁定：维修人员填写工号后复位"
                    : "安全故障锁定：先拨断电开关 OFF（维修座），等叶轮停稳";
            switch (Stage)
            {
                case RobotRepairStage.Dock:
                    return RepairCycle > 0 ? "复测未通过：让七号重新落座并夹紧（维修座）" : "让七号落座并夹紧（维修座）";
                case RobotRepairStage.PowerOff:
                    if (dock.State == DockState.SpinningDown) return "等叶轮停稳";
                    return dock.State == DockState.Clamped ? "拨断电开关 OFF（维修座）" : "让七号重新落座并夹紧（维修座）";
                case RobotRepairStage.Inspect:
                    return "检查左引擎：还剩 " + (Plan.InspectionAnchors.Length - inspected.Count) + " 处";
                case RobotRepairStage.Disassemble:
                    return "拆卸 " + Inner.NextRemoval.Id + "：" + Inner.NextRemoval.Label;
                case RobotRepairStage.RepairChoice:
                    return "马达芯已露出：选择维修方式";
                case RobotRepairStage.Reassemble:
                    return "装回 " + Inner.NextInstallation.Id + "：" + Inner.NextInstallation.Label;
                case RobotRepairStage.PowerOn:
                    return PrePowerChecked ? "拨断电开关 ON 恢复供电（维修座）" : "做通电前检查";
                case RobotRepairStage.HoverRetest:
                case RobotRepairStage.LoadRetest:
                    string what = Stage == RobotRepairStage.HoverRetest ? "悬停复测" : "负载复测";
                    if (dock.State == DockState.Undocked) return what;
                    if (dock.State == DockState.Clamped || dock.State == DockState.SpinningUp) return "等转子回到正常转速后点夹具握把松开，再按 L 离座，然后做" + what;
                    if (dock.State == DockState.SeatedOpen) return "按 L 离座，然后做" + what;
                    return "等七号离座悬停，然后做" + what;
                default:
                    return "工单完成";
            }
        }

        public static string ChoiceLabel(RobotRepairChoice c) => c == RobotRepairChoice.RebalanceRotor ? "重新做转子动平衡" : "更换马达芯";

        public static string StageLabel(RobotRepairStage s)
        {
            switch (s)
            {
                case RobotRepairStage.Dock: return "等待停靠";
                case RobotRepairStage.PowerOff: return "等待断电停转";
                case RobotRepairStage.Inspect: return "检查";
                case RobotRepairStage.Disassemble: return "拆卸";
                case RobotRepairStage.RepairChoice: return "选择维修方式";
                case RobotRepairStage.Reassemble: return "装回";
                case RobotRepairStage.PowerOn: return "通电前检查 / 恢复供电";
                case RobotRepairStage.HoverRetest: return "悬停复测";
                case RobotRepairStage.LoadRetest: return "负载复测";
                default: return "完成";
            }
        }

        public static string DockStateLabel(DockState s)
        {
            switch (s)
            {
                case DockState.Hovering: return "悬停未落座";
                case DockState.ClampsOpening: return "夹具张开中";
                case DockState.Descending: return "落座中";
                case DockState.SeatedOpen: return "已落座、夹具张开";
                case DockState.Clamping: return "夹具合拢中";
                case DockState.Clamped: return "已夹紧、通电";
                case DockState.SpinningDown: return "已断电、叶轮减速中";
                case DockState.RotorsStopped: return "已断电、叶轮停稳";
                case DockState.SpinningUp: return "恢复供电、转子加速中";
                case DockState.LiftingOff: return "升起离座中";
                default: return "已离座悬停";
            }
        }
    }
}
