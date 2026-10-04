using System.IO;
using System.Linq;
using System.Text;
using BorderRepair.Dock;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static BorderRepair.TwoNight.MeshClearance;

namespace BorderRepair.TwoNight.EditorTools
{
    /// <summary>
    /// 只读诊断：七号右手在托盘原位夹住横杆时，右手各零件与托盘架的距离；托盘离开原位多高时右手才离开托盘架。
    /// 打开已生成的 Unit07_Night 场景（不保存），按端盘覆盖层的姿态摆好右臂、把七号根放到夹取位。
    /// </summary>
    public static class TwoNightGripProbe
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(TwoNightBuilder.RobotPath, OpenSceneMode.Single);
            var inc = Object.FindFirstObjectByType<TrayIncident>();
            var so = new SerializedObject(inc);
            var grip = so.FindProperty("place").vector3Value - Vector3.up * 0.008f;   // 托盘正好在原位时的根位置 = 放盘位 P 下移 8 mm
            var robot = inc.RobotRoot; var ov = inc.Overlay;
            robot.position = grip;
            foreach (float jaw in new[] { 0f, 1f })
            {
                ov.RightWeight = 1f; ov.LeftWeight = 1f; ov.JawOpen = jaw;
                for (int i = 0; i < 6; i++) ov.RightBones[i].localRotation = ov.RightTarget(i);
                for (int i = 0; i < 4; i++) ov.LeftBones[i].localRotation = ov.LeftTarget(i);
            }
            var shelf = World(GameObject.Find("Dock_TrayShelf").GetComponent<Renderer>());
            var tray = World(GameObject.Find("Dock_PartsTray").GetComponent<Renderer>());
            var md = new StringBuilder("# 右手夹取位 ↔ 托盘架（诊断）\n\n");
            foreach (float jaw in new[] { 0f, 1f })
            {
                ov.JawOpen = jaw;
                for (int i = 0; i < 6; i++) ov.RightBones[i].localRotation = ov.RightTarget(i);
                md.AppendLine($"## 夹爪 {(jaw == 0 ? "夹住（开度 0.32）" : "半开")}\n\n| 右手零件 | ↔ 托盘架 | ↔ 托盘 | 零件最低点 y |\n|---|---|---|---|");
                var hand = ov.RightBones[3].GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToList();
                foreach (var r in hand)
                {
                    var m = World(r);
                    md.AppendLine($"| {r.name} | {Distance(m, shelf, 0.05f) * 1000:F1} mm | {Distance(m, tray, 0.05f) * 1000:F1} mm | {m.b.min.y:F4} |");
                }
                // 七号整体上移多少，右手才离开托盘架 2 mm
                var handM = hand.Select(World).ToList();
                float need = -1f;
                for (float h = 0f; h <= 0.06f; h += 0.002f)
                {
                    var moved = handM.Select(x => Moved(x, Matrix4x4.Translate(Vector3.up * h))).ToList();
                    if (Min(moved, new[] { shelf }.ToList(), 0.05f).d >= 0.002f) { need = h; break; }
                }
                md.AppendLine($"\n- 七号（连同手里的盘）上移 **{(need < 0 ? "> 60" : (need * 1000).ToString("F0"))} mm** 后，右手离托盘架 ≥ 2 mm。托盘架顶面 y {shelf.b.max.y:F4}，托盘底 y {tray.b.min.y:F4}，横杆中心约 y {tray.b.max.y - 0.005f:F4}。\n");
            }
            Directory.CreateDirectory(TwoNightBuilder.DocsDir);
            File.WriteAllText(Path.Combine(TwoNightBuilder.DocsDir, "grip_vs_shelf.md"), md.ToString(), new UTF8Encoding(false));
            Debug.Log("[TwoNightBuild] grip_vs_shelf 已写出");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
