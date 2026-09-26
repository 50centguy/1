using System;
using System.Collections.Generic;
using BorderRepair.Data;

namespace BorderRepair.Core
{
    public enum RepairStage { Intake, Inspect, Diagnose, Decide, Result, Summary }

    public enum FeedbackKind { Info, Success, Warning, Error }

    public enum ScanOutcome { Rejected, UnknownPoint, AlreadyScanned, NewFinding }

    /// <summary>一次维修动作的结果。</summary>
    public enum ActionOutcome { Rejected, WrongTool, Blocked, AlreadyDone, Performed }

    public sealed class CaseRecord
    {
        public RepairCaseData Case;
        public int WrongDiagnosisCount;
        public int InvalidActionCount;
        public bool DiagnosisCorrect;
        public RepairDecision? Decision;
        public DecisionOutcome Outcome;
        public float StartTime;
        public float EndTime;

        /// <summary>叙事案件选择的结局（判断类案件为 null）。</summary>
        public NarrativeEnding Ending;
        /// <summary>本件解锁的线索，按解锁顺序。</summary>
        public readonly List<string> Clues = new List<string>();

        public bool IsJudged => Case == null || !Case.IsNarrative;
        public bool IsFinished => Outcome != null || Ending != null;
        public bool DecisionCorrect => Outcome != null && Outcome.isCorrect;
        public float Duration => EndTime - StartTime;
    }

    /// <summary>
    /// 一轮营业的状态机：接收 → 检查/扫描 → 诊断 → 决定 → 结果 → 下一件 → 总结。
    /// 不依赖场景和 MonoBehaviour；所有越级操作都会被拒绝并发出反馈。
    /// </summary>
    public sealed class RepairSession
    {
        readonly RepairShiftData shift;
        readonly Func<float> clock;
        readonly List<CaseRecord> records = new List<CaseRecord>();
        readonly HashSet<string> scannedPoints = new HashSet<string>();
        readonly HashSet<string> doneSteps = new HashSet<string>();
        readonly Dictionary<string, RepairPartState> partStates = new Dictionary<string, RepairPartState>();

        public event Action<RepairStage> StageChanged;
        public event Action<RepairCaseData, int> CaseStarted;
        public event Action<string, FeedbackKind> Feedback;
        public event Action<InspectionPointInfo> FindingAdded;
        public event Action<bool> ScanModeChanged;
        public event Action<DecisionOutcome> DecisionResolved;

        // 叙事案件 / 维修步骤
        public event Action<RepairStepDefinition> StepPerformed;
        public event Action<string, RepairPartState> PartStateChanged;
        public event Action<ClueDefinition> ClueUnlocked;
        public event Action<NarrativeEnding> EndingResolved;

        public RepairStage Stage { get; private set; } = RepairStage.Intake;
        public int CaseIndex { get; private set; }
        public bool ScanModeActive { get; private set; }
        public float ShiftStartTime { get; private set; }
        public float ShiftEndTime { get; private set; }

        public RepairShiftData Shift => shift;
        public int CaseCount => shift.cases.Count;
        public bool IsLastCase => CaseIndex >= CaseCount - 1;
        public RepairCaseData CurrentCase => CaseIndex >= 0 && CaseIndex < CaseCount ? shift.cases[CaseIndex] : null;
        public CaseRecord CurrentRecord => records.Count > 0 ? records[records.Count - 1] : null;
        public IReadOnlyList<CaseRecord> Records => records;

        public RepairSession(RepairShiftData shift, Func<float> clock)
        {
            if (shift == null) throw new ArgumentNullException(nameof(shift));
            if (shift.cases == null || shift.cases.Count == 0) throw new ArgumentException("营业数据中没有送修物", nameof(shift));
            this.shift = shift;
            this.clock = clock ?? (() => 0f);
        }

        public bool HasScanned(string pointId) => scannedPoints.Contains(pointId);

        public int RequiredFindingCount => CurrentCase != null ? CurrentCase.RequiredPointCount : 0;

        public int FoundRequiredCount
        {
            get
            {
                int count = 0;
                foreach (var id in scannedPoints)
                {
                    var p = CurrentCase.FindPoint(id);
                    if (p != null && p.requiredForDiagnosis) count++;
                }
                return count;
            }
        }

        public int CorrectDecisionCount
        {
            get
            {
                int n = 0;
                foreach (var r in records) if (r.DecisionCorrect) n++;
                return n;
            }
        }

        /// <summary>按正确/错误计分的案件数量（不含叙事案件），用于“判断正确 X/Y”的分母。</summary>
        public int JudgedCaseCount
        {
            get
            {
                int n = 0;
                foreach (var c in shift.cases) if (c != null && !c.IsNarrative) n++;
                return n;
            }
        }

