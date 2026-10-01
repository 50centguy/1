using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Dock;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BorderRepair.FirstOrder
{
    /// <summary>
    /// 首单原型的鼠标点选 + HUD。
    /// 点选规则：沿射线按距离看命中；可操作的对象（维修座代理、首单部件、工作台落点）被选中；
    /// 碰到不属于目标的实体表面（七号机身、维修座、工作台的遮挡碰撞）就停下——被挡住的部件点不到，必须换镜头。
    /// 同一位置 1 cm 内有多个可操作对象时取体积最小的（小锁扣不会被大上盖吞掉）。
    /// </summary>
    public class FirstOrderInput : MonoBehaviour
    {
        [SerializeField] FirstOrderFlow flow;
        [SerializeField] float maxDistance = 8f;
        [SerializeField] bool showHud = true;

        public Component Hovered { get; private set; }
        public Component LastClickHit { get; private set; }
        public FirstOrderFlow Flow => flow;

        public void Configure(FirstOrderFlow f) => flow = f;

        static bool IsActionable(Component c) => c is DockInteractable d ? d.action != DockAction.ContactPad : c is FirstOrderPart || c is FirstOrderDropZone;

        static Component ActionableOn(Collider col)
        {
            foreach (var c in col.GetComponents<Component>())
            {
                if (c is FirstOrderMember m && m.owner != null) return m.owner;
                if (IsActionable(c)) return c;
            }
            return null;
        }

        public static Component Pick(Ray ray, float maxDistance = 8f)
        {
            var hits = Physics.RaycastAll(ray, maxDistance, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Component best = null;
            float first = -1f, solid = float.MaxValue, bestVol = float.MaxValue;
            var enclosing = new List<Collider>();
            foreach (var h in hits)
            {
                var a = ActionableOn(h.collider);
                if (a == null)
                {
                    if (h.collider.isTrigger || !h.collider.enabled) continue;
                    // 不属于任何可操作对象的实体表面：挡住了
                    if (best == null) return null;
                    break;
                }
                if (first < 0f) first = h.distance;
                if (h.distance > solid + 0.01f) break;                       // 已经碰到可操作对象的实体表面：再往后的看不见
                // 套在前面某个点选盒里面的小点选盒（例如上盖总成点选盒里的进气口堵塞）：射线还在大盒子里、没碰到实体表面，也算候选
                bool nested = h.collider.isTrigger && enclosing.Any(c => (c.ClosestPoint(h.point) - h.point).sqrMagnitude < 1e-8f);
                if (h.distance > first + 0.01f && !nested) break;
                if (h.collider.isTrigger && (h.collider is BoxCollider || h.collider is SphereCollider || h.collider is CapsuleCollider)) enclosing.Add(h.collider);
                else if (!h.collider.isTrigger) solid = Mathf.Min(solid, h.distance);
                var s = h.collider.bounds.size;
                float vol = s.x * s.y * s.z;
                if (vol < bestVol) { bestVol = vol; best = a; }
            }
            return best;
        }

        public Component PickScreen(Vector2 screen) => Pick(flow.Rig.Cam.ScreenPointToRay(screen), maxDistance);

        /// <summary>按屏幕坐标点击（真实鼠标与程序验收共用）。返回命中的对象和是否被接受。</summary>
        public (Component hit, bool accepted) ClickAt(Vector2 screen)
        {
            var hit = PickScreen(screen);
            LastClickHit = hit;
            return (hit, flow.Click(hit));
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                for (int i = 0; i < FirstOrderCameraRig.Order.Length; i++)
                    if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) flow.Rig.Go(FirstOrderCameraRig.Order[i]);
            }
            var mouse = Mouse.current;
            if (mouse == null) return;
            var pos = mouse.position.ReadValue();
            Hovered = PickScreen(pos);
            if (mouse.leftButton.wasPressedThisFrame) ClickAt(pos);
        }

        public static string NameOf(Component c) => c switch
        {
            FirstOrderPart p => (p.isPlaceholder ? "【占位】" : "") + p.displayName,
            FirstOrderDropZone z => "落点：" + z.displayName,
            DockInteractable d => "维修座：" + d.name,
            null => "—",
            _ => c.name,
        };

        void OnGUI()
        {
            if (!showHud || flow == null) return;
            GUI.Box(new Rect(12, 12, 900, 252), GUIContent.none);
            GUI.Label(new Rect(22, 16, 800, 22), $"七号首单 · 可玩原型（占位交互，非正式维修流程） · 步骤 {(int)flow.Step + 1}/17：{flow.Step}");
            GUI.Label(new Rect(22, 36, 800, 22), "下一步：" + flow.NextHint());
            GUI.Label(new Rect(22, 56, 800, 40), flow.Message);
            GUI.Label(new Rect(22, 92, 880, 22), $"指向：{NameOf(Hovered)}    镜头：{FirstOrderCameraRig.Labels[flow.Rig.Current]}（1 维修座 / 2 左引擎 / 3 背面 / 4 工作台 / 5 总览 / 6 右引擎 / 7 保养记录 / 8 新旧轴承 / 9 近看）    " +
                                                 $"维修座：{flow.Dock.State}，供电 {(flow.Dock.PowerOn ? "ON" : "OFF")}，转速 {flow.Dock.Rotors.SpeedDegPerSec:F0}°/s");
            int y = 114;
            GUI.Label(new Rect(22, y, 800, 22), "零件去向：");
            foreach (var p in flow.TrackedParts)
            {
                y += 18;
                GUI.Label(new Rect(36, y, 790, 22), $"{(p.isPlaceholder ? "【占位】" : "")}{p.displayName}：{p.LocationDetail}");
            }
        }
    }
}
