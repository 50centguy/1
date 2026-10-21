using UnityEngine;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // Visor HUD look shared by the desktop workspace, the AR overlay, the shop and the call window: a single amber
    // phosphor on a see-through dark field (after the reference library's monochrome terminals). Hierarchy comes from
    // brightness, not hue: faint rules, amber line work, hot near-white amber for what the player can act on; red only
    // for blockers and alarms. Sprites are generated once at runtime, so the look needs no imported art and survives
    // the PS2 pass (overlay canvases are drawn after it). Two bundled OFL fonts carry the Latin / kana labels.
    public static class RepairPropCyberStyle
    {
        public static readonly Color Amber = new Color(1f, .62f, .16f, 1f);
        public static readonly Color AmberDim = new Color(1f, .62f, .16f, .38f);
        public static readonly Color AmberFaint = new Color(1f, .62f, .16f, .12f);
        /// <summary>Actionable / pointer: the same phosphor driven hot.</summary>
        public static readonly Color Hot = new Color(1f, .87f, .60f, 1f);
        public static readonly Color HotDim = new Color(1f, .87f, .60f, .45f);
        public static readonly Color Ok = new Color(.84f, .93f, .52f, 1f);
        public static readonly Color Warn = new Color(1f, .74f, .30f, 1f);
        public static readonly Color Bad = new Color(1f, .30f, .22f, 1f);
        public static readonly Color BadDim = new Color(1f, .30f, .22f, .40f);
        public static readonly Color Ink = new Color(1f, .90f, .78f, 1f);
        public static readonly Color Dim = new Color(.86f, .66f, .46f, .72f);
        public static readonly Color Field = new Color(.020f, .014f, .008f, .80f);
        public static readonly Color FieldDeep = new Color(.012f, .009f, .006f, .86f);
        public static readonly Color Clear = new Color(0, 0, 0, 0);

        static Sprite fill, chamferFill, frame, bracket, line, dots, hatch, scan, dither;
        static Font mono, kana, ui;

        /// <summary>One pixel face for the whole HUD (Fusion Pixel 12px monospaced, OFL): Chinese, kana, Latin and digits share
        /// the same grid, so columns and labels line up.</summary>
        public static Font UiFont => ui != null ? ui : ui = Resources.Load<Font>("BorderRepairCyber/Fonts/FusionPixel12-zh_hans");
        /// <summary>The HUD face, or the given fallback when the font asset is missing.</summary>
        public static Font UiFontOr(Font fallback) => UiFont != null ? UiFont : fallback;
        /// <summary>Codes, numbers, English labels (the HUD face; Share Tech Mono when it is missing).</summary>
        public static Font Mono => UiFont != null ? UiFont : mono != null ? mono : mono = Resources.Load<Font>("BorderRepairCyber/Fonts/ShareTechMono-Regular");
        /// <summary>Kana captions (the HUD face; DotGothic16 when it is missing). Projected wall signs keep DotGothic16 via Signage.</summary>
        public static Font Kana => UiFont != null ? UiFont : kana != null ? kana : kana = Resources.Load<Font>("BorderRepairCyber/Fonts/DotGothic16-Regular");
        public static Font SignKana => Resources.Load<Font>("BorderRepairCyber/Fonts/DotGothic16-Regular");

        /// <summary>Solid 1px-sliced fill (tinted by Image.color).</summary>
        public static Sprite Fill => fill != null ? fill : fill = Build(8, 1, (x, y, s) => 1f);

        /// <summary>Fill with the top-left and bottom-right corners cut at 45 degrees; 9-sliced.</summary>
        public static Sprite ChamferFill => chamferFill != null ? chamferFill : chamferFill = Build(32, 9, (x, y, s) =>
            x + (s - 1 - y) < 6 || (s - 1 - x) + y < 6 ? 0f : 1f);

        /// <summary>Retro control outline (RETRO UI / OpenVMS monitor references): a 1px rule, a faint inner rule, and a
        /// 2px bevel on the bottom and right edges; 9-sliced so it stays crisp at any size.</summary>
        public static Sprite Frame => frame != null ? frame : frame = Build(32, 9, (x, y, s) =>
        {
            if (x >= s - 2 || y <= 1) return 1f;                 // bevel: right and bottom (rows are bottom-up)
            if (x == 0 || y == s - 1) return .85f;               // left and top rule
            if (x == 2 || y == s - 3 || x == s - 4 || y == 3) return .22f;   // inner rule
            return 0f;
        });

        /// <summary>Ordered-dither blotches (Destiny battle-report backgrounds); tiles.</summary>
        public static Sprite Dither => dither != null ? dither : dither = Build(64, 0, (x, y, s) =>
        {
            float n = Mathf.PerlinNoise(x * .09f + 3.1f, y * .09f + 7.7f) * .7f + Mathf.PerlinNoise(x * .31f, y * .31f) * .3f;
            float bayer = (new[] { 0f, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 })[(y % 4) * 4 + (x % 4)] / 16f;
            return n - .38f > bayer * .45f ? 1f : 0f;
        }, true);

        /// <summary>Corner brackets only (no edges between them); 9-sliced.</summary>
        public static Sprite Bracket => bracket != null ? bracket : bracket = Build(32, 10, (x, y, s) =>
        {
            bool cornerX = x < 9 || x > s - 10, cornerY = y < 9 || y > s - 10;
            bool tick = (x <= 1 || x >= s - 2) && cornerY || (y <= 1 || y >= s - 2) && cornerX;
            return tick ? 1f : 0f;
        });

        /// <summary>Plain white texel for line segments.</summary>
        public static Sprite Line => line != null ? line : line = Build(4, 1, (x, y, s) => 1f);

        /// <summary>Tiling dot grid (one dot per 6px), for panel fields.</summary>
        public static Sprite Dots => dots != null ? dots : dots = Build(6, 0, (x, y, s) => x == 0 && y == 0 ? 1f : 0f, true);

        /// <summary>Tiling 45-degree hatch, for warning bars and filler strips.</summary>
        public static Sprite Hatch => hatch != null ? hatch : hatch = Build(8, 0, (x, y, s) => (x + y) % 8 < 3 ? 1f : 0f, true);

        /// <summary>Tiling scanlines (every third row lit).</summary>
        public static Sprite Scan => scan != null ? scan : scan = Build(3, 0, (x, y, s) => y == 0 ? 1f : 0f, true);

        static Sprite Build(int size, int border, System.Func<int, int, int, float> alpha, bool tile = false)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave, name = "CyberHud" };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255 * alpha(x, y, size)));
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// <summary>Makes an Image a sliced fill and adds (or returns) its outline child named "Frame" (retro double rule
        /// with a bevel, or corner brackets only).</summary>
        public static Image Framed(Image fillImage, Color fillColor, Color frameColor, bool bracketsOnly = false)
        {
            fillImage.sprite = Fill; fillImage.type = Image.Type.Sliced; fillImage.color = fillColor;
            var existing = fillImage.transform.Find("Frame");
            Image outline;
            if (existing != null) outline = existing.GetComponent<Image>();
            else
            {
                var rect = (RectTransform)new GameObject("Frame", typeof(RectTransform)).transform;
                rect.SetParent(fillImage.transform, false);
                rect.SetAsFirstSibling();
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                outline = rect.gameObject.AddComponent<Image>();
                outline.raycastTarget = false;
                var element = rect.gameObject.AddComponent<LayoutElement>(); element.ignoreLayout = true;
            }
            outline.sprite = bracketsOnly ? Bracket : Frame; outline.type = Image.Type.Sliced; outline.color = frameColor;
            return outline;
        }

        public static void SetFrame(Component owner, Color color)
        {
            var frameTransform = owner != null ? owner.transform.Find("Frame") : null;
            var image = frameTransform != null ? frameTransform.GetComponent<Image>() : null;
            if (image != null) image.color = color;
        }

        /// <summary>A non-interactive tiled decoration (dots / hatch / scanlines) stretched over its parent, behind the content.</summary>
        public static Image Texture(Transform parent, Sprite sprite, Color color, string name = "Texture", float pixelsPerUnit = 1f)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(2, 2); rect.offsetMax = new Vector2(-2, -2);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite; image.type = Image.Type.Tiled; image.color = color; image.raycastTarget = false;
            image.pixelsPerUnitMultiplier = pixelsPerUnit;
            var element = rect.gameObject.AddComponent<LayoutElement>(); element.ignoreLayout = true;
            return image;
        }

        /// <summary>A plain non-interactive label outside any layout (decorations: codes, katakana strips).</summary>
        public static Text Caption(Transform parent, string value, Font font, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var rect = (RectTransform)new GameObject("Caption", typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.color = color; text.text = value; text.alignment = anchor;
            text.raycastTarget = false; text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Overflow;
            var element = rect.gameObject.AddComponent<LayoutElement>(); element.ignoreLayout = true;
            return text;
        }

        /// <summary>Inverse title bar sitting on top of a window (outside its rect, so absolute layouts inside stay put):
        /// caption left, window glyphs right.</summary>
        public static Image TitleBar(RectTransform window, string caption, Font font, float height = 18f)
        {
            var bar = (RectTransform)new GameObject("TitleBar", typeof(RectTransform)).transform;
            bar.SetParent(window, false);
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1); bar.pivot = new Vector2(.5f, 0);
            bar.anchoredPosition = new Vector2(0, -1); bar.sizeDelta = new Vector2(0, height);
            var image = bar.gameObject.AddComponent<Image>(); image.color = Amber; image.raycastTarget = false;
            var element = bar.gameObject.AddComponent<LayoutElement>(); element.ignoreLayout = true;
            var text = Caption(bar, caption, Kana != null ? Kana : font, 11, Color.black, TextAnchor.MiddleLeft);
            var r = text.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(6, 0); r.offsetMax = new Vector2(-30, 0);
            var glyphs = Caption(bar, "- x", Mono != null ? Mono : font, 11, Color.black, TextAnchor.MiddleRight);
            var g = glyphs.rectTransform; g.anchorMin = Vector2.zero; g.anchorMax = Vector2.one; g.offsetMin = Vector2.zero; g.offsetMax = new Vector2(-6, 0);
            return image;
        }

        /// <summary>Stable pseudo serial for a string: same input, same code.</summary>
        public static string Code(string seed, int digits = 6)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in seed ?? "") h = (h ^ c) * 16777619;
                return (h % (uint)Mathf.Pow(10, Mathf.Min(digits, 9))).ToString().PadLeft(digits, '0');
            }
        }
    }
}
