using System.Collections.Generic;
using BorderRepair.Dock;
using UnityEngine;

namespace BorderRepair.FirstOrder.Slice
{
    /// <summary>一次观察的结果（界面显示用）。</summary>
    public struct SliceObservationResult
    {
        public string key;          // 诊断记录用的观察项；为空 = 不计入诊断记录
        public string title;
        public string body;
        public bool seen;           // 看清了（距离够近、没被挡住）
        public string suggestShot;  // 看不清时建议切到的镜头（FirstOrderCameraRig 的 id），可为空
        public float distance;
        public float maxDistance;
    }

    /// <summary>
    /// 两晚切片的“观察”：只读 FirstOrderFlow / 维修座的状态，给出看到的内容。不推进步骤、不动供电、不切镜头——
    /// 维修流程仍然只有 FirstOrderFlow 一个状态机，供电只由维修座决定。
    /// 观察有有效距离：镜头离目标太远就只给“看不清”和建议镜头。距离按镜头位置到目标包围盒最近点算。
    /// </summary>
    public class SliceObservation
    {
        public const string IntakeL = "intake_L", EngineR = "engine_R", BearingWorn = "bearing_worn", Label = "label", Compare = "compare";

        /// <summary>诊断记录里列出的观察项（按建议顺序）。</summary>
        public static readonly (string key, string label)[] DiagnosisItems =
        {
            (EngineR, "右引擎（正常对照）"),
            (IntakeL, "左进气口"),
            (Label, "上盖内侧保养记录"),
            (BearingWorn, "左上轴承（原位）"),
            (Compare, "新旧轴承对比"),
        };

        // 有效观察距离（m）。常用镜头实测：左 / 右引擎镜头约 0.70 m，近看约 0.30 m，保养记录约 0.21 m，新旧轴承约 0.4 m，总览约 2 m。
        public const float IntakeMax = 0.9f, CoverMax = 1.2f, BearingMax = 0.45f, LabelMax = 0.40f, CompareMax = 0.65f, DockMax = 2.5f;

        readonly FirstOrderFlow flow;
        readonly HashSet<string> seen = new HashSet<string>();

        public SliceObservation(FirstOrderFlow flow) { this.flow = flow; }

        public bool HasSeen(string key) => seen.Contains(key);
        public int SeenCount => seen.Count;

        bool Stopped => flow.Dock.State == DockState.RotorsStopped;

        public SliceObservationResult Observe(Component target, Camera cam)
        {
            var r = Describe(target);
            if (target == null) return r;
            float d = DistanceTo(target, cam);
            r.distance = d;
            if (r.maxDistance > 0f && d > r.maxDistance)
            {
                r.seen = false;
                r.body = $"太远了，看不清（镜头离它 {d:F2} m，要在 {r.maxDistance:F2} m 以内）。" +
                         (string.IsNullOrEmpty(r.suggestShot) ? "" : $"切到镜头「{ShortLabel(r.suggestShot)}」再看。");
                r.key = null;
                return r;
            }
            if (r.seen && !string.IsNullOrEmpty(r.key)) seen.Add(r.key);
            return r;
        }

        public static string ShortLabel(string shot)
        {
            switch (shot)
            {
                case FirstOrderCameraRig.Dock: return "维修座";
                case FirstOrderCameraRig.EngineL: return "左引擎";
                case FirstOrderCameraRig.EngineRear: return "左引擎背面";
                case FirstOrderCameraRig.EngineClose: return "近看";
                case FirstOrderCameraRig.EngineR: return "右引擎";
                case FirstOrderCameraRig.Bench: return "工作台";
                case FirstOrderCameraRig.Record: return "保养记录";
                case FirstOrderCameraRig.Compare: return "新旧轴承";
                case FirstOrderCameraRig.Overview: return "总览";
                default: return shot;
            }
        }

