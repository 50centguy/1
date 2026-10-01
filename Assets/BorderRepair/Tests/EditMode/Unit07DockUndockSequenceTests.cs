using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Dock;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.Tests
{
    /// <summary>
    /// UNIT 07 维修座第二阶段（维修结束后离座）的顺序测试（编辑模式）：
    /// 用真实的维修座和七号预制体，控制器与转子按固定步长（1/60 s）推进，不进入 Play 模式。
    /// 检查每个阶段的手柄、状态灯、转子、机身高度一致，以及越序操作被拒绝且不改变任何状态。
    /// Animator 在编辑模式不播放，动画接管转子在 PlayMode 场景测试里验证。
    /// </summary>
    public class Unit07DockUndockSequenceTests
    {
        const string DockPrefab = "Assets/BorderRepair/Prefabs/Unit07Dock/Unit07ServiceDock.prefab";
        const string RobotPrefab = "Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_DockReady.prefab";
        const float Dt = 1f / 60f;

        GameObject dockGo, robot, flow;
        Unit07DockController dock;
        ManualServiceCompletionGate gate;
        Renderer lamp;

        [SetUp]
        public void SetUp()
        {
            dockGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DockPrefab));
            robot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefab));
            var anchor = Find(dockGo, "Dock_RobotAnchor");
            robot.transform.position = anchor.position;
            flow = new GameObject("Unit07DockFlow_Test");
            dock = flow.AddComponent<Unit07DockController>();
            lamp = Find(dockGo, "Dock_PowerSwitch_Lamp").GetComponent<Renderer>();
            dock.Configure(robot.transform, robot.GetComponent<Animator>(), robot.GetComponent<RotorPowerDriver>(), anchor,
                           Find(dockGo, "Dock_Clamp_L"), Find(dockGo, "Dock_Clamp_R"), Find(dockGo, "Dock_PowerSwitch_Lever"), lamp);
            gate = flow.AddComponent<ManualServiceCompletionGate>();
            dock.SetServiceCompletionGate(gate);
            dock.Initialize();
            Assert.IsTrue(dock.ConfigurationValid);
            dock.ResetToHover();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in new[] { dockGo, robot, flow }) if (go != null) Object.DestroyImmediate(go);
        }

        static Transform Find(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Dt) { dock.Tick(Dt); dock.Rotors.Step(Dt); }
        }

        void RunUntil(Func<bool> cond, float timeout, string what)
        {
            for (float t = 0f; !cond(); t += Dt)
            {
                if (t > timeout) Assert.Fail($"超时：{what}（状态 {dock.State}）");
                dock.Tick(Dt); dock.Rotors.Step(Dt);
            }
        }

        float RobotHeight => dock.RobotRoot.position.y - dock.RobotAnchor.position.y;

        Color LampShown()
        {
            var mpb = new MaterialPropertyBlock();
            lamp.GetPropertyBlock(mpb);
            return mpb.GetColor("_BaseColor");
        }

        /// <summary>手柄、状态灯、转子、机身高度彼此一致。</summary>
        void AssertConsistent(string phase)
        {
            Run(0.4f);   // 等手柄走完（0.3 s）
            float lever = dock.PowerOn ? 0f : 1f;
            if (dock.State == DockState.LiftingOff || dock.State == DockState.Descending) return;   // 过渡中不比
            Assert.AreEqual(lever, dock.LeverOffFraction, 1e-4f, $"{phase}：手柄位置与供电不一致");
            Assert.Less(Vector4.Distance(LampShown(), dock.LampColor), 1e-3f, $"{phase}：状态灯与供电不一致");
            Assert.AreEqual(dock.PowerOn, dock.Rotors.Powered, $"{phase}：转子供电与开关不一致");
            if (!dock.PowerOn) Assert.Less(dock.Rotors.SpeedDegPerSec, dock.Rotors.IdleSpeedDegPerSec, $"{phase}：断电后转子应在减速或静止");
            if (dock.IsSeated) Assert.AreEqual(0f, RobotHeight, 1e-4f, $"{phase}：落座时七号根应在锚点上");
            if (dock.State == DockState.Hovering || dock.State == DockState.Undocked)
                Assert.AreEqual(dock.HoverHeight, RobotHeight, 1e-4f, $"{phase}：悬停时七号根应在锚点上方 {dock.HoverHeight} m");
        }

        void DockClampPowerOffStop()
        {
            Assert.IsTrue(dock.Interact(DockAction.Clamps), dock.LastMessage);
            RunUntil(() => dock.State == DockState.SeatedOpen, 5f, "落座");
            Assert.IsTrue(dock.Interact(DockAction.Clamps), dock.LastMessage);
            RunUntil(() => dock.State == DockState.Clamped, 3f, "夹紧");
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch), dock.LastMessage);
            RunUntil(() => dock.State == DockState.RotorsStopped, 5f, "停转");
        }

        [Test]
        public void FullSequence_ServiceThenUndock_StaysConsistent()
        {
            AssertConsistent("初始悬停");
            DockClampPowerOffStop();
            AssertConsistent("断电停转");
            Assert.IsTrue(dock.CanOperateImpeller);

            // 1. 确认可以恢复供电
            gate.Confirm();
            Assert.IsTrue(dock.CanRestorePower(out var why), why);
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch), dock.LastMessage);
            Assert.AreEqual(DockState.SpinningUp, dock.State);
            Assert.IsFalse(dock.CanOperateImpeller, "恢复供电后叶轮禁止操作");
            Assert.AreEqual(0f, dock.ClampOpenFraction, "加速期间夹具保持夹紧");
            // 2. 转子回到正常转速
            float s0 = dock.Rotors.SpeedDegPerSec;
            Run(0.3f);
            Assert.Greater(dock.Rotors.SpeedDegPerSec, s0, "转子在加速");
            RunUntil(() => dock.State == DockState.Clamped, dock.Rotors.SpinUpSeconds + 1f, "回到悬停转速");
            Assert.AreEqual(dock.Rotors.IdleSpeedDegPerSec, dock.Rotors.SpeedDegPerSec, 0.01f);
            AssertConsistent("通电、转子正常");
            // 3. 松开夹具
            Assert.IsTrue(dock.Interact(DockAction.Clamps), dock.LastMessage);
            RunUntil(() => dock.State == DockState.SeatedOpen, 2f, "夹具张开");
            Assert.AreEqual(1f, dock.ClampOpenFraction, 1e-4f);
            AssertConsistent("夹具松开、仍落座");
            // 4. 升起离座：升起过程中转子仍由维修座按悬停转速驱动
            Assert.IsTrue(dock.Interact(DockAction.LiftOff), dock.LastMessage);
            Assert.AreEqual(DockState.LiftingOff, dock.State);
            Run(0.5f);
            Assert.That(RobotHeight > 0.001f && RobotHeight < dock.HoverHeight, $"升起中：{RobotHeight}");
            Assert.IsTrue(dock.Rotors.Driven, "升起途中转子仍由维修座驱动");
            Assert.AreEqual(dock.Rotors.IdleSpeedDegPerSec, dock.Rotors.SpeedDegPerSec, 0.01f);
            // 5. 离座悬停 → 转子交还 Animator
            RunUntil(() => dock.State == DockState.Undocked, 3f, "离座");
            Assert.IsFalse(dock.Rotors.Driven, "离座后转子交还 Animator");
            Assert.IsTrue(dock.Rotors.IsAtIdleSpeed);
            Assert.AreEqual(1f, dock.ClampOpenFraction, 1e-4f, "离座后夹具保持张开");
            AssertConsistent("离座悬停");
        }

        [Test]
        public void PowerRestore_RequiresServiceCompletionGate()
        {
            DockClampPowerOffStop();
            // 没有接入接口
            dock.SetServiceCompletionGate(null);
            Assert.IsFalse(dock.Interact(DockAction.PowerSwitch));
            StringAssert.Contains("允许结束维修", dock.LastMessage);
            Assert.AreEqual(DockState.RotorsStopped, dock.State);
            Assert.IsFalse(dock.PowerOn);
            // 接入但还没确认
            dock.SetServiceCompletionGate(gate);
            gate.Revoke();
            Assert.IsFalse(dock.Interact(DockAction.PowerSwitch));
            StringAssert.Contains("确认", dock.LastMessage);
            Assert.AreEqual(DockState.RotorsStopped, dock.State);
            // 自定义实现：原因原样显示给玩家
            dock.SetServiceCompletionGate(new FakeGate(false, "左上盖还没装回。"));
            Assert.IsFalse(dock.Interact(DockAction.PowerSwitch));
            Assert.AreEqual("左上盖还没装回。", dock.LastMessage);
            dock.SetServiceCompletionGate(new FakeGate(true, ""));
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch), dock.LastMessage);
            Assert.AreEqual(DockState.SpinningUp, dock.State);
        }

        [Test]
        public void PowerRestore_DuringSpinDown_AlsoRequiresGate()
        {
            Assert.IsTrue(dock.Interact(DockAction.Clamps));
            RunUntil(() => dock.State == DockState.SeatedOpen, 5f, "落座");
            Assert.IsTrue(dock.Interact(DockAction.Clamps));
            RunUntil(() => dock.State == DockState.Clamped, 3f, "夹紧");
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch));
            Run(0.3f);
            Assert.AreEqual(DockState.SpinningDown, dock.State);
            Assert.IsFalse(dock.Interact(DockAction.PowerSwitch), "减速中也不能未经确认就恢复供电");
            gate.Confirm();
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch), dock.LastMessage);
            RunUntil(() => dock.State == DockState.Clamped, 3f, "回到悬停转速");
        }

        /// <summary>每个阶段把不该做的操作都试一遍：必须被拒绝，并且状态、供电、夹具、手柄、机身高度、转子接管都不变。</summary>
        [Test]
        public void OutOfOrderActions_RefusedAtEveryPhase_WithoutSideEffects()
        {
            var log = new List<string>();
            void Refused(string phase, params DockAction[] actions)
            {
                foreach (var a in actions)
                {
                    var before = (dock.State, dock.PowerOn, dock.ClampOpenFraction, RobotHeight, dock.Rotors.Driven, dock.Rotors.Powered);
                    bool ok = dock.Interact(a);
                    var after = (dock.State, dock.PowerOn, dock.ClampOpenFraction, RobotHeight, dock.Rotors.Driven, dock.Rotors.Powered);
                    Assert.IsFalse(ok, $"{phase}：{a} 应被拒绝（实际被接受：{dock.LastMessage}）");
                    Assert.IsNotEmpty(dock.LastMessage, $"{phase}：{a} 被拒绝时要给出原因");
                    Assert.AreEqual(before, after, $"{phase}：被拒绝的 {a} 改变了状态");
                    log.Add($"{phase} / {a}：{dock.LastMessage}");
                }
            }

            Refused("悬停", DockAction.LiftOff, DockAction.PowerSwitch, DockAction.EngineLeft);
            Assert.IsTrue(dock.Interact(DockAction.Clamps));
            RunUntil(() => dock.State == DockState.SeatedOpen, 5f, "落座");
            Refused("刚落座、夹具张开", DockAction.PowerSwitch, DockAction.EngineLeft);
            Assert.IsTrue(dock.Interact(DockAction.Clamps));
            RunUntil(() => dock.State == DockState.Clamped, 3f, "夹紧");
            Refused("已夹紧、通电", DockAction.LiftOff, DockAction.EngineLeft);
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch));
            Run(0.2f);
            Refused("断电减速中", DockAction.Clamps, DockAction.LiftOff, DockAction.EngineLeft, DockAction.PowerSwitch);
            RunUntil(() => dock.State == DockState.RotorsStopped, 5f, "停转");
            Refused("断电停转（维修中）", DockAction.Clamps, DockAction.LiftOff, DockAction.PowerSwitch);
            gate.Confirm();
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch));
            Run(0.2f);
            Refused("恢复供电、转子加速中", DockAction.Clamps, DockAction.LiftOff, DockAction.EngineLeft);
            RunUntil(() => dock.State == DockState.Clamped, 3f, "回到悬停转速");
            Refused("转子正常、仍夹紧", DockAction.LiftOff, DockAction.EngineLeft);
            Assert.IsTrue(dock.Interact(DockAction.Clamps));
            Run(0.2f);
            Refused("夹具松开中", DockAction.LiftOff, DockAction.Clamps);
            RunUntil(() => dock.State == DockState.SeatedOpen, 2f, "夹具张开");
            Refused("夹具已松开、未升起", DockAction.PowerSwitch, DockAction.EngineLeft);
            Assert.IsTrue(dock.Interact(DockAction.LiftOff));
            Run(0.3f);
            Refused("升起中", DockAction.Clamps, DockAction.PowerSwitch, DockAction.LiftOff, DockAction.EngineLeft);
            RunUntil(() => dock.State == DockState.Undocked, 3f, "离座");
            Refused("离座悬停", DockAction.LiftOff, DockAction.PowerSwitch, DockAction.EngineLeft);
            TestContext.WriteLine(string.Join("\n", log));
            Assert.GreaterOrEqual(log.Count, 25);
        }

        [Test]
        public void SpinUpThenPowerOffAgain_IsAllowedWhileStillClamped()
        {
            DockClampPowerOffStop();
            gate.Confirm();
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch));
            Run(0.3f);
            Assert.AreEqual(DockState.SpinningUp, dock.State);
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch), "加速中发现问题可以再次断电（夹具仍夹紧）");
            Assert.AreEqual(DockState.SpinningDown, dock.State);
            RunUntil(() => dock.State == DockState.RotorsStopped, 5f, "再次停转");
            AssertConsistent("再次断电停转");
        }

        [Test]
        public void Undocked_CanDockAgain()
        {
            DockClampPowerOffStop();
            gate.Confirm();
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch));
            RunUntil(() => dock.State == DockState.Clamped, 3f, "回到悬停转速");
            Assert.IsTrue(dock.Interact(DockAction.Clamps));
            RunUntil(() => dock.State == DockState.SeatedOpen, 2f, "夹具张开");
            Assert.IsTrue(dock.Interact(DockAction.LiftOff));
            RunUntil(() => dock.State == DockState.Undocked, 3f, "离座");
            Assert.IsTrue(dock.Interact(DockAction.Clamps), dock.LastMessage);
            Assert.AreEqual(DockState.Descending, dock.State);
            RunUntil(() => dock.State == DockState.SeatedOpen, 3f, "重新落座");
            Assert.IsTrue(dock.Rotors.Driven, "重新落座后转子再由维修座接管");
            AssertConsistent("重新落座");
        }

        [Test]
        public void LiftOff_WithoutAnyServiceIsAllowedOncePoweredAndUnclamped()
        {
            // 落座后没有断电维修，直接离座：不需要“允许结束维修”
            Assert.IsTrue(dock.Interact(DockAction.Clamps));
            RunUntil(() => dock.State == DockState.SeatedOpen, 5f, "落座");
            Assert.IsTrue(dock.Interact(DockAction.LiftOff), dock.LastMessage);
            RunUntil(() => dock.State == DockState.Undocked, 3f, "离座");
            Assert.IsFalse(gate.Confirmed);
        }

        class FakeGate : IDockServiceCompletionGate
        {
            readonly bool ok; readonly string reason;
            public FakeGate(bool ok, string reason) { this.ok = ok; this.reason = reason; }
            public bool CanFinishService(out string r) { r = reason; return ok; }
        }
    }
}
