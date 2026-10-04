using BorderRepair.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.TwoNight
{
    public static class TwoNightScenes
    {
        public const string Menu = "TwoNight_Menu";
        public const string Counter = "Night1_Counter";
        public const string Robot = "Unit07_Night";
    }

    /// <summary>
    /// 第一晚柜台（收藏家的通讯器）。复用原型的 RepairStationController / RepairSession，不改它的流程：
    /// - 接收前：收藏家几句短对话（文本）；
    /// - 结单（营业总结那一刻）：判定正确的维修才记账（TwoNightRun.SettleCommunicator，id 只记一次），弹出账本；
    ///   判定错误 / 拒绝：不入账，提示后可以“重新处理这台通讯器”（RepairSession.Restart，原型自带的重开）；
    /// - 确认账本 → 七号场景（端盘失衡）。
    /// 本组件只订阅本场景的会话事件，场景卸载时一起销毁，不跨场景存活。
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class TwoNightCounterDirector : MonoBehaviour
    {
        [SerializeField] RepairStationController station;
        [SerializeField] TwoNightEconomy economy;
        [SerializeField] Font font;

        public TwoNightState State => TwoNightRun.Current;
        public bool LedgerVisible => ledger != null && ledger.activeSelf;
        public bool RetryVisible => retry != null && retry.activeSelf;
        public bool DialogueVisible => dialogue != null && dialogue.activeSelf;
        public Button ConfirmButton { get; private set; }
        public Button RetryButton { get; private set; }
        public Button DialogueButton { get; private set; }
        public Text LedgerText { get; private set; }
        public int SettleAttempts { get; private set; }

        GameObject ledger, retry, dialogue;
        Text dialogueText, retryText;
        int dialogueIndex;
        bool leaving;

        static readonly string[] Lines =
        {
            "收藏家：晚上好。这台旧通讯器开得了机，就是收不到信号。",
            "收藏家：不是急件，修好就行。",
            "收藏家：里面的通话记录别清掉，那是我留着的。",
        };

        public void Configure(RepairStationController s, TwoNightEconomy e, Font f) { station = s; economy = e; font = f; }

        void Awake()
        {
            if (TwoNightRun.Current == null || TwoNightRun.Current.phase == TwoNightPhase.None) TwoNightRun.NewGame(economy);
        }

        void Start()
        {
            BuildUi();
            // 原型界面的文字换成打包的中文字体（原型自己用的是系统字体）；只在这个场景里换，不改原型
            if (station != null && station.View != null && font != null)
                foreach (var t in station.View.GetComponentsInChildren<Text>(true)) t.font = font;
            if (station == null || station.Session == null) { Debug.LogError("[TwoNight] 柜台场景缺少 RepairStationController", this); return; }
            station.Session.StageChanged += OnStage;
            station.Session.CaseStarted += OnCaseStarted;
            var s = TwoNightRun.Current;
            if (s.phase == TwoNightPhase.Night1Ledger) ShowLedger();
            else if (s.phase == TwoNightPhase.Night1Counter) ShowDialogue();
        }

        void OnDestroy()
        {
            if (station != null && station.Session != null)
            {
                station.Session.StageChanged -= OnStage;
                station.Session.CaseStarted -= OnCaseStarted;
            }
        }

        void OnCaseStarted(Data.RepairCaseData data, int index) { if (TwoNightRun.Current.phase == TwoNightPhase.Night1Counter && dialogue != null) ShowDialogue(); }

        void OnStage(RepairStage stage)
        {
            if (stage != RepairStage.Summary) return;
            var rec = station.Session.CurrentRecord;
            bool correct = rec != null && rec.DecisionCorrect;
            SettleAttempts++;
            var s = TwoNightRun.Current;
            if (correct && TwoNightRun.SettleCommunicator(s, true, rec.Case != null ? rec.Case.caseId : "", economy)) { ShowLedger(); return; }
            if (s.phase == TwoNightPhase.Night1Ledger || s.phase >= TwoNightPhase.Night1Incident) { ShowLedger(); return; }   // 已结过：只显示，不再记账
            retryText.text = $"这一单没处理好：<b>{(rec != null && rec.Outcome != null ? rec.Outcome.resultTitle : "没有完成")}</b>。\n没有有效结单，不收钱，也不记账。\n\n收藏家还在等。重新检查这台通讯器。";
            retry.SetActive(true);
        }

        void ShowDialogue()
        {
            dialogueIndex = 0;
            dialogueText.text = Lines[0];
            dialogue.SetActive(true);
        }

        void NextLine()
        {
            dialogueIndex++;
            if (dialogueIndex >= Lines.Length) { dialogue.SetActive(false); return; }
            dialogueText.text = Lines[dialogueIndex];
        }

        void ShowLedger()
        {
            var s = TwoNightRun.Current;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"开店现金　　　　　　　{TwoNightUi.Money(s.startingCash)}");
            foreach (var t in s.transactions)
                sb.AppendLine($"{t.label}　　{(t.amount >= 0 ? "+" : "−")}{TwoNightUi.Money(Mathf.Abs(t.amount))}");
            sb.AppendLine("――――――――――――――――");
            sb.AppendLine($"<b>现金　　　　　　　　　{TwoNightUi.Money(s.Cash)}</b>");
            sb.AppendLine();
            sb.AppendLine($"房租 {TwoNightUi.Money(s.rent)}，第 {s.rentDueNight} 晚打烊前到期（今晚不扣）。");
            sb.Append($"<color=#F2B95A>还差 {TwoNightUi.Money(s.RentShortfall)}。</color>");
            LedgerText.text = sb.ToString();
            ConfirmButton.interactable = s.phase == TwoNightPhase.Night1Ledger;
            retry.SetActive(false);
            ledger.SetActive(true);
        }

        public void ConfirmLedger()
        {
            if (leaving) return;
            if (!TwoNightRun.ConfirmLedger(TwoNightRun.Current) && TwoNightRun.Current.phase != TwoNightPhase.Night1Incident) return;
            leaving = true;
            ConfirmButton.interactable = false;
            SceneManager.LoadScene(TwoNightScenes.Robot);
        }

        public void RetryCommunicator()
        {
            retry.SetActive(false);
            station.Session.Restart();
        }

        void BuildUi()
        {
            var root = new GameObject("TwoNightCounterUI");
            root.transform.SetParent(transform, false);
            TwoNightUi.Canvas(root, 50);

            dialogue = TwoNightUi.Panel(root.transform, "Dialogue", new Vector2(0.5f, 0f), new Vector2(-430, 260), new Vector2(860, 150)).gameObject;
            dialogueText = TwoNightUi.Label(dialogue.transform, "Line", "", font, 22, new Vector2(24, -20), new Vector2(812, 80), TwoNightUi.TextMain);
            DialogueButton = TwoNightUi.Button(dialogue.transform, "Next", "继续", font, new Vector2(720, -98), new Vector2(120, 40), NextLine, true);
            dialogue.SetActive(false);

            // 账本 / 重做面板放在全屏遮罩上：盖住原型自己的“营业总结”（它的“重新开始”按钮这时不能点）
            ledger = TwoNightUi.Shade(root.transform, "LedgerShade").gameObject;
            var ledgerPanel = TwoNightUi.Panel(ledger.transform, "Ledger", new Vector2(0.5f, 0.5f), new Vector2(-360, 260), new Vector2(720, 520), new Color(0.07f, 0.08f, 0.09f, 1f));
            TwoNightUi.Label(ledgerPanel, "Title", "第一晚 · 账本", font, 28, new Vector2(30, -24), new Vector2(660, 40), TwoNightUi.TextMain, TextAnchor.UpperLeft, FontStyle.Bold);
            LedgerText = TwoNightUi.Label(ledgerPanel, "Body", "", font, 21, new Vector2(30, -84), new Vector2(660, 330), TwoNightUi.TextMain);
            ConfirmButton = TwoNightUi.Button(ledgerPanel, "Confirm", "确认账本，打烊", font, new Vector2(470, -450), new Vector2(220, 46), ConfirmLedger, true);
            ledger.SetActive(false);

            retry = TwoNightUi.Shade(root.transform, "RetryShade").gameObject;
            var retryPanel = TwoNightUi.Panel(retry.transform, "Retry", new Vector2(0.5f, 0.5f), new Vector2(-360, 180), new Vector2(720, 330), new Color(0.07f, 0.08f, 0.09f, 1f));
            retryText = TwoNightUi.Label(retryPanel, "Body", "", font, 21, new Vector2(30, -28), new Vector2(660, 220), TwoNightUi.TextMain);
            RetryButton = TwoNightUi.Button(retryPanel, "Retry", "重新处理这台通讯器", font, new Vector2(430, -262), new Vector2(260, 46), RetryCommunicator, true);
            retry.SetActive(false);
        }
    }
}
