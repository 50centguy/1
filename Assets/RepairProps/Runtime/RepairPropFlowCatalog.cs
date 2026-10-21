using System.Collections.Generic;
using BorderRepair.Data;
using UnityEngine;

namespace BorderRepair.RepairProps
{
    // Paths and travel directions come from the delivered part contracts, not from decorative seams.
    public static class RepairPropFlowCatalog
    {
        public static RepairPropFlowDefinition Create(RepairPropKind kind, RepairCaseData repairCase)
        {
            var d = ScriptableObject.CreateInstance<RepairPropFlowDefinition>();
            d.name = kind + "_Flow";
            d.kind = kind;
            d.repairCase = repairCase;
            var b = new Author(d);
            switch (kind)
            {
                case RepairPropKind.Receiver: Receiver(b); break;
                case RepairPropKind.Cartridge: Cartridge(b); break;
                case RepairPropKind.Power: Power(b); break;
                case RepairPropKind.Omamori: Omamori(b); break;
            }
            b.Finish();
            return d;
        }

        static void Receiver(Author b)
        {
            b.Root("RX_SignalReceiver", "RX_Mainboard");
            b.Test("signal", "标准信号与锁频测试", true,
                "显示与电池响应正常，射频输入无法锁定标准信号。请检查射频小板的输入插座。",
                "标准信号已锁定，接收响应稳定，原有呼叫时段保留。");
            b.Test("identity", "只读核对原调谐记录身份", false,
                "原调谐记录身份不一致，请保留原主板。", "原调谐记录身份一致，未打开记录内容。", true);
            b.Cover("RX_RearCover", "RX_CoverScrew_", new Vector3(0, 0, -0.06f));
            b.Fixed("RX_Mainboard", "RX_Internals/RX_InnerCarrier/RX_Mainboard", true);
            b.Fixed("RX_BatteryCassette", "RX_Internals/RX_InnerCarrier/RX_BatteryCassette", true);
            b.Isolation("RX_BatteryLead_Connected", "RX_Internals/RX_BatteryLead_Connected",
                "RX_BatteryLead_Isolated", "RX_Internals/RX_BatteryLead_Isolated");
            b.Harness("RX_RFCoax_Connected", "RX_Internals/RX_RFCoax_Connected",
                "RX_RFCoax_Released", "RX_Internals/RX_RFCoax_Released");
            b.Module("RX_RF_Daughterboard", "RX_Internals/", "RX_RFScrew_", "RX_Internals/", new Vector3(0, 0, -0.025f));
            b.Visible("RX_TestLead", "RX_TestLead", "fixture", 1, false);
            b.TestDisplay("RX_ScreenState_Noise", "RX_FX/RX_ScreenState_Noise", "signal", false);
            b.TestDisplay("RX_ScreenState_Locked", "RX_FX/RX_ScreenState_Locked", "signal", true);
            b.Curtain("RX_LightCurtain_Noise", false);
            b.Curtain("RX_LightCurtain_Locked", true);
            var hinge = b.Fixed("RX_TopStrip_Hinge", "RX_TopStrip_Hinge", false, false);
            hinge.rotationKey = "strip";
            hinge.rotationAxis = Vector3.back;
            hinge.rotationDegrees = 90;
            b.Initial("strip", 0);
            b.Command("strip.deploy", "展开顶部护条", "RX_TopStrip_Hinge", PropCommandKind.Pose, RepairActionType.OpenHousing,
                new[] { C("strip", 0) }, new[] { C("strip", 1) });
            b.Command("strip.fold", "收起顶部护条", "RX_TopStrip_Hinge", PropCommandKind.Pose, RepairActionType.OpenHousing,
                new[] { C("strip", 1) }, new[] { C("strip", 0) });
        }

