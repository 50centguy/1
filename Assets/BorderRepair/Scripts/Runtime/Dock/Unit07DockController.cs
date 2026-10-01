using UnityEngine;

namespace BorderRepair.Dock
{
    public enum DockState
    {
        Hovering = 0,       // 七号在锚点上方悬停，夹具闭合，通电
        ClampsOpening = 1,
        Descending = 2,     // 夹具已张开，七号竖直落座
        SeatedOpen = 3,     // 已落座，夹具张开
        Clamping = 4,
        Clamped = 5,        // 已夹紧，通电，转子在悬停转速
        SpinningDown = 6,   // 已断电，转子减速中
        RotorsStopped = 7,  // 已断电，转子静止：允许检查左引擎
        // ---- 第二阶段：维修结束后离座（数值接在后面，第一阶段的数值不变）
        SpinningUp = 8,     // 已确认可以结束维修、恢复供电，转子正在加速回悬停转速；夹具保持夹紧
        LiftingOff = 9,     // 夹具已松开，七号竖直升起离座（转子仍由 RotorPowerDriver 按悬停转速驱动）
        Undocked = 10,      // 已离座悬停：转子交还 Animator（Idle_Hover），夹具保持张开
    }

    /// <summary>
    /// UNIT 07 维修座停靠流程。
    /// 第一阶段：夹具张开 → 七号落座 → 夹紧 → 开关 OFF → 涡轮停转 → 允许检查左引擎。
    /// 第二阶段（维修结束后）：确认可以结束维修（<see cref="IDockServiceCompletionGate"/>）→ 开关 ON、转子回到悬停转速 →
    /// 松开夹具 → 七号升起离座 → 转子控制权交还 Animator（Idle_Hover）。
    /// - 夹具开合角读取维修座 FBX 的自定义属性 unity_open_deg（导入时写入 DockPartProperties），并与 Blender 的 open_deg 核对符号；
    ///   不使用 Blender 的 open_deg 驱动。开关手柄角读取 unity_on_deg / unity_off_deg。
    /// - 手柄、状态灯、转子随供电状态一致；断电维修之后恢复供电必须先通过“允许结束维修”接口。
    /// - 各阶段拒绝越序操作，并在 LastMessage 里给出原因；被拒绝的操作不改变任何状态。
    /// 本组件不包含引擎拆卸、工单或正式维修场景逻辑。
    /// </summary>
    public class Unit07DockController : MonoBehaviour
    {
        [Header("七号")]
        [SerializeField] Transform robotRoot;
        [SerializeField] Animator robotAnimator;
        [SerializeField] RotorPowerDriver rotorDriver;
        [SerializeField] string hoverStateName = "Idle_Hover";
        [Tooltip("落座后的静止姿态（身体不再上下浮动）。使用 RobotV4 现有片段，不新建动画。")]
        [SerializeField] string seatedStateName = "Pose_Gripper_Closed";

        [Header("维修座")]
        [SerializeField] Transform robotAnchor;
        [SerializeField] Transform clampL;
        [SerializeField] Transform clampR;
        [SerializeField] Transform powerLever;
        [SerializeField] Renderer statusLamp;

        [Header("允许结束维修（以后由工单 / 维修流程实现 IDockServiceCompletionGate）")]
        [SerializeField] MonoBehaviour serviceGateBehaviour;

        [Header("节奏")]
        [SerializeField] float hoverHeight = 0.12f;
        [SerializeField] float clampSeconds = 0.6f;
        [SerializeField] float descendSeconds = 1.2f;
        [SerializeField] float liftSeconds = 1.2f;
        [SerializeField] float leverSeconds = 0.3f;
        [SerializeField] float handoverBlendSeconds = 0.2f;

        [Header("状态灯")]
        [SerializeField] Color lampOnColor = new Color(0.25f, 1f, 0.35f);
        [SerializeField] Color lampOffColor = new Color(1f, 0.62f, 0.12f);
        [SerializeField] float lampEmission = 3f;

        float unityOpenL, unityOpenR, unityOnDeg, unityOffDeg;
        Quaternion clampClosedL, clampClosedR, leverRest;
        float clampFraction;            // 0 = 夹紧，1 = 张开
        float leverFraction;            // 0 = ON，1 = OFF
        float timer;
        MaterialPropertyBlock lampBlock;
        bool configured, initialized;
        IDockServiceCompletionGate serviceGate;

