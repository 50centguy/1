using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Tools;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace BorderRepair.Tests
{
    /// <summary>
    /// 检测仪屏幕 shader 的截图与性能对比（batchmode 代码驱动，不是试玩）：
    /// 按剧情用工具动画走到 READY / BYPASS / LOG 三个读数，各截一张游戏镜头（1920×1080）和屏幕局部放大；
    /// 另用测试加的近景相机截三个状态的特写、一次切换的跳变序列，并检查稳定后逐像素不变；
    /// 最后在同一画面下对比预设开 / 关的渲染统计与渲染耗时。Explicit：只有用 -testFilter 点名时才运行。
    /// </summary>
    [Explicit("仅用于生成截图和性能对比")]
    public class TesterScreenCaptureTests
    {
        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Narrative", "Screenshots"));
        static string MetricsPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Narrative", "tester_screen_batchmode_metrics.txt"));
        const string Prefix = "20260925_TesterScreen_";

        RepairStationController controller;
        RepairToolAnimator anim;
        TesterReadout tester;
        RenderTexture rt, closeRt;
        Camera cam, close;
        StringBuilder log;
        ProfilerRecorder batches, drawCalls, setPass;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return WorkerHandTestUtil.Load(WorkerHandTestUtil.NarrativeScene, c => controller = c);
            anim = controller.ToolAnimator;
            tester = Object.FindFirstObjectByType<TesterReadout>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            batches.Dispose(); drawCalls.Dispose(); setPass.Dispose();
            if (cam != null) cam.targetTexture = null;
            if (close != null) Object.Destroy(close.gameObject);
            if (rt != null) rt.Release();
            if (closeRt != null) closeRt.Release();
            yield return null;
        }

        [UnityTest]
        public IEnumerator CaptureThreeStatesAndCompareCost()
        {
            Assert.IsTrue(tester.PresetsActive, "检测仪应已接上屏幕预设");
            Directory.CreateDirectory(OutDir);
            rt = PolishUtil.UseFullHd(controller);
            cam = controller.Inspector.ViewCamera;
            closeRt = new RenderTexture(960, 540, 24);
            close = TesterScreenUtil.CloseUp(tester, closeRt);
            log = new StringBuilder();
            log.AppendLine("检测仪屏幕 shader（BorderRepair/DiagnosticScreen）batchmode 截图与性能对比");
            log.AppendLine($"GPU: {SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceType}；游戏镜头 1920×1080，近景相机 960×540（测试临时添加）");
            log.AppendLine();
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");

            var s = controller.Session;
            for (int i = 0; i < 5; i++) yield return null;
            s.AcceptItem();
            controller.SelectTool(RepairActionType.RemoveFastener);
            Assert.IsTrue(controller.ClickPoint("fastener_a")); yield return ToolAnimUtil.WaitIdle(anim);
            Assert.IsTrue(controller.ClickPoint("fastener_b")); yield return ToolAnimUtil.WaitIdle(anim);
            controller.SelectTool(RepairActionType.OpenHousing);
            Assert.IsTrue(controller.ClickPoint("shell")); yield return ToolAnimUtil.WaitIdle(anim);

            // READY：拿起检测仪、镜头对准限力器，还没接触
            controller.SelectTool(RepairActionType.ServiceModule);
            yield return new WaitForSeconds(0.6f);
            yield return PolishUtil.Settle(controller.Inspector);
            Assert.AreEqual(TesterReadout.Reading.Ready, tester.Current);
            yield return GameShot("01_ready_ingame");
            yield return CloseShot("01_ready_closeup");

            // BYPASS：探针接触限力器时切换；接触后下一帧是跳变，约 1.2 秒后稳定
            Assert.IsTrue(controller.ClickPoint("force_limiter"));
            float t = 0f;
            while (tester.Current != TesterReadout.Reading.Bypass && t < 4f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(TesterReadout.Reading.Bypass, tester.Current);
            yield return null;
            yield return GameShot("02a_bypass_switch_jump_ingame");
            yield return new WaitForSeconds(1.2f);
            yield return GameShot("02_bypass_ingame");
            yield return CloseShot("02_bypass_closeup");
            yield return ToolAnimUtil.WaitIdle(anim);

            // LOG：插头接入数据口时切换
            yield return new WaitForSeconds(1.2f);
            yield return PolishUtil.Settle(controller.Inspector);
            Assert.IsTrue(controller.ClickPoint("data_port"));
            t = 0f;
            while (tester.Current != TesterReadout.Reading.Log && t < 4f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(TesterReadout.Reading.Log, tester.Current);
            yield return new WaitForSeconds(0.5f);
            yield return GameShot("03_log_ingame");
            yield return ToolAnimUtil.WaitIdle(anim);
            yield return new WaitForSeconds(0.5f);
            yield return CloseShot("03_log_closeup");
            Assert.IsTrue(s.HasClue("remote_params"));

            yield return SwitchSequence();
            yield return CompareCost();

            File.WriteAllText(MetricsPath, log.ToString(), new UTF8Encoding(true));
            Debug.Log("[TesterScreen] " + log);
        }

        /// <summary>游戏镜头截图 + 屏幕局部放大；记下屏幕在画面里的大小与距离上次切换的时间。</summary>
        IEnumerator GameShot(string name)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            var px = Grab(cam);
            Save(px, rt.width, rt.height, name);
            var r = PolishUtil.ScreenRect(cam, tester.Screen.bounds);
            bool inFrame = r.xMin >= 0 && r.yMin >= 0 && r.xMax <= rt.width && r.yMax <= rt.height;
            log.AppendLine($"{name}：读数 {tester.Current}，距切换 {Time.time - tester.LastSwitchTime:0.00} 秒，屏幕在画面中 {r.width:0}×{r.height:0} 像素{(inFrame ? "" : "（部分在画面外）")}");
            if (r.width > 4 && r.height > 4) SaveCrop(px, rt.width, rt.height, r, 0.35f, 4, name + "_crop");
        }

        /// <summary>近景特写，并检查稳定后相隔 1 秒逐像素一致。</summary>
        IEnumerator CloseShot(string name)
        {
            yield return null;
            var a = TesterScreenUtil.Grab(close);
            Save(a, closeRt.width, closeRt.height, name);
            yield return new WaitForSeconds(1f);
            var b = TesterScreenUtil.Grab(close);
            var rect = TesterScreenUtil.Clamp(PolishUtil.ScreenRect(close, tester.Screen.bounds), closeRt.width, closeRt.height, 0.1f);
            var d = TesterScreenUtil.Diff(a, b, closeRt.width, rect);
            log.AppendLine($"{name}：稳定检查（相隔 1 秒，屏幕区域 {rect.width}×{rect.height}）不同像素 {d.changed}/{d.total}，最大通道差 {d.maxDiff}");
            Assert.AreEqual(0, d.changed, $"{name}：稳定后画面不能变化");
        }

        /// <summary>一次切换（LOG → BYPASS → LOG）的近景序列：跳变帧与稳定帧拼成一张。</summary>
        IEnumerator SwitchSequence()
        {
            var frames = new List<(Color32[] px, float dt)>();
            tester.Show(TesterReadout.Reading.Bypass);
            float[] at = { 0f, 0.06f, 0.12f, 0.3f, 1.0f };
            foreach (var target in at)
            {
                while (Time.time - tester.LastSwitchTime < target) yield return null;
                frames.Add((TesterScreenUtil.Grab(close), Time.time - tester.LastSwitchTime));
            }
            var rect = TesterScreenUtil.Clamp(PolishUtil.ScreenRect(close, tester.Screen.bounds), closeRt.width, closeRt.height);
            int w = rect.width, h = rect.height;
            var strip = new Texture2D(w * frames.Count, h, TextureFormat.RGB24, false);
            for (int i = 0; i < frames.Count; i++)
            {
                var d = TesterScreenUtil.Diff(frames[i].px, frames[frames.Count - 1].px, closeRt.width, rect, 8);
                log.AppendLine($"切换序列：距切换 {frames[i].dt:0.000} 秒，与稳定帧相比明显不同的像素 {d.changed}/{d.total}");
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        strip.SetPixel(i * w + x, y, frames[i].px[(rect.y + y) * closeRt.width + rect.x + x]);
            }
            strip.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, Prefix + "04_switch_sequence_closeup.png"), strip.EncodeToPNG());
            Object.Destroy(strip);
            tester.Show(TesterReadout.Reading.Log);
            yield return new WaitForSeconds(0.4f);
        }

        /// <summary>预设开 / 关对比：同一画面的 Draw Calls / SetPass，以及交替多轮的单次渲染耗时（含 GPU 同步）。</summary>
        IEnumerator CompareCost()
        {
            log.AppendLine();
            foreach (bool on in new[] { true, false })
            {
                tester.SetPresetsEnabled(on);
                long b = 0, d = 0, sp = 0;
                for (int i = 0; i < 8; i++)
                {
                    yield return null;
                    b = System.Math.Max(b, batches.LastValue); d = System.Math.Max(d, drawCalls.LastValue); sp = System.Math.Max(sp, setPass.LastValue);
                }
                log.AppendLine($"渲染统计（游戏镜头，屏幕预设{(on ? "开" : "关")}，连续 8 帧取最大）：Batches {b}，Draw Calls {d}，SetPass {sp}");
                if (!on) yield return GameShot("05_presets_off_ingame");
            }

            // 近景相机改成 1920×1080、屏幕占满画面，作为像素开销最坏的情况
            var bigRt = new RenderTexture(1920, 1080, 24);
            var big = TesterScreenUtil.CloseUp(tester, bigRt, 0.95f);
            foreach (var (label, c) in new[] { ("游戏镜头 1920×1080", cam), ("屏幕占满的近景 1920×1080", big) })
            {
                var on = new List<double>(); var off = new List<double>();
                for (int round = 0; round < 6; round++)
                {
                    tester.SetPresetsEnabled(round % 2 == 0);
                    yield return null;
                    (round % 2 == 0 ? on : off).Add(TimeRenders(c, 30));
                }
                on.Sort(); off.Sort();
                log.AppendLine($"渲染耗时（{label}，每轮 30 次 Render + 读回 1 像素同步 GPU，3 轮取中位数）：预设开 {on[1]:0.000} ms / 次，关 {off[1]:0.000} ms / 次，差 {on[1] - off[1]:+0.000;-0.000} ms（开：{string.Join(", ", on.Select(v => v.ToString("0.000")))}；关：{string.Join(", ", off.Select(v => v.ToString("0.000")))}）");
            }
            tester.SetPresetsEnabled(true);
            Object.Destroy(big.gameObject);
            bigRt.Release();
        }

        static double TimeRenders(Camera c, int n)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var prev = RenderTexture.active;
            RenderTexture.active = c.targetTexture;
            c.Render(); tex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);        // 预热并同步
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++)
            {
                c.Render();
                RenderTexture.active = c.targetTexture;
                tex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);                 // 读回迫使 CPU 等 GPU 画完
            }
            sw.Stop();
            RenderTexture.active = prev;
            Object.Destroy(tex);
            return sw.Elapsed.TotalMilliseconds / n;
        }

        Color32[] Grab(Camera c) => TesterScreenUtil.Grab(c);

        void Save(Color32[] px, int w, int h, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, Prefix + name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        /// <summary>截取屏幕周围（外扩 pad 倍）并按整数倍最近邻放大，方便看清像素栅格和字。</summary>
        void SaveCrop(Color32[] px, int w, int h, Rect r, float pad, int scale, string name)
        {
            var box = TesterScreenUtil.Clamp(Rect.MinMaxRect(r.xMin - r.width * pad, r.yMin - r.height * pad, r.xMax + r.width * pad, r.yMax + r.height * pad), w, h);
            if (box.width <= 0 || box.height <= 0) return;
            var tex = new Texture2D(box.width * scale, box.height * scale, TextureFormat.RGB24, false);
            for (int y = 0; y < tex.height; y++)
                for (int x = 0; x < tex.width; x++)
                    tex.SetPixel(x, y, px[(box.y + y / scale) * w + box.x + x / scale]);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, Prefix + name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
