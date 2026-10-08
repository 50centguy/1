using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BorderRepair.Core;
using BorderRepair.FirstOrder;
using BorderRepair.Inspection;
using BorderRepair.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.TwoNight.Tests
{
    public class UnifiedClinicInspectionTests
    {
        const string ScenePath = "Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic.unity";
        UnifiedClinicDirector clinic;
        ItemInspector inspector;
        ItemScanner scanner;
        Camera camera;
        Mouse mouse;
        Keyboard keyboard;
        Action mouseCleanup;
        string saveDirectory, previousSaveDirectory;
        float previousTimeScale, previousCaptureDelta;
        CursorLockMode previousCursor;
        bool previousCursorVisible;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousSaveDirectory = TwoNightSave.OverrideDirectory;
            saveDirectory = Path.Combine(Path.GetTempPath(), "ClinicInspection_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = saveDirectory;
            previousTimeScale = Time.timeScale;
            previousCaptureDelta = Time.captureDeltaTime;
            previousCursor = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Time.timeScale = 1;
            Time.captureDeltaTime = 1f / 60f;
            TwoNightRun.NewGame(null, true);
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(TwoNightScenes.Clinic, LoadSceneMode.Single);
#endif
            for (int i = 0; i < 8; i++) yield return null;
            clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            Assert.IsNotNull(clinic);
            Assert.IsTrue(clinic.Initialized);
            Assert.AreEqual(TwoNightPhase.Night1Counter, clinic.State.phase);
            inspector = clinic.Station.Inspector;
            scanner = clinic.Station.Scanner;
            camera = inspector.ViewCamera;
            if (clinic.Robot.Flow.Rig.Walker != null) clinic.Robot.Flow.Rig.Walker.LeaveSystemCursor = true;
            mouse = FirstOrderAcceptanceDriver.CreateVirtualMouse(out mouseCleanup);
            keyboard = InputSystem.AddDevice<Keyboard>("ClinicInspection_Keyboard");
            keyboard.MakeCurrent();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            keyboard = null;
            mouseCleanup?.Invoke();
            mouseCleanup = null;
            mouse = null;
            TwoNightSave.OverrideDirectory = previousSaveDirectory;
            TwoNightRun.Clear();
            Time.timeScale = previousTimeScale;
            Time.captureDeltaTime = previousCaptureDelta;
            Cursor.lockState = previousCursor;
            Cursor.visible = previousCursorVisible;
            if (Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
            yield return null;
        }

        static bool OnScreen(Vector3 screen) => screen.z > 0 && screen.x > 3 && screen.y > 3 &&
            screen.x < Screen.width - 3 && screen.y < Screen.height - 3;

        IEnumerator MoveMouse(Vector2 position)
        {
            mouse.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            yield return null;
            yield return null;
        }

        IEnumerator Click(Vector2 position)
        {
            yield return MoveMouse(position);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left, true));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            yield return null;
            yield return null;
        }

        IEnumerator Tap(Key key)
        {
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;
        }

        static IEnumerable<Vector3> BoundsSamples(Bounds bounds)
        {
            yield return bounds.center;
            for (int i = 0; i < 125; i++)
            {
                var fraction = new Vector3(i % 5, i / 5 % 5, i / 25) / 4f;
                yield return bounds.min + Vector3.Scale(bounds.size, Vector3.one * .1f + fraction * .8f);
            }
        }

        Vector2 ButtonPixel(Button button)
        {
            Assert.IsTrue(button != null && button.isActiveAndEnabled && button.interactable, "Requested UI button must be active and interactable.");
            Canvas.ForceUpdateCanvases();
            var canvas = button.GetComponentInParent<Canvas>().rootCanvas;
            var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var rect = (RectTransform)button.transform;
            foreach (var fraction in new[] { new Vector2(.5f, .5f), new Vector2(.3f, .5f), new Vector2(.7f, .5f),
                new Vector2(.5f, .3f), new Vector2(.5f, .7f) })
            {
                var local = rect.rect.min + Vector2.Scale(rect.rect.size, fraction);
                var screen = RectTransformUtility.WorldToScreenPoint(uiCamera, rect.TransformPoint(local));
                if (screen.x < 1 || screen.x >= Screen.width || screen.y < 1 || screen.y >= Screen.height) continue;
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, hits);
                if (hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button) return screen;
            }
            Assert.Fail("No unobstructed UI pixel for " + button.name);
            return default;
        }

        IEnumerator ClickButton(Button button) => Click(ButtonPixel(button));

        Button StationButton(string field)
        {
            var info = typeof(RepairUIView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, "Serialized station UI field: " + field);
            return (Button)info.GetValue(clinic.Station.View);
        }

        Button RoomButton(string name) => UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
            .Single(b => b.name == name && b.transform.GetComponentsInParent<Transform>(true)
                .Any(t => t.name == "UnifiedClinicTradeUI"));

        IEnumerator ReceiveAndOpenInspection()
        {
            // Keep the existing fixed-room receive path; the new tests exercise actual pointer input.
            clinic.Robot.Flow.Rig.Go(FirstOrderCameraRig.Overview, true);
            yield return null;
            var zone = UnityEngine.Object.FindObjectsByType<ClinicTradeZone>(FindObjectsSortMode.None)
                .Single(z => z.Action == ClinicTradeAction.Receive);
            var roomCamera = clinic.Robot.Flow.Rig.Cam;
            Physics.SyncTransforms();
            Vector2? pixel = null;
            foreach (var collider in zone.GetComponentsInChildren<Collider>().Where(c => c.enabled && c.gameObject.activeInHierarchy))
                foreach (var sample in BoundsSamples(collider.bounds))
                {
                    var screen = roomCamera.WorldToScreenPoint(sample);
                    if (!OnScreen(screen) || FirstOrderInput.IsOverUI(screen)) continue;
                    if (Physics.Raycast(roomCamera.ScreenPointToRay(screen), out var hit, 12, ~0, QueryTriggerInteraction.Collide) &&
                        hit.collider.GetComponentInParent<ClinicTradeZone>() == zone) { pixel = screen; break; }
                }
            Assert.IsTrue(pixel.HasValue, "Physical receive zone must have an actual visible, UI-free mouse target.");
            Assert.AreEqual(0, clinic.State.customerTrades.Count);
            yield return Click(pixel.Value);
            Assert.AreEqual(1, clinic.State.customerTrades.Count, "Input System receive click accepts exactly one item.");
            Assert.AreEqual(ClinicTradeState.InRepair, clinic.TradeState);
            Assert.IsTrue(clinic.ConsoleOpen);
            if (clinic.Counter.DialogueVisible)
            {
                yield return ClickButton(clinic.Counter.DialogueButton);
                Assert.IsFalse(clinic.Counter.DialogueVisible, "Real Continue button click dismisses the customer dialogue.");
            }
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(RepairStage.Inspect, clinic.Station.Session.Stage);
            Assert.IsTrue(camera.enabled && inspector.isActiveAndEnabled && inspector.InteractionEnabled);
            Assert.IsTrue(scanner.isActiveAndEnabled);
        }

        Renderer[] ItemRenderers() => inspector.CurrentItem.GetComponentsInChildren<Renderer>(true)
            .Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();

        bool WorldOccluded(Vector2 screen, float itemDistance)
        {
            var ray = camera.ScreenPointToRay(screen);
            var item = inspector.CurrentItem.transform;
            foreach (var hit in Physics.RaycastAll(ray, itemDistance, camera.cullingMask, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(item)) return true;
            // A world label can hide the item even when it has no physics collider.
            foreach (var text in UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
            {
                var renderer = text.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled || text.transform.IsChildOf(item) ||
                    (camera.cullingMask & (1 << text.gameObject.layer)) == 0) continue;
                if (renderer.bounds.IntersectRay(ray, out float distance) && distance >= 0 && distance < itemDistance - .01f) return true;
            }
            return false;
        }

        bool ItemPixel(Vector2 screen, out float distance)
        {
            distance = -1;
            if (FirstOrderInput.IsOverUI(screen)) return false;
            var item = inspector.CurrentItem.transform;
            var hit = Physics.RaycastAll(camera.ScreenPointToRay(screen), camera.farClipPlane, camera.cullingMask,
                QueryTriggerInteraction.Collide).Where(h => h.transform.IsChildOf(item)).OrderBy(h => h.distance).FirstOrDefault();
            if (hit.collider == null || WorldOccluded(screen, hit.distance)) return false;
            distance = hit.distance;
            return true;
        }

        Vector2 FindItemPixel()
        {
            Physics.SyncTransforms();
            foreach (var renderer in ItemRenderers())
                foreach (var sample in BoundsSamples(renderer.bounds))
                {
                    var screen = camera.WorldToScreenPoint(sample);
                    if (OnScreen(screen) && ItemPixel(screen, out _)) return screen;
                }
            Assert.Fail("No visible item pixel outside UI and room/label occlusion. Camera: " + camera.transform.position);
            return default;
        }

        static Color32[] ReadPixels(RenderTexture target)
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                return image.GetPixels32();
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(image); }
        }

        static int ChangedPixels(Color32[] first, Color32[] second)
        {
            int count = 0;
            for (int i = 0; i < first.Length; i++)
                if (Math.Abs(first[i].r - second[i].r) + Math.Abs(first[i].g - second[i].g) +
                    Math.Abs(first[i].b - second[i].b) > 24) count++;
            return count;
        }

        void AssertRenderedItemVisible(string label)
        {
            Assert.AreNotEqual(GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType, "Inspection visibility requires the graphics-enabled runner.");
            var itemRenderers = ItemRenderers();
            Assert.IsNotEmpty(itemRenderers);
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            Assert.IsTrue(itemRenderers.Any(r => (camera.cullingMask & (1 << r.gameObject.layer)) != 0 &&
                GeometryUtility.TestPlanesAABB(planes, r.bounds)), "Enabled item geometry must be in the camera culling mask and frustum.");
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var enabled = renderers.ToDictionary(r => r, r => r.enabled);
            var originalTarget = camera.targetTexture;
            var originalActive = RenderTexture.active;
            int width = 512, height = Mathf.Max(128, Mathf.RoundToInt(512f * Screen.height / Mathf.Max(1, Screen.width)));
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            int visible = 0, reference = 0;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                var scene = ReadPixels(target);
                foreach (var renderer in itemRenderers) renderer.enabled = false;
                camera.Render();
                var withoutItem = ReadPixels(target);
                visible = ChangedPixels(scene, withoutItem);
                foreach (var renderer in renderers) renderer.enabled = false;
                camera.Render();
                var background = ReadPixels(target);
                foreach (var renderer in itemRenderers) renderer.enabled = true;
                camera.Render();
                reference = ChangedPixels(ReadPixels(target), background);
            }
            finally
            {
                foreach (var saved in enabled) if (saved.Key != null) saved.Key.enabled = saved.Value;
                camera.targetTexture = originalTarget;
                RenderTexture.active = originalActive;
                target.Release();
                UnityEngine.Object.Destroy(target);
            }
            Debug.Log($"[ClinicInspection] {label}: item pixels {visible}, isolated pixels {reference}, camera {camera.transform.position}, look {inspector.LookPoint}");
            Assert.Greater(reference, 150, "Item-only render must contain inspectable, lit geometry rather than a blank image.");
            Assert.Greater(visible, 150, "The real scene must render the item, not just the bench or CONSOLE label.");
            Assert.GreaterOrEqual((float)visible / reference, .65f,
                "Foreground room geometry/labels hide too much of the item. Visible " + visible + ", item-only " + reference);
            Assert.IsTrue(ItemPixel(FindItemPixel(), out _), "Visible rendered item must also have a clear physics/pointer route.");
            Assert.Greater(Vector3.Distance(inspector.LookPoint, clinic.WorkAnchor.position), 10,
                "Inspection item orbit must be isolated from the physical diagnostic bench.");
            Assert.IsFalse(inspector.CurrentItem.transform.IsChildOf(clinic.WorkAnchor), "Orbiting the item must not rotate the physical console label.");
        }

        IEnumerator Settle(int frames = 40)
        {
            // Inspector smoothing uses unscaled time; captureDeltaTime alone cannot advance it.
            double deadline = Time.realtimeSinceStartupAsDouble + .6;
            for (int i = 0; i < frames || Time.realtimeSinceStartupAsDouble < deadline; i++) yield return null;
        }

        IEnumerator Drag(float dx, float dy = 0)
        {
            var start = FindItemPixel();
            yield return MoveMouse(start);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = start }.WithButton(MouseButton.Right, true));
            yield return null;
            var position = start;
            for (int i = 0; i < 12; i++)
            {
                var next = position + new Vector2(dx, dy) / 12f;
                next.x = Mathf.Clamp(next.x, 3, Screen.width - 3);
                next.y = Mathf.Clamp(next.y, 3, Screen.height - 3);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = next, delta = next - position }.WithButton(MouseButton.Right, true));
                position = next;
                yield return null;
            }
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            yield return null;
            yield return Settle();
        }

        [UnityTest]
        public IEnumerator ReceivedItem_IsActuallyVisible_DragScrollSpaceAndResetUseRealInput()
        {
            yield return ReceiveAndOpenInspection();
            AssertRenderedItemVisible("received");
            var anchor = inspector.CurrentItem.transform.parent;
            var rotation = anchor.rotation;
            var workRotation = clinic.WorkAnchor.rotation;
            float distance = inspector.TargetDistance;
            yield return Drag(100, 30);
            Assert.Greater(Quaternion.Angle(rotation, anchor.rotation), 10, "Real held mouse drag rotates the item anchor.");
            Assert.Less(Quaternion.Angle(workRotation, clinic.WorkAnchor.rotation), .01f, "Dragging must leave the bench/CONSOLE label stationary.");
            AssertRenderedItemVisible("dragged");
            var pointer = FindItemPixel();
            yield return MoveMouse(pointer);
            var cameraBefore = camera.transform.position;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer, scroll = new Vector2(0, 120) });
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
            yield return Settle();
            Assert.Less(inspector.TargetDistance, distance - .01f, "Real scroll input zooms toward the item.");
            Assert.Greater(Vector3.Distance(cameraBefore, camera.transform.position), .01f, "Scroll must move the actual inspection camera.");
            AssertRenderedItemVisible("zoomed");
            Assert.IsFalse(scanner.ScanModeActive);
            yield return Tap(Key.Space);
            Assert.IsTrue(scanner.ScanModeActive && clinic.Station.Session.ScanModeActive, "Space toggles the actual scanner.");
            yield return Tap(Key.Space);
            Assert.IsFalse(scanner.ScanModeActive || clinic.Station.Session.ScanModeActive);
            yield return Tap(Key.R);
            yield return Settle();
            Assert.Less(Quaternion.Angle(rotation, anchor.rotation), 1, "Actual R input resets the item rotation.");
            Assert.AreEqual(distance, inspector.TargetDistance, .003f, "Actual R input resets zoom.");
            AssertRenderedItemVisible("reset");
            Assert.AreEqual(1, clinic.State.customerTrades.Count);
        }

        bool FindPointPixel(InspectionPoint point, out Vector2 screen)
        {
            Physics.SyncTransforms();
            foreach (var collider in point.GetComponentsInChildren<Collider>().Where(c => c.enabled && c.gameObject.activeInHierarchy))
                foreach (var sample in BoundsSamples(collider.bounds))
                {
                    var projected = camera.WorldToScreenPoint(sample);
                    if (!OnScreen(projected) || !ItemPixel(projected, out _) || scanner.PickPoint(projected, out _) != point) continue;
                    screen = projected;
                    return true;
                }
            screen = default;
            return false;
        }

        IEnumerator ScanRequiredWithMouse()
        {
            if (!scanner.ScanModeActive) yield return Tap(Key.Space);
            Assert.IsTrue(scanner.ScanModeActive && clinic.Station.Session.ScanModeActive);
            var required = clinic.Station.Session.CurrentCase.inspectionPoints.Where(p => p.requiredForDiagnosis).ToArray();
            Assert.IsNotEmpty(required, "Authored first case must exercise at least one required point.");
            foreach (var info in required)
            {
                Assert.IsTrue(clinic.Station.TryGetPoint(info.pointId, out var point), "Mounted authored point: " + info.pointId);
                Vector2 screen = default;
                bool found = FindPointPixel(point, out screen);
                for (int attempt = 0; attempt < 12 && !found; attempt++)
                {
                    yield return Drag(attempt % 4 == 3 ? -120 : 100, attempt % 4 == 3 ? 80 : 0);
                    found = FindPointPixel(point, out screen);
                }
                Assert.IsTrue(found, "Required point needs a rendered, physically clear mouse target after real drag rotation: " + info.pointId);
                yield return MoveMouse(screen);
                Assert.AreSame(point, scanner.Hovered, "Actual pointer movement hovers the intended inspection point.");
                int before = clinic.Station.Session.FoundRequiredCount;
                yield return Click(screen);
                Assert.IsTrue(clinic.Station.Session.HasScanned(info.pointId), "Actual mouse press/release must scan " + info.pointId);
                Assert.AreEqual(before + 1, clinic.Station.Session.FoundRequiredCount);
                Debug.Log("[ClinicInspection] Real mouse scan: " + info.pointId + " at " + screen);
            }
            Assert.AreEqual(clinic.Station.Session.RequiredFindingCount, clinic.Station.Session.FoundRequiredCount);
        }

        [UnityTest]
        public IEnumerator RequiredPoints_ActualMouseScans_ThenActualDiagnosisButtons()
        {
            yield return ReceiveAndOpenInspection();
            AssertRenderedItemVisible("before scanning");
            yield return ScanRequiredWithMouse();
            yield return ClickButton(StationButton("diagnoseButton"));
            Assert.AreEqual(RepairStage.Diagnose, clinic.Station.Session.Stage, "Real Diagnose UI click enters diagnosis.");
            Assert.IsFalse(scanner.ScanModeActive);
            yield return ClickButton(StationButton("backToInspectButton"));
            Assert.AreEqual(RepairStage.Inspect, clinic.Station.Session.Stage, "Real Back to inspection UI click returns to the item.");
            Assert.AreEqual(clinic.Station.Session.RequiredFindingCount, clinic.Station.Session.FoundRequiredCount);
            AssertRenderedItemVisible("back from diagnosis");
            yield return ClickButton(StationButton("diagnoseButton"));
            var option = clinic.Station.View.GetComponentsInChildren<Button>(true)
                .Single(b => b.name == "Option_" + clinic.Station.Session.CurrentCase.correctDiagnosisId);
            yield return ClickButton(option);
            Assert.AreEqual(RepairStage.Decide, clinic.Station.Session.Stage, "Real authored diagnosis-option click reaches the decision stage.");
            Assert.AreEqual(0, clinic.State.transactions.Count, "Inspection and diagnosis must not post delivery income.");
        }

        [UnityTest]
        public IEnumerator ReturnRoom_AndReopenInspection_PreserveVisibleItemAndRealScanFindings()
        {
            yield return ReceiveAndOpenInspection();
            AssertRenderedItemVisible("before room return");
            yield return ScanRequiredWithMouse();
            var item = inspector.CurrentItem;
            var inspectionParent = item.transform.parent;
            var caseId = clinic.Station.Session.CurrentCase.caseId;
            int findings = clinic.Station.Session.FoundRequiredCount;
            yield return ClickButton(RoomButton("Room"));
            Assert.IsFalse(clinic.ConsoleOpen);
            Assert.IsTrue(clinic.RoomWalking);
            Assert.IsFalse(camera.enabled || inspector.enabled || scanner.enabled);
            Assert.IsTrue(clinic.Robot.Flow.Rig.Cam.enabled);
            Assert.AreSame(item, inspector.CurrentItem);
            Assert.AreSame(clinic.WorkAnchor, item.transform.parent, "Returning to the room mounts the same item on the physical workbench.");
            var benchRenderers = ItemRenderers();
            Assert.IsNotEmpty(benchRenderers, "Returned item must keep its visible geometry on the bench.");
            Assert.AreEqual(clinic.TradeSupportWorldY(clinic.WorkAnchor), benchRenderers.Min(r => r.bounds.min.y), .002f,
                "Returned item must sit on the workbench support surface without sinking or floating.");
            if (clinic.Robot.Flow.Rig.Walker.Aiming) yield return Tap(Key.Tab);
            yield return ClickButton(RoomButton("Console"));
            yield return Settle(5);
            Assert.IsTrue(clinic.ConsoleOpen && camera.enabled && inspector.enabled && scanner.enabled);
            Assert.AreSame(item, inspector.CurrentItem, "Reopening must not mount a replacement item.");
            Assert.AreSame(inspectionParent, item.transform.parent, "Reopening restores the original isolated inspection-stage parent.");
            Assert.AreEqual(caseId, clinic.Station.Session.CurrentCase.caseId);
            Assert.AreEqual(findings, clinic.Station.Session.FoundRequiredCount);
            Assert.AreEqual(1, clinic.State.customerTrades.Count, "Returning/reopening must not receive the same item again.");
            AssertRenderedItemVisible("reopened");
            yield return Drag(-80, 20);
            AssertRenderedItemVisible("reopened and dragged");
            yield return Tap(Key.R);
            yield return Settle();
            AssertRenderedItemVisible("reopened and reset");
        }
    }
}
