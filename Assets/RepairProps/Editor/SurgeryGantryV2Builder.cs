using System.IO;
using System.Linq;
using BorderRepair.RepairProps;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // Imports ArtSource/SurgeryGantryV2 output (copied to Assets/RepairProps/Surgery/Art) and writes
    // Assets/RepairProps/Surgery/Resources/SurgeryGantryV2.prefab. M_BV2_* slots are remapped onto the repair bench's
    // materials and M_ST_* onto the surgery table's (build both first); M_GT_* are created here. SurgeryGantryV2Runtime
    // places the prefab at play time.
    public static class SurgeryGantryV2Builder
    {
        const string Root = "Assets/RepairProps/Surgery";
        const string Model = Root + "/Art/SurgeryGantryV2.fbx";
        const string TextureDir = Root + "/Art/Textures";
        const string MaterialDir = Root + "/Materials";
        const string PrefabPath = Root + "/Resources/SurgeryGantryV2.prefab";
        static readonly string[] SharedMaterialDirs = { "Assets/RepairProps/Bench/Materials", MaterialDir };

        [MenuItem("Border Repair/Repair Props/Build Surgery Gantry V2")]
        public static void Build()
        {
            if (!File.Exists(Model)) throw new FileNotFoundException("Copy ArtSource/SurgeryGantryV2/Export/SurgeryGantryV2.fbx to " + Model + " first.");
            if (!File.Exists(MaterialDir + "/M_ST_Hazard.mat")) throw new FileNotFoundException("Build the repair bench V2 and surgery table V2 first; their materials are shared.");
            AssetDatabase.Refresh();
            const string dark = TextureDir + "/T_GT_DarkSteel.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(dark);
            importer.sRGBTexture = true; importer.mipmapEnabled = true; importer.maxTextureSize = 512; importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Material Make(string name)
            {
                string path = MaterialDir + "/" + name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                return material;
            }
            var darkSteel = Make("M_GT_DarkSteel");
            darkSteel.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dark));
            darkSteel.SetColor("_BaseColor", Color.white); darkSteel.SetFloat("_Metallic", .6f); darkSteel.SetFloat("_Smoothness", .35f);
            var lens = Make("M_GT_Lens");
            lens.SetColor("_BaseColor", new Color(.3f, .8f, .9f)); lens.SetFloat("_Smoothness", .9f);
            lens.EnableKeyword("_EMISSION"); lens.SetColor("_EmissionColor", new Color(.3f, 1.6f, 2f));
            lens.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(darkSteel); EditorUtility.SetDirty(lens);

            var model = (ModelImporter)AssetImporter.GetAtPath(Model);
            model.globalScale = 1; model.useFileScale = true;
            model.importAnimation = false; model.importCameras = false; model.importLights = false; model.isReadable = true;
            model.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            var atlas = V2AtlasMaterial.Ensure("SurgeryGantryV2", TextureDir, MaterialDir);
            foreach (var dir in SharedMaterialDirs)
                foreach (var path in Directory.GetFiles(dir, "M_*.mat").Select(p => p.Replace('\\', '/')))
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
                }
            model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), atlas.name), atlas);
            model.SaveAndReimport();

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "SurgeryGantryV2";
            Transform Find(string name) => instance.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
            Find("BGT_Btn_Arms").gameObject.AddComponent<SurgeryTableControl>().Configure(SurgeryControlKind.Arms, "龙门机械臂");
            Find("BGT_Btn_Lamp").gameObject.AddComponent<SurgeryTableControl>().Configure(SurgeryControlKind.Lamp, "无影灯");
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            Debug.Log("[SurgeryGantryV2] built " + PrefabPath);
        }
    }
}
