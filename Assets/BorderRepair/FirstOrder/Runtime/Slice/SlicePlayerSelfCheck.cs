using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace BorderRepair.FirstOrder.Slice
{
    /// <summary>
    /// 独立 Player 自检（只在命令行带 -sliceSelfCheck &lt;输出目录&gt; 时运行，平时什么都不做）：
    /// 核对界面文字都用打包进来的中文字体、每个字都有字形；用 ScreenCapture 截游戏自己的画面（不截桌面）；
    /// 再用程序点击走一遍（观察右引擎、未停转时碰左上盖、完整首单）。这些是程序操作，不是真人试玩。结果写 selfcheck.txt，然后退出。
    ///
    /// 诊断（定位“首轮等待停转超时”）：
    /// - selfcheck_timeline.txt：每次等待的开始 / 每秒心跳 / 结束（实时、游戏时间、帧、维修座状态、转速、焦点），焦点与暂停事件，
    ///   以及一个**后台线程**每秒写的心跳（主线程停住时它照样写，能看出画面有没有前进）。
    /// - -selfCheckRunInBackground：只给自检进程打开 Application.runInBackground（窗口被遮挡 / 失焦也继续跑）。这是自检设置，
    ///   玩家版本保持项目设置（runInBackground = 0，失焦暂停），两者分开记录。
    /// - -selfCheckFocusTest &lt;秒&gt;：受控失焦。完整首单里第一次“等待涡轮停转”开始时，把本进程窗口最小化，过 N 秒后恢复并请求前台焦点，记录前后时间线。
    /// - 进程在自检结束前被关掉时，也把已有记录写进 selfcheck.txt（并注明）。
    /// </summary>
    public class SlicePlayerSelfCheck : MonoBehaviour
    {
        [SerializeField] SliceView view;
        [SerializeField] FirstOrderFlow flow;
        [SerializeField] FirstOrderInput input;
        [SerializeField] Font expectedFont;

        public void Configure(SliceView v, FirstOrderFlow f, FirstOrderInput i, Font font) { view = v; flow = f; input = i; expectedFont = font; }

        string outDir, timelinePath;
        readonly StringBuilder log = new StringBuilder();
        int shot;
        bool allOk = true, finished, active;
        float focusTestSeconds;
        bool focusTestStarted;
        readonly object tlLock = new object();
        readonly Stopwatch wall = Stopwatch.StartNew();
        volatile int lastFrame;
        Thread heartbeat;
        volatile bool stopHeartbeat;
        IntPtr hwnd = IntPtr.Zero;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
#endif

        void Start()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-sliceSelfCheck");
            if (i < 0) return;
            active = true;
            outDir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Path.Combine(Application.persistentDataPath, "SelfCheck");
            Directory.CreateDirectory(outDir);
            timelinePath = Path.Combine(outDir, "selfcheck_timeline.txt");
            File.WriteAllText(timelinePath, "", new UTF8Encoding(false));
            if (Array.IndexOf(args, "-selfCheckRunInBackground") >= 0) Application.runInBackground = true;
            int f = Array.IndexOf(args, "-selfCheckFocusTest");
            if (f >= 0 && f + 1 < args.Length) float.TryParse(args[f + 1], out focusTestSeconds);
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            hwnd = GetActiveWindow();
            if (hwnd == IntPtr.Zero) hwnd = Process.GetCurrentProcess().MainWindowHandle;
#endif
            heartbeat = new Thread(() =>
            {
                int prev = -1;
                while (!stopHeartbeat)
                {
                    Thread.Sleep(1000);
                    int now = lastFrame;
                    Timeline($"HB（后台线程）帧 {now}（+{(prev < 0 ? 0 : now - prev)}）{(prev >= 0 && now == prev ? " ← 这一秒画面没有前进（Player 暂停或卡住）" : "")}");
                    prev = now;
                }
            }) { IsBackground = true };
            heartbeat.Start();
            StartCoroutine(Run());
        }

        void Update() { lastFrame = Time.frameCount; }

        void OnApplicationFocus(bool focus) { if (active) Timeline($"FOCUS {(focus ? "获得焦点" : "失去焦点")} | 帧 {Time.frameCount}，实时 {Time.realtimeSinceStartup:F2}s，游戏时间 {Time.time:F2}s"); }
        void OnApplicationPause(bool pause) { if (active) Timeline($"PAUSE {pause} | 帧 {Time.frameCount}"); }

        void OnApplicationQuit()
        {
            if (!active) return;
            stopHeartbeat = true;
            if (!finished)
            {
                Line("（进程在自检结束前退出：这是到退出为止的记录，不是完整结果）");
                try { File.WriteAllText(Path.Combine(outDir, "selfcheck.txt"), log.ToString(), new UTF8Encoding(false)); } catch { }
            }
            Timeline("QUIT");
        }

        void Timeline(string s)
        {
            if (timelinePath == null) return;
            var line = $"{DateTime.Now:HH:mm:ss.fff} 墙钟 {wall.Elapsed.TotalSeconds:F2}s  {s}";
            lock (tlLock) { try { File.AppendAllText(timelinePath, line + "\n", new UTF8Encoding(false)); } catch { } }
        }

        void Line(string s) { log.AppendLine(s); Debug.Log("[SliceSelfCheck] " + s); Timeline("LOG " + s); }
        void Check(bool ok, string s) { if (!ok) allOk = false; Line((ok ? "通过 " : "失败 ") + s); }

        void StartFocusTest()
        {
            focusTestStarted = true;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Timeline($"FOCUS-TEST 最小化本进程窗口 {focusTestSeconds:F0} 秒（runInBackground = {Application.runInBackground}）");
            ShowWindow(hwnd, 6);   // SW_MINIMIZE
            float secs = focusTestSeconds; var h = hwnd;
            new Thread(() =>
            {
                Thread.Sleep((int)(secs * 1000));
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    ShowWindow(h, 9);                                  // SW_RESTORE
                    keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);   // Alt 按下松开：允许本进程把窗口切到前台
                    bool ok = SetForegroundWindow(h);
                    Thread.Sleep(500);
                    bool fg = GetForegroundWindow() == h;
                    Timeline($"FOCUS-TEST 恢复窗口（第 {attempt} 次）：SetForegroundWindow={ok}，前台={fg}");
                    if (fg) break;
                    Thread.Sleep(2000);
                }
            }) { IsBackground = true }.Start();
