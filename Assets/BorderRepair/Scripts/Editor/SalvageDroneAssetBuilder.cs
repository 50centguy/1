using System;
using System.Collections.Generic;
using System.IO;
using BorderRepair.Inspection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 接入 Blender 导出的回收无人机（ArtSource/SalvageDrone/build_salvagedrone.py），生成独立的最终 prefab，
    /// 并让 Case_03_SalvageDrone 引用它。原占位 prefab、通讯器和导航信标资产不受影响。
    /// 命令行：-executeMethod BorderRepair.EditorTools.SalvageDroneAssetBuilder.BuildFromCommandLine
    /// </summary>
    public static class SalvageDroneAssetBuilder
    {
        const string ArtDir = "Assets/BorderRepair/Art/SalvageDrone";
        const string ModelDir = ArtDir + "/Models";
        const string TextureDir = ArtDir + "/Textures";
        const string MaterialDir = ArtDir + "/Materials";
        const string ManifestPath = ArtDir + "/SalvageDrone_Materials.json";
        const string CasePath = PrototypeBuilder.CaseDir + "/Case_03_SalvageDrone.asset";
        public const string PrefabPath = "Assets/BorderRepair/Prefabs/Items/Item_SalvageDrone_Final.prefab";

        static readonly string[] Groups = { "Body", "MotorFL", "Mainboard", "Rotor", "Camera" };

        static readonly (string pointId, string group, float padding)[] Points =
        {
            ("motor_fl", "MotorFL", 0.003f),
            ("mainboard", "Mainboard", 0.002f),
            ("rotor", "Rotor", 0.003f),
            ("camera", "Camera", 0.002f),
        };

        static string Fbx(string group) => $"{ModelDir}/SalvageDrone_{group}.fbx";

        [MenuItem("Border Repair/Assets/Build SalvageDrone (from Blender export)", priority = 42)]
        static void MenuBuild() => Build();

        public static void BuildFromCommandLine() => Build();

        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play 模式。");
            AssetDatabase.Refresh();

            var manifest = ItemAssetPipeline.LoadManifest(ManifestPath);
            ItemAssetPipeline.ConfigureTextures(TextureDir, "Grime");
            var materials = ItemAssetPipeline.BuildMaterials(manifest, MaterialDir, TextureDir);
            foreach (var g in Groups) ItemAssetPipeline.ConfigureModel(Fbx(g), materials);

            var prefab = BuildPrefab();
            ItemAssetPipeline.AssignCasePrefab(CasePath, prefab);
            AssetDatabase.SaveAssets();
            LogStats(materials);
            Debug.Log($"[BorderRepair] 回收无人机正式资产已接入：{PrefabPath}");
        }

        static GameObject BuildPrefab()
        {
            var root = new GameObject("Item_SalvageDrone_Final");
            try
            {
                var body = ItemAssetPipeline.InstantiateModel(Fbx("Body"), root.transform, "Model_Body");
                var bodyColliders = new GameObject("BodyColliders").transform;
                bodyColliders.SetParent(root.transform, false);
                ItemAssetPipeline.AddMeshColliders(body, bodyColliders, "BodyCollider");

                foreach (var (pointId, group, padding) in Points)
                {
                    var go = new GameObject("Point_" + pointId);
                    go.transform.SetParent(root.transform, false);
                    var point = go.AddComponent<InspectionPoint>();
                    ItemAssetPipeline.InstantiateModel(Fbx(group), go.transform, "Model_" + group);
                    point.Configure(pointId, null, null);
                    ItemAssetPipeline.FitBoxCollider(go, padding);
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
                if (!ok) throw new IOException("prefab 保存失败：" + PrefabPath);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void LogStats(Dictionary<string, Material> materials)
        {
            var parts = new List<string>();
            long total = 0;
            foreach (var g in Groups)
            {
                long t = ItemAssetPipeline.CountTriangles(Fbx(g));
                total += t;
                parts.Add($"{g}={t}");
            }
            Debug.Log($"[BorderRepair] 回收无人机 Unity 导入统计：三角面 {string.Join(", ", parts)}；合计={total}；" +
                      $"材质 {materials.Count} 个；贴图 {ItemAssetPipeline.DescribeTextures(TextureDir)}");
        }
    }
}
