using System;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.FirstOrder.EditorTools.LayoutAB;
using BorderRepair.FirstOrder.Slice;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace BorderRepair.FirstOrder.EditorTools.Slice
{
    /// <summary>
    /// 两晚切片：场景生成与 Windows 构建入口。
    /// 场景 = 布局 B 测试场景（含 qa/unit07-fault-art-scene 的灯光调整）的副本，加上鼠标界面（SliceView）、EventSystem 和 Player 自检。
    /// 布局 B 原场景和正式首单场景都不改，留作对照。
    /// 构建只用 BuildScenes 里写明的场景，不读 EditorBuildSettings（那里还是旧三物件原型和 SampleScene）。
    /// </summary>
    public static class TwoNightSlice
    {
        public const string SceneDir = FirstOrderSceneBuilder.Root + "/Scenes/Slice";
        public const string ScenePath = SceneDir + "/TwoNightSlice.unity";
        public const string FontPath = "Assets/BorderRepair/Art/Fonts/NotoSansSC/NotoSansSC-Regular.otf";
        public const string BuildDir = "Builds/TwoNightSlice";
        public const string ExeName = "TwoNightSlice.exe";
        public const string DocsDir = "Docs/Integration/TwoNightSlice";
        public const string UiName = "SliceUI (两晚切片鼠标界面)";

        /// <summary>构建用的明确场景列表：只有切片场景。</summary>
        public static readonly string[] BuildScenes = { ScenePath };

        [MenuItem("Border Repair/Two-Night Slice/Build Slice Scene")]
        public static void BuildScene()
        {
            var font = EnsureFont();
            Directory.CreateDirectory(SceneDir);
            AssetDatabase.DeleteAsset(ScenePath);
            if (!AssetDatabase.CopyAsset(LayoutABScenes.SceneB, ScenePath)) throw new Exception("复制布局 B 场景失败：" + LayoutABScenes.SceneB);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var flow = UnityEngine.Object.FindFirstObjectByType<FirstOrderFlow>();
            var input = UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>();
            if (flow == null || input == null) throw new Exception("切片场景里找不到 FirstOrderFlow / FirstOrderInput");
            input.ConfigureHud(false, true, font, new Vector2(680f, 12f));   // 调试 HUD 默认关，放在右上；数字键保留为可选调试操作
            EditorUtility.SetDirty(input);

            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var ui = new GameObject(UiName);
            var view = ui.AddComponent<SliceView>();
            view.Configure(flow, input, font);

            var check = new GameObject("SlicePlayerSelfCheck (仅 -sliceSelfCheck 时运行)").AddComponent<SlicePlayerSelfCheck>();
            check.Configure(view, flow, input, font);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(DocsDir);
            File.WriteAllText(Path.Combine(DocsDir, "scene_build.txt"),
                $"切片场景 {ScenePath}\n来源 {LayoutABScenes.SceneB}（副本；原场景不改）\n字体 {FontPath}（{font.name}，dynamic {font.dynamic}）\n" +
                $"FirstOrderFlow {PathOf(flow.transform)}；FirstOrderInput 调试 HUD 默认关、数字键保留\n加了：EventSystem（InputSystemUIInputModule）、{UiName}、SlicePlayerSelfCheck\n",
                new UTF8Encoding(false));
            Debug.Log("[TwoNightSlice] 场景已生成：" + ScenePath);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        /// <summary>字体导入设置：动态字体、把字体数据打进包里（不依赖玩家电脑装的字体）。</summary>
        public static Font EnsureFont()
        {
            var imp = (TrueTypeFontImporter)AssetImporter.GetAtPath(FontPath);
            if (imp == null) throw new Exception("找不到字体 " + FontPath);
            if (imp.fontTextureCase != FontTextureCase.Dynamic || !imp.includeFontData || imp.fontReferences.Length > 0)
            {
                imp.fontTextureCase = FontTextureCase.Dynamic;
                imp.includeFontData = true;
                imp.fontReferences = new Font[0];
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        }

        [MenuItem("Border Repair/Two-Night Slice/Build Windows Player")]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) BuildSceneNoExit();
            // 窗口化 1600×900，可调大小；构建后恢复原设置（不改项目设置）
            var mode = PlayerSettings.fullScreenMode;
            int w = PlayerSettings.defaultScreenWidth, h = PlayerSettings.defaultScreenHeight;
            bool resizable = PlayerSettings.resizableWindow;
            BuildReport report;
            try
            {
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.defaultScreenWidth = 1600;
                PlayerSettings.defaultScreenHeight = 900;
                PlayerSettings.resizableWindow = true;
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = BuildScenes,
                    locationPathName = BuildDir + "/" + ExeName,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                });
            }
            finally
            {
                PlayerSettings.fullScreenMode = mode;
                PlayerSettings.defaultScreenWidth = w;
                PlayerSettings.defaultScreenHeight = h;
                PlayerSettings.resizableWindow = resizable;
            }
            var s = report.summary;
            var sb = new StringBuilder();
            sb.AppendLine($"构建 {DateTime.Now:yyyy-MM-dd HH:mm:ss}，Unity {Application.unityVersion}");
            sb.AppendLine($"结果 {s.result}，目标 {s.platform}，用时 {s.totalTime.TotalSeconds:F0} s，大小 {s.totalSize / (1024f * 1024f):F1} MB，错误 {s.totalErrors}，警告 {s.totalWarnings}");
            sb.AppendLine($"输出 {System.IO.Path.GetFullPath(BuildDir + "/" + ExeName)}");
            sb.AppendLine("场景（明确列表，不用 EditorBuildSettings）：");
            foreach (var sc in BuildScenes) sb.AppendLine("  - " + sc);
            sb.AppendLine("EditorBuildSettings 里原有的（没有用）：" + string.Join("，", EditorBuildSettings.scenes.Select(x => x.path)));
            sb.AppendLine("窗口：1600×900 窗口化、可调大小（只在构建时设置，构建后恢复项目设置）");
            Directory.CreateDirectory(DocsDir);
            File.WriteAllText(System.IO.Path.Combine(DocsDir, "build_report.txt"), sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[TwoNightSlice] " + sb);
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        static void BuildSceneNoExit()
        {
            bool batch = Application.isBatchMode;
            if (!batch) { BuildScene(); return; }
            // 批处理模式下 BuildScene 会退出编辑器，这里不能用；直接要求先生成场景
            throw new Exception("切片场景不存在：先运行 TwoNightSlice.BuildScene");
        }
    }
}
