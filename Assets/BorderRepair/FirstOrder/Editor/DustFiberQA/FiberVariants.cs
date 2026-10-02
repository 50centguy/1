using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.FirstOrder.EditorTools.LayoutAB;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.FirstOrder.EditorTools.DustFiberQA
{
    /// <summary>
    /// 细纤维试验件（只用于验收试验，不是美术源）：从故障包导入的纤维网格出发，在编辑器里生成“减少 / 适度加粗”的变体网格，
    /// 局部坐标与原网格完全相同，Play 时直接换 sharedMesh 对比。
    /// 每根纤维是一条五边形截面的管子：按连通块分开；加粗 = 顶点沿法线外推 (k − 1)·r，同时整根沿引擎轴线抬高同样的量，保持和护栅条的间隙。
    /// r 用每个顶点最短邻边估：截面五边形边长 = 2r·sin 36° ≈ 1.18 r。
    /// </summary>
    public static class FiberVariants
    {
        public const string Dir = FirstOrderSceneBuilder.Root + "/Art/DustFiberQA";
        public const string OutDir = "Docs/Integration/Unit07FirstOrder/DustFiberQA";

        public static readonly (string id, string label, int keepEvery, float scale)[] Variants =
        {
            ("F0", "原样（11 根长纤维 + 毛团）", 1, 1.0f),
            ("F1", "减少一半（隔一根留一根）", 2, 1.0f),
            ("F2", "加粗 1.4 倍", 1, 1.4f),
            ("F3", "加粗 1.8 倍", 1, 1.8f),
            ("F4", "减少一半 + 加粗 1.4 倍", 2, 1.4f),
        };

        public static void Run()
        {
            EditorSceneManager.OpenScene(LayoutABScenes.SceneB, OpenSceneMode.Single);
            var flow = Object.FindFirstObjectByType<FirstOrderFlow>();
            var mf = flow.ClogLayers.Select(r => r.GetComponent<MeshFilter>()).First(m => m.sharedMesh.name.Contains("Fibers"));
            var src = mf.sharedMesh;
            var upL = mf.transform.InverseTransformDirection(flow.EngineLHinge.up).normalized;
            var v = src.vertices; var n = src.normals; var uv = src.uv; var tri = src.triangles;
            // 按位置焊接后找连通块（导入时法线 / UV 接缝会把同一位置的顶点拆开）
            var key = v.Select(p => (Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f))).ToArray();
            var weld = new Dictionary<(int, int, int), int>();
            var w = new int[v.Length];
            for (int i = 0; i < v.Length; i++) { if (!weld.TryGetValue(key[i], out var k)) { k = weld.Count; weld[key[i]] = k; } w[i] = k; }
            var parent = Enumerable.Range(0, weld.Count).ToArray();
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            for (int t = 0; t < tri.Length; t += 3) { int a = Find(w[tri[t]]), b = Find(w[tri[t + 1]]), c = Find(w[tri[t + 2]]); parent[b] = a; parent[Find(c)] = a; }
            var comp = v.Select((_, i) => Find(w[i])).ToArray();
            var comps = comp.Distinct().OrderBy(c => c).ToList();
            // 每个顶点的最短邻边
            var minEdge = Enumerable.Repeat(float.MaxValue, v.Length).ToArray();
            for (int t = 0; t < tri.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = tri[t + e], b = tri[t + (e + 1) % 3];
                    float d = (v[a] - v[b]).magnitude;
                    if (d < 1e-7f) continue;
                    minEdge[a] = Mathf.Min(minEdge[a], d); minEdge[b] = Mathf.Min(minEdge[b], d);
                }
            var info = comps.Select(c =>
            {
                var idx = Enumerable.Range(0, v.Length).Where(i => comp[i] == c).ToList();
                var b = new Bounds(v[idx[0]], Vector3.zero); foreach (var i in idx) b.Encapsulate(v[i]);
                var edges = idx.Select(i => minEdge[i]).Where(x => x < 1f).OrderBy(x => x).ToList();
                float r = edges[edges.Count / 2] / 1.176f;
                return (c, idx, len: b.size.magnitude, r);
            }).OrderByDescending(x => x.len).ToList();
            var longF = info.Where(x => x.len > 0.006f).ToList();
            var fuzz = info.Where(x => x.len <= 0.006f).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"源网格 `{AssetDatabase.GetAssetPath(src)}` / {src.name}：{v.Length} 顶点、{tri.Length / 3} 三角面、{info.Count} 根（长纤维 {longF.Count}，毛团 {fuzz.Count}）；估算半径 {info.Min(x => x.r) * 1000:F2}–{info.Max(x => x.r) * 1000:F2} mm（脚本里是 0.14–0.24 / 0.15 mm）");
            Directory.CreateDirectory(Dir);
            foreach (var (id, label, keepEvery, scale) in Variants)
            {
                var keep = longF.Where((x, i) => i % keepEvery == 0).Concat(fuzz.Where((x, i) => i % keepEvery == 0)).ToList();
                var map = new Dictionary<int, int>();
                var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>(); var nt = new List<int>();
                foreach (var (c, idx, len, r) in keep)
                {
                    float dr = (scale - 1f) * r;
                    foreach (var i in idx) { map[i] = nv.Count; nv.Add(v[i] + n[i] * dr + upL * dr); nn.Add(n[i]); nu.Add(uv.Length > 0 ? uv[i] : Vector2.zero); }
                }
                for (int t = 0; t < tri.Length; t += 3)
                    if (map.ContainsKey(tri[t]) && map.ContainsKey(tri[t + 1]) && map.ContainsKey(tri[t + 2])) { nt.Add(map[tri[t]]); nt.Add(map[tri[t + 1]]); nt.Add(map[tri[t + 2]]); }
                var m = new Mesh { name = $"Fibers_{id}" };
                m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nu); m.SetTriangles(nt, 0);
                m.RecalculateTangents(); m.RecalculateBounds();
                string path = $"{Dir}/Fibers_{id}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(m, path);
                sb.AppendLine($"- {id} {label}：{keep.Count} 根，{nt.Count / 3} 三角面，半径 × {scale}（整根沿引擎轴线抬高同样的量）→ `{path}`");
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "fiber_variants.md"), sb.ToString(), new UTF8Encoding(false));
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
