# 叙事竖切：工人义手

原创设定（北坡港务、港区装卸工罗亚等均为本项目虚构），不使用任何其他作品的名称、素材或设定。占位美术由 Unity 基本几何体搭建，不是最终美术。

## 怎么打开

1. 在 Unity 6000.0.84f1 中打开项目。
2. 菜单 **Border Repair > Narrative > Open Worker Hand Scene**。场景不存在时，先执行 **Build Worker Hand Slice (create missing)**。
3. 按 Play。

默认原型场景（`RepairStation_Prototype.unity`）和它的三件物品、顺序都没有改动；新案件只在 `Narrative_WorkerHand.unity` 与 `Shift_Narrative_WorkerHand.asset` 中。

## 玩法

- **接收** → 阅读顾客自述。顾客只说“会突然攥紧，别声张”，不透露真相。
- **检查**：左侧工具栏选择工具，单击零件使用。
  - 空手：开启扫描（空格）后单击，查看外观。扫描外壳解锁**线索 1：外壳过度磨损**。
  - 螺丝刀（卸下固定件）：卸下螺丝 A、B，螺丝移到零件盘。螺丝 A 被租赁封条压住上半边，卸下时封条撕开。
  - 撬片（打开外壳）：两颗螺丝都卸下后才能撬开盖板，盖板移到零件盘，露出内部零件。
  - 检测仪（检测 / 更换模块）：检测限力传感器，解锁**线索 2：限力器被关闭**（红灯：旁路）；之后从数据接口读取日志，解锁**线索 3：参数由雇主远程修改**（工具栏里显示备注“三组装卸节拍提升”）。传动机构可以先检测、再更换；控制板检测结果为“原厂固件”。
- **整理结论**：三条线索都找到后，直接进入处理决定（叙事案件不做对错诊断）。
- **决定**：三个结局，各有代价，没有对错：
  - 恢复出厂限力，锁定远程写入
  - 只修磨损件，保留公司参数
  - 不改参数，把日志副本交给罗亚
- **结果 / 总结**：结果页显示“结局 · …”和代价；总结页把本单列为“叙事案件”，**不计入“判断正确 X/Y”**。

## 设计：对原有系统的最小扩展

| 位置 | 扩展 | 对旧案件的影响 |
| --- | --- | --- |
| `RepairCaseData` | 新增 `scoring`（`Judged` / `Narrative`，默认 `Judged`）、`clues`、`repairSteps`、`endings`；检查点新增 `unlocksClueId`、`requiresStepId`、`blockedFinding`；叙事案件走单独的校验分支 | 旧资产反序列化后默认为 Judged，新列表为空，校验逻辑与原来相同 |
| `RepairSession` | 新增 `PerformAction(动作, 部位)`、线索解锁、`SubmitEnding`、`JudgedCaseCount`；`TryBeginDiagnosis` 在没有诊断选项时直接进入决定阶段；`SubmitDecision` 拒绝叙事案件 | 阶段枚举和原有方法都不变；判断类案件仍经过诊断阶段 |
| `CaseRecord` | 新增 `Ending`、`Clues`、`IsJudged` | `DecisionCorrect` 的定义不变 |
| `RepairStationController` | 可选的 `toolbar` 字段、工具选择、零件状态表现、叙事结果页和总结页 | 默认场景的 `toolbar` 为空；总结页在全是判断类案件时输出与原来相同（有回归测试） |
| `RepairUIView` | 追加 `SetEndingOptions`、`ShowNarrativeResult`、`SetHint` 等；进度标签可以显示“线索” | 保留了开发者此前加入的“换件时清除提示” |
| `ItemScanner` | 新增 `PickPoint`（测试用）、`ShowScannedTint`（默认开启） | 默认行为不变 |
| 新增 `RepairPart` | 零件状态表现：Removed / Open 时移到 `snapTarget`（吸附位置），Replaced / Torn / Tested 时切换外观；不使用物理 | — |
| 新增 `RepairToolbarView` | 三件工具加“空手” | 只在叙事场景中存在 |

### 以后接 VR 手柄

规则都集中在 `RepairSession.PerformAction(RepairActionType, pointId)`；零件状态是明确的枚举，吸附位置是 prefab 里的空物体（`SnapTargets/*`）。接 VR 时只需要：抓取工具 → 工具碰到零件 → 调用同一个 `PerformAction`，`RepairPart` 会按同样的方式移动到吸附位置。也可以反过来：把零件拖进吸附区时再调用 `PerformAction`。鼠标和 VR 共用规则，不需要改动会话逻辑。

### 稳定 ID

