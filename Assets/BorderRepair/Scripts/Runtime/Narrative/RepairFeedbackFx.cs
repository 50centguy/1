using System;
using System.Collections;
using System.Collections.Generic;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Inspection;
using UnityEngine;
using Cue = BorderRepair.Narrative.RepairSoundSynth.Cue;

namespace BorderRepair.Narrative
{
    /// <summary>
    /// 维修操作的简短反馈（只放在叙事场景里）：每个步骤一个可辨认的声音 + 一个视觉提示，不使用物理。
    /// - 卸螺丝：螺丝先转 3 圈退出，再落到零件盘；棘轮“咔哒”声；若撕开封条，追加撕纸声和封条处的红色环。
    /// - 开盖：盖板先向外弹起并翘起一点，再放到零件盘；闷响 + 刮擦声；大一圈的白色环。
    /// - 探测限力器：红色指示灯闪烁后常亮；下行警告双音；红色环。
    /// - 读取日志：检测插头闪烁，三道琥珀色环依次扩散；数据啁啾声 + 完成提示音。
    /// 其他检测 / 换件用通用的确认音和环；操作被拒绝时一声低频蜂鸣。
    /// 规则仍在 RepairSession 中；本组件只订阅事件做表现。
    /// </summary>
    public class RepairFeedbackFx : MonoBehaviour
    {
        [Serializable]
        public class StepCue
        {
            public string stepId;
            public Cue cue;
        }

        [SerializeField] RepairStationController controller;
        [SerializeField] AudioSource audioSource;
        [Tooltip("扩散环使用的透明材质（顶点色控制颜色与淡出）")]
        [SerializeField] Material ringMaterial;
        [SerializeField] List<StepCue> stepCues = new List<StepCue>();
        [SerializeField, Range(0f, 1f)] float volume = 0.7f;
        [Header("零件动作（工具动画按同样的参数同步）")]
        [SerializeField] float unscrewSeconds = 0.7f;
        [SerializeField] float unscrewTurns = 3f;
        [SerializeField] float unscrewTravel = 0.006f;
        [SerializeField] float coverPopSeconds = 0.32f;
        [Header("扩散环")]
        [Tooltip("开盖时是否显示扩散环（关闭后仍保留盖板弹起和声音）")]
        [SerializeField] bool ringOnOpenCover = true;
        [Tooltip("开盖扩散环的最大半径（米，随物品缩放）：限制在开舱区域附近")]
        [SerializeField] float coverRingMaxRadius = 0.028f;

        /// <summary>最近一个扩散环扩散到最大时的世界半径（米），供测试检查。</summary>
        public float LastRingMaxRadius { get; private set; }

        public float UnscrewSeconds => unscrewSeconds;
        public float UnscrewTurns => unscrewTurns;
        public float UnscrewTravel => unscrewTravel;
        public float CoverPopSeconds => coverPopSeconds;

        static readonly Color RingWhite = new Color(0.92f, 0.96f, 1f, 1f);
        static readonly Color RingRed = new Color(1f, 0.22f, 0.16f, 1f);
        static readonly Color RingAmber = new Color(1f, 0.7f, 0.2f, 1f);
        static readonly Color RingCyan = new Color(0.35f, 0.9f, 1f, 1f);
        static readonly Color RingGreen = new Color(0.45f, 1f, 0.5f, 1f);

        readonly Dictionary<Cue, AudioClip> clips = new Dictionary<Cue, AudioClip>();
        readonly List<string> log = new List<string>();
        RepairSession session;

        /// <summary>按顺序记录已播放的反馈（“cue@pointId”），供测试和调试。</summary>
        public IReadOnlyList<string> CueLog => log;
        public Cue LastCue { get; private set; }
        public int ActiveRings { get; private set; }
        public event Action<Cue, string> CuePlayed;

        /// <summary>供编辑器生成工具配置。</summary>
        public void Configure(RepairStationController c, AudioSource source, Material ring, List<StepCue> cues)
        {
            controller = c;
            audioSource = source;
            ringMaterial = ring;
            stepCues = cues;
        }

        void Start()
        {
            if (controller == null || controller.Session == null) { enabled = false; return; }
            session = controller.Session;
            session.StepPerformed += OnStepPerformed;
            session.Feedback += OnFeedback;
        }

        void OnDestroy()
        {
            if (session == null) return;
            session.StepPerformed -= OnStepPerformed;
            session.Feedback -= OnFeedback;
        }

