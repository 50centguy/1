# UNIT 07 维修座 · 第一阶段 Unity 接入报告

- **分支**：`integ/unit07-dock-phase1`，从 `main`（`314ac87`，已含维修座美术源文件）建立的独立 worktree。
- **RobotV4**：只带入 `robot-v4-import`（`2faf272`）中**已提交**的 `Assets/RobotV4/`，共 86 个文件，FBX MD5 为 `80b94f89…`。
- **不用、不改的部分**：
  - 主 Unity 工作区里未提交的工单、场景和 RobotV4 反馈文件，没有带入，也没有依赖。
  - 维修座源美术、RobotV4 的 FBX、`.blend`、源美术都没有改。
- **环境**：Unity 6000.0.84f1，URP 17.0.4，Windows / Direct3D11 / RTX 3070。**VR 未用设备测试：未验证。**
- **本阶段不包括**：引擎拆卸、正式维修场景、工单代码，以及“六号”的 `RobotRepairController` / `Plan`（它们只在主工作区里，未提交，这个分支里没有）。

## 1. 交付内容

| 类型 | 路径 |
| --- | --- |
| 维修座 FBX 与贴图（只复制这 4 个文件，**不含占位体**） | `Assets/BorderRepair/Art/Unit07ServiceDock/UNIT07_ServiceDock.fbx`，`Textures/T_Dock_Grime / Warning / Labels.png` |
| 7 个 URP/Lit 材质（按 `materials.json`）+ 测试地面材质 | `Assets/BorderRepair/Art/Unit07ServiceDock/Materials/` |
| 维修座预制体（含 10 个简化碰撞 / 点击代理） | `Assets/BorderRepair/Prefabs/Unit07Dock/Unit07ServiceDock.prefab` |
| 七号停靠预制体（RobotV4 FBX 实例 + 新动画控制器 + 转子供电 + 左引擎检查代理；FBX 未改） | `Assets/BorderRepair/Prefabs/Unit07Dock/UNIT07_RobotV4_DockReady.prefab`、`UNIT07_RobotV4_Dock.controller` |
| 测试场景 | `Assets/BorderRepair/Scenes/Unit07Dock_Test.unity`（没有加入 Build Settings） |
| 运行时脚本（`BorderRepair.Runtime`，命名空间 `BorderRepair.Dock`） | `Scripts/Runtime/Dock/`：`Unit07DockController`、`RotorPowerDriver`、`DockPartProperties`、`DockInteractable`、`DockPickable`、`Unit07DockInput` |
| 编辑器脚本 | `Scripts/Editor/Unit07Dock/`：`Unit07DockAssetPostprocessor`（导入设置、FBX 自定义属性）、`Unit07DockBuilder`（构建）、`Unit07DockPlayCapture`（带界面的 Play 模式采集） |
| 测试 | `Tests/EditMode/Unit07DockEditModeTests.cs`（8 项）、`Tests/PlayMode/Unit07DockPlayModeTests.cs`（4 项） |
| 运行脚本 | `Docs/Integration/Unit07Dock_Phase1/run_unity.bat`：构建 → EditMode → PlayMode → 带界面的 Play 模式采集 |
| 记录 | `build_log.txt`、`play_capture.txt`、`Screenshots/`（16 张） |

## 2. 实测结果

### 导入

- **导入警告**：
  - 维修座构建期间（FBX、贴图、材质、预制体、场景）控制台警告和错误 **0 条**（`build_log.txt`）。
  - 首次打开项目时导入 RobotV4 FBX，没有丢面或其他警告。
- **维修座层级**：48 个网格，8,692 个三角面，材质 **7 个**，全是 URP/Lit。
  - `M_Dock_Ivory / Gray / Steel` 的底色贴图是 `T_Dock_Grime`，`Warning`、`Label` 各用自己的贴图，`Lamp` 开启发光。
  - 带 FBX 自定义属性的对象有 10 个。
