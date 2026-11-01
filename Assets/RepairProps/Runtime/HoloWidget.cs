using UnityEngine;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // One procedural motion graphic on a hologram panel. This writes a single static quad once (its kind and parameters
    // in uv1 / uv2); all the drawing and all the motion happen in HoloPanel.shader, so nothing is rebuilt per frame and
    // the widget shares its panel's material, reveal, breathing, scan band and tears. The owning canvas must enable
    // AdditionalCanvasShaderChannels.TexCoord1 and TexCoord2.
    [AddComponentMenu("")]
    public sealed class HoloWidget : MaskableGraphic
    {
        // Keep in step with the kind switch in HoloPanel.shader. KindOffset keeps stray uv1 data on other graphics from
        // ever reading as a widget.
        public enum Kind { Spectrum = 2, Scope, Ruler, SegmentFill, Chevrons, Ring, RowCursor, FlowRule, Bracket }
        const float KindOffset = 1000f;

        Kind kind;
        Vector4 parameters;
        float seed;

        // How many times this widget's mesh was generated (once after build, unless its rect changes).
        public int MeshBuilds { get; private set; }
        public Kind WidgetKind => kind;

        public void Configure(Kind widgetKind, float phaseSeed, Vector4 widgetParameters)
        {
            kind = widgetKind; seed = phaseSeed; parameters = widgetParameters;
            raycastTarget = false;
            SetAllDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            MeshBuilds++;
            vh.Clear();
            var rect = rectTransform.rect;
            var uv1 = new Vector4(KindOffset + (int)kind, seed, parameters.x, parameters.y);
            var uv2 = new Vector4(rect.width, rect.height, parameters.z, parameters.w);
            Add(vh, new Vector2(rect.xMin, rect.yMin), new Vector2(0, 0), uv1, uv2);
            Add(vh, new Vector2(rect.xMin, rect.yMax), new Vector2(0, 1), uv1, uv2);
            Add(vh, new Vector2(rect.xMax, rect.yMax), new Vector2(1, 1), uv1, uv2);
            Add(vh, new Vector2(rect.xMax, rect.yMin), new Vector2(1, 0), uv1, uv2);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        void Add(VertexHelper vh, Vector2 position, Vector2 uv, Vector4 uv1, Vector4 uv2)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = position; vertex.color = color; vertex.uv0 = uv; vertex.uv1 = uv1; vertex.uv2 = uv2;
            vh.AddVert(vertex);
        }
    }
}
