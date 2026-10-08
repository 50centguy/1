# 收件台拿起 → 放到维修台 → 在台上维修（2026-10-08）

分支 `feat/clinic-carry-to-bench`，从 `integ/unified-clinic-20261008` 的 d5cb584 分出，工作树 `C:\Users\Administrator\Documents\Codex\clinic-bench`。clinic-int 没有改动。

**下面的“通过”都是程序输入（脚本给位置、转头、虚拟鼠标 / 键盘事件），不是真人试玩。**

## 问题

1. 点收件台（RECEIVE）会直接进入维修模式。
2. 维修模式在房间外的检视舞台上（坐标 0, 1.2, 30），背景近黑、只照 5 m 远，看起来是“纯黑的维修画面”。

## 现在的流程

| 步骤 | 行为 |
|---|---|
| 点收件台 | 只是**拿起**：物件拿在眼前（跟着视角），碰撞关闭（不挡准星）；顾客对话照常出现；不进维修界面、维修阶段还是“接收” |
| 拿着走 | 状态栏写“手上：通讯器 → 放到维修台上”；拿着时不能再收件、不能交付；“回到维修台”按钮隐藏 |
| 准星对准维修台（WORKBENCH）左键 | 放到台上，开始在台上维修（接收 → 检查） |
| 台上维修 | 原型检视台（物件锚点 + 检视相机 + 两盏检视灯）在开局时整体挪到房间维修台台面上，从台前玩家一侧看；背景 / 可见层 / 远裁剪与房间相机相同。拖动转物件、滚轮缩放、扫描、诊断、决定都照旧。物件转到任何角度，最低点都贴着台面（0.5 mm 间隙），不穿桌子也不悬空。维修时隐藏 WORKBENCH 标签（镜头就在台前，朝向镜头的标签会挡住画面） |
| 离开维修台 / 回来 | “离开维修台”（原“返回诊所”）回到行走，物件原地留在台上；点台上的物件、维修台或“回到维修台”按钮继续修，不会重新收件 |
| 修好以后 | 和原来一样：物件出现在交件台（SEND OUT），点交件台交付后才入账 |

没有第一人称的场景仍保留原来的“收件即开修”（代码里有注释）。

顺带修掉：顾客夜七号的流程不启用，它的初始化（隐藏首单落点标记）不执行，操作垫上一直有一块发光的黄色落点标记；以前维修画面不在这里所以看不到，现在物件就放在它上面。改为顾客夜开始时隐藏全部落点标记（第二晚流程启用后照旧由流程管理）。

## 改动的文件

- `UnifiedClinicDirector.cs`：拿起 / 放下 / 检视台挪到维修台 / 贴台面 / 点台上物件回到维修 / 状态与按钮。新增 `Carrying`、`CarriedItem`、`PlaceOnBench()`、`InspectionOnBench`、`BenchTopWorldY`；序列化 `benchStandPoint`（默认取自 `ArtSource/UnifiedClinic/anchors.csv` 的 Workbench 站位 0, 0, 1.95）、`carryLocalPosition`。
- `ItemInspector.cs`：只加只读的 `ItemAnchor`。
- `ClinicTradeZone.cs`：`SetLabel` / `ShowLabel`。
- 场景没有重新生成（改动都在运行时）。

## 测试与修正的旧断言

- d5cb584 的检视测试和 Player 自检原来**要求检视舞台离维修台 10 m 以上**（为了避开遮挡）——这和“在台上修”相反，改为要求在维修台上（< 0.6 m）、背景与房间相机一致；保留了原来的遮挡检查（物件实际露出的像素 ≥ 只渲染物件时的 65%），在台上也通过。
- 依赖“收件即开修”的用例都补上“放到维修台”一步；`UnifiedClinicFirstPersonTests` 里改为：真实点击收件 → 拿着沿寻路路径走到维修台 → 准星真实左键放下。
- 新增 `Walking_ReceiveCarriesItem_PlaceOnBench_RepairInRoom`：拿起后物件在眼前、碰撞关闭；放下后物件在维修台位置、贴台面；转动后仍贴台面；镜头在维修台旁且维修台在画面里；离开后物件不动；再点维修台继续。

| 项 | 结果 |
|---|---|
| PlayMode | 129 个：122 通过、0 失败、7 跳过（原有截图采集测试）——`PlayMode_results.xml` |
| EditMode | 153 个：144 通过、9 失败——就是交付文档里那 9 个（`SourceArt_NotModified` / `BenchPlacements_DoNotPenetrate` / `DropZones_SitOnRealBenchObjects_InClearSpace` 各 3 个，新模型未重新生成首单场景引起），这次没有新增也没有修——`EditMode_results.xml` |
| Player 包 | `clinic-bench\Builds\UnifiedClinic-20261008-CarryToBench\BorderRepairClinic.exe`（Development，0 错误 0 警告） |
| 两个独立进程 | `Native/`：第 1 段（收件 → 准星放到维修台 → 台上扫描 / 诊断 / 修 → 交付入账 → 第一晚存档）通过；第 2 段（继续 → 第二晚七号完整维修复测）通过 |

截图：`Clinic_N1_CarryItem.png`（拿在手上）、`Clinic_N1_BenchRepair.png` / `Clinic_N1_BenchRepairTurned.png`（台上维修、转动后）、`Native/p1_carry_to_bench.png`、`Native/p1_inspection_scanned.png`（Player 里台上扫描）。

## 没做 / 没验证

- 修好后物件仍是直接出现在交件台（没有改成“从维修台拿到交件台”）；需要的话可以按同样方式做。
- 从行走切到台上维修是直接切镜头，没有过渡动画。
- 维修界面左上角“离开维修台 / 主菜单”按钮压住原型界面标题开头——按钮位置是原来就有的，这次只改了文字。
- 真人手感（拿着物件的位置、台上镜头角度）没人试过。