- **七号**：沿用 RobotV4 FBX，234 个网格、18 根骨骼、9 个动画片段，都保持原样。
  - 新建的动画控制器只引用这 9 个原有片段，默认状态 `Idle_Hover`。
  - Animator 的剔除模式设为 AlwaysAnimate，因为停靠逻辑依赖骨骼位置。
- **占位体**：没有导入（测试 `PlaceholderIsNotImported`）。

### 对位

- **位置**：七号根对齐 `Dock_RobotAnchor`，Unity 坐标 (0, 0.72, 0)，缩放 1，正面 +Z（前框在机身的 +Z 一侧）。
- **接触垫**：左右两块都完整落在同侧的 `Chassis_ArmHardpoint` 下面。
  - 编辑模式按落座姿态采样，顶面与硬点板底面偏差 ≤ 0.5 mm。
  - Play 模式里落座后，以及播放夹爪动作期间，偏差都 ≤ 1 mm。

### 夹具方向

- **读取方式**：夹具角度从 FBX 自定义属性 **`unity_open_deg`** 读取（左 −30°、右 +30°），并在运行时与 Blender 的 `open_deg`（左 +30°、右 −30°）核对符号：两者同号，或者缺少属性，都会报错并停用流程。代码不使用 `open_deg` 驱动。
- **实测**：
  - 按 `unity_open_deg` 转动，夹面离开安装轨超过 15 mm；
  - 按 `open_deg` 转动，夹面压向安装轨；
  - 夹紧时，夹面贴住安装轨，偏差在 1.5 mm 以内（按包围盒判断）。

### 碰撞 / 点击代理

- **位置**：只加在接触垫、夹面、夹具握把、断电开关（握把、铭牌）、零件盘、磁性盒上，共 10 个 BoxCollider，每个体积都小于 0.01 m³。
- **七号身上**：只有一个左引擎检查代理，挂在左引擎铰轴骨骼下，随引擎移动。
- **底座**：没有大碰撞盒。
- **通道检查**（EditMode 与 PlayMode 都查了，夹具夹紧、张开两种状态）：
  - 电源托盘下放、再从两臂之间前取的通道：离维修座碰撞体 **≥ 15 mm**；
  - 两侧引擎拔出、背部维修盒后抽、顶盖上取、前方各件前抽的通道：**没有**维修座碰撞体。
- **点击**：从测试镜头打射线，能直接点中每个可点的代理，不被别的代理挡住。点击路径和按钮接口走的是同一套规则。

### 停靠流程

流程：夹具张开 → 七号落座 → 夹紧 → 开关 OFF → 涡轮停转 → 允许检查左引擎（`play_capture.txt`，带界面的编辑器 Play 模式）。

| 阶段 | 转子（实测） | 叶轮 / 左引擎 |
| --- | --- | --- |
| 悬停，通电 | 由 `Idle_Hover` 驱动，360°/s | 禁止 |
| 落座，通电（转子由 `RotorPowerDriver` 接管） | 360°/s | 禁止（“七号仍在通电”） |
| 断电 0.5 s | 288°/s | 禁止（“叶轮还在减速转动”） |
| 断电 1.5 s | 144°/s | 禁止 |
| 断电 3.5 s | 0°/s，停稳 | **允许** |
| 断电状态下播放 `Gripper_OpenClose_R` | 0°/s，保持静止 | 夹爪照常开合（PlayMode 测试：上爪转过 > 25°，机身保持落座） |
| 恢复供电 1.3 s | 360°/s | 再次禁止 |

- **开关、状态灯、转子一致**：
  - ON：手柄在 `unity_on_deg`（−35°），状态灯绿色，转子转动；
  - OFF：手柄在 `unity_off_deg`（+35°），状态灯琥珀色，转子减速到停。
- **顺序不对的操作会被拒绝，并给出原因**：
  - 未落座就断电；
  - 未夹紧就断电；
  - 断电维修中松开夹具；
  - 通电或减速期间操作叶轮、检查左引擎。
