using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BorderRepair.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.Editor
{
    [Serializable] public class SourceAudit { public PropSource[] props; }
    [Serializable] public class PropSource
    {
        public string id, blendHash, fbxHash;
        public SourceMesh[] meshes;
        public SourceMaterial[] materials;
    }
    [Serializable] public class SourceMesh
    {
        public string name, path;
        public int triangles;
        public bool visible, hasUV;
    }
    [Serializable] public class SourceMaterial
    {
        public string name, baseMap, normalMap, metallicMap, roughnessMap, aoMap, emissionMap;
        public float[] color, emission;
        public float metallic, roughness, alpha, emissionStrength;
        public bool transparent;
    }
    [Serializable] public class ImportItemReport
    {
        public string id, sourceHash;
        public int meshes, trianglesAllStates, trianglesDefault, materialCount, renderersDefault, materialSlotsDefault, createdMaterialAssets;
        public Vector3 sizeMeters;
        public long boundTextureProfilerBytes;
        public bool previewRendered;
        public float previewPixelRange;
        public List<string> problems = new List<string>();
        public List<string> hierarchy = new List<string>();
        public List<ImportedTextureReport> textures = new List<ImportedTextureReport>();
    }
    [Serializable] public class ImportedTextureReport
    {
        public string name, format;
        public int width, height;
        public bool readable, sRGB, normalMap;
    }
    [Serializable] public class ImportReport
    {
        public string scope = "Editor asset import and neutral renders; not target-device performance or VR acceptance";
        public List<ImportItemReport> items = new List<ImportItemReport>();
    }

    public static class RepairPropsImporter
    {
        public const string Root = "Assets/RepairProps";
        public const string ScenePath = Root + "/Scenes/RepairProps_Clinic.unity";
        static readonly string Reports = Path.GetFullPath("Docs/Integration/RepairProps20261017");

        public static void BuildAll()
        {
            Build();
            BuildScene();
        }

        [MenuItem("Border Repair/Repair Props/Open Clinic Test Scene")]
        public static void OpenTestScene()
        {
            if (!File.Exists(ScenePath)) throw new InvalidOperationException("Generate the repair-props test scene first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Border Repair/Repair Props/Import Four Props")]
        public static void Build()
        {
            // Model instances and preview rigs live in a throwaway empty scene, never in the user's open scene.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory(Reports);
            var audit = JsonUtility.FromJson<SourceAudit>(File.ReadAllText(Root + "/SourceData/source_audit.json"));
            var report = new ImportReport();
            foreach (var prop in audit.props) report.items.Add(Import(prop));
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(Reports, "unity_import_report.json"), JsonUtility.ToJson(report, true));
            int failures = report.items.Sum(p => p.problems.Count);
            Debug.Log($"[RepairProps] Imported {report.items.Count} props, {failures} problems. See {Reports}");
            if (failures != 0) throw new InvalidOperationException("Repair props import has issues; inspect report.");
        }

        [MenuItem("Border Repair/Repair Props/Build Clinic Test Scene")]
        public static void BuildScene()
        {
            var factory = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("BorderRepair.RepairProps.Editor.RepairPropCaseFactory"))
                .FirstOrDefault(t => t != null);
            if (factory == null) throw new InvalidOperationException("Repair case factory is not ready.");
            factory.GetMethod("BuildAll").Invoke(null, null);
            const string source = "Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic.unity";
            if (!File.Exists(ScenePath) && !AssetDatabase.CopyAsset(source, ScenePath))
                throw new InvalidOperationException("Cannot copy shared clinic scene.");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            // Opening a scene can release previously loaded assets; bind from the persistent asset after the switch.
            var shift = AssetDatabase.LoadAssetAtPath<RepairShiftData>(Root + "/Data/RepairProps_Shift.asset");
            if (shift == null || shift.cases.Count != 4 || !EditorUtility.IsPersistent(shift))
                throw new InvalidOperationException("Persistent four-item shift missing after opening test scene.");
            int configured = 0;
            var roots = scene.GetRootGameObjects();
            foreach (var station in roots.SelectMany(r => r.GetComponentsInChildren<RepairStationController>(true)))
            {
                var serialized = new SerializedObject(station);
                serialized.FindProperty("shift").objectReferenceValue = shift;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(station);
                configured++;
            }
            if (configured == 0)
            {
                var named = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == "RepairStation");
                string components = named == null ? "no RepairStation object"
                    : string.Join(", ", named.GetComponents<Component>().Select(c => c == null ? "<missing script>" : c.GetType().FullName));
                throw new InvalidOperationException($"No repair station in scene ({roots.Length} roots; RepairStation: {components}).");
            }
            var bootstrapType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("BorderRepair.RepairProps.RepairPropsClinicBootstrap"))
                .FirstOrDefault(t => t != null);
            if (bootstrapType != null && Object.FindAnyObjectByType(bootstrapType) == null)
                new GameObject("RepairProps_TestSession").AddComponent(bootstrapType);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            // Opt-in editor scene: the build list (UnifiedClinic_Menu first) is left untouched.
            AssetDatabase.SaveAssets();
            // Reload the saved scene so a dropped reference cannot be reported as a successful generation.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var station in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RepairStationController>(true)))
            {
                var saved = new SerializedObject(station).FindProperty("shift").objectReferenceValue;
                if (saved == null || AssetDatabase.GetAssetPath(saved) != Root + "/Data/RepairProps_Shift.asset")
                    throw new InvalidOperationException("Saved test scene lost its four-item shift reference.");
                if (station.Inspector == null || station.Scanner == null || station.View == null)
                    throw new InvalidOperationException("Saved test scene has incomplete station references.");
            }
            Debug.Log("[RepairProps] Four-item shared-clinic test scene built: " + ScenePath);
        }

        static ImportItemReport Import(PropSource prop)
        {
            string modelDir = Root + "/Models/" + prop.id;
            string modelPath = modelDir + "/" + prop.id + ".fbx";
            Directory.CreateDirectory(Root + "/Materials/" + prop.id);
            Directory.CreateDirectory(Root + "/Prefabs");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureTextures(prop, modelDir + "/Textures");
            var mats = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var source in prop.materials)
                mats[source.name] = MakeMaterial(source, prop, modelDir);
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var pair in mats)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            importer.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new InvalidOperationException(modelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = prop.id;
            // Keep the FBX node transforms unchanged; clone the model hierarchy into a writable prefab.
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var sourceByName = prop.meshes.ToDictionary(m => m.name, StringComparer.Ordinal);
            var item = new ImportItemReport { id = prop.id, sourceHash = prop.fbxHash };
            using (var stream = File.OpenRead(modelPath))
            using (var sha = SHA256.Create())
            {
                string actualHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(actualHash, prop.fbxHash, StringComparison.OrdinalIgnoreCase))
                    item.problems.Add("Staged FBX hash differs from the recorded source: " + actualHash);
                item.sourceHash = actualHash;
            }
            var actualTextures = new HashSet<Texture>();
            var actualMaterials = new HashSet<Material>();
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (var r in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) { item.problems.Add(r.name + ": missing mesh"); continue; }
                var mesh = filter.sharedMesh;
                item.meshes++;
                int tris = 0;
                for (int i = 0; i < mesh.subMeshCount; i++) tris += (int)mesh.GetIndexCount(i) / 3;
                item.trianglesAllStates += tris;
                if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord0)) item.problems.Add(r.name + ": missing UV0");
                if (!sourceByName.TryGetValue(r.name, out var state)) item.problems.Add(r.name + ": unknown source mesh");
                else
                {
                    if (state.triangles != tris) item.problems.Add(r.name + ": triangle mismatch " + tris + "/" + state.triangles);
                    r.gameObject.SetActive(state.visible);
                }
                var remapped = r.sharedMaterials;
                for (int i = 0; i < remapped.Length; i++)
                {
                    var m = remapped[i];
                    if (m == null) { item.problems.Add(r.name + ": missing material"); continue; }
                    string name = m.name.Replace(" (Instance)", "");
                    if (mats.TryGetValue(name, out var owned)) remapped[i] = owned;
                    else if (!mats.Values.Contains(m)) item.problems.Add(r.name + ": unmapped material " + name);
                    actualMaterials.Add(remapped[i]);
                    foreach (var property in remapped[i].GetTexturePropertyNames())
                        if (remapped[i].GetTexture(property) is Texture texture) actualTextures.Add(texture);
                }
                r.sharedMaterials = remapped;
                r.shadowCastingMode = r.name.Contains("LightCurtain") ? ShadowCastingMode.Off : ShadowCastingMode.On;
                if (r.gameObject.activeInHierarchy)
                {
                    item.trianglesDefault += tris;
                    item.renderersDefault++;
                    item.materialSlotsDefault += remapped.Length;
                    if (!hasBounds) { bounds = r.bounds; hasBounds = true; } else bounds.Encapsulate(r.bounds);
                }
                item.hierarchy.Add(HierarchyPath(r.transform));
            }
            item.materialCount = actualMaterials.Count;
            item.createdMaterialAssets = mats.Count;
            item.sizeMeters = bounds.size;
            foreach (var t in actualTextures.OrderBy(t => t.name))
            {
                item.boundTextureProfilerBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                if (!(t is Texture2D texture)) continue;
                var settings = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
                item.textures.Add(new ImportedTextureReport {
                    name = t.name, width = texture.width, height = texture.height,
                    format = texture.graphicsFormat.ToString(), readable = texture.isReadable,
                    sRGB = settings != null && settings.sRGBTexture,
                    normalMap = settings != null && settings.textureType == TextureImporterType.NormalMap
                });
                if (texture.isReadable) item.problems.Add(t.name + ": unexpected readable runtime texture");
                if (prop.id == "Receiver" && t.name == "RK_RX_Exterior_V2_basecolor" && texture.width != 4096)
                    item.problems.Add("Receiver service-label albedo was not retained at 4096 pixels.");
            }
            PrefabUtility.SaveAsPrefabAsset(instance, Root + "/Prefabs/" + prop.id + ".prefab");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                item.previewPixelRange = Capture(instance, prop.id);
                item.previewRendered = true;
                if (item.previewPixelRange < .03f) item.problems.Add("Neutral preview appears blank; visual review required.");
            }
            Object.DestroyImmediate(instance);
            return item;
        }

        static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse(); return string.Join("/", parts);
        }

        static void ConfigureTextures(PropSource prop, string folder)
        {
            var normals = new HashSet<string>(prop.materials.Select(m => m.normalMap));
            var linear = new HashSet<string>(prop.materials.SelectMany(m => new[] { m.metallicMap, m.roughnessMap, m.aoMap }));
            foreach (var path in Directory.GetFiles(folder))
            {
                if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                var ti = AssetImporter.GetAtPath(path.Replace('\\', '/')) as TextureImporter;
                if (ti == null) continue;
                bool normal = normals.Contains(name);
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal && !linear.Contains(name) && !name.Contains("metallicSmoothness");
                ti.isReadable = false;
                ti.mipmapEnabled = true;
                ti.maxTextureSize = prop.id == "Omamori" || prop.id == "Cartridge" ? 1024 : 2048;
                // A 2K receiver label loses its service text at repair distance; keep only this albedo at 4K.
                if (prop.id == "Receiver" && name == "RK_RX_Exterior_V2_basecolor") ti.maxTextureSize = 4096;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                var desktop = ti.GetPlatformTextureSettings("Standalone");
                desktop.overridden = true; desktop.name = "Standalone";
                desktop.maxTextureSize = ti.maxTextureSize;
                desktop.format = normal ? TextureImporterFormat.BC5 : TextureImporterFormat.BC7;
                ti.SetPlatformTextureSettings(desktop);
                ti.SaveAndReimport();
            }
        }

        static Texture2D Texture(string directory, string image)
        {
            if (string.IsNullOrEmpty(image)) return null;
            // Tripo shell maps are delivered as .jpg; authored kit maps as .png.
            foreach (string stem in new[] { image, Path.GetFileNameWithoutExtension(image) })
                foreach (string extension in new[] { ".png", ".jpg", ".jpeg", ".tga" })
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(directory + "/Textures/" + stem + extension);
                    if (texture != null) return texture;
                }
            return null;
        }

        static Color ColorOf(float[] c, Color fallback) => c != null && c.Length >= 3
            ? new Color(c[0], c[1], c[2], c.Length > 3 ? c[3] : 1) : fallback;

        static Texture2D RequiredTexture(string directory, string image)
        {
            if (string.IsNullOrEmpty(image)) return null;
            var texture = Texture(directory, image);
            if (texture == null) throw new FileNotFoundException("A source material references a missing texture: " + image, directory);
            return texture;
        }

        static Material MakeMaterial(SourceMaterial source, PropSource prop, string directory)
        {
            string path = Root + "/Materials/" + prop.id + "/" + source.name.Replace('/', '_') + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader; material.name = source.name;
            material.SetColor("_BaseColor", ColorOf(source.color, Color.white));
            material.SetTexture("_BaseMap", RequiredTexture(directory, source.baseMap));
            material.SetFloat("_Metallic", source.metallic);
            material.SetFloat("_Smoothness", 1 - source.roughness);
            var normal = RequiredTexture(directory, source.normalMap);
            material.SetTexture("_BumpMap", normal);
            if (normal != null) material.EnableKeyword("_NORMALMAP");
            var ao = RequiredTexture(directory, source.aoMap);
            material.SetTexture("_OcclusionMap", ao);
            if (ao != null) material.EnableKeyword("_OCCLUSIONMAP");
            var packed = Texture(directory, source.baseMap.Replace("_basecolor", "_metallicSmoothness_URP"));
            if (source.baseMap.EndsWith("_basecolor") && packed != null)
            {
                material.SetTexture("_MetallicGlossMap", packed);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.SetFloat("_Smoothness", 1);
            }
            if (source.emissionStrength > 0)
            {
                material.EnableKeyword("_EMISSION");
                material.SetTexture("_EmissionMap", RequiredTexture(directory, source.emissionMap));
                material.SetColor("_EmissionColor", ColorOf(source.emission, Color.white) * source.emissionStrength);
            }
            if (source.transparent)
            {
                var color = material.GetColor("_BaseColor"); color.a = source.alpha;
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0); material.SetFloat("_Cull", 0);
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static float Capture(GameObject instance, string id)
        {
            // Build() already isolated the instance in an empty scene; the rig is created next to it and removed after.
            Bounds bounds = default; bool found = false;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
            }
            var cameraGo = new GameObject("ImportPreviewCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.backgroundColor = new Color(.14f, .16f, .17f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.nearClipPlane = .001f; camera.farClipPlane = 10;
            float radius = Mathf.Max(bounds.extents.magnitude, .03f);
            camera.orthographic = true; camera.orthographicSize = radius * 1.15f;
            camera.transform.position = bounds.center + new Vector3(.7f, .5f, 1).normalized * radius * 4;
            camera.transform.LookAt(bounds.center);
            var lightGo = new GameObject("ImportPreviewLight");
            var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2.5f;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            var rt = RenderTexture.GetTemporary(1024, 1024, 24);
            camera.targetTexture = rt;
            camera.Render(); camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = rt;
            var png = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
            png.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0); png.Apply();
            float low = 1, high = 0;
            foreach (var pixel in png.GetPixels()) { float value = pixel.grayscale; low = Mathf.Min(low, value); high = Mathf.Max(high, value); }
            File.WriteAllBytes(Path.Combine(Reports, id + "_Unity.png"), png.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(png);
            Object.DestroyImmediate(cameraGo); Object.DestroyImmediate(lightGo);
            return high - low;
        }
    }

    // Import and scene generation write materials, prefabs, data and a scene: run them only from the
    // Border Repair/Repair Props menus (or -executeMethod), never automatically on project open.
}
