using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.FirstOrder.EditorTools.LayoutAB
{
    /// <summary>
    /// 布局 A/B 测试副本：用同一个构建脚本生成两个场景副本，正式的首单测试场景（Unit07FirstOrder_Test.unity）不动。
    /// A＝原布局；B＝维修架在玩家前方偏左、工作台在前方偏右（摆位由 LayoutPlanner 搜索得出，数值写死在这里，方便复查）。
    /// 也定义两个布局共用的“同机位”相机位姿：两个场景用完全相同的位姿渲染。
    /// </summary>
    public static class LayoutABScenes
    {
        public const string OutDir = "Docs/Integration/Unit07FirstOrder/LayoutAB";
        public const string SceneDir = FirstOrderSceneBuilder.Root + "/Scenes/LayoutAB";
        public const string SceneA = SceneDir + "/Unit07FirstOrder_LayoutA.unity";
        public const string SceneB = SceneDir + "/Unit07FirstOrder_LayoutB.unity";

        // ---- A：原布局（维修架锚点 (0, 0.72, 1.15)）；B：LayoutPlanner 选出的第 1 名
        public static readonly Vector3 AnchorA = new Vector3(0f, 0.72f, 1.15f);
        public static readonly Vector3 AnchorB = new Vector3(PlannerB.anchorX, 0.72f, PlannerB.anchorZ);
        public const float DyawB = PlannerB.dyaw;
        public static readonly (Vector3 pos, float yaw) StandA = (new Vector3(PlannerB.standAX, 0f, PlannerB.standAZ), PlannerB.facingA);
        public static readonly (Vector3 pos, float yaw) StandB = (new Vector3(PlannerB.standBX, 0f, PlannerB.standBZ), PlannerB.facingB);

        public static (Vector3 pos, float yaw) Stand(string id) => id == "A" ? StandA : StandB;
        public static Vector3 Anchor(string id) => id == "A" ? AnchorA : AnchorB;

        public static FirstOrderSceneBuilder.Layout LayoutA() => new FirstOrderSceneBuilder.Layout
        {
            id = "A", scenePath = SceneA, buildLog = "LayoutAB/build_log_A.txt",
            note = "A：原布局——维修架在工作台对面 1.15 m，正面朝工作台；玩家站在两者之间",
        };

        /// <summary>B：在 A 的基础上把“维修架 + 七号”绕锚点再转 DyawB，锚点移到 AnchorB（工作台、房间不动）。</summary>
        public static FirstOrderSceneBuilder.Layout LayoutB()
        {
            var q = Quaternion.Euler(0f, DyawB, 0f);
            // 维修架根节点位置：A 的根节点相对锚点的偏移跟着转
            var rootB = AnchorB + q * (FirstOrderSceneBuilder.DockPosition - AnchorA);
            var standEye = StandB.pos + Vector3.up * 2.0f;
            return new FirstOrderSceneBuilder.Layout
            {
                id = "B", scenePath = SceneB, buildLog = "LayoutAB/build_log_B.txt",
                dockPosition = rootB, dockRotation = q * FirstOrderSceneBuilder.DockRotation,
                // 总览：站在玩家身后上方，同时看到维修架和工作台
                overviewPos = StandB.pos + Quaternion.Euler(0f, -StandB.yaw, 0f) * new Vector3(0f, 0f, 0.85f) + Vector3.up * 2.0f,
                overviewTarget = (AnchorB + new Vector3(0.05f, 0.95f, -0.45f)) / 2f + new Vector3(0f, 0.25f, 0f),
                note = $"B：维修架在玩家前方偏左、工作台在前方偏右——维修架锚点 ({AnchorB.x:F3}, {AnchorB.z:F3})，在 A 的朝向上再转 {DyawB:F0}°；玩家站位 ({StandB.pos.x:F2}, {StandB.pos.z:F2})，朝向 {StandB.yaw:F0}°",
            };
        }

        [MenuItem("Border Repair/Unit07 First Order/Layout A-B/Build Scene Copies")]
        public static void BuildBoth()
        {
            FirstOrderSceneBuilder.BuildWith(LayoutA());
            FirstOrderSceneBuilder.BuildWith(LayoutB());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ---------------------------------------------------------------- 同机位相机

        static Pose Look(Vector3 from, Vector3 to) => new Pose(from, Quaternion.LookRotation(to - from));

        static Pose Dir(Vector3 eye, float yaw, float pitch) =>
            new Pose(eye, Quaternion.Euler(pitch, 180f - yaw, 0f));      // yaw：0 = −Z（朝工作台），正值向 +X（面朝工作台时的左手边）

        /// <summary>
        /// 两个布局共用的一组相机位姿（名字, 位姿, 视场角或正交半高, 是否正交）。
        /// 站位 A、B 各看前、左 60°、右 60°、正后方；俯视；房间角落；以及读记录、对比、看复测的视线（两个站位都拍）。
        /// </summary>
        public static IEnumerable<(string name, Pose pose, float fov, bool ortho)> SharedShots(FirstOrderFlow flow, string moment)
        {
            if (moment.StartsWith("M1") || moment.StartsWith("M4"))
                yield return ("overhead", new Pose(new Vector3(0f, 2.30f, 0.275f), Quaternion.LookRotation(Vector3.down, Vector3.back)), 1.55f, true);
            if (moment.StartsWith("M1") || moment.StartsWith("M4"))
                yield return ("corner", Look(new Vector3(-1.45f, 2.15f, 1.60f), new Vector3(0.25f, 0.85f, 0.10f)), 72f, false);
            foreach (var id in new[] { "A", "B" })
            {
                var (pos, yaw) = Stand(id);
                var eye = pos + Vector3.up * LayoutPlanner.EyeHeight;
                if (moment.StartsWith("M1"))
                {
                    yield return ($"stand{id}_front", Dir(eye, yaw, 28f), 75f, false);
                    yield return ($"stand{id}_left60", Dir(eye, yaw + 60f, 25f), 75f, false);
                    yield return ($"stand{id}_right60", Dir(eye, yaw - 60f, 25f), 75f, false);
                    yield return ($"stand{id}_back", Dir(eye, yaw + 180f, 15f), 75f, false);
                }
                if (moment.StartsWith("M2"))
                    yield return ($"stand{id}_readRecord", Look(eye, flow.CoverLabel.bounds.center), 40f, false);
                if (moment.StartsWith("M3"))
                    yield return ($"stand{id}_compare", Look(eye, (flow.OldTrayZone.landing.position + flow.NewBearing.WorldBounds().center) / 2f), 45f, false);
                if (moment.StartsWith("M4"))
                    yield return ($"stand{id}_watchRetest", Look(eye, Anchor(id) + Vector3.up * 0.55f), 70f, false);
            }
        }

        /// <summary>俯视图上的临时标记（只在拍俯视图时出现）：两个站位（A 绿、B 蓝，半径 0.30 m 人体）和朝向。</summary>
        public static List<GameObject> OverheadMarkers()
        {
            var list = new List<GameObject>();
            Material M(Color c) { var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.SetColor("_BaseColor", c); return m; }
            foreach (var (id, col) in new[] { ("A", new Color(0.15f, 0.75f, 0.35f)), ("B", new Color(0.2f, 0.45f, 0.95f)) })
            {
                var (pos, yaw) = Stand(id);
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(disc.GetComponent<Collider>());
                disc.name = "LayoutAB_Stand_" + id;
                disc.transform.SetPositionAndRotation(pos + Vector3.up * 2.0f, Quaternion.identity);
                disc.transform.localScale = new Vector3(LayoutPlanner.BodyRadius * 2f, 0.005f, LayoutPlanner.BodyRadius * 2f);
                disc.GetComponent<Renderer>().sharedMaterial = M(col);
                var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(arrow.GetComponent<Collider>());
                arrow.name = "LayoutAB_Facing_" + id;
                var dir = Quaternion.Euler(0f, 180f - yaw, 0f) * Vector3.forward;
                arrow.transform.SetPositionAndRotation(pos + Vector3.up * 2.01f + dir * 0.35f, Quaternion.LookRotation(dir));
                arrow.transform.localScale = new Vector3(0.04f, 0.005f, 0.45f);
                arrow.GetComponent<Renderer>().sharedMaterial = M(col * 0.7f);
                list.Add(disc); list.Add(arrow);
            }
            return list;
        }
    }

    /// <summary>LayoutPlanner 的选择结果（见 planner_B.md），手工抄进来，方便复查和重复构建。</summary>
    public static class PlannerB
    {
        public const float anchorX = 0.375f, anchorZ = 0.825f, dyaw = 105f;
        public const float standBX = -0.40f, standBZ = 0.45f, facingB = 49f;
        public const float standAX = 0.05f, standAZ = 0.45f, facingA = -114f;
    }
}
