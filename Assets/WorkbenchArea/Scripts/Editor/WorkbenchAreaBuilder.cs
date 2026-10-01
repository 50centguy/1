using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace WorkbenchArea.EditorTools
{
    /// <summary>
    /// 地下义体医生维修工作台区域 · Unity 独立测试场景构建。
    /// 从 ArtSource/WorkbenchArea 复制 FBX 与 materials.json 用到的贴图 → 按 materials.json 建 URP Simple Lit 材质（漫反射、无金属、点采样）→
    /// 预制体（静态碰撞 + 可检查对象的占位触发区 + 被挡对象说明）→ 测试场景（暖色主光与反弹、冷色顶灯、三色渐变环境光、色调映射、两台固定镜头、占位交互、鼠标记录）。
    /// 只写 Assets/WorkbenchArea/ 与 ArtSource/WorkbenchArea/Reports/，不碰正式维修场景、工单代码或 RobotV4。
    /// 菜单：Workbench Area > Build Test Scene；命令行：-executeMethod WorkbenchArea.EditorTools.WorkbenchAreaBuilder.BuildAll
    /// </summary>
    public static class WorkbenchAreaBuilder
    {
        public const string SourceDir = "ArtSource/WorkbenchArea";
        public const string Root = "Assets/WorkbenchArea";
        public const string ArtDir = Root + "/Art";
        public const string TexDir = ArtDir + "/Textures";
        public const string MatDir = ArtDir + "/Materials";
        public const string Fbx = ArtDir + "/WorkbenchArea.fbx";
        public const string Prefab = Root + "/Prefabs/WorkbenchArea.prefab";
        public const string ScenePath = Root + "/Scenes/WorkbenchArea_Test.unity";
        public const string ReportDir = SourceDir + "/Reports/Unity";
        public const string PostProfile = ArtDir + "/WB_PostProfile.asset";

        /// <summary>materials.json 里用到的全部贴图（底色与自发光），不再手写清单。</summary>
        public static string[] Textures => JsonUtility.FromJson<MatFile>(File.ReadAllText(Path.Combine(SourceDir, "materials.json"))).materials
            .SelectMany(m => new[] { m.baseMap, m.emissionMap }).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).ToArray();

        // 与 build_workbench_area.py 中的 CAMERAS 一致（Blender (x, y, z) → Unity (-x, z, -y)）
        public static readonly Vector3 GameCamPos = B2U(0.0f, -0.80f, 1.62f), GameCamTarget = B2U(0.0f, 0.48f, 0.92f);
        public static readonly Vector3 CloseCamPos = B2U(0.06f, -0.20f, 1.42f), CloseCamTarget = B2U(0.03f, 0.42f, 0.95f);
        public const float GameFov = 50f, CloseFov = 44f;
        public static readonly Vector3 ScrewTraySlot = B2U(0.47f, 0.30f, 0.912f);
        // 盖板停放框：Blender (0.125, 0.235)–(0.375, 0.365)，250 × 130 mm；盖板 220 × 68 mm 落在中心，四周至少 15 mm 余量
        public static readonly Vector3 CoverPark = B2U(0.25f, 0.30f, 0.904f);
        public static readonly Vector3 LampTarget = B2U(-0.04f, 0.44f, 0.904f);

        public static Vector3 B2U(float x, float y, float z) => new Vector3(-x, z, -y);

        // 角色分组
        public static readonly HashSet<string> SolidRoles = new HashSet<string> { "static", "storage" };
        public static readonly HashSet<string> SolidExtra = new HashSet<string>
            { "Diag_Body", "Toolbox_Base", "Bench_Drawer_1_Probes", "Bench_Drawer_2", "Bench_Drawer_3", "Bench_CabinetDoor" };
        public static readonly HashSet<string> ZoneRoles = new HashSet<string>
            { "inspectable", "inspectable_record", "removable", "placeholder", "placeholder_inspectable", "placeholder_removable",
              "fault_indicator", "indicator", "record" };
        public static readonly HashSet<string> StaticRoles = new HashSet<string> { "static", "storage", "clutter", "record", "label" };
        public const float ZonePad = 0.006f, ZoneMin = 0.022f;
        /// <summary>挡板 → （被挡对象要先做什么，怎样移开挡板）。</summary>
        static readonly Dictionary<string, (string action, string how)> BlockedText = new Dictionary<string, (string, string)>
        {
            ["Toolbox_Tier2"] = ("先移开上层才能取用", "点击工具箱上层或按 U 先移开它"),
            ["Placeholder_Prosthetic_Cover"] = ("先拆下盖板才能检查", "按 D 演示拆下螺钉和盖板"),
        };

        [Serializable] class MatEntry { public string name, baseColor, baseMap, emissionMap, emissionColor, filterMode; public float metallic, smoothness, emissionStrength; }
        [Serializable] class MatFile { public MatEntry[] materials; }

        static readonly List<string> ImportMessages = new List<string>();
        static readonly StringBuilder Log = new StringBuilder();

        [MenuItem("Workbench Area/Build Test Scene")]
        public static void BuildAll()
        {
            ImportMessages.Clear();
            Log.Clear();
            Application.logMessageReceived += Capture;
            try
            {
                CopySources();
                var mats = BuildMaterials();
                ConfigureModel(mats);
                BuildPrefab();
                BuildScene();
                WriteReport();
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }
            if (Application.isBatchMode) EditorApplication.Exit(ImportMessages.Any(m => m.StartsWith("Error") || m.StartsWith("Exception")) ? 2 : 0);
        }

        static void Capture(string msg, string stack, LogType type)
        {
            if (type != LogType.Log && !msg.StartsWith("[WorkbenchArea]")) ImportMessages.Add($"{type}: {msg.Split('\n')[0]}");
        }

        static void Note(string s) { Log.AppendLine(s); Debug.Log("[WorkbenchArea] " + s); }

        // ------------------------------------------------------------------ 1. 复制源文件
        static void CopySources()
        {
            foreach (var d in new[] { TexDir, MatDir, Root + "/Prefabs", Root + "/Scenes" }) Directory.CreateDirectory(d);
            CopyIfChanged(Path.Combine(SourceDir, "Export/WorkbenchArea.fbx"), Fbx);
            var textures = Textures;
            foreach (var t in textures) CopyIfChanged(Path.Combine(SourceDir, "Textures", t), $"{TexDir}/{t}");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var t in textures) AssetDatabase.ImportAsset($"{TexDir}/{t}", ImportAssetOptions.ForceUpdate);   // 让后处理的点采样设置生效
            Note($"复制源文件：{Fbx} 与 {textures.Length} 张贴图（{string.Join(", ", textures)}）");
        }

        static void CopyIfChanged(string src, string dst)
        {
            if (!File.Exists(src)) throw new FileNotFoundException("缺少工作台源文件（先运行 ArtSource/WorkbenchArea/run_blender.bat）", src);
            if (File.Exists(dst) && File.ReadAllBytes(src).SequenceEqual(File.ReadAllBytes(dst))) return;
            File.Copy(src, dst, true);
        }

        // ------------------------------------------------------------------ 2. 材质：URP Simple Lit，无高光、无金属，点采样贴图
        static Dictionary<string, Material> BuildMaterials()
        {
            var file = JsonUtility.FromJson<MatFile>(File.ReadAllText(Path.Combine(SourceDir, "materials.json")));
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            var mats = new Dictionary<string, Material>();
            foreach (var e in file.materials)
            {
                string p = $"{MatDir}/{e.name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) { m = new Material(shader) { name = e.name }; AssetDatabase.CreateAsset(m, p); }
                m.shader = shader;
                ColorUtility.TryParseHtmlString(e.baseColor, out var baseCol);
                m.SetColor("_BaseColor", baseCol);
                m.SetTexture("_BaseMap", string.IsNullOrEmpty(e.baseMap) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{e.baseMap}"));
                // 纯漫反射，不做写实 PBR。脚本建的 Simple Lit 材质会带着 _SPECULAR_COLOR 与 0.5 灰高光色，
                // 低光滑度下变成整片偏冷的高光，把深色件洗成和墙一样亮（洞洞板工具“消失”）。高光色清零，关键字也关掉。
                m.SetFloat("_SpecularHighlights", 0f);
                m.SetColor("_SpecColor", new Color(0f, 0f, 0f, e.smoothness));
                m.DisableKeyword("_SPECULAR_COLOR");
                m.DisableKeyword("_SPECGLOSSMAP");
                m.SetFloat("_Smoothness", e.smoothness);
                m.SetFloat("_EnvironmentReflections", 0f);
                if (e.emissionStrength > 0f)
                {
                    ColorUtility.TryParseHtmlString(e.emissionColor, out var ec);
                    m.EnableKeyword("_EMISSION");
                    // 测试场景无 Bloom / HDR 后处理：强度 > 1 会被截成近白色，红色故障灯就不再是红色，所以封顶 1
                    m.SetColor("_EmissionColor", ec * Mathf.Min(e.emissionStrength, 1f));
                    m.SetTexture("_EmissionMap", string.IsNullOrEmpty(e.emissionMap) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{e.emissionMap}"));
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    m.DisableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", Color.black);
                    m.SetTexture("_EmissionMap", null);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                }
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                mats[e.name] = m;
            }
            AssetDatabase.SaveAssets();
            Note($"URP Simple Lit 材质 {mats.Count} 个（无高光、无金属）：{string.Join(", ", mats.Keys)}");
            return mats;
        }

        static void ConfigureModel(Dictionary<string, Material> mats)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            foreach (var kv in mats)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var mfs = model.GetComponentsInChildren<MeshFilter>(true);
            int tris = mfs.Sum(m => m.sharedMesh.triangles.Length / 3);
            var used = model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct().OrderBy(n => n).ToArray();
            int props = model.GetComponentsInChildren<WbPartProperties>(true).Count(p => p.Role.Length > 0);
            Note($"模型：{mfs.Length} 个网格，{tris} 个三角面，材质 {used.Length} 个，带 wb_role 的对象 {props} 个");
        }

        // ------------------------------------------------------------------ 3. 预制体：静态碰撞 + 占位检查区
        public static string RoleOf(Transform t)
        {
            var p = t.GetComponent<WbPartProperties>();
            return p != null ? p.Role : "";
        }

        static void BuildPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            inst.name = "WorkbenchArea";

            // 托盘 / 工具箱各层里的内容跟着本体走（Blender 里是同组兄弟对象）
            var all = inst.GetComponentsInChildren<Transform>(true).ToList();
            foreach (var host in new[] { "Tray_Screws", "Tray_OldParts", "Toolbox_Tier1", "Toolbox_Tier2" })
            {
                var h = Find(inst.transform, host);
                foreach (var c in all.Where(x => x != h && x.name.StartsWith(host + "_"))) c.SetParent(h, true);
            }

            int solid = 0, zones = 0, statics = 0;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                var t = mf.transform;
                string role = RoleOf(t);
                bool isSolid = SolidRoles.Contains(role) || SolidExtra.Contains(t.name);
                if (isSolid)
                {
                    var mc = t.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    solid++;
                }
                if (ZoneRoles.Contains(role))
                {
                    var z = new GameObject("InspectZone");
                    z.transform.SetParent(t, false);
                    if (!isSolid)   // 已有精确实体碰撞的对象（机身、底座、抽屉、柜门）直接用实体表面点选，不再套一个会盖住面板小件的包围盒
                    {
                        var box = z.AddComponent<BoxCollider>();
                        box.isTrigger = true;
                        var b = mf.sharedMesh.bounds;
                        box.center = b.center;
                        var s = b.size + Vector3.one * ZonePad;
                        box.size = new Vector3(Mathf.Max(s.x, ZoneMin), Mathf.Max(s.y, ZoneMin), Mathf.Max(s.z, ZoneMin));
                    }
                    var wi = z.AddComponent<WbInspectable>();
                    wi.target = t; wi.role = role;
                    wi.displayName = role.StartsWith("placeholder") || t.name.StartsWith("Placeholder_") ? "【占位】" + t.name : t.name;
                    zones++;
                }
                if (StaticRoles.Contains(role) && !t.name.EndsWith("_Contents"))
                {
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                    statics++;
                }
            }
            // 被挡住的部件（Blender 属性 blocked_by）：保留检查区，悬停时明确提示要先做什么，点击不执行动作
            var blocked = new List<string>();
            foreach (var wi in inst.GetComponentsInChildren<WbInspectable>(true))
            {
                var by = wi.target.GetComponent<WbPartProperties>()?.Get("blocked_by");
                if (string.IsNullOrEmpty(by)) continue;
                var blocker = Find(inst.transform, by).GetComponentsInChildren<WbInspectable>(true).First(w => w.target.name == by);
                var (action, how) = BlockedText.TryGetValue(by, out var t) ? t : ("先移开挡住它的部件", "");
                wi.blockedBy = blocker;
                wi.blockedAction = action;
                wi.hint = $"被 {by} 挡住：{action}（{how}，占位）";
                blocker.hint = $"下面的 {wi.target.name} 被它挡住：{how}（占位）";
                blocked.Add($"{wi.target.name} ← {by}");
            }
            PrefabUtility.SaveAsPrefabAsset(inst, Prefab);
            Object.DestroyImmediate(inst);
            Note($"被挡住、须先移开挡板的对象 {blocked.Count} 个：{string.Join("；", blocked)}");
            Note($"预制体 {Prefab}：静态 MeshCollider {solid} 个（墙地、台体、抽屉、柜门、货架、洞洞板、诊断仪机身、工具箱底座）；" +
                 $"可检查对象 {zones} 个（无实体碰撞的配占位触发盒，补 {ZonePad * 1000:F0} mm、最小边 {ZoneMin * 1000:F0} mm；有实体碰撞的直接用实体表面）；静态批处理对象 {statics} 个（托盘、螺钉、盖板等会动的对象不设静态）");
        }

        // ------------------------------------------------------------------ 4. 测试场景
        static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // 环境光：三色渐变、压暗、降低饱和（上一版平光绿灰把整幅画面吞成均匀暗绿）
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.19f, 0.21f, 0.20f);
            RenderSettings.ambientEquatorColor = new Color(0.14f, 0.15f, 0.14f);
            RenderSettings.ambientGroundColor = new Color(0.07f, 0.07f, 0.065f);
            RenderSettings.fog = false;

            var area = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
            area.transform.position = Vector3.zero;

            // 暖色工作灯：放在灯泡处，照向操作垫中心 —— 画面的主光和焦点
            var bulb = Find(area.transform, "Lamp_Bulb").GetComponent<Renderer>().bounds.center;
            var lamp = new GameObject("WorkLamp_Spot (warm key)").AddComponent<Light>();
            lamp.type = LightType.Spot; lamp.color = new Color(1.0f, 0.74f, 0.46f); lamp.intensity = LampIntensity;
            lamp.range = 1.7f; lamp.spotAngle = 82f; lamp.innerSpotAngle = 34f; lamp.shadows = LightShadows.Soft; lamp.shadowNormalBias = 0.2f;
            lamp.transform.SetPositionAndRotation(bulb, Quaternion.LookRotation(LampTarget - bulb));
            lamp.transform.SetParent(Find(area.transform, "Lamp_Head"), true);
            // 暖色反弹：模拟灯光打在垫子 / 台面上反上来的暖光，只照操作区附近（无阴影）
            var bounce = new GameObject("WorkArea_Bounce (warm, no shadow)").AddComponent<Light>();
            bounce.type = LightType.Point; bounce.color = new Color(1.0f, 0.70f, 0.42f); bounce.intensity = BounceIntensity; bounce.range = 0.95f;
            bounce.shadows = LightShadows.None;
            bounce.transform.position = B2U(-0.02f, 0.36f, 1.10f);

            // 冷色顶灯（日光管）与正面补光：只负责让环境不全黑
            var ceil = new GameObject("Ceiling_Fluo (cool)").AddComponent<Light>();
            ceil.type = LightType.Point; ceil.color = new Color(0.80f, 0.90f, 0.86f); ceil.intensity = CeilingIntensity; ceil.range = 3.4f; ceil.shadows = LightShadows.None;
            ceil.transform.position = B2U(0f, 0.25f, 2.30f);
            var fill = new GameObject("Room_Fill (cool, no shadow)").AddComponent<Light>();
            fill.type = LightType.Directional; fill.color = new Color(0.72f, 0.80f, 0.78f); fill.intensity = FillIntensity; fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.LookRotation(B2U(0f, 0.6f, 1.0f) - B2U(0.2f, -1.4f, 1.9f));

            // 后处理：中性色调映射（暖光焦点再亮也不截成白）+ 轻对比 + 轻暗角把视线收向操作区。不加 Bloom / 色差 / 故障类全屏效果。
            var volGo = new GameObject("PostProcess (tonemap + vignette)");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = BuildPostProfile();

            // 镜头：一台摄像机，两个固定位姿
            var rigGo = new GameObject("CameraRig");
            Transform Pose(string n, Vector3 pos, Vector3 target)
            {
                var t = new GameObject(n).transform;
                t.SetParent(rigGo.transform, false);
                t.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
                return t;
            }
            var gamePose = Pose("CamPose_Game", GameCamPos, GameCamTarget);
            var closePose = Pose("CamPose_CloseUp", CloseCamPos, CloseCamTarget);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(rigGo.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 20f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.05f, 0.06f, 0.055f);
            cam.fieldOfView = GameFov;
            camGo.transform.SetPositionAndRotation(gamePose.position, gamePose.rotation);
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var rig = rigGo.AddComponent<WbCameraRig>();
            rig.Configure(cam, gamePose, closePose, GameFov, CloseFov);
            EditorUtility.SetDirty(rig);

            // 占位交互（仅展示与路径验证）
            var demoGo = new GameObject("PLACEHOLDER_Demo (占位交互，非正式维修流程)");
            var demo = demoGo.AddComponent<WbPlaceholderDemo>();
            var tier1 = Find(area.transform, "Toolbox_Tier1").GetComponentsInChildren<WbInspectable>(true).First(w => w.target.name == "Toolbox_Tier1");
            demo.Configure(Find(area.transform, "Tray_Screws"), Find(area.transform, "Tray_OldParts"),
                           Find(area.transform, "Placeholder_Prosthetic_Cover"),
                           Enumerable.Range(1, 4).Select(i => Find(area.transform, $"Placeholder_Prosthetic_Screw_{i}")).ToArray(),
                           ScrewTraySlot, CoverPark, Find(area.transform, "Toolbox_Tier2"), tier1);
            EditorUtility.SetDirty(demo);
            // 鼠标操作记录（真人鼠标检查用；只记录，不改行为）
            var logGo = new GameObject("MouseSessionLog");
            logGo.AddComponent<WbInteractionLog>().Configure(rig);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Note($"测试场景 {ScenePath}：游戏镜头 {GameCamPos:F2} 竖直视场 {GameFov}°，近距维修镜头 {CloseCamPos:F2} 竖直视场 {CloseFov}°；" +
                 $"暖色聚光工作灯 {LampIntensity}（软阴影）+ 暖色反弹 {BounceIntensity} + 冷色顶灯 {CeilingIntensity} + 补光 {FillIntensity} + 三色渐变暗环境光；" +
                 "后处理：中性色调映射、对比 +12、暗角 0.22");
        }

        // 灯光强度（URP 实时光，单位同 Light.intensity）
        public const float LampIntensity = 2.3f, BounceIntensity = 0.40f, CeilingIntensity = 0.85f, FillIntensity = 0.12f;

        static VolumeProfile BuildPostProfile()
        {
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfile) != null) AssetDatabase.DeleteAsset(PostProfile);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, PostProfile);
            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            Add<Tonemapping>().mode.Override(TonemappingMode.Neutral);
            var ca = Add<ColorAdjustments>();
            ca.postExposure.Override(0.25f);
            ca.contrast.Override(12f);
            ca.saturation.Override(4f);
            var vg = Add<Vignette>();
            vg.intensity.Override(0.22f);
            vg.smoothness.Override(0.45f);
            vg.color.Override(new Color(0.02f, 0.02f, 0.015f));
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ------------------------------------------------------------------ 报告
        static void WriteReport()
        {
            Directory.CreateDirectory(ReportDir);
            var sb = new StringBuilder();
            sb.AppendLine("工作台区域 · Unity 构建记录");
            sb.AppendLine($"Unity {Application.unityVersion}，{DateTime.Now:yyyy-MM-dd HH:mm}");
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
