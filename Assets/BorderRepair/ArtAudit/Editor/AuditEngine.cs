using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using static BorderRepair.ArtAudit.AuditCommon;

namespace BorderRepair.ArtAudit
{
    /// <summary>
    /// 第 3 步（只读，场景不保存）：整台左引擎替换。
    /// 1) 清单：Engine_L_Hinge 骨骼以下的全部网格（随引擎走）、机身上与左引擎相关的件（留在机身）、蒙皮线缆（两端跨界）；每件的路径、父对象、原点、轴、材质、网格、独立性。
    /// 2) 接口：随引擎走的件与留在机身的件之间 ≤ 2 mm 的接触对（真实网格），即实际的机械 / 电气接口。
    /// 3) 线缆：两根蒙皮线缆的骨骼权重、两端位置和最近的插头；把引擎外移 50 / 400 mm 时线缆的拉伸量。内部引线（刚性）两端最近的对象。
    /// 4) 取出：拔连接键 / 螺栓的通道；整台引擎侧向外拔 0…400 mm、抬高、搬到工作台操作垫、落下，全程与机身 / 维修座 / 工作台的最小间距。
    /// 5) 落点：操作垫上能放几台引擎（直立、原朝向），与台灯等的间距；零件盘 / 盒的尺寸对比。
    /// 6) 镜头：常用镜头下固定件的可见比例。
    /// 输出 assembly_inventory.json / .csv、engine_measure.md、engine_measure.json。
    /// </summary>
    public static class AuditEngine
    {
        static Transform robot, body, hinge, rotor;

        class Item
        {
            public Transform t; public Renderer r; public string group, role, basis, removeDir;
        }

        static string Role(Transform t)
        {
            if (t.GetComponent<SkinnedMeshRenderer>() != null) return "split_skinned_cable";
            if (t == hinge || t == rotor) return "bone";
            if (t.IsChildOf(hinge)) return "take";
            return "stay";
        }

        public static void Run()
        {
            AuditTray.Setup();   // 打开布局 B 场景（不保存），恢复静止姿态
            AuditTray.RestPose();
            robot = Find("UNIT07_RobotV4_DockReady");
            body = FindUnder(robot, "Body");
            hinge = FindUnder(robot, "Engine_L_Hinge");
            rotor = FindUnder(robot, "Engine_L_Rotor");
            var props = LoadProps();
            var md = new StringBuilder("# 整台左引擎替换实测（Unity，布局 B 场景只读打开，未保存）\n\n");
            var js = new StringBuilder("{");
            md.AppendLine($"- 七号实例 `{PathOf(robot)}`（源 `{RobotFbx}`），停靠在维修座锚点上（场景默认：落座、夹具闭合）。坐标：Unity 世界，米，Y 向上。七号前向 = Body +Z = {body.forward:F3}；七号自己的左 = Body −X = {(-body.right):F3}。");
            md.AppendLine($"- `Engine_L_Hinge` 骨骼原点 {hinge.position:F4}，转轴（骨骼本地 X）{hinge.right:F4}；`Engine_L_Rotor` 原点 {rotor.position:F4}，转轴 {rotor.right:F4}\n");

            // ---------------- 1) 清单
            var leftNames = new[] { "_L", "_L_", "Engine_L" };
            var all = robot.GetComponentsInChildren<Transform>(true).ToList();
            var items = new List<Item>();
            foreach (var t in all)
            {
                bool underHinge = t.IsChildOf(hinge);
                bool leftRelated = new[] { "ExternalCable_L", "AuxCable_L", "Harness_Engine_L", "BodySocket_L", "BodySocket_Knob_L", "PCB_Main_Connector_Engine_L", "Chassis_MountRail_L", "Chassis_ArmHardpoint_L" }.Any(n => t.name.StartsWith(n));
                if (!underHinge && !leftRelated) continue;
                if (t.name.StartsWith("Arm_L")) continue;   // 左机械臂与引擎无关
                var it = new Item { t = t, r = t.GetComponent<Renderer>() };
                it.role = Role(t);
                string clean = t.name;
                if (props.TryGetValue(clean, out var p)) it.removeDir = p;
                it.basis = underHinge ? "Unity 层级：在 Engine_L_Hinge 骨骼以下" : "Unity 层级：挂在 Body（或其它）骨骼下，名称与左引擎相关";
                items.Add(it);
            }
            // 与左引擎有接触的机身件（不管名字）：后面接口检查会补进来
            AuditTray.Jaw(AuditTray.L, 0f); AuditTray.Jaw(AuditTray.R, 0f); AuditTray.RestPose();
            var takeR = items.Where(i => i.role == "take" && i.r != null && i.r.enabled).Select(i => i.r).ToList();
            var stayAll = robot.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && !r.transform.IsChildOf(hinge) && !(r is SkinnedMeshRenderer)).ToList();
            var takeM = takeR.Select(World).Where(m => m != null).ToList();
            var stayM = stayAll.Select(World).Where(m => m != null).ToList();

