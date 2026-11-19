using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // The street entrance, after the concept sheet the user picked (a roof over the stairs with the lit sign on it, B1 plate, folding
    // gates, a display case by the door, rusted pipes, a shutter next door, barriers, a pole and its wires). Only the ten-odd metres
    // round the stairwell; the road, the far side and the sky are the exterior pass's. Built by the interior pass under its
    // root (the window cut is there too), so one re-run rebuilds both. Handed-over objects it replaces are hidden by
    // name near the entrance (they keep their colliders) and rebuilt here under new names, so the exterior pass never
    // finds the copies when it looks things up by name.
    public static partial class ExteriorDistrictPass
    {
        static readonly Bounds EntranceArea = new Bounds(new Vector3(-1.5f, 1.6f, -2.9f), new Vector3(4.6f, 4f, 6.6f));

        static void StreetEntrance(Transform g)
        {
            Canopy(Group(g, "Covered stair - roof and lit sign"));
            Doorway(Group(g, "Doorway - B1 plate and the second folded gate"));
            DisplayCase(Group(g, "Display case beside the door"));
            StairRailsAsHandedOver();
            KerbPaint(Group(g, "Kerb - hazard paint"));
            FacadePipes(Group(g, "Facade - rusted down pipes"));
            Shutter(Group(g, "Next door - rolling shutter"));
            Barriers(Group(g, "Kerb - concrete barriers"));
            UtilityPole(Group(g, "Utility pole and the wires over the street"));
            EntranceCameras(Group(g, "Review cameras - entrance"));
        }

        static List<MeshRenderer> HideNamed(Bounds area, params string[] names)
        {
            var list = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r => names.Contains(r.name) && area.Contains(r.transform.position)).OrderBy(r => r.name).ThenBy(r => r.transform.position.x).ThenBy(r => r.transform.position.z).ToList();
            foreach (var r in list) r.enabled = false;
            log.Append("entrance hid ").Append(list.Count).Append(": ").Append(string.Join(", ", list.Select(r => r.name).Distinct())).Append('\n');
            return list;
        }

        static Material ExistingSnow()
        {
            var cap = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(r => r.name == "Snow cap");
            return cap != null ? cap.sharedMaterial : SnowMaterial();
        }

        static Material EntSteel => Worn("Ent - dark steel", Hex("#2D3133"), .4f, .5f);
        static Material EntIce => Lit("Ent - icicle ice", new Color(.74f, .82f, .88f, 1), .85f);

        // ------------------------------------------------------------------------------------------------ awning
        // A covered stair, the way basement shops in snowy towns keep their steps usable: a corrugated-steel roof over the
        // whole stairwell, from the facade to the stairhead. It falls gently toward the wall (a gutter along the wall), so
        // the street end is the high one: 2.2 m clear over the sidewalk where people step in. Two pairs of posts stand on
        // the stair cheeks just outside the guard rails. The shop's lightbox (the same 义体维修 face the old one over the
        // stairs had) stands on the roof's street end, facing the street; two fluorescent battens under the roof light the
        // steps. The old lightbox, its icicles and the enamel fascia with its snow (now under the roof) go. (Tried first: a
        // concrete canopy with the sign on its edge, then a short awning over the foot of the stairs only.)
        static void Canopy(Transform g)
        {
            HideNamed(EntranceArea, "Weathered repair lightbox", "Icicles - lightbox", "Snow on fascia", "Repair shop enamel fascia");
            var steel = EntSteel;
            var sheet = Worn("Ent - awning, corrugated steel", Hex("#4A4F4C"), .35f, .5f);
            float x0 = -2.84f, x1 = -.16f, cx = (x0 + x1) * .5f, w = x1 - x0, zw = -.05f, zf = -4.75f, yw = 3.5f, yf = 3.66f;
            float run = zw - zf, rise = yf - yw, len = Mathf.Sqrt(run * run + rise * rise);
            float RoofY(float z) => yw + rise * ((zw - z) / run);   // top of the sheet
            var rot = Quaternion.Euler(Mathf.Atan2(rise, run) * Mathf.Rad2Deg, 0, 0);   // the street end up
            var up = rot * Vector3.up;
            var mid = new Vector3(cx, (yw + yf) * .5f, (zw + zf) * .5f);
            BBox("Roof sheet", g, mid, new Vector3(w, .03f, len), sheet, rot);
            for (int i = 0; i < 14; i++)
                Box("Roof rib", g, mid + up * .025f + Vector3.right * (x0 + .1f + i * (w - .2f) / 13f - cx), new Vector3(.035f, .02f, len), sheet, rot);
            foreach (float rx in new[] { x0 + .1f, x1 - .1f })
                BBox("Roof rafter", g, mid - up * .055f + Vector3.right * (rx - cx), new Vector3(.06f, .08f, len), steel, rot);
            BBox("Roof wall ledger", g, new Vector3(cx, yw - .07f, zw - .03f), new Vector3(w, .1f, .06f), steel);
            BBox("Roof gutter", g, new Vector3(cx, yw - .02f, zw - .12f), new Vector3(w + .04f, .08f, .1f), Worn("Ent - pipe, rusted iron", Hex("#5B3A28"), .3f, .45f));
            foreach (float pz in new[] { -1.7f, -4.55f })
            {
                float yb = RoofY(pz) - .1f;
                BBox("Roof beam", g, new Vector3(cx, yb - .05f, pz), new Vector3(w + .02f, .1f, .07f), steel);
                foreach (float px in new[] { -2.68f, -.27f })
                {
                    const float ybase = 1.475f;
                    BBox("Roof post", g, new Vector3(px, (ybase + yb - .1f) * .5f, pz), new Vector3(.06f, yb - .1f - ybase, .06f), steel);
                    BBox("Roof post base plate", g, new Vector3(px, ybase + .006f, pz), new Vector3(.14f, .012f, .14f), steel);
                }
            }
            Box("Snow cap", g, mid + up * .05f, new Vector3(w - .08f, .045f, len - .1f), ExistingSnow(), rot);
            var ice = EntIce;
            Icicles(g, "Icicles - roof, street end", new Vector3(x0 + .08f, yf - .03f, zf - .01f), new Vector3(x1 - .08f, yf - .03f, zf - .01f), 12, .2f, ice, 31);
            Icicles(g, "Icicles - roof, west side", new Vector3(x0 - .005f, RoofY(-.5f) - .03f, -.5f), new Vector3(x0 - .005f, RoofY(-4.4f) - .03f, -4.4f), 16, .2f, ice, 33);
            Icicles(g, "Icicles - roof, east side", new Vector3(x1 + .005f, RoofY(-.5f) - .03f, -.5f), new Vector3(x1 + .005f, RoofY(-4.4f) - .03f, -4.4f), 16, .2f, ice, 35);

            // The lightbox on the street end, on two short legs, facing the street.
            // The street's signage is Japanese: a 義体修理 face in the style of the hand-painted original (which is left as is).
            var signTex = Tex("ent_lightbox_jp");
            var face = Lit("Ent - lit sign face", Color.white, .3f, 0, signTex, new Color(1f, .8f, .58f) * 1.3f, signTex);
            float sw = 2.2f, sh = .55f, sd = .2f, sz = zf + .16f, sy = RoofY(sz) + .1f + sh * .5f;
            BBox("Lightbox case", g, new Vector3(cx, sy, sz), new Vector3(sw + .08f, sh + .06f, sd), steel);
            Decal("Lightbox face", g, new Vector3(cx, sy, sz - sd * .5f), Vector3.back, sw, sh, face, .004f);
            foreach (float fx in new[] { cx - sw * .38f, cx + sw * .38f })
                BBox("Lightbox leg", g, new Vector3(fx, RoofY(sz) + .05f, sz), new Vector3(.06f, .1f, .06f), steel);
            Box("Snow cap", g, new Vector3(cx, sy + sh * .5f + .05f, sz), new Vector3(sw + .1f, .04f, sd + .02f), ExistingSnow());
            AddLight(g, "Lightbox glow", new Vector3(cx, sy, sz - .8f), LightType.Point, new Color(1f, .8f, .58f), 1.4f, 3.2f, false);

            // Two battens under the roof, over the foot and over the middle of the stairs. No shadows (more than five shadow
            // maps in one view break the additional-light atlas here; see the pass notes). The lower one is aimed down the
            // stairs; its cone stops about half a metre inside the gate, as far as the light would get through the opening.
            var tube = Lit("Ent - fluorescent tube", Color.white, .5f, 0, null, new Color(1f, .93f, .8f) * 2.2f);
            foreach (var (bz, name, aim, intensity) in new[] { (-1.0f, "Canopy batten", new Vector3(0, -1f, -.5f), 7f), (-3.3f, "Canopy batten, upper", new Vector3(0, -1f, .15f), 5f) })
            {
                float by = RoofY(bz) - .1f;
                BBox("Batten housing", g, new Vector3(cx, by - .025f, bz), new Vector3(1.3f, .05f, .1f), steel);
                Cyl("Batten tube", g, new Vector3(cx, by - .065f, bz), .014f, 1.2f, tube, Quaternion.Euler(0, 0, 90));
                var l = AddLight(g, name, new Vector3(cx, by - .12f, bz), LightType.Spot, new Color(1f, .9f, .76f), intensity, 6.5f, false);
                l.transform.rotation = Quaternion.LookRotation(aim);
                l.spotAngle = 110; l.innerSpotAngle = 50;
            }
        }

        // ------------------------------------------------------------------------------------------------ display case
        // A lit glass case on the wall left of the gate, at chest height on the landing, seen on the way down the stairs:
        // where the clinic shows a repaired unit. Empty plinth and an anchor for the model the user makes (no character is
        // placed here); the case can be resized to the model.
        static void DisplayCase(Transform g)
        {
            var steel = EntSteel;
            float x0 = -2.86f, x1 = -2.33f, y0 = .9f, y1 = 1.72f, zb = -.21f, zf = -.51f, cx = (x0 + x1) * .5f, cy = (y0 + y1) * .5f, cz = (zb + zf) * .5f;
            float w = x1 - x0, h = y1 - y0, d = zb - zf, t = .03f;
            BBox("Case back", g, new Vector3(cx, cy, zb - t * .5f), new Vector3(w, h, t), Lit("Ent - case back, dark felt", Hex("#1E2221"), .1f));
            BBox("Case top", g, new Vector3(cx, y1 - t * .5f, cz), new Vector3(w, t, d), steel);
            BBox("Case bottom", g, new Vector3(cx, y0 + t * .5f, cz), new Vector3(w, t, d), steel);
            foreach (float sx in new[] { x0 + t * .5f, x1 - t * .5f })
                BBox("Case side", g, new Vector3(sx, cy, cz), new Vector3(t, h, d), steel);
            var glass = Lit("Ent - case glass", new Color(.62f, .7f, .74f, .04f), .7f, 0, null, null, null, Mode.Transparent);
            // No probe reflection: the room probe is bright and turned the case milky.
            glass.SetFloat("_BlendModePreserveSpecular", 1); glass.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One); glass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            EditorUtility.SetDirty(glass);
            Decal("Case glass", g, new Vector3(cx, cy, zf), Vector3.back, w - 2 * t, h - 2 * t, glass, .002f);
            BBox("Case bracket", g, new Vector3(cx, y0 - .05f, zb - .1f), new Vector3(w * .6f, .04f, .2f), steel);
            var led = Lit("Ent - case LED strip", Color.white, .5f, 0, null, new Color(1f, .82f, .6f) * 1.2f);
            Box("Case LED strip", g, new Vector3(cx, y1 - t - .008f, zf + .05f), new Vector3(w - 2 * t - .02f, .012f, .02f), led);
            BBox("Case plinth", g, new Vector3(cx, y0 + t + .04f, cz), new Vector3(w * .7f, .08f, d * .7f), Worn("Ent - plinth, dark grey", Hex("#3A3C3B"), .45f));
            var anchor = new GameObject("Display anchor - the repaired unit goes here (model from the user)").transform;
            anchor.SetParent(g, false);
            anchor.SetPositionAndRotation(new Vector3(cx, y0 + t + .08f, cz), Quaternion.LookRotation(Vector3.back));
            AddLight(g, "Case light", new Vector3(cx, y1 - .08f, cz - .05f), LightType.Point, new Color(1f, .82f, .6f), .45f, .7f, false);
        }

        // ------------------------------------------------------------------------------------------------ doorway
        // The B1 plate on the concrete header over the gate (read on the way down), and the scissor gate's other leaf,
        // folded to the right jamb: a mirrored copy of the handed-over left one.
        static void Doorway(Transform g)
        {
            var plate = Lit("Ent - B1 plate", Color.white, .45f, 0, Tex("ent_b1_plate"));
            BBox("B1 plate backing", g, new Vector3(-1.54f, 2.45f, -.222f), new Vector3(.54f, .28f, .024f), EntSteel);
            Decal("B1 plate", g, new Vector3(-1.54f, 2.45f, -.234f), Vector3.back, .5f, .25f, plate, .002f);

            var src = AnyNamed("Open scissor gate folded to left");
            var pivot = new GameObject("Scissor gate folded to right - mirrored copy").transform;
            pivot.SetParent(g, false);
            pivot.position = new Vector3(-1.525f, 0, 0);   // halfway between the jambs
            var copy = Object.Instantiate(src.gameObject).transform;
            copy.name = "Folded leaf";
            copy.SetParent(pivot, true);
            pivot.localScale = new Vector3(-1, 1, 1);
        }

        // ------------------------------------------------------------------------------------------------ rails
        // The stair rails stay the handed-over ones (an orange repaint was tried and dropped). An earlier run of this pass
        // hid them and built orange copies; switch them back on so a re-run undoes that.
        static void StairRailsAsHandedOver()
        {
            int n = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (!r.enabled && EntranceArea.Contains(r.transform.position) && new[] { "Stair guard upright", "Continuous guard rail", "Lower rail", "Stair handrail",
                        "Downhill handrail", "Landing rail", "Stairhead newel", "Upper landing post" }.Contains(r.name))
                { r.enabled = true; n++; }
            log.Append("stair rails switched back on: ").Append(n).Append('\n');
        }

        // ------------------------------------------------------------------------------------------------ kerb
        static void KerbPaint(Transform g)
        {
            var hazard = Lit("Ent - hazard paint, worn", Color.white, .35f, 0, Tex("ent_hazard"));
            // The nose of the first step down, and the kerb face along the stairhead (no stopping here).
            Decal("Hazard paint - stairhead nose", g, new Vector3(-1.48f, 1.36f, -4.12f), Vector3.up, 1.76f, .09f, hazard, .006f, Vector3.forward);
            Decal("Hazard paint - kerb", g, new Vector3(-1.5f, 1.285f, -6.65f), Vector3.back, 3.6f, .15f, hazard, .004f);
        }

        // ------------------------------------------------------------------------------------------------ pipes
        // Down pipes from the run along the facade (y 4.3, half a metre out) to the sidewalk: two left of the canopy and
        // one right of the display window, rusted, clamped to the wall, rust run down the concrete under the clamps.
        static void FacadePipes(Transform g)
        {
            var rust = Worn("Ent - pipe, rusted iron", Hex("#5B3A28"), .3f, .45f);
            var streak = Lit("Ent - rust streak", new Color(.36f, .23f, .16f, .7f), .3f, 0, Tex("rust_streak"), mode: Mode.Transparent);
            foreach (var (x, r) in new[] { (-3.82f, .075f), (-4.04f, .055f), (2.56f, .07f) })
            {
                var pts = new List<Vector3> { new Vector3(x, 1.3f, -.36f), new Vector3(x, 3.92f, -.36f), new Vector3(x, 4.02f, -.38f),
                                              new Vector3(x, 4.14f, -.5f), new Vector3(x, 4.22f, -.58f), new Vector3(x, 4.32f, -.58f) };
                Tube("Down pipe", g, pts, r, rust, 10);
                Cyl("Pipe shoe", g, new Vector3(x, 1.4f, -.36f), r + .02f, .2f, rust);
                foreach (float cy in new[] { 2.1f, 3.3f })
                {
                    BBox("Pipe clamp", g, new Vector3(x, cy, -.36f), new Vector3(r * 2 + .04f, .04f, r * 2 + .04f), rust);
                    BBox("Pipe clamp stay", g, new Vector3(x, cy, (-.36f - .21f) * .5f), new Vector3(.03f, .03f, .15f), rust);
                    Decal("Rust run", g, new Vector3(x, cy - .45f, -.21f), Vector3.back, .3f, .9f, streak, .003f);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ shutter
        // Next door's shop front, shut: a rolling shutter with a "for rent" notice on it, its box under the sill course.
        static void Shutter(Transform g)
        {
            float x0 = 6.3f, x1 = 8.7f, yb = 1.36f, yt = 3.72f, z = -.07f, cx = (x0 + x1) * .5f;
            var steel = EntSteel;
            var curtain = Lit("Ent - rolling shutter", Color.white, .35f, .35f, Tex("ent_shutter"));
            Box("Shutter backing", g, new Vector3(cx, (yb + yt) * .5f, z - .02f), new Vector3(x1 - x0, yt - yb, .04f), steel);
            Decal("Shutter curtain", g, new Vector3(cx, (yb + yt) * .5f, z - .04f), Vector3.back, x1 - x0, yt - yb, curtain, .003f);
            BBox("Shutter box", g, new Vector3(cx, yt + .17f, z - .18f), new Vector3(x1 - x0 + .24f, .34f, .36f), steel);
            foreach (float gx in new[] { x0 - .04f, x1 + .04f })
                BBox("Shutter guide", g, new Vector3(gx, (yb + yt) * .5f, z - .05f), new Vector3(.07f, yt - yb, .09f), steel);
            BBox("Shutter bottom bar", g, new Vector3(cx, yb + .04f, z - .065f), new Vector3(x1 - x0, .08f, .05f), steel);
            foreach (float px in new[] { x0 + .35f, x1 - .35f })
                BBox("Padlock", g, new Vector3(px, yb + .07f, z - .11f), new Vector3(.06f, .08f, .03f), Worn("Ent - brass padlock", Hex("#8C7442"), .5f, .7f));
            Box("Snow cap", g, new Vector3(cx, yt + .36f, z - .18f), new Vector3(x1 - x0 + .26f, .04f, .38f), ExistingSnow());
            Icicles(g, "Icicles - shutter box", new Vector3(x0, yt, z - .36f), new Vector3(x1, yt, z - .36f), 10, .2f, EntIce, 37);
        }

        // ------------------------------------------------------------------------------------------------ barriers
        // Three concrete barriers along the kerb past the stairhead, pushed there by a plough, snow on their tops.
        static void Barriers(Transform g)
        {
            var mesh = JerseyMesh();
            var concrete = Worn("Ent - barrier concrete", Hex("#8C8A84"), .25f);
            var snow = ExistingSnow();
            foreach (var (x, z, yaw) in new[] { (-3.45f, -6.2f, 3f), (-5.8f, -6.18f, -4f), (-7.05f, -6.08f, 9f) })
            {
                var rot = Quaternion.Euler(0, yaw, 0);
                var at = new Vector3(x, 1.34f, z);
                MeshObj("Concrete barrier", g, mesh, concrete, at, rot);
                Box("Snow cap", g, at + Vector3.up * .815f, new Vector3(1.14f, .035f, .17f), snow, rot);
            }
        }

        static Mesh JerseyMesh()
        {
            var prof = new[] { new Vector2(-.3f, 0), new Vector2(.3f, 0), new Vector2(.3f, .08f), new Vector2(.15f, .26f), new Vector2(.08f, .8f), new Vector2(-.08f, .8f), new Vector2(-.15f, .26f), new Vector2(-.3f, .08f) };
            const float half = .6f;
            var centroid = new Vector2(0, .3f);
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            float run = 0;
            for (int i = 1; i < prof.Length; i++)   // the sides; the underside is never seen
            {
                Vector2 a = prof[i], b = prof[(i + 1) % prof.Length];
                int k = v.Count;
                v.Add(new Vector3(-half, a.y, a.x)); v.Add(new Vector3(half, a.y, a.x)); v.Add(new Vector3(half, b.y, b.x)); v.Add(new Vector3(-half, b.y, b.x));
                float len = (b - a).magnitude;
                uv.Add(new Vector2(0, run)); uv.Add(new Vector2(2 * half, run)); uv.Add(new Vector2(2 * half, run + len)); uv.Add(new Vector2(0, run + len));
                run += len;
                var mid = (a + b) * .5f - centroid;
                var outward = new Vector3(0, mid.y, mid.x);
                Tri(t, v, k, k + 1, k + 2, outward); Tri(t, v, k, k + 2, k + 3, outward);
            }
            foreach (float ex in new[] { -half, half })
            {
                int c = v.Count;
                v.Add(new Vector3(ex, centroid.y, centroid.x)); uv.Add(centroid);
                foreach (var p in prof) { v.Add(new Vector3(ex, p.y, p.x)); uv.Add(p); }
                for (int i = 0; i < prof.Length; i++) Tri(t, v, c, c + 1 + i, c + 1 + (i + 1) % prof.Length, new Vector3(Mathf.Sign(ex), 0, 0));
            }
            return SaveMesh("Jersey barrier", v, t, uv);
        }

        // ------------------------------------------------------------------------------------------------ pole
        // A concrete utility pole at the kerb by the stairhead: hazard sleeve at the bottom, a transformer can, two cross
        // arms, wires along the street both ways, across it to the far side and one down to the clinic's facade.
        static void UtilityPole(Transform g)
        {
            var concrete = Worn("Ent - pole concrete", Hex("#77786F"), .25f);
            var steel = EntSteel;
            var cable = Lit("Ent - cable, black", Hex("#121313"), .35f);
            var porcelain = Lit("Ent - insulator", Hex("#8E9A93"), .7f);
            var p = new Vector3(-4.6f, 1.3f, -6.3f);   // left of the stairhead, just out of the opposite-curb shot
            Cyl("Pole - lower", g, new Vector3(p.x, (p.y + 5.6f) * .5f, p.z), .16f, 5.6f - p.y, concrete);
            Cyl("Pole - upper", g, new Vector3(p.x, (5.6f + 10.4f) * .5f, p.z), .13f, 10.4f - 5.6f, concrete);
            Cyl("Pole - hazard sleeve", g, new Vector3(p.x, p.y + .85f, p.z), .168f, 1.5f, Lit("Ent - hazard sleeve", Color.white, .35f, 0, Tex("ent_hazard_sleeve")));
            for (float sy = 3.4f; sy < 9f; sy += .45f)
            {
                float side = Mathf.Repeat(sy * 2.2f, 2f) < 1 ? 1 : -1;
                Rod("Step bolt", g, new Vector3(p.x + side * .12f, sy, p.z), new Vector3(p.x + side * .32f, sy, p.z), .012f, steel);
            }
            Cyl("Transformer can", g, new Vector3(p.x, 8.2f, p.z + .4f), .27f, .8f, Worn("Ent - transformer grey", Hex("#5E6663"), .4f, .4f));
            BBox("Transformer bracket", g, new Vector3(p.x, 8.0f, p.z + .2f), new Vector3(.08f, .3f, .3f), steel);

            var ends = new List<Vector3>();
            foreach (float ay in new[] { 9.9f, 9.35f })
            {
                BBox("Cross arm", g, new Vector3(p.x, ay, p.z), new Vector3(.09f, .09f, 1.8f), steel);
                foreach (float oz in new[] { -.8f, -.3f, .3f, .8f })
                {
                    var at = new Vector3(p.x, ay + .1f, p.z + oz);
                    Cyl("Insulator", g, at, .035f, .12f, porcelain);
                    ends.Add(at + Vector3.up * .06f);
                }
            }
            int w = 0;
            void Wire(Vector3 a, Vector3 b, float sag) => Tube("Wire " + (++w), g, Catenary(a, b, sag, 18), .012f, cable, 5);
            // Along the street both ways (out of shot), on the upper arm.
            foreach (var e in ends.Take(4)) { Wire(e, new Vector3(-19f, e.y - .2f, e.z + .1f), .7f); Wire(e, new Vector3(13f, e.y - .3f, e.z - .1f), .6f); }
            // Across the street to the far side, slanting, and down to the clinic's facade, from the lower arm.
            Wire(ends[4], new Vector3(1.6f, 7.4f, -10.75f), .45f);
            Wire(ends[5], new Vector3(-7.8f, 7.8f, -10.75f), .5f);
            Wire(ends[6], new Vector3(-1.6f, 7.9f, -.06f), .35f);
            Wire(ends[7], new Vector3(3.4f, 8.2f, -.06f), .4f);
        }

        // ------------------------------------------------------------------------------------------------ cameras
        // Review only (disabled); the exterior shots render every camera in the scene.
        static void EntranceCameras(Transform g)
        {
            foreach (var (name, eye, look) in new[]
            {
                ("Entrance A - sidewalk by the display window", new Vector3(4.6f, 3.0f, -3.4f), new Vector3(-.6f, 2.5f, -.6f)),
                ("Entrance B - across the street", new Vector3(.2f, 2.95f, -9.8f), new Vector3(-.5f, 2.8f, -.8f)),
                ("Entrance C - the display window", new Vector3(1.9f, 3.0f, -2.7f), new Vector3(1.1f, 1.95f, .5f)),
            })
            {
                var go = new GameObject(name);
                go.transform.SetParent(g, false);
                go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = 55; cam.nearClipPlane = .05f; cam.farClipPlane = 300; cam.enabled = false;
            }
        }
    }
}
