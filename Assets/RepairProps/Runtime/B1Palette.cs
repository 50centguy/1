using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps
{
    // The B1's palette for the clinic runtimes that light and dress the room at play time (Docs/Art/PALETTE_BR2049_PS2.md,
    // Docs/Art/INTERIOR_B1_PASS.md section 25). Only while the B1 layout is active; the desktop clinic keeps its own look.
    //   - Cold outside, warm inside (the palette's first rule): the street keeps the cold fog; the room is the warm one
    //     (second round: the mint tubes and the fog-teal ambient read too cold). The ambient
    //     is a neutral, faintly warm grey at the old brightness, the fill is grey, and every fluorescent tube is one
    //     warm-white tube colour (the district's battens).
    //   - Amber only where a lamp hangs (counter, gate, bench). The counter's orange-red is amber and half as strong.
    //   - The robot dock's lamps were service teal against the bench amber next to them: now plain tube white.
    //   - Holograms holo cyan, screens CRT green, red only as a warning, no magenta. No glowing strips: the surgery ring's
    //     neon tube goes, its indicator specks stay, dimmed; the curtain loses its glowing cyan rim.
    public static class B1Palette
    {
        /// <summary>Lift for the district's grade: the colour wheel pushed toward cold slate #1E2A30 (30, 42, 48) and a small
        /// offset, so the darkest walls sit at a dim slate instead of black.</summary>
        public static readonly Vector4 ShadowLift = new Vector4(.985f, 1f, 1.012f, .002f);
        public static bool Active => ClinicLayout.Active != null;

        public static readonly Color Tube = new Color(1f, .945f, .863f);           // #FFF1DC warm white (mint read too cold)
        public static readonly Color CoolFill = new Color(.66f, .64f, .60f);        // neutral grey fill
        public static readonly Color Lamp = new Color(1f, .72f, .42f);              // tungsten: the #E89A3C family as light
        public static readonly Color Amber = new Color(.91f, .60f, .235f);          // #E89A3C
        public static readonly Color HoloCyan = new Color(.42f, .88f, .91f);        // #6BE0E8
        public static readonly Color HoloPale = new Color(.82f, .96f, .97f);
        public static readonly Color WarnRed = new Color(.79f, .23f, .23f);         // #C93B3B
        // A neutral, faintly warm grey at the old (blue) ambient's luminance (the street fog's teal read too cold).
        public static readonly Color AmbientSky = new Color(.148f, .14f, .126f), AmbientEquator = new Color(.098f, .093f, .084f), AmbientGround = new Color(.05f, .047f, .043f);

        public static Color Zone(string light) => light.StartsWith("Dock_", StringComparison.Ordinal) ? Tube : Lamp;

        public static float PracticalScale(string light) =>
            light.StartsWith("Dock_", StringComparison.Ordinal) ? .8f : light.StartsWith("ClinicTrade", StringComparison.Ordinal) ? .5f : 1.15f;

        // The surgery kit is a fresh instance each run: its renderers take private copies, destroyed with the runtime.
        public static void QuietSurgeryKit(GameObject kit, List<Object> created)
        {
            var copies = new Dictionary<Material, Material>();
            foreach (var r in kit.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("CY_RingNeon", StringComparison.Ordinal)) { r.enabled = false; continue; }
                r.sharedMaterials = r.sharedMaterials.Select(m => m == null ? null : Quiet(m, copies, created)).ToArray();
            }
        }

        // Scene renderers (the bench): copies too, and the originals go back on destroy.
        public static void QuietBench(Scene scene, List<(Renderer renderer, Material[] shared)> swapped, List<Object> created)
        {
            var copies = new Dictionary<Material, Material>();
            foreach (var r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)))
            {
                var shared = r.sharedMaterials;
                if (!shared.Any(m => m != null && (m.name == "M_BV2_NeonPink" || m.name == "M_BV2_Holo"))) continue;
                swapped.Add((r, shared));
                r.sharedMaterials = shared.Select(m => m == null ? null : Quiet(m, copies, created)).ToArray();
            }
        }

        static Material Quiet(Material m, Dictionary<Material, Material> copies, List<Object> created)
        {
            if (copies.TryGetValue(m, out var have)) return have;
            Material Copy() { var c = new Material(m) { name = m.name + " (B1 palette)" }; created.Add(c); return c; }
            Material copy = null;
            if (m.name.StartsWith("M_Neon_Red", StringComparison.Ordinal)) { copy = Copy(); copy.SetColor("_Color", WarnRed * 1.8f); }
            else if (m.name.StartsWith("M_Neon_Amber", StringComparison.Ordinal)) { copy = Copy(); copy.SetColor("_Color", Amber * 1.2f); }
            else if (m.name.StartsWith("M_Curtain", StringComparison.Ordinal))
            {
                copy = Copy();
                copy.SetColor("_Color", new Color(.60f, .70f, .74f, m.GetColor("_Color").a));
                copy.SetColor("_RimColor", new Color(.62f, .68f, .70f) * .45f);
            }
            else if (m.name == "M_BV2_NeonPink") { copy = Copy(); Tint(copy, WarnRed, WarnRed * 1.8f); }
            else if (m.name == "M_BV2_Holo") { copy = Copy(); Tint(copy, HoloCyan, HoloCyan * 1.5f); }
            copies[m] = copy ?? m;
            return copies[m];
        }

        static void Tint(Material m, Color albedo, Color emission)
        {
            foreach (var p in new[] { "_BaseColor", "_Color" }) if (m.HasProperty(p)) m.SetColor(p, albedo);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission);
        }
    }
}
