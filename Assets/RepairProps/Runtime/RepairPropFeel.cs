using BorderRepair.Controls;
using BorderRepair.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BorderRepair.RepairProps
{
    // Which hand motion a bench command takes (ReStory-style: every kind of action has its own feel). Screws turn,
    // covers pry and press home, plugs unlatch and pull, boards slide, contacts are scrubbed, terminals crimped, the
    // meter's probe is placed. Power, fixture, tests, poses and delivery stay a single press.
    public static class RepairPropFeel
    {
        // Scene tests that script panel presses switch this off so each press commits at once.
        public static bool Enabled = true;

        public static PropFeel For(PropCommand c)
        {
            if (c == null) return PropFeel.Tap;
            if (c.feel != PropFeel.Auto) return c.feel;
            if (c.kind == PropCommandKind.Measure) return PropFeel.Probe;
            if (c.kind != PropCommandKind.Manipulate) return PropFeel.Tap;
            string id = c.id;
            if (c.tool == RepairActionType.RemoveFastener) return id.StartsWith("install.", System.StringComparison.Ordinal) ? PropFeel.Screw : PropFeel.Unscrew;
            switch (id)
            {
                case "cover.remove": return PropFeel.Pry;
                case "cover.install": return PropFeel.Seat;
                case "isolate": case "harness.release": return PropFeel.Unplug;
                case "supply.connect": case "harness.connect": return PropFeel.Plug;
                case "tray.remove": return PropFeel.SlideOut;
                case "tray.install": return PropFeel.SlideIn;
            }
            if (id.StartsWith("module.remove.", System.StringComparison.Ordinal)) return PropFeel.SlideOut;
            if (id.StartsWith("module.install.", System.StringComparison.Ordinal)) return PropFeel.SlideIn;
            return PropFeel.Tap;
        }

        public static string Prompt(PropFeel feel)
        {
            switch (feel)
            {
                case PropFeel.Unscrew: return "拧出螺钉：在螺钉旁按住左键，绕着它逆时针画圈。";
                case PropFeel.Screw: return "拧紧螺钉：在螺孔旁按住左键，绕着它顺时针画圈。";
                case PropFeel.Pry: return "撬开盖板：按住盖板，顺着箭头拉，直到“啪”地松开。";
                case PropFeel.Seat: return "装回盖板：按住盖板，顺着箭头压到“咔”一声。";
                case PropFeel.Unplug: return "拔插头：先按住卡扣别动，卡扣松开后顺着箭头拔出。";
                case PropFeel.Plug: return "插回插头：对准插座，顺着箭头推到“咔哒”一声。";
                case PropFeel.SlideOut: return "取出：按住，顺着箭头拖出来。";
                case PropFeel.SlideIn: return "装入：按住，顺着箭头推到位。";
                case PropFeel.Scrub: return "清洁触点：在盐渍上按住左键来回擦，擦干净为止。";
                case PropFeel.Crimp: return "压紧端子：按住加力，指针进绿区时松手。";
                case PropFeel.Probe: return "测量：把红表笔拖到圈出的测量点上松手（黑表笔已夹好地线）。";
                default: return "";
            }
        }
    }

    // Pointer input the bench gestures read. Tests switch Simulated on and drive Pointer (screen pixels) and Held.
    public static class RepairPropFeelInput
    {
        public static bool Simulated;
        public static Vector2 Pointer;
        public static bool Held;
        public static Vector2 ScreenPointer => Simulated ? Pointer : RepairInput.PointerPosition;
        public static bool LeftHeld => Simulated ? Held : RepairInput.LeftHeld;
        public static bool OverUi => !Simulated && RepairInput.PointerOverUI;
        public static bool CancelPressed => !Simulated && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    }

    // One gesture's hand motion, in canvas units relative to the part's on-screen anchor. Pure: the bench component
    // feeds it the pointer each frame and draws what it reports; tests feed it directly. Tuned easy: nothing breaks
    // and nothing costs anything; a slip only resets the motion, with a line saying what to do instead.
    public sealed class RepairPropGestureLogic
    {
        public const float TurnDegrees = 360f, GrabRadius = 120f, ScrubRadius = 70f, ScrubPath = 650f, LatchSeconds = .35f,
            LatchSlop = 18f, CrimpRate = .8f, CrimpLow = .55f, CrimpHigh = .88f, ProbeSnap = 46f, ReadSeconds = .7f;
        public readonly PropFeel feel;
        public readonly Vector2 direction;   // the way the hand moves for the drag feels (unit)
        public float Progress { get; private set; }
        public float Aux { get; private set; }   // latch hold (unplug), pressure (crimp)
        public bool Latched { get; private set; }
        public bool Contact { get; private set; }
        public bool Grabbing { get; private set; }
        public bool Done { get; private set; }
        public string Message { get; private set; }
        public int Ticks { get; private set; }   // feedback beats so far (ratchet clicks, scrub strokes, latch, snap)
        public Vector2 ProbeTip { get; private set; }
        public Vector2 Holster { get; private set; }
        bool wasHeld, hasAngle, over;
        float lastAngle, path;
        Vector2 grabAt, last;

        public static float Distance(PropFeel feel)
        {
            switch (feel)
            {
                case PropFeel.Pry: return 110f;
                case PropFeel.Seat: case PropFeel.Unplug: return 70f;
                case PropFeel.Plug: return 60f;
                case PropFeel.SlideOut: case PropFeel.SlideIn: return 130f;
                default: return 0f;
            }
        }

        public static bool Pushes(PropFeel feel) => feel == PropFeel.Seat || feel == PropFeel.Plug || feel == PropFeel.SlideIn;

        /// <param name="exit">The on-screen way the part comes out; pushing feels move against it.</param>
        public RepairPropGestureLogic(PropFeel feel, Vector2 exit, Vector2 holster = default(Vector2))
        {
            this.feel = feel;
            exit = exit.sqrMagnitude > 1e-6f ? exit.normalized : Vector2.up;
            direction = Pushes(feel) ? -exit : exit;
            Holster = holster == Vector2.zero ? new Vector2(-150f, -60f) : holster;
            ProbeTip = Holster;
        }

        public void Step(Vector2 pointer, bool held, float dt)
        {
            if (Done) { wasHeld = held; return; }
            bool pressed = held && !wasHeld, released = !held && wasHeld;
            wasHeld = held;
            switch (feel)
            {
                case PropFeel.Unscrew: case PropFeel.Screw: Turn(pointer, held); break;
                case PropFeel.Scrub: Scrub(pointer, held, pressed); break;
                case PropFeel.Crimp: Crimp(held, pressed, released, dt); break;
                case PropFeel.Probe: Measure(pointer, held, released, dt); break;
                default: Drag(pointer, held, pressed, released, dt); break;
            }
            last = pointer;
        }

        // Circle the screw: screen y points up, so counter-clockwise is a growing angle. Going the wrong way only says so.
        void Turn(Vector2 p, bool held)
        {
            if (!held || p.magnitude < 12f) { hasAngle = false; return; }
            float angle = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
            if (hasAngle)
            {
                float d = Mathf.DeltaAngle(lastAngle, angle);
                if (feel == PropFeel.Screw) d = -d;
                if (d > 0f) { Progress = Mathf.Min(1f, Progress + d / TurnDegrees); Message = null; }
                else if (d < -1f) Message = feel == PropFeel.Unscrew ? "方向反了：拧出要逆时针转。" : "方向反了：拧紧要顺时针转。";
            }
            lastAngle = angle; hasAngle = true;
            Ticks = Mathf.FloorToInt(Progress * 12f);
            if (Progress >= 1f) Done = true;
        }

        // Grab near the part and move along the arrow. Letting go early springs it back. Unplugging first needs the
        // latch held still for a moment; pulling before it lets go does nothing.
        void Drag(Vector2 p, bool held, bool pressed, bool released, float dt)
        {
            if (pressed)
            {
                if (p.magnitude <= GrabRadius) { Grabbing = true; grabAt = p; Message = null; }
                else Message = "先在零件上按住，再拖。";
            }
            if (Grabbing && held)
            {
                if (feel == PropFeel.Unplug && !Latched)
                {
                    if ((p - grabAt).magnitude <= LatchSlop)
                    {
                        Aux = Mathf.Min(1f, Aux + dt / LatchSeconds);
                        if (Aux >= 1f) { Latched = true; Ticks++; grabAt = p; Message = null; }
                    }
                    else { Message = "卡住了：先按住卡扣别动，等它松开再拔。"; Aux = 0f; grabAt = p; }
                    return;
                }
                Progress = Mathf.Clamp01(Vector2.Dot(p - grabAt, direction) / Distance(feel));
                if (Progress >= 1f) { Done = true; Ticks++; }
                return;
            }
            if (released) Grabbing = false;
            if (!Grabbing) Progress = Mathf.MoveTowards(Progress, 0f, dt * 4f);
        }

        // Rub inside the crust; the path walked there is what cleans it.
        void Scrub(Vector2 p, bool held, bool pressed)
        {
            if (!held) return;
            if (p.magnitude > ScrubRadius) { Message = "在盐渍上擦。"; return; }
            Message = null;
            if (!pressed && last.magnitude <= ScrubRadius) path += Mathf.Min((p - last).magnitude, 60f);
            Progress = Mathf.Clamp01(path / ScrubPath);
            Ticks = Mathf.FloorToInt(path / 70f);
            if (Progress >= 1f) Done = true;
        }

        // Hold to squeeze; let go with the needle in the green band. Too early or too far just starts the squeeze again.
        void Crimp(bool held, bool pressed, bool released, float dt)
        {
            if (held)
            {
                if (pressed) Message = null;
                if (!over)
                {
                    Aux = Mathf.Min(1f, Aux + dt * CrimpRate);
                    if (Aux >= 1f) { over = true; Message = "压过头了：松手重来。"; }
                }
                return;
            }
            if (!released) return;
            if (!over && Aux >= CrimpLow && Aux <= CrimpHigh) { Progress = 1f; Done = true; Ticks++; Message = null; }
            else Message = over ? "压过头了：再压一次，指针进绿区就松手。" : "还不够紧：再压一次，指针进绿区再松手。";
            Aux = 0f; over = false;
        }

        // The red probe follows the pointer while held; let go on the marked point and the meter reads.
        void Measure(Vector2 p, bool held, bool released, float dt)
        {
            if (Contact)
            {
                Progress = Mathf.Min(1f, Progress + dt / ReadSeconds);
                if (Progress >= 1f) Done = true;
                return;
            }
            if (held) { ProbeTip = p; Message = null; return; }
            if (released)
            {
                if (p.magnitude <= ProbeSnap) { Contact = true; ProbeTip = Vector2.zero; Ticks++; return; }
                Message = "表笔没碰到测量点：拖到圈里再松手。";
            }
            ProbeTip = Vector2.MoveTowards(ProbeTip, Holster, dt * 900f);
        }
    }
}
