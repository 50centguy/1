using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using static BorderRepair.Art.Unit07TrayVariant.AuditRecheck.AuditCommon;

namespace BorderRepair.Art.Unit07TrayVariant.AuditRecheck
{
    /// <summary>
    /// 第 2 步（只读，场景不保存）：第一晚“端盘”用的零件盘与七号夹爪。
    /// 1) 零件盘 Dock_PartsTray：从真实网格量盘体、提手横杆（中心、方向、长度、管径）、原点。
    /// 2) 夹爪：各动画片段里每个关节绕本地 X 的角度范围；Closed / Half / Open 三个姿态的齿间距；夹住 10 mm 横杆时的夹爪开度与咬合中心。
    /// 3) 双手端盘：在七号本体坐标里搜索盘的位置和两臂关节角，让两侧咬合中心落在提手横杆上（两种握法：纵握 = 横杆沿夹爪指向；横握 = 横杆沿爪轴）。
    ///    对可行解测：盘与机身 / 机械臂的最小间距，机械臂与机身的间距，关节角是否超出已有动画用过的范围。
    /// 4) 维修座上方悬停 → 落座的过程中，手里的盘与维修座的最小间距（判断落座前是否必须先放下）。
    /// 5) 参考姿态：正常、左侧下沉（整机绕前向轴横滚）、盘面倾斜（只降左手）、盘面前倾。
    /// 输出 tray_measure.json、tray_measure.md，参考图另由 AuditRender 渲染。
    /// </summary>
    public static class AuditTray
    {
        public class Arm
        {
            public string side;
            public Transform pivot, shoulder, elbow, wrist, jawU, jawL;
            public Quaternion[] rest;
            public Renderer teethU, teethL;
            public Transform[] J => new[] { pivot, shoulder, elbow, wrist };
            public Vector3 gripLocal, pointLocal, hingeLocal;   // 咬合中心、指向、爪轴（腕骨本地）
            public float jawT;                                  // 0 = Closed，1 = Half，2 = Open
            public Quaternion jUc, jLc, jUh, jLh, jUo, jLo;
        }

        static Transform robot;
        public static Arm L, R;
        static Transform tray;
        static Quaternion trayRestRot;   // 托盘相对七号 Body 的朝向（维修座上的原始朝向）
        public static Transform Robot => robot;

        public static void Setup()
        {
            OpenSceneB();
            robot = Find("UNIT07_RobotV4_DockReady");
            tray = Find("Dock_PartsTray");
            L = MakeArm("L"); R = MakeArm("R");
            trayRestRot = Quaternion.Inverse(FindUnder(robot, "Body").rotation) * tray.rotation;
        }

        static Arm MakeArm(string s)
        {
            var a = new Arm { side = s };
            a.pivot = FindUnder(robot, $"Arm_{s}_RootPivot");
            a.shoulder = FindUnder(robot, $"Arm_{s}_Shoulder");
            a.elbow = FindUnder(robot, $"Arm_{s}_Elbow");
            a.wrist = a.elbow.Cast<Transform>().First(t => t.name == $"Arm_{s}_Wrist");
            a.jawU = a.wrist.Cast<Transform>().First(t => t.name == $"Arm_{s}_JawUpper");
            a.jawL = a.wrist.Cast<Transform>().First(t => t.name == $"Arm_{s}_JawLower");
            a.rest = a.J.Select(t => t.localRotation).ToArray();
            a.teethU = FindUnder(a.jawU, $"Arm_{s}_JawUpper_Teeth").GetComponent<Renderer>();
            a.teethL = FindUnder(a.jawL, $"Arm_{s}_JawLower_Teeth").GetComponent<Renderer>();
            return a;
        }

        public static void Pose(Arm a, float[] ang)
        {
            var j = a.J;
            for (int i = 0; i < 4; i++) j[i].localRotation = a.rest[i] * Quaternion.AngleAxis(ang[i], Vector3.right);
        }

        public static void Jaw(Arm a, float t)
        {
            if (t <= 1f) { a.jawU.localRotation = Quaternion.Slerp(a.jUc, a.jUh, t); a.jawL.localRotation = Quaternion.Slerp(a.jLc, a.jLh, t); }
            else { a.jawU.localRotation = Quaternion.Slerp(a.jUh, a.jUo, t - 1f); a.jawL.localRotation = Quaternion.Slerp(a.jLh, a.jLo, t - 1f); }
        }

        static AnimationClip Clip(string n) => AssetDatabase.LoadAllAssetsAtPath(RobotFbx).OfType<AnimationClip>().First(c => c.name == n);

        /// <summary>恢复导入时的静态姿态（Idle_Hover 第 0 帧）。</summary>
        public static void RestPose() => Clip("Idle_Hover").SampleAnimation(robot.gameObject, 0f);

        static float AngleX(Quaternion rest, Quaternion now, out float offAxis)
        {
            var q = Quaternion.Inverse(rest) * now;
            if (q.w < 0) { q.x = -q.x; q.y = -q.y; q.z = -q.z; q.w = -q.w; }
            offAxis = 2f * Mathf.Asin(Mathf.Clamp(Mathf.Sqrt(q.y * q.y + q.z * q.z), 0f, 1f)) * Mathf.Rad2Deg;
            return 2f * Mathf.Atan2(q.x, q.w) * Mathf.Rad2Deg;
        }

        // ------------------------------------------------------------------ 托盘

        public class TrayInfo
        {
            public Vector3 floorMinL, floorMaxL, boundsMinL, boundsMaxL;   // 托盘本地
            public Vector3[] barCenterL = new Vector3[2], barDirL = new Vector3[2];
            public float[] barLen = new float[2], barDia = new float[2];
            public int tris;
        }

        public static TrayInfo MeasureTray()
        {
            var mesh = tray.GetComponent<MeshFilter>().sharedMesh;
            var v = mesh.vertices;
            var info = new TrayInfo { tris = mesh.triangles.Length / 3, boundsMinL = mesh.bounds.min, boundsMaxL = mesh.bounds.max };
            // 托盘长轴 = 本地 X（Blender 脚本：x −0.70…−0.44）。盘体 = |x| ≤ 盘壁外缘；提手 = 盘壁以外的顶点
            float wallMaxX = 0.13f + 0.0005f;
            var body = v.Where(p => Mathf.Abs(p.x) <= wallMaxX).ToList();
            info.floorMinL = body.Aggregate(Vector3.Min); info.floorMaxL = body.Aggregate(Vector3.Max);
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? 1f : -1f;
                var h = v.Where(p => p.x * sign > wallMaxX).ToList();
                float xm = h.Max(p => p.x * sign);
                var bar = h.Where(p => p.x * sign > xm - 0.0101f).ToList();   // 外端横杆（管径 10 mm）
                var mn = bar.Aggregate(Vector3.Min); var mx = bar.Aggregate(Vector3.Max);
                var size = mx - mn;
                int axis = size.x > size.y ? (size.x > size.z ? 0 : 2) : (size.y > size.z ? 1 : 2);
                var dir = Vector3.zero; dir[axis] = 1f;
                // 横杆直段：沿长轴去掉两端弯头（各 1 个管径）
                info.barCenterL[s] = (mn + mx) / 2f;
                info.barDirL[s] = dir;
                info.barLen[s] = size[axis];
                var other = new List<float>(); for (int k = 0; k < 3; k++) if (k != axis) other.Add(size[k]);
                info.barDia[s] = other.Min();
            }
            return info;
        }

        // ------------------------------------------------------------------ 夹爪

