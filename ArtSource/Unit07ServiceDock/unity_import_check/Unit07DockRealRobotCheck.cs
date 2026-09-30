using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 隔离复测（只在测试副本中）：真实 RobotV4 预制体对位 Dock_RobotAnchor，
/// 检查接触面高度、安装轨间隙，并按 FBX 属性 unity_open_deg 实际转动夹具，确认方向和与机背螺栓的距离。
/// 距离按网格顶点估算（精确的网格相交已在 Blender 里用真实网格复测）。
/// </summary>
public static class Unit07DockRealRobotCheck
{
    static Vector3[] W(Transform t)
    {
        var mf = t.GetComponent<MeshFilter>();
        return mf.sharedMesh.vertices.Select(v => t.TransformPoint(v)).ToArray();
    }

    static Transform Mesh(GameObject root, string n) =>
        root.GetComponentsInChildren<Renderer>(true).First(r => r.name == n).transform;

    static float MinDist(Vector3[] a, Vector3[] b)
    {
        float d = float.MaxValue;
        foreach (var p in a) foreach (var q in b) d = Mathf.Min(d, (p - q).sqrMagnitude);
        return Mathf.Sqrt(d);
    }

    // 点到三角形的最近距离（精确）
    static float PointTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return (p - a).magnitude;
        Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return (p - b).magnitude;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return (p - (a + ab * (d1 / (d1 - d3)))).magnitude;
        Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return (p - c).magnitude;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return (p - (a + ac * (d2 / (d2 - d6)))).magnitude;
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) return (p - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))))).magnitude;
        float denom = 1f / (va + vb + vc);
        return (p - (a + ab * (vb * denom) + ac * (vc * denom))).magnitude;
    }

    static (Vector3[] v, int[] t) Geo(Transform tr)
    {
        var m = tr.GetComponent<MeshFilter>().sharedMesh;
        return (m.vertices.Select(x => tr.TransformPoint(x)).ToArray(), m.triangles);
    }

    // 两个网格间的最近距离：双向“顶点到三角形”（不含边与边之间的情况，对这里的平面接触足够）
    static float MeshDist(Transform a, Transform b)
    {
        var ga = Geo(a); var gb = Geo(b);
        float d = float.MaxValue;
        foreach (var p in ga.v) for (int i = 0; i < gb.t.Length; i += 3) d = Mathf.Min(d, PointTri(p, gb.v[gb.t[i]], gb.v[gb.t[i + 1]], gb.v[gb.t[i + 2]]));
        foreach (var p in gb.v) for (int i = 0; i < ga.t.Length; i += 3) d = Mathf.Min(d, PointTri(p, ga.v[ga.t[i]], ga.v[ga.t[i + 1]], ga.v[ga.t[i + 2]]));
        return d;
    }

    public static void Run()
    {
        var sb = new StringBuilder();
        var dock = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Unit07DockTest/UNIT07_ServiceDock.fbx"));
        var anchor = dock.GetComponentsInChildren<Transform>(true).First(t => t.name == "Dock_RobotAnchor");
        var robot = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RobotV4/Model/robot-final.fbx"));
        robot.transform.position = anchor.position;     // 预制体根 = V4 Root（最低点），旋转保持导入时的换算
        sb.AppendLine($"anchor={anchor.position:F4} robot root={robot.transform.position:F4} robotEuler={robot.transform.eulerAngles:F1}");

        float padTop = new[] { "Dock_ContactPad_L", "Dock_ContactPad_R" }.Max(n => W(Mesh(dock, n)).Max(v => v.y));
        float hpBottom = new[] { "Chassis_ArmHardpoint_L", "Chassis_ArmHardpoint_R" }.Min(n => W(Mesh(robot, n)).Min(v => v.y));
        sb.AppendLine($"contact: pad top y={padTop:F4}, hardpoint bottom y={hpBottom:F4}, offset={(padTop - hpBottom) * 1000:F2} mm");
        foreach (var s in new[] { "L", "R" })
        {
            var hp = W(Mesh(robot, $"Chassis_ArmHardpoint_{s}")); var pad = W(Mesh(dock, $"Dock_ContactPad_{s}"));
            bool padUnder = pad.Average(v => v.x) > hp.Min(v => v.x) && pad.Average(v => v.x) < hp.Max(v => v.x);
            sb.AppendLine($"pad {s} centre x={pad.Average(v => v.x):F4} within hardpoint {s} x range [{hp.Min(v => v.x):F4},{hp.Max(v => v.x):F4}]: {padUnder}");
        }

        foreach (var s in new[] { "L", "R" })
        {
            var clamp = dock.GetComponentsInChildren<Transform>(true).First(t => t.name == $"Dock_Clamp_{s}");
            var jawPad = Mesh(dock, $"Dock_Clamp_{s}_JawPad");
            var parts = new[] { clamp, jawPad, Mesh(dock, $"Dock_Clamp_{s}_Grip") };
            var railT = Mesh(robot, $"Chassis_MountRail_{s}");
            var boltT = Mesh(robot, s == "L" ? "Body_RearBolt_2" : "Body_RearBolt_1");
            var guideT = Mesh(dock, $"Dock_RailGuide_{s}");
            float unityOpen = s == "L" ? -30f : 30f;   // 与 FBX 属性 unity_open_deg 相同（导入检查已从 FBX 读到）
            var q0 = clamp.localRotation;
            float closedRail = MeshDist(jawPad, railT);
            sb.AppendLine($"clamp {s}: guide~rail {MeshDist(guideT, railT) * 1000:F1} mm; closed jaw pad~rail {closedRail * 1000:F1} mm (point-triangle distance)");
            float minBolt = float.MaxValue, worstAng = 0;
            for (int k = 0; k <= 24; k++)
            {
                float ang = unityOpen * k / 24f;
                clamp.localRotation = q0 * Quaternion.AngleAxis(ang, Vector3.up);
                float d = parts.Min(p => MeshDist(p, boltT));
                if (d < minBolt) { minBolt = d; worstAng = ang; }
            }
            clamp.localRotation = q0 * Quaternion.AngleAxis(unityOpen, Vector3.up);
            float openRail = MeshDist(jawPad, railT);
            clamp.localRotation = q0 * Quaternion.AngleAxis(-unityOpen, Vector3.up);
            float padInnerWrong = W(jawPad).Min(v => Mathf.Abs(v.x)); float railOuter = W(railT).Max(v => Mathf.Abs(v.x));
            clamp.localRotation = q0;
            sb.AppendLine($"clamp {s}: rotate unity_open_deg={unityOpen} -> jaw pad~rail {openRail * 1000:F1} mm (opens: {openRail > closedRail}); " +
                          $"wrong sign ({-unityOpen}) -> pad inner face |x|={padInnerWrong:F4} vs rail outer face |x|={railOuter:F4} (pad pushed into the rail: {padInnerWrong < railOuter}); " +
                          $"sweep min distance to rear bolt {minBolt * 1000:F1} mm at {worstAng:F2} deg (point-triangle distance, 1.25 deg steps)");
        }

        // 截图：正面与背面
        var camGo = new GameObject("CheckCam"); var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.25f, 0.27f, 0.3f); cam.fieldOfView = 32; cam.nearClipPlane = 0.01f;
        var light = new GameObject("CheckLight").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(40, 150, 0); light.intensity = 1.4f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);
        var rt = new RenderTexture(1280, 960, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
        void Shot(string file, Vector3 pos, Vector3 target)
        {
            for (int i = 0; i < 2; i++)
            {
                cam.transform.position = pos; cam.transform.LookAt(target); cam.Render();
            }
            RenderTexture.active = rt; var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(Path.GetFullPath(file), tex.EncodeToPNG());
        }
        Shot("Unit07Dock_RealRobot_front.png", new Vector3(0.9f, 1.35f, 2.6f), new Vector3(0, 0.95f, 0));   // 机器人正面 = +Z
        Shot("Unit07Dock_RealRobot_rear.png", new Vector3(-0.7f, 1.25f, -1.1f), new Vector3(0, 0.98f, -0.2f));
        File.WriteAllText(Path.GetFullPath("Unit07DockRealRobotCheck.txt"), sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[Unit07DockReal]\n" + sb);
        Object.DestroyImmediate(camGo); Object.DestroyImmediate(light.gameObject); Object.DestroyImmediate(dock); Object.DestroyImmediate(robot);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
