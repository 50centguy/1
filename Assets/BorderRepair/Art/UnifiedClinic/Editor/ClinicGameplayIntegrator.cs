using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.FirstOrder;
using BorderRepair.TwoNight;
using BorderRepair.TwoNight.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BorderRepair.Art.UnifiedClinic.EditorTools
{
    public static class ClinicGameplayIntegrator
    {
        [Serializable] class IntegrationReport
        {
            public string timestampUtc, scene, baselineSource;
            public Vector3 benchPosition, dockPosition, robotSeat, receive, deliver;
            public int environmentRenderers, hiddenLegacyObjects;
            public string[] migratedRoots, limitations;
        }

        [MenuItem("Border Repair/Unified Clinic/Rebuild Shared Gameplay With Clinic Environment")]
        public static void Build()
        {
            if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save open scene edits before rebuilding the clinic.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var paths = new[] { UnifiedClinicBuilder.ClinicPath, UnifiedClinicBuilder.ClinicPath + ".meta",
                UnifiedClinicBuilder.MenuPath, UnifiedClinicBuilder.MenuPath + ".meta",
                "Docs/Integration/UnifiedClinic/gameplay_layout.json", "Logs/UnifiedClinicBaseline.json",
                "Docs/Integration/UnifiedClinic/ArtImport/clutter_import.json" };
            var directories = new[] { "Models", "Textures", "Materials", "Prefabs", "Clutter" }
                .Select(name => ClinicArtImporter.Root + "/" + name).Append(UnifiedClinicBuilder.SceneDirectory).ToArray();
            var existingDirectories = new HashSet<string>(GeneratedDirectories(directories), StringComparer.OrdinalIgnoreCase);
            // Scenes reference imported resources by GUID, so recovery must include their bytes and importer metas.
            var saved = paths.Concat(GeneratedFiles(directories)).Distinct()
                .ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null);
            var buildScenes = EditorBuildSettings.scenes;
            try
            {
                // Validate optional imports before publishing over the last accepted scene.
                var clutter = ClinicClutterImporter.Import();
                ClinicArtImporter.ImportEnvironment();
                BuildContents(clutter);
            }
            catch
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (string added in GeneratedFiles(directories).Where(p => !saved.ContainsKey(p)))
                        if (File.Exists(added)) File.Delete(added);
                    foreach (var file in saved)
                    {
                        if (file.Value == null) { if (File.Exists(file.Key)) File.Delete(file.Key); }
                        else File.WriteAllBytes(file.Key, file.Value);
                    }
                    foreach (string added in GeneratedDirectories(directories)
                        .Where(p => !existingDirectories.Contains(p)).OrderByDescending(p => p.Length))
                        if (!Directory.EnumerateFileSystemEntries(added).Any()) Directory.Delete(added, false);
                }
                finally { AssetDatabase.StopAssetEditing(); }
                EditorBuildSettings.scenes = buildScenes;
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (setup.Any(s => s.isLoaded && s.isActive && !string.IsNullOrEmpty(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                throw;
            }
        }

        static IEnumerable<string> GeneratedFiles(IEnumerable<string> directories)
        {
            foreach (string directory in directories)
            {
                yield return directory + ".meta";
                if (Directory.Exists(directory))
                    foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                        yield return path.Replace('\\', '/');
            }
        }

        static IEnumerable<string> GeneratedDirectories(IEnumerable<string> directories)
        {
            foreach (string directory in directories.Where(Directory.Exists))
            {
                yield return directory;
                foreach (string child in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories))
                    yield return child.Replace('\\', '/');
            }
        }

        static void BuildContents(GameObject clutterAsset)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ClinicArtImporter.PrefabPath) == null)
                throw new InvalidOperationException("Import clinic art first.");
            UnifiedClinicBuilder.Build();
            var scene = SceneManager.GetActiveScene();
            var frame = scene.GetRootGameObjects().Single(g => g.name == "ClinicLegacyRoomFrame").transform;
            var anchors = ClinicArtImporter.ReadAnchors().anchors;
            ClinicArtImporter.Anchor Anchor(string name) => anchors.Single(a => a.name == name);
            var all = frame.GetComponentsInChildren<Transform>(true);
            Transform Named(string name) => all.Single(t => t.name == name);
            var bench = Named("WorkbenchArea");
            var dock = Named("Unit07ServiceDock");
            var benchDeltaRotation = Quaternion.Euler(0, 180, 0);
            var dockDeltaRotation = Quaternion.Euler(0, 165, 0);
            var benchDelta = Matrix4x4.TRS(Anchor("WorkbenchArea_Root").Position,
                benchDeltaRotation * bench.rotation, bench.lossyScale) * bench.worldToLocalMatrix;
            var dockDelta = Matrix4x4.TRS(Anchor("Dock_Root").Position,
                dockDeltaRotation * dock.rotation, dock.lossyScale) * dock.worldToLocalMatrix;
            var roots = frame.Cast<Transform>().ToArray();
            var moved = new List<string>();
            var flow = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<FirstOrderFlow>(true)).Single();
            flow.ConfigureCarrySpeed(1.5f);
            var incident = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TrayIncident>(true)).Single();
            var clinic = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<UnifiedClinicDirector>(true)).Single();

            foreach (var root in roots)
            {
                bool attachedBench = root == bench || root.name.StartsWith("FO_NewBearing_") ||
                    root.name.StartsWith("FO_Drop_") || root.name.StartsWith("Bench_");
                bool attachedDock = root == dock || root == flow.Dock.RobotRoot || root.name.StartsWith("Dock_");
                if (!attachedBench && !attachedDock) continue;
                Remap(root, attachedBench ? benchDelta : dockDelta, attachedBench ? benchDeltaRotation : dockDeltaRotation);
                moved.Add(root.name);
            }
            foreach (var pose in flow.Rig.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("CamPose_") && t.name != "CamPose_Overview"))
            {
                bool followsBench = new[] { "CamPose_Bench", "CamPose_Record", "CamPose_Compare" }.Contains(pose.name);
                Remap(pose, followsBench ? benchDelta : dockDelta, followsBench ? benchDeltaRotation : dockDeltaRotation);
            }
            foreach (var pose in all.Where(t => t.name == "RP_Bench" || t.name == "RP_EngineBay"))
                Remap(pose, pose.name == "RP_Bench" ? benchDelta : dockDelta,
                    pose.name == "RP_Bench" ? benchDeltaRotation : dockDeltaRotation);

            foreach (var zone in frame.GetComponentsInChildren<FirstOrderDropZone>(true))
            {
                zone.approachPoint = benchDelta.MultiplyPoint3x4(zone.approachPoint);
                zone.landingOffset = benchDelta.MultiplyVector(zone.landingOffset);
                zone.landingRotation = benchDeltaRotation * zone.landingRotation;
                EditorUtility.SetDirty(zone);
            }
            var approach = flow.NewBearingBenchApproach;
            flow.ConfigureNewBearingApproach(approach.use, benchDelta.MultiplyPoint3x4(approach.point));
            var data = new SerializedObject(incident);
            foreach (string field in new[] { "hover", "transitOverDock", "transitOverShelf", "place", "release", "withdraw", "withdrawUp" })
                data.FindProperty(field).vector3Value = dockDelta.MultiplyPoint3x4(data.FindProperty(field).vector3Value);
            data.FindProperty("hoverRot").quaternionValue = dockDeltaRotation * data.FindProperty("hoverRot").quaternionValue;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(flow);

            var environment = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ClinicArtImporter.PrefabPath), scene);
            environment.name = "UnifiedClinicEnvironment";
            if (clutterAsset != null)
            {
                var clutter = (GameObject)PrefabUtility.InstantiatePrefab(clutterAsset, scene);
                clutter.name = "UnifiedClinicClutter";
                clutter.transform.SetParent(environment.transform, true);
            }
            var hide = new HashSet<string>(ClinicArtImporter.BenchHideList());
            int hidden = 0;
            foreach (var child in bench.GetComponentsInChildren<Transform>(true).Where(t => hide.Contains(t.name)))
            { child.gameObject.SetActive(false); hidden++; }
            foreach (var light in frame.GetComponentsInChildren<Light>(true).Where(l => l.name.StartsWith("Ceiling_Fluo")))
                light.enabled = false;
            var tradeRoot = new GameObject("ClinicTradeAnchors").transform;
            SceneManager.MoveGameObjectToScene(tradeRoot.gameObject, scene);
            BindZone(clinic.ReceiveAnchor, Anchor("TradeReceive"), tradeRoot);
            BindZone(clinic.DeliveryAnchor, Anchor("TradeDeliver"), tradeRoot);
            var clinicData = new SerializedObject(clinic);
            clinicData.FindProperty("tradeSupportHeight").floatValue = -.11f;
            clinicData.ApplyModifiedPropertiesWithoutUndo();
            // The existing functional diagnostic console remains on the bench, not in the trade CRT.
            foreach (var marker in clinic.WorkAnchor.GetComponentsInChildren<Transform>(true).Where(t => t.name == "PLACEHOLDER_CONSOLE").ToArray())
                Object.DestroyImmediate(marker.gameObject);
            clinic.WorkAnchor.name = "ClinicDiagnosticWorkAnchor";
            var consoleBox = clinic.WorkAnchor.gameObject.AddComponent<BoxCollider>();
            consoleBox.center = new Vector3(0, -.08f, 0); consoleBox.size = new Vector3(.32f, .18f, .30f);
            consoleBox.isTrigger = true;
            var overview = flow.Rig.Get(FirstOrderCameraRig.Overview).pose;
            overview.position = new Vector3(.45f, 1.85f, -2.35f);
            overview.LookAt(new Vector3(.70f, 1.03f, .10f));
            var dockShot = flow.Rig.Get(FirstOrderCameraRig.Dock).pose;
            dockShot.position = dock.position + new Vector3(1.08f, 1.45f, 1);
            dockShot.LookAt(dock.position + new Vector3(.25f, .95f, .12f));
            var rigData = new SerializedObject(flow.Rig);
            var shots = rigData.FindProperty("shots");
            for (int i = 0; i < shots.arraySize; i++)
            {
                var shot = shots.GetArrayElementAtIndex(i);
                if (shot.FindPropertyRelative("id").stringValue == FirstOrderCameraRig.Overview)
                    shot.FindPropertyRelative("fov").floatValue = 78;
            }
            rigData.ApplyModifiedPropertiesWithoutUndo();
            flow.Rig.Go(FirstOrderCameraRig.Overview, true);
            BuildFirstPerson(flow, scene);

            // Add light only where the shared baseline has none; preserve the user's bench and dock lights.
            AddPoint("ClinicTradeTaskLight", new Vector3(2.4f, 2.1f, 0), new Color(1, .74f, .45f), 2.2f, 2.3f);
            AddPoint("ClinicSurgicalTaskLight", new Vector3(-.1f, 1.88f, -.05f), new Color(.83f, .9f, .85f), 2, 2.3f);
            AddPoint("ClinicEntranceFill", new Vector3(0, 2.1f, -2.5f), new Color(.55f, .69f, .71f), 1.1f, 2.8f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.08f, .10f, .11f);
            RenderSettings.ambientEquatorColor = new Color(.04f, .05f, .055f);
            RenderSettings.ambientGroundColor = new Color(.025f, .025f, .025f);
            var markerRoot = new GameObject("UnifiedClinic_RuntimeLayout_20261008");
            SceneManager.MoveGameObjectToScene(markerRoot, scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Cannot save shared clinic relayout.");
            UnifiedClinicBuilder.ExportBaseline();
            Directory.CreateDirectory("Docs/Integration/UnifiedClinic");
            var report = new IntegrationReport {
                timestampUtc = DateTime.UtcNow.ToString("O"), scene = scene.path, baselineSource = TwoNightBuilder.RobotPath,
                benchPosition = bench.position, dockPosition = dock.position, robotSeat = flow.Dock.RobotAnchor.position,
                receive = clinic.ReceiveAnchor.position, deliver = clinic.DeliveryAnchor.position,
                migratedRoots = moved.ToArray(), hiddenLegacyObjects = hidden,
                environmentRenderers = environment.GetComponentsInChildren<Renderer>(true).Length,
                limitations = new[] { "Placement/build report only; dynamic paths and full two-night flow require subsequent PlayMode regression.",
                    "Customer items still use the existing prototype inspection/transfer mechanism.",
                    "No human mouse or VR walk-through performed by this builder." }
            };
            File.WriteAllText("Docs/Integration/UnifiedClinic/gameplay_layout.json", JsonUtility.ToJson(report, true));
            Debug.Log("[UnifiedClinic] Environment and distinct bench/dock mappings saved with physical trade zones.");
        }

        static void Remap(Transform target, Matrix4x4 delta, Quaternion rotation)
        {
            target.SetPositionAndRotation(delta.MultiplyPoint3x4(target.position), rotation * target.rotation);
        }
        static void BuildFirstPerson(FirstOrderFlow flow, Scene scene)
        {
            var player = new GameObject("ClinicFirstPersonPlayer");
            SceneManager.MoveGameObjectToScene(player, scene);
            player.transform.position = new Vector3(0, .05f, -2.25f);
            var controller = player.AddComponent<CharacterController>();
            controller.radius = .18f; controller.height = 1.7f;
            controller.center = new Vector3(0, .85f, 0);
            controller.stepOffset = .15f; controller.skinWidth = .015f;
            controller.slopeLimit = 50; controller.minMoveDistance = 0;
            var eye = new GameObject("Eye").transform;
            eye.SetParent(player.transform, false);
            eye.localPosition = new Vector3(0, 1.55f, 0);
            var walker = player.AddComponent<FirstPersonWalker>();
            walker.Configure(flow.Rig, eye);
            flow.Rig.ConfigureWalker(walker);
            flow.Rig.Cam.nearClipPlane = .03f;
            EditorUtility.SetDirty(walker);
            EditorUtility.SetDirty(flow.Rig);
            // The authored door is closed; its open proxy gap must not let the player leave the room.
            var threshold = new GameObject("ClinicClosedDoorCollision", typeof(BoxCollider), typeof(FirstPersonBlocker));
            SceneManager.MoveGameObjectToScene(threshold, scene);
            threshold.transform.position = new Vector3(0, 1.2f, -3.48f);
            threshold.GetComponent<BoxCollider>().size = new Vector3(1.5f, 2.4f, .12f);
            threshold.layer = 2;
        }
        static void BindZone(Transform target, ClinicArtImporter.Anchor anchor, Transform parent)
        {
            target.SetParent(parent, true);
            target.SetPositionAndRotation(anchor.Position + Vector3.up * .11f, Quaternion.identity);
            foreach (var marker in target.Cast<Transform>().Where(t => t.name.StartsWith("PLACEHOLDER_") && t.GetComponent<TextMesh>() == null).ToArray())
                Object.DestroyImmediate(marker.gameObject);
            var caption = target.GetComponentInChildren<TextMesh>();
            if (caption != null) { caption.gameObject.name = "TradeActionLabel"; caption.transform.localPosition = new Vector3(0, .14f, 0); }
            var collider = target.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, -.035f, 0); collider.size = new Vector3(.36f, .15f, .36f);
            collider.isTrigger = true;
            target.name = "Clinic" + anchor.name + "Zone";
        }
        static void AddPoint(string name, Vector3 position, Color color, float intensity, float range)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.type = LightType.Point; light.transform.position = position;
            light.color = color; light.intensity = intensity; light.range = range; light.shadows = LightShadows.None;
        }
        public static void BuildBatch()
        {
            try { Build(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
