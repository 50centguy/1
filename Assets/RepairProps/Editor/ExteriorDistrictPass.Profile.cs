using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // Cost of a scene as the editor can measure it: per camera, the median wall time of a 1920x1080 render waited out
    // on the GPU (this machine, editor build, so for comparison between scenes and passes, not an absolute budget), the
    // render counters, what lies in the view; for the scene, texture and mesh memory and the pipeline's shadow setup.
    // PROFILE_SCENE picks the scene (default the district), PROFILE_CAMERAS a ';' list (default every camera).
    // Report: <EXTERIOR_OUT>/profile.txt.
    public static partial class ExteriorDistrictPass
    {
        public static void Profile()
        {
            var path = System.Environment.GetEnvironmentVariable("PROFILE_SCENE") ?? ScenePath;
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            ShaderUtil.allowAsyncCompilation = false;
            var sb = new StringBuilder();
            sb.Append("scene ").Append(path).Append('\n');
            Pipeline(sb);
            Inventory(sb);

            var only = (System.Environment.GetEnvironmentVariable("PROFILE_CAMERAS") ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => only.Count == 0 || only.Contains(c.name)).OrderBy(c => c.name).ToList();
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (ps.transform.parent == null || ps.transform.parent.GetComponent<ParticleSystem>() == null) ps.Simulate(8f, true, true);
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var recorders = new[] { "Batches Count", "SetPass Calls Count", "Draw Calls Count", "Triangles Count", "Shadow Casters Count" }
                .Select(n => (n, r: ProfilerRecorder.StartNew(ProfilerCategory.Render, n))).ToArray();
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            sb.Append("\nper camera (ms = median of 15 renders at 1920x1080, GPU waited; in view = enabled renderers whose bounds meet the frustum within the far plane):\n");
            var times = new List<float>();
            foreach (var cam in cams)
            {
                var was = cam.targetTexture;
                cam.targetTexture = rt;
                cam.aspect = 16f / 9;
                for (int i = 0; i < 3; i++) Sync(cam, rt);
                var ms = new List<double>();
                for (int i = 0; i < 15; i++)
                {
                    var w = Stopwatch.StartNew();
                    Sync(cam, rt);
                    ms.Add(w.Elapsed.TotalMilliseconds);
                }
                ms.Sort();
                var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                var inView = renderers.Where(r => GeometryUtility.TestPlanesAABB(planes, r.bounds)).ToList();
                long tris = 0;
                foreach (var r in inView)
                    if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
                        for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += (long)mf.sharedMesh.GetIndexCount(s) / 3;
                int casters = inView.Count(r => r.shadowCastingMode != ShadowCastingMode.Off);
                int mats = inView.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count();
                int lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.enabled && l.type != LightType.Directional
                    && GeometryUtility.TestPlanesAABB(planes, new Bounds(l.transform.position, Vector3.one * l.range * 2)));
                int shadowed = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.enabled && l.shadows != LightShadows.None && l.type != LightType.Directional
                    && GeometryUtility.TestPlanesAABB(planes, new Bounds(l.transform.position, Vector3.one * l.range * 2)));
                float med = (float)ms[ms.Count / 2];
                times.Add(med);
                sb.Append(string.Format("  {0,-46} {1,6:F1} ms  in view {2,5} renderers, {3,7:N0} tris, {4,4} mats, {5,4} casters, lights {6,2} ({7} shadowed)",
                    cam.name.Length > 46 ? cam.name.Substring(0, 46) : cam.name, med, inView.Count, tris, mats, casters, lights, shadowed));
                foreach (var (n, r) in recorders) if (r.Valid && r.LastValue > 0) sb.Append("  ").Append(n.Replace(" Count", "")).Append(' ').Append(r.LastValue);
                sb.Append('\n');
                cam.targetTexture = was;
                cam.ResetAspect();
            }
            if (times.Count > 0) sb.Append(string.Format("  median over cameras {0:F1} ms, worst {1:F1} ms\n", times.OrderBy(t => t).ElementAt(times.Count / 2), times.Max()));
            foreach (var (n, r) in recorders) r.Dispose();
            rt.Release();
            var dir = OutDir;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "profile.txt"), sb.ToString());
            Debug.Log("[Exterior] profile written to " + Path.Combine(dir, "profile.txt"));
        }

        static void Sync(Camera cam, RenderTexture rt)
        {
            cam.Render();
            AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
        }

        static void Pipeline(StringBuilder sb)
        {
            var asset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) { sb.Append("pipeline: not URP\n"); return; }
            sb.Append(string.Format("pipeline {0} (quality {1}): shadow distance {2} m, cascades {3}, main shadow map {4}, additional shadow atlas {5}, MSAA {6}, render scale {7}, HDR {8}, soft shadows {9}\n",
                asset.name, QualitySettings.names[QualitySettings.GetQualityLevel()], asset.shadowDistance, asset.shadowCascadeCount, asset.mainLightShadowmapResolution,
                asset.additionalLightsShadowmapResolution, asset.msaaSampleCount, asset.renderScale, asset.supportsHDR, asset.supportsSoftShadows));
        }

        static void Inventory(StringBuilder sb)
        {
            var rs = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            var meshes = new HashSet<Mesh>();
            long tris = 0;
            foreach (var r in rs)
                if (r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
                {
                    meshes.Add(mf.sharedMesh);
                    for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += (long)mf.sharedMesh.GetIndexCount(s) / 3;
                }
            var mats = rs.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();
            var texs = new HashSet<Texture>();
            foreach (var m in mats)
                foreach (var id in m.GetTexturePropertyNameIDs())
                {
                    var t = m.GetTexture(id);
                    if (t != null) texs.Add(t);
                }
            long texMem = texs.Sum(t => Profiler.GetRuntimeMemorySizeLong(t)), meshMem = meshes.Sum(m => Profiler.GetRuntimeMemorySizeLong(m));
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.enabled).ToList();
            sb.Append(string.Format("scene: {0} enabled renderers ({1} cast shadows, {2} static-batched), {3:N0} triangles, {4} meshes ({5:F1} MB), {6} materials, {7} textures ({8:F1} MB)\n",
                rs.Count, rs.Count(r => r.shadowCastingMode != ShadowCastingMode.Off), rs.Count(r => GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic)),
                tris, meshes.Count, meshMem / 1048576.0, mats.Count, texs.Count, texMem / 1048576.0));
            sb.Append(string.Format("lights: {0} enabled ({1} directional, {2} spot, {3} point), shadowed {4}; particle systems {5}; reflection probes {6}\n",
                lights.Count, lights.Count(l => l.type == LightType.Directional), lights.Count(l => l.type == LightType.Spot), lights.Count(l => l.type == LightType.Point),
                lights.Count(l => l.shadows != LightShadows.None), Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length, Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Length));
            foreach (var t in texs.OrderByDescending(t => Profiler.GetRuntimeMemorySizeLong(t)).Take(8))
                sb.Append(string.Format("  big texture {0,-48} {1}x{2} {3:F1} MB\n", t.name, t.width, t.height, Profiler.GetRuntimeMemorySizeLong(t) / 1048576.0));
        }
    }
}
