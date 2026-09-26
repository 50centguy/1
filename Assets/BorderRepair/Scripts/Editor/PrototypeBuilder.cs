using System;
using System.Collections.Generic;
using System.IO;
using BorderRepair.Data;
using BorderRepair.Inspection;
using BorderRepair.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 用 Unity API 生成第一阶段原型的全部资产（材质、占位 prefab、案例数据、场景、UI）。
    /// 默认只创建缺失的资产；“Rebuild” 才会覆盖由本工具生成的资产。
    /// 命令行：Unity -batchmode -quit -projectPath . -executeMethod BorderRepair.EditorTools.PrototypeBuilder.BuildFromCommandLine
    /// </summary>
    public static class PrototypeBuilder
    {
        public const string Root = "Assets/BorderRepair";
        public const string MaterialDir = Root + "/Art/Materials";
        public const string PrefabDir = Root + "/Prefabs/Items";
        public const string CaseDir = Root + "/Data/Cases";
        public const string ShiftPath = Root + "/Data/Shift_Prototype.asset";
        public const string ScenePath = Root + "/Scenes/RepairStation_Prototype.unity";

        static readonly Vector3 AnchorPosition = new Vector3(0f, 1.15f, 0.35f);
        static readonly Vector3 CameraDirection = new Vector3(0f, 0.42f, -1f).normalized;

        [MenuItem("Border Repair/Build Prototype (create missing)", priority = 0)]
        static void MenuBuild()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(false);
        }

        [MenuItem("Border Repair/Rebuild Prototype (overwrite generated)", priority = 1)]
        static void MenuRebuild()
        {
            if (!EditorUtility.DisplayDialog("重新生成原型",
                    "将覆盖 Assets/BorderRepair 下由工具生成的材质、占位 prefab、案例数据和场景，对它们的手动修改会丢失。\n\n继续吗？",
                    "覆盖", "取消")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(true);
        }

        [MenuItem("Border Repair/Open Prototype Scene", priority = 20)]
        static void MenuOpenScene()
        {
            if (!File.Exists(ScenePath)) { EditorUtility.DisplayDialog("边境维修站", "场景尚未生成，请先执行 Border Repair > Build Prototype。", "好"); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        public static void BuildFromCommandLine() => Build(false);
        public static void RebuildFromCommandLine() => Build(true);

        public static void Build(bool overwrite)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play 模式再生成原型。");

            // 在空场景中搭建，避免往用户当前打开的场景里放临时物体。
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var dir in new[] { MaterialDir, PrefabDir, CaseDir, Path.GetDirectoryName(ScenePath).Replace('\\', '/') })
                EnsureFolder(dir);

            var materials = BuildMaterials(overwrite);
            Material M(string key) => materials[key];

            var communicator = SavePrefab("Item_Communicator", () => PlaceholderItemFactory.BuildCommunicator(M), overwrite);
            var beacon = SavePrefab("Item_NavBeacon", () => PlaceholderItemFactory.BuildNavBeacon(M), overwrite);
            var drone = SavePrefab("Item_SalvageDrone", () => PlaceholderItemFactory.BuildSalvageDrone(M), overwrite);

            // 已有正式通讯器资产时，案例继续引用它，而不是退回占位 prefab。
            var communicatorFinal = AssetDatabase.LoadAssetAtPath<GameObject>(CommunicatorAssetBuilder.PrefabPath);
            var case1 = CaseAsset("Case_01_Communicator", overwrite, c => PrototypeCaseContent.FillCommunicator(c, communicatorFinal != null ? communicatorFinal : communicator));
            var beaconFinal = AssetDatabase.LoadAssetAtPath<GameObject>(NavBeaconAssetBuilder.PrefabPath);
            var case2 = CaseAsset("Case_02_NavBeacon", overwrite, c => PrototypeCaseContent.FillNavBeacon(c, beaconFinal != null ? beaconFinal : beacon));
            var droneFinal = AssetDatabase.LoadAssetAtPath<GameObject>(SalvageDroneAssetBuilder.PrefabPath);
            var case3 = CaseAsset("Case_03_SalvageDrone", overwrite, c => PrototypeCaseContent.FillSalvageDrone(c, droneFinal != null ? droneFinal : drone));
            var shift = ShiftAsset(overwrite, case1, case2, case3);
            AssetDatabase.SaveAssets();

            bool sceneExists = File.Exists(ScenePath);
            if (overwrite || !sceneExists)
            {
                BuildScene(materials, shift);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("场景保存失败：" + ScenePath);
            }
            else
            {
                EditorSceneManager.OpenScene(ScenePath);
            }

            AddSceneToBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BorderRepair] 原型生成完成（overwrite={overwrite}，场景{(overwrite || !sceneExists ? "已生成" : "已存在，未改动")}）：{ScenePath}");
        }

        // ---------- 材质 ----------

        struct MatSpec
        {
            public string Key; public Color Color; public float Metallic; public float Smoothness; public float Emission;
            public MatSpec(string key, Color color, float metallic, float smoothness, float emission = 0f)
            { Key = key; Color = color; Metallic = metallic; Smoothness = smoothness; Emission = emission; }
        }

        static readonly MatSpec[] MaterialSpecs =
        {
            // 物品
            new MatSpec("plastic_dark", new Color(0.12f, 0.13f, 0.15f), 0f, 0.35f),
            new MatSpec("plastic_grey", new Color(0.45f, 0.47f, 0.5f), 0f, 0.4f),
            new MatSpec("rubber", new Color(0.07f, 0.07f, 0.07f), 0f, 0.15f),
            new MatSpec("metal_dark", new Color(0.26f, 0.27f, 0.29f), 0.8f, 0.5f),
            new MatSpec("metal_light", new Color(0.72f, 0.74f, 0.77f), 0.9f, 0.65f),
            new MatSpec("screen", new Color(0.05f, 0.25f, 0.3f), 0f, 0.9f, 0.6f),
            new MatSpec("damaged", new Color(0.55f, 0.2f, 0.14f), 0.3f, 0.2f),
            new MatSpec("copper", new Color(0.85f, 0.5f, 0.25f), 1f, 0.6f),
            new MatSpec("hazard_yellow", new Color(0.93f, 0.68f, 0.1f), 0f, 0.45f),
            new MatSpec("lamp_glass", new Color(1f, 0.55f, 0.2f), 0f, 0.9f, 0.8f),
            new MatSpec("seal_red", new Color(0.78f, 0.1f, 0.1f), 0f, 0.3f),
            new MatSpec("scratched_metal", new Color(0.92f, 0.9f, 0.84f), 0.9f, 0.25f),
            new MatSpec("pcb_green", new Color(0.1f, 0.5f, 0.22f), 0.2f, 0.6f),
            new MatSpec("corroded", new Color(0.36f, 0.45f, 0.32f), 0.4f, 0.15f),
            new MatSpec("rust", new Color(0.5f, 0.28f, 0.1f), 0.2f, 0.1f),
            new MatSpec("burnt", new Color(0.09f, 0.06f, 0.05f), 0.2f, 0.05f),
            new MatSpec("rotor", new Color(0.2f, 0.2f, 0.22f), 0f, 0.3f),
            // 环境
            new MatSpec("env_wood", new Color(0.42f, 0.29f, 0.18f), 0f, 0.3f),
            new MatSpec("env_wall", new Color(0.3f, 0.33f, 0.32f), 0f, 0.1f),
            new MatSpec("env_floor", new Color(0.18f, 0.18f, 0.19f), 0f, 0.2f),
            new MatSpec("env_pegboard", new Color(0.55f, 0.46f, 0.33f), 0f, 0.15f),
            new MatSpec("env_pedestal", new Color(0.14f, 0.15f, 0.17f), 0.6f, 0.55f),
            new MatSpec("env_accent", new Color(0.95f, 0.55f, 0.15f), 0f, 0.4f, 0.4f),
            new MatSpec("env_bulb", new Color(1f, 0.85f, 0.6f), 0f, 0.9f, 2f),
        };

        static Dictionary<string, Material> BuildMaterials(bool overwrite)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("找不到 URP Lit 着色器，请确认项目使用 URP。");

            var result = new Dictionary<string, Material>();
            foreach (var spec in MaterialSpecs)
            {
                string path = $"{MaterialDir}/M_{spec.Key}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool isNew = mat == null;
                if (isNew)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                if (isNew || overwrite)
                {
                    mat.shader = shader;
                    mat.SetColor("_BaseColor", spec.Color);
                    mat.SetFloat("_Metallic", spec.Metallic);
                    mat.SetFloat("_Smoothness", spec.Smoothness);
                    if (spec.Emission > 0f)
                    {
                        mat.EnableKeyword("_EMISSION");
                        mat.SetColor("_EmissionColor", spec.Color * spec.Emission);
                        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                    EditorUtility.SetDirty(mat);
                }
                result[spec.Key] = mat;
            }
            return result;
        }

        // ---------- prefab / 数据 ----------

        static GameObject SavePrefab(string name, Func<GameObject> build, bool overwrite)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && !overwrite) return existing;

            var temp = build();
            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(temp, path, out bool success);
                if (!success) throw new IOException("prefab 保存失败：" + path);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        static RepairCaseData CaseAsset(string name, bool overwrite, Action<RepairCaseData> fill)
        {
            string path = $"{CaseDir}/{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<RepairCaseData>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<RepairCaseData>();
                fill(asset);
                AssetDatabase.CreateAsset(asset, path);
            }
            else if (overwrite)
            {
                fill(asset);
                EditorUtility.SetDirty(asset);
            }
            return asset;
        }

        static RepairShiftData ShiftAsset(bool overwrite, params RepairCaseData[] cases)
        {
            var asset = AssetDatabase.LoadAssetAtPath<RepairShiftData>(ShiftPath);
            bool isNew = asset == null;
            if (isNew) asset = ScriptableObject.CreateInstance<RepairShiftData>();
            if (isNew || overwrite)
            {
                asset.shiftTitle = "边境维修站";
                asset.targetDurationSeconds = 300f;
                asset.cases = new List<RepairCaseData>(cases);
            }
            if (isNew) AssetDatabase.CreateAsset(asset, ShiftPath);
            else EditorUtility.SetDirty(asset);
            return asset;
        }

        // ---------- 场景 ----------

        static void BuildScene(Dictionary<string, Material> m, RepairShiftData shift)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.36f, 0.39f, 0.44f);
            RenderSettings.ambientEquatorColor = new Color(0.26f, 0.25f, 0.23f);
            RenderSettings.ambientGroundColor = new Color(0.1f, 0.1f, 0.1f);

            var keyLight = new GameObject("Key Light").AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1f, 0.95f, 0.88f);
            keyLight.intensity = 1.1f;
            keyLight.shadows = LightShadows.Soft;
            keyLight.transform.rotation = Quaternion.Euler(45f, 20f, 0f);

            // 相机：固定视线方向，ItemInspector 只改变它到物品的距离。
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.08f);
            camGo.transform.position = AnchorPosition + CameraDirection * 0.6f;
            camGo.transform.rotation = Quaternion.LookRotation(-CameraDirection, Vector3.up);

            BuildWorkshop(m);

            var rig = new GameObject("InspectionRig");
            var anchor = new GameObject("ItemAnchor");
            anchor.transform.SetParent(rig.transform, false);
            anchor.transform.position = AnchorPosition;
            var inspector = rig.AddComponent<ItemInspector>();
            Wire(inspector, ("viewCamera", cam), ("itemAnchor", anchor.transform));
            var scanner = rig.AddComponent<ItemScanner>();
            Wire(scanner, ("inspector", inspector));

            var view = RepairUIBuilder.Build();

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var controller = new GameObject("RepairStation").AddComponent<RepairStationController>();
            Wire(controller, ("shift", shift), ("inspector", inspector), ("scanner", scanner), ("view", view));
        }

        static void BuildWorkshop(Dictionary<string, Material> m)
        {
            var env = new GameObject("Workshop").transform;
            Box(env, "Floor", new Vector3(0, -0.05f, 0.5f), new Vector3(8f, 0.1f, 8f), m["env_floor"]);
            Box(env, "BackWall", new Vector3(0, 1.6f, 1.45f), new Vector3(8f, 3.2f, 0.1f), m["env_wall"]);
            Box(env, "WallLeft", new Vector3(-2.6f, 1.6f, 0.5f), new Vector3(0.1f, 3.2f, 6f), m["env_wall"]);
            Box(env, "WallRight", new Vector3(2.6f, 1.6f, 0.5f), new Vector3(0.1f, 3.2f, 6f), m["env_wall"]);

            // 工作台
            Box(env, "BenchTop", new Vector3(0, 0.87f, 0.5f), new Vector3(2.4f, 0.06f, 1.0f), m["env_wood"]);
            Box(env, "BenchEdge", new Vector3(0, 0.85f, 0.005f), new Vector3(2.4f, 0.03f, 0.02f), m["env_accent"]);
            foreach (var x in new[] { -1.12f, 1.12f })
                foreach (var z in new[] { 0.06f, 0.94f })
                    Box(env, "BenchLeg", new Vector3(x, 0.42f, z), new Vector3(0.06f, 0.84f, 0.06f), m["metal_dark"]);

            // 检查转台
            var pedestal = PlaceholderItemFactory.Part(env, PrimitiveType.Cylinder, "InspectionPedestal", new Vector3(AnchorPosition.x, 0.91f, AnchorPosition.z), new Vector3(0.36f, 0.01f, 0.36f), m["env_pedestal"]);
            PlaceholderItemFactory.Part(pedestal.transform.parent, PrimitiveType.Cylinder, "PedestalRing", new Vector3(AnchorPosition.x, 0.912f, AnchorPosition.z), new Vector3(0.38f, 0.004f, 0.38f), m["env_accent"]);

            // 洞洞板与工具（纯几何体占位）
            Box(env, "Pegboard", new Vector3(0, 1.6f, 1.38f), new Vector3(2.2f, 1.0f, 0.03f), m["env_pegboard"]);
            Box(env, "Tool_Wrench", new Vector3(-0.7f, 1.62f, 1.35f), new Vector3(0.04f, 0.3f, 0.015f), m["metal_light"]);
            Box(env, "Tool_Screwdriver", new Vector3(-0.55f, 1.58f, 1.35f), new Vector3(0.025f, 0.24f, 0.02f), m["seal_red"]);
            Box(env, "Tool_Pliers", new Vector3(-0.4f, 1.6f, 1.35f), new Vector3(0.05f, 0.22f, 0.015f), m["metal_dark"]);
            Box(env, "Sign_Station", new Vector3(0.55f, 1.85f, 1.35f), new Vector3(0.7f, 0.18f, 0.02f), m["env_accent"]);
            Box(env, "Shelf", new Vector3(0.55f, 1.4f, 1.3f), new Vector3(0.8f, 0.03f, 0.16f), m["env_wood"]);
            for (int i = 0; i < 4; i++)
                Box(env, "PartsBin", new Vector3(0.28f + i * 0.18f, 1.47f, 1.3f), new Vector3(0.14f, 0.11f, 0.13f), m[i % 2 == 0 ? "plastic_grey" : "hazard_yellow"]);

            // 台灯
            PlaceholderItemFactory.Part(env, PrimitiveType.Cylinder, "Lamp_Base", new Vector3(-0.8f, 0.91f, 0.75f), new Vector3(0.16f, 0.01f, 0.16f), m["metal_dark"]);
            Box(env, "Lamp_Arm", new Vector3(-0.8f, 1.15f, 0.75f), new Vector3(0.025f, 0.48f, 0.025f), m["metal_dark"]);
            Box(env, "Lamp_Head", new Vector3(-0.68f, 1.38f, 0.7f), new Vector3(0.22f, 0.06f, 0.12f), m["env_accent"], new Vector3(0, 0, -15f));
            PlaceholderItemFactory.Part(env, PrimitiveType.Sphere, "Lamp_Bulb", new Vector3(-0.64f, 1.34f, 0.7f), new Vector3(0.05f, 0.03f, 0.05f), m["env_bulb"]);
            var lampLight = new GameObject("Lamp Light").AddComponent<Light>();
            lampLight.transform.SetParent(env, false);
            lampLight.transform.position = new Vector3(-0.55f, 1.3f, 0.55f);
            lampLight.type = LightType.Point;
            lampLight.color = new Color(1f, 0.8f, 0.55f);
            lampLight.intensity = 1.2f;
            lampLight.range = 2.5f;

            // 右侧零件盒
            Box(env, "Toolbox", new Vector3(0.85f, 0.97f, 0.7f), new Vector3(0.4f, 0.14f, 0.22f), m["seal_red"]);
            Box(env, "Toolbox_Handle", new Vector3(0.85f, 1.06f, 0.7f), new Vector3(0.2f, 0.03f, 0.03f), m["metal_dark"]);
        }

        static void Box(Transform parent, string name, Vector3 position, Vector3 scale, Material mat, Vector3 euler = default)
        {
            PlaceholderItemFactory.Part(parent, PrimitiveType.Cube, name, position, scale, mat, euler);
        }

        // ---------- 工具方法 ----------

        internal static void Wire(Object target, params (string field, Object value)[] references)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in references)
            {
                var prop = so.FindProperty(field);
                if (prop == null) throw new InvalidOperationException($"{target.GetType().Name} 没有序列化字段 '{field}'");
                prop.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void AddSceneToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == ScenePath)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
