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
        public float Progress => initialMass <= 0 ? 1 : Mathf.Clamp01(1 - (float)(remainingMass / initialMass));

        public DirtMaskData(int resolution, byte[] usedTexels, int seed = 23)
        {
            if (resolution < 8 || resolution > 512) throw new ArgumentOutOfRangeException(nameof(resolution));
            if (usedTexels == null || usedTexels.Length != resolution * resolution)
                throw new ArgumentException("UV coverage must match mask resolution.", nameof(usedTexels));
            Resolution = resolution;
            coverage = (byte[])usedTexels.Clone();
            regions = BuildRegions(coverage, resolution);
            initial = new float[coverage.Length]; remaining = new float[coverage.Length];
            Pixels = new Color32[coverage.Length];
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    int i = y * resolution + x;
                    float u = (x + .5f) / resolution, v = (y + .5f) / resolution;
                    float noise = Mathf.PerlinNoise(u * 9 + seed * .71f, v * 9 + seed * .31f);
                    float streak = Mathf.PerlinNoise(u * 33 + seed, v * 3 + seed);
                    initial[i] = coverage[i] == 0 ? 0 : Mathf.Clamp01(.12f + .75f * Mathf.SmoothStep(0, 1, (noise - .28f) / .46f) + streak * .13f);
                    initialMass += initial[i] * coverage[i] / 255.0;
                }
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
                    float next = Mathf.Max(0, remaining[i] - strength * falloff);
                    if (next >= remaining[i]) continue;
                    remainingMass -= (remaining[i] - next) * coverage[i] / 255.0;
                    remaining[i] = next; WritePixel(i); changed = true;
                }
            return changed;
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
            Pixels[i] = new Color32(value, value, value, 255);
        }
    }
}
