using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BorderRepair.FirstOrder
{
    /// <summary>测试工具（不参与玩法）：逐帧记录新旧轴承的显示、搬动零件的单帧位移 / 转角、搬运途中的穿模（凸包精查）。PlayMode 测试和布局 A/B 实测共用。</summary>
    public class FirstOrderMotionMonitor : MonoBehaviour
    {
        public FirstOrderFlow flow;
        public int frames, travelFrames, travelBroad, maxAtSeat;
        public readonly HashSet<string> travelNear = new HashSet<string>();
        public readonly HashSet<string> carries = new HashSet<string>();
        public readonly HashSet<string> aabbOnly = new HashSet<string>();
        public float maxStep, maxTurn;
        public string maxStepWhere = "", maxTurnWhere = "";
        public readonly List<string> bearingViolations = new List<string>();
        public readonly List<string> jumpViolations = new List<string>();
        public readonly List<string> travelHits = new List<string>();
        readonly Dictionary<FirstOrderPart, Pose> last = new Dictionary<FirstOrderPart, Pose>();
        const float MaxStep = 0.08f, MaxTurn = 15f;

        static bool Shown(Renderer r) => r.enabled && r.gameObject.activeInHierarchy;

        void LateUpdate()
        {
            if (flow == null) return;
            frames++;
            string at = $"第 {frames} 帧（{flow.Step}）";
            // ---- 新旧轴承
            if (flow.OriginalBearingRenderer.enabled) bearingViolations.Add($"{at}：原轴承渲染器打开了（和磨损件叠在一起）");
            var seat = flow.Bearing.HomeWorldPose().position;
            var worn = flow.Bearing.Renderers().Where(r => r != flow.OriginalBearingRenderer && Shown(r)).ToList();
            var nu = flow.NewBearing.Renderers().Where(Shown).ToList();
            int atSeat = (worn.Count > 0 && Vector3.Distance(flow.Bearing.transform.position, seat) < 0.02f ? 1 : 0) +
                         (nu.Count > 0 && Vector3.Distance(flow.NewBearing.transform.position, seat) < 0.02f ? 1 : 0);
            maxAtSeat = Mathf.Max(maxAtSeat, atSeat);
            if (atSeat > 1) bearingViolations.Add($"{at}：新旧轴承同时在轴承位");
            if (worn.Any(w => nu.Any(n => w.bounds.Intersects(n.bounds)))) bearingViolations.Add($"{at}：磨损件和新件的显示范围重叠");
            // ---- 单帧位移 / 转角
            foreach (var p in flow.TrackedParts)
            {
                var now = new Pose(p.transform.position, p.transform.rotation);
                if (last.TryGetValue(p, out var prev))
                {
                    float d = Vector3.Distance(prev.position, now.position), a = Quaternion.Angle(prev.rotation, now.rotation);
                    if (d > maxStep) { maxStep = d; maxStepWhere = $"{p.partId} {at}"; }
                    if (a > maxTurn) { maxTurn = a; maxTurnWhere = $"{p.partId} {at}"; }
                    if (d > MaxStep || a > MaxTurn) jumpViolations.Add($"{p.partId} {at}：单帧位移 {d * 1000:F0} mm、转角 {a:F1}°");
                }
                last[p] = now;
            }
            // ---- 搬运途中（竖直抬起、平移转向、落到工作台）：每个网格先用有向包围盒粗筛，和别的实体碰撞有重叠时再用凸包精确求穿入深度
            foreach (var p in new[] { flow.Cover, flow.Bearing, flow.NewBearing })
            {
                if (flow.Carrying != p) continue;
                carries.Add(flow.LastCarry);
                // 没有碰撞的物件（工作台台灯、摆件等）只能比包围盒：记下来，测试里人工判断
                var pb = p.WorldBounds();
                var mine = new HashSet<Renderer>(p.Renderers());
                foreach (var o in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    if (o.enabled && !mine.Contains(o) && o.GetComponent<Collider>() == null && o.bounds.max.y < 2.2f && o.bounds.Intersects(pb) && o.bounds.min.y > pb.min.y - 0.5f)
                        aabbOnly.Add($"{p.partId}↔{o.name}（{flow.Step}）");
                travelFrames++;
                var own = new HashSet<Transform>(p.members.Append(p.transform).SelectMany(t => t.GetComponentsInChildren<Transform>(true)));
                Physics.SyncTransforms();
                foreach (var r in p.Renderers().Where(Shown))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null) continue;
                    var mb = mf.sharedMesh.bounds;
                    var c = r.transform.TransformPoint(mb.center);
                    var ext = Vector3.Scale(mb.extents, r.transform.lossyScale);
                    ext = new Vector3(Mathf.Abs(ext.x), Mathf.Abs(ext.y), Mathf.Abs(ext.z));
                    var cand = Physics.OverlapBox(c, ext, r.transform.rotation, ~0, QueryTriggerInteraction.Ignore).Where(h => !own.Contains(h.transform) && h.name != "Engine_Shaft_L").ToList();   // 轴承与转轴同轴套装，不算
                    if (cand.Count == 0) continue;
                    travelBroad++;
                    var go = new GameObject("TmpTravelHull");
                    go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                    go.transform.localScale = r.transform.lossyScale;
                    var hull = go.AddComponent<MeshCollider>();
                    hull.convex = true;
                    hull.sharedMesh = mf.sharedMesh;
                    foreach (var h in cand)
                    {
                        if (Physics.ComputePenetration(hull, go.transform.position, go.transform.rotation, h, h.transform.position, h.transform.rotation, out _, out var d) && d > 0.0005f)
                        { if (travelHits.Count < 50) travelHits.Add($"{p.partId}/{r.name} {at} 穿入 {h.name} {d * 1000:F1} mm"); }
                        else travelNear.Add(h.name);
                    }
                    DestroyImmediate(go);
                }
            }
        }
    }
}
