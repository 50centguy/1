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
        [SerializeField] float travel = .3f;
        [SerializeField] float angle = -100f;

        Vector3 restPosition, slide, hinge;
        Quaternion restRotation;
        float openAmount, lift;
        bool open, initialized;
        Light lampLight;

        public RepairBenchPropKind Kind => kind;
        public string Label => label;
        /// <summary>Tool this prop selects; null for the loupe (empty-handed inspection).</summary>
        public RepairActionType? Tool => kind == RepairBenchPropKind.Tool && hasTool ? tool : (RepairActionType?)null;
        public bool Open => open;
        public bool LampOn => lampLight != null && lampLight.enabled;
        public bool Lifted => lift > .5f;

        public void Configure(RepairBenchPropKind kind, string label, RepairActionType? tool = null, float travel = .3f, float angle = -100f)
        {
            this.kind = kind; this.label = label; hasTool = tool.HasValue; this.tool = tool ?? default; this.travel = travel; this.angle = angle;
        }

        /// <summary>Records the rest pose; `slideDirection` is world-space toward the player, `hingePoint` the door's hinge.</summary>
        public void Initialize(Vector3 slideDirection, Light light = null)
        {
            restPosition = transform.position; restRotation = transform.rotation;
            slide = slideDirection.normalized; hinge = transform.position; lampLight = light;
            initialized = true;
        }

        public string Hint
        {
            get
            {
                switch (kind)
                {
                    case RepairBenchPropKind.Tool: return label + " · 点击取用";
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
                    transform.SetPositionAndRotation(restPosition + Vector3.up * (.07f * e) + slide * (.03f * e),
                        Quaternion.AngleAxis(-18f * e, Vector3.Cross(Vector3.up, slide)) * restRotation);
                    break;
            }
        }
    }
}
