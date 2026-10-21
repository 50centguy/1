using System;
using BorderRepair.TwoNight;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BorderRepair.Art.UnifiedClinic
{
    /// <summary>Scene-local look. Inspection uses a separate, sharper Volume profile.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class RetroClinicCamera : MonoBehaviour
    {
        [Serializable]
        public struct RendererBinding
        {
            public UniversalRenderPipelineAsset pipeline;
            public int rendererIndex;
        }

        public UnifiedClinicDirector director;
        public bool inspectionCamera;
        [Range(0, .35f)] public float diffusion = .16f;
        [Range(0, .15f)] public float inspectionDiffusion = .025f;
        [Range(.25f, 4f)] public float radiusPixels = 1.3f;
        [HideInInspector] public int originalRendererIndex = -1;
        [HideInInspector] public LayerMask originalVolumeMask = 1;
        [HideInInspector] public RendererBinding[] rendererBindings;
        public const int RoomVolumeLayer = 30;
        public const int InspectionVolumeLayer = 31;

        Camera view;
        public bool ReadabilityMode => inspectionCamera ||
            (Application.isPlaying && director != null && director.Initialized && !director.RoomWalking);
        public float EffectiveDiffusion => ReadabilityMode ? inspectionDiffusion : diffusion;

        void OnEnable()
        {
            view = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            if (view == null) return;
            var data = view.GetUniversalAdditionalCameraData();
            data.volumeLayerMask = originalVolumeMask;
            data.SetRenderer(originalRendererIndex);
        }

        void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == view) ApplyCameraSettings();
        }

        public void ApplyCameraSettings()
        {
            if (view == null) view = GetComponent<Camera>();
            var data = view.GetUniversalAdditionalCameraData();
            int layer = ReadabilityMode ? InspectionVolumeLayer : RoomVolumeLayer;
            data.volumeLayerMask = originalVolumeMask.value | (1 << layer);
            var pipeline = (QualitySettings.renderPipeline != null
                ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            if (rendererBindings != null)
                foreach (var binding in rendererBindings)
                    if (binding.pipeline == pipeline) { data.SetRenderer(binding.rendererIndex); break; }
        }
    }
}
