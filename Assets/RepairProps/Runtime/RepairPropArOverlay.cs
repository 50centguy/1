using System.Collections.Generic;
using System.Linq;
using BorderRepair.Controls;
using BorderRepair.Data;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // Visor AR layer over the bench view: wireframe boxes and tags drawn straight onto the item, as if projected into
    // the repairer's eyes. It marks what can be acted on (hot dim corners), the selection (amber), the pointer's part
    // (hot wire), and the suggested next step with its tool, plus a one-line hint. Marks lock on when they first appear
    // (corners close in from wide, a scan line crosses the box, leader lines draw out, tags type). Read-only: it never
    // selects or executes; the panel and clicks keep going through the bridge's guarded entry points. Raycasts never hit it.
    public sealed class RepairPropArOverlay : MonoBehaviour
    {
        const float LineWidth = 1.6f, Bracket = .22f;
        RepairPropStationBridge owner;
        Canvas canvas;
        RectTransform root;
        Font font;
        readonly List<Image> lines = new List<Image>();
        readonly List<(RectTransform rect, Image bg, Text text)> tags = new List<(RectTransform, Image, Text)>();
        int usedLines, usedTags;
        Text hint, controls, readout, visorId, clock, ticker;
        readonly Dictionary<string, (float first, float last)> locks = new Dictionary<string, (float, float)>();
        float tickerNext;
        int tickerSeed;
        RectTransform hintBand;
        /// <summary>The guide band along the bottom of the bench view.</summary>
        public RectTransform GuideBand => hintBand;
        /// <summary>Set while something else (the tutorial card) is telling the player the next step.</summary>
        public static bool GuideHidden;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetGuide() => GuideHidden = false;
        float tagRight, bandTop;
        readonly Vector3[] corners = new Vector3[8];
        readonly Vector2[] screen = new Vector2[8];

        public string HoveredTarget { get; private set; }
        public RepairBenchProp HoveredProp { get; private set; }
        /// <summary>With a tool in hand: what a left click at the pointer does ("左键 ▶ …") or why it does nothing ("✕ …").</summary>
        public string ToolPreview { get; private set; }
        bool pointerOnItem;
        const float DoneSeconds = .6f, TraceSeconds = 1.5f, TraceEvery = 7f, AnalysisSeconds = 3.6f;
        readonly List<Image> tagIcons = new List<Image>();
        Image stepIcon;
        string anaTarget, anaTitle, anaBody, anaIcon;
        Color anaColour;
        bool anaWave;
        float anaAt = -99, inspectSeen;
        int historySeen = -1;
        BorderRepair.Core.FeedbackKind lastFeedbackKind;
        /// <summary>The part the analysis animation is playing on right now, or null.</summary>
        public string AnalysisTarget { get; private set; }
        public string AnalysisTitle => anaTitle;
        /// <summary>Icon shown in the step header (a VisorIcons name), for tests.</summary>
        public string StepIconName { get; private set; }
        RectTransform header;
        Image headerBack, chipImage;
        Text chipText, stepText, stepSub;
        string stepId, stepMain, doneLabel;
        bool stepWasCommand;
        float stepAt;
        /// <summary>The step header at the top of the view: the first thing to read.</summary>
        public string StepText => stepText != null ? stepText.text : "";
        public string StepSubText => stepSub != null ? stepSub.text : "";
        public RectTransform StepHeader => header;
        /// <summary>Canvas-space rectangle of the header's plate (reference pixels).</summary>
        public Rect HeaderRect { get; private set; }
        /// <summary>The part the header's trace line is running to right now, or null.</summary>
        public string TraceTarget { get; private set; }
        /// <summary>The tray tool marked "take this" because the step needs it and the hand holds something else.</summary>
        public RepairBenchProp ToolToTake { get; private set; }
        BorderRepair.TwoNight.ClinicCleaningController cleaning;
        public string HintText => hint != null ? hint.text : "";
        public string NextTarget { get; private set; }
        public bool Showing => canvas != null && canvas.enabled;

        public void Initialize(RepairPropStationBridge bridge)
        {
            owner = bridge;
            if (owner.Flow != null) owner.Flow.Feedback += OnFlowFeedback;
            inspectSeen = owner.LastInspectedAt;
            font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            var host = new GameObject("RepairPropArCanvas", typeof(RectTransform));
            host.transform.SetParent(transform, false);
            canvas = host.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
            var scaler = host.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            root = (RectTransform)host.transform;
            hintBand = (RectTransform)new GameObject("HintBand", typeof(RectTransform)).transform;
            hintBand.SetParent(root, false);
            var band = hintBand.gameObject.AddComponent<Image>(); band.raycastTarget = false;
            RepairPropCyberStyle.Framed(band, new Color(0, 0, 0, .62f), RepairPropCyberStyle.AmberDim);
            RepairPropCyberStyle.TitleBar(hintBand, "GUIDE // 指引", font, 16f);
            readout = MakeText("Readout", 11, TextAnchor.UpperLeft, RepairPropCyberStyle.Dim);
            hint = MakeText("Hint", 14, TextAnchor.LowerCenter, RepairPropCyberStyle.Amber);
            controls = MakeText("Controls", 11, TextAnchor.LowerCenter, RepairPropCyberStyle.Dim);
            controls.text = "左键 用手上的工具 / 空手查看　·　右键单击 放下工具　·　拖动 旋转　·　滚轮 缩放　·　R 复位";
            visorId = MakeText("VisorId", 11, TextAnchor.UpperLeft, RepairPropCyberStyle.AmberFaint * new Color(1, 1, 1, 2.2f));
            if (RepairPropCyberStyle.Kana != null) visorId.font = RepairPropCyberStyle.Kana;
            visorId.text = "VSR-07 // 修理視界";
            clock = MakeText("Clock", 11, TextAnchor.LowerLeft, RepairPropCyberStyle.AmberDim);
            ticker = MakeText("Ticker", 9, TextAnchor.UpperLeft, new Color(1f, .62f, .16f, .22f));
            if (RepairPropCyberStyle.Mono != null) { clock.font = RepairPropCyberStyle.Mono; ticker.font = RepairPropCyberStyle.Mono; readout.font = RepairPropCyberStyle.Mono; }
            ticker.lineSpacing = 1.1f;
            HudMotion.TypeOn(hint, 220);
            BuildHeader();
        }

        // ---------- the eye's path: what to do (the step header), where (a trace to the part), with what (the tool to take) ----------

        void BuildHeader()
        {
            header = (RectTransform)new GameObject("StepHeader", typeof(RectTransform)).transform;
            header.SetParent(root, false);
            header.anchorMin = header.anchorMax = header.pivot = Vector2.zero; header.anchoredPosition = Vector2.zero; header.sizeDelta = Vector2.zero;
            var back = (RectTransform)new GameObject("Back", typeof(RectTransform)).transform; back.SetParent(header, false);
            headerBack = back.gameObject.AddComponent<Image>(); headerBack.raycastTarget = false;
            RepairPropCyberStyle.Framed(headerBack, new Color(0, 0, 0, .58f), RepairPropCyberStyle.AmberDim);
            var chip = (RectTransform)new GameObject("Chip", typeof(RectTransform)).transform; chip.SetParent(header, false);
            chipImage = chip.gameObject.AddComponent<Image>(); chipImage.raycastTarget = false; chipImage.color = RepairPropCyberStyle.Amber;
            var chipLabel = (RectTransform)new GameObject("Text", typeof(RectTransform)).transform; chipLabel.SetParent(chip, false);
            chipLabel.anchorMin = Vector2.zero; chipLabel.anchorMax = Vector2.one; chipLabel.offsetMin = chipLabel.offsetMax = Vector2.zero;
            chipText = chipLabel.gameObject.AddComponent<Text>(); chipText.font = font; chipText.fontSize = 13; chipText.alignment = TextAnchor.MiddleCenter;
            chipText.color = Color.black; chipText.raycastTarget = false; chipText.supportRichText = false;
            var iconRect = (RectTransform)new GameObject("Icon", typeof(RectTransform)).transform; iconRect.SetParent(header, false);
            stepIcon = iconRect.gameObject.AddComponent<Image>(); stepIcon.raycastTarget = false;
            stepText = MakeText("Step", 21, TextAnchor.MiddleLeft, RepairPropCyberStyle.Amber); stepText.rectTransform.SetParent(header, false);
            stepText.horizontalOverflow = HorizontalWrapMode.Overflow;
            stepSub = MakeText("StepSub", 12, TextAnchor.MiddleCenter, RepairPropCyberStyle.Dim); stepSub.rectTransform.SetParent(header, false);
            stepSub.horizontalOverflow = HorizontalWrapMode.Overflow;
            HudMotion.TypeOn(stepText, 150);
            HudMotion.TypeOn(stepSub, 160);
        }


        // ---------- the eye analysing a part: slices sweep through it, its box locks, a read-out types beside it ----------

        void OnFlowFeedback(string message, BorderRepair.Core.FeedbackKind kind) { lastFeedbackKind = kind; }

        /// <summary>Plays the analysis on <paramref name="target"/>: cyan for a look, green / amber for a reading.</summary>
        public void Analyze(string target, string title, string body, Color colour, string icon, bool wave)
        {
            anaTarget = target; anaTitle = title; anaBody = Wrap(body, 22); anaColour = colour; anaIcon = icon; anaWave = wave;
            anaAt = Time.unscaledTime;
        }

        static string Wrap(string text, int width)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= width) return text;
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < text.Length && i < width * 3; i += width)
            {
                if (i > 0) builder.Append('\n');
                builder.Append(text, i, Mathf.Min(width, text.Length - i));
            }
            return builder.ToString();
        }

        // New results to show: a test, a measurement or an inspection that just happened.
        void WatchForAnalysis(RepairPropFlow flow, RepairPropFlowDefinition d)
        {
            int count = flow.History.Count;
            if (historySeen < 0 || count < historySeen) historySeen = count;
            if (count > historySeen)
            {
                historySeen = count;
                var command = d.FindCommand(flow.History[count - 1]);
                if (command != null && (command.kind == PropCommandKind.Test || command.kind == PropCommandKind.Measure))
                    Analyze(command.target, (command.kind == PropCommandKind.Test ? "测试 // " : "测量 // ") + command.label, flow.LastFeedback,
                        lastFeedbackKind == BorderRepair.Core.FeedbackKind.Warning || lastFeedbackKind == BorderRepair.Core.FeedbackKind.Error ? RepairPropCyberStyle.Warn : RepairPropCyberStyle.Ok,
                        VisorIcons.CommandName(command), true);
            }
            if (owner.LastInspectedAt != inspectSeen)
            {
                inspectSeen = owner.LastInspectedAt;
                var node = d.FindNode(owner.LastInspected);
                if (node != null)
                {
                    int ready = d.commands.Count(c => c.target == node.objectName && flow.CanExecute(c.id, c.target, c.tool, out _));
                    string state = node.immutable ? "保留原件 · 只检查" : ready > 0 ? ready + " 项可执行" : "暂无可执行操作";
                    Analyze(node.objectName, "检查 // " + node.displayName, state, VisorIcons.Scan, "loupe", false);
                }
            }
        }

        void Analysis(Camera camera, float scale)
        {
            float age = Time.unscaledTime - anaAt;
            AnalysisTarget = null;
            if (anaTarget == null || age > AnalysisSeconds || !TargetBox(anaTarget, 1.06f)) return;
            AnalysisTarget = anaTarget;
            // The box edge that stands most upright is the sweep's axis; the other two span each slice.
            int up = 1; float best = -1;
            for (int bit = 1; bit <= 4; bit <<= 1)
            {
                float v = Mathf.Abs((box3[bit] - box3[0]).normalized.y);
                if (v > best) { best = v; up = bit; }
            }
            int a = up == 1 ? 2 : 1, b = up == 4 ? 2 : 4;
            bool flip = box3[up].y < box3[0].y;
            float fade = Mathf.Clamp01((AnalysisSeconds - age) / .5f), sweep = -1;
            for (int k = 0; k < 3; k++)
            {
                // up through the part, then back down; three slices, each a little behind and fainter
                float t = (age - k * .08f) / 1.3f;
                if (t < 0 || t > 1) continue;
                float h = t < .5f ? HudMotion.EaseOut(t * 2) : 1 - HudMotion.EaseOut((t - .5f) * 2);
                if (k == 0) sweep = h;
                if (flip) h = 1 - h;
                var colour = anaColour; colour.a = .95f - k * .3f;
                var ring = new[] { 0, a, a | b, b };
                bool visible = true;
                for (int i = 0; i < 4 && visible; i++)
                {
                    var p = camera.WorldToScreenPoint(Vector3.Lerp(box3[ring[i]], box3[ring[i] | up], h));
                    if (p.z <= 0) visible = false; else box2[i] = (Vector2)p / scale;
                }
                if (!visible) continue;
                for (int i = 0; i < 4; i++) Line(box2[i], box2[(i + 1) % 4], colour);
            }
            var hold = anaColour; hold.a = .85f * fade;
            if (TargetBox(anaTarget, 1.06f)) LockBox(camera, scale, hold, .24f, "ana:" + anaAt);
            if (!Project(camera, anaTarget, scale, out var rect)) return;
            Gauge(rect, age, fade, sweep);
            if (age < .4f) return;
            var ink = anaColour; ink.a = fade;
            // The read-out sits beside the part, on the side with room (the work panels fill the right), and the leader
            // runs to wherever it actually landed. A reading gets a scope strip inside the same window.
            var tag = Tag(new Vector2(rect.xMax + 34, rect.center.y + 6), anaTitle + (string.IsNullOrEmpty(anaBody) ? "" : "\n" + Brief(anaBody)), ink, 12, TextAnchor.UpperLeft, true, VisorIcons.Get(anaIcon));
            if (anaWave) Scope(tag);
            if (tag.anchoredPosition.x < rect.xMax + 20)
                tag.anchoredPosition = new Vector2(Mathf.Max(4, rect.xMin - 34 - tag.sizeDelta.x), tag.anchoredPosition.y);
            bool left = tag.anchoredPosition.x < rect.xMin;
            var anchor = tag.anchoredPosition;
            Leader(new Vector2(left ? rect.xMin : rect.xMax, rect.center.y), new Vector2(left ? anchor.x + tag.sizeDelta.x : anchor.x, anchor.y - 8), ink, "analine:" + anaAt);
            if (!anaWave) return;
            // A reading settles: the trace swings wide, then steadies (and stays ragged on a fault).
            float settle = Mathf.Exp(-(age - .4f) * 2.2f);
            bool rough = anaColour == RepairPropCyberStyle.Warn;
            // The strip at the bottom of the window: a rule over it, a faint centre line and graticule, the trace inside.
            float w = tag.sizeDelta.x, bottom = anchor.y - tag.sizeDelta.y, top = bottom + ScopeHeight;
            var faint = ink; faint.a *= .28f;
            Line(new Vector2(anchor.x + 4, top + 2), new Vector2(anchor.x + w - 4, top + 2), ink * new Color(1, 1, 1, .6f));
            float mid = (top + bottom) * .5f, half = ScopeHeight * .5f - 5;
            Line(new Vector2(anchor.x + 8, mid), new Vector2(anchor.x + w - 8, mid), faint);
            for (int g = 1; g < 6; g++)
            {
                float gx = anchor.x + 8 + (w - 16) * g / 6f;
                Line(new Vector2(gx, mid - 3), new Vector2(gx, mid + 3), faint);
            }
            int firstWave = usedLines;
            Vector2 previous = new Vector2(anchor.x + 8, mid);
            for (int i = 1; i <= 32; i++)
            {
                float x = anchor.x + 8 + i * (w - 16) / 32f, phase = i * .8f - age * 9f;
                float y = Mathf.Sin(phase) * (.25f + .75f * settle) * half + (rough ? (HudMotion.Noise(i * 3.1f + Mathf.Floor(age * 12)) - .5f) * half * .9f : 0);
                var point = new Vector2(x, mid + Mathf.Clamp(y, -half, half));
                Line(previous, point, ink);
                previous = point;
            }
            for (int i = firstWave; i < usedLines; i++) lines[i].transform.SetAsLastSibling();   // above the window's fill
        }

        const float ScopeHeight = 34;

        // Makes room for the scope at the bottom of a read-out window: taller, text pinned to the top, at least 160 wide.
        void Scope(RectTransform tag)
        {
            tag.sizeDelta = new Vector2(Mathf.Max(160, tag.sizeDelta.x), tag.sizeDelta.y + ScopeHeight + 4);
            var text = tag.GetComponentInChildren<Text>();
            if (text != null) { text.alignment = TextAnchor.UpperLeft; text.rectTransform.offsetMax = new Vector2(-7, -4); }
            tag.SetAsLastSibling();
        }

        // A graduated scale stands beside the part while it is read (the EVA graph displays): it draws upward, a marker
        // rides it at the height of the sweeping slice, and three figures name the divisions. It takes the side the
        // read-out does not.
        void Gauge(Rect rect, float age, float fade, float sweep)
        {
            float limit = tagRight > 0 ? tagRight : root.rect.width;
            bool onRight = rect.xMax + 240 > limit;
            float x = onRight ? rect.xMax + 14 : rect.xMin - 14, dir = onRight ? 1 : -1;
            var faint = anaColour; faint.a = .55f * fade;
            var ink = anaColour; ink.a = fade;
            float top = Mathf.Lerp(rect.yMin, rect.yMax, HudMotion.EaseOut(age / .35f));
            Line(new Vector2(x, rect.yMin), new Vector2(x, top), faint);
            for (int i = 0; i <= 16; i++)
            {
                float y = Mathf.Lerp(rect.yMin, rect.yMax, i / 16f);
                if (y > top + .5f) break;
                bool major = i % 4 == 0;
                Line(new Vector2(x, y), new Vector2(x + dir * (major ? 8 : 3), y), major ? ink : faint);
                if (i % 8 == 0) Tag(new Vector2(onRight ? x + 11 : x - 36, y + 7), i == 0 ? "-01" : i == 8 ? "±00" : "+01", faint, 9, TextAnchor.UpperLeft);
            }
            if (sweep < 0) return;
            float py = Mathf.Lerp(rect.yMin, rect.yMax, sweep);
            Vector2 tip = new Vector2(x - dir * 2, py), a = new Vector2(x - dir * 9, py + 4), b = new Vector2(x - dir * 9, py - 4);
            Line(tip, a, ink); Line(tip, b, ink); Line(a, b, ink);
        }

        // The read-out gives the gist, not the whole log line: the first sentence, cut short.
        static string Brief(string body)
        {
            int end = body.IndexOfAny(new[] { '。', '；', '\n' });
            string first = end > 0 ? body.Substring(0, end) : body;
            return first.Length > 20 ? first.Substring(0, 19) + "…" : first;
        }

        bool Analysing => anaTarget != null && Time.unscaledTime - anaAt < AnalysisSeconds;

        /// <summary>How many tray tools carry a callout this frame.</summary>
        public int TrayCallouts { get; private set; }

        // Every tool lying in the tray carries its number key, pictogram and name on a short leader (an exploded-view
        // callout), in two staggered rows so neighbours do not run into each other. The tool in hand, the one under the
        // pointer and the one the step asks for have their own, louder tags instead.
        void TrayLabels(Camera camera, float scale, RepairBenchV2Runtime bench)
        {
            var order = bench.TrayOrder;
            if (order == null) return;
            for (int i = 0; i < order.Count; i++)
            {
                var prop = order[i];
                if (prop == null || prop.InHand || prop.Lifted || prop == HoveredProp || prop == ToolToTake) continue;
                if (!ProjectRenderers(camera, prop.GetComponentsInChildren<Renderer>(), scale, out var r)) continue;
                var tag = Tag(new Vector2(r.center.x - 12, r.yMax + 10), (i + 1) + "  " + ShortName(prop), RepairPropCyberStyle.Amber, 11, TextAnchor.LowerLeft, true, VisorIcons.Prop(prop));
                // Step up until clear of the labels already placed.
                var box = new Rect(tag.anchoredPosition, tag.sizeDelta);
                for (int tries = 0; tries < 6 && trayRects.Exists(o => o.Overlaps(box)); tries++) box.y += tag.sizeDelta.y + 3;
                tag.anchoredPosition = box.position;
                trayRects.Add(new Rect(box.x - 3, box.y - 2, box.width + 6, box.height + 4));
                float x = Mathf.Clamp(r.center.x, box.xMin + 6, box.xMax - 6);
                Line(new Vector2(r.center.x, r.yMax + 1), new Vector2(x, box.yMin), RepairPropCyberStyle.AmberDim);
                Line(new Vector2(r.center.x - 3, r.yMax + 1), new Vector2(r.center.x + 3, r.yMax + 1), RepairPropCyberStyle.Amber);
                TrayCallouts++;
            }
        }
        readonly List<Rect> trayRects = new List<Rect>();

        // "放大镜（空手检查）" is "放大镜" on a label.
        static string ShortName(RepairBenchProp prop)
        {
            string name = prop.Label; int cut = name.IndexOf('（');
            return cut > 0 ? name.Substring(0, cut) : name;
        }

        string ToolName(RepairActionType tool)
        {
            var bench = RepairBenchV2Runtime.Current;
            if (bench != null)
                foreach (var prop in bench.Props)
                    if (prop.Kind == RepairBenchPropKind.Tool && prop.Tool == tool) return prop.Label;
            return RepairActionText.Label(tool).Replace(" ", "");
        }

        // One bright block at the top of the view says what to do now. When the step changes the finished one ticks off,
        // the new one types in under a rule that sweeps across, and a line runs from the header to the part.
        void Header(PropCommand next, string step, bool batch, float right, Vector2 size, Camera camera, float scale)
        {
            var flow = owner.Flow; var d = flow.Definition; var station = owner.Station;
            bool cleaningNow = cleaning != null && cleaning.Cleaning;
            string id, chip, main, sub;
            if (cleaningNow) { id = "clean"; chip = "清洁中"; main = "已清除 " + cleaning.Progress.ToString("P0"); sub = "按住左键擦拭　·　右键单击放下工具"; }
            else if (next != null)
            {
                var node = d.FindNode(next.target);
                string where = next.target == d.rootObjectName ? "物品" : node != null ? node.displayName : next.target;
                id = next.id; chip = "下一步"; main = step.Replace("（批量）", "");
                sub = batch ? "点右侧发亮的按钮，一次做完" : station.ActiveTool == next.tool ? "左键点 " + where
                    : "先拿 " + ToolName(next.tool) + "　→　再点 " + where;
            }
            else { id = "wait"; chip = "注意"; main = RepairPropWorkflowStatus.NextStep(flow); sub = ""; }

            float now = Time.unscaledTime;
            if (id != stepId)
            {
                doneLabel = stepWasCommand && stepId != null && flow.History.Contains(stepId) ? stepMain : null;
                stepId = id; stepAt = now; stepWasCommand = next != null && !cleaningNow;
            }
            stepMain = main;
            float age = now - stepAt;
            bool ticking = doneLabel != null && age < DoneSeconds;
            float fresh = ticking ? age : age - (doneLabel != null ? DoneSeconds : 0);   // seconds the shown line has been up
            string shown = ticking ? "✓ " + doneLabel : main, shownSub = ticking ? "" : sub;
            if (stepText.text != shown) stepText.text = shown;
            if (stepSub.text != shownSub) stepSub.text = shownSub;
            var ink = ticking ? RepairPropCyberStyle.Ok : id == "wait" ? RepairPropCyberStyle.Warn : RepairPropCyberStyle.Amber;
            stepText.color = ink;
            chipText.text = ticking ? "完成" : chip;
            // The chip is the one inverse block on the visor: it swells when the line changes, then breathes slowly.
            float pop = 1 - HudMotion.EaseOut(fresh / .28f);
            ink.a = .88f + .12f * Mathf.Sin(now * 2.4f);
            chipImage.color = ink;
            chipImage.rectTransform.localScale = Vector3.one * (1 + .28f * pop);

            // The picture of the step, in its tool's colour, between the chip and the words.
            StepIconName = ticking ? "check" : cleaningNow ? "brush" : next != null ? VisorIcons.CommandName(next) : null;
            var picture = VisorIcons.Get(StepIconName);
            stepIcon.enabled = picture != null;
            float iconWidth = picture != null ? 34 : 0;
            float chipWidth = 62, gap = 12, textWidth = Mathf.Min(stepText.preferredWidth, right - 260);
            float width = chipWidth + gap + iconWidth + textWidth, x0 = right / 2 - width / 2, y = size.y - 74;
            if (picture != null)
            {
                stepIcon.sprite = picture;
                stepIcon.color = ticking ? RepairPropCyberStyle.Ok : cleaningNow ? RepairPropCyberStyle.Amber : VisorIcons.ToolColor(next.tool);
                var pictureRect = stepIcon.rectTransform;
                pictureRect.anchorMin = pictureRect.anchorMax = Vector2.zero; pictureRect.pivot = new Vector2(.5f, .5f);
                pictureRect.anchoredPosition = new Vector2(x0 + chipWidth + gap + 12, y + 14); pictureRect.sizeDelta = new Vector2(24, 24);
                pictureRect.localScale = Vector3.one * (1 + .35f * pop);
            }
            stepSub.color = !ticking && next != null && !cleaningNow && !batch && station.ActiveTool != next.tool ? VisorIcons.ToolColor(next.tool) : RepairPropCyberStyle.Dim;
            var chipRect = chipImage.rectTransform;
            chipRect.anchorMin = chipRect.anchorMax = Vector2.zero; chipRect.pivot = new Vector2(.5f, .5f);
            chipRect.anchoredPosition = new Vector2(x0 + chipWidth / 2, y + 14); chipRect.sizeDelta = new Vector2(chipWidth, 24);
            var textRect = stepText.rectTransform;
            textRect.anchorMin = textRect.anchorMax = Vector2.zero; textRect.pivot = new Vector2(0, .5f);
            textRect.anchoredPosition = new Vector2(x0 + chipWidth + gap + iconWidth, y + 14); textRect.sizeDelta = new Vector2(textWidth + 8, 30);
            Place(stepSub.rectTransform, new Vector2(right / 2, y - 22), new Vector2(Mathf.Min(right - 120, 720), 18));
            float plate = Mathf.Max(width, stepSub.preferredWidth) + 44;
            Place(headerBack.rectTransform, new Vector2(right / 2, y - 28), new Vector2(Mathf.Min(plate, right - 60), 62));
            HeaderRect = Rect.MinMaxRect(right / 2 - plate / 2, y - 28, right / 2 + plate / 2, y + 34);

            // The rule under the line sweeps out from the centre.
            float sweep = HudMotion.EaseOut(fresh / .35f) * (width / 2 + 10);
            var rule = stepText.color; rule.a = .9f;
            Line(new Vector2(right / 2 - sweep, y - 3), new Vector2(right / 2 + sweep, y - 3), rule);

            // The trace: from the header down to the part the step is about, drawn once when the step comes up and again
            // every few seconds while the player waits.
            TraceTarget = null;
            if (!ticking && next != null && !cleaningNow && Project(camera, next.target, scale, out var target))
            {
                float cycle = fresh < TraceSeconds ? fresh : Mathf.Repeat(fresh - TraceSeconds, TraceEvery);
                if (cycle < TraceSeconds && target.yMax < y - 40)
                {
                    TraceTarget = next.target;
                    float draw = HudMotion.EaseOut(cycle / .45f), fade = Mathf.Clamp01((TraceSeconds - cycle) / .45f);
                    var colour = RepairPropCyberStyle.Amber; colour.a = .85f * fade;
                    Vector2 from = new Vector2(right / 2, y - 30), to = new Vector2(target.center.x, target.yMax + 10);
                    var elbow = new Vector2(to.x, from.y - 14);
                    // down a little, across, then down onto the part: a drafted leader, not a diagonal
                    float l1 = 14, l2 = Mathf.Abs(to.x - from.x), l3 = Mathf.Max(0, elbow.y - to.y), total = l1 + l2 + l3, done = draw * total;
                    var knee = new Vector2(from.x, from.y - 14);
                    Line(from, Vector2.Lerp(from, knee, Mathf.Clamp01(done / l1)), colour);
                    if (done > l1 && l2 > 1) Line(knee, Vector2.Lerp(knee, elbow, Mathf.Clamp01((done - l1) / l2)), colour);
                    if (done > l1 + l2 && l3 > 1)
                    {
                        var head = Vector2.Lerp(elbow, to, Mathf.Clamp01((done - l1 - l2) / l3));
                        Line(elbow, head, colour);
                        Line(head + new Vector2(-5, 0), head + new Vector2(5, 0), colour);
                    }
                }
            }
        }

        // Seconds since this mark (re)appeared; a mark absent for a quarter second locks on again.
        float Age(string key)
        {
            float now = Time.unscaledTime;
            if (!locks.TryGetValue(key, out var entry) || now - entry.last > .25f) entry.first = now;
            entry.last = now;
            locks[key] = entry;
            return now - entry.first;
        }

        // Corner brackets that close in from wide and fade up, with one scan line across the box while locking.
        void Lock(Rect r, Color color, float arm, string key)
        {
            float age = Age(key), t = HudMotion.EaseOut(age / .22f);
            color.a *= Mathf.Clamp01(.25f + age / .12f);
            Brackets(Grow(r, (1 - t) * 34), color, arm);
            if (age < .45f && r.height > 6)
            {
                float y = Mathf.Lerp(r.yMax, r.yMin, HudMotion.EaseOut(age / .45f));
                var scan = color; scan.a *= .7f * (1 - age / .45f);
                Line(new Vector2(r.xMin, y), new Vector2(r.xMax, y), scan);
            }
        }

        // Corner marks on an object's oriented box (the same box the selection wire uses), so every mark on the item
        // shares its angle. Locking on: the box starts half again larger and closes in, fading up.
        readonly Vector3[] box3 = new Vector3[8];
        readonly Vector2[] box2 = new Vector2[8];

        bool OrientedBox(IEnumerable<MeshFilter> filters, Transform frame, float grow)
        {
            if (frame == null) return false;
            bool any = false; Vector3 min = default, max = default;
            foreach (var filter in filters)
            {
                if (filter == null || filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var world = filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    var local = frame.InverseTransformPoint(world);
                    if (!any) { min = max = local; any = true; } else { min = Vector3.Min(min, local); max = Vector3.Max(max, local); }
                }
            }
            if (!any) return false;
            var c = (min + max) * .5f; var e = (max - min) * .5f * grow;
            for (int i = 0; i < 8; i++)
                box3[i] = frame.TransformPoint(c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
            return true;
        }

        Transform Frame(string target)
        {
            var parked = owner.BenchPresentation != null ? owner.BenchPresentation.ParkedVisual(target) : null;
            return parked != null && parked.gameObject.activeInHierarchy ? parked : owner.Flow.GetObject(target);
        }

        bool TargetBox(string target, float grow) => OrientedBox(Meshes(target).Select(m => m.filter), Frame(target), grow);

        void LockBox(Camera camera, float scale, Color color, float fraction, string key)
        {
            float age = Age(key), t = HudMotion.EaseOut(age / .22f);
            color.a *= Mathf.Clamp01(.25f + age / .12f);
            var centre = Vector3.zero;
            for (int i = 0; i < 8; i++) centre += box3[i] / 8f;
            float spread = 1 + (1 - t) * .5f;
            for (int i = 0; i < 8; i++)
            {
                var p = camera.WorldToScreenPoint(centre + (box3[i] - centre) * spread);
                if (p.z <= 0) return;
                box2[i] = (Vector2)p / scale;
            }
            for (int i = 0; i < 8; i++)
                for (int axis = 1; axis <= 4; axis <<= 1)
                {
                    var a = box2[i]; var b = box2[i ^ axis];
                    Line(a, a + (b - a) * fraction, color);
                }
        }

        // A leader line that draws out from its start over its first fifth of a second.
        void Leader(Vector2 from, Vector2 to, Color color, string key)
        {
            float t = HudMotion.EaseOut(Age(key) / .2f);
            Line(from, Vector2.Lerp(from, to, Mathf.Max(.02f, t)), color);
        }

        Text MakeText(string name, int size, TextAnchor anchor, Color color)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(root, false);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.alignment = anchor; text.color = color; text.raycastTarget = false;
            text.supportRichText = false; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            var shadow = rect.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, .9f); shadow.effectDistance = new Vector2(1, -1);
            return text;
        }

        void LateUpdate()
        {
            usedLines = usedTags = 0;
            bool show = owner != null && owner.WorkspaceVisible && owner.Station != null && owner.Station.Inspector.ViewCamera != null
                && (owner.ContextPanel == null || !owner.ContextPanel.AppearanceOpen);
            canvas.enabled = show;
            if (show) Draw();
            // The short HUD drops the visor's decorative read-outs; the tutorial's own card replaces the guide band.
            bool readouts = !RepairPropWorkspaceLayout.Compact, guide = !GuideHidden;
            visorId.enabled = clock.enabled = ticker.enabled = readouts;
            readout.enabled = false;   // the test list is in the order window; the top of the view belongs to the step header
            if (hintBand.gameObject.activeSelf != guide) hintBand.gameObject.SetActive(guide);
            hint.enabled = false; controls.enabled = guide;   // the hint's words live in the step header now
            for (int i = usedLines; i < lines.Count; i++) if (lines[i].enabled) lines[i].enabled = false;
            for (int i = usedTags; i < tags.Count; i++) if (tags[i].rect.gameObject.activeSelf) tags[i].rect.gameObject.SetActive(false);
        }

        void Draw()
        {
            var flow = owner.Flow;
            var d = flow.Definition;
            var camera = owner.Station.Inspector.ViewCamera;
            float scale = canvas.scaleFactor > 0 ? canvas.scaleFactor : 1;
            var size = root.rect.size;
            float pulse = .6f + .4f * Mathf.Sin(Time.unscaledTime * 4.2f);
            var panel = owner.ContextPanel != null && owner.ContextPanel.Visible ? owner.ContextPanel.PanelRectTransform : null;
            float right = size.x - (panel != null ? panel.rect.width + 16 : 0);
            tagRight = right - 6;

            VisorFrame(size, right);
            WatchForAnalysis(flow, d);

            // Pointer part (only when the pointer is over the view, not the panel).
            HoveredTarget = null; HoveredProp = null; ToolPreview = null; pointerOnItem = false;
            var pointer = RepairInput.PointerPosition;
            if (!RepairInput.PointerOverUI && (owner.ContextPanel == null || !owner.ContextPanel.ContainsScreenPoint(pointer)))
                HoveredTarget = Pick(pointer);

            // Actionable parts: dim cyan corner marks, so the player sees at a glance where work can happen.
            foreach (var node in d.nodes)
            {
                if (node.objectName == d.rootObjectName || node.objectName == owner.SelectedTarget || node.objectName == HoveredTarget) continue;
                if (!RepairPropWorkflowStatus.HasAvailableCommand(flow, node.objectName)) continue;
                if (TargetBox(node.objectName, 1.04f)) LockBox(camera, scale, RepairPropCyberStyle.HotDim, .22f, "act:" + node.objectName);
            }

            // Whole item: thin corner frame with the case and phase.
            var phase = RepairPropWorkflowStatus.Phase(flow);
            if (Project(camera, d.rootObjectName, scale, out var itemRect))
            {
                if (TargetBox(d.rootObjectName, 1.06f)) LockBox(camera, scale, RepairPropCyberStyle.AmberDim, .12f, "item:" + d.rootObjectName);
                Tag(new Vector2(itemRect.xMin - 10, itemRect.yMax + 14), RepairPropText.Item(d.kind) + "  ·  " + RepairPropWorkflowStatus.Label(phase),
                    RepairPropCyberStyle.Amber, 12, TextAnchor.LowerLeft);
            }

            // Suggested next step: pulsing amber marks on its target, a callout with the command and the tool to use.
            var next = RepairPropWorkflowStatus.NextCommand(flow);
            NextTarget = next?.target;
            if (next != null && Project(camera, next.target, scale, out var nextRect))
            {
                var color = RepairPropCyberStyle.Amber; color.a = pulse;
                bool whole = next.target == d.rootObjectName;
                var box = whole ? Grow(nextRect, 18) : Grow(nextRect, 6);
                if (TargetBox(next.target, whole ? 1.10f : 1.08f)) LockBox(camera, scale, color, whole ? .14f : .3f, "next:" + next.target);
                var tool = owner.Station.ActiveTool;
                string toolHint = tool == next.tool ? "" : "　[" + RepairActionText.Label(next.tool).Replace(" ", "") + "]";
                var anchor = new Vector2(box.xMax + 26, box.yMax + 20);
                Leader(new Vector2(box.xMax, box.yMax), anchor + new Vector2(0, -6), color, "nextline:" + next.target);
                Tag(anchor, "▶ " + next.label, color, 13, TextAnchor.LowerLeft, true, VisorIcons.Command(next));   // where; the header says what and with which tool
            }

            // Selection: amber wireframe box and a name tag with what can be done to it.
            if (owner.SelectedTarget != d.rootObjectName && Wire(camera, owner.SelectedTarget, scale, RepairPropCyberStyle.Amber, out var selRect))
            {
                var node = d.FindNode(owner.SelectedTarget);
                int ready = d.commands.Count(c => c.target == owner.SelectedTarget && flow.CanExecute(c.id, c.target, c.tool, out _));
                string state = node != null && node.immutable ? "保留原件 · 只检查" : ready > 0 ? ready + " 项可执行" : "暂无可执行操作";
                var anchor = new Vector2(Mathf.Max(selRect.xMin - 150, 8), selRect.yMin - 18);
                var from = new Vector2(selRect.xMin, selRect.yMin);
                // Never under the hint bar: flip above the part instead.
                if (anchor.y - 40 < bandTop) { anchor.y = selRect.yMax + 52; from.y = selRect.yMax; }
                Leader(from, anchor + new Vector2(140, -8), RepairPropCyberStyle.Amber, "selline:" + owner.SelectedTarget);
                Tag(anchor, "▣ " + (node != null ? node.displayName : owner.SelectedTarget) + "\n" + state, RepairPropCyberStyle.Amber, 12, TextAnchor.UpperLeft, true);
            }

            // Pointer: cyan wireframe and the part name right at the cursor. With a tool in hand the tag says what a left
            // click does here (green) or why it does nothing (red), so the tool reads as something to use, not a mode.
            string toolTarget = HoveredTarget ?? (pointerOnItem ? d.rootObjectName : null);
            if (toolTarget != null && owner.Station.ActiveTool.HasValue)
            {
                var command = owner.ToolCommandAt(toolTarget, out string why);
                ToolPreview = command != null ? "左键 ▶ " + command.label : "✕ " + why;
                var color = command != null ? RepairPropCyberStyle.Ok : RepairPropCyberStyle.Bad;
                var previewIcon = command != null ? VisorIcons.Command(command) : VisorIcons.Get("cross");
                if (HoveredTarget != null && HoveredTarget != owner.SelectedTarget) Wire(camera, HoveredTarget, scale, color, out _);
                var node = d.FindNode(toolTarget);
                // While a reading plays on the item the cursor tag steps aside, so the two read-outs never stack.
                if (!Analysing)
                    Tag(pointer / scale + new Vector2(18, -12), (node != null ? node.displayName : toolTarget) + "\n" + ToolPreview, color, 12, TextAnchor.UpperLeft, true, previewIcon);
            }
            else if (HoveredTarget != null && HoveredTarget != owner.SelectedTarget && Wire(camera, HoveredTarget, scale, VisorIcons.Scan, out _))
            {
                var node = d.FindNode(HoveredTarget);
                Tag(pointer / scale + new Vector2(16, -10), node != null ? node.displayName : HoveredTarget, VisorIcons.Scan, 12, TextAnchor.UpperLeft, true, VisorIcons.Get("loupe"));
            }

            // Bench prop under the pointer: cyan wire and what a click does.
            if (HoveredProp != null && WireRenderers(camera, HoveredProp.GetComponentsInChildren<MeshFilter>(), scale, HoveredProp.Kind == RepairBenchPropKind.Tool ? VisorIcons.PropColor(HoveredProp) : RepairPropCyberStyle.Hot))
                Tag(pointer / scale + new Vector2(16, -10), HoveredProp.Hint, HoveredProp.Kind == RepairBenchPropKind.Tool ? VisorIcons.PropColor(HoveredProp) : RepairPropCyberStyle.Hot, 12, TextAnchor.UpperLeft, true,
                    HoveredProp.Kind == RepairBenchPropKind.Tool ? VisorIcons.Prop(HoveredProp) : null);
            // The tool in hand, marked on the bench as well.
            var bench = RepairBenchV2Runtime.Current;
            trayRects.Clear();
            if (bench != null)
                foreach (var prop in bench.Props)
                    if (prop.Kind == RepairBenchPropKind.Tool && prop.Lifted && !prop.InHand && prop != HoveredProp && ProjectRenderers(camera, prop.GetComponentsInChildren<Renderer>(), scale, out var toolRect))
                    {
                        if (OrientedBox(prop.GetComponentsInChildren<MeshFilter>(), prop.transform, 1.06f)) LockBox(camera, scale, RepairPropCyberStyle.Amber, .25f, "tool:" + prop.name);
                        var heldTag = Tag(new Vector2(toolRect.xMin, toolRect.yMax + 8), "手持 · " + ShortName(prop), VisorIcons.PropColor(prop), 11, TextAnchor.LowerLeft, true, VisorIcons.Prop(prop));
                        trayRects.Add(new Rect(heldTag.anchoredPosition - new Vector2(3, 2), heldTag.sizeDelta + new Vector2(6, 4)));
                    }

            // With what: the step needs a tool the hand does not hold, so that tool is marked in the tray.
            TrayCallouts = 0;
            ToolToTake = null;
            if (bench != null && next != null && owner.Station.ActiveTool != next.tool && !owner.Station.CleaningActive
                && (owner.ContextPanel == null || owner.ContextPanel.BatchButtonFor(next.id) == null))
                foreach (var prop in bench.Props)
                {
                    if (prop.Kind != RepairBenchPropKind.Tool || prop.IsCleaner || prop.IsPainter || prop.Tool != next.tool || prop.InHand) continue;
                    if (!ProjectRenderers(camera, prop.GetComponentsInChildren<Renderer>(), scale, out var takeRect)) continue;
                    ToolToTake = prop;
                    var take = VisorIcons.PropColor(prop); take.a = pulse;
                    if (OrientedBox(prop.GetComponentsInChildren<MeshFilter>(), prop.transform, 1.1f)) LockBox(camera, scale, take, .3f, "take:" + prop.name);
                    if (prop != HoveredProp) { var takeTag = Tag(new Vector2(takeRect.xMin, takeRect.yMax + 8), "拿这个 ▶ " + prop.Label, take, 12, TextAnchor.LowerLeft, true, VisorIcons.Prop(prop)); trayRects.Add(new Rect(takeTag.anchoredPosition - new Vector2(3, 2), takeTag.sizeDelta + new Vector2(6, 4))); }
                }
            if (bench != null) TrayLabels(camera, scale, bench);

            // Top-centre scan line over the free view: tests at a glance.
            readout.rectTransform.anchorMin = readout.rectTransform.anchorMax = Vector2.zero;
            readout.rectTransform.pivot = new Vector2(.5f, 1);
            readout.rectTransform.anchoredPosition = new Vector2(right / 2, size.y - 22);
            readout.rectTransform.sizeDelta = new Vector2(Mathf.Min(right - 80, 820), 40);
            readout.alignment = TextAnchor.UpperCenter;
            readout.text = "SCAN // " + RepairPropText.Item(d.kind) + "　" + string.Join("　", d.tests.Select(t =>
                t.label + " [" + RepairPropWorkflowStatus.TestStatusLabel(flow, t, RepairPropWorkflowStatus.TestStatus(flow, t, owner.TestJournal)) + "]"));

            // Bottom hint, centred over the free view (left of the panel): the suggested step and its tool when there
            // is one, otherwise the flow's first blocker.
            // When a batch in the panel carries the suggested step, the hint names the batch (one click does the run).
            var batch = next != null && owner.ContextPanel != null ? owner.ContextPanel.BatchButtonFor(next.id) : null;
            string step = batch != null ? batch.GetComponentInChildren<Text>().text + "（批量）" : next != null ? next.label : null;
            hint.text = "提示 // " + (next != null ? "下一步 " + step + (batch != null || owner.Station.ActiveTool == next.tool ? ""
                : "（工具：" + RepairActionText.Label(next.tool).Replace(" ", "") + "）") : RepairPropWorkflowStatus.NextStep(flow));
            if (cleaning == null) cleaning = FindFirstObjectByType<BorderRepair.TwoNight.ClinicCleaningController>();
            if (cleaning != null && cleaning.Cleaning)
                hint.text = "清洁中 // 已清除 " + cleaning.Progress.ToString("P0") + "　按住左键擦拭　·　右键单击放下工具";
            // The step itself is the header at the top; down here only the control legend is left, small and dim.
            Place(hint.rectTransform, new Vector2(right / 2, 44), new Vector2(Mathf.Min(right - 60, 760), 60));
            bandTop = 12 + 44;
            Place(hintBand, new Vector2(right / 2, 12), new Vector2(Mathf.Min(right - 40, controls.preferredWidth + 60), 44));
            Header(next, step, batch != null, right, size, camera, scale);
            Analysis(camera, scale);
            Place(controls.rectTransform, new Vector2(right / 2, 20), new Vector2(Mathf.Min(right - 60, 760), 20));
        }

        static void Place(RectTransform rect, Vector2 bottomCentre, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = bottomCentre; rect.sizeDelta = size;
        }

        void VisorFrame(Vector2 size, float right)
        {
            // Large corner marks at the edges of the free view: the visor's field.
            Brackets(Rect.MinMaxRect(12, 12, right - 4, size.y - 12), RepairPropCyberStyle.AmberFaint, 40);
            Line(new Vector2(right / 2 - 60, size.y - 16), new Vector2(right / 2 + 60, size.y - 16), RepairPropCyberStyle.AmberDim);
            // Heading ruler under the scan line: ticks slide with the view's yaw.
            var view = owner.Station.Inspector.ViewCamera;
            float yaw = view != null ? view.transform.eulerAngles.y : 0, shift = Mathf.Repeat(yaw * 3f, 12f);
            for (float x = -120 + shift; x <= 120; x += 12)
            {
                float fade = 1 - Mathf.Abs(x) / 130f, h = Mathf.Repeat(x - shift + 120, 48f) < 1 ? 7 : 3;
                Line(new Vector2(right / 2 + x, size.y - 62), new Vector2(right / 2 + x, size.y - 62 + h), new Color(1f, .62f, .16f, .30f * fade));
            }
            Pin(visorId.rectTransform, new Vector2(22, 40), Vector2.zero, new Vector2(360, 16));
            var session = owner.Station.Session;
            var state = BorderRepair.TwoNight.TwoNightRun.Current;
            clock.text = "N" + (state != null ? Mathf.Max(1, state.night) : 1) + " // T+" + RepairUIView.FormatTime(session.ShiftDuration)
                + "  CASE " + (session.CaseIndex + 1) + "/" + session.CaseCount;
            clock.color = new Color(1f, .62f, .16f, Mathf.Repeat(Time.unscaledTime, 1.6f) < 1.1f ? .45f : .25f);
            Pin(clock.rectTransform, new Vector2(22, 22), Vector2.zero, new Vector2(320, 16));
            if (ticker.gameObject.activeSelf) ticker.gameObject.SetActive(false);   // decluttered: the hex column competed with the windows
            if (false && Time.unscaledTime >= tickerNext)
            {
                tickerNext = Time.unscaledTime + .14f; tickerSeed++;
                var lines = new System.Text.StringBuilder();
                for (int i = 0; i < 10; i++)
                {
                    int n = tickerSeed + i * 7;
                    lines.Append(((int)(HudMotion.Noise(n * .37f) * 65535)).ToString("X4")).Append(' ')
                        .Append(((int)(HudMotion.Noise(n * 1.91f) * 255)).ToString("X2")).Append('\n');
                }
                ticker.text = lines.ToString();
            }
            Pin(ticker.rectTransform, new Vector2(30, size.y * .62f), new Vector2(0, 1), new Vector2(80, 140));
        }

        static void Pin(RectTransform rect, Vector2 position, Vector2 pivot, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = pivot;
            rect.anchoredPosition = position; rect.sizeDelta = size;
        }

        string Pick(Vector2 point)
        {
            var camera = owner.Station.Inspector.ViewCamera;
            Physics.SyncTransforms();
            if (Physics.Raycast(camera.ScreenPointToRay(point), out var hit, 10, ~0, QueryTriggerInteraction.Ignore))
            {
                var parked = hit.transform.GetComponentInParent<RepairPropParkedPart>();
                if (parked != null && parked.Owner == owner.Flow) return parked.Target;
                HoveredProp = hit.transform.GetComponentInParent<RepairBenchProp>();
                if (HoveredProp != null) return null;
            }
            bool hitItem = false;
            var picked = owner.Station.Scanner != null ? owner.Station.Scanner.PickPoint(point, out hitItem) : null;
            pointerOnItem = hitItem;
            if (picked != null && owner.Flow.Definition.FindNode(picked.PointId) != null) return picked.PointId;
            return null;
        }

        // Renderers that currently show a node: its parked proxy when it lies on the bench, else the item's own meshes.
        IEnumerable<(MeshFilter filter, Renderer renderer)> Meshes(string target)
        {
            var parked = owner.BenchPresentation != null ? owner.BenchPresentation.ParkedVisual(target) : null;
            Transform source = parked != null && parked.gameObject.activeInHierarchy ? parked : owner.Flow.GetObject(target);
            if (source == null) yield break;
            var attachments = parked == source ? null : Attachments(target);
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<Renderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (attachments != null && attachments.Exists(a => a != source && filter.transform.IsChildOf(a))) continue;
                yield return (filter, renderer);
            }
        }

        // Sub-parts that only show in some state (a test lead hanging out while the item is on the fixture): they belong to
        // their own box, not to the parent's, or the parent's frame stretches out over empty bench.
        List<Transform> Attachments(string target)
        {
            attachmentList.Clear();
            foreach (var node in owner.Flow.Definition.nodes)
            {
                if (node == null || node.objectName == target || string.IsNullOrEmpty(node.visibleKey)) continue;
                var t = owner.Flow.GetObject(node.objectName);
                if (t != null) attachmentList.Add(t);
            }
            return attachmentList;
        }
        readonly List<Transform> attachmentList = new List<Transform>();

        bool Project(Camera camera, string target, float scale, out Rect rect)
        {
            bool any = false; Vector2 min = default, max = default;
            foreach (var (_, renderer) in Meshes(target))
            {
                var b = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = camera.WorldToScreenPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                    if (p.z <= 0) continue;
                    var s = (Vector2)p / scale;
                    if (!any) { min = max = s; any = true; } else { min = Vector2.Min(min, s); max = Vector2.Max(max, s); }
                }
            }
            rect = any ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : default;
            return any && rect.width > 1 && rect.height > 1;
        }

        // Oriented wire box per mesh (mesh bounds are available even on non-readable meshes).
        bool Wire(Camera camera, string target, float scale, Color color, out Rect rect)
        {
            bool any = false; Vector2 min = default, max = default;
            foreach (var (filter, _) in Meshes(target))
            {
                var b = filter.sharedMesh.bounds;
                bool visible = true;
                for (int i = 0; i < 8; i++)
                {
                    corners[i] = filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    var p = camera.WorldToScreenPoint(corners[i]);
                    if (p.z <= 0) { visible = false; break; }
                    screen[i] = (Vector2)p / scale;
                    if (!any) { min = max = screen[i]; any = true; } else { min = Vector2.Min(min, screen[i]); max = Vector2.Max(max, screen[i]); }
                }
                if (!visible) continue;
                for (int i = 0; i < 8; i++)
                    for (int axis = 1; axis <= 4; axis <<= 1)
                        if ((i & axis) == 0) Line(screen[i], screen[i | axis], color);
            }
            rect = any ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : default;
            return any;
        }

        bool ProjectRenderers(Camera camera, Renderer[] renderers, float scale, out Rect rect)
        {
            bool any = false; Vector2 min = default, max = default;
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled) continue;
                var b = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = camera.WorldToScreenPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                    if (p.z <= 0) continue;
                    var s = (Vector2)p / scale;
                    if (!any) { min = max = s; any = true; } else { min = Vector2.Min(min, s); max = Vector2.Max(max, s); }
                }
            }
            rect = any ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : default;
            return any;
        }

        bool WireRenderers(Camera camera, MeshFilter[] filters, float scale, Color color)
        {
            bool any = false;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                bool visible = true;
                for (int i = 0; i < 8; i++)
                {
                    var p = camera.WorldToScreenPoint(filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                    if (p.z <= 0) { visible = false; break; }
                    screen[i] = (Vector2)p / scale;
                }
                if (!visible) continue;
                any = true;
                for (int i = 0; i < 8; i++)
                    for (int axis = 1; axis <= 4; axis <<= 1)
                        if ((i & axis) == 0) Line(screen[i], screen[i | axis], color);
            }
            return any;
        }

        static Rect Grow(Rect r, float by) => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        void Brackets(Rect r, Color color, float arm)
        {
            arm = Mathf.Min(arm, Mathf.Max(3, Mathf.Min(r.width, r.height) * Bracket * 2));
            foreach (var (c, dx, dy) in new[] { (new Vector2(r.xMin, r.yMin), 1, 1), (new Vector2(r.xMax, r.yMin), -1, 1),
                (new Vector2(r.xMin, r.yMax), 1, -1), (new Vector2(r.xMax, r.yMax), -1, -1) })
            {
                Line(c, c + new Vector2(dx * arm, 0), color);
                Line(c, c + new Vector2(0, dy * arm), color);
            }
        }

        void Line(Vector2 a, Vector2 b, Color color)
        {
            Image image;
            if (usedLines < lines.Count) image = lines[usedLines];
            else
            {
                var rect = (RectTransform)new GameObject("Line", typeof(RectTransform)).transform;
                rect.SetParent(root, false);
                rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = new Vector2(0, .5f);
                image = rect.gameObject.AddComponent<Image>(); image.sprite = RepairPropCyberStyle.Line; image.raycastTarget = false;
                lines.Add(image);
            }
            usedLines++;
            var delta = b - a;
            var t = image.rectTransform;
            t.anchoredPosition = a;
            t.sizeDelta = new Vector2(delta.magnitude, LineWidth);
            t.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            image.color = color;
            if (!image.enabled) image.enabled = true;
        }

        RectTransform Tag(Vector2 at, string text, Color color, int size, TextAnchor anchor, bool framed = false, Sprite icon = null)
        {
            (RectTransform rect, Image bg, Text text) tag;
            if (usedTags < tags.Count) tag = tags[usedTags];
            else
            {
                var rect = (RectTransform)new GameObject("Tag", typeof(RectTransform)).transform;
                rect.SetParent(root, false);
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                var bg = rect.gameObject.AddComponent<Image>(); bg.raycastTarget = false;
                RepairPropCyberStyle.Framed(bg, RepairPropCyberStyle.Field, RepairPropCyberStyle.AmberDim, true);
                var label = (RectTransform)new GameObject("Text", typeof(RectTransform)).transform;
                label.SetParent(rect, false);
                label.anchorMin = Vector2.zero; label.anchorMax = Vector2.one; label.offsetMin = new Vector2(7, 3); label.offsetMax = new Vector2(-7, -3);
                var t = label.gameObject.AddComponent<Text>(); t.font = font; t.raycastTarget = false; t.supportRichText = false;
                label.gameObject.AddComponent<HudTypeOn>().charsPerSecond = 260;
                var shadow = label.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, .85f); shadow.effectDistance = new Vector2(1, -1);
                t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
                tag = (rect, bg, t);
                tags.Add(tag);
                var picture = (RectTransform)new GameObject("Icon", typeof(RectTransform)).transform;
                picture.SetParent(rect, false);
                picture.anchorMin = picture.anchorMax = new Vector2(0, 1); picture.pivot = new Vector2(0, 1);
                picture.anchoredPosition = new Vector2(6, -4); picture.sizeDelta = new Vector2(12, 12);
                var pictureImage = picture.gameObject.AddComponent<Image>(); pictureImage.raycastTarget = false;
                tagIcons.Add(pictureImage);
            }
            var tagIcon = tagIcons[usedTags];
            usedTags++;
            tag.rect.gameObject.SetActive(true);
            tagIcon.enabled = icon != null;
            if (icon != null) { tagIcon.sprite = icon; tagIcon.color = color; }
            tag.text.rectTransform.offsetMin = new Vector2(icon != null ? 23 : 7, 3);
            tag.text.text = text; tag.text.fontSize = size; tag.text.color = color; tag.text.alignment = TextAnchor.MiddleLeft;
            float width = tag.text.preferredWidth + (icon != null ? 30 : 14), height = Mathf.Max(tag.text.preferredHeight + 6, icon != null ? 20 : 0);
            tag.rect.pivot = anchor == TextAnchor.UpperLeft ? new Vector2(0, 1) : new Vector2(0, 0);
            var area = root.rect.size;
            float maxX = Mathf.Min(area.x, tagRight > 0 ? tagRight : area.x) - width - 4;
            tag.rect.anchoredPosition = new Vector2(Mathf.Clamp(at.x, 4, Mathf.Max(4, maxX)), Mathf.Clamp(at.y, 4, area.y - 4));
            tag.rect.sizeDelta = new Vector2(width, height);
            tag.bg.enabled = framed;
            RepairPropCyberStyle.SetFrame(tag.bg, color * new Color(1, 1, 1, .8f));
            var frameImage = tag.bg.transform.Find("Frame");
            if (frameImage != null) frameImage.gameObject.SetActive(framed);
            return tag.rect;
        }

        void OnDestroy()
        {
            if (owner != null && owner.Flow != null) owner.Flow.Feedback -= OnFlowFeedback;
            if (canvas != null) Destroy(canvas.gameObject);
        }
    }
}
