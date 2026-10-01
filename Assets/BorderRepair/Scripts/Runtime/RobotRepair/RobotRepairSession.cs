using System;
using System.Collections.Generic;

namespace BorderRepair.RobotRepair
{
    public enum RobotRepairStage
    {
        Dock, PowerOff, Inspect, Disassemble, RepairChoice, Reassemble,
        PowerOn, HoverRetest, LoadRetest, Complete
    }

    public enum RobotRepairAction
    {
        Dock, PowerOff, Inspect, Remove, ChooseRepair, Install,
        PowerOn, HoverRetest, LoadRetest
    }

    public enum RobotRepairChoice { RebalanceRotor, ReplaceMotorCore }

    public struct RobotRepairCommand
    {
        public RobotRepairAction Action;
        public string TargetId;
        public RobotRepairChoice Choice;
        public bool Passed;

        public static RobotRepairCommand For(RobotRepairAction action, string targetId = null)
            => new RobotRepairCommand { Action = action, TargetId = targetId };

        public static RobotRepairCommand Choose(RobotRepairChoice choice)
            => new RobotRepairCommand { Action = RobotRepairAction.ChooseRepair, Choice = choice };

        public static RobotRepairCommand Retest(RobotRepairAction action, bool passed)
            => new RobotRepairCommand { Action = action, Passed = passed };
    }

    public struct RobotRepairEvent
    {
        public RobotRepairAction Action;
        public string TargetId;
        public RobotRepairChoice Choice;
        public bool Passed;
        public RobotRepairStage StageAfter;
    }

    // The command source may be an IMGUI mouse panel, a future VR controller, or a test driver.
    public interface IRobotRepairCommandSource
    {
        event Action<RobotRepairCommand> CommandIssued;
    }

    public sealed class RobotRepairSession
    {
        readonly HashSet<string> inspected = new HashSet<string>();
        readonly List<RobotRepairEvent> events = new List<RobotRepairEvent>();
        int removedCount;
        int installedCount;
        bool retry;

        public readonly RobotRepairPlan Plan;
        public RobotRepairStage Stage { get; private set; } = RobotRepairStage.Dock;
        public RobotRepairChoice? Choice { get; private set; }
        public bool? HoverPassed { get; private set; }
        public bool? LoadPassed { get; private set; }
        public int RepairCycle { get; private set; }
        public string LastFeedback { get; private set; }
        public IReadOnlyList<RobotRepairEvent> Events => events;
        public int InspectedCount => inspected.Count;
        public RobotRepairStep NextRemoval => Stage == RobotRepairStage.Disassemble ? Plan.RemovalSteps[removedCount] : null;
        public RobotRepairStep NextInstallation => Stage == RobotRepairStage.Reassemble ? Plan.RemovalSteps[Plan.RemovalSteps.Length - 1 - installedCount] : null;

        public event Action<RobotRepairStage> StageChanged;
        public event Action<string> Feedback;

        public RobotRepairSession(RobotRepairPlan plan)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            LastFeedback = "请将六号停靠到维修位。";
        }

