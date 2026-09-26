using System.Collections.Generic;
using BorderRepair.Data;
using BorderRepair.Inspection;
using NUnit.Framework;
using UnityEditor;

namespace BorderRepair.Tests
{
    /// <summary>检查由 Border Repair 菜单生成的真实案例资产：数据完整、与 prefab 的检查点一致、三种判断各对应一个案例。</summary>
    public class PrototypeAssetTests
    {
        const string ShiftPath = "Assets/BorderRepair/Data/Shift_Prototype.asset";
        const string ScenePath = "Assets/BorderRepair/Scenes/RepairStation_Prototype.unity";

        static RepairShiftData LoadShift()
        {
            var shift = AssetDatabase.LoadAssetAtPath<RepairShiftData>(ShiftPath);
            Assert.IsNotNull(shift, "未找到营业数据，请先运行 Border Repair > Build Prototype");
            return shift;
        }

        [Test]
        public void ShiftHasThreeValidCases()
        {
            var shift = LoadShift();
            Assert.AreEqual(3, shift.cases.Count);
            var errors = new List<string>();
            foreach (var c in shift.cases)
            {
                Assert.IsNotNull(c);
                c.Validate(errors);
            }
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void EachCaseCoversADifferentJudgement()
        {
            var shift = LoadShift();
            var expected = new[] { RepairDecision.Repair, RepairDecision.Refuse, RepairDecision.RecommendReplacement };
            for (int i = 0; i < expected.Length; i++)
            {
                var correct = shift.cases[i].outcomes.Find(o => o.isCorrect);
                Assert.AreEqual(expected[i], correct.decision, shift.cases[i].caseId);
            }

            var communicator = shift.cases[0];
            Assert.IsTrue(communicator.FindOutcome(RepairDecision.Repair).performsPartReplacement, "通讯器正确维修应包含换件");

            var drone = shift.cases[2];
            Assert.Greater(drone.estimatedRepairCost, drone.repairCostLimit, "无人机维修成本应超过上限");
        }

        [Test]
        public void CaseInspectionPointsExistOnPrefabsWithColliders()
        {
            foreach (var c in LoadShift().cases)
            {
                var prefabPoints = new Dictionary<string, InspectionPoint>();
                foreach (var p in c.itemPrefab.GetComponentsInChildren<InspectionPoint>(true))
                {
                    Assert.IsFalse(prefabPoints.ContainsKey(p.PointId), $"{c.caseId}: prefab 中 pointId 重复 {p.PointId}");
                    prefabPoints[p.PointId] = p;
                }
                foreach (var info in c.inspectionPoints)
                {
                    Assert.IsTrue(prefabPoints.TryGetValue(info.pointId, out var point), $"{c.caseId}: prefab 缺少检查点 {info.pointId}");
                    Assert.IsNotEmpty(point.GetComponentsInChildren<UnityEngine.Collider>(), $"{c.caseId}: 检查点 {info.pointId} 没有碰撞体，无法点击扫描");
                }
            }
        }

        [Test]
        public void PrototypeSceneIsInBuildSettings()
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), "场景未生成");
            bool found = false;
            foreach (var s in EditorBuildSettings.scenes) found |= s.path == ScenePath && s.enabled;
            Assert.IsTrue(found, "场景未加入 Build Settings");
        }
    }
}
