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
        // Stains on top of the plain dirt: what each stained texel shows (linear rgb) and whether it peels slowly.
        Color32[] stain;
        bool[] stubborn;
        /// <summary>How many texels carry a stain (blood, tape, a sticker) rather than plain dirt.</summary>
        public int StainTexels { get; private set; }
        /// <summary>Stain kinds laid down by <see cref="AddStains"/>, in order: blood, tape, sticker.</summary>
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

        /// <summary>Lays stains over the dirt: blood (splash, satellite drops, a run), strips of tape, stickers. They sit
        /// only on used texels, count as dirt for the progress, and are wiped like dirt (tape and stickers more slowly).
        /// Same seed, same stains.</summary>
        public void AddStains(int seed)
        {
            int n = Resolution, covered = 0;
            for (int i = 0; i < coverage.Length; i++) if (coverage[i] != 0) covered++;
            if (covered < n * n / 50) return;
            var rng = new System.Random(seed);
            stain = new Color32[coverage.Length]; stubborn = new bool[coverage.Length];
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
            void Put(int x, int y, Color32 colour, float amount, bool slow)
            {
                if (x < 0 || y < 0 || x >= n || y >= n) return;
                int i = y * n + x;
                if (coverage[i] == 0) return;
                if (stain[i].r == 0 && stain[i].g == 0 && stain[i].b == 0) StainTexels++;
                stain[i] = colour; stubborn[i] = slow;
                initial[i] = Mathf.Max(initial[i], amount);
            }
            void Disc(float cx, float cy, float radius, Color32 colour, float amount, float ragged)
            {
                int r = Mathf.CeilToInt(radius + 1);
                for (int y = -r; y <= r; y++)
                    for (int x = -r; x <= r; x++)
                    {
                        float edge = radius * (1 - ragged + ragged * Mathf.PerlinNoise((cx + x) * .37f + seed * .013f, (cy + y) * .37f));
                        if (x * x + y * y <= edge * edge) Put(Mathf.RoundToInt(cx) + x, Mathf.RoundToInt(cy) + y, colour, amount, false);
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
            // Tape: a strip along one axis, a darker edge, torn ends.
            int tapes = rng.Next(1, 3);
            for (int k = 0; k < tapes; k++)
            {
                if (!Centre(out int cx, out int cy)) break;
                bool across = rng.Next(2) == 0;
                int length = Mathf.Max(20, Mathf.RoundToInt(R(22, 36) * unit)), width = Mathf.Max(5, Mathf.RoundToInt(R(5, 8) * unit));
                int kind = rng.Next(3);
                var face = kind == 0 ? new Color32(150, 122, 62, 0) : kind == 1 ? new Color32(70, 72, 76, 0) : new Color32(168, 130, 12, 0);   // paper, duct, hazard
                var edge = new Color32((byte)(face.r * .6f), (byte)(face.g * .6f), (byte)(face.b * .6f), 0);
                for (int a = -length / 2; a <= length / 2; a++)
                {
                    int torn = Mathf.Abs(a) > length / 2 - 2 ? rng.Next(0, 2) : 0;
                    for (int b = -width / 2 + torn; b <= width / 2 - torn; b++)
                    {
                        bool rim = Mathf.Abs(b) == width / 2 - torn;
                        bool stripe = kind == 2 && ((a + b + 400) / Mathf.Max(2, Mathf.RoundToInt(3 * unit))) % 2 == 0;
                        var colour = rim ? edge : stripe ? new Color32(40, 38, 36, 0) : face;
                        Put(cx + (across ? a : b), cy + (across ? b : a), colour, .97f, true);
                    }
                }
                StainCounts[1]++;
            }
            // Stickers: a bright rectangle with a pale border and a plain mark inside (bars, a ring, a cross).
            int stickers = rng.Next(1, 4);
            Color32[] faces = { new Color32(228, 160, 10, 0), new Color32(226, 26, 90, 0), new Color32(210, 205, 190, 0), new Color32(226, 80, 8, 0), new Color32(40, 160, 70, 0) };
            for (int k = 0; k < stickers; k++)
            {
                if (!Centre(out int cx, out int cy)) break;
                int w = Mathf.Max(13, Mathf.RoundToInt(R(12, 20) * unit)), h = Mathf.Max(10, Mathf.RoundToInt(R(10, 16) * unit)), mark = rng.Next(3);
                var face = faces[rng.Next(faces.Length)];
                // a thin rim in the face colour gone pale, so the colour reads even on a small sticker
                var border = new Color32((byte)((face.r + 214) / 2), (byte)((face.g + 210) / 2), (byte)((face.b + 200) / 2), 0); var ink = new Color32(46, 42, 40, 0);
                for (int y = -h / 2; y <= h / 2; y++)
                    for (int x = -w / 2; x <= w / 2; x++)
                    {
                        bool rim = Mathf.Abs(x) == w / 2 || Mathf.Abs(y) == h / 2;
                        bool inked = mark == 0 ? Mathf.Abs(y) < h / 4 && (x + 100) % 3 == 0
                            : mark == 1 ? Mathf.Abs(Mathf.Sqrt(x * x + y * y) - h * .28f) < 1f
                            : Mathf.Abs(Mathf.Abs(x) - Mathf.Abs(y)) < 1 && Mathf.Abs(x) < h / 3;
                        Put(cx + x, cy + y, rim ? border : inked ? ink : face, .97f, true);
                    }
                StainCounts[2]++;
            }
            initialMass = 0;
            for (int i = 0; i < initial.Length; i++) initialMass += initial[i] * coverage[i] / 255.0;
            Reset();
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
                    float grip = stubborn != null && stubborn[i] ? .45f : 1f;   // tape and stickers peel, they do not wipe
                    float next = Mathf.Max(0, remaining[i] - strength * falloff * grip);
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
