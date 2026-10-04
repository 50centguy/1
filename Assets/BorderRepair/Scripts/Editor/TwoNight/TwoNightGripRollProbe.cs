using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static BorderRepair.TwoNight.MeshClearance;

namespace BorderRepair.TwoNight.EditorTools
{
    /// <summary>
    /// 只读诊断：局部调握点——手绕横杆换个角度握（腕部滚转 φ，挂点绕横杆轴反向转 φ，托盘保持水平、横杆仍在咬合中心）。
    /// 对每个 φ：七号根放到对应的夹取位（托盘在原位），量右手 ↔ 托盘架、右手（不含两爪）↔ 托盘、托盘倾角。
    /// </summary>
    public static class TwoNightGripRollProbe
    {
        // 美术审计 0c1c906：右手咬合中心（腕骨本地），夹 10 mm 横杆
        public static readonly Vector3 GripCenterR = new Vector3(-0.0824f, -0.0007f, 0.0284f);

        /// <summary>手绕横杆转 φ 之后的托盘挂点（腕骨本地）：Translate(gc)·Rot(d, −φ)·Translate(−gc)·原挂点，d = 横杆方向（托盘本地 Y）。</summary>
        public static (Vector3 pos, Quaternion rot) RolledHold(Vector3 holdPos, Quaternion holdRot, float phi)
        {
            var d = (holdRot * Vector3.up).normalized;
            var q = Quaternion.AngleAxis(-phi, d);
            return (GripCenterR + q * (holdPos - GripCenterR), q * holdRot);
        }

        public static void Run()
        {
            EditorSceneManager.OpenScene(TwoNightBuilder.RobotPath, OpenSceneMode.Single);
            var inc = Object.FindFirstObjectByType<TrayIncident>();
            var so = new SerializedObject(inc);
            var H = so.FindProperty("hover").vector3Value;
            var robot = inc.RobotRoot; var ov = inc.Overlay;
            var hold = so.FindProperty("holdAnchor").objectReferenceValue as Transform;
            var holdPos0 = hold.localPosition; var holdRot0 = hold.localRotation;
            var trayT = GameObject.Find("Dock_PartsTray").transform;
            var trayHomePos = trayT.position; var trayHomeRot = trayT.rotation;
            var shelf = World(GameObject.Find("Dock_TrayShelf").GetComponent<Renderer>());
            var tray = World(trayT.GetComponent<Renderer>());
            var wrist = ov.RightBones[3];
            var md = new StringBuilder("# 局部调握点：手绕横杆换角度握（诊断）\n\n腕部滚转 φ（相对端盘姿态；已有动画里右腕用过 0…25°），挂点绕横杆轴反向转 φ。托盘在原位，七号根放到对应夹取位。\n\n| φ | 夹住：右手 ↔ 托盘架 | 半开：右手 ↔ 托盘架 | 右手（不含两爪）↔ 托盘 | 托盘倾角 | 最近的零件 |\n|---|---|---|---|---|---|\n");
            foreach (float phi in new[] { -40f, -35f, -30f, -25f, -20f, -15f, -10f, -5f, 0f, 5f, 10f, 15f, 20f, 25f, 30f, 35f, 40f })
            {
                var (hp, hr) = RolledHold(holdPos0, holdRot0, phi);
                hold.localPosition = hp; hold.localRotation = hr;
                robot.position = H;
                ov.RightWeight = 1f; ov.LeftWeight = 1f; ov.JawOpen = 0f;
                for (int i = 0; i < 6; i++) ov.RightBones[i].localRotation = ov.RightTarget(i);
                wrist.localRotation = ov.RightTarget(3) * Quaternion.AngleAxis(phi, Vector3.right);
                for (int i = 0; i < 4; i++) ov.LeftBones[i].localRotation = ov.LeftTarget(i);
                float tilt = Vector3.Angle(hold.forward, Vector3.up);
                robot.position = H + (trayHomePos - hold.position);
                var hand = wrist.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToList();
                var handM = hand.Select(World).ToList();
                var shelfGrip = Min(handM, new[] { shelf }.ToList(), 0.05f);
                var noJaw = hand.Where(r => !r.name.StartsWith("Arm_R_Jaw")).Select(World).ToList();
                var trayGap = Min(noJaw, new[] { tray }.ToList(), 0.05f);
                ov.JawOpen = 1f;
                for (int i = 4; i < 6; i++) ov.RightBones[i].localRotation = ov.RightTarget(i);
                var shelfHalf = Min(wrist.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).Select(World).ToList(), new[] { shelf }.ToList(), 0.05f);
                md.AppendLine($"| {phi:+0;-0;0}° | {shelfGrip.d * 1000:F1} mm | {shelfHalf.d * 1000:F1} mm | {trayGap.d * 1000:F1} mm | {tilt:F2}° | {shelfHalf.a} / {trayGap.a} |");
            }
            hold.localPosition = holdPos0; hold.localRotation = holdRot0;
            Directory.CreateDirectory(TwoNightBuilder.DocsDir);
            File.WriteAllText(Path.Combine(TwoNightBuilder.DocsDir, "grip_roll_probe.md"), md.ToString(), new UTF8Encoding(false));
            Debug.Log("[TwoNightBuild] grip_roll_probe 已写出");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
