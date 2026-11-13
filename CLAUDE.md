# 边境维修站 / Border Repair Station

地下义体维修处的**经营 + 维修解谜**游戏。Unity 6 + URP。

---

## 游戏设定

- **地点**：北坡港务（NSP）边境港区的地下维修铺。不是军事地堡——老式电梯剪刀式活动铁门、通风格栅、管线、生活痕迹；地面不是整片金属甲板。
- **时间**：夜间班次。房租 1200，**第 3 晚到期**，有付款压力。
- **布局**：入口对面是工作台，中央是义体手术台及环形下挂工具，一侧机器人维修区，另一侧交易区。交易区椅子在柜台内部，显示器朝向内部操作员及墙壁方向。交易区负责领取待维修物品、送出修好的物品。
- **顾客**：港区装卸工罗亚、夜班管线巡检员、巡线员老周、货运司机阿坎、夜班护理员、街区照护站、回收站小梅、二手义体商、收藏家。
- **维修对象**：信号接收器、记忆卡匣、维修电源、赛博御守、通讯器、导航信标、回收无人机、工人义手、UNIT 07 维修机器人。
- **技术水平**：卡带未来主义（cassette futurism）——卡带/卡匣、接收器、CRT 屏一代。东西都是租来的、修过的、捡来的。
- **制度痕迹**：租赁义体贴着封条（`NSP PORT LEASED / VOID IF OPENED`）；边境导航设备的检修封签必须完整；顾客反复要求保留通话记录、录音、原身份和校准参数。
- **UNIT 07**：既是维修对象，未来也可升级为助手（提示、拿工具、屏幕信息）。**这些未来功能不要当成已经实现。**

---

## 技术栈

- Unity 6 + URP
- **Input System**（新输入系统，用 `Keyboard.current`，不是旧 `Input.GetKey`）
- XR Interaction Toolkit（VR 版是独立场景与独立包 `com.borderrepair.repairprops.vr`）

---

## 关键场景

| 路径 | 用途 |
|---|---|
| `Assets/RepairProps/Scenes/RepairProps_Clinic.unity` | **当前手术台 / 诊所主场景** |
| `Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic.unity` | 统一诊所主场景 |
| `Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic_Menu.unity` | 统一诊所菜单入口 |
| `Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic_ArtPreview.unity` | 美术预览 |
| `Assets/BorderRepair/Scenes/Slice/Night1_Counter.unity` | 第一晚柜台 |
| `Assets/BorderRepair/Scenes/Slice/TwoNight_Menu.unity` | 两晚切片菜单 |
| `Assets/BorderRepair/Scenes/Slice/Unit07_Night.unity` | UNIT 07 夜班 |
| `Assets/RepairPropsVR/Scenes/RepairProps_Clinic_VR.unity` | VR 版诊所 |

> **`RepairProps_Clinic` 是唯一权威场景**（2026-11-12 起）：试玩包只打这一个场景，直接改它、提交到 git。它最初从 `UnifiedClinic` 复制而来，现在不再重新复制；场景丢失时用 `git restore` 找回，导入器会直接报错而不是重新复制。
>
> 旧构建器（`TwoNight/UnifiedClinicBuilder.cs`、`TwoNight/TwoNightBuilder.cs`、`Unit07Dock/Unit07DockBuilder.cs`、`ClinicArtImporter.cs` 等，菜单都在 `Border Repair/Old` 下）仍会重写 `RepairProps_Clinic` 引用的共享预制体、材质和网格（`BorderRepair/Art/UnifiedClinic`、`BorderRepair/Cleaning/Meshes`、`Prefabs/Unit07Dock` 等）。从菜单点它们会先弹窗确认（`Assets/BorderRepair/Scripts/Editor/LegacyBuilderGuard.cs`）；批处理和代码调用不拦。不要为了改诊所去重跑它们。

---

## 编辑器里的操作

- **按 `-` 键**：召唤病人罗亚进诊所（仅编辑器与开发版；实现见 `Assets/RepairProps/Runtime/SurgeryPatientRuntime.cs:152`，`Keyboard.current.minusKey`）。
- 病人流程：束带扣着时先用准星对准束带、左键解开 → 他自己坐上台躺下 → 扣上三条束带 → 准星对准**右前臂**进入近景手术。
- 近景手术要求：**束带扣紧 + 手术灯打开 + 机械臂展开**，缺哪项面板会写明原因。
- 中途退出近景**不丢进度**，再点手臂可继续。
- 正式游戏流程里还没有"叫病人进来"的正常入口，目前只能靠 `-` 键。这是待办项。

