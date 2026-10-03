using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Dock;
using UnityEngine;

namespace BorderRepair.FirstOrder
{
    public enum FoStep
    {
        SeatRobot = 0,          // 点夹具：张开，七号落座
        ClampRobot = 1,         // 点夹具：夹紧
        PowerOff = 2,           // 点断电开关，等涡轮停转
        InspectLeftEngine = 3,  // 点左引擎：开始检查（看到进气口堵塞）
        ReleaseLatches = 4,     // 扳开外侧、后侧两个锁扣（顺序不限）
        RemoveCover = 5,        // 取下左上盖总成
        PlaceCover = 6,         // 翻过来放到工作台操作垫（内侧朝上，露出保养记录）
        LocateBearing = 7,      // 检查并定位磨损的左上轴承
        RemoveBearing = 8,      // 取下旧轴承
        PlaceOldBearing = 9,    // 放进工作台旧件托盘
        FetchNewBearing = 10,   // 从工作台轴承盒取新轴承装上
        ReinstallCover = 11,    // 把上盖总成装回
        CloseLatches = 12,      // 扣回两个锁扣
        PowerOn = 13,           // 通电（进气口必须已清理）
        ReleaseAndLift = 14,    // 松开夹具，七号离座悬停
        Retest = 15,            // 离座复测（占位判定）
        Done = 16,
    }

    /// <summary>
    /// 七号首单的可玩原型流程（独立测试场景用，不是正式维修流程，也没有接入工单系统）。
    /// 维修座动作全部交给现有 Unit07DockController；引擎上的锁扣、上盖总成、上轴承都是 RobotV4 里真实存在的网格，
    /// 磨损轴承、进气口堵塞、新轴承、上盖内侧保养标记来自故障美术包（art/unit07-fault-kit）。
    /// 进气口堵塞不是一个固定步骤：断电停转后、通电之前随时可以清理；但只清理不算修好，磨损轴承必须找到并更换，否则不能通电复测。
    /// 没有对应资产或动画的部分都是占位，见 Placeholders。
    /// </summary>
    public class FirstOrderFlow : MonoBehaviour
    {
        [SerializeField] Unit07DockController dock;
        [SerializeField] FirstOrderCameraRig rig;
        [SerializeField] Transform engineLHinge;
        [SerializeField] Transform engineRHinge;
        [SerializeField] Transform partsCarrier;
        [SerializeField] FirstOrderPart latchOuter, latchRear, cover, bearing, newBearing, rightEngine;
        [SerializeField] FirstOrderDropZone matZone, oldTrayZone;
        [Header("七号故障美术包（art/unit07-fault-kit）")]
        [SerializeField] FirstOrderPart clog;
        [Tooltip("清理顺序：纤维 → 护栅积尘 → 唇口积尘 → 积尘垫")]
        [SerializeField] Renderer[] clogLayers = new Renderer[0];
        [Tooltip("RobotV4 原轴承的渲染器：关掉，由挂在它下面的磨损轴承美术件代替显示（碰撞仍保留）")]
        [SerializeField] Renderer originalBearingRenderer;
        [Tooltip("上盖内侧保养标记的渲染器，和它网格法线平均值（标记本地坐标，构建时从网格算出；运行时网格不可读）")]
        [SerializeField] Renderer coverLabel;
        [SerializeField] Vector3 coverLabelNormalLocal = Vector3.up;
        [SerializeField] float hoverHeight = 0.12f;
        [SerializeField] float moveSeconds = 0.45f;
        [SerializeField] float latchOffset = 0.006f;
        [SerializeField] float coverLift = 0.10f;
        [SerializeField] float bearingLift = 0.07f;
        [SerializeField] float travelHeight = 1.32f;
        [SerializeField] float retestSeconds = 3f;
        [SerializeField] float turnSeconds = 1.2f;         // 搬运途中转向（上盖翻面、轴承放平 / 转回装配朝向）
        [SerializeField] float cleanLayerSeconds = 0.2f;
        [SerializeField] float carryClearance = 0.03f;     // 搬运时零件包围球最低点高过沿途障碍物的余量
        [Tooltip("新轴承从轴承盒取走时的进场点（盒子上方被挡住时用；构建时规划）")]
        [SerializeField] bool newBearingUseApproach;
        [SerializeField] Vector3 newBearingApproach;

