using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BorderRepair.FirstOrder.Slice
{
    /// <summary>
    /// 两晚切片的鼠标界面（UGUI，和项目里维修台界面同一套：legacy Text + Button，字体用项目内打包的思源黑体 / Noto Sans SC）。
    /// 只做显示和把鼠标点击转成已有的入口：
    /// - 镜头栏 → FirstOrderCameraRig.Go（和数字键同一个入口）；“返回”回到上一个镜头；
    /// - 操作 / 观察模式 → FirstOrderInput.ObserveMode；观察结果来自 SliceObservation（只读）；
    /// - 维修动作仍然由 FirstOrderInput 的 3D 点选交给 FirstOrderFlow。本组件不推进步骤、不动供电。
    /// 界面元素都会接住鼠标（FirstOrderInput 遇到界面就不点 3D）；悬停提示不接鼠标。
    /// </summary>
    public class SliceView : MonoBehaviour
    {
        [SerializeField] FirstOrderFlow flow;
        [SerializeField] FirstOrderInput input;
        [SerializeField] Font font;

        public static readonly (string shot, string label)[] CameraButtons =
        {
            (FirstOrderCameraRig.Dock, "维修座"), (FirstOrderCameraRig.EngineL, "左引擎"), (FirstOrderCameraRig.EngineRear, "左引擎背面"),
            (FirstOrderCameraRig.EngineClose, "近看"), (FirstOrderCameraRig.EngineR, "右引擎"), (FirstOrderCameraRig.Bench, "工作台"),
            (FirstOrderCameraRig.Record, "保养记录"), (FirstOrderCameraRig.Compare, "新旧轴承"), (FirstOrderCameraRig.Overview, "总览"),
        };

        static readonly Color PanelBg = new Color(0.07f, 0.08f, 0.09f, 0.86f);
        static readonly Color ButtonBg = new Color(0.20f, 0.22f, 0.24f, 0.95f);
        static readonly Color ButtonOn = new Color(0.55f, 0.43f, 0.24f, 1f);      // 选中：旧黄铜色，不用红
        static readonly Color TextMain = new Color(0.92f, 0.90f, 0.86f);
        static readonly Color TextDim = new Color(0.70f, 0.70f, 0.68f);
        static readonly Color RefuseBg = new Color(0.36f, 0.26f, 0.10f, 0.92f);    // 被拒绝：暗琥珀
        static readonly Color OkBg = new Color(0.12f, 0.17f, 0.14f, 0.90f);

        public SliceObservation Observation { get; private set; }
        public SliceObservationResult LastObservation { get; private set; }
        public bool ManualOpen => manualPanel != null && manualPanel.activeSelf;
        public bool ObservationOpen => obsPanel != null && obsPanel.activeSelf;
        public InputField NotesField => notesField;
        public RectTransform ManualPanelRect => manualPanel != null ? (RectTransform)manualPanel.transform : null;
        public Text FeedbackText => feedbackText;
        public bool LastFeedbackWasRefusal { get; private set; }
        public IEnumerable<Text> AllTexts => GetComponentsInChildren<Text>(true);

        readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
        readonly List<string> history = new List<string>();
        string lastShot;
        bool goingBack;
        string lastActedMessage;
        bool lastActedOk = true;

        Text titleText, stepText, hintText, feedbackText, tooltipText, obsTitle, obsBody, manualDiag, manualTitle, manualBody;
        Image feedbackBg;
        GameObject obsPanel, manualPanel, tooltip;
        Button obsGoButton;
        InputField notesField;
        RectTransform tooltipRect, canvasRect;

        public void Configure(FirstOrderFlow f, FirstOrderInput i, Font uiFont) { flow = f; input = i; font = uiFont; }

        public Button GetButton(string id) => buttons.TryGetValue(id, out var b) ? b : null;

        /// <summary>两晚切片：换标题（例如“第二晚 · 七号内部工单”）。</summary>
        public void SetTitle(string title) { if (titleText != null) titleText.text = title; }

        /// <summary>两晚切片：换手册内容（入口手册只写安全须知、可观察现象和推荐检查方法，不写诊断答案）。</summary>
        public void SetManual(string title, string body)
        {
            if (manualTitle != null) manualTitle.text = title;
            if (manualBody != null) manualBody.text = body;
        }

        public string ManualTitleText => manualTitle != null ? manualTitle.text : "";
        public string ManualBodyText => manualBody != null ? manualBody.text : "";

        void Awake()
        {
            Observation = new SliceObservation(flow);
            Build();
        }

        void OnEnable()
        {
            if (input != null) input.Observed += OnObserved;
            if (flow != null) flow.Acted += OnActed;
        }

        void OnDisable()
        {
            if (input != null) input.Observed -= OnObserved;
            if (flow != null) flow.Acted -= OnActed;
        }

        // ------------------------------------------------------------------ 构建界面

        void Build()
        {
            var canvas = gameObject.GetComponent<Canvas>(); if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = gameObject.GetComponent<CanvasScaler>(); if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0f;                    // 按宽度缩放：4:3 窗口下底部两组按钮也不会重叠
            if (gameObject.GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            canvasRect = (RectTransform)transform;

            // 左上：状态 + 下一步
            var status = Panel("Status", transform, new Vector2(0, 1), new Vector2(16, -16), new Vector2(820, 112));
            titleText = Label(status, "Title", "七号 · 左引擎维修（两晚切片）", 20, new Vector2(14, -6), new Vector2(790, 32), TextMain, FontStyle.Bold);
            stepText = Label(status, "Step", "", 16, new Vector2(14, -38), new Vector2(790, 24), TextDim);
            hintText = Label(status, "Hint", "", 17, new Vector2(14, -64), new Vector2(790, 44), TextMain);

            // 反馈
            var fb = Panel("Feedback", transform, new Vector2(0, 1), new Vector2(16, -136), new Vector2(820, 76));
            feedbackBg = fb.GetComponent<Image>();
            feedbackText = Label(fb, "Message", "", 17, new Vector2(14, -8), new Vector2(792, 62), TextMain);

            // 底部镜头栏
            var bar = Panel("CameraBar", transform, new Vector2(0, 0), new Vector2(16, 16 + 52), new Vector2(1060, 52));
            float x = 8f;
            Label(bar, "BarLabel", "镜头", 15, new Vector2(x, -15), new Vector2(40, 24), TextDim);
            x += 44f;
            foreach (var (shot, label) in CameraButtons)
            {
                float w = label.Length >= 4 ? 100f : label.Length == 3 ? 86f : 70f;
                var b = MakeButton(bar, "Cam_" + shot, label, new Vector2(x, -8), new Vector2(w, 36), () => GoShot(shot));
                buttons["cam:" + shot] = b;
                x += w + 6f;
            }
            buttons["back"] = MakeButton(bar, "Back", "← 返回", new Vector2(x + 6f, -8), new Vector2(84, 36), Back);

            // 右下：模式 / 手册 / 调试
            var tools = Panel("Tools", transform, new Vector2(1, 0), new Vector2(-16 - 492, 16 + 52), new Vector2(492, 52));
            Label(tools, "ModeLabel", "左键", 15, new Vector2(8, -15), new Vector2(40, 24), TextDim);
            buttons["mode:act"] = MakeButton(tools, "Mode_Act", "操作", new Vector2(52, -8), new Vector2(76, 36), () => SetObserveMode(false));
            buttons["mode:observe"] = MakeButton(tools, "Mode_Observe", "观察", new Vector2(134, -8), new Vector2(76, 36), () => SetObserveMode(true));
            Label(tools, "RightClick", "右键：观察", 14, new Vector2(216, -16), new Vector2(84, 24), TextDim);
            buttons["manual"] = MakeButton(tools, "Manual", "手册", new Vector2(304, -8), new Vector2(84, 36), ToggleManual);
            buttons["debug"] = MakeButton(tools, "Debug", "调试", new Vector2(394, -8), new Vector2(90, 36), ToggleDebug);

            // 右侧：观察结果
            obsPanel = Panel("Observation", transform, new Vector2(1, 1), new Vector2(-16 - 470, -150), new Vector2(470, 300)).gameObject;
            obsTitle = Label(obsPanel.transform, "ObsTitle", "", 19, new Vector2(14, -10), new Vector2(442, 28), TextMain, FontStyle.Bold);
            obsBody = Label(obsPanel.transform, "ObsBody", "", 16, new Vector2(14, -44), new Vector2(442, 196), TextMain);
            obsGoButton = MakeButton(obsPanel.transform, "ObsGo", "切换镜头", new Vector2(14, -250), new Vector2(200, 38), () => { if (!string.IsNullOrEmpty(LastObservation.suggestShot)) GoShot(LastObservation.suggestShot); });
            buttons["obs:go"] = obsGoButton;
            buttons["obs:close"] = MakeButton(obsPanel.transform, "ObsClose", "关闭", new Vector2(370, -250), new Vector2(86, 38), () => obsPanel.SetActive(false));
            obsPanel.SetActive(false);

            // 手册
            manualPanel = Panel("Manual", transform, new Vector2(0.5f, 0.5f), new Vector2(-420, 330), new Vector2(840, 660)).gameObject;
            manualTitle = Label(manualPanel.transform, "ManualTitle", "维修手册 · 七号左引擎（工单：进气堵塞 + 左上轴承磨损）", 20, new Vector2(18, -12), new Vector2(800, 30), TextMain, FontStyle.Bold);
            manualBody = Label(manualPanel.transform, "ManualBody",
                "安全：七号落座、夹紧、断电，并且叶轮停稳之后，才能检查和拆卸。转动中只能看，不能动手。\n" +
                "诊断：先看右引擎（正常对照），再看左进气口、上盖内侧保养记录、左上轴承。只清理进气口不算修好。\n" +
                "步骤：落座 → 夹紧 → 断电停转 → 检查左引擎 → 扳开外侧、后侧锁扣 → 取下上盖总成，翻面放到操作垫 → 定位并取下磨损轴承 → 旧件放进托盘 → 装新轴承 → 装回上盖、扣回锁扣 → 清理进气口（停转期间随时）→ 通电 → 松开夹具，离座复测。\n" +
                "鼠标：左键操作，右键观察（或在右下角切到“观察”）。观察要离得够近，太远会提示切镜头。底部镜头栏切换视角，“返回”回到上一个镜头。\n" +
                "可选调试：数字键 1–9 切镜头，F3 开关调试面板（文字输入时不响应）。",
                16, new Vector2(18, -50), new Vector2(800, 300), TextMain);
            Label(manualPanel.transform, "DiagTitle", "诊断记录", 17, new Vector2(18, -360), new Vector2(300, 26), TextMain, FontStyle.Bold);
            manualDiag = Label(manualPanel.transform, "Diag", "", 16, new Vector2(18, -390), new Vector2(800, 130), TextDim);
            Label(manualPanel.transform, "NotesLabel", "备注（只记在这里，不影响维修）", 15, new Vector2(18, -526), new Vector2(400, 24), TextDim);
            notesField = MakeInput(manualPanel.transform, "Notes", new Vector2(18, -554), new Vector2(690, 40));
            buttons["manual:close"] = MakeButton(manualPanel.transform, "ManualClose", "关闭", new Vector2(720, -554), new Vector2(100, 40), ToggleManual);
            manualPanel.SetActive(false);

            // 悬停提示（不接鼠标）
            tooltip = Panel("Tooltip", transform, new Vector2(0, 0), Vector2.zero, new Vector2(320, 54)).gameObject;
            tooltip.GetComponent<Image>().raycastTarget = false;
            tooltipRect = (RectTransform)tooltip.transform;
            tooltipRect.pivot = new Vector2(0, 1);
            tooltipText = Label(tooltip.transform, "TooltipText", "", 15, new Vector2(10, -6), new Vector2(304, 44), TextMain);
            tooltip.SetActive(false);

            RefreshModeButtons();
        }

        RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = PanelBg;
            return rt;
        }

        Text Label(Transform parent, string name, string text, int size, Vector2 pos, Vector2 box, Color color, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = box;
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;      // 思源黑体行高大，Truncate 会把放不下整行的文字整行藏掉
            t.lineSpacing = 1.1f;
            t.raycastTarget = false;
            return t;
        }

        Button MakeButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = ButtonBg;
            var b = go.GetComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.45f, 1.42f, 1.35f, 1f);     // 悬停：整体提亮
            cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            cb.selectedColor = Color.white;
            cb.colorMultiplier = 1.6f;
            b.colors = cb;
            b.onClick.AddListener(onClick);
            b.onClick.AddListener(() => { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); });
            var t = Label(go.transform, "Text", label, 16, Vector2.zero, size, TextMain);
            t.alignment = TextAnchor.MiddleCenter;
            return b;
        }

        InputField MakeInput(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.18f, 1f);
            var text = Label(go.transform, "Text", "", 16, new Vector2(10, -8), size - new Vector2(20, 12), TextMain);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ph = Label(go.transform, "Placeholder", "在这里输入（数字键不会切镜头）", 16, new Vector2(10, -8), size - new Vector2(20, 12), new Color(0.5f, 0.5f, 0.5f));
            ph.fontStyle = FontStyle.Italic;
            var f = go.GetComponent<InputField>();
            f.textComponent = text;
            f.placeholder = ph;
            f.lineType = InputField.LineType.SingleLine;
            return f;
        }

        // ------------------------------------------------------------------ 动作

        public void GoShot(string shot)
        {
            if (flow == null || flow.Rig == null) return;
            flow.Rig.Go(shot);
        }

        public void Back()
        {
            if (history.Count == 0) return;
            var prev = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            goingBack = true;
            flow.Rig.Go(prev);
        }

        public void SetObserveMode(bool on)
        {
            input.ObserveMode = on;
            RefreshModeButtons();
        }

        public void ToggleManual()
        {
            manualPanel.SetActive(!manualPanel.activeSelf);
            if (!manualPanel.activeSelf && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        public void ToggleDebug()
        {
            input.ShowHud = !input.ShowHud;
            Tint(buttons["debug"], input.ShowHud);
        }

        void RefreshModeButtons()
        {
            if (input == null) return;
            Tint(buttons["mode:act"], !input.ObserveMode);
            Tint(buttons["mode:observe"], input.ObserveMode);
        }

        static void Tint(Button b, bool on) { if (b != null) ((Image)b.targetGraphic).color = on ? ButtonOn : ButtonBg; }

        void OnObserved(Component hit)
        {
            var r = Observation.Observe(hit, flow.Rig.Cam);
            LastObservation = r;
            obsTitle.text = r.title;
            obsBody.text = r.body;
            bool canGo = !r.seen && !string.IsNullOrEmpty(r.suggestShot) && r.suggestShot != flow.Rig.Current;
            obsGoButton.gameObject.SetActive(canGo);
            if (canGo) obsGoButton.GetComponentInChildren<Text>().text = "切到「" + SliceObservation.ShortLabel(r.suggestShot) + "」";
            obsPanel.SetActive(true);
        }

        void OnActed(string target, bool ok, string msg) { lastActedMessage = msg; lastActedOk = ok; }

        // ------------------------------------------------------------------ 每帧刷新

        void Update()
        {
            if (flow == null) return;
            var kb = Keyboard.current;
            if (kb != null && !FirstOrderInput.TextInputFocused)
            {
                if (kb.f3Key.wasPressedThisFrame) ToggleDebug();
                if (kb.escapeKey.wasPressedThisFrame) { if (manualPanel.activeSelf) ToggleManual(); else obsPanel.SetActive(false); }
            }

            // 镜头历史（含流程自动切换的镜头），供“返回”使用
            var cur = flow.Rig.Current;
            if (lastShot != null && cur != lastShot && !goingBack)
            {
                history.Add(lastShot);
                if (history.Count > 20) history.RemoveAt(0);
            }
            goingBack = false;
            lastShot = cur;
            foreach (var (shot, _) in CameraButtons) Tint(buttons["cam:" + shot], shot == cur);
            buttons["back"].interactable = history.Count > 0;

            // 离座由 FirstOrderFlow 自己移动七号（当前维修座没有“离座”状态），这时按流程步骤显示
            string where = flow.Step >= FoStep.Retest ? "七号离座悬停" : SliceObservation.DockStateText(flow.Dock.State);
            stepText.text = $"步骤 {(int)flow.Step + 1}/17 · 维修座：{where} · 镜头：{SliceObservation.ShortLabel(cur)}";
            hintText.text = "下一步：" + flow.NextHint();
            bool refusal = !lastActedOk && flow.Message == lastActedMessage;
            LastFeedbackWasRefusal = refusal;
            feedbackText.text = refusal ? "不行：" + flow.Message : flow.Message;
            feedbackBg.color = refusal ? RefuseBg : OkBg;

            if (manualPanel.activeSelf) manualDiag.text = DiagnosisText();
            UpdateTooltip();
        }

        public string DiagnosisText()
        {
            var sb = new StringBuilder();
            foreach (var (key, label) in SliceObservation.DiagnosisItems)
                sb.Append(Observation.HasSeen(key) ? "【已看】" : "【未看】").Append(label).Append("    ");
            sb.AppendLine();
            sb.Append($"已看 {Observation.SeenCount}/{SliceObservation.DiagnosisItems.Length}。");
            sb.Append(flow.BearingReplaced ? "左上轴承已更换。" : flow.BearingLocated ? "已定位磨损轴承。" : "");
            sb.Append(flow.ClogCleared ? "进气口已清理。" : "");
            return sb.ToString();
        }

        void UpdateTooltip()
        {
            var mouse = Mouse.current;
            var h = input != null ? input.Hovered : null;
            if (mouse == null || h == null || input.PointerOverUI) { tooltip.SetActive(false); return; }
            tooltip.SetActive(true);
            tooltipText.text = FirstOrderInput.NameOf(h) + "\n" + (input.ObserveMode ? "左键：观察" : "左键：操作 · 右键：观察");
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, mouse.position.ReadValue(), null, out var local);
            var size = canvasRect.rect.size;
            var p = local + size * 0.5f + new Vector2(18f, -18f);                       // 画布左下为原点
            p.x = Mathf.Min(p.x, size.x - tooltipRect.sizeDelta.x - 4f);
            p.y = Mathf.Max(p.y, tooltipRect.sizeDelta.y + 4f);
            tooltipRect.anchoredPosition = p;
        }
    }
}
