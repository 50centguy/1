using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.TwoNight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static BorderRepair.TwoNight.MeshClearance;

namespace BorderRepair.Art.Unit07TrayVariant.EditorTools
{
    /// <summary>
    /// 只读诊断（不保存）：
    /// 1) 坐标系：原托盘盘体与变体盘体在各自本地坐标里的顶点中心与范围（隔板不对称，能看出有没有镜像）；
    /// 2) 握杆 ↔ 两爪：把握杆（只握杆直段）沿“爪 → 握杆轴”方向每次挪 0.25 mm，第一次分开时的距离 ≈ 穿进深度（估计值，受测距局限影响）；
    /// 3) 夹爪开度 jawT 扫描：每档的爪齿 / 爪身 ↔ 握杆距离，找“爪齿刚接触、爪身不穿”的开度。
    /// </summary>
    public static class TrayVariantJawDepth
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(TrayVariantBuild.VerifyScene, OpenSceneMode.Single);
            var inc = Object.FindFirstObjectByType<TrayIncident>();
            var so = new SerializedObject(inc);
            var hold = so.FindProperty("holdAnchor").objectReferenceValue as Transform;
            var ov = inc.Overlay;
            var tray = inc.Tray.transform;
            var orig = Resources.FindObjectsOfTypeAll<Transform>().First(t => t.name == "Dock_PartsTray" && t.gameObject.scene.IsValid());
            var sb = new StringBuilder("# 变体握持细查（只读诊断）\n\n");

            // 1) 坐标系
            string Stats(Transform frame, MeshFilter mf, bool bodyOnly)
            {
                var m = frame.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var v = mf.sharedMesh.vertices.Select(p => m.MultiplyPoint3x4(p)).Where(p => !bodyOnly || Mathf.Abs(p.x) <= 0.133f).ToList();
                var c = v.Aggregate(Vector3.zero, (a, p) => a + p) / v.Count;
                return $"顶点 {v.Count}，中心 {c * 1000:F2} mm，范围 X {v.Min(p => p.x) * 1000:F1}…{v.Max(p => p.x) * 1000:F1}，Y {v.Min(p => p.y) * 1000:F1}…{v.Max(p => p.y) * 1000:F1}，Z {v.Min(p => p.z) * 1000:F1}…{v.Max(p => p.z) * 1000:F1} mm";
            }
            sb.AppendLine("## 1. 坐标系\n");
            sb.AppendLine($"- 原托盘盘体（|x| ≤ 133 mm，原托盘本地）：{Stats(orig, orig.GetComponent<MeshFilter>(), true)}");
            sb.AppendLine($"- 变体盘体（变体根本地）：{Stats(tray, tray.Find("Tray_Body").GetComponent<MeshFilter>(), false)}");
            sb.AppendLine("- 隔板偏在盘的一侧，所以顶点中心的 X 能看出两者是否镜像。\n");

            // 2) / 3) 夹住时
            ov.RightWeight = 1f; ov.LeftWeight = 1f; ov.UpperJawExtra = 0f;
            var wrist = ov.RightBones[3];
            Matrix4x4 carry() => Matrix4x4.TRS(hold.position, hold.rotation, Vector3.one) * Matrix4x4.TRS(tray.position, tray.rotation, Vector3.one).inverse;
            var oso = new SerializedObject(ov);
            var closed = new Quaternion[2]; var half = oso.FindProperty("rightOpenJaw");
            var carryArr = oso.FindProperty("rightCarry");
            // 由 Closed / Half 反推：carry 爪 = Slerp(closed, half, 0.32071)
            var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/RobotV4/Model/robot-final.fbx").OfType<AnimationClip>();
            clip.First(c => c.name == "Pose_Gripper_Closed").SampleAnimation(inc.RobotRoot.gameObject, 0f);
            closed[0] = ov.RightBones[4].localRotation; closed[1] = ov.RightBones[5].localRotation;
            var halfQ = new[] { half.GetArrayElementAtIndex(0).quaternionValue, half.GetArrayElementAtIndex(1).quaternionValue };
            for (int i = 0; i < 4; i++) ov.RightBones[i].localRotation = ov.RightTarget(i);
            for (int i = 0; i < 4; i++) ov.LeftBones[i].localRotation = ov.LeftTarget(i);

            var bars = TrayVariantBuild.Split(tray, carry()).bars;
            var barT = tray.Find(tray.Find("GripBar_PX").localPosition.x > 0 ? "GripBar_PX" : "GripBar_NX");
            var barW = carry().MultiplyPoint3x4(barT.position);
            var axisW = carry().MultiplyVector(tray.TransformDirection(Vector3.up)).normalized;
            var gripBar = bars.OrderBy(b => (b.b.center - barW).sqrMagnitude).First();

