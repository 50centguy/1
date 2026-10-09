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
        [Tooltip("维修台前玩家站的位置（ArtSource/UnifiedClinic/anchors.csv 的 Workbench：台前、面朝北）。台上维修的镜头从这一侧看物件。")]
        [SerializeField] Vector3 benchStandPoint = new Vector3(0f, 0f, 1.95f);
        [Tooltip("拿在手上的物件中心相对眼睛的位置（右、上、前，米）")]
        [SerializeField] Vector3 carryLocalPosition = new Vector3(0.12f, -0.30f, 0.55f);

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
        public string WholeEngineLog { get; private set; }
        public Vector3 BenchStandPoint => benchStandPoint;
        public bool ConsoleOpen { get; private set; }
        public string LastMessage { get; private set; }
        public bool RoomWalking => !ConsoleOpen && flow.Rig.Walking;
        /// <summary>手上拿着从收件台接收的物件（还没放到维修台）。</summary>
        public bool Carrying => carried != null;
        public GameObject CarriedItem => carried;
        /// <summary>原型检视台（物件锚点、检视相机、检视灯）已经挪到房间维修台上：在台上修，不再是房间外的黑底舞台。</summary>
        public bool InspectionOnBench => stageOnBench;
        public float BenchTopWorldY => benchTopY;
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
        GameObject carried;
        Light[] inspectionLights = new Light[0];
        float[] inspectionLightIntensity = new float[0];
        Renderer receiveLamp, deliverLamp;
        MaterialPropertyBlock lampBlock;
        ClinicTradeZone benchZone;
        Collider[] carriedColliders;
        bool[] carriedColliderEnabled;
        Transform inspectionStage;
        bool stageOnBench;
        float benchTopY;

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
            if (customerNight) MoveInspectionToBench();          // 要在检视台自己的 Awake（按相机与锚点的相对位置定视线）之前
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
            // 交易台状态灯（美术说明：EMPTY 灭、CARGO 亮、READY 闪，由程序驱动发光）
            receiveLamp = FindLamp("Trade_StatusLamp_Receive");
            deliverLamp = FindLamp("Trade_StatusLamp_Deliver");
            lampBlock = new MaterialPropertyBlock();
            benchZone = FindObjectsByType<ClinicTradeZone>(FindObjectsSortMode.None).FirstOrDefault(z => z.Action == ClinicTradeAction.Console);
            if (benchZone != null) benchZone.SetLabel("WORKBENCH");
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
            if (State.phase == TwoNightPhase.Night2Open)
            {
                // 第二晚：整个左引擎卸下来抱到维修台上修（不再让零件自己飞到工作台）
                WholeEngineLog = ClinicWholeEngine.Enable(flow, workAnchor, benchStandPoint);
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
            // 顾客夜七号流程不启用，它的初始化（会隐藏工作台落点标记）不执行：这里替它隐藏，免得操作垫上一直有一块发光的黄色标记
            if (!active) foreach (var zone in FindObjectsByType<FirstOrderDropZone>(FindObjectsSortMode.None)) zone.ShowMarker(false);
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
                    if (carried != null) return Refuse("手上已经拿着" + station.Session.CurrentCase.itemName + "，先放到维修台上。");
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
                    counter.CustomerReceived();
                    if (flow.Rig.Walker != null)
                    {
                        // 从收件台拿起来；要拿到维修台上放下才开始维修（以前这里直接进维修）
                        PickUp();
                        LastMessage = "拿起了" + data.itemName + "。走到维修台，对准台面左键放下，开始维修。";
                        RefreshStatus();
                        return true;
                    }
                    station.Session.AcceptItem();                // 没有第一人称的场景：保持原来的“收件即开修”
                    OpenConsole();
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
                    if (carried != null) return PlaceOnBench();
                    if (TradeState != ClinicTradeState.InRepair) return Refuse("维修台上没有待维修的物品。先到收件台拿一件。");
                    OpenConsole();
                    return true;
                default: return false;
            }
        }

        public void OpenConsole()
        {
            if (!Initialized || State.phase != TwoNightPhase.Night1Counter || TradeState != ClinicTradeState.InRepair || carried != null) return;
            RestoreItem();
            ConsoleOpen = true;
            for (int i = 0; i < inspectionLights.Length; i++) { inspectionLights[i].intensity = inspectionLightIntensity[i]; inspectionLights[i].enabled = true; }
            if (benchZone != null) benchZone.ShowLabel(false);     // 维修时镜头就在台前，朝向镜头的标签会挡住画面
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
            foreach (var l in inspectionLights) l.enabled = false;
            if (benchZone != null) benchZone.ShowLabel(true);
            station.enabled = station.Inspector.enabled = station.Scanner.enabled = false;
            station.View.gameObject.SetActive(false);
            station.Inspector.ViewCamera.enabled = false;
            if (TradeState == ClinicTradeState.InRepair && carried == null && !stageOnBench) PlaceItem(workAnchor);   // 台上维修时物件本来就在台上，原地不动
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

        /// <summary>
        /// 把原型检视台（物件锚点 + 检视相机 + 两盏检视灯）从房间外的黑底舞台挪到维修台台面上，镜头从台前玩家一侧看，
        /// 背景、可见层、远裁剪和房间相机一致——维修时看到的是台上的物件和房间，不再是纯黑背景。
        /// </summary>
        void MoveInspectionToBench()
        {
            var anchor = station != null ? station.Inspector.ItemAnchor : null;
            if (anchor == null || anchor.parent == null || workAnchor == null || roomCamera == null) return;
            inspectionStage = anchor.parent;
            var cam = station.Inspector.ViewCamera;
            var from = Vector3.ProjectOnPlane(cam.transform.position - anchor.position, Vector3.up);
            var to = Vector3.ProjectOnPlane(benchStandPoint - workAnchor.position, Vector3.up);
            if (from.sqrMagnitude > 1e-6f && to.sqrMagnitude > 1e-6f)
                inspectionStage.rotation = Quaternion.FromToRotation(from.normalized, to.normalized) * inspectionStage.rotation;
            benchTopY = TradeSupportWorldY(workAnchor);
            inspectionStage.position += new Vector3(workAnchor.position.x, benchTopY + 0.25f, workAnchor.position.z) - anchor.position;
            cam.clearFlags = roomCamera.clearFlags;
            cam.backgroundColor = roomCamera.backgroundColor;
            cam.cullingMask = roomCamera.cullingMask;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, roomCamera.farClipPlane);
            // 检视灯跟着到了台上：只在台上维修时开，且减弱（原来是给黑底舞台照物件用的，放在房间里会把维修台照得比别处亮很多）
            inspectionLights = inspectionStage.GetComponentsInChildren<Light>(true);
            inspectionLightIntensity = inspectionLights.Select(l => l.intensity * .4f).ToArray();
            foreach (var l in inspectionLights) l.enabled = false;
            stageOnBench = true;
        }

        void PickUp()
        {
            var item = station.Inspector.CurrentItem;
            var eye = flow.Rig.Walker != null ? flow.Rig.Walker.Eye : null;
            if (item == null || eye == null) return;
            carried = item;
            carriedColliders = item.GetComponentsInChildren<Collider>(true);
            carriedColliderEnabled = carriedColliders.Select(c => c.enabled).ToArray();
            foreach (var c in carriedColliders) c.enabled = false;     // 拿在手上时不挡准星的射线
            item.transform.SetParent(eye, true);
            item.transform.localRotation = itemLocalRotation;
            var renderers = item.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            if (renderers.Length > 0)
            {
                var b = renderers[0].bounds; foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
                item.transform.position += eye.TransformPoint(carryLocalPosition) - b.center;
            }
            else item.transform.localPosition = carryLocalPosition;
        }

        /// <summary>把手上的物件放到维修台上，开始在台上维修。</summary>
        public bool PlaceOnBench()
        {
            if (carried == null) return false;
            for (int i = 0; i < carriedColliders.Length; i++) if (carriedColliders[i] != null) carriedColliders[i].enabled = carriedColliderEnabled[i];
            carried = null; carriedColliders = null; carriedColliderEnabled = null;
            RestoreItem();
            station.Session.AcceptItem();
            OpenConsole();
            return true;
        }

        void LateUpdate()
        {
            if (lampBlock != null) { DriveLamp(receiveLamp, ReceiveLampState); DriveLamp(deliverLamp, DeliverLampState); }
            // 台上的物件转到任何角度，最低点都贴着台面（不穿进桌子，也不悬空）。本组件执行顺序靠前，检视台随后按新的锚点放镜头。
            if (!stageOnBench || carried != null || station == null) return;
            var item = station.Inspector.CurrentItem;
            if (item == null || item.transform.parent != station.Inspector.ItemAnchor) return;
            var renderers = item.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0) return;
            float delta = benchTopY + 0.0005f - renderers.Min(r => r.bounds.min.y);
            if (Mathf.Abs(delta) > 1e-4f) inspectionStage.position += Vector3.up * delta;
        }

        static Renderer FindLamp(string name) =>
            FindObjectsByType<Renderer>(FindObjectsSortMode.None).FirstOrDefault(r => r.name == name);

        public enum PadLamp { Off, On, Blink }
        /// <summary>收件台：有物件等着接收 = 亮；交件台：修好 / 待退回的物件等着交出 = 闪；其余熄灭。</summary>
        public PadLamp ReceiveLampState => State != null && State.phase == TwoNightPhase.Night1Counter && HasPendingCase && carried == null ? PadLamp.On : PadLamp.Off;
        public PadLamp DeliverLampState => State != null && State.phase == TwoNightPhase.Night1Counter &&
            (TradeState == ClinicTradeState.ReadyForDelivery || TradeState == ClinicTradeState.ReadyForReturn) ? PadLamp.Blink : PadLamp.Off;

        void DriveLamp(Renderer lamp, PadLamp state)
        {
            if (lamp == null) return;
            bool lit = state == PadLamp.On || state == PadLamp.Blink && Mathf.Repeat(Time.unscaledTime, 1.2f) < .7f;
            var mat = lamp.sharedMaterial;
            var color = lit && mat != null && mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
            lamp.GetPropertyBlock(lampBlock);
            lampBlock.SetColor("_EmissionColor", color);
            lamp.SetPropertyBlock(lampBlock);
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
                var onBench = station.Inspector.CurrentItem;
                if (carried == null && onBench != null && TradeState == ClinicTradeState.InRepair && hit.transform.IsChildOf(onBench.transform))
                {
                    if (walking && hit.distance > walker.Reach) Refuse("够不着维修台上的物品，请走近一点。");
                    else OpenConsole();                          // 点台上的物件：回到台上维修
                    break;
                }
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
            string task = carried != null ? "手上：" + station.Session.CurrentCase.itemName + " → 放到维修台上" :
                HasPendingCase ? "下一件送修物待接收" :
                TradeState == ClinicTradeState.InRepair ? "当前物品维修中" :
                TradeState == ClinicTradeState.ReadyForReturn ? "当前物品待无偿退回（不记维修收入）" :
                TradeState == ClinicTradeState.ReadyForDelivery ? "当前物品待交付（尚未入账）" : "没有待接收的下一单";
            status.text = $"第 {State.night} 晚　现金 {TwoNightUi.Money(State.Cash)}\n{task}\n{LastMessage}";
            status.transform.parent.gameObject.SetActive(!ConsoleOpen);
            consoleButton.gameObject.SetActive(!ConsoleOpen && TradeState == ClinicTradeState.InRepair && carried == null);
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
            consoleButton = TwoNightUi.Button(tradeUi.transform, "Console", "回到维修台", font, new Vector2(16, -16), new Vector2(170, 44), OpenConsole);
            roomButton = TwoNightUi.Button(tradeUi.transform, "Room", "离开维修台", font, new Vector2(16, -16), new Vector2(170, 44), ShowRoom);
            TwoNightUi.Button(tradeUi.transform, "Menu", "主菜单", font, new Vector2(16, -70), new Vector2(170, 44), () => SceneManager.LoadScene(TwoNightScenes.ClinicMenu));
            tradeUi.SetActive(State.phase == TwoNightPhase.Night1Counter || State.phase == TwoNightPhase.Night1Ledger);
        }
    }
}
