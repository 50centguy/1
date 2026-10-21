using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Data;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.UI.Tests
{
    // Walks the receiver through the guarded bridge API and records UI composites at fixed GameView sizes.
    // Only public API that predates the 2026-10-19 reorganization is used, so the same run yields before/after images.
    public sealed class RepairPropWorkspaceCaptureTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        static readonly Vector2Int[] Sizes = { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1024, 768), new Vector2Int(2560, 1440) };
        TwoNightState previousRun;
        string previousSave, isolatedSave;
        float previousTimeScale;
        int previousWidth, previousHeight;
        FullScreenMode previousScreenMode;
        Scene loaded;
        UnifiedClinicDirector clinic;
        RepairPropStationBridge bridge;
#if UNITY_EDITOR
        UiTestGameView gameView;
#endif
        RepairPropFlow Flow => bridge.Flow;
        RepairStationController Station => clinic.Station;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current;
            previousSave = TwoNightSave.OverrideDirectory;
            previousTimeScale = Time.timeScale;
            previousWidth = Screen.width; previousHeight = Screen.height; previousScreenMode = Screen.fullScreenMode;
#if UNITY_EDITOR
            if (Application.isBatchMode)
                foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    if (window.GetType().FullName == "UnityEditor.SettingsWindow") window.Close();
