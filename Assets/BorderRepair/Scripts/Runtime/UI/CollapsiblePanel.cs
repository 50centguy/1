using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BorderRepair.UI
{
    /// <summary>
    /// 可收起的面板：收起时只保留标题栏（面板高度缩到 collapsedHeight，正文隐藏），点击标题栏上的按钮展开 / 收起。
    /// 面板锚在顶部，收起时从下往上缩。只负责显示；什么时候收起由使用它的组件决定。
    /// </summary>
    public class CollapsiblePanel : MonoBehaviour
    {
        [SerializeField] RectTransform panel;
        [SerializeField] float expandedHeight = 400f;
        [SerializeField] float collapsedHeight = 58f;
        [Tooltip("收起时隐藏的正文")]
        [SerializeField] GameObject[] body = new GameObject[0];
        [SerializeField] Button toggleButton;
        [SerializeField] Text toggleLabel;
        [SerializeField] string expandedLabel = "收起 ▴";
        [SerializeField] string collapsedLabel = "展开 ▾";

        public bool Expanded { get; private set; } = true;
        public RectTransform Panel => panel;
        public string ToggleText => toggleLabel != null ? toggleLabel.text : string.Empty;
        public event Action<bool> Toggled;

        string collapsedOverride;

        /// <summary>供编辑器生成工具配置。</summary>
        public void Configure(RectTransform panelRect, float expanded, float collapsed, GameObject[] bodyObjects, Button button, Text label,
                              string expandedText, string collapsedText)
        {
            panel = panelRect;
            expandedHeight = expanded;
            collapsedHeight = collapsed;
            body = bodyObjects;
            toggleButton = button;
            toggleLabel = label;
            expandedLabel = expandedText;
            collapsedLabel = collapsedText;
        }

        void Awake()
        {
            if (toggleButton != null)
                toggleButton.onClick.AddListener(() =>
                {
                    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                    Toggle();
                });
        }

        public void Toggle() => SetExpanded(!Expanded, true);

        public void SetExpanded(bool expanded, bool byUser = false)
        {
            Expanded = expanded;
            if (panel != null)
            {
                var size = panel.sizeDelta;
                size.y = expanded ? expandedHeight : collapsedHeight;
                panel.sizeDelta = size;
            }
            foreach (var b in body) if (b != null) b.SetActive(expanded);
            RefreshLabel();
            if (byUser) Toggled?.Invoke(expanded);
        }

        /// <summary>收起状态下按钮上显示的文字（例如带未读标记）；传 null 恢复默认。</summary>
        public void SetCollapsedLabel(string text)
        {
            collapsedOverride = text;
            RefreshLabel();
        }

        void RefreshLabel()
        {
            if (toggleLabel != null) toggleLabel.text = Expanded ? expandedLabel : (collapsedOverride ?? collapsedLabel);
        }
    }
}
