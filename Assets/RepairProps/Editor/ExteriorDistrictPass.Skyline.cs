using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // The far view past the four corporate towers, the mid blocks and the west viaduct (all kept):
    //   - an outer ring of towers 175-255 m out, world-space window grids (CityBlock) with a fixed haze, a few with a
    //     vertical neon strip toward the district, masts and aviation lamps on the tall ones;
    //   - a terraced arcology to the north-west beyond the harbour, lit tier edges and a spire with a red lamp;
    //   - an elevated road arcing round the east 125 m out, head and tail lamps streaming on it (TrafficStream);
    //   - two sky searchlights from the arcology spire and one from a south-east pylon, swept by DistrictMotion;
    //   - the city's light on the snow cloud behind it all (HorizonGlow).
    // Everything is unlit, casts no shadows and is merged into a few meshes. Kept inside 300 m of the street, the far
    // plane of the review cameras. Built by the interior pass under its root, so a rerun rebuilds it.
    public static partial class ExteriorDistrictPass
    {
        static readonly Vector3 SkylineCentre = new Vector3(-9, 0, -4);
        static readonly List<DistrictMotion.Sweep> skylineSweeps = new List<DistrictMotion.Sweep>();

        static void FarSkyline(Transform g)
        {
            skylineSweeps.Clear();
            var keepClear = new[] { new Vector2(-165, -15), new Vector2(150, -6), new Vector2(-75, 135), new Vector2(-150, 95) };
            var arcology = new Vector3(-189, 0, 147);

            // Outer ring.
            var ringNear = new Mesher(); var ringFar = new Mesher();
            var neon = new[] { new Mesher(), new Mesher(), new Mesher() };
            var steadyLamps = new Mesher();
            var masts = new Mesher();
            var blinkTops = new List<Vector3>();
            int towers = 0;
            const int candidates = 64;
            for (int i = 0; i < candidates; i++)
            {
                float h1 = Hash(new Vector3(i, 1.7f, 3.1f)), h2 = Hash(new Vector3(i, 5.3f, .7f)), h3 = Hash(new Vector3(i, 9.1f, 2.2f)),
                      h4 = Hash(new Vector3(i, 2.9f, 7.7f)), h5 = Hash(new Vector3(i, 6.4f, 4.6f)), h6 = Hash(new Vector3(i, 8.2f, 1.3f));
                float theta = (i + (h1 - .5f) * .7f) * Mathf.PI * 2 / candidates;
                float r = 175 + 80 * h2;
                var p = SkylineCentre + new Vector3(Mathf.Cos(theta), 0, Mathf.Sin(theta)) * r;
                bool blocked = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(arcology.x, arcology.z)) < 85;
                foreach (var k in keepClear) blocked |= Vector2.Distance(new Vector2(p.x, p.z), k) < 42;
                if (blocked) continue;
                float west = Mathf.Clamp01(1 - Mathf.Abs(Mathf.DeltaAngle(theta * Mathf.Rad2Deg, 180)) / 60f);
                float hgt = (24 + 80 * Mathf.Pow(h3, 1.6f)) * (1 + .3f * west);
                float w = 12 + 16 * h4, d = 12 + 16 * h5;
                var ring = r < 215 ? ringNear : ringFar;
                ring.Box(p + Vector3.up * hgt * .5f, new Vector3(w, hgt, d));
                float top = hgt;
                if (hgt > 70)
                {
                    float sh = hgt * .2f;
                    ring.Box(p + Vector3.up * (hgt + sh * .5f), new Vector3(w * .65f, sh, d * .65f));
                    top += sh;
                }
                if (h6 < .4f || hgt > 95)
                {
                    float mh = 8 + 10 * h1;
                    masts.Box(p + Vector3.up * (top + mh * .5f), new Vector3(.9f, mh, .9f));
                    var lamp = p + Vector3.up * (top + mh + .8f);
                    if (hgt > 80) blinkTops.Add(lamp); else steadyLamps.Box(lamp, Vector3.one * 1.8f);
                }
                if (h4 < .3f)
                {
                    // A vertical neon strip on the face that looks back at the district.
                    var toward = SkylineCentre - p;
                    var n = Mathf.Abs(toward.x) > Mathf.Abs(toward.z) ? new Vector3(Mathf.Sign(toward.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(toward.z));
                    float halfW = n.x != 0 ? w * .5f : d * .5f, across = (n.x != 0 ? d : w) * (h5 - .5f) * .6f;
                    var side = n.x != 0 ? Vector3.forward : Vector3.right;
                    var c = p + n * (halfW + .35f) + side * across + Vector3.up * hgt * .58f;
                    neon[(int)(h6 * 2.99f)].Box(c, n.x != 0 ? new Vector3(.6f, hgt * .42f, 1.4f) : new Vector3(1.4f, hgt * .42f, .6f));
                }
                towers++;
            }
            MeshObj("Ring towers - near haze", g, ringNear.Save("Skyline ring near", g), RingMat("City ring - haze 64", .64f), Vector3.zero, Quaternion.identity, false).transform.localPosition = Vector3.zero;
            MeshObj("Ring towers - far haze", g, ringFar.Save("Skyline ring far", g), RingMat("City ring - haze 75", .75f), Vector3.zero, Quaternion.identity, false).transform.localPosition = Vector3.zero;
            var mastMat = FarMass("Skyline mast", .6f);
            MeshObj("Ring masts", g, masts.Save("Skyline masts", g), mastMat, Vector3.zero, Quaternion.identity, false).transform.localPosition = Vector3.zero;
            var neonCols = new[] { new Color(1f, .24f, .62f), HoloCyan, new Color(1f, .55f, .3f) };
            var neonNames = new[] { "magenta", "cyan", "amber" };
            for (int k = 0; k < 3; k++)
                if (neon[k].Count > 0)
                    MeshObj("Ring neon - " + neonNames[k], g, neon[k].Save("Skyline neon " + neonNames[k], g), Beacon("Skyline neon - " + neonNames[k], neonCols[k], 2.4f, 0, 1, .5f), Vector3.zero, Quaternion.identity, false).transform.localPosition = Vector3.zero;
            MeshObj("Ring aviation lamps - steady", g, steadyLamps.Save("Skyline steady lamps", g), Beacon("Aviation red steady - far", AlarmRed, 1.8f, 0, 1, .4f), Vector3.zero, Quaternion.identity, false).transform.localPosition = Vector3.zero;
            var blink = Beacon("Aviation red - far", AlarmRed, 2.6f, .45f, .4f, .35f);
            foreach (var b in blinkTops) Prim(PrimitiveType.Sphere, "Ring aviation lamp", g, b, Vector3.one * 2.4f, Quaternion.identity, blink, false);

            // Arcology.
            var ag = Group(g, "Arcology - north-west beyond the harbour");
            var mass = ArcologyMass();
            var tiers = new[] { (w: 100f, h: 38f, d: 70f), (w: 74f, h: 34f, d: 52f), (w: 50f, h: 30f, d: 34f), (w: 28f, h: 26f, d: 20f) };
            var edges = new Mesher();
            float y0 = 0;
            foreach (var t in tiers)
            {
                var c = arcology + Vector3.up * (y0 + t.h * .5f);
                var tier = Prim(PrimitiveType.Cube, "Tier", ag, c, new Vector3(t.w, t.h, t.d), Quaternion.identity, mass, false);
                // Lit edge along the top of the two faces the district sees (+x, -z).
                edges.Box(arcology + new Vector3(t.w * .5f + .4f, y0 + t.h - .5f, 0), new Vector3(.8f, .8f, t.d));
                edges.Box(arcology + new Vector3(0, y0 + t.h - .5f, -t.d * .5f - .4f), new Vector3(t.w, .8f, .8f));
                edges.Box(arcology + new Vector3(t.w * .5f + .4f, y0 + t.h * .45f, 0), new Vector3(.6f, .5f, t.d * .9f));
                y0 += t.h;
            }
            Prim(PrimitiveType.Cube, "Spire", ag, arcology + Vector3.up * (y0 + 16), new Vector3(5, 32, 5), Quaternion.identity, mass, false);
            var spireTop = arcology + Vector3.up * (y0 + 33);
            Prim(PrimitiveType.Sphere, "Spire aviation lamp", ag, spireTop, Vector3.one * 4f, Quaternion.identity, blink, false);
            MeshObj("Tier edge lights", ag, edges.Save("Arcology edges", ag), Beacon("Arcology edge light", new Color(1f, .82f, .6f), 2.2f, 0, 1, .48f), Vector3.zero, Quaternion.identity, false).transform.localPosition = Vector3.zero;

            // Elevated road round the east, with traffic.
            var rg = Group(g, "Elevated road - east, with traffic");
            var deckMesh = new Mesher();
            var headLane = new Mesher(); var tailLane = new Mesher();
            const float roadR = 125, deck = 30, half = 4.5f, thick = 1.8f, cell = 7f;
            const float a0 = -70 * Mathf.Deg2Rad, a1 = 50 * Mathf.Deg2Rad;
            const int steps = 60;
            Vector3 At(float a, float rad, float y) => SkylineCentre + new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad);
            for (int s = 0; s < steps; s++)
            {
                float a = Mathf.Lerp(a0, a1, s / (float)steps), b = Mathf.Lerp(a0, a1, (s + 1) / (float)steps);
                float ua = roadR * (a - a0) / cell, ub = roadR * (b - a0) / cell;
                var mid = (At(a, roadR, 0) + At(b, roadR, 0)) * .5f - SkylineCentre; mid.y = 0; mid.Normalize();
                deckMesh.Quad(At(a, roadR - half, deck), At(b, roadR - half, deck), At(b, roadR + half, deck), At(a, roadR + half, deck), Vector3.up);
                deckMesh.Quad(At(a, roadR - half, deck - thick), At(b, roadR - half, deck - thick), At(b, roadR + half, deck - thick), At(a, roadR + half, deck - thick), Vector3.down);
                deckMesh.Quad(At(a, roadR - half, deck - thick), At(b, roadR - half, deck - thick), At(b, roadR - half, deck + .9f), At(a, roadR - half, deck + .9f), -mid);
                deckMesh.Quad(At(a, roadR + half, deck - thick), At(b, roadR + half, deck - thick), At(b, roadR + half, deck + .9f), At(a, roadR + half, deck + .9f), mid);
                headLane.Quad(At(a, roadR - half - .05f, deck + 1.0f), At(b, roadR - half - .05f, deck + 1.0f), At(b, roadR - half - .05f, deck + 1.6f), At(a, roadR - half - .05f, deck + 1.6f), -mid,
                    new Vector2(ua, 0), new Vector2(ub, 0), new Vector2(ub, 1), new Vector2(ua, 1));
                tailLane.Quad(At(a, roadR - half - .05f, deck + 1.75f), At(b, roadR - half - .05f, deck + 1.75f), At(b, roadR - half - .05f, deck + 2.3f), At(a, roadR - half - .05f, deck + 2.3f), -mid,
                    new Vector2(ua, 0), new Vector2(ub, 0), new Vector2(ub, 1), new Vector2(ua, 1));
                if (s % 5 == 0) deckMesh.Box(At(a, roadR, (deck - thick) * .5f), new Vector3(3, deck - thick, 3));
            }
            MeshObj("Road deck and piers", rg, deckMesh.Save("Skyline road deck", rg), FarMass("Road deck mass", .5f), Vector3.zero, Quaternion.identity, false).transform.localPosition = Vector3.zero;
            MeshObj("Traffic - head lamps", rg, headLane.Save("Skyline traffic head", rg), Traffic("Traffic - head lamps", new Color(1f, .9f, .72f), 2.6f, 3.4f, 0), Vector3.zero, Quaternion.identity, false).transform.localPosition = Vector3.zero;
            MeshObj("Traffic - tail lamps", rg, tailLane.Save("Skyline traffic tail", rg), Traffic("Traffic - tail lamps", new Color(1f, .16f, .1f), 2.2f, -3.1f, 7), Vector3.zero, Quaternion.identity, false).transform.localPosition = Vector3.zero;
            // Road lamps along the deck edge.
            var roadLamps = new Mesher();
            for (int s = 0; s <= steps; s += 2) roadLamps.Box(At(Mathf.Lerp(a0, a1, s / (float)steps), roadR - half, deck + 4.2f), Vector3.one * .9f);
            MeshObj("Road lamps", rg, roadLamps.Save("Skyline road lamps", rg), Beacon("Road lamp - far sodium", new Color(1f, .62f, .3f), 2f, 0, 1, .45f), Vector3.zero, Quaternion.identity, false).transform.localPosition = Vector3.zero;

            // Sky searchlights.
            var sg = Group(g, "Sky searchlights");
            var beamMat = ShaderMat("Sky searchlight beam", "BorderRepair/Exterior/SkyBeam", m => { m.SetColor("_Color", new Color(.72f, .82f, .95f)); m.SetFloat("_Intensity", .16f); m.SetFloat("_Haze", .35f); });
            var pylon = new Vector3(70, 0, -185);
            Prim(PrimitiveType.Cube, "Searchlight pylon", sg, pylon + Vector3.up * 26, new Vector3(4, 52, 4), Quaternion.identity, mastMat, false);
            SkySearchlight(sg, "Searchlight - arcology spire west", spireTop + Vector3.down * 3 + Vector3.left * 2, beamMat, 26, .045f, 0);
            SkySearchlight(sg, "Searchlight - arcology spire east", spireTop + Vector3.down * 3 + Vector3.right * 2, beamMat, 22, .037f, .4f);
            SkySearchlight(sg, "Searchlight - south-east pylon", pylon + Vector3.up * 53, beamMat, 30, .03f, .7f);

            // City glow on the cloud.
            var glow = new Mesher();
            const int seg = 72; const float glowR = 262, gy0 = -5, gy1 = 140;
            for (int s = 0; s < seg; s++)
            {
                float a = s * Mathf.PI * 2 / seg, b = (s + 1) * Mathf.PI * 2 / seg;
                glow.Quad(At(a, glowR, gy0), At(b, glowR, gy0), At(b, glowR, gy1), At(a, glowR, gy1), -(At(a, glowR, 0) - SkylineCentre).normalized,
                    new Vector2(s / (float)seg, 0), new Vector2((s + 1) / (float)seg, 0), new Vector2((s + 1) / (float)seg, 1), new Vector2(s / (float)seg, 1));
            }
            MeshObj("Horizon glow", g, glow.Save("Skyline horizon glow", g),
                ShaderMat("Horizon glow - city on the cloud", "BorderRepair/Exterior/HorizonGlow", m => { m.SetColor("_Low", new Color(.42f, .27f, .16f)); m.SetColor("_High", new Color(.1f, .19f, .23f)); m.SetFloat("_Intensity", .32f); m.SetFloat("_Lobes", 3); }),
                Vector3.zero, Quaternion.identity, false).transform.localPosition = Vector3.zero;

            // The searchlights join the street motion.
            var motion = Object.FindFirstObjectByType<DistrictMotion>();
            if (motion != null)
            {
                motion.sweeps = motion.sweeps.Concat(skylineSweeps).ToArray();
                motion.Evaluate(0);
            }
            log.Append("skyline: ").Append(towers).Append(" ring towers (").Append(blinkTops.Count).Append(" blinking tops), arcology, east road with traffic, ")
               .Append(skylineSweeps.Count).Append(" sky searchlights, horizon glow\n");
        }

        // The mount faces the district (its +Z), the pivot leans the beam up and toward it; DistrictMotion swings it
        // side to side about the mount's Z.
        static void SkySearchlight(Transform g, string name, Vector3 at, Material beam, float side, float rate, float phase)
        {
            var toward = SkylineCentre - at; toward.y = 0;
            var mount = new GameObject(name).transform;
            mount.SetParent(g, false);
            mount.SetPositionAndRotation(at, Quaternion.LookRotation(toward.normalized, Vector3.up));
            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(mount, false);
            var rest = Quaternion.Euler(-70, 0, 0);
            pivot.localRotation = rest;
            Beam("Beam", pivot, 230, 1f, 15f, beam);
            skylineSweeps.Add(new DistrictMotion.Sweep { pivot = pivot, rest = rest, side = side, pitch = 7, rate = rate, phase = phase });
        }

        // The mid blocks' material, further back: more haze, fewer and dimmer lit windows.
        static Material RingMat(string name, float haze)
        {
            var m = CityMat(name, haze);
            m.SetFloat("_Lit", .05f);
            m.SetFloat("_Gain", .8f);
            UnityEditor.EditorUtility.SetDirty(m);
            return m;
        }

        static Material ArcologyMass()
        {
            if (mats.TryGetValue("Arcology mass", out var have)) return have;
            var m = new Material(Shader.Find("BorderRepair/Exterior/FarSilhouette"));
            m.SetColor("_Color", new Color(.04f, .058f, .068f));
            m.SetFloat("_Haze", .6f);
            m.SetTexture("_Windows", Tex("distant_windows"));
            m.SetTextureScale("_Windows", new Vector2(9, 14));
            m.SetColor("_WindowColor", new Color(.62f, .62f, .55f));
            m.SetFloat("_WindowGain", .32f);
            return Save("Arcology mass", m);
        }

        static Material Traffic(string name, Color colour, float intensity, float speed, float seed) =>
            ShaderMat(name, "BorderRepair/Exterior/TrafficStream", m =>
            {
                m.SetColor("_Color", colour); m.SetFloat("_Intensity", intensity); m.SetFloat("_Speed", speed);
                m.SetFloat("_Duty", .32f); m.SetFloat("_Density", .55f); m.SetFloat("_Seed", seed); m.SetFloat("_Haze", .42f);
            });

        static Material ShaderMat(string name, string shader, System.Action<Material> set)
        {
            if (mats.TryGetValue(name, out var have)) return have;
            var m = new Material(Shader.Find(shader));
            set(m);
            return Save(name, m);
        }

        // Collects quads and boxes in world space (each face its own vertices, so normals stay flat) and saves them as
        // one mesh in the local space of the group it will hang under.
        sealed class Mesher
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> t = new List<int>();
            public int Count => v.Count;

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Vector2? ua = null, Vector2? ub = null, Vector2? uc = null, Vector2? ud = null)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(ua ?? new Vector2(0, 0)); uv.Add(ub ?? new Vector2(1, 0)); uv.Add(uc ?? new Vector2(1, 1)); uv.Add(ud ?? new Vector2(0, 1));
                Tri(t, v, i, i + 1, i + 2, outward); Tri(t, v, i, i + 2, i + 3, outward);
            }

            // Five faces (no bottom).
            public void Box(Vector3 c, Vector3 size)
            {
                var h = size * .5f;
                Vector3 P(float x, float y, float z) => c + new Vector3(x * h.x, y * h.y, z * h.z);
                Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Vector3.up);
                Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), Vector3.back);
                Quad(P(1, -1, 1), P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), Vector3.forward);
                Quad(P(-1, -1, 1), P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), Vector3.left);
                Quad(P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), Vector3.right);
            }

            public Mesh Save(string name, Transform space)
            {
                var local = new List<Vector3>(v.Count);
                foreach (var p in v) local.Add(space.InverseTransformPoint(p));
                return SaveMesh(name, local, t, uv);
            }
        }
    }
}
