using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BorderRepair.Art.UnifiedClinic.EditorTools
{
    /// <summary>
    /// 统一诊所观感：美术说明（ArtSource/UnifiedClinic/README.md）写明“灯光只在 Blender 渲染里做，Unity 里的灯没有制作；
    /// 交易台状态灯和屏幕需要程序驱动发光”。这里补上 Unity 一侧：
    /// 1) 发光材质（屏幕、灯管）设为实时发光——否则 URP 校验材质时按“发光视为黑色”把 _EMISSION 关掉，屏幕和灯管不亮；
    /// 2) 在灯管、门灯、七号笼灯的位置放柔和的实时点光（按灯管网格的分段位置算）；
    /// 3) 环境光略微提高，暗部不再压成纯黑（保留暗调）；
    /// 4) 台上维修用的检视相机开后处理（和房间一致）。状态灯亮灭 / 闪烁、检视灯开关由运行时的 UnifiedClinicDirector 负责。
    /// 可重复执行（先删掉自己上次加的灯）。重建诊所时由 ClinicGameplayIntegrator 调用。
    /// </summary>
    public static class ClinicLookTuning
    {
        public const string FixtureRoot = "ClinicFixtureLights（灯具补光，程序生成）";
        static readonly Color Warm = new Color(1f, .78f, .55f);

        [MenuItem("Border Repair/Unified Clinic/Apply Lighting And Emission Tuning")]
        public static void ApplyToClinicScene()
        {
            var scene = EditorSceneManager.OpenScene(TwoNight.EditorTools.UnifiedClinicBuilder.ClinicPath, OpenSceneMode.Single);
            Debug.Log("[ClinicLook] " + Apply(scene));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static void ApplyBatch()
        {
            try { ApplyToClinicScene(); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static string Apply(Scene scene)
        {
            var log = new StringBuilder();
            log.AppendLine(FixEmissiveMaterials());
            var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == FixtureRoot);
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(FixtureRoot).transform;
            SceneManager.MoveGameObjectToScene(root.gameObject, scene);
            var renderers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToList();
            Renderer Named(string n) => renderers.FirstOrDefault(r => r.name == n);

            // 墙上灯管：一整块网格，按顶点分段，每段一盏
            var tubes = Named("Shell_LampTubes");
            if (tubes == null) log.AppendLine("找不到 Shell_LampTubes");
            int n = 0;
            if (tubes != null)
                foreach (var b in Islands(tubes.GetComponent<MeshFilter>().sharedMesh, tubes.transform, .12f).Where(b => b.size.magnitude > .2f))
                {
                    // 光源放在灯管下方、朝房间中心偏 0.15 m，免得贴墙只照亮墙面
                    var toCenter = Vector3.ProjectOnPlane(new Vector3(0f, b.center.y, 0f) - b.center, Vector3.up).normalized;
                    AddPoint(root, $"Fixture_Tube_{++n}", b.center + toCenter * .15f + Vector3.down * .08f, Warm, .75f, 3.2f);
                    log.AppendLine($"灯管 {n}：中心 {b.center:F2}，长 {Mathf.Max(b.size.x, b.size.z):F2} m");
                }
            var door = Named("Door_Lamp");
            if (door != null) AddPoint(root, "Fixture_Door", door.bounds.center + Vector3.forward * .25f + Vector3.down * .1f, Warm, .6f, 2.6f);
            // 维修台：原来只有台灯一盏小聚光（美术渲染里维修台是房间里最亮的区域之一）；台面上方加一盏柔和补光，照到洞洞板和两侧
            var bench = renderers.FirstOrDefault(r => r.name == "Bench_Top");
            if (bench != null) AddPoint(root, "Fixture_BenchFill", new Vector3(bench.bounds.center.x, bench.bounds.max.y + 1.05f, bench.bounds.center.z - .15f), Warm, .9f, 2.3f);
            else log.AppendLine("找不到 Bench_Top");
            var cage = Named("Unit07_CageLamp");
            if (cage != null) AddPoint(root, "Fixture_Unit07Cage", cage.bounds.center + Vector3.down * .05f, Warm, .45f, 1.6f);

            // 环境光：只把纯黑托起来一点
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.13f, .15f, .15f);
            RenderSettings.ambientEquatorColor = new Color(.075f, .085f, .085f);
            RenderSettings.ambientGroundColor = new Color(.04f, .04f, .04f);
            log.AppendLine("环境光 Trilight：天空 0.13/0.15/0.15，中间 0.075/0.085/0.085，地面 0.04");

            // 台上维修的检视相机：后处理和房间相机一致
            var room = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(c => c.CompareTag("MainCamera"));
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(c => c != room && c.transform.parent != null && c.transform.parent.name == "ClinicInspectionStage"))
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = room == null || room.GetUniversalAdditionalCameraData().renderPostProcessing;
                EditorUtility.SetDirty(data);
                log.AppendLine("检视相机后处理：" + data.renderPostProcessing);
            }
            return log.ToString();
        }

        /// <summary>美术导入的发光材质：设为实时发光并打开 _EMISSION（URP 会按“发光视为黑色”关掉关键字）。</summary>
        public static string FixEmissiveMaterials()
        {
            var sb = new StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/BorderRepair/Art/UnifiedClinic" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || !m.HasProperty("_EmissionColor")) continue;
                var e = m.GetColor("_EmissionColor");
                if (e.maxColorComponent <= 0f) continue;
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(m);
                sb.Append(m.name).Append(' ');
            }
            AssetDatabase.SaveAssets();
            return "发光材质（实时发光 + _EMISSION）：" + sb;
        }

        static void AddPoint(Transform root, string name, Vector3 position, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = position;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = color; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None; l.lightmapBakeType = LightmapBakeType.Realtime;
        }

        /// <summary>网格按顶点连通（网格格子 cell 米）分成几段，返回每段的世界包围盒。</summary>
        static List<Bounds> Islands(Mesh mesh, Transform t, float cell)
        {
            Vector3[] local; var tris = new List<int>();
            using (var data = Mesh.AcquireReadOnlyMeshData(mesh))   // 模型未开放读取（isReadable = false）时 mesh.vertices 是空的；只读网格数据在编辑器里可以读
            {
                var arr = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp);
                data[0].GetVertices(arr); local = arr.ToArray(); arr.Dispose();
                for (int sub = 0; sub < data[0].subMeshCount; sub++)
                {
                    var idx = new Unity.Collections.NativeArray<int>(data[0].GetSubMesh(sub).indexCount, Unity.Collections.Allocator.Temp);
                    data[0].GetIndices(idx, sub); tris.AddRange(idx); idx.Dispose();
                }
            }
            var verts = local.Select(v => t.TransformPoint(v)).ToArray();
            var parent = Enumerable.Range(0, verts.Length).ToArray();
            int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
            var cells = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < verts.Length; i++)
            {
                var c = Vector3Int.FloorToInt(verts[i] / cell);
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                    if (cells.TryGetValue(c + new Vector3Int(dx, dy, dz), out var j)) parent[Find(i)] = Find(j);
                cells[c] = i;
            }
            for (int k = 0; k + 2 < tris.Count; k += 3) { parent[Find(tris[k])] = Find(tris[k + 1]); parent[Find(tris[k + 1])] = Find(tris[k + 2]); }   // 同一个三角形的顶点属于同一段（细长灯管的两端顶点相距很远）
            var groups = new Dictionary<int, Bounds>();
            for (int i = 0; i < verts.Length; i++)
            {
                int r = Find(i);
                if (groups.TryGetValue(r, out var b)) { b.Encapsulate(verts[i]); groups[r] = b; }
                else groups[r] = new Bounds(verts[i], Vector3.zero);
            }
            return groups.Values.ToList();
        }
    }
}