        public static readonly string[] Placeholders =
        {
            "锁扣扳开 / 扣回：模型没有锁扣动画，用沿外法线移出 6 mm 表示已扳开（占位表现）",
            "上盖总成：上盖 + 进气唇口 + 护栅 + 风道是同级网格，由程序编成一组一起移动（分组待美术确认）",
            "取下 / 搬运 / 装回 / 上盖翻面：程序插值移动——竖直抬高越过七号机身，平移途中绕零件中心转向（翻面 / 放平），上盖落点在工作台台灯下方，所以最后低空水平钻进去；没有手部动作或拆卸动画（占位表现）",
            "清理进气口：没有清理工具和手部动作，按 纤维 → 护栅积尘 → 唇口积尘 → 积尘垫 逐层隐藏（占位表现）",
            "故障轴承定位：磨损轴承美术件（磨痕、锈斑、碎屑）+ 文字指出；没有手转、晃动或异响（诊断仍是占位）",
            "轴承盒：工作台 Box_Bearings 是实心块，没有盒内空间；新轴承平放在盒体顶面、开着的盒盖前面（美术没有内腔）",
            "保养记录：上盖内侧的贴纸只能看（镜头 7），没有可交互的记录内容；没有接入正式工单系统",
            "离座复测：模型没有失衡模拟，只核对维修项、左右引擎等高、转子转速一致（占位判定）",
        };

        public FoStep Step { get; private set; } = FoStep.SeatRobot;
        public string Message { get; private set; } = "";
        public bool Busy { get; private set; }
        public bool BearingLocated { get; private set; }
        public bool RetestPassed { get; private set; }
        public string RetestDetail { get; private set; } = "";
        public int RejectedCount { get; private set; }
        public Unit07DockController Dock => dock;
        public FirstOrderCameraRig Rig => rig;
        public FirstOrderPart LatchOuter => latchOuter;
        public FirstOrderPart LatchRear => latchRear;
        public FirstOrderPart Cover => cover;
        public FirstOrderPart Bearing => bearing;
        public FirstOrderPart NewBearing => newBearing;
        public FirstOrderPart RightEngine => rightEngine;
        public FirstOrderPart Clog => clog;
        public IReadOnlyList<Renderer> ClogLayers => clogLayers;
        public Renderer OriginalBearingRenderer => originalBearingRenderer;
        public Renderer CoverLabel => coverLabel;
        public Vector3 CoverLabelNormal => coverLabel != null ? coverLabel.transform.TransformDirection(coverLabelNormalLocal).normalized : Vector3.zero;
        public FirstOrderDropZone MatZone => matZone;
        public FirstOrderDropZone OldTrayZone => oldTrayZone;
        public Transform EngineLHinge => engineLHinge;
        public Transform EngineRHinge => engineRHinge;
        public float TravelHeight => travelHeight;
        public IEnumerable<FirstOrderPart> TrackedParts => new[] { latchOuter, latchRear, cover, clog, bearing, newBearing }.Where(p => p != null);

        public bool ClogCleared => clog == null || clog.Location == PartLocation.Cleared;
        public bool BearingReplaced => newBearing.Location == PartLocation.Installed;
        /// <summary>只清理了进气口、磨损轴承还没换：不能算修好。</summary>
        public bool CleanedOnly => clog != null && ClogCleared && !BearingReplaced;

        /// <summary>每次点击的结果（报告用）。</summary>
        public event Action<string, bool, string> Acted;   // (对象, 是否成功, 消息)

        public void Configure(Unit07DockController d, FirstOrderCameraRig r, Transform hingeL, Transform hingeR, Transform carrier,
                              FirstOrderPart lo, FirstOrderPart lr, FirstOrderPart cv, FirstOrderPart br, FirstOrderPart nb, FirstOrderPart re,
                              FirstOrderDropZone mat, FirstOrderDropZone tray)
        {
            dock = d; rig = r; engineLHinge = hingeL; engineRHinge = hingeR; partsCarrier = carrier;
            latchOuter = lo; latchRear = lr; cover = cv; bearing = br; newBearing = nb; rightEngine = re; matZone = mat; oldTrayZone = tray;
        }

        public (bool use, Vector3 point) NewBearingBenchApproach => (newBearingUseApproach, newBearingApproach);
        public void ConfigureNewBearingApproach(bool use, Vector3 approach) { newBearingUseApproach = use; newBearingApproach = approach; }

        /// <summary>接入故障美术包：进气口堵塞（可清理）、按层清理的渲染器、要关掉的原轴承渲染器、上盖内侧保养标记。</summary>
        public void ConfigureFaultKit(FirstOrderPart clogPart, Renderer[] layers, Renderer originalBearing, Renderer label, Vector3 labelNormalLocal)
        {
            clog = clogPart; clogLayers = layers; originalBearingRenderer = originalBearing; coverLabel = label; coverLabelNormalLocal = labelNormalLocal;
        }

