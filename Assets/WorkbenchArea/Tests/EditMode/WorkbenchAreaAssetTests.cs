using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WorkbenchArea.Tests
{
    /// <summary>导入结果、预制体碰撞与空间留白（Unity 坐标下复核 Blender 检查）。</summary>
    public class WorkbenchAreaAssetTests
    {
        const string Fbx = "Assets/WorkbenchArea/Art/WorkbenchArea.fbx";
        const string Prefab = "Assets/WorkbenchArea/Prefabs/WorkbenchArea.prefab";
        const string ScenePath = "Assets/WorkbenchArea/Scenes/WorkbenchArea_Test.unity";
        const float MatZ = 0.904f, ArmZ = MatZ + 0.072f;

        static Vector3 B2U(float x, float y, float z) => new Vector3(-x, z, -y);

        /// <summary>Blender 轴对齐盒 (lo, hi) → Unity 中心与半尺寸。</summary>
        static (Vector3 c, Vector3 half) Box(Vector3 lo, Vector3 hi)
        {
            var a = B2U(lo.x, lo.y, lo.z); var b = B2U(hi.x, hi.y, hi.z);
            return ((a + b) / 2f, new Vector3(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z)) / 2f);
        }

        GameObject area;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            area = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
        }

        [TearDown]
        public void TearDown() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Transform T(string n) => area.GetComponentsInChildren<Transform>(true).First(t => t.name == n);

        /// <summary>给所有没有实体碰撞的网格临时加 MeshCollider，按三角面精确检查占用（含杂物）。</summary>
        void AddGeometryProbes()
        {
            foreach (var mf in area.GetComponentsInChildren<MeshFilter>(true))
                if (mf.GetComponents<Collider>().All(c => c.isTrigger))
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            Physics.SyncTransforms();
        }

        List<string> Offenders(Vector3 lo, Vector3 hi, ISet<string> allowed)
        {
            var (c, h) = Box(lo, hi);
            return Physics.OverlapBox(c, h, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                .Select(x => x.name).Where(n => !allowed.Contains(n)).Distinct().OrderBy(n => n).ToList();
        }

        ISet<string> Allowed() => new HashSet<string>(area.GetComponentsInChildren<Transform>(true).Select(t => t.name)
            .Where(n => n.StartsWith("Placeholder_")).Concat(new[] { "Bench_Mat", "Bench_ArmCradles" }));

        [System.Serializable] class Stats { public int total_triangles, mesh_objects; public string[] materials; }
        static Stats BlenderStats => JsonUtility.FromJson<Stats>(System.IO.File.ReadAllText("ArtSource/WorkbenchArea/stats.json"));

        [Test]
        public void Import_CountsMatchBlenderStats()
        {
            var st = BlenderStats;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var mfs = model.GetComponentsInChildren<MeshFilter>(true);
            Assert.AreEqual(st.mesh_objects, mfs.Length, "网格数（与 Blender stats.json 一致）");
            Assert.AreEqual(st.total_triangles, mfs.Sum(m => m.sharedMesh.triangles.Length / 3), "三角面（与 Blender stats.json 一致）");
            var mats = model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            Assert.AreEqual(st.materials.Length, mats.Length, "材质数");
            Assert.IsTrue(mats.All(m => m != null && m.shader.name == "Universal Render Pipeline/Simple Lit"), "全部为重映射后的 URP Simple Lit 材质");
            Assert.IsTrue(mats.All(m => !m.IsKeywordEnabled("_SPECULAR_COLOR")), "不开高光（非写实 PBR）");
            Assert.IsTrue(mats.All(m => { var c = m.GetColor("_SpecColor"); return c.r + c.g + c.b < 1e-4f; }), "高光色为黑：即使关键字被重新打开也没有高光");
            var texs = mats.Select(m => m.GetTexture("_BaseMap")).Where(t => t != null).Distinct().ToArray();
            Assert.AreEqual(System.IO.Directory.GetFiles("ArtSource/WorkbenchArea/Textures", "*.png").Length, texs.Length, "贴图数（全部 Blender 贴图都被用到）");
            Assert.IsTrue(texs.All(t => t.filterMode == FilterMode.Point), "贴图点采样");
            Assert.IsTrue(texs.All(t => t.width <= 512 && t.height <= 512), "低分辨率贴图");
        }

        [Test]
        public void Import_EveryObjectHasRoleAndKeyPropsSurvive()
        {
            var props = area.GetComponentsInChildren<WbPartProperties>(true);
            Assert.AreEqual(BlenderStats.mesh_objects, props.Count(p => p.Role.Length > 0));
            Assert.AreEqual("placeholder_removable", T("Placeholder_Prosthetic_Cover").GetComponent<WbPartProperties>().Role);
            Assert.IsNotNull(T("Bench_Drawer_1_Probes").GetComponent<WbPartProperties>().Get("slide_axis_local"));
            Assert.IsNotNull(T("Bench_CabinetDoor").GetComponent<WbPartProperties>().Get("hinge_axis_local"));
            Assert.IsNotNull(T("Diag_Knob_Gain").GetComponent<WbPartProperties>().Get("rot_axis_local"));
        }

        [Test]
        public void Bench_TopIs90cm()
        {
            float top = T("Bench_Top").GetComponent<Renderer>().bounds.max.y;
            Assert.AreEqual(0.90f, top, 0.002f);
            Assert.AreEqual(MatZ, T("Bench_Mat").GetComponent<Renderer>().bounds.max.y, 0.002f);
        }

        [Test]
        public void Red_OnlyOnFaultIndicators()
        {
            var red = area.GetComponentsInChildren<Renderer>(true).Where(r => r.sharedMaterials.Any(m => m != null && m.name == "M_WB_FaultRed")).ToArray();
            Assert.IsNotEmpty(red);
            foreach (var r in red) Assert.AreEqual("fault_indicator", r.GetComponent<WbPartProperties>().Role, r.name);
        }

        [Test]
        public void Prefab_StaticCollisionAndInspectZones()
        {
            foreach (var n in new[] { "Room_Floor", "Room_WallBack", "Bench_Top", "Bench_DrawerCase", "Bench_Cabinet", "Wall_Pegboard", "Storage_Shelving", "Diag_Body", "Toolbox_Base" })
                Assert.IsTrue(T(n).GetComponents<Collider>().Any(c => !c.isTrigger), $"{n} 应有实体碰撞");
            foreach (var n in new[] { "Tray_Screws", "Tray_OldParts", "Placeholder_Prosthetic_Cover", "Placeholder_Prosthetic_Screw_1", "Placeholder_Prosthetic_Connector",
                                      "Placeholder_Prosthetic_FaultLED", "Diag_Probe", "Diag_Knob_Gain", "Toolbox_Tier1", "Lamp_Head", "Records_RepairLog" })
            {
                var z = T(n).GetComponentsInChildren<WbInspectable>(true).FirstOrDefault(w => w.target == T(n));
                Assert.IsNotNull(z, $"{n} 应有占位检查区");
                Assert.IsTrue(z.GetComponent<BoxCollider>().isTrigger);
            }
            // 会动的对象不能有实体碰撞或静态标记
            foreach (var n in new[] { "Tray_Screws", "Tray_OldParts", "Placeholder_Prosthetic_Cover", "Placeholder_Prosthetic_Screw_1" })
            {
                Assert.IsFalse(T(n).GetComponents<Collider>().Any(c => !c.isTrigger), n);
                Assert.IsFalse(GameObjectUtility.GetStaticEditorFlags(T(n).gameObject).HasFlag(StaticEditorFlags.BatchingStatic), n);
            }
            Assert.AreEqual(T("Tray_Screws"), T("Tray_Screws_Contents").parent, "托盘内容跟随托盘");
        }

        [Test]
        public void Prefab_ScrewZonesDoNotOverlap()
        {
            var zones = Enumerable.Range(1, 4).Select(i => T($"Placeholder_Prosthetic_Screw_{i}").GetComponentInChildren<BoxCollider>()).ToArray();
            Physics.SyncTransforms();
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    Assert.IsFalse(zones[i].bounds.Intersects(zones[j].bounds), $"螺钉 {i + 1} 与 {j + 1} 的检查区重叠");
        }

        [Test]
        public void Space_CentralWorkAreaAndHandZonesClear()
        {
            AddGeometryProbes();
            var allowed = Allowed();
            var zones = new Dictionary<string, (Vector3, Vector3)>
            {
                ["central_work_area"] = (new Vector3(-0.34f, 0.24f, MatZ + 0.001f), new Vector3(0.20f, 0.66f, MatZ + 0.30f)),
                ["left_hand_approach"] = (new Vector3(-0.36f, -0.10f, ArmZ - 0.02f), new Vector3(-0.16f, 0.38f, ArmZ + 0.16f)),
                ["right_hand_approach"] = (new Vector3(0.02f, -0.10f, ArmZ - 0.02f), new Vector3(0.22f, 0.38f, ArmZ + 0.16f)),
                ["over_arm_tool_space"] = (new Vector3(-0.30f, 0.38f, ArmZ + 0.06f), new Vector3(0.20f, 0.52f, ArmZ + 0.32f)),
                ["player_standing_knee_space"] = (new Vector3(-0.46f, -0.45f, 0.011f), new Vector3(0.48f, 0.13f, 1.60f)),
            };
            var fails = new List<string>();
            foreach (var kv in zones)
            {
                var off = Offenders(kv.Value.Item1, kv.Value.Item2, allowed);
                if (off.Count > 0) fails.Add($"{kv.Key}: {string.Join(", ", off)}");
            }
            Assert.IsEmpty(fails, string.Join("; ", fails));
        }

        [Test]
        public void Scene_HasTwoFixedCamerasLightsAndPlaceholderDemo()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var rig = Object.FindFirstObjectByType<WbCameraRig>();
            Assert.IsNotNull(rig);
            Assert.IsNotNull(GameObject.Find("CamPose_Game"));
            Assert.IsNotNull(GameObject.Find("CamPose_CloseUp"));
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            var warm = lights.FirstOrDefault(l => l.type == LightType.Spot);
            Assert.IsNotNull(warm, "暖色工作灯");
            Assert.Greater(warm.color.r, warm.color.b, "工作灯偏暖");
            Assert.IsTrue(lights.Any(l => l.type == LightType.Point && l.color.b > l.color.r), "冷色顶灯");
            var demo = Object.FindFirstObjectByType<WbPlaceholderDemo>();
            Assert.IsNotNull(demo);
            StringAssert.Contains("占位", demo.gameObject.name, "占位交互必须明确标注");
            Assert.IsNotNull(Object.FindFirstObjectByType<WbInteractionLog>(), "鼠标操作记录");
        }

        [Test]
        public void Scene_WarmKeyLightDominatesAndTonemapped()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            var key = lights.Single(l => l.type == LightType.Spot);
            var cool = lights.Where(l => l.color.b > l.color.r).ToArray();
            Assert.IsTrue(key.shadows != LightShadows.None, "工作灯带阴影");
            // 操作垫中心收到的光：点光 / 聚光按 强度 / 距离²，平行光按强度
            var matCenter = new Vector3(0.04f, MatZ, -0.44f);
            float At(Light l) => l.type == LightType.Directional ? l.intensity : l.intensity / Mathf.Max(0.01f, (l.transform.position - matCenter).sqrMagnitude);
            float keyAt = At(key), coolAt = cool.Sum(At);
            Assert.Greater(keyAt, 6f * coolAt, $"暖色工作灯是操作区主光（垫中心：暖 {keyAt:F2}，冷 {coolAt:F2}）");
            Assert.IsTrue(lights.Any(l => l.type == LightType.Point && l.color.r > l.color.b && l.range < 1.2f), "操作区暖色反弹光");
            Assert.AreEqual(UnityEngine.Rendering.AmbientMode.Trilight, RenderSettings.ambientMode);
            var amb = RenderSettings.ambientEquatorColor;
            Assert.Less(amb.g - Mathf.Min(amb.r, amb.b), 0.03f, "环境光不再偏成一片暗绿");
            var vol = Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            Assert.IsNotNull(vol, "后处理 Volume");
            Assert.IsTrue(vol.sharedProfile.components.Any(c => c.GetType().Name == "Tonemapping"), "色调映射：暖光焦点不截成白");
            Assert.IsFalse(vol.sharedProfile.components.Any(c => c.GetType().Name is "Bloom" or "ChromaticAberration" or "FilmGrain" or "LensDistortion"),
                "不用 Bloom / 色差 / 颗粒 / 畸变这类全屏特效");
        }

        [Test]
        public void Toolbox_LowerTierExplicitlyBlockedByUpperTier()
        {
            var lower = T("Toolbox_Tier1").GetComponentsInChildren<WbInspectable>(true).First(w => w.target == T("Toolbox_Tier1"));
            var upper = T("Toolbox_Tier2").GetComponentsInChildren<WbInspectable>(true).First(w => w.target == T("Toolbox_Tier2"));
            Assert.AreEqual(upper, lower.blockedBy, "下层的挡板是上层");
            StringAssert.Contains("先移开上层才能取用", lower.hint);
            StringAssert.Contains("先移开", upper.hint, "上层悬停时也说明下层被它挡住");
            Assert.IsTrue(lower.GetComponent<BoxCollider>().enabled, "下层保留检查区：悬停能看到“被挡住”的说明，而不是一个看似可点却毫无反应的目标");
            Assert.AreEqual(T("Toolbox_Tier2"), T("Toolbox_Tier2_Contents").parent, "上层里的工具跟着上层移动");
            var blocked = area.GetComponentsInChildren<WbInspectable>(true).Where(w => w.blockedBy != null)
                .Select(w => $"{w.target.name}<-{w.blockedBy.target.name}").OrderBy(s => s).ToArray();
            CollectionAssert.AreEqual(new[] { "Placeholder_Prosthetic_BayMotor<-Placeholder_Prosthetic_Cover", "Toolbox_Tier1<-Toolbox_Tier2" }, blocked,
                "被挡住的对象只有：工具箱下层（挡板：上层）、义肢检修口电机（挡板：盖板）");
            var bay = T("Placeholder_Prosthetic_BayMotor").GetComponentsInChildren<WbInspectable>(true).First(w => w.target == T("Placeholder_Prosthetic_BayMotor"));
            StringAssert.Contains("先拆下盖板才能检查", bay.hint);
        }

        [Test]
        public void Placeholder_AllPartsClearlyLabelled()
        {
            var ph = area.GetComponentsInChildren<WbInspectable>(true).Where(w => w.target.name.StartsWith("Placeholder_")).ToArray();
            Assert.IsNotEmpty(ph);
            foreach (var w in ph) StringAssert.StartsWith("【占位】", w.displayName, w.target.name);
            Assert.IsNotNull(T("Placeholder_Prosthetic_Tag"), "垫子上的 PH 占位标签");
            Assert.IsFalse(area.GetComponentsInChildren<Transform>(true).Any(t => t.name.Contains("RobotV4") || t.name.StartsWith("robot")), "不含 RobotV4");
        }

        [Test]
        public void CoverParkingBox_HasPlacementMargin()
        {
            // Blender 检查结果（checks.json）里记录的停放余量；Unity 里落位后的实测在 PlayMode 拆装测试中
            var text = System.IO.File.ReadAllText("ArtSource/WorkbenchArea/checks.json");
            var m = System.Text.RegularExpressions.Regex.Match(text, "\"cover_parking_box_has_margin\",\\s*\"passed\":\\s*(true|false)");
            Assert.IsTrue(m.Success && m.Groups[1].Value == "true", "Blender 停放框余量检查通过");
            var cover = T("Placeholder_Prosthetic_Cover").GetComponent<Renderer>().bounds.size;
            Assert.Less(cover.x, 0.250f - 2 * 0.012f, "停放框长 250 mm，比盖板每端至少宽 12 mm");
            Assert.Less(cover.z, 0.130f - 2 * 0.012f, "停放框宽 130 mm，比盖板每侧至少宽 12 mm");
        }
    }
}
