using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Data;
using BorderRepair.RepairProps;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // Imports ArtSource/RepairBenchV2 output (copied to Assets/RepairProps/Bench/Art) and writes
    // Assets/RepairProps/Bench/Resources/RepairBenchV2.prefab: URP Lit materials from the generated textures, a remap
    // of the FBX's material slots onto them, and RepairBenchProp on the clickable objects. Scenes are not touched;
    // RepairBenchV2Runtime places the prefab at play time. Rerunning overwrites only these generated assets.
    public static class RepairBenchV2Builder
    {
        const string Root = "Assets/RepairProps/Bench";
        const string Model = Root + "/Art/RepairBenchV2.fbx";
        const string TextureDir = Root + "/Art/Textures";
        const string MaterialDir = Root + "/Materials";
        const string PrefabPath = Root + "/Resources/RepairBenchV2.prefab";

        // name: texture, metallic, smoothness, emission (linear colour, zero = none), flat colour when untextured
        static readonly (string name, string texture, float metallic, float smoothness, Color emission, Color flat)[] Materials =
        {
            ("M_BV2_SteelGreen", "T_BV2_SteelGreen", .35f, .32f, Color.clear, Color.white),
            ("M_BV2_SteelBare", "T_BV2_SteelBare", .9f, .48f, Color.clear, Color.white),
            ("M_BV2_Worktop", "T_BV2_Worktop", 0, .22f, Color.clear, Color.white),
            ("M_BV2_Mat", "T_BV2_Mat", 0, .12f, Color.clear, Color.white),
            ("M_BV2_Ivory", "T_BV2_Ivory", 0, .35f, Color.clear, Color.white),
            ("M_BV2_WornRed", "T_BV2_WornRed", .2f, .35f, Color.clear, Color.white),
            ("M_BV2_Orange", "T_BV2_Orange", 0, .45f, Color.clear, Color.white),
            ("M_BV2_Yellow", "T_BV2_Yellow", 0, .45f, Color.clear, Color.white),
            ("M_BV2_Rubber", "T_BV2_Rubber", 0, .15f, Color.clear, Color.white),
            ("M_BV2_Foam", "T_BV2_Foam", 0, .05f, Color.clear, Color.white),
            ("M_BV2_Paper", "T_BV2_Paper", 0, .05f, Color.clear, Color.white),
            ("M_BV2_Sticky", "T_BV2_Sticky", 0, .05f, Color.clear, Color.white),
            ("M_BV2_Label", "T_BV2_Label", 0, .1f, Color.clear, Color.white),
            ("M_BV2_CrtGlass", "T_BV2_CrtGlass", 0, .85f, new Color(.05f, .22f, .12f), Color.white),
            ("M_BV2_Brass", null, .95f, .55f, Color.clear, new Color(.70f, .50f, .22f)),
            ("M_BV2_Copper", null, .95f, .5f, Color.clear, new Color(.72f, .36f, .20f)),
            ("M_BV2_Bulb", null, 0, .6f, new Color(2.4f, 1.9f, 1.2f), new Color(1, .92f, .75f)),
            ("M_BV2_LedRed", null, 0, .6f, new Color(2f, .25f, .15f), new Color(.9f, .1f, .1f)),
            ("M_BV2_LedGreen", null, 0, .6f, new Color(.3f, 2f, .5f), new Color(.2f, .9f, .3f)),
            ("M_BV2_DarkPlastic", "T_BV2_Rubber", 0, .4f, Color.clear, new Color(.8f, .8f, .8f)),
            // setting prints (SETTING_PROPS.md): NSP lease tags, paperwork, meter faces, the tester's amber LCD
            ("M_BV2_LeaseTag", "T_BV2_LeaseTag", 0, .15f, Color.clear, Color.white),
            ("M_BV2_WorkOrder", "T_BV2_WorkOrder", 0, .05f, Color.clear, Color.white),
            ("M_BV2_RentNote", "T_BV2_RentNote", 0, .05f, Color.clear, Color.white),
            ("M_BV2_SealTape", "T_BV2_SealTape", 0, .2f, Color.clear, Color.white),
            ("M_BV2_AssetStencil", "T_BV2_AssetStencil", 0, .2f, Color.clear, Color.white),
            ("M_BV2_MeterFace", "T_BV2_MeterFace", 0, .3f, Color.clear, Color.white),
            ("M_BV2_Lcd", "T_BV2_Lcd", 0, .6f, new Color(1.6f, 1.1f, .5f), Color.white),
            ("M_BV2_FixturePanel", "T_BV2_FixturePanel", .3f, .3f, Color.clear, Color.white),
            // personal items (desk pass 2)
            ("M_BV2_Ceramic", null, 0, .75f, Color.clear, new Color(.93f, .91f, .86f)),
            ("M_BV2_Hardboard", null, 0, .2f, Color.clear, new Color(.30f, .23f, .16f)),
            ("M_BV2_Plant", null, 0, .3f, Color.clear, new Color(.33f, .47f, .32f)),
            ("M_BV2_Holo", null, 0, .5f, new Color(.5f, 2.2f, 2.6f), new Color(.3f, .95f, 1f)),
            ("M_BV2_Photo", "T_BV2_Photo", 0, .4f, Color.clear, Color.white),
            ("M_BV2_TapeLabel", "T_BV2_TapeLabel", 0, .1f, Color.clear, Color.white),
            ("M_BV2_MiniFace", "T_BV2_MiniFace", 0, .7f, new Color(1.2f, 1.6f, 1.2f), Color.white),
            // wall pass: pegboard (1024 px, hole grid) and the sheet of everything pinned to it
            ("M_BV2_Pegboard", "T_BV2_Pegboard", .3f, .35f, Color.clear, Color.white),
            ("M_BV2_WallPrints", "T_BV2_WallPrints", 0, .1f, Color.clear, Color.white),
        };

        [MenuItem("Border Repair/Repair Props/Build Repair Bench V2")]
        public static void Build()
        {
            if (!File.Exists(Model)) throw new FileNotFoundException("Copy ArtSource/RepairBenchV2/Export/RepairBenchV2.fbx to " + Model + " first.");
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            AssetDatabase.Refresh();
            ImportTextures();
            var materials = BuildMaterials();
            ImportModel(materials);
            BuildPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("[RepairBenchV2] built " + PrefabPath + " with " + materials.Count + " materials");
        }

        static void ImportTextures()
        {
            foreach (var path in Directory.GetFiles(TextureDir, "*.png").Select(p => p.Replace('\\', '/')))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Point;
                importer.anisoLevel = 1;
                importer.maxTextureSize = path.Contains("Mat") || path.Contains("Worktop") || path.Contains("Pegboard") ? 1024 : 512;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }
        }

        static Dictionary<string, Material> BuildMaterials()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var result = new Dictionary<string, Material>();
            foreach (var spec in Materials)
            {
                string path = MaterialDir + "/" + spec.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                var texture = spec.texture != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + "/" + spec.texture + ".png") : null;
                material.SetTexture("_BaseMap", texture);
                material.SetColor("_BaseColor", spec.flat);
                material.SetFloat("_Metallic", spec.metallic);
                material.SetFloat("_Smoothness", spec.smoothness);
                if (spec.emission.maxColorComponent > 0)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", spec.emission);
                    material.SetTexture("_EmissionMap", texture);   // textured screens glow with their own print
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else material.DisableKeyword("_EMISSION");
                EditorUtility.SetDirty(material);
                result[spec.name] = material;
            }
            return result;
        }

        static void ImportModel(Dictionary<string, Material> materials)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            importer.globalScale = 1; importer.useFileScale = true;
            importer.importAnimation = false; importer.importCameras = false; importer.importLights = false;
            importer.isReadable = true;   // runtime static batching (V2StaticBatch) needs readable meshes
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            materials["M_RepairBenchV2_Atlas"] = V2AtlasMaterial.Ensure("RepairBenchV2", TextureDir, MaterialDir);
            foreach (var pair in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            importer.SaveAndReimport();
        }

        static void BuildPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = "RepairBenchV2";
            Transform Find(string name) => instance.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
            void Prop(string name, RepairBenchPropKind kind, string label, RepairActionType? tool = null, float travel = .3f, float angle = -100)
                => Find(name).gameObject.AddComponent<RepairBenchProp>().Configure(kind, label, tool, travel, angle);
            Prop("BV2_Tool_Screwdriver", RepairBenchPropKind.Tool, "螺丝刀", RepairActionType.RemoveFastener);
            Prop("BV2_Tool_Spudger", RepairBenchPropKind.Tool, "开壳工具", RepairActionType.OpenHousing);
            Prop("BV2_Tool_Meter", RepairBenchPropKind.Tool, "检测仪", RepairActionType.ServiceModule);
            Prop("BV2_Tool_Loupe", RepairBenchPropKind.Tool, "放大镜（空手检查）");
            Prop("BV2_Lamp", RepairBenchPropKind.Lamp, "台灯");
            for (int i = 1; i <= 3; i++) Prop("BV2_Drawer_" + i, RepairBenchPropKind.Drawer, "抽屉 " + i);
            Prop("BV2_CabinetDoor", RepairBenchPropKind.Door, "柜门");
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Object.DestroyImmediate(instance);
        }
    }
}