        void OnFeedback(string message, FeedbackKind kind)
        {
            // 检查阶段里被拒绝的操作（工具不对 / 顺序不对）给一声低频蜂鸣
            if (kind == FeedbackKind.Warning && session.Stage == RepairStage.Inspect) Play(Cue.Deny, null);
        }

        void OnStepPerformed(RepairStepDefinition step)
        {
            Play(CueFor(step), step.targetPointId);
            if (!string.IsNullOrEmpty(step.sideEffectPointId) && step.sideEffectState == RepairPartState.Torn)
                StartCoroutine(Delayed(0.14f, () => Play(Cue.SealTear, step.sideEffectPointId)));
        }

        public Cue CueFor(RepairStepDefinition step)
        {
            foreach (var c in stepCues)
                if (c != null && c.stepId == step.stepId) return c.cue;
            switch (step.action)
            {
                case RepairActionType.RemoveFastener: return Cue.Unscrew;
                case RepairActionType.OpenHousing: return Cue.OpenCover;
                default: return step.resultState == RepairPartState.Replaced ? Cue.Replace : Cue.Probe;
            }
        }

        /// <summary>播放一个反馈：声音 + 零件上的视觉提示。pointId 为空时只有声音。</summary>
        public void Play(Cue cue, string pointId)
        {
            if (cue == Cue.None) return;
            LastCue = cue;
            log.Add(string.IsNullOrEmpty(pointId) ? cue.ToString() : $"{cue}@{pointId}");
            PlaySound(cue);

            RepairPart part = null;
            InspectionPoint point = null;
            if (!string.IsNullOrEmpty(pointId))
            {
                controller.TryGetPart(pointId, out part);
                controller.TryGetPoint(pointId, out point);
            }
            switch (cue)
            {
                case Cue.Unscrew:
                    if (part != null) part.PlayExit(Vector3.up, unscrewTravel, unscrewTurns, 0f, unscrewSeconds);   // 螺丝轴线为自身 +Y（朝外）
                    Ring(point, RingWhite, 0.5f, 1.8f, 0.4f, 0f);
                    break;
                case Cue.SealTear:
                    Ring(point, RingRed, 0.6f, 1.6f, 0.35f, 0f);
                    break;
                case Cue.OpenCover:
                    if (part != null) part.PlayExit(Vector3.back, 0.012f, 0f, -12f, coverPopSeconds);  // 盖板外侧为自身 -Z
                    // 盖板很大：按盖板大小算的环会铺满画面。环限制在开舱区域附近的固定大小；也可以只对开盖关闭环
                    if (ringOnOpenCover) Ring(point, RingWhite, 0.6f, 1f, 0.45f, 0f, coverRingMaxRadius);
                    break;
                case Cue.ProbeWarning:
                    Ring(point, RingRed, 0.8f, 2.4f, 0.5f, 0f);
                    Ring(point, RingRed, 0.8f, 2.4f, 0.5f, 0.28f);
                    // 红灯接触时立刻亮起并保持常亮（“旁路”线索要看得清）；只用发光强度短暂增强来吸引注意，不熄灭
                    if (part != null) StartCoroutine(Glow(part, 2.5f, 0.6f));    // 短暂增强到稳定值的 2.5 倍，0.6 秒内回落到稳定的红色
                    break;
                case Cue.DataRead:
                    // 接口的包围盒含检测线，本身就比较大：环的放大倍数取小一些
                    for (int i = 0; i < 3; i++) Ring(point, RingAmber, 0.45f, 1.25f, 0.45f, i * 0.22f);
                    if (part != null) StartCoroutine(Blink(part, 5, 0.08f));
                    break;
                case Cue.Probe:
                    Ring(point, RingCyan, 0.7f, 1.8f, 0.4f, 0f);
                    break;
                case Cue.Replace:
                    Ring(point, RingGreen, 0.7f, 1.8f, 0.45f, 0f);
                    break;
            }
            CuePlayed?.Invoke(cue, pointId);
        }

        void PlaySound(Cue cue)
        {
            if (audioSource == null) return;
            if (!clips.TryGetValue(cue, out var clip))
            {
                clip = RepairSoundSynth.Create(cue);
                clips[cue] = clip;
            }
            if (clip != null) audioSource.PlayOneShot(clip, volume);
        }

        public AudioClip ClipFor(Cue cue)
        {
            if (!clips.TryGetValue(cue, out var clip)) clips[cue] = clip = RepairSoundSynth.Create(cue);
            return clip;
        }

        static IEnumerator Delayed(float seconds, Action action)
        {
            yield return new WaitForSeconds(seconds);
            action();
        }