        public bool Apply(RobotRepairCommand command)
        {
            switch (command.Action)
            {
                case RobotRepairAction.Dock:
                    if (Stage != RobotRepairStage.Dock) return Reject("当前无法停靠。");
                    return Accept(command, RobotRepairStage.PowerOff, "已停靠。检查前请先断电。");
                case RobotRepairAction.PowerOff:
                    if (Stage != RobotRepairStage.PowerOff) return Reject("当前无法断电。");
                    return Accept(command, retry ? RobotRepairStage.Disassemble : RobotRepairStage.Inspect,
                        retry ? "已断电。重新处理左引擎。" : "已断电。检查左引擎。");
                case RobotRepairAction.Inspect:
                    if (Stage != RobotRepairStage.Inspect) return Reject("检查前需停靠并断电。");
                    if (Array.IndexOf(Plan.InspectionAnchors, command.TargetId) < 0) return Reject("未知检查点。");
                    if (!inspected.Add(command.TargetId)) return Reject("该部位已检查。");
                    return Accept(command, inspected.Count == Plan.InspectionAnchors.Length ? RobotRepairStage.Disassemble : Stage,
                        "已记录检查：" + command.TargetId);
                case RobotRepairAction.Remove:
                    if (Stage != RobotRepairStage.Disassemble) return Reject("当前无法拆卸。");
                    if (command.TargetId != NextRemoval.Id) return Reject("请按部件表顺序操作，下一步：" + NextRemoval.Id);
                    removedCount++;
                    return Accept(command, removedCount == Plan.RemovalSteps.Length ? RobotRepairStage.RepairChoice : Stage,
                        "已完成拆卸步骤 " + command.TargetId + "。");
                case RobotRepairAction.ChooseRepair:
                    if (Stage != RobotRepairStage.RepairChoice) return Reject("露出马达芯后才能选择维修方式。");
                    if (!Enum.IsDefined(typeof(RobotRepairChoice), command.Choice)) return Reject("未知维修方式。");
                    Choice = command.Choice;
                    installedCount = 0;
                    return Accept(command, RobotRepairStage.Reassemble, "已选择维修方式。请按拆卸逆序装回。");
                case RobotRepairAction.Install:
                    if (Stage != RobotRepairStage.Reassemble) return Reject("当前无法装回。");
                    if (command.TargetId != NextInstallation.Id) return Reject("请按逆序装回，下一步：" + NextInstallation.Id);
                    installedCount++;
                    return Accept(command, installedCount == Plan.RemovalSteps.Length ? RobotRepairStage.PowerOn : Stage,
                        "已装回步骤 " + command.TargetId + "。");
                case RobotRepairAction.PowerOn:
                    if (Stage != RobotRepairStage.PowerOn) return Reject("装回完成后才能恢复供电。");
                    return Accept(command, RobotRepairStage.HoverRetest, "已恢复供电。请进行悬停复测。");
                case RobotRepairAction.HoverRetest:
                    if (Stage != RobotRepairStage.HoverRetest) return Reject("当前无法进行悬停复测。");
                    HoverPassed = command.Passed;
                    if (!command.Passed) PrepareRetry();
                    return Accept(command, command.Passed ? RobotRepairStage.LoadRetest : RobotRepairStage.Dock,
                        command.Passed ? "悬停稳定。请进行负载复测。" : "悬停仍不稳定。请再次停靠、断电并维修。");
                case RobotRepairAction.LoadRetest:
                    if (Stage != RobotRepairStage.LoadRetest) return Reject("悬停复测通过后才能进行负载复测。");
                    LoadPassed = command.Passed;
                    if (!command.Passed) PrepareRetry();
                    return Accept(command, command.Passed ? RobotRepairStage.Complete : RobotRepairStage.Dock,
                        command.Passed ? "悬停与负载复测通过，工单完成。" : "负载下仍失衡。请再次停靠、断电并维修。");
                default:
                    return Reject("未知操作。");
            }
        }

        void PrepareRetry()
        {
            retry = true;
            RepairCycle++;
            removedCount = 0;
            installedCount = 0;
            Choice = null;
        }

        bool Accept(RobotRepairCommand command, RobotRepairStage next, string message)
        {
            if (next != Stage)
            {
                Stage = next;
                StageChanged?.Invoke(Stage);
            }
            events.Add(new RobotRepairEvent
            {
                Action = command.Action, TargetId = command.TargetId, Choice = command.Choice,
                Passed = command.Passed, StageAfter = Stage
            });
            LastFeedback = message;
            Feedback?.Invoke(message);
            return true;
        }

        bool Reject(string message)
        {
            LastFeedback = message;
            Feedback?.Invoke(message);
            return false;
        }
    }
}
