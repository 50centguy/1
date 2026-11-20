using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // The playable Berth 04 scene: the clinic as it is played, inside the district (Berth04Playable at runtime).
    //   - Berth04_Playable.unity: the entry, one object that loads RepairProps_Clinic and then the district copy.
    //   - Berth04_Playable_District.unity: a copy of Temp_Berth04District moved under one root so its B1 gate meets the
    //     clinic door. The clinic is the one that cannot move (static batching, positions its runtimes use), so the
    //     district goes to it. Inside the clinic room's walls the copy keeps nothing (the B1 interior, the project's
    //     clinic assets placed there, their lights, colliders and the room probe); in the doorway it drops the folded
    //     gates and jambs that would stand in the clinic's door frame. Its cameras go (the clinic has the player's).
    //     It gains: the district's render settings on a DistrictAtmosphere, a ramp collider over the stairs (the
    //     player's step offset is 15 cm, a step is 17), the snow-exclusion box over the clinic room, a dim backing behind
    //     the street window, a sky-only reflection probe for the street (an additive scene brings no skybox reflection),
    //     and fewer shadow casters (small things and anything already unlit).
    // Neither original scene is touched; rerun after the district passes to refresh the copy.
    // Report: <EXTERIOR_OUT>/playable_report.txt and playable_walk_map.png (where a player can walk from the stairs).
    public static partial class ExteriorDistrictPass
    {
        public const string PlayableEntry = "Assets/RepairProps/Scenes/Berth04_Playable.unity";
        public const string PlayableDistrict = "Assets/RepairProps/Scenes/Berth04_Playable_District.unity";
        const string PlayableDir = "Assets/RepairProps/Scenes/Berth04_Playable";
        // The B1 gate (x -1.525) to the clinic door (x 0); the room side of the front wall (z .29) to the clinic's south
        // wall (z -3.4); 4 mm up so the stair landing covers the clinic floor's apron outside the door.
        static readonly Vector3 PlayableOffset = new Vector3(1.525f, .004f, -3.69f);

        [MenuItem("Border Repair/Berth 04/Build playable scene (clinic in the district)")]
        public static void BuildPlayable()
        {
            log = new StringBuilder();
            mats.Clear(); meshCount = 0;
            Directory.CreateDirectory(PlayableDir);
            Directory.CreateDirectory(PlayableDir + "/Materials");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            texDir = TexDir; matDir = PlayableDir + "/Materials"; meshDir = PlayableDir;
            try
            {
                var atmosphere = new GameObject("District atmosphere - render settings for Berth04Join").AddComponent<DistrictAtmosphere>();
                CaptureAtmosphere(atmosphere);

                var holder = new GameObject("District - moved to meet the clinic door").transform;
                foreach (var r in scene.GetRootGameObjects())
                    if (r.transform != holder && r != atmosphere.gameObject) r.transform.SetParent(holder, true);
                holder.position = PlayableOffset;
                Physics.SyncTransforms();

                atmosphere.reviewViews = holder.GetComponentsInChildren<Camera>(true).OrderBy(c => c.name)
                    .Select(c => new DistrictAtmosphere.View { name = c.name, position = c.transform.position, rotation = c.transform.rotation, fov = c.fieldOfView }).ToArray();
                RemoveCameras(holder);
                OpenB1(holder);
                WidenSurgeryBay(holder);
                var motion = holder.GetComponentInChildren<DistrictMotion>(true);
                if (motion != null) { motion.origin = PlayableOffset; motion.Evaluate(0); }
                atmosphere.offset = PlayableOffset;
                atmosphere.respawn = new Vector3(-1.48f, 1.36f, -4.95f) + PlayableOffset + Vector3.up * .05f;
                atmosphere.respawnYaw = 0;

                var joins = new GameObject("Joins - ramp, snow exclusion, window backing, sky probe").transform;
                StairRamp(joins);
                SnowExclusion(holder);
                // (no window backing: the street window looks into the B1 itself now)
                TrimShadowCasters(holder);
                Physics.SyncTransforms();
                StreetColliders(holder);
                B1Colliders(holder);
                Physics.SyncTransforms();
                WalkMapAndWalls(atmosphere.respawn, holder, joins);
                ColourScript(holder);
                SkyProbe(joins);
                B1Fiddles(holder);
                FarFill(holder);
                B1Cyber(holder);
                NightDetails(holder);
                FarDepth(holder);
                GlowFix(holder);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, PlayableDistrict);
            }
            finally { texDir = TexDir; matDir = MatDir; meshDir = MeshDir; }

            BuildPlayableClinic();
            var entry = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Berth 04 playable - loads the clinic, then the district").AddComponent<Berth04Playable>();
            EditorSceneManager.SaveScene(entry, PlayableEntry);
            AssetDatabase.SaveAssets();
            var glowOff = AssetDatabase.FindAssets("Glow_ t:Material", new[] { PlayableDir + "/Materials" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !File.ReadAllText(p).Contains("- _EMISSION")).ToArray();
            log.Append("glow check on disk: ").Append(glowOff.Length).Append(" Glow materials without the keyword ").Append(string.Join(", ", glowOff.Select(Path.GetFileNameWithoutExtension))).Append('\n');
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "playable_report.txt"), log.ToString());
            Debug.Log("[Exterior] playable scene built\n" + log);
        }

        static void CaptureAtmosphere(DistrictAtmosphere a)
        {
            a.fog = RenderSettings.fog; a.fogMode = RenderSettings.fogMode; a.fogColour = RenderSettings.fogColor;
            a.fogDensity = RenderSettings.fogDensity * FogLift; a.fogStart = RenderSettings.fogStartDistance; a.fogEnd = RenderSettings.fogEndDistance;
            if (RenderSettings.ambientMode == AmbientMode.Flat) a.ambientSky = a.ambientEquator = a.ambientGround = RenderSettings.ambientLight;
            else { a.ambientSky = RenderSettings.ambientSkyColor; a.ambientEquator = RenderSettings.ambientEquatorColor; a.ambientGround = RenderSettings.ambientGroundColor; }
            a.skybox = RenderSettings.skybox;
            a.sun = RenderSettings.sun;
            log.Append("atmosphere: fog ").Append(a.fog).Append(' ').Append(a.fogDensity).Append(", ambient ").Append(a.ambientSky).Append(", sky ")
               .Append(a.skybox ? a.skybox.name : "none").Append(", sun ").Append(a.sun ? a.sun.name : "none").Append('\n');
        }

        static void RemoveCameras(Transform holder)
        {
            int n = 0;
            foreach (var c in holder.GetComponentsInChildren<Camera>(true))
            {
                var go = c.gameObject;
                if (go.transform.childCount == 0 && go.GetComponent<Renderer>() == null) { Object.DestroyImmediate(go); n++; continue; }
                foreach (var comp in go.GetComponents<Component>().Where(k => k is AudioListener || k.GetType().Name == "UniversalAdditionalCameraData").ToArray()) Object.DestroyImmediate(comp);
                Object.DestroyImmediate(c);
                n++;
            }
            log.Append("cameras removed: ").Append(n).Append('\n');
        }

        // Everything of the district inside the clinic's walls goes; so do the gate parts standing in its door frame.
        static void ClearClinicRoom(Transform holder)
        {
            var assets = holder.Cast<Transform>().FirstOrDefault(t => t.name == "02 Clinic assets from the project");
            if (assets != null) { assets.gameObject.SetActive(false); log.Append("clinic assets placed in the B1: off\n"); }

            // Down to 30 cm below the floor: the B1's own floor sits 4 mm above the clinic's and would show through it.
            var room = new Bounds(new Vector3(0, 1.34f, 0), new Vector3(6.76f, 3.28f, 6.76f));
            var doorway = new Bounds(new Vector3(0, 1.255f, -3.58f), new Vector3(1.84f, 2.45f, .56f));
            var passage = new Bounds(new Vector3(0, 1.275f, -3.6f), new Vector3(1.56f, 2.05f, .7f));
            var gone = new List<string>(); var large = new List<string>(); var kept = new List<string>();
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                // By where a thing stands, not by touching: the building's street facade, its ground-floor flank and the
                // B1's retaining walls and header round the gate all reach into the room box but are what the street sees.
                // Only the B1's room-side linings between its front wall and the clinic's south wall go from the doorway.
                var b = r.bounds;
                bool inRoom = room.Contains(b.center);
                bool inDoor = !inRoom && b.Intersects(doorway) && b.min.z > -3.58f && b.max.z < -3.38f;
                if (!inRoom && !inDoor) { if (b.Intersects(room) || b.Intersects(doorway)) kept.Add(HierPath(r.transform)); continue; }
                if (b.size.x > 20 || b.size.z > 20) { large.Add(HierPath(r.transform)); continue; }
                r.enabled = false;
                gone.Add((inRoom ? "room: " : "door: ") + HierPath(r.transform));
            }
            int colliders = 0;
            foreach (var c in holder.GetComponentsInChildren<Collider>(true))
            {
                if (!c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger) continue;
                var b = c.bounds;
                if ((b.Intersects(room) && b.size.x < 20 && b.size.z < 20) || b.Intersects(passage)) { c.enabled = false; colliders++; }
            }
            int lights = 0;
            foreach (var l in holder.GetComponentsInChildren<Light>(true))
                if (l.enabled && l.type != LightType.Directional && room.Contains(l.transform.position)) { l.enabled = false; lights++; }
            int probes = 0;
            foreach (var p in holder.GetComponentsInChildren<ReflectionProbe>(true))
                if (p.enabled && room.Contains(p.transform.position)) { p.enabled = false; probes++; }
            log.Append("inside the clinic room / in its doorway: ").Append(gone.Count).Append(" renderers off, ").Append(colliders).Append(" colliders off, ")
               .Append(lights).Append(" lights off, ").Append(probes).Append(" probes off\n");
            foreach (var s in gone.Where(s => s.StartsWith("door"))) log.Append("  ").Append(s).Append('\n');
            foreach (var s in kept) log.Append("  kept (stands outside, touches the room or door): ").Append(s).Append('\n');
            foreach (var s in large) log.Append("  kept (large, crosses the room or door): ").Append(s).Append('\n');
        }

        // Over the stair nosings, from the landing (y 0 at z -1.80) to the top (y 1.36 at z -4.07), district frame.
        static void StairRamp(Transform g)
        {
            var bottom = new Vector3(-1.48f, 0, -1.796f) + PlayableOffset;
            var top = new Vector3(-1.48f, 1.36f, -4.07f) + PlayableOffset;
            var along = (top - bottom).normalized;
            var up = Vector3.Cross(along, Vector3.right).normalized;
            if (up.y < 0) up = -up;
            float length = Vector3.Distance(bottom, top);
            const float extra = .3f, thick = .3f;
            var go = new GameObject("Stair ramp - walk collider over the nosings");
            go.layer = 2;
            go.transform.SetParent(g, false);
            go.transform.SetPositionAndRotation(bottom + along * ((length - extra) * .5f) - up * (thick * .5f), Quaternion.LookRotation(along, up));
            go.AddComponent<BoxCollider>().size = new Vector3(1.76f, thick, length + extra);
            log.Append("stair ramp: ").Append(length.ToString("F2")).Append(" m at ").Append((Mathf.Atan2(1.36f, 2.274f) * Mathf.Rad2Deg).ToString("F1")).Append(" deg\n");
        }

        static void SnowExclusion(Transform holder)
        {
            var ex = holder.GetComponentsInChildren<BoxCollider>(true).FirstOrDefault(c => c.name == "Indoor snow exclusion");
            if (ex == null) { log.Append("snow exclusion: not found\n"); return; }
            ex.transform.position = new Vector3(B1Room.center.x, 1.6f, B1Room.center.z);
            ex.transform.rotation = Quaternion.identity;
            ex.center = Vector3.zero;
            ex.size = new Vector3((B1Room.size.x + .3f) / ex.transform.lossyScale.x, 3.4f / ex.transform.lossyScale.y, (B1Room.size.z + .4f) / ex.transform.lossyScale.z);
            log.Append("snow exclusion moved over the B1\n");
        }

        // The street window (district x 0..2.3) looks into the dead corner beside the clinic's south-east wall.
        static void WindowBacking(Transform g)
        {
            var m = Unlit("Playable - window backing, dim warm room", new Color(.11f, .085f, .06f));
            var go = Box("Street window backing", g, new Vector3(1.15f + PlayableOffset.x, 2.12f, -3.47f), new Vector3(2.5f, .7f, .03f), m);
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static readonly string[] UnlitShaders = { "BorderRepair/Exterior/Beacon", "BorderRepair/Exterior/Hologram", "BorderRepair/Exterior/LampVolume",
            "BorderRepair/Exterior/CityBlock", "BorderRepair/Exterior/FarSilhouette", "BorderRepair/Exterior/TrafficStream", "BorderRepair/Exterior/SkyBeam",
            "BorderRepair/Exterior/HorizonGlow", "Universal Render Pipeline/Unlit" };

        // Small things (under 30 cm across) and anything drawn with an unlit shader stop casting shadows.
        static void TrimShadowCasters(Transform holder)
        {
            int before = 0, small = 0, unlit = 0;
            foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || r.shadowCastingMode == ShadowCastingMode.Off || r is ParticleSystemRenderer) continue;
                before++;
                if (r.sharedMaterials.All(m => m != null && UnlitShaders.Contains(m.shader.name))) { r.shadowCastingMode = ShadowCastingMode.Off; unlit++; continue; }
                var s = r.bounds.size;
                if (Mathf.Max(s.x, Mathf.Max(s.y, s.z)) < .3f) { r.shadowCastingMode = ShadowCastingMode.Off; small++; }
            }
            log.Append("shadow casters: ").Append(before).Append(" -> ").Append(before - small - unlit).Append(" (").Append(small).Append(" small, ").Append(unlit).Append(" unlit)\n");
        }

        static void SkyProbe(Transform g)
        {
            var go = new GameObject("Sky reflection - outdoors");
            go.transform.SetParent(g, false);
            go.transform.position = new Vector3(0, 40, 0);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.size = new Vector3(900, 400, 900);
            probe.importance = 0; probe.blendDistance = 0; probe.resolution = 64; probe.hdr = true;
            probe.cullingMask = 0;
            var path = PlayableDir + "/Sky reflection.exr";
            Lightmapping.BakeReflectionProbe(probe, path);
            AssetDatabase.ImportAsset(path);
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            log.Append("sky reflection probe: ").Append(probe.customBakedTexture != null).Append('\n');
        }

        static bool InClinicZone(Vector3 p) => p.x > -2.3f && p.x < 5.35f && p.z > -3.62f && p.z < 2.95f;   // the B1
        // The door, the landing and the stairs up to the street: nothing new may stand in the way.
        static readonly Bounds StairCorridor = new Bounds(new Vector3(.05f, 1.3f, -6.5f), new Vector3(1.7f, 3.4f, 6.6f));

        // Static, lit things standing at walking height that the district left without a collider (vending machines, poles,
        // shutters, the far shopfronts, residents standing about) get a box over their mesh. Snow, lamp volumes, holograms
        // and the like do not; nor anything in the clinic room or on the way from its door to the street.
        static void StreetColliders(Transform holder)
        {
            int added = 0;
            foreach (var r in holder.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled || r.GetComponent<Collider>() != null) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !r.gameObject.isStatic) continue;
                if (r.sharedMaterials.Any(m => m == null || UnlitShaders.Contains(m.shader.name) || m.renderQueue >= 2450)) continue;
                var path = HierPath(r.transform);
                if (path.Contains("Snow") || path.Contains("snow") || path.Contains("Skyline")) continue;
                var b = r.bounds;
                float wide = Mathf.Max(b.size.x, b.size.z);
                if (wide < .06f || wide > 8 || b.size.y < .25f) continue;
                if (InClinicZone(b.center) || b.Intersects(StairCorridor)) continue;
                if (!Physics.Raycast(new Vector3(b.center.x, b.max.y + .05f, b.center.z), Vector3.down, out var hit, 60, ~(1 << 2), QueryTriggerInteraction.Ignore)) continue;
                float ground = hit.point.y;
                if (b.min.y > ground + 1.5f || b.max.y < ground + .3f) continue;
                var box = r.gameObject.AddComponent<BoxCollider>();
                box.center = mf.sharedMesh.bounds.center; box.size = mf.sharedMesh.bounds.size;
                added++;
            }
            log.Append("street furniture given a box collider: ").Append(added).Append('\n');
        }

        // Where the player can get to from the top of the stairs: a 0.5 m grid of downward rays over the colliders, the
        // harbour water left out (its collider is a flat floor at y 0 that runs under the buildings), flooded across rises
        // up to 40 cm. Every edge of that area that ends in nothing or drops more than 1.5 m gets an invisible wall
        // (Ignore Raycast layer, so nothing aims at it), except at the clinic. Report: playable_walk_map.png, walls yellow.
        static void WalkMapAndWalls(Vector3 start, Transform holder, Transform g)
        {
            const float cell = .5f, x0 = -130, x1 = 60, z0 = -70, z1 = 70;
            var water = holder.GetComponentsInChildren<Collider>(true).Where(c => c.name == "Water basin" && c.enabled).ToList();
            foreach (var c in water) c.enabled = false;
            Physics.SyncTransforms();
            int nx = Mathf.RoundToInt((x1 - x0) / cell), nz = Mathf.RoundToInt((z1 - z0) / cell);
            var h = new float[nx, nz];
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    var o = new Vector3(x0 + (i + .5f) * cell, 40, z0 + (k + .5f) * cell);
                    h[i, k] = Physics.Raycast(o, Vector3.down, out var hit, 80, ~(1 << 2), QueryTriggerInteraction.Ignore) && hit.normal.y > .64f ? hit.point.y : float.NaN;
                }
            foreach (var c in water) c.enabled = true;
            var reach = new bool[nx, nz];
            int si = Mathf.FloorToInt((start.x - x0) / cell), sk = Mathf.FloorToInt((start.z - z0) / cell);
            var q = new Queue<(int, int)>();
            if (si >= 0 && sk >= 0 && si < nx && sk < nz && !float.IsNaN(h[si, sk])) { reach[si, sk] = true; q.Enqueue((si, sk)); }
            var steps = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            while (q.Count > 0)
            {
                var (i, k) = q.Dequeue();
                foreach (var (di, dk) in steps)
                {
                    int a = i + di, b = k + dk;
                    if (a < 0 || b < 0 || a >= nx || b >= nz || reach[a, b] || float.IsNaN(h[a, b])) continue;
                    float dh = h[a, b] - h[i, k];
                    if (dh > .4f || dh < -1.5f) continue;
                    reach[a, b] = true; q.Enqueue((a, b));
                }
            }
            // Edges: per line between rows (walls facing z) or columns (facing x), the cells along it and their heights.
            var zEdges = new Dictionary<int, List<(int at, float h)>>();
            var xEdges = new Dictionary<int, List<(int at, float h)>>();
            var wallCells = new bool[nx, nz];
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    if (!reach[i, k]) continue;
                    foreach (var (di, dk) in steps)
                    {
                        int a = i + di, b = k + dk;
                        bool open = a < 0 || b < 0 || a >= nx || b >= nz || float.IsNaN(h[a, b]) || h[a, b] - h[i, k] < -1.5f;
                        if (!open) continue;
                        var mid = new Vector3(x0 + (i + .5f + di * .5f) * cell, 0, z0 + (k + .5f + dk * .5f) * cell);
                        if (InClinicZone(mid)) continue;
                        wallCells[i, k] = true;
                        var edges = dk != 0 ? zEdges : xEdges;
                        int line = dk != 0 ? k + (dk > 0 ? 1 : 0) : i + (di > 0 ? 1 : 0);
                        if (!edges.TryGetValue(line, out var l)) edges[line] = l = new List<(int, float)>();
                        l.Add((dk != 0 ? i : k, h[i, k]));
                    }
                }
            var walls = new GameObject("Edge walls - keep the player on the map").transform;
            walls.SetParent(g, false);
            int count = 0;
            foreach (var facingZ in new[] { true, false })
                foreach (var kv in facingZ ? zEdges : xEdges)
                {
                    var list = kv.Value.OrderBy(e => e.at).ToList();
                    int from = 0;
                    for (int n = 1; n <= list.Count; n++)
                    {
                        bool breaks = n == list.Count || list[n].at != list[n - 1].at + 1 || Mathf.Abs(list[n].h - list[from].h) > .5f;
                        if (!breaks) continue;
                        var run = list.Skip(from).Take(n - from).ToList();
                        float lo = run.Min(e => e.h) - .4f, hi = run.Max(e => e.h) + 2.4f;
                        float a0 = (facingZ ? x0 : z0) + list[from].at * cell, a1 = (facingZ ? x0 : z0) + (list[n - 1].at + 1) * cell;
                        float line = (facingZ ? z0 : x0) + kv.Key * cell;
                        var go = new GameObject("Edge wall");
                        go.layer = 2;
                        go.transform.SetParent(walls, false);
                        go.transform.position = facingZ ? new Vector3((a0 + a1) * .5f, (lo + hi) * .5f, line) : new Vector3(line, (lo + hi) * .5f, (a0 + a1) * .5f);
                        go.AddComponent<BoxCollider>().size = facingZ ? new Vector3(a1 - a0 + .1f, hi - lo, .1f) : new Vector3(.1f, hi - lo, a1 - a0 + .1f);
                        count++;
                        from = n;
                    }
                }

            var tex = new Texture2D(nx, nz, TextureFormat.RGB24, false);
            int area = 0; var rb = new Bounds(start, Vector3.zero);
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    Color c = float.IsNaN(h[i, k]) ? Color.black : new Color(.18f, .18f, .2f) + Color.white * Mathf.Clamp01(h[i, k] / 12f) * .4f;
                    if (reach[i, k]) { c = new Color(.25f, .6f, .35f) + Color.white * Mathf.Clamp01(h[i, k] / 6f) * .3f; area++; rb.Encapsulate(new Vector3(x0 + i * cell, h[i, k], z0 + k * cell)); }
                    if (wallCells[i, k]) c = new Color(.95f, .8f, .1f);
                    tex.SetPixel(i, k, c);
                }
            for (int d = -2; d <= 2; d++) tex.SetPixel(Mathf.FloorToInt((0 - x0) / cell) + d, Mathf.FloorToInt((-3.5f - z0) / cell), Color.blue);
            tex.SetPixel(si, sk, Color.red);
            tex.Apply();
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, "playable_walk_map.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            log.Append("walkable from the stairs: ").Append(area * cell * cell).Append(" m2, x ").Append(rb.min.x.ToString("F0")).Append("..").Append(rb.max.x.ToString("F0"))
               .Append(", z ").Append(rb.min.z.ToString("F0")).Append("..").Append(rb.max.z.ToString("F0")).Append("; edge walls: ").Append(count).Append('\n');
        }
    }
}
