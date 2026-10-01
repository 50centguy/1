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
    /// <summary>首单可玩原型：用真实点选（含遮挡）走完整单；拆下路径不穿过七号的其它零件；运行无控制台错误。</summary>
    public class FirstOrderPlayTests
    {
        const string ScenePath = "Assets/BorderRepair/FirstOrder/Scenes/Unit07FirstOrder_Test.unity";
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

        [UnityTest]
        public IEnumerator FullOrder_ThroughRealScreenClicks()
        {
            var driver = new FirstOrderAcceptanceDriver(flow, input) { Timeout = 30f };
            yield return driver.RunFullOrder();
            var failed = driver.Records.Where(r => !r.pass).Select(r => $"#{r.index} {r.label}：{r.message}").ToList();
            Assert.IsEmpty(failed, string.Join("\n", failed));
            Assert.AreEqual(FoStep.Done, flow.Step);
            Assert.IsTrue(flow.RetestPassed, flow.RetestDetail);
            // 零件去向
            Assert.AreEqual(PartLocation.Installed, flow.Cover.Location);
            Assert.AreEqual(PartLocation.OnBench, flow.Bearing.Location, "旧轴承留在工作台");
            Assert.AreEqual(PartLocation.Installed, flow.NewBearing.Location, "新轴承（占位）装在轴承位");
            Assert.AreEqual(flow.OldTrayZone.landing.position.x, flow.Bearing.WorldBounds().center.x, 0.01f);
            Assert.AreEqual(flow.Bearing.HomeParent, flow.NewBearing.transform.parent, "新轴承挂在旧轴承原来的父骨骼下");
            Assert.AreEqual(flow.Cover.HomeParent, flow.Cover.transform.parent, "上盖总成装回原骨骼");
            Assert.IsTrue(flow.Cover.members.All(m => m.parent == flow.Cover.HomeParent), "上盖总成成员各自回到原父节点");
            Assert.Greater(flow.RejectedCount, 0);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// 拆下路径的几何检查（上盖总成沿引擎轴线抬起 10 cm、旧轴承抬起 7 cm，各分 20 段）：
        /// 移动件用它自己的精确网格碰撞（上盖是空心罩壳，不能用包围盒，否则罩在里面的风道、轴承都会算成“碰撞”），
        /// 周围七号零件临时加凸包碰撞（对实心小件是偏保守的近似），用 Physics.ComputePenetration 判断是否穿入。
        /// 轴承与转轴同轴套装，转轴不计。结果写进日志，有穿入就失败。
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
            var own = new HashSet<Transform>(p.members) { p.transform };
            var swept = p.WorldBounds();
            swept.Encapsulate(new Bounds(swept.center + dir * dist, swept.size));
            swept.Expand(0.01f);
            // 周围的七号零件：临时凸包
            var hulls = new List<(MeshCollider hull, string name)>();
            foreach (var mf in robotRoot.GetComponentsInChildren<MeshFilter>())
            {
                if (own.Contains(mf.transform) || ignore.Contains(mf.name) || mf.GetComponent<FirstOrderPart>() == p) continue;
                var r = mf.GetComponent<Renderer>();
                if (r == null || !r.bounds.Intersects(swept)) continue;
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
