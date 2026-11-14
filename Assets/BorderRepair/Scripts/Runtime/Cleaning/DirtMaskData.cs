using System;
using UnityEngine;

namespace BorderRepair.Cleaning
{
    /// <summary>CPU-owned mask: progress measures removed dirt, excluding unused UV texels.</summary>
    public sealed class DirtMaskData
    {
        public int Resolution { get; }
        public Color32[] Pixels { get; }
        readonly float[] initial, remaining;
        readonly byte[] coverage;
        readonly int[] regions;
        double initialMass, remainingMass;
        // Stains on top of the plain dirt: what each stained texel shows (linear rgb).
        Color32[] stain;
        /// <summary>How many texels carry a stain (blood, oil, glue left by a label) rather than plain dirt.</summary>
        public int StainTexels { get; private set; }
        /// <summary>Stain kinds, in order: blood and oil laid down by <see cref="AddStains"/>, glue left by <see cref="AddResidue"/>.
        /// Tape, tags and stickers are not stains: they are objects on the item (RepairPropLabels).</summary>
        public int[] StainCounts { get; } = new int[3];
        public float Progress => initialMass <= 0 ? 1 : Mathf.Clamp01(1 - (float)(remainingMass / initialMass));

        public DirtMaskData(int resolution, byte[] usedTexels, int seed = 23) : this(resolution, usedTexels, seed, null) { }

        /// <summary>Starts from dirt baked per texel by the installer (0 = this texel was never dirty) instead of UV-space noise.</summary>
        public DirtMaskData(int resolution, byte[] usedTexels, byte[] bakedDirt) : this(resolution, usedTexels, 23, bakedDirt) { }

        DirtMaskData(int resolution, byte[] usedTexels, int seed, byte[] bakedDirt)
        {
            if (resolution < 8 || resolution > 512) throw new ArgumentOutOfRangeException(nameof(resolution));
            if (usedTexels == null || usedTexels.Length != resolution * resolution)
                throw new ArgumentException("UV coverage must match mask resolution.", nameof(usedTexels));
            if (bakedDirt != null && bakedDirt.Length != usedTexels.Length)
                throw new ArgumentException("Baked dirt must match mask resolution.", nameof(bakedDirt));
            Resolution = resolution;
            coverage = (byte[])usedTexels.Clone();
            regions = BuildRegions(coverage, resolution);
            initial = new float[coverage.Length]; remaining = new float[coverage.Length];
            Pixels = new Color32[coverage.Length];
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    int i = y * resolution + x;
                    if (bakedDirt != null) initial[i] = coverage[i] == 0 ? 0 : bakedDirt[i] / 255f;
                    else
                    {
                        float u = (x + .5f) / resolution, v = (y + .5f) / resolution;
                        float noise = Mathf.PerlinNoise(u * 9 + seed * .71f, v * 9 + seed * .31f);
                        float streak = Mathf.PerlinNoise(u * 33 + seed, v * 3 + seed);
                        initial[i] = coverage[i] == 0 ? 0 : Mathf.Clamp01(.12f + .75f * Mathf.SmoothStep(0, 1, (noise - .28f) / .46f) + streak * .13f);
                    }
                    initialMass += initial[i] * coverage[i] / 255.0;
                }
            Reset();
        }

