using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // Street motion (DistrictMotion, runtime): an NSP patrol skimmer that comes down the street from the harbour end
    // with its searchlight on the snow, a heavy lifter crossing over the harbour with a container slung under it, far
    // air traffic on three lanes, the sodium lamp at x -13 failing (and the one across from the clinic stuttering now
    // and then), the train running on the far viaduct, the delivery drone holding station. The craft are built from
    // primitives like the drone (no character or vehicle models are expected from the user for these).
    //
    // MotionClip renders the motion frame by frame with time pinned (DistrictMotion.PinShaderTime), so the shader
    // blinks match the frame rate: MOTION_VIEWS="camera name@start seconds;...", MOTION_SECONDS (12), MOTION_FPS (24),
    // MOTION_PUSH (metres the camera drifts forward over a clip, .5). Frames go to <EXTERIOR_OUT>/clip/<view>/.
    public static partial class ExteriorDistrictPass
    {
        static void StreetMotion(Transform g)
        {
            var motion = new GameObject("Motion - craft, lamps, train, drone").AddComponent<DistrictMotion>();
            motion.transform.SetParent(g, false);
            var flyers = new List<DistrictMotion.Flyer>();
            var sweeps = new List<DistrictMotion.Sweep>();
            var blinkers = new List<DistrictMotion.Blinker>();

            PatrolSkimmer(Group(g, "Craft - NSP patrol skimmer"), flyers, sweeps, blinkers);
            HeavyLifter(Group(g, "Craft - heavy lifter over the harbour"), flyers, blinkers);
            AirTraffic(Group(g, "Craft - far air traffic"), flyers, blinkers);

            motion.flyers = flyers.ToArray();
            motion.sweeps = sweeps.ToArray();
            motion.blinkers = blinkers.ToArray();
            motion.lamps = TiredLamps();
            motion.runners = TrainOnViaduct();
            var drone = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == "Cyber - delivery drone");
            motion.hovers = drone == null ? new DistrictMotion.Hover[0] : new[] { new DistrictMotion.Hover { body = drone, rest = drone.localPosition, amplitude = .07f, rate = .45f } };
            motion.Evaluate(0);
            log.Append("motion: ").Append(motion.flyers.Length).Append(" craft, ").Append(motion.blinkers.Length).Append(" blinkers, ")
               .Append(motion.lamps.Length).Append(" tired lamps, ").Append(motion.runners.Sum(r => r.parts.Length)).Append(" train parts, ")
               .Append(motion.hovers.Length).Append(" hovering\n");
        }

        // Whatever DistrictMotion moves must not be static: static batching freezes it in play mode.
        static void UnpinMovingParts()
        {
            var motion = Object.FindFirstObjectByType<DistrictMotion>();
            if (motion == null) return;
            var moving = motion.flyers.Select(f => f.body).Concat(motion.hovers.Select(h => h.body)).Concat(motion.runners.SelectMany(r => r.parts))
                .Concat(motion.sweeps.Select(s => s.pivot)).Where(t => t != null);
            int n = 0;
            foreach (var t in moving)
                foreach (var c in t.GetComponentsInChildren<Transform>(true)) { GameObjectUtility.SetStaticEditorFlags(c.gameObject, 0); n++; }
            log.Append("motion: ").Append(n).Append(" moving objects left non-static\n");
        }

        // Built about the origin, nose along +Z; DistrictMotion poses the group.
        static void PatrolSkimmer(Transform g, List<DistrictMotion.Flyer> flyers, List<DistrictMotion.Sweep> sweeps, List<DistrictMotion.Blinker> blinkers)
        {
            var hull = Worn("Ship - hull, NSP grey paint steel", Hex("#3E4447"), .35f, .3f);
            var dark = Worn("Ship - engine pod, worn steel", Hex("#232729"), .4f, .5f);
            var canopy = Lit("Ship - canopy, lit glass", new Color(.05f, .08f, .09f), .85f, 0, null, HoloCyan * .35f);
            Box("Hull", g, Vector3.zero, new Vector3(1.7f, .8f, 5.2f), hull);
            Box("Nose", g, new Vector3(0, -.1f, 3f), new Vector3(1.3f, .55f, 1.5f), hull, Quaternion.Euler(12, 0, 0));
            Box("Canopy", g, new Vector3(0, .45f, 1.3f), new Vector3(1.05f, .36f, 1.7f), canopy, Quaternion.Euler(-7, 0, 0));
            Box("Spine", g, new Vector3(0, .5f, -1.2f), new Vector3(.7f, .3f, 2.4f), hull);
            Box("NSP stripe", g, new Vector3(0, .12f, .2f), new Vector3(1.74f, .12f, 3.2f), Beacon("Ship - NSP stripe, cyan", HoloCyan, 2.2f, 0, 1));
            foreach (var s in new[] { -1f, 1f })
            {
                Box("Stub wing", g, new Vector3(s * 1.45f, -.05f, -.4f), new Vector3(1.4f, .14f, 1.5f), hull);
                Rod("Engine pod", g, new Vector3(s * 2.2f, 0, 1.1f), new Vector3(s * 2.2f, 0, -1.7f), .38f, dark);
                Cyl("Exhaust glow", g, new Vector3(s * 2.2f, 0, -1.72f), .3f, .06f, Beacon("Ship - exhaust, hot", new Color(1f, .55f, .25f), 5f, 0, 1), Quaternion.Euler(90, 0, 0));
                Box("Tail fin", g, new Vector3(s * .55f, .75f, -2.3f), new Vector3(.1f, .9f, 1.1f), hull, Quaternion.Euler(0, 0, -s * 14));
                Sphere(s < 0 ? "Nav light, port red" : "Nav light, starboard green", g, new Vector3(s * 2.62f, 0, -.4f), .1f,
                    s < 0 ? Beacon("Ship - nav red", new Color(1f, .18f, .12f), 4f, 0, 1) : Beacon("Ship - nav green", new Color(.25f, 1f, .4f), 4f, 0, 1));
            }
            foreach (var z in new[] { -1.4f, 1.3f })
                Cyl("Lift fan glow", g, new Vector3(0, -.42f, z), .32f, .04f, Beacon("Ship - lift fan, cyan", new Color(.45f, .9f, 1f), 1.4f, 0, 1));
            // Two landing lamps in the nose and a cyan wash under the hull, so the craft reads against the night sky.
            foreach (var s in new[] { -1f, 1f })
                Sphere("Nose lamp", g, new Vector3(s * .42f, -.22f, 3.72f), .11f, Beacon("Ship - nose lamp", new Color(1f, .97f, .9f), 9f, 0, 1));
            AddLight(g, "Hull underglow", new Vector3(0, -1.1f, 0), LightType.Point, new Color(.45f, .9f, 1f), 3f, 7f, false);

            var strobe = Sphere("Strobe, white", g, new Vector3(0, 1.22f, -2.6f), .09f, Beacon("Ship - strobe white", Color.white, 8f, 0, 1));
            var strobeLight = AddLight(g, "Strobe flash", new Vector3(0, 1.3f, -2.6f), LightType.Point, new Color(.92f, .96f, 1f), 0, 16, false);
            blinkers.Add(new DistrictMotion.Blinker { lamps = new[] { strobe.GetComponent<Renderer>() }, lampIntensity = 8, light = strobeLight, lightIntensity = 14, period = 1.4f, on = .05f, twin = true });
            var belly = Sphere("Anti-collision, red", g, new Vector3(0, -.47f, -.6f), .1f, Beacon("Ship - anti-collision red", new Color(1f, .15f, .1f), 6f, 0, 1));
            blinkers.Add(new DistrictMotion.Blinker { lamps = new[] { belly.GetComponent<Renderer>() }, lampIntensity = 6, period = 1.1f, on = .2f, phase = .4f });

            // Searchlight under the nose, swinging over the street.
            var mount = new Vector3(0, -.5f, 2.3f);
            Cyl("Searchlight housing", g, mount + Vector3.up * .08f, .2f, .22f, dark);
            var pivot = new GameObject("Searchlight pivot").transform;
            pivot.SetParent(g, false);
            pivot.localPosition = mount;
            var rest = Quaternion.Euler(66, 0, 0);
            pivot.localRotation = rest;
            Sphere("Searchlight lens", pivot, mount, .15f, Beacon("Ship - searchlight lens", new Color(.9f, .95f, 1f), 7f, 0, 1));
            var spot = AddLight(pivot, "Searchlight", mount, LightType.Spot, new Color(.84f, .91f, 1f), 160, 40, false);
            spot.transform.localRotation = Quaternion.identity;
            spot.spotAngle = 24; spot.innerSpotAngle = 9;
            Beam("Searchlight beam", pivot, 15f, .12f, 3f, Volume("Ship - searchlight beam", new Color(.75f, .85f, .95f), .75f));
            sweeps.Add(new DistrictMotion.Sweep { pivot = pivot, rest = rest, side = 22, pitch = 10, rate = .16f });

            flyers.Add(new DistrictMotion.Flyer
            {
                body = g, travel = 24, period = 45, phase = 0, bank = 18,
                // In from over the harbour, down the street two metres over the roofs, slowing to a crawl over the clinic
                // with the searchlight on the snow (points close together are slow: every span takes the same time), then
                // away over the blocks.
                path = new[]
                {
                    new Vector3(-110, 24, -9), new Vector3(-70, 18, -8), new Vector3(-40, 14, -7), new Vector3(-20, 12.6f, -6.5f), new Vector3(-9, 12.2f, -6.2f),
                    new Vector3(-3.5f, 12f, -6f), new Vector3(.5f, 12f, -5.8f), new Vector3(5, 12.2f, -5.6f), new Vector3(12, 12.8f, -5f), new Vector3(28, 14.5f, -3),
                    new Vector3(55, 19, 1), new Vector3(90, 26, 10),
                },
            });
        }

        static void HeavyLifter(Transform g, List<DistrictMotion.Flyer> flyers, List<DistrictMotion.Blinker> blinkers)
        {
            var mass = FarMass("Ship - lifter silhouette", .28f);
            Box("Lifter hull", g, Vector3.zero, new Vector3(8, 3.6f, 22), mass);
            Box("Lifter bridge", g, new Vector3(0, 1.6f, 10), new Vector3(6, 1.8f, 3.5f), mass);
            Box("Lifter bridge windows", g, new Vector3(0, 1.85f, 10), new Vector3(6.06f, .38f, 3.56f), Beacon("Ship - lifter bridge windows", new Color(1f, .86f, .62f), 1.5f, 0, 1, .3f));
            var cabin = Beacon("Ship - lifter cabin lights", new Color(.85f, .9f, .9f), .9f, 0, 1, .35f);
            for (float z = -8f; z <= 8.1f; z += 1.6f)
                if (Hash(new Vector3(z, 3, 1)) > .3f) Box("Lifter port light", g, new Vector3(0, .7f, z), new Vector3(8.04f, .28f, .6f), cabin);
            var thrust = Beacon("Ship - lifter thrust", new Color(1f, .5f, .22f), 3.2f, 0, 1, .25f);
            var haze = Volume("Ship - lifter thrust haze", new Color(1f, .55f, .3f), .9f);
            foreach (var x in new[] { -1f, 1f })
                foreach (var z in new[] { -1f, 1f })
                {
                    var c = new Vector3(x * 5.4f, -.6f, z * 7.5f);
                    Box("Engine arm", g, new Vector3(x * 4.5f, -.4f, z * 7.5f), new Vector3(1.8f, .8f, 1.2f), mass);
                    Cyl("Lift engine", g, c, 1.5f, 1.8f, mass);
                    Cyl("Thrust glow", g, c + Vector3.down * .93f, 1.25f, .08f, thrust);
                    var b = new GameObject("Thrust haze").transform;
                    b.SetParent(g, false); b.localPosition = c + Vector3.down * 1f; b.localRotation = Quaternion.Euler(90, 0, 0);
                    Beam("Thrust haze", b, 7f, 1.1f, 2.2f, haze);
                }
            Box("Slung container", g, new Vector3(0, -5.3f, 0), new Vector3(3.2f, 3f, 10f), mass);
            foreach (var x in new[] { -1.3f, 1.3f })
                foreach (var z in new[] { -4.4f, 4.4f })
                    Rod("Sling cable", g, new Vector3(x, -1.8f, z), new Vector3(x, -3.8f, z), .07f, mass);
            var red = Beacon("Ship - lifter beacon red", new Color(1f, .16f, .1f), 6f, 0, 1, .2f);
            var tops = new[] { Sphere("Beacon, top aft", g, new Vector3(0, 2.0f, -10.5f), .5f, red), Sphere("Beacon, top fore", g, new Vector3(0, 2.7f, 10.5f), .5f, red) };
            blinkers.Add(new DistrictMotion.Blinker { lamps = tops.Select(o => o.GetComponent<Renderer>()).ToArray(), lampIntensity = 6, period = 1.6f, on = .4f });
            var white = Beacon("Ship - lifter strobe white", Color.white, 9f, 0, 1, .15f);
            var strobes = new[] { -1f, 1f }.Select(x => Sphere("Strobe, corner", g, new Vector3(x * 4.2f, 0, -11.2f), .45f, white)).ToArray();
            blinkers.Add(new DistrictMotion.Blinker { lamps = strobes.Select(o => o.GetComponent<Renderer>()).ToArray(), lampIntensity = 9, period = 2.2f, on = .07f, twin = true, phase = .5f });

            flyers.Add(new DistrictMotion.Flyer
            {
                body = g, travel = 64, period = 80, phase = 26, bank = 4,
                // Across the harbour, beyond the berths, at a crawl.
                path = new[] { new Vector3(-66, 26, -170), new Vector3(-70, 27, -60), new Vector3(-72, 28, 40), new Vector3(-68, 30, 160) },
            });
        }

        static void AirTraffic(Transform g, List<DistrictMotion.Flyer> flyers, List<DistrictMotion.Blinker> blinkers)
        {
            var mass = FarMass("Ship - far craft silhouette", .55f);
            var head = Beacon("Ship - far craft headlight", new Color(1f, .95f, .86f), 4.5f, 0, 1, .22f);
            var tail = Beacon("Ship - far craft tail red", new Color(1f, .2f, .14f), 5f, 0, 1, .22f);
            var lanes = new[]
            {
                (from: new Vector3(-150, 52, -240), to: new Vector3(-150, 55, 240), travel: 36f, phases: new[] { 0f, 12f, 24f }),
                (from: new Vector3(-178, 61, 240), to: new Vector3(-178, 58, -240), travel: 42f, phases: new[] { 5f, 19f, 33f }),
                (from: new Vector3(-230, 47, 150), to: new Vector3(230, 49, 150), travel: 34f, phases: new[] { 3f, 20f }),
            };
            int n = 0;
            foreach (var lane in lanes)
                foreach (var phase in lane.phases)
                {
                    var c = Group(g, "Far craft " + (++n));
                    Box("Far craft body", c, Vector3.zero, new Vector3(1.6f, .7f, 3.6f), mass);
                    Sphere("Far craft headlight", c, new Vector3(0, -.1f, 2f), .75f, head);
                    var t = Sphere("Far craft tail light", c, new Vector3(0, .2f, -2f), .5f, tail);
                    blinkers.Add(new DistrictMotion.Blinker { lamps = new[] { t.GetComponent<Renderer>() }, lampIntensity = 5, period = 1.2f, on = .3f, phase = n * .37f });
                    var mid = (lane.from + lane.to) * .5f + Vector3.up * (n % 2 == 0 ? 2 : -2);
                    flyers.Add(new DistrictMotion.Flyer { body = c, path = new[] { lane.from, mid, lane.to }, travel = lane.travel, period = lane.travel, phase = phase, bank = 0 });
                }
        }

        // A cone of LampVolume light from the transform's origin along its +Z (uv.y runs 0 at the apex to 1 at the end).
        static void Beam(string name, Transform parent, float length, float r0, float r1, Material m)
        {
            const int sides = 20;
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            for (int j = 0; j <= sides; j++)
            {
                float a = j * Mathf.PI * 2 / sides;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                v.Add(dir * r0); uv.Add(new Vector2(j / (float)sides, 0));
                v.Add(dir * r1 + Vector3.forward * length); uv.Add(new Vector2(j / (float)sides, 1));
            }
            for (int j = 0; j < sides; j++)
            {
                int a = j * 2, b = a + 1, c = a + 2, d = a + 3;
                var outward = new Vector3(v[b].x + v[d].x, v[b].y + v[d].y, 0);
                Tri(t, v, a, b, c, outward); Tri(t, v, c, b, d, outward);
            }
            var go = MeshObj(name, parent, SaveMesh(name, v, t, uv), m, parent.position, parent.rotation, false);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
        }

        // The three street lamps' heads get an emissive copy of their material (they were lit only by their own light);
        // the one at x -13 fails, the one across from the clinic stutters, the one at x 9 stays steady.
        static DistrictMotion.Lamp[] TiredLamps()
        {
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.name == "Aged sodium street lamp").ToList();
            var heads = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name == "Diffuser").ToList();
            var cones = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name.StartsWith("Lamp volume - street", System.StringComparison.Ordinal)).ToList();
            Material glowing = null;
            foreach (var h in heads)
            {
                if (glowing == null)
                {
                    if (!mats.TryGetValue("Lamp head - glowing", out glowing))
                    {
                        var copy = new Material(h.sharedMaterial);
                        copy.EnableKeyword("_EMISSION");
                        copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                        copy.SetColor("_EmissionColor", new Color(1.5f, 1.32f, 1.05f));
                        glowing = Save("Lamp head - glowing", copy);
                    }
                }
                h.sharedMaterial = glowing;
            }
            var result = new List<DistrictMotion.Lamp>();
            foreach (var (x, seed, severity) in new[] { (-13f, 3.7f, 1f), (-2f, 9.1f, .3f) })
            {
                var l = lights.FirstOrDefault(o => Mathf.Abs(o.transform.position.x - x) < .6f);
                if (l == null) continue;
                var p = l.transform.position;
                var glows = new List<Renderer>(); var props = new List<string>(); var bases = new List<Color>();
                foreach (var h in heads.Where(r => Vector3.Distance(r.bounds.center, p) < 1f)) { glows.Add(h); props.Add("_EmissionColor"); bases.Add(glowing.GetColor("_EmissionColor")); }
                foreach (var c in cones.Where(r => Mathf.Abs(r.transform.position.x - p.x) < .6f && Mathf.Abs(r.transform.position.z - p.z) < .6f))
                {
                    glows.Add(c); props.Add("_Intensity");
                    bases.Add(new Color(0, 0, 0, c.sharedMaterial.GetFloat("_Intensity")));
                }
                result.Add(new DistrictMotion.Lamp { light = l, intensity = l.intensity, colour = l.color, glows = glows.ToArray(), glowProperty = props.ToArray(), glowBase = bases.ToArray(), seed = seed, severity = severity });
            }
            return result.ToArray();
        }

        static DistrictMotion.Runner[] TrainOnViaduct()
        {
            var parts = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name == "Train car" || t.name == "Train windows").OrderBy(t => t.position.z).ToArray();
            if (parts.Length == 0) return new DistrictMotion.Runner[0];
            return new[] { new DistrictMotion.Runner { parts = parts, rest = parts.Select(p => p.position.z).ToArray(), speed = 14, wrapAt = 150, length = 300, visibleHalf = 90 } };
        }

        public static void MotionClip()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ShaderUtil.allowAsyncCompilation = false;
            var motion = Object.FindFirstObjectByType<DistrictMotion>();
            if (motion == null) { Debug.LogError("[Motion] no DistrictMotion in the scene; run ApplyInterior first"); return; }
            float seconds = Env("MOTION_SECONDS", 12), fps = Env("MOTION_FPS", 24), push = Env("MOTION_PUSH", .5f);
            var spec = System.Environment.GetEnvironmentVariable("MOTION_VIEWS") ?? "Street E - down the street, both sides@0";
            var roots = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Where(ps => ps.transform.parent == null || ps.transform.parent.GetComponent<ParticleSystem>() == null).ToArray();
            // MOTION_CAMS="label|x y z|x y z;..." adds temporary cameras (eye, look-at) the views can name.
            var temporary = new List<GameObject>();
            foreach (var c in (System.Environment.GetEnvironmentVariable("MOTION_CAMS") ?? "").Split(';').Where(s => s.Contains("|")))
            {
                var p = c.Split('|');
                Vector3 V(string s) { var n = s.Trim().Split(' ').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); return new Vector3(n[0], n[1], n[2]); }
                var go = new GameObject(p[0].Trim());
                go.transform.SetPositionAndRotation(V(p[1]), Quaternion.LookRotation(V(p[2]) - V(p[1])));
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = 55; cam.nearClipPlane = .05f; cam.farClipPlane = 300; cam.enabled = false;
                temporary.Add(go);
            }
            var cams = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            int frames = Mathf.RoundToInt(seconds * fps), total = 0;
            try
            {
                foreach (var item in spec.Split(';').Where(s => s.Trim().Length > 0))
                {
                    var bits = item.Split('@');
                    var cam = cams.FirstOrDefault(c => c.name == bits[0].Trim());
                    if (cam == null) { Debug.LogWarning("[Motion] no camera " + bits[0]); continue; }
                    float start = bits.Length > 1 ? float.Parse(bits[1], System.Globalization.CultureInfo.InvariantCulture) : 0;
                    var dir = Path.Combine(OutDir, "clip", Safe(cam.name));
                    Directory.CreateDirectory(dir);
                    var eye = cam.transform.position;
                    foreach (var ps in roots) ps.Simulate(8f, true, true);
                    var was = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.aspect = 1280f / 720f;
                    motion.Evaluate(start);
                    DistrictMotion.PinShaderTime(start);
                    cam.Render();   // warm-up: the first render after a target change came out with the old framing
                    for (int f = 0; f < frames; f++)
                    {
                        float t = start + f / fps;
                        motion.Evaluate(t);
                        DistrictMotion.PinShaderTime(t);
                        if (f > 0) foreach (var ps in roots) ps.Simulate(1f / fps, true, false);
                        cam.transform.position = eye + cam.transform.forward * (push * f / Mathf.Max(frames - 1, 1));
                        cam.targetTexture = rt;
                        cam.Render();
                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                        tex.Apply();
                        RenderTexture.active = null;
                        File.WriteAllBytes(Path.Combine(dir, "f" + f.ToString("0000") + ".jpg"), tex.EncodeToJPG(93));
                        total++;
                    }
                    cam.targetTexture = was;
                    cam.ResetAspect();
                    cam.transform.position = eye;
                }
            }
            finally
            {
                foreach (var go in temporary) Object.DestroyImmediate(go);
                DistrictMotion.ReleaseShaderTime();
                rt.Release();
            }
            Debug.Log("[Motion] rendered " + total + " frames into " + Path.Combine(OutDir, "clip"));
        }

        static float Env(string name, float fallback) =>
            float.TryParse(System.Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
