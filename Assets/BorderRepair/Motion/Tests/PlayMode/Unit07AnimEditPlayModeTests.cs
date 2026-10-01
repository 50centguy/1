using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BorderRepair.Dock;
using BorderRepair.FirstOrder;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.Motion.Tests
{
    /// <summary>
    /// “Unity 内手动编辑动画”工作流（Play 模式，首单测试场景）：
    /// 转子骨骼任何时刻只有一个写入者；默认配置下首单 32 步全部通过；离座的运行结果与配置曲线一致。控制台出现错误时测试自动失败。
    /// </summary>
    public class Unit07AnimEditPlayModeTests
    {
        const string ScenePath = "Assets/BorderRepair/FirstOrder/Scenes/Unit07FirstOrder_Test.unity";
        const string EditableController = "Assets/BorderRepair/Animation/Unit07/Controllers/UNIT07_RobotV4_Editable.controller";
        const string OriginalController = "Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_Dock.controller";
        Unit07DockController dock;
        FirstOrderFlow flow;

        [UnitySetUp]
        public IEnumerator Load()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("Unit07FirstOrder_Test");
#endif
            yield return null;
            dock = UnityEngine.Object.FindFirstObjectByType<Unit07DockController>();
            flow = UnityEngine.Object.FindFirstObjectByType<FirstOrderFlow>();
            Assert.IsNotNull(dock);
            Assert.IsTrue(dock.ConfigurationValid);
        }

        static IEnumerator WaitFor(Func<bool> cond, float timeout, string what)
        {
            float t = 0f;
            while (!cond())
            {
                t += Time.deltaTime;
                if (t > timeout) Assert.Fail("等待超时：" + what);
                yield return null;
            }
        }

        IEnumerator Seat()
        {
            Assert.IsTrue(dock.Interact(DockAction.Clamps), dock.LastMessage);
            yield return WaitFor(() => dock.State == DockState.SeatedOpen, 5f, "落座");
            for (int i = 0; i < 3; i++) yield return null;
        }

        /// <summary>Animator 是否在写转子：关掉 RotorPowerDriver，把转子设成一个特殊姿态，过几帧看有没有被改写。返回最大偏离角度。</summary>
        IEnumerator AnimatorWritesRotor(Action<float> result)
        {
            var driver = dock.Rotors;
            var rotor = driver.Rotors[0];
            driver.enabled = false;
            var sentinel = Quaternion.Euler(12f, 34f, 56f);
            rotor.localRotation = sentinel;
            float maxDev = 0f;
            // 至少 6 帧且至少 0.1 s：批处理模式一帧只有约 0.3 ms，只数帧数时转动太小、测不出来
            float waited = 0f;
            for (int i = 0; i < 6 || waited < 0.1f; i++)
            {
                yield return null;
                waited += Time.deltaTime;
                maxDev = Mathf.Max(maxDev, Quaternion.Angle(sentinel, rotor.localRotation));
            }
            driver.enabled = true;
            result(maxDev);
        }

        /// <summary>脚本是否在写转子：关掉 Animator（RotorPowerDriver 照常运行），过几帧看转子有没有变化。返回最大变化角度。</summary>
        IEnumerator ScriptWritesRotor(Action<float> result)
        {
            var anim = dock.RobotAnimator;
            var rotor = dock.Rotors.Rotors[0];
            anim.enabled = false;
            var q = rotor.localRotation;
            float maxDev = 0f;
            // 至少 6 帧且至少 0.1 s：批处理模式一帧只有约 0.3 ms，只数帧数时转动太小、测不出来
            float waited = 0f;
            for (int i = 0; i < 6 || waited < 0.1f; i++)
            {
                yield return null;
                waited += Time.deltaTime;
                maxDev = Mathf.Max(maxDev, Quaternion.Angle(q, rotor.localRotation));
            }
            anim.enabled = true;
            result(maxDev);
        }

        /// <summary>按约 1/30 s 的窗口累计转角，换算成转速（逐帧累计会因 Quaternion.Angle 的判等阈值算成 0）。</summary>
        static IEnumerator MeasureSpeed(Transform rotor, float seconds, Action<float> result)
        {
            float total = 0f, time = 0f, window = 0f;
            var prev = rotor.localRotation;
            while (time < seconds)
            {
                yield return null;
                window += Time.deltaTime;
                if (window < 1f / 30f) continue;
                total += Quaternion.Angle(prev, rotor.localRotation);
                prev = rotor.localRotation;
                time += window;
                window = 0f;
            }
            result(total / time);
        }

        [UnityTest]
        public IEnumerator RotorBones_OnlyAnimatorWrites_HoveringAndSeated()
        {
#if UNITY_EDITOR
            Assert.AreEqual(EditableController, AssetDatabase.GetAssetPath(dock.RobotAnimator.runtimeAnimatorController), "首单测试场景用可编辑控制器");
#endif
            var anim = dock.RobotAnimator;
            var drv = dock.Rotors;
            int layer = anim.GetLayerIndex(drv.RotorLayerName);
            Assert.GreaterOrEqual(layer, 0);
            // 悬停：Rotors 层播 Rotor_Spin（Idle_Hover），转子由 Animator 转动，脚本不写
            Assert.IsTrue(anim.GetCurrentAnimatorStateInfo(layer).IsName(drv.RotorSpinState));
            float speed = 0f, s1 = 0f, a = 0f;
            yield return MeasureSpeed(drv.Rotors[0], 0.3f, v => speed = v);
            Assert.AreEqual(360f, speed, 360f * 0.15f, "悬停：Animator 按 Idle_Hover 转动转子");
            yield return ScriptWritesRotor(v => s1 = v);
            Assert.Less(s1, 0.001f, "悬停：脚本不写转子");

            // 落座：接管处逐帧检查转子不跳变（两根都查）
            Assert.IsTrue(dock.Interact(DockAction.Clamps), dock.LastMessage);
            var r0 = drv.Rotors[0]; var r1 = drv.Rotors[1];
            var p0 = r0.localRotation; var p1 = r1.localRotation;
            float maxExcess = 0f, tt = 0f;
            int after = 0;
            while (after < 10)
            {
                yield return null;
                tt += Time.deltaTime;
                if (tt > 5f) Assert.Fail("落座超时");
                float expected = 360f * Time.deltaTime;
                maxExcess = Mathf.Max(maxExcess, Quaternion.Angle(p0, r0.localRotation) - expected, Quaternion.Angle(p1, r1.localRotation) - expected);
                p0 = r0.localRotation; p1 = r1.localRotation;
                if (drv.Driven) after++;
            }
            Assert.Less(maxExcess, 2f, "接管前后转子没有跳变（每帧转角不超过悬停转速对应的角度 + 2°）");
            Assert.IsTrue(drv.DrivesThroughAnimator, "可编辑控制器：接管后经 Animator 的 Rotor_Angle 写转子");
            Assert.IsTrue(anim.GetCurrentAnimatorStateInfo(layer).IsName(drv.RotorAngleState));

            // 落座、通电：只有 Animator 写转子；转速仍是悬停转速；骨骼姿态 = 静止姿态 + 脚本算出的转角
            yield return ScriptWritesRotor(v => s1 = v);
            Assert.Less(s1, 0.001f, "接管期间脚本不直接写转子骨骼");
            yield return AnimatorWritesRotor(v => a = v);
            Assert.Greater(a, 1f, "接管期间转子由 Animator 写（唯一写入者）");
            yield return null; yield return null;
            yield return MeasureSpeed(r0, 0.3f, v => speed = v);
            Assert.AreEqual(360f, speed, 360f * 0.15f, "接管后通电：仍是悬停转速");
            float d = Mathf.Abs(Mathf.DeltaAngle(drv.AngleFromRest(0), drv.AngleDeg));
            // 测试协程在脚本 Update 之后、Animator 求值之前读骨骼：转动中允许差一帧的转角
            Assert.Less(d, 360f * Time.deltaTime + 0.01f, "转子姿态 = 静止姿态绕转轴转过脚本算出的角度（差一帧以内）");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(drv.AngleFromRest(1), drv.AngleDeg)), 360f * Time.deltaTime + 0.01f, "右转子同样");

            // 断电减速：仍由 Animator 写，转速按配置下降，最后停住
            Assert.IsTrue(dock.Interact(DockAction.Clamps));
            yield return WaitFor(() => dock.State == DockState.Clamped, 3f, "夹紧");
            Assert.IsTrue(dock.Interact(DockAction.PowerSwitch));
            yield return WaitFor(() => dock.State == DockState.RotorsStopped, drv.SpinDownSeconds + 2f, "停转");
            var still = r0.localRotation;
            for (int i = 0; i < 10; i++) yield return null;
            Assert.Less(Quaternion.Angle(still, r0.localRotation), 0.001f, "停转后转子静止");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(drv.AngleFromRest(0), drv.AngleDeg)), 0.01f, "停转后：转子姿态 = 静止姿态 + 脚本转角（精确）");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(drv.AngleFromRest(1), drv.AngleDeg)), 0.01f, "右转子同样");
            // 身体其它骨骼仍由 Animator 驱动：断电后播放夹爪动作照常
            anim.Play("Gripper_OpenClose_R", 0, 0f);
            var jaw = dock.RobotRoot.GetComponentsInChildren<Transform>().First(x => x.name == "Arm_R_JawUpper" && x.childCount > 0);
            var j0 = jaw.localRotation;
            float t = 0f;
            while (t < 0.4f) { t += Time.deltaTime; yield return null; }
            Assert.Greater(Quaternion.Angle(j0, jaw.localRotation), 1f, "夹爪动作照常播放");
            Assert.Less(Quaternion.Angle(still, r0.localRotation), 0.001f, "播放夹爪动作时转子仍静止");
        }

