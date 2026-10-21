using System.IO;
using System.Linq;
using BorderRepair.RepairProps;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // Imports ArtSource/SurgeryTableV2 output (copied to Assets/RepairProps/Surgery/Art) and writes
    // Assets/RepairProps/Surgery/Resources/SurgeryTableV2.prefab. Shared M_BV2_* slots are remapped onto the repair
    // bench's materials (build the bench first); M_ST_* materials are created here. SurgeryTableV2Runtime places the
    // prefab at play time; no scene is touched.
    public static class SurgeryTableV2Builder
    {
        const string Root = "Assets/RepairProps/Surgery";
        const string Model = Root + "/Art/SurgeryTableV2.fbx";
        const string TextureDir = Root + "/Art/Textures";
        const string MaterialDir = Root + "/Materials";
        const string BenchMaterials = "Assets/RepairProps/Bench/Materials";
        const string PrefabPath = Root + "/Resources/SurgeryTableV2.prefab";

        static readonly (string name, string texture, float metallic, float smoothness, Color flat)[] Own =
        {
            ("M_ST_Vinyl", "T_ST_Vinyl", 0, .45f, Color.white),
            ("M_ST_Hazard", "T_ST_Hazard", 0, .3f, Color.white),
            ("M_ST_Label", "T_ST_Label", 0, .1f, Color.white),
            ("M_ST_Bag", null, 0, .8f, new Color(.85f, .88f, .82f)),
            ("M_ST_Screen", "T_ST_Screen", 0, .8f, Color.white),   // green-phosphor patient monitor (emissive, below)
        };

        [MenuItem("Border Repair/Repair Props/Build Surgery Table V2")]
        public static void Build()
        {
            if (!File.Exists(Model)) throw new FileNotFoundException("Copy ArtSource/SurgeryTableV2/Export/SurgeryTableV2.fbx to " + Model + " first.");
            if (!Directory.Exists(BenchMaterials)) throw new DirectoryNotFoundException("Build the repair bench V2 first; its materials are shared.");
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(TextureDir, "*.png").Select(p => p.Replace('\\', '/')))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.sRGBTexture = true; importer.mipmapEnabled = true; importer.filterMode = FilterMode.Point;
                importer.maxTextureSize = 512; importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var model = (ModelImporter)AssetImporter.GetAtPath(Model);
            model.globalScale = 1; model.useFileScale = true;
            model.importAnimation = false; model.importCameras = false; model.importLights = false; model.isReadable = true;
            model.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var spec in Own)
            {
                string path = MaterialDir + "/" + spec.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                material.SetTexture("_BaseMap", spec.texture != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/" + spec.texture + ".png") : null);
                material.SetColor("_BaseColor", spec.flat);
                material.SetFloat("_Metallic", spec.metallic);
                material.SetFloat("_Smoothness", spec.smoothness);
                if (spec.name == "M_ST_Screen")
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetTexture("_EmissionMap", material.GetTexture("_BaseMap"));
                    material.SetColor("_EmissionColor", new Color(1.4f, 1.6f, 1.4f));
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                EditorUtility.SetDirty(material);
                model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), spec.name), material);
            }
            model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_SurgeryTableV2_Atlas"), V2AtlasMaterial.Ensure("SurgeryTableV2", TextureDir, MaterialDir));
            foreach (var path in Directory.GetFiles(BenchMaterials, "M_BV2_*.mat").Select(p => p.Replace('\\', '/')))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
            }
            model.SaveAndReimport();

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "SurgeryTableV2";
            Transform Find(string name) => instance.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
            Find("BST_Pendant").gameObject.AddComponent<SurgeryTableControl>().Configure(SurgeryControlKind.Pendant, "手术台控制器");
            Find("BST_Tray").gameObject.AddComponent<SurgeryTableControl>().Configure(SurgeryControlKind.Tray, "器械托盘");
            for (int i = 1; i <= 3; i++) Find("BST_Strap_" + i).gameObject.AddComponent<SurgeryTableControl>().Configure(SurgeryControlKind.Strap, "束带 " + i);
            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            Debug.Log("[SurgeryTableV2] built " + PrefabPath);
        }
    }
}
