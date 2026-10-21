using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BorderRepair.Art.UnifiedClinic
{
    /// <summary>Serialized baseline for the editor's Restore Previous Look command.</summary>
    public sealed class RetroClinicLookState : MonoBehaviour
    {
        [Serializable] public struct MaterialPair { public Material original, styled; }
        [Serializable] public struct LightState { public Light light; public Color color; public float intensity; }
        [Serializable] public struct CameraState
        {
            public Camera camera;
            public int rendererIndex;
            public LayerMask volumeMask;
            public bool postProcessing;
        }
        public List<MaterialPair> materials = new List<MaterialPair>();
        public List<LightState> lights = new List<LightState>();
        public List<CameraState> cameras = new List<CameraState>();
        public AmbientMode ambientMode;
        public Color sky, equator, ground;
    }
}
