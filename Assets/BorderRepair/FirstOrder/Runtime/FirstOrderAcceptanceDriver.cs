using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Dock;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

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
            public bool clickable, accepted, pass, hovered;
            public Dictionary<string, string> extra = new Dictionary<string, string>();   // 布局实测等附加量测
        }

        readonly FirstOrderFlow flow;
        readonly FirstOrderInput input;
        readonly Func<string, IEnumerator> onShot;   // 每步截图（可为空）
        public readonly List<Record> Records = new List<Record>();
        public float Timeout = 20f;
        /// <summary>诊断时间线（可为空）：每次等待的开始、每秒一次的心跳、结束时的实时 / 游戏时间 / 帧数 / 维修座状态 / 转速 / 焦点。</summary>
        public Action<string> Trace;
        string Snap() => $"实时 {Time.realtimeSinceStartup:F2}s，游戏时间 {Time.time:F2}s，帧 {Time.frameCount}，维修座 {flow.Dock.State}，转速 {flow.Dock.Rotors.SpeedDegPerSec:F0}°/s，焦点 {Application.isFocused}";
        /// <summary>不为空时：点击经 Input System 的这个鼠标设备送进游戏（FirstOrderInput.Update 读鼠标），而不是直接调 ClickAt。</summary>
        public Mouse VirtualMouse;
        /// <summary>每次点击前的量测钩子（布局实测用）。</summary>
        public Action<Record, Component> BeforeClick;
        /// <summary>每个“查看”步骤记下之后调用（布局实测用）。</summary>
        public Action<Record> OnView;

        public FirstOrderAcceptanceDriver(FirstOrderFlow f, FirstOrderInput i, Func<string, IEnumerator> shot = null)
        {
            flow = f; input = i; onShot = shot;
        }

        /// <summary>
        /// 建一个虚拟鼠标设备（测试工具）：临时让 Input System 在编辑器失焦时也把输入送进 Game 视图，用完 cleanup 恢复原设置并移除设备。
        /// 这是程序生成的鼠标事件，不是真人操作。
        /// </summary>
        public static Mouse CreateVirtualMouse(out Action cleanup)
        {
            var old = InputSystem.settings;
            var oldBg = old.backgroundBehavior;
#if UNITY_EDITOR
            var oldEd = old.editorInputBehaviorInPlayMode;
#endif
            var s = UnityEngine.Object.Instantiate(old);
            s.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            s.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            InputSystem.settings = s;
            var m = InputSystem.AddDevice<Mouse>("FirstOrder_VirtualMouse");
            m.MakeCurrent();
            cleanup = () =>
            {
                try { if (m.added) InputSystem.RemoveDevice(m); } catch (Exception e) { Debug.LogWarning("[FirstOrder] 移除虚拟鼠标：" + e.Message); }
                try
                {
                    // 换设置时 Input System 会销毁旧的设置对象，所以在一份新副本上把两个值改回去
                    var r = UnityEngine.Object.Instantiate(InputSystem.settings);
                    r.backgroundBehavior = oldBg;
#if UNITY_EDITOR
                    r.editorInputBehaviorInPlayMode = oldEd;
#endif
                    InputSystem.settings = r;
                }
                catch (Exception e) { Debug.LogWarning("[FirstOrder] 恢复输入设置：" + e.Message); }
            };
            return m;
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
                    if (!FirstOrderInput.IsOverUI(sp) && FirstOrderInput.Pick(cam.ScreenPointToRay(sp)) == target)
                    { screen = sp; return true; }
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
            BeforeClick?.Invoke(rec, target);
            rec.clickable = target != null && FindClickPoint(target, out var sp);
            if (!rec.clickable)
            {
                rec.message = "在这个镜头下找不到能选中目标的屏幕点（被挡住或不在画面内）";
                rec.pass = false;
            }
            else if (VirtualMouse != null)
            {
                // 经 Input System 鼠标设备：移到屏幕点 → 等两帧看悬停 → 按下 → 松开；由 FirstOrderInput.Update 自己读鼠标、自己点
                FindClickPoint(target, out sp);
                rec.screenPoint = $"({sp.x:F0}, {sp.y:F0}) / {flow.Rig.Cam.pixelWidth}×{flow.Rig.Cam.pixelHeight}（虚拟鼠标设备）";
                bool? accepted = null;
                void OnActed(string t, bool ok, string msg) { if (accepted == null) accepted = ok; }
                flow.Acted += OnActed;
                VirtualMouse.MakeCurrent();
                InputSystem.QueueStateEvent(VirtualMouse, new MouseState { position = sp });
                yield return null; yield return null;
                rec.hovered = input.Hovered == target;
                InputSystem.QueueStateEvent(VirtualMouse, new MouseState { position = sp }.WithButton(MouseButton.Left, true));
                yield return null;
                InputSystem.QueueStateEvent(VirtualMouse, new MouseState { position = sp }.WithButton(MouseButton.Left, false));
                yield return null;
                flow.Acted -= OnActed;
                rec.accepted = accepted ?? false;
                rec.message = accepted == null ? "鼠标按下后游戏没有收到点击（输入没有送到 Game 视图）" : flow.Message;
                rec.pass = accepted != null && rec.hovered && input.LastClickHit == target && rec.accepted == expectAccept && (expectText == null || flow.Message.Contains(expectText));
                if (accepted == false && PartsSummary() != before) { rec.pass = false; rec.message += "【被拒绝的操作改变了零件状态】"; }
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
            for (float spent = 0f; flow.Busy && spent < Timeout; spent += Mathf.Min(Time.unscaledDeltaTime, 0.25f)) yield return null;   // 同 WaitFor：按运行时间计
            rec.stepAfter = flow.Step.ToString(); rec.dockState = flow.Dock.State.ToString(); rec.parts = PartsSummary();
            Records.Add(rec);
            if (onShot != null) yield return onShot($"A{rec.index:00}_{(rec.pass ? "ok" : "FAIL")}_{label}");
        }

        public IEnumerator WaitFor(string label, Func<bool> cond)
        {
            float r0 = Time.realtimeSinceStartup, t0 = Time.time; int f0 = Time.frameCount;
            Trace?.Invoke($"WAIT-BEGIN {label} | {Snap()}");
            // 等待预算按“Player 实际在跑的时间”计：每帧最多记 0.25 s。窗口失焦被暂停（runInBackground = false）时实时照走但不扣预算，
            // 否则恢复后的第一帧就会因为实时已过 Timeout 而判超时（2026-10-04 首次核心自检失败的样子）。暂停本身仍记在 Trace 里。
            float spent = 0f, beat = r0 + 1f;
            while (!cond() && spent < Timeout)
            {
                if (Trace != null && Time.realtimeSinceStartup >= beat) { Trace($"WAIT … {label} | {Snap()}"); beat = Time.realtimeSinceStartup + 1f; }
                yield return null;
                spent += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            }
            var ok = cond();
            Trace?.Invoke($"WAIT-END {label}：{(ok ? "成立" : "超时（运行 " + Timeout.ToString("F0") + " s）")} | 计入预算 {spent:F2}s，实时 +{Time.realtimeSinceStartup - r0:F2}s，游戏时间 +{Time.time - t0:F2}s，帧 +{Time.frameCount - f0} | {Snap()}");
            Records.Add(new Record { index = Records.Count + 1, label = label, camera = FirstOrderCameraRig.Labels[flow.Rig.Current], target = "（等待）", targetPath = "-",
                                     expect = "条件成立", clickable = true, accepted = ok, pass = ok, message = flow.Message,
                                     stepAfter = flow.Step.ToString(), dockState = flow.Dock.State.ToString(), parts = PartsSummary() });
        }

        /// <summary>
        /// 只看不点的一步（截图用）：切到镜头，核对画面里应该成立的条件（例如保养标记朝上、新旧轴承分开摆着），记为一条记录。
        /// </summary>
        public IEnumerator View(string label, string camera, Func<bool> cond, Func<string> detail = null)
        {
            flow.Rig.Go(camera, true);
            yield return null;
            var ok = cond();
            Records.Add(new Record { index = Records.Count + 1, label = label, camera = FirstOrderCameraRig.Labels[camera], target = "（查看）", targetPath = "-",
                                     expect = "画面条件成立", clickable = true, accepted = ok, pass = ok, message = detail != null ? detail() : flow.Message,
                                     stepAfter = flow.Step.ToString(), dockState = flow.Dock.State.ToString(), parts = PartsSummary() });
            OnView?.Invoke(Records[Records.Count - 1]);
            if (onShot != null) yield return onShot($"A{Records.Count:00}_{(ok ? "ok" : "FAIL")}_{label}");
        }

        // 画面核对用的小工具
        bool OnScreen(Renderer r)
        {
            var cam = flow.Rig.Cam;
            var sp = cam.WorldToViewportPoint(r.bounds.center);
            return r.enabled && r.gameObject.activeInHierarchy && sp.z > 0f && sp.x > 0.02f && sp.x < 0.98f && sp.y > 0.02f && sp.y < 0.98f;
        }

        Renderer LabelRenderer => flow.CoverLabel;
        IEnumerable<Renderer> WornRenderers => flow.Bearing.Renderers().Where(r => r != flow.OriginalBearingRenderer);
        IEnumerable<Renderer> NewRenderers => flow.NewBearing.Renderers();

        /// <summary>完整首单：正确操作 + 穿插的拒绝检查。cleanEarly = 检查左引擎后马上清理进气口（并试一次“只清理就复测”）；false = 装回上盖后、通电前才清理。</summary>
        public IEnumerator RunFullOrder(bool cleanEarly = true, bool resumeAtInspection = false)
        {
            var dock = flow.Dock;
            const string D = FirstOrderCameraRig.Dock, E = FirstOrderCameraRig.EngineL, R = FirstOrderCameraRig.EngineRear,
                         B = FirstOrderCameraRig.Bench, O = FirstOrderCameraRig.Overview, ER = FirstOrderCameraRig.EngineR,
                         REC = FirstOrderCameraRig.Record, CMP = FirstOrderCameraRig.Compare, CL = FirstOrderCameraRig.EngineClose;

            if (!resumeAtInspection)
            {
                yield return Click("拒绝：通电悬停时碰左上盖", E, flow.Cover, false, "供电");
                yield return DockAndPowerOff();
            }
            yield return View("故障原位：进气口堵塞（断电停转）", CL, () => flow.ClogLayers.All(OnScreen),
                              () => $"进气口堵塞 {flow.ClogLayers.Count} 层都在画面里：{string.Join("、", flow.ClogLayers.Select(r => r.GetComponent<MeshFilter>().sharedMesh.name))}");
            yield return Click("4 检查左引擎：点左上盖", E, flow.Cover, true);
            yield return Click("拒绝：要拆右引擎（右引擎对照镜头）", ER, flow.RightEngine, false, "右引擎");
            if (cleanEarly)
            {
                yield return CleanClog("4b 清理：点进气口堵塞（断电停转后允许）");
                yield return Click("拒绝：只清理就尝试复测（点夹具握把松开）", D, Grip(), false, "只清理");
                yield return Click("拒绝：只清理就尝试复测（点断电开关通电）", D, Lever(), false, "只清理");
            }
            yield return Click("拒绝：锁扣没扳开就取上盖", E, flow.Cover, false, "锁扣");
            yield return Click("5a 拆：扳开外侧锁扣", E, flow.LatchOuter, true);
            yield return Click("5b 拆：扳开后侧锁扣（背面镜头）", R, flow.LatchRear, true);
            yield return Click("拒绝：维修中途通电", D, Lever(), false);
            yield return Click("5c 拆：取下左上盖总成", E, flow.Cover, true);
            yield return Click("5d 去向：上盖总成翻面放到工作台操作垫", O, flow.MatZone, true);
            yield return View("翻盖读保养记录：上盖内侧朝上", REC, () => LabelRenderer != null && OnScreen(LabelRenderer) && Vector3.Dot(LabelNormal(), Vector3.up) > 0.9f,
                              () => $"保养标记朝向与竖直向上夹角 {Vector3.Angle(LabelNormal(), Vector3.up):F1}°");
            yield return View("故障原位：上盖拆下后的磨损轴承", CL, () => WornRenderers.Any(OnScreen) && !flow.OriginalBearingRenderer.enabled,
                              () => "磨损轴承在原位，原轴承渲染器关闭");
            yield return Click("6 定位故障轴承：点左上轴承", E, flow.Bearing, true);
            yield return Click("7a 更换：取下旧轴承", E, flow.Bearing, true);
            yield return Click("拒绝：把旧轴承放到上盖的落点", O, flow.MatZone, false);
            yield return Click("7b 去向：旧轴承平放进工作台托盘", O, flow.OldTrayZone, true);
            yield return View("新旧轴承对比：托盘里的旧件 / 轴承盒上的新件", CMP, () => WornRenderers.Any(OnScreen) && NewRenderers.Any(OnScreen),
                              () => $"旧轴承 {flow.Bearing.WorldBounds().center:F3}，新轴承 {flow.NewBearing.WorldBounds().center:F3}，相距 {Vector3.Distance(flow.Bearing.WorldBounds().center, flow.NewBearing.WorldBounds().center) * 1000:F0} mm");
            yield return Click("拒绝：新轴承还没装就通电", D, Lever(), false, "新轴承");
            yield return Click("7c 更换：从工作台轴承盒取新轴承装上", B, flow.NewBearing, true);
            yield return View("装回：新轴承装在原位（上盖装回前）", CL, () => flow.NewBearing.Location == PartLocation.Installed && NewRenderers.Any(OnScreen) && !WornRenderers.Any(OnScreen),
                              () => $"新轴承与原轴承原位距离 {Vector3.Distance(flow.NewBearing.transform.position, flow.Bearing.HomeWorldPose().position) * 1000:F2} mm；旧轴承在{flow.OldTrayZone.displayName}");
            yield return Click("拒绝：新轴承装上了但上盖没装回就通电", D, Lever(), false, "上盖");
            yield return Click("8a 装回：上盖总成翻回来装回左引擎", B, flow.Cover, true);
            yield return Click("拒绝：锁扣没扣回就通电", D, Lever(), false, "锁扣");
            yield return Click("8b 装回：扣回外侧锁扣", E, flow.LatchOuter, true);
            yield return Click("拒绝：只扣回一个锁扣就通电", D, Lever(), false, "锁扣");
            yield return Click("8c 装回：扣回后侧锁扣（背面镜头）", R, flow.LatchRear, true);
            if (!cleanEarly)
            {
                yield return Click("拒绝：进气口没清理就通电", D, Lever(), false, "进气口");
                yield return CleanClog("8d 清理：点进气口堵塞（装回后、通电前）");
            }
            yield return View("装回：新轴承、上盖总成、锁扣都在原位", CL, () => flow.NewBearing.Location == PartLocation.Installed && flow.Cover.Location == PartLocation.Installed &&
                                                                               flow.LatchOuter.Location == PartLocation.Installed && flow.LatchRear.Location == PartLocation.Installed);
            yield return Click("9 通电：点断电开关手柄", D, Lever(), true);
            yield return WaitFor("等待：涡轮恢复转动", () => dock.Rotors.SpeedDegPerSec > 1f);
            yield return Click("10 离座：点夹具握把（松开 → 上浮）", D, Grip(), true);
            yield return WaitFor("等待：离座复测结束（Done）", () => flow.Step == FoStep.Done);
            yield return WaitFor("复测结果（占位判定）：" + flow.RetestDetail, () => flow.RetestPassed);
        }

        /// <summary>只清理进气口、不处理轴承，就尝试复测（松开夹具 / 通电）：都必须被拒绝，七号留在维修座上、不通电。</summary>
        public IEnumerator RunCleanOnlyAttempt()
        {
            const string D = FirstOrderCameraRig.Dock, E = FirstOrderCameraRig.EngineL;
            yield return DockAndPowerOff();
            yield return Click("4 检查左引擎：点左上盖", E, flow.Cover, true);
            yield return CleanClog("4b 只清理进气口");
            yield return Click("拒绝：只清理就尝试复测（点夹具握把松开）", D, Grip(), false, "只清理");
            yield return Click("拒绝：只清理就尝试复测（点断电开关通电）", D, Lever(), false, "只清理");
            yield return WaitFor("复测没有开始：七号仍夹在维修座上、断电、磨损轴承仍在原位",
                                 () => flow.Dock.State == DockState.RotorsStopped && !flow.Dock.PowerOn && flow.Step < FoStep.PowerOn && !flow.RetestPassed &&
                                       flow.Bearing.Location == PartLocation.Installed);
        }

        IEnumerator DockAndPowerOff()
        {
            var dock = flow.Dock;
            const string D = FirstOrderCameraRig.Dock, E = FirstOrderCameraRig.EngineL;
            yield return Click("1 七号入座：点夹具握把（张开 → 落座）", D, Grip(), true);
            yield return WaitFor("等待：七号落座（SeatedOpen）", () => dock.State == DockState.SeatedOpen);
            yield return Click("拒绝：没夹紧就断电", D, Lever(), false, "夹紧");
            yield return Click("2 夹具固定：点夹具握把（夹紧）", D, Grip(), true);
            yield return WaitFor("等待：已夹紧（Clamped）", () => dock.State == DockState.Clamped);
            yield return Click("3 断电：点断电开关手柄", D, Lever(), true);
            yield return Click("拒绝：涡轮减速中碰外侧锁扣", E, flow.LatchOuter, false, "叶轮");
            yield return WaitFor("等待：涡轮停转（RotorsStopped，转速 0）", () => dock.State == DockState.RotorsStopped && dock.Rotors.SpeedDegPerSec <= 0f);
        }

        IEnumerator CleanClog(string label)
        {
            yield return Click(label, FirstOrderCameraRig.EngineL, flow.Clog, true);
            yield return WaitFor("等待：清理完成（积尘、纤维逐层清掉）", () => flow.Clog.Location == PartLocation.Cleared && !flow.Busy && flow.ClogLayers.All(r => !r.enabled));
            yield return View("清理后的进气口", FirstOrderCameraRig.EngineClose, () => flow.ClogLayers.All(r => !r.enabled), () => flow.Message);
        }

        // 左右夹具握把是同一个动作：取维修座镜头下能点到的那一个
        Component Grip()
        {
            flow.Rig.Go(FirstOrderCameraRig.Dock, true);
            var grips = new[] { "Dock_Clamp_L_Grip", "Dock_Clamp_R_Grip" }.Select(n => (Component)GameObject.Find(n).GetComponent<DockInteractable>()).ToList();
            return grips.FirstOrDefault(g => FindClickPoint(g, out _)) ?? grips[0];
        }

        static Component Lever() => GameObject.Find("Dock_PowerSwitch_LeverGrip").GetComponent<DockInteractable>();

        /// <summary>保养标记的朝向（世界）：构建时从网格法线算好，运行时网格不可读。</summary>
        Vector3 LabelNormal() => flow.CoverLabelNormal;
    }
}
