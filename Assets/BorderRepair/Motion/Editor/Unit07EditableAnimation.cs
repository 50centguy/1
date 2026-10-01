using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BorderRepair.Dock;
using BorderRepair.FirstOrder;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using WorkbenchArea;
using Object = UnityEngine.Object;

namespace BorderRepair.Motion.EditorTools
{
    /// <summary>
    /// UNIT 07“Unity 内手动编辑动画”的持久资产：
    /// - RobotV4 FBX 9 个动作的独立、可写副本（.anim），保留骨骼路径、全部曲线、循环设置和事件；
    /// - 测试场景专用的可编辑动画控制器（不覆盖 RobotV4_Acceptance.controller 和 UNIT07_RobotV4_Dock.controller）；
    /// - 脚本动作的配置资产（时长 + 运动曲线）。
    /// 所有生成都是“缺什么补什么”：已经存在的资产一律跳过，绝不覆盖手调过的内容。
    /// 恢复原版只有一个明确入口（菜单“把选中的动画片段恢复为 FBX 原版”），恢复前先备份。
    /// </summary>
    public static class Unit07EditableAnimation
    {
        public const string Root = "Assets/BorderRepair/Animation/Unit07";
        public const string ClipDir = Root + "/Clips";
        public const string ControllerDir = Root + "/Controllers";
        public const string MotionDir = Root + "/Motion";
        public const string BackupDir = Root + "/Backups";
        public const string ControllerPath = ControllerDir + "/UNIT07_RobotV4_Editable.controller";
        public const string BodyMaskPath = ControllerDir + "/UNIT07_Mask_Body_NoRotors.mask";
        public const string RotorMaskPath = ControllerDir + "/UNIT07_Mask_RotorsOnly.mask";
        public const string DockMotionPath = MotionDir + "/UNIT07_DockMotion.asset";
        public const string RotorMotionPath = MotionDir + "/UNIT07_RotorMotion.asset";
        public const string FirstOrderMotionPath = MotionDir + "/UNIT07_FirstOrderMotion.asset";
        public const string WorkbenchMotionPath = MotionDir + "/WB_PlaceholderMotion.asset";
        public const string RobotFbx = Unit07AnimAudit.RobotFbx;
        public const string RobotPrefab = "Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_DockReady.prefab";
        public const string RotorLayer = "Rotors";
        public const string RotorSpinState = "Rotor_Spin";
        public const string RotorAngleState = "Rotor_Angle";
        public const string RotorAngleClipPath = ControllerDir + "/UNIT07_RotorAngle_Generated.anim";
        public const string DefaultState = "Idle_Hover";

        public static string ClipPath(string clipName) => $"{ClipDir}/{clipName}.anim";

        public class Assets
        {
            public AnimatorController controller;
            public Unit07DockMotionConfig dock;
            public Unit07RotorMotionConfig rotor;
            public FirstOrderMotionConfig firstOrder;
            public WbPlaceholderMotionConfig workbench;
            public readonly List<AnimationClip> clips = new List<AnimationClip>();
            public readonly List<string> created = new List<string>();
            public readonly List<string> kept = new List<string>();
        }

        // ------------------------------------------------------------------ 菜单

        [MenuItem("Border Repair/Unit07 动画/1. 生成可编辑动画与动作配置（已存在的不覆盖）", priority = 1)]
        static void MenuEnsure()
        {
            var a = EnsureAll();
            var msg = $"新建 {a.created.Count} 个，已存在保留 {a.kept.Count} 个（没有覆盖任何已有文件）。\n\n" +
                      (a.created.Count > 0 ? "新建：\n" + string.Join("\n", a.created) + "\n\n" : "") +
                      "可编辑片段在 " + ClipDir + "，动作配置在 " + MotionDir + "。";
            Debug.Log("[Unit07Anim] " + msg);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("UNIT 07 可编辑动画", msg, "好");
        }