---

## 美术约束（重要）

1. **台面上的仪器屏幕挂在实体设备上**：CRT、单色荧光屏、段码 LCD、指针表头、指示灯。仪器、终端、诊断设备一律如此。
2. **房间导视是全息投影**（2026-11 起的方向变更）。门头、营业时间、受付窗口、价目表、作业台、柜台收/发 pad 这些**房间级标识**用悬浮全息面板，不做实体灯箱。要点见下节。
3. 赛博朋克感来自：**二手义体、混用品牌、补线、旧胶带、手写编号、过期耗材**。
4. **不依赖**：全屏故障特效、强闪烁、静态扫描线。
5. 参考方向：卡带未来主义医疗设备、复古工业诊断屏。
6. 流程里出现的设备要在台上**看得见**——例如"外部测试连接/负载"和"测试供电"对应桌上的测试座：指针表、开关、香蕉插孔、夹线。

## 全息投影的具体做法

实现集中在 `Assets/RepairProps/Runtime/ClinicSignageRuntime.cs` 和 `Assets/RepairProps/Cyber/Resources/BorderRepairCyber/HoloPanel.shader`。

- 面板离墙漂浮 0.10–0.24 m，**不贴墙面**（贴墙会被管线、层板挡住）。
- shader 是 `ZTest LEqual`（`_ZTest` 属性）：NPC、手、搬运物正常挡住投影；墙面杂物靠上面的离墙距离避开，不靠关深度测试。个别面板被杂物切到时，加大那块的 lift。
- 动态图形是 `HoloWidget`（`Assets/RepairProps/Runtime/HoloWidget.cs`）：每个一块静态四边形，种类和参数写在 uv1/uv2，图形和动画全在 shader 里，不每帧重建网格。默认 PS2 预设 288 行、24 级色阶，3 m 外一个虚拟像素约 12 mm，图形细节要按这个尺寸定。
- 动效参照 `reference-library`（Arasaka 脑机终端 / CP2077 加密传输 / Destiny 星门与诊断报告 / 未来交通导视）：**静止时文字锐利、几乎无色散**；色散只出现在故障的那一条带上；扫描亮带很淡（增益 0.15、周期 9 s）；撕裂罕见（全房间合计约 0.27 次/秒）。
- 可读性第一：任何时候文字都要能读。不做全屏噪点、不做持续强闪烁。
- 层次靠**细边框 + 四角 L 形括号**，不靠把底板调亮。
- 注意：全息面板是加法混合，背后有强光源时会掉对比度。遇到这种情况优先把面板往外挪，或单独调高该面板的 `_Intensity`。

---

## 剧情文本

物品背景故事、部件痕迹、测试附注、来电台词和 UNIT 07 的台词都在 `Assets/RepairProps/Story/Resources/RepairPropsStory/` 的 `props.json`、`calls.json`、`assistant.json` 里，由 `RepairPropStoryText` 读取。改剧情直接改 JSON，不用改代码；键名（物品 kind、部件节点名、测试 id、来电 id、助手的 group/hint 键）要保持不变，`RepairPropStoryTextTests` 会检查它们都还在。界面提示和按钮文字仍写在代码里。

**对话编辑器**：菜单 `Border Repair/Repair Props/对话编辑器`。可以编辑三个文件、撤销、检查错误、试播来电分支，保存时按固定格式写回（`DialogueJson`）。

来电支持分支（`Assets/RepairProps/Runtime/Dialogue/`）：每通来电的 `trigger` 决定什么时候打进来（按文件顺序检查，每局一次）；台词可以有 `id`（跳转标签）、`when`（条件，不满足就跳过）、`set`（设置/清除标记，如 `a, !b`）、`next`（跳到标签或 `end`）和最多 4 个 `choices`（玩家选项，按数字键或点击）。条件写法：`flag:x`、`played:来电id`、`delivered:receiver`、`phase:Night1Counter`、`time>25`、`cash>=1200`、`delivered>=1`，用 `&`、`|`、`!`、括号组合。标记每局清空，目前不进存档。

