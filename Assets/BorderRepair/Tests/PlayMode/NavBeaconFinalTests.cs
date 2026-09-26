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
    static class NavBeaconTestUtil
    {
        const string ScenePath = "Assets/BorderRepair/Scenes/RepairStation_Prototype.unity";

        /// <summary>加载场景，并用正确操作处理完第一件通讯器，停在导航信标的接收阶段。</summary>
        public static IEnumerator LoadAndAdvanceToBeacon(System.Action<RepairStationController> onReady)
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("RepairStation_Prototype");
#endif
            yield return null;
            var c = Object.FindFirstObjectByType<RepairStationController>();
            Assert.IsNotNull(c);
            var s = c.Session;
            s.AcceptItem();
            s.ToggleScanMode();
            c.ScanPoint("antenna");
            Assert.IsTrue(s.TryBeginDiagnosis());
            Assert.IsTrue(s.SubmitDiagnosis(s.CurrentCase.correctDiagnosisId));
            Assert.IsTrue(s.SubmitDecision(RepairDecision.Repair));
            Assert.IsTrue(s.NextCase());
            yield return null;
            Assert.AreEqual("case_02_nav_beacon", s.CurrentCase.caseId);
            Assert.AreEqual(RepairStage.Intake, s.Stage);
            onReady(c);
        }

        public static InspectionPoint Point(RepairStationController c, string id)
        {
            Assert.IsTrue(c.TryGetPoint(id, out var p), $"缺少检查点 {id}");
            return p;
        }
    }

    /// <summary>正式导航信标：朝向、四个扫描点的屏幕拾取、旋转后立即拾取、“拒收并登记”流程。</summary>
    public class NavBeaconFinalTests
    {
        RepairStationController controller;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return NavBeaconTestUtil.LoadAndAdvanceToBeacon(c => controller = c);
        }

        InspectionPoint Point(string id) => NavBeaconTestUtil.Point(controller, id);

        /// <summary>
        /// 检查点的可见中心：用 Renderer.bounds（随 Transform 立即更新），不用 Collider.bounds（要等物理同步）。
        /// 这样“旋转后立即拾取”检验的是 ItemScanner 自己的同步逻辑，而不是测试替它同步。
        /// </summary>
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

        /// <summary>把检查点的可见中心投影到屏幕，再用 ItemScanner 的射线拾取；不等待下一帧。</summary>
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
        public IEnumerator FrontFacesCameraAndBackAfterRotation()
        {
            controller.Session.AcceptItem();
            yield return null;
            Assert.Less(CameraDistance("seal"), CameraDistance("port"), "接收时正面（封签）应朝向镜头");

            controller.Inspector.Rotate(new Vector2(-180f, 0f));
            controller.Inspector.SnapToTarget();
            yield return null;
            Assert.Less(CameraDistance("port"), CameraDistance("seal"), "旋转 180° 后背面（调试接口）应朝向镜头");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AllFourPointsPickableFromScreen()
        {
            controller.Session.AcceptItem();
            yield return null;
            AssertPickable("seal");
            AssertPickable("lamp");
            AssertPickable("mast");

            controller.Inspector.Rotate(new Vector2(-180f, 0f));
            controller.Inspector.SnapToTarget();
            yield return null;
            AssertPickable("port");
        }

        [UnityTest]
        public IEnumerator PickImmediatelyAfterRotation()
        {
            controller.Session.AcceptItem();
            yield return null;
            var inspector = controller.Inspector;

            // 旋转后不等下一帧、不等物理步，立即拾取
            inspector.Rotate(new Vector2(-180f, 0f));
            inspector.SnapToTarget();
            AssertPickable("port");

            inspector.Rotate(new Vector2(180f, 0f));
            inspector.SnapToTarget();
            AssertPickable("seal");

            inspector.Rotate(new Vector2(-90f, 25f));
            inspector.SnapToTarget();
            AssertPickable("lamp");
            yield return null;
        }

        [UnityTest]
        public IEnumerator CaseCompletesWithRefuseAndRegister()
        {
            var s = controller.Session;
            var data = s.CurrentCase;

            // 剧情证据方向：封签和接口是异常，灯组和天线是正常
            Assert.IsTrue(data.FindPoint("seal").requiredForDiagnosis);
            Assert.IsTrue(data.FindPoint("port").requiredForDiagnosis);
            Assert.IsFalse(data.FindPoint("lamp").requiredForDiagnosis);
            Assert.IsFalse(data.FindPoint("mast").requiredForDiagnosis);

            Assert.IsTrue(s.AcceptItem());
            Assert.IsTrue(s.ToggleScanMode());
            Assert.AreEqual(ScanOutcome.NewFinding, controller.ScanPoint("lamp"));
            Assert.AreEqual(ScanOutcome.NewFinding, controller.ScanPoint("mast"));
            Assert.IsFalse(s.TryBeginDiagnosis(), "只看了正常部位时不应允许诊断");
            Assert.AreEqual(ScanOutcome.NewFinding, controller.ScanPoint("seal"));
            Assert.AreEqual(ScanOutcome.NewFinding, controller.ScanPoint("port"));
            Assert.IsTrue(s.TryBeginDiagnosis());

            Assert.IsFalse(s.SubmitDiagnosis("normal_wear"));
            Assert.IsTrue(s.SubmitDiagnosis("tampered"));
            Assert.IsTrue(s.SubmitDecision(RepairDecision.Refuse));
            yield return null;

            Assert.AreEqual(RepairStage.Result, s.Stage);
            Assert.IsTrue(s.CurrentRecord.DecisionCorrect);
            Assert.AreEqual("拒收并登记", s.CurrentRecord.Outcome.resultTitle);
            Assert.AreEqual(1, s.CurrentRecord.WrongDiagnosisCount);

            Assert.IsTrue(s.NextCase());
            Assert.AreEqual("case_03_salvage_drone", s.CurrentCase.caseId);
        }
    }

    /// <summary>生成 AE-002 用的游戏内截图。Explicit：只有用 -testFilter 点名时才运行。</summary>
    [Explicit("仅用于生成截图")]
    public class NavBeaconCaptureTests
    {
        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "AssetEvaluation", "Screenshots"));

        RepairStationController controller;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return NavBeaconTestUtil.LoadAndAdvanceToBeacon(c => controller = c);
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

            yield return Shot(cam, rt, "06_unity_intake_front");

            s.AcceptItem();
            s.ToggleScanMode();
            controller.ScanPoint("seal");
            inspector.Rotate(new Vector2(12f, 20f));     // 玩家向上拖动：正面转向镜头
            inspector.Zoom(100f);
            inspector.SnapToTarget();
            yield return null;
            NavBeaconTestUtil.Point(controller, "seal").SetVisualState(InspectionPoint.VisualState.Normal);
            yield return Shot(cam, rt, "07_unity_seal_zoom");

            inspector.ResetView();
            inspector.Rotate(new Vector2(-180f, 20f));
            inspector.Zoom(100f);
            inspector.SnapToTarget();
            controller.ScanPoint("port");
            yield return null;
            NavBeaconTestUtil.Point(controller, "port").SetVisualState(InspectionPoint.VisualState.Normal);
            yield return Shot(cam, rt, "08_unity_port_zoom");

            s.TryBeginDiagnosis();
            s.SubmitDiagnosis("tampered");
            s.SubmitDecision(RepairDecision.Refuse);
            inspector.ResetView();
            inspector.Rotate(new Vector2(25f, 0f));
            inspector.SnapToTarget();
            yield return null;
            yield return Shot(cam, rt, "09_unity_refused_result");
            Assert.AreEqual(RepairStage.Result, s.Stage);
        }

        static IEnumerator Shot(Camera cam, RenderTexture rt, string name)
        {
            // 批处理模式下 WaitForEndOfFrame 不会返回，等两帧后手动渲染
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
            File.WriteAllBytes(Path.Combine(OutDir, $"20260924_NavBeacon_{name}.png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
