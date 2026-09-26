using System.Collections.Generic;
using BorderRepair.Core;
using BorderRepair.Data;
using NUnit.Framework;
using UnityEngine;

namespace BorderRepair.Tests
{
    public class RepairSessionTests
    {
        readonly List<Object> created = new List<Object>();
        float now;

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        RepairCaseData MakeCase(string id, RepairDecision correct, params string[] requiredPoints)
        {
            var c = ScriptableObject.CreateInstance<RepairCaseData>();
            created.Add(c);
            c.caseId = id;
            c.itemName = id;
            foreach (var p in requiredPoints)
                c.inspectionPoints.Add(new InspectionPointInfo { pointId = p, displayName = p, finding = "bad", requiredForDiagnosis = true });
            c.inspectionPoints.Add(new InspectionPointInfo { pointId = "ok_part", displayName = "ok", finding = "fine" });
            c.diagnosisOptions.Add(new DiagnosisOption { optionId = "right", label = "right" });
            c.diagnosisOptions.Add(new DiagnosisOption { optionId = "wrong", label = "wrong", wrongFeedback = "nope" });
            c.correctDiagnosisId = "right";
            foreach (RepairDecision d in System.Enum.GetValues(typeof(RepairDecision)))
                c.outcomes.Add(new DecisionOutcome { decision = d, isCorrect = d == correct, resultTitle = d.ToString(), reason = "r" });
            return c;
        }

        RepairSession MakeSession(params RepairCaseData[] cases)
        {
            var shift = ScriptableObject.CreateInstance<RepairShiftData>();
            created.Add(shift);
            shift.cases = new List<RepairCaseData>(cases);
            var session = new RepairSession(shift, () => now);
            session.StartShift();
            return session;
        }

        static void ScanAllRequired(RepairSession s)
        {
            Assert.IsTrue(s.SetScanMode(true));
            foreach (var p in s.CurrentCase.inspectionPoints)
                if (p.requiredForDiagnosis) Assert.AreEqual(ScanOutcome.NewFinding, s.ReportScan(p.pointId));
        }

        [Test]
        public void StartsInIntakeWithFirstCase()
        {
            var s = MakeSession(MakeCase("a", RepairDecision.Repair, "p1"));
            Assert.AreEqual(RepairStage.Intake, s.Stage);
            Assert.AreEqual("a", s.CurrentCase.caseId);
            Assert.IsFalse(s.ScanModeActive);
        }

        [Test]
        public void CannotSkipSteps()
        {
            var s = MakeSession(MakeCase("a", RepairDecision.Repair, "p1"));
            var messages = new List<string>();
            s.Feedback += (m, k) => messages.Add(m);

            Assert.IsFalse(s.SetScanMode(true), "接收前不能扫描");
            Assert.IsFalse(s.TryBeginDiagnosis(), "接收前不能诊断");
            Assert.IsFalse(s.SubmitDecision(RepairDecision.Repair), "接收前不能做决定");
            Assert.IsFalse(s.NextCase(), "接收前不能跳到下一件");
            Assert.AreEqual(RepairStage.Intake, s.Stage);
            Assert.AreEqual(4, messages.Count, "每次非法操作都应有反馈");

            Assert.IsTrue(s.AcceptItem());
            Assert.IsFalse(s.SubmitDiagnosis("right"), "检查阶段不能直接提交诊断");
            Assert.IsFalse(s.SubmitDecision(RepairDecision.Repair));
            Assert.AreEqual(RepairStage.Inspect, s.Stage);
        }

        [Test]
        public void ScanRequiresScanModeAndDiagnosisRequiresAllKeyFindings()
        {
            var s = MakeSession(MakeCase("a", RepairDecision.Repair, "p1", "p2"));
            s.AcceptItem();

            Assert.AreEqual(ScanOutcome.Rejected, s.ReportScan("p1"), "未开扫描模式时扫描应被拒绝");
            Assert.IsTrue(s.SetScanMode(true));
            Assert.AreEqual(ScanOutcome.NewFinding, s.ReportScan("p1"));
            Assert.AreEqual(ScanOutcome.AlreadyScanned, s.ReportScan("p1"));
            Assert.AreEqual(ScanOutcome.UnknownPoint, s.ReportScan(null));
            Assert.AreEqual(ScanOutcome.NewFinding, s.ReportScan("ok_part"));
            Assert.AreEqual(1, s.FoundRequiredCount);

            Assert.IsFalse(s.TryBeginDiagnosis(), "关键异常未找齐时不能诊断");
            Assert.AreEqual(RepairStage.Inspect, s.Stage);

            s.ReportScan("p2");
            Assert.IsTrue(s.TryBeginDiagnosis());
            Assert.AreEqual(RepairStage.Diagnose, s.Stage);
            Assert.IsFalse(s.ScanModeActive, "离开检查阶段应关闭扫描");
        }

