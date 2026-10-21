using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Data;
using BorderRepair.FirstOrder;
using BorderRepair.Inspection;
using BorderRepair.RepairProps.Cosmetics;
using BorderRepair.TwoNight;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BorderRepair.RepairProps.VR
{
    [DefaultExecutionOrder(1000)]
    public sealed class ClinicVrSession : MonoBehaviour
    {
        public UnifiedClinicDirector clinic;
        public XROrigin origin;
        public NearFarInteractor rightHand;
        public VrClinicPanel panel;
        public bool Ready { get; private set; }
#if UNITY_EDITOR
        // Explicit test fixture opt-in, absent from PC player builds. Never an automatic no-headset fallback.
        public static bool EditorSimulationForTests;
#endif
        public string Feedback { get; private set; }
        public string SelectedTarget { get; private set; }
        public RepairPropFlow ActiveFlow { get; private set; }
        public CosmeticController Cosmetics { get; private set; }
        public RepairActionType? Tool => clinic.Station.ActiveTool;
        public bool CanWork => Ready && clinic.Initialized && ActiveFlow != null &&
            clinic.Station.Inspector.CurrentItem == ActiveFlow.gameObject &&
            VrSafetyPolicy.CanWork(clinic.ConsoleOpen && clinic.TradeState == ClinicTradeState.InRepair,
                clinic.Carrying, ActiveFlow.Session == clinic.Station.Session,
                clinic.Station.InputLocked || clinic.Station.CleaningActive, ActiveFlow.Completed,
                Vector3.Distance(origin.Camera.transform.position, clinic.WorkAnchor.position));

        readonly Dictionary<Behaviour, bool> desktopStates = new Dictionary<Behaviour, bool>();
        readonly Dictionary<Collider, bool> desktopColliders = new Dictionary<Collider, bool>();
        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        InputAction trigger;
        GameObject currentItem;
        bool ownsLoader;
        bool ownsSubsystems;
        bool claimedPresentation;
        bool rebuildPanel;
        bool ending;

        IEnumerator Start()
        {
            if (clinic == null || origin == null || rightHand == null || panel == null)
            { Fail("VR scene references are missing."); yield break; }
            float deadline = Time.realtimeSinceStartup + 15f;
            while (!clinic.Initialized && Time.realtimeSinceStartup < deadline) yield return null;
            if (!clinic.Initialized) { Fail("Clinic initialization timed out."); yield break; }
            bool simulate = false;
#if UNITY_EDITOR
            simulate = EditorSimulationForTests;
#endif
            if (!simulate)
            {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null) { Fail("Configure Standalone OpenXR before opening the VR scene."); yield break; }
            if (manager.activeLoader == null)
            {
                ownsLoader = true;
                yield return manager.InitializeLoader();
            }
            if (ending) yield break;
            if (manager.activeLoader == null) { Fail("No active OpenXR runtime; desktop presentation was left unchanged."); yield break; }
            SubsystemManager.GetSubsystems(displays);
            if (!displays.Any(d => d.running)) { manager.StartSubsystems(); ownsSubsystems = true; }
            deadline = Time.realtimeSinceStartup + 5f;
            do
            {
                SubsystemManager.GetSubsystems(displays);
                if (displays.Any(d => d.running)) break;
                yield return null;
            } while (Time.realtimeSinceStartup < deadline);
            if (!displays.Any(d => d.running)) { Fail("OpenXR display did not start; connect the headset and active PC runtime."); yield break; }
            }
            origin.gameObject.SetActive(true);
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!camera.transform.IsChildOf(origin.transform)) Remember(camera);
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!listener.transform.IsChildOf(origin.transform)) Remember(listener);
            foreach (var rig in FindObjectsByType<FirstOrderCameraRig>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Remember(rig);
            foreach (var walker in FindObjectsByType<FirstPersonWalker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Remember(walker);
                var collider = walker.GetComponent<CharacterController>();
                if (collider != null) desktopColliders.Add(collider, collider.enabled);
            }
            foreach (var input in FindObjectsByType<FirstOrderInput>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Remember(input);
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!canvas.transform.IsChildOf(origin.transform)) Remember(canvas);
            Remember(clinic.Station.Inspector);
            Remember(clinic.Station.Scanner);
            Remember(clinic.Station);
            Remember(clinic);
            claimedPresentation = true;
            trigger = new InputAction("RepairActivate", InputActionType.Button, "<XRController>{RightHand}/triggerPressed");
            trigger.Enable();
            Cosmetics = FindFirstObjectByType<CosmeticController>();
            if (Cosmetics != null) Cosmetics.BindVR(() => Ready && !rightHand.hasSelection &&
                InputSystem.devices.OfType<UnityEngine.InputSystem.XR.XRController>().Any(d =>
                    d.usages.Contains(UnityEngine.InputSystem.CommonUsages.RightHand) && d.isTracked.isPressed) &&
                Vector3.Distance(origin.Camera.transform.position, clinic.WorkAnchor.position) <= 1.25f);
            panel.Initialize(this);
            Ready = true;
            Feedback = simulate ? "EDITOR SIMULATION - NO XR DEVICE" : "PC VR";
            rebuildPanel = true;
        }

        void Remember(Behaviour component)
        {
            if (component != null && !desktopStates.ContainsKey(component)) desktopStates.Add(component, component.enabled);
        }

        void Update()
        {
            if (!Ready) return;
            var item = clinic.Station.Inspector.CurrentItem;
            if (item != currentItem)
            {
                if (ActiveFlow != null) ActiveFlow.Changed -= OnFlowChanged;
                currentItem = item;
                ActiveFlow = item != null ? item.GetComponent<RepairPropFlow>() : null;
                if (ActiveFlow != null)
                {
                    ActiveFlow.Changed += OnFlowChanged;
                    SelectedTarget = ActiveFlow.Definition.rootObjectName;
                }
                rebuildPanel = true;
            }
            if (item != null && clinic.CarriedItem == item && item.GetComponent<VrCarryItem>() == null)
                item.AddComponent<VrCarryItem>().Configure(this);
            if (Cosmetics != null && Cosmetics.Mode != CosmeticMode.Off)
            {
                var aim = rightHand.curveOrigin != null ? rightHand.curveOrigin : rightHand.transform;
                Cosmetics.ProcessRay(new Ray(aim.position, aim.forward), trigger.IsPressed(),
                    rightHand.hasSelection || rightHand.TryGetCurrentUIRaycastResult(out _), 1.25f, origin.transform);
            }
            if ((Cosmetics == null || Cosmetics.Mode == CosmeticMode.Off) && trigger.WasPressedThisFrame() && CanWork && !rightHand.hasSelection &&
                !rightHand.TryGetCurrentUIRaycastResult(out _)) PickPart();
            if (rebuildPanel) { rebuildPanel = false; panel.Rebuild(); }
            panel.Refresh();
        }

        void LateUpdate()
        {
            if (!Ready) return;
            // The legacy director still owns seating and accounting, not the HMD camera.
            foreach (var bridge in FindObjectsByType<RepairPropStationBridge>(FindObjectsSortMode.None))
                if (bridge.Flow.Session != null) Remember(bridge);
            foreach (var entry in desktopStates) if (entry.Key != null) entry.Key.enabled = false;
            foreach (var entry in desktopColliders) if (entry.Key != null) entry.Key.enabled = false;
            SeatMountedItem();
            origin.Camera.enabled = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Receive()
        {
            if (!Ready || Vector3.Distance(origin.Camera.transform.position, clinic.ReceiveAnchor.position) > 1.5f) return;
            if (!clinic.Interact(ClinicTradeAction.Receive)) { SetFeedback(clinic.LastMessage); return; }
            clinic.Counter.DismissDialogue();
            ReturnToIntake(clinic.CarriedItem);
            rebuildPanel = true;
        }

        public void Deliver()
        {
            if (!Ready || Vector3.Distance(origin.Camera.transform.position, clinic.DeliveryAnchor.position) > 1.5f) return;
            if (!clinic.Interact(ClinicTradeAction.Deliver)) SetFeedback(clinic.LastMessage);
            rebuildPanel = true;
        }

        public void ReturnToIntake(GameObject item)
        {
            if (item == null || clinic.CarriedItem != item) return;
            item.transform.SetParent(clinic.ReceiveAnchor, true);
            item.transform.SetPositionAndRotation(clinic.ReceiveAnchor.position, clinic.ReceiveAnchor.rotation);
            var visible = item.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (visible.Length > 0)
                item.transform.position += Vector3.up * (clinic.TradeSupportWorldY(clinic.ReceiveAnchor) - visible.Min(r => r.bounds.min.y));
        }

        public bool TryDock(GameObject item)
        {
            if (!Ready || item == null || clinic.CarriedItem != item) return false;
            var renderers = item.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0) return false;
            var center = renderers[0].bounds.center;
            var delta = center - clinic.WorkAnchor.position;
            if (!VrSafetyPolicy.CanDock(new Vector2(delta.x, delta.z).magnitude,
                renderers.Min(r => r.bounds.min.y) - clinic.BenchTopWorldY)) return false;
            bool docked = clinic.PlaceOnBench();
            if (docked) item.GetComponent<VrCarryItem>()?.Suspend();
            rebuildPanel = true;
            return docked;
        }

        public void SelectTool(RepairActionType? tool)
        {
            if (!CanWork) return;
            clinic.Station.SelectTool(tool);
            rebuildPanel = true;
        }

        public void SelectTarget(string target)
        {
            if (!CanWork || ActiveFlow.Definition.FindNode(target) == null) return;
            SelectedTarget = target;
            rebuildPanel = true;
        }

        public bool Execute(string command)
        {
            if (!CanWork || !Tool.HasValue) return false;
            bool accepted = ActiveFlow.TryExecute(command, SelectedTarget, Tool.Value);
            SetFeedback(ActiveFlow.LastFeedback);
            rebuildPanel = true;
            return accepted;
        }

        public void Inspect()
        {
            if (!CanWork) return;
            ActiveFlow.TryInspect(SelectedTarget);
            SetFeedback(ActiveFlow.LastFeedback);
        }

        void PickPart()
        {
            Physics.SyncTransforms();
            var aim = rightHand.curveOrigin != null ? rightHand.curveOrigin : rightHand.transform;
            var ray = new Ray(aim.position, aim.forward);
            foreach (var hit in Physics.RaycastAll(ray, 1.25f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (hit.transform.IsChildOf(origin.transform)) continue;
                var parked = hit.transform.GetComponentInParent<RepairPropParkedPart>();
                if (parked != null && parked.Owner == ActiveFlow) { SelectTarget(parked.Target); return; }
                if (!hit.transform.IsChildOf(ActiveFlow.transform)) break;
                var t = hit.transform;
                while (t != null)
                {
                    if (ActiveFlow.Definition.FindNode(t.name) != null) { SelectTarget(t.name); return; }
                    if (t == ActiveFlow.transform) break;
                    t = t.parent;
                }
                SelectTarget(ActiveFlow.Definition.rootObjectName);
                return;
            }
        }

        void OnFlowChanged() => rebuildPanel = true;

        void SeatMountedItem()
        {
            if (clinic.Carrying || currentItem == null || !clinic.InspectionOnBench ||
                currentItem.transform.parent != clinic.Station.Inspector.ItemAnchor) return;
            var visible = currentItem.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (visible.Length == 0) return;
            float bottom;
            if (!clinic.Station.Inspector.TryGetStableSupportBottom(out bottom)) bottom = visible.Min(r => r.bounds.min.y);
            float delta = clinic.BenchTopWorldY + .0005f - bottom;
            if (Mathf.Abs(delta) > .0001f) clinic.Station.Inspector.ItemAnchor.parent.position += Vector3.up * delta;
        }
        public void SetFeedback(string message) { Feedback = message; }
        void Fail(string message) { Debug.LogError("[RepairPropsVR] " + message, this); Shutdown(); }
        void OnDisable() { StopAllCoroutines(); Shutdown(); }

        void Shutdown()
        {
            if (ending) return;
            ending = true;
            Ready = false;
            if (Cosmetics != null) Cosmetics.UnbindVR();
            if (panel != null) panel.SetVisible(false);
            foreach (var item in FindObjectsByType<VrCarryItem>(FindObjectsSortMode.None)) item.Suspend();
            if (ActiveFlow != null) ActiveFlow.Changed -= OnFlowChanged;
            trigger?.Dispose();
            trigger = null;
            if (claimedPresentation)
            {
                foreach (var entry in desktopStates) if (entry.Key != null) entry.Key.enabled = entry.Value;
                foreach (var entry in desktopColliders) if (entry.Key != null) entry.Key.enabled = entry.Value;
            }
            if (origin != null) origin.gameObject.SetActive(false);
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager != null && ownsSubsystems) manager.StopSubsystems();
            if (manager != null && ownsLoader && manager.activeLoader != null) manager.DeinitializeLoader();
        }
    }
}
