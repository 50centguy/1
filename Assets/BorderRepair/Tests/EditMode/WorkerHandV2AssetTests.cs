using System.Collections.Generic;
using System.IO;
using System.Linq;
using BorderRepair.Data;
using BorderRepair.Inspection;
using BorderRepair.Tools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BorderRepair.Tests
{
    /// <summary>工人义手 v2：与占位 prefab 同样的检查点和碰撞体、工具锚点、吸附位置、URP 材质；案件步骤不变；默认场景不受影响。</summary>
    public class WorkerHandV2AssetTests
    {
        const string V2Path = "Assets/BorderRepair/Prefabs/Items/Narrative/Item_WorkerProsthetic_v2.prefab";
        const string PlaceholderPath = "Assets/BorderRepair/Prefabs/Items/Narrative/Item_WorkerProsthetic_Placeholder.prefab";
        const string CasePath = "Assets/BorderRepair/Data/Narrative/Case_N01_WorkerProsthetic.asset";
        const string RigDir = "Assets/BorderRepair/Prefabs/Tools";

        static GameObject Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(go, $"找不到 {path}（Border Repair > Narrative > Build Worker Hand v2）");
            return go;
        }

        [Test]
        public void V2KeepsPointIdsHierarchyAndColliders()
        {
            var v2 = Load(V2Path);
            var old = Load(PlaceholderPath);
            var ids = v2.GetComponentsInChildren<InspectionPoint>(true).Select(p => p.PointId).OrderBy(x => x).ToArray();
            var oldIds = old.GetComponentsInChildren<InspectionPoint>(true).Select(p => p.PointId).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(oldIds, ids, "v2 的检查点 pointId 应与占位 prefab 完全一致");
            foreach (var p in v2.GetComponentsInChildren<InspectionPoint>(true))
                Assert.IsNotNull(p.GetComponent<Collider>(), $"{p.PointId} 缺少简化碰撞体");
            var seal = v2.GetComponentsInChildren<InspectionPoint>(true).First(p => p.PointId == "lease_seal");
            Assert.AreEqual("shell", seal.transform.parent.GetComponent<InspectionPoint>().PointId, "封条仍挂在盖板下，随盖板移动");
            Assert.Greater(v2.GetComponentsInChildren<BoxCollider>(true).Count(c => c.GetComponent<InspectionPoint>() == null), 5, "机身应有简化碰撞体");
            Assert.AreEqual(0, v2.GetComponentsInChildren<MeshCollider>(true).Length, "只用简化碰撞体");
        }

        [Test]
        public void CaseUsesV2WithUnchangedStepsAndText()
        {
            var c = AssetDatabase.LoadAssetAtPath<RepairCaseData>(CasePath);
            Assert.AreEqual(Load(V2Path), c.itemPrefab, "叙事案件应引用 v2 prefab");
            CollectionAssert.AreEqual(
                new[] { "remove_fastener_a", "remove_fastener_b", "open_housing", "test_drive", "replace_drive", "test_limiter", "test_board", "read_log" },
                c.repairSteps.Select(s => s.stepId).ToArray(), "维修步骤与顺序不变");
            CollectionAssert.AreEqual(new[] { "overwork_wear", "limiter_disabled", "remote_params" }, c.clues.Select(x => x.clueId).ToArray());
            StringAssert.Contains("远程", c.clues[2].text, "致伤主因仍是雇主远程修改参数");
        }

        [TestCase("fastener_a", ToolKind.Screwdriver)]
        [TestCase("fastener_b", ToolKind.Screwdriver)]
        [TestCase("shell", ToolKind.Pry)]
        [TestCase("force_limiter", ToolKind.Probe)]
        [TestCase("data_port", ToolKind.Plug)]
        public void EveryAnimatedStepHasAnAnchorOnItsPart(string pointId, ToolKind kind)
        {
            var point = Load(V2Path).GetComponentsInChildren<InspectionPoint>(true).First(p => p.PointId == pointId);
            var anchors = point.GetComponentsInChildren<ToolAnchor>(true).Where(a => a.GetComponentInParent<InspectionPoint>(true) == point).ToList();
            Assert.AreEqual(1, anchors.Count(a => a.Kind == kind), $"{pointId} 应有一个 {kind} 锚点");
            var a0 = anchors.First(a => a.Kind == kind);
            Assert.Less(Mathf.Abs(Vector3.Dot(a0.transform.up, a0.transform.forward)), 1e-3f, "锚点的工具轴线与手臂方向应互相垂直");
            // 接触点贴着零件：离零件自己的碰撞体不超过 3 mm
            var col = point.GetComponent<Collider>();
            var closest = col.ClosestPoint(a0.transform.position);
            Assert.Less(Vector3.Distance(closest, a0.transform.position), 0.003f, $"{pointId} 的锚点离零件太远");
        }

        [Test]
        public void SnapTargetsLieOnTheTray()
        {
            var v2 = Load(V2Path);
            foreach (var id in new[] { "shell", "fastener_a", "fastener_b" })
            {
                var part = v2.GetComponentsInChildren<RepairPart>(true).First(p => p.PartId == id);
                Assert.IsNotNull(part.SnapTarget, $"{id} 缺少吸附位置");
                Assert.Less(part.SnapTarget.localPosition.y, -0.06f, $"{id} 的吸附位置应在零件盘上（物品下方）");
            }
        }

        [Test]
        public void V2UsesUrpLitMaterialsWithTextures()
        {
            var mats = Load(V2Path).GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct().ToList();
            Assert.IsFalse(mats.Any(m => m == null), "有空材质槽");
            foreach (var m in mats) Assert.AreEqual("Universal Render Pipeline/Lit", m.shader.name, m.name);
            foreach (var name in new[] { "M_Hand_Decal", "M_Hand_Board", "M_Hand_BayLiner", "M_Hand_Enamel" })
            {
                var m = mats.FirstOrDefault(x => x.name == name);
                Assert.IsNotNull(m, $"缺少 {name}");
                Assert.IsNotNull(m.GetTexture("_BaseMap"), $"{name} 缺少贴图");
            }
            Assert.LessOrEqual(mats.Count, 30, "材质数量超出预算");
        }

        [Test]
        public void ToolRigsHaveTipAtOriginAndNoShadows()
        {
            var rigs = AssetDatabase.FindAssets("t:Prefab", new[] { RigDir }).Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Select(g => g.GetComponent<ToolRig>()).Where(r => r != null).ToList();
            CollectionAssert.AreEquivalent(new[] { ToolKind.Screwdriver, ToolKind.Pry, ToolKind.Probe, ToolKind.Plug }, rigs.Select(r => r.Kind).ToArray());
            foreach (var rig in rigs)
            {
                Assert.IsNotNull(rig.ToolModel);
                Assert.IsNotNull(rig.Glove);
                // 工具网格的最低点（沿本地 +Y）应在原点附近：作用端 = 原点
                float minY = float.MaxValue;
                foreach (var mf in rig.ToolModel.GetComponentsInChildren<MeshFilter>(true))
                {
                    var m = rig.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    foreach (var v in mf.sharedMesh.vertices) minY = Mathf.Min(minY, m.MultiplyPoint3x4(v).y);
                }
                Assert.Less(Mathf.Abs(minY), 0.0015f, $"{rig.Kind} 的作用端应在原点（实际最低点 {minY:0.0000}）");
                foreach (var r in rig.GetComponentsInChildren<Renderer>(true))
                    Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode, "工具不投影，避免阴影盖住线索");
            }
        }

        [Test]
        public void DefaultSceneHasNoToolAnimation()
        {
            string guid = AssetDatabase.FindAssets("RepairToolAnimator t:MonoScript").Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p) == "RepairToolAnimator").Select(AssetDatabase.AssetPathToGUID).First();
            StringAssert.DoesNotContain(guid, File.ReadAllText("Assets/BorderRepair/Scenes/RepairStation_Prototype.unity"));
            StringAssert.Contains(guid, File.ReadAllText("Assets/BorderRepair/Scenes/Narrative_WorkerHand.unity"));
        }
    }
}
