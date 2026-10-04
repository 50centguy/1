# 两晚切片 · 第一晚串联 → 第二晚安全开场（N1N2）

- **分支**：`integ/two-night-slice`，基线 `8dc8bb8`（七号左引擎鼠标维修核心）。本次提交号见 `git log`（交付消息里也给出）。没有合并 `main`，没有整分支合入 `workorder06`。
- **开工前核查**：实际 HEAD = `8dc8bb8`，工作树干净（没有未提交项），没有已有的第一晚串联实现，所以在同一分支上接着做，没有另起新线。
- **没碰的东西**：
  - 共享主工作区（`robot-v4-import`，34 条未提交状态）：MD5 快照前后一致；
  - RobotV4 FBX（MD5 `80b94f89…`）、Final prefab、故障包源文件、布局 A/B 对照场景、七号鼠标核心场景都没改；
  - `BorderRepairStation_unit07int`（8 条）、`BorderRepairStation_workbench`（1 条）、其它工作树都没碰。
- **验证范围**：单元 / 场景测试和 Player 自检都是**程序事件**（程序点击、程序推进柜台会话），**不是真人试玩**。真人试玩清单见第 9 节。VR、目标硬件性能未验证。

## 1. 交付物

| 项目 | 内容 |
|---|---|
| 新试玩包 | `C:\Users\Administrator\UnityProjects\BorderRepairStation_slice\Builds\TwoNightSlice_N1N2_20261004-n1n2\TwoNightSlice.exe`（Windows 64 位，窗口 1600×900，可调大小；`Builds/` 不进 git） |
| 基准包（保留不动） | `Builds\TwoNightSlice\TwoNightSlice.exe`（`8dc8bb8`） |
| 构建记录 | `build_report_n1n2.txt`（Succeeded，0 错误 0 警告，160.9 MB） |
| 场景（Build 列表，写死在 `TwoNightBuilder.BuildScenes`，同时写进 EditorBuildSettings 最前面） | 0 `Assets/BorderRepair/Scenes/Slice/TwoNight_Menu.unity`（入口）<br>1 `Assets/BorderRepair/Scenes/Slice/Night1_Counter.unity`（原型营业场景的副本，只换营业数据 + 两晚协调）<br>2 `Assets/BorderRepair/Scenes/Slice/Unit07_Night.unity`（布局 B 副本 + 端盘演出 + 两晚协调）<br>3 `Assets/BorderRepair/FirstOrder/Scenes/Slice/TwoNightSlice.unity`（七号鼠标核心，**只作回归入口**：exe 加 `-twoNightCore`） |
| 存档 | `%USERPROFILE%\AppData\LocalLow\DefaultCompany\BorderRepairStation\TwoNightSlice\checkpoint.json`（`Application.persistentDataPath`；JsonUtility 结构化；先写 `.tmp` 再替换）。命令行 `-twoNightSaveDir <目录>` 可改位置（自检用） |
| 代码 | 运行时 `Assets/BorderRepair/Scripts/Runtime/TwoNight/`，编辑器 `Assets/BorderRepair/Scripts/Editor/TwoNight/`，测试 `Assets/BorderRepair/Tests/TwoNight/` |
| 重新生成场景 / 构建 | 菜单 `Border Repair/Two-Night Slice/Build Night Scenes (Menu, Counter, Unit07)`、`Build Windows Player (Night 1 → Night 2)`；批处理 `-executeMethod BorderRepair.TwoNight.EditorTools.TwoNightBuilder.BuildAll` / `.BuildWindows -buildTag <标签>`。全部用 Editor API 生成，不手写 YAML，没有运行 Rebuild Prototype |

对既有代码的改动（都很小，七号核心默认行为不变）：

- `FirstOrderFlow`：`InspectionLocked`（第一晚只停靠和登记，不检查不拆；停转安全的拒绝原因优先），`ResumeAtInspection`（读档后从“检查左引擎”开始，要求维修座已是“断电、叶轮停稳”）。**没有**给七号维修步骤另建状态机；两晚协调只管夜晚、案件、账目和场景切换。
- `Unit07DockController.RestoreRotorsStopped`、`RotorPowerDriver.StopNow`：读档恢复“已落座、夹紧、断电、停稳”。
- `SliceView`：可换标题和手册内容。

