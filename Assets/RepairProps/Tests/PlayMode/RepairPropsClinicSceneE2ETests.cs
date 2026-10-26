using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Inspection;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.Tests
{
    // Real generated scene/assets, driven through public controllers, not a human-input acceptance test.
    public sealed class RepairPropsClinicSceneE2ETests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        const string ReportDirectory = "C:/Users/Administrator/Documents/Codex/2026-09-29/border-repair-station-art/work/repair-props-integration-2026-10-17/scene-flow-verification";
        readonly SceneEvidence evidence = new SceneEvidence();
        TwoNightState previousRun;
        string previousSaveDirectory;
        float previousTimeScale;
        Scene loadedScene;
        UnifiedClinicDirector clinic;
        RepairPropStationBridge bridge;
        List<LocalPose> retained;
        int purchaseCount;
        RepairPropFlow Flow => bridge.Flow;
        RepairStationController Station => clinic.Station;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current;
            previousSaveDirectory = TwoNightSave.OverrideDirectory;
            previousTimeScale = Time.timeScale;
            // No save is requested. Isolate any incidental save lookup from the player's real checkpoint.
            TwoNightSave.OverrideDirectory = Path.Combine(Path.GetTempPath(), "RepairPropsSceneE2E_" + Guid.NewGuid().ToString("N"));
            TwoNightRun.Clear();
            Time.timeScale = 1;
            evidence.captures.Clear();
            evidence.finishedOrders.Clear();
            evidence.completed = false;
            purchaseCount = 0;
#if UNITY_EDITOR
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null,
                "The parent must generate the real scene before running this test; do not synthesize a replacement.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Requires the existing editor scene; this test does not change build settings.");
            yield break;
#endif
            loadedScene = SceneManager.GetSceneByPath(ScenePath);
            Assert.That(loadedScene.isLoaded, Is.True);
            yield return Until(() =>
            {
                clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>();
                return clinic != null && clinic.Initialized;
            }, "UnifiedClinicDirector must finish its own Start coroutine.");
            Assert.That(clinic.gameObject.scene, Is.EqualTo(loadedScene));
            Assert.That(Station.Session.CaseCount, Is.EqualTo(4));
            Assert.That(clinic.State.customerQueueCount, Is.EqualTo(4));
            Assert.That(clinic.State.customerTrades, Is.Empty);
            Assert.That(clinic.State.transactions, Is.Empty);
            Assert.That(clinic.Robot.Flow.Rig.Walker, Is.Not.Null, "Carry/placement requires the real walking scene.");
            Assert.That(clinic.InspectionOnBench, Is.True);
#if UNITY_EDITOR
            Assert.That(AssetDatabase.GetAssetPath(Station.Session.Shift),
                Is.EqualTo("Assets/RepairProps/Data/RepairProps_Shift.asset"));