        void Start()
        {
            newBearing.Location = PartLocation.Stored;
            newBearing.LocationDetail = "工作台轴承盒 Box_Bearings（平放在盒体顶面）";
            if (clog != null) clog.LocationDetail = "左进气口护栅上（积尘、纤维堵塞）";
            if (originalBearingRenderer != null) originalBearingRenderer.enabled = false;
            matZone.ShowMarker(false);
            oldTrayZone.ShowMarker(false);
            Say("七号在维修座上方悬停。点夹具握把，让它落座。");
            if (rig != null) rig.Go(FirstOrderCameraRig.Dock);
        }

        void Update()
        {
            // 维修座的异步状态推进（落座、夹紧、停转）
            switch (Step)
            {
                case FoStep.SeatRobot when dock.State == DockState.SeatedOpen:
                    Advance(FoStep.ClampRobot, "七号已落座。再点夹具：夹紧。");
                    break;
                case FoStep.ClampRobot when dock.State == DockState.Clamped:
                    Advance(FoStep.PowerOff, "已夹紧。点断电开关：OFF。");
                    break;
                case FoStep.PowerOff when dock.State == DockState.RotorsStopped:
                    Advance(FoStep.InspectLeftEngine, "涡轮已停转。点左引擎开始检查。");
                    break;
            }
        }

        void Advance(FoStep next, string msg)
        {
            Step = next;
            Say(msg);
            if (rig == null) return;
            switch (next)
            {
                case FoStep.ReleaseLatches: case FoStep.LocateBearing: case FoStep.CloseLatches: rig.Go(FirstOrderCameraRig.EngineL); break;
                case FoStep.PlaceCover: case FoStep.PlaceOldBearing: case FoStep.FetchNewBearing: case FoStep.ReinstallCover: rig.Go(FirstOrderCameraRig.Overview); break;
                case FoStep.PowerOn: case FoStep.ReleaseAndLift: case FoStep.Retest: rig.Go(FirstOrderCameraRig.Dock); break;
            }
        }

        void Say(string s) => Message = s;

        bool Refuse(string target, string msg)
        {
            RejectedCount++;
            Say(msg);
            Acted?.Invoke(target, false, msg);
            return false;
        }

        bool Ok(string target, string msg)
        {
            Say(msg);
            Acted?.Invoke(target, true, msg);
            return true;
        }

        bool EngineSafe => dock.State == DockState.RotorsStopped;

        /// <summary>还没做完的维修项（通电 / 复测前要全部完成）。</summary>
        public string PendingRepairs()
        {
            var list = new List<string>();
            if (!ClogCleared) list.Add("进气口还堵着（断电停转时点进气口清理）");
            if (!BearingReplaced)
                list.Add(bearing.Location == PartLocation.Installed ? (BearingLocated ? "左上轴承磨损，还没换" : "上盖下面的轴承还没检查（左上轴承有磨损要处理）") : "新轴承还没装上");
            if (cover.Location != PartLocation.Installed) list.Add("上盖没装好");
            if (latchOuter.Location != PartLocation.Installed || latchRear.Location != PartLocation.Installed) list.Add("锁扣还没扣回");
            return string.Join("；", list);
        }

        string PowerRefusal()
        {
            var todo = PendingRepairs();
            if (todo.Length == 0) return "维修还没做完，先别通电。";
            return (CleanedOnly ? "只清理了进气口，不能算修好，还不能通电复测：" : "还不能通电：") + todo + "。";
        }

        // ------------------------------------------------------------------ 点击入口

        public bool Click(Component hit)
        {
            if (hit == null) return Refuse("（无）", "这里没有可操作的东西。");
            if (Busy) return Refuse(hit.name, "上一个动作还没做完。");
            switch (hit)
            {
                case DockInteractable di: return ClickDock(di);
                case FirstOrderPart p: return ClickPart(p);
                case FirstOrderDropZone z: return ClickZone(z);
                default: return Refuse(hit.name, $"{hit.name}：本单用不到。");
            }
        }

