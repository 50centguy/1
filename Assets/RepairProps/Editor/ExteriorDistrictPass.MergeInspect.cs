using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // Read-only survey for merging the clinic gameplay scene into the district: room bounds, doors, colliders,
    // lights and render settings of both scenes. Report: <EXTERIOR_OUT>/merge_inspect.txt.
    public static partial class ExteriorDistrictPass
    {
        const string ClinicScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";

        public static void InspectMerge()
        {
            var sb = new StringBuilder();
            foreach (var path in new[] { ClinicScenePath, ScenePath })
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                sb.Append("==== ").Append(path).Append('\n');
                sb.Append(string.Format("render settings: fog {0} mode {1} colour {2} density {3} start {4} end {5}; ambient mode {6} sky {7} equator {8} ground {9} intensity {10}; skybox {11}; sun {12}; reflection {13} intensity {14}\n",
                    RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogColor, RenderSettings.fogDensity, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance,
                    RenderSettings.ambientMode, RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor, RenderSettings.ambientIntensity,
                    RenderSettings.skybox ? RenderSettings.skybox.name : "none", RenderSettings.sun ? RenderSettings.sun.name : "none", RenderSettings.defaultReflectionMode, RenderSettings.reflectionIntensity));
                foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                {
                    var rs = root.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                    var b = rs.Length > 0 ? rs.Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; }) : new Bounds();
                    sb.Append(string.Format("root {0}: active {1}, renderers {2}, bounds min {3} max {4}, colliders {5}\n", root.name, root.activeSelf, rs.Length, V(b.min), V(b.max),
                        root.GetComponentsInChildren<Collider>(true).Count(c => c.enabled && c.gameObject.activeInHierarchy)));
                }
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.type == LightType.Directional))
                    sb.Append(string.Format("directional {0}: enabled {1} intensity {2} colour {3} shadows {4} rot {5} cullingMask {6} renderingLayers {7}\n", HierPath(l.transform), l.enabled, l.intensity, l.color, l.shadows, l.transform.eulerAngles, l.cullingMask, l.renderingLayerMask));
                var keys = path == ClinicScenePath
                    ? new[] { "Wall", "Door", "door", "Floor", "Ceiling", "Frame", "Window", "Shutter", "Bench_Top", "Blocker", "Collision", "Player", "Eye", "Counter", "Probe" }
                    : new[] { "Door", "door", "Shutter", "shutter", "Stair", "stair", "Step", "step", "Landing", "landing", "Front wall", "front wall", "Wall", "Floor", "floor", "Ceiling", "Sidewalk", "sidewalk", "Road", "road" };
                foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(t => HierPath(t)))
                {
                    if (!keys.Any(k => t.name.Contains(k))) continue;
                    var r = t.GetComponent<Renderer>(); var c = t.GetComponent<Collider>();
                    if (r == null && c == null && t.childCount == 0) continue;
                    var rb = r != null ? r.bounds : (Bounds?)null;
                    var cb = c != null ? c.bounds : (Bounds?)null;
                    sb.Append(string.Format("  {0} [{1}] pos {2}{3}{4}\n", HierPath(t), t.gameObject.activeInHierarchy ? "on" : "off", V(t.position),
                        rb.HasValue ? " R " + V(rb.Value.min) + ".." + V(rb.Value.max) + (r.enabled ? "" : " (disabled)") : "",
                        cb.HasValue ? " C " + c.GetType().Name + " " + V(cb.Value.min) + ".." + V(cb.Value.max) + (c.enabled ? "" : " (disabled)") + (c.isTrigger ? " trigger" : "") : ""));
                }
            }
            var dir = OutDir;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "merge_inspect.txt"), sb.ToString());
            Debug.Log("[Exterior] merge survey written");
        }

        static string V(Vector3 v) => string.Format("({0:F2} {1:F2} {2:F2})", v.x, v.y, v.z);

        static string HierPath(Transform t) => t.parent == null ? t.name : HierPath(t.parent) + "/" + t.name;
    }
}
