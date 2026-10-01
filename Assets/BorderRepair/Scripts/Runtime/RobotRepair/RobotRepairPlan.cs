using System;
using System.Collections.Generic;

namespace BorderRepair.RobotRepair
{
    public sealed class RobotRepairStep
    {
        public readonly string Id;
        public readonly string Label;
        public readonly string ModelAnchor;
        public readonly string[] RequiredModelAnchors;

        public RobotRepairStep(string id, string label, string modelAnchor, params string[] additionalAnchors)
        {
            Id = id;
            Label = label;
            ModelAnchor = modelAnchor;
            RequiredModelAnchors = new string[1 + (additionalAnchors?.Length ?? 0)];
            RequiredModelAnchors[0] = modelAnchor;
            if (additionalAnchors != null) Array.Copy(additionalAnchors, 0, RequiredModelAnchors, 1, additionalAnchors.Length);
        }
    }

    public sealed class RobotRepairPlan
    {
        public readonly string CaseId;
        public readonly string[] InspectionAnchors;
        public readonly RobotRepairStep[] RemovalSteps;

        public RobotRepairPlan(string caseId, string[] inspectionAnchors, RobotRepairStep[] removalSteps)
        {
            if (string.IsNullOrEmpty(caseId)) throw new ArgumentException("Case ID is required", nameof(caseId));
            if (inspectionAnchors == null || inspectionAnchors.Length == 0) throw new ArgumentException("Inspection anchors are required", nameof(inspectionAnchors));
            if (removalSteps == null || removalSteps.Length == 0) throw new ArgumentException("Removal steps are required", nameof(removalSteps));
            var ids = new HashSet<string>();
            foreach (var anchor in inspectionAnchors)
                if (string.IsNullOrEmpty(anchor) || !ids.Add(anchor)) throw new ArgumentException("Inspection anchors must be unique and nonempty", nameof(inspectionAnchors));
            ids.Clear();
            foreach (var step in removalSteps)
                if (step == null || string.IsNullOrEmpty(step.Id) || !ids.Add(step.Id))
                    throw new ArgumentException("Removal steps must have unique IDs and model anchors", nameof(removalSteps));
                else
                    foreach (var anchor in step.RequiredModelAnchors)
                        if (string.IsNullOrEmpty(anchor)) throw new ArgumentException("Model anchors must be nonempty", nameof(removalSteps));
            CaseId = caseId;
            InspectionAnchors = (string[])inspectionAnchors.Clone();
            RemovalSteps = (RobotRepairStep[])removalSteps.Clone();
        }

        // IDs and order follow the V4 art handoff's assembly_check.json main path and 10L branch.
        // Bench-only branches for unrelated body modules and the right engine are excluded.
        public static RobotRepairPlan SixLeftEngine()
        {
            return new RobotRepairPlan("case_06_left_engine_imbalance",
                new[] { "Engine_UpperCover_L", "Engine_IntakeGuard_L", "ExternalCable_L_PlugBody" },
                new[]
                {
                    new RobotRepairStep("1", "卸四颗螺钉，取下顶盖", "TopCover", "TopCover_Screw_1", "TopCover_Screw_2", "TopCover_Screw_3", "TopCover_Screw_4"),
                    new RobotRepairStep("2", "拔下屏幕驱动插头", "Harness_Screen_PlugB"),
                    new RobotRepairStep("3", "上提并取下前框", "FrontBezel", "FrontBezel_Screw_1", "FrontBezel_Screw_2"),
                    new RobotRepairStep("4", "取下屏幕保护玻璃", "ScreenGlass"),
                    new RobotRepairStep("5", "抽出屏幕盒", "ScreenCassette_Box", "ScreenCassette_Screw_L", "ScreenCassette_Screw_R"),
                    new RobotRepairStep("6", "断开主板线束", "Harness_Engine_L_PlugA", "Harness_Engine_R_PlugA", "Harness_Power_PlugA", "Harness_Service_PlugA", "Harness_Screen_PlugA"),
                    new RobotRepairStep("7", "抽出主板托板", "PCB_Sled", "PCB_Sled_Screw_1", "PCB_Sled_Screw_2"),
                    new RobotRepairStep("8", "放下电源托盘", "PowerTray", "PowerTray_Screw_1", "PowerTray_Screw_2", "PowerTray_Screw_3", "PowerTray_Screw_4"),
                    new RobotRepairStep("9", "抽出背部维修盒", "RearServiceCassette", "RearService_Bolt_1", "RearService_Bolt_2"),
                    new RobotRepairStep("10L", "断开外露线缆，取下左引擎", "Engine_MountBase_L", "ExternalCable_L_PlugBody", "AuxCable_L_PlugBody"),
                    new RobotRepairStep("10La", "扳开锁扣，取下引擎上盖", "Engine_UpperCover_L", "Engine_CoverLatch_Outer_L", "Engine_CoverLatch_Rear_L"),
                    new RobotRepairStep("10Lb", "卸风扇螺母和风扇", "Engine_Fan_L", "Engine_FanNut_L"),
                    new RobotRepairStep("10Lc", "卸支架螺钉，提出马达芯", "Engine_MotorHousing_L", "Engine_MotorBracket_Screw_L_1", "Engine_MotorBracket_Screw_L_2", "Engine_MotorBracket_Screw_L_3"),
                });
        }
    }
}
