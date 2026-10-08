using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BorderRepair.Art.UnifiedClinic.EditorTools
{
    public static class ClinicArtImporter
    {
        public const string Source = "ArtSource/UnifiedClinic";
        public const string Root = "Assets/BorderRepair/Art/UnifiedClinic";
        public const string PrefabPath = Root + "/Prefabs/UnifiedClinicEnvironment.prefab";
        public const string PreviewPath = "Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic_ArtPreview.unity";
        const string Reports = "Docs/Integration/UnifiedClinic/ArtImport";
        static readonly string[] ModelNames = {
            "UnifiedClinic_Environment", "UnifiedClinic_MonitorArm", "UnifiedClinic_CollisionProxies"
        };

        [Serializable] class MaterialFile { public MaterialEntry[] materials; }
        [Serializable] class MaterialEntry
        {
            public string name, baseColor, baseMap, emissionMap, emissionColor, filterMode;
            public float smoothness, emissionStrength;
        }
        [Serializable] public class AnchorFile { public Anchor[] anchors; }
        [Serializable] public class Anchor
        {
            public string name, kind;
            public float[] unity_pos, look_at;
            public float unity_yaw_deg, unity_vertical_fov_deg;
            public Vector3 Position => new Vector3(unity_pos[0], unity_pos[1], unity_pos[2]);
        }
        [Serializable] class ChecksFile { public Check[] checks; }
        [Serializable] class Check { public string name; public string[] hide_in_unity; }
        [Serializable] class ImportReport
        {
            public string timestampUtc, scene, source, unityVersion;
            public long environmentTriangles;
            public int environmentMeshes, materialCount, colliderCount, anchorCount;
            public float maxAnchorErrorMeters;
            public string[] messages, limitations;
        }

        public static AnchorFile ReadAnchors() => JsonUtility.FromJson<AnchorFile>(File.ReadAllText(Source + "/anchors.json"));
        public static string[] BenchHideList() => JsonUtility.FromJson<ChecksFile>(File.ReadAllText(Source + "/checks.json"))
            .checks.Single(c => c.name == "workbench_hide_list").hide_in_unity;

        [MenuItem("Border Repair/Unified Clinic/Import Art And Build Isolated Preview")]
        public static void BuildPreview()
        {
            if (EditorSceneManager.GetSceneManagerSetup().Any(s => s.isLoaded &&
                UnityEngine.SceneManagement.SceneManager.GetSceneByPath(s.path).isDirty))
                throw new InvalidOperationException("Save open scene edits before generating the preview.");
            var messages = new List<string>();
            void Capture(string message, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning)
                    messages.Add(type + ": " + message);
            }
            Application.logMessageReceived += Capture;
            try
            {
                Directory.CreateDirectory(Reports);
                var environment = ImportEnvironment();
                var anchors = ReadAnchors().anchors;
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(environment);
                instance.name = "UnifiedClinicEnvironment";
                float anchorError = ValidateAnchors(instance, anchors);
                PlaceExisting("Assets/WorkbenchArea/Prefabs/WorkbenchArea.prefab", "WorkbenchArea_Root", anchors, BenchHideList());
                PlaceExisting("Assets/BorderRepair/Prefabs/Unit07Dock/Unit07ServiceDock.prefab", "Dock_Root", anchors);
                var robot = PlaceExisting("Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_DockReady.prefab", "DockRobot", anchors);
                // The robot prefab uses Unity-facing axes; the bench and dock retain FBX import rotation.
                robot.transform.rotation = Quaternion.Euler(0, anchors.Single(a => a.name == "DockRobot").unity_yaw_deg, 0);
                foreach (var animator in robot.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(.10f, .13f, .14f);
                RenderSettings.ambientEquatorColor = new Color(.05f, .06f, .07f);
                RenderSettings.ambientGroundColor = new Color(.035f, .035f, .035f);
                RenderSettings.fog = false;
                AddLight("BenchWarm", new Vector3(0, 2.1f, 2.6f), new Color(1, .76f, .48f), 4.5f, 2.5f);
                AddLight("SurgeryTask", new Vector3(-.1f, 1.88f, -.05f), new Color(.83f, .9f, .85f), 3.5f, 2.2f);
                AddLight("TradeWarm", new Vector3(2.4f, 2.1f, 0), new Color(1, .74f, .45f), 3.2f, 2.1f);
                AddLight("DockFill", new Vector3(-2.15f, 2.1f, 0), new Color(.65f, .78f, .79f), 3f, 2.2f);
                AddLight("EntranceFill", new Vector3(0, 2.1f, -2.5f), new Color(.55f, .69f, .71f), 2f, 2.3f);
                var cameras = new List<Camera>();
                foreach (var anchor in anchors.Where(a => a.kind == "camera"))
                {
                    var camera = new GameObject(anchor.name).AddComponent<Camera>();
                    camera.transform.position = anchor.Position;
                    camera.transform.LookAt(new Vector3(anchor.look_at[0], anchor.look_at[1], anchor.look_at[2]));
                    camera.fieldOfView = anchor.unity_vertical_fov_deg;
                    camera.nearClipPlane = .03f;
                    camera.farClipPlane = 30;
                    camera.backgroundColor = new Color(.02f, .02f, .025f);
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.enabled = anchor.name == "Cam_Entrance";
                    if (camera.enabled) { camera.tag = "MainCamera"; camera.gameObject.AddComponent<AudioListener>(); }
                    cameras.Add(camera);
                }
                if (!EditorSceneManager.SaveScene(scene, PreviewPath)) throw new IOException("Cannot save preview scene.");
                foreach (var camera in cameras) RenderShot(camera);
                AssetDatabase.SaveAssets();
                var filters = instance.GetComponentsInChildren<MeshFilter>(true);
                var report = new ImportReport {
                    timestampUtc = DateTime.UtcNow.ToString("O"), source = Source, scene = PreviewPath,
                    unityVersion = Application.unityVersion, environmentTriangles = filters.Sum(f => Triangles(f.sharedMesh)),
                    environmentMeshes = filters.Length, materialCount = instance.GetComponentsInChildren<Renderer>(true)
                        .SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count(),
                    colliderCount = instance.GetComponentsInChildren<MeshCollider>(true).Length,
                    anchorCount = anchors.Length, maxAnchorErrorMeters = anchorError, messages = messages.ToArray(),
                    limitations = new[] {
                        "Isolated art preview only; gameplay routes are not migrated by this builder.",
                        "Real RobotV4 is instantiated, but exact mesh interference is not certified here.",
                        "Editor camera rendering; no human play, VR, GPU timing or target-device measurements.",
                        "Temporary preview lighting is not final art approval."
                    }
                };
                File.WriteAllText(Reports + "/import_report.json", JsonUtility.ToJson(report, true));
                if (messages.Any(m => m.StartsWith("Error:") || m.StartsWith("Exception:")))
                    throw new InvalidOperationException("Art import logged errors. See import_report.json.");
                Debug.Log("[UnifiedClinicArt] Real FBX imported and isolated preview saved: " + PreviewPath);
            }
            finally { Application.logMessageReceived -= Capture; }
        }

        public static void BuildPreviewBatch()
        {
            try { BuildPreview(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static string ModelPath(string name) => Root + "/Models/" + name + ".fbx";
        static void CopySources()
        {
            foreach (string sub in new[] { "Models", "Textures", "Materials", "Prefabs" }) Directory.CreateDirectory(Root + "/" + sub);
            foreach (string model in ModelNames) CopyChanged(Source + "/Export/" + model + ".fbx", ModelPath(model));
            foreach (string file in Directory.GetFiles(Source + "/Textures", "*.png"))
                CopyChanged(file, Root + "/Textures/" + Path.GetFileName(file));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string path in Directory.GetFiles(Root + "/Textures", "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = true;
                importer.isReadable = false;
                importer.maxTextureSize = 512;
                importer.SaveAndReimport();
            }
        }
        static void CopyChanged(string source, string target)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("Generate clinic art first.", source);
            if (!File.Exists(target) || !File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(target))) File.Copy(source, target, true);
        }
        static Dictionary<string, Material> BuildMaterials()
        {
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) throw new InvalidOperationException("URP Simple Lit is unavailable.");
            var result = new Dictionary<string, Material>();
            foreach (var entry in JsonUtility.FromJson<MaterialFile>(File.ReadAllText(Source + "/materials.json")).materials)
            {
                string path = Root + "/Materials/" + entry.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
                material.shader = shader;
                if (!ColorUtility.TryParseHtmlString(entry.baseColor, out var color)) throw new FormatException(entry.baseColor);
                material.SetColor("_BaseColor", color);
                material.SetTexture("_BaseMap", LoadTexture(entry.baseMap));
                material.SetFloat("_SpecularHighlights", 1);
                material.SetColor("_SpecColor", Color.black);
                material.DisableKeyword("_SPECULAR_COLOR");
                material.DisableKeyword("_SPECGLOSSMAP");
                material.SetFloat("_Smoothness", entry.smoothness);
                if (entry.emissionStrength > 0)
                {
                    if (!ColorUtility.TryParseHtmlString(entry.emissionColor, out var emission)) throw new FormatException(entry.emissionColor);
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", emission * Mathf.Min(1, entry.emissionStrength));
                    material.SetTexture("_EmissionMap", LoadTexture(entry.emissionMap));
                }
                else { material.DisableKeyword("_EMISSION"); material.SetColor("_EmissionColor", Color.black); }
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                result.Add(entry.name, material);
            }
            AssetDatabase.SaveAssets();
            return result;
        }
        static Texture2D LoadTexture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + name) ??
                throw new FileNotFoundException("Missing mapped texture.", name);
        }
        static void ConfigureModel(string name, Dictionary<string, Material> materials)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath(name));
            importer.isReadable = false;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.globalScale = 1;
            foreach (var material in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.Key), material.Value);
            importer.SaveAndReimport();
        }
        public static GameObject ImportEnvironment()
        {
            CopySources();
            var materials = BuildMaterials();
            foreach (string name in ModelNames) ConfigureModel(name, materials);
            return MakeEnvironmentPrefab();
        }
        static GameObject MakeEnvironmentPrefab()
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(ModelNames[0])));
            try
            {
                var proxy = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(ModelNames[2])));
                proxy.transform.SetParent(instance.transform, true);
                foreach (var filter in proxy.GetComponentsInChildren<MeshFilter>(true))
                {
                    // Proxy rotations and the door gap are baked into mesh vertices, not Transform rotations.
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    collider.convex = false;
                    Object.DestroyImmediate(filter.GetComponent<Renderer>());
                    Object.DestroyImmediate(filter);
                }
                proxy.name = "StaticCollisionProxies";
                // Monitor arm is already in the environment export; its separate FBX is a reusable module, not another instance.
                return PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            }
            finally { Object.DestroyImmediate(instance); }
        }
        static GameObject PlaceExisting(string prefab, string anchorName, Anchor[] anchors, string[] hide = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
            if (asset == null) throw new FileNotFoundException("Missing existing prefab.", prefab);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            var anchor = anchors.Single(a => a.name == anchorName);
            instance.transform.position = anchor.Position;
            instance.transform.rotation = Quaternion.Euler(0, anchor.unity_yaw_deg, 0) * instance.transform.rotation;
            if (hide != null)
                foreach (var child in instance.GetComponentsInChildren<Transform>(true).Where(t => hide.Contains(t.name))) child.gameObject.SetActive(false);
            return instance;
        }
        static float ValidateAnchors(GameObject instance, Anchor[] anchors)
        {
            var all = instance.GetComponentsInChildren<Transform>(true);
            float maximum = 0;
            foreach (var anchor in anchors)
            {
                var matches = all.Where(t => t.name == "ANCHOR_" + anchor.name).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("Expected one exported anchor: " + anchor.name);
                float error = Vector3.Distance(matches[0].position, anchor.Position);
                maximum = Mathf.Max(maximum, error);
                if (error > .001f) throw new InvalidOperationException("FBX anchor mismatch: " + anchor.name + " " + error + " m");
            }
            return maximum;
        }
        static void AddLight(string name, Vector3 position, Color color, float intensity, float range)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.type = LightType.Point;
            light.transform.position = position;
            light.color = color; light.intensity = intensity; light.range = range;
            light.shadows = LightShadows.None;
        }
        static void RenderShot(Camera camera)
        {
            var previous = RenderTexture.active;
            var target = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render(); camera.Render(); camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                File.WriteAllBytes(Reports + "/" + camera.name + ".png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
            }
        }
        static long Triangles(Mesh mesh)
        {
            long count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) if (mesh.GetTopology(i) == MeshTopology.Triangles) count += mesh.GetIndexCount(i) / 3;
            return count;
        }
    }
}
