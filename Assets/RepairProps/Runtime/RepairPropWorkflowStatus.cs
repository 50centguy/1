using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Core;

namespace BorderRepair.RepairProps
{
    public enum RepairPropPhase { Intake, InitialTest, SafeDisassembly, Replacement, Reassembly, Retest, Delivery }
    public enum RepairPropCommandState { Available, Blocked, Current }
    public enum RepairPropTestStatus { NotRun, ExpectedFault, Normal, Passed, Counting, RetestFailed, NeedsRetest }

    // Read-only presentation of the canonical flow for desktop and VR panels. It never writes flow state,
    // executes commands or decides that a test passed; readiness always comes from RepairPropFlow.CanExecute.
    public static class RepairPropWorkflowStatus
    {
        public static readonly string[] PhaseLabels = { "接收", "初测", "拆解", "更换", "复装", "复测", "交付" };
        static readonly string[] DisassemblyIds = { "remove.", "cover.remove", "isolate", "harness.release", "tray.remove", "module.remove.old" };
        static readonly string[] ReassemblyIds = { "module.install.new", "install.", "tray.install", "harness.connect", "supply.connect", "cover.install" };

        public static string Label(RepairPropPhase phase) => PhaseLabels[(int)phase];

        public static RepairPropPhase Phase(RepairPropFlow flow)
        {
            if (flow == null || flow.Definition == null || flow.Session == null) return RepairPropPhase.Intake;
            if (flow.Completed) return RepairPropPhase.Delivery;
            if (flow.Session.Stage != RepairStage.Inspect || flow.Session.CurrentCase != flow.Definition.repairCase) return RepairPropPhase.Intake;
            if (!InitialDiagnosisDone(flow)) return RepairPropPhase.InitialTest;
            if (flow.GetState("module") == 1) return RepairPropPhase.Replacement;
            if (flow.RepairAttempted) return !flow.IsAssembled ? RepairPropPhase.Reassembly : AllTestsPassed(flow) ? RepairPropPhase.Delivery : RepairPropPhase.Retest;
            return RepairPropPhase.SafeDisassembly;
        }

        // ---- Fault pool (symptom-led diagnosis): open up, measure the suspects, then walk the found fault's route. ----

        /// <summary>The fault a meter reading has pointed at, or null while every measured suspect reads normal.</summary>
        public static PropFault FoundFault(RepairPropFlow flow)
        {
            var probe = flow.Definition.probes.FirstOrDefault(p => flow.Reading(p) == 2);
            return probe != null ? flow.Definition.FindFault(probe.fault) : null;
        }

        /// <summary>"射频同轴线接头：异常 · 电池连接线：未测 …", one entry per suspect.</summary>
        public static string SuspectList(RepairPropFlow flow) => string.Join("　", flow.Definition.probes.Select(p =>
            p.suspect + "：" + (flow.Reading(p) == 2 ? "异常" : flow.Reading(p) == 1 ? "正常" : "未测")));

        static bool CoverStep(PropCommand c) => c.id == "cover.remove" ||
            c.id.StartsWith("remove.", StringComparison.Ordinal) && c.requires.Any(r => r.key == "cover" && r.value == 0);

        static IEnumerable<PropCommand> Route(RepairPropFlow flow, PropFault fault) =>
            fault.route.SelectMany(step => flow.Definition.commands.Where(c => c.id == step ||
                (step.EndsWith(".") || step.EndsWith("_")) && c.id.StartsWith(step, StringComparison.Ordinal)));

        // What the guide offers while the item is open and not yet repaired, in order of preference.
        static IEnumerable<PropCommand> PoolOpenSteps(RepairPropFlow flow)
        {
            var d = flow.Definition;
            if (flow.GetState("cover") == 0) return d.commands.Where(CoverStep);
            var found = FoundFault(flow);
            if (found != null) return Route(flow, found);
            var measure = d.commands.Where(c => c.kind == PropCommandKind.Measure && flow.Reading(d.FindProbe(c.probeId)) == 0);
            // A suspect that cannot be measured as things stand (a lead already unplugged) is put back first.
            var putBack = d.commands.Where(c => IsAny(c.id, ReassemblyIds) && c.id != "module.install.new" && c.id != "cover.install" && !CoverScrewInstall(c));
            return measure.Concat(putBack);
        }

