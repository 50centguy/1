using System.IO;
using BorderRepair.Inspection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.Tests
{
    /// <summary>Shader 原型：编译无误、pass 齐全、正式 prefab 与默认 / 叙事场景未被改动。</summary>
    public class ShaderLabAssetTests
    {
        const string Root = "Assets/BorderRepair/Art/ShaderLab";
        static readonly string[] WornMaterials =
        {
            Root + "/Materials/M_Worn_PaintedMetal.mat",
            Root + "/Materials/M_Worn_OldCeramic.mat",
            Root + "/Materials/M_Worn_BareMetal.mat",
        };
        const string OverlayPath = Root + "/Materials/M_DiagnosticOverlay.mat";
        const string ProfilePath = Root + "/DiagnosticHighlightProfile.asset";

        static readonly string[] FormalPrefabs =
        {
            "Assets/BorderRepair/Prefabs/Items/Item_Communicator_Final.prefab",
            "Assets/BorderRepair/Prefabs/Items/Item_NavBeacon_Final.prefab",
            "Assets/BorderRepair/Prefabs/Items/Item_SalvageDrone_Final.prefab",
            "Assets/BorderRepair/Prefabs/Items/Narrative/Item_WorkerProsthetic_Placeholder.prefab",
        };

        [TestCase("BorderRepair/WornSurface")]
        [TestCase("BorderRepair/DiagnosticOverlay")]
        public void ShaderCompilesWithoutErrors(string name)
        {
            var shader = Shader.Find(name);
            Assert.IsNotNull(shader, $"找不到 shader {name}");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), $"{name} 有编译错误");
            foreach (var m in ShaderUtil.GetShaderMessages(shader))
                Assert.AreNotEqual(UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, m.severity, $"{name}: {m.message}");
            Assert.IsTrue(shader.isSupported, $"{name} 在当前平台不受支持");
        }

        [Test]
        public void WornMaterialsCastShadowsAndHaveDepthPasses()
        {
            foreach (var path in WornMaterials)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.IsNotNull(m, path);
                Assert.AreEqual("BorderRepair/WornSurface", m.shader.name);
                // 注意：batchmode 编辑器里 Material.passCount 反映的不是渲染时生效的 SubShader
                // （URP 自带的 Lit 在同样环境下也只显示 1 个回退 pass），所以直接检查 URP SubShader 的 pass 名称；
                // 渲染时是否真的用上这个 SubShader，由 PlayMode 测试 WornSurfaceRendersWithUrpSubshader 检查。
                var sub = ShaderUtil.GetShaderData(m.shader).GetSubshader(0);
                var names = new System.Collections.Generic.List<string>();
                for (int i = 0; i < sub.PassCount; i++) names.Add(sub.GetPass(i).Name);
                foreach (var pass in new[] { "ForwardLit", "ShadowCaster", "DepthOnly", "DepthNormals" })
                    Assert.IsTrue(names.Exists(n => string.Equals(n, pass, System.StringComparison.OrdinalIgnoreCase)),
                                  $"{path} 缺少 {pass} pass（现有：{string.Join(", ", names)}）");
                Assert.IsTrue(m.GetShaderPassEnabled("ShadowCaster"));
                Assert.IsNotNull(m.GetTexture("_WearMask"), $"{path} 缺少磨损遮罩");
            }
            var ceramic = AssetDatabase.LoadAssetAtPath<Material>(WornMaterials[1]);
            Assert.IsTrue(ceramic.IsKeywordEnabled("_EMISSION"), "旧陶瓷材质应带少量自发光");
        }

        [Test]
        public void OverlayDoesNotCastShadowsOrWriteDepth()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(OverlayPath);
            Assert.IsNotNull(m);
            var data = ShaderUtil.GetShaderData(m.shader);
            Assert.AreEqual(1, data.SubshaderCount, "叠加层没有回退 SubShader");
            var sub = data.GetSubshader(0);
            Assert.AreEqual(1, sub.PassCount, "叠加层只有一个绘制 pass");
            string passName = sub.GetPass(0).Name.ToUpperInvariant();
            StringAssert.DoesNotContain("SHADOW", passName, "叠加层不应投射阴影");
            StringAssert.DoesNotContain("DEPTH", passName, "叠加层不应写入深度预渲染");
            Assert.GreaterOrEqual(m.renderQueue, 3000, "叠加层应在不透明物体之后绘制");
            var profile = AssetDatabase.LoadAssetAtPath<DiagnosticHighlightProfile>(ProfilePath);
            Assert.IsNotNull(profile);
            Assert.AreEqual(m, profile.overlayMaterial);
        }

        [Test]
        public void FormalPrefabsDoNotReferenceLabMaterials()
        {
            foreach (var path in FormalPrefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path);
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (var mat in r.sharedMaterials)
                    {
                        if (mat == null) continue;
                        StringAssert.DoesNotStartWith("M_Worn_", mat.name, $"{path} 的 {r.name} 被改成了实验材质");
                        Assert.AreNotEqual("M_DiagnosticOverlay", mat.name, $"{path} 的 {r.name} 残留了叠加材质");
                    }
            }
        }

        [TestCase("Assets/BorderRepair/Scenes/RepairStation_Prototype.unity")]
        [TestCase("Assets/BorderRepair/Scenes/Narrative_WorkerHand.unity")]
        public void PlayableScenesDoNotEnableDiagnosticOverlay(string scenePath)
        {
            var guids = AssetDatabase.FindAssets("DiagnosticHighlightSettings t:MonoScript");
            Assert.IsNotEmpty(guids);
            string text = File.ReadAllText(scenePath);
            StringAssert.DoesNotContain(guids[0], text, $"{scenePath} 不应包含 DiagnosticHighlightSettings（保持原风格）");
        }
    }
}
