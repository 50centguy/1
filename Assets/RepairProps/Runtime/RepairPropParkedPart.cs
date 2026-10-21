using UnityEngine;

namespace BorderRepair.RepairProps
{
    // Visual parking proxies keep the original flow target and its safety gates.
    public sealed class RepairPropParkedPart : MonoBehaviour
    {
        public RepairPropFlow Owner { get; private set; }
        public string Target { get; private set; }
        public void Configure(RepairPropFlow owner, string target) { Owner = owner; Target = target; }
    }
}
