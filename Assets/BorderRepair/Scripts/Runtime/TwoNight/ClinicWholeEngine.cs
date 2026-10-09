using System.Collections.Generic;
using System.Linq;
using System.Text;
using BorderRepair.FirstOrder;
using UnityEngine;

namespace BorderRepair.TwoNight
{
    /// <summary>
    /// 统一诊所第二晚：开启整引擎维修（FirstOrderFlow.EnableWholeEngineMode），运行时搭好三样东西：
    /// 1) 左引擎内侧连接键（成员：螺栓、辅助线缆插头）做成可点的部件，外加一个稍大的点选盒（它很小、夹在引擎和机身之间）；
    /// 2) 维修台上放引擎的落点：在台面上找一块放得下整台引擎（按竖直轴任意朝向的占地）的空位——避开台上的物件、
    ///    上盖 / 旧轴承落点、新轴承，离操作垫最近；
    /// 3) 七号左侧安装位的点选盒（只在抱着修好的引擎时打开）。
    /// 落点搜索用包围盒近似，不是网格级碰撞检查。
    /// </summary>
    public static class ClinicWholeEngine
    {
        public const string EnginePartId = "engine_l";

        public static string Enable(FirstOrderFlow flow, Transform workAnchor, Vector3 standPoint)
        {
            var log = new StringBuilder();
            var hinge = flow.EngineLHinge;
            Transform Find(string n) => hinge.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
            var key = Find("ConnectionKey_L"); var bolt = Find("ConnectionKey_Bolt_L"); var plug = Find("AuxCable_L_PlugEngine");
            if (key == null) return "找不到 ConnectionKey_L，整引擎维修没有开启";

            // ---- 1) 连接键
            var connector = key.gameObject.AddComponent<FirstOrderPart>();
            connector.partId = "engine_l_connector";
            connector.displayName = "左引擎连接键 / 辅助线缆";
            connector.realPath = "Robot_Rig/Root/Body/Engine_L_Hinge/ConnectionKey_L（+ 螺栓、辅助线缆插头）";
            connector.members = new List<Transform> { bolt, plug }.Where(t => t != null).ToList();
            connector.LocationDetail = "左引擎内侧（已接好）";
            var cb = Bounds(new[] { key, bolt, plug }.Where(t => t != null));
            var proxy = new GameObject("ClinicConnectorClickProxy");
            proxy.transform.SetParent(key, false);
            proxy.transform.SetPositionAndRotation(cb.center, Quaternion.identity);
            var pc = proxy.AddComponent<BoxCollider>(); pc.isTrigger = true;
            pc.size = Abs(proxy.transform.InverseTransformVector(cb.size + Vector3.one * .03f));
            proxy.AddComponent<FirstOrderMember>().owner = connector;

            // ---- 2) 维修台落点（先建，开启整引擎模式后按放上台的朝向找空位）
            var benchZoneGo = new GameObject("ClinicEngineBenchSpot（维修台：放左引擎）");
            var landing = new GameObject("Landing").transform; landing.SetParent(benchZoneGo.transform, false);
            var bz = benchZoneGo.AddComponent<BoxCollider>(); bz.isTrigger = true;
            var benchZone = benchZoneGo.AddComponent<FirstOrderDropZone>();
            benchZone.zoneId = "engine_bench"; benchZone.displayName = "维修台（放左引擎）";
            benchZone.acceptsPartId = EnginePartId; benchZone.landing = landing;
            var eb = flow.EngineBounds();

            // ---- 3) 七号左侧安装位
            var mountGo = new GameObject("ClinicEngineMount（七号左引擎安装位）");
            mountGo.transform.SetParent(hinge.parent, false);
            mountGo.transform.SetPositionAndRotation(eb.center, Quaternion.identity);
            var mz = mountGo.AddComponent<BoxCollider>(); mz.isTrigger = true;
            mz.size = Abs(mountGo.transform.InverseTransformVector(eb.size + Vector3.one * .04f));
            var mount = mountGo.AddComponent<FirstOrderDropZone>();
            mount.zoneId = "engine_mount"; mount.displayName = "七号左引擎安装位";
            mount.benchObjectPath = "Robot_Rig/Root/Body"; mount.acceptsPartId = EnginePartId; mount.landing = mountGo.transform;

            // ---- 进气口堵塞：进气口在引擎顶面（在七号上、放到台上都是直立的），玩家站在台边斜着往下看，视线先碰到进气唇口。
            //      在堵塞上加一个罩住进气唇口口沿、顶部高出唇口 1 cm 的点选盒：从上方斜看时视线先进它、后碰唇口（清理后随堵塞的其他碰撞一起关掉）
            var lip = Find("Engine_IntakeLip_L");
            if (flow.Clog != null && flow.Clog.GetComponent<BoxCollider>() is BoxCollider clogBox && lip != null && lip.GetComponent<MeshFilter>() is MeshFilter lipMf)
            {
                var ct = flow.Clog.transform;
                var outward = ct.InverseTransformDirection(Vector3.up);
                int k = Mathf.Abs(outward.x) >= Mathf.Abs(outward.y) && Mathf.Abs(outward.x) >= Mathf.Abs(outward.z) ? 0 : Mathf.Abs(outward.y) >= Mathf.Abs(outward.z) ? 1 : 2;
                float sign = Mathf.Sign(outward[k]), scale = Mathf.Abs(ct.lossyScale[k]) > 1e-6f ? Mathf.Abs(ct.lossyScale[k]) : 1f;
                // 唇口网格包围盒换到堵塞本地
                Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
                var mb = lipMf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var v = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1));
                    var p = ct.InverseTransformPoint(lip.TransformPoint(v));
                    lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
                }
                // 竖直方向：从堵塞点选盒的顶面到唇口顶面再往上 1 cm
                float clogTop = clogBox.center[k] + sign * clogBox.size[k] * .5f;
                float lipTop = (sign > 0 ? hi[k] : lo[k]) + sign * .01f / scale;
                var mouth = flow.Clog.gameObject.AddComponent<BoxCollider>(); mouth.isTrigger = true;
                var c = (lo + hi) * .5f; var s = hi - lo;
                c[k] = (clogTop + lipTop) * .5f; s[k] = Mathf.Abs(lipTop - clogTop);
                mouth.center = c; mouth.size = s;
            }

            flow.EnableWholeEngineMode(connector, benchZone, mount, standPoint);

            // ---- 维修台上找空位
            var bench = workAnchor.GetComponentInParent<Renderer>();
            float top = bench != null ? bench.bounds.max.y : workAnchor.position.y;
            var center = flow.MatZone.landing.position; center.y = top;
            var face = Vector3.ProjectOnPlane(standPoint - center, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, face).normalized;
            var size = flow.EngineBenchSize(center);                                    // 按放上台的朝向（锁扣朝玩家）算占地
            float hx = size.x * .5f + .01f, hz = size.z * .5f + .01f, height = size.y;
            var robotRoot = flow.Dock.RobotRoot;
            var markers = new HashSet<Renderer>(new[] { flow.MatZone.marker, flow.OldTrayZone.marker }.Where(r => r != null));
            var ignoreNames = new HashSet<string> { "Bench_Top", "Bench_TopEdge", "Bench_Mat", "Bench_Frame" };
            var obstacles = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && !markers.Contains(r) && !ignoreNames.Contains(r.name) &&
                            r.GetComponent<TextMesh>() == null && !r.transform.IsChildOf(robotRoot) && !r.transform.IsChildOf(hinge) &&
                            r.bounds.max.y > top + .003f && r.bounds.min.y < top + height + .05f &&
                            r.bounds.size.x < 2f && r.bounds.size.z < 2f)                     // 房间外壳（墙面管线、墙裙）包围盒罩住整个房间，不算台上的物件
                .ToList();
            // 矮物件（托架块、平放的纸和记录本，高出台面不到 6 cm）可以架在上面；更高的才挡路
            const float LowLimit = .06f;
            var supports = obstacles.Where(r => r.bounds.max.y <= top + LowLimit).Select(r => r.bounds).ToList();
            var tall = obstacles.Where(r => r.bounds.max.y > top + LowLimit).Select(r => r.bounds).ToList();
            tall.Add(Expand(ZoneBounds(flow.OldTrayZone), .05f));
            var benchArea = bench != null ? bench.bounds : new Bounds(center, Vector3.one);
            // 在台面上找一块 2hx × 2hz、高 h 的空位：离 near 最近、尽量平放；extra 是额外要避开的
            Vector3? FindSpot(float fx, float fz, float h, List<Bounds> extra, Vector3 near, out float support)
            {
                Vector3? found = null; float bestCost = float.MaxValue; support = top;
                for (float a2 = -.75f; a2 <= .75f; a2 += .02f)
                    for (float d = -.25f; d <= .25f; d += .02f)
                    {
                        var c = center + right * a2 + face * d;
                        if (c.x - fx < benchArea.min.x || c.x + fx > benchArea.max.x || c.z - fz < benchArea.min.z || c.z + fz > benchArea.max.z) continue;
                        var box = new Bounds(c + Vector3.up * (LowLimit + h * .5f), new Vector3(2 * fx, h, 2 * fz));
                        if (tall.Any(o => o.Intersects(box)) || extra.Any(o => o.Intersects(box))) continue;
                        var foot = new Bounds(c + Vector3.up * LowLimit * .5f, new Vector3(2 * fx, LowLimit + .01f, 2 * fz));
                        float sup = supports.Where(o => o.Intersects(foot)).Select(o => o.max.y).DefaultIfEmpty(top).Max();
                        float cost = Vector3.Distance(new Vector3(c.x, 0f, c.z), new Vector3(near.x, 0f, near.z)) + 3f * (sup - top);
                        if (cost < bestCost) { bestCost = cost; found = c; support = sup; }
                    }
                return found;
            }

            // 引擎：操作垫中间优先（可以架在托架块上）
            var engineSpot = FindSpot(hx, hz, height, new List<Bounds>(), center, out float engineSupport);
            var spot = engineSpot ?? center;
            spot.y = engineSpot != null ? engineSupport : top;
            log.AppendLine($"台面 {benchArea.min:F2}–{benchArea.max:F2}，顶面 {top:F3}；操作垫中心 {center:F3}；引擎（放上台的朝向）占地 {2 * hx:F2} × {2 * hz:F2} m、高 {height:F2} m");
            log.AppendLine(engineSpot != null ? $"引擎放在 {spot:F3}（离操作垫中心 {Vector3.Distance(new Vector3(spot.x, top, spot.z), center):F2} m，底面架在 {spot.y - top:F3} m 高的矮物件上）"
                                              : $"维修台上找不到放引擎的空位，退回操作垫中心 {spot:F3}");
            benchZoneGo.transform.position = spot;
            bz.center = Vector3.up * .1f; bz.size = new Vector3(2 * hx, .2f, 2 * hz);

            // 上盖：翻面平放的占地（上盖三个尺寸里较大的两个），避开引擎；正上方要空着（搬运最后是竖直落下，不能有台灯、架子）
            var cs = flow.Cover.WorldBounds().size; var dims = new[] { cs.x, cs.y, cs.z }.OrderByDescending(v => v).ToArray();
            float cf = dims[0] * .5f + .01f;
            var engineBox = new Bounds(spot + Vector3.up * .5f, new Vector3(2 * hx + .04f, 1f, 2 * hz + .04f));
            var overhead = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.GetComponent<TextMesh>() == null && !r.transform.IsChildOf(robotRoot) &&
                            r.bounds.min.y > top + LowLimit && r.bounds.min.y < top + .9f && r.bounds.size.x < 2f && r.bounds.size.z < 2f)
                .Select(r => new Bounds(new Vector3(r.bounds.center.x, top + .45f, r.bounds.center.z), new Vector3(r.bounds.size.x, .9f, r.bounds.size.z))).ToList();
            var coverSpot = FindSpot(cf, cf, dims[2], overhead.Append(engineBox).ToList(), spot, out float coverSupport);
            if (coverSpot != null)
            {
                var zone = flow.MatZone;
                var target = new Vector3(coverSpot.Value.x, coverSupport, coverSpot.Value.z);
                var delta = target - new Vector3(zone.landing.position.x, top, zone.landing.position.z);
                zone.transform.position += delta;
                if (zone.landing != null && !zone.landing.IsChildOf(zone.transform)) zone.landing.position += delta;
                zone.useApproach = false;                                                       // 原来绕开台灯低空钻入；新位置正上方是空的
                log.AppendLine($"上盖翻面的落点挪到 {target:F3}（引擎占了操作垫中间；上方无遮挡，不再低空钻入）");
            }
            else log.AppendLine("台上找不到放上盖的空位，上盖落点不动（会和引擎重叠）");
            benchZone.benchObjectPath = bench != null ? bench.name : "workbench";

            log.Append("整引擎维修已开启：拔线松键 → 抱下左引擎 → 放到维修台修 → 抱回装上 → 接回线缆 → 通电复测");
            return log.ToString();
        }

        static Bounds Bounds(IEnumerable<Transform> ts)
        {
            var rs = ts.SelectMany(t => t.GetComponentsInChildren<Renderer>()).ToArray();
            if (rs.Length == 0) return new Bounds(ts.First().position, Vector3.one * .03f);
            var b = rs[0].bounds; foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds); return b;
        }

        static Bounds ZoneBounds(FirstOrderDropZone z)
        {
            var c = z.GetComponent<Collider>();
            return c != null ? c.bounds : new Bounds(z.landing.position, Vector3.one * .1f);
        }

        static Bounds Expand(Bounds b, float r) { b.Expand(new Vector3(2 * r, 0f, 2 * r)); return b; }
        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }
}
