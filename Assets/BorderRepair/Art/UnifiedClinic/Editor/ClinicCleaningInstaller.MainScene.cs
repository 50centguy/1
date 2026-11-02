using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Cleaning;
using BorderRepair.TwoNight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.Art.UnifiedClinic.EditorTools
{
    // The main scene's half of the cleaning install. These items differ from the customer items in one way that decides
    // everything here: appearance editing owns their shells (it needs the authored mesh and URP/Lit in slot 0), so the
    // dirt goes on as CleanableSurface's overlay form and the renderers stay as authored.
    //
    // Idempotent: a prefab whose surfaces all carry the current bake version is not opened for writing, and neither is a
    // scene that already has its controller. Dirt is per instance at runtime; what this writes is each surface's starting state.
    public static partial class ClinicCleaningInstaller
    {
        public const string MainScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        public const string OverlayTemplatePath = Root + "/DirtOverlay_Template.mat";
        const string MainSceneControllerName = "ClinicCleaning";
        const string MainSceneReport = "Docs/Integration/UnifiedClinic/Cleaning/main_scene_installed.txt";
        const int MainSceneBakeVersion = 1;   // raise when the dirt recipe changes: installed prefabs rebake on the next run
        const double MinimumDirtyShare = .15;

        sealed class DirtProfile
        {
            public string path;
            public Func<string, bool> isShell;
            public Color color;
            public float opacity, smoothness;
            public float frequency;   // patches per longest mesh dimension
            public float cover;       // share of the exposed surface that starts dirty
            public float settle;      // how much more readily upward faces collect it
            public float seed;
            public bool optional;     // not finding a shell is reported, not fatal
            public static Func<string, bool> Named(params string[] names) => names.Contains;
        }

        // Shell lists match CosmeticItem.Eligible: the parts the game already treats as this item's outside.
        static readonly DirtProfile[] MainSceneProfiles =
        {
            // Pocket radio: handled all shift, so greasy thumb smears rather than settled dust.
            new DirtProfile { path = "Assets/RepairProps/Prefabs/Receiver.prefab", isShell = DirtProfile.Named("RX_FrontShell", "RX_RearShell", "RX_RearCover", "RX_TopStrip"),
                color = new Color(.17f, .135f, .09f), opacity = .9f, smoothness = .28f, frequency = 3.2f, cover = .46f, settle = .05f, seed = 11.3f },
            // Cassette from a drawer: fine pale dust gathered on the flat top.
            new DirtProfile { path = "Assets/RepairProps/Prefabs/Cartridge.prefab", isShell = DirtProfile.Named("MC_BaseTray", "MC_TopCover"),
                color = new Color(.46f, .43f, .37f), opacity = .85f, smoothness = .04f, frequency = 5.5f, cover = .5f, settle = .22f, seed = 37.9f },
            // Service power unit: sooty, large blotches from living on a shop floor.
            new DirtProfile { path = "Assets/RepairProps/Prefabs/Power.prefab", isShell = DirtProfile.Named("PS_ext_Shell_p21", "PS_LeftServiceCover"),
                color = new Color(.075f, .07f, .065f), opacity = .92f, smoothness = .1f, frequency = 2.4f, cover = .52f, settle = .12f, seed = 71.7f },
            // Worn charm: small pale specks of lint and skin oil, the lightest of the four.
            new DirtProfile { path = "Assets/RepairProps/Prefabs/Omamori.prefab", isShell = DirtProfile.Named("OM_ext_Shell", "OM_ext_Face", "OM_RearCover"),
                color = new Color(.39f, .335f, .265f), opacity = .75f, smoothness = .05f, frequency = 7.5f, cover = .38f, settle = .08f, seed = 113.1f },
            // UNIT 07: oil and dust over the armour, heaviest where it collects on top.
            new DirtProfile { path = "Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_DockReady.prefab", isShell = IsRobotArmour, optional = true,
                color = new Color(.14f, .115f, .085f), opacity = .85f, smoothness = .15f, frequency = 4f, cover = .42f, settle = .18f, seed = 157.3f },
        };

        static bool IsRobotArmour(string rendererName)
        {
            string name = rendererName.ToLowerInvariant();
            if (new[] { "screen", "glass", "lamp", "led", "label", "seal", "bearing", "rotor", "cable", "wire", "bolt", "screw" }.Any(name.Contains)) return false;
            return new[] { "body", "shell", "cover", "housing", "armor", "armour", "chassis", "torso", "panel" }.Any(name.Contains);
        }

        /// <summary>Batch entry: -executeMethod BorderRepair.Art.UnifiedClinic.EditorTools.ClinicCleaningInstaller.RunBatch</summary>
        public static void RunBatch()
        {
            try { InstallMainScene(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("Border Repair/Clinic Base/Cleaning/Install Main Scene Cleaning", priority = 2001)]
        public static void InstallMainScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before installing cleaning.");
            Directory.CreateDirectory(Root + "/Meshes"); AssetDatabase.Refresh();
            var template = OverlayTemplate();
            var report = new List<string>();
            bool changed = false;
            foreach (var profile in MainSceneProfiles) changed |= InstallPrefab(profile, template, report);
            changed |= InstallSceneController(report);
            Debug.Log("[ClinicCleaning] " + (changed ? "Installed.\n" : "Nothing to do.\n") + string.Join("\n", report));
            if (!changed) return;   // a run that changes nothing leaves no trace, not even a new timestamp
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(MainSceneReport));
            File.WriteAllText(MainSceneReport, DateTime.UtcNow.ToString("O") + "\n" + string.Join("\n", report) + "\n");
        }

        static Material OverlayTemplate()
        {
            var shader = Shader.Find("BorderRepair/DirtOverlay");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("DirtOverlay shader is missing or does not compile.");
            var template = AssetDatabase.LoadAssetAtPath<Material>(OverlayTemplatePath);
            if (template != null) return template;
            template = new Material(shader) { name = "DirtOverlay_Template" };
            AssetDatabase.CreateAsset(template, OverlayTemplatePath);
            return template;
        }

        static bool InstallPrefab(DirtProfile profile, Material template, List<string> report)
        {
            var prefab = PrefabUtility.LoadPrefabContents(profile.path);
            try
            {
                var existing = prefab.GetComponentsInChildren<CleanableSurface>(true);
                if (existing.Length > 0 && existing.All(s => s.OverlayInstalled(MainSceneBakeVersion)))
                {
                    report.Add(profile.path + " | already installed, " + existing.Length + " surfaces");
                    return false;
                }
                // Checked before anything is written, so the message can list what the prefab really has.
                var meshRenderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
                if (!meshRenderers.Any(r => profile.isShell(r.name) && r.GetComponent<MeshFilter>() != null && r.sharedMaterials.Any(IsPaintable)))
                {
                    string found = profile.path + " | NOT INSTALLED, no shell among: " + string.Join(", ", meshRenderers.Select(r => r.name));
                    if (!profile.optional) throw new InvalidOperationException(found);
                    report.Add(found);
                    Debug.LogWarning("[ClinicCleaning] " + found);
                    return false;
                }
                var bakes = new List<SurfaceBake>();
                foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    var slots = renderer.sharedMaterials.Select((m, i) => (m, i)).Where(p => IsPaintable(p.m)).Select(p => p.i).ToArray();
                    if (slots.Length > 0 && profile.isShell(renderer.name))
                    {
                        var readable = ReadableCopy(filter.sharedMesh);
                        var coverage = Coverage(readable, slots, Resolution, out var triangles);
                        if (!coverage.Any(p => p > 0)) { report.Add(profile.path + " | no UV coverage, skipped: " + renderer.name); continue; }
                        var surface = renderer.GetComponent<CleanableSurface>();
                        if (surface == null) surface = renderer.gameObject.AddComponent<CleanableSurface>();
                        surface.Configure(renderer, readable, template, Resolution, coverage, slots, triangles);
                        bakes.Add(new SurfaceBake { surface = surface, mesh = readable, coverage = coverage, slots = slots, triangles = triangles });
                    }
                    else if (renderer.sharedMaterials.Any(m => m != null && m.renderQueue <= 2500))
                    {
                        // Everything else opaque blocks the wipe: screens, labels, seals, knobs, and the insides once a cover is off.
                        var occluder = renderer.GetComponent<CleaningOccluder>();
                        if (occluder == null) occluder = renderer.gameObject.AddComponent<CleaningOccluder>();
                        occluder.Configure(renderer, ReadableCopy(filter.sharedMesh, false));
                    }
                }
                if (bakes.Count == 0) throw new InvalidOperationException("No shell surface had UV coverage in " + profile.path);
                BakeExposedCoverage(prefab, bakes);
                int kept = 0;
                foreach (var bake in bakes)
                {
                    if (!bake.coverage.Any(p => p > 0))
                    {
                        report.Add(profile.path + " | hidden part skipped: " + bake.surface.name);
                        UnityEngine.Object.DestroyImmediate(bake.surface);
                        continue;
                    }
                    var dirt = BakeDirt(bake, profile, out double dirtyShare);
                    if (dirtyShare < MinimumDirtyShare) throw new InvalidOperationException(profile.path + " | " + bake.surface.name + " came out almost clean: " + dirtyShare.ToString("P0"));
                    var renderer = bake.surface.Target;
                    bake.surface.ConfigureOverlay(renderer, bake.mesh, template, Resolution, bake.coverage, bake.slots, bake.triangles, dirt, MainSceneBakeVersion);
                    bake.surface.SetDirtAppearance(profile.color, profile.opacity, profile.smoothness);
                    report.Add(profile.path + " | " + renderer.name + " | slots " + string.Join(",", bake.slots) + " | exposed texels " + bake.coverage.Count(p => p > 0) + " | dirty " + dirtyShare.ToString("P0"));
                    kept++;
                }
                if (kept == 0) throw new InvalidOperationException("Every shell of " + profile.path + " is hidden.");
                PrefabUtility.SaveAsPrefabAsset(prefab, profile.path);
                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }

        // Dirt is decided from where a texel sits on the object, not where it sits in the atlas: patches run straight
        // across UV seams and take their scale from this part's size, so each shell gets its own, not a stretched copy.
        static byte[] BakeDirt(SurfaceBake bake, DirtProfile profile, out double dirtyShare)
        {
            var values = new float[bake.coverage.Length];
            var filled = new bool[bake.coverage.Length];
            var positions = bake.mesh.vertices; var uv = bake.mesh.uv2;
            var transform = bake.surface.Target.transform;
            var normalMatrix = transform.localToWorldMatrix.inverse.transpose;
            var size = bake.mesh.bounds.size;
            float unit = Mathf.Max(size.x, size.y, size.z, 1e-5f);
            // Every part gets its own stretch of noise, whatever its mesh bounds happen to be.
            float offset = profile.seed + Mathf.Abs(bake.surface.name.GetHashCodeStable()) % 997 * .37f;
            float Cross(Vector2 v, Vector2 w) => v.x * w.y - v.y * w.x;
            foreach (int slot in bake.slots)
            {
                var indices = bake.mesh.GetTriangles(slot);
                for (int t = 0; t < indices.Length; t += 3)
                {
                    int ia = indices[t], ib = indices[t + 1], ic = indices[t + 2];
                    Vector2 a = uv[ia], b = uv[ib], c = uv[ic];
                    float area = Cross(b - a, c - a); if (Mathf.Abs(area) < 1e-8f) continue;
                    Vector3 rawNormal = normalMatrix.MultiplyVector(Vector3.Cross(positions[ib] - positions[ia], positions[ic] - positions[ia]));
                    float lengthSquared = rawNormal.sqrMagnitude;
                    float up = float.IsFinite(lengthSquared) && lengthSquared > 1e-24f ? rawNormal.y / Mathf.Sqrt(lengthSquared) : 0;
                    int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x) * Resolution), 0, Resolution - 1);
                    int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x, c.x) * Resolution), 0, Resolution - 1);
                    int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y) * Resolution), 0, Resolution - 1);
                    int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y, c.y) * Resolution), 0, Resolution - 1);
                    for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
                    {
                        int pixel = y * Resolution + x;
                        if (bake.coverage[pixel] == 0 || filled[pixel]) continue;
                        var p = new Vector2((x + .5f) / Resolution, (y + .5f) / Resolution);
                        float u = Cross(b - p, c - p) / area, v = Cross(c - p, a - p) / area;
                        if (u < 0 || v < 0 || u + v > 1.00001f) continue;
                        Vector3 local = (positions[ia] * u + positions[ib] * v + positions[ic] * (1 - u - v) - bake.mesh.bounds.center) / unit;
                        values[pixel] = Noise(local * profile.frequency, offset) + Noise(local * profile.frequency * 2.9f, offset + 41) * .35f + Mathf.Max(0, up) * profile.settle;
                        filled[pixel] = true;
                    }
                }
            }
            var ordered = values.Where((_, i) => filled[i]).OrderBy(value => value).ToArray();
            var dirt = new byte[values.Length];
            if (ordered.Length == 0) { dirtyShare = 0; return dirt; }
            // A percentile rather than a fixed cut: each part ends up with its profile's share of dirt whatever its noise did.
            float threshold = ordered[Mathf.Clamp(Mathf.RoundToInt(ordered.Length * (1 - profile.cover)), 0, ordered.Length - 1)];
            float top = Mathf.Max(ordered[ordered.Length - 1], threshold + .01f);
            int dirty = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (!filled[i] || values[i] <= threshold) continue;
                // A soft edge into a thick middle, so a patch reads as a patch and the thick parts scrape.
                float body = Mathf.SmoothStep(0, 1, Mathf.Clamp01((values[i] - threshold) / ((top - threshold) * .3f)));
                dirt[i] = (byte)Mathf.RoundToInt(255 * Mathf.Lerp(.25f, 1, body));
                dirty++;
            }
            dirtyShare = dirty / (double)ordered.Length;
            return dirt;
        }

        static float Noise(Vector3 p, float offset) =>
            (Mathf.PerlinNoise(p.x + offset, p.y - offset) + Mathf.PerlinNoise(p.y - offset * .7f, p.z + offset * .3f) + Mathf.PerlinNoise(p.z + offset * .5f, p.x + offset * .9f)) / 3f;

        // string.GetHashCode differs between runs; a rebake must give the same dirt.
        static int GetHashCodeStable(this string text)
        {
            int hash = 17;
            foreach (char c in text) hash = unchecked(hash * 31 + c);
            return hash == int.MinValue ? 0 : hash;
        }

        static bool InstallSceneController(List<string> report)
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            if (scene.GetRootGameObjects().Any(root => root.GetComponentInChildren<ClinicCleaningController>(true) != null))
            {
                report.Add(MainScenePath + " | controller already present");
                return false;
            }
            new GameObject(MainSceneControllerName).AddComponent<ClinicCleaningController>();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + MainScenePath);
            report.Add(MainScenePath + " | controller added");
            return true;
        }
    }
}
