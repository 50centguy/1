# 两晚切片 · 七号左引擎维修核心（鼠标可操作）

- **分支 / 工作树**：
  - 分支 `integ/two-night-slice`，从 `art/unit07-fault-kit-dust-fibers`（`c60b768`）开出；
  - 干净的独立工作树：`C:\Users\Administrator\UnityProjects\BorderRepairStation_slice`。
- **开工前核查**：
  - 远端没有新提交；没有已有的 `two-night-slice` 或同等集成分支，所以新建了这一个。
  - 没有正在运行的 Unity 编辑器。
- **没碰的工作区**：
  - 共享主工作区（`robot-v4-import`，有未提交内容）没有切换、没有改动；
  - `BorderRepairStation_unit07int` 有 8 个未提交改动，没有碰；
  - 其它工作树都没有碰。
- **复用，没有重做**：A/B 布局（用 B）、故障美术包、灯光 QA（反射探针、两盏补光、FXAA，仍是待美术签收的测试设置）、积尘和纤维修整、翻盖 / 清理 / 取放 / 搬运表现。
- **验证范围**：
  - 只做了程序鼠标 / 键盘事件，**不是真人试玩**；真人鼠标试玩由你验收。
  - **VR、目标硬件性能均未验证。**

## 1. 运行入口

| 项目 | 内容 |
|---|---|
| exe | `C:\Users\Administrator\UnityProjects\BorderRepairStation_slice\Builds\TwoNightSlice\TwoNightSlice.exe`（Windows 64 位，窗口 1600×900，可调大小；`Builds/` 不进 git） |
| 场景 | `Assets/BorderRepair/FirstOrder/Scenes/Slice/TwoNightSlice.unity`：布局 B 场景的副本，加上鼠标界面、EventSystem、Player 自检。原场景（布局 A/B、正式首单测试场景）都不改，留作对照 |
| 构建 | `TwoNightSlice.BuildWindows`。只用写明的场景列表 `BuildScenes = { TwoNightSlice.unity }`，不读 EditorBuildSettings——那里还是旧三物件原型和 SampleScene，不会被打进包 |
| 重新生成场景 | 菜单 `Border Repair/Two-Night Slice/Build Slice Scene`，或批处理 `-executeMethod BorderRepair.FirstOrder.EditorTools.Slice.TwoNightSlice.BuildScene` |
| 重新构建 | 菜单 `Border Repair/Two-Night Slice/Build Windows Player`，或批处理 `-executeMethod BorderRepair.FirstOrder.EditorTools.Slice.TwoNightSlice.BuildWindows`；记录写到 `build_report.txt` |
| 自检 | `TwoNightSlice.exe -sliceSelfCheck <输出目录>`。不带这个参数时自检组件什么都不做 |

## 2. 玩法（全部可用鼠标完成）

| 操作 | 怎么做 |
|---|---|
| 维修动作 | 左键点 3D 对象。例如夹具握把：落座 / 夹紧 / 松开；断电开关；锁扣、上盖、进气口、轴承、工作台落点 |
| 观察 | 右键点 3D 对象，或在右下角切到“观察”后用左键。观察只读状态，不推进步骤 |
| 镜头 | 底部镜头栏：维修座、左引擎、左引擎背面、近看、右引擎、工作台、保养记录、新旧轴承、总览；“← 返回”回到上一个镜头（流程自动切的镜头也算） |
| 手册 | 右下“手册”：安全规则、诊断顺序、步骤、鼠标说明、诊断记录（5 项观察，已看 / 未看），以及备注输入框 |
| 反馈 | 左上：步骤、维修座状态、镜头、下一步提示；下面一条是反馈，被拒绝时是暗琥珀色并以“不行：”开头，说明原因，可以继续操作 |
| 悬停 | 3D 对象：鼠标旁显示名称和“左键：操作 · 右键：观察”；按钮：提亮。鼠标在界面上时不悬停、不点 3D |
| 调试（可选） | 数字键 1–9 切镜头；F3 或“调试”按钮开关调试 HUD（默认关）。文字输入框有焦点时都不响应 |

**右侧对照和误拆右侧**（反馈不同）：

- 观察右引擎：基准说明。转动中只能看外观；停转后可以手转叶轮，顺滑、无异响，并和左侧对比。
- 左键点右引擎：拒绝，写“误拆右侧：右引擎是这单的正常对照……右引擎不拆，继续处理左侧”。

**有效观察距离**（镜头到目标包围盒最近点）：

| 观察对象 | 要在多近以内 |
|---|---|
| 进气口 / 右引擎 | 0.9 m |
| 上盖外观 | 1.2 m |
| 轴承原位 | 0.45 m |
| 保养记录 | 0.40 m |
| 新旧对比 | 0.65 m |

- 太远时只显示“看不清”，并给一个“切到「近看」”之类的按钮。
- 实测：左引擎镜头离轴承 0.67 m，看不清；近看 0.29 m，看得清。

**未停转时**：

- 检查 / 拆卸都被拒绝，并说明原因：“七号还在供电……”；减速中写“已断电，但叶轮还在减速转动（xx°/s）”。
- 被拒绝的操作不改变任何状态，可以继续。

## 3. 改了什么

