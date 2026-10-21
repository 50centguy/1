using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // Imports ArtSource/ClinicStorageV2 output (atlas FBX copied to Assets/RepairProps/Storage/Art) and writes
    // Assets/RepairProps/Storage/Resources/ClinicStorageV2.prefab: the room's drawer wall, tool chest, cold cabinet,
    // limb rack, lockers and dock crates in the room frame. Shared M_BV2_* slots are remapped onto the bench's
    // materials (build the bench first); the label sheet and the two glowing gels are created here.
    public static class ClinicStorageV2Builder
    {
        const string Root = "Assets/RepairProps/Storage";
        const string Model = Root + "/Art/ClinicStorageV2.fbx";
        const string TextureDir = Root + "/Art/Textures";
        const string MaterialDir = Root + "/Materials";
        const string BenchMaterials = "Assets/RepairProps/Bench/Materials";
        const string PrefabPath = Root + "/Resources/ClinicStorageV2.prefab";

        static readonly (string name, string texture, float smoothness, Color emission, Color flat)[] Own =
        {
            ("M_ST_Prints", "T_ST_Prints", .15f, Color.clear, Color.white),
            ("M_ST_Gel", null, .8f, new Color(1.6f, .55f, .75f), new Color(.95f, .38f, .52f)),
            ("M_ST_GelAmber", null, .8f, new Color(1.8f, 1.0f, .35f), new Color(1f, .62f, .25f)),
        };

        [MenuItem("Border Repair/Repair Props/Build Clinic Storage V2")]
        public static void Build()
        {
            if (!File.Exists(Model)) throw new FileNotFoundException("Copy ArtSource/ClinicStorageV2/Atlas/ClinicStorageV2.fbx to " + Model + " first.");
            if (!Directory.Exists(BenchMaterials)) throw new DirectoryNotFoundException("Build the repair bench V2 first; its materials are shared.");
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh();
            var prints = (TextureImporter)AssetImporter.GetAtPath(TextureDir + "/T_ST_Prints.png");
            prints.sRGBTexture = true; prints.mipmapEnabled = true; prints.filterMode = FilterMode.Point;
            prints.maxTextureSize = 1024; prints.textureCompression = TextureImporterCompression.Compressed; prints.wrapMode = TextureWrapMode.Clamp;
            prints.SaveAndReimport();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var model = (ModelImporter)AssetImporter.GetAtPath(Model);
            model.globalScale = 1; model.useFileScale = true;
            model.importAnimation = false; model.importCameras = false; model.importLights = false;
            model.isReadable = true;   // V2StaticBatch
            model.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var spec in Own)
            {
                string path = MaterialDir + "/" + spec.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                var texture = spec.texture != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/" + spec.texture + ".png") : null;
                material.SetTexture("_BaseMap", texture);
                material.SetColor("_BaseColor", spec.flat);
                material.SetFloat("_Metallic", 0);
                material.SetFloat("_Smoothness", spec.smoothness);
                if (spec.emission.maxColorComponent > 0)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", spec.emission);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else material.DisableKeyword("_EMISSION");
                EditorUtility.SetDirty(material);
                model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), spec.name), material);
            }
            model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_ClinicStorageV2_Atlas"), V2AtlasMaterial.Ensure("ClinicStorageV2", TextureDir, MaterialDir));
            foreach (var path in Directory.GetFiles(BenchMaterials, "M_BV2_*.mat").Select(p => p.Replace('\\', '/')))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
            }
            model.SaveAndReimport();

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "ClinicStorageV2";
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                bool thin = renderer.name.EndsWith("Prints");
                renderer.shadowCastingMode = thin ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            Debug.Log("[ClinicStorageV2] built " + PrefabPath);
        }
    }
}
