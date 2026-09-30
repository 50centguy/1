using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UNIT 07 维修座 FBX 导入检查（只在测试副本 BorderRepairStation_V4ImportTest 中使用）。
/// 检查：导入警告、对象层级与名称、尺寸、三角面、自定义属性、机器人锚点位置，
/// 以及开关手柄 / 夹具在 Unity 中绕本地轴转动的方向是否与 Blender 中写的角度一致。
/// </summary>
public class Unit07DockImportCheck : AssetPostprocessor
{
    const string Dir = "Assets/Unit07DockTest";
    static readonly Dictionary<string, string> UserProps = new Dictionary<string, string>();
    static readonly List<string> Messages = new List<string>();

    void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] names, object[] values)
    {
        if (!assetPath.StartsWith(Dir)) return;
        UserProps[go.name] = string.Join(", ", names.Select((n, i) => $"{n}={values[i]}"));
    }

    public static void Run()
    {
        var sb = new StringBuilder();
        Application.logMessageReceived += (m, s, t) => { if (t != LogType.Log) Messages.Add($"{t}: {m.Split('\n')[0]}"); };
        foreach (var f in new[] { "UNIT07_ServiceDock.fbx", "UNIT07_RobotPlaceholder.fbx" })
            AssetDatabase.ImportAsset($"{Dir}/{f}", ImportAssetOptions.ForceUpdate);
        var dock = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/UNIT07_ServiceDock.fbx");
        var ph = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/UNIT07_RobotPlaceholder.fbx");
        var inst = (GameObject)Object.Instantiate(dock);
        var phInst = (GameObject)Object.Instantiate(ph);
        Transform F(string n) => inst.GetComponentsInChildren<Transform>(true).First(t => t.name == n);

        var mfs = inst.GetComponentsInChildren<MeshFilter>(true);
        int tris = mfs.Sum(m => m.sharedMesh.triangles.Length / 3);
        var rs = inst.GetComponentsInChildren<Renderer>(true);
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        var pb = phInst.GetComponentsInChildren<Renderer>(true)[0].bounds; foreach (var r in phInst.GetComponentsInChildren<Renderer>(true)) pb.Encapsulate(r.bounds);
        var mats = rs.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct().OrderBy(n => n).ToArray();
        sb.AppendLine($"root={inst.name} localEuler={inst.transform.localEulerAngles} meshes={mfs.Length} triangles={tris} materials={mats.Length} [{string.Join(", ", mats)}]");
        sb.AppendLine($"dock bounds center={b.center:F3} size={b.size:F3} min={b.min:F3} max={b.max:F3}");
        sb.AppendLine($"placeholder bounds size={pb.size:F4} min={pb.min:F4} max={pb.max:F4}");
        sb.AppendLine($"Dock_RobotAnchor world={F("Dock_RobotAnchor").position:F4}");
        foreach (var n in new[] { "Dock_ContactPad_L", "Dock_ContactPad_R", "Dock_Clamp_L", "Dock_Clamp_R", "Dock_PowerSwitch", "Dock_PowerSwitch_Lever", "Dock_PartsTray", "Dock_MagneticBox" })
        {
            var t = F(n);
            sb.AppendLine($"{n}: parent={t.parent.name} world={t.position:F4} localEuler={t.localEulerAngles:F1} right={t.right:F2} up={t.up:F2} fwd={t.forward:F2}");
        }

        // 开关：导入后处于 OFF（手柄朝下）；按 Blender 的 on_deg - off_deg = -70° 绕本地 X 转，握把应当向上
        var lever = F("Dock_PowerSwitch_Lever"); var grip = F("Dock_PowerSwitch_LeverGrip");
        float yOff = grip.position.y;
        var q0 = lever.localRotation;
        lever.Rotate(-70f, 0, 0, Space.Self);
        float yOnSameSign = grip.position.y;
        lever.localRotation = q0; lever.Rotate(70f, 0, 0, Space.Self);
        float yOnFlipped = grip.position.y;
        lever.localRotation = q0;
        sb.AppendLine($"lever grip Y: OFF={yOff:F4}; after Rotate(-70 about local X)={yOnSameSign:F4}; after Rotate(+70)={yOnFlipped:F4} -> " +
                      (yOnSameSign > yOff ? "Unity uses the same sign as Blender (ON = rotate -70 from OFF)" : "Unity sign is flipped (ON = rotate +70 from OFF)"));
        // 夹具：Blender open_deg（L = +30，R = -30）绕本地 Y；张开时夹面应远离中线（|x| 变大）
        foreach (var s in new[] { "L", "R" })
        {
            var c = F($"Dock_Clamp_{s}"); var pad = F($"Dock_Clamp_{s}_JawPad");
            float x0 = Mathf.Abs(pad.position.x); var cq = c.localRotation;
            float deg = s == "L" ? 30f : -30f;
            c.Rotate(0, deg, 0, Space.Self); float xSame = Mathf.Abs(pad.position.x);
            c.localRotation = cq; c.Rotate(0, -deg, 0, Space.Self); float xFlip = Mathf.Abs(pad.position.x);
            c.localRotation = cq;
            sb.AppendLine($"clamp {s} jaw |x|: closed={x0:F4}; Rotate({deg} about local Y)={xSame:F4}; Rotate({-deg})={xFlip:F4} -> " +
                          (xSame > x0 ? "same sign as Blender open_deg" : "flipped sign"));
        }
        sb.AppendLine("user properties imported: " + UserProps.Count);
        foreach (var kv in UserProps.OrderBy(k => k.Key)) sb.AppendLine($"  {kv.Key}: {kv.Value}");
        sb.AppendLine("warnings/errors during import: " + Messages.Count);
        foreach (var m in Messages.Distinct()) sb.AppendLine("  " + m);
        Object.DestroyImmediate(inst); Object.DestroyImmediate(phInst);
        File.WriteAllText(Path.GetFullPath("Unit07DockImportCheck.txt"), sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[Unit07Dock]\n" + sb);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
