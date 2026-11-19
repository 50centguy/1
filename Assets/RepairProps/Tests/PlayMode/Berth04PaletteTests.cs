using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.Tests
{
    public sealed partial class Berth04PlayableTests
    {
        // The palette (Docs/Art/PALETTE_BR2049_PS2.md, INTERIOR_B1_PASS.md section 25) holds in the joined scene: nothing
        // magenta anywhere, and the B1's lights are only the two families, cool neutral and amber. The B1's cyber
        // dressing is there, inside the room, off the walk route, and the monitor's trace runs.
        [UnityTest, Timeout(600000)]
        public IEnumerator Palette_TwoFamiliesNoMagenta_AndTheB1CyberDressing()
        {
            for (int i = 0; i < 20; i++) yield return null;
            var log = new StringBuilder("palette:\n");
            var magenta = new List<string>();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.isActiveAndEnabled))
                if (Family(l.color) == "magenta/purple") magenta.Add("light " + l.name);
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer)))
                foreach (var m in r.sharedMaterials.Where(m => m != null))
                    foreach (var p in new[] { "_Color", "_BaseColor", "_EmissionColor" })
                    {
                        if (!m.HasProperty(p) || p == "_EmissionColor" && !m.IsKeywordEnabled("_EMISSION")) continue;
                        var c = m.GetColor(p);
                        if (c.maxColorComponent > .15f && Sat(c) > .35f && Family(c) == "magenta/purple") magenta.Add(r.name + " / " + m.name + " " + p);
                    }
            Assert.That(magenta, Is.Empty, "magenta: " + string.Join("; ", magenta));

            var room = ClinicLayout.Active.room;
            var b1Lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.isActiveAndEnabled && room.Contains(l.transform.position)).ToArray();
            foreach (var l in b1Lights) log.Append("  B1 light ").Append(l.name).Append(' ').Append(Family(l.color)).Append(' ').Append(l.intensity.ToString("F2")).Append('\n');
            var off = b1Lights.Where(l => Family(l.color) != "neutral" && Family(l.color) != "amber/orange").Select(l => l.name + " " + Family(l.color)).ToArray();
            Assert.That(off, Is.Empty, "B1 lights outside cool neutral / amber: " + string.Join("; ", off));
            Assert.That(b1Lights.Count(l => Family(l.color) == "amber/orange"), Is.GreaterThan(3), "the lamps are amber");

            var cyber = GameObject.Find("B1 cyber (generated)");
            Assert.That(cyber, Is.Not.Null, "B1 cyber dressing");
            foreach (var name in new[] { "Holo - queue number over the counter (projector on the ceiling)", "CRT - port notice channel, on the sill cabinet",
                         "Biomonitor - surgery bay wall", "Spare arm on a hook - leased", "Bay power - junction box and cables", "Gate lock panel" })
                Assert.That(cyber.transform.Find(name), Is.Not.Null, name);
            var walkRoute = new[] { (1.95f, -2.3f), (2.0f, -.35f), (.6f, .1f), (-.55f, .5f), (-.2f, 1.2f), (2.1f, -1.4f), (3.2f, -1.4f), (3.2f, .8f), (3.2f, -.3f), (4.45f, .05f), (4.65f, 1.0f), (1.5f, -2.4f), (.2f, -2.4f) };
            foreach (var r in cyber.GetComponentsInChildren<Renderer>())
            {
                Assert.That(room.Contains(r.bounds.center), Is.True, r.name + " in the B1");
                foreach (var (x, z) in walkRoute)
                    Assert.That(r.bounds.Intersects(new Bounds(new Vector3(x, .95f, z), new Vector3(.6f, 1.9f, .6f))), Is.False, r.name + " clear of the walk route at " + x + ", " + z);
            }
            var trace = cyber.GetComponentInChildren<B1Screen>();
            Assert.That(trace, Is.Not.Null, "monitor trace");
            float before = trace.Offset;
            for (float w = 0; w < .3f; w += Time.unscaledDeltaTime) yield return null;
            Assert.That(trace.Offset, Is.Not.EqualTo(before), "the trace runs");
            log.Append("  cyber renderers ").Append(cyber.GetComponentsInChildren<Renderer>().Length).Append('\n');

            // Emitters glow (their _EMISSION keyword is on: section 25's glow fix): the B1's bulbs, tubes, pilot lamp,
            // dial and screens, the street's lamp heads and lightboxes; and the festive bulbs are in the amber family.
            string[] emitters = { "Bulb", "Tube", "Pilot lamp", "Dial", "Screen - ECG", "Screen - vitals", "Readout - 施錠", "Diffuser", "Sodium lamp lens", "Lightbox face" };
            var dark = new List<string>(); int lit = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy && emitters.Contains(r.name)))
            {
                var m = r.sharedMaterial;
                if (m == null || !m.HasProperty("_EmissionColor") || m.GetColor("_EmissionColor").maxColorComponent < .1f) continue;
                if (!m.IsKeywordEnabled("_EMISSION")) { dark.Add(r.name + " / " + m.name + " does not glow"); continue; }
                lit++;
                var family = Family(m.GetColor("_EmissionColor"));
                if (r.name == "Bulb" && room.Contains(r.bounds.center) && family != "amber/orange" && family != "neutral") dark.Add(r.name + " / " + m.name + " " + family);
            }
            Assert.That(dark, Is.Empty, "emitters: " + string.Join("; ", dark));
            Assert.That(lit, Is.GreaterThan(20), "glowing emitters");
            log.Append("  glowing emitters ").Append(lit).Append('\n');
            Berth04Measure.Append("palette.txt", log.ToString());
        }
    }
}
