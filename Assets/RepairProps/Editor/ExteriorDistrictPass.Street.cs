using System.Linq;
using UnityEngine;

namespace BorderRepair.RepairProps.EditorTools
{
    // Third pass: the street gets the things a street has (a vending pair with its own glow, a snowed-in cargo trike,
    // bollards, drains with meltwater, bins and bags, a street-name plate, flyers, cables across), and the distance gets
    // layers (mid blocks with lit windows in the scene fog, an elevated railway with a train beyond the harbour).
    public static partial class ExteriorDistrictPass
    {
        static void BuildStreetDetail(Transform g)
        {
            var steel = Mat("Dark steel");
            var snow = SnowMaterial();

            // Vending pair on the clinic's street floor, east of the service door: one lit, one dead with a notice.
            {
                var h = Group(g, "Vending machines");
                var body = Lit("Vending body", new Color(.4f, .43f, .44f), .4f, .3f);
                foreach (var (x, tex, glow) in new[] { (3.3f, "vending_drinks", 1.1f), (5.35f, "vending_dead", .3f) })
                {
                    float fz = WallHit(new Vector3(x, 1.9f, -3f), Vector3.forward, out var hit) ? hit.point.z : -.05f;
                    float y = Ground(x, fz - .5f);
                    Box("Vending cabinet", h, new Vector3(x, y + .92f, fz - .37f), new Vector3(.9f, 1.84f, .72f), body);
                    Decal("Vending front", h, new Vector3(x, y + .94f, fz - .735f), Vector3.back, .84f, 1.68f,
                          Lit("Vending front - " + tex, Color.white, .7f, 0, Tex(tex), new Color(.86f, .88f, .86f) * glow, Tex(tex)), 0f);
                    SnowCap(h, new Vector3(x, y + 1.87f, fz - .37f), new Vector3(.94f, .06f, .76f));
                }
                float gz = WallHit(new Vector3(3.3f, 1.9f, -3f), Vector3.forward, out var gh) ? gh.point.z : -.05f;
                var glowLight = new GameObject("Vending glow").AddComponent<Light>();
                glowLight.transform.SetParent(h, false);
                glowLight.transform.position = new Vector3(3.3f, Ground(3.3f, gz - 1.2f) + 1.1f, gz - 1.15f);
                glowLight.type = LightType.Point;
                glowLight.color = new Color(.84f, .9f, .9f);
                glowLight.intensity = 1.2f;
                glowLight.range = 3.4f;
                glowLight.shadows = LightShadows.None;
            }

            // A cargo trike left on the far pavement under the tenement, load under a tarp, all of it snowed over.
            {
                var h = Group(g, "Cargo trike - snowed in");
                float x0 = 2.9f, z0 = -10.15f, y = Ground(x0, z0);
                var paint = Lit("Trike paint - faded blue", new Color(.3f, .36f, .4f), .35f, .3f);
                var tyre = Mat("Old rubber");
                var wheel = Quaternion.Euler(90, 0, 0);
                Box("Cargo bed", h, new Vector3(x0 - .35f, y + .55f, z0), new Vector3(1.4f, .08f, 1.08f), paint);
                foreach (var s in new[] { -1, 1 })
                {
                    Box("Bed rail", h, new Vector3(x0 - .35f, y + .7f, z0 + s * .53f), new Vector3(1.4f, .24f, .03f), paint);
                    Cyl("Rear wheel", h, new Vector3(x0 - .55f, y + .24f, z0 + s * .5f), .24f, .07f, tyre, wheel);
                }
                Box("Bed tailboard", h, new Vector3(x0 - 1.04f, y + .7f, z0), new Vector3(.03f, .24f, 1.08f), paint);
                Box("Load under tarp", h, new Vector3(x0 - .38f, y + .82f, z0), new Vector3(1.28f, .46f, .98f), Mat("Tarp rubber green"));
                SnowCap(h, new Vector3(x0 - .38f, y + 1.08f, z0), new Vector3(1.32f, .07f, 1.02f));
                Rod("Frame", h, new Vector3(x0 + .3f, y + .55f, z0), new Vector3(x0 + .85f, y + .62f, z0), .03f, paint);
                Rod("Fork", h, new Vector3(x0 + .85f, y + .26f, z0), new Vector3(x0 + .78f, y + 1.0f, z0), .025f, steel);
                Rod("Handlebar", h, new Vector3(x0 + .76f, y + 1.0f, z0 - .3f), new Vector3(x0 + .76f, y + 1.0f, z0 + .3f), .018f, steel);
                Cyl("Front wheel", h, new Vector3(x0 + .86f, y + .26f, z0), .26f, .06f, tyre, wheel);
                Box("Saddle", h, new Vector3(x0 + .45f, y + .86f, z0), new Vector3(.24f, .06f, .14f), Mat("Old rubber"));
                Box("Snow on saddle", h, new Vector3(x0 + .45f, y + .91f, z0), new Vector3(.26f, .04f, .16f), snow);
                Decal("Number plate", h, new Vector3(x0 - 1.06f, y + .52f, z0), Vector3.left, .3f, .095f, Lit("Number plate", Color.white, .5f, 0, Tex("van_plate")), 0f);
            }

            // Bollards along the near kerb east of the stairs, each with its own cap of snow.
            {
                var h = Group(g, "Bollards");
                for (float x = .9f; x <= 6.6f; x += 1.4f)
                {
                    float y = Ground(x, -6.5f);
                    Cyl("Bollard", h, new Vector3(x, y + .4f, -6.5f), .065f, .8f, steel);
                    Cyl("Bollard snow", h, new Vector3(x, y + .82f, -6.5f), .072f, .045f, snow);
                }
            }

            // Drains at the kerbs, the only place the slush runs off: a dark grate and a meltwater stain around it.
            {
                var h = Group(g, "Drains and meltwater");
                var grate = Lit("Drain grate", Color.white, .5f, .6f, Tex("drain_grate"));
                var melt = Lit("Meltwater", new Color(.05f, .06f, .065f, .5f), .92f, 0, null, mode: Mode.Transparent);
                foreach (var (x, z) in new[] { (-4.2f, -9.12f), (2.4f, -9.12f), (-6.1f, -6.78f), (4.6f, -6.78f) })
                {
                    float y = Ground(x, z);
                    Box("Drain grate", h, new Vector3(x, y + .006f, z), new Vector3(.56f, .02f, .28f), grate);
                    Decal("Meltwater", h, new Vector3(x, y, z), Vector3.up, 1.3f, .8f, melt, .004f, Vector3.forward);
                }
            }

            // Bins and bags at the end of the tenement frontage.
            {
                var h = Group(g, "Bins and bags");
                var bin = Lit("Wheelie bin", new Color(.25f, .29f, .27f), .35f);
                var lid = Lit("Wheelie bin lid", new Color(.2f, .23f, .22f), .4f);
                var bag = Lit("Rubbish bag", new Color(.045f, .05f, .055f), .78f);
                foreach (var x in new[] { 5.15f, 5.9f })
                {
                    float y = Ground(x, -10.45f);
                    Box("Bin", h, new Vector3(x, y + .52f, -10.45f), new Vector3(.62f, 1.04f, .68f), bin);
                    Box("Bin lid", h, new Vector3(x, y + 1.06f, -10.43f), new Vector3(.66f, .05f, .74f), lid);
                    SnowCap(h, new Vector3(x, y + 1.11f, -10.43f), new Vector3(.64f, .05f, .7f));
                }
                foreach (var (x, z, sx, sy, sz) in new[] { (4.5f, -10.4f, .5f, .4f, .44f), (4.15f, -10.55f, .44f, .36f, .4f), (4.35f, -10.0f, .4f, .3f, .38f), (6.5f, -10.4f, .48f, .38f, .42f) })
                {
                    float y = Ground(x, z);
                    Prim(PrimitiveType.Sphere, "Rubbish bag", h, new Vector3(x, y + sy * .45f, z), new Vector3(sx, sy, sz), Quaternion.Euler(0, x * 40, 0), bag);
                }
                Prim(PrimitiveType.Sphere, "Snow on bag", h, new Vector3(4.5f, Ground(4.5f, -10.4f) + .37f, -10.4f), new Vector3(.36f, .06f, .3f), Quaternion.identity, snow, false);
            }

            // The street's name on the tenement corner, and the neighbourhood's flyers on its wall.
            {
                var h = Group(g, "Street plate and flyers");
                if (WallHit(new Vector3(-9.1f, 3.05f, -9.5f), Vector3.back, out var plate))
                    Decal("Street name plate", h, plate.point, plate.normal, .64f, .25f,
                          Lit("Street plate enamel", Color.white, .5f, 0, Tex("street_plate"), new Color(.1f, .11f, .11f), Tex("street_plate")), .015f);
                if (WallHit(new Vector3(-4.4f, 2.0f, -9.5f), Vector3.back, out var wall))
                    Decal("Flyers", h, wall.point, wall.normal, 1.12f, .84f, Lit("Flyers", Color.white, .1f, 0, Tex("flyers"), mode: Mode.Transparent), .01f);
            }

            // Cables strung across the street between the two fronts.
            {
                var h = Group(g, "Cables across the street");
                var cable = Mat("Charge cable");
                foreach (var (x, dx, sag) in new[] { (-6.3f, .5f, .55f), (1.6f, -.4f, .7f), (7.2f, .3f, .45f) })
                {
                    var a = new Vector3(x, 6.4f, -10.8f);
                    var b = new Vector3(x + dx, 7.5f, WallHit(new Vector3(x + dx, 7.5f, -5f), Vector3.forward, out var hit) ? hit.point.z - .02f : -.1f);
                    Tube("Overhead cable", h, Catenary(a, b, sag, 16), .011f, cable);
                }
            }
        }

