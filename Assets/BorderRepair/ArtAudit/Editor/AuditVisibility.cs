using System.Linq;
using System.Text;
using BorderRepair.FirstOrder;
using UnityEditor;
using UnityEngine;
using static BorderRepair.ArtAudit.AuditCommon;

namespace BorderRepair.ArtAudit
{
    /// <summary>
    /// 第 5 步（只读，场景不保存）：整引擎替换要碰的固定件 / 插头，在各游戏镜头里看不看得到。
    /// 方法：每个目标网格取最多 80 个顶点（略向外偏 1 mm），从镜头位置打射线（Physics.Raycast，七号零件都有 MeshCollider），
    /// 第一个命中的是目标自己 = 这个点可见。另记目标在 1600×900 画面里的包围盒尺寸。七号停在维修座上（落座、夹具闭合）。
    /// 限制：蒙皮线缆没有碰撞体，挡不住射线；工作台 / 维修座只有部分物件有碰撞体，结果对它们偏乐观。
    /// </summary>
    public static class AuditVisibility
    {
        public static void Run()
        {
            AuditTray.Setup(); AuditTray.RestPose();
            Physics.SyncTransforms();
            var robot = Find("UNIT07_RobotV4_DockReady");
            var rig = Object.FindFirstObjectByType<FirstOrderCameraRig>();
            var targets = new[] { "ConnectionKey_L", "ConnectionKey_Bolt_L", "EngineAnchor_L", "BodySocket_L", "ExternalCable_L_PlugEngine", "ExternalCable_L_PlugBody", "AuxCable_L_PlugEngine", "AuxCable_L_PlugBody", "Engine_CoverLatch_Outer_L", "Engine_CoverLatch_Rear_L" };
            var shots = new[] { FirstOrderCameraRig.Dock, FirstOrderCameraRig.EngineL, FirstOrderCameraRig.EngineRear, FirstOrderCameraRig.EngineClose, FirstOrderCameraRig.Overview };
            var md = new StringBuilder("# 左引擎固定件 / 插头在游戏镜头里的可见比例\n\n");
            md.AppendLine("七号停在维修座上（落座、夹具闭合，上盖装着）。每格 = 可见采样点比例 / 包围盒在 1600×900 画面里的宽度（px）。“—” = 不在画面内。\n");
            md.AppendLine("| 目标 | " + string.Join(" | ", shots.Select(s => $"{s}（{FirstOrderCameraRig.Labels[s]}）")) + " |");
            md.AppendLine("|---|" + string.Join("", shots.Select(_ => "---|")));
            var js = new StringBuilder("{");
            var camGo = new GameObject("VisCam"); var cam = camGo.AddComponent<Camera>(); cam.aspect = 16f / 9f;
            foreach (var tn in targets)
            {
                var tr = FindUnder(robot, tn); if (tr == null) continue;
                var r = tr.GetComponent<Renderer>(); var col = tr.GetComponent<Collider>();
                var wm = World(r);
                var step = Mathf.Max(1, wm.v.Length / 80);
                var samples = wm.v.Where((v, i) => i % step == 0).Select(v => v + (v - r.bounds.center).normalized * 0.001f).ToList();
                md.Append($"| `{tn}`（{(tr.IsChildOf(FindUnder(robot, "Engine_L_Hinge")) ? "随引擎" : "机身")}） |");
                js.Append($"\"{tn}\":{{");
                foreach (var shot in shots)
                {
                    var s = rig.Get(shot);
                    cam.transform.SetPositionAndRotation(s.pose.position, s.pose.rotation); cam.fieldOfView = s.fov;
                    var vp = cam.WorldToViewportPoint(r.bounds.center);
                    if (vp.z <= 0 || vp.x < 0 || vp.x > 1 || vp.y < 0 || vp.y > 1) { md.Append(" — |"); js.Append($"\"{shot}\":null,"); continue; }
                    int vis = 0;
                    foreach (var p in samples)
                    {
                        var dir = p - cam.transform.position;
                        if (Physics.Raycast(cam.transform.position, dir.normalized, out var hit, dir.magnitude + 0.003f, ~0, QueryTriggerInteraction.Ignore))
                        { if (hit.collider == col || hit.collider.transform == tr) vis++; }
                        else vis++;
                    }
                    var b = r.bounds; float minx = 1, maxx = 0;
                    for (int i = 0; i < 8; i++) { var q = cam.WorldToViewportPoint(new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y, i / 4 == 0 ? b.min.z : b.max.z)); minx = Mathf.Min(minx, q.x); maxx = Mathf.Max(maxx, q.x); }
                    float frac = vis / (float)samples.Count;
                    md.Append($" {frac:P0} / {(maxx - minx) * 1600f:F0} px |");
                    js.Append($"\"{shot}\":{{\"visible\":{F(frac)},\"px\":{F((maxx - minx) * 1600f)}}},");
                }
                md.AppendLine();
                js.Append("\"x\":0},");
            }
            Object.DestroyImmediate(camGo);
            md.AppendLine("\n限制：蒙皮线缆没有碰撞体、不挡射线；只看七号停在维修座上、上盖装着的状态。");
            js.Append("\"done\":true}");
            Write("visibility.md", md.ToString()); Write("visibility.json", js.ToString());
            Debug.Log("[ArtAudit] visibility 已写出");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
