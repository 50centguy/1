using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BorderRepair.Inspection
{
    /// <summary>
    /// 挂在物品 prefab 的可扫描部位上（需要子物体带 Collider）。
    /// 只保存 pointId 和外观引用；文本、是否异常等内容在 RepairCaseData 中配置。
    ///
    /// 高亮有两条路径：
    /// - 场景里有启用的 <see cref="DiagnosticHighlightSettings"/> 时：给本部位的渲染器追加诊断叠加材质，
    ///   参数写在该材质槽的 per-material-index MaterialPropertyBlock 上，不碰底层材质和渲染器级属性块。
    /// - 否则沿用原来的底色染色（默认场景风格不变），但只改写 _BaseColor，并在恢复时还原原值（原本没有 _BaseColor 就恢复为没有），不清掉其他属性。
    /// 只处理“属于自己”的渲染器：嵌套在子检查点下的渲染器由子检查点自己管理。
    /// </summary>
    public class InspectionPoint : MonoBehaviour
    {
        /// <summary>Anomaly 为新增状态，追加在末尾，不改变原有枚举值。</summary>
        public enum VisualState { Normal, Hover, Scanned, Anomaly }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color HoverTint = new Color(0.35f, 0.95f, 1f);
        static readonly Color ScannedTint = new Color(0.45f, 1f, 0.5f);

        [SerializeField] string pointId;
        [Tooltip("换件维修前显示的损坏外观（可空）")]
        [SerializeField] GameObject brokenVisual;
        [Tooltip("换件维修后显示的新部件外观（可空，初始应为隐藏）")]
        [SerializeField] GameObject repairedVisual;

        Renderer[] renderers;
        MaterialPropertyBlock block;
        MaterialPropertyBlock overlayBlock;

        /// <summary>
        /// 旧路径：每个渲染器染色前的完整属性块快照（为空表示原本没有属性块）。
        /// 恢复时以快照为底，再合入染色期间其他代码改过的 shader 属性；只有 _BaseColor 回到快照里的状态（包括“原本没有”）。
        /// </summary>
        readonly Dictionary<Renderer, MaterialPropertyBlock> legacySaved = new Dictionary<Renderer, MaterialPropertyBlock>();

        public string PointId => pointId;
        public bool IsRepaired { get; private set; }
        public VisualState CurrentState { get; private set; } = VisualState.Normal;
        public IReadOnlyList<Renderer> OwnedRenderers { get { EnsureInit(); return renderers; } }

        /// <summary>供编辑器生成工具配置占位 prefab。</summary>
        public void Configure(string id, GameObject broken, GameObject repaired)
        {
            pointId = id;
            brokenVisual = broken;
            repairedVisual = repaired;
        }

        void Awake() => EnsureInit();

        void EnsureInit()
        {
            if (renderers != null) return;
            var owned = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r.GetComponentInParent<InspectionPoint>(true) == this) owned.Add(r);
            renderers = owned.ToArray();
            block = new MaterialPropertyBlock();
            overlayBlock = new MaterialPropertyBlock();
        }

        public void SetVisualState(VisualState state)
        {
            EnsureInit();
            CurrentState = state;
            var settings = DiagnosticHighlightSettings.Active;
            if (settings != null && settings.IsValid)
            {
                RestoreLegacyTint();
                ApplyOverlay(state, settings);
            }
            else
            {
                RemoveOverlay(DiagnosticHighlightSettings.LastOverlayMaterial);
                // 旧路径没有单独的异常表现：按“已扫描”显示，默认场景风格不变
                ApplyLegacyTint(state == VisualState.Anomaly ? VisualState.Scanned : state);
            }
        }

        // ---------- 诊断叠加层 ----------

        void ApplyOverlay(VisualState state, DiagnosticHighlightSettings settings)
        {
            var overlay = settings.Profile.overlayMaterial;
            if (state == VisualState.Normal)
            {
                RemoveOverlay(overlay);
                return;
            }
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                int index = Array.IndexOf(mats, overlay);
                if (index < 0)
                {
                    Array.Resize(ref mats, mats.Length + 1);
                    index = mats.Length - 1;
                    mats[index] = overlay;
                    r.sharedMaterials = mats;
                }
                overlayBlock.Clear();
                settings.Profile.Fill(overlayBlock, state);
                r.SetPropertyBlock(overlayBlock, index);
            }
        }

        void RemoveOverlay(Material overlay)
        {
            if (overlay == null) return;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                int index = Array.IndexOf(mats, overlay);
                if (index < 0) continue;
                overlayBlock.Clear();
                r.SetPropertyBlock(overlayBlock, index);     // 清掉该槽位上我们写的参数
                var trimmed = new Material[mats.Length - 1];
                for (int i = 0, j = 0; i < mats.Length; i++)
                    if (i != index) trimmed[j++] = mats[i];
                r.sharedMaterials = trimmed;
            }
        }

        // ---------- 旧路径：底色染色（只动 _BaseColor，恢复时还原） ----------

        void ApplyLegacyTint(VisualState state)
        {
            if (state == VisualState.Normal)
            {
                RestoreLegacyTint();
                return;
            }
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (!legacySaved.TryGetValue(r, out var original))
                {
                    original = new MaterialPropertyBlock();
                    r.GetPropertyBlock(original);
                    legacySaved[r] = original;
                }
                var mat = r.sharedMaterial;
                Color baseColor = original.HasColor(BaseColorId) ? original.GetColor(BaseColorId)
                    : mat != null && mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId) : Color.white;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, Color.Lerp(baseColor, state == VisualState.Hover ? HoverTint : ScannedTint, 0.6f));
                r.SetPropertyBlock(block);
            }
        }

        void RestoreLegacyTint()
        {
            foreach (var kv in legacySaved)
            {
                var r = kv.Key;
                if (r == null) continue;
                var restored = kv.Value;
                r.GetPropertyBlock(block);                     // 当前块：可能含有染色期间其他代码写入的参数
                CopyShaderPropertiesExcept(r, block, restored, BaseColorId);
                // 原本没有属性块、期间也没人写入：还原为“没有属性块”
                r.SetPropertyBlock(restored.isEmpty ? null : restored);
            }
            legacySaved.Clear();
        }

        /// <summary>
        /// 把 from 中属于该渲染器材质 shader 的属性复制到 to，跳过 skipId。
        /// MaterialPropertyBlock 既不能删除单个属性也不能枚举，所以按 shader 声明的属性逐个复制；
        /// 非 shader 声明的属性以染色前的快照为准。
        /// </summary>
        static void CopyShaderPropertiesExcept(Renderer r, MaterialPropertyBlock from, MaterialPropertyBlock to, int skipId)
        {
            if (from.isEmpty) return;
            var done = new HashSet<int> { skipId };
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null || mat.shader == null) continue;
                var shader = mat.shader;
                for (int i = 0, n = shader.GetPropertyCount(); i < n; i++)
                {
                    int id = shader.GetPropertyNameId(i);
                    if (!done.Add(id)) continue;
                    switch (shader.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Color:
                        case ShaderPropertyType.Vector:
                            if (from.HasVector(id) || from.HasColor(id)) to.SetVector(id, from.GetVector(id));   // 按原始数值复制，不做色彩空间转换
                            break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            if (from.HasFloat(id)) to.SetFloat(id, from.GetFloat(id));
                            break;
                        case ShaderPropertyType.Int:
                            if (from.HasInteger(id)) to.SetInteger(id, from.GetInteger(id));
                            else if (from.HasFloat(id)) to.SetFloat(id, from.GetFloat(id));
                            break;
                        case ShaderPropertyType.Texture:
                            if (from.HasTexture(id)) to.SetTexture(id, from.GetTexture(id));
                            break;
                    }
                }
            }
        }

        public void ApplyRepair()
        {
            if (brokenVisual != null) brokenVisual.SetActive(false);
            if (repairedVisual != null) repairedVisual.SetActive(true);
            IsRepaired = true;
        }
    }
}
