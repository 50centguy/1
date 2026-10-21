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
        float tagRight, bandTop;
        readonly Vector3[] corners = new Vector3[8];
        readonly Vector2[] screen = new Vector2[8];

        public string HoveredTarget { get; private set; }
        public RepairBenchProp HoveredProp { get; private set; }
        public string HintText => hint != null ? hint.text : "";
        public string NextTarget { get; private set; }
        public bool Showing => canvas != null && canvas.enabled;

        public void Initialize(RepairPropStationBridge bridge)
        {
            owner = bridge;
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
            controls.text = "左键 选择 / 执行　·　拖动 拿起旋转　·　滚轮 缩放　·　R 放下复位";
            visorId = MakeText("VisorId", 11, TextAnchor.UpperLeft, RepairPropCyberStyle.AmberFaint * new Color(1, 1, 1, 2.2f));
            if (RepairPropCyberStyle.Kana != null) visorId.font = RepairPropCyberStyle.Kana;
            visorId.text = "VSR-07 // 修理視界　ＡＲオーバーレイ";
            clock = MakeText("Clock", 11, TextAnchor.LowerLeft, RepairPropCyberStyle.AmberDim);
            ticker = MakeText("Ticker", 9, TextAnchor.UpperLeft, new Color(1f, .62f, .16f, .22f));
            if (RepairPropCyberStyle.Mono != null) { clock.font = RepairPropCyberStyle.Mono; ticker.font = RepairPropCyberStyle.Mono; readout.font = RepairPropCyberStyle.Mono; }
            ticker.lineSpacing = 1.1f;
            HudMotion.TypeOn(hint, 220);
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

            // Pointer part (only when the pointer is over the view, not the panel).
            HoveredTarget = null; HoveredProp = null;
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
                Tag(anchor, "下一步 ▶ " + next.label + toolHint, color, 13, TextAnchor.LowerLeft, true);
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

            // Pointer: cyan wireframe and the part name right at the cursor.
            if (HoveredTarget != null && HoveredTarget != owner.SelectedTarget && Wire(camera, HoveredTarget, scale, RepairPropCyberStyle.Hot, out _))
            {
                var node = d.FindNode(HoveredTarget);
                Tag(pointer / scale + new Vector2(16, -10), node != null ? node.displayName : HoveredTarget, RepairPropCyberStyle.Hot, 12, TextAnchor.UpperLeft);
            }

            // Bench prop under the pointer: cyan wire and what a click does.
            if (HoveredProp != null && WireRenderers(camera, HoveredProp.GetComponentsInChildren<MeshFilter>(), scale, RepairPropCyberStyle.Hot))
                Tag(pointer / scale + new Vector2(16, -10), HoveredProp.Hint, RepairPropCyberStyle.Hot, 12, TextAnchor.UpperLeft, true);
            // The tool in hand, marked on the bench as well.
            var bench = RepairBenchV2Runtime.Current;
            if (bench != null)
                foreach (var prop in bench.Props)
                    if (prop.Kind == RepairBenchPropKind.Tool && prop.Lifted && prop != HoveredProp && ProjectRenderers(camera, prop.GetComponentsInChildren<Renderer>(), scale, out var toolRect))
                    {
                        if (OrientedBox(prop.GetComponentsInChildren<MeshFilter>(), prop.transform, 1.06f)) LockBox(camera, scale, RepairPropCyberStyle.Amber, .25f, "tool:" + prop.name);
                        Tag(new Vector2(toolRect.xMin, toolRect.yMax + 8), "手持 · " + prop.Label, RepairPropCyberStyle.Amber, 11, TextAnchor.LowerLeft);
                    }

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
            Place(hint.rectTransform, new Vector2(right / 2, 44), new Vector2(Mathf.Min(right - 60, 760), 60));
            float hintHeight = hint.preferredHeight;
            bandTop = 12 + hintHeight + 40;
            Place(hintBand, new Vector2(right / 2, 12), new Vector2(Mathf.Min(right - 40, Mathf.Max(hint.preferredWidth, controls.preferredWidth) + 60), hintHeight + 40));
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
                + "   CASE " + (session.CaseIndex + 1) + "/" + session.CaseCount + "   REC";
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
            var picked = owner.Station.Scanner != null ? owner.Station.Scanner.PickPoint(point, out bool hitItem) : null;
            if (picked != null && owner.Flow.Definition.FindNode(picked.PointId) != null) return picked.PointId;
            return null;
        }

        // Renderers that currently show a node: its parked proxy when it lies on the bench, else the item's own meshes.
        IEnumerable<(MeshFilter filter, Renderer renderer)> Meshes(string target)
        {
            var parked = owner.BenchPresentation != null ? owner.BenchPresentation.ParkedVisual(target) : null;
            Transform source = parked != null && parked.gameObject.activeInHierarchy ? parked : owner.Flow.GetObject(target);
            if (source == null) yield break;
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<Renderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                yield return (filter, renderer);
            }
        }

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

        void Tag(Vector2 at, string text, Color color, int size, TextAnchor anchor, bool framed = false)
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
            }
            usedTags++;
            tag.rect.gameObject.SetActive(true);
            tag.text.text = text; tag.text.fontSize = size; tag.text.color = color; tag.text.alignment = TextAnchor.MiddleLeft;
            float width = tag.text.preferredWidth + 14, height = tag.text.preferredHeight + 6;
            tag.rect.pivot = anchor == TextAnchor.UpperLeft ? new Vector2(0, 1) : new Vector2(0, 0);
            var area = root.rect.size;
            float maxX = Mathf.Min(area.x, tagRight > 0 ? tagRight : area.x) - width - 4;
            tag.rect.anchoredPosition = new Vector2(Mathf.Clamp(at.x, 4, Mathf.Max(4, maxX)), Mathf.Clamp(at.y, 4, area.y - 4));
            tag.rect.sizeDelta = new Vector2(width, height);
            tag.bg.enabled = framed;
            RepairPropCyberStyle.SetFrame(tag.bg, color * new Color(1, 1, 1, .8f));
            var frameImage = tag.bg.transform.Find("Frame");
            if (frameImage != null) frameImage.gameObject.SetActive(framed);
        }

        void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }
    }
}
