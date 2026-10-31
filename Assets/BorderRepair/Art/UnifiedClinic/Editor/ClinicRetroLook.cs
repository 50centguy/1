using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.TwoNight;
using BorderRepair.TwoNight.EditorTools;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BorderRepair.Art.UnifiedClinic.EditorTools
{
    /// <summary>Deterministic, scene-local look with original material/lighting snapshots.</summary>
    public static class ClinicRetroLook
    {
        public const string Root = "Assets/BorderRepair/Art/UnifiedClinic/RetroLook";
        public const string StateName = "RetroClinicLook_2000s";
        public const string RoomProfilePath = Root + "/Room_Profile.asset";
        public const string InspectionProfilePath = Root + "/Inspection_Profile.asset";
        public const string ReportDirectory = "Docs/Integration/UnifiedClinic/RetroLook";
        static readonly string[] Pipelines = { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" };

        [MenuItem("Border Repair/Clinic Base/Retro Look/Apply Soft Japanese 3D Look", priority = 2000)]
        public static void ApplyToScene()
        {
            RejectUnsavedScenes();
            var scene = EditorSceneManager.OpenScene(UnifiedClinicBuilder.ClinicPath, OpenSceneMode.Single);
            Apply(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[RetroClinic] Applied soft Japanese 3D look. Profiles and original snapshots are in " + Root);
        }

        public static void ApplyBatch()
        {
            try { ApplyToScene(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void RejectUnsavedScenes()
        {
            if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save open scene edits before applying or restoring the clinic look.");
        }

        public static void Apply(Scene scene)
        {
            foreach (string sub in new[] { "", "/Materials", "/Textures", "/Renderers" }) Directory.CreateDirectory(Root + sub);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var shader = Shader.Find("Hidden/BorderRepair/RetroClinicDiffusion");
            if (shader == null) throw new InvalidOperationException("Retro diffusion shader is missing.");
            if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Retro diffusion shader compile errors: " +
                string.Join("; ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
            var diffusionMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Diffusion.mat");
            if (diffusionMaterial == null)
            {
                diffusionMaterial = new Material(shader) { name = "RetroClinic_Diffusion" };
                AssetDatabase.CreateAsset(diffusionMaterial, Root + "/Diffusion.mat");
            }
            var bindings = Pipelines.Select(p => InstallRenderer(p, diffusionMaterial)).ToArray();
            var roomProfile = Profile(RoomProfilePath, false);
            var inspectionProfile = Profile(InspectionProfilePath, true);

            var state = FindState(scene);
            bool firstApply = state == null;
            if (firstApply)
            {
                var root = new GameObject(StateName);
                SceneManager.MoveGameObjectToScene(root, scene);
                state = root.AddComponent<RetroClinicLookState>();
                state.ambientMode = RenderSettings.ambientMode;
                state.sky = RenderSettings.ambientSkyColor;
                state.equator = RenderSettings.ambientEquatorColor;
                state.ground = RenderSettings.ambientGroundColor;
                foreach (var light in Components<Light>(scene))
                    state.lights.Add(new RetroClinicLookState.LightState { light = light, color = light.color, intensity = light.intensity });
                foreach (var camera in Components<Camera>(scene))
                {
                    var data = camera.GetUniversalAdditionalCameraData();
                    state.cameras.Add(new RetroClinicLookState.CameraState { camera = camera,
                        rendererIndex = new SerializedObject(data).FindProperty("m_RendererIndex").intValue,
                        volumeMask = data.volumeLayerMask, postProcessing = data.renderPostProcessing });
                }
            }
            RestoreMaterialBindings(scene, state);
            var sources = Components<Renderer>(scene).SelectMany(r => r.sharedMaterials).Where(IsSurface).Distinct().ToArray();
            var materialMap = new Dictionary<Material, Material>();
            var textures = new Dictionary<string, Texture>();
            foreach (var source in sources)
            {
                string path = AssetDatabase.GetAssetPath(source);
                if (string.IsNullOrEmpty(path)) continue;
                string key = AssetDatabase.AssetPathToGUID(path).Substring(0, 8);
                string target = Root + "/Materials/" + source.name + "_" + key + ".mat";
                var styled = AssetDatabase.LoadAssetAtPath<Material>(target);
                if (styled == null) { styled = new Material(source); AssetDatabase.CreateAsset(styled, target); }
                else styled.CopyPropertiesFromMaterial(source);
                styled.name = source.name + "_Retro";
                TuneMaterial(styled, source, textures);
                EditorUtility.SetDirty(styled);
                materialMap.Add(source, styled);
                if (!state.materials.Any(p => p.original == source))
                    state.materials.Add(new RetroClinicLookState.MaterialPair { original = source, styled = styled });
            }
            foreach (var renderer in Components<Renderer>(scene))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && materialMap.TryGetValue(materials[i], out var styled))
                    { materials[i] = styled; changed = true; }
                if (changed)
                {
                    renderer.sharedMaterials = materials;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }
            ConfigureVolume(state.transform, "Room grading", RetroClinicCamera.RoomVolumeLayer, roomProfile);
            ConfigureVolume(state.transform, "Inspection grading", RetroClinicCamera.InspectionVolumeLayer, inspectionProfile);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.18f, .22f, .245f);
            RenderSettings.ambientEquatorColor = new Color(.105f, .135f, .15f);
            RenderSettings.ambientGroundColor = new Color(.055f, .067f, .075f);
            foreach (var baseline in state.lights)
            {
                var light = baseline.light;
                if (light == null) continue;
                light.color = baseline.color;
                light.intensity = baseline.intensity;
                if (baseline.color.r > baseline.color.b * 1.18f)
                    light.color = Color.Lerp(baseline.color, new Color(1f, .93f, .82f), .58f);
                else if (light.name.Contains("Fill") || light.name.Contains("Fluo"))
                    light.color = Color.Lerp(baseline.color, new Color(.69f, .83f, .91f), .25f);
                if (light.name == "Room_Fill (cool, no shadow)") light.intensity *= 1.25f;
                EditorUtility.SetDirty(light);
            }
            var director = Components<UnifiedClinicDirector>(scene).FirstOrDefault();
            foreach (var baseline in state.cameras)
            {
                if (baseline.camera == null) continue;
                var look = baseline.camera.GetComponent<RetroClinicCamera>();
                if (look == null) look = baseline.camera.gameObject.AddComponent<RetroClinicCamera>();
                look.director = director;
                look.inspectionCamera = baseline.camera.transform.parent != null && baseline.camera.transform.parent.name == "ClinicInspectionStage";
                look.originalRendererIndex = baseline.rendererIndex;
                look.originalVolumeMask = baseline.volumeMask;
                look.rendererBindings = bindings;
                baseline.camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                look.ApplyCameraSettings();
                EditorUtility.SetDirty(look);
                EditorUtility.SetDirty(baseline.camera.GetUniversalAdditionalCameraData());
            }
            EditorUtility.SetDirty(state);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(ReportDirectory);
            File.WriteAllText(ReportDirectory + "/applied.txt", $"Applied UTC: {DateTime.UtcNow:O}\nUnity: {Application.unityVersion}\nScene: {scene.path}\n" +
                $"Scene material variants: {materialMap.Count}\nSmoothed texture variants: {textures.Count}\nCamera bindings: {state.cameras.Count}\n" +
                "Source models, source textures, original materials, gameplay code and default renderer indices are preserved.\n" +
                "World: soft diffusion + neutral tonemapping + restrained bloom + cool shadow grading.\n" +
                "Inspection: lower diffusion/bloom/grain; full output resolution; no DOF, motion blur or chromatic aberration.\n");
        }

        static bool IsSurface(Material m) => m != null && m.shader != null &&
            (m.shader.name == "Universal Render Pipeline/Simple Lit" || m.shader.name == "Universal Render Pipeline/Lit");

        static void TuneMaterial(Material material, Material source, Dictionary<string, Texture> textures)
        {
            string name = source.name.ToLowerInvariant();
            bool textSurface = new[] { "screen", "label", "seal", "text", "paper", "pcb", "mainboard" }.Any(name.Contains);
            if (!textSurface && source.HasProperty("_BaseMap") && source.GetTexture("_BaseMap") != null)
                material.SetTexture("_BaseMap", SmoothTexture(source.GetTexture("_BaseMap"), textures));
            if (material.HasProperty("_BaseColor"))
            {
                Color tint = name.Contains("wall") || name.Contains("ceiling") ? new Color(.88f, .96f, 1f) :
                    name.Contains("rubber") ? new Color(.96f, .98f, 1f) : new Color(.97f, .98f, 1f);
                material.SetColor("_BaseColor", source.GetColor("_BaseColor") * tint);
            }
            if (material.shader.name == "Universal Render Pipeline/Simple Lit" && !textSurface)
            {
                float specular = name.Contains("rubber") || name.Contains("fabric") || name.Contains("wood") ? .035f :
                    name.Contains("steel") || name.Contains("metal") || name.Contains("chrome") ? .2f : .11f;
                material.SetColor("_SpecColor", new Color(specular, specular, specular));
                material.SetFloat("_Smoothness", name.Contains("steel") || name.Contains("metal") ? .33f : .22f);
                material.SetFloat("_SpecularHighlights", 1);
                material.EnableKeyword("_SPECULAR_COLOR");
            }
            if (material.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", Mathf.Min(source.GetFloat("_BumpScale"), .65f));
            if (material.HasProperty("_EmissionColor") && source.GetColor("_EmissionColor").maxColorComponent > 0)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }

        static Texture SmoothTexture(Texture source, Dictionary<string, Texture> cache)
        {
            string path = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(path) || !(AssetImporter.GetAtPath(path) is TextureImporter)) return source;
            if (cache.TryGetValue(path, out var cached)) return cached;
            string key = AssetDatabase.AssetPathToGUID(path).Substring(0, 8);
            string target = Root + "/Textures/" + Path.GetFileNameWithoutExtension(path) + "_" + key + Path.GetExtension(path);
            if (!File.Exists(target) && !AssetDatabase.CopyAsset(path, target)) throw new IOException("Cannot create texture variant: " + path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(target);
            if (importer.filterMode != FilterMode.Bilinear || importer.anisoLevel != 2)
            { importer.filterMode = FilterMode.Bilinear; importer.mipmapEnabled = true; importer.anisoLevel = 2; importer.SaveAndReimport(); }
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(target);
            cache.Add(path, texture);
            return texture;
        }

        static RetroClinicCamera.RendererBinding InstallRenderer(string pipelinePath, Material diffusion)
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null) throw new InvalidOperationException("Missing URP asset: " + pipelinePath);
            var serialized = new SerializedObject(pipeline);
            var list = serialized.FindProperty("m_RendererDataList");
            int defaultIndex = serialized.FindProperty("m_DefaultRendererIndex").intValue;
            string rendererPath = Root + "/Renderers/" + pipeline.name + "_RetroRenderer.asset";
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
            if (data == null)
            {
                string source = AssetDatabase.GetAssetPath(list.GetArrayElementAtIndex(defaultIndex).objectReferenceValue);
                if (!AssetDatabase.CopyAsset(source, rendererPath)) throw new IOException("Cannot clone renderer: " + source);
                data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
            }
            var feature = data.rendererFeatures.OfType<RetroClinicRendererFeature>().FirstOrDefault();
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<RetroClinicRendererFeature>();
                feature.name = "RetroClinic soft diffusion (marked cameras only)";
                AssetDatabase.AddObjectToAsset(feature, data);
                data.rendererFeatures.Add(feature);
            }
            feature.Configure(diffusion);
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(data);
            data.SetDirty();
            int index = -1;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) index = i;
            if (index < 0)
            { index = list.arraySize; list.arraySize++; list.GetArrayElementAtIndex(index).objectReferenceValue = data; }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            return new RetroClinicCamera.RendererBinding { pipeline = pipeline, rendererIndex = index };
        }

        static VolumeProfile Profile(string path, bool inspection)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile != null) return profile; // Preserve artist edits when the scene is rebuilt.
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = inspection ? "RetroClinic Inspection (clear)" : "RetroClinic Room (soft)";
            AssetDatabase.CreateAsset(profile, path);
            T Add<T>() where T : VolumeComponent => profile.Add<T>(true);
            Add<Tonemapping>().mode.Override(TonemappingMode.Neutral);
            var grade = Add<ColorAdjustments>();
            grade.postExposure.Override(inspection ? .25f : .35f);
            grade.contrast.Override(inspection ? 3f : 5f);
            grade.saturation.Override(inspection ? -5f : -9f);
            var balance = Add<WhiteBalance>(); balance.temperature.Override(-3f); balance.tint.Override(-1f);
            var ranges = Add<ShadowsMidtonesHighlights>();
            ranges.shadows.Override(new Vector4(.94f, 1.015f, 1.045f, .025f));
            ranges.midtones.Override(new Vector4(1f, 1f, 1f, .015f));
            ranges.highlights.Override(new Vector4(1.025f, 1.008f, .98f, 0));
            var bloom = Add<Bloom>(); bloom.intensity.Override(inspection ? .10f : .24f);
            bloom.threshold.Override(1.05f); bloom.scatter.Override(.63f); bloom.clamp.Override(12f);
            var grain = Add<FilmGrain>(); grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(inspection ? .008f : .028f); grain.response.Override(.85f);
            var vignette = Add<Vignette>(); vignette.intensity.Override(inspection ? .07f : .13f);
            vignette.smoothness.Override(.55f); vignette.color.Override(new Color(.018f, .025f, .03f));
            Add<DepthOfField>().mode.Override(DepthOfFieldMode.Off);
            Add<MotionBlur>().intensity.Override(0);
            Add<ChromaticAberration>().intensity.Override(0);
            foreach (var component in profile.components) { AssetDatabase.AddObjectToAsset(component, profile); EditorUtility.SetDirty(component); }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static void ConfigureVolume(Transform parent, string name, int layer, VolumeProfile profile)
        {
            var child = parent.Find(name);
            if (child == null) { child = new GameObject(name).transform; child.SetParent(parent, false); }
            child.gameObject.layer = layer;
            var volume = child.GetComponent<Volume>();
            if (volume == null) volume = child.gameObject.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 100; volume.weight = 1; volume.sharedProfile = profile;
        }

        public static RetroClinicLookState FindState(Scene scene) => Components<RetroClinicLookState>(scene).FirstOrDefault();
        static IEnumerable<T> Components<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true));

        static void RestoreMaterialBindings(Scene scene, RetroClinicLookState state)
        {
            var original = state.materials.Where(p => p.styled != null).ToDictionary(p => p.styled, p => p.original);
            foreach (var renderer in Components<Renderer>(scene))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && original.TryGetValue(materials[i], out var source)) { materials[i] = source; changed = true; }
                if (changed) { renderer.sharedMaterials = materials; PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); }
            }
        }

        public static void Restore(Scene scene)
        {
            var state = FindState(scene);
            if (state == null) return;
            RestoreMaterialBindings(scene, state);
            RenderSettings.ambientMode = state.ambientMode;
            RenderSettings.ambientSkyColor = state.sky;
            RenderSettings.ambientEquatorColor = state.equator;
            RenderSettings.ambientGroundColor = state.ground;
            foreach (var light in state.lights)
                if (light.light != null) { light.light.color = light.color; light.light.intensity = light.intensity; }
            foreach (var camera in state.cameras)
            {
                if (camera.camera == null) continue;
                var marker = camera.camera.GetComponent<RetroClinicCamera>();
                if (marker != null) Object.DestroyImmediate(marker);
                var data = camera.camera.GetUniversalAdditionalCameraData();
                data.SetRenderer(camera.rendererIndex); data.volumeLayerMask = camera.volumeMask;
                data.renderPostProcessing = camera.postProcessing;
            }
            Object.DestroyImmediate(state.gameObject);
        }

        [MenuItem("Border Repair/Clinic Base/Retro Look/Restore Previous Look", priority = 2000)]
        public static void RestoreToScene()
        {
            RejectUnsavedScenes();
            var scene = EditorSceneManager.OpenScene(UnifiedClinicBuilder.ClinicPath, OpenSceneMode.Single);
            Restore(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        [MenuItem("Border Repair/Old/Unified Clinic/Retro Look/Build Preview Player", priority = 10500)]
        public static void BuildPlayer()
        {
            string output = "Builds/UnifiedClinic-20261009-RetroLook";
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { UnifiedClinicBuilder.MenuPath, UnifiedClinicBuilder.ClinicPath },
                locationPathName = output + "/BorderRepairClinic.exe", target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            Directory.CreateDirectory(ReportDirectory);
            File.WriteAllText(ReportDirectory + "/player_build.txt", $"{DateTime.UtcNow:O}\nResult: {report.summary.result}\n" +
                $"Errors: {report.summary.totalErrors}\nWarnings: {report.summary.totalWarnings}\nPath: {report.summary.outputPath}\n");
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Retro clinic player build failed.");
        }

        public static void BuildPlayerBatch()
        {
            try { BuildPlayer(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
