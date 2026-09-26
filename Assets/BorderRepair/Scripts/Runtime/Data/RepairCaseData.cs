using System;
using System.Collections.Generic;
using UnityEngine;

namespace BorderRepair.Data
{
    /// <summary>物品上一个可扫描部位的文本信息。pointId 必须与物品 prefab 中 InspectionPoint 的 pointId 一致。</summary>
    [Serializable]
    public class InspectionPointInfo
    {
        public string pointId;
        public string displayName;
        [TextArea(2, 4)] public string finding;
        [Tooltip("勾选后：必须扫描到该部位才能进入诊断，并在记录中标为异常。")]
        public bool requiredForDiagnosis;

        [Header("叙事案件（可选）")]
        [Tooltip("扫描到该部位时解锁的线索 clueId。")]
        public string unlocksClueId;
        [Tooltip("必须先完成这个维修步骤（stepId）才能扫描到该部位，例如外壳里面的零件。")]
        public string requiresStepId;
        [TextArea(1, 3)] public string blockedFinding;
    }

    /// <summary>案件的计分方式：原有案件按正确/错误判断；叙事案件有多个各有代价的结局，不计入“判断正确”。</summary>
    public enum CaseScoring { Judged = 0, Narrative = 1 }

    /// <summary>三种可复用的维修动作。鼠标工具和以后的 VR 手柄都只通过这三种动作操作零件。</summary>
    public enum RepairActionType { RemoveFastener = 0, OpenHousing = 1, ServiceModule = 2 }

    /// <summary>零件的明确状态；Removed / Open 对应零件移动到吸附位置。</summary>
    public enum RepairPartState { Installed = 0, Removed = 1, Open = 2, Tested = 3, Replaced = 4, Torn = 5 }

    public static class RepairActionText
    {
        public static string Label(RepairActionType action)
        {
            switch (action)
            {
                case RepairActionType.RemoveFastener: return "卸下固定件";
                case RepairActionType.OpenHousing: return "打开外壳";
                case RepairActionType.ServiceModule: return "检测 / 更换模块";
                default: return action.ToString();
            }
        }
    }

    [Serializable]
    public class ClueDefinition
    {
        public string clueId;
        public string title;
        [TextArea(2, 5)] public string text;
        [Tooltip("勾选后：找齐这些线索才能做出处理决定。")]
        public bool requiredForDecision = true;
    }

    /// <summary>一个维修步骤：对某个部位执行某种动作。前置步骤和线索都满足时才能执行。</summary>
    [Serializable]
    public class RepairStepDefinition
    {
        public string stepId;
        public RepairActionType action;
        public string targetPointId;
        public string label;
        public List<string> requiredSteps = new List<string>();
        public List<string> requiredClues = new List<string>();
        public RepairPartState resultState;
        [Tooltip("执行后顺带改变的另一个部位（例如卸螺丝时撕开封条），可空。")]
        public string sideEffectPointId;
        public RepairPartState sideEffectState;
        [TextArea(2, 5)] public string resultText;
        [TextArea(1, 3)] public string blockedText;
        public string unlocksClueId;
    }

    /// <summary>叙事案件的结局：没有对错，只有各自的代价。</summary>
    [Serializable]
    public class NarrativeEnding
    {
        public string endingId;
        public string label;
        public string resultTitle;
        [TextArea(3, 8)] public string narrative;
        public List<string> costs = new List<string>();
        public List<string> requiredClues = new List<string>();
    }

    [Serializable]
    public class DiagnosisOption
    {
        public string optionId;
        public string label;
        [TextArea(2, 4)] [Tooltip("选错此项时给玩家的反馈。")]
        public string wrongFeedback;
    }

    [Serializable]
    public class DecisionOutcome
    {
        public RepairDecision decision;
        public bool isCorrect;
        public string resultTitle;
        [TextArea(3, 6)] public string reason;
        [Tooltip("选择此决定后，是否把 replacementPointId 指向的部件换成修好的外观。")]
        public bool performsPartReplacement;
    }

    /// <summary>
    /// 一件送修物的全部游戏内容。替换 prefab、顾客文本、检查点、诊断和结果只需要改这个资产，不需要改主流程代码。
    /// 注意：这是游戏内的“维修判断”数据，与开发阶段的 AI 资产评价记录（Docs/AssetEvaluation）无关。
    /// </summary>
    [CreateAssetMenu(menuName = "Border Repair/Repair Case", fileName = "RepairCase")]
    public class RepairCaseData : ScriptableObject
    {
        [Header("物品")]
        public string caseId;
        public string itemName;
        public GameObject itemPrefab;

