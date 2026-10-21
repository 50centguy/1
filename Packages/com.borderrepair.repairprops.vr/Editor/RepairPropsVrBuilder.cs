using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BorderRepair.FirstOrder;
using BorderRepair.TwoNight;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace BorderRepair.RepairProps.VR.Editor
{
    public static class RepairPropsVrBuilder
    {
        const string Root = "Assets/RepairPropsVR";
        const string Source = "Assets/RepairProps/Scenes/RepairProps_Clinic.unity";
        const string Target = Root + "/Scenes/RepairProps_Clinic_VR.unity";

        [MenuItem("Border Repair/Repair Props VR/Open VR Test Scene")]
        public static void OpenScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(Target);
        }

        [MenuItem("Border Repair/Repair Props VR/1 Import Official Starter Assets")]
        public static void ImportSamples()
        {
            var sample = Sample.FindByPackage("com.unity.xr.interaction.toolkit", "3.0.11")
                .FirstOrDefault(s => s.displayName == "Starter Assets");
            if (string.IsNullOrEmpty(sample.displayName)) throw new InvalidOperationException("XRI 3.0.11 Starter Assets not found.");
            if (!sample.isImported && !sample.Import(Sample.ImportOptions.OverridePreviousImports)) throw new InvalidOperationException("Starter Assets import failed.");
        }

        [MenuItem("Border Repair/Repair Props VR/2 Configure Standalone OpenXR")]
        public static void ConfigureOpenXR()
        {
            Folder(Root + "/XR");
            if (!EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.k_SettingsKey, out var perTarget))
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, Root + "/XR/GeneralSettings.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            var group = BuildTargetGroup.Standalone;
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = false;
            var manager = perTarget.ManagerSettingsForBuildTarget(group);
            if (!XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group))
                throw new InvalidOperationException("OpenXR loader registration failed.");
            FeatureHelpers.RefreshFeatures(group);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            var touch = settings != null ? settings.GetFeature<OculusTouchControllerProfile>() : null;
            if (touch == null) throw new InvalidOperationException("OpenXR Touch feature is not ready; resolve Project Validation and retry.");
            touch.enabled = true;
            EditorUtility.SetDirty(touch);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(perTarget);
            AssetDatabase.SaveAssets();
            Debug.Log("PC OpenXR configured. Automatic XR startup is OFF; only the VR scene initializes it. Check XR Project Validation before headset play.");
        }

        [MenuItem("Border Repair/Repair Props VR/3 Build Separate VR Scene")]
        public static void BuildScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            if (!File.Exists(Source)) throw new FileNotFoundException("Build the existing four-prop clinic first.", Source);
            var sample = Sample.FindByPackage("com.unity.xr.interaction.toolkit", "3.0.11").FirstOrDefault(s => s.displayName == "Starter Assets");
            if (!sample.isImported) throw new InvalidOperationException("Import Starter Assets first, and let Unity finish compiling.");
            var importedPath = sample.importPath.Replace('\\', '/');
            if (Path.IsPathRooted(importedPath)) importedPath = FileUtil.GetProjectRelativePath(importedPath);
            var prefabPath = importedPath + "/Prefabs/XR Origin (XR Rig).prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new InvalidOperationException("Official XR Origin prefab is missing.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(Target) && !EditorUtility.DisplayDialog("VR scene exists", "Replace only the generated VR scene?", "Replace", "Cancel")) return;
            string before = Hash(Source);
            Folder(Root + "/Scenes");
            var source = EditorSceneManager.OpenScene(Source);
            EditorSceneManager.SaveScene(source, Target, true);
            var scene = EditorSceneManager.OpenScene(Target);
            var clinic = UnityEngine.Object.FindFirstObjectByType<UnifiedClinicDirector>();
            var walker = UnityEngine.Object.FindFirstObjectByType<FirstPersonWalker>();
            if (clinic == null || walker == null) throw new InvalidOperationException("Expected shared clinic references were not found.");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var origin = rig.GetComponent<XROrigin>();
            if (origin == null) throw new InvalidOperationException("Official prefab has no XROrigin.");
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.transform.position = walker.transform.position;
            origin.transform.rotation = Quaternion.Euler(0, walker.transform.eulerAngles.y, 0);
            origin.Camera.tag = "MainCamera";
            foreach (var p in rig.GetComponentsInChildren<ContinuousMoveProvider>(true)) p.enabled = false;
            foreach (var p in rig.GetComponentsInChildren<ContinuousTurnProvider>(true)) p.enabled = false;
            foreach (var p in rig.GetComponentsInChildren<SnapTurnProvider>(true)) p.turnAmount = 30;
            ConfigureControllerActions(rig);
            var right = rig.GetComponentsInChildren<NearFarInteractor>(true).FirstOrDefault(h =>
                h.GetComponentsInParent<Transform>(true).Any(t => t.name.Contains("Right")));
            if (right == null) throw new InvalidOperationException("Official right-hand NearFarInteractor was not found.");
            right.enableUIInteraction = true;
            if (UnityEngine.Object.FindFirstObjectByType<XRInteractionManager>() == null) new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            var events = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (events == null) events = new GameObject("VR Event System").AddComponent<EventSystem>();
            foreach (var module in events.GetComponents<BaseInputModule>()) UnityEngine.Object.DestroyImmediate(module);
            events.gameObject.AddComponent<XRUIInputModule>();
            var host = new GameObject("Clinic PC VR Session");
            var session = host.AddComponent<ClinicVrSession>();
            var panel = host.AddComponent<VrClinicPanel>();
            session.clinic = clinic;
            session.origin = origin;
            session.rightHand = right;
            session.panel = panel;
            float floor = walker.transform.position.y;
            panel.benchTeleport = Anchor("Workbench Teleport", clinic.BenchStandPoint, floor);
            var receive = clinic.ReceiveAnchor.position;
            var towardRoom = clinic.BenchStandPoint - receive;
            towardRoom.y = 0;
            panel.counterTeleport = Anchor("Counter Teleport", receive + towardRoom.normalized * .8f, floor);
            rig.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (Hash(Source) != before) throw new InvalidOperationException("Desktop source scene unexpectedly changed.");
            Debug.Log("Created " + Target + ". Build list unchanged. PC runtime, head clearance, teleport positions and full VR flow still need validation.");
        }

        static TeleportationAnchor Anchor(string name, Vector3 point, float floor)
        {
            point.y = floor;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.position = point + Vector3.up * .003f;
            go.transform.localScale = new Vector3(.5f, .003f, .5f);
            var destination = new GameObject("Destination").transform;
            destination.SetParent(go.transform, true);
            destination.position = point;
            var anchor = go.AddComponent<TeleportationAnchor>();
            anchor.teleportAnchorTransform = destination;
            anchor.interactionLayers = 1 << 31;
            return anchor;
        }

        [MenuItem("Border Repair/Repair Props VR/Apply Comfort Defaults To VR Scene")]
        public static void ApplyComfortDefaults()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(Target);
            var session = UnityEngine.Object.FindFirstObjectByType<ClinicVrSession>();
            if (session == null || session.origin == null) throw new InvalidOperationException("VR scene rig is missing.");
            ConfigureControllerActions(session.origin.gameObject);
            foreach (var p in session.origin.GetComponentsInChildren<ContinuousMoveProvider>(true)) p.enabled = false;
            foreach (var p in session.origin.GetComponentsInChildren<ContinuousTurnProvider>(true)) p.enabled = false;
            foreach (var p in session.origin.GetComponentsInChildren<SnapTurnProvider>(true)) p.turnAmount = 30;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void ConfigureControllerActions(GameObject rig)
        {
            foreach (var behaviour in rig.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != "ControllerInputActionManager") continue;
                var serialized = new SerializedObject(behaviour);
                var motion = serialized.FindProperty("m_SmoothMotionEnabled");
                var turn = serialized.FindProperty("m_SmoothTurnEnabled");
                if (motion == null || turn == null) throw new InvalidOperationException("Official controller settings contract changed.");
                motion.boolValue = false;
                turn.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void Folder(string path)
        {
            string current = "Assets";
            foreach (var part in path.Split('/').Skip(1))
            {
                string child = current + "/" + part;
                if (!AssetDatabase.IsValidFolder(child)) AssetDatabase.CreateFolder(current, part);
                current = child;
            }
        }

        static string Hash(string file)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file)));
        }
    }
}
