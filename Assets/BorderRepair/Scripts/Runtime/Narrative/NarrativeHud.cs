using BorderRepair.Core;
using BorderRepair.Data;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BorderRepair.Narrative
{
    /// <summary>
    /// 叙事场景的界面布局（只放在叙事场景里）：减少常驻面板对物件的遮挡。
    /// - 顾客说明：接收阶段展开；接收物品（已读）后自动收起，只留标题栏，随时可以再展开。
    /// - 线索记录：检查阶段默认收起，只留标题栏和“扫描 / 线索 n/m / 瞄准”状态行；
    ///   有新线索时按钮显示“● 新线索”，展开后清除。按钮或 Tab 键随时展开 / 收起。
    /// 面板内容仍由 RepairUIView / RepairStationController 维护，本组件只控制展开与收起。
    /// </summary>
    public class NarrativeHud : MonoBehaviour
    {
        [SerializeField] RepairStationController controller;
        [SerializeField] CollapsiblePanel customerPanel;
        [SerializeField] CollapsiblePanel cluePanel;

        RepairSession session;
        bool unread;

        public CollapsiblePanel CustomerPanel => customerPanel;
        public CollapsiblePanel CluePanel => cluePanel;
        public bool HasUnreadClue => unread;

        /// <summary>供编辑器生成工具配置。</summary>
        public void Configure(RepairStationController c, CollapsiblePanel customer, CollapsiblePanel clues)
        {
            controller = c;
            customerPanel = customer;
            cluePanel = clues;
        }

        void Start()
        {
            if (controller == null || controller.Session == null) { enabled = false; return; }
            session = controller.Session;
            session.StageChanged += OnStageChanged;
            session.ClueUnlocked += OnClueUnlocked;
            if (cluePanel != null) cluePanel.Toggled += expanded => { if (expanded) MarkRead(); };
            OnStageChanged(session.Stage);
        }

        void OnDestroy()
        {
            if (session == null) return;
            session.StageChanged -= OnStageChanged;
            session.ClueUnlocked -= OnClueUnlocked;
        }

        void Update()
        {
            if (session == null || cluePanel == null) return;
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame && session.Stage != RepairStage.Summary)
            {
                cluePanel.Toggle();
                if (cluePanel.Expanded) MarkRead();
            }
        }

        void OnStageChanged(RepairStage stage)
        {
            // 接收阶段需要读顾客说明；接收之后说明已读，收起让出画面
            if (customerPanel != null) customerPanel.SetExpanded(stage == RepairStage.Intake);
            if (cluePanel != null && (stage == RepairStage.Intake || stage == RepairStage.Inspect))
            {
                if (stage == RepairStage.Intake) unread = false;
                cluePanel.SetExpanded(false);
                RefreshClueLabel();
            }
        }

        void OnClueUnlocked(ClueDefinition clue)
        {
            if (cluePanel != null && cluePanel.Expanded) return;
            unread = true;
            RefreshClueLabel();
        }

        void MarkRead()
        {
            unread = false;
            RefreshClueLabel();
        }

        void RefreshClueLabel()
        {
            if (cluePanel == null) return;
            cluePanel.SetCollapsedLabel(unread ? "<color=#FFB347>● 新线索</color> ▾ [Tab]" : null);
        }
    }
}
