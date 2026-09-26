using System.IO;
using System.Linq;
using BorderRepair.Tools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.Tests
{
    /// <summary>
    /// 检测仪屏幕 shader（BorderRepair/DiagnosticScreen）：编译无误、pass 齐全、只用物体 UV；
    /// 三个预设材质颜色正确；叙事场景的检测仪已接上预设；默认场景不受影响。
    /// </summary>
    public class DiagnosticScreenAssetTests
    {
        const string ShaderName = "BorderRepair/DiagnosticScreen";
        const string ShaderPath = "Assets/BorderRepair/Shaders/DiagnosticScreen.shader";
        const string InputPath = "Assets/BorderRepair/Shaders/DiagnosticScreenInput.hlsl";
        const string MaterialDir = "Assets/BorderRepair/Art/WorkerHand/Materials/Screen";
        const string NarrativeScene = "Assets/BorderRepair/Scenes/Narrative_WorkerHand.unity";
        const string DefaultScene = "Assets/BorderRepair/Scenes/RepairStation_Prototype.unity";
        static readonly string[] Presets = { "M_TesterScreen_Ready", "M_TesterScreen_Bypass", "M_TesterScreen_Log" };

        static Material Preset(int i) => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDir}/{Presets[i]}.mat");

        [Test]
        public void ShaderCompilesWithoutErrors()
        {
            var shader = Shader.Find(ShaderName);
            Assert.IsNotNull(shader, $"找不到 shader {ShaderName}");
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), $"{ShaderName} 有编译错误");
            foreach (var m in ShaderUtil.GetShaderMessages(shader))
                Assert.AreNotEqual(UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, m.severity, $"{ShaderName}: {m.message}");
            Assert.IsTrue(shader.isSupported, $"{ShaderName} 在当前平台不受支持");
        }

        [Test]
        public void UrpSubshaderHasForwardAndDepthPasses()
        {
            // batchmode 编辑器里 Material.passCount 反映的是回退 SubShader，直接检查 URP SubShader 的 pass
            var sub = ShaderUtil.GetShaderData(Shader.Find(ShaderName)).GetSubshader(0);
            var names = Enumerable.Range(0, sub.PassCount).Select(i => sub.GetPass(i).Name).ToArray();
            CollectionAssert.IsSupersetOf(names, new[] { "Unlit", "ShadowCaster", "DepthOnly", "DepthNormals" }, string.Join(", ", names));
        }

        [Test]
        public void ShaderUsesOnlyObjectUvNotScreenCoordinates()
        {
            // 以后 VR 双眼渲染时两眼结果一致：不能用屏幕坐标 / 屏幕尺寸 / 片元的裁剪空间位置
            string src = File.ReadAllText(ShaderPath) + File.ReadAllText(InputPath);
            foreach (var token in new[] { "_ScreenParams", "_ScaledScreenParams", "GetNormalizedScreenSpaceUV", "ComputeScreenPos", "positionNDC", "VPOS", "input.positionCS" })
                Assert.IsFalse(src.Contains(token), $"DiagnosticScreen 不应依赖屏幕坐标（发现 {token}）");
            StringAssert.Contains("UNITY_VERTEX_OUTPUT_STEREO", src, "保留单通道立体渲染所需的宏");
        }

        [Test]
        public void ThreePresetsHaveDistinctStateColors()
        {
            var mats = Enumerable.Range(0, 3).Select(Preset).ToArray();
            for (int i = 0; i < 3; i++)
            {
                Assert.IsNotNull(mats[i], Presets[i]);
                Assert.AreEqual(ShaderName, mats[i].shader.name, Presets[i]);
                Assert.IsNotNull(mats[i].GetTexture("_BaseMap"), $"{Presets[i]} 缺少读数贴图集");
            }
            Color ready = mats[0].GetColor("_InkColor"), bypass = mats[1].GetColor("_InkColor"), log = mats[2].GetColor("_InkColor");
            Assert.IsTrue(ready.g > ready.r * 2f && ready.g > ready.b * 2f && ready.maxColorComponent < 1.2f, $"READY 应为暗绿：{ready}");
            Assert.IsTrue(bypass.r > bypass.g * 2f && bypass.g > bypass.b, $"BYPASS 应为警示红橙：{bypass}");
            Assert.IsTrue(Mathf.Min(log.r, log.g, log.b) > 0.6f * log.maxColorComponent, $"LOG 应为冷白：{log}");
            Assert.IsTrue(log.b >= log.r, $"LOG 偏冷：{log}");
            foreach (var m in mats)
            {
                var sw = m.GetVector("_Switch");
                Assert.That(sw.x, Is.InRange(0.05f, 0.35f), $"{m.name}：切换跳变应短暂");
                Assert.That(m.GetVector("_Scan").y, Is.LessThanOrEqualTo(0.15f), $"{m.name}：扫描线应很轻");
                Assert.That(m.GetVector("_Grid").z, Is.LessThanOrEqualTo(0.25f), $"{m.name}：像素栅格不能压过文字");
            }
        }

        [Test]
        public void NarrativeTesterUsesPresetsAndScreenMeshFitsTheRect()
        {
            var scene = EditorSceneManager.OpenScene(NarrativeScene, OpenSceneMode.Additive);
            try
            {
                var readout = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TesterReadout>(true)).Single();
                var so = new SerializedObject(readout);
                var arr = so.FindProperty("presets");
                Assert.AreEqual(3, arr.arraySize);
                for (int i = 0; i < 3; i++) Assert.AreEqual(Preset(i), arr.GetArrayElementAtIndex(i).objectReferenceValue, Presets[i]);
                Assert.IsTrue(so.FindProperty("usePresets").boolValue);

                var screen = readout.Screen;
                Assert.AreEqual(1, screen.sharedMaterials.Length, "屏幕渲染器只能有一个材质槽（换材质不会影响机身）");
                Assert.AreEqual("M_Hand_Decal", screen.sharedMaterial.name, "场景里保存的仍是原材质，预设只在运行时换上");
                var uv = screen.GetComponent<MeshFilter>().sharedMesh.uv;
                Vector2 min = new Vector2(uv.Min(u => u.x), uv.Min(u => u.y)), max = new Vector2(uv.Max(u => u.x), uv.Max(u => u.y));
                var rect = Preset(0).GetVector("_ScreenRect");
                Assert.AreEqual(rect.x, min.x, 2e-3f); Assert.AreEqual(rect.y, min.y, 2e-3f);
                Assert.AreEqual(rect.x + rect.z, max.x, 2e-3f); Assert.AreEqual(rect.y + rect.w, max.y, 2e-3f);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void DefaultSceneDoesNotReferenceTheScreenPresets()
        {
            string text = File.ReadAllText(DefaultScene);
            foreach (var name in Presets)
                StringAssert.DoesNotContain(AssetDatabase.AssetPathToGUID($"{MaterialDir}/{name}.mat"), text, name);
        }
    }
}
