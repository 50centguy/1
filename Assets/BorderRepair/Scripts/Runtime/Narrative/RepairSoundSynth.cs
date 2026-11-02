using System;
using UnityEngine;

namespace BorderRepair.Narrative
{
    /// <summary>
    /// 维修反馈音效：运行时用代码合成的短音（不依赖音频素材，便于替换成正式音效前先验证节奏与辨识度）。
    /// 每种音效的“特征”尽量不同：棘轮连击 / 撕纸噪声 / 低频闷响 / 下行警告双音 / 数据啁啾 / 单次确认音 / 低频蜂鸣。
    /// </summary>
    public static class RepairSoundSynth
    {
        public const int Rate = 44100;

        // 新值只能加在末尾：RepairFeedbackFx.StepCue 把枚举按整数序列化在资产里。
        public enum Cue { None, Unscrew, SealTear, OpenCover, ProbeWarning, DataRead, Probe, Replace, Deny, CleanWipe, CleanScrape, CleanDust, CleanDone }

        /// <summary>这个音是首尾相接的循环素材（用 AudioSource.loop 播放，不用 PlayOneShot）。</summary>
        public static bool Loops(Cue cue) => cue == Cue.CleanWipe;

        public static AudioClip Create(Cue cue)
        {
            float[] data;
            switch (cue)
            {
                case Cue.Unscrew: data = Unscrew(); break;
                case Cue.SealTear: data = SealTear(); break;
                case Cue.OpenCover: data = OpenCover(); break;
                case Cue.ProbeWarning: data = ProbeWarning(); break;
                case Cue.DataRead: data = DataRead(); break;
                case Cue.Probe: data = Probe(); break;
                case Cue.Replace: data = Replace(); break;
                case Cue.Deny: data = Deny(); break;
                case Cue.CleanWipe: data = CleanWipe(); break;
                case Cue.CleanScrape: data = CleanScrape(); break;
                case Cue.CleanDust: data = CleanDust(); break;
                case Cue.CleanDone: data = CleanDone(); break;
                default: return null;
            }
            Normalize(data, 0.8f);
            var clip = AudioClip.Create("sfx_" + cue.ToString().ToLowerInvariant(), data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ---------- 音色 ----------

        /// <summary>螺丝刀：7 下逐渐加快的棘轮“咔哒”，最后一声轻的金属“叮”（螺丝落进零件盘）。</summary>
        static float[] Unscrew()
        {
            var d = Buffer(0.62f);
            var rng = new System.Random(11);
            float t = 0.0f;
            for (int i = 0; i < 7; i++)
            {
                Click(d, t, 0.006f, 3200f, 0.9f, rng);
                t += Mathf.Lerp(0.072f, 0.045f, i / 6f);
            }
            Ping(d, t + 0.06f, 0.16f, new[] { 2600f, 4150f, 6300f }, 0.5f);
            return d;
        }

        /// <summary>封条撕开：0.28 秒粗糙的带通噪声，振幅抖动。</summary>
        static float[] SealTear()
        {
            var d = Buffer(0.3f);
            var rng = new System.Random(23);
            float lp = 0f, hp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float time = i / (float)Rate;
                float env = Mathf.Clamp01(time / 0.02f) * Mathf.Clamp01((0.3f - time) / 0.08f);
                float rough = 0.55f + 0.45f * Mathf.PerlinNoise(time * 90f, 0.3f);
                float n = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.35f * (n - lp);
                hp = lp - hp * 0.2f;
                d[i] = hp * env * rough;
            }
            return d;
        }

        /// <summary>撬开盖板：起始一声“咔”，随后低频闷响（110→65 Hz）和一小段金属刮擦。</summary>
        static float[] OpenCover()
        {
            var d = Buffer(0.42f);
            var rng = new System.Random(5);
            Click(d, 0f, 0.01f, 1800f, 1f, rng);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float time = i / (float)Rate;
                if (time < 0.02f || time > 0.24f) continue;
                float k = (time - 0.02f) / 0.22f;
                float f = Mathf.Lerp(110f, 65f, k);
                phase += 2f * Mathf.PI * f / Rate;
                d[i] += Mathf.Sin(phase) * Mathf.Exp(-k * 4f) * 0.9f;
            }
            float lp = 0f;
            for (int i = (int)(0.2f * Rate); i < d.Length; i++)
            {
                float time = i / (float)Rate - 0.2f;
                float n = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.6f * (n - lp);
                d[i] += (n - lp) * 0.25f * Mathf.Exp(-time * 14f) * (0.6f + 0.4f * Mathf.Sin(time * 2f * Mathf.PI * 38f));
            }
            return d;
        }

