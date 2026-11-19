using System;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    // Street motion for the Berth 04 district: craft flying their lanes, a failing sodium lamp, the train on the far
    // viaduct, the delivery drone holding station. Everything is a pure function of time (Evaluate), so a clip renderer
    // can step it frame by frame; in play mode Update drives it from Time.time. At t = 0 nothing the scene saves moves
    // off its authored value (lamps full, train and drone at rest), so building the scene with Evaluate(0) is stable.
    [DisallowMultipleComponent]
    public sealed class DistrictMotion : MonoBehaviour
    {
        static readonly int TimeId = Shader.PropertyToID("_DistrictTime");
        static readonly int TimeOnId = Shader.PropertyToID("_DistrictTimeOn");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        // A craft following a Catmull-Rom path for `travel` seconds out of every `period`; hidden the rest of the time.
        [Serializable]
        public class Flyer
        {
            public Transform body;
            public Vector3[] path = new Vector3[0];
            public float travel = 10, period = 30, phase;
            public float bank = 25;
        }

        // A searchlight head: swings side to side about the craft's fore-aft axis (so the pool stays on the ground and
        // crosses the street) and nods fore and aft.
        [Serializable]
        public class Sweep
        {
            public Transform pivot;
            public Quaternion rest = Quaternion.identity;
            public float side = 20, pitch = 6, rate = .25f, phase;
        }

        // Navigation or warning lights: lit for `on` seconds every `period` (twice, with `twin`). Beacon-shader
        // renderers have their _Intensity switched; an optional real light flashes with them.
        [Serializable]
        public class Blinker
        {
            public Renderer[] lamps = new Renderer[0];
            public float lampIntensity = 2;
            public Light light;
            public float lightIntensity;
            public float period = 1, on = .1f, phase;
            public bool twin;
        }

        // A street lamp with a tired ballast. `severity` 1 drops out and restrikes; lower values only stutter.
        [Serializable]
        public class Lamp
        {
            public Light light;
            public float intensity;
            public Color colour = Color.white;
            public Renderer[] glows = new Renderer[0];
            public string[] glowProperty = new string[0];
            public Color[] glowBase = new Color[0];
            public float seed, severity = 1;
        }

        // Parts that run along +Z and wrap (the train); a part is hidden while it is off the visible stretch.
        [Serializable]
        public class Runner
        {
            public Transform[] parts = new Transform[0];
            public float[] rest = new float[0];
            public float speed = 12, wrapAt = 150, length = 300, visibleHalf = 92;
        }

        // Holding station in the wind.
        [Serializable]
        public class Hover
        {
            public Transform body;
            public Vector3 rest;
            public float amplitude = .06f, rate = .5f;
        }

        public Flyer[] flyers = new Flyer[0];
        public Sweep[] sweeps = new Sweep[0];
        public Blinker[] blinkers = new Blinker[0];
        public Lamp[] lamps = new Lamp[0];
        public Runner[] runners = new Runner[0];
        public Hover[] hovers = new Hover[0];

        // Where the district's origin sits in the world. Paths and the train's run are authored in district space; a
        // copy of the district moved to meet another scene (the playable clinic) sets this to its offset.
        public Vector3 origin;

        MaterialPropertyBlock block;

        void Update()
        {
            if (Application.isPlaying) Evaluate(Time.time);
        }

        // The exterior shaders (beacons, holograms, lamp volumes) read _DistrictTime while this is pinned, so a clip
        // rendered faster or slower than real time still blinks at the right rate.
        public static void PinShaderTime(float t)
        {
            Shader.SetGlobalFloat(TimeOnId, 1);
            Shader.SetGlobalFloat(TimeId, t);
        }

        public static void ReleaseShaderTime() => Shader.SetGlobalFloat(TimeOnId, 0);

        public void Evaluate(float t)
        {
            block ??= new MaterialPropertyBlock();
            foreach (var f in flyers) Pose(f, t, origin);
            foreach (var s in sweeps)
            {
                if (s.pivot == null) continue;
                float a = (t * s.rate + s.phase) * Mathf.PI * 2;
                s.pivot.localRotation = Quaternion.AngleAxis(s.side * Mathf.Sin(a), Vector3.forward) * s.rest * Quaternion.Euler(s.pitch * Mathf.Sin(a * .7f + 1.3f), 0, 0);
            }
            foreach (var b in blinkers) Blink(b, t);
            foreach (var l in lamps) Flicker(l, t);
            foreach (var r in runners) Run(r, t, origin.z);
            foreach (var h in hovers)
            {
                if (h.body == null) continue;
                float a = t * h.rate * Mathf.PI * 2;
                h.body.localPosition = h.rest + new Vector3(Mathf.Sin(a * .63f) * h.amplitude * .6f, Mathf.Sin(a) * h.amplitude, Mathf.Sin(a * .41f) * h.amplitude * .4f);
            }
        }

        static void Pose(Flyer f, float t, Vector3 origin)
        {
            if (f.body == null || f.path.Length < 2) return;
            float local = Mathf.Repeat(t + f.phase, Mathf.Max(f.period, f.travel));
            bool show = local <= f.travel;
            if (f.body.gameObject.activeSelf != show) f.body.gameObject.SetActive(show);
            if (!show) return;
            float u = local / f.travel;
            var p = Spline(f.path, u);
            var a = Spline(f.path, Mathf.Max(u - .01f, 0));
            var b = Spline(f.path, Mathf.Min(u + .01f, 1));
            var dir = b - a;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            // Bank into the turn: the change of heading across the sample window.
            var d0 = p - a; var d1 = b - p;
            float turn = Vector3.SignedAngle(new Vector3(d0.x, 0, d0.z), new Vector3(d1.x, 0, d1.z), Vector3.up);
            float roll = Mathf.Clamp(-turn * 6f, -f.bank, f.bank);
            f.body.SetPositionAndRotation(p + origin, Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0, 0, roll));
        }

        static Vector3 Spline(Vector3[] pts, float u)
        {
            int n = pts.Length - 1;
            float x = Mathf.Clamp01(u) * n;
            int i = Mathf.Min((int)x, n - 1);
            float s = x - i;
            var p0 = pts[Mathf.Max(i - 1, 0)]; var p1 = pts[i]; var p2 = pts[i + 1]; var p3 = pts[Mathf.Min(i + 2, n)];
            float s2 = s * s, s3 = s2 * s;
            return .5f * (2 * p1 + (p2 - p0) * s + (2 * p0 - 5 * p1 + 4 * p2 - p3) * s2 + (3 * p1 - p0 - 3 * p2 + p3) * s3);
        }

        void Blink(Blinker b, float t)
        {
            float x = Mathf.Repeat(t + b.phase, b.period);
            bool lit = x < b.on || (b.twin && x >= b.on * 2.2f && x < b.on * 3.2f);
            foreach (var r in b.lamps)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(IntensityId, lit ? b.lampIntensity : 0);
                r.SetPropertyBlock(block);
            }
            if (b.light != null) b.light.intensity = lit ? b.lightIntensity : 0;
        }

        // 1 at t = 0 and through every quiet stretch; a 9 Hz on/off stutter in each 7-second cycle; with severity
        // over .5 some cycles drop out and restrike, sodium-red and dim first.
        public static float Level(float t, float seed, float severity, out float strike)
        {
            strike = 0;
            const float cycle = 7f;
            float k = Mathf.Floor(t / cycle), x = t - k * cycle;
            float h1 = Hash(k * 1.37f + seed), h2 = Hash(k * 2.11f + seed * 3.1f);
            float quiet = 1.4f + 2f * h1;
            if (x < quiet) return 1;
            float burst = severity > .5f ? 1.1f + 1.2f * h2 : .25f + .25f * h2;
            if (x < quiet + burst)
                return Hash(Mathf.Floor(t * 9f) + seed * 7.7f) > .5f ? 1 : .05f;
            if (severity <= .5f || h2 > .7f) return 1;
            float dark = x - quiet - burst;
            if (dark < .9f) return .03f;
            float warm = Mathf.Clamp01((dark - .9f) / 1.6f);
            strike = 1 - warm;
            return Mathf.Lerp(.12f, 1, warm * warm * (3 - 2 * warm));
        }

        void Flicker(Lamp l, float t)
        {
            float level = Level(t, l.seed, l.severity, out float strike);
            if (l.light != null)
            {
                l.light.intensity = l.intensity * level;
                l.light.color = Color.Lerp(l.colour, new Color(1f, .42f, .28f), strike);
            }
            for (int i = 0; i < l.glows.Length; i++)
            {
                var r = l.glows[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                var c = l.glowBase[i] * level;
                if (strike > 0) c = Color.Lerp(c, new Color(c.maxColorComponent, c.maxColorComponent * .4f, c.maxColorComponent * .25f), strike);
                if (l.glowProperty[i] == "_Intensity") block.SetFloat(IntensityId, l.glowBase[i].a * level);
                else block.SetColor(l.glowProperty[i], c);
                r.SetPropertyBlock(block);
            }
        }

        static void Run(Runner r, float t, float originZ)
        {
            float offset = Mathf.Repeat(r.speed * t, r.length);
            for (int i = 0; i < r.parts.Length; i++)
            {
                var p = r.parts[i];
                if (p == null) continue;
                float z = r.rest[i] + offset;
                if (z > r.wrapAt) z -= r.length;
                var pos = p.position;
                if (pos.z != z + originZ) p.position = new Vector3(pos.x, pos.y, z + originZ);
                var rend = p.GetComponent<Renderer>();
                bool vis = Mathf.Abs(z) <= r.visibleHalf;
                if (rend != null && rend.enabled != vis) rend.enabled = vis;
            }
        }

        static float Hash(float x) => Mathf.Abs(Mathf.Sin(x * 12.9898f + 78.233f) * 43758.5453f) % 1f;
    }
}
