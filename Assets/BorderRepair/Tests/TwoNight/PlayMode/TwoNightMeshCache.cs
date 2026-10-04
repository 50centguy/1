using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.TwoNight.Tests
{
    /// <summary>
    /// 动作探针用的网格缓存。编辑器 PlayMode 里：不可读网格的顶点读不出来；静态合批的物体，MeshFilter 被换成了合并网格。
    /// 所以进入播放前（编辑模式，IPrebuildSetup）以只读方式附加打开七号场景，把每个 MeshFilter 的原始网格（本地坐标）
    /// 按层级路径（名字 + 同名兄弟序号）写进项目 Temp/，然后关掉该场景、不保存；播放时按同一路径取。不改导入设置，不改场景。
    /// </summary>
    public class TwoNightMeshCache : IPrebuildSetup
    {
        const string FilePath = "Temp/TwoNightMeshCache.bin";
        static Dictionary<string, (Vector3[] v, int[] t)> loaded, byName;

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent)
            {
                // 名字 + 同名兄弟里的序号（运行时新建的物体名字不同，不影响；根物体的场景顺序在附加打开时会变，所以不用总序号）
                int same = 0;
                if (t.parent != null) for (int i = 0; i < t.GetSiblingIndex(); i++) { if (t.parent.GetChild(i).name == t.name) same++; }
                else foreach (var g in t.gameObject.scene.GetRootGameObjects()) { if (g.transform == t) break; if (g.name == t.name) same++; }
                parts.Add(t.name + "#" + same);
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        static string Leaf(string key) { var last = key.Substring(key.LastIndexOf('/') + 1); return last.Substring(0, last.LastIndexOf('#')); }

        public void Setup()
        {
#if UNITY_EDITOR
            var path = "Assets/BorderRepair/Scenes/Slice/" + TwoNightScenes.Robot + ".unity";
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                using (var w = new BinaryWriter(File.Create(FilePath)))
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                        {
                            if (mf.sharedMesh == null) continue;
                            var v = mf.sharedMesh.vertices; var t = mf.sharedMesh.triangles;
                            w.Write(PathOf(mf.transform)); w.Write(v.Length); foreach (var p in v) { w.Write(p.x); w.Write(p.y); w.Write(p.z); }
                            w.Write(t.Length); foreach (var i in t) w.Write(i);
                        }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
#endif
        }

        /// <summary>挂到 MeshClearance.PlayingReader。</summary>
        public static void Install()
        {
            if (loaded == null)
            {
                loaded = new Dictionary<string, (Vector3[], int[])>();
                using (var r = new BinaryReader(File.OpenRead(FilePath)))
                    while (r.BaseStream.Position < r.BaseStream.Length)
                    {
                        var k = r.ReadString();
                        var v = new Vector3[r.ReadInt32()]; for (int i = 0; i < v.Length; i++) v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        var t = new int[r.ReadInt32()]; for (int i = 0; i < t.Length; i++) t[i] = r.ReadInt32();
                        loaded[k] = (v, t);
                    }
                // 运行时会换父对象的物体（托盘进右手挂点）：按唯一的物体名再找一次
                byName = loaded.GroupBy(kv => Leaf(kv.Key)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First().Value);
            }
            MeshClearance.PlayingReader = r => loaded.TryGetValue(PathOf(r.transform), out var d) || byName.TryGetValue(r.name, out d) ? d : throw new KeyNotFoundException("网格缓存里没有 " + PathOf(r.transform));
        }
    }
}
