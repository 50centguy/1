using System;
using System.Collections.Generic;
using BorderRepair.Narrative;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // Bench gestures. Between pressing a part command (its panel row, or a click on the part with its tool) and the
    // command committing, the hand does the motion: turn a screw, pry the cover, hold a latch and pull, slide a board,
    // scrub a contact, crimp a terminal, place the meter's probe. A visor widget over the part shows the motion and its
    // progress, the part moves with it (parts the flow may move only, put back before the commit), and each kind has its
    // own sound. The flow is untouched until the motion finishes; Esc or pressing another command drops it at no cost.
    // A screw set's batch button is the electric screwdriver: one press, the screws spin out (or in) one after another.
    public sealed class RepairPropGesture : MonoBehaviour
    {
        const float AutoSeconds = .3f, FlashSeconds = .3f;
        static readonly Color Amber = RepairPropCyberStyle.Amber, Dim = RepairPropCyberStyle.AmberDim, Ok = RepairPropCyberStyle.Ok,
            Warn = RepairPropCyberStyle.Warn, Hot = RepairPropCyberStyle.Hot, Bad = RepairPropCyberStyle.Bad;
        static Sprite ringSprite, dotSprite, crustSprite;
        static AudioClip tickClip, clackClip, pingClip, hissClip, popClip, slideClip, beepClip, warnClip;

        RepairPropStationBridge owner;
        Canvas canvas;
        RectTransform root, widget, track, gauge, meter, probe, wire, card;
        Image ringBack, ring, orbit, trackBack, trackFill, gaugeZone, gaugeFill, needle, crust, target;
        Text chevrons, prompt, message, lcd, title;
        Font font;
        AudioSource speaker;

        PropCommand current;
        Func<bool> commit;
        RepairPropGestureLogic logic;
        Transform part;
        Vector3 basePosition, axisSelf, centerSelf;
        Quaternion baseRotation;
        float travel;
        bool moves, reservedBefore, reserving, blockedPress, wasHeld;
        int ticksPlayed;
        float flashUntil, lastScrub;
        readonly List<PropCommand> autoQueue = new List<PropCommand>();
        Func<PropCommand, bool> autoCommit;
        Action autoDone;
        PropCommand autoCurrent;
        float autoStarted;
        int autoTicks;

        public bool Busy => current != null || autoCurrent != null;
        public bool AutoRunning => autoCurrent != null;
        public PropCommand Current => current ?? autoCurrent;
        public PropFeel Feel { get; private set; }
        public RepairPropGestureLogic Logic => logic;
        /// <summary>Where the part sits on screen (pixels) and the canvas-to-screen scale, for scripted pointers.</summary>
        public Vector2 AnchorScreen { get; private set; }
        public float CanvasScale => canvas != null ? canvas.scaleFactor : 1f;
        public string PromptText => prompt != null ? prompt.text : "";
        public string MessageText => message != null ? message.text : "";
        public bool WidgetShowing => widget != null && widget.gameObject.activeSelf;

        public void Initialize(RepairPropStationBridge bridge)
        {
            owner = bridge;
            font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            var host = new GameObject("RepairPropGestureCanvas", typeof(RectTransform));
            host.transform.SetParent(transform, false);
            canvas = host.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 53;
            var scaler = host.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            root = (RectTransform)host.transform;
            speaker = gameObject.AddComponent<AudioSource>(); speaker.playOnAwake = false; speaker.spatialBlend = 0f; speaker.volume = .55f;
            BuildWidget();
            widget.gameObject.SetActive(false);
        }

        // ---------- driving ----------

        /// <summary>Starts the hand motion for a command; <paramref name="onCommit"/> runs the command when it finishes.
        /// A command without a motion commits at once. Returns false only when nothing could start.</summary>
        public bool Begin(PropCommand command, Func<bool> onCommit)
        {
            if (command == null || onCommit == null || AutoRunning) return false;
            if (current != null) Cancel();
            var feel = RepairPropFeel.For(command);
            if (feel == PropFeel.Tap) return onCommit();
            current = command; commit = onCommit; Feel = feel;
            Bind(command.target);
            var camera = owner.Station.Inspector.ViewCamera;
            Vector2 exit = Vector2.up;
            if (camera != null)
            {
                var anchor = AnchorWorld();
                Vector2 a = camera.WorldToScreenPoint(anchor), b = camera.WorldToScreenPoint(anchor + ExitWorld(camera) * .03f);
                if ((b - a).magnitude > 4f) exit = (b - a).normalized;
            }
            AnchorScreen = ToScreen(AnchorWorld());
            Vector2 local = Local(AnchorScreen);
            // The meter waits on whichever side of the part has room.
            var holster = new Vector2(local.x > 0 ? -170f : 170f, -40f);
            logic = new RepairPropGestureLogic(feel, exit, holster);
            ticksPlayed = 0; blockedPress = RepairPropFeelInput.LeftHeld; wasHeld = blockedPress;
            Reserve(true);
            Show(feel, command);
            return true;
        }

        /// <summary>Drops the motion in progress: the part goes back and the command does not run.</summary>
        public void Cancel()
        {
            Restore();
            current = null; commit = null; logic = null;
            if (autoCurrent != null) { autoCurrent = null; autoQueue.Clear(); autoDone?.Invoke(); autoDone = null; }
            Reserve(false);
            flashUntil = 0f;
            if (widget != null) widget.gameObject.SetActive(false);
        }

        /// <summary>The electric screwdriver: each screw spins for a moment and then commits, in order.</summary>
        public bool RunAuto(IList<PropCommand> members, Func<PropCommand, bool> onCommit, Action onDone)
        {
            if (members == null || members.Count == 0 || onCommit == null || AutoRunning) return false;
            if (current != null) Cancel();
            autoQueue.Clear(); autoQueue.AddRange(members);
            autoCommit = onCommit; autoDone = onDone;
            Reserve(true);
            NextAuto();
            return true;
        }

        void NextAuto()
        {
            Restore();
            if (autoQueue.Count == 0)
            {
                autoCurrent = null; Reserve(false);
                flashUntil = Time.unscaledTime + FlashSeconds;
                var done = autoDone; autoDone = null; done?.Invoke();
                return;
            }
            autoCurrent = autoQueue[0]; autoQueue.RemoveAt(0);
            Feel = RepairPropFeel.For(autoCurrent);
            Bind(autoCurrent.target);
            autoStarted = Time.unscaledTime; autoTicks = 0;
            Show(Feel, autoCurrent);
            prompt.text = "电动起子：" + autoCurrent.label + "（剩 " + (autoQueue.Count + 1) + " 颗）";
        }

        void Update()
        {
            if (owner == null || widget == null) return;
            float now = Time.unscaledTime;
            if (autoCurrent != null) { StepAuto(now); return; }
            if (current == null)
            {
                if (widget.gameObject.activeSelf)
                {
                    float u = 1f - (flashUntil - now) / FlashSeconds;
                    if (u >= 1f) widget.gameObject.SetActive(false);
                    else { widget.localScale = Vector3.one * (1f + .18f * u); SetAlpha(1f - u); }
                }
                return;
            }
            if (!owner.CanInteract || RepairPropFeelInput.CancelPressed) { Cancel(); return; }
            var camera = owner.Station.Inspector.ViewCamera;
            Restore();
            AnchorScreen = ToScreen(AnchorWorld());
            Vector2 anchor = Local(AnchorScreen);
            widget.anchoredPosition = anchor;
            Vector2 pointer = RepairPropFeelInput.ScreenPointer;
            bool held = RepairPropFeelInput.LeftHeld;
            // A press that lands on the panel or another window is not a hand on the part.
            if (held && !wasHeld && (RepairPropFeelInput.OverUi || owner.ContextPanel != null && owner.ContextPanel.ContainsScreenPoint(pointer))) blockedPress = true;
            if (!held) blockedPress = false;
            wasHeld = held;
            logic.Step(Local(pointer) - anchor, held && !blockedPress, Time.unscaledDeltaTime);
            Pose(logic.Progress, camera);
            Sound();
            Draw(now);
            if (logic.Done) Finish();
        }

        void StepAuto(float now)
        {
            if (!owner.CanInteract) { Cancel(); return; }
            Restore();
            AnchorScreen = ToScreen(AnchorWorld());
            widget.anchoredPosition = Local(AnchorScreen);
            float t = Mathf.Clamp01((now - autoStarted) / AutoSeconds);
            Pose(t, owner.Station.Inspector.ViewCamera);
            ring.fillAmount = t;
            ring.color = Amber;
            OrbitDot(now);
            int ticks = Mathf.FloorToInt(t * 6f);
            if (ticks > autoTicks) { autoTicks = ticks; Play(tickClip, .9f + t * .4f, .7f); }
            if (t < 1f) return;
            Restore();
            var command = autoCurrent;
            Play(pingClip, Feel == PropFeel.Unscrew ? 1.1f : .8f, .6f);
            autoCommit(command);
            if (autoCurrent == command) NextAuto();
        }

        void Finish()
        {
            Restore();
            var run = commit; var feel = Feel;
            current = null; commit = null;
            Reserve(false);
            flashUntil = Time.unscaledTime + FlashSeconds;
            Tint(Ok);
            switch (feel)
            {
                case PropFeel.Unscrew: Play(pingClip, 1.1f, .7f); break;
                case PropFeel.Screw: Play(clackClip, .8f, .8f); break;
                case PropFeel.Pry: Play(popClip, 1f, .9f); break;
                case PropFeel.Seat: case PropFeel.Plug: case PropFeel.Crimp: Play(clackClip, 1f, .9f); break;
                case PropFeel.Unplug: Play(popClip, 1.3f, .7f); break;
                case PropFeel.SlideOut: case PropFeel.SlideIn: Play(slideClip, 1f, .7f); break;
                case PropFeel.Scrub: Play(pingClip, 1.4f, .5f); break;
            }
            run();
        }

        // ---------- the part ----------

        void Bind(string objectName)
        {
            var flow = owner.Flow;
            part = flow.GetObject(objectName);
            var node = flow.Definition.FindNode(objectName);
            centerSelf = Vector3.zero;
            moves = false;
            if (part == null) return;
            int count = 0; var sum = Vector3.zero;
            foreach (var filter in part.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                sum += part.InverseTransformPoint(filter.transform.TransformPoint(filter.sharedMesh.bounds.center)); count++;
            }
            if (count > 0) centerSelf = sum / count;
            // Retained originals never move (the flow checks their pose); hidden parts have nothing to show.
            moves = node != null && !node.immutable && part.gameObject.activeInHierarchy && part.parent != null;
            basePosition = part.localPosition; baseRotation = part.localRotation;
            var camera = owner.Station.Inspector.ViewCamera;
            var exit = camera != null ? ExitWorld(camera) : part.forward;
            axisSelf = Quaternion.Inverse(part.rotation) * exit;
            travel = node != null ? Mathf.Max(.01f, flow.GetObject(flow.Definition.rootObjectName).TransformVector(node.parkingOffset).magnitude) : .01f;
        }

        Vector3 ExitWorld(Camera camera)
        {
            var flow = owner.Flow;
            var node = Current != null ? flow.Definition.FindNode(Current.target) : null;
            var rootObject = flow.GetObject(flow.Definition.rootObjectName);
            if (node != null && rootObject != null && node.parkingOffset.sqrMagnitude > 1e-10f) return rootObject.TransformVector(node.parkingOffset).normalized;
            return -camera.transform.forward;
        }

        Vector3 AnchorWorld() => part != null ? part.TransformPoint(centerSelf) : owner.transform.position;

        // How the part follows the hand: screws spin and back out, a cover strains then lifts, a plug and a board come
        // part of the way out. Pushes and fixes leave it where it is.
        void Pose(float t, Camera camera)
        {
            if (!moves || part == null) return;
            float angle = 0f, shift = 0f;
            switch (Feel)
            {
                case PropFeel.Unscrew: angle = 540f * t; shift = .004f * t; break;
                case PropFeel.Screw: angle = -540f * t; break;
                case PropFeel.Pry: shift = .006f * t; break;
                case PropFeel.Unplug: shift = .008f * t; break;
                case PropFeel.SlideOut: shift = travel * .5f * t; break;
                default: return;
            }
            float scale = Mathf.Max(1e-6f, part.parent.lossyScale.x);
            part.localPosition = basePosition + baseRotation * axisSelf * (shift / scale);
            part.localRotation = baseRotation * Quaternion.AngleAxis(angle, axisSelf);
            // A cover under the pry bar trembles harder the closer it is to letting go.
            if (Feel == PropFeel.Pry && t > .05f)
                part.localPosition += baseRotation * new Vector3(Mathf.Sin(Time.unscaledTime * 90f), Mathf.Cos(Time.unscaledTime * 77f), 0f) * (.0007f * t / scale);
        }

        void Restore()
        {
            if (!moves || part == null) return;
            part.localPosition = basePosition; part.localRotation = baseRotation;
        }

        void Reserve(bool on)
        {
            var inspector = owner != null && owner.Station != null ? owner.Station.Inspector : null;
            if (inspector == null) return;
            if (on && !reserving) { reservedBefore = inspector.LeftPointerReserved; inspector.LeftPointerReserved = true; reserving = true; }
            else if (!on && reserving) { inspector.LeftPointerReserved = reservedBefore; reserving = false; }
        }

        Vector2 ToScreen(Vector3 world)
        {
            var camera = owner.Station.Inspector.ViewCamera;
            return camera != null ? (Vector2)camera.WorldToScreenPoint(world) : Vector2.zero;
        }

        Vector2 Local(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var local);
            return local;
        }

        // ---------- sound ----------

        void Sound()
        {
            if (logic.Ticks <= ticksPlayed) return;
            ticksPlayed = logic.Ticks;
            switch (Feel)
            {
                case PropFeel.Unscrew: case PropFeel.Screw: Play(tickClip, .9f + logic.Progress * .5f, .6f); break;
                case PropFeel.Unplug: if (!logic.Done) Play(clackClip, 1.4f, .6f); break;
                case PropFeel.Scrub:
                    if (Time.unscaledTime - lastScrub > .06f) { lastScrub = Time.unscaledTime; Play(hissClip, UnityEngine.Random.Range(.85f, 1.2f), .45f); }
                    break;
                case PropFeel.Probe:
                    var probeData = owner.Flow.Definition.FindProbe(current.probeId);
                    Play(probeData != null && owner.Flow.ProbeReadsAbnormal(probeData) ? warnClip : beepClip, 1f, .6f);
                    break;
            }
        }

        void Play(AudioClip clip, float pitch, float volume)
        {
            if (clip == null || speaker == null) return;
            speaker.pitch = pitch;
            speaker.PlayOneShot(clip, volume);
        }

        // ---------- widget ----------

        void Show(PropFeel feel, PropCommand command)
        {
            widget.gameObject.SetActive(true);
            widget.localScale = Vector3.one;
            SetAlpha(1f);
            Tint(Amber);
            bool turn = feel == PropFeel.Unscrew || feel == PropFeel.Screw;
            bool drag = RepairPropGestureLogic.Distance(feel) > 0f;
            ringBack.gameObject.SetActive(turn || feel == PropFeel.Unplug || feel == PropFeel.Scrub);
            ring.gameObject.SetActive(turn || feel == PropFeel.Unplug);
            orbit.gameObject.SetActive(turn);
            track.gameObject.SetActive(drag);
            gauge.gameObject.SetActive(feel == PropFeel.Crimp);
            crust.gameObject.SetActive(feel == PropFeel.Scrub);
            meter.gameObject.SetActive(feel == PropFeel.Probe);
            probe.gameObject.SetActive(feel == PropFeel.Probe);
            wire.gameObject.SetActive(feel == PropFeel.Probe);
            target.gameObject.SetActive(feel == PropFeel.Probe);
            float ringSize = feel == PropFeel.Scrub ? RepairPropGestureLogic.ScrubRadius * 2f : feel == PropFeel.Unplug ? 54f : 84f;
            ringBack.rectTransform.sizeDelta = ring.rectTransform.sizeDelta = Vector2.one * ringSize;
            ring.fillClockwise = feel == PropFeel.Screw || feel == PropFeel.Unplug;
            if (drag && logic != null)
            {
                float length = RepairPropGestureLogic.Distance(feel);
                track.sizeDelta = new Vector2(length, 10f);
                track.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(logic.direction.y, logic.direction.x) * Mathf.Rad2Deg);
            }
            if (feel == PropFeel.Probe && logic != null)
                meter.anchoredPosition = logic.Holster + new Vector2(0f, 64f);
            title.text = command.label;
            prompt.text = RepairPropFeel.Prompt(feel);
            message.text = "";
        }

        void Draw(float now)
        {
            float t = logic.Progress;
            switch (Feel)
            {
                case PropFeel.Unscrew: case PropFeel.Screw:
                    ring.fillAmount = t; OrbitDot(now); break;
                case PropFeel.Unplug:
                    ring.fillAmount = logic.Latched ? 1f : logic.Aux;
                    ring.color = logic.Latched ? Ok : Amber;
                    trackFill.fillAmount = t;
                    break;
                case PropFeel.Scrub:
                    var c = crust.color; c.a = .9f * (1f - t); crust.color = c;
                    break;
                case PropFeel.Crimp:
                    gaugeFill.fillAmount = logic.Aux;
                    bool inZone = logic.Aux >= RepairPropGestureLogic.CrimpLow && logic.Aux <= RepairPropGestureLogic.CrimpHigh;
                    gaugeFill.color = logic.Aux >= 1f ? Bad : inZone ? Ok : Amber;
                    ((RectTransform)needle.transform).anchoredPosition = new Vector2(0f, (logic.Aux - .5f) * 100f);
                    break;
                case PropFeel.Probe:
                    probe.anchoredPosition = logic.ProbeTip;
                    Line(wire, meter.anchoredPosition + new Vector2(0f, -22f), logic.ProbeTip);
                    target.color = logic.Contact ? Ok : Color.Lerp(Dim, Hot, .5f + .5f * Mathf.Sin(now * 6f));
                    lcd.text = Reading(t, now);
                    break;
                default:
                    trackFill.fillAmount = t;
                    break;
            }
            if (RepairPropGestureLogic.Distance(Feel) > 0f)
                chevrons.color = Color.Lerp(Dim, Hot, .5f + .5f * Mathf.Sin(now * 8f));
            message.text = logic.Message ?? "";
        }

        // The display scrambles while the reading settles, then holds it; "a|b" flickers (a loose contact).
        string Reading(float t, float now)
        {
            if (!logic.Contact) return "-- . --";
            var probeData = owner.Flow.Definition.FindProbe(current.probeId);
            if (probeData == null) return "----";
            if (t < .6f) return (UnityEngine.Random.Range(0, 99) + UnityEngine.Random.value).ToString("0.0") + " Ω";
            string shown = owner.Flow.ProbeReadsAbnormal(probeData) ? probeData.abnormalDisplay : probeData.normalDisplay;
            if (string.IsNullOrEmpty(shown)) return "----";
            var options = shown.Split('|');
            return options[Mathf.FloorToInt(now * 4f) % options.Length];
        }

        void OrbitDot(float now)
        {
            // A dot circling the ring the way the hand should go.
            float sign = Feel == PropFeel.Screw ? -1f : 1f;
            float a = sign * now * 300f * Mathf.Deg2Rad + Mathf.PI * .5f;
            float r = ring.rectTransform.sizeDelta.x * .5f;
            orbit.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        void Tint(Color color)
        {
            ring.color = color; trackFill.color = color; orbit.color = color; target.color = color;
        }

        void SetAlpha(float alpha)
        {
            var group = widget.GetComponent<CanvasGroup>();
            group.alpha = alpha;
        }

        static void Line(RectTransform line, Vector2 from, Vector2 to)
        {
            var d = to - from;
            line.anchoredPosition = from;
            line.sizeDelta = new Vector2(d.magnitude, 2f);
            line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        void BuildWidget()
        {
            widget = Rect("Gesture", root);
            widget.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            ringBack = Img("RingBack", widget, Ring, new Color(1f, .62f, .16f, .25f), Vector2.one * 84f);
            ring = Img("Ring", widget, Ring, Amber, Vector2.one * 84f);
            ring.type = Image.Type.Filled; ring.fillMethod = Image.FillMethod.Radial360; ring.fillOrigin = (int)Image.Origin360.Top;
            orbit = Img("Orbit", widget, Dot, Amber, Vector2.one * 9f);

            track = Rect("Track", widget); track.pivot = new Vector2(0f, .5f);
            trackBack = Img("Back", track, RepairPropCyberStyle.Frame, Dim, Vector2.zero); trackBack.type = Image.Type.Sliced; Stretch(trackBack.rectTransform);
            trackFill = Img("Fill", track, RepairPropCyberStyle.Fill, Amber, Vector2.zero); Stretch(trackFill.rectTransform, 2f);
            trackFill.type = Image.Type.Filled; trackFill.fillMethod = Image.FillMethod.Horizontal;
            chevrons = Txt("Chevrons", track, ">>", 16, Hot, TextAnchor.MiddleLeft);
            var chevronRect = chevrons.rectTransform; chevronRect.anchorMin = chevronRect.anchorMax = new Vector2(1f, .5f);
            chevronRect.pivot = new Vector2(0f, .5f); chevronRect.sizeDelta = new Vector2(34f, 20f); chevronRect.anchoredPosition = new Vector2(4f, 0f);
            var grip = Img("Grip", track, RepairPropCyberStyle.Frame, Hot, Vector2.one * 16f); grip.type = Image.Type.Sliced;
            var gripRect = grip.rectTransform; gripRect.anchorMin = gripRect.anchorMax = new Vector2(0f, .5f);

            gauge = Rect("Gauge", widget); gauge.sizeDelta = new Vector2(18f, 100f); gauge.anchoredPosition = new Vector2(70f, 0f);
            var gaugeBack = Img("Back", gauge, RepairPropCyberStyle.Frame, Dim, Vector2.zero); gaugeBack.type = Image.Type.Sliced; Stretch(gaugeBack.rectTransform);
            gaugeZone = Img("Zone", gauge, RepairPropCyberStyle.Fill, new Color(.84f, .93f, .52f, .28f), Vector2.zero);
            var zone = gaugeZone.rectTransform; zone.anchorMin = new Vector2(0f, RepairPropGestureLogic.CrimpLow); zone.anchorMax = new Vector2(1f, RepairPropGestureLogic.CrimpHigh);
            zone.offsetMin = zone.offsetMax = Vector2.zero;
            gaugeFill = Img("Fill", gauge, RepairPropCyberStyle.Fill, Amber, Vector2.zero); Stretch(gaugeFill.rectTransform, 3f);
            gaugeFill.type = Image.Type.Filled; gaugeFill.fillMethod = Image.FillMethod.Vertical;
            needle = Img("Needle", gauge, RepairPropCyberStyle.Fill, Hot, new Vector2(30f, 2f));
            var gaugeLabel = Txt("Label", gauge, "压力", 11, Dim, TextAnchor.UpperCenter);
            gaugeLabel.rectTransform.anchoredPosition = new Vector2(0f, -62f); gaugeLabel.rectTransform.sizeDelta = new Vector2(60f, 16f);

            crust = Img("Crust", widget, Crust, new Color(.95f, .97f, 1f, .9f), Vector2.one * (RepairPropGestureLogic.ScrubRadius * 1.4f));

            target = Img("Target", widget, Ring, Hot, Vector2.one * (RepairPropGestureLogic.ProbeSnap * 1.2f));
            wire = Rect("Wire", widget); wire.pivot = new Vector2(0f, .5f);
            var wireImage = wire.gameObject.AddComponent<Image>(); wireImage.sprite = RepairPropCyberStyle.Fill; wireImage.color = new Color(1f, .30f, .22f, .75f); wireImage.raycastTarget = false;
            meter = Rect("Meter", widget); meter.sizeDelta = new Vector2(120f, 44f);
            RepairPropCyberStyle.Framed(meter.gameObject.AddComponent<Image>(), RepairPropCyberStyle.FieldDeep, Amber).raycastTarget = false;
            lcd = Txt("Lcd", meter, "", 18, Ok, TextAnchor.MiddleCenter);
            if (RepairPropCyberStyle.Mono != null) lcd.font = RepairPropCyberStyle.Mono;
            Stretch(lcd.rectTransform, 4f);
            var meterTag = Txt("Tag", meter, "万用表 · Ω", 10, Dim, TextAnchor.LowerCenter);
            meterTag.rectTransform.anchoredPosition = new Vector2(0f, 30f); meterTag.rectTransform.sizeDelta = new Vector2(120f, 14f);
            probe = Rect("Probe", widget); probe.sizeDelta = new Vector2(14f, 14f);
            Img("Tip", probe, Dot, Bad, Vector2.one * 12f);
            var probeTag = Txt("Tag", probe, "红笔", 10, Bad, TextAnchor.MiddleLeft);
            probeTag.rectTransform.anchoredPosition = new Vector2(26f, 0f); probeTag.rectTransform.sizeDelta = new Vector2(40f, 14f);

            card = Rect("Card", widget); card.pivot = new Vector2(.5f, 1f); card.anchoredPosition = new Vector2(0f, -92f); card.sizeDelta = new Vector2(340f, 74f);
            RepairPropCyberStyle.Framed(card.gameObject.AddComponent<Image>(), new Color(0f, 0f, 0f, .70f), Dim).raycastTarget = false;
            title = Txt("Title", card, "", 13, Amber, TextAnchor.UpperLeft); title.fontStyle = FontStyle.Bold;
            Place(title.rectTransform, 8f, -5f, 18f);
            prompt = Txt("Prompt", card, "", 12, RepairPropCyberStyle.Ink, TextAnchor.UpperLeft);
            Place(prompt.rectTransform, 8f, -24f, 30f);
            message = Txt("Message", card, "", 12, Warn, TextAnchor.UpperLeft);
            Place(message.rectTransform, 8f, -54f, 16f);
            var cancel = Txt("Cancel", card, "Esc 取消", 10, Dim, TextAnchor.UpperRight);
            Place(cancel.rectTransform, 8f, -5f, 14f);
        }

        static void Place(RectTransform rect, float inset, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(1f, 1f); rect.pivot = new Vector2(.5f, 1f);
            rect.offsetMin = new Vector2(inset, top - height); rect.offsetMax = new Vector2(-inset, top);
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        static Image Img(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
        {
            var image = Rect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite; image.color = color; image.raycastTarget = false;
            image.rectTransform.sizeDelta = size;
            return image;
        }

        Text Txt(string name, Transform parent, string value, int size, Color color, TextAnchor anchor)
        {
            var text = Rect(name, parent).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.color = color; text.alignment = anchor; text.text = value;
            text.raycastTarget = false; text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset; rect.offsetMax = -Vector2.one * inset;
        }

        // ---------- generated art and sound ----------

        static Sprite Ring => ringSprite != null ? ringSprite : ringSprite = Disc(96, 40f, 47f);
        static Sprite Dot => dotSprite != null ? dotSprite : dotSprite = Disc(16, 0f, 7.5f);

        static Sprite Disc(int size, float inner, float outer)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float c = (size - 1) * .5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                    float a = Mathf.Clamp01(outer - r + .5f) * Mathf.Clamp01(r - inner + .5f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
        }

        // Salt crust: clustered white specks, densest in the middle.
        static Sprite Crust
        {
            get
            {
                if (crustSprite != null) return crustSprite;
                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var rng = new System.Random(7);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float r = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
                        float density = Mathf.Clamp01(1.1f - r * 1.2f) * (.55f + .45f * Mathf.PerlinNoise(x * .21f, y * .21f));
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, rng.NextDouble() < density * .75f ? .6f + (float)rng.NextDouble() * .4f : 0f));
                    }
                texture.Apply();
                return crustSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
            }
        }

        void Awake()
        {
            if (tickClip != null) return;
            const int rate = RepairSoundSynth.Rate;
            tickClip = Clip("tick", .03f, (t, n) => Mathf.Exp(-t * 900f) * (Mathf.Sin(t * 2f * Mathf.PI * 3400f) * .6f + n * .5f));
            clackClip = Clip("clack", .07f, (t, n) => Mathf.Exp(-t * 260f) * (Mathf.Sin(t * 2f * Mathf.PI * 1250f) * .7f + n * .45f));
            pingClip = Clip("ping", .25f, (t, n) => Mathf.Exp(-t * 18f) * (Mathf.Sin(t * 2f * Mathf.PI * 2600f) * .5f + Mathf.Sin(t * 2f * Mathf.PI * 4150f) * .3f));
            hissClip = Clip("hiss", .07f, (t, n) => Mathf.Sin(Mathf.PI * t / .07f) * n * .7f);
            beepClip = Clip("beep", .14f, (t, n) => Mathf.Clamp01(t * 200f) * Mathf.Clamp01((.14f - t) * 60f) * Mathf.Sign(Mathf.Sin(t * 2f * Mathf.PI * 2000f)) * .35f);
            popClip = RepairSoundSynth.Create(RepairSoundSynth.Cue.OpenCover);
            slideClip = RepairSoundSynth.Create(RepairSoundSynth.Cue.Replace);
            warnClip = RepairSoundSynth.Create(RepairSoundSynth.Cue.ProbeWarning);

            AudioClip Clip(string name, float seconds, Func<float, float, float> wave)
            {
                var rng = new System.Random(name.GetHashCode());
                var data = new float[Mathf.CeilToInt(seconds * rate)];
                for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(wave(i / (float)rate, (float)(rng.NextDouble() * 2 - 1)), -1f, 1f);
                var clip = AudioClip.Create("feel_" + name, data.Length, 1, rate, false);
                clip.SetData(data, 0);
                return clip;
            }
        }

        void OnDestroy()
        {
            Restore();
            Reserve(false);
        }
    }
}
