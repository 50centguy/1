using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.Art.UnifiedClinic.EditorTools
{
    public static class ClinicClutterImporter
    {
        const string Source = "ArtSource/UnifiedClinicClutter";
        const string Root = ClinicArtImporter.Root + "/Clutter";
        const string ModelPath = Root + "/UnifiedClinic_ClutterAddon.fbx";
        [Serializable] class Counts { public int triangles, mesh_objects, materials, textures; }
        [Serializable] class Stats { public Counts counts; public bool passed_all; public string[] failures; }
        [Serializable] class PlacementFile { public Placement[] objects; }
        [Serializable] class Placement { public string name; public int triangles; }
        [Serializable] class Report
        {
            public string timestampUtc, source;
            public long triangles;
            public int meshes, materials, textures;
            public string[] limitations;
        }

        public static GameObject Import()
        {
            if (!Directory.Exists(Source)) return null;
            var stats = JsonUtility.FromJson<Stats>(File.ReadAllText(Source + "/stats.json"));
            if (!stats.passed_all || stats.failures.Length != 0)
                throw new InvalidOperationException("Clutter source checks failed.");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Root + "/Textures");
            Directory.CreateDirectory(Root + "/Materials");
            CopyChanged(Source + "/Export/UnifiedClinic_ClutterAddon.fbx", ModelPath);
            foreach (string source in Directory.GetFiles(Source + "/Textures", "*.png"))
                CopyChanged(source, Root + "/Textures/" + Path.GetFileName(source));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string path in Directory.GetFiles(Root + "/Textures", "*.png"))
            {
                var texture = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                texture.sRGBTexture = true;
                texture.filterMode = FilterMode.Point;
                texture.mipmapEnabled = true;
                texture.isReadable = false;
                texture.maxTextureSize = 256;
                texture.SaveAndReimport();
            }
            // These mappings are the four MAT_SPECS in the deterministic Blender build script.
            var materials = new Dictionary<string, Material> {
                { "M_UCC_Fabric", Material("M_UCC_Fabric", "T_UCC_SoftAtlas.png", .95f) },
                { "M_UCC_Paper", Material("M_UCC_Paper", "T_UCC_SoftAtlas.png", .85f) },
                { "M_UCC_PaintedMetal", Material("M_UCC_PaintedMetal", "T_UCC_PaintAtlas.png", .68f) },
                { "M_UCC_Cable", Material("M_UCC_Cable", "T_UCC_PaintAtlas.png", .72f) }
            };
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.isReadable = false;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.globalScale = 1;
            foreach (var material in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.Key), material.Value);
            importer.SaveAndReimport();
            AssetDatabase.SaveAssets();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var filters = asset.GetComponentsInChildren<MeshFilter>(true);
            long triangles = filters.Sum(f => TriangleCount(f.sharedMesh));
            if (filters.Length != stats.counts.mesh_objects || triangles != stats.counts.triangles ||
                materials.Count != stats.counts.materials || Directory.GetFiles(Root + "/Textures", "*.png").Length != stats.counts.textures)
            {
                var expected = JsonUtility.FromJson<PlacementFile>(File.ReadAllText(Source + "/placement_bounds.json")).objects;
                string details = string.Join("; ", filters.Select(f => $"{f.name} {TriangleCount(f.sharedMesh)}/{expected.Single(p => p.name == f.name).triangles}"));
                throw new InvalidOperationException($"Clutter Unity import counts differ from Blender source: " +
                    $"meshes {filters.Length}/{stats.counts.mesh_objects}, triangles {triangles}/{stats.counts.triangles}, " +
                    $"materials {materials.Count}/{stats.counts.materials}, textures {Directory.GetFiles(Root + "/Textures", "*.png").Length}/{stats.counts.textures}. {details}");
            }
            if (asset.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m == null || !materials.ContainsValue(m))))
                throw new InvalidOperationException("Unmapped clutter material after FBX import.");
            Directory.CreateDirectory("Docs/Integration/UnifiedClinic/ArtImport");
            File.WriteAllText("Docs/Integration/UnifiedClinic/ArtImport/clutter_import.json", JsonUtility.ToJson(new Report {
                timestampUtc = DateTime.UtcNow.ToString("O"), source = Source,
                triangles = triangles, meshes = filters.Length, materials = materials.Count, textures = stats.counts.textures,
                limitations = new[] { "Import/count/material validation only; full-scene geometry and human/VR checks are separate.",
                    "Static wall dressing has no gameplay interactions or physics simulation." }
            }, true));
            return asset;
        }

        static Material Material(string name, string textureName, float roughness)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) throw new InvalidOperationException("URP Simple Lit unavailable.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + textureName)
                ?? throw new FileNotFoundException("Clutter texture unavailable.", textureName));
            material.SetFloat("_Smoothness", 1 - roughness);
            material.SetColor("_SpecColor", Color.black);
            material.DisableKeyword("_SPECULAR_COLOR");
            material.DisableKeyword("_SPECGLOSSMAP");
            material.DisableKeyword("_EMISSION");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }
        static long TriangleCount(Mesh mesh)
        {
            long count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetTopology(i) == MeshTopology.Triangles) count += mesh.GetIndexCount(i) / 3;
            return count;
        }
        static void CopyChanged(string source, string target)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("Missing generated clutter asset.", source);
            if (!File.Exists(target) || !File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(target))) File.Copy(source, target, true);
        }
    }
}
