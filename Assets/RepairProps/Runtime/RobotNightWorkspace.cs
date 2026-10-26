using System.Collections.Generic;
using System.Linq;
using BorderRepair.FirstOrder;
using BorderRepair.FirstOrder.Slice;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // The UNIT 07 nights (night 1 after closing, night 2) in the desktop clinic, laid out like the bench workspace
    // (RepairPropContextPanel): a right-hand column of windows — work order, tools, camera views, log — and the visor's
    // guide band at the bottom. Display only: it mirrors the robot slice HUD (SliceView) and the robot director's
    // status line, forwards its buttons to theirs, and hides their old panels while it is shown. The flow, the preset
    // views, the manual, the observation card and the tooltip are untouched.
    [AddComponentMenu("")]
    public sealed class RobotNightWorkspace : MonoBehaviour
    {
        public const string ScenePath = RepairBenchV2Runtime.ScenePath;
        static readonly Color PanelBg = RepairPropCyberStyle.Field, Rule = RepairPropCyberStyle.AmberDim;
        static readonly Color Ink = RepairPropCyberStyle.Ink, Dim = RepairPropCyberStyle.Dim, Accent = RepairPropCyberStyle.Amber;
        static readonly Color Warn = RepairPropCyberStyle.Warn, Info = RepairPropCyberStyle.Hot;
        static readonly Color ButtonBg = new Color(.02f, .03f, .035f, .55f);
        static readonly string[] OldPanels = { "Status", "Feedback", "CameraBar", "Controls", "Tools" };
        public static RobotNightWorkspace Current { get; private set; }

        UnifiedClinicDirector clinic;
        SliceView view;
        Canvas sliceCanvas, canvas;
        FirstOrderInput input;
        Font font;
        RectTransform column, guideBand;
        Text title, subtitle, statusTitle, stepLine, hintLine, toolStatus, feedback, caseCode, hint, controls, visorId, clock;
        Button act, observe, manual, debug, back;
        readonly Dictionary<string, Button> views = new Dictionary<string, Button>();
        readonly List<Canvas> hidden = new List<Canvas>();
        RectTransform captionPanel, observationPanel, manualPanel;
        Vector2 captionHome, observationHome, observationAnchor, manualHome;
        readonly Vector3[] corners = new Vector3[4];
        bool shown;

        public bool Visible => shown;
        public RectTransform PanelRectTransform => column;
        public Button ViewButton(string shot) => views.TryGetValue(shot, out var button) ? button : null;
        public Button ActButton => act;
        public Button ObserveButton => observe;
        public Button ManualButton => manual;
        public Button BackButton => back;
        public string StepText => stepLine != null ? stepLine.text : "";
        public string FeedbackText => feedback != null ? feedback.text : "";
        public string GuideText => hint != null ? hint.text : "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() { SceneManager.sceneLoaded -= OnSceneLoaded; Current = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.path, ScenePath, System.StringComparison.OrdinalIgnoreCase)) return;
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;
            var host = new GameObject("RobotNightWorkspace (runtime only)");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.AddComponent<RobotNightWorkspace>();
        }

        void Awake()
        {
            Current = this;
            font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            var host = Rect("RobotNightCanvas", transform);
            canvas = host.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9;   // under the slice HUD (10): its manual, observation card and tooltip stay on top
            var scaler = host.gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            host.gameObject.AddComponent<GraphicRaycaster>();

            column = Rect("RobotNightPanel", host);
            column.anchorMin = column.anchorMax = column.pivot = new Vector2(1, 1); column.anchoredPosition = new Vector2(-8, -42);
            column.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            Column(column, 0, 4);
            column.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 1. Work order: the robot, the night's money, where the job stands and the next step.
            var caseWindow = Window("CaseWindow", "工单 // CASE", false, 0f, out var caseBar, 0);
            caseCode = RepairPropCyberStyle.Caption(caseBar.transform.parent, "", RepairPropCyberStyle.Mono != null ? RepairPropCyberStyle.Mono : font, 10, RepairPropCyberStyle.AmberDim, TextAnchor.MiddleRight);
            var codeRect = caseCode.rectTransform; codeRect.anchorMin = Vector2.zero; codeRect.anchorMax = Vector2.one; codeRect.offsetMin = new Vector2(110, 0); codeRect.offsetMax = new Vector2(-34, 0);
            title = Label(caseWindow, "", 16, 22, Accent); title.fontStyle = FontStyle.Bold;
            subtitle = Paragraph(caseWindow, 12, Dim);
            var statusCard = Rect("StatusCard", caseWindow);
            RepairPropCyberStyle.Framed(statusCard.gameObject.AddComponent<Image>(), new Color(0, 0, 0, 0), Rule, true);
            Column(statusCard, 6, 2);
            statusTitle = Label(statusCard, "", 13, 18, Accent); statusTitle.fontStyle = FontStyle.Bold;
            stepLine = Paragraph(statusCard, 12, Dim);
            hintLine = Paragraph(statusCard, 13, Ink); HudMotion.TypeOn(hintLine, 260);

            // 2. Tools: what the left button does, the manual, the debug readout, back to walking.
            var toolWindow = Window("ToolWindow", "工具 // TOOLS", false, .08f, out var toolBar);
            toolStatus = RepairPropCyberStyle.Caption(toolBar.transform.parent, "", font, 10, Dim, TextAnchor.MiddleRight);
            var statusRect = toolStatus.rectTransform; statusRect.anchorMin = Vector2.zero; statusRect.anchorMax = Vector2.one; statusRect.offsetMin = new Vector2(84, 0); statusRect.offsetMax = new Vector2(-30, 0);
            var modeRow = Row(toolWindow, 30, 4);
            act = Button(modeRow, "操作", () => Press("mode:act"), 0, 30, 14); act.name = "模式_操作";
            observe = Button(modeRow, "观察", () => Press("mode:observe"), 0, 30, 14); observe.name = "模式_观察";
            var commandRow = Row(toolWindow, 30, 4);
            manual = Button(commandRow, "手册", () => Press("manual"), 0, 30, 14); manual.name = "手册";
            debug = Button(commandRow, "调试", () => Press("debug"), 0, 30, 14); debug.name = "调试";
            back = Button(commandRow, "← 行走", () => Press("back"), 0, 30, 14); back.name = "返回";

            // 3. Views: the preset camera positions around UNIT 07, three to a row.
            var viewWindow = Window("ViewWindow", "镜头 // VIEWS", false, .16f, out _);
            RectTransform row = null;
            for (int i = 0; i < SliceView.CameraButtons.Length; i++)
            {
                if (i % 3 == 0) row = Row(viewWindow, 28, 4);
                var (shot, label) = SliceView.CameraButtons[i];
                var button = Button(row, label, () => Press("cam:" + shot), 0, 28, 13); button.name = "镜头_" + shot;
                views.Add(shot, button);
            }

            // 4. Log: the flow's last message (refusals in amber).
            var logWindow = Window("LogWindow", "日志 // LOG", false, .24f, out _, 2);
            feedback = Paragraph(logWindow, 13, Info); feedback.GetComponent<LayoutElement>().minHeight = 18; HudMotion.TypeOn(feedback, 200);

            // Guide band and visor marks, as on the bench's AR layer.
            guideBand = Rect("HintBand", host);
            var band = guideBand.gameObject.AddComponent<Image>(); band.raycastTarget = false;
            RepairPropCyberStyle.Framed(band, new Color(0, 0, 0, .62f), RepairPropCyberStyle.AmberDim);
            RepairPropCyberStyle.TitleBar(guideBand, "GUIDE // 指引", font, 16f);
            hint = Free(host, "Hint", 14, TextAnchor.LowerCenter, Accent); HudMotion.TypeOn(hint, 220);
            controls = Free(host, "Controls", 11, TextAnchor.LowerCenter, Dim);
            visorId = Free(host, "VisorId", 11, TextAnchor.UpperLeft, RepairPropCyberStyle.AmberFaint * new Color(1, 1, 1, 2.2f));
            if (RepairPropCyberStyle.Kana != null) visorId.font = RepairPropCyberStyle.Kana;
            visorId.text = "VSR-07 // 整備視界　ＡＲオーバーレイ";
            clock = Free(host, "Clock", 11, TextAnchor.LowerLeft, RepairPropCyberStyle.AmberDim);
            if (RepairPropCyberStyle.Mono != null) clock.font = RepairPropCyberStyle.Mono;

            canvas.enabled = false;
        }

        void Press(string id)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            var button = view != null ? view.GetButton(id) : null;
            if (button != null && button.interactable) button.onClick.Invoke();
        }

        // The robot director and its slice HUD are built only once the robot part of the night starts.
        bool Bind()
        {
            if (view != null) return true;
            if (clinic == null) clinic = FindObjectsByType<UnifiedClinicDirector>(FindObjectsSortMode.None).FirstOrDefault(c => c.gameObject.scene == gameObject.scene);
            view = FindObjectsByType<SliceView>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(v => v.gameObject.scene == gameObject.scene);
            if (view == null || view.GetButton("manual") == null) { view = null; return false; }
            sliceCanvas = view.GetComponent<Canvas>();
            input = FindObjectsByType<FirstOrderInput>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(i => i.gameObject.scene == gameObject.scene);
            foreach (var name in OldPanels)
            {
                var panel = view.transform.Find(name);
                if (panel == null) continue;
                hidden.Add(Layer(panel));
            }
            observationPanel = view.transform.Find("Observation") as RectTransform;
            if (observationPanel != null) { observationHome = observationPanel.anchoredPosition; observationAnchor = observationPanel.anchorMin; }
            manualPanel = view.ManualPanelRect;
            if (manualPanel != null) manualHome = manualPanel.anchoredPosition;
            return true;
        }

        void LateUpdate()
        {
            bool show = Bind() && view.isActiveAndEnabled && sliceCanvas != null && sliceCanvas.enabled;
            if (show != shown) Show(show);
            if (!show) return;
            var area = (RectTransform)canvas.transform;
            float width = Mathf.Clamp(area.rect.width * .26f, 300, 340);
            column.sizeDelta = new Vector2(Mathf.Min(width, Mathf.Max(240, area.rect.width - 16)), column.sizeDelta.y);
            RefreshCase();
            RefreshTools();
            feedback.text = Find("Feedback/Message");
            feedback.color = view.LastFeedbackWasRefusal ? Warn : Info;
            RefreshGuide(area.rect.size, area.rect.width - column.sizeDelta.x - 16);
            FitManual();
        }

        // The manual is a centred modal sized for the bare slice HUD; with the column up it centres in the space left
        // of the column instead, and the story caption steps aside while it is open (it would cover the notes field).
        void FitManual()
        {
            if (manualPanel == null) return;
            var parent = (RectTransform)manualPanel.parent;
            column.GetWorldCorners(corners);
            float columnLeft = parent.InverseTransformPoint(corners[0]).x;
            float left = parent.rect.xMin + 16, right = columnLeft - 16, width = manualPanel.rect.width;
            // anchoredPosition is the pivot; work in left edges. Only move when the home spot runs under the column.
            float pivot = width * manualPanel.pivot.x, homeLeft = manualHome.x - pivot;
            float edge = homeLeft + width <= right ? homeLeft : Mathf.Max(left, (left + right - width) / 2);
            manualPanel.anchoredPosition = new Vector2(edge + pivot, manualHome.y);
            if (captionPanel != null) Hide(Layer(captionPanel), view.ManualOpen);
        }

        void Show(bool on)
        {
            shown = on;
            canvas.enabled = on;
            // Each window replays its boot when the column comes back, as the bench workspace does.
            column.gameObject.SetActive(on);
            foreach (var layer in hidden) Hide(layer, on);
            var robot = clinic != null ? clinic.Robot : null;
            var hud = robot != null && robot.HudText != null ? robot.HudText.transform.parent : null;
            if (hud != null) Hide(Layer(hud), on);
            // The story caption sits above the guide band; the observation card opens on the left, clear of the column.
            if (captionPanel == null && robot != null && robot.CaptionText != null)
            { captionPanel = robot.CaptionText.transform.parent as RectTransform; if (captionPanel != null) captionHome = captionPanel.anchoredPosition; }
            if (captionPanel != null) captionPanel.anchoredPosition = on ? new Vector2(captionHome.x, captionHome.y + 50) : captionHome;
            if (captionPanel != null && !on) Hide(Layer(captionPanel), false);
            if (manualPanel != null && !on) manualPanel.anchoredPosition = manualHome;
            if (observationPanel != null)
            {
                observationPanel.anchorMin = observationPanel.anchorMax = on ? new Vector2(0, 1) : observationAnchor;
                observationPanel.anchoredPosition = on ? new Vector2(16, -140) : observationHome;
            }
        }

        void RefreshCase()
        {
            var run = TwoNightRun.Current;
            int bars = 3 + Mathf.FloorToInt(HudMotion.Noise(Mathf.Floor(Time.unscaledTime * 2.5f)) * 2.99f);
            caseCode.text = (run != null && !string.IsNullOrEmpty(run.unit07WorkOrderId) ? run.unit07WorkOrderId : "NSP-07") + " " + new string('|', bars) + new string('.', 5 - bars);
            title.text = Find("Status/Title");
            subtitle.text = run == null ? "" : "第 " + run.night + " 晚　现金 " + TwoNightUi.Money(run.Cash) + "　房租 " + TwoNightUi.Money(run.rent) +
                "（第 " + run.rentDueNight + " 晚到期）还差 " + TwoNightUi.Money(run.RentShortfall);
            var hud = clinic != null && clinic.Robot != null && clinic.Robot.HudText != null ? clinic.Robot.HudText.text : "";
            var lines = hud.Split('\n');
            statusTitle.text = lines.Length > 2 ? lines[2] : "七号";
            stepLine.text = Find("Status/Step");
            hintLine.text = Find("Status/Hint");
        }

        void RefreshTools()
        {
            bool observing = input != null && input.ObserveMode;
            Paint(act, !observing); Paint(observe, observing);
            Paint(manual, view.ManualOpen);
            Paint(debug, input != null && input.ShowHud);
            var oldBack = view.GetButton("back");
            back.interactable = oldBack != null && oldBack.interactable;
            back.GetComponentInChildren<Text>().text = Find("CameraBar/Back/Text", "← 行走");
            Paint(back, false);
            back.GetComponentInChildren<Text>().color = back.interactable ? Ink : Dim;
            var current = clinic != null && clinic.Robot != null ? clinic.Robot.Flow.Rig.Current : null;
            foreach (var pair in views) Paint(pair.Value, pair.Key == current);
            toolStatus.text = "左键 · " + (observing ? "观察" : "操作") + "　右键 · 观察";
        }

        void RefreshGuide(Vector2 size, float right)
        {
            hint.text = "提示 // " + Find("Status/Hint");
            controls.text = view.ControlsHintText.Length > 0 ? view.ControlsHintText : "左键 操作　·　右键 观察　·　镜头 / 返回 在右侧";
            // The band centres between the visor id in the bottom-left corner and the column.
            const float corner = 300;
            float centre = (corner + right) / 2, room = right - corner;
            Place(hint.rectTransform, new Vector2(centre, 44), new Vector2(Mathf.Min(room - 60, 760), 60));
            float hintHeight = hint.preferredHeight;
            Place(guideBand, new Vector2(centre, 12), new Vector2(Mathf.Min(room - 20, Mathf.Max(hint.preferredWidth, controls.preferredWidth) + 60), hintHeight + 40));
            Place(controls.rectTransform, new Vector2(centre, 20), new Vector2(Mathf.Min(room - 60, 760), 20));
            Pin(visorId.rectTransform, new Vector2(22, 40), new Vector2(360, 16));
            var run = TwoNightRun.Current;
            clock.text = "N" + (run != null ? Mathf.Max(1, run.night) : 1) + " // UNIT 07   " + (clinic != null && clinic.Robot != null ? SliceObservation.ShortLabel(clinic.Robot.Flow.Rig.Current) : "") + "   REC";
            clock.color = new Color(1f, .62f, .16f, Mathf.Repeat(Time.unscaledTime, 1.6f) < 1.1f ? .45f : .25f);
            Pin(clock.rectTransform, new Vector2(22, 22), new Vector2(320, 16));
        }

        // The old panels are hidden through a nested canvas of their own, not a CanvasGroup: the cyber skin's boot
        // animation owns their group alpha and their frames ignore parent groups. The panels stay active, so the
        // slice code keeps writing their text and the column mirrors it. Unity's fake null defeats ??, hence the checks.
        static Canvas Layer(Transform owner)
        {
            var layer = owner.GetComponent<Canvas>();
            if (layer != null) return layer;
            layer = owner.gameObject.AddComponent<Canvas>();
            owner.gameObject.AddComponent<GraphicRaycaster>();
            return layer;
        }

        static void Hide(Canvas layer, bool hide)
        {
            layer.enabled = !hide;
            var raycaster = layer.GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = !hide;
        }

        // Plain text of one of the slice HUD's labels (its rich-text colour tags dropped).
        string Find(string path, string fallback = "")
        {
            var text = view.transform.Find(path)?.GetComponent<Text>();
            if (text == null) return fallback;
            return System.Text.RegularExpressions.Regex.Replace(text.text, "<[^>]+>", "");
        }

        // ------------------------------------------------------------------------------------------------ widgets
        // The same widgets as the bench workspace (RepairPropContextPanel / SurgeryCloseupPanel).
        static void Paint(Button button, bool selected)
        {
            if (button == null) return;
            button.targetGraphic.color = selected ? new Color(1f, .62f, .16f, .13f) : ButtonBg;
            RepairPropCyberStyle.SetFrame(button, selected ? Accent : Rule);
            var text = button.GetComponentInChildren<Text>(); if (text != null) text.color = selected ? Accent : Ink;
            var box = button.transform.Find("Box") as RectTransform;
            if (box == null)
            {
                box = (RectTransform)new GameObject("Box", typeof(RectTransform)).transform; box.SetParent(button.transform, false);
                box.anchorMin = box.anchorMax = new Vector2(0, .5f); box.pivot = new Vector2(0, .5f); box.anchoredPosition = new Vector2(6, 0); box.sizeDelta = new Vector2(7, 7);
                var element = box.gameObject.AddComponent<LayoutElement>(); element.ignoreLayout = true;
                var img = box.gameObject.AddComponent<Image>(); img.raycastTarget = false;
            }
            var boxImage = box.GetComponent<Image>();
            boxImage.sprite = selected ? RepairPropCyberStyle.Fill : RepairPropCyberStyle.Frame; boxImage.type = Image.Type.Sliced;
            boxImage.color = selected ? Accent : RepairPropCyberStyle.AmberDim;
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false); return rect;
        }

        static void Column(RectTransform rect, int padding, float spacing)
        {
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>(); group.padding = new RectOffset(padding, padding, padding, padding);
            group.spacing = spacing; group.childControlWidth = group.childControlHeight = true; group.childForceExpandWidth = true; group.childForceExpandHeight = false;
        }

        static RectTransform Row(Transform parent, int height, float spacing = 4)
        {
            var row = Rect("Row", parent); Size(row, height);
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>(); group.spacing = spacing;
            group.childControlWidth = group.childControlHeight = true; group.childForceExpandWidth = true; group.childForceExpandHeight = false; return row;
        }

        static void Size(RectTransform rect, float height, float width = 0)
        {
            var element = rect.gameObject.AddComponent<LayoutElement>(); element.minHeight = element.preferredHeight = height; element.flexibleHeight = 0;
            if (width > 0) element.minWidth = element.preferredWidth = width;
            else { element.minWidth = 0; element.flexibleWidth = 1; }
        }

        Text Label(Transform parent, string value, int size, float height, Color? color = null)
        {
            var rect = Rect("Label", parent); Size(rect, height);
            var text = rect.gameObject.AddComponent<Text>(); text.font = font; text.fontSize = size; text.text = value;
            text.supportRichText = false; text.color = color ?? Ink; text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; text.raycastTarget = false; return text;
        }

        // Wrapped text whose layout height follows its own preferred height at the column's actual width.
        Text Paragraph(Transform parent, int size, Color color)
        {
            var rect = Rect("Text", parent);
            var element = rect.gameObject.AddComponent<LayoutElement>(); element.minHeight = size + 4; element.flexibleWidth = 1;
            var text = rect.gameObject.AddComponent<Text>(); text.font = font; text.fontSize = size; text.color = color; text.lineSpacing = 1.05f;
            text.supportRichText = false; text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow; text.raycastTarget = false; return text;
        }

        // Loose text on the canvas (guide band, visor marks), shadowed like the AR layer's.
        Text Free(Transform parent, string name, int size, TextAnchor anchor, Color color)
        {
            var rect = Rect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.alignment = anchor; text.color = color; text.raycastTarget = false;
            text.supportRichText = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            var shadow = rect.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, .9f); shadow.effectDistance = new Vector2(1, -1);
            return text;
        }

        // Bottom-centre placement in canvas units (origin bottom left).
        static void Place(RectTransform rect, Vector2 bottomCentre, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = bottomCentre; rect.sizeDelta = size;
        }

        static void Pin(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }

        // One window of the column: framed field, title bar, padded body; boots up window by window.
        // Rank 0 = primary (bright frame, solid amber bar), 1 = secondary, 2 = tertiary.
        RectTransform Window(string name, string caption, bool flexible, float delay, out Text bar, int rank = 1)
        {
            var window = Rect(name, column);
            var image = window.gameObject.AddComponent<Image>(); image.raycastTarget = true;
            RepairPropCyberStyle.Framed(image, rank == 2 ? new Color(.02f, .014f, .008f, .55f) : PanelBg, rank == 0 ? Accent : rank == 1 ? Rule : RepairPropCyberStyle.AmberFaint);
            if (rank == 0)
            {
                RepairPropCyberStyle.Texture(window, RepairPropCyberStyle.Dots, new Color(1f, .62f, .16f, .08f), "DotGrid");
                RepairPropCyberStyle.Texture(window, RepairPropCyberStyle.Dither, new Color(1f, .42f, .10f, .05f), "Dither");
            }
            var group = window.gameObject.AddComponent<VerticalLayoutGroup>(); group.padding = new RectOffset(6, 6, 0, 5); group.spacing = 3;
            group.childControlWidth = group.childControlHeight = true; group.childForceExpandWidth = true; group.childForceExpandHeight = false;
            var titleBar = Rect("TitleBar", window); Size(titleBar, rank == 2 ? 15 : 18);
            var barImage = titleBar.gameObject.AddComponent<Image>();
            barImage.color = rank == 0 ? Accent : rank == 1 ? new Color(0, 0, 0, .45f) : new Color(0, 0, 0, 0);
            Color barInk = rank == 0 ? Color.black : rank == 1 ? Accent : Dim;
            bar = Label(titleBar, caption, rank == 2 ? 9 : 11, rank == 2 ? 15 : 18, barInk);
            if (RepairPropCyberStyle.Kana != null) bar.font = RepairPropCyberStyle.Kana;
            var barRect = (RectTransform)bar.transform; barRect.anchorMin = Vector2.zero; barRect.anchorMax = Vector2.one; barRect.offsetMin = new Vector2(5, 0); barRect.offsetMax = new Vector2(-30, 0);
            var glyphs = RepairPropCyberStyle.Caption(titleBar, "- x", RepairPropCyberStyle.Mono != null ? RepairPropCyberStyle.Mono : font, 11, barInk, TextAnchor.MiddleRight);
            if (rank >= 1) { var rule = Rect("Rule", titleBar); rule.anchorMin = Vector2.zero; rule.anchorMax = new Vector2(1, 0); rule.sizeDelta = new Vector2(0, 1); var ri = rule.gameObject.AddComponent<Image>(); ri.color = rank == 1 ? Rule : RepairPropCyberStyle.AmberFaint; ri.raycastTarget = false; }
            HudWindowDrag.Attach(barImage, window, "robot/" + name);
            var glyphRect = glyphs.rectTransform; glyphRect.anchorMin = Vector2.zero; glyphRect.anchorMax = Vector2.one; glyphRect.offsetMin = Vector2.zero; glyphRect.offsetMax = new Vector2(-5, 0);
            var element = window.gameObject.AddComponent<LayoutElement>();
            if (flexible) { element.minHeight = 120; element.flexibleHeight = 1; }
            HudMotion.Boot(window.gameObject, delay);
            return window;
        }

        Button Button(Transform parent, string label, UnityEngine.Events.UnityAction action, float width, float height, int size)
        {
            var rect = Rect(label, parent); Size(rect, height, width);
            var image = rect.gameObject.AddComponent<Image>(); RepairPropCyberStyle.Framed(image, ButtonBg, Rule);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(action);
            var colors = button.colors; colors.normalColor = colors.selectedColor = new Color(.9f, .9f, .9f, 1); colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(.7f, .7f, .7f, 1); colors.disabledColor = new Color(.7f, .7f, .7f, 1); button.colors = colors;
            var text = Label(rect, label, size, height); text.alignment = TextAnchor.MiddleCenter;
            var textRect = (RectTransform)text.transform; textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.offsetMin = new Vector2(7, 3); textRect.offsetMax = new Vector2(-7, -3);
            return button;
        }

        void OnDestroy() { if (Current == this) Current = null; }
    }
}
