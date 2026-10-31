using System.Collections.Generic;
using System.Linq;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    public enum RepairPropWorkspaceTab { Operate, Order, Records }

    // Desktop repair workspace: a single right-hand column on one overlay canvas, so the bench view keeps most of the
    // screen. It reads RepairPropFlow and dispatches only through the bridge's guarded entry points.
    // A normal EventSystem canvas consumes pointer events before they can reach the item inspector.
    public sealed class RepairPropContextPanel : MonoBehaviour
    {
        sealed class ActionRow {public Button button;public Text label,reason;public Image strip;public string command;public int section;}
        // A batch runs a run of commands the player would otherwise click one by one (a set of screws, the power-down or
        // power-up pair) in their order; the single commands stay listed for precise work.
        sealed class Batch {public string key,label;public List<PropCommand> members;public bool pair;public Button button;public Text text;}
        // Visor HUD palette (RepairPropCyberStyle): see-through dark field, amber line work, cyan for information.
        static readonly Color PanelBg=RepairPropCyberStyle.Field,Rule=RepairPropCyberStyle.AmberDim,ChipBg=new Color(1f,.62f,.16f,.07f);
        static readonly Color Ink=RepairPropCyberStyle.Ink,Dim=RepairPropCyberStyle.Dim,Accent=RepairPropCyberStyle.Amber;
        static readonly Color Ok=RepairPropCyberStyle.Ok,Warn=RepairPropCyberStyle.Warn,Bad=RepairPropCyberStyle.Bad,Info=RepairPropCyberStyle.Hot;
        static readonly Color ButtonBg=new Color(.02f,.03f,.035f,.55f),ReadyBg=new Color(1f,.62f,.16f,.09f),IdleBg=new Color(0,0,0,.30f),DeliverBg=new Color(.84f,.93f,.52f,.20f);
        RepairPropStationBridge owner;
        UnifiedClinicDirector clinic;
        Canvas canvas;
        Font font;
        Text title,subtitle,statusTitle,statusBody,toolStatus,feedback,signal,caseCode;
        RectTransform content,statusCard,testList,repairBody,toolRow,targetRow,toolsBody,finishWindow;
        readonly List<(Image bg,Text text)> phaseChips=new List<(Image,Text)>();
        readonly List<(Text text,PropTest test)> testLines=new List<(Text,PropTest)>();
        readonly List<ActionRow> actions=new List<ActionRow>();
        readonly List<Batch> batches=new List<Batch>();
        readonly Dictionary<string,Button> commands=new Dictionary<string,Button>();
        readonly Dictionary<RepairActionType,Button> tools=new Dictionary<RepairActionType,Button>();
        readonly Dictionary<RepairPropWorkspaceTab,Button> tabs=new Dictionary<RepairPropWorkspaceTab,Button>();
        readonly List<PropNode> targets=new List<PropNode>();
        Button inspectTool,repairMode,appearanceMode;
        RepairPropWorkspaceTab tab,builtTab;
        string previousTarget,lastFeedback,lastViewFeedback;
        int builtRecords=-1;
        bool dirty=true,refreshing,wasVisible,appearanceRequested,builtAppearance;
        RepairUIView heldHud;
        public RectTransform PanelRectTransform {get;private set;}
        public RectTransform AppearanceSlot {get;private set;}
        public ScrollRect Scroll {get;private set;}
        public Dropdown TargetPicker {get;private set;}
        public bool Visible=>PanelRectTransform!=null && PanelRectTransform.gameObject.activeInHierarchy && canvas.enabled;
        public Button CommandButton(string id)=>commands.TryGetValue(id,out var button) ? button:null;
        /// <summary>The batch button currently offered for a command (null when that command is not part of a shown batch).</summary>
        public Button BatchButtonFor(string id)=>batches.FirstOrDefault(b=>b.button!=null && b.members.Any(m=>m.id==id))?.button;
        public RepairPropWorkspaceTab Tab=>tab;
        public bool AppearanceOpen=>appearanceRequested || RepairPropWorkspaceLayout.AppearanceEditing;
        public RepairPropPhase Phase=>RepairPropWorkflowStatus.Phase(owner!=null ? owner.Flow:null);
        public string StatusText=>statusBody!=null ? statusBody.text:"";
        public string FeedbackText=>feedback!=null ? feedback.text:"";
        public IEnumerable<string> TargetOptions=>targets.Select(n=>n.objectName);
        public Button TabButton(RepairPropWorkspaceTab value)=>tabs.TryGetValue(value,out var button) ? button:null;
        public Button ModeButton(bool appearance)=>appearance ? appearanceMode:repairMode;
        public string CommandReason(string id)
        {
            var command=owner!=null ? owner.Flow.Definition.FindCommand(id):null;
            if(command==null) return null;
            RepairPropWorkflowStatus.CommandState(owner.Flow,command,out string reason);
            return reason ?? "";
        }
        // A blocker shared by several rows of one section is written once, under the first of them.
        public bool ReasonShown(string reason)=>!string.IsNullOrEmpty(reason) && actions.Any(a=>a.reason.isActiveAndEnabled && a.reason.text==reason);
        public Button PartButton(string objectName)=>content!=null ? content.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.name=="零件_"+objectName):null;
        public Button QuickTargetButton(string objectName)=>content!=null ? content.GetComponentsInChildren<Button>().FirstOrDefault(b=>b.name=="可操作_"+objectName):null;

        public void Initialize(RepairPropStationBridge bridge)
        {
            owner=bridge;
            font=RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            var host=Rect("RepairCommandsCanvas",transform);
            canvas=host.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=55;
            var scaler=host.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1280,720);scaler.matchWidthOrHeight=.5f;
            host.gameObject.AddComponent<GraphicRaycaster>();
            PanelRectTransform=Rect("RepairCommandsPanel",host);
            PanelRectTransform.anchorMin=PanelRectTransform.anchorMax=new Vector2(1,1);
            PanelRectTransform.pivot=new Vector2(1,1);PanelRectTransform.anchoredPosition=new Vector2(-8,-42);
            // The column is a stack of separate windows (retro desktop references); the transparent backing still takes
            // the pointer in the gaps, so nothing between the windows reaches the bench.
            var backing=PanelRectTransform.gameObject.AddComponent<Image>();backing.color=new Color(0,0,0,0);
            Column(PanelRectTransform,0,4);

            // 1. Case: what is on the bench, where the job stands, what the tests say.
            var caseWindow=Window("CaseWindow","工单 // CASE",false,0f,out var caseBar);caseBar.name="VisorHeader";
            caseCode=RepairPropCyberStyle.Caption(caseBar.transform.parent,"",RepairPropCyberStyle.Mono!=null ? RepairPropCyberStyle.Mono:font,10,RepairPropCyberStyle.AmberDim,TextAnchor.MiddleRight);
            var codeRect=caseCode.rectTransform;codeRect.anchorMin=Vector2.zero;codeRect.anchorMax=Vector2.one;codeRect.offsetMin=new Vector2(90,0);codeRect.offsetMax=new Vector2(-34,0);
            signal=RepairPropCyberStyle.Caption(caseBar.transform.parent,"",font,10,Color.black,TextAnchor.MiddleRight);
            title=Label(caseWindow,"",16,22,Accent);title.fontStyle=FontStyle.Bold;
            subtitle=Paragraph(caseWindow,12,Dim);
            var phaseRow=Row(caseWindow,22,2);
            for(int i=0;i<RepairPropWorkflowStatus.PhaseLabels.Length;i++)
            {
                var chip=Rect("阶段_"+RepairPropWorkflowStatus.PhaseLabels[i],phaseRow);Size(chip,22);
                var bg=chip.gameObject.AddComponent<Image>();bg.raycastTarget=false;
                var text=Label(chip,RepairPropWorkflowStatus.PhaseLabels[i],12,22);text.alignment=TextAnchor.MiddleCenter;Stretch((RectTransform)text.transform,1,0);
                phaseChips.Add((bg,text));
            }
            statusCard=Rect("StatusCard",caseWindow);
            RepairPropCyberStyle.Framed(statusCard.gameObject.AddComponent<Image>(),new Color(0,0,0,0),Rule,true);
            Column(statusCard,6,2);
            statusTitle=Label(statusCard,"",13,18,Accent);statusTitle.fontStyle=FontStyle.Bold;
            statusBody=Paragraph(statusCard,13,Ink);HudMotion.TypeOn(statusBody,260);
            testList=Rect("Tests",statusCard);Column(testList,0,1);
            foreach(var test in owner.Flow.Definition.tests) testLines.Add((Paragraph(testList,12,Dim),test));

            // 2. Tools: repair / finish mode, the tool in hand, the part it is aimed at.
            var toolWindow=Window("ToolWindow","工具 // TOOLS",false,.08f,out var toolBar);
            // The tool / selection line rides in the title bar instead of taking a row.
            toolStatus=RepairPropCyberStyle.Caption(toolBar.transform.parent,"",font,11,Dim,TextAnchor.MiddleRight);
            var statusRect=toolStatus.rectTransform;statusRect.anchorMin=Vector2.zero;statusRect.anchorMax=Vector2.one;statusRect.offsetMin=new Vector2(84,0);statusRect.offsetMax=new Vector2(-30,0);
            toolStatus.fontSize=10;
            var modeRow=Row(toolWindow,24,4);
            repairMode=Button(modeRow,"维修",()=>SetAppearanceOpen(false),0,24,13);repairMode.name="模式_维修";
            appearanceMode=Button(modeRow,"外观",()=>SetAppearanceOpen(true),0,24,13);appearanceMode.name="模式_外观";
            toolsBody=Rect("ToolsBody",toolWindow);Column(toolsBody,0,4);
            toolRow=Row(toolsBody,30,4);
            inspectTool=Button(toolRow,"检查",()=>{owner.SelectToolFromPanel(null);owner.InspectSelected();},0,30,14);
            foreach(var tool in new[]{RepairActionType.RemoveFastener,RepairActionType.OpenHousing,RepairActionType.ServiceModule})
            {
                var value=tool;
                tools.Add(tool,Button(toolRow,tool==RepairActionType.RemoveFastener ? "螺丝刀" : tool==RepairActionType.OpenHousing ? "开壳" : "检测仪",()=>owner.SelectToolFromPanel(value),0,30,14));
            }
            targetRow=Row(toolsBody,34,4);
            TargetPicker=Picker(targetRow);
            Button(targetRow,"整件",()=>owner.SelectTarget(owner.Flow.Definition.rootObjectName),52,34,14);
            TargetPicker.onValueChanged.AddListener(index=>
            {
                if(!refreshing && index>=0 && index<targets.Count) owner.SelectTarget(targets[index].objectName);
            });

            // 3. Actions: the pages (operations / work order / records) and their list.
            repairBody=Window("ActionWindow","操作 // ACTIONS",true,.16f,out _,0);
            var tabRow=Row(repairBody,26,2);
            foreach(var value in new[]{RepairPropWorkspaceTab.Operate,RepairPropWorkspaceTab.Order,RepairPropWorkspaceTab.Records})
            {
                var page=value;
                string label=page==RepairPropWorkspaceTab.Operate ? "操作":page==RepairPropWorkspaceTab.Order ? "工单":"记录";
                var button=Button(tabRow,label,()=>ShowTab(page),0,26,13);button.name="页签_"+label;tabs.Add(page,button);
            }
            Scroll=MakeScroll(repairBody,out content);

            // 3'. Finish (appearance editing) takes the actions window's place.
            finishWindow=Window("FinishWindow","外观 // FINISH",true,0f,out _,0);
            AppearanceSlot=Rect("AppearanceSlot",finishWindow);
            var slotSize=AppearanceSlot.gameObject.AddComponent<LayoutElement>();slotSize.minHeight=200;slotSize.flexibleHeight=1;
            finishWindow.gameObject.SetActive(false);

            // 4. Log: the last message from the bench.
            var logWindow=Window("LogWindow","日志 // LOG",false,.24f,out _,2);
            feedback=Paragraph(logWindow,13,Info);feedback.GetComponent<LayoutElement>().minHeight=18;HudMotion.TypeOn(feedback,200);

            owner.Flow.Changed+=OnChanged;
            owner.Flow.Feedback+=OnFeedback;
            if(owner.TestJournal!=null) owner.TestJournal.Recorded+=OnChanged;
            if(Application.isPlaying && owner.Station!=null && owner.Station.View!=null) {heldHud=owner.Station.View;RepairPropLegacyHud.Acquire(heldHud,this);}
            RepairPropWorkspaceLayout.Register(this);
            Refresh();
        }
        void OnChanged()=>dirty=true;
        void Update()=>Refresh();

        public void ShowTab(RepairPropWorkspaceTab value) {if(tab!=value){tab=value;dirty=true;}}

        // Repair and appearance editing are exclusive: returning to repair switches any active brush off first.
        public void SetAppearanceOpen(bool open)
        {
            if(!open && RepairPropWorkspaceLayout.AppearanceEditing) RepairPropWorkspaceLayout.CloseAppearance();
            appearanceRequested=open;
            if(TargetPicker!=null) TargetPicker.Hide();
            dirty=true;
        }

        public void Refresh()
        {
            if(owner==null || PanelRectTransform==null) return;
            bool visible=owner.WorkspaceVisible;
            if(PanelRectTransform.gameObject.activeSelf!=visible) PanelRectTransform.gameObject.SetActive(visible);
            if(!visible) {wasVisible=false;return;}
            if(!wasVisible)
            {
                wasVisible=true;dirty=true;
                // The request stays readable on the order tab; the intake dialogue would otherwise sit over the bench.
                if(clinic==null) clinic=FindFirstObjectByType<UnifiedClinicDirector>();
                if(clinic!=null && clinic.Station==owner.Station && clinic.Counter!=null && clinic.Counter.DialogueVisible) clinic.Counter.DismissDialogue();
            }
            var flow=owner.Flow;
            var area=(RectTransform)canvas.transform;
            float width=Mathf.Clamp(area.rect.width*.28f,316,360);
            PanelRectTransform.sizeDelta=new Vector2(Mathf.Min(width,Mathf.Max(240,area.rect.width-16)),Mathf.Max(320,area.rect.height-50));
            bool appearance=AppearanceOpen;
            if(appearance!=builtAppearance)
            {
                builtAppearance=appearance;
                repairBody.gameObject.SetActive(!appearance);statusCard.gameObject.SetActive(!appearance);toolsBody.gameObject.SetActive(!appearance);
                finishWindow.gameObject.SetActive(appearance);AppearanceSlot.gameObject.SetActive(appearance);
            }
            Paint(repairMode,!appearance ? Accent:ButtonBg,!appearance ? Color.black:Ink);
            Paint(appearanceMode,appearance ? Accent:ButtonBg,appearance ? Color.black:Ink);
            RefreshHeader(flow);
            if(appearance) {RefreshFeedback();return;}
            foreach(var pair in tabs) Paint(pair.Value,pair.Key==tab ? Rule:IdleBg,pair.Key==tab ? Ink:Dim);
            var tool=owner.Station.ActiveTool;
            foreach(var pair in tools) Paint(pair.Value,tool==pair.Key ? Accent:ButtonBg,tool==pair.Key ? Color.black:Ink);
            Paint(inspectTool,!tool.HasValue ? Accent:ButtonBg,!tool.HasValue ? Color.black:Ink);
            var selected=flow.Definition.FindNode(owner.SelectedTarget);
            toolStatus.text=(tool.HasValue ? RepairActionText.Label(tool.Value).Replace(" ",""):"空手")+" → "+(selected!=null ? selected.displayName:"—");
            int records=RecordCount(flow);
            if(dirty || previousTarget!=owner.SelectedTarget || builtTab!=tab || tab==RepairPropWorkspaceTab.Records && records!=builtRecords) Rebuild(records);
            refreshing=true;
            TargetPicker.SetValueWithoutNotify(Mathf.Max(0,targets.FindIndex(n=>n.objectName==owner.SelectedTarget)));
            TargetPicker.RefreshShownValue();refreshing=false;
            RefreshRows(flow);
            RefreshFeedback();
        }

        void RefreshHeader(RepairPropFlow flow)
        {
            var session=owner.Station.Session;
            title.text="第 "+(session.CaseIndex+1)+"/"+session.CaseCount+" 件 · "+RepairPropText.Item(flow.Definition.kind);
            // Case code and a live link meter, left of the screen-mode button that shares this corner.
            int bars=3+Mathf.FloorToInt(HudMotion.Noise(Mathf.Floor(Time.unscaledTime*2.5f))*2.99f);
            caseCode.text="WO-"+RepairPropCyberStyle.Code(flow.Definition.repairCase.caseId,4)+" "+new string('|',bars)+new string('.',5-bars);
            signal.text="";
            var state=TwoNightRun.Current;
            subtitle.text=flow.Definition.repairCase.customerName+"　营业 "+RepairUIView.FormatTime(session.ShiftDuration)
                +(state!=null ? "　现金 "+TwoNightUi.Money(state.Cash):"");
            var phase=RepairPropWorkflowStatus.Phase(flow);
            for(int i=0;i<phaseChips.Count;i++)
            {
                bool current=i==(int)phase,done=i<(int)phase || flow.Completed;
                phaseChips[i].bg.color=current ? Accent:new Color(0,0,0,0);
                phaseChips[i].text.color=current ? Color.black:done ? Ok:Dim;
            }
            statusTitle.text="当前阶段："+RepairPropWorkflowStatus.Label(phase);
            statusBody.text=RepairPropWorkflowStatus.NextStep(flow);
            foreach(var line in testLines)
            {
                var status=RepairPropWorkflowStatus.TestStatus(flow,line.test,owner.TestJournal);
                line.text.text=line.test.label+"："+RepairPropWorkflowStatus.TestStatusLabel(flow,line.test,status);
                line.text.color=status==RepairPropTestStatus.Passed || status==RepairPropTestStatus.Normal ? Ok
                    : status==RepairPropTestStatus.ExpectedFault ? Warn : status==RepairPropTestStatus.RetestFailed ? Bad
                    : status==RepairPropTestStatus.NotRun ? Dim : Info;
            }
        }

        void RefreshRows(RepairPropFlow flow)
        {
            var shown=new HashSet<(int,string)>();
            // The electric screwdriver holds the bench while it runs; a hand gesture gives way to any other press.
            bool handsBusy=owner.Gesture!=null && owner.Gesture.AutoRunning;
            string next=RepairPropWorkflowStatus.NextCommand(flow)?.id;
            var focusBatch=batches.FirstOrDefault(b=>b.members.Any(m=>m.id==next));
            foreach(var batch in batches)
            {
                bool on=batch==focusBatch,ready=batch.members.Any(m=>Ready(flow,m));
                batch.button.interactable=ready && owner.CanInteract && !handsBusy;
                batch.button.targetGraphic.color=on ? Accent:ready ? ReadyBg:IdleBg;
                RepairPropCyberStyle.SetFrame(batch.button,on ? RepairPropCyberStyle.Hot:ready ? Accent:Rule);
                batch.text.color=on ? Color.black:ready ? Ink:Dim;
            }
            if(focusBatch!=null) next=null;   // the batch carries the focus, not one of its single steps
            foreach(var row in actions)
            {
                var command=flow.Definition.FindCommand(row.command);
                if(command==null) continue;
                var state=RepairPropWorkflowStatus.CommandState(flow,command,out string reason);
                bool ready=state==RepairPropCommandState.Available;
                row.button.interactable=ready && owner.CanInteract && !handsBusy;
                bool focus=ready && command.id==next;
                row.button.targetGraphic.color=focus ? Accent:ready ? (command.kind==PropCommandKind.Complete ? DeliverBg:ReadyBg):IdleBg;
                RepairPropCyberStyle.SetFrame(row.button,focus ? RepairPropCyberStyle.Hot:ready ? (command.kind==PropCommandKind.Complete ? Ok:Accent):Rule);
                row.strip.color=focus ? Color.black:ready ? Accent:state==RepairPropCommandState.Current ? Rule:Warn;
                row.label.color=focus ? Color.black:ready ? Ink:Dim;
                // A shared blocker is written once per section, under the first command it blocks.
                bool show=!ready && !string.IsNullOrEmpty(reason) && shown.Add((row.section,reason));
                row.reason.text=show ? reason:"";
                row.reason.color=state==RepairPropCommandState.Current ? Dim:Warn;
                if(row.reason.gameObject.activeSelf!=show) row.reason.gameObject.SetActive(show);
            }
        }

        void RefreshFeedback()
        {
            string view=owner.Station.View!=null ? owner.Station.View.LastFeedback:null;
            if(view!=lastViewFeedback)
            {
                lastViewFeedback=view;
                if(!string.IsNullOrEmpty(view) && view!=lastFeedback) {lastFeedback=view;feedback.text=view;feedback.color=Info;FitFeedback();}
            }
        }

        void OnFeedback(string message,FeedbackKind kind)
        {
            lastFeedback=message;
            if(feedback==null) return;
            feedback.text=message ?? "";
            feedback.color=kind==FeedbackKind.Success ? Ok:kind==FeedbackKind.Warning ? Warn:kind==FeedbackKind.Error ? Bad:Info;
            FitFeedback();
        }

        // The log takes the room its message needs (a test result with its story note runs to four lines) and gives it
        // back to the actions list afterwards.
        void FitFeedback()
        {
            float width=((RectTransform)feedback.transform).rect.width;
            if(width<=0) return;
            var settings=feedback.GetGenerationSettings(new Vector2(width,0));
            float height=feedback.cachedTextGeneratorForLayout.GetPreferredHeight(feedback.text,settings)/feedback.pixelsPerUnit;
            // The generator can measure before the window has its final width; full-width glyphs give a floor.
            int lines=0;
            foreach(var line in feedback.text.Split('\n')) lines+=Mathf.Max(1,Mathf.CeilToInt(line.Length*feedback.fontSize/Mathf.Max(1f,width-4)));
            height=Mathf.Max(height,lines*feedback.fontSize*1.35f);
            feedback.GetComponent<LayoutElement>().minHeight=Mathf.Clamp(height+4,18,84);
        }

        void Rebuild(int records)
        {
            var flow=owner.Flow;var d=flow.Definition;
            bool changedTarget=previousTarget!=owner.SelectedTarget || builtTab!=tab;
            previousTarget=owner.SelectedTarget;builtTab=tab;builtRecords=records;dirty=false;
            foreach(Transform child in content) {child.gameObject.SetActive(false);Destroy(child.gameObject);}
            actions.Clear();commands.Clear();targets.Clear();batches.Clear();
            bool operate=tab==RepairPropWorkspaceTab.Operate;
            toolRow.gameObject.SetActive(operate);targetRow.gameObject.SetActive(operate);toolStatus.gameObject.SetActive(operate);
            foreach(var node in d.nodes) if(node.objectName==owner.SelectedTarget || Listed(flow,node)) targets.Add(node);
            refreshing=true;
            TargetPicker.ClearOptions();TargetPicker.AddOptions(targets.Select(n=>n.displayName).ToList());
            refreshing=false;
            if(tab==RepairPropWorkspaceTab.Order) BuildOrder(flow);
            else if(tab==RepairPropWorkspaceTab.Records) BuildRecords(flow);
            else BuildOperations(flow);
            if(changedTarget) {Scroll.StopMovement();Scroll.verticalNormalizedPosition=1;}
        }

        void BuildOperations(RepairPropFlow flow)
        {
            var d=flow.Definition;string root=d.rootObjectName;
            bool rootSelected=owner.SelectedTarget==root;
            // The selected part's operations come first; the whole-item section always follows, so power, tests and
            // delivery confirmation stay reachable whatever is selected.
            var selected=d.FindNode(owner.SelectedTarget);
            if(!rootSelected && selected!=null)
            {
                Section(content,"所选部件 · "+selected.displayName+(selected.immutable ? "（保留原件）":""));
                var own=d.commands.Where(c=>c.target==selected.objectName).ToList();
                foreach(var c in own) AddCommand(c,false,1);
                if(own.Count==0) Paragraph(content,13,Dim).text=selected.immutable ? "原有保留部件只可检查，不拆换、不读取私人内容。":"此部件没有维修操作，可用“检查”查看。";
                if(!string.IsNullOrEmpty(selected.motionNote)) Paragraph(content,12,Dim).text=selected.motionNote;
            }
            var offered=Batches(flow).Where(b=>Offered(flow,b)).ToList();
            if(offered.Count>0)
            {
                Section(content,"批量 · 按顺序一次完成");
                foreach(var batch in offered) AddBatch(batch);
            }
            Section(content,"整件 · "+RepairPropText.Item(d.kind)+(rootSelected ? "（已选）":""));
            foreach(var c in d.commands.Where(c=>c.target==root)) AddCommand(c,true,0);
            var quick=targets.Where(n=>n.objectName!=root && n.objectName!=owner.SelectedTarget && RepairPropWorkflowStatus.HasAvailableCommand(flow,n.objectName)).ToList();
            if(quick.Count>0)
            {
                Section(content,"可操作部件（点选后操作）");
                foreach(var node in quick)
                {
                    var target=node;
                    var chip=Button(content,node.displayName,()=>owner.SelectTarget(target.objectName),0,30,14);chip.name="可操作_"+node.objectName;
                    chip.GetComponentInChildren<Text>().alignment=TextAnchor.MiddleLeft;
                }
            }
            Section(content,"零件栏");
            Paragraph(content,12,Dim).text=ModuleLine(flow);
            var parts=d.nodes.Where(n=>n.movable&&flow.GetState(n.outKey)==n.outValue).ToList();
            if(parts.Count==0) Paragraph(content,12,Dim).text="没有拆下的部件。";
            foreach(var node in parts)
            {
                var part=node;
                var button=Button(content,node.displayName,()=>owner.SelectTarget(part.objectName),0,44,15);button.name="零件_"+node.objectName;
                var label=button.GetComponentInChildren<Text>();label.alignment=TextAnchor.UpperLeft;
                var tag=Label(button.transform,PartTag(part),12,44,Dim);tag.alignment=TextAnchor.LowerLeft;Stretch((RectTransform)tag.transform,7,3);
                if(part.objectName==owner.SelectedTarget) button.targetGraphic.color=Rule;
            }
        }

        // Screw sets (same name stem, same verb) and the power-down / power-up pairs.
        static List<Batch> Batches(RepairPropFlow flow)
        {
            var d=flow.Definition;var list=new List<Batch>();
            foreach(var verb in new[]{"remove.","install."})
                foreach(var group in d.commands.Where(c=>c.id.StartsWith(verb,System.StringComparison.Ordinal) && c.target.Length>3
                             && char.IsDigit(c.target[c.target.Length-1]) && char.IsDigit(c.target[c.target.Length-2]) && c.target[c.target.Length-3]=='_')
                         .GroupBy(c=>c.target.Substring(0,c.target.Length-2)))
                {
                    var members=group.ToList();if(members.Count<2) continue;
                    string stem=members[0].label.TrimEnd('0','1','2','3','4','5','6','7','8','9');
                    list.Add(new Batch{key=verb+group.Key,members=members,label=stem.Replace("卸下","卸下全部").Replace("装回","装回全部")});
                }
            void Pair(string key,string a,string b,System.Func<PropCommand,PropCommand,string> label)
            {
                var first=d.FindCommand(a);var second=d.FindCommand(b);
                if(first!=null && second!=null) list.Add(new Batch{key=key,members=new List<PropCommand>{first,second},pair=true,label=label(first,second)});
            }
            Pair("safe.off","power.off","fixture.disconnect",(a,b)=>"断电并"+b.label);
            Pair("test.on","fixture.connect","power.on",(a,b)=>a.label+"并通电");
            return list;
        }

        static bool Ready(RepairPropFlow flow,PropCommand c)=>flow.CanExecute(c.id,c.target,c.tool,out _);

        // A screw set is offered while at least two of its screws are ready; a pair while its first step is ready and
        // its second has not been done yet.
        static bool Offered(RepairPropFlow flow,Batch b)
        {
            if(!b.pair) return b.members.Count(m=>Ready(flow,m))>=2;
            var second=b.members[1];
            return Ready(flow,b.members[0]) && !second.effects.All(e=>flow.GetState(e.key)==e.value);
        }

        void AddBatch(Batch batch)
        {
            var run=batch;
            int count=run.pair ? 2:run.members.Count(m=>Ready(owner.Flow,m));
            run.button=Button(content,run.label+(run.pair ? "":" ×"+count),()=>RunBatch(run),0,44,14);run.button.name="批量_"+run.key;
            run.text=run.button.GetComponentInChildren<Text>();
            batches.Add(run);
        }

        void RunBatch(Batch batch)
        {
            if(!owner.CanInteract) return;
            var flow=owner.Flow;string root=flow.Definition.rootObjectName;
            // With hand feel on, a screw set goes to the electric screwdriver: the screws spin out (or in) in turn.
            if(!batch.pair && owner.Gesture!=null && RepairPropFeel.Enabled)
            {
                var ready=batch.members.Where(m=>Ready(flow,m)).ToList();
                owner.Gesture.RunAuto(ready,m=>Ready(flow,m) && owner.SelectTarget(m.target) && owner.ExecuteFromPanel(m.id),()=>{owner.SelectTarget(root);dirty=true;});
                return;
            }
            foreach(var member in batch.members)
            {
                if(!Ready(flow,member)) continue;
                if(member.target==root) owner.ExecuteWholeItemFromPanel(member.id);
                else {owner.SelectTarget(member.target);owner.ExecuteFromPanel(member.id);}
            }
            if(batch.members.Any(m=>m.target!=root)) owner.SelectTarget(root);
            dirty=true;
        }

        void AddCommand(PropCommand c,bool wholeItem,int section)
        {
            var command=c;
            var button=Button(content,c.label,()=>owner.Perform(command.id,wholeItem),0,48,16);
            var strip=Rect("状态条",button.transform);strip.anchorMin=Vector2.zero;strip.anchorMax=new Vector2(0,1);strip.pivot=new Vector2(0,.5f);strip.sizeDelta=new Vector2(4,0);
            var stripImage=strip.gameObject.AddComponent<Image>();stripImage.raycastTarget=false;
            commands.Add(c.id,button);
            var code=RepairPropCyberStyle.Caption(button.transform,(c.kind==PropCommandKind.Complete ? "FIN-":"CMD-")+(actions.Count+1).ToString("00"),
                RepairPropCyberStyle.Mono!=null ? RepairPropCyberStyle.Mono:font,10,RepairPropCyberStyle.AmberDim,TextAnchor.UpperRight);
            var codeRect=code.rectTransform;codeRect.anchorMin=new Vector2(1,1);codeRect.anchorMax=new Vector2(1,1);codeRect.pivot=new Vector2(1,1);
            codeRect.sizeDelta=new Vector2(54,14);codeRect.anchoredPosition=new Vector2(-8,-3);
            var reason=Paragraph(content,12,Warn);
            actions.Add(new ActionRow{button=button,label=button.GetComponentInChildren<Text>(),reason=reason,strip=stripImage,command=c.id,section=section});
        }

        void BuildOrder(RepairPropFlow flow)
        {
            var data=flow.Definition.repairCase;
            Section(content,"顾客");
            Paragraph(content,14,Ink).text=data.customerName;
            Section(content,"诉求");
            Paragraph(content,14,Ink).text="“"+data.customerStatement+"”";
            Section(content,"物件来历");
            Paragraph(content,13,Ink).text=RepairPropStories.Backstory(flow.Definition.kind);
            if(!string.IsNullOrEmpty(data.intakeNote)) {Section(content,"工单备注");Paragraph(content,13,Info).text=data.intakeNote;}
            if(data.estimatedRepairCost>0) {Section(content,"预估维修费");Paragraph(content,13,Ink).text=data.estimatedRepairCost.ToString();}
        }

        void BuildRecords(RepairPropFlow flow)
        {
            Section(content,"测试记录");
            var entries=owner.TestJournal!=null ? owner.TestJournal.Entries:null;
            if(entries==null || entries.Count==0) Paragraph(content,12,Dim).text="尚未测试。";
            else for(int i=entries.Count-1;i>=0;i--)
            {
                var e=entries[i];
                var head=Paragraph(content,13,e.passed ? Ok:e.retest ? Bad:Warn);
                head.text=(e.retest ? "复测":"初测")+" · "+e.label+"："+(e.passed ? "通过":e.retest ? "未通过":"发现故障");
                if(!string.IsNullOrEmpty(e.message)) Paragraph(content,12,Dim).text=e.message;
            }
            Section(content,"检查记录");
            var session=owner.Station.Session;
            var scanned=flow.Definition.repairCase.inspectionPoints.Where(p=>session.HasScanned(p.pointId)).ToList();
            if(scanned.Count==0) Paragraph(content,12,Dim).text="尚未检查部件。选中部件后点“检查”。";
            foreach(var p in scanned) Paragraph(content,12,Ink).text=p.displayName+"："+p.finding;
        }

        int RecordCount(RepairPropFlow flow)
        {
            var session=owner.Station.Session;
            return (owner.TestJournal!=null ? owner.TestJournal.Entries.Count:0)*1000+flow.Definition.repairCase.inspectionPoints.Count(p=>session.HasScanned(p.pointId));
        }

        // Stage-appropriate targets: one entry per physically present variant; internal parts only once exposed.
        static bool Listed(RepairPropFlow flow,PropNode node)
        {
            var d=flow.Definition;
            if(node.objectName==d.rootObjectName) return true;
            if(!node.immutable && !d.commands.Any(c=>c.target==node.objectName)) return false;
            if(!flow.Matches(node.inspectRequires)) return false;
            if(node.movable && flow.GetState(node.outKey)==node.outValue) return true;
            var t=flow.GetObject(node.objectName);
            return t!=null && t.gameObject.activeInHierarchy;
        }

        string PartTag(PropNode node)
        {
            if (node.outKey=="new.out" && owner.Flow.CommandPolicy!=null && !owner.Flow.InventoryVisible(node))
                return "替换件 · 缺货，需采购";
            string kind=node.objectName.EndsWith("_New") ? "替换件 · 待装":node.objectName.EndsWith("_Fault") ? "故障件":"原件";
            if(node.objectName.EndsWith("_New")) return kind;
            bool parked=owner.BenchPresentation!=null && owner.BenchPresentation.ParkedVisual(node.objectName)!=null && !node.inventoryOnly;
            return kind+" · "+(node.inventoryOnly ? "零件栏":parked ? "台面":"已取下");
        }

        static string ModuleLine(RepairPropFlow flow)
        {
            int module=flow.GetState("module");
            return module==2 ? (flow.GetState("seat")==1 ? "模块：替换件已装上":"模块：替换件已取下") : module==1 ? "模块：已取下，待安装" : "模块：原故障件在位";
        }

        public bool ContainsScreenPoint(Vector2 point)=>Visible && RectTransformUtility.RectangleContainsScreenPoint(PanelRectTransform,point,null);

        static void Paint(Button button,Color bg,Color ink)
        {
            if(button==null) return;
            bool selected=bg==Accent || bg==Rule;
            button.targetGraphic.color=selected ? new Color(1f,.62f,.16f,.13f):ButtonBg;
            RepairPropCyberStyle.SetFrame(button,selected ? Accent:Rule);
            var text=button.GetComponentInChildren<Text>();if(text!=null) text.color=selected ? Accent:ink==Color.black ? Ink:ink;
            // Retro toggle: a small checkbox square at the left, filled when this choice is on.
            var box=button.transform.Find("Box") as RectTransform;
            if(box==null)
            {
                box=(RectTransform)new GameObject("Box",typeof(RectTransform)).transform;box.SetParent(button.transform,false);
                box.anchorMin=box.anchorMax=new Vector2(0,.5f);box.pivot=new Vector2(0,.5f);box.anchoredPosition=new Vector2(6,0);box.sizeDelta=new Vector2(7,7);
                var element=box.gameObject.AddComponent<LayoutElement>();element.ignoreLayout=true;
                var img=box.gameObject.AddComponent<Image>();img.raycastTarget=false;
            }
            var boxImage=box.GetComponent<Image>();
            boxImage.sprite=selected ? RepairPropCyberStyle.Fill:RepairPropCyberStyle.Frame;boxImage.type=Image.Type.Sliced;
            boxImage.color=selected ? Accent:RepairPropCyberStyle.AmberDim;
        }
        RectTransform Rect(string name,Transform parent)
        {
            var rect=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;
            rect.SetParent(parent,false);return rect;
        }
        void Column(RectTransform rect,int padding,float spacing)
        {
            var group=rect.gameObject.AddComponent<VerticalLayoutGroup>();group.padding=new RectOffset(padding,padding,padding,padding);
            group.spacing=spacing;group.childControlWidth=group.childControlHeight=true;group.childForceExpandWidth=true;group.childForceExpandHeight=false;
        }
        RectTransform Row(Transform parent,int height,float spacing=4)
        {
            var row=Rect("Row",parent);Size(row,height);
            var group=row.gameObject.AddComponent<HorizontalLayoutGroup>();group.spacing=spacing;
            group.childControlWidth=group.childControlHeight=true;group.childForceExpandWidth=true;group.childForceExpandHeight=false;return row;
        }
        void Size(RectTransform rect,float height,float width=0)
        {
            var element=rect.gameObject.AddComponent<LayoutElement>();element.minHeight=element.preferredHeight=height;element.flexibleHeight=0;
            if(width>0) element.minWidth=element.preferredWidth=width;
            else {element.minWidth=0;element.flexibleWidth=1;}
        }
        Text Label(Transform parent,string value,int size,float height,Color? color=null)
        {
            var rect=Rect("Label",parent);Size(rect,height);
            var text=rect.gameObject.AddComponent<Text>();text.font=font;text.fontSize=size;text.text=value;
            text.supportRichText=false;text.color=color ?? Ink;text.alignment=TextAnchor.MiddleLeft;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;text.raycastTarget=false;return text;
        }
        // Wrapped text whose layout height follows its own preferred height at the column's actual width.
        Text Paragraph(Transform parent,int size,Color color)
        {
            var rect=Rect("Text",parent);
            var element=rect.gameObject.AddComponent<LayoutElement>();element.minHeight=size+4;element.flexibleWidth=1;
            var text=rect.gameObject.AddComponent<Text>();text.font=font;text.fontSize=size;text.color=color;text.lineSpacing=1.05f;
            text.supportRichText=false;text.alignment=TextAnchor.UpperLeft;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Overflow;text.raycastTarget=false;return text;
        }
        // Section header as an inverse tab (amber block, black type) on a rule, like the monitor screens' titled boxes.
        void Section(Transform parent,string value)
        {
            var row=Rect("Section",parent);Size(row,22);
            var tab=Rect("Tab",row);tab.anchorMin=Vector2.zero;tab.anchorMax=new Vector2(0,1);tab.pivot=new Vector2(0,.5f);
            var tabImage=tab.gameObject.AddComponent<Image>();tabImage.color=new Color(1f,.62f,.16f,.22f);tabImage.raycastTarget=false;
            var text=Label(row,value,13,22,Accent);text.fontStyle=FontStyle.Bold;text.alignment=TextAnchor.MiddleLeft;
            var textRect=(RectTransform)text.transform;textRect.anchorMin=Vector2.zero;textRect.anchorMax=Vector2.one;
            textRect.offsetMin=new Vector2(6,0);textRect.offsetMax=Vector2.zero;
            tab.sizeDelta=new Vector2(Mathf.Min(text.preferredWidth+14,280),0);
            var rule=Rect("分隔",row);rule.anchorMin=Vector2.zero;rule.anchorMax=new Vector2(1,0);rule.pivot=new Vector2(.5f,0);rule.sizeDelta=new Vector2(0,1);
            var image=rule.gameObject.AddComponent<Image>();image.color=Rule;image.raycastTarget=false;
            var hatch=Rect("斜纹",row);hatch.anchorMin=new Vector2(.78f,0);hatch.anchorMax=new Vector2(1,0);hatch.pivot=new Vector2(1,0);
            hatch.sizeDelta=new Vector2(0,5);hatch.anchoredPosition=new Vector2(0,3);
            var hatchImage=hatch.gameObject.AddComponent<Image>();hatchImage.sprite=RepairPropCyberStyle.Hatch;hatchImage.type=Image.Type.Tiled;
            hatchImage.color=RepairPropCyberStyle.AmberDim;hatchImage.raycastTarget=false;
        }
        // One window of the column: framed field, inverse title bar (title, a mono code, window glyphs), a padded body
        // column. Opens with the boot sequence, staggered so the stack comes up window by window.
        // Rank sets the visual weight: 0 = primary (the window the player works in: bright frame, solid amber bar),
        // 1 = secondary (dim frame, dark bar with amber type), 2 = tertiary (no fill, a dim caption on a rule).
        RectTransform Window(string name,string caption,bool flexible,float delay,out Text bar,int rank=1)
        {
            var window=Rect(name,PanelRectTransform);
            var image=window.gameObject.AddComponent<Image>();image.raycastTarget=true;   // a dragged-out window still blocks the bench
            RepairPropCyberStyle.Framed(image,rank==2 ? new Color(.02f,.014f,.008f,.55f):PanelBg,rank==0 ? Accent:rank==1 ? Rule:RepairPropCyberStyle.AmberFaint);
            if(rank==0)
            {
                RepairPropCyberStyle.Texture(window,RepairPropCyberStyle.Dots,new Color(1f,.62f,.16f,.08f),"DotGrid");
                RepairPropCyberStyle.Texture(window,RepairPropCyberStyle.Dither,new Color(1f,.42f,.10f,.05f),"Dither");
            }
            var group=window.gameObject.AddComponent<VerticalLayoutGroup>();group.padding=new RectOffset(6,6,0,5);group.spacing=3;
            group.childControlWidth=group.childControlHeight=true;group.childForceExpandWidth=true;group.childForceExpandHeight=false;
            var titleBar=Rect("TitleBar",window);Size(titleBar,rank==2 ? 15:18);
            var barImage=titleBar.gameObject.AddComponent<Image>();
            barImage.color=rank==0 ? Accent:rank==1 ? new Color(0,0,0,.45f):new Color(0,0,0,0);
            Color barInk=rank==0 ? Color.black:rank==1 ? Accent:Dim;
            bar=Label(titleBar,caption,rank==2 ? 9:11,rank==2 ? 15:18,barInk);
            if(RepairPropCyberStyle.Kana!=null) bar.font=RepairPropCyberStyle.Kana;
            var barRect=(RectTransform)bar.transform;barRect.anchorMin=Vector2.zero;barRect.anchorMax=Vector2.one;barRect.offsetMin=new Vector2(5,0);barRect.offsetMax=new Vector2(-30,0);
            var glyphs=RepairPropCyberStyle.Caption(titleBar,"- x",RepairPropCyberStyle.Mono!=null ? RepairPropCyberStyle.Mono:font,11,barInk,TextAnchor.MiddleRight);
            if(rank>=1) {var rule=Rect("Rule",titleBar);rule.anchorMin=Vector2.zero;rule.anchorMax=new Vector2(1,0);rule.sizeDelta=new Vector2(0,1);var ri=rule.gameObject.AddComponent<Image>();ri.color=rank==1 ? Rule:RepairPropCyberStyle.AmberFaint;ri.raycastTarget=false;}
            HudWindowDrag.Attach(barImage,window,"workspace/"+name);
            var glyphRect=glyphs.rectTransform;glyphRect.anchorMin=Vector2.zero;glyphRect.anchorMax=Vector2.one;glyphRect.offsetMin=new Vector2(0,0);glyphRect.offsetMax=new Vector2(-5,0);
            var element=window.gameObject.AddComponent<LayoutElement>();
            if(flexible) {element.minHeight=120;element.flexibleHeight=1;}
            HudMotion.Boot(window.gameObject,delay);
            return window;
        }
        // Small mono tag laid over the right / centre of the header line (outside layout, inside the panel).
        Text HeaderTag(Transform header,TextAnchor anchor,Color color)
        {
            var tag=RepairPropCyberStyle.Caption(header,"",RepairPropCyberStyle.Mono!=null ? RepairPropCyberStyle.Mono:font,11,color,anchor);
            var rect=tag.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            rect.offsetMin=new Vector2(anchor==TextAnchor.MiddleCenter ? 172:236,0);rect.offsetMax=new Vector2(anchor==TextAnchor.MiddleCenter ? -120:0,0);
            tag.alignment=TextAnchor.MiddleLeft;
            return tag;
        }
        Button Button(Transform parent,string label,UnityEngine.Events.UnityAction action,float width,float height,int size)
        {
            var rect=Rect(label,parent);Size(rect,height,width);
            var image=rect.gameObject.AddComponent<Image>();RepairPropCyberStyle.Framed(image,ButtonBg,Rule);
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;
            button.navigation=new Navigation{mode=Navigation.Mode.None};
            button.onClick.AddListener(()=> {if(EventSystem.current!=null) EventSystem.current.SetSelectedGameObject(null);action();});
            var colors=button.colors;colors.normalColor=colors.selectedColor=new Color(.9f,.9f,.9f,1);colors.highlightedColor=Color.white;
            colors.pressedColor=new Color(.7f,.7f,.7f,1);colors.disabledColor=new Color(.9f,.9f,.9f,1);button.colors=colors;
            var text=Label(rect,label,size,height);text.alignment=TextAnchor.MiddleCenter;
            Stretch((RectTransform)text.transform,7,3);return button;
        }
        static void Stretch(RectTransform rect,float x,float y)
        {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(x,y);rect.offsetMax=new Vector2(-x,-y);}

        ScrollRect MakeScroll(Transform parent,out RectTransform body)
        {
            var root=Rect("Operations",parent);
            var element=root.gameObject.AddComponent<LayoutElement>();element.minHeight=60;element.flexibleHeight=1;
            var scroll=root.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.vertical=true;
            scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=28;
            var viewport=Rect("Viewport",root);Stretch(viewport,0,0);viewport.offsetMax=new Vector2(-10,0);
            viewport.gameObject.AddComponent<Image>().color=new Color(0,0,0,.18f);
            viewport.gameObject.AddComponent<RectMask2D>();
            body=Rect("Content",viewport);body.anchorMin=new Vector2(0,1);body.anchorMax=Vector2.one;body.pivot=new Vector2(.5f,1);body.sizeDelta=Vector2.zero;
            Column(body,4,4);var fitter=body.gameObject.AddComponent<ContentSizeFitter>();fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport=viewport;scroll.content=body;
            var bar=Rect("Scrollbar",root);bar.anchorMin=new Vector2(1,0);bar.anchorMax=Vector2.one;bar.pivot=new Vector2(1,.5f);bar.sizeDelta=new Vector2(8,0);
            bar.gameObject.AddComponent<Image>().color=ChipBg;
            var scrollbar=bar.gameObject.AddComponent<Scrollbar>();scrollbar.direction=Scrollbar.Direction.BottomToTop;
            var handle=Rect("Handle",bar);Stretch(handle,1,0);
            var handleImage=handle.gameObject.AddComponent<Image>();handleImage.color=Rule;
            scrollbar.handleRect=handle;scrollbar.targetGraphic=handleImage;
            scroll.verticalScrollbar=scrollbar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
            return scroll;
        }
        Dropdown Picker(Transform parent)
        {
            var rect=Rect("PartSelector",parent);Size(rect,32);
            var image=rect.gameObject.AddComponent<Image>();RepairPropCyberStyle.Framed(image,ButtonBg,Accent);
            var dropdown=rect.gameObject.AddComponent<Dropdown>();dropdown.targetGraphic=image;
            var caption=Label(rect,"",15,32,Accent);Stretch((RectTransform)caption.transform,8,2);
            ((RectTransform)caption.transform).offsetMax=new Vector2(-26,-2);dropdown.captionText=caption;
            var arrow=Label(rect,"▼",11,30,Accent);var arrowRect=(RectTransform)arrow.transform;
            arrowRect.anchorMin=new Vector2(1,0);arrowRect.anchorMax=Vector2.one;arrowRect.pivot=new Vector2(1,.5f);
            arrowRect.sizeDelta=new Vector2(23,0);arrow.alignment=TextAnchor.MiddleCenter;
            var template=Rect("Template",rect);template.anchorMin=new Vector2(0,0);template.anchorMax=new Vector2(1,0);template.pivot=new Vector2(.5f,1);template.sizeDelta=new Vector2(0,240);
            RepairPropCyberStyle.Framed(template.gameObject.AddComponent<Image>(),RepairPropCyberStyle.FieldDeep,Accent);
            var scroll=template.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=30;
            var viewport=Rect("Viewport",template);Stretch(viewport,3,3);viewport.gameObject.AddComponent<Image>().color=new Color(0,0,0,.4f);viewport.gameObject.AddComponent<RectMask2D>();
            var list=Rect("Content",viewport);list.anchorMin=new Vector2(0,1);list.anchorMax=Vector2.one;list.pivot=new Vector2(.5f,1);list.sizeDelta=new Vector2(0,40);
            var item=Rect("Item",list);item.anchorMin=new Vector2(0,.5f);item.anchorMax=new Vector2(1,.5f);item.sizeDelta=new Vector2(0,40);
            var toggle=item.gameObject.AddComponent<Toggle>();var itemImage=item.gameObject.AddComponent<Image>();itemImage.color=new Color(1f,.62f,.16f,.08f);toggle.targetGraphic=itemImage;
            var selected=Rect("Selected",item);Stretch(selected,1,1);var selection=selected.gameObject.AddComponent<Image>();selection.color=new Color(1f,.62f,.16f,.30f);selection.raycastTarget=false;toggle.graphic=selection;
            var itemText=Label(item,"",15,38);Stretch((RectTransform)itemText.transform,6,2);
            dropdown.itemText=itemText;dropdown.template=template;scroll.viewport=viewport;scroll.content=list;
            template.gameObject.SetActive(false);return dropdown;
        }
        void OnDestroy()
        {
            if(owner!=null && owner.Flow!=null) {owner.Flow.Changed-=OnChanged;owner.Flow.Feedback-=OnFeedback;}
            if(owner!=null && owner.TestJournal!=null) owner.TestJournal.Recorded-=OnChanged;
            if(!ReferenceEquals(heldHud,null)) RepairPropLegacyHud.Release(heldHud,this);
            RepairPropWorkspaceLayout.Unregister(this);
        }
    }
}