#else
            Timeline("FOCUS-TEST 只在 Windows Player 里做");
#endif
        }

        IEnumerator Shot(string name)
        {
            for (int k = 0; k < 3; k++) yield return null;
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(outDir, $"{++shot:00}_{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            for (int k = 0; k < 3; k++) yield return null;
            Line($"截图（游戏画面，ScreenCapture）：{Path.GetFileName(path)}");
        }

        void CheckFonts(string when)
        {
            Canvas.ForceUpdateCanvases();
            var texts = view.AllTexts.Where(t => t.enabled && t.gameObject.activeInHierarchy && !string.IsNullOrEmpty(t.text)).ToList();   // 输入框有内容时占位文字被禁用，不算
            int wrongFont = 0, missing = 0, chars = 0, hidden = 0;
            var hiddenNames = new StringBuilder();
            var missingChars = new StringBuilder();
            foreach (var t in texts)
            {
                if (t.font != expectedFont) wrongFont++;
                int visible = t.cachedTextGenerator.characterCountVisible, len = t.text.Replace("\n", "").Length;
                if (visible < len * 0.8f) { hidden++; if (hiddenNames.Length < 120) hiddenNames.Append(t.name).Append($"({visible}/{len}) "); }
                t.font.RequestCharactersInTexture(t.text, t.fontSize, t.fontStyle);
                foreach (var c in t.text)
                {
                    if (char.IsWhiteSpace(c)) continue;
                    chars++;
                    if (!t.font.HasCharacter(c)) { missing++; if (missingChars.Length < 40) missingChars.Append(c); }
                }
            }
            Check(wrongFont == 0 && missing == 0 && hidden == 0 && texts.Count > 0,
                  $"{when}：显示中的文字 {texts.Count} 段、{chars} 个字符；不是打包字体的 {wrongFont} 段；没有字形的字符 {missing} 个{(missing > 0 ? "（" + missingChars + "）" : "")}；被截掉 / 没显示出来的 {hidden} 段{(hidden > 0 ? "（" + hiddenNames + "）" : "")}");
        }

        IEnumerator Run()
        {
            Line($"Player 自检 {DateTime.Now:yyyy-MM-dd HH:mm:ss}，Unity {Application.unityVersion}，{SystemInfo.operatingSystem}，{SystemInfo.graphicsDeviceName}，{Screen.width}×{Screen.height}，编辑器内 {Application.isEditor}");
            Line($"场景：{UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}（Build 里共 {UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings} 个场景）");
            Line($"打包字体：{(expectedFont != null ? expectedFont.name : "无")}，dynamic {(expectedFont != null && expectedFont.dynamic)}，fontNames [{(expectedFont != null ? string.Join(", ", expectedFont.fontNames) : "")}]");
            Line($"运行设置：runInBackground = {Application.runInBackground}（{(Array.IndexOf(Environment.GetCommandLineArgs(), "-selfCheckRunInBackground") >= 0 ? "自检参数打开，玩家版本不是这样" : "项目设置，与玩家版本相同")}）；受控失焦 {(focusTestSeconds > 0 ? focusTestSeconds.ToString("F0") + " 秒" : "无")}；启动时焦点 {Application.isFocused}");
            input.enabled = false;   // 自检期间不读真实鼠标，免得桌面上的鼠标位置干扰程序点击
            for (int k = 0; k < 60; k++) yield return null;
            CheckFonts("启动");
            yield return Shot("start_dock");

            view.ToggleManual();
            view.NotesField.text = "测试备注：右引擎正常，左进气口堵";
            yield return null;
            CheckFonts("打开手册");
            yield return Shot("manual_open");
            view.ToggleManual();

            // 观察右引擎（程序点击，七号还悬停通电）
            var driver = new FirstOrderAcceptanceDriver(flow, input) { Timeout = 60f };
            flow.Rig.Go(FirstOrderCameraRig.EngineR, true);
            yield return null; yield return null;
            Check(driver.FindClickPoint(flow.RightEngine, out var sp), "右引擎镜头下能点到右引擎");
            input.ObserveAt(sp);
            yield return null;
            Check(view.ObservationOpen && view.LastObservation.seen && view.LastObservation.key == SliceObservation.EngineR, "观察右引擎：" + view.LastObservation.title);
            CheckFonts("观察面板");
            yield return Shot("observe_right_engine");

            // 未停转时碰左上盖：拒绝并说明原因
            flow.Rig.Go(FirstOrderCameraRig.EngineL, true);
            yield return null; yield return null;
            Check(driver.FindClickPoint(flow.Cover, out sp), "左引擎镜头下能点到左上盖");
            var (_, accepted) = input.ClickAt(sp);
            yield return null;
            Check(!accepted && view.LastFeedbackWasRefusal && flow.Message.Contains("供电"), "通电悬停时碰左上盖被拒绝：" + flow.Message);
            yield return Shot("refuse_while_powered");

            // 完整首单（验收驱动，程序点击）
            // 只截几个关键步骤（验收驱动的截图钩子）：误拆右侧、减速中拒绝、清理、翻盖读记录、定位轴承、新旧对比、离座
            string[] keep = { "拒绝：要拆右引擎", "拒绝：涡轮减速中", "4b 清理", "翻盖读保养记录", "6 定位", "新旧轴承对比", "10 离座" };
            IEnumerator StepShot(string name) { if (keep.Any(k => name.Contains(k))) yield return Shot("order_" + name.Substring(name.IndexOf('_') + 1).Replace(' ', '_').Replace('：', '_').Replace('/', '_')); }
            var full = new FirstOrderAcceptanceDriver(flow, input, StepShot) { Timeout = 60f };
            full.Trace = s =>
            {
                Timeline(s);
                if (focusTestSeconds > 0 && !focusTestStarted && s.StartsWith("WAIT-BEGIN") && s.Contains("涡轮停转")) StartFocusTest();
            };
            yield return full.RunFullOrder();
            int failed = full.Records.Count(r => !r.pass);
            Check(failed == 0 && flow.Step == FoStep.Done && flow.RetestPassed, $"完整首单（程序点击）：{full.Records.Count} 步，失败 {failed}，结束步骤 {flow.Step}，复测 {flow.RetestPassed}");
            foreach (var r in full.Records.Where(r => !r.pass)) Line($"  失败 #{r.index} {r.label}：{r.message}");
            CheckFonts("流程结束");
            yield return Shot("order_done");

            Line(allOk ? "结果：全部通过" : "结果：有失败项");
            finished = true;
            File.WriteAllText(Path.Combine(outDir, "selfcheck.txt"), log.ToString(), new UTF8Encoding(false));
            stopHeartbeat = true;
            yield return null;
            Application.Quit(allOk ? 0 : 1);
        }
    }
}
