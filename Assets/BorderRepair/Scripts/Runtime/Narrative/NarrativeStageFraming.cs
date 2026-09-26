using System;
using System.Collections.Generic;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Inspection;
using UnityEngine;

namespace BorderRepair.Narrative
{
    /// <summary>
    /// 叙事场景的分阶段取景（只放在叙事场景里；默认营业场景没有本组件，镜头行为不变）。
    /// 规则：
    /// - 检查阶段拿着某件工具时，镜头对准这件工具“下一步要处理的零件”；空手 / 扫描时看整件物品；
    /// - 刚完成一步时，先停在这一步的零件上 holdAfterStep 秒，让反馈看得见，然后再转向下一个零件；
    /// - 其他阶段看整件物品。
    /// 每个零件的取景（框住哪些部位、物品转到什么角度、占画面多少）在 shots 里配置，按 pointId 查找。
    /// 玩家仍可以拖动旋转、滚轮缩放；R 回到当前取景的默认角度。
    /// </summary>
    public class NarrativeStageFraming : MonoBehaviour
    {
        [Serializable]
        public class Shot
        {
            [Tooltip("对应的 pointId（工具的目标零件）")]
            public string pointId;
            [Tooltip("取景要框住的部位；留空时只框 pointId 本身")]
            public string[] frameIds = new string[0];
            public float yaw;
            [Tooltip("负值让物品顶面转向镜头")]
            public float pitch;
            [Tooltip("框住的部位（包围球直径）占画面短边的比例")]
            [Range(0.1f, 1f)] public float fill = 0.5f;
            [Tooltip("最小取景半径（米），防止很小的零件被放得过大")]
            public float minRadius = 0.03f;
        }

        [SerializeField] RepairStationController controller;
        [SerializeField] float holdAfterStep = 1.1f;
        [Tooltip("整件物品取景：包围球直径占画面短边的比例；0 = 使用检查台默认距离。细长物品可以大于 1。")]
        [SerializeField] float overviewFill = 1.2f;
        [SerializeField] List<Shot> shots = new List<Shot>();

        /// <summary>当前取景：pointId，或 "overview"（整件物品）。</summary>
        public string CurrentShotId { get; private set; } = Overview;
        public const string Overview = "overview";

        public IReadOnlyList<Shot> Shots => shots;

        RepairSession session;
        string heldShot;
        float holdUntil;

        /// <summary>供编辑器生成工具配置。</summary>
        public void Configure(RepairStationController c, List<Shot> list, float hold, float overview)
        {
            controller = c;
            shots = list;
            holdAfterStep = hold;
            overviewFill = overview;
        }

        void Start()
        {
            if (controller == null || controller.Session == null) { enabled = false; return; }
            session = controller.Session;
            session.StepPerformed += OnStepPerformed;
            // 新物品放上检查台时直接到位（不从上一件物品的取景慢慢移过来）
            session.CaseStarted += (_, __) => { heldShot = null; Apply(DesiredShot(), true); };
            Apply(DesiredShot(), true);
        }

        void OnDestroy()
        {
            if (session != null) session.StepPerformed -= OnStepPerformed;
        }

        void OnStepPerformed(RepairStepDefinition step)
        {
            if (FindShot(step.targetPointId) == null) return;
            heldShot = step.targetPointId;
            holdUntil = Time.time + holdAfterStep;
            Apply(heldShot);
        }

        void Update() => Refresh(false);

        /// <summary>按当前阶段 / 工具 / 进度决定取景；只有取景变化时才移动镜头，不打断玩家的手动旋转。</summary>
        public void Refresh(bool force)
        {
            if (session == null) return;
            // 工具动画进行中：保持当前取景，结束后再按规则转向（避免镜头在工具接触零件时移开）
            var anim = controller.ToolAnimator;
            if (anim != null && anim.Busy && session.Stage == RepairStage.Inspect)
            {
                heldShot ??= CurrentShotId;
                holdUntil = Mathf.Max(holdUntil, Time.time + 0.35f);
            }
            // 停留只在检查阶段有效：刚做完最后一步就进入决定阶段时，立刻回到整件物品
            if (heldShot != null && (Time.time >= holdUntil || session.Stage != RepairStage.Inspect)) heldShot = null;
            string desired = heldShot ?? DesiredShot();
            if (force || desired != CurrentShotId) Apply(desired);
        }