            // ---------------- 2) 接口：随引擎走 ↔ 留在机身，≤ 2 mm
            md.AppendLine("## 接口：随引擎走的件 ↔ 留在机身的件（真实网格最近距离 ≤ 2 mm）\n\n| 随引擎走 | 留在机身 | 最近距离 | 接触点（世界） |\n|---|---|---|---|");
            js.Append("\"interfaces\":[");
            var contacts = new List<(string a, string b, float d, Vector3 p)>();
            foreach (var a in takeM)
                foreach (var b in stayM)
                {
                    float d = MinDistance(a, b, 0.002f, out var pa, out var pb);
                    if (d < 0.002f) contacts.Add((a.name, b.name, d, (pa + pb) / 2f));
                }
            foreach (var c in contacts.OrderBy(c => c.a))
            {
                md.AppendLine($"| {c.a} | {c.b} | {Mm(c.d)} mm | {c.p:F4} |");
                js.Append($"{{\"take\":{S(c.a)},\"stay\":{S(c.b)},\"mm\":{Mm(c.d)},\"at\":{V(c.p)}}},");
                if (!items.Any(i => i.t.name == c.b && i.role == "stay"))
                {
                    var tr = stayAll.First(r => r.name == c.b).transform;
                    items.Add(new Item { t = tr, r = tr.GetComponent<Renderer>(), role = "stay", basis = "实测：与随引擎走的件接触（≤ 2 mm）", removeDir = props.TryGetValue(tr.name, out var pp) ? pp : null });
                }
            }
            js.Append("{}],");
            md.AppendLine($"\n共 {contacts.Count} 对。没有出现在这张表里的机身件和引擎没有直接接触。\n");

