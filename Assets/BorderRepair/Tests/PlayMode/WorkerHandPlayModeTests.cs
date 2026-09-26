using System.Collections;
using System.IO;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Inspection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.Tests
{
    static class WorkerHandTestUtil
    {
        public const string NarrativeScene = "Assets/BorderRepair/Scenes/Narrative_WorkerHand.unity";
        public const string DefaultScene = "Assets/BorderRepair/Scenes/RepairStation_Prototype.unity";

        public static IEnumerator Load(string path, System.Action<RepairStationController> onReady)
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync(Path.GetFileNameWithoutExtension(path));
#endif
            yield return null;
            var c = Object.FindFirstObjectByType<RepairStationController>();
            Assert.IsNotNull(c);
            Assert.IsNotNull(c.Session, "控制器未初始化");
            onReady(c);
        }

        public static InspectionPoint Point(RepairStationController c, string id)
        {
            Assert.IsTrue(c.TryGetPoint(id, out var p), $"缺少检查点 {id}");
            return p;
        }

        public static Vector3 VisualCenter(InspectionPoint p)
        {
            var rs = p.GetComponentsInChildren<Renderer>(false);
            Assert.IsNotEmpty(rs, $"{p.PointId} 没有可见网格");
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.center;
        }

        /// <summary>把世界坐标投影到屏幕，经 ItemScanner（与鼠标单击相同的射线）拾取检查点。</summary>
        public static string PickAt(RepairStationController c, Vector3 world)
        {
            var screen = c.Inspector.ViewCamera.WorldToScreenPoint(world);
            Assert.Greater(screen.z, 0f);
            var p = c.Scanner.PickPoint(screen, out _);
            return p != null ? p.PointId : null;
        }

        /// <summary>螺丝 A 上半边被封条压住，瞄准露在外面的下半边。</summary>
        public static Vector3 FastenerALowerHalf(RepairStationController c)
        {
            var item = c.Inspector.CurrentItem.transform;
            return Point(c, "fastener_a").transform.position + item.TransformVector(new Vector3(0f, -0.0032f, 0f));
        }

        /// <summary>按剧情完成调查（经 API），停在决定阶段。</summary>
        public static void Investigate(RepairStationController c)
        {
            var s = c.Session;
            Assert.IsTrue(s.AcceptItem());
            Assert.IsTrue(s.SetScanMode(true));
            Assert.AreEqual(ScanOutcome.NewFinding, c.ScanPoint("shell"));
            Assert.AreEqual(ActionOutcome.Performed, c.UseTool(RepairActionType.RemoveFastener, "fastener_a"));
            Assert.AreEqual(ActionOutcome.Performed, c.UseTool(RepairActionType.RemoveFastener, "fastener_b"));
            Assert.AreEqual(ActionOutcome.Performed, c.UseTool(RepairActionType.OpenHousing, "shell"));
            Assert.AreEqual(ActionOutcome.Performed, c.UseTool(RepairActionType.ServiceModule, "force_limiter"));
            Assert.AreEqual(ActionOutcome.Performed, c.UseTool(RepairActionType.ServiceModule, "data_port"));
            Assert.IsTrue(s.TryBeginDiagnosis());
            Assert.AreEqual(RepairStage.Decide, s.Stage);
        }
    }

    /// <summary>叙事竖切场景：工具拆卸（屏幕拾取）、吸附位置、线索与三种结局、总结页不计入判断。</summary>
    public class WorkerHandPlayModeTests
    {
        RepairStationController controller;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return WorkerHandTestUtil.Load(WorkerHandTestUtil.NarrativeScene, c => controller = c);
        }

        [UnityTest]
        public IEnumerator SceneUsesNarrativeShiftAndToolbar()
        {
            var s = controller.Session;
            Assert.AreEqual(1, s.CaseCount);
            Assert.AreEqual("case_n01_worker_prosthetic", s.CurrentCase.caseId);
            Assert.IsNotNull(controller.Toolbar, "叙事场景应有工具栏");
            Assert.IsTrue(controller.Toolbar.transform.GetChild(0).gameObject.activeSelf, "带维修步骤的案件应显示工具栏");

            controller.SelectTool(RepairActionType.RemoveFastener);
            Assert.IsNull(controller.ActiveTool, "接收物品前不能拿工具");
            s.AcceptItem();
            yield return null;
            controller.SelectTool(RepairActionType.RemoveFastener);
            Assert.AreEqual(RepairActionType.RemoveFastener, controller.ActiveTool);
            Assert.IsTrue(controller.Scanner.ScanModeActive, "拿着工具时应有悬停高亮与点击拾取");
            Assert.IsFalse(s.ScanModeActive, "工具与扫描互斥");
            s.ToggleScanMode();
            Assert.IsNull(controller.ActiveTool, "开启扫描后放下工具");
        }

        [UnityTest]
        public IEnumerator DisassemblyThroughScreenPicksAndSnapPositions()
        {
            var s = controller.Session;
            s.AcceptItem();
            yield return null;

            // 开壳前：瞄准限力传感器，射线先打到盖板
            var limiterPos = WorkerHandTestUtil.VisualCenter(WorkerHandTestUtil.Point(controller, "force_limiter"));
            Assert.AreEqual("shell", WorkerHandTestUtil.PickAt(controller, limiterPos), "盖板应挡住内部零件");

            controller.SelectTool(RepairActionType.RemoveFastener);
            string a = WorkerHandTestUtil.PickAt(controller, WorkerHandTestUtil.FastenerALowerHalf(controller));
            Assert.AreEqual("fastener_a", a);
            Assert.AreEqual(ActionOutcome.Performed, controller.UseTool(controller.ActiveTool.Value, a));
            string b = WorkerHandTestUtil.PickAt(controller, WorkerHandTestUtil.VisualCenter(WorkerHandTestUtil.Point(controller, "fastener_b")));
            Assert.AreEqual("fastener_b", b);

            controller.SelectTool(RepairActionType.OpenHousing);
            Assert.AreEqual(ActionOutcome.Blocked, controller.UseTool(RepairActionType.OpenHousing, "shell"), "还剩一颗螺丝");
            controller.SelectTool(RepairActionType.RemoveFastener);
            Assert.AreEqual(ActionOutcome.Performed, controller.UseTool(controller.ActiveTool.Value, b));

            controller.SelectTool(RepairActionType.OpenHousing);
            yield return new WaitForSeconds(1.2f);   // 等螺丝吸附到零件盘，避免它们挡住盖板
            string shell = WorkerHandTestUtil.PickAt(controller, WorkerHandTestUtil.VisualCenter(WorkerHandTestUtil.Point(controller, "shell")));
            Assert.AreEqual("shell", shell);
            Assert.AreEqual(ActionOutcome.Performed, controller.UseTool(controller.ActiveTool.Value, shell));
            yield return new WaitForSeconds(1.5f);

            foreach (var id in new[] { "shell", "fastener_a", "fastener_b" })
            {
                Assert.IsTrue(controller.TryGetPart(id, out var part));
                Assert.IsTrue(part.IsAtTarget, $"{id} 应已到位");
                Assert.Less(Vector3.Distance(part.transform.position, part.SnapTarget.position), 0.001f, $"{id} 应吸附到零件盘");
            }
            Assert.IsTrue(controller.TryGetPart("lease_seal", out var seal));
            Assert.AreEqual(RepairPartState.Torn, seal.State);

            // 开壳后：内部零件可以直接拾取
            limiterPos = WorkerHandTestUtil.VisualCenter(WorkerHandTestUtil.Point(controller, "force_limiter"));
            Assert.AreEqual("force_limiter", WorkerHandTestUtil.PickAt(controller, limiterPos));
            controller.SelectTool(RepairActionType.ServiceModule);
            Assert.AreEqual(ActionOutcome.Performed, controller.UseTool(controller.ActiveTool.Value, "force_limiter"));
            Assert.IsTrue(controller.TryGetPart("force_limiter", out var limiter));
            Assert.AreEqual(RepairPartState.Tested, limiter.State);
            Assert.IsTrue(s.HasClue("limiter_disabled"));
        }

        IEnumerator PlayToEnding(string endingId)
        {
            WorkerHandTestUtil.Investigate(controller);
            yield return null;
            var s = controller.Session;
            Assert.AreEqual(3, controller.View.EndingButtonCount, "决定阶段应显示三个结局按钮");
            Assert.IsFalse(s.SubmitDecision(RepairDecision.Repair));
            Assert.IsTrue(s.SubmitEnding(endingId));
            yield return null;
            StringAssert.Contains("结局", controller.View.ResultTitle);
            StringAssert.DoesNotContain("判断", controller.View.ResultTitle, "叙事结局不显示判断正确/失误");
            Assert.IsTrue(s.NextCase());
            yield return null;
            Assert.AreEqual(RepairStage.Summary, s.Stage);
            StringAssert.DoesNotContain("判断正确", controller.View.SummaryText, "叙事案件不计入判断正确");
            StringAssert.Contains("叙事案件", controller.View.SummaryText);
            StringAssert.Contains(s.CurrentCase.FindEnding(endingId).resultTitle, controller.View.SummaryText);
        }

        [UnityTest] public IEnumerator EndingRestoreLimits() => PlayToEnding("restore_limits");
        [UnityTest] public IEnumerator EndingKeepParams() => PlayToEnding("keep_params");
        [UnityTest] public IEnumerator EndingGiveLog() => PlayToEnding("give_log");
    }

    /// <summary>默认营业场景回归：没有工具栏、三件物品顺序不变、总结仍显示“判断正确 3/3”。</summary>
    public class DefaultShiftRegressionTests
    {
        RepairStationController controller;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return WorkerHandTestUtil.Load(WorkerHandTestUtil.DefaultScene, c => controller = c);
        }

        [UnityTest]
        public IEnumerator DefaultShiftStillJudgedAndOrdered()
        {
            var s = controller.Session;
            Assert.IsNull(controller.Toolbar, "默认场景不应有维修工具栏");
            Assert.AreEqual(3, s.CaseCount);
            string[] order = { "case_01_communicator", "case_02_nav_beacon", "case_03_salvage_drone" };
            for (int i = 0; i < 3; i++)
            {
                var data = s.CurrentCase;
                Assert.AreEqual(order[i], data.caseId);
                Assert.AreEqual(0, controller.View.EndingButtonCount);
                s.AcceptItem();
                s.ToggleScanMode();
                foreach (var p in data.inspectionPoints) controller.ScanPoint(p.pointId);
                Assert.IsTrue(s.TryBeginDiagnosis());
                Assert.AreEqual(RepairStage.Diagnose, s.Stage);
                Assert.IsTrue(s.SubmitDiagnosis(data.correctDiagnosisId));
                Assert.IsTrue(s.SubmitDecision(data.outcomes.Find(o => o.isCorrect).decision));
                StringAssert.Contains("判断正确", controller.View.ResultTitle);
                Assert.IsTrue(s.NextCase());
                yield return null;
            }
            StringAssert.Contains("判断正确：<b>3 / 3</b>", controller.View.SummaryText);
            StringAssert.DoesNotContain("叙事", controller.View.SummaryText);
        }
    }
}
