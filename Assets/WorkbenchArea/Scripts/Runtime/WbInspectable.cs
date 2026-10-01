using UnityEngine;

namespace WorkbenchArea
{
    /// <summary>
    /// 可检查对象的占位碰撞区域（挂在子物体 InspectZone 上，触发器 BoxCollider；有实体碰撞的对象直接用实体表面，不带触发盒）。
    /// 本阶段只用于镜头点选与空间检查，不包含正式维修逻辑。
    /// 被别的部件挡住、要先移开它才能取用的对象（工具箱下层）：仍可悬停，HUD 明确提示“先移开上层才能取用”，点击不执行任何动作。
    /// </summary>
    public class WbInspectable : MonoBehaviour
    {
        public Transform target;      // 对应的模型对象
        public string role;           // 来自 wb_role
        public string displayName;
        [Tooltip("挡住它的部件；为空表示可直接取用")]
        public WbInspectable blockedBy;
        [Tooltip("悬停时显示的说明")]
        public string hint;
        [Tooltip("被挡住时要先做什么，例如“先移开上层才能取用”")]
        public string blockedAction;

        /// <summary>挡住它的部件还在原位时为 true。由占位交互在移开 / 放回挡板时切换。</summary>
        public bool IsBlocked { get; set; }

        public string HudText => IsBlocked && blockedBy != null
            ? $"{displayName}（被 {blockedBy.displayName} 挡住：{blockedAction}）"
            : displayName;
    }
}
