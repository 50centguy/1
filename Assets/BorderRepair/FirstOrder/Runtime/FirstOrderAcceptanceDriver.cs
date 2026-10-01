using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Dock;
using UnityEngine;

namespace BorderRepair.FirstOrder
{
    /// <summary>
    /// 程序验收驱动（测试工具，不参与玩法）：按“玩家会怎么点”走完七号首单。
    /// 每一步先切到该步的镜头，在目标的点选范围里找一个屏幕点，用真实的点选规则（含遮挡）确认这个点确实选中目标，再从这个屏幕点点击。
    /// 找不到这样的点 = 目标被挡住或不在画面里，记为失败，不会绕过镜头直接调用。
    /// 同时穿插“应该被拒绝”的操作，核对拒绝原因、并确认被拒绝的操作不改变任何零件状态。
    /// </summary>
    public class FirstOrderAcceptanceDriver
    {
        public class Record
        {
            public int index;
            public string label, camera, target, targetPath, expect, message, stepAfter, dockState, parts, screenPoint;
            public bool clickable, accepted, pass;
        }

        readonly FirstOrderFlow flow;
        readonly FirstOrderInput input;
        readonly Func<string, IEnumerator> onShot;   // 每步截图（可为空）
        public readonly List<Record> Records = new List<Record>();
        public float Timeout = 20f;

        public FirstOrderAcceptanceDriver(FirstOrderFlow f, FirstOrderInput i, Func<string, IEnumerator> shot = null)
        {
            flow = f; input = i; onShot = shot;
        }

        public bool AllPassed => Records.Count > 0 && Records.All(r => r.pass);

        string PartsSummary() => string.Join("；", flow.TrackedParts.Select(p => $"{p.partId}={p.Location}"));

