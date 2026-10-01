using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Dock;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// UNIT 07 维修座第一阶段接入：导入维修座 FBX 与 3 张贴图、配置 7 个 URP 材质、制作维修座预制体、
    /// 七号停靠用预制体（RobotV4 FBX 实例 + 新动画控制器 + 转子供电 + 左引擎检查代理，不改 RobotV4 FBX）、测试场景。
    /// 菜单：Border Repair > Unit07 Dock > Build Phase 1；命令行：-executeMethod BorderRepair.EditorTools.Unit07DockBuilder.BuildAll
    /// 不导入占位体（UNIT07_RobotPlaceholder.fbx）。
    /// </summary>
    public static class Unit07DockBuilder
    {
        public const string SourceDir = "ArtSource/Unit07ServiceDock";
        public const string ArtDir = "Assets/BorderRepair/Art/Unit07ServiceDock";
        public const string TexDir = ArtDir + "/Textures";
        public const string MatDir = ArtDir + "/Materials";
        public const string DockFbx = ArtDir + "/UNIT07_ServiceDock.fbx";
        public const string PrefabDir = "Assets/BorderRepair/Prefabs/Unit07Dock";
        public const string DockPrefab = PrefabDir + "/Unit07ServiceDock.prefab";
        public const string RobotPrefab = PrefabDir + "/UNIT07_RobotV4_DockReady.prefab";
        public const string RobotController = PrefabDir + "/UNIT07_RobotV4_Dock.controller";
        public const string ScenePath = "Assets/BorderRepair/Scenes/Unit07Dock_Test.unity";
        public const string RobotFbx = "Assets/RobotV4/Model/robot-final.fbx";
        public const string ReportDir = "Docs/Integration/Unit07Dock_Phase1";
        static readonly string[] Textures = { "T_Dock_Grime.png", "T_Dock_Warning.png", "T_Dock_Labels.png" };

        [Serializable] class MatEntry { public string name, baseColor, baseMap, emissionMap, emissionColor; public float metallic, smoothness, emissionStrength; }
        [Serializable] class MatFile { public MatEntry[] materials; }

        static readonly List<string> ImportMessages = new List<string>();
        static readonly StringBuilder Log = new StringBuilder();

        [MenuItem("Border Repair/Unit07 Dock/Build Phase 1")]
        public static void BuildAll()
        {
            ImportMessages.Clear();
            Log.Clear();
            Application.logMessageReceived += Capture;
            try
            {
                CopySources();
                ConfigureTextures();
                var mats = BuildMaterials();
                ConfigureDockModel(mats);
                BuildDockPrefab();
                BuildRobotPrefab();
                BuildScene();
                WriteReport();
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void Capture(string msg, string stack, LogType type)
        {
            if (type != LogType.Log && !msg.StartsWith("[Unit07Dock]")) ImportMessages.Add($"{type}: {msg.Split('\n')[0]}");
        }

        static void Note(string s) { Log.AppendLine(s); Debug.Log("[Unit07Dock] " + s); }

        // ------------------------------------------------------------------ 1. 复制源文件（只复制维修座 FBX 与 3 张贴图）
        static void CopySources()
        {
            Directory.CreateDirectory(TexDir);
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory(PrefabDir);
            CopyIfChanged(Path.Combine(SourceDir, "Export/UNIT07_ServiceDock.fbx"), DockFbx);
            foreach (var t in Textures)
                CopyIfChanged(Path.Combine(SourceDir, "Textures", t), $"{TexDir}/{t}");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Note($"复制源文件：{DockFbx} 与 {Textures.Length} 张贴图（占位体 UNIT07_RobotPlaceholder.fbx 不导入）");
        }

        static void CopyIfChanged(string src, string dst)
        {
            if (!File.Exists(src)) throw new FileNotFoundException("缺少维修座源文件", src);
            if (File.Exists(dst) && File.ReadAllBytes(src).SequenceEqual(File.ReadAllBytes(dst))) return;
            File.Copy(src, dst, true);
        }

        // ------------------------------------------------------------------ 2. 贴图与材质
        static void ConfigureTextures()
        {
            foreach (var t in Textures)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath($"{TexDir}/{t}");
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;          // 三张都是底色贴图
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.maxTextureSize = 1024;
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.SaveAndReimport();
            }
        }

        static Dictionary<string, Material> BuildMaterials()
        {
            var file = JsonUtility.FromJson<MatFile>(File.ReadAllText(Path.Combine(SourceDir, "materials.json")));
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var mats = new Dictionary<string, Material>();
            foreach (var e in file.materials)
            {
                string p = $"{MatDir}/{e.name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) { m = new Material(lit) { name = e.name }; AssetDatabase.CreateAsset(m, p); }
                m.shader = lit;
                ColorUtility.TryParseHtmlString(e.baseColor, out var baseCol);
                m.SetColor("_BaseColor", baseCol);
                m.SetTexture("_BaseMap", string.IsNullOrEmpty(e.baseMap) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{e.baseMap}"));
                m.SetFloat("_Metallic", e.metallic);
                m.SetFloat("_Smoothness", e.smoothness);
                if (e.emissionStrength > 0f)
                {
                    ColorUtility.TryParseHtmlString(e.emissionColor, out var ec);
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", ec * e.emissionStrength);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    m.DisableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", Color.black);
                }
                EditorUtility.SetDirty(m);
                mats[e.name] = m;
            }
            AssetDatabase.SaveAssets();
            Note($"URP/Lit 材质 {mats.Count} 个：{string.Join(", ", mats.Keys)}");
            return mats;
        }

        static void ConfigureDockModel(Dictionary<string, Material> mats)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(DockFbx);
            foreach (var kv in mats)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(DockFbx);
            var mfs = model.GetComponentsInChildren<MeshFilter>(true);
            int tris = mfs.Sum(m => m.sharedMesh.triangles.Length / 3);
            var used = model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct().OrderBy(n => n).ToArray();
            int props = model.GetComponentsInChildren<DockPartProperties>(true).Length;
            Note($"维修座模型：{mfs.Length} 个网格，{tris} 个三角面，材质 {used.Length} 个 [{string.Join(", ", used)}]，带自定义属性的对象 {props} 个");
        }

        // ------------------------------------------------------------------ 3. 维修座预制体（加简化碰撞 / 点击代理）
        static readonly (string obj, DockAction action, float pad)[] Proxies =
        {
            ("Dock_ContactPad_L", DockAction.ContactPad, 1f),
            ("Dock_ContactPad_R", DockAction.ContactPad, 1f),
            ("Dock_Clamp_L_JawPad", DockAction.Clamps, 1f),
            ("Dock_Clamp_R_JawPad", DockAction.Clamps, 1f),
            ("Dock_Clamp_L_Grip", DockAction.Clamps, 1.3f),
            ("Dock_Clamp_R_Grip", DockAction.Clamps, 1.3f),
            ("Dock_PowerSwitch_LeverGrip", DockAction.PowerSwitch, 1.5f),
            ("Dock_PowerSwitch_Label", DockAction.PowerSwitch, 1f),
            ("Dock_PartsTray", DockAction.PartsTray, 1f),
            ("Dock_MagneticBox", DockAction.MagneticBox, 1f),
        };

        static void BuildDockPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(DockFbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "Unit07ServiceDock";
            foreach (var (objName, action, pad) in Proxies)
            {
                var t = Find(inst.transform, objName);
                var mf = t.GetComponent<MeshFilter>();
                var box = t.gameObject.AddComponent<BoxCollider>();
                var b = mf.sharedMesh.bounds;
                box.center = b.center;
                box.size = b.size * pad;
                var di = t.gameObject.AddComponent<DockInteractable>();
                di.action = action;
                if (action == DockAction.PartsTray || action == DockAction.MagneticBox)
                    di.pickable = t.gameObject.AddComponent<DockPickable>();
            }
            PrefabUtility.SaveAsPrefabAsset(inst, DockPrefab);
            Object.DestroyImmediate(inst);
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(DockPrefab);
            Note($"维修座预制体：{DockPrefab}；碰撞 / 点击代理 {saved.GetComponentsInChildren<Collider>(true).Length} 个（只加在接触垫、夹面、夹具握把、开关握把与铭牌、零件盘、磁性盒上）");
        }

        // ------------------------------------------------------------------ 4. 七号停靠用预制体（不改 RobotV4 FBX）
        static void BuildRobotPrefab()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(RobotFbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).OrderBy(c => c.name).ToArray();
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(RobotController) != null) AssetDatabase.DeleteAsset(RobotController);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(RobotController);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var c in clips) ctrl.AddMotion(c);
            sm.defaultState = sm.states.First(s => s.state.name == "Idle_Hover").state;

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(RobotFbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = "UNIT07_RobotV4";
            var anim = inst.GetComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // 停靠逻辑依赖骨骼位置；镜头外也要更新

            // 转子转轴：从 Idle_Hover 实测（第 0 帧与第 1 帧的相对旋转）
            var idle = clips.First(c => c.name == "Idle_Hover");
            var rotors = new[] { Find(inst.transform, "Engine_L_Rotor"), Find(inst.transform, "Engine_R_Rotor") };
            var axes = new Vector3[2];
            float speed = 0f;
            for (int i = 0; i < 2; i++)
            {
                idle.SampleAnimation(inst, 0f);
                var q0 = rotors[i].localRotation;
                idle.SampleAnimation(inst, 1f / 30f);
                var q1 = rotors[i].localRotation;
                (Quaternion.Inverse(q0) * q1).ToAngleAxis(out float ang, out Vector3 axis);
                if (ang > 180f) { ang = 360f - ang; axis = -axis; }
                axes[i] = axis.normalized;
                speed = ang * 30f;
            }
            idle.SampleAnimation(inst, 0f);
            var driver = inst.AddComponent<RotorPowerDriver>();
            driver.Configure(rotors, axes, Mathf.Round(speed));
            Note($"转子转轴（本地）：L {axes[0]:F3}，R {axes[1]:F3}；悬停转速 {Mathf.Round(speed)}°/s（Idle_Hover 实测）");

            // 左引擎检查代理：贴在左引擎上盖范围上，挂在左引擎铰轴骨骼下，随引擎移动；不属于维修座
            var hinge = Find(inst.transform, "Engine_L_Hinge");
            var cover = inst.GetComponentsInChildren<Renderer>(true).First(r => r.name == "Engine_UpperCover_L");
            var proxy = new GameObject("Unit07_EngineL_InspectProxy");
            proxy.transform.SetParent(hinge, false);
            proxy.transform.position = cover.bounds.center;
            proxy.transform.rotation = Quaternion.identity;
            var pb = proxy.AddComponent<BoxCollider>();
            var ls = proxy.transform.lossyScale;
            pb.size = new Vector3(cover.bounds.size.x / ls.x, cover.bounds.size.y / ls.y, cover.bounds.size.z / ls.z);
            proxy.AddComponent<DockInteractable>().action = DockAction.EngineLeft;

            PrefabUtility.SaveAsPrefabAsset(inst, RobotPrefab);
            Object.DestroyImmediate(inst);
            Note($"七号停靠预制体：{RobotPrefab}（基于 {RobotFbx}，FBX 未改动）；动画控制器 {RobotController}：{clips.Length} 个原有片段，默认 Idle_Hover");
        }

        // ------------------------------------------------------------------ 5. 测试场景
        static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.38f, 0.40f, 0.44f);

            var sun = new GameObject("Key Light").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.3f; sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, 150f, 0f);
            var fill = new GameObject("Fill Light").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 0.45f; fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(20f, -40f, 0f);

            var floorMatPath = $"{MatDir}/M_Unit07Test_Floor.mat";
            var floorMat = AssetDatabase.LoadAssetAtPath<Material>(floorMatPath);
            if (floorMat == null) { floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(floorMat, floorMatPath); }
            floorMat.SetColor("_BaseColor", new Color(0.34f, 0.35f, 0.37f));
            floorMat.SetFloat("_Smoothness", 0.25f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 0.4f;
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            Object.DestroyImmediate(floor.GetComponent<Collider>());   // 地面不参与点击

            var dock = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DockPrefab));
            dock.transform.position = Vector3.zero;
            var anchor = Find(dock.transform, "Dock_RobotAnchor");
            var robot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefab));
            robot.transform.position = anchor.position;           // Start() 时再抬到悬停高度
            robot.transform.rotation = Quaternion.identity;
            robot.transform.localScale = Vector3.one;

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 32f; cam.nearClipPlane = 0.02f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.20f, 0.22f, 0.25f);
            camGo.transform.position = new Vector3(0.95f, 1.5f, 2.3f);    // 机器人正面朝 +Z：镜头在正面偏右
            camGo.transform.LookAt(new Vector3(0f, 0.95f, 0f));
            var hold = new GameObject("HoldPoint").transform;
            hold.SetParent(camGo.transform, false);
            hold.localPosition = new Vector3(0.18f, -0.22f, 0.6f);
            foreach (var p in dock.GetComponentsInChildren<DockPickable>(true))
            {
                p.HoldPoint = hold;
                EditorUtility.SetDirty(p);
            }

            var flow = new GameObject("Unit07DockFlow");
            var ctrl = flow.AddComponent<Unit07DockController>();
            ctrl.Configure(robot.transform, robot.GetComponent<Animator>(), robot.GetComponent<RotorPowerDriver>(), anchor,
                           Find(dock.transform, "Dock_Clamp_L"), Find(dock.transform, "Dock_Clamp_R"),
                           Find(dock.transform, "Dock_PowerSwitch_Lever"), Find(dock.transform, "Dock_PowerSwitch_Lamp").GetComponent<Renderer>());
            flow.AddComponent<Unit07DockInput>().Configure(cam, ctrl);
            // 可编辑动画与动作配置（持久资产：已存在的不覆盖，这里只引用）
            BorderRepair.Motion.EditorTools.Unit07EditableAnimation.ApplyToDock(ctrl, BorderRepair.Motion.EditorTools.Unit07EditableAnimation.EnsureAll());
            EditorUtility.SetDirty(ctrl);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Note($"测试场景：{ScenePath}；七号根对齐 Dock_RobotAnchor {anchor.position:F3}，缩放 1，正面 +Z");
        }

        // ------------------------------------------------------------------ 报告
        static void WriteReport()
        {
            Directory.CreateDirectory(ReportDir);
            var sb = new StringBuilder();
            sb.AppendLine("UNIT 07 维修座第一阶段接入 · 构建记录");
            sb.AppendLine($"Unity {Application.unityVersion}");
            sb.Append(Log);
            sb.AppendLine($"构建期间控制台警告 / 错误：{ImportMessages.Count}");
            foreach (var m in ImportMessages.Distinct()) sb.AppendLine("  " + m);
            File.WriteAllText(Path.Combine(ReportDir, "build_log.txt"), sb.ToString(), new UTF8Encoding(false));
        }

        public static Transform Find(Transform root, string name)
        {
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            if (t == null) throw new InvalidOperationException($"找不到对象 {name}");
            return t;
        }
    }
}
