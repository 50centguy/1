using UnityEngine;

namespace BorderRepair.Inspection
{
    public enum ToolKind { Screwdriver, Pry, Probe, Plug }

    /// <summary>
    /// 零件上的工具作用点（挂在 InspectionPoint 下的子物体，随零件一起移动、旋转、缩放）。约定：
    /// - position = 工具作用端（刀头 / 撬片刃口 / 探针尖 / 插片底）接触零件的点；
    /// - up = 从接触点指向工具柄的方向（工具沿 -up 方向压入）；
    /// - forward = 握工具的手套手臂大致伸出的方向（在物品自身坐标里选定，避开关键线索，与相机无关）。
    /// 桌面版的工具动画按这个坐标系播放预设动作；以后接 VR 时，手柄驱动的工具只需在接触这个点时调用同一个 RepairSession.PerformAction。
    /// </summary>
    public class ToolAnchor : MonoBehaviour
    {
        [SerializeField] ToolKind kind;
        [Tooltip("撬片施力时，工具轴线（up）倒向的方向（本地坐标）；其他工具不用")]
        [SerializeField] Vector3 leverTowardLocal = Vector3.right;

        public ToolKind Kind => kind;
        public Vector3 ContactPoint => transform.position;
        public Vector3 OutAxis => transform.up;
        public Vector3 ArmDirection => transform.forward;
        public Vector3 LeverToward => transform.TransformDirection(leverTowardLocal).normalized;
        /// <summary>物品的统一缩放（物品被检查台缩放到统一大小；工具也按同样比例显示）。</summary>
        public float WorldScale => transform.lossyScale.x;

        /// <summary>供编辑器生成工具配置。</summary>
        public void Configure(ToolKind k, Vector3 leverLocal)
        {
            kind = k;
            leverTowardLocal = leverLocal;
        }
    }
}
