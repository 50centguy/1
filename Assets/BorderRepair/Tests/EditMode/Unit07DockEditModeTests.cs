using System.Linq;
using BorderRepair.Dock;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.Tests
{
    /// <summary>UNIT 07 维修座第一阶段接入：资产、材质、属性、代理碰撞、对位与夹具方向（编辑模式）。</summary>
    public class Unit07DockEditModeTests
    {
        const string DockFbx = "Assets/BorderRepair/Art/Unit07ServiceDock/UNIT07_ServiceDock.fbx";
        const string DockPrefab = "Assets/BorderRepair/Prefabs/Unit07Dock/Unit07ServiceDock.prefab";
        const string RobotPrefab = "Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_DockReady.prefab";
        const string RobotFbx = "Assets/RobotV4/Model/robot-final.fbx";
        const float RobotRootY = 0.72f;

        GameObject dock, robot;

        [TearDown]
        public void Cleanup()
        {
            if (dock != null) Object.DestroyImmediate(dock);
            if (robot != null) Object.DestroyImmediate(robot);
        }

        static Transform Find(GameObject root, string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
        static Renderer Rend(GameObject root, string name) => root.GetComponentsInChildren<Renderer>(true).First(r => r.name == name);

        /// <summary>Blender 机器人坐标（前 = -Y）的包围盒 → Unity 世界坐标（前 = +Z，X 镜像），七号根在锚点上。</summary>
        public static Bounds FromRobot(Vector3 lo, Vector3 hi)
        {
            var min = new Vector3(-hi.x, lo.z + RobotRootY, -hi.y);
            var max = new Vector3(-lo.x, hi.z + RobotRootY, -lo.y);
            var b = new Bounds(); b.SetMinMax(min, max); return b;
        }

        public static float AabbDistance(Bounds a, Bounds b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
            float dy = Mathf.Max(0f, Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y));
            float dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static readonly (string name, Bounds box, float minGap)[] Corridors =
        {
            ("power_tray_drop", FromRobot(new Vector3(-0.126f, -0.103f, 0.05f), new Vector3(0.126f, 0.103f, 0.207f)), 0.015f),
            ("power_tray_out_front", FromRobot(new Vector3(-0.126f, -0.45f, 0.05f), new Vector3(0.126f, 0.103f, 0.10f)), 0.015f),
            ("engine_L_out", FromRobot(new Vector3(0.219f, -0.14f, 0.26f), new Vector3(0.92f, 0.12f, 0.575f)), 0f),
            ("engine_R_out", FromRobot(new Vector3(-0.92f, -0.14f, 0.26f), new Vector3(-0.219f, 0.12f, 0.575f)), 0f),
            ("rear_cassette_back", FromRobot(new Vector3(-0.085f, 0.13f, 0.39f), new Vector3(0.085f, 0.50f, 0.57f)), 0f),
            ("top_cover_up", FromRobot(new Vector3(-0.175f, -0.18f, 0.60f), new Vector3(0.175f, -0.012f, 0.95f)), 0f),
            ("front_parts_forward", FromRobot(new Vector3(-0.23f, -0.65f, 0.26f), new Vector3(0.23f, -0.19f, 0.64f)), 0f),
        };

        void SpawnDocked()
        {
            dock = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DockPrefab));
            robot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefab));
            robot.transform.position = Find(dock, "Dock_RobotAnchor").position;
            var seated = AssetDatabase.LoadAllAssetsAtPath(RobotFbx).OfType<AnimationClip>().First(c => c.name == "Pose_Gripper_Closed");
            seated.SampleAnimation(robot, 0f);
            Physics.SyncTransforms();
        }

        [Test]
        public void DockModelImportedWithSevenUrpMaterials()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(DockFbx);
            Assert.IsNotNull(model, "维修座 FBX 未导入");
            var mfs = model.GetComponentsInChildren<MeshFilter>(true);
            Assert.AreEqual(48, mfs.Length);
            Assert.AreEqual(8692, mfs.Sum(m => m.sharedMesh.triangles.Length / 3));
            var mats = model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            Assert.AreEqual(7, mats.Length);
            foreach (var m in mats)
            {
                Assert.IsNotNull(m);
                Assert.AreEqual("Universal Render Pipeline/Lit", m.shader.name, m.name);
                StringAssert.StartsWith("M_Dock_", m.name);
            }
            foreach (var n in new[] { "M_Dock_Ivory", "M_Dock_Gray", "M_Dock_Steel" })
                Assert.AreEqual("T_Dock_Grime", mats.First(m => m.name == n).GetTexture("_BaseMap").name, n);
            Assert.AreEqual("T_Dock_Warning", mats.First(m => m.name == "M_Dock_Warning").GetTexture("_BaseMap").name);
            Assert.AreEqual("T_Dock_Labels", mats.First(m => m.name == "M_Dock_Label").GetTexture("_BaseMap").name);
            Assert.IsTrue(mats.First(m => m.name == "M_Dock_Lamp").IsKeywordEnabled("_EMISSION"));
        }

        [Test]
        public void PlaceholderIsNotImported()
        {
            Assert.IsEmpty(AssetDatabase.FindAssets("UNIT07_RobotPlaceholder", new[] { "Assets" }), "占位体不应导入项目");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DockPrefab);
            Assert.IsFalse(prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("PH_")), "预制体里不应有占位体对象");
        }

        [Test]
        public void UnityAnglesComeFromFbxProperties()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DockPrefab);
            float Get(string obj, string key)
            {
                var p = Find(prefab, obj).GetComponent<DockPartProperties>();
                Assert.IsNotNull(p, $"{obj} 缺少 DockPartProperties");
                Assert.IsTrue(p.TryGetFloat(key, out var v), $"{obj} 缺少 {key}");
                return v;
            }
            Assert.AreEqual(-30f, Get("Dock_Clamp_L", "unity_open_deg"), 1e-4);
            Assert.AreEqual(30f, Get("Dock_Clamp_R", "unity_open_deg"), 1e-4);
            Assert.AreEqual(-Get("Dock_Clamp_L", "open_deg"), Get("Dock_Clamp_L", "unity_open_deg"), 1e-4, "unity_open_deg 应与 Blender open_deg 反号");
            Assert.AreEqual(-Get("Dock_Clamp_R", "open_deg"), Get("Dock_Clamp_R", "unity_open_deg"), 1e-4);
            Assert.AreEqual(35f, Get("Dock_PowerSwitch_Lever", "unity_off_deg"), 1e-4);
            Assert.AreEqual(-35f, Get("Dock_PowerSwitch_Lever", "unity_on_deg"), 1e-4);
        }

        [Test]
        public void ProxiesOnlyOnTheListedParts()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DockPrefab);
            var cols = prefab.GetComponentsInChildren<Collider>(true);
            var expected = new[] { "Dock_ContactPad_L", "Dock_ContactPad_R", "Dock_Clamp_L_JawPad", "Dock_Clamp_R_JawPad", "Dock_Clamp_L_Grip", "Dock_Clamp_R_Grip",
                                   "Dock_PowerSwitch_LeverGrip", "Dock_PowerSwitch_Label", "Dock_PartsTray", "Dock_MagneticBox" };
            CollectionAssert.AreEquivalent(expected, cols.Select(c => c.name).ToArray());
            foreach (var c in cols)
            {
                Assert.IsInstanceOf<BoxCollider>(c);
                Assert.IsNotNull(c.GetComponent<DockInteractable>(), c.name);
                var s = ((BoxCollider)c).size;
                Assert.Less(s.x * s.y * s.z, 0.01f, $"{c.name} 的代理过大");
            }
            Assert.IsNotNull(Find(prefab, "Dock_PartsTray").GetComponent<DockPickable>());
            Assert.IsNotNull(Find(prefab, "Dock_MagneticBox").GetComponent<DockPickable>());
        }

        [Test]
        public void DockCollidersKeepRemovalCorridorsClear()
        {
            SpawnDocked();
            var dockCols = dock.GetComponentsInChildren<Collider>(true);
            foreach (var (name, box, minGap) in Corridors)
                foreach (var c in dockCols)
                {
                    float d = AabbDistance(c.bounds, box);
                    Assert.GreaterOrEqual(d, minGap > 0 ? minGap : 1e-5f, $"{c.name} 离通道 {name} 只有 {d * 1000:F1} mm");
                }
        }

        [Test]
        public void RobotRootAlignsWithAnchorAndPadsSitUnderHardpoints()
        {
            SpawnDocked();
            var anchor = Find(dock, "Dock_RobotAnchor");
            Assert.That(Vector3.Distance(anchor.position, new Vector3(0f, 0.72f, 0f)), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(robot.transform.position, anchor.position), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(robot.transform.lossyScale, Vector3.one), Is.LessThan(1e-5f));
            // 正面 +Z：前框在机身外壳的 +Z 一侧
            Assert.Greater(Rend(robot, "FrontBezel").bounds.center.z, Rend(robot, "Body_Shell").bounds.center.z);
            foreach (var s in new[] { "L", "R" })
            {
                var pad = Find(dock, $"Dock_ContactPad_{s}").GetComponent<Collider>().bounds;
                var hp = Rend(robot, $"Chassis_ArmHardpoint_{s}").bounds;
                Assert.That(pad.min.x >= hp.min.x - 1e-4f && pad.max.x <= hp.max.x + 1e-4f, $"接触垫 {s} 的 X 范围不在同侧硬点板下");
                Assert.That(pad.min.z >= hp.min.z - 1e-4f && pad.max.z <= hp.max.z + 1e-4f, $"接触垫 {s} 的 Z 范围不在同侧硬点板下");
                Assert.AreEqual(hp.min.y, pad.max.y, 0.0005f, $"接触垫 {s} 顶面应贴住硬点板底面");
            }
        }

        [Test]
        public void ClampOpensAwayFromRailWithUnityAngleOnly()
        {
            SpawnDocked();
            foreach (var s in new[] { "L", "R" })
            {
                var clamp = Find(dock, $"Dock_Clamp_{s}");
                var props = clamp.GetComponent<DockPartProperties>();
                props.TryGetFloat("unity_open_deg", out var unityOpen);
                props.TryGetFloat("open_deg", out var blenderOpen);
                var pad = Rend(dock, $"Dock_Clamp_{s}_JawPad");
                var rail = Rend(robot, $"Chassis_MountRail_{s}").bounds;
                float railOuter = Mathf.Max(Mathf.Abs(rail.min.x), Mathf.Abs(rail.max.x));
                var q0 = clamp.localRotation;
                float Inner() => Mathf.Min(Mathf.Abs(pad.bounds.min.x), Mathf.Abs(pad.bounds.max.x));
                float closed = Inner();
                Assert.AreEqual(railOuter, closed, 0.0015f, $"夹具 {s} 夹紧时夹面应贴住安装轨");
                clamp.localRotation = q0 * Quaternion.AngleAxis(unityOpen, Vector3.up);
                float opened = Inner();
                clamp.localRotation = q0 * Quaternion.AngleAxis(blenderOpen, Vector3.up);
                float wrong = Inner();
                clamp.localRotation = q0;
                Assert.Greater(opened - railOuter, 0.015f, $"按 unity_open_deg 转动，夹具 {s} 应离开安装轨");
                Assert.Less(wrong, railOuter, $"按 Blender open_deg 转动，夹具 {s} 会压向安装轨（所以不能用）");
            }
        }

        [Test]
        public void RotorAxesMatchIdleHoverClip()
        {
            robot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefab));
            var driver = robot.GetComponent<RotorPowerDriver>();
            Assert.IsNotNull(driver);
            Assert.AreEqual(360f, driver.IdleSpeedDegPerSec, 1f);
            var idle = AssetDatabase.LoadAllAssetsAtPath(RobotFbx).OfType<AnimationClip>().First(c => c.name == "Idle_Hover");
            for (int i = 0; i < driver.Rotors.Length; i++)
            {
                idle.SampleAnimation(robot, 0.1f);
                var q0 = driver.Rotors[i].localRotation;
                idle.SampleAnimation(robot, 0.1f + 1f / 30f);
                (Quaternion.Inverse(q0) * driver.Rotors[i].localRotation).ToAngleAxis(out var ang, out var axis);
                if (ang > 180f) axis = -axis;
                Assert.Less(Vector3.Angle(axis, driver.LocalAxes[i]), 1f, $"转子 {driver.Rotors[i].name} 转轴与 Idle_Hover 不一致");
            }
        }
    }
}
