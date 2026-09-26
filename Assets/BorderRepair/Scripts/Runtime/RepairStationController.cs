using System.Collections.Generic;
using System.Text;
using BorderRepair.Controls;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Inspection;
using BorderRepair.UI;
using UnityEngine;

namespace BorderRepair
{
    /// <summary>把 RepairSession（流程）、ItemInspector/ItemScanner（交互）和 RepairUIView（显示）连接起来。</summary>
    public class RepairStationController : MonoBehaviour
    {
        [SerializeField] RepairShiftData shift;
        [SerializeField] ItemInspector inspector;
        [SerializeField] ItemScanner scanner;
        [SerializeField] RepairUIView view;
        [Tooltip("可选：维修工具栏。只有带维修步骤的案件（叙事竖切场景）需要；默认营业场景留空。")]
        [SerializeField] RepairToolbarView toolbar;
        [Tooltip("可选：工具动画（手套手拿工具进入、对准、接触、退出）。只放在叙事场景里；为空时单击零件立即执行，与原来相同。")]
        [SerializeField] Tools.RepairToolAnimator toolAnimator;

        public RepairSession Session { get; private set; }
        public Tools.RepairToolAnimator ToolAnimator => toolAnimator;
        /// <summary>工具动画进行中：不接受新的零件单击、换工具和扫描切换，避免同一步被重复触发。</summary>
        public bool InputLocked => toolAnimator != null && toolAnimator.Busy;
        public ItemInspector Inspector => inspector;
        public ItemScanner Scanner => scanner;
        public RepairUIView View => view;
        public RepairToolbarView Toolbar => toolbar;

        /// <summary>当前选中的维修工具；null 表示空手（单击部位按扫描处理）。</summary>
        public RepairActionType? ActiveTool { get; private set; }

        readonly List<string> findingLines = new List<string>();
        readonly Dictionary<string, InspectionPoint> points = new Dictionary<string, InspectionPoint>();
        readonly Dictionary<string, RepairPart> parts = new Dictionary<string, RepairPart>();

        public bool TryGetPoint(string pointId, out InspectionPoint point) => points.TryGetValue(pointId, out point);
        public bool TryGetPart(string pointId, out RepairPart part) => parts.TryGetValue(pointId, out part);

        void Awake()
        {
            if (shift == null || inspector == null || scanner == null || view == null)
            {
                Debug.LogError("[BorderRepair] RepairStationController 缺少引用，请用菜单 Border Repair > Rebuild Prototype 重新生成场景。", this);
                enabled = false;
                return;
            }

            var errors = new List<string>();
            foreach (var c in shift.cases)
            {
                if (c == null) errors.Add("营业数据中有空的案例引用");
                else c.Validate(errors);
            }
            foreach (var e in errors) Debug.LogError("[BorderRepair] 案例数据错误：" + e, this);

            Session = new RepairSession(shift, () => Time.time);
            Session.CaseStarted += OnCaseStarted;
            Session.StageChanged += OnStageChanged;
            Session.Feedback += view.ShowFeedback;
            Session.FindingAdded += OnFindingAdded;
            Session.ScanModeChanged += OnScanModeChanged;
            Session.DecisionResolved += OnDecisionResolved;
            Session.PartStateChanged += OnPartStateChanged;
            Session.StepPerformed += OnStepPerformed;
            Session.ClueUnlocked += OnClueUnlocked;
            Session.EndingResolved += OnEndingResolved;

            view.EndingChosen += id => Session.SubmitEnding(id);
            if (toolbar != null) toolbar.ToolSelected += SelectTool;

            view.AcceptClicked += () => Session.AcceptItem();
            view.ScanToggleClicked += () => Session.ToggleScanMode();
            view.DiagnoseClicked += () => Session.TryBeginDiagnosis();
            view.BackToInspectClicked += () => Session.ReturnToInspection();
            view.DiagnosisChosen += id => Session.SubmitDiagnosis(id);
            view.DecisionChosen += d => Session.SubmitDecision(d);
            view.NextClicked += () => Session.NextCase();
            view.RestartClicked += () => Session.Restart();

            scanner.PointClicked += OnPointClicked;
            scanner.ItemSurfaceClicked += OnSurfaceClicked;
            scanner.MissedItem += () => view.ShowFeedback("没有对准物品。", FeedbackKind.Info);
            scanner.HoverChanged += OnHoverChanged;
        }

