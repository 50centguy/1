using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BorderRepair.Controls
{
    /// <summary>对 Input System 设备 API 的薄封装（项目 activeInputHandler = Input System）。</summary>
    public static class RepairInput
    {
        public static Vector2 PointerPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static Vector2 PointerDelta => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;

        public static bool LeftPressed => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static bool LeftReleased => Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
        public static bool LeftHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;
        public static bool RightPressed => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        public static bool RightHeld => Mouse.current != null && Mouse.current.rightButton.isPressed;

        /// <summary>滚轮格数：向上为正。兼容按 120/格 或已归一化的滚轮值。</summary>
        public static float ScrollSteps
        {
            get
            {
                if (Mouse.current == null) return 0f;
                float y = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(y) < 0.01f) return 0f;
                return Mathf.Abs(y) > 10f ? y / 120f : y;
            }
        }

        public static bool ToggleScanPressed => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        public static bool ResetViewPressed => Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;

        public static bool PointerOverUI => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
