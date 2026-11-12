using System;
using System.Collections;
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
    // The guided first order: the card follows the game from the counter to the bench, the fault is the one that needs
    // a new part, the flow holds the player to the step in hand, the short HUD folds the extra rows away, and skipping
    // gives everything back.
    public sealed class ClinicTutorialTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        TwoNightState previousRun;
        string previousSave, isolatedSave;
        Scene loaded;
        UnifiedClinicDirector clinic;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current; previousSave = TwoNightSave.OverrideDirectory;
            isolatedSave = Path.Combine(Path.GetTempPath(), "ClinicTutorial_" + Guid.NewGuid().ToString("N"));
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
            if (ClinicTutorialRuntime.Current != null) Object.Destroy(ClinicTutorialRuntime.Current.gameObject);
            RepairPropWorkspaceLayout.ResetCompact();
            yield return null;
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene("ClinicTutorialCleanup_" + Guid.NewGuid().ToString("N")));
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
            TwoNightRun.Set(previousRun); TwoNightSave.OverrideDirectory = previousSave;
            if (Directory.Exists(isolatedSave)) Directory.Delete(isolatedSave, true);
        }

        static IEnumerator Until(Func<bool> ready, string reason)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, reason);
        }

        [UnityTest]
        public IEnumerator SceneLoadedLater_DoesNotStartTheLesson()
        {
            yield return null;
            Assert.That(ClinicTutorialRuntime.Current, Is.Null, "Only a game started in the clinic scene is guided.");
            Assert.That(RepairPropWorkspaceLayout.Compact, Is.False);
            Assert.That(RepairPropFlow.ExecuteGate, Is.Null);
        }

        [UnityTest]
        public IEnumerator FirstOrder_IsGuidedStepByStep()
        {
            var tutorial = ClinicTutorialRuntime.Begin();
            Assert.That(RepairPropWorkspaceLayout.Compact, Is.True, "The lesson comes with the short HUD.");
            yield return Until(() => tutorial.StepId == "receive", "It starts at the counter.");
            Assert.That(tutorial.Card.gameObject.activeInHierarchy, Is.True);
            StringAssert.Contains("收件台", tutorial.StepText);

            Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True, clinic.LastMessage);
            yield return Until(() => tutorial.StepId == "dialogue", "The customer talks first.");
            clinic.Counter.DismissDialogue();
            yield return Until(() => tutorial.StepId == "carry", "Then the item is carried to the bench.");

            var bridge = clinic.Station.Inspector.CurrentItem.GetComponent<RepairPropStationBridge>();
            yield return Until(() => bridge.Flow.Session != null, "Bridge must attach.");
            var flow = bridge.Flow;
            Assert.That(flow.ActiveFault, Is.Not.Null);
            CollectionAssert.Contains(flow.ActiveFault.route, "module.install.new", "The lesson's fault is the one that needs a new part.");

            Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True, clinic.LastMessage);
            yield return Until(() => bridge.CanInteract, "Bench must be interactive.");
            yield return Until(() => tutorial.StepId.StartsWith("cmd:", StringComparison.Ordinal), "At the bench it names a command.");
            for (int i = 0; i < 30; i++) yield return null;

            // The card takes the guide band's place and marks the button to press.
            Assert.That(bridge.ArOverlay.GuideBand.gameObject.activeSelf, Is.False, "The guide band steps aside for the lesson card.");
            Assert.That(tutorial.Marked, Is.Not.Null, "The first bench step is a whole-item command with a button to mark.");
            var corners = new Vector3[4]; tutorial.Card.GetWorldCorners(corners);
            var cardRect = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
            bridge.ContextPanel.PanelRectTransform.GetWorldCorners(corners);
            Assert.That(cardRect.Overlaps(Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y)), Is.False, "The lesson card keeps off the panel column.");

            // Short HUD: the extra rows are folded away until asked for.
            var panel = bridge.ContextPanel;
            Assert.That(panel.TabButton(RepairPropWorkspaceTab.Order).gameObject.activeInHierarchy, Is.False);
            Assert.That(panel.TargetPicker.gameObject.activeInHierarchy, Is.False);
            panel.DetailToggle.onClick.Invoke();
            yield return null; yield return null;
            Assert.That(panel.TabButton(RepairPropWorkspaceTab.Order).gameObject.activeInHierarchy, Is.True, "The toggle brings the full panel back.");
            Assert.That(panel.TargetPicker.gameObject.activeInHierarchy, Is.True);
            panel.DetailToggle.onClick.Invoke();
            yield return null;

            // Follow the guide until the flow itself would allow something else: the lesson must refuse it.
            bool refused = false;
            for (int step = 0; step < 40 && !refused; step++)
            {
                var next = RepairPropWorkflowStatus.NextCommand(flow);
                Assert.That(next, Is.Not.Null, "The guide always has a step before the part must be bought (step " + step + ").");
                var gate = RepairPropFlow.ExecuteGate;
                RepairPropFlow.ExecuteGate = null;
                var stray = flow.Definition.commands.FirstOrDefault(c => c.kind != PropCommandKind.Inspect && c.kind != PropCommandKind.Pose
                    && ClinicTutorialRuntime.Family(c.id) != ClinicTutorialRuntime.Family(next.id) && flow.CanExecute(c.id, c.target, c.tool, out _));
                RepairPropFlow.ExecuteGate = gate;
                if (stray != null)
                {
                    yield return null;   // a fresh frame: the gate's cached step is this one
                    Assert.That(flow.CanExecute(stray.id, stray.target, stray.tool, out string reason), Is.False, stray.id + " is not the step in hand (" + next.id + ").");
                    StringAssert.StartsWith("教学中", reason);
                    Assert.That(flow.CanExecute(next.id, next.target, next.tool, out string why), Is.True, why);
                    refused = true;
                    break;
                }
                Assert.That(flow.TryExecute(next.id, next.target, next.tool), Is.True, next.id);
                yield return null;
            }
            Assert.That(refused, Is.True, "Somewhere along the order the flow offers more than the step in hand, and the lesson holds the line.");

            // Skipping gives everything back, except the short HUD.
            tutorial.Skip();
            yield return null; yield return null;
            Assert.That(ClinicTutorialRuntime.Current, Is.Null);
            Assert.That(RepairPropFlow.ExecuteGate, Is.Null);
            Assert.That(RepairPropFlow.FaultChooser, Is.Null);
            Assert.That(bridge.ArOverlay.GuideBand.gameObject.activeSelf, Is.True, "The guide band is back.");
            Assert.That(RepairPropWorkspaceLayout.Compact, Is.True);
        }
    }
}