        void Start()
        {
            if (Session != null) Session.StartShift();
        }

        void Update()
        {
            if (Session == null) return;
            if (RepairInput.ToggleScanPressed && Session.Stage != RepairStage.Summary && !InputLocked) Session.ToggleScanMode();
            if (RepairInput.ResetViewPressed && inspector.InteractionEnabled) inspector.ResetView();
            view.SetTimer(Session.ShiftDuration, shift.targetDurationSeconds);
        }

        /// <summary>按 pointId 扫描当前物品上的检查点（与鼠标单击走同一流程，供测试和调试使用）。</summary>
        public ScanOutcome ScanPoint(string pointId)
        {
            return Session.ReportScan(points.ContainsKey(pointId) ? pointId : null);
        }

        /// <summary>选择维修工具（null = 空手）。选工具时关闭扫描模式；两者互斥。</summary>
        public void SelectTool(RepairActionType? tool)
        {
            if (Session == null || InputLocked) return;
            if (tool.HasValue && Session.Stage != RepairStage.Inspect)
            {
                view.ShowFeedback("接收物品后才能动手维修。", FeedbackKind.Warning);
                return;
            }
            ActiveTool = tool;
            if (tool.HasValue) Session.SetScanMode(false);
            scanner.SetScanMode(Session.ScanModeActive || ActiveTool.HasValue);
            RefreshToolbar();
        }

        /// <summary>用当前工具（或指定动作）处理某个部位，与鼠标单击走同一流程（供测试和以后的 VR 输入使用）。</summary>
        public ActionOutcome UseTool(RepairActionType action, string pointId) => Session.PerformAction(action, pointId);

        void OnPointClicked(InspectionPoint point) => ClickPoint(point.PointId);

        /// <summary>
        /// 与鼠标单击零件相同的处理（供测试调用）：拿着工具时，有工具动画就先播放动画、在接触时刻执行步骤；
        /// 动作不成立（工具不对、顺序不对）时不播放动画，立即给出原来的反馈。空手时按扫描处理。
        /// 返回值：true = 已开始工具动画。
        /// </summary>
        public bool ClickPoint(string pointId)
        {
            if (Session == null || InputLocked) return false;
            if (!ActiveTool.HasValue)
            {
                Session.ReportScan(pointId);
                return false;
            }
            if (toolAnimator != null && toolAnimator.TryBegin(ActiveTool.Value, pointId)) return true;
            Session.PerformAction(ActiveTool.Value, pointId);
            return false;
        }

        void OnSurfaceClicked()
        {
            if (ActiveTool.HasValue) view.ShowFeedback("这里没有可以用工具处理的零件。", FeedbackKind.Info);
            else Session.ReportScan(null);
        }

        void RefreshToolbar()
        {
            scanner.ShowScannedTint = !ActiveTool.HasValue;
            if (toolbar == null || Session == null) return;
            var data = Session.CurrentCase;
            bool hasSteps = data != null && data.HasRepairSteps;
            toolbar.SetVisible(hasSteps);
            toolbar.SetInteractable(hasSteps && Session.Stage == RepairStage.Inspect);
            toolbar.SetActiveTool(ActiveTool);
            if (hasSteps && Session.Stage == RepairStage.Inspect)
                toolbar.SetStatus(ActiveTool.HasValue
                    ? $"当前工具：<b>{RepairActionText.Label(ActiveTool.Value)}</b>\n单击零件使用。"
                    : "空手：单击部位按扫描处理（需开启扫描模式）。");
        }

