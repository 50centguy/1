using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using static BorderRepair.ArtAudit.AuditCommon;

namespace BorderRepair.ArtAudit
{
    /// <summary>
    /// 第 3b 步（只读，场景不保存）：补测
    /// 1) Blender → Unity 轴向：用几个带 remove_dir 的件，看 Blender +X 在 Unity 里对应七号 Body 的哪个方向（实测，不靠推断）。
    /// 2) 连接键留在机身插座里、引擎沿七号左向外滑：滑多远才脱开连接键，途中碰不碰别的。
    /// 3) 搬运路线搜索：侧拔 400 mm → 升 / 降到搬运高度 h → 水平移到落点上方 → 竖直落到垫面（台灯下方的落点改为低空水平进入）。
    ///    对工作台现状下的可行落点逐个试，h 从 0.92 到 1.60 m，取全程最小间距最大的一条。
    /// 输出 engine_path.md / engine_path.json。
    /// </summary>
    public static class AuditEnginePath
    {
        static WMesh Moved(WMesh m, Matrix4x4 mtx)
        {
            var v = m.v.Select(p => mtx.MultiplyPoint3x4(p)).ToArray();
            var b = new Bounds(v[0], Vector3.zero); foreach (var p in v) b.Encapsulate(p);
            return new WMesh { name = m.name, v = v, tri = m.tri, b = b };
        }

        public static void Run()
        {
            AuditTray.Setup(); AuditTray.RestPose();
            var robot = Find("UNIT07_RobotV4_DockReady");
            var body = FindUnder(robot, "Body");
            var hinge = FindUnder(robot, "Engine_L_Hinge");
            var md = new StringBuilder("# 整台左引擎：轴向核对、连接键、搬运路线（补测）\n\n");
            var js = new StringBuilder("{");

            // 1) 轴向：外侧锁扣（Blender remove_dir +X）、后侧锁扣（+Y）、后端盖（+Y）、上盖（≈ +Z）相对引擎铰点的位置
            md.AppendLine("## Blender remove_dir 在 Unity 七号 Body 坐标里的方向（实测）\n\n| 件 | Blender remove_dir | 件中心相对 Engine_L_Hinge（Body 本地） | 结论 |\n|---|---|---|---|");
            foreach (var (n, dir) in new[] { ("Engine_CoverLatch_Outer_L", "+X"), ("Engine_CoverLatch_Rear_L", "+Y"), ("Engine_RearCap_L", "+Y"), ("Engine_UpperCover_L", "≈+Z") })
            {
                var r = FindUnder(robot, n).GetComponent<Renderer>();
                var rel = body.InverseTransformDirection(r.bounds.center - hinge.position);
                md.AppendLine($"| {n} | {dir} | {rel:F3} | 件在铰点的 {(Mathf.Abs(rel.x) > Mathf.Abs(rel.z) ? (rel.x < 0 ? "Body −X（七号左 / 外侧）" : "Body +X（七号右 / 内侧）") : (rel.z < 0 ? "Body −Z（七号后）" : "Body +Z（七号前）"))} |");
            }
            md.AppendLine("\n由此：Blender +X（外侧）= Unity Body −X（七号左）；Blender +Y（后）= Unity Body −Z；Blender +Z = Unity Body +Y。即 Unity Body = (−x, z, −y)。\n");

            // 静态环境
            var dock = Find("Unit07ServiceDock"); var bench = Find("WorkbenchArea"); var mat = FindUnder(bench, "Bench_Mat");
            var movable = new System.Func<string, bool>(n => false);   // 工作台现状（义体占位道具在 B 场景里本来就是隐藏的）
            var env = dock.GetComponentsInChildren<Renderer>(true).Concat(bench.GetComponentsInChildren<Renderer>(true))
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.bounds.max.y < 2.2f && !movable(r.name) && r.transform != mat).Select(World).Where(m => m != null).ToList();
            var take = hinge.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToList();
            var key = take.First(r => r.name == "ConnectionKey_L");
            var bolt = take.First(r => r.name == "ConnectionKey_Bolt_L");
            var group = take.Where(r => r != key && r != bolt).Select(World).Where(m => m != null).ToList();
            var robotStay = robot.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && !r.transform.IsChildOf(hinge) && !(r is SkinnedMeshRenderer)).Select(World).Where(m => m != null).ToList();
            var keyM = World(key); var boltM = World(bolt);
            var left = -body.right;

            // 2) 连接键留在插座：引擎外滑
            md.AppendLine("## 连接键留在机身插座里，引擎沿七号左向外滑\n\n| 外滑 | 引擎件 ↔ 连接键 | 引擎件 ↔ 机身其它 / 环境 |\n|---|---|---|");
            float freeAt = float.NaN;
            for (float d = 0.005f; d <= 0.1001f; d += 0.005f)
            {
                var g = group.Select(m => Moved(m, Matrix4x4.Translate(left * d))).ToList();
                var k = MinDistance(g, new List<WMesh> { keyM }, 0.05f);
                var o = MinDistance(g, robotStay.Concat(env).ToList(), 0.05f);
                if (float.IsNaN(freeAt) && k.d > 0.0005f) freeAt = d;
                if (Mathf.RoundToInt(d * 1000) % 10 == 0 || float.IsNaN(freeAt) || Mathf.Abs(d - freeAt) < 1e-4f)
                    md.AppendLine($"| {Mm(d)} mm | {Mm(k.d)} mm（{k.a}） | {Mm(o.d)} mm（{o.a} ↔ {o.b}） |");
                if (!float.IsNaN(freeAt) && d > freeAt + 0.02f) break;
            }
            md.AppendLine($"\n- 连接键不拔、引擎直接外滑：滑 **{(float.IsNaN(freeAt) ? "> 100" : Mm(freeAt))} mm** 后离开连接键。\n");
            js.Append($"\"slide_off_key_mm\":{(float.IsNaN(freeAt) ? "null" : Mm(freeAt))},");

