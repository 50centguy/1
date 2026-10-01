using System;
using System.Collections.Generic;
using System.Linq;
using BorderRepair.Dock;
using BorderRepair.FirstOrder;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorkbenchArea;
using Object = UnityEngine.Object;

namespace BorderRepair.Motion.EditorTools
{
    /// <summary>
    /// 不进入 Play 的动作预览：拖进度滑杆，在当前打开的测试场景里按配置资产的时长和曲线摆出该时刻的姿态。
    /// 用 Unity 的 AnimationMode（与 Animation 窗口预览相同的机制）临时驱动属性：预览中的姿态不会被保存到场景或预制体，
    /// 结束预览、关闭窗口、进入 Play、保存场景、切换场景、脚本重新编译时都会自动还原。姿态计算调用运行时组件自己的方法，预览和运行一致。
    /// </summary>
    [InitializeOnLoad]
    public static class Unit07MotionPreview
    {
        public enum Kind
        {
            DockClamps, DockDescend, DockLever, RotorSpinDown, RotorSpinUp, LiftOff, CoverLift, LatchOpen, WorkbenchTrayLift, RobotClip
        }

        public static readonly Dictionary<Kind, string> Labels = new Dictionary<Kind, string>
        {
            { Kind.DockClamps, "维修座 · 夹具张开（倒放 = 合拢）" },
            { Kind.DockDescend, "维修座 · 七号落座" },
            { Kind.DockLever, "维修座 · 断电开关手柄 ON → OFF" },
            { Kind.RotorSpinDown, "七号 · 转子断电减速" },
            { Kind.RotorSpinUp, "七号 · 转子通电加速" },
            { Kind.LiftOff, "首单 · 七号离座上浮" },
            { Kind.CoverLift, "首单 · 左上盖总成取下（第一段：沿引擎轴线抬起）" },
            { Kind.LatchOpen, "首单 · 外侧锁扣扳开" },
            { Kind.WorkbenchTrayLift, "工作台 · 螺钉托盘抬起（第一段）" },
            { Kind.RobotClip, "七号 · 骨骼动作片段（.anim）" },
        };

        static readonly string[] TransformProps =
        {
            "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
            "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
        };

        public static bool Active { get; private set; }
        public static Kind Current { get; private set; }
        public static AnimationClip Clip { get; private set; }
        public static string Info { get; private set; } = "";
        /// <summary>最近一次摆出的时间进度（0–1）。</summary>
        public static float Progress { get; private set; }

        static readonly List<(Transform t, Vector3 lp, Quaternion lr, string json)> snapshot = new List<(Transform, Vector3, Quaternion, string)>();
        static Action<float> sampler;
        static Func<float> seconds;
        static GameObject clipTarget;

        static Unit07MotionPreview()
        {
            EditorApplication.playModeStateChanged += _ => End();
            EditorSceneManager.sceneSaving += (_, __) => End();
            EditorSceneManager.sceneClosing += (_, __) => End();
            AssemblyReloadEvents.beforeAssemblyReload += End;
        }

        /// <summary>这个动作的时长（秒），按当前配置。</summary>
        public static float Seconds => seconds != null ? seconds() : 0f;

        /// <summary>开始预览：在当前场景里找到对应对象并记录原始姿态。找不到返回 false，原因见 Info。</summary>
        public static bool Begin(Kind kind, AnimationClip clip = null)
        {
            End();
            Current = kind;
            Clip = clip;
            var targets = new List<Transform>();
            if (!Prepare(kind, clip, targets)) return false;
            snapshot.Clear();
            foreach (var t in targets.Where(t => t != null).Distinct()) snapshot.Add((t, t.localPosition, t.localRotation, EditorJsonUtility.ToJson(t)));
            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            foreach (var (t, _, _, _) in snapshot)
                foreach (var prop in TransformProps)
                {
                    var mod = new PropertyModification { target = t, propertyPath = prop, value = Read(t, prop).ToString("R") };
                    AnimationMode.AddPropertyModification(EditorCurveBinding.FloatCurve("", typeof(Transform), prop), mod, true);
                }
            AnimationMode.EndSampling();
            Active = true;
            Sample(0f);
            return true;
        }

