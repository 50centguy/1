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
        const string BurstDebugDir = "BorderRepairStation_BurstDebugInformation_DoNotShip";

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

            DeleteBurstDebugInformation();
            Debug.Log("[ClinicHoloPreview] build ok -> " + s.outputPath);
        }

        // Unity writes this next to the player; it is for debugging this build and must not go out with it.
        // A build that worked is still a good build if this fails, so it only warns.
        static void DeleteBurstDebugInformation()
        {
            string path = Path.Combine(OutputDir, BurstDebugDir);
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ClinicHoloPreview] could not delete " + path + ": " + e.Message);
            }
        }

        public static void BuildBatch()
        {
            try { Build(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