        static bool CoverScrewInstall(PropCommand c) => c.id.StartsWith("install.", StringComparison.Ordinal) && c.requires.Any(r => r.key == "cover" && r.value == 0);

        static PropCommand PoolReplacementStep(RepairPropFlow flow)
        {
            // A board taken out on a hunch goes back unless the meter found it bad.
            var found = FoundFault(flow);
            bool replace = found != null && found.route.Contains("module.install.new");
            return flow.Definition.FindCommand(replace ? "module.install.new" : "module.install.old");
        }

        public static bool InitialDiagnosisDone(RepairPropFlow flow) => flow.Definition.tests.All(t => flow.GetState("diagnosed." + t.id) == 1);
        public static bool AllTestsPassed(RepairPropFlow flow) => flow.Definition.tests.All(t => flow.GetState("pass." + t.id) == 1);

        // Each command is evaluated against its own authored target with its own tool, exactly as the panel dispatches it.
        public static RepairPropCommandState CommandState(RepairPropFlow flow, PropCommand command, out string reason)
        {
            if (flow.CanExecute(command.id, command.target, command.tool, out reason)) return RepairPropCommandState.Available;
            return command.effects.Count > 0 && flow.Matches(command.effects) ? RepairPropCommandState.Current : RepairPropCommandState.Blocked;
        }

        public static bool HasAvailableCommand(RepairPropFlow flow, string target) =>
            flow.Definition.commands.Any(c => c.target == target && flow.CanExecute(c.id, c.target, c.tool, out _));

        // One sentence: the next executable step for this phase, or the flow's own first blocking condition.
        public static string NextStep(RepairPropFlow flow)
        {
            var d = flow.Definition;
            switch (Phase(flow))
            {
                case RepairPropPhase.Intake:
                    return flow.Completed ? "" : "到收件台接收物品，拿到维修台上放下后开始。";
                case RepairPropPhase.InitialTest:
                {
                    var pending = d.tests.Where(t => flow.GetState("diagnosed." + t.id) == 0).ToList();
                    var command = d.FindCommand("test." + pending[0].id);
                    string rest = pending.Count > 1 ? "，之后还需“" + string.Join("”“", pending.Skip(1).Select(t => t.label)) + "”" : "";
                    if (flow.CanExecute(command.id, command.target, command.tool, out string reason))
                        return "整件保持组装、接入外部测试连接并通电，运行“" + pending[0].label + "”" + rest + "。初测全部完成后才能拆解。";
                    return reason + (pending.Count > 0 ? "（初测还差 " + pending.Count + " 项）" : "");
                }
                case RepairPropPhase.SafeDisassembly when d.HasFaultPool:
                {
                    bool failed = flow.GetState("retest.failed") == 1 && flow.GetState("last.retest") == 1;
                    if (flow.GetState("power") != 0 || flow.GetState("fixture") != 0)
                        return (failed ? "复测没通过，症状还在。" : "初测完成，症状已记下。") + "关机并解除外部测试连接，开盖用万用表排查。";
                    var steps = PoolOpenSteps(flow).ToList();
                    if (flow.GetState("cover") == 0) return "开盖排查：" + Suggest(flow, steps, "没有可执行的开盖操作。");
                    var found = FoundFault(flow);
                    if (found != null) return "测量找到原因：" + found.label + "。" + Suggest(flow, steps, "");
                    return "用万用表逐个测量嫌疑部位（" + SuspectList(flow) + "）。" + Suggest(flow, steps, "");
                }
                case RepairPropPhase.Replacement when d.HasFaultPool:
                {
                    var step = PoolReplacementStep(flow);
                    if (step.id == "module.install.new" && flow.CommandPolicy != null && !flow.CommandPolicy.ReplacementAvailable)
                    { flow.CanExecute(step.id, step.target, step.tool, out string shortage); return shortage; }
                    return Suggest(flow, new[] { step }, "") is string s && s.Length > 0 ? s : flow.CanExecute(step.id, step.target, step.tool, out string blocker) ? "" : blocker;
                }
                case RepairPropPhase.SafeDisassembly:
                case RepairPropPhase.Replacement:
                {
                    if (flow.GetState("module")==1 && flow.CommandPolicy!=null && !flow.CommandPolicy.ReplacementAvailable)
                    {
                        var install=d.FindCommand("module.install.new");
                        flow.CanExecute(install.id,install.target,install.tool,out string shortage);
                        return shortage;
                    }
                    if (flow.GetState("power") != 0 || flow.GetState("fixture") != 0)
                        return "初测已完成。关机并解除外部测试连接后，才能拆开或移动部件。";
                    return Suggest(flow, flow.Definition.commands.Where(c => IsAny(c.id, DisassemblyIds) || c.id == "module.install.new"),
                        "没有可执行的拆解操作：");
                }
                case RepairPropPhase.Reassembly:
                {
                    var assemble = d.FindCommand("fixture.connect");
                    flow.CanExecute(assemble.id, assemble.target, assemble.tool, out string blocked);
                    return Suggest(flow, d.commands.Where(c => IsAny(c.id, ReassemblyIds)), "") is string s && s.Length > 0 ? s : blocked;
                }
                case RepairPropPhase.Retest:
                {
                    var test = d.tests.FirstOrDefault(t => flow.GetState("pass." + t.id) != 1);
                    var command = d.FindCommand("test." + test.id);
                    string failed = flow.GetState("retest.failed") == 1 ? "上次复测未通过；" : "";
                    if (!flow.CanExecute(command.id, command.target, command.tool, out string reason)) return failed + reason;
                    int count = flow.GetState("count." + test.id);
                    return failed + "运行复测“" + test.label + "”" + (test.consecutivePasses > 1 ? "（连续通过 " + count + "/" + test.consecutivePasses + "）" : "") + "。";
                }
                default:
                    if (flow.Completed) return "复测已通过，原件已验证。到交件台交付。";
                    var complete = d.FindCommand("complete");
                    return flow.CanExecute(complete.id, complete.target, complete.tool, out string why)
                        ? "全部复测通过，可在整件操作中确认交付。" : why;
            }
        }

