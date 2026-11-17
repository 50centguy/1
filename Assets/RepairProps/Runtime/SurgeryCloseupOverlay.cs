using System.Collections.Generic;
using System.Linq;
using BorderRepair.Controls;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // The visor AR layer over the close-up surgery view, the same as the bench's (RepairPropArOverlay): wire boxes and
    // tags drawn onto the arm. It marks the parts that can be worked on now (dim corners), the whole arm and the phase,
    // the suggested next step with its gantry tool (pulsing, with a callout), the selection (amber wire and what can be
    // done to it), the part under the pointer (hot wire and its name), and the tool at work with its progress; a scan
    // line at the top and the guide band at the bottom. Read-only: it never selects or runs anything.
    // With an arm driven it also draws the tool tip's reticle (green when it is on its part, hot while working, with
    // what a press would do or how far off it is), the tolerance ring on the part it would work, the empty bolt holes,
    // the forearm's scan sections, and where a carried load has to go. The first second plays the link coming up.
    public sealed class SurgeryCloseupOverlay : MonoBehaviour
    {
        const float LineWidth = 1.6f, Bracket = .22f;
        SurgeryCloseupRuntime owner;
        Canvas canvas;
        RectTransform root, hintBand;
        Font font;
        readonly List<Image> lines = new List<Image>();
        readonly List<(RectTransform rect, Image bg, Text text)> tags = new List<(RectTransform, Image, Text)>();
        readonly Dictionary<string, (float first, float last)> locks = new Dictionary<string, (float, float)>();
        readonly Vector3[] box3 = new Vector3[8];
        readonly Vector2[] box2 = new Vector2[8];
        int usedLines, usedTags;
        float tagRight, bandTop;
        Text hint, controls, readout, visorId, clock;

        public string HoveredTarget { get; private set; }
        /// <summary>Top of the guide band (canvas units): the arm bar sits on it.</summary>
        public float BandTop => bandTop > 0 ? bandTop : 80;
        /// <summary>The line by the tool tip (what a press does, how far off, the work's progress).</summary>
        public string TipText { get; private set; }
        public string NextTarget { get; private set; }
        public string HintText => hint != null ? hint.text : "";
        public bool Showing => canvas != null && canvas.enabled;

        static SurgeryProcedureRuntime Op => SurgeryProcedureRuntime.Current;

        public void Initialize(SurgeryCloseupRuntime runtime)
        {
            owner = runtime;
            font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            var host = new GameObject("SurgeryArCanvas", typeof(RectTransform));
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
            readout = MakeText("Readout", 11, TextAnchor.UpperCenter, RepairPropCyberStyle.Dim);
            hint = MakeText("Hint", 14, TextAnchor.LowerCenter, RepairPropCyberStyle.Amber);
            controls = MakeText("Controls", 11, TextAnchor.LowerCenter, RepairPropCyberStyle.Dim);
            controls.text = "1–6 放下机械臂　·　按住左键 操作　·　右键 收臂 / 拖动平移　·　滚轮 缩放　·　L 灯　·　T 托盘　·　Esc 断开";
            visorId = MakeText("VisorId", 11, TextAnchor.UpperLeft, RepairPropCyberStyle.AmberFaint * new Color(1, 1, 1, 2.2f));
            if (RepairPropCyberStyle.Kana != null) visorId.font = RepairPropCyberStyle.Kana;
            visorId.text = "NL-07 // 神経リンク　手術台直結";
            clock = MakeText("Clock", 11, TextAnchor.LowerLeft, RepairPropCyberStyle.AmberDim);
            if (RepairPropCyberStyle.Mono != null) { clock.font = RepairPropCyberStyle.Mono; readout.font = RepairPropCyberStyle.Mono; }
            HudMotion.TypeOn(hint, 220);
            canvas.enabled = false;
        }

        void LateUpdate()
        {
            usedLines = usedTags = 0;
            bool show = owner != null && owner.Active && owner.View != null && Op != null;
            if (canvas.enabled != show) canvas.enabled = show;
            if (show) Draw();
            for (int i = usedLines; i < lines.Count; i++) if (lines[i].enabled) lines[i].enabled = false;
            for (int i = usedTags; i < tags.Count; i++) if (tags[i].rect.gameObject.activeSelf) tags[i].rect.gameObject.SetActive(false);
        }

        void Draw()
        {
            var camera = owner.View;
            float scale = canvas.scaleFactor > 0 ? canvas.scaleFactor : 1;
            var size = root.rect.size;
            float pulse = .6f + .4f * Mathf.Sin(Time.unscaledTime * 4.2f);
            var panel = owner.Panel != null && owner.Panel.Visible ? owner.Panel.PanelRectTransform : null;
            float right = size.x - (panel != null ? panel.rect.width + 16 : 0);
            tagRight = right - 6;
            VisorFrame(size, right);

            HoveredTarget = null;
            var pointer = RepairInput.PointerPosition;
            if (!RepairInput.PointerOverUI && (owner.Panel == null || !owner.Panel.ContainsScreenPoint(pointer))) HoveredTarget = owner.Pick(pointer);

            // Parts that can be worked on now: dim corner marks.
            var ready = new HashSet<string>(Op.Commands.Where(c => !c.table && c.target != SurgeryProcedureRuntime.Whole && Op.Why(c) == null).Select(c => c.target));
            foreach (var id in ready)
                if (id != owner.Selected && id != HoveredTarget && Box(id, 1.08f)) LockBox(camera, scale, RepairPropCyberStyle.HotDim, .22f, "act:" + id);

            // The whole arm: a thin frame with the patient and the phase.
            string phase = SurgeryProcedureRuntime.PhaseLabels[Op.Phase];
            if (Project(camera, SurgeryProcedureRuntime.Whole, scale, out var armRect))
            {
                if (Box(SurgeryProcedureRuntime.Whole, 1.04f)) LockBox(camera, scale, RepairPropCyberStyle.AmberDim, .12f, "arm");
                Tag(new Vector2(armRect.xMin - 10, armRect.yMax + 14), "森田的右臂  ·  " + phase, RepairPropCyberStyle.Amber, 12, TextAnchor.LowerLeft);
            }

            // The suggested next step: pulsing marks on its part, a callout with the step and the arm to use.
            var next = Op.Running ? null : Op.Next();
            NextTarget = next != null && !next.table ? next.target : null;
            // With its arm already out the tip's own tag says it; the callout stands aside.
            bool armOut = next != null && owner.Tool != null && owner.Tool == next.tool && owner.ArmReady;
            if (NextTarget != null && !armOut && Project(camera, NextTarget, scale, out var nextRect))
            {
                var color = RepairPropCyberStyle.Amber; color.a = pulse;
                bool whole = NextTarget == SurgeryProcedureRuntime.Whole;
                var frame = whole ? Grow(nextRect, 14) : Grow(nextRect, 6);
                if (Box(NextTarget, whole ? 1.10f : 1.12f)) LockBox(camera, scale, color, whole ? .14f : .3f, "next:" + NextTarget);
                string tool = next.tool == null || owner.Tool == next.tool ? "" : "　[" + SurgeryProcedureRuntime.ToolName(next.tool) + "]";
                var anchor = new Vector2(frame.xMax + 26, frame.yMax + 20);
                Leader(new Vector2(frame.xMax, frame.yMax), anchor + new Vector2(0, -6), color, "nextline:" + NextTarget);
                Tag(anchor, "下一步 ▶ " + next.label + tool, color, 13, TextAnchor.LowerLeft, true);
            }

            DrawArm(camera, scale);
            LinkIn(size);

            // The tool at work on its timer: its part in hot wire (the whole arm gets no box, it would fill the view)
            // and a progress tag off the point it works on. By hand, the tip's own tag says it.
            var running = Op.RunningCommand;
            if (running != null && !Op.Manual && (running.target == SurgeryProcedureRuntime.Whole || Wire(camera, running.target, scale, RepairPropCyberStyle.Hot, out _)))
            {
                var work = camera.WorldToScreenPoint(Op.TargetPoint(running.target));
                if (work.z > 0)
                {
                    var point = (Vector2)work / scale;
                    var anchor = point + new Vector2(46, -34);
                    Leader(point, anchor + new Vector2(0, 4), RepairPropCyberStyle.Hot, "workline:" + running.id);
                    // Only digits change while it works, so the tag does not retype itself (HudTypeOn ignores digit changes).
                    Tag(anchor, "▶ " + SurgeryProcedureRuntime.ToolName(running.tool) + "　" + running.label + "\n工作中 "
                        + Mathf.RoundToInt(Op.Progress * 100) + "%", RepairPropCyberStyle.Hot, 12, TextAnchor.UpperLeft, true);
                }
            }

            // The selection: amber wire and what can be done to it.
            string selected = owner.Selected;
            if (selected != SurgeryProcedureRuntime.Whole && (running == null || running.target != selected) && Wire(camera, selected, scale, RepairPropCyberStyle.Amber, out var selRect))
            {
                int count = Op.Commands.Count(c => !c.table && c.target == selected && Op.Why(c) == null);
                string state = selected == "SocketRing" ? "身体侧 · 保留" : count > 0 ? count + " 项可执行" : "暂无可执行操作";
                var anchor = new Vector2(Mathf.Max(selRect.xMin - 150, 8), selRect.yMin - 18);
                var from = new Vector2(selRect.xMin, selRect.yMin);
                if (anchor.y - 40 < bandTop) { anchor.y = selRect.yMax + 52; from.y = selRect.yMax; }   // never under the guide band
                Leader(from, anchor + new Vector2(140, -8), RepairPropCyberStyle.Amber, "selline:" + selected);
                Tag(anchor, "▣ " + SurgeryCyberArm.Label(selected) + "\n" + state, RepairPropCyberStyle.Amber, 12, TextAnchor.UpperLeft, true);
            }

            // The pointer's part: hot wire and its name at the cursor.
            if (HoveredTarget != null && HoveredTarget != selected && Wire(camera, HoveredTarget, scale, RepairPropCyberStyle.Hot, out _))
                Tag(pointer / scale + new Vector2(16, -10), SurgeryCyberArm.Label(HoveredTarget), RepairPropCyberStyle.Hot, 12, TextAnchor.UpperLeft);

            // Top-centre scan line: the two checks at a glance.
            readout.rectTransform.anchorMin = readout.rectTransform.anchorMax = Vector2.zero;
            readout.rectTransform.pivot = new Vector2(.5f, 1);
            readout.rectTransform.anchoredPosition = new Vector2(right / 2, size.y - 22);
            readout.rectTransform.sizeDelta = new Vector2(Mathf.Min(right - 80, 820), 40);
            int links = Op.LinkResults.Count;
            readout.text = "SCAN // 森田的右臂　义体扫描 [" + (Op.Get("scanned") == 1 ? "固件锁机" : "未扫描") + "]　神经链路联调 ["
                + (Op.Get("linked") == 1 ? "通过" : links > 0 ? links + "/" + SurgeryProcedureRuntime.LinkPasses : "未测") + "]";

            // Bottom guide: the suggested step, which arm to bring down for it, and how to work it once it is out.
            hint.text = "提示 // " + Guide(Op.Manual ? Op.RunningCommand : next);
            Place(hint.rectTransform, new Vector2(right / 2, 44), new Vector2(Mathf.Min(right - 60, 760), 60));
            float hintHeight = hint.preferredHeight;
            bandTop = 12 + hintHeight + 40;
            Place(hintBand, new Vector2(right / 2, 12), new Vector2(Mathf.Min(right - 40, Mathf.Max(hint.preferredWidth, controls.preferredWidth) + 60), hintHeight + 40));
            Place(controls.rectTransform, new Vector2(right / 2, 20), new Vector2(Mathf.Min(right - 60, 760), 20));
        }

        // One line for the guide band.
        string Guide(SurgeryProcedureRuntime.Command step)
        {
            if (owner.Carrying != null) return Coach(Op.RunningCommand);
            if (Op.Running && !Op.Manual) return Op.NextStepText;
            if (step == null) return Op.NextStepText;
            if (step.table)
                return "先" + step.label + (step.id == "lamp.on" ? "（按 L）" : step.id == "tray.out" ? "（按 T）" : step.id == "straps.close" ? "（Esc 断开链接，到房间里扣上束带）" : "");
            if (step.tool != null && owner.Tool != step.tool)
                return "下一步 " + step.label + "：按 " + (System.Array.IndexOf(SurgeryProcedureRuntime.ToolOrder, step.tool) + 1) + " 放下" + SurgeryProcedureRuntime.ToolName(step.tool);
            return "下一步 " + step.label + "：" + Coach(step);
        }

        static string Coach(SurgeryProcedureRuntime.Command c)
        {
            if (c == null) return "";
            switch (c.id)
            {
                case "scan": return "按住左键，扫描臂沿前臂从头扫到尾";
                case "cover.remove": return "夹爪移到检修盖板上，按住左键夹起，拖开后松开";
                case "battery.isolate": return "探针对准电池服务插头，按住左键拔下";
                case "neural.release": return "探针对准神经接头，按住左键拔下";
                case "neural.connect": return "探针对准神经接头，按住左键插上";
                case "test.link": return "探针按在神经接头上，按住左键联调（一次一轮）";
                case "tape.cut": return "切割臂贴着胶布，按住左键来回划";
                case "socket.clean": return "吸头放在接口触点上，按住左键来回移动";
                case "forearm.remove": return "夹爪按住旧前臂夹起，拖到托盘的空位上松开";
                case "forearm.fit": return "夹爪按住替换前臂夹起，对准接口环后松开";
            }
            if (c.id.StartsWith("remove.", System.StringComparison.Ordinal)) return "钻头对准" + SurgeryCyberArm.Label(c.target) + "，按住左键拧下";
            if (c.id.StartsWith("install.", System.StringComparison.Ordinal)) return "钻头对准空螺孔，按住左键拧紧" + SurgeryCyberArm.Label(c.target);
            return c.label;
        }

        // The driven arm: its tip reticle and tag, the ring on what it would work, the empty holes, the scan sections,
        // where a carried load goes.
        void DrawArm(Camera camera, float scale)
        {
            TipText = null;
            if (owner.Tool == null) return;
            var tipScreen = camera.WorldToScreenPoint(owner.Tip);
            if (tipScreen.z <= 0) return;
            var tip = (Vector2)tipScreen / scale;
            bool carrying = owner.Carrying != null;
            Color color = !owner.ArmReady ? RepairPropCyberStyle.AmberDim : owner.Working && !carrying ? RepairPropCyberStyle.Hot
                : owner.Aligned ? RepairPropCyberStyle.Ok : RepairPropCyberStyle.Amber;

            if (owner.Tool == "Drill")
                foreach (var c in Op.Commands.Where(c => c.tool == "Drill" && c.id.StartsWith("install.", System.StringComparison.Ordinal) && Op.WhyIfIdle(c) == null))
                    Ring(camera, scale, owner.HolePoint(c.target), SurgeryCloseupRuntime.ScrewTolerance, RepairPropCyberStyle.AmberDim);
            if (owner.Tool == "Scanner" && Op.Get("scanned") == 0 && owner.ScanSpan(out var from, out var to))
                for (int i = 0; i < SurgeryCloseupRuntime.ScanBins; i++)
                {
                    var a = camera.WorldToScreenPoint(Vector3.Lerp(from, to, (i + .1f) / SurgeryCloseupRuntime.ScanBins));
                    var b = camera.WorldToScreenPoint(Vector3.Lerp(from, to, (i + .9f) / SurgeryCloseupRuntime.ScanBins));
                    if (a.z > 0 && b.z > 0) Line((Vector2)a / scale, (Vector2)b / scale, IsScanned(i) ? RepairPropCyberStyle.Ok : RepairPropCyberStyle.AmberDim);
                }

            if (carrying && owner.DropPoint.HasValue)
            {
                var place = camera.WorldToScreenPoint(owner.DropPoint.Value);
                var load = camera.WorldToScreenPoint(owner.Handle);
                if (place.z > 0 && load.z > 0)
                {
                    Ring(camera, scale, owner.DropPoint.Value, owner.AimTolerance, owner.Aligned ? RepairPropCyberStyle.Ok : RepairPropCyberStyle.Amber);
                    Line((Vector2)load / scale, (Vector2)place / scale, owner.Aligned ? RepairPropCyberStyle.Ok : RepairPropCyberStyle.AmberDim);
                }
            }
            else if (!carrying && owner.AimCommand != null && owner.Tool != "Scanner" && owner.Tool != "Gripper")
            {
                Ring(camera, scale, owner.AimPoint, owner.AimTolerance, owner.Aligned ? RepairPropCyberStyle.Ok : RepairPropCyberStyle.Amber);
                var aim = camera.WorldToScreenPoint(owner.AimPoint);
                if (aim.z > 0 && !owner.Aligned) Line(tip, (Vector2)aim / scale, RepairPropCyberStyle.AmberDim);
            }

            // The reticle: a small square with ticks out to the sides.
            float r = owner.Working ? 7 : 9;
            Brackets(Rect.MinMaxRect(tip.x - r, tip.y - r, tip.x + r, tip.y + r), color, 4);
            Line(tip + new Vector2(-r - 7, 0), tip + new Vector2(-r - 2, 0), color);
            Line(tip + new Vector2(r + 2, 0), tip + new Vector2(r + 7, 0), color);
            Line(tip + new Vector2(0, -r - 7), tip + new Vector2(0, -r - 2), color);
            Line(tip + new Vector2(0, r + 2), tip + new Vector2(0, r + 7), color);

            string name = SurgeryProcedureRuntime.ToolName(owner.Tool);
            if (!owner.ArmReady) TipText = name + "　就位中…";
            else if (carrying) TipText = name + "　夹着" + SurgeryCyberArm.Label(owner.Selected) + (owner.Aligned ? "\n松开左键 ▶ 放下" : owner.DropPoint.HasValue ? "\n偏 " + Mathf.RoundToInt(owner.Offset * 1000) + " 毫米" : "");
            else if (owner.Working) TipText = name + "　" + (Op.RunningCommand != null ? Op.RunningCommand.label : "") + "\n" + Mathf.RoundToInt(Op.Progress * 100) + "%"
                + (owner.Tool == "Scanner" ? "　扫过 " + owner.ScannedSections + "/" + SurgeryCloseupRuntime.ScanBins + " 段" : "");
            else if (owner.AimCommand == null) TipText = name + "　这里没有能做的";
            else if (owner.Aligned) TipText = name + "　▶ 按住左键：" + owner.AimCommand.label + (Op.Manual && Op.RunningCommand == owner.AimCommand ? "（" + Mathf.RoundToInt(Op.Progress * 100) + "%）" : "");
            else TipText = name + "　" + owner.AimCommand.label + "\n偏 " + Mathf.Max(1, Mathf.RoundToInt((owner.Offset - owner.AimTolerance) * 1000)) + " 毫米";
            Tag(tip + new Vector2(18, -12), TipText, color, 12, TextAnchor.UpperLeft, true);
        }

        bool IsScanned(int section) => owner.SectionScanned(section);

        // The link coming up: cyan scan lines sweeping down and the sync count, fading out after a second.
        void LinkIn(Vector2 size)
        {
            float age = owner.LinkAge;
            if (age > 1.3f) return;
            float fade = 1 - Mathf.Clamp01((age - .8f) / .5f);
            var cyan = new Color(.3f, 1f, .9f, .5f * fade);
            for (int i = 0; i < 9; i++)
            {
                float y = Mathf.Repeat(size.y * (1 - age * 1.3f) + i * size.y / 9f, size.y);
                Line(new Vector2(0, y), new Vector2(size.x, y), i == 0 ? cyan : cyan * new Color(1, 1, 1, .35f));
            }
            Tag(new Vector2(size.x / 2 - 160, size.y / 2 + 30), "神经链路接入 // 手术台\n同步 " + Mathf.RoundToInt(Mathf.Clamp01(age / .8f) * 100) + "%",
                new Color(.3f, 1f, .9f, fade), 16, TextAnchor.UpperLeft, true);
        }

        // A ring of a given world radius on the horizontal plane around a point.
        void Ring(Camera camera, float scale, Vector3 centre, float radius, Color color)
        {
            Vector2 previous = default;
            for (int i = 0; i <= 20; i++)
            {
                float a = i / 20f * Mathf.PI * 2;
                var p = camera.WorldToScreenPoint(centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Mathf.Max(radius, .002f));
                if (p.z <= 0) return;
                var s = (Vector2)p / scale;
                if (i > 0) Line(previous, s, color);
                previous = s;
            }
        }

        void VisorFrame(Vector2 size, float right)
        {
            Brackets(Rect.MinMaxRect(12, 12, right - 4, size.y - 12), RepairPropCyberStyle.AmberFaint, 40);
            Line(new Vector2(right / 2 - 60, size.y - 16), new Vector2(right / 2 + 60, size.y - 16), RepairPropCyberStyle.AmberDim);
            // Heading ruler under the scan line: ticks slide with the view's yaw.
            float yaw = owner.View.transform.eulerAngles.y, shift = Mathf.Repeat(yaw * 3f, 12f);
            for (float x = -120 + shift; x <= 120; x += 12)
            {
                float fade = 1 - Mathf.Abs(x) / 130f, h = Mathf.Repeat(x - shift + 120, 48f) < 1 ? 7 : 3;
                Line(new Vector2(right / 2 + x, size.y - 62), new Vector2(right / 2 + x, size.y - 62 + h), new Color(1f, .62f, .16f, .30f * fade));
            }
            Pin(visorId.rectTransform, new Vector2(22, 40), Vector2.zero, new Vector2(360, 16));
            var run = BorderRepair.TwoNight.TwoNightRun.Current;
            clock.text = "N" + (run != null ? Mathf.Max(1, run.night) : 1) + " // T+" + RepairUIView.FormatTime(owner.OpenSeconds) + "   OP 森田   REC";
            clock.color = new Color(1f, .62f, .16f, Mathf.Repeat(Time.unscaledTime, 1.6f) < 1.1f ? .45f : .25f);
            Pin(clock.rectTransform, new Vector2(22, 22), Vector2.zero, new Vector2(320, 16));
        }

        // ------------------------------------------------------------------------------------------------ marks
        IEnumerable<MeshFilter> Meshes(string id)
        {
            var source = Op.TargetTransform(id);
            if (source == null) yield break;
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<Renderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                yield return filter;
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

        // The oriented box of a target's meshes in the target's own frame.
        bool Box(string id, float grow)
        {
            var frame = Op.TargetTransform(id);
            if (frame == null) return false;
            bool any = false; Vector3 min = default, max = default;
            foreach (var filter in Meshes(id))
            {
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

        // Corner marks on the oriented box; locking on, the box starts half again larger and closes in, fading up.
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

        bool Project(Camera camera, string id, float scale, out Rect rect)
        {
            bool any = false; Vector2 min = default, max = default;
            foreach (var filter in Meshes(id))
            {
                var b = filter.GetComponent<Renderer>().bounds;
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

        // One oriented wire box per target (in its own frame), so a part reads as one shape.
        bool Wire(Camera camera, string id, float scale, Color color, out Rect rect)
        {
            rect = default;
            if (!Box(id, 1.06f)) return false;
            Vector2 min = default, max = default;
            for (int i = 0; i < 8; i++)
            {
                var p = camera.WorldToScreenPoint(box3[i]);
                if (p.z <= 0) return false;
                box2[i] = (Vector2)p / scale;
                if (i == 0) { min = max = box2[i]; } else { min = Vector2.Min(min, box2[i]); max = Vector2.Max(max, box2[i]); }
            }
            for (int i = 0; i < 8; i++)
                for (int axis = 1; axis <= 4; axis <<= 1)
                    if ((i & axis) == 0) Line(box2[i], box2[i | axis], color);
            rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        // A leader line that draws out from its start over its first fifth of a second.
        void Leader(Vector2 from, Vector2 to, Color color, string key)
        {
            float t = HudMotion.EaseOut(Age(key) / .2f);
            Line(from, Vector2.Lerp(from, to, Mathf.Max(.02f, t)), color);
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

        // ------------------------------------------------------------------------------------------------ drawing
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

        static void Place(RectTransform rect, Vector2 bottomCentre, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = bottomCentre; rect.sizeDelta = size;
        }

        static void Pin(RectTransform rect, Vector2 position, Vector2 pivot, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = pivot;
            rect.anchoredPosition = position; rect.sizeDelta = size;
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
