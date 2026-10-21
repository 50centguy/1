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
    // Authored clinic and queued Input System events; not a human-input acceptance run.
    public sealed class RepairPropContextPanelSceneTests
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
        int inspectorClickCount;
        Action cleanupMouse;
        Scene loaded;
        UnifiedClinicDirector clinic;
        RepairPropStationBridge bridge;
        RepairStationController Station => clinic.Station;
        RepairPropFlow Flow => bridge.Flow;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousRun = TwoNightRun.Current;
            previousSave = TwoNightSave.OverrideDirectory;
            previousTimeScale = Time.timeScale;
            previousCursor = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            previousMouse = Mouse.current;
            previousWidth = Screen.width;
            previousHeight = Screen.height;
            previousScreenMode = Screen.fullScreenMode;
            requestedResolution = false;
#if UNITY_EDITOR
            if(Application.isBatchMode)
                foreach(var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    if(window.GetType().FullName=="UnityEditor.SettingsWindow") window.Close();
#endif
            inspectorClickCount = 0;
            isolatedSave = Path.Combine(Path.GetTempPath(), "RepairPropUI_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = isolatedSave;
            TwoNightRun.Clear();
            Time.timeScale = 1;
#if UNITY_EDITOR
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), Is.Not.Null,
                "Generate the authored clinic before running; this fixture does not synthesize a scene.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Requires the authored editor scene; does not change build settings.");
            yield break;
