using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BorderRepair.RepairProps
{
    // The project runs with VSync off and no frame cap, so a frame takes anything from 1.5 ms to 20 ms: the uneven pace
    // reads as stutter even where the average is high. On the desktop clinic the frame rate is held at the display's
    // refresh rate (60 when it cannot be read). Not in batch mode (tests measure uncapped), not in XR (the headset paces
    // itself), not under the standalone benchmark (-clinicBenchmark wants it uncapped), and never over a cap or VSync
    // someone else already set.
    public static class ClinicFramePacing
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() => SceneManager.sceneLoaded -= OnSceneLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install() => SceneManager.sceneLoaded += OnSceneLoaded;

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!RepairBenchV2Runtime.IsClinicScene(scene.path)) return;
            if (Application.isBatchMode || UnityEngine.XR.XRSettings.isDeviceActive) return;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-clinicBenchmark") >= 0) return;
            if (Application.targetFrameRate > 0 || QualitySettings.vSyncCount > 0) return;
            Application.targetFrameRate = Rate();
        }

        /// <summary>The display's refresh rate, rounded, between 60 and 240 (60 when unknown).</summary>
        public static int Rate()
        {
            double hz = Screen.currentResolution.refreshRateRatio.value;
            return double.IsNaN(hz) || hz < 1 ? 60 : Mathf.Clamp(Mathf.RoundToInt((float)hz), 60, 240);
        }
    }
}
