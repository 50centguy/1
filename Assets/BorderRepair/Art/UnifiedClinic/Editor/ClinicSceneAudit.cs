using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace BorderRepair.Art.UnifiedClinic.EditorTools
{
    public static class ClinicSceneAudit
    {
        [Serializable] public class ObjectEntry
        {
            public string path;
            public float[] position, rotation, size;
            public long triangles;
            public int materialSlots;
            public bool active;
        }

        [Serializable] public class Report
        {
            public string scene, unityVersion, measurement;
            public string[] dependencies;
            public ObjectEntry[] renderers;
            public long instanceTriangles, activeTriangles, uniqueMeshTriangles, textureRuntimeBytes;
            public int rendererCount, activeRenderers, uniqueMeshes, materialCount, textureCount, missingScripts;
            public string[] limitations;
        }

        [MenuItem("Border Repair/Unified Clinic/Audit Saved Scene")]
        public static void Run()
        {
            string path = Argument("-clinicScene", "Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic.unity");
            if (!File.Exists(path)) throw new FileNotFoundException("Build the shared clinic before auditing.", path);
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save open edits before auditing a saved scene.");
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var renderers = roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
            var meshes = new HashSet<Mesh>();
            var materials = new HashSet<Material>();
            var textures = new HashSet<Texture>();
            var entries = new List<ObjectEntry>();
            long activeTriangles = 0, allTriangles = 0;
            foreach (var renderer in renderers)
            {
                var skin = renderer as SkinnedMeshRenderer;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                long count = TriangleCount(mesh);
                if (mesh != null) meshes.Add(mesh);
                bool active = renderer.enabled && renderer.gameObject.activeInHierarchy;
                allTriangles += count;
                if (active) activeTriangles += count;
                foreach (var material in renderer.sharedMaterials.Where(m => m != null))
                {
                    materials.Add(material);
                    foreach (string property in material.GetTexturePropertyNames())
                    {
                        var texture = material.GetTexture(property);
                        if (texture != null) textures.Add(texture);
                    }
                }
                entries.Add(new ObjectEntry {
                    path = HierarchyPath(renderer.transform), position = V(renderer.transform.position),
                    rotation = V(renderer.transform.eulerAngles), size = V(renderer.bounds.size),
                    triangles = count, materialSlots = renderer.sharedMaterials.Length, active = active
                });
            }
            var report = new Report {
                scene = path, unityVersion = Application.unityVersion,
                measurement = "Saved scene static inventory, not GPU render-pass triangle counts or measured frame timing.",
                dependencies = AssetDatabase.GetDependencies(path, true).OrderBy(p => p).ToArray(),
                renderers = entries.OrderBy(e => e.path).ToArray(),
                instanceTriangles = allTriangles, activeTriangles = activeTriangles,
                uniqueMeshTriangles = meshes.Sum(TriangleCount), rendererCount = renderers.Length,
                activeRenderers = entries.Count(e => e.active), uniqueMeshes = meshes.Count,
                materialCount = materials.Count, textureCount = textures.Count,
                textureRuntimeBytes = textures.Sum(t => Profiler.GetRuntimeMemorySizeLong(t)),
                missingScripts = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)),
                limitations = new[] {
                    "Instance triangles include inactive assets; active counts are not camera-visibility counts.",
                    "No Read/Write or material toggles were changed to collect mesh counts.",
                    "Textures use Unity runtime-size estimates; platform upload, streaming and stereo costs are unmeasured.",
                    "Standalone CPU/GPU timing, draw calls, human mouse play and VR headset acceptance remain separate checks."
                }
            };
            string output = Argument("-clinicAuditOutput", "Docs/Integration/UnifiedClinic/static_scene_audit.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log($"[UnifiedClinic Audit] {entries.Count} renderers, {activeTriangles} active static triangles; {report.missingScripts} missing scripts. {output}");
            if (report.missingScripts != 0) throw new InvalidOperationException("Shared scene contains missing scripts.");
        }

        static long TriangleCount(Mesh mesh)
        {
            if (mesh == null) return 0;
            long count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                if (mesh.GetTopology(i) == MeshTopology.Triangles) count += mesh.GetIndexCount(i) / 3;
            return count;
        }

        static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };
        static string HierarchyPath(Transform t) => t.parent == null ? t.name : HierarchyPath(t.parent) + "/" + t.name;
        static string Argument(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
    }
}
