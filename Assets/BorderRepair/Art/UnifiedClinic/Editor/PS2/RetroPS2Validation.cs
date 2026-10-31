using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Art.UnifiedClinic.PS2;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BorderRepair.Art.UnifiedClinic.EditorTools
{
    /// <summary>
    /// Read-only static checks for the PS2 look. Never opens, saves or modifies scenes, materials or renderer
    /// assets. The batch entry writes a text report only into a new directory passed with -ps2Report.
    /// </summary>
    public static class RetroPS2Validation
    {
        const string ShaderPath = "Assets/BorderRepair/Art/UnifiedClinic/Runtime/PS2/Resources/" + RetroPS2CameraEffect.ShaderResource + ".shader";
        const string VRScenePath = "Assets/RepairPropsVR/Scenes/RepairProps_Clinic_VR.unity";

        [MenuItem("Border Repair/Clinic Base/Retro Look/PS2 Static Check (read-only)", priority = 2000)]
        public static void RunFromMenu()
        {
            var lines = Run(out bool ok);
            foreach (var line in lines) Debug.Log("[RetroPS2Check] " + line);
            if (ok) Debug.Log("[RetroPS2Check] PASS (static only; rendering still needs Play Mode capture)");
            else Debug.LogError("[RetroPS2Check] FAIL — see lines above");
        }

        /// <summary>-executeMethod BorderRepair.Art.UnifiedClinic.EditorTools.RetroPS2Validation.RunBatch -ps2Report &lt;new dir&gt;</summary>
        public static void RunBatch()
        {
            int code = 1;
            try
            {
                var lines = Run(out bool ok);
                string dir = Argument("-ps2Report");
                if (!string.IsNullOrEmpty(dir))
                {
                    if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
                        throw new IOException("Report directory must be new or empty: " + dir);
                    Directory.CreateDirectory(dir);
                    File.WriteAllLines(Path.Combine(dir, "ps2_static_check.txt"),
                        new[] { "UTC " + DateTime.UtcNow.ToString("O"), "Unity " + Application.unityVersion, ok ? "PASS" : "FAIL" }.Concat(lines));
                }
                foreach (var line in lines) Debug.Log("[RetroPS2Check] " + line);
                code = ok ? 0 : 1;
            }
            catch (Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(code);
        }

        static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public static List<string> Run(out bool ok)
        {
            var lines = new List<string>();
            bool pass = true;
            void Check(bool condition, string message) { lines.Add((condition ? "OK   " : "FAIL ") + message); pass &= condition; }

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Check(shader != null, "PS2 shader asset at " + ShaderPath);
            if (shader != null)
            {
                Check(shader.name == "Hidden/BorderRepair/RetroClinicPS2", "shader name " + shader.name);
                bool error = ShaderUtil.ShaderHasError(shader);
                Check(!error, "shader compiles without errors" + (error ? ": " +
                    string.Join("; ", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)) : ""));
                Check(Resources.Load<Shader>(RetroPS2CameraEffect.ShaderResource) == shader,
                    "Resources.Load(\"" + RetroPS2CameraEffect.ShaderResource + "\") resolves (Player include via Resources)");
                foreach (var property in new[] { "_PS2Strength", "_PS2Lines", "_PS2PixelSharpness", "_PS2ColorLevels", "_PS2Dither" })
                    Check(shader.FindPropertyIndex(property) >= 0, "shader property " + property);
            }

            foreach (var path in RetroPS2CameraEffect.DesktopScenePaths)
            {
                Check(AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null, "desktop scene exists " + path);
                Check(File.Exists(path) && File.ReadAllText(path).Contains(CameraScriptGuid()),
                    "desktop scene has a RetroClinicCamera (runtime opt-in target) " + path);
            }
            Check(!RetroPS2CameraEffect.DesktopScenePaths.Contains(VRScenePath, StringComparer.OrdinalIgnoreCase),
                "VR scene excluded from PS2 scope " + VRScenePath);

            var renderers = AssetDatabase.FindAssets("t:UniversalRendererData", new[] { ClinicRetroLook.Root + "/Renderers" })
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            Check(renderers.Length > 0, "retro renderers found: " + renderers.Length);
            foreach (var path in renderers)
            {
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                var feature = data != null ? data.rendererFeatures.OfType<RetroClinicRendererFeature>().FirstOrDefault() : null;
                Check(feature != null && feature.isActive, "RetroClinicRendererFeature active in " + path);
                Check(feature == null || feature.injectionPoint == FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing,
                    "old diffusion still BeforeRenderingPostProcessing in " + path);
            }
            ok = pass;
            return lines;
        }

        static string CameraScriptGuid()
        {
            var script = AssetDatabase.FindAssets("RetroClinicCamera t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => p.EndsWith("/RetroClinicCamera.cs"));
            return script == null ? "<missing>" : AssetDatabase.AssetPathToGUID(script);
        }
    }

    /// <summary>Play-mode Inspector for the session PS2 settings (same values as the in-game "画面" panel).</summary>
    [CustomEditor(typeof(RetroPS2Runtime))]
    public sealed class RetroPS2RuntimeEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("运行时设置，不写入场景或存档。= 键打开游戏内面板。", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (RetroPS2Preset preset in Enum.GetValues(typeof(RetroPS2Preset)))
                    if (GUILayout.Button(preset.ToString())) RetroPS2Settings.ApplyPreset(preset);
                if (GUILayout.Button("Reset")) RetroPS2Settings.ResetToDefaults();
            }
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.Toggle("PS2 效果（总开关）", RetroPS2Settings.Enabled);
            Profile("房间行走", RetroPS2Settings.Room);
            Profile("维修近看", RetroPS2Settings.Inspection);
            if (EditorGUI.EndChangeCheck())
            {
                RetroPS2Settings.Enabled = enabled;
                RetroPS2Settings.NotifyChanged();
            }
            var runtime = (RetroPS2Runtime)target;
            EditorGUILayout.LabelField("已标记相机", runtime.MarkedCameraCount.ToString());
        }

        static void Profile(string label, RetroPS2Profile p)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            p.strength = EditorGUILayout.Slider("强度", p.strength, 0, 1);
            p.virtualLines = EditorGUILayout.IntSlider("低分辨率（行）", p.virtualLines, 120, 1080);
            p.pixelSharpness = EditorGUILayout.Slider("像素边缘", p.pixelSharpness, 0, 1);
            p.colorLevels = EditorGUILayout.IntSlider("色阶", p.colorLevels, 4, 256);
            p.dither = EditorGUILayout.Slider("抖色", p.dither, 0, 1);
            p.softness = EditorGUILayout.Slider("柔光倍数", p.softness, 0, 2);
        }
    }
}
