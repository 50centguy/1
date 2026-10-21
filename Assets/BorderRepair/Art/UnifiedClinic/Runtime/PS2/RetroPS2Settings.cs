using System;
using UnityEngine;

namespace BorderRepair.Art.UnifiedClinic.PS2
{
    public enum RetroPS2Preset { Default, ClearRepair, StrongPixel }

    /// <summary>One set of PS2-look parameters. Room walking and repair close-up each own one.</summary>
    [Serializable]
    public sealed class RetroPS2Profile
    {
        [Tooltip("0 = 原画面，1 = 完整 PS2 处理")]
        [Range(0, 1)] public float strength = .85f;
        [Tooltip("虚拟竖向分辨率（行）。PS2 常见 448/480；越小越粗。高于屏幕高度时不降采样。")]
        [Range(120, 1080)] public int virtualLines = 448;
        [Tooltip("虚拟像素边缘：0 = 柔和插值（主机输出），1 = 硬方块")]
        [Range(0, 1)] public float pixelSharpness = .35f;
        [Tooltip("每通道色阶。32 ≈ PS2 16 位帧缓冲；256 = 不量化")]
        [Range(4, 256)] public int colorLevels = 32;
        [Tooltip("4x4 有序抖色强度")]
        [Range(0, 1)] public float dither = .6f;
        [Tooltip("原有柔光的倍数（1 = 与原画面相同）")]
        [Range(0, 2)] public float softness = 1f;
        [Tooltip("复合视频色度渗色（水平，单位：虚拟像素）")]
        [Range(0, 3)] public float chromaBleed = 1f;

        public void CopyFrom(RetroPS2Profile other)
        {
            strength = other.strength; virtualLines = other.virtualLines; pixelSharpness = other.pixelSharpness;
            colorLevels = other.colorLevels; dither = other.dither; softness = other.softness; chromaBleed = other.chromaBleed;
        }

        // 2026-10-20: the first defaults (448/720 lines, 32/64 levels) were barely visible on a 1080p screen. The
        // overlay UI and the AR markers are never processed, so the close-up view can afford a real low-res image.
        public static RetroPS2Profile Create(RetroPS2Preset preset, bool inspection)
        {
            switch (preset)
            {
                case RetroPS2Preset.ClearRepair:
                    return new RetroPS2Profile { strength = .8f, virtualLines = 540, pixelSharpness = .3f, colorLevels = 48, dither = .5f, softness = .7f, chromaBleed = .5f };
                case RetroPS2Preset.StrongPixel:
                    return inspection
                        ? new RetroPS2Profile { strength = 1f, virtualLines = 240, pixelSharpness = .9f, colorLevels = 20, dither = 1f, softness = .9f, chromaBleed = 1.5f }
                        : new RetroPS2Profile { strength = 1f, virtualLines = 200, pixelSharpness = 1f, colorLevels = 16, dither = 1f, softness = 1f, chromaBleed = 2f };
                default:
                    return inspection
                        ? new RetroPS2Profile { strength = 1f, virtualLines = 360, pixelSharpness = .75f, colorLevels = 28, dither = .85f, softness = .8f, chromaBleed = .4f }
                        : new RetroPS2Profile { strength = 1f, virtualLines = 288, pixelSharpness = .85f, colorLevels = 24, dither = .9f, softness = 1f, chromaBleed = .6f };
            }
        }
    }

    /// <summary>
    /// Session-only player settings (no PlayerPrefs, no save files). Off disables only the PS2 pass and
    /// restores the old diffusion multiplier to 1, i.e. the task-start picture.
    /// </summary>
    public static class RetroPS2Settings
    {
        public static bool Enabled = true;
        /// <summary>PS2 时代的贴图分辨率：开启时全局贴图降一级 mip（只影响运行时，不改资源）。</summary>
        public static bool LowResTextures = true;
        public static RetroPS2Preset Preset { get; private set; } = RetroPS2Preset.Default;
        public static readonly RetroPS2Profile Room = RetroPS2Profile.Create(RetroPS2Preset.Default, false);
        public static readonly RetroPS2Profile Inspection = RetroPS2Profile.Create(RetroPS2Preset.Default, true);
        public static event Action Changed;

        public static void ApplyPreset(RetroPS2Preset preset)
        {
            Preset = preset;
            Room.CopyFrom(RetroPS2Profile.Create(preset, false));
            Inspection.CopyFrom(RetroPS2Profile.Create(preset, true));
            NotifyChanged();
        }

        public static void ResetToDefaults()
        {
            Enabled = true;
            LowResTextures = true;
            ApplyPreset(RetroPS2Preset.Default);
        }

        public static void NotifyChanged() => Changed?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Changed = null;
            Enabled = true;
            LowResTextures = true;
            Preset = RetroPS2Preset.Default;
            Room.CopyFrom(RetroPS2Profile.Create(RetroPS2Preset.Default, false));
            Inspection.CopyFrom(RetroPS2Profile.Create(RetroPS2Preset.Default, true));
        }
    }
}