        /// <summary>Lays stains over the dirt: blood (splash, satellite drops, a run) and oil smears. They sit only on used
        /// texels, count as dirt for the progress, and are wiped like dirt. Same seed, same stains.</summary>
        public void AddStains(int seed)
        {
            int n = Resolution, covered = 0;
            for (int i = 0; i < coverage.Length; i++) if (coverage[i] != 0) covered++;
            if (covered < n * n / 50) return;
            var rng = new System.Random(seed);
            stain = new Color32[coverage.Length];
            float unit = n / 256f, share = covered / (float)(n * n);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            bool Centre(out int cx, out int cy)
            {
                for (int tries = 0; tries < 60; tries++)
                {
                    cx = rng.Next(n); cy = rng.Next(n);
                    if (coverage[cy * n + cx] != 0) return true;
                }
                cx = cy = 0; return false;
            }
            void Put(int x, int y, Color32 colour, float amount)
            {
                if (x < 0 || y < 0 || x >= n || y >= n) return;
                int i = y * n + x;
                if (coverage[i] == 0) return;
                if (stain[i].r == 0 && stain[i].g == 0 && stain[i].b == 0) StainTexels++;
                stain[i] = colour;
                initial[i] = Mathf.Max(initial[i], amount);
            }
            void Disc(float cx, float cy, float radius, Color32 colour, float amount, float ragged)
            {
                int r = Mathf.CeilToInt(radius + 1);
                for (int y = -r; y <= r; y++)
                    for (int x = -r; x <= r; x++)
                    {
                        float edge = radius * (1 - ragged + ragged * Mathf.PerlinNoise((cx + x) * .37f + seed * .013f, (cy + y) * .37f));
                        if (x * x + y * y <= edge * edge) Put(Mathf.RoundToInt(cx) + x, Mathf.RoundToInt(cy) + y, colour, amount);
                    }
            }
            // Blood: a ragged pool, drops thrown off it, one run. Fresh is red, old is brown; linear values.
            int blood = share > .12f ? rng.Next(0, 3) : rng.Next(0, 2);
            for (int k = 0; k < blood; k++)
            {
                if (!Centre(out int cx, out int cy)) break;
                bool old = rng.Next(3) == 0;
                var body = old ? new Color32(52, 12, 8, 0) : new Color32(92, 6, 6, 0);
                var dark = old ? new Color32(34, 8, 6, 0) : new Color32(56, 3, 4, 0);
                float radius = R(5, 11) * unit;
                Disc(cx, cy, radius, body, .92f, .45f);
                Disc(cx + R(-1, 1) * unit, cy + R(-1, 1) * unit, radius * .45f, dark, .96f, .3f);
                int drops = rng.Next(4, 9);
                for (int d = 0; d < drops; d++)
                {
                    float angle = R(0, Mathf.PI * 2), far = radius * R(1.2f, 2.4f);
                    Disc(cx + Mathf.Cos(angle) * far, cy + Mathf.Sin(angle) * far, R(.8f, 2.2f) * unit, body, .85f, .2f);
                }
                float run = R(0, Mathf.PI * 2), length = radius * R(1.5f, 3f);
                for (float t = 0; t < length; t += .7f)
                    Disc(cx + Mathf.Cos(run) * t, cy + Mathf.Sin(run) * t, Mathf.Lerp(1.6f, .7f, t / length) * unit, body, .88f, .1f);
                StainCounts[0]++;
            }
            // Oil and grease: dark smears dragged along one direction, thinning out.
            int oil = rng.Next(1, 4);
            for (int k = 0; k < oil; k++)
            {
                if (!Centre(out int cx, out int cy)) break;
                var body = rng.Next(2) == 0 ? new Color32(10, 9, 8, 0) : new Color32(22, 15, 6, 0);
                float angle = R(0, Mathf.PI * 2), length = R(10, 26) * unit, width = R(2.2f, 4.5f) * unit;
                for (float t = 0; t < length; t += .6f)
                {
                    float wobble = Mathf.Sin(t * .35f + k) * 1.2f * unit;
                    Disc(cx + Mathf.Cos(angle) * t - Mathf.Sin(angle) * wobble, cy + Mathf.Sin(angle) * t + Mathf.Cos(angle) * wobble,
                        Mathf.Lerp(width, width * .35f, t / length), body, Mathf.Lerp(.9f, .6f, t / length), .5f);
                }
                StainCounts[1]++;
            }
            initialMass = 0;
            for (int i = 0; i < initial.Length; i++) initialMass += initial[i] * coverage[i] / 255.0;
            Reset();
        }

