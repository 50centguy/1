using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Inspection;
using BorderRepair.Narrative;
using BorderRepair.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Cue = BorderRepair.Narrative.RepairSoundSynth.Cue;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BorderRepair.Tests
{
    static class PolishUtil
    {
        /// <summary>
        /// 用 1920×1080 离屏渲染代替 batchmode 下的默认窗口尺寸，界面切到 ScreenSpaceCamera，
        /// 这样取景和面板遮挡的判断与 16:9 全高清屏幕一致。
        /// </summary>
        public static RenderTexture UseFullHd(RepairStationController c)
        {
            var cam = c.Inspector.ViewCamera;
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            var canvas = c.View.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 0.01f;
            Canvas.ForceUpdateCanvases();
            return rt;
        }

        public static Bounds VisibleBounds(InspectionPoint p)
        {
            var rs = p.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled).ToArray();
            Assert.IsNotEmpty(rs, $"{p.PointId} 没有可见网格");
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>部位包围盒在屏幕上的矩形（像素）。</summary>
        public static Rect ScreenRect(Camera cam, Bounds b)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var s = cam.WorldToScreenPoint(c);
                x0 = Mathf.Min(x0, s.x); y0 = Mathf.Min(y0, s.y); x1 = Mathf.Max(x1, s.x); y1 = Mathf.Max(y1, s.y);
            }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        /// <summary>界面元素在屏幕上的矩形（像素）。</summary>
        public static Rect ScreenRect(Camera cam, RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var a = cam.WorldToScreenPoint(corners[0]);
            var b = cam.WorldToScreenPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static float Overlap(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0 && h > 0 ? w * h : 0f;
        }

        /// <summary>当前显示的常驻面板（顾客说明、线索、工具栏）遮住部位屏幕矩形的比例。</summary>
        public static float PanelCoverage(RepairStationController c, Rect part, out string worst)
        {
            var cam = c.Inspector.ViewCamera;
            var canvas = c.View.transform;
            var panels = new List<RectTransform>
            {
                canvas.Find("CustomerPanel") as RectTransform,
                canvas.Find("FindingsPanel") as RectTransform,
                c.Toolbar != null ? c.Toolbar.transform.Find("Panel") as RectTransform : null,
            };
            float area = Mathf.Max(1f, part.width * part.height), max = 0f;
            worst = "-";
            foreach (var p in panels)
            {
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                float f = Overlap(part, ScreenRect(cam, p)) / area;
                if (f > max) { max = f; worst = p.name; }
            }
            return max;
        }

        public static IEnumerator Settle(ItemInspector insp, float maxSeconds = 2f)
        {
            // 等镜头平滑移动到位：连续几帧位置变化很小即可
            var cam = insp.ViewCamera.transform;
            var last = cam.position;
            int still = 0;
            float t = 0f;
            while (t < maxSeconds && still < 4)
            {
                yield return null;
                t += Time.deltaTime;
                still = (cam.position - last).sqrMagnitude < 1e-9f ? still + 1 : 0;
                last = cam.position;
            }
        }
    }

    /// <summary>工人义手打磨：分阶段取景、面板收起、操作反馈、场景级磨损材质。</summary>
    public class WorkerHandPolishPlayModeTests
    {
        RepairStationController controller;
        NarrativeStageFraming framing;
        RepairFeedbackFx fx;
        NarrativeHud hud;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return WorkerHandTestUtil.Load(WorkerHandTestUtil.NarrativeScene, c => controller = c);
            framing = Object.FindFirstObjectByType<NarrativeStageFraming>();
            fx = Object.FindFirstObjectByType<RepairFeedbackFx>();
            hud = Object.FindFirstObjectByType<NarrativeHud>();
            Assert.IsNotNull(framing, "叙事场景缺少分阶段取景");
            Assert.IsNotNull(fx, "叙事场景缺少操作反馈");
            Assert.IsNotNull(hud, "叙事场景缺少面板布局");
        }

        [UnityTest]
        public IEnumerator EachOperationCentresItsPartAndPanelsStayClear()
        {
            var rt = PolishUtil.UseFullHd(controller);
            var cam = controller.Inspector.ViewCamera;
            var s = controller.Session;
            s.AcceptItem();
            yield return null;

            // （工具，期望取景的零件，取景后要执行的步骤）
            var plan = new (RepairActionType tool, string point)[]
            {
                (RepairActionType.RemoveFastener, "fastener_a"),
                (RepairActionType.RemoveFastener, "fastener_b"),
                (RepairActionType.OpenHousing, "shell"),
                (RepairActionType.ServiceModule, "force_limiter"),
                (RepairActionType.ServiceModule, "data_port"),
            };
            var report = new StringBuilder();
            foreach (var (tool, point) in plan)
            {
                controller.SelectTool(tool);
                yield return new WaitForSeconds(1.3f);          // 上一步的停留结束后才转向下一个零件
                yield return PolishUtil.Settle(controller.Inspector);
                Assert.AreEqual(point, framing.CurrentShotId, $"拿着{RepairActionText.Label(tool)}时应对准 {point}");

                var p = WorkerHandTestUtil.Point(controller, point);
                var rect = PolishUtil.ScreenRect(cam, PolishUtil.VisibleBounds(p));
                var centre = new Vector2(rect.center.x / cam.pixelWidth, rect.center.y / cam.pixelHeight);
                float size = Mathf.Max(rect.width / cam.pixelWidth, rect.height / cam.pixelHeight);
                float cover = PolishUtil.PanelCoverage(controller, rect, out var worst);
                report.AppendLine($"{point}: 中心 ({centre.x:0.00}, {centre.y:0.00}) 大小 {size:0.00} 遮挡 {cover:P0}（{worst}）");
                Assert.Less(Mathf.Abs(centre.x - 0.5f), 0.08f, $"{point} 应在画面水平中央：{report}");
                Assert.Less(Mathf.Abs(centre.y - 0.5f), 0.1f, $"{point} 应在画面垂直中央：{report}");
                Assert.Greater(size, 0.04f, $"{point} 在画面上太小：{report}");
                Assert.Less(size, 0.9f, $"{point} 放得过大：{report}");
                Assert.Less(cover, 0.02f, $"{point} 被面板 {worst} 遮住：{report}");

                // 用工具处理这个零件（与鼠标单击同一流程）
                Assert.AreEqual(ActionOutcome.Performed, controller.UseTool(tool, point), report.ToString());
                Assert.AreEqual(point, framing.CurrentShotId, "刚完成的一步应先停在这个零件上");
            }
            Debug.Log("[WorkerHandPolish] 取景\n" + report);

            // 其他阶段看整件物品（线索 1 要扫描外壳才能拿到）
            s.SetScanMode(true);
            Assert.AreEqual(ScanOutcome.NewFinding, controller.ScanPoint("shell"));
            Assert.IsTrue(s.TryBeginDiagnosis(), "三条线索齐了应能进入决定阶段");
            yield return null;
            Assert.AreEqual(RepairStage.Decide, s.Stage);
            Assert.AreEqual(NarrativeStageFraming.Overview, framing.CurrentShotId);
            controller.Inspector.SnapToTarget();
            Assert.Less(Vector3.Distance(controller.Inspector.LookPoint, controller.Inspector.CurrentItem.transform.parent.position), 1e-4f,
                "整件物品取景应对准物品中心");
            cam.targetTexture = null;
            rt.Release();
        }

        [UnityTest]
        public IEnumerator HandToolLooksAtWholeItemAndResetReturnsToShot()
        {
            var s = controller.Session;
            s.AcceptItem();
            controller.SelectTool(RepairActionType.RemoveFastener);
            yield return null;
            Assert.AreEqual("fastener_a", framing.CurrentShotId);
            var insp = controller.Inspector;
            insp.SnapToTarget();
            var framed = insp.ViewCamera.transform.position;

            insp.Rotate(new Vector2(40f, 20f));                 // 玩家手动旋转后，R 回到当前取景
            insp.SnapToTarget();
            Assert.Greater(Vector3.Distance(framed, insp.ViewCamera.transform.position), 1e-3f);
            insp.ResetView(true);
            Assert.Less(Vector3.Distance(framed, insp.ViewCamera.transform.position), 1e-4f, "R 应回到当前零件的取景");

            controller.SelectTool(null);                          // 空手：看整件物品
            yield return null;
            Assert.AreEqual(NarrativeStageFraming.Overview, framing.CurrentShotId);
            insp.SnapToTarget();
            Assert.Less(Vector3.Distance(insp.LookPoint, insp.CurrentItem.transform.parent.position), 1e-4f, "空手时对准物品中心");
        }

        [UnityTest]
        public IEnumerator CustomerNoteCollapsesAfterAcceptAndCluesStayReachable()
        {
            var s = controller.Session;
            var customer = hud.CustomerPanel;
            var clues = hud.CluePanel;
            Assert.IsTrue(customer.Expanded, "接收阶段应显示顾客说明");
            Assert.IsFalse(clues.Expanded);

            s.AcceptItem();
            yield return null;
            Assert.IsFalse(customer.Expanded, "接收后顾客说明应自动收起");
            Assert.Less(customer.Panel.sizeDelta.y, 80f);
            customer.Toggle();
            Assert.IsTrue(customer.Expanded, "收起后仍可以再展开");
            StringAssert.Contains("罗亚", controller.View.transform.Find("CustomerPanel/CustomerText").GetComponent<UnityEngine.UI.Text>().text);
            customer.Toggle();

            s.SetScanMode(true);
            controller.ScanPoint("shell");                        // 解锁线索 1
            Assert.IsTrue(hud.HasUnreadClue, "新线索应有未读标记");
            StringAssert.Contains("新线索", clues.ToggleText);
            var findingsText = controller.View.transform.Find("FindingsPanel/FindingsText");
            Assert.IsFalse(findingsText.gameObject.activeSelf, "收起时不显示线索正文");

            clues.Toggle();                                        // 与点击按钮相同
            Assert.IsTrue(clues.Expanded);
            Assert.IsTrue(findingsText.gameObject.activeSelf);
            StringAssert.Contains("外壳过度磨损", findingsText.GetComponent<UnityEngine.UI.Text>().text);
            Assert.IsFalse(hud.HasUnreadClue, "展开后清除未读标记");

            // 收起状态下标题栏仍显示线索进度
            clues.Toggle();
            StringAssert.Contains("线索 1/3", controller.View.transform.Find("FindingsPanel/ScanStatus").GetComponent<UnityEngine.UI.Text>().text);
        }

        [UnityTest]
        public IEnumerator OperationsPlayDistinctFeedback()
        {
            var s = controller.Session;
            s.AcceptItem();
            Assert.IsTrue(controller.TryGetPart("fastener_a", out var screwA));

            Assert.AreEqual(ActionOutcome.Blocked, controller.UseTool(RepairActionType.OpenHousing, "shell"));
            Assert.AreEqual(Cue.Deny, fx.LastCue, "被拒绝的操作应有提示音");

            Assert.AreEqual(ActionOutcome.Performed, controller.UseTool(RepairActionType.RemoveFastener, "fastener_a"));
            Assert.AreEqual(Cue.Unscrew, fx.LastCue);
            Assert.IsTrue(screwA.IsPlayingExit, "螺丝应先转出来再落到零件盘");
            yield return null;
            Assert.Greater(fx.ActiveRings, 0, "应显示扩散环");
            yield return new WaitForSeconds(0.3f);
            CollectionAssert.Contains(fx.CueLog, "SealTear@lease_seal", "卸螺丝 A 撕开封条应有撕纸声");

            controller.UseTool(RepairActionType.RemoveFastener, "fastener_b");
            controller.UseTool(RepairActionType.OpenHousing, "shell");
            Assert.AreEqual(Cue.OpenCover, fx.LastCue);
            Assert.IsTrue(controller.TryGetPart("shell", out var shell));
            Assert.IsTrue(shell.IsPlayingExit, "盖板应先弹起再放到零件盘");

            controller.UseTool(RepairActionType.ServiceModule, "force_limiter");
            Assert.AreEqual(Cue.ProbeWarning, fx.LastCue);
            Assert.IsTrue(controller.TryGetPart("force_limiter", out var limiter));
            // 红灯接触后立刻常亮，至少 1 秒内不熄灭（“旁路”线索要看得清）
            for (float t = 0f; t < 1.1f; t += Time.deltaTime)
            {
                Assert.IsTrue(limiter.TestedIndicator.activeSelf, $"红灯在检测后 {t:0.00} 秒熄灭了");
                yield return null;
            }
            yield return new WaitForSeconds(1.2f);
            Assert.IsTrue(limiter.TestedIndicator.activeSelf, "闪烁结束后红灯常亮");

            controller.UseTool(RepairActionType.ServiceModule, "data_port");
            Assert.AreEqual(Cue.DataRead, fx.LastCue);

            var expected = new[] { "Deny", "Unscrew@fastener_a", "SealTear@lease_seal", "Unscrew@fastener_b", "OpenCover@shell", "ProbeWarning@force_limiter", "DataRead@data_port" };
            CollectionAssert.AreEqual(expected, fx.CueLog.ToArray());

            // 零件最终仍吸附到零件盘（离位动作不改变结果位置）
            yield return new WaitForSeconds(1.5f);
            foreach (var id in new[] { "fastener_a", "fastener_b", "shell" })
            {
                Assert.IsTrue(controller.TryGetPart(id, out var part));
                Assert.IsTrue(part.IsAtTarget, id);
                Assert.Less(Vector3.Distance(part.transform.position, part.SnapTarget.position), 0.001f, id);
            }
        }

        /// <summary>
        /// 叙事案件现在用 v2 正式模型（自带地下诊所 lookdev 材质），场景级换材质对它关闭；
        /// 占位 prefab 保留作回退，换材质组件对占位模型仍然有效（切回占位时外观与上一版相同）。
        /// </summary>
        [UnityTest]
        public IEnumerator V2UsesItsOwnMaterialsAndPlaceholderSkinStillWorks()
        {
            yield return null;
            var item = controller.Inspector.CurrentItem;
            var names = item.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).ToList();
            foreach (var m in new[] { "M_Hand_Enamel", "M_Hand_Decal", "M_Hand_BayLiner", "M_Hand_Steel" })
                CollectionAssert.Contains(names, m, $"v2 义手应使用 {m}");
            Assert.IsFalse(names.Any(n => n.StartsWith("M_Worn_") || n == "M_plastic_grey" || n == "M_metal_light"), "v2 不应再用占位或 ShaderLab 材质");
            var skin = Object.FindFirstObjectByType<ItemMaterialSkin>(FindObjectsInactive.Include);
            Assert.IsFalse(skin.enabled, "v2 下场景级换材质应关闭");
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BorderRepair/Prefabs/Items/Narrative/Item_WorkerProsthetic_Placeholder.prefab");
            Assert.IsNotNull(prefab, "旧占位 prefab 应保留，供回退");
            Assert.IsFalse(prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Any(m => m != null && m.name.StartsWith("M_Worn_")),
                "prefab 资产不应被改动");
            var copy = Object.Instantiate(prefab);
            skin.Apply(copy);
            Assert.Greater(skin.LastSwapCount, 10, "切回占位 prefab 时，换材质仍然有效");
            Object.Destroy(copy);
#endif
            // 磨损材质接受场景光照：主光不是唯一光源，工作台主光、补光、反射探针都在
            Assert.IsNotNull(GameObject.Find("BenchKeySpot"));
            Assert.IsNotNull(GameObject.Find("BenchFillLight"));
            Assert.IsNotNull(Object.FindFirstObjectByType<ReflectionProbe>());
        }
    }

    /// <summary>默认营业场景：没有任何打磨组件，镜头不使用零件焦点。</summary>
    public class DefaultScenePolishRegressionTests
    {
        [UnityTest]
        public IEnumerator DefaultSceneHasNoFramingFxOrCollapsiblePanels()
        {
            RepairStationController c = null;
            yield return WorkerHandTestUtil.Load(WorkerHandTestUtil.DefaultScene, x => c = x);
            Assert.IsNull(Object.FindFirstObjectByType<NarrativeStageFraming>());
            Assert.IsNull(Object.FindFirstObjectByType<RepairFeedbackFx>());
            Assert.IsNull(Object.FindFirstObjectByType<NarrativeHud>());
            Assert.IsNull(Object.FindFirstObjectByType<ItemMaterialSkin>());
            Assert.IsNull(Object.FindFirstObjectByType<CollapsiblePanel>());
            Assert.IsNull(GameObject.Find("NarrativePolish"));

            var insp = c.Inspector;
            c.Session.AcceptItem();
            yield return null;
            Assert.IsFalse(insp.HasFocus);
            insp.ResetView(true);
            // 与原来的公式相同：镜头在锚点沿初始视线方向、默认距离处
            var anchor = insp.CurrentItem.transform.parent.position;
            Assert.Less(Vector3.Distance(insp.LookPoint, anchor), 1e-5f);
            Assert.AreEqual(insp.TargetDistance, Vector3.Distance(insp.ViewCamera.transform.position, anchor), 1e-4f);
            Assert.AreEqual(insp.MinDistance, insp.LowerDistanceLimit, 1e-6f);
        }
    }

    /// <summary>
    /// 叙事竖切各阶段截图：只用游戏自己的镜头（自动取景，测试代码不手动旋转、缩放），1920×1080。
    /// 另外在同一机位下对比“工作台照明开 / 关”时封条、螺丝、线路、接口区域的亮度与对比度。
    /// Explicit：只有用 -testFilter 点名时才运行。这是 batchmode 下代码驱动的截图，不是试玩。
    /// </summary>
    [Explicit("仅用于生成截图")]
    public class WorkerHandCaptureTests
    {
        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Narrative", "Screenshots"));
        static string MetricsPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Narrative", "polish_batchmode_metrics.txt"));
        RepairStationController controller;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return WorkerHandTestUtil.Load(WorkerHandTestUtil.NarrativeScene, c => controller = c);
        }

        [UnityTest]
        public IEnumerator CaptureStages()
        {
            Directory.CreateDirectory(OutDir);
            var rt = PolishUtil.UseFullHd(controller);
            var cam = controller.Inspector.ViewCamera;
            var insp = controller.Inspector;
            var s = controller.Session;
            var framing = Object.FindFirstObjectByType<NarrativeStageFraming>();
            var metrics = new StringBuilder();
            metrics.AppendLine("工人义手打磨 · batchmode 截图附带的数值（1920×1080，游戏镜头自动取景）");
            metrics.AppendLine();

            // 切换界面渲染方式后多等几帧，让字体贴图按新分辨率重建
            for (int i = 0; i < 5; i++) yield return null;
            yield return PolishUtil.Settle(insp);
            yield return Shot(cam, rt, "01_intake");

            s.AcceptItem();
            s.SetScanMode(true);
            controller.ScanPoint("shell");
            yield return PolishUtil.Settle(insp);
            yield return Shot(cam, rt, "02_scan_shell_overview");

            controller.SelectTool(RepairActionType.RemoveFastener);
            yield return PolishUtil.Settle(insp);
            yield return Shot(cam, rt, "03_screwdriver_frames_fastener_a");
            Frame(metrics, cam, "fastener_a");
            yield return LightingCompare(metrics, cam, rt, new[] { "lease_seal", "fastener_a", "fastener_b" });

            controller.UseTool(RepairActionType.RemoveFastener, "fastener_a");
            yield return new WaitForSeconds(0.2f);
            yield return Shot(cam, rt, "04_fastener_a_feedback");
            yield return new WaitForSeconds(0.7f);
            yield return Shot(cam, rt, "05_fastener_a_removed_seal_torn");

            yield return new WaitForSeconds(0.6f);
            yield return PolishUtil.Settle(insp);
            yield return Shot(cam, rt, "06_frames_fastener_b");
            Frame(metrics, cam, "fastener_b");
            controller.UseTool(RepairActionType.RemoveFastener, "fastener_b");
            yield return new WaitForSeconds(1.2f);

            controller.SelectTool(RepairActionType.OpenHousing);
            yield return PolishUtil.Settle(insp);
            yield return Shot(cam, rt, "07_pry_frames_shell");
            Frame(metrics, cam, "shell");
            controller.UseTool(RepairActionType.OpenHousing, "shell");
            yield return new WaitForSeconds(0.15f);
            yield return Shot(cam, rt, "08_housing_open_feedback");
            yield return new WaitForSeconds(1.2f);

            controller.SelectTool(RepairActionType.ServiceModule);
            yield return PolishUtil.Settle(insp);
            yield return Shot(cam, rt, "09_tester_frames_limiter");
            Frame(metrics, cam, "force_limiter");
            yield return LightingCompare(metrics, cam, rt, new[] { "force_limiter", "drive", "control_board" });
            controller.UseTool(RepairActionType.ServiceModule, "force_limiter");
            yield return new WaitForSeconds(0.25f);
            yield return Shot(cam, rt, "10_limiter_feedback");
            yield return new WaitForSeconds(0.8f);
            yield return Shot(cam, rt, "11_limiter_bypassed_red");

            yield return new WaitForSeconds(0.4f);
            yield return PolishUtil.Settle(insp);
            Assert.AreEqual("data_port", framing.CurrentShotId);
            yield return Shot(cam, rt, "12_frames_data_port");
            Frame(metrics, cam, "data_port");
            yield return LightingCompare(metrics, cam, rt, new[] { "data_port" });
            controller.UseTool(RepairActionType.ServiceModule, "data_port");
            yield return new WaitForSeconds(0.3f);
            yield return Shot(cam, rt, "13_log_read_feedback");
            yield return new WaitForSeconds(1.2f);

            var hud = Object.FindFirstObjectByType<NarrativeHud>();
            hud.CluePanel.SetExpanded(true);
            yield return Shot(cam, rt, "14_clues_expanded");
            hud.CluePanel.SetExpanded(false);

            s.TryBeginDiagnosis();
            yield return PolishUtil.Settle(insp);
            yield return Shot(cam, rt, "15_decide_endings");
            s.SubmitEnding("restore_limits");
            yield return Shot(cam, rt, "16_result_restore_limits");
            s.NextCase();
            yield return Shot(cam, rt, "17_summary");
            Assert.AreEqual(RepairStage.Summary, s.Stage);

            File.WriteAllText(MetricsPath, metrics.ToString(), new UTF8Encoding(true));
            Debug.Log("[WorkerHandPolish] " + metrics);
            cam.targetTexture = null;
            rt.Release();
        }

        void Frame(StringBuilder log, Camera cam, string pointId)
        {
            var p = WorkerHandTestUtil.Point(controller, pointId);
            var rect = PolishUtil.ScreenRect(cam, PolishUtil.VisibleBounds(p));
            float cover = PolishUtil.PanelCoverage(controller, rect, out var worst);
            log.AppendLine($"取景 {pointId}：中心 ({rect.center.x / cam.pixelWidth:0.00}, {rect.center.y / cam.pixelHeight:0.00})，" +
                           $"包围盒 {rect.width:0}×{rect.height:0} 像素，面板遮挡 {cover:P0}（{worst}）");
        }

        /// <summary>同一机位、同一帧：工作台主光 / 补光 / 反射探针开与关，比较各部位区域的平均亮度和对比度（亮度标准差）。</summary>
        IEnumerator LightingCompare(StringBuilder log, Camera cam, RenderTexture rt, string[] pointIds)
        {
            var bench = GameObject.Find("BenchLighting");
            Assert.IsNotNull(bench);
            var on = Render(cam, rt);
            bench.SetActive(false);
            yield return null;
            var off = Render(cam, rt);
            bench.SetActive(true);
            yield return null;
            log.AppendLine($"照明对比（同一机位；左：关闭工作台照明，右：开启）");
            foreach (var id in pointIds)
            {
                var rect = PolishUtil.ScreenRect(cam, PolishUtil.VisibleBounds(WorkerHandTestUtil.Point(controller, id)));
                var (m0, c0) = Stats(off, rt.width, rt.height, rect);
                var (m1, c1) = Stats(on, rt.width, rt.height, rect);
                log.AppendLine($"  {id}：平均亮度 {m0:0} → {m1:0}，对比度（亮度标准差）{c0:0.0} → {c1:0.0}");
            }
            log.AppendLine();
        }

        static Color32[] Render(Camera cam, RenderTexture rt)
        {
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            Object.Destroy(tex);
            return px;
        }

        static (double mean, double std) Stats(Color32[] px, int w, int h, Rect r)
        {
            int x0 = Mathf.Clamp((int)r.xMin, 0, w - 1), x1 = Mathf.Clamp((int)r.xMax, x0 + 1, w);
            int y0 = Mathf.Clamp((int)r.yMin, 0, h - 1), y1 = Mathf.Clamp((int)r.yMax, y0 + 1, h);
            double s = 0, ss = 0; int n = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var c = px[y * w + x];
                    double l = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                    s += l; ss += l * l; n++;
                }
            double m = s / n;
            return (m, System.Math.Sqrt(System.Math.Max(0, ss / n - m * m)));
        }

        static IEnumerator Shot(Camera cam, RenderTexture rt, string name)
        {
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            var px = Render(cam, rt);
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, $"20260924_WorkerHandPolish_{name}.png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
