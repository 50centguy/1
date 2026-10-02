using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BorderRepair.FirstOrder
{
    /// <summary>
    /// 故障美术真实场景验收的观感调整（只放在布局 A/B 测试副本里，不进正式场景）：
    /// 一组可以整体开 / 关的场景级改动——反射探针、保养标记的材质替换、可选的补光和镜头抗锯齿。
    /// 关掉时场景与调整前完全一致，所以验收可以在同一帧、同一机位拍修改前 / 后。
    /// 不改任何美术源材质、RobotV4 资源和维修流程。
    /// </summary>
    public class FaultArtLookFix : MonoBehaviour
    {
        [System.Serializable] public class MaterialSwap { public Renderer renderer; public Material original, fixedMaterial; }
        [System.Serializable] public class LightTweak { public Light light; public float originalIntensity, fixedIntensity; }

        public bool applyOnStart = true;
        public List<ReflectionProbe> probes = new List<ReflectionProbe>();
        public List<Light> addedLights = new List<Light>();
        public List<MaterialSwap> swaps = new List<MaterialSwap>();
        public List<LightTweak> lightTweaks = new List<LightTweak>();
        public Camera gameCamera;
        public AntialiasingMode originalAA = AntialiasingMode.None, fixedAA = AntialiasingMode.None;

        public bool Applied { get; private set; }

        void Start()
        {
            Apply(applyOnStart);
        }

        public void Apply(bool on)
        {
            foreach (var p in probes) if (p != null) p.enabled = on;
            foreach (var l in addedLights) if (l != null) l.enabled = on;
            foreach (var s in swaps) if (s.renderer != null) s.renderer.sharedMaterial = on ? s.fixedMaterial : s.original;
            foreach (var t in lightTweaks) if (t.light != null) t.light.intensity = on ? t.fixedIntensity : t.originalIntensity;
            if (gameCamera != null) gameCamera.GetUniversalAdditionalCameraData().antialiasing = on ? fixedAA : originalAA;
            Applied = on;
        }
    }
}
