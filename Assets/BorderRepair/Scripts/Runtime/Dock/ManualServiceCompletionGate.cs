using UnityEngine;
using UnityEngine.InputSystem;

namespace BorderRepair.Dock
{
    /// <summary>
    /// 【占位】只给维修座单独测试场景（Unit07Dock_Test）用的“允许结束维修”实现：没有工单，由测试者按 F 手动确认维修已结束。
    /// 接入工单的场景不放这个组件，也就没有 F 确认。不要在正式流程里沿用。
    /// </summary>
    public class ManualServiceCompletionGate : MonoBehaviour, IDockServiceCompletionGate
    {
        [SerializeField] bool confirmed;

        public bool Confirmed => confirmed;

        public void Confirm() => confirmed = true;
        public void Revoke() => confirmed = false;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.fKey.wasPressedThisFrame) Confirm();
        }

        public bool CanFinishService(out string reason)
        {
            reason = confirmed ? string.Empty : "维修还没确认结束（测试场景：按 F 手动确认，占位）。";
            return confirmed;
        }
    }
}
