# 与 workorder06 集成时要迁移的动作配置

本分支 `integ/unit07-anim-edit` 从 `integ/unit07-first-order`（`8de5a69`）开出，**没有**合入 `integ/unit07-dock-phase1` / `integ/unit07-workorder06`。两条线各自改了维修座和转子代码，将来合并时需要按下表手工迁移，不能直接用 Git 自动合并的结果。

workorder06 的数值来自对该分支 `9a8dcf9` 的只读核对。

## 1. 维修座 `Unit07DockController`

| workorder06 里的字段 / 写法 | 本分支对应 | 迁移做法 |
|---|---|---|
| `clampSeconds = 0.6`，`MoveTowards` 线性 | `Unit07DockMotionConfig.clamps`（0.6 s，线性） | 删字段，改读配置；`ApplyClamps` 改调 `PoseClamps(progress)` |
| `descendSeconds = 1.2`，`SmoothStep` | `Unit07DockMotionConfig.descend`（1.2 s，缓入缓出） | 同上，改调 `PoseDescend(progress)` |
| `leverSeconds = 0.3`，`MoveTowards` 线性 | `Unit07DockMotionConfig.lever`（0.3 s，线性） | 同上，改调 `PoseLever(progress)` |
| 落座后 `CrossFade(seatedStateName, 0.15f)` | `Unit07DockMotionConfig.seatedBlendSeconds`（0.15） | 改读配置 |
| **正式离座** `liftSeconds = 1.2`，`SmoothStep`（`LiftingOff` 状态） | 本分支没有（只有首单原型离座 `FirstOrderMotionConfig.liftOff`） | 在 `Unit07DockMotionConfig` 新增 `liftOff`（默认 1.2 s 缓入缓出），新增 `PoseLiftOff(progress)`；预览窗口的“七号离座”改为预览维修座的正式离座 |
| `handoverBlendSeconds = 0.2`（离座交还 Animator） | 本分支对应 `FirstOrderMotionConfig.liftBlendSeconds`（0.2，原型） | 在 `Unit07DockMotionConfig` 新增 `handoverBlendSeconds`（默认 0.2） |
| `Initialize()` / `ResetToHover()` / `Tick(dt)` | 本分支也加了同名方法（行为相同） | 合并时保留一份，注意 workorder06 版的 `Initialize` 还会读取“允许结束维修”接口 |

正式离座接入后，首单原型里的 `FirstOrderFlow.LiftOff` 和 `FirstOrderMotionConfig.liftOff` / `liftBlendSeconds` 应改为调用维修座的正式离座，或者保留但只给首单原型场景用，二选一，避免两处配置同一个动作。

## 2. 转子 `RotorPowerDriver`

| workorder06 | 本分支 | 迁移做法 |
|---|---|---|
| `spinDownSeconds = 2.5`、`spinUpSeconds = 1.0`，`MoveTowards` 匀加减速 | `Unit07RotorMotionConfig.spinDown` / `spinUp`（同值，线性） | 删字段，改读配置 |
| `Step(dt)`、`IsAtIdleSpeed`、`SpinUpSeconds` | 本分支同名，行为相同 | 保留一份 |
| `LateUpdate` 直接写转子骨骼 | 可编辑控制器下改为经 Animator 的 `Rotor_Angle` 状态写；原控制器下保持直接写 | 用本分支版本 |
| 第二阶段 PlayMode 测试 `UndockAfterService_HandsRotorsBackToAnimator` 按骨骼实际旋转测转速、按 `Idle_Hover` 当前帧比姿态 | 可编辑控制器下离座后仍由 `Rotor_Spin`（`Idle_Hover` 副本）驱动，测法不变 | 合并后重跑 |

## 3. 场景构建

| workorder06 的构建入口 | 迁移做法 |
|---|---|
| `Unit07DockBuilder.BuildSceneBase()` / `RebuildSceneOnly()`（维修座测试场景） | 在 `BuildSceneBase` 末尾调用 `Unit07EditableAnimation.ApplyToDock(ctrl, Unit07EditableAnimation.EnsureAll())`。本分支把这一句加在第一阶段的 `BuildScene` 里 |
| `Unit07WorkOrder06SceneBuilder.Build()`（工单 06 集成场景） | 共用 `BuildSceneBase`，接上面的改动后自动带上 |
| `Unit07_WorkOrder06_Test.unity`、`Unit07Dock_Test.unity` | 重新构建，或运行菜单“给现有维修座、工作台测试场景接上可编辑资产”（需要把工单 06 场景路径加进该菜单） |

## 4. 资产（直接沿用，不需要改）

- `Assets/BorderRepair/Animation/Unit07/Clips/*.anim`：9 个可编辑片段。
- `Assets/BorderRepair/Animation/Unit07/Controllers/`：可编辑控制器、两个遮罩、转角辅助片段。
- `Assets/BorderRepair/Animation/Unit07/Motion/*.asset`：4 个动作配置。上面新增 `liftOff` / `handoverBlendSeconds` 字段后，已存在的配置资产会用代码里的默认值补上新字段，已手调的字段不受影响。
- 程序集 `BorderRepair.Motion`（运行时）和 `BorderRepair.Motion.Editor`（编辑器）。workorder06 的 `BorderRepair.Runtime` / 测试程序集要加上对 `BorderRepair.Motion` 的引用。
