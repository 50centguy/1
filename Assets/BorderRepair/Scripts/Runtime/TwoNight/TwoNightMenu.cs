using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.TwoNight
{
    /// <summary>
    /// 两晚切片主菜单：开始新游戏 / 继续 / 重新开始（确认后只删本切片存档）/ 退出。
    /// 没有有效存档时“继续”不可用，并写明原因（没有存档、损坏、版本不符）。
    /// 命令行（Player）：
    /// - -twoNightSaveDir &lt;目录&gt;：存档放到指定目录（自检用，不碰玩家的真存档）；
    /// - -twoNightCore：直接打开七号鼠标维修核心（回归测试入口，不是玩家正常入口）。
    /// </summary>
    public class TwoNightMenu : MonoBehaviour
    {
        public const string CoreScene = "TwoNightSlice";

        [SerializeField] TwoNightEconomy economy;
        [SerializeField] Font font;

        public Button NewButton { get; private set; }
        public Button ContinueButton { get; private set; }
        public Button RestartButton { get; private set; }
        public Button ConfirmYes { get; private set; }
        public Button ConfirmNo { get; private set; }
        public Text StatusText => status;
        public SaveLoadResult LastRead { get; private set; }

        Text status;
        GameObject confirm;

        public void Configure(TwoNightEconomy e, Font f) { economy = e; font = f; }

        public static void ApplyCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-twoNightSaveDir");
            if (i >= 0 && i + 1 < args.Length) TwoNightSave.OverrideDirectory = args[i + 1];
        }

        void Awake()
        {
            ApplyCommandLine();
            TwoNightPlayerCheck.StartIfRequested();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-twoNightCore") >= 0 && !Application.isEditor) { SceneManager.LoadScene(CoreScene); return; }
            Build();
        }

        void Start() { if (ContinueButton != null) Refresh(); }

        public void Refresh()
        {
            LastRead = TwoNightSave.Read();
            bool ok = LastRead.status == SaveStatus.Ok;
            ContinueButton.interactable = ok;
            switch (LastRead.status)
            {
                case SaveStatus.None: status.text = "没有存档。从第一晚开始。"; break;
                case SaveStatus.Ok:
                    var s = LastRead.state;
                    status.text = $"存档：第一晚已结束（{LocalTime(s.savedAtUtc)}）。现金 {TwoNightUi.Money(s.Cash)}，七号内部工单 {s.unit07WorkOrderId} 待修。";
                    break;
                default:
                    status.text = $"<color=#F2B95A>存档不能用：{LastRead.message}</color>\n可以“重新开始”（只删除这个切片的存档）。";
                    break;
            }
        }

        static string LocalTime(string utc) => DateTime.TryParse(utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("MM-dd HH:mm") : "";

        public void NewGame()
        {
            TwoNightRun.NewGame(economy);
            SceneManager.LoadScene(TwoNightScenes.Counter);
        }

        public void Continue()
        {
            var r = TwoNightSave.Read();
            LastRead = r;
            if (r.status != SaveStatus.Ok) { Refresh(); return; }
            TwoNightRun.Set(r.state);
            if (!TwoNightRun.BeginNight2(r.state)) { status.text = "存档阶段不对，不能继续。"; return; }
            SceneManager.LoadScene(TwoNightScenes.Robot);
        }

        public void AskRestart() => confirm.SetActive(true);

        public void ConfirmRestart()
        {
            confirm.SetActive(false);
            TwoNightSave.Delete();
            NewGame();
        }

        void Build()
        {
            var root = new GameObject("TwoNightMenuUI");
            root.transform.SetParent(transform, false);
            TwoNightUi.Canvas(root, 10);
            var p = TwoNightUi.Panel(root.transform, "Menu", new Vector2(0.5f, 0.5f), new Vector2(-380, 260), new Vector2(760, 520));
            TwoNightUi.Label(p, "Title", "边境维修站 · 两晚切片", font, 34, new Vector2(40, -36), new Vector2(680, 50), TwoNightUi.TextMain, TextAnchor.UpperLeft, FontStyle.Bold);
            TwoNightUi.Label(p, "Sub", "第一晚：收藏家的通讯器、账本、七号打烊后的异常。第二晚：开始检查七号。", font, 17, new Vector2(40, -92), new Vector2(680, 50), TwoNightUi.TextDim);
            NewButton = TwoNightUi.Button(p, "New", "开始新游戏", font, new Vector2(40, -170), new Vector2(300, 52), NewGame, true);
            ContinueButton = TwoNightUi.Button(p, "Continue", "继续", font, new Vector2(40, -236), new Vector2(300, 52), Continue);
            RestartButton = TwoNightUi.Button(p, "Restart", "重新开始（删除存档）", font, new Vector2(40, -302), new Vector2(300, 52), AskRestart);
            TwoNightUi.Button(p, "Quit", "退出", font, new Vector2(40, -368), new Vector2(300, 52), Application.Quit);
            status = TwoNightUi.Label(p, "Status", "", font, 17, new Vector2(370, -170), new Vector2(360, 250), TwoNightUi.TextMain);
            TwoNightUi.Label(p, "Note", "存档位置：" + TwoNightSave.FilePath, font, 13, new Vector2(40, -450), new Vector2(680, 50), TwoNightUi.TextDim);

            confirm = TwoNightUi.Panel(root.transform, "Confirm", new Vector2(0.5f, 0.5f), new Vector2(-300, 120), new Vector2(600, 240)).gameObject;
            TwoNightUi.Label(confirm.transform, "Q", "重新开始会删除这个切片的存档（只删 checkpoint.json，不动其它文件），从第一晚开始。确定吗？", font, 19, new Vector2(30, -30), new Vector2(540, 110), TwoNightUi.TextMain);
            ConfirmYes = TwoNightUi.Button(confirm.transform, "Yes", "确定重新开始", font, new Vector2(30, -170), new Vector2(240, 46), ConfirmRestart, true);
            ConfirmNo = TwoNightUi.Button(confirm.transform, "No", "取消", font, new Vector2(330, -170), new Vector2(240, 46), () => confirm.SetActive(false));
            confirm.SetActive(false);
        }
    }
}
