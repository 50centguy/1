using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Dock;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.Motion.EditorTools
{
    /// <summary>
    /// 给图文说明截图（带界面的编辑器，不要 -batchmode）：
    /// Unity.exe -projectPath &lt;项目&gt; -executeMethod BorderRepair.Motion.EditorTools.Unit07AnimGuideCapture.Begin
    /// 依次在 Project / Inspector / 预览窗口 / Animation 窗口 / Scene 视图里摆好要讲的内容并截图到 Docs/Integration/Unit07AnimEdit/Guide/。
    /// 只读：预览会还原、不保存场景、不改资源。截完自动退出。
    /// </summary>
    public static class Unit07AnimGuideCapture
    {
        const string OutDir = "Docs/Integration/Unit07AnimEdit/Guide";
        static readonly List<(int wait, Action act)> steps = new List<(int, Action)>();
        static int index, frames;
        static readonly List<string> log = new List<string>();

        public static void Begin()
        {
            Directory.CreateDirectory(OutDir);
            EditorSceneManager.OpenScene("Assets/BorderRepair/FirstOrder/Scenes/Unit07FirstOrder_Test.unity", OpenSceneMode.Single);
            var dockCfg = AssetDatabase.LoadAssetAtPath<Object>(Unit07EditableAnimation.DockMotionPath);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Unit07EditableAnimation.ClipPath("Gripper_OpenClose_R"));
            var dock = Object.FindFirstObjectByType<Unit07DockController>();
            var robot = dock.RobotRoot.gameObject;

            void S(int wait, Action a) => steps.Add((wait, a));
            S(60, () =>
            {
                var main = EditorGUIUtility.GetMainWindowPosition();
                EditorGUIUtility.SetMainWindowPosition(new Rect(main.x, main.y, Mathf.Max(main.width, 1600), Mathf.Max(main.height, 950)));
            });
            // 1. Project 窗口：资产在哪
            // URP 模板的 Readme 会在启动后自动选中一次：多等一会儿再选，截图前再选一次
            S(240, () =>
            {
                var so = new SerializedObject(dockCfg);
                foreach (var p in new[] { "clamps", "descend", "lever" }) so.FindProperty(p).isExpanded = true;   // 展开，让截图里看得到时长和曲线
                var pb = Win("ProjectBrowser");
                pb?.Focus();                              // 先聚焦再选：聚焦 Project 窗口会恢复它自己上次的选择
                var folder = AssetDatabase.LoadAssetAtPath<Object>(Unit07EditableAnimation.MotionDir);
                pb?.GetType().GetMethod("ShowFolderContents", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                                        null, new[] { typeof(int), typeof(bool) }, null)?.Invoke(pb, new object[] { folder.GetInstanceID(), true });
                Selection.activeObject = dockCfg; EditorGUIUtility.PingObject(dockCfg);
            });
            S(30, () => { Selection.activeObject = dockCfg; });
            // 每个窗口截图前先聚焦、等它重画（没聚焦的窗口不重画，截到的是旧画面）
            S(10, () => Win("ProjectBrowser")?.Focus());
            S(40, () => Shot("ProjectBrowser", "G01_project_motion_folder"));
            S(10, () => { Selection.activeObject = dockCfg; Win("InspectorWindow")?.Focus(); });
            S(40, () => Shot("InspectorWindow", "G02_dock_motion_config_inspector"));
            // 2. Hierarchy 选维修座流程对象：Inspector 里看到配置引用
            S(20, () => { Selection.activeGameObject = dock.gameObject; EditorGUIUtility.PingObject(dock.gameObject); });
            S(10, () => Win("SceneHierarchyWindow")?.Focus());
            S(40, () => Shot("SceneHierarchyWindow", "G03_hierarchy_dockflow"));
            S(10, () => { Selection.activeGameObject = dock.gameObject; Win("InspectorWindow")?.Focus(); });
            S(40, () => Shot("InspectorWindow", "G04_dock_controller_inspector"));
            // 3. 预览窗口：夹具张开到一半
            S(20, () =>
            {
                Unit07MotionPreviewWindow.Open();
                Unit07MotionPreview.Begin(Unit07MotionPreview.Kind.DockClamps);
                Unit07MotionPreview.Sample(0.5f);
                var sv = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
                sv.Focus();
                sv.drawGizmos = false;   // 灯光图标会挡住夹具
                var pose = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == "CamPose_Dock");
                if (pose != null) sv.AlignViewToObject(pose); else sv.Frame(new Bounds(dock.ClampL.position * 0.5f + dock.ClampR.position * 0.5f, Vector3.one * 0.55f), true);
            });
            S(60, () => { Shot("Unit07MotionPreviewWindow", "G05_preview_window"); Shot("SceneView", "G06_scene_clamps_half_open_preview"); });
            S(10, () => { Unit07MotionPreview.Sample(0f); });
            S(30, () => { Shot("SceneView", "G06a_scene_clamps_closed_preview"); Unit07MotionPreview.Sample(1f); });
            S(30, () => { Shot("SceneView", "G06b_scene_clamps_fully_open_preview"); Unit07MotionPreview.End(); });
            // 4. Animation 窗口：七号右夹爪片段
            S(20, () =>
            {
                Selection.activeGameObject = robot;
                var aw = EditorWindow.GetWindow<AnimationWindow>();
                aw.Focus();
                aw.position = new Rect(120, 120, 1300, 620);
                aw.animationClip = clip;
                aw.previewing = true;
                aw.time = 0.8f;
                var jaw = robot.GetComponentsInChildren<Transform>().First(t => t.name == "Arm_R_JawUpper" && t.childCount > 0);
                var sv = SceneView.lastActiveSceneView;
                sv.drawGizmos = false;
                sv.Frame(new Bounds(jaw.position, Vector3.one * 0.35f), true);
            });
            S(60, () => { Shot("AnimationWindow", "G07_animation_window_gripper_r"); Shot("SceneView", "G08_scene_gripper_r_at_0p8s"); });
            S(20, () =>
            {
                var aw = EditorWindow.GetWindow<AnimationWindow>();
                aw.previewing = false;
                log.Add($"Animation 窗口：片段 {aw.animationClip?.name}，可编辑 {aw.animationClip != null && !AssetDatabase.IsSubAsset(aw.animationClip)}");
            });
            S(20, () =>
            {
                File.WriteAllLines(Path.Combine(OutDir, "capture_log.txt"), log);
                EditorApplication.update -= Tick;
                EditorApplication.Exit(0);
            });
            index = 0; frames = 0;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (index >= steps.Count) return;
            if (++frames < steps[index].wait) { InternalEditorUtility.RepaintAllViews(); return; }
            frames = 0;
            try { steps[index].act(); }
            catch (Exception e) { log.Add($"步骤 {index} 异常：{e}"); }
            index++;
        }

        static void FlipRows(Texture2D t)
        {
            var px = t.GetPixels();
            int w = t.width, h = t.height;
            var o = new Color[px.Length];
            for (int y = 0; y < h; y++) Array.Copy(px, y * w, o, (h - 1 - y) * w, w);
            t.SetPixels(o);
        }

        static EditorWindow Win(string typeName) =>
            Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType().Name == typeName);

        /// <summary>
        /// 只截 Unity 窗口自己的内容：用编辑器内部的 GUIView.GrabPixels 把该窗口画进 RenderTexture，
        /// 不读桌面像素，所以不会截到其它程序、任务栏或通知。拿不到这个内部方法时不截图（不退回读屏幕）。
        /// </summary>
        static void Shot(string typeName, string file)
        {
            var w = Win(typeName);
            if (w == null) { log.Add($"{file}：找不到窗口 {typeName}"); return; }
            // 先同步重画一次窗口，避免截到上一次的画面
            typeof(EditorWindow).GetMethod("RepaintImmediately", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.Invoke(w, null);
            var host = typeof(EditorWindow).GetField("m_Parent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(w);
            var grab = host?.GetType().GetMethod("GrabPixels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                                                null, new[] { typeof(RenderTexture), typeof(Rect) }, null);
            if (grab == null) { log.Add($"{file}：拿不到 GUIView.GrabPixels，跳过（不读屏幕）"); return; }
            float s = EditorGUIUtility.pixelsPerPoint;
            int width = Mathf.RoundToInt(w.position.width * s), height = Mathf.RoundToInt(w.position.height * s);
            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            rt.Create();
            grab.Invoke(host, new object[] { rt, new Rect(0, 0, width, height) });
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            FlipRows(tex);   // GrabPixels 得到的行序是倒的
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(OutDir, file + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            log.Add($"{file}：{typeName}，{width}×{height}");
        }
    }
}
