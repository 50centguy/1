using System.Collections.Generic;
using BorderRepair.Core;
using BorderRepair.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.RepairProps.Tests
{
    public sealed class RepairPropFlowTests
    {
        [TestCase(RepairPropKind.Receiver)]
        [TestCase(RepairPropKind.Cartridge)]
        [TestCase(RepairPropKind.Power)]
        [TestCase(RepairPropKind.Omamori)]
        public void RemovedCoverIsStoredWithoutOccludingInternalsAndReturnsHome(RepairPropKind kind)
        {
            using (var rig = new PropFlowTestRig(kind))
            {
                var command = rig.definition.FindCommand("cover.remove");
                var cover = rig.flow.GetObject(command.target);
                var homePosition = cover.localPosition;
                var homeRotation = cover.localRotation;
                var homeParent = cover.parent;
                Assert.That(cover.gameObject.activeSelf, Is.True);
                rig.Diagnose();
                rig.OpenToModule();
                Assert.That(rig.flow.GetState("cover"), Is.EqualTo(1));
                Assert.That(rig.definition.FindNode(command.target).inventoryOnly, Is.True);
                Assert.That(cover.gameObject.activeSelf, Is.False, "Stored cover must not block the open service aperture.");
                Assert.That(cover.parent, Is.SameAs(homeParent));
                rig.CloseWithReplacement();
                Assert.That(cover.gameObject.activeSelf, Is.True);
                Assert.That(cover.localPosition, Is.EqualTo(homePosition));
                Assert.That(Quaternion.Angle(cover.localRotation, homeRotation), Is.LessThan(0.001f));
                Assert.That(cover.parent, Is.SameAs(homeParent));
            }
        }

        [TestCase(RepairPropKind.Receiver)]
        [TestCase(RepairPropKind.Cartridge)]
        [TestCase(RepairPropKind.Power)]
        [TestCase(RepairPropKind.Omamori)]
        public void EveryActualModelHasReachableGuardedRepairAndCoreCompletion(RepairPropKind kind)
        {
            using (var rig = new PropFlowTestRig(kind))
            {
                var errors = new List<string>();
                Assert.That(rig.definition.Validate(errors), Is.True, string.Join("\n", errors));
                Assert.That(rig.repairCase.Validate(errors), Is.True, string.Join("\n", errors));
                Assert.That(rig.Try("complete"), Is.False);
                string gateTarget = rig.repairCase.FindStep(rig.definition.completionStepId).targetPointId;
                Assert.That(rig.flow.Session.PerformAction(RepairActionType.ServiceModule, gateTarget), Is.EqualTo(ActionOutcome.Blocked), "Stock controller must not bypass the flow gate.");
                Assert.That(rig.flow.Session.TryBeginDiagnosis(), Is.False);
                var record = rig.flow.Session.CurrentRecord;
                var retained = rig.flow.GetObject(rig.definition.retainedObjectName);
                var position = retained.localPosition;
                var parent = retained.parent;
                rig.Diagnose(); rig.OpenToModule();
                Assert.That(rig.flow.GetState("seat"), Is.Zero);
                Assert.That(rig.Try("power.on"), Is.False);
                Assert.That(rig.Try("test." + rig.definition.tests[0].id), Is.False);
                rig.CloseWithReplacement();
                Assert.That(rig.flow.IsAssembled, Is.True);
                Assert.That(rig.Try("complete"), Is.False, "Reassembly alone does not complete the repair.");
                rig.Retest(); rig.Do("complete");
                Assert.That(rig.flow.Completed, Is.True);
                Assert.That(rig.flow.Session.Stage, Is.EqualTo(RepairStage.Result));
                Assert.That(rig.flow.Session.CurrentRecord, Is.SameAs(record));
                Assert.That(record.IsFinished, Is.True);
                Assert.That(retained.localPosition, Is.EqualTo(position));
                Assert.That(retained.parent, Is.SameAs(parent));
                Assert.That(rig.flow.Session.HasClue("flow.verified"), Is.True);
            }
        }

        [TestCase(RepairPropKind.Receiver)]
        [TestCase(RepairPropKind.Cartridge)]
        [TestCase(RepairPropKind.Power)]
        [TestCase(RepairPropKind.Omamori)]
        public void WrongToolsTargetsAndPoweredManipulationCannotChangeAssembly(RepairPropKind kind)
        {
            using (var rig = new PropFlowTestRig(kind))
            {
                string id = "remove." + rig.coverScrewPrefix + "01";
                var c = rig.definition.FindCommand(id);
                Assert.That(rig.flow.TryExecute(id, c.target, RepairActionType.OpenHousing), Is.False);
                Assert.That(rig.flow.TryExecute(id, rig.definition.retainedObjectName, c.tool), Is.False);
                Assert.That(rig.Try(id), Is.False);
                rig.Diagnose(); rig.Do("power.off");
                Assert.That(rig.Try(id), Is.False, "External connection still attached.");
                rig.Do("fixture.disconnect");
                Assert.That(rig.Try("harness.release"), Is.False);
                Assert.That(rig.Try("module.remove.old"), Is.False);
                Assert.That(rig.flow.GetState("seat"), Is.EqualTo(1));
                Assert.That(rig.flow.GetState("out." + c.target), Is.Zero);
                Assert.That(rig.flow.Session.CurrentRecord.InvalidActionCount, Is.GreaterThanOrEqualTo(5));
            }
        }

        [TestCase(RepairPropKind.Receiver)]
        [TestCase(RepairPropKind.Cartridge)]
        [TestCase(RepairPropKind.Power)]
        [TestCase(RepairPropKind.Omamori)]
        public void FailedRetestPreservesRecordAndSupportsRepairInPlace(RepairPropKind kind)
        {
            using (var rig = new PropFlowTestRig(kind))
            {
                rig.Diagnose(); rig.OpenToModule(); rig.CloseWithReplacement();
                var record = rig.flow.Session.CurrentRecord;
                int count = rig.flow.History.Count;
                rig.flow.TestProbe = new FailingProbe();
                rig.Do("test." + rig.definition.tests[0].id);
                Assert.That(rig.Try("complete"), Is.False);
                Assert.That(rig.flow.FailedRetests, Is.EqualTo(1));
                Assert.That(rig.flow.IsAssembled, Is.True);
                Assert.That(rig.flow.GetState("module"), Is.EqualTo(2));
                Assert.That(rig.flow.Session.Stage, Is.EqualTo(RepairStage.Inspect));
                Assert.That(rig.flow.Session.CurrentRecord, Is.SameAs(record));
                Assert.That(rig.flow.History.Count, Is.GreaterThan(count));
                rig.OpenToModule(true); rig.CloseWithReplacement();
                rig.flow.TestProbe = null; rig.Retest(); rig.Do("complete");
                Assert.That(rig.flow.Session.Records.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void PowerRequiresCurrentNoLoadPassAndLoadedPass()
        {
            using (var rig = new PropFlowTestRig(RepairPropKind.Power))
            {
                Assert.That(rig.Try("test.load"), Is.False);
                rig.Diagnose(); rig.OpenToModule(); rig.CloseWithReplacement();
                Assert.That(rig.Try("test.load"), Is.False);
                rig.Do("test.no_load"); Assert.That(rig.Try("complete"), Is.False);
                rig.Do("test.load"); rig.Do("complete");
            }
        }

        [Test]
        public void OmamoriRequiresThreeConsecutiveWakePassesAndRetainedIdentity()
        {
            using (var rig = new PropFlowTestRig(RepairPropKind.Omamori))
            {
                rig.Diagnose(); rig.OpenToModule(); rig.CloseWithReplacement();
                rig.Do("test.identity"); rig.Do("test.wake"); rig.Do("test.wake");
                Assert.That(rig.Try("complete"), Is.False);
                rig.Do("test.wake"); rig.Do("complete");
            }
        }

        [Test]
        public void ChangedRetainedCoreAndNonReadOnlyProbeCannotComplete()
        {
            using (var rig = new PropFlowTestRig(RepairPropKind.Cartridge))
            {
                rig.Diagnose(); rig.OpenToModule(); rig.CloseWithReplacement();
                var core = rig.flow.GetObject("MC_DataCore");
                core.localPosition += Vector3.right;
                Assert.That(rig.Try("test.read"), Is.False);
                core.localPosition -= Vector3.right;
                rig.flow.TestProbe = new WritingProbe();
                rig.Do("test.read"); Assert.That(rig.Try("complete"), Is.False);
                Assert.That(rig.flow.Session.HasClue("flow.verified"), Is.False);
                foreach (string forbidden in new[] { "format", "copy", "play.private", "core.replace" })
                    Assert.That(rig.flow.TryExecute(forbidden, "MC_DataCore", RepairActionType.ServiceModule), Is.False);
            }
        }

        [Test]
        public void SharedCoreSessionAdvancesAllFourCasesWithoutReplacingQueueOrRecords()
        {
            var shift = ScriptableObject.CreateInstance<RepairShiftData>();
            var definitions = new List<RepairPropFlowDefinition>();
            try
            {
                foreach (RepairPropKind kind in System.Enum.GetValues(typeof(RepairPropKind)))
                {
                    var c = ScriptableObject.CreateInstance<RepairCaseData>();
                    definitions.Add(RepairPropFlowCatalog.Create(kind, c));
                    shift.cases.Add(c);
                }
                var session = new RepairSession(shift, () => 1f);
                session.StartShift();
                foreach (var definition in definitions)
                {
                    Assert.That(session.CurrentCase, Is.SameAs(definition.repairCase));
                    session.AcceptItem();
                    using (var rig = new PropFlowTestRig(definition.kind, session, definition))
                    {
                        Assert.That(rig.flow.Session, Is.SameAs(session));
                        rig.Diagnose(); rig.OpenToModule(); rig.CloseWithReplacement(); rig.Retest(); rig.Do("complete");
                    }
                    Assert.That(session.NextCase(), Is.True);
                }
                Assert.That(session.Stage, Is.EqualTo(RepairStage.Summary));
                Assert.That(session.Records.Count, Is.EqualTo(4));
                foreach (var record in session.Records) Assert.That(record.IsFinished, Is.True);
            }
            finally
            {
                foreach (var d in definitions) { Object.DestroyImmediate(d.repairCase); Object.DestroyImmediate(d); }
                Object.DestroyImmediate(shift);
            }
        }

        [Test]
        public void GeneratedShiftUsesFourPrefabsWithActualBindingsAndNoCartridgeFixture()
        {
            var shift = AssetDatabase.LoadAssetAtPath<RepairShiftData>("Assets/RepairProps/Data/RepairProps_Shift.asset");
            if (shift == null) Assert.Ignore("Parent must run RepairPropCaseFactory.BuildAll after model prefab import.");
            Assert.That(shift.cases.Count, Is.EqualTo(4));
            foreach (var c in shift.cases)
            {
                var flow = c.itemPrefab.GetComponent<RepairPropFlow>();
                Assert.That(flow, Is.Not.Null);
                Assert.That(c.itemPrefab.GetComponent<RepairPropStationBridge>(), Is.Not.Null);
                foreach (var node in flow.Definition.nodes)
                {
                    bool found = false;
                    foreach (var t in c.itemPrefab.GetComponentsInChildren<Transform>(true))
                    {
                        var point = t.GetComponent<BorderRepair.Inspection.InspectionPoint>();
                        if (t.name == node.objectName || (node.objectName == flow.Definition.rootObjectName && point != null && point.PointId == node.objectName)) found = true;
                    }
                    Assert.That(found, Is.True, node.hierarchyPath);
                    Assert.That(node.objectName, Does.Not.StartWith("MC_Test"));
                }
            }
        }

        [TestCase(RepairPropKind.Receiver)]
        [TestCase(RepairPropKind.Cartridge)]
        [TestCase(RepairPropKind.Power)]
        [TestCase(RepairPropKind.Omamori)]
        public void VerifiedCompletionBecomesOneDeliverableClinicTradeWithItsOwnFee(RepairPropKind kind)
        {
            using (var rig = new PropFlowTestRig(kind))
            {
                // First queue slot on purpose: a repair prop there must not take the collector's contract.
                var state = new TwoNight.TwoNightState { unifiedClinic = true, phase = TwoNight.TwoNightPhase.Night1Counter, customerQueueCount = 1 };
                string caseId = rig.repairCase.caseId;
                int fee = rig.repairCase.estimatedRepairCost;
                Assert.That(fee, Is.GreaterThan(0));
                Assert.That(TwoNight.TwoNightRun.ReceiveCustomer(state, caseId, 0, fee, 0), Is.True);
                rig.Diagnose(); rig.OpenToModule(); rig.CloseWithReplacement(); rig.Retest(); rig.Do("complete");
                var record = rig.flow.Session.CurrentRecord;
                Assert.That(record.Ending, Is.Not.Null);
                Assert.That(record.Decision, Is.EqualTo(RepairDecision.Repair));
                Assert.That(record.DecisionCorrect, Is.True);
                // The same call TwoNightCounterDirector makes when the session reaches Result.
                Assert.That(TwoNight.TwoNightRun.CompleteCustomerDecision(state, record.DecisionCorrect, record.Case.caseId, record.Decision.Value), Is.True);
                Assert.That(state.ActiveTrade.state, Is.EqualTo(TwoNight.ClinicTradeState.ReadyForDelivery));
                Assert.That(TwoNight.TwoNightRun.DeliverCustomer(state, caseId), Is.True);
                Assert.That(TwoNight.TwoNightRun.DeliverCustomer(state, caseId), Is.False, "Delivery books once.");
                var income = state.transactions.FindAll(t => t.id == state.ActiveTrade.IncomeId);
                Assert.That(income.Count, Is.EqualTo(1));
                Assert.That(income[0].amount, Is.EqualTo(fee));
            }
        }

        sealed class FailingProbe : IRepairPropTestProbe
        { public PropTestResult Measure(RepairPropFlow flow, PropTest test) => new PropTestResult(false); }
        sealed class WritingProbe : IRepairPropTestProbe
        { public PropTestResult Measure(RepairPropFlow flow, PropTest test) => new PropTestResult(true, true, false); }
    }
}
