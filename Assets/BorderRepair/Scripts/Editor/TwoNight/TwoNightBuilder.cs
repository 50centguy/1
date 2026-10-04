using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Data;
using BorderRepair.Dock;
using BorderRepair.EditorTools;
using BorderRepair.FirstOrder;
using BorderRepair.FirstOrder.EditorTools.LayoutAB;
using BorderRepair.FirstOrder.EditorTools.Slice;
using BorderRepair.FirstOrder.Slice;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using static BorderRepair.TwoNight.MeshClearance;

namespace BorderRepair.TwoNight.EditorTools
{
    /// <summary>
    /// 两晚切片：数据、场景、构建。全部用 Editor API 生成，不手写 YAML。
    /// - 柜台场景 = 原型营业场景 RepairStation_Prototype 的副本（原场景、原案例、原 Final prefab 不改），换成只有收藏家通讯器一单的营业数据；
    /// - 七号场景 = 布局 B 场景的副本（原 B 场景、正式首单场景、七号鼠标核心场景都不改），加切片鼠标界面、右臂端盘覆盖层、右腕挂点、屏幕状态字、端盘演出、两晚协调；
    /// - 主菜单场景；
    /// - Windows 构建：明确场景列表（主菜单、柜台、七号、七号鼠标核心回归入口），输出到新的版本目录，不覆盖 Builds/TwoNightSlice（8dc8bb8 基准包）。
    /// 端盘姿态取自美术审计 art/unit07-asset-audit 0c1c906 的 tray_handoff.md 第 3 节（右手单手纵握）。
    /// </summary>
    public static class TwoNightBuilder
    {
        public const string DataDir = "Assets/BorderRepair/Data/TwoNight";
        public const string SceneDir = "Assets/BorderRepair/Scenes/Slice";
        public const string MenuPath = SceneDir + "/" + TwoNightScenes.Menu + ".unity";
        public const string CounterPath = SceneDir + "/" + TwoNightScenes.Counter + ".unity";
        public const string RobotPath = SceneDir + "/" + TwoNightScenes.Robot + ".unity";
        public const string DocsDir = "Docs/Integration/TwoNightSlice";
        public const string EconomyPath = DataDir + "/TwoNightEconomy.asset";
        public const string CasePath = DataDir + "/Case_N1_Collector_Communicator.asset";
        public const string ShiftPath = DataDir + "/Shift_Night1.asset";
        const string RobotFbx = "Assets/RobotV4/Model/robot-final.fbx";
        // 美术交付的零件盘变体（art/unit07-tray-variant 658f8f7，已合入本分支）：提手抬高 45 mm 悬臂式，右手单手握持
        public const string TrayVariantPrefab = "Assets/BorderRepair/Art/Unit07TrayVariant/Prefabs/Dock_PartsTray_HandleRaised.prefab";
        public const string GripDataPath = "Assets/BorderRepair/Art/Unit07TrayVariant/Data/Unit07TrayGripData_HandleRaised.asset";

        // 美术审计 0c1c906 · tray_measure.json "recommended"（右手，纵握近侧横杆，握点在横杆中心前 30 mm）
        static readonly float[] CarryJoints = { 3.16558f, 36.33465f, -58.62568f, 16.34933f };   // 根环 / 肩 / 肘 / 腕，绕骨骼本地 X，相对 Idle_Hover 第 0 帧
        const float GripJawT = 0.32071f;
        // 放盘：托盘降到离原位 8 mm 时下爪离托盘架边沿约 4.7 mm（grip_vs_shelf.md：抬 6 mm 时 2 mm）
        const float ClearanceGoal = 0.010f, GuideGoal = 0.002f, GripGoal = 0.002f, LeanDeg = 4f;

        static Font LoadFont() => TwoNightSlice.EnsureFont();