        public static void EnsureAllBatch()
        {
            int code = 0;
            try
            {
                var a = EnsureAll();
                Debug.Log($"[Unit07Anim] 生成：新建 {a.created.Count}（{string.Join("、", a.created)}），已存在保留 {a.kept.Count}");
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        [MenuItem("Border Repair/Unit07 动画/查看可编辑片段状态（哪些手调过）", priority = 20)]
        public static void MenuStatus()
        {
            var lines = ClipStatus().Select(s => $"{s.name}：{s.state}").ToList();
            var text = string.Join("\n", lines);
            Debug.Log("[Unit07Anim] 可编辑片段状态：\n" + text);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("UNIT 07 可编辑片段状态", text, "好");
        }

        [MenuItem("Border Repair/Unit07 动画/把选中的动画片段恢复为 FBX 原版（先备份）", priority = 21)]
        static void MenuRestoreSelected()
        {
            var sel = Selection.objects.OfType<AnimationClip>().Where(c => AssetDatabase.GetAssetPath(c).StartsWith(ClipDir + "/")).ToList();
            if (sel.Count == 0)
            {
                EditorUtility.DisplayDialog("恢复 FBX 原版", "请先在 Project 窗口里选中 " + ClipDir + " 里的一个或多个 .anim。", "好");
                return;
            }
            if (!EditorUtility.DisplayDialog("恢复 FBX 原版",
                    $"把下列片段恢复为 FBX 里的原版：\n{string.Join("\n", sel.Select(c => c.name))}\n\n当前内容会先备份到 {BackupDir}。继续吗？", "恢复", "取消")) return;
            foreach (var c in sel)
            {
                var backup = RestoreFromFbx(c);
                Debug.Log($"[Unit07Anim] {c.name} 已恢复为 FBX 原版；原内容备份在 {backup}");
            }
        }

        [MenuItem("Border Repair/Unit07 动画/把选中的动画片段恢复为 FBX 原版（先备份）", true)]
        static bool CanRestoreSelected() => Selection.objects.OfType<AnimationClip>().Any(c => AssetDatabase.GetAssetPath(c).StartsWith(ClipDir + "/"));

        // ------------------------------------------------------------------ 接到场景（构建脚本和“给现有测试场景接上”共用）

        /// <summary>维修座场景：七号用可编辑控制器（场景实例上覆盖，不改预制体），维修座和转子引用动作配置。</summary>
        public static void ApplyToDock(Unit07DockController dock, Assets a)
        {
            dock.SetMotionConfig(a.dock);
            if (dock.RobotAnimator != null)
            {
                dock.RobotAnimator.runtimeAnimatorController = a.controller;
                EditorUtility.SetDirty(dock.RobotAnimator);
            }
            if (dock.Rotors != null)
            {
                dock.Rotors.SetMotionConfig(a.rotor);
                EditorUtility.SetDirty(dock.Rotors);
            }
            EditorUtility.SetDirty(dock);
        }

        public const string DockTestScene = "Assets/BorderRepair/Scenes/Unit07Dock_Test.unity";

        /// <summary>
        /// 不重建、只给现有的维修座测试场景和工作台测试场景接上可编辑资产（首单测试场景由它的构建脚本重建）。
        /// 只改这两个测试场景里的引用，不动预制体、FBX、原控制器。
        /// </summary>
        [MenuItem("Border Repair/Unit07 动画/给现有维修座、工作台测试场景接上可编辑资产", priority = 30)]
        public static void PatchExistingTestScenes()
        {
            var a = EnsureAll();
            var s = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(DockTestScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
            foreach (var d in Object.FindObjectsByType<Unit07DockController>(FindObjectsSortMode.None)) ApplyToDock(d, a);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s);
            s = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Unit07AnimAudit.WorkbenchScene, UnityEditor.SceneManagement.OpenSceneMode.Single);
            foreach (var w in Object.FindObjectsByType<WbPlaceholderDemo>(FindObjectsSortMode.None)) { w.SetMotionConfig(a.workbench); EditorUtility.SetDirty(w); }
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s);
            Debug.Log($"[Unit07Anim] 已给 {DockTestScene}、{Unit07AnimAudit.WorkbenchScene} 接上可编辑资产");
        }

