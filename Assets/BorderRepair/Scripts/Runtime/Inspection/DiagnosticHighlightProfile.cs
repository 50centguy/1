using System;
using UnityEngine;

namespace BorderRepair.Inspection
{
    /// <summary>
    /// 诊断高亮的外观配置（悬停 / 已扫描 / 异常）。数值写入叠加材质槽位的 MaterialPropertyBlock，
    /// 不修改任何材质资产。对应 shader：BorderRepair/DiagnosticOverlay。
    /// </summary>
    [CreateAssetMenu(menuName = "Border Repair/Diagnostic Highlight Profile", fileName = "DiagnosticHighlightProfile")]
    public class DiagnosticHighlightProfile : ScriptableObject
    {
        [Serializable]
        public class StateStyle
        {
            [ColorUsage(false, true)] public Color color = Color.cyan;
            [Tooltip("整体轻微提亮（滤色混合），过高会冲淡文字与划痕")]
            [Range(0f, 0.5f)] public float tint = 0.08f;
            [Tooltip("边缘光强度：主要亮在轮廓上，不遮挡正面细节")]
            [Range(0f, 2f)] public float rim = 0.9f;
            [Range(0.5f, 8f)] public float rimPower = 2.5f;
            [Range(0f, 1f)] public float scanlines;
            [Tooltip("扫描线：每米条数（世界空间）")]
            public float scanlineDensity = 140f;
            [Tooltip("脉冲频率（弧度/秒），0 = 不闪烁")]
            public float pulseSpeed;
            [Tooltip("异常斜纹强度（屏幕空间）")]
            [Range(0f, 1f)] public float hatch;
            [Tooltip("斜纹间距（像素）")]
            public float hatchPeriodPx = 16f;
        }

        static readonly int DiagColorId = Shader.PropertyToID("_DiagColor");
        static readonly int DiagParamsId = Shader.PropertyToID("_DiagParams");
        static readonly int DiagParams2Id = Shader.PropertyToID("_DiagParams2");

        public Material overlayMaterial;
        public StateStyle hover = new StateStyle { color = new Color(0.3f, 0.9f, 1f), tint = 0.1f, rim = 1.0f, rimPower = 2.5f, scanlines = 0.35f };
        // 已扫描：正对镜头的平面几乎没有边缘光，主要靠提亮区分（4% 在截图中与普通状态区别不明显，调到 12%）
        public StateStyle scanned = new StateStyle { color = new Color(0.45f, 1f, 0.55f), tint = 0.12f, rim = 0.8f, rimPower = 2.5f };
        // 异常：可读性优先。斜纹会切断封条文字和主板丝印，默认关闭（参数仍可配置）；
        // 改用均匀的橙色提亮 + 脉冲区分，边缘光指数提高，避免斜看主板时整块被边缘光染橙。
        public StateStyle anomaly = new StateStyle { color = new Color(1f, 0.55f, 0.1f), tint = 0.1f, rim = 1.2f, rimPower = 3.5f, pulseSpeed = 3f, hatch = 0f, hatchPeriodPx = 18f };

        /// <summary>把三种状态恢复为代码中的默认值（编辑器“重置高亮配置”使用）。</summary>
        public void ResetStylesToDefaults()
        {
            var d = CreateInstance<DiagnosticHighlightProfile>();
            hover = d.hover;
            scanned = d.scanned;
            anomaly = d.anomaly;
            if (Application.isPlaying) Destroy(d); else DestroyImmediate(d);
        }

        public StateStyle StyleFor(InspectionPoint.VisualState state)
        {
            switch (state)
            {
                case InspectionPoint.VisualState.Hover: return hover;
                case InspectionPoint.VisualState.Anomaly: return anomaly;
                default: return scanned;
            }
        }

        /// <summary>只写入叠加 shader 的三个属性，不触碰其他属性。</summary>
        public void Fill(MaterialPropertyBlock block, InspectionPoint.VisualState state)
        {
            var s = StyleFor(state);
            block.SetColor(DiagColorId, s.color);
            block.SetVector(DiagParamsId, new Vector4(s.tint, s.rim, s.rimPower, s.scanlines));
            block.SetVector(DiagParams2Id, new Vector4(s.scanlineDensity, s.pulseSpeed, s.hatch, Mathf.Max(2f, s.hatchPeriodPx)));
        }
    }
}