        /// <summary>
        /// The command the AR overlay points at: the phase's first executable step, in the same order as
        /// <see cref="NextStep"/>, falling back to the first executable power/fixture setup step that unblocks it.
        /// Null when nothing can run (the reason is then <see cref="NextStep"/>'s text).
        /// </summary>
        public static PropCommand NextCommand(RepairPropFlow flow)
        {
            if (flow == null || flow.Definition == null || flow.Completed) return null;
            var d = flow.Definition;
            bool Ready(PropCommand c) => c != null && flow.CanExecute(c.id, c.target, c.tool, out _);
            IEnumerable<PropCommand> Setup(params string[] order) => order.Select(d.FindCommand).Where(Ready);
            switch (Phase(flow))
            {
                case RepairPropPhase.Intake: return null;
                case RepairPropPhase.InitialTest:
                {
                    var test = d.tests.Where(t => flow.GetState("diagnosed." + t.id) == 0).Select(t => d.FindCommand("test." + t.id)).FirstOrDefault(Ready);
                    return test ?? Setup("fixture.connect", "power.on").FirstOrDefault();
                }
                case RepairPropPhase.SafeDisassembly:
                case RepairPropPhase.Replacement:
                    if (flow.GetState("power") != 0 || flow.GetState("fixture") != 0) return Setup("power.off", "fixture.disconnect").FirstOrDefault();
                    if (d.HasFaultPool)
                        return flow.GetState("module") == 1 ? (Ready(PoolReplacementStep(flow)) ? PoolReplacementStep(flow) : null) : PoolOpenSteps(flow).FirstOrDefault(Ready);
                    return d.commands.Where(c => IsAny(c.id, DisassemblyIds) || c.id == "module.install.new").FirstOrDefault(Ready);
                case RepairPropPhase.Reassembly:
                    return d.commands.Where(c => IsAny(c.id, ReassemblyIds)).OrderBy(ReassemblyRank).FirstOrDefault(Ready)
                        ?? Setup("fixture.connect", "power.on").FirstOrDefault();
                case RepairPropPhase.Retest:
                {
                    var test = d.tests.Where(t => flow.GetState("pass." + t.id) != 1).Select(t => d.FindCommand("test." + t.id)).FirstOrDefault(Ready);
                    return test ?? Setup("fixture.connect", "power.on").FirstOrDefault();
                }
                default:
                    var complete = d.FindCommand("complete");
                    return Ready(complete) ? complete : null;
            }
        }

