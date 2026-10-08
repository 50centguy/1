using System.Collections;
using System.Linq;
using BorderRepair.Controls;
using BorderRepair.Core;
using BorderRepair.FirstOrder;
using BorderRepair.FirstOrder.Slice;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BorderRepair.TwoNight
{
    [DefaultExecutionOrder(-500)]
    public class UnifiedClinicDirector : MonoBehaviour
    {
        [SerializeField] RepairStationController station;
        [SerializeField] GameObject consoleSetup;
        [SerializeField] TwoNightCounterDirector counter;
        [SerializeField] TwoNightRobotDirector robot;
        [SerializeField] FirstOrderFlow flow;
        [SerializeField] FirstOrderInput robotInput;
        [SerializeField] SliceView robotView;
        [SerializeField] TwoNightEconomy economy;
        [SerializeField] Font font;
        [SerializeField] Transform receiveAnchor, workAnchor, deliveryAnchor, consoleCameraAnchor;
        [Tooltip("Local Y offset from receive/delivery anchor to the item's support surface.")]
        [SerializeField] float tradeSupportHeight;
        [SerializeField] Camera roomCamera;

        public RepairStationController Station => station;
        public TwoNightCounterDirector Counter => counter;
        public TwoNightRobotDirector Robot => robot;
        public Transform ReceiveAnchor => receiveAnchor;
        public Transform WorkAnchor => workAnchor;
        public Transform DeliveryAnchor => deliveryAnchor;
        public Transform ConsoleCameraAnchor => consoleCameraAnchor;
        public float TradeSupportWorldY(Transform anchor)
        {
            var bench = anchor == workAnchor ? anchor.GetComponentInParent<Renderer>() : null;
            return bench != null ? bench.bounds.max.y : anchor.TransformPoint(Vector3.up * tradeSupportHeight).y;
        }
        public bool Initialized { get; private set; }
        public bool ConsoleOpen { get; private set; }
        public string LastMessage { get; private set; }
        public bool RoomWalking => !ConsoleOpen && flow.Rig.Walking;
        public bool HasPendingCase => State != null && State.phase == TwoNightPhase.Night1Counter &&
            (State.ActiveTrade == null || (State.ActiveTrade.state == ClinicTradeState.Delivered &&
                State.ActiveTrade.queueIndex + 1 < State.customerQueueCount));
        public ClinicTradeState TradeState => State.ActiveTrade != null ? State.ActiveTrade.state : State.communicatorTrade;
        public TwoNightState State => TwoNightRun.Current;

        GameObject tradeUi;
        GameObject crosshair;
        Text status;
        Button consoleButton, roomButton;
        Transform itemParent;
        Vector3 itemLocalPosition;
        Quaternion itemLocalRotation;
        Vector3 itemLocalScale;
        GameObject pendingPreview;

        public void Configure(RepairStationController s, GameObject setup, TwoNightCounterDirector c,
            TwoNightRobotDirector r, FirstOrderFlow f, FirstOrderInput i, SliceView v,
            TwoNightEconomy e, Font uiFont, Camera room, Transform receive, Transform work,
            Transform delivery, Transform consoleCamera)
        {
            station = s; consoleSetup = setup; counter = c; robot = r; flow = f;
            robotInput = i; robotView = v; economy = e; font = uiFont; roomCamera = room;
            receiveAnchor = receive; workAnchor = work; deliveryAnchor = delivery; consoleCameraAnchor = consoleCamera;
        }

        void Awake()
        {
            if (State == null || State.phase == TwoNightPhase.None) TwoNightRun.NewGame(economy, true);
            if (State.phase == TwoNightPhase.Night1Counter && State.customerTrades != null && State.customerTrades.Count > 0)
            {
                // Only night-end is persisted. Reset receipts and session together on room reentry.
                TwoNightRun.NewGame(economy, true);
                LastMessage = "未保存的营业进度已重置，本晚从第一件重新开始。";
            }
            State.unifiedClinic = true;
            if (State.communicatorSettled) State.communicatorTrade = ClinicTradeState.Delivered;
            bool customerNight = State.phase == TwoNightPhase.Night1Counter || State.phase == TwoNightPhase.Night1Ledger;
            consoleSetup.SetActive(customerNight);
            counter.gameObject.SetActive(customerNight);
            station.Inspector.enabled = false;
            station.Scanner.enabled = false;
            station.View.gameObject.SetActive(false);
            station.Inspector.ViewCamera.enabled = false;
            SetRobotActive(!customerNight);
            if (customerNight)
            {
                if (flow.Rig.FirstPersonEnabled) flow.Rig.Walk(true);
                else flow.Rig.Go(FirstOrderCameraRig.Overview, true);
            }
        }

        IEnumerator Start()
        {
            BuildUi();
            if (flow.Rig.Walker != null)
                flow.Rig.Walker.CursorRequests.Add(() => ConsoleOpen || counter.LedgerVisible ||
                    counter.RetryVisible || counter.DialogueVisible);
            // The original station and dock initialize through their own Start methods.
            yield return null;
            if (State.phase == TwoNightPhase.Night1Counter || State.phase == TwoNightPhase.Night1Ledger)
            {
                station.enabled = false;
                var item = station.Inspector.CurrentItem;
                if (item == null || station.Session == null)
                { Debug.LogError("[UnifiedClinic] Prototype console failed to initialize.", this); yield break; }
                State.customerQueueCount = station.Session.CaseCount;
                CacheItemPose();
                if (HasPendingCase) PlaceItem(receiveAnchor);
                else station.Inspector.Clear();
                // Walker Awake runs after this director's early Awake. Select walking once all actors are ready.
                if (flow.Rig.FirstPersonEnabled) flow.Rig.Walk(true);
            }
            Initialized = true;
            RefreshStatus();
        }

        void SetRobotActive(bool active)
        {
            robotInput.enabled = active && State.phase != TwoNightPhase.Night2Open;
            flow.enabled = active;
            flow.InspectionLocked = true;
            flow.Rig.enabled = active || flow.Rig.Walker != null;
            robotView.gameObject.SetActive(active);
            robot.gameObject.SetActive(active);
            roomCamera.enabled = true;
        }

        public bool Interact(ClinicTradeAction action)
        {
            if (!Initialized) return false;
            if (State.phase != TwoNightPhase.Night1Counter) return Refuse("没有待接收的下一单。七号是内部工单。");
            switch (action)
            {
                case ClinicTradeAction.Receive:
                    if (!HasPendingCase) return Refuse("当前物品仍在处理中，不能再接收。");
                    if (State.ActiveTrade != null)
                    {
                        if (!station.Session.NextCase()) return Refuse("下一单暂时不能接收。");
                        if (pendingPreview != null) Destroy(pendingPreview);
                        CacheItemPose();
                    }
                    var data = station.Session.CurrentCase;
                    // Only the collector has a parts-cost contract. Other authored cases use their existing fee.
                    int income = station.Session.CaseIndex == 0 ? State.communicatorIncome : data.estimatedRepairCost;
                    int partsCost = station.Session.CaseIndex == 0 ? State.communicatorPartsCost : 0;
                    if (!TwoNightRun.ReceiveCustomer(State, data.caseId, station.Session.CaseIndex, income, partsCost))
                        return Refuse("当前物品仍在处理中，不能再接收。");
                    RestoreItem();
                    station.Session.AcceptItem();
                    OpenConsole();
                    counter.CustomerReceived();
                    return true;
                case ClinicTradeAction.Deliver:
                    if (!TwoNightRun.DeliverCustomer(State, station.Session.CurrentCase.caseId))
                        return Refuse(station.Session.CurrentCase.itemName + "尚未修好，不能交付。");
                    ShowRoom();
                    PlaceItem(deliveryAnchor);
                    station.Inspector.Clear();
                    if (State.phase == TwoNightPhase.Night1Ledger) counter.DeliveryCompleted();
                    else CreatePendingPreview();
                    RefreshStatus();
                    return true;
                case ClinicTradeAction.Console:
                    if (TradeState != ClinicTradeState.InRepair) return Refuse("控制台当前没有待维修物品。");
                    OpenConsole();
                    return true;
                default: return false;
            }
        }

        public void OpenConsole()
        {
            if (!Initialized || State.phase != TwoNightPhase.Night1Counter || TradeState != ClinicTradeState.InRepair) return;
            RestoreItem();
            ConsoleOpen = true;
            LastMessage = null;
            flow.Rig.enabled = false;
            roomCamera.enabled = false;
            station.Inspector.ViewCamera.enabled = true;
            station.View.gameObject.SetActive(true);
            station.enabled = station.Inspector.enabled = station.Scanner.enabled = true;
            RefreshStatus();
        }

        public void ShowRoom()
        {
            ConsoleOpen = false;
            station.enabled = station.Inspector.enabled = station.Scanner.enabled = false;
            station.View.gameObject.SetActive(false);
            station.Inspector.ViewCamera.enabled = false;
            if (TradeState == ClinicTradeState.InRepair) PlaceItem(workAnchor);
            roomCamera.enabled = true;
            flow.Rig.enabled = true;
            if (flow.Rig.FirstPersonEnabled) flow.Rig.Walk(true);
            else flow.Rig.Go(FirstOrderCameraRig.Overview, true);
            RefreshStatus();
        }

        public void RepairCompleted()
        {
            counter.DismissDialogue();
            if (station.Session.Stage == RepairStage.Result && station.Session.IsLastCase) station.Session.NextCase();
            ShowRoom();
            PlaceItem(deliveryAnchor);
            LastMessage = station.Session.CurrentCase.itemName + (State.ActiveTrade.returnedUnpaid ?
                "：" + BorderRepair.Data.RepairDecisionText.Label(State.ActiveTrade.decision) + "，等待无偿退回。" : "已修好，等待交付。");
            RefreshStatus();
        }

        void PlaceItem(Transform anchor)
        {
            var item = station.Inspector.CurrentItem;
            if (item == null) return;
            item.transform.SetParent(anchor, true);
            item.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            SeatOnSupport(item.gameObject, anchor);
        }

        void SeatOnSupport(GameObject item, Transform anchor)
        {
            var renderers = item.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0) return;
            float bottom = renderers.Min(r => r.bounds.min.y);
            item.transform.position += Vector3.up * (TradeSupportWorldY(anchor) - bottom);
        }

        void RestoreItem()
        {
            var item = station.Inspector.CurrentItem;
            if (item == null) return;
            item.transform.SetParent(itemParent, false);
            item.transform.localPosition = itemLocalPosition;
            item.transform.localRotation = itemLocalRotation;
            item.transform.localScale = itemLocalScale;
            station.Inspector.ResetView(true);
        }

        void CacheItemPose()
        {
            var item = station.Inspector.CurrentItem;
            itemParent = item.transform.parent;
            itemLocalPosition = item.transform.localPosition;
            itemLocalRotation = item.transform.localRotation;
            itemLocalScale = item.transform.localScale;
        }

        void CreatePendingPreview()
        {
            if (!HasPendingCase) return;
            var next = station.Session.Shift.cases[station.Session.CaseIndex + 1];
            if (next.itemPrefab == null) return;
            pendingPreview = Instantiate(next.itemPrefab, receiveAnchor);
            pendingPreview.transform.SetPositionAndRotation(receiveAnchor.position, receiveAnchor.rotation);
            var renderers = pendingPreview.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            if (bounds.extents.magnitude > 0) pendingPreview.transform.localScale *= station.Inspector.ItemRadius / bounds.extents.magnitude;
            SeatOnSupport(pendingPreview, receiveAnchor);
        }

        public void BeginRobotNight()
        {
            if (State.phase != TwoNightPhase.Night1Incident) return;
            ShowRoom();
            consoleSetup.SetActive(false);
            counter.gameObject.SetActive(false);
            SetRobotActive(true);
            tradeUi.SetActive(false);
        }

        bool Refuse(string message) { LastMessage = message; RefreshStatus(); return false; }

        void Update()
        {
            if (crosshair != null)
                crosshair.SetActive(RoomWalking && flow.Rig.Walker.Aiming);
            if (!Initialized || State == null || ConsoleOpen || State.phase != TwoNightPhase.Night1Counter ||
                counter.LedgerVisible || counter.RetryVisible || counter.DialogueVisible || !RepairInput.LeftPressed) return;
            var walker = flow.Rig.Walker;
            bool walking = flow.Rig.Walking;
            if (walking && (!walker.Aiming || walker.ClickConsumedThisFrame) ||
                !walking && RepairInput.PointerOverUI) return;
            Vector2 pointer = walking ? new Vector2(Screen.width * .5f, Screen.height * .5f) : RepairInput.PointerPosition;
            foreach (var hit in Physics.RaycastAll(roomCamera.ScreenPointToRay(pointer), 8, ~0,
                QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            {
                if (hit.collider is CharacterController || hit.collider.GetComponent<FirstPersonBlocker>() != null) continue;
                var zone = hit.collider.GetComponentInParent<ClinicTradeZone>();
                if (zone != null)
                {
                    if (walking && hit.distance > walker.Reach) Refuse("够不着交易区物品，请走近一点。");
                    else zone.Activate();
                    break;
                }
                if (!hit.collider.isTrigger) break;
            }
        }

        void RefreshStatus()
        {
            if (status == null) return;
            string task = HasPendingCase ? "下一件送修物待接收" :
                TradeState == ClinicTradeState.InRepair ? "当前物品维修中" :
                TradeState == ClinicTradeState.ReadyForReturn ? "当前物品待无偿退回（不记维修收入）" :
                TradeState == ClinicTradeState.ReadyForDelivery ? "当前物品待交付（尚未入账）" : "没有待接收的下一单";
            status.text = $"第 {State.night} 晚　现金 {TwoNightUi.Money(State.Cash)}\n{task}\n{LastMessage}";
            status.transform.parent.gameObject.SetActive(!ConsoleOpen);
            consoleButton.gameObject.SetActive(!ConsoleOpen && TradeState == ClinicTradeState.InRepair);
            roomButton.gameObject.SetActive(ConsoleOpen);
        }

        void BuildUi()
        {
            tradeUi = new GameObject("UnifiedClinicTradeUI");
            tradeUi.transform.SetParent(transform, false);
            TwoNightUi.Canvas(tradeUi, 30);
            crosshair = new GameObject("TradeCrosshair", typeof(RectTransform), typeof(Image));
            crosshair.transform.SetParent(tradeUi.transform, false);
            var crosshairRect = (RectTransform)crosshair.transform;
            crosshairRect.anchorMin = crosshairRect.anchorMax = crosshairRect.pivot = new Vector2(.5f, .5f);
            crosshairRect.sizeDelta = new Vector2(6, 6);
            crosshair.GetComponent<Image>().raycastTarget = false;
            crosshair.GetComponent<Image>().color = new Color(.95f, .93f, .88f, .85f);
            var panel = TwoNightUi.Panel(tradeUi.transform, "TradeStatus", new Vector2(1, 1), new Vector2(-416, -16), new Vector2(400, 120));
            panel.GetComponent<Image>().raycastTarget = false;
            status = TwoNightUi.Label(panel, "Status", "", font, 18, new Vector2(14, -10), new Vector2(372, 100), TwoNightUi.TextMain);
            consoleButton = TwoNightUi.Button(tradeUi.transform, "Console", "维修控制台", font, new Vector2(16, -16), new Vector2(170, 44), OpenConsole);
            roomButton = TwoNightUi.Button(tradeUi.transform, "Room", "返回诊所", font, new Vector2(16, -16), new Vector2(170, 44), ShowRoom);
            TwoNightUi.Button(tradeUi.transform, "Menu", "主菜单", font, new Vector2(16, -70), new Vector2(170, 44), () => SceneManager.LoadScene(TwoNightScenes.ClinicMenu));
            tradeUi.SetActive(State.phase == TwoNightPhase.Night1Counter || State.phase == TwoNightPhase.Night1Ledger);
        }
    }
}
