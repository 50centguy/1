using System;
using System.Collections;
using System.Linq;
using BorderRepair.Dock;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.Tests
{
    /// <summary>UNIT 07 维修座第一阶段停靠流程（Play 模式）。控制台出现错误时测试自动失败。</summary>
    public class Unit07DockPlayModeTests
    {
        const string ScenePath = "Assets/BorderRepair/Scenes/Unit07Dock_Test.unity";
        Unit07DockController dock;
        Unit07DockInput input;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("Unit07Dock_Test");
#endif
            yield return null;
            dock = UnityEngine.Object.FindFirstObjectByType<Unit07DockController>();
            input = UnityEngine.Object.FindFirstObjectByType<Unit07DockInput>();
            Assert.IsNotNull(dock);
            Assert.IsTrue(dock.ConfigurationValid, "维修座配置无效");
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

        Transform Find(string name) => UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(t => t.name == name);
        Renderer Rend(string name) => UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(r => r.name == name);

        float RotorDelta(Transform rotor, Quaternion before) => Quaternion.Angle(before, rotor.localRotation);

        IEnumerator DockAndClamp()
        {
            Assert.IsTrue(dock.Interact(DockAction.Clamps), dock.LastMessage);
            yield return WaitFor(() => dock.State == DockState.SeatedOpen, 5f, "落座");
            yield return Seconds(0.3f);
            Assert.IsTrue(dock.Interact(DockAction.Clamps), dock.LastMessage);
            yield return WaitFor(() => dock.State == DockState.Clamped, 3f, "夹紧");
        }

        [UnityTest]
        public IEnumerator DockingFlowPowerAndRotorsStayConsistent()
        {
            var rotorL = dock.Rotors.Rotors[0];
            var padL = Rend("Dock_Clamp_L_JawPad");
            float closedPadX = Mathf.Abs(padL.bounds.center.x);

            // 悬停：通电、转子由 Idle_Hover 驱动旋转
            Assert.AreEqual(DockState.Hovering, dock.State);
            Assert.AreEqual(0.84f, dock.RobotRoot.position.y, 1e-3f);
            var q = rotorL.localRotation;
            yield return Seconds(0.1f);
            Assert.Greater(RotorDelta(rotorL, q), 5f, "悬停时转子应在转动");

            // 顺序不对的操作被拒绝
            Assert.IsFalse(dock.SetPower(false), "未落座时不应允许断电");
            Assert.IsFalse(dock.InspectLeftEngine(), "通电时不应允许检查左引擎");

            // 张开 → 落座
            Assert.IsTrue(dock.Interact(DockAction.Clamps));
            yield return WaitFor(() => dock.State == DockState.SeatedOpen, 5f, "落座");
            Assert.AreEqual(1f, dock.ClampOpenFraction, 1e-4f);
            Assert.Greater(Mathf.Abs(padL.bounds.center.x), closedPadX + 0.01f, "按 unity_open_deg 张开后夹面应离开中线");
            Assert.Less(Vector3.Distance(dock.RobotRoot.position, dock.RobotAnchor.position), 1e-4f, "七号根应落在 Dock_RobotAnchor");
            yield return Seconds(0.3f);   // 等切到静止姿态
            foreach (var s in new[] { "L", "R" })
            {
                var pad = Find($"Dock_ContactPad_{s}").GetComponent<Collider>().bounds;
                var hp = Rend($"Chassis_ArmHardpoint_{s}").bounds;
                Assert.AreEqual(hp.min.y, pad.max.y, 0.001f, $"落座后接触垫 {s} 应贴住硬点板");
                Assert.That(pad.center.x > hp.min.x && pad.center.x < hp.max.x, $"接触垫 {s} 不在同侧硬点板下");
            }
            Assert.IsTrue(dock.Rotors.Driven);
            q = rotorL.localRotation;
            yield return Seconds(0.1f);
            Assert.Greater(RotorDelta(rotorL, q), 5f, "落座后仍通电，转子应继续转动");

            // 夹紧
            Assert.IsTrue(dock.Interact(DockAction.Clamps));
            yield return WaitFor(() => dock.State == DockState.Clamped, 3f, "夹紧");
            Assert.AreEqual(closedPadX, Mathf.Abs(padL.bounds.center.x), 1e-4f);
            Assert.IsFalse(dock.InspectLeftEngine());
            Assert.IsTrue(dock.PowerOn);
            AssertLamp(true);

            // 断电：手柄 OFF、灯变色、转子减速；减速期间叶轮禁止操作，夹具锁住
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch), dock.LastMessage);
            Assert.AreEqual(DockState.SpinningDown, dock.State);
            Assert.IsFalse(dock.PowerOn);
            AssertLamp(false);
            Assert.IsFalse(dock.CanOperateImpeller);
            Assert.IsFalse(dock.InspectLeftEngine(), "减速期间不应允许操作叶轮");
            Assert.IsFalse(dock.Interact(DockAction.Clamps), "断电维修中夹具应保持锁紧");
            yield return Seconds(0.5f);
            Assert.AreEqual(1f, dock.LeverOffFraction, 1e-4f, "手柄应到 OFF 位置");
            float midSpeed = dock.Rotors.SpeedDegPerSec;
            Assert.That(midSpeed > 0f && midSpeed < dock.Rotors.IdleSpeedDegPerSec, $"断电 0.5 s 后转子应在减速：{midSpeed}");
            yield return WaitFor(() => dock.State == DockState.RotorsStopped, dock.Rotors.SpinDownSeconds + 2f, "转子停转");
            q = rotorL.localRotation;
            yield return Seconds(0.5f);
            Assert.Less(RotorDelta(rotorL, q), 0.01f, "断电停转后转子应静止");
            Assert.IsTrue(dock.CanOperateImpeller);
            Assert.IsTrue(dock.InspectLeftEngine(), dock.LastMessage);

            // 恢复供电：手柄 ON、灯变回、转子重新加速，叶轮再次禁止操作
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch));
            AssertLamp(true);
            Assert.AreEqual(DockState.Clamped, dock.State);
            Assert.IsFalse(dock.InspectLeftEngine());
            yield return Seconds(1.2f);
            Assert.AreEqual(dock.Rotors.IdleSpeedDegPerSec, dock.Rotors.SpeedDegPerSec, 1f);
            Assert.AreEqual(0f, dock.LeverOffFraction, 1e-4f, "手柄应回到 ON 位置");
        }

        void AssertLamp(bool on)
        {
            var mpb = new MaterialPropertyBlock();
            Rend("Dock_PowerSwitch_Lamp").GetPropertyBlock(mpb);
            var c = mpb.GetColor("_BaseColor");
            var expected = dock.LampColor;
            Assert.Less(Vector4.Distance(c, expected), 1e-3f, $"状态灯颜色与供电状态不一致（{(on ? "ON" : "OFF")}）");
        }

        [UnityTest]
        public IEnumerator PowerOffDoesNotBreakGripperAnimation()
        {
            yield return DockAndClamp();
            Assert.IsTrue(dock.SetPower(false));
            yield return WaitFor(() => dock.State == DockState.RotorsStopped, dock.Rotors.SpinDownSeconds + 2f, "转子停转");
            var jaw = Find("Arm_R_JawUpper");
            var rotorL = dock.Rotors.Rotors[0];
            var q0 = jaw.localRotation;
            var r0 = rotorL.localRotation;
            dock.RobotAnimator.Play("Gripper_OpenClose_R", 0, 0f);
            yield return Seconds(0.8f);   // 第 24 帧附近：全开 36°
            Assert.Greater(Quaternion.Angle(q0, jaw.localRotation), 25f, "断电后夹爪动作应照常播放");
            Assert.Less(Quaternion.Angle(r0, rotorL.localRotation), 0.01f, "播放夹爪动作时转子应保持静止");
            var hp = Rend("Chassis_ArmHardpoint_L").bounds;
            var pad = Find("Dock_ContactPad_L").GetComponent<Collider>().bounds;
            Assert.AreEqual(hp.min.y, pad.max.y, 0.001f, "播放夹爪动作时机身应保持落座");
        }

        [UnityTest]
        public IEnumerator ProxiesAreClickableFromTheCamera()
        {
            yield return DockAndClamp();
            var cam = input.ViewCamera;
            var targets = UnityEngine.Object.FindObjectsByType<DockInteractable>(FindObjectsSortMode.None)
                .Where(d => d.action != DockAction.ContactPad && !d.name.EndsWith("JawPad")).ToArray();
            Assert.GreaterOrEqual(targets.Length, 7);
            foreach (var t in targets)
            {
                var col = t.GetComponent<Collider>();
                var sp = cam.WorldToScreenPoint(col.bounds.center);
                Assert.Greater(sp.z, 0f, $"{t.name} 在镜头后面");
                var hit = input.Pick(sp);
                Assert.AreSame(t, hit, $"从镜头点击 {t.name} 时命中了 {(hit != null ? hit.name : "空")}");
            }
            // 通过点击路径断电（与按钮 API 走同一条规则）
            var lever = targets.First(t => t.name == "Dock_PowerSwitch_LeverGrip");
            Assert.IsTrue(input.TryClick(cam.WorldToScreenPoint(lever.GetComponent<Collider>().bounds.center), out _), dock.LastMessage);
            Assert.IsFalse(dock.PowerOn);
            // 零件盘可以取下、放回
            var tray = targets.First(t => t.action == DockAction.PartsTray);
            var home = tray.transform.position;
            Assert.IsTrue(dock.Interact(DockAction.PartsTray, tray.pickable));
            Assert.IsTrue(tray.pickable.Taken);
            Assert.IsTrue(dock.Interact(DockAction.PartsTray, tray.pickable));
            Assert.Less(Vector3.Distance(home, tray.transform.position), 1e-5f);
        }

        [UnityTest]
        public IEnumerator RemovalCorridorsHaveNoDockColliders()
        {
            Assert.IsTrue(dock.Interact(DockAction.Clamps));
            yield return WaitFor(() => dock.State == DockState.SeatedOpen, 5f, "落座");
            foreach (var clampsOpen in new[] { true, false })
            {
                if (!clampsOpen)
                {
                    Assert.IsTrue(dock.Interact(DockAction.Clamps));
                    yield return WaitFor(() => dock.State == DockState.Clamped, 3f, "夹紧");
                }
                Physics.SyncTransforms();
                var dockCols = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None)
                    .Where(c => c.transform.root != dock.RobotRoot.root).ToArray();   // 左引擎检查代理挂在七号身上，随引擎移动，不算维修座
                Assert.IsNotEmpty(dockCols);
                foreach (var (name, box, minGap) in Unit07DockCorridors.All)
                {
                    var bad = dockCols.Where(c => Unit07DockCorridors.AabbDistance(c.bounds, box) < Mathf.Max(minGap, 1e-5f))
                        .Select(c => $"{c.name} ({Unit07DockCorridors.AabbDistance(c.bounds, box) * 1000:F1} mm)").ToArray();
                    Assert.IsEmpty(bad, $"通道 {name}（夹具{(clampsOpen ? "张开" : "夹紧")}）离维修座碰撞体不足 {minGap * 1000:F0} mm");
                }
            }
        }
    }

    /// <summary>拆装通道（Unity 世界坐标，七号落座时），与 ArtSource/Unit07ServiceDock 的检查一致。</summary>
    public static class Unit07DockCorridors
    {
        static Bounds R(Vector3 lo, Vector3 hi)
        {
            var b = new Bounds();
            b.SetMinMax(new Vector3(-hi.x, lo.z + 0.72f, -hi.y), new Vector3(-lo.x, hi.z + 0.72f, -lo.y));
            return b;
        }

        public static float AabbDistance(Bounds a, Bounds b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
            float dy = Mathf.Max(0f, Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y));
            float dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static readonly (string name, Bounds box, float minGap)[] All =
        {
            ("power_tray_drop", R(new Vector3(-0.126f, -0.103f, 0.05f), new Vector3(0.126f, 0.103f, 0.207f)), 0.015f),
            ("power_tray_out_front", R(new Vector3(-0.126f, -0.45f, 0.05f), new Vector3(0.126f, 0.103f, 0.10f)), 0.015f),
            ("engine_L_out", R(new Vector3(0.219f, -0.14f, 0.26f), new Vector3(0.92f, 0.12f, 0.575f)), 0f),
            ("engine_R_out", R(new Vector3(-0.92f, -0.14f, 0.26f), new Vector3(-0.219f, 0.12f, 0.575f)), 0f),
            ("rear_cassette_back", R(new Vector3(-0.085f, 0.13f, 0.39f), new Vector3(0.085f, 0.50f, 0.57f)), 0f),
        };
    }
}
