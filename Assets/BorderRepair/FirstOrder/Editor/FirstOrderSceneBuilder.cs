using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Dock;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace BorderRepair.FirstOrder.EditorTools
{
    /// <summary>
    /// 七号首单可玩原型 · 独立测试场景构建。只用已有预制体（RobotV4 停靠预制体、维修座、工作台），不改它们的资源和源文件：
    /// 点选代理、遮挡碰撞、落点、镜头都只加在场景实例上。工作台自带的义肢占位件（WB_Placeholder）在本场景里关掉，腾出操作垫当零件落点。
    /// 命令行：-executeMethod BorderRepair.FirstOrder.EditorTools.FirstOrderSceneBuilder.Build
    /// </summary>
    public static class FirstOrderSceneBuilder
    {
        public const string Root = "Assets/BorderRepair/FirstOrder";
        public const string ScenePath = Root + "/Scenes/Unit07FirstOrder_Test.unity";
        public const string ArtDir = Root + "/Art";
        public const string ReportDir = FirstOrderAudit.OutDir;
        public static readonly Vector3 DockPosition = new Vector3(0f, 0f, 1.15f);
        public static readonly Quaternion DockRotation = Quaternion.Euler(0f, 180f, 0f);   // 维修座正面朝工作台
        const string EngineL = "Engine_L_Hinge", EngineR = "Engine_R_Hinge";

        static readonly StringBuilder Log = new StringBuilder();
        static void Note(string s) { Log.AppendLine(s); Debug.Log("[FirstOrderBuild] " + s); }

        [MenuItem("Border Repair/Unit07 First Order/Build Test Scene")]
        public static void Build()
        {
            Log.Clear();
            Directory.CreateDirectory(ArtDir);
            Directory.CreateDirectory(Root + "/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------------------------------------------------------- 1. 已有预制体
            var bench = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FirstOrderAudit.BenchPrefab));
            bench.transform.position = Vector3.zero;          // 保留预制体根节点的 FBX 轴向旋转
            Find(bench.transform, "WB_Placeholder").gameObject.SetActive(false);
            var dockGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FirstOrderAudit.DockPrefab));
            // 预制体根节点自带 FBX 轴向换算的旋转：在它外面再转 180°，不能直接覆盖
            dockGo.transform.SetPositionAndRotation(DockPosition, DockRotation * dockGo.transform.rotation);
            var anchor = Find(dockGo.transform, "Dock_RobotAnchor");
            var robot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FirstOrderAudit.RobotPrefab));
            robot.transform.SetPositionAndRotation(anchor.position, DockRotation * robot.transform.rotation);
            Note($"工作台 {FirstOrderAudit.BenchPrefab} 在原点（关掉 WB_Placeholder）；维修座 {FirstOrderAudit.DockPrefab} 在 {DockPosition}，转 180° 正面朝工作台；" +
                 $"七号 {FirstOrderAudit.RobotPrefab} 对齐 Dock_RobotAnchor {anchor.position:F3}");

            // ---------------------------------------------------------------- 2. 维修座流程（现有 Unit07DockController，不改）
            var dockCtl = new GameObject("Unit07DockFlow").AddComponent<Unit07DockController>();
            dockCtl.Configure(robot.transform, robot.GetComponent<Animator>(), robot.GetComponent<RotorPowerDriver>(), anchor,
                              Find(dockGo.transform, "Dock_Clamp_L"), Find(dockGo.transform, "Dock_Clamp_R"),
                              Find(dockGo.transform, "Dock_PowerSwitch_Lever"), Find(dockGo.transform, "Dock_PowerSwitch_Lamp").GetComponent<Renderer>());

            // ---------------------------------------------------------------- 3. 遮挡碰撞：七号与维修座的每个网格（场景实例上加，不改预制体资源）
            int occ = 0;
            foreach (var root in new[] { robot, dockGo })
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.GetComponent<Collider>() != null && !mf.GetComponent<Collider>().isTrigger) continue;
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    occ++;
                }
            Note($"遮挡碰撞：给七号和维修座的 {occ} 个网格加 MeshCollider（射线被机身挡住时，后面的部件点不到）");

            // ---------------------------------------------------------------- 4. 真实部件 → 首单部件
            var hingeL = Find(robot.transform, EngineL);
            var hingeR = Find(robot.transform, EngineR);
            string RPath(Transform t) => "Robot_V4（" + robot.name + "）/" + PathOf(t, robot.transform);
            FirstOrderPart Part(Transform t, string id, string label, params Transform[] members)
            {
                var p = t.gameObject.AddComponent<FirstOrderPart>();
                p.partId = id; p.displayName = label; p.realPath = RPath(t);
                p.members = members.ToList();
                foreach (var m in members) m.gameObject.AddComponent<FirstOrderMember>().owner = p;
                AddProxy(t.gameObject, members);
                return p;
            }
            var latchOuter = Part(Find(hingeL, "Engine_CoverLatch_Outer_L"), "engine_l_latch_outer", "左上盖外侧锁扣");
            var latchRear = Part(Find(hingeL, "Engine_CoverLatch_Rear_L"), "engine_l_latch_rear", "左上盖后侧锁扣");
            var cover = Part(Find(hingeL, "Engine_UpperCover_L"), "engine_l_cover", "左上盖总成（上盖 + 进气唇口 + 护栅 + 风道）",
                             Find(hingeL, "Engine_IntakeLip_L"), Find(hingeL, "Engine_IntakeGuard_L"), Find(hingeL, "Engine_IntakeDuct_L"));
            var bearingOld = Part(Find(hingeL, "Engine_BearingTop_L"), "engine_l_bearing", "左上轴承（故障件）");
            var rightEngine = Part(Find(hingeR, "Engine_UpperCover_R"), "engine_r", "右引擎上盖（正常，不拆）");
            Note("首单部件（真实网格）：" + string.Join("；", new[] { latchOuter, latchRear, cover, bearingOld, rightEngine }.Select(p => $"{p.partId} = {p.realPath}")));

            // ---------------------------------------------------------------- 5. 故障美术包（art/unit07-fault-kit）：挂在真实部件下，按美术包实测的本地位姿
            // 在遮挡碰撞之后加，美术件自己不带实体碰撞（原轴承、上盖、护栅的碰撞照旧负责遮挡和点选）
            var hingeUp = hingeL.up;
            var oldR = bearingOld.GetComponent<Renderer>();
            var worn = Mount(KitWorn, bearingOld.transform, WornPos);
            oldR.enabled = false;                                            // 原轴承渲染器关掉：由磨损件代替显示，不能两个叠在一起
            var wornMf = KitMesh(worn, "UNIT07_FK_BearingTop_L_Worn");
            Note($"磨损轴承：{KitWorn} 挂在 {RPath(bearingOld.transform)} 下；关掉原轴承 MeshRenderer（碰撞保留）。" +
                 $"顶点质心与原轴承差 {(Centroid(wornMf) - Centroid(bearingOld.GetComponent<MeshFilter>())).magnitude * 1000f:F2} mm");

            var guardT = Find(hingeL, "Engine_IntakeGuard_L");
            var clogRoot = new GameObject("FO_IntakeClog_L (故障美术包：进气口堵塞)").transform;
            clogRoot.SetParent(guardT, false);
            clogRoot.localPosition = ClogPos; clogRoot.localRotation = MountRot;
            var clogFbx = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(KitClog), clogRoot);
            clogFbx.transform.localPosition = Vector3.zero; clogFbx.transform.localRotation = Quaternion.identity; clogFbx.transform.localScale = Vector3.one;
            var clog = clogRoot.gameObject.AddComponent<FirstOrderPart>();
            clog.partId = "engine_l_intake_clog"; clog.displayName = "左进气口堵塞（积尘 + 纤维）";
            clog.realPath = KitClog + "（故障美术包）挂在 " + RPath(guardT);
            string[] layerOrder = { "Fibers", "GuardDust", "RimDust", "DustMat" };
            int LayerIndex(Renderer r)
            {
                var n = r.name + "|" + r.GetComponent<MeshFilter>()?.sharedMesh?.name;
                int i = Array.FindIndex(layerOrder, k => n.Contains(k));
                return i < 0 ? layerOrder.Length : i;
            }
            var clogLayers = clogFbx.GetComponentsInChildren<Renderer>(true).OrderBy(LayerIndex).ToArray();
            AddClogProxy(clog, clogLayers, guardT.GetComponent<MeshCollider>());
            Note($"进气口堵塞：{KitClog} 挂在 {RPath(guardT)} 下；清理时逐层隐藏：{string.Join(" → ", clogLayers.Select(r => r.GetComponent<MeshFilter>().sharedMesh.name))}");

            var coverT = cover.transform;
            var label = Mount(KitLabel, coverT, LabelPos);
            var labelR = label.GetComponentsInChildren<Renderer>(true).First();
            Note($"保养标记：{KitLabel} 挂在 {RPath(coverT)} 下（上盖内侧，翻盖后朝上可读）");

            // 新轴承：主对象的装配位姿 = 原轴承位姿（装上后与原轴承完全重合），美术件按挂载位姿挂在主对象下
            var box = Find(bench.transform, "Box_Bearings");
            var nbRoot = new GameObject("FO_NewBearing_L (故障美术包：新轴承)").transform;
            nbRoot.SetParent(bearingOld.transform, false);
            nbRoot.SetParent(null, true);                                    // 带上原轴承的世界缩放
            var nbFbx = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(KitNew), nbRoot);
            nbFbx.transform.localPosition = WornPos; nbFbx.transform.localRotation = MountRot; nbFbx.transform.localScale = Vector3.one;
            var newBearing = nbRoot.gameObject.AddComponent<FirstOrderPart>();
            newBearing.partId = "engine_l_bearing_new"; newBearing.displayName = "新轴承";
            newBearing.realPath = KitNew + "（故障美术包），放在 WorkbenchArea/" + PathOf(box, bench.transform) + " 上";
            AddProxy(nbRoot.gameObject, new Transform[0], nbFbx.GetComponentsInChildren<Renderer>(true));
            var newMf = KitMesh(nbFbx, "UNIT07_FK_BearingTop_L_New");
            Note($"新轴承：{KitNew}；装配位置与原轴承顶点质心差 {(Centroid(newMf) - Centroid(bearingOld.GetComponent<MeshFilter>())).magnitude * 1000f:F2} mm");
            // 平放：转轴竖直；位置：轴承盒盒体顶面（盒盖竖在后沿，盒子没有内腔），最低顶点贴着顶面
            var nbFlat = Flat(newMf, nbRoot.rotation);
            var (boxSpot, boxNote) = BoxBodyTop(box, FirstOrderGeometry.BoundsAt(FirstOrderGeometry.LocalOffsets(newBearing), nbFlat).size);
            nbRoot.SetPositionAndRotation(boxSpot + FirstOrderGeometry.RestingOffset(FirstOrderGeometry.LocalOffsets(newBearing), nbFlat, 0.0005f), nbFlat);
            Note($"新轴承摆放：平放在 {PathOf(box, bench.transform)} 盒体顶面 {boxSpot:F3}（{boxNote}）。轴承盒是实心块，没有内腔，放不进“盒里”");

            // ---------------------------------------------------------------- 6. 工作台落点（在真实工作台对象上算位置）
            var markMat = Mat("M_FO_DropMarker", new Color(0.95f, 0.78f, 0.25f), 0.8f);
            var mat = Find(bench.transform, "Bench_Mat").GetComponent<Renderer>().bounds;
            // 上盖总成翻过来放：内侧（保养标记）朝上，标记文字正对站在工作台前（+Z 一侧）的玩家
            var flip = Quaternion.AngleAxis(180f, Vector3.right);
            var flipUp = Quaternion.FromToRotation(flip * hingeUp, Vector3.down) * flip;
            var textUp = Vector3.ProjectOnPlane(flipUp * LabelTextUp(labelR), Vector3.up);
            var yaw = Quaternion.AngleAxis(Vector3.SignedAngle(textUp, Vector3.back, Vector3.up), Vector3.up);
            var coverFlat = yaw * flipUp * coverT.rotation;
            var coverOffsets = FirstOrderGeometry.LocalOffsets(cover);
            var coverSize = FirstOrderGeometry.BoundsAt(coverOffsets, coverFlat).size;
            var matZone = Zone("FO_Drop_Mat", "mat", "工作台操作垫", "WorkbenchArea/" + PathOf(Find(bench.transform, "Bench_Mat"), bench.transform), cover.partId,
                               FindFreeSpotOnMat(mat, coverSize), new Vector3(0.28f, 0.03f, 0.28f), 0.27f, markMat);
            matZone.orientPart = true; matZone.landingRotation = coverFlat; matZone.landingOffset = FirstOrderGeometry.RestingOffset(coverOffsets, coverFlat);
            // 旧轴承：平放进托盘。优先旧件托盘；托盘里原有的旧件已经占满时，退到螺钉托盘空着的前格（不挪动工作台原有物件）
            var oldFlat = Flat(wornMf, bearingOld.transform.rotation);
            var oldOffsets = FirstOrderGeometry.LocalOffsets(bearingOld);
            var oldSize = FirstOrderGeometry.BoundsAt(oldOffsets, oldFlat).size;
            Transform trayT = null; Vector3 traySpot = default; string trayLabel = null, why = "";
            foreach (var (name, lab) in new[] { ("Tray_OldParts", "旧件托盘"), ("Tray_Screws", "螺钉托盘前格") })
            {
                var t = Find(bench.transform, name);
                if (FindFreeSpotInTray(t, oldSize, out traySpot, out var blockers)) { trayT = t; trayLabel = lab; break; }
                why += $"{lab} 没有放得下 {oldSize.x * 1000:F0} mm 轴承的空位（被 {blockers} 占满）；";
            }
            if (trayT == null) throw new InvalidOperationException("工作台托盘里都放不下旧轴承：" + why);
            var trayZone = Zone("FO_Drop_PartsTray", "parts_tray", trayLabel, "WorkbenchArea/" + PathOf(trayT, bench.transform), bearingOld.partId,
                                traySpot, new Vector3(0.07f, 0.03f, 0.07f), 0.065f, markMat);
            trayZone.orientPart = true; trayZone.landingRotation = oldFlat; trayZone.landingOffset = FirstOrderGeometry.RestingOffset(oldOffsets, oldFlat);
            Note($"落点：上盖总成翻面（内侧朝上，占地 {coverSize.x * 1000:F0}×{coverSize.z * 1000:F0} mm、高 {coverSize.y * 1000:F0} mm）→ {matZone.benchObjectPath} {matZone.landing.position:F3}；" +
                 $"旧轴承平放 → {trayZone.benchObjectPath} 里的空位 {traySpot:F3}" +
                 (why.Length > 0 ? $"（{why}所以放到{trayLabel}）" : "（自动避开托盘里原有的物件）"));
            (bool use, Vector3 point) newBearingApproach;
            // 工作台一侧的进出路线（落点上方被台灯等挡住时低空水平进出），按精确网格规划
            foreach (var (z, part, what) in new[] { (matZone, (FirstOrderPart)cover, "上盖总成 → 操作垫"), (trayZone, bearingOld, "旧轴承 → 托盘") })
            {
                var (use, ap, note) = PlanBenchApproach(part, z.landing.position + z.landingOffset, z.landingRotation, bench.transform, robot.transform.position);
                z.useApproach = use; z.approachPoint = ap;
                Note($"进出路线 {what}：{note}");
            }
            {
                var (use, ap, note) = PlanBenchApproach(newBearing, nbRoot.position, nbRoot.rotation, bench.transform, robot.transform.position);
                newBearingApproach = (use, ap);
                Note($"进出路线 新轴承 ← 轴承盒：{note}");
            }

            // ---------------------------------------------------------------- 7. 灯光（沿用工作台的暖光 / 后处理，维修座区域补一盏顶灯）
            BuildLights(bench);

            // ---------------------------------------------------------------- 8. 镜头
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 20f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.05f, 0.06f, 0.055f);
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var rigGo = new GameObject("FirstOrderCameraRig");
            camGo.transform.SetParent(rigGo.transform, false);
            var engC = cover.WorldBounds().center;
            var engRC = rightEngine.WorldBounds().center;
            var shots = new List<FirstOrderCameraRig.Shot>
            {
                Shot(rigGo, FirstOrderCameraRig.Dock, new Vector3(0.95f, 1.35f, 0.55f), new Vector3(0.12f, 0.88f, 1.22f), 55f),   // 侧前方：七号落座后仍看得到夹具握把和断电开关
                Shot(rigGo, FirstOrderCameraRig.EngineL, engC + new Vector3(0.42f, 0.36f, -0.42f), engC + new Vector3(0f, -0.03f, 0f), 42f),
                Shot(rigGo, FirstOrderCameraRig.EngineRear, engC + new Vector3(0.40f, 0.34f, 0.52f), engC + new Vector3(0f, -0.04f, 0.05f), 42f),
                Shot(rigGo, FirstOrderCameraRig.Bench, new Vector3(0.0f, 1.62f, 0.62f), new Vector3(-0.15f, 0.92f, -0.42f), 52f),
                Shot(rigGo, FirstOrderCameraRig.Overview, new Vector3(1.40f, 2.05f, 0.30f), new Vector3(0.05f, 0.95f, 0.30f), 62f),
                Shot(rigGo, FirstOrderCameraRig.EngineR, engRC + new Vector3(-0.42f, 0.36f, -0.42f), engRC + new Vector3(0f, -0.03f, 0f), 42f),
            };
            // 保养记录：上盖翻面落到操作垫后，标记所在的位置（按落点位姿算）；新旧轴承对比：托盘落点和轴承盒之间
            var labelOnMat = matZone.landing.position + matZone.landingOffset + coverFlat * (Quaternion.Inverse(coverT.rotation) * (labelR.bounds.center - coverT.position));
            shots.Add(Shot(rigGo, FirstOrderCameraRig.Record, labelOnMat + new Vector3(0f, 0.19f, 0.08f), labelOnMat, 34f));
            var oldOnTray = trayZone.landing.position;
            var mid = (oldOnTray + newBearing.WorldBounds().center) / 2f;
            float span = Vector3.Distance(oldOnTray, newBearing.WorldBounds().center);
            shots.Add(Shot(rigGo, FirstOrderCameraRig.Compare, mid + new Vector3(0f, 0.16f + span * 0.55f, 0.12f + span * 0.45f), mid, 38f));
            // 左引擎近看：从斜上方看进气口（上盖装着时）和轴承位（上盖拆下后）
            var closeFocus = Vector3.Lerp(clogLayers.Last().bounds.center, wornMf.GetComponent<Renderer>().bounds.center, 0.5f);
            shots.Add(Shot(rigGo, FirstOrderCameraRig.EngineClose, closeFocus + new Vector3(0.11f, 0.25f, -0.11f), closeFocus, 36f));
            Note($"镜头：保养记录对准操作垫上翻面后的标记 {labelOnMat:F3}；新旧轴承对比对准托盘落点和轴承盒之间 {mid:F3}（两者相距 {span * 1000:F0} mm）");
            var rig = rigGo.AddComponent<FirstOrderCameraRig>();
            rig.Configure(cam, shots);
            camGo.transform.SetPositionAndRotation(shots[0].pose.position, shots[0].pose.rotation);
            cam.fieldOfView = shots[0].fov;

            // ---------------------------------------------------------------- 9. 流程与输入
            var carrier = new GameObject("FO_PartsCarrier (取下的零件挂在这里)").transform;
            var flowGo = new GameObject("FirstOrderFlow (七号首单原型：占位交互，非正式维修流程)");
            var flow = flowGo.AddComponent<FirstOrderFlow>();
            flow.Configure(dockCtl, rig, hingeL, hingeR, carrier, latchOuter, latchRear, cover, bearingOld, newBearing, rightEngine, matZone, trayZone);
            flow.ConfigureFaultKit(clog, clogLayers, oldR, labelR, labelR.GetComponent<MeshFilter>().sharedMesh.normals.Aggregate(Vector3.zero, (a, n) => a + n).normalized);
            flow.ConfigureNewBearingApproach(newBearingApproach.use, newBearingApproach.point);
            flowGo.AddComponent<FirstOrderInput>().Configure(flow);
            EditorUtility.SetDirty(flow);

            // 进气口堵塞的点选核对：左引擎镜头下，朝堵塞中心的射线按真实点选规则选中的是它（不是上盖总成）
            // （维修座集成留下的整块左引擎检查代理在开始检查后才关掉，这里临时关掉再核对）
            var inspectProxy = hingeL.GetComponentsInChildren<DockInteractable>(true).Where(d => d.action == DockAction.EngineLeft).Select(d => d.GetComponent<Collider>()).FirstOrDefault(c => c != null);
            if (inspectProxy != null) inspectProxy.enabled = false;
            Physics.SyncTransforms();
            var engPose = shots.First(s => s.id == FirstOrderCameraRig.EngineL).pose;
            var clogC = clogLayers.Last().bounds.center;
            var picked = FirstOrderInput.Pick(new Ray(engPose.position, clogC - engPose.position));
            if (inspectProxy != null) inspectProxy.enabled = true;
            Note($"点选核对：左引擎镜头朝进气口堵塞中心点 → {FirstOrderInput.NameOf(picked)}");
            if (picked != clog) Debug.LogWarning("[FirstOrderBuild] 左引擎镜头下点不中进气口堵塞");
            static int Tris(UnityEngine.Mesh m) => Enumerable.Range(0, m.subMeshCount).Sum(i => (int)m.GetIndexCount(i) / 3);
            var perKit = new[] { ("磨损轴承", worn), ("进气口堵塞", clogFbx), ("保养标记", label), ("新轴承", nbFbx) }
                .Select(x => (x.Item1, tris: x.Item2.GetComponentsInChildren<MeshFilter>(true).Sum(m => Tris(m.sharedMesh)),
                              detail: string.Join("+", x.Item2.GetComponentsInChildren<MeshFilter>(true).Select(m => $"{m.sharedMesh.name} {Tris(m.sharedMesh)}")))).ToList();
            Note($"故障美术包网格三角面（静态统计，Unity 导入后的索引数 / 3）：{string.Join("；", perKit.Select(k => $"{k.Item1} {k.tris}（{k.detail}）"))}；合计 {perKit.Sum(k => k.tris)}。" +
                 $"关掉的原轴承渲染器 {Tris(oldR.GetComponent<MeshFilter>().sharedMesh)} 面；去掉的占位圆柱（Unity 内置 Cylinder）{Tris(Resources.GetBuiltinResource<UnityEngine.Mesh>("Cylinder.fbx"))} 面");

            EditorSceneManager.SaveScene(scene, ScenePath);
            Note($"测试场景：{ScenePath}");
            Directory.CreateDirectory(ReportDir);
            File.WriteAllText(Path.Combine(ReportDir, "build_log.txt"), $"七号首单原型 · 场景构建记录（Unity {Application.unityVersion}，{DateTime.Now:yyyy-MM-dd HH:mm}）\n" + Log, new UTF8Encoding(false));
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------ 工具

        static FirstOrderCameraRig.Shot Shot(GameObject rig, string id, Vector3 pos, Vector3 target, float fov)
        {
            var t = new GameObject("CamPose_" + id).transform;
            t.SetParent(rig.transform, false);
            t.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
            return new FirstOrderCameraRig.Shot { id = id, pose = t, fov = fov };
        }

        // ------------------------------------------------------------------ 故障美术包

        const string KitDir = "Assets/BorderRepair/Art/Unit07FaultKit/Models/";
        public const string KitWorn = KitDir + "UNIT07_FK_BearingWorn.fbx", KitNew = KitDir + "UNIT07_FK_BearingNew.fbx",
                            KitClog = KitDir + "UNIT07_FK_IntakeClog.fbx", KitLabel = KitDir + "UNIT07_FK_CoverLabel.fbx";
        // 美术包 README 第 5 节 / unity_check 实测的挂载本地位姿（父对象在七号静态姿态下；四个资源旋转相同，缩放 1）
        static readonly Quaternion MountRot = new Quaternion(0.70700f, 0.05222f, -0.05196f, 0.70337f).normalized;
        static readonly Vector3 WornPos = new Vector3(0.00073f, -0.00003f, 0.00495f);     // 父：Engine_BearingTop_L（新轴承同）
        static readonly Vector3 ClogPos = new Vector3(-0.00088f, 0.00003f, -0.00593f);    // 父：Engine_IntakeGuard_L
        static readonly Vector3 LabelPos = new Vector3(0.06842f, -0.00006f, 0.01115f);    // 父：Engine_UpperCover_L

        static GameObject Mount(string fbx, Transform parent, Vector3 localPos)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx), parent);
            go.transform.localPosition = localPos; go.transform.localRotation = MountRot; go.transform.localScale = Vector3.one;
            return go;
        }

        /// <summary>单个对象的 FBX 导入后会合并到根上、名字变成文件名：同时按网格名找。</summary>
        static MeshFilter KitMesh(GameObject g, string name) =>
            g.GetComponentsInChildren<MeshFilter>(true).First(m => m.name == name || m.sharedMesh.name == name);

        static Vector3 Centroid(MeshFilter mf)
        {
            var vs = mf.sharedMesh.vertices;
            var s = Vector3.zero;
            foreach (var v in vs) s += mf.transform.TransformPoint(v);
            return s / vs.Length;
        }

        /// <summary>把主对象当前朝向 rootRot 转成“轴承转轴竖直”的平放朝向（转的角度最小）。</summary>
        static Quaternion Flat(MeshFilter ring, Quaternion rootRot)
        {
            var axis = FirstOrderGeometry.ThinAxisWorld(ring);
            if (axis.y < 0f) axis = -axis;
            return Quaternion.FromToRotation(axis, Vector3.up) * rootRot;
        }

        /// <summary>标记贴图里“文字向上”（UV 的 v 增大方向）在世界里的方向：取面积最大的三角形求 dP/dv。</summary>
        static Vector3 LabelTextUp(Renderer label)
        {
            var mf = label.GetComponent<MeshFilter>();
            var m = mf.sharedMesh;
            var vs = m.vertices; var uv = m.uv; var tri = m.triangles;
            int best = 0; float bestA = -1f;
            for (int i = 0; i < tri.Length; i += 3)
            {
                float a = Vector3.Cross(vs[tri[i + 1]] - vs[tri[i]], vs[tri[i + 2]] - vs[tri[i]]).sqrMagnitude;
                if (a > bestA) { bestA = a; best = i; }
            }
            Vector3 p0 = mf.transform.TransformPoint(vs[tri[best]]), e1 = mf.transform.TransformPoint(vs[tri[best + 1]]) - p0, e2 = mf.transform.TransformPoint(vs[tri[best + 2]]) - p0;
            Vector2 d1 = uv[tri[best + 1]] - uv[tri[best]], d2 = uv[tri[best + 2]] - uv[tri[best]];
            float det = d1.x * d2.y - d2.x * d1.y;
            return ((-d2.x * e1 + d1.x * e2) / det).normalized;
        }

        /// <summary>
        /// 轴承盒 Box_Bearings 盒体顶面上放得下轴承的位置：临时加精确碰撞，从上往下打一排射线，
        /// 数量最多的那一层高度是盒体顶面（盒盖竖在后沿，比顶面高）；取顶面区域的中心，再核对轴承一圈都落在顶面上。
        /// </summary>
        static (Vector3 spot, string note) BoxBodyTop(Transform box, Vector3 itemSize)
        {
            var mf = box.GetComponent<MeshFilter>();
            var tmp = new GameObject("TmpBoxCollider");
            tmp.transform.SetPositionAndRotation(box.position, box.rotation);
            tmp.transform.localScale = box.lossyScale;
            var mc = tmp.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            Physics.SyncTransforms();
            var b = box.GetComponent<Renderer>().bounds;
            var hits = new List<Vector3>();
            for (int i = 0; i <= 40; i++)
                for (int j = 0; j <= 40; j++)
                {
                    var o = new Vector3(Mathf.Lerp(b.min.x, b.max.x, i / 40f), b.max.y + 0.05f, Mathf.Lerp(b.min.z, b.max.z, j / 40f));
                    if (mc.Raycast(new Ray(o, Vector3.down), out var h, 1f)) hits.Add(h.point);
                }
            float top = hits.GroupBy(p => Mathf.Round(p.y * 1000f)).OrderByDescending(g => g.Count()).First().Key / 1000f;
            var flat = hits.Where(p => Mathf.Abs(p.y - top) < 0.0015f).ToList();
            var c = new Vector3(flat.Average(p => p.x), top, flat.Average(p => p.z));
            float r = Mathf.Max(itemSize.x, itemSize.z) / 2f;
            int onTop = 0, ring = 24;
            for (int k = 0; k < ring; k++)
            {
                var q = c + Quaternion.AngleAxis(k * 360f / ring, Vector3.up) * Vector3.right * r;
                if (mc.Raycast(new Ray(q + Vector3.up * 0.05f, Vector3.down), out var h, 1f) && Mathf.Abs(h.point.y - top) < 0.0015f) onTop++;
            }
            float lidTop = hits.Max(p => p.y);
            Object.DestroyImmediate(tmp);
            return (c, $"盒体顶面高 {top:F3} m，顶面区域 {(flat.Max(p => p.x) - flat.Min(p => p.x)) * 1000:F0}×{(flat.Max(p => p.z) - flat.Min(p => p.z)) * 1000:F0} mm；" +
                       $"轴承外圈 {ring} 个采样点中 {onTop} 个落在顶面上；盒盖最高 {lidTop:F3} m");
        }

        /// <summary>
        /// 进气口堵塞的点选代理：触发盒包住堵塞美术件（每边补 1 mm）。它套在上盖总成的点选盒里面；
        /// 点选规则把“套在前一个点选盒里、还没碰到实体表面的小点选盒”也算候选，取体积小的，所以点进气口选中的是堵塞。
        /// </summary>
        static void AddClogProxy(FirstOrderPart clog, Renderer[] layers, Collider guard)
        {
            var t = clog.transform;
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            foreach (var r in layers)
            {
                var mb = r.GetComponent<MeshFilter>().sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var v = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1));
                    var p = t.InverseTransformPoint(r.transform.TransformPoint(v));
                    lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
                }
            }
            var box = clog.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = (lo + hi) / 2f;
            box.size = hi - lo + Vector3.one * 0.002f;
            Physics.SyncTransforms();
            var gb = guard.bounds.size; var bb = box.bounds.size;
            Note($"进气口堵塞点选盒：世界包围盒 {bb.x * 1000:F0}×{bb.y * 1000:F0}×{bb.z * 1000:F0} mm" +
                 $"（护栅碰撞 {gb.x * 1000:F0}×{gb.y * 1000:F0}×{gb.z * 1000:F0} mm）");
        }

        /// <summary>点选代理：触发盒包住主对象和总成成员的网格（世界包围盒换算到主对象本地，每边补 2 mm、最小 20 mm）。extra：另外要包进去的渲染器（美术件）。</summary>
        static void AddProxy(GameObject go, Transform[] members, IEnumerable<Renderer> extra = null)
        {
            var rs = new List<Renderer> { go.GetComponent<Renderer>() };
            rs.AddRange(members.Select(m => m.GetComponent<Renderer>()));
            if (extra != null) rs.AddRange(extra);
            var t = go.transform;
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            foreach (var r in rs.Where(r => r != null))
            {
                var mb = r.GetComponent<MeshFilter>().sharedMesh.bounds;      // 网格本地包围盒：不需要 CPU 可读
                for (int i = 0; i < 8; i++)
                {
                    var v = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1));
                    var p = t.InverseTransformPoint(r.transform.TransformPoint(v));
                    lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
                }
            }
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = (lo + hi) / 2f;
            var size = hi - lo + Vector3.one * 0.004f;
            var ls = t.lossyScale;
            box.size = new Vector3(Mathf.Max(size.x, 0.02f / ls.x), Mathf.Max(size.y, 0.02f / ls.y), Mathf.Max(size.z, 0.02f / ls.z));
        }

        static FirstOrderDropZone Zone(string name, string id, string label, string benchPath, string accepts, Vector3 landing, Vector3 size, float markerDia, Material markMat)
        {
            var go = new GameObject(name);
            go.transform.position = landing + Vector3.up * size.y / 2f;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true; bc.size = size;
            var land = new GameObject("Landing").transform;
            land.SetParent(go.transform, false);
            land.position = landing;
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.name = "Marker";
            marker.transform.SetParent(go.transform, false);
            marker.transform.position = landing + Vector3.up * 0.0015f;
            marker.transform.localScale = new Vector3(markerDia, 0.001f, markerDia);
            marker.GetComponent<Renderer>().sharedMaterial = markMat;
            marker.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var z = go.AddComponent<FirstOrderDropZone>();
            z.zoneId = id; z.displayName = label; z.benchObjectPath = benchPath; z.acceptsPartId = accepts; z.landing = land;
            z.marker = marker.GetComponent<Renderer>();
            return z;
        }

        /// <summary>在操作垫上找一块放得下上盖总成的空地：避开垫上原有的托架等实体碰撞，从垫子中心向外找，四周留 10 mm。</summary>
        static Vector3 FindFreeSpotOnMat(Bounds mat, Vector3 itemSize)
        {
            Physics.SyncTransforms();
            var half = new Vector3(itemSize.x / 2f + 0.01f, itemSize.y / 2f, itemSize.z / 2f + 0.01f);
            var candidates = new List<Vector3>();
            for (float x = mat.min.x + half.x; x <= mat.max.x - half.x; x += 0.005f)
                for (float z = mat.min.z + half.z; z <= mat.max.z - half.z; z += 0.005f)
                    candidates.Add(new Vector3(x, mat.max.y, z));
            foreach (var c in candidates.OrderBy(c => (c - mat.center).sqrMagnitude))
                if (!Physics.CheckBox(c + Vector3.up * (half.y + 0.0015f), half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    return c;
            throw new InvalidOperationException("操作垫上找不到放得下上盖总成的空地");
        }

        /// <summary>
        /// 工作台一侧的进出路线（按精确网格核对，编辑器里做；Play 模式下工作台被静态合批、台灯没有碰撞，没法精确查）：
        /// 零件在摆放位姿下，先看落点正上方 1 m 的竖直通道是否空着；空着就直接竖直落下。
        /// 不空（例如上盖落点在台灯下面）就找一个“进场点”：从落点竖直抬到台灯下方的低空 → 水平移到进场点 → 从进场点竖直往上 1 m 都空着。
        /// 运行时零件在高处越过七号后先落到进场点，再水平钻到落点上方落下；从工作台取走时反过来走。
        /// </summary>
        public static (bool use, Vector3 approach, string note) PlanBenchApproach(FirstOrderPart p, Vector3 landPos, Quaternion landRot, Transform bench, Vector3 robotPos)
        {
            using var probe = new BenchProbe(p, landRot, bench);
            if (probe.Leg(landPos, landPos + Vector3.up * 1.0f, out var block))
                return (false, landPos, "落点正上方 1 m 竖直通道空着，直接竖直落下");
            // 能抬到的最高低空：从落点往上，直到碰到东西为止，再留 6 cm
            float maxLow = 0f;
            for (float y = 0.01f; y <= 1.0f; y += 0.01f)
            {
                if (!probe.Clear(landPos + Vector3.up * y)) break;
                maxLow = y;
            }
            float low = Mathf.Max(0.03f, maxLow - 0.06f);
            var toRobot = Vector3.ProjectOnPlane(robotPos - landPos, Vector3.up).normalized;
            foreach (var turn in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f })
                for (float d = 0.15f; d <= 0.601f; d += 0.05f)
                {
                    var lowPos = landPos + Vector3.up * low;
                    var a = lowPos + Quaternion.AngleAxis(turn, Vector3.up) * toRobot * d;
                    if (probe.Leg(landPos, lowPos, out _) && probe.Leg(lowPos, a, out _) && probe.Leg(a, a + Vector3.up * (1.0f - low), out _))
                        return (true, a, $"落点正上方被 {block} 挡住（竖直往上最多 {maxLow * 100:F0} cm）；改走：竖直抬 {low * 100:F0} cm → 朝七号方向偏 {turn:F0}° 水平移 {d * 100:F0} cm 到进场点 → 竖直往上 1 m 空着");
                }
            throw new InvalidOperationException($"{p.displayName}：工作台一侧找不到不穿过 {block} 的进出路线");
        }

        /// <summary>零件凸包放到任意位置、对工作台网格求穿入深度（工作台网格临时加精确碰撞，用完删掉）。</summary>
        sealed class BenchProbe : IDisposable
        {
            readonly List<(Transform t, Vector3 off, Quaternion rot)> hulls = new List<(Transform, Vector3, Quaternion)>();
            readonly List<(Collider c, string n)> env = new List<(Collider, string)>();
            readonly List<Object> temp = new List<Object>();

            public BenchProbe(FirstOrderPart p, Quaternion landRot, Transform bench)
            {
                var root = p.transform;
                var inv = Quaternion.Inverse(root.rotation);
                foreach (var r in p.Renderers().Where(r => r.enabled && r.GetComponent<MeshFilter>() != null))
                {
                    var go = new GameObject("TmpProbe_" + r.name);
                    go.transform.localScale = r.transform.lossyScale;
                    var c = go.AddComponent<MeshCollider>(); c.convex = true; c.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                    hulls.Add((go.transform, landRot * (inv * (r.transform.position - root.position)), landRot * inv * r.transform.rotation));
                    temp.Add(go);
                }
                foreach (var mf in bench.GetComponentsInChildren<MeshFilter>())
                {
                    var rr = mf.GetComponent<Renderer>();
                    if (rr == null || !rr.enabled || rr.bounds.max.y > 2.2f) continue;
                    var c = mf.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh;
                    env.Add((c, mf.name)); temp.Add(c);
                }
            }

            public bool Clear(Vector3 pos) => Blocker(pos) == null;

            public string Blocker(Vector3 pos)
            {
                foreach (var (t, off, rot) in hulls) t.SetPositionAndRotation(pos + off, rot);
                Physics.SyncTransforms();
                foreach (var (t, _, _) in hulls)
                {
                    var h = t.GetComponent<MeshCollider>();
                    var hb = h.bounds; hb.Expand(0.002f);
                    foreach (var (e, n) in env)
                        if (e.bounds.Intersects(hb) &&
                            Physics.ComputePenetration(h, t.position, t.rotation, e, e.transform.position, e.transform.rotation, out _, out var d) && d > 0.0005f)
                            return n;
                }
                return null;
            }

            /// <summary>从 a 到 b 每 1 cm 查一次（不含起点）；碰到东西就返回 false 和挡路的对象名。</summary>
            public bool Leg(Vector3 a, Vector3 b, out string blocker)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.01f));
                for (int i = 1; i <= n; i++)
                {
                    blocker = Blocker(Vector3.Lerp(a, b, i / (float)n));
                    if (blocker != null) return false;
                }
                blocker = null;
                return true;
            }

            public void Dispose() { foreach (var o in temp) Object.DestroyImmediate(o); }
        }

        /// <summary>在旧件托盘里找一个放得下轴承的空位：托盘与托盘内容临时加精确碰撞，逐点试放。</summary>
        static bool FindFreeSpotInTray(Transform tray, Vector3 itemSize, out Vector3 spot, out string blockers)
        {
            var temp = new List<Component>();
            var meshes = tray.GetComponentsInChildren<MeshFilter>(true).ToList();     // 托盘本身 + 跟着托盘走的内容
            foreach (var mf in meshes) temp.Add(mf.gameObject.AddComponent<MeshCollider>());
            foreach (var (mc, mf) in temp.Cast<MeshCollider>().Zip(meshes, (a, b) => (a, b))) mc.sharedMesh = mf.sharedMesh;
            Physics.SyncTransforms();
            var b = tray.GetComponent<Renderer>().bounds;
            float floor = b.min.y + 0.004f;                       // 托盘底板厚 4 mm
            var half = new Vector3(itemSize.x / 2f + 0.002f, itemSize.y / 2f, itemSize.z / 2f + 0.002f);   // 四周留 2 mm
            spot = Vector3.zero; bool found = false;
            for (float x = b.min.x + half.x + 0.003f; x <= b.max.x - half.x - 0.003f && !found; x += 0.0025f)
                for (float z = b.max.z - half.z - 0.003f; z >= b.min.z + half.z + 0.003f && !found; z -= 0.0025f)
                {
                    var c = new Vector3(x, floor + half.y + 0.0015f, z);
                    if (Physics.CheckBox(c, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    spot = new Vector3(x, floor, z); found = true;
                }
            var c0 = new Vector3(b.center.x, floor + half.y + 0.0015f, b.center.z);
            blockers = string.Join("、", Physics.OverlapBox(c0, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Select(x => x.name).Distinct());
            foreach (var c in temp) Object.DestroyImmediate(c);
            return found;
        }

        static Material Mat(string name, Color c, float emission)
        {
            string p = $"{ArtDir}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, p); }
            m.shader = shader;
            m.SetColor("_BaseColor", c);
            m.SetFloat("_SpecularHighlights", 0f);
            m.SetColor("_SpecColor", Color.black);
            m.DisableKeyword("_SPECULAR_COLOR");
            if (emission > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * emission); }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        static void BuildLights(GameObject bench)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.19f, 0.21f, 0.20f);
            RenderSettings.ambientEquatorColor = new Color(0.14f, 0.15f, 0.14f);
            RenderSettings.ambientGroundColor = new Color(0.07f, 0.07f, 0.065f);
            // 工作台暖色工作灯（与工作台测试场景一致）
            var bulb = Find(bench.transform, "Lamp_Bulb").GetComponent<Renderer>().bounds.center;
            var lampTarget = new Vector3(0.04f, 0.904f, -0.44f);
            var lamp = new GameObject("Bench_WorkLamp_Spot (warm)").AddComponent<Light>();
            lamp.type = LightType.Spot; lamp.color = new Color(1.0f, 0.74f, 0.46f); lamp.intensity = 2.3f; lamp.range = 1.7f;
            lamp.spotAngle = 82f; lamp.innerSpotAngle = 34f; lamp.shadows = LightShadows.Soft;
            lamp.transform.SetPositionAndRotation(bulb, Quaternion.LookRotation(lampTarget - bulb));
            // 维修座上方的检修灯（维修座区域原本没有灯）
            var dockLamp = new GameObject("Dock_ServiceLight (warm-neutral)").AddComponent<Light>();
            dockLamp.type = LightType.Spot; dockLamp.color = new Color(1.0f, 0.86f, 0.70f); dockLamp.intensity = 3.0f; dockLamp.range = 2.6f;
            dockLamp.spotAngle = 70f; dockLamp.innerSpotAngle = 35f; dockLamp.shadows = LightShadows.Soft;
            dockLamp.transform.SetPositionAndRotation(new Vector3(0.35f, 2.30f, 0.75f), Quaternion.LookRotation(new Vector3(0.15f, 1.0f, 1.15f) - new Vector3(0.35f, 2.30f, 0.75f)));
            var ceil = new GameObject("Ceiling_Fluo (cool)").AddComponent<Light>();
            ceil.type = LightType.Point; ceil.color = new Color(0.80f, 0.90f, 0.86f); ceil.intensity = 0.85f; ceil.range = 3.4f;
            ceil.transform.position = new Vector3(0f, 2.30f, -0.25f);
            var fill = new GameObject("Room_Fill (cool, no shadow)").AddComponent<Light>();
            fill.type = LightType.Directional; fill.color = new Color(0.72f, 0.80f, 0.78f); fill.intensity = 0.15f; fill.shadows = LightShadows.None;
            fill.transform.rotation = Quaternion.Euler(40f, 200f, 0f);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/WorkbenchArea/Art/WB_PostProfile.asset");
            if (profile != null)
            {
                var vol = new GameObject("PostProcess (shared WB profile)").AddComponent<Volume>();
                vol.isGlobal = true; vol.sharedProfile = profile;
            }
        }

        public static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var x = t; x != null && x != root; x = x.parent) parts.Insert(0, x.name);
            return string.Join("/", parts);
        }

        public static Transform Find(Transform root, string name)
        {
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
            if (t == null) throw new InvalidOperationException($"找不到对象 {name}");
            return t;
        }
    }
}
