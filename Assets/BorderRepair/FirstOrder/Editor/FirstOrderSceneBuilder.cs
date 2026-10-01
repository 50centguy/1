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

            // ---------------------------------------------------------------- 5. 新轴承：没有美术资产 → 同尺寸占位圆柱，放在工作台轴承盒上
            var phMat = Mat("M_FO_Placeholder", new Color(0.43f, 0.51f, 0.58f), 0f);
            var box = Find(bench.transform, "Box_Bearings");
            var oldR = bearingOld.GetComponent<Renderer>();
            var nb = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            nb.name = "FO_Placeholder_NewBearing (占位：无美术资产)";
            Object.DestroyImmediate(nb.GetComponent<Collider>());
            float dia = Mathf.Max(oldR.bounds.size.x, oldR.bounds.size.z), h = oldR.bounds.size.y;
            nb.transform.localScale = new Vector3(dia, h / 2f, dia);
            var boxTop = box.GetComponent<Renderer>().bounds;
            nb.transform.position = new Vector3(boxTop.center.x, boxTop.min.y + 0.05f + h / 2f + 0.001f, boxTop.center.z + 0.01f);   // 轴承盒主体顶面（高 50 mm）
            nb.GetComponent<Renderer>().sharedMaterial = phMat;
            nb.AddComponent<MeshCollider>().sharedMesh = nb.GetComponent<MeshFilter>().sharedMesh;
            var newBearing = nb.AddComponent<FirstOrderPart>();
            newBearing.partId = "engine_l_bearing_new"; newBearing.displayName = "新轴承"; newBearing.isPlaceholder = true;
            newBearing.realPath = "（无美术资产，程序生成的占位圆柱）放在 WorkbenchArea/" + PathOf(box, bench.transform) + " 上";
            AddProxy(nb, new Transform[0]);
            Note($"新轴承占位：直径 {dia * 1000:F0} mm、高 {h * 1000:F0} mm 的圆柱（照旧轴承包围盒），放在 {PathOf(box, bench.transform)} 上，名字带“占位”");

            // ---------------------------------------------------------------- 6. 工作台落点（在真实工作台对象上算位置）
            var markMat = Mat("M_FO_DropMarker", new Color(0.95f, 0.78f, 0.25f), 0.8f);
            var mat = Find(bench.transform, "Bench_Mat").GetComponent<Renderer>().bounds;
            var matZone = Zone("FO_Drop_Mat", "mat", "工作台操作垫", "WorkbenchArea/" + PathOf(Find(bench.transform, "Bench_Mat"), bench.transform), cover.partId,
                               FindFreeSpotOnMat(mat, cover.WorldBounds().size), new Vector3(0.28f, 0.03f, 0.28f), 0.27f, markMat);
            // 旧轴承：优先旧件托盘；托盘里原有的旧件已经占满时，退到螺钉托盘空着的前格（不挪动工作台原有物件）
            Transform trayT = null; Vector3 traySpot = default; string trayLabel = null, why = "";
            foreach (var (name, label) in new[] { ("Tray_OldParts", "旧件托盘"), ("Tray_Screws", "螺钉托盘前格") })
            {
                var t = Find(bench.transform, name);
                if (FindFreeSpotInTray(t, oldR.bounds.size, out traySpot, out var blockers)) { trayT = t; trayLabel = label; break; }
                why += $"{label} 没有放得下 {oldR.bounds.size.x * 1000:F0} mm 轴承的空位（被 {blockers} 占满）；";
            }
            if (trayT == null) throw new InvalidOperationException("工作台托盘里都放不下旧轴承：" + why);
            var trayZone = Zone("FO_Drop_PartsTray", "parts_tray", trayLabel, "WorkbenchArea/" + PathOf(trayT, bench.transform), bearingOld.partId,
                                traySpot, new Vector3(0.07f, 0.03f, 0.07f), 0.065f, markMat);
            Note($"落点：上盖总成 → {matZone.benchObjectPath} {matZone.landing.position:F3}；旧轴承 → {trayZone.benchObjectPath} 里的空位 {traySpot:F3}" +
                 (why.Length > 0 ? $"（{why}所以放到{trayLabel}）" : "（自动避开托盘里原有的物件）"));

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
            var rig = rigGo.AddComponent<FirstOrderCameraRig>();
            rig.Configure(cam, shots);
            camGo.transform.SetPositionAndRotation(shots[0].pose.position, shots[0].pose.rotation);
            cam.fieldOfView = shots[0].fov;

            // ---------------------------------------------------------------- 9. 流程与输入
            var carrier = new GameObject("FO_PartsCarrier (取下的零件挂在这里)").transform;
            var flowGo = new GameObject("FirstOrderFlow (七号首单原型：占位交互，非正式维修流程)");
            var flow = flowGo.AddComponent<FirstOrderFlow>();
            flow.Configure(dockCtl, rig, hingeL, hingeR, carrier, latchOuter, latchRear, cover, bearingOld, newBearing, rightEngine, matZone, trayZone);
            flowGo.AddComponent<FirstOrderInput>().Configure(flow);
            EditorUtility.SetDirty(flow);

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

        /// <summary>点选代理：触发盒包住主对象和总成成员的网格（世界包围盒换算到主对象本地，每边补 2 mm、最小 20 mm）。</summary>
        static void AddProxy(GameObject go, Transform[] members)
        {
            var rs = new List<Renderer> { go.GetComponent<Renderer>() };
            rs.AddRange(members.Select(m => m.GetComponent<Renderer>()));
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