#if UNITY_EDITOR
        /// <summary>对照：换回原控制器（没有 Rotors 层），同样的两项检查能发现脚本和 Animator 都在写转子骨骼——说明上一个测试的检查是有效的。</summary>
        [UnityTest]
        public IEnumerator Control_OriginalController_HasTwoWriters()
        {
            dock.RobotAnimator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(OriginalController);
            dock.RobotAnimator.Play("Idle_Hover", 0, 0f);
            yield return Seat();
            Assert.IsTrue(dock.Rotors.Driven);
            Assert.IsFalse(dock.Rotors.DrivesThroughAnimator, "原控制器：保持原来的直接写骨骼");
            float s = -1f, a = -1f;
            yield return ScriptWritesRotor(v => s = v);
            yield return AnimatorWritesRotor(v => a = v);
            Assert.Greater(s, 1f, "原控制器下脚本写转子");
            Assert.Greater(a, 1f, "原控制器下 Animator 也写转子（两个写入者）");
        }
#endif

        [UnityTest]
        public IEnumerator FirstOrder_32Steps_PassWithEditableAssets()
        {
#if UNITY_EDITOR
            Assert.AreEqual("Assets/BorderRepair/Animation/Unit07/Motion/UNIT07_FirstOrderMotion.asset", AssetDatabase.GetAssetPath(flow.Motion));
            Assert.AreEqual("Assets/BorderRepair/Animation/Unit07/Motion/UNIT07_DockMotion.asset", AssetDatabase.GetAssetPath(dock.Motion));
            Assert.AreEqual("Assets/BorderRepair/Animation/Unit07/Motion/UNIT07_RotorMotion.asset", AssetDatabase.GetAssetPath(dock.Rotors.Motion));
#endif
            var input = UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>();
            var driver = new FirstOrderAcceptanceDriver(flow, input) { Timeout = 30f };
            yield return driver.RunFullOrder();
            var failed = driver.Records.Where(r => !r.pass).Select(r => $"#{r.index} {r.label}：{r.message}").ToList();
            Assert.IsEmpty(failed, string.Join("\n", failed));
            Assert.AreEqual(32, driver.Records.Count, "首单 32 步");
            Assert.AreEqual(FoStep.Done, flow.Step);
            Assert.IsTrue(flow.RetestPassed, flow.RetestDetail);
            Assert.IsFalse(dock.Rotors.Driven, "离座后转子交还 Animator");
            Assert.IsTrue(dock.RobotAnimator.GetCurrentAnimatorStateInfo(dock.RobotAnimator.GetLayerIndex("Rotors")).IsName("Rotor_Spin"));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>改离座配置（时长、曲线、Animator 过渡），运行时的上浮高度逐帧符合曲线，过渡时长符合配置。</summary>
        [UnityTest]
        public IEnumerator LiftOff_FollowsEditedCurve_AtRuntime()
        {
            var cfg = ScriptableObject.CreateInstance<FirstOrderMotionConfig>();
            cfg.liftOff = new MotionTiming(0.8f, new AnimationCurve(new Keyframe(0f, 0f, 3f, 3f), new Keyframe(0.5f, 0.85f, 0.6f, 0.6f), new Keyframe(1f, 1f, 0f, 0f)));
            cfg.liftBlendSeconds = 0.35f;
            flow.SetMotionConfig(cfg);
            // 落座、夹紧，再松开夹具（七号仍在接触垫上），然后启动原型离座动作
            yield return Seat();
            Assert.IsTrue(dock.Interact(DockAction.Clamps));
            yield return WaitFor(() => dock.State == DockState.Clamped, 3f, "夹紧");
            Assert.IsTrue(dock.Interact(DockAction.Clamps));
            yield return WaitFor(() => dock.State == DockState.SeatedOpen, 3f, "松开");
            var lift = typeof(FirstOrderFlow).GetMethod("LiftOff", BindingFlags.Instance | BindingFlags.NonPublic);
            float hover = flow.HoverHeight, y0 = dock.RobotAnchor.position.y;
            float start = Time.time;
            flow.StartCoroutine((IEnumerator)lift.Invoke(flow, null));
            yield return null;
            var anim = dock.RobotAnimator;
            Assert.IsTrue(anim.IsInTransition(0), "离座时 Animator 过渡回 Idle_Hover");
            Assert.AreEqual(0.35f, anim.GetAnimatorTransitionInfo(0).duration, 1e-3f, "过渡时长按配置");
            int checkedFrames = 0;
            while (Time.time - start < 0.8f + 0.1f)
            {
                float e = Time.time - start, h = dock.RobotRoot.position.y - y0;
                // 协程与测试在同一帧的先后不确定：允许两帧的误差
                float lo = hover * cfg.liftOff.Evaluate((e - 2f * Time.deltaTime) / 0.8f) - 1e-5f;
                float hi = hover * cfg.liftOff.Evaluate((e + 2f * Time.deltaTime) / 0.8f) + 1e-5f;
                Assert.That(h, Is.InRange(lo, hi), $"t={e:F3}s 高度 {h:F4} 应在曲线 [{lo:F4}, {hi:F4}] 内");
                checkedFrames++;
                yield return null;
            }
            Assert.AreEqual(hover, dock.RobotRoot.position.y - y0, 1e-5f, "离座结束在悬停高度");
            Assert.Greater(checkedFrames, 10);
            UnityEngine.Object.Destroy(cfg);
        }
    }
}
