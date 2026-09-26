using System;
using BorderRepair.Data;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BorderRepair.UI
{
    /// <summary>
    /// 维修工具栏：螺丝刀（卸下固定件）、撬片（打开外壳）、检测仪（检测 / 更换模块）。
    /// 只负责显示与发出“选择工具”事件；规则由 RepairSession.PerformAction 决定。
    /// 只在带维修步骤的案件中显示（默认营业场景不含本组件）。
    /// </summary>
    public class RepairToolbarView : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] Button handButton;
        [SerializeField] Button fastenerButton;
        [SerializeField] Button housingButton;
        [SerializeField] Button moduleButton;
        [SerializeField] Text statusText;

        static readonly Color Normal = new Color(0.24f, 0.28f, 0.33f);
        static readonly Color Selected = new Color(0.86f, 0.5f, 0.14f);

        /// <summary>选择的工具；null 表示空手（单击部位时按扫描处理）。</summary>
        public event Action<RepairActionType?> ToolSelected;

        void Awake()
        {
            var labels = GetComponentsInChildren<Text>(true);
            var font = UIFontProvider.GetCjkFont(labels.Length > 0 ? labels[0].font : null);
            if (font != null) foreach (var t in labels) t.font = font;

            Bind(handButton, null);
            Bind(fastenerButton, RepairActionType.RemoveFastener);
            Bind(housingButton, RepairActionType.OpenHousing);
            Bind(moduleButton, RepairActionType.ServiceModule);
        }

        void Bind(Button button, RepairActionType? tool)
        {
            if (button == null) return;
            button.onClick.AddListener(() =>
            {
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                ToolSelected?.Invoke(tool);
            });
        }

        public void SetVisible(bool visible)
        {
            if (panel != null) panel.SetActive(visible);
        }

        public void SetInteractable(bool interactable)
        {
            foreach (var b in new[] { handButton, fastenerButton, housingButton, moduleButton })
                if (b != null) b.interactable = interactable;
        }

        public void SetActiveTool(RepairActionType? tool)
        {
            Paint(handButton, tool == null);
            Paint(fastenerButton, tool == RepairActionType.RemoveFastener);
            Paint(housingButton, tool == RepairActionType.OpenHousing);
            Paint(moduleButton, tool == RepairActionType.ServiceModule);
        }

        public void SetStatus(string text)
        {
            // 旧版 Text 优先在空格处换行：“螺丝 A 卸下，……”会在“A”后面提前断行。换成不换行空格，让中文按字换行。
            if (statusText != null) statusText.text = text != null ? text.Replace(' ', ' ') : string.Empty;
        }

        public string StatusText => statusText != null ? statusText.text : string.Empty;

        static void Paint(Button b, bool on)
        {
            if (b == null) return;
            var img = b.targetGraphic as Image;
            if (img != null) img.color = on ? Selected : Normal;
        }
    }
}
