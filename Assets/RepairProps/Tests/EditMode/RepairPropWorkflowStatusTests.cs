using System.Collections.Generic;
using System.Linq;
using BorderRepair.UI;
using NUnit.Framework;
using UnityEngine;

namespace BorderRepair.RepairProps.Tests
{
    // Block wording, phase and test-record presentation over the real catalog definitions. Readiness itself is the
    // unchanged CanExecute; these tests only check what the player is told and that nothing becomes runnable early.
    public sealed class RepairPropWorkflowStatusTests
    {
        const string OldGeneric = "请检查诊断、拆装入口、接头与固定件";

        [Test]
        public void Receiver_DisconnectedBeforeDiagnosis_ExplainsConnectPowerAndBothInitialTests()
        {
            using (var rig = new PropFlowTestRig(RepairPropKind.Receiver))
            {
                var flow = rig.flow;
                Assert.That(RepairPropWorkflowStatus.Phase(flow), Is.EqualTo(RepairPropPhase.InitialTest));
                // The player's mistake from the field report: switching off and unplugging before testing.
                rig.Do("power.off");
                rig.Do("fixture.disconnect");
                string screw = rig.coverScrewPrefix + "01";
                Assert.That(Reason(rig, "remove." + screw), Does.StartWith("初始诊断未完成"));
                Assert.That(Reason(rig, "remove." + screw), Does.Contain("标准信号与锁频测试").And.Contain("只读核对原调谐记录身份"));
                Assert.That(Reason(rig, "remove." + screw), Does.Contain("接入外部测试连接"), "Must say to reconnect, not to power off again.");
                Assert.That(Reason(rig, "test.signal"), Does.StartWith("外部测试连接未接入"));
                Assert.That(RepairPropWorkflowStatus.NextStep(flow), Does.StartWith("外部测试连接未接入"));
                Assert.That(Reason(rig, "power.on"), Does.StartWith("外部测试连接未接入"));

                rig.Do("fixture.connect");
                Assert.That(Reason(rig, "test.signal"), Does.StartWith("测试供电未开启"));
                Assert.That(Reason(rig, "remove." + screw), Does.Contain("开启测试供电"));
                rig.Do("power.on");
                Assert.That(Can(rig, "test.signal"), Is.True, "Connected and powered: the initial signal test is runnable.");
                Assert.That(Can(rig, "remove." + screw), Is.False);

                rig.Do("test.signal");
                Assert.That(flow.LastTestPassed, Is.False, "The receiver's first signal test is the expected fault.");
                var signal = rig.definition.FindTest("signal");
                var journal = new RepairPropTestJournal(flow);
                try
                {
                    // A journal attached after the fact has no record; it must not invent a pass or a fault.
                    Assert.That(RepairPropWorkflowStatus.TestStatus(flow, signal, journal), Is.EqualTo(RepairPropTestStatus.NeedsRetest));
                }
                finally { journal.Dispose(); }
                Assert.That(Reason(rig, "remove." + screw), Does.Contain("只读核对原调谐记录身份").And.Not.Contain("标准信号与锁频测试"));
                Assert.That(RepairPropWorkflowStatus.Phase(flow), Is.EqualTo(RepairPropPhase.InitialTest));
                rig.Do("test.identity");
                Assert.That(RepairPropWorkflowStatus.Phase(flow), Is.EqualTo(RepairPropPhase.SafeDisassembly));
                Assert.That(Reason(rig, "remove." + screw), Is.EqualTo("请先关机并解除外部测试连接，再拆开或移动部件。"));
                rig.Do("power.off");
                Assert.That(Reason(rig, "remove." + screw), Is.EqualTo("请先解除外部测试连接，再拆开或移动部件。"));
                rig.Do("fixture.disconnect");
                Assert.That(Can(rig, "remove." + screw), Is.True);
                Assert.That(RepairPropWorkflowStatus.NextStep(flow), Does.Contain("卸下盖板螺钉1"));
            }
        }

