using System.Linq;
using BorderRepair.Cleaning;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.FirstOrder;
using BorderRepair.Narrative;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // Playtest keys, in the editor and development builds only (a release build never installs this; F1 is ClinicHelpRuntime, in every build):
    //   F4  cleans every dirty CleanableSurface, through its normal completion event
    //   F5  completes the current step of whatever is going on, one step per press
    // F5 never writes state itself. Every branch calls the entry point the player's own action reaches, and that entry
    // point still judges: bench commands go through RepairPropStationBridge and RepairPropFlow.CanExecute, surgery through
    // SurgeryProcedureRuntime.Run, trades through UnifiedClinicDirector.Interact. A refusal is shown as it is, so a broken
    // step stays visible instead of being papered over. Skipped: only the hand motion and the waiting.
    [AddComponentMenu("")]
    public sealed class ClinicDevKeysRuntime : MonoBehaviour
    {
        const float ToastSeconds = 3.5f;

        public static ClinicDevKeysRuntime Current { get; private set; }

        readonly struct Result
        {
            public readonly bool ok;
            public readonly string message;
            public Result(bool ok, string message) { this.ok = ok; this.message = message; }
        }

        Canvas canvas;
        GameObject toast;
        Text toastText;
        CanvasGroup toastGroup;
        AudioSource speaker;
        AudioClip denyClip, doneClip;
        float toastUntil;

        public string LastMessage { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (!Debug.isDebugBuild || UnityEngine.XR.XRSettings.isDeviceActive || Current != null) return;
            var host = new GameObject("ClinicDevKeys (runtime only)");
            DontDestroyOnLoad(host);
            Current = host.AddComponent<ClinicDevKeysRuntime>();
        }

        void Awake()
        {
            speaker = gameObject.AddComponent<AudioSource>();
            speaker.playOnAwake = false; speaker.spatialBlend = 0;
        }

        void Update()
        {
            UpdateUi();
            var keyboard = Keyboard.current;
            if (keyboard == null || FirstOrderInput.TextInputFocused) return;
            if (keyboard.f4Key.wasPressedThisFrame) CleanAll();
            if (keyboard.f5Key.wasPressedThisFrame) CompleteStep();
        }

        // ------------------------------------------------------------------------------------------------ F4
        /// <summary>Cleans every dirty surface through CleanAll: their Cleaned events carry the sound and the visuals.</summary>
        public bool CleanAll()
        {
            var surfaces = FindObjectsByType<CleanableSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var dirty = surfaces.Where(s => s.Ready && !s.IsClean).ToList();
            if (dirty.Count == 0)
            {
                Report(new Result(false, surfaces.Length == 0
                    ? "F4：这里没有带污渍的物品。接一件物品再试。触点和接口清洁用 F5。"
                    : "F4：已经全部干净了。"));
                return false;
            }
            // The clinic's cleaning controller voices the surfaces on its bench; anything else gets the same finish from here.
            var cleaning = FindFirstObjectByType<ClinicCleaningController>();
            bool voiced = cleaning != null && dirty.Any(s => cleaning.Surfaces.Contains(s));
            foreach (var surface in dirty) surface.CleanAll();
            if (!voiced) Play(ref doneClip, RepairSoundSynth.Cue.CleanDone, .9f);
            Report(new Result(true, "F4：清除了 " + dirty.Count + " 处污渍。"));
            return true;
        }

        // ------------------------------------------------------------------------------------------------ F5
        /// <summary>Completes one step of the current activity, or says why it cannot be skipped.</summary>
        public bool CompleteStep()
        {
            var result = Step();
            Report(result);
            return result.ok;
        }

        Result Step()
        {
            var clinic = FindFirstObjectByType<UnifiedClinicDirector>();
            bool clinicReady = clinic != null && clinic.Initialized;
            if (clinicReady && clinic.ConsoleOpen) return StepAtBench(clinic);
            var patient = SurgeryPatientRuntime.Current;
            if (patient != null && patient.Busy) return StepSurgery(patient);
            if (clinicReady && clinic.State != null && (clinic.State.phase == TwoNightPhase.Night1Counter || clinic.State.phase == TwoNightPhase.Night1Ledger)) return StepAtCounter(clinic);
            var robot = FindFirstObjectByType<FirstOrderFlow>();
            if (robot != null && robot.isActiveAndEnabled)
                return new Result(false, "七号工单还没有可以安全跳过的入口，请手动操作。下一步：" + robot.NextHint());
            return new Result(false, "现在没有进行中的流程。到收件台接一单，或按 - 叫病人。");
        }

        // ---- bench ----
        Result StepAtBench(UnifiedClinicDirector clinic)
        {
            var cleaning = FindFirstObjectByType<ClinicCleaningController>();
            if (cleaning != null && cleaning.Cleaning)
            {
                if (cleaning.AllClean)
                {
                    cleaning.SetCleaning(false);
                    return new Result(true, "已经全部干净，放下了清洁工具。");
                }
                foreach (var surface in cleaning.Surfaces) if (surface != null) surface.CleanAll();
                return new Result(true, "清洁到 100%。");
            }
            var item = clinic.Station.Inspector.CurrentItem;
            var bridge = item != null ? item.GetComponentInChildren<RepairPropStationBridge>(true) : null;
            return bridge != null ? StepBenchFlow(bridge, clinic) : StepSession(clinic.Station.Session);
        }

        static Result StepBenchFlow(RepairPropStationBridge bridge, UnifiedClinicDirector clinic)
        {
            var flow = bridge.Flow;
            if (bridge.Gesture != null && bridge.Gesture.Busy)
            {
                string label = bridge.Gesture.Current != null ? bridge.Gesture.Current.label : "当前动作";
                bridge.Gesture.CompleteNow();
                return new Result(true, label + "。" + FirstLine(flow.LastFeedback));
            }
            if (!bridge.CanInteract)
            {
                if (flow.Completed) return new Result(false, "这件已经修好，等它回到交件台。");
                var station = clinic.Station;
                if (station.CleaningActive) return new Result(false, "外观编辑或清洁工具还开着，先关掉。");
                if (station.InputLocked) return new Result(false, "工具正在动作，等它结束。");
                return new Result(false, "维修台现在不接受操作。");
            }
            var next = RepairPropWorkflowStatus.NextCommand(flow);
            if (next == null) return new Result(false, "不能跳过：" + RepairPropWorkflowStatus.NextStep(flow));
            bool whole = next.target == flow.Definition.rootObjectName;
            bool ran = whole ? bridge.ExecuteWholeItemFromPanel(next.id) : bridge.SelectTarget(next.target) && bridge.ExecuteFromPanel(next.id);
            if (ran) return new Result(true, next.label + "。" + (next.label == flow.LastFeedback ? "" : FirstLine(flow.LastFeedback)));
            flow.CanExecute(next.id, next.target, next.tool, out string reason);
            return new Result(false, "“" + next.label + "”不能执行：" + (reason ?? "维修台没有接受这个操作。"));
        }

        // Items without a bench flow: the session's own authored steps, scans and judgement, in order.
        static Result StepSession(RepairSession session)
        {
            if (session == null || session.CurrentCase == null) return new Result(false, "维修台上没有进行中的物品。");
            string said = null;
            System.Action<string, FeedbackKind> listen = (text, kind) => said = text;
            session.Feedback += listen;
            try
            {
                var data = session.CurrentCase;
                string Said(string fallback) => string.IsNullOrEmpty(said) ? fallback : said;
                switch (session.Stage)
                {
                    case RepairStage.Inspect:
                        foreach (var step in data.repairSteps)
                        {
                            if (step == null || session.IsStepDone(step.stepId)) continue;
                            if (session.PreviewAction(step.action, step.targetPointId, out var ready) == ActionOutcome.Performed && ready == step)
                            {
                                session.PerformAction(step.action, step.targetPointId);
                                return new Result(true, "完成步骤：" + step.label + "。");
                            }
                        }
                        foreach (var point in data.inspectionPoints)
                        {
                            if (point == null || session.HasScanned(point.pointId) || !point.requiredForDiagnosis && string.IsNullOrEmpty(point.unlocksClueId)) continue;
                            bool wasScanning = session.ScanModeActive;
                            session.SetScanMode(true);
                            var outcome = session.ReportScan(point.pointId);
                            session.SetScanMode(wasScanning);
                            if (outcome != ScanOutcome.NewFinding) continue;   // not reachable yet (still behind a step): try the next one
                            return new Result(true, "扫描：" + point.displayName + "。");
                        }
                        return session.TryBeginDiagnosis() ? new Result(true, "检查完成，进入诊断。") : new Result(false, "不能跳过：" + Said("检查还没做完。"));
                    case RepairStage.Diagnose:
                        return session.SubmitDiagnosis(data.correctDiagnosisId) ? new Result(true, "诊断：选了正确答案。") : new Result(false, "不能跳过：" + Said("诊断没有通过。"));
                    case RepairStage.Decide:
                        if (data.IsNarrative)
                        {
                            foreach (var ending in data.endings)
                                if (ending != null && session.SubmitEnding(ending.endingId)) return new Result(true, "结局：选了“" + ending.label + "”。");
                            return new Result(false, "不能跳过：" + Said("还没有可选的结局。"));
                        }
                        var correct = data.outcomes.FirstOrDefault(o => o != null && o.isCorrect);
                        if (correct != null && session.SubmitDecision(correct.decision)) return new Result(true, "决定：" + correct.resultTitle + "。");
                        return new Result(false, "不能跳过：" + Said("没有可用的决定。"));
                    case RepairStage.Result:
                    case RepairStage.Summary:
                        return new Result(false, "这一单已有结果，请点界面上的按钮继续。");
                    default:
                        return new Result(false, "还没有接收物品。");
                }
            }
            finally { session.Feedback -= listen; }
        }

        // ---- surgery ----
        static Result StepSurgery(SurgeryPatientRuntime patient)
        {
            var op = SurgeryProcedureRuntime.Current;
            var table = SurgeryTableV2Runtime.Current;
            if (op == null || table == null) return new Result(false, "手术台不可用。");
            switch (patient.PhaseNow)
            {
                case SurgeryPatientRuntime.Phase.WaitClear: return OpenStraps(table, "解开了束带，罗亚开始上台。");
                case SurgeryPatientRuntime.Phase.AwaitStraps:
                    return op.Run("straps.close") ? new Result(true, "扣上了全部束带。") : new Result(false, "不能跳过：" + op.LastMessage);
                case SurgeryPatientRuntime.Phase.Secured: break;
                default: return new Result(false, "不能跳过：罗亚正在走动或上下手术台，这段动作没有快进入口，等他停下。");
            }
            if (op.Complete) return OpenStraps(table, "手术已完成：解开了束带，罗亚起身离开。");
            var closeup = SurgeryCloseupRuntime.Current;
            if (closeup != null && !closeup.Active) closeup.Open();
            if (op.Running)
            {
                string label = op.RunningCommand.label;
                op.FastForward();
                return new Result(true, "快进完成：" + label + "。");
            }
            var next = op.Next();
            if (next == null) return new Result(false, "不能跳过：" + op.NextStepText);
            bool ran = closeup != null && closeup.Active ? closeup.RunFromPanel(next.id) : op.Run(next.id);
            if (!ran) return new Result(false, "不能跳过：" + (op.LastMessage ?? op.Why(next)));
            op.FastForward();
            return new Result(true, next.label + "。");
        }

        static Result OpenStraps(SurgeryTableV2Runtime table, string message)
        {
            var closed = table.Controls.Where(c => c.Kind == SurgeryControlKind.Strap && table.StrapClosed(c)).ToList();
            if (closed.Count == 0) return new Result(false, "束带已经全部解开，等罗亚动作。");
            foreach (var strap in closed) table.Activate(strap);
            return new Result(true, message);
        }

        // ---- counter ----
        static Result StepAtCounter(UnifiedClinicDirector clinic)
        {
            var counter = clinic.Counter;
            if (counter.DialogueVisible)
            {
                counter.DialogueButton.onClick.Invoke();
                return new Result(true, "跳过一句顾客对白。");
            }
            if (counter.RetryVisible)
            {
                counter.RetryCommunicator();
                return new Result(true, "重新处理这一单。");
            }
            if (counter.LedgerVisible)
            {
                if (!counter.ConfirmButton.interactable) return new Result(false, "不能跳过：账本现在不能确认。");
                counter.ConfirmLedger();
                return new Result(true, "确认了今晚的账本。");
            }
            if (clinic.Carrying) return Trade(clinic, ClinicTradeAction.Console, "放到维修台上，开始维修。");
            if (clinic.DeliverLampState != UnifiedClinicDirector.PadLamp.Off) return Trade(clinic, ClinicTradeAction.Deliver, "交付了物品。");
            if (clinic.ReceiveLampState == UnifiedClinicDirector.PadLamp.On) return Trade(clinic, ClinicTradeAction.Receive, "接收了下一件物品。");
            if (clinic.State.ActiveTrade != null && clinic.TradeState == ClinicTradeState.InRepair) return Trade(clinic, ClinicTradeAction.Console, "回到维修台。");
            return new Result(false, "现在没有可以推进的事。可以按 - 叫病人。");
        }

        static Result Trade(UnifiedClinicDirector clinic, ClinicTradeAction action, string done) =>
            clinic.Interact(action) ? new Result(true, done) : new Result(false, "不能跳过：" + clinic.LastMessage);

        // ------------------------------------------------------------------------------------------------ feedback
        void Report(Result result)
        {
            LastMessage = result.message;
            ShowToast(result.message, result.ok);
            if (!result.ok) Play(ref denyClip, RepairSoundSynth.Cue.Deny, .75f);
            Debug.Log("[DevKeys] " + (result.ok ? "ok: " : "refused: ") + result.message);
        }

        void Play(ref AudioClip clip, RepairSoundSynth.Cue cue, float volume)
        {
            if (clip == null) clip = RepairSoundSynth.Create(cue);
            speaker.PlayOneShot(clip, volume);
        }

        static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int end = text.IndexOf('\n');
            return end < 0 ? text : text.Substring(0, end);
        }

        // ------------------------------------------------------------------------------------------------ UI
        void ShowToast(string message, bool ok)
        {
            EnsureUi();
            toastText.text = message;
            toastText.color = ok ? new Color(.72f, .97f, .66f) : new Color(1f, .72f, .38f);
            var rect = (RectTransform)toast.transform;
            // Width first: the wrapped height depends on it.
            rect.sizeDelta = new Vector2(Mathf.Min(toastText.preferredWidth + 32, 900), 36);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, Mathf.Max(36, toastText.preferredHeight + 14));
            toast.SetActive(true);
            toastUntil = Time.unscaledTime + ToastSeconds;
        }

        void UpdateUi()
        {
            if (canvas == null) return;
            float now = Time.unscaledTime;
            bool toastVisible = now < toastUntil;
            if (toast.activeSelf != toastVisible) toast.SetActive(toastVisible);
            if (toastVisible) toastGroup.alpha = Mathf.Clamp01((toastUntil - now) / .4f);
            // Nothing on screen: the canvas costs nothing and stays out of the frame.
            canvas.enabled = toastVisible;
        }

        void EnsureUi()
        {
            if (canvas != null) return;
            var font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            canvas = ClinicHelpRuntime.OverlayCanvas("DevKeysCanvas", transform);
            BuildToast(canvas.transform, font);
        }

        void BuildToast(Transform parent, Font font)
        {
            var box = ClinicHelpRuntime.Box("Toast", parent, new Color(0, 0, 0, .78f));
            toast = box.gameObject;
            box.anchorMin = box.anchorMax = new Vector2(.5f, 0); box.pivot = new Vector2(.5f, 0);
            box.anchoredPosition = new Vector2(0, 96); box.sizeDelta = new Vector2(400, 36);
            toastGroup = toast.AddComponent<CanvasGroup>();
            toastGroup.blocksRaycasts = false; toastGroup.interactable = false;
            toastText = ClinicHelpRuntime.Label("Text", box, font, 17, Color.white, TextAnchor.MiddleCenter);
            toastText.horizontalOverflow = HorizontalWrapMode.Wrap;
            var r = toastText.rectTransform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(14, 5); r.offsetMax = new Vector2(-14, -5);
            toast.SetActive(false);
        }

        void OnDestroy()
        {
            if (denyClip != null) Destroy(denyClip);
            if (doneClip != null) Destroy(doneClip);
            if (Current == this) Current = null;
        }
    }
}
