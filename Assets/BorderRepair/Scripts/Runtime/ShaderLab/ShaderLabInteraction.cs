using System.Collections.Generic;
using BorderRepair.Controls;
using BorderRepair.Inspection;
using BorderRepair.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BorderRepair.ShaderLab
{
    /// <summary>
    /// ShaderLab 测试场景的交互（只存在于测试场景）：
    /// 鼠标悬停 → 悬停高亮；左键单击 → 普通 / 已扫描 / 异常 循环；右键拖动 → 环绕；滚轮 → 推拉；
    /// H → 在“诊断叠加层”和“原来的底色染色”之间切换，便于对比。
    /// </summary>
    public class ShaderLabInteraction : MonoBehaviour
    {
        [SerializeField] Camera labCamera;
        [SerializeField] DiagnosticHighlightSettings settings;
        [SerializeField] Text statusText;
        [SerializeField] Vector3 orbitTarget = new Vector3(0.15f, 0.08f, 0.05f);

        readonly Dictionary<InspectionPoint, InspectionPoint.VisualState> assigned = new Dictionary<InspectionPoint, InspectionPoint.VisualState>();
        InspectionPoint hovered;
        float yaw, pitch = 18f, distance = 1.35f;

        void Start()
        {
            if (statusText != null)
            {
                var font = UIFontProvider.GetCjkFont(statusText.font);
                if (font != null) statusText.font = font;
            }
            var offset = labCamera.transform.position - orbitTarget;
            distance = offset.magnitude;
            // Euler(pitch, yaw) * (0,0,-d) = (-d·cos p·sin y, d·sin p, -d·cos p·cos y)
            yaw = Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(offset.y / distance, -1f, 1f)) * Mathf.Rad2Deg;
            UpdateStatus();
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame) ToggleHighlightMode();

            if (RepairInput.RightHeld)
            {
                var d = RepairInput.PointerDelta * 0.25f;
                yaw += d.x;
                pitch = Mathf.Clamp(pitch - d.y, 5f, 80f);
            }
            float scroll = RepairInput.ScrollSteps;
            if (scroll != 0f) distance = Mathf.Clamp(distance * (1f - scroll * 0.1f), 0.2f, 3f);
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            labCamera.transform.position = orbitTarget + rot * new Vector3(0f, 0f, -distance);
            labCamera.transform.LookAt(orbitTarget);

            var point = Pick();
            if (point != hovered) SetHovered(point);
            if (hovered != null && RepairInput.LeftPressed)
            {
                var next = StateOf(hovered) == InspectionPoint.VisualState.Normal ? InspectionPoint.VisualState.Scanned
                         : StateOf(hovered) == InspectionPoint.VisualState.Scanned ? InspectionPoint.VisualState.Anomaly
                         : InspectionPoint.VisualState.Normal;
                assigned[hovered] = next;
                // 立即显示新状态；鼠标移开再移回时才重新显示悬停
                hovered.SetVisualState(next == InspectionPoint.VisualState.Normal ? InspectionPoint.VisualState.Hover : next);
                UpdateStatus();
            }
        }

        /// <summary>H：切换叠加层 / 旧染色。所有部位按新路径重画；当前悬停的部位保持它正在显示的状态（悬停或刚点出的状态）。</summary>
        public void ToggleHighlightMode()
        {
            if (settings == null) return;
            settings.enabled = !settings.enabled;
            foreach (var p in FindObjectsByType<InspectionPoint>(FindObjectsSortMode.None))
                p.SetVisualState(p == hovered ? p.CurrentState : StateOf(p));
            UpdateStatus();
        }

        /// <summary>鼠标移入 / 移出部位；测试也直接调用它（不经过鼠标射线）。</summary>
        public void SetHovered(InspectionPoint point)
        {
            if (hovered != null) hovered.SetVisualState(StateOf(hovered));
            hovered = point;
            if (hovered != null) hovered.SetVisualState(InspectionPoint.VisualState.Hover);
            UpdateStatus();
        }

        InspectionPoint.VisualState StateOf(InspectionPoint p) =>
            assigned.TryGetValue(p, out var s) ? s : InspectionPoint.VisualState.Normal;

        InspectionPoint Pick()
        {
            if (Mouse.current == null) return null;
            var ray = labCamera.ScreenPointToRay(RepairInput.PointerPosition);
            if (!Physics.Raycast(ray, out var hit, 20f)) return null;
            return hit.collider.GetComponentInParent<InspectionPoint>();
        }

        void UpdateStatus()
        {
            if (statusText == null) return;
            string mode = settings != null && settings.enabled ? "诊断叠加层" : "原底色染色（对比）";
            string target = hovered != null ? $"{hovered.PointId}（单击后：{NextLabel(StateOf(hovered))}）" : "—";
            statusText.text = $"<b>ShaderLab</b>　高亮：{mode}［H 切换］\n悬停：{target}\n左键循环 普通/已扫描/异常 · 右键拖动环绕 · 滚轮推拉";
        }

        static string NextLabel(InspectionPoint.VisualState s) =>
            s == InspectionPoint.VisualState.Normal ? "已扫描" : s == InspectionPoint.VisualState.Scanned ? "异常" : "普通";
    }
}
