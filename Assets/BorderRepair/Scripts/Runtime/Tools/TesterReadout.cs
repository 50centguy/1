using UnityEngine;

namespace BorderRepair.Tools
{
    /// <summary>
    /// 检测仪主机屏幕的读数：屏幕贴图集里并排放着三种读数（READY / LIM BYPASS / LOG DUMP），
    /// 通过属性块改 _BaseMap 的偏移切换，不改材质资产。
    /// 可选：给三个读数各配一个屏幕材质预设（BorderRepair/DiagnosticScreen：暗绿 / 警示红橙 / 冷白），
    /// 切换时换成对应预设，并记下切换时间，让 shader 只在切换后短暂跳变一次。不配预设时与原来相同。
    /// </summary>
    public class TesterReadout : MonoBehaviour
    {
        public enum Reading { Ready, Bypass, Log }

        static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        static readonly int SwitchTimeId = Shader.PropertyToID("_SwitchTime");

        [SerializeField] Renderer screen;
        [SerializeField] float regionWidth = 1f / 3f;
        [Tooltip("可选：READY / BYPASS / LOG 三个屏幕材质预设；为空时保持屏幕原材质")]
        [SerializeField] Material[] presets = new Material[0];
        [Tooltip("关掉后即使配了预设也用原材质（对比、回退用）")]
        [SerializeField] bool usePresets = true;

        MaterialPropertyBlock block;
        Material original;

        public Reading Current { get; private set; } = Reading.Ready;
        public Renderer Screen => screen;
        public System.Collections.Generic.IReadOnlyList<Material> Presets => presets;
        public bool PresetsActive => usePresets && presets != null && presets.Length == 3 && presets[0] != null;
        /// <summary>最近一次切换读数的时间（Time.time：URP 运行时给 shader 的 _Time.y 用的就是它）。</summary>
        public float LastSwitchTime { get; private set; } = -100f;

        /// <summary>供编辑器生成工具配置。</summary>
        public void Configure(Renderer screenRenderer) => screen = screenRenderer;

        public void ConfigurePresets(Material ready, Material bypass, Material log)
        {
            presets = new[] { ready, bypass, log };
        }

        void Awake()
        {
            if (screen != null) original = screen.sharedMaterial;
            Apply(Reading.Ready, false);
        }

        public void Show(Reading reading) => Apply(reading, reading != Current);

        /// <summary>打开 / 关闭屏幕 shader 预设（用于前后对比）；关闭时换回原材质。</summary>
        public void SetPresetsEnabled(bool enabled)
        {
            usePresets = enabled;
            Apply(Current, false);
        }

        void Apply(Reading reading, bool changed)
        {
            Current = reading;
            if (screen == null) return;
            if (original == null) original = screen.sharedMaterial;
            // 只换这个渲染器引用的材质，不修改任何材质资产
            var mat = PresetsActive ? presets[(int)reading] : original;
            if (screen.sharedMaterial != mat) screen.sharedMaterial = mat;
            block ??= new MaterialPropertyBlock();
            screen.GetPropertyBlock(block);
            block.SetVector(BaseMapSt, new Vector4(1f, 1f, regionWidth * (int)reading, 0f));
            if (changed) LastSwitchTime = Time.time;
            block.SetFloat(SwitchTimeId, LastSwitchTime);
            screen.SetPropertyBlock(block);
        }
    }
}