        public DockState State { get; private set; } = DockState.Hovering;
        public bool PowerOn { get; private set; } = true;
        public string LastMessage { get; private set; } = string.Empty;
        public float ClampOpenFraction => clampFraction;
        public float LeverOffFraction => leverFraction;
        public float UnityOpenDegL => unityOpenL;
        public float UnityOpenDegR => unityOpenR;
        public float UnityOnDeg => unityOnDeg;
        public float UnityOffDeg => unityOffDeg;
        public bool ConfigurationValid => configured;
        public RotorPowerDriver Rotors => rotorDriver;
        public Transform RobotRoot => robotRoot;
        public Transform RobotAnchor => robotAnchor;
        public Animator RobotAnimator => robotAnimator;
        public float HoverHeight => hoverHeight;
        public string HoverStateName => hoverStateName;
        public IDockServiceCompletionGate ServiceGate => serviceGate;

        /// <summary>七号的机身停在接触垫上（不含升起中和已离座）。</summary>
        public bool IsSeated => State == DockState.SeatedOpen || State == DockState.Clamping || State == DockState.Clamped ||
                                State == DockState.SpinningDown || State == DockState.RotorsStopped || State == DockState.SpinningUp ||
                                State == DockState.ClampsOpening && IsRobotSeatedPosition();
        /// <summary>只有断电且转子完全静止时，才允许操作叶轮、检查左引擎。</summary>
        public bool CanOperateImpeller => State == DockState.RotorsStopped;
        public bool LeftEngineInspectable => CanOperateImpeller;
        public Color LampColor => PowerOn ? lampOnColor : lampOffColor;

        public void Configure(Transform robot, Animator animator, RotorPowerDriver rotors, Transform anchor,
                              Transform clampLeft, Transform clampRight, Transform lever, Renderer lamp)
        {
            robotRoot = robot; robotAnimator = animator; rotorDriver = rotors; robotAnchor = anchor;
            clampL = clampLeft; clampR = clampRight; powerLever = lever; statusLamp = lamp;
        }

        /// <summary>接入“允许结束维修”接口。传 null 表示没有接入：断电维修之后一律不允许恢复供电。</summary>
        public void SetServiceCompletionGate(IDockServiceCompletionGate gate)
        {
            serviceGate = gate;
            serviceGateBehaviour = gate as MonoBehaviour;
        }

        /// <summary>断电维修之后能否恢复供电（问“允许结束维修”接口）。</summary>
        public bool CanRestorePower(out string reason)
        {
            reason = string.Empty;
            if (PowerOn) { reason = "已经通电。"; return false; }
            if (serviceGate == null) { reason = "没有接入“允许结束维修”接口，不能恢复供电。"; return false; }
            if (!serviceGate.CanFinishService(out reason)) { if (string.IsNullOrEmpty(reason)) reason = "维修还没允许结束。"; return false; }
            return true;
        }

        void Awake() => Initialize();
        void Start() => ResetToHover();
        void Update() => Tick(Time.deltaTime);

        /// <summary>读取属性、记录夹具和手柄的原始姿态（Awake 调用；编辑模式测试直接调用）。可重复调用。</summary>
        public void Initialize()
        {
            if (initialized) return;
            configured = ReadProperties();
            if (serviceGate == null && serviceGateBehaviour != null)
            {
                serviceGate = serviceGateBehaviour as IDockServiceCompletionGate;
                if (serviceGate == null) Debug.LogError($"[Unit07Dock] {serviceGateBehaviour.name} 没有实现 IDockServiceCompletionGate。", this);
            }
            if (!configured) return;
            clampClosedL = clampL.localRotation;
            clampClosedR = clampR.localRotation;
            // 导入后的手柄处在 OFF 位置（unity_off_deg），先换算出 0° 的静止姿态
            leverRest = powerLever.localRotation * Quaternion.AngleAxis(-unityOffDeg, Vector3.right);
            lampBlock = new MaterialPropertyBlock();
            initialized = true;
        }

