# UNIT 07 维修座 · 第二阶段：维修结束后离座

- **分支**：`integ/unit07-dock-phase1`，在第一阶段 `1e94395` 上继续。工作区开始时是干净的，只有 `ProjectSettings` 的换行差异，已还原。
- **没有合并**到 `main` 或主工作区。
- **不用、不改的部分**：
  - 没有改 RobotV4 的 FBX、`.blend`、源美术。
  - 主工作区里未提交的五个工单文件和 RobotV4 反馈文件，没有带入，也没有引用。
  - 没有接入“六号”的 `RobotRepairController` / `Plan` 等未提交代码，也没有假定六号和七号是同一台机器人。
  - 本阶段代码只针对 UNIT 07 维修座流程。

## 1. 流程

维修结束后，按下列顺序离座。每一步都要求上一步已完成。

| # | 阶段 | 玩家操作 | 状态（`DockState`） | 允许的条件 |
|---|---|---|---|---|
| 1 | 确认可以恢复供电 | 点断电开关 | `RotorsStopped` / `SpinningDown` → `SpinningUp` | “允许结束维修”接口（`IDockServiceCompletionGate`）返回可以；没有接入接口或返回不可以时拒绝，并显示接口给出的原因 |
| 2 | 转子回到正常转速 | （等待） | `SpinningUp` → `Clamped` | `RotorPowerDriver` 在 1 s 内把转速升回 360°/s；期间夹具保持锁紧，叶轮禁止操作 |
| 3 | 松开夹具 | 点夹具握把 | `Clamped` → `ClampsOpening` → `SeatedOpen` | 通电，且转子在悬停转速 |
| 4 | 七号升起离座 | 按 L（或调用 `Interact(DockAction.LiftOff)`） | `SeatedOpen` → `LiftingOff` | 夹具已松开、通电、转子在悬停转速。升起用 1.2 s，升到锚点上方 0.12 m（即落座前的悬停高度）；升起途中转子仍由维修座按悬停转速驱动 |
| 5 | 转子交还 Animator | （自动） | `LiftingOff` → `Undocked` | 到达悬停高度后，`RotorPowerDriver.Release()`，Animator 用 0.2 s 过渡到 `Idle_Hover`；之后转子和机身浮动都由 `Idle_Hover` 驱动 |

- **离座之后**：再点夹具握把，七号会重新落座（`Undocked` → `Descending` → `SeatedOpen`），转子重新由维修座接管。
- **没有断电维修时**：落座后直接松夹具、离座，不需要“允许结束维修”。这个接口只管“断电维修之后恢复供电”。
- **加速中再次断电**：允许，回到减速、停转。夹具仍然锁紧。

### 越序操作一律拒绝，并给出原因

- 断电维修中、减速中：松开夹具、离座都被拒绝；未经确认恢复供电也被拒绝。
- 转子加速中：松开夹具、离座都被拒绝。
- 还夹着：离座被拒绝。
- 夹具松开中、升起中：再点夹具、断电、再次离座都被拒绝。
- 已离座悬停：断电、离座、检查左引擎都被拒绝。

被拒绝的操作不改变状态、供电、夹具开度、机身高度和转子接管（EditMode 测试逐项核对）。

### 一致性

| 状态 | 手柄 | 状态灯 | 转子 | 机身 |
|---|---|---|---|---|
| 断电后 | OFF（`unity_off_deg`） | 琥珀色 | 减速到 0 | 落座 |
| 恢复供电后 | ON（`unity_on_deg`） | 绿色 | 加速到 360°/s | 落座 |
| 离座后 | ON | 绿色 | 由 `Idle_Hover` 驱动（实测 360°/s，姿态与片段当前帧一致） | 在悬停高度随 `Idle_Hover` 浮动，硬点板离开接触垫 |

## 2. 留给工单接入的接口

```csharp
namespace BorderRepair.Dock
{
    public interface IDockServiceCompletionGate
    {
        bool CanFinishService(out string reason);   // 不能结束时，reason 会显示给玩家
    }
}
// 接入：Unit07DockController.SetServiceCompletionGate(gate)
// 或在 Inspector 里把实现了接口的组件拖到 serviceGateBehaviour
// 查询：Unit07DockController.CanRestorePower(out reason)
```