        static void Cartridge(Author b)
        {
            b.Root("MC_MemoryCartridge", "MC_DataCore");
            b.Test("read", "只读识别与数据块一致性测试", true,
                "识别间歇中断，可读数据块仍保留原身份，接口触点存在损伤。",
                "多次识别与只读一致性验证通过，未复制、写入或格式化内容。", true);
            // Blender +Z becomes Unity +Y; Blender -X becomes Unity +X under the delivered axis conversion.
            b.Cover("MC_TopCover", "MC_CoverScrew_", new Vector3(0, 0.025f, 0));
            b.Fixed("MC_DataCore", "MC_BaseTray/MC_DataCore", true);
            b.Harness("MC_BoardJumper_Connected", "MC_Internals/MC_BoardJumper_Connected",
                "MC_BoardJumper_Released", "MC_Internals/MC_BoardJumper_Released", false);
            b.Module("MC_InterfaceBoard", "MC_Internals/", "MC_IFScrew_", "MC_Internals/", new Vector3(0.036f, 0, 0));
            b.TestDisplay("MC_StatusLED_Fail", "MC_FX/MC_StatusLED_Fail", "read", false);
            b.TestDisplay("MC_StatusLED_OK", "MC_FX/MC_StatusLED_OK", "read", true);
        }

        static void Power(Author b)
        {
            b.Root("PS_PowerUnit", "PS_SealedBatteryCassette");
            b.Test("no_load", "空载输出与电池检查", false,
                "空载检查未通过，请保留密封电池盒。", "电池电量正常，空载输出正常。");
            b.Test("load", "标准模拟负载测试", true,
                "电池电量正常，但标准模拟负载下输出掉压，请检修输出模块。",
                "标准模拟负载下输出稳定，输出告警解除。");
            b.Cover("PS_LeftServiceCover", "PS_CoverScrew_", new Vector3(-0.06f, 0, 0));
            b.Fixed("PS_SealedBatteryCassette", "PS_Interior/PS_SealedBatteryCassette", true);
            b.Fixed("PS_FrontElectronics", "PS_Interior/PS_FrontElectronics", true);
            b.Fixed("PS_Fan", "PS_Interior/PS_Fan", true);
            b.Isolation("PS_BatteryLead_Connected", "PS_Interior/PS_BatteryLead_Connected",
                "PS_BatteryLead_Isolated", "PS_Interior/PS_BatteryLead_Isolated", "PS_BatteryServicePlug", "PS_Interior/PS_BatteryServicePlug", new Vector3(-0.015f, 0, 0));
            b.Harness("PS_TrayHarness_Connected", "PS_Interior/PS_TrayHarness_Connected",
                "PS_TrayHarness_Released", "PS_Interior/PS_TrayHarness_Released", true,
                "PS_TrayHarnessPlug", "PS_Interior/PS_TrayHarnessPlug", new Vector3(-0.01f, 0, 0));
            b.Tray("PS_OutputTray", "PS_Interior/PS_OutputTray", "PS_TrayScrew_", "PS_Interior/", new Vector3(-0.20f, 0, 0));
            b.Module("PS_OutputModule", "PS_Interior/PS_OutputTray/", "PS_ModuleScrew_", "PS_Interior/PS_OutputTray/", new Vector3(0, 0.04f, 0));
        }

        static void Omamori(Author b)
        {
            b.Root("OM_Omamori", "OM_MemoryCore");
            b.Test("identity", "只读核对身份与记忆条目一致性", false,
                "原身份不一致，请保留原记忆核心。", "原身份与记忆条目一致，录音内容保持私密。", true);
            b.Test("wake", "唤醒按键响应测试", true,
                "唤醒脉冲中断，原身份仍保留，请检查唤醒模块的腐蚀痕迹。",
                "唤醒响应稳定，未播放录音。", false, 3);
            b.Cover("OM_RearCover", "OM_CoverScrew_", new Vector3(0, 0, -0.03f));
            b.Fixed("OM_MemoryCore", "OM_Interior/OM_MainBoard/OM_MemoryCore", true);
            b.Fixed("OM_MainBoard", "OM_Interior/OM_MainBoard", true);
            b.Fixed("OM_Coil", "OM_Interior/OM_Coil", true);
            b.Fixed("OM_PowerCell", "OM_Interior/OM_PowerCell", true);
            b.Isolation("OM_PowerLead_Connected", "OM_Interior/OM_PowerLead_Connected",
                "OM_PowerLead_Isolated", "OM_Interior/OM_PowerLead_Isolated", "OM_PowerPlug", "OM_Interior/OM_PowerPlug", new Vector3(0, 0, -0.004f));
            b.Harness("OM_WakeFlex_Connected", "OM_Interior/OM_WakeFlex_Connected",
                "OM_WakeFlex_Released", "OM_Interior/OM_WakeFlex_Released", true,
                "OM_WakePlug", "OM_Interior/OM_WakePlug", new Vector3(0, 0, -0.004f));
            b.Module("OM_WakeModule", "OM_Interior/", "OM_WakeScrew_", "OM_Interior/", new Vector3(0, 0, -0.022f));
            b.FollowWake("OM_WakeFlex_Released");
            b.FollowWake("OM_WakePlug");
        }