## 2. 玩家流程（本次实现）

1. **主菜单**：开始 / 继续 / 重新开始（二次确认）/ 退出。没有有效存档时“继续”不可点；存档损坏、版本不对、内容不安全时“继续”不可点并说明原因，不崩溃。“重新开始”只删本切片的 `checkpoint.json`（和残留的 `.tmp`）。
2. **第一晚柜台**：收藏家带通讯器来（三句对白），复用原型的真实维修流程（扫描 → 诊断 → 决定）。
   - 正确（诊断对 + 决定“维修”）结单 → 进入账本。
   - 错误决定：**不入账**，弹出“重新处理这台通讯器”，用原型自己的 `Session.Restart()` 重来。
3. **账本**：开店现金 500 + 维修收入 400 − 耗材 100 = **800**；房租 1,200，第 3 晚打烊前到期，**今晚不扣**，显示“还差 400”。账本盖住原型自己的营业总结（它的“重新开始”此时点不到）。确认账本 → 七号场景。
4. **七号端盘失衡**（约 15.5 s，演出期间不接受 3D 点击，只显示字幕和账目）：七号右手单手端着零件盘 → 机身慢慢向左倾 4°（两次小回摆）、盘随之偏斜、屏幕 `TRAY UNSTABLE` → 回正 → 把盘放回托盘架 → 两臂交还动画 → 落回悬停位。**不拆盖、不露轴承、不给诊断。**
5. **停靠**：玩家用鼠标点夹具握把（张开落座 → 夹紧）→ 断电 → 等叶轮停稳（减速中点锁扣会被拒绝）。停稳后碰上盖被拒绝：“今晚不拆：先登记内部维修单，明天开盖检查。”
6. **登记**：停稳后出现“登记内部维修单”。登记前再从真实状态收一次“安全停靠”结论（落座、夹紧、断电、停稳、托盘在原位、机身回正），全部满足才写夜末存档；写失败给出可读原因并可重试。
7. **退出 → 重新启动 exe → 继续**：直接进入第二晚——现金 800，七号已停靠夹紧、断电停稳、**仍未修**（工单 `WO-U07-N1`），不重放事故、不重做停靠；手册打开（只写安全须知、可观察现象、建议检查方法，不写“上轴承磨损”）；可以开始检查。**做到这里停止。**

## 3. 状态与账目

| 阶段（`TwoNightPhase`） | 进入条件（`TwoNightRun`） | 账目 / 存档 |
|---|---|---|
| `Night1Counter` | `NewGame`：现金 500 | 无 |
| `Night1Ledger` | `SettleCommunicator(correct: true)`，只在柜台阶段、只一次 | 记两笔：`N1-COMM-INCOME` +400、`N1-COMM-PARTS` −100。`Post` 按 ID 幂等，重复结单 / 重复调用不再记 |
| `Night1Incident` | `ConfirmLedger`，只从账本阶段 | 无 |
| `Night1Docking` | `FinishIncident`，只一次 | 无 |
| `Night1Ended` | `RegisterUnit07(安全)`，只从停靠阶段 | 写 `checkpoint.json`（只在这里写，且必须安全） |
| `Night2Open` | 菜单“继续”读档后 `BeginNight2`（只在内存里） | 不改存档；再继续一次仍从存档读，钱不变 |

- 每个转换都检查当前阶段，乱序调用返回 false、状态不变（EditMode 测试覆盖）。
- 错误决定不会触发成功收入；读档、重复继续都不会重复入账。
- 存档读回时校验：阶段 / 夜数、交易 ID 唯一、通讯器两笔都在且金额对、流程标记一致、七号未修、房租参数、安全停靠六项。不满足 → `Corrupt` / `VersionMismatch` / `Unsafe`，“继续”不可用。
- 不存协程、不存“手上拿着的零件”，只存结论。

## 4. 端盘动作：姿态、控制权、验证

