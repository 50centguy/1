using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BorderRepair.Art.UnifiedClinic
{
    /// <summary>Uses URP's own full-screen pass implementation, including RenderGraph support.</summary>
    public sealed class RetroClinicRendererFeature : FullScreenPassRendererFeature
    {
        [SerializeField] Material templateMaterial;
        Material instance;
        bool created;
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
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            var look = camera.GetComponent<RetroClinicCamera>();
            if (look == null || !look.isActiveAndEnabled || instance == null ||
                renderingData.cameraData.renderType == CameraRenderType.Overlay || look.EffectiveDiffusion <= 0) return;
            instance.SetFloat(Blend, look.EffectiveDiffusion);
            instance.SetFloat(Radius, look.radiusPixels);
            passMaterial = instance;
            try { base.AddRenderPasses(renderer, ref renderingData); }
            finally { passMaterial = templateMaterial; }
        }

        protected override void Dispose(bool disposing)
        {
            if (created) base.Dispose(disposing);
            created = false;
            CoreUtils.Destroy(instance);
            instance = null;
        }
    }
}