---

## 教学关与精简界面

- **教学关**（`Assets/RepairProps/Runtime/ClinicTutorialRuntime.cs`）：新开一局的第一单由 UNIT 07 一步一步带。屏幕下方的教学卡片取代指引条，闪动框标出要点的按钮，流程拒绝“当前这一步”以外的操作（`RepairPropFlow.ExecuteGate`，同一类步骤算一步，见 `Family`），接收器的故障固定为要换小板的那一种（`RepairPropFlow.FaultChooser`）。第一单交付或点“跳过教学”后结束。教学期间七号的闲聊和来电都先不出现。
- 只有**从诊所场景启动**的游戏（打包后的游戏，或在编辑器里打开 `RepairProps_Clinic` 后按 Play）才会自动开始教学；之后才加载的场景（测试的做法）不受影响。测试里用 `ClinicTutorialRuntime.Begin()` 手动开始。
- **精简界面**（`RepairPropWorkspaceLayout.Compact`）和教学一起打开，教学结束后保留：阶段条、测试明细、部件下拉框、工单/记录页签、画面装饰读数收起，操作列表只列当前能做的；工单窗口里的“▼ 显示详细信息”可以切回完整面板。默认值是关，所以没开教学的调用方看到的仍是完整面板。

---

## 按键约定

- **Esc 一次只关最上面一层**：配件采购 > SYS 菜单 > 手册 > 进行中的维修手势 > 手术近景 > 松开光标。新的 Esc 处理不要直接读 `escapeKey`，用 `BorderRepair.Controls.EscapeRouter`：`Register(层, this, () => 是否打开)`，再问 `Pressed(层)`。
- **数字键**：来电出现选项时 1–4 选回答，此时停靠七号的机位键让位（`FirstOrderInput.NumberKeysClaimed`）。
- 读字母键的地方要检查 `FirstOrderInput.TextInputFocused`，输入框里打字不算操作。

## 义眼界面的视线路径（维修台）

界面显示在玩家的义眼上。同一时刻只有一个焦点，玩家按固定顺序看：

1. **做什么**：视野上方居中的步骤标题（`RepairPropArOverlay.Header`）。「下一步」反白块是整个界面唯一的反白高亮；第二行写用哪件工具、点哪里。
2. **在哪做**：步骤一变，一条折线从标题画到物品上的目标框，之后每隔几秒再画一次；目标上的标签只写「▶ 步骤名」。
3. **用什么**：手上工具不对时，托盘里该拿的那件被框出并标「拿这个」（`ToolToTake`）。
4. 右侧只有「操作」窗口常亮；工单、工具、日志窗口盖着一层暗罩，鼠标移到面板上才亮（`RepairPropContextPanel.Shade`）。底部只剩按键说明。

动效规则：步骤完成先打一下「✓ 完成」，新步骤逐字打出，标题下的横线从中间扫开，反白块弹一下后缓慢呼吸。不加全屏闪烁、强闪、静态扫描线。
**维修台的屏幕分区（2026-11-13）**：顶部中间＝阶段条（`ContextPanel.StageRail`，接收→交付，带分段进度线）在上、「下一步」标题在下；左下＝实物工具托盘，它就是选工具的地方——托盘里每件工具带一条引线标注「数字键 图标 名字」（`ArOverlay.TrayLabels`，互相避让），数字键按屏幕从左到右编号（`RepairBenchV2Runtime.TrayOrder`，通话窗口开着时数字键归通话）；右栏不再有「检查/螺丝刀/开壳/检测仪」按钮（有托盘时隐藏），那个窗口叫「对象 // TARGET」，只留维修/外观和对象选择。窗口标题栏只有标题文字是实心小标签，其余是细线加斜纹（`CyberStyle.TitleBar` / `BarRule`）；边框是均匀细线、四角加重（EVA / Destiny 终端的画框），不要再用整条实心色带和斑驳底纹。分析动画旁有一根 EVA 式刻度尺（`ArOverlay.Gauge`）。

