using System.Linq;
using BorderRepair.Dock;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.FirstOrder.Tests
{
    /// <summary>首单测试场景的组成：用的都是真实对象；只有标了占位的对象是程序生成的；预制体资源没有被改。</summary>
    [TestFixture("Assets/BorderRepair/FirstOrder/Scenes/Unit07FirstOrder_Test.unity")]
    [TestFixture("Assets/BorderRepair/FirstOrder/Scenes/LayoutAB/Unit07FirstOrder_LayoutA.unity")]
    [TestFixture("Assets/BorderRepair/FirstOrder/Scenes/LayoutAB/Unit07FirstOrder_LayoutB.unity")]
    public class FirstOrderSceneTests
    {
        readonly string ScenePath;
        public FirstOrderSceneTests(string scenePath) { ScenePath = scenePath; }   // 正式首单测试场景 + 布局 A/B 两个测试副本
        const string RobotPrefab = "Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_DockReady.prefab";
        const string DockPrefab = "Assets/BorderRepair/Prefabs/Unit07Dock/Unit07ServiceDock.prefab";
        const string BenchPrefab = "Assets/WorkbenchArea/Prefabs/WorkbenchArea.prefab";

        [SetUp] public void Open() => EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        [TearDown] public void Close() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        static Transform Under(string root, string path)
        {
            var r = GameObject.Find(root);
            Assert.IsNotNull(r, root);
            var t = r.transform.Find(path);
            Assert.IsNotNull(t, $"{root}/{path} 不存在");
            return t;
        }

        [Test]
        public void RealObjects_UsedByTheOrderExist()
        {
            const string E = "Robot_Rig/Root/Body/Engine_L_Hinge/";
            foreach (var n in new[] { "Engine_CoverLatch_Outer_L", "Engine_CoverLatch_Rear_L", "Engine_UpperCover_L", "Engine_IntakeLip_L", "Engine_IntakeGuard_L",
                                      "Engine_IntakeDuct_L", "Engine_BearingTop_L", "Engine_L_Rotor/Engine_Shaft_L" })
                Under("UNIT07_RobotV4_DockReady", E + n);
            Under("UNIT07_RobotV4_DockReady", "Robot_Rig/Root/Body/Engine_R_Hinge/Engine_UpperCover_R");
            foreach (var n in new[] { "Dock_Clamp_L/Dock_Clamp_L_Grip", "Dock_Clamp_R/Dock_Clamp_R_Grip", "Dock_PowerSwitch/Dock_PowerSwitch_Lever/Dock_PowerSwitch_LeverGrip", "Dock_RobotAnchor" })
                Under("Unit07ServiceDock", n);
            foreach (var n in new[] { "WB_Mat/Bench_Mat", "WB_Trays/Tray_Screws", "WB_Trays/Tray_OldParts", "WB_Trays/Box_Bearings" })
                Under("WorkbenchArea", n);
        }

        [Test]
        public void Parts_AreRealMeshesExceptLabelledPlaceholders()
        {
            var parts = Object.FindObjectsByType<FirstOrderPart>(FindObjectsSortMode.None);
            Assert.AreEqual(7, parts.Length);
            foreach (var p in parts)
            {
                if (p.isPlaceholder)
                {
                    StringAssert.Contains("占位", p.name, "程序生成的对象名字里必须写明占位");
                    StringAssert.Contains("无美术资产", p.realPath);
                    continue;
                }
                var mf = p.GetComponent<MeshFilter>();
                if (mf == null)
                {
                    // 故障美术包的部件（进气口堵塞、新轴承）：主对象是空节点，网格全部来自美术包 FBX
                    StringAssert.Contains(KitDir, p.realPath, p.name);
                    var kitMeshes = p.GetComponentsInChildren<MeshFilter>(true);
                    Assert.IsNotEmpty(kitMeshes, p.name);
                    foreach (var k in kitMeshes) StringAssert.StartsWith(KitDir, AssetDatabase.GetAssetPath(k.sharedMesh), $"{p.name}/{k.name} 的网格来自故障美术包");
                    continue;
                }
                Assert.AreEqual(p.name, mf.sharedMesh.name, $"{p.name} 用的是 FBX 里的同名网格");
                StringAssert.EndsWith(p.name, p.realPath);
                Assert.IsTrue(AssetDatabase.GetAssetPath(mf.sharedMesh).EndsWith("robot-final.fbx"), $"{p.name} 的网格来自 RobotV4 FBX");
            }
            Assert.AreEqual(0, parts.Count(p => p.isPlaceholder), "新轴承换成了美术包资源，不再有占位部件");
            Assert.IsNull(GameObject.Find("FO_Placeholder_NewBearing (占位：无美术资产)"), "占位圆柱已去掉");
            var cover = parts.Single(p => p.partId == "engine_l_cover");
            CollectionAssert.AreEquivalent(new[] { "Engine_IntakeLip_L", "Engine_IntakeGuard_L", "Engine_IntakeDuct_L" }, cover.members.Select(m => m.name));
        }

        const string KitDir = "Assets/BorderRepair/Art/Unit07FaultKit/Models/";

        /// <summary>
        /// 故障美术包挂在真实部件下：磨损轴承在 Engine_BearingTop_L 下（原轴承渲染器关掉、碰撞保留），堵塞在护栅下，保养标记在上盖下；
        /// 新轴承放在工作台轴承盒上；美术件不带实体碰撞（不改变遮挡）。
        /// </summary>
        [Test]
        public void FaultKit_MountedOnRealParts()
        {
            const string E = "Robot_Rig/Root/Body/Engine_L_Hinge/";
            var bearing = Under("UNIT07_RobotV4_DockReady", E + "Engine_BearingTop_L");
            Assert.IsFalse(bearing.GetComponent<MeshRenderer>().enabled, "原轴承渲染器关掉，由磨损件代替显示");
            Assert.IsNotNull(bearing.GetComponent<MeshCollider>(), "原轴承的碰撞保留（遮挡 / 点选）");
            string KitOf(Transform t) => AssetDatabase.GetAssetPath(t.GetComponent<MeshFilter>().sharedMesh);
            Assert.IsTrue(bearing.GetComponentsInChildren<MeshFilter>(true).Any(m => KitOf(m.transform).EndsWith("UNIT07_FK_BearingWorn.fbx")), "磨损轴承挂在原轴承下");
            var guard = Under("UNIT07_RobotV4_DockReady", E + "Engine_IntakeGuard_L");
            var clog = guard.GetComponentInChildren<FirstOrderPart>(true);
            Assert.IsNotNull(clog, "进气口堵塞挂在护栅下");
            Assert.AreEqual("engine_l_intake_clog", clog.partId);
            Assert.AreEqual(4, clog.GetComponentsInChildren<MeshRenderer>(true).Length, "堵塞 4 层：积尘垫、纤维、护栅积尘、唇口积尘");
            var cover = Under("UNIT07_RobotV4_DockReady", E + "Engine_UpperCover_L");
            Assert.IsTrue(cover.GetComponentsInChildren<MeshFilter>(true).Any(m => KitOf(m.transform).EndsWith("UNIT07_FK_CoverLabel.fbx")), "保养标记挂在上盖下");
            var flow = Object.FindFirstObjectByType<FirstOrderFlow>();
            Assert.AreEqual(clog, flow.Clog);
            Assert.AreEqual(bearing.GetComponent<MeshRenderer>(), flow.OriginalBearingRenderer);
            Assert.AreEqual(4, flow.ClogLayers.Count);
            StringAssert.Contains("Fibers", flow.ClogLayers[0].GetComponent<MeshFilter>().sharedMesh.name, "先清纤维");
            StringAssert.Contains("DustMat", flow.ClogLayers[3].GetComponent<MeshFilter>().sharedMesh.name, "最后清积尘垫");
            // 美术件只有点选用的触发盒，没有实体碰撞
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(m => AssetDatabase.GetAssetPath(m.sharedMesh).StartsWith(KitDir)))
                Assert.IsTrue(mf.GetComponents<Collider>().All(c => c.isTrigger), $"{mf.name} 不带实体碰撞");
            // 新轴承在轴承盒上，平放
            var box = Under("WorkbenchArea", "WB_Trays/Box_Bearings").GetComponent<Renderer>().bounds;
            var nb = flow.NewBearing.WorldBounds();
            Assert.IsTrue(box.min.x < nb.center.x && nb.center.x < box.max.x && box.min.z < nb.center.z && nb.center.z < box.max.z, "新轴承在轴承盒上方");
            float low = flow.NewBearing.Renderers().SelectMany(FirstOrderGeometry.WorldVertices).Min(v => v.y);
            Assert.AreEqual(box.min.y + 0.05f, low, 0.0015f, "新轴承最低点贴着盒体顶面（高 50 mm）");
        }

        /// <summary>
        /// 工作台上的三个摆放位姿不穿模、不悬空：新轴承平放在轴承盒顶面（场景里的位姿）、上盖总成翻面放在操作垫上、旧轴承平放在托盘里（落点位姿，Play 模式下流程正好放到这里）。
        /// 零件每个显示中的网格在目标位姿下做凸包，工作台上挨着的网格临时加精确碰撞，求穿入深度（≤ 0.5 mm 算不穿）；
        /// 最低顶点到下面表面的间隙 0–2 mm，且整体放低 2 mm 就会碰到表面。编辑器里网格可读、工作台没有静态合批，所以放在 EditMode 做。
        /// </summary>
        [Test]
        public void BenchPlacements_DoNotPenetrate()
        {
            var flow = Object.FindFirstObjectByType<FirstOrderFlow>();
            var bench = GameObject.Find("WorkbenchArea").transform;
            var report = new System.Collections.Generic.List<string>();
            void Check(FirstOrderPart p, Vector3 pos, Quaternion rot, (bool use, Vector3 point) approach, string what)
            {
                var root = p.transform;
                var inv = Quaternion.Inverse(root.rotation);
                var rs = p.Renderers().Where(r => r.enabled && r.GetComponent<MeshFilter>() != null).ToList();
                var temp = new System.Collections.Generic.List<Object>();
                var hulls = new System.Collections.Generic.List<(MeshCollider c, string n)>();
                var lo = new Bounds(pos, Vector3.zero);
                float low = float.MaxValue;
                foreach (var r in rs)
                {
                    var go = new GameObject("TmpHull_" + r.name);
                    go.transform.SetPositionAndRotation(pos + rot * (inv * (r.transform.position - root.position)), rot * inv * r.transform.rotation);
                    go.transform.localScale = r.transform.lossyScale;
                    var c = go.AddComponent<MeshCollider>(); c.convex = true; c.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
                    hulls.Add((c, r.name)); temp.Add(go);
                    foreach (var v in c.sharedMesh.vertices) { var w = go.transform.TransformPoint(v); lo.Encapsulate(w); low = Mathf.Min(low, w.y); }
                }
                lo.Expand(0.01f);
                var env = new System.Collections.Generic.List<(MeshCollider c, string n)>();
                foreach (var mf in bench.GetComponentsInChildren<MeshFilter>())
                {
                    var rr = mf.GetComponent<Renderer>();
                    if (rr == null || !rr.enabled || !rr.bounds.Intersects(lo)) continue;
                    var c = mf.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh;
                    env.Add((c, mf.name)); temp.Add(c);
                }
                Physics.SyncTransforms();
                System.Collections.Generic.List<string> Pen(Vector3 off)
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var (h, hn) in hulls)
                        foreach (var (e, en) in env)
                            if (Physics.ComputePenetration(h, h.transform.position + off, h.transform.rotation, e, e.transform.position, e.transform.rotation, out _, out var d) && d > 0.0005f)
                                list.Add($"{hn} 穿入 {en} {d * 1000:F1} mm");
                    return list;
                }
                var hits = Pen(Vector3.zero);
                bool touches = Pen(Vector3.down * 0.002f).Count > 0;
                // 工作台一侧的进出路线：落点上方空着就竖直往上 1 m；被挡住（台灯）就按构建时规划的进场点：竖直抬到低空 → 水平到进场点 → 竖直往上到 1 m。
                // 每 1 cm 查一次，途中不能碰到工作台上的任何东西（台灯等没有碰撞的物件也临时加精确碰撞；Play 模式下它们被静态合批，没法精确查）
                foreach (var mf in bench.GetComponentsInChildren<MeshFilter>())
                {
                    var rr = mf.GetComponent<Renderer>();
                    if (rr == null || !rr.enabled || rr.bounds.max.y > 2.2f || env.Any(e => e.c.gameObject == mf.gameObject)) continue;
                    var c = mf.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh;
                    env.Add((c, mf.name)); temp.Add(c);
                }
                Physics.SyncTransforms();
                var legs = approach.use
                    ? new[] { Vector3.zero, Vector3.up * (approach.point.y - pos.y), approach.point - pos, approach.point - pos + Vector3.up * (1.0f - (approach.point.y - pos.y)) }
                    : new[] { Vector3.zero, Vector3.up * 1.0f };
                var pathHits = new System.Collections.Generic.HashSet<string>();
                for (int l = 1; l < legs.Length; l++)
                {
                    int n = Mathf.CeilToInt(Vector3.Distance(legs[l - 1], legs[l]) / 0.01f);
                    for (int k = 1; k <= n; k++)
                        foreach (var x in Pen(Vector3.Lerp(legs[l - 1], legs[l], k / (float)n))) pathHits.Add(x.Split(' ')[2] + $"（第 {l} 段）");
                }
                var firstHits = pathHits.GroupBy(x => x.Split('（')[0]).Select(g => g.First()).ToList();
                float surface = float.MinValue;
                foreach (var (e, _) in env)
                    if (e.Raycast(new Ray(new Vector3(lo.center.x, low + 0.05f, lo.center.z), Vector3.down), out var hit, 0.2f)) surface = Mathf.Max(surface, hit.point.y);
                report.Add($"{what}：{hulls.Count} 个网格 × {env.Count} 个工作台网格，" +
                           $"穿入 {(hits.Count == 0 ? "无" : string.Join(", ", hits))}；最低顶点 {low:F4} m，中心下方表面 {surface:F4} m；放低 2 mm 碰到表面 {touches}；进出路线（{(approach.use ? "经进场点 " + approach.point.ToString("F3") : "竖直 1 m")}）碰到 {(firstHits.Count == 0 ? "无" : string.Join("、", firstHits))}");
                foreach (var o in temp) Object.DestroyImmediate(o);
                Assert.IsEmpty(hits, report.Last());
                Assert.IsTrue(touches, report.Last() + "：应贴着表面放，没有悬空 2 mm 以上");
                Assert.IsEmpty(firstHits, report.Last() + "：工作台一侧的进出路线要空着");
            }
            Check(flow.NewBearing, flow.NewBearing.transform.position, flow.NewBearing.transform.rotation, flow.NewBearingBenchApproach, "新轴承平放在轴承盒顶面");
            Check(flow.Cover, flow.MatZone.landing.position + flow.MatZone.landingOffset, flow.MatZone.landingRotation, (flow.MatZone.useApproach, flow.MatZone.approachPoint), "上盖总成翻面放在操作垫上");
            Check(flow.Bearing, flow.OldTrayZone.landing.position + flow.OldTrayZone.landingOffset, flow.OldTrayZone.landingRotation, (flow.OldTrayZone.useApproach, flow.OldTrayZone.approachPoint), "旧轴承平放在托盘里");
            Debug.Log("[FirstOrderTest] 工作台摆放：\n" + string.Join("\n", report));
        }

        /// <summary>RobotV4 原 FBX 和故障美术包 FBX 都没被改（字节级校验）。</summary>
        [Test]
        public void SourceArt_NotModified()
        {
            var expect = new System.Collections.Generic.Dictionary<string, string>
            {
                ["Assets/RobotV4/Model/robot-final.fbx"] = "80b94f89c3dfbd62e03214e7487e5d96",
                [KitDir + "UNIT07_FK_BearingNew.fbx"] = "dbc00062aa5a6a2be170086bcdba1283",
                [KitDir + "UNIT07_FK_BearingWorn.fbx"] = "93458ceefff0018cd5ce87bba2c3700c",
                [KitDir + "UNIT07_FK_CoverLabel.fbx"] = "c837a5b9a4b7645cf1722aaab6ca0383",
                [KitDir + "UNIT07_FK_IntakeClog.fbx"] = "620d0e8ad175dbf35647fcd054140216",
            };
            using var md5 = System.Security.Cryptography.MD5.Create();
            foreach (var kv in expect)
            {
                var hash = string.Concat(md5.ComputeHash(System.IO.File.ReadAllBytes(kv.Key)).Select(b => b.ToString("x2")));
                Assert.AreEqual(kv.Value, hash, kv.Key);
            }
        }

        [Test]
        public void SourcePrefabs_NotModified()
        {
            foreach (var path in new[] { RobotPrefab, DockPrefab, BenchPrefab })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsEmpty(go.GetComponentsInChildren<FirstOrderPart>(true), path);
                Assert.IsEmpty(go.GetComponentsInChildren<FirstOrderMember>(true), path);
            }
            Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefab).GetComponentsInChildren<Collider>(true).Length,
                "RobotV4 停靠预制体仍只有维修座集成留下的 1 个检查代理碰撞；点选和遮挡碰撞只加在场景实例上");
        }

        [Test]
        public void Robot_SitsOnDockAnchor_FacingTheBench()
        {
            var robot = GameObject.Find("UNIT07_RobotV4_DockReady").transform;
            var anchor = Under("Unit07ServiceDock", "Dock_RobotAnchor");
            Assert.Less(Vector3.Distance(robot.position, anchor.position), 1e-4f);
            var hingeL = Under("UNIT07_RobotV4_DockReady", "Robot_Rig/Root/Body/Engine_L_Hinge");
            var hingeR = Under("UNIT07_RobotV4_DockReady", "Robot_Rig/Root/Body/Engine_R_Hinge");
            Assert.AreEqual(hingeL.position.y, hingeR.position.y, 0.01f, "七号是直立的（左右引擎同高）");
            Assert.Greater(Vector3.Dot(Vector3.up, hingeL.up), 0.95f, "引擎轴线竖直向上");
            var bench = Under("WorkbenchArea", "WB_Bench/Bench_Top");
            Assert.AreEqual(0.90f, bench.GetComponent<Renderer>().bounds.max.y, 0.002f, "工作台直立、台面 0.90 m");
            Assert.IsFalse(Under("WorkbenchArea", "WB_Placeholder").gameObject.activeSelf, "工作台自带的义肢占位件在本场景里关掉");
        }

        [Test]
        public void DropZones_SitOnRealBenchObjects_InClearSpace()
        {
            var zones = Object.FindObjectsByType<FirstOrderDropZone>(FindObjectsSortMode.None);
            Assert.AreEqual(2, zones.Length);
            var mat = Under("WorkbenchArea", "WB_Mat/Bench_Mat").GetComponent<Renderer>().bounds;
            var matZone = zones.Single(z => z.zoneId == "mat");
            Assert.AreEqual(mat.max.y, matZone.landing.position.y, 0.001f);
            Assert.IsTrue(mat.min.x < matZone.landing.position.x && matZone.landing.position.x < mat.max.x && mat.min.z < matZone.landing.position.z && matZone.landing.position.z < mat.max.z);
            // 上盖总成翻过来放（内侧朝上）：按翻面后的顶点包围盒核对落点空地
            Assert.IsTrue(matZone.orientPart);
            var coverPart = Object.FindObjectsByType<FirstOrderPart>(FindObjectsSortMode.None).Single(p => p.partId == "engine_l_cover");
            var flipped = FirstOrderGeometry.BoundsAt(FirstOrderGeometry.LocalOffsets(coverPart), matZone.landingRotation);
            Assert.AreEqual(0f, flipped.min.y + matZone.landingOffset.y, 0.0015f, "翻面后最低顶点落在操作垫表面上");
            var cover = flipped.size;
            Physics.SyncTransforms();
            var hits = Physics.OverlapBox(matZone.landing.position + Vector3.up * (cover.y / 2f + 0.002f), cover / 2f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            Assert.IsEmpty(hits.Select(h => h.name), "上盖总成的落点上没有别的东西（托架等）");
            var tray = zones.Single(z => z.zoneId == "parts_tray");
            StringAssert.StartsWith("WorkbenchArea/WB_Trays/", tray.benchObjectPath);
        }

        [Test]
        public void Flow_UsesExistingDockController()
        {
            var flow = Object.FindFirstObjectByType<FirstOrderFlow>();
            Assert.IsNotNull(flow.Dock, "维修座动作交给现有 Unit07DockController");
            Assert.IsNotNull(Object.FindFirstObjectByType<FirstOrderInput>());
            Assert.IsNull(Object.FindFirstObjectByType<Unit07DockInput>(), "不再挂维修座自己的输入，点击统一由首单输入处理（避免一次点击触发两次）");
            StringAssert.Contains("占位", flow.gameObject.name);
        }
    }
}
