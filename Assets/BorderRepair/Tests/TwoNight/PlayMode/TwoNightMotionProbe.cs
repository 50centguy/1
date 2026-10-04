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
    /// 端盘演出的动作探针：按固定步长（1/60 s）真实播放整段演出（Animator 的身体浮动 / 引擎摆动、两臂权重过渡、夹爪开合、放盘、倾斜都在），
    /// 每 3 帧用真实网格量一次：
    /// A 七号（+ 手上的托盘）↔ 维修座 / 工作台（托盘离开手后也算环境，但右手 ↔ 托盘单列为 D；托盘在离原位 20 mm 内不计托盘 ↔ 托盘架：它本来就坐在凹槽里）；
    /// B 手上的托盘 ↔ 七号（不含右手两爪）；
    /// C 两臂 ↔ 七号其它部分（不含两臂自身根座的铰接面）；
    /// D 右手（腕以下）↔ 托盘。
    /// 结果写 Docs/Integration/TwoNightSlice/incident_motion_play.md。
    /// 判定：A / B / C 任何阶段出现 0 mm 即失败；D 只在“端着 / 放手滑出”阶段允许为 0（审计握法本身下爪穿进盘壁约 20 mm，见 incident_motion.md，需美术补件），其余阶段也必须 &gt; 0。
    /// </summary>
    public class TwoNightMotionProbe
    {
        [UnityTest, PrebuildSetup(typeof(TwoNightMeshCache))]
        public IEnumerator Incident_FullMotion_NoPenetration()
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
                var inc = UnityEngine.Object.FindFirstObjectByType<TrayIncident>();
                var robot = inc.RobotRoot;
                var tray = inc.Tray;
                var dock = GameObject.Find("Unit07ServiceDock").transform;
                var bench = GameObject.Find("WorkbenchArea").transform;
                bool Visible(Renderer r) => r.enabled && r.gameObject.activeInHierarchy;
                var env = dock.GetComponentsInChildren<Renderer>(true).Concat(bench.GetComponentsInChildren<Renderer>(true))
                    .Where(r => Visible(r) && r.transform != tray.transform && r.bounds.max.y < 2.2f).Select(World).Where(m => m != null).ToList();
                var trayR = tray.GetComponent<Renderer>();
                var armR = inc.Overlay.RightBones[0]; var armL = inc.Overlay.LeftBones[0];
                var armNames = new HashSet<string>(armR.GetComponentsInChildren<Renderer>(true).Concat(armL.GetComponentsInChildren<Renderer>(true)).Select(r => r.name));
                bool Seat(string n) => n.Contains("RootSeat") || n.Contains("RootRing") || n.Contains("RootBolts");
                var director = UnityEngine.Object.FindFirstObjectByType<TwoNightRobotDirector>();
                var trayHome = director.TrayHomePosition;                 // 托盘一开场就在手里：原位取导演在第一帧之前记下的
                // 托盘坐在原位时本来就贴着的环境件（托盘架凹槽、旁边的磁性盒等）：托盘离原位 20 mm 内不计这些
                var trayAtHome = Moved(World(trayR), Matrix4x4.TRS(trayHome, director.TrayHomeRotation, Vector3.one) * Matrix4x4.TRS(tray.transform.position, tray.transform.rotation, Vector3.one).inverse);
                var seatedWith = new HashSet<string>(env.Where(o => Distance(trayAtHome, o, 0.002f) < 0.001f).Select(o => o.name));
                var handNames = new HashSet<string>(inc.Overlay.RightBones[3].GetComponentsInChildren<Renderer>(true).Select(r => r.name));
                var known = new HashSet<string> { "carry", "lean", "return", "release" };

                var rows = new Dictionary<string, (float a, string ap, float b, string bp, float c, string cp, float d, string dp, int n)>();
                int frame = 0;
                while (!inc.Done && frame < 60 * 120)
                {
                    yield return null;
                    frame++;
                    if (frame % 3 != 0) continue;
                    string phase = inc.Phase;
                    bool taken = tray.Taken;
                    var robotRs = robot.GetComponentsInChildren<Renderer>(true).Where(Visible).ToList();
                    var robotM = robotRs.Where(r => r != trayR).Select(World).Where(m => m != null).ToList();
                    var trayM = World(trayR);
                    bool nearShelf = (tray.transform.position - trayHome).magnitude < 0.02f;
                    var envNow = nearShelf ? env.Where(o => !seatedWith.Contains(o.name)).ToList() : env;
                    var all = robotM[0].b; foreach (var m in robotM) all.Encapsulate(m.b); all.Expand(0.1f);
                    var A = Min(robotM, env.Where(o => o.b.Intersects(all)).ToList(), 0.05f);
                    var At = Min(new List<WMesh> { trayM }, envNow, 0.05f);                                   // 托盘 ↔ 环境
                    if (At.d < A.d) A = At;
                    if (!taken)
                    {
                        var A2 = Min(robotRs.Where(r => r != trayR && !handNames.Contains(r.name)).Select(World).Where(m => m != null).ToList(), new List<WMesh> { trayM }, 0.05f);
                        if (A2.d < A.d) A = A2;
                    }
                    var D = Min(robotRs.Where(r => handNames.Contains(r.name)).Select(World).Where(m => m != null).ToList(), new List<WMesh> { trayM }, 0.05f);
                    (float d, string a, string b) B = (0.05f, "-", "-");
                    if (taken) B = Min(new List<WMesh> { trayM }, robotRs.Where(r => r != trayR && !r.name.StartsWith("Arm_R_Jaw")).Select(World).Where(m => m != null).ToList(), 0.05f);
                    var arms = robotRs.Where(r => armNames.Contains(r.name) && !Seat(r.name)).Select(World).Where(m => m != null).ToList();
                    var others = robotRs.Where(r => r != trayR && !armNames.Contains(r.name) && !Seat(r.name)).Select(World).Where(m => m != null).ToList();
                    var C = Min(arms, others, 0.05f);
                    rows.TryGetValue(phase, out var row);
                    if (row.n == 0) row = (0.05f, "-", 0.05f, "-", 0.05f, "-", 0.05f, "-", 0);
                    if (A.d < row.a) { row.a = A.d; row.ap = A.a + " ↔ " + A.b; }
                    if (B.d < row.b) { row.b = B.d; row.bp = B.a + " ↔ " + B.b; }
                    if (C.d < row.c) { row.c = C.d; row.cp = C.a + " ↔ " + C.b; }
                    if (D.d < row.d) { row.d = D.d; row.dp = D.a + " ↔ " + D.b; }
                    row.n++;
                    rows[phase] = row;
                }
                Assert.IsTrue(inc.Done, "演出在 120 秒（模拟）内播完");

                string Mm(float d) => d >= 0.05f ? "≥ 50" : (d * 1000).ToString("F1");
                var md = new StringBuilder("# 第一晚端盘演出：真实播放逐帧间隙（PlayMode 动作探针）\n\n");
                md.AppendLine($"- {DateTime.Now:yyyy-MM-dd HH:mm}，Unity {Application.unityVersion}。按固定步长 1/60 s 播放整段演出，共 {frame} 帧（约 {frame / 60f:F1} s），每 3 帧量一次真实网格。程序播放，不是真人观看。");
                md.AppendLine("- 包含 Animator 的身体浮动 / 引擎摆动（Idle_Hover）、两臂权重过渡、上爪张开、放盘滑出、托盘落下、倾斜。");
                md.AppendLine($"- A：七号（+ 手上的托盘）↔ 维修座 / 工作台；托盘离手后，七号（不含右手）↔ 托盘也算进 A；托盘离原位 20 mm 内不计托盘 ↔ 它在原位时本来就贴着的件（{string.Join("、", seatedWith)}）。");
                md.AppendLine("- B：手上的托盘 ↔ 七号（不含右手两爪）。C：两臂 ↔ 七号其它部分（不含两臂自身根座的铰接面）。D：右手（腕以下）↔ 托盘。");
                md.AppendLine("- 阶段：carry 端着停 / lean 左倾回正 / return 移到托盘架、降到离原位 8 mm、张上爪 / release 手滑出、托盘落下 / away 退开升高回到 T1 / arms_out 两臂交还 Animator / descend 落回悬停位。\n");
                md.AppendLine("| 阶段 | 采样 | A 最小 | A 最近的两件 | B 最小 | C 最小 | C 最近的两件 | D 右手 ↔ 托盘 | D 最近的两件 |\n|---|---|---|---|---|---|---|---|---|");
                foreach (var kv in rows)
                    md.AppendLine($"| {kv.Key} | {kv.Value.n} | {Mm(kv.Value.a)} mm | {kv.Value.ap} | {(kv.Value.b >= 0.05f && kv.Value.bp == "-" ? "—" : Mm(kv.Value.b) + " mm")} | {Mm(kv.Value.c)} mm | {kv.Value.cp} | {Mm(kv.Value.d)} mm{(kv.Value.d <= 0f && known.Contains(kv.Key) ? "（已知：审计握法下爪穿进盘壁）" : "")} | {kv.Value.dp} |");
                Directory.CreateDirectory("Docs/Integration/TwoNightSlice");
                File.WriteAllText("Docs/Integration/TwoNightSlice/incident_motion_play.md", md.ToString(), new UTF8Encoding(false));

                var bad = rows.Where(kv => kv.Value.a <= 0f || kv.Value.b <= 0f || kv.Value.c <= 0f || kv.Value.d <= 0f && !known.Contains(kv.Key)).Select(kv => kv.Key).ToList();
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
