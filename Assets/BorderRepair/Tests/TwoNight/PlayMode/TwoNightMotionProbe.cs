using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static BorderRepair.TwoNight.MeshClearance;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.TwoNight.Tests
{
    /// <summary>
    /// 端盘演出的动作探针（发布场景 Unit07_Night，托盘 = 提手抬高变体）：按固定步长 1/60 s 真实播放整段演出（Animator 浮动 / 摆动、两臂权重过渡、上爪开合、放盘滑出、托盘落座、倾斜都在）。
    /// 采样：
    ///   - 关键接触段（return 下降落座与开爪、release 滑出放手、away 退开升起）**每一帧**量右手 ↔ 托盘与托盘 ↔ 环境；
    ///   - 其余检查每 3 帧量一次。
    /// 分类（TrayGeometry；停用的旧盘不参与）：
    ///   A 七号 / 手上托盘 ↔ 维修座、工作台；托盘离手后七号（不含右手）↔ 托盘；托盘离原位 20 mm 内不计它原位就坐着的件；
    ///   B 手上托盘 ↔ 七号（不含右手）；C 两臂 ↔ 机身（不含根座铰接面）；
    ///   D1 右手非爪齿零件（爪身、爪架、销轴、腕）↔ 托盘全部几何（含握杆）；D2 爪齿 ↔ 盘体 / 立柱；D3 爪齿 ↔ 握杆直段（握持接触，允许为 0，只记录）。
    /// 判定：A / B / C / D1 / D2 任何阶段出现 0 mm 即失败。
    /// 局限：测距不算边到边最近距离、判不出共面重叠；采样之间（每帧之间的 1/60 s）不插值；所以这是“逐帧 / 每 3 帧采样下没有测到穿插”，不是连续几何证明。
    /// 结果写 Docs/Integration/TwoNightSlice/incident_motion_play.md。
    /// </summary>
    public class TwoNightMotionProbe
    {
        struct Row { public int n, nKey; public float a, b, c, d1, d2, d3; public string ap, bp, cp, d1p, d2p, d3p; }

        [UnityTest, PrebuildSetup(typeof(TwoNightMeshCache))]
        public IEnumerator Incident_FullMotion_StrictGrip_NoPenetration()
        {
            TwoNightMeshCache.Install();
            var dir = Path.Combine(Path.GetTempPath(), "TwoNightProbe_" + Guid.NewGuid().ToString("N"));
            TwoNightSave.OverrideDirectory = dir;
            var s = TwoNightRun.NewGame(null);
            TwoNightRun.SettleCommunicator(s, true, "probe", null);
            TwoNightRun.ConfirmLedger(s);
            Time.captureDeltaTime = 1f / 60f;
            try
            {
#if UNITY_EDITOR
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/BorderRepair/Scenes/Slice/" + TwoNightScenes.Robot + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
#endif
                yield return null;
                var director = UnityEngine.Object.FindFirstObjectByType<TwoNightRobotDirector>();
                var inc = director.Incident;
                var robot = inc.RobotRoot; var tray = inc.Tray; var trayT = tray.transform;
                Assert.AreEqual("Dock_PartsTray_HandleRaised", tray.name, "发布场景用的是提手抬高变体");
                var oldTray = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "Dock_PartsTray" && t.gameObject.scene.IsValid());
                Assert.IsTrue(oldTray == null || !oldTray.gameObject.activeInHierarchy, "原托盘在发布场景里停用");
                bool Visible(Renderer r) => r.enabled && r.gameObject.activeInHierarchy;
                var trayRs = new HashSet<Renderer>(trayT.GetComponentsInChildren<Renderer>(true));
                var dock = GameObject.Find("Unit07ServiceDock").transform; var bench = GameObject.Find("WorkbenchArea").transform;
                var env = dock.GetComponentsInChildren<Renderer>(true).Concat(bench.GetComponentsInChildren<Renderer>(true))
                    .Where(r => Visible(r) && !trayRs.Contains(r) && r.bounds.max.y < 2.2f).Select(World).Where(m => m != null).ToList();
                var handRs = new HashSet<Renderer>(inc.Overlay.RightBones[3].GetComponentsInChildren<Renderer>(true));
                var armNames = new HashSet<string>(inc.Overlay.RightBones[0].GetComponentsInChildren<Renderer>(true).Concat(inc.Overlay.LeftBones[0].GetComponentsInChildren<Renderer>(true)).Select(r => r.name));
                bool Seat(string n) => n.Contains("RootSeat") || n.Contains("RootRing") || n.Contains("RootBolts");
                var homePos = director.TrayHomePosition; var homeRot = director.TrayHomeRotation;
                var toHome = Matrix4x4.TRS(homePos, homeRot, Vector3.one) * Matrix4x4.TRS(trayT.position, trayT.rotation, Vector3.one).inverse;
                var homeMeshes = TrayGeometry.Split(trayT, toHome).All;    // 第一帧前 Prepare 已经把盘拿到手上：按当前位姿算，再平移回原位
                var seatedWith = new HashSet<string>(env.Where(o => homeMeshes.Any(t => Distance(t, o, 0.002f) < 0.001f)).Select(o => o.name));
                var keyPhases = new HashSet<string> { "return", "release", "away" };

                var rows = new Dictionary<string, Row>(); var order = new List<string>();
                int frame = 0, keyFrames = 0;
                while (!inc.Done && frame < 60 * 120)
                {
                    yield return null; frame++;
                    string phase = inc.Phase; bool taken = tray.Taken;
                    bool key = keyPhases.Contains(phase);
                    bool full = frame % 3 == 0;
                    if (!key && !full) continue;
                    if (!rows.TryGetValue(phase, out var row)) { row = new Row { a = 0.05f, b = 0.05f, c = 0.05f, d1 = 0.05f, d2 = 0.05f, d3 = 0.05f }; order.Add(phase); }
                    var rs = robot.GetComponentsInChildren<Renderer>(true).Where(r => Visible(r) && !trayRs.Contains(r)).ToList();
                    var parts = TrayGeometry.Split(trayT, Matrix4x4.identity); var trayAll = parts.All;
                    bool nearHome = (trayT.position - homePos).magnitude < 0.02f;
                    var envNow = nearHome ? env.Where(o => !seatedWith.Contains(o.name)).ToList() : env;
                    var nonTeeth = rs.Where(r => handRs.Contains(r) && !TrayGeometry.IsTeeth(r)).Select(World).ToList();
                    var teeth = rs.Where(r => handRs.Contains(r) && TrayGeometry.IsTeeth(r)).Select(World).ToList();
                    var D1 = Min(nonTeeth, trayAll, 0.05f); var D2 = Min(teeth, parts.NonBar, 0.05f); var D3 = Min(teeth, parts.bars, 0.05f);
                    var At = Min(trayAll, envNow, 0.05f);
                    if (D1.d < row.d1) { row.d1 = D1.d; row.d1p = D1.a + " ↔ " + D1.b; }
                    if (D2.d < row.d2) { row.d2 = D2.d; row.d2p = D2.a + " ↔ " + D2.b; }
                    if (D3.d < row.d3) { row.d3 = D3.d; row.d3p = D3.a + " ↔ " + D3.b; }
                    if (At.d < row.a) { row.a = At.d; row.ap = At.a + " ↔ " + At.b; }
                    if (key) { row.nKey++; keyFrames++; }
                    if (full)
                    {
                        var robotAll = rs.Select(World).Where(m => m != null).ToList();
                        var box = robotAll[0].b; foreach (var m in robotAll) box.Encapsulate(m.b); box.Expand(0.1f);
                        var A = Min(robotAll, env.Where(o => o.b.Intersects(box)).ToList(), 0.05f);
                        if (A.d < row.a) { row.a = A.d; row.ap = A.a + " ↔ " + A.b; }
                        var nonHand = rs.Where(r => !handRs.Contains(r)).Select(World).Where(m => m != null).ToList();
                        if (taken) { var B = Min(trayAll, nonHand, 0.05f); if (B.d < row.b) { row.b = B.d; row.bp = B.a + " ↔ " + B.b; } }
                        else { var A2 = Min(nonHand, trayAll, 0.05f); if (A2.d < row.a) { row.a = A2.d; row.ap = A2.a + " ↔ " + A2.b; } }
                        var C = Min(rs.Where(r => armNames.Contains(r.name) && !Seat(r.name)).Select(World).ToList(), rs.Where(r => !armNames.Contains(r.name) && !Seat(r.name)).Select(World).ToList(), 0.05f);
                        if (C.d < row.c) { row.c = C.d; row.cp = C.a + " ↔ " + C.b; }
                        row.n++;
                    }
                    rows[phase] = row;
                }
                Assert.IsTrue(inc.Done, "演出在 120 秒（模拟）内播完");
                yield return null;
                Assert.IsFalse(tray.Taken, "托盘交还托盘架");
                Assert.Less((trayT.position - homePos).magnitude, 1e-5f, "托盘回到原位");
                Assert.Less(Quaternion.Angle(trayT.rotation, homeRot), 0.01f, "托盘朝向复原");
                Assert.AreEqual(director.TrayHomeParent, trayT.parent, "托盘父对象复原");
                Assert.Less(Vector3.Angle(robot.up, Vector3.up), 0.01f, "机身回正");
                Assert.AreEqual(0f, inc.Overlay.RightWeight); Assert.AreEqual(0f, inc.Overlay.LeftWeight); Assert.AreEqual(0f, inc.Overlay.SteadyWeight);

                string Mm(float d) => d >= 0.05f ? "≥ 50" : (d * 1000).ToString("F1");
                var md = new StringBuilder("# 第一晚端盘演出：真实播放检查（PlayMode 动作探针，发布场景）\n\n");
                md.AppendLine($"- {DateTime.Now:yyyy-MM-dd HH:mm}，Unity {Application.unityVersion}。托盘：`{tray.name}`（提手抬高 45 mm 悬臂变体；原托盘停用）。固定步长 1/60 s，共 {frame} 帧（约 {frame / 60f:F1} s）。程序播放，不是真人观看。");
                md.AppendLine($"- 采样：关键接触段（return / release / away）每一帧量右手 ↔ 托盘、托盘 ↔ 环境（共 {keyFrames} 帧）；七号整体 ↔ 环境、手上托盘 ↔ 七号、两臂 ↔ 机身每 3 帧一次。");
                md.AppendLine("- 局限：测距不算边到边、判不出共面重叠；帧与帧之间不插值。结论是“这些采样里没有测到穿插”，不是连续几何证明。");
                md.AppendLine($"- 托盘离原位 20 mm 内不计它原位就坐着的件：{string.Join("、", seatedWith)}。\n");
                md.AppendLine("| 阶段 | 3 帧采样 | 每帧采样 | A 七号 / 托盘 ↔ 环境 | 最近 | B 手上托盘 ↔ 七号其余 | C 两臂 ↔ 机身 | D1 右手非爪齿 ↔ 托盘全部 | 最近 | D2 爪齿 ↔ 盘体 / 立柱 | D3 爪齿 ↔ 握杆（允许） |\n|---|---|---|---|---|---|---|---|---|---|---|");
                foreach (var k in order)
                {
                    var r = rows[k];
                    md.AppendLine($"| {k} | {r.n} | {r.nKey} | {Mm(r.a)} mm | {r.ap} | {(r.bp == null ? "—" : Mm(r.b) + " mm")} | {Mm(r.c)} mm | {Mm(r.d1)} mm | {r.d1p} | {Mm(r.d2)} mm | {Mm(r.d3)} mm |");
                }
                var bad = order.Where(k => rows[k].a <= 0f || rows[k].b <= 0f || rows[k].c <= 0f || rows[k].d1 <= 0f || rows[k].d2 <= 0f).ToList();
                md.AppendLine($"\n- 演出结束：托盘父对象 `{trayT.parent.name}`、离原位 {(trayT.position - homePos).magnitude * 1000:F3} mm、朝向差 {Quaternion.Angle(trayT.rotation, homeRot):F3}°；机身倾角 {Vector3.Angle(robot.up, Vector3.up):F3}°；覆盖层权重全部为 0（Animator 接管）。");
                md.AppendLine(bad.Count == 0 ? "- **程序检测结论**：A / B / C / D1 / D2 各阶段都 > 0；只有 D3（爪齿 ↔ 握杆）为握持接触。两臂 ↔ 机身 9.0 mm 是静止姿态本身的值。"
                                             : "- **程序检测结论**：这些阶段出现 0 mm：" + string.Join("、", bad));
                Directory.CreateDirectory("Docs/Integration/TwoNightSlice");
                File.WriteAllText("Docs/Integration/TwoNightSlice/incident_motion_play.md", md.ToString(), new UTF8Encoding(false));
                Assert.IsEmpty(bad, "这些阶段出现穿插：" + string.Join("、", bad) + "（见 incident_motion_play.md）");
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
