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
    /// 只读诊断：审计握法下，右手各零件与托盘是“贴着”还是“穿进去”。把托盘相对手平移 0.5–5 mm（上 / 下 / 沿横杆 / 七号左右 / 前后），
    /// 看哪个方向、多少毫米后分开。夹住与上爪张开两种状态各量一次。
    /// </summary>
    public static class TwoNightJawProbe
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(TwoNightBuilder.RobotPath, OpenSceneMode.Single);
            var inc = Object.FindFirstObjectByType<TrayIncident>();
            var so = new SerializedObject(inc);
            var P = so.FindProperty("place").vector3Value;
            var robot = inc.RobotRoot; var ov = inc.Overlay;
            var hold = so.FindProperty("holdAnchor").objectReferenceValue as Transform;
            var body = so.FindProperty("body").objectReferenceValue as Transform;
            var trayT = inc.Tray.transform;
            var wrist = ov.RightBones[3];
            robot.position = P;
            var md = new StringBuilder("# 右手 ↔ 手里的托盘：贴着还是穿进（诊断）\n\n托盘按挂点放在手里；把托盘相对手平移下表的距离后再量。单位 mm，0 = 接触或穿插。\n\n");
            var bar = (hold.rotation * Vector3.up).normalized;
            var dirs = new (string n, Vector3 v)[] { ("上", Vector3.up), ("下", Vector3.down), ("沿横杆+", bar), ("沿横杆−", -bar), ("七号右", body.right), ("七号左", -body.right), ("七号前", body.forward), ("七号后", -body.forward) };
            float[] offs = { 0f, 0.001f, 0.002f, 0.005f, 0.01f, 0.02f, 0.03f };
            foreach (var (label, upper) in new[] { ("夹住", 0f), ("上爪张开（Half）", 1f) })
            {
                ov.RightWeight = 1f; ov.LeftWeight = 1f; ov.JawOpen = 0f; ov.UpperJawExtra = upper;
                for (int i = 0; i < 6; i++) ov.RightBones[i].localRotation = ov.RightTarget(i);
                for (int i = 0; i < 4; i++) ov.LeftBones[i].localRotation = ov.LeftTarget(i);
                trayT.SetPositionAndRotation(hold.position, hold.rotation);
                var tray = World(trayT.GetComponent<Renderer>());
                var parts = wrist.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).Select(r => (r.name, m: World(r))).ToList();
                md.AppendLine($"## {label}\n");
                foreach (var (n, v) in dirs)
                {
                    md.AppendLine($"### 托盘向{n}\n\n| 零件 | " + string.Join(" | ", offs.Select(o => (o * 1000).ToString("0.#") + " mm")) + " |\n|---|" + string.Concat(offs.Select(_ => "---|")));
                    foreach (var (pn, pm) in parts)
                    {
                        var row = offs.Select(o => Distance(pm, Moved(tray, Matrix4x4.Translate(v * o)), 0.04f) * 1000f).ToArray();
                        if (row.All(x => x >= 19.9f)) continue;
                        md.AppendLine($"| {pn} | " + string.Join(" | ", row.Select(x => x >= 19.9f ? "≥20" : x.ToString("F1"))) + " |");
                    }
                    md.AppendLine();
                }
            }
            Directory.CreateDirectory(TwoNightBuilder.DocsDir);
            File.WriteAllText(Path.Combine(TwoNightBuilder.DocsDir, "jaw_contact_probe.md"), md.ToString(), new UTF8Encoding(false));
            Debug.Log("[TwoNightBuild] jaw_contact_probe 已写出");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
