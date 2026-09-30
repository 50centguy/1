using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RobotV4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// V4 机器人 Play 模式检查：打开验收场景 → 进入 Play 模式 → 由场景里的 Animator 实时播放各动作
/// （不采样、不强制剔除模式），按时间点截图并记录关节角度、控制台警告与错误 → 退出 Play 模式。
/// 命令行（带界面启动编辑器，不用 -batchmode）：
///   Unity.exe -projectPath <项目> -executeMethod RobotV4PlayCheck.Begin -robotOut <输出目录>
/// 只读取场景与模型，不保存场景、不改任何资源。
/// </summary>
[InitializeOnLoad]
public static class RobotV4PlayCheck
{
    const string ScenePath = "Assets/RobotV4/Scenes/RobotV4_Acceptance.unity";
    const string KeyActive = "RobotV4PlayCheck.Active";
    const string KeyOut = "RobotV4PlayCheck.Out";
    const string KeyExit = "RobotV4PlayCheck.ExitCode";

    static RobotV4PlayCheck() { EditorApplication.playModeStateChanged += OnPlayModeChanged; }

    public static void Begin()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-robotOut");
        string outDir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.GetFullPath("Temp/RobotV4PlayCheck");
        Directory.CreateDirectory(outDir);
        SessionState.SetBool(KeyActive, true);
        SessionState.SetString(KeyOut, outDir);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange s)
    {
        if (!SessionState.GetBool(KeyActive, false)) return;
        if (s == PlayModeStateChange.EnteredPlayMode) Runner.Start(SessionState.GetString(KeyOut, "Temp"));
        if (s == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(KeyActive, false);
            int code = SessionState.GetInt(KeyExit, 4);
            Debug.Log("[V4Play] 已退出 Play 模式，关闭编辑器，退出码 " + code);
            EditorApplication.Exit(code);
        }
    }

    // Play 模式里按时间推进的检查步骤（Play 期间不会再发生域重载，静态状态可用）
    static class Runner
    {
        static string outDir;
        static readonly StringBuilder log = new StringBuilder();
        static readonly List<string> problems = new List<string>();
        static readonly List<string> console = new List<string>();
        static readonly List<(float at, Action act)> steps = new List<(float, Action)>();
        static int next;
        static float t0;
        static GameObject robot;
        static Animator anim;
        static RobotScreenFace face;
        static Camera cap;
        static RenderTexture rt;
        static Quaternion jawClosed, rotorA;
        static float rotorAt;
        static Vector3 front = Vector3.forward, right = Vector3.right, center;

        static void Note(string s) { log.AppendLine(s); Debug.Log("[V4Play] " + s); }
        static void Problem(string s) { problems.Add(s); Note("PROBLEM " + s); }
        static Transform Bone(string n) => robot.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
        static Renderer Mesh(string n) => robot.GetComponentsInChildren<Renderer>(true).First(r => r.name == n);

        public static void Start(string dir)
        {
            outDir = dir;
            Application.logMessageReceived += (msg, st, type) =>
            {
                if (msg.StartsWith("[V4Play]")) return;
                if (type != LogType.Log) console.Add($"{type}: {msg.Split('\n')[0]}");
            };
            robot = GameObject.Find("Robot_V4");
            if (robot == null) { Problem("场景里没有 Robot_V4"); Finish(); return; }
            anim = robot.GetComponent<Animator>();
            face = robot.GetComponent<RobotScreenFace>();
            center = robot.transform.position + Vector3.up * 0.32f;
            Note($"进入 Play 模式：场景 {EditorSceneManager.GetActiveScene().path}，Unity {Application.unityVersion}，" +
                 $"Animator 剔除模式 {anim.cullingMode}，控制器 {anim.runtimeAnimatorController.name}，主相机 {(Camera.main != null ? Camera.main.name : "无")}");
            cap = new GameObject("PlayCheckCapture").AddComponent<Camera>();
            cap.enabled = false;
            cap.fieldOfView = 30f; cap.nearClipPlane = 0.01f;
            cap.clearFlags = CameraClearFlags.SolidColor; cap.backgroundColor = new Color(0.30f, 0.32f, 0.36f);
            rt = new RenderTexture(1280, 960, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cap.targetTexture = rt;

            var eng = Mesh("Engine_UpperCover_R").bounds.center;
            float T = 0f;
            void At(float dt, Action a) { T += dt; steps.Add((T, a)); }

            // 1. 默认状态：Idle_Hover 自动播放
            At(1.5f, () =>
            {
                var st = anim.GetCurrentAnimatorStateInfo(0);
                Note($"默认状态 Idle_Hover：{st.IsName("Idle_Hover")}，normalizedTime {st.normalizedTime:F2}，已播放 {Time.time - t0:F2} s，帧率约 {1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):F0} fps");
                if (!st.IsName("Idle_Hover")) Problem("进入 Play 后默认状态不是 Idle_Hover");
                jawClosed = Bone("Arm_R_JawUpper").localRotation;
                rotorA = Bone("Engine_R_Rotor").localRotation; rotorAt = Time.time;
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "P00_game_view_main_camera.png"));
                Shot("P01_idle_front", front, center, 2.4f);
            });
            At(0.2f, () =>
            {
                float dt = Time.time - rotorAt;
                float ang = Quaternion.Angle(rotorA, Bone("Engine_R_Rotor").localRotation);
                float expect = 360f * dt % 360f; if (expect > 180f) expect = 360f - expect;
                Note($"转子实时转速：{dt:F3} s 内转 {ang:F1}°（按 360°/s 应为 {expect:F1}°）");
                if (Mathf.Abs(ang - expect) > 8f) Problem($"转子实时转角 {ang:F1}° 与期望 {expect:F1}° 不符");
                Shot("P02_idle_threequarter", front + right * 0.8f + Vector3.up * 0.45f, center, 2.4f);
                Shot("P03_idle_side_right", right, center, 2.4f);
            });
            At(0.1f, () => Shot("P04_idle_engine_R_closeup", front + right * 0.6f + Vector3.up * 0.45f, eng + Vector3.down * 0.05f, 0.9f));
            At(0.1f, () => Shot("P05_idle_engine_R_outlet_fan", Vector3.down + right * 0.2f + front * 0.2f, eng + Vector3.down * 0.15f, 0.8f));
            // 2. 机械臂展开
            At(0.1f, () => { anim.Play("Arm_Deploy_R", 0, 0f); anim.Play("Arm_Deploy_R", 0, 0f); });
            At(0.05f, () => StateShot("P06_deploy_R_start", "Arm_Deploy_R", right + front * 0.35f + Vector3.up * 0.15f, center + Vector3.down * 0.12f, 1.8f));
            At(0.55f, () => StateShot("P07_deploy_R_mid", "Arm_Deploy_R", right + front * 0.35f + Vector3.up * 0.15f, center + Vector3.down * 0.12f, 1.8f));
            At(0.8f, () => StateShot("P08_deploy_R_end", "Arm_Deploy_R", right + front * 0.35f + Vector3.up * 0.15f, center + Vector3.down * 0.12f, 1.8f));
            // 3. 夹爪开合（实时播放，读取当前帧的上爪角度）
            At(0.2f, () => anim.Play("Gripper_OpenClose_R", 0, 0f));
            foreach (var (dt, name) in new[] { (0.4f, "P09_gripper_R_t0.4"), (0.4f, "P10_gripper_R_t0.8_open"), (0.4f, "P11_gripper_R_t1.2") })
                At(dt, () =>
                {
                    var st = anim.GetCurrentAnimatorStateInfo(0);
                    float frame = (st.normalizedTime % 1f) * 48f;
                    float expect = frame <= 24f ? 36f * Mathf.InverseLerp(0, 24, frame) : 36f * Mathf.InverseLerp(48, 24, frame);
                    float a = Quaternion.Angle(jawClosed, Bone("Arm_R_JawUpper").localRotation);
                    Note($"{name}：Gripper_OpenClose_R 第 {frame:F1} 帧，上爪 {a:F1}°（按关键帧线性估计约 {expect:F1}°，曲线有缓动）");
                    if (a < -0.1f || a > 36.5f) Problem($"夹爪角度越界 {a:F1}°");
                    var jaw = Mesh("Arm_R_JawUpper").bounds.center;
                    Shot(name, right + front * 0.25f, jaw, 0.5f);
                });
            // 4. 维修伸手
            At(0.2f, () => anim.Play("Repair_Reach", 0, 0f));
            At(1.3f, () => StateShot("P12_repair_reach_mid", "Repair_Reach", right + front * 0.35f + Vector3.up * 0.15f, center + Vector3.down * 0.12f, 1.8f));
            At(1.4f, () => { StateShot("P13_repair_reach_end", "Repair_Reach", front + right * 0.6f + Vector3.up * 0.3f, center, 2.4f); anim.Play("Idle_Hover", 0, 0f); });
            // 5. 屏幕表情
            for (int k = 0; k < 3; k++)
            {
                int idx = k;
                At(0.3f, () =>
                {
                    face.Show(idx);
                    var mpb = new MaterialPropertyBlock(); face.Screen.GetPropertyBlock(mpb);
                    var tex = mpb.GetTexture("_BaseMap");
                    Note($"屏幕表情 {idx}：{(tex != null ? tex.name : "null")}");
                    Shot($"P14_screen_face_{idx}_{(tex != null ? tex.name : "null")}", front + Vector3.up * 0.1f + right * 0.25f, Mesh("ScreenContent_Animated").bounds.center, 1.0f);
                });
            }
            At(0.3f, () => { face.Show(1); Shot("P15_idle_front_end", front, center, 2.4f); });
            At(0.5f, Finish);
            t0 = Time.time;
            next = 0;
            EditorApplication.update += Tick;
        }

        static void StateShot(string name, string state, Vector3 dir, Vector3 target, float dist)
        {
            var st = anim.GetCurrentAnimatorStateInfo(0);
            Note($"{name}：当前状态 {state} = {st.IsName(state)}，normalizedTime {st.normalizedTime:F2}");
            if (!st.IsName(state)) Problem($"{name} 时状态不是 {state}");
            Shot(name, dir, target, dist);
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
            while (next < steps.Count && Time.time - t0 >= steps[next].at)
            {
                try { steps[next].act(); }
                catch (Exception e) { Problem($"步骤 {next} 异常：{e.Message}"); }
                next++;
            }
        }

        static void Shot(string name, Vector3 dir, Vector3 target, float dist)
        {
            cap.transform.position = target + dir.normalized * dist;
            cap.transform.rotation = Quaternion.LookRotation(target - cap.transform.position, Vector3.up);
            cap.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Note($"控制台警告 / 错误 {console.Count} 条" + (console.Count > 0 ? "：" + string.Join(" | ", console.Distinct().Take(20)) : ""));
            foreach (var c in console.Where(c => c.StartsWith("Error") || c.StartsWith("Exception"))) Problem("控制台：" + c);
            Note($"完成：问题 {problems.Count} 个");
            File.WriteAllText(Path.Combine(outDir, "play_mode_check.log.txt"), log.ToString(), new UTF8Encoding(false));
            SessionState.SetInt(KeyExit, problems.Count == 0 ? 0 : 3);
            if (rt != null) rt.Release();
            EditorApplication.ExitPlaymode();
        }
    }
}