**姿态**：美术审计 `0c1c906` 的 `tray_handoff.md` 第 3 节，右手单手纵握近侧横杆（关节 3.17 / 36.33 / −58.63 / 16.35°，夹爪 0.321）；挂点 `…/Arm_R_Wrist/TwoNight_TrayHoldPoint（右手端盘挂点）`，`DockPickable.holdPoint` 已显式指向它；放回用 `PutBack()` 恢复父对象、本地位置、本地旋转。左臂用现有片段 `Arm_Deploy_L` 第 0 帧收纳（右手在托盘架上放盘时，静止姿态的左夹爪会压到托盘架）。倾斜用预制体根绕 Body 原点、七号前向轴转，关节不动，盘随机身一起斜；落座前回正到 0°。

**控制权**：`TrayCarryOverlay`（执行顺序 150，LateUpdate），与 `RotorPowerDriver` 同一种做法。Animator 照常每帧播放（不改片段、不全局停 Animator）；本组件在它之后只覆盖右臂 4 关节 + 两爪、左臂 4 关节，以及放盘那几秒的 Root / Body（按 Idle_Hover 第 0 帧稳住，见下）。权重为 0 时完全不写，控制权交还 Animator；同一时间这些骨骼只有它一个最终写入者。演出期间预制体根只由 `TrayIncident` 写（维修座在悬停状态下不写根）。

**收窄动作（按决策文档“3.6 mm 在动作中不过就先局部调握点或收窄动作”）**：

- 实测审计握法在托盘原位时，下爪压到托盘架（`grip_vs_shelf.md`）；换握角（腕部滚转 −40…+40°，`grip_roll_probe.md`）也找不到能夹起的角度，滚转超出已有动画 0…25° 的范围也不采用。
- 所以**不演“从架上夹起”**：七号一开场（第一帧渲染之前）就端着盘停在维修座上方 60 mm。
- 放回是真实放下：移到托盘架上方 → 把盘降到离原位 8 mm（再低下爪会碰托盘架）→ 上爪张开 → 盘交还托盘架、先搁在下爪上 → 手水平向“右后”滑出 76 mm，贴着横杆的爪都分开 → 盘落下最后 8 mm 回到原位 → 手退开。
- 放盘那几秒把 Root / Body 稳在 Idle_Hover 第 0 帧：Idle_Hover 的身体浮动约 ±6 mm，会吃掉 8 mm 余量（逐帧探针第一次就测到盘碰磁性盒）。

**验证**（不把 3.6 mm 静态间隙当成动作通过）：

- 静态路径（构建时，刚体平移，`incident_motion.md`）：倾斜 0–5° ≥ 50 mm；搬运 ≥ 50 mm；降到放盘位 25.8 mm；夹住时右爪 ↔ 托盘架 4.7 mm；张开上爪不碰新零件（3.4 mm）；滑出全程新接触最近 3.6 mm、下爪 ↔ 环境 4.7 mm、放手后 ↔ 原位托盘 4.7 mm；落回悬停位（导板内）7.7 mm。
- **逐帧动作探针**（PlayMode，固定步长 1/60 s 真实播放整段，含 Animator 浮动、权重过渡、开爪、滑出、落盘、倾斜，`incident_motion_play.md`）：

| 阶段 | 七号 / 盘 ↔ 环境 | 手上盘 ↔ 七号（不含两爪） | 两臂 ↔ 机身 | 右手 ↔ 盘 |
|---|---|---|---|---|
| 端着停 carry | ≥ 50 mm | 3.6 mm | 9.0 mm | 0（已知，见下） |
| 左倾回正 lean | ≥ 50 mm | 3.6 mm | 9.0 mm | 0（已知） |
| 移到架上、降、开上爪 return | 4.7 mm | 3.6 mm | 9.0 mm | 0（已知） |
| 滑出、落盘 release | 4.8 mm | — | 9.0 mm | 起点 0，滑出后分开 |
| 退开 away | 10.3 mm | — | 9.0 mm | 5.2 mm |
| 两臂交还 arms_out | 10.3 mm | — | 9.0 mm | ≥ 50 mm |
| 落回 descend | 4.8 mm | — | 9.0 mm | ≥ 50 mm |

