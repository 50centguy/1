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
            var gradingSettings = RetroGradingSettings.Load();
            var roomGrading = RetroGradingSettings.Resolve(gradingSettings, false);
            var inspectionGrading = RetroGradingSettings.Resolve(gradingSettings, true);
            var roomProfile = Profile(RoomProfilePath, false, roomGrading);
            var inspectionProfile = Profile(InspectionProfilePath, true, inspectionGrading);

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
            // Shell bindings are sticky: a scene that was rebound to the shell materials keeps them across re-apply.
            bool keepShell = state.shellSlots.Count > 0 || state.materials.Any(p => IsShellMaterial(p.styled));
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
                // Match the exact pair: one original may legitimately own several styled partners (Retro variant + shell).
                if (!state.materials.Any(p => p.original == source && p.styled == styled))
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
            if (keepShell) BindShellSlots(scene, state, LoadShellMaterials(), false, out _);
            ConfigureVolume(state.transform, "Room grading", RetroClinicCamera.RoomVolumeLayer, roomProfile, roomGrading);
            ConfigureVolume(state.transform, "Inspection grading", RetroClinicCamera.InspectionVolumeLayer, inspectionProfile, inspectionGrading);
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

        static VolumeProfile Profile(string path, bool inspection, RetroGradingSettings.Grading grading)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            // Preserve artist edits when the scene is rebuilt; only the RetroGradingSettings fields are re-written (ConfigureVolume).
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = inspection ? "RetroClinic Inspection (clear)" : "RetroClinic Room (soft)";
            AssetDatabase.CreateAsset(profile, path);
            T Add<T>() where T : VolumeComponent => profile.Add<T>(true);
            Add<Tonemapping>().mode.Override(TonemappingMode.Neutral);
            var grade = Add<ColorAdjustments>();
            grade.contrast.Override(inspection ? 3f : 5f);
            grade.saturation.Override(inspection ? -5f : -9f);
            var balance = Add<WhiteBalance>(); balance.temperature.Override(-3f); balance.tint.Override(-1f);
            var ranges = Add<ShadowsMidtonesHighlights>();
            ranges.shadows.Override(new Vector4(.94f, 1.015f, 1.045f, .025f));
            ranges.midtones.Override(new Vector4(1f, 1f, 1f, .015f));
            ranges.highlights.Override(new Vector4(1.025f, 1.008f, .98f, 0));
            Add<Bloom>();
            var grain = Add<FilmGrain>(); grain.type.Override(FilmGrainLookup.Thin1);
            grain.response.Override(.85f);
            var vignette = Add<Vignette>();
            vignette.smoothness.Override(.55f); vignette.color.Override(new Color(.018f, .025f, .03f));
            Add<DepthOfField>().mode.Override(DepthOfFieldMode.Off);
            Add<MotionBlur>().intensity.Override(0);
            Add<ChromaticAberration>().intensity.Override(0);
            ApplyGrading(profile, grading);
            foreach (var component in profile.components) { AssetDatabase.AddObjectToAsset(component, profile); EditorUtility.SetDirty(component); }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>Writes only the RetroGradingSettings fields; every other profile parameter is left alone.</summary>
        static void ApplyGrading(VolumeProfile profile, RetroGradingSettings.Grading grading)
        {
            if (profile.TryGet<ColorAdjustments>(out var grade))
            {
                grade.postExposure.Override(grading.postExposure);
                EditorUtility.SetDirty(grade);
            }
            else Debug.LogWarning("[RetroClinic] " + profile.name + " has no ColorAdjustments; postExposure not applied.");
            if (profile.TryGet<Bloom>(out var bloom))
            {
                bloom.intensity.Override(grading.bloomIntensity);
                bloom.threshold.Override(grading.bloomThreshold);
                bloom.scatter.Override(grading.bloomScatter);
                bloom.clamp.Override(grading.bloomClamp);
                EditorUtility.SetDirty(bloom);
            }
            else Debug.LogWarning("[RetroClinic] " + profile.name + " has no Bloom; bloom settings not applied.");
            if (profile.TryGet<Vignette>(out var vignette))
            {
                vignette.intensity.Override(grading.vignetteIntensity);
                EditorUtility.SetDirty(vignette);
            }
            else Debug.LogWarning("[RetroClinic] " + profile.name + " has no Vignette; vignette intensity not applied.");
            if (profile.TryGet<FilmGrain>(out var grain))
            {
                grain.intensity.Override(grading.grainIntensity);
                EditorUtility.SetDirty(grain);
            }
            else Debug.LogWarning("[RetroClinic] " + profile.name + " has no FilmGrain; grain intensity not applied.");
        }

        /// <summary>Pushes the settings into the two existing profile assets. They are shared by every clinic scene, so no scene is opened or re-applied.</summary>
        public static void ApplyGradingToProfiles(RetroGradingSettings settings)
        {
            void Push(string path, bool inspection)
            {
                var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
                if (profile == null) { Debug.LogWarning("[RetroClinic] Volume profile not found: " + path); return; }
                ApplyGrading(profile, RetroGradingSettings.Resolve(settings, inspection));
                EditorUtility.SetDirty(profile);
            }
            Push(RoomProfilePath, false);
            Push(InspectionProfilePath, true);
        }

        static void ConfigureVolume(Transform parent, string name, int layer, VolumeProfile profile, RetroGradingSettings.Grading grading)
        {
            ApplyGrading(profile, grading);
            EditorUtility.SetDirty(profile);
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
            // 1) shell slots back to the exact material they replaced (normally the Retro variant);
            // 2) every styled material (Retro variants and any stray shell binding) back to its original.
            UndoShellSlots(state);
            var original = new Dictionary<Material, Material>();
            foreach (var pair in state.materials)
                if (pair.styled != null && pair.original != null && !original.ContainsKey(pair.styled))
                    original.Add(pair.styled, pair.original);
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

        // ------------------------------------------------------------------------------------------
        // Clinic shell: floor / wall / ceiling rebound from their Retro variants to BorderRepair/ClinicShell.
        // Every rebound slot is recorded (renderer, slot, exact previous material) so UnbindShell and
        // Restore put back precisely what was there; the (original, shell) pairs are recorded as well so a
        // full Restore still reaches the original even if a slot record were lost.
        // ------------------------------------------------------------------------------------------
        public const string ShellShaderName = "BorderRepair/ClinicShell";
        public const string ShellRoot = "Assets/BorderRepair/Art/UnifiedClinic/Shell";
        public const string ShellFloorPath = ShellRoot + "/M_UC_Shell_Floor.mat";
        public const string ShellWallPath = ShellRoot + "/M_UC_Shell_Wall.mat";
        public const string ShellCeilingPath = ShellRoot + "/M_UC_Shell_Ceiling.mat";
        public const string FloorSourcePath = "Assets/BorderRepair/Art/UnifiedClinic/Materials/M_UC_Floor.mat";
        public const string WallSourcePath = "Assets/BorderRepair/Art/UnifiedClinic/Materials/M_UC_Wall.mat";
        const float ShellWallThickness = 0.20f;   // build_unified_clinic.py: WT
        const float ShellDefaultApothem = 3.40f;  // build_unified_clinic.py: A
        public static readonly string[] ShellScenePaths =
        {
            "Assets/RepairProps/Scenes/RepairProps_Clinic.unity",
            "Assets/RepairPropsVR/Scenes/RepairProps_Clinic_VR.unity",
        };

        public sealed class ShellMaterials { public Material floor, wall, ceiling; }

        public struct ShellBindResult
        {
            public int slots, floorSlots, wallSlots, ceilingSlots;
            public bool measured;
            public Vector4 roomCenter;   // xyz world; y = floor top
            public float apothem;
        }

        public static ShellMaterials LoadShellMaterials()
        {
            var shader = Shader.Find(ShellShaderName);
            if (shader == null) throw new InvalidOperationException("Clinic shell shader is missing: " + ShellShaderName);
            if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Clinic shell shader compile errors: " +
                string.Join("; ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
            Material Load(string path)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) throw new InvalidOperationException("Clinic shell material is missing: " + path);
                if (material.shader != shader)
                    throw new InvalidOperationException(path + " must use " + ShellShaderName + " (has " + (material.shader != null ? material.shader.name : "none") + ")");
                return material;
            }
            return new ShellMaterials { floor = Load(ShellFloorPath), wall = Load(ShellWallPath), ceiling = Load(ShellCeilingPath) };
        }

        public static bool IsShellMaterial(Material material)
        {
            if (material == null) return false;
            string path = AssetDatabase.GetAssetPath(material);
            return path == ShellFloorPath || path == ShellWallPath || path == ShellCeilingPath;
        }

        static bool IsCeilingRenderer(Renderer renderer)
        {
            if (renderer.name.StartsWith("Ceiling", StringComparison.Ordinal)) return true;
            var size = renderer.bounds.size;   // fallback: a large flat slab that carries the wall material
            return size.y < 0.3f && size.x > 3f && size.z > 3f;
        }

        static Bounds Encapsulate(Bounds? accumulated, Bounds bounds)
        {
            if (!accumulated.HasValue) return bounds;
            var result = accumulated.Value;
            result.Encapsulate(bounds);
            return result;
        }

        /// <summary>Rebinds the Retro variants of M_UC_Floor / M_UC_Wall in an already-styled scene to the shell
        /// materials. Idempotent. With <paramref name="configureMaterials"/> the measured room centre / apothem are
        /// written into the (shared) shell material assets.</summary>
        public static ShellBindResult BindShell(Scene scene, bool configureMaterials = true)
        {
            var state = FindState(scene);
            if (state == null) throw new InvalidOperationException("Scene " + scene.path + " has no " + StateName + "; apply the Retro look first.");
            var shells = LoadShellMaterials();
            BindShellSlots(scene, state, shells, configureMaterials, out var result);
            EditorUtility.SetDirty(state);
            return result;
        }

        static void BindShellSlots(Scene scene, RetroClinicLookState state, ShellMaterials shells, bool configureMaterials, out ShellBindResult result)
        {
            result = default;
            UndoShellSlots(state);   // always start from the exact pre-shell bindings
            var styledToOriginal = new Dictionary<Material, Material>();
            foreach (var pair in state.materials)
                if (pair.styled != null && pair.original != null && !IsShellMaterial(pair.styled) && !styledToOriginal.ContainsKey(pair.styled))
                    styledToOriginal.Add(pair.styled, pair.original);

            Bounds? wallBounds = null, floorBounds = null;
            int floorSlots = 0, wallSlots = 0, ceilingSlots = 0;
            foreach (var renderer in Components<Renderer>(scene))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var current = materials[i];
                    if (current == null || !styledToOriginal.TryGetValue(current, out var original)) continue;
                    string originalPath = AssetDatabase.GetAssetPath(original);
                    Material shell;
                    if (originalPath == FloorSourcePath)
                    {
                        shell = shells.floor;
                        floorSlots++;
                        floorBounds = Encapsulate(floorBounds, renderer.bounds);
                    }
                    else if (originalPath == WallSourcePath)
                    {
                        if (IsCeilingRenderer(renderer)) { shell = shells.ceiling; ceilingSlots++; }
                        else
                        {
                            shell = shells.wall;
                            wallSlots++;
                            if (!renderer.name.StartsWith("Corridor", StringComparison.Ordinal))
                                wallBounds = Encapsulate(wallBounds, renderer.bounds);
                        }
                    }
                    else continue;
                    materials[i] = shell;
                    changed = true;
                    state.shellSlots.Add(new RetroClinicLookState.ShellSlot { renderer = renderer, slot = i, previous = current, shell = shell });
                    if (!state.materials.Any(p => p.original == original && p.styled == shell))
                        state.materials.Add(new RetroClinicLookState.MaterialPair { original = original, styled = shell });
                }
                if (changed)
                {
                    renderer.sharedMaterials = materials;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }
            result.slots = state.shellSlots.Count;
            result.floorSlots = floorSlots;
            result.wallSlots = wallSlots;
            result.ceilingSlots = ceilingSlots;

            if (!wallBounds.HasValue)
            {
                if (configureMaterials) Debug.LogWarning("[ClinicShell] No octagon wall renderers found in " + scene.path + "; room centre not measured.");
                return;
            }
            // Octagon walls: outer faces sit at apothem + wall thickness on both axes, so the combined bounds are
            // centred on the room and their half-extent minus the thickness is the inner apothem.
            var walls = wallBounds.Value;
            float apothem = Mathf.Min(walls.extents.x, walls.extents.z) - ShellWallThickness;
            if (apothem < 2f || apothem > 6f || Mathf.Abs(walls.extents.x - walls.extents.z) > 0.05f)
            {
                Debug.LogWarning($"[ClinicShell] Wall bounds in {scene.path} do not look like the octagon room " +
                                 $"(extents {walls.extents.x:F3} x {walls.extents.z:F3}); apothem falls back to {ShellDefaultApothem} m.");
                apothem = ShellDefaultApothem;
            }
            float floorY = floorBounds.HasValue ? floorBounds.Value.max.y : walls.min.y;
            result.measured = true;
            result.roomCenter = new Vector4(walls.center.x, floorY, walls.center.z, 0f);
            result.apothem = apothem;
            if (configureMaterials)
                foreach (var material in new[] { shells.floor, shells.wall, shells.ceiling })
                {
                    material.SetVector("_RoomCenter", result.roomCenter);
                    material.SetFloat("_RoomApothem", apothem);
                    EditorUtility.SetDirty(material);
                }
        }

        static void UndoShellSlots(RetroClinicLookState state)
        {
            for (int k = state.shellSlots.Count - 1; k >= 0; k--)
            {
                var record = state.shellSlots[k];
                if (record.renderer == null) continue;
                var materials = record.renderer.sharedMaterials;
                if (record.slot < 0 || record.slot >= materials.Length || materials[record.slot] != record.shell) continue;
                materials[record.slot] = record.previous;
                record.renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(record.renderer);
            }
            state.shellSlots.Clear();
        }

        /// <summary>Puts every shell slot back to the exact material it replaced and forgets the shell pairs.
        /// Returns the number of slots changed.</summary>
        public static int UnbindShell(Scene scene)
        {
            var state = FindState(scene);
            if (state == null) return 0;
            int count = state.shellSlots.Count;
            UndoShellSlots(state);
            // Shell bindings without a slot record (hand edits): back to that original's Retro variant.
            var retroOf = new Dictionary<Material, Material>();
            var shellOriginal = new Dictionary<Material, Material>();
            foreach (var pair in state.materials)
            {
                if (pair.original == null || pair.styled == null) continue;
                if (IsShellMaterial(pair.styled)) { if (!shellOriginal.ContainsKey(pair.styled)) shellOriginal.Add(pair.styled, pair.original); }
                else if (!retroOf.ContainsKey(pair.original)) retroOf.Add(pair.original, pair.styled);
            }
            foreach (var renderer in Components<Renderer>(scene))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && shellOriginal.TryGetValue(materials[i], out var original))
                    {
                        materials[i] = retroOf.TryGetValue(original, out var retro) ? retro : original;
                        changed = true;
                        count++;
                    }
                if (changed) { renderer.sharedMaterials = materials; PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); }
            }
            state.materials.RemoveAll(p => IsShellMaterial(p.styled));
            EditorUtility.SetDirty(state);
            return count;
        }

        /// <summary>Menu only (never run automatically): rebinds the Retro wall/floor/ceiling copies in the
        /// RepairProps clinic and its VR copy to the shell materials and saves both scenes.</summary>
        [MenuItem("Border Repair/Clinic Base/Retro Look/Bind Clinic Shell Materials (RepairProps + VR)", priority = 2001)]
        public static void BindShellToScenes()
        {
            RejectUnsavedScenes();
            LoadShellMaterials();   // fail before touching any scene
            var setup = EditorSceneManager.GetSceneManagerSetup();
            ShellBindResult? reference = null;
            try
            {
                foreach (string path in ShellScenePaths)
                {
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    if (FindState(scene) == null)
                    {
                        Debug.LogError("[ClinicShell] " + path + ": no " + StateName + " - skipped (apply the Retro look first).");
                        continue;
                    }
                    // The shell materials are shared: the first measured scene writes the room centre, later scenes are checked against it.
                    var result = BindShell(scene, !reference.HasValue);
                    if (result.measured)
                    {
                        if (!reference.HasValue) reference = result;
                        else if (Vector4.Distance(reference.Value.roomCenter, result.roomCenter) > 0.01f || Mathf.Abs(reference.Value.apothem - result.apothem) > 0.01f)
                            Debug.LogWarning($"[ClinicShell] {path}: room centre {result.roomCenter} / apothem {result.apothem:F3} differs from " +
                                             $"{reference.Value.roomCenter} / {reference.Value.apothem:F3}; the shared shell materials keep the first values.");
                    }
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log($"[ClinicShell] {path}: {result.slots} slots (floor {result.floorSlots}, wall {result.wallSlots}, ceiling {result.ceilingSlots}); " +
                              $"room centre {result.roomCenter}, apothem {result.apothem:F3}");
                }
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (setup.Any(s => s.isLoaded && !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        [MenuItem("Border Repair/Clinic Base/Retro Look/Unbind Clinic Shell Materials (RepairProps + VR)", priority = 2001)]
        public static void UnbindShellFromScenes()
        {
            RejectUnsavedScenes();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string path in ShellScenePaths)
                {
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    if (FindState(scene) == null) continue;
                    int count = UnbindShell(scene);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    Debug.Log($"[ClinicShell] {path}: {count} slots restored to their Retro variants.");
                }
            }
            finally
            {
                if (setup.Any(s => s.isLoaded && !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
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