        static float Read(Transform t, string prop)
        {
            switch (prop)
            {
                case "m_LocalPosition.x": return t.localPosition.x;
                case "m_LocalPosition.y": return t.localPosition.y;
                case "m_LocalPosition.z": return t.localPosition.z;
                case "m_LocalRotation.x": return t.localRotation.x;
                case "m_LocalRotation.y": return t.localRotation.y;
                case "m_LocalRotation.z": return t.localRotation.z;
                default: return t.localRotation.w;
            }
        }

        /// <summary>摆出时间进度 progress（0–1）时的姿态。</summary>
        public static void Sample(float progress)
        {
            if (!Active) return;
            progress = Mathf.Clamp01(progress);
            Progress = progress;
            // 每次都从原始姿态出发，结果只取决于进度
            foreach (var (t, lp, lr, _) in snapshot) { t.localPosition = lp; t.localRotation = lr; }
            AnimationMode.BeginSampling();
            if (Current == Kind.RobotClip && Clip != null && clipTarget != null)
                AnimationMode.SampleAnimationClip(clipTarget, Clip, progress * Clip.length);
            else
                sampler?.Invoke(progress);
            AnimationMode.EndSampling();
            SceneView.RepaintAll();
        }

        /// <summary>结束预览并还原所有姿态（不会留下任何修改）。</summary>
        public static void End()
        {
            if (!Active && !AnimationMode.InAnimationMode()) return;
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            // AnimationMode 已经把驱动过的属性还原；再按序列化数据逐位核对，不一致的按原序列化数据写回
            // （用序列化写回而不是 transform.localRotation = …：后者会重新归一化四元数，末位可能和场景文件里的值不同）
            foreach (var (t, _, _, json) in snapshot)
            {
                if (t == null) continue;
                if (EditorJsonUtility.ToJson(t) != json) EditorJsonUtility.FromJsonOverwrite(json, t);
            }
            snapshot.Clear();
            sampler = null;
            seconds = null;
            clipTarget = null;
            Active = false;
            SceneView.RepaintAll();
        }

        static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        static bool Fail(string why) { Info = why; return false; }

