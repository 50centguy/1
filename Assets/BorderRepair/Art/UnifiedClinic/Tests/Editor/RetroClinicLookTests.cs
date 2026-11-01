using System.IO;
using System.Linq;
using BorderRepair.Art.UnifiedClinic.EditorTools;
using BorderRepair.TwoNight.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BorderRepair.Art.UnifiedClinic.Tests
{
    public class RetroClinicLookTests
    {
        [Test]
        public void ReapplyIsIdempotentAndRestorePreservesOriginalMaterialsAndLights()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.OpenScene(UnifiedClinicBuilder.ClinicPath, OpenSceneMode.Single);
            try
            {
                ClinicRetroLook.Restore(scene);
                var renderers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToArray();
                var materialBindings = renderers.Select(r => r.sharedMaterials.ToArray()).ToArray();
                var lights = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).ToArray();
                var colors = lights.Select(l => l.color).ToArray();
                var intensities = lights.Select(l => l.intensity).ToArray();
                var sourceFiles = materialBindings.SelectMany(m => m).Where(m => m != null)
                    .Select(AssetDatabase.GetAssetPath).Where(File.Exists).Distinct()
                    .ToDictionary(p => p, File.ReadAllBytes);
                ClinicRetroLook.Apply(scene);
                int variantCount = ClinicRetroLook.FindState(scene).materials.Count;
                ClinicRetroLook.Apply(scene);
                Assert.AreEqual(variantCount, ClinicRetroLook.FindState(scene).materials.Count, "Reapply must not create variant chains.");
                Assert.AreEqual(1, scene.GetRootGameObjects().Count(g => g.name == ClinicRetroLook.StateName));
                foreach (var file in sourceFiles) CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(file.Key), file.Key);
                foreach (var camera in ClinicRetroLook.FindState(scene).cameras)
                    foreach (var binding in camera.camera.GetComponent<RetroClinicCamera>().rendererBindings)
                    {
                        var pipeline = new SerializedObject(binding.pipeline);
                        Assert.AreEqual(0, pipeline.FindProperty("m_DefaultRendererIndex").intValue, "Other scenes must retain the default renderer.");
                    }
                ClinicRetroLook.Restore(scene);
                Assert.IsNull(ClinicRetroLook.FindState(scene));
                for (int i = 0; i < renderers.Length; i++) CollectionAssert.AreEqual(materialBindings[i], renderers[i].sharedMaterials);
                for (int i = 0; i < lights.Length; i++) { Assert.AreEqual(colors[i], lights[i].color); Assert.AreEqual(intensities[i], lights[i].intensity); }
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (setup.Any(s => s.isLoaded && !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        [Test]
        public void GradingFallsBackToBuiltInValuesWhenAssetOrFieldIsUnusable()
        {
            Assert.AreEqual(0f, RetroGradingSettings.Resolve(null, false).postExposure);
            Assert.AreEqual(.1f, RetroGradingSettings.Resolve(null, true).postExposure);
            var settings = ScriptableObject.CreateInstance<RetroGradingSettings>();
            try
            {
                Assert.AreEqual(0f, RetroGradingSettings.Resolve(settings, false).postExposure);
                Assert.AreEqual(.1f, RetroGradingSettings.Resolve(settings, true).postExposure);
                settings.room.postExposure = float.NaN;
                settings.inspection = null;
                Assert.AreEqual(0f, RetroGradingSettings.Resolve(settings, false).postExposure);
                Assert.AreEqual(.24f, RetroGradingSettings.Resolve(settings, false).bloomIntensity);
                Assert.AreEqual(.1f, RetroGradingSettings.Resolve(settings, true).postExposure);
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void InspectionCameraUsesClearProfileAndDiffusionShaderCompiles()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.OpenScene(UnifiedClinicBuilder.ClinicPath, OpenSceneMode.Single);
            try
            {
                var state = ClinicRetroLook.FindState(scene);
                Assert.IsNotNull(state);
                var looks = state.cameras.Select(s => s.camera.GetComponent<RetroClinicCamera>()).ToArray();
                Assert.IsTrue(looks.Any(l => l.inspectionCamera));
                foreach (var look in looks)
                {
                    look.ApplyCameraSettings();
                    var data = look.GetComponent<Camera>().GetUniversalAdditionalCameraData();
                    int expectedLayer = look.inspectionCamera ? RetroClinicCamera.InspectionVolumeLayer : RetroClinicCamera.RoomVolumeLayer;
                    int otherLayer = look.inspectionCamera ? RetroClinicCamera.RoomVolumeLayer : RetroClinicCamera.InspectionVolumeLayer;
                    Assert.AreNotEqual(0, data.volumeLayerMask.value & (1 << expectedLayer));
                    Assert.AreEqual(0, data.volumeLayerMask.value & (1 << otherLayer));
                    if (look.inspectionCamera) Assert.Less(look.EffectiveDiffusion, .04f);
                }
                var shader = Shader.Find("Hidden/BorderRepair/RetroClinicDiffusion");
                Assert.IsNotNull(shader);
                Assert.IsFalse(ShaderUtil.ShaderHasError(shader), string.Join("; ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
                var room = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(ClinicRetroLook.RoomProfilePath);
                var inspection = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(ClinicRetroLook.InspectionProfilePath);
                Assert.IsTrue(room.TryGet<Bloom>(out var roomBloom));
                Assert.IsTrue(inspection.TryGet<Bloom>(out var inspectionBloom));
                Assert.Less(inspectionBloom.intensity.value, roomBloom.intensity.value);
                Assert.IsTrue(inspection.TryGet<DepthOfField>(out var dof));
                Assert.AreEqual(DepthOfFieldMode.Off, dof.mode.value);
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (setup.Any(s => s.isLoaded && !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }
    }
}
