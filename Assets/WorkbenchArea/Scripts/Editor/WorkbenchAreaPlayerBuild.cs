using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WorkbenchArea.EditorTools
{
    /// <summary>
    /// 只含工作台测试场景的 Windows 独立构建，用来在编辑器之外测量 Draw Call（运行时由 WbDrawCallProbe 在 -wbProbe 参数下自动测量并退出）。
    /// 输出到 Builds/WorkbenchAreaProbe/（已被 .gitignore 忽略）。不改 EditorBuildSettings 的场景列表。
    /// 命令行：-executeMethod WorkbenchArea.EditorTools.WorkbenchAreaPlayerBuild.Build [-wbDevelopment]
    /// </summary>
    public static class WorkbenchAreaPlayerBuild
    {
        public const string OutDir = "Builds/WorkbenchAreaProbe";
        public const string Exe = OutDir + "/WorkbenchAreaProbe.exe";

        [MenuItem("Workbench Area/Build Draw Call Probe Player")]
        public static void Build()
        {
            bool dev = System.Environment.GetCommandLineArgs().Contains("-wbDevelopment");
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { WorkbenchAreaBuilder.ScenePath },
                locationPathName = Exe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = dev ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            var sb = new StringBuilder();
            sb.AppendLine($"独立构建：{s.result}，{(dev ? "Development" : "Release")}，{s.totalSize / 1048576.0:F1} MB，用时 {s.totalTime.TotalSeconds:F0} s，错误 {s.totalErrors}，警告 {s.totalWarnings}");
            sb.AppendLine($"输出：{Path.GetFullPath(Exe)}");
            Directory.CreateDirectory(WorkbenchAreaBuilder.ReportDir);
            File.WriteAllText(Path.Combine(WorkbenchAreaBuilder.ReportDir, "player_build.txt"), sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[WorkbenchArea] " + sb);
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
