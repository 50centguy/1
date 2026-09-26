using UnityEngine;

namespace BorderRepair.Inspection
{
    /// <summary>
    /// 场景级开关：场景里有启用的本组件时，InspectionPoint 使用诊断叠加层高亮；
    /// 没有时沿用原来的底色染色。默认原型场景和叙事场景都不含本组件，风格不变。
    /// </summary>
    public class DiagnosticHighlightSettings : MonoBehaviour
    {
        [SerializeField] DiagnosticHighlightProfile profile;

        public static DiagnosticHighlightSettings Active { get; private set; }

        /// <summary>最近一次使用的叠加材质；关闭设置后用它清理残留的叠加层。</summary>
        public static Material LastOverlayMaterial { get; private set; }

        public DiagnosticHighlightProfile Profile => profile;
        public bool IsValid => profile != null && profile.overlayMaterial != null;

        public void Configure(DiagnosticHighlightProfile p)
        {
            profile = p;
            if (isActiveAndEnabled && IsValid) LastOverlayMaterial = profile.overlayMaterial;
        }

        void OnEnable()
        {
            Active = this;
            if (IsValid) LastOverlayMaterial = profile.overlayMaterial;
        }

        void OnDisable()
        {
            if (Active == this) Active = null;
        }
    }
}
