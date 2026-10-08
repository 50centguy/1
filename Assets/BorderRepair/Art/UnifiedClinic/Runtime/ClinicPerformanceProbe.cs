using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.FirstOrder;
using BorderRepair.FirstOrder.Slice;
using BorderRepair.TwoNight;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace BorderRepair.Art.UnifiedClinic
{
    // Opt-in standalone probe only; normal game runs never instantiate this component.
    public sealed class ClinicPerformanceProbe : MonoBehaviour
    {
        [Serializable] class Sample
        {
            public string state, camera;
            public int sampledFrames, timingFrames, gpuTimingFrames;
            public bool drawCounterAvailable, triangleCounterAvailable;
            public double averageFrameMs, frameP95Ms, cpuTimingMeanMs, gpuTimingMeanMs;
            public double drawCallsMean, trianglesSubmittedMean;
            public long allocatedMemoryBytes;
        }
        [Serializable] class Report
        {
            public string timestampUtc, unityVersion, gpu, cpu, renderMode;
            public int width, height;
            public bool editor, uncapped;
            public Sample[] samples;
            public string[] limitations;
        }
        string output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartRequested()
        {
            if (Application.isEditor) return;
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-clinicBenchmark");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("ClinicStandaloneBenchmark");
            DontDestroyOnLoad(go);
            go.AddComponent<ClinicPerformanceProbe>().output = args[i + 1];
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            Application.runInBackground = true;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            TwoNightSave.OverrideDirectory = Path.Combine(output, "IsolatedSave");
            var results = new List<Sample>();
            yield return null;
            TwoNightRun.NewGame(null, true);
            yield return SceneManager.LoadSceneAsync(TwoNightScenes.Clinic, LoadSceneMode.Single);
            yield return WaitForClinic();
            var clinic = FindFirstObjectByType<UnifiedClinicDirector>();
            if (!Ready(clinic, TwoNightPhase.Night1Counter)) yield break;
            clinic.Robot.Flow.Rig.Go(FirstOrderCameraRig.Overview, true);
            yield return Measure("Night1_AwaitingReceive", results);
            var state = TwoNightRun.NewGame(null, true);
            if (!TwoNightRun.ReceiveCommunicator(state, "case_n1_collector_communicator") ||
                !TwoNightRun.CompleteCommunicatorRepair(state, true, state.communicatorCaseId) ||
                !TwoNightRun.DeliverCommunicator(state, state.communicatorCaseId, null) ||
                !TwoNightRun.ConfirmLedger(state) || !TwoNightRun.FinishIncident(state) ||
                !TwoNightRun.RegisterUnit07(state, new Unit07SafeState { seated = true, clamped = true, powerOff = true,
                    rotorsStopped = true, trayStowed = true, robotUpright = true }, out _) || !TwoNightRun.BeginNight2(state))
            {
                File.WriteAllText(Path.Combine(output, "failure.txt"), "Cannot prepare isolated night-two benchmark state.");
                Application.Quit(2); yield break;
            }
            yield return SceneManager.LoadSceneAsync(TwoNightScenes.Clinic, LoadSceneMode.Single);
            yield return WaitForClinic();
            clinic = FindFirstObjectByType<UnifiedClinicDirector>();
            if (!Ready(clinic, TwoNightPhase.Night2Open)) yield break;
            if (clinic.Robot.RestoreFailed) { Application.Quit(3); yield break; }
            var view = FindFirstObjectByType<SliceView>();
            if (view.ManualOpen) view.ToggleManual();
            clinic.Robot.Flow.Rig.Go(FirstOrderCameraRig.Overview, true);
            yield return Measure("Night2_Overview", results);
            clinic.Robot.Flow.Rig.Go(FirstOrderCameraRig.EngineClose, true);
            yield return Measure("Night2_EngineClose", results);
            var report = new Report {
                timestampUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                gpu = SystemInfo.graphicsDeviceName, cpu = SystemInfo.processorType,
                width = Screen.width, height = Screen.height, editor = Application.isEditor, uncapped = true,
                renderMode = "One explicit camera render/frame to a native offscreen surface, including camera-space UI. Hidden-window backbuffer is not measured.",
                samples = results.ToArray(), limitations = new[] {
                    "Standalone measurements on this machine only; not target-device or VR acceptance.",
                    "VSync and frame cap disabled for this opt-in probe; normal game settings are unchanged.",
                    "Frame timings can include driver/present waits; zero GPU timings are excluded, with gpuTimingFrames=0 and mean=-1 if unavailable.",
                    "Profiler triangle/draw counters include render submissions, not unique asset triangles.",
                    "Three stationary viewpoints; interactive worst cases and headset stereo performance still need measurement."
                }
            };
            File.WriteAllText(Path.Combine(output, "standalone_performance.json"), JsonUtility.ToJson(report, true));
            TwoNightSave.OverrideDirectory = null;
            Application.Quit(0);
        }

        static IEnumerator WaitForClinic()
        {
            for (int i = 0; i < 12; i++) yield return null;
            float deadline = Time.realtimeSinceStartup + 15;
            while (FindFirstObjectByType<UnifiedClinicDirector>()?.Initialized != true && Time.realtimeSinceStartup < deadline)
                yield return null;
        }
        bool Ready(UnifiedClinicDirector clinic, TwoNightPhase expected)
        {
            if (clinic != null && clinic.Initialized && clinic.State.phase == expected) return true;
            File.WriteAllText(Path.Combine(output, "failure.txt"), "Clinic initialization/phase failed: expected " + expected);
            TwoNightSave.OverrideDirectory = null;
            Application.Quit(4);
            return false;
        }
        IEnumerator Measure(string state, List<Sample> results)
        {
            using var frame = new ClinicOffscreenFrame(Camera.main, Screen.width, Screen.height);
            for (int i = 0; i < 120; i++) { frame.Render(); yield return null; }
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var frameMs = new List<double>();
            double drawSum = 0, triangleSum = 0, cpuSum = 0, gpuSum = 0;
            int timingCount = 0, gpuTimingCount = 0;
            ulong lastTiming = 0;
            var timing = new FrameTiming[1];
            for (int i = 0; i < 240; i++)
            {
                FrameTimingManager.CaptureFrameTimings();
                frame.Render();
                yield return null;
                frameMs.Add(Time.unscaledDeltaTime * 1000);
                if (draws.Valid) drawSum += draws.LastValue;
                if (triangles.Valid) triangleSum += triangles.LastValue;
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].frameStartTimestamp != lastTiming)
                {
                    lastTiming = timing[0].frameStartTimestamp;
                    if (timing[0].cpuFrameTime > 0) { timingCount++; cpuSum += timing[0].cpuFrameTime; }
                    if (timing[0].gpuFrameTime > 0) { gpuTimingCount++; gpuSum += timing[0].gpuFrameTime; }
                }
            }
            var sorted = frameMs.OrderBy(v => v).ToArray();
            results.Add(new Sample {
                state = state, camera = FindFirstObjectByType<UnifiedClinicDirector>().Robot.Flow.Rig.Current,
                sampledFrames = frameMs.Count, averageFrameMs = frameMs.Average(), frameP95Ms = sorted[(int)(sorted.Length * .95)],
                drawCounterAvailable = draws.Valid, triangleCounterAvailable = triangles.Valid,
                drawCallsMean = draws.Valid ? drawSum / frameMs.Count : -1,
                trianglesSubmittedMean = triangles.Valid ? triangleSum / frameMs.Count : -1,
                timingFrames = timingCount, cpuTimingMeanMs = timingCount > 0 ? cpuSum / timingCount : -1,
                gpuTimingFrames = gpuTimingCount, gpuTimingMeanMs = gpuTimingCount > 0 ? gpuSum / gpuTimingCount : -1,
                allocatedMemoryBytes = Profiler.GetTotalAllocatedMemoryLong()
            });
            frame.SavePng(Path.Combine(output, state + ".png"));
        }
    }
}
