using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BorderRepair.TwoNight
{
    /// <summary>两晚切片界面的小工具：和切片鼠标界面同一套 UGUI（legacy Text + Button），字体用打包的 Noto Sans SC。</summary>
    public static class TwoNightUi
    {
        public static readonly Color PanelBg = new Color(0.07f, 0.08f, 0.09f, 0.92f);
        public static readonly Color ButtonBg = new Color(0.22f, 0.24f, 0.26f, 1f);
        public static readonly Color Accent = new Color(0.55f, 0.43f, 0.24f, 1f);
        public static readonly Color TextMain = new Color(0.92f, 0.90f, 0.86f);
        public static readonly Color TextDim = new Color(0.70f, 0.70f, 0.68f);
        public static readonly Color Warn = new Color(0.95f, 0.72f, 0.35f);

        public static Canvas Canvas(GameObject go, int order)
        {
            var c = go.GetComponent<Canvas>(); if (c == null) c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
            var s = go.GetComponent<CanvasScaler>(); if (s == null) s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; s.referenceResolution = new Vector2(1600, 900); s.matchWidthOrHeight = 0f;
            if (go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
            return c;
        }

        /// <summary>全屏遮罩（挡住并压暗下面的界面，也挡住下面按钮的点击）。</summary>
        public static RectTransform Shade(Transform parent, string name, float alpha = 0.88f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>(); img.color = new Color(0.02f, 0.025f, 0.03f, alpha); img.raycastTarget = true;
            return rt;
        }

        /// <summary>面板：anchor 是锚点（0..1），pos 是锚点到面板左上角的偏移（像素，参考分辨率 1600×900）。</summary>
        public static RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color? bg = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            go.GetComponent<Image>().color = bg ?? PanelBg;
            return rt;
        }

        public static Text Label(Transform parent, string name, string text, Font font, int size, Vector2 pos, Vector2 box, Color color, TextAnchor align = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos; rt.sizeDelta = box;
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.color = color; t.text = text; t.alignment = align; t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.lineSpacing = 1.1f;
            t.raycastTarget = false; t.supportRichText = true;
            return t;
        }

        public static Button Button(Transform parent, string name, string label, Font font, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick, bool accent = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = go.GetComponent<Image>(); img.color = accent ? Accent : ButtonBg;
            var b = go.GetComponent<Button>(); b.targetGraphic = img;
            var cb = b.colors; cb.highlightedColor = new Color(1.3f, 1.3f, 1.25f, 1f); cb.colorMultiplier = 1.5f; cb.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f); b.colors = cb;
            b.onClick.AddListener(onClick);
            b.onClick.AddListener(() => { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); });
            Label(go.transform, "Text", label, font, 18, Vector2.zero, size, TextMain, TextAnchor.MiddleCenter);
            return b;
        }

        public static string Money(int v) => v.ToString("N0");
    }
}
