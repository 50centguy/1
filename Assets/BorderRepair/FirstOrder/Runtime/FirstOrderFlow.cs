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
        InspectLeftEngine = 3,  // 点左引擎：开始检查
        ReleaseLatches = 4,     // 扳开外侧、后侧两个锁扣（顺序不限）
        RemoveCover = 5,        // 取下左上盖总成
        PlaceCover = 6,         // 放到工作台操作垫
        LocateBearing = 7,      // 检查并定位故障的左上轴承
        RemoveBearing = 8,      // 取下旧轴承
        PlaceOldBearing = 9,    // 放进工作台旧件托盘
        FetchNewBearing = 10,   // 从工作台轴承盒取新轴承装上（新轴承是占位）
        ReinstallCover = 11,    // 把上盖总成装回
        CloseLatches = 12,      // 扣回两个锁扣
        PowerOn = 13,           // 通电
        ReleaseAndLift = 14,    // 松开夹具，七号离座悬停
        Retest = 15,            // 离座复测（占位判定）
        Done = 16,
    }

    /// <summary>
    /// 七号首单的可玩原型流程（独立测试场景用，不是正式维修流程，也没有接入工单系统）。
    /// 维修座动作全部交给现有 Unit07DockController；引擎上的锁扣、上盖总成、上轴承都是 RobotV4 里真实存在的网格。
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
        [SerializeField] float hoverHeight = 0.12f;
        [SerializeField] float moveSeconds = 0.45f;
        [SerializeField] float latchOffset = 0.006f;
        [SerializeField] float coverLift = 0.10f;
        [SerializeField] float bearingLift = 0.07f;
        [SerializeField] float travelHeight = 1.32f;
        [SerializeField] float retestSeconds = 3f;

        public static readonly string[] Placeholders =
        {
            "锁扣扳开 / 扣回：模型没有锁扣动画，用沿外法线移出 6 mm 表示已扳开（占位表现）",
            "上盖总成：上盖 + 进气唇口 + 护栅 + 风道是同级网格，由程序编成一组一起移动（分组待美术确认）",
            "取下 / 搬运 / 装回：程序直线插值移动，没有手部动作或拆卸动画（占位表现）",
            "故障轴承定位：只用红色着色和文字标出，没有磨损外观、手转、晃动或声音（占位诊断）",
            "新轴承：没有新轴承美术资产，用同尺寸圆柱体代替，名字带【占位】",
            "离座复测：模型没有失衡模拟，只核对左右引擎等高、转子转速一致（占位判定）",
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
        public FirstOrderDropZone MatZone => matZone;
        public FirstOrderDropZone OldTrayZone => oldTrayZone;
        public Transform EngineLHinge => engineLHinge;
        public Transform EngineRHinge => engineRHinge;
        public IEnumerable<FirstOrderPart> TrackedParts => new[] { latchOuter, latchRear, cover, bearing, newBearing };

        /// <summary>每次点击的结果（报告用）。</summary>
        public event Action<string, bool, string> Acted;   // (对象, 是否成功, 消息)

        public void Configure(Unit07DockController d, FirstOrderCameraRig r, Transform hingeL, Transform hingeR, Transform carrier,
                              FirstOrderPart lo, FirstOrderPart lr, FirstOrderPart cv, FirstOrderPart br, FirstOrderPart nb, FirstOrderPart re,
                              FirstOrderDropZone mat, FirstOrderDropZone tray)
        {
            dock = d; rig = r; engineLHinge = hingeL; engineRHinge = hingeR; partsCarrier = carrier;
            latchOuter = lo; latchRear = lr; cover = cv; bearing = br; newBearing = nb; rightEngine = re; matZone = mat; oldTrayZone = tray;
        }

        void Start()
        {
            newBearing.Location = PartLocation.Stored;
            newBearing.LocationDetail = "工作台轴承盒 Box_Bearings";
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
                        return Refuse(n, Step < FoStep.PowerOn ? "维修中，夹具保持锁紧。" : Step == FoStep.PowerOn ? "先通电，再松开夹具。" : "七号已经离座了。");
                    return dock.Interact(DockAction.Clamps) ? Ok(n, dock.LastMessage) : Refuse(n, dock.LastMessage);
                case DockAction.PowerSwitch:
                    if (Step == FoStep.PowerOff)
                        return dock.Interact(DockAction.PowerSwitch) ? Ok(n, dock.LastMessage) : Refuse(n, dock.LastMessage);
                    if (Step == FoStep.PowerOn)
                    {
                        if (!dock.Interact(DockAction.PowerSwitch)) return Refuse(n, dock.LastMessage);
                        Advance(FoStep.ReleaseAndLift, "恢复供电，涡轮重新转动。点夹具握把：松开，让七号离座。");
                        return Ok(n, Message);
                    }
                    if (Step > FoStep.PowerOff && Step < FoStep.PowerOn)
                    {
                        if (cover.Location != PartLocation.Installed) return Refuse(n, "上盖没装好，不能通电。");
                        if (latchOuter.Location != PartLocation.Installed || latchRear.Location != PartLocation.Installed) return Refuse(n, "锁扣还没扣回，不能通电。");
                        return Refuse(n, "维修还没做完，先别通电。");
                    }
                    return dock.Interact(DockAction.PowerSwitch) ? Ok(n, dock.LastMessage) : Refuse(n, dock.LastMessage);
                case DockAction.EngineLeft:
                    if (Step < FoStep.InspectLeftEngine) return Refuse(n, dock.InspectLeftEngine() ? "先完成停靠和断电。" : dock.LastMessage);
                    if (Step == FoStep.InspectLeftEngine) return InspectLeft(n, di.GetComponent<Collider>());
                    return Refuse(n, "已经在检查左引擎了。");
                default:
                    return Refuse(n, $"{n}：本单用不到。");
            }
        }

        /// <summary>开始检查左引擎：关掉维修座集成留下的整块左引擎检查代理（否则它会盖住锁扣、上盖和轴承的点选）。</summary>
        bool InspectLeft(string n, Collider proxy)
        {
            if (!dock.InspectLeftEngine()) return Refuse(n, dock.LastMessage);
            if (proxy == null && engineLHinge != null)
                proxy = engineLHinge.GetComponentsInChildren<DockInteractable>(true).FirstOrDefault(d => d.action == DockAction.EngineLeft)?.GetComponent<Collider>();
            if (proxy != null) proxy.enabled = false;
            Advance(FoStep.ReleaseLatches, "左引擎：上盖由外侧和后侧两个锁扣固定。先扳开两个锁扣（后侧那个要转到背面，按 3）。");
            return Ok(n, Message);
        }

        bool ClickPart(FirstOrderPart p)
        {
            string n = p.realPath;
            if (p == rightEngine) return Refuse(n, "右引擎的手感和进气口都正常，不用拆。");
            if (p != newBearing && !EngineSafe && Step < FoStep.PowerOn)
                return Refuse(n, "七号还在供电或叶轮还在转。先停靠、夹紧、断电，等涡轮停稳。");
            if (Step == FoStep.InspectLeftEngine && p != newBearing) return InspectLeft(n, null);   // 点左引擎上的任何部件都算“开始检查”
            if (Step < FoStep.InspectLeftEngine) return Refuse(n, "先完成停靠和断电。");

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
                        Advance(FoStep.PowerOn, "两个锁扣都扣回了。点断电开关：通电。");
                    else Say($"{p.displayName}已扣回。还有一个锁扣（后侧按 3 转到背面）。");
                    return Ok(n, Message);
                }
                return Refuse(n, p.Location == PartLocation.Released ? "这个锁扣已经扳开了。" : "现在不用动锁扣。");
            }

            if (p == cover)
            {
                if (Step == FoStep.ReleaseLatches) return Refuse(n, "还有锁扣没扳开。后侧那个要转到背面（按 3）。");
                if (Step == FoStep.RemoveCover)
                {
                    StartCoroutine(RemoveFromEngine(cover, coverLift, FoStep.PlaceCover, "上盖总成已取下。点工作台操作垫上的落点，放下它。", matZone));
                    return Ok(n, "取下上盖总成（上盖 + 进气唇口 + 护栅 + 风道）。");
                }
                if (Step == FoStep.PlaceCover) return Refuse(n, "上盖在手上：点工作台操作垫上的落点。");
                if (Step == FoStep.ReinstallCover && cover.Location == PartLocation.OnBench)
                {
                    StartCoroutine(InstallOnEngine(cover, coverLift, FoStep.CloseLatches, "上盖总成装回。扣回两个锁扣（后侧按 3）。"));
                    return Ok(n, "把上盖总成从操作垫装回左引擎。");
                }
                if (cover.Location == PartLocation.OnBench) return Refuse(n, newBearing.Location != PartLocation.Installed ? "轴承位还空着，先装新轴承。" : "现在不用动上盖。");
                return Refuse(n, "现在不用动上盖。");
            }

            if (p == bearing)
            {
                if (cover.Location == PartLocation.Installed || Step < FoStep.LocateBearing && cover.Location == PartLocation.Held)
                    return Refuse(n, "轴承被上盖总成挡住：先取下上盖。");
                if (Step == FoStep.LocateBearing)
                {
                    BearingLocated = true;
                    SetFaultTint(bearing, true);
                    Advance(FoStep.RemoveBearing, "找到了：左上轴承 Engine_BearingTop_L 磨损（占位诊断：只用红色标出）。再点它，取下旧轴承。");
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
                    return Ok(n, "从轴承盒取出新轴承（占位），装到左上轴承位。");
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

        IEnumerator PlaceOnBench(FirstOrderPart p, FirstOrderDropZone z)
        {
            Busy = true;
            var b = p.WorldBounds();
            float above = p.transform.position.y - b.min.y;                      // 原点到包围盒底面的高度
            var offsetXZ = new Vector3(p.transform.position.x - b.center.x, 0f, p.transform.position.z - b.center.z);
            var land = z.landing.position + offsetXZ + Vector3.up * (above + 0.001f);
            var pos = p.transform.position;
            yield return MoveTo(p.transform, new Vector3(pos.x, Mathf.Max(pos.y, travelHeight), pos.z));
            yield return MoveTo(p.transform, new Vector3(land.x, Mathf.Max(pos.y, travelHeight), land.z));
            yield return MoveTo(p.transform, land);
            z.ShowMarker(false);
            p.Location = PartLocation.OnBench; p.LocationDetail = $"{z.displayName}（{z.benchObjectPath}）";
            if (p == cover) Advance(FoStep.LocateBearing, "上盖总成放在操作垫上。现在能看到左上轴承：点它检查。");
            else Advance(FoStep.FetchNewBearing, $"旧轴承放进{oldTrayZone.displayName}。点工作台轴承盒上的新轴承（占位）。");
            Busy = false;
        }

        IEnumerator InstallOnEngine(FirstOrderPart p, float lift, FoStep next, string msg)
        {
            Busy = true;
            var home = p.HomeWorldPose();
            var pos = p.transform.position;
            var above = home.position + engineLHinge.up * lift;
            yield return MoveTo(p.transform, new Vector3(pos.x, Mathf.Max(pos.y, travelHeight), pos.z));
            yield return MoveTo(p.transform, new Vector3(above.x, Mathf.Max(above.y, travelHeight), above.z));
            yield return MoveTo(p.transform, above);
            p.transform.rotation = home.rotation;
            yield return MoveTo(p.transform, home.position);
            p.Reattach();
            p.Location = PartLocation.Installed; p.LocationDetail = "七号左引擎原位";
            Advance(next, msg);
            Busy = false;
        }

        IEnumerator InstallNewBearing()
        {
            Busy = true;
            var home = bearing.HomeWorldPose();            // 新轴承装到旧轴承的原位
            var t = newBearing.transform;
            var pos = t.position;
            var above = home.position + engineLHinge.up * bearingLift;
            yield return MoveTo(t, new Vector3(pos.x, Mathf.Max(pos.y, travelHeight), pos.z));
            yield return MoveTo(t, new Vector3(above.x, Mathf.Max(above.y, travelHeight), above.z));
            yield return MoveTo(t, above);
            t.rotation = home.rotation;
            yield return MoveTo(t, home.position);
            t.SetParent(bearing.HomeParent, true);
            newBearing.Location = PartLocation.Installed; newBearing.LocationDetail = "七号左上轴承位（占位新轴承）";
            Advance(FoStep.ReinstallCover, "新轴承（占位）装上了。点操作垫上的上盖总成，装回去。");
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
            bool bearingOk = newBearing.Location == PartLocation.Installed && bearing.Location == PartLocation.OnBench;
            bool coverOk = cover.Location == PartLocation.Installed && latchOuter.Location == PartLocation.Installed && latchRear.Location == PartLocation.Installed;
            RetestPassed = bearingOk && coverOk && maxDy < 0.005f && dock.Rotors.Powered;
            RetestDetail = $"悬停 {retestSeconds:F0} s、{samples} 帧：左右引擎高度差最大 {maxDy * 1000:F1} mm；轴承已更换 {bearingOk}；上盖和锁扣复位 {coverOk}。" +
                           "（占位判定：模型没有失衡模拟）";
            Step = FoStep.Done;
            Say(RetestPassed ? "复测通过（占位判定）：左右等高，转子同速。首单原型流程结束。" : "复测未通过：" + RetestDetail);
            Acted?.Invoke("Retest", RetestPassed, Message);
            Busy = false;
        }

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static void SetFaultTint(FirstOrderPart p, bool on)
        {
            foreach (var r in p.Renderers())
            {
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                if (on) mpb.SetColor(BaseColor, new Color(0.95f, 0.22f, 0.16f)); else mpb.Clear();
                r.SetPropertyBlock(mpb);
            }
        }

        public string NextHint()
        {
            switch (Step)
            {
                case FoStep.SeatRobot: return "点维修座夹具的黄色握把：张开，让七号落座";
                case FoStep.ClampRobot: return dock.State == DockState.SeatedOpen ? "点夹具握把：夹紧" : "等七号落座…";
                case FoStep.PowerOff: return dock.State == DockState.SpinningDown ? "等涡轮停转…" : "点断电开关：OFF";
                case FoStep.InspectLeftEngine: return "点左引擎（上盖）：开始检查";
                case FoStep.ReleaseLatches: return "扳开外侧锁扣、后侧锁扣（按 3 转到背面）";
                case FoStep.RemoveCover: return "点左上盖：取下上盖总成";
                case FoStep.PlaceCover: return "点工作台操作垫上的黄色落点";
                case FoStep.LocateBearing: return "点左上轴承：检查";
                case FoStep.RemoveBearing: return "再点左上轴承：取下";
                case FoStep.PlaceOldBearing: return $"点工作台{oldTrayZone.displayName}里的黄色落点";
                case FoStep.FetchNewBearing: return "点工作台轴承盒上的新轴承（占位）";
                case FoStep.ReinstallCover: return "点操作垫上的上盖总成：装回";
                case FoStep.CloseLatches: return "扣回外侧锁扣、后侧锁扣（按 3 转到背面）";
                case FoStep.PowerOn: return "点断电开关：通电";
                case FoStep.ReleaseAndLift: return "点夹具握把：松开，让七号离座";
                case FoStep.Retest: return "复测中…";
                default: return "完成";
            }
        }
    }
}
