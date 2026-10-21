using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // Motion for the visor HUD, after the reference library's terminal clips: frames open from a line, content blinks
    // on, text types out with a hot head character. Everything is cosmetic and leaves the logical UI untouched:
    // Text.text keeps the full string (typing only hides glyph quads), nothing moves under the pointer, and raycast
    // targets are never toggled, so clicks and tests behave the same mid-animation. Unscaled time throughout.
    public static class HudMotion
    {
        public static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t) * (1 - t); }

        /// <summary>Cheap stable hash noise in [0,1) for flicker.</summary>
        public static float Noise(float x) => Mathf.Repeat(Mathf.Sin(x * 12.9898f) * 43758.5453f, 1f);

        /// <summary>Adds typing to a Text (before any Shadow, so it hides the shadow quads too).</summary>
        public static HudTypeOn TypeOn(Text text, float charsPerSecond = 120f)
        {
            if (text == null) return null;
            var effect = text.GetComponent<HudTypeOn>();
            if (effect == null)
            {
                var shadow = text.GetComponent<Shadow>();
                Color shadowColor = default; Vector2 distance = default;
                if (shadow != null) { shadowColor = shadow.effectColor; distance = shadow.effectDistance; Object.DestroyImmediate(shadow); }
                effect = text.gameObject.AddComponent<HudTypeOn>();
                if (shadow != null) { var s = text.gameObject.AddComponent<Shadow>(); s.effectColor = shadowColor; s.effectDistance = distance; }
            }
            effect.charsPerSecond = charsPerSecond;
            return effect;
        }

        /// <summary>Adds the open animation to a panel (plays on every enable).</summary>
        public static HudBoot Boot(GameObject panel, float delay = 0f)
        {
            var boot = panel.GetComponent<HudBoot>();
            if (boot == null) boot = panel.AddComponent<HudBoot>();
            boot.delay = delay;
            return boot;
        }
    }

    /// <summary>Types the text out whenever it changes (number-only changes such as clocks do not retype). The head of
    /// the line is drawn hot amber while it types; one last rebuild after the end puts every glyph back in its own colour.</summary>
    [AddComponentMenu("")]
    public sealed class HudTypeOn : BaseMeshEffect
    {
        public float charsPerSecond = 120f;
        Text text;
        string shown;
        float start = -100f;
        int quads;
        bool settled = true;

        public bool Animating => text != null && (Time.unscaledTime - start) * charsPerSecond < quads + 1;

        protected override void OnEnable()
        {
            base.OnEnable();
            text = GetComponent<Text>();
            Restart();
        }

        /// <summary>Retypes from the start, optionally after a delay (boot sequences type line by line).</summary>
        public void Restart(float delay = 0f)
        {
            if (text == null) text = GetComponent<Text>();
            shown = text != null ? text.text : null;
            start = Time.unscaledTime + delay;
            quads = text != null && text.text != null ? text.text.Length : 0;
            settled = false;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        void LateUpdate()
        {
            if (text == null) return;
            if (text.text != shown)
            {
                if (Stem(text.text) == Stem(shown)) shown = text.text;
                else Restart();
            }
            if (Animating) graphic.SetVerticesDirty();
            else if (!settled) { settled = true; graphic.SetVerticesDirty(); }
        }

        static string Stem(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var chars = new System.Text.StringBuilder(value.Length);
            foreach (char c in value) if (!char.IsDigit(c)) chars.Append(c);
            return chars.ToString();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            int count = vh.currentVertCount / 4;
            quads = count;
            float revealed = (Time.unscaledTime - start) * charsPerSecond;
            if (revealed >= count + 1) return;
            var v = new UIVertex();
            for (int q = 0; q < count; q++)
            {
                bool hidden = q >= revealed, head = !hidden && q >= revealed - 1;
                for (int k = 0; k < 4; k++)
                {
                    vh.PopulateUIVertex(ref v, q * 4 + k);
                    if (hidden) v.color.a = 0;
                    else if (head) v.color = new Color32(255, 214, 140, v.color.a);
                    vh.SetUIVertex(v, q * 4 + k);
                }
            }
        }
    }

    /// <summary>Panel open, after the Destiny terminal clips: four corner brackets blink in at the centre and fly out to
    /// the corners, the outline follows, then every line types in from the top down.</summary>
    [AddComponentMenu("")]
    public sealed class HudBoot : MonoBehaviour
    {
        public float delay;
        const float Seed = .14f, Fly = .22f, Hold = .30f;
        static Sprite corner;
        static readonly Vector2[] Signs = { new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, -1) };
        CanvasGroup group, frameGroup;
        bool ownGroup, settled;
        RectTransform frame;
        readonly RectTransform[] corners = new RectTransform[4];
        readonly CanvasGroup[] cornerGroups = new CanvasGroup[4];
        readonly List<Text> texts = new List<Text>();
        float start;

        public bool Playing => Time.unscaledTime - start < delay + Seed + Fly + Hold;

        static Sprite Corner
        {
            get
            {
                if (corner != null) return corner;
                const int s = 12;
                var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                var px = new Color32[s * s];
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                        px[y * s + x] = new Color32(255, 255, 255, (byte)(x < 2 || y >= s - 2 ? 255 : 0));
                t.SetPixels32(px); t.Apply(false, true);
                corner = Sprite.Create(t, new Rect(0, 0, s, s), new Vector2(0, 1), 100);
                corner.hideFlags = HideFlags.HideAndDontSave;
                return corner;
            }
        }

        void OnEnable()
        {
            start = Time.unscaledTime; settled = false;
            // Only hide content through a group this component made; a group owned by game code is left alone.
            if (group == null)
            {
                group = GetComponent<CanvasGroup>();
                if (group == null) { group = gameObject.AddComponent<CanvasGroup>(); ownGroup = true; }
            }
            frame = transform.Find("Frame") as RectTransform;
            if (frame != null && frameGroup == null)
            {
                frameGroup = frame.GetComponent<CanvasGroup>();
                if (frameGroup == null) frameGroup = frame.gameObject.AddComponent<CanvasGroup>();
                frameGroup.ignoreParentGroups = true;
            }
            for (int i = 0; i < 4; i++)
            {
                if (corners[i] != null) { corners[i].gameObject.SetActive(true); continue; }
                var c = (RectTransform)new GameObject("BootCorner", typeof(RectTransform)).transform;
                c.SetParent(transform, false);
                c.anchorMin = c.anchorMax = new Vector2(.5f, .5f); c.pivot = new Vector2(0, 1); c.sizeDelta = new Vector2(12, 12);
                c.localRotation = Quaternion.Euler(0, 0, -90 * i);   // top-left, top-right, bottom-right, bottom-left
                var img = c.gameObject.AddComponent<Image>(); img.sprite = Corner; img.raycastTarget = false; img.color = new Color(1f, .82f, .5f, 1);
                var element = c.gameObject.AddComponent<LayoutElement>(); element.ignoreLayout = true;
                cornerGroups[i] = c.gameObject.AddComponent<CanvasGroup>();
                cornerGroups[i].ignoreParentGroups = true; cornerGroups[i].blocksRaycasts = false;
                corners[i] = c;
            }
            // Every line types in after the frame is up, top line first.
            texts.Clear();
            GetComponentsInChildren(false, texts);
            texts.Sort((a, b) => b.rectTransform.position.y.CompareTo(a.rectTransform.position.y));
            for (int i = 0; i < texts.Count; i++)
            {
                if (string.IsNullOrEmpty(texts[i].text)) continue;
                var typing = texts[i].GetComponent<HudTypeOn>();
                if (typing == null) typing = HudMotion.TypeOn(texts[i], 280);
                typing.Restart(delay + Seed + Fly + .04f + Mathf.Min(i, 24) * .035f);
            }
            Apply();
        }

        void OnDisable()
        {
            if (group != null && ownGroup) group.alpha = 1;
            if (frameGroup != null) frameGroup.alpha = 1;
            foreach (var c in corners) if (c != null) c.gameObject.SetActive(false);
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            if (settled) return;
            float t = Time.unscaledTime - start - delay;
            if (!Playing)
            {
                settled = true;
                if (ownGroup) group.alpha = 1;
                if (frameGroup != null) frameGroup.alpha = 1;
                foreach (var c in corners) if (c != null) c.gameObject.SetActive(false);
                return;
            }
            var rect = ((RectTransform)transform).rect;
            // Corners are anchored at the panel centre: Seed blinks a small bracket square there, Fly carries it out.
            var offset = Vector2.zero;
            var half = Vector2.Lerp(new Vector2(14, 10), rect.size * .5f, HudMotion.EaseOut((t - Seed) / Fly));
            bool seedOn = t >= 0 && (t >= Seed || HudMotion.Noise(Mathf.Floor(t * 30f)) > .3f);
            float fade = t > Seed + Fly + .12f ? Mathf.Clamp01(1 - (t - Seed - Fly - .12f) / .15f) : 1;
            for (int i = 0; i < 4; i++)
            {
                if (corners[i] == null) continue;
                corners[i].anchoredPosition = offset + Vector2.Scale(Signs[i], half);
                cornerGroups[i].alpha = seedOn ? fade : 0;
            }
            if (frameGroup != null) frameGroup.alpha = Mathf.Clamp01((t - Seed - Fly * .7f) / .08f);
            if (ownGroup) group.alpha = t < Seed + Fly * .8f ? 0 : 1;
        }
    }
}
