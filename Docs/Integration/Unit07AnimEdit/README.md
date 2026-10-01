# UNIT 07 · Unity 内手动编辑动画工作流

- **分支**：`integ/unit07-anim-edit`，从 `integ/unit07-first-order`（`8de5a69`）开出，独立工作区 `BorderRepairStation_anim`。
- **没有合并**：没有合并到 `main`，也没有合入 workorder06。
- **没有改动的文件**：
  - RobotV4 的 FBX（`robot-final.fbx`，MD5 `80b94f89…`）、`.blend` / `ArtSource`；
  - 主工作区的未提交文件；
  - 正式场景，以及原控制器 `RobotV4_Acceptance.controller`、`UNIT07_RobotV4_Dock.controller`；
  - 七号停靠预制体。
- **给非程序人员的图文说明**：[GUIDE.md](GUIDE.md)。
- **与 workorder06 集成时的迁移清单**：[MIGRATION_workorder06.md](MIGRATION_workorder06.md)。

## 1. 审计结果（改动前）

完整报告见 [audit_before.md](audit_before.md)（改动前），以及 [audit.md](audit.md)（改动后，同一工具重新生成）。

| 类别 | 动作 | 原来的时长 / 缓动来自 |
|---|---|---|
| RobotV4 FBX 骨骼动作 | 9 个：`Arm_Deploy_L/R`、`Gripper_OpenClose_L/R`（循环）、`Idle_Hover`（循环）、`Pose_Gripper_Closed/Half/Open`（静态姿态）、`Repair_Reach`；Generic 骨架，每个片段 19 块骨骼 × 10 条曲线 = 190 条，30 帧 / 秒逐帧烘焙 | FBX 子资源，只读，只能回 Blender 改 |
| 维修座 / 转子 | 夹具 0.6 s 线性、落座 1.2 s SmoothStep、手柄 0.3 s 线性、落座过渡 0.15 s（写死）、转子断电 2.5 s / 通电 1.0 s 匀变速 | 组件字段 + 代码里的缓动 |
| 首单 / 工作台占位移动 | 锁扣、上盖、轴承每段 0.45 s SmoothStep；**原型离座 1.2 s SmoothStep + 0.2 s Animator 过渡（写死）**；工作台每段 0.35 s SmoothStep | 组件字段 + 代码 |

**发现的问题**：七号的两根转子骨骼有两个写入者。所有 FBX 片段都绑定了转子骨骼，落座后 `RotorPowerDriver` 又在 LateUpdate 里覆盖它们：Animator 每帧先写，脚本后写。其它脚本移动的对象（七号根节点、锁扣、上盖总成及成员、轴承、维修座夹具 / 手柄、工作台物件）都没有被片段绑定，维修座和工作台上也没有 Animator。

## 2. 做了什么

### 2.1 FBX 动作 → 可写的 .anim 副本

- **一次性生成入口**：菜单 **Border Repair → Unit07 动画 → 1. 生成可编辑动画与动作配置（已存在的不覆盖）**。命令行入口：`-executeMethod BorderRepair.Motion.EditorTools.Unit07EditableAnimation.EnsureAllBatch`。
- **复制内容**：`Assets/BorderRepair/Animation/Unit07/Clips/` 下 9 个 `.anim`。全部浮点曲线逐条复制（骨骼路径、属性名、关键帧值和切线都不变），同时复制循环等片段设置、帧率、事件。四元数旋转保持四元数，不转成欧拉角。
- **出处记录**：每个副本的 `.meta` 里记录来源 FBX、片段名，以及复制时 FBX 片段和副本的内容指纹。菜单“查看可编辑片段状态”据此判断两件事：副本有没有被手调过，FBX 有没有更新过。
- **不覆盖**：已经存在的文件一律跳过。重复运行、重新导入 FBX、重新构建场景，都不会改动手调过的 `.anim` 或配置。
- **恢复原版**：只能通过菜单“把选中的动画片段恢复为 FBX 原版（先备份）”。恢复前先备份到 `Backups/时间戳/`；恢复是就地替换内容，文件和 GUID 不变，控制器引用不会断。
- **静态姿态**：FBX 导入设置和 `RobotV4ModelPostprocessor` 都没动。副本 `Idle_Hover` 第 0 帧与模型静态姿态一致（测试核对）。

