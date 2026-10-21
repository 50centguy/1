using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // Imports ArtSource/ClinicCyberKit output (copied to Assets/RepairProps/Cyber/Art) into two Resources prefabs:
    // CyberRoomKit and CyberSurgeryKit. Neon and curtain materials use the BorderRepairCyber shaders, the film viewer URP Lit;
    // M_GT_DarkSteel slots are remapped onto the surgery gantry's material (build it first).
    public static class ClinicCyberKitBuilder
    {
        const string Root = "Assets/RepairProps/Cyber";
        const string ArtDir = Root + "/Art";
        const string TextureDir = ArtDir + "/Textures";
        const string MaterialDir = Root + "/Materials";
        static readonly string[] Kits = { "CyberRoomKit", "CyberSurgeryKit" };   // the bench kit carried only holograms, now removed

        // HDR colours are linear; bloom (threshold ~0.85) picks up anything above ~1.
        static readonly (string name, Color color, float flicker)[] Neon =
        {
            // Moderate HDR: hot enough to bloom, low enough that the tube core keeps its hue instead of clipping white.
            ("M_Neon_Magenta", new Color(1f, .08f, .5f) * 2.2f, .10f),
            ("M_Neon_Cyan", new Color(.15f, .85f, 1f) * 2.0f, .04f),
            ("M_Neon_Orange", new Color(1f, .20f, .05f) * 2.3f, .08f),
            ("M_Neon_Red", new Color(1f, .05f, .03f) * 2.4f, .25f),
            ("M_Neon_Amber", new Color(1f, .55f, .08f) * 2.0f, .15f),
            ("M_Neon_MagentaFaulty", new Color(1f, .08f, .5f) * 2.2f, .85f),   // the sign's failing character
            ("M_Neon_CyanDim", new Color(.15f, .85f, 1f) * 1.1f, .02f),        // gantry status strip, not a feature light
        };

        [MenuItem("Border Repair/Repair Props/Build Clinic Cyber Kit")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(Root + "/Resources");
            foreach (var path in Directory.GetFiles(TextureDir, "*.png").Select(p => p.Replace('\\', '/')))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.sRGBTexture = true; importer.mipmapEnabled = true; importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 256; importer.textureCompression = TextureImporterCompression.Compressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            Material Make(string name, string shaderResource)
            {
                var shader = Resources.Load<Shader>("BorderRepairCyber/" + shaderResource);
                string path = MaterialDir + "/" + name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                return material;
            }
            var materials = new System.Collections.Generic.Dictionary<string, Material>();
            float seed = 0;
            foreach (var (name, color, flicker) in Neon)
            {
                var m = Make(name, "CyberNeon");
                m.SetColor("_Color", color); m.SetFloat("_Flicker", flicker); m.SetFloat("_Seed", seed += 1.7f);
                materials[name] = m;
            }
            // Backlit film on the hanging viewer: lit like paper, glowing through with its own print.
            var film = AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/M_Film.mat");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (film == null) { film = new Material(lit); AssetDatabase.CreateAsset(film, MaterialDir + "/M_Film.mat"); }
            film.shader = lit;
            var filmTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/T_Film_Body.png");
            film.SetTexture("_BaseMap", filmTexture);
            film.SetFloat("_Smoothness", .55f);
            film.EnableKeyword("_EMISSION");
            film.SetTexture("_EmissionMap", filmTexture);
            film.SetColor("_EmissionColor", new Color(.75f, .9f, 1.05f) * 1.5f);
            film.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(film);
            materials["M_Film"] = film;
            var curtain = Make("M_Curtain", "CyberCurtain");
            curtain.SetColor("_Color", new Color(.55f, .78f, .88f, .14f));
            curtain.SetColor("_RimColor", new Color(.5f, .9f, 1f) * 1.2f);
            materials["M_Curtain"] = curtain;
            var dark = AssetDatabase.LoadAssetAtPath<Material>("Assets/RepairProps/Surgery/Materials/M_GT_DarkSteel.mat");
            if (dark == null) throw new FileNotFoundException("Build Surgery Gantry V2 first; its M_GT_DarkSteel is shared.");
            materials["M_GT_DarkSteel"] = dark;

            foreach (var kit in Kits)
            {
                string model = ArtDir + "/" + kit + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(model);
                importer.globalScale = 1; importer.useFileScale = true;
                importer.importAnimation = false; importer.importCameras = false; importer.importLights = false; importer.isReadable = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                foreach (var pair in materials)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
                importer.SaveAndReimport();
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(model));
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = kit;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    // Emissive and see-through dressing never casts shadows; only the dark sign plates and the rail do.
                    bool solid = renderer.sharedMaterials.All(m => m == dark);
                    renderer.shadowCastingMode = solid ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = solid;
                }
                PrefabUtility.SaveAsPrefabAsset(instance, Root + "/Resources/" + kit + ".prefab");
                Object.DestroyImmediate(instance);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ClinicCyberKit] built " + string.Join(", ", Kits));
        }
    }
}
