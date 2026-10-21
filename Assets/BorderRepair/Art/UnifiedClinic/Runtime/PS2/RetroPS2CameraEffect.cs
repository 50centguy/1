using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace BorderRepair.Art.UnifiedClinic.PS2
{
    /// <summary>
    /// Runtime-only marker that opts one desktop RetroClinicCamera into the PS2 pass. It is added by
    /// <see cref="RetroPS2Runtime"/> in play mode only, never serialized, and owns its own material instance,
    /// so cameras never share parameters and the template shader is never modified.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class RetroPS2CameraEffect : MonoBehaviour
    {
        public const string ShaderResource = "BorderRepairPS2/RetroClinicPS2";
        public static readonly string[] DesktopScenePaths =
        {
            "Assets/RepairProps/Scenes/RepairProps_Clinic.unity",
            "Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic.unity",
        };

        static readonly int Strength = Shader.PropertyToID("_PS2Strength");
        static readonly int Lines = Shader.PropertyToID("_PS2Lines");
        static readonly int Sharpness = Shader.PropertyToID("_PS2PixelSharpness");
        static readonly int Levels = Shader.PropertyToID("_PS2ColorLevels");
        static readonly int Dither = Shader.PropertyToID("_PS2Dither");
        static readonly int Bleed = Shader.PropertyToID("_PS2ChromaBleed");
        static Shader shader;
        /// <summary>Test captures render the game camera into a texture; they may opt that render into the pass.</summary>
        public static bool IncludeOffscreenCaptures;
        static bool shaderMissingLogged;

        RetroClinicCamera look;
        Material material;

        public RetroClinicCamera Look => look != null ? look : look = GetComponent<RetroClinicCamera>();
        public RetroPS2Profile ActiveProfile => Look != null && Look.ReadabilityMode ? RetroPS2Settings.Inspection : RetroPS2Settings.Room;
        public bool Active => RetroPS2Settings.Enabled && isActiveAndEnabled && Look != null && Look.isActiveAndEnabled;
        /// <summary>Multiplier on the existing diffusion. Exactly 1 whenever the PS2 look is off.</summary>
        public float DiffusionScale => Active ? ActiveProfile.softness : 1f;

        public static bool IsDesktopScene(Scene scene)
        {
            foreach (var path in DesktopScenePaths)
                if (string.Equals(scene.path, path, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Desktop game view only: no XR, SceneView, preview, reflection or overlay cameras.</summary>
        public static bool Eligible(ref CameraData cameraData)
        {
            if (!Application.isPlaying || cameraData.cameraType != CameraType.Game ||
                cameraData.renderType != CameraRenderType.Base || cameraData.xrRendering ||
                (cameraData.xr != null && cameraData.xr.enabled) || XRSettings.isDeviceActive ||
                (cameraData.targetTexture != null && !IncludeOffscreenCaptures) || cameraData.isHDROutputActive) return false;
            var camera = cameraData.camera;
            return camera != null && IsDesktopScene(camera.gameObject.scene);
        }

        /// <summary>Material for this frame, or null when the pass must be skipped.</summary>
        public Material PrepareMaterial()
        {
            if (!Active) return null;
            var profile = ActiveProfile;
            if (profile.strength <= 0) return null;
            if (material == null)
            {
                if (shader == null) shader = Resources.Load<Shader>(ShaderResource);
                if (shader == null || !shader.isSupported)
                {
                    if (!shaderMissingLogged) Debug.LogWarning("[RetroPS2] Shader Resources/" + ShaderResource + " missing or unsupported; PS2 look skipped.");
                    shaderMissingLogged = true;
                    return null;
                }
                material = new Material(shader) { name = "RetroPS2 (" + name + ")", hideFlags = HideFlags.HideAndDontSave };
            }
            material.SetFloat(Strength, profile.strength);
            material.SetFloat(Lines, profile.virtualLines);
            material.SetFloat(Sharpness, profile.pixelSharpness);
            material.SetFloat(Levels, profile.colorLevels);
            material.SetFloat(Dither, profile.dither);
            material.SetFloat(Bleed, profile.chromaBleed);
            return material;
        }

        void OnDisable()
        {
            CoreUtils.Destroy(material);
            material = null;
        }
    }
}