            // ---------------- 3) 线缆
            md.AppendLine("## 蒙皮线缆与引线\n");
            js.Append("\"cables\":[");
            foreach (var name in new[] { "ExternalCable_L", "AuxCable_L" })
            {
                var sk = FindUnder(robot, name).GetComponent<SkinnedMeshRenderer>();
                var w0 = World(sk);
                var bw = sk.sharedMesh.boneWeights;
                var bones = sk.bones;
                int hingeIdx = Array.IndexOf(bones, hinge), bodyIdx = Array.IndexOf(bones, body);
                var wv = w0.v;
                // 两端：权重全在 Body 的顶点簇中心、全在 Hinge 的顶点簇中心
                Vector3 endBody = Avg(wv, Enumerable.Range(0, bw.Length).Where(i => Top(bw[i]) == bodyIdx && MaxW(bw[i]) > 0.99f));
                Vector3 endEng = Avg(wv, Enumerable.Range(0, bw.Length).Where(i => Top(bw[i]) == hingeIdx && MaxW(bw[i]) > 0.99f));
                int blended = bw.Count(b => MaxW(b) < 0.99f);
                // 每个顶点的 Hinge 权重沿线缆长度的分布：按到 Body 端的距离分 5 段
                float len = (endEng - endBody).magnitude;
                var segs = new float[5]; var cnt = new int[5];
                for (int i = 0; i < bw.Length; i++)
                {
                    float tpos = Mathf.Clamp01(Vector3.Dot(wv[i] - endBody, (endEng - endBody).normalized) / Mathf.Max(len, 1e-4f));
                    int k = Mathf.Min(4, (int)(tpos * 5));
                    segs[k] += WeightOf(bw[i], hingeIdx); cnt[k]++;
                }
                var plugB = World(FindUnder(robot, name + "_PlugBody").GetComponent<Renderer>());
                var plugE = World(FindUnder(robot, name + "_PlugEngine").GetComponent<Renderer>());
                float dB = MinDistance(w0, plugB, 0.05f, out _, out _), dE = MinDistance(w0, plugE, 0.05f, out _, out _);
                // 引擎外移时（移动 Hinge 骨骼）线缆的形状：两端距离变化
                float Stretch(float mm)
                {
                    var keep = hinge.position;
                    hinge.position = keep - body.right * mm / 1000f;
                    var w1 = World(sk);
                    var b1 = Avg(w1.v, Enumerable.Range(0, bw.Length).Where(i => Top(bw[i]) == bodyIdx && MaxW(bw[i]) > 0.99f));
                    var e1 = Avg(w1.v, Enumerable.Range(0, bw.Length).Where(i => Top(bw[i]) == hingeIdx && MaxW(bw[i]) > 0.99f));
                    hinge.position = keep;
                    return (e1 - b1).magnitude - len;
                }
                float s50 = Stretch(50f), s400 = Stretch(400f);
                md.AppendLine($"- `{PathOf(sk.transform)}`：SkinnedMeshRenderer，{sk.sharedMesh.vertexCount} 顶点，骨骼 [{string.Join(", ", bones.Select(b => b.name))}]，根骨骼 {sk.rootBone?.name}；材质 {string.Join("/", sk.sharedMaterials.Select(m => m.name))}");
                md.AppendLine($"  - 机身端（权重全在 Body）中心 {endBody:F4}，最近插头 `{name}_PlugBody` 距离 {Mm(dB)} mm；引擎端（权重全在 Engine_L_Hinge）中心 {endEng:F4}，最近插头 `{name}_PlugEngine` 距离 {Mm(dE)} mm；两端直线距离 {Mm(len)} mm");
                md.AppendLine($"  - 混合权重顶点 {blended}/{bw.Length}；沿线缆从机身端到引擎端 5 段的平均 Hinge 权重：{string.Join(" → ", segs.Select((s, k) => cnt[k] > 0 ? (s / cnt[k]).ToString("F2") : "—"))}");
                md.AppendLine($"  - 引擎沿七号左向外移 50 mm：两端间距 +{Mm(s50)} mm；外移 400 mm：+{Mm(s400)} mm（蒙皮只有两根骨骼，线缆被直线拉长，不会下垂）");
                md.AppendLine($"  - 插头拆卸方向（FBX 自定义属性 remove_dir，Blender 坐标）：机身端 {(props.TryGetValue(name + "_PlugBody", out var r1) ? r1 : "无")}，引擎端 {(props.TryGetValue(name + "_PlugEngine", out var r2) ? r2 : "无")}\n");
                js.Append($"{{\"path\":{S(PathOf(sk.transform))},\"bones\":[{string.Join(",", bones.Select(b => S(b.name)))}],\"verts\":{bw.Length},\"blended\":{blended},\"end_body\":{V(endBody)},\"end_engine\":{V(endEng)},\"plug_body_gap_mm\":{Mm(dB)},\"plug_engine_gap_mm\":{Mm(dE)},\"len_mm\":{Mm(len)},\"stretch50_mm\":{Mm(s50)},\"stretch400_mm\":{Mm(s400)},\"hinge_weight_by_segment\":[{string.Join(",", segs.Select((s, k) => F(cnt[k] > 0 ? s / cnt[k] : -1)))}]}},");
            }
            // 刚性引线 / 线束：两端最近的对象
            foreach (var name in new[] { "Engine_InternalLead_L", "Harness_Engine_L", "Harness_Engine_L_PlugA", "Harness_Engine_L_PlugB", "PCB_Main_Connector_Engine_L", "ExternalCable_Clip_L", "BodySocket_L", "BodySocket_Knob_L" })
            {
                var tr = FindUnder(robot, name);
                if (tr == null) { md.AppendLine($"- `{name}`：场景里没有"); continue; }
                var m = World(tr.GetComponent<Renderer>());
                var c = m.v.Aggregate(Vector3.zero, (a, v) => a + v) / m.v.Length;
                var far = m.v.OrderByDescending(v => (v - c).sqrMagnitude).First();
                var far2 = m.v.OrderByDescending(v => (v - far).sqrMagnitude).First();
                string Near(Vector3 pt)
                {
                    var probe = new WMesh { name = "probe", v = new[] { pt }, tri = new int[0], b = new Bounds(pt, Vector3.zero) };
                    var cands = robot.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.transform != tr && !(r is SkinnedMeshRenderer)).Select(World).Where(x => x != null).ToList();
                    var best = cands.Select(x => (x.name, d: MinDistance(probe, x, 0.03f, out _, out _))).OrderBy(x => x.d).Take(3).ToList();
                    return string.Join("、", best.Select(b => $"{b.name} {Mm(b.d)} mm"));
                }
                var sideA = tr.IsChildOf(hinge) ? "随引擎" : "机身";
                md.AppendLine($"- `{PathOf(tr)}`（{sideA}，{tr.GetComponent<Renderer>().GetType().Name}，材质 {tr.GetComponent<Renderer>().sharedMaterial.name}）：端点 A {far:F4} 附近：{Near(far)}；端点 B {far2:F4} 附近：{Near(far2)}");
                js.Append($"{{\"path\":{S(PathOf(tr))},\"side\":{S(sideA)},\"endA\":{V(far)},\"endA_near\":{S(Near(far))},\"endB\":{V(far2)},\"endB_near\":{S(Near(far2))}}},");
            }
            js.Append("{}],");
            md.AppendLine();