| 文件 | 改动 |
|---|---|
| `FirstOrder/Runtime/FirstOrderInput.cs` | 点 3D 前先做一次界面射线：鼠标在 UGUI 上就不悬停、不点击。文字输入框有焦点时不响应数字键。右键 / 观察模式发出 `Observed` 事件。调试 HUD 可开关、可设字体和位置。数字键改成可关的 `debugKeys`（默认开） |
| `FirstOrder/Runtime/FirstOrderFlow.cs` | **只改提示文字**：<br>• “按 3 / 7 / 8”改成镜头按钮名；<br>• 误拆右侧的拒绝说明右侧是正常对照；<br>• 减速中的拒绝写明叶轮在减速。<br>步骤、门禁、供电逻辑没改 |
| `FirstOrder/Runtime/Slice/SliceView.cs`（新） | UGUI 界面，和项目维修台界面同一套：legacy `Text` + `Button` + `InputSystemUIInputModule`。只调已有入口：`Rig.Go`、`input.ObserveMode`；维修动作仍由 3D 点选交给 `FirstOrderFlow` |
| `FirstOrder/Runtime/Slice/SliceObservation.cs`（新） | 观察文字和有效距离。只读 `FirstOrderFlow` / 维修座状态，**不是**第二个状态机：不推进步骤、不动供电、不切镜头 |
| `FirstOrder/Runtime/Slice/SlicePlayerSelfCheck.cs`（新） | 独立 Player 自检（见第 4 节） |
| `FirstOrder/Editor/Slice/TwoNightSlice.cs`（新） | 生成切片场景、构建 exe；构建时临时设成窗口 1600×900，构建后恢复项目设置 |
| `Art/Fonts/NotoSansSC/`（新） | 思源黑体 Noto Sans SC Regular（SIL OFL 1.1，许可证 `OFL.txt` 一起放着，可随游戏分发）。动态字体，字体数据打进包，不依赖玩家电脑上装的字体 |
| asmdef | FirstOrder 运行时 / 编辑器 / 测试加了 `UnityEngine.UI` 引用 |
| 测试 | 新增 `SlicePlayTests`（PlayMode）、`SliceSceneTests`（EditMode）；`FirstOrderPlayTests` 加了切片场景这一组 |

## 4. 验证

结果见第 5 节（构建、自检、回归的数字在那里更新）。

- `mouse_path.md`：只用虚拟鼠标走完维修核心，镜头只经镜头栏切换。
- `ui_input_guard.md`：
  - 点手册面板，后面的维修座夹具握把左键、右键都不响应；
  - 备注框有焦点时数字键 2、F3 不生效；
  - 关掉以后数字键可用，调试按钮能开关 HUD。
- `PlayerSelfCheck/`：独立 exe 里的自检记录 `selfcheck.txt` 和截图（`ScreenCapture`，只截游戏自己的画面，不截桌面）。

## 5. 结果

| 项目 | 结果 |
|---|---|
| Windows 构建 | 成功，158.9 MB，0 错误 0 警告；包里只有 1 个场景（`build_report.txt`） |
| 独立 Player 自检（1600×900，RTX 3070） | 全部通过：<br>• 每个时刻（启动、手册、观察面板、流程结束）显示的文字都用打包字体（`Noto Sans SC`）；<br>• 没有缺字形的字符，也没有被截掉 / 没显示出来的文字；<br>• 完整首单 45 步（程序点击）0 失败，复测通过（占位判定）；<br>• 共 12 张截图 |
| 只用鼠标的维修核心（编辑器 PlayMode，虚拟鼠标） | 37 条记录全部通过，诊断记录 5/5（`mouse_path.md`） |
| 界面不穿透 / 文字输入屏蔽快捷键 | 通过（`ui_input_guard.md`） |
| EditMode | 108/108（原 104 + 切片 4） |
| PlayMode | 90 通过、0 失败、7 跳过（跳过的是原有的手动测试）。包括：<br>• 正式首单、布局 A、布局 B、切片 四个场景各一组原有流程测试；<br>• 两个切片鼠标测试 |
| 原场景 | 正式首单测试场景、布局 A/B 场景文件都没改；RobotV4 FBX 没改 |

**自检中修过的问题**：

- 状态栏标题第一次构建时没显示：思源黑体行高大，`VerticalWrapMode.Truncate` 会把放不下整行的文字整行藏掉。已改成 Overflow，自检也加了“被截掉 / 没显示”的检查。
- 离座后状态栏显示“已落座，夹具张开”：改成按流程步骤显示“七号离座悬停”。

## 6. 未验证 / 待确认

- **真人鼠标试玩**：没做，由你验收。以上都是程序生成的鼠标 / 键盘事件。
- **文字输入**：备注框的焦点和快捷键屏蔽测过了。**真键盘打中文（输入法）没有在独立 Player 里试过**，程序只直接设过文字。
- **VR、目标硬件性能**：未验证。
- **离座**：当前维修座没有“离座”状态，`FirstOrderFlow` 自己移动七号；界面在这一步按流程显示“七号离座悬停”。见 `workorder06_migration.md` 第 4 节第 1 条。
- **美术签收**：补光、反射探针、FXAA 仍是测试设置。
- **工单 06**：只读比较，见 `workorder06_migration.md`；本次没有合入。