        public static void MeasureJaw(Arm a, StringBuilder md, StringBuilder js)
        {
            var poses = new[] { ("Closed", "Pose_Gripper_Closed"), ("Half", "Pose_Gripper_Half"), ("Open", "Pose_Gripper_Open") };
            foreach (var (n, c) in poses)
            {
                Clip(c).SampleAnimation(robot.gameObject, 0f);
                if (n == "Closed") { a.jUc = a.jawU.localRotation; a.jLc = a.jawL.localRotation; }
                if (n == "Half") { a.jUh = a.jawU.localRotation; a.jLh = a.jawL.localRotation; }
                if (n == "Open") { a.jUo = a.jawU.localRotation; a.jLo = a.jawL.localRotation; }
            }
            RestPose();
            js.Append($"\"jaw_{a.side}\":{{");
            foreach (var (t, n) in new[] { (0f, "Closed"), (1f, "Half"), (2f, "Open") })
            {
                Jaw(a, t);
                float gap = MinDistance(World(a.teethU), World(a.teethL), 0.2f, out var pa, out var pb);
                float ua = AngleX(a.jUc, a.jawU.localRotation, out _), la = AngleX(a.jLc, a.jawL.localRotation, out _);
                md.AppendLine($"| {a.side} | {n} | {Mm(gap)} | {ua:F1}° / {la:F1}° |");
                js.Append($"\"{n}\":{{\"teeth_gap_mm\":{Mm(gap)},\"upper_deg_from_closed\":{F(ua)},\"lower_deg_from_closed\":{F(la)}}},");
            }
            // 夹住 10 mm 横杆：在 Closed–Half 之间找齿间距 = 10 mm 的开度
            float lo = 0f, hi = 1f;
            Jaw(a, 0f);
            float g0 = MinDistance(World(a.teethU), World(a.teethL), 0.2f, out _, out _);
            if (g0 >= 0.010f) a.jawT = 0f;
            else
            {
                for (int i = 0; i < 20; i++)
                {
                    float m = (lo + hi) / 2f; Jaw(a, m);
                    float g = MinDistance(World(a.teethU), World(a.teethL), 0.2f, out _, out _);
                    if (g < 0.010f) lo = m; else hi = m;
                }
                a.jawT = (lo + hi) / 2f;
            }
            Jaw(a, a.jawT);
            float gapT = MinDistance(World(a.teethU), World(a.teethL), 0.2f, out var p1, out var p2);
            var grip = (p1 + p2) / 2f;
            a.gripLocal = a.wrist.InverseTransformPoint(grip);
            a.hingeLocal = a.wrist.InverseTransformDirection(a.jawU.right).normalized;
            var point = Vector3.ProjectOnPlane(grip - a.wrist.position, a.jawU.right).normalized;
            a.pointLocal = a.wrist.InverseTransformDirection(point).normalized;
            js.Append($"\"bar10_jawT\":{F(a.jawT)},\"bar10_gap_mm\":{Mm(gapT)},\"closed_gap_mm\":{Mm(g0)},\"grip_local_wrist\":{V(a.gripLocal)},\"point_local_wrist\":{V(a.pointLocal)},\"hinge_local_wrist\":{V(a.hingeLocal)},\"grip_world_rest\":{V(grip)}}},");
            md.AppendLine($"| {a.side} | 夹 10 mm 横杆 | {Mm(gapT)}（开度参数 {a.jawT:F2}，0 = Closed、1 = Half） | 咬合中心（腕骨本地）{a.gripLocal:F4} |");
        }

        public static Vector3 GripWorld(Arm a) => a.wrist.TransformPoint(a.gripLocal);
        public static Vector3 PointWorld(Arm a) => a.wrist.TransformDirection(a.pointLocal);
        public static Vector3 HingeWorld(Arm a) => a.wrist.TransformDirection(a.hingeLocal);

        // ------------------------------------------------------------------ 关节范围（已有动画）

