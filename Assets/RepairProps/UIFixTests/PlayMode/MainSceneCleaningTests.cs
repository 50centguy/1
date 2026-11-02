using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Cleaning;
using BorderRepair.Core;
using BorderRepair.FirstOrder;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.UI.Tests
{
    // The cleaning loop in the authored main scene, order by order, through the same path as held input. Needs the
    // installer's output: ClinicCleaningInstaller.RunBatch.
    public sealed class MainSceneCleaningTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        string previousSave, isolatedSave;
        UnifiedClinicDirector clinic;
        ClinicCleaningController cleaning;
        Mouse mouse;
        Keyboard keyboard;
        Action cleanupMouse;
        CursorLockMode previousCursor;
        bool previousCursorVisible;
        TwoNightState previousRun;
        Scene loaded;

        RepairStationController Station => clinic.Station;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousSave = TwoNightSave.OverrideDirectory;
            previousRun = TwoNightRun.Current;
            previousCursor = Cursor.lockState; previousCursorVisible = Cursor.visible;
            isolatedSave = Path.Combine(Path.GetTempPath(), "MainSceneCleaning_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = isolatedSave;
            TwoNightRun.Clear();
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
                cleaning = Object.FindFirstObjectByType<ClinicCleaningController>();
                return clinic != null && clinic.Initialized && cleaning != null;
            }, "The main scene must carry a cleaning controller (run ClinicCleaningInstaller.RunBatch).");
            clinic.Robot.Flow.Rig.Walker.LeaveSystemCursor = true;
            mouse = FirstOrderAcceptanceDriver.CreateVirtualMouse(out cleanupMouse);
            keyboard = InputSystem.AddDevice<Keyboard>("MainSceneCleaningKeyboard"); keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(-100, -100) });
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (cleaning != null) cleaning.SetCleaning(false);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            cleanupMouse?.Invoke(); cleanupMouse = null;
            Cursor.lockState = previousCursor; Cursor.visible = previousCursorVisible;
            // Unload before restoring the run: the scene's cosmetic controller reads the night's state every frame,
            // so clearing it while the scene is still live logs a null dereference instead of a clean teardown.
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene("MainSceneCleaningCleanup_" + Guid.NewGuid().ToString("N")));
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
            TwoNightRun.Set(previousRun);
            TwoNightSave.OverrideDirectory = previousSave;
            if (!string.IsNullOrEmpty(isolatedSave) && Directory.Exists(isolatedSave)) Directory.Delete(isolatedSave, true);
        }

        [UnityTest]
        public IEnumerator EveryPropCanBeWipedByHeldInput_AndFinishesWithoutTouchingTheShell()
        {
            // The night's first order arrives the real way. The other three go on the same bench directly (without their
            // repair adapter, which would refuse a case that isn't theirs): playing out whole repairs is the playthrough tests' job.
            yield return ReceiveAndBench();
            foreach (var kind in new[] { "Receiver", "Cartridge", "Power", "Omamori" })
            {
                var item = Station.Inspector.CurrentItem;
                if (item.name != kind)
                {
#if UNITY_EDITOR
                    item = Station.Inspector.Mount(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RepairProps/Prefabs/" + kind + ".prefab"));
                    Object.Destroy(item.GetComponent<RepairPropStationBridge>());
                    yield return null;
#endif
                }
                Assert.That(item.name, Is.EqualTo(kind));
                yield return Until(() => cleaning.Surfaces.Count > 0 && cleaning.Surfaces[0] != null && cleaning.Surfaces[0].transform.IsChildOf(item.transform), kind + ": the controller must bind this item's surfaces.");
                Assert.That(cleaning.Available, Is.True, kind + ": " + cleaning.UnavailableReason);

                // The shell renderers stay as authored: appearance editing depends on it.
                var surfaces = cleaning.Surfaces.ToArray();
                foreach (var surface in surfaces)
                {
                    var filter = surface.Target.GetComponent<MeshFilter>();
                    Assert.That(filter.sharedMesh, Is.Not.SameAs(surface.CleaningMesh), kind + " must keep its authored mesh.");
                    Assert.That(surface.Target.sharedMaterials.Any(m => m != null && m.shader.name.StartsWith("BorderRepair/Dirt")), Is.False, kind + " must keep its authored materials.");
                }

                // C, through the keyboard.
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.C)); yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                Assert.That(cleaning.Cleaning, Is.True, kind + ": C must pick up the cleaning tool.");
                Assert.That(Station.CleaningActive, Is.True);
                Capture(kind + "-dirty");

                Vector2 point = default;
                yield return Until(() => TryFindDirtyPoint(out point), kind + ": some dirty shell must be reachable from the bench view.", 3);
                float before = cleaning.Progress;
                float end = Time.realtimeSinceStartup + .8f;
                while (Time.realtimeSinceStartup < end)
                {
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = point + new Vector2(Mathf.Sin(Time.realtimeSinceStartup * 5) * 20, 0), buttons = 1 });
                    yield return null;
                }
                InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null;
                Assert.That(cleaning.Progress, Is.GreaterThan(before), kind + ": held input must remove dirt.");
                Assert.That(cleaning.Progress, Is.LessThan(.9f), kind + ": one wipe must not clean the whole item.");
                Assert.That(cleaning.LastWipe, Is.Not.EqualTo(ClinicCleaningController.WipeResult.Blocked), kind);

                // The dirt is patches, not a coat: part of the exposed surface starts clean and part is thick enough to scrape.
                foreach (var surface in surfaces)
                {
                    var pixels = surface.MaskTexture.GetPixels32();
                    Assert.That(pixels.Any(p => p.r > 140), Is.True, kind + "/" + surface.name + " has no thick dirt.");
                }

                // A small part may already have finished under the wipe: only the rest still owe a completion event.
                int stillDirty = surfaces.Count(s => !s.IsClean), cleaned = 0;
                foreach (var surface in surfaces) surface.Cleaned += () => cleaned++;
                foreach (var surface in surfaces) surface.CleanAll();
                Assert.That(cleaning.AllClean, Is.True, kind);
                Assert.That(cleaning.Progress, Is.EqualTo(1).Within(.001f), kind);
                Assert.That(cleaned, Is.EqualTo(stillDirty), kind + ": finishing must raise each surface's completion event.");
                yield return null;
                Capture(kind);
                cleaning.SetCleaning(false);
                Assert.That(Station.CleaningActive, Is.False, kind);
                var bridge = item.GetComponent<RepairPropStationBridge>();
                if (bridge != null) Assert.That(bridge.CanInteract, Is.True, kind + ": repair input must come back.");
            }
        }

        [UnityTest]
        public IEnumerator AppearanceEditingKeepsItsSurfacesAndTheCleaningToolWaits()
        {
            yield return ReceiveAndBench();
            yield return Until(() => cleaning.Surfaces.Count > 0, "Surfaces must bind.");
            var item = Station.Inspector.CurrentItem;
            Assert.That(item.GetComponentsInChildren<CleanableSurface>(true).All(s => s.Ready), Is.True);
            // Whoever else holds the bench's claim (appearance editing, the shop) keeps it.
            Station.CleaningActive = true;
            Assert.That(cleaning.Available, Is.False);
            Assert.That(cleaning.SetCleaning(true), Is.False);
            Assert.That(cleaning.UnavailableReason, Does.Contain("外观"));
            Station.CleaningActive = false;
            Assert.That(cleaning.Available, Is.True);
        }

        [UnityTest]
        public IEnumerator F4CleansTheBoundItemAndLeavesTheSceneryDirty()
        {
            var keysHost = new GameObject("F4TestKeys");
            try
            {
                var keys = keysHost.AddComponent<ClinicDevKeysRuntime>();
                yield return ReceiveAndBench();
                yield return Until(() => cleaning.Surfaces.Count > 0, "Surfaces must bind.");
                var bound = cleaning.Surfaces.ToArray();
                // Whatever is dirty outside the controller when F4 runs: UNIT 07's shells, per the installer.
                var scenery = Object.FindObjectsByType<CleanableSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(s => s.Ready && !s.IsClean && !bound.Contains(s)).ToArray();
                Assert.That(scenery.Length, Is.GreaterThan(0), "The scene must have dirty surfaces outside the bench item, or this proves nothing.");
                var before = scenery.Select(s => s.Progress).ToArray();

                Assert.That(keys.CleanAll(), Is.True, keys.LastMessage);
                Assert.That(cleaning.AllClean, Is.True, "F4 finishes the bound item.");
                for (int i = 0; i < scenery.Length; i++)
                {
                    Assert.That(scenery[i].IsClean, Is.False, scenery[i].name + " is scenery: F4 must leave it dirty.");
                    Assert.That(scenery[i].Progress, Is.EqualTo(before[i]), scenery[i].name + " is scenery: F4 must not touch it.");
                }

                Assert.That(keys.CleanAll(), Is.False, "Nothing left to clean is a refusal.");
                Assert.That(keys.LastMessage, Does.Contain("当前维修物品已经全部干净"));
            }
            finally { Object.Destroy(keysHost); }
        }

        IEnumerator ReceiveAndBench()
        {
            Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True, clinic.LastMessage);
            var item = Station.Inspector.CurrentItem;
            var bridge = item.GetComponent<RepairPropStationBridge>();
            Assert.That(bridge, Is.Not.Null);
            yield return Until(() => bridge.Flow.Session != null, "The mounted item must bind.");
            clinic.Counter.DismissDialogue();
            Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True, clinic.LastMessage);
            yield return Until(() => bridge.CanInteract, "The bench must accept the item.");
            yield return new WaitForSecondsRealtime(.9f);   // camera settles
        }

        bool TryFindDirtyPoint(out Vector2 found)
        {
            found = default;
            var camera = Station.Inspector.ViewCamera;
            Physics.SyncTransforms();
            var candidates = new List<Vector2>();
            for (int y = camera.pixelHeight / 6; y < camera.pixelHeight * 5 / 6; y += 8)
                for (int x = camera.pixelWidth / 6; x < camera.pixelWidth * 5 / 6; x += 8) candidates.Add(new Vector2(x, y));
            var centre = new Vector2(camera.pixelWidth * .5f, camera.pixelHeight * .5f);
            foreach (var point in candidates.OrderBy(p => (p - centre).sqrMagnitude))
            {
                if (cleaning.TryGetCleaningHit(point, out var surface, out var hit) && surface.RemainingAt(hit.textureCoord2) > .3f)
                { found = point; return true; }
            }
            return false;
        }

        void Capture(string stage)
        {
            string folder = "Logs/cleaning-main"; Directory.CreateDirectory(folder);
            using var frame = new ClinicOffscreenFrame(Station.Inspector.ViewCamera, 1280, 720);
            frame.Render(); frame.Render(); frame.SavePng(folder + "/" + stage + ".png");
        }

        static IEnumerator Until(Func<bool> condition, string message, float seconds = 20)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail(message);
                yield return null;
            }
        }
    }
}
