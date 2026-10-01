using System.Linq;
using BorderRepair.Dock;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.FirstOrder.Tests
{
    /// <summary>首单测试场景的组成：用的都是真实对象；只有标了占位的对象是程序生成的；预制体资源没有被改。</summary>
    public class FirstOrderSceneTests
    {
        const string ScenePath = "Assets/BorderRepair/FirstOrder/Scenes/Unit07FirstOrder_Test.unity";
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
            Assert.AreEqual(6, parts.Length);
            foreach (var p in parts)
            {
                if (p.isPlaceholder)
                {
                    StringAssert.Contains("占位", p.name, "程序生成的对象名字里必须写明占位");
                    StringAssert.Contains("无美术资产", p.realPath);
                    continue;
                }
                var mf = p.GetComponent<MeshFilter>();
                Assert.IsNotNull(mf, p.name);
                Assert.AreEqual(p.name, mf.sharedMesh.name, $"{p.name} 用的是 FBX 里的同名网格");
                StringAssert.EndsWith(p.name, p.realPath);
                Assert.IsTrue(AssetDatabase.GetAssetPath(mf.sharedMesh).EndsWith("robot-final.fbx"), $"{p.name} 的网格来自 RobotV4 FBX");
            }
            Assert.AreEqual(1, parts.Count(p => p.isPlaceholder), "只有新轴承是占位");
            var cover = parts.Single(p => p.partId == "engine_l_cover");
            CollectionAssert.AreEquivalent(new[] { "Engine_IntakeLip_L", "Engine_IntakeGuard_L", "Engine_IntakeDuct_L" }, cover.members.Select(m => m.name));
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
            var cover = Object.FindObjectsByType<FirstOrderPart>(FindObjectsSortMode.None).Single(p => p.partId == "engine_l_cover").WorldBounds().size;
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
