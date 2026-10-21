using System.IO;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // Imports ArtSource/ClinicNpcs output (NPC_<Name>.fbx and its two textures, copied to Assets/RepairProps/Npcs/Art)
    // and writes Assets/RepairProps/Npcs/Resources/Npcs/NPC_<Name>.prefab with a ClinicNpc component wired to the head
    // and the face slot. Body atlas and face sheet are point-filtered, uncompressed (small, and the painted face must
    // not block-compress), clamped so the face frames never bleed into each other.
    public static class ClinicNpcBuilder
    {
        const string Root = "Assets/RepairProps/Npcs";
        const string Art = Root + "/Art";
        const string TextureDir = Art + "/Textures";
        const string MaterialDir = Root + "/Materials";
        const string PrefabDir = Root + "/Resources/Npcs";
        public static readonly string[] Names = { "Kasumi", "Tobi", "Gen", "Rin" };

        [MenuItem("Border Repair/Repair Props/Build Clinic NPCs")]
        public static void Build()
        {
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(PrefabDir);
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var name in Names)
            {
                string model = Art + "/NPC_" + name + ".fbx";
                if (!File.Exists(model)) throw new FileNotFoundException("Copy ArtSource/ClinicNpcs/Export/NPC_" + name + ".fbx to " + model + " first.");
                var body = Material(shader, name, "M_NPC_" + name, TextureDir + "/T_NPC_" + name + ".png");
                var face = Material(shader, name, "M_NPC_" + name + "_Face", TextureDir + "/T_NPC_" + name + "_Face.png");
                var importer = (ModelImporter)AssetImporter.GetAtPath(model);
                importer.globalScale = 1; importer.useFileScale = true;
                importer.importAnimation = false; importer.importCameras = false; importer.importLights = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), body.name), body);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), face.name), face);
                importer.SaveAndReimport();

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(model));
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = "Model";
                // An identity root above the FBX's axis-converted hierarchy: turning and breathing act in room axes.
                var wrapper = new GameObject("NPC_" + name);
                instance.transform.SetParent(wrapper.transform, false);
                var npc = wrapper.AddComponent<ClinicNpc>();
                npc.Configure(name, Find(instance.transform, name + "_Head"), Find(instance.transform, "Ref_Eye"), Find(instance.transform, "Ref_Front"));
                instance = wrapper;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }
                PrefabUtility.SaveAsPrefabAsset(instance, PrefabDir + "/NPC_" + name + ".prefab");
                Object.DestroyImmediate(instance);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ClinicNpcs] built " + Names.Length + " prefabs in " + PrefabDir);
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            throw new System.InvalidOperationException(root.name + " has no " + name);
        }

        static Material Material(Shader shader, string npc, string name, string texturePath)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            if (importer == null) throw new FileNotFoundException(texturePath);
            importer.sRGBTexture = true; importer.mipmapEnabled = false; importer.filterMode = FilterMode.Point;
            importer.maxTextureSize = 512; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.None; importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            string path = MaterialDir + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Smoothness", .12f);
            material.SetFloat("_SpecularHighlights", 0); material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.SetFloat("_EnvironmentReflections", 0); material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            // A little self-light keeps painted faces readable in the clinic's dim corners (anime shading is flat anyway).
            material.EnableKeyword("_EMISSION");
            material.SetTexture("_EmissionMap", texture);
            material.SetColor("_EmissionColor", new Color(.16f, .15f, .15f));
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
