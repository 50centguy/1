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
    /// <summary>检查正式通讯器资产在检查台上的朝向、扫描碰撞和换件切换。</summary>
    public class CommunicatorFinalTests
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
            Assert.IsNotNull(controller);
            Assert.AreEqual("case_01_communicator", controller.Session.CurrentCase.caseId);
        }

        InspectionPoint Point(string id)
        {
            Assert.IsTrue(controller.TryGetPoint(id, out var p), $"缺少检查点 {id}");
            return p;
        }

        [UnityTest]
        public IEnumerator FrontFacesCameraAndPointsAreRaycastable()
        {
            controller.Session.AcceptItem();
            yield return null;
            var cam = controller.Inspector.ViewCamera;

            float screenDist = Vector3.Distance(cam.transform.position, Point("screen").GetComponent<Collider>().bounds.center);
            float batteryDist = Vector3.Distance(cam.transform.position, Point("battery").GetComponent<Collider>().bounds.center);
            Assert.Less(screenDist, batteryDist, "屏幕应朝向镜头（正面朝 -Z）");

            // 从镜头方向打向屏幕中心，最近的命中应属于 screen 检查点（不被机身碰撞体挡住）
            AssertRayHitsPoint(cam.transform.position, Point("screen"));
            AssertRayHitsPoint(cam.transform.position, Point("antenna"));

            // 旋转 180° 后电池仓应能被命中
            controller.Inspector.Rotate(new Vector2(-180f, 0f));
            controller.Inspector.SnapToTarget();
            yield return null;
            AssertRayHitsPoint(cam.transform.position, Point("battery"));
        }

        static void AssertRayHitsPoint(Vector3 origin, InspectionPoint point)
        {
            // 与 ItemScanner 相同：射线检测前同步物理变换
            Physics.SyncTransforms();
            var target = point.GetComponent<Collider>().bounds.center;
            var hits = Physics.RaycastAll(origin, (target - origin).normalized, 10f);
            float best = float.MaxValue;
            Collider nearest = null;
            foreach (var h in hits)
                if (h.distance < best) { best = h.distance; nearest = h.collider; }
            Assert.IsNotNull(nearest, $"射线没有命中任何物体（{point.PointId}）");
            Assert.AreEqual(point, nearest.GetComponentInParent<InspectionPoint>(), $"射线先命中了 {nearest.name}，而不是 {point.PointId}");
        }

        [UnityTest]
        public IEnumerator RepairSwapsAntennaVisualsAndStaysInFrame()
        {
            var s = controller.Session;
            var antenna = Point("antenna");
            var renderersBefore = CountActiveRenderers(antenna);

            s.AcceptItem();
            s.ToggleScanMode();
            controller.ScanPoint("antenna");
            Assert.IsTrue(s.TryBeginDiagnosis());
            Assert.IsTrue(s.SubmitDiagnosis(s.CurrentCase.correctDiagnosisId));
            Assert.IsTrue(s.SubmitDecision(RepairDecision.Repair));
            yield return null;

            Assert.IsTrue(antenna.IsRepaired);
            Assert.AreNotEqual(renderersBefore, CountActiveRenderers(antenna), "换件后天线外观应切换");

            // 拉到最远并复位视角，新天线顶端仍应在画面内
            var inspector = controller.Inspector;
            inspector.ResetView();
            inspector.SnapToTarget();
            var cam = inspector.ViewCamera;
            var b = antenna.GetComponent<Collider>().bounds;
            var top = cam.WorldToViewportPoint(new Vector3(b.center.x, b.max.y, b.center.z));
            Assert.IsTrue(top.y > 0f && top.y < 1f, $"新天线顶端超出画面（viewport y = {top.y:0.00}）");
        }

        static int CountActiveRenderers(InspectionPoint p)
        {
            int n = 0;
            foreach (var r in p.GetComponentsInChildren<Renderer>(false)) n++;
            return n;
        }
    }

    /// <summary>生成资产评价用的游戏内截图。标记为 Explicit，只有用 -testFilter 点名时才运行。</summary>
    [Explicit("仅用于生成截图")]
    public class CommunicatorCaptureTests
    {
        const string ScenePath = "Assets/BorderRepair/Scenes/RepairStation_Prototype.unity";
        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "AssetEvaluation", "Screenshots"));

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
            Assert.IsNotNull(controller);
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

            yield return Shot(cam, rt, "05_unity_intake_front");

            s.AcceptItem();
            s.ToggleScanMode();
            controller.ScanPoint("screen");
            controller.ScanPoint("battery");
            inspector.Rotate(new Vector2(-160f, 12f));
            inspector.SnapToTarget();
            yield return null;
            Point("battery").SetVisualState(InspectionPoint.VisualState.Hover);
            yield return Shot(cam, rt, "06_unity_back_scan_battery");

            inspector.ResetView();
            inspector.Rotate(new Vector2(25f, -20f));
            inspector.Zoom(100f);
            inspector.SnapToTarget();
            yield return null;
            yield return Shot(cam, rt, "07_unity_damage_closeup");

            controller.ScanPoint("antenna");
            s.TryBeginDiagnosis();
            s.SubmitDiagnosis(s.CurrentCase.correctDiagnosisId);
            s.SubmitDecision(RepairDecision.Repair);
            inspector.ResetView();
            inspector.Rotate(new Vector2(20f, 0f));
            inspector.SnapToTarget();
            yield return null;
            yield return Shot(cam, rt, "08_unity_repaired_result");
            Assert.AreEqual(RepairStage.Result, s.Stage);
        }

        InspectionPoint Point(string id)
        {
            controller.TryGetPoint(id, out var p);
            return p;
        }

        static IEnumerator Shot(Camera cam, RenderTexture rt, string name)
        {
            // 批处理模式下 WaitForEndOfFrame 不会返回，这里等两帧后手动渲染
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
            File.WriteAllBytes(Path.Combine(OutDir, $"20260924_Communicator_{name}.png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
