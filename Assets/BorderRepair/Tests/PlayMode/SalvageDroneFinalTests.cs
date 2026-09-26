using System.Collections;
using System.IO;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Inspection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BorderRepair.Tests
{
    static class SalvageDroneTestUtil
    {
        /// <summary>用正确操作处理完通讯器和导航信标，停在回收无人机的接收阶段。</summary>
        public static IEnumerator LoadAndAdvanceToDrone(System.Action<RepairStationController> onReady)
        {
            RepairStationController c = null;
            yield return NavBeaconTestUtil.LoadAndAdvanceToBeacon(x => c = x);
            var s = c.Session;
            s.AcceptItem();
            s.ToggleScanMode();
            c.ScanPoint("seal");
            c.ScanPoint("port");
            Assert.IsTrue(s.TryBeginDiagnosis());
            Assert.IsTrue(s.SubmitDiagnosis("tampered"));
            Assert.IsTrue(s.SubmitDecision(RepairDecision.Refuse));
            Assert.IsTrue(s.NextCase());
            yield return null;
            Assert.AreEqual("case_03_salvage_drone", s.CurrentCase.caseId);
            Assert.AreEqual(RepairStage.Intake, s.Stage);
            onReady(c);
        }
    }

    /// <summary>正式回收无人机：朝向、四个扫描点的屏幕拾取、旋转后立即拾取、“建议更换或拆件回收”流程。</summary>
    public class SalvageDroneFinalTests
    {
        RepairStationController controller;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return SalvageDroneTestUtil.LoadAndAdvanceToDrone(c => controller = c);
        }

        InspectionPoint Point(string id) => NavBeaconTestUtil.Point(controller, id);

        /// <summary>用 Renderer.bounds（随 Transform 立即更新）取可见中心，不依赖物理同步。</summary>
        Vector3 VisualCenter(string id)
        {
            var renderers = Point(id).GetComponentsInChildren<Renderer>(false);
            Assert.IsNotEmpty(renderers, $"{id} 没有可见网格");
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b.center;
        }

        float CameraDistance(string id) =>
            Vector3.Distance(controller.Inspector.ViewCamera.transform.position, VisualCenter(id));

        void AssertPickable(string id)
        {
            var cam = controller.Inspector.ViewCamera;
            var screen = cam.WorldToScreenPoint(VisualCenter(id));
            Assert.Greater(screen.z, 0f, $"{id} 在镜头后面");
            var picked = controller.Scanner.PickPoint(screen, out bool hitItem);
            Assert.IsTrue(hitItem, $"{id}：射线没有命中物品");
            Assert.IsNotNull(picked, $"{id}：射线命中了物品表面，但不是任何检查点");
            Assert.AreEqual(id, picked.PointId, $"拾取 {id} 时先命中了 {picked.PointId}");
        }

        [UnityTest]
        public IEnumerator NoseFacesCameraAndTailAfterRotation()
        {
            controller.Session.AcceptItem();
            yield return null;
            Assert.Less(CameraDistance("camera"), CameraDistance("rotor"), "接收时机头（云台相机）应朝向镜头");
            Assert.Less(CameraDistance("motor_fl"), CameraDistance("rotor"), "左前电机应比右后桨叶更靠近镜头");

            controller.Inspector.Rotate(new Vector2(-180f, 0f));
            controller.Inspector.SnapToTarget();
            yield return null;
            Assert.Less(CameraDistance("rotor"), CameraDistance("motor_fl"), "旋转 180° 后机尾（右后桨叶）应朝向镜头");
            Assert.Less(CameraDistance("rotor"), CameraDistance("camera"));
        }

        [UnityTest]
        public IEnumerator AllFourPointsPickableFromScreen()
        {
            controller.Session.AcceptItem();
            yield return null;
            AssertPickable("motor_fl");
            AssertPickable("mainboard");
            AssertPickable("rotor");
            AssertPickable("camera");
        }

        [UnityTest]
        public IEnumerator PickImmediatelyAfterRotation()
        {
            controller.Session.AcceptItem();
            yield return null;
            var inspector = controller.Inspector;

            // 旋转后不等下一帧、不等物理步，立即拾取。
            // 机尾朝向镜头时，向后翻起的舱盖和电池挡住电路板（设计如此），这时只检查右后桨叶。
            inspector.Rotate(new Vector2(-180f, 0f));
            inspector.SnapToTarget();
            AssertPickable("rotor");

            inspector.Rotate(new Vector2(180f, 0f));
            inspector.SnapToTarget();
            AssertPickable("motor_fl");
            AssertPickable("camera");
            AssertPickable("mainboard");
            yield return null;
        }

        [UnityTest]
        public IEnumerator CaseCompletesWithRecommendReplacement()
        {
            var s = controller.Session;
            var data = s.CurrentCase;

            // 剧情证据方向：电机、主控板为异常；桨叶、相机为正常
            Assert.IsTrue(data.FindPoint("motor_fl").requiredForDiagnosis);
            Assert.IsTrue(data.FindPoint("mainboard").requiredForDiagnosis);
            Assert.IsFalse(data.FindPoint("rotor").requiredForDiagnosis);
            Assert.IsFalse(data.FindPoint("camera").requiredForDiagnosis);
            Assert.Greater(data.estimatedRepairCost, data.repairCostLimit, "维修费应超过顾客上限");

            Assert.IsTrue(s.AcceptItem());
            Assert.IsTrue(s.ToggleScanMode());
            Assert.AreEqual(ScanOutcome.NewFinding, controller.ScanPoint("rotor"));
            Assert.AreEqual(ScanOutcome.NewFinding, controller.ScanPoint("camera"));
            Assert.IsFalse(s.TryBeginDiagnosis(), "只看了正常部位时不应允许诊断");
            Assert.AreEqual(ScanOutcome.NewFinding, controller.ScanPoint("motor_fl"));
            Assert.IsFalse(s.TryBeginDiagnosis(), "只找到电机、没看主控板时不应允许诊断");
            Assert.AreEqual(ScanOutcome.NewFinding, controller.ScanPoint("mainboard"));
            Assert.IsTrue(s.TryBeginDiagnosis());

            Assert.IsFalse(s.SubmitDiagnosis("single_motor"));
            Assert.IsTrue(s.SubmitDiagnosis("multi_damage"));
            Assert.IsTrue(s.SubmitDecision(RepairDecision.RecommendReplacement));
            yield return null;

            Assert.AreEqual(RepairStage.Result, s.Stage);
            Assert.IsTrue(s.CurrentRecord.DecisionCorrect);
            Assert.AreEqual("建议更换或拆件回收", s.CurrentRecord.Outcome.resultTitle);
            Assert.IsTrue(s.IsLastCase);
            Assert.IsTrue(s.NextCase());
            Assert.AreEqual(RepairStage.Summary, s.Stage);
            Assert.AreEqual(3, s.CorrectDecisionCount);
        }
    }

    /// <summary>生成 AE-003 用的游戏内截图。Explicit：只有用 -testFilter 点名时才运行。</summary>
    [Explicit("仅用于生成截图")]
    public class SalvageDroneCaptureTests
    {
        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "AssetEvaluation", "Screenshots"));

        RepairStationController controller;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return SalvageDroneTestUtil.LoadAndAdvanceToDrone(c => controller = c);
        }

        [UnityTest]
        public IEnumerator CaptureUnityShots()
        {
            Directory.CreateDirectory(OutDir);
            var cam = controller.Inspector.ViewCamera;
            var canvas = controller.View.GetComponent<Canvas>();
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 0.01f;
            var s = controller.Session;
            var inspector = controller.Inspector;

            yield return Shot(cam, rt, "07_unity_intake_front");

            s.AcceptItem();
            s.ToggleScanMode();
            controller.ScanPoint("motor_fl");
            inspector.Rotate(new Vector2(45f, 20f));       // 把左前臂转到镜头正前方（-45° 会转成右前臂）
            inspector.Zoom(100f);
            inspector.SnapToTarget();
            yield return null;
            NavBeaconTestUtil.Point(controller, "motor_fl").SetVisualState(InspectionPoint.VisualState.Normal);
            yield return Shot(cam, rt, "08_unity_motor_zoom");

            controller.ScanPoint("mainboard");
            inspector.ResetView();
            inspector.Rotate(new Vector2(0f, -15f));
            inspector.Zoom(100f);
            inspector.SnapToTarget();
            yield return null;
            NavBeaconTestUtil.Point(controller, "mainboard").SetVisualState(InspectionPoint.VisualState.Normal);
            yield return Shot(cam, rt, "09_unity_mainboard_zoom");

            s.TryBeginDiagnosis();
            s.SubmitDiagnosis("multi_damage");
            yield return Shot(cam, rt, "10_unity_decide_cost");

            s.SubmitDecision(RepairDecision.RecommendReplacement);
            inspector.ResetView();
            inspector.Rotate(new Vector2(30f, 0f));
            inspector.SnapToTarget();
            yield return null;
            yield return Shot(cam, rt, "11_unity_result");
            Assert.AreEqual(RepairStage.Result, s.Stage);
        }

        static IEnumerator Shot(Camera cam, RenderTexture rt, string name)
        {
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(OutDir, $"20260924_SalvageDrone_{name}.png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
