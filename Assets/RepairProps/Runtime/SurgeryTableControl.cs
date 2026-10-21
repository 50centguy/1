using UnityEngine;

namespace BorderRepair.RepairProps
{
    public enum SurgeryControlKind { Pendant, Strap, Tray, Arms, Lamp }

    /// <summary>A clickable part of the V2 surgery table (aimed at with the room crosshair, within reach).</summary>
    [DisallowMultipleComponent]
    public sealed class SurgeryTableControl : MonoBehaviour
    {
        [SerializeField] SurgeryControlKind kind;
        [SerializeField] string label;
        public SurgeryControlKind Kind => kind;
        public string Label => label;
        public void Configure(SurgeryControlKind kind, string label) { this.kind = kind; this.label = label; }

        public string Hint
        {
            get
            {
                var table = SurgeryTableV2Runtime.Current;
                switch (kind)
                {
                    case SurgeryControlKind.Pendant: return label + " · 左键：" + (table != null && table.ChairPose ? "放平" : "升起背板");
                    case SurgeryControlKind.Arms: return label + " · 左键：" + (SurgeryGantryV2Runtime.Current != null && SurgeryGantryV2Runtime.Current.ArmsDeployed ? "收回机械臂" : "展开机械臂");
                    case SurgeryControlKind.Lamp: return label + " · 左键：" + (SurgeryGantryV2Runtime.Current != null && SurgeryGantryV2Runtime.Current.LampOn ? "关灯" : "开灯");
                    case SurgeryControlKind.Strap: return label + " · 左键：" + (table != null && table.StrapClosed(this) ? "解开" : "扣上");
                    default: return label + " · 左键：" + (table != null && table.TrayOut ? "收回" : "摆出");
                }
            }
        }
    }
}
