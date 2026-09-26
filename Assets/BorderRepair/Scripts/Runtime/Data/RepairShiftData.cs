using System.Collections.Generic;
using UnityEngine;

namespace BorderRepair.Data
{
    /// <summary>一次营业（一轮游戏）要处理的送修物列表。</summary>
    [CreateAssetMenu(menuName = "Border Repair/Repair Shift", fileName = "RepairShift")]
    public class RepairShiftData : ScriptableObject
    {
        public string shiftTitle = "边境维修站";
        [Tooltip("目标营业时长（秒），仅用于显示，不会强制结束。")]
        public float targetDurationSeconds = 300f;
        public List<RepairCaseData> cases = new List<RepairCaseData>();
    }
}
