using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.FirstOrder;
using BorderRepair.TwoNight;
using BorderRepair.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.UI.Tests
{
    // The reorganized desktop workspace in the authored clinic, driven by queued Input System mouse events.
    // Not a human acceptance session; aesthetic approval remains manual.
    public sealed class RepairPropWorkspaceSceneTests
    {
        const string ScenePath = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        TwoNightState previousRun;
        string previousSave, isolatedSave;
        float previousTimeScale;
        CursorLockMode previousCursor;
        bool previousCursorVisible;
        Mouse previousMouse, mouse;
        int previousWidth, previousHeight;
        FullScreenMode previousScreenMode;
        bool requestedResolution;
#if UNITY_EDITOR
        UiTestGameView gameView;
#endif
        Action cleanupMouse;
        Scene loaded;
        UnifiedClinicDirector clinic;
        RepairPropStationBridge bridge;
        int inspectorClicks;
        RepairStationController Station => clinic.Station;
        RepairPropFlow Flow => bridge.Flow;
        RepairPropContextPanel Panel => bridge.ContextPanel;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current;
            previousSave = TwoNightSave.OverrideDirectory;
            previousTimeScale = Time.timeScale;
            previousCursor = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            previousMouse = Mouse.current;
            previousWidth = Screen.width; previousHeight = Screen.height; previousScreenMode = Screen.fullScreenMode;
            requestedResolution = false;
#if UNITY_EDITOR
            if (Application.isBatchMode)
                foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    if (window.GetType().FullName == "UnityEditor.SettingsWindow") window.Close();
#endif
            inspectorClicks = 0;
            isolatedSave = Path.Combine(Path.GetTempPath(), "RepairPropWorkspace_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = isolatedSave;
            TwoNightRun.Clear();
            Time.timeScale = 1;
#if UNITY_EDITOR
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Requires the authored editor scene.");
            yield break;
#endif
            loaded = SceneManager.GetSceneByPath(ScenePath);
            yield return Until(() =>
            {
                clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>();
                return clinic != null && clinic.Initialized;
            }, "The real clinic must initialize.");
            clinic.Robot.Flow.Rig.Walker.LeaveSystemCursor = true;
            // Mechanical/UI coverage starts with a genuinely purchased replacement, not free prototype stock.
            Assert.That(ClinicSupplies.Purchase(clinic.State, new System.Collections.Generic.Dictionary<string, int> { { "rx-rf", 1 } },
                System.Guid.NewGuid().ToString("N"), out string stockReason), Is.True, stockReason);
            mouse = FirstOrderAcceptanceDriver.CreateVirtualMouse(out cleanupMouse);
            QueueMouse(new Vector2(-100, -100));
            yield return null;
            yield return Until(() => EventSystem.current != null && EventSystem.current.currentInputModule is InputSystemUIInputModule,
                "The authored EventSystem must use InputSystemUIInputModule.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (clinic != null && Station != null && Station.Inspector != null) Station.Inspector.Clicked -= CountClick;
            if (loaded.IsValid() && loaded.isLoaded)
            {
                var cleanup = SceneManager.CreateScene("RepairPropWorkspaceCleanup_" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
            cleanupMouse?.Invoke(); cleanupMouse = null;
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            TwoNightRun.Set(previousRun);
            TwoNightSave.OverrideDirectory = previousSave;
            Time.timeScale = previousTimeScale;
            Cursor.lockState = previousCursor;
            Cursor.visible = previousCursorVisible;
            if (requestedResolution)
            {
#if UNITY_EDITOR
                gameView?.Dispose(); gameView = null;
#endif
                Screen.SetResolution(previousWidth, previousHeight, previousScreenMode);
                for (int i = 0; i < 30 && (Screen.width != previousWidth || Screen.height != previousHeight); i++) yield return null;
            }
            if (!string.IsNullOrEmpty(isolatedSave) && Directory.Exists(isolatedSave))
            {
                string resolved = Path.GetFullPath(isolatedSave);
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                Assert.That(resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase), Is.True);
                Assert.That(Path.GetFileName(resolved).StartsWith("RepairPropWorkspace_", StringComparison.Ordinal), Is.True);
                Directory.Delete(resolved, true);
            }
            clinic = null; bridge = null; mouse = null;
        }

        [UnityTest]
        public IEnumerator Receiver_DisconnectedBeforeDiagnosis_UiLeadsConnectPowerAndBothInitialTests()
        {
            yield return ReceiveAndBench();
            string screw = "RX_CoverScrew_01";
            // Reproduce the reported mistake through the real whole-item buttons.
            yield return Click(Panel.CommandButton("power.off"));
            yield return Click(Panel.CommandButton("fixture.disconnect"));
            Assert.That(Flow.GetState("power"), Is.Zero);
            Assert.That(Flow.GetState("fixture"), Is.Zero);
            yield return SelectWithPicker(screw);
            Assert.That(Panel.CommandButton("remove." + screw).interactable, Is.False);
            Assert.That(Panel.CommandReason("remove." + screw), Does.StartWith("初始诊断未完成").And.Contain("接入外部测试连接"));
            Assert.That(Panel.ReasonShown(Panel.CommandReason("remove." + screw)), Is.True);
            var signal = Panel.CommandButton("test.signal");
            Assert.That(signal, Is.Not.Null, "Whole-item tests stay listed while a screw is selected.");
            Assert.That(signal.interactable, Is.False);
            Assert.That(Panel.CommandReason("test.signal"), Does.StartWith("外部测试连接未接入"));
            Assert.That(Panel.ReasonShown(Panel.CommandReason("test.signal")), Is.True, "The blocker is written in the whole-item section.");
            Assert.That(Panel.StatusText, Does.StartWith("外部测试连接未接入"));
            Assert.That(Panel.Phase, Is.EqualTo(RepairPropPhase.InitialTest));

            yield return Click(Panel.CommandButton("fixture.connect"));
            Assert.That(Flow.GetState("fixture"), Is.EqualTo(1));
            Assert.That(bridge.SelectedTarget, Is.EqualTo(screw), "A whole-item action does not drop the selected part.");
            Assert.That(Panel.CommandReason("test.signal"), Does.StartWith("测试供电未开启"));
            Assert.That(Panel.ReasonShown(Panel.CommandReason("test.signal")), Is.True);
            yield return Click(Panel.CommandButton("power.on"));
            Assert.That(Panel.CommandButton("test.signal").interactable, Is.True, "Connected and powered: the signal test runs.");
            yield return Click(Panel.CommandButton("test.signal"));
            Assert.That(Flow.GetState("diagnosed.signal"), Is.EqualTo(1));
            Assert.That(Flow.LastTestPassed, Is.False);
            Assert.That(Panel.GetComponentsInChildren<Text>().Any(t => t.isActiveAndEnabled && t.text.StartsWith("标准信号与锁频测试：初测发现故障")), Is.True,
                "An expected first fault reads as a recorded diagnosis.");
            Assert.That(Panel.FeedbackText, Is.EqualTo(Flow.LastFeedback));
            Assert.That(Panel.CommandButton("remove." + screw).interactable, Is.False, "Screws stay locked until both initial tests ran.");
            Assert.That(Panel.CommandReason("remove." + screw), Does.Contain("只读核对原调谐记录身份"));
            yield return Click(Panel.CommandButton("test.identity"));
            Assert.That(Panel.Phase, Is.EqualTo(RepairPropPhase.SafeDisassembly));
            Assert.That(Panel.CommandReason("remove." + screw), Does.StartWith("请先关机并解除外部测试连接"));
            AssertNoDeliveryOffered();
            Assert.That(inspectorClicks, Is.Zero, "Workspace clicks never reach the inspector.");
        }

        [UnityTest]
        public IEnumerator PartsInventory_RemovedPartsSelectable_ParkedPickVisible_AndQuickTargetsOnlySelect()
        {
            yield return ReceiveAndBench();
            foreach (var test in Flow.Definition.tests) Do("test." + test.id);
            Do("power.off"); Do("fixture.disconnect");
            yield return Layout();
            var quick = Panel.QuickTargetButton("RX_CoverScrew_01");
            Assert.That(quick, Is.Not.Null, "Parts with an executable command are offered as targets.");
            int history = Flow.History.Count;
            yield return Click(quick);
            Assert.That(bridge.SelectedTarget, Is.EqualTo("RX_CoverScrew_01"));
            Assert.That(Flow.History.Count, Is.EqualTo(history), "Choosing a target never executes a command.");
            yield return Click(Panel.CommandButton("remove.RX_CoverScrew_01"));
            Assert.That(Flow.GetState("out.RX_CoverScrew_01"), Is.EqualTo(1));
            for (int i = 2; i <= 4; i++) Do("remove.RX_CoverScrew_0" + i);
            Do("cover.remove");
            yield return Until(() => !bridge.BenchPresentation.IsAnimating, "Parts must settle on the desk.");
            yield return SelectWithPicker(Flow.Definition.rootObjectName);
            var options = Panel.TargetOptions.ToList();
            Assert.That(options, Does.Contain("RX_BatteryLead_Connected"));
            Assert.That(options, Does.Not.Contain("RX_BatteryLead_Isolated"), "Only the physically present variant is listed.");
            Assert.That(options, Does.Contain("RX_Mainboard"), "Retained parts are inspectable once exposed.");

            var part = Panel.PartButton("RX_CoverScrew_02");
            Assert.That(part, Is.Not.Null, "Removed screws appear in the parts inventory.");
            Assert.That(part.GetComponentsInChildren<Text>().Any(t => t.text == "原件 · 台面"), Is.True);
            Assert.That(Panel.PartButton("RX_RF_Daughterboard_New").GetComponentsInChildren<Text>().Any(t => t.text == "替换件 · 待装"), Is.True);
            yield return Click(part);
            Assert.That(bridge.SelectedTarget, Is.EqualTo("RX_CoverScrew_02"));
            Assert.That(Panel.CommandButton("install.RX_CoverScrew_02"), Is.Not.Null);
            Assert.That(Panel.CommandButton("power.on"), Is.Not.Null, "Whole-item actions remain listed for a removed part.");

            // The real parked mesh stays outside the workspace and is picked through the inspector click path.
            var parked = bridge.BenchPresentation.ParkedVisual("RX_CoverScrew_03");
            Assert.That(parked, Is.Not.Null);
            var camera = Station.Inspector.ViewCamera;
            Vector2 point = camera.WorldToScreenPoint(ItemBounds(parked).center);
            Assert.That(camera.pixelRect.Contains(point), Is.True);
            Assert.That(Panel.ContainsScreenPoint(point), Is.False, "Parked parts must not sit under the workspace.");
            Assert.That(UiAt(point), Is.False, "No overlay may cover the parked part.");
            QueueMouse(point); yield return null; yield return null;
            QueueMouse(point, true); yield return null;
            QueueMouse(point); yield return null; yield return null;
            yield return Until(() => bridge.SelectedTarget == "RX_CoverScrew_03", "A real click on the parked screw selects it.");
        }

        [UnityTest]
        public IEnumerator Workspace_ConsumesPointerAndWheel_HeaderAndTabsInsidePanel()
        {
            yield return ReceiveAndBench();
            var rotation = Station.Inspector.ItemAnchor.rotation;
            float distance = Station.Inspector.TargetDistance;
            var header = Panel.GetComponentsInChildren<Text>().First(t => t.text.StartsWith("第 1/4 件"));
            Vector2 start = Center(header.rectTransform);
            Assert.That(Panel.ContainsScreenPoint(start), Is.True);
            QueueMouse(start); yield return null; yield return null;
            QueueMouse(start, true); yield return null;
            QueueMouse(start + new Vector2(-40, -10), true); yield return null;
            QueueMouse(start + new Vector2(-40, -10)); yield return null; yield return null;
            QueueMouse(start, false, new Vector2(0, -120)); yield return null;
            QueueMouse(start); yield return null; yield return null;
            Assert.That(inspectorClicks, Is.Zero);
            Assert.That(Quaternion.Angle(rotation, Station.Inspector.ItemAnchor.rotation), Is.LessThan(.01f));
            Assert.That(Station.Inspector.TargetDistance, Is.EqualTo(distance).Within(.001f));

            yield return Click(Panel.TabButton(RepairPropWorkspaceTab.Order));
            Assert.That(Panel.Tab, Is.EqualTo(RepairPropWorkspaceTab.Order));
            Assert.That(Panel.Scroll.content.GetComponentsInChildren<Text>().Any(t => t.text.Contains(Flow.Definition.repairCase.customerStatement)), Is.True,
                "The customer request stays available after the intake dialogue closes.");
            Assert.That(clinic.Counter.DialogueVisible, Is.False);
            Do("test.signal");
            yield return Click(Panel.TabButton(RepairPropWorkspaceTab.Records));
            Assert.That(Panel.Scroll.content.GetComponentsInChildren<Text>().Any(t => t.text.StartsWith("初测 · 标准信号与锁频测试：发现故障")), Is.True);
            yield return Click(Panel.TabButton(RepairPropWorkspaceTab.Operate));
            Assert.That(Panel.CommandButton("power.off"), Is.Not.Null);
            Assert.That(inspectorClicks, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AppearanceMode_IsExclusiveWithRepair_AndReturnsOwnership()
        {
            yield return ReceiveAndBench();
            foreach (var test in Flow.Definition.tests) Do("test." + test.id);
            Do("power.off"); Do("fixture.disconnect");
            yield return Click(Panel.ModeButton(true));
            Assert.That(Panel.AppearanceOpen, Is.True);
            yield return Layout();
            var editor = FindRect("外观面板");
            Assert.That(editor, Is.Not.Null);
            Assert.That(editor.gameObject.activeInHierarchy, Is.True, "The appearance editor occupies the workspace page.");
            Rect panelRect = ScreenRect(Panel.PanelRectTransform), editorRect = ScreenRect(editor);
            Assert.That(editorRect.xMin, Is.GreaterThanOrEqualTo(panelRect.xMin - 1));
            Assert.That(editorRect.xMax, Is.LessThanOrEqualTo(panelRect.xMax + 1));
            Assert.That(editorRect.yMin, Is.GreaterThanOrEqualTo(panelRect.yMin - 1));
            Assert.That(Panel.Scroll.gameObject.activeInHierarchy, Is.False, "Repair operations are hidden while editing appearance.");
            var spray = editor.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<Text>().text == "喷涂");
            yield return Click(spray);
            Assert.That(Station.CleaningActive, Is.True, "A brush claims the bench input.");
            Assert.That(bridge.CanInteract, Is.False);
            Assert.That(Panel.Visible, Is.True, "The workspace header stays while painting.");
            Assert.That(bridge.ExecuteWholeItemFromPanel("fixture.connect"), Is.False);
            yield return Click(Panel.ModeButton(false));
            Assert.That(Station.CleaningActive, Is.False, "Returning to repair switches the brush off.");
            Assert.That(Panel.AppearanceOpen, Is.False);
            yield return Layout();
            Assert.That(editor.gameObject.activeInHierarchy, Is.False);
            Assert.That(bridge.CanInteract, Is.True);
            Assert.That(Panel.CommandButton("fixture.connect").interactable, Is.True);
            Assert.That(inspectorClicks, Is.Zero);
        }

        [UnityTest]
        public IEnumerator LegacyHud_SuppressedVisuallyDuringWork_RestoredWhenWorkspaceIsDestroyed()
        {
            var view = Station.View;
            yield return ReceiveAndBench();
            foreach (string name in RepairPropLegacyHud.Panels)
            {
                var group = view.transform.Find(name).GetComponent<CanvasGroup>();
                Assert.That(group.alpha, Is.Zero, name);
                Assert.That(group.blocksRaycasts, Is.False, name);
            }
            Assert.That(view.isActiveAndEnabled && Station.Scanner.isActiveAndEnabled && Station.isActiveAndEnabled, Is.True,
                "Ownership components stay enabled.");
            var item = Station.Inspector.CurrentItem;
            var panelObject = Panel.gameObject;
            Station.Inspector.Clear();
            yield return null; yield return null;
            Assert.That(item == null && panelObject == null, Is.True);
            foreach (string name in RepairPropLegacyHud.Panels)
                Assert.That(view.transform.Find(name).GetComponent<CanvasGroup>(), Is.Null, name + " must be restored.");
            Assert.That(RepairPropLegacyHud.IsSuppressed(view), Is.False);
        }

        [UnityTest]
        public IEnumerator Delivery_NotOfferedUntilVerified_ThenConfirmedOnceThroughTheWorkspace()
        {
            yield return ReceiveAndBench();
            foreach (var test in Flow.Definition.tests) Do("test." + test.id);
            Do("power.off"); Do("fixture.disconnect");
            for (int i = 1; i <= 4; i++) Do("remove.RX_CoverScrew_0" + i);
            Do("cover.remove"); Do("isolate"); Do("harness.release");
            Do("remove.RX_RFScrew_01"); Do("remove.RX_RFScrew_02");
            Do("module.remove.old");
            yield return Layout();
            Assert.That(Panel.Phase, Is.EqualTo(RepairPropPhase.Replacement));
            AssertNoDeliveryOffered();
            Do("module.install.new");
            Do("install.RX_RFScrew_01"); Do("install.RX_RFScrew_02");
            Do("harness.connect"); Do("supply.connect"); Do("cover.install");
            for (int i = 1; i <= 4; i++) Do("install.RX_CoverScrew_0" + i);
            Do("fixture.connect"); Do("power.on");
            yield return Layout();
            Assert.That(Panel.Phase, Is.EqualTo(RepairPropPhase.Retest));
            AssertNoDeliveryOffered();
            Do("test.signal");
            yield return Layout();
            AssertNoDeliveryOffered();
            Do("test.identity");
            yield return Layout();
            Assert.That(Panel.Phase, Is.EqualTo(RepairPropPhase.Delivery));
            var complete = Panel.CommandButton("complete");
            Assert.That(complete.interactable, Is.True);
            yield return Click(complete);
            yield return Until(() => clinic.TradeState == ClinicTradeState.ReadyForDelivery, "Verified completion reaches the delivery pad.");
            Assert.That(Flow.Completed, Is.True);
            Assert.That(bridge.WorkspaceVisible, Is.False);
            Assert.That(Panel.Visible, Is.False, "The workspace closes with the bench.");
        }

        [UnityTest]
        public IEnumerator Layout_FitsAt1280_1920_1024_KeepsMainViewAndTextUnclipped()
        {
            yield return ReceiveAndBench();
            foreach (var test in Flow.Definition.tests) Do("test." + test.id);
            Do("power.off"); Do("fixture.disconnect");
            for (int i = 1; i <= 4; i++) Do("remove.RX_CoverScrew_0" + i);
            Do("cover.remove");
            yield return Until(() => !bridge.BenchPresentation.IsAnimating, "Parts must settle.");
            Assert.That(bridge.SelectTarget("RX_RearCover"), Is.True);
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1024, 768) })
            {
                yield return RequestResolution(size);
                foreach (var tab in new[] { RepairPropWorkspaceTab.Operate, RepairPropWorkspaceTab.Order, RepairPropWorkspaceTab.Records })
                {
                    Panel.ShowTab(tab);
                    yield return Layout();
                    Rect panel = ScreenRect(Panel.PanelRectTransform);
                    Assert.That(panel.xMin, Is.GreaterThanOrEqualTo(-1)); Assert.That(panel.yMin, Is.GreaterThanOrEqualTo(-1));
                    Assert.That(panel.xMax, Is.LessThanOrEqualTo(Screen.width + 1)); Assert.That(panel.yMax, Is.LessThanOrEqualTo(Screen.height + 1));
                    Assert.That(panel.xMin / Screen.width, Is.GreaterThanOrEqualTo(.55f), size + ": the main view keeps at least 55% of the width.");
                    var item = ItemBounds(Station.Inspector.CurrentItem.transform);
                    Vector2 itemCenter = Station.Inspector.ViewCamera.WorldToScreenPoint(item.center);
                    Assert.That(UiAt(itemCenter), Is.False, size + ": the item centre is not covered by UI.");
                    foreach (var text in Panel.PanelRectTransform.GetComponentsInChildren<Text>())
                    {
                        if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text)) continue;
                        if (text.transform.IsChildOf(Panel.Scroll.viewport)) continue; // checked below against the viewport
                        AssertTextInside(text, Panel.PanelRectTransform, size + "/" + tab);
                    }
                    foreach (var text in Panel.Scroll.content.GetComponentsInChildren<Text>())
                    {
                        if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text)) continue;
                        Assert.That(text.rectTransform.rect.height + 1, Is.GreaterThanOrEqualTo(text.preferredHeight), size + "/" + tab + " clipped: " + text.text);
                        Assert.That(text.rectTransform.rect.width, Is.GreaterThan(20), text.text);
                    }
                    var director = clinic.GetComponentsInChildren<Button>().Where(b => b.isActiveAndEnabled).Select(b => ScreenRect((RectTransform)b.transform));
                    foreach (var other in director) Assert.That(other.Overlaps(panel), Is.False, size + ": workspace overlaps a clinic exit button.");
                }
                Panel.ShowTab(RepairPropWorkspaceTab.Operate);
                yield return Layout();
                // The real dropdown popup stays on screen.
                Panel.TargetPicker.Show();
                yield return null; yield return null;
                var list = Panel.TargetPicker.GetComponentsInChildren<ScrollRect>().FirstOrDefault(s => s.transform != Panel.Scroll.transform && s.name == "Dropdown List");
                if (list == null) list = Object.FindObjectsByType<ScrollRect>(FindObjectsSortMode.None).FirstOrDefault(s => s.name == "Dropdown List");
                Assert.That(list, Is.Not.Null, "The dropdown list must open.");
                Rect popup = ScreenRect((RectTransform)list.transform);
                Assert.That(popup.yMin, Is.GreaterThanOrEqualTo(-1), size + " dropdown bottom");
                Assert.That(popup.xMax, Is.LessThanOrEqualTo(Screen.width + 1), size + " dropdown right");
                Panel.TargetPicker.Hide();
                yield return new WaitForSecondsRealtime(.25f);
            }
        }

        void AssertNoDeliveryOffered()
        {
            var complete = Panel.CommandButton("complete");
            Assert.That(complete, Is.Not.Null);
            Assert.That(complete.interactable, Is.False, "Delivery confirmation stays disabled before every retest passes.");
            Assert.That(Panel.CommandReason("complete"), Is.Not.Empty);
            Assert.That(clinic.TradeState, Is.EqualTo(ClinicTradeState.InRepair));
            Assert.That(clinic.DeliverLampState, Is.EqualTo(UnifiedClinicDirector.PadLamp.Off));
            Assert.That(Flow.Completed, Is.False);
        }

        static void AssertTextInside(Text text, RectTransform container, string context)
        {
            Rect bounds = ScreenRect(text.rectTransform), parent = ScreenRect(container);
            Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(parent.xMin - 1), context + ": " + text.text);
            Assert.That(bounds.xMax, Is.LessThanOrEqualTo(parent.xMax + 1), context + ": " + text.text);
            Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(parent.yMin - 1), context + ": " + text.text);
            Assert.That(bounds.yMax, Is.LessThanOrEqualTo(parent.yMax + 1), context + ": " + text.text);
            Assert.That(text.rectTransform.rect.height + 1, Is.GreaterThanOrEqualTo(text.preferredHeight), context + " clipped: " + text.text);
            Assert.That(text.resizeTextForBestFit, Is.False, context + ": " + text.text);
        }

        IEnumerator ReceiveAndBench()
        {
            Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True, clinic.LastMessage);
            bridge = Station.Inspector.CurrentItem.GetComponent<RepairPropStationBridge>();
            yield return Until(() => Flow.Session != null && bridge.ContextPanel != null, "Bridge and workspace must bind.");
            Assert.That(Panel.Visible, Is.False, "No workspace while carrying the item.");
            Assert.That(clinic.Counter.DialogueVisible, Is.True, "The intake dialogue shows in the room.");
            Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True, clinic.LastMessage);
            yield return Until(() => bridge.CanInteract && Panel.Visible && CameraSettled(), "Bench must settle with the workspace.");
            Assert.That(clinic.Counter.DialogueVisible, Is.False, "The dialogue yields the bench to the workspace.");
            yield return Layout();
            Station.Inspector.Clicked += CountClick;
        }

        void CountClick(Vector2 point) => inspectorClicks++;

        void Do(string id)
        {
            var command = Flow.Definition.FindCommand(id);
            Assert.That(command, Is.Not.Null, id);
            Assert.That(bridge.SelectTarget(command.target), Is.True, id);
            Assert.That(bridge.ExecuteFromPanel(id), Is.True, id + ": " + Flow.LastFeedback);
        }

        IEnumerator SelectWithPicker(string target)
        {
            var node = Flow.Definition.FindNode(target);
            int index = Panel.TargetPicker.options.FindIndex(o => o.text == node.displayName);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "Picker must list " + target);
            Panel.TargetPicker.SetValueWithoutNotify(index);
            Panel.TargetPicker.onValueChanged.Invoke(index);
            yield return Layout();
            Assert.That(bridge.SelectedTarget, Is.EqualTo(target));
        }

        IEnumerator RequestResolution(Vector2Int size)
        {
            requestedResolution = true;
#if UNITY_EDITOR
            if (gameView == null) gameView = new UiTestGameView();
            gameView.Set(size.x, size.y);
#endif
            Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
            for (int i = 0; i < 30 && (Screen.width != size.x || Screen.height != size.y); i++) yield return null;
            yield return Layout();
            Assert.That(new Vector2Int(Screen.width, Screen.height), Is.EqualTo(size), "The GameView must adopt the requested size.");
            TestContext.WriteLine("Requested " + size.x + "x" + size.y + "; actual " + Screen.width + "x" + Screen.height);
        }

        IEnumerator Layout()
        {
            Panel.Refresh();
            yield return null;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(Panel.PanelRectTransform);
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        IEnumerator Click(Button button)
        {
            Assert.That(button, Is.Not.Null);
            yield return Layout();
            Vector2 point = Center((RectTransform)button.transform);
            var scroll = button.GetComponentInParent<ScrollRect>();
            if (scroll != null && !RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport, point, null))
            {
                // Bring the row into the viewport the way a player would scroll to it.
                var content = scroll.content;
                float top = -((RectTransform)button.transform).anchoredPosition.y - 8;
                float range = Mathf.Max(1, content.rect.height - scroll.viewport.rect.height);
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = Mathf.Clamp01(1 - top / range);
                yield return Layout();
                point = Center((RectTransform)button.transform);
            }
            yield return Until(() => TopRaycastBelongsTo(button.gameObject, point), "The real canvas must raycast to " + button.name);
            QueueMouse(point); yield return null; yield return null;
            QueueMouse(point, true); yield return null;
            QueueMouse(point); yield return null;
            yield return Layout();
        }

        void QueueMouse(Vector2 point, bool down = false, Vector2 scroll = default)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState
            {
                position = point, delta = point - mouse.position.ReadValue(),
                buttons = down ? (ushort)1 : (ushort)0, scroll = scroll
            });
        }

        static RectTransform FindRect(string name) =>
            Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(r => r.name == name);

        static Bounds ItemBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            var b = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }

        static bool UiAt(Vector2 point)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            return hits.Count > 0;
        }

        static Vector2 Center(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));

        static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(null, corners[0]), max = min;
            foreach (var c in corners) { var p = RectTransformUtility.WorldToScreenPoint(null, c); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        static bool TopRaycastBelongsTo(GameObject root, Vector2 point)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            return hits.Count > 0 && (hits[0].gameObject == root || hits[0].gameObject.transform.IsChildOf(root.transform));
        }

        bool CameraSettled()
        {
            var inspector = Station.Inspector;
            var camera = inspector.ViewCamera;
            return camera.isActiveAndEnabled
                && Mathf.Abs(Vector3.Distance(camera.transform.position, inspector.LookPoint) - inspector.TargetDistance) < .001f
                && Vector3.Angle(camera.transform.forward, inspector.LookPoint - camera.transform.position) < .1f;
        }

        static IEnumerator Until(Func<bool> ready, string reason)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, reason);
        }
    }
}