        [Test]
        public void WrongDiagnosisGivesFeedbackAndStays()
        {
            var s = MakeSession(MakeCase("a", RepairDecision.Repair, "p1"));
            s.AcceptItem();
            ScanAllRequired(s);
            s.TryBeginDiagnosis();

            string last = null;
            s.Feedback += (m, k) => last = m;
            Assert.IsFalse(s.SubmitDiagnosis("wrong"));
            Assert.AreEqual("nope", last);
            Assert.AreEqual(RepairStage.Diagnose, s.Stage);
            Assert.AreEqual(1, s.CurrentRecord.WrongDiagnosisCount);

            Assert.IsTrue(s.ReturnToInspection());
            Assert.AreEqual(RepairStage.Inspect, s.Stage);
            Assert.IsTrue(s.TryBeginDiagnosis(), "已找到的发现在返回检查后保留");
            Assert.IsTrue(s.SubmitDiagnosis("right"));
            Assert.AreEqual(RepairStage.Decide, s.Stage);
        }

        [Test]
        public void WrongDecisionStillEndsCase()
        {
            var s = MakeSession(MakeCase("a", RepairDecision.Refuse, "p1"), MakeCase("b", RepairDecision.Repair, "p1"));
            s.AcceptItem();
            ScanAllRequired(s);
            s.TryBeginDiagnosis();
            s.SubmitDiagnosis("right");

            DecisionOutcome resolved = null;
            s.DecisionResolved += o => resolved = o;
            Assert.IsTrue(s.SubmitDecision(RepairDecision.Repair));
            Assert.AreEqual(RepairStage.Result, s.Stage);
            Assert.IsNotNull(resolved);
            Assert.IsFalse(resolved.isCorrect);
            Assert.IsFalse(s.CurrentRecord.DecisionCorrect);

            Assert.IsTrue(s.NextCase());
            Assert.AreEqual(RepairStage.Intake, s.Stage);
            Assert.AreEqual("b", s.CurrentCase.caseId);
            Assert.AreEqual(0, s.FoundRequiredCount, "新案例的扫描记录应清空");
        }

        [Test]
        public void FullShiftReachesSummaryAndRestartResets()
        {
            var s = MakeSession(
                MakeCase("a", RepairDecision.Repair, "p1"),
                MakeCase("b", RepairDecision.Refuse, "p1", "p2"),
                MakeCase("c", RepairDecision.RecommendReplacement, "p1", "p2"));
            now = 10f;
            s.StartShift();

            var decisions = new[] { RepairDecision.Repair, RepairDecision.Refuse, RepairDecision.RecommendReplacement };
            for (int i = 0; i < 3; i++)
            {
                now += 20f;
                Assert.IsTrue(s.AcceptItem());
                ScanAllRequired(s);
                Assert.IsTrue(s.TryBeginDiagnosis());
                Assert.IsTrue(s.SubmitDiagnosis("right"));
                Assert.IsTrue(s.SubmitDecision(decisions[i]));
                Assert.AreEqual(i == 2, s.IsLastCase);
                Assert.IsTrue(s.NextCase());
            }

            Assert.AreEqual(RepairStage.Summary, s.Stage);
            Assert.AreEqual(3, s.CorrectDecisionCount);
            Assert.AreEqual(0, s.TotalWrongDiagnoses);
            Assert.AreEqual(60f, s.ShiftDuration, 0.001f);
            Assert.IsFalse(s.NextCase(), "总结页不能再进入下一件");

            s.Restart();
            Assert.AreEqual(RepairStage.Intake, s.Stage);
            Assert.AreEqual(0, s.CaseIndex);
            Assert.AreEqual(1, s.Records.Count);
        }

        [Test]
        public void ValidateDetectsBrokenCaseData()
        {
            var c = MakeCase("a", RepairDecision.Repair, "p1");
            var errors = new List<string>();
            c.itemPrefab = new GameObject("dummy");
            created.Add(c.itemPrefab);
            Assert.IsTrue(c.Validate(errors), string.Join("\n", errors));

            c.correctDiagnosisId = "missing";
            c.outcomes[1].isCorrect = true;
            c.outcomes[0].performsPartReplacement = true;
            errors.Clear();
            Assert.IsFalse(c.Validate(errors));
            Assert.GreaterOrEqual(errors.Count, 3);
        }
    }
}
