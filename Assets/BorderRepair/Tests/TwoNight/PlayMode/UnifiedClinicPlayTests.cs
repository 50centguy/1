using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Dock;
using BorderRepair.FirstOrder;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.TwoNight.Tests
{
    public class UnifiedClinicPlayTests
    {
        const string PathToScene = "Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic.unity";
        string directory;
        Mouse mouse;
        Action mouseCleanup;
        Action motionCleanup;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "UnifiedClinicPlay_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            TwoNightSave.OverrideDirectory = directory;
            TwoNightRun.Clear();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            motionCleanup?.Invoke();
            mouseCleanup?.Invoke();
            mouseCleanup = null;
            mouse = null;
            TwoNightSave.OverrideDirectory = null;
            TwoNightRun.Clear();
            Time.timeScale = 1;
            Time.captureDeltaTime = 0;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            yield return null;
        }

        static IEnumerator Load(string path = PathToScene)
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            for (int i = 0; i < 5; i++) yield return null;
        }

        static void AssertSingles()
        {
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a => a.enabled));
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c => c.enabled));
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<UnifiedClinicDirector>(FindObjectsSortMode.None).Length);
        }

        void EnsureMouse()
        {
            if (mouse != null) return;
            var settings = InputSystem.settings;
            var background = settings.backgroundBehavior;
#if UNITY_EDITOR
            var editorInput = settings.editorInputBehaviorInPlayMode;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            mouse = InputSystem.AddDevice<Mouse>("ClinicAcceptanceMouse");
            mouse.MakeCurrent();
            var device = mouse;
            mouseCleanup = () =>
            {
                if (device.added) InputSystem.RemoveDevice(device);
                settings.backgroundBehavior = background;
#if UNITY_EDITOR
                settings.editorInputBehaviorInPlayMode = editorInput;
#endif
            };
        }

        IEnumerator ClickZone(UnifiedClinicDirector clinic, ClinicTradeAction action)
        {
            var zone = UnityEngine.Object.FindObjectsByType<ClinicTradeZone>(FindObjectsSortMode.None).Single(z => z.Action == action);
            var camera = Camera.main;
            Vector2? position = null;
            Physics.SyncTransforms();
            bool HitsZone(Vector2 point) => !FirstOrderInput.IsOverUI(point) &&
                Physics.Raycast(camera.ScreenPointToRay(point), out var hit) &&
                hit.collider.GetComponentInParent<ClinicTradeZone>() == zone;
            // Project collider samples first: wall-facing buttons can be smaller than the screen grid.
            foreach (var collider in zone.GetComponentsInChildren<Collider>().Where(c => c.enabled && c.gameObject.activeInHierarchy))
            {
                var bounds = collider.bounds;
                for (int i = 0; i < 125 && position == null; i++)
                {
                    var fraction = new Vector3(i % 5, (i / 5) % 5, i / 25) / 4f;
                    var world = bounds.min + Vector3.Scale(bounds.size, Vector3.one * .05f + fraction * .9f);
                    var screen = camera.WorldToScreenPoint(world);
                    if (screen.z > 0 && screen.x > 1 && screen.x < Screen.width - 1 &&
                        screen.y > 1 && screen.y < Screen.height - 1 && HitsZone(screen)) position = (Vector2)screen;
                }
            }
            // Find an unobstructed pixel on the actual physical zone, using the player's camera.
            for (int x = 2; x < 80 && position == null; x++)
                for (int y = 2; y < 50 && position == null; y++)
                {
                    var point = new Vector2(Screen.width * x / 80f, Screen.height * y / 50f);
                    if (HitsZone(point)) position = point;
                }
            Assert.IsNotNull(position, "Physical zone must be visible and clickable: " + action);
            EnsureMouse();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position.Value });
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position.Value }.WithButton(MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position.Value });
            yield return null;
        }

        static void Repair(RepairStationController station, RepairDecision decision)
        {
            var session = station.Session;
            Assert.IsTrue(session.SetScanMode(true));
            foreach (var point in session.CurrentCase.inspectionPoints.Where(p => p.requiredForDiagnosis))
                Assert.AreEqual(ScanOutcome.NewFinding, station.ScanPoint(point.pointId));
            Assert.IsTrue(session.TryBeginDiagnosis());
            Assert.IsTrue(session.SubmitDiagnosis(session.CurrentCase.correctDiagnosisId));
            Assert.IsTrue(session.SubmitDecision(decision));
        }

        static void AssertSupportedItem(UnifiedClinicDirector clinic, GameObject item, Transform anchor)
        {
            Assert.IsNotNull(item);
            Assert.AreSame(anchor, item.transform.parent);
            var renderers = item.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            Assert.IsNotEmpty(renderers, "Physical trade item must have visible geometry.");
            float bottom = renderers.Min(r => r.bounds.min.y);
            float support = clinic.TradeSupportWorldY(anchor);
            Assert.GreaterOrEqual(bottom, support - .001f, "Trade item must not sink into the pad.");
            Assert.LessOrEqual(bottom, support + .003f, "Trade item must not float above the pad.");
        }

        static void PrepareNight2()
        {
            var s = TwoNightRun.NewGame(null, true);
            TwoNightRun.ReceiveCommunicator(s, "case_n1_collector_communicator");
            TwoNightRun.CompleteCommunicatorRepair(s, true, s.communicatorCaseId);
            TwoNightRun.DeliverCommunicator(s, s.communicatorCaseId, null);
            TwoNightRun.ConfirmLedger(s); TwoNightRun.FinishIncident(s);
            Assert.IsTrue(TwoNightRun.RegisterUnit07(s, new Unit07SafeState { seated = true, clamped = true,
                powerOff = true, rotorsStopped = true, trayStowed = true, robotUpright = true }, out _));
            Assert.IsTrue(TwoNightSave.Write(s, out _));
            var restored = TwoNightSave.Read();
            Assert.AreEqual(SaveStatus.Ok, restored.status);
            TwoNightRun.Set(restored.state);
            Assert.IsTrue(TwoNightRun.BeginNight2(restored.state));
        }

        static void CaptureScene(Camera camera, string name)
        {
            var target = new RenderTexture(1280, 720, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                texture.Apply();
                var pixels = texture.GetPixels();
                float min = 1, max = 0;
                for (int i = 0; i < pixels.Length; i += 521)
                {
                    var value = pixels[i].grayscale;
                    min = Mathf.Min(min, value); max = Mathf.Max(max, value);
                }
                Assert.Greater(max - min, 0.1f, "Actual shared room must render a nonblank image.");
                Directory.CreateDirectory("Logs");
                File.WriteAllBytes("Logs/" + name + ".png", texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                UnityEngine.Object.Destroy(target);
                UnityEngine.Object.Destroy(texture);
            }
        }

        [UnityTest]
        public IEnumerator PhysicalTrade_RepairSummary_NoIncomeUntilDelivery_ThenSameRoomIncident()
        {
            TwoNightRun.NewGame(null, true);
            yield return Load();
            var clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            Assert.IsTrue(clinic.Initialized);
            AssertSingles();
            Assert.IsTrue(clinic.HasPendingCase);
            Assert.IsFalse(clinic.Robot.isActiveAndEnabled);
            Assert.IsFalse(clinic.Robot.Flow.enabled);
            Assert.IsFalse(UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>().enabled);
            Assert.IsFalse(clinic.Station.View.gameObject.activeInHierarchy);
            Assert.AreSame(clinic.ReceiveAnchor, clinic.Station.Inspector.CurrentItem.transform.parent);
            AssertSupportedItem(clinic, clinic.Station.Inspector.CurrentItem.gameObject, clinic.ReceiveAnchor);
            foreach (var zone in UnityEngine.Object.FindObjectsByType<ClinicTradeZone>(FindObjectsSortMode.None))
            {
                var label = zone.GetComponentInChildren<TextMesh>();
                Assert.IsTrue(zone.GetComponentsInChildren<Collider>().Any(c => c.enabled && c.gameObject.activeInHierarchy));
                if (label == null || !label.gameObject.activeInHierarchy) continue;
                Assert.Greater(Vector3.Dot(label.transform.forward,
                    (label.transform.position - Camera.main.transform.position).normalized), 0.99f,
                    "TextMesh front faces the camera without mirrored labels.");
            }
            CaptureScene(Camera.main, "Clinic_N1_Receive");
            yield return ClickZone(clinic, ClinicTradeAction.Receive);
            Assert.AreEqual(ClinicTradeState.InRepair, clinic.State.communicatorTrade);
            Assert.AreEqual(RepairStage.Inspect, clinic.Station.Session.Stage);
            Assert.IsTrue(clinic.ConsoleOpen);
            AssertSingles();
            Assert.IsFalse(clinic.Interact(ClinicTradeAction.Receive));
            Assert.IsFalse(clinic.Interact(ClinicTradeAction.Deliver));
            Repair(clinic.Station, RepairDecision.Repair);
            yield return null;
            Assert.AreEqual(RepairStage.Summary, clinic.Station.Session.Stage);
            Assert.AreEqual(ClinicTradeState.ReadyForDelivery, clinic.State.communicatorTrade);
            Assert.AreEqual(500, clinic.State.Cash);
            Assert.IsEmpty(clinic.State.transactions);
            Assert.IsFalse(clinic.ConsoleOpen);
            Assert.IsFalse(clinic.Station.View.gameObject.activeInHierarchy, "Summary restart must be inaccessible.");
            Assert.AreSame(clinic.DeliveryAnchor, clinic.Station.Inspector.CurrentItem.transform.parent);
            AssertSupportedItem(clinic, clinic.Station.Inspector.CurrentItem.gameObject, clinic.DeliveryAnchor);
            CaptureScene(Camera.main, "Clinic_N1_AwaitDelivery");
            AssertSingles();
            yield return ClickZone(clinic, ClinicTradeAction.Deliver);
            Assert.AreEqual(800, clinic.State.Cash);
            Assert.AreEqual(ClinicTradeState.Delivered, clinic.State.communicatorTrade);
            Assert.IsFalse(clinic.HasPendingCase);
            Assert.IsNull(clinic.Station.Inspector.CurrentItem);
            Assert.IsTrue(clinic.Counter.LedgerVisible);
            Assert.IsFalse(clinic.Interact(ClinicTradeAction.Deliver));
            Assert.IsFalse(clinic.Interact(ClinicTradeAction.Receive));
            Assert.AreEqual(2, clinic.State.transactions.Count);
            var sceneHandle = SceneManager.GetActiveScene().handle;
            clinic.Counter.ConfirmLedger();
            yield return null;
            Assert.AreEqual(sceneHandle, SceneManager.GetActiveScene().handle, "Incident must use the same room.");
            Assert.IsTrue(clinic.Robot.isActiveAndEnabled);
            Assert.IsFalse(clinic.Counter.gameObject.activeInHierarchy);
            Assert.IsFalse(clinic.Station.gameObject.activeInHierarchy);
            Assert.IsTrue(clinic.Robot.Incident.Playing);
            AssertSingles();
            Time.timeScale = 4;
            float until = Time.realtimeSinceStartup + 40;
            while (clinic.State.phase != TwoNightPhase.Night1Docking && Time.realtimeSinceStartup < until) yield return null;
            Assert.AreEqual(TwoNightPhase.Night1Docking, clinic.State.phase);
            var flow = clinic.Robot.Flow;
            var input = UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>();
            var driver = new FirstOrderAcceptanceDriver(flow, input) { VirtualMouse = mouse };
            flow.Rig.Go(FirstOrderCameraRig.Dock, true);
            yield return null;
            var grips = new[] { "Dock_Clamp_L_Grip", "Dock_Clamp_R_Grip" }
                .Select(n => GameObject.Find(n).GetComponent<DockInteractable>()).ToArray();
            var grip = grips.FirstOrDefault(g => driver.FindClickPoint(g, out _));
            Assert.IsNotNull(grip, "Relocated night-one dock grip is reachable.");
            yield return driver.Click("Seat robot", FirstOrderCameraRig.Dock, grip, true);
            Assert.IsTrue(driver.Records.Last().pass, driver.Records.Last().message);
            until = Time.realtimeSinceStartup + 10;
            while (flow.Dock.State != DockState.SeatedOpen && Time.realtimeSinceStartup < until) yield return null;
            yield return driver.Click("Clamp robot", FirstOrderCameraRig.Dock, grip, true);
            Assert.IsTrue(driver.Records.Last().pass, driver.Records.Last().message);
            until = Time.realtimeSinceStartup + 10;
            while (flow.Dock.State != DockState.Clamped && Time.realtimeSinceStartup < until) yield return null;
            yield return driver.Click("Power off", FirstOrderCameraRig.Dock,
                GameObject.Find("Dock_PowerSwitch_LeverGrip").GetComponent<DockInteractable>(), true);
            Assert.IsTrue(driver.Records.Last().pass, driver.Records.Last().message);
            until = Time.realtimeSinceStartup + 10;
            while (flow.Dock.State != DockState.RotorsStopped && Time.realtimeSinceStartup < until) yield return null;
            yield return null;
            Assert.IsTrue(clinic.Robot.CollectSafeState().IsSafe);
            clinic.Robot.Register();
            Assert.AreEqual(TwoNightPhase.Night1Ended, clinic.State.phase);
            Assert.AreEqual(SaveStatus.Ok, TwoNightSave.Read().status);
            Assert.AreEqual(800, clinic.State.Cash);
            Assert.AreEqual(sceneHandle, SceneManager.GetActiveScene().handle);
            var checkpoint = File.ReadAllText(TwoNightSave.FilePath);
            yield return Load("Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic_Menu.unity");
            var menu = UnityEngine.Object.FindFirstObjectByType<TwoNightMenu>();
            Assert.IsTrue(menu.ContinueButton.interactable);
            menu.ContinueButton.onClick.Invoke();
            until = Time.realtimeSinceStartup + 20;
            while (SceneManager.GetActiveScene().path != PathToScene && Time.realtimeSinceStartup < until) yield return null;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(PathToScene, SceneManager.GetActiveScene().path);
            clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            AssertSingles();
            Assert.IsNull(clinic.Station.Session);
            Assert.AreEqual(TwoNightPhase.Night2Open, clinic.State.phase);
            yield return CompleteRestoredRobotRepair(clinic);
            Assert.AreEqual(800, clinic.State.Cash);
            Assert.AreEqual(2, clinic.State.transactions.Count, "Internal robot work must never post customer trade income.");
            Assert.AreEqual(checkpoint, File.ReadAllText(TwoNightSave.FilePath), "Night2 does not overwrite the night-end checkpoint.");
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator CompleteRestoredRobotRepair(UnifiedClinicDirector clinic)
        {
            var flow = clinic.Robot.Flow;
            float previousCaptureDelta = Time.captureDeltaTime;
            Time.timeScale = 1;
            Time.captureDeltaTime = 1f / 60f;
            var geometry = new MotionGeometryCoverage();
            var addedColliders = new List<MeshCollider>();
            var monitor = flow.gameObject.AddComponent<FirstOrderMotionMonitor>();
            monitor.flow = flow;
            void CaptureCookingError(string message, string stack, LogType type)
            {
                if ((type == LogType.Error || type == LogType.Exception || type == LogType.Warning) &&
                    (message.IndexOf("mesh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     message.IndexOf("cook", StringComparison.OrdinalIgnoreCase) >= 0)) geometry.cookingMessages.Add(message);
            }
            Application.logMessageReceived += CaptureCookingError;
            FirstOrderAcceptanceDriver driver = null;
            motionCleanup = () =>
            {
                motionCleanup = null;
                Application.logMessageReceived -= CaptureCookingError;
                geometry.frames = monitor.frames;
                geometry.travelFrames = monitor.travelFrames;
                geometry.maxStep = monitor.maxStep;
                geometry.maxTurn = monitor.maxTurn;
                geometry.maxCarrySpeed = flow.MaxCarrySpeed;
                geometry.maxStepWhere = monitor.maxStepWhere;
                geometry.maxTurnWhere = monitor.maxTurnWhere;
                geometry.retestPassed = flow.RetestPassed;
                geometry.finalStep = flow.Step.ToString();
                geometry.travelNear = monitor.travelNear.ToArray();
                geometry.sampleDeltaSeconds = Time.captureDeltaTime;
                geometry.bearingViolations = monitor.bearingViolations.ToArray();
                geometry.jumpViolations = monitor.jumpViolations.ToArray();
                geometry.travelHits = monitor.travelHits.ToArray();
                geometry.aabbOnly = monitor.aabbOnly.ToArray();
                geometry.clickFailures = driver == null ? new string[0] : driver.Records.Where(r => !r.pass)
                    .Select(r => r.label + " [" + r.camera + "] " + r.screenPoint + ": " + r.message).ToArray();
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/UnifiedClinicIntegratedMotion.json", JsonUtility.ToJson(geometry, true));
                foreach (var collider in addedColliders) UnityEngine.Object.Destroy(collider);
                UnityEngine.Object.Destroy(monitor);
                Time.captureDeltaTime = previousCaptureDelta;
            };
            try
            {
                var environment = GameObject.Find("UnifiedClinicEnvironment");
                Assert.IsNotNull(environment, "Full geometry regression requires the integrated shared environment.");
                foreach (var filter in environment.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer = filter.GetComponent<Renderer>();
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || filter.sharedMesh == null) continue;
                    geometry.activeMeshes++;
                    if (filter.GetComponentsInParent<Transform>().Any(t => t.name == "UnifiedClinicClutter")) geometry.clutterMeshes++;
                    if (!filter.sharedMesh.isReadable) geometry.unreadableMeshes.Add(filter.name);
                    var collider = filter.GetComponent<Collider>();
                    if (collider == null)
                    {
                        var meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                        addedColliders.Add(meshCollider);
                        meshCollider.convex = false;
                        meshCollider.sharedMesh = filter.sharedMesh;
                        collider = meshCollider;
                        geometry.addedMeshColliders++;
                    }
                    if (!collider.enabled || collider.isTrigger || collider.bounds.size.sqrMagnitude < 1e-10f ||
                        (collider is MeshCollider mesh && (mesh.convex || mesh.sharedMesh != filter.sharedMesh)))
                        geometry.invalidColliders.Add(filter.name);
                }
                Physics.SyncTransforms();
                Assert.Greater(geometry.activeMeshes, 100, "Include all environment pieces, including ceiling/gantry/tools.");
                if (Directory.Exists("ArtSource/UnifiedClinicClutter"))
                    Assert.AreEqual(11, geometry.clutterMeshes, "Include the actual integrated clutter meshes in static coverage.");
                Assert.Greater(geometry.addedMeshColliders, 0);
                Assert.IsEmpty(geometry.invalidColliders, "Static triangle collision coverage is incomplete.");
                Assert.IsEmpty(geometry.cookingMessages, "Mesh cooking/readability errors prevent a clean-path claim.");
                var input = UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>();
                var view = UnityEngine.Object.FindFirstObjectByType<BorderRepair.FirstOrder.Slice.SliceView>();
                Assert.IsTrue(view.ManualOpen);
                view.ToggleManual();
                EnsureMouse();
                int shotIndex = 0;
                IEnumerator Shot(string name)
                {
                    CaptureScene(flow.Rig.Cam, $"Clinic_N2_Order_{++shotIndex:00}");
                    yield return null;
                }
                driver = new FirstOrderAcceptanceDriver(flow, input, Shot) { VirtualMouse = mouse, Timeout = 25 };
                driver.BeforeClick = (record, target) =>
                {
                    if (!driver.FindClickPoint(target, out var point)) return;
                    var hits = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
                    if (hits.Count > 0 || input.ModalBlocked || !input.enabled)
                        geometry.uiBlockedClicks.Add(record.label + " [" + flow.Rig.Current + "] " + point +
                            " UI=" + string.Join(",", hits.Select(h => h.gameObject.name)) +
                            " modal=" + input.ModalBlocked + " enabled=" + input.enabled);
                };
                yield return driver.RunFullOrder(resumeAtInspection: true);
                Assert.IsTrue(driver.AllPassed, string.Join("\n", driver.Records.Where(r => !r.pass).Select(r => r.label + ": " + r.message)));
                Assert.AreEqual(FoStep.Done, flow.Step);
                Assert.IsTrue(flow.RetestPassed, flow.RetestDetail);
                yield return null;
                StringAssert.Contains("复测通过", clinic.Robot.HudText.text);
                StringAssert.DoesNotContain("未修", clinic.Robot.HudText.text);
                StringAssert.Contains("复测通过", clinic.Robot.CaptionText.text);
                CaptureScene(flow.Rig.Cam, "Clinic_N2_RetestComplete");
                Assert.Greater(monitor.travelFrames, 0);
                Assert.IsEmpty(geometry.cookingMessages, "Mesh cooking errors prevent a clean-path claim.");
                Assert.IsEmpty(monitor.bearingViolations, string.Join("\n", monitor.bearingViolations.Take(12)));
                Assert.IsEmpty(monitor.jumpViolations, string.Join("\n", monitor.jumpViolations.Take(12)));
                Assert.IsEmpty(monitor.travelHits, string.Join("\n", monitor.travelHits.Take(12)));
            }
            finally
            {
                motionCleanup?.Invoke();
            }
        }

        [Serializable]
        class MotionGeometryCoverage
        {
            public int activeMeshes, clutterMeshes, addedMeshColliders, frames, travelFrames;
            public bool retestPassed;
            public string finalStep, maxStepWhere, maxTurnWhere;
            public float maxStep, maxTurn, sampleDeltaSeconds, maxCarrySpeed;
            public List<string> unreadableMeshes = new List<string>();
            public List<string> invalidColliders = new List<string>();
            public List<string> cookingMessages = new List<string>();
            public List<string> uiBlockedClicks = new List<string>();
            public string[] bearingViolations, jumpViolations, travelHits, travelNear, aabbOnly, clickFailures;
        }

        [UnityTest]
        public IEnumerator RestoredPowerLever_SkipsUiCoveredCandidate_AndReceivesGenuineMouseClick()
        {
            PrepareNight2();
            yield return Load();
            var clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            var flow = clinic.Robot.Flow;
            var input = UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>();
            var view = UnityEngine.Object.FindFirstObjectByType<BorderRepair.FirstOrder.Slice.SliceView>();
            if (view.ManualOpen) view.ToggleManual();
            flow.Rig.Go(FirstOrderCameraRig.Dock, true);
            yield return null;
            var lever = GameObject.Find("Dock_PowerSwitch_LeverGrip").GetComponent<DockInteractable>();
            Vector2? first = null;
            Physics.SyncTransforms();
            foreach (var collider in lever.GetComponents<Collider>().Where(c => c.enabled))
                for (int i = 0; i < 125 && first == null; i++)
                {
                    var fraction = new Vector3(i % 5, i / 5 % 5, i / 25) / 4f;
                    var point = flow.Rig.Cam.WorldToScreenPoint(collider.bounds.min +
                        Vector3.Scale(collider.bounds.size, Vector3.one * .05f + fraction * .9f));
                    if (point.z > 0 && point.x > 1 && point.y > 1 && point.x < flow.Rig.Cam.pixelWidth - 1 &&
                        point.y < flow.Rig.Cam.pixelHeight - 1 && FirstOrderInput.Pick(flow.Rig.Cam.ScreenPointToRay(point)) == lever)
                        first = (Vector2)point;
                }
            Assert.IsNotNull(first, "The real lever must have a physics-visible candidate.");
            GameObject overlay = null;
            try
            {
                // Keep the regression meaningful even after the production Dock camera is reframed.
                if (!FirstOrderInput.IsOverUI(first.Value))
                {
                    overlay = new GameObject("LeverCandidateBlocker", typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
                    overlay.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                    overlay.GetComponent<Canvas>().sortingOrder = 1000;
                    var blocker = new GameObject("BlockedFirstPixel", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                    var rect = blocker.GetComponent<RectTransform>();
                    rect.SetParent(overlay.transform, false);
                    rect.anchorMin = rect.anchorMax = Vector2.one * .5f;
                    rect.sizeDelta = Vector2.one * 2;
                    yield return null;
                    Assert.IsTrue(RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        (RectTransform)overlay.transform, first.Value, null, out var local));
                    rect.anchoredPosition = local;
                    Canvas.ForceUpdateCanvases();
                    yield return null;
                }
                Assert.IsTrue(FirstOrderInput.IsOverUI(first.Value), "Old first-candidate behavior must encounter real UGUI coverage.");
                EnsureMouse();
                var driver = new FirstOrderAcceptanceDriver(flow, input) { VirtualMouse = mouse };
                Assert.IsTrue(driver.FindClickPoint(lever, out var clear), "A player-clickable lever pixel must remain outside UI.");
                Assert.IsFalse(FirstOrderInput.IsOverUI(clear));
                Assert.AreSame(lever, FirstOrderInput.Pick(flow.Rig.Cam.ScreenPointToRay(clear)));
                var step = flow.Step;
                yield return driver.Click("Unsafe power remains refused", FirstOrderCameraRig.Dock, lever, false);
                Assert.IsTrue(driver.AllPassed, driver.Records.Last().message);
                Assert.IsTrue(driver.Records.Last().hovered);
                Assert.AreSame(lever, input.LastClickHit);
                Assert.AreEqual(step, flow.Step);
                Assert.AreEqual(DockState.RotorsStopped, flow.Dock.State);
                LogAssert.NoUnexpectedReceived();
            }
            finally { if (overlay != null) UnityEngine.Object.Destroy(overlay); }
        }

        [UnityTest]
        public IEnumerator FailedRepair_RetriesSameOccupiedItem_WithoutReceivingAgain()
        {
            TwoNightRun.NewGame(null, true);
            yield return Load();
            var clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            Assert.IsTrue(clinic.Interact(ClinicTradeAction.Receive));
            Repair(clinic.Station, RepairDecision.Refuse);
            yield return null;
            Assert.IsTrue(clinic.Counter.RetryVisible);
            Assert.AreEqual(ClinicTradeState.InRepair, clinic.State.communicatorTrade);
            Assert.IsFalse(clinic.Interact(ClinicTradeAction.Deliver));
            clinic.Counter.RetryCommunicator();
            yield return null;
            Assert.AreEqual(RepairStage.Inspect, clinic.Station.Session.Stage);
            Assert.IsFalse(clinic.HasPendingCase);
            Repair(clinic.Station, RepairDecision.Repair);
            Assert.AreEqual(ClinicTradeState.ReadyForDelivery, clinic.State.communicatorTrade);
            Assert.AreEqual(500, clinic.State.Cash);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ConfiguredTwoCaseQueue_NextItemRequiresPhysicalReceive_AfterDelivery()
        {
            TwoNightRun.NewGame(null, true);
            yield return Load();
            var clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            var session = clinic.Station.Session;
            var originalCases = session.Shift.cases;
            var second = UnityEngine.Object.Instantiate(originalCases[0]);
            second.caseId = "test_second_communicator";
            second.customerName = "Authored second customer";
            second.customerStatement = "Authored second statement";
            second.itemName = "Second receiver";
            second.estimatedRepairCost = 210;
            session.Shift.cases = new List<RepairCaseData> { originalCases[0], second };
            clinic.State.customerQueueCount = 2;
            try
            {
                yield return ClickZone(clinic, ClinicTradeAction.Receive);
                Repair(clinic.Station, RepairDecision.Repair);
                Assert.AreEqual(RepairStage.Result, session.Stage, "Do not advance to the next item on repair completion.");
                Assert.AreEqual(0, session.CaseIndex);
                Assert.IsFalse(clinic.HasPendingCase);
                Assert.IsFalse(clinic.Interact(ClinicTradeAction.Receive));
                yield return ClickZone(clinic, ClinicTradeAction.Deliver);
                Assert.AreEqual(800, clinic.State.Cash);
                Assert.AreEqual(0, session.CaseIndex, "Delivery must not auto-switch or auto-accept the next case.");
                Assert.AreEqual(RepairStage.Result, session.Stage);
                Assert.IsNull(clinic.Station.Inspector.CurrentItem);
                Assert.IsTrue(clinic.HasPendingCase);
                Assert.IsFalse(clinic.Counter.LedgerVisible);
                var preview = clinic.ReceiveAnchor.Cast<Transform>().Single(t => t.name == second.itemPrefab.name + "(Clone)");
                AssertSupportedItem(clinic, preview.gameObject, clinic.ReceiveAnchor);
                Assert.IsFalse(clinic.Interact(ClinicTradeAction.Deliver));
                yield return ClickZone(clinic, ClinicTradeAction.Receive);
                Assert.AreEqual(1, session.CaseIndex);
                Assert.AreEqual(second.caseId, session.CurrentCase.caseId);
                Assert.AreEqual(RepairStage.Inspect, session.Stage);
                var authoredDialogue = clinic.Counter.DialogueButton.transform.parent.Find("Line").GetComponent<UnityEngine.UI.Text>().text;
                StringAssert.Contains(second.customerName, authoredDialogue);
                StringAssert.Contains(second.customerStatement, authoredDialogue);
                Assert.IsFalse(clinic.Interact(ClinicTradeAction.Receive));
                Repair(clinic.Station, RepairDecision.Refuse);
                Assert.IsFalse(session.CurrentRecord.DecisionCorrect, "An incorrect refusal still requires retry.");
                Assert.IsTrue(clinic.Counter.RetryVisible);
                Assert.IsFalse(clinic.Interact(ClinicTradeAction.Deliver));
                StringAssert.Contains(second.itemName, clinic.LastMessage);
                StringAssert.Contains(second.itemName, clinic.Counter.RetryButton.GetComponentInChildren<UnityEngine.UI.Text>().text);
                clinic.Counter.RetryCommunicator();
                yield return null;
                Assert.AreEqual(1, session.CaseIndex, "Retry must stay on the second case.");
                Assert.AreEqual(second.caseId, session.CurrentCase.caseId);
                Assert.AreEqual(2, session.Records.Count, "Keep the settled first record and replace only the failed record.");
                Assert.IsTrue(session.Records[0].DecisionCorrect);
                Assert.AreEqual(800, clinic.State.Cash);
                Assert.AreEqual(2, clinic.State.transactions.Count);
                Repair(clinic.Station, RepairDecision.Repair);
                Assert.AreEqual(800, clinic.State.Cash);
                Assert.AreEqual(RepairStage.Summary, session.Stage);
                yield return ClickZone(clinic, ClinicTradeAction.Deliver);
                Assert.AreEqual(1010, clinic.State.Cash, "Second case uses its existing fee; no invented parts charge.");
                Assert.AreEqual(4, clinic.State.transactions.Count);
                Assert.AreEqual(2, clinic.State.customerTrades.Count);
                Assert.IsFalse(clinic.HasPendingCase);
                Assert.IsFalse(clinic.Interact(ClinicTradeAction.Deliver));
                Assert.IsTrue(clinic.Counter.LedgerVisible);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                session.Shift.cases = originalCases;
                UnityEngine.Object.Destroy(second);
            }
        }

        static RepairCaseData AuthoredCase(string name)
        {
#if UNITY_EDITOR
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<RepairCaseData>("Assets/BorderRepair/Data/Cases/" + name + ".asset");
            Assert.IsNotNull(data, name);
            return data;
#else
            throw new NotSupportedException("Authored queue acceptance runs in the editor.");
#endif
        }

        [UnityTest]
        public IEnumerator AuthoredQueue_RefusalAndReplacementAdvice_ReturnUnpaidThenAllowNextIntake()
        {
            TwoNightRun.NewGame(null, true);
            yield return Load();
            var clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            var session = clinic.Station.Session;
            var shift = session.Shift;
            var originals = shift.cases;
            var beacon = AuthoredCase("Case_02_NavBeacon");
            var drone = AuthoredCase("Case_03_SalvageDrone");
            shift.cases = new List<RepairCaseData> { originals[0], beacon, drone };
            clinic.State.customerQueueCount = 3;
            try
            {
                Assert.IsTrue(clinic.Interact(ClinicTradeAction.Receive));
                Repair(clinic.Station, RepairDecision.Repair);
                Assert.IsTrue(clinic.Interact(ClinicTradeAction.Deliver));
                Assert.AreEqual(800, clinic.State.Cash);
                for (int index = 1; index <= 2; index++)
                {
                    Assert.AreEqual(index - 1, session.CaseIndex, "No automatic next intake.");
                    Assert.IsTrue(clinic.HasPendingCase);
                    Assert.IsTrue(clinic.Interact(ClinicTradeAction.Receive));
                    Assert.AreSame(index == 1 ? beacon : drone, session.CurrentCase);
                    var correct = index == 1 ? RepairDecision.Refuse : RepairDecision.RecommendReplacement;
                    Assert.IsTrue(session.CurrentCase.FindOutcome(correct).isCorrect);
                    Repair(clinic.Station, RepairDecision.Repair);
                    Assert.IsTrue(clinic.Counter.RetryVisible, "Wrong repair cannot charge an authored return-only case.");
                    Assert.IsFalse(clinic.Interact(ClinicTradeAction.Deliver));
                    clinic.Counter.RetryCommunicator();
                    Repair(clinic.Station, correct);
                    Assert.IsTrue(session.CurrentRecord.DecisionCorrect);
                    Assert.AreEqual(ClinicTradeState.ReadyForReturn, clinic.TradeState);
                    Assert.IsFalse(clinic.Counter.RetryVisible);
                    Assert.IsTrue(clinic.State.ActiveTrade.returnedUnpaid);
                    Assert.AreEqual(correct, clinic.State.ActiveTrade.decision);
                    Assert.AreSame(clinic.DeliveryAnchor, clinic.Station.Inspector.CurrentItem.transform.parent);
                    Assert.IsFalse(clinic.Interact(ClinicTradeAction.Receive));
                    Assert.AreEqual(800, clinic.State.Cash);
                    yield return ClickZone(clinic, ClinicTradeAction.Deliver);
                    Assert.AreEqual(ClinicTradeState.Delivered, clinic.TradeState);
                    Assert.IsNull(clinic.Station.Inspector.CurrentItem);
                    Assert.AreEqual(800, clinic.State.Cash);
                    Assert.AreEqual(2, clinic.State.transactions.Count, "Unpaid returns must not create income/cost receipts.");
                    Assert.IsFalse(clinic.Interact(ClinicTradeAction.Deliver));
                }
                Assert.IsFalse(clinic.HasPendingCase);
                Assert.AreEqual(RepairStage.Summary, session.Stage);
                Assert.IsTrue(clinic.Counter.LedgerVisible);
                Assert.AreEqual(3, clinic.State.customerTrades.Count);
                LogAssert.NoUnexpectedReceived();
            }
            finally { shift.cases = originals; }
        }

        [UnityTest]
        public IEnumerator ReentryAfterFirstDelivery_ResetsWholeUnsavedNightWithoutDuplicateIncome() => ReenterCustomerNight(false);

        [UnityTest]
        public IEnumerator ReentryDuringSecondRepair_ResetsWholeUnsavedNightWithoutOccupiedItemDeadlock() => ReenterCustomerNight(true);

        IEnumerator ReenterCustomerNight(bool secondReceived)
        {
            PrepareNight2();
            var checkpoint = File.ReadAllText(TwoNightSave.FilePath);
            TwoNightRun.NewGame(null, true);
            yield return Load();
            var clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            var shift = clinic.Station.Session.Shift;
            var originals = shift.cases;
            var beacon = AuthoredCase("Case_02_NavBeacon");
            shift.cases = new List<RepairCaseData> { originals[0], beacon };
            clinic.State.customerQueueCount = 2;
            try
            {
                Assert.IsTrue(clinic.Interact(ClinicTradeAction.Receive));
                Repair(clinic.Station, RepairDecision.Repair);
                Assert.IsTrue(clinic.Interact(ClinicTradeAction.Deliver));
                Assert.AreEqual(800, clinic.State.Cash);
                if (secondReceived)
                {
                    Assert.IsTrue(clinic.Interact(ClinicTradeAction.Receive));
                    Assert.IsTrue(clinic.Station.Session.SetScanMode(true));
                    var point = beacon.inspectionPoints.First(p => p.requiredForDiagnosis);
                    Assert.AreEqual(ScanOutcome.NewFinding, clinic.Station.ScanPoint(point.pointId));
                    Assert.AreEqual(1, clinic.Station.Session.CaseIndex);
                    Assert.AreEqual(ClinicTradeState.InRepair, clinic.TradeState);
                }
                yield return Load("Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic_Menu.unity");
                yield return Load(); // Direct same-process scene reentry, not menu NewGame.
                clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
                Assert.IsTrue(clinic.Initialized);
                AssertSingles();
                Assert.AreEqual(500, clinic.State.Cash);
                Assert.IsEmpty(clinic.State.transactions);
                Assert.IsEmpty(clinic.State.customerTrades);
                Assert.IsFalse(clinic.State.communicatorSettled);
                Assert.IsNull(clinic.State.communicatorCaseId);
                Assert.AreEqual(0, clinic.Station.Session.CaseIndex);
                Assert.AreEqual(RepairStage.Intake, clinic.Station.Session.Stage);
                Assert.AreEqual(1, clinic.Station.Session.Records.Count);
                Assert.IsFalse(clinic.ConsoleOpen);
                Assert.IsTrue(clinic.HasPendingCase);
                Assert.AreSame(clinic.ReceiveAnchor, clinic.Station.Inspector.CurrentItem.transform.parent);
                Assert.IsTrue(clinic.Interact(ClinicTradeAction.Receive));
                Repair(clinic.Station, RepairDecision.Repair);
                Assert.IsTrue(clinic.Interact(ClinicTradeAction.Deliver));
                Assert.AreEqual(800, clinic.State.Cash, "Replayed first delivery is not added to retained cash.");
                Assert.AreEqual(2, clinic.State.transactions.Count);
                Assert.IsTrue(clinic.Interact(ClinicTradeAction.Receive));
                Assert.AreSame(beacon, clinic.Station.Session.CurrentCase);
                Repair(clinic.Station, RepairDecision.Refuse);
                Assert.IsTrue(clinic.Interact(ClinicTradeAction.Deliver));
                Assert.IsTrue(clinic.Counter.LedgerVisible);
                Assert.AreEqual(800, clinic.State.Cash);
                Assert.AreEqual(checkpoint, File.ReadAllText(TwoNightSave.FilePath), "Unsaved reset never replaces the existing safe checkpoint.");
                LogAssert.NoUnexpectedReceived();
            }
            finally { shift.cases = originals; }
        }

        [UnityTest]
        public IEnumerator CheckpointContinue_SharedNight2_NoCustomerSessionOrDualUi()
        {
            PrepareNight2();
            yield return Load();
            var clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            Assert.IsTrue(clinic.Initialized);
            AssertSingles();
            Assert.IsFalse(clinic.HasPendingCase);
            Assert.IsNull(clinic.Station.Session, "Night2 must not start the customer engine.");
            Assert.IsFalse(clinic.Counter.gameObject.activeInHierarchy);
            Assert.AreEqual(FoStep.InspectLeftEngine, clinic.Robot.Flow.Step);
            Assert.AreEqual(DockState.RotorsStopped, clinic.Robot.Flow.Dock.State);
            Assert.IsFalse(clinic.Robot.Flow.InspectionLocked);
            Assert.IsFalse(clinic.Interact(ClinicTradeAction.Receive));
            Assert.IsFalse(clinic.Interact(ClinicTradeAction.Deliver));
            Assert.AreEqual(800, clinic.State.Cash);
            Assert.AreEqual(2, clinic.State.transactions.Count);
            CaptureScene(Camera.main, "Clinic_N2_CheckpointRestore");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Night2_DockRestoreFailure_KeepsInspectionAndUiLocked() => RestoreFailure(true);

        [UnityTest]
        public IEnumerator Night2_FlowRestoreFailure_KeepsInspectionAndUiLocked() => RestoreFailure(false);

        [UnityTest]
        public IEnumerator SharedMenu_NewAndContinue_BothRouteToSharedClinic()
        {
            const string menuPath = "Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic_Menu.unity";
            yield return Load(menuPath);
            var menu = UnityEngine.Object.FindFirstObjectByType<TwoNightMenu>();
            Assert.IsFalse(menu.ContinueButton.interactable);
            menu.NewButton.onClick.Invoke();
            float until = Time.realtimeSinceStartup + 20;
            while (SceneManager.GetActiveScene().path != PathToScene && Time.realtimeSinceStartup < until) yield return null;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(PathToScene, SceneManager.GetActiveScene().path);
            var clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            Assert.IsTrue(clinic.HasPendingCase);
            Assert.IsTrue(clinic.State.unifiedClinic);
            PrepareNight2();
            yield return Load(menuPath);
            menu = UnityEngine.Object.FindFirstObjectByType<TwoNightMenu>();
            Assert.IsTrue(menu.ContinueButton.interactable);
            menu.ContinueButton.onClick.Invoke();
            until = Time.realtimeSinceStartup + 20;
            while (SceneManager.GetActiveScene().path != PathToScene && Time.realtimeSinceStartup < until) yield return null;
            for (int i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(PathToScene, SceneManager.GetActiveScene().path);
            clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            Assert.AreEqual(TwoNightPhase.Night2Open, clinic.State.phase);
            Assert.IsFalse(clinic.HasPendingCase);
            Assert.IsNull(clinic.Station.Session);
            AssertSingles();
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator RestoreFailure(bool dockFailure)
        {
            PrepareNight2();
            bool inspectionCalled = false;
            void Inject(Scene scene, LoadSceneMode mode)
            {
                if (scene.path != PathToScene) return;
                var robot = UnityEngine.Object.FindFirstObjectByType<TwoNightRobotDirector>();
                Assert.IsTrue(robot.Flow.InspectionLocked, "Inspection starts locked before any restoration call.");
                Assert.IsFalse(UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>().enabled);
                if (dockFailure) robot.DockRestoreOverride = () => false;
                robot.InspectionRestoreOverride = () => { inspectionCalled = true; return false; };
            }
            SceneManager.sceneLoaded += Inject;
            try { yield return Load(); }
            finally { SceneManager.sceneLoaded -= Inject; }
            var clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            Assert.IsTrue(clinic.Robot.RestoreFailed);
            Assert.IsTrue(clinic.Robot.Flow.InspectionLocked);
            Assert.IsFalse(clinic.Robot.Flow.enabled);
            Assert.IsFalse(UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>().enabled);
            var view = UnityEngine.Object.FindFirstObjectByType<BorderRepair.FirstOrder.Slice.SliceView>();
            Assert.IsFalse(view.ManualOpen);
            Assert.IsFalse(view.GetComponent<Canvas>().enabled);
            Assert.IsTrue(clinic.Robot.EndPanelVisible);
            Assert.AreEqual(!dockFailure, inspectionCalled, "Flow restoration must not run after dock failure.");
            Assert.IsFalse(clinic.HasPendingCase);
            Assert.AreEqual(800, clinic.State.Cash);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
