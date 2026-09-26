using System;
using System.Collections;
using System.Collections.Generic;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Inspection;
using BorderRepair.Narrative;
using UnityEngine;

namespace BorderRepair.Tools
{
    /// <summary>
    /// 桌面版的工具动作（只放在叙事场景里）：选中工具后单击零件时，手套手拿着工具
    /// “进入 → 对准 → 接触操作 → 退出”。规则仍由 RepairSession 决定：
    /// - 开始前用 PreviewAction 判断（不改状态）；判断不通过（工具不对、顺序不对、已做过）时不播放动画，照常立即反馈；
    /// - 真正的 PerformAction 在工具接触零件的那一刻调用，螺丝转出、盖板弹起、灯光、声音都从这一刻开始；
    /// - 动画中途被打断（阶段切换、重新开始、换下一件物品）时，接触之前什么都没改；接触之后的状态本来就由会话记录。
    /// 工具姿态每帧都从零件上的 ToolAnchor（零件本地坐标）重新计算，物品旋转或缩放后仍然对齐；不使用相机坐标。
    /// 以后接 VR：手柄直接驱动 ToolRig，在接触锚点时调用同一个 PerformAction，本组件可以不用。
    /// </summary>
    public class RepairToolAnimator : MonoBehaviour
    {
        public enum Phase { Idle, Enter, Align, Contact, Exit }

        [SerializeField] RepairStationController controller;
        [SerializeField] RepairFeedbackFx fx;
        [SerializeField] List<ToolRig> rigPrefabs = new List<ToolRig>();
        [SerializeField] TesterReadout tester;

        [Header("通用（米、秒；距离按物品缩放）")]
        [SerializeField] float approachDistance = 0.09f;
        [SerializeField] float approachSideOffset = 0.05f;
        [SerializeField] float alignDistance = 0.012f;
        [SerializeField] float enterSeconds = 0.42f;
        [SerializeField] float alignSeconds = 0.2f;
        [SerializeField] float pressSeconds = 0.12f;
        [SerializeField] float exitSeconds = 0.36f;

        [Header("各工具")]
        [SerializeField] float screwdriverSeat = 0.0006f;
        [SerializeField] float pryDepth = 0.0025f;
        [SerializeField] float pryLeverDegrees = 16f;
        [SerializeField] float probePress = 0.0003f;
        [SerializeField] float probeHoldSeconds = 0.6f;
        [SerializeField] float plugDepth = 0.004f;
        [SerializeField] float plugInsertSeconds = 0.25f;
        [SerializeField] float plugReadSeconds = 0.9f;

        readonly Dictionary<ToolKind, ToolRig> rigs = new Dictionary<ToolKind, ToolRig>();
        RepairSession session;
        Coroutine running;
        ToolRig activeRig;
        ToolAnchor activeAnchor;

        public bool Busy => running != null;
        public Phase CurrentPhase { get; private set; } = Phase.Idle;
        public string ActivePointId { get; private set; }
        public ToolKind? ActiveKind => activeRig != null ? activeRig.Kind : (ToolKind?)null;
        public ToolRig ActiveRig => activeRig;
        public ToolAnchor ActiveAnchor => activeAnchor;
        /// <summary>接触时刻 PerformAction 的结果（最近一次）。</summary>
        public ActionOutcome? LastContactOutcome { get; private set; }
        public event Action<Phase> PhaseChanged;

        /// <summary>供编辑器生成工具配置。</summary>
        public void Configure(RepairStationController c, RepairFeedbackFx feedback, List<ToolRig> prefabs, TesterReadout readout)
        {
            controller = c;
            fx = feedback;
            rigPrefabs = prefabs;
            tester = readout;
        }

        void Start()
        {
            if (controller == null || controller.Session == null) { enabled = false; return; }
            session = controller.Session;
            session.StageChanged += OnStageChanged;
            session.CaseStarted += OnCaseStarted;
            foreach (var p in rigPrefabs)
            {
                if (p == null || rigs.ContainsKey(p.Kind)) continue;
                var rig = Instantiate(p, transform);
                rig.name = p.name;
                rig.gameObject.SetActive(false);
                rigs[p.Kind] = rig;
            }
        }

        void OnDestroy()
        {
            if (session == null) return;
            session.StageChanged -= OnStageChanged;
            session.CaseStarted -= OnCaseStarted;
        }

        void OnStageChanged(RepairStage stage)
        {
            if (stage != RepairStage.Inspect) Cancel();
        }

        void OnCaseStarted(RepairCaseData data, int index)
        {
            Cancel();
            if (tester != null) tester.Show(TesterReadout.Reading.Ready);
        }

        public static ToolKind KindFor(RepairActionType action, ToolAnchor anchorOnTarget) =>
            action == RepairActionType.RemoveFastener ? ToolKind.Screwdriver
            : action == RepairActionType.OpenHousing ? ToolKind.Pry
            : anchorOnTarget != null && anchorOnTarget.Kind == ToolKind.Plug ? ToolKind.Plug : ToolKind.Probe;

