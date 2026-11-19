using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // Palette pass (Docs/Art/PALETTE_BR2049_PS2.md): 60% cold haze grey, 25% iron-teal shadow, 15% light accents, of which
    // warm amber belongs to the shop alone and cold holo cyan carries the projected signage. Outside is cold; the B1
    // entrance is the one warm thing on the street. No magenta, no purple.
    public static partial class ExteriorDistrictPass
    {
        static readonly Color FogTeal = Hex("#3B4B54"), ConcreteTeal = Hex("#253238"), IronShadow = Hex("#141C20"), ColdRust = Hex("#4E433B"),
            Amber = Hex("#E89A3C"), HoloCyan = Hex("#6BE0E8"), CrtGreen = Hex("#5CE67E"), AlarmRed = Hex("#C93B3B");

        // Albedos behind the palette's on-screen colours (they read darker under the night lights and the haze).
        static readonly Color SnowAlbedo = new Color(.6f, .66f, .7f),
            ConcreteAlbedo = new Color(.26f, .32f, .35f), StuccoAlbedo = new Color(.32f, .35f, .36f), StreetLamp = Hex("#DCD4C4");   // aged LED/metal halide: neutral, a touch warm; brighter than the first cold pass

        // The district's surfaces move off the handed-over unlit shaders (fake lamp pools, their own near-black fog) onto
        // the lit, world-mapped Surface shader; the speckled glass becomes dark frosted panes. Only this scene's renderers
        // are re-pointed; the handed-over materials themselves are untouched.
        static void ApplyPalette()
        {
            RenderSettings.fogColor = FogTeal;
            RenderSettings.fogDensity = .025f;   // a touch more haze than the handed-over .022
            log.Append("fog colour -> #3B4B54, density .025\n");

            // Walls: scanned weathered concrete (rain streaks, tie holes, grime) and panelled plaster, CC0 from Poly Haven.
            var concrete = SurfaceMat("Concrete - weathered", "srf_wall_grime", 3.2f, 0, ConcreteAlbedo, .85f, .12f, .06f);
            var stucco = SurfaceMat("Concrete - plaster", "srf_wall_plaster", 3.6f, 0, StuccoAlbedo, .85f, .12f, .06f);
            var iron = SurfaceMat("Iron - salt stained", "iron_paint", 1.1f, 0, ColdRust * 1.1f, .55f, .3f, .2f, normal: null);
            // Ground: snow over the real ground, which shows through where it is trodden, driven over or blown thin.
            var steps = SurfaceMat("Snow - pavement and steps", "srf_wall_plaster", 2.4f, 1, new Color(.3f, .32f, .33f), 0, .22f, .12f,
                                   ground: "srf_wall_plaster", groundColour: new Color(.22f, .23f, .24f), groundScale: 2.4f, cover: .73f);
            var ground = SurfaceMat("Snow - street, quay and court", "srf_asphalt", 2.5f, 2, new Color(.2f, .22f, .23f), 0, .22f, .14f,
                                    ground: "srf_asphalt", groundColour: new Color(.13f, .14f, .15f), groundScale: 2.5f, cover: .69f);
            var water = SurfaceMat("Water - harbour", null, 10f, 3, new Color(.1f, .14f, .16f), 0, .88f, .6f, normal: null,
                                   baseTex: AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/RepairProps/Scenes/Temp_HarbourStudy/harbour_water.png"));
            var pack = SurfaceMat("Snow - packed relief", null, 1f, 4, Color.white, 0, .22f, .1f, normal: null,
                                  ground: "srf_asphalt", groundColour: new Color(.14f, .15f, .16f), groundScale: 2.5f, cover: .77f);
            // Where the street shows through, it is wet: a little more sheen under the lamps than the default .5.
            foreach (var m in new[] { ground, pack }) { m.SetFloat("_WetSmoothness", .62f); EditorUtility.SetDirty(m); }
            var caps = SnowMaterial();
            var glass = Lit("Glass - night frost", Color.white, .85f, 0, Tex("glass_frost"));
            var swap = new Dictionary<string, Material>
            {
                { "shuttered concrete", concrete }, { "Temp_Street_distant_stucco", stucco }, { "Temp_Street_plaster", stucco },
                { "salt stained iron", iron }, { "trampled snow", steps }, { "quayside snow", ground }, { "snow and tyre marks", ground },
                { "dark river", water }, { "Snowpack", pack }, { "Temp_Street_snow", caps }, { "Temp_Street_salt_snow", caps },
                { "Temp_Street_night_glass", glass },
            };
            var counts = new Dictionary<string, int>();
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects().Where(r => ExteriorRoots.Contains(r.name) && r.name != PassRoot))
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var ms = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < ms.Length; i++)
                    {
                        if (ms[i] == null || !swap.TryGetValue(ms[i].name, out var to)) continue;
                        if (to == glass && r.name == "Distant rooftop") to = stucco;   // whole far blocks were wearing the glass
                        counts[ms[i].name] = counts.TryGetValue(ms[i].name, out var c) ? c + 1 : 1;
                        ms[i] = to; changed = true;
                    }
                    if (changed) r.sharedMaterials = ms;
                }
            log.Append("surfaces re-pointed: ").Append(counts.Count == 0 ? "none (already done)" : string.Join(", ", counts.Select(kv => kv.Key + " " + kv.Value))).Append('\n');

            // Outside goes cold: mercury street heads, cold floods on the wharf, a fluorescent tube at the residents' door.
            // Amber stays with the shop (the sign wash) and with light leaking out of rooms.
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                Color? colour = null; float? intensity = null;
                switch (l.name)
                {
                    case "Aged sodium street lamp": colour = StreetLamp; intensity = 6.5f; l.range = 10.5f; break;
                    case "Wharf amber pool": colour = new Color(.82f, .84f, .82f); intensity = 5.4f; break;
                    case "Court wall sconce": colour = new Color(.75f, .82f, .82f); intensity = 1.5f; break;
                    case "Street fill": colour = new Color(.62f, .7f, .74f); intensity = 4f; break;
                    case "Blue overcast at upper landing": colour = new Color(.62f, .71f, .76f); intensity = 3.4f; break;
                    case "Cold overcast sky light": colour = new Color(.66f, .74f, .78f); l.shadowStrength = .6f; break;   // overcast: soft, never black
                    case "Street sign wash": colour = Amber; intensity = 2.1f; break;
                    case "Reflected window light": colour = Amber; break;
                }
                if (colour.HasValue) l.color = colour.Value;
                if (intensity.HasValue) l.intensity = intensity.Value;
                if (colour.HasValue) log.Append("light ").Append(l.name).Append(" -> #").Append(ColorUtility.ToHtmlStringRGB(l.color)).Append(" i ").Append(l.intensity).Append('\n');
            }
            // The berth board was a self-lit amber panel, a second warm lamp on the quay; repainted cold enamel, lit by the flood.
            var pierSign = Lit("Berth board - cold enamel", Color.white, .3f, 0, Tex("pier_cold"), new Color(.3f, .34f, .35f), Tex("pier_cold"));
            foreach (var r in Renderers("District signage pier")) r.sharedMaterial = pierSign;
            var coldHead = Lit("Lamp head - neutral", new Color(.8f, .78f, .74f), .6f, 0, null, new Color(.95f, .92f, .86f) * 1.7f);
            foreach (var r in Renderers("Diffuser").Concat(Renderers("Sodium lamp lens")).Concat(Renderers("Entry lamp under lintel")))
                r.sharedMaterial = coldHead;
        }

        // tex/normal name textures in Textures/ (a normal map "<tex>_n" is used when it exists, unless normal is passed as null).
        static Material SurfaceMat(string name, string tex, float scale, float kind, Color tint, float topSnow, float smooth, float spec,
                                   string normal = "", string ground = null, Color? groundColour = null, float groundScale = 2.5f, float cover = .7f, Texture baseTex = null)
        {
            if (mats.TryGetValue(name, out var have)) return have;
            var m = new Material(Shader.Find("BorderRepair/Exterior/Surface"));
            var b = baseTex != null ? baseTex : tex != null ? Tex(tex) : null;
            if (b != null) m.SetTexture("_BaseMap", b);
            var nm = normal == "" && tex != null ? Tex(tex + "_n") : normal != null && normal != "" ? Tex(normal) : null;
            if (nm != null) m.SetTexture("_NormalMap", nm);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Scale", scale);
            m.SetFloat("_Kind", kind);
            m.SetColor("_SnowColor", SnowAlbedo);
            m.SetTexture("_SnowMap", Tex("srf_snow"));
            m.SetTexture("_SnowNormal", Tex("srf_snow_n"));
            m.SetFloat("_SnowScale", 2.2f);
            m.SetTexture("_ThinMap", Tex("srf_thin_snow"));
            m.SetFloat("_ThinScale", 1.6f);
            if (ground != null)
            {
                m.SetTexture("_GroundMap", Tex(ground));
                var gn = Tex(ground + "_n");
                if (gn != null) m.SetTexture("_GroundNormal", gn);
            }
            m.SetColor("_GroundColor", groundColour ?? new Color(.12f, .13f, .14f));
            m.SetFloat("_GroundScale", groundScale);
            m.SetFloat("_Cover", cover);
            m.SetFloat("_TopSnow", topSnow);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Specular", spec);
            return Save(name, m);
        }

        static Material Holo(string name, string tex, float intensity = 1.3f, float haze = -1, float flicker = .25f, float spin = 0, float cull = 2)
        {
            if (mats.TryGetValue(name, out var have)) return have;
            var m = new Material(Shader.Find("BorderRepair/Exterior/Hologram"));
            m.SetTexture("_MainTex", Tex(tex));
            m.SetColor("_Color", HoloCyan);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Haze", haze);
            m.SetFloat("_Flicker", flicker);
            m.SetFloat("_Cull", cull);
            m.SetFloat("_Spin", spin);
            return Save(name, m);
        }

        // Projected signage, each with a projector someone mounted for a reason. Cold cyan, the 5% of the frame. The
        // entrance is where it gathers, beside the one warm doorway; elsewhere the trades, the regulator and the berth.
        static void BuildHolograms(Transform g)
        {
            var steel = Mat("Dark steel");
            var led = Beacon("Status cyan", HoloCyan, 1.6f, 0, 1);

            // The shop's hours, projected out over the stairwell mouth.
            {
                var h = Group(g, "Holo - shop hours 24H");
                Box("Projector", h, new Vector3(.7f, 3.66f, -.12f), new Vector3(.22f, .08f, .16f), steel);
                Sphere("Projector lens", h, new Vector3(.7f, 3.62f, -.21f), .02f, led);
                Decal("Hours", h, new Vector3(.8f, 3.02f, -1.05f), new Vector3(-.3f, 0, -1f).normalized, 1.3f, .49f, Holo("Holo - shop hours", "holo_24h", 1.5f), 0f);
            }
            // The shop's turning arm on a post beside the stairs: what is repaired downstairs, at eye height.
            {
                var h = Group(g, "Holo - turning arm");
                float x = .55f, z = -3.3f, y = Ground(x, z);
                Box("Projector post", h, new Vector3(x, y + .5f, z), new Vector3(.16f, 1f, .16f), steel);
                Cyl("Projector head", h, new Vector3(x, y + 1.03f, z), .11f, .06f, steel);
                Sphere("Projector lens", h, new Vector3(x, y + 1.07f, z), .03f, led);
                Decal("Arm", h, new Vector3(x, y + 1.58f, z), Vector3.back, .48f, .96f, Holo("Holo - turning arm", "holo_arm", 1.4f, -1, .2f, .7f, 0), 0f);
            }
            // Blade signs projected out from the street facades, read down the street: the neighbourhood's trades.
            {
                var h = Group(g, "Holo - street blades");
                var blades = new[] { (x: -7.4f, dir: Vector3.back, tex: "blade_inn"), (x: 3.4f, dir: Vector3.back, tex: "blade_charge"),
                                     (x: -5.4f, dir: Vector3.forward, tex: "blade_recycle"), (x: 5.6f, dir: Vector3.forward, tex: "blade_clinic") };
                foreach (var b in blades)
                {
                    if (!WallHit(new Vector3(b.x, 4.7f, -6f), b.dir, out var hit)) continue;
                    var n = hit.normal; n.y = 0; n.Normalize();
                    var c = hit.point + n * .42f;
                    string label = b.tex.Substring(6);
                    var m = Holo("Holo - blade " + label, b.tex, 1.25f, -1, .3f);
                    Decal("Blade " + label + " - east face", h, c, Vector3.right, .34f, 1.36f, m, .006f);
                    Decal("Blade " + label + " - west face", h, c, Vector3.left, .34f, 1.36f, m, .006f);
                    Rod("Blade arm", h, hit.point + Vector3.up * .74f, c + n * .2f + Vector3.up * .74f, .012f, steel);
                    Box("Blade emitter", h, hit.point + n * .05f + Vector3.up * .74f, new Vector3(.12f, .06f, .1f), steel);
                }
            }
            // Port patrol frame over the junction camera: the regulator's view, shown to whoever passes under it.
            {
                var h = Group(g, "Holo - NSP patrol frame");
                float y = Ground(-15.6f, -2.2f);
                var c = new Vector3(-15.6f, y + 3.4f, -2.2f);
                var patrol = Holo("Holo - NSP patrol", "holo_patrol", 1.15f, -1, .35f);
                Decal("Frame facing east", h, c + new Vector3(.01f, 0, 0), new Vector3(.94f, 0, -.34f), 1.25f, .78f, patrol, 0f);
                Decal("Frame facing west", h, c - new Vector3(.01f, 0, 0), new Vector3(-.94f, 0, -.34f), 1.25f, .78f, patrol, 0f);
                Box("Emitter", h, new Vector3(-15.6f, y + 2.86f, -2.2f), new Vector3(.12f, .06f, .12f), steel);
            }
            // Berth 04's readout over the shore power pedestal, facing the approach along the quay.
            var ped = Renderers("Shore power pedestal").FirstOrDefault();
            if (ped != null)
            {
                var h = Group(g, "Holo - berth 04 shore power");
                var b = ped.bounds;
                Box("Emitter", h, new Vector3(b.center.x, b.max.y + .07f, b.center.z), new Vector3(.16f, .06f, .16f), steel);
                Sphere("Emitter lens", h, new Vector3(b.center.x, b.max.y + .1f, b.center.z), .02f, led);
                Decal("Readout", h, new Vector3(b.center.x, b.max.y + .76f, b.center.z), new Vector3(1f, 0, -.35f).normalized, 1.2f, .6f, Holo("Holo - berth 04", "holo_berth", 1.35f), 0f);
            }
            // The court's address beside the residents' door, and the locker's pickup count.
            {
                float wz = WallZ(-12.7f);
                Decal("Holo - court address", g, new Vector3(-12.7f, 3.12f, wz - .05f), Vector3.back, .96f, .3f, Holo("Holo - court address", "holo_address", 1.15f), 0f);
                float lz = WallZ(-19.3f), ly = Ground(-19.3f, lz - .3f);
                Decal("Holo - locker pickup", g, new Vector3(-19.3f, ly + 1.48f, lz - .58f), Vector3.back, .6f, .2f, Holo("Holo - locker pickup", "holo_locker", 1.05f), 0f);
            }
            // A port patrol drone holding over the quay mouth with its scan cone on the snow, and a second far over the basin.
            {
                var h = Group(g, "NSP patrol drone - quay mouth");
                var c = new Vector3(-18.6f, 6.6f, -4.2f);
                Box("Drone body", h, c, new Vector3(.5f, .12f, .3f), steel);
                foreach (var sx in new[] { -1, 1 })
                    foreach (var sz in new[] { -1, 1 })
                    {
                        var tip = c + new Vector3(sx * .36f, .03f, sz * .26f);
                        Rod("Drone arm", h, c, tip, .015f, steel);
                        Cyl("Rotor", h, tip + Vector3.up * .03f, .13f, .008f, steel);
                    }
                Sphere("Drone beacon", h, c + Vector3.down * .08f, .035f, Beacon("Status red", AlarmRed, 2.4f, .5f, .3f));
                Box("Drone eye", h, c + new Vector3(0, -.02f, -.16f), new Vector3(.16f, .03f, .01f), led);
                LampCone(h, "Drone scan cone", c + Vector3.down * .1f, .22f, Volume("Drone scan", HoloCyan, .06f));
                var far = Group(g, "NSP drone - far over the basin");
                var f = new Vector3(-34f, 13f, 8f);
                Box("Drone body", far, f, new Vector3(.6f, .14f, .36f), steel);
                Sphere("Drone beacon", far, f + Vector3.down * .1f, .07f, Beacon("Status red", AlarmRed, 2.4f, .5f, .3f));
                Sphere("Drone lamp", far, f + new Vector3(0, -.05f, -.2f), .05f, led);
            }
        }

        // The settled-snow relief came with short dunes along the stair shoulders and ruts; the ground should read as one
        // sheet of snow. A smoothed copy keeps the broad shoulders and ruts and half of the small relief, softens the slush
        // edges, and leaves its rim in place so it still meets kerbs and walls. The handed-over mesh is untouched; this
        // scene's renderer (and any collider) points at the copy. Runs before the build so every ground probe sees it.
        static void SmoothSnowpack()
        {
            var src = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/RepairProps/Scenes/Temp_SnowDepth/Packed snow.mesh.asset");
            var root = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(r => r.name == "Settled snow - stair shoulders and wheel ruts");
            if (src == null || root == null) { log.Append("snowpack: source mesh or root missing\n"); return; }
            var mesh = Object.Instantiate(src);
            mesh.name = "Packed snow - smoothed";
            var v = mesh.vertices; var nrm = mesh.normals; var col = mesh.colors; var tris = mesh.triangles;
            var weld = new Dictionary<Vector3Int, int>(); var id = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var k = Vector3Int.RoundToInt(v[i] * 1000);
                if (!weld.TryGetValue(k, out id[i])) { id[i] = weld.Count; weld[k] = id[i]; }
            }
            int n = weld.Count;
            var nb = new HashSet<int>[n]; for (int i = 0; i < n; i++) nb[i] = new HashSet<int>();
            var edges = new Dictionary<long, int>();
            for (int t = 0; t < tris.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = id[tris[t + e]], b = id[tris[t + (e + 1) % 3]];
                    if (a == b) continue;
                    nb[a].Add(b); nb[b].Add(a);
                    long key = (long)Mathf.Min(a, b) * n + Mathf.Max(a, b);
                    edges[key] = edges.TryGetValue(key, out var c) ? c + 1 : 1;
                }
            var fixedV = new bool[n];
            foreach (var kv in edges) if (kv.Value == 1) { fixedV[(int)(kv.Key / n)] = true; fixedV[(int)(kv.Key % n)] = true; }
            var h0 = new float[n]; var r0 = new float[n]; var top = new bool[n];
            for (int i = 0; i < v.Length; i++)
            {
                h0[id[i]] = v[i].y;
                r0[id[i]] = col.Length == v.Length ? col[i].r : 1;
                if (nrm.Length == v.Length && nrm[i].y > .5f) top[id[i]] = true;
            }
            for (int i = 0; i < n; i++) if (!top[i]) fixedV[i] = true;
            float[] Relax(float[] x, int passes)
            {
                var cur = (float[])x.Clone(); var next = new float[n];
                for (int p = 0; p < passes; p++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (fixedV[i] || nb[i].Count == 0) { next[i] = cur[i]; continue; }
                        float sum = 0; foreach (var j in nb[i]) sum += cur[j];
                        next[i] = cur[i] + .5f * (sum / nb[i].Count - cur[i]);
                    }
                    (cur, next) = (next, cur);
                }
                return cur;
            }
            var broad = Relax(h0, 30);
            var red = Relax(r0, 3);
            float moved = 0, maxMoved = 0;
            for (int i = 0; i < n; i++)
            {
                float hNew = broad[i] + (h0[i] - broad[i]) * .5f;
                moved += Mathf.Abs(hNew - h0[i]); maxMoved = Mathf.Max(maxMoved, Mathf.Abs(hNew - h0[i]));
                broad[i] = hNew;
            }
            // Feather the slab's edges: where it stops on flat ground as a low lip, the top ramps down to the foot of
            // its skirt over .6 m, so the snow runs out into the street instead of ending at a step. Drifts, shoulders and
            // edges over a real drop (stairwell, quay) are taller than that and keep their shape.
            var xz = new Vector2[n]; var dist = new float[n]; var foot = new float[n];
            for (int i = 0; i < v.Length; i++) xz[id[i]] = new Vector2(v[i].x, v[i].z);
            for (int i = 0; i < n; i++) { dist[i] = top[i] ? 1e9f : 0; foot[i] = h0[i]; }
            for (int pass = 0; pass < 4; pass++)                      // a skirt column's foot is its lowest vertex
                for (int i = 0; i < n; i++)
                    if (!top[i]) foreach (var j in nb[i]) if (!top[j] && (xz[i] - xz[j]).sqrMagnitude < .0025f) foot[i] = Mathf.Min(foot[i], foot[j]);
            for (int pass = 0; pass < 40; pass++)
                for (int i = 0; i < n; i++)
                    if (top[i]) foreach (var j in nb[i])
                    {
                        float d = dist[j] + Vector2.Distance(xz[i], xz[j]);
                        if (d < dist[i] - 1e-4f || (d < dist[i] + 1e-4f && foot[j] < foot[i])) { dist[i] = d; foot[i] = foot[j]; }
                    }
            int feathered = 0;
            for (int i = 0; i < n; i++)
            {
                if (!top[i] || dist[i] > .6f) continue;
                float drop = broad[i] - foot[i];
                float lip = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.1f, .2f, drop));
                if (lip <= 0 || drop <= .02f) continue;
                float f = Mathf.SmoothStep(0, 1, dist[i] / .6f);
                broad[i] = Mathf.Lerp(foot[i] + .015f, broad[i], Mathf.Lerp(1, f, lip));
                feathered++;
            }
            log.Append("snowpack edges feathered: ").Append(feathered).Append(" top vertices within .6 m of a lip under 20 cm\n");
            for (int i = 0; i < v.Length; i++)
            {
                v[i].y = broad[id[i]];
                if (col.Length == v.Length) col[i].r = red[id[i]];
            }
            mesh.vertices = v;
            if (col.Length == v.Length) mesh.colors = col;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, MeshDir + "/Packed snow - smoothed.asset");
            int pointed = 0;
            // On a re-run the previous copy was deleted with the Meshes folder, so a missing mesh is ours too.
            bool Ours(Mesh m) => m == null || m == src || m.name == mesh.name;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) if (Ours(mf.sharedMesh)) { mf.sharedMesh = mesh; pointed++; }
            foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true)) if (Ours(mc.sharedMesh)) { mc.sharedMesh = mesh; pointed++; }
            log.Append("snowpack smoothed: ").Append(v.Length).Append(" vertices (").Append(n).Append(" welded), mean shift ")
               .Append((moved / Mathf.Max(1, n) * 100).ToString("F1")).Append(" cm, max ").Append((maxMoved * 100).ToString("F1")).Append(" cm; references re-pointed ").Append(pointed).Append('\n');
        }

        // The skyline the haze leaves: corporate towers well past the district, dark masses with red aviation lamps, one
        // carrying the company's band. They sit at the ends of the street vistas and behind the harbour.
        static void BuildFarTowers(Transform g)
        {
            if (!mats.TryGetValue("Far tower mass", out var mass))
            {
                var m = new Material(Shader.Find("BorderRepair/Exterior/FarSilhouette"));
                m.SetColor("_Color", new Color(.045f, .065f, .075f));
                m.SetFloat("_Haze", .5f);
                m.SetTexture("_Windows", Tex("distant_windows"));
                m.SetTextureScale("_Windows", new Vector2(5, 24));
                m.SetColor("_WindowColor", new Color(.5f, .66f, .72f));
                m.SetFloat("_WindowGain", .3f);
                mass = Save("Far tower mass", m);
            }
            var aviation = Beacon("Aviation red - far", AlarmRed, 2.6f, .45f, .4f, .35f);
            var aviationSteady = Beacon("Aviation red steady - far", AlarmRed, 1.8f, 0, 1, .4f);
            var band = Holo("Holo - corporate band", "holo_corp_band", 2.2f, .45f, .15f);
            var towers = new[]
            {
                (name: "West vista", pos: new Vector2(-165, -15), w: 30f, d: 28f, h: 170f, face: Vector3.right),
                (name: "East vista", pos: new Vector2(150, -6), w: 26f, d: 26f, h: 150f, face: Vector3.left),
                (name: "North harbour", pos: new Vector2(-75, 135), w: 36f, d: 30f, h: 140f, face: Vector3.back),
                (name: "North-west", pos: new Vector2(-150, 95), w: 24f, d: 24f, h: 115f, face: Vector3.back),
            };
            foreach (var t in towers)
            {
                var tg = Group(g, "Far tower - " + t.name);
                var c = new Vector3(t.pos.x, t.h * .4f, t.pos.y);
                Prim(PrimitiveType.Cube, "Mass", tg, c, new Vector3(t.w, t.h * .8f, t.d), Quaternion.identity, mass, false);
                var top = new Vector3(t.pos.x, t.h * .8f + t.h * .1f, t.pos.y);
                Prim(PrimitiveType.Cube, "Setback", tg, top, new Vector3(t.w * .62f, t.h * .2f, t.d * .62f), Quaternion.identity, mass, false);
                Prim(PrimitiveType.Cube, "Mast", tg, new Vector3(t.pos.x, t.h + 6, t.pos.y), new Vector3(.8f, 12, .8f), Quaternion.identity, mass, false);
                Prim(PrimitiveType.Sphere, "Aviation lamp - mast", tg, new Vector3(t.pos.x, t.h + 12.8f, t.pos.y), Vector3.one * 3f, Quaternion.identity, aviation, false);
                foreach (var sx in new[] { -1, 1 })
                    foreach (var sz in new[] { -1, 1 })
                    {
                        Prim(PrimitiveType.Sphere, "Aviation lamp - roof", tg, new Vector3(t.pos.x + sx * t.w * .31f, t.h + 1f, t.pos.y + sz * t.d * .31f), Vector3.one * 2.2f, Quaternion.identity, aviationSteady, false);
                        Prim(PrimitiveType.Sphere, "Aviation lamp - mid", tg, new Vector3(t.pos.x + sx * t.w * .5f, t.h * .55f, t.pos.y + sz * t.d * .5f), Vector3.one * 1.8f, Quaternion.identity, aviationSteady, false);
                    }
                if (t.name == "West vista" || t.name == "North harbour")
                {
                    float half = (t.face.x != 0 ? t.d : t.w) * .5f + .4f;
                    float span = (t.face.x != 0 ? t.d : t.w) * .92f;
                    Decal("Corporate band", tg, new Vector3(t.pos.x, t.h * .62f, t.pos.y) + t.face * half, t.face, span, span / 5f, band, 0f);
                }
            }
        }
    }
}