        bool ClickDock(DockInteractable di)
        {
            string n = di.name;
            switch (di.action)
            {
                case DockAction.Clamps:
                    if (Step == FoStep.ReleaseAndLift)
                    {
                        if (!dock.Interact(DockAction.Clamps)) return Refuse(n, dock.LastMessage);
                        StartCoroutine(LiftOff());
                        return Ok(n, "夹具张开，七号离座上浮。");
                    }
                    if (Step != FoStep.SeatRobot && Step != FoStep.ClampRobot)
                    {
                        // 松开夹具 = 让七号离座复测
                        if (Step < FoStep.PowerOn && CleanedOnly) return Refuse(n, "只清理了进气口，不能算修好，还不能离座复测：" + PendingRepairs() + "。夹具保持锁紧。");
                        return Refuse(n, Step < FoStep.PowerOn ? "维修中，夹具保持锁紧。" : Step == FoStep.PowerOn ? "先通电，再松开夹具。" : "七号已经离座了。");
                    }
                    return dock.Interact(DockAction.Clamps) ? Ok(n, dock.LastMessage) : Refuse(n, dock.LastMessage);
                case DockAction.PowerSwitch:
                    if (Step == FoStep.PowerOff)
                        return dock.Interact(DockAction.PowerSwitch) ? Ok(n, dock.LastMessage) : Refuse(n, dock.LastMessage);
                    if (Step == FoStep.PowerOn)
                    {
                        if (!ClogCleared) return Refuse(n, PowerRefusal());
                        if (!dock.Interact(DockAction.PowerSwitch)) return Refuse(n, dock.LastMessage);
                        Advance(FoStep.ReleaseAndLift, "恢复供电，涡轮重新转动。点夹具握把：松开，让七号离座。");
                        return Ok(n, Message);
                    }
                    if (Step > FoStep.PowerOff && Step < FoStep.PowerOn) return Refuse(n, PowerRefusal());
                    return dock.Interact(DockAction.PowerSwitch) ? Ok(n, dock.LastMessage) : Refuse(n, dock.LastMessage);
                case DockAction.EngineLeft:
                    if (Step < FoStep.InspectLeftEngine) return Refuse(n, dock.InspectLeftEngine() ? "先完成停靠和断电。" : dock.LastMessage);
                    if (Step == FoStep.InspectLeftEngine) return InspectLeft(n, di.GetComponent<Collider>());
                    return Refuse(n, "已经在检查左引擎了。");
                default:
                    return Refuse(n, $"{n}：本单用不到。");
            }
        }

        /// <summary>开始检查左引擎：关掉维修座集成留下的整块左引擎检查代理（否则它会盖住锁扣、上盖、进气口和轴承的点选）。</summary>
        bool InspectLeft(string n, Collider proxy)
        {
            if (!dock.InspectLeftEngine()) return Refuse(n, dock.LastMessage);
            if (proxy == null && engineLHinge != null)
                proxy = engineLHinge.GetComponentsInChildren<DockInteractable>(true).FirstOrDefault(d => d.action == DockAction.EngineLeft)?.GetComponent<Collider>();
            if (proxy != null) proxy.enabled = false;
            Advance(FoStep.ReleaseLatches, "左引擎：进气口护栅上糊着积尘和纤维（堵塞），现在已断电停转，可以点进气口清理。" +
                                           "上盖由外侧和后侧两个锁扣固定，拆上盖要先扳开两个锁扣（后侧那个在背面：切到镜头「左引擎背面」）。");
            return Ok(n, Message);
        }

