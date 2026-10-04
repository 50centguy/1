using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static BorderRepair.TwoNight.MeshClearance;

namespace BorderRepair.TwoNight
{
    /// <summary>
    /// 托盘几何分区（编辑器检查与 PlayMode 探针共用；Player 里网格不可读，不在运行逻辑里用）。
    /// 支持托盘由多个子物体组成（提手抬高变体：Tray_Body + Tray_Handle_PX / NX，带 GripBarStart_* / GripBarEnd_* 标记）。
    /// 分三类：盘体、握杆直段（离握杆轴线 ≤ 6.5 mm 且在两个标记之间的三角面）、立柱与安装座（提手其余部分）。
    /// 没有握杆标记的单网格托盘（原 Dock_PartsTray）整个算盘体。只取启用且激活的 Renderer（停用的旧盘不会被算进来）。
    /// </summary>
    public static class TrayGeometry
    {
        public const float BarRadialLimit = 0.0065f;

        public class Parts
        {
            public List<WMesh> body = new List<WMesh>(), bars = new List<WMesh>(), legs = new List<WMesh>();
            public List<WMesh> All => body.Concat(bars).Concat(legs).ToList();
            public List<WMesh> NonBar => body.Concat(legs).ToList();
        }

        public static IEnumerable<Renderer> Renderers(Transform tray) =>
            tray.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy);

        /// <summary>世界坐标（再乘 extra）的分区网格。</summary>
        public static Parts Split(Transform tray, Matrix4x4 extra)
        {
            var parts = new Parts();
            foreach (var r in Renderers(tray))
            {
                var w = World(r); if (w == null) continue;
                w = Moved(w, extra);
                string sfx = r.name.EndsWith("_PX") ? "PX" : r.name.EndsWith("_NX") ? "NX" : null;
                var s = sfx != null ? tray.Find("GripBarStart_" + sfx) : null;
                var e = sfx != null ? tray.Find("GripBarEnd_" + sfx) : null;
                if (s == null || e == null) { parts.body.Add(w); continue; }
                var p0 = extra.MultiplyPoint3x4(s.position); var p1 = extra.MultiplyPoint3x4(e.position);
                var c = (p0 + p1) / 2f; var axis = (p1 - p0).normalized; float half = (p1 - p0).magnitude / 2f + 0.0005f;
                var bt = new List<int>(); var lt = new List<int>();
                for (int i = 0; i < w.tri.Length; i += 3)
                {
                    var cen = (w.v[w.tri[i]] + w.v[w.tri[i + 1]] + w.v[w.tri[i + 2]]) / 3f;
                    float along = Vector3.Dot(cen - c, axis);
                    float radial = (cen - c - axis * along).magnitude;
                    (Mathf.Abs(along) <= half && radial <= BarRadialLimit ? bt : lt).AddRange(new[] { w.tri[i], w.tri[i + 1], w.tri[i + 2] });
                }
                if (bt.Count > 0) parts.bars.Add(Sub(w, bt, r.name + "·握杆直段"));
                if (lt.Count > 0) parts.legs.Add(Sub(w, lt, r.name + "·立柱 / 安装座"));
            }
            return parts;
        }

        static WMesh Sub(WMesh w, List<int> tri, string name)
        {
            var b = new Bounds(w.v[tri[0]], Vector3.zero); foreach (var k in tri) b.Encapsulate(w.v[k]);
            return new WMesh { name = name, v = w.v, tri = tri.ToArray(), b = b };
        }

        public static bool IsTeeth(Renderer r) => r.name.EndsWith("_Teeth");
    }
}