        static string PathOf(Component c)
        {
            if (c is FirstOrderPart p) return p.realPath;
            if (c is FirstOrderDropZone z) return z.benchObjectPath + "（落点 " + z.name + "）";
            var parts = new List<string>();
            for (var t = c.transform; t != null; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        /// <summary>在目标碰撞范围内找一个“真实点选会选中它”的屏幕点。</summary>
        public bool FindClickPoint(Component target, out Vector2 screen)
        {
            screen = default;
            var cam = flow.Rig.Cam;
            var cols = target.GetComponents<Collider>().Where(c => c.enabled).ToList();
            if (target is FirstOrderPart fp) cols.AddRange(fp.members.SelectMany(m => m.GetComponents<Collider>()));
            Physics.SyncTransforms();
            foreach (var col in cols)
            {
                var b = col.bounds;
                for (int i = 0; i < 125; i++)
                {
                    var f = new Vector3(i % 5, (i / 5) % 5, i / 25) / 4f;          // 5×5×5 网格，从中心往外更容易命中可见面
                    var w = b.min + Vector3.Scale(b.size, Vector3.one * 0.5f + (f - Vector3.one * 0.5f) * 0.9f);
                    var sp = cam.WorldToScreenPoint(w);
                    if (sp.z <= 0 || sp.x < 1 || sp.y < 1 || sp.x > cam.pixelWidth - 1 || sp.y > cam.pixelHeight - 1) continue;
                    if (FirstOrderInput.Pick(cam.ScreenPointToRay(sp)) == target) { screen = sp; return true; }
                }
            }
            return false;
        }

        public IEnumerator Click(string label, string camera, Component target, bool expectAccept, string expectText = null)
        {
            flow.Rig.Go(camera, true);
            yield return null;
            var rec = new Record { index = Records.Count + 1, label = label, camera = FirstOrderCameraRig.Labels[camera], target = FirstOrderInput.NameOf(target),
                                   targetPath = target != null ? PathOf(target) : "-", expect = expectAccept ? "接受" : "拒绝" };
            var before = PartsSummary();
            rec.clickable = target != null && FindClickPoint(target, out var sp);
            if (!rec.clickable)
            {
                rec.message = "在这个镜头下找不到能选中目标的屏幕点（被挡住或不在画面内）";
                rec.pass = false;
            }
            else
            {
                FindClickPoint(target, out sp);
                rec.screenPoint = $"({sp.x:F0}, {sp.y:F0}) / {flow.Rig.Cam.pixelWidth}×{flow.Rig.Cam.pixelHeight}";
                var (hit, accepted) = input.ClickAt(sp);
                rec.accepted = accepted;
                rec.message = flow.Message;
                rec.pass = hit == target && accepted == expectAccept && (expectText == null || flow.Message.Contains(expectText));
                if (!accepted && PartsSummary() != before) { rec.pass = false; rec.message += "【被拒绝的操作改变了零件状态】"; }
            }
            float until = Time.realtimeSinceStartup + Timeout;
            while (flow.Busy && Time.realtimeSinceStartup < until) yield return null;
            rec.stepAfter = flow.Step.ToString(); rec.dockState = flow.Dock.State.ToString(); rec.parts = PartsSummary();
            Records.Add(rec);
            if (onShot != null) yield return onShot($"A{rec.index:00}_{(rec.pass ? "ok" : "FAIL")}_{label}");
        }

        public IEnumerator WaitFor(string label, Func<bool> cond)
        {
            float until = Time.realtimeSinceStartup + Timeout;
            while (!cond() && Time.realtimeSinceStartup < until) yield return null;
            var ok = cond();
            Records.Add(new Record { index = Records.Count + 1, label = label, camera = FirstOrderCameraRig.Labels[flow.Rig.Current], target = "（等待）", targetPath = "-",
                                     expect = "条件成立", clickable = true, accepted = ok, pass = ok, message = flow.Message,
                                     stepAfter = flow.Step.ToString(), dockState = flow.Dock.State.ToString(), parts = PartsSummary() });
        }

        /// <summary>完整首单：正确操作 + 穿插的拒绝检查。</summary>
        public IEnumerator RunFullOrder()
        {
            var dock = flow.Dock;
            // 左右夹具握把是同一个动作：取维修座镜头下能点到的那一个
            Component Grip()
            {
                flow.Rig.Go(FirstOrderCameraRig.Dock, true);
                var grips = new[] { "Dock_Clamp_L_Grip", "Dock_Clamp_R_Grip" }.Select(n => (Component)GameObject.Find(n).GetComponent<DockInteractable>()).ToList();
                return grips.FirstOrDefault(g => FindClickPoint(g, out _)) ?? grips[0];
            }
            Component Lever() => GameObject.Find("Dock_PowerSwitch_LeverGrip").GetComponent<DockInteractable>();
            const string D = FirstOrderCameraRig.Dock, E = FirstOrderCameraRig.EngineL, R = FirstOrderCameraRig.EngineRear,
                         B = FirstOrderCameraRig.Bench, O = FirstOrderCameraRig.Overview, ER = FirstOrderCameraRig.EngineR;

            yield return Click("拒绝：通电悬停时碰左上盖", E, flow.Cover, false, "供电");
            yield return Click("1 七号入座：点夹具握把（张开 → 落座）", D, Grip(), true);
            yield return WaitFor("等待：七号落座（SeatedOpen）", () => dock.State == DockState.SeatedOpen);
            yield return Click("拒绝：没夹紧就断电", D, Lever(), false, "夹紧");
            yield return Click("2 夹具固定：点夹具握把（夹紧）", D, Grip(), true);
            yield return WaitFor("等待：已夹紧（Clamped）", () => dock.State == DockState.Clamped);
            yield return Click("3 断电：点断电开关手柄", D, Lever(), true);
            yield return Click("拒绝：涡轮减速中碰外侧锁扣", E, flow.LatchOuter, false, "叶轮");
            yield return WaitFor("等待：涡轮停转（RotorsStopped，转速 0）", () => dock.State == DockState.RotorsStopped && dock.Rotors.SpeedDegPerSec <= 0f);
            yield return Click("4 检查左引擎：点左上盖", E, flow.Cover, true);
            yield return Click("拒绝：要拆右引擎（右引擎对照镜头）", ER, flow.RightEngine, false, "右引擎");
            yield return Click("拒绝：锁扣没扳开就取上盖", E, flow.Cover, false, "锁扣");
            yield return Click("5a 拆：扳开外侧锁扣", E, flow.LatchOuter, true);
            yield return Click("5b 拆：扳开后侧锁扣（背面镜头）", R, flow.LatchRear, true);
            yield return Click("拒绝：维修中途通电", D, Lever(), false);
            yield return Click("5c 拆：取下左上盖总成", E, flow.Cover, true);
            yield return Click("5d 去向：上盖总成放到工作台操作垫", O, flow.MatZone, true);
            yield return Click("6 定位故障轴承：点左上轴承", E, flow.Bearing, true);
            yield return Click("7a 更换：取下旧轴承", E, flow.Bearing, true);
            yield return Click("拒绝：把旧轴承放到上盖的落点", O, flow.MatZone, false);
            yield return Click("7b 去向：旧轴承放进工作台托盘", O, flow.OldTrayZone, true);
            yield return Click("拒绝：上盖没装回就通电", D, Lever(), false, "上盖");
            yield return Click("7c 更换：从工作台轴承盒取新轴承装上（占位）", B, flow.NewBearing, true);
            yield return Click("8a 装回：上盖总成装回左引擎", B, flow.Cover, true);
            yield return Click("拒绝：锁扣没扣回就通电", D, Lever(), false, "锁扣");
            yield return Click("8b 装回：扣回外侧锁扣", E, flow.LatchOuter, true);
            yield return Click("8c 装回：扣回后侧锁扣（背面镜头）", R, flow.LatchRear, true);
            yield return Click("9 通电：点断电开关手柄", D, Lever(), true);
            yield return WaitFor("等待：涡轮恢复转动", () => dock.Rotors.SpeedDegPerSec > 1f);
            yield return Click("10 离座：点夹具握把（松开 → 上浮）", D, Grip(), true);
            yield return WaitFor("等待：离座复测结束（Done）", () => flow.Step == FoStep.Done);
            yield return WaitFor("复测结果（占位判定）：" + flow.RetestDetail, () => flow.RetestPassed);
        }
    }
}
