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
            Assert.AreEqual(44f, rig.Cam.fieldOfView, 0.01f);
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
            var bay = T("Placeholder_Prosthetic_BayMotor").GetComponentsInChildren<WbInspectable>(true).First(w => w.target == T("Placeholder_Prosthetic_BayMotor"));
            Assert.IsFalse(bay.IsBlocked, "盖板拆下后检修口电机可以检查");
            var tray = T("Tray_Screws").GetComponent<Renderer>().bounds;
            foreach (var s in screws)
                Assert.IsTrue(s.position.x > tray.min.x && s.position.x < tray.max.x && s.position.z > tray.min.z && s.position.z < tray.max.z, $"{s.name} 落在螺钉托盘内");
            var cb = demo.Cover.GetComponent<Renderer>().bounds;
            Assert.AreEqual(0.904f, cb.min.y, 0.002f, "盖板平放在操作垫上");
            // 停放框（Blender x 0.125–0.375, y 0.235–0.365 → Unity x -0.375..-0.125, z -0.365..-0.235），四周至少 12 mm 余量
            float mx0 = cb.min.x - (-0.375f), mx1 = -0.125f - cb.max.x, mz0 = cb.min.z - (-0.365f), mz1 = -0.235f - cb.max.z;
            float minMargin = Mathf.Min(Mathf.Min(mx0, mx1), Mathf.Min(mz0, mz1));
            Assert.GreaterOrEqual(minMargin, 0.012f, $"盖板落位后四周余量（mm）：{mx0 * 1000:F1} / {mx1 * 1000:F1} / {mz0 * 1000:F1} / {mz1 * 1000:F1}");
            Assert.IsEmpty(hitsScrew, string.Join(", ", hitsScrew));
            Assert.IsEmpty(hitsCover, string.Join(", ", hitsCover));
            Write($"占位拆装：4 颗螺钉 → 螺钉托盘、盖板 → COVER 停放框，途中碰撞 {(hitsScrew.Count + hitsCover.Count == 0 ? "无" : string.Join(", ", hitsScrew.Concat(hitsCover)))}；" +
                  $"盖板停放包围盒 {cb.min:F3}–{cb.max:F3}，距停放框四边 {mx0 * 1000:F1} / {mx1 * 1000:F1} / {mz0 * 1000:F1} / {mz1 * 1000:F1} mm");
            // 装回
            yield return demo.StartCoroutine(demo.Reassemble());
            Assert.IsFalse(demo.Disassembled);
            Assert.IsTrue(bay.IsBlocked, "盖板装回后检修口电机又被挡住");
            var now = screws.Select(s => s.position).Append(demo.Cover.position).ToArray();
            for (int i = 0; i < now.Length; i++) Assert.Less(Vector3.Distance(now[i], homes[i]), 1e-4f, "装回原位");
            Write("占位装回：全部回到原位（误差 < 0.1 mm）");
        }

        // ---------------------------------------------------------------- “看得见就点得到”：不留看似可点却点不到的目标
        /// <summary>
        /// 两台镜头下，对每个可检查对象的 9 个瞄准点：先用真实点选规则记下选中谁，再给所有网格加精确碰撞（单独一层），
        /// 看这条射线第一眼看到的是不是这个对象。看到了却选不中 = 看似可点却点不到 → 失败。被挡住的工具箱下层选中自己并带“先移开上层”说明，算通过。
        /// </summary>
        [UnityTest]
        public IEnumerator Cameras_EveryVisibleTargetIsPickable()
        {
            var zones = area.GetComponentsInChildren<WbInspectable>(true);
            var picks = new List<(WbInspectable z, WbCameraRig.View v, Ray ray, WbInspectable picked)>();
            foreach (var view in new[] { WbCameraRig.View.Game, WbCameraRig.View.CloseUp })
            {
                rig.SetView(view);
                Physics.SyncTransforms();
                var cam = rig.Cam;
                foreach (var z in zones)
                {
                    var r = z.target.GetComponent<Renderer>();
                    var rb = r.bounds;
                    for (int i = -1; i < 8; i++)
                    {
                        var aim = i < 0 ? rb.center : rb.center + Vector3.Scale(rb.extents * 0.7f, new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1));
                        var vp = cam.WorldToViewportPoint(aim);
                        if (vp.z <= 0 || vp.x < 0 || vp.x > 1 || vp.y < 0 || vp.y > 1) continue;
                        var ray = new Ray(cam.transform.position, aim - cam.transform.position);
                        picks.Add((z, view, ray, WbCameraRig.Pick(ray)));
                    }
                }
            }
            // 第二阶段：精确可见性（所有网格 → 第 31 层的 MeshCollider）
            const int probeLayer = 31;
