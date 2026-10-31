using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BorderRepair.RepairProps.Tests
{
    // The receiver's fault pool (easy diagnosis): one of three faults per order, a signal test that reports only the
    // symptom, a meter on each suspect, and a fix that needs a new part only when the RF board itself is bad.
    public sealed class RepairPropReceiverFaultTests
    {
        [TearDown] public void Unpin() => RepairPropFlow.FaultOverride = 0;

        static PropFlowTestRig Rig(int fault)
        {
            var rig = new PropFlowTestRig(RepairPropKind.Receiver);
            RepairPropFlow.FaultOverride = fault;
            rig.flow.RollFault();
            Assert.That(rig.flow.ActiveFault, Is.SameAs(rig.definition.faults[fault - 1]));
            return rig;
        }

        // Presses whatever the visor guide suggests until the item is delivered; returns the command ids pressed.
        static List<string> FollowGuide(PropFlowTestRig rig)
        {
            var pressed = new List<string>();
            while (!rig.flow.Completed && pressed.Count < 80)
            {
                var next = RepairPropWorkflowStatus.NextCommand(rig.flow);
                Assert.That(next, Is.Not.Null, "Guide went silent: " + RepairPropWorkflowStatus.NextStep(rig.flow));
                Assert.That(rig.flow.TryExecute(next.id, next.target, next.tool), Is.True, next.id + ": " + rig.flow.LastFeedback);
                pressed.Add(next.id);
            }
            Assert.That(rig.flow.Completed, Is.True, "Delivered within 80 suggested steps.");
            return pressed;
        }

        [TestCase(1, "board")]
        [TestCase(2, "coax")]
        [TestCase(3, "lead")]
        public void EachFault_IsFoundByMeasuringAndFixedByFollowingTheGuide(int fault, string id)
        {
            using (var rig = Rig(fault))
            {
                var pressed = FollowGuide(rig);
                int firstMeasure = pressed.FindIndex(p => p.StartsWith("measure."));
                int firstFix = pressed.FindIndex(p => p == "fix.coax" || p == "fix.lead" || p == "module.remove.old");
                Assert.That(firstMeasure, Is.GreaterThan(0), "The guide measures before it repairs.");
                Assert.That(firstFix, Is.GreaterThan(firstMeasure));
                Assert.That(pressed, Does.Contain("measure." + id), "The faulty suspect is measured.");
                Assert.That(rig.flow.GetState("meter." + id), Is.EqualTo(2), "It read abnormal.");
                Assert.That(rig.flow.GetState("module") == 2, Is.EqualTo(id == "board"), "Only a bad board takes the new part.");
                if (id != "board") Assert.That(pressed, Has.No.Member("module.remove.old"), "A good board is never pulled.");
                Assert.That(rig.flow.FailedRetests, Is.Zero);
            }
        }

        [Test]
        public void SignalTest_ReportsTheSymptomAndTheSuspects_NotTheCulprit()
        {
            var symptoms = new HashSet<string>();
            for (int fault = 1; fault <= 3; fault++)
                using (var rig = Rig(fault))
                {
                    rig.Do("test.signal");
                    string said = rig.flow.LastFeedback;
                    Assert.That(said, Does.StartWith(rig.flow.ActiveFault.symptom));
                    Assert.That(said, Does.Contain("用万用表"), "Easy mode names the suspects and the meter.");
                    Assert.That(said, Does.Not.Contain(rig.flow.ActiveFault.label));
                    symptoms.Add(rig.flow.ActiveFault.symptom);
                }
            Assert.That(symptoms.Count, Is.EqualTo(3), "Each fault reads differently.");
        }

        [Test]
        public void Meter_ReadsAbnormalOnlyOnTheActiveFault_AndNormalOnceFixed()
        {
            using (var rig = Rig(2))
            {
                rig.Diagnose();
                rig.Do("power.off"); rig.Do("fixture.disconnect");
                for (int i = 1; i <= 4; i++) rig.Do("remove." + rig.coverScrewPrefix + i.ToString("00"));
                rig.Do("cover.remove");
                rig.Do("measure.board"); Assert.That(rig.flow.GetState("meter.board"), Is.EqualTo(1));
                rig.Do("measure.lead"); Assert.That(rig.flow.GetState("meter.lead"), Is.EqualTo(1));
                rig.Do("measure.coax"); Assert.That(rig.flow.GetState("meter.coax"), Is.EqualTo(2));
                Assert.That(RepairPropWorkflowStatus.FoundFault(rig.flow).id, Is.EqualTo("coax"));
                Assert.That(RepairPropWorkflowStatus.NextStep(rig.flow), Does.StartWith("测量找到原因：射频同轴线接头氧化"));
                rig.Do("isolate"); rig.Do("harness.release"); rig.Do("fix.coax");
                rig.Do("harness.connect");
                rig.Do("measure.coax");
                Assert.That(rig.flow.GetState("meter.coax"), Is.EqualTo(1), "A cleaned contact reads normal.");
            }
        }

        [Test]
        public void WrongFix_FailsTheRetestWithoutCost_AndTheGuideGoesBackToMeasuring()
        {
            using (var rig = Rig(3))
            {
                rig.Diagnose();
                rig.Do("power.off"); rig.Do("fixture.disconnect");
                for (int i = 1; i <= 4; i++) rig.Do("remove." + rig.coverScrewPrefix + i.ToString("00"));
                rig.Do("cover.remove");
                // A guess without the meter: clean the coax, close up, retest.
                rig.Do("isolate"); rig.Do("harness.release"); rig.Do("fix.coax");
                Assert.That(RepairPropWorkflowStatus.Phase(rig.flow), Is.EqualTo(RepairPropPhase.Reassembly));
                rig.Do("harness.connect"); rig.Do("supply.connect"); rig.Do("cover.install");
                for (int i = 1; i <= 4; i++) rig.Do("install." + rig.coverScrewPrefix + i.ToString("00"));
                rig.Do("fixture.connect"); rig.Do("power.on");
                rig.Do("test.signal");
                Assert.That(rig.flow.LastTestPassed, Is.False);
                Assert.That(rig.flow.LastFeedback, Does.StartWith("症状没有变化"));
                Assert.That(rig.flow.FailedRetests, Is.EqualTo(1));
                Assert.That(RepairPropWorkflowStatus.Phase(rig.flow), Is.EqualTo(RepairPropPhase.SafeDisassembly));
                Assert.That(RepairPropWorkflowStatus.NextStep(rig.flow), Does.StartWith("复测没通过，症状还在"));
                var pressed = FollowGuide(rig);
                Assert.That(pressed, Does.Contain("measure.lead").And.Contain("fix.lead"));
                Assert.That(rig.flow.GetState("module"), Is.EqualTo(0), "Still no part used.");
            }
        }

        [Test]
        public void OtherProps_KeepTheirSingleFault()
        {
            foreach (var kind in new[] { RepairPropKind.Cartridge, RepairPropKind.Power, RepairPropKind.Omamori })
                using (var rig = new PropFlowTestRig(kind))
                {
                    Assert.That(rig.definition.HasFaultPool, Is.False, kind.ToString());
                    Assert.That(rig.definition.FindCommand("complete").requires.Any(r => r.key == "module" && r.value == 2), Is.True, kind.ToString());
                }
        }
    }
}