        bool ClickPart(FirstOrderPart p)
        {
            string n = p.realPath;
            if (p == rightEngine) return Refuse(n, "误拆右侧：右引擎是这单的正常对照（进气口干净、转子与左侧同速、手转顺滑），故障在左引擎。右引擎不拆，继续处理左侧。");
            if (p != newBearing && !EngineSafe && Step < FoStep.PowerOn)
                return Refuse(n, dock.State == DockState.SpinningDown
                    ? $"已断电，但叶轮还在减速转动（{dock.Rotors.SpeedDegPerSec:F0}°/s）。等它停稳再检查、拆卸。"
                    : "七号还在供电或叶轮还在转。先停靠、夹紧、断电，等涡轮停稳。");
            if (Step == FoStep.InspectLeftEngine && p != newBearing) return InspectLeft(n, null);   // 点左引擎上的任何部件都算“开始检查”
            if (Step < FoStep.InspectLeftEngine) return Refuse(n, "先完成停靠和断电。");

            if (p == clog)
            {
                if (clog.Location == PartLocation.Cleared) return Refuse(n, "进气口已经清理干净了。");
                if (!EngineSafe || Step > FoStep.PowerOn) return Refuse(n, "通电时不能清理进气口。");
                if (cover.Location == PartLocation.Held) return Refuse(n, "上盖总成还在手上：先放到操作垫上。");
                StartCoroutine(CleanClog());
                return Ok(n, "清理进气口：拨掉缠住护栅的纤维，刷掉护栅和唇口上的积尘。");
            }

            if (p == latchOuter || p == latchRear)
            {
                if (Step == FoStep.ReleaseLatches && p.Location == PartLocation.Installed)
                {
                    StartCoroutine(MoveLatch(p, true));
                    p.Location = PartLocation.Released; p.LocationDetail = "已扳开（仍在引擎上）";
                    if (latchOuter.Location == PartLocation.Released && latchRear.Location == PartLocation.Released)
                    {
                        Advance(FoStep.RemoveCover, "两个锁扣都扳开了。点上盖，沿引擎轴线取下上盖总成。");
                        if (rig != null) rig.Go(FirstOrderCameraRig.EngineL);
                    }
                    else Say($"{p.displayName}已扳开。还有一个锁扣。");
                    return Ok(n, Message);
                }
                if (Step == FoStep.CloseLatches && p.Location == PartLocation.Released)
                {
                    StartCoroutine(MoveLatch(p, false));
                    p.Location = PartLocation.Installed; p.LocationDetail = "七号左引擎原位（已扣回）";
                    if (latchOuter.Location == PartLocation.Installed && latchRear.Location == PartLocation.Installed)
                        Advance(FoStep.PowerOn, ClogCleared ? "两个锁扣都扣回了。点断电开关：通电。" : "两个锁扣都扣回了。进气口还堵着：先点进气口清理，再通电。");
                    else Say($"{p.displayName}已扣回。还有一个锁扣（后侧在镜头「左引擎背面」）。");
                    return Ok(n, Message);
                }
                return Refuse(n, p.Location == PartLocation.Released ? "这个锁扣已经扳开了。" : "现在不用动锁扣。");
            }

            if (p == cover)
            {
                if (Step == FoStep.ReleaseLatches) return Refuse(n, "还有锁扣没扳开。后侧那个在镜头「左引擎背面」。");
                if (Step == FoStep.RemoveCover)
                {
                    StartCoroutine(RemoveFromEngine(cover, coverLift, FoStep.PlaceCover, "上盖总成已取下。点工作台操作垫上的落点，翻过来放下（内侧朝上）。", matZone));
                    return Ok(n, "取下上盖总成（上盖 + 进气唇口 + 护栅 + 风道）。");
                }
                if (Step == FoStep.PlaceCover) return Refuse(n, "上盖在手上：点工作台操作垫上的落点。");
                if (Step == FoStep.ReinstallCover && cover.Location == PartLocation.OnBench)
                {
                    StartCoroutine(InstallOnEngine(cover, coverLift, FoStep.CloseLatches,
                        "上盖总成装回。扣回两个锁扣（后侧锁扣在镜头「左引擎背面」）。" + (ClogCleared ? "" : "进气口还堵着，记得清理。")));
                    return Ok(n, "把上盖总成从操作垫上翻回来，装回左引擎。");
                }
                if (cover.Location == PartLocation.OnBench) return Refuse(n, !BearingReplaced ? "轴承位还空着，先装新轴承。" : "现在不用动上盖。");
                return Refuse(n, "现在不用动上盖。");
            }

            if (p == bearing)
            {
                if (cover.Location == PartLocation.Installed || Step < FoStep.LocateBearing && cover.Location == PartLocation.Held)
                    return Refuse(n, "轴承被上盖总成挡住：先取下上盖。");
                if (Step == FoStep.LocateBearing)
                {
                    BearingLocated = true;
                    Advance(FoStep.RemoveBearing, "找到了：左上轴承 Engine_BearingTop_L 磨损——外圈有磨痕、锈斑，旁边有金属碎屑。光清理进气口修不好它。再点它，取下旧轴承。");
                    return Ok(n, Message);
                }
                if (Step == FoStep.RemoveBearing)
                {
                    StartCoroutine(RemoveFromEngine(bearing, bearingLift, FoStep.PlaceOldBearing, $"旧轴承已取下。点工作台{oldTrayZone.displayName}里的黄色落点。", oldTrayZone));
                    return Ok(n, "沿转轴取下旧轴承。");
                }
                return Refuse(n, bearing.Location == PartLocation.OnBench ? $"旧轴承已经放进{oldTrayZone.displayName}。" : "现在不用动轴承。");
            }

            if (p == newBearing)
            {
                if (Step < FoStep.FetchNewBearing) return Refuse(n, Step <= FoStep.PlaceOldBearing ? "旧轴承还在轴承位上，先取下旧轴承。" : "现在不用拿新轴承。");
                if (Step == FoStep.FetchNewBearing)
                {
                    StartCoroutine(InstallNewBearing());
                    return Ok(n, "从轴承盒取出新轴承，装到左上轴承位。");
                }
                return Refuse(n, "新轴承已经装上了。");
            }
            return Refuse(n, $"{p.displayName}：本单不用拆。");
        }

        bool ClickZone(FirstOrderDropZone z)
        {
            string n = z.benchObjectPath;
            var held = Step == FoStep.PlaceCover ? cover : Step == FoStep.PlaceOldBearing ? bearing : null;
            if (held == null) return Refuse(n, "手上没有要放下的零件。");
            if (z.acceptsPartId != held.partId) return Refuse(n, $"{z.displayName}放的不是{held.displayName}。");
            StartCoroutine(PlaceOnBench(held, z));
            return Ok(n, $"{held.displayName}放到{z.displayName}。");
        }

        // ------------------------------------------------------------------ 动作（占位表现：程序插值移动）

