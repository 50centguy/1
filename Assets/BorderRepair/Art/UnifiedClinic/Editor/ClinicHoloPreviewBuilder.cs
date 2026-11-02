using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BorderRepair.Art.UnifiedClinic.EditorTools
{
    // Builds a standalone Windows player that opens straight into the repair-props clinic.
    // That is the scene the ClinicShell materials and the holographic signage are bound in:
    // EditorBuildSettings does not list it, so the shared clinic player would show the old look.
    public static class ClinicHoloPreviewBuilder
    {
        const string ClinicScene = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        const string OutputDir = "Builds/ClinicHolo";
        const string ExeName = "BorderRepairClinic.exe";

        [MenuItem("Border Repair/Clinic Preview/Build Windows Player (holo clinic)", priority = 10500)]
        public static void Build()
        {
            if (!File.Exists(ClinicScene)) throw new InvalidOperationException("Missing scene: " + ClinicScene);
            Directory.CreateDirectory(OutputDir);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ClinicScene },
                locationPathName = Path.Combine(OutputDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });

            var s = report.summary;
            File.WriteAllText(Path.Combine(OutputDir, "build-report.txt"),
                $"{DateTime.UtcNow:O}\nScene: {ClinicScene}\nResult: {s.result}\n" +
                $"Errors: {s.totalErrors}\nWarnings: {s.totalWarnings}\n" +
                $"Bytes: {s.totalSize}\nDuration: {s.totalTime}\nPath: {s.outputPath}\n");

            if (s.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Clinic player build failed: " + s.result + " errors=" + s.totalErrors);

            Debug.Log("[ClinicHoloPreview] build ok -> " + s.outputPath);
        }

        public static void BuildBatch()
        {
            try { Build(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
