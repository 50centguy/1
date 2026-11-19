using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // Two playable-copy extras (the district scene itself is not touched):
    //   - FarFill: where a player standing in the street looks out between the district's blocks and the outer ring
    //     (175 m and more), the far view ran out into bare sky. A middle ring of low hazed blocks 85-165 m out fills it
    //     (one merged unlit mesh, kept off the arcology, the sightlines the skyline keeps clear and the east road), and
    //     two rings of thin mist (MistBand: one alpha draw each) stand at 70 and 150 m so the gaps read as haze. The
    //     district's fog is raised a fifth (in CaptureAtmosphere).
    //   - B1Fiddles: small things in the B1 to fiddle with (B1Fiddle), and a transistor radio on the sill cabinet.
    public static partial class ExteriorDistrictPass
    {
        const float FogLift = 1.2f;

        static void FarFill(Transform holder)
        {
            var sky = holder.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith("Skyline - outer city", StringComparison.Ordinal));
            var g = Group(sky, "Middle ring and mist - playable");
            var centre = SkylineCentre + PlayableOffset;
            // What of the district stands out there already (not the skyline, not the ground and other huge things).
            var taken = holder.GetComponentsInChildren<Renderer>(false)
                .Where(r => r.enabled && !r.transform.IsChildOf(sky) && !(r is ParticleSystemRenderer) && r.bounds.size.x < 70 && r.bounds.size.z < 70)
                .Select(r => r.bounds).ToList();
            var keepClear = new[] { new Vector2(-165, -15), new Vector2(150, -6), new Vector2(-75, 135), new Vector2(-150, 95), new Vector2(-189, 147), new Vector2(70, -185) }
                .Select(k => Mathf.Atan2(k.y - SkylineCentre.z, k.x - SkylineCentre.x) * Mathf.Rad2Deg).ToArray();
            var mesh = new Mesher();
            int placed = 0;
            const int candidates = 72;
            for (int i = 0; i < candidates; i++)
            {
                float h1 = Hash(new Vector3(i, 3.3f, 8.1f)), h2 = Hash(new Vector3(i, 7.7f, 1.9f)), h3 = Hash(new Vector3(i, 4.4f, 6.6f)), h4 = Hash(new Vector3(i, 1.2f, 9.4f));
                float deg = (i + (h1 - .5f) * .6f) * 360f / candidates;
                if (keepClear.Any(k => Mathf.Abs(Mathf.DeltaAngle(deg, k)) < 9)) continue;
                // The east road runs 125 m out from -70 to 50 degrees: behind it there, in front of it nowhere.
                bool road = deg >= 290 || deg <= 50;
                float r = road ? 142 + 22 * h2 : 85 + 55 * h2;
                float hgt = road ? 34 + 30 * h3 : 16 + 42 * Mathf.Pow(h3, 1.3f);
                float w = 10 + 12 * h4, d = 10 + 12 * h1;
                var p = centre + new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad), 0, Mathf.Sin(deg * Mathf.Deg2Rad)) * r;
                var foot = new Bounds(p + Vector3.up * hgt * .5f, new Vector3(w + 8, hgt, d + 8));
                if (taken.Any(b => b.Intersects(foot))) continue;
                mesh.Box(p + Vector3.up * (hgt * .5f - 2), new Vector3(w, hgt + 4, d));
                if (h2 < .35f && hgt > 30) mesh.Box(p + Vector3.up * (hgt + 3), new Vector3(w * .6f, 6, d * .6f));
                placed++;
            }
            MeshObj("Middle ring blocks", g, mesh.Save("Playable middle ring", g), RingMat("City ring - haze 52", .52f), Vector3.zero, Quaternion.identity, false).transform.localPosition = Vector3.zero;

            void Band(string name, float radius, float top, float density, float falloff)
            {
                var band = new Mesher();
                const int seg = 64;
                Vector3 At(float a, float y) => centre + new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
                for (int s = 0; s < seg; s++)
                {
                    float a = s * Mathf.PI * 2 / seg, b = (s + 1) * Mathf.PI * 2 / seg;
                    band.Quad(At(a, -3), At(b, -3), At(b, top), At(a, top), -(At(a, 0) - centre).normalized,
                        new Vector2(s / (float)seg, 0), new Vector2((s + 1) / (float)seg, 0), new Vector2((s + 1) / (float)seg, 1), new Vector2(s / (float)seg, 1));
                }
                var m = ShaderMat("Mist band - " + name, "BorderRepair/Exterior/MistBand", k => { k.SetFloat("_Density", density); k.SetFloat("_Falloff", falloff); k.SetColor("_Tint", new Color(1.05f, 1.06f, 1.08f)); });
                MeshObj("Mist - " + name, g, band.Save("Playable mist " + name, g), m, Vector3.zero, Quaternion.identity, false).transform.localPosition = Vector3.zero;
            }
            Band("near, 70 m", 70, 26, .32f, 1.8f);
            Band("far, 150 m", 150, 70, .5f, 1.4f);
            foreach (var t in g.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            log.Append("far fill: ").Append(placed).Append(" middle-ring blocks, mist rings at 70 and 150 m, fog x").Append(FogLift).Append('\n');
        }

        static void B1Fiddles(Transform holder)
        {
            var pass = holder.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith("09 Interior pass", StringComparison.Ordinal));
            var all = pass.GetComponentsInChildren<Transform>(true);
            Transform Named(string name) => all.FirstOrDefault(t => t.name == name && B1Room.Contains(BoundsOf(t).center));
            var root = new GameObject("B1 fiddles (generated)").transform;
            root.SetParent(holder, true);
            var added = new List<string>();

            B1Fiddle Add(B1FiddleKind kind, Transform item, Bounds box, Transform second = null, Light lamp = null, IEnumerable<Renderer> glows = null)
            {
                if (item == null) { log.Append("fiddle ").Append(kind).Append(": item missing\n"); return null; }
                var go = new GameObject("Fiddle - " + kind + " (" + item.name + ")");
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(box.center, Quaternion.identity);
                var c = go.AddComponent<BoxCollider>();
                var s = go.transform.lossyScale;
                c.size = new Vector3(box.size.x / s.x, box.size.y / s.y, box.size.z / s.z);
                var speaker = go.AddComponent<AudioSource>();
                speaker.playOnAwake = false; speaker.spatialBlend = 1; speaker.rolloffMode = AudioRolloffMode.Linear; speaker.minDistance = .6f; speaker.maxDistance = 9; speaker.dopplerLevel = 0;
                var f = go.AddComponent<B1Fiddle>();
                f.kind = kind; f.item = item; f.second = second; f.lamp = lamp; f.speaker = speaker;
                f.glows = (glows ?? Enumerable.Empty<Renderer>()).ToArray();
                foreach (var t in new[] { item, second }.Where(t => t != null).SelectMany(t => t.GetComponentsInChildren<Transform>(true)))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                foreach (var r in f.glows) GameObjectUtility.SetStaticEditorFlags(r.gameObject, 0);
                added.Add(kind.ToString());
                return f;
            }
            Bounds Grown(Bounds b, float by) { b.Expand(by * 2); return b; }

            var bulb = Named("Caged bulb inside the gate");
            if (bulb != null)
            {
                // Only the bulb end: the cord to the ceiling is too thin to aim at and would put the box in the ceiling.
                var glass = bulb.GetComponentsInChildren<Renderer>().Where(r => r.name == "Bulb").ToArray();
                Add(B1FiddleKind.CagedBulb, bulb, Grown(glass.Length > 0 ? glass[0].bounds : BoundsOf(bulb), .07f), null, bulb.GetComponentInChildren<Light>(), glass);
            }
            var festive = Named("Old festive bulbs over the counter");
            if (festive != null)
                Add(B1FiddleKind.FestiveBulbs, festive, Grown(BoundsOf(festive), .05f), null, null, festive.GetComponentsInChildren<Renderer>().Where(r => r.name == "Bulb"));
            var heater = Named("Electric radiator");
            if (heater != null)
                Add(B1FiddleKind.Heater, heater, Grown(BoundsOf(heater), .02f), null, null, heater.GetComponentsInChildren<Renderer>().Where(r => r.name == "Pilot lamp"));
            var water = Named("Water dispenser");
            if (water != null)
                Add(B1FiddleKind.WaterDispenser, water.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Tap cold"), Grown(BoundsOf(water), .02f),
                    water.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Bottle"));
            var paper = Named("Folded newspaper");
            if (paper != null) { var b = BoundsOf(paper); b.Expand(new Vector3(.04f, .08f, .04f)); Add(B1FiddleKind.Newspaper, paper, b); }
            var thermos = Named("Thermos"); var cup = Named("Thermos cup");
            if (thermos != null && cup != null)
            {
                var b = BoundsOf(thermos); b.Encapsulate(BoundsOf(cup)); b.Encapsulate(b.center + Vector3.right * .16f); b.Expand(.03f);
                Add(B1FiddleKind.Thermos, thermos, b, cup);
            }
            var radio = Radio(Named("Under the window - low cabinets"));
            if (radio != null) Add(B1FiddleKind.Radio, radio, Grown(BoundsOf(radio), .02f), null, null, radio.GetComponentsInChildren<Renderer>().Where(r => r.name == "Dial"));
            log.Append("B1 fiddles: ").Append(string.Join(", ", added)).Append('\n');
        }

        // A small transistor radio on the sill cabinet's worktop, in the widest free stretch between what stands there.
        static Transform Radio(Transform cabinets)
        {
            if (cabinets == null) return null;
            var worktop = cabinets.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "Worktop");
            if (worktop == null) return null;
            var top = worktop.bounds;
            var busy = cabinets.GetComponentsInChildren<Renderer>().Where(r => r != worktop && r.bounds.min.y > top.max.y - .02f)
                .Select(r => (lo: r.bounds.min.x - .05f, hi: r.bounds.max.x + .05f)).OrderBy(s => s.lo).ToList();
            float best = 0, at = top.center.x, from = top.min.x + .04f;
            foreach (var s in busy.Concat(new[] { (lo: top.max.x - .04f, hi: top.max.x) }))
            {
                if (s.lo - from > best) { best = s.lo - from; at = (s.lo + from) * .5f; }
                from = Mathf.Max(from, s.hi);
            }
            if (best < .32f) { log.Append("radio: no room on the worktop\n"); return null; }
            var g = new GameObject("Radio - transistor, on the sill cabinet").transform;
            g.SetParent(cabinets, true);
            const float w = .27f, h = .15f, d = .09f;
            var c = new Vector3(at, top.max.y + h * .5f + .002f, top.center.z + .02f);
            var face = c.z + d * .5f;   // the room is to +z
            Box("Body", g, c, new Vector3(w, h, d), Worn("Int - radio, brown bakelite", Hex("#4A3426"), .55f));
            Box("Grille", g, new Vector3(c.x - .06f, c.y - .005f, face + .002f), new Vector3(.12f, .11f, .004f), Lit("Int - radio grille", Hex("#1F1D1B"), .15f));
            for (int k = 0; k < 5; k++)
                Box("Grille slot", g, new Vector3(c.x - .06f, c.y - .045f + k * .02f, face + .0045f), new Vector3(.105f, .004f, .002f), Lit("Int - radio grille slot", Hex("#8B7A62"), .4f, .6f));
            Box("Dial", g, new Vector3(c.x + .07f, c.y + .035f, face + .002f), new Vector3(.1f, .032f, .004f), Lit("Int - radio dial", Hex("#C9A36A"), .7f, 0, null, new Color(1f, .68f, .32f) * 1.8f));
            Box("Dial needle", g, new Vector3(c.x + .055f, c.y + .035f, face + .0045f), new Vector3(.003f, .028f, .002f), Lit("Int - radio needle", Hex("#B03A2A"), .4f));
            var knob = Worn("Int - radio knob", Hex("#D8CFBF"), .5f);
            Prim(PrimitiveType.Cylinder, "Knob", g, new Vector3(c.x + .045f, c.y - .03f, face + .008f), new Vector3(.03f, .008f, .03f), Quaternion.Euler(90, 0, 0), knob, false);
            Prim(PrimitiveType.Cylinder, "Knob", g, new Vector3(c.x + .095f, c.y - .03f, face + .008f), new Vector3(.03f, .008f, .03f), Quaternion.Euler(90, 0, 0), knob, false);
            Box("Handle", g, new Vector3(c.x, c.y + h * .5f + .012f, c.z), new Vector3(.17f, .01f, .018f), Lit("Int - radio handle", Hex("#2A2826"), .4f));
            var tilt = Quaternion.Euler(0, 0, -24);
            Prim(PrimitiveType.Cylinder, "Antenna", g, new Vector3(c.x + w * .5f - .02f, c.y + h * .5f, c.z - .02f) + tilt * Vector3.up * .11f, new Vector3(.005f, .11f, .005f), tilt,
                Worn("Int - chrome, dull", Hex("#9A9C9A"), .7f, .9f), false);
            log.Append("radio at x ").Append(at.ToString("F2")).Append(" on the worktop (gap ").Append(best.ToString("F2")).Append(" m)\n");
            return g;
        }
    }
}
