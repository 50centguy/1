using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Dock;
using BorderRepair.FirstOrder;
using BorderRepair.FirstOrder.Slice;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.TwoNight.Tests
{
    public class UnifiedClinicFirstPersonTests
    {
        const string ScenePath = "Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic.unity";
        const string ReportPath = "Logs/UnifiedClinicFirstPerson.json";
        const float GridStep = .18f;
        static readonly Vector3 Spawn = new Vector3(0, .05f, -2.25f);

        [Serializable] class Measurement
        {
            public string label;
            public Vector3 position;
            public float yaw, distance;
        }
        [Serializable] class TestRecord
        {
            public string test, result, failure;
            public ColliderRecord[] colliders;
            public List<Measurement> measurements = new List<Measurement>();
        }
        [Serializable] class ColliderRecord
        {
            public string path, type;
            public bool trigger;
            public Vector3 min, max;
        }
        [Serializable] class Report
        {
            public string timestampUtc, scene, unityVersion;
            public string input = "Virtual Input System keyboard/mouse at synthetic 60 Hz; runtime CharacterController movement. Programmatic aiming and blocker start placement are test setup.";
            public List<TestRecord> tests = new List<TestRecord>();
        }

        readonly List<TestRecord> records = new List<TestRecord>();
        TestRecord record;
        string saveDirectory, previousSaveDirectory;
        float previousTimeScale, previousCaptureDelta;
        CursorLockMode previousCursorLock;
        bool previousCursorVisible;
        Keyboard keyboard;
        Mouse mouse;
        Action mouseCleanup;
        UnifiedClinicDirector clinic;
        FirstOrderFlow flow;
        FirstPersonWalker walker;
        CharacterController controller;
        Collider floor;

        [OneTimeSetUp]
        public void BeginReport() => records.Clear();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            record = new TestRecord { test = TestContext.CurrentContext.Test.Name };
            records.Add(record);
            previousSaveDirectory = TwoNightSave.OverrideDirectory;
            saveDirectory = Path.Combine(Path.GetTempPath(), "UnifiedClinicFp_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = saveDirectory;
            TwoNightRun.Clear();
            previousTimeScale = Time.timeScale;
            previousCaptureDelta = Time.captureDeltaTime;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Time.timeScale = 1;
            Time.captureDeltaTime = 1f / 60f;
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
            if (walker != null) walker.LeaveSystemCursor = true;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            TwoNightSave.OverrideDirectory = previousSaveDirectory;
            TwoNightRun.Clear();
            Time.timeScale = previousTimeScale;
            Time.captureDeltaTime = previousCaptureDelta;
            if (Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
            record.result = TestContext.CurrentContext.Result.Outcome.Status.ToString();
            record.failure = TestContext.CurrentContext.Result.Message;
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, JsonUtility.ToJson(new Report {
                timestampUtc = DateTime.UtcNow.ToString("O"), scene = ScenePath,
                unityVersion = Application.unityVersion, tests = records
            }, true));
            yield return null;
        }

        IEnumerator LoadClinic(bool night2)
        {
            var state = TwoNightRun.NewGame(null, true);
            if (night2)
            {
                Assert.IsTrue(TwoNightRun.ReceiveCommunicator(state, "case_n1_collector_communicator"));
                Assert.IsTrue(TwoNightRun.CompleteCommunicatorRepair(state, true, state.communicatorCaseId));
                Assert.IsTrue(TwoNightRun.DeliverCommunicator(state, state.communicatorCaseId, null));
                Assert.IsTrue(TwoNightRun.ConfirmLedger(state));
                Assert.IsTrue(TwoNightRun.FinishIncident(state));
                Assert.IsTrue(TwoNightRun.RegisterUnit07(state, new Unit07SafeState {
                    seated = true, clamped = true, powerOff = true, rotorsStopped = true,
                    trayStowed = true, robotUpright = true
                }, out var why), why);
                Assert.IsTrue(TwoNightRun.BeginNight2(state));
            }
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(TwoNightScenes.Clinic, LoadSceneMode.Single);
#endif
            for (int i = 0; i < 8; i++) yield return null;
            clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            Assert.IsNotNull(clinic);
            Assert.IsTrue(clinic.Initialized, "Shared clinic initialization must finish before input tests.");
            Assert.AreEqual(night2 ? TwoNightPhase.Night2Open : TwoNightPhase.Night1Counter, clinic.State.phase);
            flow = clinic.Robot.Flow;
            walker = flow.Rig.Walker;
            Assert.IsNotNull(walker, "Shared clinic must serialize the migrated walker.");
            controller = walker.GetComponent<CharacterController>();
            Assert.IsTrue(controller != null && controller.enabled);
            Assert.IsTrue(flow.Rig.Walking, "Born walking must not require a test call to Rig.Walk().");
            Assert.Less(Vector2.Distance(Xz(Spawn), Xz(walker.transform.position)), .08f, "Shared entrance spawn.");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0, walker.Yaw)), 1, "Shared entrance faces into the room.");
            walker.LeaveSystemCursor = true;
            mouse = FirstOrderAcceptanceDriver.CreateVirtualMouse(out mouseCleanup);
            keyboard = InputSystem.AddDevice<Keyboard>("UnifiedClinicFp_Keyboard");
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = ScreenCenter });
            yield return null;
            Physics.SyncTransforms();
            floor = NamedCollider("COL_Floor");
            record.colliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                .Where(c => c.enabled && !OwnCollider(c)).Select(c => new ColliderRecord {
                    path = HierarchyPath(c.transform), type = c.GetType().Name, trigger = c.isTrigger,
                    min = c.bounds.min, max = c.bounds.max
                }).OrderBy(c => c.path).ToArray();
            Note(night2 ? "Night2 birth" : "Night1 birth");
        }

        static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            for (var parent = transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }

        static Vector2 Xz(Vector3 position) => new Vector2(position.x, position.z);
        static Vector2 ScreenCenter => new Vector2(Screen.width * .5f, Screen.height * .5f);

        void Note(string label, float distance = 0)
        {
            record.measurements.Add(new Measurement {
                label = label, position = walker.transform.position, yaw = walker.Yaw, distance = distance
            });
        }

        IEnumerator Keys(float seconds, params Key[] keys)
        {
            keyboard.MakeCurrent();
            float deadline = Time.realtimeSinceStartup + 10;
            for (float elapsed = 0; elapsed < seconds && Time.realtimeSinceStartup < deadline; elapsed += Time.deltaTime)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
                yield return null;
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        IEnumerator MouseDelta(float x, float y)
        {
            mouse.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = ScreenCenter, delta = new Vector2(x, y) });
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = ScreenCenter });
            yield return null;
        }

        IEnumerator LeftClick()
        {
            mouse.MakeCurrent();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = ScreenCenter }.WithButton(MouseButton.Left, true));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = ScreenCenter });
            yield return null;
            yield return null;
        }

        IEnumerator AssertKeyboardAndMouse()
        {
            Assert.IsTrue(walker.Aiming, "Room control must accept input when no modal is open.");
            Assert.IsNull(walker.ScriptedMove, "Movement assertions must read the actual keyboard.");
            var before = walker.transform.position;
            yield return Keys(.3f, Key.W);
            Assert.Greater(Vector2.Distance(Xz(before), Xz(walker.transform.position)), .2f, "W must move the controller from the entrance.");
            Note("Actual W movement");
            float yaw = walker.Yaw;
            walker.LeaveSystemCursor = false;
            yield return MouseDelta(80, 0);
            Assert.Greater(Mathf.DeltaAngle(yaw, walker.Yaw), 3, "Mouse delta must turn the shared room walker.");
            Note("Actual mouse yaw");
            walker.LeaveSystemCursor = true;
        }

        [UnityTest]
        public IEnumerator Night1_BornWalking_KeyboardAndMouseControl()
        {
            yield return LoadClinic(false);
            yield return AssertKeyboardAndMouse();
        }

        [UnityTest]
        public IEnumerator Night1_LongFrame_DoesNotCreateLargeMovementStep()
        {
            yield return LoadClinic(false);
            Time.captureDeltaTime = .5f;
            var before = walker.transform.position;
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            float moved = Vector2.Distance(Xz(before), Xz(walker.transform.position));
            Assert.Greater(moved, .02f);
            Assert.Less(moved, .15f, "A long render/loading frame must not produce a large physical step.");
            Note("Long frame bounded movement", moved);
        }

        [UnityTest]
        public IEnumerator Night2_BornWalking_ManualPausesInput_AndFixedViewsReturn()
        {
            yield return LoadClinic(true);
            var view = UnityEngine.Object.FindFirstObjectByType<SliceView>();
            Assert.IsNotNull(view);
            Assert.IsTrue(view.ManualOpen, "Night2 starts with the existing repair manual open.");
            var before = walker.transform.position;
            float yaw = walker.Yaw;
            Assert.IsFalse(walker.Aiming);
            walker.LeaveSystemCursor = false;
            yield return Keys(.35f, Key.W);
            yield return MouseDelta(80, 0);
            Assert.Less(Vector2.Distance(Xz(before), Xz(walker.transform.position)), .01f, "Manual blocks keyboard movement.");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(yaw, walker.Yaw)), .01f, "Manual blocks mouse yaw.");
            Assert.IsFalse(view.CrosshairVisible);
            walker.LeaveSystemCursor = true;
            Note("Manual paused input");
            view.ToggleManual();
            yield return null;
            yield return null;
            Assert.IsTrue(view.CrosshairVisible);
            yield return AssertKeyboardAndMouse();

            flow.Rig.Go(FirstOrderCameraRig.EngineClose, true);
            yield return null;
            Assert.IsFalse(flow.Rig.Walking);
            var body = walker.transform.position;
            var back = view.GetButton("back");
            Assert.IsNotNull(back);
            Assert.IsTrue(back.interactable);
            back.onClick.Invoke();
            for (int i = 0; i < 30; i++) yield return null;
            Assert.IsTrue(flow.Rig.Walking, "Fixed-view Back button returns to walking.");
            Assert.Less(Vector2.Distance(Xz(body), Xz(walker.transform.position)), .01f, "Returning must not teleport the body.");
            flow.Rig.Go(FirstOrderCameraRig.Bench, true);
            yield return null;
            yield return Keys(.15f, Key.D);
            Assert.IsTrue(flow.Rig.Walking, "An actual movement key exits a fixed view.");
            Note("Fixed views returned to walking");
        }

        ClinicTradeZone ReceiveZone() => UnityEngine.Object.FindObjectsByType<ClinicTradeZone>(FindObjectsSortMode.None)
            .Single(z => z.Action == ClinicTradeAction.Receive);

        bool OwnCollider(Collider collider) => collider == controller || collider.transform.IsChildOf(walker.transform);

        RaycastHit? ZoneHit(ClinicTradeZone zone, Ray ray)
        {
            foreach (var hit in Physics.RaycastAll(ray, 8, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            {
                if (OwnCollider(hit.collider) || hit.collider.GetComponent<FirstPersonBlocker>() != null) continue;
                var hitZone = hit.collider.GetComponentInParent<ClinicTradeZone>();
                if (hitZone == zone) return hit;
                if (!hit.collider.isTrigger || hitZone != null) return null;
            }
            return null;
        }

        IEnumerator AimAtZone(ClinicTradeZone zone, bool near)
        {
            Physics.SyncTransforms();
            Vector3? aim = null;
            foreach (var collider in zone.GetComponentsInChildren<Collider>().Where(c => c.enabled && c.gameObject.activeInHierarchy))
            {
                for (int i = 0; i < 125 && aim == null; i++)
                {
                    var fraction = new Vector3(i % 5, i / 5 % 5, i / 25) / 4f;
                    var point = collider.bounds.min + Vector3.Scale(collider.bounds.size, Vector3.one * .1f + fraction * .8f);
                    var hit = ZoneHit(zone, new Ray(walker.Eye.position, point - walker.Eye.position));
                    if (hit.HasValue && (near ? hit.Value.distance < walker.Reach - .03f : hit.Value.distance > walker.Reach + .1f)) aim = point;
                }
            }
            Assert.IsTrue(aim.HasValue, "Trade zone must have an unobstructed " + (near ? "reachable" : "distant") + " crosshair target.");
            walker.LookAt(aim.Value);
            flow.Rig.Walk(true);
            yield return null;
            yield return null;
            var actual = ZoneHit(zone, flow.Rig.Cam.ViewportPointToRay(new Vector3(.5f, .5f, 0)));
            Assert.IsTrue(actual.HasValue, "Player camera crosshair must hit the actual receive zone.");
            Assert.AreEqual(near, actual.Value.distance <= walker.Reach, "Crosshair hit distance establishes the reach test.");
            Note(near ? "Near receive crosshair" : "Distant receive crosshair", actual.Value.distance);
        }

        [UnityTest]
        public IEnumerator Night1_TradeReachRefusal_NearMouseClick_ConsolePauseResume()
        {
            yield return LoadClinic(false);
            var zone = ReceiveZone();
            var grid = new WalkGrid(this);
            var near = grid.TradeApproach(zone, true);
            var distant = grid.TradeApproach(zone, false);
            walker.PlaceAt(distant, 0);
            yield return null;
            yield return AimAtZone(zone, false);
            int trades = clinic.State.customerTrades.Count;
            yield return LeftClick();
            Assert.AreEqual(trades, clinic.State.customerTrades.Count, "A real distant click must not receive the customer item.");
            Assert.IsFalse(clinic.ConsoleOpen);
            Assert.IsFalse(string.IsNullOrEmpty(clinic.LastMessage), "Reach refusal must produce feedback.");
            StringAssert.Contains("\u591f\u4e0d\u7740", clinic.LastMessage, "Distant physical click must reach the explicit reach guard.");
            yield return WalkPath(grid.Path(walker.transform.position, near), "Walk into trade reach");
            yield return AimAtZone(zone, true);
            yield return LeftClick();
            Assert.AreEqual(trades + 1, clinic.State.customerTrades.Count, "Actual nearby mouse click receives exactly once.");
            Assert.IsTrue(clinic.ConsoleOpen);
            Assert.AreEqual(ClinicTradeState.InRepair, clinic.TradeState);
            Assert.IsFalse(walker.Aiming, "Console/dialogue releases walking control.");
            var before = walker.transform.position;
            float yaw = walker.Yaw;
            walker.LeaveSystemCursor = false;
            yield return Keys(.35f, Key.W);
            yield return MouseDelta(80, 0);
            Assert.Less(Vector2.Distance(Xz(before), Xz(walker.transform.position)), .01f, "N1 modal blocks keyboard movement.");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(yaw, walker.Yaw)), .01f, "N1 modal blocks mouse yaw.");
            walker.LeaveSystemCursor = true;
            clinic.Counter.DismissDialogue();
            clinic.ShowRoom();
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(flow.Rig.Walking && walker.Aiming, "Returning from the console resumes room walking.");
            before = walker.transform.position;
            walker.Look(0, walker.Pitch);
            yield return Keys(.25f, Key.S);
            Assert.Greater(Vector2.Distance(Xz(before), Xz(walker.transform.position)), .12f, "Movement resumes after closing the modal.");
            Assert.AreEqual(trades + 1, clinic.State.customerTrades.Count);
            Note("Console closed and input resumed");
        }

        Collider NamedCollider(string name) => UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
            .Single(c => c.name == name && c.enabled && !c.isTrigger);

        void Capsule(Vector3 feet, out Vector3 bottom, out Vector3 top, out float radius)
        {
            radius = controller.radius + .015f;
            bottom = feet + Vector3.up * (radius + .035f);
            top = feet + Vector3.up * Mathf.Max(radius + .035f, controller.height - radius);
        }

        bool ClearPoint(Vector3 feet)
        {
            Capsule(feet, out var bottom, out var top, out var radius);
            return Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore).All(OwnCollider);
        }

        bool ClearSegment(Vector3 from, Vector3 to)
        {
            if (!ClearPoint(to)) return false;
            Capsule(from, out var bottom, out var top, out var radius);
            var delta = to - from;
            return delta.magnitude < .001f || Physics.CapsuleCastAll(bottom, top, radius, delta.normalized,
                delta.magnitude, ~0, QueryTriggerInteraction.Ignore).All(h => OwnCollider(h.collider));
        }

        IEnumerator WalkPath(List<Vector3> path, string label)
        {
            Assert.IsNull(walker.ScriptedMove);
            foreach (var target in path)
            {
                record.measurements.Add(new Measurement { label = label + " planned waypoint", position = target });
                float deadline = Time.realtimeSinceStartup + 12;
                var previous = Xz(walker.transform.position);
                float lastProgress = Time.realtimeSinceStartup;
                while (Vector2.Distance(Xz(walker.transform.position), Xz(target)) > .03f && Time.realtimeSinceStartup < deadline)
                {
                    var delta = target - walker.transform.position;
                    walker.Look(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, 10);
                    keyboard.MakeCurrent();
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                    yield return null;
                    if (Vector2.Distance(previous, Xz(walker.transform.position)) > .025f)
                    {
                        previous = Xz(walker.transform.position);
                        lastProgress = Time.realtimeSinceStartup;
                    }
                    if (Time.realtimeSinceStartup - lastProgress > 2) break;
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                Note(label + " waypoint", Vector2.Distance(Xz(target), Xz(walker.transform.position)));
                Assert.Less(Vector2.Distance(Xz(target), Xz(walker.transform.position)), .055f,
                    label + ": keyboard movement stalled before " + target + "; stopped at " + walker.transform.position);
                Assert.IsTrue(flow.Rig.Walking && walker.Aiming, label + ": room control must remain active.");
                Assert.Greater(walker.transform.position.y, -.1f, "Connected route must remain on the room floor.");
            }
        }

        [UnityTest]
        public IEnumerator Night1_EntranceTradeBenchDock_ConnectedKeyboardRouteAroundSurgery()
        {
            yield return LoadClinic(false);
            var grid = new WalkGrid(this);
            var bed = NamedCollider("COL_Bed");
            var receive = grid.Approach(clinic.ReceiveAnchor.position, Vector3.left, .85f);
            var bench = grid.Approach(clinic.WorkAnchor.position, Vector3.back, .9f);
            var dock = grid.Approach(flow.Dock.RobotAnchor.position, Vector3.right, .9f);
            var path = grid.Path(walker.transform.position, receive);
            Assert.IsTrue(path.Any(p => p.z > bed.bounds.min.z && p.z < bed.bounds.max.z &&
                (p.x < bed.bounds.min.x - controller.radius || p.x > bed.bounds.max.x + controller.radius)),
                "Entrance-to-trade path must go around the surgical bed.");
            yield return WalkPath(path, "Entrance to trade");
            yield return AimAtZone(ReceiveZone(), true);
            yield return WalkPath(grid.Path(walker.transform.position, bench), "Trade to bench");
            Note("Reached diagnostic bench", Vector2.Distance(Xz(walker.transform.position), Xz(clinic.WorkAnchor.position)));
            yield return WalkPath(grid.Path(walker.transform.position, dock), "Bench to dock");
            Note("Reached Unit07 dock", Vector2.Distance(Xz(walker.transform.position), Xz(flow.Dock.RobotAnchor.position)));
            Assert.IsFalse(clinic.ConsoleOpen);
            Assert.AreEqual(0, clinic.State.customerTrades.Count, "Walking the route must not receive or deliver an item.");
        }

        bool BlockerStart(IEnumerable<Collider> candidates, out Collider blocker, out Vector3 start, out Vector3 direction)
        {
            float feetY = walker.transform.position.y;
            foreach (var candidate in candidates.Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy))
            {
                if (candidate.bounds.max.y < feetY + controller.stepOffset + .2f) continue;
                var center = candidate.bounds.center;
                center.y = Mathf.Clamp(feetY + .75f, candidate.bounds.min.y + .02f, candidate.bounds.max.y - .02f);
                foreach (var outward in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
                    new Vector3(1, 0, 1).normalized, new Vector3(-1, 0, -1).normalized })
                {
                    var outside = center + outward * 5;
                    if (!candidate.Raycast(new Ray(outside, -outward), out var hit, 6)) continue;
                    if (Mathf.Abs(hit.normal.y) > .2f) continue;
                    var normal = new Vector3(hit.normal.x, 0, hit.normal.z).normalized;
                    var feet = hit.point + normal * (controller.radius + .35f);
                    feet.y = feetY;
                    if (!NamedFloorRay(new Ray(feet + Vector3.up * .5f, Vector3.down)) || !ClearPoint(feet)) continue;
                    Capsule(feet, out var bottom, out var top, out var radius);
                    var first = Physics.CapsuleCastAll(bottom, top, radius, -normal, 1, ~0, QueryTriggerInteraction.Ignore)
                        .Where(h => !OwnCollider(h.collider)).OrderBy(h => h.distance).FirstOrDefault();
                    if (first.collider != candidate) continue;
                    blocker = candidate; start = feet; direction = -normal;
                    return true;
                }
            }
            blocker = null; start = direction = default;
            return false;
        }

        IEnumerator AssertBlocked(IEnumerable<Collider> candidates, string label)
        {
            Physics.SyncTransforms();
            Assert.IsTrue(BlockerStart(candidates, out var blocker, out var start, out var direction),
                label + ": no clear outside setup position facing the actual solid blocker.");
            walker.PlaceAt(start, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, 10);
            yield return null;
            var before = walker.transform.position;
            yield return Keys(1.25f, Key.W);
            var stopped = walker.transform.position;
            float progress = Vector3.Dot(stopped - before, direction);
            Assert.Less(progress, .65f, label + ": sustained W must be stopped by " + blocker.name);
            var settle = stopped;
            yield return Keys(.4f, Key.W);
            Assert.Less(Vector2.Distance(Xz(settle), Xz(walker.transform.position)), .035f, label + ": controller stays blocked.");
            Assert.Greater(walker.transform.position.y, -.1f, label + ": player remains above floor.");
            Capsule(walker.transform.position, out var bottom, out var top, out var radius);
            Assert.IsTrue(Physics.OverlapCapsule(bottom, top, radius + controller.skinWidth + .03f, ~0, QueryTriggerInteraction.Ignore)
                .Contains(blocker) || Physics.CapsuleCastAll(bottom, top, radius, direction, .12f, ~0, QueryTriggerInteraction.Ignore)
                .Any(h => h.collider == blocker), label + ": stopped beside the intended blocker, not another obstruction.");
            Note(label + " blocked by " + blocker.name, progress);
        }

        [UnityTest]
        public IEnumerator Night2_WallBedAndDock_StopActualKeyboardMovement()
        {
            yield return LoadClinic(true);
            var view = UnityEngine.Object.FindFirstObjectByType<SliceView>();
            if (view.ManualOpen) view.ToggleManual();
            yield return null;
            var colliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            yield return AssertBlocked(colliders.Where(c => c.name.StartsWith("COL_Wall_")), "Room wall");
            yield return AssertBlocked(new[] { NamedCollider("COL_Bed") }, "Surgical bed");
            var dock = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Single(t => t.name == "Unit07ServiceDock");
            yield return AssertBlocked(colliders.Where(c => c.transform.IsChildOf(dock) ||
                c.transform.IsChildOf(flow.Dock.RobotRoot) || c.GetComponent<FirstPersonBlocker>() != null &&
                c.name.IndexOf("Dock", StringComparison.OrdinalIgnoreCase) >= 0), "Dock/robot assembly");
        }

        static Vector3 TargetCenter(Component target)
        {
            var collider = target.GetComponentInChildren<Collider>();
            if (collider != null) return collider.bounds.center;
            var renderer = target.GetComponentInChildren<Renderer>();
            Assert.IsNotNull(renderer, "Physical target needs a collider or rendered bounds: " + target.name);
            return renderer.bounds.center;
        }

        [UnityTest]
        public IEnumerator Night2_WalkBehindRobot_ActualCrosshairClickReleasesRearLatch()
        {
            yield return LoadClinic(true);
            var view = UnityEngine.Object.FindFirstObjectByType<SliceView>();
            if (view.ManualOpen) view.ToggleManual();
            yield return null;
            var input = UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>();
            Assert.IsNotNull(input);
            var grid = new WalkGrid(this);
            var inspection = grid.PickApproach(flow.Cover, hit => hit == flow.Cover ||
                hit is DockInteractable dock && dock.action == DockAction.EngineLeft);
            yield return WalkPath(inspection.path, "Entrance to left-engine inspection");
            walker.LookAt(inspection.aim);
            flow.Rig.Walk(true);
            yield return null;
            yield return null;
            Assert.IsTrue(input.Hovered == flow.Cover || input.Hovered is DockInteractable d && d.action == DockAction.EngineLeft,
                "Actual walking crosshair must select the left-engine inspection entry.");
            Assert.IsFalse(input.HoverOutOfReach);
            yield return LeftClick();
            Assert.AreEqual(FoStep.ReleaseLatches, flow.Step, "Nearby real click begins inspection without a fixed camera.");
            Assert.IsTrue(flow.Rig.Walking);

            grid = new WalkGrid(this);
            var rear = grid.PickApproach(flow.LatchRear, hit => hit == flow.LatchRear);
            yield return WalkPath(rear.path, "Walk behind robot to rear latch");
            var rearDirection = Xz(TargetCenter(flow.LatchRear)) - Xz(flow.Dock.RobotRoot.position);
            Assert.Greater(Vector2.Dot(Xz(walker.transform.position) - Xz(flow.Dock.RobotRoot.position), rearDirection.normalized),
                .05f, "Latch approach must stand on the rear-latch side of the robot.");
            walker.LookAt(rear.aim);
            flow.Rig.Walk(true);
            yield return null;
            yield return null;
            Assert.AreEqual(flow.LatchRear, input.Hovered, "Actual rear-latch crosshair target: " + FirstOrderInput.NameOf(input.Hovered));
            Assert.IsFalse(input.HoverOutOfReach, "Rear latch must be reachable from a standing position behind the robot.");
            Note("Rear latch within physical reach", input.HoverDistance);
            yield return LeftClick();
            float deadline = Time.realtimeSinceStartup + 10;
            while (flow.Busy && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(flow.Busy, "Rear-latch operation must finish.");
            Assert.AreEqual(PartLocation.Released, flow.LatchRear.Location, "Real crosshair click must release the physical rear latch.");
            Assert.IsTrue(flow.Rig.Walking, "Rear-latch operation must retain walking mode.");
            Note("Rear latch released");
        }

        // Plan on the current collision world; all execution still goes through W and CharacterController.Move.
        sealed class WalkGrid
        {
            readonly UnifiedClinicFirstPersonTests owner;
            readonly Vector3 origin;
            readonly int width, depth;
            readonly bool[] free;

            public WalkGrid(UnifiedClinicFirstPersonTests owner)
            {
                this.owner = owner;
                Physics.SyncTransforms();
                var bounds = owner.floor.bounds;
                float inset = owner.controller.radius + .05f;
                origin = new Vector3(bounds.min.x + inset, owner.walker.transform.position.y, bounds.min.z + inset);
                width = Mathf.FloorToInt((bounds.size.x - 2 * inset) / GridStep) + 1;
                depth = Mathf.FloorToInt((bounds.size.z - 2 * inset) / GridStep) + 1;
                free = new bool[width * depth];
                for (int i = 0; i < free.Length; i++)
                {
                    var feet = Point(i);
                    // Floor ray also rejects the octagonal AABB's outside corners.
                    var ray = new Ray(feet + Vector3.up * .5f, Vector3.down);
                    free[i] = owner.NamedFloorRay(ray) && owner.ClearPoint(feet);
                }
            }

            Vector3 Point(int index) => origin + new Vector3(index % width * GridStep, 0, index / width * GridStep);

            public Vector3 Approach(Vector3 anchor, Vector3 side, float distance)
            {
                var desired = anchor + side * distance;
                desired.y = origin.y;
                int best = -1;
                float error = float.MaxValue;
                for (int i = 0; i < free.Length; i++)
                {
                    if (!free[i]) continue;
                    var offset = Point(i) - anchor;
                    offset.y = 0;
                    if (Vector3.Dot(offset, side) < .2f || offset.magnitude > distance + .35f) continue;
                    float value = (Point(i) - desired).sqrMagnitude;
                    if (value < error) { best = i; error = value; }
                }
                Assert.GreaterOrEqual(best, 0, "No standing clearance beside scene anchor " + anchor);
                Assert.Less(Mathf.Sqrt(error), .4f, "Authored anchor must have a nearby standing approach.");
                return Point(best);
            }

            public Vector3 TradeApproach(ClinicTradeZone zone, bool near)
            {
                int start = Nearest(owner.walker.transform.position);
                var connected = Parents(start);
                var desired = zone.transform.position + Vector3.left * (near ? .85f : 1.9f);
                var eyeOffset = owner.walker.Eye.position - owner.walker.transform.position;
                foreach (int cell in Enumerable.Range(0, free.Length).Where(i => free[i] && connected[i] >= 0 &&
                    Point(i).x < zone.transform.position.x && Vector2.Distance(Xz(Point(i)), Xz(zone.transform.position)) < 3.3f)
                    .OrderBy(i => Vector2.Distance(Xz(Point(i)), Xz(desired))))
                {
                    var eye = Point(cell) + eyeOffset;
                    foreach (var collider in zone.GetComponentsInChildren<Collider>().Where(c => c.enabled))
                        for (int sample = 0; sample < 125; sample++)
                        {
                            var fraction = new Vector3(sample % 5, sample / 5 % 5, sample / 25) / 4f;
                            var aim = collider.bounds.min + Vector3.Scale(collider.bounds.size, Vector3.one * .1f + fraction * .8f);
                            var hit = owner.ZoneHit(zone, new Ray(eye, aim - eye));
                            if (hit.HasValue && (near ? hit.Value.distance < owner.walker.Reach - .03f :
                                hit.Value.distance > owner.walker.Reach + .1f)) return Point(cell);
                        }
                }
                Assert.Fail("No connected capsule-clear standing position with visible trade zone at the required reach distance.");
                return default;
            }

            int Nearest(Vector3 position)
            {
                int best = -1;
                float error = float.MaxValue;
                for (int i = 0; i < free.Length; i++)
                {
                    if (!free[i]) continue;
                    float value = (Xz(Point(i)) - Xz(position)).sqrMagnitude;
                    if (value < error && owner.ClearSegment(position, Point(i))) { best = i; error = value; }
                }
                Assert.GreaterOrEqual(best, 0, "Current feet must connect to the collision grid.");
                Assert.Less(Mathf.Sqrt(error), .35f, "Grid connection must not bridge a blocked spawn.");
                return best;
            }

            public List<Vector3> Path(Vector3 from, Vector3 to)
            {
                int start = Nearest(from), goal = Nearest(to);
                var parent = Parents(start, goal);
                Assert.GreaterOrEqual(parent[goal], 0, "No connected capsule-clear route from " + from + " to " + to + ". Check retained legacy physical colliders.");
                return Reconstruct(parent, start, goal);
            }

            int[] Parents(int start, int goal = -1)
            {
                var parent = Enumerable.Repeat(-1, free.Length).ToArray();
                parent[start] = start;
                var queue = new Queue<int>();
                queue.Enqueue(start);
                while (queue.Count > 0 && (goal < 0 || parent[goal] < 0))
                {
                    int node = queue.Dequeue();
                    foreach (var delta in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                    {
                        int x = node % width + delta.x, z = node / width + delta.y;
                        if (x < 0 || x >= width || z < 0 || z >= depth) continue;
                        int next = z * width + x;
                        if (!free[next] || parent[next] >= 0 || !owner.ClearSegment(Point(node), Point(next))) continue;
                        parent[next] = node;
                        queue.Enqueue(next);
                    }
                }
                return parent;
            }

            List<Vector3> Reconstruct(int[] parent, int start, int goal)
            {
                var path = new List<Vector3>();
                for (int node = goal; ; node = parent[node])
                {
                    path.Add(Point(node));
                    if (node == start) break;
                }
                path.Reverse();
                return path;
            }

            public (List<Vector3> path, Vector3 aim) PickApproach(Component target, Func<Component, bool> accepts)
            {
                int start = Nearest(owner.walker.transform.position);
                var parents = Parents(start);
                var bounds = target.GetComponentsInChildren<Collider>().Where(c => c.enabled && c.gameObject.activeInHierarchy)
                    .Select(c => c.bounds).ToArray();
                Assert.IsNotEmpty(bounds, "No active physical pick bounds for " + target.name);
                var center = TargetCenter(target);
                var eyeOffset = owner.walker.Eye.position - owner.walker.transform.position;
                foreach (int cell in Enumerable.Range(0, free.Length).Where(i => free[i] && parents[i] >= 0 &&
                    Vector2.Distance(Xz(Point(i)), Xz(center)) < owner.walker.Reach + .3f)
                    .OrderBy(i => Vector2.Distance(Xz(Point(i)), Xz(center))))
                {
                    var eye = Point(cell) + eyeOffset;
                    foreach (var bound in bounds)
                        for (int i = 0; i < 125; i++)
                        {
                            var fraction = new Vector3(i % 5, i / 5 % 5, i / 25) / 4f;
                            var aim = bound.min + Vector3.Scale(bound.size, Vector3.one * .1f + fraction * .8f);
                            var hit = FirstOrderInput.Pick(new Ray(eye, aim - eye), 8, out var distance);
                            if (!accepts(hit) || distance > owner.walker.Reach - .08f || distance < .05f) continue;
                            return (Reconstruct(parents, start, cell), aim);
                        }
                }
                Assert.Fail("No connected standing approach with a visible, reachable " + target.name +
                    ". See collider bounds in " + ReportPath);
                return default;
            }
        }

        bool NamedFloorRay(Ray ray) => floor.Raycast(ray, out _, 1);
    }
}
