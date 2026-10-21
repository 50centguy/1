using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.UI.Tests
{
    // Measures what the four-prop desktop scene actually costs after the V2 bench / table / gantry replacements:
    // per-module triangles, renderers, material slots, unique materials and texture memory of everything that renders
    // (forceRenderingOff excluded), lights and shadow casters, and what three cameras really draw (UnityStats after a
    // render). Writes a JSON report when BORDER_REPAIR_PERF_REPORT names a file. It asserts nothing about budgets.
    public sealed class ScenePerformanceBudgetTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        TwoNightState previousRun;
        string previousSave, isolatedSave;
        Scene loaded;

        [Serializable] public sealed class Module { public string name; public int renderers, triangles, materialSlots, uniqueMaterials, shadowCasters, staticBatched; public float textureMB; }
        [Serializable] public sealed class View { public string name; public int visibleRenderers, visibleTriangles, visibleMaterialSlots, visibleShadowCasters; }
        [Serializable] public sealed class Report
        {
            public int totalTriangles, totalRenderers, totalMaterialSlots, uniqueMaterials, uniqueTextures, realtimeLights, shadowedLights;
            public float textureMB;
            public List<Module> modules = new List<Module>();
            public List<View> views = new List<View>();
            public List<string> heaviestRenderers = new List<string>();
            public List<string> workbenchLeftovers = new List<string>();
            public List<string> lights = new List<string>();
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current; previousSave = TwoNightSave.OverrideDirectory;
            isolatedSave = Path.Combine(Path.GetTempPath(), "RepairPropPerf_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = isolatedSave;
            TwoNightRun.Clear();
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Requires the authored editor scene.");
            yield break;
#endif
            loaded = SceneManager.GetSceneByPath(ScenePath);
            float deadline = Time.realtimeSinceStartup + 20;
            UnifiedClinicDirector clinic = null;
            while ((clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>()) == null || !clinic.Initialized)
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("Clinic must initialize.");
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (loaded.IsValid() && loaded.isLoaded)
            {
                SceneManager.SetActiveScene(SceneManager.CreateScene("PerfCleanup_" + Guid.NewGuid().ToString("N")));
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
            TwoNightRun.Set(previousRun); TwoNightSave.OverrideDirectory = previousSave;
            if (Directory.Exists(isolatedSave)) Directory.Delete(isolatedSave, true);
        }

        static int Triangles(Renderer r)
        {
            Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null;
            if (mesh == null) return 0;
            // A statically batched renderer shares the scene's combined mesh and draws only its own submesh range.
            int first = 0, count = mesh.subMeshCount;
            if (r.isPartOfStaticBatch && r is MeshRenderer mr) { first = mr.subMeshStartIndex; count = Mathf.Min(r.sharedMaterials.Length, mesh.subMeshCount - first); }
            long n = 0;
            for (int i = first; i < first + count; i++) n += mesh.GetIndexCount(i);
            return (int)(n / 3);
        }

        static string ModuleOf(Renderer r)
        {
            var t = r.transform;
            while (t != null)
            {
                string n = t.name;
                if (n == "RepairBenchV2") return "维修台 V2";
                if (n == "SurgeryTableV2") return "手术台 V2";
                if (n == "SurgeryGantryV2") return "手术龙门 V2";
                if (n == "TradeCounterV2") return "柜台 V2";
                if (n == "DockCornerV2") return "维修座墙面 V2";
                if (n == "ClinicStorageV2") return "储物 V2";
                if (n.StartsWith("NPC_", StringComparison.Ordinal)) return "NPC";
                if (n.StartsWith("PJ_", StringComparison.Ordinal)) return "投影标牌";
                if (r.GetComponentInParent<RepairPropFlow>(true) != null) return "四件维修道具";
                if (n.StartsWith("WorkbenchArea", StringComparison.Ordinal)) return "原工作台（未隐藏部分）";
                if (n.Contains("Clutter")) return "房间杂物";
                if (n.StartsWith("UnifiedClinic", StringComparison.Ordinal)) return "房间外壳/家具";
                if (n.Contains("Robot") || n.Contains("UNIT07") || n.Contains("Unit07")) return "机器人/维修座";
                t = t.parent;
            }
            return "其他";
        }

        static string MeshName(Renderer r)
        {
            var mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null;
            return mesh != null ? mesh.name + "/v" + mesh.vertexCount + "/sub" + mesh.subMeshCount : "none";
        }

        static IEnumerable<string> AncestorNames(Transform t)
        {
            var names = new List<string>();
            for (var p = t.parent; p != null && names.Count < 3; p = p.parent) names.Add(p.name);
            return names;
        }

        [UnityTest]
        public IEnumerator MeasureSceneCost()
        {
            yield return null; yield return null;
            var report = new Report();
            var renderers = loaded.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>())
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff && !(r is ParticleSystemRenderer)).ToList();
            var textures = new HashSet<Texture>();
            foreach (var group in renderers.GroupBy(ModuleOf))
            {
                var mats = group.SelectMany(r => r.sharedMaterials).Where(m => m != null).ToList();
                var tex = new HashSet<Texture>(mats.SelectMany(m => m.GetTexturePropertyNameIDs().Select(m.GetTexture)).Where(x => x != null));
                textures.UnionWith(tex);
                report.modules.Add(new Module
                {
                    name = group.Key, renderers = group.Count(), triangles = group.Sum(Triangles), materialSlots = mats.Count,
                    uniqueMaterials = mats.Distinct().Count(), shadowCasters = group.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off),
                    staticBatched = group.Count(r => r.isPartOfStaticBatch),
                    textureMB = tex.Sum(x => Profiler.GetRuntimeMemorySizeLong(x)) / 1048576f,
                });
            }
            report.modules = report.modules.OrderByDescending(m => m.triangles).ToList();
            report.totalTriangles = report.modules.Sum(m => m.triangles);
            report.totalRenderers = renderers.Count;
            report.totalMaterialSlots = report.modules.Sum(m => m.materialSlots);
            report.uniqueMaterials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count();
            report.uniqueTextures = textures.Count;
            report.textureMB = textures.Sum(x => Profiler.GetRuntimeMemorySizeLong(x)) / 1048576f;
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.isActiveAndEnabled && l.lightmapBakeType != LightmapBakeType.Baked).ToList();
            report.realtimeLights = lights.Count;
            report.shadowedLights = lights.Count(l => l.shadows != LightShadows.None);
            report.workbenchLeftovers = renderers.Where(r => ModuleOf(r).StartsWith("原工作台", StringComparison.Ordinal)).OrderByDescending(Triangles)
                .Select(r => r.name + " (" + string.Join("/", AncestorNames(r.transform)) + ") : " + Triangles(r) + " mesh=" + MeshName(r)
                    + " mats=" + string.Join("+", r.sharedMaterials.Where(m => m != null).Select(m => m.name))).ToList();
            report.lights = lights.Select(l => l.name + " " + l.type + " shadows=" + l.shadows + " range=" + l.range.ToString("0.0")).ToList();
            report.heaviestRenderers = renderers.OrderByDescending(Triangles).Take(15).Select(r => ModuleOf(r) + " / " + r.name + " : " + Triangles(r) + " tris, " + r.sharedMaterials.Length + " mats").ToList();

            // Frustum-culled estimate per view (batch mode leaves UnityStats empty): what a camera at that pose submits.
            var clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>();
            var room = clinic.Robot.Flow.Rig.Cam;
            var pose = (room.transform.position, room.transform.rotation);
            var tableRoot = SurgeryTableV2Runtime.Current != null ? SurgeryTableV2Runtime.Current.Table.transform.position : Vector3.zero;
            var bench = RepairBenchV2Runtime.Current != null ? RepairBenchV2Runtime.Current.Bench.transform.position : Vector3.zero;
            var poses = new List<(string, Vector3?, Vector3)>
            {
                ("房间入口（玩家出生点）", null, Vector3.zero),
                ("手术台旁站立（1.6 m）", tableRoot + new Vector3(.9f, 1.6f, -.6f), tableRoot + Vector3.up * 1.1f),
                ("维修台前站立（1.6 m）", bench + (tableRoot - bench).normalized * .7f + Vector3.up * 1.6f, bench + Vector3.up * .95f),
            };
            foreach (var (name, at, look) in poses)
            {
                if (at.HasValue) { room.transform.position = at.Value; room.transform.LookAt(look); }
                var planes = GeometryUtility.CalculateFrustumPlanes(room);
                var seen = renderers.Where(r => GeometryUtility.TestPlanesAABB(planes, r.bounds)).ToList();
                report.views.Add(new View { name = name, visibleRenderers = seen.Count, visibleTriangles = seen.Sum(Triangles),
                    visibleMaterialSlots = seen.Sum(r => r.sharedMaterials.Length),
                    visibleShadowCasters = seen.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) });
                room.transform.SetPositionAndRotation(pose.position, pose.rotation);
            }
            string json = JsonUtility.ToJson(report, true);
            TestContext.WriteLine(json);
            string path = Environment.GetEnvironmentVariable("BORDER_REPAIR_PERF_REPORT");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, json);
            Assert.That(report.totalTriangles, Is.GreaterThan(0));
        }
    }
}
