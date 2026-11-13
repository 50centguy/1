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
        public RepairPropGesture Gesture { get; private set; }
        public RepairStationController Station => station;
        public string SelectedTarget => selectedTarget;
        /// <summary>The part last looked at closely and when (unscaled time): the visor plays its analysis on it.</summary>
        public string LastInspected { get; private set; }
        public float LastInspectedAt { get; private set; }
        void Inspect(string target)
        {
            if (flow.TryInspect(target) == ScanOutcome.Rejected) return;
            LastInspected = target; LastInspectedAt = Time.unscaledTime;
        }
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
            if (Application.isPlaying && flow != null && flow.Definition != null && flow.Definition.kind == RepairPropKind.Omamori && GetComponent<OmamoriCord>() == null)
                gameObject.AddComponent<OmamoriCord>();
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
            // Each order of a fault-pool item carries one of its faults, picked when it reaches the bench.
            if (Application.isPlaying) flow.RollFault();
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
                var hands = new GameObject("RepairPropGesture_Runtime");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(hands, gameObject.scene);
                Gesture = hands.AddComponent<RepairPropGesture>();
                Gesture.Initialize(this);
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
            if (ContextPanel != null && ContextPanel.ContainsScreenPoint(screen)) return;
            var ray = station.Inspector.ViewCamera.ScreenPointToRay(screen);
            Physics.SyncTransforms();
            if (ContextPanel != null && ContextPanel.AppearanceOpen)
            {
                // Only the paint can answers while the appearance editor is open: a click on it puts it away.
                var can = Physics.Raycast(ray, out var canHit, 10, ~0, QueryTriggerInteraction.Ignore) ? canHit.transform.GetComponentInParent<RepairBenchProp>() : null;
                if (can != null && can.IsPainter) can.Activate(this);
                return;
            }
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
            var command = ClickCommandAt(selectedTarget, out string why);
            if (command != null) { Perform(command.id, command.target == flow.Definition.rootObjectName); return; }
            if (!station.ActiveTool.HasValue)
            {
                // Empty hands (the loupe): a click on an exposed part looks at it closely.
                if (station.Session.ScanModeActive || Inspectable(selectedTarget)) Inspect(selectedTarget);
                else if (why != null) RejectClick(why);
                return;
            }
            RejectClick(why);
        }

        /// <summary>Done with bare hands, whatever is (or is not) in them: plugging and unplugging, swapping a module,
        /// power and the test hook-up, handing the job over. The tester is for testing and measuring only.</summary>
        public static bool ByHand(PropCommand c)
        {
            if (c == null) return false;
            if (c.kind == PropCommandKind.Power || c.kind == PropCommandKind.Fixture || c.kind == PropCommandKind.Complete) return true;
            if (c.kind != PropCommandKind.Manipulate) return false;
            switch (RepairPropFeel.For(c))
            {
                case PropFeel.Unplug: case PropFeel.Plug: case PropFeel.SlideOut: case PropFeel.SlideIn: return true;
                case PropFeel.Tap: return c.tool == RepairActionType.ServiceModule;   // plain swaps authored on the tester
                default: return false;   // screws, covers, scrubbing and crimping take their tool
            }
        }

        /// <summary>True while the job waits for a replacement part that is not in stock.</summary>
        public bool NeedsPurchase => flow != null && flow.CommandPolicy != null && !flow.CommandPolicy.ReplacementAvailable
            && RepairPropWorkflowStatus.Phase(flow) == RepairPropPhase.Replacement;

        /// <summary>True when the step needs a tool that is not in the hand.</summary>
        public bool NeedsTool(PropCommand c) => c != null && !ByHand(c) && station.ActiveTool != c.tool;

        // A by-hand step leaves the hand as it was: the station's tool is switched for the command and put back after.
        RepairActionType? handBefore;
        bool handPending;
        /// <summary>The tool the player is holding, as shown: unchanged while a by-hand step borrows the station's tool.</summary>
        public RepairActionType? ShownTool => handPending ? handBefore : station.ActiveTool;

        void Update()
        {
            if (!handPending || Gesture != null && Gesture.Busy) return;
            handPending = false;
            station.SelectTool(handBefore);
        }

        /// <summary>What a left click on <paramref name="target"/> does right now, or null (and why, when something is
        /// in the way). With empty hands a part that can be looked at is looked at first, unless the by-hand step on it
        /// is the suggested one.</summary>
        public PropCommand ClickCommandAt(string target, out string why)
        {
            var command = ToolCommandAt(target, out why);
            if (command != null && !station.ActiveTool.HasValue && command != RepairPropWorkflowStatus.NextCommand(flow)
                && (station.Session.ScanModeActive || Inspectable(target))) { why = null; return null; }
            return command;
        }

        bool Inspectable(string target)
        {
            var node = flow.Definition.FindNode(target);
            var part = flow.GetObject(target);
            return target != flow.Definition.rootObjectName && node != null && flow.Matches(node.inspectRequires)
                && part != null && part.gameObject.activeInHierarchy;
        }

        /// <summary>What a left click with the tool in hand does on <paramref name="target"/>: the command it would run,
        /// or null and why not. Several commands ready on one part: the suggested next step wins, else the first. A click on
        /// the item's body, or anywhere when the suggested step is a whole-item one for this tool (the tester for a test,
        /// say), runs the whole-item command.</summary>
        public PropCommand ToolCommandAt(string target, out string why)
        {
            why = null;
            if (!CanInteract || target == null) { why = "现在不能动手。"; return null; }
            var tool = station.ActiveTool;
            var next = RepairPropWorkflowStatus.NextCommand(flow);
            string root = flow.Definition.rootObjectName;
            var found = Ready(target, tool, next, ref why);
            if (found == null && tool.HasValue && target != root && next != null && next.target == root && next.tool == tool && !ByHand(next))
            {
                string ignored = null;
                found = Ready(root, tool, next, ref ignored);
            }
            if (found == null && why == null && tool.HasValue) why = "这个部位用不上手上的工具，换一件试试。";
            return found;
        }

        PropCommand Ready(string target, RepairActionType? tool, PropCommand next, ref string why)
        {
            PropCommand first = null;
            bool whyIsPending = false;
            string root = flow.Definition.rootObjectName;
            foreach (var c in flow.Definition.commands)
            {
                if (c.target != target) continue;
                bool hand = ByHand(c);
                if (!hand && (!tool.HasValue || c.tool != tool.Value)) continue;
                // Whole-item by-hand steps (power, hook-up, hand-over) answer a click on the body only when they are the
                // suggested step; the others stay on the panel, so a stray click never switches the item off.
                if (hand && target == root && c != next) continue;
                if (flow.CanExecute(c.id, c.target, c.tool, out string reason))
                {
                    if (c == next) return c;
                    if (first == null) first = c;
                    continue;
                }
                // Prefer the reason of a step still to do over "already done".
                bool pending = c.effects.Count == 0 || !flow.Matches(c.effects);
                if (why == null || pending && !whyIsPending) { why = reason; whyIsPending = pending; }
            }
            if (first != null) why = null;
            return first;
        }

        // A click with a tool that does nothing must say why. Not a flow rejection: no penalty, and the flow state is untouched.
        void RejectClick(string reason)
        {
            if (station.View != null) station.View.ShowFeedback(reason, FeedbackKind.Warning);
            if (Gesture != null) Gesture.PlayDeny();
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

        /// <summary>What a player's press does: a command with a hand motion (RepairPropFeel) starts its gesture and runs
        /// when the motion finishes; anything else runs at once. ExecuteFromPanel / ExecuteWholeItemFromPanel stay the
        /// immediate, guarded entry points the gesture commits through.</summary>
        public bool Perform(string commandId, bool wholeItem)
        {
            if (!CanInteract) return false;
            var command = Flow.Definition.FindCommand(commandId);
            if (command == null) return false;
            var before = station.ActiveTool;
            bool started = Begin();
            if (started && ByHand(command) && before != command.tool)
            {
                if (Gesture != null && Gesture.Busy) { handBefore = before; handPending = true; }
                else station.SelectTool(before);   // done at once: the hand is as it was
            }
            return started;

            bool Run() => wholeItem ? ExecuteWholeItemFromPanel(commandId) : ExecuteFromPanel(commandId);
            bool Begin()
            {
                var feel = RepairPropFeel.For(command);
                if (Gesture == null || !RepairPropFeel.Enabled || feel == PropFeel.Tap) return Run();
                if (wholeItem ? command.target != Flow.Definition.rootObjectName : command.target != selectedTarget) return false;
                if (!Flow.CanExecute(command.id, command.target, command.tool, out _)) return Run();
                station.SelectTool(command.tool);
                // A screw answers one click: the driver runs it out (or home) by itself, no circling by hand.
                if (feel == PropFeel.Unscrew || feel == PropFeel.Screw)
                    return Gesture.RunAuto(new[] { command }, m => SelectTarget(m.target) && ExecuteFromPanel(m.id), null);
                return Gesture.Begin(command, () => CanInteract && (wholeItem || SelectTarget(command.target)) && Run());
            }
        }

        public void SelectToolFromPanel(RepairActionType? tool)
        { if (CanInteract) station.SelectTool(tool); }

        public void InspectSelected()
        { if (CanInteract) Inspect(selectedTarget); }

        void OnFeedback(string message, FeedbackKind kind)
        {
            if (station.View != null) station.View.ShowFeedback(message, kind);
            if (station.Toolbar != null) station.Toolbar.SetStatus(message);
        }

        void OnDestroy()
        {
            if (ContextPanel != null) Destroy(ContextPanel.gameObject);
            if (ArOverlay != null) Destroy(ArOverlay.gameObject);
            if (Gesture != null) Destroy(Gesture.gameObject);
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
