# UNIT 07 现有动作审计（只读）

Unity 6000.0.84f1，2026-10-02 01:07。由 `Unit07AnimAudit` 生成。

## 1. RobotV4 FBX 骨骼动作

- **来源**：`Assets/RobotV4/Model/robot-final.fbx`（只读，片段是 FBX 的子资源，不能直接保存修改）。
- **导入设置**：动画类型 Generic，重采样曲线 True，压缩 Off，片段 9 个。
- **导入后处理**：`RobotV4ModelPostprocessor` 把 `Idle_Hover` 第 0 帧采样到模型上，作为预制体的静态姿态。

| 片段 | 时长 (s) | 帧率 | 帧数 | 循环 | 曲线数 | 绑定路径 | 根节点曲线 | 真正在动的骨骼 |
|---|---|---|---|---|---|---|---|---|
| `Arm_Deploy_L` | 1.200 | 30 | 36 | 否 | 190 | 19 | 无 | Arm_L_Elbow、Arm_L_RootPivot、Arm_L_Shoulder |
| `Arm_Deploy_R` | 1.200 | 30 | 36 | 否 | 190 | 19 | 无 | Arm_R_Elbow、Arm_R_RootPivot、Arm_R_Shoulder |
| `Gripper_OpenClose_L` | 1.600 | 30 | 48 | 是 | 190 | 19 | 无 | Arm_L_JawLower、Arm_L_JawUpper |
| `Gripper_OpenClose_R` | 1.600 | 30 | 48 | 是 | 190 | 19 | 无 | Arm_R_JawLower、Arm_R_JawUpper |
| `Idle_Hover` | 2.000 | 30 | 60 | 是 | 190 | 19 | 无 | Body、Engine_L_Hinge、Engine_L_Rotor、Engine_R_Hinge、Engine_R_Rotor |
| `Pose_Gripper_Closed` | 0.033 | 30 | 1 | 否 | 190 | 19 | 无 | （静态姿态） |
| `Pose_Gripper_Half` | 0.033 | 30 | 1 | 否 | 190 | 19 | 无 | （静态姿态） |
| `Pose_Gripper_Open` | 0.033 | 30 | 1 | 否 | 190 | 19 | 无 | （静态姿态） |
| `Repair_Reach` | 2.500 | 30 | 75 | 否 | 190 | 19 | 无 | Arm_R_Elbow、Arm_R_JawLower、Arm_R_JawUpper、Arm_R_Shoulder、Arm_R_Wrist、Body |

- **控制器** `Assets/RobotV4/RobotV4_Acceptance.controller`：1 层，9 个状态，默认 `Idle_Hover`；片段全部引用 FBX 子资源：True；Write Defaults：True
- **控制器** `Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_Dock.controller`：1 层，9 个状态，默认 `Idle_Hover`；片段全部引用 FBX 子资源：True；Write Defaults：True

## 2. 维修座脚本动作（`Unit07DockController` / `RotorPowerDriver`）

首单测试场景 `Assets/BorderRepair/FirstOrder/Scenes/Unit07FirstOrder_Test.unity` 里的数值（与 `Unit07Dock_Test` 相同，都是脚本默认值）。七号的 Animator 控制器：`Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_Dock.controller`。

| 动作 | 写入对象 | 时长 | 缓动（现版代码） | 状态判断 |
|---|---|---|---|---|
| 夹具张开 / 合拢 | `Dock_Clamp_L`、`Dock_Clamp_R` 本地旋转（绕本地 Y，角度来自 FBX 属性 `unity_open_deg`） | 0.6 s | 线性（`Mathf.MoveTowards`），中途可反向 | `ClampsOpening` / `Clamping` |
| 七号落座 | 七号根节点世界位置（悬停高度 0.12 m → 0） | 1.2 s | `Mathf.SmoothStep` | `Descending`；结束时转子交给 `RotorPowerDriver`，Animator 用 0.15 s 过渡到 `Pose_Gripper_Closed` |
| 断电开关手柄 | `Dock_PowerSwitch_Lever` 本地旋转（`unity_on_deg` ↔ `unity_off_deg`） | 0.3 s | 线性（`Mathf.MoveTowards`），中途可反向 | 跟随供电状态 |
| 转子减速（断电） | 两根转子骨骼本地旋转 | 2.5 s（从 360°/s 到 0） | 转速线性下降（匀减速） | `SpinningDown` → `RotorsStopped` |
| 转子加速（通电） | 同上 | 1 s（0 → 360°/s） | 转速线性上升（匀加速） | 首单 `PowerOn` 步骤 |
| 状态灯 | 材质属性块（不是 Transform） | 立即 | — | 跟随供电状态 |