**点线面的规矩（2026-11-13，用户嫌乱之后定的）**：同一时刻场景里只有一个最亮的点——步骤要先拿工具时，亮的是托盘上「拿这个」，标题折线也指向那件工具（`TraceToTool`），物品上的下一步标记变暗且不带文字，工具到手才亮。托盘标注平时只是小数字加图标，鼠标到托盘附近才展开成带名字的标签（`TrayOpen`）。不重复信息：物品上方不再挂「名称 · 阶段」。右栏是一个面：只有「操作」窗口有明显边框，其余窗口边框用 `AmberFaint`。底部按键说明是一行字，不是窗口（没有标题栏）。加新标记前先问：它是不是此刻唯一该看的点？不是就做暗、做小，或者等情境到了再出现。

**动手规则（2026-11-14，用户定的直觉）**：螺丝——拿着螺丝刀点一下，电动螺丝刀自己拧（`Gesture.RunAuto`），不再手动画圈。徒手——电源、测试连接、交付，以及插/拔/滑出/滑入这几类动作，不要求任何工具，空手或拿着别的工具都能点（`RepairPropStationBridge.ByHand`，按动作判断，不看数据里挂的是哪件工具）；做完手上工具不变（`ShownTool`）。检测仪只用于测试和测量；撬盖板、擦触点、压端子仍要对应工具。点机身只执行“当前建议的那一步”的整机徒手操作；空手点既能看又能做的零件先查看，除非它正是建议的那一步（`ClickCommandAt`）。悬停只画一个四角框，不画全部子零件线框。中键拖动平移视角（`ItemInspector.Pan`），R 复位。拿在手上的工具按远近补偿缩放，屏幕上大小不变（`HoldScale`）。手势提示带黑描边、用亮色，在反光零件上也看得见。

**右栏与采购（2026-11-14）**：正常开局右栏自动收起（`RepairPropWorkspaceLayout.AutoHide`，和精简界面一起由教学开启），鼠标到右边缘才滑出，离开后停半秒再收；需要点面板按钮、没有建议步骤、开外观页时自己出来；收起时右边缘留一条「操作面板」侧栏；面板左上角有「固定」。顶部标题和阶段条不跟面板滑动（`ContextPanel.Docked`）。配件采购在桌上电脑（`RepairBenchPropKind.Terminal`，挂在 `BV2_Crt_Screen` 上），在维修台时 SYS 菜单不显示采购；缺件时电脑上有醒目标记（`NeedsPurchase`）。

**测试范围**：只跑 `^BorderRepair\.RepairProps`。用户明确不要再跑旧项目（FirstOrder、WorkbenchArea 等）的测试；TwoNight 和早期通用测试是否还在用尚未确认，先不跑。

**桌上电脑与桌面布置（2026-11-14）**：配件电脑（`BV2_Crt*`）在维修台右后角，转 -28° 面向玩家；它的转角必须放在物体变换里，不能烤进顶点——Unity 的 `SetupCrt` 从屏幕物体的变换读玻璃朝向，烤进去的话读数和采购界面会平贴着墙、和玻璃对不上（`build_crt` 里的 `turned()`）。电脑带抽拉键盘、钢管提手架、卡槽和琥珀灯；原来贴在它身上的零碎由 `relocate_sets()` 整体搬过去。电烙铁一套在左后角。零件柜顶有圆屏小示波器，洞洞板前有一束垂线和检修牌（`build_bench_identity()`）。改模型的流程：改 `build_repair_bench_v2.py` → Blender 构建 → `atlas_bake.py` → 拷贝 FBX 和两张图集 → Unity 批处理 `RepairBenchV2Builder.Build`。只用已有材质，新增材质要同时登记到 `RepairBenchV2Builder.cs`。

**配色守住参考库的琥珀单色**（`VisorIcons.cs`；对照 Destiny 终端、琥珀网络图、SIGNALIS）：层级靠亮度和反色块，不靠色相；浅琥珀（Hot）＝眼睛在看东西（悬停、检查、分析）；绿＝完成/通过；红＝故障/不行。工具之间靠图标区分，颜色只在琥珀亮度里微调。不要再加青、蓝、粉这类参考里没有的色相（2026-11-13 用户否决过青色）。
**图标**：`VisorIcons` 里用 12×12 点阵在代码里画，不用美术文件；`VisorIcons.Command(命令)` 按命令种类给图（电源、插头、波形、螺丝、盖板、芯片…），`Tool/Prop` 给工具图。新加命令种类时在 `CommandName` 里补一条。
**物体分析动画**（`RepairPropArOverlay.Analyze`）：检查、测量、测试之后，三层切片在目标的包围盒里上下扫一遍，角标锁定，旁边打出读数；测量/测试带一条逐渐稳定的波形（异常时保持毛刺）。只在真有结果时播放，文字用流程给的真实反馈，不放假数据。
新增界面元素先想它在这条路径上的第几步；不在路径上的信息一律用暗色小字。

