using System;
using System.Collections;
using System.Linq;
using BorderRepair.FirstOrder;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

namespace BorderRepair.RepairProps.UI.Tests
{
    public sealed class RepairPropShopSceneTests
    {
        Scene loaded;
        TwoNightState previous;
        UnifiedClinicDirector clinic;
        RepairPropEconomyController shop;
        RepairPropShopPanel panel;
        Mouse mouse;
        Action cleanupMouse;
        CursorLockMode cursor;
        bool cursorVisible;
#if UNITY_EDITOR
        UiTestGameView gameView;
#endif
        [UnitySetUp] public IEnumerator Setup()
        {
            previous = TwoNightRun.Current; cursor = Cursor.lockState; cursorVisible = Cursor.visible;
            TwoNightRun.Clear();
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(RepairPropEconomyController.ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Authored editor scene required."); yield break;
#endif
            loaded = SceneManager.GetSceneByPath(RepairPropEconomyController.ScenePath);
            yield return Until(() => {
                clinic = Object.FindFirstObjectByType<UnifiedClinicDirector>();
                shop = Object.FindFirstObjectByType<RepairPropEconomyController>();
                return clinic != null && clinic.Initialized && shop != null;
            });
            clinic.Robot.Flow.Rig.Walker.LeaveSystemCursor = true;
            mouse = FirstOrderAcceptanceDriver.CreateVirtualMouse(out cleanupMouse);
            panel = shop.GetComponent<RepairPropShopPanel>();
            Assert.That(panel, Is.Not.Null);
            yield return null;
        }
        [UnityTearDown] public IEnumerator Teardown()
        {
            if (shop != null) shop.Close();
            if (loaded.IsValid() && loaded.isLoaded)
            {
                var cleanup = SceneManager.CreateScene("ShopCleanup_" + Guid.NewGuid().ToString("N"));
                SceneManager.SetActiveScene(cleanup); yield return SceneManager.UnloadSceneAsync(loaded);
            }
#if UNITY_EDITOR
            gameView?.Dispose(); gameView = null;
#endif
            cleanupMouse?.Invoke(); cleanupMouse = null;
            TwoNightRun.Set(previous); Cursor.lockState = cursor; Cursor.visible = cursorVisible;
        }
        [UnityTest] public IEnumerator RealMouseCartCheckoutAndCloseRestoreRepairOwnership()
        {
            Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True);
            yield return null;
            clinic.Counter.DismissDialogue();
            Assert.That(clinic.PlaceOnBench(), Is.True);
            yield return null;
            var flow = clinic.Station.Inspector.CurrentItem.GetComponent<RepairPropFlow>();
            var bridge = flow.GetComponent<RepairPropStationBridge>();
            Assert.That(flow.CommandPolicy, Is.Not.Null);
            Assert.That(ClinicSupplies.Available(shop.State, "rx-rf"), Is.Zero);
            Assert.That(bridge.CanInteract, Is.True);
            yield return new WaitForSeconds(1.1f);
            string replacement = flow.Definition.FindCommand("module.install.new").target;
            var stockedVisual = bridge.BenchPresentation.ParkedVisual(replacement);
            Assert.That(stockedVisual.gameObject.activeInHierarchy, Is.False, "No free replacement should appear on the desk.");
            // The shop is a program on a physical screen, opened from the system launcher's menu.
            Assert.That(RepairPropLauncher.Current, Is.Not.Null);
            yield return Click(RepairPropLauncher.Current.IconButton);
            Assert.That(RepairPropLauncher.Current.MenuOpen, Is.True);
            yield return Click(panel.OpenButton);
            Assert.That(shop.IsOpen, Is.True);
            yield return Until(() => panel.DeviceReady);
            Assert.That(panel.OnScreen, Is.True, "The program runs on the screen at the bench.");
            Assert.That(clinic.Station.Inspector.InteractionEnabled, Is.False);
            Assert.That(bridge.CanInteract, Is.False);
            yield return Click(panel.QuantityButton("rx-rf", true));
            Assert.That(shop.Cart["rx-rf"], Is.EqualTo(1));
            yield return Click(panel.PurchaseButton);
            Assert.That(shop.State.Cash, Is.EqualTo(472));
            Assert.That(ClinicSupplies.Available(shop.State, "rx-rf"), Is.EqualTo(1));
            yield return null;
            Assert.That(stockedVisual.gameObject.activeInHierarchy, Is.True, "The purchased part must become pickable on the real desk.");
            yield return Click(panel.PurchaseButton);
            Assert.That(shop.State.Cash, Is.EqualTo(472));
            var pointer = new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -4) };
            ExecuteEvents.Execute(panel.Scroll.gameObject, pointer, ExecuteEvents.scrollHandler);
            Assert.That(clinic.Station.Inspector.InteractionEnabled, Is.False);
            shop.Close(); yield return null;
            Assert.That(clinic.Station.CleaningActive, Is.False);
            Assert.That(clinic.Station.Inspector.InteractionEnabled, Is.True);
            Assert.That(bridge.CanInteract, Is.True);
            Assert.That(shop.State.transactions.Count, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator DestroyedOrderReturnsOnlyItsUnconsumedAllocation()
        {
            Assert.That(clinic.Interact(ClinicTradeAction.Receive), Is.True); yield return null;
            clinic.Counter.DismissDialogue(); Assert.That(clinic.PlaceOnBench(), Is.True); yield return null;
            var flow = clinic.Station.Inspector.CurrentItem.GetComponent<RepairPropFlow>();
            shop.Open(); shop.SetQuantity("rx-rf", 1); Assert.That(shop.Purchase(), Is.True); shop.Close();
            Assert.That(ClinicSupplies.Reserve(shop.State, flow.Definition.repairCase.caseId, "rx-rf", out _), Is.True);
            Object.Destroy(flow.gameObject); yield return null;
            Assert.That(ClinicSupplies.Available(shop.State, "rx-rf"), Is.EqualTo(1));
            Assert.That(shop.State.Cash, Is.EqualTo(472));
        }
        [UnityTest] public IEnumerator ProcurementScreensFitAndRenderAtThreeRealGameViewSizes()
        {
            Assert.That(shop.Open(), Is.True);
            shop.SetQuantity("rx-rf", 1); shop.SetQuantity("mc-interface", 1);
            yield return Until(() => panel.DeviceReady);
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(1024, 768) })
            {
#if UNITY_EDITOR
                if (gameView == null) gameView = new UiTestGameView();
                gameView.Set(size.x, size.y);
#endif
                for (int i = 0; i < 8; i++) yield return null;
                Assert.That(new Vector2Int(Screen.width, Screen.height), Is.EqualTo(size));
                foreach (int tab in new[] { 0, 1, 2 })
                {
                    panel.ShowTab(tab); yield return null; Canvas.ForceUpdateCanvases();
                    var corners = new Vector3[4]; panel.Window.GetWorldCorners(corners);
                    var view = panel.DeviceCamera;
                    if (view != null) for (int i = 0; i < 4; i++) corners[i] = view.WorldToScreenPoint(corners[i]);
                    Assert.That(corners.All(c => c.x >= 0 && c.x <= Screen.width && c.y >= 0 && c.y <= Screen.height), Is.True);
                    foreach (var text in panel.Window.GetComponentsInChildren<Text>())
                    {
                        if (string.IsNullOrEmpty(text.text)) continue;
                        Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 2), text.text + " clips vertically");
                    }
                    var camera = panel.DeviceCamera != null ? panel.DeviceCamera : Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c => c.isActiveAndEnabled);
                    RepairPropWorkspaceCapture.Capture(camera, "shop-tab-" + tab, size, Array.Empty<Renderer>(), Array.Empty<Transform>());
                }
            }
        }
        IEnumerator Click(Button button)
        {
            Assert.That(button, Is.Not.Null); Canvas.ForceUpdateCanvases();
            var r = (RectTransform)button.transform;
            var root = button.GetComponentInParent<Canvas>().rootCanvas;
            var eye = root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
            Vector2 position = RectTransformUtility.WorldToScreenPoint(eye, r.TransformPoint(r.rect.center));
            var down = new MouseState { position = position }.WithButton(MouseButton.Left, true);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }); yield return null;
            InputSystem.QueueStateEvent(mouse, down); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }); yield return null; yield return null;
        }
        static IEnumerator Until(Func<bool> ready)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True);
        }
    }
}
