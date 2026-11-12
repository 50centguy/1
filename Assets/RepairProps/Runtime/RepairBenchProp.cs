using BorderRepair.Data;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    public enum RepairBenchPropKind { Tool, Lamp, Drawer, Door }

    // One clickable thing on the V2 repair bench. Tools hand their repair tool to the station through the bridge's
    // guarded tool selection (the station stays the only authority on the active tool); the lamp, drawers and cabinet
    // door only animate themselves. Motion is set up by RepairBenchV2Runtime from the bench's own geometry.
    [DisallowMultipleComponent]
    public sealed class RepairBenchProp : MonoBehaviour
    {
        [SerializeField] RepairBenchPropKind kind;
        [SerializeField] string label;
        [SerializeField] bool hasTool;
        [SerializeField] RepairActionType tool;
        [SerializeField] bool cleaner;
        [SerializeField] float travel = .3f;
        [SerializeField] float angle = -100f;

        Vector3 restPosition, slide, hinge;
        Quaternion restRotation;
        float openAmount, lift;
        bool open, initialized;
        Light lampLight;
        // In hand: the tool leaves the tray and follows the pointer, its tip on the spot it would work on.
        bool holding, posed, hasTip;
        Vector3 holdTip, holdShaft, tipLocal, centerLocal, posNow;
        Quaternion rotNow;
        Collider[] colliders;

        public RepairBenchPropKind Kind => kind;
        public string Label => label;
        /// <summary>Tool this prop selects; null for the loupe (empty-handed inspection).</summary>
        public RepairActionType? Tool => kind == RepairBenchPropKind.Tool && hasTool ? tool : (RepairActionType?)null;
        /// <summary>The cleaning tool (blower brush): taking it starts cleaning, putting it down ends it.</summary>
        public bool IsCleaner => kind == RepairBenchPropKind.Tool && cleaner;
        /// <summary>The loupe: empty-handed inspection, neither a repair tool nor the cleaner.</summary>
        public bool IsLoupe => kind == RepairBenchPropKind.Tool && !hasTool && !cleaner;
        public bool Open => open;
        public bool LampOn => lampLight != null && lampLight.enabled;
        public bool Lifted => lift > .5f;
        /// <summary>True while the tool is out of the tray, following the pointer.</summary>
        public bool InHand => holding;
        /// <summary>Has a working end (screwdriver, opening tool); the tester is held by its body, beside the spot.</summary>
        public bool HasTip => hasTip;
        /// <summary>Where the working end of the tool is now (world); the tool's centre when it has no clear tip.</summary>
        public Vector3 TipPosition => transform.TransformPoint(hasTip ? tipLocal : centerLocal);

        /// <summary>Takes the tool in hand: its tip goes to <paramref name="tip"/>, the shaft leaning back along
        /// <paramref name="shaft"/> (world, from the tip toward the handle). The tool's colliders are off while held, so the
        /// pointer sees through it to the part.</summary>
        public void Hold(Vector3 tip, Vector3 shaft)
        {
            holdTip = tip; holdShaft = shaft.sqrMagnitude > 1e-6f ? shaft.normalized : Vector3.up;
            if (holding) return;
            holding = true; SetColliders(false);
        }

        /// <summary>Back to the tray (lifted while it is still the selected tool).</summary>
        public void PutBack()
        {
            if (!holding) return;
            holding = false; SetColliders(true);
        }

        void SetColliders(bool on)
        {
            if (colliders == null) colliders = GetComponentsInChildren<Collider>(true);
            foreach (var c in colliders) if (c != null) c.enabled = on;
        }

        // The working end of a long, thin tool (screwdriver, opening tool): the narrower end of its long axis. The tester,
        // about as wide as it is long, has no tip and is held by its body.
        void FindTip()
        {
            var filter = GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;
            var mesh = filter.sharedMesh;
            var b = mesh.bounds;
            centerLocal = b.center;
            // The model says where its working end is: an empty child named <tool>_Tip.
            var marker = transform.Find(name + "_Tip");
            if (marker != null) { tipLocal = marker.localPosition; hasTip = true; return; }
            if (!mesh.isReadable) return;
            int axis = b.extents.x >= b.extents.y && b.extents.x >= b.extents.z ? 0 : b.extents.y >= b.extents.z ? 1 : 2;
            float length = b.size[axis];
            float width = Mathf.Max(b.size[(axis + 1) % 3], b.size[(axis + 2) % 3]);
            if (width <= 0 || length / width < 3f) return;
            float band = length * .03f, low = b.min[axis] + band, high = b.max[axis] - band;
            float rLow = 0, rHigh = 0;
            foreach (var v in mesh.vertices)
            {
                var off = v - b.center; off[axis] = 0;
                float r = off.magnitude;
                if (v[axis] <= low) rLow = Mathf.Max(rLow, r);
                else if (v[axis] >= high) rHigh = Mathf.Max(rHigh, r);
            }
            if (Mathf.Min(rLow, rHigh) > Mathf.Max(rLow, rHigh) * .85f) return;
            tipLocal = b.center;
            tipLocal[axis] = rHigh < rLow ? b.max[axis] : b.min[axis];
            hasTip = true;
        }

        public void Configure(RepairBenchPropKind kind, string label, RepairActionType? tool = null, float travel = .3f, float angle = -100f, bool cleaner = false)
        {
            this.cleaner = cleaner;
            this.kind = kind; this.label = label; hasTool = tool.HasValue; this.tool = tool ?? default; this.travel = travel; this.angle = angle;
        }

        /// <summary>Records the rest pose; `slideDirection` is world-space toward the player, `hingePoint` the door's hinge.</summary>
        public void Initialize(Vector3 slideDirection, Light light = null)
        {
            restPosition = transform.position; restRotation = transform.rotation;
            slide = slideDirection.normalized; hinge = transform.position; lampLight = light;
            posNow = restPosition; rotNow = restRotation;
            if (kind == RepairBenchPropKind.Tool) FindTip();
            initialized = true;
        }

        public string Hint
        {
            get
            {
                switch (kind)
                {
                    case RepairBenchPropKind.Tool: return label + (cleaner ? " · 点击拿起，按住左键擦拭" : hasTool ? " · 点击拿起" : " · 点击换成空手检查");
                    case RepairBenchPropKind.Lamp: return "台灯 · 点击" + (LampOn ? "关灯" : "开灯");
                    case RepairBenchPropKind.Drawer: return label + " · 点击" + (open ? "关上" : "拉开");
                    default: return label + " · 点击" + (open ? "关上" : "打开");
                }
            }
        }

        public void Activate(RepairPropStationBridge bridge)
        {
            switch (kind)
            {
                case RepairBenchPropKind.Tool:
                    if (bridge == null) return;
                    if (cleaner)
                    {
                        var cleaning = FindFirstObjectByType<BorderRepair.TwoNight.ClinicCleaningController>();
                        if (cleaning == null) return;
                        bool wanted = !cleaning.Cleaning;
                        if (!cleaning.SetCleaning(wanted) && wanted && bridge.Station != null && bridge.Station.View != null)
                            bridge.Station.View.ShowFeedback(cleaning.UnavailableReason, BorderRepair.Core.FeedbackKind.Warning);
                        return;
                    }
                    bridge.SelectToolFromPanel(Tool);
                    break;
                case RepairBenchPropKind.Lamp:
                    if (lampLight != null) lampLight.enabled = !lampLight.enabled;
                    break;
                default:
                    open = !open;
                    break;
            }
        }

        /// <summary>Tool rack: the selected tool rises out of its foam slot and tilts toward the player.</summary>
        public void SetSelected(bool selected) => lift = Mathf.MoveTowards(lift, selected ? 1 : 0, Time.unscaledDeltaTime * 5f);

        // Tip on the spot, shaft leaning back toward the hand; a tool without a tip floats just off the surface, face up.
        void HeldPose(out Vector3 position, out Quaternion rotation)
        {
            var scale = transform.lossyScale;
            if (hasTip)
            {
                var tipDirection = restRotation * Vector3.Scale(scale, tipLocal - centerLocal).normalized;
                rotation = Quaternion.FromToRotation(tipDirection, -holdShaft) * restRotation;
                position = holdTip - rotation * Vector3.Scale(scale, tipLocal);
            }
            else
            {
                rotation = Quaternion.FromToRotation(Vector3.up, holdShaft) * restRotation;   // it lay face up in the tray
                position = holdTip + holdShaft * .04f - rotation * Vector3.Scale(scale, centerLocal);
            }
        }

        void LateUpdate()
        {
            if (!initialized) return;
            float t = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            switch (kind)
            {
                case RepairBenchPropKind.Drawer:
                    openAmount = Mathf.Lerp(openAmount, open ? 1 : 0, t);
                    transform.position = restPosition + slide * (travel * openAmount);
                    break;
                case RepairBenchPropKind.Door:
                    openAmount = Mathf.Lerp(openAmount, open ? 1 : 0, t);
                    transform.SetPositionAndRotation(hinge, Quaternion.AngleAxis(angle * openAmount, Vector3.up) * restRotation);
                    break;
                case RepairBenchPropKind.Tool:
                    float e = Mathf.SmoothStep(0, 1, lift);
                    var position = restPosition + Vector3.up * (.07f * e) + slide * (.03f * e);
                    var rotation = Quaternion.AngleAxis(-18f * e, Vector3.Cross(Vector3.up, slide)) * restRotation;
                    if (holding) HeldPose(out position, out rotation);
                    float k = 1f - Mathf.Exp(-(holding ? 22f : 14f) * Time.unscaledDeltaTime);
                    if (!posed) { posNow = transform.position; rotNow = transform.rotation; posed = true; }
                    posNow = Vector3.Lerp(posNow, position, k); rotNow = Quaternion.Slerp(rotNow, rotation, k);
                    transform.SetPositionAndRotation(posNow, rotNow);
                    break;
            }
        }
    }
}