        [MenuItem("Border Repair/Two-Night Slice/Build Night Scenes (Menu, Counter, Unit07)")]
        public static void BuildAll()
        {
            EnsureData();
            BuildCounter();
            BuildRobot();
            BuildMenu();
            AssetDatabase.SaveAssets();
            // 编辑器构建列表（PlayMode 测试按场景名加载也要用）：两晚场景排在最前，原有条目保留在后面
            var keep = EditorBuildSettings.scenes.Where(e => !BuildScenes.Contains(e.path)).ToList();
            EditorBuildSettings.scenes = BuildScenes.Select(x => new EditorBuildSettingsScene(x, true)).Concat(keep).ToArray();
            Debug.Log("[TwoNightBuild] 场景已生成");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ 数据

        public static (TwoNightEconomy eco, RepairShiftData shift) EnsureData()
        {
            Directory.CreateDirectory(DataDir);
            var eco = AssetDatabase.LoadAssetAtPath<TwoNightEconomy>(EconomyPath);
            if (eco == null) { eco = ScriptableObject.CreateInstance<TwoNightEconomy>(); AssetDatabase.CreateAsset(eco, EconomyPath); }

            // 只在缺失时创建：场景里引用的是这些资产的 GUID，重建会让已生成场景的引用变空
            var c = AssetDatabase.LoadAssetAtPath<RepairCaseData>(CasePath);
            if (c == null)
            {
                if (!AssetDatabase.CopyAsset(PrototypeBuilder.CaseDir + "/Case_01_Communicator.asset", CasePath)) throw new Exception("复制通讯器案例失败");
                c = AssetDatabase.LoadAssetAtPath<RepairCaseData>(CasePath);
            }
            c.caseId = "case_n1_collector_communicator";
            c.customerName = "收藏家";
            c.customerStatement = "开得了机，就是收不到信号。不是急件，修好就行。里面的通话记录别清掉。";
            EditorUtility.SetDirty(c);

            var shift = AssetDatabase.LoadAssetAtPath<RepairShiftData>(ShiftPath);
            if (shift == null) { shift = ScriptableObject.CreateInstance<RepairShiftData>(); AssetDatabase.CreateAsset(shift, ShiftPath); }
            shift.shiftTitle = "第一晚 · 边境维修站";
            shift.targetDurationSeconds = 300f;
            shift.cases = new List<RepairCaseData> { c };
            EditorUtility.SetDirty(shift);
            AssetDatabase.SaveAssets();
            return (eco, shift);
        }

        // ------------------------------------------------------------------ 柜台

        public static void BuildCounter()
        {
            var (eco, shift) = EnsureData();
            Directory.CreateDirectory(SceneDir);
            AssetDatabase.DeleteAsset(CounterPath);
            if (!AssetDatabase.CopyAsset(PrototypeBuilder.ScenePath, CounterPath)) throw new Exception("复制原型场景失败");
            var scene = EditorSceneManager.OpenScene(CounterPath, OpenSceneMode.Single);
            // 打开场景（Single）会卸载没被引用的资产，先前拿到的对象会失效：开场景之后再按路径取
            eco = AssetDatabase.LoadAssetAtPath<TwoNightEconomy>(EconomyPath);
            shift = AssetDatabase.LoadAssetAtPath<RepairShiftData>(ShiftPath);
            var station = UnityEngine.Object.FindFirstObjectByType<RepairStationController>();
            var so = new SerializedObject(station);
            so.FindProperty("shift").objectReferenceValue = shift;
            so.ApplyModifiedPropertiesWithoutUndo();
            var go = new GameObject("TwoNightCounterDirector（两晚：柜台与账本）");
            go.AddComponent<TwoNightCounterDirector>().Configure(station, eco, LoadFont());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // ------------------------------------------------------------------ 主菜单

        public static void BuildMenu()
        {
            var (eco, _) = EnsureData();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // 打开场景（Single）会卸载没被引用的资产，先前拿到的对象会失效：开场景之后再按路径取
            eco = AssetDatabase.LoadAssetAtPath<TwoNightEconomy>(EconomyPath);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.gameObject.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.05f, 0.06f, 0.06f);
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            new GameObject("TwoNightMenu").AddComponent<TwoNightMenu>().Configure(eco, LoadFont());
            EditorSceneManager.SaveScene(scene, MenuPath);
        }

        // ------------------------------------------------------------------ 七号

        static AnimationClip Clip(string n) => AssetDatabase.LoadAllAssetsAtPath(RobotFbx).OfType<AnimationClip>().First(c => c.name == n);

        static Transform Under(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        public static void BuildRobot()
        {
            var (eco, _) = EnsureData();
            var font = LoadFont();
            Directory.CreateDirectory(SceneDir);
            AssetDatabase.DeleteAsset(RobotPath);
            if (!AssetDatabase.CopyAsset(LayoutABScenes.SceneB, RobotPath)) throw new Exception("复制布局 B 场景失败");
            var scene = EditorSceneManager.OpenScene(RobotPath, OpenSceneMode.Single);
            // 打开场景（Single）会卸载没被引用的资产，先前拿到的对象会失效：开场景之后再按路径取
            eco = AssetDatabase.LoadAssetAtPath<TwoNightEconomy>(EconomyPath);
            var flow = UnityEngine.Object.FindFirstObjectByType<FirstOrderFlow>();
            var input = UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>();
            input.ConfigureHud(false, true, font, new Vector2(680f, 12f));
            EditorUtility.SetDirty(input);
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            var mainCam = Camera.main != null ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 0) mainCam.gameObject.AddComponent<AudioListener>();   // 布局 B 原场景没有
            var view = new GameObject(TwoNightSlice.UiName).AddComponent<SliceView>();
            view.Configure(flow, input, font);

            // ---- 七号右臂：静止姿态、端盘姿态、夹爪
            var robot = flow.Dock.RobotRoot;
            var body = Under(robot, "Body");
            var bones = new Transform[6];
            bones[0] = Under(robot, "Arm_R_RootPivot"); bones[1] = Under(robot, "Arm_R_Shoulder"); bones[2] = Under(robot, "Arm_R_Elbow");
            bones[3] = bones[2].Cast<Transform>().First(t => t.name == "Arm_R_Wrist");
            bones[4] = bones[3].Cast<Transform>().First(t => t.name == "Arm_R_JawUpper");
            bones[5] = bones[3].Cast<Transform>().First(t => t.name == "Arm_R_JawLower");
            Quaternion[] Sample(string clip) { Clip(clip).SampleAnimation(robot.gameObject, 0f); return bones.Select(b => b.localRotation).ToArray(); }
            var closed = Sample("Pose_Gripper_Closed"); var half = Sample("Pose_Gripper_Half"); var open = Sample("Pose_Gripper_Open");
            var lb = new Transform[4];
            lb[0] = Under(robot, "Arm_L_RootPivot"); lb[1] = Under(robot, "Arm_L_Shoulder"); lb[2] = Under(robot, "Arm_L_Elbow");
            lb[3] = lb[2].Cast<Transform>().First(t => t.name == "Arm_L_Wrist");
            Clip("Arm_Deploy_L").SampleAnimation(robot.gameObject, 0f);
            var stow = lb.Select(b => b.localRotation).ToArray();
            var rest = Sample("Idle_Hover");
            var steady = new[] { Under(robot, "Root"), body };
            var steadyPos = steady.Select(b => b.localPosition).ToArray(); var steadyRot = steady.Select(b => b.localRotation).ToArray();
            var restL = lb.Select(b => b.localRotation).ToArray();
            var carry = new Quaternion[6];
            for (int i = 0; i < 4; i++) carry[i] = rest[i] * Quaternion.AngleAxis(CarryJoints[i], Vector3.right);
            carry[4] = Quaternion.Slerp(closed[4], half[4], GripJawT);
            carry[5] = Quaternion.Slerp(closed[5], half[5], GripJawT);
            var openJaw = new[] { half[4], half[5] };   // 接近 / 放开时用 Half（齿间距 21.5 mm）：托盘凹在托盘架里，全开的下爪会碰到托盘架
            var overlay = robot.gameObject.AddComponent<TrayCarryOverlay>();
            overlay.Configure(bones, carry, openJaw, lb, stow);
            overlay.ConfigureSteady(steady, steadyPos, steadyRot);

            // ---- 托盘：只在本发布场景里停用原 Dock_PartsTray（场景改动，不改维修座预制体 / FBX / 共享材质），换上美术变体，放在原托盘的父对象和本地位姿上
            var origTray = GameObject.Find("Dock_PartsTray");
            var variant = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TrayVariantPrefab), scene);
            variant.transform.SetParent(origTray.transform.parent, false);
            variant.transform.localPosition = origTray.transform.localPosition; variant.transform.localRotation = origTray.transform.localRotation; variant.transform.localScale = origTray.transform.localScale;
            origTray.SetActive(false);
            var grip = AssetDatabase.LoadAssetAtPath<BorderRepair.Art.Unit07TrayVariant.Unit07TrayGripData>(GripDataPath);
            var hold = new GameObject("TwoNight_TrayHoldPoint（右手端盘挂点）").transform;
            hold.SetParent(bones[3], false);
            hold.localPosition = grip.holdLocalPosition; hold.localRotation = grip.holdLocalRotation;   // 美术按变体握杆重算的挂点（旧挂点不再使用）
            var tray = variant.GetComponent<DockPickable>();
            var tso = new SerializedObject(tray); tso.FindProperty("holdPoint").objectReferenceValue = hold; tso.ApplyModifiedPropertiesWithoutUndo();