        public bool IsStepDone(string stepId) => doneSteps.Contains(stepId);
        public bool HasClue(string clueId) => CurrentRecord != null && CurrentRecord.Clues.Contains(clueId);
        public int RequiredClueCount => CurrentCase != null ? CurrentCase.RequiredClueCount : 0;

        public int FoundRequiredClueCount
        {
            get
            {
                int n = 0;
                if (CurrentRecord == null) return 0;
                foreach (var id in CurrentRecord.Clues)
                {
                    var c = CurrentCase.FindClue(id);
                    if (c != null && c.requiredForDecision) n++;
                }
                return n;
            }
        }

        public RepairPartState GetPartState(string pointId) =>
            pointId != null && partStates.TryGetValue(pointId, out var s) ? s : RepairPartState.Installed;

        public int TotalWrongDiagnoses
        {
            get
            {
                int n = 0;
                foreach (var r in records) n += r.WrongDiagnosisCount;
                return n;
            }
        }

        public float ShiftDuration => (Stage == RepairStage.Summary ? ShiftEndTime : clock()) - ShiftStartTime;

        // ---------- 流程操作 ----------

        public void StartShift()
        {
            records.Clear();
            ShiftStartTime = clock();
            ShiftEndTime = 0f;
            BeginCase(0);
        }

        public void Restart() => StartShift();

        public bool AcceptItem()
        {
            if (!RequireStage(RepairStage.Intake, "接收物品")) return false;
            SetStage(RepairStage.Inspect);
            Say($"已接收：{CurrentCase.itemName}。拖动旋转查看外观，开启扫描模式检查可疑部位。", FeedbackKind.Info);
            return true;
        }

        public bool SetScanMode(bool on)
        {
            if (on == ScanModeActive) return true;
            if (on && !RequireStage(RepairStage.Inspect, "开启扫描")) return false;
            SetScanModeInternal(on);
            return true;
        }

        public bool ToggleScanMode() => SetScanMode(!ScanModeActive);

        /// <summary>报告一次扫描点击。pointId 为 null 表示点到了物品上没有检查点的位置。</summary>
        public ScanOutcome ReportScan(string pointId)
        {
            if (!RequireStage(RepairStage.Inspect, "扫描")) return ScanOutcome.Rejected;
            if (!ScanModeActive)
            {
                CurrentRecord.InvalidActionCount++;
                Say("请先开启扫描模式（空格键或【扫描模式】按钮），再点击部位。", FeedbackKind.Warning);
                return ScanOutcome.Rejected;
            }

            var info = CurrentCase.FindPoint(pointId);
            if (info == null)
            {
                Say("该处扫描未见异常。", FeedbackKind.Info);
                return ScanOutcome.UnknownPoint;
            }
            if (!string.IsNullOrEmpty(info.requiresStepId) && !doneSteps.Contains(info.requiresStepId))
            {
                Say(string.IsNullOrEmpty(info.blockedFinding) ? "现在还看不到这个部位。" : info.blockedFinding, FeedbackKind.Warning);
                return ScanOutcome.Rejected;
            }
            if (!scannedPoints.Add(info.pointId))
            {
                Say($"{info.displayName} 已经扫描过了。", FeedbackKind.Info);
                return ScanOutcome.AlreadyScanned;
            }

            FindingAdded?.Invoke(info);
            if (CurrentCase.IsNarrative)
                Say($"{info.displayName}：{info.finding}", FeedbackKind.Info);
            else if (info.requiredForDiagnosis)
                Say($"发现异常：{info.displayName}（关键异常 {FoundRequiredCount}/{RequiredFindingCount}）", FeedbackKind.Success);
            else
                Say($"{info.displayName}：未见异常。", FeedbackKind.Info);
            if (!string.IsNullOrEmpty(info.unlocksClueId)) UnlockClue(info.unlocksClueId);
            return ScanOutcome.NewFinding;
        }

        /// <summary>
        /// 用三种维修动作之一处理某个部位。按案件数据中的步骤判断前置条件，执行后更新零件状态、解锁线索。
        /// 鼠标工具与以后的 VR 手柄都调用这个方法。
        /// </summary>
        public ActionOutcome PerformAction(RepairActionType action, string pointId)
        {
            if (!RequireStage(RepairStage.Inspect, "动手维修")) return ActionOutcome.Rejected;
            return Resolve(action, pointId, apply: true, out _);
        }

        /// <summary>
        /// 只判断、不执行：这个动作现在会得到什么结果（不改状态、不发反馈、不计失误）。
        /// 工具动画用它决定要不要播放“进入—对准—接触—退出”；真正的状态变化仍在接触时刻调用 PerformAction 完成。
        /// </summary>
        public ActionOutcome PreviewAction(RepairActionType action, string pointId, out RepairStepDefinition step)
        {
            step = null;
            if (Stage != RepairStage.Inspect || CurrentCase == null) return ActionOutcome.Rejected;
            return Resolve(action, pointId, apply: false, out step);
        }

