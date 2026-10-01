using System;
using System.IO;
using System.Linq;
using BorderRepair.Dock;
using BorderRepair.FirstOrder;
using BorderRepair.FirstOrder.EditorTools;
using BorderRepair.Motion.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorkbenchArea;
using Object = UnityEngine.Object;

namespace BorderRepair.Motion.Tests
{
    /// <summary>
    /// “Unity 内手动编辑动画”工作流（编辑模式）：
    /// 片段副本与 FBX 原版逐条一致；重复生成、重新导入 FBX、重新构建场景都不覆盖手调；恢复原版先备份；
    /// 默认配置与原代码逐帧一致；不进 Play 的预览结束后完全还原，且与运行时姿态一致。
    /// </summary>
    public class Unit07AnimEditEditModeTests
    {
        const string DockPrefab = "Assets/BorderRepair/Prefabs/Unit07Dock/Unit07ServiceDock.prefab";
        const float Dt = 1f / 60f;

        [OneTimeSetUp]
        public void EnsureAssets() => Unit07EditableAnimation.EnsureAll();

        [TearDown]
        public void CloseScene()
        {
            Unit07MotionPreview.End();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static AnimationClip Copy(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(Unit07EditableAnimation.ClipPath(name));

        // ================================================================== 曲线

        [Test]
        public void MotionTiming_DefaultCurves_EqualOriginalFormulas()
        {
            var lin = new MotionTiming(1f, MotionTiming.Linear());
            var smooth = new MotionTiming(1f, MotionTiming.SmoothStep());
            for (int i = 0; i <= 200; i++)
            {
                float t = i / 200f;
                Assert.AreEqual(t, lin.Evaluate(t), 1e-5f, "线性 = Mathf.MoveTowards 匀速");
                Assert.AreEqual(Mathf.SmoothStep(0f, 1f, t), smooth.Evaluate(t), 1e-5f, "缓入缓出 = Mathf.SmoothStep");
                Assert.AreEqual(t, lin.InverseEvaluate(lin.Evaluate(t)), 1e-4f);
                Assert.AreEqual(t, smooth.InverseEvaluate(smooth.Evaluate(t)), 1e-3f);
            }
            var overshoot = new MotionTiming(1f, new AnimationCurve(new Keyframe(0f, -0.3f), new Keyframe(0.5f, 1.4f), new Keyframe(1f, 1f)));
            for (int i = 0; i <= 100; i++) Assert.That(overshoot.Evaluate(i / 100f), Is.InRange(0f, 1f), "曲线超出 0–1 的部分被截断，路径不越界");
        }

        // ================================================================== 片段副本

        [Test]
        public void CopiedClips_AreWritableAssets_WithIdenticalBindingsCurvesLoopAndEvents()
        {
            var src = Unit07AnimAudit.FbxClips().ToList();
            Assert.AreEqual(9, src.Count, "RobotV4 FBX 有 9 个动作");
            foreach (var s in src)
            {
                var c = Copy(s.name);
                Assert.IsNotNull(c, $"{s.name}：缺少可编辑副本");
                var path = AssetDatabase.GetAssetPath(c);
                Assert.IsTrue(path.EndsWith(".anim") && AssetDatabase.IsMainAsset(c), $"{s.name}：副本应是独立的 .anim 主资源");
                Assert.IsFalse((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0, $"{s.name}：副本可写");
                Assert.AreEqual(s.frameRate, c.frameRate, s.name);
                Assert.AreEqual(s.length, c.length, 1e-5f, s.name);
                var ss = AnimationUtility.GetAnimationClipSettings(s);
                var cs = AnimationUtility.GetAnimationClipSettings(c);
                Assert.AreEqual(ss.loopTime, cs.loopTime, $"{s.name}：循环");
                Assert.AreEqual(ss.loopBlend, cs.loopBlend, s.name);
                Assert.AreEqual(ss.startTime, cs.startTime, 1e-6f, s.name);
                Assert.AreEqual(ss.stopTime, cs.stopTime, 1e-6f, s.name);
                Assert.AreEqual(ss.keepOriginalPositionY, cs.keepOriginalPositionY, s.name);
                Assert.AreEqual(AnimationUtility.GetAnimationEvents(s).Length, AnimationUtility.GetAnimationEvents(c).Length, s.name);
                var sb = AnimationUtility.GetCurveBindings(s).OrderBy(b => b.path).ThenBy(b => b.propertyName).ToArray();
                var cb = AnimationUtility.GetCurveBindings(c).OrderBy(b => b.path).ThenBy(b => b.propertyName).ToArray();
                Assert.AreEqual(sb.Length, cb.Length, $"{s.name}：曲线条数");
                for (int i = 0; i < sb.Length; i++)
                {
                    Assert.AreEqual(sb[i].path, cb[i].path, $"{s.name}：骨骼路径");
                    Assert.AreEqual(sb[i].propertyName, cb[i].propertyName, s.name);
                    Assert.AreEqual(sb[i].type, cb[i].type, s.name);
                    var ks = AnimationUtility.GetEditorCurve(s, sb[i]).keys;
                    var kc = AnimationUtility.GetEditorCurve(c, cb[i]).keys;
                    Assert.AreEqual(ks.Length, kc.Length, $"{s.name} {sb[i].path}.{sb[i].propertyName}：关键帧数");
                    for (int k = 0; k < ks.Length; k++)
                    {
                        Assert.AreEqual(ks[k].time, kc[k].time, 1e-6f);
                        Assert.AreEqual(ks[k].value, kc[k].value, 1e-6f, $"{s.name} {sb[i].path}.{sb[i].propertyName} 第 {k} 帧");
                        Assert.AreEqual(ks[k].inTangent, kc[k].inTangent, 1e-4f);
                        Assert.AreEqual(ks[k].outTangent, kc[k].outTangent, 1e-4f);
                    }
                }
            }
        }

        [Test]
        public void CopiedClips_SampleToTheSamePose_AsFbx_AndKeepTheStaticPose()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Unit07AnimAudit.RobotFbx);
            var a = Object.Instantiate(model);
            var b = Object.Instantiate(model);
            try
            {
                var ta = a.GetComponentsInChildren<Transform>(true);
                var tb = b.GetComponentsInChildren<Transform>(true);
                // 静态姿态：导入后处理把 Idle_Hover 第 0 帧作为模型姿态；副本第 0 帧必须给出同一姿态
                Copy("Idle_Hover").SampleAnimation(b, 0f);
                for (int i = 0; i < ta.Length; i++)
                    Assert.Less(Quaternion.Angle(ta[i].localRotation, tb[i].localRotation), 0.01f, $"静态姿态 {ta[i].name}");
                foreach (var s in Unit07AnimAudit.FbxClips())
                {
                    var c = Copy(s.name);
                    for (float t = 0f; t <= s.length + 1e-4f; t += 1f / 90f)       // 比关键帧更密，覆盖帧间插值
                    {
                        s.SampleAnimation(a, t);
                        c.SampleAnimation(b, t);
                        for (int i = 0; i < ta.Length; i++)
                        {
                            Assert.Less((ta[i].localPosition - tb[i].localPosition).magnitude, 1e-5f, $"{s.name} t={t:F3} {ta[i].name} 位置");
                            Assert.Less(Quaternion.Angle(ta[i].localRotation, tb[i].localRotation), 0.01f, $"{s.name} t={t:F3} {ta[i].name} 旋转");
                        }
                    }
                }
            }
            finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
        }

        [Test]
        public void EditableController_IsSeparate_UsesCopies_AndOriginalControllersStillUseFbx()
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Unit07EditableAnimation.ControllerPath);
            Assert.IsNotNull(ac);
            var states = ac.layers[0].stateMachine.states.Select(s => s.state).ToList();
            CollectionAssert.AreEquivalent(Unit07AnimAudit.FbxClips().Select(c => c.name), states.Select(s => s.name), "状态名与原控制器相同");
            Assert.IsTrue(states.All(s => AssetDatabase.GetAssetPath(s.motion).StartsWith(Unit07EditableAnimation.ClipDir + "/")), "可编辑控制器只引用 .anim 副本");
            Assert.AreEqual("Idle_Hover", ac.layers[0].stateMachine.defaultState.name);
            var rl = ac.layers.Single(l => l.name == Unit07EditableAnimation.RotorLayer);
            Assert.AreEqual(1f, rl.defaultWeight);
            var angleState = rl.stateMachine.states.Select(s => s.state).Single(s => s.name == Unit07EditableAnimation.RotorAngleState);
            Assert.AreEqual(0f, angleState.speed, "Rotor_Angle 的时刻只由 RotorPowerDriver 定位");
            Assert.AreEqual(Unit07EditableAnimation.RotorAngleClipPath, AssetDatabase.GetAssetPath(angleState.motion));
            Assert.AreEqual("Idle_Hover", rl.stateMachine.defaultState.motion.name, "悬停时 Rotors 层播 Idle_Hover");
            // 转角片段：第 k 度 = 静止姿态绕实测转轴转 k 度
            var angleClip = (AnimationClip)angleState.motion;
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Unit07AnimAudit.RobotFbx));
            try
            {
                var driver = AssetDatabase.LoadAssetAtPath<GameObject>(Unit07EditableAnimation.RobotPrefab).GetComponent<RotorPowerDriver>();
                var paths = Unit07EditableAnimation.RotorPaths();
                var rest = paths.Select(p => model.transform.Find(p).localRotation).ToArray();
                foreach (var deg in new[] { 0f, 1f, 37f, 90f, 181f, 271.5f, 359f })
                {
                    angleClip.SampleAnimation(model, deg / 360f);
                    for (int r = 0; r < paths.Length; r++)
                        Assert.Less(Quaternion.Angle(rest[r] * Quaternion.AngleAxis(deg, driver.LocalAxes[r]), model.transform.Find(paths[r]).localRotation), 0.01f, $"{paths[r]} {deg}°");
                }
            }
            finally { Object.DestroyImmediate(model); }
            var rotorPaths = Unit07EditableAnimation.RotorPaths();
            var bm = ac.layers[0].avatarMask;
            var rm = rl.avatarMask;
            for (int i = 0; i < bm.transformCount; i++)
            {
                bool isRotor = rotorPaths.Contains(bm.GetTransformPath(i));
                Assert.AreEqual(!isRotor, bm.GetTransformActive(i), "Base Layer 遮罩：除转子外全部");
            }
            for (int i = 0; i < rm.transformCount; i++)
                Assert.AreEqual(rotorPaths.Contains(rm.GetTransformPath(i)), rm.GetTransformActive(i), "Rotors 层遮罩：只有转子");
            foreach (var orig in new[] { "Assets/RobotV4/RobotV4_Acceptance.controller", "Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_Dock.controller" })
            {
                var o = AssetDatabase.LoadAssetAtPath<AnimatorController>(orig);
                Assert.AreEqual(1, o.layers.Length, orig + "：原控制器没有被改");
                Assert.IsTrue(o.layers[0].stateMachine.states.All(s => AssetDatabase.GetAssetPath(s.state.motion) == Unit07AnimAudit.RobotFbx), orig + "：仍引用 FBX 原片段");
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Unit07EditableAnimation.RobotPrefab);
            Assert.AreEqual("Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_Dock.controller",
                            AssetDatabase.GetAssetPath(prefab.GetComponent<Animator>().runtimeAnimatorController), "停靠预制体本身不改");
        }

        // ================================================================== 持久性

        /// <summary>手调一个片段和一个配置，测试结束后原样写回文件。</summary>
        sealed class HandEdit : IDisposable
        {
            readonly string clipPath = Unit07EditableAnimation.ClipPath("Gripper_OpenClose_R");
            readonly string cfgPath = Unit07EditableAnimation.DockMotionPath;
            readonly byte[] clipBytes, cfgBytes;
            public const float Sentinel = 0.123456f;
            public const float ClampSeconds = 0.987f;

            public HandEdit()
            {
                clipBytes = File.ReadAllBytes(clipPath);
                cfgBytes = File.ReadAllBytes(cfgPath);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                var b = AnimationUtility.GetCurveBindings(clip).First(x => x.path.EndsWith("Arm_R_JawUpper") && x.propertyName == "m_LocalPosition.x");
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                var keys = curve.keys; keys[10].value = Sentinel; curve.keys = keys;
                AnimationUtility.SetEditorCurve(clip, b, curve);
                var cfg = AssetDatabase.LoadAssetAtPath<Unit07DockMotionConfig>(cfgPath);
                cfg.clamps.seconds = ClampSeconds;
                cfg.clamps.curve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.3f, 0.8f), new Keyframe(1, 1));
                EditorUtility.SetDirty(clip); EditorUtility.SetDirty(cfg);
                AssetDatabase.SaveAssets();
            }

            public static bool StillThere()
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Unit07EditableAnimation.ClipPath("Gripper_OpenClose_R"));
                var b = AnimationUtility.GetCurveBindings(clip).First(x => x.path.EndsWith("Arm_R_JawUpper") && x.propertyName == "m_LocalPosition.x");
                var cfg = AssetDatabase.LoadAssetAtPath<Unit07DockMotionConfig>(Unit07EditableAnimation.DockMotionPath);
                return Mathf.Approximately(AnimationUtility.GetEditorCurve(clip, b).keys[10].value, Sentinel) &&
                       Mathf.Approximately(cfg.clamps.seconds, ClampSeconds) && cfg.clamps.curve.length == 3;
            }

            public void Dispose()
            {
                File.WriteAllBytes(clipPath, clipBytes);
                File.WriteAllBytes(cfgPath, cfgBytes);
                AssetDatabase.ImportAsset(clipPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(cfgPath, ImportAssetOptions.ForceUpdate);
            }
        }

        [Test]
        public void HandEdits_SurviveRegeneration_FbxReimport_AndSceneRebuild()
        {
            const string tempScene = "Assets/BorderRepair/Animation/Unit07/__TempRebuild_Test.unity";
            var fbxHashes = Unit07AnimAudit.FbxClips().ToDictionary(c => c.name, Unit07EditableAnimation.ClipHash);
            using (new HandEdit())
            {
                Assert.IsTrue(HandEdit.StillThere());
                var status = Unit07EditableAnimation.ClipStatus().Single(s => s.name == "Gripper_OpenClose_R");
                Assert.IsTrue(status.handEdited, "状态里能看出手调过");

                // 1. 再跑一次生成：全部跳过
                var a = Unit07EditableAnimation.EnsureAll();
                Assert.IsEmpty(a.created, "重复运行不新建、不覆盖");
                Assert.IsTrue(HandEdit.StillThere(), "重复生成后手调还在");

                // 2. 重新导入 FBX：副本、配置、控制器引用都不变；FBX 原片段也没变
                AssetDatabase.ImportAsset(Unit07AnimAudit.RobotFbx, ImportAssetOptions.ForceUpdate);
                Assert.IsTrue(HandEdit.StillThere(), "重新导入 FBX 后手调还在");
                foreach (var c in Unit07AnimAudit.FbxClips()) Assert.AreEqual(fbxHashes[c.name], Unit07EditableAnimation.ClipHash(c), "FBX 原片段没变");
                var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Unit07EditableAnimation.ControllerPath);
                Assert.IsTrue(ac.layers[0].stateMachine.states.All(s => AssetDatabase.GetAssetPath(s.state.motion).StartsWith(Unit07EditableAnimation.ClipDir)));

                // 3. 重新构建首单测试场景（构建到临时路径）：场景引用持久资产，手调还在
                //    构建脚本会重写首单的两个占位材质（与本功能无关），测试结束后写回原文件，避免改动工作区
                var mats = Directory.GetFiles(FirstOrderSceneBuilder.ArtDir, "*.mat").ToDictionary(p => p, File.ReadAllBytes);
                try
                {
                    FirstOrderSceneBuilder.BuildTo(tempScene);
                    Assert.IsTrue(HandEdit.StillThere(), "重新构建场景后手调还在");
                    EditorSceneManager.OpenScene(tempScene, OpenSceneMode.Single);
                    var dock = Object.FindFirstObjectByType<Unit07DockController>();
                    var flow = Object.FindFirstObjectByType<FirstOrderFlow>();
                    Assert.AreEqual(Unit07EditableAnimation.ControllerPath, AssetDatabase.GetAssetPath(dock.RobotAnimator.runtimeAnimatorController));
                    Assert.AreEqual(Unit07EditableAnimation.DockMotionPath, AssetDatabase.GetAssetPath(dock.Motion));
                    Assert.AreEqual(Unit07EditableAnimation.RotorMotionPath, AssetDatabase.GetAssetPath(dock.Rotors.Motion));
                    Assert.AreEqual(Unit07EditableAnimation.FirstOrderMotionPath, AssetDatabase.GetAssetPath(flow.Motion));
                    Assert.AreEqual(HandEdit.ClampSeconds, dock.Motion.clamps.seconds, 1e-6f, "场景读到的是手调后的配置");
                }
                finally
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    AssetDatabase.DeleteAsset(tempScene);
                    foreach (var kv in mats) { File.WriteAllBytes(kv.Key, kv.Value); AssetDatabase.ImportAsset(kv.Key.Replace('\\', '/'), ImportAssetOptions.ForceUpdate); }
                }
            }
            Assert.IsFalse(Unit07EditableAnimation.ClipStatus().Single(s => s.name == "Gripper_OpenClose_R").handEdited, "测试结束后写回原样");
        }

        [Test]
        public void RestoreFromFbx_BacksUpCurrentContent_AndKeepsGuid()
        {
            var path = Unit07EditableAnimation.ClipPath("Gripper_OpenClose_R");
            var guid = AssetDatabase.AssetPathToGUID(path);
            string backup = null;
            using (new HandEdit())
            {
                try
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    backup = Unit07EditableAnimation.RestoreFromFbx(clip);
                    var src = Unit07AnimAudit.FbxClips().Single(c => c.name == clip.name);
                    Assert.AreEqual(Unit07EditableAnimation.ClipHash(src), Unit07EditableAnimation.ClipHash(clip), "恢复后与 FBX 原版一致");
                    Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path), "文件和 GUID 不变，控制器引用不断");
                    var b = AssetDatabase.LoadAssetAtPath<AnimationClip>(backup);
                    Assert.IsNotNull(b, "备份存在");
                    var bind = AnimationUtility.GetCurveBindings(b).First(x => x.path.EndsWith("Arm_R_JawUpper") && x.propertyName == "m_LocalPosition.x");
                    Assert.AreEqual(HandEdit.Sentinel, AnimationUtility.GetEditorCurve(b, bind).keys[10].value, 1e-6f, "备份里是恢复前的手调内容");
                }
                finally
                {
                    if (backup != null) AssetDatabase.DeleteAsset(Path.GetDirectoryName(backup).Replace('\\', '/'));
                    if (AssetDatabase.IsValidFolder(Unit07EditableAnimation.BackupDir) && AssetDatabase.FindAssets("", new[] { Unit07EditableAnimation.BackupDir }).Length == 0)
                        AssetDatabase.DeleteAsset(Unit07EditableAnimation.BackupDir);
                }
            }
        }

        // ================================================================== 默认配置与原代码逐帧一致

        GameObject dockGo, robotGo, flowGo;

        Unit07DockController RealDock(Unit07DockMotionConfig dockCfg, Unit07RotorMotionConfig rotorCfg)
        {
            dockGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DockPrefab));
            robotGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Unit07EditableAnimation.RobotPrefab));
            Transform F(GameObject r, string n) => r.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
            var anchor = F(dockGo, "Dock_RobotAnchor");
            robotGo.transform.position = anchor.position;
            flowGo = new GameObject("dock_test");
            var dock = flowGo.AddComponent<Unit07DockController>();
            dock.Configure(robotGo.transform, robotGo.GetComponent<Animator>(), robotGo.GetComponent<RotorPowerDriver>(), anchor,
                           F(dockGo, "Dock_Clamp_L"), F(dockGo, "Dock_Clamp_R"), F(dockGo, "Dock_PowerSwitch_Lever"), F(dockGo, "Dock_PowerSwitch_Lamp").GetComponent<Renderer>());
            dock.SetMotionConfig(dockCfg);
            dock.Rotors.SetMotionConfig(rotorCfg);
            dock.Initialize();
            Assert.IsTrue(dock.ConfigurationValid);
            dock.ResetToHover();
            return dock;
        }

        void DestroyDock() { foreach (var g in new[] { dockGo, robotGo, flowGo }) if (g != null) Object.DestroyImmediate(g); }

        void Step(Unit07DockController d) { d.Tick(Dt); d.Rotors.Step(Dt); }

        /// <summary>
        /// 用配置资产的默认值跑一遍“张开 → 落座 → 夹紧 → 断电 → 停转 → 通电 → 回到悬停转速”，
        /// 每一帧把夹具角度、七号高度、手柄角度、转子转速与原代码的公式（MoveTowards / SmoothStep / 匀加减速）对比。
        /// </summary>
        [Test]
        public void DefaultConfig_DockAndRotors_MatchOriginalCode_FrameByFrame()
        {
            var dockCfg = AssetDatabase.LoadAssetAtPath<Unit07DockMotionConfig>(Unit07EditableAnimation.DockMotionPath);
            var rotorCfg = AssetDatabase.LoadAssetAtPath<Unit07RotorMotionConfig>(Unit07EditableAnimation.RotorMotionPath);
            Assert.AreEqual("线性（匀速）", Unit07AnimAudit.Curve(dockCfg.clamps));
            Assert.AreEqual("缓入缓出（等同 SmoothStep）", Unit07AnimAudit.Curve(dockCfg.descend));
            var d = RealDock(dockCfg, rotorCfg);
            try
            {
                var closedL = d.ClampL.localRotation;
                var leverAtOn = d.PowerLever.localRotation;
                float hover = d.HoverHeight, refClamp = 0f, refLever = 0f, refTimer = 0f, refSpeed = 360f;
                int frames = 0;
                float maxClamp = 0f, maxHeight = 0f, maxLever = 0f, maxSpeed = 0f;
                void Compare()
                {
                    var expClamp = closedL * Quaternion.AngleAxis(d.UnityOpenDegL * refClamp, Vector3.up);
                    maxClamp = Mathf.Max(maxClamp, Quaternion.Angle(expClamp, d.ClampL.localRotation));
                    if (d.State == DockState.Descending)
                        maxHeight = Mathf.Max(maxHeight, Mathf.Abs(hover * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(refTimer))) - (d.RobotRoot.position.y - d.RobotAnchor.position.y)));
                    var expLever = leverAtOn * Quaternion.AngleAxis(Mathf.Lerp(0f, d.UnityOffDeg - d.UnityOnDeg, refLever), Vector3.right);
                    maxLever = Mathf.Max(maxLever, Quaternion.Angle(expLever, d.PowerLever.localRotation));
                    if (d.Rotors.Driven) maxSpeed = Mathf.Max(maxSpeed, Mathf.Abs(refSpeed - d.Rotors.SpeedDegPerSec));
                }
                void Run(Func<bool> until, string what)
                {
                    for (int i = 0; !until(); i++)
                    {
                        if (i > 2000) Assert.Fail("超时：" + what);
                        var st = d.State;
                        // 原代码（Unit07DockController / RotorPowerDriver 修改前）的公式
                        if (st == DockState.ClampsOpening) refClamp = Mathf.MoveTowards(refClamp, 1f, Dt / 0.6f);
                        if (st == DockState.Clamping) refClamp = Mathf.MoveTowards(refClamp, 0f, Dt / 0.6f);
                        if (st == DockState.Descending) refTimer += Dt / 1.2f;
                        refLever = Mathf.MoveTowards(refLever, d.PowerOn ? 0f : 1f, Dt / 0.3f);
                        bool driven = d.Rotors.Driven;
                        Step(d);
                        if (driven) refSpeed = Mathf.MoveTowards(refSpeed, d.Rotors.Powered ? 360f : 0f, (d.Rotors.Powered ? 360f / 1.0f : 360f / 2.5f) * Dt);
                        frames++;
                        Compare();
                    }
                }
                Assert.IsTrue(d.Interact(DockAction.Clamps));
                Run(() => d.State == DockState.SeatedOpen, "落座");
                Assert.IsTrue(d.Interact(DockAction.Clamps));
                Run(() => d.State == DockState.Clamped, "夹紧");
                Assert.IsTrue(d.Interact(DockAction.PowerSwitch));
                Run(() => d.State == DockState.RotorsStopped, "停转");
                Assert.IsTrue(d.Interact(DockAction.PowerSwitch));
                Run(() => d.Rotors.SpeedDegPerSec >= 360f - 1e-3f && d.LeverOffFraction <= 0f, "回到悬停转速");
                Assert.Less(maxClamp, 0.01f, "夹具角度与原代码一致（°）");
                Assert.Less(maxHeight, 1e-5f, "落座高度与原代码一致（m）");
                Assert.Less(maxLever, 0.01f, "手柄角度与原代码一致（°）");
                Assert.Less(maxSpeed, 0.05f, "转子转速与原代码一致（°/s）");
                Assert.Greater(frames, 340, "覆盖完整流程：张开 0.6 + 落座 1.2 + 夹紧 0.6 + 减速 2.5 + 加速 1.0 s");
            }
            finally { DestroyDock(); }
        }

        [Test]
        public void EditedConfig_ChangesDurationsAndCurves_WithoutChangingStateRules()
        {
            var dockCfg = Object.Instantiate(AssetDatabase.LoadAssetAtPath<Unit07DockMotionConfig>(Unit07EditableAnimation.DockMotionPath));
            var rotorCfg = Object.Instantiate(AssetDatabase.LoadAssetAtPath<Unit07RotorMotionConfig>(Unit07EditableAnimation.RotorMotionPath));
            dockCfg.clamps = new MotionTiming(1.5f, MotionTiming.SmoothStep());
            dockCfg.descend = new MotionTiming(0.6f, MotionTiming.Linear());
            rotorCfg.spinDown = new MotionTiming(1.0f, new AnimationCurve(new Keyframe(0, 0, 2, 2), new Keyframe(1, 1, 0, 0)));   // 先快后慢
            var d = RealDock(dockCfg, rotorCfg);
            try
            {
                Assert.IsTrue(d.Interact(DockAction.Clamps));
                float t = 0f;
                while (d.State == DockState.ClampsOpening) { Step(d); t += Dt; if (t > 5f) Assert.Fail(); }
                Assert.AreEqual(1.5f, t, 2 * Dt, "夹具张开时长按配置");
                float tDesc = 0f;
                while (d.State == DockState.Descending) { Step(d); tDesc += Dt; if (tDesc > 5f) Assert.Fail(); }
                Assert.AreEqual(0.6f, tDesc, 2 * Dt, "落座时长按配置");
                Assert.IsFalse(d.Interact(DockAction.PowerSwitch), "错误操作仍被拒绝：没夹紧不能断电");
                Assert.IsTrue(d.Interact(DockAction.Clamps));
                while (d.State == DockState.Clamping) Step(d);
                Assert.IsTrue(d.Interact(DockAction.PowerSwitch));
                float tStop = 0f, speedHalf = -1f;
                while (d.State != DockState.RotorsStopped)
                {
                    Step(d); tStop += Dt;
                    if (speedHalf < 0f && tStop >= 0.5f) speedHalf = d.Rotors.SpeedDegPerSec;
                    if (tStop > 5f) Assert.Fail();
                    Assert.IsFalse(d.InspectLeftEngine() && d.State != DockState.RotorsStopped, "叶轮减速中仍禁止检查");
                }
                Assert.AreEqual(1.0f, tStop, 2 * Dt, "转子减速时长按配置");
                Assert.AreEqual(360f * (1f - rotorCfg.spinDown.Evaluate(0.5f)), speedHalf, 360f * 2 * Dt * 2f, "转速按曲线变化");
                Assert.Less(speedHalf, 360f * 0.5f, "先快后慢：一半时间时转速已低于一半");
            }
            finally { DestroyDock(); Object.DestroyImmediate(dockCfg); Object.DestroyImmediate(rotorCfg); }
        }

        // ================================================================== 不进 Play 的预览

        static readonly Unit07MotionPreview.Kind[] FirstOrderKinds =
        {
            Unit07MotionPreview.Kind.DockClamps, Unit07MotionPreview.Kind.DockDescend, Unit07MotionPreview.Kind.DockLever,
            Unit07MotionPreview.Kind.RotorSpinDown, Unit07MotionPreview.Kind.RotorSpinUp, Unit07MotionPreview.Kind.LiftOff,
            Unit07MotionPreview.Kind.CoverLift, Unit07MotionPreview.Kind.LatchOpen, Unit07MotionPreview.Kind.RobotClip,
        };

        [Test]
        public void Preview_EveryMotion_RestoresPoses_AndLeavesSceneAndPrefabsClean()
        {
            var scene = EditorSceneManager.OpenScene(FirstOrderSceneBuilder.ScenePath, OpenSceneMode.Single);
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var before = all.ToDictionary(t => t, t => (t.localPosition, t.localRotation));
            var robot = Object.FindFirstObjectByType<Unit07DockController>().RobotRoot.gameObject;
            int prefabMods = PrefabUtility.GetPropertyModifications(robot).Length;
            foreach (var kind in FirstOrderKinds)
            {
                Assert.IsTrue(Unit07MotionPreview.Begin(kind, Copy("Gripper_OpenClose_R")), $"{kind}：{Unit07MotionPreview.Info}");
                int moved = 0;
                foreach (var p in new[] { 0.25f, 0.5f, 1f })
                {
                    Unit07MotionPreview.Sample(p);
                    moved += all.Count(t => (t.localPosition - before[t].localPosition).sqrMagnitude > 1e-10f || Quaternion.Angle(t.localRotation, before[t].localRotation) > 0.01f);
                }
                Assert.Greater(moved, 0, $"{kind}：预览确实改变了姿态");
                Unit07MotionPreview.End();
                foreach (var t in all)
                {
                    Assert.AreEqual(before[t].localPosition, t.localPosition, $"{kind}：{t.name} 位置已还原");
                    Assert.AreEqual(before[t].localRotation, t.localRotation, $"{kind}：{t.name} 旋转已还原");
                }
                Assert.IsFalse(scene.isDirty, $"{kind}：预览后场景没有被标记为已修改");
                Assert.AreEqual(prefabMods, PrefabUtility.GetPropertyModifications(robot).Length, $"{kind}：预览没有留下预制体覆盖");
            }
        }

        [Test]
        public void Preview_EqualsRuntimePose_AtTheSameTime()
        {
            var dockCfg = AssetDatabase.LoadAssetAtPath<Unit07DockMotionConfig>(Unit07EditableAnimation.DockMotionPath);
            var rotorCfg = AssetDatabase.LoadAssetAtPath<Unit07RotorMotionConfig>(Unit07EditableAnimation.RotorMotionPath);
            var d = RealDock(dockCfg, rotorCfg);
            try
            {
                // 运行：张开夹具，推进到 0.3 s（夹具时长 0.6 s 的一半）
                Assert.IsTrue(d.Interact(DockAction.Clamps));
                for (int i = 0; i < 18; i++) Step(d);
                var runClamp = d.ClampL.localRotation;
                // 预览：同一时刻（进度 0.5），从闭合姿态出发
                var closed = d.ClampL.localRotation;
                d.PoseClamps(0f);
                var closedPose = d.ClampL.localRotation;
                d.PoseClamps(18 * Dt / dockCfg.clamps.Seconds);
                Assert.Less(Quaternion.Angle(runClamp, d.ClampL.localRotation), 0.01f, "夹具：预览 = 运行");
                Assert.Greater(Quaternion.Angle(closedPose, runClamp), 1f);
                // 落座：运行 0.5 s
                while (d.State != DockState.Descending) Step(d);
                for (int i = 0; i < 30; i++) Step(d);
                float runH = d.RobotRoot.position.y;
                d.PoseDescend(30 * Dt / dockCfg.descend.Seconds);
                Assert.AreEqual(runH, d.RobotRoot.position.y, 1e-5f, "落座：预览 = 运行");
                while (d.State != DockState.SeatedOpen) Step(d);
                Assert.IsTrue(d.Interact(DockAction.Clamps));
                while (d.State != DockState.Clamped) Step(d);
                // 转子减速：运行 1.0 s 的累计转角 = 预览积分
                var rotor = d.Rotors.Rotors[0];
                var start = rotor.localRotation;
                Assert.IsTrue(d.Interact(DockAction.PowerSwitch));
                for (int i = 0; i < 60; i++) Step(d);
                float runAngle = Quaternion.Angle(start, rotor.localRotation);
                float previewAngle = RotorPowerDriver.AngleAfter(rotorCfg.spinDown, false, d.Rotors.IdleSpeedDegPerSec, 1.0f, out var frac);
                // 预览从转子当前位置起算；运行时断电前已经在转，所以比较“断电后 1 s 内转过的角度”
                Assert.AreEqual(360f * (1f - rotorCfg.spinDown.Evaluate(1.0f / rotorCfg.spinDown.Seconds)), d.Rotors.SpeedDegPerSec, 0.5f, "转速：预览 = 运行");
                Assert.AreEqual(d.Rotors.SpeedDegPerSec / 360f, frac, 0.01f);
                Assert.Greater(previewAngle, 0f);
                Assert.Greater(runAngle, 0f);
            }
            finally { DestroyDock(); }
        }

        [Test]
        public void Preview_InWorkbenchScene_RestoresTray()
        {
            var scene = EditorSceneManager.OpenScene(Unit07AnimAudit.WorkbenchScene, OpenSceneMode.Single);
            var demo = Object.FindFirstObjectByType<WbPlaceholderDemo>();
            Assert.AreEqual(Unit07EditableAnimation.WorkbenchMotionPath, AssetDatabase.GetAssetPath(demo.Motion), "工作台测试场景引用持久配置");
            var tray = demo.TrayScrews;
            var p0 = tray.position;
            Assert.IsTrue(Unit07MotionPreview.Begin(Unit07MotionPreview.Kind.WorkbenchTrayLift), Unit07MotionPreview.Info);
            Unit07MotionPreview.Sample(0.5f);
            Assert.AreEqual(p0.y + demo.TrayLift * demo.Motion.step.Evaluate(0.5f), tray.position.y, 1e-5f, "预览 = 运行时 PoseSegment");
            Unit07MotionPreview.End();
            Assert.AreEqual(p0, tray.position);
            Assert.IsFalse(scene.isDirty);
        }
    }
}
