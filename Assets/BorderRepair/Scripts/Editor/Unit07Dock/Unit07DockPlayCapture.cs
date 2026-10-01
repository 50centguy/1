using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Dock;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 带界面的编辑器 Play 模式采集：接入前后的渲染统计与帧时间、停靠流程截图、断电前后转子转速、控制台错误。
    /// 命令行（不要 -batchmode）：Unity.exe -projectPath &lt;项目&gt; -executeMethod BorderRepair.EditorTools.Unit07DockPlayCapture.Begin
    /// 第二阶段起还采集维修结束后的离座流程（确认结束 → 恢复供电 → 松开夹具 → 升起离座 → 转子交还 Animator）。
    /// 结果写到 Docs/Integration/Unit07Dock_Phase2/（第一阶段的记录保留在 Unit07Dock_Phase1/，不覆盖）。只读取场景，不保存场景、不改资源。
    /// </summary>
    [InitializeOnLoad]
    public static class Unit07DockPlayCapture
    {
        const string KeyActive = "Unit07DockPlayCapture.Active";
        const string KeyExit = "Unit07DockPlayCapture.Exit";
        public const string Phase2ReportDir = "Docs/Integration/Unit07Dock_Phase2";
        static string OutDir => Path.GetFullPath(Phase2ReportDir);

        static Unit07DockPlayCapture() { EditorApplication.playModeStateChanged += OnPlayMode; }

        public static void Begin()
        {
            Directory.CreateDirectory(Path.Combine(OutDir, "Screenshots"));
            SessionState.SetBool(KeyActive, true);
            EditorSceneManager.OpenScene(Unit07DockBuilder.ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (!SessionState.GetBool(KeyActive, false)) return;
            if (s == PlayModeStateChange.EnteredPlayMode) Runner.Start(OutDir);
            if (s == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(KeyActive, false);
                EditorApplication.Exit(SessionState.GetInt(KeyExit, 4));
            }
        }

        static class Runner
        {
            static string outDir;
            static readonly StringBuilder log = new StringBuilder();
            static readonly List<string> console = new List<string>();
            static readonly List<(float at, Action act)> steps = new List<(float, Action)>();
            static int next;
            static float t0;
            static Quaternion measureRotor;
            static float measureT;
            static Unit07DockController dock;
            static GameObject dockRoot, robotRoot;
            static Camera cam;
            static Camera cap;
            static RenderTexture rt;
            static bool measuring;
            static string measureLabel;
            static int measureWarm;
            static readonly List<float> frameMs = new List<float>(), mainMs = new List<float>();
            static readonly List<long> draws = new List<long>(), batches = new List<long>(), setpass = new List<long>(), tris = new List<long>();
            static ProfilerRecorder rDraw, rBatch, rSetPass, rTris, rMain;
            static readonly Dictionary<string, string> perf = new Dictionary<string, string>();

            static void Note(string s) { log.AppendLine(s); Debug.Log("[Unit07DockCapture] " + s); }

            public static void Start(string dir)
            {
                outDir = dir;
                Application.logMessageReceived += (m, st, type) =>
                {
                    if (m.StartsWith("[Unit07DockCapture]")) return;
                    if (type != LogType.Log) console.Add($"{type}: {m.Split('\n')[0]}");
                };
                dock = Object.FindFirstObjectByType<Unit07DockController>();
                dockRoot = GameObject.Find("Unit07ServiceDock");
                robotRoot = dock.RobotRoot.gameObject;
                cam = Camera.main;
                rDraw = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                rBatch = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                rSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                rTris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                rMain = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
                cap = new GameObject("CaptureCam").AddComponent<Camera>();
                cap.CopyFrom(cam);
                cap.enabled = false;
                rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
                cap.targetTexture = rt;
                Note($"进入 Play 模式：Unity {Application.unityVersion}，Game 视图 {Screen.width}×{Screen.height}，图形 API {SystemInfo.graphicsDeviceType}，GPU {SystemInfo.graphicsDeviceName}");

                float T = 0f;
                void At(float dt, Action a) { T += dt; steps.Add((T, a)); }
                // 1. 接入前后的渲染统计（同一场景、同一镜头；七号悬停）
                At(0.5f, () => { dockRoot.SetActive(false); robotRoot.SetActive(false); Measure("C_empty_scene"); });
                At(4.0f, () => { robotRoot.SetActive(true); Measure("A_before_robot_only"); });
                At(4.0f, () => { dockRoot.SetActive(true); Measure("B_after_robot_plus_dock"); });
                // 2. 停靠流程截图与转子转速
                At(4.0f, () => { Shot("P01_hovering_power_on"); Rotor("悬停通电"); ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "Screenshots", "P00_game_view_hud_hovering.png")); });
                At(0.3f, () => Act(DockAction.Clamps));
                At(0.35f, () => Shot("P02_clamps_opening_descending"));
                At(1.8f, () => { Shot("P03_seated_clamps_open"); Rotor("落座通电"); Shot("P03b_rear_clamps_open", new Vector3(-0.75f, 1.2f, -1.15f), new Vector3(0f, 0.98f, -0.2f)); });
                At(0.3f, () => Act(DockAction.Clamps));
                At(0.9f, () => { Shot("P04_clamped"); Shot("P04b_rear_clamps_closed", new Vector3(-0.75f, 1.2f, -1.15f), new Vector3(0f, 0.98f, -0.2f)); Act(DockAction.EngineLeft); });
                At(0.3f, () => Act(DockAction.PowerSwitch));
                At(0.5f, () => { Shot("P05_power_off_spinning_down"); Rotor("断电 0.5 s"); Act(DockAction.EngineLeft); Shot("P05b_switch_off_closeup", new Vector3(-0.85f, 0.95f, 0.95f), new Vector3(-0.44f, 0.74f, 0.22f)); });
                At(1.0f, () => Rotor("断电 1.5 s"));
                At(2.0f, () => { Rotor("断电 3.5 s"); Note($"状态：{dock.State}"); });
                At(0.3f, () => { Act(DockAction.EngineLeft); Shot("P06_rotors_stopped_inspect_allowed"); Shot("P06b_engine_outlet_stopped", new Vector3(-0.6f, 0.55f, 0.55f), new Vector3(-0.36f, 1.0f, 0f)); ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "Screenshots", "P06c_game_view_hud_stopped.png")); });
                At(0.3f, () => { var tray = Object.FindObjectsByType<DockInteractable>(FindObjectsSortMode.None).First(d => d.action == DockAction.PartsTray); dock.Interact(DockAction.PartsTray, tray.pickable); Note(dock.LastMessage); });
                At(0.3f, () => Shot("P07_parts_tray_taken"));
                At(0.3f, () => { var tray = Object.FindObjectsByType<DockInteractable>(FindObjectsSortMode.None).First(d => d.action == DockAction.PartsTray); dock.Interact(DockAction.PartsTray, tray.pickable); });
                At(0.3f, () => { dock.RobotAnimator.Play("Gripper_OpenClose_R", 0, 0f); Note("断电状态下播放 Gripper_OpenClose_R"); });
                At(0.8f, () => { Shot("P08_power_off_gripper_animation_still_plays", new Vector3(0.9f, 0.95f, 1.1f), new Vector3(0.2f, 0.85f, 0.15f)); Rotor("断电播放夹爪动作"); });
                At(0.3f, () => Shot("P09_underside_tray_corridor", new Vector3(0.55f, 0.35f, 1.05f), new Vector3(0f, 0.88f, 0f)));
                // 第二阶段：维修结束后离座
                At(0.3f, () => Act(DockAction.PowerSwitch));    // 未确认维修结束：应被拒绝
                At(0.3f, () => { ((ManualServiceCompletionGate)dock.ServiceGate).Confirm(); Note("【占位】测试场景手动确认：维修已结束（未接工单）"); Act(DockAction.PowerSwitch); });
                At(0.4f, () => { Rotor("恢复供电 0.4 s（加速中）"); Shot("P10_power_on_spinning_up"); Act(DockAction.Clamps); });   // 加速中松夹具：应被拒绝
                At(1.0f, () => { Rotor("恢复供电 1.4 s"); Note($"状态：{dock.State}"); Act(DockAction.LiftOff); });              // 还夹着就离座：应被拒绝
                At(0.3f, () => Act(DockAction.Clamps));
                At(0.9f, () => { Shot("P11_clamps_released_still_seated"); Note($"状态：{dock.State}"); Act(DockAction.LiftOff); });
                At(0.6f, () => { Rotor("升起中"); Shot("P12_lifting_off"); });
                At(1.0f, () => { Rotor("离座后"); Note($"状态：{dock.State}，Animator 当前片段 Idle_Hover：{dock.RobotAnimator.GetCurrentAnimatorStateInfo(0).IsName("Idle_Hover")}");
                                 Shot("P13_undocked_animator_drives_rotors"); ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "Screenshots", "P13b_game_view_hud_undocked.png")); });
                At(0.5f, () => { measureRotor = dock.Rotors.Rotors[0].localRotation; measureT = Time.time; });
                At(0.1f, () => Note($"离座后转子实测转速（Animator 驱动，按骨骼实际旋转换算）：{Quaternion.Angle(measureRotor, dock.Rotors.Rotors[0].localRotation) / Mathf.Max(1e-4f, Time.time - measureT):F0}°/s"));
                At(0.3f, () => Act(DockAction.PowerSwitch));    // 离座后断电：应被拒绝
                At(0.5f, Finish);
                t0 = Time.realtimeSinceStartup;
                next = 0;
                EditorApplication.update += Tick;
            }

            static void Act(DockAction a)
            {
                bool ok = dock.Interact(a);
                Note($"操作 {a}：{(ok ? "执行" : "拒绝")} — {dock.LastMessage}（状态 {dock.State}）");
            }

            static void Rotor(string label)
            {
                var r = dock.Rotors;
                Note($"转子 · {label}：接管={r.Driven}，供电={r.Powered}，转速 {r.SpeedDegPerSec:F1}°/s，停稳={r.IsStopped}，叶轮可操作={dock.CanOperateImpeller}");
            }

            static void Measure(string label)
            {
                measuring = true; measureLabel = label; measureWarm = 45;
                frameMs.Clear(); mainMs.Clear(); draws.Clear(); batches.Clear(); setpass.Clear(); tris.Clear();
            }

            static void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                if (measuring)
                {
                    if (measureWarm-- > 0) return;
                    frameMs.Add(Time.unscaledDeltaTime * 1000f);
                    mainMs.Add(rMain.LastValue * 1e-6f);
                    draws.Add(rDraw.LastValue); batches.Add(rBatch.LastValue); setpass.Add(rSetPass.LastValue); tris.Add(rTris.LastValue);
                    if (frameMs.Count >= 120)
                    {
                        measuring = false;
                        string line = $"{measureLabel}: draw calls 中位 {Med(draws)}，batches {Med(batches)}，SetPass {Med(setpass)}，渲染三角面 {Med(tris)}，" +
                                      $"帧时间中位 {Med(frameMs):F2} ms / P95 {P95(frameMs):F2} ms，主线程中位 {Med(mainMs):F2} ms（120 帧，预热 45 帧）";
                        perf[measureLabel] = line;
                        Note(line);
                    }
                    return;
                }
                float now = Time.realtimeSinceStartup - t0;
                while (next < steps.Count && now >= steps[next].at && !measuring)
                {
                    try { steps[next].act(); }
                    catch (Exception e) { Note($"步骤 {next} 异常：{e.Message}"); console.Add("Exception: " + e.Message); }
                    next++;
                }
            }

            static long Med(List<long> v) { var s = v.OrderBy(x => x).ToList(); return s.Count == 0 ? 0 : s[s.Count / 2]; }
            static float Med(List<float> v) { var s = v.OrderBy(x => x).ToList(); return s.Count == 0 ? 0 : s[s.Count / 2]; }
            static float P95(List<float> v) { var s = v.OrderBy(x => x).ToList(); return s.Count == 0 ? 0 : s[Mathf.Min(s.Count - 1, (int)(s.Count * 0.95f))]; }

            static void Shot(string name, Vector3? pos = null, Vector3? target = null)
            {
                if (pos.HasValue) { cap.transform.position = pos.Value; cap.transform.LookAt(target.Value); }
                else { cap.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation); }
                cap.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(outDir, "Screenshots", name + ".png"), tex.EncodeToPNG());
                Object.Destroy(tex);
            }

            static void Finish()
            {
                EditorApplication.update -= Tick;
                Note($"控制台警告 / 错误 {console.Count} 条" + (console.Count > 0 ? "：" + string.Join(" | ", console.Distinct().Take(20)) : ""));
                File.WriteAllText(Path.Combine(outDir, "play_capture.txt"), log.ToString(), new UTF8Encoding(false));
                rDraw.Dispose(); rBatch.Dispose(); rSetPass.Dispose(); rTris.Dispose(); rMain.Dispose();
                if (rt != null) rt.Release();
                SessionState.SetInt(KeyExit, console.Any(c => c.StartsWith("Error") || c.StartsWith("Exception")) ? 3 : 0);
                EditorApplication.ExitPlaymode();
            }
        }
    }
}