        /// <summary>零件上与这个动作对应的锚点（没有则返回 null，调用方按原来的方式立即执行）。</summary>
        public ToolAnchor FindAnchor(RepairActionType action, string pointId)
        {
            if (!controller.TryGetPoint(pointId, out var point)) return null;
            var anchors = point.GetComponentsInChildren<ToolAnchor>(true);
            foreach (var a in anchors)
            {
                if (a.GetComponentInParent<InspectionPoint>(true) != point) continue;       // 嵌套部位的锚点归子部位
                if (action == RepairActionType.RemoveFastener && a.Kind == ToolKind.Screwdriver) return a;
                if (action == RepairActionType.OpenHousing && a.Kind == ToolKind.Pry) return a;
                if (action == RepairActionType.ServiceModule && (a.Kind == ToolKind.Probe || a.Kind == ToolKind.Plug)) return a;
            }
            return null;
        }

        /// <summary>
        /// 尝试用动画执行一步。返回 true 表示动画已开始（结果在接触时刻产生）；
        /// 返回 false 表示不播放动画（忙、判断不通过、没有锚点或工具），调用方应立即调用 PerformAction 给出正常反馈。
        /// </summary>
        public bool TryBegin(RepairActionType action, string pointId)
        {
            if (Busy || session == null) return false;
            if (session.PreviewAction(action, pointId, out _) != ActionOutcome.Performed) return false;
            var anchor = FindAnchor(action, pointId);
            if (anchor == null) return false;
            if (!rigs.TryGetValue(KindFor(action, anchor), out var rig)) return false;
            var item = controller.Inspector.CurrentItem;
            if (item == null) return false;
            activeRig = rig;
            activeAnchor = Freeze(anchor, item.transform);
            ActivePointId = pointId;
            LastContactOutcome = null;
            running = StartCoroutine(Run(action, pointId, rig, activeAnchor));
            return true;
        }

        /// <summary>
        /// 把锚点的当前姿态复制到物品根物体下：随物品旋转、缩放，但不随零件移动——
        /// 螺丝转出后会被送到零件盘、盖板会弹起，工具退出时不应跟着零件走。
        /// </summary>
        static ToolAnchor Freeze(ToolAnchor source, Transform itemRoot)
        {
            var go = new GameObject("ToolAnchor_Active");
            go.transform.SetParent(itemRoot, false);
            go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            go.transform.localScale = Vector3.one;
            var a = go.AddComponent<ToolAnchor>();
            a.Configure(source.Kind, go.transform.InverseTransformDirection(source.LeverToward));
            return a;
        }

        void ReleaseAnchor()
        {
            if (activeAnchor != null) Destroy(activeAnchor.gameObject);
            activeAnchor = null;
        }

        /// <summary>中断：收起工具。接触之前没有任何状态变化；接触之后的状态已由会话记录，零件按会话状态显示。</summary>
        public void Cancel()
        {
            if (running != null) StopCoroutine(running);
            running = null;
            if (activeRig != null)
            {
                activeRig.SetSpin(0f);
                activeRig.gameObject.SetActive(false);
            }
            activeRig = null;
            ReleaseAnchor();
            ActivePointId = null;
            SetPhase(Phase.Idle);
        }

        void SetPhase(Phase p)
        {
            if (CurrentPhase == p) return;
            CurrentPhase = p;
            PhaseChanged?.Invoke(p);
        }

        // ---------- 姿态 ----------

        /// <summary>
        /// 工具在锚点坐标系里的姿态：沿 up 离开接触点 along 米、朝手臂一侧偏 side 米（都按物品缩放），
        /// 再绕接触点额外旋转 extra；工具轴线对准 up，手套手臂朝 forward。
        /// </summary>
        public static void Pose(ToolAnchor anchor, float along, float side, Quaternion extra, out Vector3 position, out Quaternion rotation)
        {
            float s = anchor.WorldScale;
            var up = anchor.OutAxis;
            var arm = anchor.ArmDirection - up * Vector3.Dot(anchor.ArmDirection, up);
            arm = arm.sqrMagnitude > 1e-8f ? arm.normalized : Vector3.Cross(up, Vector3.right).normalized;
            var x = -arm;                                            // 手套手臂朝工具本地 -X
            var z = Vector3.Cross(x, up);
            var baseRot = Quaternion.LookRotation(z, up);
            var tip = anchor.ContactPoint;
            position = tip + extra * (up * (along * s)) + arm * (side * s);
            rotation = extra * baseRot;
        }

        void Place(ToolRig rig, ToolAnchor anchor, float along, float side, Quaternion extra)
        {
            Pose(anchor, along, side, extra, out var p, out var r);
            rig.transform.SetPositionAndRotation(p, r);
            rig.transform.localScale = Vector3.one * anchor.WorldScale;
        }

        static float Ease(float k) => 1f - (1f - k) * (1f - k);

