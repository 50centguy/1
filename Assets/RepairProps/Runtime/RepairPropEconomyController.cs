using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.TwoNight;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BorderRepair.RepairProps
{
    [DefaultExecutionOrder(-300)]
    public sealed class RepairPropEconomyController : MonoBehaviour
    {
        public const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        public UnifiedClinicDirector Clinic { get; private set; }
        public TwoNightState State => Clinic != null ? Clinic.State : null;
        public bool IsOpen { get; private set; }
        public string Message { get; private set; } = "";
        public readonly Dictionary<string, int> Cart = new Dictionary<string, int>();
        public event Action Changed;
        string requestId = Guid.NewGuid().ToString("N");
        bool oldInteraction, oldCleaning;
        Func<bool> cursorRequest;
        RepairPropShopPanel panel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        { SceneManager.sceneLoaded -= Loaded; SceneManager.sceneLoaded += Loaded; }
        static void Loaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != ScenePath || scene.GetRootGameObjects().Any(g => g.GetComponentInChildren<RepairPropEconomyController>(true) != null)) return;
            var root = new GameObject("PartsProcurement_Runtime");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<RepairPropEconomyController>();
        }
        void Awake()
        {
            Clinic = FindObjectsByType<UnifiedClinicDirector>(FindObjectsSortMode.None).FirstOrDefault(c => c.gameObject.scene == gameObject.scene);
            if (State != null && State.supplies == null) State.supplies = new ClinicSupplyState();
            if (State != null) { State.supplyRulesVersion = 1; State.legacyFreeSupplies = false; }
        }
        void Start()
        {
            if (Clinic == null) { enabled = false; return; }
            cursorRequest = () => IsOpen;
            Clinic.Robot.Flow.Rig.Walker?.CursorRequests.Add(cursorRequest);
            panel = gameObject.AddComponent<RepairPropShopPanel>();
            panel.Initialize(this);
        }
        public static void TryBind(RepairPropFlow flow)
        {
            if (!Application.isPlaying || flow.gameObject.scene.path != ScenePath) return;
            var controller = FindObjectsByType<RepairPropEconomyController>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.gameObject.scene == flow.gameObject.scene);
            if (controller?.State?.supplies == null || flow.Session != controller.Clinic.Station.Session) return;
            flow.CommandPolicy = new RepairPropSupplyPolicy(controller.State, flow.Definition.repairCase.caseId, flow.Definition.kind);
        }
        public string RecommendedSku
        {
            get
            {
                var item = Clinic != null ? Clinic.Station.Inspector.CurrentItem : null;
                var flow = item != null ? item.GetComponent<RepairPropFlow>() : null;
                return flow != null ? RepairPropSupplyPolicy.SkuFor(flow.Definition.kind) : null;
            }
        }
        public bool CanOpen => Clinic != null && Clinic.Initialized && State?.supplies != null &&
            State.phase == TwoNightPhase.Night1Counter && !Clinic.Carrying && !Clinic.Counter.DialogueVisible &&
            !Clinic.Counter.LedgerVisible && !Clinic.Counter.RetryVisible && !Clinic.Station.InputLocked &&
            (!Clinic.Station.CleaningActive || IsOpen);
        public bool Open()
        {
            if (IsOpen) return true;
            if (!CanOpen) return false;
            var station = Clinic.Station;
            oldInteraction = station.Inspector.InteractionEnabled;
            oldCleaning = station.CleaningActive;
            station.CleaningActive = true;
            station.Inspector.InteractionEnabled = false;
            IsOpen = true;
            Message = "";
            Changed?.Invoke();
            return true;
        }
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (Clinic != null && Clinic.Station != null)
            {
                Clinic.Station.CleaningActive = oldCleaning;
                Clinic.Station.Inspector.InteractionEnabled = oldInteraction;
            }
            Changed?.Invoke();
        }
        public void SetQuantity(string sku, int quantity)
        {
            if (ClinicSupplies.Find(sku) == null) return;
            quantity = Mathf.Clamp(quantity, 0, ClinicSupplies.MaxPerPurchase);
            if (quantity == 0) Cart.Remove(sku); else Cart[sku] = quantity;
            requestId = Guid.NewGuid().ToString("N"); Message = ""; Changed?.Invoke();
        }
        public bool Purchase()
        {
            if (!IsOpen || !CanOpen) return false;
            bool ok = ClinicSupplies.Purchase(State, Cart, requestId, out var reason);
            Message = reason;
            if (ok) { Cart.Clear(); requestId = Guid.NewGuid().ToString("N"); }
            if (ok) Clinic.RefreshEconomyStatus();
            Changed?.Invoke(); return ok;
        }
        public bool Refund(string sku)
        {
            if (!IsOpen || !CanOpen) return false;
            bool ok = ClinicSupplies.Refund(State, sku, 1, Guid.NewGuid().ToString("N"), out var reason);
            if (ok) Clinic.RefreshEconomyStatus();
            Message = reason; Changed?.Invoke(); return ok;
        }
        void Update()
        {
            if (IsOpen && (State == null || State.phase != TwoNightPhase.Night1Counter || Clinic.Carrying)) Close();
            panel?.Refresh();
        }
        void OnDisable() => Close();
        void OnDestroy()
        {
            Close();
            if (Clinic != null && Clinic.Robot != null) Clinic.Robot.Flow.Rig.Walker?.CursorRequests.Remove(cursorRequest);
        }
    }
}
