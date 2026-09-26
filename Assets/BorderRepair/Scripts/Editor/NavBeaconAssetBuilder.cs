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
    /// 接入 Blender 导出的导航信标（ArtSource/NavBeacon/build_navbeacon.py），生成独立的最终 prefab，
    /// 并让 Case_02_NavBeacon 引用它。原占位 prefab 与通讯器资产不受影响。
    /// 命令行：-executeMethod BorderRepair.EditorTools.NavBeaconAssetBuilder.BuildFromCommandLine
    /// </summary>
    public static class NavBeaconAssetBuilder
    {
        const string ArtDir = "Assets/BorderRepair/Art/NavBeacon";
        const string ModelDir = ArtDir + "/Models";
        const string TextureDir = ArtDir + "/Textures";
        const string MaterialDir = ArtDir + "/Materials";
        const string ManifestPath = ArtDir + "/NavBeacon_Materials.json";
        const string CasePath = PrototypeBuilder.CaseDir + "/Case_02_NavBeacon.asset";
        public const string PrefabPath = "Assets/BorderRepair/Prefabs/Items/Item_NavBeacon_Final.prefab";

        static readonly string[] Groups = { "Body", "Seal", "Port", "Lamp", "Mast" };

        // pointId → (FBX 分组, 碰撞体外扩)；天线较细，外扩更多，便于点击
        static readonly (string pointId, string group, float padding)[] Points =
        {
            ("seal", "Seal", 0.002f),
            ("port", "Port", 0.002f),
            ("lamp", "Lamp", 0.002f),
            ("mast", "Mast", 0.004f),
        };

        static string Fbx(string group) => $"{ModelDir}/NavBeacon_{group}.fbx";

        [MenuItem("Border Repair/Assets/Build NavBeacon (from Blender export)", priority = 41)]
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
            Debug.Log($"[BorderRepair] 导航信标正式资产已接入：{PrefabPath}");
        }

        static GameObject BuildPrefab()
        {
            var root = new GameObject("Item_NavBeacon_Final");
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
            Debug.Log($"[BorderRepair] 导航信标 Unity 导入统计：三角面 {string.Join(", ", parts)}；合计={total}；" +
                      $"材质 {materials.Count} 个；贴图 {ItemAssetPipeline.DescribeTextures(TextureDir)}");
        }
    }
}
