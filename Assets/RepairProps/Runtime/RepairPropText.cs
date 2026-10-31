using System.Collections.Generic;

namespace BorderRepair.RepairProps
{
    public static class RepairPropText
    {
        public static string Item(RepairPropKind kind)
        {
            switch (kind)
            {
                case RepairPropKind.Receiver: return "信号接收器";
                case RepairPropKind.Cartridge: return "记忆卡匣";
                case RepairPropKind.Power: return "维修电源";
                default: return "赛博御守";
            }
        }

        static readonly Dictionary<string, string> parts = new Dictionary<string, string>
        {
            { "RX_Mainboard", "原解码与记录主板" }, { "RX_BatteryCassette", "密封电池盒" },
            { "RX_RearCover", "后盖" }, { "RX_BatteryLead", "电池连接线" }, { "RX_RFCoax", "射频同轴线" },
            { "RX_RF_Daughterboard", "射频接口小板" }, { "RX_TestLead", "测试连接线" },
            { "RX_ScreenState_Noise", "噪声显示" }, { "RX_ScreenState_Locked", "锁频显示" },
            { "RX_LightCurtain_Noise", "噪声光幕" }, { "RX_LightCurtain_Locked", "锁频光幕" },
            { "RX_TopStrip_Hinge", "顶部活动护条" }, { "MC_TopCover", "卡匣顶盖" },
            { "MC_DataCore", "原数据核心" }, { "MC_BoardJumper", "板间连接排线" },
            { "MC_InterfaceBoard", "卡匣接口板" }, { "MC_StatusLED_Fail", "识别异常指示" },
            { "MC_StatusLED_OK", "识别正常指示" }, { "PS_LeftServiceCover", "维修侧盖" },
            { "PS_SealedBatteryCassette", "原密封电池盒" }, { "PS_FrontElectronics", "前部控制与接口板" },
            { "PS_Fan", "散热风扇" }, { "PS_BatteryLead", "电池服务连接线" },
            { "PS_BatteryServicePlug", "电池服务插头" }, { "PS_TrayHarness", "输出托盘线束" },
            { "PS_TrayHarnessPlug", "输出托盘线束插头" }, { "PS_OutputTray", "输出托盘" },
            { "PS_OutputModule", "输出模块" }, { "OM_RearCover", "御守后盖" },
            { "OM_MemoryCore", "原记忆核心" }, { "OM_MainBoard", "原控制主板" },
            { "OM_Coil", "感应线圈" }, { "OM_PowerCell", "密封微型供电盒" },
            { "OM_PowerLead", "微型供电连接线" }, { "OM_PowerPlug", "微型供电插头" },
            { "OM_WakeFlex", "唤醒模块排线" }, { "OM_WakePlug", "唤醒模块插头" },
            { "OM_WakeModule", "唤醒按键模块" }
        };

        public static string Part(string name, RepairPropKind kind, string rootName)
        {
            if (name == rootName) return Item(kind);
            if (parts.TryGetValue(name, out var exact)) return exact;
            foreach (var suffix in new[] { "_Connected", "_Isolated", "_Released", "_Fault", "_New" })
            {
                if (!name.EndsWith(suffix, System.StringComparison.Ordinal)) continue;
                string stem = name.Substring(0, name.Length - suffix.Length);
                if (!parts.TryGetValue(stem, out var label)) break;
                return label + (suffix == "_Connected" ? "（已连接）" : suffix == "_Isolated" ? "（已隔离）"
                    : suffix == "_Released" ? "（已释放）" : suffix == "_Fault" ? (kind == RepairPropKind.Receiver ? "（原件）" : "（故障件）") : "（替换件）");
            }
            if (name.Contains("Screw_"))
            {
                string number = name.Substring(name.LastIndexOf('_') + 1);
                return (name.Contains("CoverScrew") ? "盖板螺钉" : name.Contains("TrayScrew") ? "托盘固定螺钉" : "模块固定螺钉") + number;
            }
            return "部件";
        }
    }
}
