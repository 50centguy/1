using UnityEngine;

namespace WorkbenchArea
{
    /// <summary>
    /// 可检查对象的占位碰撞区域（挂在子物体 InspectZone 上，触发器 BoxCollider）。
    /// 本阶段只用于镜头点选与空间检查，不包含正式维修逻辑。
    /// </summary>
    public class WbInspectable : MonoBehaviour
    {
        public Transform target;      // 对应的模型对象
        public string role;           // 来自 wb_role
        public string displayName;
    }
}
