using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static BorderRepair.TwoNight.MeshClearance;

namespace BorderRepair.Art.Unit07TrayVariant.EditorTools
{
    /// <summary>
    /// 只读搜索（不保存场景）：提手抬高 Δ、支腿位置 ±legY、握点在横杆上的位置 g 三个参数。
    /// 手臂保持审计端盘姿态（关节不动），托盘相对手重新求：横杆上 (x=151.6, y=g, z=47+Δ) 落在右手咬合中心。
    /// 托盘盘体用原网格的盘体部分（|x| ≤ 133 mm，真实三角面）；新提手按管子解析计算（圆管半径 5 mm，轴线折线）。
    /// 检查：右手全部零件 ↔ 盘体；右手非爪齿零件 ↔ 提手（横杆 + 支腿）；爪齿 ↔ 支腿；七号其余部分 ↔ 整个托盘。
    /// 结论只用于选参数；选中的方案再用 Blender 实际网格复核。
    /// </summary>
    public static class TrayVariantSearch
    {
        const string Scene = "Assets/BorderRepair/Scenes/Slice/Unit07_Night.unity";
        const string OutDir = "ArtSource/Unit07TrayVariant/Reports";
        public const float BarX = 0.1516f, BarZ0 = 0.047f, TubeR = 0.005f, WallOuterX = 0.130f, LegFootZ = 0.028f;

        /// <summary>新提手轴线（托盘本地，单侧 +X）：支腿脚 → 支腿顶 → 横杆 → 另一支腿顶 → 脚。</summary>
        public static Vector3[] HandleAxis(float delta, float legY)
        {
            float zb = BarZ0 + delta;
            return new[] { new Vector3(WallOuterX, -legY, LegFootZ), new Vector3(BarX, -legY, LegFootZ + 0.008f), new Vector3(BarX, -legY, zb),
                           new Vector3(BarX, legY, zb), new Vector3(BarX, legY, LegFootZ + 0.008f), new Vector3(WallOuterX, legY, LegFootZ) };
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).magnitude;
        }

        /// <summary>点到提手管表面的距离（负数 = 在管子里面）；bar = 只算横杆段，legs = 只算支腿段。</summary>
        public static float TubeDist(Vector3 p, Vector3[] axis, bool bar, bool legs)
        {
            float d = float.MaxValue;
            for (int i = 0; i < axis.Length - 1; i++)
            {
                bool isBar = i == 2;
                if (isBar && !bar || !isBar && !legs) continue;
                d = Mathf.Min(d, SegDist(p, axis[i], axis[i + 1]));
            }
            return d - TubeR;
        }

        public static void Run()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var inc = Object.FindFirstObjectByType<BorderRepair.TwoNight.TrayIncident>();
            var so = new SerializedObject(inc);
            var hold = so.FindProperty("holdAnchor").objectReferenceValue as Transform;
            var ov = inc.Overlay;
            ov.RightWeight = 1f; ov.LeftWeight = 1f; ov.JawOpen = 0f; ov.UpperJawExtra = 0f;
            for (int i = 0; i < 6; i++) ov.RightBones[i].localRotation = ov.RightTarget(i);
            for (int i = 0; i < 4; i++) ov.LeftBones[i].localRotation = ov.LeftTarget(i);
            var toTray = hold.worldToLocalMatrix;
            var robot = inc.RobotRoot;
            var trayT = GameObject.Find("Dock_PartsTray").transform;

            // 盘体（原网格，托盘本地）
            var src = trayT.GetComponent<MeshFilter>().sharedMesh; var lv = src.vertices; var tri = src.triangles;
            var keep = new List<int>();
            for (int i = 0; i < tri.Length; i += 3)
                if (Mathf.Abs((lv[tri[i]].x + lv[tri[i + 1]].x + lv[tri[i + 2]].x) / 3f) <= 0.133f) keep.AddRange(new[] { tri[i], tri[i + 1], tri[i + 2] });
            var body = new WMesh { name = "盘体", v = lv, tri = keep.ToArray(), b = new Bounds(Vector3.zero, Vector3.one) };
            body.b = new Bounds(lv[keep[0]], Vector3.zero); foreach (var k in keep) body.b.Encapsulate(lv[k]);

