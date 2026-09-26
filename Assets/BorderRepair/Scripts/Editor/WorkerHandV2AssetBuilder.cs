using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Inspection;
using BorderRepair.Narrative;
using BorderRepair.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 工人义手 v2：接入 Blender 导出的正式模型（ArtSource/WorkerHand/build_workerhand.py --export），
    /// 生成独立的 v2 prefab 和四套“工具 + 手套手”，并接入叙事场景（工具动画、检测仪读数）。
    /// 旧占位 prefab 保留不动，可以用菜单切回。检查点 pointId、步骤、案件文字都不变；案件只改 itemPrefab 引用。
    /// 命令行：-executeMethod BorderRepair.EditorTools.WorkerHandV2AssetBuilder.BuildFromCommandLine
    /// </summary>
    public static class WorkerHandV2AssetBuilder
    {
        const string ArtDir = "Assets/BorderRepair/Art/WorkerHand";
        const string ModelDir = ArtDir + "/Models";
        const string TextureDir = ArtDir + "/Textures";
        const string MaterialDir = ArtDir + "/Materials";
        const string ManifestPath = ArtDir + "/WorkerHand_Materials.json";
        const string AnchorsPath = ArtDir + "/WorkerHand_Anchors.json";
        public const string PrefabPath = "Assets/BorderRepair/Prefabs/Items/Narrative/Item_WorkerProsthetic_v2.prefab";
        public const string RigDir = "Assets/BorderRepair/Prefabs/Tools";

        static string Fbx(string group) => $"{ModelDir}/WorkerHand_{group}.fbx";

        public static readonly string[] Groups =
        {
            "Body", "Shell", "SealIntact", "SealTorn", "FastenerA", "FastenerB", "DriveStatic", "DriveWorn", "DriveNew", "DriveTestClip",
            "Limiter", "LimiterLedRed", "Board", "BoardLedGreen", "DataPort", "Tray",
            "Tool_Screwdriver", "Tool_Pry", "Tool_TesterBody", "Tool_Probe", "Tool_Plug", "Glove", "GlovePinch",
        };
        static readonly HashSet<string> ReadableGroups = new HashSet<string> { "Tool_Screwdriver", "Tool_Pry", "Tool_Probe", "Tool_Plug", "Glove", "GlovePinch" };

        // Blender 坐标（米）：与 build_workerhand.py 的常量一致
        const float CoverFront = -0.0345f, FrontY = -0.030f, BayBack = -0.008f;
        const float BayX0 = -0.138f, BayX1 = 0.018f, BayZ = 0.022f;

        /// <summary>
        /// Blender（Z 向上，正面 -Y）→ 本物品 prefab 坐标（Y 向上，正面 -Z）。
        /// FBX 导出（-Z forward, Y up）+ Unity 导入 + ItemAssetPipeline.ModelRotation（绕 Y 转 180°）合起来就是 (x, z, y)。构建时会用封条的实际网格位置核对。
        /// </summary>
        public static Vector3 B2U(Vector3 b) => new Vector3(b.x, b.z, b.y);
        static Vector3 B2U(float[] a) => B2U(new Vector3(a[0], a[1], a[2]));

        [Serializable] class AnchorEntry { public string id; public string kind; public float[] pos; public float[] @out; public float[] arm; public float[] lever; }
        [Serializable] class SnapEntry { public string name; public float[] pos; }
        [Serializable] class ToolEntry { public string kind; public string group; public string glove; public float grip; }
        [Serializable] class AnchorFile { public AnchorEntry[] anchors; public SnapEntry[] snaps; public ToolEntry[] tools; }

        [MenuItem("Border Repair/Narrative/Build Worker Hand v2 (from Blender export)", priority = 65)]
        static void MenuBuild()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorSceneManager.OpenScene(NarrativeSliceBuilder.ScenePath);
        }

        [MenuItem("Border Repair/Narrative/Use Placeholder Worker Hand (rollback)", priority = 66)]
        static void MenuRollback()
        {
            var placeholder = AssetDatabase.LoadAssetAtPath<GameObject>(NarrativeSliceBuilder.PrefabPath);
            ItemAssetPipeline.AssignCasePrefab(NarrativeSliceBuilder.CasePath, placeholder);
            SetSkinEnabled(true);
            AssetDatabase.SaveAssets();
            Debug.Log("[BorderRepair] 叙事案件已切回占位义手 prefab（v2 prefab 保留）");
        }

        public static void BuildFromCommandLine() => Build();

        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play 模式。");
            AssetDatabase.Refresh();
            var manifest = ItemAssetPipeline.LoadManifest(ManifestPath);
            if (!File.Exists(AnchorsPath)) throw new FileNotFoundException("找不到锚点文件，请用 --export 运行 Blender 脚本", AnchorsPath);
            var anchors = JsonUtility.FromJson<AnchorFile>(File.ReadAllText(AnchorsPath));

            ItemAssetPipeline.ConfigureTextures(TextureDir, "Grime");
            var materials = ItemAssetPipeline.BuildMaterials(manifest, MaterialDir, TextureDir);
            foreach (var g in Groups)
            {
                ItemAssetPipeline.ConfigureModel(Fbx(g), materials);
                if (ReadableGroups.Contains(g))                      // 工具网格可读：测试里检查穿模
                {
                    var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx(g));
                    mi.isReadable = true;
                    mi.SaveAndReimport();
                }
            }

            var prefab = BuildItemPrefab(anchors);
            var rigs = BuildRigs(anchors);
            ItemAssetPipeline.AssignCasePrefab(NarrativeSliceBuilder.CasePath, prefab);
            AssetDatabase.SaveAssets();
            IntegrateScene(rigs);
            LogStats(materials);
        }

        // ---------- 物品 prefab ----------

        static GameObject Instantiate(string group, Transform parent)
        {
            var go = ItemAssetPipeline.InstantiateModel(Fbx(group), parent, "Model_" + group);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        static Transform Point(Transform parent, Transform root, string id, Vector3 pivotBlender, Quaternion rotation)
        {
            var t = new GameObject("Point_" + id).transform;
            t.SetParent(parent, false);
            t.position = root.TransformPoint(B2U(pivotBlender));
            t.rotation = root.rotation * rotation;
            t.gameObject.AddComponent<InspectionPoint>().Configure(id, null, null);
            return t;
        }

        /// <summary>模型实例先放在物品根下（与 Blender 世界坐标一致），再移到部位下面并保持世界位置。</summary>
        static GameObject Visual(string group, Transform root, Transform point, string name = null)
        {
            var go = Instantiate(group, root);
            go.transform.SetParent(point, true);
            if (name != null) go.name = name;
            return go;
        }

        static GameObject BuildItemPrefab(AnchorFile data)
        {
            var root = new GameObject("Item_WorkerProsthetic_v2");
            try
            {
                var rt = root.transform;
                Instantiate("Body", rt);
                Instantiate("Tray", rt);
                Instantiate("DriveStatic", rt);
                BuildBodyColliders(rt);

                var outward = Quaternion.FromToRotation(Vector3.up, Vector3.back);   // 螺丝：本地 +Y 朝外（镜头一侧）

                // 盖板（先拟合碰撞体，再挂封条子部位，避免盖板碰撞体把封条也包进去）
                var shell = Point(rt, rt, "shell", new Vector3(-0.06f, FrontY - 0.0005f - 0.002f, 0f), Quaternion.identity);
                Visual("Shell", rt, shell);
                ItemAssetPipeline.FitBoxCollider(shell.gameObject, 0.0003f);
                var seal = Point(shell, rt, "lease_seal", new Vector3(-0.12f, CoverFront - 0.0012f, 0.0205f), Quaternion.identity);
                var intact = Visual("SealIntact", rt, seal, "Intact");
                // 只按完整封条拟合：撕开后翘起的右半边会往下盖到螺丝 A 的下半边，不能算进碰撞体
                ItemAssetPipeline.FitBoxCollider(seal.gameObject, 0.0003f);
                var torn = Visual("SealTorn", rt, seal, "Torn");
                torn.SetActive(false);
                seal.gameObject.AddComponent<RepairPart>().Configure("lease_seal", null, null, null, intact, torn, null);

                var fa = Point(rt, rt, "fastener_a", new Vector3(-0.12f, CoverFront, 0.016f), outward);
                Visual("FastenerA", rt, fa);
                ItemAssetPipeline.FitBoxCollider(fa.gameObject, 0.0002f);
                var fb = Point(rt, rt, "fastener_b", new Vector3(0.004f, CoverFront, -0.016f), outward);
                Visual("FastenerB", rt, fb);
                ItemAssetPipeline.FitBoxCollider(fb.gameObject, 0.0002f);

                var drive = Point(rt, rt, "drive", new Vector3(-0.052f, BayBack - 0.0075f, 0.007f), Quaternion.identity);
                var worn = Visual("DriveWorn", rt, drive, "WornGears");
                var fresh = Visual("DriveNew", rt, drive, "NewGears");
                var clip = Visual("DriveTestClip", rt, drive, "TestClip");
                ItemAssetPipeline.FitBoxCollider(drive.gameObject, 0.0005f);
                fresh.SetActive(false);
                clip.SetActive(false);
                drive.gameObject.AddComponent<RepairPart>().Configure("drive", null, worn, fresh, null, null, clip);

                var limiter = Point(rt, rt, "force_limiter", new Vector3(0.002f, BayBack - 0.0045f, -0.001f), Quaternion.identity);
                Visual("Limiter", rt, limiter);
                var red = Visual("LimiterLedRed", rt, limiter, "BypassIndicator");
                ItemAssetPipeline.FitBoxCollider(limiter.gameObject, 0.0005f);
                red.SetActive(false);
                limiter.gameObject.AddComponent<RepairPart>().Configure("force_limiter", null, null, null, null, null, red);

                var board = Point(rt, rt, "control_board", new Vector3(-0.108f, BayBack - 0.0022f, -0.0055f), Quaternion.identity);
                Visual("Board", rt, board);
                var green = Visual("BoardLedGreen", rt, board, "SignatureOk");
                ItemAssetPipeline.FitBoxCollider(board.gameObject, 0.0005f);
                green.SetActive(false);
                board.gameObject.AddComponent<RepairPart>().Configure("control_board", null, null, null, null, null, green);

                var port = Point(rt, rt, "data_port", new Vector3(0.068f, 0f, 0.0325f), Quaternion.identity);
                Visual("DataPort", rt, port);
                ItemAssetPipeline.FitBoxCollider(port.gameObject, 0.0005f);
                port.gameObject.AddComponent<RepairPart>().Configure("data_port", null, null, null, null, null, null);

                // 零件盘吸附位置：盖板正面朝上平躺；螺丝平躺（轴线沿 +X）
                var snaps = new GameObject("SnapTargets").transform;
                snaps.SetParent(rt, false);
                Transform Snap(string name, Quaternion rot)
                {
                    var e = data.snaps.First(s => s.name == name);
                    var t = new GameObject(name).transform;
                    t.SetParent(snaps, false);
                    t.localPosition = B2U(e.pos);
                    t.localRotation = rot;
                    return t;
                }
                var snapShell = Snap("Snap_ShellOnTray", Quaternion.FromToRotation(Vector3.back, Vector3.up));
                var snapA = Snap("Snap_FastenerA_OnTray", Quaternion.FromToRotation(Vector3.up, Vector3.right));
                var snapB = Snap("Snap_FastenerB_OnTray", Quaternion.FromToRotation(Vector3.up, Vector3.right));
                shell.gameObject.AddComponent<RepairPart>().Configure("shell", snapShell, null, null, null, null, null);
                fa.gameObject.AddComponent<RepairPart>().Configure("fastener_a", snapA, null, null, null, null, null);
                fb.gameObject.AddComponent<RepairPart>().Configure("fastener_b", snapB, null, null, null, null, null);

                // 工具锚点
                var points = rt.GetComponentsInChildren<InspectionPoint>(true).ToDictionary(p => p.PointId, p => p.transform);
                foreach (var a in data.anchors)
                {
                    var kind = (ToolKind)Enum.Parse(typeof(ToolKind), a.kind);
                    var t = new GameObject("ToolAnchor_" + a.kind).transform;
                    t.SetParent(points[a.id], false);
                    t.position = rt.TransformPoint(B2U(a.pos));
                    var up = B2U(a.@out).normalized;
                    var arm = B2U(a.arm);
                    arm = (arm - up * Vector3.Dot(arm, up)).normalized;
                    t.rotation = rt.rotation * Quaternion.LookRotation(arm, up);
                    var lever = B2U(a.lever);
                    t.gameObject.AddComponent<ToolAnchor>().Configure(kind, lever.sqrMagnitude > 0 ? t.InverseTransformDirection(lever.normalized) : Vector3.zero);
                }

                VerifyAxisMapping(seal);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
                if (!ok) throw new IOException("prefab 保存失败：" + PrefabPath);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>用封条网格的实际位置核对 Blender → Unity 的坐标换算（差 1 mm 以上就报错）。</summary>
        static void VerifyAxisMapping(Transform seal)
        {
            var mf = seal.GetComponentsInChildren<MeshFilter>(true).First(m => m.GetComponentInParent<Renderer>(true) != null);
            var b = mf.sharedMesh.bounds;
            var center = seal.root.InverseTransformPoint(mf.transform.TransformPoint(b.center));
            var expected = B2U(new Vector3(-0.12f, CoverFront - 0.0012f, 0.0205f));
            if (Vector3.Distance(center, expected) > 0.001f)
                throw new InvalidDataException($"坐标换算不对：封条网格中心 {center.ToString("F4")}，按 Blender 坐标换算应为 {expected.ToString("F4")}");
        }

        /// <summary>
        /// 机身的简化碰撞体（盒子），维修舱开口处留空：开盖后射线能打到舱内零件，盖板在时先打到盖板。
        /// </summary>
        static void BuildBodyColliders(Transform root)
        {
            var parent = new GameObject("BodyColliders").transform;
            parent.SetParent(root, false);
            var boxes = new (string name, Vector3 min, Vector3 max)[]
            {
                ("ForearmBack", new Vector3(-0.165f, BayBack, -0.034f), new Vector3(0.042f, 0.03f, 0.034f)),
                ("ForearmFrontLeft", new Vector3(-0.165f, FrontY, -0.034f), new Vector3(BayX0, BayBack, 0.034f)),
                ("ForearmFrontRight", new Vector3(BayX1, FrontY, -0.034f), new Vector3(0.042f, BayBack, 0.034f)),
                ("ForearmFrontTop", new Vector3(BayX0, FrontY, BayZ), new Vector3(BayX1, BayBack, 0.035f)),
                ("ForearmFrontBottom", new Vector3(BayX0, FrontY, -0.035f), new Vector3(BayX1, BayBack, -BayZ)),
                ("SocketCuff", new Vector3(-0.213f, -0.044f, -0.044f), new Vector3(-0.16f, 0.044f, 0.044f)),
                ("Wrist", new Vector3(0.04f, -0.026f, -0.03f), new Vector3(0.082f, 0.026f, 0.03f)),
                ("PalmFingers", new Vector3(0.078f, -0.02f, -0.037f), new Vector3(0.2f, 0.04f, 0.037f)),
                ("Thumb", new Vector3(0.095f, -0.005f, 0.03f), new Vector3(0.14f, 0.03f, 0.075f)),
                ("Tray", new Vector3(-0.175f, -0.082f, -0.081f), new Vector3(0.055f, 0.018f, -0.068f)),
            };
            foreach (var (name, min, max) in boxes)
            {
                var go = new GameObject("BodyCollider_" + name);
                go.transform.SetParent(parent, false);
                var box = go.AddComponent<BoxCollider>();
                var a = B2U(min);
                var b = B2U(max);
                box.center = (a + b) / 2f;
                box.size = new Vector3(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y), Mathf.Abs(b.z - a.z));
            }
        }

        // ---------- 工具 + 手套手 ----------

        static Dictionary<ToolKind, ToolRig> BuildRigs(AnchorFile data)
        {
            EnsureFolder(RigDir);
            var result = new Dictionary<ToolKind, ToolRig>();
            foreach (var t in data.tools)
            {
                var kind = (ToolKind)Enum.Parse(typeof(ToolKind), t.kind);
                var root = new GameObject("ToolRig_" + t.kind);
                try
                {
                    var tool = ItemAssetPipeline.InstantiateModel(Fbx(t.group), root.transform, "Tool");
                    var glove = ItemAssetPipeline.InstantiateModel(Fbx(t.glove), root.transform, "Glove");
                    glove.transform.localPosition = new Vector3(0f, t.grip, 0f);
                    // 工具和手套不投影：避免阴影盖住封条、指示灯等线索
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
                    root.AddComponent<ToolRig>().Configure(kind, tool.transform, glove.transform);
                    string path = $"{RigDir}/ToolRig_{t.kind}.prefab";
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
                    if (!ok) throw new IOException("prefab 保存失败：" + path);
                    result[kind] = prefab.GetComponent<ToolRig>();
                }
                finally { Object.DestroyImmediate(root); }
            }
            return result;
        }

        // ---------- 场景 ----------

        static void IntegrateScene(Dictionary<ToolKind, ToolRig> rigs)
        {
            var scene = EditorSceneManager.OpenScene(NarrativeSliceBuilder.ScenePath, OpenSceneMode.Single);
            var controller = Object.FindFirstObjectByType<RepairStationController>();
            var fx = Object.FindFirstObjectByType<RepairFeedbackFx>();
            var polish = GameObject.Find(WorkerHandPolishBuilder.RootName);
            if (controller == null || fx == null || polish == null)
                throw new InvalidOperationException("叙事场景缺少控制器或打磨组件，请先运行 Apply Worker Hand Polish");

            // 桌上的检测仪主机：探针 / 插头接触时切换屏幕读数
            var oldTester = polish.transform.Find("BenchTester");
            if (oldTester != null) Object.DestroyImmediate(oldTester.gameObject);
            var anchor = GameObject.Find("ItemAnchor").transform;
            var testerRoot = new GameObject("BenchTester").transform;
            testerRoot.SetParent(polish.transform, false);
            testerRoot.position = new Vector3(anchor.position.x + 0.3f, 0.9f, anchor.position.z + 0.12f);
            testerRoot.rotation = Quaternion.Euler(0f, -18f, 0f);
            var testerModel = Instantiate("Tool_TesterBody", testerRoot);
            var screen = testerModel.GetComponentsInChildren<Renderer>(true).First(r => r.sharedMaterial != null && r.sharedMaterial.name == "M_Hand_Decal");
            var readout = testerRoot.gameObject.AddComponent<TesterReadout>();
            readout.Configure(screen);
            var presets = TesterScreenBuilder.LoadExisting();                // 屏幕 shader 预设（已生成时保留）
            if (presets != null) TesterScreenBuilder.AssignPresets(readout, presets);

            var oldAnim = polish.transform.Find("ToolAnimation");
            if (oldAnim != null) Object.DestroyImmediate(oldAnim.gameObject);
            var animGo = new GameObject("ToolAnimation");
            animGo.transform.SetParent(polish.transform, false);
            var animator = animGo.AddComponent<RepairToolAnimator>();
            animator.Configure(controller, fx, rigs.Values.ToList(), readout);

            var so = new SerializedObject(controller);
            so.FindProperty("toolAnimator").objectReferenceValue = animator;
            so.ApplyModifiedPropertiesWithoutUndo();
            SetSkinEnabled(false, scene);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("场景保存失败：" + NarrativeSliceBuilder.ScenePath);
        }

        /// <summary>v2 自带地下诊所 lookdev 材质；场景级的占位材质替换只对占位 prefab 有意义。</summary>
        static void SetSkinEnabled(bool enabled, UnityEngine.SceneManagement.Scene? openScene = null)
        {
            var scene = openScene ?? EditorSceneManager.OpenScene(NarrativeSliceBuilder.ScenePath, OpenSceneMode.Single);
            var skin = Object.FindFirstObjectByType<ItemMaterialSkin>(FindObjectsInactive.Include);
            if (skin != null) skin.enabled = enabled;
            if (openScene == null)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        static void LogStats(Dictionary<string, Material> materials)
        {
            var parts = new List<string>();
            long hand = 0;
            foreach (var g in Groups)
            {
                long t = ItemAssetPipeline.CountTriangles(Fbx(g));
                parts.Add($"{g}={t}");
                if (!g.StartsWith("Tool_") && !g.StartsWith("Glove")) hand += t;
            }
            Debug.Log($"[BorderRepair] 工人义手 v2 Unity 导入统计：三角面 {string.Join(", ", parts)}；义手（含所有变体）合计={hand}；" +
                      $"材质 {materials.Count} 个；贴图 {ItemAssetPipeline.DescribeTextures(TextureDir)}");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
