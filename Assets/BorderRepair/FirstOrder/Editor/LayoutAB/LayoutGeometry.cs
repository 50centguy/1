using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Dock;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BorderRepair.FirstOrder.EditorTools.LayoutAB
{
    /// <summary>
    /// 布局 A/B · 站位与手部接近空间的几何评估（编辑器里用精确网格；只是几何，没有用 VR 头显）：
    /// - 站位处人体（半径 0.25 m、高 1.85 m 的胶囊）是否碰到东西，周围 1 m 内的可站面积；
    /// - 从站位的肩膀（1.40 m）到每个关键点的距离（≤ 0.70 m 算原地够得着）和手部通路（半径 3.5 cm 的球扫过去，先碰到的是不是目标）；
    /// - 每个关键点 0.25–0.80 m 范围内能站人、而且手部通路畅通的位置有多少，离主站位最近的那个要走几步（0.6 m 一步）；
    /// - 七号离座上浮的空间（机身上方 0.30 m）和抵达 / 离开通道（锚点到房间敞开一侧，1.05 m 宽、0.70–1.45 m 高）。
    /// 输出 Docs/Integration/Unit07FirstOrder/LayoutAB/geometry_vr.md。
    /// </summary>
    public static class LayoutGeometry
    {
        const float Shoulder = 1.40f, Reach = 0.70f, LeanReach = 0.85f, ShoulderLow = 1.00f, HandR = 0.035f, BodyR = 0.25f, BodyTop = 1.85f;

        /// <summary>弯腰 / 下蹲时肩高在 1.00–1.45 m 之间取最合适的高度，返回那个肩位置。</summary>
        static Vector3 BestShoulder(Vector3 stand, Vector3 point) => new Vector3(stand.x, Mathf.Clamp(point.y + 0.35f, ShoulderLow, 1.45f), stand.z);   // 肩在目标上方约 0.35 m（俯身向下够）

        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 布局 A/B · 站位与手部接近空间（几何评估）");
            sb.AppendLine();
            sb.AppendLine("**这是几何评估，没有戴 VR 头显测试，手感、身高差异、手柄射线都未验证。**");
            sb.AppendLine();
            sb.AppendLine($"- 人体：半径 {BodyR} m、高 {BodyTop} m 的胶囊。站直：肩高 {Shoulder} m，肩到目标 ≤ {Reach} m 算站直就够得着；弯腰 / 下蹲：肩高可在 {ShoulderLow}–1.45 m 之间、身体前倾，肩到目标 ≤ {LeanReach} m 算够得着。手部通路 = 半径 {HandR * 1000:F0} mm 的球从肩扫到目标上方 12 cm 再落到目标（弯腰时肩在目标上方约 0.35 m），碰到的第一处是目标本身或离目标点 6 cm 以内。");
            sb.AppendLine("- 碰撞：工作台 / 房间 / 维修架 / 七号 / 故障美术件全部用精确网格（没有碰撞的临时加上，评估完删掉）；维修座集成留下的整块左引擎检查代理不算实体。");
            sb.AppendLine();
            foreach (var id in new[] { "A", "B" }) Evaluate(id, sb);
            Directory.CreateDirectory(LayoutABScenes.OutDir);
            File.WriteAllText(Path.Combine(LayoutABScenes.OutDir, "geometry_vr.md"), sb.ToString(), new UTF8Encoding(false));
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void Evaluate(string id, StringBuilder sb)
        {
            EditorSceneManager.OpenScene(id == "A" ? LayoutABScenes.SceneA : LayoutABScenes.SceneB, OpenSceneMode.Single);
            var flow = Object.FindFirstObjectByType<FirstOrderFlow>();
            var temp = new List<Component>();
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                var r = mf.GetComponent<Renderer>();
                if (r == null || !r.enabled || !mf.gameObject.activeInHierarchy || mf.GetComponents<Collider>().Any(c => !c.isTrigger)) continue;
                if (r.bounds.max.y > 2.3f && r.bounds.size.y < 0.4f) continue;   // 顶棚灯具
                var c = mf.gameObject.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh; temp.Add(c);
            }
            var inspect = Object.FindObjectsByType<DockInteractable>(FindObjectsSortMode.None).Where(d => d.action == DockAction.EngineLeft).Select(d => d.GetComponent<Collider>()).FirstOrDefault();
            if (inspect != null) inspect.enabled = false;
            Physics.SyncTransforms();
            const QueryTriggerInteraction Q = QueryTriggerInteraction.Ignore;

            var (stand, yaw) = LayoutABScenes.Stand(id);
            var shoulder = stand + Vector3.up * Shoulder;
            bool BodyFree(Vector3 p) => !Physics.CheckCapsule(p + Vector3.up * (BodyR + 0.06f), p + Vector3.up * (BodyTop - BodyR), BodyR, ~0, Q);
            float freeR = 0f;
            for (float r = 0.15f; r <= 0.6f; r += 0.01f) { if (Physics.CheckCapsule(stand + Vector3.up * (r + 0.06f), stand + Vector3.up * Mathf.Max(r + 0.07f, BodyTop - r), r, ~0, Q)) break; freeR = r; }
            int freeCells = 0;
            for (float x = -1f; x <= 1f; x += 0.05f)
                for (float z = -1f; z <= 1f; z += 0.05f)
                    if (x * x + z * z <= 1f && BodyFree(stand + new Vector3(x, 0f, z))) freeCells++;

            sb.AppendLine($"## 布局 {id}");
            sb.AppendLine();
            sb.AppendLine($"- 主站位 ({stand.x:F2}, {stand.z:F2})，朝向 {yaw:F0}°：人体胶囊{(BodyFree(stand) ? "不碰任何东西" : "**碰到东西**")}；站位处能放下的最大人体半径 {freeR:F2} m；周围 1 m 内可站面积约 {freeCells * 0.0025f:F2} m²");

            // ---- 关键点
            Vector3 C(Component c) => c is FirstOrderPart p ? p.WorldBounds().center : c.GetComponent<Renderer>().bounds.center;
            var gripL = GameObject.Find("Dock_Clamp_L_Grip").GetComponent<Renderer>(); var gripR = GameObject.Find("Dock_Clamp_R_Grip").GetComponent<Renderer>();
            var grip = (gripL.bounds.center - shoulder).sqrMagnitude < (gripR.bounds.center - shoulder).sqrMagnitude ? gripL : gripR;
            var targets = new List<(string name, Vector3 point, Component[] own, bool coverOff)>
            {
                ("左上盖（取 / 装）", C(flow.Cover) + Vector3.up * 0.03f, new Component[] { flow.Cover }.Concat(flow.Cover.members).ToArray(), false),
                ("进气口（清理）", flow.ClogLayers.Last().bounds.center, new Component[] { flow.Clog, flow.Cover }.Concat(flow.Cover.members).ToArray(), false),
                ("外侧锁扣", C(flow.LatchOuter), new Component[] { flow.LatchOuter }, false),
                ("后侧锁扣", C(flow.LatchRear), new Component[] { flow.LatchRear }, false),
                ("左上轴承（上盖拆下后）", flow.Bearing.WorldBounds().center, new Component[] { flow.Bearing }, true),
                ("断电开关", GameObject.Find("Dock_PowerSwitch_LeverGrip").GetComponent<Renderer>().bounds.center, new Component[] { GameObject.Find("Dock_PowerSwitch_LeverGrip").transform }, false),
                ("夹具握把（近的那个）", grip.bounds.center, new Component[] { grip.transform }, false),
                ("右引擎上盖（对照）", C(flow.RightEngine), new Component[] { flow.RightEngine }, false),
                ("操作垫落点", flow.MatZone.landing.position + Vector3.up * 0.03f, new Component[] { GameObject.Find("Bench_Mat").transform }, false),
                ("旧件落点（托盘）", flow.OldTrayZone.landing.position + Vector3.up * 0.02f, new Component[] { GameObject.Find("Tray_Screws").transform }, false),
                ("轴承盒上的新轴承", flow.NewBearing.WorldBounds().center, new Component[] { flow.NewBearing, GameObject.Find("Box_Bearings").transform }, false),
            };
            sb.AppendLine();
            sb.AppendLine("| 关键点 | 方位角（相对朝向） | 站直：肩到目标 | 站直够得着 | 弯腰 / 下蹲：肩到目标 | 弯腰 / 下蹲够得着 | 原地手部通路 | 0.25–0.80 m 内可站且够得着、通路畅通的位置 | 最近那个离主站位 | 要走几步（0.6 m 一步） |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            var coverCols = new Component[] { flow.Cover, flow.Clog }.Concat(flow.Cover.members).SelectMany(t => t.GetComponentsInChildren<Collider>()).Where(c => c.enabled).ToList();
            foreach (var (name, point, own, coverOff) in targets)
            {
                if (coverOff) { foreach (var c in coverCols) c.enabled = false; Physics.SyncTransforms(); }
                var ownSet = new HashSet<Transform>(own.SelectMany(o => o.GetComponentsInChildren<Transform>(true)));
                string HandPath(Vector3 from, out bool ok)
                {
                    // 手从肩伸到目标上方 12 cm，再落到目标（台面上的东西手是从上面够的）
                    var above = point + Vector3.up * 0.12f;
                    foreach (var (a, b) in new[] { (from, above), (above, point) })
                    {
                        var d = b - a; float dist = d.magnitude;
                        if (dist < 0.005f || !Physics.SphereCast(a, HandR, d / dist, out var hit, dist - 0.005f, ~0, Q)) continue;
                        if (ownSet.Contains(hit.collider.transform) || (hit.point - point).magnitude <= 0.06f) break;
                        ok = false; return $"先碰到 {hit.collider.name}（离目标 {(hit.point - point).magnitude * 100:F0} cm）";
                    }
                    ok = true; return "畅通";
                }
                float rel = Mathf.DeltaAngle(yaw, LayoutPlanner.Yaw(stand + Vector3.up * LayoutPlanner.EyeHeight, point));
                float reach = (point - shoulder).magnitude;
                var low = BestShoulder(stand, point);
                float reachLow = (point - low).magnitude;
                var path = HandPath(reachLow <= LeanReach ? low : shoulder, out var pathOk);
                int spots = 0; float nearest = float.MaxValue;
                for (float x = -0.8f; x <= 0.8f; x += 0.05f)
                    for (float z = -0.8f; z <= 0.8f; z += 0.05f)
                    {
                        float hd = Mathf.Sqrt(x * x + z * z);
                        if (hd < 0.25f || hd > 0.8f) continue;
                        var p = new Vector3(point.x + x, 0f, point.z + z);
                        if (!BodyFree(p)) continue;
                        var sh = BestShoulder(p, point);
                        if ((point - sh).magnitude > LeanReach) continue;
                        HandPath(sh, out var ok);
                        if (!ok) continue;
                        spots++;
                        nearest = Mathf.Min(nearest, new Vector2(p.x - stand.x, p.z - stand.z).magnitude);
                    }
                if (coverOff) { foreach (var c in coverCols) c.enabled = true; Physics.SyncTransforms(); }
                bool here = reachLow <= LeanReach && pathOk;
                sb.AppendLine($"| {name} | {rel:+0;-0;0}° | {reach:F2} m | {(reach <= Reach ? "是" : "否")} | {reachLow:F2} m | {(reachLow <= LeanReach ? "是" : "否")} | {path} | {spots} 处（约 {spots * 0.0025f:F2} m²） | " +
                              $"{(spots > 0 ? $"{nearest:F2} m" : "—")} | {(here ? "0（原地）" : spots == 0 ? "附近没有能站的位置" : Mathf.Max(1, Mathf.CeilToInt(nearest / 0.6f)).ToString())} |");
            }

            // ---- 离座上浮空间、抵达 / 离开通道
            var robot = GameObject.Find("UNIT07_RobotV4_DockReady");
            var robotCols = new HashSet<Collider>(robot.GetComponentsInChildren<Collider>(true));
            var dockCols = new HashSet<Collider>(GameObject.Find("Unit07ServiceDock").GetComponentsInChildren<Collider>(true));
            var rb = robot.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            var lift = Physics.OverlapBox(new Vector3(rb.center.x, rb.max.y + 0.16f, rb.center.z), new Vector3(rb.extents.x, 0.15f, rb.extents.z), Quaternion.identity, ~0, Q)
                              .Where(c => !robotCols.Contains(c)).Select(c => c.name).Distinct().ToList();
            var anchor = LayoutABScenes.Anchor(id);
            float floorMaxZ = 1.7f;
            var lane = Physics.OverlapBox(new Vector3(anchor.x, 1.075f, (anchor.z + 0.3f + floorMaxZ) / 2f), new Vector3(0.525f, 0.375f, Mathf.Max(0.01f, (floorMaxZ - anchor.z - 0.3f) / 2f)), Quaternion.identity, ~0, Q)
                              .Where(c => !robotCols.Contains(c) && !dockCols.Contains(c)).Select(c => c.name).Distinct().ToList();
            bool playerInLane = Mathf.Abs(stand.x - anchor.x) < 0.525f + BodyR && stand.z > anchor.z;
            sb.AppendLine();
            sb.AppendLine($"- 离座上浮：七号机身上方 0.30 m 内{(lift.Count == 0 ? "没有东西" : "有 " + string.Join("、", lift))}（原型只上浮 0.12 m）。");
            sb.AppendLine($"- 抵达 / 离开通道（锚点 ({anchor.x:F2}, {anchor.z:F2}) 朝 +Z 敞开一侧，1.05 m 宽、0.70–1.45 m 高）：{(lane.Count == 0 ? "空着" : "有 " + string.Join("、", lane))}；玩家主站位{(playerInLane ? "**在通道里**" : "不在通道里")}。");
            sb.AppendLine();

            foreach (var c in temp) Object.DestroyImmediate(c);
        }
    }
}