        // Layers past the district. Near blocks (under ~50 m) take the scene fog, so they step back into the snow haze;
        // anything further gets a fixed haze so it reads as a shape instead of vanishing.
        static void BuildFarCity(Transform g)
        {
            var blocks = new[]
            {
                (n: "East 1", x: 38f, z: -15f, w: 12f, d: 10f, h: 22f), (n: "East 2", x: 44f, z: 2f, w: 10f, d: 14f, h: 30f),
                (n: "East 3", x: 36f, z: 16f, w: 12f, d: 10f, h: 18f), (n: "East 4", x: 58f, z: -6f, w: 14f, d: 12f, h: 36f),
                (n: "East 5", x: 52f, z: -27f, w: 12f, d: 12f, h: 26f),
                (n: "South 1", x: -4f, z: -30f, w: 16f, d: 9f, h: 19f), (n: "South 2", x: 14f, z: -34f, w: 14f, d: 10f, h: 24f),
                (n: "South 3", x: -20f, z: -30f, w: 12f, d: 10f, h: 15f), (n: "South 4", x: 28f, z: -38f, w: 12f, d: 12f, h: 30f),
                (n: "West 1", x: -58f, z: -20f, w: 12f, d: 10f, h: 20f), (n: "West 2", x: -52f, z: -38f, w: 14f, d: 10f, h: 16f),
                (n: "North 1", x: -30f, z: 50f, w: 16f, d: 10f, h: 22f), (n: "North 2", x: -6f, z: 56f, w: 14f, d: 12f, h: 30f),
                (n: "North 3", x: 18f, z: 46f, w: 12f, d: 10f, h: 18f), (n: "North 4", x: -52f, z: 62f, w: 14f, d: 12f, h: 34f),
            };
            var red = Beacon("Aviation red - mid", AlarmRed, 2.2f, .45f, .4f);
            var tankMat = Lit("Roof tank", new Color(.14f, .16f, .17f), .3f, .2f);
            foreach (var b in blocks)
            {
                float dist = Vector2.Distance(new Vector2(b.x, b.z), new Vector2(-9, -4));
                float haze = dist < 46f ? -1f : Mathf.Clamp01(.3f + (dist - 46f) / 110f);
                var mat = CityMat(haze < 0 ? "City block - scene fog" : "City block - haze " + Mathf.RoundToInt(haze * 10), haze);
                var bg = Group(g, "Block - " + b.n);
                Prim(PrimitiveType.Cube, "Mass", bg, new Vector3(b.x, b.h * .5f, b.z), new Vector3(b.w, b.h, b.d), Quaternion.identity, mat, false);
                float hsh = Hash(new Vector3(b.x, 0, b.z));
                if (hsh < .6f)
                    Cyl("Roof water tank", bg, new Vector3(b.x + b.w * .25f, b.h + 1.3f, b.z - b.d * .2f), 1.1f, 2f, haze < 0 ? tankMat : mat);
                if (b.h >= 24f)
                {
                    Rod("Roof mast", bg, new Vector3(b.x - b.w * .3f, b.h, b.z + b.d * .25f), new Vector3(b.x - b.w * .3f, b.h + 7f, b.z + b.d * .25f), .08f, haze < 0 ? tankMat : mat);
                    Prim(PrimitiveType.Sphere, "Aviation lamp", bg, new Vector3(b.x - b.w * .3f, b.h + 7.2f, b.z + b.d * .25f), Vector3.one * .5f, Quaternion.identity, red, false);
                }
            }
            // A rooftop billboard on the tallest east block, facing back up the street.
            {
                var bg = Group(g, "Rooftop billboard - East 2");
                var frame = Lit("Billboard frame", new Color(.12f, .13f, .14f), .3f, .4f);
                for (int i = -1; i <= 1; i += 2)
                    Box("Billboard leg", bg, new Vector3(39.2f, 31.6f, 2f + i * 3.4f), new Vector3(.3f, 3.2f, .3f), frame);
                Decal("Billboard", bg, new Vector3(38.9f, 35.2f, 2f), Vector3.left, 9f, 3.6f, Holo("Holo - corporate band near", "holo_corp_band", 2.4f, -1, .12f), 0f);
            }

            // The elevated railway beyond the harbour, and a train of lit windows on it.
            {
                var vg = Group(g, "Elevated railway");
                var mass = FarMass("Viaduct mass", .42f);
                var lamp = Beacon("Viaduct lamp", new Color(.86f, .86f, .8f), 1.6f, 0, 1, .38f);
                const float vx = -112f, deck = 20f;
                Prim(PrimitiveType.Cube, "Deck", vg, new Vector3(vx, deck, 0), new Vector3(8f, 1.6f, 200f), Quaternion.identity, mass, false);
                for (float z = -96f; z <= 96f; z += 18f)
                {
                    Prim(PrimitiveType.Cube, "Pier", vg, new Vector3(vx, deck * .5f - .4f, z), new Vector3(2.4f, deck - .8f, 2.4f), Quaternion.identity, mass, false);
                    foreach (var s in new[] { -1, 1 })
                        Prim(PrimitiveType.Sphere, "Deck lamp", vg, new Vector3(vx + s * 4.1f, deck + 1.4f, z), Vector3.one * .6f, Quaternion.identity, lamp, false);
                }
                var band = Beacon("Train window band", new Color(.72f, .8f, .8f), 1.3f, 0, 1, .4f);
                for (int c = 0; c < 5; c++)
                {
                    float z = -12f + c * 21f;
                    Prim(PrimitiveType.Cube, "Train car", vg, new Vector3(vx - 1.6f, deck + 2.5f, z), new Vector3(3.2f, 3.4f, 20f), Quaternion.identity, mass, false);
                    Prim(PrimitiveType.Cube, "Train windows", vg, new Vector3(vx - 1.6f, deck + 2.9f, z), new Vector3(3.26f, .7f, 18.4f), Quaternion.identity, band, false);
                }
            }
        }

        static Material CityMat(string name, float haze)
        {
            if (mats.TryGetValue(name, out var have)) return have;
            var m = new Material(Shader.Find("BorderRepair/Exterior/CityBlock"));
            m.SetColor("_Color", new Color(.07f, .09f, .1f));
            m.SetColor("_Warm", Amber);
            m.SetColor("_Screen", HoloCyan);
            m.SetColor("_Neutral", new Color(.5f, .51f, .48f));   // dim: at this range a bright neutral window reads as a white tile
            m.SetFloat("_Lit", .07f);
            m.SetFloat("_Gain", .95f);
            m.SetFloat("_Haze", haze);
            return Save(name, m);
        }

        static Material FarMass(string name, float haze)
        {
            if (mats.TryGetValue(name, out var have)) return have;
            var m = new Material(Shader.Find("BorderRepair/Exterior/FarSilhouette"));
            m.SetColor("_Color", new Color(.05f, .07f, .08f));
            m.SetFloat("_Haze", haze);
            m.SetFloat("_WindowGain", 0);
            return Save(name, m);
        }
    }
}