## 维修台工具：拿在手上用

- 托盘里五件：螺丝刀、开壳工具、检测仪、放大镜（空手检查）、清洁工具（吹尘刷）。点一下拿起，工具离开托盘跟着鼠标走，工作端落在指着的位置（`RepairBenchV2Runtime.DriveHand` / `RepairBenchProp.Hold`）。
- **左键就是用手上的工具**：光标旁写明这一下会做什么（绿）或为什么不行（红）。规则只有一处：`RepairPropStationBridge.ToolCommandAt`，悬停提示和点击都问它。
- 空手（放大镜）点露出来的部件就是检查；清洁工具拿起即进入清洁（等同 C 键），按住左键擦。
- **右键单击放下**（右键拖动仍是转动物品）；清洁时点托盘里别的工具会直接换过去。
- 工具模型的工作端由 FBX 里名为 `<工具名>_Tip` 的空物体标出；没有这个标记的工具（检测仪）拿在指点位置旁边。
- 模型来源：`ArtSource/RepairBenchV2/build_repair_bench_v2.py`（Blender 脚本）→ `atlas_bake.py` → 拷到 `Assets/RepairProps/Bench/Art` → 菜单 `Border Repair/Repair Props/Build Repair Bench V2`。托盘必须留在维修台镜头内（x -0.50..-0.20，y 0.30..0.60）。
- 检测仪的两根线是运行时的物理软线（`RepairBenchLeads.cs`，verlet 绳 + LineRenderer），模型里只有表笔 `BV2_Tool_Meter_Probe*` 和插孔位置 `BV2_Tool_Meter_Jack*`；拿着检测仪时红表笔针尖落在指点位置。
- 手上的工具不会低于台面：`RepairBenchV2Runtime.Hold` 的 `floorY`。
- 托盘后面的涂装喷罐 `BV2_Tool_Sprayer` 是外观涂装的入口（等同工具窗口里的「外观」按钮）。
- 维修台上清洁工具是托盘里的实物，屏幕上的清洁栏不显示（`ClinicCleaningController.ControlsHidden`），进度写在底部提示行。
- 后台 batch 测试自动静音（`BatchModeMute.cs`）。

---

## 开发规则

1. **先只读审查**，区分"已完成 / 仅编写 / 未验证 / 已知问题"，不要一上手就大规模修改。
2. **不要自动**做这些事：提交、推送、合并 main、删除旧项目、重做模型。
3. **Unity 编辑器开着时不要跑 batch 测试**，等关闭后再跑。
4. 未经确认不要执行大规模修改。
5. Tripo 网页版由用户本人生成，**不要自动操作或消耗积分**。
6. 描述代码完整程度时必须**实际读文件**，不要仅凭之前的描述认定验收通过。

---

## 分支约定

| 分支 | 说明 |
|---|---|
| `main` | 主线（注意：`main` 里**没有** UnifiedClinic 场景） |
| `integ/repair-props-20261017` | 当前开发线 |
| `backup/repair-props-wip-20261031` | 2026-10-31 的手术台开发 WIP 快照 |

提交信息末尾加：

```
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

---

## 相关文档

- `Docs/Integration/RepairProps20261017/README.md` — 四件道具集成说明
- `Docs/Integration/RepairPropsCyberBench/ART_DIRECTION.md` — 美术方向
- `Docs/Integration/RepairPropsCyberBench/PERFORMANCE.md` — 性能
- `Docs/Integration/RepairPropsCosmetics/README.md` — 外观/手感
- `Docs/Narrative/WorkerHand_Slice.md` — 叙事切片
- `Docs/ArtProduction/TRIPO_WORKFLOW.md` — Tripo 模型流程
