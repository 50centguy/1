using System;
using System.Collections;
using System.Linq;
using BorderRepair.Dock;
using BorderRepair.RobotRepair;
using BorderRepair.Unit07WorkOrder;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.Tests
{
    /// <summary>
    /// 工单 06 × UNIT 07 维修座集成场景（Play 模式）。维修座的夹具、断电开关用镜头射线点击（Unit07DockInput.TryClick），
    /// 离座用 L 键对应的 DockAction.LiftOff；工单操作调用工单面板背后的同一组方法。工单阶段只由桥接组件每帧从维修座同步，测试不手动同步。
    /// 控制台出现错误时测试自动失败。
    /// </summary>
    public class Unit07WorkOrder06PlayModeTests
    {
        const string ScenePath = "Assets/BorderRepair/Scenes/Unit07_WorkOrder06_Test.unity";
        Unit07DockController dock;
        Unit07DockInput input;
        Unit07WorkOrder06Bridge bridge;
        Unit07WorkOrder06Session wo;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("Unit07_WorkOrder06_Test");
#endif
            yield return null;
            dock = UnityEngine.Object.FindFirstObjectByType<Unit07DockController>();
            input = UnityEngine.Object.FindFirstObjectByType<Unit07DockInput>();
            bridge = UnityEngine.Object.FindFirstObjectByType<Unit07WorkOrder06Bridge>();
            Assert.IsNotNull(dock); Assert.IsNotNull(input); Assert.IsNotNull(bridge);
            Assert.IsTrue(dock.ConfigurationValid, "维修座配置无效");
            wo = bridge.Session;
            Assert.IsNotNull(wo);
        }

        static IEnumerator WaitFor(Func<bool> cond, float timeout, string what)
        {
            float t = 0f;
            while (!cond())
            {
                t += Time.deltaTime;
                if (t > timeout) Assert.Fail($"等待超时：{what}");
                yield return null;
            }
        }

        static IEnumerator Seconds(float s)
        {
            float t = 0f;
            while (t < s) { t += Time.deltaTime; yield return null; }
        }

        /// <summary>从镜头点击某个维修座部件（射线必须真的打到该部件的代理）。</summary>
        bool Click(DockAction action)
        {
            var cam = input.ViewCamera;
            foreach (var di in UnityEngine.Object.FindObjectsByType<DockInteractable>(FindObjectsSortMode.None).Where(d => d.action == action))
            {
                var col = di.GetComponent<Collider>();
                if (col == null) continue;
                Vector2 sp = cam.WorldToScreenPoint(col.bounds.center);
                if (input.Pick(sp) != di) continue;
                Assert.IsFalse(bridge.BlocksClick(sp), $"{action} 的点击位置 {sp} 被工单面板挡住（屏幕 {Screen.width}×{Screen.height}）");
                return input.TryClick(sp, out _);
            }
            Assert.Fail($"镜头点不到 {action}（被遮挡或不在画面里）");
            return false;
        }

        IEnumerator ClampAndStop()
        {
            Assert.IsTrue(Click(DockAction.Clamps), dock.LastMessage);
            yield return WaitFor(() => dock.State == DockState.SeatedOpen, 5f, "落座");
            Assert.IsTrue(Click(DockAction.Clamps), dock.LastMessage);
            yield return WaitFor(() => dock.State == DockState.Clamped, 3f, "夹紧");
            yield return null;
            Assert.AreEqual(RobotRepairStage.PowerOff, wo.Stage, "夹紧后工单同步为已停靠");
            Assert.IsTrue(Click(DockAction.PowerSwitch), dock.LastMessage);
            yield return null;
            Assert.IsFalse(wo.Inspect(wo.Plan.InspectionAnchors[0]), "叶轮减速中不能检查");
            Assert.IsFalse(Click(DockAction.PowerSwitch), "减速中恢复供电：工单不放行");
            yield return WaitFor(() => dock.State == DockState.RotorsStopped, dock.Rotors.SpinDownSeconds + 2f, "停转");
            yield return null;
        }

        void RepairOnce(RobotRepairChoice choice)
        {
            foreach (var step in wo.Plan.RemovalSteps)
            {
                Assert.IsFalse(Click(DockAction.PowerSwitch), "拆卸中恢复供电");
                Assert.IsTrue(wo.Remove(step.Id), wo.LastFeedback);
            }
            Assert.IsTrue(wo.ChooseRepair(choice), wo.LastFeedback);
            foreach (var step in wo.Plan.RemovalSteps.Reverse()) Assert.IsTrue(wo.Install(step.Id), wo.LastFeedback);
            Assert.IsFalse(Click(DockAction.PowerSwitch), "通电前检查前恢复供电");
            StringAssert.Contains("通电前检查", dock.LastMessage);
            Assert.IsTrue(wo.RunPrePowerCheck(), wo.LastFeedback);
            Assert.IsFalse(dock.PowerOn, "工单不自己通电");
        }

        IEnumerator RestoreAndUndock()
        {
            Assert.IsTrue(Click(DockAction.PowerSwitch), dock.LastMessage);
            Assert.AreEqual(DockState.SpinningUp, dock.State);
            yield return null;
            Assert.AreEqual(RobotRepairStage.HoverRetest, wo.Stage);
            Assert.IsFalse(wo.HoverRetest(true), "夹在维修座上不能复测");
            yield return WaitFor(() => dock.State == DockState.Clamped, dock.Rotors.SpinUpSeconds + 2f, "转子回到悬停转速");
            Assert.IsTrue(Click(DockAction.Clamps), dock.LastMessage);
            yield return WaitFor(() => dock.State == DockState.SeatedOpen, 3f, "夹具张开");
            Assert.IsFalse(wo.HoverRetest(true), "落座未离座不能复测");
            Assert.IsTrue(dock.Interact(DockAction.LiftOff), dock.LastMessage);   // 与 L 键相同的入口
            yield return WaitFor(() => dock.State == DockState.Undocked, 3f, "离座");
            yield return Seconds(0.4f);
            Assert.IsFalse(dock.Rotors.Driven, "离座后转子交还 Animator");
            Assert.IsTrue(dock.RobotAnimator.GetCurrentAnimatorStateInfo(0).IsName(dock.HoverStateName));
        }

        [UnityTest]
        public IEnumerator SceneWiring_WorkOrderIsTheOnlyGate_NoManualConfirm()
        {
            Assert.AreSame(bridge, dock.ServiceGate as Unit07WorkOrder06Bridge, "维修座的“允许结束维修”应是工单");
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<ManualServiceCompletionGate>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                            "集成场景不应有占位的手动确认（F）");
            var gates = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).OfType<IDockServiceCompletionGate>().ToArray();
            Assert.AreEqual(1, gates.Length, "场景里只能有一个“允许结束维修”实现");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<Unit07DockController>(FindObjectsSortMode.None).Length, "只能有一个控制器决定供电");
            CollectionAssert.IsEmpty(bridge.MissingModelAnchors(), "工单用到的模型节点都应在七号上");
            Assert.AreEqual("case_06_left_engine_imbalance", wo.CaseId);
            Assert.AreEqual("UNIT07", Unit07WorkOrder06Session.RobotId);
            // 面板区域的点击不打射线（维修座部件不在面板下，由 Click() 逐次检查）
            Assert.IsTrue(bridge.BlocksClick(new Vector2(Screen.width - 30f, Screen.height - 30f)));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FullRoundTrip_FailedHoverRetest_Redock_SecondCycle_Pass()
        {
            Assert.AreEqual(RobotRepairStage.Dock, wo.Stage);
            Assert.IsFalse(wo.Inspect(wo.Plan.InspectionAnchors[0]), "悬停时不能检查");
            yield return ClampAndStop();
            Assert.AreEqual(RobotRepairStage.Inspect, wo.Stage, "停转后工单进入检查");
            Assert.IsTrue(dock.LeftEngineInspectable);
            foreach (var a in wo.Plan.InspectionAnchors)
            {
                Assert.IsFalse(Click(DockAction.PowerSwitch), "检查中恢复供电");
                Assert.IsTrue(wo.Inspect(a), wo.LastFeedback);
            }
            RepairOnce(RobotRepairChoice.RebalanceRotor);
            yield return RestoreAndUndock();

            // 悬停复测失败 → 点夹具握把重新落座 → 夹紧 → 断电 → 第二轮
            Assert.IsTrue(wo.HoverRetest(false), wo.LastFeedback);
            Assert.AreEqual(RobotRepairStage.Dock, wo.Stage);
            Assert.AreEqual(1, wo.RepairCycle);
            Assert.IsTrue(Click(DockAction.Clamps), "复测失败后重新落座：" + dock.LastMessage);
            yield return WaitFor(() => dock.State == DockState.SeatedOpen, 5f, "重新落座");
            Assert.IsTrue(dock.Rotors.Driven, "重新落座后转子由维修座接管");
            Assert.IsTrue(Click(DockAction.Clamps), dock.LastMessage);
            yield return WaitFor(() => dock.State == DockState.Clamped, 3f, "重新夹紧");
            Assert.IsTrue(Click(DockAction.PowerSwitch), dock.LastMessage);
            yield return WaitFor(() => dock.State == DockState.RotorsStopped, dock.Rotors.SpinDownSeconds + 2f, "再次停转");
            yield return null;
            Assert.AreEqual(RobotRepairStage.Disassemble, wo.Stage, "第二轮直接重新拆卸");
            RepairOnce(RobotRepairChoice.ReplaceMotorCore);
            yield return RestoreAndUndock();
            Assert.IsTrue(wo.HoverRetest(true), wo.LastFeedback);
            Assert.IsTrue(wo.LoadRetest(true), wo.LastFeedback);
            Assert.AreEqual(RobotRepairStage.Complete, wo.Stage);
            Assert.IsNull(wo.SafetyFault);

            // 离座后转子由 Idle_Hover 以悬停转速驱动：按约 1/30 s 窗口累计角度
            var rotor = dock.Rotors.Rotors[0];
            float total = 0f, time = 0f, window = 0f;
            var prev = rotor.localRotation;
            while (time < 0.5f)
            {
                yield return null;
                window += Time.deltaTime;
                if (window < 1f / 30f) continue;
                total += Quaternion.Angle(prev, rotor.localRotation);
                prev = rotor.localRotation;
                time += window; window = 0f;
            }
            Assert.AreEqual(dock.Rotors.IdleSpeedDegPerSec, total / time, dock.Rotors.IdleSpeedDegPerSec * 0.15f, "离座后转子转速");
            Assert.IsTrue(dock.PowerOn);
            Assert.AreEqual(0f, dock.LeverOffFraction, 1e-4f, "手柄 ON");

            // 工单记录：两轮、全部带机器人 ID 和工单 ID，期间没有拒绝过的拆装记入工单
            Assert.IsTrue(wo.Log.All(e => e.RobotId == "UNIT07" && e.CaseId == "case_06_left_engine_imbalance"));
            Assert.AreEqual(2 * 13, wo.Inner.Events.Count(e => e.Action == RobotRepairAction.Remove));
            Assert.AreEqual(2 * 13, wo.Inner.Events.Count(e => e.Action == RobotRepairAction.Install));
            Assert.AreEqual(2, wo.Inner.Events.Count(e => e.Action == RobotRepairAction.PowerOn));
        }

        /// <summary>模拟接线错误：总是放行的接口。</summary>
        sealed class BypassGate : IDockServiceCompletionGate
        {
            public bool CanFinishService(out string reason) { reason = string.Empty; return true; }
        }

        /// <summary>
        /// 回归：拆卸中异常通电（维修座被临时接到总是放行的接口）→ 桥接每帧自己发现故障 → 接回工单、点击断电、叶轮停稳 →
        /// 工单操作和恢复供电全部被拒绝 → 维修人员复位 → 继续完成。
        /// </summary>
        [UnityTest]
        public IEnumerator AbnormalPowerOn_ThenPowerOff_LockedUntilTechnicianReset()
        {
            yield return ClampAndStop();
            foreach (var a in wo.Plan.InspectionAnchors) Assert.IsTrue(wo.Inspect(a), wo.LastFeedback);
            for (int i = 0; i < 3; i++) Assert.IsTrue(wo.Remove(wo.Plan.RemovalSteps[i].Id), wo.LastFeedback);

            dock.SetServiceCompletionGate(new BypassGate());
            Assert.IsTrue(Click(DockAction.PowerSwitch), "接线错误时维修座会通电：" + dock.LastMessage);
            yield return null;
            yield return null;
            Assert.IsTrue(wo.IsLocked, "桥接每帧同步，应自行发现异常通电");
            dock.SetServiceCompletionGate(bridge);

            yield return WaitFor(() => dock.State == DockState.Clamped, dock.Rotors.SpinUpSeconds + 2f, "加速完成");
            Assert.IsTrue(Click(DockAction.PowerSwitch), "断电：" + dock.LastMessage);
            yield return WaitFor(() => dock.State == DockState.RotorsStopped, dock.Rotors.SpinDownSeconds + 2f, "再次停转");
            yield return null;
            Assert.IsTrue(wo.IsLocked, "再断电不会自动解锁");
            Assert.AreEqual(RobotRepairStage.Disassemble, wo.Stage);
            int events = wo.Inner.Events.Count;
            Assert.IsFalse(wo.Remove("4"));
            Assert.IsFalse(wo.Inspect(wo.Plan.InspectionAnchors[0]));
            Assert.IsFalse(wo.RunPrePowerCheck());
            Assert.IsFalse(wo.HoverRetest(true));
            StringAssert.Contains("安全故障锁定", wo.LastFeedback);
            Assert.AreEqual(events, wo.Inner.Events.Count);
            Assert.IsFalse(Click(DockAction.PowerSwitch), "故障锁定时恢复供电");
            StringAssert.Contains("安全故障锁定", dock.LastMessage);
            StringAssert.Contains("复位", wo.NextHint());

            Assert.IsFalse(wo.ResetSafetyFault(""), "没有工号不能复位");
            Assert.IsTrue(wo.ResetSafetyFault("T-0601"), wo.LastFeedback);
            Assert.AreEqual("4", wo.Inner.NextRemoval.Id);
            foreach (var step in wo.Plan.RemovalSteps.Skip(3)) Assert.IsTrue(wo.Remove(step.Id), wo.LastFeedback);
            Assert.IsTrue(wo.ChooseRepair(RobotRepairChoice.RebalanceRotor));
            foreach (var step in wo.Plan.RemovalSteps.Reverse()) Assert.IsTrue(wo.Install(step.Id), wo.LastFeedback);
            Assert.IsTrue(wo.RunPrePowerCheck(), wo.LastFeedback);
            yield return RestoreAndUndock();
            Assert.IsTrue(wo.HoverRetest(true));
            Assert.IsTrue(wo.LoadRetest(true));
            Assert.AreEqual(RobotRepairStage.Complete, wo.Stage);
            Assert.AreEqual(1, wo.FaultCount);
            Assert.AreEqual("T-0601", wo.LastResetBy);
        }
    }
}
