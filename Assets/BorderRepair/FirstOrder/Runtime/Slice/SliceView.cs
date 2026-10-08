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
        /// <summary>诊断记录里哪些项要显示（为空 = 全部显示，即核心切片原样）。两晚切片第二晚用它让“新旧轴承对比”在拆下旧件后才出现。</summary>
        public System.Func<string, bool> DiagnosisItemVisible { get; set; }
        public bool TooltipVisible => tooltip != null && tooltip.activeSelf;
        public bool CrosshairVisible => crosshair != null && crosshair.activeSelf;
        public string ControlsHintText => controlsText != null && controls.activeSelf ? controlsText.text : "";
        public string TooltipText => tooltipText != null ? tooltipText.text : "";
        public RectTransform TooltipRect => tooltipRect;
        /// <summary>悬停提示要避开的界面区域（画布坐标，左下为原点）。</summary>
        public List<Rect> TooltipAvoidRects() => new[] { statusRect, feedbackRect, barRect, toolsRect, controls != null && controls.activeSelf ? (RectTransform)controls.transform : null, obsPanel != null && obsPanel.activeSelf ? (RectTransform)obsPanel.transform : null }
            .Where(r => r != null).Select(CanvasRect).ToList();

        readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
        readonly List<string> history = new List<string>();
        string lastShot;
        bool goingBack;
        string lastActedMessage;
        bool lastActedOk = true;

        Text titleText, stepText, hintText, feedbackText, tooltipText, obsTitle, obsBody, manualDiag, manualTitle, manualBody;
        Image feedbackBg;
        GameObject obsPanel, manualPanel, tooltip, crosshair, controls;
        Text controlsText, backLabel;
        string localMsg, localMsgAtFlow;
        Button obsGoButton;
        InputField notesField;
        RectTransform tooltipRect, canvasRect, statusRect, feedbackRect, barRect, toolsRect;

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
            if (input != null) { input.Observed += OnObserved; input.OutOfReach += OnOutOfReach; }
            if (flow != null) flow.Acted += OnActed;
        }

        void OnDisable()
        {
            if (input != null) { input.Observed -= OnObserved; input.OutOfReach -= OnOutOfReach; }
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
            var status = Panel("Status", transform, new Vector2(0, 1), new Vector2(16, -16), new Vector2(820, 112)); statusRect = status;
            titleText = Label(status, "Title", "七号 · 左引擎维修（两晚切片）", 20, new Vector2(14, -6), new Vector2(790, 32), TextMain, FontStyle.Bold);
            stepText = Label(status, "Step", "", 16, new Vector2(14, -38), new Vector2(790, 24), TextDim);
            hintText = Label(status, "Hint", "", 17, new Vector2(14, -64), new Vector2(790, 44), TextMain);

            // 反馈
            var fb = Panel("Feedback", transform, new Vector2(0, 1), new Vector2(16, -136), new Vector2(820, 76)); feedbackRect = fb;
            feedbackBg = fb.GetComponent<Image>();
            feedbackText = Label(fb, "Message", "", 17, new Vector2(14, -8), new Vector2(792, 62), TextMain);

            // 底部镜头栏
            var bar = Panel("CameraBar", transform, new Vector2(0, 0), new Vector2(16, 16 + 52), new Vector2(1060, 52)); barRect = bar;
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
            backLabel = buttons["back"].GetComponentInChildren<Text>();

            // 第一人称：操作说明条（镜头栏上方，不接鼠标）和准星
            controls = Panel("Controls", transform, new Vector2(0, 0), new Vector2(16, 16 + 52 + 6 + 30), new Vector2(1060, 30)).gameObject;
            controls.GetComponent<Image>().raycastTarget = false;
            controlsText = Label(controls.transform, "ControlsText", "", 14, new Vector2(10, -5), new Vector2(1040, 22), TextDim);
            controlsText.raycastTarget = false;
            controls.SetActive(false);
            crosshair = new GameObject("Crosshair", typeof(RectTransform), typeof(Image));
            crosshair.transform.SetParent(transform, false);
            var chr = (RectTransform)crosshair.transform; chr.anchorMin = chr.anchorMax = chr.pivot = new Vector2(0.5f, 0.5f); chr.sizeDelta = new Vector2(6, 6);
            var chi = crosshair.GetComponent<Image>(); chi.color = new Color(0.95f, 0.93f, 0.88f, 0.85f); chi.raycastTarget = false;
            crosshair.SetActive(false);

            // 右下：模式 / 手册 / 调试
            var tools = Panel("Tools", transform, new Vector2(1, 0), new Vector2(-16 - 492, 16 + 52), new Vector2(492, 52)); toolsRect = tools;
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
            manualPanel.GetComponent<Image>().color = new Color(PanelBg.r, PanelBg.g, PanelBg.b, 0.97f);   // 手册是模态：底色接近不透明，后面的反馈条文字不透出来
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
            if (flow.Rig.FirstPersonEnabled) { flow.Rig.Walk(); return; }       // 第一人称场景：从固定机位回到行走
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
            if (input != null) input.ModalBlocked = manualPanel.activeSelf;     // 手册展开：不悬停、不点后方 3D；关上恢复
            if (manualPanel.activeSelf) tooltip.SetActive(false);
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

        void OnActed(string target, bool ok, string msg) { lastActedMessage = msg; lastActedOk = ok; localMsg = null; }

        void OnOutOfReach(Component hit, float d)
        {
            localMsg = $"够不着{FirstOrderInput.NameOf(hit)}（离眼睛 {d:F1} m，伸手 {flow.Rig.Walker.Reach:F1} m 以内）。走近一点再点。";
            localMsgAtFlow = flow.Message;
        }

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
            bool fp = flow.Rig.FirstPersonEnabled;
            backLabel.text = fp ? "← 行走" : "← 返回";
            buttons["back"].interactable = fp ? !flow.Rig.Walking : history.Count > 0;
            UpdateFirstPersonHud();

            // 离座由 FirstOrderFlow 自己移动七号（当前维修座没有“离座”状态），这时按流程步骤显示
            string where = flow.Step >= FoStep.Retest ? "七号离座悬停" : SliceObservation.DockStateText(flow.Dock.State);
            stepText.text = $"步骤 {(int)flow.Step + 1}/17 · 维修座：{where} · 镜头：{SliceObservation.ShortLabel(cur)}";
            hintText.text = "下一步：" + flow.NextHint();
            bool refusal = !lastActedOk && flow.Message == lastActedMessage;
            if (localMsg != null && flow.Message != localMsgAtFlow) localMsg = null;   // 流程有了新消息：本地提示让位
            if (localMsg != null) refusal = true;
            LastFeedbackWasRefusal = refusal;
            feedbackText.text = localMsg != null ? "不行：" + localMsg : refusal ? "不行：" + flow.Message : flow.Message;
            feedbackBg.color = refusal ? RefuseBg : OkBg;

            if (input != null) input.ModalBlocked = manualPanel.activeSelf;
            if (manualPanel.activeSelf) manualDiag.text = DiagnosisText();
            UpdateTooltip();
        }

        public string DiagnosisText()
        {
            var sb = new StringBuilder();
            var items = SliceObservation.DiagnosisItems.Where(i => DiagnosisItemVisible == null || DiagnosisItemVisible(i.key)).ToList();
            foreach (var (key, label) in items)
                sb.Append(Observation.HasSeen(key) ? "【已看】" : "【未看】").Append(label).Append("    ");
            sb.AppendLine();
            sb.Append($"已看 {items.Count(i => Observation.HasSeen(i.key))}/{items.Count}。");
            sb.Append(flow.BearingReplaced ? "左上轴承已更换。" : flow.BearingLocated ? "已定位磨损轴承。" : "");
            sb.Append(flow.ClogCleared ? "进气口已清理。" : "");
            return sb.ToString();
        }

        void UpdateFirstPersonHud()
        {
            var rig = flow.Rig;
            bool fp = rig.FirstPersonEnabled;
            if (controls.activeSelf != fp) controls.SetActive(fp);
            bool aim = fp && rig.Walker.Aiming && input != null && input.enabled;
            if (crosshair.activeSelf != aim) crosshair.SetActive(aim);
            if (!fp) return;
            controlsText.text = rig.Walking
                ? (rig.Walker.Aiming
                    ? "WASD 移动 · Shift 快走 · C 蹲下 · 鼠标转头 · 准星对准：左键操作、右键观察 · Tab 放开鼠标点界面"
                    : "鼠标已放开：可以点界面按钮。点一下画面（不在按钮上）或按 Tab，回到准星。")
                : "固定机位（近看）：直接用鼠标点。按 WASD 或「← 行走」回到行走。";
        }

        void UpdateTooltip()
        {
            var mouse = Mouse.current;
            var h = input != null ? input.Hovered : null;
            bool walking = flow.Rig.Walking;
            if (mouse == null || h == null || input.PointerOverUI || manualPanel.activeSelf || walking && !flow.Rig.Walker.Aiming) { tooltip.SetActive(false); return; }
            tooltipText.text = FirstOrderInput.NameOf(h) + "\n" +
                (input.HoverOutOfReach ? $"够不着（{input.HoverDistance:F1} m），走近一点 · 右键：观察" : input.ObserveMode ? "左键：观察" : "左键：操作 · 右键：观察");
            // 高度随文字（名字长时换行不溢出），宽度固定
            const float w = 320f;
            var tr = tooltipText.rectTransform; tr.sizeDelta = new Vector2(w - 16f, 200f);
            float hgt = Mathf.Max(54f, tooltipText.preferredHeight + 14f);
            tooltipRect.sizeDelta = new Vector2(w, hgt); tr.sizeDelta = new Vector2(w - 16f, hgt - 10f);
            var pointer = walking ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) : mouse.position.ReadValue();   // 行走：提示跟着准星
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, pointer, null, out var local);
            var size = canvasRect.rect.size;
            if (PlaceTooltip(local + size * 0.5f, new Vector2(w, hgt), size, TooltipAvoidRects(), out var topLeft))
            {
                tooltip.SetActive(true);
                tooltipRect.anchoredPosition = topLeft;
            }
            else tooltip.SetActive(false);                                           // 四个方向都会压到按钮或出界：宁可不显示
        }

        Rect CanvasRect(RectTransform rt)
        {
            var c = new Vector3[4]; rt.GetWorldCorners(c);
            var size = canvasRect.rect.size;
            Vector2 a = (Vector2)canvasRect.InverseTransformPoint(c[0]) + size * 0.5f, b = (Vector2)canvasRect.InverseTransformPoint(c[2]) + size * 0.5f;
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>
        /// 悬停提示放在哪里（画布坐标，左下为原点；返回提示框左上角）：依次试光标右下、左下、右上、左上（离光标 18 像素），
        /// 第一个完全在画布内（留 4 像素）且不压到 avoid 区域（各外扩 4 像素）的位置。都不行返回 false。
        /// </summary>
        public static bool PlaceTooltip(Vector2 cursor, Vector2 box, Vector2 canvas, IList<Rect> avoid, out Vector2 topLeft)
        {
            const float gap = 18f, margin = 4f;
            var cands = new[] {
                new Vector2(cursor.x + gap, cursor.y - gap), new Vector2(cursor.x - gap - box.x, cursor.y - gap),
                new Vector2(cursor.x + gap, cursor.y + gap + box.y), new Vector2(cursor.x - gap - box.x, cursor.y + gap + box.y) };
            foreach (var tl in cands)
            {
                var r = new Rect(tl.x, tl.y - box.y, box.x, box.y);
                if (r.xMin < margin || r.yMin < margin || r.xMax > canvas.x - margin || r.yMax > canvas.y - margin) continue;
                bool hit = false;
                foreach (var a in avoid) { var e = new Rect(a.x - margin, a.y - margin, a.width + 2 * margin, a.height + 2 * margin); if (e.Overlaps(r)) { hit = true; break; } }
                if (hit) continue;
                topLeft = tl; return true;
            }
            topLeft = Vector2.zero; return false;
        }
    }
}
