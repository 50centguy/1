using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace BorderRepair.FirstOrder.Tests
{
    /// <summary>
    /// 首单可玩原型（接入七号故障美术包）：用真实点选（含遮挡）走完整单；只清理就复测、没装好就通电都被拒绝；
    /// 新旧轴承不会同时出现在轴承位；搬动的零件不瞬移、搬运途中和放下后不穿模；拆下路径不穿过七号的其它零件；运行无控制台错误。
    /// 这些都是程序点击，不是真人鼠标试玩。
    /// </summary>
    [TestFixture("Assets/BorderRepair/FirstOrder/Scenes/Unit07FirstOrder_Test.unity")]
    [TestFixture("Assets/BorderRepair/FirstOrder/Scenes/LayoutAB/Unit07FirstOrder_LayoutA.unity")]
    [TestFixture("Assets/BorderRepair/FirstOrder/Scenes/LayoutAB/Unit07FirstOrder_LayoutB.unity")]
    public class FirstOrderPlayTests
    {
        readonly string ScenePath;
        public FirstOrderPlayTests(string scenePath) { ScenePath = scenePath; }   // 正式首单测试场景 + 布局 A/B 两个测试副本
        FirstOrderFlow flow;
        FirstOrderInput input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
            yield return null;
            flow = Object.FindFirstObjectByType<FirstOrderFlow>();
            input = Object.FindFirstObjectByType<FirstOrderInput>();
            input.enabled = false;   // 不读真实鼠标；验收驱动按屏幕坐标点
            yield return null;
        }

        [TearDown]
        public void TearDown() => Time.captureDeltaTime = 0f;

        static void AssertAllPassed(FirstOrderAcceptanceDriver driver)
        {
            var failed = driver.Records.Where(r => !r.pass).Select(r => $"#{r.index} {r.label}：{r.message}").ToList();
            Assert.IsEmpty(failed, string.Join("\n", failed));
        }

        [UnityTest]
        public IEnumerator FullOrder_ThroughRealScreenClicks()
        {
            var driver = new FirstOrderAcceptanceDriver(flow, input) { Timeout = 30f };
            yield return driver.RunFullOrder();
            AssertAllPassed(driver);
            Assert.AreEqual(FoStep.Done, flow.Step);
            Assert.IsTrue(flow.RetestPassed, flow.RetestDetail);
            // 零件去向
            Assert.AreEqual(PartLocation.Installed, flow.Cover.Location);
            Assert.AreEqual(PartLocation.Cleared, flow.Clog.Location, "进气口已清理");
            Assert.AreEqual(PartLocation.OnBench, flow.Bearing.Location, "旧轴承留在工作台");
            Assert.AreEqual(PartLocation.Installed, flow.NewBearing.Location, "新轴承装在轴承位");
            Assert.AreEqual(flow.OldTrayZone.landing.position.x, flow.Bearing.WorldBounds().center.x, 0.01f);
            Assert.AreEqual(flow.Bearing.HomeParent, flow.NewBearing.transform.parent, "新轴承挂在旧轴承原来的父骨骼下");
            var home = flow.Bearing.HomeWorldPose();
            Assert.Less(Vector3.Distance(home.position, flow.NewBearing.transform.position), 1e-4f, "新轴承装在旧轴承原位");
            Assert.Less(Quaternion.Angle(home.rotation, flow.NewBearing.transform.rotation), 0.01f, "新轴承朝向与旧轴承原位一致");
            Assert.AreEqual(flow.Cover.HomeParent, flow.Cover.transform.parent, "上盖总成装回原骨骼");
            Assert.IsTrue(flow.Cover.members.All(m => m.parent == flow.Cover.HomeParent), "上盖总成成员各自回到原父节点");
            Assert.IsFalse(flow.OriginalBearingRenderer.enabled, "原轴承渲染器一直关着");
            Assert.Greater(flow.RejectedCount, 0);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>只清理进气口就尝试复测（松开夹具 / 通电）：都被拒绝，提示还有磨损轴承；七号留在维修座上、不通电，零件状态不变。</summary>
        [UnityTest]
        public IEnumerator CleanOnly_ThenRetestAttempt_IsRefused()
        {
            var driver = new FirstOrderAcceptanceDriver(flow, input) { Timeout = 30f };
            yield return driver.RunCleanOnlyAttempt();
            AssertAllPassed(driver);
            var refused = driver.Records.Where(r => r.label.StartsWith("拒绝：只清理")).ToList();
            Assert.AreEqual(2, refused.Count);
            foreach (var r in refused)
            {
                Assert.IsFalse(r.accepted, r.label);
                StringAssert.Contains("轴承", r.message, "拒绝理由要指出轴承还没处理");
                Assert.AreEqual("RotorsStopped", r.dockState, "没有松开夹具、没有通电");
            }
            Assert.IsTrue(flow.CleanedOnly);
            Assert.IsFalse(flow.RetestPassed);
            Assert.Less(flow.Step, FoStep.PowerOn);
            Assert.AreEqual(PartLocation.Installed, flow.Bearing.Location, "磨损轴承还在原位");
            Assert.AreEqual(PartLocation.Stored, flow.NewBearing.Location);
            for (int i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(BorderRepair.Dock.DockState.RotorsStopped, flow.Dock.State, "等一会儿也没有自己开始复测");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// 没装好就通电：轴承拆下没装新的、新轴承装了但上盖没装回、锁扣都没扣 / 只扣一个、进气口没清理（这一轮装回后才清理）——每次通电都被拒绝且不通电；全部做完才能通电。
        /// </summary>
        [UnityTest]
        public IEnumerator PowerOn_BeforeFullyInstalled_IsRefused()
        {
            var driver = new FirstOrderAcceptanceDriver(flow, input) { Timeout = 30f };
            yield return driver.RunFullOrder(cleanEarly: false);
            AssertAllPassed(driver);
            var powerRefusals = driver.Records.Where(r => r.label.StartsWith("拒绝：") && r.label.Contains("通电") && !r.label.Contains("悬停")).ToList();
            var expect = new Dictionary<string, string>
            {
                ["拒绝：维修中途通电"] = "进气口", ["拒绝：新轴承还没装就通电"] = "新轴承", ["拒绝：新轴承装上了但上盖没装回就通电"] = "上盖",
                ["拒绝：锁扣没扣回就通电"] = "锁扣", ["拒绝：只扣回一个锁扣就通电"] = "锁扣", ["拒绝：进气口没清理就通电"] = "进气口",
            };
            CollectionAssert.IsSubsetOf(expect.Keys, powerRefusals.Select(r => r.label));
            foreach (var r in powerRefusals.Where(r => expect.ContainsKey(r.label)))
            {
                Assert.IsFalse(r.accepted, r.label);
                StringAssert.Contains(expect[r.label], r.message, r.label);
                Assert.AreEqual("RotorsStopped", r.dockState, r.label + "：没有通电");
            }
            Assert.IsTrue(flow.RetestPassed, flow.RetestDetail);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>逐帧核对：原轴承渲染器始终关着；轴承位上最多只有一个轴承；磨损件和新件的显示范围从不重叠。</summary>
        [UnityTest]
        public IEnumerator OldAndNewBearings_NeverShownTogether()
        {
            Time.captureDeltaTime = 1f / 60f;
            var mon = new GameObject("FaultKitMonitor").AddComponent<FirstOrderMotionMonitor>();
            mon.flow = flow;
            var driver = new FirstOrderAcceptanceDriver(flow, input) { Timeout = 60f };
            yield return driver.RunFullOrder();
            AssertAllPassed(driver);
            Debug.Log($"[FirstOrderTest] 新旧轴承逐帧核对：{mon.frames} 帧，同时在轴承位的最多 {mon.maxAtSeat} 个；违规 {mon.bearingViolations.Count} 条");
            Assert.Greater(mon.frames, 500);
            Assert.IsEmpty(mon.bearingViolations.Take(10), string.Join("\n", mon.bearingViolations.Take(10)));
            Assert.AreEqual(1, mon.maxAtSeat);
        }

        /// <summary>
        /// 逐帧核对搬动的零件（锁扣、上盖总成、进气口堵塞、旧轴承、新轴承）：每帧位移 ≤ 8 cm、转角 ≤ 15°（固定 60 帧/秒），不瞬移；
        /// 搬运途中（竖直抬起、平移并转向、落到工作台），每个显示中的网格做凸包，和场景里别的实体碰撞求穿入深度（先用有向包围盒粗筛），≤ 0.5 mm 才算不碰；
        /// 放下后零件的位姿正好是构建时按网格顶点算好的落点位姿（落点本身不穿模由 EditMode 的 BenchPlacements_DoNotPenetrate 核对——
        /// Play 模式下工作台网格被静态合批，读到的是合并网格，不能拿来做精确碰撞）。
        /// </summary>
        [UnityTest]
        public IEnumerator MovedParts_NoTeleport_NoClipping()
        {
            Time.captureDeltaTime = 1f / 60f;
            var boxPose = new Pose(flow.NewBearing.transform.position, flow.NewBearing.transform.rotation);
            Assert.Less(Vector3.Angle(BearingAxis(flow.NewBearing), Vector3.up), 1f, "新轴承平放（转轴竖直）");

            var mon = new GameObject("FaultKitMonitor").AddComponent<FirstOrderMotionMonitor>();
            mon.flow = flow;
            var driver = new FirstOrderAcceptanceDriver(flow, input) { Timeout = 60f };
            var it = driver.RunFullOrder();
            yield return RunUntil(it, FoStep.FetchNewBearing);
            AssertAtLanding(flow.Cover, flow.MatZone, "翻面的上盖总成");
            AssertAtLanding(flow.Bearing, flow.OldTrayZone, "旧轴承");
            Assert.Less(Vector3.Distance(boxPose.position, flow.NewBearing.transform.position), 1e-5f, "新轴承还在轴承盒上没动");
            var n = flow.CoverLabelNormal;
            Debug.Log($"[FirstOrderTest] 保养标记法线与竖直向上夹角 {Vector3.Angle(n, Vector3.up):F1}°");
            Assert.Less(Vector3.Angle(n, Vector3.up), 25f, "上盖翻过来，保养标记朝上");
            Assert.Less(Vector3.Angle(BearingAxis(flow.Bearing), Vector3.up), 1f, "旧轴承平放（转轴竖直）");
            while (it.MoveNext()) yield return it.Current;
            AssertAllPassed(driver);
            Debug.Log($"[FirstOrderTest] 逐帧：{mon.frames} 帧，最大单帧位移 {mon.maxStep * 1000:F1} mm（{mon.maxStepWhere}），最大单帧转角 {mon.maxTurn:F2}°（{mon.maxTurnWhere}）；" +
                      $"搬运途中检查了 {mon.travelFrames} 帧（{string.Join("；", mon.carries)}），包围盒粗筛有重叠的 {mon.travelBroad} 次，凸包精查穿入 {mon.travelHits.Count} 处" +
                      (mon.travelHits.Count > 0 ? "：" + string.Join("；", mon.travelHits.Take(8)) : "") + (mon.travelNear.Count > 0 ? "；粗筛重叠但没穿入的对象：" + string.Join("、", mon.travelNear) : "") +
                      "；没有碰撞、只能比包围盒、包围盒有交叠的物件：" + (mon.aabbOnly.Count == 0 ? "无" : string.Join("、", mon.aabbOnly)));
            Assert.IsEmpty(mon.jumpViolations.Take(10), string.Join("\n", mon.jumpViolations.Take(10)));
            Assert.Greater(mon.travelFrames, 100, "搬运途中的帧确实检查到了");
            Assert.IsEmpty(mon.travelHits.Take(10), string.Join("\n", mon.travelHits.Take(10)));
        }

        static void AssertAtLanding(FirstOrderPart p, FirstOrderDropZone z, string what)
        {
            Assert.IsTrue(z.orientPart, z.name);
            var pos = z.landing.position + z.landingOffset;
            Debug.Log($"[FirstOrderTest] {what}落点：位置差 {Vector3.Distance(pos, p.transform.position) * 1000:F3} mm，朝向差 {Quaternion.Angle(z.landingRotation, p.transform.rotation):F3}°");
            Assert.Less(Vector3.Distance(pos, p.transform.position), 1e-4f, what + "落在构建时算好的位置");
            Assert.Less(Quaternion.Angle(z.landingRotation, p.transform.rotation), 0.05f, what + "转到构建时算好的朝向");
        }
        static Vector3 BearingAxis(FirstOrderPart p)
        {
            var mf = p.Renderers().Where(r => r.enabled).Select(r => r.GetComponent<MeshFilter>()).First(m => m.sharedMesh.name.Contains("BearingTop_L_"));
            var a = FirstOrderGeometry.ThinAxisWorld(mf);
            return a.y < 0f ? -a : a;
        }

        /// <summary>
        /// 拆下路径的几何检查（上盖总成沿引擎轴线抬起 10 cm、旧轴承抬起 7 cm，各分 20 段）：
        /// 移动件用它自己的精确网格碰撞（上盖是空心罩壳，不能用包围盒，否则罩在里面的风道、轴承都会算成“碰撞”），
        /// 周围七号零件临时加凸包碰撞（对实心小件是偏保守的近似），用 Physics.ComputePenetration 判断是否穿入。
        /// 轴承与转轴同轴套装，转轴不计。挂在移动件下面的美术件（堵塞、保养标记、磨损件）跟着一起动，不算“周围零件”。结果写进日志，有穿入就失败。
        /// </summary>
        [UnityTest]
        public IEnumerator RemovalPaths_DoNotPassThroughOtherRobotParts()
        {
            var driver = new FirstOrderAcceptanceDriver(flow, input) { Timeout = 30f };
            var it = driver.RunFullOrder();
            yield return RunUntil(it, FoStep.RemoveCover);          // 两个锁扣都扳开，上盖还在原位
            var coverHits = Sweep(flow.Cover, flow.EngineLHinge.up, 0.10f, new HashSet<string>());
            Debug.Log("[FirstOrderTest] 上盖总成抬起 10 cm 途中穿入：" + (coverHits.Count == 0 ? "无" : string.Join(", ", coverHits)));
            yield return RunUntil(it, FoStep.RemoveBearing);        // 上盖已放到工作台，轴承已定位
            var bearingHits = Sweep(flow.Bearing, flow.EngineLHinge.up, 0.07f, new HashSet<string> { "Engine_Shaft_L" });
            Debug.Log("[FirstOrderTest] 旧轴承抬起 7 cm 途中穿入（转轴同轴不计）：" + (bearingHits.Count == 0 ? "无" : string.Join(", ", bearingHits)));
            Assert.IsEmpty(coverHits, "上盖总成取下路径");
            Assert.IsEmpty(bearingHits, "旧轴承取下路径");
        }

        IEnumerator RunUntil(IEnumerator it, FoStep target)
        {
            while (flow.Step != target && it.MoveNext())
            {
                if (it.Current is IEnumerator nested) { while (nested.MoveNext()) yield return nested.Current; }
                else yield return it.Current;
            }
        }

        static List<string> Sweep(FirstOrderPart p, Vector3 dir, float dist, HashSet<string> ignore)
        {
            Physics.SyncTransforms();
            var robotRoot = (p.HomeParent != null ? p.HomeParent : p.transform).root;
            var moving = new List<MeshCollider>();
            foreach (var t in p.members.Append(p.transform)) moving.AddRange(t.GetComponents<MeshCollider>().Where(c => !c.convex));
            var own = new HashSet<Transform>(p.members.Append(p.transform).SelectMany(t => t.GetComponentsInChildren<Transform>(true)));
            var swept = p.WorldBounds();
            swept.Encapsulate(new Bounds(swept.center + dir * dist, swept.size));
            swept.Expand(0.01f);
            // 周围的七号零件：临时凸包（只算显示中的网格；原轴承渲染器关着，由挂在它下面的磨损件代表）
            var hulls = new List<(MeshCollider hull, string name)>();
            foreach (var mf in robotRoot.GetComponentsInChildren<MeshFilter>())
            {
                if (own.Contains(mf.transform) || ignore.Contains(mf.name) || mf.GetComponent<FirstOrderPart>() == p) continue;
                var r = mf.GetComponent<Renderer>();
                if (r == null || !r.enabled || !r.bounds.Intersects(swept)) continue;
                var go = new GameObject("TmpHull_" + mf.name);
                go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                go.transform.localScale = mf.transform.lossyScale;
                var h = go.AddComponent<MeshCollider>();
                h.convex = true;
                h.sharedMesh = mf.sharedMesh;
                hulls.Add((h, mf.name));
            }
            Physics.SyncTransforms();
            bool Pen(MeshCollider hull, MeshCollider m, Vector3 off, out float depth) =>
                Physics.ComputePenetration(hull, hull.transform.position, hull.transform.rotation, m, m.transform.position + off, m.transform.rotation, out _, out depth) && depth > 0.0005f;
            // 在原位就已经“穿入”的：是凸包把空心壳体填实造成的假象（例如上盖罩在下壳上沿外面），凸包方法判断不了，单独列出
            var undecidable = hulls.Where(h => moving.Any(m => Pen(h.hull, m, Vector3.zero, out _))).Select(h => h.name).ToHashSet();
            var hits = new HashSet<string>();
            for (int i = 1; i <= 20; i++)
            {
                var off = dir * dist * i / 20f;
                foreach (var m in moving)
                    foreach (var (hull, name) in hulls)
                        if (!undecidable.Contains(name) && Pen(hull, m, off, out var depth))
                            hits.Add($"{name}（{m.name} 抬起 {off.magnitude * 1000:F0} mm 时穿入 {depth * 1000:F1} mm）");
            }
            foreach (var (hull, _) in hulls) Object.Destroy(hull.gameObject);
            Debug.Log($"[FirstOrderTest] {p.name}：检查了 {hulls.Count} 个邻近零件；原位就与凸包重叠、凸包方法判断不了的：{(undecidable.Count == 0 ? "无" : string.Join(", ", undecidable))}");
            // 同一个零件只报第一次
            return hits.GroupBy(h => h.Split('（')[0]).Select(g => g.First()).OrderBy(h => h).ToList();
        }

        [UnityTest]
        public IEnumerator Scene_RunsWithoutConsoleErrors()
        {
            input.enabled = true;
            for (int i = 0; i < 60; i++) yield return null;
            LogAssert.NoUnexpectedReceived();
        }
    }

}
