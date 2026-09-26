using System;
using System.Collections.Generic;
using BorderRepair.Data;
using UnityEngine;

namespace BorderRepair.Narrative
{
    /// <summary>
    /// 场景级换材质（只放在叙事场景里）：物品放上检查台后，把占位材质换成已有的磨损材质。
    /// 只改这个场景里实例的 sharedMaterials 槽位，不改 prefab，也不改材质资产。
    /// </summary>
    public class ItemMaterialSkin : MonoBehaviour
    {
        [Serializable]
        public class Swap
        {
            public Material from;
            public Material to;
        }

        [SerializeField] RepairStationController controller;
        [SerializeField] List<Swap> swaps = new List<Swap>();

        public int LastSwapCount { get; private set; }
        public IReadOnlyList<Swap> Swaps => swaps;

        /// <summary>供编辑器生成工具配置。</summary>
        public void Configure(RepairStationController c, List<Swap> list)
        {
            controller = c;
            swaps = list;
        }

        void Start()
        {
            if (controller == null || controller.Session == null) { enabled = false; return; }
            controller.Session.CaseStarted += OnCaseStarted;
            Apply(controller.Inspector.CurrentItem);
        }

        void OnDestroy()
        {
            if (controller != null && controller.Session != null) controller.Session.CaseStarted -= OnCaseStarted;
        }

        // 控制器先订阅 CaseStarted 并完成放置，这里拿到的是已经放好的实例
        void OnCaseStarted(RepairCaseData data, int index) => Apply(controller.Inspector.CurrentItem);

        public void Apply(GameObject item)
        {
            LastSwapCount = 0;
            if (item == null) return;
            foreach (var r in item.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    foreach (var s in swaps)
                    {
                        if (s == null || s.from == null || s.to == null || mats[i] != s.from) continue;
                        mats[i] = s.to;
                        changed = true;
                        LastSwapCount++;
                        break;
                    }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