        /// <summary>初始：通电悬停在锚点上方，夹具闭合，手柄在 ON（Start 调用）。</summary>
        public void ResetToHover()
        {
            if (!configured) return;
            PowerOn = true;
            rotorDriver.SetPower(true);
            rotorDriver.Release();
            clampFraction = 0f;
            leverFraction = 0f;
            ApplyClamps();
            ApplyLever();
            ApplyLamp();
            robotRoot.position = robotAnchor.position + Vector3.up * hoverHeight;
            if (robotAnimator != null && robotAnimator.isActiveAndEnabled && Application.isPlaying) robotAnimator.Play(hoverStateName, 0, 0f);
            State = DockState.Hovering;
            Say("七号在维修座上方悬停。先张开夹具。");
        }

        bool ReadProperties()
        {
            bool ok = true;
            ok &= ReadClamp(clampL, out unityOpenL);
            ok &= ReadClamp(clampR, out unityOpenR);
            var lp = powerLever != null ? powerLever.GetComponent<DockPartProperties>() : null;
            if (lp == null || !lp.TryGetFloat("unity_on_deg", out unityOnDeg) || !lp.TryGetFloat("unity_off_deg", out unityOffDeg))
            {
                Debug.LogError("[Unit07Dock] 断电开关手柄缺少 unity_on_deg / unity_off_deg 属性（维修座 FBX 导入后处理未运行？）", this);
                ok = false;
            }
            if (robotRoot == null || robotAnchor == null || rotorDriver == null)
            {
                Debug.LogError("[Unit07Dock] 七号或锚点未配置。", this);
                ok = false;
            }
            return ok;
        }