#endif
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Unload before restoring static state so a live clinic cannot use the previous run.
            if (loadedScene.IsValid() && loadedScene.isLoaded)
            {
                var cleanup = SceneManager.CreateScene("RepairPropsE2ECleanup_" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(loadedScene);
            }
            TwoNightRun.Set(previousRun);
            TwoNightSave.OverrideDirectory = previousSaveDirectory;
            Time.timeScale = previousTimeScale;
            Directory.CreateDirectory(ReportDirectory);
            File.WriteAllText(Path.Combine(ReportDirectory, "scene-e2e-evidence.json"), JsonUtility.ToJson(evidence, true));
            clinic = null;
            bridge = null;
            retained = null;
        }

        [UnityTest]
        [Category("RepairPropsSceneE2E")]
        public IEnumerator FourImportedOrders_ReceiveCarryBenchRepairRetryAndDeliverExactlyOnce()
        {
            var session = Station.Session;
            int initialCash = clinic.State.Cash;
            int expectedCash = initialCash;
            var kinds = new[] { RepairPropKind.Receiver, RepairPropKind.Cartridge, RepairPropKind.Power, RepairPropKind.Omamori };
            for (int index = 0; index < kinds.Length; index++)
            {
                var kind = kinds[index];
                string sku = RepairPropSupplyPolicy.SkuFor(kind);
                Assert.That(ClinicSupplies.Purchase(clinic.State, new Dictionary<string, int> { { sku, 1 } },
                    Guid.NewGuid().ToString("N"), out string purchaseReason), Is.True, purchaseReason);
                purchaseCount++;
                expectedCash -= ClinicSupplies.Find(sku).Price;
                TestContext.WriteLine("Real clinic queue order " + index + ": " + kind);
                Assert.That(clinic.HasPendingCase, Is.True);
                Assert.That(session.CaseIndex, Is.EqualTo(Math.Max(0, index - 1)), "No automatic next intake.");
                if (index == 0) AssertSupported(Station.Inspector.CurrentItem, clinic.ReceiveAnchor);
                Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True, clinic.LastMessage);
                var item = Station.Inspector.CurrentItem;
                Assert.That(item, Is.Not.Null);
                bridge = item.GetComponent<RepairPropStationBridge>();
                Assert.That(bridge, Is.Not.Null);
                // In later orders Receive instantiates the next prefab; its bridge attaches in Start, not immediately.
                yield return Until(() => bridge.Flow.Session != null, kind + " bridge must attach through its real lifecycle.");
                Assert.That(Flow.Session, Is.SameAs(session));
                Assert.That(Flow.Definition.kind, Is.EqualTo(kind));
                Assert.That(Flow.Definition.repairCase, Is.SameAs(session.CurrentCase));
                AssertRealPrefab(item, kind);
                retained = Flow.Definition.nodes.Where(n => n.immutable).Select(n => new LocalPose(Flow.GetObject(n.objectName))).ToList();
                Assert.That(retained, Is.Not.Empty);
                Assert.That(session.CaseIndex, Is.EqualTo(index));
                Assert.That(session.Records.Count, Is.EqualTo(index + 1));
                Assert.That(clinic.State.customerTrades.Count, Is.EqualTo(index + 1));
                var record = session.CurrentRecord;
                var trade = clinic.State.ActiveTrade;
                Assert.That(trade.caseId, Is.EqualTo(session.CurrentCase.caseId));
                Assert.That(trade.income, Is.EqualTo(session.CurrentCase.estimatedRepairCost));
                Assert.That(trade.income, Is.GreaterThan(0));
                Assert.That(trade.partsCost, Is.Zero);
                Assert.That(clinic.Carrying, Is.True);
                Assert.That(clinic.CarriedItem, Is.SameAs(item));
                Assert.That(item.transform.parent, Is.SameAs(clinic.Robot.Flow.Rig.Walker.Eye));
                Assert.That(item.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), Is.True,
                    "Carried geometry must not intercept the walking crosshair.");
                Assert.That(session.Stage, Is.EqualTo(RepairStage.Intake));
                Assert.That(clinic.ConsoleOpen, Is.False);
                AssertBridgeClosed();
                Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.False);
                Assert.That(clinic.Interact(ClinicTradeAction.Deliver), Is.False);
                AssertNoNewMoney(expectedCash, index);

                clinic.Counter.DismissDialogue();
                Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True, "Place the carried item through the clinic controller.");
                yield return Until(() => bridge.CanInteract, kind + " bench must enable the real station/camera/scanner.");
                Assert.That(clinic.Carrying, Is.False);
                Assert.That(session.Stage, Is.EqualTo(RepairStage.Inspect));
                Assert.That(Station.Inspector.CurrentItem, Is.SameAs(item));
                Assert.That(item.transform.parent, Is.SameAs(Station.Inspector.ItemAnchor));
                Assert.That(item.GetComponentsInChildren<Collider>().Any(c => c.enabled), Is.True);
                Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.False);
                Assert.That(clinic.Interact(ClinicTradeAction.Deliver), Is.False);
                AssertInvariants();
                yield return WaitForBenchSettled();
                Assert.That(bridge.BenchPresentation.AllSlotsFit, Is.True, kind + " tabletop slots must fit without overlap.");
                Capture(kind, "bench", item, BenchCamera());
                AssertSomeGeometryPickable();

                Reject("complete");
                var off = Flow.Definition.FindCommand("power.off");
                Assert.That(bridge.SelectTarget(off.target), Is.True);
                Station.SelectTool(RepairActionType.ServiceModule);
                RejectSelected("power.off", true, "Wrong tool must not change power or history.");
                Reject("cover.remove");
                foreach (var test in Flow.Definition.tests)
                {
                    Do("test." + test.id);
                    Assert.That(Flow.GetState("diagnosed." + test.id), Is.EqualTo(1));
                    Assert.That(Flow.LastTestPassed, Is.EqualTo(!test.functional), "Initial fault must be diagnosed on the actual item: " + test.id);
                }
                yield return LeaveAndReturn(item);

                Do("power.off");
                Reject("cover.remove");
                Do("fixture.disconnect");
                Reject("module.install.new");
                string coverScrews = new[] { "RX_CoverScrew_", "MC_CoverScrew_", "PS_CoverScrew_", "OM_CoverScrew_" }[index];
                string moduleScrews = new[] { "RX_RFScrew_", "MC_IFScrew_", "PS_ModuleScrew_", "OM_WakeScrew_" }[index];
                for (int screw = 1; screw <= 3; screw++) Do("remove." + coverScrews + screw.ToString("00"));
                Reject("cover.remove");
                Do("remove." + coverScrews + "04");
                Do("cover.remove");
                yield return WaitForBenchSettled();
                Capture(kind, "open", item, BenchCamera());
                Reject("test." + Flow.Definition.tests[0].id);
                if (kind != RepairPropKind.Cartridge)
                {
                    Reject("harness.release");
                    Do("isolate");
                }
                Do("harness.release");
                if (kind == RepairPropKind.Power)
                {
                    Reject("tray.remove");
                    Do("remove.PS_TrayScrew_01");
                    Do("remove.PS_TrayScrew_02");
                    Do("tray.remove");
                }
                Reject("module.remove.old");
                for (int screw = 1; screw <= 2; screw++) Do("remove." + moduleScrews + screw.ToString("00"));
                Do("module.remove.old");
                Assert.That(Flow.GetState("seat"), Is.Zero);
                Do("module.install.new");
                Reject("module.install.old");
                Assert.That(Flow.GetState("module"), Is.EqualTo(2));
                yield return LeaveAndReturn(item);
                Reject("fixture.connect");
                for (int screw = 1; screw <= 2; screw++) Do("install." + moduleScrews + screw.ToString("00"));
                if (kind == RepairPropKind.Power)
                {
                    Do("tray.install");
                    Do("install.PS_TrayScrew_01");
                    Do("install.PS_TrayScrew_02");
                }
                Do("harness.connect");
                if (kind != RepairPropKind.Cartridge) Do("supply.connect");
                Do("cover.install");
                for (int screw = 1; screw <= 4; screw++) Do("install." + coverScrews + screw.ToString("00"));
                Assert.That(Flow.IsAssembled, Is.True);
                Do("fixture.connect");
                Do("power.on");
                yield return null;
                AssertInvariants();

                // Earn earlier passes (including partial repeated passes), then fail the final measurement.
                var tests = Flow.Definition.tests;
                for (int t = 0; t < tests.Count; t++)
                    for (int pass = 0; pass < tests[t].consecutivePasses - (t == tests.Count - 1 ? 1 : 0); pass++)
                        Do("test." + tests[t].id);
                var mechanicalState = StateSnapshot();
                var repairedPoses = Flow.Definition.nodes.ToDictionary(n => n.objectName,
                    n => new LocalPose(Flow.GetObject(n.objectName)));
                var failure = new FailOnceProbe();
                Flow.TestProbe = failure;
                Do("test." + tests[tests.Count - 1].id);
                Assert.That(failure.Calls, Is.EqualTo(1));
                Flow.TestProbe = null; // Retry uses the actual default evaluator, not an always-pass double.
                Assert.That(Flow.FailedRetests, Is.EqualTo(1));
                Assert.That(Flow.LastTestPassed, Is.False);
                Assert.That(Flow.Completed, Is.False);
                Assert.That(Flow.GetState("retest.failed"), Is.EqualTo(1));
                Assert.That(session.CurrentRecord, Is.SameAs(record));
                Assert.That(Flow.IsAssembled, Is.True);
                foreach (var entry in mechanicalState.Where(e => !IsMeasurementState(e.Key)))
                    Assert.That(Flow.GetState(entry.Key), Is.EqualTo(entry.Value), "Failed retest lost repair progress: " + entry.Key);
                foreach (var pose in repairedPoses)
                    pose.Value.AssertUnchanged(checkActive: !IsRetestFx(Flow.Definition.FindNode(pose.Key)));
                AssertFailedRetestFx();
                AssertInvariants();
                foreach (var test in tests)
                {
                    Assert.That(Flow.GetState("pass." + test.id), Is.Zero);
                    Assert.That(Flow.GetState("count." + test.id), Is.Zero, "Failure must also reset partial consecutive passes.");
                }
                Reject("complete");
                Assert.That(clinic.TradeState, Is.EqualTo(ClinicTradeState.InRepair));
                Assert.That(clinic.Interact(ClinicTradeAction.Deliver), Is.False);
                AssertNoNewMoney(expectedCash, index);
                foreach (var test in tests)
                    for (int pass = 1; pass <= test.consecutivePasses; pass++)
                    {
                        Do("test." + test.id);
                        Assert.That(Flow.LastTestPassed, Is.True);
                        Assert.That(Flow.GetState("count." + test.id), Is.EqualTo(pass));
                        Assert.That(Flow.GetState("pass." + test.id), Is.EqualTo(pass == test.consecutivePasses ? 1 : 0));
                        if (pass < test.consecutivePasses) Reject("complete");
                    }
                Do("complete");
                yield return Until(() => clinic.TradeState == ClinicTradeState.ReadyForDelivery && !clinic.ConsoleOpen,
                    kind + " must reach delivery through TwoNightCounterDirector's real subscription.");
                yield return Until(() => item.GetComponentsInChildren<RepairPart>(true).All(p => p.IsAtTarget),
                    "The station's Tested part-state event must settle through RepairPart.Update.");
                AssertInvariants();
                Assert.That(Flow.Completed, Is.True);
                Assert.That(record.Decision, Is.EqualTo(RepairDecision.Repair));
                Assert.That(record.DecisionCorrect, Is.True);
                Assert.That(record.Ending.endingId, Is.EqualTo(Flow.Definition.completionEndingId));
                Assert.That(session.Stage, Is.EqualTo(index == 3 ? RepairStage.Summary : RepairStage.Result));
                Assert.That(session.CaseIndex, Is.EqualTo(index));
                Assert.That(Station.Inspector.CurrentItem, Is.SameAs(item));
                AssertSupported(item, clinic.DeliveryAnchor);
                AssertBridgeClosed();
                AssertNoNewMoney(expectedCash, index);
                // Aim through the existing player API; the player stays at the real scene's current standing position.
                var walker = clinic.Robot.Flow.Rig.Walker;
                walker.LookAt(VisibleBounds(item).center);
                yield return Until(() => Quaternion.Angle(ActiveCamera().transform.rotation, walker.Eye.rotation) < 0.1f,
                    "The room rig must follow the player's delivery-pad aim.");
                Capture(kind, "completed", item, ActiveCamera());
                Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.False);
                Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.False);
                Assert.That(clinic.Interact(ClinicTradeAction.Deliver), Is.True, clinic.LastMessage);
                expectedCash += trade.income;
                Assert.That(clinic.TradeState, Is.EqualTo(ClinicTradeState.Delivered));
                Assert.That(Station.Inspector.CurrentItem, Is.Null);
                AssertNoNewMoney(expectedCash, index + 1);
                Assert.That(clinic.State.transactions.Count(t => t.id == trade.IncomeId), Is.EqualTo(1));
                Assert.That(clinic.State.transactions.Single(t => t.id == trade.IncomeId).amount, Is.EqualTo(trade.income));
                Assert.That(clinic.State.transactions.Count(t => t.id == trade.PartsId), Is.EqualTo(1));
                Assert.That(clinic.State.transactions.Single(t => t.id == trade.PartsId).amount, Is.Zero);
                Assert.That(clinic.Interact(ClinicTradeAction.Deliver), Is.False, "Repeated delivery must not book again.");
                AssertNoNewMoney(expectedCash, index + 1);
                evidence.finishedOrders.Add(kind.ToString());
                yield return null; // Deferred Destroy and preview Start must run before the next intake.
                Assert.That(item == null, Is.True, "Delivered mounted instance is released by the real inspector.");
                Assert.That(clinic.HasPendingCase, Is.EqualTo(index < 3));
                Assert.That(clinic.Counter.LedgerVisible, Is.EqualTo(index == 3));
            }
            Assert.That(Station.Session, Is.SameAs(session));
            Assert.That(session.Records.Count, Is.EqualTo(4));
            Assert.That(clinic.State.phase, Is.EqualTo(TwoNightPhase.Night1Ledger));
            Assert.That(clinic.State.transactions.Select(t => t.id).Distinct().Count(), Is.EqualTo(8 + purchaseCount));
            foreach (string sku in kinds.Select(RepairPropSupplyPolicy.SkuFor))
            {
                Assert.That(ClinicSupplies.Available(clinic.State, sku), Is.Zero);
                Assert.That(ClinicSupplies.Consumed(clinic.State, sku), Is.EqualTo(1));
            }
            Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.False);
            Assert.That(clinic.State.Cash - initialCash, Is.EqualTo(session.Shift.cases.Sum(c => c.estimatedRepairCost) -
                kinds.Sum(k => ClinicSupplies.Find(RepairPropSupplyPolicy.SkuFor(k)).Price)));
            evidence.completed = true;
        }

        IEnumerator LeaveAndReturn(GameObject item)
        {
            var before = StateSnapshot();
            var history = Flow.History.ToArray();
            var record = Station.Session.CurrentRecord;
            string target = bridge.SelectedTarget;
            int invalid = record.InvalidActionCount;
            clinic.ShowRoom();
            yield return null;
            Assert.That(clinic.ConsoleOpen, Is.False);
            AssertBridgeClosed();
            CollectionAssert.AreEqual(history, Flow.History);
            Assert.That(bridge.SelectedTarget, Is.EqualTo(target));
            Assert.That(record.InvalidActionCount, Is.EqualTo(invalid), "Off-bench refusal must not dispatch a repair action.");
            foreach (var pair in before) Assert.That(Flow.GetState(pair.Key), Is.EqualTo(pair.Value), pair.Key);
            Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True);
            yield return Until(() => bridge.CanInteract, "Returning to the bench must restore the existing bridge.");
            Assert.That(Station.Inspector.CurrentItem, Is.SameAs(item));
            Assert.That(Station.Session.CurrentRecord, Is.SameAs(record));
            foreach (var pair in before) Assert.That(Flow.GetState(pair.Key), Is.EqualTo(pair.Value), pair.Key);
            yield return WaitForBenchSettled();
            Assert.That(BenchCamera(), Is.SameAs(Station.Inspector.ViewCamera));
            AssertInvariants();
        }

        void AssertBridgeClosed()
        {
            Assert.That(bridge.CanInteract, Is.False);
            Assert.That(bridge.ContextPanelVisible, Is.False);
            Assert.That(bridge.SelectTarget(Flow.Definition.rootObjectName), Is.False);
            Assert.That(bridge.ExecuteSelected("power.off"), Is.False);
            Assert.That(Station.Inspector.ViewCamera.enabled, Is.False);
            Assert.That(Station.View.gameObject.activeInHierarchy, Is.False);
        }

        void Do(string id)
        {
            var command = Flow.Definition.FindCommand(id);
            Assert.That(command, Is.Not.Null, id);
            Station.SelectTool(command.tool);
            Assert.That(Station.ActiveTool, Is.EqualTo(command.tool), id + " tool selection");
            Assert.That(bridge.SelectTarget(command.target), Is.True, id + " target selection");
            int count = Flow.History.Count;
            Assert.That(bridge.ExecuteSelected(id), Is.True, Flow.Definition.kind + "/" + id + ": " + Flow.LastFeedback);
            Assert.That(Flow.History.Count, Is.EqualTo(count + 1), id);
            Assert.That(Flow.History.Last(), Is.EqualTo(id));
            AssertInvariants(!Flow.Completed);
        }

        void Reject(string id)
        {
            var command = Flow.Definition.FindCommand(id);
            Assert.That(command, Is.Not.Null, id);
            Station.SelectTool(command.tool);
            Assert.That(bridge.SelectTarget(command.target), Is.True, id);
            RejectSelected(id, true, "Out-of-order operation must be rejected.");
        }

        void RejectSelected(string id, bool counted, string message)
        {
            var state = StateSnapshot();
            var history = Flow.History.ToArray();
            int invalid = Station.Session.CurrentRecord.InvalidActionCount;
            Assert.That(bridge.ExecuteSelected(id), Is.False, id + ": " + message);
            CollectionAssert.AreEqual(history, Flow.History, id);
            foreach (var entry in state) Assert.That(Flow.GetState(entry.Key), Is.EqualTo(entry.Value), id + "/" + entry.Key);
            Assert.That(Station.Session.CurrentRecord.InvalidActionCount, Is.EqualTo(invalid + (counted ? 1 : 0)), id);
            AssertInvariants();
        }

        Dictionary<string, int> StateSnapshot()
        {
            var keys = Flow.Definition.initialState.Select(c => c.key)
                .Concat(Flow.Definition.commands.SelectMany(c => c.requires.Concat(c.effects)).Select(c => c.key))
                .Concat(Flow.Definition.tests.SelectMany(t => new[] { "pass." + t.id, "count." + t.id, "diagnosed." + t.id, "display." + t.id }))
                .Concat(new[] { "retest.failed", "curtain" }).Distinct();
            return keys.ToDictionary(k => k, Flow.GetState);
        }

        static bool IsMeasurementState(string key) => key.StartsWith("pass.", StringComparison.Ordinal)
            || key.StartsWith("count.", StringComparison.Ordinal) || key.StartsWith("diagnosed.", StringComparison.Ordinal)
            || key.StartsWith("display.", StringComparison.Ordinal) || key == "retest.failed" || key == "curtain";

        static bool IsRetestFx(PropNode node) => !node.immutable && !node.movable
            && !string.IsNullOrEmpty(node.visibleKey)
            && (node.visibleKey.StartsWith("display.", StringComparison.Ordinal) || node.visibleKey == "curtain");

        void AssertFailedRetestFx()
        {
            Assert.That(Flow.GetState("power"), Is.EqualTo(1));
            Assert.That(Flow.GetState("fixture"), Is.EqualTo(1));
            foreach (var test in Flow.Definition.tests)
                Assert.That(Flow.GetState("display." + test.id), Is.EqualTo(1), "Failed retest must show failure, not locked/OK: " + test.id);
            int curtain = Flow.Definition.kind == RepairPropKind.Receiver && Flow.GetState("strip") == 1 ? 1 : 0;
            if (Flow.Definition.kind == RepairPropKind.Receiver)
                Assert.That(Flow.GetState("curtain"), Is.EqualTo(curtain));
            foreach (var node in Flow.Definition.nodes.Where(IsRetestFx))
            {
                int expected = node.visibleKey == "curtain" ? curtain : 1;
                Assert.That(Flow.GetObject(node.objectName).gameObject.activeSelf, Is.EqualTo(node.visibleValue == expected),
                    "Declared test FX must reflect the failed measurement: " + node.objectName);
            }
        }

        void AssertInvariants(bool requireSettled = true)
        {
            foreach (var pose in retained) pose.AssertUnchanged();
            var old = Flow.GetObject(Flow.Definition.FindCommand("module.remove.old").target);
            var replacement = Flow.GetObject(Flow.Definition.FindCommand("module.install.new").target);
            Assert.That(replacement.gameObject.activeSelf, Is.EqualTo(Flow.GetState("new.out") == 0));
            if (Flow.GetState("new.out") == 0)
            {
                Assert.That(old.gameObject.activeSelf, Is.False, "Old and replacement modules must not overlap in the seat.");
                Assert.That(Flow.GetState("old.out"), Is.EqualTo(1));
            }
            else Assert.That(old.gameObject.activeSelf, Is.True, "The original module remains visible in its seat or parking position.");
            foreach (var node in Flow.Definition.nodes.Where(n => n.movable))
            {
                var part = Flow.GetObject(node.objectName).GetComponent<RepairPart>();
                Assert.That(part, Is.Not.Null, node.objectName);
                if (requireSettled)
                    Assert.That(part.IsAtTarget, Is.True, "This flow commits poses synchronously; do not wait away a regression: " + node.objectName);
            }
        }

        void AssertNoNewMoney(int cash, int delivered)
        {
            Assert.That(clinic.State.Cash, Is.EqualTo(cash));
            // Current ledger records one income and one zero-cost entry per delivery.
            Assert.That(clinic.State.transactions.Count, Is.EqualTo(delivered * 2 + purchaseCount));
        }

        void AssertSupported(GameObject item, Transform anchor)
        {
            Assert.That(item, Is.Not.Null);
            Assert.That(item.transform.parent, Is.SameAs(anchor));
            float bottom = VisibleBounds(item).min.y;
            Assert.That(bottom, Is.EqualTo(clinic.TradeSupportWorldY(anchor)).Within(0.003f), "Item must rest on its real trade pad.");
        }

        static void AssertRealPrefab(GameObject item, RepairPropKind kind)
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RepairProps/Prefabs/" + kind + ".prefab");
            Assert.That(prefab, Is.Not.Null);
            // The omamori's soft cord is runtime geometry beside the prefab's meshes, not part of them.
            var actual = item.GetComponentsInChildren<MeshFilter>(true).Where(m => m.name != "OmamoriCord_Runtime").Select(m => m.sharedMesh).ToArray();
            var expected = prefab.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh).ToArray();
            Assert.That(actual, Is.Not.Empty);
            Assert.That(actual.All(m => m != null), Is.True, kind + " has missing imported geometry.");
            CollectionAssert.AreEquivalent(expected, actual, "Mounted item must retain all actual prefab meshes.");
            Assert.That(actual.Any(m => AssetDatabase.GetAssetPath(m).StartsWith("Assets/RepairProps/Models/", StringComparison.Ordinal)), Is.True);
            Assert.That(item.GetComponent<RepairPropFlow>().Definition,
                Is.SameAs(prefab.GetComponent<RepairPropFlow>().Definition));
