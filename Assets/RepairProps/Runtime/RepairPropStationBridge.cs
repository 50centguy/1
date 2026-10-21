using System;
using System.Collections.Generic;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.Inspection;
using BorderRepair.UI;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    // Attached to each item prefab. Reuses the station's mounted item, toolbar and inspector click event.
    // The scanner's stock session dispatch is suppressed only for this mounted adapter's lifetime.
    [RequireComponent(typeof(RepairPropFlow))]
    public sealed class RepairPropStationBridge : MonoBehaviour
    {
        [SerializeField] bool showContextPanel = true;
        static readonly Dictionary<ItemScanner, RepairPropStationBridge> owners = new Dictionary<ItemScanner, RepairPropStationBridge>();
        RepairStationController station;
        RepairPropFlow flow;
        string selectedTarget;
        bool wasSuppressed;
        bool attached;
        public RepairPropBenchPresentation BenchPresentation { get; private set; }
        public RepairPropContextPanel ContextPanel { get; private set; }
        public RepairPropArOverlay ArOverlay { get; private set; }
        public RepairStationController Station => station;
        public string SelectedTarget => selectedTarget;
        public RepairPropFlow Flow => flow != null ? flow : GetComponent<RepairPropFlow>();
        public bool CanInteract
        {
            get
            {
                if (!attached || !isActiveAndEnabled || station == null || !station.isActiveAndEnabled
                    || station.Inspector == null || !station.Inspector.isActiveAndEnabled || !station.Inspector.InteractionEnabled
                    || station.Scanner == null || !station.Scanner.isActiveAndEnabled
                    || station.View == null || !station.View.isActiveAndEnabled
                    || station.Inspector.ViewCamera == null || !station.Inspector.ViewCamera.isActiveAndEnabled
                    || station.Session != flow.Session || station.Session.CurrentCase != flow.Definition.repairCase
                    || flow.Completed || station.Session.Stage != RepairStage.Inspect || station.InputLocked || station.CleaningActive)
                    return false;
                var item = station.Inspector.CurrentItem;
                return item != null && (transform == item.transform || transform.IsChildOf(item.transform));
            }
        }
        public bool ContextPanelVisible => showContextPanel && CanInteract;
        public RepairPropTestJournal TestJournal { get; private set; }
        // Presentation only: the same ownership checks as CanInteract, except that appearance editing's temporary
        // CleaningActive claim keeps the workspace on screen. Commands still require CanInteract.
        public bool WorkspaceVisible
        {
            get
            {
                if (!showContextPanel || !attached || !isActiveAndEnabled || station == null || !station.isActiveAndEnabled
                    || station.Inspector == null || !station.Inspector.isActiveAndEnabled || !station.Inspector.InteractionEnabled
                    || station.Scanner == null || !station.Scanner.isActiveAndEnabled
                    || station.View == null || !station.View.isActiveAndEnabled
                    || station.Inspector.ViewCamera == null || !station.Inspector.ViewCamera.isActiveAndEnabled
                    || station.Session != flow.Session || station.Session.CurrentCase != flow.Definition.repairCase
                    || flow.Completed || station.Session.Stage != RepairStage.Inspect)
                    return false;
                var item = station.Inspector.CurrentItem;
                return item != null && (transform == item.transform || transform.IsChildOf(item.transform));
            }
        }

        void Start()
        {
            flow = GetComponent<RepairPropFlow>();
            foreach (var candidate in FindObjectsByType<RepairStationController>(FindObjectsSortMode.None))
            {
                var item = candidate.Inspector != null ? candidate.Inspector.CurrentItem : null;
                if (item != null && (transform == item.transform || transform.IsChildOf(item.transform)))
                { Attach(candidate); return; }
            }
        }

        public void Attach(RepairStationController controller)
        {
            if (attached) return;
            station = controller;
            flow = GetComponent<RepairPropFlow>();
            if (station == null || station.Session == null || station.Scanner == null || station.Inspector == null)
                throw new ArgumentException("A configured repair station is required.");
            flow.Initialize(flow.Definition, transform, station.Session);
            TestJournal = new RepairPropTestJournal(flow);
            selectedTarget = flow.Definition.rootObjectName;
            EnsurePickingColliders();
            wasSuppressed = owners.TryGetValue(station.Scanner, out var previousOwner)
                ? previousOwner.wasSuppressed : station.Scanner.InteractionSuppressed;
            owners[station.Scanner] = this;
            station.Scanner.InteractionSuppressed = true;
            station.Inspector.Clicked += OnInspectorClicked;
            flow.Feedback += OnFeedback;
            attached = true;
            var clinic = FindFirstObjectByType<BorderRepair.TwoNight.UnifiedClinicDirector>();
            if (Application.isPlaying && clinic != null && clinic.Station == station && clinic.InspectionOnBench)
            {
                station.Inspector.SetBenchMounted(true);
                BenchPresentation = gameObject.AddComponent<RepairPropBenchPresentation>();
                BenchPresentation.Initialize(flow, clinic);
            }
            if (Application.isPlaying && showContextPanel)
            {
                var ui = new GameObject("RepairPropPanel_Runtime");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(ui, gameObject.scene);
                ContextPanel = ui.AddComponent<RepairPropContextPanel>();
                ContextPanel.Initialize(this);
                // The visor overlay is its own object: the workspace panel keeps exactly one canvas.
                var visor = new GameObject("RepairPropArOverlay_Runtime");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(visor, gameObject.scene);
                ArOverlay = visor.AddComponent<RepairPropArOverlay>();
                ArOverlay.Initialize(this);
            }
            OnFeedback("维修物品已接入，原有部件与私人记录保留。", FeedbackKind.Info);
        }

        void EnsurePickingColliders()
        {
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                // FX overlays are visual evidence, never opaque picking surfaces.
                if (filter.name.Contains("ScreenState") || filter.name.Contains("LightCurtain") || filter.name.Contains("StatusLED_")) continue;
                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
            }
        }

        void OnInspectorClicked(Vector2 screen)
        {
            if (!CanInteract) return;
            if (ContextPanel != null && (ContextPanel.ContainsScreenPoint(screen) || ContextPanel.AppearanceOpen)) return;
            var ray = station.Inspector.ViewCamera.ScreenPointToRay(screen);
            Physics.SyncTransforms();
            string parkedTarget = null;
            if (Physics.Raycast(ray, out var hit, 10, ~0, QueryTriggerInteraction.Ignore))
            {
                var parked = hit.transform.GetComponentInParent<RepairPropParkedPart>();
                if (parked != null && parked.Owner == flow) parkedTarget = parked.Target;
                // Bench props (tools, lamp, drawers, cabinet door) act on a click; tools go through SelectToolFromPanel.
                var prop = parked == null ? hit.transform.GetComponentInParent<RepairBenchProp>() : null;
                if (prop != null) { prop.Activate(this); return; }
            }
            if (parkedTarget != null) SelectTarget(parkedTarget);
            else
            {
                var point = station.Scanner.PickPoint(screen, out bool hitItem);
                if (!hitItem) return;
                SelectTarget(point != null ? point.PointId : flow.Definition.rootObjectName);
            }
            if (!station.ActiveTool.HasValue && station.Session.ScanModeActive) flow.TryInspect(selectedTarget);
            else if (station.ActiveTool.HasValue)
            {
                PropCommand ready = null;
                int count = 0;
                foreach (var c in flow.Definition.commands)
                    if (c.target == selectedTarget && flow.CanExecute(c.id, c.target, station.ActiveTool.Value, out _))
                    { ready = c; count++; }
                if (count == 1) ExecuteSelected(ready.id);
            }
        }

        public bool SelectTarget(string objectName)
        {
            if (!CanInteract || Flow.Definition.FindNode(objectName) == null) return false;
            selectedTarget = objectName;
            if (station.Toolbar != null) station.Toolbar.SetStatus(Flow.Definition.FindNode(objectName).displayName);
            return true;
        }

        // Used by a custom scene panel, or by the contextual panel below. Tool selection remains on the stock toolbar.
        public bool ExecuteSelected(string commandId)
        {
            if (!CanInteract || !station.ActiveTool.HasValue) return false;
            return flow.TryExecute(commandId, selectedTarget, station.ActiveTool.Value);
        }

        public bool ExecuteFromPanel(string commandId)
        {
            if (!CanInteract) return false;
            var command = Flow.Definition.FindCommand(commandId);
            if (command == null || command.target != selectedTarget) return false;
            if (!Flow.CanExecute(command.id, selectedTarget, command.tool, out _)) return false;
            station.SelectTool(command.tool);
            return ExecuteSelected(commandId);
        }

        // Whole-item commands (power, fixture, tests, delivery confirmation) stay reachable while a part is selected.
        // Only commands authored on the item root qualify; the selected part is kept, and the flow guard still decides.
        public bool ExecuteWholeItemFromPanel(string commandId)
        {
            if (!CanInteract) return false;
            var command = Flow.Definition.FindCommand(commandId);
            string root = Flow.Definition.rootObjectName;
            if (command == null || command.target != root) return false;
            if (!Flow.CanExecute(command.id, root, command.tool, out _)) return false;
            station.SelectTool(command.tool);
            if (!CanInteract || station.ActiveTool != command.tool) return false;
            return flow.TryExecute(command.id, root, station.ActiveTool.Value);
        }

        public void SelectToolFromPanel(RepairActionType? tool)
        { if (CanInteract) station.SelectTool(tool); }

        public void InspectSelected()
        { if (CanInteract) Flow.TryInspect(selectedTarget); }

        void OnFeedback(string message, FeedbackKind kind)
        {
            if (station.View != null) station.View.ShowFeedback(message, kind);
            if (station.Toolbar != null) station.Toolbar.SetStatus(message);
        }

        void OnDestroy()
        {
            if (ContextPanel != null) Destroy(ContextPanel.gameObject);
            if (ArOverlay != null) Destroy(ArOverlay.gameObject);
            TestJournal?.Dispose();
            if (!attached || station == null) return;
            station.Inspector.Clicked -= OnInspectorClicked;
            flow.Feedback -= OnFeedback;
            if (station.Scanner != null && owners.TryGetValue(station.Scanner, out var owner) && owner == this)
            {
                station.Scanner.InteractionSuppressed = wasSuppressed;
                owners.Remove(station.Scanner);
            }
        }
    }
}
