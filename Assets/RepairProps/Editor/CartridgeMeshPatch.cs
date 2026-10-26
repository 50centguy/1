using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // Patches the memory cartridge's broken faces without remodelling it (the FBX is not touched):
    // - MC_TopCover: closes the hairline T-junction crack (an edge with a vertex sitting on it, no matching split);
    // - MC_ext_BaseScrew_B_p17: re-winds the faces that point inwards (Unity culls them, so they read as a hole);
    // - MC_ext_EdgeStrip_C_p59 / _D_p61: duplicates of strips A / B in the same place (z-fighting, and D carries a stray
    //   spike), switched off.
    // Patched meshes are saved beside the model and the prefab points at them. Re-runnable.
    static class CartridgeMeshPatch
    {
        const string PrefabPath = "Assets/RepairProps/Prefabs/Cartridge.prefab";
        const string FbxPath = "Assets/RepairProps/Models/Cartridge/Cartridge.fbx";
        const string OutFolder = "Assets/RepairProps/Models/Cartridge/Patched";
        static readonly string[] Duplicates = { "MC_ext_EdgeStrip_C_p59", "MC_ext_EdgeStrip_D_p61" };

        public static void Run()
        {
            var log = new StringBuilder();
            Directory.CreateDirectory(OutFolder);
            var source = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Mesh>().ToDictionary(m => m.name);
            var cover = Save(CloseTJunctions(source["MC_TopCover"], log));
            var screw = Save(Rewind(source["MC_ext_BaseScrew_B_p17"], log));
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var all = root.GetComponentsInChildren<Transform>(true);
                Point(all.First(t => t.name == "MC_TopCover"), cover);
                Point(all.First(t => t.name == "MC_ext_BaseScrew_B_p17"), screw);
                foreach (var name in Duplicates)
                {
                    all.First(t => t.name == name).gameObject.SetActive(false);
                    log.AppendLine("off " + name);
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("[CartridgeMeshPatch]\n" + log);
        }

        static void Point(Transform node, Mesh mesh)
        {
            node.GetComponent<MeshFilter>().sharedMesh = mesh;
            foreach (var c in node.GetComponents<MeshCollider>()) c.sharedMesh = mesh;
        }

        static Mesh Save(Mesh mesh)
        {
            string path = OutFolder + "/" + mesh.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            return existing;
        }

        // Welds by position so seams split for UVs/normals do not count as open edges.
        static int[] Weld(Vector3[] v)
        {
            var key = new Dictionary<Vector3Int, int>();
            var id = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var k = Vector3Int.RoundToInt(v[i] * 1e6f);
                if (!key.TryGetValue(k, out int w)) key[k] = w = key.Count;
                id[i] = w;
            }
            return id;
        }

        static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        static Mesh CloseTJunctions(Mesh source, StringBuilder log)
        {
            var mesh = Object.Instantiate(source); mesh.name = source.name + "_patched";
            var v = new List<Vector3>(); mesh.GetVertices(v);
            var n = new List<Vector3>(); mesh.GetNormals(n);
            var t = new List<Vector4>(); mesh.GetTangents(t);
            var uv = new List<Vector2>(); mesh.GetUVs(0, uv);
            var weld = Weld(v.ToArray());
            var subs = Enumerable.Range(0, mesh.subMeshCount).Select(s => mesh.GetTriangles(s).ToList()).ToList();
            var use = new Dictionary<long, int>();
            foreach (var tris in subs)
                for (int i = 0; i < tris.Count; i += 3)
                    for (int e = 0; e < 3; e++) { long k = Key(weld[tris[i + e]], weld[tris[i + (e + 1) % 3]]); use[k] = use.TryGetValue(k, out int u) ? u + 1 : 1; }
            var openVerts = new HashSet<int>();
            foreach (var p in use.Where(p => p.Value == 1)) { openVerts.Add((int)(p.Key >> 32)); openVerts.Add((int)(p.Key & 0xffffffff)); }
            var anyIndexOf = new Dictionary<int, int>();
            for (int i = 0; i < v.Count; i++) if (!anyIndexOf.ContainsKey(weld[i])) anyIndexOf[weld[i]] = i;
            int splits = 0;
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var tris in subs)
                {
                    for (int i = 0; i < tris.Count && !changed; i += 3)
                        for (int e = 0; e < 3 && !changed; e++)
                        {
                            int ia = tris[i + e], ib = tris[i + (e + 1) % 3], ic = tris[i + (e + 2) % 3];
                            if (use.TryGetValue(Key(weld[ia], weld[ib]), out int u) && u != 1) continue;
                            // An open edge with another open vertex lying on it: split the triangle there.
                            foreach (int w in openVerts)
                            {
                                if (w == weld[ia] || w == weld[ib]) continue;
                                var p = v[anyIndexOf[w]];
                                var ab = v[ib] - v[ia];
                                float s = Vector3.Dot(p - v[ia], ab) / ab.sqrMagnitude;
                                if (s <= .001f || s >= .999f) continue;
                                if ((v[ia] + ab * s - p).magnitude > 1e-6f) continue;
                                int m = v.Count;
                                v.Add(p);
                                if (n.Count > 0) n.Add(Vector3.Lerp(n[ia], n[ib], s).normalized);
                                if (t.Count > 0) t.Add(Vector4.Lerp(t[ia], t[ib], s));
                                if (uv.Count > 0) uv.Add(Vector2.Lerp(uv[ia], uv[ib], s));
                                System.Array.Resize(ref weld, weld.Length + 1); weld[m] = w;
                                tris[i] = ia; tris[i + 1] = m; tris[i + 2] = ic;
                                tris.AddRange(new[] { m, ib, ic });
                                use.Remove(Key(weld[ia], weld[ib]));
                                foreach (var k in new[] { Key(weld[ia], w), Key(w, weld[ib]) }) use[k] = use.TryGetValue(k, out int c) ? c + 1 : 1;
                                splits++; changed = true;
                                break;
                            }
                        }
                    if (changed) break;
                }
            }
            mesh.SetVertices(v);
            if (n.Count > 0) mesh.SetNormals(n);
            if (t.Count > 0) mesh.SetTangents(t);
            if (uv.Count > 0) mesh.SetUVs(0, uv);
            for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateBounds();
            log.AppendLine(string.Format("{0}: split {1} triangle(s), open edges {2} -> {3}", source.name, splits, OpenEdges(source), OpenEdges(mesh)));
            return mesh;
        }

        static Mesh Rewind(Mesh source, StringBuilder log)
        {
            var mesh = Object.Instantiate(source); mesh.name = source.name + "_patched";
            var v = mesh.vertices;
            var weld = Weld(v);
            int flipped = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tris = mesh.GetTriangles(s);
                int count = tris.Length / 3;
                // Propagate one winding across shared edges: a neighbour that runs the shared edge the same way is flipped.
                var byEdge = new Dictionary<long, List<int>>();
                for (int f = 0; f < count; f++)
                    for (int e = 0; e < 3; e++)
                    {
                        long k = Key(weld[tris[f * 3 + e]], weld[tris[f * 3 + (e + 1) % 3]]);
                        if (!byEdge.TryGetValue(k, out var list)) byEdge[k] = list = new List<int>();
                        list.Add(f);
                    }
                var seen = new bool[count];
                for (int startFace = 0; startFace < count; startFace++)
                {
                    if (seen[startFace]) continue;
                    var component = new List<int>();
                    var queue = new Queue<int>(); queue.Enqueue(startFace); seen[startFace] = true;
                    while (queue.Count > 0)
                    {
                        int f = queue.Dequeue(); component.Add(f);
                        for (int e = 0; e < 3; e++)
                        {
                            int a = weld[tris[f * 3 + e]], b = weld[tris[f * 3 + (e + 1) % 3]];
                            foreach (int g in byEdge[Key(a, b)])
                            {
                                if (g == f || seen[g]) continue;
                                bool same = false;
                                for (int q = 0; q < 3; q++)
                                    if (weld[tris[g * 3 + q]] == a && weld[tris[g * 3 + (q + 1) % 3]] == b) same = true;
                                if (same) { (tris[g * 3 + 1], tris[g * 3 + 2]) = (tris[g * 3 + 2], tris[g * 3 + 1]); flipped++; }
                                seen[g] = true; queue.Enqueue(g);
                            }
                        }
                    }
                    // A closed piece must enclose positive volume; otherwise the whole piece is inside out.
                    float volume = 0;
                    foreach (int f in component) volume += Vector3.Dot(v[tris[f * 3]], Vector3.Cross(v[tris[f * 3 + 1]], v[tris[f * 3 + 2]]));
                    // Unity's face normal is Cross(b - a, c - a), so an outward-wound piece has positive signed volume.
                    if (volume < 0)
                        foreach (int f in component) { (tris[f * 3 + 1], tris[f * 3 + 2]) = (tris[f * 3 + 2], tris[f * 3 + 1]); flipped++; }
                }
                mesh.SetTriangles(tris, s);
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            log.AppendLine(string.Format("{0}: re-wound {1} face flip(s)", source.name, flipped));
            return mesh;
        }

        static int OpenEdges(Mesh mesh)
        {
            var weld = Weld(mesh.vertices);
            var use = new Dictionary<long, int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tris = mesh.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3)
                    for (int e = 0; e < 3; e++) { long k = Key(weld[tris[i + e]], weld[tris[i + (e + 1) % 3]]); use[k] = use.TryGetValue(k, out int u) ? u + 1 : 1; }
            }
            return use.Count(p => p.Value == 1);
        }
    }
}
