using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Dock;
using BorderRepair.FirstOrder;
using BorderRepair.TwoNight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static BorderRepair.TwoNight.MeshClearance;

namespace BorderRepair.Art.Unit07TrayVariant.EditorTools
{
    /// <summary>
    /// 零件盘提手抬高变体：导入 → 预制体 → 握持数据 → 隔离验证场景（发布场景的副本）→ 路线重算与静态检查报告。
    /// 只写 Assets/BorderRepair/Art/Unit07TrayVariant/ 与 ArtSource/Unit07TrayVariant/。原托盘、维修座预制体、托盘架、RobotV4、共享材质、发布场景都只读。
    ///
    /// 检查规则（区分握持接触与穿插，不整组排除）：
    ///   允许接触：右手爪齿（*_Teeth）↔ 握杆直段（提手网格里离握杆轴线 ≤ 半径 + 1.5 mm、且在两个立柱圆角之间的三角面）。
    ///   必须 ≥ 阈值：爪齿 ↔ 盘体 / 立柱 / 安装座；右手其余零件（上下爪爪身、爪架、销轴、腕）↔ 托盘全部几何（含握杆）；七号其余部分 ↔ 托盘。
    /// 测距：MeshClearance（双向点到三角形 + 双面 Möller–Trumbore 边穿三角形）。**不算边到边最近距离**：两条边擦身而过时，结果偏大，
    ///   最多约一个三角形边长；**共面重叠**（两个面贴在同一平面上、没有边穿过对方）判不出穿插。所以关键近距离处另有剖视图和多角度图。
    /// </summary>
    public static class TrayVariantBuild
    {
        public const string Root = "Assets/BorderRepair/Art/Unit07TrayVariant";
        public const string ModelPath = Root + "/Models/UNIT07_PartsTray_HandleRaised.fbx";
        public const string PrefabPath = Root + "/Prefabs/Dock_PartsTray_HandleRaised.prefab";
        public const string GripDataPath = Root + "/Data/Unit07TrayGripData_HandleRaised.asset";
        public const string VerifyScene = Root + "/Verification/TrayVariant_Verify.unity";
        public const string SourceScene = "Assets/BorderRepair/Scenes/Slice/Unit07_Night.unity";
        const string SourceFbx = "ArtSource/Unit07TrayVariant/Export/UNIT07_PartsTray_HandleRaised.fbx";
        const string DockPrefab = "Assets/BorderRepair/Prefabs/Unit07Dock/Unit07ServiceDock.prefab";
        const string DockIvory = "Assets/BorderRepair/Art/Unit07ServiceDock/Materials/M_Dock_Ivory.mat";
        const string RobotFbx = "Assets/RobotV4/Model/robot-final.fbx";
        public const string Reports = "ArtSource/Unit07TrayVariant/Reports";
        public const string VariantName = "Dock_PartsTray_HandleRaised";
        public const string HoldName = "TwoNight_TrayHoldPoint（右手端盘挂点）";
        public static readonly Vector3 GripCenterWrist = new Vector3(-0.0824f, -0.0007f, 0.0284f);   // 审计：右手咬合中心（腕骨本地）
        static readonly float[] CarryJoints = { 3.16558f, 36.33465f, -58.62568f, 16.34933f };
        const float LeanDeg = 4f, Clear = 0.010f, Guide = 0.002f, Grip = 0.002f, BarContactSlack = 0.0015f;

