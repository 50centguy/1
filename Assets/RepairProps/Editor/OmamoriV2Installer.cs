using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // Swaps the omamori's scanned Tripo exterior for the clean V2 rebuild (ArtSource/OmamoriV2): OM_Exterior's pieces are
    // replaced by OM_ext_Shell / OM_ext_Face / OM_ext_Screen and the OM_RearCover node gets the new cover mesh. The RK
    // interior, the cover screws and every flow node keep their objects. Re-runnable; rerun after RepairPropsImporter.BuildAll.
    static class OmamoriV2Installer
    {
        const string PrefabPath = "Assets/RepairProps/Prefabs/Omamori.prefab";
        const string ModelFolder = "Assets/RepairProps/Models/Omamori/V2";
        const string ModelPath = ModelFolder + "/Omamori_V2.fbx";
        const string MaterialFolder = "Assets/RepairProps/Materials/Omamori/V2";

        public static void Run()
        {
            var log = new StringBuilder();
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.importCameras = false; importer.importLights = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.isReadable = true;
            importer.SaveAndReimport();
            foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { ModelFolder }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.anisoLevel = 2;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/RepairProps/Materials/Omamori", "V2");
            var materials = new[] { "Shell", "Face", "Screen", "RearCover" }.ToDictionary(p => p, Material);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var exterior = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "OM_Exterior");
                for (int i = exterior.childCount - 1; i >= 0; i--) Object.DestroyImmediate(exterior.GetChild(i).gameObject);
                foreach (var part in new[] { "OM_ext_Shell", "OM_ext_Face", "OM_ext_Screen" })
                {
                    var go = new GameObject(part);
                    go.layer = exterior.gameObject.layer;
                    go.transform.SetParent(exterior, false);
                    Fit(go, model, part, materials[part.Substring("OM_ext_".Length)], log);
                }
                var cover = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "OM_RearCover").gameObject;
                Fit(cover, model, "OM_RearCover", materials["RearCover"], log);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("[OmamoriV2Installer]\n" + log);
        }

        static void Fit(GameObject target, GameObject model, string part, Material material, StringBuilder log)
        {
            var source = model.GetComponentsInChildren<MeshFilter>(true).First(f => f.name == part);
            // Keep the imported node's pose so the mesh lands where Blender put it.
            target.transform.localPosition = source.transform.localPosition;
            target.transform.localRotation = source.transform.localRotation;
            target.transform.localScale = source.transform.localScale;
            var filter = Ensure<MeshFilter>(target);
            filter.sharedMesh = source.sharedMesh;
            var renderer = Ensure<MeshRenderer>(target);
            renderer.sharedMaterials = new[] { material };
            var collider = Ensure<MeshCollider>(target);
            collider.sharedMesh = source.sharedMesh;
            log.AppendLine(string.Format("{0,-14} mesh={1} tris={2} pose={3}/{4}/{5} bounds={6}", part, source.sharedMesh.name, source.sharedMesh.triangles.Length / 3,
                source.transform.localPosition.ToString("F4"), source.transform.localEulerAngles.ToString("F1"), source.transform.localScale.ToString("F3"), source.sharedMesh.bounds));
        }

        // Unity's fake-null components defeat ??, so test explicitly.
        static T Ensure<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        static Material Material(string part)
        {
            string path = MaterialFolder + "/M_OmamoriV2_" + part + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(ModelFolder + "/OmamoriV2_" + part + "_basecolor.png");
            material.SetTexture("_BaseMap", baseMap);
            material.SetTexture("_MainTex", baseMap);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", part == "Screen" ? .75f : .32f);
            if (part == "Screen")
            {
                material.EnableKeyword("_EMISSION");
                material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ModelFolder + "/OmamoriV2_Screen_emission.png"));
                material.SetColor("_EmissionColor", Color.white * 1.6f);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
