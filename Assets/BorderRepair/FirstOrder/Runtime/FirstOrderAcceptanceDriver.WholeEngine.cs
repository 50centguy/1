using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace BorderRepair.FirstOrder
{
    /// <summary>
    /// 整引擎维修的程序验收（统一诊所第二晚）：七号这边的点击沿用固定机位；拔线、抱引擎、放维修台、台上拆修、抱回、装回、接线
    /// 用第一人称——站到位置、转头让准星对准目标（用真实点选规则确认准星选中的就是它）、再左键。程序输入，不是真人试玩。
    /// </summary>
    public partial class FirstOrderAcceptanceDriver
    {
        /// <summary>站到 feet（脚底），准星对准 target 后左键。经虚拟鼠标设备时由 FirstOrderInput 按准星自己点（含伸手距离限制）。</summary>
        public IEnumerator ClickWalking(string label, Vector3 feet, Component target, bool expectAccept, string expectText = null)
        {
            var rig = flow.Rig; var walker = rig.Walker;
            rig.Walk(true);
            // 站位：先原地；对不准时等几帧（上一步的零件可能还在落定），再往前凑 0.3 m（从上往下看）、左右各挪 0.15 m 试
            var fwd = Vector3.ProjectOnPlane(TargetCenter(target) - feet, Vector3.up).normalized;
            var side = Vector3.Cross(Vector3.up, fwd);
            Vector3? aimPoint = null;
            foreach (var f in new[] { feet, feet, feet + fwd * .3f, feet + side * .15f, feet - side * .15f, feet + fwd * .3f + side * .15f, feet + fwd * .3f - side * .15f })
            {
                walker.PlaceAt(f, 0f);
                yield return Settle();
                walker.LookAt(TargetCenter(target));
                yield return null; yield return null;
                aimPoint = target != null ? VisiblePoint(target, rig.Cam.transform.position) : null;
                if (aimPoint != null) break;
            }
            var rec = new Record { index = Records.Count + 1, label = label, camera = "行走（第一人称）", target = FirstOrderInput.NameOf(target),
                                   targetPath = target != null ? PathOf(target) : "-", expect = expectAccept ? "接受" : "拒绝" };
            var before = PartsSummary();
            BeforeClick?.Invoke(rec, target);
            // 先在当前视野里找到能选中目标的点，再转头让准星（画面中央）落在那一点上
            rec.clickable = aimPoint != null;
            if (rec.clickable)
            {
                walker.LookAt(aimPoint.Value);
                yield return null; yield return null;
                var center = new Vector2(rig.Cam.pixelWidth * .5f, rig.Cam.pixelHeight * .5f);
                rec.clickable = FirstOrderInput.Pick(rig.Cam.ScreenPointToRay(center), 8f, out float dist) == target;
                rec.screenPoint = $"准星 {dist:F2} m / {rig.Cam.pixelWidth}×{rig.Cam.pixelHeight}";
                rec.extra["distance"] = dist.ToString("F2");
            }
            if (!rec.clickable)
            {
                var toTarget = new Ray(rig.Cam.transform.position, TargetCenter(target) - rig.Cam.transform.position);
                var hits = Physics.RaycastAll(toTarget, 3f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance).Take(4).Select(h => h.collider.name + (h.collider.isTrigger ? "(T)" : "") + "@" + h.distance.ToString("F2"));
                rec.message = "站在这里准星对不准目标（被挡住或不在视野内）；朝目标中心的射线依次碰到：" + string.Join(" / ", hits) + "；点选结果：" + FirstOrderInput.NameOf(FirstOrderInput.Pick(toTarget, 3f));
                rec.pass = false;
            }
            else if (VirtualMouse != null)
            {
                var center = new Vector2(Screen.width * .5f, Screen.height * .5f);
                bool? accepted = null;
                void OnActed(string t, bool ok, string msg) { if (accepted == null) accepted = ok; }
                flow.Acted += OnActed;
                VirtualMouse.MakeCurrent();
                InputSystem.QueueStateEvent(VirtualMouse, new MouseState { position = center });
                // 按下前逐帧重新对准，直到游戏里的准星悬停在目标上（Player 帧率高，站定后视角还可能有细小变化）
                for (int k = 0; k < 30 && input.Hovered != target; k++) { walker.LookAt(aimPoint.Value); yield return null; }
                yield return null;
                rec.hovered = input.Hovered == target;
                string hoveredName = FirstOrderInput.NameOf(input.Hovered);
                InputSystem.QueueStateEvent(VirtualMouse, new MouseState { position = center }.WithButton(MouseButton.Left, true));
                yield return null;
                InputSystem.QueueStateEvent(VirtualMouse, new MouseState { position = center }.WithButton(MouseButton.Left, false));
                yield return null;
                flow.Acted -= OnActed;
                rec.accepted = accepted ?? false;
                rec.message = accepted == null ? "准星左键后游戏没有收到点击（够不着或输入没送到）：" + flow.Message : flow.Message;
                rec.pass = accepted != null && rec.hovered && rec.accepted == expectAccept && (expectText == null || flow.Message.Contains(expectText));
                if (accepted == false && PartsSummary() != before) { rec.pass = false; rec.message += "【被拒绝的操作改变了零件状态】"; }
                if (!rec.hovered) rec.message += $"【按下时准星悬停的是：{hoveredName}】";
            }
            else
            {
                var (hit, accepted) = input.ClickAt(new Vector2(rig.Cam.pixelWidth * .5f, rig.Cam.pixelHeight * .5f));
                rec.accepted = accepted;
                rec.message = flow.Message;
                rec.pass = hit == target && accepted == expectAccept && (expectText == null || flow.Message.Contains(expectText));
                if (!accepted && PartsSummary() != before) { rec.pass = false; rec.message += "【被拒绝的操作改变了零件状态】"; }
            }
            for (float spent = 0f; flow.Busy && spent < Timeout; spent += Mathf.Min(Time.unscaledDeltaTime, 0.25f)) yield return null;
            rec.stepAfter = flow.Step + " / 引擎 " + flow.EngineState; rec.dockState = flow.Dock.State.ToString(); rec.parts = PartsSummary();
            Records.Add(rec);
            if (onShot != null) yield return onShot($"A{rec.index:00}_{(rec.pass ? "ok" : "FAIL")}_{label}");
        }

        /// <summary>等相机停稳（连续两帧移动不到 0.5 mm，最多 90 帧）：刚放到新站位时角色还在落地。</summary>
        IEnumerator Settle()
        {
            var cam = flow.Rig.Cam.transform;
            var last = cam.position; int still = 0;
            for (int k = 0; k < 90 && still < 2; k++)
            {
                yield return null;
                still = (cam.position - last).sqrMagnitude < .0005f * .0005f ? still + 1 : 0;
                last = cam.position;
            }
        }

        /// <summary>目标上一个从 eye 看得见、按真实点选规则选中的就是它的点（不看屏幕上有没有界面：第一人称用画面中央的准星点）。</summary>
        Vector3? VisiblePoint(Component target, Vector3 eye)
        {
            var cols = target.GetComponents<Collider>().Where(c => c.enabled).ToList();
            if (target is FirstOrderPart fp) cols.AddRange(fp.members.SelectMany(m => m.GetComponents<Collider>()).Where(c => c.enabled));
            Physics.SyncTransforms();
            // 候选方向：目标中心 + 每个碰撞体包围盒内 5×5×5 个点。优先选“四周偏 0.4° 也都选中目标”的方向：
            // 准星落在目标边缘时，换个分辨率（Player 是 4K）就可能点到旁边的零件
            var dirs = new List<Vector3> { TargetCenter(target) - eye };
            foreach (var col in cols)
            {
                var b = col.bounds;
                for (int i = 0; i < 125; i++)
                {
                    var f = new Vector3(i % 5, (i / 5) % 5, i / 25) / 4f;
                    dirs.Add(b.min + Vector3.Scale(b.size, Vector3.one * 0.5f + (f - Vector3.one * 0.5f) * 0.9f) - eye);
                }
            }
            Vector3? fallback = null;
            foreach (var dir in dirs)
            {
                var ray = new Ray(eye, dir);
                if (FirstOrderInput.Pick(ray, 8f, out float d) != target) continue;
                var hit = ray.GetPoint(d);
                fallback ??= hit;
                var rot = Quaternion.LookRotation(dir);
                bool margin = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right }
                    .All(a => FirstOrderInput.Pick(new Ray(eye, rot * Quaternion.AngleAxis(.4f, a) * Vector3.forward), 8f) == target);
                if (margin) return hit;
            }
            return fallback;
        }

        static Vector3 TargetCenter(Component c) => c switch
        {
            FirstOrderPart p => p.WorldBounds().center,
            _ => c.GetComponent<Collider>() != null ? c.GetComponent<Collider>().bounds.center : c.transform.position,
        };

        /// <summary>七号左侧站位：从机身中心经左引擎往外 0.55 m（脚底）。</summary>
        Vector3 RobotSideStand()
        {
            var body = flow.Dock.RobotRoot.position;
            var c = flow.EngineHomeCenter;
            var outward = Vector3.ProjectOnPlane(c - body, Vector3.up).normalized;
            var p = c + outward * .55f;
            return new Vector3(p.x, .05f, p.z);
        }

        /// <summary>第一人称站到 feet、看向 look，截一张图（只拍图，不点击；看抱在手上的引擎）。</summary>
        IEnumerator ShowWalking(string key, Vector3 feet, Vector3 look)
        {
            var walker = flow.Rig.Walker;
            flow.Rig.Walk(true);
            walker.PlaceAt(feet, 0f);
            for (int k = 0; k < 4; k++) yield return null;
            walker.LookAt(look);
            yield return null; yield return null;
            if (onShot != null) yield return onShot($"V_{key} ");
        }

        Vector3 BenchStand() => new Vector3(flow.EngineFacePoint.x, .05f, flow.EngineFacePoint.z);

        /// <summary>完整的整引擎维修（第二晚，从“可以开始检查”开始），穿插应该被拒绝的操作。</summary>
        public IEnumerator RunWholeEngineOrder()
        {
            var dock = flow.Dock;
            const string D = FirstOrderCameraRig.Dock, E = FirstOrderCameraRig.EngineL, ER = FirstOrderCameraRig.EngineR;
            var bench = BenchStand();
            yield return Click("4 检查左引擎：点左上盖", E, flow.Cover, true);
            yield return Click("拒绝：要拆右引擎（右引擎对照镜头）", ER, flow.RightEngine, false, "右引擎");
            yield return Click("5a 点左引擎：拔下辅助线缆、松开连接键", E, flow.Cover, true, "辅助线缆");
            yield return Click("5b 再点左引擎：整个卸下来抱在手上", E, flow.Cover, true, "抱在手上");
            yield return WaitFor("引擎抱在手上", () => flow.EngineState == EngineMountState.InHands);
            yield return ShowWalking("hold", bench, flow.EngineBenchZone.transform.position);
            yield return Click("拒绝：抱着引擎去点断电开关", D, Lever(), false, "手上");
            yield return ClickWalking("5c 抱到维修台放下", bench, flow.EngineBenchZone, true);
            yield return WaitFor("引擎在维修台上", () => flow.EngineState == EngineMountState.OnBench);
            yield return ClickWalking("6a 清理进气口（在维修台上）", bench, flow.Clog, true);
            yield return WaitFor("进气口清理完", () => flow.ClogCleared && !flow.Busy);
            yield return ClickWalking("6b 扳开外侧锁扣", bench, flow.LatchOuter, true);
            yield return ClickWalking("6c 扳开后侧锁扣", bench, flow.LatchRear, true);
            yield return ClickWalking("6d 取下上盖总成", bench, flow.Cover, true);
            yield return ClickWalking("6e 上盖翻面放到操作垫", bench, flow.MatZone, true);
            yield return ClickWalking("7a 定位故障轴承：点左上轴承", bench, flow.Bearing, true);
            yield return ClickWalking("7b 取下旧轴承", bench, flow.Bearing, true);
            yield return ClickWalking("7c 旧轴承放进托盘", bench, flow.OldTrayZone, true);
            yield return ClickWalking("7d 装上新轴承", bench, flow.NewBearing, true);
            yield return ClickWalking("8a 装回上盖总成", bench, flow.Cover, true);
            yield return ClickWalking("8b 扣回外侧锁扣", bench, flow.LatchOuter, true);
            yield return ClickWalking("8c 扣回后侧锁扣", bench, flow.LatchRear, true);
            yield return Click("拒绝：引擎还在维修台上就通电", D, Lever(), false, "维修台");
            yield return ClickWalking("9a 抱起修好的左引擎", bench, flow.Cover, true);
            yield return WaitFor("修好的引擎抱在手上", () => flow.EngineState == EngineMountState.InHandsBack);
            yield return Click("9b 装回七号左侧安装位", E, flow.EngineMountZone, true);
            yield return WaitFor("引擎装回七号", () => flow.EngineState == EngineMountState.Mounted);
            yield return Click("拒绝：线缆没接就通电", D, Lever(), false, "连接键");
            yield return Click("9c 点左引擎：插回辅助线缆、拧紧连接键", E, flow.Cover, true, "插回");
            var home = flow.EngineHomeWorldPose;
            yield return WaitFor($"引擎回到原位（偏差 {Vector3.Distance(flow.EngineLHinge.position, home.position) * 1000:F2} mm）",
                                 () => flow.EngineState == EngineMountState.Reconnected && Vector3.Distance(flow.EngineLHinge.position, home.position) < .0005f);
            yield return Click("10 通电：点断电开关手柄", D, Lever(), true);
            yield return WaitFor("等待：涡轮恢复转动", () => dock.Rotors.SpeedDegPerSec > 1f);
            yield return Click("11 离座：点夹具握把（松开 → 上浮）", D, Grip(), true);
            yield return WaitFor("等待：离座复测结束（Done）", () => flow.Step == FoStep.Done);
            yield return WaitFor("复测结果（占位判定）：" + flow.RetestDetail, () => flow.RetestPassed);
        }
    }
}
