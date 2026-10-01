using UnityEngine;
using UnityEngine.InputSystem;

namespace BorderRepair.Dock
{
    /// <summary>
    /// 测试场景的鼠标点击：从镜头打射线，命中 DockInteractable 就交给 Unit07DockController。另有简单的 IMGUI 提示。
    /// 第二阶段：L = 七号升起离座；F = 【占位】手动确认“维修已结束”（只有场景里接的是 ManualServiceCompletionGate 时才有用，不是工单）。
    /// </summary>
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
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.lKey.wasPressedThisFrame) controller.Interact(DockAction.LiftOff);
                if (kb.fKey.wasPressedThisFrame && controller.ServiceGate is ManualServiceCompletionGate g) g.Confirm();
            }
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
            GUI.Box(new Rect(12, 12, 560, 142), GUIContent.none);
            GUI.Label(new Rect(22, 18, 540, 22), $"UNIT 07 维修座 · 状态：{controller.State} · 供电：{(controller.PowerOn ? "ON" : "OFF")} · 转速 {controller.Rotors.SpeedDegPerSec:F0}°/s" +
                                                 (controller.Rotors.Driven ? "（维修座驱动）" : "（Animator 驱动）"));
            GUI.Label(new Rect(22, 42, 540, 22), "下一步：" + controller.NextHint());
            GUI.Label(new Rect(22, 66, 540, 36), controller.LastMessage);
            string gate = controller.ServiceGate == null ? "未接入" :
                          controller.ServiceGate is ManualServiceCompletionGate m ? (m.Confirmed ? "【占位】已手动确认" : "【占位】未确认（按 F 手动确认）") : "已接入";
            GUI.Label(new Rect(22, 102, 540, 22), $"允许结束维修：{gate}");
            GUI.Label(new Rect(22, 124, 540, 22), "按键：L = 升起离座");
        }
    }
}