        static bool Prepare(Kind kind, AnimationClip clip, List<Transform> targets)
        {
            Info = "";
            switch (kind)
            {
                case Kind.DockClamps:
                case Kind.DockDescend:
                case Kind.DockLever:
                {
                    var dock = Find<Unit07DockController>();
                    if (dock == null) return Fail("当前场景里没有维修座（Unit07DockController）。请打开首单或维修座测试场景。");
                    dock.Initialize();
                    if (!dock.ConfigurationValid) return Fail("维修座配置无效，见控制台。");
                    if (kind == Kind.DockClamps)
                    {
                        targets.Add(dock.ClampL); targets.Add(dock.ClampR);
                        sampler = p => dock.PoseClamps(p);
                        seconds = () => dock.Motion.clamps.Seconds;
                        Info = "配置：" + Describe(dock.Motion);
                    }
                    else if (kind == Kind.DockLever)
                    {
                        targets.Add(dock.PowerLever);
                        sampler = p => dock.PoseLever(p);
                        seconds = () => dock.Motion.lever.Seconds;
                        Info = "配置：" + Describe(dock.Motion);
                    }
                    else
                    {
                        targets.Add(dock.RobotRoot);
                        sampler = p => dock.PoseDescend(p);
                        seconds = () => dock.Motion.descend.Seconds;
                        Info = "配置：" + Describe(dock.Motion);
                    }
                    return true;
                }
                case Kind.RotorSpinDown:
                case Kind.RotorSpinUp:
                {
                    var driver = Find<RotorPowerDriver>();
                    if (driver == null) return Fail("当前场景里没有七号的 RotorPowerDriver。");
                    targets.AddRange(driver.Rotors);
                    bool up = kind == Kind.RotorSpinUp;
                    driver.CaptureBaseForPreview();
                    sampler = p =>
                    {
                        var m = up ? driver.Motion.spinUp : driver.Motion.spinDown;
                        float a = RotorPowerDriver.AngleAfter(m, up, driver.IdleSpeedDegPerSec, p * m.Seconds, out var frac);
                        driver.WriteRotors(Mathf.Repeat(a, 360f));
                        Info = $"配置：{Describe(driver.Motion)}；此刻转速 {driver.IdleSpeedDegPerSec * frac:F0}°/s，累计转过 {a:F0}°";
                    };
                    seconds = () => (up ? driver.Motion.spinUp : driver.Motion.spinDown).Seconds;
                    return true;
                }
                case Kind.LiftOff:
                case Kind.CoverLift:
                case Kind.LatchOpen:
                {
                    var flow = Find<FirstOrderFlow>();
                    if (flow == null) return Fail("当前场景里没有首单流程（FirstOrderFlow）。请打开首单测试场景。");
                    if (kind == Kind.LiftOff)
                    {
                        targets.Add(flow.Dock.RobotRoot);
                        sampler = p => flow.PoseLiftOff(p);
                        seconds = () => flow.Motion.liftOff.Seconds;
                    }
                    else if (kind == Kind.CoverLift)
                    {
                        var cover = flow.Cover;
                        targets.Add(cover.transform);
                        targets.AddRange(cover.members);
                        var up = flow.EngineLHinge.up * flow.CoverLift;
                        var starts = new[] { cover.transform }.Concat(cover.members).Select(t => (t, t.position)).ToList();
                        sampler = p =>
                        {
                            float k = flow.Motion.partMove.Evaluate(p);
                            foreach (var (t, s) in starts) t.position = Vector3.Lerp(s, s + up, k);   // 运行时成员挂在主对象下一起移动，效果相同
                        };
                        seconds = () => flow.Motion.partMove.Seconds;
                    }
                    else
                    {
                        var latch = flow.LatchOuter;
                        targets.Add(latch.transform);
                        var home = latch.transform.position;
                        var c = flow.Cover.WorldBounds().center;
                        var dir = Vector3.ProjectOnPlane(home - c, flow.EngineLHinge.up).normalized;     // 与 FirstOrderFlow.MoveLatch 相同
                        var target = home + dir * flow.LatchOffset;
                        sampler = p => latch.transform.position = Vector3.Lerp(home, target, flow.Motion.partMove.Evaluate(p));
                        seconds = () => flow.Motion.partMove.Seconds;
                    }
                    Info = "配置：" + Describe(flow.Motion);
                    return true;
                }
                case Kind.WorkbenchTrayLift:
                {
                    var demo = Find<WbPlaceholderDemo>();
                    if (demo == null) return Fail("当前场景里没有工作台占位演示（WbPlaceholderDemo）。请打开工作台测试场景。");
                    var tray = demo.TrayScrews;
                    targets.Add(tray);
                    var a = tray.position;
                    var b = a + Vector3.up * demo.TrayLift;
                    sampler = p => demo.PoseSegment(tray, a, b, p);
                    seconds = () => demo.Motion.step.Seconds;
                    Info = "配置：" + Describe(demo.Motion);
                    return true;
                }
                default:
                {
                    if (clip == null) return Fail("请先选一个动画片段。");
                    var driver = Find<RotorPowerDriver>();
                    var animator = driver != null ? driver.GetComponent<Animator>() : Find<Animator>();
                    if (animator == null) return Fail("当前场景里没有七号的 Animator。");
                    clipTarget = animator.gameObject;
                    targets.AddRange(animator.GetComponentsInChildren<Transform>(true));
                    seconds = () => clip.length;
                    var s = AnimationUtility.GetAnimationClipSettings(clip);
                    Info = $"片段 {AssetDatabase.GetAssetPath(clip)}：{clip.length:F2} s，{clip.frameRate:F0} 帧/秒，循环 {(s.loopTime ? "是" : "否")}";
                    return true;
                }
            }
        }

