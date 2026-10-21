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
            {
                yield return Until(() => clinic.ReceiveLampState == UnifiedClinicDirector.PadLamp.On, "Next order waits at the receive pad.");
                Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True, clinic.LastMessage);
                var bridge = clinic.Station.Inspector.CurrentItem.GetComponent<RepairPropStationBridge>();
                yield return Until(() => bridge.Flow.Session != null && bridge.ContextPanel != null, "Bridge binds.");
                clinic.Counter.DismissDialogue();
                Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True, clinic.LastMessage);
                yield return Until(() => bridge.CanInteract, "Bench is interactive.");
                var flow = bridge.Flow;
                string kind = flow.Definition.kind.ToString();
                trace.Add("=== " + kind + " · " + flow.Definition.repairCase.customerName + " · " + RepairPropText.Item(flow.Definition.kind));
                yield return Shoot(kind + "-00-intake");
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
                    if (now != phase) { phase = now; yield return Shoot(kind + "-" + steps.ToString("00") + "-" + now); }
                }
                Assert.That(flow.Completed, Is.True, kind + " finished within 120 suggested steps.");
                trace.Add("    clicks: " + steps);
                Assert.That(steps, Is.LessThanOrEqualTo(24), kind + ": screw sets and power pairs are one click each.");
                yield return Until(() => clinic.TradeState == ClinicTradeState.ReadyForDelivery, kind + " reaches the delivery pad.");
                Assert.That(clinic.Interact(ClinicTradeAction.Deliver), Is.True, clinic.LastMessage);
                yield return null; yield return null;
            }
            if (!string.IsNullOrEmpty(Folder))
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllLines(Path.Combine(Folder, "playthrough-trace.txt"), trace);
            }
            Debug.Log("[Playthrough]\n" + string.Join("\n", trace));
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
