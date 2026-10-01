using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WorkbenchArea.EditorTools
{
    /// <summary>
    /// 带界面的编辑器 Play 模式采集：六个视角截图（正面、斜俯、玩家近景、侧面剖切、去杂物、游戏镜头）、占位交互状态截图、
    /// 完整场景与去杂物两种情况下的 draw calls / batches / SetPass / 三角面 / 帧时间、控制台错误。
    /// 命令行（不要 -batchmode）：Unity.exe -projectPath &lt;项目&gt; -executeMethod WorkbenchArea.EditorTools.WorkbenchAreaPlayCapture.Begin
    /// 结果写到 ArtSource/WorkbenchArea/Reports/Unity/。只读取场景，不保存场景。
    /// </summary>
    [InitializeOnLoad]
    public static class WorkbenchAreaPlayCapture
    {
        const string KeyActive = "WorkbenchAreaPlayCapture.Active";
        const string KeyExit = "WorkbenchAreaPlayCapture.Exit";
        static string OutDir => Path.GetFullPath(WorkbenchAreaBuilder.ReportDir);

        static WorkbenchAreaPlayCapture() { EditorApplication.playModeStateChanged += OnPlayMode; }

        [MenuItem("Workbench Area/Play Capture")]
        public static void Begin()
        {
            Directory.CreateDirectory(Path.Combine(OutDir, "Screenshots"));
            SessionState.SetBool(KeyActive, true);
            EditorSceneManager.OpenScene(WorkbenchAreaBuilder.ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (!SessionState.GetBool(KeyActive, false)) return;
            if (s == PlayModeStateChange.EnteredPlayMode) Runner.Start(OutDir);
            if (s == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(KeyActive, false);
                if (!Application.isBatchMode && Environment.GetCommandLineArgs().Contains("-executeMethod"))
                    EditorApplication.Exit(SessionState.GetInt(KeyExit, 4));
            }
        }

        static Vector3 B2U(float x, float y, float z) => WorkbenchAreaBuilder.B2U(x, y, z);
        static float FovFromLens(float mm) => 2f * Mathf.Atan(36f * 9f / 16f / 2f / mm) * Mathf.Rad2Deg;   // 与 Blender 渲染相同：36 mm 宽、16:9

        static class Runner
        {
            static string outDir;
            static readonly StringBuilder log = new StringBuilder();
            static readonly List<string> console = new List<string>();
            static readonly List<(float at, Action act)> steps = new List<(float, Action)>();
            static int next;
            static float t0;
            static WbCameraRig rig;
            static WbPlaceholderDemo demo;
            static GameObject area;
            static Camera cam, cap;
            static RenderTexture rt;
            static bool measuring;
            static string measureLabel;
            static int measureWarm;
            static float measureStart;
            static readonly List<float> frameMs = new List<float>(), mainMs = new List<float>();
            static readonly List<long> draws = new List<long>(), batches = new List<long>(), setpass = new List<long>(), tris = new List<long>();
            static ProfilerRecorder rDraw, rBatch, rSetPass, rTris, rMain;

            static void Note(string s) { log.AppendLine(s); Debug.Log("[WorkbenchAreaCapture] " + s); }

            static GameObject[] Clutter() => area.GetComponentsInChildren<WbPartProperties>(true).Where(p => p.Role == "clutter").Select(p => p.gameObject).ToArray();
            static void SetClutter(bool on) { foreach (var g in Clutter()) g.SetActive(on); }
            static readonly string[] Cutaway = { "Room_WallRight", "Storage_Shelving", "Clutter_Shelving", "Clutter_ShelvingCRT" };
            static void SetCutaway(bool hidden) { foreach (var n in Cutaway) WorkbenchAreaBuilder.Find(area.transform, n).gameObject.SetActive(!hidden); }

            public static void Start(string dir)
            {
                outDir = dir;
                Application.logMessageReceived += (m, st, type) =>
                {
                    if (m.StartsWith("[WorkbenchAreaCapture]")) return;
                    if (type != LogType.Log) console.Add($"{type}: {m.Split('\n')[0]}");
                };
                rig = Object.FindFirstObjectByType<WbCameraRig>();
                demo = Object.FindFirstObjectByType<WbPlaceholderDemo>();
                area = GameObject.Find("WorkbenchArea");
                cam = rig.Cam;
                rDraw = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                rBatch = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                rSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                rTris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                rMain = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
                cap = new GameObject("CaptureCam").AddComponent<Camera>();
                cap.CopyFrom(cam);
                UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cap).renderPostProcessing = true;   // CopyFrom 不复制 URP 的后处理开关；截图要和游戏画面一致
                cap.enabled = false;
                rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 };
                cap.targetTexture = rt;
                Note($"进入 Play 模式：Unity {Application.unityVersion}，Game 视图 {Screen.width}×{Screen.height}，图形 API {SystemInfo.graphicsDeviceType}，GPU {SystemInfo.graphicsDeviceName}");

                float T = 0f;
                void At(float dt, Action a) { T += dt; steps.Add((T, a)); }
                // 1. 渲染统计：游戏镜头，完整场景 / 去杂物 / 近距镜头
                At(2.5f, () => { rig.SetView(WbCameraRig.View.Game); Measure("A_game_camera_full_scene"); });
                At(3.0f, () => { SetClutter(false); Measure("B_game_camera_clutter_hidden"); });
                At(3.0f, () => { SetClutter(true); rig.SetView(WbCameraRig.View.CloseUp); Measure("C_closeup_camera_full_scene"); });
                // 2. 六个视角（与 Blender 渲染 R01–R06 同机位）
                At(3.0f, () =>
                {
                    rig.SetView(WbCameraRig.View.Game);
                    Shot("U01_front", B2U(0f, -2.6f, 1.35f), B2U(0f, 0.6f, 1.15f), FovFromLens(30f));
                    Shot("U02_oblique_top", B2U(1.6f, -1.6f, 2.6f), B2U(0f, 0.45f, 0.95f), FovFromLens(28f));
                    Shot("U03_player_closeup", WorkbenchAreaBuilder.CloseCamPos, WorkbenchAreaBuilder.CloseCamTarget, WorkbenchAreaBuilder.CloseFov);
                    SetCutaway(true);
                    Shot("U04_side_cutaway", B2U(3.4f, -0.35f, 1.25f), B2U(0f, 0.25f, 0.95f), FovFromLens(30f));
                    SetCutaway(false);
                    SetClutter(false);
                    Shot("U05_clutter_hidden_work_area", B2U(1.6f, -1.6f, 2.6f), B2U(0f, 0.45f, 0.95f), FovFromLens(28f));
                    SetClutter(true);
                    Shot("U06_game_camera", WorkbenchAreaBuilder.GameCamPos, WorkbenchAreaBuilder.GameCamTarget, WorkbenchAreaBuilder.GameFov);
                    ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "Screenshots", "U00_game_view_with_hud.png"));
                });
                // 3. 占位交互状态
                At(0.5f, () => { demo.StartCoroutine(demo.Disassemble()); Note("【占位】开始拆下演示"); });
                At(6.5f, () =>
                {
                    Note($"【占位】拆下演示完成：{demo.Disassembled}");
                    Shot("U07_placeholder_disassembled_closeup", WorkbenchAreaBuilder.CloseCamPos, WorkbenchAreaBuilder.CloseCamTarget, WorkbenchAreaBuilder.CloseFov);
                    demo.StartCoroutine(demo.ToggleTray(demo.TrayOldParts));
                });
                At(1.2f, () =>
                {
                    Note($"【占位】旧件托盘已取出：{demo.IsTaken(demo.TrayOldParts)}");
                    Shot("U08_placeholder_tray_taken_game", WorkbenchAreaBuilder.GameCamPos, WorkbenchAreaBuilder.GameCamTarget, WorkbenchAreaBuilder.GameFov);
                    Shot("U09_placeholder_tray_taken_side", B2U(1.5f, -1.0f, 1.45f), B2U(0.3f, 0.3f, 0.95f), 45f);
                });
                At(0.3f, () => { demo.StartCoroutine(demo.ToggleUpperTier()); Note("【占位】移开工具箱上层"); });
                At(1.0f, () =>
                {
                    Note($"【占位】上层已移开：{demo.UpperTierMoved}，下层被挡住：{demo.ToolboxLower.IsBlocked}");
                    Shot("U10_placeholder_toolbox_upper_moved", B2U(0.45f, -0.35f, 1.55f), B2U(0.73f, 0.70f, 1.0f), 45f);
                });
                At(0.5f, Finish);
                t0 = Time.realtimeSinceStartup;
                next = 0;
                EditorApplication.update += Tick;
            }

            static void Measure(string label)
            {
                measuring = true; measureLabel = label; measureWarm = 45; measureStart = Time.realtimeSinceStartup;
                frameMs.Clear(); mainMs.Clear(); draws.Clear(); batches.Clear(); setpass.Clear(); tris.Clear();
            }

            static void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                if (measuring)
                {
                    if (measureWarm-- > 0) return;
                    bool timedOut = Time.realtimeSinceStartup - measureStart > 20f;   // 编辑器失焦被节流时不无限等待
                    if (!timedOut && (rDraw.LastValue == 0 || Time.unscaledDeltaTime > 0.25f)) return;   // 编辑器未真正渲染的帧（失焦节流、启动卡顿）不计入
                    if (!timedOut)
                    {
                        frameMs.Add(Time.unscaledDeltaTime * 1000f);
                        mainMs.Add(rMain.LastValue * 1e-6f);
                        draws.Add(rDraw.LastValue); batches.Add(rBatch.LastValue); setpass.Add(rSetPass.LastValue); tris.Add(rTris.LastValue);
                    }
                    if (frameMs.Count >= 120 || timedOut)
                    {
                        measuring = false;
                        Note($"{measureLabel}: draw calls 中位 {Med(draws)}，batches {Med(batches)}，SetPass {Med(setpass)}，渲染三角面 {Med(tris)}，" +
                             $"帧时间中位 {Med(frameMs):F2} ms / P95 {P95(frameMs):F2} ms，主线程中位 {Med(mainMs):F2} ms（{frameMs.Count} 帧{(frameMs.Count < 120 ? "，编辑器被节流，样本不足" : "")}，预热 45 帧）");
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

            static void Shot(string name, Vector3 pos, Vector3 target, float fov)
            {
                cap.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
                cap.fieldOfView = fov;
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