检查点 / 零件：`shell`、`lease_seal`、`fastener_a`、`fastener_b`、`drive`、`force_limiter`、`control_board`、`data_port`
线索：`overwork_wear`、`limiter_disabled`、`remote_params`
步骤：`remove_fastener_a`、`remove_fastener_b`、`open_housing`、`test_drive`、`replace_drive`、`test_limiter`、`test_board`、`read_log`
结局：`restore_limits`、`keep_params`、`give_log`

## 打磨（2026-09-24）

只动这一单：叙事场景 `Narrative_WorkerHand.unity` 里的 `NarrativePolish` 物体、界面上的两个收起按钮、工具栏布局，以及这件占位 prefab 的封条印字。没有增加案件、结局、shader 或 VR 功能；默认三件物品场景、正式 prefab 不变（有测试检查）。菜单 **Border Repair > Narrative > Apply Worker Hand Polish** 只补缺失的部分；**Reapply …** 重建打磨物体。

| 方面 | 做法 | 在哪里调 |
| --- | --- | --- |
| 分阶段取景 | 拿着工具时，镜头对准这件工具下一步要处理的零件（优先会解锁线索的步骤：限力器 → 日志）；完成一步后先在该零件上停 1.1 秒让反馈看得见，再转向下一个；空手 / 其他阶段看整件物品。R 回到当前取景的默认角度。实现是 `ItemInspector` 新增的可选焦点（`Focus` / `ClearFocus`），默认场景从不调用，镜头行为不变 | `NarrativePolish/StageFraming`：每个零件的框选范围、角度、占画面比例 |
| 面板遮挡 | 顾客说明在接收后自动收起（只留标题栏，可再展开）；线索记录在检查阶段收起，只留“扫描 / 线索 n/3 / 瞄准”一行，有新线索时按钮显示“● 新线索”，按钮或 Tab 随时展开；工具栏由 440×380 缩到 300×350；底部提示加了深色底 | `CustomerPanel`、`FindingsPanel` 上的 `CollapsiblePanel`；`NarrativePolish/NarrativeHud` |
| 操作反馈 | 卸螺丝：螺丝先转 3 圈退出再落盘，棘轮声 + 白环；撕开封条追加撕纸声 + 红环。开盖：盖板先弹起翘起再落盘，闷响 + 刮擦。探测限力器：红灯闪烁后常亮，下行警告双音 + 红环。读取日志：检测插头闪烁，数据啁啾 + 完成音 + 三道琥珀色环。操作被拒绝：低频蜂鸣。声音都是运行时代码合成的短音（`RepairSoundSynth`），不依赖音频素材；动作没有物理，只是表现 | `NarrativePolish/RepairFeedbackFx`：步骤 → 反馈的对应、音量 |
| 材质 | 场景级替换为 ShaderLab 的三种磨损材质（浅灰塑料 → 旧白陶瓷，浅色金属盖板 → 烤漆金属，深色金属 → 裸露金属），只改场景里实例的材质槽，prefab 仍引用占位材质 | `NarrativePolish/ItemMaterialSkin` |
| 照明 | 中性白聚光（主光）+ 偏冷弱点光（补光）+ 实时反射探针（让金属度高的螺丝不发黑）；场景里的暖色台灯从 1.2 调到 0.6 | `NarrativePolish/BenchLighting` |
| 封条印字 | 原来用一根灰条代表文字；现在是“NSP PORT LEASED / VOID IF OPENED”印在封条上（编辑器里渲染成贴图，URP Lit 面片，接受光照、被正常遮挡）；撕开后左半边只剩“NSP PO / VOID I” | `Art/Narrative/T_SealLabel.png`、`M_SealLabel*.mat` |

## 重新生成与测试

- 双击 `ArtSource/Narrative/run_unity.bat`，依次执行：生成（只补缺失）→ 应用打磨 → EditMode → PlayMode → 截图。任一步失败即停止并返回非零退出码；每步前删除旧的结果 XML。参数：`rebuild` 覆盖本竖切生成的资产；`repolish` 只重建打磨物体；`nopause` 不在结尾暂停。日志在 `ArtSource/Narrative/unity_logs/`。
- 打磨后的阶段截图：`Docs/Narrative/Screenshots/20260924_WorkerHandPolish_*.png`（1920×1080，游戏自己的镜头，测试代码不手动旋转或缩放），附带数值在 `Docs/Narrative/polish_batchmode_metrics.txt`（取景位置、面板遮挡、同一机位下工作台照明开 / 关的亮度与对比度）。打磨前的旧截图 `20260924_WorkerHand_*.png` 保留作对照（当时的截图测试手动缩放过镜头）。
- 这些截图都由 Explicit 测试 `WorkerHandCaptureTests` 在 batchmode 下用代码驱动生成，**不是试玩截图**；声音在 batchmode 下无法验证是否好听、好辨认，只测了“播放了哪一个”和音频特征差异。
