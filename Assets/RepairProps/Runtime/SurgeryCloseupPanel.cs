using System.Collections.Generic;
using System.Linq;
using BorderRepair.Core;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    public enum SurgeryWorkspaceTab { Operate, Chart, Records }

    // The close-up surgery workspace: the same right-hand column of windows as the bench repair workspace
    // (RepairPropContextPanel), on one overlay canvas, plus the leave button where the bench has its own. The column
    // only informs (the work is done with the arms in the view): case (patient, phase chips, the next step, the two
    // checks, the arm in use and the table), steps / chart / records pages, log. Under the view, the arm bar picks the
    // gantry arm to drive (0 visor, 1-6 the arms) and switches the lamp (L) and the tray (T).
    // It reads SurgeryProcedureRuntime and dispatches only through SurgeryCloseupRuntime; the flow decides every command.
    public sealed class SurgeryCloseupPanel : MonoBehaviour
    {
        static readonly Color PanelBg = RepairPropCyberStyle.Field, Rule = RepairPropCyberStyle.AmberDim, ChipBg = new Color(1f, .62f, .16f, .07f);
        static readonly Color Ink = RepairPropCyberStyle.Ink, Dim = RepairPropCyberStyle.Dim, Accent = RepairPropCyberStyle.Amber;
        static readonly Color Ok = RepairPropCyberStyle.Ok, Warn = RepairPropCyberStyle.Warn, Bad = RepairPropCyberStyle.Bad, Info = RepairPropCyberStyle.Hot;
        static readonly Color ButtonBg = new Color(.02f, .03f, .035f, .55f), ReadyBg = new Color(1f, .62f, .16f, .09f), IdleBg = new Color(0, 0, 0, .30f), DoneBg = new Color(.84f, .93f, .52f, .20f);
        SurgeryCloseupRuntime owner;
        Canvas canvas;
        Font font;
        Text title, subtitle, statusTitle, statusBody, armLine, tableLine, feedback, caseCode, scanLine, linkLine;
        RectTransform content;
        readonly List<(Image bg, Text text)> phaseChips = new List<(Image, Text)>();
        readonly Dictionary<string, Button> arms = new Dictionary<string, Button>();
        readonly Dictionary<SurgeryWorkspaceTab, Button> tabs = new Dictionary<SurgeryWorkspaceTab, Button>();
        readonly List<string> steps = new List<string>();
        Button visor, lamp, tray, leave;
        RectTransform bar;
        SurgeryWorkspaceTab tab, builtTab;
        string builtSignature;
        bool dirty = true, wasVisible;
        const string VisorKey = "Visor";

        public RectTransform PanelRectTransform { get; private set; }
        public ScrollRect Scroll { get; private set; }
        public Button LeaveButton => leave;
        public bool Visible => PanelRectTransform != null && PanelRectTransform.gameObject.activeInHierarchy && canvas.enabled;
        /// <summary>The arm bar's slot for a gantry arm (null: the visor).</summary>
        public Button ArmButton(string tool) => arms.TryGetValue(tool ?? VisorKey, out var button) ? button : null;
        public Button LampButton => lamp;
        public Button TrayButton => tray;
        public RectTransform ArmBar => bar;
        public Button TabButton(SurgeryWorkspaceTab value) => tabs.TryGetValue(value, out var button) ? button : null;
        public SurgeryWorkspaceTab Tab => tab;
        public string StatusText => statusBody != null ? statusBody.text : "";
        public string ArmText => armLine != null ? armLine.text : "";
        public string FeedbackText => feedback != null ? feedback.text : "";
        /// <summary>The rows of the steps page as shown ("✓ …", "▶ …", "· …").</summary>
        public IReadOnlyList<string> StepRows => steps;
        public bool ContainsScreenPoint(Vector2 point) => Visible && (RectTransformUtility.RectangleContainsScreenPoint(PanelRectTransform, point, null)
            || bar != null && bar.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(bar, point, null));

        static SurgeryProcedureRuntime Op => SurgeryProcedureRuntime.Current;

        public void Initialize(SurgeryCloseupRuntime runtime)
        {
            owner = runtime;
            font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            var host = Rect("SurgeryCommandsCanvas", transform);
            canvas = host.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 55;
            var scaler = host.gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            host.gameObject.AddComponent<GraphicRaycaster>();

            // Leave: where the bench workspace has "离开维修台".
            var leaveRect = Rect("离开手术台", host);
            leaveRect.anchorMin = leaveRect.anchorMax = leaveRect.pivot = new Vector2(0, 1);
            leaveRect.anchoredPosition = new Vector2(16, -16); leaveRect.sizeDelta = new Vector2(170, 44);
            var leaveImage = leaveRect.gameObject.AddComponent<Image>();
            RepairPropCyberStyle.Framed(leaveImage, new Color(.10f, .07f, .03f, .62f), Rule);
            leave = leaveRect.gameObject.AddComponent<Button>(); leave.targetGraphic = leaveImage;
            leave.navigation = new Navigation { mode = Navigation.Mode.None };
            leave.onClick.AddListener(() => { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); owner.Close(); });
            var leaveText = Label(leaveRect, "离开手术台", 18, 44, Ink);
            var leaveTextRect = (RectTransform)leaveText.transform; leaveTextRect.anchorMin = Vector2.zero; leaveTextRect.anchorMax = Vector2.one;
            leaveTextRect.offsetMin = new Vector2(16, 0); leaveTextRect.offsetMax = new Vector2(-8, 0);

            PanelRectTransform = Rect("SurgeryCommandsPanel", host);
            PanelRectTransform.anchorMin = PanelRectTransform.anchorMax = new Vector2(1, 1);
            PanelRectTransform.pivot = new Vector2(1, 1); PanelRectTransform.anchoredPosition = new Vector2(-8, -42);
            var backing = PanelRectTransform.gameObject.AddComponent<Image>(); backing.color = new Color(0, 0, 0, 0);
            Column(PanelRectTransform, 0, 4);

            // 1. Case: who is on the table, where the operation stands, what the two checks say.
            var caseWindow = Window("CaseWindow", "手术 // SURGERY", false, 0f, out var caseBar);
            caseCode = RepairPropCyberStyle.Caption(caseBar.transform.parent, "", RepairPropCyberStyle.Mono != null ? RepairPropCyberStyle.Mono : font, 10, RepairPropCyberStyle.AmberDim, TextAnchor.MiddleRight);
            var codeRect = caseCode.rectTransform; codeRect.anchorMin = Vector2.zero; codeRect.anchorMax = Vector2.one; codeRect.offsetMin = new Vector2(110, 0); codeRect.offsetMax = new Vector2(-34, 0);
            title = Label(caseWindow, "手术 · 森田的右前臂", 16, 22, Accent);             subtitle = Paragraph(caseWindow, 12, Dim);
            var phaseRow = Row(caseWindow, 22, 2);
            foreach (var label in SurgeryProcedureRuntime.PhaseLabels)
            {
                var chip = Rect("阶段_" + label, phaseRow); Size(chip, 22);
                var bg = chip.gameObject.AddComponent<Image>(); bg.raycastTarget = false;
                var text = Label(chip, label, 12, 22); text.alignment = TextAnchor.MiddleCenter; Stretch((RectTransform)text.transform, 1, 0);
                phaseChips.Add((bg, text));
            }
            var statusCard = Rect("StatusCard", caseWindow);
            RepairPropCyberStyle.Framed(statusCard.gameObject.AddComponent<Image>(), new Color(0, 0, 0, 0), Rule, true);
            Column(statusCard, 6, 2);
            statusTitle = Label(statusCard, "", 13, 18, Accent);             statusBody = Paragraph(statusCard, 13, Ink); HudMotion.TypeOn(statusBody, 260);
            scanLine = Paragraph(statusCard, 12, Dim);
            linkLine = Paragraph(statusCard, 12, Dim);
            armLine = Paragraph(statusCard, 12, Info);
            tableLine = Paragraph(statusCard, 12, Dim);

            // 2. The arm bar under the view: the visor, the six arms (number keys), the lamp and the tray.
            bar = Rect("ArmBar", host);
            bar.anchorMin = bar.anchorMax = Vector2.zero; bar.pivot = new Vector2(.5f, 0);
            var barRow = bar.gameObject.AddComponent<HorizontalLayoutGroup>(); barRow.spacing = 4;
            barRow.childControlWidth = barRow.childControlHeight = true; barRow.childForceExpandWidth = true; barRow.childForceExpandHeight = true;
            visor = Button(bar, "0  检查", () => owner.SelectTool(null), 0, 34, 13); visor.name = "臂_检查"; arms[VisorKey] = visor;
            for (int i = 0; i < SurgeryProcedureRuntime.ToolOrder.Length; i++)
            {
                string tool = SurgeryProcedureRuntime.ToolOrder[i];
                var button = Button(bar, (i + 1) + "  " + SurgeryProcedureRuntime.ToolName(tool), () => owner.SelectTool(tool), 0, 34, 13);
                button.name = "臂_" + tool;
                arms[tool] = button;
            }
            lamp = Button(bar, "L  手术灯", () => owner.ToggleLamp(), 0, 34, 13); lamp.name = "台_手术灯";
            tray = Button(bar, "T  托盘", () => owner.ToggleTray(), 0, 34, 13); tray.name = "台_托盘";
            HudMotion.Boot(bar.gameObject, .1f);

            // 3. Actions: operate / chart / records.
            var actionWindow = Window("ActionWindow", "操作 // ACTIONS", true, .16f, out _, 0);
            var tabRow = Row(actionWindow, 26, 2);
            foreach (var value in new[] { SurgeryWorkspaceTab.Operate, SurgeryWorkspaceTab.Chart, SurgeryWorkspaceTab.Records })
            {
                var page = value;
                string label = page == SurgeryWorkspaceTab.Operate ? "步骤" : page == SurgeryWorkspaceTab.Chart ? "病历" : "记录";
                var button = Button(tabRow, label, () => ShowTab(page), 0, 26, 13); button.name = "页签_" + label; tabs.Add(page, button);
            }
            Scroll = MakeScroll(actionWindow, out content);

            // 4. Log: the last message from the table.
            var logWindow = Window("LogWindow", "日志 // LOG", false, .24f, out _, 2);
            feedback = Paragraph(logWindow, 13, Info); feedback.GetComponent<LayoutElement>().minHeight = 18; HudMotion.TypeOn(feedback, 200);

            owner.Changed += OnChanged;
            if (Op != null) { Op.Changed += OnChanged; Op.Feedback += OnFeedback; }
            PanelRectTransform.gameObject.SetActive(false); leave.gameObject.SetActive(false); bar.gameObject.SetActive(false);
            Refresh();
        }

        void OnChanged() => dirty = true;
        void Update() => Refresh();
        public void ShowTab(SurgeryWorkspaceTab value) { if (tab != value) { tab = value; dirty = true; } }

        public void Refresh()
        {
            if (owner == null || PanelRectTransform == null) return;
            bool visible = owner.Active && Op != null;
            // Shown by activating the column (each window replays its boot on enable), as the bench workspace does.
            if (PanelRectTransform.gameObject.activeSelf != visible) { PanelRectTransform.gameObject.SetActive(visible); leave.gameObject.SetActive(visible); bar.gameObject.SetActive(visible); }
            if (!visible) { wasVisible = false; return; }
            if (!wasVisible) { wasVisible = true; dirty = true; feedback.text = Op.LastMessage ?? "已接入手术台：按数字键放下机械臂，把臂尖移到部件上，按住左键操作。"; }
            var area = (RectTransform)canvas.transform;
            float width = Mathf.Clamp(area.rect.width * .28f, 316, 360);
            PanelRectTransform.sizeDelta = new Vector2(Mathf.Min(width, Mathf.Max(240, area.rect.width - 16)), Mathf.Max(320, area.rect.height - 50));
            RefreshHeader();
            foreach (var pair in tabs) Paint(pair.Value, pair.Key == tab ? Rule : IdleBg, pair.Key == tab ? Ink : Dim);
            RefreshBar(area);
            // Rebuild when the page or what is on the table changed.
            string signature = Signature();
            if (dirty || builtTab != tab || signature != builtSignature) Rebuild(signature);
        }

        // The arm bar sits under the view, centred in the free space left of the column, over the guide band.
        void RefreshBar(RectTransform area)
        {
            float right = area.rect.width - PanelRectTransform.rect.width - 16;
            float width = Mathf.Min(right - 40, 760);
            bar.sizeDelta = new Vector2(width, 34);
            float above = owner.Overlay != null ? owner.Overlay.BandTop : 80;
            bar.anchoredPosition = new Vector2(right / 2, above + 24);   // clear of the band's title tab
            var next = Op.Running ? null : Op.Next();
            float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 5f);
            foreach (var pair in arms)
            {
                string tool = pair.Key == VisorKey ? null : pair.Key;
                bool on = owner.Tool == tool;
                Paint(pair.Value, on ? Accent : ButtonBg, on ? Color.black : Ink);
                // The arm the next step wants blinks its frame until it is out.
                if (!on && tool != null && next != null && next.tool == tool)
                    RepairPropCyberStyle.SetFrame(pair.Value, Color.Lerp(Rule, RepairPropCyberStyle.Hot, pulse));
            }
            bool lampOn = Op.Get("lamp") == 1, trayOut = Op.Get("tray") == 1;
            Paint(lamp, lampOn ? Rule : ButtonBg, lampOn ? Ok : Dim);
            Paint(tray, trayOut ? Rule : ButtonBg, trayOut ? Ok : Dim);
            if (next != null && next.id == "lamp.on") RepairPropCyberStyle.SetFrame(lamp, Color.Lerp(Rule, RepairPropCyberStyle.Hot, pulse));
            if (next != null && next.id == "tray.out") RepairPropCyberStyle.SetFrame(tray, Color.Lerp(Rule, RepairPropCyberStyle.Hot, pulse));
        }

        void RefreshHeader()
        {
            int bars = 3 + Mathf.FloorToInt(HudMotion.Noise(Mathf.Floor(Time.unscaledTime * 2.5f)) * 2.99f);
            caseCode.text = "OP-" + RepairPropCyberStyle.Code("surgery-luo-right-forearm", 4) + " " + new string('|', bars) + new string('.', 5 - bars);
            var run = TwoNightRun.Current;
            subtitle.text = "森田 · 3号吊机 装卸工　手术计时 " + RepairUIView.FormatTime(owner.OpenSeconds) + (run != null ? "　现金 " + TwoNightUi.Money(run.Cash) : "");
            int phase = Op.Phase;
            for (int i = 0; i < phaseChips.Count; i++)
            {
                bool current = i == phase, done = i < phase || Op.Complete;
                phaseChips[i].bg.color = current ? Accent : new Color(0, 0, 0, 0);
                phaseChips[i].text.color = current ? Color.black : done ? Ok : Dim;
            }
            statusTitle.text = "当前阶段：" + SurgeryProcedureRuntime.PhaseLabels[phase];
            statusBody.text = Op.NextStepText;
            bool scanned = Op.Get("scanned") == 1;
            scanLine.text = "义体扫描：" + (scanned ? "固件锁机；神经接口完好" : "未扫描");
            scanLine.color = scanned ? Warn : Dim;
            int links = Op.LinkResults.Count;
            linkLine.text = "神经链路联调：" + (Op.Get("linked") == 1 ? "通过（" + SurgeryProcedureRuntime.LinkPasses + "/" + SurgeryProcedureRuntime.LinkPasses + "）"
                : links > 0 ? links + "/" + SurgeryProcedureRuntime.LinkPasses : "未测");
            linkLine.color = Op.Get("linked") == 1 ? Ok : links > 0 ? Info : Dim;
            string arm = owner.Tool == null ? "机械臂：收起（指针检查部件）"
                : "机械臂：" + SurgeryProcedureRuntime.ToolName(owner.Tool) + (owner.Carrying != null ? " · 夹着" + SurgeryCyberArm.Label(owner.Selected)
                    : !owner.ArmReady ? " · 就位中" : owner.Working ? " · 工作中 " + Mathf.RoundToInt(Op.Progress * 100) + "%" : owner.Aligned ? " · 已对准" : " · 跟随指针");
            armLine.text = arm;
            armLine.color = owner.Working ? Info : owner.Aligned ? Ok : Dim;
            int straps = Table != null ? Table.StrapsClosed : 0, strapCount = Table != null ? Table.StrapCount : 0;
            tableLine.text = "手术台：灯" + (Op.Get("lamp") == 1 ? "开" : "关") + " · 托盘" + (Op.Get("tray") == 1 ? "摆出" : "收起") + " · 束带 " + straps + "/" + strapCount;
        }

        static SurgeryTableV2Runtime Table => SurgeryTableV2Runtime.Current;

        void OnFeedback(string message, FeedbackKind kind)
        {
            if (feedback == null) return;
            feedback.text = message ?? "";
            feedback.color = kind == FeedbackKind.Success ? Ok : kind == FeedbackKind.Warning ? Warn : kind == FeedbackKind.Error ? Bad : Info;
            float w = ((RectTransform)feedback.transform).rect.width;
            int lines = 0;
            foreach (var line in feedback.text.Split('\n')) lines += Mathf.Max(1, Mathf.CeilToInt(line.Length * feedback.fontSize / Mathf.Max(1f, w - 4)));
            feedback.GetComponent<LayoutElement>().minHeight = Mathf.Clamp(lines * feedback.fontSize * 1.35f + 4, 18, 84);
        }

        // What decides the page's layout: which steps are done, what is running, what came off, the records.
        string Signature()
        {
            var done = new System.Text.StringBuilder();
            foreach (var c in Op.Commands) done.Append(c.table ? "" : Finished(c) ? "1" : "0");
            var bin = string.Join(",", Bin().Select(b => b.id));
            var next = Op.Next();
            return done + "|" + bin + "|" + (Op.RunningCommand?.id ?? "-") + "|" + (next?.id ?? "-") + "|" + Op.Inspected.Count + "|" + Op.LinkResults.Count + "|" + Op.Get("scanned");
        }

        void Rebuild(string signature)
        {
            bool changedPage = builtTab != tab;
            builtTab = tab; builtSignature = signature; dirty = false;
            foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            steps.Clear();
            if (tab == SurgeryWorkspaceTab.Chart) BuildChart();
            else if (tab == SurgeryWorkspaceTab.Records) BuildRecords();
            else BuildSteps();
            if (changedPage) { Scroll.StopMovement(); Scroll.verticalNormalizedPosition = 1; }
        }

        // The parts bin and the tray: what has come off (and is waiting to go back) and the two forearms.
        IEnumerable<(string id, string label, string tag)> Bin()
        {
            foreach (var s in SurgeryCyberArm.CoverScrews) if (Op.Get("out." + s) == 1) yield return (s, SurgeryCyberArm.Label(s), "零件栏");
            if (Op.Get("cover") == 1) yield return ("ServiceCover", "检修盖板", "零件栏");
            if (Op.Get("tape") == 1) yield return ("ClampTape", "胶布（写着字）", "零件栏");
            foreach (var b in SurgeryCyberArm.Bolts) if (Op.Get("out." + b) == 1) yield return (b, SurgeryCyberArm.Label(b), "零件栏 · 待装回");
            if (Op.OldArm != null && Op.Get("arm") != 1) yield return (SurgeryCyberArm.Old, "旧前臂", "托盘 · 锁死的旧重手");
            if (Op.NewArm != null && Op.Get("arm") != 2) yield return (SurgeryCyberArm.New, "替换前臂", "托盘 · 待装");
        }

        // The operation as a read-only checklist: screw and bolt sets on one row each with their count.
        void BuildSteps()
        {
            var next = Op.Running ? Op.RunningCommand : Op.Next();
            Section(content, "手术步骤");
            var seen = new HashSet<string>();
            foreach (var c in Op.Commands)
            {
                if (c.table || c.id == "complete" && !Op.IsDone(c) && next != c) continue;
                string family = Family(c.id);
                if (family != null && !seen.Add(family)) continue;
                var members = family != null ? Op.Commands.Where(m => Family(m.id) == family).ToList() : new List<SurgeryProcedureRuntime.Command> { c };
                int doneCount = members.Count(Finished);
                bool done = doneCount == members.Count, current = next != null && members.Contains(next);
                string label = family != null ? FamilyLabel(family) + "　" + doneCount + "/" + members.Count
                    : c.id == "test.link" ? c.label + "　" + Op.LinkResults.Count + "/" + SurgeryProcedureRuntime.LinkPasses : c.label;
                string row = (done ? "✓ " : current ? "▶ " : "· ") + label;
                steps.Add(row);
                var line = Paragraph(content, 13, done ? Ok * new Color(1, 1, 1, .7f) : current ? Accent : Dim);
                line.text = row + (c.tool != null ? "　[" + SurgeryProcedureRuntime.ToolName(c.tool) + "]" : "");
            }
            if (next != null && next.table)
            {
                Section(content, "手术台");
                Paragraph(content, 13, Warn).text = "先" + next.label + (next.id == "lamp.on" ? "（L）" : next.id == "tray.out" ? "（T）" : next.id == "straps.close" ? "（断开链接，在房间里扣上）" : "");
            }
            Section(content, "零件栏 · 托盘");
            var bin = Bin().ToList();
            if (bin.Count == 0) Paragraph(content, 12, Dim).text = "还没有拆下的部件。";
            foreach (var entry in bin) Paragraph(content, 12, Ink).text = entry.label + "　—　" + entry.tag;
        }

        // Ticked once carried out (a plug pulled and later put back stays ticked for the pull).
        static bool Finished(SurgeryProcedureRuntime.Command c) => Op.Did(c.id);

        static string Family(string id)
        {
            if (id.StartsWith("remove.CoverScrew")) return "remove.CoverScrew";
            if (id.StartsWith("remove.ClampBolt")) return "remove.ClampBolt";
            if (id.StartsWith("install.ClampBolt")) return "install.ClampBolt";
            return null;
        }

        static string FamilyLabel(string family) => family == "remove.CoverScrew" ? "卸下检修盖螺钉" : family == "remove.ClampBolt" ? "卸下卡箍螺栓" : "拧紧卡箍螺栓";

        void BuildChart()
        {
            Section(content, "病人");
            Paragraph(content, 14, Ink).text = "森田 健二　3号吊机 · 老装卸工";
            Section(content, "诉求");
            Paragraph(content, 14, Ink).text = "“右手动不了了。合同到期那天夜里，它自己就锁上了。”";
            Section(content, "义体来历");
            Paragraph(content, 13, Ink).text = "老厂的重型吊机义手，用了十一年。厂家撤走、售后停服以后，老型号固件说锁就锁，手指僵在半握的位置。";
            Section(content, "手术备注");
            Paragraph(content, 13, Info).text = "保留身体侧的接口环和神经接头，只更换前臂。旧前臂留在托盘上，不读取、不复制里面的作业记录。";
            Section(content, "费用");
            Paragraph(content, 13, Dim).text = "待定：手术还没有接入营业结算。";
        }

        void BuildRecords()
        {
            Section(content, "联调记录");
            if (Op.LinkResults.Count == 0) Paragraph(content, 12, Dim).text = "尚未联调。";
            for (int i = Op.LinkResults.Count - 1; i >= 0; i--) Paragraph(content, 13, i + 1 >= SurgeryProcedureRuntime.LinkPasses ? Ok : Info).text = Op.LinkResults[i];
            Section(content, "扫描");
            Paragraph(content, 12, Op.Get("scanned") == 1 ? Warn : Dim).text = Op.Get("scanned") == 1 ? Op.Find("scan").done : "尚未扫描。";
            Section(content, "检查记录");
            if (Op.Inspected.Count == 0) Paragraph(content, 12, Dim).text = "尚未检查部件。收起机械臂（0 或右键）后，左键点部件就能检查。";
            foreach (var id in Op.Inspected) Paragraph(content, 12, Ink).text = SurgeryCyberArm.Label(id) + "：" + Op.Finding(id);
        }

        // ------------------------------------------------------------------------------------------------ widgets
        // The same widgets as the bench workspace (RepairPropContextPanel).
        static void Paint(Button button, Color bg, Color ink)
        {
            if (button == null) return;
            bool selected = bg == Accent || bg == Rule;
            button.targetGraphic.color = selected ? new Color(1f, .62f, .16f, .13f) : ButtonBg;
            RepairPropCyberStyle.SetFrame(button, selected ? Accent : Rule);
            var text = button.GetComponentInChildren<Text>(); if (text != null) text.color = selected ? Accent : ink == Color.black ? Ink : ink;
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

        // Section header: an inverse tab on a rule.
        void Section(Transform parent, string value)
        {
            var row = Rect("Section", parent); Size(row, 22);
            var tabRect = Rect("Tab", row); tabRect.anchorMin = Vector2.zero; tabRect.anchorMax = new Vector2(0, 1); tabRect.pivot = new Vector2(0, .5f);
            var tabImage = tabRect.gameObject.AddComponent<Image>(); tabImage.color = new Color(1f, .62f, .16f, .22f); tabImage.raycastTarget = false;
            var text = Label(row, value, 13, 22, Accent); text.alignment = TextAnchor.MiddleLeft;
            var textRect = (RectTransform)text.transform; textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6, 0); textRect.offsetMax = Vector2.zero;
            tabRect.sizeDelta = new Vector2(Mathf.Min(text.preferredWidth + 14, 280), 0);
            var rule = Rect("分隔", row); rule.anchorMin = Vector2.zero; rule.anchorMax = new Vector2(1, 0); rule.pivot = new Vector2(.5f, 0); rule.sizeDelta = new Vector2(0, 1);
            var image = rule.gameObject.AddComponent<Image>(); image.color = Rule; image.raycastTarget = false;
            var hatch = Rect("斜纹", row); hatch.anchorMin = new Vector2(.78f, 0); hatch.anchorMax = new Vector2(1, 0); hatch.pivot = new Vector2(1, 0);
            hatch.sizeDelta = new Vector2(0, 5); hatch.anchoredPosition = new Vector2(0, 3);
            var hatchImage = hatch.gameObject.AddComponent<Image>(); hatchImage.sprite = RepairPropCyberStyle.Hatch; hatchImage.type = Image.Type.Tiled;
            hatchImage.color = RepairPropCyberStyle.AmberDim; hatchImage.raycastTarget = false;
        }

        // One window of the column: framed field, title bar, padded body; boots up window by window.
        // Rank 0 = primary (bright frame, solid amber bar), 1 = secondary, 2 = tertiary.
        RectTransform Window(string name, string caption, bool flexible, float delay, out Text bar, int rank = 1)
        {
            var window = Rect(name, PanelRectTransform);
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
            HudWindowDrag.Attach(barImage, window, "surgery/" + name);
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
            button.onClick.AddListener(() => { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); action(); });
            var colors = button.colors; colors.normalColor = colors.selectedColor = new Color(.9f, .9f, .9f, 1); colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(.7f, .7f, .7f, 1); colors.disabledColor = new Color(.9f, .9f, .9f, 1); button.colors = colors;
            var text = Label(rect, label, size, height); text.alignment = TextAnchor.MiddleCenter;
            Stretch((RectTransform)text.transform, 7, 3); return button;
        }

        static void Stretch(RectTransform rect, float x, float y)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(x, y); rect.offsetMax = new Vector2(-x, -y); }

        ScrollRect MakeScroll(Transform parent, out RectTransform body)
        {
            var root = Rect("Operations", parent);
            var element = root.gameObject.AddComponent<LayoutElement>(); element.minHeight = 60; element.flexibleHeight = 1;
            var scroll = root.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 28;
            var viewport = Rect("Viewport", root); Stretch(viewport, 0, 0); viewport.offsetMax = new Vector2(-10, 0);
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .18f);
            viewport.gameObject.AddComponent<RectMask2D>();
            body = Rect("Content", viewport); body.anchorMin = new Vector2(0, 1); body.anchorMax = Vector2.one; body.pivot = new Vector2(.5f, 1); body.sizeDelta = Vector2.zero;
            Column(body, 4, 4); var fitter = body.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = body;
            var bar = Rect("Scrollbar", root); bar.anchorMin = new Vector2(1, 0); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(1, .5f); bar.sizeDelta = new Vector2(8, 0);
            bar.gameObject.AddComponent<Image>().color = ChipBg;
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>(); scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var handle = Rect("Handle", bar); Stretch(handle, 1, 0);
            var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = Rule;
            scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage;
            scroll.verticalScrollbar = scrollbar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            return scroll;
        }

        void OnDestroy()
        {
            if (owner != null) owner.Changed -= OnChanged;
            if (Op != null) { Op.Changed -= OnChanged; Op.Feedback -= OnFeedback; }
        }
    }
}