            // ---- 屏幕状态字（世界空间，贴在屏幕玻璃前面）
            var glass = Under(robot, "ScreenGlass").GetComponent<Renderer>();
            var status = BuildScreenStatus(glass, body, font);

            // ---- 端盘路径（预制体根在悬停位，右臂端盘姿态）与静态间隙检查
            var report = new StringBuilder();
            var path = PlanIncident(flow, robot, body, bones, carry, openJaw, rest, lb, stow, restL, hold, tray, LeanDeg, report);
            Clip("Idle_Hover").SampleAnimation(robot.gameObject, 0f);

            var incident = new GameObject("TwoNight_TrayIncident（第一晚端盘失衡演出）").AddComponent<TrayIncident>();
            incident.Configure(robot, body, overlay, tray, hold, status, path.hover, path.hoverRot, path.overDock, path.overShelf, path.place, path.release, path.withdraw, path.withdrawUp, LeanDeg);
            var director = new GameObject("TwoNightRobotDirector（两晚：七号）").AddComponent<TwoNightRobotDirector>();
            director.Configure(flow, input, view, incident, eco, font);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory(DocsDir);
            File.WriteAllText(Path.Combine(DocsDir, "incident_motion.md"), report.ToString(), new UTF8Encoding(false));
        }

        static GameObject BuildScreenStatus(Renderer glass, Transform body, Font font)
        {
            var go = new GameObject("TwoNight_ScreenStatus（屏幕状态文字）", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(glass.transform, false);
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(300, 200);
            var fwd = body.forward;
            rt.position = glass.bounds.center + fwd * 0.012f;
            rt.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            float width = Vector3.Dot(glass.bounds.size, new Vector3(Mathf.Abs(body.right.x), Mathf.Abs(body.right.y), Mathf.Abs(body.right.z)));
            rt.localScale = Vector3.one * (width * 0.8f / 300f) / glass.transform.lossyScale.x;
            var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(go.transform, false);
            var brt = (RectTransform)bg.transform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0.03f, 0.05f, 0.03f, 0.92f);
            var tgo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            tgo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)tgo.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = tgo.GetComponent<Text>();
            t.font = font; t.fontSize = 46; t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter;
            t.color = new Color(0.98f, 0.72f, 0.25f); t.text = "TRAY\nUNSTABLE";
            go.SetActive(false);
            return go;
        }

        struct IncidentPath { public Vector3 hover, overDock, overShelf, place, release, withdraw, withdrawUp; public Quaternion hoverRot; }

        /// <summary>
        /// 端盘路线（托盘 = 提手抬高变体，多子物体）：开场 T1 端着盘 → 左倾回正 → T2（托盘架上方）→ P（托盘落座位，离原位 placeAbove）→ 上爪张开
        /// → 沿选出的方向水平滑出到 R（爪齿离开握杆）→ 托盘落到原位 → 再退到 W → WU（同搬运高度）→ T1 → 两臂交还 → 竖直落回 H。
        /// 检查规则：只允许“爪齿 ↔ 握杆直段”接触；右手其余零件（上下爪爪身、爪架、销轴、腕）↔ 托盘全部几何（含握杆）必须 ≥ 阈值；爪齿 ↔ 盘体 / 立柱也必须 ≥ 阈值。
        /// 托盘分区见 TrayGeometry；停用的旧盘不参与（只取激活的 Renderer）。七号按刚体平移检查，Animator 浮动和过渡另由 PlayMode 探针播放检查。
        /// 测距：MeshClearance（双向点到三角形 + 双面边穿三角形），不算边到边、判不出共面重叠，2–5 mm 级数字是估计。
        /// </summary>
        static IncidentPath PlanIncident(FirstOrderFlow flow, Transform robot, Transform body, Transform[] rb, Quaternion[] carry, Quaternion[] openJaw,
                                         Quaternion[] restR, Transform[] lb, Quaternion[] stow, Quaternion[] restL, Transform hold, DockPickable tray,
                                         float leanDeg, StringBuilder md)
        {
            var dock = flow.Dock;
            float hoverHeight = new SerializedObject(dock).FindProperty("hoverHeight").floatValue;
            var homePos = robot.position; var homeRot = robot.rotation;
            var H = dock.RobotAnchor.position + Vector3.up * hoverHeight;
            robot.SetPositionAndRotation(H, homeRot);
            void PoseRest() { for (int i = 0; i < 6; i++) rb[i].localRotation = restR[i]; for (int i = 0; i < 4; i++) lb[i].localRotation = restL[i]; }
            void PoseCarry(float w = 1f, float upperOpen = 0f)
            {
                for (int i = 0; i < 6; i++) { var t = i == 4 ? Quaternion.Slerp(carry[4], openJaw[0], upperOpen) : carry[i]; rb[i].localRotation = Quaternion.Slerp(restR[i], t, w); }
                for (int i = 0; i < 4; i++) lb[i].localRotation = Quaternion.Slerp(restL[i], stow[i], w);
            }
            PoseCarry();
            var trayT = tray.transform;
            var trayHomePos = trayT.position; var trayHomeRot = trayT.rotation;
            var S = H + (trayHomePos - hold.position);
            float rotErr = Quaternion.Angle(hold.rotation, trayHomeRot);

            bool Visible(Renderer r) => r.enabled && r.gameObject.activeInHierarchy;
            var trayRs = new HashSet<Renderer>(trayT.GetComponentsInChildren<Renderer>(true));
            var dockRoot = GameObject.Find("Unit07ServiceDock").transform; var bench = GameObject.Find("WorkbenchArea").transform;
            var env = dockRoot.GetComponentsInChildren<Renderer>(true).Concat(bench.GetComponentsInChildren<Renderer>(true))
                .Where(r => Visible(r) && !trayRs.Contains(r) && r.bounds.max.y < 2.2f).Select(World).Where(m => m != null).ToList();
            var homeParts = TrayGeometry.Split(trayT, Matrix4x4.identity); var homeAll = homeParts.All;
            var seatedWith = new HashSet<string>(env.Where(o => homeAll.Any(t => Distance(t, o, 0.002f) < 0.001f)).Select(o => o.name));
            var envNoSeat = env.Where(o => !seatedWith.Contains(o.name)).ToList();
            var handRs = new HashSet<Renderer>(rb[3].GetComponentsInChildren<Renderer>(true));
            List<WMesh> RobotNow(Func<Renderer, bool> keep) => robot.GetComponentsInChildren<Renderer>(true).Where(r => Visible(r) && keep(r) && !trayRs.Contains(r)).Select(World).Where(m => m != null).ToList();
            var carryMtx = Matrix4x4.TRS(hold.position, hold.rotation, Vector3.one) * Matrix4x4.TRS(trayHomePos, trayHomeRot, Vector3.one).inverse;
            var inHand = TrayGeometry.Split(trayT, carryMtx); var trayCarried = inHand.All;

            // ---- 静态握持（H、夹住）
            PoseCarry();
            var gNonTeeth = Min(RobotNow(r => handRs.Contains(r) && !TrayGeometry.IsTeeth(r)), trayCarried, 0.05f);
            var gTeeth = Min(RobotNow(r => handRs.Contains(r) && TrayGeometry.IsTeeth(r)), inHand.NonBar, 0.05f);
            var gBar = Min(RobotNow(r => handRs.Contains(r) && TrayGeometry.IsTeeth(r)), inHand.bars, 0.05f);
            var gRest = Min(RobotNow(r => !handRs.Contains(r)), trayCarried, 0.05f);
            var homeGap = Min(homeAll, envNoSeat, 0.05f);

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
            var loaded = robotGrip.Concat(trayCarried).ToList();

            // 放盘高度：托盘离原位从 0 起，夹住的右手 ↔ 环境 ≥ 2 mm
            float placeAbove = -1f; (float d, string pair) placeGap = (0f, "-");
            foreach (float a in new[] { 0f, 0.002f, 0.004f, 0.006f, 0.008f, 0.010f })
            {
                placeGap = Near(At(robotGrip, S + Vector3.up * a), env, 0.05f);
                if (placeGap.d >= GripGoal) { placeAbove = a; break; }
            }
            if (placeAbove < 0f) placeAbove = 0.010f;
            var P = S + Vector3.up * placeAbove;
            var trayAtP = TrayGeometry.Split(trayT, Matrix4x4.Translate(Vector3.up * placeAbove));

            PoseCarry(1f, 1f);
            var openAll = RobotNow(r => true);
            var openNonTeeth = RobotNow(r => handRs.Contains(r) && !TrayGeometry.IsTeeth(r)); var openTeeth = RobotNow(r => handRs.Contains(r) && TrayGeometry.IsTeeth(r));
            var jawOpenGap = Near(At(openNonTeeth, P), trayAtP.All, 0.05f);
            var jawOpenTeeth = Near(At(openTeeth, P), trayAtP.NonBar, 0.05f);
            PoseCarry();

            // 滑出方向：爪齿 ↔ 握杆分开到 ≥ 2 mm 且不再碰回；其余一切 ↔ 托盘全部几何与环境全程 ≥ 2 mm；放手后 ↔ 原位托盘 ≥ 2 mm
            var right = body.right; right.y = 0; right.Normalize(); var fwd = body.forward; fwd.y = 0; fwd.Normalize();
            var gpW = trayT.TransformPoint(trayT.Find("GripPoint_R") != null ? trayT.Find("GripPoint_R").localPosition : Vector3.zero) - trayT.position; gpW.y = 0;
            var outA = Vector3.Project(gpW, trayT.right); outA.y = 0; outA = outA.sqrMagnitude > 1e-8f ? outA.normalized : right;
            var dirs = new List<(string, Vector3)> { ("外（离开盘中心）", outA), ("外后", (outA - fwd).normalized), ("外前", (outA + fwd).normalized), ("右", right), ("右后", (right - fwd).normalized),
                                                     ("后", -fwd), ("前", fwd) };
            (string name, Vector3 v, float min, float release) best = ("-", outA, -1f, 0f);
            var dirTable = new StringBuilder();
            const float step = 0.004f, maxSlide = 0.12f;
            foreach (var (n, v) in dirs)
            {
                float release = -1f, fresh = 0.05f, envMin = 0.05f, after = 0.05f; string why = "", pair = "-";
                for (float s = 0f; s <= maxSlide + 1e-4f; s += step)
                {
                    var p = P + v * s;
                    var nt = Near(At(openNonTeeth, p), trayAtP.All, fresh); if (nt.d < fresh) { fresh = nt.d; pair = nt.pair; }
                    var te = Near(At(openTeeth, p), trayAtP.NonBar, fresh); if (te.d < fresh) { fresh = te.d; pair = te.pair; }
                    var e = Near(At(openAll, p), envNoSeat, envMin); if (e.d < envMin) envMin = e.d;
                    var tb = Near(At(openTeeth, p), trayAtP.bars, 0.05f);
                    if (release < 0f) { if (tb.d >= GripGoal) release = s; }
                    else
                    {
                        if (tb.d < GripGoal && why == "") why = $"爪齿在 {s * 1000:F0} mm 又碰回握杆";
                        var h = Near(At(openAll, p), homeAll, after); if (h.d < after) after = h.d;
                    }
                }
                if (release < 0f) why = "爪齿始终没离开握杆";
                float score = why != "" ? -1f : Mathf.Min(fresh, Mathf.Min(envMin, after));
                dirTable.AppendLine($"| {n} | {(release < 0 ? "—" : (release * 1000).ToString("F0") + " mm")} | {MmS(fresh)} | {MmS(envMin)} | {(release < 0 ? "—" : MmS(after))} | {(why != "" ? why : pair)} |");
                if (score > best.min) best = (n, v, score, release);
            }
            var R = P + best.v * Mathf.Max(best.release, step);
            var W = P + best.v * Mathf.Max(best.release + 0.04f, 0.08f);

            var armNames = new HashSet<string>(rb[0].GetComponentsInChildren<Renderer>(true).Concat(lb[0].GetComponentsInChildren<Renderer>(true)).Select(r => r.name));
            bool Seat(string n) => n.Contains("RootSeat") || n.Contains("RootRing") || n.Contains("RootBolts");
            List<(string leg, float d, string pair, float goal)> legs = null; float dhChosen = 0f;
            for (float dh = 0.06f; dh <= 0.451f; dh += 0.03f)
            {
                var T1 = H + Vector3.up * dh; var T2 = new Vector3(P.x, T1.y, P.z); var WU = new Vector3(W.x, T1.y, W.z);
                var l = new List<(string, float, string, float)>();
                void Add(string name, (float d, string pair) r, float goal) => l.Add((name, r.d, r.pair, goal));
                float leanMin = 0.05f; string leanPair = "-"; var pivot = body.position + (T1 - H);
                for (float deg = 0f; deg <= leanDeg + 1.01f; deg += 0.5f)
                {
                    var q = Quaternion.AngleAxis(deg, homeRot * Vector3.forward);
                    var m = Matrix4x4.TRS(pivot, q, Vector3.one) * Matrix4x4.Translate(-pivot) * Matrix4x4.Translate(T1 - H);
                    var r = Near(loaded.Select(x => Moved(x, m)).ToList(), env, leanMin); if (r.d < leanMin) { leanMin = r.d; leanPair = r.pair; }
                }
                Add($"开场带盘停在 T1，左倾 0–{leanDeg + 1:F0}°（比演出多 1°）", (leanMin, leanPair), ClearanceGoal);
                Add("带盘：T1 平移到托盘架上方 T2", Leg(loaded, T1, T2, env), ClearanceGoal);
                Add($"带盘：T2 下降到放盘位 P（托盘离原位 {placeAbove * 1000:F0} mm；最后 20 mm 另列）", Leg(loaded, T2, P, envNoSeat, 0.02f), ClearanceGoal);
                Add("带盘：最后 20 mm 落座（托盘 ↔ 旁边的件，原位关系；不含它坐着的托盘架）", Leg(loaded, P + Vector3.up * 0.02f, P, envNoSeat), 0.005f);
                Add("夹住：七号（含右爪，不含托盘）↔ 环境，T2 下降到 P", Leg(robotGrip, T2, P, env), GripGoal);
                Add("在 P 张开上爪：右手非爪齿零件 ↔ 托盘全部", jawOpenGap, GripGoal);
                Add("在 P 张开上爪：爪齿 ↔ 盘体 / 立柱", jawOpenTeeth, GripGoal);
                Add($"上爪开、沿“{best.name}”滑出（新接触、环境、放手后原位托盘）", (best.min, "-"), GripGoal);
                Add("空手：W 升到搬运高度", Leg(openAll, W, WU, envNoSeat.Concat(homeAll).ToList()), ClearanceGoal);
                Add("空手：平移回 T1", Leg(openAll, WU, T1, envNoSeat.Concat(homeAll).ToList()), ClearanceGoal);
                float blendMin = 0.05f; string blendPair = "-";
                for (int k = 0; k <= 5; k++)
                {
                    PoseCarry(k / 5f, 1f);
                    var arms = At(RobotNow(r => armNames.Contains(r.name) && !Seat(r.name)), T1);
                    var oth = At(RobotNow(r => !armNames.Contains(r.name) && !Seat(r.name)), T1);
                    var r1 = Min(arms, oth.Concat(env).ToList(), blendMin); if (r1.d < blendMin) { blendMin = r1.d; blendPair = r1.a + " ↔ " + r1.b; }
                }
                PoseCarry();
                Add("两臂交还 Animator（T1；不计根环与根座铰接面）", (blendMin, blendPair), ClearanceGoal);
                Add("空手、两臂原姿态：T1 竖直落回悬停位 H（导板内，同正常落座路径）", Leg(robotRest, T1, H, env), GuideGoal);
                legs = l; dhChosen = dh;
                bool HeightDependent(string n) => !n.StartsWith("两臂交还") && !n.StartsWith("在 P") && !n.StartsWith("上爪开") && !n.StartsWith("夹住") && !n.StartsWith("带盘：最后");
                if (l.Where(x => HeightDependent(x.Item1)).All(x => x.Item2 >= x.Item4)) break;
            }

            md.AppendLine("# 第一晚端盘演出：路线与静态间隙（编辑器构建时检查）\n");
            md.AppendLine($"- 生成时间 {DateTime.Now:yyyy-MM-dd HH:mm}；场景 `{RobotPath}`（布局 B 副本）。托盘 = `{TrayVariantPrefab}`（提手抬高 45 mm 悬臂变体；原托盘在本场景停用，原维修座预制体不改）。");
            md.AppendLine($"- 右臂端盘姿态：审计关节 {string.Join(" / ", CarryJoints.Select(x => x.ToString("F2")))}°，夹爪 {GripJawT:F3}；挂点用美术交付的握持数据 `{GripDataPath}`：本地 {hold.localPosition * 1000:F2} mm，旋转 {hold.localRotation.x:F5}, {hold.localRotation.y:F5}, {hold.localRotation.z:F5}, {hold.localRotation.w:F5}；挂点与托盘原位朝向差 {rotErr:F2}°。");
            md.AppendLine("- 规则：只允许爪齿 ↔ 握杆直段接触；其余右手零件 ↔ 托盘全部几何、爪齿 ↔ 盘体 / 立柱都要 ≥ 阈值。测距 MeshClearance（不算边到边、判不出共面重叠，2–5 mm 级是估计）。\n");
            md.AppendLine("## 静态握持（悬停位、夹住）\n");
            md.AppendLine($"- 右手非爪齿零件 ↔ 托盘全部：**{MmS(gNonTeeth.d)}**（{gNonTeeth.a} ↔ {gNonTeeth.b}）；爪齿 ↔ 盘体 / 立柱：**{MmS(gTeeth.d)}**（{gTeeth.a} ↔ {gTeeth.b}）；爪齿 ↔ 握杆：{MmS(gBar.d)}（握持接触，允许）。");
            md.AppendLine($"- 七号其余部分 ↔ 手上托盘：**{MmS(gRest.d)}**（{gRest.a} ↔ {gRest.b}）。托盘原位 ↔ 环境（不含它坐着的 {string.Join("、", seatedWith)}）：{MmS(homeGap.d)}（{homeGap.a} ↔ {homeGap.b}）。\n");
            md.AppendLine("## 路线\n");
            md.AppendLine($"- 悬停位 H {H:F4}；放盘位 P = 托盘原位 + {placeAbove * 1000:F0} mm（夹住时右手 ↔ 环境 {MmS(placeGap.d)}，{placeGap.pair}）；上爪张开后沿“{best.name}”滑出 {Mathf.Max(best.release, step) * 1000:F0} mm 放手，再退到 {Vector3.Distance(P, W) * 1000:F0} mm 升起；搬运高度 dh = **{dhChosen * 1000:F0} mm**。旧托盘的路线常量不再使用。\n");
            md.AppendLine("上爪张开后各滑出方向（每 4 mm 一档，最多 120 mm）：\n\n| 方向 | 爪齿离开握杆 | 新接触最近（托盘全部几何） | 七号 ↔ 环境 | 放手后 ↔ 原位托盘 | 最紧处 / 原因 |\n|---|---|---|---|---|---|");
            md.Append(dirTable);
            md.AppendLine("\n| 段 | 最小间隙 | 阈值 | 最近的两件 |\n|---|---|---|---|");
            foreach (var l in legs) md.AppendLine($"| {l.leg} | {MmS(l.d)} | {l.goal * 1000:F0} mm{(l.d < l.goal ? " ⚠ 未达到" : "")} | {l.pair} |");
            md.AppendLine("\n两臂过渡的 9.0 mm（左肩叉 ↔ 机身挂点）是静止姿态本身的值（美术审计已记），过渡中不变小。刚体平移的静态检查；真实播放见 `incident_motion_play.md`。");

            PoseRest();
            robot.SetPositionAndRotation(homePos, homeRot);
            var t1 = H + Vector3.up * dhChosen;
            return new IncidentPath { hover = H, hoverRot = homeRot, overDock = t1, overShelf = new Vector3(P.x, t1.y, P.z), place = P, release = R, withdraw = W, withdrawUp = new Vector3(W.x, t1.y, W.z) };
        }

        static string MmS(float d) => d >= 0.05f ? "≥ 50 mm" : (d * 1000f).ToString("F1") + " mm";

        // ------------------------------------------------------------------ 构建

        public static readonly string[] BuildScenes = { MenuPath, CounterPath, RobotPath, TwoNightSlice.ScenePath };

        [MenuItem("Border Repair/Two-Night Slice/Build Windows Player (Night 1 → Night 2)")]
        public static void BuildWindows()
        {
            string tag = DateTime.Now.ToString("yyyyMMdd-HHmm");
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-buildTag");
            if (i >= 0 && i + 1 < args.Length) tag = args[i + 1];
            string dir = "Builds/TwoNightSlice_N1N2_" + tag;
            var mode = PlayerSettings.fullScreenMode; int w = PlayerSettings.defaultScreenWidth, h = PlayerSettings.defaultScreenHeight; bool resizable = PlayerSettings.resizableWindow;
            BuildReport report;
            try
            {
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900; PlayerSettings.resizableWindow = true;
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = BuildScenes, locationPathName = dir + "/TwoNightSlice.exe", target = BuildTarget.StandaloneWindows64, targetGroup = BuildTargetGroup.Standalone });
            }
            finally
            {
                PlayerSettings.fullScreenMode = mode; PlayerSettings.defaultScreenWidth = w; PlayerSettings.defaultScreenHeight = h; PlayerSettings.resizableWindow = resizable;
            }
            var s = report.summary;
            var sb = new StringBuilder();
            sb.AppendLine($"构建 {DateTime.Now:yyyy-MM-dd HH:mm:ss}，Unity {Application.unityVersion}");
            sb.AppendLine($"结果 {s.result}，用时 {s.totalTime.TotalSeconds:F0} s，大小 {s.totalSize / (1024f * 1024f):F1} MB，错误 {s.totalErrors}，警告 {s.totalWarnings}");
            sb.AppendLine($"输出 {Path.GetFullPath(dir + "/TwoNightSlice.exe")}（新版本目录；8dc8bb8 基准包 Builds/TwoNightSlice/ 不动）");
            sb.AppendLine("场景（明确列表，第 0 个是入口）：");
            for (int k = 0; k < BuildScenes.Length; k++) sb.AppendLine($"  {k}. {BuildScenes[k]}" + (k == 3 ? "（七号鼠标核心，回归测试入口：exe 加 -twoNightCore）" : ""));
            Directory.CreateDirectory(DocsDir);
            File.WriteAllText(Path.Combine(DocsDir, "build_report_n1n2.txt"), sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[TwoNightBuild] " + sb);
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
