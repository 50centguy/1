using System.Collections.Generic;
using BorderRepair.Data;
using UnityEngine;

namespace BorderRepair.EditorTools
{
    /// <summary>
    /// 叙事竖切“工人义手”的初始内容（原创设定）。只在首次生成 .asset 时写入；之后可直接在 Inspector 中编辑。
    /// 节奏：顾客自述会突然攥紧 → 外壳过度磨损 → 限力器被关闭 → 日志显示参数由雇主远程修改 → 选择处理方式。
    /// 每条线索都由具体操作解锁：扫描外壳、用检测仪测限力传感器、从数据接口读取日志。
    /// </summary>
    internal static class WorkerHandCaseContent
    {
        // 稳定 ID：prefab 的 InspectionPoint / RepairPart、案件数据、测试共用
        public const string Shell = "shell";
        public const string LeaseSeal = "lease_seal";
        public const string FastenerA = "fastener_a";
        public const string FastenerB = "fastener_b";
        public const string Drive = "drive";
        public const string ForceLimiter = "force_limiter";
        public const string ControlBoard = "control_board";
        public const string DataPort = "data_port";

        public const string ClueWear = "overwork_wear";
        public const string ClueLimiter = "limiter_disabled";
        public const string ClueRemote = "remote_params";

        public static void Fill(RepairCaseData c, GameObject prefab)
        {
            c.caseId = "case_n01_worker_prosthetic";
            c.itemName = "工人义手";
            c.itemPrefab = prefab;
            c.scoring = CaseScoring.Narrative;
            c.customerName = "港区装卸工 罗亚";
            c.customerStatement = "这只手干活时会突然攥紧，攥住了就不松开。前天差点把搭档的手腕捏伤。你帮我修好就行，别声张。";
            c.intakeNote = "租赁义体。外壳上贴着租赁方的封条，拆开外壳会撕毁封条。";

            c.inspectionPoints = new List<InspectionPointInfo>
            {
                P(Shell, "外壳（背侧盖板）", "盖板边缘和指节护板磨得发亮，磨损量大约相当于标称寿命的三倍工时。", ClueWear),
                P(LeaseSeal, "租赁封条", "“北坡港务 · 租赁义体”封条，压住了一颗固定螺丝的上半边。卸下这颗螺丝就会撕开封条，租赁方会知道外壳被第三方打开过。"),
                P(FastenerA, "固定螺丝 A（封条下）", "内六角螺丝，下半边露在封条外面，可以用螺丝刀卸下。"),
                P(FastenerB, "固定螺丝 B", "内六角螺丝，可以用螺丝刀卸下。"),
                P(Drive, "传动机构", "指节传动齿轮磨损明显，齿面发亮。", null, "open_housing", "外壳还没打开，看不到里面。"),
                P(ForceLimiter, "限力传感器", "传感器外观完好，指示灯不亮。要知道它是否在工作，得用检测仪测一下。", null, "open_housing", "外壳还没打开，看不到里面。"),
                P(ControlBoard, "控制板", "原厂控制板，没有加装元件或改线的痕迹。", null, "open_housing", "外壳还没打开，看不到里面。"),
                P(DataPort, "数据接口", "手腕侧的维护接口，可以用检测仪读取控制日志。"),
            };

            // 叙事案件不做对错诊断：找齐线索后直接进入处理决定
            c.diagnosisOptions = new List<DiagnosisOption>();
            c.correctDiagnosisId = string.Empty;
            c.diagnosisConfirmedText = string.Empty;
            c.outcomes = new List<DecisionOutcome>();
            c.replacementPointId = string.Empty;
            c.estimatedRepairCost = 320;
            c.repairCostLimit = 0;

            c.clues = new List<ClueDefinition>
            {
                // 线索正文控制在 1–2 行，右侧记录区放得下；细节放在对应操作的结果文字里
                Clue(ClueWear, "外壳过度磨损", "磨损约为标称寿命的三倍工时。"),
                Clue(ClueLimiter, "限力器被关闭", "限力被软件关闭，阈值调到最大，攥紧后不会松开。"),
                Clue(ClueRemote, "参数由雇主远程修改", "每班开工前，港务运维终端远程写入：关闭限力，增益 140%。"),
            };

            c.repairSteps = new List<RepairStepDefinition>
            {
                Step("remove_fastener_a", RepairActionType.RemoveFastener, FastenerA, "卸下固定螺丝 A", RepairPartState.Removed,
                     "螺丝 A 卸下，放进零件盘。封条跟着撕开了——这件事瞒不住租赁方。",
                     sideEffectPoint: LeaseSeal, sideEffectState: RepairPartState.Torn),
                Step("remove_fastener_b", RepairActionType.RemoveFastener, FastenerB, "卸下固定螺丝 B", RepairPartState.Removed,
                     "螺丝 B 卸下，放进零件盘。"),
                Step("open_housing", RepairActionType.OpenHousing, Shell, "撬开背侧盖板", RepairPartState.Open,
                     "盖板撬开，放到零件盘上。传动机构、限力传感器和控制板露出来了。",
                     requiredSteps: new[] { "remove_fastener_a", "remove_fastener_b" },
                     blocked: "盖板还被螺丝固定着。先用螺丝刀卸下两颗固定螺丝。"),
                Step("test_drive", RepairActionType.ServiceModule, Drive, "检测传动机构", RepairPartState.Tested,
                     "检测：齿轮组磨损 62%，还在公差内，但这是长期超负荷的结果。可以再用检测仪换上新齿轮组。",
                     requiredSteps: new[] { "open_housing" }, blocked: "外壳还没打开。"),
                Step("replace_drive", RepairActionType.ServiceModule, Drive, "更换传动齿轮组", RepairPartState.Replaced,
                     "换上新的传动齿轮组。磨损的问题解决了，但它为什么会磨成这样，还没解决。",
                     requiredSteps: new[] { "test_drive" }),
                Step("test_limiter", RepairActionType.ServiceModule, ForceLimiter, "检测限力传感器", RepairPartState.Tested,
                     "检测：传感器正常，但限力功能被软件关闭了——阈值被设成了最大值。指示灯亮起红色：旁路。",
                     requiredSteps: new[] { "open_housing" }, blocked: "外壳还没打开。", unlocksClue: ClueLimiter),
                Step("test_board", RepairActionType.ServiceModule, ControlBoard, "检测控制板", RepairPartState.Tested,
                     "检测：固件是原厂签名，没有被刷机。参数的每一次改动都记在日志区，可以从数据接口导出。",
                     requiredSteps: new[] { "open_housing" }, blocked: "外壳还没打开。"),
                Step("read_log", RepairActionType.ServiceModule, DataPort, "读取控制日志", RepairPartState.Tested,
                     "导出日志，按“限力”筛选：每个班次开工前都有一条远程写入，来源“港务运维终端”，备注“三组装卸节拍提升”。",
                     requiredSteps: new[] { "test_limiter" },
                     blocked: "接口能连上，但日志有几千条。先弄清楚是哪个功能出了问题，再去查对应的记录。",
                     unlocksClue: ClueRemote),
            };

            c.endings = new List<NarrativeEnding>
            {
                Ending("restore_limits", "恢复出厂限力，锁定远程写入", "恢复出厂限力",
                    "你把限力阈值改回出厂值，关闭了远程写入。这只手不会再把人攥伤了。",
                    new[]
                    {
                        "罗亚：手安全了，但跟不上 140% 的节拍，可能被调岗或扣工分。",
                        "维修站：封条破了、远程写入被关，租赁方会追查是谁动的手。",
                        "港务：这只手不再执行运维终端的指令。",
                    }, ClueLimiter),
                Ending("keep_params", "只修磨损件，保留公司参数", "按原样修好",
                    "你只处理磨损，参数原样保留。罗亚明天能照常上班，你也收到了修理费。",
                    new[]
                    {
                        "罗亚：保住了工作，但限力仍然关着，下一次攥紧仍可能伤人。",
                        "维修站：你知道问题在哪里，却把它原样交了回去。",
                        "港务：没人知道这只手被查过——除了那张撕开的封条。",
                    }),
                Ending("give_log", "不改参数，把日志副本交给罗亚", "把选择交还给顾客",
                    "你没有改参数，只把导出的日志副本和一份检测记录交给罗亚。要不要拿去找工会或劳动监察，由罗亚决定。",
                    new[]
                    {
                        "罗亚：手里有了证据，但做决定之前，这只手依然危险。",
                        "维修站：交出设备数据可能违反租赁合同，港务可能不再送修。",
                        "港务：会不会被追责，取决于罗亚下一步怎么做。",
                    }, ClueRemote),
            };
        }

