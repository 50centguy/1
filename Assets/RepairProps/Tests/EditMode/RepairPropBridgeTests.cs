using System;
using System.Reflection;
using BorderRepair.Data;
using BorderRepair.Inspection;
using BorderRepair.UI;
using NUnit.Framework;
using UnityEngine;

namespace BorderRepair.RepairProps.Tests
{
    public sealed class RepairPropBridgeTests
    {
        [TestCase("interaction")]
        [TestCase("station")]
        [TestCase("inspector")]
        [TestCase("scanner")]
        [TestCase("view")]
        [TestCase("camera")]
        [TestCase("mounted_item")]
        [TestCase("station_object")]
        [TestCase("bridge")]
        public void LeavingBenchRejectsUiSelectionClicksAndCommandsUntilReentered(string blocker)
        {
            using (var rig = new PropFlowTestRig(RepairPropKind.Cartridge, initialize: false))
            {
                var host = new GameObject("BridgeStationTest");
                host.SetActive(false);
                try
                {
                    // Lifecycle-only doubles avoid starting a second core shift or building unrelated UI.
                    // The real station properties, tool selection and bridge command path remain in use.
                    var station = host.AddComponent<BridgeTestStation>();
                    var inspector = Child(host, "Inspector").AddComponent<BridgeTestInspector>();
                    var scanner = Child(host, "Scanner").AddComponent<BridgeTestScanner>();
                    var view = Child(host, "View").AddComponent<BridgeTestView>();
                    var camera = Child(host, "Camera").AddComponent<Camera>();
                    Set(typeof(ItemInspector), inspector, "viewCamera", camera);
                    Set(typeof(ItemInspector), inspector, "<CurrentItem>k__BackingField", rig.root);
                    Set(typeof(ItemScanner), scanner, "inspector", inspector);
                    Set(typeof(RepairStationController), station, "inspector", inspector);
                    Set(typeof(RepairStationController), station, "scanner", scanner);
                    Set(typeof(RepairStationController), station, "view", view);
                    Set(typeof(RepairStationController), station, "<Session>k__BackingField", rig.session);
                    host.SetActive(true);
                    station.enabled = false;
                    inspector.InteractionEnabled = false;
                    var bridge = rig.root.AddComponent<RepairPropStationBridge>();
                    bridge.Attach(station);
                    Assert.That(bridge.Flow.Session, Is.SameAs(rig.session), "Attach is allowed while the bench is inactive.");
                    Assert.That(bridge.ContextPanelVisible, Is.False);
                    station.enabled = true;
                    inspector.InteractionEnabled = true;
                    station.SelectTool(RepairActionType.OpenHousing);
                    Assert.That(bridge.CanInteract, Is.True);
                    Assert.That(bridge.ContextPanelVisible, Is.True);

                    Action restore;
                    switch (blocker)
                    {
                        case "interaction": inspector.InteractionEnabled = false; restore = () => inspector.InteractionEnabled = true; break;
                        case "station": station.enabled = false; restore = () => station.enabled = true; break;
                        case "inspector": inspector.enabled = false; restore = () => inspector.enabled = true; break;
                        case "scanner": scanner.enabled = false; restore = () => scanner.enabled = true; break;
                        case "view": view.gameObject.SetActive(false); restore = () => view.gameObject.SetActive(true); break;
                        case "camera": camera.enabled = false; restore = () => camera.enabled = true; break;
                        case "mounted_item":
                            Set(typeof(ItemInspector), inspector, "<CurrentItem>k__BackingField", null);
                            restore = () => Set(typeof(ItemInspector), inspector, "<CurrentItem>k__BackingField", rig.root); break;
                        case "station_object": host.SetActive(false); restore = () => host.SetActive(true); break;
                        default: bridge.enabled = false; restore = () => bridge.enabled = true; break;
                    }
                    int invalid = rig.session.CurrentRecord.InvalidActionCount;
                    Assert.That(bridge.CanInteract, Is.False);
                    Assert.That(bridge.ContextPanelVisible, Is.False, "OnGUI uses this exact visibility gate.");
                    Assert.That(bridge.SelectTarget("MC_InterfaceBoard_New"), Is.False);
                    Assert.That(bridge.ExecuteSelected("power.off"), Is.False);
                    typeof(RepairPropStationBridge).GetMethod("OnInspectorClicked", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(bridge, new object[] { Vector2.zero });
                    Assert.That(bridge.SelectedTarget, Is.EqualTo(rig.definition.rootObjectName));
                    Assert.That(rig.flow.History.Count, Is.Zero);
                    Assert.That(rig.flow.GetState("power"), Is.EqualTo(1));
                    Assert.That(rig.session.CurrentRecord.InvalidActionCount, Is.EqualTo(invalid));
                    restore();
                    Assert.That(bridge.ContextPanelVisible, Is.True);
                    Assert.That(bridge.ExecuteSelected("power.off"), Is.True, "Returning to the bench restores the actual command path.");
                    Assert.That(rig.flow.GetState("power"), Is.Zero);
                }
                finally { UnityEngine.Object.DestroyImmediate(host); }
            }
        }

        [TestCase(RepairPropKind.Receiver)]
        [TestCase(RepairPropKind.Cartridge)]
        [TestCase(RepairPropKind.Power)]
        [TestCase(RepairPropKind.Omamori)]
        public void PlayerFacingCasePartAndCommandLabelsUseChinese(RepairPropKind kind)
        {
            using (var rig = new PropFlowTestRig(kind))
            {
                const string cjk = @"[\u4e00-\u9fff]";
                Assert.That(rig.repairCase.itemName, Does.Match(cjk));
                Assert.That(rig.repairCase.customerStatement, Does.Match(cjk));
                foreach (var node in rig.definition.nodes)
                {
                    Assert.That(node.displayName, Does.Match(cjk), node.objectName);
                    Assert.That(node.displayName, Is.Not.EqualTo("部件"), "Missing translation: " + node.objectName);
                    if (node.inventoryOnly && node.objectName.Contains("Screw")) Assert.That(node.motionNote, Does.Contain("未知"));
                }
                foreach (var command in rig.definition.commands) Assert.That(command.label, Does.Match(cjk), command.id);
            }
        }

        static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }
        static void Set(Type type, object instance, string field, object value) =>
            type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
    }

    public sealed class BridgeTestStation : RepairStationController
    { void Awake() { } void Start() { } void Update() { } }
    public sealed class BridgeTestInspector : ItemInspector
    { void Awake() { } void Update() { } }
    public sealed class BridgeTestScanner : ItemScanner
    { void OnEnable() { } void OnDisable() { } void Update() { } }
    public sealed class BridgeTestView : RepairUIView
    { void Awake() { } void Update() { } }
}
