using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.OpenXR;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace BorderRepair.RepairProps.VR.Editor
{
    public static class VrConfigurationReport
    {
        [MenuItem("Border Repair/Repair Props VR/4 Validate PC VR Configuration")]
        public static void WriteReport()
        {
            const string scenePath = "Assets/RepairPropsVR/Scenes/RepairProps_Clinic_VR.unity";
            EditorSceneManager.OpenScene(scenePath);
            var host = UnityEngine.Object.FindFirstObjectByType<ClinicVrSession>();
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            var issues = new List<OpenXRFeature.ValidationRule>();
            OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Standalone);
            var result = new Report {
                utc=DateTime.UtcNow.ToString("o"),scene=scenePath,
                automaticXRStartup=general != null && general.InitManagerOnStart,
                openXRLoader=general != null && general.Manager.activeLoaders.Any(l => l is OpenXRLoader),
                touchProfile=xr != null && xr.GetFeature<OculusTouchControllerProfile>() != null && xr.GetFeature<OculusTouchControllerProfile>().enabled,
                rigPresent=host != null && host.origin != null && host.rightHand != null && host.panel != null,
                requiredIssues=issues.Where(i => i.error || i.errorEnteringPlaymode).Select(i => i.message).ToArray(),
                advisoryIssues=issues.Where(i => !i.error && !i.errorEnteringPlaymode).Select(i => i.message).ToArray(),
                buildScenePaths=EditorBuildSettings.scenes.Select(s => s.path).ToArray(),
                missingScripts=UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
                    .SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)),
            };
            Directory.CreateDirectory("Docs/Integration/RepairPropsVR");
            File.WriteAllText("Docs/Integration/RepairPropsVR/configuration.json", JsonUtility.ToJson(result,true));
            Debug.Log("PC VR CONFIGURATION " + JsonUtility.ToJson(result));
            if (!result.openXRLoader || !result.touchProfile || !result.rigPresent || result.automaticXRStartup || result.missingScripts != 0 || result.requiredIssues.Length != 0)
                throw new InvalidOperationException("PC VR configuration has unresolved required issues; inspect configuration.json.");
        }

        [Serializable] class Report
        {
            public string utc;
            public string scene;
            public bool automaticXRStartup;
            public bool openXRLoader;
            public bool touchProfile;
            public bool rigPresent;
            public int missingScripts;
            public string[] requiredIssues;
            public string[] advisoryIssues;
            public string[] buildScenePaths;
            public string scope = "Actual editor settings and serialized scene check; not a headset, optical, input-device, comfort or performance acceptance.";
        }
    }
}