        ActionOutcome Resolve(RepairActionType action, string pointId, bool apply, out RepairStepDefinition resolved)
        {
            resolved = null;
            var matching = new List<RepairStepDefinition>();
            bool pointHasSteps = false;
            foreach (var s in CurrentCase.repairSteps)
            {
                if (s == null || s.targetPointId != pointId) continue;
                pointHasSteps = true;
                if (s.action == action) matching.Add(s);
            }
            if (matching.Count == 0)
            {
                if (!apply) return ActionOutcome.WrongTool;
                CurrentRecord.InvalidActionCount++;
                Say(pointHasSteps ? $"这个部位用不上“{RepairActionText.Label(action)}”。换一件工具试试。" : "这里没有需要这样处理的零件。", FeedbackKind.Warning);
                return ActionOutcome.WrongTool;
            }

            RepairStepDefinition firstPending = null, ready = null;
            foreach (var s in matching)
            {
                if (doneSteps.Contains(s.stepId)) continue;
                if (firstPending == null) firstPending = s;
                if (IsStepReady(s)) { ready = s; break; }
            }
            if (firstPending == null)
            {
                if (apply) Say("这一步已经做过了。", FeedbackKind.Info);
                return ActionOutcome.AlreadyDone;
            }
            if (ready == null)
            {
                if (!apply) return ActionOutcome.Blocked;
                CurrentRecord.InvalidActionCount++;
                Say(string.IsNullOrEmpty(firstPending.blockedText) ? "现在还做不到这一步。" : firstPending.blockedText, FeedbackKind.Warning);
                return ActionOutcome.Blocked;
            }

            resolved = ready;
            if (!apply) return ActionOutcome.Performed;
            doneSteps.Add(ready.stepId);
            SetPartState(ready.targetPointId, ready.resultState);
            if (!string.IsNullOrEmpty(ready.sideEffectPointId)) SetPartState(ready.sideEffectPointId, ready.sideEffectState);
            StepPerformed?.Invoke(ready);
            if (!string.IsNullOrEmpty(ready.resultText)) Say(ready.resultText, FeedbackKind.Info);
            if (!string.IsNullOrEmpty(ready.unlocksClueId)) UnlockClue(ready.unlocksClueId);
            return ActionOutcome.Performed;
        }

        bool IsStepReady(RepairStepDefinition step)
        {
            foreach (var r in step.requiredSteps) if (!doneSteps.Contains(r)) return false;
            foreach (var r in step.requiredClues) if (!HasClue(r)) return false;
            return true;
        }

        void SetPartState(string pointId, RepairPartState state)
        {
            partStates[pointId] = state;
            PartStateChanged?.Invoke(pointId, state);
        }

        void UnlockClue(string clueId)
        {
            var clue = CurrentCase.FindClue(clueId);
            if (clue == null || CurrentRecord.Clues.Contains(clueId)) return;
            CurrentRecord.Clues.Add(clueId);
            ClueUnlocked?.Invoke(clue);
            Say($"新线索：{clue.title}（{FoundRequiredClueCount}/{RequiredClueCount}）", FeedbackKind.Success);
        }

        public bool TryBeginDiagnosis()
        {
            if (!RequireStage(RepairStage.Inspect, "开始诊断")) return false;
            if (CurrentCase.IsNarrative)
            {
                int foundClues = FoundRequiredClueCount, needClues = RequiredClueCount;
                if (foundClues < needClues)
                {
                    CurrentRecord.InvalidActionCount++;
                    Say($"还有没弄清楚的地方：线索 {foundClues}/{needClues}。继续检查、动手拆解。", FeedbackKind.Warning);
                    return false;
                }
            }
            else
            {
                int found = FoundRequiredCount, need = RequiredFindingCount;
                if (found < need)
                {
                    CurrentRecord.InvalidActionCount++;
                    Say($"检查还不充分：关键异常已找到 {found}/{need} 处。开启扫描模式，继续点击可疑部位。", FeedbackKind.Warning);
                    return false;
                }
            }
            SetScanModeInternal(false);
            // 没有诊断选项的案件（叙事案件）直接进入处理决定
            SetStage(CurrentCase.diagnosisOptions.Count == 0 ? RepairStage.Decide : RepairStage.Diagnose);
            return true;
        }

        public bool ReturnToInspection()
        {
            if (!RequireStage(RepairStage.Diagnose, "返回检查")) return false;
            SetStage(RepairStage.Inspect);
            return true;
        }

