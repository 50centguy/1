using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Core;
using BorderRepair.FirstOrder;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.Cosmetics.Tests
{
    // Uses the authored desktop clinic and public controllers. It is not a human-input acceptance run.
    public sealed class CosmeticClinicSceneTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        TwoNightState previousRun;
        string previousSave;
        float previousTimeScale;
        CursorLockMode previousCursor;
        bool previousCursorVisible;
        Mouse previousMouse;
        string isolatedSave;
        Scene loaded;
        UnifiedClinicDirector clinic;
        CosmeticController controller;
        RepairPropStationBridge bridge;
        Action cleanupMouse;
        Mouse mouse;
        readonly List<Object> owned = new List<Object>();
        RepairStationController Station => clinic.Station;
        RepairPropFlow Flow => bridge.Flow;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current;
            previousSave = TwoNightSave.OverrideDirectory;
            previousTimeScale = Time.timeScale;
            previousCursor = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            previousMouse = Mouse.current;
            isolatedSave = Path.Combine(Path.GetTempPath(), "CosmeticClinic_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = isolatedSave;
            TwoNightRun.Clear();
            Time.timeScale = 1;
            CosmeticPixels.RequireShaders();
#if UNITY_EDITOR
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null,
                "Generate the real repair-props scene before running these tests.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Requires the authored editor scene; does not alter build settings.");
            yield break;
#endif
            loaded = SceneManager.GetSceneByPath(ScenePath);
            yield return Until(() =>
            {
                clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>();
                controller = Object.FindFirstObjectByType<CosmeticController>();
                return clinic != null && clinic.Initialized && controller != null && controller.Clinic == clinic && controller.Item != null;
            }, "Clinic and cosmetics must bind through their real scene lifecycle.");
            Assert.That(controller.gameObject.scene, Is.EqualTo(loaded));
            Assert.That(Station.Session.CaseCount, Is.EqualTo(4));
            Assert.That(clinic.Robot.Flow.Rig.Walker, Is.Not.Null);
            clinic.Robot.Flow.Rig.Walker.LeaveSystemCursor = true;
            // Mechanical/UI coverage starts with a genuinely purchased replacement, not free prototype stock.
            Assert.That(ClinicSupplies.Purchase(clinic.State, new System.Collections.Generic.Dictionary<string, int> { { "rx-rf", 1 } },
                System.Guid.NewGuid().ToString("N"), out string stockReason), Is.True, stockReason);
            mouse = FirstOrderAcceptanceDriver.CreateVirtualMouse(out cleanupMouse);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(-100, -100) });
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (controller != null) controller.SetMode(CosmeticMode.Off);
            // Unload first: the scene must not observe the restored caller's TwoNightRun.
            if (loaded.IsValid() && loaded.isLoaded)
            {
                var cleanup = SceneManager.CreateScene("CosmeticClinicCleanup_" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
            cleanupMouse?.Invoke();
            cleanupMouse = null;
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
            TwoNightRun.Set(previousRun);
            TwoNightSave.OverrideDirectory = previousSave;
            Time.timeScale = previousTimeScale;
            Cursor.lockState = previousCursor;
            Cursor.visible = previousCursorVisible;
            if (!string.IsNullOrEmpty(isolatedSave) && Directory.Exists(isolatedSave))
            {
                string resolved = Path.GetFullPath(isolatedSave);
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                Assert.That(resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase), Is.True);
                Assert.That(Path.GetFileName(resolved).StartsWith("CosmeticClinic_", StringComparison.Ordinal), Is.True);
                Directory.Delete(resolved, true);
            }
            clinic = null; controller = null; bridge = null; mouse = null;
        }

        [UnityTest]
        public IEnumerator IntakeBenchAndSafetyGates_ModeClaimsBlockBridge_AndOffRestoresExactFlags()
        {
            AssertUnavailable();
            yield return ReceiveAndBench(false);
            var session = Station.Session;
            var record = session.CurrentRecord;
            Assert.That(Flow.GetState("power"), Is.EqualTo(1));
            Assert.That(Flow.GetState("fixture"), Is.EqualTo(1));
            AssertUnavailable();
            foreach (var test in Flow.Definition.tests) Do("test." + test.id);
            Do("power.off");
            AssertUnavailable();
            Do("fixture.disconnect");
            yield return Until(() => controller.Available, "Power-off plus disconnected fixture must enable assembled shells.");
            Station.CleaningActive = true;
            Assert.That(controller.SetMode(CosmeticMode.Spray), Is.False, "Existing cleaning ownership must exclude cosmetics.");
            Station.CleaningActive = false;
            Station.SelectTool(null);
            session.SetScanMode(true);
            bool reserved = Station.Inspector.LeftPointerReserved;
            bool suppressed = Station.Scanner.InteractionSuppressed;
            bool tinted = Station.Scanner.ShowScannedTint;
            bool scanning = session.ScanModeActive;
            var history = Flow.History.ToArray();
            int invalid = record.InvalidActionCount;
            var poses = Flow.Definition.nodes.Where(n => n.immutable).Select(n => new RetainedPose(Flow.GetObject(n.objectName))).ToArray();

            foreach (var mode in new[] { CosmeticMode.Spray, CosmeticMode.Decal, CosmeticMode.Replace })
            {
                Assert.That(controller.SetMode(mode), Is.True);
                Assert.That(Station.CleaningActive, Is.True);
                Assert.That(Station.Inspector.LeftPointerReserved, Is.True);
                Assert.That(Station.Scanner.InteractionSuppressed, Is.True);
                Assert.That(Station.Scanner.ShowScannedTint, Is.False);
                Assert.That(session.ScanModeActive, Is.False);
                Assert.That(bridge.CanInteract, Is.False);
                Assert.That(bridge.SelectTarget(Flow.Definition.rootObjectName), Is.False);
                Assert.That(bridge.ExecuteSelected("fixture.connect"), Is.False);
                Assert.That(Station.Session, Is.SameAs(session));
                Assert.That(Flow.Session, Is.SameAs(session));
                Assert.That(session.CurrentRecord, Is.SameAs(record));
                Assert.That(session.Stage, Is.EqualTo(RepairStage.Inspect));
            }
            Assert.That(controller.SetMode(CosmeticMode.Off), Is.True);
            Assert.That(bridge.CanInteract, Is.True);
            Assert.That(Station.CleaningActive, Is.False);
            Assert.That(Station.Inspector.LeftPointerReserved, Is.EqualTo(reserved));
            Assert.That(Station.Scanner.InteractionSuppressed, Is.EqualTo(suppressed));
            Assert.That(Station.Scanner.ShowScannedTint, Is.EqualTo(tinted));
            Assert.That(session.ScanModeActive, Is.EqualTo(scanning));
            CollectionAssert.AreEqual(history, Flow.History);
            Assert.That(record.InvalidActionCount, Is.EqualTo(invalid));
            foreach (var pose in poses) pose.AssertUnchanged();

            Assert.That(controller.SetMode(CosmeticMode.Spray), Is.True);
            clinic.ShowRoom();
            yield return null;
            Assert.That(controller.Mode, Is.EqualTo(CosmeticMode.Off));
            AssertUnavailable();
            Assert.That(bridge.CanInteract, Is.False);
            Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True);
            yield return Until(() => controller.Available && bridge.CanInteract, "Returning must restore the same session and bridge.");
            Assert.That(Flow.Session, Is.SameAs(session));
            Assert.That(session.CurrentRecord, Is.SameAs(record));
            CollectionAssert.AreEqual(history, Flow.History);

            Do("fixture.connect");
            AssertUnavailable();
            Do("fixture.disconnect");
            // A removed cover is an explicit assembled-state blocker, even with safe power and fixture states.
            for (int i = 1; i <= 4; i++) Do("remove.RX_CoverScrew_" + i.ToString("00"));
            Do("cover.remove");
            Assert.That(Flow.IsAssembled, Is.False);
            AssertUnavailable();
        }

        [UnityTest]
        public IEnumerator PublicMouseRay_RejectsRoomOcclusionAndUiBlockedPress_RequiresReleaseToResume()
        {
            yield return ReceiveAndBench(true);
            Ray ray = FindEligibleRay(false, out var initialHit);
            var surface = controller.Item.SurfaceFor(initialHit);
            Assert.That(surface, Is.Not.Null);
            Assert.That(controller.SetMode(CosmeticMode.Spray), Is.True);
            Assert.That(controller.ProcessRay(ray, true, false, 10), Is.False, "Mode changes must wait for release.");
            controller.ProcessRay(ray, false, false, 10);
            Assert.That(controller.ProcessRay(ray, true, false, 10), Is.True);
            controller.ProcessRay(ray, false, false, 10);
            Assert.That(controller.Item.ActionCount, Is.EqualTo(1));
            var painted = CosmeticPixels.Read(surface.Layer);
            Assert.That(painted.Any(p => p.a > 0), Is.True, "The real imported shell must receive GPU paint.");
            CosmeticPixels.Capture(surface.Layer, "desktop-ray-brush");
            var history = Flow.History.ToArray();
            var session = Station.Session;
            var record = session.CurrentRecord;

            var wall = Own(new GameObject("CosmeticTest_RoomOccluder"));
            wall.transform.position = ray.GetPoint(initialHit.distance * .5f);
            wall.transform.rotation = Quaternion.LookRotation(ray.direction);
            wall.AddComponent<BoxCollider>().size = new Vector3(.04f, .04f, .005f);
            Physics.SyncTransforms();
            Assert.That(Physics.Raycast(ray, out var nearest, 10, Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(nearest.collider.gameObject, Is.SameAs(wall));
            Assert.That(controller.TryHit(ray, 10, null, out _), Is.False, "Opaque room geometry must stop hit traversal.");
            controller.ProcessRay(ray, false, false, 10);
            Assert.That(controller.ProcessRay(ray, true, false, 10), Is.False);
            controller.ProcessRay(ray, false, false, 10);
            Assert.That(controller.Item.ActionCount, Is.EqualTo(1));
            CollectionAssert.AreEqual(painted, CosmeticPixels.Read(surface.Layer));
            wall.SetActive(false);
            Physics.SyncTransforms();
            Assert.That(controller.TryHit(ray, 10, null, out _), Is.True);

            yield return new WaitForSecondsRealtime(.04f);
            Assert.That(controller.ProcessRay(ray, true, true, 10), Is.False, "A UI-consumed press must not edit.");
            Assert.That(controller.ProcessRay(ray, true, false, 10), Is.False, "Dragging out of UI while held must remain latched.");
            Assert.That(controller.ProcessRay(ray, true, false, 10), Is.False);
            Assert.That(controller.Item.ActionCount, Is.EqualTo(1));
            CollectionAssert.AreEqual(painted, CosmeticPixels.Read(surface.Layer));
            controller.ProcessRay(ray, false, false, 10);
            Assert.That(controller.ProcessRay(ray, true, false, 10), Is.True, "Release and a fresh press must resume painting.");
            controller.ProcessRay(ray, false, false, 10);
            Assert.That(controller.Item.ActionCount, Is.EqualTo(2));
            Assert.That(controller.TryHit(ray, float.NaN, null, out _), Is.False);
            Assert.That(controller.TryHit(ray, 0, null, out _), Is.False);
            Assert.That(controller.TryHit(new Ray(ray.origin, -ray.direction), .1f, null, out _), Is.False);
            CollectionAssert.AreEqual(history, Flow.History);
            Assert.That(Station.Session, Is.SameAs(session));
            Assert.That(session.CurrentRecord, Is.SameAs(record));
            Assert.That(session.Stage, Is.EqualTo(RepairStage.Inspect));
            controller.SetMode(CosmeticMode.Off);
            Assert.That(bridge.CanInteract, Is.True);
        }

        [UnityTest]
        public IEnumerator InputSystemMouse_AimedAtActualEligibleShell_PaintsThroughDesktopUpdate()
        {
            yield return ReceiveAndBench(true);
            Assert.That(controller.SetMode(CosmeticMode.Spray), Is.True);
            Ray ray = FindEligibleRay(true, out var hit);
            var surface = controller.Item.SurfaceFor(hit);
            var camera = Station.Inspector.ViewCamera;
            Vector2 point = camera.WorldToScreenPoint(hit.point);
            Assert.That(controller.TryHit(camera.ScreenPointToRay(point), 10, null, out var aimed), Is.True);
            Assert.That(controller.Item.SurfaceFor(aimed), Is.SameAs(surface));
            var history = Flow.History.ToArray();
            int invalid = Station.Session.CurrentRecord.InvalidActionCount;
            var anchorRotation = Station.Inspector.ItemAnchor.rotation;
            var ui = Own(new GameObject("CosmeticTest_UiBlocker", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)));
            var canvas = ui.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var image = new GameObject("RaycastTarget", typeof(RectTransform), typeof(Image));
            image.transform.SetParent(ui.transform, false);
            var rect = image.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            image.GetComponent<Image>().color = new Color(1, 1, 1, .05f);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(OverUi(point), Is.True, "The test overlay must actually intercept UI raycasts: screen="+point+", canvas="+((RectTransform)ui.transform).rect+", depth="+image.GetComponent<Image>().depth+", events="+EventSystem.current);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            yield return null;
            // Public ProcessRay release also documents the mode-switch contract before real mouse input.
            controller.ProcessRay(ray, false, false, 10);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 });
            yield return null;
            Assert.That(surface.Edited, Is.False, "The actual UI raycast must consume the mouse press.");
            Assert.That(controller.Item.ActionCount, Is.Zero);
            ui.SetActive(false);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 });
            yield return null;
            Assert.That(surface.Edited, Is.False, "Leaving UI while held must not start an edit.");
            Assert.That(controller.Item.ActionCount, Is.Zero);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 });
            yield return Until(() => surface.Edited, "Desktop Update must consume the held Input System mouse and paint the aimed shell.");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            yield return null;
            Assert.That(controller.Item.ActionCount, Is.EqualTo(1));
            Assert.That(CosmeticPixels.Read(surface.Layer).Any(p => p.a > 0), Is.True);
            Assert.That(Quaternion.Angle(anchorRotation, Station.Inspector.ItemAnchor.rotation), Is.LessThan(.1f));
            CollectionAssert.AreEqual(history, Flow.History);
            Assert.That(Station.Session.CurrentRecord.InvalidActionCount, Is.EqualTo(invalid));
            CosmeticPixels.Capture(surface.Layer, "desktop-input-system-brush");
            CaptureDesktopPreview();
            controller.Undo();
            Assert.That(surface.Renderer.sharedMaterials[surface.Slot], Is.SameAs(surface.Original));
            Assert.That(surface.Edited, Is.False);
            controller.Redo();
            Assert.That(surface.Edited, Is.True);
            controller.Clear();
            Assert.That(surface.Renderer.sharedMaterials[surface.Slot], Is.SameAs(surface.Original));
            controller.Undo();
            Assert.That(surface.Edited, Is.True);
        }

        void CaptureDesktopPreview()
        {
            if(Environment.GetEnvironmentVariable("BORDER_REPAIR_COSMETICS_CAPTURE")!="1") return;
            var panel=Object.FindFirstObjectByType<CosmeticPanel>();
            var expand=panel.GetComponentsInChildren<Button>(true).FirstOrDefault(b=>b.GetComponentInChildren<Text>(true)?.text=="+");
            if(expand!=null) expand.onClick.Invoke();
            panel.Refresh();
            var canvas=panel.GetComponentInChildren<Canvas>(true);
            var camera=Station.Inspector.ViewCamera;
            var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;float plane=canvas.planeDistance;
            var active=RenderTexture.active;var oldTarget=camera.targetTexture;
            var rt=RenderTexture.GetTemporary(1280,720,24);
            var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=rt;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=.2f;
                Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
                texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();
                const string folder="C:/Users/Administrator/Documents/Codex/2026-09-29/border-repair-station-art/work/repair-props-cosmetics-2026-10-18/Evidence";
                Directory.CreateDirectory(folder);File.WriteAllBytes(folder+"/desktop-panel-preview.png",texture.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;canvas.planeDistance=plane;
                camera.targetTexture=oldTarget;RenderTexture.active=active;
                RenderTexture.ReleaseTemporary(rt);Object.Destroy(texture);
            }
        }

        [UnityTest]
        public IEnumerator FourRealOrders_ReplacementImagesAndUndoHistoryNeverCrossOrderBoundaries()
        {
            var session = Station.Session;
            var images = new List<Texture2D>();
            var originalMaps = new Dictionary<Material, Texture>();
            var normalMaps = new Dictionary<Material, Texture>();
            var kinds = new[] { RepairPropKind.Receiver, RepairPropKind.Cartridge, RepairPropKind.Power, RepairPropKind.Omamori };
            for (int index = 0; index < kinds.Length; index++)
            {
                yield return ReceiveAndBench(true);
                Assert.That(Flow.Definition.kind, Is.EqualTo(kinds[index]));
                string sku = RepairPropSupplyPolicy.SkuFor(kinds[index]);
                if (ClinicSupplies.Available(clinic.State, sku) == 0)
                    Assert.That(ClinicSupplies.Purchase(clinic.State, new Dictionary<string, int> { { sku, 1 } },
                        Guid.NewGuid().ToString("N"), out string stockReason), Is.True, stockReason);
                Assert.That(controller.Item.ActionCount, Is.Zero);
                Assert.That(controller.Item.CanUndo, Is.False);
                Assert.That(controller.Item.CanRedo, Is.False);
                Assert.That(controller.Item.Surfaces, Is.Not.Empty);
                foreach (var surface in controller.Item.Surfaces)
                {
                    Assert.That(surface.Edited, Is.False);
                    Assert.That(surface.Layer, Is.Null, "A later order must not inherit a prior overlay.");
                    Assert.That(surface.Renderer.sharedMaterials[surface.Slot], Is.SameAs(surface.Original));
                    Assert.That(images.Contains(surface.Original.GetTexture("_BaseMap") as Texture2D), Is.False);
                    originalMaps[surface.Original] = surface.Original.GetTexture("_BaseMap");
                    normalMaps[surface.Original] = surface.Original.GetTexture("_BumpMap");
                }
                var allSlots = SlotSnapshot(controller.Item.gameObject);
                var image = Own(new Texture2D(16, 16, TextureFormat.RGBA32, false));
                image.name = "Order_" + index + "_Only";
                image.SetPixels(Enumerable.Repeat(new Color(.15f * index, .8f - .12f * index, .25f + .15f * index, 1), 256).ToArray());
                image.Apply(false, false);
                images.Add(image);
                CosmeticCatalog.Selected = image;
                Assert.That(controller.SetMode(CosmeticMode.Replace), Is.True);
                controller.ReplaceWholeShell();
                Assert.That(controller.Item.ActionCount, Is.EqualTo(1));
                foreach (var surface in controller.Item.Surfaces)
                {
                    Assert.That(surface.Edited, Is.True);
                    Assert.That(surface.RuntimeMaterial.GetTexture("_BaseMap"), Is.SameAs(image));
                    Assert.That(surface.RuntimeMaterial.GetTexture("_BumpMap"), Is.SameAs(normalMaps[surface.Original]));
                }
                AssertOtherSlotsUnchanged(allSlots);
                controller.Clear();
                foreach (var surface in controller.Item.Surfaces)
                    Assert.That(surface.Renderer.sharedMaterials[surface.Slot], Is.SameAs(surface.Original));
                controller.Undo();
                foreach (var surface in controller.Item.Surfaces)
                    Assert.That(surface.RuntimeMaterial.GetTexture("_BaseMap"), Is.SameAs(image));
                controller.SetMode(CosmeticMode.Off);
                Assert.That(bridge.CanInteract, Is.True);
                Assert.That(Flow.Session, Is.SameAs(session));
                yield return RepairAndDeliver();
                foreach (var pair in originalMaps)
                {
                    Assert.That(pair.Key.GetTexture("_BaseMap"), Is.SameAs(pair.Value));
                    Assert.That(pair.Key.GetTexture("_BumpMap"), Is.SameAs(normalMaps[pair.Key]));
                }
            }
            Assert.That(session.Records.Count, Is.EqualTo(4));
        }

        IEnumerator ReceiveAndBench(bool makeSafe)
        {
            Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True, clinic.LastMessage);
            var item = Station.Inspector.CurrentItem;
            bridge = item.GetComponent<RepairPropStationBridge>();
            Assert.That(bridge, Is.Not.Null);
            yield return Until(() => bridge.Flow.Session != null && controller.Flow == bridge.Flow, "The mounted instance must bind through Start/Update.");
            Assert.That(clinic.Carrying, Is.True);
            Assert.That(Station.Session.Stage, Is.EqualTo(RepairStage.Intake));
            AssertUnavailable();
            clinic.Counter.DismissDialogue();
            Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True, clinic.LastMessage);
            yield return Until(() => bridge.CanInteract && CameraSettled(), "Real bench, picking colliders, and inspection camera must settle.");
            Assert.That(Station.Inspector.CurrentItem, Is.SameAs(item));
            Assert.That(Flow.Session, Is.SameAs(Station.Session));
            Assert.That(Station.Session.Stage, Is.EqualTo(RepairStage.Inspect));
            if (makeSafe)
            {
                Do("power.off");
                Do("fixture.disconnect");
                yield return Until(() => controller.Available, "An assembled, unpowered, disconnected item must allow cosmetics.");
            }
        }

        void AssertUnavailable()
        {
            Assert.That(controller.Available, Is.False);
            foreach (var mode in new[] { CosmeticMode.Spray, CosmeticMode.Decal, CosmeticMode.Replace })
                Assert.That(controller.SetMode(mode), Is.False, mode.ToString());
        }

        bool CameraSettled()
        {
            var inspector = Station.Inspector;
            var camera = inspector.ViewCamera;
            return camera.isActiveAndEnabled && Mathf.Abs(Vector3.Distance(camera.transform.position, inspector.LookPoint) - inspector.TargetDistance) < .001f
                && Vector3.Angle(camera.transform.forward, inspector.LookPoint - camera.transform.position) < .1f;
        }

        Ray FindEligibleRay(bool fromCamera, out RaycastHit hit)
        {
            Physics.SyncTransforms();
            var camera = Station.Inspector.ViewCamera;
            int attempted = 0;
            foreach (var surface in controller.Item.Surfaces)
            {
                var collider = surface.Renderer.GetComponent<MeshCollider>();
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                var bounds = collider.bounds;
                for (int i = 0; i < 125; i++)
                {
                    var fraction = new Vector3(i % 5, i / 5 % 5, i / 25) * .2f + Vector3.one * .1f;
                    var point = bounds.min + Vector3.Scale(bounds.size, fraction);
                    var directions = fromCamera ? new[] { camera.transform.position - point } : new[] {
                        camera.transform.position - point, collider.transform.forward, -collider.transform.forward,
                        collider.transform.right, -collider.transform.right, collider.transform.up, -collider.transform.up };
                    foreach (var direction in directions)
                    {
                        var origin = fromCamera ? camera.transform.position : point + direction.normalized * (bounds.size.magnitude + .08f);
                        var ray = new Ray(origin, (point - origin).normalized);
                        attempted++;
                        if (!controller.TryHit(ray, 10, null, out var candidate)) continue;
                        if (controller.Item.SurfaceFor(candidate) != surface) continue;
                        if (fromCamera)
                        {
                            Vector3 screen = camera.WorldToScreenPoint(candidate.point);
                            if (screen.z <= camera.nearClipPlane || !camera.pixelRect.Contains(screen) || OverUi(screen)) continue;
                            var panel = controller.GetComponent<CosmeticPanel>();
                            if (panel != null && panel.BlocksPointer(screen)) continue;
                            ray = camera.ScreenPointToRay(screen);
                            if (!controller.TryHit(ray, 10, null, out candidate) || controller.Item.SurfaceFor(candidate) != surface) continue;
                        }
                        hit = candidate;
                        return ray;
                    }
                }
            }
            Assert.Fail("No actual eligible shell point was reachable; kind=" + Flow.Definition.kind + ", rays=" + attempted + ", fromCamera=" + fromCamera);
            hit = default;
            return default;
        }

        static bool OverUi(Vector2 point)
        {
            if (EventSystem.current == null) return false;
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, results);
            return results.Count > 0;
        }

        void Do(string id)
        {
            var command = Flow.Definition.FindCommand(id);
            Assert.That(command, Is.Not.Null, id);
            Station.SelectTool(command.tool);
            Assert.That(bridge.SelectTarget(command.target), Is.True, id);
            Assert.That(bridge.ExecuteSelected(id), Is.True, id + ": " + Flow.LastFeedback);
        }

        IEnumerator RepairAndDeliver()
        {
            var item = Station.Inspector.CurrentItem;
            Do("fixture.connect");
            Do("power.on");
            foreach (var test in Flow.Definition.tests) Do("test." + test.id);
            Do("power.off");
            Do("fixture.disconnect");
            var kind = Flow.Definition.kind;
            string cover = kind == RepairPropKind.Receiver ? "RX_CoverScrew_" : kind == RepairPropKind.Cartridge ? "MC_CoverScrew_" : kind == RepairPropKind.Power ? "PS_CoverScrew_" : "OM_CoverScrew_";
            string module = kind == RepairPropKind.Receiver ? "RX_RFScrew_" : kind == RepairPropKind.Cartridge ? "MC_IFScrew_" : kind == RepairPropKind.Power ? "PS_ModuleScrew_" : "OM_WakeScrew_";
            for (int i = 1; i <= 4; i++) Do("remove." + cover + i.ToString("00"));
            Do("cover.remove");
            if (kind != RepairPropKind.Cartridge) Do("isolate");
            Do("harness.release");
            if (kind == RepairPropKind.Power)
            { Do("remove.PS_TrayScrew_01"); Do("remove.PS_TrayScrew_02"); Do("tray.remove"); }
            for (int i = 1; i <= 2; i++) Do("remove." + module + i.ToString("00"));
            Do("module.remove.old");
            Do("module.install.new");
            for (int i = 1; i <= 2; i++) Do("install." + module + i.ToString("00"));
            if (kind == RepairPropKind.Power)
            { Do("tray.install"); Do("install.PS_TrayScrew_01"); Do("install.PS_TrayScrew_02"); }
            Do("harness.connect");
            if (kind != RepairPropKind.Cartridge) Do("supply.connect");
            Do("cover.install");
            for (int i = 1; i <= 4; i++) Do("install." + cover + i.ToString("00"));
            Do("fixture.connect");
            Do("power.on");
            foreach (var test in Flow.Definition.tests)
                for (int pass = 0; pass < test.consecutivePasses; pass++) Do("test." + test.id);
            Do("complete");
            yield return Until(() => clinic.TradeState == ClinicTradeState.ReadyForDelivery && !clinic.ConsoleOpen, "The repaired order must reach its real delivery state.");
            Assert.That(clinic.Interact(ClinicTradeAction.Deliver), Is.True, clinic.LastMessage);
            yield return null;
            Assert.That(item == null, Is.True, "Delivery must release the edited instance.");
            yield return Until(() => controller.Item == null, "The controller must release the previous order's history.");
        }

        Dictionary<Renderer, Material[]> SlotSnapshot(GameObject root)
        {
            return root.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => r.sharedMaterials);
        }

        void AssertOtherSlotsUnchanged(Dictionary<Renderer, Material[]> snapshot)
        {
            foreach (var pair in snapshot)
                for (int slot = 0; slot < pair.Value.Length; slot++)
                    if (!controller.Item.Surfaces.Any(s => s.Renderer == pair.Key && s.Slot == slot))
                        Assert.That(pair.Key.sharedMaterials[slot], Is.SameAs(pair.Value[slot]), pair.Key.name + "/slot " + slot);
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        static IEnumerator Until(Func<bool> ready, string reason)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, reason);
        }

        sealed class RetainedPose
        {
            readonly Transform transform;
            readonly Transform parent;
            readonly Vector3 position, scale;
            readonly Quaternion rotation;
            public RetainedPose(Transform value)
            {
                transform = value; parent = value.parent; position = value.localPosition;
                scale = value.localScale; rotation = value.localRotation;
            }
            public void AssertUnchanged()
            {
                Assert.That(transform.parent, Is.SameAs(parent), transform.name);
                Assert.That(transform.localPosition, Is.EqualTo(position), transform.name);
                Assert.That(transform.localScale, Is.EqualTo(scale), transform.name);
                Assert.That(Quaternion.Angle(transform.localRotation, rotation), Is.LessThan(.01f), transform.name);
            }
        }
    }
}