- 两臂 ↔ 机身 9.0 mm 是静止姿态本身的值（左肩叉 ↔ 机身挂点，美术审计已记），过渡中不变小。

**新发现（审计没测到）**：审计握法**静态**就有穿插——右下爪的爪身穿进托盘端壁约 20 mm（`incident_motion.md`“审计握法本身”表、`jaw_contact_probe.md`、近景图 `img/grip_back_right.png`）。审计的“盘 ↔ 机身 3.6 mm”不含握盘的两爪，所以没看到。程序上无法消除（换握角也不行），列为美术最小修正：

| 最小美术修正（二选一，七号不改） | 依据 |
|---|---|
| `Dock_PartsTray` 两端提手横杆整体**外伸 ≥ 52 mm** | 移动盘体网格求出：右手两爪 + 爪架 + 铰销离盘体 ≥ 2 mm |
| 或提手横杆**抬高 ≥ 39 mm** | 同上 |
| 另：要演“从架上夹起 / 放到底”，`Dock_TrayShelf` 在提手下方开 ≥ 8 mm 的缺口，或托盘底座垫高约 10 mm | `grip_vs_shelf.md`：夹住时下爪最低点 y 0.7967，低于架面 0.8000 |

**测距算法修正**：`MeshClearance` 原来照搬审计 `AuditCommon` 的“线段穿三角形”写法，在线段与法线反向时把交点按顶点镜像，会误报穿插、也会漏报（例：在三角形外穿过平面被判为穿插）。已改为双面 Möller–Trumbore 并加单元测试。**美术审计 `0c1c906` 里用同一函数得到的 0 mm 结论（例如“左手单手碰到左前臂 0 mm”）建议用修正后的算法复核**；本切片的所有间隙数字都是修正后重算的。

## 5. 测试结果

| 套件 | 结果 | XML |
|---|---|---|
| EditMode 全部 | 117 / 117 通过（含两晚 9 项：账目幂等、错误决定不入账、乱序拒绝、只在安全夜末存档、往返与重复继续不加钱、缺失 / 损坏 / 版本 / 重复 ID、只删本切片存档、写失败可读、测距穿插判定） | `TestResults_N1N2/n1n2_final_EditMode.xml` |
| PlayMode 全部 | 98 通过，0 失败，7 跳过（既有的截图类 Explicit 测试） | `TestResults_N1N2/n1n2_final_PlayMode.xml` |
| 其中两晚 8 项 | 柜台正确结单只记一次并进七号；错误决定不入账、重做后才入账；没确认账本不触发事故；事故 → 停靠 → 登记 → 安全存档；连续两次继续、第二晚安全开场、钱不变；损坏存档“继续”不可用、重新开始只删本切片存档；无存档时“继续”不可用；端盘整段逐帧探针 | 同上 |
| 回归 | 七号鼠标核心（FirstOrder / Slice 场景测试）、原型三物件、通讯器等既有 PlayMode 全部通过 | 同上 |

## 6. Player 自检（程序事件，独立进程）

- 第 1 段（`-twoNightSelfCheck <目录> -twoNightPhase 1 -twoNightSaveDir <临时目录>`）：菜单 → 新游戏 → 柜台（程序推进会话）→ 账本 800 / 还差 400 → 端盘演出 → 程序点击停靠、夹紧、断电、减速中拒绝、停稳后拒绝拆盖 → 登记 → 夜末存档 Ok → 退出。**全部通过**。`N1N2SelfCheck/selfcheck_phase1.txt`
- 第 2 段（**新进程**，同一存档目录）：菜单“继续”可用 → 第二晚：第 2 晚、现金 800、还差 400、工单 WO-U07-N1；维修座 RotorsStopped、断电、转速 0；七号未修；不重放事故；手册打开、不写诊断答案；点上盖可以开始检查；存档未被改动。**全部通过**。`N1N2SelfCheck/selfcheck_phase2.txt`
- 两段 Player 日志 0 个 error / exception。
- 七号鼠标核心回归入口（新包 `-twoNightCore -sliceSelfCheck`）：第一次运行在“等叶轮停稳”处超时失败、进程提前结束；同一个包立刻重跑 45 步全部通过（`N1N2SelfCheck/core_regression/selfcheck.txt`；截图与 8dc8bb8 的 `PlayerSelfCheck/` 相同，没有重复提交），基准包同一自检也通过。项目设置 `runInBackground = 0`（基准包相同），窗口失去焦点时 Player 会暂停而实时计时继续，最可能是这个原因；**未能确证**，真人试玩时请留意。