        /// <summary>检测到旁路：探针接触“嗒”，然后两声下行警告音（高→低），听起来是“不对”。</summary>
        static float[] ProbeWarning()
        {
            var d = Buffer(0.62f);
            var rng = new System.Random(7);
            Click(d, 0f, 0.004f, 2500f, 0.6f, rng);
            Tone(d, 0.06f, 0.12f, 1046f, 0.7f, true);
            Tone(d, 0.22f, 0.26f, 622f, 0.8f, true);
            return d;
        }

        /// <summary>读取日志：插头“咔”，14 声快速的数据啁啾，最后上行两音表示导出完成。</summary>
        static float[] DataRead()
        {
            var d = Buffer(0.95f);
            var rng = new System.Random(3);
            Click(d, 0f, 0.006f, 1500f, 0.8f, rng);
            float[] freqs = { 1250f, 1650f, 2050f, 2450f };
            float t = 0.07f;
            for (int i = 0; i < 14; i++)
            {
                Tone(d, t, 0.024f, freqs[rng.Next(freqs.Length)], 0.35f, false);
                t += 0.038f;
            }
            Tone(d, t + 0.05f, 0.09f, 880f, 0.55f, false);
            Tone(d, t + 0.15f, 0.12f, 1320f, 0.55f, false);
            return d;
        }

        /// <summary>普通检测：单次短“嘀—嘀”（上行），表示读数正常。</summary>
        static float[] Probe()
        {
            var d = Buffer(0.25f);
            Tone(d, 0f, 0.07f, 1320f, 0.55f, false);
            Tone(d, 0.09f, 0.08f, 1760f, 0.55f, false);
            return d;
        }

        /// <summary>换件：两声低沉的“咔嗒”卡入。</summary>
        static float[] Replace()
        {
            var d = Buffer(0.3f);
            var rng = new System.Random(9);
            Click(d, 0f, 0.012f, 900f, 1f, rng);
            Click(d, 0.12f, 0.014f, 700f, 1f, rng);
            return d;
        }

        /// <summary>操作被拒绝：短促的低频蜂鸣。</summary>
        static float[] Deny()
        {
            var d = Buffer(0.2f);
            Tone(d, 0f, 0.16f, 180f, 0.6f, true);
            return d;
        }