        /// <summary>Glue left where a label was pulled off: new dirt on the island under <paramref name="uv"/>, a pale
        /// tacky patch that has to be wiped. Returns false when nothing was laid (off the mesh).</summary>
        public bool AddResidue(Vector2 uv, float radius, Color32 colour, float amount = .8f)
        {
            int region = RegionAt(uv);
            if (region == 0 || !float.IsFinite(radius) || radius <= 0) return false;
            if (stain == null) stain = new Color32[coverage.Length];
            int n = Resolution, r = Mathf.CeilToInt(radius * n) + 1, cx = Mathf.Clamp((int)(uv.x * n), 0, n - 1), cy = Mathf.Clamp((int)(uv.y * n), 0, n - 1);
            bool laid = false;
            for (int y = Mathf.Max(0, cy - r); y <= Mathf.Min(n - 1, cy + r); y++)
                for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(n - 1, cx + r); x++)
                {
                    int i = y * n + x;
                    if (regions[i] != region) continue;
                    float d = new Vector2((x + .5f) / n - uv.x, (y + .5f) / n - uv.y).magnitude / radius;
                    // a patchy blot: glue keeps the label's outline only roughly
                    float patch = amount * (1 - Mathf.SmoothStep(.6f, 1, d)) * (.55f + .45f * Mathf.PerlinNoise(x * .9f, y * .9f));
                    if (patch <= remaining[i] || patch < .08f) continue;
                    double weight = coverage[i] / 255.0;
                    if (patch > initial[i]) { initialMass += (patch - initial[i]) * weight; initial[i] = patch; }
                    remainingMass += (patch - remaining[i]) * weight; remaining[i] = patch;
                    if (stain[i].r == 0 && stain[i].g == 0 && stain[i].b == 0) StainTexels++;
                    stain[i] = colour; WritePixel(i); laid = true;
                }
            if (laid) StainCounts[2]++;
            return laid;
        }

        public bool Paint(Vector2 uv, float radius, float strength, bool isolateIsland = false)
        {
            if (!float.IsFinite(uv.x) || !float.IsFinite(uv.y) || !float.IsFinite(radius) || !float.IsFinite(strength)
                || radius <= 0 || strength <= 0 || uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return false;
            int region = isolateIsland ? RegionAt(uv) : 0;
            if (isolateIsland && region == 0) return false;
            int minX = Mathf.Clamp(Mathf.FloorToInt((uv.x - radius) * Resolution),0,Resolution-1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((uv.x + radius) * Resolution),0,Resolution-1);
            int minY = Mathf.Clamp(Mathf.FloorToInt((uv.y - radius) * Resolution),0,Resolution-1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt((uv.y + radius) * Resolution),0,Resolution-1);
            // Limit work for accidental huge radii. Unique atlas edges never wrap to another island.
            if (radius >= .5f) { minX = minY = 0; maxX = maxY = Resolution - 1; }
            bool changed = false;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    int px = x, py = y;
                    float dx = Mathf.Abs((px + .5f) / Resolution - uv.x), dy = Mathf.Abs((py + .5f) / Resolution - uv.y);
                    float distance = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                    if (distance >= 1) continue;
                    int i = py * Resolution + px;
                    if (isolateIsland && regions[i] != region) continue;
                    float falloff = 1 - Mathf.SmoothStep(.45f, 1, distance);
                    float next = Mathf.Max(0, remaining[i] - strength * falloff);
                    if (next >= remaining[i]) continue;
                    remainingMass -= (remaining[i] - next) * coverage[i] / 255.0;
                    remaining[i] = next; WritePixel(i); changed = true;
                }
            return changed;
        }

        /// <summary>笔刷中心处还剩多少污垢（0 干净 .. 1 最厚）：决定这一下听起来是刮、是擦、还是扫灰。</summary>
        public float RemainingAt(Vector2 uv)
        {
            if (!float.IsFinite(uv.x) || !float.IsFinite(uv.y)) return 0;
            int x = Mathf.Clamp((int)(uv.x * Resolution), 0, Resolution - 1);
            int y = Mathf.Clamp((int)(uv.y * Resolution), 0, Resolution - 1);
            return remaining[y * Resolution + x];
        }

        public bool SameIsland(Vector2 a, Vector2 b)
        {
            int region = RegionAt(a);
            return region != 0 && region == RegionAt(b);
        }

        int RegionAt(Vector2 uv)
        {
            if (!float.IsFinite(uv.x) || !float.IsFinite(uv.y) || uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return 0;
            int x = Mathf.Clamp((int)(uv.x * Resolution), 0, Resolution - 1);
            int y = Mathf.Clamp((int)(uv.y * Resolution), 0, Resolution - 1);
            int region = regions[y * Resolution + x];
            if (region != 0) return region;
            // Triangle edges can land between texel centres. Use only the closest one-texel neighbour.
            float closest = float.MaxValue;
            for (int py = Mathf.Max(0, y - 1); py <= Mathf.Min(Resolution - 1, y + 1); py++)
                for (int px = Mathf.Max(0, x - 1); px <= Mathf.Min(Resolution - 1, x + 1); px++)
                {
                    int candidate = regions[py * Resolution + px];
                    float distance = (new Vector2((px + .5f) / Resolution, (py + .5f) / Resolution) - uv).sqrMagnitude;
                    if (candidate != 0 && distance < closest) { closest = distance; region = candidate; }
                }
            return region;
        }

        static int[] BuildRegions(byte[] used, int size)
        {
            var result = new int[used.Length];
            var queue = new int[used.Length];
            int region = 0;
            for (int start = 0; start < used.Length; start++)
            {
                if (used[start] == 0 || result[start] != 0) continue;
                region++; int read = 0, count = 1; queue[0] = start; result[start] = region;
                while (read < count)
                {
                    int i = queue[read++], x = i % size, y = i / size;
                    void Visit(int next)
                    {
                        if (used[next] == 0 || result[next] != 0) return;
                        result[next] = region; queue[count++] = next;
                    }
                    if (x > 0) Visit(i - 1); if (x < size - 1) Visit(i + 1);
                    if (y > 0) Visit(i - size); if (y < size - 1) Visit(i + size);
                }
            }
            return result;
        }

        public void Reset()
        {
            remainingMass = initialMass;
            Array.Copy(initial, remaining, initial.Length);
            for (int i = 0; i < Pixels.Length; i++) WritePixel(i);
        }

        public void Clear()
        {
            remainingMass = 0;
            Array.Clear(remaining, 0, remaining.Length);
            for (int i = 0; i < Pixels.Length; i++) WritePixel(i);
        }

        public string Export() => Convert.ToBase64String(Array.ConvertAll(Pixels, p => p.r));

        public bool Import(string encoded)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(encoded ?? ""); } catch (FormatException) { return false; }
            if (bytes.Length != Pixels.Length) return false;
            remainingMass = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                remaining[i] = Mathf.Min(initial[i], bytes[i] / 255f);
                remainingMass += remaining[i] * coverage[i] / 255.0;
                WritePixel(i);
            }
            return true;
        }

        void WritePixel(int i)
        {
            byte value = (byte)Mathf.RoundToInt(Mathf.Clamp01(remaining[i]) * 255);
            // R = dirt left. G, B, A = the stain's colour where there is one (all zero = plain dirt in the surface's dirt colour).
            if (stain != null) { var s = stain[i]; Pixels[i] = new Color32(value, s.r, s.g, s.b); }
            else Pixels[i] = new Color32(value, 0, 0, 0);
        }
    }
}