        static float DistanceTo(Component c, Camera cam)
        {
            Bounds b;
            if (c is FirstOrderPart p) b = p.WorldBounds();
            else
            {
                var col = c.GetComponent<Collider>();
                b = col != null ? col.bounds : new Bounds(c.transform.position, Vector3.zero);
            }
            return Vector3.Distance(cam.transform.position, b.ClosestPoint(cam.transform.position));
        }

        static SliceObservationResult R(string key, string title, string body, float max, string shot, bool seen = true) =>
            new SliceObservationResult { key = key, title = title, body = body, maxDistance = max, suggestShot = shot, seen = seen };

        SliceObservationResult Describe(Component target)
        {
            const string Close = FirstOrderCameraRig.EngineClose, EL = FirstOrderCameraRig.EngineL, ER = FirstOrderCameraRig.EngineR,
                         Rec = FirstOrderCameraRig.Record, Cmp = FirstOrderCameraRig.Compare, D = FirstOrderCameraRig.Dock, Rear = FirstOrderCameraRig.EngineRear;
            var cover = flow.Cover;
            switch (target)
            {
                case null:
                    return R(null, "（没有对象）", "这里没有可观察的东西。把鼠标移到维修座、七号的引擎或工作台上的零件上再观察。", 0f, null, false);

                case FirstOrderPart p when p == flow.RightEngine:
                    return R(EngineR, "右引擎（正常，作对照）",
                        Stopped
                            ? "进气口护栅干净，看得到风道深处；断电停稳后手转叶轮顺滑、没有异响和轴向晃动。左引擎和它对比：进气口糊着积尘，上轴承位有磨损（要拆上盖才看得到）。"
                            : "进气口护栅干净，转子转速和左侧一致。转动中只能看外观；手转叶轮要先断电、等叶轮停稳。",
                        IntakeMax, ER);

                case FirstOrderPart p when p == flow.Clog:
                    if (p.Location == PartLocation.Cleared)
                        return R(IntakeL, "左进气口（已清理）", "护栅干净了，和右侧对照一样。但只清理进气口不算修好，上盖下面的左上轴承还要检查。", IntakeMax, Close);
                    return R(IntakeL, "左进气口：堵塞",
                        "护栅上糊着暖褐色的积尘和几缕纤维，开口被挡住一大片；右侧对照的进气口是干净的。" +
                        (Stopped ? "现在已断电停稳，左键可以清理。" : "涡轮还在转：现在只能看，清理要先断电、等叶轮停稳。"),
                        IntakeMax, Close);

                case FirstOrderPart p when p == cover:
                    if (p.Location == PartLocation.OnBench)
                        return R(Label, "上盖内侧 · 保养记录",
                            "贴纸表头 L-03 SERVICE，三行手写：\n03.11  BRG CHECK   OK（轴承检查正常）\n07.02  INTAKE CLEAN（清理进气口）\n11.20  BRG NOISE - WATCH（轴承有异响，待观察）\n" +
                            "最后一行下面画了提示线：上次就记下了轴承异响。",
                            LabelMax, Rec);
                    if (p.Location == PartLocation.Held)
                        return R(null, "左上盖总成（拿在手上）", "先放到工作台操作垫上，翻过来才能看内侧。", 0f, null);
                    return R(null, "左上盖总成",
                        "上盖 + 进气唇口 + 护栅 + 风道一起拆装，由外侧、后侧两个锁扣固定（后侧在镜头「左引擎背面」）。内侧贴着保养记录，拆下翻过来才看得到。",
                        CoverMax, EL);

                case FirstOrderPart p when p == flow.LatchOuter || p == flow.LatchRear:
                    return R(null, p.displayName,
                        p.Location == PartLocation.Released ? "已扳开。" : "扣着。" + (p == flow.LatchRear ? "它在引擎背面。" : ""),
                        CoverMax, p == flow.LatchRear ? Rear : EL);

                case FirstOrderPart p when p == flow.Bearing:
                    if (p.Location == PartLocation.Installed && cover.Location != PartLocation.OnBench)
                        return R(null, "左上轴承", "被上盖总成挡着，看不到。先扳开两个锁扣、取下上盖。", 0f, null, false);
                    if (p.Location == PartLocation.Installed)
                        return R(BearingWorn, "左上轴承（原位）：磨损",
                            "外圈端面有一圈磨痕和锈斑，旁边散着细小的金属碎屑；对照右引擎（正常）没有这些。和保养记录里 11.20 的“轴承异响”对得上。",
                            BearingMax, Close);
                    if (p.Location == PartLocation.Held)
                        return R(null, "旧轴承（拿在手上）", "放进工作台旧件托盘，再和轴承盒上的新轴承对比。", 0f, null);
                    return R(Compare, "新旧轴承对比",
                        "托盘里的旧轴承：外圈磨痕、锈斑、碎屑。" +
                        (flow.NewBearing.Location == PartLocation.Stored ? "轴承盒上的新轴承：外圈光洁，压印清楚。同一型号，可以直接换。" : "新轴承已经装到左上轴承位。"),
                        CompareMax, Cmp);

                case FirstOrderPart p when p == flow.NewBearing:
                    if (p.Location == PartLocation.Installed)
                        return R(null, "新轴承（已装上）", "装在左上轴承位，和原来的位置、朝向一致。", BearingMax, Close);
                    return R(flow.Bearing.Location == PartLocation.OnBench ? Compare : null, "新轴承（轴承盒上）",
                        "外圈光洁，压印清楚，没有锈。" + (flow.Bearing.Location == PartLocation.OnBench ? "和托盘里的旧轴承对比：旧的有磨痕、锈斑和碎屑。" : "旧轴承还在七号身上。"),
                        CompareMax, Cmp);

                case FirstOrderPart p:
                    return R(null, p.displayName, p.LocationDetail, CoverMax, null);

                case FirstOrderDropZone z:
                    return R(null, "落点：" + z.displayName, "放零件的位置（" + z.benchObjectPath + "）。", DockMax, FirstOrderCameraRig.Overview);

                case DockInteractable di:
                    switch (di.action)
                    {
                        case DockAction.Clamps:
                            return R(null, "维修座夹具握把", "维修座：" + DockStateText(flow.Dock.State) + "。", DockMax, D);
                        case DockAction.PowerSwitch:
                            return R(null, "断电开关",
                                $"现在{(flow.Dock.PowerOn ? "通电（ON）" : "断电（OFF）")}，转子 {flow.Dock.Rotors.SpeedDegPerSec:F0}°/s。" +
                                (flow.Dock.State == DockState.SpinningDown ? "叶轮还在减速，等它停稳。" : ""),
                                DockMax, D);
                        case DockAction.EngineLeft:
                            return R(null, "左引擎（外观）",
                                "从这里看进气口发暗，像是堵了；凑近看要切到镜头「左引擎」或「近看」。" + (Stopped ? "" : "现在还通着电。"),
                                CoverMax, EL);
                        default:
                            return R(null, di.name, "本单用不到。", DockMax, null);
                    }

                default:
                    return R(null, target.name, "本单用不到。", 0f, null);
            }
        }

        public static string DockStateText(DockState s)
        {
            switch (s)
            {
                case DockState.Hovering: return "七号悬停未落座";
                case DockState.ClampsOpening: return "夹具张开中";
                case DockState.Descending: return "七号落座中";
                case DockState.SeatedOpen: return "已落座，夹具张开";
                case DockState.Clamping: return "夹具合拢中";
                case DockState.Clamped: return "已夹紧，仍通电";
                case DockState.SpinningDown: return "已断电，叶轮减速中";
                case DockState.RotorsStopped: return "已断电，叶轮停稳";
                default: return s.ToString();
            }
        }
    }
}
