using System;
using System.Collections.Generic;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BorderRepair.RepairProps.Cosmetics
{
    public sealed class CosmeticPanel : MonoBehaviour
    {
        static readonly Color Background = new Color(.10f, .11f, .11f, .98f);
        static readonly Color Normal = new Color(.23f, .25f, .25f, 1);
        static readonly Color Active = new Color(.12f, .48f, .40f, 1);
        static readonly Color[] Colors = { Color.white, new Color(.12f, .13f, .13f),
            new Color(.83f, .22f, .20f), new Color(.94f, .74f, .18f),
            new Color(.12f, .65f, .62f), new Color(.22f, .48f, .80f) };
        readonly List<Texture2D> options = new List<Texture2D>();
        readonly List<RaycastResult> hits = new List<RaycastResult>();
        readonly Button[] modes = new Button[4];
        readonly Button[] swatches = new Button[6];
        CosmeticController owner;
        RectTransform root;
        Canvas desktopCanvas;
        GameObject body;
        Font font;
        float unit = 1;
        bool vr, collapsed, confirming, refreshing;
        Button collapse, undo, redo, clear, apply, confirm, cancel;
        Text radiusValue, opacityValue, rotationValue, status, confirmation;
        Slider radius, opacity, rotation;
        Dropdown dropdown;
        RawImage preview;
        LayoutElement rootSize;
        RectTransform header;
        string lastStatus;
        readonly Vector3[] slotCorners = new Vector3[4];

        public void Initialize(CosmeticController owner, bool worldSpace = false, Camera camera = null)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (this.owner != null) this.owner.Changed -= Refresh;
            if (root != null)
            {
                var old = desktopCanvas != null ? desktopCanvas.gameObject : root.gameObject;
                old.SetActive(false);
                Destroy(old);
            }
            this.owner = owner;
            vr = worldSpace || owner.IsVR;
            unit = vr ? 2 : 1;
            collapsed = !vr;
            confirming = false;
            lastStatus = null;
            desktopCanvas = null;
            font = UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            Transform host = transform;
            if (!vr)
            {
                var canvasObject = new GameObject("CosmeticCanvas", typeof(RectTransform), typeof(Canvas),
                    typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasObject.transform.SetParent(transform, false);
                desktopCanvas = canvasObject.GetComponent<Canvas>();
                desktopCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                desktopCanvas.sortingOrder = 70;
                var scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 720);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = .5f;
                host = canvasObject.transform;
            }
            // VR inherits the host canvas and its tracked-device raycaster unchanged.
            root = Rect("外观面板", host);
            root.gameObject.AddComponent<Image>().color = Background;
            rootSize = root.gameObject.AddComponent<LayoutElement>();
            rootSize.flexibleWidth = 1;
            var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)(8 * unit), (int)(8 * unit), (int)(8 * unit), (int)(8 * unit));
            layout.spacing = 4 * unit;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            if (!vr)
            {
                root.anchorMin = root.anchorMax = Vector2.zero;
                root.pivot = Vector2.zero;
                root.anchoredPosition = new Vector2(12, 86);
            }
            else
            {
                root.anchorMin = new Vector2(0, 1);
                root.anchorMax = Vector2.one;
                root.pivot = new Vector2(.5f, 1);
                root.sizeDelta = new Vector2(0, 700);
            }
            header = Row(root, 24);
            Label(header, "外观", 0);
            collapse = Command(header, "-", () =>
            {
                collapsed = !collapsed;
                confirming = false;
                dropdown.Hide();
                Refresh();
            }, 30);
            var content = Rect("内容", root);
            body = content.gameObject;
            var column = body.AddComponent<VerticalLayoutGroup>();
            column.spacing = 4 * unit;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            var bodySize = body.AddComponent<LayoutElement>();
            bodySize.preferredHeight = 294 * unit;
            bodySize.flexibleHeight = 1;

            var modeRow = Row(content, 28);
            string[] modeNames = { "维修", "喷涂", "贴花", "局部换图" };
            for (int i = 0; i < modes.Length; i++)
            {
                var mode = (CosmeticMode)i;
                modes[i] = Command(modeRow, modeNames[i], () =>
                {
                    confirming = false;
                    this.owner.SetMode(mode);
                    Refresh();
                });
            }
            var colorRow = Row(content, 22);
            Label(colorRow, "颜色", 50);
            for (int i = 0; i < swatches.Length; i++)
            {
                int index = i;
                swatches[i] = Command(colorRow, "", () => { this.owner.PaintColor = Colors[index]; Refresh(); }, 22);
                swatches[i].gameObject.name = "色块_" + i;
                swatches[i].GetComponent<Image>().color = Colors[i];
                var outline = swatches[i].gameObject.AddComponent<Outline>();
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(2 * unit, 2 * unit);
                outline.enabled = false;
            }
            var spacer = Rect("留白", colorRow).gameObject.AddComponent<LayoutElement>();
            spacer.flexibleWidth = 1;
            radius = SliderRow(content, "半径", .003f, .04f, out radiusValue,
                value => { this.owner.Radius = value; Refresh(); });
            opacity = SliderRow(content, "不透明", .1f, 1, out opacityValue,
                value => { this.owner.Opacity = value; Refresh(); });
            rotation = SliderRow(content, "旋转", 0, 360, out rotationValue,
                value => { this.owner.Rotation = value; Refresh(); });

            var imageRow = Row(content, 34);
            var imageBox = Rect("图案预览", imageRow);
            Size(imageBox, 34, 34);
            imageBox.gameObject.AddComponent<Image>().color = new Color(.28f, .29f, .29f);
            var imageRect = Rect("图片", imageBox);
            Stretch(imageRect, 0);
            preview = imageRect.gameObject.AddComponent<RawImage>();
            preview.raycastTarget = false;
            var aspect = imageRect.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            dropdown = ImageDropdown(imageRow);
            var catalogRow = Row(content, 24);
            Command(catalogRow, "重载图案", () => { CosmeticCatalog.Reload(); Refresh(); });
            Command(catalogRow, "图片目录", () => { CosmeticCatalog.OpenFolder(); Refresh(); });
            var history = Row(content, 24);
            undo = Command(history, "撤销", () => { this.owner.Undo(); Refresh(); });
            redo = Command(history, "重做", () => { this.owner.Redo(); Refresh(); });
            clear = Command(history, "清空", () => { confirming = true; Refresh(); });
            var action = Row(content, 24);
            apply = Command(action, "整壳换图", () => { this.owner.ReplaceWholeShell(); Refresh(); });
            confirmation = Label(action, "清空涂装？", 0);
            confirm = Command(action, "确认", () => { this.owner.Clear(); confirming = false; Refresh(); });
            cancel = Command(action, "取消", () => { confirming = false; Refresh(); });
            BuildStatus(content);
            this.owner.Changed += Refresh;
            // The desktop repair workspace may host this editor in its appearance page; brush ownership stays here.
            if (!vr) RepairPropWorkspaceLayout.BindAppearance(this, () => this.owner != null && this.owner.Mode != CosmeticMode.Off,
                () => { if (this.owner != null) this.owner.SetMode(CosmeticMode.Off); Refresh(); });
            Refresh();
        }

        public void Refresh()
        {
            if (owner == null || root == null || refreshing) return;
            refreshing = true;
            try
            {
                // Hosted by the desktop repair workspace: shown only on its appearance page, docked in its slot.
                // While that workspace is hidden (the parts shop is over it) the editor hides too, instead of
                // falling back to its standalone corner.
                bool hosted = !vr && RepairPropWorkspaceLayout.Hosting;
                bool workspace = !vr && RepairPropWorkspaceLayout.Host != null;
                var slot = hosted ? RepairPropWorkspaceLayout.AppearanceSlot : null;
                root.gameObject.SetActive(isActiveAndEnabled && (vr ||
                    owner.Clinic != null && owner.Clinic.ConsoleOpen && owner.Flow != null && (!workspace || slot != null)));
                if (hosted) collapsed = false;
                header.gameObject.SetActive(!hosted);
                modes[0].GetComponentInChildren<Text>().text = hosted ? "浏览" : "维修";
                if (!owner.Available) confirming = false;
                body.SetActive(!collapsed);
                collapse.GetComponentInChildren<Text>().text = collapsed ? "+" : "-";
                float height = collapsed ? 40 * unit : vr ? 700 : 338;
                rootSize.preferredHeight = rootSize.minHeight = height;
                if (!vr)
                {
                    var canvasRect = (RectTransform)desktopCanvas.transform;
                    root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
                    if (slot != null)
                    {
                        // Both are screen-space overlay canvases, so world corners are screen pixels.
                        slot.GetWorldCorners(slotCorners);
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, slotCorners[0], null, out var min);
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, slotCorners[2], null, out var max);
                        root.anchoredPosition = min - canvasRect.rect.min;
                        root.sizeDelta = new Vector2(Mathf.Max(0, max.x - min.x), Mathf.Max(height, max.y - min.y));
                    }
                    else
                    {
                        root.anchoredPosition = new Vector2(12, 86);
                        root.sizeDelta = new Vector2(Mathf.Min(440, Mathf.Max(0, canvasRect.rect.width - 24)), height);
                    }
                }
                else root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                bool available = owner.Available;
                for (int i = 0; i < modes.Length; i++)
                {
                    modes[i].interactable = i == 0 || available;
                    modes[i].GetComponent<Image>().color = (int)owner.Mode == i ? Active : Normal;
                }
                foreach (var swatch in swatches)
                {
                    swatch.interactable = available && owner.Mode == CosmeticMode.Spray && !confirming;
                    swatch.GetComponent<Outline>().enabled =
                        ((Vector4)swatch.GetComponent<Image>().color - (Vector4)owner.PaintColor).sqrMagnitude < .001f;
                }
                radius.SetValueWithoutNotify(owner.Radius);
                opacity.SetValueWithoutNotify(owner.Opacity);
                rotation.SetValueWithoutNotify(owner.Rotation);
                radiusValue.text = (owner.Radius * 1000).ToString("0.#") + " mm";
                opacityValue.text = (owner.Opacity * 100).ToString("0") + "%";
                rotationValue.text = owner.Rotation.ToString("0") + "°";
                bool brush = owner.Mode == CosmeticMode.Spray || owner.Mode == CosmeticMode.Decal;
                radius.interactable = opacity.interactable = available && brush && !confirming;
                rotation.interactable = available && owner.Mode == CosmeticMode.Decal && !confirming;
                RefreshImages();
                dropdown.interactable = available && !confirming && options.Count > 0;
                undo.interactable = available && owner.CanUndo && !confirming;
                redo.interactable = available && owner.CanRedo && !confirming;
                clear.interactable = available && !confirming;
                apply.gameObject.SetActive(!confirming);
                apply.interactable = available && owner.Mode == CosmeticMode.Replace && CosmeticCatalog.Selected != null;
                confirmation.gameObject.SetActive(confirming);
                confirm.gameObject.SetActive(confirming);
                cancel.gameObject.SetActive(confirming);
                confirm.interactable = available;
                string message = owner.Feedback;
                if (!string.IsNullOrEmpty(message)) message += "\n";
                message += CosmeticCatalog.Feedback;
                if (message != lastStatus)
                {
                    lastStatus = status.text = message;
                    var statusScroll=status.GetComponentInParent<ScrollRect>(true);
                    if(statusScroll!=null) statusScroll.verticalNormalizedPosition = 1;
                }
            }
            finally { refreshing = false; }
        }

        public bool BlocksPointer(Vector2 screen)
        {
            return !vr && isActiveAndEnabled && root != null && root.gameObject.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(root, screen, null);
        }

        public bool PointerOverUI(Vector2 screen)
        {
            var events = EventSystem.current;
            if (events == null) return BlocksPointer(screen);
            hits.Clear();
            events.RaycastAll(new PointerEventData(events) { position = screen }, hits);
            return hits.Count > 0 || BlocksPointer(screen);
        }

        void RefreshImages()
        {
            var images = CosmeticCatalog.Images;
            bool changed = images.Count != options.Count;
            if (!changed)
                for (int i = 0; i < images.Count; i++)
                    if (images[i] != options[i]) { changed = true; break; }
            if (changed)
            {
                dropdown.Hide();
                options.Clear();
                var labels = new List<Dropdown.OptionData>();
                foreach (var image in images)
                {
                    options.Add(image);
                    labels.Add(new Dropdown.OptionData(image != null ? image.name : "图片已释放"));
                }
                dropdown.ClearOptions();
                dropdown.AddOptions(labels);
            }
            int selected = options.IndexOf(CosmeticCatalog.Selected);
            dropdown.SetValueWithoutNotify(Mathf.Max(0, selected));
            dropdown.RefreshShownValue();
            preview.texture = CosmeticCatalog.Selected;
            preview.enabled = preview.texture != null;
            if (preview.texture != null)
                preview.GetComponent<AspectRatioFitter>().aspectRatio = (float)preview.texture.width / preview.texture.height;
        }

        RectTransform Rect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        void Size(RectTransform rect, float height, float width = 0)
        {
            var size = rect.gameObject.AddComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = height * unit;
            if (width > 0) size.minWidth = size.preferredWidth = width * unit;
            else { size.minWidth = 0; size.flexibleWidth = 1; }
        }

        RectTransform Row(Transform parent, float height)
        {
            var row = Rect("行", parent);
            Size(row, height);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4 * unit;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return row;
        }

        static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        Text Label(Transform parent, string text, float width)
        {
            var rect = Rect("文字", parent);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = vr ? 26 : 14;
            label.color = Color.white;
            label.text = text;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.alignment = TextAnchor.MiddleLeft;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = vr ? 18 : 10;
            label.resizeTextMaxSize = label.fontSize;
            Size(rect, 22, width);
            return label;
        }

        Button Command(Transform parent, string text, UnityEngine.Events.UnityAction action, float width = 0)
        {
            var rect = Rect(string.IsNullOrEmpty(text) ? "色块" : text, parent);
            Size(rect, 22, width);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Normal;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(action);
            var label = Label(rect, text, 0);
            label.alignment = TextAnchor.MiddleCenter;
            Stretch((RectTransform)label.transform, 2 * unit);
            return button;
        }

        Slider SliderRow(Transform parent, string name, float min, float max, out Text value,
            UnityEngine.Events.UnityAction<float> changed)
        {
            var row = Row(parent, 22);
            Label(row, name, 50);
            var rect = Rect(name, row);
            Size(rect, 22);
            rect.gameObject.AddComponent<Image>().color = Color.clear;
            var slider = rect.gameObject.AddComponent<Slider>();
            var track = Rect("轨道", rect);
            Stretch(track, 0);
            track.offsetMin = new Vector2(0, 9 * unit);
            track.offsetMax = new Vector2(0, -9 * unit);
            track.gameObject.AddComponent<Image>().color = Normal;
            var area = Rect("滑动区", rect);
            Stretch(area, 8 * unit);
            var thumb = Rect("滑块", area);
            thumb.sizeDelta = new Vector2(16 * unit, 14 * unit);
            var graphic = thumb.gameObject.AddComponent<Image>();
            graphic.color = new Color(.72f, .83f, .78f);
            slider.handleRect = thumb;
            slider.targetGraphic = graphic;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.onValueChanged.AddListener(v => { if (!refreshing) changed(v); });
            value = Label(row, "", 62);
            value.alignment = TextAnchor.MiddleRight;
            return slider;
        }

        Dropdown ImageDropdown(Transform parent)
        {
            var rect = Rect("图案选择", parent);
            Size(rect, 30);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Normal;
            var result = rect.gameObject.AddComponent<Dropdown>();
            result.targetGraphic = image;
            result.navigation = new Navigation { mode = Navigation.Mode.None };
            var caption = Label(rect, "", 0);
            Stretch((RectTransform)caption.transform, 5 * unit);
            ((RectTransform)caption.transform).offsetMax = new Vector2(-24 * unit, -5 * unit);
            result.captionText = caption;
            var arrow = Label(rect, "v", 0);
            var ar = (RectTransform)arrow.transform;
            ar.anchorMin = new Vector2(1, 0);
            ar.anchorMax = Vector2.one;
            ar.pivot = new Vector2(1, .5f);
            ar.sizeDelta = new Vector2(24 * unit, 0);
            arrow.alignment = TextAnchor.MiddleCenter;
            var template = Rect("选项", rect);
            template.anchorMin = new Vector2(0, 1);
            template.anchorMax = Vector2.one;
            template.pivot = new Vector2(.5f, 0);
            template.anchoredPosition = new Vector2(0, 2 * unit);
            template.sizeDelta = new Vector2(0, 156 * unit);
            template.gameObject.AddComponent<Image>().color = Background;
            var scroll = template.gameObject.AddComponent<ScrollRect>();
            var viewport = Rect("可视区", template);
            Stretch(viewport, 2 * unit);
            viewport.gameObject.AddComponent<Image>().color = Background;
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var content = Rect("选项内容", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1);
            content.sizeDelta = new Vector2(0, 26 * unit);
            var item = Rect("选项行", content);
            item.anchorMin = Vector2.zero;
            item.anchorMax = new Vector2(1, 0);
            item.pivot = new Vector2(.5f, .5f);
            item.sizeDelta = new Vector2(0, 26 * unit);
            item.anchoredPosition = new Vector2(0, 13 * unit);
            var itemImage = item.gameObject.AddComponent<Image>();
            itemImage.color = Normal;
            var toggle = item.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = itemImage;
            var selection = Rect("已选", item);
            Stretch(selection, 1 * unit);
            toggle.graphic = selection.gameObject.AddComponent<Image>();
            toggle.graphic.color = Active;
            var itemText = Label(item, "", 0);
            Stretch((RectTransform)itemText.transform, 4 * unit);
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 26 * unit;
            result.template = template;
            result.itemText = itemText;
            result.onValueChanged.AddListener(index =>
            {
                if (refreshing || index < 0 || index >= options.Count) return;
                CosmeticCatalog.Selected = options[index];
                Refresh();
            });
            template.gameObject.SetActive(false);
            return result;
        }

        void BuildStatus(Transform parent)
        {
            var rect = Rect("当前状态", parent);
            Size(rect, 36);
            rect.GetComponent<LayoutElement>().flexibleHeight = 1;
            rect.gameObject.AddComponent<Image>().color = Background;
            var scroll = rect.gameObject.AddComponent<ScrollRect>();
            rect.gameObject.AddComponent<RectMask2D>();
            status = Label(rect, "", 0);
            status.resizeTextForBestFit = false;
            status.fontSize = vr ? 22 : 12;
            status.alignment = TextAnchor.UpperLeft;
            status.verticalOverflow = VerticalWrapMode.Overflow;
            var textRect = (RectTransform)status.transform;
            textRect.anchorMin = new Vector2(0, 1);
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(.5f, 1);
            textRect.sizeDelta = Vector2.zero;
            var fitter = status.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = textRect;
            scroll.viewport = rect;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 18 * unit;
        }

        void OnEnable() { Refresh(); }
        void OnDisable()
        {
            if (dropdown != null) dropdown.Hide();
            if (root != null) root.gameObject.SetActive(false);
        }
        void OnDestroy()
        {
            if (owner != null) owner.Changed -= Refresh;
            RepairPropWorkspaceLayout.UnbindAppearance(this);
            if (desktopCanvas != null) Destroy(desktopCanvas.gameObject);
            else if (root != null) Destroy(root.gameObject);
        }
    }
}
