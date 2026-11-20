using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.Tests
{
    // Read only: where the joined scene's colour comes from. Every light, every material with a saturated or HDR colour
    // (by area: the B1, the street, the far view), every volume's overridden settings, the fog and ambient indoors and
    // out, and frames through the player's eye (with the clinic's filters) from the B1 and the street.
    public sealed partial class Berth04PlayableTests
    {
        public static readonly (string name, Vector3 feet, float yaw, float pitch)[] ColourViewsB1 =
        {
            ("B1 - at the counter", new Vector3(.1f, .05f, -.15f), 180, 8),
            ("B1 - at the bench", new Vector3(-.4f, .05f, .5f), 270, 12),
            ("B1 - toward UNIT 07", new Vector3(0, .05f, .3f), 0, 6),
            ("B1 - toward the door", new Vector3(.6f, .05f, 1.4f), 190, 3),
            ("B1 - in from the door", new Vector3(.25f, .05f, -2.85f), 15, 4),
            ("B1 - waiting side", new Vector3(1.4f, .05f, -2.6f), 75, 6),
            ("B1 - surgery bay", new Vector3(3.0f, .05f, -.5f), 40, 14),
            ("B1 - workshop back to the gate", new Vector3(.9f, .05f, 1.9f), 215, 8),
        };

        static float Hue(Color c) { Color.RGBToHSV(c, out var h, out _, out _); return h * 360; }
        static float Sat(Color c) { Color.RGBToHSV(c, out _, out var s, out _); return s; }
        static string Hex(Color c) { var m = Mathf.Max(1, c.maxColorComponent); return "#" + ColorUtility.ToHtmlStringRGB(c / m) + (m > 1.01f ? " x" + m.ToString("F2") : ""); }
        public static string Family(Color c)
        {
            float s = Sat(c), h = Hue(c);
            if (s < .22f) return "neutral";
            if (h < 15 || h >= 345) return "red";
            if (h < 48) return "amber/orange";
            if (h < 70) return "yellow";
            if (h < 160) return "green";
            if (h < 200) return "cyan";
            if (h < 255) return "blue";
            return "magenta/purple";
        }

        [UnityTest, Explicit("diagnostic dump; run by name"), Timeout(900000)]
        public IEnumerator Diagnose_Colour()
        {
            for (int i = 0; i < 30; i++) yield return null;
            var join = Berth04Playable.Current.Join;
            var room = ClinicLayout.Active.room; room.Expand(.3f);
            var door = new Vector3(0, 1.3f, -12f);
            string Area(Vector3 p) => room.Contains(p) ? "B1" : new Vector2(p.x - door.x, p.z - door.z).magnitude < 70 ? "street" : "far";
            var sb = new StringBuilder();

            sb.Append("== lights (enabled)\n");
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.isActiveAndEnabled)
                         .OrderBy(l => Area(l.transform.position)).ThenBy(l => P(l.transform)))
                sb.Append("  ").Append(Area(l.transform.position)).Append(" | ").Append(P(l.transform)).Append(" [").Append(l.gameObject.scene.name).Append("] ")
                  .Append(l.type).Append(' ').Append(Hex(l.color)).Append(" (").Append(Family(l.color)).Append(") i ").Append(l.intensity.ToString("F2"))
                  .Append(" r ").Append(l.range.ToString("F1")).Append(l.type == LightType.Spot ? " spot " + l.spotAngle.ToString("F0") : "")
                  .Append(" shadows ").Append(l.shadows).Append(" at ").Append(V(l.transform.position)).Append('\n');

            // Materials with a saturated or HDR colour, by area.
            sb.Append("== coloured materials (any colour property saturated > .3 or brighter than 1), by area\n");
            var seen = new Dictionary<(string, Material), (int n, float area, string where)>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer) continue;
                var b = r.bounds;
                string a = Area(b.center);
                float face = Mathf.Max(b.size.x * b.size.y, b.size.z * b.size.y, b.size.x * b.size.z);
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    var key = (a, m);
                    seen.TryGetValue(key, out var s);
                    seen[key] = (s.n + 1, s.area + face, s.where ?? P(r.transform));
                }
            }
            foreach (var g in seen.OrderBy(k => k.Key.Item1).ThenByDescending(k => k.Value.area))
            {
                var m = g.Key.Item2;
                var parts = new List<string>();
                var sh = m.shader;
                for (int i = 0; i < sh.GetPropertyCount(); i++)
                {
                    if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
                    var name = sh.GetPropertyName(i);
                    var c = m.GetColor(name);
                    if (name == "_EmissionColor" && !m.IsKeywordEnabled("_EMISSION")) continue;
                    if (Sat(c) > .3f && c.maxColorComponent > .06f || c.maxColorComponent > 1.01f)
                        parts.Add(name + " " + Hex(c) + " " + Family(c));
                }
                foreach (var f in new[] { "_Intensity", "_Glow", "_Haze", "_Lit" })
                    if (m.HasProperty(f)) parts.Add(f + " " + m.GetFloat(f).ToString("F2"));
                if (parts.Count(p => p.Contains("#")) == 0) continue;
                sb.Append("  ").Append(g.Key.Item1).Append(" | ").Append(m.name).Append(" / ").Append(sh.name).Append(" | ").Append(g.Value.n).Append(" r, face ")
                  .Append(g.Value.area.ToString("F1")).Append(" m2 | ").Append(string.Join("; ", parts)).Append(" | e.g. ").Append(g.Value.where).Append('\n');
            }

            sb.Append("== volumes (overridden settings)\n");
            object G(object o, string n) { if (o == null) return null; var t = o.GetType(); var pr = t.GetProperty(n); if (pr != null) return pr.GetValue(o); return t.GetField(n)?.GetValue(o); }
            bool IsParam(Type t) { for (; t != null; t = t.BaseType) if (t.Name == "VolumeParameter") return true; return false; }
            foreach (var v in Object.FindObjectsByType<Behaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(x => x != null && x.GetType().Name == "Volume"))
            {
                var prof = (G(v, "profile") ?? G(v, "sharedProfile")) as ScriptableObject;
                sb.Append("  ").Append(P(v.transform)).Append(" [").Append(v.gameObject.scene.name).Append("] active ").Append(v.isActiveAndEnabled).Append(" layer ").Append(v.gameObject.layer)
                  .Append(" global ").Append(G(v, "isGlobal")).Append(" prio ").Append(G(v, "priority")).Append(" w ").Append(G(v, "weight")).Append(" profile ").Append(prof ? prof.name : "null").Append('\n');
                if (!(G(prof, "components") is IEnumerable comps)) continue;
                foreach (var c in comps)
                {
                    var on = G(c, "active");
                    var ps = c.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => IsParam(f.FieldType))
                        .Select(f => (f.Name, p: f.GetValue(c))).Where(x => x.p != null && Equals(G(x.p, "overrideState"), true))
                        .Select(x => { var val = G(x.p, "value"); return x.Name + "=" + (val is Color col ? Hex(col) : val is float fl ? fl.ToString("F2") : val?.ToString()); });
                    sb.Append("    ").Append(c.GetType().Name).Append(Equals(on, false) ? " (off)" : "").Append(": ").Append(string.Join(", ", ps)).Append('\n');
                }
            }

            var eye = join.Eye;
            sb.Append("== eye camera ").Append(P(eye.transform)).Append(" components: ").Append(string.Join(", ", eye.GetComponents<Behaviour>().Where(b => b != null).Select(b => b.GetType().Name + (b.enabled ? "" : "-off")))).Append('\n');
            void Atmos(string where)
            {
                sb.Append("  ").Append(where).Append(": fog ").Append(RenderSettings.fog).Append(' ').Append(RenderSettings.fogMode).Append(' ').Append(RenderSettings.fogDensity.ToString("F4")).Append(' ').Append(Hex(RenderSettings.fogColor))
                  .Append(" ambient ").Append(RenderSettings.ambientMode).Append(' ').Append(Hex(RenderSettings.ambientSkyColor)).Append('/').Append(Hex(RenderSettings.ambientEquatorColor)).Append('/').Append(Hex(RenderSettings.ambientGroundColor))
                  .Append(" light ").Append(Hex(RenderSettings.ambientLight)).Append('\n');
            }

            sb.Append("== atmosphere\n");
            foreach (var (name, feet, yaw, pitch) in ColourViewsB1)
            {
                join.Walker.PlaceAt(feet, yaw, pitch);
                for (int i = 0; i < 4; i++) yield return null;
                if (name == ColourViewsB1[0].name) Atmos("in the B1");
                Berth04Measure.Frame(eye, "colour - " + Berth04Measure.Safe(name) + ".jpg");
            }
            foreach (var v in join.Outdoor.reviewViews.Where(v => v.position.y > .5f && v.position.y < 10 && !v.name.StartsWith("Counter") && !v.name.StartsWith("Main Camera")))
            {
                var e = v.rotation.eulerAngles;
                join.Walker.PlaceAt(v.position - Vector3.up * 1.55f, e.y, Mathf.DeltaAngle(0, e.x));
                for (int i = 0; i < 4; i++) yield return null;
                Berth04Measure.Frame(eye, "colour - street - " + Berth04Measure.Safe(v.name) + ".jpg");
            }
            Atmos("in the street");
            Berth04Measure.Append("colour.txt", sb.ToString());
        }

        // Read only: the same views through the player's eye with the clinic's PS2 pass on (as played) and off. Review
        // captures render offscreen, which the pass skips unless asked; this asks for the length of the test.
        [UnityTest, Explicit("look-dev captures; run by name"), Timeout(900000)]
        public IEnumerator Diagnose_PS2Look()
        {
            for (int i = 0; i < 30; i++) yield return null;
            var join = Berth04Playable.Current.Join;
            var eye = join.Eye;
            var effect = eye.GetComponents<Behaviour>().FirstOrDefault(b => b != null && b.GetType().Name == "RetroPS2CameraEffect");
            var flag = effect?.GetType().GetField("IncludeOffscreenCaptures", BindingFlags.Public | BindingFlags.Static);
            Assert.That(flag, Is.Not.Null, "PS2 pass on the eye");
            var shots = new List<(string name, Vector3 feet, float yaw, float pitch)>(ColourViewsB1);
            shots.Add(("detail - counter corner, heater and ashtray", new Vector3(-.5f, .05f, -.05f), 222, 30));
            shots.Add(("detail - high window from the counter", new Vector3(1.6f, .05f, -1.2f), 155, -12));
            shots.Add(("detail - street, box by the clinic wall", new Vector3(3.0f, 1.35f, -8.0f), 22, 18));
            shots.Add(("detail - street, bin and box across the road", new Vector3(-1.2f, 1.35f, -10.6f), 180, 16));
            shots.Add(("detail - back room door beside UNIT07", new Vector3(1.75f, .05f, -.6f), 8, 8));
            shots.Add(("detail - back room, inside from the door", new Vector3(1.9f, .05f, 3.05f), 330, 14));
            shots.Add(("detail - back room, the cot and the bulb", new Vector3(.4f, .05f, 3.2f), 95, 12));
            shots.Add(("detail - street, clinic side west", new Vector3(-6.5f, 1.35f, -11.8f), 30, 6));
            shots.Add(("detail - street, clinic side east", new Vector3(9.0f, 1.35f, -11.8f), 25, 6));
            shots.Add(("detail - street, the pole line", new Vector3(-12f, 1.35f, -12.5f), 82, -12));
            shots.Add(("detail - harbour, the cranes across the basin", new Vector3(-46f, 1.35f, -7f), 318, -12));
            foreach (var v in join.Outdoor.reviewViews.Where(v => v.position.y > .5f && v.position.y < 10 && !v.name.StartsWith("Counter") && !v.name.StartsWith("Main Camera")))
            {
                var e = v.rotation.eulerAngles;
                shots.Add(("street - " + v.name, v.position - Vector3.up * 1.55f, e.y, Mathf.DeltaAngle(0, e.x)));
            }
            try
            {
                foreach (var (name, feet, yaw, pitch) in shots)
                {
                    join.Walker.PlaceAt(feet, yaw, pitch);
                    for (int i = 0; i < 4; i++) yield return null;
                    flag.SetValue(null, true);
                    Berth04Measure.Frame(eye, "ps2 on - " + Berth04Measure.Safe(name) + ".jpg");
                    flag.SetValue(null, false);
                    Berth04Measure.Frame(eye, "ps2 off - " + Berth04Measure.Safe(name) + ".jpg");
                }
            }
            finally { flag.SetValue(null, false); }
        }
    }
}
