using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace WorkbenchArea.Tests
{
    /// <summary>
    /// 测试场景运行时验证：两台镜头的点选与小件可读性、托盘取放、占位拆装路径、镜头切换、控制台无错误。
    /// 测得数据追加写到 ArtSource/WorkbenchArea/Reports/Unity/playmode_measurements.txt。
    /// </summary>
    public class WorkbenchAreaPlayTests
    {
        const string ScenePath = "Assets/WorkbenchArea/Scenes/WorkbenchArea_Test.unity";
        const string Report = "ArtSource/WorkbenchArea/Reports/Unity/playmode_measurements.txt";
        static readonly Vector2 RefScreen = new Vector2(1920, 1080);
        static bool reportStarted;

        WbCameraRig rig;
        WbPlaceholderDemo demo;
        GameObject area;

        static void Write(string line)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Report));
            if (!reportStarted) { File.WriteAllText(Report, $"工作台区域 · PlayMode 实测（Unity {Application.unityVersion}）\n", new UTF8Encoding(false)); reportStarted = true; }
            File.AppendAllText(Report, line + "\n", new UTF8Encoding(false));
            Debug.Log("[WorkbenchAreaTest] " + line);
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
            yield return null;
            rig = Object.FindFirstObjectByType<WbCameraRig>();
            demo = Object.FindFirstObjectByType<WbPlaceholderDemo>();
            area = GameObject.Find("WorkbenchArea");
            Assert.IsNotNull(rig); Assert.IsNotNull(demo); Assert.IsNotNull(area);
            rig.enabled = false;   // 测试里不读鼠标 / 键盘
            demo.enabled = false;
            yield return null;
        }

        Transform T(string n) => area.GetComponentsInChildren<Transform>(true).First(t => t.name == n);

        static readonly string[] KeyTargets =
        {
            "Placeholder_Prosthetic_Screw_1", "Placeholder_Prosthetic_Screw_2", "Placeholder_Prosthetic_Screw_3", "Placeholder_Prosthetic_Screw_4",
            "Placeholder_Prosthetic_Cover", "Placeholder_Prosthetic_Connector", "Placeholder_Prosthetic_FaultLED",
            "Tray_Screws", "Tray_OldParts", "Diag_Probe", "Diag_Knob_Gain", "Diag_Switch_Power", "Toolbox_Tier1", "Records_RepairLog", "Lamp_Head",
        };

        /// <summary>点选：从镜头对准目标检查区中心（以及目标网格中心）发射射线，命中应为目标本身。</summary>
        string PickReport(WbCameraRig.View view, out List<string> missed)
        {
            rig.SetView(view);
            Physics.SyncTransforms();
            var cam = rig.Cam;
            missed = new List<string>();
            var sb = new StringBuilder();
            foreach (var n in KeyTargets)
            {
                var t = T(n);
                var zone = t.GetComponentsInChildren<WbInspectable>(true).First(w => w.target == t);
                // 对准网格包围盒中心及 8 个内缩 30% 的角点；任一点能选中即“可点”，并记录能选中的比例
                var rb = t.GetComponent<Renderer>().bounds;
                var aims = new List<Vector3> { rb.center };
                for (int i = 0; i < 8; i++)
                    aims.Add(rb.center + Vector3.Scale(rb.extents * 0.7f, new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1)));
                WbInspectable other = null;
                int good = 0;
                foreach (var aim in aims)
                {
                    var picked = WbCameraRig.Pick(new Ray(cam.transform.position, aim - cam.transform.position));
                    if (picked == zone) good++; else if (other == null) other = picked;
                }
                var zb = zone.GetComponent<BoxCollider>();
                float px = ScreenSize(cam, zb != null ? zb.bounds : rb);
                bool ok = good > 0;
                if (!ok) missed.Add($"{n}→{(other ? other.displayName : "无")}");
                sb.Append($"{n} {(ok ? $"可点 {good}/{aims.Count}" : "被挡")} 检查区 {px:F0}px; ");
            }
            return sb.ToString();
        }

        /// <summary>包围盒在 1920×1080 下的屏幕尺寸（取投影宽高中较大者）。</summary>
        static float ScreenSize(Camera cam, Bounds b)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i < 8; i++)
                pts.Add(cam.WorldToViewportPoint(b.center + Vector3.Scale(b.extents, new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1) * 2 - Vector3.one)));
            float w = (pts.Max(p => p.x) - pts.Min(p => p.x)) * RefScreen.x, h = (pts.Max(p => p.y) - pts.Min(p => p.y)) * RefScreen.y;
            return Mathf.Max(w, h);
        }

        [UnityTest]
        public IEnumerator Cameras_KeyTargetsClickable_AndSmallPartSize()
        {
            var game = PickReport(WbCameraRig.View.Game, out var missGame);
            var close = PickReport(WbCameraRig.View.CloseUp, out var missClose);
            // 螺钉头直径 7 mm 的视直径（参考 1920×1080 竖直像素）
            string Px(WbCameraRig.View v)
            {
                rig.SetView(v);
                var cam = rig.Cam;
                float k = RefScreen.y / (2f * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2f));
                var heads = Enumerable.Range(1, 4).Select(i => T($"Placeholder_Prosthetic_Screw_{i}").position).ToArray();
                float min = heads.Min(h => 0.007f * k / Vector3.Distance(cam.transform.position, h));
                return $"{min:F1}px";
            }
            Write($"游戏镜头点选：{game}");
            Write($"游戏镜头被挡：{(missGame.Count == 0 ? "无" : string.Join(", ", missGame))}；螺钉头最小 {Px(WbCameraRig.View.Game)}");
            Write($"近距镜头点选：{close}");
            Write($"近距镜头被挡：{(missClose.Count == 0 ? "无" : string.Join(", ", missClose))}；螺钉头最小 {Px(WbCameraRig.View.CloseUp)}");
            // 近距维修镜头必须能点到全部义肢占位件小件；游戏镜头必须能点到托盘、探头、诊断仪与盖板
            Assert.IsEmpty(missClose.Where(m => m.StartsWith("Placeholder_")), "近距镜头：" + string.Join(", ", missClose));
            Assert.IsEmpty(missGame.Where(m => m.StartsWith("Tray_") || m.StartsWith("Diag_") || m.StartsWith("Placeholder_Prosthetic_Cover")), "游戏镜头：" + string.Join(", ", missGame));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Cameras_ToggleBetweenFixedPoses()
        {
            rig.SetView(WbCameraRig.View.CloseUp);
            yield return null;
            Assert.Less(Vector3.Distance(rig.Cam.transform.position, GameObject.Find("CamPose_CloseUp").transform.position), 1e-4f);
            Assert.AreEqual(38f, rig.Cam.fieldOfView, 0.01f);
            rig.SetView(WbCameraRig.View.Game);
            yield return null;
            Assert.Less(Vector3.Distance(rig.Cam.transform.position, GameObject.Find("CamPose_Game").transform.position), 1e-4f);
            Assert.AreEqual(50f, rig.Cam.fieldOfView, 0.01f);
        }

        // ---------------------------------------------------------------- 路径检查：所有网格临时加 MeshCollider，移动中每帧用包围盒（内缩 1.5 mm）查占用
        // 静态批处理后运行时的 sharedMesh 是合并网格（世界坐标），所以从 FBX 资源取原始网格。
        void AddGeometryProbes()
        {
#if UNITY_EDITOR
            var src = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/WorkbenchArea/Art/WorkbenchArea.fbx").OfType<Mesh>()
                .GroupBy(m => m.name).ToDictionary(g => g.Key, g => g.First());
            foreach (var mf in area.GetComponentsInChildren<MeshFilter>(true))
                if (mf.GetComponents<Collider>().All(c => c.isTrigger))
                {
                    Assert.IsTrue(src.TryGetValue(mf.name, out var mesh), $"FBX 中找不到网格 {mf.name}");
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
                }
            Physics.SyncTransforms();
#endif
        }

        static Bounds GroupBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        IEnumerator Watch(IEnumerator routine, Transform[] movers, ISet<string> allowed, HashSet<string> hits)
        {
            var co = demo.StartCoroutine(routine);
            int guard = 0;
            float until = Time.realtimeSinceStartup + 20f;
            while (demo.Busy || guard == 0)
            {
                guard = 1;
                Physics.SyncTransforms();
                foreach (var m in movers)
                {
                    var b = GroupBounds(m);
                    foreach (var c in Physics.OverlapBox(b.center, Vector3.Max(b.extents - Vector3.one * 0.0015f, Vector3.one * 0.0005f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                        if (!allowed.Contains(c.name) && !c.transform.IsChildOf(m)) hits.Add($"{m.name}×{c.name}");
                }
                if (Time.realtimeSinceStartup > until) Assert.Fail("占位动作超时");
                yield return null;
            }
            yield return co;
        }

        [UnityTest]
        public IEnumerator Placeholder_TrayTakeAndPut()
        {
            AddGeometryProbes();
            var log = new StringBuilder();
            foreach (var tn in new[] { "Tray_Screws", "Tray_OldParts" })
            {
                var tray = T(tn);
                var home = tray.position;
                var hits = new HashSet<string>();
                var allowed = new HashSet<string> { "Bench_Top", "Bench_Mat" };   // 托盘原本就放在台面 / 垫子上
                yield return Watch(demo.ToggleTray(tray), new[] { tray }, allowed, hits);
                Assert.IsTrue(demo.IsTaken(tray));
                var path = demo.TrayPath(tray);
                Assert.Less(Vector3.Distance(tray.position, path[2]), 1e-4f);
                yield return Watch(demo.ToggleTray(tray), new[] { tray }, allowed, hits);
                Assert.IsFalse(demo.IsTaken(tray));
                Assert.Less(Vector3.Distance(tray.position, home), 1e-4f, "放回原位");
                log.Append($"{tn}：抬起 {(path[1].y - path[0].y) * 100:F0} cm，拉出 {(path[2].z - path[1].z) * 100:F0} cm 到台沿外，取 / 放途中碰撞 {(hits.Count == 0 ? "无" : string.Join(", ", hits))}；");
                Assert.IsEmpty(hits, string.Join(", ", hits));
            }
            Write("托盘取放（占位）：" + log);
        }

        [UnityTest]
        public IEnumerator Placeholder_DisassemblyPathClearAndReversible()
        {
            AddGeometryProbes();
            var ph = new HashSet<string>(area.GetComponentsInChildren<Transform>(true).Select(t => t.name).Where(n => n.StartsWith("Placeholder_")));
            var screws = demo.Screws;
            var homes = screws.Select(s => s.position).Append(demo.Cover.position).ToArray();
            var hitsScrew = new HashSet<string>();
            var hitsCover = new HashSet<string>();
            var allowedScrew = new HashSet<string>(ph) { "Tray_Screws", "Tray_Screws_Contents" };
            var allowedCover = new HashSet<string>(ph) { "Bench_Mat" };
            // 拆下：螺钉与盖板分别监视（盖板最后动）
            var co = demo.StartCoroutine(demo.Disassemble());
            float until = Time.realtimeSinceStartup + 30f;
            yield return null;
            while (demo.Busy)
            {
                Physics.SyncTransforms();
                foreach (var s in screws.Append(demo.Cover))
                {
                    var b = s.GetComponent<Renderer>().bounds;
                    var allowed = s == demo.Cover ? allowedCover : allowedScrew;
                    var hits = s == demo.Cover ? hitsCover : hitsScrew;
                    foreach (var c in Physics.OverlapBox(b.center, Vector3.Max(b.extents - Vector3.one * 0.0015f, Vector3.one * 0.0005f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                        if (!allowed.Contains(c.name)) hits.Add($"{s.name}×{c.name}");
                }
                if (Time.realtimeSinceStartup > until) Assert.Fail("拆下演示超时");
                yield return null;
            }
            yield return co;
            Assert.IsTrue(demo.Disassembled);
            var tray = T("Tray_Screws").GetComponent<Renderer>().bounds;
            foreach (var s in screws)
                Assert.IsTrue(s.position.x > tray.min.x && s.position.x < tray.max.x && s.position.z > tray.min.z && s.position.z < tray.max.z, $"{s.name} 落在螺钉托盘内");
            var cb = demo.Cover.GetComponent<Renderer>().bounds;
            Assert.AreEqual(0.904f, cb.min.y, 0.002f, "盖板平放在操作垫上");
            // 停放框（Blender x 0.15–0.37, y 0.24–0.36 → Unity x -0.37..-0.15, z -0.36..-0.24）
            Assert.IsTrue(cb.min.x > -0.372f && cb.max.x < -0.148f && cb.min.z > -0.362f && cb.max.z < -0.238f, $"盖板在 COVER 停放框内：{cb.min:F3}–{cb.max:F3}");
            Assert.IsEmpty(hitsScrew, string.Join(", ", hitsScrew));
            Assert.IsEmpty(hitsCover, string.Join(", ", hitsCover));
            Write($"占位拆装：4 颗螺钉 → 螺钉托盘、盖板 → COVER 停放框，途中碰撞 {(hitsScrew.Count + hitsCover.Count == 0 ? "无" : string.Join(", ", hitsScrew.Concat(hitsCover)))}；" +
                  $"盖板停放包围盒 {cb.min:F3}–{cb.max:F3}");
            // 装回
            yield return demo.StartCoroutine(demo.Reassemble());
            Assert.IsFalse(demo.Disassembled);
            var now = screws.Select(s => s.position).Append(demo.Cover.position).ToArray();
            for (int i = 0; i < now.Length; i++) Assert.Less(Vector3.Distance(now[i], homes[i]), 1e-4f, "装回原位");
            Write("占位装回：全部回到原位（误差 < 0.1 mm）");
        }

        [UnityTest]
        public IEnumerator Scene_RunsWithoutConsoleErrors()
        {
            rig.enabled = true;
            demo.enabled = true;
            for (int i = 0; i < 30; i++) yield return null;
            rig.SetView(WbCameraRig.View.CloseUp);
            for (int i = 0; i < 10; i++) yield return null;
            LogAssert.NoUnexpectedReceived();
        }
    }
}