        IEnumerator MoveTo(Transform t, Vector3 target)
        {
            Vector3 a = t.position;
            float time = 0f;
            while (time < moveSeconds)
            {
                time += Time.deltaTime;
                t.position = Vector3.Lerp(a, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / moveSeconds)));
                yield return null;
            }
            t.position = target;
        }

        /// <summary>
        /// 平移的同时绕零件中心转到目标朝向（不在某一帧突然转向）。转向放在平移的中段：离开起点上方以后才开始转，到终点上方之前转完。
        /// pivot：转动中心相对原点的偏移（去掉当前朝向的量）。
        /// </summary>
        IEnumerator MoveTurn(Transform t, Vector3 target, Quaternion targetRot, Vector3 pivot, float seconds)
        {
            Quaternion r0 = t.rotation;
            Vector3 c0 = t.position + r0 * pivot, c1 = target + targetRot * pivot;
            float time = 0f;
            while (time < seconds)
            {
                time += Time.deltaTime;
                float k = Mathf.Clamp01(time / seconds);
                var r = Quaternion.Slerp(r0, targetRot, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.8f, k)));
                t.SetPositionAndRotation(Vector3.Lerp(c0, c1, Mathf.SmoothStep(0f, 1f, k)) - r * pivot, r);
                yield return null;
            }
            t.SetPositionAndRotation(target, targetRot);
        }

        static Vector3 PivotOf(FirstOrderPart p) => Quaternion.Inverse(p.transform.rotation) * (p.WorldBounds().center - p.transform.position);

        IEnumerator MoveLatch(FirstOrderPart latch, bool open)
        {
            Busy = true;
            var home = latch.HomeWorldPose();
            var c = cover.Location == PartLocation.Installed ? cover.WorldBounds().center : engineLHinge.position;
            var dir = Vector3.ProjectOnPlane(home.position - c, engineLHinge.up).normalized;
            yield return MoveTo(latch.transform, open ? home.position + dir * latchOffset : home.position);
            if (!open) latch.Reattach();
            Busy = false;
        }

        /// <summary>清理进气口：按层隐藏（没有清理工具动画；不移动任何东西，所以不会穿模）。</summary>
        IEnumerator CleanClog()
        {
            Busy = true;
            foreach (var r in clogLayers)
            {
                for (float t = 0f; t < cleanLayerSeconds; t += Time.deltaTime) yield return null;
                if (r != null) r.enabled = false;
            }
            foreach (var c in clog.GetComponents<Collider>()) c.enabled = false;
            clog.Location = PartLocation.Cleared; clog.LocationDetail = "已清理（纤维、积尘已清掉）";
            string next = BearingReplaced ? (Step == FoStep.PowerOn ? "点断电开关：通电。" : "")
                        : BearingLocated ? "但只清理不算修好：左上轴承磨损还没换。"
                        : "但只清理不算修好：拆下上盖，检查下面的左上轴承。";
            Say("进气口清理干净了。" + next);
            Acted?.Invoke(clog.realPath, true, Message);
            Busy = false;
        }

        IEnumerator RemoveFromEngine(FirstOrderPart p, float lift, FoStep next, string msg, FirstOrderDropZone zone)
        {
            Busy = true;
            p.Detach(partsCarrier);
            var up = engineLHinge.up;
            yield return MoveTo(p.transform, p.transform.position + up * lift);
            p.Location = PartLocation.Held; p.LocationDetail = "已取下，悬在引擎上方，等待放到工作台";
            zone.ShowMarker(true);
            Advance(next, msg);
            Busy = false;
        }

        /// <summary>
        /// 搬运高度（零件中心）：零件包围球的最低点要高过沿途障碍物（七号机身、维修座、工作台上的物件）的最高点 carryClearance——
        /// 转向时零件任意朝向都在包围球里，所以翻面、放平都不会扫到下面的东西。
        /// 沿途 = 起点到终点的水平带（两侧各加一个包围球半径）；高过 2.2 m 的房间顶部管线不算。
        /// </summary>
        float CarryCenterHeight(FirstOrderPart p, Vector3 from, Vector3 to, out string obstacle)
        {
            var b = p.WorldBounds();
            float radius = b.extents.magnitude;
            var own = new HashSet<Renderer>(p.Renderers());
            var lane = new Bounds(new Vector3(from.x, 1f, from.z), Vector3.zero);
            lane.Encapsulate(new Vector3(to.x, 1f, to.z));
            lane.Expand(new Vector3(2f * radius, 10f, 2f * radius));
            float top = float.MinValue; obstacle = "—";
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || own.Contains(r) || r.bounds.max.y > 2.2f) continue;
                if (matZone.marker == r || oldTrayZone.marker == r || !r.bounds.Intersects(lane)) continue;
                if (r.bounds.max.y > top) { top = r.bounds.max.y; obstacle = r.name; }
            }
            return Mathf.Max(travelHeight, top + carryClearance + radius);
        }

        /// <summary>正在搬运的零件（竖直抬起 → 平移并转向 → 竖直落下这几段；装回引擎最后贴近装配位的那一段不算）。测试逐帧核对它不穿模。</summary>
        public FirstOrderPart Carrying { get; private set; }
        public string LastCarry { get; private set; } = "";

        /// <summary>
        /// 搬运：（从工作台取走且落点上方被挡住时，先低空水平钻到进场点）→ 竖直抬到搬运高度 → 平移到终点（或终点的进场点）上方，途中绕零件中心转到终点朝向
        /// →（有进场点时：竖直落到进场点，再低空水平钻到落点上方）→ 竖直落到终点。
        /// checkDescent = false 时最后竖直落下那段不算“搬运”（装回引擎时落到装配位上方，之后由调用方沿轴线装入）。
        /// </summary>
        IEnumerator Carry(FirstOrderPart p, Vector3 end, Quaternion endRot, bool checkDescent, Vector3? fromApproach = null, Vector3? toApproach = null)
        {
            var t = p.transform;
            Carrying = p;
            if (fromApproach is Vector3 fa)
            {
                yield return MoveTo(t, new Vector3(t.position.x, fa.y, t.position.z));
                yield return MoveTo(t, fa);
            }
            var pivot = PivotOf(p);
            var via = toApproach ?? end;
            float cy = CarryCenterHeight(p, t.position, via, out var obstacle);
            LastCarry = $"{p.partId}：搬运时中心高 {cy:F3} m（沿途最高 {obstacle}）" + (fromApproach != null ? "，从工作台低空钻出" : "") + (toApproach != null ? "，低空钻进落点（上方有台灯）" : "");
            var c0 = t.position + t.rotation * pivot;
            yield return MoveTo(t, t.position + Vector3.up * Mathf.Max(0f, cy - c0.y));
            yield return MoveTurn(t, new Vector3(via.x, Mathf.Max(via.y, cy - (endRot * pivot).y), via.z), endRot, pivot, turnSeconds);
            if (toApproach is Vector3 ta)
            {
                yield return MoveTo(t, ta);
                yield return MoveTo(t, new Vector3(end.x, ta.y, end.z));
            }
            if (!checkDescent) Carrying = null;
            yield return MoveTo(t, end);
            Carrying = null;
        }

        IEnumerator PlaceOnBench(FirstOrderPart p, FirstOrderDropZone z)
        {
            Busy = true;
            Quaternion rot = p.transform.rotation;
            Vector3 land;
            if (z.orientPart)
            {
                rot = z.landingRotation;                                         // 上盖翻面内侧朝上 / 旧轴承平放：构建时按顶点算好
                land = z.landing.position + z.landingOffset;
            }
            else
            {
                var b = p.WorldBounds();
                float above = p.transform.position.y - b.min.y;                  // 原点到包围盒底面的高度
                var offsetXZ = new Vector3(p.transform.position.x - b.center.x, 0f, p.transform.position.z - b.center.z);
                land = z.landing.position + offsetXZ + Vector3.up * (above + 0.001f);
            }
            yield return Carry(p, land, rot, true, null, z.useApproach ? z.approachPoint : (Vector3?)null);
            z.ShowMarker(false);
            p.Location = PartLocation.OnBench; p.LocationDetail = $"{z.displayName}（{z.benchObjectPath}）";
            if (p == cover) Advance(FoStep.LocateBearing, "上盖总成翻过来放在操作垫上，内侧朝上，能看到保养记录（镜头「保养记录」近看）。现在能看到左上轴承：点它检查。");
            else Advance(FoStep.FetchNewBearing, $"旧轴承平放进{oldTrayZone.displayName}，可以和轴承盒上的新轴承对比（镜头「新旧轴承」）。点新轴承，装到左上轴承位。");
            Busy = false;
        }

        IEnumerator InstallOnEngine(FirstOrderPart p, float lift, FoStep next, string msg)
        {
            Busy = true;
            var home = p.HomeWorldPose();
            yield return Carry(p, home.position + engineLHinge.up * lift, home.rotation, false, p == cover && matZone.useApproach ? matZone.approachPoint : (Vector3?)null);
            yield return MoveTo(p.transform, home.position);                    // 沿引擎轴线装入（拆下路径的反向，测试另外核对）
            p.Reattach();
            p.Location = PartLocation.Installed; p.LocationDetail = "七号左引擎原位";
            Advance(next, msg);
            Busy = false;
        }

        IEnumerator InstallNewBearing()
        {
            Busy = true;
            var home = bearing.HomeWorldPose();            // 新轴承装到旧轴承的原位（新轴承主对象的装配位姿 = 旧轴承原位）
            var t = newBearing.transform;
            yield return Carry(newBearing, home.position + engineLHinge.up * bearingLift, home.rotation, false, newBearingUseApproach ? newBearingApproach : (Vector3?)null);
            yield return MoveTo(t, home.position);
            t.SetParent(bearing.HomeParent, true);
            newBearing.Location = PartLocation.Installed; newBearing.LocationDetail = "七号左上轴承位（新轴承）";
            Advance(FoStep.ReinstallCover, "新轴承装上了。点操作垫上的上盖总成，装回去。" + (ClogCleared ? "" : "进气口还堵着，装回后记得清理。"));
            Busy = false;
        }

        IEnumerator LiftOff()
        {
            Busy = true;
            while (dock.State != DockState.SeatedOpen) yield return null;     // 等夹具张开
            var root = dock.RobotRoot;
            var a = root.position;
            var b = dock.RobotAnchor.position + Vector3.up * hoverHeight;
            dock.Rotors.Release();                                            // 交还给 Idle_Hover 片段
            if (dock.RobotAnimator != null) dock.RobotAnimator.CrossFadeInFixedTime("Idle_Hover", 0.2f, 0);
            float time = 0f;
            while (time < 1.2f)
            {
                time += Time.deltaTime;
                root.position = Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / 1.2f)));
                yield return null;
            }
            root.position = b;
            Advance(FoStep.Retest, "七号离座悬停。复测中……");
            Busy = false;
            StartCoroutine(Retest());
        }

        IEnumerator Retest()
        {
            Busy = true;
            float t = 0f, maxDy = 0f;
            int samples = 0;
            while (t < retestSeconds)
            {
                t += Time.deltaTime;
                maxDy = Mathf.Max(maxDy, Mathf.Abs(engineLHinge.position.y - engineRHinge.position.y));
                samples++;
                yield return null;
            }
            bool bearingOk = BearingReplaced && bearing.Location == PartLocation.OnBench;
            bool coverOk = cover.Location == PartLocation.Installed && latchOuter.Location == PartLocation.Installed && latchRear.Location == PartLocation.Installed;
            RetestPassed = bearingOk && ClogCleared && coverOk && maxDy < 0.005f && dock.Rotors.Powered;
            RetestDetail = $"悬停 {retestSeconds:F0} s、{samples} 帧：左右引擎高度差最大 {maxDy * 1000:F1} mm；轴承已更换 {bearingOk}；进气口已清理 {ClogCleared}；上盖和锁扣复位 {coverOk}。" +
                           "（占位判定：模型没有失衡模拟）";
            Step = FoStep.Done;
            Say(RetestPassed ? "复测通过（占位判定）：维修项都做完，左右等高，转子同速。首单原型流程结束。" : "复测未通过：" + RetestDetail);
            Acted?.Invoke("Retest", RetestPassed, Message);
            Busy = false;
        }

        public string NextHint()
        {
            string clean = ClogCleared ? "" : "；进气口堵塞断电时随时可以点击清理";
            switch (Step)
            {
                case FoStep.SeatRobot: return "点维修座夹具的黄色握把：张开，让七号落座";
                case FoStep.ClampRobot: return dock.State == DockState.SeatedOpen ? "点夹具握把：夹紧" : "等七号落座…";
                case FoStep.PowerOff: return dock.State == DockState.SpinningDown ? "等涡轮停转…" : "点断电开关：OFF";
                case FoStep.InspectLeftEngine: return "点左引擎（上盖或进气口）：开始检查";
                case FoStep.ReleaseLatches: return "扳开外侧锁扣、后侧锁扣（后侧在镜头「左引擎背面」）" + clean;
                case FoStep.RemoveCover: return "点左上盖：取下上盖总成" + clean;
                case FoStep.PlaceCover: return "点工作台操作垫上的黄色落点（上盖翻过来放）";
                case FoStep.LocateBearing: return "点左上轴承：检查（镜头「保养记录」看上盖内侧）";
                case FoStep.RemoveBearing: return "再点左上轴承：取下";
                case FoStep.PlaceOldBearing: return $"点工作台{oldTrayZone.displayName}里的黄色落点";
                case FoStep.FetchNewBearing: return "点工作台轴承盒上的新轴承（镜头「新旧轴承」对比）";
                case FoStep.ReinstallCover: return "点操作垫上的上盖总成：装回";
                case FoStep.CloseLatches: return "扣回外侧锁扣、后侧锁扣（后侧在镜头「左引擎背面」）" + clean;
                case FoStep.PowerOn: return ClogCleared ? "点断电开关：通电" : "先点进气口清理堵塞，再通电";
                case FoStep.ReleaseAndLift: return "点夹具握把：松开，让七号离座";
                case FoStep.Retest: return "复测中…";
                default: return "完成";
            }
        }
    }
}
