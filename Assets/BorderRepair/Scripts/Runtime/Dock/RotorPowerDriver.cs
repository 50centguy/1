using BorderRepair.Motion;
using UnityEngine;

namespace BorderRepair.Dock
{
    /// <summary>
    /// 七号两台涡轮转子的供电表现。
    /// - 未接管时（悬停）：不干预，转子由 Animator 的 Idle_Hover 片段驱动。
    /// - 接管后（落座后）：通电按悬停转速旋转，断电后按减速曲线降到停止，通电后按加速曲线回到悬停转速。
    /// 时长和曲线来自 <see cref="Unit07RotorMotionConfig"/>（没接配置时用与原代码相同的默认值：2.5 s 匀减速、1.0 s 匀加速）。
    ///
    /// 谁写转子骨骼：
    /// - 七号用可编辑动画控制器时（控制器里有 Rotors 层和 Rotor_Angle 状态）：本组件只计算转角，每帧在 Animator 求值前
    ///   把 Rotors 层定位到 Rotor_Angle 状态的对应时刻，由 Animator 写转子骨骼——转子骨骼任何时刻只有 Animator 一个写入者。
    ///   （实测：Generic 骨架下，只要控制器里任何片段绑定了某块骨骼，Animator 每帧都会写它，遮罩、Write Defaults 关都挡不住，
    ///   所以不能靠“让 Animator 不写”，只能让脚本不写。）
    /// - 原控制器（没有 Rotors 层）：保持原来的做法，在 LateUpdate 里覆盖转子骨骼（Animator 先写、本组件后写）。
    /// 转轴（转子骨骼本地坐标）由编辑器构建脚本从 Idle_Hover 片段实测后写入。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class RotorPowerDriver : MonoBehaviour
    {
        [SerializeField] Transform[] rotors = new Transform[0];
        [SerializeField] Vector3[] localAxes = new Vector3[0];
        [Tooltip("Idle_Hover 中转子的转速：两秒转 720°。")]
        [SerializeField] float idleSpeedDegPerSec = 360f;
        [Tooltip("转子启停的时长与曲线（配置资产）。为空时用默认值：断电 2.5 s、通电 1.0 s，线性。")]
        [SerializeField] Unit07RotorMotionConfig motion;
        [Header("可编辑控制器里的转子层（没有这个层时用原来的做法）")]
        [SerializeField] string rotorLayerName = "Rotors";
        [SerializeField] string rotorSpinState = "Rotor_Spin";
        [Tooltip("把转子从静止姿态绕转轴转 0–360° 的辅助状态（1 秒 = 360°），接管时由本组件定位时刻。")]
        [SerializeField] string rotorAngleState = "Rotor_Angle";

        enum Ramp { Steady, Down, Up }

        Quaternion[] baseRotations = new Quaternion[0];
        Quaternion[] restRotations = new Quaternion[0];
        float angle;
        float level;            // 转速 / 悬停转速，0–1
        float rampT;            // 当前加速 / 减速曲线上的时间进度 0–1
        Ramp ramp = Ramp.Steady;
        bool powered = true;
        Unit07RotorMotionConfig defaults;
        Animator animator;
        int rotorLayer = -1, angleStateHash;

        public bool Driven { get; private set; }
        /// <summary>接管期间由 Animator 的 Rotor_Angle 状态写转子骨骼（true），还是本组件直接写（false，原控制器）。</summary>
        public bool DrivesThroughAnimator { get; private set; }
        public bool Powered => powered;
        public float SpeedDegPerSec => Driven ? idleSpeedDegPerSec * level : (powered ? idleSpeedDegPerSec : 0f);
        public bool IsStopped => Driven && !powered && level <= 0f;
        /// <summary>通电且在悬停转速（接管中且已加速完成，或由 Animator 驱动）。</summary>
        public bool IsAtIdleSpeed => powered && (!Driven || level >= 1f);
        public float IdleSpeedDegPerSec => idleSpeedDegPerSec;
        public float AngleDeg => angle;
        public Unit07RotorMotionConfig Motion => motion != null ? motion : (defaults != null ? defaults : defaults = Unit07RotorMotionConfig.CreateDefault());
        public float SpinDownSeconds => Motion.spinDown.Seconds;
        public float SpinUpSeconds => Motion.spinUp.Seconds;
        public Transform[] Rotors => rotors;
        public Vector3[] LocalAxes => localAxes;
        public string RotorLayerName => rotorLayerName;
        public string RotorSpinState => rotorSpinState;
        public string RotorAngleState => rotorAngleState;

        public void Configure(Transform[] rotorBones, Vector3[] axes, float idleSpeed)
        {
            rotors = rotorBones;
            localAxes = axes;
            idleSpeedDegPerSec = idleSpeed;
        }

        public void SetMotionConfig(Unit07RotorMotionConfig config) => motion = config;

        void Awake() => CaptureRest();

        /// <summary>转子的静止姿态（预制体的静态姿态 = Idle_Hover 第 0 帧），在 Animator 第一次求值前记录。</summary>
        void CaptureRest()
        {
            restRotations = new Quaternion[rotors.Length];
            for (int i = 0; i < rotors.Length; i++) restRotations[i] = rotors[i] != null ? rotors[i].localRotation : Quaternion.identity;
        }

        /// <summary>从 Animator 当前写入的转子姿态接管，保持当前转速，避免跳变。</summary>
        public void TakeOver()
        {
            if (Driven) return;
            baseRotations = new Quaternion[rotors.Length];
            for (int i = 0; i < rotors.Length; i++)
                baseRotations[i] = rotors[i].localRotation;
            level = powered ? 1f : 0f;
            ramp = Ramp.Steady;
            Driven = true;
            DrivesThroughAnimator = FindAngleState();
            // 经 Animator 写时，转角从“静止姿态起算”：接管时的转角 = 当前姿态相对静止姿态绕转轴转过的角度
            angle = DrivesThroughAnimator ? AngleFromRest(0) : 0f;
            if (DrivesThroughAnimator) PlayAngle();
        }

        /// <summary>交还给 Animator（重新悬停时使用）。</summary>
        public void Release()
        {
            Driven = false;
            if (Application.isPlaying && FindLayer()) animator.Play(rotorSpinState, rotorLayer, 0f);
            DrivesThroughAnimator = false;
        }

        public void SetPower(bool on)
        {
            if (on == powered) return;
            powered = on;
            if (!Driven) return;
            // 从当前转速接着走新的曲线（中途反向不跳变）
            if (on) { rampT = Motion.spinUp.InverseEvaluate(level); ramp = Ramp.Up; }
            else { rampT = Motion.spinDown.InverseEvaluate(1f - level); ramp = Ramp.Down; }
        }

        // 经 Animator 写：在 Update（Animator 求值之前）定位 Rotor_Angle；原控制器：在 LateUpdate（Animator 之后）直接写骨骼
        void Update() { if (Driven && DrivesThroughAnimator) Step(Time.deltaTime); }
        void LateUpdate() { if (Driven && !DrivesThroughAnimator) Step(Time.deltaTime); }

        /// <summary>推进转速和转角（Update / LateUpdate 调用；编辑模式测试可以按固定步长直接调用）。</summary>
        public void Step(float dt)
        {
            if (!Driven) return;
            if (ramp != Ramp.Steady)
            {
                var timing = ramp == Ramp.Up ? Motion.spinUp : Motion.spinDown;
                rampT = Mathf.Min(1f, rampT + dt / timing.Seconds);
                if (rampT >= 1f) { level = ramp == Ramp.Up ? 1f : 0f; ramp = Ramp.Steady; }
                else level = ramp == Ramp.Up ? timing.Evaluate(rampT) : 1f - timing.Evaluate(rampT);
            }
            angle = Mathf.Repeat(angle + idleSpeedDegPerSec * level * dt, 360f);
            if (DrivesThroughAnimator) PlayAngle();
            else WriteRotors(angle);
        }

        /// <summary>直接把两根转子转到“接管时的姿态 + angle 度”。只用于原控制器和不进 Play 的预览。</summary>
        public void WriteRotors(float angleDeg)
        {
            for (int i = 0; i < rotors.Length; i++)
            {
                if (rotors[i] == null) continue;
                var b = i < baseRotations.Length ? baseRotations[i] : rotors[i].localRotation;
                rotors[i].localRotation = b * Quaternion.AngleAxis(angleDeg, localAxes[i]);
            }
        }

        /// <summary>预览用：以当前骨骼姿态作为接管时的姿态（不改变 Driven 等运行状态）。</summary>
        public void CaptureBaseForPreview()
        {
            baseRotations = new Quaternion[rotors.Length];
            for (int i = 0; i < rotors.Length; i++) baseRotations[i] = rotors[i] != null ? rotors[i].localRotation : Quaternion.identity;
        }

        /// <summary>第 i 根转子当前姿态相对静止姿态绕转轴转过的角度（0–360°）。</summary>
        public float AngleFromRest(int i)
        {
            if (i >= rotors.Length || i >= restRotations.Length) return 0f;
            var delta = Quaternion.Inverse(restRotations[i]) * rotors[i].localRotation;
            delta.ToAngleAxis(out var a, out var axis);
            if (Vector3.Dot(axis, localAxes[i]) < 0f) a = -a;
            return Mathf.Repeat(a, 360f);
        }

        /// <summary>
        /// 加速 / 减速曲线开始后 seconds 秒，转子相对开始时转过的角度（度），以及此刻的转速比例。
        /// 与 <see cref="Step"/> 用同样的积分方式（固定步长 1/600 s），预览和测试用。
        /// </summary>
        public static float AngleAfter(MotionTiming timing, bool up, float idleSpeed, float seconds, out float speedFraction)
        {
            const float h = 1f / 600f;
            float a = 0f, t = 0f, lvl = up ? 0f : 1f;
            while (t < seconds - 1e-6f)
            {
                float dt = Mathf.Min(h, seconds - t);
                t += dt;
                float p = Mathf.Min(1f, t / timing.Seconds);
                lvl = up ? (p >= 1f ? 1f : timing.Evaluate(p)) : (p >= 1f ? 0f : 1f - timing.Evaluate(p));
                a += idleSpeed * lvl * dt;
            }
            speedFraction = lvl;
            return a;
        }

        bool FindLayer()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return false;
            rotorLayer = animator.GetLayerIndex(rotorLayerName);
            return rotorLayer >= 0;
        }

        bool FindAngleState()
        {
            if (!Application.isPlaying || !FindLayer()) return false;
            angleStateHash = Animator.StringToHash(rotorAngleState);
            return animator.HasState(rotorLayer, angleStateHash);
        }

        void PlayAngle() => animator.Play(angleStateHash, rotorLayer, angle / 360f);
    }
}
