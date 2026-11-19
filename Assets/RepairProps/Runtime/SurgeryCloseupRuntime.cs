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
    // Close-up surgery on the strapped-in patient's right arm, with the player's mind linked into the surgery table:
    // clicking his arm in the room swaps the first-person view for the table's own overhead camera looking down on the
    // forearm (head end up the screen, his outer side on the left, where the gantry arms come in from). The wheel
    // zooms, a right drag pans, R resets, Esc or the button disconnects.
    //
    // The gantry arms are driven by hand, one at a time (1-6 or the arm bar; 0 / a right click folds it away and
    // the pointer inspects again). The chosen arm comes down (SurgeryGantryV2Runtime.Drive) and its tool tip follows
    // the pointer a few centimetres above whatever is under it. Nothing snaps: the tip has to be put on the part,
    // and the reticle turns green once it is close enough. Holding the left button lowers the tool and works:
    //   drill — hold on a screw or bolt (or the empty hole, to put it back) until it is out / tight;
    //   probe — hold on a plug to pull or seat it; on the neural plug of the new arm, one link pass per hold;
    //   scanner — hold and sweep along the forearm until every section has been read;
    //   cutter — draw it along the tape;  suction — move it about over the socket's contacts;
    //   gripper — press on the cover or a forearm to pick it up, carry it, let go where it goes (the cover anywhere
    //   off the bay, the old forearm on the tray, the new one on the socket ring). A drop in the wrong place is
    //   refused and it stays in the gripper.
    // Letting go mid-step pauses it; working another part drops the unfinished one. The right-hand workspace only
    // shows the case, the steps, the records and the log (SurgeryCloseupPanel); the arm bar and the visor layer are
    // drawn over the view (SurgeryCloseupOverlay). The operation itself is SurgeryProcedureRuntime.
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-50)]   // the camera is placed before the AR layer draws over it
    public sealed class SurgeryCloseupRuntime : MonoBehaviour
    {
        const float DefaultHeight = .62f, MinHeight = .28f, MaxHeight = 1.2f, Tilt = 10f, PanLimit = .55f, Smoothing = 12f, BlendSeconds = .9f, ViewFov = 50f;
        const float Hover = .05f, CarryHover = .12f, ScanHover = .10f, Contact = .003f, AimSmoothing = 9f, DragThreshold = 6f, TouchHeight = .015f;
        /// <summary>How close (horizontally, m) the tip must be to a screw, a bolt or a hole to turn it, to a plug to
        /// probe it, and a new forearm to the socket to let it go.</summary>
        public const float ScrewTolerance = .007f, ProbeTolerance = .010f, FitTolerance = .02f;
        const float CutLength = .05f, SuctionLength = .12f, ScanReach = .07f;
        public const int ScanBins = 8;

        public static SurgeryCloseupRuntime Current { get; private set; }

        readonly List<FirstPersonWalker> cursorOwners = new List<FirstPersonWalker>();
        readonly HashSet<int> scanned = new HashSet<int>();
        readonly Dictionary<string, Vector3> centres = new Dictionary<string, Vector3>();
        UnifiedClinicDirector clinic;
        Camera view, room;
        GameObject tradeUi;
        bool tradeUiWasActive, rightPressing, rightDragging, leftInView, wasDown, latch, aimSet, following, refused;
        bool scripted, scriptedHeld;
        Vector2 scriptedPointer, rightAt;
        float height, targetHeight, blend = 1, fromFov, openedAt, pull, scanMin, scanMax, surfaceY;
        int openedFrame;
        Vector3 fromPosition, follow, pan, targetPan, aim, lastTip, grabOffset;
        Quaternion fromRotation, carriedFrom;
        string carrying;
        Transform carried;
        SurgeryProcedureRuntime.Command anchorCommand;   // the hand-worked step and where its part was when it began
        Vector3 anchorPoint;

        public bool Active { get; private set; }
        public Camera View => view;
        /// <summary>The last part inspected or worked (the whole arm by default).</summary>
        public string Selected { get; private set; } = SurgeryProcedureRuntime.Whole;
        /// <summary>The gantry arm being driven; null = none (the pointer inspects).</summary>
        public string Tool { get; private set; }
        public SurgeryCloseupPanel Panel { get; private set; }
        public SurgeryCloseupOverlay Overlay { get; private set; }
        public float OpenSeconds => Active ? Time.unscaledTime - openedAt : 0;
        public event Action Changed;

        /// <summary>The tool tip of the driven arm (world).</summary>
        public Vector3 Tip { get; private set; }
        /// <summary>The driven arm is down and its tip follows the pointer.</summary>
        public bool ArmReady { get; private set; }
        /// <summary>What the tool would work on where its tip is (the nearest ready command of this tool), or null.</summary>
        public SurgeryProcedureRuntime.Command AimCommand { get; private set; }
        /// <summary>That command's point: the screw, the hole, the plug, the socket, a forearm's middle, a drop place.</summary>
        public Vector3 AimPoint { get; private set; }
        public float AimTolerance { get; private set; }
        /// <summary>Horizontal distance from the tip (or the carried load) to the aim point (m).</summary>
        public float Offset { get; private set; } = float.MaxValue;
        public bool Aligned { get; private set; }
        /// <summary>The left button is held and the tool is working its part (or the gripper holds its load).</summary>
        public bool Working { get; private set; }
        /// <summary>The gripper's load: the command carrying it (cover.remove, forearm.remove, forearm.fit), or null.</summary>
        public string Carrying => carrying;
        public Transform CarriedTransform => carried;
        /// <summary>Where the carried load has to go (for the visor), when it has a place.</summary>
        public Vector3? DropPoint { get; private set; }
        /// <summary>The part under the pointer.</summary>
        public string Hovered { get; private set; }
        /// <summary>Sections of the forearm the scanner has read in this pass.</summary>
        public int ScannedSections => scanned.Count;
        public bool SectionScanned(int section) => scanned.Contains(section);
        /// <summary>Seconds since the link came up (the visor plays its link-in on the first second).</summary>
        public float LinkAge => Active ? Time.unscaledTime - openedAt : 99;
        /// <summary>One line of the driven arm's state, for test messages.</summary>
        public string DebugState => "aim=" + (AimCommand != null ? AimCommand.id : "-") + " aligned=" + Aligned + " off=" + Offset.ToString("F4") + " tol=" + AimTolerance.ToString("F4")
            + " surface=" + surfaceY.ToString("F3") + " latch=" + latch + " down=" + wasDown + " hovered=" + Hovered + " running=" + (Op != null && Op.RunningCommand != null ? Op.RunningCommand.id : "-");

        static SurgeryProcedureRuntime Op => SurgeryProcedureRuntime.Current;
        static SurgeryPatientRuntime Patient => SurgeryPatientRuntime.Current;
        static SurgeryTableV2Runtime Table => SurgeryTableV2Runtime.Current;
        static SurgeryGantryV2Runtime Gantry => SurgeryGantryV2Runtime.Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; SurgeryArmPart.Pickable = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!RepairBenchV2Runtime.IsClinicScene(scene.path)) return;
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;
            var host = new GameObject("SurgeryCloseup (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<SurgeryCloseupRuntime>();
        }

        void Awake()
        {
            Current = this;
            global::BorderRepair.Controls.EscapeRouter.Register(global::BorderRepair.Controls.EscapeRouter.Layer.Closeup, this, () => Active);
        }

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
            if (Op != null) Op.StepDone += OnStepDone;
        }

        // A step finished under a held button: the button has to come up before the tool works again (one bolt per hold).
        void OnStepDone(SurgeryProcedureRuntime.Command c)
        {
            if (wasDown) latch = true;
            if (c.id == "scan") scanned.Clear();
        }

        // ------------------------------------------------------------------------------------------------ open / close
        /// <summary>Links into the table over the secured patient's arm. False when there is nobody on the table to operate on.</summary>
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
            height = targetHeight; pan = targetPan; following = false;
            room.enabled = false;
            view.enabled = true;
            // The room's trade HUD (status card, crosshair, bench buttons) gives way, as it does at the bench.
            tradeUi = clinic != null ? clinic.transform.Find("UnifiedClinicTradeUI")?.gameObject : null;
            if (tradeUi != null) { tradeUiWasActive = tradeUi.activeSelf; tradeUi.SetActive(false); }
            SurgeryArmPart.Pickable = true;
            SurgeryArmPart.SyncAll();
            Selected = SurgeryProcedureRuntime.Whole; Tool = null;
            rightPressing = rightDragging = leftInView = wasDown = latch = aimSet = false;
            Op.Linked = true;
            if (Gantry != null) Gantry.Link(true);
            CacheCentres();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Back to the room. An unfinished hand-worked step is dropped (a carried part goes back); a timed one finishes.</summary>
        public void Close()
        {
            if (!Active) return;
            if (Op != null && Op.Manual) Op.Abort();
            carrying = null; carried = null; Working = false; DropPoint = null;
            Active = false;
            if (Op != null) Op.Linked = false;
            if (Gantry != null) Gantry.Link(false);
            Tool = null;
            if (view != null) view.enabled = false;
            if (room != null) room.enabled = true;
            if (tradeUi != null && tradeUiWasActive && !tradeUi.activeSelf) tradeUi.SetActive(true);
            SurgeryArmPart.Pickable = false;
            SurgeryArmPart.SyncAll();
            rightPressing = rightDragging = leftInView = wasDown = false;
            Changed?.Invoke();
        }

        // A copy of the bench close-up camera: the same lens, URP data and retro look (it is the inspection camera, so it
        // renders sharper than the room view), placed over the operating table.
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
        /// <summary>Tests (and later input methods) drive the pointer and the left button instead of the mouse.</summary>
        public void Script(Vector2 screen, bool held) { scripted = true; scriptedPointer = screen; scriptedHeld = held; }
        public void Unscript() { scripted = false; scriptedHeld = false; }

        Vector2 Pointer => scripted ? scriptedPointer : RepairInput.PointerPosition;

        void Update()
        {
            if (!Active) return;
            if (Patient == null || Op == null || Patient.PhaseNow != SurgeryPatientRuntime.Phase.Secured) { Close(); return; }
            if (global::BorderRepair.Controls.EscapeRouter.Pressed(global::BorderRepair.Controls.EscapeRouter.Layer.Closeup)) { Close(); return; }
            if (Time.frameCount == openedFrame) return;   // the click that opened the view is not a click in it
            if (RepairInput.ResetViewPressed) ResetView();
            Keys();
            bool overUi = RepairInput.PointerOverUI;
            if (RepairInput.RightPressed && !overUi) { rightPressing = true; rightDragging = false; rightAt = RepairInput.PointerPosition; }
            if (rightPressing)
            {
                if (RepairInput.RightHeld)
                {
                    if (!rightDragging && (RepairInput.PointerPosition - rightAt).magnitude > DragThreshold) rightDragging = true;
                    if (rightDragging) Pan(RepairInput.PointerDelta);
                }
                else
                {
                    if (!rightDragging && carrying == null) SelectTool(null);   // a right click puts the arm away
                    rightPressing = rightDragging = false;
                }
            }
            float scroll = RepairInput.ScrollSteps;
            if (scroll != 0 && !overUi) Zoom(scroll);
            if (RepairInput.LeftPressed && !overUi) leftInView = true;
            if (!RepairInput.LeftHeld) leftInView = false;
            Work(scripted ? scriptedHeld : leftInView, Time.deltaTime);
        }

        void Keys()
        {
            int digit = RepairInput.DigitPressed;
            if (digit >= 1 && digit <= SurgeryProcedureRuntime.ToolOrder.Length) SelectTool(SurgeryProcedureRuntime.ToolOrder[digit - 1]);
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame || keyboard.backquoteKey.wasPressedThisFrame) SelectTool(null);
            if (keyboard.lKey.wasPressedThisFrame) ToggleLamp();
            if (keyboard.tKey.wasPressedThisFrame) ToggleTray();
        }

        public void ToggleLamp()
        {
            if (!Active || Op == null || Gantry == null) return;
            Op.Run(Gantry.LampOn ? "lamp.off" : "lamp.on");
            Changed?.Invoke();
        }

        public void ToggleTray()
        {
            if (!Active || Op == null || Table == null) return;
            if (carrying == "forearm.remove" || carrying == "forearm.fit") { Op.Say("夹爪还夹着前臂：先放下再收托盘", FeedbackKind.Warning); return; }
            Op.Run(Table.TrayOut ? "tray.in" : "tray.out");
            Changed?.Invoke();
        }

        /// <summary>Screen pixels of a right drag: the view slides over the table.</summary>
        public void Pan(Vector2 pixels)
        {
            if (view == null || Table == null) return;
            float metres = 2f * (height + pull) * Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height);
            targetPan -= (Table.Right * pixels.x + Table.HeadDir * pixels.y) * metres;
            targetPan = Vector3.ClampMagnitude(targetPan, PanLimit);
        }

        /// <summary>Positive in, negative out, in wheel steps.</summary>
        public void Zoom(float steps) => targetHeight = Mathf.Clamp(targetHeight * (1f - steps * .12f), MinHeight, MaxHeight);

        public void ResetView() { targetHeight = DefaultHeight; targetPan = Vector3.zero; }

        /// <summary>Inspects the part at a screen point (what the pointer does with no arm out).</summary>
        public void Click(Vector2 screen)
        {
            if (!Active || Op == null) return;
            var id = Pick(screen);
            if (id == null) return;
            Select(id);
            Op.Inspect(id);
        }

        /// <summary>The arm part under a screen point: the part of the nearest pick shape the pointer's ray meets (not the load in the gripper).</summary>
        public string Pick(Vector2 screen)
        {
            if (!Active || view == null) return null;
            Physics.SyncTransforms();
            SurgeryArmPart best = null;
            float nearest = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(view.ScreenPointToRay(screen), 3f, ~0, QueryTriggerInteraction.Collide))
            {
                if (carried != null && hit.collider.transform.IsChildOf(carried)) continue;
                var part = hit.collider.GetComponentInParent<SurgeryArmPart>();
                if (part != null && hit.distance < nearest) { best = part; nearest = hit.distance; }
            }
            return best != null ? best.Id : null;
        }

        // The world point under a screen point: a part (the top of its own geometry, not its padded pick shape), the
        // patient, the table (never the gantry, the gripper's load or the room's click targets); past everything, the
        // forearm's height.
        Vector3 Surface(Vector2 screen)
        {
            var ray = view.ScreenPointToRay(screen);
            var rig = Gantry != null && Gantry.Gantry != null ? Gantry.Gantry.transform : null;
            float nearest = float.MaxValue, nearestPart = float.MaxValue;
            SurgeryArmPart part = null;
            foreach (var hit in Physics.RaycastAll(ray, 3f, ~0, QueryTriggerInteraction.Collide))
            {
                var t = hit.collider.transform;
                if (rig != null && t.IsChildOf(rig)) continue;
                if (carried != null && t.IsChildOf(carried)) continue;
                var owner = hit.collider.GetComponentInParent<SurgeryArmPart>();
                if (owner != null) { if (hit.distance < nearestPart) { nearestPart = hit.distance; part = owner; } continue; }
                if (hit.collider.isTrigger) continue;
                if (hit.collider.GetComponentInParent<SurgeryTableControl>() != null) continue;   // the surgery site box
                if (hit.distance < nearest) nearest = hit.distance;
            }
            // A part wins over the bed's coarse collision box, which encloses the patient.
            if (part != null && PartSurface(part, ray, out float onPart)) return ray.GetPoint(onPart);
            if (nearest < float.MaxValue) return ray.GetPoint(nearest);
            var plane = new Plane(Vector3.up, Op.TargetPoint(SurgeryProcedureRuntime.Whole));
            return plane.Raycast(ray, out float at) ? ray.GetPoint(at) : Op.TargetPoint(SurgeryProcedureRuntime.Whole);
        }

        // Where the ray enters the part's own renderers' boxes (not those of parts inside it).
        static bool PartSurface(SurgeryArmPart part, Ray ray, out float distance)
        {
            distance = float.MaxValue;
            foreach (var r in part.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r.GetComponentInParent<SurgeryArmPart>() != part) continue;
                if (r.bounds.IntersectRay(ray, out float d) && d < distance) distance = d;
            }
            return distance < float.MaxValue;
        }

        // ------------------------------------------------------------------------------------------------ the driven arm
        public bool Select(string id)
        {
            if (!Active || string.IsNullOrEmpty(id)) return false;
            if (Selected == id) return true;
            Selected = id;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Brings a gantry arm down to drive (null puts it away). Refused while the gripper holds something.</summary>
        public void SelectTool(string tool)
        {
            if (!Active) return;
            if (carrying != null && tool != "Gripper")
            {
                Op.Say("夹爪还夹着" + LoadName(carrying) + "：先放下", FeedbackKind.Warning);
                return;
            }
            if (tool == Tool) return;
            if (Op != null && Op.Manual) Op.Abort();
            scanned.Clear();
            Tool = tool;
            Working = false; AimCommand = null; Aligned = false; ArmReady = false;
            if (!aimSet) { aim = Op.TargetPoint(SurgeryProcedureRuntime.Whole) + Vector3.up * Hover; aimSet = true; }
            if (Gantry != null) Gantry.Drive(tool, aim);
            Changed?.Invoke();
        }

        static string LoadName(string command) => command == "cover.remove" ? "检修盖板" : command == "forearm.remove" ? "旧前臂" : "替换前臂";

        void Work(bool down, float dt)
        {
            bool pressed = down && !wasDown, released = !down && wasDown;
            wasDown = down;
            if (released) { latch = false; refused = false; }
            var pointer = Pointer;
            bool pointerFree = scripted || !RepairInput.PointerOverUI && (Panel == null || !Panel.ContainsScreenPoint(pointer));
            Hovered = pointerFree ? Pick(pointer) : null;
            if (Tool == null)
            {
                ArmReady = false; Working = false; AimCommand = null; Aligned = false;
                if (pressed && Hovered != null) { Select(Hovered); Op.Inspect(Hovered); }
                return;
            }
            var gantry = Gantry;
            if (gantry == null) return;
            if (pointerFree)
            {
                float hover = carrying != null ? CarryHover : Tool == "Scanner" ? ScanHover : down && !latch ? Contact : Hover;
                var surface = Surface(pointer);
                surfaceY = surface.y;
                var wanted = surface + Vector3.up * hover;
                aim = aimSet ? Vector3.Lerp(aim, wanted, 1f - Mathf.Exp(-AimSmoothing * dt)) : wanted;
                aimSet = true;
                gantry.AimAt(aim);
            }
            ArmReady = gantry.Ready(Tool);
            Tip = gantry.TipPoint(Tool);
            if (carrying != null)
            {
                Carry();
                if (released) Drop();
                lastTip = Tip;
                return;
            }
            Target();
            if (down && !latch && ArmReady) Hold(dt, pressed);
            else
            {
                Working = false;
                if (pressed && !ArmReady) Op.Say(SurgeryProcedureRuntime.ToolName(Tool) + "还在就位", FeedbackKind.Info);
            }
            lastTip = Tip;
        }

        // What the tool would work on: the ready command of this tool whose point is nearest the tip.
        void Target()
        {
            AimCommand = null; Aligned = false; Offset = float.MaxValue; DropPoint = null;
            float best = float.MaxValue;
            foreach (var c in Op.Commands)
            {
                if (c.table || c.tool != Tool || Op.WhyIfIdle(c) != null) continue;
                var point = PointOf(c, out float tolerance);
                // A part that moves as it is worked (a plug coming out) is still aimed at where it was.
                if (Op.Manual && c == Op.RunningCommand && c == anchorCommand) point = anchorPoint;
                float d = Flat(Tip - point).magnitude;
                if (Tool == "Gripper") d = Grabbable(c) || d < .03f ? 0 : d + 1;   // the gripper takes what the pointer is on (or its middle)
                if (d >= best) continue;
                best = d; AimCommand = c; AimPoint = point; AimTolerance = tolerance;
            }
            if (AimCommand == null) return;
            Offset = Tool == "Scanner" ? ScanDistance(Tip) : Tool == "Gripper" ? (best < 1 ? 0 : best - 1) : best;
            Aligned = Offset <= AimTolerance;
        }

        Vector3 PointOf(SurgeryProcedureRuntime.Command c, out float tolerance)
        {
            switch (c.tool)
            {
                case "Drill": tolerance = ScrewTolerance; return HolePoint(c.target);
                case "Probe": tolerance = ProbeTolerance; return Op.TargetPoint(c.target == SurgeryProcedureRuntime.Whole ? "NeuralPlug" : c.target);
                case "Scanner": tolerance = ScanReach; return Op.TargetPoint(SurgeryProcedureRuntime.Whole);
                case "Cutter": tolerance = Radius(c.target) + .004f; return Op.TargetPoint(c.target);
                case "Suction": tolerance = Radius(c.target) + .006f; return Op.TargetPoint(c.target);
                default:
                    tolerance = .02f;
                    if (c.id == "forearm.remove" && Op.OldArm != null) return Op.OldArm.TransformPoint(new Vector3(0, -.2f, 0));
                    if (c.id == "forearm.fit" && Op.NewArm != null) return Op.NewArm.TransformPoint(new Vector3(0, -.2f, 0));
                    return Op.TargetPoint(c.target);
            }
        }

        // The gripper's command is on what the pointer is over: the cover, or anywhere on the forearm it takes.
        bool Grabbable(SurgeryProcedureRuntime.Command c)
        {
            if (Hovered == null) return false;
            if (c.id == "cover.remove") return Hovered == "ServiceCover";
            if (c.id == "forearm.remove") return Op.OldArm != null && SurgeryCyberArm.FindPart(Op.OldArm, Hovered) != null;
            if (c.id == "forearm.fit") return Op.NewArm != null && SurgeryCyberArm.FindPart(Op.NewArm, Hovered) != null;
            return false;
        }

        float Radius(string id)
        {
            var t = Op.TargetTransform(id);
            if (t == null) return .01f;
            var renderers = t.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return .01f;
            var b = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return Mathf.Max(b.extents.x, b.extents.z);
        }

        // Screws and bolts are aimed at their centre; out, at the centre they had in their hole.
        void CacheCentres()
        {
            foreach (var id in SurgeryCyberArm.Bolts.Concat(SurgeryCyberArm.CoverScrews))
            {
                var part = Op.Part(id);
                if (part == null || !part.gameObject.activeInHierarchy || part.transform.localScale != Vector3.one) continue;
                part.Restore();
                centres[id] = part.transform.InverseTransformPoint(Op.TargetPoint(id));
            }
        }

        /// <summary>Where a screw or bolt sits in its hole (where the drill must be put, whether it is in or out).</summary>
        public Vector3 HolePoint(string id)
        {
            var part = Op.Part(id);
            if (part == null) return Op.TargetPoint(id);
            if (!centres.ContainsKey(id) && part.gameObject.activeInHierarchy && part.transform.localScale == Vector3.one)
                centres[id] = part.transform.InverseTransformPoint(Op.TargetPoint(id));
            centres.TryGetValue(id, out var local);
            var parent = part.transform.parent;
            var rest = Matrix4x4.TRS(part.RestPosition, part.RestRotation, Vector3.one);
            return (parent != null ? parent.localToWorldMatrix * rest : rest).MultiplyPoint3x4(local);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        void Hold(float dt, bool pressed)
        {
            var c = AimCommand;
            if (c == null || !Aligned) { Working = false; if (pressed) Refuse(); return; }
            // Down on what is under the pointer (the tool has come down from its hover).
            bool touching = Tool == "Scanner" || Tip.y - surfaceY < TouchHeight;
            if (!touching) { Working = false; return; }
            if (Op.Running && Op.RunningCommand != c)
            {
                if (!Op.Manual) { Working = false; return; }   // a timed step finishes first
                Op.Abort();
                scanned.Clear();
            }
            if (!Op.Running)
            {
                if (!Op.BeginManual(c.id)) { Working = false; return; }
                anchorCommand = c; anchorPoint = AimPoint;
                if (c.id == "scan") StartScan();
                if (Tool == "Gripper") { Grab(c); return; }
            }
            Working = true;
            Select(c.target);
            float moved = Flat(Tip - lastTip).magnitude;
            switch (Tool)
            {
                case "Drill": case "Probe": Op.Advance(dt / Mathf.Max(.3f, c.seconds)); break;
                case "Cutter": Op.Advance(moved / CutLength); break;
                case "Suction": Op.Advance(moved / SuctionLength + dt * .08f); break;
                case "Scanner":
                    int bin = ScanBin(Tip);
                    if (bin >= 0) scanned.Add(bin);
                    Op.SetProgress(scanned.Count / (float)ScanBins);
                    break;
            }
        }

        // A press that cannot work here says why, once.
        void Refuse()
        {
            if (refused) return;
            refused = true;
            string tool = SurgeryProcedureRuntime.ToolName(Tool);
            // Near a part this arm can work: it is just off target.
            if (AimCommand != null && Offset < .04f)
            {
                Op.Say("对准" + SurgeryCyberArm.Label(AimCommand.target == SurgeryProcedureRuntime.Whole ? "NeuralPlug" : AimCommand.target) + "：还偏 "
                    + Mathf.Max(1, Mathf.RoundToInt((Offset - AimTolerance) * 1000)) + " 毫米", FeedbackKind.Info);
                return;
            }
            if (Hovered != null)
            {
                var own = Op.Commands.Where(c => !c.table && c.target == Hovered && !Op.IsDone(c)).ToList();
                var other = own.FirstOrDefault(c => Op.WhyIfIdle(c) == null && c.tool != Tool);
                if (other != null) { Op.Say(SurgeryCyberArm.Label(Hovered) + "用不上" + tool + "：换" + SurgeryProcedureRuntime.ToolName(other.tool), FeedbackKind.Warning); return; }
                var mine = own.FirstOrDefault(c => c.tool == Tool);
                if (mine == null && own.Count > 0) { Op.Say(SurgeryCyberArm.Label(Hovered) + "用不上" + tool, FeedbackKind.Warning); return; }
                if (mine != null && Op.WhyIfIdle(mine) != null) { Op.Say(Op.WhyIfIdle(mine), FeedbackKind.Warning); return; }
            }
            if (AimCommand != null)
            {
                Op.Say("对准" + SurgeryCyberArm.Label(AimCommand.target == SurgeryProcedureRuntime.Whole ? "NeuralPlug" : AimCommand.target) + "：还偏 "
                    + Mathf.Max(1, Mathf.RoundToInt((Offset - AimTolerance) * 1000)) + " 毫米", FeedbackKind.Info);
                return;
            }
            var anyMine = Op.Commands.FirstOrDefault(c => !c.table && c.tool == Tool && !Op.IsDone(c));
            Op.Say(anyMine != null ? tool + "：" + Op.WhyIfIdle(anyMine) : "这里没有" + tool + "能做的", FeedbackKind.Warning);
        }

        // ------------------------------------------------------------------------------------------------ scanner
        void StartScan()
        {
            scanned.Clear();
            var bone = Patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm);
            var arm = Patient.CyberArm != null ? Patient.CyberArm : bone;
            var axis = ScanAxis(bone);
            scanMin = float.MaxValue; scanMax = float.MinValue;
            foreach (var r in arm.GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    float s = Vector3.Dot(Flat(corner - bone.position), axis);
                    scanMin = Mathf.Min(scanMin, s); scanMax = Mathf.Max(scanMax, s);
                }
            }
            if (scanMax - scanMin < .05f) { scanMin = 0; scanMax = .4f; }
            // The very ends (the elbow cap, the fingertips) are not asked for.
            float trim = (scanMax - scanMin) * .06f;
            scanMin += trim; scanMax -= trim;
        }

        // The forearm's long axis seen from above (the arm slopes in the chair pose; the scan goes by the view).
        static Vector3 ScanAxis(Transform bone)
        {
            var axis = Flat(bone.TransformDirection(Vector3.down));
            return axis.sqrMagnitude < 1e-6f ? Vector3.forward : axis.normalized;
        }

        /// <summary>The section of the forearm (0..ScanBins-1) under a point, or -1 off it.</summary>
        public int ScanBin(Vector3 tip)
        {
            var bone = Patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm);
            float s = Vector3.Dot(Flat(tip - bone.position), ScanAxis(bone));
            if (scanMax <= scanMin || s < scanMin - .03f || s > scanMax + .03f || ScanDistance(tip) > ScanReach) return -1;
            return Mathf.Clamp(Mathf.FloorToInt((s - scanMin) / (scanMax - scanMin) * ScanBins), 0, ScanBins - 1);
        }

        /// <summary>The forearm's long axis from end to end (the scanner's sweep), once a scan has begun.</summary>
        public bool ScanSpan(out Vector3 from, out Vector3 to)
        {
            var bone = Patient != null ? Patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm) : null;
            from = to = default;
            if (bone == null || scanMax <= scanMin) return false;
            // On the real (sloping) axis, at the points whose view-from-above distance is the span's ends.
            var axis = bone.TransformDirection(Vector3.down);
            float flat = Mathf.Max(.05f, Flat(axis).magnitude);
            from = bone.position + axis * (scanMin / flat); to = bone.position + axis * (scanMax / flat);
            return true;
        }

        // Sideways from the forearm's long axis, seen from above.
        float ScanDistance(Vector3 tip)
        {
            var bone = Patient != null ? Patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm) : null;
            if (bone == null) return float.MaxValue;
            var axis = Flat(bone.TransformDirection(Vector3.down));
            var d = Flat(tip - bone.position);
            return axis.sqrMagnitude < 1e-6f ? d.magnitude : Vector3.ProjectOnPlane(d, axis.normalized).magnitude;
        }

        // ------------------------------------------------------------------------------------------------ gripper
        void Grab(SurgeryProcedureRuntime.Command c)
        {
            carrying = c.id;
            var cover = c.id == "cover.remove" ? Op.Part("ServiceCover") : null;
            carried = cover != null ? cover.transform : c.id == "forearm.remove" ? Op.OldArm : c.id == "forearm.fit" ? Op.NewArm : null;
            if (carried == null) { Op.Abort(); carrying = null; return; }
            grabOffset = carried.InverseTransformPoint(Tip);   // where the jaws hold it, in its own frame
            carriedFrom = carried.rotation;
            Working = true;
            Select(c.target);
            Op.Say("夹起" + LoadName(c.id) + (c.id == "forearm.remove" ? "：放到托盘的空位上" : c.id == "forearm.fit" ? "：对准接口环放下" : "：移开放下"), FeedbackKind.Info);
            Changed?.Invoke();
        }

        void Carry()
        {
            if (carried == null || !Op.Manual) { carrying = null; carried = null; DropPoint = null; return; }
            Quaternion to = carriedFrom; Vector3 place = Handle;
            if (carrying == "forearm.remove") { place = Op.TraySpot(-1); to = Op.TrayRest; }
            else if (carrying == "forearm.fit")
            {
                var bone = Patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm);
                place = bone.position; to = bone.rotation;
            }
            DropPoint = carrying == "cover.remove" ? (Vector3?)null : place;
            // Near its place the load turns to lie the way it goes (the gripper's wrist); the position is the player's.
            float near = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.04f, .3f, Flat(Handle - place).magnitude));
            carried.rotation = Quaternion.Slerp(carriedFrom, to, near);
            carried.position += Tip - carried.TransformPoint(grabOffset);   // it turns about the jaws, which are at the tip
            Working = true;
            AimCommand = Op.RunningCommand;
            Aligned = DropProblem() == null;
            AimPoint = DropPoint ?? carried.position;
            Offset = DropPoint.HasValue ? Flat(Handle - DropPoint.Value).magnitude : 0;
            AimTolerance = carrying == "forearm.fit" ? FitTolerance : .06f;
        }

        /// <summary>The point of the load that goes onto its place: an old forearm's middle onto the tray, a new one's
        /// elbow end onto the socket ring, the cover itself.</summary>
        public Vector3 Handle => carried == null ? Tip : carrying == "forearm.remove" ? carried.TransformPoint(new Vector3(0, -.2f, 0)) : carried.position;

        // Why the load cannot be let go here (null when it can).
        string DropProblem()
        {
            switch (carrying)
            {
                case "cover.remove":
                {
                    var cover = Op.Part("ServiceCover");
                    var parent = cover.transform.parent;
                    var rest = parent != null ? parent.TransformPoint(cover.RestPosition) : cover.RestPosition;
                    return Vector3.Distance(carried.position, rest) < .05f ? "盖板还压在检修口上：移开一点再松开" : null;
                }
                case "forearm.remove":
                    return Table == null || !Table.TrayOut ? "托盘收着：按 T 摆出托盘"
                        : Flat(Handle - Op.TraySpot(-1)).magnitude > .06f ? "放到托盘的空位上" : null;
                case "forearm.fit":
                {
                    var bone = Patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm);
                    float off = Flat(carried.position - bone.position).magnitude;
                    return off > FitTolerance ? "对准接口环再松开：还差 " + Mathf.Max(1, Mathf.RoundToInt((off - FitTolerance) * 1000)) + " 毫米"
                        : Mathf.Abs(carried.position.y - bone.position.y) > .35f ? "放低一点" : null;   // it is carried at hover height; letting go seats it
                }
            }
            return null;
        }

        void Drop()
        {
            string problem = DropProblem();
            if (problem != null) { Op.Say(problem, FeedbackKind.Warning); return; }
            Op.SetProgress(1);
            carrying = null; carried = null; DropPoint = null; Working = false;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------------------------------------ dev
        /// <summary>Runs a step by itself on the timer (the dev fast-forward key), not by hand.</summary>
        public bool RunFromPanel(string commandId)
        {
            if (!Active || Op == null) return false;
            var c = Op.Find(commandId);
            if (c == null) return false;
            if (Op.Manual) { Op.Abort(); carrying = null; carried = null; DropPoint = null; }
            if (!c.table && c.target != SurgeryProcedureRuntime.Whole) Selected = c.target;
            bool ran = Op.Run(commandId);
            Changed?.Invoke();
            return ran;
        }

        public void InspectSelected()
        {
            if (!Active || Op == null) return;
            Op.Inspect(Selected);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------------------------------------ camera
        Vector3 Focus()
        {
            var lower = Patient != null ? Patient.BoneTransform(SurgeryPatientRuntime.Bone.RightLowerArm) : null;
            if (lower != null) return lower.TransformPoint(new Vector3(0, -.16f, .03f));   // the forearm's middle
            return Table != null ? Table.Floor + Vector3.up : Vector3.up;
        }

        // While a forearm goes between his arm and the tray, the view takes in both (fixed, so it does not chase the
        // pointer); otherwise it rests on the forearm.
        void Frame(out Vector3 target, out float extra)
        {
            target = Focus(); extra = 0;
            if (Op == null || Table == null || Table.Tray == null || Tool != "Gripper") return;
            var c = Op.RunningCommand ?? AimCommand ?? Op.Next();
            if (c == null || c.id != "forearm.remove" && c.id != "forearm.fit") return;
            var tray = Table.Tray.GetComponent<Renderer>();
            var trayPoint = tray != null ? tray.bounds.center : Table.Tray.position;
            // Both the socket and the tray in view, lifted a little so the tray clears the bar and the guide at the bottom.
            target = Vector3.Lerp(target, trayPoint, .5f) - Table.HeadDir * .07f;
            extra = Flat(trayPoint - Focus()).magnitude * 1.05f;
        }

        void Pose(Vector3 focus, float range, out Vector3 position, out Quaternion rotation)
        {
            var head = Table != null ? Table.HeadDir : Vector3.forward;
            float tilt = Tilt * Mathf.Deg2Rad;
            position = focus + (Vector3.up * Mathf.Cos(tilt) - head * Mathf.Sin(tilt)) * range;
            rotation = Quaternion.LookRotation(focus - position, head);
        }

        void LateUpdate()
        {
            if (!Active || view == null) return;
            float k = 1f - Mathf.Exp(-Smoothing * Time.unscaledDeltaTime), slow = 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime);
            height = Mathf.Lerp(height, targetHeight, k); pan = Vector3.Lerp(pan, targetPan, k);
            Frame(out var frameTarget, out var framePull);
            if (!following) { follow = frameTarget; pull = framePull; following = true; }
            follow = Vector3.Lerp(follow, frameTarget, slow); pull = Mathf.Lerp(pull, framePull, slow);
            Pose(follow + pan, height + pull, out var position, out var rotation);
            if (blend < 1f)
            {
                // The link comes up: from where the eyes were in the room, up and over to the table's own camera.
                blend = Mathf.Min(1f, blend + Time.unscaledDeltaTime / BlendSeconds);
                float e = Mathf.SmoothStep(0f, 1f, blend);
                var lift = Vector3.up * (.35f * Mathf.Sin(e * Mathf.PI));
                position = Vector3.Lerp(fromPosition, position, e) + lift;
                rotation = Quaternion.Slerp(fromRotation, rotation, e);
                view.fieldOfView = Mathf.Lerp(fromFov, ViewFov, e);
            }
            else view.fieldOfView = ViewFov;
            view.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>Puts the camera in place at once (tests, screenshots).</summary>
        public void Snap()
        {
            height = targetHeight; pan = targetPan; blend = 1f; following = false;
            LateUpdate();
        }

        void OnDestroy()
        {
            if (Active)
            {
                if (room != null) room.enabled = true;
                if (tradeUi != null && tradeUiWasActive) tradeUi.SetActive(true);
            }
            if (Op != null) Op.StepDone -= OnStepDone;
            SurgeryArmPart.Pickable = false;
            if (view != null) Destroy(view.gameObject);
            if (Panel != null) Destroy(Panel.gameObject);
            if (Overlay != null) Destroy(Overlay.gameObject);
            if (Current == this) Current = null;
        }
    }
}