        // ---------- 流程事件 ----------

        void OnCaseStarted(RepairCaseData data, int index)
        {
            findingLines.Clear();
            points.Clear();

            var item = inspector.Mount(data.itemPrefab);
            if (item != null)
            {
                foreach (var p in item.GetComponentsInChildren<InspectionPoint>(true))
                {
                    if (string.IsNullOrEmpty(p.PointId)) continue;
                    if (points.ContainsKey(p.PointId)) Debug.LogWarning($"[BorderRepair] {data.itemPrefab.name} 中 pointId 重复：{p.PointId}", p);
                    points[p.PointId] = p;
                }
            }
            foreach (var info in data.inspectionPoints)
                if (!points.ContainsKey(info.pointId))
                    Debug.LogError($"[BorderRepair] 案例 {data.caseId} 的检查点 '{info.pointId}' 在 prefab 中找不到对应的 InspectionPoint。", this);

            parts.Clear();
            if (item != null)
                foreach (var part in item.GetComponentsInChildren<RepairPart>(true))
                    if (!string.IsNullOrEmpty(part.PartId)) parts[part.PartId] = part;
            foreach (var step in data.repairSteps)
                if (step != null && !parts.ContainsKey(step.targetPointId))
                    Debug.LogError($"[BorderRepair] 案例 {data.caseId} 的步骤 '{step.stepId}' 的目标 '{step.targetPointId}' 在 prefab 中没有 RepairPart。", this);

            ActiveTool = null;
            scanner.ResetForItem();
            view.SetCase(data, index, Session.CaseCount, shift.shiftTitle);
            view.SetDiagnosisOptions(data.diagnosisOptions);
            view.SetEndingOptions(data.IsNarrative ? data.endings : null);
            view.SetDiagnoseButtonLabel(data.IsNarrative ? "整理结论" : "开始诊断");
            RefreshFindings();
            RefreshToolbar();
        }

        void OnStageChanged(RepairStage stage)
        {
            inspector.InteractionEnabled = stage == RepairStage.Inspect || stage == RepairStage.Diagnose
                                           || stage == RepairStage.Decide || stage == RepairStage.Result;
            scanner.ScanAllowed = stage == RepairStage.Inspect;
            if (stage != RepairStage.Inspect && ActiveTool.HasValue)
            {
                ActiveTool = null;
                scanner.SetScanMode(Session.ScanModeActive);
            }

            var data = Session.CurrentCase;
            bool narrative = data != null && data.IsNarrative;
            if (stage == RepairStage.Decide)
                view.SetDecisionInfo(narrative ? BuildNarrativeDecisionInfo() : BuildDecisionInfo(data));
            if (stage == RepairStage.Summary) view.ShowSummary(BuildSummary());
            view.ShowStage(stage);

            if (narrative && stage == RepairStage.Inspect)
                view.SetHint("左侧选择工具后单击零件；空手时开启扫描可查看外观\n找齐线索后点击【整理结论】");
            else if (narrative && stage == RepairStage.Decide)
                view.SetHint("这一单没有标准答案。选择一种处理方式，每一种都有代价。");
            RefreshToolbar();
        }

        void OnFindingAdded(InspectionPointInfo info)
        {
            if (points.TryGetValue(info.pointId, out var p))
            {
                // 关键异常 / 会解锁线索的部位标为“异常”；没有诊断高亮设置的场景里显示与“已扫描”相同
                if (info.requiredForDiagnosis || !string.IsNullOrEmpty(info.unlocksClueId)) scanner.MarkAnomaly(p);
                else scanner.MarkScanned(p);
            }
            if (Session.CurrentCase.IsNarrative)
            {
                // 叙事案件的扫描结果显示在工具栏状态里；右侧记录只列线索，避免文字溢出
                if (toolbar != null) toolbar.SetStatus($"<b>{info.displayName}</b>\n{info.finding}");
                return;
            }
            string tag = info.requiredForDiagnosis ? "<color=#FFB347>[异常]</color>" : "<color=#8FD18F>[正常]</color>";
            findingLines.Add($"{tag} <b>{info.displayName}</b>\n{info.finding}");
            RefreshFindings();
        }

