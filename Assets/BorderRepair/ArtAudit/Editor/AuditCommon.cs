using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.ArtAudit
{
    /// <summary>
    /// 七号美术审计的共用工具（只读）：打开布局 B 场景（不保存）、路径、简单 JSON 输出、网格间距（点到三角形，双向）。
    /// 所有坐标均为 Unity 世界坐标（米，Y 向上）。机器人自身的左 / 右沿用 RobotV4 命名（_L = 机器人自己的左）。
    /// </summary>
    public static class AuditCommon
    {
        public const string SceneB = "Assets/BorderRepair/FirstOrder/Scenes/LayoutAB/Unit07FirstOrder_LayoutB.unity";
        public const string RobotFbx = "Assets/RobotV4/Model/robot-final.fbx";
        public const string OutDir = "Docs/Integration/Unit07ArtAudit";
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void OpenSceneB() => EditorSceneManager.OpenScene(SceneB, OpenSceneMode.Single);

        public static Transform Find(string name) =>
            Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == name);

        public static Transform FindUnder(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        public static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        public static string F(float v) => v.ToString("0.#####", Inv);
        public static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
        public static string Q(Quaternion q) => $"[{F(q.x)},{F(q.y)},{F(q.z)},{F(q.w)}]";
        public static string S(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
        public static string Mm(float m) => (m * 1000f).ToString("0.0", Inv);

        public static void Write(string file, string text)
        {
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, file), text, new UTF8Encoding(false));
        }

        // ------------------------------------------------------------------ 世界空间三角网格

        public class WMesh
        {
            public string name;
            public Vector3[] v;
            public int[] tri;
            public Bounds b;
        }

        /// <summary>渲染器当前姿态下的世界空间网格（蒙皮件先 BakeMesh）。</summary>
        public static WMesh World(Renderer r)
        {
            Mesh m; Matrix4x4 mtx;
            if (r is SkinnedMeshRenderer sk)
            {
                m = new Mesh(); sk.BakeMesh(m, true);
                mtx = Matrix4x4.TRS(sk.transform.position, sk.transform.rotation, Vector3.one);
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) return null;
                m = mf.sharedMesh; mtx = r.transform.localToWorldMatrix;
            }
            var v = m.vertices.Select(p => mtx.MultiplyPoint3x4(p)).ToArray();
            if (v.Length == 0) return null;
            var b = new Bounds(v[0], Vector3.zero);
            foreach (var p in v) b.Encapsulate(p);
            return new WMesh { name = r.name, v = v, tri = m.triangles, b = b };
        }

        static Vector3 ClosestOnTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            // Ericson, Real-Time Collision Detection 5.1.5
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

        static bool SegTri(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, qp = p - q;
            Vector3 n = Vector3.Cross(ab, ac);
            float d = Vector3.Dot(qp, n);
            if (Mathf.Abs(d) < 1e-12f) return false;
            Vector3 ap = p - a;
            float t = Vector3.Dot(ap, n);
            if (d < 0f) { d = -d; t = -t; ap = -ap; qp = -qp; n = -n; }
            if (t < 0f || t > d) return false;
            Vector3 e = Vector3.Cross(qp, ap);
            float v = Vector3.Dot(ac, e); if (v < 0f || v > d) return false;
            float w = -Vector3.Dot(ab, e); if (w < 0f || v + w > d) return false;
            return true;
        }

        /// <summary>
        /// A 的顶点到 B 的三角形、B 的顶点到 A 的三角形的最短距离（只看包围盒放大 cutoff 后相交的部分）。
        /// 边与边之间的最近点不单独算，所以结果略偏大（网格越密越准）；另查 A 的边是否穿过 B 的三角形（穿插 = 0）。
        /// 返回 cutoff 表示超出检查范围。
        /// </summary>
        public static float MinDistance(WMesh A, WMesh B, float cutoff, out Vector3 pa, out Vector3 pb)
        {
            pa = pb = Vector3.zero;
            var ea = A.b; ea.Expand(cutoff * 2f);
            if (!ea.Intersects(B.b)) return cutoff;
            float best = cutoff;
            Dir(A, B, cutoff, ref best, ref pa, ref pb, false);
            Dir(B, A, cutoff, ref best, ref pa, ref pb, true);
            if (best > 0f && (Crosses(A, B) || Crosses(B, A))) { best = 0f; }
            return best;
        }

        static void Dir(WMesh P, WMesh T, float cutoff, ref float best, ref Vector3 pa, ref Vector3 pb, bool swap)
        {
            var tb = T.b; tb.Expand(cutoff * 2f);
            var pts = new List<Vector3>();
            foreach (var p in P.v) if (tb.Contains(p)) pts.Add(p);
            if (pts.Count == 0) return;
            var pbx = P.b; pbx.Expand(cutoff * 2f);
            for (int i = 0; i < T.tri.Length; i += 3)
            {
                Vector3 a = T.v[T.tri[i]], b = T.v[T.tri[i + 1]], c = T.v[T.tri[i + 2]];
                var tbb = new Bounds(a, Vector3.zero); tbb.Encapsulate(b); tbb.Encapsulate(c);
                if (!tbb.Intersects(pbx)) continue;
                tbb.Expand(best * 2f);
                foreach (var p in pts)
                {
                    if (!tbb.Contains(p)) continue;
                    var cpt = ClosestOnTri(p, a, b, c);
                    float d = (cpt - p).magnitude;
                    if (d < best) { best = d; if (swap) { pa = cpt; pb = p; } else { pa = p; pb = cpt; } }
                }
            }
        }

        /// <summary>包围盒与 region 相交的三角形（顶点 + 包围盒）。</summary>
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
            var eb = E.b; eb.Expand(0.002f);
            var tb = T.b; tb.Expand(0.002f);
            if (!eb.Intersects(tb)) return false;
            // 只看两个包围盒的重叠区里的三角形
            var ov = new Bounds(); ov.SetMinMax(Vector3.Max(eb.min, tb.min), Vector3.Min(eb.max, tb.max));
            var tt = TrisIn(T, ov);
            if (tt.Count == 0) return false;
            var et = TrisIn(E, ov);
            foreach (var e in et)
            {
                var ev = new[] { e.a, e.b, e.c };
                for (int k = 0; k < 3; k++)
                {
                    Vector3 p = ev[k], q = ev[(k + 1) % 3];
                    var sb = new Bounds(p, Vector3.zero); sb.Encapsulate(q);
                    foreach (var t in tt)
                        if (t.bb.Intersects(sb) && SegTri(p, q, t.a, t.b, t.c)) return true;
                }
            }
            return false;
        }

        /// <summary>一组网格到另一组网格的最短距离，并给出是哪两个对象。</summary>
        public static (float d, string a, string b, Vector3 pa, Vector3 pb) MinDistance(IList<WMesh> A, IList<WMesh> B, float cutoff)
        {
            (float d, string a, string b, Vector3 pa, Vector3 pb) best = (cutoff, "-", "-", Vector3.zero, Vector3.zero);
            foreach (var x in A)
                foreach (var y in B)
                {
                    float d = MinDistance(x, y, Mathf.Min(cutoff, best.d), out var pa, out var pb);
                    if (d < best.d) best = (d, x.name, y.name, pa, pb);
                }
            return best;
        }
    }
}