        /// <summary>抹布摩擦（循环）：带通噪声 + 三个来回的力度起伏。尾部与开头等功率交叉淡化，循环接缝听不出来。</summary>
        static float[] CleanWipe()
        {
            const float body = 0.84f, fade = 0.1f;
            var d = Buffer(body + fade);
            var rng = new System.Random(31);
            float low = 0f, band = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float time = i / (float)Rate;
                float stroke = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Mathf.PI * time * 3f / body));
                float grain = 0.75f + 0.25f * Mathf.PerlinNoise(time * 140f, 0.7f);
                float n = (float)(rng.NextDouble() * 2 - 1);
                low += 0.42f * (n - low);
                band += 0.12f * (low - band);
                d[i] = (low - band) * stroke * grain;
            }
            int length = Mathf.CeilToInt(body * Rate), overlap = d.Length - length;
            var loop = new float[length];
            Array.Copy(d, loop, length);
            for (int j = 0; j < overlap; j++)
            {
                float k = (j + 0.5f) / overlap;
                loop[j] = d[j] * Mathf.Sin(k * Mathf.PI * 0.5f) + d[length + j] * Mathf.Cos(k * Mathf.PI * 0.5f);
            }
            return loop;
        }

        /// <summary>刮硬污渍：一串不均匀的细小“咔”（中频共振）+ 粗糙的噪声底，颗粒感强。</summary>
        static float[] CleanScrape()
        {
            const float seconds = 0.28f;
            var d = Buffer(seconds);
            var rng = new System.Random(41);
            for (float t = 0f; t < 0.25f; t += 0.011f + (float)rng.NextDouble() * 0.016f)
            {
                float gain = (0.35f + 0.65f * (float)rng.NextDouble()) * (1f - 0.35f * t / 0.25f);
                Click(d, t, 0.003f + (float)rng.NextDouble() * 0.004f, 900f + (float)rng.NextDouble() * 1100f, gain, rng);
            }
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float time = i / (float)Rate;
                float env = Mathf.Clamp01(time / 0.012f) * Mathf.Clamp01((seconds - time) / 0.06f);
                float n = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.3f * (n - lp);
                d[i] += (n - lp) * 0.16f * env * (0.5f + 0.5f * Mathf.PerlinNoise(time * 260f, 0.2f));
            }
            return d;
        }

        /// <summary>吹灰 / 刷子扫过：高通噪声的一口气（快起慢收），带轻微颤动和几粒飞走的灰尘声。</summary>
        static float[] CleanDust()
        {
            const float seconds = 0.36f;
            var d = Buffer(seconds);
            var rng = new System.Random(57);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float time = i / (float)Rate, k = time / seconds;
                float env = Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.6f));
                float n = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.55f * (n - lp);
                d[i] = (n - lp) * env * env * (0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * time * (38f + 30f * k)));
            }
            for (int i = 0; i < 5; i++)
                Click(d, 0.05f + i * 0.055f + (float)rng.NextDouble() * 0.02f, 0.002f, 5200f + (float)rng.NextDouble() * 2200f, 0.18f, rng);
            return d;
        }

        /// <summary>清洁完成：一声轻“嗒”，接上行的两记金属“叮”（G6→C7），干净利落。</summary>
        static float[] CleanDone()
        {
            var d = Buffer(0.52f);
            var rng = new System.Random(61);
            Click(d, 0f, 0.004f, 2600f, 0.35f, rng);
            Ping(d, 0.012f, 0.18f, new[] { 1568f, 3136f, 4704f }, 0.5f);
            Ping(d, 0.12f, 0.38f, new[] { 2093f, 4186f, 6272f }, 0.62f);
            return d;
        }

        // ---------- 基本元素 ----------

        static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];

        /// <summary>短噪声爆 + 高频衰减正弦：机械“咔”。</summary>
        static void Click(float[] d, float start, float length, float ring, float gain, System.Random rng)
        {
            int s = (int)(start * Rate);
            int n = (int)((length + 0.02f) * Rate);
            for (int i = 0; i < n && s + i < d.Length; i++)
            {
                float time = i / (float)Rate;
                float noise = time < length ? (float)(rng.NextDouble() * 2 - 1) * (1f - time / length) : 0f;
                float tone = Mathf.Sin(2f * Mathf.PI * ring * time) * Mathf.Exp(-time * 220f);
                d[s + i] += (noise * 0.7f + tone * 0.6f) * gain;
            }
        }

        /// <summary>几个不成谐波的衰减正弦：金属“叮”。</summary>
        static void Ping(float[] d, float start, float length, float[] partials, float gain)
        {
            int s = (int)(start * Rate);
            int n = (int)(length * Rate);
            for (int i = 0; i < n && s + i < d.Length; i++)
            {
                float time = i / (float)Rate;
                float v = 0f;
                for (int p = 0; p < partials.Length; p++)
                    v += Mathf.Sin(2f * Mathf.PI * partials[p] * time) * Mathf.Exp(-time * (18f + p * 10f)) / (p + 1);
                d[s + i] += v * gain;
            }
        }

        /// <summary>带起止淡入淡出的音；square 时加奇次谐波，听起来更像提示器。</summary>
        static void Tone(float[] d, float start, float length, float freq, float gain, bool square)
        {
            int s = (int)(start * Rate);
            int n = (int)(length * Rate);
            for (int i = 0; i < n && s + i < d.Length; i++)
            {
                float time = i / (float)Rate;
                float env = Mathf.Clamp01(time / 0.005f) * Mathf.Clamp01((length - time) / 0.012f);
                float w = 2f * Mathf.PI * freq * time;
                float v = Mathf.Sin(w);
                if (square) v += Mathf.Sin(3f * w) / 3f + Mathf.Sin(5f * w) / 5f;
                d[s + i] += v * env * gain;
            }
        }

        static void Normalize(float[] d, float peak)
        {
            float max = 0f;
            foreach (var v in d) max = Math.Max(max, Math.Abs(v));
            if (max < 1e-6f) return;
            float k = peak / max;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }
    }
}
