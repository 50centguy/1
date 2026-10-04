using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BorderRepair.TwoNight
{
    /// <summary>
    /// 真实三角网格之间的最近距离（与美术审计 art/unit07-asset-audit 的 AuditCommon 同一算法）：
    /// 双向“点到三角形”最近距离 + 边穿三角形检查（穿插记 0），只比较包围盒相近的部分。边到边最近点不单独算，结果最多偏大约一个网格边长。
    /// 只在编辑器里用（构建脚本的静态路径检查、PlayMode 的动作探针）；Player 里 RobotV4 网格不可读。
    /// </summary>
    public static class MeshClearance
    {
        public class WMesh { public string name; public Vector3[] v; public int[] tri; public Bounds b; }

        public static WMesh World(Renderer r)
        {
            Mesh m; Matrix4x4 mtx;
            if (r is SkinnedMeshRenderer sk) { m = new Mesh(); sk.BakeMesh(m, true); mtx = Matrix4x4.TRS(sk.transform.position, sk.transform.rotation, Vector3.one); }
            else { var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable && !Application.isEditor) return null; m = mf.sharedMesh; mtx = r.transform.localToWorldMatrix; }
            var (src, tri) = Application.isPlaying && !(r is SkinnedMeshRenderer) && (!m.isReadable || r.isPartOfStaticBatch) ? ReadPlaying(r) : (m.vertices, m.triangles);
            if (src.Length == 0) return null;
            var v = new Vector3[src.Length];
            for (int i = 0; i < src.Length; i++) v[i] = mtx.MultiplyPoint3x4(src[i]);
            return new WMesh { name = r.name, v = v, tri = tri, b = BoundsOf(v) };
        }

        static readonly Dictionary<Renderer, (Vector3[] v, int[] t)> cache = new Dictionary<Renderer, (Vector3[], int[])>();

        /// <summary>
        /// 播放时的网格读取器（按 Renderer，返回本地坐标的原始网格）。编辑模式下 mesh.vertices 可以直接读；编辑器 PlayMode 里不可读网格读不出（MeshData 也不行），
        /// 静态合批的物体网格还被换成了合并网格。由 PlayMode 动作探针在进入播放前（编辑模式）把原始网格缓存好，再挂到这里。不改任何导入设置。
        /// </summary>
        public static System.Func<Renderer, (Vector3[] v, int[] t)> PlayingReader;

        static (Vector3[] v, int[] t) ReadPlaying(Renderer r)
        {
            if (cache.TryGetValue(r, out var hit)) return hit;
            if (PlayingReader == null) throw new System.InvalidOperationException(r.name + " 的网格播放时读不出，需要先挂 MeshClearance.PlayingReader");
            return cache[r] = PlayingReader(r);
        }

        public static WMesh Moved(WMesh m, Matrix4x4 mtx)
        {
            var v = new Vector3[m.v.Length];
            for (int i = 0; i < v.Length; i++) v[i] = mtx.MultiplyPoint3x4(m.v[i]);
            return new WMesh { name = m.name, v = v, tri = m.tri, b = BoundsOf(v) };
        }

        static Bounds BoundsOf(Vector3[] v) { var b = new Bounds(v[0], Vector3.zero); for (int i = 1; i < v.Length; i++) b.Encapsulate(v[i]); return b; }

        static Vector3 ClosestOnTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        /// <summary>
        /// 线段 pq 是否穿过三角形 abc（双面，Möller–Trumbore）。
        /// 注：美术审计 AuditCommon 的旧写法在线段与法线反向时把交点按顶点 a 镜像了，会误报穿插、也会漏报；这里已改正（见 README）。
        /// </summary>
        static bool SegTri(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 dir = q - p, e1 = b - a, e2 = c - a;
            Vector3 h = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, h);
            if (Mathf.Abs(det) < 1e-18f) return false;          // 平行
            float inv = 1f / det;
            Vector3 s = p - a;
            float u = inv * Vector3.Dot(s, h); if (u < 0f || u > 1f) return false;
            Vector3 qv = Vector3.Cross(s, e1);
            float v = inv * Vector3.Dot(dir, qv); if (v < 0f || u + v > 1f) return false;
            float t = inv * Vector3.Dot(e2, qv);
            return t >= 0f && t <= 1f;
        }

        static List<(Vector3 a, Vector3 b, Vector3 c, Bounds bb)> TrisIn(WMesh M, Bounds region)
        {
            var res = new List<(Vector3, Vector3, Vector3, Bounds)>();
            for (int i = 0; i < M.tri.Length; i += 3)
            {
                Vector3 a = M.v[M.tri[i]], b = M.v[M.tri[i + 1]], c = M.v[M.tri[i + 2]];
                var bb = new Bounds(a, Vector3.zero); bb.Encapsulate(b); bb.Encapsulate(c);
                if (bb.Intersects(region)) res.Add((a, b, c, bb));
            }
            return res;
        }

        static bool Crosses(WMesh E, WMesh T)
        {
            var eb = E.b; eb.Expand(0.002f); var tb = T.b; tb.Expand(0.002f);
            if (!eb.Intersects(tb)) return false;
            var ov = new Bounds(); ov.SetMinMax(Vector3.Max(eb.min, tb.min), Vector3.Min(eb.max, tb.max));
            var tt = TrisIn(T, ov); if (tt.Count == 0) return false;
            foreach (var e in TrisIn(E, ov))
            {
                var ev = new[] { e.a, e.b, e.c };
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = ev[k], q = ev[(k + 1) % 3];
                    var sb = new Bounds(p, Vector3.zero); sb.Encapsulate(q);
                    foreach (var t in tt) if (t.bb.Intersects(sb) && SegTri(p, q, t.a, t.b, t.c)) return true;
                }
            }
            return false;
        }

        static void Dir(WMesh P, WMesh T, ref float best)
        {
            var tb = T.b; tb.Expand(best * 2f);
            var pts = P.v.Where(p => tb.Contains(p)).ToList();
            if (pts.Count == 0) return;
            var pb = P.b; pb.Expand(best * 2f);
            for (int i = 0; i < T.tri.Length; i += 3)
            {
                Vector3 a = T.v[T.tri[i]], b = T.v[T.tri[i + 1]], c = T.v[T.tri[i + 2]];
                var bb = new Bounds(a, Vector3.zero); bb.Encapsulate(b); bb.Encapsulate(c);
                if (!bb.Intersects(pb)) continue;
                bb.Expand(best * 2f);
                foreach (var p in pts)
                {
                    if (!bb.Contains(p)) continue;
                    float d = (ClosestOnTri(p, a, b, c) - p).magnitude;
                    if (d < best) best = d;
                }
            }
        }

        public static float Distance(WMesh A, WMesh B, float cutoff)
        {
            var ea = A.b; ea.Expand(cutoff * 2f);
            if (!ea.Intersects(B.b)) return cutoff;
            float best = cutoff;
            Dir(A, B, ref best); Dir(B, A, ref best);
            if (best > 0f && (Crosses(A, B) || Crosses(B, A))) best = 0f;
            return best;
        }

        /// <summary>两组网格之间的最近距离和对应的两个对象名（超出 cutoff 时返回 cutoff）。</summary>
        public static (float d, string a, string b) Min(IList<WMesh> A, IList<WMesh> B, float cutoff)
        {
            (float d, string a, string b) best = (cutoff, "-", "-");
            foreach (var x in A)
                foreach (var y in B)
                {
                    if (x == null || y == null) continue;
                    float d = Distance(x, y, best.d);
                    if (d < best.d) best = (d, x.name, y.name);
                }
            return best;
        }
    }
}