        [TestCase(RepairPropKind.Receiver)]
        [TestCase(RepairPropKind.Cartridge)]
        [TestCase(RepairPropKind.Power)]
        [TestCase(RepairPropKind.Omamori)]
        public void EveryBlockedCommandAlongTheRepairNamesASpecificCondition(RepairPropKind kind)
        {
            using (var rig = new PropFlowTestRig(kind))
            {
                var journal = new RepairPropTestJournal(rig.flow);
                try
                {
                    var phases = new List<RepairPropPhase>();
                    void Check(string step)
                    {
                        phases.Add(RepairPropWorkflowStatus.Phase(rig.flow));
                        Assert.That(RepairPropWorkflowStatus.NextStep(rig.flow), Is.Not.Empty, step);
                        foreach (var c in rig.definition.commands)
                        {
                            var state = RepairPropWorkflowStatus.CommandState(rig.flow, c, out string reason);
                            if (state == RepairPropCommandState.Available) { Assert.That(reason, Is.Null); continue; }
                            Assert.That(reason, Is.Not.Null.And.Not.Empty, step + "/" + c.id);
                            Assert.That(reason, Does.Not.Contain(OldGeneric), step + "/" + c.id);
                            Assert.That(reason, Does.Not.StartWith("尚不能"), step + "/" + c.id + ": " + reason);
                        }
                    }
                    Check("intake");
                    Assert.That(rig.Try("complete"), Is.False);
                    rig.Diagnose(); Check("diagnosed");
                    foreach (var t in rig.definition.tests)
                    {
                        var status = RepairPropWorkflowStatus.TestStatus(rig.flow, t, journal);
                        // A later failing initial test clears every pass flag, so a good result may read Normal.
                        if (t.functional) Assert.That(status, Is.EqualTo(RepairPropTestStatus.ExpectedFault), t.id);
                        else Assert.That(status, Is.EqualTo(RepairPropTestStatus.Passed).Or.EqualTo(RepairPropTestStatus.Normal), t.id);
                        Assert.That(RepairPropWorkflowStatus.TestStatusLabel(rig.flow, t, status), t.functional ? (NUnit.Framework.Constraints.IResolveConstraint)Does.Contain("故障") : Does.Not.Contain("故障"), t.id);
                    }
                    rig.OpenToModule(); Check("module out");
                    // Power-off clears pass flags; the recorded initial result must still read as a diagnosis.
                    foreach (var t in rig.definition.tests)
                        Assert.That(RepairPropWorkflowStatus.TestStatus(rig.flow, t, journal),
                            Is.EqualTo(t.functional ? RepairPropTestStatus.ExpectedFault : RepairPropTestStatus.Normal), t.id);
                    Assert.That(RepairPropWorkflowStatus.Phase(rig.flow), Is.EqualTo(RepairPropPhase.Replacement));
                    rig.Do("module.install.new");
                    Assert.That(RepairPropWorkflowStatus.Phase(rig.flow), Is.EqualTo(RepairPropPhase.Reassembly));
                    Assert.That(Reason(rig, "fixture.connect"), Does.StartWith("组装未完成"));
                    Assert.That(Reason(rig, "test." + rig.definition.tests[0].id), Does.StartWith("组装未完成"));
                    Check("replacement in");
                    // PropFlowTestRig.CloseWithReplacement without its first step (the module is already in).
                    for (int i = 1; i <= 2; i++) rig.Do("install." + rig.moduleScrewPrefix + i.ToString("00"));
                    if (kind == RepairPropKind.Power) { rig.Do("tray.install"); rig.Do("install.PS_TrayScrew_01"); rig.Do("install.PS_TrayScrew_02"); }
                    rig.Do("harness.connect");
                    if (kind != RepairPropKind.Cartridge) rig.Do("supply.connect");
                    rig.Do("cover.install");
                    for (int i = 1; i <= 4; i++) rig.Do("install." + rig.coverScrewPrefix + i.ToString("00"));
                    Assert.That(RepairPropWorkflowStatus.Phase(rig.flow), Is.EqualTo(RepairPropPhase.Retest), "Assembled with the replacement: retest phase.");
                    Assert.That(Reason(rig, "test." + rig.definition.tests[0].id), Does.StartWith("外部测试连接未接入"));
                    rig.Do("fixture.connect");
                    Assert.That(Reason(rig, "test." + rig.definition.tests[0].id), Does.StartWith("测试供电未开启"));
                    rig.Do("power.on");
                    Check("closed");
                    Assert.That(RepairPropWorkflowStatus.Phase(rig.flow), Is.EqualTo(RepairPropPhase.Retest));
                    Assert.That(Reason(rig, "complete"), Is.Not.Empty);
                    Assert.That(rig.Try("complete"), Is.False, "Delivery confirmation stays closed before every retest passes.");
                    rig.Retest(); Check("retested");
                    Assert.That(RepairPropWorkflowStatus.Phase(rig.flow), Is.EqualTo(RepairPropPhase.Delivery));
                    foreach (var t in rig.definition.tests)
                        Assert.That(RepairPropWorkflowStatus.TestStatus(rig.flow, t, journal), Is.EqualTo(RepairPropTestStatus.Passed));
                    Assert.That(journal.Entries.Where(e => e.retest).All(e => e.passed), Is.True);
                    Assert.That(journal.Entries.Any(e => !e.retest && !e.passed), Is.True, "The initial expected fault is kept as a record.");
                    rig.Do("complete");
                    Assert.That(RepairPropWorkflowStatus.Phase(rig.flow), Is.EqualTo(RepairPropPhase.Delivery));
                    CollectionAssert.IsOrdered(phases.Select(p => (int)p));
                }
                finally { journal.Dispose(); }
            }
        }

