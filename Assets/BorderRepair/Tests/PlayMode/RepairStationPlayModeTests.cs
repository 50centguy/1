using System.Collections;
using BorderRepair.Core;
using BorderRepair.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.Tests
{
    /// <summary>加载生成的原型场景，用控制器 API 把三件物品完整走一遍（不模拟真实鼠标）。</summary>
    public class RepairStationPlayModeTests
    {
        const string ScenePath = "Assets/BorderRepair/Scenes/RepairStation_Prototype.unity";

        RepairStationController controller;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("RepairStation_Prototype");
#endif
            yield return null;
            controller = Object.FindFirstObjectByType<RepairStationController>();
            Assert.IsNotNull(controller, "场景中没有 RepairStationController");
            Assert.IsNotNull(controller.Session, "控制器未初始化（检查引用是否缺失）");
        }

        [UnityTest]
        public IEnumerator PlaysAllThreeCasesToSummaryAndRestarts()
        {
            var s = controller.Session;
            Assert.AreEqual(RepairStage.Intake, s.Stage);
            Assert.AreEqual(3, s.CaseCount);

            for (int i = 0; i < s.CaseCount; i++)
            {
                var data = s.CurrentCase;
                Assert.IsNotNull(controller.Inspector.CurrentItem, $"{data.caseId}: 物品未放上工作台");
                Assert.IsFalse(controller.Inspector.InteractionEnabled, "接收前不应允许旋转");

                Assert.IsTrue(s.AcceptItem());
                yield return null;
                Assert.IsTrue(controller.Inspector.InteractionEnabled);

                Assert.IsFalse(s.TryBeginDiagnosis(), "未扫描就诊断应被拒绝");
                Assert.AreEqual(ScanOutcome.Rejected, controller.ScanPoint(data.inspectionPoints[0].pointId), "未开扫描模式时应被拒绝");

                Assert.IsTrue(s.ToggleScanMode());
                Assert.IsTrue(controller.Scanner.ScanModeActive);
                foreach (var p in data.inspectionPoints)
                    Assert.AreEqual(ScanOutcome.NewFinding, controller.ScanPoint(p.pointId), $"{data.caseId}: 扫描 {p.pointId}");

                Assert.IsTrue(s.TryBeginDiagnosis());
                yield return null;

                var wrong = data.diagnosisOptions.Find(o => o.optionId != data.correctDiagnosisId);
                Assert.IsFalse(s.SubmitDiagnosis(wrong.optionId));
                Assert.IsTrue(s.SubmitDiagnosis(data.correctDiagnosisId));

                var correct = data.outcomes.Find(o => o.isCorrect);
                Assert.IsTrue(s.SubmitDecision(correct.decision));
                Assert.AreEqual(RepairStage.Result, s.Stage);
                Assert.IsTrue(s.CurrentRecord.DecisionCorrect);

                if (correct.performsPartReplacement)
                {
                    Assert.IsTrue(controller.TryGetPoint(data.replacementPointId, out var part));
                    Assert.IsTrue(part.IsRepaired, "换件维修后部件应显示为新件");
                }
                yield return null;
                Assert.IsTrue(s.NextCase());
                yield return null;
                if (i < s.CaseCount - 1)
                    Assert.IsNull(controller.View.LastFeedback, "进入下一件时不应保留上一件的提示");
            }

            Assert.AreEqual(RepairStage.Summary, s.Stage);
            Assert.AreEqual(3, s.CorrectDecisionCount);
            Assert.AreEqual(3, s.TotalWrongDiagnoses);

            s.Restart();
            yield return null;
            Assert.AreEqual(RepairStage.Intake, s.Stage);
            Assert.AreEqual(0, s.CaseIndex);
            Assert.IsNotNull(controller.Inspector.CurrentItem);
            Assert.IsNull(controller.View.LastFeedback, "重新开始时不应保留上一轮的提示");
        }

        [UnityTest]
        public IEnumerator ZoomAndRotationStayWithinLimits()
        {
            controller.Session.AcceptItem();
            yield return null;

            var inspector = controller.Inspector;
            var cam = inspector.ViewCamera;
            var anchor = inspector.CurrentItem.transform.parent;

            inspector.Zoom(1000f);
            inspector.SnapToTarget();
            float near = Vector3.Distance(cam.transform.position, anchor.position);
            Assert.AreEqual(inspector.MinDistance, near, 0.001f);
            Assert.Greater(near - inspector.ItemRadius, cam.nearClipPlane, "拉到最近时物品不应穿过近裁剪面");

            inspector.Zoom(-1000f);
            inspector.SnapToTarget();
            float far = Vector3.Distance(cam.transform.position, anchor.position);
            Assert.AreEqual(inspector.MaxDistance, far, 0.001f);
            Vector3 vp = cam.WorldToViewportPoint(anchor.position);
            Assert.IsTrue(vp.z > 0 && vp.x > 0 && vp.x < 1 && vp.y > 0 && vp.y < 1, "拉到最远时物品仍应在画面中");

            inspector.Rotate(new Vector2(0f, 10000f));
            inspector.SnapToTarget();
            Assert.LessOrEqual(Mathf.Abs(inspector.Pitch), 75.001f, "俯仰角应被限制");
            yield return null;
        }
    }
}
