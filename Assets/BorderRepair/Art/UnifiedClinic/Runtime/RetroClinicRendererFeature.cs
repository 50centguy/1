using BorderRepair.Art.UnifiedClinic.PS2;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BorderRepair.Art.UnifiedClinic
{
    /// <summary>
    /// Uses URP's own full-screen pass implementation, including RenderGraph support.
    /// Diffusion stays before post-processing (HDR, ahead of grading). The optional PS2 look is a second official
    /// FullScreenPass after post-processing, so it quantizes the final graded image once; it only runs for cameras
    /// a desktop scene opted in at runtime (<see cref="RetroPS2CameraEffect"/>), never for VR/XR or editor views.
    /// </summary>
    public sealed class RetroClinicRendererFeature : FullScreenPassRendererFeature
    {
        [SerializeField] Material templateMaterial;
        Material instance;
        bool created;
        FullScreenPassRendererFeature ps2Pass; // runtime-only helper, never serialized into the renderer asset
        static readonly int Blend = Shader.PropertyToID("_DiffusionBlend");
        static readonly int Radius = Shader.PropertyToID("_RadiusPixels");

        public void Configure(Material material) { templateMaterial = material; Create(); }

        public override void Create()
        {
            if (created) base.Dispose(true);
            CoreUtils.Destroy(instance);
            instance = templateMaterial == null ? null : new Material(templateMaterial) { hideFlags = HideFlags.HideAndDontSave };
            passMaterial = templateMaterial;
            fetchColorBuffer = true;
            injectionPoint = InjectionPoint.BeforeRenderingPostProcessing;
            requirements = ScriptableRenderPassInput.None;
            base.Create();
            created = true;
            CreatePS2Pass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            var look = camera.GetComponent<RetroClinicCamera>();
            if (look == null || !look.isActiveAndEnabled ||
                renderingData.cameraData.renderType == CameraRenderType.Overlay) return;
            var ps2 = RetroPS2CameraEffect.Eligible(ref renderingData.cameraData) ? camera.GetComponent<RetroPS2CameraEffect>() : null;
            // Scale is exactly 1 when the PS2 look is off or absent, so the original diffusion is unchanged.
            float diffusion = look.EffectiveDiffusion * (ps2 != null ? ps2.DiffusionScale : 1f);
            if (instance != null && diffusion > 0)
            {
                instance.SetFloat(Blend, diffusion);
                instance.SetFloat(Radius, look.radiusPixels);
                passMaterial = instance;
                try { base.AddRenderPasses(renderer, ref renderingData); }
                finally { passMaterial = templateMaterial; }
            }
            var ps2Material = ps2 != null ? ps2.PrepareMaterial() : null;
            if (ps2Material == null) return;
            if (ps2Pass == null) CreatePS2Pass();
            ps2Pass.passMaterial = ps2Material;
            try { ps2Pass.AddRenderPasses(renderer, ref renderingData); }
            finally { ps2Pass.passMaterial = null; }
        }

        void CreatePS2Pass()
        {
            DestroyPS2Pass();
            ps2Pass = CreateInstance<FullScreenPassRendererFeature>();
            ps2Pass.name = "RetroClinic PS2 look (desktop cameras only)";
            ps2Pass.hideFlags = HideFlags.HideAndDontSave;
            ps2Pass.injectionPoint = InjectionPoint.AfterRenderingPostProcessing;
            ps2Pass.fetchColorBuffer = true;
            ps2Pass.requirements = ScriptableRenderPassInput.None;
            ps2Pass.passMaterial = null;
            ps2Pass.Create();
        }

        void DestroyPS2Pass()
        {
            if (ps2Pass == null) return;
            ps2Pass.Dispose();
            CoreUtils.Destroy(ps2Pass);
            ps2Pass = null;
        }

        void OnDisable() => DestroyPS2Pass();

        protected override void Dispose(bool disposing)
        {
            if (created) base.Dispose(disposing);
            created = false;
            CoreUtils.Destroy(instance);
            instance = null;
            DestroyPS2Pass();
        }
    }
}
