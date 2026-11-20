using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.EditorTools
{
    // Night details for the playable copy (Docs/Art/INTERIOR_B1_PASS.md section 25.9): a few half-buried boxes and rusted
    // bins at the street's pole feet, snow falling past the B1's high window with a faint cold sky light through it, and
    // the night clerk's things at the counter (a small electric heater with a glowing orange tube, a tin ashtray with a
    // thin thread of smoke) against the cold outside. Nothing here has a collider; nothing is on a walk route.
    public static partial class ExteriorDistrictPass
    {
        static void NightDetails(Transform holder)
        {
            var root = new GameObject("Night details (generated)").transform;
            root.SetParent(holder, true);
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderers = holder.GetComponentsInChildren<Renderer>(true);
            var made = new List<string>();
            var snow = SnowMaterial();
            var puff = Particles("Steam puff", Tex("steam_puff"));

            // -- Street: at the feet of the poles, a box half sunk in the snow or a rusted bin with snow on its lid.
            {
                var g = Group(root, "Street - boxes and bins in the snow");
                var card = Lit("Night - wet cardboard", new Color(.34f, .27f, .2f), .2f);
                var tape = Lit("Night - parcel tape, old", new Color(.42f, .36f, .26f), .4f);
                var bin = Worn("Night - bin, rusted steel", Hex("#4E433B"), .3f, .5f);
                var binLid = Worn("Night - bin lid, rusted", Hex("#3A332E"), .3f, .5f);
                // Anchors: the pole and the light standards at the kerb (the item goes beside them, along the street), the
                // down pipes at the wall corners (the item stands off the wall, toward the street's middle at z -9).
                var anchors = new List<(Renderer r, bool wall)>();
                foreach (var r in renderers.Where(r => !(r is ParticleSystemRenderer) && !B1Room.Contains(r.bounds.center) && r.bounds.size.y > 2.5f
                                                       && (r.name == "Pole - lower" || r.name == "Municipal light standard" || r.name == "Drainpipe" || r.name == "Down pipe" && r.bounds.center.x > 3f))
                                          .OrderBy(r => r.bounds.center.x))
                    if (anchors.All(q => Vector2.Distance(new Vector2(q.r.bounds.center.x, q.r.bounds.center.z), new Vector2(r.bounds.center.x, r.bounds.center.z)) > 1f))
                        anchors.Add((r, r.name.Contains("pipe")));
                int i = 0;
                foreach (var (pole, wall) in anchors)
                {
                    var b = pole.bounds;
                    float side = i % 2 == 0 ? 1 : -1;
                    var foot = wall ? new Vector3(b.center.x + side * .35f, b.min.y, b.center.z + Mathf.Sign(-9f - b.center.z) * .38f)
                                    : new Vector3(b.center.x + side * (b.extents.x + .32f), b.min.y, b.center.z);
                    if (i % 2 == 0)
                    {
                        var rot = Quaternion.Euler(0, 17 + i * 23, 0);
                        Box("Box, half in the snow", g, foot + Vector3.up * .08f, new Vector3(.46f, .34f, .38f), card, rot);
                        Box("Tape", g, foot + Vector3.up * .252f, new Vector3(.05f, .006f, .385f), tape, rot);
                        Box("Snow on the box", g, foot + Vector3.up * .268f, new Vector3(.4f, .035f, .34f), snow, rot * Quaternion.Euler(3, 0, -2));
                        Mound("Snow drift against the box", g, foot + new Vector3(-side * .1f, -.03f, .04f), new Vector3(.82f, .34f, .7f), snow);
                    }
                    else
                    {
                        Cyl("Bin", g, foot + Vector3.up * .24f, .2f, .5f, bin);
                        Cyl("Bin lid", g, foot + Vector3.up * .5f, .215f, .03f, binLid);
                        Cyl("Snow on the lid", g, foot + Vector3.up * .525f, .19f, .03f, snow);
                        Mound("Snow drift at the bin", g, foot + new Vector3(side * .06f, -.04f, .05f), new Vector3(.75f, .3f, .7f), snow);
                    }
                    made.Add((i % 2 == 0 ? "box" : "bin") + " at " + pole.name + " " + foot.ToString("F1"));
                    i++;
                }
                foreach (var r in g.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            }

            // -- The B1's high window: snow falling past it outside, and a faint cold sky light in through it toward the
            //    waiting side (outside the room; the street lamp's own cone, recoloured, already reaches the counter).
            var window = holder.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Street window - right of the gate");
            if (window != null)
            {
                var glass = window.GetComponentsInChildren<Renderer>(true).Where(r => r.name.Contains("lass") || r.name.Contains("Pane") || r.name.Contains("pane")).ToList();
                var wb = glass.Count > 0 ? glass.Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; }) : BoundsOf(window);
                var g = Group(root, "High window - snow and cold sky");
                var flakes = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(p => p.name == "Street and open stairwell");
                var flakeMat = flakes != null ? flakes.GetComponent<ParticleSystemRenderer>().sharedMaterial : puff;
                Snowfall(g, new Vector3(wb.center.x, wb.max.y + .9f, wb.min.z - .45f), new Vector3(wb.size.x + .6f, .05f, .6f), flakeMat);
                var cold = Color.Lerp(HoloCyan, FogTeal, .5f);
                var target = new Vector3(3.6f, .7f, -2.4f);
                var from = new Vector3(wb.center.x + .4f, wb.max.y + 1.4f, wb.min.z - 1.6f);
                var sky = AddLight(g, "High window - cold sky toward the waiting side", from, LightType.Spot, cold, 3.2f, 7f, false);
                sky.transform.rotation = Quaternion.LookRotation(target - from);
                sky.spotAngle = 60; sky.innerSpotAngle = 30;
                made.Add("high window " + wb.center.ToString("F2") + " glass " + glass.Count + ", snow " + (flakes != null ? "street flakes" : "puff"));
            }
            else made.Add("high window not found");

            // -- The counter: the night clerk's heater on the floor at the counter's left end, and an ashtray on the top.
            var mat = renderers.FirstOrDefault(r => r.name == "Mat - behind the counter");
            if (mat != null)
            {
                var m = mat.bounds;
                var g = Group(root, "Counter - night shift heater and ashtray");
                var body = Worn("Night - heater body, cream enamel", Hex("#8C8370"), .35f, .2f);
                var grille = Worn("Night - heater grille, chrome", Hex("#8E8F8A"), .6f, .9f);
                var tube = Lit("Night - heater tube, glowing", new Color(.5f, .2f, .1f), .5f, 0, null, new Color(1f, .42f, .16f) * 2.6f);
                var rot = Quaternion.LookRotation(new Vector3(.75f, 0, .66f).normalized);   // turned toward where the clerk stands
                var at = new Vector3(m.min.x - .05f, m.min.y, m.min.z - .02f);
                Vector3 P(float x, float y, float z) => at + rot * new Vector3(x, y, z);
                BBox("Heater body", g, P(0, .2f, 0), new Vector3(.3f, .36f, .14f), body, rot);
                Box("Heater feet", g, P(0, .012f, 0), new Vector3(.34f, .024f, .2f), body, rot);
                for (int k = 0; k < 2; k++)
                    Cyl("Heater tube " + (k + 1), g, P(0, .14f + k * .1f, .075f), .014f, .24f, tube, rot * Quaternion.Euler(0, 0, 90));
                for (int k = 0; k < 5; k++)
                    Box("Grille bar", g, P(-.11f + k * .055f, .19f, .09f), new Vector3(.006f, .27f, .006f), grille, rot);
                var glow = AddLight(g, "Heater tube glow", P(0, .2f, .3f), LightType.Point, new Color(1f, .45f, .2f), .9f, 1.8f, false);
                made.Add("heater at " + at.ToString("F2"));

                // The ashtray on the counter top, above the heater's end.
                // (the counter is the clinic scene's; the thermos the interior pass stood on its top is here)
                var thermos = renderers.FirstOrDefault(r => r.name == "Thermos" && B1Room.Contains(r.bounds.center));
                if (thermos != null)
                {
                    var c = thermos.bounds;
                    var tin = Worn("Night - ashtray, pressed tin", Hex("#6E706C"), .55f, .8f);
                    var ash = Lit("Night - ash", new Color(.22f, .21f, .2f), .1f);
                    var paper = Lit("Night - cigarette paper", new Color(.78f, .76f, .7f), .2f);
                    var ember = Beacon("Night - cigarette ember", new Color(1f, .42f, .16f), 1.8f, .4f, .8f);
                    var tray = new Vector3(c.center.x + .2f, c.min.y, c.center.z + .06f);
                    Cyl("Ashtray", g, tray + Vector3.up * .011f, .055f, .022f, tin);
                    Cyl("Ash", g, tray + Vector3.up * .02f, .043f, .004f, ash);
                    var tip = tray + new Vector3(.05f, .03f, .01f);
                    Rod("Cigarette", g, tray + new Vector3(-.015f, .024f, 0), tip, .0042f, paper);
                    Sphere("Ember", g, tip, .0046f, ember);
                    Smoke(g, tip + Vector3.up * .006f, puff);
                    made.Add("ashtray by the thermos at " + tray.ToString("F2"));
                }
                else made.Add("thermos not found (no ashtray)");
            }
            else made.Add("counter mat not found");

            log.Append("night details: ").Append(string.Join("; ", made)).Append('\n');
        }

        // A soft drift: a flattened sphere sunk into the ground, so things look half buried rather than set on a slab.
        static void Mound(string name, Transform parent, Vector3 centre, Vector3 size, Material snow)
        {
            var go = Sphere(name, parent, centre, .5f, snow);
            go.transform.localScale = size;
        }

        static void Snowfall(Transform parent, Vector3 centre, Vector3 box, Material mat)
        {
            var go = new GameObject("Snow falling past the window");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(centre, Quaternion.identity);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.playOnAwake = true; main.duration = 4;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.6f, 3.4f);
            main.startSpeed = 0;
            main.startSize = new ParticleSystem.MinMaxCurve(.012f, .028f);
            main.startColor = new Color(.85f, .9f, .94f, .85f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            var em = ps.emission; em.rateOverTime = 32;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = box;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(.15f, .35f); vel.y = new ParticleSystem.MinMaxCurve(-.55f, -.4f); vel.z = new ParticleSystem.MinMaxCurve(-.05f, .05f);
            var noise = ps.noise; noise.enabled = true; noise.strength = .12f; noise.frequency = .6f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard; r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
        }

        static void Smoke(Transform parent, Vector3 pos, Material mat)
        {
            var go = new GameObject("Smoke - cigarette");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.up));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.playOnAwake = true; main.duration = 4;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.03f, .06f);
            main.startSize = new ParticleSystem.MinMaxCurve(.018f, .03f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f);
            main.startColor = new Color(.7f, .72f, .74f, .16f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 30;
            var em = ps.emission; em.rateOverTime = 7;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 4; shape.radius = .002f;
            var colour = ps.colorOverLifetime; colour.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                         new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(0, 1) });
            colour.color = grad;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .7f, 1, 3.2f));
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(.01f); vel.y = new ParticleSystem.MinMaxCurve(.11f); vel.z = new ParticleSystem.MinMaxCurve(0f);
            var noise = ps.noise; noise.enabled = true; noise.strength = .03f; noise.frequency = 1.2f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard; r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
        }
    }
}