        public static Dictionary<string, (float min, float max, float off)> AuthoredRanges(Arm a)
        {
            var res = new Dictionary<string, (float, float, float)>();
            var names = new[] { "pivot", "shoulder", "elbow", "wrist" };
            foreach (var c in AssetDatabase.LoadAllAssetsAtPath(RobotFbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")))
                for (int s = 0; s <= 60; s++)
                {
                    c.SampleAnimation(robot.gameObject, c.length * s / 60f);
                    var j = a.J;
                    for (int i = 0; i < 4; i++)
                    {
                        float ang = AngleX(a.rest[i], j[i].localRotation, out float off);
                        var k = names[i];
                        res[k] = res.TryGetValue(k, out var r) ? (Mathf.Min(r.Item1, ang), Mathf.Max(r.Item2, ang), Mathf.Max(r.Item3, off)) : (ang, ang, off);
                    }
                }
            RestPose();
            return res;
        }

        // ------------------------------------------------------------------ 双手端盘求解

        public struct Solve { public float[] ang; public float err, axisDot; }

        /// <summary>一只手：让咬合中心落到 target、指定的轴（纵握：夹爪指向；横握：爪轴）与横杆方向平行。坐标下降 + 多起点。</summary>
        public static float[] Lo, Hi;   // 不为空时：关节角限制在这个范围（已有动画用过的角度 ±5°）

        public static Solve SolveArm(Arm a, Vector3 target, Vector3 barDir, bool longitudinal, System.Random rng, float[] seed = null)
        {
            bool Ok(float[] x) { if (Lo == null) return true; for (int i = 0; i < 4; i++) if (x[i] < Lo[i] || x[i] > Hi[i]) return false; return true; }
            float Cost(float[] x)
            {
                Pose(a, x);
                var ax = longitudinal ? PointWorld(a) : HingeWorld(a);
                float d = (GripWorld(a) - target).magnitude;
                return d * d * 1e4f + 0.5f * (1f - Mathf.Abs(Vector3.Dot(ax, barDir)));
            }
            float[] best = null; float bestC = float.MaxValue;
            int starts = seed != null ? 4 : 24;
            for (int st = 0; st < starts; st++)
            {
                var x = st == 0 && seed != null ? (float[])seed.Clone() : st == 0 ? new float[4]
                      : new[] { (float)(rng.NextDouble() * 120 - 60), (float)(rng.NextDouble() * 200 - 100), (float)(rng.NextDouble() * 200 - 100), (float)(rng.NextDouble() * 360 - 180) };
                if (seed != null && st > 0) for (int i = 0; i < 4; i++) x[i] = seed[i] + (float)(rng.NextDouble() * 40 - 20);
                if (Lo != null) for (int i = 0; i < 4; i++) x[i] = Mathf.Clamp(st == 0 && seed == null ? 0f : x[i], Lo[i], Hi[i]);
                if (Lo != null && seed == null && st > 0) for (int i = 0; i < 4; i++) x[i] = Lo[i] + (float)rng.NextDouble() * (Hi[i] - Lo[i]);
                float c = Cost(x), step = 16f;
                while (step > 0.01f)
                {
                    bool imp = false;
                    for (int i = 0; i < 4; i++)
                        foreach (var sgn in new[] { 1f, -1f })
                        {
                            x[i] += sgn * step; float c2 = Ok(x) ? Cost(x) : float.MaxValue;
                            if (c2 < c) { c = c2; imp = true; } else x[i] -= sgn * step;
                        }
                    if (!imp) step *= 0.5f;
                }
                if (c < bestC) { bestC = c; best = (float[])x.Clone(); }
            }
            Pose(a, best);
            var axw = longitudinal ? PointWorld(a) : HingeWorld(a);
            return new Solve { ang = best, err = (GripWorld(a) - target).magnitude, axisDot = Mathf.Abs(Vector3.Dot(axw, barDir)) };
        }

        public static void PlaceTray(Transform body, Vector3 offsetBody, float yawDeg, float rollDeg = 0f, float pitchDeg = 0f)
        {
            // 托盘长轴（本地 X）沿七号左右方向（Body 本地 X），横杆沿七号前后。roll：绕七号前向轴（Body 本地 Z），正 = 左侧（本地 −X）下沉
            var rot = body.rotation * Quaternion.AngleAxis(pitchDeg, Vector3.right) * Quaternion.AngleAxis(rollDeg, Vector3.forward) * Quaternion.AngleAxis(yawDeg, Vector3.up);
            tray.SetPositionAndRotation(body.TransformPoint(offsetBody), rot * trayRestRot);
        }

        public static Transform TrayT => tray;

        /// <summary>托盘本地横杆中心：哪一端靠七号左手（Body 本地 −X）。</summary>
        public static (Vector3 l, Vector3 r, Vector3 dir) Bars(TrayInfo ti)
        {
            var a = tray.TransformPoint(ti.barCenterL[0]); var b = tray.TransformPoint(ti.barCenterL[1]);
            var body = FindUnder(robot, "Body");
            bool aLeft = body.InverseTransformPoint(a).x < body.InverseTransformPoint(b).x;
            return (aLeft ? a : b, aLeft ? b : a, tray.TransformDirection(ti.barDirL[0]).normalized);
        }

        public static List<WMesh> RobotMeshes(Func<Renderer, bool> include) =>
            robot.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && include(r)).Select(World).Where(m => m != null).ToList();

        public static bool IsGripPart(Renderer r) => r.name.Contains("_Jaw");   // 两侧上下爪与齿

        /// <summary>
        /// 定向复核（本单新增）：只重跑“单手握一端提手”一节，同一批求解出来的姿态分别用审计原写法和修正写法测间距，
        /// 输出到 ArtSource/Unit07TrayVariant/Reports/audit_recheck_one_hand.md。不写审计目录，不保存场景。
        /// </summary>
        public static void RecheckOneHand()
        {
            Setup(); RestPose();
            var body = FindUnder(robot, "Body"); var ti = MeasureTray(); var trayR = tray.GetComponent<Renderer>();
            var rng = new System.Random(7);
            // 与审计 Run() 相同的准备：量夹爪咬合几何、取已有动画的关节角范围（±5°）作为求解限制
            var junk = new StringBuilder(); MeasureJaw(L, junk, junk); MeasureJaw(R, junk, junk);
            var ranges = new Dictionary<string, Dictionary<string, (float min, float max, float off)>> { ["L"] = AuthoredRanges(L), ["R"] = AuthoredRanges(R) };
            float U(string j, bool max) => max ? Mathf.Max(ranges["L"][j].max, -ranges["R"][j].min, ranges["R"][j].max, -ranges["L"][j].min)
                                               : Mathf.Min(ranges["L"][j].min, -ranges["R"][j].max, ranges["R"][j].min, -ranges["L"][j].max);
            var jn = new[] { "pivot", "shoulder", "elbow", "wrist" };
            Lo = jn.Select(j => j == "shoulder" || j == "elbow" ? Mathf.Min(ranges["L"][j].min, ranges["R"][j].min) - 5f : U(j, false) - 5f).ToArray();
            Hi = jn.Select(j => j == "shoulder" || j == "elbow" ? Mathf.Max(ranges["L"][j].max, ranges["R"][j].max) + 5f : U(j, true) + 5f).ToArray();
            var md = new StringBuilder("# 审计结论定向复核：单手握一端提手（同一批姿态，旧 / 新“线段穿三角形”）\n\n");
            md.AppendLine("- 姿态求解与审计 0c1c906 相同（复制的 AuditTray 代码、同样的扫描范围和抽样）；只换测距里的线段穿三角形判定。点到三角形部分不变。");
            md.AppendLine("- 0 mm 的含义：穿插（或贴合）。旧写法会把三角形外、按顶点镜像后落在三角形内的交点误判为穿插，也会漏判一部分真穿插。\n");
            md.AppendLine("| 手 | 握法 | 盘心（Body 本地） | 关节角 | 盘↔机身 旧 | 盘↔机身 新 | 最近（新） | 盘↔另一只手 旧 / 新 | 机械臂↔机身 旧 / 新 |\n|---|---|---|---|---|---|---|---|---|");
            int n = 0, zeroOld = 0, zeroNew = 0, changed = 0;
            var rows = new List<(float key, string row)>();
            foreach (var arm in new[] { L, R })
                foreach (bool lon in new[] { true, false })
                    foreach (bool nearBar in new[] { true, false })
                        foreach (float along in new[] { -0.03f, 0f, 0.03f })
                        {
                            float[] seed = null; var hits = new List<(Vector3 c, Solve s)>();
                            for (float x = -0.35f; x <= 0.351f; x += 0.05f)
                                for (float f = 0.05f; f <= 0.351f; f += 0.05f)
                                    for (float y = -0.45f; y <= -0.199f; y += 0.05f)
                                    {
                                        RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT);
                                        PlaceTray(body, new Vector3(x, y, f), 0f);
                                        var (bl, br, dir) = Bars(ti);
                                        var target = ((arm == L) == nearBar ? bl : br) + dir * along;
                                        var sv = SolveArm(arm, target, dir, lon, rng, seed);
                                        if (sv.err < 0.003f && sv.axisDot > 0.966f) { hits.Add((new Vector3(x, y, f), sv)); seed = sv.ang; }
                                    }
                            foreach (var h in hits.Where((v, i) => i % Math.Max(1, hits.Count / 12) == 0))
                            {
                                RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT);
                                PlaceTray(body, h.c, 0f); Pose(arm, h.s.ang);
                                var other = arm == L ? R : L;
                                var trayM = new List<WMesh> { World(trayR) };
                                var bodyM = RobotMeshes(r => !(r.name.Contains($"Arm_{arm.side}_Jaw")) && !r.name.StartsWith($"Arm_{other.side}_"));
                                var otherM = RobotMeshes(r => r.name.StartsWith($"Arm_{other.side}_"));
                                var armM = RobotMeshes(r => r.name.StartsWith($"Arm_{arm.side}_") && !r.name.Contains("Root")); var restM = RobotMeshes(r => !r.name.StartsWith("Arm_"));
                                FixedSegTri = false; var tbO = MinDistance(trayM, bodyM, 0.10f); var toO = MinDistance(trayM, otherM, 0.10f); var abO = MinDistance(armM, restM, 0.10f);
                                FixedSegTri = true; var tbN = MinDistance(trayM, bodyM, 0.10f); var toN = MinDistance(trayM, otherM, 0.10f); var abN = MinDistance(armM, restM, 0.10f);
                                n++; if (tbO.d <= 0f) zeroOld++; if (tbN.d <= 0f) zeroNew++;
                                if (Mathf.Abs(tbO.d - tbN.d) > 1e-5f || Mathf.Abs(toO.d - toN.d) > 1e-5f || Mathf.Abs(abO.d - abN.d) > 1e-5f) changed++;
                                rows.Add((Mathf.Min(tbN.d, Mathf.Min(toN.d, abN.d)), $"| {arm.side} | {(lon ? "纵握" : "横握")} | ({h.c.x:F2}, {h.c.y:F2}, {h.c.z:F2}) | {string.Join("/", h.s.ang.Select(a => a.ToString("F0")))} | {Mm(tbO.d)} | {Mm(tbN.d)} | {tbN.a}↔{tbN.b}（{(nearBar ? "近侧" : "远侧")}，偏 {along * 1000:F0} mm） | {Mm(toO.d)} / {Mm(toN.d)} | {Mm(abO.d)} / {Mm(abN.d)} |"));
                            }
                        }
            foreach (var r in rows.OrderByDescending(r => r.key).Take(16)) md.AppendLine(r.row);
            int leftAll = rows.Count(r => r.row.StartsWith("| L")), leftClearNew = rows.Count(r => r.row.StartsWith("| L") && r.key > 0f);
            md.AppendLine($"\n- 抽样姿态共 {n} 个；“盘↔机身”为 0 的：旧写法 {zeroOld} 个，新写法 {zeroNew} 个；三项里任一项数值有变化的姿态 {changed} 个。");
            md.AppendLine($"- 左手单手：抽样 {leftAll} 个，新写法下三项都 > 0 的 {leftClearNew} 个。表里按新写法三项最小值排序，只列前 16。");
            md.AppendLine("- 无论左手结论是否改变，本单仍按已定的右手方案做（任务卡要求）。");
            Directory.CreateDirectory("ArtSource/Unit07TrayVariant/Reports");
            File.WriteAllText("ArtSource/Unit07TrayVariant/Reports/audit_recheck_one_hand.md", md.ToString(), new UTF8Encoding(false));
            Debug.Log("[TrayVariant] audit recheck 已写出");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static void Run()
        {
            Setup();
            RestPose();
            var md = new StringBuilder();
            var js = new StringBuilder("{");
            var body = FindUnder(robot, "Body");
            var ti = MeasureTray();
            var trayR = tray.GetComponent<Renderer>();
            md.AppendLine("# 端盘实测（Unity，布局 B 场景只读打开，未保存）\n");
            md.AppendLine($"- 托盘：`{PathOf(tray)}`，源 `{AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(tray.gameObject))}`，{ti.tris} 三角面，单一网格（盘体 + 两端提手合并），材质 {string.Join("/", trayR.sharedMaterials.Select(m => m.name))}");
            md.AppendLine($"- 组件：{string.Join("、", tray.GetComponents<Component>().Where(c => !(c is Transform)).Select(c => c.GetType().Name))}");
            md.AppendLine($"- 原点（世界）{tray.position:F4}，朝向 X 轴 {tray.right:F4}；本地包围盒 {ti.boundsMinL:F4} … {ti.boundsMaxL:F4}（含提手）");
            md.AppendLine($"- 盘体（不含提手）本地 {ti.floorMinL:F4} … {ti.floorMaxL:F4} → {Mm(ti.floorMaxL.x - ti.floorMinL.x)} × {Mm(ti.floorMaxL.y - ti.floorMinL.y)} mm、盘壁高 {Mm(ti.floorMaxL.z - ti.floorMinL.z)} mm（导入后托盘本地 Z 向上、X 为长轴、Y 为前后）");
            for (int s = 0; s < 2; s++)
                md.AppendLine($"- 提手横杆 {s}：中心（本地）{ti.barCenterL[s]:F4}，方向（本地）{ti.barDirL[s]}，外包长度 {Mm(ti.barLen[s])} mm，管径约 {Mm(ti.barDia[s])} mm，世界 {tray.TransformPoint(ti.barCenterL[s]):F4}");
            float spacing = Vector3.Distance(ti.barCenterL[0], ti.barCenterL[1]);
            md.AppendLine($"- 两横杆中心距 **{Mm(spacing)} mm**；原点在盘底中心，离两横杆中心连线 {Mm(Vector3.Distance((ti.barCenterL[0] + ti.barCenterL[1]) / 2f, Vector3.zero))} mm\n");
            js.Append($"\"tray\":{{\"path\":{S(PathOf(tray))},\"tris\":{ti.tris},\"origin_world\":{V(tray.position)},\"x_axis_world\":{V(tray.right)},\"bounds_local\":[{V(ti.boundsMinL)},{V(ti.boundsMaxL)}],\"body_local\":[{V(ti.floorMinL)},{V(ti.floorMaxL)}]," +
                      $"\"bar_center_local\":[{V(ti.barCenterL[0])},{V(ti.barCenterL[1])}],\"bar_dir_local\":{V(ti.barDirL[0])},\"bar_len_mm\":{Mm(ti.barLen[0])},\"bar_dia_mm\":{Mm(ti.barDia[0])},\"bar_spacing_mm\":{Mm(spacing)}}},");

            // 夹爪
            md.AppendLine("## 夹爪开度（真实齿网格最近距离）\n\n| 侧 | 姿态 | 上下齿最近距离 mm | 上 / 下爪相对 Closed |\n|---|---|---|---|");
            MeasureJaw(L, md, js); MeasureJaw(R, md, js);
            RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT);
            md.AppendLine();
            var gl = GripWorld(L); var gr = GripWorld(R);
            md.AppendLine($"- 静止姿态（Idle_Hover 第 0 帧）两侧咬合中心相距 **{Mm(Vector3.Distance(gl, gr))} mm**（世界 L {gl:F4}，R {gr:F4}）；七号本体坐标 L {body.InverseTransformPoint(gl):F4}，R {body.InverseTransformPoint(gr):F4}");
            md.AppendLine($"- 夹爪指向（世界）L {PointWorld(L):F3}；爪轴 L {HingeWorld(L):F3}；七号前向（Body +Z）{body.forward:F3}、左右（Body +X = 七号右侧）{body.right:F3}\n");
            js.Append($"\"rest_grip_world\":{{\"L\":{V(gl)},\"R\":{V(gr)},\"distance_mm\":{Mm(Vector3.Distance(gl, gr))}}},");

