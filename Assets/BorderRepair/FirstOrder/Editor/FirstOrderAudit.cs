using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.FirstOrder.EditorTools
{
    /// <summary>
    /// 只读核查：列出 RobotV4（停靠预制体）、维修座、工作台在 Unity 里真实存在的对象、锚点、碰撞与交互接口。
    /// 不改任何资源。输出 Docs/Integration/Unit07FirstOrder/audit_*.txt。
    /// 命令行：-executeMethod BorderRepair.FirstOrder.EditorTools.FirstOrderAudit.Run
    /// </summary>
    public static class FirstOrderAudit
    {
        public const string RobotPrefab = "Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_DockReady.prefab";
        public const string DockPrefab = "Assets/BorderRepair/Prefabs/Unit07Dock/Unit07ServiceDock.prefab";
        public const string BenchPrefab = "Assets/WorkbenchArea/Prefabs/WorkbenchArea.prefab";
        public const string OutDir = "Docs/Integration/Unit07FirstOrder";

        [MenuItem("Border Repair/Unit07 First Order/Audit Real Objects")]
        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            AuditRobot();
            AuditDock();
            AuditBench();
            Debug.Log("[FirstOrderAudit] done");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static string Path(Transform t, Transform root)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (var x = t; x != null && x != root; x = x.parent) parts.Insert(0, x.name);
            return string.Join("/", parts);
        }

        static string Rend(Transform t)
        {
            var r = t.GetComponent<Renderer>();
            if (r == null) return "";
            Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : t.GetComponent<MeshFilter>()?.sharedMesh;
            var b = r.bounds;
            return $"{r.GetType().Name} mesh={m?.name} tris={(m != null ? m.triangles.Length / 3 : 0)} mats=[{string.Join(",", r.sharedMaterials.Select(x => x ? x.name : "null"))}] " +
                   $"boundsC={b.center:F3} size={b.size * 1000:F0}mm";
        }

        static void AuditRobot()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefab);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.position = Vector3.zero;
            var sb = new StringBuilder();
            sb.AppendLine($"# RobotV4 停靠预制体：{RobotPrefab}（根 {inst.name}，放在原点核查）");
            sb.AppendLine($"Animator: {inst.GetComponent<Animator>()?.runtimeAnimatorController?.name}；片段：" +
                          string.Join(", ", inst.GetComponent<Animator>()?.runtimeAnimatorController?.animationClips.Select(c => c.name).Distinct() ?? new string[0]));
            sb.AppendLine($"组件（根）：{string.Join(", ", inst.GetComponents<Component>().Select(c => c.GetType().Name))}");
            sb.AppendLine($"全部 Collider：{inst.GetComponentsInChildren<Collider>(true).Length} 个 → " +
                          string.Join("; ", inst.GetComponentsInChildren<Collider>(true).Select(c => $"{Path(c.transform, inst.transform)}[{c.GetType().Name}]")));
            sb.AppendLine($"Renderer：{inst.GetComponentsInChildren<Renderer>(true).Length} 个（SkinnedMeshRenderer {inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length}）");
            sb.AppendLine();
            foreach (var key in new[] { "Engine_L_Hinge", "Engine_R_Hinge" })
            {
                var e = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == key);
                sb.AppendLine($"## {key}：{(e != null ? Path(e, inst.transform) : "不存在")}");
                if (e == null) continue;
                sb.AppendLine($"   pos={e.position:F3} rot={e.rotation.eulerAngles:F1} up(local Y)={e.up:F3} fwd={e.forward:F3}");
                foreach (var t in e.GetComponentsInChildren<Transform>(true).Skip(1))
                    sb.AppendLine($"   {Path(t, e)}  pos={t.position:F3}  {Rend(t)}");
                sb.AppendLine();
            }
            // 引擎之外与本单有关的对象（线缆、插头、屏幕、夹爪）
            sb.AppendLine("## 其它相关对象");
            foreach (var t in inst.GetComponentsInChildren<Transform>(true).Where(t => !t.GetComponentsInParent<Transform>(true).Any(p => p.name.StartsWith("Engine_") && p.name.EndsWith("_Hinge"))
                         && (t.name.Contains("Cable") || t.name.Contains("Plug") || t.name.Contains("Screen") || t.name.Contains("Jaw") || t.name.Contains("Engine"))))
                sb.AppendLine($"   {Path(t, inst.transform)}  {Rend(t)}");
            File.WriteAllText(System.IO.Path.Combine(OutDir, "audit_robotv4.txt"), sb.ToString(), new UTF8Encoding(false));
            Object.DestroyImmediate(inst);
        }

        static void AuditDock()
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DockPrefab));
            var sb = new StringBuilder();
            sb.AppendLine($"# 维修座预制体：{DockPrefab}");
            var all = inst.GetComponentsInChildren<Transform>(true);
            var b = new Bounds(inst.transform.position, Vector3.zero);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(r.bounds);
            sb.AppendLine($"整体包围盒：中心 {b.center:F3}，尺寸 {b.size:F3} m");
            foreach (var t in all.Where(t => t.name.Contains("Anchor") || t.GetComponent<Collider>() != null || t.name.Contains("Clamp") && t.parent == inst.transform || t.name.Contains("PowerSwitch") || t.name.Contains("Tray") || t.name.Contains("Magnetic")))
            {
                var comps = string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name).Where(n => n != "Transform"));
                sb.AppendLine($"   {Path(t, inst.transform)}  pos={t.position:F3}  [{comps}]");
            }
            File.WriteAllText(System.IO.Path.Combine(OutDir, "audit_dock.txt"), sb.ToString(), new UTF8Encoding(false));
            Object.DestroyImmediate(inst);
        }

        static void AuditBench()
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BenchPrefab));
            var sb = new StringBuilder();
            sb.AppendLine($"# 工作台预制体：{BenchPrefab}");
            var b = new Bounds(inst.transform.position, Vector3.zero);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(r.bounds);
            sb.AppendLine($"整体包围盒：中心 {b.center:F3}，尺寸 {b.size:F3} m");
            foreach (var n in new[] { "Bench_Top", "Bench_Mat", "Tray_Screws", "Tray_OldParts", "Box_Bearings", "Toolbox_Tier2", "Placeholder_Prosthetic_Forearm", "Storage_PartsOrganizer" })
            {
                var t = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n);
                sb.AppendLine(t == null ? $"   {n}: 不存在" : $"   {Path(t, inst.transform)}  pos={t.position:F3}  {Rend(t)}");
            }
            sb.AppendLine($"可检查对象（WbInspectable）：{inst.GetComponentsInChildren<WorkbenchArea.WbInspectable>(true).Length} 个");
            File.WriteAllText(System.IO.Path.Combine(OutDir, "audit_workbench.txt"), sb.ToString(), new UTF8Encoding(false));
            Object.DestroyImmediate(inst);
        }
    }
}
