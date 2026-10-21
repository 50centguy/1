using UnityEditor;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // The one URP Lit material a V2 asset's baked atlas uses (ArtSource/RepairBenchV2/atlas_bake.py): base colour from
    // <Name>_Atlas.png, metallic (R) and smoothness (A) from <Name>_AtlasMS.png.
    public static class V2AtlasMaterial
    {
        public static Material Ensure(string name, string textureDir, string materialDir)
        {
            string color = textureDir + "/" + name + "_Atlas.png", ms = textureDir + "/" + name + "_AtlasMS.png";
            foreach (var (path, srgb) in new[] { (color, true), (ms, false) })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer == null) throw new System.IO.FileNotFoundException(path);
                importer.sRGBTexture = srgb; importer.mipmapEnabled = true; importer.filterMode = FilterMode.Point;   // matches the room's point-sampled retro textures
                importer.maxTextureSize = 1024; importer.textureCompression = TextureImporterCompression.Compressed;
                importer.alphaSource = srgb ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            string materialPath = materialDir + "/M_" + name + "_Atlas.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
            material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(color));
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ms));
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