            // 右手零件、七号其余零件：旧挂点下的托盘本地坐标
            var hand = ov.RightBones[3].GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).Select(r => (r, m: Moved(World(r), toTray))).ToList();
            var handSet = new HashSet<Renderer>(hand.Select(h => h.r));
            var rest = robot.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy && !handSet.Contains(r) && r.transform != trayT)
                            .Select(r => Moved(World(r), toTray)).Where(m => m != null).ToList();
            bool Teeth(string n) => n.EndsWith("_Teeth");
            var gripC = toTray.MultiplyPoint3x4(ov.RightBones[3].TransformPoint(new Vector3(-0.0824f, -0.0007f, 0.0284f)));

            var sb = new StringBuilder("# 托盘提手变体参数搜索（只读，解析管子 + 原盘体网格）\n\n");
            sb.AppendLine($"- 手臂保持审计端盘姿态；右手咬合中心在旧托盘本地 {gripC * 1000:F1} mm。新托盘相对手：横杆上 (151.6, g, 47+Δ) mm 落在咬合中心。");
            sb.AppendLine("- 允许接触：爪齿 ↔ 横杆。其余：右手全部零件 ↔ 盘体，右手非爪齿零件 ↔ 横杆（负数 = 穿进管子），右手全部零件 ↔ 支腿，七号其余 ↔ 盘体。单位 mm。\n");
            sb.AppendLine("| Δ | legY | g | 右手↔盘体 | 非爪齿↔横杆 | 右手↔支腿 | 七号其余↔盘体 | 七号其余↔提手 | 最紧的零件 |\n|---|---|---|---|---|---|---|---|---|");
            var results = new List<(float score, string row, float delta, float legY, float g)>();
            foreach (float delta in new[] { 0.040f, 0.042f, 0.045f })
                foreach (float legY in new[] { 0.045f, 0.060f, 0.075f, 0.084f })
                    foreach (float g in new[] { 0.030f, 0.020f, 0.010f, 0.0f, -0.010f, -0.020f })
                    {
                        if (Mathf.Abs(g) + 0.012f > legY) continue;
                        var shift = new Vector3(0f, gripC.y - g, gripC.z - (BarZ0 + delta));   // 新托盘本地 = 旧托盘本地 − shift（托盘相对手平移）
                        var toNew = Matrix4x4.Translate(-shift);
                        var axis = HandleAxis(delta, legY);
                        float dBody = 0.05f, dBar = 0.05f, dLeg = 0.05f; string worst = "-"; float worstD = 1f;
                        foreach (var (r, m) in hand)
                        {
                            var mm = Moved(m, toNew);
                            float db = Distance(mm, body, 0.05f);
                            if (db < dBody) dBody = db;
                            float bar = float.MaxValue, leg = float.MaxValue;
                            foreach (var p in mm.v) { bar = Mathf.Min(bar, TubeDist(p, axis, true, false)); leg = Mathf.Min(leg, TubeDist(p, axis, false, true)); }
                            if (!Teeth(r.name) && bar < dBar) dBar = bar;
                            if (leg < dLeg) dLeg = leg;
                            float local = Mathf.Min(db, Mathf.Min(leg, Teeth(r.name) ? 1f : bar));
                            if (local < worstD) { worstD = local; worst = r.name; }
                        }
                        float dRest = Min(rest.Select(x => Moved(x, toNew)).ToList(), new List<WMesh> { body }, 0.05f).d;
                        float dRestH = rest.Select(x => Moved(x, toNew)).Min(x => x.v.Min(p => TubeDist(p, axis, true, true)));
                        float score = Mathf.Min(Mathf.Min(dBody, dLeg), Mathf.Min(dRest, Mathf.Min(dRestH, dBar + 0.003f)));
                        string row = $"| {delta * 1000:F0} | {legY * 1000:F0} | {g * 1000:F0} | {dBody * 1000:F1} | {dBar * 1000:F1} | {dLeg * 1000:F1} | {dRest * 1000:F1} | {Mathf.Min(dRestH, 0.05f) * 1000:F1} | {worst} |";
                        results.Add((score, row, delta, legY, g));
                    }
            foreach (var r in results.OrderByDescending(x => x.score).Take(25)) sb.AppendLine(r.row);
            sb.AppendLine("\n（按最紧一项从大到小排，只列前 25 个。横杆一栏是非爪齿零件到横杆表面，负数表示穿进；爪齿与横杆接触是允许的。）");
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "param_search.md"), sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[TrayVariant] search 已写出");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
