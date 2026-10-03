using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using static BorderRepair.ArtAudit.AuditCommon;

namespace BorderRepair.ArtAudit
{
    /// <summary>
    /// 第 1 步（只读）：打开布局 B 场景（不保存），导出七号、维修座、工作台的完整层级：
    /// 路径、父对象、本地 / 世界位姿、骨骼局部 X 轴（= 关节转轴）的世界方向、渲染器类型、材质、包围盒、静态标记、组件；
    /// 蒙皮件的骨骼、每根骨骼的权重分布（顶点数、权重 > 0.99 的顶点、混合区、各簇的世界中心和端点）；
    /// RobotV4 FBX 里的动画片段和它们驱动的骨骼。输出 raw_hierarchy_B.json。
    /// </summary>
    public static class AuditDump
    {
        public static void Run()
        {
            OpenSceneB();
            var sb = new StringBuilder();
            sb.Append("{\"scene\":").Append(S(SceneB)).Append(",\"unity\":").Append(S(Application.unityVersion)).Append(",\"roots\":[");
            bool first = true;
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!first) sb.Append(","); first = false;
                sb.Append("{\"root\":").Append(S(root.name)).Append(",\"nodes\":[");
                bool f2 = true;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!f2) sb.Append(","); f2 = false;
                    Node(sb, t);
                }
                sb.Append("]}");
            }
            sb.Append("],\"clips\":[");
            var clips = AssetDatabase.LoadAllAssetsAtPath(RobotFbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
            for (int i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                var paths = AnimationUtility.GetCurveBindings(c).Select(b => b.path + ":" + b.propertyName.Split('.')[0]).Distinct().OrderBy(x => x).ToList();
                if (i > 0) sb.Append(",");
                sb.Append("{\"name\":").Append(S(c.name)).Append(",\"length\":").Append(F(c.length)).Append(",\"fps\":").Append(F(c.frameRate))
                  .Append(",\"loop\":").Append(c.isLooping ? "true" : "false")
                  .Append(",\"events\":[").Append(string.Join(",", c.events.Select(e => S(e.functionName + "@" + F(e.time))))).Append("]")
                  .Append(",\"bindings\":[").Append(string.Join(",", paths.Select(S))).Append("]}");
            }
            sb.Append("]}");
            Write("raw_hierarchy_B.json", sb.ToString());
            Debug.Log("[ArtAudit] raw_hierarchy_B.json 已写出");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void Node(StringBuilder sb, Transform t)
        {
            sb.Append("{\"path\":").Append(S(PathOf(t)))
              .Append(",\"name\":").Append(S(t.name))
              .Append(",\"parent\":").Append(S(t.parent ? t.parent.name : null))
              .Append(",\"active\":").Append(t.gameObject.activeInHierarchy ? "true" : "false")
              .Append(",\"static\":").Append(S(GameObjectUtility.GetStaticEditorFlags(t.gameObject).ToString()))
              .Append(",\"lp\":").Append(V(t.localPosition)).Append(",\"lr\":").Append(V(t.localEulerAngles)).Append(",\"ls\":").Append(V(t.localScale))
              .Append(",\"wp\":").Append(V(t.position)).Append(",\"wq\":").Append(Q(t.rotation))
              .Append(",\"wx\":").Append(V(t.right)).Append(",\"wy\":").Append(V(t.up)).Append(",\"wz\":").Append(V(t.forward))
              .Append(",\"lossy\":").Append(V(t.lossyScale));
            var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToList();
            sb.Append(",\"comps\":[").Append(string.Join(",", comps.Select(S))).Append("]");
            var prefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
            if (prefab != null) sb.Append(",\"source\":").Append(S(AssetDatabase.GetAssetPath(prefab)));
            var r = t.GetComponent<Renderer>();
            if (r != null)
            {
                sb.Append(",\"renderer\":{\"type\":").Append(S(r.GetType().Name))
                  .Append(",\"enabled\":").Append(r.enabled ? "true" : "false")
                  .Append(",\"mats\":[").Append(string.Join(",", r.sharedMaterials.Select(m => S(m ? m.name : null)))).Append("]")
                  .Append(",\"wb_min\":").Append(V(r.bounds.min)).Append(",\"wb_max\":").Append(V(r.bounds.max));
                Mesh mesh = r is SkinnedMeshRenderer sk0 ? sk0.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh != null)
                {
                    sb.Append(",\"mesh\":").Append(S(mesh.name)).Append(",\"mesh_asset\":").Append(S(AssetDatabase.GetAssetPath(mesh)))
                      .Append(",\"verts\":").Append(mesh.vertexCount).Append(",\"tris\":").Append(mesh.triangles.Length / 3)
                      .Append(",\"lb_min\":").Append(V(mesh.bounds.min)).Append(",\"lb_max\":").Append(V(mesh.bounds.max));
                }
                if (r is SkinnedMeshRenderer sk && mesh != null) Skin(sb, sk, mesh);
                sb.Append("}");
            }
            var col = t.GetComponents<Collider>();
            if (col.Length > 0)
                sb.Append(",\"colliders\":[").Append(string.Join(",", col.Select(c => S(c.GetType().Name + (c.isTrigger ? "(trigger)" : "") + (c.enabled ? "" : "(off)"))))).Append("]");
            sb.Append("}");
        }

        /// <summary>蒙皮：每根骨骼的权重簇（按顶点最大权重归类），混合区顶点数，各簇的世界中心和沿最长轴的两端。</summary>
        static void Skin(StringBuilder sb, SkinnedMeshRenderer sk, Mesh mesh)
        {
            var bones = sk.bones;
            sb.Append(",\"rootBone\":").Append(S(sk.rootBone ? PathOf(sk.rootBone) : null))
              .Append(",\"bones\":[").Append(string.Join(",", bones.Select(b => S(b ? PathOf(b) : null)))).Append("]");
            var baked = new Mesh(); sk.BakeMesh(baked, true);
            var mtx = Matrix4x4.TRS(sk.transform.position, sk.transform.rotation, Vector3.one);
            var wv = baked.vertices.Select(p => mtx.MultiplyPoint3x4(p)).ToArray();
            var bw = mesh.boneWeights;
            var groups = new Dictionary<int, List<int>>();
            int blended = 0;
            var blendIdx = new List<int>();
            for (int i = 0; i < bw.Length; i++)
            {
                var w = bw[i];
                int top = w.boneIndex0; float wt = w.weight0;
                if (w.weight1 > wt) { top = w.boneIndex1; wt = w.weight1; }
                if (!groups.TryGetValue(top, out var l)) groups[top] = l = new List<int>();
                l.Add(i);
                if (wt < 0.99f) { blended++; blendIdx.Add(i); }
            }
            sb.Append(",\"blended_verts\":").Append(blended).Append(",\"weight_groups\":[");
            bool first = true;
            foreach (var kv in groups)
            {
                var pts = kv.Value.Select(i => wv[i]).ToList();
                int pure = kv.Value.Count(i => Mathf.Max(bw[i].weight0, bw[i].weight1) >= 0.99f);
                var c = pts.Aggregate(Vector3.zero, (a, p) => a + p) / pts.Count;
                var far = pts.OrderByDescending(p => (p - c).sqrMagnitude).First();
                var far2 = pts.OrderByDescending(p => (p - far).sqrMagnitude).First();
                if (!first) sb.Append(","); first = false;
                sb.Append("{\"bone\":").Append(S(kv.Key < bones.Length && bones[kv.Key] ? bones[kv.Key].name : "?"))
                  .Append(",\"verts\":").Append(pts.Count).Append(",\"pure\":").Append(pure)
                  .Append(",\"center\":").Append(V(c)).Append(",\"endA\":").Append(V(far)).Append(",\"endB\":").Append(V(far2)).Append("}");
            }
            sb.Append("]");
            if (blendIdx.Count > 0)
            {
                var bp = blendIdx.Select(i => wv[i]).ToList();
                var bmin = bp.Aggregate(Vector3.Min); var bmax = bp.Aggregate(Vector3.Max);
                sb.Append(",\"blend_min\":").Append(V(bmin)).Append(",\"blend_max\":").Append(V(bmax));
            }
        }
    }
}