        static InspectionPointInfo P(string id, string name, string finding, string clue = null, string requiresStep = null, string blocked = null) =>
            new InspectionPointInfo
            {
                pointId = id, displayName = name, finding = finding, requiredForDiagnosis = false,
                unlocksClueId = clue ?? string.Empty, requiresStepId = requiresStep ?? string.Empty, blockedFinding = blocked ?? string.Empty,
            };

        static ClueDefinition Clue(string id, string title, string text) =>
            new ClueDefinition { clueId = id, title = title, text = text, requiredForDecision = true };

        static RepairStepDefinition Step(string id, RepairActionType action, string target, string label, RepairPartState result, string text,
                                         string[] requiredSteps = null, string blocked = null, string unlocksClue = null,
                                         string sideEffectPoint = null, RepairPartState sideEffectState = RepairPartState.Installed) =>
            new RepairStepDefinition
            {
                stepId = id, action = action, targetPointId = target, label = label, resultState = result, resultText = text,
                requiredSteps = new List<string>(requiredSteps ?? new string[0]), requiredClues = new List<string>(),
                blockedText = blocked ?? string.Empty, unlocksClueId = unlocksClue ?? string.Empty,
                sideEffectPointId = sideEffectPoint ?? string.Empty, sideEffectState = sideEffectState,
            };

        static NarrativeEnding Ending(string id, string label, string title, string narrative, string[] costs, params string[] requiredClues) =>
            new NarrativeEnding
            {
                endingId = id, label = label, resultTitle = title, narrative = narrative,
                costs = new List<string>(costs), requiredClues = new List<string>(requiredClues),
            };
    }
}
