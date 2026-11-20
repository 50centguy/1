using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // The street's two sides in balance (Docs/Art/INTERIOR_B1_PASS.md section 25.12).
    //   - The utility line: the one pole's wires ran 14 m west and 18 m east and ended in the air. Two more poles of the
    //     same make carry them (no transformer on these), so the pole reads as one of a line.
    //   - The clinic's side (north) had the entrance and bare wall either side, against the shops, stall and pharmacy
    //     across the road. Ordinary things on it, against the wall and out of the walk: a gas meter and its pipes, stacked
    //     crates and a pallet left of the entrance; a meter box, three wheelie bins, a scooter under a tarp east of the
    //     shutter; two wall air-conditioner units overhead. No signs, no new light.
    public static partial class ExteriorDistrictPass
    {
        static void StreetSide(Transform holder)
        {
            var done = new List<string>();
            var all = holder.GetComponentsInChildren<Transform>(true);
            var root = new GameObject("Street side (generated)").transform;
            root.SetParent(holder, true);
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // -- Poles at the wires' ends.
            var line = all.FirstOrDefault(t => t.name.StartsWith("Utility pole and the wires", StringComparison.Ordinal));
            if (line != null)
            {
                var parts = line.GetComponentsInChildren<Renderer>(true).Where(r => !r.name.StartsWith("Wire", StringComparison.Ordinal) && !r.name.StartsWith("Transformer", StringComparison.Ordinal)).ToArray();
                var wires = line.GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("Wire", StringComparison.Ordinal) && r.bounds.size.x > 10).ToArray();
                var pole = parts.First(r => r.name == "Pole - lower").bounds.center;
                var ends = new[] { wires.Min(r => r.bounds.min.x), wires.Max(r => r.bounds.max.x) };
                foreach (var x in ends)
                {
                    var g = Group(root, "Utility pole - carries the wires' end at x " + x.ToString("F1"));
                    foreach (var r in parts)
                    {
                        var copy = Object.Instantiate(r.gameObject, g);
                        copy.name = r.name;
                        copy.transform.SetPositionAndRotation(r.transform.position + new Vector3(x - pole.x, 0, 0), r.transform.rotation);
                        copy.transform.localScale = r.transform.lossyScale;
                    }
                    done.Add("pole at x " + x.ToString("F1"));
                }
            }

            // -- The clinic's side.
            var snow = SnowMaterial();
            var crate = Lit("Street - plastic crate, faded blue-grey", Hex("#4A5A63"), .35f);
            var crateRed = Lit("Street - plastic crate, faded brick", Hex("#5E463E"), .35f);
            var wood = Lit("Street - pallet wood, grey", Hex("#5B5348"), .2f);
            var meter = Worn("Street - meter box, grey enamel", Hex("#6B7378"), .35f, .3f);
            var pipe = Worn("Street - gas pipe, yellow, faded", Hex("#8C7A4A"), .3f, .4f);
            var steel = Worn("Street - dark steel", Hex("#2E3438"), .4f, .6f);
            var binGreen = Lit("Street - wheelie bin, faded green-grey", Hex("#3F4B44"), .3f);
            var binGrey = Lit("Street - wheelie bin, grey", Hex("#4C5054"), .3f);
            var tarp = Lit("Street - tarp, old blue-grey", Hex("#3B4650"), .45f);
            var tyre = Lit("Street - tyre", Hex("#161819"), .2f);
            var acBody = Worn("Street - AC unit, yellowed", Hex("#8E8A7C"), .3f, .2f);
            const float ground = 1.36f, wall = -3.76f;
            float Front(float depth) => wall - depth * .5f - .02f;
            var side = Group(root, "Clinic side - things against the wall");
            var solid = new List<GameObject>();

            // West of the entrance (flank wall x -6..-2.3).
            {
                var m = Box("Gas meter cabinet", side, new Vector3(-5.6f, ground + 1.05f, Front(.3f)), new Vector3(.62f, .82f, .3f), meter);
                Box("Snow on the cabinet", side, new Vector3(-5.6f, ground + 1.475f, Front(.32f)), new Vector3(.64f, .04f, .34f), snow);
                Rod("Gas pipe - riser", side, new Vector3(-5.82f, ground, wall - .08f), new Vector3(-5.82f, ground + .64f, wall - .08f), .03f, pipe);
                Rod("Gas pipe - up the wall", side, new Vector3(-5.38f, ground + 1.46f, wall - .07f), new Vector3(-5.38f, ground + 3.6f, wall - .07f), .03f, pipe);
                Rod("Gas pipe - along", side, new Vector3(-5.38f, ground + 3.6f, wall - .07f), new Vector3(-3.1f, ground + 3.6f, wall - .07f), .03f, pipe);
                for (int k = 0; k < 3; k++)
                    solid.Add(Box("Plastic crate", side, new Vector3(-4.55f + (k == 2 ? .04f : 0), ground + .15f + k * .3f, Front(.4f)), new Vector3(.55f, .29f, .4f), k == 1 ? crateRed : crate, Quaternion.Euler(0, k * 4f, 0)));
                Box("Snow on the crates", side, new Vector3(-4.52f, ground + .925f, Front(.4f)), new Vector3(.5f, .05f, .36f), snow);
                Mound("Snow drift at the crates", side, new Vector3(-4.6f, ground - .04f, Front(.4f) - .2f), new Vector3(.9f, .26f, .6f), snow);
                solid.Add(Box("Pallet, leaning", side, new Vector3(-3.55f, ground + .55f, wall - .2f), new Vector3(1.0f, 1.15f, .12f), wood, Quaternion.Euler(-14f, 0, 0)));
                Mound("Snow drift at the pallet", side, new Vector3(-3.55f, ground - .04f, wall - .45f), new Vector3(1.1f, .22f, .5f), snow);
            }
            // East of the shutter (flank wall x 10.6..15).
            {
                Box("Meter box", side, new Vector3(7.25f, ground + 1.35f, Front(.22f)), new Vector3(.45f, .6f, .22f), meter);
                Rod("Meter conduit", side, new Vector3(7.25f, ground + 1.65f, wall - .05f), new Vector3(7.25f, ground + 3.9f, wall - .05f), .025f, steel);
                float[] xs = { 11.0f, 11.75f, 12.5f };
                for (int k = 0; k < xs.Length; k++)
                {
                    var bin = k == 1 ? binGrey : binGreen;
                    var c = new Vector3(xs[k], ground + .5f, Front(.72f));
                    solid.Add(Box("Wheelie bin", side, c, new Vector3(.62f, 1.0f, .72f), bin, Quaternion.Euler(0, (k - 1) * 3f, 0)));
                    Box("Wheelie bin lid", side, c + new Vector3(0, .53f, -.02f), new Vector3(.66f, .05f, .78f), bin, Quaternion.Euler(k == 2 ? -6f : 0, (k - 1) * 3f, 0));
                    Box("Snow on the lid", side, c + new Vector3(0, .575f, -.02f), new Vector3(.58f, .04f, .7f), snow);
                    Cyl("Wheel", side, c + new Vector3(-.22f, -.42f, .3f), .08f, .04f, tyre, Quaternion.Euler(0, 0, 90));
                    Cyl("Wheel", side, c + new Vector3(.22f, -.42f, .3f), .08f, .04f, tyre, Quaternion.Euler(0, 0, 90));
                }
                Mound("Snow drift along the bins", side, new Vector3(11.75f, ground - .05f, Front(.72f) - .45f), new Vector3(2.6f, .24f, .5f), snow);
                // The scooter under its tarp, parked along the wall.
                var sc = new Vector3(14.0f, ground, wall - .45f);
                solid.Add(Box("Scooter under a tarp - body", side, sc + new Vector3(0, .55f, 0), new Vector3(1.5f, .62f, .5f), tarp, Quaternion.Euler(0, 0, 2f)));
                Box("Scooter under a tarp - handlebars", side, sc + new Vector3(-.6f, 1.0f, 0), new Vector3(.3f, .32f, .7f), tarp, Quaternion.Euler(0, 0, -12f));
                Box("Tarp hem", side, sc + new Vector3(.05f, .25f, 0), new Vector3(1.62f, .1f, .56f), tarp);
                Cyl("Tyre", side, sc + new Vector3(-.58f, .2f, 0), .2f, .06f, tyre, Quaternion.Euler(90, 0, 0));
                Cyl("Tyre", side, sc + new Vector3(.58f, .2f, 0), .2f, .06f, tyre, Quaternion.Euler(90, 0, 0));
                Box("Snow on the tarp", side, sc + new Vector3(.1f, .88f, 0), new Vector3(1.2f, .05f, .42f), snow);
                Mound("Snow drift at the scooter", side, sc + new Vector3(0, -.04f, -.3f), new Vector3(1.8f, .22f, .5f), snow);
            }
            // Air conditioners overhead, one each side.
            foreach (var x in new[] { -4.8f, 12.6f })
            {
                var c = new Vector3(x, ground + 3.1f, Front(.3f) - .05f);
                Box("Wall AC unit", side, c, new Vector3(.8f, .55f, .3f), acBody);
                Cyl("Wall AC unit - fan guard", side, c + new Vector3(-.08f, 0, -.16f), .2f, .01f, steel, Quaternion.Euler(90, 0, 0));
                Box("Wall AC bracket", side, c + new Vector3(-.3f, -.3f, .03f), new Vector3(.04f, .05f, .34f), steel);
                Box("Wall AC bracket", side, c + new Vector3(.3f, -.3f, .03f), new Vector3(.04f, .05f, .34f), steel);
                Box("Snow on the AC", side, c + new Vector3(0, .3f, 0), new Vector3(.78f, .05f, .3f), snow);
                Rod("Condensate drip pipe", side, c + new Vector3(.32f, -.27f, .05f), new Vector3(x + .32f, ground + .1f, wall - .06f), .012f, steel);
            }
            foreach (var go in solid) go.AddComponent<BoxCollider>();
            foreach (var r in side.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            done.Add("clinic side: " + side.GetComponentsInChildren<Renderer>().Length + " parts, " + solid.Count + " with colliders");
            log.Append("street side: ").Append(string.Join("; ", done)).Append('\n');
        }
    }
}