        void RefreshFindings()
        {
            var data = Session.CurrentCase;
            if (data != null && data.IsNarrative)
                view.SetFindings(findingLines, Session.FoundRequiredClueCount, Session.RequiredClueCount, "线索");
            else
                view.SetFindings(findingLines, Session.FoundRequiredCount, Session.RequiredFindingCount);
        }

        void OnScanModeChanged(bool active)
        {
            if (active) ActiveTool = null;
            scanner.SetScanMode(active || ActiveTool.HasValue);
            view.SetScanState(active);
            RefreshToolbar();
        }

        void OnPartStateChanged(string pointId, RepairPartState state)
        {
            if (parts.TryGetValue(pointId, out var part)) part.SetState(state);
        }

        void OnStepPerformed(RepairStepDefinition step)
        {
            if (toolbar != null) toolbar.SetStatus($"<b>{step.label}</b>\n{step.resultText}");
        }

        void OnClueUnlocked(ClueDefinition clue)
        {
            int n = Session.CurrentRecord.Clues.Count;
            findingLines.Add($"<color=#FFB347>[线索 {n}]</color> <b>{clue.title}</b>\n{clue.text}");
            RefreshFindings();
        }

        void OnEndingResolved(NarrativeEnding ending)
        {
            var body = new StringBuilder();
            body.AppendLine(ending.narrative);
            if (ending.costs.Count > 0)
            {
                body.AppendLine();
                body.AppendLine("<color=#FFB347>代价</color>");
                // 圆点后不留空格：旧版 Text 只在空格处换行，留空格会让圆点单独占一行
                foreach (var c in ending.costs) body.AppendLine("·" + c);
            }
            view.ShowNarrativeResult(ending.resultTitle, body.ToString().TrimEnd(), Session.IsLastCase);
        }

        void OnHoverChanged(InspectionPoint point)
        {
            if (point == null) { view.SetHover(null); return; }
            var info = Session.CurrentCase.FindPoint(point.PointId);
            view.SetHover(info != null ? info.displayName : "未登记部位");
        }

        void OnDecisionResolved(DecisionOutcome outcome)
        {
            var data = Session.CurrentCase;
            if (outcome.performsPartReplacement && points.TryGetValue(data.replacementPointId, out var part))
                part.ApplyRepair();

            var record = Session.CurrentRecord;
            var body = new StringBuilder();
            body.AppendLine($"你的决定：{RepairDecisionText.Label(outcome.decision)}");
            body.AppendLine();
            body.AppendLine(outcome.reason);
            if (outcome.performsPartReplacement)
            {
                var info = data.FindPoint(data.replacementPointId);
                body.AppendLine();
                body.AppendLine($"<color=#8FE08F>已更换部件：{(info != null ? info.displayName : data.replacementPointId)}（可旋转查看）</color>");
            }
            body.AppendLine();
            body.Append($"<color=#9FB3C8>诊断失误 {record.WrongDiagnosisCount} 次 · 用时 {RepairUIView.FormatTime(record.Duration)}</color>");
            view.ShowResult(outcome.isCorrect, outcome.resultTitle, body.ToString(), Session.IsLastCase);
        }

        // ---------- 文本 ----------