        bool ReadClamp(Transform clamp, out float unityOpen)
        {
            unityOpen = 0f;
            var p = clamp != null ? clamp.GetComponent<DockPartProperties>() : null;
            if (p == null || !p.TryGetFloat("unity_open_deg", out unityOpen))
            {
                Debug.LogError($"[Unit07Dock] 夹具 {(clamp != null ? clamp.name : "null")} 缺少 unity_open_deg 属性，拒绝使用 Blender 的 open_deg。", this);
                return false;
            }
            // 核对：FBX 导入到 Unity 时 X 轴镜像，绕本地 Y 的角度必须与 Blender 的 open_deg 反号
            if (p.TryGetFloat("open_deg", out var blenderOpen) && Mathf.Abs(blenderOpen) > 0.01f && Mathf.Sign(blenderOpen) == Mathf.Sign(unityOpen))
            {
                Debug.LogError($"[Unit07Dock] 夹具 {clamp.name} 的 unity_open_deg ({unityOpen}) 与 open_deg ({blenderOpen}) 同号，未做轴向换算。", this);
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ 交互入口

        /// <summary>玩家点击某个代理。返回 false 表示被拒绝，原因见 LastMessage。</summary>
        public bool Interact(DockAction action, DockPickable pickable = null)
        {
            if (!configured) return Say("维修座配置无效，见控制台。", false);
            switch (action)
            {
                case DockAction.Clamps: return ToggleClamps();
                case DockAction.PowerSwitch: return SetPower(!PowerOn);
                case DockAction.EngineLeft: return InspectLeftEngine();
                case DockAction.LiftOff: return LiftOff();
                case DockAction.PartsTray:
                case DockAction.MagneticBox:
                    if (pickable == null) return false;
                    pickable.Toggle();
                    return Say(pickable.Taken ? $"取下了{Label(action)}。" : $"{Label(action)}放回原处。", true);
                default:
                    return false;
            }
        }

        static string Label(DockAction a) => a == DockAction.PartsTray ? "零件盘" : "磁性零件盒";

        public bool ToggleClamps()
        {
            switch (State)
            {
                case DockState.Hovering:
                    State = DockState.ClampsOpening; timer = 0f;
                    return Say("夹具张开，七号准备落座。", true);
                case DockState.SeatedOpen:
                    State = DockState.Clamping; timer = 0f;
                    return Say("夹具合拢。", true);
                case DockState.Clamped:
                    State = DockState.ClampsOpening; timer = 0f;
                    return Say("夹具张开（七号仍停在接触垫上）。", true);
                case DockState.Undocked:
                    // 已离座悬停、夹具保持张开：直接重新落座
                    State = DockState.Descending; timer = 0f;
                    return Say("七号重新落座。", true);
                case DockState.SpinningDown:
                case DockState.RotorsStopped:
                    return Say("断电维修中，夹具保持锁紧。先恢复供电再松开夹具。", false);
                case DockState.SpinningUp:
                    return Say("转子还没回到正常转速，夹具保持锁紧。", false);
                case DockState.LiftingOff:
                    return Say("七号正在升起离座，夹具不能动作。", false);
                default:
                    return Say("夹具正在动作，请稍候。", false);
            }
        }

        public bool SetPower(bool on)
        {
            if (on == PowerOn) return Say(on ? "已经通电。" : "已经断电。", false);
            if (on)
            {
                // 断电维修之后恢复供电：先问“允许结束维修”接口
                if (!CanRestorePower(out var reason)) return Say(reason, false);
                PowerOn = true;
                rotorDriver.SetPower(true);
                ApplyLamp();
                State = DockState.SpinningUp;
                return Say("确认维修结束，恢复供电。涡轮加速中，叶轮禁止操作，夹具保持锁紧。", true);
            }
            if (State != DockState.Clamped && State != DockState.SpinningUp)
                return Say(!IsSeated ? "七号不在维修座上，不能断电。" : "先夹紧夹具再断电。", false);
            PowerOn = false;
            rotorDriver.SetPower(false);
            ApplyLamp();
            State = DockState.SpinningDown;
            return Say("断电。涡轮减速中，等叶轮停稳。", true);
        }

        public bool InspectLeftEngine()
        {
            if (State == DockState.RotorsStopped)
                return Say("叶轮已停稳。可以检查左引擎。", true);
            if (State == DockState.SpinningDown)
                return Say("叶轮还在减速转动，禁止操作。", false);
            if (PowerOn)
                return Say("七号仍在通电，涡轮在转动。先落座、夹紧并断电。", false);
            return Say("现在不能检查左引擎。", false);
        }

        /// <summary>七号升起离座：只有夹具已松开、通电且转子已回到悬停转速时允许。</summary>
        public bool LiftOff()
        {
            switch (State)
            {
                case DockState.SeatedOpen:
                    if (!PowerOn) return Say("断电中不能离座。", false);
                    if (!rotorDriver.IsAtIdleSpeed) return Say("转子还没回到正常转速，不能离座。", false);
                    State = DockState.LiftingOff; timer = 0f;
                    return Say("七号升起离座。", true);
                case DockState.Clamped:
                case DockState.Clamping:
                    return Say("夹具还夹着，先松开夹具。", false);
                case DockState.SpinningDown:
                case DockState.RotorsStopped:
                    return Say("断电维修中不能离座。先确认维修结束并恢复供电。", false);
                case DockState.SpinningUp:
                    return Say("转子还没回到正常转速，夹具也还夹着。", false);
                case DockState.LiftingOff:
                    return Say("正在升起。", false);
                case DockState.Hovering:
                case DockState.Undocked:
                    return Say("七号已经在悬停，没有落座。", false);
                default:
                    return Say("夹具或七号正在动作，请稍候。", false);
            }
        }

        bool Say(string msg, bool result = true)
        {
            LastMessage = msg;
            return result;
        }

        public string NextHint()
        {
            switch (State)
            {
                case DockState.Hovering: return "点击夹具的黄色握把：张开夹具";
                case DockState.ClampsOpening: return "夹具张开中…";
                case DockState.Descending: return "七号落座中…";
                case DockState.SeatedOpen: return PowerOn && rotorDriver.IsAtIdleSpeed ? "点击夹具握把：夹紧；或按 L：七号升起离座" : "点击夹具握把：夹紧";
                case DockState.Clamping: return "夹具合拢中…";
                case DockState.Clamped: return "点击断电开关：OFF；维修结束后点夹具握把：松开";
                case DockState.SpinningDown: return "等涡轮停转…";
                case DockState.RotorsStopped:
                    return CanRestorePower(out var why) ? "维修已允许结束：点击断电开关恢复供电" : "可以检查左引擎。结束维修前不能恢复供电：" + why;
                case DockState.SpinningUp: return "涡轮加速中…（夹具保持锁紧）";
                case DockState.LiftingOff: return "七号升起中…";
                case DockState.Undocked: return "已离座悬停（转子由 Idle_Hover 驱动）。点夹具握把可重新落座";
                default: return string.Empty;
            }
        }

        // ------------------------------------------------------------------ 每帧推进

        /// <summary>推进一帧（Update 调用；编辑模式测试也直接调用它，按固定步长推进）。</summary>
        public void Tick(float dt)
        {
            if (!configured) return;
            switch (State)
            {
                case DockState.ClampsOpening:
                    clampFraction = Mathf.MoveTowards(clampFraction, 1f, dt / clampSeconds);
                    if (clampFraction >= 1f)
                    {
                        if (IsRobotSeatedPosition())
                        {
                            State = DockState.SeatedOpen;
                            Say(PowerOn && rotorDriver.IsAtIdleSpeed ? "夹具已松开。可以让七号升起离座（L）。" : "夹具已张开。", true);
                        }
                        else { State = DockState.Descending; timer = 0f; }
                    }
                    break;
                case DockState.Descending:
                    timer += dt / descendSeconds;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(timer));
                    robotRoot.position = robotAnchor.position + Vector3.up * (hoverHeight * (1f - k));
                    if (timer >= 1f)
                    {
                        robotRoot.position = robotAnchor.position;
                        rotorDriver.TakeOver();
                        CrossFade(seatedStateName, 0.15f);
                        State = DockState.SeatedOpen;
                        Say("七号已落在接触垫上。夹紧夹具。", true);
                    }
                    break;
                case DockState.Clamping:
                    clampFraction = Mathf.MoveTowards(clampFraction, 0f, dt / clampSeconds);
                    if (clampFraction <= 0f)
                    {
                        State = DockState.Clamped;
                        Say("已夹紧。可以断电。", true);
                    }
                    break;
                case DockState.SpinningDown:
                    if (rotorDriver.IsStopped)
                    {
                        State = DockState.RotorsStopped;
                        Say("涡轮已停转。可以检查左引擎。", true);
                    }
                    break;
                case DockState.SpinningUp:
                    if (rotorDriver.IsAtIdleSpeed)
                    {
                        State = DockState.Clamped;
                        Say("涡轮回到正常转速。可以松开夹具。", true);
                    }
                    break;
                case DockState.LiftingOff:
                    timer += dt / liftSeconds;
                    float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(timer));
                    robotRoot.position = robotAnchor.position + Vector3.up * (hoverHeight * u);
                    if (timer >= 1f)
                    {
                        robotRoot.position = robotAnchor.position + Vector3.up * hoverHeight;
                        // 交还：转子不再由 RotorPowerDriver 覆盖，由 Idle_Hover 片段驱动（悬停转速相同，由第一阶段从片段实测）
                        rotorDriver.Release();
                        CrossFade(hoverStateName, handoverBlendSeconds);
                        State = DockState.Undocked;
                        Say("七号已离座悬停，转子交还 Animator。", true);
                    }
                    break;
            }
            float leverTarget = PowerOn ? 0f : 1f;
            leverFraction = Mathf.MoveTowards(leverFraction, leverTarget, dt / leverSeconds);
            ApplyClamps();
            ApplyLever();
        }

