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
        Clamped = 5,        // 已夹紧，仍通电，转子转动
        SpinningDown = 6,   // 已断电，转子减速中
        RotorsStopped = 7,  // 已断电，转子静止：允许检查左引擎
    }

    /// <summary>
    /// UNIT 07 维修座第一阶段停靠流程：夹具张开 → 七号落座 → 夹紧 → 开关 OFF → 涡轮停转 → 允许左引擎检查。
    /// - 夹具开合角读取维修座 FBX 的自定义属性 unity_open_deg（导入时写入 DockPartProperties），并与 Blender 的 open_deg 核对符号；
    ///   不使用 Blender 的 open_deg 驱动。
    /// - 开关手柄角读取 unity_on_deg / unity_off_deg；状态灯与转子表现随供电状态一致。
    /// - 转子由 RotorPowerDriver 在落座后接管；断电后转子减速期间以及通电时，禁止操作叶轮 / 检查左引擎。
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

        [Header("节奏")]
        [SerializeField] float hoverHeight = 0.12f;
        [SerializeField] float clampSeconds = 0.6f;
        [SerializeField] float descendSeconds = 1.2f;
        [SerializeField] float leverSeconds = 0.3f;

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
        bool configured;

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
        public bool IsSeated => State >= DockState.SeatedOpen;
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

        void Awake()
        {
            configured = ReadProperties();
            if (!configured) return;
            clampClosedL = clampL.localRotation;
            clampClosedR = clampR.localRotation;
            // 导入后的手柄处在 OFF 位置（unity_off_deg），先换算出 0° 的静止姿态
            leverRest = powerLever.localRotation * Quaternion.AngleAxis(-unityOffDeg, Vector3.right);
            lampBlock = new MaterialPropertyBlock();
        }

        void Start()
        {
            if (!configured) return;
            // 初始：通电悬停在锚点上方，夹具闭合，手柄在 ON
            PowerOn = true;
            rotorDriver.SetPower(true);
            clampFraction = 0f;
            leverFraction = 0f;
            ApplyClamps();
            ApplyLever();
            ApplyLamp();
            robotRoot.position = robotAnchor.position + Vector3.up * hoverHeight;
            if (robotAnimator != null) robotAnimator.Play(hoverStateName, 0, 0f);
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
                case DockState.SpinningDown:
                case DockState.RotorsStopped:
                    return Say("断电维修中，夹具保持锁紧。先恢复供电再松开夹具。", false);
                default:
                    return Say("夹具正在动作，请稍候。", false);
            }
        }

        public bool SetPower(bool on)
        {
            if (on == PowerOn) return Say(on ? "已经通电。" : "已经断电。", false);
            if (!on && State != DockState.Clamped)
                return Say(State < DockState.SeatedOpen ? "七号还没落座，不能断电。" : "先夹紧夹具再断电。", false);
            PowerOn = on;
            rotorDriver.SetPower(on);
            ApplyLamp();
            if (on)
            {
                State = DockState.Clamped;
                return Say("恢复供电，涡轮重新转动。叶轮禁止操作。", true);
            }
            State = DockState.SpinningDown;
            return Say("断电。涡轮减速中，等叶轮停稳。", true);
        }

        public bool InspectLeftEngine()
        {
            if (State == DockState.RotorsStopped)
                return Say("叶轮已停稳。可以检查左引擎（本阶段不做拆卸）。", true);
            if (State == DockState.SpinningDown)
                return Say("叶轮还在减速转动，禁止操作。", false);
            if (PowerOn)
                return Say("七号仍在通电，涡轮在转动。先落座、夹紧并断电。", false);
            return Say("现在不能检查左引擎。", false);
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
                case DockState.SeatedOpen: return "点击夹具握把：夹紧";
                case DockState.Clamping: return "夹具合拢中…";
                case DockState.Clamped: return "点击断电开关：OFF";
                case DockState.SpinningDown: return "等涡轮停转…";
                case DockState.RotorsStopped: return "点击左引擎：开始检查";
                default: return string.Empty;
            }
        }

        // ------------------------------------------------------------------ 每帧推进

        void Update()
        {
            if (!configured) return;
            float dt = Time.deltaTime;
            switch (State)
            {
                case DockState.ClampsOpening:
                    clampFraction = Mathf.MoveTowards(clampFraction, 1f, dt / clampSeconds);
                    if (clampFraction >= 1f)
                    {
                        if (IsRobotSeatedPosition()) State = DockState.SeatedOpen;
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
                        if (robotAnimator != null) robotAnimator.CrossFadeInFixedTime(seatedStateName, 0.15f, 0);
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
            }
            float leverTarget = PowerOn ? 0f : 1f;
            leverFraction = Mathf.MoveTowards(leverFraction, leverTarget, dt / leverSeconds);
            ApplyClamps();
            ApplyLever();
        }

        /// <summary>
        /// 读档恢复：七号已落座、夹具夹紧、断电、叶轮停稳（与玩家依次点完落座 → 夹紧 → 断电 → 等停稳之后的状态相同）。
        /// 只给两晚切片的第二晚开场用；供电、夹具、转子仍然只由维修座管理。
        /// </summary>
        public bool RestoreRotorsStopped()
        {
            if (!configured) return Say("维修座配置无效，见控制台。", false);
            robotRoot.position = robotAnchor.position;
            rotorDriver.TakeOver();
            rotorDriver.StopNow();
            if (robotAnimator != null) robotAnimator.Play(seatedStateName, 0, 0f);
            PowerOn = false;
            clampFraction = 0f;
            leverFraction = 1f;
            ApplyClamps();
            ApplyLever();
            ApplyLamp();
            State = DockState.RotorsStopped;
            return Say("七号停在维修座上：已夹紧、断电，叶轮停稳。", true);
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
