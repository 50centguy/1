using System.Collections.Generic;

namespace BorderRepair.RepairProps
{
    // What each of the night's four props carries besides its fault, told only through what a repairer may see:
    // marks on parts (inspection findings), metadata a read-only check reports, and what the owner says. Private
    // contents are never opened. Story notes: Docs/Integration/RepairPropsCyberBench/STORIES.md.
    // One thread runs through all four: someone, or something, answering in place of a person who is gone.
    public static class RepairPropStories
    {
        public static string Backstory(RepairPropKind kind)
        {
            switch (kind)
            {
                case RepairPropKind.Receiver:
                    return "凛和哥哥都是三号管廊的夜班巡检员，哥哥的呼号是「巡检 07-B」。去年七号泊位底下漏压，他没有上来。这台是他的值班机，里面存着兄妹约好的呼叫时段：每晚 02:10。";
                case RepairPropKind.Cartridge:
                    return "源从潮汐回收船收来一批旧义体，这只卡匣是从其中一只装卸义手里拆出来的，里面是那只手的校准数据。源说，校准好的“手感”比义手本身值钱。";
                case RepairPropKind.Power:
                    return "街区照护站的夜间电源，带着透析机和走廊夜灯。鳶说电池换过也没用，一到半夜负载上来就断。";
                default:
                    return "鳶照顾过的三床奶奶的御守。奶奶走之前，每晚都要按一下，听孙子说一声“晚安”。后来孙子的号码停了。";
            }
        }

        static readonly Dictionary<RepairPropKind, (string node, string text)[]> findings = new Dictionary<RepairPropKind, (string, string)[]>
        {
            [RepairPropKind.Receiver] = new[]
            {
                ("RX_RearCover", "后盖内侧用记号笔写着「07-B」，边上有几道磕痕，像是被安全帽灯反复碰过。"),
                ("RX_BatteryCassette", "电池盒背面贴着一条胶带，手写：「哥 02:10 别关」。"),
                ("RX_RF_Daughterboard_Fault", "射频插座里有白色的盐结晶：这台机器在海水里泡过。故障就在这里。"),
                ("RX_Mainboard", "记录区完好。按规定只核对身份，不打开记录内容。"),
                ("RX_TopStrip_Hinge", "护条上贴着一张褪色的港务安全贴：「下井前报呼号」。"),
            },
            [RepairPropKind.Cartridge] = new[]
            {
                ("MC_TopCover", "顶盖贴着回收标签：「潮汐回收 · 批次 0913 · 来源不明」。"),
                ("MC_DataCore", "数据核心外壳上刻着属主缩写「LUO.Y」，下面一行小字「吊机 3 组」。"),
                ("MC_InterfaceBoard_Fault", "接口触点被反复强行插拔磨坏了：有人试过不用密钥硬读。"),
                ("MC_BoardJumper", "排线上缠着一圈旧医用胶布，写着「别格式化」，笔迹不是源的。"),
            },
            [RepairPropKind.Power] = new[]
            {
                ("PS_LeftServiceCover", "侧盖内侧贴着照护站的设备清单，最后一行是铅笔补上的：「小绿（旧看护机）」。"),
                ("PS_TrayHarness", "输出线束上有一处私接分线，焊得很仔细，胶带上写着「小绿 · 别拔」。"),
                ("PS_OutputModule_Fault", "输出模块过热发黄：负载里一直多带着一台没登记的设备。"),
                ("PS_SealedBatteryCassette", "密封电池盒封条完好，电量正常，问题不在电池。"),
                ("PS_Fan", "风扇叶片上卡着一片干掉的花瓣。"),
            },
            [RepairPropKind.Omamori] = new[]
            {
                ("OM_RearCover", "背盖里压着一张折小的纸条：「3 床 · 晚安 · 别让她知道」。"),
                ("OM_MemoryCore", "记忆核心完好。按规定只核对身份，不播放录音。"),
                ("OM_WakeModule_Fault", "唤醒按键的触点磨得发亮：每晚都有人按，按了很多年。"),
                ("OM_Coil", "感应线圈上缠着一根褪色的红绳。"),
            },
        };

        /// <summary>The story mark on a part (variants such as _Connected / _Released share their stem's), or null.</summary>
        public static string Finding(RepairPropKind kind, string objectName)
        {
            if (!findings.TryGetValue(kind, out var list) || string.IsNullOrEmpty(objectName)) return null;
            foreach (var (node, text) in list)
                if (objectName == node || objectName.StartsWith(node + "_", System.StringComparison.Ordinal)) return text;
            return null;
        }

        /// <summary>A line the test adds to its result (read-only metadata, or what the bench measures), or null.</summary>
        public static string TestNote(RepairPropKind kind, string testId, bool pass)
        {
            switch (kind)
            {
                case RepairPropKind.Receiver when testId == "signal":
                    return pass ? "另：02:10 频段收到一次自动应答握手，呼号仍是「巡检 07-B」，发信方登记为 NSP 自动巡检单元。"
                        : "射频插座有盐蚀。";
                case RepairPropKind.Cartridge when testId == "read":
                    return pass ? "校准块完整：一只做了很多年装卸的手留下的抓握节拍。"
                        : "属主标记：LUO.Y（装卸 3 组），状态：雇主已远程释放。";
                case RepairPropKind.Power when testId == "load":
                    return pass ? "私接分线按原样保留，新模块能同时带动透析机和那台旧看护机。"
                        : "线束上有一处私接分线，负载里多出一台未登记设备。";
                case RepairPropKind.Omamori when testId == "identity":
                    return pass ? "条目元数据：最近 212 条“晚安”的录入设备是「照护站 03 号终端（夜班）」，不是孙子的号码。" : null;
                default:
                    return null;
            }
        }
    }
}
