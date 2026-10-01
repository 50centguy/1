using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Dock;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.FirstOrder.EditorTools.LayoutAB
{
    /// <summary>
    /// 布局 B 摆位搜索（只读，不保存场景）：打开首单测试场景（A），在内存里移动“维修架 + 七号”，对每个候选摆位：
    /// 1) 平面占地：维修架各部件、七号的包围盒随摆位旋转，和工作台 / 房间物件、墙、地面范围不能重叠；
    /// 2) 玩家站位：在空地网格里找站位（人体半径 0.30 m），要求够得着（左引擎 ≤ 0.80 m、开关 / 夹具握把 ≤ 0.95 m、操作垫和托盘 ≤ 1.05 m）；
    /// 3) 按首单动作顺序算玩家要转的角度：大于 90° 的转向次数、总转角；维修架要在玩家正前方偏左、工作台在偏右；
    /// 4) 机器人抵达 / 离开通道：从七号位置朝房间敞开一侧，1.05 m 宽的通道里没有东西；
    /// 5) 排名靠前的候选再用真实碰撞做点选可见性：从玩家眼睛（1.60 m）按真实点选规则（含遮挡）能点到关键部件的比例。
    /// A 布局维修架不动，只按同样的规则找最佳站位，作为对照。
    /// </summary>
    public static class LayoutPlanner
    {
        public const float EyeHeight = 1.60f, BodyRadius = 0.30f;

        // ---- 场景里的关键对象（A 世界坐标）
        public class Targets
        {
            public Vector3 coverL, clog, latchOuter, latchRear, bearing, lever, gripL, gripR, robot, coverR;
            public Vector3 mat, label, tray, box;
        }

        /// <summary>首单动作顺序（玩家依次看 / 点的目标）。</summary>
        public static readonly string[] Sequence =
        {
            "grip", "grip", "lever", "coverL", "clog", "latchOuter", "latchRear", "coverL", "mat", "label", "bearing", "bearing",
            "tray", "compare", "box", "mat", "latchOuter", "latchRear", "lever", "grip", "robot",
        };

        public static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["grip"] = "夹具握把", ["lever"] = "断电开关", ["coverL"] = "左上盖", ["clog"] = "进气口", ["latchOuter"] = "外侧锁扣", ["latchRear"] = "后侧锁扣",
            ["mat"] = "操作垫", ["label"] = "翻盖读记录", ["bearing"] = "左上轴承", ["tray"] = "旧件落点", ["compare"] = "新旧轴承对比", ["box"] = "轴承盒新轴承", ["robot"] = "离座复测（看七号）",
        };

        public static Vector3 Get(Targets t, string id, Vector3 eye) => id switch
        {
            "grip" => (t.gripL - eye).sqrMagnitude < (t.gripR - eye).sqrMagnitude ? t.gripL : t.gripR,
            "lever" => t.lever, "coverL" => t.coverL, "clog" => t.clog, "latchOuter" => t.latchOuter, "latchRear" => t.latchRear,
            "mat" => t.mat, "label" => t.label, "bearing" => t.bearing, "tray" => t.tray, "box" => t.box,
            "compare" => (t.tray + t.box) / 2f, "robot" => t.robot, _ => throw new ArgumentException(id),
        };

        public static bool IsDock(string id) => id is "grip" or "lever" or "coverL" or "clog" or "latchOuter" or "latchRear" or "bearing" or "robot";

        /// <summary>水平方位角（度，0 = −Z 朝工作台，向 +X 为正 = 玩家面向 −Z 时的左手边）。</summary>
        public static float Yaw(Vector3 from, Vector3 to) => Mathf.Atan2(to.x - from.x, -(to.z - from.z)) * Mathf.Rad2Deg;

        public class TurnStats
        {
            public int over90, over60; public float total, maxStep; public List<(string a, string b, float d)> steps = new List<(string, string, float)>();
            public float facing, dockMean, benchMean, spread;
        }

        public static TurnStats Turns(Targets t, Vector3 stand)
        {
            var eye = stand + Vector3.up * EyeHeight;
            var s = new TurnStats();
            float prev = float.NaN; string prevId = null;
            var yaws = new List<float>();
            foreach (var id in Sequence)
            {
                float y = Yaw(eye, Get(t, id, eye));
                yaws.Add(y);
                if (!float.IsNaN(prev))
                {
                    float d = Mathf.Abs(Mathf.DeltaAngle(prev, y));
                    s.total += d; s.maxStep = Mathf.Max(s.maxStep, d);
                    if (d > 90f) s.over90++;
                    if (d > 60f) s.over60++;
                    s.steps.Add((prevId, id, d));
                }
                prev = y; prevId = id;
            }
            // 朝向 = 覆盖所有目标的最小扇区的中线
            var sorted = yaws.Select(y => (y + 360f) % 360f).Distinct().OrderBy(y => y).ToList();
            float bestGap = -1f, gapEnd = 0f;
            for (int i = 0; i < sorted.Count; i++)
            {
                float a = sorted[i], b = i + 1 < sorted.Count ? sorted[i + 1] : sorted[0] + 360f;
                if (b - a > bestGap) { bestGap = b - a; gapEnd = b; }
            }
            s.spread = 360f - bestGap;
            s.facing = Mathf.DeltaAngle(0f, gapEnd + s.spread / 2f);
            float Mean(IEnumerable<string> ids) => ids.Select(id => Mathf.DeltaAngle(s.facing, Yaw(eye, Get(t, id, eye)))).Average();
            s.dockMean = Mean(Sequence.Where(IsDock).Distinct());
            s.benchMean = Mean(Sequence.Where(id => !IsDock(id)).Distinct());
            return s;
        }

        // ---- 平面占地
        public struct Box2
        {
            public string name; public Vector2 c, h; public float rot, ymin, ymax;
            public Vector2 U => new Vector2(Mathf.Cos(rot * Mathf.Deg2Rad), Mathf.Sin(rot * Mathf.Deg2Rad));
            public float R => h.magnitude;
        }

        /// <summary>二维有向盒重叠（分离轴），高度区间也要重叠。不分配内存。</summary>
        static bool Overlap(Box2 a, Box2 b)
        {
            if (a.ymax < b.ymin || b.ymax < a.ymin) return false;
            var d = b.c - a.c;
            float rr = a.R + b.R;
            if (d.sqrMagnitude > rr * rr) return false;
            Vector2 au = a.U, av = new Vector2(-au.y, au.x), bu = b.U, bv = new Vector2(-bu.y, bu.x);
            bool Sep(Vector2 ax) =>
                Mathf.Abs(Vector2.Dot(d, ax)) > a.h.x * Mathf.Abs(Vector2.Dot(au, ax)) + a.h.y * Mathf.Abs(Vector2.Dot(av, ax)) +
                                                b.h.x * Mathf.Abs(Vector2.Dot(bu, ax)) + b.h.y * Mathf.Abs(Vector2.Dot(bv, ax));
            return !(Sep(au) || Sep(av) || Sep(bu) || Sep(bv));
        }

        public static Box2 FromBounds(string name, Bounds b, float pad = 0f) =>
            new Box2 { name = name, c = new Vector2(b.center.x, b.center.z), h = new Vector2(b.extents.x + pad, b.extents.z + pad), rot = 0f, ymin = b.min.y, ymax = b.max.y };

        /// <summary>A 世界 → 候选摆位：绕 A 的锚点转 dyaw，再平移到 anchorB。</summary>
        public static Matrix4x4 Delta(Vector3 anchorA, Vector3 anchorB, float dyaw) =>
            Matrix4x4.Translate(anchorB) * Matrix4x4.Rotate(Quaternion.Euler(0f, dyaw, 0f)) * Matrix4x4.Translate(-anchorA);

        static Box2 Move(Box2 b, Matrix4x4 m, float dyaw)
        {
            var p = m.MultiplyPoint3x4(new Vector3(b.c.x, 0f, b.c.y));
            // Unity 绕 Y 正转是 x→−z 方向；二维盒子用 (x, z) 平面上的角度，方向相反
            return new Box2 { name = b.name, c = new Vector2(p.x, p.z), h = b.h, rot = b.rot - dyaw, ymin = b.ymin, ymax = b.ymax };
        }

        public class Candidate
        {
            public Vector3 anchor; public float dyaw; public Vector3 stand; public TurnStats turns; public float score; public string why = "";
            public Dictionary<string, float> visible = new Dictionary<string, float>();
            public float dEngine, dLever, dGrip, dMat, dTray;
        }

        public static Targets ReadTargets(out GameObject dock, out GameObject robot)
        {
            dock = GameObject.Find("Unit07ServiceDock"); robot = GameObject.Find("UNIT07_RobotV4_DockReady");
            var flow = UnityEngine.Object.FindFirstObjectByType<FirstOrderFlow>();
            Vector3 C(Component c) => c is FirstOrderPart p ? p.WorldBounds().center : c.GetComponent<Renderer>().bounds.center;
            var t = new Targets
            {
                coverL = C(flow.Cover), clog = flow.ClogLayers.Last().bounds.center, latchOuter = C(flow.LatchOuter), latchRear = C(flow.LatchRear),
                bearing = flow.Bearing.WorldBounds().center, lever = GameObject.Find("Dock_PowerSwitch_LeverGrip").GetComponent<Renderer>().bounds.center,
                gripL = GameObject.Find("Dock_Clamp_L_Grip").GetComponent<Renderer>().bounds.center, gripR = GameObject.Find("Dock_Clamp_R_Grip").GetComponent<Renderer>().bounds.center,
                robot = robot.GetComponentsInChildren<Renderer>().Aggregate(new Bounds(robot.transform.position + Vector3.up * 0.4f, Vector3.zero), (b, r) => { b.Encapsulate(r.bounds); return b; }).center,
                coverR = C(flow.RightEngine),
                mat = flow.MatZone.landing.position + Vector3.up * 0.05f, tray = flow.OldTrayZone.landing.position + Vector3.up * 0.01f, box = flow.NewBearing.WorldBounds().center,
            };
            t.label = flow.MatZone.landing.position + Vector3.up * 0.04f;
            return t;
        }

        public static Targets MoveTargets(Targets a, Matrix4x4 m)
        {
            Vector3 P(Vector3 v) => m.MultiplyPoint3x4(v);
            return new Targets
            {
                coverL = P(a.coverL), clog = P(a.clog), latchOuter = P(a.latchOuter), latchRear = P(a.latchRear), bearing = P(a.bearing), lever = P(a.lever),
                gripL = P(a.gripL), gripR = P(a.gripR), robot = P(a.robot), coverR = P(a.coverR), mat = a.mat, label = a.label, tray = a.tray, box = a.box,
            };
        }

        static float H(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        static List<Vector3> freeCells;

        /// <summary>候选站位：人体圆不碰房间物件的格子（只算一次），再去掉碰到维修架 / 七号的。</summary>
        static IEnumerable<Vector3> Stands(Targets t, List<Box2> dockBoxes, List<Box2> obstacles, Bounds floor)
        {
            if (freeCells == null)
            {
                freeCells = new List<Vector3>();
                for (float x = floor.min.x + 0.35f; x <= floor.max.x - 0.35f; x += 0.05f)
                    for (float z = -0.2f; z <= floor.max.z - 0.35f; z += 0.05f)
                    {
                        var body = new Box2 { c = new Vector2(x, z), h = Vector2.one * BodyRadius, ymin = 0.05f, ymax = 1.8f };
                        if (!obstacles.Any(o => Overlap(o, body))) freeCells.Add(new Vector3(x, 0f, z));
                    }
            }
            foreach (var p in freeCells)
            {
                var body = new Box2 { c = new Vector2(p.x, p.z), h = Vector2.one * BodyRadius, ymin = 0.05f, ymax = 1.8f };
                bool hit = false;
                foreach (var o in dockBoxes) if (Overlap(o, body)) { hit = true; break; }
                if (!hit) yield return p;
            }
        }

        public static string Reach(Targets t, Vector3 p, out float dE, out float dL, out float dG, out float dM, out float dT)
        {
            var eye = p + Vector3.up * EyeHeight;
            dE = H(p, t.coverL); dL = H(p, t.lever); dG = H(p, Get(t, "grip", eye)); dM = H(p, t.mat); dT = Mathf.Max(H(p, t.tray), H(p, t.box));
            if (dE > 0.80f) return "左引擎太远";
            if (dL > 0.95f) return "断电开关太远";
            if (dG > 0.95f) return "夹具握把太远";
            if (dM > 1.00f || dT > 1.00f) return "工作台太远";
            return null;
        }

        public static void Run()
        {
            freeCells = null;
            EditorSceneManager.OpenScene(FirstOrderSceneBuilder.ScenePath, OpenSceneMode.Single);
            var tA = ReadTargets(out var dock, out var robot);
            var anchorA = GameObject.Find("Dock_RobotAnchor").transform.position;
            var bench = GameObject.Find("WorkbenchArea");
            var floorR = bench.GetComponentsInChildren<Renderer>().First(r => r.name == "Room_Floor").bounds;
            // 房间与工作台的障碍（地面、墙脚线、顶上管线、墙体用边界处理）
            var skip = new HashSet<string> { "Room_Floor", "Room_FloorDrain", "Room_Wainscot", "Room_Conduits", "Room_CeilingFixture", "Room_CeilingTube" };
            // 工作台台身范围内的东西（台面杂物、台灯、抽屉……）合成一个“工作台”盒子，其余单独算
            var benchFoot = bench.transform.Find("WB_Bench").GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            bool OnBench(Bounds b) => b.min.x >= benchFoot.min.x - 0.02f && b.max.x <= benchFoot.max.x + 0.02f && b.min.z >= benchFoot.min.z - 0.12f && b.max.z <= benchFoot.max.z + 0.02f;
            var all = bench.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy && !skip.Contains(r.name) && r.bounds.max.y > 0.02f).ToList();
            var benchAll = all.Where(r => OnBench(r.bounds)).Select(r => r.bounds).Aggregate(benchFoot, (a, b) => { a.Encapsulate(b); return a; });
            var obstacles = all.Where(r => !OnBench(r.bounds)).Select(r => FromBounds(r.name, r.bounds, 0.02f)).Append(FromBounds("工作台（含台面物件）", benchAll, 0.02f)).ToList();
            // 维修架 + 七号的占地盒（A 世界）
            var dockBoxesA = dock.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.bounds.size.magnitude > 0.03f).Select(r => FromBounds(r.name, r.bounds))
                                 .Append(FromBounds("七号", robot.GetComponentsInChildren<Renderer>().Aggregate(new Bounds(robot.transform.position + Vector3.up * 0.4f, Vector3.zero), (b, r) => { b.Encapsulate(r.bounds); return b; })))
                                 .ToList();
            var sb = new StringBuilder();
            sb.AppendLine("# 布局 B 摆位搜索（LayoutPlanner）");
            sb.AppendLine();
            sb.AppendLine($"- 房间地面 x {floorR.min.x:F2}…{floorR.max.x:F2}，z {floorR.min.z:F2}…{floorR.max.z:F2}；z = {floorR.max.z:F2} 一侧敞开（没有墙）。障碍 {obstacles.Count} 个（工作台、房间物件、墙）。");
            sb.AppendLine($"- 玩家：人体半径 {BodyRadius} m，眼高 {EyeHeight} m；够得着：左引擎 ≤ 0.80 m，开关 / 握把 ≤ 0.95 m，操作垫 / 托盘 / 轴承盒 ≤ 1.00 m（水平距离，最多前倾半步；三者同时在 0.85 m 内的站位在这张工作台前不存在）；操作垫正前方 0.50 × 0.45 m 要留给人站（维修架不能占）。");
            sb.AppendLine($"- 动作顺序：{string.Join(" → ", Sequence.Select(s => Names[s]))}");
            sb.AppendLine();

            // ---- A：维修架不动，找最佳站位
            var candA = BestStand(tA, dockBoxesA, obstacles, floorR, anchorA, anchorA, 0f, requireLeftRight: false);
            sb.AppendLine($"## A（原布局）最佳站位 {candA?.stand:F2}：{Describe(candA)}");
            sb.AppendLine();

            // ---- B：搜索维修架摆位
            var benchTopZ = bench.GetComponentsInChildren<Renderer>().First(r => r.name == "Bench_Top").bounds.max.z;
            var matFront = new Box2 { name = "操作垫正前方", c = new Vector2(tA.mat.x, benchTopZ + 0.225f), h = new Vector2(0.25f, 0.225f), ymin = 0f, ymax = 1.8f };
            var results = new List<Candidate>();
            int tried = 0, fit = 0;
            var seen = new HashSet<(int, int, int)>();
            void Try(float x, float z, float dyaw)
            {
                dyaw = (dyaw % 360f + 360f) % 360f;
                if (!seen.Add((Mathf.RoundToInt(x * 1000), Mathf.RoundToInt(z * 1000), Mathf.RoundToInt(dyaw)))) return;
                tried++;
                var anchorB = new Vector3(x, anchorA.y, z);
                var m = Delta(anchorA, anchorB, dyaw);
                var boxes = dockBoxesA.Select(b => Move(b, m, dyaw)).ToList();
                foreach (var b in boxes) foreach (var o in obstacles) if (Overlap(o, b)) return;
                foreach (var b in boxes) if (Overlap(matFront, b)) return;                       // 操作垫正前方留给人站
                if (!InsideFloor(boxes, floorR)) return;
                if (!ArrivalClear(m, dyaw, anchorB, obstacles, floorR)) return;
                fit++;
                var c = BestStand(MoveTargets(tA, m), boxes, obstacles, floorR, anchorA, anchorB, dyaw, requireLeftRight: true);
                if (c != null) results.Add(c);
            }
            // 粗搜：锚点 10 cm 网格 × 朝向 15°；细搜：前 12 名周围 ±10 cm（2.5 cm 一档）× ±10°（5° 一档）
            for (float dyaw = 0f; dyaw < 360f; dyaw += 15f)
                for (float x = floorR.min.x + 0.3f; x <= floorR.max.x - 0.3f; x += 0.1f)
                    for (float z = 0.1f; z <= floorR.max.z - 0.2f; z += 0.1f)
                        Try(x, z, dyaw);
            foreach (var c in results.OrderBy(r => r.score).Take(12).ToList())
                for (float dx = -0.1f; dx <= 0.101f; dx += 0.025f)
                    for (float dz = -0.1f; dz <= 0.101f; dz += 0.025f)
                        for (float dy = -10f; dy <= 10.1f; dy += 5f)
                            Try(c.anchor.x + dx, c.anchor.z + dz, c.dyaw + dy);
            sb.AppendLine($"## B 搜索：试了 {tried} 个摆位（粗搜锚点 10 cm 网格 × 朝向 15°，再在前 12 名周围细搜 2.5 cm × 5°），占地不冲突且抵达通道空着的 {fit} 个，满足够得着与“架左台右”的 {results.Count} 个。");
            sb.AppendLine();
            var top = results.OrderBy(r => r.score).Take(40).ToList();
            // ---- 真实碰撞的点选可见性
            var flow = UnityEngine.Object.FindFirstObjectByType<FirstOrderFlow>();
            var inspect = robot.GetComponentsInChildren<DockInteractable>(true).Where(d => d.action == DockAction.EngineLeft).Select(d => d.GetComponent<Collider>()).FirstOrDefault();
            if (inspect != null) inspect.enabled = false;
            var dockPose = (dock.transform.position, dock.transform.rotation); var robotPose = (robot.transform.position, robot.transform.rotation);
            foreach (var c in top.Prepend(candA).Where(c => c != null))
            {
                var m = Delta(anchorA, c.anchor, c.dyaw);
                var q = Quaternion.Euler(0f, c.dyaw, 0f);
                dock.transform.SetPositionAndRotation(m.MultiplyPoint3x4(dockPose.Item1), q * dockPose.Item2);
                robot.transform.SetPositionAndRotation(m.MultiplyPoint3x4(robotPose.Item1), q * robotPose.Item2);
                Physics.SyncTransforms();
                c.visible = Visibility(flow, c.stand + Vector3.up * EyeHeight);
                float vis = new[] { "左上盖", "进气口", "外侧锁扣", "断电开关", "夹具握把" }.Sum(k => c.visible[k] > 0.05f ? 0f : 40f) + (c.visible["后侧锁扣"] > 0.05f ? 0f : 15f) + (c.visible["左上轴承"] > 0.05f ? 0f : 25f);
                c.score += vis;
            }
            dock.transform.SetPositionAndRotation(dockPose.Item1, dockPose.Item2);
            robot.transform.SetPositionAndRotation(robotPose.Item1, robotPose.Item2);
            if (candA != null) sb.AppendLine($"A 的可见性（站位眼睛 → 真实点选）：{string.Join("，", candA.visible.Select(kv => $"{kv.Key} {kv.Value:P0}"))}");
            sb.AppendLine();
            sb.AppendLine("| 排名 | 锚点 | 维修架转角 | 站位 | 朝向 | >90° 转向 | >60° 转向 | 总转角 | 架在左（平均方位） | 台在右 | 左引擎 / 开关 / 握把 / 垫 / 托盘距离 | 可见：左上盖 / 进气口 / 外锁扣 / 后锁扣 / 轴承 / 开关 / 握把 | 得分 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            var ranked = top.OrderBy(r => r.score).ToList();
            for (int i = 0; i < Math.Min(15, ranked.Count); i++)
            {
                var c = ranked[i];
                string V(string k) => c.visible.TryGetValue(k, out var v) ? v.ToString("P0") : "-";
                sb.AppendLine($"| {i + 1} | ({c.anchor.x:F2}, {c.anchor.z:F2}) | {c.dyaw:F0}° | ({c.stand.x:F2}, {c.stand.z:F2}) | {c.turns.facing:F0}° | {c.turns.over90} | {c.turns.over60} | {c.turns.total:F0}° | {c.turns.dockMean:F0}° | {c.turns.benchMean:F0}° | " +
                              $"{c.dEngine:F2} / {c.dLever:F2} / {c.dGrip:F2} / {c.dMat:F2} / {c.dTray:F2} | {V("左上盖")} / {V("进气口")} / {V("外侧锁扣")} / {V("后侧锁扣")} / {V("左上轴承")} / {V("断电开关")} / {V("夹具握把")} | {c.score:F0} |");
            }
            var best = ranked.FirstOrDefault();
            if (best != null)
            {
                sb.AppendLine();
                sb.AppendLine($"**选用第 1 名**：锚点 ({best.anchor.x:F3}, {best.anchor.z:F3})，维修架在 A 的基础上再转 {best.dyaw:F0}°，玩家站位 ({best.stand.x:F2}, {best.stand.z:F2})，朝向 {best.turns.facing:F0}°。");
                sb.AppendLine($"LAYOUT_B anchor={best.anchor.x:F3},{best.anchor.z:F3} dyaw={best.dyaw:F0} stand={best.stand.x:F2},{best.stand.z:F2} facing={best.turns.facing:F0}");
            }
            if (candA != null) sb.AppendLine($"LAYOUT_A stand={candA.stand.x:F2},{candA.stand.z:F2} facing={candA.turns.facing:F0}");
            Directory.CreateDirectory(LayoutABScenes.OutDir);
            File.WriteAllText(Path.Combine(LayoutABScenes.OutDir, "planner_B.md"), sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[LayoutAB] " + sb.ToString().Split('\n').Last(l => l.StartsWith("LAYOUT_B") || l.StartsWith("LAYOUT_A")));
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static string Describe(Candidate c) => c == null ? "没有可行站位" :
            $">90° 转向 {c.turns.over90} 次、>60° {c.turns.over60} 次、总转角 {c.turns.total:F0}°；左引擎 {c.dEngine:F2} m、开关 {c.dLever:F2} m、握把 {c.dGrip:F2} m、操作垫 {c.dMat:F2} m、托盘 / 轴承盒 {c.dTray:F2} m";

        static bool InsideFloor(List<Box2> boxes, Bounds floor)
        {
            foreach (var b in boxes)
            {
                float r = b.rot * Mathf.Deg2Rad;
                var ex = new Vector2(Mathf.Abs(Mathf.Cos(r)) * b.h.x + Mathf.Abs(Mathf.Sin(r)) * b.h.y, Mathf.Abs(Mathf.Sin(r)) * b.h.x + Mathf.Abs(Mathf.Cos(r)) * b.h.y);
                if (b.c.x - ex.x < floor.min.x + 0.02f || b.c.x + ex.x > floor.max.x - 0.02f || b.c.y - ex.y < floor.min.z + 0.02f || b.c.y + ex.y > floor.max.z) return false;
            }
            return true;
        }

        /// <summary>抵达 / 离开通道：七号（1.05 m 宽，0.7–1.45 m 高）从锚点朝 +Z 敞开一侧直线进出，途中不碰房间物件。</summary>
        static bool ArrivalClear(Matrix4x4 m, float dyaw, Vector3 anchor, List<Box2> obstacles, Bounds floor)
        {
            float len = floor.max.z - anchor.z;
            if (len <= 0f) return true;
            var lane = new Box2 { c = new Vector2(anchor.x, anchor.z + len / 2f), h = new Vector2(0.525f, len / 2f), ymin = 0.70f, ymax = 1.45f };
            return !obstacles.Any(o => Overlap(o, lane));
        }

        static Candidate BestStand(Targets t, List<Box2> dockBoxes, List<Box2> obstacles, Bounds floor, Vector3 anchorA, Vector3 anchorB, float dyaw, bool requireLeftRight)
        {
            Candidate best = null;
            foreach (var p in Stands(t, dockBoxes, obstacles, floor))
            {
                var why = Reach(t, p, out var dE, out var dL, out var dG, out var dM, out var dT);
                if (why != null) continue;
                var s = Turns(t, p);
                if (requireLeftRight && !(s.dockMean > 10f && s.benchMean < -10f)) continue;      // 架在玩家左前、台在右前
                // 也不能有目标落在玩家正后方（> 100°）
                float score = s.over90 * 100f + s.over60 * 10f + s.total * 0.2f + s.spread * 0.3f + Mathf.Abs(dE - 0.6f) * 40f;
                if (best == null || score < best.score)
                    best = new Candidate { anchor = anchorB, dyaw = dyaw, stand = p, turns = s, score = score, dEngine = dE, dLever = dL, dGrip = dG, dMat = dM, dTray = dT };
            }
            return best;
        }

        /// <summary>从玩家眼睛看关键部件：在部件点选范围里取 5×5×5 个点，按真实点选规则（含遮挡）能选中它的比例。</summary>
        public static Dictionary<string, float> Visibility(FirstOrderFlow flow, Vector3 eye)
        {
            var res = new Dictionary<string, float>();
            float Frac(Component target, IEnumerable<Collider> cols)
            {
                int hit = 0, n = 0;
                foreach (var col in cols.Where(c => c != null && c.enabled))
                {
                    var b = col.bounds;
                    for (int i = 0; i < 125; i++)
                    {
                        var f = new Vector3(i % 5, (i / 5) % 5, i / 25) / 4f;
                        var w = b.min + Vector3.Scale(b.size, Vector3.one * 0.5f + (f - Vector3.one * 0.5f) * 0.9f);
                        n++;
                        if (FirstOrderInput.Pick(new Ray(eye, w - eye)) == target) hit++;
                    }
                }
                return n == 0 ? 0f : hit / (float)n;
            }
            IEnumerable<Collider> Cols(FirstOrderPart p) => p.GetComponents<Collider>().Concat(p.members.SelectMany(m => m.GetComponents<Collider>()));
            res["左上盖"] = Frac(flow.Cover, Cols(flow.Cover));
            res["进气口"] = Frac(flow.Clog, Cols(flow.Clog));
            res["外侧锁扣"] = Frac(flow.LatchOuter, Cols(flow.LatchOuter));
            res["后侧锁扣"] = Frac(flow.LatchRear, Cols(flow.LatchRear));
            var lever = GameObject.Find("Dock_PowerSwitch_LeverGrip").GetComponent<DockInteractable>();
            res["断电开关"] = Frac(lever, lever.GetComponents<Collider>());
            var grips = new[] { "Dock_Clamp_L_Grip", "Dock_Clamp_R_Grip" }.Select(n => GameObject.Find(n).GetComponent<DockInteractable>()).ToList();
            res["夹具握把"] = grips.Max(g => Frac(g, g.GetComponents<Collider>()));
            // 轴承：上盖拆下后才看得到——临时关掉上盖总成的碰撞
            var coverCols = Cols(flow.Cover).Concat(flow.Clog.GetComponents<Collider>()).Where(c => c.enabled).ToList();
            foreach (var c in coverCols) c.enabled = false;
            Physics.SyncTransforms();
            res["左上轴承"] = Frac(flow.Bearing, Cols(flow.Bearing));
            foreach (var c in coverCols) c.enabled = true;
            Physics.SyncTransforms();
            return res;
        }
    }
}