转子骨骼：`Robot_Rig/Root/Body/Engine_L_Hinge/Engine_L_Rotor`、`Robot_Rig/Root/Body/Engine_R_Hinge/Engine_R_Rotor`。

## 3. 首单 / 工作台占位移动

| 动作 | 写入对象 | 时长 | 缓动（现版代码） | 路径 |
|---|---|---|---|---|
| 锁扣扳开 / 扣回（`FirstOrderFlow.MoveLatch`） | 外侧、后侧锁扣世界位置 | 0.45 s | `Mathf.SmoothStep` | 沿外法线移出 6 mm 再移回 |
| 上盖总成取下 / 搬运 / 装回 | 上盖（成员一起挂在主对象下） | 每段 0.45 s | `Mathf.SmoothStep` | 沿引擎轴线抬起 10 cm → 升到 1.32 m → 水平 → 落下 |
| 旧轴承取下 / 搬运，新轴承装上 | 旧轴承、占位新轴承 | 每段 0.45 s | `Mathf.SmoothStep` | 沿轴抬起 7 cm → 搬运高度 → 落下 |
| 七号离座（`FirstOrderFlow.LiftOff`，原型） | 七号根节点世界位置（0 → 悬停高度） | 1.2 s（写死在代码里） | `Mathf.SmoothStep`；Animator 0.2 s 过渡回 `Idle_Hover`（写死） | 夹具张开后上浮 |
| 复测采样（不是动作） | 只读 | 3 s | — | — |
| 工作台托盘取放、义肢占位件拆装、工具箱上层（`WbPlaceholderDemo.Move`） | 托盘、4 颗螺钉、盖板、工具箱上层世界位置 | 每段 0.35 s | `Mathf.SmoothStep` | 抬起 → 平移 → 放下（几何路径另有检查） |

## 4. 有没有两个“写入者”同时写同一个 Transform

- **转子骨骼**：**有冲突**。`Engine_L_Rotor`、`Engine_R_Rotor` 被片段绑定（Idle_Hover 等），落座后 `RotorPowerDriver` 又在 LateUpdate 覆盖它们：Animator 每帧先写，脚本后写。现版靠执行顺序得到正确画面，但属于两个写入者。
- **脚本移动的七号部件和七号根节点**：没有被片段绑定（七号根节点（落座 / 离座）：片段没有绑定；左上盖外侧锁扣：片段没有绑定；左上盖后侧锁扣：片段没有绑定；左上盖总成（上盖 + 进气唇口 + 护栅 + 风道）：片段没有绑定；左上盖总成（上盖 + 进气唇口 + 护栅 + 风道） 成员：片段没有绑定；左上盖总成（上盖 + 进气唇口 + 护栅 + 风道） 成员：片段没有绑定；左上盖总成（上盖 + 进气唇口 + 护栅 + 风道） 成员：片段没有绑定；左上轴承（故障件）：片段没有绑定）。
- **维修座**：场景里除七号外的 Animator：无。夹具、手柄只由 `Unit07DockController` 写。
- **工作台**：场景里 Animator 0 个。占位移动只由 `WbPlaceholderDemo` 写。

## 5. 哪些参数现在只能改代码

- **离座**：`FirstOrderFlow.LiftOff` 的 1.2 s 上浮时长、0.2 s Animator 过渡写死在代码里。
- **落座过渡**：`Unit07DockController` 落座后 0.15 s 的 Animator 过渡写死在代码里。
- **缓动**：所有缓动（`SmoothStep` / 线性）写死在代码里。
- **时长**：其余时长是组件序列化字段，只能在场景实例上改；重新构建场景会回到代码默认值。
- **FBX 片段**：片段是 FBX 子资源，只读；改动作只能回 Blender 重新导出。
