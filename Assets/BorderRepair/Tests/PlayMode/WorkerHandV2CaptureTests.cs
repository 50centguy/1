using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Inspection;
using BorderRepair.Tools;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace BorderRepair.Tests
{
    /// <summary>
    /// 工人义手 v2 + 工具动画的游戏镜头截图（1920×1080，自动取景，测试代码不手动转镜头）：
    /// 接收、卸螺丝（进入 / 对准 / 接触 / 退出）、开盖、探测、读日志，以及接触时刻工具 / 手套遮挡关键线索的比例、渲染统计。
    /// Explicit：只有用 -testFilter 点名时才运行。这是 batchmode 下代码驱动的截图，不是试玩。
    /// </summary>
    [Explicit("仅用于生成截图")]
    public class WorkerHandV2CaptureTests
    {
        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Narrative", "Screenshots"));
        static string MetricsPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Narrative", "v2_toolanim_batchmode_metrics.txt"));

        RepairStationController controller;
        RepairToolAnimator anim;
        RenderTexture rt;
        Camera cam;
        StringBuilder log;
        ProfilerRecorder batches, drawCalls, setPass, tris;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return WorkerHandTestUtil.Load(WorkerHandTestUtil.NarrativeScene, c => controller = c);
            anim = controller.ToolAnimator;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            batches.Dispose(); drawCalls.Dispose(); setPass.Dispose(); tris.Dispose();
            if (cam != null) cam.targetTexture = null;
            if (rt != null) rt.Release();
            yield return null;
        }

        [UnityTest]
        public IEnumerator CaptureStagesAndMeasure()
        {
            Directory.CreateDirectory(OutDir);
            rt = PolishUtil.UseFullHd(controller);
            cam = controller.Inspector.ViewCamera;
            var s = controller.Session;
            log = new StringBuilder();
            log.AppendLine("工人义手 v2 · 工具动画 batchmode 截图附带的数值（1920×1080，游戏镜头自动取景）");
            log.AppendLine($"GPU: {SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceType}");
            log.AppendLine();
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");

            for (int i = 0; i < 5; i++) yield return null;
            yield return PolishUtil.Settle(controller.Inspector);
            yield return Shot("01_intake");
            yield return Stats("接收");
            // 对照：同一帧设置下把义手隐藏，看义手本身占多少
            var item = controller.Inspector.CurrentItem;
            int itemRenderers = item.GetComponentsInChildren<Renderer>(false).Length;
            int sceneRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length;
            int shadowLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.isActiveAndEnabled && l.shadows != LightShadows.None);
            log.AppendLine($"场景可见渲染器 {sceneRenderers} 个，其中义手 {itemRenderers} 个；投影的灯 {shadowLights} 盏");
            item.SetActive(false);
            yield return Stats("对照：隐藏义手");
            item.SetActive(true);

            s.AcceptItem();
            s.SetScanMode(true);
            controller.ScanPoint("shell");
            s.SetScanMode(false);

            // 卸螺丝 A：四个阶段各一张
            controller.SelectTool(RepairActionType.RemoveFastener);
            yield return PolishUtil.Settle(controller.Inspector);
            Assert.IsTrue(controller.ClickPoint("fastener_a"));
            yield return WaitPhaseShot(RepairToolAnimator.Phase.Enter, 0.12f, "02a_screwdriver_enter");
            yield return WaitPhaseShot(RepairToolAnimator.Phase.Align, 0.08f, "02b_screwdriver_align");
            yield return WaitContactShot(0.25f, "02c_screwdriver_contact_unscrewing", new[] { "lease_seal" });
            yield return Stats("卸螺丝（接触中，含螺丝刀 + 手套）");
            yield return WaitPhaseShot(RepairToolAnimator.Phase.Exit, 0.12f, "02d_screwdriver_exit");
            yield return ToolAnimUtil.WaitIdle(anim);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("02e_fastener_a_removed_seal_torn");

            yield return new WaitForSeconds(1.0f);
            yield return PolishUtil.Settle(controller.Inspector);
            Assert.IsTrue(controller.ClickPoint("fastener_b"));
            yield return WaitContactShot(0.25f, "03_screwdriver_contact_fastener_b", new[] { "lease_seal" });
            yield return ToolAnimUtil.WaitIdle(anim);
            yield return new WaitForSeconds(0.8f);

            // 开盖
            controller.SelectTool(RepairActionType.OpenHousing);
            yield return PolishUtil.Settle(controller.Inspector);
            Assert.IsTrue(controller.ClickPoint("shell"));
            yield return WaitPhaseShot(RepairToolAnimator.Phase.Align, 0.1f, "04a_pry_align_seam");
            yield return WaitContactShot(0.2f, "04b_pry_lever_cover_pops", new[] { "lease_seal" });
            yield return Stats("开盖（接触中）");
            yield return ToolAnimUtil.WaitIdle(anim);
            yield return new WaitForSeconds(0.9f);
            yield return Shot("04c_housing_open_bay_visible");

            // 探测限力器
            controller.SelectTool(RepairActionType.ServiceModule);
            yield return new WaitForSeconds(0.4f);
            yield return PolishUtil.Settle(controller.Inspector);
            Assert.IsTrue(controller.ClickPoint("force_limiter"));
            // 发光增强在接触后 0.6 秒内回落：0.45 秒时红灯已稳定亮着、探针仍在接触
            yield return WaitContactShot(0.45f, "05_probe_contact_limiter_red", new[] { "force_limiter" }, requireLimiterLit: true);
            yield return Stats("探测（接触中）");
            yield return new WaitForSeconds(0.75f);
            Assert.IsTrue(controller.TryGetPart("force_limiter", out var lim) && lim.TestedIndicator.activeInHierarchy, "探针离开后红灯仍常亮");
            yield return Shot("05b_limiter_red_steady_after_probe");
            yield return ToolAnimUtil.WaitIdle(anim);

            // 读日志
            yield return new WaitForSeconds(1.2f);
            yield return PolishUtil.Settle(controller.Inspector);
            Assert.AreEqual("data_port", Object.FindFirstObjectByType<BorderRepair.Narrative.NarrativeStageFraming>().CurrentShotId);
            Assert.IsTrue(controller.ClickPoint("data_port"));
            yield return WaitContactShot(0.4f, "06_plug_inserted_reading_log", new[] { "data_port" });
            yield return Stats("读日志（插头接入）");
            yield return ToolAnimUtil.WaitIdle(anim);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("07_log_read_tools_withdrawn");
            Assert.IsTrue(s.HasClue("remote_params"));

            File.WriteAllText(MetricsPath, log.ToString(), new UTF8Encoding(true));
            Debug.Log("[WorkerHandV2] " + log);
        }

        IEnumerator WaitPhaseShot(RepairToolAnimator.Phase phase, float after, string name)
        {
            yield return ToolAnimUtil.WaitPhase(anim, phase);
            yield return new WaitForSeconds(after);
            yield return Shot(name);
        }

        /// <summary>等到接触时刻（会话已执行），再过 after 秒截图；同一帧再渲染一次不带工具的画面，计算关键线索被遮挡的比例。</summary>
        IEnumerator WaitContactShot(float after, string name, string[] cluePoints, bool requireLimiterLit = false)
        {
            float t = 0f;
            while (anim.LastContactOutcome == null && t < 4f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(ActionOutcome.Performed, anim.LastContactOutcome, name);
            yield return new WaitForSeconds(after);
            if (requireLimiterLit)
                Assert.IsTrue(controller.TryGetPart("force_limiter", out var lit) && lit.TestedIndicator.activeInHierarchy, $"{name}：截图这一帧红灯必须亮着");
            yield return null;
            Canvas.ForceUpdateCanvases();
            var withTools = Render();
            Save(withTools, name);
            var rig = anim.ActiveRig;
            rig.gameObject.SetActive(false);
            var without = Render();
            rig.gameObject.SetActive(true);
            foreach (var id in cluePoints)
            {
                Assert.IsTrue(controller.TryGetPoint(id, out var p));
                var rect = PolishUtil.ScreenRect(cam, PolishUtil.VisibleBounds(p));
                float covered = Covered(withTools, without, rect);
                log.AppendLine($"{name}：线索 {id} 屏幕区域 {rect.width:0}×{rect.height:0} 像素，被工具 / 手套遮挡 {covered:P1}");
            }
            // 限力器指示灯与旁路标签单独算（探测、读日志时）
            // 用“有工具 / 无工具”的差异衡量遮挡，指示灯此刻是否在闪烁的亮相都不影响结果
            if (controller.TryGetPart("force_limiter", out var limiter) && limiter.State == RepairPartState.Tested && limiter.TestedIndicator != null)
            {
                // 两次渲染已经完成：临时显示指示物只为取得它的包围盒，不影响截图
                bool was = limiter.TestedIndicator.activeSelf;
                limiter.TestedIndicator.SetActive(true);
                foreach (var r in limiter.TestedIndicator.GetComponentsInChildren<Renderer>(true))
                {
                    var rect = PolishUtil.ScreenRect(cam, r.bounds);
                    log.AppendLine($"{name}：{r.name}（红灯 / 旁路标签）被遮挡 {Covered(withTools, without, rect):P1}");
                }
                limiter.TestedIndicator.SetActive(was);
            }
        }

        static float Covered(Color32[] a, Color32[] b, Rect r)
        {
            int x0 = Mathf.Clamp((int)r.xMin, 0, 1919), x1 = Mathf.Clamp((int)r.xMax, x0 + 1, 1920);
            int y0 = Mathf.Clamp((int)r.yMin, 0, 1079), y1 = Mathf.Clamp((int)r.yMax, y0 + 1, 1080);
            int n = 0, changed = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var p = a[y * 1920 + x]; var q = b[y * 1920 + x];
                    n++;
                    if (Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b) > 45) changed++;
                }
            return n > 0 ? changed / (float)n : 0f;
        }

        IEnumerator Stats(string label)
        {
            long b = 0, d = 0, sp = 0, tr = 0;
            for (int i = 0; i < 8; i++)
            {
                yield return null;
                b = System.Math.Max(b, batches.LastValue); d = System.Math.Max(d, drawCalls.LastValue);
                sp = System.Math.Max(sp, setPass.LastValue); tr = System.Math.Max(tr, tris.LastValue);
            }
            log.AppendLine($"渲染统计（{label}，Unity ProfilerRecorder，连续 8 帧取最大）：Batches {b}，Draw Calls {d}，SetPass {sp}，三角形 {tr}");
        }

        Color32[] Render()
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

        void Save(Color32[] px, string name)
        {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, $"20260925_WorkerHandV2_{name}.png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        IEnumerator Shot(string name)
        {
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            Save(Render(), name);
        }
    }
}
