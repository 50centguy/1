using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BorderRepair.FirstOrder.EditorTools.FaultArtQA
{
    /// <summary>故障美术真实场景验收 · 只读核查：渲染管线、后处理、灯光、反射探针、镜头抗锯齿、故障美术件材质。不保存任何东西。</summary>
    public static class FaultArtAudit
    {
        public const string OutDir = "Docs/Integration/Unit07FirstOrder/FaultArtQA";

        public static void Run()
        {
            var sb = new StringBuilder();
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            sb.AppendLine($"# 渲染设置核查（质量档 {QualitySettings.names[QualitySettings.GetQualityLevel()]}）");
            if (urp != null)
                sb.AppendLine($"- URP 资源 `{AssetDatabase.GetAssetPath(urp)}`：MSAA {urp.msaaSampleCount}x，HDR {urp.supportsHDR}，渲染缩放 {urp.renderScale}，主光阴影 {urp.supportsMainLightShadows}，附加光阴影 {urp.supportsAdditionalLightShadows}，阴影距离 {urp.shadowDistance}，附加光 {urp.additionalLightsRenderingMode} 上限 {urp.maxAdditionalLightsCount}");
            foreach (var scene in new[] { "Assets/BorderRepair/FirstOrder/Scenes/LayoutAB/Unit07FirstOrder_LayoutB.unity" })
            {
                EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                sb.AppendLine($"\n## 场景 `{scene}`");
                sb.AppendLine($"- 环境光 {RenderSettings.ambientMode}：天 {RenderSettings.ambientSkyColor} 中 {RenderSettings.ambientEquatorColor} 地 {RenderSettings.ambientGroundColor}；环境反射来源 {RenderSettings.defaultReflectionMode}，强度 {RenderSettings.reflectionIntensity}，自定义反射 {(RenderSettings.customReflectionTexture != null ? RenderSettings.customReflectionTexture.name : "无")}");
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    sb.AppendLine($"- 灯 `{l.name}`：{l.type} 强度 {l.intensity} 颜色 {l.color} 范围 {l.range} 角 {l.spotAngle}/{l.innerSpotAngle} 阴影 {l.shadows} 位置 {l.transform.position:F2}");
                sb.AppendLine($"- 反射探针：{Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Length} 个");
                foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
                {
                    sb.AppendLine($"- 后处理 `{v.name}`（{AssetDatabase.GetAssetPath(v.sharedProfile)}）：");
                    foreach (var c in v.sharedProfile.components.Where(c => c.active))
                        sb.AppendLine($"  - {c.GetType().Name}：" + string.Join("，", c.parameters.Select((p, i) => (p, i)).Where(x => x.p.overrideState).Select(x => $"{c.GetType().GetFields().Where(f => typeof(VolumeParameter).IsAssignableFrom(f.FieldType)).ElementAtOrDefault(x.i)?.Name}={x.p.GetType().GetProperty("value")?.GetValue(x.p)}")));
                }
                var cam = Object.FindFirstObjectByType<Camera>();
                var ad = cam.GetUniversalAdditionalCameraData();
                sb.AppendLine($"- 主相机：后处理 {ad.renderPostProcessing}，抗锯齿 {ad.antialiasing}（{ad.antialiasingQuality}），allowMSAA {cam.allowMSAA}，HDR {cam.allowHDR}");
                var flow = Object.FindFirstObjectByType<FirstOrderFlow>();
                var rs = flow.ClogLayers.Concat(flow.Bearing.Renderers().Where(r => r != flow.OriginalBearingRenderer)).Concat(flow.NewBearing.Renderers()).Append(flow.CoverLabel)
                             .Append(flow.Cover.GetComponent<Renderer>()).Append(flow.OriginalBearingRenderer);
                foreach (var r in rs.Distinct())
                {
                    var m = r.sharedMaterial;
                    sb.AppendLine($"- 渲染器 `{r.name}` → 材质 `{AssetDatabase.GetAssetPath(m)}`（{m.shader.name}）关键字 [{string.Join(" ", m.shaderKeywords)}]");
                    foreach (var p in new[] { "_BaseColor", "_Metallic", "_Smoothness", "_SmoothnessTextureChannel", "_BumpScale", "_OcclusionStrength", "_SpecularHighlights", "_EnvironmentReflections", "_Surface", "_AlphaClip", "_Cull" })
                        if (m.HasProperty(p)) sb.Append($" {p}={(p == "_BaseColor" ? m.GetColor(p).ToString() : m.GetFloat(p).ToString("0.###"))}");
                    sb.AppendLine();
                    foreach (var t in new[] { "_BaseMap", "_MetallicGlossMap", "_BumpMap", "_OcclusionMap", "_EmissionMap" })
                        if (m.HasProperty(t) && m.GetTexture(t) != null) sb.AppendLine($"    {t} = `{AssetDatabase.GetAssetPath(m.GetTexture(t))}`");
                }
            }
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "audit_render.md"), sb.ToString(), new UTF8Encoding(false));
            EditorApplication.Exit(0);
        }
    }
}
