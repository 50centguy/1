using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BorderRepair.RepairProps.EditorTools
{
    // Read-only survey of the district: materials by use (how much surface, which meshes, what scale), lights and render
    // settings. Feeds the palette pass; writes census.txt next to the shots.
    public static partial class ExteriorDistrictPass
    {
        public static void Census()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var sb = new StringBuilder();
            sb.Append("fog ").Append(RenderSettings.fog).Append(' ').Append(RenderSettings.fogMode).Append(" colour #").Append(ColorUtility.ToHtmlStringRGB(RenderSettings.fogColor))
              .Append(" density ").Append(RenderSettings.fogDensity).Append(" lin ").Append(RenderSettings.fogStartDistance).Append('-').Append(RenderSettings.fogEndDistance).Append('\n');
            sb.Append("ambient ").Append(RenderSettings.ambientMode).Append(" sky #").Append(ColorUtility.ToHtmlStringRGB(RenderSettings.ambientSkyColor))
              .Append(" equator #").Append(ColorUtility.ToHtmlStringRGB(RenderSettings.ambientEquatorColor)).Append(" ground #").Append(ColorUtility.ToHtmlStringRGB(RenderSettings.ambientGroundColor))
              .Append(" intensity ").Append(RenderSettings.ambientIntensity).Append(" skybox ").Append(RenderSettings.skybox ? RenderSettings.skybox.name : "none").Append(" reflection ").Append(RenderSettings.defaultReflectionMode).Append('\n');
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).Take(1))
                sb.Append("camera clear ").Append(cam.clearFlags).Append(" bg #").Append(ColorUtility.ToHtmlStringRGB(cam.backgroundColor)).Append('\n');
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.Append("volume ").Append(v.name).Append(" profile ").Append(v.sharedProfile ? v.sharedProfile.name : "none").Append('\n');
            sb.Append("\nLIGHTS\n");
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(l => l.name))
                sb.Append(l.name).Append(" | ").Append(l.type).Append(" #").Append(ColorUtility.ToHtmlStringRGB(l.color)).Append(" i=").Append(l.intensity)
                  .Append(" r=").Append(l.range).Append(" shadows=").Append(l.shadows).Append(" p=").Append(l.transform.position.ToString("F1")).Append(" on=").Append(l.enabled && l.gameObject.activeInHierarchy).Append('\n');

            sb.Append("\nMATERIALS (exterior roots)\n");
            var rows = scene.GetRootGameObjects().Where(r => ExteriorRoots.Contains(r.name) && r.name != PassRoot)
                .SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(true))
                .SelectMany(r => r.sharedMaterials.Where(m => m != null).Select(m => (r, m)))
                .GroupBy(x => x.m)
                .Select(g => new
                {
                    mat = g.Key,
                    count = g.Count(),
                    area = g.Sum(x => { var s = x.r.bounds.size; return s.x * s.y + s.y * s.z + s.x * s.z; }),
                    names = string.Join(", ", g.Select(x => x.r.name).Distinct().Take(6)),
                    meshes = string.Join(", ", g.Select(x => x.r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null).Where(m => m != null).Select(m => m.name + (m.isReadable ? "" : "(nr)")).Distinct().Take(4)),
                    scale = g.Select(x => x.r.transform.lossyScale).OrderByDescending(s => s.magnitude).First(),
                })
                .OrderByDescending(x => x.area);
            foreach (var x in rows)
            {
                var m = x.mat;
                var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                sb.Append(m.name).Append(" | ").Append(AssetDatabase.GetAssetPath(m)).Append(" | ").Append(m.shader.name)
                  .Append(" | base #").Append(m.HasProperty("_BaseColor") ? ColorUtility.ToHtmlStringRGBA(m.GetColor("_BaseColor")) : "-")
                  .Append(" tex ").Append(tex ? tex.name + " " + tex.width + "x" + tex.height + " " + AssetDatabase.GetAssetPath(tex) : "-")
                  .Append(" tiling ").Append(m.HasProperty("_BaseMap") ? m.GetTextureScale("_BaseMap").ToString("F2") : "-")
                  .Append(" emis ").Append(m.IsKeywordEnabled("_EMISSION") ? "#" + ColorUtility.ToHtmlStringRGB(m.GetColor("_EmissionColor")) : "-")
                  .Append(" | n=").Append(x.count).Append(" area=").Append(x.area.ToString("F0")).Append(" maxScale=").Append(x.scale.ToString("F1"))
                  .Append(" | ").Append(x.names).Append(" | meshes: ").Append(x.meshes).Append('\n');
            }
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "census.txt"), sb.ToString());
            Debug.Log("[Exterior] census written");
        }
    }
}