        [Header("顾客")]
        public string customerName;
        [TextArea(3, 6)] public string customerStatement;
        [TextArea(2, 4)] public string intakeNote;

        [Header("检查")]
        public List<InspectionPointInfo> inspectionPoints = new List<InspectionPointInfo>();

        [Header("诊断")]
        public List<DiagnosisOption> diagnosisOptions = new List<DiagnosisOption>();
        public string correctDiagnosisId;
        [TextArea(2, 4)] public string diagnosisConfirmedText;

        [Header("成本（0 表示不显示上限）")]
        public int estimatedRepairCost;
        public int repairCostLimit;

        [Header("决定与结果")]
        public List<DecisionOutcome> outcomes = new List<DecisionOutcome>();
        [Tooltip("换件维修时要替换外观的部位 pointId，可留空。")]
        public string replacementPointId;

        [Header("叙事案件（可选；原有判断类案件保持默认值即可）")]
        public CaseScoring scoring = CaseScoring.Judged;
        public List<ClueDefinition> clues = new List<ClueDefinition>();
        public List<RepairStepDefinition> repairSteps = new List<RepairStepDefinition>();
        public List<NarrativeEnding> endings = new List<NarrativeEnding>();

        public bool IsNarrative => scoring == CaseScoring.Narrative;
        public bool HasRepairSteps => repairSteps != null && repairSteps.Count > 0;

        public ClueDefinition FindClue(string clueId)
        {
            if (string.IsNullOrEmpty(clueId) || clues == null) return null;
            return clues.Find(c => c != null && c.clueId == clueId);
        }

        public RepairStepDefinition FindStep(string stepId)
        {
            if (string.IsNullOrEmpty(stepId) || repairSteps == null) return null;
            return repairSteps.Find(s => s != null && s.stepId == stepId);
        }

        public NarrativeEnding FindEnding(string endingId)
        {
            if (string.IsNullOrEmpty(endingId) || endings == null) return null;
            return endings.Find(e => e != null && e.endingId == endingId);
        }

        public int RequiredClueCount
        {
            get
            {
                int count = 0;
                if (clues != null)
                    foreach (var c in clues)
                        if (c != null && c.requiredForDecision) count++;
                return count;
            }
        }

        public InspectionPointInfo FindPoint(string pointId)
        {
            if (string.IsNullOrEmpty(pointId)) return null;
            return inspectionPoints.Find(p => p != null && p.pointId == pointId);
        }

        public DiagnosisOption FindDiagnosis(string optionId)
        {
            if (string.IsNullOrEmpty(optionId)) return null;
            return diagnosisOptions.Find(o => o != null && o.optionId == optionId);
        }

        public DecisionOutcome FindOutcome(RepairDecision decision)
        {
            return outcomes.Find(o => o != null && o.decision == decision);
        }

        public DiagnosisOption CorrectDiagnosis => FindDiagnosis(correctDiagnosisId);

        public int RequiredPointCount
        {
            get
            {
                int count = 0;
                foreach (var p in inspectionPoints)
                    if (p != null && p.requiredForDiagnosis) count++;
                return count;
            }
        }

        /// <summary>检查数据是否能让流程正常走完；返回 false 时 errors 中列出原因。</summary>
        public bool Validate(List<string> errors)
        {
            int before = errors.Count;
            string label = string.IsNullOrEmpty(caseId) ? name : caseId;

            if (string.IsNullOrEmpty(caseId)) errors.Add($"{label}: caseId 为空");
            if (itemPrefab == null) errors.Add($"{label}: 未指定 itemPrefab");

            var ids = new HashSet<string>();
            foreach (var p in inspectionPoints)
            {
                if (p == null || string.IsNullOrEmpty(p.pointId)) { errors.Add($"{label}: 有检查点缺少 pointId"); continue; }
                if (!ids.Add(p.pointId)) errors.Add($"{label}: 检查点 pointId 重复: {p.pointId}");
            }

            if (IsNarrative)
            {
                ValidateNarrative(label, ids, errors);
                return errors.Count == before;
            }

            if (RequiredPointCount == 0) errors.Add($"{label}: 至少需要一个 requiredForDiagnosis 检查点，否则可以跳过检查");

            if (diagnosisOptions.Count < 2) errors.Add($"{label}: 诊断选项少于 2 个");
            if (CorrectDiagnosis == null) errors.Add($"{label}: correctDiagnosisId '{correctDiagnosisId}' 不在诊断选项中");

            int correctCount = 0;
            foreach (RepairDecision d in Enum.GetValues(typeof(RepairDecision)))
            {
                var o = FindOutcome(d);
                if (o == null) { errors.Add($"{label}: 缺少决定 {d} 的结果"); continue; }
                if (o.isCorrect) correctCount++;
                if (o.performsPartReplacement && FindPoint(replacementPointId) == null)
                    errors.Add($"{label}: 决定 {d} 需要换件，但 replacementPointId '{replacementPointId}' 不是有效检查点");
            }
            if (correctCount != 1) errors.Add($"{label}: 正确决定应当恰好 1 个，当前 {correctCount} 个");

            return errors.Count == before;
        }

