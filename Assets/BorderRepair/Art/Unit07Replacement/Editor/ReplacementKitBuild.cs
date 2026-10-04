using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.FirstOrder;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static BorderRepair.TwoNight.MeshClearance;

namespace BorderRepair.Art.Unit07Replacement.EditorTools
{
    /// <summary>
    /// 左引擎替换补件：导入 → 贴图 / 材质 → 预制体 → 隔离验证场景（布局 B 副本）→ 检查与渲染。
    /// 只写 Assets/BorderRepair/Art/Unit07Replacement/ 与 ArtSource/Unit07Replacement/；RobotV4、维修座、工作台、共享材质、发布场景只读。
    /// 显示切换只在本脚本的“检查”里临时做（不保存），程序的拆装逻辑不在这里实现。
    /// </summary>
    public static class ReplacementKitBuild
    {
        public const string Root = "Assets/BorderRepair/Art/Unit07Replacement";
        const string SrcFbx = "ArtSource/Unit07Replacement/Export/UNIT07_ReplacementKit_Cables.fbx";
        const string ModelPath = Root + "/Models/UNIT07_ReplacementKit_Cables.fbx";
        const string TexDir = Root + "/Textures", MatDir = Root + "/Materials", PrefabDir = Root + "/Prefabs";
        public const string VerifyScene = Root + "/Verification/ReplacementKit_Verify.unity";
        const string SourceScene = "Assets/BorderRepair/FirstOrder/Scenes/LayoutAB/Unit07FirstOrder_LayoutB.unity";
        const string Reports = "ArtSource/Unit07Replacement/Reports";
        const string RobotFbx = "Assets/RobotV4/Model/robot-final.fbx";
        public static readonly string[] Cables = { "ExternalCable_L", "AuxCable_L" };
        const string ClipName = "ExternalCable_Clip_L";