            // 关节范围
            md.AppendLine("## 已有动画里的关节角范围（绕各骨骼本地 X，相对静止姿态）\n\n| 侧 | 关节 | 最小 | 最大 | 离轴最大 |\n|---|---|---|---|---|");
            var ranges = new Dictionary<string, Dictionary<string, (float min, float max, float off)>> { ["L"] = AuthoredRanges(L), ["R"] = AuthoredRanges(R) };
            foreach (var s in ranges) foreach (var k in s.Value) md.AppendLine($"| {s.Key} | {k.Key} | {k.Value.min:F1}° | {k.Value.max:F1}° | {k.Value.off:F2}° |");
            js.Append("\"authored_ranges\":{" + string.Join(",", ranges.Select(s => $"\"{s.Key}\":{{" + string.Join(",", s.Value.Select(k => $"\"{k.Key}\":[{F(k.Value.min)},{F(k.Value.max)}]")) + "}")) + "},");
            md.AppendLine();
            RestPose();

            // 求解：盘心在七号本体坐标（左右居中），扫前后、上下
            var rng = new System.Random(7);
            var cands = new List<(float f, float y, bool lon, Solve sl, Solve sr)>();
            foreach (bool lon in new[] { true, false })
            {
                float[] seedL = null, seedR = null;   // 相邻格子的解作为起点（第一个格子多起点全局搜）
                for (float f = -0.05f; f <= 0.40f; f += 0.025f)
                    for (float y = -0.50f; y <= -0.10f; y += 0.025f)
                    {
                        RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT);
                        PlaceTray(body, new Vector3(0f, y, f), 0f);
                        var (bl, br, dir) = Bars(ti);
                        var sl = SolveArm(L, bl, dir, lon, rng, seedL);
                        var sr = SolveArm(R, br, dir, lon, rng, seedR);
                        if (sl.err > 0.01f) sl = SolveArm(L, bl, dir, lon, rng);   // 起点不好时再全局搜一次
                        if (sr.err > 0.01f) sr = SolveArm(R, br, dir, lon, rng);
                        seedL = sl.ang; seedR = sr.ang;
                        if (sl.err < 0.003f && sr.err < 0.003f && sl.axisDot > 0.985f && sr.axisDot > 0.985f) cands.Add((f, y, lon, sl, sr));
                    }
            }
            md.AppendLine($"## 双手端盘求解\n\n盘长轴沿七号左右、横杆沿前后（唯一能让左右两只手各握一端的摆法）。扫描盘心：前后 −0.05…0.40 m、上下（相对 Body 原点）−0.50…−0.10 m，步长 25 mm；两种握法。");
            md.AppendLine($"咬合中心到横杆中心误差 < 3 mm、握持轴与横杆夹角 < 10° 的解：**{cands.Count} 个**（纵握 {cands.Count(c => c.lon)}，横握 {cands.Count(c => !c.lon)}）。\n");

            // 对可行解测间距，取最好的
            var robotStatic = new Func<Renderer, bool>(r => !IsGripPart(r));
            var results = new List<(float f, float y, bool lon, Solve sl, Solve sr, float trayBody, string tb, float armBody, string ab, bool inRange, string rangeNote)>();
            foreach (var c in cands.Where((x, i) => i % Math.Max(1, cands.Count / 60) == 0))   // 可行解太多时均匀取约 60 个测间距
            {
                PlaceTray(body, new Vector3(0f, c.y, c.f), 0f);
                RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT); Pose(L, c.sl.ang); Pose(R, c.sr.ang);
                var trayM = new List<WMesh> { World(trayR) };
                var others = RobotMeshes(robotStatic);
                var tb = MinDistance(trayM, others, 0.10f);
                var armM = RobotMeshes(r => r.name.StartsWith("Arm_") && !r.name.Contains("Root"));
                var bodyM = RobotMeshes(r => !r.name.StartsWith("Arm_"));
                var ab = MinDistance(armM, bodyM, 0.10f);
                var notes = new List<string>(); bool inR = true;
                foreach (var (arm, sol) in new[] { (L, c.sl), (R, c.sr) })
                {
                    var names = new[] { "pivot", "shoulder", "elbow", "wrist" };
                    for (int i = 0; i < 4; i++)
                    {
                        var rr = ranges[arm.side][names[i]];
                        if (sol.ang[i] < rr.min - 2f || sol.ang[i] > rr.max + 2f) { inR = false; notes.Add($"{arm.side}.{names[i]} {sol.ang[i]:F0}°（动画 {rr.min:F0}…{rr.max:F0}°）"); }
                    }
                }
                results.Add((c.f, c.y, c.lon, c.sl, c.sr, tb.d, $"{tb.a}↔{tb.b}", ab.d, $"{ab.a}↔{ab.b}", inR, string.Join("，", notes)));
            }
            var ranked = results.OrderByDescending(r => Mathf.Min(r.trayBody, r.armBody)).ToList();
            md.AppendLine("| 握法 | 盘心（Body 本地 前, 上） | 左臂 pivot/肩/肘/腕 | 右臂 | 盘↔机身最小 | 机械臂↔机身最小 | 关节角在已有动画范围内 |\n|---|---|---|---|---|---|---|");
            foreach (var r in ranked.Take(8))
                md.AppendLine($"| {(r.lon ? "纵握" : "横握")} | ({r.f:F3}, {r.y:F3}) | {string.Join(" / ", r.sl.ang.Select(a => a.ToString("F0")))} | {string.Join(" / ", r.sr.ang.Select(a => a.ToString("F0")))} | {Mm(r.trayBody)} mm（{r.tb}） | {Mm(r.armBody)} mm（{r.ab}） | {(r.inRange ? "是" : "否：" + r.rangeNote)} |");
            md.AppendLine();
            js.Append("\"candidates\":[" + string.Join(",", ranked.Take(20).Select(r =>
                $"{{\"grip\":\"{(r.lon ? "longitudinal" : "transverse")}\",\"tray_body_local\":[0,{F(r.y)},{F(r.f)}],\"L\":[{string.Join(",", r.sl.ang.Select(F))}],\"R\":[{string.Join(",", r.sr.ang.Select(F))}],\"err_mm\":[{Mm(r.sl.err)},{Mm(r.sr.err)}],\"tray_body_mm\":{Mm(r.trayBody)},\"tray_body_pair\":{S(r.tb)},\"arm_body_mm\":{Mm(r.armBody)},\"arm_body_pair\":{S(r.ab)},\"in_authored_range\":{(r.inRange ? "true" : "false")},\"range_note\":{S(r.rangeNote)}}}")) + "],");

            // ---- 限制在已有动画用过的角度 ±5°（根环、腕部左右互为镜像，取并集）
            float U(string j, bool max) => max ? Mathf.Max(ranges["L"][j].max, -ranges["R"][j].min, ranges["R"][j].max, -ranges["L"][j].min)
                                              : Mathf.Min(ranges["L"][j].min, -ranges["R"][j].max, ranges["R"][j].min, -ranges["L"][j].max);
            var jn = new[] { "pivot", "shoulder", "elbow", "wrist" };
            var lo = jn.Select(j => j == "shoulder" || j == "elbow" ? Mathf.Min(ranges["L"][j].min, ranges["R"][j].min) - 5f : U(j, false) - 5f).ToArray();
            var hi = jn.Select(j => j == "shoulder" || j == "elbow" ? Mathf.Max(ranges["L"][j].max, ranges["R"][j].max) + 5f : U(j, true) + 5f).ToArray();
            Lo = lo; Hi = hi;
            md.AppendLine($"## 限制在已有动画用过的角度（±5°）\n\n范围：根环 {lo[0]:F0}…{hi[0]:F0}°，肩 {lo[1]:F0}…{hi[1]:F0}°，肘 {lo[2]:F0}…{hi[2]:F0}°，腕 {lo[3]:F0}…{hi[3]:F0}°（根环、腕部左右镜像取并集）。\n");
            js.Append($"\"bounded_range\":{{\"lo\":[{string.Join(",", lo.Select(F))}],\"hi\":[{string.Join(",", hi.Select(F))}]}},");

            // 双手：最接近的解
            (float worst, float f, float y, bool lon, Solve sl, Solve sr) bestTwo = (float.MaxValue, 0, 0, true, default, default);
            foreach (bool lon in new[] { true, false })
            {
                float[] seedL = null, seedR = null;
                for (float f = -0.05f; f <= 0.40f; f += 0.025f)
                    for (float y = -0.50f; y <= -0.10f; y += 0.025f)
                    {
                        RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT);
                        PlaceTray(body, new Vector3(0f, y, f), 0f);
                        var (bl, br, dir) = Bars(ti);
                        var sl = SolveArm(L, bl, dir, lon, rng, seedL); var sr = SolveArm(R, br, dir, lon, rng, seedR);
                        seedL = sl.ang; seedR = sr.ang;
                        float worst = Mathf.Max(sl.err, sr.err) + 0.05f * (2f - sl.axisDot - sr.axisDot);
                        if (worst < bestTwo.worst) bestTwo = (worst, f, y, lon, sl, sr);
                    }
            }
            md.AppendLine($"- **双手握两端提手：在这个范围内做不到。** 最接近的一组：{(bestTwo.lon ? "纵握" : "横握")}，盘心 Body 本地 (0, {bestTwo.y:F3}, {bestTwo.f:F3})，左手离横杆 {Mm(bestTwo.sl.err)} mm、右手 {Mm(bestTwo.sr.err)} mm，握持轴与横杆夹角 {Mathf.Acos(Mathf.Clamp01(bestTwo.sl.axisDot)) * Mathf.Rad2Deg:F0}° / {Mathf.Acos(Mathf.Clamp01(bestTwo.sr.axisDot)) * Mathf.Rad2Deg:F0}°。");
            js.Append($"\"bounded_two_hand_best\":{{\"grip\":\"{(bestTwo.lon ? "longitudinal" : "transverse")}\",\"tray_body_local\":[0,{F(bestTwo.y)},{F(bestTwo.f)}],\"err_mm\":[{Mm(bestTwo.sl.err)},{Mm(bestTwo.sr.err)}],\"L\":[{string.Join(",", bestTwo.sl.ang.Select(F))}],\"R\":[{string.Join(",", bestTwo.sr.ang.Select(F))}]}},");

            // 两只手在范围内能靠多近（指向前方、夹 10 mm 横杆的姿态）：直接求两个咬合中心的最小距离
            {
                RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT);
                float bestD = float.MaxValue; float[] bl = null, br = null;
                for (int k = 0; k < 400; k++)
                {
                    var a = Enumerable.Range(0, 4).Select(i => lo[i] + (float)rng.NextDouble() * (hi[i] - lo[i])).ToArray();
                    var b = new[] { -a[0], a[1], a[2], -a[3] };   // 右臂镜像
                    Pose(L, a); Pose(R, b);
                    float d = (GripWorld(L) - GripWorld(R)).magnitude;
                    if (d < bestD) { bestD = d; bl = a; br = b; }
                }
                md.AppendLine($"- 范围内两侧咬合中心最近能靠到 **{Mm(bestD)} mm**（随机 400 组镜像姿态取最小，左臂 {string.Join("/", bl.Select(x => x.ToString("F0")))}）；提手横杆中心距是 {Mm(spacing)} mm。\n");
                js.Append($"\"bounded_min_grip_span_mm\":{Mm(bestD)},");
            }

            // 单手：一只手纵握 / 横握一端提手，盘向内侧伸出；扫盘心位置
            md.AppendLine("## 单手握一端提手（限制在已有动画角度 ±5°）\n\n| 手 | 握法 | 盘心（Body 本地 左右, 上, 前） | 关节角 | 盘↔机身最小（不含握盘那只手的爪） | 盘↔另一只手 | 机械臂↔机身 |\n|---|---|---|---|---|---|---|");
            var oneHand = new List<(Arm arm, bool lon, Vector3 c, Solve s, float tb, string tbp, float to, float ab)>();
            foreach (var arm in new[] { L, R })
                foreach (bool lon in new[] { true, false })
                    foreach (bool nearBar in new[] { true, false })          // 握靠自己这侧的横杆（盘伸向内侧）/ 另一侧的横杆（盘伸向外侧）
                        foreach (float along in new[] { -0.03f, 0f, 0.03f })  // 沿横杆偏离中心的握点
                {
                    float[] seed = null;
                    var hits = new List<(Vector3 c, Solve s)>();
                    for (float x = -0.35f; x <= 0.351f; x += 0.05f)
                        for (float f = 0.05f; f <= 0.351f; f += 0.05f)
                            for (float y = -0.45f; y <= -0.199f; y += 0.05f)
                            {
                                RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT);
                                PlaceTray(body, new Vector3(x, y, f), 0f);
                                var (bl, br, dir) = Bars(ti);
                                var target = (arm == L) == nearBar ? bl : br;
                                target += dir * along;
                                var s = SolveArm(arm, target, dir, lon, rng, seed);
                                if (s.err < 0.003f && s.axisDot > 0.966f) { hits.Add((new Vector3(x, y, f), s)); seed = s.ang; }
                            }
                    foreach (var h in hits.Where((v, i) => i % Math.Max(1, hits.Count / 12) == 0))
                    {
                        RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT);
                        PlaceTray(body, h.c, 0f); Pose(arm, h.s.ang);
                        var other = arm == L ? R : L;
                        var tb = MinDistance(new List<WMesh> { World(trayR) }, RobotMeshes(r => !(r.name.Contains($"Arm_{arm.side}_Jaw")) && !r.name.StartsWith($"Arm_{other.side}_")), 0.10f);
                        var to = MinDistance(new List<WMesh> { World(trayR) }, RobotMeshes(r => r.name.StartsWith($"Arm_{other.side}_")), 0.10f);
                        var ab = MinDistance(RobotMeshes(r => r.name.StartsWith($"Arm_{arm.side}_") && !r.name.Contains("Root")), RobotMeshes(r => !r.name.StartsWith("Arm_")), 0.10f);
                        oneHand.Add((arm, lon, h.c, h.s, tb.d, $"{tb.a}↔{tb.b}（{(nearBar ? "近侧横杆" : "远侧横杆")}，握点偏 {along * 1000:F0} mm）", to.d, ab.d));
                    }
                }
            var oneRanked = oneHand.OrderByDescending(o => Mathf.Min(o.tb, Mathf.Min(o.to, o.ab))).ToList();
            foreach (var o in oneRanked.Take(10))
                md.AppendLine($"| {o.arm.side} | {(o.lon ? "纵握" : "横握")} | ({o.c.x:F2}, {o.c.y:F2}, {o.c.z:F2}) | {string.Join("/", o.s.ang.Select(a => a.ToString("F0")))} | {Mm(o.tb)} mm（{o.tbp}） | {Mm(o.to)} mm | {Mm(o.ab)} mm |");
            md.AppendLine($"\n单手可行解共 {oneHand.Count} 个（抽样测间距）。\n");
            js.Append("\"one_hand\":[" + string.Join(",", oneRanked.Take(20).Select(o =>
                $"{{\"hand\":\"{o.arm.side}\",\"grip\":\"{(o.lon ? "longitudinal" : "transverse")}\",\"tray_body_local\":{V(o.c)},\"joints\":[{string.Join(",", o.s.ang.Select(F))}],\"tray_body_mm\":{Mm(o.tb)},\"tray_body_pair\":{S(o.tbp)},\"tray_other_arm_mm\":{Mm(o.to)},\"arm_body_mm\":{Mm(o.ab)}}}")) + "],");

            // 需要多宽的提手：假想两根沿前后的横杆，中心距 sp，水平；两手在已有角度范围内纵握 / 横握
            md.AppendLine("## 提手间距需求（假想横杆，不用现有托盘网格）\n\n| 横杆中心距 | 可行解数（误差 < 3 mm、夹角 < 10°） | 最好的一组：握法、横杆高度 / 前后（Body 本地）、左臂关节 | 机械臂↔机身最小 |\n|---|---|---|---|");
            js.Append("\"spacing_study\":[");
            foreach (float sp in new[] { 0.38f, 0.42f, 0.46f, 0.50f, 0.53f, 0.56f })
            {
                int n = 0; (float score, bool lon, float f, float y, Solve sl, Solve sr, float ab) best = (-1f, true, 0, 0, default, default, 0);
                foreach (bool lon in new[] { true, false })
                {
                    float[] seedL = null, seedR = null;
                    for (float f = 0f; f <= 0.351f; f += 0.05f)
                        for (float y = -0.45f; y <= -0.199f; y += 0.025f)
                        {
                            RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT);
                            var tl = body.TransformPoint(new Vector3(-sp / 2f, y, f)); var tr = body.TransformPoint(new Vector3(sp / 2f, y, f));
                            var sl = SolveArm(L, tl, body.forward, lon, rng, seedL); var sr = SolveArm(R, tr, body.forward, lon, rng, seedR);
                            seedL = sl.ang; seedR = sr.ang;
                            if (sl.err < 0.003f && sr.err < 0.003f && sl.axisDot > 0.985f && sr.axisDot > 0.985f)
                            {
                                n++;
                                Pose(L, sl.ang); Pose(R, sr.ang);
                                var ab = MinDistance(RobotMeshes(r => r.name.StartsWith("Arm_") && !r.name.Contains("Root")), RobotMeshes(r => !r.name.StartsWith("Arm_")), 0.05f).d;
                                if (ab > best.score) best = (ab, lon, f, y, sl, sr, ab);
                            }
                        }
                }
                md.AppendLine(n == 0 ? $"| {Mm(sp)} mm | 0 | — | — |"
                    : $"| {Mm(sp)} mm | {n} | {(best.lon ? "纵握" : "横握")}，上 {best.y:F3} / 前 {best.f:F3}，{string.Join("/", best.sl.ang.Select(a => a.ToString("F0")))} | {Mm(best.ab)} mm |");
                js.Append($"{{\"spacing_mm\":{Mm(sp)},\"feasible\":{n}" + (n > 0 ? $",\"grip\":\"{(best.lon ? "longitudinal" : "transverse")}\",\"bar_body_local_y\":{F(best.y)},\"bar_body_local_fwd\":{F(best.f)},\"L\":[{string.Join(",", best.sl.ang.Select(F))}],\"R\":[{string.Join(",", best.sr.ang.Select(F))}],\"arm_body_mm\":{Mm(best.ab)}" : "") + "},");
            }
            js.Append("{}],");
            md.AppendLine();

            if (oneRanked.Count > 0)
            {
                var b = oneRanked[0];
                VariantsOne(md, js, body, ti, b.arm, b.lon, b.c, b.s.ang, rng);
            }
            else md.AppendLine("**单手握提手在已有角度范围内也做不到。**");
            Lo = Hi = null;

            js.Append("\"done\":true}");
            Write("tray_measure.json", js.ToString());
            Write("tray_measure.md", md.ToString());
            Debug.Log("[ArtAudit] tray_measure 已写出");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>单手端盘的参考姿态：正常、整机左倾 3° / 6°、腕部转动让盘面横滚 ±4°；悬停 → 落座过程中手里的盘与维修座的间距。</summary>
        public static void VariantsOne(StringBuilder md, StringBuilder js, Transform body, TrayInfo ti, Arm arm, bool lon, Vector3 c, float[] ang, System.Random rng)
        {
            var trayR = tray.GetComponent<Renderer>();
            var other = arm == L ? R : L;
            var root = robot;
            var anchor = Find("Dock_RobotAnchor");
            Vector3 rootDocked = root.position; Quaternion rootRot = root.rotation;
            var dockM = Find("Unit07ServiceDock").GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.transform != tray).Select(World).Where(m => m != null).ToList();
            // 盘相对握持手腕的位姿（刚性）：程序可直接把托盘挂到腕骨上
            RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT); Pose(arm, ang); PlaceTray(body, c, 0f);
            var relPos = arm.wrist.InverseTransformPoint(tray.position);
            var relRot = Quaternion.Inverse(arm.wrist.rotation) * tray.rotation;
            md.AppendLine($"## 单手端盘参考姿态（推荐：{arm.side} 手{(lon ? "纵握" : "横握")}）\n");
            md.AppendLine($"- 托盘挂到 `{PathOf(arm.wrist)}` 下的本地位姿：位置 {relPos:F4}，旋转（四元数）{relRot.x:F4}, {relRot.y:F4}, {relRot.z:F4}, {relRot.w:F4}；夹爪开度参数 {arm.jawT:F2}（Closed→Half 之间，齿间距 10 mm）\n");
            js.Append($"\"recommended\":{{\"hand\":\"{arm.side}\",\"grip\":\"{(lon ? "longitudinal" : "transverse")}\",\"wrist\":{S(PathOf(arm.wrist))},\"tray_local_pos\":{V(relPos)},\"tray_local_rot\":{Q(relRot)},\"jawT\":{F(arm.jawT)},\"poses\":{{");
            md.AppendLine("| 姿态 | 怎么摆 | 盘面与水平夹角 | 盘↔机身最小 | 盘↔另一只手 | 关节角 |\n|---|---|---|---|---|---|");
            void Row(string id, string label, string how, float[] a)
            {
                RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT); Pose(arm, a);
                tray.SetPositionAndRotation(arm.wrist.TransformPoint(relPos), arm.wrist.rotation * relRot);
                float tilt = Vector3.Angle(tray.forward, Vector3.up);
                var tb = MinDistance(new List<WMesh> { World(trayR) }, RobotMeshes(r => !r.name.Contains($"Arm_{arm.side}_Jaw") && !r.name.StartsWith($"Arm_{other.side}_")), 0.10f);
                var to = MinDistance(new List<WMesh> { World(trayR) }, RobotMeshes(r => r.name.StartsWith($"Arm_{other.side}_")), 0.10f);
                md.AppendLine($"| {label} | {how} | {tilt:F1}° | {Mm(tb.d)} mm（{tb.a}↔{tb.b}） | {Mm(to.d)} mm | {string.Join("/", a.Select(x => x.ToString("F1")))} |");
                js.Append($"\"{id}\":{{\"root_pos\":{V(root.position)},\"root_rot\":{Q(root.rotation)},\"joints\":[{string.Join(",", a.Select(F))}],\"tray_pos\":{V(tray.position)},\"tray_rot\":{Q(tray.rotation)},\"tray_tilt_deg\":{F(tilt)},\"tray_body_mm\":{Mm(tb.d)},\"tray_other_arm_mm\":{Mm(to.d)}}},");
            }
            root.SetPositionAndRotation(rootDocked, rootRot);
            Row("normal", "正常", "盘面水平", ang);
            foreach (float deg in new[] { 3f, 6f })
            {
                root.SetPositionAndRotation(rootDocked, rootRot);
                root.RotateAround(body.position, body.forward, deg);
                Row($"robot_roll_{deg:F0}", $"左侧下沉 {deg:F0}°", $"预制体根绕 Body 原点、七号前向轴转 {deg:F0}°，关节不变", ang);
            }
            root.SetPositionAndRotation(rootDocked, rootRot);
            foreach (float deg in new[] { -4f, 4f })
            {
                var a = (float[])ang.Clone(); a[3] += deg;
                Row($"wrist_roll_{(deg < 0 ? "m" : "p")}{Mathf.Abs(deg):F0}", $"盘面倾斜 {deg:+0;-0}°", $"只转腕骨 {deg:+0;-0}°（盘随手腕横滚）", a);
            }
            js.Append("\"x\":0}},");
            md.AppendLine();

            md.AppendLine("## 维修座上方：悬停 → 落座，手里的盘与维修座（推荐姿态）\n\n| 七号根离锚点高度 | 盘↔维修座最小 | 最近的维修座对象 |\n|---|---|---|");
            js.Append("\"dock_descent\":[");
            float firstContact = float.NaN;
            for (float h = 0.30f; h >= -0.0001f; h -= 0.02f)
            {
                root.SetPositionAndRotation(anchor.position + Vector3.up * h, rootRot);
                RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT); Pose(arm, ang);
                tray.SetPositionAndRotation(arm.wrist.TransformPoint(relPos), arm.wrist.rotation * relRot);
                var d = MinDistance(new List<WMesh> { World(trayR) }, dockM, 0.30f);
                if (float.IsNaN(firstContact) && d.d <= 0.0001f) firstContact = h;
                md.AppendLine($"| {Mm(h)} mm | {Mm(d.d)} mm | {d.b} |");
                js.Append($"{{\"h_mm\":{Mm(h)},\"tray_dock_mm\":{Mm(d.d)},\"nearest\":{S(d.b)}}},");
            }
            js.Append("{}],");
            md.AppendLine($"\n- 维修座初始悬停高度为锚点上方 120 mm（`Unit07DockController.hoverHeight`）。盘第一次碰到维修座的高度：{(float.IsNaN(firstContact) ? "整个落座过程都没碰到" : Mm(firstContact) + " mm")}\n");
            js.Append($"\"first_contact_h_mm\":{(float.IsNaN(firstContact) ? "null" : Mm(firstContact))},");
            root.SetPositionAndRotation(rootDocked, rootRot);
            RestPose();
        }

        /// <summary>参考姿态：正常 / 整机左倾 / 只降左手（盘面横滚）/ 盘面前倾；以及悬停 → 落座过程中手里的盘与维修座的间距。</summary>
        static void Variants(StringBuilder md, StringBuilder js, Transform body, TrayInfo ti, float f, float y, bool lon, float[] aL, float[] aR, System.Random rng)
        {
            var trayR = tray.GetComponent<Renderer>();
            var dockRoot = Find("Unit07ServiceDock");
            var dockM = dockRoot.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.transform != tray).Select(World).Where(m => m != null).ToList();
            var root = robot;   // 预制体根（动画片段不写它；维修座落座 / 离座写它的位置）
            var anchor = Find("Dock_RobotAnchor");
            Vector3 rootDocked = root.position; Quaternion rootRot = root.rotation;
            js.Append("\"poses\":{");

            md.AppendLine("## 参考姿态（以最优解为“正常”）\n\n| 姿态 | 怎么摆 | 盘面相对水平 | 左 / 右咬合误差 | 盘↔机身最小 | 机械臂↔机身 | 关节角（左 / 右） |\n|---|---|---|---|---|---|---|");
            void Row(string id, string label, string how, float[] l, float[] r)
            {
                RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT); Pose(L, l); Pose(R, r);
                var (bl, br, _) = Bars(ti);
                float el = (GripWorld(L) - bl).magnitude, er = (GripWorld(R) - br).magnitude;
                float tilt = Vector3.Angle(tray.forward, Vector3.up);   // 托盘本地 Z = 盘面法线
                var tb = MinDistance(new List<WMesh> { World(trayR) }, RobotMeshes(x => !IsGripPart(x)), 0.10f);
                var ab = MinDistance(RobotMeshes(x => x.name.StartsWith("Arm_") && !x.name.Contains("Root")), RobotMeshes(x => !x.name.StartsWith("Arm_")), 0.10f);
                md.AppendLine($"| {label} | {how} | {tilt:F1}° | {Mm(el)} / {Mm(er)} mm | {Mm(tb.d)} mm | {Mm(ab.d)} mm | {string.Join("/", l.Select(a => a.ToString("F0")))} ; {string.Join("/", r.Select(a => a.ToString("F0")))} |");
                js.Append($"\"{id}\":{{\"root_pos\":{V(root.position)},\"root_rot\":{Q(root.rotation)},\"tray_pos\":{V(tray.position)},\"tray_rot\":{Q(tray.rotation)},\"tray_tilt_deg\":{F(tilt)},\"L\":[{string.Join(",", l.Select(F))}],\"R\":[{string.Join(",", r.Select(F))}],\"grip_err_mm\":[{Mm(el)},{Mm(er)}],\"tray_body_mm\":{Mm(tb.d)},\"arm_body_mm\":{Mm(ab.d)},\"jawT\":[{F(L.jawT)},{F(R.jawT)}]}},");
            }

            // 正常
            root.SetPositionAndRotation(rootDocked, rootRot);
            PlaceTray(body, new Vector3(0f, y, f), 0f);
            Row("normal", "正常端盘", $"{(lon ? "纵握" : "横握")}，盘心 Body 本地 (0, {y:F3}, {f:F3})", aL, aR);

            // 整机左倾：绕 Body 原点的前向轴转（七号左侧 = Body −X 下沉）；手和盘随整机一起转，关节不变
            foreach (float deg in new[] { 3f, 6f })
            {
                root.SetPositionAndRotation(rootDocked, rootRot);
                var pivot = body.position;
                root.RotateAround(pivot, body.forward, deg);   // 绕 +Z 正转：+X 向上、−X（左）向下
                PlaceTray(body, new Vector3(0f, y, f), 0f);
                Row($"robot_roll_{deg:F0}", $"整机左倾 {deg:F0}°", $"预制体根绕 Body 原点、七号前向轴转 {deg:F0}°（左侧下沉），关节不变", aL, aR);
            }
            root.SetPositionAndRotation(rootDocked, rootRot);

            // 只降左手：盘面绕前向轴横滚，右手保持，左手重解
            foreach (float deg in new[] { 3f, 6f })
            {
                RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT); Pose(R, aR);
                var gR = GripWorld(R);
                // 盘绕右侧横杆转：先摆正常，再绕右横杆中心转
                PlaceTray(body, new Vector3(0f, y, f), 0f);
                var (bl0, br0, _) = Bars(ti);
                tray.RotateAround(br0, body.forward, deg);
                var (bl, br, dir) = Bars(ti);
                var sl = SolveArm(L, bl, dir, lon, rng, aL);
                Row($"tray_roll_{deg:F0}", $"盘面左低 {deg:F0}°", $"右手不动，盘绕右横杆转 {deg:F0}°，左手跟随（左横杆下降 {Mm(bl0.y - bl.y)} mm）", sl.ang, aR);
            }

            // 盘面前倾：绕两横杆中点的左右轴转，两手腕跟随
            foreach (float deg in new[] { 3f })
            {
                RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT);
                PlaceTray(body, new Vector3(0f, y, f), 0f);
                var (bl0, br0, _) = Bars(ti);
                tray.RotateAround((bl0 + br0) / 2f, body.right, deg);
                var (bl, br, dir) = Bars(ti);
                var sl = SolveArm(L, bl, dir, lon, rng, aL); var sr = SolveArm(R, br, dir, lon, rng, aR);
                Row($"tray_pitch_{deg:F0}", $"盘面前倾 {deg:F0}°", "两手一起跟随（腕 / 肘微调）", sl.ang, sr.ang);
            }
            md.AppendLine();

            // 悬停 → 落座：手持正常姿态的盘，与维修座的最小间距
            md.AppendLine("## 维修座上方：悬停 → 落座，手里的盘与维修座\n\n| 七号根离锚点高度 | 盘↔维修座最小 | 最近的维修座对象 |\n|---|---|---|");
            js.Append("\"dock_descent\":[");
            float firstContact = float.NaN;
            for (float h = 0.30f; h >= -0.0001f; h -= 0.02f)
            {
                root.SetPositionAndRotation(anchor.position + Vector3.up * h, rootRot);
                RestPose(); Jaw(L, L.jawT); Jaw(R, R.jawT); Pose(L, aL); Pose(R, aR);
                PlaceTray(body, new Vector3(0f, y, f), 0f);
                var d = MinDistance(new List<WMesh> { World(trayR) }, dockM, 0.30f);
                if (float.IsNaN(firstContact) && d.d <= 0.0001f) firstContact = h;
                md.AppendLine($"| {Mm(h)} mm | {Mm(d.d)} mm | {d.b} |");
                js.Append($"{{\"h_mm\":{Mm(h)},\"tray_dock_mm\":{Mm(d.d)},\"nearest\":{S(d.b)}}},");
            }
            js.Append("{}],");
            md.AppendLine($"\n- 维修座初始悬停高度为锚点上方 120 mm（`Unit07DockController.hoverHeight`）。盘第一次碰到维修座的高度：{(float.IsNaN(firstContact) ? "整个落座过程都没碰到" : Mm(firstContact) + " mm")}\n");
            js.Append($"\"first_contact_h_mm\":{(float.IsNaN(firstContact) ? "null" : Mm(firstContact))},");
            root.SetPositionAndRotation(rootDocked, rootRot);
            js.Append("\"x\":0},");
        }
    }
}