- **断电只影响转子**：`RotorPowerDriver` 只在 `LateUpdate` 里覆盖两根转子骨骼，Animator 照常驱动身体、手臂和夹爪。落座后切到 RobotV4 原有的 `Pose_Gripper_Closed` 片段作为静止姿态（身体不再上下浮动），没有新建动画片段。
- **零件盘和磁性盒**：都是独立物件，可以取下（移到镜头前的手持位置），也可以放回原处，位置误差 < 0.01 mm。

### 渲染统计

同一场景、同一镜头，在带界面的编辑器 Play 模式中测量。Game 视图 1471 × 886，Direct3D11，RTX 3070。每组预热 45 帧、记录 120 帧，取中位数。

| 配置 | Draw calls / Batches | SetPass | 渲染三角面 | 帧时间中位 / P95 |
| --- | ---: | ---: | ---: | ---: |
| 空场景（参考） | 17 | 17 | 954 | 1.74 / 4.48 ms |
| **接入前**：只有七号 | 717 | 20 | 602,277 | 2.27 / 2.80 ms |
| **接入后**：七号 + 维修座 | 846 | 29 | 626,157 | 2.41 / 2.75 ms |
| 增量 | **+129** | +9 | +23,880 | 中位 +0.14 ms |

- 这些是编辑器里的数值，含编辑器开销，帧时间波动较大，不能代表发布版本或 VR 头显上的表现。
- 维修座有 48 个网格，却带来 129 个 draw call，因为阴影等渲染通道会让每个网格被绘制不止一次。如果之后需要压 draw call，可以合并维修座的静止部件（本阶段没做）。

### 测试

| 测试 | 结果 |
| --- | --- |
| `Unit07DockEditModeTests`（8 项：导入与材质、没有占位体、Unity 角度来自 FBX 属性、代理只在指定部件上、拆装通道、对位与接触垫、夹具方向、转子转轴与 `Idle_Hover` 一致） | 8 / 8 通过 |
| `Unit07DockPlayModeTests`（4 项：完整停靠流程与供电一致性、断电不影响夹爪动作、代理可点击、运行时通道里没有维修座碰撞体） | 4 / 4 通过 |
| 回归：项目全部 EditMode 测试 | 65 / 65 通过 |
| 回归：项目全部 PlayMode 测试 | 52 通过，0 失败，7 跳过（原有的截图用 Explicit 测试） |
| 控制台错误 | Play 模式采集中警告 / 错误 **0 条**；PlayMode 测试出现任何错误日志都会自动失败 |

## 3. 依赖说明

- **`RobotV4RepairFeedback`**：没有使用。它是主工作区里未提交的文件，这个分支里没有。
  - 状态灯和转子表现由本分支的 `Unit07DockController`、`RotorPowerDriver` 实现。
  - 以后如果要换成 `RobotV4RepairFeedback`，需要先把它单独提交，再在这里接入。
- **`RobotScreenFace`**：已随 RobotV4 带入，但本阶段没有用到。
- **程序集**：RobotV4 的脚本不在任何 asmdef 里（属于 Assembly-CSharp），`BorderRepair.Runtime` 引用不到。所以停靠逻辑只依赖 Unity 自身的类型（Animator、Transform），不调用 RobotV4 的脚本。

## 4. 未验证 / 后续

- **VR**：未用设备测试。手部接近、抓取零件盘和握把都没有验证。
- **输入**：测试场景只有鼠标点击和 IMGUI 提示，不是正式 UI。夹具的两侧同时开合；零件盘的“取下”是移到镜头前，没有物理、拖拽或放置吸附。
- **未实现**：离座（七号重新升起、转子交还给 Animator）。`RotorPowerDriver.Release()` 可以把转子交还给 Animator，但 `Unit07DockController` 还没有离座流程。
- **帧时间**：只在编辑器里测过，没在发布版本或目标设备上测。
- **命名**：主工作区里未提交的程序仍然用 “Robot6 / 六号” 的名字，这个分支没有涉及，以后统一时需要注意。