            sb.AppendLine("## 2. 夹爪开度扫描（爪齿 / 爪身 ↔ 被握的握杆直段，mm；0 = 接触或穿插）\n");
            sb.AppendLine("| jawT | 下爪齿 | 上爪齿 | 下爪身 | 上爪身 | 下爪身估计穿进 | 上爪身估计穿进 |\n|---|---|---|---|---|---|---|");
            foreach (float t in new[] { 0.32071f, 0.34f, 0.36f, 0.38f, 0.40f, 0.43f, 0.46f, 0.50f })
            {
                ov.RightBones[4].localRotation = Quaternion.Slerp(closed[0], halfQ[0], t);
                ov.RightBones[5].localRotation = Quaternion.Slerp(closed[1], halfQ[1], t);
                WMesh Part(string n) => World(wrist.GetComponentsInChildren<Renderer>(true).First(r => r.name == n));
                float D(string n) => Distance(Part(n), gripBar, 0.05f) * 1000f;
                string Depth(string n)
                {
                    var m = Part(n);
                    if (Distance(m, gripBar, 0.05f) > 0f) return "—";
                    var c = m.v.Aggregate(Vector3.zero, (a, p) => a + p) / m.v.Length;
                    var dir = (barW - c); dir -= axisW * Vector3.Dot(dir, axisW); dir.Normalize();
                    for (float o = 0.00025f; o <= 0.006f; o += 0.00025f)
                        if (Distance(m, Moved(gripBar, Matrix4x4.Translate(dir * o)), 0.05f) > 0f) return $"≈ {o * 1000:F2}";
                    return "> 6";
                }
                sb.AppendLine($"| {t:F3} | {D("Arm_R_JawLower_Teeth"):F2} | {D("Arm_R_JawUpper_Teeth"):F2} | {D("Arm_R_JawLower"):F2} | {D("Arm_R_JawUpper"):F2} | {Depth("Arm_R_JawLower")} | {Depth("Arm_R_JawUpper")} |");
            }
            // 3) 握杆在 +Y 方向能伸到哪里：程序生成的 12 段圆管（半径 5 mm）代替握杆，y 从 −84 mm 到 yEnd；量两爪爪身 / 爪架 / 销轴
            ov.RightBones[4].localRotation = Quaternion.Slerp(closed[0], halfQ[0], 0.32071f);
            ov.RightBones[5].localRotation = Quaternion.Slerp(closed[1], halfQ[1], 0.32071f);
            var toW = carry() * tray.localToWorldMatrix;
            var gp = tray.Find("GripPoint_R").localPosition;
            WMesh Cyl(Vector3 a0, Vector3 a1)
            {
                var ax = (a1 - a0).normalized; var n1 = Vector3.Cross(ax, Mathf.Abs(ax.x) < 0.9f ? Vector3.right : Vector3.up).normalized; var n2 = Vector3.Cross(ax, n1);
                var v = new List<Vector3>(); var tri = new List<int>(); int seg = 12;
                for (int k = 0; k < 2; k++) for (int i = 0; i < seg; i++) { float a = 2 * Mathf.PI * i / seg; v.Add(toW.MultiplyPoint3x4((k == 0 ? a0 : a1) + (n1 * Mathf.Cos(a) + n2 * Mathf.Sin(a)) * 0.005f)); }
                for (int i = 0; i < seg; i++) { int j = (i + 1) % seg; tri.AddRange(new[] { i, j, seg + j, i, seg + j, seg + i }); }
                for (int i = 1; i < seg - 1; i++) { tri.AddRange(new[] { 0, i + 1, i }); tri.AddRange(new[] { seg, seg + i, seg + i + 1 }); }
                var b = new Bounds(v[0], Vector3.zero); foreach (var p in v) b.Encapsulate(p);
                return new WMesh { name = "cyl", v = v.ToArray(), tri = tri.ToArray(), b = b };
            }
            var names = new[] { "Arm_R_JawLower", "Arm_R_JawUpper", "Arm_R_GripperBracket", "Arm_R_HingePin_Lower", "Arm_R_HingePin_Upper", "Arm_R_Wrist", "Arm_R_JawLower_Teeth", "Arm_R_JawUpper_Teeth" };
            var parts = names.Select(n => World(wrist.GetComponentsInChildren<Renderer>(true).First(r => r.name == n))).ToList();
            sb.AppendLine("\n## 3. 握杆 +Y 端能伸到哪里（程序圆管代替握杆，y 从 −84 mm 到 yEnd；夹住 0.321；mm）\n");
            sb.AppendLine("| yEnd | " + string.Join(" | ", names.Select(n => n.Replace("Arm_R_", ""))) + " |\n|---|" + string.Concat(names.Select(_ => "---|")));
            foreach (float ye in new[] { 0.030f, 0.034f, 0.038f, 0.042f, 0.046f, 0.050f, 0.055f, 0.060f, 0.070f, 0.084f })
            {
                var tube = Cyl(new Vector3(gp.x, -0.084f, gp.z), new Vector3(gp.x, ye, gp.z));
                sb.AppendLine($"| {ye * 1000:F0} | " + string.Join(" | ", parts.Select(p => (Distance(p, tube, 0.05f) * 1000f).ToString("F2"))) + " |");
            }
            sb.AppendLine("\n支腿（竖直圆管，x 同握杆、z 25→92 mm，半径 5 mm）放在 y 处时 ↔ 右手全部零件（mm）：\n\n| y | 最近 | 零件 |\n|---|---|---|");
            foreach (float y in new[] { -0.084f, -0.060f, -0.040f, -0.030f, -0.024f, -0.020f, -0.016f })
            {
                var leg = Cyl(new Vector3(gp.x, y, 0.025f), new Vector3(gp.x, y, gp.z));
                var best = parts.Select((p, i) => (d: Distance(p, leg, 0.05f), n: names[i])).OrderBy(x => x.d).First();
                sb.AppendLine($"| {y * 1000:F0} | {best.d * 1000:F2} | {best.n} |");
            }
            sb.AppendLine("\n“估计穿进”：把握杆沿“爪身中心 → 握杆轴”的垂直方向挪开，第一次测得 > 0 的距离；测距不算边到边，结果是估计，不是精确深度。");
            File.WriteAllText(Path.Combine(TrayVariantBuild.Reports, "jaw_detail_probe.md"), sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[TrayVariant] jaw detail 已写出");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