        [MenuItem("Border Repair/Unit07 Tray Variant/Build All (import, prefab, verification scene, report)")]
        public static void BuildAll()
        {
            Import();
            BuildPrefab();
            BuildVerification();
            AssetDatabase.SaveAssets();
            Debug.Log("[TrayVariant] 完成");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ 导入（导入设置照原维修座 FBX：比例 1、不烘焙轴转换、不导入动画 / 相机 / 灯光、网格不可读）

        public static void Import()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ModelPath));
            File.Copy(SourceFbx, ModelPath, true);
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = false;
            mi.importAnimation = false; mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = true;
            mi.isReadable = false; mi.meshCompression = ModelImporterMeshCompression.Off; mi.addCollider = false; mi.weldVertices = true;
            mi.animationType = ModelImporterAnimationType.None;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Dock_Ivory"), AssetDatabase.LoadAssetAtPath<Material>(DockIvory));
            mi.SaveAndReimport();
        }

        // ------------------------------------------------------------------ 预制体

        public static void BuildPrefab()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            // FBX 里只有一个顶层空物体时，Unity 把它并进模型根（根名 = 文件名）；有同名子物体时用子物体
            var node = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == VariantName) ?? inst.transform;
            node.SetParent(null, true);
            if (node.gameObject != inst) UnityEngine.Object.DestroyImmediate(inst);
            var root = node.gameObject;
            root.name = VariantName;

            // 原托盘的组件：从维修座预制体只读取值（LoadPrefabContents 后不保存）
            var dock = PrefabUtility.LoadPrefabContents(DockPrefab);
            try
            {
                var orig = dock.GetComponentsInChildren<Transform>(true).First(t => t.name == "Dock_PartsTray").gameObject;
                foreach (var c in new Type[] { typeof(DockPartProperties), typeof(DockInteractable), typeof(DockPickable) })
                {
                    var src = orig.GetComponent(c); if (src == null) continue;
                    // 只复制字段值（组件之间直接 CopySerialized 会连带内部的 GameObject 指向，弄坏内存里的维修座预制体）
                    var old = root.GetComponent(c); if (old != null) UnityEngine.Object.DestroyImmediate(old);
                    UnityEditorInternal.ComponentUtility.CopyComponent(src);
                    UnityEditorInternal.ComponentUtility.PasteComponentAsNew(root);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(dock); }
            var pick = root.GetComponent<DockPickable>();
            if (pick != null) { var so = new SerializedObject(pick); so.FindProperty("holdPoint").objectReferenceValue = null; so.ApplyModifiedPropertiesWithoutUndo(); }
            // 碰撞盒：包住盘体 + 两个提手（根本地坐标）
            var bc = root.GetComponent<BoxCollider>(); if (bc == null) bc = root.AddComponent<BoxCollider>();
            var b = LocalBounds(root.transform, root.GetComponentsInChildren<MeshFilter>(true));
            bc.center = b.center; bc.size = b.size;
            // 右手握点标记（托盘本地）：握杆中心沿握杆偏 +30.2 mm，与审计握法的握点位置相同（见 param_search.md）
            var barPX = root.transform.Find("GripBar_PX"); var barNX = root.transform.Find("GripBar_NX");
            var bar = barPX.localPosition.x > 0 ? barPX : barNX;     // 七号右手握托盘本地 +X 端（72838e9 实测）
            var gp = root.transform.Find("GripPoint_R"); if (gp == null) gp = new GameObject("GripPoint_R").transform;
            gp.SetParent(root.transform, false);
            gp.localPosition = new Vector3(bar.localPosition.x, 0.0302f, bar.localPosition.z); gp.localRotation = Quaternion.identity;
            // 核对 Blender → Unity 的 Y 方向：握杆封端必须在托盘本地 +Y（夹爪铰链一侧），否则用 TRAY_Y_SIGN=-1 重新生成
            var end = root.transform.Find(bar == barPX ? "GripBarEnd_PX" : "GripBarEnd_NX").localPosition;
            Debug.Log($"[TrayVariant] AxisMarker_PlusY 本地 {root.transform.Find("AxisMarker_PlusY").localPosition * 1000:F1} mm；握杆封端 {end * 1000:F1} mm");
            if (end.y < 0.02f) throw new Exception("握杆封端不在托盘本地 +Y：请用环境变量 TRAY_Y_SIGN=-1 重新运行 Blender 脚本");
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        static Bounds LocalBounds(Transform root, IEnumerable<MeshFilter> mfs)
        {
            bool any = false; var b = new Bounds();
            foreach (var mf in mfs)
            {
                if (mf.sharedMesh == null) continue;
                var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices) { var p = m.MultiplyPoint3x4(v); if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p); }
            }
            return b;
        }

        // ------------------------------------------------------------------ 托盘分区（握杆直段 / 立柱与安装座 / 盘体）

        public class TrayParts { public WMesh body; public List<WMesh> bars = new List<WMesh>(), legs = new List<WMesh>(); public List<WMesh> All => new[] { body }.Concat(bars).Concat(legs).ToList(); }

        /// <summary>把变体托盘拆成三类（世界坐标，按 tray 当前位姿；extra = 额外的世界变换）。握杆直段 = 离握杆轴线 ≤ 半径 + 1.5 mm、在两立柱圆角之间。</summary>
        public static TrayParts Split(Transform tray, Matrix4x4 extra)
        {
            var parts = new TrayParts();
            foreach (var mf in tray.GetComponentsInChildren<MeshFilter>(true))
            {
                var w = World(mf.GetComponent<Renderer>()); if (w == null) continue;
                w = Moved(w, extra);
                if (mf.name == "Tray_Body") { parts.body = w; continue; }
                string sfx = mf.name.EndsWith("PX") ? "PX" : "NX";
                var p0 = extra.MultiplyPoint3x4(tray.Find("GripBarStart_" + sfx).position); var p1 = extra.MultiplyPoint3x4(tray.Find("GripBarEnd_" + sfx).position);
                var c = (p0 + p1) / 2f; var axis = (p1 - p0).normalized;
                float half = (p1 - p0).magnitude / 2f + 0.0005f;     // 握杆直段：远端立柱圆角之外 → 封端
                var barTri = new List<int>(); var legTri = new List<int>();
                for (int i = 0; i < w.tri.Length; i += 3)
                {
                    var cen = (w.v[w.tri[i]] + w.v[w.tri[i + 1]] + w.v[w.tri[i + 2]]) / 3f;
                    float along = Vector3.Dot(cen - c, axis);
                    float radial = (cen - c - axis * along).magnitude;
                    bool isBar = Mathf.Abs(along) <= half && radial <= 0.005f + BarContactSlack;
                    (isBar ? barTri : legTri).AddRange(new[] { w.tri[i], w.tri[i + 1], w.tri[i + 2] });
                }
                parts.bars.Add(Sub(w, barTri, mf.name + "·握杆直段"));
                parts.legs.Add(Sub(w, legTri, mf.name + "·立柱 / 安装座"));
            }
            return parts;
        }

        static WMesh Sub(WMesh w, List<int> tri, string name)
        {
            var b = new Bounds(w.v[tri[0]], Vector3.zero); foreach (var k in tri) b.Encapsulate(w.v[k]);
            return new WMesh { name = name, v = w.v, tri = tri.ToArray(), b = b };
        }

        // ------------------------------------------------------------------ 隔离验证场景、握持数据、路线

        public static void BuildVerification()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(VerifyScene));
            AssetDatabase.DeleteAsset(VerifyScene);
            if (!AssetDatabase.CopyAsset(SourceScene, VerifyScene)) throw new Exception("复制七号场景失败");
            var scene = EditorSceneManager.OpenScene(VerifyScene, OpenSceneMode.Single);
            var inc = UnityEngine.Object.FindFirstObjectByType<TrayIncident>();
            var iso = new SerializedObject(inc);
            var flow = UnityEngine.Object.FindFirstObjectByType<FirstOrderFlow>();
            var robot = inc.RobotRoot; var ov = inc.Overlay;
            var hold = iso.FindProperty("holdAnchor").objectReferenceValue as Transform;
            var body = iso.FindProperty("body").objectReferenceValue as Transform;
            var status = iso.FindProperty("screenStatus").objectReferenceValue as GameObject;
            var origGo = GameObject.Find("Dock_PartsTray");
            Debug.Log($"[TrayVariant] 原托盘 {origGo} 组件 {string.Join(",", origGo.GetComponents<Component>().Select(c => c == null ? "(missing)" : c.GetType().Name))}；incident.tray = {(inc.Tray == null ? "null/destroyed" : inc.Tray.name)}");
            var origTray = origGo.transform;
            var oldHoldPos = hold.localPosition; var oldHoldRot = hold.localRotation;

            // 原托盘在副本里停用（只是这个副本的场景改动；维修座预制体不动），换上变体，放在原托盘的父对象和本地位姿上
            var variant = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            variant.transform.SetParent(origTray.parent, false);
            variant.transform.localPosition = origTray.localPosition; variant.transform.localRotation = origTray.localRotation; variant.transform.localScale = origTray.localScale;
            origTray.gameObject.SetActive(false);
            var vt = variant.transform;
            var pick = variant.GetComponent<DockPickable>();

            // 坐标系一致性：变体盘体隔板（不对称件）和原托盘隔板在各自本地坐标里的位置
            var md = new StringBuilder("# 零件盘提手抬高变体 · Unity 隔离验证（静态 + 路线）\n\n");
            md.AppendLine($"- 生成 {DateTime.Now:yyyy-MM-dd HH:mm}，Unity {Application.unityVersion}，场景 `{VerifyScene}`（发布场景 `{SourceScene}` 的副本；原托盘在副本里停用，换成变体）。");
            md.AppendLine($"- 坐标系核对：原托盘隔板中心（原托盘本地）{DividerCenter(origTray, origTray.GetComponent<MeshFilter>()) * 1000:F1} mm；变体隔板中心（变体根本地）{DividerCenter(vt, vt.Find("Tray_Body").GetComponent<MeshFilter>()) * 1000:F1} mm。两者相同说明变体根的本地坐标就是原托盘的本地坐标。");

            // 手臂端盘姿态（审计关节角不变）
            ov.RightWeight = 1f; ov.LeftWeight = 1f; ov.JawOpen = 0f; ov.UpperJawExtra = 0f;
            for (int i = 0; i < 6; i++) ov.RightBones[i].localRotation = ov.RightTarget(i);
            for (int i = 0; i < 4; i++) ov.LeftBones[i].localRotation = ov.LeftTarget(i);
            var wrist = ov.RightBones[3];

            // ---- 新挂点：托盘轴相对腕骨的朝向不变（握法不变），位置使“变体握点 GripPoint_R”落在右手咬合中心
            var gpLocal = vt.Find("GripPoint_R").localPosition;
            var gcWrist = GripCenterWrist;
            // 腕骨本地：托盘原点 = 咬合中心 − 朝向 × 握点（托盘本地）
            var newHoldPos = gcWrist - oldHoldRot * gpLocal;
            var oldGripInTray = Quaternion.Inverse(oldHoldRot) * (gcWrist - oldHoldPos);
            hold.localPosition = newHoldPos; hold.localRotation = oldHoldRot;
            var pso = new SerializedObject(pick); pso.FindProperty("holdPoint").objectReferenceValue = hold; pso.ApplyModifiedPropertiesWithoutUndo();
            iso.FindProperty("tray").objectReferenceValue = pick; iso.ApplyModifiedPropertiesWithoutUndo();
            md.AppendLine($"- 握法：手臂关节角不变（审计 {string.Join(" / ", CarryJoints.Select(x => x.ToString("F2")))}°，夹爪 0.321）；托盘轴相对腕骨的朝向不变。");
            md.AppendLine($"- 旧握点（原托盘本地）{oldGripInTray * 1000:F1} mm → 新握点 `GripPoint_R`（变体本地）{gpLocal * 1000:F1} mm：握杆上同一位置（离握杆中心 +30.2 mm），高度 +{(gpLocal.z - oldGripInTray.z) * 1000:F1} mm。");
            md.AppendLine($"- 新挂点（腕骨本地）{newHoldPos * 1000:F2} mm，旋转 {oldHoldRot.x:F5}, {oldHoldRot.y:F5}, {oldHoldRot.z:F5}, {oldHoldRot.w:F5}；旧挂点 {oldHoldPos * 1000:F2} mm（不再使用）。\n");

            var report = PlanAndCheck(flow, robot, body, ov, hold, vt, md, out var path);
            iso.Update();
            inc.Configure(robot, body, ov, pick, hold, status, path.hover, path.hoverRot, path.overDock, path.overShelf, path.place, path.release, path.withdraw, path.withdrawUp, LeanDeg);
            EditorUtility.SetDirty(inc);
            AnimationClipSample(robot.gameObject, "Idle_Hover");
            ov.RightWeight = 0f; ov.LeftWeight = 0f; ov.SteadyWeight = 0f;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // ---- 握持数据
            Directory.CreateDirectory(Path.GetDirectoryName(GripDataPath));
            var data = AssetDatabase.LoadAssetAtPath<Unit07TrayGripData>(GripDataPath);
            if (data == null) { data = ScriptableObject.CreateInstance<Unit07TrayGripData>(); AssetDatabase.CreateAsset(data, GripDataPath); }
            var barT = vt.Find(vt.Find("GripBar_PX").localPosition.x > 0 ? "GripBar_PX" : "GripBar_NX");
            data.trayPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); data.trayPrefabPath = PrefabPath;
            data.gripBarCenterLocal = barT.localPosition; data.gripBarAxisLocal = Vector3.up; data.gripPointLocal = gpLocal;
            string sfx = barT.name.EndsWith("PX") ? "PX" : "NX";   // 允许爪齿接触的握杆直段：相对握杆中心沿握杆方向的范围
            data.allowedContactSpan = new Vector2(vt.Find("GripBarStart_" + sfx).localPosition.y - barT.localPosition.y, vt.Find("GripBarEnd_" + sfx).localPosition.y - barT.localPosition.y); data.gripBarRadius = 0.005f;
            data.wristBonePath = PathOf(wrist, robot);
            data.holdLocalPosition = newHoldPos; data.holdLocalRotation = oldHoldRot;
            data.previousHoldLocalPosition = oldHoldPos; data.previousHoldLocalRotation = oldHoldRot;
            data.rightArmJointsDeg = CarryJoints.ToArray(); data.jawT = 0.32071f;
            data.homeParentPath = PathOf(origTray.parent, null); data.homeLocalPosition = origTray.localPosition; data.homeLocalRotation = origTray.localRotation;
            data.routeNotes = path.notes;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(Reports, "grip_data.json"), JsonUtility.ToJson(data, true), new UTF8Encoding(false));
            Directory.CreateDirectory(Reports);
            File.WriteAllText(Path.Combine(Reports, "unity_static_and_route.md"), md.ToString(), new UTF8Encoding(false));
        }

        static string PathOf(Transform t, Transform stopAbove)
        {
            var names = new List<string>();
            for (; t != null && t != stopAbove; t = t.parent) names.Add(t.name);
            if (stopAbove != null) names.Add(stopAbove.name);
            names.Reverse(); return string.Join("/", names);
        }

        static Vector3 DividerCenter(Transform frame, MeshFilter mf)
        {
            var m = frame.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var pts = mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)).Where(p => Mathf.Abs(p.x) < 0.05f && p.z > 0.006f && Mathf.Abs(p.y) < 0.08f).ToList();
            return pts.Count == 0 ? Vector3.zero : pts.Aggregate(Vector3.zero, (a, p) => a + p) / pts.Count;
        }

        static void AnimationClipSample(GameObject robot, string clip)
        {
            var c = AssetDatabase.LoadAllAssetsAtPath(RobotFbx).OfType<AnimationClip>().First(x => x.name == clip);
            c.SampleAnimation(robot, 0f);
        }

        public struct RoutePath { public Vector3 hover, overDock, overShelf, place, release, withdraw, withdrawUp; public Quaternion hoverRot; public string notes; }

        static bool PlanAndCheck(FirstOrderFlow flow, Transform robot, Transform body, TrayCarryOverlay ov, Transform hold, Transform tray, StringBuilder md, out RoutePath path)
        {
            var dock = flow.Dock;
            float hoverHeight = new SerializedObject(dock).FindProperty("hoverHeight").floatValue;
            var homePos = robot.position; var homeRot = robot.rotation;
            var H = dock.RobotAnchor.position + Vector3.up * hoverHeight;
            var oso = new SerializedObject(ov);
            Quaternion[] Arr(string n) { var p = oso.FindProperty(n); return Enumerable.Range(0, p.arraySize).Select(i => p.GetArrayElementAtIndex(i).quaternionValue).ToArray(); }
            var carry = Arr("rightCarry"); var openJaw = Arr("rightOpenJaw"); var stow = Arr("leftStow");
            var rb = ov.RightBones; var lb = ov.LeftBones;
            AnimationClipSample(robot.gameObject, "Idle_Hover");
            var restR = rb.Select(b => b.localRotation).ToArray(); var restL = lb.Select(b => b.localRotation).ToArray();
            robot.SetPositionAndRotation(H, homeRot);
            void PoseRest() { for (int i = 0; i < 6; i++) rb[i].localRotation = restR[i]; for (int i = 0; i < 4; i++) lb[i].localRotation = restL[i]; }
            void PoseCarry(float w = 1f, float upperOpen = 0f)
            {
                for (int i = 0; i < 6; i++) { var t = i == 4 ? Quaternion.Slerp(carry[4], openJaw[0], upperOpen) : carry[i]; rb[i].localRotation = Quaternion.Slerp(restR[i], t, w); }
                for (int i = 0; i < 4; i++) lb[i].localRotation = Quaternion.Slerp(restL[i], stow[i], w);
            }
            PoseCarry();
            var trayHomePos = tray.position; var trayHomeRot = tray.rotation;
            var S = H + (trayHomePos - hold.position);
            float rotErr = Quaternion.Angle(hold.rotation, trayHomeRot);

            bool Visible(Renderer r) => r.enabled && r.gameObject.activeInHierarchy;
            var dockRoot = GameObject.Find("Unit07ServiceDock").transform; var bench = GameObject.Find("WorkbenchArea").transform;
            var trayRs = new HashSet<Renderer>(tray.GetComponentsInChildren<Renderer>(true));
            var env = dockRoot.GetComponentsInChildren<Renderer>(true).Concat(bench.GetComponentsInChildren<Renderer>(true))
                .Where(r => Visible(r) && !trayRs.Contains(r) && r.bounds.max.y < 2.2f).Select(World).Where(m => m != null).ToList();
            var homeParts = Split(tray, Matrix4x4.identity);
            var homeAll = homeParts.All;
            var seatedWith = new HashSet<string>(env.Where(o => homeAll.Any(t => Distance(t, o, 0.002f) < 0.001f)).Select(o => o.name));
            var homeEnvGap = Min(homeAll, env.Where(o => !seatedWith.Contains(o.name)).ToList(), 0.05f);

            var handRs = new HashSet<Renderer>(rb[3].GetComponentsInChildren<Renderer>(true));
            bool Teeth(Renderer r) => r.name.EndsWith("_Teeth");
            List<WMesh> RobotNow(Func<Renderer, bool> keep) => robot.GetComponentsInChildren<Renderer>(true).Where(r => Visible(r) && keep(r) && !trayRs.Contains(r)).Select(World).Where(m => m != null).ToList();
            var carryMtx = Matrix4x4.TRS(hold.position, hold.rotation, Vector3.one) * Matrix4x4.TRS(trayHomePos, trayHomeRot, Vector3.one).inverse;
            var inHand = Split(tray, carryMtx);
            var trayCarriedAll = inHand.All;

            // ---- 静态握持（悬停位 H、夹住）：分类检查
            PoseCarry();
            var nonTeeth = RobotNow(r => handRs.Contains(r) && !Teeth(r)); var teeth = RobotNow(r => handRs.Contains(r) && Teeth(r)); var others = RobotNow(r => !handRs.Contains(r));
            var g1 = Min(nonTeeth, trayCarriedAll, 0.05f);
            var g2 = Min(teeth, new[] { inHand.body }.Concat(inHand.legs).ToList(), 0.05f);
            var g3 = Min(teeth, inHand.bars, 0.05f);
            var g4 = Min(others, trayCarriedAll, 0.05f);
            var perPart = new StringBuilder();
            foreach (var r in robot.GetComponentsInChildren<Renderer>(true).Where(r => handRs.Contains(r) && Visible(r)))
            {
                var m = World(r);
                float dB = Distance(m, inHand.body, 0.05f), dL = inHand.legs.Min(x => Distance(m, x, 0.05f)), dBar = inHand.bars.Min(x => Distance(m, x, 0.05f));
                perPart.AppendLine($"| {r.name} | {(Teeth(r) ? "爪齿（可接触握杆）" : "不可接触")} | {Mm(dB)} | {Mm(dL)} | {Mm(dBar)}{(Teeth(r) && dBar <= 0f ? "（握持接触，允许）" : "")} |");
            }
            md.AppendLine("## 1. 静态握持（七号在悬停位、右手夹住；托盘在新挂点上）\n");
            md.AppendLine("| 右手零件 | 规则 | ↔ 盘体 | ↔ 立柱 / 安装座 | ↔ 握杆直段 |\n|---|---|---|---|---|");
            md.Append(perPart);
            md.AppendLine($"\n- 右手非爪齿零件 ↔ 托盘全部：**{Mm(g1.d)} mm**（{g1.a} ↔ {g1.b}）；爪齿 ↔ 盘体 / 立柱：**{Mm(g2.d)} mm**（{g2.a} ↔ {g2.b}）；爪齿 ↔ 握杆：{Mm(g3.d)} mm（握持接触，允许）。");
            md.AppendLine($"- 七号其余部分（不含右手）↔ 手上的托盘：**{Mm(g4.d)} mm**（{g4.a} ↔ {g4.b}）。挂点朝向与托盘原位朝向差 {rotErr:F2}°。");
            md.AppendLine($"- 原位（托盘架上）：变体 ↔ 环境（不含它本来就坐着的 {string.Join("、", seatedWith)}）{Mm(homeEnvGap.d)} mm（{homeEnvGap.a} ↔ {homeEnvGap.b}）。\n");

            // ---- 路线
            List<WMesh> At(List<WMesh> ms, Vector3 p) => ms.Select(m => Moved(m, Matrix4x4.Translate(p - H))).ToList();
            (float d, string pair) Near(List<WMesh> mv, List<WMesh> obs, float cap)
            {
                var all = mv[0].b; foreach (var m in mv) all.Encapsulate(m.b); all.Expand(0.1f);
                var r = Min(mv, obs.Where(o => o.b.Intersects(all)).ToList(), cap); return (r.d, r.a + " ↔ " + r.b);
            }
            (float d, string pair) Leg(List<WMesh> moving, Vector3 from, Vector3 to, List<WMesh> obs, float skipEnd = 0f)
            {
                float best = 0.05f; string pair = "-"; int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / 0.01f));
                for (int k = 0; k <= n; k++)
                {
                    var p = Vector3.Lerp(from, to, k / (float)n);
                    if (skipEnd > 0f && Vector3.Distance(p, to) < skipEnd) continue;
                    var r = Near(At(moving, p), obs, best); if (r.d < best) { best = r.d; pair = r.pair; }
                }
                return (best, pair);
            }
            PoseRest(); var robotRest = RobotNow(r => true);
            PoseCarry(); var robotGrip = RobotNow(r => true);
            var loaded = robotGrip.Concat(trayCarriedAll).ToList();
            var envNoSeat = env.Where(o => !seatedWith.Contains(o.name)).ToList();

            // 放盘高度：托盘离原位多少（从 0 开始），右手（夹住）↔ 环境 ≥ 2 mm
            float placeAbove = -1f; (float d, string pair) placeGap = (0f, "-");
            foreach (float a in new[] { 0f, 0.002f, 0.004f, 0.006f, 0.008f, 0.010f })
            {
                placeGap = Near(At(robotGrip, S + Vector3.up * a), env, 0.05f);
                if (placeGap.d >= Grip) { placeAbove = a; break; }
            }
            if (placeAbove < 0f) placeAbove = 0.010f;
            var P = S + Vector3.up * placeAbove;
            var trayAtP = Split(tray, Matrix4x4.Translate(Vector3.up * placeAbove));

            // 在 P 张开上爪：原来不接触托盘的零件张开后也不接触
            PoseCarry(1f, 1f);
            var openAll = RobotNow(r => true);
            var openHandNonTeeth = RobotNow(r => handRs.Contains(r) && !Teeth(r)); var openTeeth = RobotNow(r => handRs.Contains(r) && Teeth(r));
            var jawOpenGap = Near(At(openHandNonTeeth, P), trayAtP.All, 0.05f);
            var jawOpenTeethGap = Near(At(openTeeth, P), new[] { trayAtP.body }.Concat(trayAtP.legs).ToList(), 0.05f);
            PoseCarry();

            // 滑出方向：开上爪后，爪齿 ↔ 握杆要分开到 ≥ 2 mm 且不再碰回；其余一切 ↔ 托盘（全部几何）与环境全程 ≥ 2 mm；放手后 ↔ 原位托盘 ≥ 2 mm
            var right = body.right; right.y = 0; right.Normalize(); var fwd = body.forward; fwd.y = 0; fwd.Normalize();
            var outward = (tray.TransformPoint(tray.Find("GripPoint_R").localPosition) - tray.position); outward.y = 0;
            var outA = Vector3.Project(outward, tray.right).normalized; outA.y = 0; outA.Normalize();
            var dirs = new List<(string, Vector3)> { ("外（离开盘中心）", outA), ("外后", (outA - fwd).normalized), ("外前", (outA + fwd).normalized), ("右", right), ("右后", (right - fwd).normalized),
                                                     ("后", -fwd), ("前", fwd), ("下外", (outA - Vector3.up * 0.4f).normalized) };
            (string name, Vector3 v, float min, float release) best = ("-", outA, -1f, 0f);
            var dirTable = new StringBuilder();
            const float step = 0.004f, maxSlide = 0.12f;
            foreach (var (n, v) in dirs)
            {
                float release = -1f, fresh = 0.05f, envMin = 0.05f, after = 0.05f; string why = "", pair = "-";
                for (float s = 0f; s <= maxSlide + 1e-4f; s += step)
                {
                    var p = P + v * s;
                    var nt = Near(At(openHandNonTeeth, p), trayAtP.All, fresh); if (nt.d < fresh) { fresh = nt.d; pair = nt.pair; }
                    var te = Near(At(openTeeth, p), new[] { trayAtP.body }.Concat(trayAtP.legs).ToList(), fresh); if (te.d < fresh) { fresh = te.d; pair = te.pair; }
                    var e = Near(At(openAll, p), envNoSeat, envMin); if (e.d < envMin) envMin = e.d;
                    var tb = Near(At(openTeeth, p), trayAtP.bars, 0.05f);
                    if (release < 0f) { if (tb.d >= Grip) release = s; }
                    else
                    {
                        if (tb.d < Grip && why == "") why = $"爪齿在 {s * 1000:F0} mm 又碰回握杆";
                        var h = Near(At(openAll, p), homeAll, after); if (h.d < after) after = h.d;
                    }
                }
                if (release < 0f) why = "爪齿始终没离开握杆";
                float score = why != "" ? -1f : Mathf.Min(fresh, Mathf.Min(envMin, after));
                dirTable.AppendLine($"| {n} | {(release < 0 ? "—" : (release * 1000).ToString("F0") + " mm")} | {Mm(fresh)} mm | {Mm(envMin)} mm | {(release < 0 ? "—" : Mm(after) + " mm")} | {(why != "" ? why : pair)} |");
                if (score > best.min) best = (n, v, score, release);
            }
            var R = P + best.v * Mathf.Max(best.release, step);
            var W = P + best.v * Mathf.Max(best.release + 0.04f, 0.08f);   // 多退一点再升：升起时爪架不擦握杆

            // 搬运高度
            var armNames = new HashSet<string>(rb[0].GetComponentsInChildren<Renderer>(true).Concat(lb[0].GetComponentsInChildren<Renderer>(true)).Select(r => r.name));
            bool Seat(string n) => n.Contains("RootSeat") || n.Contains("RootRing") || n.Contains("RootBolts");
            List<(string leg, float d, string pair, float goal)> legs = null; float dhChosen = 0f;
            for (float dh = 0.06f; dh <= 0.451f; dh += 0.03f)
            {
                var T1 = H + Vector3.up * dh; var T2 = new Vector3(P.x, T1.y, P.z); var WU = new Vector3(W.x, T1.y, W.z);
                var l = new List<(string, float, string, float)>();
                void Add(string name, (float d, string pair) r, float goal) => l.Add((name, r.d, r.pair, goal));
                float leanMin = 0.05f; string leanPair = "-"; var pivot = body.position + (T1 - H);
                for (float deg = 0f; deg <= LeanDeg + 1.01f; deg += 0.5f)
                {
                    var q = Quaternion.AngleAxis(deg, homeRot * Vector3.forward);
                    var m = Matrix4x4.TRS(pivot, q, Vector3.one) * Matrix4x4.Translate(-pivot) * Matrix4x4.Translate(T1 - H);
                    var r = Near(loaded.Select(x => Moved(x, m)).ToList(), env, leanMin); if (r.d < leanMin) { leanMin = r.d; leanPair = r.pair; }
                }
                Add($"开场带盘停在 T1，左倾 0–{LeanDeg + 1:F0}°", (leanMin, leanPair), Clear);
                Add("带盘：T1 平移到托盘架上方 T2", Leg(loaded, T1, T2, env), Clear);
                Add($"带盘：T2 下降到放盘位 P（托盘离原位 {placeAbove * 1000:F0} mm；最后 20 mm 托盘 ↔ 它原位就贴着的件不计）", Leg(robotGrip.Concat(trayCarriedAll).ToList(), T2, P, envNoSeat, 0.02f), Clear);
                Add("带盘：最后 20 mm 落座（托盘 ↔ 旁边的件，原位关系；不含它坐着的托盘架）", Leg(robotGrip.Concat(trayCarriedAll).ToList(), P + Vector3.up * 0.02f, P, envNoSeat), 0.005f);
                Add("夹住：七号（含右爪，不含托盘）↔ 环境，T2 下降到 P", Leg(robotGrip, T2, P, env), Grip);
                Add("在 P 张开上爪：右手非爪齿零件 ↔ 托盘全部", jawOpenGap, Grip);
                Add("在 P 张开上爪：爪齿 ↔ 盘体 / 立柱", jawOpenTeethGap, Grip);
                Add($"上爪开、沿“{best.name}”滑出（新接触、环境、放手后原位托盘）", (best.min, "-"), Grip);
                Add("空手：W 升到搬运高度", Leg(openAll, W, WU, envNoSeat.Concat(homeAll).ToList()), Clear);
                Add("空手：平移回 T1", Leg(openAll, WU, T1, envNoSeat.Concat(homeAll).ToList()), Clear);
                float blendMin = 0.05f; string blendPair = "-";
                for (int k = 0; k <= 5; k++)
                {
                    PoseCarry(k / 5f, 1f);
                    var arms = At(RobotNow(r => armNames.Contains(r.name) && !Seat(r.name)), T1);
                    var oth = At(RobotNow(r => !armNames.Contains(r.name) && !Seat(r.name)), T1);
                    var r1 = Min(arms, oth.Concat(env).ToList(), blendMin); if (r1.d < blendMin) { blendMin = r1.d; blendPair = r1.a + " ↔ " + r1.b; }
                }
                PoseCarry();
                Add("两臂交还 Animator（T1；不计根环与根座铰接面）", (blendMin, blendPair), Clear);
                Add("空手、两臂原姿态：T1 竖直落回悬停位 H（导板内）", Leg(robotRest, T1, H, env), Guide);
                legs = l; dhChosen = dh;
                bool HeightDependent(string n) => !n.StartsWith("两臂交还") && !n.StartsWith("在 P") && !n.StartsWith("上爪开") && !n.StartsWith("夹住");
                if (l.Where(x => HeightDependent(x.Item1)).All(x => x.Item2 >= x.Item4)) break;
            }

            md.AppendLine("## 2. 演出路线（刚体平移的静态检查；逐帧真实播放见 motion_play.md）\n");
            md.AppendLine($"- 悬停位 H {H:F4}；放盘位 P = 托盘原位 + {placeAbove * 1000:F0} mm（在 P 夹住时右手 ↔ 环境 {Mm(placeGap.d)} mm，{placeGap.pair}）；放手：沿“{best.name}”{Mathf.Max(best.release, step) * 1000:F0} mm；退出终点再多 20 mm；搬运高度 dh = **{dhChosen * 1000:F0} mm**。");
            md.AppendLine("- 旧路线常量（离原位 8 mm、右后 76 mm、dh 60 mm）是旧托盘的结果，这里全部按变体重算。\n");
            md.AppendLine("上爪张开后各滑出方向（每 4 mm 一档，最多 120 mm）：\n\n| 方向 | 爪齿离开握杆 | 新接触最近（全部托盘几何） | 七号 ↔ 环境 | 放手后 ↔ 原位托盘 | 最紧处 / 原因 |\n|---|---|---|---|---|---|");
            md.Append(dirTable);
            md.AppendLine("\n| 段 | 最小间隙 | 阈值 | 最近的两件 |\n|---|---|---|---|");
            foreach (var l in legs) md.AppendLine($"| {l.leg} | {(l.d >= 0.05f ? "≥ 50" : (l.d * 1000).ToString("F1"))} mm | {l.goal * 1000:F0} mm{(l.d < l.goal ? " ⚠ 未达到" : "")} | {l.pair} |");
            md.AppendLine("\n测距局限：不算边到边最近距离（两条边擦过时结果偏大，最多约一个三角形边长，这里的网格边长约 2–5 mm）；共面重叠判不出穿插。近距离处看剖视图。");

            PoseRest();
            robot.SetPositionAndRotation(homePos, homeRot);
            var t1 = H + Vector3.up * dhChosen;
            path = new RoutePath
            {
                hover = H, hoverRot = homeRot, overDock = t1, overShelf = new Vector3(P.x, t1.y, P.z), place = P, release = R, withdraw = W, withdrawUp = new Vector3(W.x, t1.y, W.z),
                notes = $"搬运高度 dh = {dhChosen * 1000:F0} mm（悬停位上方）；放盘：托盘降到离原位 {placeAbove * 1000:F0} mm；上爪张开后沿“{best.name}”滑出 {Mathf.Max(best.release, step) * 1000:F0} mm 放手，再退 20 mm；" +
                        $"世界坐标（隔离验证场景，与发布场景布局 B 相同）：H {H:F4}，P {P:F4}，R {R:F4}，W {W:F4}。程序接入时按发布场景重算，不照抄。",
            };
            return true;
        }

        static string Mm(float d) => d >= 0.05f ? "≥ 50" : (d * 1000f).ToString("F1");
    }
}