#if UNITY_EDITOR
            var src = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/WorkbenchArea/Art/WorkbenchArea.fbx").OfType<Mesh>()
                .GroupBy(m => m.name).ToDictionary(g => g.Key, g => g.First());
            foreach (var mf in area.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = new GameObject("VisProbe_" + mf.name) { layer = probeLayer };
                go.transform.SetParent(mf.transform, false);
                go.AddComponent<MeshCollider>().sharedMesh = src[mf.name];
            }
#endif
            Physics.SyncTransforms();
            // 按“对象 × 镜头”统计：看得见的瞄准点里至少有一个能选中它（被挡住的下层选中挡板也算——挡板的悬停说明会告诉玩家先移开它）
            var perTarget = new Dictionary<(WbInspectable, WbCameraRig.View), (int visible, int ok)>();
            var edgeCases = new List<string>();
            int visibleSamples = 0;
            foreach (var p in picks)
            {
                if (!Physics.Raycast(p.ray, out var hit, 5f, 1 << probeLayer, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider.transform.parent != p.z.target) continue;   // 这条射线第一眼看到的不是它：本来就看不见，不要求能点
                visibleSamples++;
                bool ok = p.picked == p.z || (p.z.blockedBy != null && p.picked == p.z.blockedBy);
                if (!ok) edgeCases.Add($"{p.v}:{p.z.target.name}→{(p.picked ? p.picked.target.name : "无")}");
                var key = (p.z, p.v);
                perTarget.TryGetValue(key, out var c);
                perTarget[key] = (c.visible + 1, c.ok + (ok ? 1 : 0));
            }
            var unreachable = perTarget.Where(kv => kv.Value.ok == 0).Select(kv => $"{kv.Key.Item2}:{kv.Key.Item1.target.name}（可见 {kv.Value.visible} 点）").ToList();
            Write($"看得见就点得到：两台镜头共 {perTarget.Count} 个“对象 × 镜头”组合看得见（{visibleSamples} 个可见瞄准点）；看得见却完全点不中的组合 {unreachable.Count} 个" +
                  (unreachable.Count > 0 ? "：" + string.Join(", ", unreachable) : "") +
                  $"。边缘点被相邻小件 / 包围盒较松的部件抢走 {edgeCases.Count} 处（同一对象换个位置仍能选中）：{string.Join(", ", edgeCases.Distinct().Take(12))}");
            Assert.IsEmpty(unreachable, string.Join(", ", unreachable));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Toolbox_LowerTierOnlyAfterUpperTierMoved()
        {
            var lower = demo.ToolboxLower;
            Assert.IsTrue(lower.IsBlocked, "上层在原位：下层被挡住");
            StringAssert.Contains("先移开上层才能取用", lower.HudText);
            AddGeometryProbes();
            var hits = new HashSet<string>();
            var upper = demo.ToolboxUpper;
            var allowed = new HashSet<string> { "Toolbox_Arms", "Toolbox_Tier1", "Toolbox_Base" };   // 上层原本就搁在支臂上、贴着下层
            yield return Watch(demo.ToggleUpperTier(), new[] { upper }, allowed, hits);
            Assert.IsTrue(demo.UpperTierMoved);
            Assert.IsFalse(lower.IsBlocked, "移开上层后下层可以取用");
            // 移开后从游戏镜头看下层：可见的瞄准点必须能选中下层
            rig.SetView(WbCameraRig.View.Game);
            Physics.SyncTransforms();
            int ok = 0;
            var rb = lower.target.GetComponent<Renderer>().bounds;
            for (int i = 0; i < 8; i++)
            {
                var aim = rb.center + Vector3.Scale(rb.extents * 0.6f, new Vector3((i & 1) * 2 - 1, 0.5f, ((i >> 2) & 1) * 2 - 1));
                if (WbCameraRig.Pick(new Ray(rig.Cam.transform.position, aim - rig.Cam.transform.position)) == lower) ok++;
            }
            Assert.Greater(ok, 0, "移开上层后，游戏镜头能点到下层");
            yield return Watch(demo.ToggleUpperTier(), new[] { upper }, allowed, hits);
            Assert.IsTrue(lower.IsBlocked, "放回上层后下层又被挡住");
            Assert.IsEmpty(hits, string.Join(", ", hits));
            Write($"工具箱上层移开 / 放回（占位）：上层抬起 {(demo.UpperTierPath()[1].y - demo.UpperTierPath()[0].y) * 100:F0} cm，途中碰撞 {(hits.Count == 0 ? "无" : string.Join(", ", hits))}；" +
                  $"移开后游戏镜头 {ok}/8 个瞄准点能选中下层；上层在原位时下层悬停提示：“{lower.HudText}”");
        }

        // ---------------------------------------------------------------- 模拟鼠标：经 Input System 走完整的 悬停 → HUD → 点击 → 占位动作 链路
        [UnityTest]
        public IEnumerator Mouse_SimulatedHoverAndClickThroughInputSystem()
        {
            var settings = UnityEngine.InputSystem.InputSystem.settings;
            var prevBg = settings.backgroundBehavior;
#if UNITY_EDITOR
            var prevEd = settings.editorInputBehaviorInPlayMode;
            settings.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;   // 批处理模式下编辑器没有焦点
            var mouse = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>("WbTestMouse");
            try
            {
                mouse.MakeCurrent();
                rig.enabled = true;
                demo.enabled = true;
                rig.SetView(WbCameraRig.View.Game);
                yield return null;
                var cam = rig.Cam;
                var log = new StringBuilder();
                IEnumerator MoveTo(Vector3 world)
                {
                    var sp = (Vector2)cam.WorldToScreenPoint(world);
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = sp });
                    yield return null;
                    yield return null;
                }
                IEnumerator Click(Vector3 world)
                {
                    var sp = (Vector2)cam.WorldToScreenPoint(world);
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = sp }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, true));
                    yield return null;
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = sp });
                    yield return null;
                }
                // 1. 悬停托盘 → HUD 指向托盘
                var trayZone = demo.TrayScrews.GetComponentsInChildren<WbInspectable>(true).First(w => w.target == demo.TrayScrews);
                yield return MoveTo(trayZone.GetComponent<BoxCollider>().bounds.center);
                Assert.AreEqual(trayZone, rig.Hovered,
                    $"悬停螺钉托盘：current={UnityEngine.InputSystem.Mouse.current?.name} pos={UnityEngine.InputSystem.Mouse.current?.position.ReadValue()} " +
                    $"目标屏幕点={(Vector2)cam.WorldToScreenPoint(trayZone.GetComponent<BoxCollider>().bounds.center)} 屏幕={Screen.width}×{Screen.height} 实际悬停={rig.Hovered?.target.name}");
                log.Append($"悬停托盘 → HUD“{rig.Hovered.HudText}”；");
                // 2. 点击托盘 → 占位取出
                yield return Click(trayZone.GetComponent<BoxCollider>().bounds.center);
                float until = Time.realtimeSinceStartup + 20f;
                while (demo.Busy && Time.realtimeSinceStartup < until) yield return null;
                Assert.IsTrue(demo.IsTaken(demo.TrayScrews), "点击托盘 → 取出（占位）");
                log.Append($"点击托盘 → {demo.LastMessage}；");
                // 3. 悬停被挡住的工具箱下层（若有可见点）/ 上层 → 说明文字
                var upperZone = demo.ToolboxUpper.GetComponentsInChildren<WbInspectable>(true).First(w => w.target == demo.ToolboxUpper);
                yield return MoveTo(upperZone.GetComponent<BoxCollider>().bounds.center);
                Assert.AreEqual(upperZone, rig.Hovered, "悬停工具箱上层");
                StringAssert.Contains("先移开", rig.Hovered.hint);
                log.Append($"悬停工具箱上层 → 提示“{rig.Hovered.hint}”；");
                // 4. Tab 切近距镜头后悬停一颗螺钉
                rig.SetView(WbCameraRig.View.CloseUp);
                yield return null;
                var screw = demo.Screws[0];
                var screwZone = screw.GetComponentsInChildren<WbInspectable>(true).First(w => w.target == screw);
                yield return MoveTo(screw.position);
                Assert.AreEqual(screwZone, rig.Hovered, "近距镜头悬停螺钉");
                StringAssert.StartsWith("【占位】", rig.Hovered.displayName);
                log.Append($"近距镜头悬停螺钉 → “{rig.Hovered.HudText}”");
                Write("模拟鼠标（Input System 虚拟鼠标，非真人）：" + log);
            }
            finally
            {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
                settings.backgroundBehavior = prevBg;
#if UNITY_EDITOR
                settings.editorInputBehaviorInPlayMode = prevEd;
#endif
            }
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
