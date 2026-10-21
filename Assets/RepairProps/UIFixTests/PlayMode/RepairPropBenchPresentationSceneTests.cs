using System;
using System.Collections;
using System.IO;
using System.Linq;
using BorderRepair.Core;
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

namespace BorderRepair.RepairProps.UI.Tests
{
    public sealed class RepairPropBenchPresentationSceneTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        const string Evidence = "C:/Users/Administrator/Documents/Codex/2026-09-29/border-repair-station-art/work/repair-props-feedback-2026-10-18/Evidence";
        UnifiedClinicDirector clinic;
        RepairPropStationBridge bridge;
        Scene loaded;
        TwoNightState previousRun;
        string previousSave;
        float previousTime;
        RepairPropFlow Flow => bridge.Flow;
        RepairPropBenchPresentation Presentation => bridge.BenchPresentation;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current;
            previousSave = TwoNightSave.OverrideDirectory;
            previousTime = Time.timeScale;
            TwoNightSave.OverrideDirectory = Path.Combine(Path.GetTempPath(), "RepairPropPresentation_" + Guid.NewGuid().ToString("N"));
            TwoNightRun.Clear(); Time.timeScale = 1;
#if UNITY_EDITOR
            if (Application.isBatchMode)
                foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    if (window.GetType().FullName == "UnityEditor.SettingsWindow") window.Close();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Requires the real authored editor scene."); yield break;
#endif
            loaded = SceneManager.GetSceneByPath(ScenePath);
            yield return Until(() => { clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>(); return clinic != null && clinic.Initialized; });
            Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True);
            Assert.That(ClinicSupplies.Purchase(clinic.State, new System.Collections.Generic.Dictionary<string, int> { { "rx-rf", 1 } },
                System.Guid.NewGuid().ToString("N"), out string stockReason), Is.True, stockReason);
            bridge = clinic.Station.Inspector.CurrentItem.GetComponent<RepairPropStationBridge>();
            yield return Until(() => Flow.Session != null);
            clinic.Counter.DismissDialogue();
            Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True);
            yield return Until(() => bridge.CanInteract && Presentation.ParkingRoot.activeSelf);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene("PresentationCleanup_" + Guid.NewGuid().ToString("N")));
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
            TwoNightRun.Set(previousRun); TwoNightSave.OverrideDirectory = previousSave; Time.timeScale = previousTime;
        }

        [UnityTest]
        public IEnumerator ScrewUnthreadsParksOnTableAndReturnsWithoutChangingInspectionCenter()
        {
            Assert.That(clinic.Station.Inspector.BenchMounted, Is.True);
            Assert.That(Presentation.AllSlotsFit, Is.True, "All part footprints must fit the actual bench.");
            Do("test.signal"); Do("test.identity"); Do("power.off"); Do("fixture.disconnect");
            var inspector = clinic.Station.Inspector;
            var pivot = inspector.LookPoint;
            var modelPosition = bridge.transform.position;
            var modelRotation = bridge.transform.rotation;
            Do("remove.RX_CoverScrew_01");
            Assert.That(Presentation.IsAnimating, Is.True);
            var visual = Presentation.ParkedVisual("RX_CoverScrew_01");
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(visual.gameObject.activeInHierarchy, Is.True);
            var during = visual.position;
            var duringRotation = visual.rotation;
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(Quaternion.Angle(duringRotation, visual.rotation), Is.GreaterThan(10), "A visible unthreading rotation is required.");
            yield return Until(() => !Presentation.IsAnimating);
            Assert.That(Vector3.Distance(during, visual.position), Is.GreaterThan(.01f));
            AssertGrounded(visual);
            var parkedPosition = visual.position;
            Assert.That(Flow.GetObject("RX_CoverScrew_01").GetComponentsInChildren<Renderer>(true).All(r => !r.enabled), Is.True,
                "The floating canonical copy must not remain visible beside the animated part.");
            Capture("receiver-screw-parked", inspector.ViewCamera);
            var cameraPosition = inspector.ViewCamera.transform.position;
            var cameraRotation = inspector.ViewCamera.transform.rotation;
            inspector.Rotate(new Vector2(120, 28)); inspector.SnapToTarget();
            yield return null; yield return null;
            // Dragging lifts the item off the bench and turns it in the air; the camera does not move at all.
            Assert.That(Quaternion.Angle(modelRotation, bridge.transform.rotation), Is.GreaterThan(100));
            Assert.That(inspector.Lift, Is.GreaterThan(0f));
            Assert.That(Vector3.Distance(pivot + Vector3.up * inspector.Lift, inspector.LookPoint), Is.LessThan(.001f),
                "The item turns about the centre of its assembled visible meshes, which only rises by the lift.");
            Assert.That(Vector3.Distance(cameraPosition, inspector.ViewCamera.transform.position), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(cameraRotation, inspector.ViewCamera.transform.rotation), Is.LessThan(.01f));
            var held = bridge.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            Assert.That(held.Min(r => r.bounds.min.y), Is.GreaterThan(clinic.BenchTopWorldY), "The held item must clear the bench.");
            Assert.That(Vector3.Distance(parkedPosition, visual.position), Is.LessThan(.0001f));
            AssertGrounded(visual);
            Capture("receiver-turned-120", inspector.ViewCamera);
            inspector.ResetView(true); yield return null; yield return null;
            Assert.That(inspector.Lift, Is.EqualTo(0f));
            Assert.That(inspector.TryGetStableSupportBottom(out float bottom), Is.True);
            Assert.That(bottom, Is.EqualTo(clinic.BenchTopWorldY).Within(.002f), "Reset puts the item back down on the bench.");
            Assert.That(Vector3.Distance(cameraPosition, inspector.ViewCamera.transform.position), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(pivot, inspector.LookPoint), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(modelPosition, bridge.transform.position), Is.LessThan(.001f));
            Physics.SyncTransforms();
            var marker = visual.GetComponent<RepairPropParkedPart>();
            Assert.That(marker.Owner, Is.SameAs(Flow));
            Assert.That(marker.Target, Is.EqualTo("RX_CoverScrew_01"));
            var bounds = BoundsOf(visual);
            var ray = new Ray(bounds.center + Vector3.up * .2f, Vector3.down);
            Assert.That(Physics.RaycastAll(ray, .25f).Any(h => h.transform.GetComponentInParent<RepairPropParkedPart>() == marker), Is.True,
                "The tabletop part needs a real picking collider and its original guarded target.");
            Do("install.RX_CoverScrew_01");
            Assert.That(Presentation.IsAnimating, Is.True);
            yield return Until(() => !Presentation.IsAnimating);
            Assert.That(visual.gameObject.activeSelf, Is.False);
            Assert.That(Flow.GetObject("RX_CoverScrew_01").GetComponentsInChildren<Renderer>(true).Any(r => r.enabled), Is.True);
            Assert.That(Vector3.Distance(pivot, inspector.LookPoint), Is.LessThan(.001f));
            Assert.That(Flow.IsAssembled, Is.True);
        }

        [UnityTest]
        public IEnumerator CoverAndFaultModuleRemainGroundedAndCleanupWithTheirOwner()
        {
            Do("test.signal"); Do("test.identity"); Do("power.off"); Do("fixture.disconnect");
            for (int i = 1; i <= 4; i++) Do("remove.RX_CoverScrew_" + i.ToString("00"));
            Do("cover.remove"); Do("isolate"); Do("harness.release");
            Do("remove.RX_RFScrew_01"); Do("remove.RX_RFScrew_02"); Do("module.remove.old");
            yield return Until(() => !Presentation.IsAnimating);
            Assert.That(Presentation.AllSlotsFit, Is.True);
            foreach (var node in Flow.Definition.nodes.Where(n => n.movable && Flow.GetState(n.outKey) == n.outValue))
                AssertGrounded(Presentation.ParkedVisual(node.objectName));
            var cover = Presentation.ParkedVisual("RX_RearCover");
            var camera = clinic.Station.Inspector.ViewCamera;
            Physics.SyncTransforms();
            Capture("receiver-disassembled-table", camera);
            Assert.That(Physics.Raycast(new Ray(camera.transform.position, BoundsOf(cover).center - camera.transform.position),
                out var hit, 10, ~0, QueryTriggerInteraction.Ignore), Is.True);
            TestContext.WriteLine("Parked cover pick first hit: " + hit.transform.name + "; cover bounds: " + BoundsOf(cover));
            Assert.That(hit.transform.GetComponentInParent<RepairPropParkedPart>(), Is.SameAs(cover.GetComponent<RepairPropParkedPart>()),
                "The parked cover must be visible/pickable from the default inspection camera, not hidden behind the item.");
            Capture("receiver-disassembled-table", clinic.Station.Inspector.ViewCamera);
            Do("module.install.new");
            yield return Until(() => !Presentation.IsAnimating);
            AssertGrounded(Presentation.ParkedVisual("RX_RF_Daughterboard_Fault"));
            var root = Presentation.ParkingRoot;
            clinic.Station.Inspector.Clear();
            yield return null; yield return null;
            Assert.That(root == null, Is.True, "No parked meshes or colliders may leak to the next order.");
        }

        void Do(string id)
        {
            var command = Flow.Definition.FindCommand(id);
            Assert.That(bridge.SelectTarget(command.target), Is.True, id);
            Assert.That(bridge.ExecuteFromPanel(id), Is.True, id + ": " + Flow.LastFeedback);
        }

        void AssertGrounded(Transform visual)
        {
            Assert.That(visual, Is.Not.Null);
            Assert.That(visual.gameObject.activeInHierarchy, Is.True);
            Assert.That(BoundsOf(visual).min.y - clinic.BenchTopWorldY, Is.InRange(.0005f, .003f));
        }

        static Bounds BoundsOf(Transform t)
        {
            var renderers = t.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.GreaterThan(0));
            var bounds = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        static IEnumerator Until(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, "Timed out waiting for real scene state.");
        }

        static void Capture(string name, Camera camera)
        {
            Directory.CreateDirectory(Evidence);
            var target = new RenderTexture(1600, 900, 24);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(Evidence, name + ".png"), image.EncodeToPNG());
            }
            finally { camera.targetTexture = oldTarget; RenderTexture.active = oldActive; Object.Destroy(target); Object.Destroy(image); }
        }
    }
}