        public bool SubmitDiagnosis(string optionId)
        {
            if (!RequireStage(RepairStage.Diagnose, "提交诊断")) return false;
            var option = CurrentCase.FindDiagnosis(optionId);
            if (option == null)
            {
                Say("无效的诊断选项。", FeedbackKind.Error);
                return false;
            }

            if (option.optionId == CurrentCase.correctDiagnosisId)
            {
                CurrentRecord.DiagnosisCorrect = true;
                SetStage(RepairStage.Decide);
                Say(string.IsNullOrEmpty(CurrentCase.diagnosisConfirmedText) ? "诊断与检查结果一致。" : CurrentCase.diagnosisConfirmedText, FeedbackKind.Success);
                return true;
            }

            CurrentRecord.WrongDiagnosisCount++;
            Say(string.IsNullOrEmpty(option.wrongFeedback) ? "这个诊断与扫描记录不符，再看看记录。" : option.wrongFeedback, FeedbackKind.Error);
            return false;
        }

        public bool SubmitDecision(RepairDecision decision)
        {
            if (!RequireStage(RepairStage.Decide, "做出处理决定")) return false;
            if (CurrentCase.IsNarrative)
            {
                Say("这一单没有标准答案。请从右侧选择一种处理方式。", FeedbackKind.Warning);
                return false;
            }
            var outcome = CurrentCase.FindOutcome(decision) ?? new DecisionOutcome
            {
                decision = decision,
                isCorrect = false,
                resultTitle = "未配置结果",
                reason = "该案例没有为此决定配置结果，请检查案例数据。"
            };

            var record = CurrentRecord;
            record.Decision = decision;
            record.Outcome = outcome;
            record.EndTime = clock();

            DecisionResolved?.Invoke(outcome);
            SetStage(RepairStage.Result);
            return true;
        }

        /// <summary>叙事案件：选择一个结局。不判定对错，记录在 CaseRecord.Ending。</summary>
        public bool SubmitEnding(string endingId)
        {
            if (!RequireStage(RepairStage.Decide, "选择处理方式")) return false;
            if (!CurrentCase.IsNarrative)
            {
                Say("这一单请用“维修 / 建议更换 / 拒绝处理”做决定。", FeedbackKind.Warning);
                return false;
            }
            var ending = CurrentCase.FindEnding(endingId);
            if (ending == null)
            {
                Say("无效的处理方式。", FeedbackKind.Error);
                return false;
            }
            foreach (var r in ending.requiredClues)
            {
                if (HasClue(r)) continue;
                Say("你还没掌握做这个选择所需的信息。", FeedbackKind.Warning);
                return false;
            }

            var record = CurrentRecord;
            record.Ending = ending;
            record.EndTime = clock();
            EndingResolved?.Invoke(ending);
            SetStage(RepairStage.Result);
            return true;
        }

        public bool NextCase()
        {
            if (!RequireStage(RepairStage.Result, "进入下一件")) return false;
            if (IsLastCase)
            {
                ShiftEndTime = clock();
                SetStage(RepairStage.Summary);
            }
            else
            {
                BeginCase(CaseIndex + 1);
            }
            return true;
        }

        // ---------- 内部 ----------

        void BeginCase(int index)
        {
            CaseIndex = index;
            scannedPoints.Clear();
            doneSteps.Clear();
            partStates.Clear();
            SetScanModeInternal(false);
            records.Add(new CaseRecord { Case = CurrentCase, StartTime = clock() });
            CaseStarted?.Invoke(CurrentCase, index);
            SetStage(RepairStage.Intake);
        }

        void SetStage(RepairStage stage)
        {
            if (stage != RepairStage.Inspect) SetScanModeInternal(false);
            Stage = stage;
            StageChanged?.Invoke(stage);
        }

        void SetScanModeInternal(bool on)
        {
            if (ScanModeActive == on) return;
            ScanModeActive = on;
            ScanModeChanged?.Invoke(on);
        }

        bool RequireStage(RepairStage required, string action)
        {
            if (Stage == required) return true;
            if (CurrentRecord != null && Stage != RepairStage.Summary) CurrentRecord.InvalidActionCount++;
            Say($"现在不能{action}：{StageHint(Stage)}", FeedbackKind.Warning);
            return false;
        }

        static string StageHint(RepairStage stage)
        {
            switch (stage)
            {
                case RepairStage.Intake: return "请先接收物品。";
                case RepairStage.Inspect: return "请先完成检查，再开始诊断。";
                case RepairStage.Diagnose: return "请先选择诊断。";
                case RepairStage.Decide: return "请先做出处理决定。";
                case RepairStage.Result: return "请点击【下一件】继续。";
                case RepairStage.Summary: return "本次营业已结束，可以重新开始。";
                default: return string.Empty;
            }
        }

        void Say(string message, FeedbackKind kind) => Feedback?.Invoke(message, kind);
    }
}