## 7. 关键截图（Player 内 Unity 画面，ScreenCapture；不是桌面截图）

`N1N2SelfCheck/`：`p1_01_menu`、`p1_02_counter_collector`、`p1_04_ledger`、`p1_05_incident_carry`、`p1_06_incident_lean`、`p1_07_incident_release`、`p1_09_after_incident`、`p1_10_docked_ready_to_register`、`p1_11_night1_end_saved`、`p2_01_menu_continue`、`p2_02_night2_manual`、`p2_04_night2_inspect_started`。
`img/`：审计握法近景（编辑器内 Unity 相机）`grip_front`、`grip_right`、`grip_back_right`（下爪穿进盘壁最清楚）、`grip_below`。

## 8. 未实现 / 未验证

- 不做（按本单）：双手托盘、新模型、新 shader、换引擎、正式双复测、工人完整案件、VR、库存商店。第二晚只做到“安全开场、可以开始检查”。
- 审计握法下爪穿进托盘端壁约 20 mm（第 4 节），需美术补件；第一晚没有“从架上夹起”。
- 端盘演出没有真人看过；倾角 4°、节奏、镜头（总览）都待真人签收。
- 第二晚的手册“诊断记录”沿用核心切片的检查清单（含“左上轴承（原位）”“新旧轴承对比”两个待看项），手册正文不写“上轴承磨损”。是否连清单项名称也要隐藏，待策划确认。
- 第二晚手册打开时，右下角原有的鼠标落点提示会和“手册 / 调试”按钮重叠（核心切片原有界面，本次没改）。
- 柜台只有收藏家一单；房租第 3 晚的扣款、第三晚都没有做。
- 存档只有一个槽；没有自动存档（按要求只在夜末安全点写）。
- VR、目标硬件性能、其它分辨率 / 全屏都未验证。

## 9. 真人试玩清单

1. 双击 `Builds\TwoNightSlice_N1N2_20261004-n1n2\TwoNightSlice.exe`；第一次应看到“继续”灰掉。
2. 新游戏 → 和收藏家对话 → 用原型流程修通讯器。试一次**错误决定**：应提示重来、账本不出现、钱不变；再正确修好。
3. 账本：500 + 400 − 100 = 800，房租 1,200 第 3 晚到期、今晚不扣、还差 400；营业总结的“重新开始”此时应点不到。确认。
4. 看端盘演出：盘在右手、机身左沉、盘跟着斜、屏幕 TRAY UNSTABLE、回正、盘放回架上落稳、机身回正；有没有看到夹爪穿进盘壁、盘跳动、手臂抖动？倾斜是否“轻微但看得出”？
5. 用鼠标：点夹具黄色握把（张开落座）→ 再点（夹紧）→ 点断电开关 → 叶轮减速时点锁扣应被拒绝 → 停稳后点上盖应提示“今晚不拆” → 点“登记内部维修单” → 夜末画面。
6. 退出游戏（菜单或关窗口），**重新打开 exe** → 继续：应直接在第二晚，现金 800，七号停在座上、叶轮不转、没修；手册里没有“上轴承磨损”；能点左引擎开始检查。
7. 再退出、再继续一次：钱仍是 800。
8. 主菜单“重新开始”（二次确认）后应回到没有存档的状态。
9. 可选：`TwoNightSlice.exe -twoNightCore` 打开七号鼠标核心，按原核心的流程走一遍，确认没回归。
