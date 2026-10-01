using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.FirstOrder.EditorTools.LayoutAB
{
    /// <summary>布局 A/B 实测 · 只读核查：打开首单测试场景（A 布局），列出工作台房间、维修架、七号的实际位置和尺寸。不保存场景。</summary>
    public static class LayoutAudit
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(FirstOrderSceneBuilder.ScenePath, OpenSceneMode.Single);
            var sb = new StringBuilder();
            string B(Bounds b) => $"min {b.min:F3} max {b.max:F3} size {b.size:F3}";
            Bounds Of(GameObject g) { var rs = g.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                sb.AppendLine($"# {root.name} pos {root.transform.position:F3} rot {root.transform.eulerAngles:F1}" +
                              (root.GetComponentsInChildren<Renderer>().Any(r => r.enabled) ? "  " + B(Of(root)) : ""));
                if (root.name == "WorkbenchArea" || root.name == "Unit07ServiceDock")
                    foreach (Transform c in root.transform)
                    {
                        if (!c.gameObject.activeInHierarchy || !c.GetComponentsInChildren<Renderer>().Any(r => r.enabled)) { sb.AppendLine($"  - {c.name}（关闭或无渲染）"); continue; }
                        sb.AppendLine($"  - {c.name}: {B(Of(c.gameObject))}");
                        foreach (Transform cc in c)
                            if (cc.GetComponentsInChildren<Renderer>().Any(r => r.enabled)) sb.AppendLine($"      · {cc.name}: {B(Of(cc.gameObject))}");
                    }
            }
            var robot = GameObject.Find("UNIT07_RobotV4_DockReady");
            foreach (var n in new[] { "Engine_L_Hinge", "Engine_R_Hinge", "Engine_UpperCover_L", "Engine_UpperCover_R", "Engine_CoverLatch_Outer_L", "Engine_CoverLatch_Rear_L", "Body_Shell", "FrontBezel" })
            {
                var t = robot.GetComponentsInChildren<Transform>().First(x => x.name == n);
                var r = t.GetComponent<Renderer>();
                sb.AppendLine($"robot {n}: pos {t.position:F3}" + (r != null ? "  " + B(r.bounds) : ""));
            }
            foreach (var n in new[] { "Dock_Clamp_L_Grip", "Dock_Clamp_R_Grip", "Dock_PowerSwitch_LeverGrip", "Dock_RobotAnchor" })
            {
                var t = GameObject.Find(n).transform; var r = t.GetComponent<Renderer>();
                sb.AppendLine($"dock {n}: pos {t.position:F3}" + (r != null ? "  " + B(r.bounds) : ""));
            }
            sb.AppendLine("bench colliders: " + string.Join("、", GameObject.Find("WorkbenchArea").GetComponentsInChildren<Collider>().Select(c => $"{c.name}({c.GetType().Name}{(c.isTrigger ? ",trigger" : "")})")));
            Directory.CreateDirectory("Docs/Integration/Unit07FirstOrder/LayoutAB");
            File.WriteAllText("Docs/Integration/Unit07FirstOrder/LayoutAB/audit_layout_A.txt", sb.ToString(), new UTF8Encoding(false));
            EditorApplication.Exit(0);
        }
    }
}
