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
    // (RepairPropContextPanel), on one overlay canvas, plus the leave button where the bench has its own:
    //   case (patient, phase chips, the next step, the two checks), tools (visor + the six gantry arms, the part
    //   picker), actions (operate / chart / records pages), log.
    // It reads SurgeryProcedureRuntime and dispatches only through SurgeryCloseupRuntime; the flow decides every command.
    public sealed class SurgeryCloseupPanel : MonoBehaviour
    {
        sealed class ActionRow { public Button button; public Text label, reason; public Image strip; public string command; public int section; }
        sealed class Batch { public string key, label; public List<SurgeryProcedureRuntime.Command> members; public Button button; public Text text; }
        static readonly Color PanelBg = RepairPropCyberStyle.Field, Rule = RepairPropCyberStyle.AmberDim, ChipBg = new Color(1f, .62f, .16f, .07f);
        static readonly Color Ink = RepairPropCyberStyle.Ink, Dim = RepairPropCyberStyle.Dim, Accent = RepairPropCyberStyle.Amber;
        static readonly Color Ok = RepairPropCyberStyle.Ok, Warn = RepairPropCyberStyle.Warn, Bad = RepairPropCyberStyle.Bad, Info = RepairPropCyberStyle.Hot;
        static readonly Color ButtonBg = new Color(.02f, .03f, .035f, .55f), ReadyBg = new Color(1f, .62f, .16f, .09f), IdleBg = new Color(0, 0, 0, .30f), DoneBg = new Color(.84f, .93f, .52f, .20f);
        SurgeryCloseupRuntime owner;
        Canvas canvas;
        Font font;
        Text title, subtitle, statusTitle, statusBody, toolStatus, feedback, caseCode, scanLine, linkLine;
        RectTransform content;
        readonly List<(Image bg, Text text)> phaseChips = new List<(Image, Text)>();
        readonly List<ActionRow> actions = new List<ActionRow>();
        readonly List<Batch> batches = new List<Batch>();
        readonly Dictionary<string, Button> commands = new Dictionary<string, Button>();
        readonly Dictionary<string, Button> tools = new Dictionary<string, Button>();
        readonly Dictionary<SurgeryWorkspaceTab, Button> tabs = new Dictionary<SurgeryWorkspaceTab, Button>();
        readonly List<string> targets = new List<string>();
        Button inspectTool, leave;
        SurgeryWorkspaceTab tab, builtTab;
        string previousTarget, builtSignature;
        bool dirty = true, refreshing, wasVisible;

        public RectTransform PanelRectTransform { get; private set; }
        public ScrollRect Scroll { get; private set; }
        public Dropdown TargetPicker { get; private set; }
        public Button LeaveButton => leave;
        public bool Visible => PanelRectTransform != null && PanelRectTransform.gameObject.activeInHierarchy && canvas.enabled;
        public Button CommandButton(string id) => commands.TryGetValue(id, out var button) ? button : null;
        public Button BatchButton(string key) => batches.FirstOrDefault(b => b.key == key)?.button;
        public Button BatchButtonFor(string commandId) => batches.FirstOrDefault(b => b.button != null && b.members.Any(m => m.id == commandId))?.button;
        public Button ToolButton(string tool) => tool == null ? inspectTool : tools.TryGetValue(tool, out var button) ? button : null;
        public Button TabButton(SurgeryWorkspaceTab value) => tabs.TryGetValue(value, out var button) ? button : null;
        public SurgeryWorkspaceTab Tab => tab;
        public string StatusText => statusBody != null ? statusBody.text : "";
        public string FeedbackText => feedback != null ? feedback.text : "";
        public IEnumerable<string> TargetOptions => targets;
        public bool ContainsScreenPoint(Vector2 point) => Visible && RectTransformUtility.RectangleContainsScreenPoint(PanelRectTransform, point, null);
        public string ReasonFor(string id) => actions.FirstOrDefault(a => a.command == id)?.reason.text;
        public bool ReasonShown(string reason) => !string.IsNullOrEmpty(reason) && actions.Any(a => a.reason.isActiveAndEnabled && a.reason.text == reason);
        public Button PartButton(string id) => content != null ? content.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "零件_" + id) : null;
        public Button QuickTargetButton(string id) => content != null ? content.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "可操作_" + id) : null;

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
            title = Label(caseWindow, "手术 · 罗亚的右前臂", 16, 22, Accent); title.fontStyle = FontStyle.Bold;
            subtitle = Paragraph(caseWindow, 12, Dim);
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
            statusTitle = Label(statusCard, "", 13, 18, Accent); statusTitle.fontStyle = FontStyle.Bold;
            statusBody = Paragraph(statusCard, 13, Ink); HudMotion.TypeOn(statusBody, 260);
            scanLine = Paragraph(statusCard, 12, Dim);
            linkLine = Paragraph(statusCard, 12, Dim);

            // 2. Tools: the visor (inspect) and the gantry's six arms; the part they are aimed at.
            var toolWindow = Window("ToolWindow", "工具 // TOOLS", false, .08f, out var toolBar);
            toolStatus = RepairPropCyberStyle.Caption(toolBar.transform.parent, "", font, 10, Dim, TextAnchor.MiddleRight);
            var statusRect = toolStatus.rectTransform; statusRect.anchorMin = Vector2.zero; statusRect.anchorMax = Vector2.one; statusRect.offsetMin = new Vector2(84, 0); statusRect.offsetMax = new Vector2(-30, 0);
            var firstRow = Row(toolWindow, 30, 4);
            inspectTool = Button(firstRow, "检查", () => owner.InspectSelected(), 0, 30, 14); inspectTool.name = "工具_检查";
            var secondRow = Row(toolWindow, 30, 4);
            for (int i = 0; i < SurgeryProcedureRuntime.ToolOrder.Length; i++)
            {
                string tool = SurgeryProcedureRuntime.ToolOrder[i];
                var button = Button(i < 3 ? firstRow : secondRow, SurgeryProcedureRuntime.ToolName(tool), () => owner.SelectTool(tool), 0, 30, 14);
                button.name = "工具_" + tool;
                tools.Add(tool, button);
            }
            var targetRow = Row(toolWindow, 34, 4);
            TargetPicker = Picker(targetRow);
            Button(targetRow, "整臂", () => owner.Select(SurgeryProcedureRuntime.Whole), 52, 34, 14).name = "整臂";
            TargetPicker.onValueChanged.AddListener(index =>
            {
                if (!refreshing && index >= 0 && index < targets.Count) owner.Select(targets[index]);
            });

            // 3. Actions: operate / chart / records.
            var actionWindow = Window("ActionWindow", "操作 // ACTIONS", true, .16f, out _, 0);
            var tabRow = Row(actionWindow, 26, 2);
            foreach (var value in new[] { SurgeryWorkspaceTab.Operate, SurgeryWorkspaceTab.Chart, SurgeryWorkspaceTab.Records })
            {
                var page = value;
                string label = page == SurgeryWorkspaceTab.Operate ? "操作" : page == SurgeryWorkspaceTab.Chart ? "病历" : "记录";
                var button = Button(tabRow, label, () => ShowTab(page), 0, 26, 13); button.name = "页签_" + label; tabs.Add(page, button);
            }
            Scroll = MakeScroll(actionWindow, out content);

            // 4. Log: the last message from the table.
            var logWindow = Window("LogWindow", "日志 // LOG", false, .24f, out _, 2);
            feedback = Paragraph(logWindow, 13, Info); feedback.GetComponent<LayoutElement>().minHeight = 18; HudMotion.TypeOn(feedback, 200);

            owner.Changed += OnChanged;
            if (Op != null) { Op.Changed += OnChanged; Op.Feedback += OnFeedback; }
            PanelRectTransform.gameObject.SetActive(false); leave.gameObject.SetActive(false);
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
            if (PanelRectTransform.gameObject.activeSelf != visible) { PanelRectTransform.gameObject.SetActive(visible); leave.gameObject.SetActive(visible); }
            if (!visible) { wasVisible = false; return; }
            if (!wasVisible) { wasVisible = true; dirty = true; feedback.text = Op.LastMessage ?? "近景手术：选中部件，换上对应的机械臂，按步骤来。"; }
            var area = (RectTransform)canvas.transform;
            float width = Mathf.Clamp(area.rect.width * .28f, 316, 360);
            PanelRectTransform.sizeDelta = new Vector2(Mathf.Min(width, Mathf.Max(240, area.rect.width - 16)), Mathf.Max(320, area.rect.height - 50));
            RefreshHeader();
            foreach (var pair in tabs) Paint(pair.Value, pair.Key == tab ? Rule : IdleBg, pair.Key == tab ? Ink : Dim);
            foreach (var pair in tools) Paint(pair.Value, owner.Tool == pair.Key ? Accent : ButtonBg, owner.Tool == pair.Key ? Color.black : Ink);
            Paint(inspectTool, owner.Tool == null ? Accent : ButtonBg, owner.Tool == null ? Color.black : Ink);
            toolStatus.text = SurgeryProcedureRuntime.ToolName(owner.Tool) + " → " + SurgeryCyberArm.Label(owner.Selected);
            // Rebuild when the page, the selection or what is on the table changed; otherwise only repaint the rows.
            string signature = Signature();
            if (dirty || previousTarget != owner.Selected || builtTab != tab || signature != builtSignature) Rebuild(signature);
            refreshing = true;
            TargetPicker.SetValueWithoutNotify(Mathf.Max(0, targets.IndexOf(owner.Selected)));
            TargetPicker.RefreshShownValue(); refreshing = false;
            RefreshRows();
        }

        void RefreshHeader()
        {
            int bars = 3 + Mathf.FloorToInt(HudMotion.Noise(Mathf.Floor(Time.unscaledTime * 2.5f)) * 2.99f);
            caseCode.text = "OP-" + RepairPropCyberStyle.Code("surgery-luo-right-forearm", 4) + " " + new string('|', bars) + new string('.', 5 - bars);
            var run = TwoNightRun.Current;
            subtitle.text = "罗亚 · 码头吊机操作员　手术计时 " + RepairUIView.FormatTime(owner.OpenSeconds) + (run != null ? "　现金 " + TwoNightUi.Money(run.Cash) : "");
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
            scanLine.text = "义体扫描：" + (scanned ? "租赁锁 · 远程锁死；神经接口完好" : "未扫描");
            scanLine.color = scanned ? Warn : Dim;
            int links = Op.LinkResults.Count;
            linkLine.text = "神经链路联调：" + (Op.Get("linked") == 1 ? "通过（" + SurgeryProcedureRuntime.LinkPasses + "/" + SurgeryProcedureRuntime.LinkPasses + "）"
                : links > 0 ? links + "/" + SurgeryProcedureRuntime.LinkPasses : "未测");
            linkLine.color = Op.Get("linked") == 1 ? Ok : links > 0 ? Info : Dim;
        }

        void RefreshRows()
        {
            var shown = new HashSet<(int, string)>();
            var next = Op.Next();
            var focusBatch = next != null ? batches.FirstOrDefault(b => b.members.Any(m => m.id == next.id)) : null;
            foreach (var batch in batches)
            {
                bool on = batch == focusBatch, ready = batch.members.Any(m => Op.Why(m) == null);
                batch.button.interactable = ready;
                batch.button.targetGraphic.color = on ? Accent : ready ? ReadyBg : IdleBg;
                RepairPropCyberStyle.SetFrame(batch.button, on ? RepairPropCyberStyle.Hot : ready ? Accent : Rule);
                batch.text.color = on ? Color.black : ready ? Ink : Dim;
            }
            string focus = focusBatch != null ? null : next?.id;   // a batch carries the focus instead of one of its single steps
            foreach (var row in actions)
            {
                var command = Op.Find(row.command);
                if (command == null) continue;
                var state = Op.StateOf(command, out string reason);
                bool ready = state == SurgeryProcedureRuntime.CommandState.Available;
                row.button.interactable = ready;
                bool lead = ready && command.id == focus;
                bool final = command.id == "complete";
                row.button.targetGraphic.color = lead ? Accent : ready ? (final ? DoneBg : ReadyBg) : IdleBg;
                RepairPropCyberStyle.SetFrame(row.button, lead ? RepairPropCyberStyle.Hot : ready ? (final ? Ok : Accent) : Rule);
                row.strip.color = lead ? Color.black : ready ? Accent : state == SurgeryProcedureRuntime.CommandState.Done ? Rule : Warn;
                row.label.color = lead ? Color.black : ready ? Ink : Dim;
                // A shared blocker is written once per section, under the first command it blocks.
                bool show = !ready && !string.IsNullOrEmpty(reason) && shown.Add((row.section, reason));
                row.reason.text = show ? reason : "";
                row.reason.color = state == SurgeryProcedureRuntime.CommandState.Done ? Dim : Warn;
                if (row.reason.gameObject.activeSelf != show) row.reason.gameObject.SetActive(show);
            }
        }

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

        // What decides the page's layout: the parts on the table and which commands are offered (not their readiness).
        string Signature()
        {
            var parts = string.Join(",", Selectable());
            var bin = string.Join(",", Bin().Select(b => b.id));
            var quick = string.Join(",", QuickTargets());
            var offered = string.Join(",", Batches().Where(Offered).Select(b => b.key + b.members.Count(m => Op.Why(m) == null)));
            return parts + "|" + bin + "|" + quick + "|" + offered + "|" + Op.Get("lamp") + Op.Get("arms") + Op.Get("tray") + Op.Get("straps") + "|" + Op.Inspected.Count + "|" + Op.LinkResults.Count + "|" + Op.Get("scanned");
        }

        void Rebuild(string signature)
        {
            bool changedTarget = previousTarget != owner.Selected || builtTab != tab;
            previousTarget = owner.Selected; builtTab = tab; builtSignature = signature; dirty = false;
            foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            actions.Clear(); commands.Clear(); batches.Clear();
            targets.Clear(); targets.AddRange(Selectable());
            if (!targets.Contains(owner.Selected)) targets.Add(owner.Selected);
            refreshing = true;
            TargetPicker.ClearOptions(); TargetPicker.AddOptions(targets.Select(SurgeryCyberArm.Label).ToList());
            refreshing = false;
            if (tab == SurgeryWorkspaceTab.Chart) BuildChart();
            else if (tab == SurgeryWorkspaceTab.Records) BuildRecords();
            else BuildOperations();
            if (changedTarget) { Scroll.StopMovement(); Scroll.verticalNormalizedPosition = 1; }
        }

        // Parts on his arm or on the tray that can be picked now, the whole arm first.
        IEnumerable<string> Selectable()
        {
            yield return SurgeryProcedureRuntime.Whole;
            var seen = new HashSet<string>();
            foreach (var root in new[] { Op.TargetTransform(SurgeryProcedureRuntime.Whole), Op.OldArm, Op.NewArm })
            {
                if (root == null) continue;
                foreach (var part in root.GetComponentsInChildren<SurgeryArmPart>())
                    if (part.Exposed && seen.Add(part.Id)) yield return part.Id;
            }
        }

        IEnumerable<string> QuickTargets() => Selectable().Where(id => id != SurgeryProcedureRuntime.Whole && id != owner.Selected
            && Op.Commands.Any(c => !c.table && c.target == id && Op.Why(c) == null));

        // The parts bin and the tray: what has come off (and is waiting to go back) and the two forearms.
        IEnumerable<(string id, string label, string tag)> Bin()
        {
            foreach (var s in SurgeryCyberArm.CoverScrews) if (Op.Get("out." + s) == 1) yield return (s, SurgeryCyberArm.Label(s), "零件栏");
            if (Op.Get("cover") == 1) yield return ("ServiceCover", "检修盖板", "零件栏");
            if (Op.Get("tape") == 1) yield return ("ClampTape", "胶布（写着字）", "零件栏");
            foreach (var b in SurgeryCyberArm.Bolts) if (Op.Get("out." + b) == 1) yield return (b, SurgeryCyberArm.Label(b), "零件栏 · 待装回");
            if (Op.OldArm != null && Op.Get("arm") != 1) yield return (SurgeryCyberArm.Old, "旧前臂", "托盘 · 锁死的租赁件");
            if (Op.NewArm != null && Op.Get("arm") != 2) yield return (SurgeryCyberArm.New, "替换前臂", "托盘 · 待装");
        }

        void BuildOperations()
        {
            string selected = owner.Selected;
            if (selected != SurgeryProcedureRuntime.Whole && selected != SurgeryProcedureRuntime.TableTarget)
            {
                bool body = selected == "SocketRing" || selected == "Socket" || selected == "NeuralPlug";
                Section(content, "所选部件 · " + SurgeryCyberArm.Label(selected) + (selected == "SocketRing" ? "（保留）" : ""));
                var own = Op.Commands.Where(c => !c.table && c.target == selected).ToList();
                foreach (var c in own) AddCommand(c, 1);
                if (own.Count == 0) Paragraph(content, 13, Dim).text = body ? "身体侧部件：只清理、不拆换。可用“检查”查看。" : "此部件没有手术操作，可用“检查”查看。";
            }
            var offered = Batches().Where(Offered).ToList();
            if (offered.Count > 0)
            {
                Section(content, "批量 · 按顺序一次完成");
                foreach (var batch in offered) AddBatch(batch);
            }
            Section(content, "整臂 · 罗亚的右臂" + (selected == SurgeryProcedureRuntime.Whole ? "（已选）" : ""));
            foreach (var c in Op.Commands.Where(c => !c.table && c.target == SurgeryProcedureRuntime.Whole)) AddCommand(c, 0);
            // The table's switches: only the one each that applies now.
            Section(content, "手术台");
            foreach (var c in Op.Commands.Where(c => c.table && c.requires.All(r => Op.Get(r.key) == r.value))) AddCommand(c, 2);
            var quick = QuickTargets().ToList();
            if (quick.Count > 0)
            {
                Section(content, "可操作部件（点选后操作）");
                foreach (var id in quick)
                {
                    var target = id;
                    var chip = Button(content, SurgeryCyberArm.Label(id), () => owner.Select(target), 0, 30, 14); chip.name = "可操作_" + id;
                    chip.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                }
            }
            Section(content, "零件栏 · 托盘");
            var bin = Bin().ToList();
            if (bin.Count == 0) Paragraph(content, 12, Dim).text = "还没有拆下的部件。";
            foreach (var entry in bin)
            {
                var id = entry.id;
                var button = Button(content, entry.label, () => owner.Select(id), 0, 44, 15); button.name = "零件_" + id;
                var label = button.GetComponentInChildren<Text>(); label.alignment = TextAnchor.UpperLeft;
                var tag = Label(button.transform, entry.tag, 12, 44, Dim); tag.alignment = TextAnchor.LowerLeft; Stretch((RectTransform)tag.transform, 7, 3);
                if (id == owner.Selected) button.targetGraphic.color = Rule;
            }
        }

        // Screw and bolt sets (same verb on the same family).
        List<Batch> Batches()
        {
            var list = new List<Batch>();
            void Add(string key, string label, IEnumerable<string> ids)
            {
                var members = ids.Select(Op.Find).Where(c => c != null).ToList();
                if (members.Count > 1) list.Add(new Batch { key = key, label = label, members = members });
            }
            Add("remove.CoverScrew", "卸下全部检修盖螺钉", SurgeryCyberArm.CoverScrews.Select(s => "remove." + s));
            Add("remove.ClampBolt", "卸下全部卡箍螺栓", SurgeryCyberArm.Bolts.Select(b => "remove." + b));
            Add("install.ClampBolt", "拧紧全部卡箍螺栓", SurgeryCyberArm.Bolts.Select(b => "install." + b));
            return list;
        }

        // A set is offered while at least two of its members are ready.
        bool Offered(Batch b) => b.members.Count(m => Op.Why(m) == null) >= 2;

        void AddBatch(Batch batch)
        {
            var run = batch;
            int count = run.members.Count(m => Op.Why(m) == null);
            run.button = Button(content, run.label + " ×" + count, () => owner.RunBatch(run.members.Where(m => Op.Why(m) == null).Select(m => m.id)), 0, 44, 14);
            run.button.name = "批量_" + run.key;
            run.text = run.button.GetComponentInChildren<Text>();
            batches.Add(run);
        }

        void AddCommand(SurgeryProcedureRuntime.Command c, int section)
        {
            var command = c;
            var button = Button(content, c.label, () => owner.RunFromPanel(command.id), 0, 48, 16);
            button.name = "命令_" + c.id;
            var strip = Rect("状态条", button.transform); strip.anchorMin = Vector2.zero; strip.anchorMax = new Vector2(0, 1); strip.pivot = new Vector2(0, .5f); strip.sizeDelta = new Vector2(4, 0);
            var stripImage = strip.gameObject.AddComponent<Image>(); stripImage.raycastTarget = false;
            commands[c.id] = button;
            string code = (c.id == "complete" ? "FIN-" : c.table ? "TBL-" : "CMD-") + (actions.Count + 1).ToString("00");
            if (c.tool != null) code = SurgeryProcedureRuntime.ToolName(c.tool) + " · " + code;
            var caption = RepairPropCyberStyle.Caption(button.transform, code, font, 10, RepairPropCyberStyle.AmberDim, TextAnchor.UpperRight);
            var captionRect = caption.rectTransform; captionRect.anchorMin = captionRect.anchorMax = captionRect.pivot = new Vector2(1, 1);
            captionRect.sizeDelta = new Vector2(110, 14); captionRect.anchoredPosition = new Vector2(-8, -3);
            var reason = Paragraph(content, 12, Warn);
            actions.Add(new ActionRow { button = button, label = button.GetComponentInChildren<Text>(), reason = reason, strip = stripImage, command = c.id, section = section });
        }

        void BuildChart()
        {
            Section(content, "病人");
            Paragraph(content, 14, Ink).text = "罗亚　码头吊机 3 组 · 吊机操作员";
            Section(content, "诉求");
            Paragraph(content, 14, Ink).text = "“右手动不了了。合同到期那天夜里，它自己就锁上了。”";
            Section(content, "义体来历");
            Paragraph(content, 13, Ink).text = "码头公司租给吊机工的标准作业臂，租金每月从工资里扣。公司裁掉了吊机 3 组，当晚远程锁死了整组人的手臂，系统里写着“雇主已释放”。";
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
            if (Op.Inspected.Count == 0) Paragraph(content, 12, Dim).text = "尚未检查部件。选中部件后点“检查”，或换成“检查”直接点部件。";
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
            var text = Label(row, value, 13, 22, Accent); text.fontStyle = FontStyle.Bold; text.alignment = TextAnchor.MiddleLeft;
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

        Dropdown Picker(Transform parent)
        {
            var rect = Rect("PartSelector", parent); Size(rect, 32);
            var image = rect.gameObject.AddComponent<Image>(); RepairPropCyberStyle.Framed(image, ButtonBg, Accent);
            var dropdown = rect.gameObject.AddComponent<Dropdown>(); dropdown.targetGraphic = image;
            var caption = Label(rect, "", 15, 32, Accent); Stretch((RectTransform)caption.transform, 8, 2);
            ((RectTransform)caption.transform).offsetMax = new Vector2(-26, -2); dropdown.captionText = caption;
            var arrow = Label(rect, "▼", 11, 30, Accent); var arrowRect = (RectTransform)arrow.transform;
            arrowRect.anchorMin = new Vector2(1, 0); arrowRect.anchorMax = Vector2.one; arrowRect.pivot = new Vector2(1, .5f);
            arrowRect.sizeDelta = new Vector2(23, 0); arrow.alignment = TextAnchor.MiddleCenter;
            var template = Rect("Template", rect); template.anchorMin = new Vector2(0, 0); template.anchorMax = new Vector2(1, 0); template.pivot = new Vector2(.5f, 1); template.sizeDelta = new Vector2(0, 240);
            RepairPropCyberStyle.Framed(template.gameObject.AddComponent<Image>(), RepairPropCyberStyle.FieldDeep, Accent);
            var scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            var viewport = Rect("Viewport", template); Stretch(viewport, 3, 3); viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .4f); viewport.gameObject.AddComponent<RectMask2D>();
            var list = Rect("Content", viewport); list.anchorMin = new Vector2(0, 1); list.anchorMax = Vector2.one; list.pivot = new Vector2(.5f, 1); list.sizeDelta = new Vector2(0, 40);
            var item = Rect("Item", list); item.anchorMin = new Vector2(0, .5f); item.anchorMax = new Vector2(1, .5f); item.sizeDelta = new Vector2(0, 40);
            var toggle = item.gameObject.AddComponent<Toggle>(); var itemImage = item.gameObject.AddComponent<Image>(); itemImage.color = new Color(1f, .62f, .16f, .08f); toggle.targetGraphic = itemImage;
            var selected = Rect("Selected", item); Stretch(selected, 1, 1); var selection = selected.gameObject.AddComponent<Image>(); selection.color = new Color(1f, .62f, .16f, .30f); selection.raycastTarget = false; toggle.graphic = selection;
            var itemText = Label(item, "", 15, 38); Stretch((RectTransform)itemText.transform, 6, 2);
            dropdown.itemText = itemText; dropdown.template = template; scroll.viewport = viewport; scroll.content = list;
            template.gameObject.SetActive(false); return dropdown;
        }

        void OnDestroy()
        {
            if (owner != null) owner.Changed -= OnChanged;
            if (Op != null) { Op.Changed -= OnChanged; Op.Feedback -= OnFeedback; }
        }
    }
}
