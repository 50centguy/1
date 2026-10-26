using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Controls;
using BorderRepair.Core;
using BorderRepair.FirstOrder;
using BorderRepair.TwoNight;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BorderRepair.RepairProps
{
    // Close-up surgery on the strapped-in patient's right arm: the surgery table's counterpart of repairing a prop on
    // the bench. Clicking his arm in the room swaps the first-person view for a close camera on the arm (a copy of the
    // bench inspection camera, so it renders the same) with the same workspace: case / tools / actions / log windows on
    // the right (SurgeryCloseupPanel) and the visor AR layer over the view (SurgeryCloseupOverlay). Dragging turns the
    // view about the arm, the wheel zooms, R resets, Esc or the button leaves. A click selects the part under the
    // pointer; with a gantry tool chosen it runs that tool's one ready command on the part, as the bench does; with the
    // visor (no tool) it inspects the part. The operation itself is SurgeryProcedureRuntime.
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-50)]   // the camera is placed before the AR layer draws over it
    public sealed class SurgeryCloseupRuntime : MonoBehaviour
    {
        const float DefaultDistance = .50f, MinDistance = .24f, MaxDistance = 1.05f, DefaultPitch = 52f, DefaultYaw = -10f;
        const float YawLimit = 80f, MinPitch = 10f, MaxPitch = 84f, DegreesPerPixel = .3f, DragThreshold = 6f, Smoothing = 14f, BlendSeconds = .35f;
        // Degrees the camera turns right of the arm, so the arm sits in the free view left of the workspace column.
        const float Aside = 9f;

        public static SurgeryCloseupRuntime Current { get; private set; }

        readonly List<FirstPersonWalker> cursorOwners = new List<FirstPersonWalker>();
        UnifiedClinicDirector clinic;
        Camera view, room;
        GameObject tradeUi;
        bool tradeUiWasActive, pressing, dragging;
        float yaw, pitch, distance, targetYaw, targetPitch, targetDistance, blend = 1, fromFov, openedAt;
        int openedFrame;
        Vector3 fromPosition, follow;
        float pull;
        bool following;
        Quaternion fromRotation;
        Vector2 pressAt;

        public bool Active { get; private set; }
        public Camera View => view;
        /// <summary>The selected part id (the whole arm by default).</summary>
        public string Selected { get; private set; } = SurgeryProcedureRuntime.Whole;
        /// <summary>The gantry tool in use; null = the visor (inspect).</summary>
        public string Tool { get; private set; }
        public SurgeryCloseupPanel Panel { get; private set; }
        public SurgeryCloseupOverlay Overlay { get; private set; }
        public float OpenSeconds => Active ? Time.unscaledTime - openedAt : 0;
        public event Action Changed;

        static SurgeryProcedureRuntime Op => SurgeryProcedureRuntime.Current;
        static SurgeryPatientRuntime Patient => SurgeryPatientRuntime.Current;
        static SurgeryTableV2Runtime Table => SurgeryTableV2Runtime.Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; SurgeryArmPart.Pickable = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, SurgeryTableV2Runtime.ScenePath, StringComparison.OrdinalIgnoreCase)) return;
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;
            var host = new GameObject("SurgeryCloseup (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<SurgeryCloseupRuntime>();
        }

        void Awake() => Current = this;

        void Start()
        {
            var panel = new GameObject("SurgeryCloseupPanel_Runtime");
            SceneManager.MoveGameObjectToScene(panel, gameObject.scene);
            Panel = panel.AddComponent<SurgeryCloseupPanel>();
            Panel.Initialize(this);
            var visor = new GameObject("SurgeryCloseupOverlay_Runtime");
            SceneManager.MoveGameObjectToScene(visor, gameObject.scene);
            Overlay = visor.AddComponent<SurgeryCloseupOverlay>();
            Overlay.Initialize(this);
        }

        // ------------------------------------------------------------------------------------------------ open / close
        /// <summary>Opens the close-up on the secured patient's arm. False when there is nobody on the table to operate on.</summary>
        public bool Open()
        {
            if (Active) return true;
            if (Patient == null || Op == null || Patient.PhaseNow != SurgeryPatientRuntime.Phase.Secured) return false;
            if (clinic == null) clinic = FindFirstObjectByType<UnifiedClinicDirector>();
            if (clinic != null && clinic.ConsoleOpen) return false;
            var rig = clinic != null && clinic.Robot != null && clinic.Robot.Flow != null ? clinic.Robot.Flow.Rig : null;
            room = rig != null && rig.Cam != null ? rig.Cam : Camera.main;
            if (room == null) return false;
            if (view == null && (view = MakeCamera()) == null) return false;
            var walker = rig != null ? rig.Walker : null;
            if (walker != null && !cursorOwners.Contains(walker))
            {
                // While open the walker frees the pointer and stands still (it neither moves nor turns the head).
                walker.CursorRequests.Add(() => Current != null && Current.Active);
                cursorOwners.Add(walker);
            }
            Active = true;
            openedFrame = Time.frameCount; openedAt = Time.unscaledTime;
            fromPosition = room.transform.position; fromRotation = room.transform.rotation; fromFov = room.fieldOfView; blend = 0;
            ResetView();
            yaw = targetYaw; pitch = targetPitch; distance = targetDistance; following = false;
            room.enabled = false;
            view.enabled = true;
            // The room's trade HUD (status card, crosshair, bench buttons) gives way, as it does at the bench.
            tradeUi = clinic != null ? clinic.transform.Find("UnifiedClinicTradeUI")?.gameObject : null;
            if (tradeUi != null) { tradeUiWasActive = tradeUi.activeSelf; tradeUi.SetActive(false); }
            SurgeryArmPart.Pickable = true;
            SurgeryArmPart.SyncAll();
            Selected = SurgeryProcedureRuntime.Whole; Tool = null;
            pressing = dragging = false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Back to the room. The operation keeps its state (a command already running finishes).</summary>
        public void Close()
        {
            if (!Active) return;
            Active = false;
            if (view != null) view.enabled = false;
            if (room != null) room.enabled = true;
            if (tradeUi != null && tradeUiWasActive && !tradeUi.activeSelf) tradeUi.SetActive(true);
            SurgeryArmPart.Pickable = false;
            SurgeryArmPart.SyncAll();
            pressing = dragging = false;
            Changed?.Invoke();
        }

        // A copy of the bench close-up camera: the same lens, URP data and retro look (it is the inspection camera, so it
        // renders sharper than the room view), placed at the operating table.
        Camera MakeCamera()
        {
            var source = clinic != null && clinic.Station != null && clinic.Station.Inspector != null ? clinic.Station.Inspector.ViewCamera : null;
            if (source == null) source = room;
            var copy = Instantiate(source.gameObject);
            copy.name = "SurgeryCloseupCamera";
            SceneManager.MoveGameObjectToScene(copy, gameObject.scene);
            copy.tag = "Untagged";
            foreach (var listener in copy.GetComponents<AudioListener>()) Destroy(listener);   // the room camera keeps the ears
            foreach (Transform child in copy.transform) Destroy(child.gameObject);
            var cam = copy.GetComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = .02f;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, room.farClipPlane);
            cam.clearFlags = room.clearFlags; cam.backgroundColor = room.backgroundColor; cam.cullingMask = room.cullingMask;
            cam.depth = room.depth;
            return cam;
        }

        // ------------------------------------------------------------------------------------------------ input
        void Update()
        {
            if (!Active) return;
            if (Patient == null || Op == null || Patient.PhaseNow != SurgeryPatientRuntime.Phase.Secured) { Close(); return; }
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (Time.frameCount == openedFrame) return;   // the click that opened the view is not a click in it
            if (RepairInput.ResetViewPressed) ResetView();
            bool overUi = RepairInput.PointerOverUI;
            if ((RepairInput.LeftPressed || RepairInput.RightPressed) && !overUi) { pressing = true; dragging = false; pressAt = RepairInput.PointerPosition; }
            if (pressing)
            {
                if (RepairInput.LeftHeld || RepairInput.RightHeld)
                {
                    if (!dragging && (RepairInput.PointerPosition - pressAt).magnitude > DragThreshold) dragging = true;
                    if (dragging) Orbit(RepairInput.PointerDelta * DegreesPerPixel);
                }
                else
                {
                    if (!dragging && RepairInput.LeftReleased) Click(RepairInput.PointerPosition);
                    pressing = dragging = false;
                }
            }
            float scroll = RepairInput.ScrollSteps;
            if (scroll != 0 && !overUi) Zoom(scroll);
        }

        public void Orbit(Vector2 degrees)
        {
            targetYaw = Mathf.Clamp(targetYaw - degrees.x, DefaultYaw - YawLimit, DefaultYaw + YawLimit);
            targetPitch = Mathf.Clamp(targetPitch - degrees.y, MinPitch, MaxPitch);
        }

        /// <summary>Positive in, negative out, in wheel steps.</summary>
        public void Zoom(float steps) => targetDistance = Mathf.Clamp(targetDistance * (1f - steps * .12f), MinDistance, MaxDistance);

        public void ResetView() { targetYaw = DefaultYaw; targetPitch = DefaultPitch; targetDistance = DefaultDistance; }

        /// <summary>Same as a left click at a screen point (tests and later input methods use it).</summary>
        public void Click(Vector2 screen)
        {
            if (!Active || Op == null) return;
            var id = Pick(screen);
            if (id == null) return;
            Select(id);
            if (Tool == null) { Op.Inspect(id); return; }
            var own = Op.Commands.Where(c => !c.table && c.tool == Tool && c.target == id).ToList();
            var ready = own.Where(c => Op.Why(c) == null).ToList();
            // A tool aimed anywhere on the arm also does the whole arm's work (the scan, the link test).
            var whole = Op.Commands.Where(c => !c.table && c.tool == Tool && c.target == SurgeryProcedureRuntime.Whole).ToList();
            if (ready.Count == 0) ready = whole.Where(c => Op.Why(c) == null).ToList();
            if (ready.Count == 1) { Op.Run(ready[0].id); Changed?.Invoke(); return; }
            var blocked = own.FirstOrDefault(c => !Op.IsDone(c)) ?? own.FirstOrDefault() ?? whole.FirstOrDefault(c => !Op.IsDone(c));
            Op.Say(blocked != null ? Op.Why(blocked) : SurgeryCyberArm.Label(id) + "用不上" + SurgeryProcedureRuntime.ToolName(Tool) + "。", FeedbackKind.Warning);
        }

        /// <summary>The arm part under a screen point: the part of the nearest pick shape the pointer's ray meets.</summary>
        public string Pick(Vector2 screen)
        {
            if (!Active || view == null) return null;
            Physics.SyncTransforms();
            SurgeryArmPart best = null;
            float nearest = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(view.ScreenPointToRay(screen), 3f, ~0, QueryTriggerInteraction.Collide))
            {
                var part = hit.collider.GetComponentInParent<SurgeryArmPart>();
                if (part != null && hit.distance < nearest) { best = part; nearest = hit.distance; }
            }
            return best != null ? best.Id : null;
        }

        // ------------------------------------------------------------------------------------------------ selection
        public bool Select(string id)
        {
            if (!Active || string.IsNullOrEmpty(id)) return false;
            Selected = id;
            Changed?.Invoke();
            return true;
        }

        public void SelectTool(string tool)
        {
            if (!Active) return;
            Tool = tool;
            Changed?.Invoke();
        }

        /// <summary>A command row: takes its tool, selects its part and runs it (the flow still decides).</summary>
        public bool RunFromPanel(string commandId)
        {
            if (!Active || Op == null) return false;
            var c = Op.Find(commandId);
            if (c == null) return false;
            if (!c.table)
            {
                Tool = c.tool;
                if (c.target != SurgeryProcedureRuntime.Whole) Selected = c.target;
            }
            bool ran = Op.Run(commandId);
            Changed?.Invoke();
            return ran;
        }

        /// <summary>A run of commands one after another (a set of bolts), in order, each when it can.</summary>
        public void RunBatch(IEnumerable<string> commandIds)
        {
            if (!Active || Op == null) return;
            var ids = commandIds.ToList();
            if (ids.Count == 0) return;
            Tool = Op.Find(ids[0])?.tool;
            Op.RunAll(ids);
            Changed?.Invoke();
        }

        public void InspectSelected()
        {
            if (!Active || Op == null) return;
            Tool = null;
            Op.Inspect(Selected);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------------------------------------ camera
        Vector3 Focus()
        {
            var lower = Patient != null ? Patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm) : null;
            if (lower != null) return lower.TransformPoint(new Vector3(0, -.10f, .03f));   // the socket end, where the work is
            return Table != null ? Table.Floor + Vector3.up : Vector3.up;
        }

        // An orbit about the forearm from his outer side (west of the table): yaw turns toward his head (+) or feet (-),
        // pitch rises toward looking straight down.
        void Pose(Vector3 focus, float range, out Vector3 position, out Quaternion rotation)
        {
            var outward = Table != null ? -Table.Right : Vector3.left;
            var level = Quaternion.AngleAxis(yaw, Vector3.up) * outward;
            float p = pitch * Mathf.Deg2Rad;
            position = focus + (level * Mathf.Cos(p) + Vector3.up * Mathf.Sin(p)) * range;
            rotation = Quaternion.LookRotation(focus - position, Vector3.up) * Quaternion.Euler(0, Aside, 0);
        }

        // While the gripper carries a forearm between his arm and the tray (at the table's foot), the view eases back
        // to keep both in frame, then returns to the socket.
        void Follow(Vector3 socket, out Vector3 target, out float pull)
        {
            target = socket; pull = 0;
            var c = Op != null ? Op.RunningCommand : null;
            var carried = c == null ? null : c.id == "forearm.remove" ? Op.OldArm : c.id == "forearm.fit" ? Op.NewArm : null;
            if (carried == null) return;
            var arm = carried.TransformPoint(new Vector3(0, -.2f, 0));
            target = Vector3.Lerp(socket, arm, .5f);
            pull = Vector3.Distance(socket, arm) * .8f;
        }

        void LateUpdate()
        {
            if (!Active || view == null) return;
            float k = 1f - Mathf.Exp(-Smoothing * Time.unscaledDeltaTime), slow = 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime);
            yaw = Mathf.Lerp(yaw, targetYaw, k); pitch = Mathf.Lerp(pitch, targetPitch, k); distance = Mathf.Lerp(distance, targetDistance, k);
            Follow(Focus(), out var followTarget, out var followPull);
            if (!following) { follow = followTarget; pull = followPull; following = true; }
            follow = Vector3.Lerp(follow, followTarget, slow); pull = Mathf.Lerp(pull, followPull, slow);
            Pose(follow, distance + pull, out var position, out var rotation);
            if (blend < 1f)
            {
                // From where the eyes were in the room into the orbit.
                blend = Mathf.Min(1f, blend + Time.unscaledDeltaTime / BlendSeconds);
                float e = Mathf.SmoothStep(0f, 1f, blend);
                position = Vector3.Lerp(fromPosition, position, e);
                rotation = Quaternion.Slerp(fromRotation, rotation, e);
                view.fieldOfView = Mathf.Lerp(fromFov, ViewFov, e);
            }
            else view.fieldOfView = ViewFov;
            view.transform.SetPositionAndRotation(position, rotation);
        }

        const float ViewFov = 50f;

        /// <summary>Puts the camera on its orbit at once (tests, screenshots).</summary>
        public void Snap()
        {
            yaw = targetYaw; pitch = targetPitch; distance = targetDistance; blend = 1f; following = false;
            LateUpdate();
        }

        void OnDestroy()
        {
            if (Active)
            {
                if (room != null) room.enabled = true;
                if (tradeUi != null && tradeUiWasActive) tradeUi.SetActive(true);
            }
            SurgeryArmPart.Pickable = false;
            if (view != null) Destroy(view.gameObject);
            if (Panel != null) Destroy(Panel.gameObject);
            if (Overlay != null) Destroy(Overlay.gameObject);
            if (Current == this) Current = null;
        }
    }
}