### 2.2 可编辑控制器（只给测试场景用）

`Controllers/UNIT07_RobotV4_Editable.controller`：

- **Base Layer**：9 个状态，名字和原控制器相同，引用 `.anim` 副本，默认 `Idle_Hover`。遮罩去掉两根转子骨骼。
- **Rotors 层**：遮罩只含转子骨骼。悬停时播 `Rotor_Spin`（`Idle_Hover` 副本里的转子转动）。落座后播 `Rotor_Angle`：生成的辅助片段，1 秒 = 绕实测转轴转 360°，速度为 0。`RotorPowerDriver` 只计算转角，每帧在 Animator 求值前用 `Animator.Play` 定位到这个时刻。

**为什么这样做**：我先试了“接管时让 Animator 不写转子”（空状态 + Write Defaults 关、层权重 0、只用遮罩），四种结构都失败了，Animator 仍把转子改写约 160°。实测结论：Generic 骨架下，只要控制器里任何片段绑定了某块骨骼，Animator 每帧都会写它。所以改为脚本不写、只由 Animator 写。原控制器（没有 Rotors 层）下，`RotorPowerDriver` 保持原来的直接写法，没有接这套资产的场景行为不变。

**哪些场景用它**：

- 首单测试场景、维修座测试场景：七号用这个控制器，是场景实例上的覆盖，预制体不改。
- 原验收控制器和七号停靠预制体：都没有改。

### 2.3 脚本动作 → 配置资产 + AnimationCurve

`Animation/Unit07/Motion/` 下 4 个配置资产，在 Inspector 里改时长和曲线。齿轮菜单里的 Reset 恢复默认。默认值与原代码完全一致：

| 配置 | 字段（默认） | 用在 |
|---|---|---|
| `UNIT07_DockMotion` | 夹具 0.6 s 线性、落座 1.2 s 缓入缓出、手柄 0.3 s 线性、落座过渡 0.15 s | `Unit07DockController` |
| `UNIT07_RotorMotion` | 断电 2.5 s、通电 1.0 s，转速线性变化 | `RotorPowerDriver` |
| `UNIT07_FirstOrderMotion` | 每段移动 0.45 s 缓入缓出、**原型离座** 1.2 s 缓入缓出、离座过渡 0.2 s | `FirstOrderFlow` |
| `WB_PlaceholderMotion` | 每段移动 0.35 s 缓入缓出 | `WbPlaceholderDemo` |

- **曲线含义**：横轴是时间进度，纵轴是动作完成度。纵轴超出 0–1 的部分会截断，所以零件只在原来的起点和终点之间走，碰撞路径不变。
- **默认曲线**：线性曲线 = `MoveTowards` 匀速；缓入缓出曲线是两端切线为 0 的三次曲线，与 `Mathf.SmoothStep` 公式相同。
- **中途反向**：夹具、手柄反向时沿同一条曲线倒着走。转子从当前转速反查新曲线上的进度后继续，不跳变。
- **状态判断、拒绝逻辑、几何**：状态判断和错误操作的拒绝逻辑一行没改。路径几何（张开角度、悬停高度、抬起距离）也没放进配置。
- **没接配置时**：组件用与原代码相同的内置默认值。

### 2.4 不进 Play 的预览

菜单 **Border Repair → Unit07 动画 → 2. 动作预览（不进 Play）**。

- **能预览的动作**：维修座夹具、落座、手柄；转子减速 / 加速；首单离座、左上盖抬起、外侧锁扣；工作台托盘；任意 `.anim` 片段。
- **用法**：拖进度滑杆或播放一遍。
- **还原机制**：用 Unity 的 AnimationMode 临时驱动属性，不会保存到场景或预制体。结束时再按序列化数据逐位核对、写回。结束预览、关闭窗口、进入 Play、保存或关闭场景、脚本重新编译时都会自动还原。
- **和运行时一致**：姿态由运行时组件自己的方法计算（`PoseClamps` / `PoseDescend` / `PoseLever` / `PoseLiftOff` / `PoseSegment` / `RotorPowerDriver.AngleAfter`），预览和运行用的是同一段代码。

