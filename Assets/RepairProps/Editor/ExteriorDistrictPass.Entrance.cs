using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // The street entrance, after the concept sheet the user picked (lit sign on a canopy over the stairs, B1 plate, folding
    // gates, orange stair rails, rusted pipes, a shutter next door, barriers, a pole and its wires). Only the ten-odd metres
    // round the stairwell; the road, the far side and the sky are the exterior pass's. Built by the interior pass under its
    // root (the window cut is there too), so one re-run rebuilds both. Handed-over objects it replaces are hidden by
    // name near the entrance (they keep their colliders) and rebuilt here under new names, so the exterior pass never
    // finds the copies when it looks things up by name.
    public static partial class ExteriorDistrictPass
    {
        static readonly Bounds EntranceArea = new Bounds(new Vector3(-1.5f, 1.6f, -2.9f), new Vector3(4.6f, 4f, 6.6f));

        static void StreetEntrance(Transform g)
        {
            Canopy(Group(g, "Canopy and lit sign over the stairs"));
            Doorway(Group(g, "Doorway - B1 plate and the second folded gate"));
            OrangeRails(Group(g, "Stair rails - old orange paint"));
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

        // ------------------------------------------------------------------------------------------------ canopy
        // A concrete canopy over the foot of the stairs, hung from the facade on two tie rods, with the shop's lightbox
        // (the same 义体维修 face the old one over the stairs had) standing on its front edge, and a fluorescent batten
        // under it lighting the steps. The old lightbox, its icicles and the enamel fascia with its snow (now under the canopy) go.
        static void Canopy(Transform g)
        {
            HideNamed(EntranceArea, "Weathered repair lightbox", "Icicles - lightbox", "Snow on fascia", "Repair shop enamel fascia");
            var concrete = Worn("Ent - canopy concrete", Hex("#77756E"), .2f);
            var steel = EntSteel;
            float x0 = -2.85f, x1 = -.15f, z0 = -.05f, z1 = -1.72f, y0 = 3.4f, y1 = 3.56f, cx = (x0 + x1) * .5f;
            BBox("Canopy slab", g, new Vector3(cx, (y0 + y1) * .5f, (z0 + z1) * .5f), new Vector3(x1 - x0, y1 - y0, z0 - z1), concrete);
            BBox("Canopy drip edge", g, new Vector3(cx, y0 - .03f, z1 + .035f), new Vector3(x1 - x0, .06f, .07f), concrete);
            foreach (float tx in new[] { x0 + .12f, x1 - .12f })
            {
                Rod("Tie rod", g, new Vector3(tx, 4.0f, z0 - .02f), new Vector3(tx, y1 + .01f, z1 + .1f), .014f, steel);
                BBox("Tie rod wall plate", g, new Vector3(tx, 4.0f, z0 - .012f), new Vector3(.12f, .16f, .024f), steel);
            }

            var signTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/RepairProps/Scenes/Temp_HarbourStudy/repair_lightbox.png");
            var face = Lit("Ent - lit sign face", Color.white, .3f, 0, signTex, new Color(1f, .8f, .58f) * 1.3f, signTex);
            float sw = 2.2f, sh = .55f, sd = .24f, sz = z1 + .2f, sy = y1 + .07f + sh * .5f;
            BBox("Lightbox case", g, new Vector3(cx, sy, sz), new Vector3(sw + .08f, sh + .08f, sd), steel);
            Decal("Lightbox face", g, new Vector3(cx, sy, sz - sd * .5f), Vector3.back, sw, sh, face, .004f);
            foreach (float fx in new[] { cx - sw * .38f, cx + sw * .38f })
                BBox("Lightbox foot", g, new Vector3(fx, y1 + .035f, sz), new Vector3(.08f, .07f, sd - .04f), steel);
            var snow = ExistingSnow();
            Box("Snow cap", g, new Vector3(cx, sy + sh * .5f + .04f + .02f, sz), new Vector3(sw + .1f, .04f, sd + .02f), snow);
            float back = sz + sd * .5f + .02f;
            Box("Snow cap", g, new Vector3(cx, y1 + .025f, (z0 + back) * .5f - .01f), new Vector3(x1 - x0 - .06f, .05f, z0 - back - .04f), snow);
            Icicles(g, "Icicles - canopy", new Vector3(x0 + .1f, y0 - .06f, z1 - .002f), new Vector3(x1 - .1f, y0 - .06f, z1 - .002f), 14, .24f, EntIce, 31);
            AddLight(g, "Lightbox glow", new Vector3(cx, sy, sz - .7f), LightType.Point, new Color(1f, .8f, .58f), 1.4f, 3.2f, false);

            var tube = Lit("Ent - fluorescent tube", Color.white, .5f, 0, null, new Color(1f, .93f, .8f) * 2.2f);
            BBox("Batten housing", g, new Vector3(cx, y0 - .025f, -1.0f), new Vector3(1.3f, .05f, .1f), steel);
            Cyl("Batten tube", g, new Vector3(cx, y0 - .065f, -1.0f), .014f, 1.2f, tube, Quaternion.Euler(0, 0, 90));
            // No shadows (the shadow atlas limit, as for the display spot). Aimed down the stairs; the cone stops about half
            // a metre inside the gate, which is as far as the light would get through the opening anyway.
            var l = AddLight(g, "Canopy batten", new Vector3(cx, y0 - .12f, -1.0f), LightType.Spot, new Color(1f, .9f, .76f), 7f, 6.5f, false);
            l.transform.rotation = Quaternion.LookRotation(new Vector3(0, -1f, -.5f));
            l.spotAngle = 110; l.innerSpotAngle = 50;
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
        // The stair guard, handrails and newels: old orange paint gone dull and rusty, the one warm colour on the steel.
        static void OrangeRails(Transform g)
        {
            var paint = Worn("Ent - rail, old orange paint", Hex("#A9521F"), .38f, .3f);
            int n = 0;
            foreach (var r in HideNamed(EntranceArea, "Stair guard upright", "Continuous guard rail", "Lower rail", "Stair handrail",
                                        "Downhill handrail", "Landing rail", "Stairhead newel", "Upper landing post"))
            {
                var t = r.transform;
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                if (mesh != null && mesh.name == "Cube") BBox(r.name + " - orange paint", g, t.position, t.lossyScale, paint, t.rotation);
                else MeshObj(r.name + " - orange paint", g, mesh, paint, t.position, t.rotation).transform.localScale = t.lossyScale;
                n++;
            }
            log.Append("orange rails: ").Append(n).Append('\n');
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