        static PropCondition C(string key, int value) => new PropCondition(key, value);

        sealed class Author
        {
            readonly RepairPropFlowDefinition d;
            readonly List<PropCondition> insideClosed = new List<PropCondition>();
            readonly List<PropCondition> diagnosis = new List<PropCondition>();
            readonly List<PropCommand> serviceCommands = new List<PropCommand>();
            string cover;
            bool hasIsolation;
            bool hasTray;
            public Author(RepairPropFlowDefinition d) { this.d = d; }
            public void Initial(string key, int value) { if (!d.initialState.Exists(c => c.key == key)) d.initialState.Add(C(key, value)); }
            public void FollowWake(string name)
            {
                var node = d.FindNode(name);
                node.followsRemovedWakeModule = true;
                node.motionNote = "排线与插头随模块移动为原型联动，尚未验证软线扫掠与碰撞。";
            }
            public void Root(string root, string retained)
            {
                d.rootObjectName = root;
                d.retainedObjectName = retained;
                Fixed(root, "", false, false);
                Initial("power", 1); Initial("fixture", 1); Initial("module", 0); Initial("seat", 1);
                Initial("old.out", 0); Initial("new.out", 1); Initial("isolated", 0); Initial("released", 0); Initial("tray", 0);
            }
            public PropNode Fixed(string name, string path, bool immutable, bool internalPart = true)
            {
                var n = new PropNode { objectName = name, displayName = RepairPropText.Part(name, d.kind, d.rootObjectName), hierarchyPath = string.IsNullOrEmpty(path) ? d.rootObjectName : d.rootObjectName + "/" + path, immutable = immutable };
                if (internalPart) n.inspectRequires.Add(C("cover", 1));
                d.nodes.Add(n);
                return n;
            }
            PropNode Moving(string name, string path, string key, Vector3 offset, bool internalPart = true)
            {
                var n = Fixed(name, path, false, internalPart);
                n.movable = true; n.outKey = key; n.parkingOffset = offset;
                return n;
            }
            public PropNode Visible(string name, string path, string key, int value, bool internalPart = true)
            {
                var n = Fixed(name, path, false, internalPart);
                n.visibleKey = key; n.visibleValue = value;
                return n;
            }
            public void TestDisplay(string name, string path, string test, bool pass)
            {
                Visible(name, path, "display." + test, pass ? 2 : 1, false);
            }
            public void Curtain(string name, bool pass)
            {
                Visible(name, "RX_FX/" + name, "curtain", pass ? 2 : 1, false);
            }
            public void Test(string id, string label, bool functional, string fail, string pass, bool identity = false, int count = 1)
            {
                d.tests.Add(new PropTest { id = id, label = label, functional = functional, faultFinding = fail, passFinding = pass, checksRetainedIdentity = identity, consecutivePasses = count });
                diagnosis.Add(C("diagnosed." + id, 1));
            }
            public PropCommand Command(string id, string label, string target, PropCommandKind kind, RepairActionType tool,
                IEnumerable<PropCondition> requires = null, IEnumerable<PropCondition> effects = null)
            {
                var c = new PropCommand { id = id, label = label, target = target, kind = kind, tool = tool, invalidatesTests = kind == PropCommandKind.Manipulate };
                if (requires != null) c.requires.AddRange(requires);
                if (effects != null) c.effects.AddRange(effects);
                d.commands.Add(c);
                return c;
            }
            PropCommand Service(string id, string label, string target, RepairActionType tool, params PropCondition[] req)
            {
                var c = Command(id, label, target, PropCommandKind.Manipulate, tool, req);
                c.requires.Add(C("power", 0)); c.requires.Add(C("fixture", 0));
                c.requires.AddRange(diagnosis);
                serviceCommands.Add(c);
                return c;
            }
            public void Cover(string name, string screwPrefix, Vector3 travel)
            {
                cover = name;
                Initial("cover", 0);
                var coverNode = Moving(name, name, "cover", travel, false);
                coverNode.inventoryOnly = true;
                coverNode.motionNote = "盖板暂存在零件栏，装回时恢复原安装位。";
                var remove = Service("cover.remove", "取下维修盖板", name, RepairActionType.OpenHousing, C("cover", 0));
                var install = Service("cover.install", "盖板归位", name, RepairActionType.OpenHousing, C("cover", 1));
                for (int i = 1; i <= 4; i++)
                {
                    string screw = screwPrefix + i.ToString("00"); string key = "out." + screw;
                    Initial(key, 0); d.assembled.Add(C(key, 0));
                    Vector3 screwTravel = d.kind == RepairPropKind.Receiver ? new Vector3(0, 0, -0.012f)
                        : d.kind == RepairPropKind.Power ? new Vector3(-0.012f, 0, 0)
                        : d.kind == RepairPropKind.Omamori ? new Vector3(0, 0, -0.009f) : Vector3.zero;
                    var screwNode = Moving(screw, screw, key, screwTravel, false);
                    screwNode.inventoryOnly = d.kind == RepairPropKind.Cartridge;
                    if (screwNode.inventoryOnly) screwNode.motionNote = "拆卸行程未知：原型仅收纳至零件栏，不表示已验证移动距离或碰撞。";
                    Service("remove." + screw, "卸下盖板螺钉" + i, screw, RepairActionType.RemoveFastener, C(key, 0), C("cover", 0)).effects.Add(C(key, 1));
                    Service("install." + screw, "装回盖板螺钉" + i, screw, RepairActionType.RemoveFastener, C(key, 1), C("cover", 0)).effects.Add(C(key, 0));
                    remove.requires.Add(C(key, 1));
                    install.requires.Add(C(key, 1));
                }
                remove.effects.Add(C("cover", 1)); install.effects.Add(C("cover", 0));
                d.assembled.Add(C("cover", 0));
            }
            public void Isolation(string connected, string connectedPath, string isolated, string isolatedPath,
                string plug = null, string plugPath = null, Vector3 travel = default(Vector3))
            {
                hasIsolation = true;
                if (plug == null)
                {
                    Visible(connected, connectedPath, "isolated", 0);
                    Visible(isolated, isolatedPath, "isolated", 1);
                }
                else
                {
                    Moving(plug, plugPath, "isolated", travel);
                    Visible(connected, connectedPath, "isolated", 0); Visible(isolated, isolatedPath, "isolated", 1);
                }
                Service("isolate", "握住插头并断开内部供电", plug ?? connected, RepairActionType.ServiceModule,
                    C("cover", 1), C("isolated", 0)).effects.Add(C("isolated", 1));
                var reconnect = Service("supply.connect", "接回内部供电", plug ?? isolated, RepairActionType.ServiceModule,
                    C("cover", 1), C("isolated", 1));
                reconnect.effects.Add(C("isolated", 0));
                d.assembled.Add(C("isolated", 0));
            }
            public void Harness(string connected, string connectedPath, string released, string releasedPath, bool isolated = true,
                string plug = null, string plugPath = null, Vector3 travel = default(Vector3))
            {
                if (plug != null)
                {
                    var plugNode = Moving(plug, plugPath, "released", travel);
                    if (d.kind == RepairPropKind.Power)
                        plugNode.motionNote = "原型仅演示契约给出的脱座位移；线束完整停放距离未知，尚未验证碰撞。";
                }
                Visible(connected, connectedPath, "released", 0); Visible(released, releasedPath, "released", 1);
                var release = Service("harness.release", "释放带定位的信号／模块接头", plug ?? connected, RepairActionType.ServiceModule,
                    C("cover", 1), C("released", 0), C("tray", 0));
                var connect = Service("harness.connect", "插回带定位的信号／模块接头", plug ?? released, RepairActionType.ServiceModule,
                    C("cover", 1), C("released", 1), C("tray", 0), C("seat", 1));
                if (isolated) { release.requires.Add(C("isolated", 1)); connect.requires.Add(C("isolated", 1)); }
                release.effects.Add(C("released", 1)); connect.effects.Add(C("released", 0));
                d.assembled.Add(C("released", 0));
            }
            public void Tray(string name, string path, string screwPrefix, string screwPath, Vector3 travel)
            {
                hasTray = true;
                Moving(name, path, "tray", travel);
                var remove = Service("tray.remove", "沿导轨抽出输出托盘", name, RepairActionType.OpenHousing,
                    C("cover", 1), C("isolated", 1), C("released", 1), C("tray", 0));
                var install = Service("tray.install", "输出托盘归位", name, RepairActionType.OpenHousing,
                    C("cover", 1), C("isolated", 1), C("released", 1), C("tray", 1), C("seat", 1));
                remove.effects.Add(C("tray", 1)); install.effects.Add(C("tray", 0));
                for (int i = 1; i <= 2; i++)
                {
                    string screw = screwPrefix + i.ToString("00"); string key = "out." + screw;
                    Initial(key, 0); d.assembled.Add(C(key, 0));
                    Moving(screw, screwPath + screw, key, new Vector3(0, 0.012f, 0));
                    Service("remove." + screw, "卸下托盘固定螺钉" + i, screw, RepairActionType.RemoveFastener,
                        C("cover", 1), C("isolated", 1), C("released", 1), C("tray", 0), C(key, 0)).effects.Add(C(key, 1));
                    Service("install." + screw, "装回托盘固定螺钉" + i, screw, RepairActionType.RemoveFastener,
                        C("cover", 1), C("isolated", 1), C("released", 1), C("tray", 0), C(key, 1)).effects.Add(C(key, 0));
                    remove.requires.Add(C(key, 1)); install.requires.Add(C(key, 1));
                    insideClosed.Add(C(key, 0));
                }
                d.assembled.Add(C("tray", 0));
            }
            public void Module(string prefix, string path, string screwPrefix, string screwPath, Vector3 travel)
            {
                var oldNode = Moving(prefix + "_Fault", path + prefix + "_Fault", "old.out", travel);
                oldNode.hideWhenKey = "new.out"; oldNode.hideWhenValue = 0;
                var newNode = Moving(prefix + "_New", path + prefix + "_New", "new.out", travel);
                newNode.inventoryOnly = true;
                var access = new List<PropCondition> { C("cover", 1), C("released", 1), C("tray", hasTray ? 1 : 0) };
                if (hasIsolation) access.Add(C("isolated", 1));
                var screwsOut = new List<PropCondition>();
                for (int i = 1; i <= 2; i++)
                {
                    string screw = screwPrefix + i.ToString("00"); string key = "out." + screw;
                    Initial(key, 0); d.assembled.Add(C(key, 0)); insideClosed.Add(C(key, 0)); screwsOut.Add(C(key, 1));
                    Vector3 screwTravel = d.kind == RepairPropKind.Power ? new Vector3(0, 0.01f, 0)
                        : d.kind == RepairPropKind.Omamori ? new Vector3(0, 0, -0.005f) : Vector3.zero;
                    var screwNode = Moving(screw, screwPath + screw, key, screwTravel);
                    screwNode.inventoryOnly = d.kind == RepairPropKind.Receiver || d.kind == RepairPropKind.Cartridge;
                    if (screwNode.inventoryOnly) screwNode.motionNote = "拆卸行程未知：原型仅收纳至零件栏，不表示已验证移动距离或碰撞。";
                    var remove = Service("remove." + screw, "卸下模块固定螺钉" + i, screw, RepairActionType.RemoveFastener, C(key, 0), C("seat", 1));
                    var install = Service("install." + screw, "装回模块固定螺钉" + i, screw, RepairActionType.RemoveFastener, C(key, 1), C("seat", 1));
                    remove.requires.AddRange(access); install.requires.AddRange(access);
                    remove.effects.Add(C(key, 1)); install.effects.Add(C(key, 0));
                }
                foreach (bool replacement in new[] { false, true })
                {
                    string name = replacement ? newNode.objectName : oldNode.objectName;
                    string key = replacement ? "new.out" : "old.out";
                    int module = replacement ? 2 : 0;
                    var remove = Service(replacement ? "module.remove.new" : "module.remove.old", replacement ? "取下替换模块" : "取下故障模块", name,
                        RepairActionType.ServiceModule, C("module", module), C("seat", 1), C(key, 0));
                    remove.requires.AddRange(access); remove.requires.AddRange(screwsOut);
                    remove.effects.Add(C("seat", 0)); remove.effects.Add(C("module", 1)); remove.effects.Add(C(key, 1));
                    remove.effects.Add(C("last.module", module));
                    var install = Service(replacement ? "module.install.new" : "module.install.old", replacement ? "安装替换模块" : "装回原故障模块", name,
                        RepairActionType.ServiceModule, C("module", 1), C("seat", 0), C("old.out", 1), C("new.out", 1));
                    install.requires.AddRange(access); install.requires.AddRange(screwsOut);
                    install.effects.Add(C("seat", 1)); install.effects.Add(C("module", module)); install.effects.Add(C(key, 0));
                }
                d.assembled.Add(C("seat", 1));
                // A module that rides on a tray goes back with it only once it is screwed down; otherwise the tray could
                // be slid home with the module loose and its screws then unreachable (tray in = module screws blocked).
                var trayHome = d.commands.Find(c => c.id == "tray.install");
                if (trayHome != null)
                    foreach (var screwOut in screwsOut) trayHome.requires.Add(C(screwOut.key, 0));
            }
            public void Finish()
            {
                foreach (var c in serviceCommands)
                {
                    if (c.id == "cover.install")
                    {
                        c.requires.AddRange(insideClosed); c.requires.Add(C("seat", 1)); c.requires.Add(C("released", 0));
                        c.requires.Add(C("isolated", 0)); c.requires.Add(C("tray", 0));
                    }
                    if (c.id == "supply.connect")
                    { c.requires.AddRange(insideClosed); c.requires.Add(C("released", 0)); c.requires.Add(C("seat", 1)); c.requires.Add(C("tray", 0)); }
                    if (c.id == "harness.connect" || c.id == "tray.install") c.requires.AddRange(insideClosed.FindAll(r => !r.key.Contains("TrayScrew")));
                }
                var off = Command("power.off", "关机／停止输出", d.rootObjectName, PropCommandKind.Power, RepairActionType.OpenHousing,
                    new[] { C("power", 1) }, new[] { C("power", 0) });
                off.invalidatesTests = true;
                Command("fixture.disconnect", d.kind == RepairPropKind.Cartridge ? "退出只读测试座" : "解除外部测试连接／负载",
                    d.rootObjectName, PropCommandKind.Fixture, RepairActionType.ServiceModule,
                    new[] { C("power", 0), C("fixture", 1) }, new[] { C("fixture", 0) });
                var dock = Command("fixture.connect", d.kind == RepairPropKind.Cartridge ? "接入只读测试座" : "接入外部测试连接／负载",
                    d.rootObjectName, PropCommandKind.Fixture, RepairActionType.ServiceModule,
                    new[] { C("power", 0), C("fixture", 0) }, new[] { C("fixture", 1) });
                dock.requires.AddRange(d.assembled);
                var on = Command("power.on", "开启测试供电", d.rootObjectName, PropCommandKind.Power, RepairActionType.OpenHousing,
                    new[] { C("power", 0), C("fixture", 1) }, new[] { C("power", 1) });
                on.requires.AddRange(d.assembled);
                foreach (var t in d.tests)
                {
                    var test = Command("test." + t.id, t.label, d.rootObjectName, PropCommandKind.Test, RepairActionType.ServiceModule,
                        new[] { C("power", 1), C("fixture", 1) });
                    test.testId = t.id;
                    if (d.kind == RepairPropKind.Power && t.id == "load") test.requires.Add(C("pass.no_load", 1));
                }
                var complete = Command("complete", "确认复装复测并交付原件", d.rootObjectName, PropCommandKind.Complete,
                    RepairActionType.ServiceModule, new[] { C("power", 1), C("fixture", 1), C("module", 2) });
                complete.requires.AddRange(diagnosis);
                ConfigureCase();
            }
            void ConfigureCase()
            {
                var c = d.repairCase;
                c.caseId = "repair-prop-" + d.kind.ToString().ToLowerInvariant();
                c.itemName = RepairPropText.Item(d.kind);
                c.customerName = d.kind == RepairPropKind.Receiver ? "夜班管线巡检员" : d.kind == RepairPropKind.Cartridge ? "二手义体商" : d.kind == RepairPropKind.Power ? "街区照护站" : "夜班护理员";
                c.customerStatement = d.kind == RepairPropKind.Receiver ? "能开机，却只剩噪声。请保留原来的频段和约定呼叫时段。" : d.kind == RepairPropKind.Cartridge ? "识别总是中断。请恢复可读性，保留原来的校准参数与记录。" : d.kind == RepairPropKind.Power ? "明明还有电，一接负载就中断，之前换电池也没用。" : "它几乎唤不醒了。请保留旧录音和我的原身份。";
                c.intakeNote = "保留原有核心、密封部件和外观磨损。仅作只读验证，不播放私人录音，不复制、格式化或更换原核心。";
                // Placeholder fees: midpoints of the flow design's planning estimates, pending the user's pricing decision.
                c.estimatedRepairCost = d.kind == RepairPropKind.Receiver ? 75 : d.kind == RepairPropKind.Power ? 130 : 65;
                c.scoring = CaseScoring.Narrative;
                c.inspectionPoints.Clear(); c.repairSteps.Clear(); c.clues.Clear(); c.endings.Clear(); c.diagnosisOptions.Clear();
                foreach (var n in d.nodes)
                    c.inspectionPoints.Add(new InspectionPointInfo { pointId = n.objectName, displayName = n.displayName,
                        finding = (RepairPropStories.Finding(d.kind, n.objectName) is string mark ? mark + "\n" : "")
                            + (n.immutable ? "原有保留部件，仅供检查，不拆换，不读取私人内容。" : "检查部件与固定连接状态，功能故障需通过维修台测试确认。")
                            + (string.IsNullOrEmpty(n.motionNote) ? "" : "\n" + n.motionNote) });
                c.clues.Add(new ClueDefinition { clueId = "flow.verified", title = "复装与复测已确认", text = "原有核心保留，连接、固定件和功能复测均已通过。", requiredForDecision = true });
                string replacementPoint = d.nodes.Find(n => n.outKey == "new.out").objectName;
                c.repairSteps.Add(new RepairStepDefinition { stepId = d.completionStepId, targetPointId = replacementPoint,
                    action = RepairActionType.ServiceModule, resultState = RepairPartState.Tested,
                    label = "记录复测通过", requiredClues = new List<string> { "flow.verified" }, unlocksClueId = "flow.verified",
                    blockedText = "请先按顺序完成拆装、复装与全部复测。" });
                c.endings.Add(new NarrativeEnding { endingId = d.completionEndingId, label = "交还已验证原件",
                    resultTitle = "维修验证通过", narrative = "原有部件与私人数据保留，复装和功能复测全部通过。", requiredClues = new List<string> { "flow.verified" } });
                c.endings.Add(new NarrativeEnding { endingId = "hold.verified", label = "保管原件，等待领取", resultTitle = "等待领取",
                    narrative = "已验证的原件留在维修台，等待物主领取。", requiredClues = new List<string> { "flow.verified" } });
            }
        }
    }
}
