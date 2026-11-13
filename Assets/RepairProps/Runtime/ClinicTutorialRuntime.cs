using System.Linq;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.RepairProps
{
    // The first order of a new game is a guided one. UNIT 07 names one step at a time on a card at the bottom of the
    // screen (it takes the guide band's place at the bench), a pulsing frame marks the button to press, and the flow
    // refuses anything that is not the step in hand. The order's fault is pinned to the one that needs a new part, so
    // the lesson covers buying and replacing. It ends when the first order is delivered, or when the player skips it.
    //
    // It starts by itself only when the game is started in the clinic scene (a build, or Play from that scene); scenes
    // loaded later, as the tests do, are left alone. The short bench HUD (RepairPropWorkspaceLayout.Compact) is
    // switched on at the same moment and stays on after the lesson.
    [AddComponentMenu("")]
    public sealed class ClinicTutorialRuntime : MonoBehaviour
    {
        public const string GameplayScene = "RepairProps_Clinic";
        const string CompatibleSku = "rx-rf";   // the receiver's RF daughterboard

        public static ClinicTutorialRuntime Current { get; private set; }

        UnifiedClinicDirector clinic;
        Canvas canvas;
        RectTransform card, marker;
        Image markerImage;
        Text body, footer;
        Font font;
        float finishedAt = -1;
        int gateFrame = -1;
        RepairPropFlow gateFlow;
        PropCommand gateNext;
        bool resolving;

        /// <summary>True from the start until the first order is delivered or the lesson is skipped.</summary>
        public bool Running { get; private set; }
        /// <summary>Which step the card is on: receive, dialogue, carry, return, cmd:family, buy, buy-menu, shop, gesture, blocked, leave, deliver, done.</summary>
        public string StepId { get; private set; } = "";
        public string StepText => body != null ? body.text : "";
        public RectTransform Card => card;
        /// <summary>The control the pulsing frame is on, or null.</summary>
        public RectTransform Marked { get; private set; }
        public Button SkipButton { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() => Current = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartWithTheGame()
        {
            if (UnityEngine.XR.XRSettings.isDeviceActive || SceneManager.GetActiveScene().name != GameplayScene) return;
            Begin();
        }

        /// <summary>Starts the lesson and the short HUD (also what the tests call). Call before the first item is received.</summary>
        public static ClinicTutorialRuntime Begin()
        {
            if (Current != null) return Current;
            RepairPropWorkspaceLayout.Compact = true;
            RepairPropWorkspaceLayout.AutoHide = true;
            var host = new GameObject("ClinicTutorial (runtime only)");
            return host.AddComponent<ClinicTutorialRuntime>();
        }

        void Awake()
        {
            Current = this;
            Running = true;
            RepairPropFlow.FaultChooser = ChooseFault;
            RepairPropFlow.ExecuteGate = Gate;
            font = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            BuildUi();
        }

        void OnDestroy()
        {
            Release();
            if (Current == this) Current = null;
        }

        void Release()
        {
            Running = false;
            if (RepairPropFlow.FaultChooser == ChooseFault) RepairPropFlow.FaultChooser = null;
            if (RepairPropFlow.ExecuteGate == Gate) RepairPropFlow.ExecuteGate = null;
            RepairPropArOverlay.GuideHidden = false;
        }

        /// <summary>Ends the lesson now: the gate lifts and the guide band comes back. The short HUD stays.</summary>
        public void Skip()
        {
            Release();
            Destroy(gameObject);
        }

        // ---- rules -------------------------------------------------------------------------------------------

        static int Delivered(TwoNightState state) =>
            state == null || state.customerTrades == null ? 0 : state.customerTrades.Count(t => t.state == ClinicTradeState.Delivered);

        /// <summary>The lesson's fault: the one whose repair route fits a new part (1-based), or 0 to leave the dice alone.</summary>
        public static int TutorialFault(RepairPropFlowDefinition definition)
        {
            if (definition == null || !definition.HasFaultPool || definition.kind != RepairPropKind.Receiver) return 0;
            return definition.faults.FindIndex(f => f.route.Contains("module.install.new")) + 1;
        }

        int ChooseFault(RepairPropFlow flow) => Running && Delivered(TwoNightRun.Current) == 0 ? TutorialFault(flow.Definition) : 0;

        /// <summary>Steps that count as the same move: the screws of one cover, power and test leads, the tests.</summary>
        public static string Family(string commandId)
        {
            if (string.IsNullOrEmpty(commandId)) return "";
            int dot = commandId.IndexOf('.');
            string head = dot < 0 ? commandId : commandId.Substring(0, dot);
            return head == "power" || head == "fixture" ? "setup" : head;
        }

        PropCommand Next(RepairPropFlow flow)
        {
            if (gateFrame == Time.frameCount && gateFlow == flow) return gateNext;
            resolving = true;
            try { gateNext = RepairPropWorkflowStatus.NextCommand(flow); }
            finally { resolving = false; }
            gateFrame = Time.frameCount; gateFlow = flow;
            return gateNext;
        }

        // Looking is always free; anything else must be the step in hand or one of its kind.
        string Gate(RepairPropFlow flow, PropCommand command)
        {
            if (!Running || resolving || command == null) return null;
            if (command.kind == PropCommandKind.Inspect || command.kind == PropCommandKind.Pose) return null;
            var next = Next(flow);
            if (next == null || Family(next.id) == Family(command.id)) return null;
            return "教学中：先完成这一步——" + next.label + "。";
        }

        // ---- steps -------------------------------------------------------------------------------------------

        void Update()
        {
            if (clinic == null) clinic = FindFirstObjectByType<UnifiedClinicDirector>();
            var state = TwoNightRun.Current;
            bool ready = clinic != null && clinic.Initialized && state != null && !ClinicStartScreenRuntime.Blocking;
            card.gameObject.SetActive(ready);
            if (!ready) { Mark(null); return; }

            if (Running && (state.phase != TwoNightPhase.Night1Counter || Delivered(state) > 0))
            {
                // Delivered (or a game that is already past the first order): say goodbye once, then go.
                bool taught = Delivered(state) > 0 && state.phase == TwoNightPhase.Night1Counter;
                Release();
                if (!taught) { Destroy(gameObject); return; }
                finishedAt = Time.unscaledTime;
                Show("done", "第一单交付，钱到账了。后面的单子我不再一步步带：不知道做什么，就看屏幕下方的指引，或者按 F1。", null);
            }
            if (!Running)
            {
                if (finishedAt >= 0 && Time.unscaledTime - finishedAt > 9f) Destroy(gameObject);
                Layout(false);
                return;
            }

            var station = clinic.Station;
            var item = station != null && station.Inspector != null ? station.Inspector.CurrentItem : null;
            var bridge = item != null ? item.GetComponent<RepairPropStationBridge>() : null;
            bool atBench = clinic.ConsoleOpen && bridge != null && bridge.WorkspaceVisible;
            RepairPropArOverlay.GuideHidden = true;
            Layout(atBench);

            if (clinic.Counter != null && clinic.Counter.DialogueVisible)
                Show("dialogue", "听顾客把话说完，再点对话框里的「继续」。他要保留的东西，修的时候不能换掉。", null);
            else if (clinic.Carrying)
                Show("carry", "东西在你手上了。走到标着 WORKBENCH 的维修台，准星对准台面，按左键放下。", null);
            else if (clinic.HasPendingCase)
                Show("receive", "收件台的灯亮了，有活。走过去，准星对准台上的物品，按左键接收。（WASD 走路，鼠标转头）", null);
            else if (clinic.TradeState == ClinicTradeState.ReadyForDelivery || bridge != null && bridge.Flow != null && bridge.Flow.Completed)
            {
                if (clinic.ConsoleOpen) Show("leave", "修好了。点左上角的「离开维修台」。", null);
                else Show("deliver", "把它送到闪灯的交件台：准星对准后按左键交付。交付了钱才入账。", null);
            }
            else if (clinic.TradeState == ClinicTradeState.InRepair)
            {
                if (atBench) Bench(bridge);
                else Show("return", "回到维修台：准星对准台上的物品，按左键。", null);
            }
            else Show("wait", "稍等，下一步马上就来。", null);
        }

        void Bench(RepairPropStationBridge bridge)
        {
            var flow = bridge.Flow;
            var economy = FindFirstObjectByType<RepairPropEconomyController>();
            var launcher = RepairPropLauncher.Current;
            if (economy != null && economy.IsOpen)
            {
                var shop = FindFirstObjectByType<RepairPropShopPanel>();
                bool have = flow.CommandPolicy == null || flow.CommandPolicy.ReplacementAvailable;
                var plus = shop != null ? shop.QuantityButton(CompatibleSku, true) : null;
                if (have) Show("shop", "买到了。按 Esc 关掉采购终端，回去装上。", null);
                else Show("shop", "找标着「▶ 本单兼容」的那一行，点 + 把数量加到 1，再点确认采购。多买的可以在库存页退掉。",
                    plus != null ? (RectTransform)plus.transform : shop != null && shop.PurchaseButton != null ? (RectTransform)shop.PurchaseButton.transform : null);
                return;
            }
            if (bridge.Gesture != null && bridge.Gesture.Busy && !bridge.Gesture.AutoRunning)
            {
                Show("gesture", "照着屏幕上的图示拖动鼠标，把这个动作做完。做错了不会弄坏东西，按 Esc 可以取消重来。", null);
                return;
            }
            var next = Next(flow);
            if (next == null)
            {
                bool shortage = RepairPropWorkflowStatus.Phase(flow) == RepairPropPhase.Replacement && flow.CommandPolicy != null && !flow.CommandPolicy.ReplacementAvailable;
                bool benchComputer = RepairBenchV2Runtime.Current != null && System.Linq.Enumerable.Any(RepairBenchV2Runtime.Current.Props, p => p.Kind == RepairBenchPropKind.Terminal);
                if (shortage && benchComputer)
                    Show("buy", "拆下来的小板是坏的，店里没有备件，得买一块新的。点维修台右后角那台电脑的屏幕（上面标着「缺件」）。", null);
                else if (shortage && launcher != null && launcher.MenuOpen)
                    Show("buy-menu", "点「01 配件采购」。", launcher.ShopEntry != null ? (RectTransform)launcher.ShopEntry.transform : null);
                else if (shortage)
                    Show("buy", "拆下来的小板是坏的，店里没有备件，得买一块新的。点左上角的 SYS 图标。", launcher != null && launcher.IconButton != null ? (RectTransform)launcher.IconButton.transform : null);
                else Show("blocked", RepairPropWorkflowStatus.NextStep(flow), null);
                return;
            }
            var panel = bridge.ContextPanel;
            var button = panel != null ? panel.BatchButtonFor(next.id) ?? panel.CommandButton(next.id) : null;
            bool shown = button != null && button.gameObject.activeInHierarchy;
            string root = flow.Definition.rootObjectName;
            var node = flow.Definition.FindNode(next.target);
            // A batch button runs the whole set by itself; only a single step needs its part picked first.
            bool batch = shown && panel.BatchButtonFor(next.id) == button;
            string aim = !batch && next.target != root && bridge.SelectedTarget != next.target && node != null
                ? "先点画面上橙色框住的部件（" + node.displayName + "）。然后：" : "";
            string label = shown ? button.GetComponentInChildren<Text>().text : next.label;
            Show("cmd:" + Family(next.id), aim + Explain(flow, next, label), shown ? (RectTransform)button.transform : null);
        }

        static string Explain(RepairPropFlow flow, PropCommand next, string label)
        {
            string press = "点右边发亮的「" + label + "」";
            var phase = RepairPropWorkflowStatus.Phase(flow);
            switch (Family(next.id))
            {
                case "test":
                    return phase == RepairPropPhase.Retest ? "装好了，再测一遍，确认真的修好：" + press + "。"
                        : "先别拆。通着电跑一遍测试，看它到底哪里不对：" + press + "。";
                case "setup":
                    return next.id.EndsWith(".on") || next.id.EndsWith(".connect") ? "测试要先接上测试座并通电：" + press + "。"
                        : "动手拆之前必须断电、拔掉测试线：" + press + "。";
                case "remove": return "把固定件卸下来：" + press + "，再照图示拧。螺丝要一颗一颗卸完。";
                case "cover": return (next.id.EndsWith(".remove") ? "螺丝卸完了，撬开盖板：" : "把盖板扣回去：") + press + "。";
                case "measure": return "用万用表找原因：" + press + "，把红表笔拖到测点上。读数异常的那一处就是坏的。";
                case "module":
                    return next.id == "module.install.new" ? "把新买的小板装上去：" + press + "。"
                        : next.id == "module.install.old" ? "这块没坏，装回去：" + press + "。" : "找到了，是这块小板。把它拆下来：" + press + "。";
                case "install": return "按拆的相反顺序装回去：" + press + "。";
                case "complete": return "复测全部通过，原件也都还在。" + press + "，确认交付。";
                default: return "下一步：" + press + "。";
            }
        }

        void Show(string id, string text, RectTransform mark)
        {
            StepId = id;
            if (body.text != text) body.text = text;
            footer.text = Running ? "UNIT 07  //  教学  //  做完这一步会自动进入下一步" : "UNIT 07  //  教学结束";
            SkipButton.gameObject.SetActive(Running);
            Mark(mark);
        }

        // ---- card and marker ---------------------------------------------------------------------------------

        void BuildUi()
        {
            var host = new GameObject("TutorialCanvas", typeof(RectTransform));
            host.transform.SetParent(transform, false);
            canvas = host.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 88;
            var scaler = host.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            host.AddComponent<GraphicRaycaster>();

            card = Rect("TutorialCard", host.transform);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, 0);
            card.sizeDelta = new Vector2(560, 96);
            var bg = card.gameObject.AddComponent<Image>(); bg.raycastTarget = false;
            RepairPropCyberStyle.Framed(bg, RepairPropCyberStyle.Field, RepairPropCyberStyle.Hot);
            RepairPropCyberStyle.TitleBar(card, "UNIT 07 // 教学", font);
            body = Label(card, 15, RepairPropCyberStyle.Ink, TextAnchor.UpperLeft);
            Stretch(body.rectTransform, 12, 20, 12, 22);
            body.lineSpacing = 1.1f;
            footer = Label(card, 10, RepairPropCyberStyle.AmberDim, TextAnchor.LowerLeft);
            Stretch(footer.rectTransform, 12, 4, 110, 70);
            var skip = Rect("Skip", card);
            skip.anchorMin = skip.anchorMax = skip.pivot = new Vector2(1, 0); skip.anchoredPosition = new Vector2(-8, 4); skip.sizeDelta = new Vector2(92, 18);
            var skipImage = skip.gameObject.AddComponent<Image>();
            RepairPropCyberStyle.Framed(skipImage, new Color(0, 0, 0, .5f), RepairPropCyberStyle.AmberDim);
            SkipButton = skip.gameObject.AddComponent<Button>(); SkipButton.targetGraphic = skipImage;
            SkipButton.onClick.AddListener(Skip);
            var skipText = Label(skip, 11, RepairPropCyberStyle.Amber, TextAnchor.MiddleCenter);
            Stretch(skipText.rectTransform, 0, 0, 0, 0); skipText.text = "跳过教学";

            marker = Rect("Marker", host.transform);
            marker.anchorMin = marker.anchorMax = Vector2.zero; marker.pivot = new Vector2(.5f, .5f);
            markerImage = marker.gameObject.AddComponent<Image>(); markerImage.raycastTarget = false;
            markerImage.sprite = RepairPropCyberStyle.Bracket; markerImage.type = Image.Type.Sliced;
            marker.gameObject.SetActive(false);
            card.gameObject.SetActive(false);
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            r.SetParent(parent, false);
            return r;
        }

        Text Label(Transform parent, int size, Color color, TextAnchor anchor)
        {
            var text = Rect("Label", parent).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.color = color; text.alignment = anchor; text.raycastTarget = false; text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static void Stretch(RectTransform r, float left, float bottom, float right, float top)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top);
        }

        // Bottom centre of the free view: at the bench the panel column takes the right of the screen.
        void Layout(bool atBench)
        {
            var area = (RectTransform)canvas.transform;
            float column = atBench ? Mathf.Clamp(area.rect.width * .28f, 316, 360) + 16 : 0;
            card.anchoredPosition = new Vector2(-column / 2, 14);
            card.sizeDelta = new Vector2(Mathf.Min(560, area.rect.width - column - 40), Mathf.Max(96, body.preferredHeight + 46));
        }

        void Mark(RectTransform target)
        {
            Marked = target != null && target.gameObject.activeInHierarchy ? target : null;
            marker.gameObject.SetActive(Marked != null);
            if (Marked == null) return;
            var corners = new Vector3[4];
            Marked.GetWorldCorners(corners);   // overlay canvases: world corners are screen pixels
            var area = (RectTransform)canvas.transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, corners[0], null, out var min);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, corners[2], null, out var max);
            float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 6f);
            marker.anchoredPosition = (min + max) / 2 - area.rect.min;
            marker.sizeDelta = max - min + Vector2.one * (8 + 6 * pulse);
            markerImage.color = new Color(1f, .78f, .25f, .55f + .45f * pulse);
        }
    }
}
