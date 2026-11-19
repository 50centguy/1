using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // The street around the clinic, so it is not the one lit door on a street of shutters: two shops open across the
    // road (a convenience store, a noodle bar), a pharmacy's night hatch further down, and the street's cyber pieces (a
    // projected ad, a delivery drone, cameras, a fibre trunk over the road, a dumped prosthetic arm in the snow).
    // Interiors are painted cards behind half-raised shutters, the way PS2 games lit a shop you never enter. No new
    // light casts shadows (more than five shadow maps in one view break the additional-light atlas here).
    public static partial class ExteriorDistrictPass
    {
        // The far side's facade: the frontage block faces the street at z -10.85, the sidewalk top is 1.35.
        const float FarFace = -10.85f, FarWalk = 1.35f;

        static void Neighbours(Transform g)
        {
            ConvenienceStore(Group(g, "Shop across - convenience store"));
            NoodleBar(Group(g, "Shop across - noodle bar"));
            PharmacyHatch(Group(g, "Shop down the road - pharmacy night hatch"));
            HoloAd(Group(g, "Cyber - projected ad across the street"));
            Drone(Group(g, "Cyber - delivery drone"));
            Cameras(Group(g, "Cyber - street cameras"));
            FibreTrunk(Group(g, "Cyber - fibre trunk over the road"));
            DumpedArm(Group(g, "Cyber - prosthetic arm dumped in the snow"));
            NeighbourCameras(g);
            JapaneseSigns();
        }

        static Material ShopCard(string name, string tex, Color glow) =>
            Lit(name, new Color(.3f, .3f, .3f), .2f, 0, Tex(tex), glow, Tex(tex));

        static Material LitSign(string name, string tex, float glow = 1.1f) =>
            Lit(name, Color.white, .3f, 0, Tex(tex), Color.white * glow, Tex(tex));

        // ------------------------------------------------------------------------------------------------ store
        // Directly across from the stairhead: the green louvred shutter, raised to a hand's width over head height.
        static void ConvenienceStore(Transform g)
        {
            HideNamed(new Bounds(new Vector3(.13f, 2.6f, -10.58f), new Vector3(2.2f, 1.9f, .4f)), "Louvred shutter");
            const float x = .13f, w = 1.96f, zr = -10.6f, top = 3.6f;
            var green = Worn("Nb - shutter, green painted steel", Hex("#3E5A4C"), .3f, .3f);
            BBox("Shutter bottom bar", g, new Vector3(x, top, -10.58f), new Vector3(w + .02f, .06f, .14f), green);
            Decal("Store inside", g, new Vector3(x, (FarWalk + top) * .5f, zr), Vector3.forward, w, top - FarWalk, ShopCard("Nb - store interior", "nb_store_interior", new Color(.92f, .97f, 1f) * .95f), .002f);
            Decal("Store sign", g, new Vector3(x, 4.05f, -10.22f), Vector3.forward, 2.2f, .26f, LitSign("Nb - store sign", "nb_store_sign", 1.2f), .004f);
            BBox("Door mat", g, new Vector3(x, FarWalk + .01f, -10.3f), new Vector3(1.2f, .02f, .5f), Worn("Nb - rubber mat", Hex("#2A2C2B"), .2f));
            var crate = Worn("Nb - water crate, blue plastic", Hex("#33597A"), .35f);
            for (int i = 0; i < 3; i++)
                BBox("Water crate", g, new Vector3(1.45f + (i == 2 ? .05f : 0), FarWalk + .15f + (i == 2 ? .3f : 0), -10.5f + (i == 1 ? .42f : 0)), new Vector3(.4f, .3f, .4f), crate, Quaternion.Euler(0, i * 7, 0));
            Box("Snow cap", g, new Vector3(1.5f, FarWalk + .62f, -10.5f), new Vector3(.38f, .03f, .38f), ExistingSnow());
            AddLight(g, "Store spill", new Vector3(x, 2.7f, -10.1f), LightType.Point, new Color(.86f, .93f, 1f), 2.4f, 4.5f, false);
        }

        // ------------------------------------------------------------------------------------------------ noodle bar
        // A counter open to the street under a canvas awning, stools on the sidewalk, noren, two paper lanterns, the pot
        // steaming. The rolled shutter above is the one that was down to the pavement.
        static void NoodleBar(Transform g)
        {
            HideNamed(new Bounds(new Vector3(-7.2f, 2.35f, -10.7f), new Vector3(3.2f, 1.75f, .3f)), "Rolled metal slat");
            const float x = -7.2f, x0 = -8.65f, x1 = -5.75f;
            Decal("Noodle bar inside", g, new Vector3(x, (FarWalk + 3.2f) * .5f, -10.8f), Vector3.forward, x1 - x0, 3.2f - FarWalk, ShopCard("Nb - noodle bar interior", "nb_noodle_interior", new Color(1f, .9f, .8f) * .9f), .002f);
            var steel = Worn("Nb - counter, brushed steel", Hex("#8A8E8C"), .5f, .7f);
            BBox("Counter", g, new Vector3(x, FarWalk + .52f, -10.59f), new Vector3(2.7f, 1.04f, .42f), steel);
            BBox("Counter top", g, new Vector3(x, FarWalk + 1.06f, -10.55f), new Vector3(2.8f, .04f, .55f), Lit("Nb - counter top, red laminate", Hex("#7A2A24"), .5f));
            var bowl = Lit("Nb - bowl, white", Hex("#E2DED2"), .6f);
            foreach (float bx in new[] { -8.1f, -6.5f })
                Cyl("Bowl", g, new Vector3(bx, FarWalk + 1.12f, -10.45f), .08f, .07f, bowl);
            var seat = Worn("Nb - stool seat, red vinyl", Hex("#8C2C26"), .5f);
            var leg = Worn("Nb - stool leg, worn steel", Hex("#6E7471"), .5f, .7f);
            foreach (float sx in new[] { -8.25f, -7.55f, -6.85f, -6.15f })
            {
                Cyl("Stool leg", g, new Vector3(sx, FarWalk + .33f, -10.02f), .03f, .66f, leg);
                Cyl("Stool foot", g, new Vector3(sx, FarWalk + .01f, -10.02f), .16f, .02f, leg);
                Cyl("Stool seat", g, new Vector3(sx, FarWalk + .69f, -10.02f), .17f, .06f, seat);
            }

            // Awning: canvas on a frame, from the wall at 3.3 down to 3.0 at the front, snow on it.
            float zw = FarFace, zf = -9.95f, yw = 3.3f, yf = 3.0f, run = zf - zw, drop = yw - yf, len = Mathf.Sqrt(run * run + drop * drop);
            var rot = Quaternion.Euler(Mathf.Atan2(drop, run) * Mathf.Rad2Deg, 0, 0);   // the street (+z) end down
            var mid = new Vector3(x, (yw + yf) * .5f, (zw + zf) * .5f);
            BBox("Awning canvas", g, mid, new Vector3(3.1f, .025f, len), Worn("Nb - awning canvas, faded red", Hex("#6E2C26"), .15f), rot);
            Box("Snow cap", g, mid + rot * Vector3.up * .035f, new Vector3(3.0f, .04f, len - .06f), ExistingSnow(), rot);
            Icicles(g, "Icicles - noodle bar awning", new Vector3(x0 - .1f, yf - .02f, zf + .01f), new Vector3(x1 + .1f, yf - .02f, zf + .01f), 9, .16f, EntIce, 51);
            for (int i = 0; i < 3; i++)
                Decal("Noren", g, new Vector3(x + .6f - i * .6f, yf - .25f, zf + .02f), Vector3.forward, .56f, .45f, Lit("Nb - noren " + i, Color.white, .1f, 0, Tex("nb_noren_" + i)), 0f);
            var lantern = Lit("Nb - paper lantern, red", Hex("#B0302A"), .2f, 0, null, new Color(1f, .35f, .25f) * 1.6f);
            var cap = Lit("Nb - lantern cap, black", Hex("#151515"), .3f);
            foreach (float lx in new[] { x0 - .05f, x1 + .05f })
            {
                Prim(PrimitiveType.Capsule, "Paper lantern", g, new Vector3(lx, yf - .24f, zf + .03f), new Vector3(.26f, .17f, .26f), Quaternion.identity, lantern, false);
                Cyl("Lantern cap", g, new Vector3(lx, yf - .07f, zf + .03f), .07f, .03f, cap);
                Cyl("Lantern cap", g, new Vector3(lx, yf - .41f, zf + .03f), .07f, .03f, cap);
            }

            // Lightbox on the wall over the awning, and a red neon blade out from the wall for people down the street.
            var case_ = Worn("Nb - sign case, dark steel", Hex("#2D3133"), .4f, .5f);
            BBox("Lightbox case", g, new Vector3(x, 3.78f, FarFace + .08f), new Vector3(2.08f, .56f, .16f), case_);
            Decal("Lightbox face", g, new Vector3(x, 3.78f, FarFace + .16f), Vector3.forward, 2.0f, .5f, LitSign("Nb - noodle bar sign", "nb_noodle_sign", 1.1f), .004f);
            float bx2 = x1 + .3f, bz = FarFace + .4f;
            var blade = Lit("Nb - neon blade, noodles", new Color(.04f, .04f, .05f), .4f, 0, Tex("nb_blade_noodle"), Color.white * 1.8f, Tex("nb_blade_noodle"));
            BBox("Neon blade box", g, new Vector3(bx2, 4.0f, bz), new Vector3(.06f, 1.26f, .44f), case_);
            Decal("Neon blade - east face", g, new Vector3(bx2, 4.0f, bz), Vector3.right, .4f, 1.2f, blade, .034f);
            Decal("Neon blade - west face", g, new Vector3(bx2, 4.0f, bz), Vector3.left, .4f, 1.2f, blade, .034f);
            foreach (float by in new[] { 3.5f, 4.5f })
                Rod("Blade bracket", g, new Vector3(bx2, by, FarFace), new Vector3(bx2, by, bz - .2f), .012f, case_);

            Steam("Steam - noodle pot", g, new Vector3(-7.9f, 2.55f, -10.7f), Vector3.up, .8f, Particles("Steam puff", Tex("steam_puff")));
            AddLight(g, "Noodle bar spill", new Vector3(x, 2.85f, -10.2f), LightType.Point, new Color(1f, .84f, .72f), 2.2f, 4.5f, false);
        }

        // ------------------------------------------------------------------------------------------------ pharmacy
        // Further down, shutter still down: a lit night-hatch plate on it and a green LED cross out from the wall.
        static void PharmacyHatch(Transform g)
        {
            const float face = -10.66f;
            Decal("Night hatch plate", g, new Vector3(-13.4f, 2.45f, face), Vector3.forward, .84f, .42f, LitSign("Nb - pharmacy hatch sign", "nb_pharmacy_sign", .9f), .006f);
            var case_ = Worn("Nb - sign case, dark steel", Hex("#2D3133"), .4f, .5f);
            Cyl("Bell push", g, new Vector3(-12.85f, 2.3f, face + .02f), .025f, .03f, Lit("Nb - bell push, red", Hex("#B03A30"), .5f), Quaternion.Euler(90, 0, 0));
            float cx = -12.6f, cy = 3.95f, cz = -10.45f;
            var cross = Lit("Nb - LED cross, green", new Color(.03f, .05f, .04f), .4f, 0, Tex("nb_pharmacy_cross"), Color.white * 2.2f, Tex("nb_pharmacy_cross"));
            BBox("Cross box", g, new Vector3(cx, cy, cz), new Vector3(.08f, .7f, .7f), case_);
            Decal("Cross - east face", g, new Vector3(cx, cy, cz), Vector3.right, .64f, .64f, cross, .042f);
            Decal("Cross - west face", g, new Vector3(cx, cy, cz), Vector3.left, .64f, .64f, cross, .042f);
            Rod("Cross bracket", g, new Vector3(cx, cy + .3f, -10.9f), new Vector3(cx, cy + .3f, cz - .3f), .012f, case_);
            Rod("Cross bracket", g, new Vector3(cx, cy - .3f, -10.9f), new Vector3(cx, cy - .3f, cz - .3f), .012f, case_);
            AddLight(g, "Pharmacy cross", new Vector3(cx, cy, cz + .3f), LightType.Point, new Color(.4f, 1f, .6f), .8f, 3f, false);
        }

        // ------------------------------------------------------------------------------------------------ cyber
        // A prosthetics ad projected over the noodle bar's neighbour, magenta: the only one, the street's loudest thing.
        static void HoloAd(Transform g)
        {
            var m = Holo("Nb - holo ad, neural interface", "nb_holo_ad", 1.35f, -1, .3f);
            m.SetColor("_Color", new Color(1f, .45f, .8f));
            EditorUtility.SetDirty(m);
            Decal("Ad", g, new Vector3(-3.4f, 5.75f, FarFace + .3f), Vector3.forward, 2.6f, 1.52f, m, 0f);
            var steel = EntSteel;
            BBox("Projector", g, new Vector3(-3.4f, 4.86f, FarFace + .12f), new Vector3(.3f, .1f, .24f), steel);
            Sphere("Projector lens", g, new Vector3(-3.4f, 4.92f, FarFace + .2f), .025f, Beacon("Nb - lens, magenta", new Color(1f, .45f, .8f), 1.6f, 0, 1));
        }

        // A parcel drone holding over the street, nav lights blinking, the parcel on a tether.
        static void Drone(Transform g)
        {
            var c = new Vector3(1.6f, 5.4f, -5.6f);
            var shell = Worn("Nb - drone shell, grey polymer", Hex("#55595A"), .45f);
            var dark = Worn("Nb - black plastic", Hex("#1C1E1E"), .4f);
            BBox("Drone body", g, c, new Vector3(.38f, .1f, .26f), shell);
            var rotor = Lit("Nb - rotor blur", new Color(.2f, .21f, .22f, .35f), .3f, 0, null, null, null, Mode.Transparent);
            int k = 0;
            foreach (var arm in new[] { new Vector3(.32f, 0, .24f), new Vector3(-.32f, 0, .24f), new Vector3(.32f, 0, -.24f), new Vector3(-.32f, 0, -.24f) })
            {
                Rod("Drone arm", g, c, c + arm, .014f, dark);
                Cyl("Motor", g, c + arm + Vector3.up * .03f, .03f, .05f, dark);
                Cyl("Rotor disc", g, c + arm + Vector3.up * .06f, .15f, .004f, rotor);
                Sphere("Nav light", g, c + arm - Vector3.up * .02f, .014f, k++ < 2 ? Beacon("Nb - nav light, green", new Color(.35f, 1f, .45f), 2f, 1.2f, .4f) : Beacon("Nb - nav light, red", new Color(1f, .3f, .25f), 2f, 1.2f, .4f));
            }
            Rod("Tether", g, c - Vector3.up * .05f, c - Vector3.up * .45f, .004f, dark);
            BBox("Parcel", g, c - Vector3.up * .53f, new Vector3(.22f, .16f, .18f), Worn("Nb - parcel, brown card", Hex("#8A6A48"), .1f));
            Box("Snow on parcel", g, c - Vector3.up * .44f, new Vector3(.2f, .015f, .16f), ExistingSnow());
            Sphere("Scanner", g, c - Vector3.up * .06f, .025f, Beacon("Status cyan", HoloCyan, 1.6f, 0, 1));
            AddLight(g, "Drone scanner", c - Vector3.up * .12f, LightType.Point, HoloCyan, .5f, 2f, false);
        }

        static void Cameras(Transform g)
        {
            var housing = Worn("Nb - camera housing, white polymer", Hex("#BDBBB2"), .4f);
            var steel = EntSteel;
            var led = Beacon("Nb - camera LED, red", new Color(1f, .25f, .2f), 2.2f, .5f, .5f);
            foreach (var (pos, n) in new[] { (new Vector3(.95f, 3.95f, -.05f), Vector3.back), (new Vector3(-4.3f, 4.2f, FarFace), Vector3.forward), (new Vector3(5.2f, 4.0f, -.05f), Vector3.back) })
            {
                var aim = Quaternion.LookRotation((n + Vector3.down * .45f).normalized);
                var head = pos + n * .28f;
                Rod("Camera arm", g, pos, head + Vector3.up * .06f, .015f, steel);
                BBox("Camera housing", g, head, new Vector3(.11f, .1f, .26f), housing, aim);
                Sphere("Camera LED", g, head + aim * new Vector3(.03f, .05f, .13f), .008f, led);
                Cyl("Camera lens", g, head + aim * Vector3.forward * .13f, .035f, .02f, Lit("Nb - lens glass", new Color(.03f, .04f, .05f), .95f), aim * Quaternion.Euler(90, 0, 0));
            }
        }

        // Three fibre cables slung from the far facade to the clinic's, a splice box on them halfway with its status
        // lights: the street's data runs overhead like the power does.
        static void FibreTrunk(Transform g)
        {
            var cable = Lit("Nb - fibre trunk, black", Hex("#141516"), .45f);
            var a = new Vector3(3.2f, 6.3f, FarFace); var b = new Vector3(3.1f, 6.1f, -.05f);
            List<Vector3> centre = null;
            foreach (float dx in new[] { -.08f, 0f, .08f })
            {
                var pts = Catenary(a + Vector3.right * dx, b + Vector3.right * dx, .55f + dx, 18);
                Tube("Fibre cable", g, pts, .022f, cable, 6);
                if (dx == 0) centre = pts;
            }
            var box = centre[centre.Count / 2] - Vector3.up * .06f;
            BBox("Splice box", g, box, new Vector3(.26f, .16f, .34f), Worn("Nb - splice box, dark steel", Hex("#2D3133"), .4f, .5f));
            Box("Splice box LEDs", g, box + new Vector3(.131f, .02f, 0), new Vector3(.004f, .02f, .2f), Beacon("Status cyan", HoloCyan, 1.6f, 0, 1));
            Box("Snow cap", g, box + Vector3.up * .095f, new Vector3(.24f, .03f, .32f), ExistingSnow());
            foreach (var end in new[] { a, b })
                BBox("Cable anchor", g, end, new Vector3(.3f, .1f, .12f), EntSteel);
        }

        // Someone's old forearm-and-hand unit, dumped in the snow under the clinic's window: white shell, steel joints,
        // the wiring hanging out of the elbow, one indicator still faintly lit.
        static void DumpedArm(Transform g)
        {
            var pivot = new GameObject("Arm pivot").transform;
            pivot.SetParent(g, false);
            var o = new Vector3(1.5f, 1.585f, -.72f);
            pivot.position = o;
            var shell = Worn("Nb - prosthetic shell, white polymer", Hex("#C9C6BC"), .45f);
            var joint = Worn("Nb - prosthetic joint, brushed steel", Hex("#8A8E8C"), .55f, .8f);
            var dark = Worn("Nb - black plastic", Hex("#1C1E1E"), .4f);
            var along = Quaternion.Euler(0, 0, 90);
            Prim(PrimitiveType.Capsule, "Forearm shell", pivot, o + new Vector3(0, .02f, 0), new Vector3(.1f, .19f, .1f), along, shell);
            Box("Forearm seam", pivot, o + new Vector3(0, .07f, 0), new Vector3(.3f, .006f, .02f), dark);
            Sphere("Elbow joint", pivot, o + new Vector3(-.21f, .02f, 0), .055f, joint);
            Sphere("Wrist joint", pivot, o + new Vector3(.2f, .02f, 0), .04f, joint);
            BBox("Palm", pivot, o + new Vector3(.29f, .02f, 0), new Vector3(.11f, .035f, .09f), shell);
            for (int i = 0; i < 4; i++)
            {
                var f0 = o + new Vector3(.345f, .02f, -.033f + i * .022f);
                Rod("Finger", pivot, f0, f0 + new Vector3(.07f - (i == 3 ? .02f : 0), -.01f, 0), .009f, joint);
            }
            Rod("Thumb", pivot, o + new Vector3(.27f, .02f, .045f), o + new Vector3(.32f, .015f, .08f), .01f, joint);
            var colours = new[] { Hex("#A8322B"), Hex("#2F5F96"), Hex("#C9A53A") };
            for (int i = 0; i < 3; i++)
            {
                var s = o + new Vector3(-.25f, .02f, -.02f + i * .02f);
                Tube("Wiring", pivot, new List<Vector3> { s, s + new Vector3(-.06f, .01f, .01f * i), s + new Vector3(-.11f, -.02f, .03f * i - .02f), s + new Vector3(-.15f, -.035f, .05f * i - .03f) }, .006f, Lit("Nb - wire insulation " + i, colours[i], .4f), 5);
            }
            Sphere("Status pip", pivot, o + new Vector3(.1f, .072f, 0), .008f, Beacon("Nb - status pip, dim cyan", HoloCyan, .9f, .25f, .2f));
            Box("Snow drift over it", pivot, o + new Vector3(-.05f, -.03f, .05f), new Vector3(.34f, .05f, .16f), ExistingSnow(), Quaternion.Euler(0, 0, 4));
            pivot.rotation = Quaternion.Euler(0, 28, 0);
        }

        // The street's signage is Japanese. Our own textures were regenerated in Japanese; the three junction signs from
        // the map pass (whose textures are not ours) get Japanese copies of their material in this scene, and the
        // counter label seen through the door reads 受付.
        static void JapaneseSigns()
        {
            int n = 0;
            foreach (var (obj, tex, glow) in new[] { ("District signage shop", "map_shop_jp", .55f), ("District signage way", "map_way_jp", .45f), ("District signage pier", "map_pier_jp", .3f) })
            {
                var t = Tex(tex);
                if (t == null) continue;
                var m = Lit("Jp - " + obj, Color.white, .3f, 0, t, new Color(glow, glow * 1.06f, glow * 1.08f), t);
                foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name == obj)) { r.sharedMaterial = m; n++; }
            }
            foreach (var tm in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(t => t.text == "收件" || t.text == "受付"))
            {
                tm.text = "受付"; n++;
            }
            log.Append("signage switched to Japanese: ").Append(n).Append('\n');
        }

        static void NeighbourCameras(Transform g)
        {
            foreach (var (name, eye, look) in new[]
            {
                ("Street C - across to the noodle bar", new Vector3(-4.4f, 3.0f, -5.2f), new Vector3(-6.4f, 2.7f, -10.8f)),
                ("Street D - stairhead, looking across", new Vector3(-1.5f, 3.0f, -4.9f), new Vector3(-1.2f, 3.4f, -10.8f)),
                ("Street E - down the street, both sides", new Vector3(5.5f, 3.3f, -6.6f), new Vector3(-8f, 3.2f, -7.4f)),
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
