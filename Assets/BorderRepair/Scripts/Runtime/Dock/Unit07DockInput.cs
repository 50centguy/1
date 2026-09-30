using UnityEngine;
using UnityEngine.InputSystem;

namespace BorderRepair.Dock
{
    /// <summary>测试场景的鼠标点击：从镜头打射线，命中 DockInteractable 就交给 Unit07DockController。另有简单的 IMGUI 提示。</summary>
    public class Unit07DockInput : MonoBehaviour
    {
        [SerializeField] Camera viewCamera;
        [SerializeField] Unit07DockController controller;
        [SerializeField] float maxDistance = 6f;
        [SerializeField] bool showHud = true;

        public Camera ViewCamera => viewCamera;

        public void Configure(Camera cam, Unit07DockController dock)
        {
            viewCamera = cam;
            controller = dock;
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            TryClick(mouse.position.ReadValue(), out _);
        }

        /// <summary>按屏幕坐标点击；返回命中的代理（没命中为 null）。</summary>
        public bool TryClick(Vector2 screenPos, out DockInteractable hit)
        {
            hit = Pick(screenPos);
            if (hit == null || hit.action == DockAction.ContactPad) return false;
            return controller.Interact(hit.action, hit.pickable);
        }

        public DockInteractable Pick(Vector2 screenPos)
        {
            if (viewCamera == null) return null;
            var ray = viewCamera.ScreenPointToRay(screenPos);
            return Physics.Raycast(ray, out var info, maxDistance, ~0, QueryTriggerInteraction.Collide)
                ? info.collider.GetComponent<DockInteractable>()
                : null;
        }

        void OnGUI()
        {
            if (!showHud || controller == null) return;
            GUI.Box(new Rect(12, 12, 460, 96), GUIContent.none);
            GUI.Label(new Rect(22, 18, 440, 22), $"UNIT 07 维修座 · 状态：{controller.State} · 供电：{(controller.PowerOn ? "ON" : "OFF")}");
            GUI.Label(new Rect(22, 42, 440, 22), "下一步：" + controller.NextHint());
            GUI.Label(new Rect(22, 66, 440, 36), controller.LastMessage);
        }
    }
}
