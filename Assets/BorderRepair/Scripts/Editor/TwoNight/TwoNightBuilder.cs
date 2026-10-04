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

        // 美术审计 0c1c906 · tray_measure.json "recommended"（右手，纵握近侧横杆，握点在横杆中心前 30 mm）
        static readonly float[] CarryJoints = { 3.16558f, 36.33465f, -58.62568f, 16.34933f };   // 根环 / 肩 / 肘 / 腕，绕骨骼本地 X，相对 Idle_Hover 第 0 帧
        const float GripJawT = 0.32071f;
        static readonly Vector3 HoldPos = new Vector3(-0.12313f, -0.08609f, -0.10257f);           // 托盘原点相对右腕骨
        static readonly Quaternion HoldRot = new Quaternion(-0.5911f, -0.53961f, -0.42572f, 0.42211f);
        // 放盘：托盘降到离原位 8 mm 时下爪离托盘架边沿约 4.7 mm（grip_vs_shelf.md：抬 6 mm 时 2 mm）
        const float PlaceAbove = 0.008f, ClearanceGoal = 0.010f, GuideGoal = 0.002f, GripGoal = 0.002f, LeanDeg = 4f;

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

            var hold = new GameObject("TwoNight_TrayHoldPoint（右手端盘挂点）").transform;
            hold.SetParent(bones[3], false);
            hold.localPosition = HoldPos; hold.localRotation = HoldRot;
            var tray = GameObject.Find("Dock_PartsTray").GetComponent<DockPickable>();
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
        /// 端盘路径（收窄后的动作：七号一开场就端着盘，不从托盘架上夹起；七号只平移、不转向）：
        /// T1（H 上方 dh，端着盘）左倾 → 回正 → T2（托盘架上方，同高）→ P（托盘离原位 Drop）→ 上爪张开 → 沿退出方向滑到 R（下爪离开横杆）→ 托盘落回原位
        /// → 继续退到 W → WU（W 上方，同搬运高度）→ T1 → 两臂交还 → 竖直落回 H。
        /// 阈值：在维修座导板里竖直升降的那段 ≥ 2 mm（与正常落座同一路径），右爪在托盘架边上的几段 ≥ 2 mm（同审计静态握持标准），其余 ≥ 10 mm。
        /// 七号按刚体平移检查；Animator 的身体浮动、引擎摆动、权重过渡中的真实帧，另由 PlayMode 动作探针逐帧量。
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
                for (int i = 0; i < 6; i++)
                {
                    var target = i == 4 ? Quaternion.Slerp(carry[4], openJaw[0], upperOpen) : carry[i];
                    rb[i].localRotation = Quaternion.Slerp(restR[i], target, w);
                }
                for (int i = 0; i < 4; i++) lb[i].localRotation = Quaternion.Slerp(restL[i], stow[i], w);
            }

            PoseCarry();
            var trayT = tray.transform;
            var trayHomePos = trayT.position; var trayHomeRot = trayT.rotation;
            var S = H + (trayHomePos - hold.position);          // 手里的托盘正好在原位时的根位置（只用来推出 P）
            float rotErr = Quaternion.Angle(hold.rotation, trayHomeRot);
            var P = S + Vector3.up * PlaceAbove;

            var dockRoot = GameObject.Find("Unit07ServiceDock").transform;
            var bench = GameObject.Find("WorkbenchArea").transform;
            bool Visible(Renderer r) => r.enabled && r.gameObject.activeInHierarchy;
            var env = dockRoot.GetComponentsInChildren<Renderer>(true).Concat(bench.GetComponentsInChildren<Renderer>(true))
                .Where(r => Visible(r) && r.transform != trayT && r.bounds.max.y < 2.2f).Select(World).Where(m => m != null).ToList();
            var shelfName = "Dock_TrayShelf";
            var trayHome = World(trayT.GetComponent<Renderer>());
            var trayAtP = Moved(trayHome, Matrix4x4.Translate(Vector3.up * PlaceAbove));
            var envTrayHome = env.Concat(new[] { trayHome }).ToList();
            List<WMesh> RobotNow(Func<Renderer, bool> keep) => robot.GetComponentsInChildren<Renderer>(true).Where(r => Visible(r) && keep(r)).Select(World).Where(m => m != null).ToList();
            bool IsRJaw(Renderer r) => r.name.StartsWith("Arm_R_Jaw");
            bool IsLower(Renderer r) => r.name.StartsWith("Arm_R_JawLower");
            PoseRest(); var robotRest = RobotNow(r => true);
            PoseCarry(); var robotGrip = RobotNow(r => true);
            var carryTrayMtx = Matrix4x4.TRS(hold.position, hold.rotation, Vector3.one) * Matrix4x4.TRS(trayHomePos, trayHomeRot, Vector3.one).inverse;
            var trayCarried = Moved(trayHome, carryTrayMtx);
            var trayVsRobot = Min(new List<WMesh> { trayCarried }, RobotNow(r => !IsRJaw(r)), 0.05f);
            var loaded = robotGrip.Concat(new[] { trayCarried }).ToList();

            List<WMesh> At(List<WMesh> ms, Vector3 p) => ms.Select(m => Moved(m, Matrix4x4.Translate(p - H))).ToList();
            (float d, string pair) Near(List<WMesh> mv, List<WMesh> obstacles, float cap)
            {
                var all = mv[0].b; foreach (var m in mv) all.Encapsulate(m.b); all.Expand(0.1f);
                var r = Min(mv, obstacles.Where(o => o.b.Intersects(all)).ToList(), cap);
                return (r.d, r.a + " ↔ " + r.b);
            }
            (float d, string pair) Leg(List<WMesh> moving, Vector3 from, Vector3 to, List<WMesh> obstacles, float skipEnd = 0f)
            {
                float best = 0.05f; string pair = "-";
                float len = Vector3.Distance(from, to); int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.01f));
                for (int k = 0; k <= n; k++)
                {
                    var p = Vector3.Lerp(from, to, k / (float)n);
                    if (skipEnd > 0f && Vector3.Distance(p, to) < skipEnd) continue;
                    var r = Near(At(moving, p), obstacles, best);
                    if (r.d < best) { best = r.d; pair = r.pair; }
                }
                return (best, pair);
            }

            // ---- A0：审计握法本身（静态）：右爪 ↔ 托盘。托盘是单一网格，按托盘本地 X 拆成“盘体”（|x| ≤ 133 mm）和“两端提手”。
            WMesh TrayPart(bool handles)
            {
                var src = trayT.GetComponent<MeshFilter>().sharedMesh; var lv = src.vertices; var tri = src.triangles;
                var keep = new List<int>();
                for (int i = 0; i < tri.Length; i += 3)
                {
                    float cx = (lv[tri[i]].x + lv[tri[i + 1]].x + lv[tri[i + 2]].x) / 3f;
                    if ((Mathf.Abs(cx) > 0.133f) == handles) { keep.Add(tri[i]); keep.Add(tri[i + 1]); keep.Add(tri[i + 2]); }
                }
                var mtx = Matrix4x4.TRS(hold.position, hold.rotation, Vector3.one) * Matrix4x4.Scale(trayT.lossyScale);
                var v = lv.Select(x => mtx.MultiplyPoint3x4(x)).ToArray();
                var b = new Bounds(v[keep[0]], Vector3.zero); foreach (var k in keep) b.Encapsulate(v[k]);
                return new WMesh { name = handles ? "Dock_PartsTray（提手）" : "Dock_PartsTray（盘体）", v = v, tri = keep.ToArray(), b = b };
            }
            var trayBodyInHand = TrayPart(false); var trayHandlesInHand = TrayPart(true);
            var trayUp = (hold.rotation * Vector3.forward).normalized;                 // 托盘本地 Z = 向上
            var gripStatic = new StringBuilder();
            foreach (var part in RobotNow(r => r.name.StartsWith("Arm_R_Jaw")))
            {
                float dBody = Distance(part, trayBodyInHand, 0.05f), dHandle = Distance(part, trayHandlesInHand, 0.05f);
                string depth = "—";
                if (dBody <= 0f)
                    for (float o = 0.001f; o <= 0.04f; o += 0.001f)                   // 盘体沿托盘本地 −Z 移多少才与这个爪分开 ≈ 穿进盘壁的深度
                        if (Distance(part, Moved(trayBodyInHand, Matrix4x4.Translate(-trayUp * o)), 0.05f) > 0f) { depth = $"{o * 1000:F0} mm"; break; }
                gripStatic.AppendLine($"| {part.name} | {dHandle * 1000:F1} mm | {dBody * 1000:F1} mm | {depth} |");
            }
            var gripCentreW = rb[3].TransformPoint(TwoNightGripRollProbe.GripCenterR);
            var trayX = (hold.rotation * Vector3.right).normalized;
            var outward = Vector3.Dot(gripCentreW - hold.position, trayX) >= 0f ? trayX : -trayX;   // 从盘中心指向被握提手
            var jaws = RobotNow(r => r.name.StartsWith("Arm_R_Jaw") || r.name.StartsWith("Arm_R_Gripper") || r.name.StartsWith("Arm_R_Hinge"));
            float NeedShift(Vector3 dir)
            {
                for (float o = 0f; o <= 0.06f; o += 0.001f)
                    if (Min(jaws, new List<WMesh> { Moved(trayBodyInHand, Matrix4x4.Translate(dir * o)) }, 0.05f).d >= GripGoal) return o;
                return float.NaN;
            }
            float needOut = NeedShift(-outward), needDown = NeedShift(-trayUp);   // 盘体相对提手“往里 / 往下”= 提手相对盘体“往外 / 往上”

            // ---- A：放盘位 P：张开上爪、沿某方向滑出。逐个零件看间隙曲线：
            //   开始就贴着 / 穿进托盘的零件（夹住横杆的爪、压在盘壁上的下爪）必须在滑出途中分开到 ≥ 2 mm，之后不再靠近到 2 mm 以内；
            //   开始时离托盘 ≥ 2 mm 的零件全程保持 ≥ 2 mm；整个七号 ↔ 环境全程 ≥ 2 mm；
            //   放手位 = 所有“开始贴着”的零件都分开的那一档；之后托盘回到原位，七号 ↔ 原位托盘也要 ≥ 2 mm。
            var robotAtPGrip = Near(At(RobotNow(r => true), P), env, 0.05f);   // 夹住、带盘到 P：七号 ↔ 环境（不含托盘）
            var armR = new HashSet<string>(rb[0].GetComponentsInChildren<Renderer>(true).Select(r => r.name));
            var jawProfile = new StringBuilder(); float jawOpenMin = 0.05f; string jawOpenPair = "-";
            PoseCarry(); var gripParts = RobotNow(r => armR.Contains(r.name));
            var startD = gripParts.ToDictionary(m => m.name, m => Distance(Moved(m, Matrix4x4.Translate(P - H)), trayAtP, 0.05f));
            for (int k = 1; k <= 5; k++)
            {
                PoseCarry(1f, k / 5f);
                foreach (var m in RobotNow(r => armR.Contains(r.name)))
                {
                    float d = Distance(Moved(m, Matrix4x4.Translate(P - H)), trayAtP, 0.05f);
                    if (startD[m.name] >= GripGoal && d < jawOpenMin) { jawOpenMin = d; jawOpenPair = m.name + " ↔ Dock_PartsTray"; }
                    if (k == 5 && m.name.StartsWith("Arm_R_Jaw")) jawProfile.Append($"{m.name} {startD[m.name] * 1000:F1} → {d * 1000:F1} mm；");
                }
            }
            PoseCarry(1f, 1f);
            var openParts = RobotNow(r => armR.Contains(r.name)); var openAll = RobotNow(r => true);
            var openStart = openParts.ToDictionary(m => m.name, m => Distance(Moved(m, Matrix4x4.Translate(P - H)), trayAtP, 0.05f));
            PoseCarry();
            var bar = (hold.rotation * Vector3.up).normalized; bar.y = 0f; bar.Normalize();       // 横杆方向（水平分量）
            var right = body.right; right.y = 0f; right.Normalize();
            var fwd = body.forward; fwd.y = 0f; fwd.Normalize();
            var dirs = new List<(string name, Vector3 v)> { ("沿横杆 +", bar), ("沿横杆 −", -bar), ("右", right), ("左", -right), ("前", fwd), ("后", -fwd),
                                                            ("右前", (right + fwd).normalized), ("右后", (right - fwd).normalized), ("左前", (-right + fwd).normalized), ("左后", (-right - fwd).normalized),
                                                            ("右下", (right - Vector3.up * 0.3f).normalized) };   // 不试向上的方向：下爪还在横杆下面，往上走会把盘一起带起来
            var outH = outward; outH.y = 0f; outH.Normalize();
            dirs.Add(("外（离开盘中心）", outH)); dirs.Add(("外后", (outH - fwd).normalized)); dirs.Add(("外前", (outH + fwd).normalized));
            (string name, Vector3 v, float score, float release, string pair) best = ("-", right, -1f, 0f, "-");
            var exitTable = new StringBuilder(); var bestMin = new Dictionary<string, float>();
            const float step = 0.004f, maxSlide = 0.10f;
            foreach (var (n, v) in dirs)
            {
                var cleared = new Dictionary<string, float>();      // 开始贴着的零件：第一次 ≥ 2 mm 的滑出距离
                float fresh = 0.05f, envMin = 0.05f, after = 0.05f, release = -1f; string freshPair = "-", envPair = "-", afterPair = "-", why = "";
                int touchSteps = 0; var touchParts = new HashSet<string>();
                var stuck = openStart.Where(x => x.Value < GripGoal).Select(x => x.Key).ToList();
                for (float s = 0f; s <= maxSlide + 1e-4f; s += step)
                {
                    var p = P + v * s;
                    var mv = openParts.Select(m => Moved(m, Matrix4x4.Translate(p - H))).ToList();
                    foreach (var m in mv)
                    {
                        float d = Distance(m, trayAtP, 0.05f);
                        if (openStart[m.name] >= GripGoal) { if (d < fresh) { fresh = d; freshPair = m.name + " ↔ 托盘"; } if (d < GripGoal) { touchSteps++; touchParts.Add(m.name); } }
                        else if (!cleared.ContainsKey(m.name)) { if (d >= GripGoal) cleared[m.name] = s; }
                        else if (d < GripGoal && why == "") why = $"{m.name} 在 {s * 1000:F0} mm 又碰回托盘";
                    }
                    var e = Near(At(openAll, p), env, envMin);
                    if (e.d < envMin) { envMin = e.d; envPair = e.pair; }
                    if (e.d < GripGoal) { touchSteps++; touchParts.Add(e.pair); }
                    if (release < 0f && stuck.All(cleared.ContainsKey)) release = s;
                    if (release >= 0f)
                    {
                        var h = Near(At(openAll, p), new List<WMesh> { trayHome }, after);
                        if (h.d < after) { after = h.d; afterPair = h.pair + "（原位）"; }
                        if (h.d < GripGoal) { touchSteps++; touchParts.Add(h.pair + "（原位）"); }
                    }
                }
                if (release < 0f) why = "滑出 " + (maxSlide * 1000).ToString("F0") + " mm 仍未分开：" + string.Join("、", stuck.Where(x => !cleared.ContainsKey(x)));
                // 排序：先看“新增接触”的总长度（档数 × 4 mm），再看最小间隙；贴着的爪分不开或又碰回的方向不用
                float score = why != "" ? -1f : 1000f - touchSteps + Mathf.Min(fresh, Mathf.Min(envMin, after));
                string worst = new[] { (fresh, freshPair), (envMin, envPair), (after, afterPair) }.OrderBy(x => x.Item1).First().Item2;
                exitTable.AppendLine($"| {n} | {(release < 0f ? "—" : (release * 1000).ToString("F0") + " mm")} | {fresh * 1000:F1} mm | {envMin * 1000:F1} mm | {(release < 0f ? "—" : (after * 1000).ToString("F1") + " mm")} | {(why != "" ? why : (touchSteps == 0 ? worst : $"新增接触共 {touchSteps * step * 1000:F0} mm 路程：{string.Join("、", touchParts)}"))} |");
                if (score > best.score) best = (n, v, score, release, touchSteps == 0 ? worst : $"新增接触 {touchSteps * step * 1000:F0} mm 路程：{string.Join("、", touchParts)}");
                bestMin[n] = Mathf.Min(fresh, Mathf.Min(envMin, after));
            }
            var R = P + best.v * Mathf.Max(best.release, step);
            var W = P + best.v * Mathf.Max(best.release + 0.02f, 0.06f);

            // ---- B：搬运高度
            var armNames = new HashSet<string>(rb[0].GetComponentsInChildren<Renderer>(true).Concat(lb[0].GetComponentsInChildren<Renderer>(true)).Select(r => r.name));
            bool Seat(string n) => n.Contains("RootSeat") || n.Contains("RootRing") || n.Contains("RootBolts");
            List<(string leg, float d, string pair, float goal)> legs = null; float dhChosen = 0f;
            PoseCarry(1f, 1f); var robotOpenAll = RobotNow(r => true); PoseCarry();
            for (float dh = 0.06f; dh <= 0.451f; dh += 0.03f)
            {
                var T1 = H + Vector3.up * dh;
                var T2 = new Vector3(P.x, T1.y, P.z);
                var WU = new Vector3(W.x, T1.y, W.z);
                var l = new List<(string, float, string, float)>();
                void Add(string name, (float d, string pair) r, float goal) => l.Add((name, r.d, r.pair, goal));
                float leanMin = 0.05f; string leanPair = "-";
                var pivot = body.position + (T1 - H);
                for (float deg = 0f; deg <= leanDeg + 1.01f; deg += 0.5f)
                {
                    var q = Quaternion.AngleAxis(deg, homeRot * Vector3.forward);
                    var m = Matrix4x4.TRS(pivot, q, Vector3.one) * Matrix4x4.Translate(-pivot) * Matrix4x4.Translate(T1 - H);
                    var r = Near(loaded.Select(x => Moved(x, m)).ToList(), env, leanMin);
                    if (r.d < leanMin) { leanMin = r.d; leanPair = r.pair; }
                }
                Add($"开场带盘停在 T1，左倾 0–{leanDeg + 1:F0}°（比演出多 1°）", (leanMin, leanPair), ClearanceGoal);
                Add("带盘：T1 平移到托盘架上方 T2", Leg(loaded, T1, T2, env), ClearanceGoal);
                Add($"带盘：T2 下降到放盘位 P（托盘离原位 {PlaceAbove * 1000:F0} mm；最后 20 mm 托盘进托盘架凹槽，托盘 ↔ 托盘架不计）", Leg(loaded, T2, P, env, 0.02f), ClearanceGoal);
                Add("夹住：七号（含右爪）↔ 环境，从 T2 下降到 P 全程（不含托盘）", Leg(robotGrip, T2, P, env), GripGoal);
                Add("在 P 张开上爪：原来离托盘 ≥ 2 mm 的右手零件不碰托盘", (jawOpenMin, jawOpenPair), GripGoal);
                Add($"上爪开、沿“{best.name}”滑出 {Vector3.Distance(P, W) * 1000:F0} mm（贴着托盘的爪都分开后才放手；新接触 / 环境 / 放手后的原位托盘）", (bestMin.TryGetValue(best.name, out var bm) ? bm : 0f, best.pair), GripGoal);
                Add("空手：W 升到搬运高度", Leg(robotOpenAll, W, WU, envTrayHome), ClearanceGoal);
                Add("空手：平移回 T1", Leg(robotOpenAll, WU, T1, envTrayHome), ClearanceGoal);
                float blendMin = 0.05f; string blendPair = "-";
                for (int k = 0; k <= 5; k++)
                {
                    PoseCarry(k / 5f, 1f);
                    var arms = At(RobotNow(r => armNames.Contains(r.name) && !Seat(r.name)), T1);
                    var others = At(RobotNow(r => !armNames.Contains(r.name) && !Seat(r.name)), T1);
                    var r1 = Min(arms, others.Concat(env).ToList(), blendMin);
                    if (r1.d < blendMin) { blendMin = r1.d; blendPair = r1.a + " ↔ " + r1.b; }
                }
                PoseCarry();
                Add("两臂交还 Animator（权重 1 → 0，在 T1；不计根环与根座的铰接面）", (blendMin, blendPair), ClearanceGoal);
                Add("空手、两臂原姿态：T1 竖直落回悬停位 H（在维修座导板里，同正常落座路径）", Leg(robotRest, T1, H, env), GuideGoal);
                legs = l; dhChosen = dh;
                if (l.Where(x => !x.Item1.StartsWith("两臂交还") && !x.Item1.StartsWith("在 P") && !x.Item1.StartsWith("上爪开")).All(x => x.Item2 >= x.Item4)) break;   // 只按与高度有关的段选 dh（两臂过渡 9.0 mm 是静止姿态本身的值；放盘那两段在 P，与 dh 无关）
            }

            md.AppendLine("# 第一晚端盘演出：路径与静态间隙（编辑器构建时检查）\n");
            md.AppendLine($"- 生成时间 {DateTime.Now:yyyy-MM-dd HH:mm}；场景 `{RobotPath}`（布局 B 副本）。坐标为 Unity 世界坐标（米）。");
            md.AppendLine($"- 右臂端盘姿态：美术审计 0c1c906 `tray_handoff.md` 第 3 节，关节（相对 Idle_Hover 第 0 帧，绕骨骼本地 X）{string.Join(" / ", CarryJoints.Select(x => x.ToString("F2")))}°，夹爪开度 {GripJawT:F3}（Closed→Half）。审计的挂点原样使用（不滚转腕部）。");
            md.AppendLine("- **收窄动作**：不从托盘架上夹起托盘。实测审计握法在托盘原位时下爪压进托盘架边沿 3.3 mm（`grip_vs_shelf.md`），换握角（`grip_roll_probe.md`）也不行。所以七号一开场就端着盘（第一帧渲染前就位），放回时把盘降到离原位 " + (PlaceAbove * 1000).ToString("F0") + " mm、上爪张开、下爪滑出横杆，托盘落下最后这段。真正的取 / 放要补件（见 README「美术最小修正」）。");
            md.AppendLine("- 左臂：收纳姿态 = 现有片段 `Arm_Deploy_L` 第 0 帧（不新建动画）。原因：右手在托盘架上放盘时，静止姿态的左夹爪正好压在托盘架上。");
            md.AppendLine($"- 托盘挂点 `…/Arm_R_Wrist/TwoNight_TrayHoldPoint（右手端盘挂点）`，本地位置 {hold.localPosition:F4}、本地旋转 {hold.localRotation.x:F4}, {hold.localRotation.y:F4}, {hold.localRotation.z:F4}, {hold.localRotation.w:F4}；`Dock_PartsTray.DockPickable.holdPoint` 已设为它。挂点与托盘原位朝向相差 {rotErr:F2}°（落下时过渡、PutBack 恢复原位姿）。");
            md.AppendLine($"- 悬停位 H {H:F4}；放盘位 P {P:F4}（相对 H {(P - H) * 1000:F0} mm）；放手位 R = P + {best.name} {Mathf.Max(best.release, step) * 1000:F0} mm；退出终点 W = P + {Vector3.Distance(P, W) * 1000:F0} mm；搬运高度 dh = **{dhChosen * 1000:F0} mm**（满足阈值的最小一档；都不满足时是最后一档）。");
            md.AppendLine($"- 托盘（在手上）↔ 七号（不含右爪）：{trayVsRobot.d * 1000:F1} mm（{trayVsRobot.a} ↔ {trayVsRobot.b}），手臂姿态固定，演出全程不变。");
            md.AppendLine($"- 在 P 张开上爪（夹住 → 张开，各爪 ↔ 托盘）：{jawProfile}");
            md.AppendLine($"- 带盘夹住停在 P：七号 ↔ 环境 {robotAtPGrip.d * 1000:F1} mm（{robotAtPGrip.pair}）。\n");
            md.AppendLine("**审计握法本身（静态，托盘在手里）**：右爪各零件 ↔ 托盘。托盘是单一网格，这里按托盘本地 X 拆成两端提手和盘体（|x| ≤ 133 mm）。“穿进深度”= 盘体沿托盘向下移多少毫米才与该零件分开。\n\n| 零件 | ↔ 提手 | ↔ 盘体 | 穿进盘体深度 |\n|---|---|---|---|");
            md.Append(gripStatic);
            md.AppendLine($"\n- 右手（两爪 + 爪架 + 铰销）离盘体 ≥ 2 mm 需要：提手横杆整体再往外伸 **{needOut * 1000:F0} mm**，或再抬高 **{needDown * 1000:F0} mm**（只动提手，盘体不动；用移动盘体网格求出，精度 1 mm）。\n");
            md.AppendLine("上爪张开后，各滑出方向（每 4 mm 一档，最多 " + (maxSlide * 1000).ToString("F0") + " mm）。“新接触”= 开始时离托盘 ≥ 2 mm 的零件途中最近；开始就贴着的爪要分开到 ≥ 2 mm 且不再碰回：\n\n| 方向 | 放手位（贴着的爪都分开） | 新接触最近 | 七号 ↔ 环境 | 放手后 ↔ 原位托盘 | 最紧处 / 不行的原因 |\n|---|---|---|---|---|---|");
            md.Append(exitTable);
            md.AppendLine();
            md.AppendLine("| 段 | 最小间隙 | 阈值 | 最近的两件 |\n|---|---|---|---|");
            foreach (var l in legs) md.AppendLine($"| {l.leg} | {(l.d >= 0.05f ? "≥ 50" : (l.d * 1000).ToString("F1"))} mm | {l.goal * 1000:F0} mm{(l.d < l.goal ? " ⚠ 未达到" : "")} | {l.pair} |");
            md.AppendLine("\n两臂过渡的 9.0 mm（左肩叉 ↔ 机身挂点）是静止姿态本身的值（美术审计已记），过渡中不变小。“≥ 50 mm”表示 50 mm 内没有任何网格。这是刚体平移的静态检查；真实播放时的逐帧间隙见 `incident_motion_play.md`。");

            PoseRest();
            robot.SetPositionAndRotation(homePos, homeRot);
            var t1 = H + Vector3.up * dhChosen;
            return new IncidentPath { hover = H, hoverRot = homeRot, overDock = t1, overShelf = new Vector3(P.x, t1.y, P.z), place = P, release = R, withdraw = W, withdrawUp = new Vector3(W.x, t1.y, W.z) };
        }

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