        public static void PatchExistingTestScenesBatch()
        {
            int code = 0;
            try { PatchExistingTestScenes(); } catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        // ------------------------------------------------------------------ 生成（缺什么补什么）

        public static Assets EnsureAll()
        {
            var a = new Assets();
            foreach (var d in new[] { ClipDir, ControllerDir, MotionDir }) EnsureFolder(d);

            foreach (var src in Unit07AnimAudit.FbxClips())
            {
                var path = ClipPath(src.name);
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing != null) { a.kept.Add(path); a.clips.Add(existing); continue; }
                var copy = CopyClip(src);
                AssetDatabase.CreateAsset(copy, path);
                WriteProvenance(path, src, copy);
                a.created.Add(path);
                a.clips.Add(copy);
            }

            var bodyMask = EnsureMask(BodyMaskPath, false, a);
            var rotorMask = EnsureMask(RotorMaskPath, true, a);
            a.controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (a.controller != null) a.kept.Add(ControllerPath);
            else { a.controller = CreateController(a.clips, bodyMask, rotorMask, EnsureRotorAngleClip(a)); a.created.Add(ControllerPath); }

            a.dock = EnsureConfig<Unit07DockMotionConfig>(DockMotionPath, a);
            a.rotor = EnsureConfig<Unit07RotorMotionConfig>(RotorMotionPath, a);
            a.firstOrder = EnsureConfig<FirstOrderMotionConfig>(FirstOrderMotionPath, a);
            a.workbench = EnsureConfig<WbPlaceholderMotionConfig>(WorkbenchMotionPath, a);
            AssetDatabase.SaveAssets();
            return a;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static T EnsureConfig<T>(string path, Assets a) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) { a.kept.Add(path); return existing; }
            var c = ScriptableObject.CreateInstance<T>();      // 字段初始值 = 原代码默认值
            AssetDatabase.CreateAsset(c, path);
            a.created.Add(path);
            return c;
        }

        // ------------------------------------------------------------------ 片段复制

        /// <summary>把 FBX 子片段复制成独立的 AnimationClip：全部浮点曲线（骨骼路径、属性名不变）、对象引用曲线、片段设置（循环等）、帧率、事件。</summary>
        public static AnimationClip CopyClip(AnimationClip src)
        {
            var dst = new AnimationClip { name = src.name };
            CopyInto(src, dst);
            return dst;
        }

