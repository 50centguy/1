using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // Imports ArtSource/ClinicZonesV2 output (atlas FBX copied to Assets/RepairProps/Zones/Art) and writes
    // Assets/RepairProps/Zones/Resources/ClinicZonesV2.prefab: the trade counter and the UNIT07 dock corner, both in
    // the room frame. Shared M_BV2_* slots are remapped onto the repair bench's materials (build the bench first);
    // M_ZN_* prints and lamps are created here. ClinicZonesV2Runtime places the prefab at play time; no scene is touched.
    public static class ClinicZonesV2Builder
    {
        const string Root = "Assets/RepairProps/Zones";
        const string Model = Root + "/Art/ClinicZonesV2.fbx";
        const string TextureDir = Root + "/Art/Textures";
        const string MaterialDir = Root + "/Materials";
        const string BenchMaterials = "Assets/RepairProps/Bench/Materials";
        const string PrefabPath = Root + "/Resources/ClinicZonesV2.prefab";

        // name: texture, smoothness, emission (zero = none), flat colour when untextured
        static readonly (string name, string texture, float smoothness, Color emission, Color flat)[] Own =
        {
            ("M_ZN_CounterPrints", "T_ZN_CounterPrints", .1f, Color.clear, Color.white),
            ("M_ZN_DockPrints", "T_ZN_DockPrints", .15f, Color.clear, Color.white),
            ("M_ZN_FloorMat", "T_ZN_FloorMat", .35f, Color.clear, Color.white),
            ("M_ZN_TermGlass", "T_ZN_TermGlass", .85f, new Color(1.6f, 1.2f, .8f), Color.white),   // the live text is a canvas on top
            ("M_ZN_LampAmber", null, .6f, new Color(2.2f, 1.2f, .3f), new Color(1f, .6f, .2f)),
        };

        [MenuItem("Border Repair/Repair Props/Build Clinic Zones V2")]
        public static void Build()
        {
            if (!File.Exists(Model)) throw new FileNotFoundException("Copy ArtSource/ClinicZonesV2/Atlas/ClinicZonesV2.fbx to " + Model + " first.");
            if (!Directory.Exists(BenchMaterials)) throw new DirectoryNotFoundException("Build the repair bench V2 first; its materials are shared.");
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(TextureDir, "T_ZN_*.png").Select(p => p.Replace('\\', '/')))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.sRGBTexture = true; importer.mipmapEnabled = true; importer.filterMode = FilterMode.Point;
                importer.maxTextureSize = 1024; importer.textureCompression = TextureImporterCompression.Compressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
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
                    material.SetTexture("_EmissionMap", texture);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else material.DisableKeyword("_EMISSION");
                EditorUtility.SetDirty(material);
                model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), spec.name), material);
            }
            model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_ClinicZonesV2_Atlas"), V2AtlasMaterial.Ensure("ClinicZonesV2", TextureDir, MaterialDir));
            foreach (var path in Directory.GetFiles(BenchMaterials, "M_BV2_*.mat").Select(p => p.Replace('\\', '/')))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name), material);
            }
            model.SaveAndReimport();

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "ClinicZonesV2";
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                // Paper, tag strings and lamps are too thin to cast anything worth a shadow pass.
                bool thin = renderer.name.EndsWith("Prints") || renderer.name == "ZC_TagStrings" || renderer.name == "ZD_FloorMat"
                    || renderer.name.Contains("Lamp") || renderer.name.EndsWith("_Led") || renderer.name == "ZC_Terminal_Glass";
                renderer.shadowCastingMode = thin ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            Debug.Log("[ClinicZonesV2] built " + PrefabPath);
        }
    }
}