        static string BuildDecisionInfo(RepairCaseData data)
        {
            var sb = new StringBuilder();
            var diagnosis = data.CorrectDiagnosis;
            sb.AppendLine($"诊断结论：<b>{(diagnosis != null ? diagnosis.label : "-")}</b>");
            if (data.estimatedRepairCost > 0) sb.AppendLine($"预估维修费：{data.estimatedRepairCost} 信用点");
            if (data.repairCostLimit > 0)
            {
                bool over = data.estimatedRepairCost > data.repairCostLimit;
                sb.AppendLine($"顾客可接受上限：{(over ? "<color=#FF8A65>" : "")}{data.repairCostLimit} 信用点{(over ? "（已超出）</color>" : "")}");
            }
            sb.Append("\n<color=#9FB3C8>维修：本店修复（含换件）\n建议更换：不修，建议换新或回收\n拒绝处理：不接此单</color>");
            return sb.ToString();
        }

        string BuildNarrativeDecisionInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("你已经弄清楚：");
            foreach (var id in Session.CurrentRecord.Clues)
            {
                var clue = Session.CurrentCase.FindClue(id);
                if (clue != null) sb.AppendLine($"· {clue.title}");
            }
            sb.Append("\n<color=#9FB3C8>这一单没有标准答案。\n每一种处理方式都有人要付出代价。</color>");
            return sb.ToString();
        }

        string BuildSummary()
        {
            var s = Session;
            int judged = s.JudgedCaseCount;
            var sb = new StringBuilder();
            sb.AppendLine($"<size=34><b>{shift.shiftTitle} · 营业总结</b></size>");
            sb.AppendLine();
            // 叙事案件不计入“判断正确 X/Y”
            if (judged > 0)
                sb.AppendLine($"判断正确：<b>{s.CorrectDecisionCount} / {judged}</b>　　诊断失误：<b>{s.TotalWrongDiagnoses}</b> 次　　营业用时：<b>{RepairUIView.FormatTime(s.ShiftDuration)}</b>（目标 {RepairUIView.FormatTime(shift.targetDurationSeconds)}）");
            else
                sb.AppendLine($"营业用时：<b>{RepairUIView.FormatTime(s.ShiftDuration)}</b>（目标 {RepairUIView.FormatTime(shift.targetDurationSeconds)}）");
            sb.AppendLine();
            for (int i = 0; i < s.Records.Count; i++)
            {
                var r = s.Records[i];
                if (!r.IsJudged)
                {
                    string ending = r.Ending != null ? r.Ending.resultTitle : "未选择";
                    sb.AppendLine($"{i + 1}. <b>{r.Case.itemName}</b>　<color=#FFB347>叙事案件</color>　结局：{ending}　线索 {r.Clues.Count} 条　用时 {RepairUIView.FormatTime(r.Duration)}");
                    if (r.Ending != null && r.Ending.costs.Count > 0) sb.AppendLine($"    <color=#9FB3C8>{r.Ending.costs[0]}</color>");
                    continue;
                }
                string verdict = r.DecisionCorrect ? "<color=#8FE08F>正确</color>" : "<color=#FF8A65>失误</color>";
                string decision = r.Decision.HasValue ? RepairDecisionText.Label(r.Decision.Value) : "-";
                sb.AppendLine($"{i + 1}. <b>{r.Case.itemName}</b>　决定：{decision}（{verdict}）　诊断失误 {r.WrongDiagnosisCount} 次　用时 {RepairUIView.FormatTime(r.Duration)}");
                if (r.Outcome != null) sb.AppendLine($"    <color=#9FB3C8>{r.Outcome.resultTitle}</color>");
            }
            sb.AppendLine();
            string rating;
            if (judged == 0)
                rating = "这一单没有对错。它会跟着那只手，回到码头上。";
            else
                rating = s.CorrectDecisionCount == judged
                    ? (s.TotalWrongDiagnoses == 0 ? "判断干净利落，这个柜台交给你了。" : "判断都对了，诊断还可以更稳。")
                    : s.CorrectDecisionCount == 0 ? "今天的每一单都需要复盘。" : "有的单子处理得不错，失误的那几件值得复盘。";
            sb.Append($"<color=#FFB347>{rating}</color>");
            return sb.ToString();
        }
    }
}
