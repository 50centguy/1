using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using BorderRepair.Data;
using BorderRepair.EditorTools;
using BorderRepair.FirstOrder;
using BorderRepair.FirstOrder.Slice;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BorderRepair.TwoNight.EditorTools
{
    public static class UnifiedClinicBuilder
    {
        public const string SceneDirectory = "Assets/BorderRepair/Scenes/UnifiedClinic";
        public const string ClinicPath = SceneDirectory + "/UnifiedClinic.unity";
        public const string MenuPath = SceneDirectory + "/UnifiedClinic_Menu.unity";
        const string ScratchPath = SceneDirectory + "/PrototypeConsole_Import.unity";

        [MenuItem("Border Repair/Two-Night Slice/Build Unified Clinic From Current N2 Snapshot")]
        public static void Build()
        {
            EnsureFolder(SceneDirectory);
            if (EditorSceneManager.GetSceneManagerSetup().Any(s => s.isLoaded && SceneManager.GetSceneByPath(s.path).isDirty))
                throw new InvalidOperationException("Save open scene edits before building the unified clinic.");

            // Save the CURRENT N2 scene to a new path before touching any of its objects.
            var scene = EditorSceneManager.OpenScene(TwoNightBuilder.RobotPath, OpenSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, ClinicPath)) throw new InvalidOperationException("Cannot save the shared scene copy.");
            var legacyRoots = scene.GetRootGameObjects();
            var legacyFrame = new GameObject("ClinicLegacyRoomFrame").transform;
            foreach (var root in legacyRoots) root.transform.SetParent(legacyFrame, true);
            var flow = One<FirstOrderFlow>(scene);
            var input = One<FirstOrderInput>(scene);
            var view = One<SliceView>(scene);
            var robot = One<TwoNightRobotDirector>(scene);
            var eco = AssetDatabase.LoadAssetAtPath<TwoNightEconomy>(TwoNightBuilder.EconomyPath);
            var setup = ImportConsole(scene);
            var station = setup.GetComponentInChildren<RepairStationController>(true);
            var shift = new SerializedObject(station).FindProperty("shift").objectReferenceValue as RepairShiftData;
            if (eco == null || shift == null || shift.cases == null || shift.cases.Count == 0 ||
                shift.cases.Any(c => c == null || string.IsNullOrEmpty(c.caseId)) ||
                shift.cases.Select(c => c.caseId).Distinct().Count() != shift.cases.Count)
                throw new InvalidOperationException("Expected an authored shift with unique case IDs. No synthetic queue is generated.");
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/BorderRepair/FirstOrder/Art/Fonts/NotoSansSC-Regular.ttf");
            if (font == null)
                font = AssetDatabase.FindAssets("t:Font").Select(g => AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(g)))
                    .FirstOrDefault(f => f.name.Contains("Noto") || f.name.Contains("SourceHan"));
            if (font == null) throw new InvalidOperationException("Missing bundled CJK font.");

            var anchors = BuildAnchors(scene);
            var inspectorData = new SerializedObject(station.Inspector);
            var itemAnchor = (Transform)inspectorData.FindProperty("itemAnchor").objectReferenceValue;
            var consoleCam = station.Inspector.ViewCamera;
            var cameraOffset = consoleCam.transform.position - itemAnchor.position;
            // Orbiting the item must never rotate bench labels or put the camera inside room geometry.
            var inspectionStage = new GameObject("ClinicInspectionStage").transform;
            inspectionStage.SetParent(setup.transform, false);
            inspectionStage.position = new Vector3(0, 1.2f, 30);
            itemAnchor.SetParent(inspectionStage, false);
            itemAnchor.localPosition = Vector3.zero;
            itemAnchor.localRotation = Quaternion.identity;
            consoleCam.transform.SetParent(inspectionStage, false);
            consoleCam.transform.localPosition = cameraOffset;
            consoleCam.transform.LookAt(itemAnchor);
            consoleCam.farClipPlane = 5;
            consoleCam.clearFlags = CameraClearFlags.SolidColor;
            consoleCam.backgroundColor = new Color(.06f, .07f, .08f);
            var front = cameraOffset.normalized;
            var right = Vector3.Cross(Vector3.up, -front).normalized;
            InspectionLight(inspectionStage, "InspectionKey", front * .7f - right * .4f + Vector3.up * .4f,
                new Color(1, .9f, .78f), 2.5f);
            InspectionLight(inspectionStage, "InspectionFill", front * .4f + right * .6f + Vector3.up * .2f,
                new Color(.7f, .82f, 1), 1.5f);
            consoleCam.tag = "Untagged";
            foreach (var listener in consoleCam.GetComponents<AudioListener>()) listener.enabled = false;
            foreach (var listener in setup.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            // Dependencies are imported once; the shared room retains its own light, camera and robot.
            var coordinator = new GameObject("UnifiedClinicDirector").AddComponent<UnifiedClinicDirector>();
            SceneManager.MoveGameObjectToScene(coordinator.gameObject, scene);
            var counter = new GameObject("UnifiedClinicCounterDirector").AddComponent<TwoNightCounterDirector>();
            SceneManager.MoveGameObjectToScene(counter.gameObject, scene);
            counter.Configure(station, eco, font);
            counter.ConfigureUnified(coordinator);
            robot.ConfigureUnified();
            coordinator.Configure(station, setup, counter, robot, flow, input, view, eco, font, flow.Rig.Cam,
                anchors.receive, anchors.work, anchors.delivery, anchors.camera);
            AddZone(anchors.receive, coordinator, ClinicTradeAction.Receive, "RECEIVE", new Color(0.16f, 0.66f, 0.55f));
            AddZone(anchors.delivery, coordinator, ClinicTradeAction.Deliver, "SEND OUT", new Color(0.78f, 0.40f, 0.28f));
            AddZone(anchors.work, coordinator, ClinicTradeAction.Console, "CONSOLE", new Color(0.31f, 0.55f, 0.79f));
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Cannot save configured clinic.");

            // Clone the modified menu through scene APIs, keeping its legacy counterpart untouched.
            var menuScene = EditorSceneManager.OpenScene(TwoNightBuilder.MenuPath, OpenSceneMode.Single);
            if (!EditorSceneManager.SaveScene(menuScene, MenuPath)) throw new InvalidOperationException("Cannot save shared menu copy.");
            One<TwoNightMenu>(menuScene).ConfigureUnified();
            EditorSceneManager.SaveScene(menuScene);
            AssetDatabase.SaveAssets();
            var routes = new[] { MenuPath, ClinicPath };
            EditorBuildSettings.scenes = routes.Select(p => new EditorBuildSettingsScene(p, true))
                .Concat(EditorBuildSettings.scenes.Where(s => !routes.Contains(s.path))
                    .Select(s => new EditorBuildSettingsScene(s.path, s.enabled))).ToArray();
            EditorSceneManager.OpenScene(ClinicPath, OpenSceneMode.Single);
            ExportBaseline();
            Debug.Log("[UnifiedClinic] Built shared N1/N2 room from current Unit07_Night; original console and robot references retained.");
        }

        static void InspectionLight(Transform stage, string name, Vector3 position, Color color, float intensity)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(stage, false);
            light.transform.localPosition = position;
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = 3;
        }

        [Serializable] class Baseline { public string scene; public List<WorldFrame> frames = new List<WorldFrame>(); }
        [Serializable] class WorldFrame
        {
            public string path;
            public Vector3 position, scale;
            public Quaternion rotation;
        }

        public static void ExportBaseline()
        {
            var scene = SceneManager.GetActiveScene();
            var report = new Baseline { scene = scene.path };
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string path = t.name;
                    for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
                    report.frames.Add(new WorldFrame { path = path, position = t.position, rotation = t.rotation, scale = t.lossyScale });
                }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/UnifiedClinicBaseline.json", JsonUtility.ToJson(report, true));
        }

        // Rigid whole-room moves only; independent bench/dock art alignment needs separate remapping.
        public static void RelocateLegacyRoom(Vector3 position, Quaternion rotation)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Relocate the clinic before Play.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ClinicPath) throw new InvalidOperationException("Open the shared clinic before relocating its frame.");
            var frame = scene.GetRootGameObjects().Single(g => g.name == "ClinicLegacyRoomFrame").transform;
            var previous = frame.worldToLocalMatrix;
            var rotationDelta = rotation * Quaternion.Inverse(frame.rotation);
            frame.SetPositionAndRotation(position, rotation);
            var delta = frame.localToWorldMatrix * previous;
            var incident = new SerializedObject(One<TrayIncident>(scene));
            foreach (var name in new[] { "hover", "transitOverDock", "transitOverShelf", "place", "release", "withdraw", "withdrawUp" })
            {
                var point = incident.FindProperty(name);
                point.vector3Value = delta.MultiplyPoint3x4(point.vector3Value);
            }
            var facing = incident.FindProperty("hoverRot");
            facing.quaternionValue = rotationDelta * facing.quaternionValue;
            incident.ApplyModifiedPropertiesWithoutUndo();
            var flow = One<FirstOrderFlow>(scene);
            var approach = flow.NewBearingBenchApproach;
            flow.ConfigureNewBearingApproach(approach.use, delta.MultiplyPoint3x4(approach.point));
            foreach (var zone in frame.GetComponentsInChildren<FirstOrderDropZone>(true))
            {
                zone.approachPoint = delta.MultiplyPoint3x4(zone.approachPoint);
                zone.landingOffset = delta.MultiplyVector(zone.landingOffset);
                zone.landingRotation = rotationDelta * zone.landingRotation;
                EditorUtility.SetDirty(zone);
            }
            EditorUtility.SetDirty(flow);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        public static void BuildBatch()
        {
            try { Build(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static T One<T>(Scene scene) where T : Component
        {
            var matches = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException($"Expected one {typeof(T).Name} in {scene.path}, found {matches.Length}.");
            return matches[0];
        }

        static GameObject ImportConsole(Scene target)
        {
            string path = File.Exists(TwoNightBuilder.CounterPath) ? TwoNightBuilder.CounterPath : PrototypeBuilder.ScenePath;
            if (!AssetDatabase.CopyAsset(path, ScratchPath))
                throw new InvalidOperationException("Cannot import current console snapshot.");
            var source = EditorSceneManager.OpenScene(ScratchPath, OpenSceneMode.Additive);
            if (!source.GetRootGameObjects().Any(g => g.GetComponentInChildren<RepairStationController>(true) != null))
            {
                EditorSceneManager.CloseScene(source, true);
                AssetDatabase.DeleteAsset(ScratchPath);
                if (!AssetDatabase.CopyAsset(PrototypeBuilder.ScenePath, ScratchPath)) throw new InvalidOperationException("Prototype console fallback unavailable.");
                source = EditorSceneManager.OpenScene(ScratchPath, OpenSceneMode.Additive);
            }
            try
            {
                var station = One<RepairStationController>(source);
                var roots = new HashSet<GameObject> { station.transform.root.gameObject };
                // Follow serialized scene references transitively before moving their roots together.
                bool expanded;
                do
                {
                    expanded = false;
                    foreach (var root in roots.ToArray())
                        foreach (var component in root.GetComponentsInChildren<Component>(true).Where(c => c != null))
                        {
                            var property = new SerializedObject(component).GetIterator();
                            while (property.Next(true))
                            {
                                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                                var reference = property.objectReferenceValue;
                                var obj = reference as GameObject ?? (reference as Component)?.gameObject;
                                if (obj != null && obj.scene == source && roots.Add(obj.transform.root.gameObject)) expanded = true;
                            }
                        }
                } while (expanded);
                var group = new GameObject("PrototypeRepairConsole_Setup");
                SceneManager.MoveGameObjectToScene(group, target);
                foreach (var root in roots)
                {
                    SceneManager.MoveGameObjectToScene(root, target);
                    root.transform.SetParent(group.transform, true);
                }
                foreach (var legacyDirector in group.GetComponentsInChildren<TwoNightCounterDirector>(true))
                    UnityEngine.Object.DestroyImmediate(legacyDirector);
                return group;
            }
            finally { EditorSceneManager.CloseScene(source, true); AssetDatabase.DeleteAsset(ScratchPath); }
        }

        static (Transform receive, Transform work, Transform delivery, Transform camera) BuildAnchors(Scene scene)
        {
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            Transform Named(string name) => all.FirstOrDefault(t => t.name == name);
            var bench = Named("Bench_Top") ?? Named("RP_Bench");
            if (bench == null) throw new InvalidOperationException("Current N2 room has no bench for fallback anchors.");
            var renderer = bench.GetComponent<Renderer>();
            var bounds = renderer != null ? renderer.bounds : new Bounds(bench.position, Vector3.one);
            var root = Named("ClinicTradeAnchors");
            if (root == null)
            {
                root = new GameObject("ClinicTradeAnchors_PLACEHOLDER").transform;
                SceneManager.MoveGameObjectToScene(root.gameObject, scene);
                root.SetParent(bench, true);
                root.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z), Quaternion.identity);
            }
            Transform Anchor(string name, Vector3 local)
            {
                var existing = Named(name);
                if (existing != null) return existing;
                var anchor = new GameObject(name).transform;
                anchor.SetParent(root, false);
                anchor.localPosition = local;
                return anchor;
            }
            float span = Mathf.Max(0.35f, bounds.extents.x * 0.65f);
            return (Anchor("ClinicTradeReceiveAnchor", new Vector3(-span, 0.16f, 0)),
                Anchor("ClinicTradeWorkAnchor", new Vector3(0, 0.30f, 0)),
                Anchor("ClinicTradeDeliverAnchor", new Vector3(span, 0.16f, 0)),
                Anchor("ClinicConsoleCameraAnchor", new Vector3(0, 0.30f, 0)));
        }

        static void AddZone(Transform anchor, UnifiedClinicDirector clinic, ClinicTradeAction action, string label, Color color)
        {
            var zone = anchor.gameObject.AddComponent<ClinicTradeZone>();
            zone.Configure(clinic, action);
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "PLACEHOLDER_" + label.Replace(" ", "_");
            marker.transform.SetParent(anchor, false);
            marker.transform.localPosition = new Vector3(0, -0.05f, 0);
            marker.transform.localScale = new Vector3(0.26f, 0.08f, 0.16f);
            var path = SceneDirectory + "/M_Trade_" + action + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { color = color };
                AssetDatabase.CreateAsset(material, path);
            }
            marker.GetComponent<Renderer>().sharedMaterial = material;
            var caption = new GameObject("PLACEHOLDER_Label").AddComponent<TextMesh>();
            caption.transform.SetParent(anchor, false);
            caption.transform.localPosition = new Vector3(0, 0.12f, 0);
            caption.transform.localScale = Vector3.one * 0.02f;
            var camera = clinic.Robot.Flow.Rig.Cam.transform;
            caption.transform.rotation = Quaternion.LookRotation(caption.transform.position - camera.position, camera.up);
            caption.text = label; caption.fontSize = 40; caption.anchor = TextAnchor.MiddleCenter;
            zone.ConfigureLabel(caption);
        }

        static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(name).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            string parent = parts[0];
            foreach (var part in parts.Skip(1))
            {
                var next = parent + "/" + part;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, part);
                parent = next;
            }
        }
    }
}