        /// <summary>指示物闪烁 count 次，最后回到与状态一致的显示。</summary>
        static IEnumerator Blink(RepairPart part, int count, float interval)
        {
            var indicator = part.TestedIndicator;
            if (indicator == null) yield break;
            for (int i = 0; i < count * 2; i++)
            {
                yield return new WaitForSeconds(interval);
                if (part == null || indicator == null) yield break;
                indicator.SetActive(!indicator.activeSelf);
            }
            if (indicator != null) indicator.SetActive(part.State == RepairPartState.Tested);
        }

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Tooltip("红灯稳定亮起时的发光强度（相对材质原值）。原值约 6 倍，没有色调映射时会被压成黄白色，看不出是红灯")]
        [SerializeField] float indicatorSteadyGain = 0.25f;
        [Tooltip("指示灯发光的绿 / 蓝分量保留比例：越小越接近纯红，避免高亮时被截成黄白或粉色")]
        [SerializeField, Range(0f, 1f)] float indicatorHueKeep = 0.15f;

        /// <summary>
        /// 指示灯立即常亮：先短暂增强（peak 为相对稳定值的倍数），seconds 秒内回落到稳定强度并保持，全程不熄灭。
        /// 只改指示物里发光材质渲染器的属性块，不改材质资产。
        /// </summary>
        IEnumerator Glow(RepairPart part, float peak, float seconds)
        {
            float steady = indicatorSteadyGain;
            peak *= steady;
            var indicator = part.TestedIndicator;
            if (indicator == null) yield break;
            indicator.SetActive(part.State == RepairPartState.Tested);
            var targets = new List<(Renderer r, Vector4 baseColor)>();   // 按原始数值读写，避免 GetColor/SetColor 的色彩空间换算把颜色抬亮
            foreach (var r in indicator.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterial != null && r.sharedMaterial.IsKeywordEnabled("_EMISSION"))
                {
                    var c = r.sharedMaterial.GetVector(EmissionColorId);
                    targets.Add((r, new Vector4(c.x, c.y * indicatorHueKeep, c.z * indicatorHueKeep, c.w)));
                }
            var block = new MaterialPropertyBlock();
            float t = 0f;
            while (t < seconds)
            {
                if (part == null || indicator == null) yield break;
                float k = t / seconds;
                float gain = steady + (peak - steady) * Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 2f)) * (1f - k);
                foreach (var (r, c) in targets)
                {
                    r.GetPropertyBlock(block);
                    block.SetVector(EmissionColorId, c * gain);
                    r.SetPropertyBlock(block);
                }
                t += Time.deltaTime;
                yield return null;
            }
            foreach (var (r, c) in targets)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetVector(EmissionColorId, c * steady);
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// 在部位前方生成一个面向镜头、扩散并淡出的环。半径按部位大小计算；maxRadius（米，随物品缩放）&gt; 0 时限制最大半径。
        /// </summary>
        void Ring(InspectionPoint point, Color color, float fromScale, float toScale, float seconds, float delay, float maxRadius = 0f)
        {
            if (point == null || ringMaterial == null) return;
            var renderers = point.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0) return;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float limit = maxRadius > 0f ? maxRadius * point.transform.lossyScale.x : 0f;
            StartCoroutine(RingRoutine(b, color, fromScale, toScale, seconds, delay, limit));
        }

        IEnumerator RingRoutine(Bounds b, Color color, float fromScale, float toScale, float seconds, float delay, float limit)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            var cam = controller.Inspector.ViewCamera;
            float radius = Mathf.Max(0.012f, b.extents.magnitude);
            if (limit > 0f) radius = Mathf.Min(radius, limit);
            LastRingMaxRadius = radius * toScale;
            var go = new GameObject("FxRing");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = ringMaterial;
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            const int segments = 40;
            lr.positionCount = segments;
            lr.widthMultiplier = Mathf.Min(radius * 0.16f, 0.004f);
            ActiveRings++;
            float t = 0f;
            while (t < seconds)
            {
                float k = t / seconds;
                // 放在部位前方（朝镜头一侧），面向镜头，避免被部位自身挡住
                var toCam = (cam.transform.position - b.center).normalized;
                go.transform.position = b.center + toCam * radius;
                go.transform.rotation = Quaternion.LookRotation(-toCam, cam.transform.up);
                float r = radius * Mathf.Lerp(fromScale, toScale, 1f - (1f - k) * (1f - k));
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
                }
                var c = color;
                c.a = 1f - k;
                lr.startColor = lr.endColor = c;
                t += Time.deltaTime;
                yield return null;
            }
            ActiveRings--;
            Destroy(go);
        }
    }
}
