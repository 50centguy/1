using BorderRepair.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BorderRepair.EditorTools
{
    /// <summary>生成 uGUI 界面并连接到 RepairUIView 的序列化字段。参考分辨率 1920×1080。</summary>
    internal static class RepairUIBuilder
    {
        static readonly Color PanelColor = new Color(0.08f, 0.1f, 0.12f, 0.9f);
        static readonly Color TextColor = new Color(0.87f, 0.9f, 0.93f);
        static readonly Color TitleColor = new Color(1f, 0.72f, 0.3f);
        static readonly Color AccentButton = new Color(0.86f, 0.5f, 0.14f);
        static readonly Color ScanButton = new Color(0.12f, 0.46f, 0.56f);
        static readonly Color NeutralButton = new Color(0.24f, 0.28f, 0.33f);
        static readonly Color RepairButton = new Color(0.2f, 0.5f, 0.28f);
        static readonly Color ReplaceButton = new Color(0.62f, 0.46f, 0.12f);
        static readonly Color RefuseButton = new Color(0.62f, 0.2f, 0.16f);
        static readonly Color ScanCyan = new Color(0.3f, 0.9f, 1f, 0.85f);

        static Font font;

        public static RepairUIView Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("RepairUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.layer = LayerMask.NameToLayer("UI");
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            // 扫描覆盖层（放在最底层，不拦截点击）
            var overlay = Rect("ScanOverlay", root);
            Fill(overlay, 0, 0, 0, 0);
            EdgeBar(overlay, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -64), new Vector2(0, 6));
            EdgeBar(overlay, "Bottom", new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 6));
            EdgeBar(overlay, "Left", new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(6, 0));
            EdgeBar(overlay, "Right", new Vector2(1, 0), new Vector2(1, 1), Vector2.zero, new Vector2(6, 0));
            var overlayLabel = Label(overlay, "ScanLabel", "扫描模式 · 单击可疑部位进行扫描", 22, TextAnchor.MiddleCenter, FontStyle.Bold, ScanCyan);
            Place(overlayLabel.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 170), new Vector2(800, 34));
            Shadowed(overlayLabel);

            // 顶栏
            var top = Panel("TopBar", root, new Color(0.05f, 0.06f, 0.08f, 0.95f));
            Place(top.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 64));
            var caseHeader = Label(top.transform, "CaseHeader", "边境维修站", 26, TextAnchor.MiddleLeft, FontStyle.Bold);
            Place(caseHeader.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(560, 0));
            var stage = Label(top.transform, "Stage", "", 22, TextAnchor.MiddleCenter);
            Place(stage.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(740, 0));
            var timer = Label(top.transform, "Timer", "", 22, TextAnchor.MiddleRight);
            Place(timer.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(420, 0));

            // 顾客说明（左）
            var customer = Panel("CustomerPanel", root, PanelColor);
            Place(customer.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -84), new Vector2(440, 400));
            Title(customer.transform, "顾客说明");
            var customerText = Label(customer.transform, "CustomerText", "", 21, TextAnchor.UpperLeft);
            Fill(customerText.rectTransform, 18, 18, 58, 16);

            // 扫描记录（右上）
            var findings = Panel("FindingsPanel", root, PanelColor);
            Place(findings.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -84), new Vector2(460, 400));
            Title(findings.transform, "扫描记录");
            var scanStatus = Label(findings.transform, "ScanStatus", "", 19, TextAnchor.UpperLeft);
            Place(scanStatus.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -54), new Vector2(-36, 56));
            var findingsText = Label(findings.transform, "FindingsText", "", 18, TextAnchor.UpperLeft);
            Fill(findingsText.rectTransform, 18, 18, 116, 14);

            // 底部提示与操作按钮
            var hint = Label(root, "Hint", "", 20, TextAnchor.MiddleCenter);
            Place(hint.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 96), new Vector2(940, 64));
            Shadowed(hint);

            var actionRow = Rect("ActionRow", root);
            Place(actionRow, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(760, 60));
            var rowLayout = actionRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 16;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;
            var acceptButton = LayoutButton(actionRow, "AcceptButton", "接收物品", AccentButton, 240, 60, out _);
            var scanButton = LayoutButton(actionRow, "ScanButton", "扫描模式 [空格]", ScanButton, 240, 60, out var scanLabel);
            var diagnoseButton = LayoutButton(actionRow, "DiagnoseButton", "开始诊断", AccentButton, 240, 60, out _);

            // 诊断面板（右下）
            var diagnosis = ActionPanel(root, "DiagnosisPanel", "选择故障诊断");
            var optionContainer = Rect("Options", diagnosis.transform);
            Fill(optionContainer, 16, 16, 62, 84);
            var optionLayout = optionContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            optionLayout.spacing = 10;
            optionLayout.childControlWidth = optionLayout.childControlHeight = true;
            optionLayout.childForceExpandWidth = true;
            optionLayout.childForceExpandHeight = false;
            var optionTemplate = LayoutButton(optionContainer, "OptionTemplate", "诊断选项", NeutralButton, 0, 76, out var optionLabel);
            optionLabel.alignment = TextAnchor.MiddleLeft;
            optionLabel.fontSize = 20;
            var backButton = MakeButton(diagnosis.transform, "BackToInspectButton", "返回检查", NeutralButton, out _);
            Place((RectTransform)backButton.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(-32, 54));

            // 决定面板
            var decision = ActionPanel(root, "DecisionPanel", "处理决定");
            var decisionInfo = Label(decision.transform, "DecisionInfo", "", 18, TextAnchor.UpperLeft);
            decisionInfo.lineSpacing = 1f;
            Fill(decisionInfo.rectTransform, 18, 18, 56, 190);
            var decisionButtons = Rect("DecisionButtons", decision.transform);
            Place(decisionButtons, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(-32, 164));
            var decisionLayout = decisionButtons.gameObject.AddComponent<VerticalLayoutGroup>();
            decisionLayout.spacing = 10;
            decisionLayout.childControlWidth = decisionLayout.childControlHeight = true;
            decisionLayout.childForceExpandWidth = true;
            decisionLayout.childForceExpandHeight = false;
            var repairButton = LayoutButton(decisionButtons, "RepairButton", "维修", RepairButton, 0, 48, out _);
            var replaceButton = LayoutButton(decisionButtons, "ReplaceButton", "建议更换", ReplaceButton, 0, 48, out _);
            var refuseButton = LayoutButton(decisionButtons, "RefuseButton", "拒绝处理", RefuseButton, 0, 48, out _);

            // 结果面板
            var result = ActionPanel(root, "ResultPanel", null);
            var resultTitle = Label(result.transform, "ResultTitle", "", 24, TextAnchor.UpperLeft, FontStyle.Bold);
            Place(resultTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(-36, 64));
            var resultBody = Label(result.transform, "ResultBody", "", 19, TextAnchor.UpperLeft);
            Fill(resultBody.rectTransform, 18, 18, 86, 90);
            var nextButton = MakeButton(result.transform, "NextButton", "下一件", AccentButton, out var nextLabel);
            Place((RectTransform)nextButton.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(-32, 60));

            // 反馈提示（顶部中央）
            var feedback = Panel("Feedback", root, new Color(0.14f, 0.2f, 0.28f, 0.95f));
            feedback.raycastTarget = false;
            Place(feedback.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(900, 64));
            var feedbackText = Label(feedback.transform, "FeedbackText", "", 21, TextAnchor.MiddleCenter);
            Fill(feedbackText.rectTransform, 16, 16, 6, 6);

            // 营业总结（全屏）
            var summary = Panel("SummaryPanel", root, new Color(0f, 0f, 0f, 0.78f));
            Fill(summary.rectTransform, 0, 0, 0, 0);
            var summaryBox = Panel("SummaryBox", summary.transform, new Color(0.09f, 0.11f, 0.13f, 0.98f));
            Place(summaryBox.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1160, 720));
            var summaryText = Label(summaryBox.transform, "SummaryText", "", 22, TextAnchor.UpperLeft);
            Fill(summaryText.rectTransform, 44, 44, 40, 120);
            var restartButton = MakeButton(summaryBox.transform, "RestartButton", "重新开始", AccentButton, out _);
            Place((RectTransform)restartButton.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(300, 64));

            var view = canvasGo.AddComponent<RepairUIView>();
            PrototypeBuilder.Wire(view,
                ("caseHeaderText", caseHeader), ("stageText", stage), ("timerText", timer),
                ("customerText", customerText), ("scanStatusText", scanStatus), ("findingsText", findingsText),
                ("hintText", hint), ("acceptButton", acceptButton), ("scanButton", scanButton), ("scanButtonLabel", scanLabel), ("diagnoseButton", diagnoseButton),
                ("scanOverlay", overlay.gameObject),
                ("feedbackRoot", feedback.gameObject), ("feedbackBackground", feedback), ("feedbackText", feedbackText),
                ("diagnosisPanel", diagnosis.gameObject), ("diagnosisOptionContainer", optionContainer), ("diagnosisOptionTemplate", optionTemplate), ("backToInspectButton", backButton),
                ("decisionPanel", decision.gameObject), ("decisionInfoText", decisionInfo), ("repairButton", repairButton), ("replaceButton", replaceButton), ("refuseButton", refuseButton),
                ("resultPanel", result.gameObject), ("resultTitleText", resultTitle), ("resultBodyText", resultBody), ("nextButton", nextButton), ("nextButtonLabel", nextLabel),
                ("summaryPanel", summary.gameObject), ("summaryText", summaryText), ("restartButton", restartButton));

            optionTemplate.gameObject.SetActive(false);
            overlay.gameObject.SetActive(false);
            feedback.gameObject.SetActive(false);
            diagnosis.gameObject.SetActive(false);
            decision.gameObject.SetActive(false);
            result.gameObject.SetActive(false);
            summary.gameObject.SetActive(false);
            return view;
        }

        // ---------- helpers ----------

        static Image ActionPanel(Transform root, string name, string title)
        {
            var panel = Panel(name, root, PanelColor);
            Place(panel.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-20, 110), new Vector2(460, 480));
            if (title != null) Title(panel.transform, title);
            return panel;
        }

        static void Title(Transform parent, string text)
        {
            var t = Label(parent, "Title", text, 24, TextAnchor.MiddleLeft, FontStyle.Bold, TitleColor);
            Place(t.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(-36, 36));
        }

        static void EdgeBar(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            var img = Rect("Edge" + name, parent).gameObject.AddComponent<Image>();
            img.color = ScanCyan;
            img.raycastTarget = false;
            var pivot = new Vector2(anchorMin.x == anchorMax.x ? anchorMin.x : 0.5f, anchorMin.y == anchorMax.y ? anchorMin.y : 0.5f);
            Place(img.rectTransform, anchorMin, anchorMax, pivot, position, size);
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Image Panel(string name, Transform parent, Color color)
        {
            var img = Rect(name, parent).gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        static Text Label(Transform parent, string name, string text, int size, TextAnchor align, FontStyle style = FontStyle.Normal, Color? color = null)
        {
            var t = Rect(name, parent).gameObject.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.fontStyle = style;
            t.color = color ?? TextColor;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.1f;
            t.raycastTarget = false;
            return t;
        }

        static void Shadowed(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.85f);
            shadow.effectDistance = new Vector2(2, -2);
        }

        static Button MakeButton(Transform parent, string name, string label, Color color, out Text text)
        {
            var img = Panel(name, parent, color);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.normalColor = new Color(0.88f, 0.88f, 0.88f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.65f, 0.65f, 0.65f);
            colors.selectedColor = new Color(0.88f, 0.88f, 0.88f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.6f);
            button.colors = colors;

            text = Label(img.transform, "Label", label, 22, TextAnchor.MiddleCenter, FontStyle.Bold, Color.white);
            Fill(text.rectTransform, 14, 14, 4, 4);
            return button;
        }

        static Button LayoutButton(RectTransform parent, string name, string label, Color color, float width, float height, out Text text)
        {
            var button = MakeButton(parent, name, label, color, out text);
            var le = button.gameObject.AddComponent<LayoutElement>();
            if (width > 0) le.preferredWidth = width;
            le.preferredHeight = height;
            return button;
        }

        static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        static void Fill(RectTransform rt, float left, float right, float top, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }
    }
}
