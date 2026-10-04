using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Dock;
using BorderRepair.FirstOrder;
using BorderRepair.TwoNight;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static BorderRepair.TwoNight.MeshClearance;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.Art.Unit07TrayVariant.Tests
{
    /// <summary>
    /// 网格缓存：编辑器 PlayMode 里不可读网格读不出、静态合批的物体换成合并网格。进入播放前（编辑模式）只读地附加打开验证场景，
    /// 按层级路径（名字 + 同名兄弟序号）缓存原始网格到项目 Temp/；托盘换父对象后按唯一名字找。不改导入设置、不改场景。
    /// </summary>
    public class VariantMeshCache : IPrebuildSetup
    {
        public const string Scene = "Assets/BorderRepair/Art/Unit07TrayVariant/Verification/TrayVariant_Verify.unity";
        const string FilePath = "Temp/TrayVariantMeshCache.bin";
        static Dictionary<string, (Vector3[] v, int[] t)> loaded, byName;

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent)
            {
                int same = 0;
                if (t.parent != null) { for (int i = 0; i < t.GetSiblingIndex(); i++) if (t.parent.GetChild(i).name == t.name) same++; }
                else foreach (var g in t.gameObject.scene.GetRootGameObjects()) { if (g.transform == t) break; if (g.name == t.name) same++; }
                parts.Add(t.name + "#" + same);
            }
            parts.Reverse(); return string.Join("/", parts);
        }
        static string Leaf(string key) { var last = key.Substring(key.LastIndexOf('/') + 1); return last.Substring(0, last.LastIndexOf('#')); }

        public void Setup()
        {
#if UNITY_EDITOR
            var scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Additive);
            try
            {
                using (var w = new BinaryWriter(File.Create(FilePath)))
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                        {
                            if (mf.sharedMesh == null) continue;
                            var v = mf.sharedMesh.vertices; var t = mf.sharedMesh.triangles;
                            w.Write(PathOf(mf.transform)); w.Write(v.Length); foreach (var p in v) { w.Write(p.x); w.Write(p.y); w.Write(p.z); }
                            w.Write(t.Length); foreach (var i in t) w.Write(i);
                        }
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
#endif
        }

        public static void Install()
        {
            if (loaded == null)
            {
                loaded = new Dictionary<string, (Vector3[], int[])>();
                using (var r = new BinaryReader(File.OpenRead(FilePath)))
                    while (r.BaseStream.Position < r.BaseStream.Length)
                    {
                        var k = r.ReadString();
                        var v = new Vector3[r.ReadInt32()]; for (int i = 0; i < v.Length; i++) v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        var t = new int[r.ReadInt32()]; for (int i = 0; i < t.Length; i++) t[i] = r.ReadInt32();
                        loaded[k] = (v, t);
                    }
                byName = loaded.GroupBy(kv => Leaf(kv.Key)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First().Value);
            }
            MeshClearance.PlayingReader = r => loaded.TryGetValue(PathOf(r.transform), out var d) || byName.TryGetValue(r.name, out d) ? d : throw new KeyNotFoundException("网格缓存里没有 " + PathOf(r.transform));
        }
    }

    /// <summary>
    /// 托盘变体的完整动作验证（隔离验证场景，程序播放，不是真人观看）：
    /// 开场持盘 → 左倾回正 → 移到托盘架 → 下降落座 → 开上爪 → 手退出 → 两臂交还 Animator → 落回悬停位；之后程序点击停靠、夹紧、断电、登记，检查夜末安全存档。
    /// 按固定步长 1/60 s 播放（含 Animator 浮动与过渡），每 3 帧量一次真实网格，分类判定：
    ///   A 七号（+ 手上托盘）↔ 环境；托盘离手后七号（不含右手）↔ 托盘；托盘离原位 20 mm 内不计它原位就贴着的托盘架；
    ///   B 手上托盘 ↔ 七号其余（不含右手）；C 两臂 ↔ 机身（不含根座铰接面）；
    ///   D1 右手非爪齿零件 ↔ 托盘全部几何（含握杆）—— 任何阶段都不能为 0；
    ///   D2 爪齿 ↔ 盘体 / 立柱 / 安装座 —— 任何阶段都不能为 0；
    ///   D3 爪齿 ↔ 握杆直段 —— 握持接触，允许为 0，只记录。
    /// 测距局限：不算边到边最近距离、判不出共面重叠；结果写 ArtSource/Unit07TrayVariant/Reports/motion_play.md。
    /// </summary>
    public class VariantMotionTests
    {
        class Parts { public WMesh body; public List<WMesh> bars = new List<WMesh>(), legs = new List<WMesh>(); public List<WMesh> All => new[] { body }.Concat(bars).Concat(legs).ToList(); }

        static Parts Split(Transform tray)
        {
            var parts = new Parts();
            foreach (var mf in tray.GetComponentsInChildren<MeshFilter>(true))
            {
                var w = World(mf.GetComponent<Renderer>()); if (w == null) continue;
                if (mf.name == "Tray_Body") { parts.body = w; continue; }
                string sfx = mf.name.EndsWith("PX") ? "PX" : "NX";
                var p0 = tray.Find("GripBarStart_" + sfx).position; var p1 = tray.Find("GripBarEnd_" + sfx).position;
                var c = (p0 + p1) / 2f; var axis = (p1 - p0).normalized; float half = (p1 - p0).magnitude / 2f + 0.0005f;
                var bt = new List<int>(); var lt = new List<int>();
                for (int i = 0; i < w.tri.Length; i += 3)
                {
                    var cen = (w.v[w.tri[i]] + w.v[w.tri[i + 1]] + w.v[w.tri[i + 2]]) / 3f;
                    float along = Vector3.Dot(cen - c, axis); float radial = (cen - c - axis * along).magnitude;
                    (Mathf.Abs(along) <= half && radial <= 0.0065f ? bt : lt).AddRange(new[] { w.tri[i], w.tri[i + 1], w.tri[i + 2] });
                }
                parts.bars.Add(Sub(w, bt, mf.name + "·握杆直段")); parts.legs.Add(Sub(w, lt, mf.name + "·立柱 / 安装座"));
            }
            return parts;
        }

        static WMesh Sub(WMesh w, List<int> tri, string name)
        {
            var b = new Bounds(w.v[tri[0]], Vector3.zero); foreach (var k in tri) b.Encapsulate(w.v[k]);
            return new WMesh { name = name, v = w.v, tri = tri.ToArray(), b = b };
        }

        struct Row { public int n; public float a, b, c, d1, d2, d3; public string ap, bp, cp, d1p, d2p; }

        [UnityTest, PrebuildSetup(typeof(VariantMeshCache))]
        public IEnumerator FullIncident_StrictGrip_ThenDock_SafeSave()
        {
            VariantMeshCache.Install();
            var dir = Path.Combine(Path.GetTempPath(), "TrayVariantProbe_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = dir;
            var s = TwoNightRun.NewGame(null);
            TwoNightRun.SettleCommunicator(s, true, "probe", null);
            TwoNightRun.ConfirmLedger(s);
            Time.captureDeltaTime = 1f / 60f;
            var md = new StringBuilder("# 托盘变体 · 完整动作逐帧检查（PlayMode，隔离验证场景）\n\n");
            try
            {
#if UNITY_EDITOR
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(VariantMeshCache.Scene, new LoadSceneParameters(LoadSceneMode.Single));
#endif
                yield return null;
                var director = UnityEngine.Object.FindFirstObjectByType<TwoNightRobotDirector>();
                var inc = director.Incident; var flow = director.Flow; var dock = flow.Dock;
                var robot = inc.RobotRoot; var tray = inc.Tray; var trayT = tray.transform;
                Assert.AreEqual("Dock_PartsTray_HandleRaised", tray.name, "验证场景用的是变体托盘");
                var homeParent = GameObject.Find("Unit07ServiceDock").transform;
                var homePos = director.TrayHomePosition; var homeRot = director.TrayHomeRotation;
                var trayRs = new HashSet<Renderer>(trayT.GetComponentsInChildren<Renderer>(true));
                bool Visible(Renderer r) => r.enabled && r.gameObject.activeInHierarchy;
                var env = homeParent.GetComponentsInChildren<Renderer>(true).Concat(GameObject.Find("WorkbenchArea").GetComponentsInChildren<Renderer>(true))
                    .Where(r => Visible(r) && !trayRs.Contains(r) && r.bounds.max.y < 2.2f).Select(World).Where(m => m != null).ToList();
                var handRs = new HashSet<Renderer>(inc.Overlay.RightBones[3].GetComponentsInChildren<Renderer>(true));
                var armNames = new HashSet<string>(inc.Overlay.RightBones[0].GetComponentsInChildren<Renderer>(true).Concat(inc.Overlay.LeftBones[0].GetComponentsInChildren<Renderer>(true)).Select(r => r.name));
                bool Seat(string n) => n.Contains("RootSeat") || n.Contains("RootRing") || n.Contains("RootBolts");
                bool Teeth(Renderer r) => r.name.EndsWith("_Teeth");

                // 托盘在原位时本来就贴着的环境件
                var trayHomeAll = Split(trayT);   // 第一帧前 Prepare 已经把盘拿到手上；这里先按当前位姿算，再平移回原位
                var toHome = Matrix4x4.TRS(homePos, homeRot, Vector3.one) * Matrix4x4.TRS(trayT.position, trayT.rotation, Vector3.one).inverse;
                var homeMeshes = trayHomeAll.All.Select(m => Moved(m, toHome)).ToList();
                var seatedWith = new HashSet<string>(env.Where(o => homeMeshes.Any(t => Distance(t, o, 0.002f) < 0.001f)).Select(o => o.name));

                var rows = new Dictionary<string, Row>(); var order = new List<string>();
                int frame = 0;
                while (!inc.Done && frame < 60 * 120)
                {
                    yield return null; frame++;
                    if (frame % 3 != 0) continue;
                    string phase = inc.Phase; bool taken = tray.Taken;
                    var rs = robot.GetComponentsInChildren<Renderer>(true).Where(r => Visible(r) && !trayRs.Contains(r)).ToList();
                    var parts = Split(trayT); var trayAll = parts.All;
                    bool nearHome = (trayT.position - homePos).magnitude < 0.02f;
                    var envNow = nearHome ? env.Where(o => !seatedWith.Contains(o.name)).ToList() : env;
                    var robotAll = rs.Select(World).Where(m => m != null).ToList();
                    var box = robotAll[0].b; foreach (var m in robotAll) box.Encapsulate(m.b); box.Expand(0.1f);
                    var A = Min(robotAll, env.Where(o => o.b.Intersects(box)).ToList(), 0.05f);
                    var At = Min(trayAll, envNow, 0.05f); if (At.d < A.d) A = At;
                    var nonHand = rs.Where(r => !handRs.Contains(r)).Select(World).Where(m => m != null).ToList();
                    (float d, string a, string b) B = (0.05f, "-", "-");
                    if (taken) B = Min(trayAll, nonHand, 0.05f);
                    else { var A2 = Min(nonHand, trayAll, 0.05f); if (A2.d < A.d) A = A2; }
                    var C = Min(rs.Where(r => armNames.Contains(r.name) && !Seat(r.name)).Select(World).ToList(), rs.Where(r => !armNames.Contains(r.name) && !Seat(r.name)).Select(World).ToList(), 0.05f);
                    var D1 = Min(rs.Where(r => handRs.Contains(r) && !Teeth(r)).Select(World).ToList(), trayAll, 0.05f);
                    var teeth = rs.Where(r => handRs.Contains(r) && Teeth(r)).Select(World).ToList();
                    var D2 = Min(teeth, new[] { parts.body }.Concat(parts.legs).ToList(), 0.05f);
                    var D3 = Min(teeth, parts.bars, 0.05f);
                    if (!rows.TryGetValue(phase, out var row)) { row = new Row { a = 0.05f, b = 0.05f, c = 0.05f, d1 = 0.05f, d2 = 0.05f, d3 = 0.05f }; order.Add(phase); }
                    if (A.d < row.a) { row.a = A.d; row.ap = A.a + " ↔ " + A.b; }
                    if (B.d < row.b) { row.b = B.d; row.bp = B.a + " ↔ " + B.b; }
                    if (C.d < row.c) { row.c = C.d; row.cp = C.a + " ↔ " + C.b; }
                    if (D1.d < row.d1) { row.d1 = D1.d; row.d1p = D1.a + " ↔ " + D1.b; }
                    if (D2.d < row.d2) { row.d2 = D2.d; row.d2p = D2.a + " ↔ " + D2.b; }
                    if (D3.d < row.d3) row.d3 = D3.d;
                    row.n++; rows[phase] = row;
                }
                Assert.IsTrue(inc.Done, "演出在 120 秒（模拟）内播完");
                string Mm(float d) => d >= 0.05f ? "≥ 50" : (d * 1000).ToString("F1");
                md.AppendLine($"- {DateTime.Now:yyyy-MM-dd HH:mm}，Unity {Application.unityVersion}。固定步长 1/60 s，共 {frame} 帧（约 {frame / 60f:F1} s），每 3 帧量一次。程序播放，不是真人观看。");
                md.AppendLine($"- 托盘原位就贴着的件（离原位 20 mm 内不计）：{string.Join("、", seatedWith)}。阶段：carry 端着 / lean 左倾回正 / return 移到托盘架、落座、张上爪 / release 手退出、（落盘） / away 退开升高 / arms_out 两臂交还 / descend 落回悬停位。\n");
                md.AppendLine("| 阶段 | 采样 | A 七号 / 托盘 ↔ 环境 | 最近 | B 手上托盘 ↔ 七号其余 | C 两臂 ↔ 机身 | D1 右手非爪齿 ↔ 托盘全部 | 最近 | D2 爪齿 ↔ 盘体 / 立柱 | D3 爪齿 ↔ 握杆（允许接触） |\n|---|---|---|---|---|---|---|---|---|---|");
                foreach (var k in order)
                {
                    var r = rows[k];
                    md.AppendLine($"| {k} | {r.n} | {Mm(r.a)} mm | {r.ap} | {(r.bp == null || r.bp == "- ↔ -" ? "—" : Mm(r.b) + " mm")} | {Mm(r.c)} mm | {Mm(r.d1)} mm | {r.d1p} | {Mm(r.d2)} mm | {Mm(r.d3)} mm |");
                }

                // 归位、回正、控制权交还
                yield return null;
                Assert.IsFalse(tray.Taken, "托盘已交还托盘架");
                Assert.AreEqual(homeParent, trayT.parent, "父对象恢复为维修座");
                Assert.Less((trayT.position - homePos).magnitude, 1e-5f, "托盘在原位");
                Assert.Less(Quaternion.Angle(trayT.rotation, homeRot), 0.01f, "托盘朝向复原");
                Assert.Less(Vector3.Angle(robot.up, Vector3.up), 0.01f, "机身回正");
                Assert.AreEqual(0f, inc.Overlay.RightWeight); Assert.AreEqual(0f, inc.Overlay.LeftWeight); Assert.AreEqual(0f, inc.Overlay.SteadyWeight);
                md.AppendLine($"\n- 演出结束：托盘父对象 `{trayT.parent.name}`，离原位 {(trayT.position - homePos).magnitude * 1000:F3} mm、朝向差 {Quaternion.Angle(trayT.rotation, homeRot):F3}°；机身倾角 {Vector3.Angle(robot.up, Vector3.up):F3}°；覆盖层权重 0（Animator 接管）。");

                // 停靠、夹紧、断电、登记、夜末存档（程序点击，与鼠标同一入口）
                var input = UnityEngine.Object.FindFirstObjectByType<FirstOrderInput>();
                var drv = new FirstOrderAcceptanceDriver(flow, input);
                input.enabled = false;
                Component Grip()
                {
                    flow.Rig.Go(FirstOrderCameraRig.Dock, true);
                    var gs = new[] { "Dock_Clamp_L_Grip", "Dock_Clamp_R_Grip" }.Select(n => (Component)GameObject.Find(n).GetComponent<DockInteractable>()).ToList();
                    return gs.FirstOrDefault(g => drv.FindClickPoint(g, out _)) ?? gs[0];
                }
                IEnumerator Click(Component target, bool expect)
                {
                    flow.Rig.Go(FirstOrderCameraRig.Dock, true); yield return null;
                    Assert.IsTrue(drv.FindClickPoint(target, out var sp), "点得到 " + target.name);
                    var (hit, ok) = input.ClickAt(sp);
                    Assert.AreEqual(target, hit); Assert.AreEqual(expect, ok, flow.Message);
                    float u = Time.realtimeSinceStartup + 20f; while (flow.Busy && Time.realtimeSinceStartup < u) yield return null;
                }
                IEnumerator Wait(Func<bool> c) { float u = Time.realtimeSinceStartup + 30f; while (!c() && Time.realtimeSinceStartup < u) yield return null; Assert.IsTrue(c()); }
                yield return Click(Grip(), true); yield return Wait(() => dock.State == DockState.SeatedOpen);
                yield return Click(Grip(), true); yield return Wait(() => dock.State == DockState.Clamped);
                yield return Click(GameObject.Find("Dock_PowerSwitch_LeverGrip").GetComponent<DockInteractable>(), true);
                yield return Wait(() => dock.State == DockState.RotorsStopped);
                yield return null;
                var safe = director.CollectSafeState();
                Assert.IsTrue(safe.IsSafe, safe.WhyUnsafe());
                director.Register();
                yield return null;
                var save = TwoNightSave.Read();
                Assert.AreEqual(SaveStatus.Ok, save.status, save.message + " / " + director.LastSaveMessage);
                Assert.AreEqual(800, save.state.Cash);
                Assert.IsTrue(save.state.unit07.trayStowed && save.state.unit07.IsSafe);
                md.AppendLine($"- 之后程序点击停靠 → 夹紧 → 断电 → 停稳 → 登记：安全六项全部成立，夜末存档 {save.status}，现金 {save.state.Cash}，托盘在原位 {save.state.unit07.trayStowed}。");

                var bad = order.Where(k => rows[k].a <= 0f || rows[k].b <= 0f || rows[k].c <= 0f || rows[k].d1 <= 0f || rows[k].d2 <= 0f).ToList();
                md.AppendLine(bad.Count == 0 ? "\n**结论（程序检测）**：A / B / C / D1 / D2 各阶段都 > 0；只有 D3（爪齿 ↔ 握杆）为握持接触。受测距局限（不算边到边、共面）约束，见剖视图。"
                                             : "\n**结论（程序检测）**：这些阶段出现 0 mm：" + string.Join("、", bad));
                Directory.CreateDirectory("ArtSource/Unit07TrayVariant/Reports");
                File.WriteAllText("ArtSource/Unit07TrayVariant/Reports/motion_play.md", md.ToString(), new UTF8Encoding(false));
                Assert.IsEmpty(bad, "这些阶段出现穿插：" + string.Join("、", bad));
            }
            finally
            {
                Time.captureDeltaTime = 0f;
                TwoNightSave.OverrideDirectory = null;
                TwoNightRun.Clear();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