        // Physical build order, inside out: the module, its screws, the tray it rides on and the tray's screws, the
        // harness and supply leads, the cover, the cover screws. Stable within a rank (authoring order).
        static int ReassemblyRank(PropCommand c)
        {
            if (c.id == "module.install.new") return 0;
            if (c.id == "tray.install") return 2;
            if (c.id == "harness.connect") return 4;
            if (c.id == "supply.connect") return 5;
            if (c.id == "cover.install") return 6;
            if (!c.id.StartsWith("install.", StringComparison.Ordinal)) return 8;
            if (c.requires.Any(r => r.key == "cover" && r.value == 0)) return 7;   // cover screws: cover already on
            if (c.requires.Any(r => r.key == "seat")) return 1;                   // module screws: module seated
            return 3;                                                             // tray screws
        }

        static string Suggest(RepairPropFlow flow, IEnumerable<PropCommand> candidates, string none)
        {
            var ready = candidates.Where(c => flow.CanExecute(c.id, c.target, c.tool, out _)).ToList();
            if (ready.Count == 0) return none;
            var node = flow.Definition.FindNode(ready[0].target);
            return "下一步可做：" + ready[0].label + (node != null && node.objectName != flow.Definition.rootObjectName ? " · " + node.displayName : "")
                + (ready.Count > 1 ? "，共 " + ready.Count + " 项可执行" : "") + "。";
        }

        static bool IsAny(string id, string[] prefixes) => prefixes.Any(p => p.EndsWith(".") ? id.StartsWith(p, StringComparison.Ordinal) : id == p);

        public static RepairPropTestStatus TestStatus(RepairPropFlow flow, PropTest test, RepairPropTestJournal journal)
        {
            if (flow.GetState("pass." + test.id) == 1) return RepairPropTestStatus.Passed;
            if (flow.GetState("count." + test.id) > 0) return RepairPropTestStatus.Counting;
            if (flow.GetState("diagnosed." + test.id) == 0) return RepairPropTestStatus.NotRun;
            var last = journal?.Last(test.id);
            if (last == null) return RepairPropTestStatus.NeedsRetest;
            if (!last.retest) return last.passed ? RepairPropTestStatus.Normal : RepairPropTestStatus.ExpectedFault;
            return last.passed ? RepairPropTestStatus.NeedsRetest : RepairPropTestStatus.RetestFailed;
        }

        public static string TestStatusLabel(RepairPropFlow flow, PropTest test, RepairPropTestStatus status)
        {
            switch (status)
            {
                case RepairPropTestStatus.NotRun: return "未测";
                case RepairPropTestStatus.ExpectedFault: return "初测发现故障（已诊断）";
                case RepairPropTestStatus.Normal: return "初测正常";
                case RepairPropTestStatus.Passed: return "通过";
                case RepairPropTestStatus.Counting: return "已通过 " + flow.GetState("count." + test.id) + "/" + test.consecutivePasses;
                case RepairPropTestStatus.RetestFailed: return "复测未通过";
                default: return "需复测";
            }
        }
    }

    // UI-side record of measured outcomes. The flow clears pass flags on power-off, so this keeps what each test
    // actually reported for display. It subscribes to the flow; it never feeds results back into it.
    public sealed class RepairPropTestJournal : IDisposable
    {
        public sealed class Entry
        {
            public string testId, label, message;
            public bool passed, retest;
        }
        readonly RepairPropFlow flow;
        readonly List<Entry> entries = new List<Entry>();
        int seen;
        public IReadOnlyList<Entry> Entries => entries;
        public event Action Recorded;

        public RepairPropTestJournal(RepairPropFlow flow)
        {
            this.flow = flow;
            seen = flow.History.Count;
            flow.Changed += OnChanged;
        }

        public Entry Last(string testId)
        {
            for (int i = entries.Count - 1; i >= 0; i--) if (entries[i].testId == testId) return entries[i];
            return null;
        }

        void OnChanged()
        {
            var history = flow.History;
            for (; seen < history.Count; seen++)
            {
                string id = history[seen];
                if (!id.StartsWith("test.", StringComparison.Ordinal) || seen != history.Count - 1) continue;
                var test = flow.Definition.FindTest(id.Substring(5));
                if (test == null) continue;
                entries.Add(new Entry { testId = test.id, label = test.label, message = flow.LastFeedback,
                    passed = flow.LastTestPassed, retest = flow.GetState("last.retest") == 1 });
                Recorded?.Invoke();
            }
        }

        public void Dispose() { if (flow != null) flow.Changed -= OnChanged; }
    }
}