            // ---------------- 4) 取出路线
            Extraction(md, js, takeR, stayAll);

            // ---------------- 清单输出
            WriteInventory(items, contacts);
            js.Append("\"done\":true}");
            Write("engine_measure.json", js.ToString());
            Write("engine_measure.md", md.ToString());
            Debug.Log("[ArtAudit] engine_measure 已写出");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static int Top(BoneWeight w) => w.weight1 > w.weight0 ? w.boneIndex1 : w.boneIndex0;
        static float MaxW(BoneWeight w) => Mathf.Max(w.weight0, w.weight1, w.weight2, w.weight3);
        static float WeightOf(BoneWeight w, int idx) => (w.boneIndex0 == idx ? w.weight0 : 0) + (w.boneIndex1 == idx ? w.weight1 : 0) + (w.boneIndex2 == idx ? w.weight2 : 0) + (w.boneIndex3 == idx ? w.weight3 : 0);
        static Vector3 Avg(Vector3[] v, IEnumerable<int> idx) { var l = idx.ToList(); return l.Count == 0 ? Vector3.zero : l.Aggregate(Vector3.zero, (a, i) => a + v[i]) / l.Count; }

        /// <summary>raw_fbx_props.json（Blender 只读导入 FBX 得到的自定义属性）里的 remove_dir。</summary>
        static Dictionary<string, string> LoadProps()
        {
            var res = new Dictionary<string, string>();
            var path = System.IO.Path.Combine(OutDir, "raw_fbx_props.json");
            if (!System.IO.File.Exists(path)) return res;
            var txt = System.IO.File.ReadAllText(path);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(txt, "\"([A-Za-z0-9_]+)\": \\{[^{}]*?\"props\": \\{([^}]*)\\}"))
            {
                var dir = System.Text.RegularExpressions.Regex.Match(m.Groups[2].Value, "\"remove_dir\": \"([^\"]+)\"");
                if (dir.Success) res[m.Groups[1].Value] = dir.Groups[1].Value;
            }
            return res;
        }

        // ------------------------------------------------------------------ 取出 / 搬运 / 落点

        static WMesh Moved(WMesh m, Matrix4x4 mtx)
        {
            var v = m.v.Select(p => mtx.MultiplyPoint3x4(p)).ToArray();
            var b = new Bounds(v[0], Vector3.zero); foreach (var p in v) b.Encapsulate(p);
            return new WMesh { name = m.name, v = v, tri = m.tri, b = b };
        }

        static void Extraction(StringBuilder md, StringBuilder js, List<Renderer> takeR, List<Renderer> stayAll)
        {
            var dock = Find("Unit07ServiceDock");
            var bench = Find("WorkbenchArea");
            var mat = FindUnder(bench, "Bench_Mat");
            var envR = dock.GetComponentsInChildren<Renderer>(true).Concat(bench.GetComponentsInChildren<Renderer>(true))
                           .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.bounds.max.y < 2.2f).ToList();
            var envM = envR.Select(World).Where(m => m != null).ToList();
            var robotStay = stayAll.Select(World).Where(m => m != null).ToList();
            // 连接键 / 螺栓先单独拔出，其余整台走
            var keyNames = new[] { "ConnectionKey_L", "ConnectionKey_Bolt_L" };
            var group = takeR.Where(r => !keyNames.Contains(r.name)).Select(World).Where(m => m != null).ToList();
            var gb = group[0].b; foreach (var g in group) gb.Encapsulate(g.b);
            var left = -body.right;
            md.AppendLine($"## 取出与搬运（真实网格，夹具闭合、落座状态）\n\n- 随引擎整体移动的网格 {group.Count} 件（不含先拔出的连接键 / 螺栓），整体包围盒 {Mm(gb.size.x)} × {Mm(gb.size.y)} × {Mm(gb.size.z)} mm（世界轴），中心 {gb.center:F4}，最低点 y {gb.min.y:F3}");
            js.Append($"\"assembly\":{{\"members\":{group.Count},\"bounds_center\":{V(gb.center)},\"bounds_size\":{V(gb.size)}}},");

            // 连接键、螺栓：沿各方向拔，多远脱离配合件（距离 > 0.5 mm）、途中离其它件最近多少
            var engaged = new Dictionary<string, string[]> { ["ConnectionKey_L"] = new[] { "BodySocket_L", "EngineAnchor_L", "ConnectionKey_Bolt_L" }, ["ConnectionKey_Bolt_L"] = new[] { "EngineAnchor_L", "ConnectionKey_L" } };
            md.AppendLine("\n| 固定件 | 方向（七号 Body） | 拔多远脱开配合件 | 途中离其它件最小 | 最近的其它件 |\n|---|---|---|---|---|");
            foreach (var kn in keyNames)
            {
                var kr = takeR.FirstOrDefault(r => r.name == kn);
                if (kr == null) { md.AppendLine($"| {kn} | 没有找到 | | | |"); continue; }
                var km = World(kr);
                var all = robotStay.Concat(takeR.Select(World).Where(m => m != null)).Concat(envM).Where(m => m.name != kn).ToList();
                var mates = all.Where(m => engaged[kn].Contains(m.name)).ToList();
                var others = all.Where(m => !engaged[kn].Contains(m.name)).ToList();
                foreach (var dirName in new[] { "+X", "-X", "+Y", "-Y", "+Z", "-Z" })
                {
                    var dl = dirName == "+X" ? Vector3.right : dirName == "-X" ? Vector3.left : dirName == "+Y" ? Vector3.up : dirName == "-Y" ? Vector3.down : dirName == "+Z" ? Vector3.forward : Vector3.back;
                    var dw = body.TransformDirection(dl);
                    float freeAt = float.NaN, minOther = float.MaxValue; string who = "-";
                    for (float st = 0.005f; st <= 0.1201f; st += 0.005f)
                    {
                        var moved = new List<WMesh> { Moved(km, Matrix4x4.Translate(dw * st)) };
                        var o = MinDistance(moved, others, 0.03f);
                        if (o.d < minOther) { minOther = o.d; who = o.b; }
                        if (float.IsNaN(freeAt) && MinDistance(moved, mates, 0.01f).d > 0.0005f) { freeAt = st; break; }
                    }
                    md.AppendLine($"| {kn} | {dirName}（世界 {dw:F2}） | {(float.IsNaN(freeAt) ? "120 mm 内没脱开" : Mm(freeAt) + " mm")} | {Mm(minOther)} mm | {who} |");
                    js.Append($"\"key_{kn}_{dirName}\":{{\"free_mm\":{(float.IsNaN(freeAt) ? "null" : Mm(freeAt))},\"min_other_mm\":{Mm(minOther)},\"nearest\":{S(who)}}},");
                }
            }

            // 侧拔 0…400 mm
            md.AppendLine("\n| 阶段 | 位移 | 与机身 / 维修座 / 工作台最小间距 | 最近的对象（引擎件 ↔ 环境件） |\n|---|---|---|---|");
            js.Append("\"path\":[");
            var staticAll = robotStay.Concat(envM).ToList();
            float worstSide = float.MaxValue;
            for (float d = 0.01f; d <= 0.4001f; d += 0.01f)
            {
                var mtx = Matrix4x4.Translate(left * d);
                var r = MinDistance(group.Select(g => Moved(g, mtx)).ToList(), staticAll, 0.10f);
                worstSide = Mathf.Min(worstSide, r.d);
                if (Mathf.Abs(d * 100 - Mathf.Round(d * 100)) < 0.01f && (Mathf.RoundToInt(d * 1000) % 50 == 0 || d <= 0.05f || r.d < 0.005f))
                    md.AppendLine($"| 侧拔 | {Mm(d)} mm | {Mm(r.d)} mm | {r.a} ↔ {r.b} |");
                js.Append($"{{\"stage\":\"side\",\"mm\":{Mm(d)},\"min_mm\":{Mm(r.d)},\"pair\":{S(r.a + "↔" + r.b)}}},");
            }
            // 落点：操作垫上方找空位（直立、原朝向；绕竖直轴转 0 / 90 / 180 / 270°）
            var matB = mat.GetComponent<Renderer>().bounds;
            float matTop = matB.max.y;
            var benchOnMat = bench.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy && r.transform != mat && r.bounds.max.y > matTop + 0.001f && r.bounds.min.y < matTop + 0.8f &&
                                                                                       r.bounds.max.x > matB.min.x && r.bounds.min.x < matB.max.x && r.bounds.max.z > matB.min.z && r.bounds.min.z < matB.max.z).ToList();
            var benchOnMatM = benchOnMat.Select(World).Where(m => m != null).ToList();
            var startC = gb.center + left * 0.40f;
            var movable = new Func<string, bool>(n => n == "Diag_Probe");
            List<(Vector3 delta, float yaw, float clr, string who)> Search(List<WMesh> obst)
            {
                var res = new List<(Vector3, float, float, string)>();
                foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
                {
                    var rot = Matrix4x4.TRS(gb.center, Quaternion.AngleAxis(yaw, Vector3.up), Vector3.one) * Matrix4x4.Translate(-gb.center);
                    var rg = group.Select(g => Moved(g, rot)).ToList();
                    var rb = rg[0].b; foreach (var g in rg) rb.Encapsulate(g.b);
                    for (float x = matB.min.x; x <= matB.max.x; x += 0.03f)
                        for (float z = matB.min.z; z <= matB.max.z; z += 0.03f)
                        {
                            var delta = new Vector3(x - rb.center.x, matTop + 0.001f - rb.min.y, z - rb.center.z);
                            var fp = new Bounds(rb.center + delta, rb.size);
                            if (fp.min.x < matB.min.x || fp.max.x > matB.max.x || fp.min.z < matB.min.z || fp.max.z > matB.max.z) continue;   // 整台都在垫子上
                            var mtx = Matrix4x4.Translate(delta) * rot;
                            var r = MinDistance(group.Select(g => Moved(g, mtx)).ToList(), obst, 0.05f);
                            res.Add((delta, yaw, r.d, r.b));
                        }
                }
                return res;
            }
            var allSpots = Search(benchOnMatM);
            var clearedObst = benchOnMatM.Where(m => !movable(m.name)).ToList();
            var allSpotsCleared = Search(clearedObst);
            var spots = allSpots.Where(sp => sp.clr > 0.005f).ToList();
            var spotsCleared = allSpotsCleared.Where(sp => sp.clr > 0.005f).ToList();
            var bestAsIs = allSpots.OrderByDescending(sp => sp.clr).FirstOrDefault();
            var bestCleared = allSpotsCleared.OrderByDescending(sp => sp.clr).FirstOrDefault();
            md.AppendLine($"\n- 操作垫 `{PathOf(mat)}`：{Mm(matB.size.x)} × {Mm(matB.size.z)} mm（世界 x × z），顶面 y {matTop:F3}；垫面上方的工作台物件 {benchOnMat.Count} 件：{string.Join("、", benchOnMat.Select(r => r.name))}");
            md.AppendLine($"- 引擎直立（原安装朝向）、整台都在垫子上（30 mm 网格，绕竖直轴 0/90/180/270°），共试 {allSpots.Count} 个位置：");
            md.AppendLine($"  - 工作台现状：离其它物件 > 5 mm 的落点 **{spots.Count} 个**；最好的位置离 {bestAsIs.who} 只有 {Mm(bestAsIs.clr)} mm");
            md.AppendLine($"  - 移走诊断探针（Diag_Probe）后：**{spotsCleared.Count} 个**；最好的位置离 {bestCleared.who} {Mm(bestCleared.clr)} mm");
            js.Append("{}],");
            js.Append($"\"mat\":{{\"path\":{S(PathOf(mat))},\"size_xz\":[{F(matB.size.x)},{F(matB.size.z)}],\"top_y\":{F(matTop)},\"objects_on_mat\":[{string.Join(",", benchOnMat.Select(r => S(r.name)))}],\"tried\":{allSpots.Count},\"spots_as_is\":{spots.Count},\"best_as_is_mm\":{Mm(bestAsIs.clr)},\"best_as_is_blocker\":{S(bestAsIs.who)},\"spots_props_cleared\":{spotsCleared.Count},\"best_cleared_mm\":{Mm(bestCleared.clr)},\"best_cleared_blocker\":{S(bestCleared.who)}}},");
            if (spots.Count == 0) spots = spotsCleared;   // 现状放不下时，用移走道具后的落点继续测搬运

            // 两台（旧件 + 新件）能不能同时放下：在可行落点里找两个互不重叠（包围盒间隔 > 20 mm）的
            (Vector3 d1, Vector3 d2, float y1, float y2)? pair = null;
            Bounds FootAt((Vector3 delta, float yaw, float clr, string who) s)
            {
                var rot = Matrix4x4.TRS(gb.center, Quaternion.AngleAxis(s.yaw, Vector3.up), Vector3.one) * Matrix4x4.Translate(-gb.center);
                var rg = group.Select(g => Moved(g, Matrix4x4.Translate(s.delta) * rot)).ToList();
                var rb = rg[0].b; foreach (var g in rg) rb.Encapsulate(g.b); return rb;
            }
            var foots = spots.Select(s => (s, FootAt(s))).ToList();
            for (int i = 0; i < foots.Count && pair == null; i++)
                for (int j = i + 1; j < foots.Count; j++)
                {
                    var a = foots[i].Item2; a.Expand(0.04f);
                    if (!a.Intersects(foots[j].Item2)) { pair = (foots[i].s.delta, foots[j].s.delta, foots[i].s.yaw, foots[j].s.yaw); break; }
                }
            md.AppendLine($"- 两台引擎（旧件落点 + 新件展示）同时直立放在垫子上、互相间隔 ≥ 20 mm：{(pair == null ? "**放不下**" : $"可以，例如旧件中心 {(gb.center + pair.Value.d1):F3}（转 {pair.Value.y1:F0}°）、新件中心 {(gb.center + pair.Value.d2):F3}（转 {pair.Value.y2:F0}°）")}");
            js.Append(pair == null ? "\"two_on_mat\":false," : $"\"two_on_mat\":true,\"two_d1\":{V(pair.Value.d1)},\"two_d2\":{V(pair.Value.d2)},\"two_y1\":{F(pair.Value.y1)},\"two_y2\":{F(pair.Value.y2)},");

            // 搬运：侧拔到 400 mm → 抬到搬运高度 → 平移到第一个落点上方 → 落下
            if (spots.Count > 0)
            {
                var land = spots.OrderByDescending(s => s.clr).First();
                var landMtx = Matrix4x4.Translate(land.delta) * Matrix4x4.TRS(gb.center, Quaternion.AngleAxis(land.yaw, Vector3.up), Vector3.one) * Matrix4x4.Translate(-gb.center);
                var landed = group.Select(g => Moved(g, landMtx)).ToList();
                var lb = landed[0].b; foreach (var g in landed) lb.Encapsulate(g.b);
                // 搬运高度：沿途最高障碍物（2.2 m 以下）+ 30 mm
                var lane = new Bounds(new Vector3(startC.x, 1f, startC.z), Vector3.zero); lane.Encapsulate(new Vector3(lb.center.x, 1f, lb.center.z));
                lane.Expand(new Vector3(gb.size.x + 0.05f, 10f, gb.size.z + 0.05f));
                float top = envR.Concat(stayAll).Where(r => r.bounds.Intersects(lane) && r.bounds.max.y < 2.2f).Max(r => r.bounds.max.y);
                float liftTo = top + 0.03f - gb.min.y;   // 引擎最低点高过障碍 30 mm 需要的上升量
                md.AppendLine($"- 搬运路线：侧拔 400 mm → 竖直抬高 {Mm(Mathf.Max(0, liftTo))} mm（沿途最高障碍顶 y {top:F3}）→ 水平移到落点上方 → 竖直落到垫面（落点 {lb.center:F3}，转 {land.yaw:F0}°，落下后离最近物件 {Mm(land.clr)} mm：{land.who}）");
                float worstCarry = float.MaxValue; string worstWho = "-";
                var p0 = left * 0.40f; var p1 = p0 + Vector3.up * Mathf.Max(0, liftTo);
                var p3 = new Vector3(land.delta.x, 0, land.delta.z) + Vector3.up * Mathf.Max(0, liftTo);
                for (int k = 0; k <= 40; k++)
                {
                    float t = k / 40f;
                    Matrix4x4 m;
                    if (t < 0.25f) m = Matrix4x4.Translate(Vector3.Lerp(p0, p1, t / 0.25f));
                    else if (t < 0.75f) { float u = (t - 0.25f) / 0.5f; m = Matrix4x4.Translate(Vector3.Lerp(p1, p3, u)) * Matrix4x4.TRS(gb.center, Quaternion.AngleAxis(land.yaw * u, Vector3.up), Vector3.one) * Matrix4x4.Translate(-gb.center); }
                    else { float u = (t - 0.75f) / 0.25f; m = Matrix4x4.Translate(Vector3.Lerp(p3, land.delta + Vector3.up * 0.01f, u)) * Matrix4x4.TRS(gb.center, Quaternion.AngleAxis(land.yaw, Vector3.up), Vector3.one) * Matrix4x4.Translate(-gb.center); }
                    var r = MinDistance(group.Select(g => Moved(g, m)).ToList(), staticAll.Where(s => s.name != mat.name).ToList(), 0.10f);
                    if (r.d < worstCarry) { worstCarry = r.d; worstWho = r.a + " ↔ " + r.b; }
                }
                md.AppendLine($"- 搬运全程（不含最后贴垫面）最小间距 **{Mm(worstCarry)} mm**（{worstWho}）；侧拔 0–400 mm 段最小 **{Mm(worstSide)} mm**");
                js.Append($"\"carry\":{{\"lift_mm\":{Mm(Mathf.Max(0, liftTo))},\"land_center\":{V(lb.center)},\"land_yaw\":{F(land.yaw)},\"land_clear_mm\":{Mm(land.clr)},\"carry_min_mm\":{Mm(worstCarry)},\"carry_min_pair\":{S(worstWho)},\"side_min_mm\":{Mm(worstSide)},\"land_delta\":{V(land.delta)}}},");
            }

            // 容器尺寸对比
            md.AppendLine("\n| 容器 | 尺寸（世界包围盒 x × y × z，mm） | 能否容纳整台引擎（{0}） |\n|---|---|---|".Replace("{0}", $"{Mm(gb.size.x)} × {Mm(gb.size.y)} × {Mm(gb.size.z)}"));
            foreach (var n in new[] { "Bench_Mat", "Tray_OldParts", "Tray_Screws", "Box_Bearings", "Dock_PartsTray", "Dock_MagneticBox", "Dock_TrayShelf" })
            {
                var tr = Find(n); if (tr == null) continue;
                var b = tr.GetComponent<Renderer>().bounds;
                bool fits = n == "Bench_Mat" ? spots.Count > 0 : Mathf.Max(b.size.x, b.size.z) >= Mathf.Min(gb.size.x, gb.size.z) && Mathf.Min(b.size.x, b.size.z) >= Mathf.Min(gb.size.x, gb.size.z) * 0.9f;
                md.AppendLine($"| `{PathOf(tr)}` | {Mm(b.size.x)} × {Mm(b.size.y)} × {Mm(b.size.z)} | {(fits ? "可以（见上）" : "不能")} |");
            }
            md.AppendLine();
        }

        // ------------------------------------------------------------------ 清单

        static void WriteInventory(List<Item> items, List<(string a, string b, float d, Vector3 p)> contacts)
        {
            var csv = new StringBuilder("path,name,parent,role,renderer,material,mesh,tris,origin_world,origin_local,rot_axis_world_localX,bounds_min,bounds_max,remove_dir_blender,independent,contacts,basis\n");
            var json = new StringBuilder("[");
            string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            bool first = true;
            foreach (var it in items.OrderBy(i => i.role).ThenBy(i => PathOf(i.t)))
            {
                var t = it.t; var r = it.r;
                Mesh mesh = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r != null ? r.GetComponent<MeshFilter>()?.sharedMesh : null;
                string mat = r != null ? string.Join("/", r.sharedMaterials.Select(m => m ? m.name : "")) : "";
                // 独立：自己的 GameObject、自己的渲染器、子对象里没有别的渲染器
                bool indep = r != null && t.GetComponentsInChildren<Renderer>(true).Length == 1;
                var cs = string.Join("; ", contacts.Where(c => c.a == t.name || c.b == t.name).Select(c => (c.a == t.name ? c.b : c.a) + " " + Mm(c.d) + "mm"));
                string role = it.role == "take" ? "随引擎拆下" : it.role == "stay" ? "留在机身" : it.role == "bone" ? "骨骼（动画写入，不移动）" : "蒙皮线缆（两端跨界）";
                csv.AppendLine(string.Join(",", Csv(PathOf(t)), Csv(t.name), Csv(t.parent?.name), Csv(role), Csv(r?.GetType().Name ?? "无（变换 / 骨骼）"), Csv(mat), Csv(mesh?.name),
                    mesh != null ? (mesh.triangles.Length / 3).ToString() : "", Csv(V(t.position)), Csv(V(t.localPosition)), Csv(V(t.right)),
                    Csv(r != null ? V(r.bounds.min) : ""), Csv(r != null ? V(r.bounds.max) : ""), Csv(it.removeDir), indep ? "是" : r == null ? "—" : "否（子对象另有渲染器）", Csv(cs), Csv(it.basis)));
                if (!first) json.Append(","); first = false;
                json.Append($"{{\"path\":{S(PathOf(t))},\"name\":{S(t.name)},\"parent\":{S(t.parent?.name)},\"role\":{S(role)},\"renderer\":{S(r?.GetType().Name)},\"material\":{S(mat)},\"mesh\":{S(mesh?.name)},\"tris\":{(mesh != null ? (mesh.triangles.Length / 3).ToString() : "null")}," +
                            $"\"origin_world\":{V(t.position)},\"origin_local\":{V(t.localPosition)},\"local_rot\":{V(t.localEulerAngles)},\"axis_x_world\":{V(t.right)},\"axis_y_world\":{V(t.up)},\"axis_z_world\":{V(t.forward)}," +
                            (r != null ? $"\"bounds_min\":{V(r.bounds.min)},\"bounds_max\":{V(r.bounds.max)}," : "") +
                            $"\"remove_dir_blender\":{S(it.removeDir)},\"independent\":{(indep ? "true" : "false")},\"contacts\":{S(cs)},\"basis\":{S(it.basis)}}}");
            }
            json.Append("]");
            Write("assembly_inventory.csv", "\uFEFF" + csv);
            Write("assembly_inventory.json", json.ToString());
        }
    }
}