- 维修座本身不判断维修内容。以后由工单 / 维修流程实现这个接口，例如“本单零件已装回、复查已通过”。
- 测试场景里接的是 `ManualServiceCompletionGate`。它是**占位**：没有工单，由测试者按 F 手动确认，场景对象名里写明“占位：手动确认，未接工单”。正式接入时应换成工单的实现，不要沿用它。
- 没有接入接口时（传 `null`），断电维修之后一律不允许恢复供电。

## 3. 修改清单

| 文件 | 改动 |
|---|---|
| `Scripts/Runtime/Dock/Unit07DockController.cs` | 见下文“控制器改动” |
| `Scripts/Runtime/Dock/RotorPowerDriver.cs` | `IsAtIdleSpeed`、`SpinUpSeconds`；`LateUpdate` 改为调用公开的 `Step(dt)`（行为不变，编辑模式测试可以按固定步长推进） |
| `Scripts/Runtime/Dock/DockInteractable.cs` | `DockAction.LiftOff = 6` |
| `Scripts/Runtime/Dock/IDockServiceCompletionGate.cs`（新） | “允许结束维修”接口 |
| `Scripts/Runtime/Dock/ManualServiceCompletionGate.cs`（新） | 测试场景的手动占位实现 |
| `Scripts/Runtime/Dock/Unit07DockInput.cs` | L = 升起离座，F = 占位手动确认；HUD 显示转速由谁驱动、接口状态 |
| `Scripts/Editor/Unit07Dock/Unit07DockBuilder.cs` | 测试场景接入手动占位接口；新增 `RebuildSceneOnly`（只重建场景，不重新生成预制体和材质，避免无关改动） |
| `Scripts/Editor/Unit07Dock/Unit07DockPlayCapture.cs` | 采集离座流程；结果写到 `Unit07Dock_Phase2/`，第一阶段的记录不覆盖 |
| `Scenes/Unit07Dock_Test.unity` | 由 `RebuildSceneOnly` 重建：多了 `ServiceCompletionGate (占位：手动确认，未接工单)` |
| `Tests/EditMode/Unit07DockUndockSequenceTests.cs`（新） | 7 项顺序测试，见第 4 节 |
| `Tests/PlayMode/Unit07DockPlayModeTests.cs` | 新增场景测试 `UndockAfterService_HandsRotorsBackToAnimator`；第一阶段测试的“恢复供电”一段改为先确认；修正夹爪同名查找（见下） |

**控制器改动**（`Unit07DockController.cs`）：

- 新状态 `SpinningUp`、`LiftingOff`、`Undocked`，数值接在后面，第一阶段的数值不变。
- 新方法与接口：`LiftOff()`、`SetServiceCompletionGate()`、`CanRestorePower()`。
- 恢复供电要过接口。
- `IsSeated` 改为明确列出落座状态，以前用 `>=` 判断，会把新增的升起中、已离座算进去。
- `Awake` / `Start` / `Update` 改为调用公开的 `Initialize` / `ResetToHover` / `Tick`，供编辑模式测试使用。

**对第一阶段行为的唯一改变**：断电之后（包括还在减速时）恢复供电，现在必须先通过“允许结束维修”接口。以前可以随时恢复。

**测试修正**：

- **夹爪同名查找**：模型里有两个 `Arm_R_JawUpper`（骨骼，以及骨骼下的同名网格），不排序的按名查找可能拿到网格，读到 0°。改为比较世界旋转。这个问题在首单集成分支里出现过：3 次失败 2 次。
- **转速测量**：离座后按约 1/30 s 的窗口测转速。逐帧累计在批处理模式下（每帧约 0.5 ms，转 0.2°）会低于 `Quaternion.Angle` 的判等阈值，被算成 0。

## 4. 测试结果

见第 5 节的实测记录。

