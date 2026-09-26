using System;
using System.Collections.Generic;
using BorderRepair.Controls;
using UnityEngine;

namespace BorderRepair.Inspection
{
    /// <summary>扫描模式：悬停高亮可扫描部位，单击时报告命中的检查点。</summary>
    public class ItemScanner : MonoBehaviour
    {
        [SerializeField] ItemInspector inspector;
        [SerializeField] float maxRayDistance = 10f;

        /// <summary>命中检查点。</summary>
        public event Action<InspectionPoint> PointClicked;
        /// <summary>命中物品但不是检查点；或扫描模式关闭时点到物品。</summary>
        public event Action ItemSurfaceClicked;
        /// <summary>扫描模式下点到了物品以外。</summary>
        public event Action MissedItem;
        public event Action<InspectionPoint> HoverChanged;

        public bool ScanModeActive { get; private set; }
        public bool ScanAllowed { get; set; }
        public InspectionPoint Hovered { get; private set; }

        bool showScannedTint = true;

        /// <summary>是否给已扫描的部位上绿色调（默认开启；拿着维修工具时关闭，避免与零件本身的颜色混淆）。</summary>
        public bool ShowScannedTint
        {
            get => showScannedTint;
            set
            {
                if (showScannedTint == value) return;
                showScannedTint = value;
                RefreshVisuals();
            }
        }

        readonly HashSet<InspectionPoint> scanned = new HashSet<InspectionPoint>();
        readonly HashSet<InspectionPoint> anomalies = new HashSet<InspectionPoint>();

        void OnEnable() => inspector.Clicked += OnInspectorClicked;
        void OnDisable() => inspector.Clicked -= OnInspectorClicked;

        public void ResetForItem()
        {
            scanned.Clear();
            anomalies.Clear();
            SetHover(null);
            RefreshVisuals();
        }

        /// <summary>把已扫描的部位标记为异常（诊断叠加层下显示异常样式；旧染色路径下与“已扫描”相同）。</summary>
        public void MarkAnomaly(InspectionPoint point)
        {
            if (point == null) return;
            bool changed = scanned.Add(point) | anomalies.Add(point);
            if (changed) RefreshVisuals();
        }

        /// <summary>按当前状态重新应用所有部位的高亮（例如切换了高亮设置之后）。</summary>
        public void RefreshHighlights() => RefreshVisuals();

        public void SetScanMode(bool active)
        {
            ScanModeActive = active;
            if (!active) SetHover(null);
            RefreshVisuals();
        }

        public void MarkScanned(InspectionPoint point)
        {
            if (point != null && scanned.Add(point)) RefreshVisuals();
        }

        void Update()
        {
            if (!ScanAllowed || !ScanModeActive || RepairInput.PointerOverUI)
            {
                SetHover(null);
                return;
            }
            Raycast(RepairInput.PointerPosition, out var point, out _);
            SetHover(point);
        }

        void OnInspectorClicked(Vector2 screenPosition)
        {
            if (!ScanAllowed) return;
            Raycast(screenPosition, out var point, out bool hitItem);
            if (!ScanModeActive)
            {
                // 未开扫描时点到物品，交给流程层给出“请先开启扫描模式”的提示。
                if (hitItem) ItemSurfaceClicked?.Invoke();
                return;
            }
            if (point != null) PointClicked?.Invoke(point);
            else if (hitItem) ItemSurfaceClicked?.Invoke();
            else MissedItem?.Invoke();
        }

        /// <summary>按屏幕坐标拾取检查点，与鼠标单击走同一条射线逻辑（供测试和调试使用）。</summary>
        public InspectionPoint PickPoint(Vector2 screenPosition, out bool hitItem)
        {
            Raycast(screenPosition, out var point, out hitItem);
            return point;
        }

        bool Raycast(Vector2 screenPosition, out InspectionPoint point, out bool hitItem)
        {
            point = null;
            hitItem = false;
            var item = inspector.CurrentItem;
            if (item == null) return false;

            // 物品刚被旋转/缩放时，物理世界要到下一个物理步才会同步；先同步，避免命中旋转前的碰撞体位置。
            Physics.SyncTransforms();
            Ray ray = inspector.ViewCamera.ScreenPointToRay(screenPosition);
            var hits = Physics.RaycastAll(ray, maxRayDistance, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            foreach (var hit in hits)
            {
                if (!hit.transform.IsChildOf(item.transform) || hit.distance >= best) continue;
                best = hit.distance;
                hitItem = true;
                point = hit.collider.GetComponentInParent<InspectionPoint>();
            }
            return hitItem;
        }

        void SetHover(InspectionPoint point)
        {
            if (Hovered == point) return;
            Hovered = point;
            RefreshVisuals();
            HoverChanged?.Invoke(point);
        }

        void RefreshVisuals()
        {
            var item = inspector.CurrentItem;
            if (item == null) return;
            foreach (var p in item.GetComponentsInChildren<InspectionPoint>(true))
            {
                var state = InspectionPoint.VisualState.Normal;
                if (ScanModeActive)
                {
                    if (p == Hovered) state = InspectionPoint.VisualState.Hover;
                    else if (showScannedTint && scanned.Contains(p))
                        state = anomalies.Contains(p) ? InspectionPoint.VisualState.Anomaly : InspectionPoint.VisualState.Scanned;
                }
                p.SetVisualState(state);
            }
        }
    }
}
