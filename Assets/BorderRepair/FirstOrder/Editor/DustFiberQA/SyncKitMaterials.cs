using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.FirstOrder.EditorTools.DustFiberQA
{
    /// <summary>
    /// 把故障包源（ArtSource/Unit07FaultKit/materials.json，由 Blender 构建脚本生成）里的材质参数同步到 Unity 的 URP Lit 材质：
    /// 底色（贴图乘的颜色）、光滑度（没有金属 / 光滑度贴图时）、高光开关、环境反射开关。贴图引用不动。
    /// </summary>
    public static class SyncKitMaterials
    {
        const string Manifest = "ArtSource/Unit07FaultKit/materials.json";
        const string MatDir = "Assets/BorderRepair/Art/Unit07FaultKit/Materials";

        [System.Serializable] class Entry { public string name, baseColor, metallicGlossMap; public float smoothness; public bool specularHighlights = true, environmentReflections = true; }
        [System.Serializable] class Root { public Entry[] materials; }

        public static void Run()
        {
            var root = JsonUtility.FromJson<Root>(File.ReadAllText(Manifest));
            var log = new StringBuilder();
            foreach (var e in root.materials)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{e.name}.mat");
                if (m == null) { log.AppendLine($"- {e.name}：Unity 里没有这个材质"); continue; }
                ColorUtility.TryParseHtmlString(e.baseColor, out var c);
                var before = $"底色 {m.GetColor("_BaseColor")}，光滑度 {m.GetFloat("_Smoothness"):0.###}，高光 {m.GetFloat("_SpecularHighlights") > 0.5f}，环境反射 {m.GetFloat("_EnvironmentReflections") > 0.5f}";
                m.SetColor("_BaseColor", c);
                if (string.IsNullOrEmpty(e.metallicGlossMap)) m.SetFloat("_Smoothness", e.smoothness);
                m.SetFloat("_SpecularHighlights", e.specularHighlights ? 1f : 0f);
                if (e.specularHighlights) m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF"); else m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                m.SetFloat("_EnvironmentReflections", e.environmentReflections ? 1f : 0f);
                if (e.environmentReflections) m.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); else m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                EditorUtility.SetDirty(m);
                var after = $"底色 {m.GetColor("_BaseColor")}，光滑度 {m.GetFloat("_Smoothness"):0.###}，高光 {m.GetFloat("_SpecularHighlights") > 0.5f}，环境反射 {m.GetFloat("_EnvironmentReflections") > 0.5f}";
                log.AppendLine(before == after ? $"- {e.name}：不变（{after}）" : $"- {e.name}：{before} → **{after}**");
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(FiberVariants.OutDir);
            File.WriteAllText(Path.Combine(FiberVariants.OutDir, "material_sync.md"), "# 故障包材质同步（materials.json → Unity）\n\n" + log, new UTF8Encoding(false));
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