        IEnumerator Lerp(float seconds, Action<float> apply)
        {
            float t = 0f;
            while (t < seconds)
            {
                if (activeAnchor == null) { Cancel(); yield break; }       // 物品被换掉
                apply(Ease(Mathf.Clamp01(t / seconds)));
                t += Time.deltaTime;
                yield return null;
            }
            apply(1f);
        }

        IEnumerator Run(RepairActionType action, string pointId, ToolRig rig, ToolAnchor anchor)
        {
            rig.SetSpin(0f);
            Place(rig, anchor, approachDistance, approachSideOffset, Quaternion.identity);
            rig.gameObject.SetActive(true);
            var kind = rig.Kind;

            // 进入：从手臂一侧、离零件较远处移到正对接触点上方
            SetPhase(Phase.Enter);
            yield return Lerp(enterSeconds, k => Place(rig, anchor, Mathf.Lerp(approachDistance, alignDistance, k), Mathf.Lerp(approachSideOffset, 0f, k), Quaternion.identity));
            // 对准：沿工具轴线靠近到刚好贴上
            SetPhase(Phase.Align);
            yield return Lerp(alignSeconds, k => Place(rig, anchor, Mathf.Lerp(alignDistance, 0.0015f, k), 0f, Quaternion.identity));

            // 接触操作
            SetPhase(Phase.Contact);
            float seat = kind == ToolKind.Screwdriver ? -screwdriverSeat : kind == ToolKind.Pry ? -pryDepth : kind == ToolKind.Probe ? -probePress : -plugDepth;
            float pressTime = kind == ToolKind.Plug ? plugInsertSeconds : pressSeconds;
            yield return Lerp(pressTime, k => Place(rig, anchor, Mathf.Lerp(0.0015f, seat, k), 0f, Quaternion.identity));

            // 接触时刻：由会话判定并执行（与零件动作、灯光、声音同一帧开始）
            var outcome = session.PerformAction(action, pointId);
            LastContactOutcome = outcome;
            var extra = Quaternion.identity;
            float along = seat;
            if (outcome == ActionOutcome.Performed)
            {
                switch (kind)
                {
                    case ToolKind.Screwdriver:
                    {
                        // 与 RepairFeedbackFx 让螺丝转出的参数一致：同样的时长、圈数、退出距离
                        float secs = fx != null ? fx.UnscrewSeconds : 0.7f, turns = fx != null ? fx.UnscrewTurns : 3f, travel = fx != null ? fx.UnscrewTravel : 0.006f;
                        yield return Lerp(secs, k =>
                        {
                            rig.SetSpin(-turns * 360f * k);
                            Place(rig, anchor, seat + travel * k, 0f, Quaternion.identity);
                        });
                        along = seat + travel;
                        break;
                    }
                    case ToolKind.Pry:
                    {
                        // 撬：绕刃口倒向施力方向，盖板同时弹起（RepairFeedbackFx）
                        var from = anchor.OutAxis;
                        var to = Vector3.RotateTowards(from, anchor.LeverToward, pryLeverDegrees * Mathf.Deg2Rad, 0f);
                        var lever = Quaternion.FromToRotation(from, to);
                        float secs = fx != null ? fx.CoverPopSeconds : 0.32f;
                        yield return Lerp(secs, k => Place(rig, anchor, seat, 0f, Quaternion.Slerp(Quaternion.identity, lever, k)));
                        extra = lever;
                        break;
                    }
                    case ToolKind.Probe:
                        if (tester != null) tester.Show(pointId == "force_limiter" ? TesterReadout.Reading.Bypass : TesterReadout.Reading.Ready);
                        yield return Hold(probeHoldSeconds, rig, anchor, seat);
                        break;
                    case ToolKind.Plug:
                        if (tester != null) tester.Show(TesterReadout.Reading.Log);
                        yield return Hold(plugReadSeconds, rig, anchor, seat);
                        // 拔出
                        yield return Lerp(plugInsertSeconds, k => Place(rig, anchor, Mathf.Lerp(seat, alignDistance, k), 0f, Quaternion.identity));
                        along = alignDistance;
                        break;
                }
            }

            // 退出：沿工具轴线离开，回到手臂一侧
            SetPhase(Phase.Exit);
            float startAlong = along;
            var startExtra = extra;
            yield return Lerp(exitSeconds, k => Place(rig, anchor, Mathf.Lerp(startAlong, approachDistance, k), Mathf.Lerp(0f, approachSideOffset, k),
                                                     Quaternion.Slerp(startExtra, Quaternion.identity, k)));
            rig.SetSpin(0f);
            rig.gameObject.SetActive(false);
            running = null;
            activeRig = null;
            ReleaseAnchor();
            ActivePointId = null;
            SetPhase(Phase.Idle);
        }

        IEnumerator Hold(float seconds, ToolRig rig, ToolAnchor anchor, float along)
        {
            float t = 0f;
            while (t < seconds)
            {
                if (activeAnchor == null) { Cancel(); yield break; }
                Place(rig, anchor, along, 0f, Quaternion.identity);         // 物品被旋转时也保持贴合
                t += Time.deltaTime;
                yield return null;
            }
        }
    }
}