### 2.5 场景构建脚本只引用持久资产

| 场景 | 处理 |
|---|---|
| 首单测试场景 | `FirstOrderSceneBuilder.Build` 先 `EnsureAll()`，只补缺，再引用资产。场景已用它重建 |
| 维修座测试场景 | `Unit07DockBuilder` 已接上资产。这次没有整体重建（整体重建会重新生成预制体和原控制器），只用菜单“给现有维修座、工作台测试场景接上可编辑资产”改了引用：+10 / −4 行 |
| 工作台测试场景 | `WorkbenchAreaBuilder` 已接上资产。同样只改了引用：+1 / −1 行 |

## 3. 文件清单

**新增**：

- `Assets/BorderRepair/Motion/`（新目录）：
  - 运行时程序集 `BorderRepair.Motion`：`MotionTiming.cs`。
  - 编辑器程序集 `BorderRepair.Motion.Editor`：
    - `Unit07EditableAnimation.cs`：生成、恢复、状态、接场景。
    - `Unit07MotionPreview.cs`：预览窗口。
    - `Unit07AnimAudit.cs`：审计。
    - `Unit07AnimGuideCapture.cs`：说明截图。截图只截 Unity 窗口自身的画面，不读屏幕。
  - 测试 `Tests/EditMode/Unit07AnimEditEditModeTests.cs`、`Tests/PlayMode/Unit07AnimEditPlayModeTests.cs`。
- 配置类：
  - `Scripts/Runtime/Dock/Unit07DockMotionConfig.cs`、`Unit07RotorMotionConfig.cs`
  - `FirstOrder/Runtime/FirstOrderMotionConfig.cs`
  - `WorkbenchArea/Scripts/Runtime/WbPlaceholderMotionConfig.cs`
- `Assets/BorderRepair/Animation/Unit07/`：
  - `Clips/` 9 个 `.anim`（文本格式约 27 MB：保留了 FBX 的逐帧关键帧）；
  - `Controllers/` 里的可编辑控制器、2 个遮罩、`UNIT07_RotorAngle_Generated.anim`；
  - `Motion/` 4 个配置。
- 文档：`Docs/Integration/Unit07AnimEdit/` 下的 `README.md`、`GUIDE.md`、`Guide/*.png`（10 张）、`audit_before.md`、`audit.md`、`MIGRATION_workorder06.md`。

**修改**：

| 文件 | 改动 |
|---|---|
| `Unit07DockController.cs` | 时长和缓动改读配置；新增 `Initialize` / `ResetToHover` / `Tick` 和 `PoseXxx` 方法。状态机和拒绝逻辑不变 |
| `RotorPowerDriver.cs` | 曲线驱动；可编辑控制器下经 Animator 写转子 |
| `FirstOrderFlow.cs` | 零件移动、离座改读配置；新增 `PoseLiftOff` |
| `WbPlaceholderDemo.cs` | 每段移动改读配置；新增 `PoseSegment` |
| 三个场景构建脚本 | 引用持久资产 |
| 三个测试场景 | 改引用，见 2.5 |
| 12 个 `.asmdef` | 加上对 `BorderRepair.Motion`（或其编辑器程序集）的引用 |
| `Docs/Integration/Unit07FirstOrder/build_log.txt` | 重建首单场景时自动更新 |

## 4. 测试结果

Unity 6000.0.84f1，批处理模式，2026-10-02，最终代码。

| 测试 | 内容 | 结果 |
|---|---|---|
| `Unit07AnimEditEditModeTests`（11 项） | 见下文“EditMode 测试明细” | 11/11 |
| `Unit07AnimEditPlayModeTests`（4 项） | 见下文“PlayMode 测试明细” | 4/4 |
| 首单原有测试 `FirstOrderPlayTests` | 32 步、拆下路径碰撞几何、控制台无错误 | 通过 |
| 新测试 + 首单 + 维修座 PlayMode 测试连跑 3 次 | — | 3 次都是 11/11 |
| 全项目回归 | EditMode 全部；PlayMode 全部 | 见下文“全项目回归” |

**全项目回归**：