#endif
            loaded = SceneManager.GetSceneByPath(ScenePath);
            yield return Until(() =>
            {
                clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>();
                return clinic != null && clinic.Initialized;
            }, "The real clinic must initialize.");
            Assert.That(clinic.gameObject.scene, Is.EqualTo(loaded));
            Assert.That(Station.Session.CaseCount, Is.EqualTo(4));
            clinic.Robot.Flow.Rig.Walker.LeaveSystemCursor = true;
            mouse = FirstOrderAcceptanceDriver.CreateVirtualMouse(out cleanupMouse);
            QueueMouse(new Vector2(-100, -100));
            yield return null;
            Assert.That(EventSystem.current, Is.Not.Null);
            yield return Until(() => EventSystem.current.currentInputModule is InputSystemUIInputModule,
                "The authored EventSystem must use InputSystemUIInputModule.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (clinic != null && Station != null && Station.Inspector != null)
                Station.Inspector.Clicked -= CountInspectorClick;
            // Unload before restoring the caller's static run/save state.
            if (loaded.IsValid() && loaded.isLoaded)
            {
                var cleanup = SceneManager.CreateScene("RepairPropUICleanup_" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(cleanup);
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
            cleanupMouse?.Invoke();
            cleanupMouse = null;
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            TwoNightRun.Set(previousRun);
            TwoNightSave.OverrideDirectory = previousSave;
            Time.timeScale = previousTimeScale;
            Cursor.lockState = previousCursor;
            Cursor.visible = previousCursorVisible;
            if (requestedResolution)
            {
#if UNITY_EDITOR
                gameView?.Dispose();gameView=null;
#endif
                Screen.SetResolution(previousWidth, previousHeight, previousScreenMode);
                for (int i = 0; i < 30 && (Screen.width != previousWidth || Screen.height != previousHeight); i++)
                    yield return null;
            }
            if (!string.IsNullOrEmpty(isolatedSave) && Directory.Exists(isolatedSave))
            {
                string resolved = Path.GetFullPath(isolatedSave);
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                Assert.That(resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase), Is.True);
                Assert.That(Path.GetFileName(resolved).StartsWith("RepairPropUI_", StringComparison.Ordinal), Is.True);
                Directory.Delete(resolved, true);
            }
            clinic = null; bridge = null; mouse = null;
        }

        [UnityTest]
        public IEnumerator IntakeToBench_QueuedPowerAndFixtureClicks_AutoPickToolExactlyOnce()
        {
            yield return ReceiveAndBench();
            Assert.That(bridge.Station, Is.SameAs(Station));
            Assert.That(Flow.GetState("power"), Is.EqualTo(1));
            Assert.That(Flow.GetState("fixture"), Is.EqualTo(1));
            Assert.That(Station.ActiveTool.HasValue, Is.False);
            Assert.That(bridge.SelectedTarget, Is.EqualTo(Flow.Definition.FindCommand("power.off").target));
            var power = bridge.ContextPanel.CommandButton("power.off");
            Assert.That(power, Is.Not.Null);
            Assert.That(power.interactable, Is.True, "Panel readiness uses the command's tool even when none is selected.");
            AssertTextFits(power.GetComponentInChildren<Text>(), 16, (RectTransform)power.transform);
            int history = Flow.History.Count;
            int invalid = Station.Session.CurrentRecord.InvalidActionCount;
            Quaternion rotation = Station.Inspector.ItemAnchor.rotation;
            yield return Click(power);
            Assert.That(Flow.GetState("power"), Is.Zero);
            Assert.That(Station.ActiveTool, Is.EqualTo(RepairActionType.OpenHousing));
            Assert.That(Flow.History.Count, Is.EqualTo(history + 1), "One physical press/release must execute once.");
            Assert.That(Station.Session.CurrentRecord.InvalidActionCount, Is.EqualTo(invalid));
            Assert.That(Quaternion.Angle(rotation, Station.Inspector.ItemAnchor.rotation), Is.LessThan(.01f));
            yield return null;
            yield return null;
            Assert.That(Flow.History.Count, Is.EqualTo(history + 1), "No repeat dispatch on later frames.");
            var disconnect = bridge.ContextPanel.CommandButton("fixture.disconnect");
            Assert.That(disconnect, Is.Not.Null);
            Assert.That(disconnect.interactable, Is.True);
            AssertTextFits(disconnect.GetComponentInChildren<Text>(), 16, (RectTransform)disconnect.transform);
            yield return Click(disconnect);
            Assert.That(Flow.GetState("fixture"), Is.Zero);
            Assert.That(Flow.History.Count, Is.EqualTo(history + 2));
            Assert.That(Station.ActiveTool, Is.EqualTo(RepairActionType.ServiceModule));
            Assert.That(Station.Session.CurrentRecord.InvalidActionCount, Is.EqualTo(invalid));
            Assert.That(inspectorClickCount, Is.Zero, "Command clicks must not also reach ItemInspector.Clicked.");

            int inspectorClicks = 0;
            Action<Vector2> onInspectorClick = _ => inspectorClicks++;
            Station.Inspector.Clicked += onInspectorClick;
            try
            {
                // Hold and move over the real panel, so a leaked inspector press would rotate the item.
                var panel = bridge.ContextPanel;
                Vector2 start = Center(panel.TargetPicker.GetComponent<RectTransform>());
                QueueMouse(start);
                yield return null;
                yield return null;
                QueueMouse(start, true);
                yield return null;
                QueueMouse(start + new Vector2(-18, -12), true);
                yield return null;
                QueueMouse(start + new Vector2(-18, -12));
                yield return null;
                yield return null;
                panel.TargetPicker.Hide();
                Assert.That(inspectorClicks, Is.Zero, "Panel input must be consumed before ItemInspector.Clicked.");
                Assert.That(Quaternion.Angle(rotation, Station.Inspector.ItemAnchor.rotation), Is.LessThan(.01f));
                Assert.That(Flow.History.Count, Is.EqualTo(history + 2));
            }
            finally { Station.Inspector.Clicked -= onInspectorClick; }
        }

        [UnityTest]
        public IEnumerator RearCover_DisabledReasonAndAllDispatchPathsStayBlocked_BridgeDisableHidesForVr()
        {
            yield return ReceiveAndBench();
            Assert.That(Flow.Definition.kind, Is.EqualTo(RepairPropKind.Receiver));
            string cover = Flow.Definition.FindCommand("cover.remove").target;
            Assert.That(cover, Does.Contain("RearCover"));
            yield return SelectTargetWithPicker(cover);
            Assert.That(bridge.SelectedTarget, Is.EqualTo(cover));
            int invalid = Station.Session.CurrentRecord.InvalidActionCount;
            var history = Flow.History.ToArray();
            var state = StateSnapshot();
            var originalTool = Station.ActiveTool;
            Assert.That(bridge.ExecuteFromPanel("power.off"), Is.False, "A panel command cannot silently retarget the item.");
            Assert.That(Station.ActiveTool, Is.EqualTo(originalTool));
            Assert.That(bridge.SelectedTarget, Is.EqualTo(cover));
            foreach (string id in new[] { "cover.remove", "cover.install" })
            {
                yield return AssertDisabledAndClick(id);
                Assert.That(bridge.ExecuteFromPanel(id), Is.False);
                CollectionAssert.AreEqual(history, Flow.History);
                AssertState(state);
                Assert.That(Station.Session.CurrentRecord.InvalidActionCount, Is.EqualTo(invalid));
            }

            // Diagnose normally, then make safe: installed fasteners must still prevent cover movement.
            yield return SelectTargetWithPicker(Flow.Definition.rootObjectName);
            foreach (var test in Flow.Definition.tests)
                Assert.That(bridge.ExecuteFromPanel("test." + test.id), Is.True, Flow.LastFeedback);
            Assert.That(bridge.ExecuteFromPanel("power.off"), Is.True, Flow.LastFeedback);
            Assert.That(bridge.ExecuteFromPanel("fixture.disconnect"), Is.True, Flow.LastFeedback);
            yield return SelectTargetWithPicker(cover);
            history = Flow.History.ToArray();
            state = StateSnapshot();
            yield return AssertDisabledAndClick("cover.remove");
            Assert.That(bridge.ExecuteFromPanel("cover.remove"), Is.False);
            CollectionAssert.AreEqual(history, Flow.History);
            AssertState(state);
            Assert.That(Station.Session.CurrentRecord.InvalidActionCount, Is.EqualTo(invalid));

            yield return SelectTargetWithPicker(Flow.Definition.rootObjectName);
            var panel = bridge.ContextPanel;
            var staleButton = panel.CommandButton("fixture.connect");
            Assert.That(staleButton.interactable, Is.True);
            Vector2 oldPoint = Center((RectTransform)staleButton.transform);
            int canvasCount = panel.GetComponentsInChildren<Canvas>(true).Length;
            history = Flow.History.ToArray();
            originalTool = Station.ActiveTool;
            string record = JsonUtility.ToJson(Station.Session.CurrentRecord);
            bridge.enabled = false; // ClinicVrSession disables the desktop bridge on takeover.
            yield return null;
            yield return null;
            Assert.That(bridge.CanInteract, Is.False);
            Assert.That(panel.Visible, Is.False, "A separate canvas must follow the disabled desktop bridge.");
            Assert.That(panel.ContainsScreenPoint(oldPoint), Is.False);
            Assert.That(TopRaycastBelongsTo(panel.PanelRectTransform.gameObject, oldPoint), Is.False);
            Assert.That(bridge.ExecuteFromPanel("fixture.connect"), Is.False);
            Assert.That(bridge.ExecuteSelected("fixture.connect"), Is.False);
            Assert.That(bridge.SelectTarget(cover), Is.False);
            bridge.SelectToolFromPanel(RepairActionType.RemoveFastener);
            bridge.InspectSelected();
            staleButton.onClick.Invoke();
            ExecuteEvents.Execute(staleButton.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            Assert.That(Station.ActiveTool, Is.EqualTo(originalTool));
            Assert.That(Flow.GetState("fixture"), Is.Zero);
            CollectionAssert.AreEqual(history, Flow.History);
            Assert.That(JsonUtility.ToJson(Station.Session.CurrentRecord), Is.EqualTo(record));
            bridge.enabled = true;
            yield return Until(() => panel.Visible && bridge.CanInteract, "Desktop panel must recover when bridge ownership returns.");
            Assert.That(bridge.ContextPanel, Is.SameAs(panel));
            Assert.That(panel.GetComponentsInChildren<Canvas>(true).Length, Is.EqualTo(canvasCount));

            bridge.SelectToolFromPanel(null);
            Assert.That(Station.ActiveTool.HasValue, Is.False);
            Assert.That(bridge.ExecuteSelected("fixture.connect"), Is.False, "Strict legacy dispatch must still require a selected tool.");
            Assert.That(Flow.GetState("fixture"), Is.Zero);
            CollectionAssert.AreEqual(history, Flow.History);
            Assert.That(Station.Session.CurrentRecord.InvalidActionCount, Is.EqualTo(invalid));
        }

        [UnityTest]
        public IEnumerator ResponsiveCanvas_LongLabelsAndQueuedWheel_UseExplicitFontSizesAtRequestedResolutions()
        {
            yield return ReceiveAndBench();
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080) })
            {
                yield return RequestResolution(size);
                yield return SelectTargetWithPicker(Flow.Definition.rootObjectName);
                var panel = bridge.ContextPanel;
                var canvases = panel.GetComponentsInChildren<Canvas>(true);
                Assert.That(canvases.Length, Is.EqualTo(1), "The context panel owns one independent canvas.");
                var canvas = canvases[0];
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
                Assert.That(canvas.sortingOrder, Is.EqualTo(55));
                Assert.That(canvas.GetComponent<GraphicRaycaster>(), Is.Not.Null);
                var scaler = canvas.GetComponent<CanvasScaler>();
                Assert.That(scaler, Is.Not.Null);
                Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
                Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1280, 720)));
                Rect screen = ScreenRect(panel.PanelRectTransform);
                Assert.That(screen.width, Is.GreaterThan(100));
                Assert.That(screen.height, Is.GreaterThan(100));
                Assert.That(screen.xMin, Is.GreaterThanOrEqualTo(-1));
                Assert.That(screen.yMin, Is.GreaterThanOrEqualTo(-1));
                Assert.That(screen.xMax, Is.LessThanOrEqualTo(Screen.width + 1));
                Assert.That(screen.yMax, Is.LessThanOrEqualTo(Screen.height + 1));
                Assert.That(screen.center.x, Is.GreaterThan(Screen.width * .5f));
                Assert.That(panel.ContainsScreenPoint(screen.center), Is.True);
                Assert.That(panel.ContainsScreenPoint(new Vector2(-10, -10)), Is.False);
                var providerFont = RepairPropCyberStyle.UiFontOr(UIFontProvider.GetCjkFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));   // the HUD pixel face (CJK-capable)
                Assert.That(providerFont.dynamic, Is.True, "The HUD face is a dynamic font, so every requested size is rasterised.");
                var rootCommands = Flow.Definition.commands.Where(c => c.target == Flow.Definition.rootObjectName).ToArray();
                Assert.That(rootCommands.Length, Is.GreaterThan(3));
                Assert.That(rootCommands.Any(c => c.id == "fixture.disconnect"), Is.True, "Exercise the authored fixture command.");
                foreach (var command in rootCommands)
                {
                    var button = panel.CommandButton(command.id);
                    Assert.That(button, Is.Not.Null, command.id);
                    var text = button.GetComponentInChildren<Text>();
                    Assert.That(text.text, Is.EqualTo(command.label));
                    Assert.That(text.font, Is.SameAs(providerFont));
                    AssertTextFits(text, 16, (RectTransform)button.transform);
                }
                AssertTextFits(panel.TargetPicker.captionText, 15, (RectTransform)panel.TargetPicker.transform);
                Assert.That(panel.TargetPicker.itemText.fontSize, Is.EqualTo(15));
                Assert.That(((RectTransform)panel.TargetPicker.transform.parent).rect.height,Is.LessThanOrEqualTo(38.5f),"Part selector row must not consume scroll-area height.");
                var toolRow=panel.GetComponentsInChildren<Button>().First(b=>b.name=="检测仪").transform.parent;
                Assert.That(((RectTransform)toolRow).rect.height,Is.LessThanOrEqualTo(34.5f),"Tool row must retain its fixed height.");
                var wrapped=panel.CommandButton("fixture.disconnect").GetComponentInChildren<Text>();
                string authored=wrapped.text;
                try
                {
                    wrapped.text=authored+"（外部测试接口／负载连接）";
                    yield return Layout();
                    Assert.That(wrapped.preferredHeight,Is.GreaterThan(wrapped.fontSize),"Exercise a genuine multi-line caption without changing case data.");
                    AssertTextFits(wrapped,16,(RectTransform)wrapped.transform.parent);
                }
                finally {wrapped.text=authored;}

                var scroll = panel.Scroll;
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 1;
                yield return Layout();
                Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height + 1), "Real command content must overflow.");
                Vector2 point = Center(scroll.viewport);
                yield return Until(() => TopRaycastBelongsTo(scroll.gameObject, point), "Wheel must hover the real ScrollRect.");
                QueueMouse(point);
                yield return null;
                yield return null;
                float position = scroll.content.anchoredPosition.y;
                float normalized = scroll.verticalNormalizedPosition;
                float distance = Station.Inspector.TargetDistance;
                var history = Flow.History.ToArray();
                int invalid = Station.Session.CurrentRecord.InvalidActionCount;
                QueueMouse(point, false, new Vector2(0, -120));
                yield return null;
                QueueMouse(point);
                yield return Until(() => Mathf.Abs(scroll.content.anchoredPosition.y - position) > 1,
                    "Queued InputSystem mouse wheel must move the actual content.");
                Assert.That(scroll.verticalNormalizedPosition, Is.LessThan(normalized));
                Assert.That(Station.Inspector.TargetDistance, Is.EqualTo(distance).Within(.001f), "UI wheel must not zoom the item.");
                CollectionAssert.AreEqual(history, Flow.History);
                Assert.That(Station.Session.CurrentRecord.InvalidActionCount, Is.EqualTo(invalid));
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 1;
                yield return Layout();
                CaptureComposition("root-requested-" + size.x + "x" + size.y);
                yield return SelectTargetWithPicker(Flow.Definition.FindCommand("cover.remove").target);
                AssertTextFits(panel.TargetPicker.captionText, 15, (RectTransform)panel.TargetPicker.transform);
                CaptureComposition("rear-cover-requested-" + size.x + "x" + size.y);
            }
        }

        [UnityTest]
        public IEnumerator ActualTargetDropdown_QueuedMouseSelectsRearCover()
        {
            yield return ReceiveAndBench();
            var picker=bridge.ContextPanel.TargetPicker;
            var point=Center((RectTransform)picker.transform);
            yield return Until(()=>TopRaycastBelongsTo(picker.gameObject,point),"The real part selector must receive pointer events.");
            QueueMouse(point);yield return null;yield return null;
            QueueMouse(point,true);yield return null;
            QueueMouse(point);yield return null;
            string cover=Flow.Definition.FindCommand("cover.remove").target;
            string label=Flow.Definition.FindNode(cover).displayName;
            Toggle entry=null;
            yield return Until(()=>
            {
                entry=Object.FindObjectsByType<Toggle>(FindObjectsSortMode.None)
                    .FirstOrDefault(t=>t.isActiveAndEnabled&&t.GetComponentInChildren<Text>()?.text==label);
                return entry!=null;
            },"Opening the actual Dropdown must create its RearCover entry.");
            Canvas.ForceUpdateCanvases();
            point=Center((RectTransform)entry.transform);
            yield return Until(()=>TopRaycastBelongsTo(entry.gameObject,point),"RearCover popup entry must raycast above the panel.");
            QueueMouse(point);yield return null;yield return null;
            QueueMouse(point,true);yield return null;
            QueueMouse(point);yield return null;
            yield return Until(()=>bridge.SelectedTarget==cover,"Clicking the actual popup must select RearCover.");
            yield return new WaitForSecondsRealtime(.2f);
            yield return Layout();
            Assert.That(picker.captionText.text,Is.EqualTo(label));
            Assert.That(bridge.ContextPanel.CommandButton("cover.remove"),Is.Not.Null);
            Assert.That(inspectorClickCount,Is.Zero,"Dropdown clicks must not reach the model inspector.");
        }

        IEnumerator AssertDisabledAndClick(string id)
        {
            yield return Layout();
            var command = Flow.Definition.FindCommand(id);
            Assert.That(Flow.CanExecute(id, bridge.SelectedTarget, command.tool, out string reason), Is.False);
            Assert.That(reason, Is.Not.Null.And.Not.Empty);
            var button = bridge.ContextPanel.CommandButton(id);
            Assert.That(button, Is.Not.Null, id);
            Assert.That(button.interactable, Is.False, id);
            var reasonText = bridge.ContextPanel.Scroll.content.GetComponentsInChildren<Text>()
                .FirstOrDefault(t => t.isActiveAndEnabled && t.text == reason);
            Assert.That(reasonText, Is.Not.Null, "The disabled command must show its actual prerequisite reason: " + id);
            Assert.That(reasonText.rectTransform.rect.height + 1, Is.GreaterThanOrEqualTo(reasonText.preferredHeight));
            var state = StateSnapshot();
            var history = Flow.History.ToArray();
            int invalid = Station.Session.CurrentRecord.InvalidActionCount;
            var tool = Station.ActiveTool;
            ExecuteEvents.Execute(button.gameObject,
                new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            button.onClick.Invoke(); // A stale/programmatic callback must also respect the bridge gate.
            yield return Click(button);
            CollectionAssert.AreEqual(history, Flow.History);
            AssertState(state);
            Assert.That(Station.ActiveTool, Is.EqualTo(tool));
            Assert.That(Station.Session.CurrentRecord.InvalidActionCount, Is.EqualTo(invalid));
        }

        IEnumerator SelectTargetWithPicker(string target)
        {
            var panel = bridge.ContextPanel;
            var node = Flow.Definition.FindNode(target);
            Assert.That(node, Is.Not.Null);
            int index = panel.TargetPicker.options.FindIndex(option => option.text == node.displayName);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "The actual target dropdown must include " + target);
            panel.TargetPicker.SetValueWithoutNotify(index);
            panel.TargetPicker.onValueChanged.Invoke(index);
            yield return Layout();
            Assert.That(bridge.SelectedTarget, Is.EqualTo(target));
            Assert.That(panel.TargetPicker.captionText.text, Is.EqualTo(node.displayName));
        }

        Dictionary<string, int> StateSnapshot()
        {
            var keys = Flow.Definition.initialState.Select(c => c.key)
                .Concat(Flow.Definition.commands.SelectMany(c => c.effects).Select(c => c.key)).Distinct();
            return keys.ToDictionary(key => key, Flow.GetState);
        }

        void AssertState(Dictionary<string, int> state)
        {
            foreach (var pair in state) Assert.That(Flow.GetState(pair.Key), Is.EqualTo(pair.Value), pair.Key);
        }

        IEnumerator RequestResolution(Vector2Int size)
        {
            requestedResolution = true;
#if UNITY_EDITOR
            if(gameView==null) gameView=new UiTestGameView();
            gameView.Set(size.x,size.y);
#endif
            Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
            for (int i = 0; i < 30 && (Screen.width != size.x || Screen.height != size.y); i++) yield return null;
            yield return Layout();
            Assert.That(new Vector2Int(Screen.width,Screen.height),Is.EqualTo(size),"The editor GameView must actually adopt the requested size.");
            TestContext.WriteLine("Requested " + size.x + "x" + size.y + "; actual canvas " + Screen.width + "x" + Screen.height);
        }

        static void AssertTextFits(Text text, int size, RectTransform container)
        {
            Assert.That(text, Is.Not.Null);
            Assert.That(text.fontSize, Is.EqualTo(size), text.text);
            Assert.That(text.resizeTextForBestFit, Is.False, "Explicit sizes must survive a base-32 font.");
            Assert.That(text.rectTransform.rect.width, Is.GreaterThan(20));
            Assert.That(text.rectTransform.rect.height + .5f, Is.GreaterThanOrEqualTo(text.preferredHeight), "Full label height: " + text.text);
            Rect bounds = ScreenRect(text.rectTransform);
            Rect parent = ScreenRect(container);
            Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(parent.xMin - 1), text.text);
            Assert.That(bounds.xMax, Is.LessThanOrEqualTo(parent.xMax + 1), text.text);
            Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(parent.yMin - 1), text.text);
            Assert.That(bounds.yMax, Is.LessThanOrEqualTo(parent.yMax + 1), text.text);
            Assert.That(text.rectTransform.rect.height, Is.GreaterThanOrEqualTo(size), "A full-height label cannot collapse to a thin strip.");
            if (text.horizontalOverflow == HorizontalWrapMode.Overflow)
                Assert.That(text.preferredWidth, Is.LessThanOrEqualTo(text.rectTransform.rect.width + .5f), text.text);
        }

        static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            Vector2 max = min;
            foreach (var corner in corners)
            {
                var point = RectTransformUtility.WorldToScreenPoint(null, corner);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        void CaptureComposition(string name)
        {
            if (Environment.GetEnvironmentVariable("BORDER_REPAIR_UI_CAPTURE") != "1") return;
            const string folder = "C:/Users/Administrator/Documents/Codex/2026-09-29/border-repair-station-art/work/repair-props-ui-fix-2026-10-18/Evidence";
            var camera = Station.Inspector.ViewCamera;
            var overlays = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay)
                .Select(c => new CanvasSnapshot(c)).ToArray();
            Assert.That(overlays.Length, Is.GreaterThan(0));
            int width = Screen.width, height = Screen.height;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            int previousMask = camera.cullingMask;
            var target = RenderTexture.GetTemporary(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            var contextCanvas = bridge.ContextPanel.PanelRectTransform.GetComponentInParent<Canvas>();
            bool contextEnabled = contextCanvas.enabled;
            Rect panelBounds = ScreenRect(bridge.ContextPanel.PanelRectTransform);
            try
            {
                camera.targetTexture = target;
                camera.cullingMask = -1;
                foreach (var overlay in overlays)
                {
                    overlay.Canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    overlay.Canvas.worldCamera = camera;
                    overlay.Canvas.planeDistance = camera.nearClipPlane + .1f;
                }
                // Render the live scene and every overlay together; no end-of-frame yield in batch mode.
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                var composition = pixels.GetPixels32();
                byte[] png = pixels.EncodeToPNG();

                // Prove that the captured image contains the live context canvas, not only the 3D view.
                contextCanvas.enabled = false;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                var withoutPanel = pixels.GetPixels32();
                int changed = 0;
                for (int y = Mathf.Max(0, Mathf.CeilToInt(panelBounds.yMin)); y < Mathf.Min(height, panelBounds.yMax); y += 3)
                    for (int x = Mathf.Max(0, Mathf.CeilToInt(panelBounds.xMin)); x < Mathf.Min(width, panelBounds.xMax); x += 3)
                    {
                        int i = y * width + x;
                        var a = composition[i]; var b = withoutPanel[i];
                        if (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 12) changed++;
                    }
                Assert.That(changed, Is.GreaterThan(100), "Camera.Render must include the actual uGUI panel.");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, name + "-actual-" + width + "x" + height + ".png");
                File.WriteAllBytes(path, png);
                TestContext.WriteLine("Live canvas composition: " + path);
            }
            finally
            {
                contextCanvas.enabled = contextEnabled;
                foreach (var overlay in overlays) overlay.Restore();
                camera.targetTexture = previousTarget;
                camera.cullingMask = previousMask;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(pixels);
                Canvas.ForceUpdateCanvases();
            }
        }

        sealed class CanvasSnapshot
        {
            public readonly Canvas Canvas;
            readonly RenderMode mode;
            readonly Camera camera;
            readonly float planeDistance;
            public CanvasSnapshot(Canvas canvas)
            {
                Canvas = canvas; mode = canvas.renderMode;
                camera = canvas.worldCamera; planeDistance = canvas.planeDistance;
            }
            public void Restore()
            {
                Canvas.renderMode = mode; Canvas.worldCamera = camera; Canvas.planeDistance = planeDistance;
            }
        }

        void CountInspectorClick(Vector2 point) => inspectorClickCount++;

        IEnumerator ReceiveAndBench()
        {
            Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True, clinic.LastMessage);
            var item = Station.Inspector.CurrentItem;
            bridge = item.GetComponent<RepairPropStationBridge>();
            Assert.That(bridge, Is.Not.Null);
            yield return Until(() => Flow.Session != null && bridge.ContextPanel != null,
                "The mounted bridge and separate context panel must bind through their lifecycle.");
            Assert.That(clinic.Carrying, Is.True);
            Assert.That(Station.Session.Stage, Is.EqualTo(RepairStage.Intake));
            Assert.That(bridge.ContextPanel.Visible, Is.False);
            clinic.Counter.DismissDialogue();
            Assert.That(clinic.Interact(ClinicTradeAction.Console), Is.True, clinic.LastMessage);
            yield return Until(() => bridge.CanInteract && bridge.ContextPanel.Visible && CameraSettled(),
                "The real bench and inspection camera must settle before UI input.");
            Assert.That(Flow.Session, Is.SameAs(Station.Session));
            Assert.That(Station.Session.Stage, Is.EqualTo(RepairStage.Inspect));
            yield return Layout();
            Station.Inspector.Clicked += CountInspectorClick;
        }

        bool CameraSettled()
        {
            var inspector = Station.Inspector;
            var camera = inspector.ViewCamera;
            return camera.isActiveAndEnabled
                && Mathf.Abs(Vector3.Distance(camera.transform.position, inspector.LookPoint) - inspector.TargetDistance) < .001f
                && Vector3.Angle(camera.transform.forward, inspector.LookPoint - camera.transform.position) < .1f;
        }

        IEnumerator Layout()
        {
            bridge.ContextPanel.Refresh();
            yield return null;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(bridge.ContextPanel.PanelRectTransform);
            Canvas.ForceUpdateCanvases();
            yield return null;
        }

        IEnumerator Click(Button button)
        {
            Assert.That(button, Is.Not.Null);
            yield return Layout();
            Vector2 point = Center((RectTransform)button.transform);
            Assert.That(bridge.ContextPanel.ContainsScreenPoint(point), Is.True);
            yield return Until(() => TopRaycastBelongsTo(button.gameObject, point),
                "The actual canvas must raycast to the command before clicking: " + button.name);
            QueueMouse(point);
            yield return null;
            yield return null;
            Assert.That(EventSystem.current.IsPointerOverGameObject(mouse.deviceId), Is.True,
                "The InputSystem UI module must observe the hovered virtual mouse before the press.");
            QueueMouse(point, true);
            yield return null;
            QueueMouse(point);
            yield return null;
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

        static Vector2 Center(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));

        static bool TopRaycastBelongsTo(GameObject root, Vector2 point)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            return hits.Count > 0 && (hits[0].gameObject == root || hits[0].gameObject.transform.IsChildOf(root.transform));
        }

        static IEnumerator Until(Func<bool> ready, string reason)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, reason);
        }
    }
}
