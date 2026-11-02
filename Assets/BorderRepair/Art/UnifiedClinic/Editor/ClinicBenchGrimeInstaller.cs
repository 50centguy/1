using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BorderRepair.Art.UnifiedClinic.EditorTools
{
    // 工作台上的助焊剂残迹。台面用的是工作区那套 M_WB_* 材质，不能为了几滴脏东西去改它，
    // 所以这里按台面的真实边界生成一组静态贴花 Quad：几滩平放的、几道从台沿垂下来的。
    // 贴花不可清洁，属于布景；正式游玩时它们是玩家"这活儿干过很多次"的证据。
    // 批处理入口：-executeMethod BorderRepair.Art.UnifiedClinic.EditorTools.ClinicBenchGrimeInstaller.RunBatch
    public static class ClinicBenchGrimeInstaller
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        const string MaterialFolder = "Assets/RepairProps/Art/Materials";
        const string PoolMaterialPath = MaterialFolder + "/M_BenchGrime.mat";
        const string RunMaterialPath = MaterialFolder + "/M_BenchGrime_Run.mat";
        const string RootName = "ClinicBenchGrime";
        const string ShaderName = "BorderRepair/BenchGrime";
        const string ReportPath = "Docs/Integration/UnifiedClinic/Shell/bench_grime.txt";

        // 台面坐标是归一化的（0..1 跨过台面长度/宽度），一次写好确定下来，重跑结果完全一样。
        static readonly Vector3[] Pools =
        {
            new Vector3(.30f, .42f, .34f),   // 焊接角的主滩
            new Vector3(.44f, .55f, .19f),
            new Vector3(.22f, .62f, .23f),
            new Vector3(.63f, .38f, .15f),
        };

        static readonly Vector2[] Runs =
        {
            new Vector2(.34f, .30f),
            new Vector2(.52f, .22f),
        };

        public static void RunBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid()) { Debug.LogError("[BenchGrime] cannot open " + ScenePath); return; }

            var shader = Shader.Find(ShaderName);
            if (shader == null) { Debug.LogError("[BenchGrime] shader not found: " + ShaderName); return; }

            var top = FindByName(scene, "Bench_Top") ?? FindByName(scene, "RP_Bench");
            if (top == null) { Debug.LogError("[BenchGrime] no bench top in the scene."); return; }
            var surface = top.GetComponent<Renderer>();
            if (surface == null) surface = top.GetComponentInChildren<Renderer>();
            if (surface == null) { Debug.LogError("[BenchGrime] bench top has no renderer."); return; }
            var b = surface.bounds;

            var report = new StringBuilder();
            report.AppendLine("bench flux grime, generated from " + ScenePath);
            report.AppendLine(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "bench top '{0}' bounds centre {1} size {2}",
                surface.name, b.center.ToString("F3"), b.size.ToString("F3")));

            // 重跑时整组重建，避免残留旧位置（这条路径只有本安装器写，安全）
            var existing = FindByName(scene, RootName);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

            // 一份材质一种形态。MaterialPropertyBlock 不会存进场景，用它设 _Mode 的话重开工程
            // 两道"淌"的就会按"滩"画出来 —— 所以形态必须落在材质资产上。
            var poolMaterial = EnsureMaterial(shader, PoolMaterialPath, "M_BenchGrime", 0f, 0f);
            var runMaterial = EnsureMaterial(shader, RunMaterialPath, "M_BenchGrime_Run", 1f, 7.3f);

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            float topY = b.max.y + .0015f;
            for (int i = 0; i < Pools.Length; i++)
            {
                var pool = Pools[i];
                float x = Mathf.Lerp(b.min.x, b.max.x, pool.x);
                float z = Mathf.Lerp(b.min.z, b.max.z, pool.y);
                float size = pool.z;
                MakeDecal(root.transform, poolMaterial, "Grime_Pool_" + i,
                    new Vector3(x, topY, z), Quaternion.Euler(90f, PoolYaw(x, z), 0f), new Vector3(size, size, 1f));
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  pool  at ({0:F3},{1:F3}) size {2:F2} m", x, z, size));
            }

            for (int i = 0; i < Runs.Length; i++)
            {
                var run = Runs[i];
                float x = Mathf.Lerp(b.min.x, b.max.x, run.x);
                float height = Mathf.Lerp(.16f, .34f, run.y);
                MakeDecal(root.transform, runMaterial, "Grime_Run_" + i,
                    new Vector3(x, b.max.y - height * .5f, b.min.z - .0015f), Quaternion.identity,
                    new Vector3(.075f, height, 1f));
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  run   at x {0:F3}, height {1:F2} m, on the -Z edge (z {2:F3})", x, height, b.min.z));
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            report.AppendLine("decal quads: " + root.transform.childCount);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log("[BenchGrime] " + root.transform.childCount + " decals" + Environment.NewLine + report);
        }

        // 每次随机数都用固定公式算，重跑不会挪位置。
        static float PoolYaw(float x, float z) => Mathf.Repeat(Mathf.Abs(x * 137.1f + z * 71.3f), 360f);

        // 材质只建一次：重跑时保留美术在 Inspector 里调过的颜色和不透明度，只把形态与种子钉回去。
        static Material EnsureMaterial(Shader shader, string path, string name, float mode, float seed)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetFloat("_Mode", mode);
            material.SetFloat("_Seed", seed);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        static GameObject MakeDecal(Transform parent, Material material, string name,
                                    Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            // 贴花不能参与碰撞：清洁与拾取的射线都在这张台上打，多一个碰撞体会毁掉整个交互
            var collider = quad.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.position = position;
            quad.transform.rotation = rotation;
            quad.transform.localScale = scale;
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return quad;
        }

        static Transform FindByName(UnityEngine.SceneManagement.Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t;
            return null;
        }
    }
}
