using System;
using System.Collections.Generic;
using BorderRepair.Core;
using BorderRepair.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BorderRepair.UI
{
    /// <summary>维修台 UI：只负责显示和把按钮点击转成事件，不包含流程判断。</summary>
    public class RepairUIView : MonoBehaviour
    {
        [Header("顶栏")]
        [SerializeField] Text caseHeaderText;
        [SerializeField] Text stageText;
        [SerializeField] Text timerText;

        [Header("顾客说明 / 扫描记录")]
        [SerializeField] Text customerText;
        [SerializeField] Text scanStatusText;
        [SerializeField] Text findingsText;

        [Header("底部操作")]
        [SerializeField] Text hintText;
        [SerializeField] Button acceptButton;
        [SerializeField] Button scanButton;
        [SerializeField] Text scanButtonLabel;
        [SerializeField] Button diagnoseButton;

        [Header("扫描覆盖层")]
        [SerializeField] GameObject scanOverlay;

        [Header("反馈")]
        [SerializeField] GameObject feedbackRoot;
        [SerializeField] Image feedbackBackground;
        [SerializeField] Text feedbackText;
        [SerializeField] float feedbackSeconds = 3.5f;

        [Header("诊断")]
        [SerializeField] GameObject diagnosisPanel;
        [SerializeField] RectTransform diagnosisOptionContainer;
        [SerializeField] Button diagnosisOptionTemplate;
        [SerializeField] Button backToInspectButton;

        [Header("决定")]
        [SerializeField] GameObject decisionPanel;
        [SerializeField] Text decisionInfoText;
        [SerializeField] Button repairButton;
        [SerializeField] Button replaceButton;
        [SerializeField] Button refuseButton;

        [Header("结果")]
        [SerializeField] GameObject resultPanel;
        [SerializeField] Text resultTitleText;
        [SerializeField] Text resultBodyText;
        [SerializeField] Button nextButton;
        [SerializeField] Text nextButtonLabel;

        [Header("营业总结")]
        [SerializeField] GameObject summaryPanel;
        [SerializeField] Text summaryText;
        [SerializeField] Button restartButton;

        public event Action AcceptClicked;
        public event Action ScanToggleClicked;
        public event Action DiagnoseClicked;
        public event Action BackToInspectClicked;
        public event Action<string> DiagnosisChosen;
        public event Action<RepairDecision> DecisionChosen;
        public event Action NextClicked;
        public event Action RestartClicked;
        /// <summary>叙事案件：选择了某个结局（endingId）。</summary>
        public event Action<string> EndingChosen;

        static readonly string[] StageNames = { "接收", "检查", "诊断", "决定", "结果" };

        readonly List<Button> optionButtons = new List<Button>();
        readonly List<Button> endingButtons = new List<Button>();
        float feedbackHideTime;
        string hoverLabel;
        bool scanActive;
        int foundCount, requiredCount;
        string progressLabel = "关键异常";

        public string LastFeedback { get; private set; }

        void Awake()
        {
            var fallback = caseHeaderText != null ? caseHeaderText.font : null;
            var font = UIFontProvider.GetCjkFont(fallback);
            if (font != null)
                foreach (var t in GetComponentsInChildren<Text>(true)) t.font = font;

            Bind(acceptButton, () => AcceptClicked?.Invoke());
            Bind(scanButton, () => ScanToggleClicked?.Invoke());
            Bind(diagnoseButton, () => DiagnoseClicked?.Invoke());
            Bind(backToInspectButton, () => BackToInspectClicked?.Invoke());
            Bind(repairButton, () => DecisionChosen?.Invoke(RepairDecision.Repair));
            Bind(replaceButton, () => DecisionChosen?.Invoke(RepairDecision.RecommendReplacement));
            Bind(refuseButton, () => DecisionChosen?.Invoke(RepairDecision.Refuse));
            Bind(nextButton, () => NextClicked?.Invoke());
            Bind(restartButton, () => RestartClicked?.Invoke());

            if (diagnosisOptionTemplate != null) diagnosisOptionTemplate.gameObject.SetActive(false);
            if (feedbackRoot != null) feedbackRoot.SetActive(false);
            if (scanOverlay != null) scanOverlay.SetActive(false);
        }

        static void Bind(Button button, Action action)
        {
            if (button == null) return;
            button.onClick.AddListener(() =>
            {
                // 点击后清除选中，避免键盘提交键重复触发按钮。
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                action();
            });
        }

        void Update()
        {
            if (feedbackRoot != null && feedbackRoot.activeSelf && Time.unscaledTime > feedbackHideTime)
                feedbackRoot.SetActive(false);
        }

        // ---------- 刷新 ----------

        public void SetCase(RepairCaseData data, int index, int total, string shiftTitle)
        {
            LastFeedback = null;
            if (feedbackRoot != null) feedbackRoot.SetActive(false);
            caseHeaderText.text = $"{shiftTitle} · 第 {index + 1}/{total} 件：{data.itemName}";
            string note = string.IsNullOrEmpty(data.intakeNote) ? string.Empty : $"\n\n<color=#9FB3C8>工单备注：{data.intakeNote}</color>";
            customerText.text = $"<b>{data.customerName}</b>\n\n“{data.customerStatement}”{note}";
        }

        public void SetDiagnosisOptions(IList<DiagnosisOption> options)
        {
            foreach (var b in optionButtons) if (b != null) Destroy(b.gameObject);
            optionButtons.Clear();
            if (diagnosisOptionTemplate == null) return;

            foreach (var option in options)
            {
                var button = Instantiate(diagnosisOptionTemplate, diagnosisOptionContainer);
                button.gameObject.SetActive(true);
                button.name = "Option_" + option.optionId;
                var label = button.GetComponentInChildren<Text>(true);
                if (label != null) label.text = option.label;
                string id = option.optionId;
                Bind(button, () => DiagnosisChosen?.Invoke(id));
                optionButtons.Add(button);
            }
        }

        public void ShowStage(RepairStage stage)
        {
            stageText.text = BuildStageLine(stage);
            hintText.text = HintFor(stage);

            acceptButton.gameObject.SetActive(stage == RepairStage.Intake);
            scanButton.gameObject.SetActive(stage == RepairStage.Inspect);
            diagnoseButton.gameObject.SetActive(stage == RepairStage.Inspect);

            diagnosisPanel.SetActive(stage == RepairStage.Diagnose);
            decisionPanel.SetActive(stage == RepairStage.Decide);
            resultPanel.SetActive(stage == RepairStage.Result);
            summaryPanel.SetActive(stage == RepairStage.Summary);
            RefreshScanStatus(stage == RepairStage.Inspect);
        }

        public void SetFindings(IList<string> lines, int found, int required, string label = "关键异常")
        {
            foundCount = found;
            requiredCount = required;
            progressLabel = label;
            findingsText.text = lines.Count == 0
                ? "<color=#8A96A3>尚无扫描记录。\n接收物品后开启扫描模式，点击可疑部位。</color>"
                : string.Join("\n\n", lines);
            RefreshScanStatus(scanButton.gameObject.activeSelf);
        }

        public void SetScanState(bool active)
        {
            scanActive = active;
            scanOverlay.SetActive(active);
            scanButtonLabel.text = active ? "关闭扫描 [空格]" : "扫描模式 [空格]";
            RefreshScanStatus(scanButton.gameObject.activeSelf);
        }

        public void SetHover(string label)
        {
            hoverLabel = label;
            RefreshScanStatus(scanButton.gameObject.activeSelf);
        }

        void RefreshScanStatus(bool inspecting)
        {
            string state = scanActive ? "<color=#5FE0FF>扫描：开启</color>" : "<color=#8A96A3>扫描：关闭</color>";
            string progress = $"{progressLabel} {foundCount}/{requiredCount}";
            string hover = scanActive && inspecting
                ? (string.IsNullOrEmpty(hoverLabel) ? "\n瞄准：—" : $"\n瞄准：<color=#5FE0FF>{hoverLabel}</color>（单击扫描）")
                : string.Empty;
            scanStatusText.text = $"{state}　{progress}{hover}";
        }

        public void SetTimer(float elapsedSeconds, float targetSeconds)
        {
            string color = elapsedSeconds > targetSeconds ? "#FF8A65" : "#DDE6EE";
            timerText.text = $"营业时间 <color={color}>{FormatTime(elapsedSeconds)}</color> / 目标 {FormatTime(targetSeconds)}";
        }

        public void SetDecisionInfo(string text) => decisionInfoText.text = text;

        public void ShowResult(bool correct, string title, string body, bool isLastCase)
        {
            resultTitleText.text = correct ? $"<color=#8FE08F>判断正确 · {title}</color>" : $"<color=#FF8A65>判断失误 · {title}</color>";
            resultBodyText.text = body;
            nextButtonLabel.text = isLastCase ? "查看营业总结" : "下一件";
        }

        public void ShowSummary(string text) => summaryText.text = text;

        // ---------- 叙事案件（默认营业流程不会调用） ----------

        /// <summary>
        /// 叙事案件：用数据驱动的结局按钮替换“维修 / 建议更换 / 拒绝处理”三个固定按钮。
        /// 传 null 或空列表时恢复固定按钮（判断类案件）。
        /// </summary>
        public void SetEndingOptions(IList<NarrativeEnding> endings)
        {
            foreach (var b in endingButtons) if (b != null) Destroy(b.gameObject);
            endingButtons.Clear();
            bool narrative = endings != null && endings.Count > 0;
            repairButton.gameObject.SetActive(!narrative);
            replaceButton.gameObject.SetActive(!narrative);
            refuseButton.gameObject.SetActive(!narrative);
            if (!narrative || diagnosisOptionTemplate == null) return;

            var container = repairButton.transform.parent;
            foreach (var ending in endings)
            {
                var button = Instantiate(diagnosisOptionTemplate, container);
                button.gameObject.SetActive(true);
                button.name = "Ending_" + ending.endingId;
                var label = button.GetComponentInChildren<Text>(true);
                if (label != null) { label.text = ending.label; label.fontSize = 18; }
                var layout = button.GetComponent<LayoutElement>();
                if (layout != null) layout.preferredHeight = 48;
                string id = ending.endingId;
                Bind(button, () => EndingChosen?.Invoke(id));
                endingButtons.Add(button);
            }
        }

        public int EndingButtonCount => endingButtons.Count;

        /// <summary>叙事案件的结果页：不显示“判断正确 / 失误”。</summary>
        public void ShowNarrativeResult(string title, string body, bool isLastCase)
        {
            resultTitleText.text = $"<color=#FFB347>结局 · {title}</color>";
            resultBodyText.text = body;
            nextButtonLabel.text = isLastCase ? "查看营业总结" : "下一件";
        }

        public string ResultTitle => resultTitleText != null ? resultTitleText.text : string.Empty;
        public string SummaryText => summaryText != null ? summaryText.text : string.Empty;

        /// <summary>覆盖当前阶段的底部提示（在 ShowStage 之后调用）。</summary>
        public void SetHint(string text) => hintText.text = text;

        public void SetDiagnoseButtonLabel(string text)
        {
            var label = diagnoseButton.GetComponentInChildren<Text>(true);
            if (label != null) label.text = text;
        }

        public void ShowFeedback(string message, FeedbackKind kind)
        {
            LastFeedback = message;
            if (feedbackRoot == null) return;
            feedbackText.text = message;
            feedbackBackground.color = ColorFor(kind);
            feedbackRoot.SetActive(true);
            feedbackHideTime = Time.unscaledTime + feedbackSeconds;
        }

        // ---------- 文本 ----------

        static string BuildStageLine(RepairStage stage)
        {
            if (stage == RepairStage.Summary) return "<color=#FFB347><b>营业总结</b></color>";
            int current = (int)stage;
            var parts = new string[StageNames.Length];
            for (int i = 0; i < StageNames.Length; i++)
            {
                string label = $"{i + 1}.{StageNames[i]}";
                if (i < current) parts[i] = $"<color=#8FD18F>{label}</color>";
                else if (i == current) parts[i] = $"<color=#FFB347><b>{label}</b></color>";
                else parts[i] = $"<color=#66717C>{label}</color>";
            }
            return string.Join("  ›  ", parts);
        }

        static string HintFor(RepairStage stage)
        {
            switch (stage)
            {
                case RepairStage.Intake: return "阅读左侧顾客说明，然后点击【接收物品】。";
                case RepairStage.Inspect: return "拖动鼠标旋转 · 滚轮缩放 · R 复位视角 · 空格切换扫描\n扫描模式下单击部位进行扫描，找齐关键异常后点击【开始诊断】";
                case RepairStage.Diagnose: return "根据扫描记录，在右侧选择故障诊断。可以【返回检查】继续扫描。";
                case RepairStage.Decide: return "选择处理方式：维修 / 建议更换 / 拒绝处理。注意维修费与顾客上限。";
                case RepairStage.Result: return "查看结果与理由，然后继续。";
                case RepairStage.Summary: return "本次营业结束，点击【重新开始】再来一轮。";
                default: return string.Empty;
            }
        }

        static Color ColorFor(FeedbackKind kind)
        {
            switch (kind)
            {
                case FeedbackKind.Success: return new Color(0.16f, 0.42f, 0.22f, 0.95f);
                case FeedbackKind.Warning: return new Color(0.55f, 0.38f, 0.08f, 0.95f);
                case FeedbackKind.Error: return new Color(0.6f, 0.18f, 0.14f, 0.95f);
                default: return new Color(0.14f, 0.2f, 0.28f, 0.95f);
            }
        }

        public static string FormatTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }
    }
}