        [MenuItem("Border Repair/Unit07 Replacement Kit/Build All")]
        public static void BuildAll()
        {
            Import(); Materials(); Prefabs(); BuildScene(); Verify();
            AssetDatabase.SaveAssets();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ 导入

        static void Import()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ModelPath));
            File.Copy(SrcFbx, ModelPath, true);
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = false;
            mi.importAnimation = false; mi.importCameras = false; mi.importLights = false; mi.animationType = ModelImporterAnimationType.None;
            mi.isReadable = false; mi.meshCompression = ModelImporterMeshCompression.Off; mi.weldVertices = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription; mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Engine"), AssetDatabase.LoadAssetAtPath<Material>("Assets/RobotV4/Materials/M_Engine.mat"));
            mi.SaveAndReimport();
        }

        // ------------------------------------------------------------------ 新件贴图与材质（原材质只复制，不改）

        static void Materials()
        {
            Directory.CreateDirectory(TexDir); Directory.CreateDirectory(MatDir);
            foreach (var (src, tex, srcMat, dstMat) in new[] {
                ("Assets/RobotV4/Model/textures/T_Engine_BaseColor.png", "T_Engine_BaseColor_ReplacementL", "Assets/RobotV4/Materials/M_Engine.mat", "M_Engine_ReplacementL"),
                ("Assets/RobotV4/Model/textures/T_Internal_BaseColor.png", "T_Internal_BaseColor_ReplacementL", "Assets/RobotV4/Materials/M_Internal.mat", "M_Internal_ReplacementL") })
            {
                var dst = $"{TexDir}/{tex}.png";
                File.Copy($"ArtSource/Unit07Replacement/Textures/{tex}.png", dst, true);
                AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
                var si = (TextureImporter)AssetImporter.GetAtPath(src); var di = (TextureImporter)AssetImporter.GetAtPath(dst);
                var ts = new TextureImporterSettings(); si.ReadTextureSettings(ts); di.SetTextureSettings(ts);
                di.SetPlatformTextureSettings(si.GetDefaultPlatformTextureSettings());
                di.SaveAndReimport();
                var mp = $"{MatDir}/{dstMat}.mat";
                AssetDatabase.DeleteAsset(mp);
                AssetDatabase.CopyAsset(srcMat, mp);
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
                var orig = AssetDatabase.LoadAssetAtPath<Material>(srcMat);
                foreach (var prop in new[] { "_BaseMap", "_MainTex" })
                    if (m.HasProperty(prop) && orig.GetTexture(prop) != null && AssetDatabase.GetAssetPath(orig.GetTexture(prop)) == src) m.SetTexture(prop, t);
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ 预制体：Kit 根（放在原插头的本地位姿上）→ 网格与挂点

        static void Prefabs()
        {
            Directory.CreateDirectory(PrefabDir);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var kit in inst.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Kit_")).ToList())
            {
                var go = UnityEngine.Object.Instantiate(kit.gameObject); go.name = kit.name;
                foreach (var c in go.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Calib_")).ToList()) UnityEngine.Object.DestroyImmediate(c.gameObject);
                go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
                PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{kit.name}.prefab");
                UnityEngine.Object.DestroyImmediate(go);
            }
            UnityEngine.Object.DestroyImmediate(inst);
        }

        // ------------------------------------------------------------------ 隔离验证场景

        static Transform Under(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        static void BuildScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(VerifyScene));
            AssetDatabase.DeleteAsset(VerifyScene);
            AssetDatabase.CopyAsset(SourceScene, VerifyScene);
            var scene = EditorSceneManager.OpenScene(VerifyScene, OpenSceneMode.Single);
            var robot = GameObject.Find("UNIT07_RobotV4_DockReady").transform;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            foreach (var c in Cables)
                foreach (var (kitName, refName) in new[] { ($"Kit_{c}_Unplugged", $"{c}_PlugEngine"), ($"Kit_{c}_BodySocketFixed", $"{c}_PlugBody") })
                {
                    var refT = Under(robot, refName);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{kitName}.prefab"), scene);
                    go.transform.SetParent(refT.parent, false);
                    go.transform.localPosition = refT.localPosition; go.transform.localRotation = refT.localRotation; go.transform.localScale = refT.localScale;
                    go.SetActive(false);   // 默认是“连接”状态：补件不显示（程序按显示表切换）
                }
            // 标定用：模型里的 Calib_* 原样放到同一位姿（只在验证场景，不进预制体），用来证明“补件本地坐标 = 原物体本地坐标”
            var cal = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            PrefabUtility.UnpackPrefabInstance(cal, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            cal.name = "_Calibration（不进预制体）";
            foreach (var kit in cal.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Kit_")).ToList())
            {
                string c = Cables.First(x => kit.name.Contains(x));
                var refT = Under(robot, kit.name.EndsWith("_Unplugged") ? $"{c}_PlugEngine" : $"{c}_PlugBody");
                var holder = new GameObject("Calib_" + kit.name).transform;
                holder.SetParent(refT.parent, false);
                holder.localPosition = refT.localPosition; holder.localRotation = refT.localRotation; holder.localScale = refT.localScale;
                kit.SetParent(holder, false); kit.localPosition = Vector3.zero; kit.localRotation = Quaternion.identity; kit.localScale = Vector3.one;
                foreach (var t in kit.GetComponentsInChildren<Transform>(true).Where(t => !t.name.StartsWith("Calib_") && t != kit).ToList()) UnityEngine.Object.DestroyImmediate(t.gameObject);
                holder.gameObject.SetActive(false);
            }
            UnityEngine.Object.DestroyImmediate(cal);
            EditorSceneManager.SaveScene(scene);
        }

        // ------------------------------------------------------------------ 检查

        enum State { Connected, Unplugged, EngineRemoved, Reconnected }

        class Ctx
        {
            public Transform robot, hinge, body;
            public Dictionary<string, GameObject> unplug = new Dictionary<string, GameObject>(), socket = new Dictionary<string, GameObject>();
            public List<Renderer> engineGroup;      // 随旧总成走的原网格（Engine_L_Hinge 下，不含连接键 / 螺栓）
            public Renderer[] all;
        }

        static void SetState(Ctx x, State s)
        {
            bool conn = s == State.Connected || s == State.Reconnected;
            foreach (var c in Cables)
            {
                Under(x.robot, c).GetComponent<Renderer>().enabled = conn;
                Under(x.robot, c + "_PlugBody").GetComponent<Renderer>().enabled = conn;
                x.socket[c].SetActive(!conn);
                x.unplug[c].SetActive(s == State.Unplugged);
            }
            Under(x.robot, ClipName).GetComponent<Renderer>().enabled = conn;
            foreach (var r in x.engineGroup) r.enabled = s != State.EngineRemoved;
        }

        static string Mm(float d) => d >= 0.05f ? "≥ 50" : (d * 1000f).ToString("F1");

        static List<WMesh> Meshes(IEnumerable<Renderer> rs) => rs.Where(r => r != null && r.enabled && r.gameObject.activeInHierarchy).Select(World).Where(m => m != null).ToList();

        static void Verify()
        {
            var scene = EditorSceneManager.OpenScene(VerifyScene, OpenSceneMode.Single);
            var x = new Ctx();
            x.robot = GameObject.Find("UNIT07_RobotV4_DockReady").transform;
            x.hinge = Under(x.robot, "Engine_L_Hinge"); x.body = Under(x.robot, "Body");
            foreach (var c in Cables)
            {
                x.unplug[c] = x.hinge.GetComponentsInChildren<Transform>(true).First(t => t.name == $"Kit_{c}_Unplugged").gameObject;
                x.socket[c] = x.body.GetComponentsInChildren<Transform>(true).First(t => t.name == $"Kit_{c}_BodySocketFixed" && t.parent == Under(x.robot, c + "_PlugBody").parent).gameObject;
            }
            var kitRs = new HashSet<Renderer>(x.unplug.Values.Concat(x.socket.Values).SelectMany(g => g.GetComponentsInChildren<Renderer>(true)));
            var calibRs = new HashSet<Renderer>(Resources.FindObjectsOfTypeAll<Renderer>().Where(r => r.gameObject.scene == scene && r.name.StartsWith("Calib_")));
            x.engineGroup = x.hinge.GetComponentsInChildren<Renderer>(true).Where(r => !kitRs.Contains(r) && !calibRs.Contains(r) && r.name != "ConnectionKey_L" && r.name != "ConnectionKey_Bolt_L").ToList();
            var md = new StringBuilder("# 左引擎替换补件 · Unity 隔离验证\n\n");
            md.AppendLine($"- {DateTime.Now:yyyy-MM-dd HH:mm}，Unity {Application.unityVersion}，场景 `{VerifyScene}`（布局 B `{SourceScene}` 的副本；编辑模式，检查时的显示切换不保存）。");
            md.AppendLine("- 测距：集成线修正后的 MeshClearance（双向点到三角形 + 双面 Möller–Trumbore 边穿三角形，0 = 接触或穿插）；不算边到边最近距离、判不出共面重叠，2–5 mm 级数字是估计。\n");

            // 1) 标定：补件本地坐标 = 原物体本地坐标
            md.AppendLine("## 1. 坐标标定（补件里的原插头完整复制 vs 场景里的原插头）\n\n| 复制件 | 对应原物体 | 顶点最大偏差 |\n|---|---|---|");
            foreach (var holder in Resources.FindObjectsOfTypeAll<Transform>().Where(t => t.gameObject.scene == scene && t.name.StartsWith("Calib_Kit_")))
            {
                holder.gameObject.SetActive(true);
                foreach (var mf in holder.GetComponentsInChildren<MeshFilter>(true))
                {
                    var origName = mf.name.Substring("Calib_".Length);
                    var orig = Under(x.robot, origName).GetComponent<MeshFilter>();
                    var a = mf.sharedMesh.vertices.Select(v => mf.transform.TransformPoint(v)).ToArray();
                    var b = orig.sharedMesh.vertices.Select(v => orig.transform.TransformPoint(v)).ToArray();
                    float err = a.Max(p => b.Min(q => (p - q).magnitude));      // 每个复制顶点到最近原顶点（顶点顺序可能不同）
                    md.AppendLine($"| {mf.name} | {origName} | {err * 1000:F3} mm |");
                }
                holder.gameObject.SetActive(false);
            }

            // 2) 各姿态 × 状态
            var poses = new List<(string name, Action apply)> { ("编辑默认（布局 B 存档姿态）", () => { }) };
            var clips = AssetDatabase.LoadAllAssetsAtPath(RobotFbx).OfType<AnimationClip>().ToList();
            var seated = clips.First(c => c.name == "Pose_Gripper_Closed"); var idle = clips.First(c => c.name == "Idle_Hover");
            poses.Add(("落座（Pose_Gripper_Closed，维修座拔线时的姿态）", () => seated.SampleAnimation(x.robot.gameObject, 0f)));
            for (int k = 0; k < 4; k++) { float t = idle.length * k / 4f; poses.Add(($"悬停 Idle_Hover t={t:F2}s（引擎摆动）", () => idle.SampleAnimation(x.robot.gameObject, t))); }
            md.AppendLine("\n## 2. 显示状态与间距\n");
            md.AppendLine("| 姿态 | 断开线缆（含自由插头 / 卡箍）↔ 七号其余 | 最近的两件 | 两根断开线缆之间 | 固定插座补件 ↔ 机身（原插头同项基线） | 引擎端：断开线缆 ↔ 原线引擎端偏差 |\n|---|---|---|---|---|---|");
            var robotRs = x.robot.GetComponentsInChildren<Renderer>(true).Where(r => !kitRs.Contains(r) && !calibRs.Contains(r)).ToList();
            foreach (var (pname, apply) in poses)
            {
                apply();
                SetState(x, State.Connected);
                // 原线引擎端：烘焙蒙皮线，取离引擎端插头 12 mm 内的顶点
                float endDev = 0f;
                var baseline = new Dictionary<string, float>();
                foreach (var c in Cables)
                {
                    var pe = Under(x.robot, c + "_PlugEngine");
                    var orig = World(Under(x.robot, c).GetComponent<Renderer>());
                    var near = orig.v.Where(v => (v - pe.position).magnitude < 0.012f).ToList();
                    var kitCable = World(x.unplug[c].GetComponentsInChildren<Renderer>(true).First(r => r.name == c + "_Unplugged"));
                    if (near.Count > 0) endDev = Mathf.Max(endDev, near.Max(p => kitCable.v.Min(q => (p - q).magnitude)));
                    var pb = Under(x.robot, c + "_PlugBody").GetComponent<Renderer>();
                    baseline[c] = Min(Meshes(new[] { pb }), Meshes(robotRs.Where(r => r != pb && !Cables.Contains(r.name) && r.name != ClipName)), 0.05f).d;
                }
                SetState(x, State.Unplugged);
                var visibleRobot = robotRs.Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
                float worst = 0.05f; string pair = "-";
                foreach (var c in Cables)
                {
                    var own = Under(x.robot, c + "_PlugEngine").GetComponent<Renderer>();
                    var cableR = x.unplug[c].GetComponentsInChildren<Renderer>(true).First(r => r.name == c + "_Unplugged");
                    var otherKit = x.unplug[c].GetComponentsInChildren<Renderer>(true).Where(r => r != cableR).ToList();
                    // 线身：不计自己那个引擎端插头（连接处本来就接着）；自由插头 / 卡箍：七号全部
                    var r1 = Min(Meshes(new[] { cableR }), Meshes(visibleRobot.Where(r => r != own)), 0.05f);
                    var r2 = Min(Meshes(otherKit), Meshes(visibleRobot), 0.05f);
                    foreach (var r in new[] { r1, r2 }) if (r.d < worst) { worst = r.d; pair = r.a + " ↔ " + r.b; }
                }
                var between = Min(Meshes(x.unplug[Cables[0]].GetComponentsInChildren<Renderer>()), Meshes(x.unplug[Cables[1]].GetComponentsInChildren<Renderer>()), 0.05f);
                var sockTxt = string.Join("；", Cables.Select(c =>
                {
                    var r = Min(Meshes(x.socket[c].GetComponentsInChildren<Renderer>()), Meshes(visibleRobot), 0.05f);
                    return $"{c}: {Mm(r.d)}（{r.a} ↔ {r.b}；原插头基线 {Mm(baseline[c])}）";
                }));
                md.AppendLine($"| {pname} | {Mm(worst)} mm | {pair} | {Mm(between.d)} mm | {sockTxt} | {endDev * 1000:F2} mm |");
                SetState(x, State.Connected);
            }

            // 3) 显示表核对（每个状态里每个角色只出现一次）
            seated.SampleAnimation(x.robot.gameObject, 0f);
            md.AppendLine("\n## 3. 显示核对（落座姿态；数“看得见的”对象）\n\n| 状态 | 黄线 | 灰线 | 黄线机身端（原插头 / 固定插座） | 灰线机身端 | 自由插头 | 卡箍 | 引擎件 |\n|---|---|---|---|---|---|---|---|");
            foreach (State s in Enum.GetValues(typeof(State)))
            {
                SetState(x, s);
                bool Vis(Renderer r) => r != null && r.enabled && r.gameObject.activeInHierarchy;
                int Count(IEnumerable<Renderer> rs) => rs.Count(Vis);
                string Line(string c) => $"{Count(new[] { Under(x.robot, c).GetComponent<Renderer>() }.Concat(x.unplug[c].GetComponentsInChildren<Renderer>(true).Where(r => r.name == c + "_Unplugged")))}";
                string End(string c) => $"{Count(new[] { Under(x.robot, c + "_PlugBody").GetComponent<Renderer>() })} / {Count(x.socket[c].GetComponentsInChildren<Renderer>(true).Where(r => r.name.EndsWith("_SocketFlange")))}";
                int free = Count(x.unplug.Values.SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).Where(r => r.name.EndsWith("_FreePlug")));
                int clip = Count(new[] { Under(x.robot, ClipName).GetComponent<Renderer>() }.Concat(x.unplug[Cables[0]].GetComponentsInChildren<Renderer>(true).Where(r => r.name.EndsWith("_Clip_Unplugged"))));
                md.AppendLine($"| {s} | {Line(Cables[0])} 根 | {Line(Cables[1])} 根 | {End(Cables[0])} | {End(Cables[1])} | {free} | {clip} | {Count(x.engineGroup)} / {x.engineGroup.Count} |");
            }
            md.AppendLine("\n（EngineRemoved 是“机身一侧”：旧总成连同断开线缆已经搬走，这里把它们隐藏；Reconnected 用原线和原插头，引擎件换新材质。）");

            // 4) 旧总成在工作台上的摆放（审计 engine_path：绕竖直轴转 90°，平移到操作垫）
            SetState(x, State.Unplugged);
            var take = x.engineGroup.Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            var tb = take[0].bounds; foreach (var r in take) tb.Encapsulate(r.bounds);
            var startC = new Vector3(0.26708f, 1.1368f, 0.46697f); var delta = new Vector3(-0.19708f, -0.0781f, -0.81697f);
            var land = Matrix4x4.Translate(delta) * Matrix4x4.TRS(startC, Quaternion.AngleAxis(90f, Vector3.up), Vector3.one) * Matrix4x4.Translate(-startC);
            var benchRs = GameObject.Find("WorkbenchArea").GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            var kitMoved = x.unplug.Values.SelectMany(g => g.GetComponentsInChildren<Renderer>()).Select(r => Moved(World(r), land)).ToList();
            var groupMoved = Meshes(take).Select(m => Moved(m, land)).ToList();
            var bench = Min(kitMoved, Meshes(benchRs), 0.05f);
            var benchG = Min(groupMoved, Meshes(benchRs), 0.05f);
            md.AppendLine($"\n## 4. 旧总成暂存（工作台）\n\n- 引擎件（落座姿态）包围盒中心 {tb.center:F4}，审计记录的起点 {startC:F4}（相差 {(tb.center - startC).magnitude * 1000:F1} mm；这里按“编辑默认姿态”的审计值摆放，落座姿态略有不同时按程序实际路线为准）。");
            md.AppendLine($"- 断开线缆（随旧总成）↔ 工作台：**{Mm(bench.d)} mm**（{bench.a} ↔ {bench.b}）；旧引擎件 ↔ 工作台 {Mm(benchG.d)} mm（{benchG.a} ↔ {benchG.b}）。");

            // 5) 镜头遮挡：在游戏镜头和审计 E01b 机位，补件表面有多少采样点能直接看到（临时 MeshCollider，射线到采样点，不穿透）
            seated.SampleAnimation(x.robot.gameObject, 0f);
            SetState(x, State.Unplugged);
            md.AppendLine("\n## 5. 镜头可见性（落座、拔下状态；采样顶点直接可见的比例，射线只打可见网格）\n\n| 镜头 | 黄线固定插座 | 黄线自由插头 | 灰线固定插座 | 灰线自由插头 |\n|---|---|---|---|---|");
            var temp = new List<GameObject>();
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy && !calibRs.Contains(r)))
            {
                Mesh m = null;
                if (r is SkinnedMeshRenderer sk) { m = new Mesh(); sk.BakeMesh(m, true); }
                else if (r.TryGetComponent<MeshFilter>(out var mf)) m = mf.sharedMesh;
                if (m == null) continue;
                var go = new GameObject("_tmpcol"); go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                go.transform.localScale = r is SkinnedMeshRenderer ? Vector3.one : r.transform.lossyScale;
                go.AddComponent<MeshCollider>().sharedMesh = m; temp.Add(go);
                go.name = "_tmpcol:" + r.GetInstanceID();
            }
            Physics.SyncTransforms();
            var rig = UnityEngine.Object.FindFirstObjectByType<FirstOrderCameraRig>();
            var socketPos = Under(x.robot, "ExternalCable_L_PlugBody").position;
            var cams = new List<(string, Vector3, Vector3)>();
            if (rig != null) foreach (var id in FirstOrderCameraRig.Order) { var sh = rig.Get(id); if (sh.pose != null) cams.Add((FirstOrderCameraRig.Labels[id], sh.pose.position, sh.pose.forward)); }
            var bodyFrame = x.body;
            var e01b = socketPos + bodyFrame.TransformDirection(new Vector3(-0.22f, 0.30f, -0.30f).normalized) * 0.5f;   // 审计 E01b：插座中心 + 七号左 0.22、后 0.30、上 0.30（Body 本地 −X / −Z / +Y）
            e01b = socketPos + bodyFrame.TransformVector(new Vector3(-0.22f, 0.30f, -0.30f));
            cams.Add(("审计 E01b 机位（寻位参考）", e01b, (socketPos - e01b).normalized));
            foreach (var (label, pos, fwd) in cams)
            {
                string Frac(IEnumerable<Renderer> rs)
                {
                    int n = 0, seen = 0;
                    foreach (var r in rs)
                    {
                        var w = World(r); var ids = new HashSet<int>(new[] { r.GetInstanceID() });
                        for (int i = 0; i < w.v.Length; i += Math.Max(1, w.v.Length / 60))
                        {
                            var p = w.v[i]; var dir = p - pos; float dist = dir.magnitude;
                            if (Vector3.Dot(dir.normalized, fwd) < Mathf.Cos(35f * Mathf.Deg2Rad)) continue;    // 不在画面里（约 70° 视锥）
                            n++;
                            if (!Physics.Raycast(pos, dir / dist, out var hit, dist - 0.0015f) || hit.collider.name == "_tmpcol:" + r.GetInstanceID()) seen++;
                        }
                    }
                    return n == 0 ? "不在画面" : $"{100f * seen / n:F0}%";
                }
                Renderer[] Socket(string c) => x.socket[c].GetComponentsInChildren<Renderer>().Where(r => r.name.EndsWith("_SocketFlange")).ToArray();
                Renderer[] Free(string c) => x.unplug[c].GetComponentsInChildren<Renderer>().Where(r => r.name.EndsWith("_FreePlug")).ToArray();
                md.AppendLine($"| {label} | {Frac(Socket(Cables[0]))} | {Frac(Free(Cables[0]))} | {Frac(Socket(Cables[1]))} | {Frac(Free(Cables[1]))} |");
            }
            foreach (var g in temp) UnityEngine.Object.DestroyImmediate(g);

            // 6) 材质映射（左引擎件 → 新件材质）
            md.AppendLine("\n## 6. 逐对象材质映射（新左引擎；右引擎与机身不变）\n\n| 对象（Engine_L_Hinge 下） | 原材质 | 新件材质 | 说明 |\n|---|---|---|---|");
            var flow = UnityEngine.Object.FindFirstObjectByType<FirstOrderFlow>();
            var faultOnly = new HashSet<Renderer>();
            if (flow != null) { foreach (var r in flow.ClogLayers) faultOnly.Add(r); if (flow.CoverLabel != null) faultOnly.Add(flow.CoverLabel); }
            var worn = flow != null ? flow.Bearing.GetComponentsInChildren<Renderer>(true).Where(r => r != flow.OriginalBearingRenderer).ToList() : new List<Renderer>();
            var newEng = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Engine_ReplacementL.mat"); var newInt = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Internal_ReplacementL.mat");
            var map = new StringBuilder("object,path,source_materials,replacement_materials,note\n");
            foreach (var r in x.hinge.GetComponentsInChildren<Renderer>(true).Where(r => !kitRs.Contains(r) && !calibRs.Contains(r)))
            {
                var src = r.sharedMaterials.Select(m => m != null ? m.name : "(空)").ToArray();
                string note; string[] dst;
                if (faultOnly.Contains(r)) { note = r == flow.CoverLabel ? "旧保养贴纸：随旧上盖留在旧总成，新件不显示" : "故障包积尘 / 纤维：只属于旧件，新件不显示"; dst = new[] { "（不显示）" }; }
                else if (worn.Contains(r)) { note = "磨损轴承（故障件）：新件不显示"; dst = new[] { "（不显示）" }; }
                else if (r.name == "ConnectionKey_L" || r.name == "ConnectionKey_Bolt_L") { note = "连接键 / 螺栓留在机身（不随总成），材质不变"; dst = src; }
                else
                {
                    dst = r.sharedMaterials.Select(m => m == null ? "(空)" : m.name == "M_Engine" ? newEng.name : m.name == "M_Internal" ? newInt.name : m.name).ToArray();
                    note = r == (flow != null ? flow.OriginalBearingRenderer : null) ? "正常轴承：新件显示" : (dst.SequenceEqual(src) ? "非 M_Engine / M_Internal，保持原材质" : "");
                }
                string path = PathOf(r.transform, x.robot);
                md.AppendLine($"| {r.name} | {string.Join(", ", src)} | {string.Join(", ", dst)} | {note} |");
                map.AppendLine($"{r.name},{path},{string.Join("|", src)},{string.Join("|", dst)},{note}");
            }
            foreach (var c in Cables)
            {
                var r = Under(x.robot, c).GetComponent<Renderer>();
                md.AppendLine($"| {c}（机器人根下，蒙皮线） | {r.sharedMaterial.name} | {newEng.name} | 新引擎重新连接时显示的线（原线复用）；断开线缆补件仍用原 M_Engine（随旧总成） |");
                map.AppendLine($"{c},{PathOf(r.transform, x.robot)},{r.sharedMaterial.name},{newEng.name},新引擎重新连接时的线（原线复用）");
            }
            // 7) 挂点（从预制体读回：相对 Kit 根 = 相对原插头物体的本地坐标）
            var anchors = new StringBuilder("kit,anchor,runtime_parent,kit_root_local_pose,anchor_local_pos_mm,anchor_local_rot_xyzw\n");
            foreach (var c in Cables)
                foreach (var (kit, refName, parentBone) in new[] { (x.unplug[c], $"{c}_PlugEngine", "Engine_L_Hinge"), (x.socket[c], $"{c}_PlugBody", "Body") })
                    foreach (var a in kit.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Anchor_")))
                    {
                        var lp = kit.transform.InverseTransformPoint(a.position) * 1000f; var lr = Quaternion.Inverse(kit.transform.rotation) * a.rotation;
                        anchors.AppendLine($"{kit.name},{a.name},{parentBone},同 {refName} 的 localPosition/localRotation/localScale,\"{lp.x:F3} {lp.y:F3} {lp.z:F3}\",\"{lr.x:F6} {lr.y:F6} {lr.z:F6} {lr.w:F6}\"");
                    }
            Directory.CreateDirectory(Reports);
            File.WriteAllText(Path.Combine(Reports, "anchors.csv"), anchors.ToString(), new UTF8Encoding(true));
            Render(x, newEng, newInt, seated);
            File.WriteAllText(Path.Combine(Reports, "material_map.csv"), map.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(Reports, "unity_verify.md"), md.ToString(), new UTF8Encoding(false));
            Debug.Log("[ReplacementKit] verify 已写出");
        }

        /// <summary>Unity 相机渲染（场景自带灯光；不读桌面）：各状态 × 游戏镜头，外加新旧引擎同机位对照。</summary>
        static void Render(Ctx x, Material newEng, Material newInt, AnimationClip seated)
        {
            const string Out = "ArtSource/Unit07Replacement/Renders/Unity";
            Directory.CreateDirectory(Out);
            seated.SampleAnimation(x.robot.gameObject, 0f);
            var rig = UnityEngine.Object.FindFirstObjectByType<FirstOrderCameraRig>();
            var cam = new GameObject("_ShotCam").AddComponent<Camera>();
            var rt = new RenderTexture(1600, 900, 24); cam.targetTexture = rt; cam.nearClipPlane = 0.02f;
            void Shot(string file)
            {
                cam.Render(); RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(Out, file), tex.EncodeToPNG()); RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(tex);
            }
            void FromRig(string id) { var s = rig.Get(id); cam.transform.SetPositionAndRotation(s.pose.position, s.pose.rotation); cam.fieldOfView = s.fov > 1f ? s.fov : 40f; }
            var sock = Under(x.robot, "ExternalCable_L_PlugBody").position; var sockAux = Under(x.robot, "AuxCable_L_PlugBody").position;
            void Close(bool rear)
            {
                var c = rear ? sockAux : sock;
                var off = x.body.TransformVector(rear ? new Vector3(-0.20f, -0.02f, -0.34f) : new Vector3(-0.20f, -0.02f, 0.34f));   // Body −X = 七号左；+Z 前 / −Z 后
                cam.transform.position = c + off + Vector3.down * 0.03f; cam.transform.LookAt(c + Vector3.down * 0.03f); cam.fieldOfView = 30f;
            }
            var origMats = x.engineGroup.ToDictionary(r => r, r => r.sharedMaterials);
            var cableMats = Cables.ToDictionary(c => c, c => Under(x.robot, c).GetComponent<Renderer>().sharedMaterial);
            void NewMaterials(bool on)
            {
                foreach (var r in x.engineGroup)
                    r.sharedMaterials = on ? origMats[r].Select(m => m == null ? m : m.name == "M_Engine" ? newEng : m.name == "M_Internal" ? newInt : m).ToArray() : origMats[r];
                foreach (var c in Cables) Under(x.robot, c).GetComponent<Renderer>().sharedMaterial = on ? newEng : cableMats[c];
            }
            var flow = UnityEngine.Object.FindFirstObjectByType<FirstOrderFlow>();
            var faultRs = new List<Renderer>();
            if (flow != null) { faultRs.AddRange(flow.ClogLayers); if (flow.CoverLabel != null) faultRs.Add(flow.CoverLabel); faultRs.AddRange(flow.Bearing.GetComponentsInChildren<Renderer>(true).Where(r => r != flow.OriginalBearingRenderer)); }
            foreach (State s in Enum.GetValues(typeof(State)))
            {
                SetState(x, s);
                bool fresh = s == State.Reconnected;
                NewMaterials(fresh);
                foreach (var r in faultRs) r.enabled = !fresh && s != State.EngineRemoved;
                if (flow != null && flow.OriginalBearingRenderer != null) flow.OriginalBearingRenderer.enabled = fresh;
                Close(false); Shot($"U_{(int)s + 1}_{s}_close_front.png");
                Close(true); Shot($"U_{(int)s + 1}_{s}_close_rear.png");
                if (rig != null) { FromRig(FirstOrderCameraRig.EngineL); Shot($"U_{(int)s + 1}_{s}_game_EngineL.png"); FromRig(FirstOrderCameraRig.EngineRear); Shot($"U_{(int)s + 1}_{s}_game_EngineRear.png"); }
            }
            // 新旧引擎同机位同灯光（连接状态，旧件带故障包；新件不显示故障包、显示正常轴承）
            SetState(x, State.Connected);
            if (rig != null)
            {
                FromRig(FirstOrderCameraRig.Overview); var p = cam.transform.position;
                cam.transform.position = Under(x.robot, "Engine_L_Hinge").position + x.body.TransformVector(new Vector3(-0.55f, 0.25f, 0.45f));
                cam.transform.LookAt(Under(x.robot, "Engine_L_Hinge").position + x.body.TransformVector(new Vector3(-0.17f, -0.03f, 0f))); cam.fieldOfView = 32f;
                NewMaterials(false); foreach (var r in faultRs) r.enabled = true; if (flow?.OriginalBearingRenderer != null) flow.OriginalBearingRenderer.enabled = false;
                Shot("M_old_engine_scene_light.png");
                NewMaterials(true); foreach (var r in faultRs) r.enabled = false; if (flow?.OriginalBearingRenderer != null) flow.OriginalBearingRenderer.enabled = true;
                Shot("M_new_engine_scene_light.png");
                NewMaterials(false);
            }
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
        }

        static string PathOf(Transform t, Transform stop)
        {
            var n = new List<string>(); for (; t != null && t != stop.parent; t = t.parent) n.Add(t.name); n.Reverse(); return string.Join("/", n);
        }
    }
}
