using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.UI.Tests
{
    // All four props of the night, one after another, worked only by following the visor's own suggestion
    // (RepairPropWorkflowStatus.NextCommand) through the panel's dispatch, then delivered at the counter. Proves every
    // flow can be finished from its guidance alone, and writes a step trace (and, with BORDER_REPAIR_CYBER_SHOTS,
    // screenshots at the milestones) for the flow review.
    public sealed class RepairPropPlaythroughTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        static string Folder => Environment.GetEnvironmentVariable("BORDER_REPAIR_CYBER_SHOTS");
        TwoNightState previousRun;
        string previousSave, isolatedSave;
        Scene loaded;
        UnifiedClinicDirector clinic;
#if UNITY_EDITOR
        UiTestGameView gameView;
#endif

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current; previousSave = TwoNightSave.OverrideDirectory;
            isolatedSave = Path.Combine(Path.GetTempPath(), "RepairPropPlaythrough_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = isolatedSave;
            TwoNightRun.Clear();
            Time.timeScale = 1;
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Requires the authored editor scene.");
            yield break;
#endif
            loaded = SceneManager.GetSceneByPath(ScenePath);
            yield return Until(() => (clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>()) != null && clinic.Initialized, "Clinic must initialize.");
            clinic.Robot.Flow.Rig.Walker.LeaveSystemCursor = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene("PlaythroughCleanup_" + Guid.NewGuid().ToString("N")));
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
#if UNITY_EDITOR
            gameView?.Dispose(); gameView = null;
#endif
            TwoNightRun.Set(previousRun); TwoNightSave.OverrideDirectory = previousSave;
            if (Directory.Exists(isolatedSave)) Directory.Delete(isolatedSave, true);
        }

        [UnityTest]
        public IEnumerator AllFourProps_FinishedByFollowingTheVisorGuide_AndDelivered()
        {
#if UNITY_EDITOR
            gameView = new UiTestGameView(); gameView.Set(1920, 1080);
#endif
            var cart = ClinicSupplies.Products.ToDictionary(p => p.Id, p => 1);
            Assert.That(ClinicSupplies.Purchase(clinic.State, cart, Guid.NewGuid().ToString("N"), out string reason), Is.True, reason);
            var trace = new List<string>();
            for (int item = 0; item < clinic.Station.Session.CaseCount; item++)
                yield return RepairAndDeliverNext(trace, "");
            if (!string.IsNullOrEmpty(Folder))
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllLines(Path.Combine(Folder, "playthrough-trace.txt"), trace);
            }
            Debug.Log("[Playthrough]\n" + string.Join("\n", trace));
        }

        // The receiver's coax fault (the main tour runs the pinned RF-board one): found with the meter and fixed
        // without a part, so the stock is untouched and the order still delivers. The lead fault takes the same
        // part-free path; RepairPropReceiverFaultTests walks it through the guide.
        [UnityTest]
        public IEnumerator Receiver_CoaxFault_FoundByMeterAndDeliveredWithoutAPart()
        {
#if UNITY_EDITOR
            gameView = new UiTestGameView(); gameView.Set(1920, 1080);
#endif
            var cart = ClinicSupplies.Products.ToDictionary(p => p.Id, p => 1);
            Assert.That(ClinicSupplies.Purchase(clinic.State, cart, Guid.NewGuid().ToString("N"), out string reason), Is.True, reason);
            var trace = new List<string>();
            RepairPropFlow.FaultOverride = 2;
            int stock = ClinicSupplies.Available(clinic.State, "rx-rf");
            try { yield return RepairAndDeliverNext(trace, "coax-"); }
            finally { RepairPropFlow.FaultOverride = 1; }
            if (!string.IsNullOrEmpty(Folder))
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllLines(Path.Combine(Folder, "playthrough-coax-trace.txt"), trace);
            }
            Debug.Log("[Playthrough coax]\n" + string.Join("\n", trace));
            Assert.That(trace[0], Does.Contain("Receiver"), "The receiver is the night's first order.");
            Assert.That(ClinicSupplies.Available(clinic.State, "rx-rf"), Is.EqualTo(stock), "No RF board was used.");
            Assert.That(trace.Any(t => t.Contains("measure.coax")) && trace.Any(t => t.Contains("fix.coax")), Is.True);
            Assert.That(trace.Any(t => t.Contains("module.remove.old")), Is.False, "The good board stays in.");
            var problems = new List<string>();
            ClinicSupplies.Validate(clinic.State, problems);
            Assert.That(problems, Is.Empty, "The part-free delivery keeps the supply ledger valid.");
        }

        // The receiver worked by hand, as a player with hand feel on: every part command goes through its gesture,
        // driven by a scripted pointer (turn, pry, unlatch and pull, push home, slide, scrub, crimp, probe), and every
        // gesture must finish and commit. The first screw of each direction is turned by hand; the rest of a set goes
        // to the electric screwdriver (the batch button).
        [UnityTest] public IEnumerator Receiver_ByHand_BoardFault() => ByHand(1, PropFeel.SlideOut, PropFeel.SlideIn);
        [UnityTest] public IEnumerator Receiver_ByHand_CoaxFault() => ByHand(2, PropFeel.Scrub);
        [UnityTest] public IEnumerator Receiver_ByHand_LeadFault() => ByHand(3, PropFeel.Crimp);

        IEnumerator ByHand(int fault, params PropFeel[] special)
        {
#if UNITY_EDITOR
            gameView = new UiTestGameView(); gameView.Set(1920, 1080);
#endif
            var cart = ClinicSupplies.Products.ToDictionary(p => p.Id, p => 1);
            Assert.That(ClinicSupplies.Purchase(clinic.State, cart, Guid.NewGuid().ToString("N"), out string reason), Is.True, reason);
            RepairPropFlow.FaultOverride = fault;
            RepairPropFeel.Enabled = true;
            RepairPropFeelInput.Simulated = true;
            var trace = new List<string>();
            var felt = new HashSet<PropFeel> { PropFeel.Tap };
            try
            {
                yield return Until(() => clinic.ReceiveLampState == UnifiedClinicDirector.PadLamp.On, "The receiver waits at the receive pad.");
                Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True, clinic.LastMessage);
                var bridge = clinic.Station.Inspector.CurrentItem.GetComponent<RepairPropStationBridge>();
                yield return Until(() => bridge.Flow.Session != null && bridge.ContextPanel != null, "Bridge binds.");
                clinic.Counter.DismissDialogue();
                Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True, clinic.LastMessage);
                yield return Until(() => bridge.CanInteract, "Bench is interactive.");
                var flow = bridge.Flow;
                Assert.That(flow.Definition.kind, Is.EqualTo(RepairPropKind.Receiver));
                flow.RollFault();
                var hands = bridge.Gesture;
                Assert.That(hands, Is.Not.Null, "The desktop bench has hands.");
                string tag = "hand-" + flow.ActiveFault.id + "-";
                int steps = 0;
                while (!flow.Completed && steps < 120)
                {
                    var next = RepairPropWorkflowStatus.NextCommand(flow);
                    Assert.That(next, Is.Not.Null, "The guide went silent: " + RepairPropWorkflowStatus.NextStep(flow));
                    var feel = RepairPropFeel.For(next);
                    bridge.ContextPanel.Refresh();
                    var batch = bridge.ContextPanel.BatchButtonFor(next.id);
                    if (batch != null && batch.interactable && felt.Contains(feel))
                    {
                        string label = batch.GetComponentInChildren<UnityEngine.UI.Text>().text;
                        batch.onClick.Invoke();
                        bool screwdriver = hands.AutoRunning;
                        yield return Until(() => !hands.Busy, "The electric screwdriver finishes.");
                        trace.Add(string.Format("{0,3} BATCH {1,-8} {2}", steps, screwdriver ? "电动起子" : "成对", label));
                    }
                    else
                    {
                        bool whole = next.target == flow.Definition.rootObjectName;
                        Assert.That(bridge.SelectTarget(next.target), Is.True, next.target);
                        int before = flow.History.Count;
                        Assert.That(bridge.Perform(next.id, whole), Is.True, next.id + ": " + flow.LastFeedback);
                        if (hands.Busy)
                        {
                            Assert.That(hands.Feel, Is.EqualTo(feel));
                            Assert.That(flow.History.Count, Is.EqualTo(before), next.id + " waits for the hand.");
                            yield return Drive(hands, tag + feel, !felt.Contains(feel));
                            felt.Add(feel);
                        }
                        Assert.That(flow.History.Count, Is.GreaterThan(before), next.id + " committed: " + flow.LastFeedback);
                        trace.Add(string.Format("{0,3} {1,-8} {2,-22} {3} | {4}", steps, feel, next.id, next.label, flow.LastFeedback));
                    }
                    steps++;
                    yield return Until(() => bridge.BenchPresentation == null || !bridge.BenchPresentation.IsAnimating, "Parts settle.");
                    yield return null;
                }
                Assert.That(flow.Completed, Is.True, "Finished by hand within 120 steps.");
                Debug.Log("[ByHand " + flow.ActiveFault.id + "]\n" + string.Join("\n", trace));
                if (!string.IsNullOrEmpty(Folder)) { Directory.CreateDirectory(Folder); File.WriteAllLines(Path.Combine(Folder, tag + "trace.txt"), trace); }
                foreach (var expected in new[] { PropFeel.Unscrew, PropFeel.Screw, PropFeel.Pry, PropFeel.Seat, PropFeel.Unplug, PropFeel.Plug, PropFeel.Probe }.Concat(special))
                    Assert.That(felt, Does.Contain(expected), "Done by hand: " + expected);
                Assert.That(flow.FailedRetests, Is.Zero);
                yield return Until(() => clinic.TradeState == ClinicTradeState.ReadyForDelivery, "The receiver reaches the delivery pad.");
                Assert.That(clinic.Interact(ClinicTradeAction.Deliver), Is.True, clinic.LastMessage);
            }
            finally
            {
                RepairPropFlow.FaultOverride = 1;
                RepairPropFeel.Enabled = false;
                RepairPropFeelInput.Simulated = false; RepairPropFeelInput.Held = false;
            }
        }

        // Moves the scripted pointer the way each motion asks until the gesture commits; shoots the first of each kind
        // half way through.
        IEnumerator Drive(RepairPropGesture hands, string shot, bool takeShot)
        {
            var logic = hands.Logic; var feel = hands.Feel;
            float scale = hands.CanvasScale, mid = (RepairPropGestureLogic.CrimpLow + RepairPropGestureLogic.CrimpHigh) * .5f;
            float deadline = Time.realtimeSinceStartup + 20f;
            int frame = 0, latchedAt = -1;
            RepairPropFeelInput.Held = false; RepairPropFeelInput.Pointer = hands.AnchorScreen;
            yield return null;
            while (hands.Busy && hands.Logic == logic && Time.realtimeSinceStartup < deadline)
            {
                Vector2 at = Vector2.zero; bool held = true;
                switch (feel)
                {
                    case PropFeel.Unscrew: case PropFeel.Screw:
                        float a = (feel == PropFeel.Unscrew ? 1f : -1f) * frame * 12f * Mathf.Deg2Rad;
                        at = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 50f; break;
                    case PropFeel.Unplug:
                        if (logic.Latched && latchedAt < 0) latchedAt = frame;
                        at = latchedAt < 0 ? Vector2.zero : logic.direction * Mathf.Min(90f, (frame - latchedAt) * 8f); break;
                    case PropFeel.Scrub: at = new Vector2(frame % 2 == 0 ? -40f : 40f, frame % 3 * 6f); break;
                    case PropFeel.Crimp: held = logic.Aux < mid; break;
                    case PropFeel.Probe:
                        int k = frame % 14;
                        held = !logic.Contact && k < 10;
                        at = logic.Contact ? Vector2.zero : Vector2.Lerp(logic.Holster, Vector2.zero, k / 9f); break;
                    default: at = logic.direction * Mathf.Min(RepairPropGestureLogic.Distance(feel) * 1.3f, frame * 8f); break;
                }
                RepairPropFeelInput.Pointer = hands.AnchorScreen + at * scale;
                RepairPropFeelInput.Held = held;
                float shown = feel == PropFeel.Crimp ? logic.Aux / mid : feel == PropFeel.Unplug && !logic.Latched ? logic.Aux * .5f : logic.Progress;
                if (takeShot && shown >= .5f && !string.IsNullOrEmpty(Folder))
                {
                    takeShot = false;
                    Directory.CreateDirectory(Folder);
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "flow-" + shot + ".png"));
                }
                frame++;
                yield return null;
            }
            RepairPropFeelInput.Held = false;
            yield return null;
            Assert.That(logic.Done, Is.True, feel + " finished by hand. " + hands.MessageText);
        }

        IEnumerator RepairAndDeliverNext(List<string> trace, string shotPrefix)
        {
            {
                yield return Until(() => clinic.ReceiveLampState == UnifiedClinicDirector.PadLamp.On, "Next order waits at the receive pad.");
                Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True, clinic.LastMessage);
                var bridge = clinic.Station.Inspector.CurrentItem.GetComponent<RepairPropStationBridge>();
                yield return Until(() => bridge.Flow.Session != null && bridge.ContextPanel != null, "Bridge binds.");
                clinic.Counter.DismissDialogue();
                Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True, clinic.LastMessage);
                yield return Until(() => bridge.CanInteract, "Bench is interactive.");
                var flow = bridge.Flow;
                // The first order reaches the bench while the scene loads, before a test can pin its fault; nothing
                // has been done to it yet, so it rolls again under the test's pin.
                flow.RollFault();
                string kind = flow.Definition.kind.ToString();
                trace.Add("=== " + kind + " · " + flow.Definition.repairCase.customerName + " · " + RepairPropText.Item(flow.Definition.kind));
                yield return Shoot(shotPrefix + kind + "-00-intake");
                int steps = 0;
                string phase = null;
                while (!flow.Completed && steps < 120)
                {
                    var next = RepairPropWorkflowStatus.NextCommand(flow);
                    Assert.That(next, Is.Not.Null, kind + ": the guide went silent at step " + steps + " (" + RepairPropWorkflowStatus.NextStep(flow) + ")");
                    string hint = bridge.ArOverlay != null ? bridge.ArOverlay.HintText : "";
                    // A batch button that carries the suggested step is what a player presses (one click for a screw set).
                    bridge.ContextPanel.Refresh();
                    var batch = bridge.ContextPanel.BatchButtonFor(next.id);
                    if (batch != null && batch.interactable)
                    {
                        string label = batch.GetComponentInChildren<UnityEngine.UI.Text>().text;
                        batch.onClick.Invoke();
                        trace.Add(string.Format("{0,3} {1,-10} {2,-28} {3} | {4}", steps, RepairPropWorkflowStatus.Phase(flow), "BATCH", label, flow.LastFeedback));
                        Debug.Log("[Step] " + kind + " " + trace[trace.Count - 1]);
                        steps++;
                        yield return Until(() => bridge.BenchPresentation == null || !bridge.BenchPresentation.IsAnimating, "Parts settle.");
                        yield return null;
                        continue;
                    }
                    bool whole = next.target == flow.Definition.rootObjectName;
                    // As a player does: pick the suggested part (the visor marks it), then press the command.
                    Assert.That(bridge.SelectTarget(next.target), Is.True, kind + ": cannot select " + next.target);
                    bool ok = whole ? bridge.ExecuteWholeItemFromPanel(next.id) : bridge.ExecuteFromPanel(next.id);
                    trace.Add(string.Format("{0,3} {1,-10} {2,-28} {3} | {4} | {5}", steps, RepairPropWorkflowStatus.Phase(flow), next.id, next.label, ok ? "OK" : "REFUSED", flow.LastFeedback));
                    Debug.Log("[Step] " + kind + " " + trace[trace.Count - 1]);
                    Assert.That(ok, Is.True, kind + ": suggested " + next.id + " was refused: " + flow.LastFeedback);
                    steps++;
                    yield return Until(() => bridge.BenchPresentation == null || !bridge.BenchPresentation.IsAnimating, "Parts settle.");
                    yield return null;
                    var now = RepairPropWorkflowStatus.Phase(flow).ToString();
                    if (now != phase) { phase = now; yield return Shoot(shotPrefix + kind + "-" + steps.ToString("00") + "-" + now); }
                    else if (next.kind == PropCommandKind.Measure) yield return Shoot(shotPrefix + kind + "-" + steps.ToString("00") + "-" + next.id);
                }
                Assert.That(flow.Completed, Is.True, kind + " finished within 120 suggested steps.");
                trace.Add("    clicks: " + steps);
                Assert.That(steps, Is.LessThanOrEqualTo(24), kind + ": screw sets and power pairs are one click each.");
                yield return Until(() => clinic.TradeState == ClinicTradeState.ReadyForDelivery, kind + " reaches the delivery pad.");
                Assert.That(clinic.Interact(ClinicTradeAction.Deliver), Is.True, clinic.LastMessage);
                yield return null; yield return null;
            }
        }

        IEnumerator Shoot(string name)
        {
            for (int i = 0; i < 12; i++) yield return null;
            if (string.IsNullOrEmpty(Folder)) yield break;
            // Let the log finish typing (HudTypeOn) so the shot carries the whole result and its story note.
            float settle = Time.realtimeSinceStartup + 0.8f;
            while (Time.realtimeSinceStartup < settle) yield return null;
            Directory.CreateDirectory(Folder);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "flow-" + name + ".png"));
            yield return null;
        }

        static IEnumerator Until(Func<bool> ready, string reason)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, reason);
        }
    }
}