#endif
            isolatedSave = Path.Combine(Path.GetTempPath(), "RepairPropUICapture_" + Guid.NewGuid().ToString("N"));
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
            yield return Until(() =>
            {
                clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>();
                return clinic != null && clinic.Initialized;
            }, "The real clinic must initialize.");
            clinic.Robot.Flow.Rig.Walker.LeaveSystemCursor = true;
            // Mechanical/UI coverage starts with a genuinely purchased replacement, not free prototype stock.
            Assert.That(ClinicSupplies.Purchase(clinic.State, new System.Collections.Generic.Dictionary<string, int> { { "rx-rf", 1 } },
                System.Guid.NewGuid().ToString("N"), out string stockReason), Is.True, stockReason);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (loaded.IsValid() && loaded.isLoaded)
            {
                var cleanup = SceneManager.CreateScene("RepairPropUICaptureCleanup_" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
#if UNITY_EDITOR
            gameView?.Dispose(); gameView = null;
#endif
            Screen.SetResolution(previousWidth, previousHeight, previousScreenMode);
            for (int i = 0; i < 30 && (Screen.width != previousWidth || Screen.height != previousHeight); i++) yield return null;
            TwoNightRun.Set(previousRun);
            TwoNightSave.OverrideDirectory = previousSave;
            Time.timeScale = previousTimeScale;
            if (!string.IsNullOrEmpty(isolatedSave) && Directory.Exists(isolatedSave))
            {
                string resolved = Path.GetFullPath(isolatedSave);
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                Assert.That(resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase), Is.True);
                Assert.That(Path.GetFileName(resolved).StartsWith("RepairPropUICapture_", StringComparison.Ordinal), Is.True);
                Directory.Delete(resolved, true);
            }
            clinic = null; bridge = null;
        }

        [UnityTest]
        public IEnumerator Receiver_WorkspaceStates_CompositeAtFixedGameViewSizes()
        {
            // Without BORDER_REPAIR_UI_SHOTS the same walk runs as a smoke test and writes no files.
            Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True, clinic.LastMessage);
            bridge = Station.Inspector.CurrentItem.GetComponent<RepairPropStationBridge>();
            yield return Until(() => Flow.Session != null, "Bridge must attach.");
            clinic.Counter.DismissDialogue();
            Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True, clinic.LastMessage);
            yield return Until(() => bridge.CanInteract && CameraSettled(), "Bench must settle.");
            Assert.That(Flow.Definition.kind, Is.EqualTo(RepairPropKind.Receiver));

            yield return Shoot("01-intake-bench-idle");
            Assert.That(bridge.SelectTarget("RX_CoverScrew_01"), Is.True);
            yield return Shoot("02-assembled-screw-selected");
            Select(Flow.Definition.rootObjectName);
            Do("power.off");
            Do("fixture.disconnect");
            Select("RX_CoverScrew_01");
            yield return Shoot("03-powered-off-before-diagnosis");
            Do("fixture.connect");
            Do("power.on");
            Do("test.signal");
            Select(Flow.Definition.rootObjectName);
            yield return Shoot("04-first-test-expected-fault");
            Do("test.identity");
            Do("power.off");
            Do("fixture.disconnect");
            Select(Flow.Definition.rootObjectName);
            yield return Shoot("05-diagnosed-safe-assembled");
            yield return AppearanceShot();
            for (int i = 1; i <= 4; i++) Do("remove.RX_CoverScrew_" + i.ToString("00"));
            Do("cover.remove");
            Do("isolate");
            Do("harness.release");
            Do("remove.RX_RFScrew_01");
            Do("remove.RX_RFScrew_02");
            Do("module.remove.old");
            yield return WaitForBenchSettled();
            Select("RX_RF_Daughterboard_New");
            yield return Shoot("07-module-out-replacement-selected");
            Do("module.install.new");
            Do("install.RX_RFScrew_01");
            Do("install.RX_RFScrew_02");
            Do("harness.connect");
            Do("supply.connect");
            yield return WaitForBenchSettled();
            Select("RX_RearCover");
            yield return Shoot("08-reassembly-cover-parked");
            Do("cover.install");
            for (int i = 1; i <= 4; i++) Do("install.RX_CoverScrew_" + i.ToString("00"));
            Do("fixture.connect");
            Do("power.on");
            yield return WaitForBenchSettled();
            Select(Flow.Definition.rootObjectName);
            yield return Shoot("09-retest-pending");
            Do("test.signal");
            Do("test.identity");
            Select(Flow.Definition.rootObjectName);
            yield return Shoot("10-verified-delivery-available");
            Assert.That(Flow.Completed, Is.False, "Capture never confirms delivery.");
        }

        IEnumerator AppearanceShot()
        {
            // Open the desktop appearance editor through its real button; no cosmetics assembly reference is needed.
            // Workspace mode buttons (after 2026-10-19) or the original floating panel's "+" (baseline).
            var root = GameObject.Find("Cosmetics_Runtime");
            if (root == null) yield break;
            var buttons = Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            var opener = buttons.FirstOrDefault(b => b.isActiveAndEnabled && b.name == "模式_外观")
                ?? root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.isActiveAndEnabled && b.GetComponentInChildren<Text>(true)?.text == "+");
            if (opener == null) yield break;
            opener.onClick.Invoke();
            yield return null;
            yield return null;
            var spray = root.GetComponentsInChildren<Button>(true)
                .FirstOrDefault(b => b.isActiveAndEnabled && b.interactable && b.GetComponentInChildren<Text>(true)?.text == "喷涂");
            if (spray != null) spray.onClick.Invoke();
            yield return null;
            yield return Shoot("06-appearance-spray");
            buttons = Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            var off = buttons.FirstOrDefault(b => b.isActiveAndEnabled && b.name == "模式_维修")
                ?? root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.isActiveAndEnabled && b.name == "维修");
            if (off != null) off.onClick.Invoke();
            yield return null;
            var close = root.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.isActiveAndEnabled && b.GetComponentInChildren<Text>(true)?.text == "-");
            if (close != null) close.onClick.Invoke();
            yield return null;
            yield return Until(() => bridge.CanInteract, "Leaving appearance editing must restore repair ownership.");
        }

        IEnumerator Shoot(string state)
        {
            foreach (var size in Sizes)
            {
#if UNITY_EDITOR
                if (gameView == null) gameView = new UiTestGameView();
                gameView.Set(size.x, size.y);
#endif
                Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
                for (int i = 0; i < 30 && (Screen.width != size.x || Screen.height != size.y); i++) yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return null;
                var item = Station.Inspector.CurrentItem.GetComponentsInChildren<Renderer>();
                var parked = bridge.BenchPresentation != null && bridge.BenchPresentation.ParkingRoot != null
                    ? bridge.BenchPresentation.ParkingRoot.transform.Cast<Transform>().ToArray() : new Transform[0];
                RepairPropWorkspaceCapture.Capture(Station.Inspector.ViewCamera, state, size, item, parked);
            }
        }

        void Select(string target) => Assert.That(bridge.SelectTarget(target), Is.True, target);

        void Do(string id)
        {
            var command = Flow.Definition.FindCommand(id);
            Assert.That(command, Is.Not.Null, id);
            Assert.That(bridge.SelectTarget(command.target), Is.True, id);
            Assert.That(bridge.ExecuteFromPanel(id), Is.True, id + ": " + Flow.LastFeedback);
        }

        IEnumerator WaitForBenchSettled()
        {
            yield return Until(() => bridge.BenchPresentation == null || !bridge.BenchPresentation.IsAnimating, "Bench animation must settle.");
            yield return null;
        }

        bool CameraSettled()
        {
            var inspector = Station.Inspector;
            var camera = inspector.ViewCamera;
            return camera.isActiveAndEnabled
                && Mathf.Abs(Vector3.Distance(camera.transform.position, inspector.LookPoint) - inspector.TargetDistance) < .001f
                && Vector3.Angle(camera.transform.forward, inspector.LookPoint - camera.transform.position) < .1f;
        }

        static IEnumerator Until(Func<bool> ready, string reason)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, reason);
        }
    }
}
