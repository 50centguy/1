using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BorderRepair.FirstOrder
{
    /// <summary>
    /// 按网格顶点算部件的摆放（场景构建和测试用，只在编辑器里跑：RobotV4 和故障美术包的网格都没开 Read/Write，只有编辑器能读顶点）。
    /// 包围盒在零件倾斜时会放大，摆放和穿模判断用顶点才准。
    /// </summary>
    public static class FirstOrderGeometry
    {
        /// <summary>部件（主对象 + 成员 + 子对象里正在显示的网格）每个顶点相对主对象原点的偏移，去掉主对象当前朝向（乘上目标朝向就是摆到那个朝向时的偏移）。</summary>
        public static List<Vector3> LocalOffsets(FirstOrderPart p, bool enabledOnly = true)
        {
            var inv = Quaternion.Inverse(p.transform.rotation);
            var o = p.transform.position;
            var list = new List<Vector3>();
            foreach (var r in p.Renderers())
            {
                if (enabledOnly && !(r.enabled && r.gameObject.activeInHierarchy)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices) list.Add(inv * (r.transform.TransformPoint(v) - o));
            }
            return list;
        }

        /// <summary>主对象转到 rot、原点在世界原点时，部件顶点的世界包围盒。</summary>
        public static Bounds BoundsAt(IEnumerable<Vector3> offsets, Quaternion rot)
        {
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            foreach (var v in offsets) { var w = rot * v; lo = Vector3.Min(lo, w); hi = Vector3.Max(hi, w); }
            var b = new Bounds(); b.SetMinMax(lo, hi);
            return b;
        }

        /// <summary>
        /// 把部件转到 rot、平放在 surface 点上：返回主对象原点相对 surface 的偏移（最低顶点高出表面 gap，顶点水平中心对准 surface）。
        /// </summary>
        public static Vector3 RestingOffset(IEnumerable<Vector3> offsets, Quaternion rot, float gap = 0.0008f)
        {
            var b = BoundsAt(offsets, rot);
            return new Vector3(-b.center.x, -b.min.y + gap, -b.center.z);
        }

        /// <summary>渲染器所有顶点的世界坐标。</summary>
        public static IEnumerable<Vector3> WorldVertices(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) yield break;
            foreach (var v in mf.sharedMesh.vertices) yield return r.transform.TransformPoint(v);
        }

        /// <summary>网格最薄的本地轴（轴承的转轴），换成世界方向。</summary>
        public static Vector3 ThinAxisWorld(MeshFilter mf)
        {
            var s = mf.sharedMesh.bounds.size;
            var axis = s.x <= s.y && s.x <= s.z ? Vector3.right : s.y <= s.z ? Vector3.up : Vector3.forward;
            return mf.transform.TransformDirection(axis).normalized;
        }
    }
}