| 测试 | 内容 | 结果 |
|---|---|---|
| `Unit07DockUndockSequenceTests`（EditMode，新，7 项） | 完整的维修 → 离座顺序，每一阶段核对手柄、状态灯、转子、机身高度；恢复供电必须过接口（未接入、未确认、自定义原因、允许）；减速中恢复供电也要过接口；11 个阶段 × 越序操作共 30 次，全部被拒绝、有原因、不改变状态；加速中再次断电；离座后重新落座；没有断电维修时直接离座 | 7/7 |
| `Unit07DockEditModeTests`（第一阶段，8 项） | 未改 | 8/8 |
| `Unit07DockPlayModeTests`（5 项，含新场景测试） | 新测试：维修后离座，交还后 RotorPowerDriver 不再覆盖；Animator 在 `Idle_Hover` 且不在过渡；左右转子由动画转动，实测转速在 360°/s ±15% 内；转子姿态与 `Idle_Hover` 当前帧一致（误差 < 8°）；机身在悬停高度、随动画浮动、离开接触垫；手柄 ON、灯绿色；离座后断电被拒绝 | 5/5，连跑 3 次都通过 |
| 全项目回归 | EditMode 全部；PlayMode 全部 | EditMode **72/72**（第一阶段 65 + 新增 7）；PlayMode **53 通过、0 失败、7 跳过**（第一阶段 52 + 新增 1；跳过的是原有的、需要手动开启的截图测试） |
| 带界面的 Play 采集（`Unit07Dock_Phase2/play_capture.txt`） | 越序操作逐条被拒绝；离座后实测转子 360°/s（Animator 驱动）；控制台警告 / 错误 0 条 | 通过 |

## 5. 实测记录

Unity 6000.0.84f1，批处理模式测试 + 带界面的编辑器 Play 采集（Game 视图 1471×886，D3D11，RTX 3070），2026-10-01。

| 记录 | 数值 |
|---|---|
| 断电 0.5 / 1.5 / 3.5 s 转速 | 288 / 144 / 0 °/s（与第一阶段一致） |
| 未确认就恢复供电 | 拒绝：“维修还没确认结束（测试场景：按 F 手动确认，占位）。” |
| 确认后恢复供电 0.4 s | 144 °/s，状态 `SpinningUp`；此时松夹具被拒绝：“转子还没回到正常转速，夹具保持锁紧。” |
| 恢复供电 1.4 s | 360 °/s，状态 `Clamped`；此时离座被拒绝：“夹具还夹着，先松开夹具。” |
| 松开夹具后 | `SeatedOpen`，机身仍落座 |
| 升起中 | 转子由维修座驱动，360 °/s |
| 离座后 | `Undocked`；Animator 在 `Idle_Hover`；转子接管 = 否；按骨骼实际旋转实测 **360 °/s** |
| 离座后断电 | 拒绝：“七号不在维修座上，不能断电。” |
| 控制台警告 / 错误 | 0 条 |

截图：`Screenshots/P10_power_on_spinning_up`、`P11_clamps_released_still_seated`、`P12_lifting_off`、`P13_undocked_animator_drives_rotors`、`P13b_game_view_hud_undocked`（带 HUD）。

## 6. 需要真人和设备验收的项目（没有验证）

- **真人鼠标操作**：没有做。测试和采集都是程序调用和程序点击。需要真人确认以下几点：
  - 点夹具握把松开、按 L 离座、按 F 确认这套操作是否顺手。
  - HUD 的提示和拒绝原因是否看得懂。
  - 0.2 s 的动画过渡、1.2 s 的升起时长是否自然。
  - 离座时转子从“维修座驱动”切回 `Idle_Hover` 的那一帧，转子角度会跳到片段当前帧的角度。两者转速相同，测试按姿态一致验证过，但肉眼能否察觉没有确认。
- **离座操作入口**：现在是键盘 L 或接口调用，没有对应的维修座部件或正式 UI。正式入口待设计确认。
- **VR 设备**：没有验证。包括用手扳开关手柄、握夹具握把、看着七号升起时的尺度和舒适度，以及头显上的帧时间。
- **目标硬件性能**：只在编辑器里测过。
- **工单接入**：接口已留好，但没有任何真实实现。“维修已结束”的判定条件要等工单 / 维修流程定义。