        void CrossFade(string state, float seconds)
        {
            if (robotAnimator != null && robotAnimator.isActiveAndEnabled && robotAnimator.runtimeAnimatorController != null && Application.isPlaying)
                robotAnimator.CrossFadeInFixedTime(state, seconds, 0);
        }

        bool IsRobotSeatedPosition() => (robotRoot.position - robotAnchor.position).sqrMagnitude < 1e-8f;

        void ApplyClamps()
        {
            clampL.localRotation = clampClosedL * Quaternion.AngleAxis(unityOpenL * clampFraction, Vector3.up);
            clampR.localRotation = clampClosedR * Quaternion.AngleAxis(unityOpenR * clampFraction, Vector3.up);
        }

        void ApplyLever()
        {
            float deg = Mathf.Lerp(unityOnDeg, unityOffDeg, leverFraction);
            powerLever.localRotation = leverRest * Quaternion.AngleAxis(deg, Vector3.right);
        }

        void ApplyLamp()
        {
            if (statusLamp == null) return;
            statusLamp.GetPropertyBlock(lampBlock);
            var c = LampColor;
            lampBlock.SetColor("_BaseColor", c);
            lampBlock.SetColor("_EmissionColor", c * lampEmission);
            statusLamp.SetPropertyBlock(lampBlock);
        }
    }
}