        /// <summary>不含停留：根据阶段和当前工具应该看的取景。</summary>
        public string DesiredShot()
        {
            if (session.Stage != RepairStage.Inspect || session.CurrentCase == null) return Overview;
            var tool = controller.ActiveTool;
            if (!tool.HasValue) return Overview;
            var next = NextTarget(tool.Value);
            // 这件工具已经没有要做的事：保持当前取景
            if (next == null) return CurrentShotId;
            return FindShot(next) != null ? next : Overview;
        }

        /// <summary>
        /// 这件工具下一步要处理的零件：优先“已经可以做、且会解锁线索”的步骤（主线节奏：限力器 → 日志），
        /// 其次任何可以做的步骤，最后是第一个还没做的步骤。
        /// </summary>
        public string NextTarget(RepairActionType tool)
        {
            RepairStepDefinition firstPending = null, firstReady = null;
            foreach (var s in session.CurrentCase.repairSteps)
            {
                if (s == null || s.action != tool || session.IsStepDone(s.stepId)) continue;
                if (firstPending == null) firstPending = s;
                if (!IsReady(s)) continue;
                if (!string.IsNullOrEmpty(s.unlocksClueId) && !session.HasClue(s.unlocksClueId)) return s.targetPointId;
                if (firstReady == null) firstReady = s;
            }
            return (firstReady ?? firstPending)?.targetPointId;
        }

        bool IsReady(RepairStepDefinition s)
        {
            foreach (var r in s.requiredSteps) if (!session.IsStepDone(r)) return false;
            foreach (var r in s.requiredClues) if (!session.HasClue(r)) return false;
            return true;
        }

        public Shot FindShot(string pointId)
        {
            if (string.IsNullOrEmpty(pointId)) return null;
            foreach (var s in shots) if (s != null && s.pointId == pointId) return s;
            return null;
        }

        void Apply(string shotId, bool immediate = false)
        {
            CurrentShotId = shotId;
            var insp = controller.Inspector;
            var shot = FindShot(shotId);
            if (shot == null || insp.CurrentItem == null || !TryFrameBounds(shot, out var bounds))
            {
                ApplyOverview(insp, immediate);
                return;
            }
            insp.Focus(bounds, shot.yaw, shot.pitch, shot.fill, shot.minRadius, immediate);
        }

        void ApplyOverview(ItemInspector insp, bool immediate)
        {
            CurrentShotId = Overview;
            if (overviewFill <= 0f || insp.CurrentItem == null) { insp.ClearFocus(immediate); return; }
            // 整件物品：以物品中心（锚点）为焦点，只把距离拉近到细长物品也能占满画面
            var b = new Bounds(insp.CurrentItem.transform.parent.position, Vector3.one * (insp.ItemRadius * 2f / Mathf.Sqrt(3f)));
            insp.Focus(b, 0f, 0f, overviewFill, 0f, immediate);
        }

        /// <summary>取景部位的包围盒：只算当前可见的渲染器；零件已被移走（在零件盘上）时仍以它当前的位置为准。</summary>
        public bool TryFrameBounds(Shot shot, out Bounds bounds)
        {
            bounds = default;
            bool has = false;
            var ids = shot.frameIds != null && shot.frameIds.Length > 0 ? shot.frameIds : new[] { shot.pointId };
            foreach (var id in ids)
            {
                if (!controller.TryGetPoint(id, out var point)) continue;
                foreach (var r in point.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled) continue;
                    if (!has) { bounds = r.bounds; has = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }
            return has;
        }
    }
}
