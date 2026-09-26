using System.Collections.Generic;
using BorderRepair.Data;
using UnityEngine;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 第一阶段三个测试案例的初始内容，只在首次生成 .asset 时写入。
    /// 之后直接在 Inspector 里编辑 Assets/BorderRepair/Data/Cases 下的资产即可，不必改这里。
    /// </summary>
    internal static class PrototypeCaseContent
    {
        public static void FillCommunicator(RepairCaseData c, GameObject prefab)
        {
            c.caseId = "case_01_communicator";
            c.itemName = "通讯器";
            c.itemPrefab = prefab;
            c.customerName = "巡线员 老周";
            c.customerStatement = "前天巡逻时它从车上摔下去了。还能开机，就是一直收不到信号。明早还要出勤，麻烦尽快。";
            c.intakeNote = "外观有摔落磕痕；顾客可接受维修费上限 600。";
            c.inspectionPoints = new List<InspectionPointInfo>
            {
                P("antenna", "天线模块", "天线根部断裂，内部馈线外露。天线模块是标准可更换件。", true),
                P("battery", "电池仓", "电池电压 3.9V，触点干净，正常。", false),
                P("screen", "显示屏", "显示正常，无坏点，排线牢固。", false),
            };
            c.diagnosisOptions = new List<DiagnosisOption>
            {
                D("battery_aging", "电池老化，导致供电不足", "电池读数正常；而且顾客说能开机，问题在信号。"),
                D("antenna_broken", "天线模块断裂，信号无法收发", ""),
                D("mainboard_burnt", "主板烧毁", "扫描没有发现主板异常。不要过度诊断，顾客付的是修理费。"),
            };
            c.correctDiagnosisId = "antenna_broken";
            c.diagnosisConfirmedText = "诊断确认：天线模块断裂。标准件有库存。";
            c.estimatedRepairCost = 180;
            c.repairCostLimit = 600;
            c.replacementPointId = "antenna";
            c.outcomes = new List<DecisionOutcome>
            {
                O(RepairDecision.Repair, true, "换件维修完成",
                  "只有天线模块这一个标准件损坏。更换后信号恢复，费用 180，在顾客上限 600 以内，维修是最合理的选择。", true),
                O(RepairDecision.RecommendReplacement, false, "让顾客多花了冤枉钱",
                  "只坏了一个便宜的标准件，却建议整机更换。顾客多花钱，维修站也失了信誉。"),
                O(RepairDecision.Refuse, false, "错过了一单正当生意",
                  "物品来源正常，故障明确，修起来也简单，没有理由拒修。"),
            };
        }

        public static void FillNavBeacon(RepairCaseData c, GameObject prefab)
        {
            c.caseId = "case_02_nav_beacon";
            c.itemName = "导航信标";
            c.itemPrefab = prefab;
            c.customerName = "货运司机 阿坎";
            c.customerStatement = "信标最近好像有点毛病，你帮我恢复一下出厂设置就行，别的不用多问。钱不是问题。";
            c.intakeNote = "外观完好。按规定，边境导航设备的检修封签必须完整。";
            c.inspectionPoints = new List<InspectionPointInfo>
            {
                P("seal", "检修封签", "封签被撕开后又贴了回去，边缘有残胶；固定螺丝上有非原厂工具留下的新鲜划痕。", true),
                P("port", "调试接口", "接口里有新焊上的跳线和一块未登记的小芯片，发射频率被改写。", true),
                P("lamp", "灯组", "亮度和闪烁频率正常。", false),
                P("mast", "外置天线", "天线完好，连接牢固。", false),
            };
            c.diagnosisOptions = new List<DiagnosisOption>
            {
                D("normal_wear", "长期使用造成的正常磨损", "正常磨损不会留下新焊点，也不会有重新贴过的封签。"),
                D("tampered", "频率模块被人为改装（私自改频）", ""),
                D("lamp_aging", "灯组老化，亮度不足", "灯组读数正常，顾客也没有提到灯的问题。"),
            };
            c.correctDiagnosisId = "tampered";
            c.diagnosisConfirmedText = "诊断确认：信标被人为改频，封签和焊点都是证据。";
            c.estimatedRepairCost = 150;
            c.repairCostLimit = 0;
            c.replacementPointId = "";
            c.outcomes = new List<DecisionOutcome>
            {
                O(RepairDecision.Refuse, true, "拒收并登记",
                  "信标被人为改频，重贴的封签和新焊点都是证据。维修站不能替人把改装过的导航设备“洗白”，应当拒收并登记。"),
                O(RepairDecision.Repair, false, "帮人抹掉了改装痕迹",
                  "“恢复出厂设置”等于销毁改装证据。技术上能修，但这单本来就不该接。"),
                O(RepairDecision.RecommendReplacement, false, "回避了真正的问题",
                  "设备本身没坏。建议更换解决不了改装问题，改装痕迹也没有登记。"),
            };
        }

        public static void FillSalvageDrone(RepairCaseData c, GameObject prefab)
        {
            c.caseId = "case_03_salvage_drone";
            c.itemName = "回收无人机";
            c.itemPrefab = prefab;
            c.customerName = "回收站 小梅";
            c.customerStatement = "这是从河滩上捡回来的无人机。老板说能修就修，但修理费超过 1200 就不值了，他要拿去当二手卖。";
            c.intakeNote = "顾客可接受维修费上限 1200。";
            c.inspectionPoints = new List<InspectionPointInfo>
            {
                P("motor_fl", "左前电机", "电机线圈烧毁，需要连同电调一起更换。", true),
                P("mainboard", "主控板舱", "主控板大面积进水腐蚀，多处焊点氧化，需要整板更换。", true),
                P("rotor", "桨叶", "桨叶有轻微磨损，还能继续用。", false),
                P("camera", "云台相机", "云台和镜头正常。", false),
            };
            c.diagnosisOptions = new List<DiagnosisOption>
            {
                D("single_motor", "只有单个电机故障，换电机就行", "电机只是其中一处。主控板腐蚀同样必须处理。"),
                D("multi_damage", "主控板腐蚀 + 电机烧毁，多处重大损坏", ""),
                D("bent_rotor", "只是桨叶弯曲", "桨叶只有轻微磨损，不是主要故障。"),
            };
            c.correctDiagnosisId = "multi_damage";
            c.diagnosisConfirmedText = "诊断确认：需要更换主控板和电机，技术上可以修。";
            c.estimatedRepairCost = 1850;
            c.repairCostLimit = 1200;
            c.replacementPointId = "";
            c.outcomes = new List<DecisionOutcome>
            {
                O(RepairDecision.RecommendReplacement, true, "建议更换或拆件回收",
                  "技术上可以修：换掉主控板和电机就能恢复。但预估费用 1850，超过了顾客的上限 1200。修了不划算，应当建议更换或拆件回收。"),
                O(RepairDecision.Repair, false, "修好了，但账单超了",
                  "维修费 1850 超过了顾客给的上限 1200，顾客不会接受这张账单。"),
                O(RepairDecision.Refuse, false, "拒绝的理由不成立",
                  "设备来源正常，故障也能修，只是不划算。应该给出更换建议，而不是直接拒绝。"),
            };
        }

        static InspectionPointInfo P(string id, string name, string finding, bool required) =>
            new InspectionPointInfo { pointId = id, displayName = name, finding = finding, requiredForDiagnosis = required };

        static DiagnosisOption D(string id, string label, string wrong) =>
            new DiagnosisOption { optionId = id, label = label, wrongFeedback = wrong };

        static DecisionOutcome O(RepairDecision d, bool correct, string title, string reason, bool replace = false) =>
            new DecisionOutcome { decision = d, isCorrect = correct, resultTitle = title, reason = reason, performsPartReplacement = replace };
    }
}
