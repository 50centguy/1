using System;
using System.IO;
using BorderRepair.TwoNight.EditorTools;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BorderRepair.Art.UnifiedClinic.EditorTools
{
    public static class ClinicPlayerBuilder
    {
        [MenuItem("Border Repair/Unified Clinic/Build Isolated Windows Player")]
        public static void Build()
        {
            string output = "Builds/UnifiedClinic-20261008-InspectionFix";
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { UnifiedClinicBuilder.MenuPath, UnifiedClinicBuilder.ClinicPath },
                locationPathName = output + "/BorderRepairClinic.exe", target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            Directory.CreateDirectory("Docs/Integration/UnifiedClinic");
            File.WriteAllText("Docs/Integration/UnifiedClinic/player_build.txt", $"{DateTime.UtcNow:O}\n" +
                $"Result: {report.summary.result}\nErrors: {report.summary.totalErrors}\nWarnings: {report.summary.totalWarnings}\n" +
                $"Bytes: {report.summary.totalSize}\nPath: {report.summary.outputPath}\n");
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Shared clinic player build failed.");
        }
        public static void BuildBatch()
        {
            try { Build(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
