using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BorderRepair.Art.Unit07TrayVariant.EditorTools
{
    /// <summary>
    /// 只读诊断（不保存场景）：在 72838e9 的端盘姿态下，把右手各零件的顶点换算到“托盘本地坐标”（X 长轴、Y 前后、Z 向上，米），
    /// 给出每个零件的包围范围。用来决定新提手的高度和支腿位置。旧挂点只作起点。
    /// </summary>
    public static class TrayVariantHandProbe
    {
        const string Scene = "Assets/BorderRepair/Scenes/Slice/Unit07_Night.unity";
        const string OutDir = "ArtSource/Unit07TrayVariant/Reports";

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
            var toTray = hold.worldToLocalMatrix;   // 挂点 = 托盘原点（盘底中心）在手上的位置；挂点本地轴 = 托盘本地轴
            var sb = new StringBuilder("# 右手零件在托盘本地坐标里的范围（旧挂点，夹住）\n\n单位 mm；托盘本地 X 长轴、Y 前后、Z 向上，原点盘底中心。\n\n| 零件 | X 最小 | X 最大 | Y 最小 | Y 最大 | Z 最小 | Z 最大 |\n|---|---|---|---|---|---|---|\n");
            foreach (var r in ov.RightBones[3].GetComponentsInChildren<Renderer>(true).Where(r => r.enabled))
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var m = toTray * r.transform.localToWorldMatrix;
                var v = mf.sharedMesh.vertices.Select(p => m.MultiplyPoint3x4(p) * 1000f).ToArray();
                sb.AppendLine($"| {r.name} | {v.Min(p => p.x):F1} | {v.Max(p => p.x):F1} | {v.Min(p => p.y):F1} | {v.Max(p => p.y):F1} | {v.Min(p => p.z):F1} | {v.Max(p => p.z):F1} |");
            }
            var gc = toTray.MultiplyPoint3x4(ov.RightBones[3].TransformPoint(new Vector3(-0.0824f, -0.0007f, 0.0284f))) * 1000f;
            sb.AppendLine($"\n- 右手咬合中心（审计腕骨本地 (-82.4, -0.7, 28.4) mm）在托盘本地：{gc:F1} mm");
            sb.AppendLine($"- 挂点（腕骨本地）：{hold.localPosition * 1000f:F2} mm，{hold.localRotation.x:F5}, {hold.localRotation.y:F5}, {hold.localRotation.z:F5}, {hold.localRotation.w:F5}");
            var tray = GameObject.Find("Dock_PartsTray").transform;
            sb.AppendLine($"- 原托盘：父对象 `{tray.parent.name}`，本地位置 {tray.localPosition:F5}，本地旋转 {tray.localRotation.x:F5}, {tray.localRotation.y:F5}, {tray.localRotation.z:F5}, {tray.localRotation.w:F5}，本地缩放 {tray.localScale:F4}；世界 {tray.position:F4}");
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "hand_in_tray_frame_before.md"), sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[TrayVariant] hand probe 已写出");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
