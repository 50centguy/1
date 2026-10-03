using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace BorderRepair.FirstOrder.Slice
{
    /// <summary>
    /// 独立 Player 自检（只在命令行带 -sliceSelfCheck &lt;输出目录&gt; 时运行，平时什么都不做）：
    /// 核对界面文字都用打包进来的中文字体、每个字都有字形；用 ScreenCapture 截游戏自己的画面（不截桌面）；
    /// 再用程序点击走一遍（观察右引擎、未停转时碰左上盖、完整首单）。这些是程序操作，不是真人试玩。结果写 selfcheck.txt，然后退出。
    /// </summary>
    public class SlicePlayerSelfCheck : MonoBehaviour
    {
        [SerializeField] SliceView view;
        [SerializeField] FirstOrderFlow flow;
        [SerializeField] FirstOrderInput input;
        [SerializeField] Font expectedFont;

        public void Configure(SliceView v, FirstOrderFlow f, FirstOrderInput i, Font font) { view = v; flow = f; input = i; expectedFont = font; }

        string outDir;
        readonly StringBuilder log = new StringBuilder();
        int shot;
        bool allOk = true;

        void Start()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-sliceSelfCheck");
            if (i < 0) return;
            outDir = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : Path.Combine(Application.persistentDataPath, "SelfCheck");
            Directory.CreateDirectory(outDir);
            StartCoroutine(Run());
        }

        void Line(string s) { log.AppendLine(s); Debug.Log("[SliceSelfCheck] " + s); }
        void Check(bool ok, string s) { if (!ok) allOk = false; Line((ok ? "通过 " : "失败 ") + s); }

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
            yield return full.RunFullOrder();
            int failed = full.Records.Count(r => !r.pass);
            Check(failed == 0 && flow.Step == FoStep.Done && flow.RetestPassed, $"完整首单（程序点击）：{full.Records.Count} 步，失败 {failed}，结束步骤 {flow.Step}，复测 {flow.RetestPassed}");
            foreach (var r in full.Records.Where(r => !r.pass)) Line($"  失败 #{r.index} {r.label}：{r.message}");
            CheckFonts("流程结束");
            yield return Shot("order_done");

            Line(allOk ? "结果：全部通过" : "结果：有失败项");
            File.WriteAllText(Path.Combine(outDir, "selfcheck.txt"), log.ToString(), new UTF8Encoding(false));
            yield return null;
            Application.Quit(allOk ? 0 : 1);
        }
    }
}