        static void CopyInto(AnimationClip src, AnimationClip dst)
        {
            dst.ClearCurves();
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(dst)) AnimationUtility.SetObjectReferenceCurve(dst, b, null);
            dst.legacy = false;
            dst.frameRate = src.frameRate;
            dst.wrapMode = src.wrapMode;
            var bindings = AnimationUtility.GetCurveBindings(src);
            var curves = bindings.Select(b => AnimationUtility.GetEditorCurve(src, b)).ToArray();
            AnimationUtility.SetEditorCurves(dst, bindings, curves);
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(src))
                AnimationUtility.SetObjectReferenceCurve(dst, b, AnimationUtility.GetObjectReferenceCurve(src, b));
            AnimationUtility.SetAnimationClipSettings(dst, AnimationUtility.GetAnimationClipSettings(src));
            AnimationUtility.SetAnimationEvents(dst, AnimationUtility.GetAnimationEvents(src));
            dst.localBounds = src.localBounds;
        }

        /// <summary>曲线内容的指纹（绑定、关键帧时间 / 值 / 切线、循环设置、事件）。用来判断副本有没有被手调、FBX 有没有更新。</summary>
        public static string ClipHash(AnimationClip c)
        {
            var sb = new StringBuilder();
            var s = AnimationUtility.GetAnimationClipSettings(c);
            sb.Append($"loop={s.loopTime};fr={c.frameRate:R};");
            foreach (var b in AnimationUtility.GetCurveBindings(c).OrderBy(b => b.path).ThenBy(b => b.propertyName))
            {
                sb.Append(b.path).Append('|').Append(b.propertyName).Append(':');
                foreach (var k in AnimationUtility.GetEditorCurve(c, b).keys)
                    sb.Append($"{k.time:R},{k.value:R},{k.inTangent:R},{k.outTangent:R};");
            }
            foreach (var e in AnimationUtility.GetAnimationEvents(c)) sb.Append($"ev:{e.time:R}:{e.functionName};");
            using (var md5 = MD5.Create())
                return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 16);
        }

        [Serializable]
        public class Provenance
        {
            public string sourceFbx;
            public string sourceGuid;
            public string sourceClip;
            public string sourceHash;   // 复制（或恢复）时 FBX 片段的指纹
            public string copyHash;     // 复制（或恢复）时副本的指纹
            public string copiedAt;
        }

        static void WriteProvenance(string path, AnimationClip src, AnimationClip copy)
        {
            var p = new Provenance
            {
                sourceFbx = RobotFbx, sourceGuid = AssetDatabase.AssetPathToGUID(RobotFbx), sourceClip = src.name,
                sourceHash = ClipHash(src), copyHash = ClipHash(copy), copiedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            var imp = AssetImporter.GetAtPath(path);
            imp.userData = JsonUtility.ToJson(p);
            imp.SaveAndReimport();
        }

        public static Provenance ReadProvenance(AnimationClip copy)
        {
            var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(copy));
            if (imp == null || string.IsNullOrEmpty(imp.userData)) return null;
            try { return JsonUtility.FromJson<Provenance>(imp.userData); } catch { return null; }
        }

        public struct ClipState { public string name, state; public bool handEdited, sourceChanged; }

        /// <summary>每个可编辑片段：是否手调过（与复制时不同）、FBX 原版是否已更新（与复制时不同）。</summary>
        public static List<ClipState> ClipStatus()
        {
            var src = Unit07AnimAudit.FbxClips().ToDictionary(c => c.name);
            var list = new List<ClipState>();
            foreach (var name in src.Keys)
            {
                var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(name));
                if (copy == null) { list.Add(new ClipState { name = name, state = "还没有生成副本" }); continue; }
                var p = ReadProvenance(copy);
                bool edited = p == null || ClipHash(copy) != p.copyHash;
                bool changed = p != null && ClipHash(src[name]) != p.sourceHash;
                list.Add(new ClipState
                {
                    name = name, handEdited = edited, sourceChanged = changed,
                    state = (edited ? "已手调（与 FBX 原版不同）" : "与 FBX 原版相同") + (changed ? "；FBX 里的原版已更新，副本没有自动跟随" : "")
                });
            }
            return list;
        }

        /// <summary>
        /// 把可编辑片段恢复为 FBX 原版：先把当前内容备份到 Backups/时间戳/，再就地替换曲线和设置（文件和 GUID 不变，控制器引用不断）。返回备份路径。
        /// </summary>
        public static string RestoreFromFbx(AnimationClip copy)
        {
            var path = AssetDatabase.GetAssetPath(copy);
            var src = Unit07AnimAudit.FbxClips().FirstOrDefault(c => c.name == copy.name)
                      ?? throw new InvalidOperationException($"FBX 里没有名为 {copy.name} 的片段");
            var dir = $"{BackupDir}/{DateTime.Now:yyyyMMdd_HHmmss}";
            EnsureFolder(dir);
            var backup = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{copy.name}.anim");
            if (!AssetDatabase.CopyAsset(path, backup)) throw new IOException("备份失败：" + backup);
            var bimp = AssetImporter.GetAtPath(backup);
            if (bimp != null) { bimp.userData = ""; bimp.SaveAndReimport(); }   // 备份不算可编辑副本
            Undo.RecordObject(copy, "恢复 FBX 原版");
            CopyInto(src, copy);
            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssets();
            WriteProvenance(path, src, copy);
            return backup;
        }

        // ------------------------------------------------------------------ 遮罩与控制器

        /// <summary>七号转子骨骼（相对七号根节点的路径），以停靠预制体上 RotorPowerDriver 的配置为准。</summary>
        public static string[] RotorPaths()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefab);
            var driver = prefab != null ? prefab.GetComponent<RotorPowerDriver>() : null;
            if (driver == null || driver.Rotors.Length == 0) throw new InvalidOperationException($"{RobotPrefab} 上没有配置 RotorPowerDriver 的转子骨骼");
            return driver.Rotors.Select(r => Unit07AnimAudit.PathOf(r, prefab.transform)).ToArray();
        }

        static AvatarMask EnsureMask(string path, bool rotorsOnly, Assets a)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (existing != null) { a.kept.Add(path); return existing; }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(RobotFbx);
            var rotors = new HashSet<string>(RotorPaths());
            var mask = new AvatarMask();
            mask.AddTransformPath(model.transform, true);
            for (int i = 0; i < mask.transformCount; i++)
            {
                bool isRotor = rotors.Contains(mask.GetTransformPath(i));
                mask.SetTransformActive(i, rotorsOnly ? isRotor : !isRotor);
            }
            AssetDatabase.CreateAsset(mask, path);
            a.created.Add(path);
            return mask;
        }

        /// <summary>
        /// 生成的辅助片段（不是 FBX 动作，不要手调）：1 秒内把两根转子从静止姿态（Idle_Hover 第 0 帧）绕实测转轴转 0 → 360°。
        /// 落座后 RotorPowerDriver 只算转角，用 Animator.Play 把 Rotor_Angle 状态定位到“转角 / 360°”，由 Animator 写转子骨骼。
        /// 每 2° 一个关键帧、线性切线：两帧之间的插值误差远小于 0.01°。
        /// </summary>
        public static AnimationClip EnsureRotorAngleClip(Assets a)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(RotorAngleClipPath);
            if (existing != null) { a?.kept.Add(RotorAngleClipPath); return existing; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefab);
            var driver = prefab.GetComponent<RotorPowerDriver>();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(RobotFbx);
            var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(RotorAngleClipPath), frameRate = 180f };
            var paths = RotorPaths();
            for (int r = 0; r < paths.Length; r++)
            {
                var rest = model.transform.Find(paths[r]).localRotation;
                var axis = driver.LocalAxes[r];
                var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                Quaternion prev = rest;
                for (int k = 0; k <= 180; k++)
                {
                    var q = rest * Quaternion.AngleAxis(k * 2f, axis);
                    if (Quaternion.Dot(prev, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);   // 保持符号连续
                    prev = q;
                    float t = k / 180f;
                    curves[0].AddKey(t, q.x); curves[1].AddKey(t, q.y); curves[2].AddKey(t, q.z); curves[3].AddKey(t, q.w);
                }
                string[] props = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
                for (int c = 0; c < 4; c++)
                {
                    for (int k = 0; k < curves[c].length; k++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curves[c], k, AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyRightTangentMode(curves[c], k, AnimationUtility.TangentMode.Linear);
                    }
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(paths[r], typeof(Transform), props[c]), curves[c]);
                }
            }
            var s = AnimationUtility.GetAnimationClipSettings(clip);
            s.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, s);
            AssetDatabase.CreateAsset(clip, RotorAngleClipPath);
            a?.created.Add(RotorAngleClipPath);
            return clip;
        }

        /// <summary>
        /// 可编辑控制器：Base Layer = 9 个可编辑片段（状态名与原控制器相同，默认 Idle_Hover），遮罩去掉两根转子骨骼；
        /// Rotors 层（覆盖，权重 1，只含转子骨骼）：Rotor_Spin 播 Idle_Hover 的转子转动（悬停时），
        /// Rotor_Angle 播生成的转角片段、速度 0，由 RotorPowerDriver 每帧定位时刻（落座后）。
        /// 这样落座前后转子骨骼都只由 Animator 写，脚本只算转角。
        /// </summary>
        static AnimatorController CreateController(List<AnimationClip> clips, AvatarMask bodyMask, AvatarMask rotorMask, AnimationClip rotorAngle)
        {
            var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var layers = ac.layers;
            layers[0].avatarMask = bodyMask;
            var sm = layers[0].stateMachine;
            foreach (var c in clips.OrderBy(c => c.name))
            {
                var st = sm.AddState(c.name);
                st.motion = c;
                st.writeDefaultValues = true;
            }
            sm.defaultState = sm.states.First(s => s.state.name == DefaultState).state;
            ac.layers = layers;

            ac.AddLayer(RotorLayer);
            layers = ac.layers;
            var rl = layers[1];
            rl.avatarMask = rotorMask;
            rl.defaultWeight = 1f;
            rl.blendingMode = AnimatorLayerBlendingMode.Override;
            var spin = rl.stateMachine.AddState(RotorSpinState);
            spin.motion = clips.First(c => c.name == DefaultState);
            spin.writeDefaultValues = true;
            var angleState = rl.stateMachine.AddState(RotorAngleState);
            angleState.motion = rotorAngle;
            angleState.speed = 0f;                 // 时刻完全由 RotorPowerDriver 定位，Animator 自己不推进
            angleState.writeDefaultValues = true;
            rl.stateMachine.defaultState = spin;
            ac.layers = layers;
            EditorUtility.SetDirty(ac);
            return ac;
        }
    }
}
