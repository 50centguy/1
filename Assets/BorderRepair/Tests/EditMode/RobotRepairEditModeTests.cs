using System;
using BorderRepair.RobotRepair;
using NUnit.Framework;

namespace BorderRepair.Tests
{
    public class RobotRepairEditModeTests
    {
        static RobotRepairSession InspectedSession()
        {
            var s = new RobotRepairSession(RobotRepairPlan.SixLeftEngine());
            Assert.IsTrue(s.Apply(RobotRepairCommand.For(RobotRepairAction.Dock)));
            Assert.IsTrue(s.Apply(RobotRepairCommand.For(RobotRepairAction.PowerOff)));
            foreach (var anchor in s.Plan.InspectionAnchors)
                Assert.IsTrue(s.Apply(RobotRepairCommand.For(RobotRepairAction.Inspect, anchor)));
            return s;
        }

        static void Disassemble(RobotRepairSession s)
        {
            foreach (var step in s.Plan.RemovalSteps)
                Assert.IsTrue(s.Apply(RobotRepairCommand.For(RobotRepairAction.Remove, step.Id)), step.Id);
        }

        static void Reassemble(RobotRepairSession s)
        {
            for (int i = s.Plan.RemovalSteps.Length - 1; i >= 0; i--)
                Assert.IsTrue(s.Apply(RobotRepairCommand.For(RobotRepairAction.Install, s.Plan.RemovalSteps[i].Id)));
        }

        [Test]
        public void FullWorkOrderFollowsV4RemovalAndReverseAssembly()
        {
            var s = new RobotRepairSession(RobotRepairPlan.SixLeftEngine());
            Assert.AreEqual("case_06_left_engine_imbalance", s.Plan.CaseId);
            CollectionAssert.AreEqual(new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10L", "10La", "10Lb", "10Lc" },
                Array.ConvertAll(s.Plan.RemovalSteps, step => step.Id));
            Assert.IsFalse(s.Apply(RobotRepairCommand.For(RobotRepairAction.PowerOff)));
            Assert.IsTrue(s.Apply(RobotRepairCommand.For(RobotRepairAction.Dock)));
            Assert.IsFalse(s.Apply(RobotRepairCommand.For(RobotRepairAction.Inspect, s.Plan.InspectionAnchors[0])));
            Assert.IsTrue(s.Apply(RobotRepairCommand.For(RobotRepairAction.PowerOff)));
            Assert.IsFalse(s.Apply(RobotRepairCommand.For(RobotRepairAction.Inspect, "Engine_UpperCover_R")));
            foreach (var anchor in s.Plan.InspectionAnchors)
                Assert.IsTrue(s.Apply(RobotRepairCommand.For(RobotRepairAction.Inspect, anchor)));
            Assert.AreEqual(RobotRepairStage.Disassemble, s.Stage);
            Assert.IsFalse(s.Apply(RobotRepairCommand.For(RobotRepairAction.Remove, "10L")));
            Disassemble(s);
            Assert.AreEqual(RobotRepairStage.RepairChoice, s.Stage);
            Assert.IsTrue(s.Apply(RobotRepairCommand.Choose(RobotRepairChoice.RebalanceRotor)));
            Assert.IsFalse(s.Apply(RobotRepairCommand.For(RobotRepairAction.Install, "1")));
            Reassemble(s);
            Assert.AreEqual(RobotRepairStage.PowerOn, s.Stage);
            Assert.IsFalse(s.Apply(RobotRepairCommand.Retest(RobotRepairAction.HoverRetest, true)));
            Assert.IsTrue(s.Apply(RobotRepairCommand.For(RobotRepairAction.PowerOn)));
            Assert.IsFalse(s.Apply(RobotRepairCommand.Retest(RobotRepairAction.LoadRetest, true)));
            Assert.IsTrue(s.Apply(RobotRepairCommand.Retest(RobotRepairAction.HoverRetest, true)));
            Assert.IsTrue(s.Apply(RobotRepairCommand.Retest(RobotRepairAction.LoadRetest, true)));
            Assert.AreEqual(RobotRepairStage.Complete, s.Stage);
            Assert.AreEqual(RobotRepairChoice.RebalanceRotor, s.Choice);
            Assert.IsTrue(s.HoverPassed.Value && s.LoadPassed.Value);
            Assert.IsFalse(s.Apply(RobotRepairCommand.For(RobotRepairAction.Remove, "1")));
            Assert.AreEqual(35, s.Events.Count);
        }

        [TestCase(RobotRepairAction.HoverRetest)]
        [TestCase(RobotRepairAction.LoadRetest)]
        public void FailedRetestRequiresDockAndPowerIsolationBeforeRetry(RobotRepairAction failure)
        {
            var s = InspectedSession();
            Disassemble(s);
            Assert.IsTrue(s.Apply(RobotRepairCommand.Choose(RobotRepairChoice.ReplaceMotorCore)));
            Reassemble(s);
            Assert.IsTrue(s.Apply(RobotRepairCommand.For(RobotRepairAction.PowerOn)));
            if (failure == RobotRepairAction.LoadRetest)
                Assert.IsTrue(s.Apply(RobotRepairCommand.Retest(RobotRepairAction.HoverRetest, true)));
            Assert.IsTrue(s.Apply(RobotRepairCommand.Retest(failure, false)));
            Assert.AreEqual(RobotRepairStage.Dock, s.Stage);
            Assert.AreEqual(1, s.RepairCycle);
            Assert.IsFalse(s.Apply(RobotRepairCommand.For(RobotRepairAction.Remove, "1")));
            Assert.IsTrue(s.Apply(RobotRepairCommand.For(RobotRepairAction.Dock)));
            Assert.IsTrue(s.Apply(RobotRepairCommand.For(RobotRepairAction.PowerOff)));
            Assert.AreEqual(RobotRepairStage.Disassemble, s.Stage);
            Disassemble(s);
            Assert.IsTrue(s.Apply(RobotRepairCommand.Choose(RobotRepairChoice.RebalanceRotor)));
            Reassemble(s);
            Assert.IsTrue(s.Apply(RobotRepairCommand.For(RobotRepairAction.PowerOn)));
            Assert.IsTrue(s.Apply(RobotRepairCommand.Retest(RobotRepairAction.HoverRetest, true)));
            Assert.IsTrue(s.Apply(RobotRepairCommand.Retest(RobotRepairAction.LoadRetest, true)));
            Assert.AreEqual(RobotRepairStage.Complete, s.Stage);
        }

        [Test]
        public void PlanRejectsDuplicateStepIds()
        {
            Assert.Throws<ArgumentException>(() => new RobotRepairPlan("test", new[] { "left" }, new[]
            {
                new RobotRepairStep("1", "first", "part_a"),
                new RobotRepairStep("1", "second", "part_b")
            }));
        }
    }
}