        /// <summary>叙事案件的校验：线索可被某个操作解锁、步骤引用有效、至少两个结局。不要求 isCorrect。</summary>
        void ValidateNarrative(string label, HashSet<string> pointIds, List<string> errors)
        {
            var clueIds = new HashSet<string>();
            foreach (var c in clues)
            {
                if (c == null || string.IsNullOrEmpty(c.clueId)) { errors.Add($"{label}: 有线索缺少 clueId"); continue; }
                if (!clueIds.Add(c.clueId)) errors.Add($"{label}: clueId 重复: {c.clueId}");
            }
            if (RequiredClueCount == 0) errors.Add($"{label}: 叙事案件至少需要一条 requiredForDecision 线索");

            var stepIds = new HashSet<string>();
            foreach (var s in repairSteps)
            {
                if (s == null || string.IsNullOrEmpty(s.stepId)) { errors.Add($"{label}: 有步骤缺少 stepId"); continue; }
                if (!stepIds.Add(s.stepId)) errors.Add($"{label}: stepId 重复: {s.stepId}");
            }

            var unlockable = new HashSet<string>();
            foreach (var p in inspectionPoints)
            {
                if (p == null) continue;
                if (!string.IsNullOrEmpty(p.unlocksClueId))
                {
                    if (!clueIds.Contains(p.unlocksClueId)) errors.Add($"{label}: 检查点 {p.pointId} 解锁的线索 {p.unlocksClueId} 不存在");
                    unlockable.Add(p.unlocksClueId);
                }
                if (!string.IsNullOrEmpty(p.requiresStepId) && !stepIds.Contains(p.requiresStepId))
                    errors.Add($"{label}: 检查点 {p.pointId} 依赖的步骤 {p.requiresStepId} 不存在");
            }
            foreach (var s in repairSteps)
            {
                if (s == null) continue;
                if (!pointIds.Contains(s.targetPointId)) errors.Add($"{label}: 步骤 {s.stepId} 的目标部位 {s.targetPointId} 不存在");
                if (!string.IsNullOrEmpty(s.sideEffectPointId) && !pointIds.Contains(s.sideEffectPointId))
                    errors.Add($"{label}: 步骤 {s.stepId} 的连带部位 {s.sideEffectPointId} 不存在");
                foreach (var r in s.requiredSteps)
                    if (!stepIds.Contains(r)) errors.Add($"{label}: 步骤 {s.stepId} 的前置步骤 {r} 不存在");
                foreach (var r in s.requiredClues)
                    if (!clueIds.Contains(r)) errors.Add($"{label}: 步骤 {s.stepId} 的前置线索 {r} 不存在");
                if (!string.IsNullOrEmpty(s.unlocksClueId))
                {
                    if (!clueIds.Contains(s.unlocksClueId)) errors.Add($"{label}: 步骤 {s.stepId} 解锁的线索 {s.unlocksClueId} 不存在");
                    unlockable.Add(s.unlocksClueId);
                }
            }
            foreach (var c in clues)
                if (c != null && c.requiredForDecision && !unlockable.Contains(c.clueId))
                    errors.Add($"{label}: 线索 {c.clueId} 没有任何操作可以解锁");

            var endingIds = new HashSet<string>();
            foreach (var e in endings)
            {
                if (e == null || string.IsNullOrEmpty(e.endingId)) { errors.Add($"{label}: 有结局缺少 endingId"); continue; }
                if (!endingIds.Add(e.endingId)) errors.Add($"{label}: endingId 重复: {e.endingId}");
                foreach (var r in e.requiredClues)
                    if (!clueIds.Contains(r)) errors.Add($"{label}: 结局 {e.endingId} 的前置线索 {r} 不存在");
            }
            if (endingIds.Count < 2) errors.Add($"{label}: 叙事案件至少需要两个结局");
        }
    }
}