            // 3) 搬运路线
            var matB = mat.GetComponent<Renderer>().bounds; float matTop = matB.max.y;
            var gb = group[0].b; foreach (var m in group) gb.Encapsulate(m.b);
            var onMat = env.Where(m => m.b.max.y > matTop + 0.001f && m.b.max.x > matB.min.x && m.b.min.x < matB.max.x && m.b.max.z > matB.min.z && m.b.min.z < matB.max.z).ToList();
            var spots = new List<(Matrix4x4 land, float yaw, Vector3 delta, float clr)>();
            foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
            {
                var rot = Matrix4x4.TRS(gb.center, Quaternion.AngleAxis(yaw, Vector3.up), Vector3.one) * Matrix4x4.Translate(-gb.center);
                var rg = group.Select(m => Moved(m, rot)).ToList(); var rb = rg[0].b; foreach (var m in rg) rb.Encapsulate(m.b);
                for (float x = matB.min.x; x <= matB.max.x; x += 0.03f)
                    for (float z = matB.min.z; z <= matB.max.z; z += 0.03f)
                    {
                        var delta = new Vector3(x - rb.center.x, matTop + 0.001f - rb.min.y, z - rb.center.z);
                        var fp = new Bounds(rb.center + delta, rb.size);
                        if (fp.min.x < matB.min.x || fp.max.x > matB.max.x || fp.min.z < matB.min.z || fp.max.z > matB.max.z) continue;
                        var lm = Matrix4x4.Translate(delta) * rot;
                        float c = MinDistance(group.Select(m => Moved(m, lm)).ToList(), onMat, 0.05f).d;
                        if (c > 0.005f) spots.Add((lm, yaw, delta, c));
                    }
            }
            var trySpots = spots.OrderByDescending(s => s.clr).Take(12).ToList();
            var staticAll = robotStay.Concat(env).ToList();
            (float worst, float h, int si, string who) best = (-1f, 0, -1, "-");
            var p0 = left * 0.40f;
            for (int si = 0; si < trySpots.Count; si++)
            {
                var sp = trySpots[si];
                for (float h = 0.92f; h <= 1.601f; h += 0.04f)
                {
                    float dh = h - gb.min.y;            // 引擎最低点到 h 的位移
                    var a = p0; var b = p0 + Vector3.up * dh; var landC = sp.land.MultiplyPoint3x4(gb.center);
                    var c = new Vector3(landC.x - gb.center.x, dh, landC.z - gb.center.z);
                    float worst = float.MaxValue; string who = "-";
                    for (int k = 0; k <= 48; k++)
                    {
                        float t = k / 48f; Matrix4x4 m;
                        var rotHalf = new System.Func<float, Matrix4x4>(u => Matrix4x4.TRS(gb.center, Quaternion.AngleAxis(sp.yaw * u, Vector3.up), Vector3.one) * Matrix4x4.Translate(-gb.center));
                        if (t < 0.2f) m = Matrix4x4.Translate(Vector3.Lerp(a, b, t / 0.2f));
                        else if (t < 0.8f) { float u = (t - 0.2f) / 0.6f; m = Matrix4x4.Translate(Vector3.Lerp(b, c, u)) * rotHalf(u); }
                        else { float u = (t - 0.8f) / 0.2f; m = Matrix4x4.Translate(Vector3.Lerp(c, sp.delta + Vector3.up * 0.005f, u)) * rotHalf(1f); }
                        if (t >= 0.8f && k == 48) break;   // 最后贴垫面那一刻不算
                        var r = MinDistance(group.Select(g => Moved(g, m)).ToList(), staticAll, 0.10f);
                        if (r.d < worst) { worst = r.d; who = r.a + " ↔ " + r.b; }
                        if (worst <= best.worst) break;
                    }
                    if (worst > best.worst) best = (worst, h, si, who);
                }
            }
            if (best.si >= 0)
            {
                var sp = trySpots[best.si];
                var landC = sp.land.MultiplyPoint3x4(gb.center);
                md.AppendLine("## 搬运路线（引擎最低点高度 h，侧拔 → 升降 → 水平 → 落下）\n");
                md.AppendLine($"- 试了 {trySpots.Count} 个落点（工作台现状、离垫上物件最远的前 12 个）× h = 0.92…1.60 m（步长 40 mm）。");
                md.AppendLine($"- 最好的一条：h = **{best.h:F2} m**（引擎最低点），落点中心 {landC:F3}、绕竖直轴转 {sp.yaw:F0}°，全程最小间距 **{Mm(best.worst)} mm**（{best.who}）；落下后离垫上物件 {Mm(sp.clr)} mm。");
                md.AppendLine("- 路线不经过七号上方（侧拔后直接在七号左侧升降），水平段从维修座一侧移到操作垫。");
                js.Append($"\"path\":{{\"h\":{F(best.h)},\"land_center\":{V(landC)},\"land_yaw\":{F(sp.yaw)},\"land_delta\":{V(sp.delta)},\"min_mm\":{Mm(best.worst)},\"pair\":{S(best.who)},\"land_clear_mm\":{Mm(sp.clr)},\"start_center\":{V(gb.center)},\"side_mm\":400}},");
            }
            else md.AppendLine("**没有找到可行的搬运路线。**");
            js.Append("\"done\":true}");
            Write("engine_path.md", md.ToString());
            Write("engine_path.json", js.ToString());
            Debug.Log("[ArtAudit] engine_path 已写出");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
