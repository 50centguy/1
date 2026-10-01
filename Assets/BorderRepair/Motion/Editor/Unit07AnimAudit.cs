using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Dock;
using BorderRepair.FirstOrder;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorkbenchArea;
using Object = UnityEngine.Object;

namespace BorderRepair.Motion.EditorTools
{
    /// <summary>
    /// 审计 UNIT 07 现有的三类动作，写报告到 Docs/Integration/Unit07AnimEdit/audit.md：
    /// 1. RobotV4 FBX 的骨骼动作片段（时长、帧率、循环、绑定、哪些骨骼真的在动）；
    /// 2. 维修座脚本动作（Unit07DockController / RotorPowerDriver）；
    /// 3. 首单 / 工作台占位移动（FirstOrderFlow / WbPlaceholderDemo）。
    /// 并检查：脚本移动的 Transform 是否也被 Animator 的片段绑定（同时写同一个 Transform）。只读，不改任何资源和场景。
    /// 命令行：-executeMethod BorderRepair.Motion.EditorTools.Unit07AnimAudit.RunBatch
    /// </summary>
    public static class Unit07AnimAudit
    {
        public const string RobotFbx = "Assets/RobotV4/Model/robot-final.fbx";
        public const string OutDir = "Docs/Integration/Unit07AnimEdit";
        public const string FirstOrderScene = "Assets/BorderRepair/FirstOrder/Scenes/Unit07FirstOrder_Test.unity";
        public const string WorkbenchScene = "Assets/WorkbenchArea/Scenes/WorkbenchArea_Test.unity";

        public static IEnumerable<AnimationClip> FbxClips() =>
            AssetDatabase.LoadAllAssetsAtPath(RobotFbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).OrderBy(c => c.name);

        [MenuItem("Border Repair/Unit07 动画/审计现有动作（只读，写报告）")]
        public static void Run() => Write(BuildReport());

        public static void RunBatch()
        {
            int code = 0;
            try { Write(BuildReport()); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static void Write(string md)
        {
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "audit.md"), md, new UTF8Encoding(false));
            Debug.Log("[Unit07Anim] 审计报告：" + Path.Combine(OutDir, "audit.md"));
        }

        /// <summary>片段里真正在变化的绑定路径（常量曲线不算“在动”）。</summary>
        public static HashSet<string> AnimatedPaths(AnimationClip clip)
        {
            var set = new HashSet<string>();
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                var c = AnimationUtility.GetEditorCurve(clip, b);
                if (c == null || c.length < 2) continue;
                float min = c.keys.Min(k => k.value), max = c.keys.Max(k => k.value);
                if (max - min > 1e-5f) set.Add(b.path);
            }
            return set;
        }

        public static HashSet<string> BoundPaths(IEnumerable<AnimationClip> clips) =>
            new HashSet<string>(clips.SelectMany(c => AnimationUtility.GetCurveBindings(c)).Select(b => b.path));

        public static string PathOf(Transform t, Transform root)
        {
            if (t == root) return "";
            var parts = new List<string>();
            for (var x = t; x != null && x != root; x = x.parent) parts.Insert(0, x.name);
            return string.Join("/", parts);
        }

        static string Short(string path) => path.Length == 0 ? "（根）" : path.Substring(path.LastIndexOf('/') + 1);

        public static string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# UNIT 07 动作审计（只读）");
            sb.AppendLine();
            sb.AppendLine($"Unity {Application.unityVersion}，{DateTime.Now:yyyy-MM-dd HH:mm}。由 `Unit07AnimAudit` 生成。");
            sb.AppendLine();