| | 结果 | 说明 |
|---|---|---|
| EditMode | **94/94** | 原有 83 + 新增 11 |
| PlayMode | **67 通过、0 失败、7 跳过** | 原有 63 + 新增 4；跳过的是原有的、需要手动开启的截图测试 |

**EditMode 测试明细**：

- **曲线公式**：默认曲线 = `MoveTowards` / `SmoothStep` 公式；曲线越界会被截断。
- **片段副本**：9 个副本是可写的独立 `.anim`。绑定、每条曲线的每个关键帧（值、切线）、循环、首尾时间、帧率、事件都与 FBX 一致。按 1/90 s 采样，全部骨骼的姿态一致（位置 < 1e-5 m，旋转 < 0.01°）。静态姿态一致。
- **控制器**：可编辑控制器只引用副本，遮罩和转角片段正确（0–359° 抽查 < 0.01°）。原控制器、停靠预制体没改。
- **持久性**：手调一个片段和一个配置之后，以下三种情况手调都还在，而且 FBX 原片段指纹不变：
  - 重复生成（不新建任何文件）；
  - 重新导入 FBX；
  - 重新构建首单场景（构建到临时路径，场景引用的是手调后的配置）。
- **恢复原版**：先备份，恢复后与 FBX 一致，GUID 不变。
- **默认配置逐帧一致**：完整流程（张开 → 落座 → 夹紧 → 断电 → 停转 → 通电），每帧把夹具角度、七号高度、手柄角度、转子转速与原代码公式对比。误差：角度 < 0.01°、高度 < 1e-5 m、转速 < 0.05°/s。
- **改配置后生效**：改了配置后时长和曲线生效（±2 帧），错误操作照样被拒绝，叶轮减速中仍禁止检查。
- **预览**：首单场景里 9 种预览逐个开始、摆姿态、结束：
  - 所有 Transform 逐位还原；
  - 场景没有被标记为已修改；
  - 预制体覆盖数不变；
  - 同一时刻预览姿态 = 运行姿态；
  - 工作台场景同样核对过。

**PlayMode 测试明细**：

- **转子只有一个写入者**：悬停时 Animator 转、脚本不写；落座后脚本不写、Animator 写。接管时不跳变，通电仍是 360°/s，停转后姿态精确；断电时夹爪动画照常、转子保持静止。
- **对照**：换回原控制器后，同样的检查能测出两个写入者，说明上面的检查有效。
- **首单 32 步**：用可编辑资产走完 32 步，全部通过，复测通过，离座后转子交还 `Rotor_Spin`。
- **改离座曲线后**：运行时的上浮高度逐帧落在曲线范围内（容差两帧），Animator 过渡时长按配置。

**复现**：

```
Unity.exe -batchmode -projectPath <工作区> -runTests -testPlatform EditMode
Unity.exe -batchmode -projectPath <工作区> -runTests -testPlatform PlayMode
```

## 5. 已知限制、需要人工确认的项目

- **副本是逐帧烘焙的关键帧**：FBX 导出时每帧一个关键帧，副本照原样保留，没有精简，所以改某一段时要按说明“先删中间帧再设关键帧”。是否要提供“精简关键帧”的工具，需要另行确认（精简会改变曲线，不符合这次“保留曲线”的要求）。
- **转子转动只来自 Idle_Hover**：可编辑控制器里，悬停转子的转动只来自 `Idle_Hover`。在其它片段里改转子关键帧不生效（说明里已写明）。
- **真人操作没有验收**：说明里的 Unity 操作步骤（曲线编辑弹窗、录制关键帧、Dopesheet 缩放、Reset、恢复菜单）都是 Unity 的标准操作，但没有由非程序人员实际走一遍。预览窗口、恢复菜单、状态菜单的逻辑有自动测试；界面按钮本身没有真人点过。
- **截图**：截图是程序摆好界面后截取的 Unity 窗口画面。Project 窗口截图里文件夹路径是对的。
- **维修座测试场景**：只改了引用、没有整体重建；用构建脚本整体重建时会同时重新生成停靠预制体和原控制器（这是原有行为）。
- **与 workorder06 合并**：需要手工迁移，见 `MIGRATION_workorder06.md`。
