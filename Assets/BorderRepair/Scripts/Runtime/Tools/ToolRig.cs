using BorderRepair.Inspection;
using UnityEngine;

namespace BorderRepair.Tools
{
    /// <summary>
    /// 一件工具 + 握着它的简化手套手（prefab 根物体）。约定：原点 = 工具作用端，本地 +Y = 从作用端指向手柄，
    /// 手套手臂朝本地 -X。只负责外观；位置由 RepairToolAnimator（桌面预设动作）或以后的 VR 手柄驱动。
    /// </summary>
    public class ToolRig : MonoBehaviour
    {
        [SerializeField] ToolKind kind;
        [Tooltip("可以绕工具轴转动的部分（螺丝刀拧螺丝时只转工具，不转手套）")]
        [SerializeField] Transform toolModel;
        [SerializeField] Transform glove;

        Quaternion toolBaseRotation;
        bool initialized;

        public ToolKind Kind => kind;
        public Transform ToolModel => toolModel;
        public Transform Glove => glove;

        /// <summary>供编辑器生成工具配置。</summary>
        public void Configure(ToolKind k, Transform tool, Transform gloveTransform)
        {
            kind = k;
            toolModel = tool;
            glove = gloveTransform;
        }

        void Init()
        {
            if (initialized) return;
            initialized = true;
            if (toolModel != null) toolBaseRotation = toolModel.localRotation;
        }

        /// <summary>工具绕自身轴线转过的角度（度）；手套不动。</summary>
        public void SetSpin(float degrees)
        {
            Init();
            if (toolModel != null) toolModel.localRotation = Quaternion.AngleAxis(degrees, Vector3.up) * toolBaseRotation;
        }
    }
}
