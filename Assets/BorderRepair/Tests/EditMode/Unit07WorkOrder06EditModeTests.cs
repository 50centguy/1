using System;
using System.Linq;
using BorderRepair.Dock;
using BorderRepair.RobotRepair;
using BorderRepair.Unit07WorkOrder;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.Tests
{
    /// <summary>
    /// 工单 06 接入 UNIT 07 维修座（编辑模式）。
    /// 前半部分用构造的维修座状态验证工单的顺序和安全规则；后半部分用真实的维修座和七号预制体（按 1/60 s 推进）验证
    /// “工单是唯一放行恢复供电的接口、维修座是唯一决定供电的控制器”，以及复测失败后重新停靠的完整往返。
    /// </summary>
    public class Unit07WorkOrder06EditModeTests
    {
        // ================================================================== 构造的维修座状态

        Unit07DockSnapshot fake;

        static Unit07DockSnapshot Snap(DockState s, bool trays = true)
        {
            bool powered = s != DockState.SpinningDown && s != DockState.RotorsStopped;
            return new Unit07DockSnapshot { State = s, PowerOn = powered, RotorsAtIdle = powered && s != DockState.SpinningUp, TraysStowed = trays };
        }

        Unit07WorkOrder06Session NewFake()
        {
            fake = Snap(DockState.Hovering);
            return new Unit07WorkOrder06Session(() => fake);
        }

        void SetDock(Unit07WorkOrder06Session s, DockState state, bool trays = true) { fake = Snap(state, trays); s.Sync(); }

        /// <summary>按真实维修座的顺序把构造的状态推到“断电、叶轮停稳”。</summary>
        void DockAndStop(Unit07WorkOrder06Session s)
        {
            foreach (var st in new[] { DockState.ClampsOpening, DockState.Descending, DockState.SeatedOpen, DockState.Clamping,
                                       DockState.Clamped, DockState.SpinningDown, DockState.RotorsStopped })
                SetDock(s, st);
        }

        void Undock(Unit07WorkOrder06Session s)
        {
            foreach (var st in new[] { DockState.SpinningUp, DockState.Clamped, DockState.ClampsOpening, DockState.SeatedOpen,
                                       DockState.LiftingOff, DockState.Undocked })
                SetDock(s, st);
        }

        static void InspectAll(Unit07WorkOrder06Session s)
        {
            foreach (var a in s.Plan.InspectionAnchors) Assert.IsTrue(s.Inspect(a), s.LastFeedback);
        }

        static void RemoveAll(Unit07WorkOrder06Session s)
        {
            foreach (var step in s.Plan.RemovalSteps) Assert.IsTrue(s.Remove(step.Id), s.LastFeedback);
        }

        static void InstallAll(Unit07WorkOrder06Session s)
        {
            foreach (var step in s.Plan.RemovalSteps.Reverse()) Assert.IsTrue(s.Install(step.Id), s.LastFeedback);
        }

        /// <summary>从头推进到指定阶段（第一轮）。</summary>
        Unit07WorkOrder06Session Reach(RobotRepairStage target)
        {
            var s = NewFake();
            if (target == RobotRepairStage.Dock) return s;
            SetDock(s, DockState.Clamped);
            if (target == RobotRepairStage.PowerOff) return s;
            SetDock(s, DockState.SpinningDown); SetDock(s, DockState.RotorsStopped);
            if (target == RobotRepairStage.Inspect) return s;
            InspectAll(s);
            if (target == RobotRepairStage.Disassemble) return s;
            RemoveAll(s);
            if (target == RobotRepairStage.RepairChoice) return s;
            Assert.IsTrue(s.ChooseRepair(RobotRepairChoice.RebalanceRotor), s.LastFeedback);
            if (target == RobotRepairStage.Reassemble) return s;
            InstallAll(s);
            if (target == RobotRepairStage.PowerOn) return s;
            Assert.IsTrue(s.RunPrePowerCheck(), s.LastFeedback);
            Undock(s);
            if (target == RobotRepairStage.HoverRetest) return s;
            Assert.IsTrue(s.HoverRetest(true), s.LastFeedback);
            if (target == RobotRepairStage.LoadRetest) return s;
            Assert.IsTrue(s.LoadRetest(true), s.LastFeedback);
            return s;
        }

        [Test]
        public void Ids_KeepCase06AndUseUnit07()
        {
            var s = NewFake();
            Assert.AreEqual("case_06_left_engine_imbalance", s.CaseId);
            Assert.AreEqual("UNIT07", Unit07WorkOrder06Session.RobotId);
            Assert.AreEqual(3, s.Plan.InspectionAnchors.Length);
            Assert.AreEqual(13, s.Plan.RemovalSteps.Length);
            StringAssert.DoesNotContain("六号", s.LastFeedback, "七号接入后的初始提示不应写六号");
            DockAndStop(s);
            s.Inspect(s.Plan.InspectionAnchors[0]);
            Assert.IsTrue(s.Log.All(e => e.RobotId == "UNIT07" && e.CaseId == "case_06_left_engine_imbalance"));
        }

        [Test]
        public void DockStepsFollowRealDockState_NotPlayerButtons()
        {
            var s = NewFake();
            foreach (var st in new[] { DockState.Hovering, DockState.ClampsOpening, DockState.Descending, DockState.SeatedOpen, DockState.Clamping })
            {
                SetDock(s, st);
                Assert.AreEqual(RobotRepairStage.Dock, s.Stage, $"维修座 {st}：还没夹紧，工单仍在等待停靠");
            }
            SetDock(s, DockState.Clamped);
            Assert.AreEqual(RobotRepairStage.PowerOff, s.Stage, "夹紧后工单进入断电");
            SetDock(s, DockState.SpinningDown);
            Assert.AreEqual(RobotRepairStage.PowerOff, s.Stage, "叶轮减速中不算断电完成");
            SetDock(s, DockState.RotorsStopped);
            Assert.AreEqual(RobotRepairStage.Inspect, s.Stage, "叶轮停稳后才进入检查");
            Assert.AreEqual(new[] { RobotRepairAction.Dock, RobotRepairAction.PowerOff }, s.Inner.Events.Select(e => e.Action).ToArray());

            // 夹紧后又松开离座（没断电）：工单停在断电，重新夹紧后继续
            var t = NewFake();
            SetDock(t, DockState.Clamped);
            SetDock(t, DockState.ClampsOpening); SetDock(t, DockState.SeatedOpen); SetDock(t, DockState.LiftingOff); SetDock(t, DockState.Undocked);
            Assert.AreEqual(RobotRepairStage.PowerOff, t.Stage);
            Assert.AreEqual("让七号重新落座并夹紧（维修座）", t.NextHint());
            SetDock(t, DockState.Descending); SetDock(t, DockState.SeatedOpen); SetDock(t, DockState.Clamping); SetDock(t, DockState.Clamped);
            SetDock(t, DockState.SpinningDown); SetDock(t, DockState.RotorsStopped);
            Assert.AreEqual(RobotRepairStage.Inspect, t.Stage);
        }

        [Test]
        public void FullSequence_WithFailedHoverAndLoadRetests_RedocksAndRepairsAgain()
        {
            var s = NewFake();
            DockAndStop(s);
            InspectAll(s);
            Assert.AreEqual(RobotRepairStage.Disassemble, s.Stage);
            Assert.IsFalse(s.Remove("2"), "越序拆卸应被拒绝");
            StringAssert.Contains("下一步：1", s.LastFeedback);
            RemoveAll(s);
            Assert.AreEqual(RobotRepairStage.RepairChoice, s.Stage);
            Assert.IsTrue(s.ChooseRepair(RobotRepairChoice.RebalanceRotor));
            Assert.IsFalse(s.Install("1"), "装回必须逆序，第一步是 10Lc");
            InstallAll(s);
            Assert.AreEqual(RobotRepairStage.PowerOn, s.Stage);
            Assert.IsFalse(s.CanFinishService(out var why), "装回后还没做通电前检查");
            StringAssert.Contains("通电前检查", why);
            Assert.IsTrue(s.RunPrePowerCheck(), s.LastFeedback);
            Assert.IsTrue(s.CanFinishService(out why), why);
            Assert.AreEqual(RobotRepairStage.PowerOn, s.Stage, "工单只放行，维修座通电后才进入复测");

            SetDock(s, DockState.SpinningUp);
            Assert.AreEqual(RobotRepairStage.HoverRetest, s.Stage, "维修座通电后同步为恢复供电");
            Assert.IsFalse(s.HoverRetest(true), "还夹在维修座上，不能复测");
            Undock(s);
            Assert.IsTrue(s.HoverRetest(false), s.LastFeedback);
            Assert.AreEqual(RobotRepairStage.Dock, s.Stage, "悬停复测失败：回到停靠");
            Assert.AreEqual(1, s.RepairCycle);
            Assert.IsFalse(s.PrePowerChecked, "失败后通电前检查要重新做");
            StringAssert.Contains("重新落座", s.LastFeedback);

            // 第二轮：重新落座、夹紧、断电；跳过检查，直接重新拆卸
            SetDock(s, DockState.Descending); SetDock(s, DockState.SeatedOpen); SetDock(s, DockState.Clamping); SetDock(s, DockState.Clamped);
            Assert.AreEqual(RobotRepairStage.PowerOff, s.Stage);
            Assert.IsFalse(s.CanFinishService(out _), "复测失败、没重新维修前不放行");
            SetDock(s, DockState.SpinningDown); SetDock(s, DockState.RotorsStopped);
            Assert.AreEqual(RobotRepairStage.Disassemble, s.Stage);
            Assert.IsFalse(s.CanFinishService(out _));
            RemoveAll(s);
            Assert.IsTrue(s.ChooseRepair(RobotRepairChoice.ReplaceMotorCore));
            InstallAll(s);
            Assert.IsTrue(s.RunPrePowerCheck(), s.LastFeedback);
            Undock(s);
            Assert.IsTrue(s.HoverRetest(true), s.LastFeedback);
            Assert.AreEqual(RobotRepairStage.LoadRetest, s.Stage);
            Assert.IsTrue(s.LoadRetest(false), s.LastFeedback);
            Assert.AreEqual(RobotRepairStage.Dock, s.Stage, "负载复测失败：回到停靠");
            Assert.AreEqual(2, s.RepairCycle);

            // 第三轮：通过
            DockAndStopFromUndocked(s);
            Assert.AreEqual(RobotRepairStage.Disassemble, s.Stage);
            RemoveAll(s);
            Assert.IsTrue(s.ChooseRepair(RobotRepairChoice.RebalanceRotor));
            InstallAll(s);
            Assert.IsTrue(s.RunPrePowerCheck());
            Undock(s);
            Assert.IsTrue(s.HoverRetest(true));
            Assert.IsTrue(s.LoadRetest(true));
            Assert.AreEqual(RobotRepairStage.Complete, s.Stage);
            Assert.AreEqual("工单完成", s.NextHint());
            Assert.IsNull(s.SafetyFault);
            Assert.IsTrue(s.CanFinishService(out _), "工单完成后，之后的断电维修也允许恢复供电");
        }

        void DockAndStopFromUndocked(Unit07WorkOrder06Session s)
        {
            foreach (var st in new[] { DockState.Descending, DockState.SeatedOpen, DockState.Clamping, DockState.Clamped,
                                       DockState.SpinningDown, DockState.RotorsStopped })
                SetDock(s, st);
        }

        static readonly RobotRepairStage[] WorkStages =
            { RobotRepairStage.Inspect, RobotRepairStage.Disassemble, RobotRepairStage.RepairChoice, RobotRepairStage.Reassemble, RobotRepairStage.PowerOn };

        /// <summary>每个工作阶段 × 每个不是“断电、叶轮停稳”的维修座状态：检查、拆卸、维修选择、装回、通电前检查全部被拒绝，工单不变。</summary>
        [Test]
        public void WorkCommands_RefusedUnlessRotorsStopped()
        {
            int refused = 0;
            foreach (var stage in WorkStages)
            foreach (DockState st in Enum.GetValues(typeof(DockState)))
            {
                if (st == DockState.RotorsStopped) continue;
                var s = Reach(stage);
                Assert.AreEqual(stage, s.Stage);
                int events = s.Inner.Events.Count;
                fake = Snap(st);                      // 不调用 Sync：每个操作自己读维修座
                var next = s.Plan.RemovalSteps[0].Id;
                var attempts = new Func<bool>[]
                {
                    () => s.Inspect(s.Plan.InspectionAnchors.FirstOrDefault(a => !s.IsInspected(a)) ?? s.Plan.InspectionAnchors[0]),
                    () => s.Remove(s.Inner.NextRemoval?.Id ?? next),
                    () => s.ChooseRepair(RobotRepairChoice.RebalanceRotor),
                    () => s.Install(s.Inner.NextInstallation?.Id ?? next),
                    () => s.RunPrePowerCheck(),
                };
                foreach (var a in attempts)
                {
                    Assert.IsFalse(a(), $"工单 {stage}、维修座 {st}：应拒绝");
                    Assert.IsFalse(string.IsNullOrEmpty(s.LastFeedback));
                    refused++;
                }
                Assert.AreEqual(events, s.Inner.Events.Count, $"工单 {stage}、维修座 {st}：被拒绝的操作不应记入工单");
                Assert.IsFalse(s.PrePowerChecked);
                if (st == DockState.SpinningDown) StringAssert.Contains("减速", s.LastFeedback);
            }
            Assert.AreEqual(WorkStages.Length * 10 * 5, refused);
        }

        [Test]
        public void Retests_RefusedUntilUndockedAtHoverSpeed()
        {
            foreach (var stage in new[] { RobotRepairStage.HoverRetest, RobotRepairStage.LoadRetest })
            foreach (DockState st in Enum.GetValues(typeof(DockState)))
            {
                if (st == DockState.Undocked) continue;
                if (st == DockState.SpinningDown || st == DockState.RotorsStopped) continue;   // 见下：断电后工单不在复测
                var s = Reach(stage);
                SetDock(s, st);
                int events = s.Inner.Events.Count;
                Assert.IsFalse(stage == RobotRepairStage.HoverRetest ? s.HoverRetest(true) : s.LoadRetest(true), $"{stage}、维修座 {st}：应拒绝");
                Assert.IsFalse(stage == RobotRepairStage.HoverRetest ? s.HoverRetest(false) : s.LoadRetest(false), $"{stage}、维修座 {st}：应拒绝");
                StringAssert.Contains("离座悬停后", s.LastFeedback);
                Assert.AreEqual(stage, s.Stage);
                Assert.AreEqual(events, s.Inner.Events.Count);
            }
            // 已离座但转子没在悬停转速（不应出现，防御）
            var u = Reach(RobotRepairStage.HoverRetest);
            fake = new Unit07DockSnapshot { State = DockState.Undocked, PowerOn = true, RotorsAtIdle = false, TraysStowed = true };
            Assert.IsFalse(u.HoverRetest(true));
            // 复测前又落座、断电：复测被拒绝，恢复供电仍放行（期间没有再拆）
            var v = Reach(RobotRepairStage.HoverRetest);
            DockAndStopFromUndocked(v);
            Assert.IsFalse(v.HoverRetest(true));
            Assert.IsFalse(v.Inspect(v.Plan.InspectionAnchors[0]), "复测阶段不能重新检查");
            Assert.IsFalse(v.Remove("1"), "复测阶段不能拆卸");
            Assert.IsTrue(v.CanFinishService(out var why), why);
        }

        [Test]
        public void Gate_ClosedUntilReassembledAndPrePowerChecked()
        {
            foreach (RobotRepairStage stage in Enum.GetValues(typeof(RobotRepairStage)))
            {
                var s = Reach(stage);
                bool open = s.CanFinishService(out var why);
                bool expected = stage == RobotRepairStage.HoverRetest || stage == RobotRepairStage.LoadRetest || stage == RobotRepairStage.Complete;
                Assert.AreEqual(expected, open, $"工单阶段 {stage}：放行应为 {expected}（{why}）");
                if (!open) Assert.IsFalse(string.IsNullOrEmpty(why), $"{stage}：拒绝时要给出原因");
            }
            // 装回完成：检查前关、检查后开
            var p = Reach(RobotRepairStage.PowerOn);
            Assert.IsFalse(p.CanFinishService(out _));
            Assert.IsTrue(p.RunPrePowerCheck());
            Assert.IsTrue(p.CanFinishService(out _));
            Assert.IsFalse(p.RunPrePowerCheck(), "重复检查");
            // 装回差一步：不能检查，也不放行
            var q = Reach(RobotRepairStage.Reassemble);
            foreach (var step in q.Plan.RemovalSteps.Reverse().Take(12)) Assert.IsTrue(q.Install(step.Id));
            Assert.AreEqual(RobotRepairStage.Reassemble, q.Stage);
            Assert.IsFalse(q.RunPrePowerCheck());
            Assert.IsFalse(q.CanFinishService(out _));
        }

        [Test]
        public void PrePowerCheck_RequiresTraysStowed()
        {
            var s = Reach(RobotRepairStage.PowerOn);
            SetDock(s, DockState.RotorsStopped, trays: false);
            Assert.IsFalse(s.RunPrePowerCheck());
            StringAssert.Contains("零件盘", s.LastFeedback);
            Assert.IsFalse(s.CanFinishService(out _));
            SetDock(s, DockState.RotorsStopped, trays: true);
            Assert.IsTrue(s.RunPrePowerCheck(), s.LastFeedback);
        }

        [Test]
        public void DockPoweredWithoutGate_IsReportedAsSafetyFault()
        {
            var s = Reach(RobotRepairStage.Disassemble);
            Assert.IsNull(s.SafetyFault);
            SetDock(s, DockState.SpinningUp);     // 假设接线错误：维修座绕过了工单接口
            Assert.IsNotNull(s.SafetyFault);
            Assert.AreEqual(RobotRepairStage.Disassemble, s.Stage, "绕过接口通电不会推进工单");
            Assert.IsFalse(s.Remove(s.Inner.NextRemoval.Id), "通电时不能拆卸");
        }

        // ================================================================== 真实维修座

        const string DockPrefab = "Assets/BorderRepair/Prefabs/Unit07Dock/Unit07ServiceDock.prefab";
        const string RobotPrefab = "Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_DockReady.prefab";
        const float Dt = 1f / 60f;

        GameObject dockGo, robot, flow;
        Unit07DockController dock;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in new[] { dockGo, robot, flow }) if (go != null) Object.DestroyImmediate(go);
        }

        static Transform Find(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        Unit07WorkOrder06Session WireRealDock()
        {
            dockGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DockPrefab));
            robot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefab));
            var anchor = Find(dockGo, "Dock_RobotAnchor");
            robot.transform.position = anchor.position;
            flow = new GameObject("Unit07WorkOrder06_Test");
            dock = flow.AddComponent<Unit07DockController>();
            dock.Configure(robot.transform, robot.GetComponent<Animator>(), robot.GetComponent<RotorPowerDriver>(), anchor,
                           Find(dockGo, "Dock_Clamp_L"), Find(dockGo, "Dock_Clamp_R"), Find(dockGo, "Dock_PowerSwitch_Lever"),
                           Find(dockGo, "Dock_PowerSwitch_Lamp").GetComponent<Renderer>());
            var trays = dockGo.GetComponentsInChildren<DockPickable>(true);
            var s = new Unit07WorkOrder06Session(() => new Unit07DockSnapshot
            {
                State = dock.State, PowerOn = dock.PowerOn, RotorsAtIdle = dock.Rotors.IsAtIdleSpeed, TraysStowed = trays.All(t => !t.Taken)
            });
            dock.SetServiceCompletionGate(s);
            dock.Initialize();
            Assert.IsTrue(dock.ConfigurationValid);
            dock.ResetToHover();
            return s;
        }

        void RunUntil(Unit07WorkOrder06Session s, Func<bool> cond, float timeout, string what)
        {
            for (float t = 0f; !cond(); t += Dt)
            {
                if (t > timeout) Assert.Fail($"超时：{what}（维修座 {dock.State}，工单 {s.Stage}）");
                dock.Tick(Dt); dock.Rotors.Step(Dt); s.Sync();
            }
        }

        /// <summary>工单操作不改变维修座的供电、状态和夹具。</summary>
        void NoDockSideEffect(Func<bool> op, bool expect, string what)
        {
            var (st, pw, cl, lv) = (dock.State, dock.PowerOn, dock.ClampOpenFraction, dock.LeverOffFraction);
            Assert.AreEqual(expect, op(), what);
            Assert.AreEqual(st, dock.State, what + "：工单操作改变了维修座状态");
            Assert.AreEqual(pw, dock.PowerOn, what + "：工单操作改变了供电");
            Assert.AreEqual(cl, dock.ClampOpenFraction, what);
            Assert.AreEqual(lv, dock.LeverOffFraction, what);
        }

        void RealDockClampStop(Unit07WorkOrder06Session s)
        {
            Assert.IsTrue(dock.Interact(DockAction.Clamps), dock.LastMessage);
            RunUntil(s, () => dock.State == DockState.SeatedOpen, 5f, "落座");
            Assert.IsTrue(dock.Interact(DockAction.Clamps), dock.LastMessage);
            RunUntil(s, () => dock.State == DockState.Clamped, 3f, "夹紧");
            Assert.AreEqual(RobotRepairStage.PowerOff, s.Stage);
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch), dock.LastMessage);
            Assert.AreEqual(DockState.SpinningDown, dock.State);
            NoDockSideEffect(() => s.Inspect(s.Plan.InspectionAnchors[0]), false, "叶轮减速中检查");
            Assert.IsFalse(dock.Interact(DockAction.PowerSwitch), "减速中恢复供电：工单不放行");
            StringAssert.Contains("工单 06", dock.LastMessage);
            RunUntil(s, () => dock.State == DockState.RotorsStopped, 5f, "停转");
        }

        void RealDockRestoreAndUndock(Unit07WorkOrder06Session s)
        {
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch), dock.LastMessage);
            Assert.AreEqual(DockState.SpinningUp, dock.State);
            s.Sync();
            Assert.AreEqual(RobotRepairStage.HoverRetest, s.Stage);
            NoDockSideEffect(() => s.HoverRetest(true), false, "加速中复测");
            RunUntil(s, () => dock.State == DockState.Clamped, 3f, "转子回到悬停转速");
            NoDockSideEffect(() => s.HoverRetest(true), false, "夹紧时复测");
            Assert.IsTrue(dock.Interact(DockAction.Clamps), dock.LastMessage);
            RunUntil(s, () => dock.State == DockState.SeatedOpen, 3f, "夹具张开");
            NoDockSideEffect(() => s.HoverRetest(true), false, "落座未离座时复测");
            Assert.IsTrue(dock.Interact(DockAction.LiftOff), dock.LastMessage);
            RunUntil(s, () => dock.State == DockState.Undocked, 3f, "离座");
            Assert.IsFalse(dock.Rotors.Driven, "离座后转子交还 Animator");
        }

        [Test]
        public void RealDock_FullRoundTrip_FailedRetestRedocks_DockIsOnlyPowerAuthority()
        {
            var s = WireRealDock();
            Assert.AreSame(s, dock.ServiceGate);
            NoDockSideEffect(() => s.Inspect(s.Plan.InspectionAnchors[0]), false, "悬停时检查");
            RealDockClampStop(s);
            Assert.AreEqual(RobotRepairStage.Inspect, s.Stage);

            // 每一步工单操作都不碰维修座；每个阶段都尝试恢复供电，都被工单拒绝
            foreach (var a in s.Plan.InspectionAnchors)
            {
                Assert.IsFalse(dock.Interact(DockAction.PowerSwitch), "检查中恢复供电");
                NoDockSideEffect(() => s.Inspect(a), true, "检查 " + a);
            }
            foreach (var step in s.Plan.RemovalSteps)
            {
                Assert.IsFalse(dock.Interact(DockAction.PowerSwitch), "拆卸中恢复供电");
                Assert.IsFalse(dock.Interact(DockAction.Clamps), "拆卸中松开夹具");
                Assert.IsFalse(dock.Interact(DockAction.LiftOff), "拆卸中离座");
                NoDockSideEffect(() => s.Remove(step.Id), true, "拆卸 " + step.Id);
            }
            Assert.IsFalse(dock.Interact(DockAction.PowerSwitch), "露出马达芯时恢复供电");
            NoDockSideEffect(() => s.ChooseRepair(RobotRepairChoice.RebalanceRotor), true, "选择维修方式");
            foreach (var step in s.Plan.RemovalSteps.Reverse())
            {
                Assert.IsFalse(dock.Interact(DockAction.PowerSwitch), "装回中恢复供电");
                NoDockSideEffect(() => s.Install(step.Id), true, "装回 " + step.Id);
            }
            Assert.IsFalse(dock.Interact(DockAction.PowerSwitch), "装回后、通电前检查前恢复供电");
            StringAssert.Contains("通电前检查", dock.LastMessage);
            Assert.AreEqual(DockState.RotorsStopped, dock.State);
            Assert.IsFalse(dock.PowerOn);
            NoDockSideEffect(() => s.RunPrePowerCheck(), true, "通电前检查");
            Assert.IsFalse(dock.PowerOn, "通电前检查只放行，不自己通电");
            RealDockRestoreAndUndock(s);

            // 悬停复测失败 → 重新落座、夹紧、断电 → 第二轮
            NoDockSideEffect(() => s.HoverRetest(false), true, "悬停复测失败");
            Assert.AreEqual(RobotRepairStage.Dock, s.Stage);
            Assert.IsTrue(dock.Interact(DockAction.Clamps), "复测失败后可以重新落座：" + dock.LastMessage);
            RunUntil(s, () => dock.State == DockState.SeatedOpen, 5f, "重新落座");
            Assert.IsTrue(dock.Interact(DockAction.Clamps), dock.LastMessage);
            RunUntil(s, () => dock.State == DockState.Clamped, 3f, "重新夹紧");
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch), dock.LastMessage);
            RunUntil(s, () => dock.State == DockState.RotorsStopped, 5f, "再次停转");
            Assert.AreEqual(RobotRepairStage.Disassemble, s.Stage, "第二轮直接重新拆卸");
            Assert.IsFalse(dock.Interact(DockAction.PowerSwitch), "第二轮未装回前恢复供电");
            foreach (var step in s.Plan.RemovalSteps) Assert.IsTrue(s.Remove(step.Id), s.LastFeedback);
            Assert.IsTrue(s.ChooseRepair(RobotRepairChoice.ReplaceMotorCore));
            foreach (var step in s.Plan.RemovalSteps.Reverse()) Assert.IsTrue(s.Install(step.Id), s.LastFeedback);
            Assert.IsTrue(s.RunPrePowerCheck(), s.LastFeedback);
            RealDockRestoreAndUndock(s);
            Assert.IsTrue(s.HoverRetest(true), s.LastFeedback);
            Assert.IsTrue(s.LoadRetest(true), s.LastFeedback);
            Assert.AreEqual(RobotRepairStage.Complete, s.Stage);
            Assert.AreEqual(1, s.RepairCycle);
            Assert.IsNull(s.SafetyFault);
            Assert.AreEqual(DockState.Undocked, dock.State);
            Assert.IsTrue(dock.PowerOn);
        }

        [Test]
        public void RealDock_PrePowerCheckRefusedWhileTrayTaken()
        {
            var s = WireRealDock();
            // 编辑模式下预制体实例里的物体不能换父节点（取下物品要移到手持位置），先解包
            PrefabUtility.UnpackPrefabInstance(dockGo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var tray = dockGo.GetComponentsInChildren<DockPickable>(true).First();
            var hold = new GameObject("Hold").transform;
            tray.HoldPoint = hold;
            RealDockClampStop(s);
            foreach (var a in s.Plan.InspectionAnchors) s.Inspect(a);
            foreach (var step in s.Plan.RemovalSteps) s.Remove(step.Id);
            s.ChooseRepair(RobotRepairChoice.RebalanceRotor);
            foreach (var step in s.Plan.RemovalSteps.Reverse()) s.Install(step.Id);
            Assert.AreEqual(RobotRepairStage.PowerOn, s.Stage);
            Assert.IsTrue(dock.Interact(DockAction.PartsTray, tray), dock.LastMessage);
            Assert.IsTrue(tray.Taken);
            Assert.IsFalse(s.RunPrePowerCheck());
            Assert.IsFalse(dock.Interact(DockAction.PowerSwitch));
            Assert.IsTrue(dock.Interact(DockAction.PartsTray, tray));
            Assert.IsTrue(s.RunPrePowerCheck(), s.LastFeedback);
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch), dock.LastMessage);
            Object.DestroyImmediate(hold.gameObject);
        }
    }
}