        static string Describe(Object config)
        {
            var path = AssetDatabase.GetAssetPath(config);
            return string.IsNullOrEmpty(path) ? "（没接配置资产，用默认值）" : path;
        }
    }

    /// <summary>预览窗口：菜单 Border Repair > Unit07 动画 > 2. 动作预览（不进 Play）。</summary>
    public class Unit07MotionPreviewWindow : EditorWindow
    {
        Unit07MotionPreview.Kind kind = Unit07MotionPreview.Kind.DockClamps;
        AnimationClip clip;
        float progress;
        bool playing;
        double playStart;

        [MenuItem("Border Repair/Unit07 动画/2. 动作预览（不进 Play）", priority = 2)]
        public static void Open() => GetWindow<Unit07MotionPreviewWindow>("UNIT07 动作预览");

        void OnEnable() => EditorApplication.update += Tick;
        void OnDisable() { EditorApplication.update -= Tick; Unit07MotionPreview.End(); }

        void Tick()
        {
            if (!playing || !Unit07MotionPreview.Active) return;
            float len = Mathf.Max(0.01f, Unit07MotionPreview.Seconds);
            progress = (float)((EditorApplication.timeSinceStartup - playStart) / len);
            if (progress >= 1f) { progress = 1f; playing = false; }
            Unit07MotionPreview.Sample(progress);
            Repaint();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("预览只是临时摆姿态，不会保存到场景或预制体。结束预览、关闭窗口、进入 Play 或保存场景时自动还原。", MessageType.Info);
            using (new EditorGUI.DisabledScope(Unit07MotionPreview.Active))
            {
                var labels = Unit07MotionPreview.Labels.Values.ToArray();
                var kinds = Unit07MotionPreview.Labels.Keys.ToArray();
                int i = EditorGUILayout.Popup("动作", Array.IndexOf(kinds, kind), labels);
                kind = kinds[Mathf.Max(0, i)];
                if (kind == Unit07MotionPreview.Kind.RobotClip)
                {
                    if (clip == null) clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Unit07EditableAnimation.ClipPath("Gripper_OpenClose_R"));
                    clip = (AnimationClip)EditorGUILayout.ObjectField("动画片段", clip, typeof(AnimationClip), false);
                }
            }
            EditorGUILayout.Space();
            if (!Unit07MotionPreview.Active)
            {
                if (GUILayout.Button("开始预览", GUILayout.Height(28)))
                {
                    progress = 0f;
                    if (!Unit07MotionPreview.Begin(kind, clip)) ShowNotification(new GUIContent(Unit07MotionPreview.Info));
                }
                if (!string.IsNullOrEmpty(Unit07MotionPreview.Info)) EditorGUILayout.HelpBox(Unit07MotionPreview.Info, MessageType.Warning);
                return;
            }
            float len = Unit07MotionPreview.Seconds;
            if (!playing) progress = Unit07MotionPreview.Progress;
            EditorGUI.BeginChangeCheck();
            progress = EditorGUILayout.Slider("进度", progress, 0f, 1f);
            if (EditorGUI.EndChangeCheck()) { playing = false; Unit07MotionPreview.Sample(progress); }
            EditorGUILayout.LabelField("时间", $"{progress * len:F2} s / {len:F2} s");
            EditorGUILayout.HelpBox(Unit07MotionPreview.Info, MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(playing ? "暂停" : "播放一遍"))
                {
                    playing = !playing;
                    if (playing) { if (progress >= 1f) progress = 0f; playStart = EditorApplication.timeSinceStartup - progress * Mathf.Max(0.01f, len); }
                }
                if (GUILayout.Button("刷新（改了配置后点）")) Unit07MotionPreview.Sample(progress);
            }
            if (GUILayout.Button("结束预览（还原姿态）", GUILayout.Height(28))) { playing = false; Unit07MotionPreview.End(); }
        }
    }
}