            // ---------------------------------------------------------------- 1. FBX 骨骼动作
            var clips = FbxClips().ToList();
            var importer = (ModelImporter)AssetImporter.GetAtPath(RobotFbx);
            sb.AppendLine("## 1. RobotV4 FBX 骨骼动作");
            sb.AppendLine();
            sb.AppendLine($"- **来源**：`{RobotFbx}`（只读，片段是 FBX 的子资源，不能直接保存修改）。");
            sb.AppendLine($"- **导入设置**：动画类型 {importer.animationType}，重采样曲线 {importer.resampleCurves}，压缩 {importer.animationCompression}，片段 {clips.Count} 个。");
            sb.AppendLine("- **导入后处理**：`RobotV4ModelPostprocessor` 把 `Idle_Hover` 第 0 帧采样到模型上，作为预制体的静态姿态。");
            sb.AppendLine();
            sb.AppendLine("| 片段 | 时长 (s) | 帧率 | 帧数 | 循环 | 曲线数 | 绑定路径 | 根节点曲线 | 真正在动的骨骼 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var c in clips)
            {
                var s = AnimationUtility.GetAnimationClipSettings(c);
                var bindings = AnimationUtility.GetCurveBindings(c);
                var paths = bindings.Select(b => b.path).Distinct().Count();
                var root = bindings.Where(b => b.path.Length == 0).Select(b => b.propertyName).ToList();
                var moving = AnimatedPaths(c).Select(Short).OrderBy(x => x).ToList();
                sb.AppendLine($"| `{c.name}` | {c.length:F3} | {c.frameRate:F0} | {Mathf.RoundToInt(c.length * c.frameRate)} | {(s.loopTime ? "是" : "否")} | {bindings.Length} | {paths} | " +
                              $"{(root.Count == 0 ? "无" : string.Join(", ", root))} | {(moving.Count == 0 ? "（静态姿态）" : string.Join("、", moving.Take(12)) + (moving.Count > 12 ? $" 等 {moving.Count} 个" : ""))} |");
            }
            sb.AppendLine();
            foreach (var path in new[] { "Assets/RobotV4/RobotV4_Acceptance.controller", "Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_Dock.controller" })
            {
                var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (ac == null) { sb.AppendLine($"- `{path}`：不存在"); continue; }
                var layer = ac.layers[0];
                var states = layer.stateMachine.states.Select(x => x.state).ToList();
                sb.AppendLine($"- **控制器** `{path}`：{ac.layers.Length} 层，{states.Count} 个状态，默认 `{layer.stateMachine.defaultState?.name}`；" +
                              $"片段全部引用 FBX 子资源：{states.All(x => AssetDatabase.GetAssetPath(x.motion) == RobotFbx)}；Write Defaults：{string.Join("/", states.Select(x => x.writeDefaultValues).Distinct())}");
            }
            sb.AppendLine();

            // ---------------------------------------------------------------- 2/3. 场景里的脚本动作
            var bound = BoundPaths(clips);
            var foScene = EditorSceneManager.OpenScene(FirstOrderScene, OpenSceneMode.Single);
            var dock = Object.FindFirstObjectByType<Unit07DockController>();
            var flow = Object.FindFirstObjectByType<FirstOrderFlow>();
            var robot = dock.RobotRoot;
            var anim = dock.RobotAnimator;
            var rotor = robot.GetComponent<RotorPowerDriver>();
            float F(Object o, string field) => new SerializedObject(o).FindProperty(field).floatValue;

            sb.AppendLine("## 2. 维修座脚本动作（`Unit07DockController` / `RotorPowerDriver`）");
            sb.AppendLine();
            sb.AppendLine($"首单测试场景 `{FirstOrderScene}` 里的数值。七号的 Animator 控制器：`{AssetDatabase.GetAssetPath(anim.runtimeAnimatorController)}`。");
            sb.AppendLine();
            sb.AppendLine("| 动作 | 写入对象 | 时长 | 缓动（现版代码） | 状态判断 |");
            sb.AppendLine("|---|---|---|---|---|");
            sb.AppendLine($"| 夹具张开 / 合拢 | `Dock_Clamp_L`、`Dock_Clamp_R` 本地旋转（绕本地 Y，角度来自 FBX 属性 `unity_open_deg`） | {dock.Motion.clamps.Seconds} s | {Curve(dock.Motion.clamps)}，中途可反向 | `ClampsOpening` / `Clamping` |");
            sb.AppendLine($"| 七号落座 | 七号根节点世界位置（悬停高度 {F(dock, "hoverHeight")} m → 0） | {dock.Motion.descend.Seconds} s | {Curve(dock.Motion.descend)} | `Descending`；结束时转子交给 `RotorPowerDriver`，Animator 用 {dock.Motion.seatedBlendSeconds} s 过渡到 `Pose_Gripper_Closed` |");
            sb.AppendLine($"| 断电开关手柄 | `Dock_PowerSwitch_Lever` 本地旋转（`unity_on_deg` ↔ `unity_off_deg`） | {dock.Motion.lever.Seconds} s | {Curve(dock.Motion.lever)}，中途可反向 | 跟随供电状态 |");
            sb.AppendLine($"| 转子减速（断电） | 两根转子骨骼本地旋转 | {rotor.SpinDownSeconds} s（从 {rotor.IdleSpeedDegPerSec}°/s 到 0） | 转速变化：{Curve(rotor.Motion.spinDown)} | `SpinningDown` → `RotorsStopped` |");
            sb.AppendLine($"| 转子加速（通电） | 同上 | {rotor.SpinUpSeconds} s（0 → {rotor.IdleSpeedDegPerSec}°/s） | 转速变化：{Curve(rotor.Motion.spinUp)} | 首单 `PowerOn` 步骤 |");
            sb.AppendLine("| 状态灯 | 材质属性块（不是 Transform） | 立即 | — | 跟随供电状态 |");
            sb.AppendLine();
            sb.AppendLine($"转子骨骼：{string.Join("、", rotor.Rotors.Select(r => $"`{PathOf(r, robot)}`"))}。");
            sb.AppendLine();

            sb.AppendLine("## 3. 首单 / 工作台占位移动");
            sb.AppendLine();
            sb.AppendLine("| 动作 | 写入对象 | 时长 | 缓动（现版代码） | 路径 |");
            sb.AppendLine("|---|---|---|---|---|");
            sb.AppendLine($"| 锁扣扳开 / 扣回（`FirstOrderFlow.MoveLatch`） | 外侧、后侧锁扣世界位置 | {flow.Motion.partMove.Seconds} s | {Curve(flow.Motion.partMove)} | 沿外法线移出 {F(flow, "latchOffset") * 1000:F0} mm 再移回 |");
            sb.AppendLine($"| 上盖总成取下 / 搬运 / 装回 | 上盖（成员一起挂在主对象下） | 每段 {flow.Motion.partMove.Seconds} s | {Curve(flow.Motion.partMove)} | 沿引擎轴线抬起 {F(flow, "coverLift") * 100:F0} cm → 升到 {F(flow, "travelHeight")} m → 水平 → 落下 |");
            sb.AppendLine($"| 旧轴承取下 / 搬运，新轴承装上 | 旧轴承、占位新轴承 | 每段 {flow.Motion.partMove.Seconds} s | {Curve(flow.Motion.partMove)} | 沿轴抬起 {F(flow, "bearingLift") * 100:F0} cm → 搬运高度 → 落下 |");
            sb.AppendLine($"| 七号离座（`FirstOrderFlow.LiftOff`，原型） | 七号根节点世界位置（0 → 悬停高度） | {flow.Motion.liftOff.Seconds} s | {Curve(flow.Motion.liftOff)}；Animator {flow.Motion.liftBlendSeconds} s 过渡回 `Idle_Hover` | 夹具张开后上浮 |");
            sb.AppendLine($"| 复测采样（不是动作） | 只读 | {F(flow, "retestSeconds")} s | — | — |");

            // 冲突检查：脚本写的 Transform 是否被片段绑定
            var scriptWritten = new List<(string what, Transform t)> { ("七号根节点（落座 / 离座）", robot) };
            foreach (var p in flow.TrackedParts.Where(p => p != null && !p.isPlaceholder))
            {
                scriptWritten.Add((p.displayName, p.transform));
                foreach (var m in p.members) scriptWritten.Add((p.displayName + " 成员", m));
            }
            var conflicts = new List<string>();
            var notes = new List<string>();
            foreach (var (what, t) in scriptWritten)
            {
                if (!t.IsChildOf(robot) && t != robot) continue;
                var path = PathOf(t, robot);
                if (bound.Contains(path)) conflicts.Add($"{what} `{(path.Length == 0 ? "（根）" : path)}`");
                else notes.Add($"{what}：片段没有绑定");
            }
            var rotorConflicts = rotor.Rotors.Where(r => bound.Contains(PathOf(r, robot))).Select(r => r.name).ToList();
            var ctrl = anim.runtimeAnimatorController as AnimatorController;
            string rotorLayerInfo = null;
            if (ctrl != null && ctrl.layers.Length > 1 && ctrl.layers.Any(l => l.name == rotor.RotorLayerName))
            {
                var baseMask = ctrl.layers[0].avatarMask;
                var rotorPaths = new HashSet<string>(rotor.Rotors.Select(r => PathOf(r, robot)));
                bool baseExcludes = baseMask != null && Enumerable.Range(0, baseMask.transformCount)
                    .Where(i => rotorPaths.Contains(baseMask.GetTransformPath(i))).All(i => !baseMask.GetTransformActive(i));
                var rl = ctrl.layers.First(l => l.name == rotor.RotorLayerName);
                var angleState = rl.stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == rotor.RotorAngleState);
                rotorLayerInfo = $"七号用 `{AssetDatabase.GetAssetPath(ctrl)}`：Base Layer 遮罩去掉转子骨骼 = {baseExcludes}；" +
                                 $"`{rl.name}` 层只含转子，悬停播 `{rotor.RotorSpinState}`（Idle_Hover），落座后播 `{rotor.RotorAngleState}`" +
                                 $"（{(angleState != null ? AssetDatabase.GetAssetPath(angleState.motion) : "缺失")}，速度 {angleState?.speed}），时刻由 `RotorPowerDriver` 每帧定位。";
            }
            var foMotionAsset = AssetDatabase.GetAssetPath(flow.Motion);
            var dockMotionAsset = AssetDatabase.GetAssetPath(dock.Motion);
            var rotorMotionAsset = AssetDatabase.GetAssetPath(rotor.Motion);
            var dockAnimators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None).Where(a => a != anim).Select(a => a.name).ToList();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var wbScene = EditorSceneManager.OpenScene(WorkbenchScene, OpenSceneMode.Single);
            var demo = Object.FindFirstObjectByType<WbPlaceholderDemo>();
            sb.AppendLine($"| 工作台托盘取放、义肢占位件拆装、工具箱上层（`WbPlaceholderDemo.Move`） | 托盘、4 颗螺钉、盖板、工具箱上层世界位置 | 每段 {demo.Motion.step.Seconds} s | {Curve(demo.Motion.step)} | 抬起 → 平移 → 放下（几何路径另有检查） |");
            var wbAnimators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None).Length;
            var wbMotionAsset = AssetDatabase.GetAssetPath(demo.Motion);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            sb.AppendLine();

            sb.AppendLine("## 4. 有没有两个“写入者”同时写同一个 Transform");
            sb.AppendLine();
            if (rotorLayerInfo != null)
                sb.AppendLine($"- **转子骨骼**：片段绑定了 `{string.Join("`、`", rotorConflicts)}`。实测（Generic 骨架）：只要控制器里任何片段绑定了某块骨骼，Animator 每帧都会写它，遮罩、Write Defaults 关都挡不住。所以改为脚本不写、只由 Animator 写：{rotorLayerInfo}（由 PlayMode 测试 `RotorBones_OnlyAnimatorWrites_HoveringAndSeated` 验证；原控制器下的两个写入者由对照测试复现。）");
            else
                sb.AppendLine($"- **转子骨骼**：{(rotorConflicts.Count > 0 ? $"**有冲突**。`{string.Join("`、`", rotorConflicts)}` 被片段绑定（Idle_Hover 等），落座后 `RotorPowerDriver` 又在 LateUpdate 覆盖它们：Animator 每帧先写，脚本后写。现版靠执行顺序得到正确画面，但属于两个写入者。" : "片段没有绑定。")}");
            sb.AppendLine($"- **脚本移动的七号部件和七号根节点**：{(conflicts.Count > 0 ? "**有冲突**：" + string.Join("；", conflicts) : "没有被片段绑定（" + string.Join("；", notes) + "）")}。");
            sb.AppendLine($"- **维修座**：场景里除七号外的 Animator：{(dockAnimators.Count == 0 ? "无" : string.Join("、", dockAnimators))}。夹具、手柄只由 `Unit07DockController` 写。");
            sb.AppendLine($"- **工作台**：场景里 Animator {wbAnimators} 个。占位移动只由 `WbPlaceholderDemo` 写。");
            sb.AppendLine();

            sb.AppendLine("## 5. 时长和曲线从哪里来");
            sb.AppendLine();
            string Src(string p) => string.IsNullOrEmpty(p) ? "**没接配置资产**（用代码里的默认值）" : $"`{p}`";
            sb.AppendLine($"- **维修座（首单测试场景）**：{Src(dockMotionAsset)}");
            sb.AppendLine($"- **转子（首单测试场景）**：{Src(rotorMotionAsset)}");
            sb.AppendLine($"- **首单占位移动与离座**：{Src(foMotionAsset)}");
            sb.AppendLine($"- **工作台占位移动**：{Src(wbMotionAsset)}");
            sb.AppendLine($"- **七号骨骼动作**：七号 Animator 控制器 `{(ctrl != null ? AssetDatabase.GetAssetPath(ctrl) : "?")}`；FBX 子片段只读，可编辑副本见 `Assets/BorderRepair/Animation/Unit07/Clips`。");
            return sb.ToString();
        }

        /// <summary>曲线的中文说明：与默认线性 / 缓入缓出相同时直接写名字，否则写“自定义（n 个关键帧）”。</summary>
        public static string Curve(MotionTiming m)
        {
            bool Same(AnimationCurve a, AnimationCurve b) => a != null && a.length == b.length &&
                a.keys.Zip(b.keys, (x, y) => Mathf.Approximately(x.time, y.time) && Mathf.Approximately(x.value, y.value) &&
                                              Mathf.Approximately(x.inTangent, y.inTangent) && Mathf.Approximately(x.outTangent, y.outTangent)).All(v => v);
            if (Same(m.curve, MotionTiming.Linear())) return "线性（匀速）";
            if (Same(m.curve, MotionTiming.SmoothStep())) return "缓入缓出（等同 SmoothStep）";
            return $"自定义曲线（{m.curve?.length ?? 0} 个关键帧）";
        }
    }
}