#endif
        }

        float BenchSupportError() => VisibleBounds(Station.Inspector.CurrentItem).min.y - (clinic.BenchTopWorldY + 0.0005f);

        float BenchCameraDistanceError() => Vector3.Distance(Station.Inspector.ViewCamera.transform.position,
            Station.Inspector.LookPoint) - Station.Inspector.TargetDistance;

        float BenchCameraAimError() => Vector3.Angle(Station.Inspector.ViewCamera.transform.forward,
            Station.Inspector.LookPoint - Station.Inspector.ViewCamera.transform.position);

        IEnumerator WaitForBenchSettled()
        {
            // Clinic LateUpdate seats visible bounds and moves the whole stage; inspector LateUpdate follows it.
            float deadline = Time.realtimeSinceStartup + 15f;
            int stableFrames = 0;
            while (stableFrames < 2 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                bool ready = bridge.CanInteract && Mathf.Abs(BenchSupportError()) < 0.001f
                    && Mathf.Abs(BenchCameraDistanceError()) < 0.001f && BenchCameraAimError() < 0.1f;
                stableFrames = ready ? stableFrames + 1 : 0;
            }
            Assert.That(stableFrames, Is.EqualTo(2), "Bench seating/camera did not settle across two frame boundaries. "
                + "Support error (m): " + BenchSupportError() + "; camera distance error (m): " + BenchCameraDistanceError()
                + "; camera aim error (deg): " + BenchCameraAimError());
        }

        Camera BenchCamera()
        {
            Assert.That(clinic.ConsoleOpen, Is.True);
            Assert.That(bridge.ContextPanelVisible, Is.True);
            Assert.That(ActiveCamera(), Is.SameAs(Station.Inspector.ViewCamera));
            var anchor = Station.Inspector.ItemAnchor.position;
            var work = clinic.WorkAnchor.position;
            Assert.That(Vector2.Distance(new Vector2(anchor.x, anchor.z), new Vector2(work.x, work.z)),
                Is.LessThan(0.001f), "Seating may change height, but must not move the inspection anchor off the workbench spot.");
            Assert.That(Mathf.Abs(BenchSupportError()), Is.LessThan(0.001f), "Visible geometry must rest at the real bench support height.");
            Assert.That(Mathf.Abs(BenchCameraDistanceError()), Is.LessThan(0.001f), "Inspector camera must follow its current look point at the requested distance.");
            Assert.That(BenchCameraAimError(), Is.LessThan(0.1f), "Inspector camera must aim at the current look point.");
            return Station.Inspector.ViewCamera;
        }

        static Camera ActiveCamera()
        {
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.isActiveAndEnabled).ToArray();
            Assert.That(cameras.Length, Is.EqualTo(1), "Room/bench camera ownership must not overlap.");
            return cameras[0];
        }

        void AssertSomeGeometryPickable()
        {
            var camera = BenchCamera();
            // Carry/placement and stage seating modify transforms outside FixedUpdate. Sync before reading bounds,
            // otherwise every stale projection can be skipped before PickPoint gets a chance to sync the world.
            Physics.SyncTransforms();
            int colliderCount = 0, sampleCount = 0, candidateCount = 0, hitItemCount = 0, pointCount = 0;
            var pointIds = new HashSet<string>();
            var colliderBounds = new List<string>();
            foreach (var collider in Station.Inspector.CurrentItem.GetComponentsInChildren<Collider>().Where(c => c.enabled))
            {
                colliderCount++;
                var bounds = collider.bounds;
                colliderBounds.Add(collider.name + ": center=" + bounds.center.ToString("F5") + ", size=" + bounds.size.ToString("F5"));
                for (int i = 0; i < 27; i++)
                {
                    sampleCount++;
                    var fraction = new Vector3(i % 3, i / 3 % 3, i / 9) * 0.4f + Vector3.one * 0.1f;
                    var screen = camera.WorldToScreenPoint(bounds.min + Vector3.Scale(bounds.size, fraction));
                    if (screen.z <= camera.nearClipPlane || screen.x <= 0 || screen.x >= Screen.width || screen.y <= 0 || screen.y >= Screen.height) continue;
                    candidateCount++;
                    var point = Station.Scanner.PickPoint(screen, out bool hitItem);
                    if (hitItem) hitItemCount++;
                    if (point != null) pointCount++;
                    pointIds.Add(point != null ? point.PointId ?? "<empty>" : "<none>");
                    if (!hitItem || point == null || Flow.Definition.FindNode(point.PointId) == null) continue;
                    Assert.That(bridge.SelectTarget(point.PointId), Is.True);
                    Assert.That(bridge.SelectedTarget, Is.EqualTo(point.PointId));
                    return;
                }
            }
            Assert.Fail(Flow.Definition.kind + " has no on-screen imported geometry pickable by the actual scanner. "
                + "Enabled colliders=" + colliderCount + "; samples=" + sampleCount + "; on-screen candidates/PickPoint calls=" + candidateCount
                + "; hitItem=" + hitItemCount + "; non-null points=" + pointCount + "; pointIds=[" + string.Join(", ", pointIds.OrderBy(id => id))
                + "]; screen=" + Screen.width + "x" + Screen.height + "; camera pixelRect=" + camera.pixelRect
                + "; synchronized collider bounds: " + string.Join("; ", colliderBounds));
        }

        static Bounds VisibleBounds(GameObject item)
        {
            var renderers = item.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            Assert.That(renderers, Is.Not.Empty, item.name + " must have visible geometry.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            Assert.That(bounds.size.sqrMagnitude, Is.GreaterThan(0.000001f));
            return bounds;
        }

        void Capture(RepairPropKind kind, string phase, GameObject item, Camera camera)
        {
            Directory.CreateDirectory(ReportDirectory);
            var bounds = VisibleBounds(item);
            string path = Path.Combine(ReportDirectory, kind + "_" + phase + ".png");
            var record = new CaptureEvidence
            {
                kind = kind.ToString(), phase = phase, imagePath = path, cameraName = camera.name,
                cameraPosition = camera.transform.position, cameraEuler = camera.transform.eulerAngles,
                fieldOfView = camera.fieldOfView, nearClip = camera.nearClipPlane, farClip = camera.farClipPlane,
                itemBoundsCenter = bounds.center, itemBoundsSize = bounds.size,
                supportError = bounds.min.y - (phase == "completed" ? clinic.TradeSupportWorldY(clinic.DeliveryAnchor) : clinic.BenchTopWorldY + 0.0005f),
                itemParent = item.transform.parent.name, viewportCenter = camera.WorldToViewportPoint(bounds.center),
                frustumIntersects = GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), bounds)
            };
            evidence.captures.Add(record);
            Assert.That(record.frustumIntersects, Is.True, kind + "/" + phase + " must intersect the active camera frustum.");
            Assert.That(record.viewportCenter.z, Is.GreaterThan(camera.nearClipPlane));
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                var colors = pixels.GetPixels32();
                int low = 765, high = 0;
                for (int i = 0; i < colors.Length; i += 521)
                {
                    int brightness = colors[i].r + colors[i].g + colors[i].b;
                    low = Math.Min(low, brightness);
                    high = Math.Max(high, brightness);
                }
                record.sampledBrightnessRange = (high - low) / 765f;
                Assert.That(record.sampledBrightnessRange, Is.GreaterThan(0.01f), "Camera render must not be a uniform blank frame: " + path);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(pixels);
            }
        }

        static IEnumerator Until(Func<bool> ready, string reason)
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, reason);
        }

        sealed class FailOnceProbe : IRepairPropTestProbe
        {
            public int Calls { get; private set; }
            public PropTestResult Measure(RepairPropFlow flow, PropTest test)
            {
                Calls++;
                return new PropTestResult(false, true, true, "Scene E2E: injected transient bench measurement failure.");
            }
        }

        sealed class LocalPose
        {
            readonly Transform target, parent;
            readonly Vector3 position, scale;
            readonly Quaternion rotation;
            readonly bool active;
            public LocalPose(Transform target)
            {
                Assert.That(target, Is.Not.Null);
                this.target = target;
                parent = target.parent;
                position = target.localPosition;
                rotation = target.localRotation;
                scale = target.localScale;
                active = target.gameObject.activeSelf;
            }
            public void AssertUnchanged(bool checkActive = true)
            {
                Assert.That(target, Is.Not.Null);
                Assert.That(target.parent, Is.SameAs(parent), target.name);
                Assert.That(Vector3.Distance(target.localPosition, position), Is.LessThan(0.000001f), target.name);
                Assert.That(Quaternion.Angle(target.localRotation, rotation), Is.LessThan(0.001f), target.name);
                Assert.That(Vector3.Distance(target.localScale, scale), Is.LessThan(0.000001f), target.name);
                if (checkActive) Assert.That(target.gameObject.activeSelf, Is.EqualTo(active), target.name);
            }
        }

        [Serializable]
        sealed class SceneEvidence
        {
            public string scene = ScenePath;
            public string input = "Programmatic public controller calls; not human play. Only the failed measurement is injected. Completed captures aim with FirstPersonWalker.LookAt from the current standing position.";
            public string boundaries = "Camera.Render excludes overlay UI. Scanner projection samples prove some geometry is pickable, not all part click routes, occlusion, walking collisions, free grabbing, VR or performance. Delivery uses the existing controller's automatic placement/clear; no separate pickup-from-delivery action exists.";
            public bool completed;
            public List<string> finishedOrders = new List<string>();
            public List<CaptureEvidence> captures = new List<CaptureEvidence>();
        }

        [Serializable]
        sealed class CaptureEvidence
        {
            public string kind, phase, imagePath, cameraName, itemParent;
            public Vector3 cameraPosition, cameraEuler, itemBoundsCenter, itemBoundsSize, viewportCenter;
            public float fieldOfView, nearClip, farClip, sampledBrightnessRange, supportError;
            public bool frustumIntersects;
        }
    }
}