        [Test]
        public void FailedRetestIsShownAsFailureAndAlreadyDoneCommandsReadAsCurrentState()
        {
            using (var rig = new PropFlowTestRig(RepairPropKind.Cartridge))
            {
                var journal = new RepairPropTestJournal(rig.flow);
                try
                {
                    rig.Diagnose(); rig.OpenToModule(); rig.CloseWithReplacement();
                    rig.flow.TestProbe = new FailProbe();
                    var test = rig.definition.tests[0];
                    rig.Do("test." + test.id);
                    rig.flow.TestProbe = null;
                    Assert.That(RepairPropWorkflowStatus.TestStatus(rig.flow, test, journal), Is.EqualTo(RepairPropTestStatus.RetestFailed));
                    Assert.That(RepairPropWorkflowStatus.NextStep(rig.flow), Does.StartWith("上次复测未通过"));
                    var on = rig.definition.FindCommand("power.on");
                    Assert.That(RepairPropWorkflowStatus.CommandState(rig.flow, on, out string reason), Is.EqualTo(RepairPropCommandState.Current));
                    Assert.That(reason, Is.EqualTo("测试供电已开启。"));
                    var install = rig.definition.FindCommand("install." + rig.coverScrewPrefix + "01");
                    Assert.That(RepairPropWorkflowStatus.CommandState(rig.flow, install, out reason), Is.EqualTo(RepairPropCommandState.Current));
                    Assert.That(reason, Does.StartWith("无需操作"));
                }
                finally { journal.Dispose(); }
            }
        }

        [Test]
        public void LegacyHudSuppressionIsVisualOnlyReferenceCountedAndRestoresExactly()
        {
            var root = new GameObject("LegacyHudTest");
            try
            {
                var view = root.AddComponent<RepairUIView>();
                foreach (string name in RepairPropLegacyHud.Panels) new GameObject(name, typeof(RectTransform)).transform.SetParent(root.transform, false);
                var scan = new GameObject("ScanOverlay", typeof(RectTransform)); scan.transform.SetParent(root.transform, false);
                var authored = root.transform.Find("Feedback").gameObject.AddComponent<CanvasGroup>();
                authored.alpha = .5f; authored.interactable = false; authored.blocksRaycasts = true;
                object first = new object(), second = new object();
                RepairPropLegacyHud.Acquire(view, first);
                RepairPropLegacyHud.Acquire(view, second);
                Assert.That(view.enabled && view.gameObject.activeSelf, Is.True, "The view itself is never disabled.");
                foreach (string name in RepairPropLegacyHud.Panels)
                {
                    var group = root.transform.Find(name).GetComponent<CanvasGroup>();
                    Assert.That(group, Is.Not.Null, name);
                    Assert.That(group.alpha, Is.Zero, name);
                    Assert.That(group.blocksRaycasts, Is.False, name);
                    Assert.That(group.gameObject.activeSelf, Is.True, name);
                }
                Assert.That(scan.GetComponent<CanvasGroup>(), Is.Null, "The scan overlay stays untouched.");
                RepairPropLegacyHud.Release(view, first);
                Assert.That(RepairPropLegacyHud.IsSuppressed(view), Is.True, "A remaining workspace keeps the suppression.");
                RepairPropLegacyHud.Release(view, second);
                Assert.That(RepairPropLegacyHud.IsSuppressed(view), Is.False);
                foreach (string name in RepairPropLegacyHud.Panels.Where(n => n != "Feedback"))
                    Assert.That(root.transform.Find(name).GetComponent<CanvasGroup>(), Is.Null, name + " added group must be removed.");
                Assert.That(authored.alpha, Is.EqualTo(.5f));
                Assert.That(authored.interactable, Is.False);
                Assert.That(authored.blocksRaycasts, Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static bool Can(PropFlowTestRig rig, string id)
        {
            var c = rig.definition.FindCommand(id);
            return rig.flow.CanExecute(id, c.target, c.tool, out _);
        }

        static string Reason(PropFlowTestRig rig, string id)
        {
            var c = rig.definition.FindCommand(id);
            Assert.That(rig.flow.CanExecute(id, c.target, c.tool, out string reason), Is.False, id + " must be blocked here.");
            return reason;
        }

        sealed class FailProbe : IRepairPropTestProbe
        {
            public PropTestResult Measure(RepairPropFlow flow, PropTest test) => new PropTestResult(false);
        }
    }
}
