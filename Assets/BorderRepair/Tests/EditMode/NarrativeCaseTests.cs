using System.Collections.Generic;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Inspection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.Tests
{
    /// <summary>叙事竖切“工人义手”：数据完整性、拆卸顺序、线索解锁、三种结局、与旧案件的兼容。</summary>
    public class NarrativeCaseTests
    {
        const string CasePath = "Assets/BorderRepair/Data/Narrative/Case_N01_WorkerProsthetic.asset";
        const string ShiftPath = "Assets/BorderRepair/Data/Narrative/Shift_Narrative_WorkerHand.asset";
        const string PrefabPath = "Assets/BorderRepair/Prefabs/Items/Narrative/Item_WorkerProsthetic_Placeholder.prefab";
        const string DefaultShiftPath = "Assets/BorderRepair/Data/Shift_Prototype.asset";

        static readonly string[] StablePointIds =
            { "shell", "lease_seal", "fastener_a", "fastener_b", "drive", "force_limiter", "control_board", "data_port" };

        readonly List<Object> created = new List<Object>();
        float now;

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        static RepairCaseData LoadCase()
        {
            var c = AssetDatabase.LoadAssetAtPath<RepairCaseData>(CasePath);
            Assert.IsNotNull(c, "未找到叙事案件，请先运行 Border Repair > Narrative > Build Worker Hand Slice");
            return c;
        }

        RepairSession NewSession(params RepairCaseData[] cases)
        {
            var shift = ScriptableObject.CreateInstance<RepairShiftData>();
            created.Add(shift);
            shift.cases = new List<RepairCaseData>(cases);
            var s = new RepairSession(shift, () => now);
            s.StartShift();
            return s;
        }

        RepairCaseData MakeJudgedCase()
        {
            var c = ScriptableObject.CreateInstance<RepairCaseData>();
            created.Add(c);
            c.caseId = "judged";
            c.itemName = "judged";
            c.inspectionPoints.Add(new InspectionPointInfo { pointId = "p1", displayName = "p1", finding = "bad", requiredForDiagnosis = true });
            c.diagnosisOptions.Add(new DiagnosisOption { optionId = "right", label = "right" });
            c.diagnosisOptions.Add(new DiagnosisOption { optionId = "wrong", label = "wrong" });
            c.correctDiagnosisId = "right";
            foreach (RepairDecision d in System.Enum.GetValues(typeof(RepairDecision)))
                c.outcomes.Add(new DecisionOutcome { decision = d, isCorrect = d == RepairDecision.Repair, resultTitle = d.ToString(), reason = "r" });
            return c;
        }

        /// <summary>按剧情顺序完成全部调查，停在“决定”阶段。</summary>
        static void Investigate(RepairSession s)
        {
            Assert.IsTrue(s.AcceptItem());
            Assert.IsTrue(s.SetScanMode(true));
            Assert.AreEqual(ScanOutcome.NewFinding, s.ReportScan("shell"));
            Assert.AreEqual(ActionOutcome.Performed, s.PerformAction(RepairActionType.RemoveFastener, "fastener_a"));
            Assert.AreEqual(ActionOutcome.Performed, s.PerformAction(RepairActionType.RemoveFastener, "fastener_b"));
            Assert.AreEqual(ActionOutcome.Performed, s.PerformAction(RepairActionType.OpenHousing, "shell"));
            Assert.AreEqual(ActionOutcome.Performed, s.PerformAction(RepairActionType.ServiceModule, "force_limiter"));
            Assert.AreEqual(ActionOutcome.Performed, s.PerformAction(RepairActionType.ServiceModule, "data_port"));
            Assert.IsTrue(s.TryBeginDiagnosis());
            Assert.AreEqual(RepairStage.Decide, s.Stage);
        }

        // ---------- 数据 ----------

        [Test]
        public void NarrativeAssetsValidateWithStableIds()
        {
            var c = LoadCase();
            var errors = new List<string>();
            Assert.IsTrue(c.Validate(errors), string.Join("\n", errors));
            Assert.IsTrue(c.IsNarrative);
            CollectionAssert.AreEquivalent(StablePointIds, c.inspectionPoints.ConvertAll(p => p.pointId));
            Assert.AreEqual(3, c.RequiredClueCount);
            Assert.AreEqual(3, c.endings.Count);
            Assert.IsEmpty(c.diagnosisOptions, "叙事案件不做对错诊断");

            var shift = AssetDatabase.LoadAssetAtPath<RepairShiftData>(ShiftPath);
            Assert.IsNotNull(shift);
            Assert.AreEqual(1, shift.cases.Count);
            Assert.AreEqual(c, shift.cases[0]);
        }

        [Test]
        public void DefaultShiftIsUnchanged()
        {
            var shift = AssetDatabase.LoadAssetAtPath<RepairShiftData>(DefaultShiftPath);
            Assert.IsNotNull(shift);
            CollectionAssert.AreEqual(new[] { "case_01_communicator", "case_02_nav_beacon", "case_03_salvage_drone" },
                                      shift.cases.ConvertAll(x => x.caseId), "默认营业的三件物品及顺序不应改变");
            foreach (var x in shift.cases)
            {
                Assert.IsFalse(x.IsNarrative, $"{x.caseId} 应仍为判断类案件");
                Assert.IsFalse(x.HasRepairSteps);
            }
        }

        // 占位 prefab（回退用）和 v2 正式 prefab 都要满足同样的结构
        [TestCase(PrefabPath)]
        [TestCase("Assets/BorderRepair/Prefabs/Items/Narrative/Item_WorkerProsthetic_v2.prefab")]
        public void PrefabHasPartsAndSnapTargetsForEverySteppedPoint(string prefabPath)
        {
            var c = LoadCase();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab);
            var parts = new Dictionary<string, RepairPart>();
            foreach (var p in prefab.GetComponentsInChildren<RepairPart>(true)) parts[p.PartId] = p;
            var points = new HashSet<string>();
            foreach (var p in prefab.GetComponentsInChildren<InspectionPoint>(true)) points.Add(p.PointId);

            foreach (var id in StablePointIds) Assert.IsTrue(points.Contains(id), $"prefab 缺少检查点 {id}");
            foreach (var step in c.repairSteps)
            {
                Assert.IsTrue(parts.TryGetValue(step.targetPointId, out var part), $"步骤 {step.stepId} 的目标 {step.targetPointId} 没有 RepairPart");
                if (step.resultState == RepairPartState.Removed || step.resultState == RepairPartState.Open)
                    Assert.IsNotNull(part.SnapTarget, $"{step.targetPointId} 需要吸附位置");
            }
        }

        [Test]
        public void IntakeTextDoesNotRevealTheTruth()
        {
            var c = LoadCase();
            foreach (var word in new[] { "远程", "雇主", "配额", "节拍", "140", "参数", "运维", "限力" })
            {
                StringAssert.DoesNotContain(word, c.customerStatement, "顾客自述不应一开始讲出真相");
                StringAssert.DoesNotContain(word, c.intakeNote, "工单备注不应一开始讲出真相");
            }
        }

        // ---------- 拆卸顺序 ----------

        [Test]
        public void DisassemblyOrderIsEnforced()
        {
            var s = NewSession(LoadCase());
            Assert.AreEqual(ActionOutcome.Rejected, s.PerformAction(RepairActionType.RemoveFastener, "fastener_a"), "接收前不能动手");
            s.AcceptItem();

            Assert.AreEqual(ActionOutcome.Blocked, s.PerformAction(RepairActionType.OpenHousing, "shell"), "螺丝没卸不能开壳");
            Assert.AreEqual(ActionOutcome.WrongTool, s.PerformAction(RepairActionType.RemoveFastener, "shell"));
            Assert.AreEqual(ActionOutcome.Blocked, s.PerformAction(RepairActionType.ServiceModule, "force_limiter"), "没开壳不能检测内部模块");

            Assert.AreEqual(ActionOutcome.Performed, s.PerformAction(RepairActionType.RemoveFastener, "fastener_a"));
            Assert.AreEqual(RepairPartState.Removed, s.GetPartState("fastener_a"));
            Assert.AreEqual(RepairPartState.Torn, s.GetPartState("lease_seal"), "卸下螺丝 A 会撕开租赁封条");
            Assert.AreEqual(ActionOutcome.AlreadyDone, s.PerformAction(RepairActionType.RemoveFastener, "fastener_a"));
            Assert.AreEqual(ActionOutcome.Blocked, s.PerformAction(RepairActionType.OpenHousing, "shell"), "还剩一颗螺丝");

            Assert.AreEqual(ActionOutcome.Performed, s.PerformAction(RepairActionType.RemoveFastener, "fastener_b"));
            Assert.AreEqual(ActionOutcome.Performed, s.PerformAction(RepairActionType.OpenHousing, "shell"));
            Assert.AreEqual(RepairPartState.Open, s.GetPartState("shell"));
        }

        [Test]
        public void InternalPartsCannotBeScannedBeforeOpening()
        {
            var s = NewSession(LoadCase());
            s.AcceptItem();
            s.SetScanMode(true);
            Assert.AreEqual(ScanOutcome.Rejected, s.ReportScan("force_limiter"));
            s.PerformAction(RepairActionType.RemoveFastener, "fastener_a");
            s.PerformAction(RepairActionType.RemoveFastener, "fastener_b");
            s.PerformAction(RepairActionType.OpenHousing, "shell");
            Assert.AreEqual(ScanOutcome.NewFinding, s.ReportScan("force_limiter"));
        }

        [Test]
        public void ModuleIsTestedBeforeItCanBeReplaced()
        {
            var s = NewSession(LoadCase());
            s.AcceptItem();
            s.PerformAction(RepairActionType.RemoveFastener, "fastener_a");
            s.PerformAction(RepairActionType.RemoveFastener, "fastener_b");
            s.PerformAction(RepairActionType.OpenHousing, "shell");
            Assert.AreEqual(ActionOutcome.Performed, s.PerformAction(RepairActionType.ServiceModule, "drive"));
            Assert.AreEqual(RepairPartState.Tested, s.GetPartState("drive"));
            Assert.AreEqual(ActionOutcome.Performed, s.PerformAction(RepairActionType.ServiceModule, "drive"));
            Assert.AreEqual(RepairPartState.Replaced, s.GetPartState("drive"));
            Assert.AreEqual(ActionOutcome.AlreadyDone, s.PerformAction(RepairActionType.ServiceModule, "drive"));
        }

        // ---------- 线索 ----------

        [Test]
        public void CluesUnlockOnlyThroughSpecificActionsInOrder()
        {
            var s = NewSession(LoadCase());
            var unlocked = new List<string>();
            s.ClueUnlocked += c => unlocked.Add(c.clueId);
            s.AcceptItem();
            Assert.AreEqual(0, s.FoundRequiredClueCount);
            Assert.IsFalse(s.TryBeginDiagnosis(), "没有线索不能做决定");

            s.SetScanMode(true);
            s.ReportScan("lease_seal");
            s.ReportScan("fastener_a");
            Assert.IsEmpty(unlocked, "扫描封条和螺丝不解锁线索");
            s.ReportScan("shell");
            CollectionAssert.AreEqual(new[] { "overwork_wear" }, unlocked);

            Assert.AreEqual(ActionOutcome.Blocked, s.PerformAction(RepairActionType.ServiceModule, "data_port"), "还不知道查什么时读不了日志");
            s.PerformAction(RepairActionType.RemoveFastener, "fastener_a");
            s.PerformAction(RepairActionType.RemoveFastener, "fastener_b");
            s.PerformAction(RepairActionType.OpenHousing, "shell");
            s.PerformAction(RepairActionType.ServiceModule, "control_board");
            s.PerformAction(RepairActionType.ServiceModule, "drive");
            Assert.AreEqual(1, unlocked.Count, "开壳、测控制板、测传动都不解锁新线索");

            s.PerformAction(RepairActionType.ServiceModule, "force_limiter");
            Assert.AreEqual("limiter_disabled", unlocked[1]);
            Assert.IsFalse(s.TryBeginDiagnosis(), "还差日志");

            Assert.AreEqual(ActionOutcome.Performed, s.PerformAction(RepairActionType.ServiceModule, "data_port"));
            CollectionAssert.AreEqual(new[] { "overwork_wear", "limiter_disabled", "remote_params" }, unlocked);
            CollectionAssert.AreEqual(unlocked, s.CurrentRecord.Clues);

            Assert.IsTrue(s.TryBeginDiagnosis());
            Assert.AreEqual(RepairStage.Decide, s.Stage, "叙事案件跳过对错诊断，直接进入处理决定");
        }

        // ---------- 结局 ----------

        [TestCase("restore_limits")]
        [TestCase("keep_params")]
        [TestCase("give_log")]
        public void EachEndingCompletesWithoutRightOrWrong(string endingId)
        {
            var s = NewSession(LoadCase());
            now = 5f;
            Investigate(s);
            Assert.IsFalse(s.SubmitDecision(RepairDecision.Repair), "叙事案件不接受“维修 / 更换 / 拒绝”判断");
            Assert.AreEqual(RepairStage.Decide, s.Stage);

            NarrativeEnding resolved = null;
            s.EndingResolved += e => resolved = e;
            now = 65f;
            Assert.IsTrue(s.SubmitEnding(endingId));
            Assert.AreEqual(RepairStage.Result, s.Stage);
            Assert.AreEqual(endingId, resolved.endingId);
            Assert.IsNotEmpty(resolved.costs, "每个结局都应写明代价");

            var r = s.CurrentRecord;
            Assert.IsFalse(r.IsJudged);
            Assert.IsTrue(r.IsFinished);
            Assert.IsFalse(r.DecisionCorrect);
            Assert.IsNull(r.Outcome);
            Assert.AreEqual(endingId, r.Ending.endingId);

            Assert.IsTrue(s.NextCase());
            Assert.AreEqual(RepairStage.Summary, s.Stage);
            Assert.AreEqual(0, s.JudgedCaseCount);
            Assert.AreEqual(0, s.CorrectDecisionCount);
        }

        [Test]
        public void UnknownEndingIsRejected()
        {
            var s = NewSession(LoadCase());
            Investigate(s);
            Assert.IsFalse(s.SubmitEnding("not_an_ending"));
            Assert.AreEqual(RepairStage.Decide, s.Stage);
        }

        // ---------- 与旧案件兼容 ----------

        [Test]
        public void JudgedCaseRejectsEndingsAndStillUsesDiagnosis()
        {
            var s = NewSession(MakeJudgedCase());
            s.AcceptItem();
            Assert.AreEqual(ActionOutcome.WrongTool, s.PerformAction(RepairActionType.RemoveFastener, "p1"), "没有维修步骤的旧案件不能拆卸");
            s.SetScanMode(true);
            s.ReportScan("p1");
            Assert.IsTrue(s.TryBeginDiagnosis());
            Assert.AreEqual(RepairStage.Diagnose, s.Stage, "判断类案件仍经过诊断阶段");
            Assert.IsTrue(s.SubmitDiagnosis("right"));
            Assert.IsFalse(s.SubmitEnding("restore_limits"), "判断类案件不接受叙事结局");
            Assert.AreEqual(ActionOutcome.Rejected, s.PerformAction(RepairActionType.RemoveFastener, "p1"), "决定阶段不能动手");
            Assert.IsTrue(s.SubmitDecision(RepairDecision.Repair));
            Assert.IsTrue(s.CurrentRecord.DecisionCorrect);
            Assert.IsTrue(s.CurrentRecord.IsJudged);
        }

        [Test]
        public void SummaryCountsExcludeNarrativeCase()
        {
            var s = NewSession(MakeJudgedCase(), LoadCase());
            s.AcceptItem();
            s.SetScanMode(true);
            s.ReportScan("p1");
            s.TryBeginDiagnosis();
            s.SubmitDiagnosis("right");
            s.SubmitDecision(RepairDecision.Repair);
            s.NextCase();

            Investigate(s);
            s.SubmitEnding("keep_params");
            s.NextCase();

            Assert.AreEqual(RepairStage.Summary, s.Stage);
            Assert.AreEqual(2, s.CaseCount);
            Assert.AreEqual(1, s.JudgedCaseCount, "叙事案件不计入判断类案件数");
            Assert.AreEqual(1, s.CorrectDecisionCount);
        }

        [Test]
        public void JudgedValidationIsUnchangedForOldCases()
        {
            var c = MakeJudgedCase();
            c.itemPrefab = new GameObject("dummy");
            created.Add(c.itemPrefab);
            var errors = new List<string>();
            Assert.IsTrue(c.Validate(errors), string.Join("\n", errors));
            c.outcomes[1].isCorrect = true;
            errors.Clear();
            Assert.IsFalse(c.Validate(errors), "判断类案件仍要求恰好一个正确决定");
        }
    }
}
