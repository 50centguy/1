using UnityEngine;

namespace BorderRepair.Dock
{
    /// <summary>
    /// 【占位】测试场景用的“允许结束维修”实现：没有工单，由测试者手动确认维修已结束。
    /// 正式接入时换成工单 / 维修流程对 <see cref="IDockServiceCompletionGate"/> 的实现，不要沿用这个组件。
    /// </summary>
    public class ManualServiceCompletionGate : MonoBehaviour, IDockServiceCompletionGate
    {
        [SerializeField] bool confirmed;

        public bool Confirmed => confirmed;

        public void Confirm() => confirmed = true;
        public void Revoke() => confirmed = false;

        public bool CanFinishService(out string reason)
        {
            reason = confirmed ? string.Empty : "维修还没确认结束（测试场景：按 F 手动确认，占位）。";
            return confirmed;
        }
    }
}
