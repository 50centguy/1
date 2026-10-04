# 七号零件盘 · 提手抬高变体（右手单手端盘的穿插修正）

- **分支 / 工作树**：`art/unit07-tray-variant`，独立工作树 `C:\Users\Administrator\UnityProjects\BorderRepairStation_trayvar`，从 `integ/two-night-slice` 的 `72838e9` 开出（开工时核对：程序线仍是 `72838e9`，没有已有的同等修正版）。**未合并、未推送 main。**
- **问题**：七号右手按美术审计姿态端原零件盘时，下爪爪身穿进盘端壁约 20 mm（集成线 `README_N1N2.md` 第 4 节）。
- **结论（几何 / 程序检测）**：变体提手握杆抬高 45 mm、改成“悬臂式”后，静态握持与完整演出的逐帧检测里，**右手非爪齿零件（上下爪爪身、爪架、销轴、腕）与托盘任何几何都不接触**（最近 2.1 mm），**爪齿与盘体 / 立柱不接触**（最近 4.1 mm），只有**爪齿与握杆直段**是握持接触。演出后托盘回到原位（0.000 mm / 0.000°），停靠、断电、登记、夜末安全存档通过。
- **验证范围**：都是程序检测（Unity 编辑器里的隔离验证场景、程序播放、程序点击），**不是真人观看或试玩**；测距有局限（见第 6 节）。端盘观感、鼠标试玩、VR 需另行验收。

## 1. 交付物

| 类别 | 路径 |
|---|---|
| Blender 生成脚本 | `ArtSource/Unit07TrayVariant/build_tray_variant.py`（`run_blender.bat`；`-- --norender` 只生成不渲染） |
| Blender 源文件 | `ArtSource/Unit07TrayVariant/Unit07TrayVariant.blend`（变体 + 隐藏的原盘参考） |
| FBX | `ArtSource/Unit07TrayVariant/Export/UNIT07_PartsTray_HandleRaised.fbx` = `Assets/BorderRepair/Art/Unit07TrayVariant/Models/UNIT07_PartsTray_HandleRaised.fbx`（两份 MD5 相同 `81af41db…`；Blender 每次导出文件头时间戳不同，所以交付的这份就是 Unity 里验证过的那份） |
| Unity 预制体 | `Assets/BorderRepair/Art/Unit07TrayVariant/Prefabs/Dock_PartsTray_HandleRaised.prefab` |
| 握持数据 | `Assets/BorderRepair/Art/Unit07TrayVariant/Data/Unit07TrayGripData_HandleRaised.asset`（`Unit07TrayGripData`），同内容 `Reports/grip_data.json` |
| 隔离验证场景 | `Assets/BorderRepair/Art/Unit07TrayVariant/Verification/TrayVariant_Verify.unity`（发布场景 `Unit07_Night` 的副本；只在副本里停用原托盘、换变体、改挂点和路线） |
| 构建 / 检测脚本 | `Assets/BorderRepair/Art/Unit07TrayVariant/Editor/`（`TrayVariantBuild.BuildAll` 导入→预制体→验证场景→报告；`TrayVariantShots`、`TrayVariantJawDepth`、`TrayVariantSearch`、`TrayVariantHandProbe` 只读诊断；`TrayVariantDiag` 见第 8 节；`AuditRecheck/` 审计定向复核） |
| 动作测试 | `Assets/BorderRepair/Art/Unit07TrayVariant/Tests/VariantMotionTests.cs`（PlayMode）；结果 XML `Reports/TestResults/variant_motion_PlayMode.xml`（1/1 通过） |
| 报告 | `Reports/unity_static_and_route.md`（静态握持 + 路线）、`Reports/motion_play.md`（逐帧）、`Reports/jaw_detail_probe.md`（爪 ↔ 握杆细查）、`Reports/param_search.md`（第一轮参数搜索）、`Reports/hand_in_tray_frame_before.md`、`Reports/audit_recheck_one_hand.md` |
| 图 | Blender：`Renders/B01–B06`；Unity：`Renders/Unity/U01–U08_{before,after}.png`（见第 5 节） |

## 2. 几何修改（只改提手）

| 项 | 原盘 | 变体 |
|---|---|---|
| 盘体 | 260 × 180 mm，壁高 35 mm（底板 4 mm、壁厚 4 mm、隔板），原点盘底中心 | **完全相同**（同一组尺寸重建，`body_bounds` 一致；坐标核对：隔板在同一侧，无镜像） |
| 握杆 | 管径 10 mm，中心离盘中心 X ±151.6 mm、高 47 mm；Y −45…+45 mm，两端斜撑到壁顶 | 管径 10 mm、X ±151.6 mm 不变；**高 92 mm（+45 mm）**；直段 Y −74…+30 mm，**+30 mm 处封端** |
| 支撑 | 两端斜撑 | 两根立柱都在夹爪尖一侧（Y −84、−30 mm），脚落在端壁外面、带安装座（14 × 15 × 5 mm） |
| 总尺寸 | 313.6 × 180 × 51.8 mm | 313.2 × 180 × 97 mm |
| 三角面 | 1440（单一网格） | 1832：盘体 648 + 提手 592 × 2 |
| Renderer / 材质 | 1 / M_Dock_Ivory | **3** / M_Dock_Ivory（复用，未改）；贴图复用 `T_Dock_Grime.png`。多 2 个 Renderer，同材质，SRP Batcher 下额外开销很小，未实测帧时间 |

**为什么是“悬臂式”**（第一版 ±84 mm 两端立柱失败，`jaw_detail_probe.md` 第 3 节）：七号是纵握，夹爪铰链在握点 +Y 侧约 25 mm；握杆再往 +Y 伸，下爪身 / 上爪身就穿进握杆（≥ 6 mm / 约 3.5 mm）。程序圆管扫描：握杆端 ≤ +30 mm 时下爪身离握杆 2.05 mm；立柱在 Y ≤ −24 mm 时离右手 ≥ 12.5 mm。所以握杆从 −84 mm 伸到 +30 mm 封端、两根立柱都放在爪尖一侧。没有试“外伸 52 mm”方案：抬高方案已通过，任务卡要求不要两种方案叠加。

## 3. 握点与挂点（程序接入用）

| 项 | 值 |
|---|---|
| 托盘本地坐标 | 变体根本地 = 原 `Dock_PartsTray` 本地：X 长轴、Y 前后、Z 向上，原点盘底中心，米，缩放 1 |
| 握杆（被握那根，托盘本地 +X 端，网格名 `Tray_Handle_NX`） | 中心 (151.6, −27.0, 92.0) mm，方向 +Y；允许爪齿接触的直段：相对中心 −47…+57 mm（即 Y −74…+30 mm）；标记 `GripBar_NX` / `GripBarStart_NX` / `GripBarEnd_NX` |
| 握点 `GripPoint_R`（右手咬合中心落点） | (151.6, 30.2, 92.0) mm —— 与原握法在握杆上的位置相同（Y +30.2），高度 +45 mm |
| 右腕骨路径 | `UNIT07_RobotV4_DockReady/Robot_Rig/Root/Body/Arm_R_RootPivot/Arm_R_Shoulder/Arm_R_Elbow/Arm_R_Wrist` |
| **新挂点**（托盘原点相对右腕骨） | 位置 (−125.27, −129.25, −89.90) mm；旋转 (x, y, z, w) = (−0.59110, −0.53961, −0.42572, 0.42211) |
| 旧挂点（**不再使用**） | (−123.13, −86.09, −102.57) mm，旋转同上 |
| 求法 | 托盘轴相对腕骨的朝向不变（握法不变）；位置使 `GripPoint_R` 落在审计右手咬合中心（腕骨本地 (−82.4, −0.7, 28.4) mm）。不是旧数值加常量 |
| 手臂姿态 | 不变：审计关节角 3.17 / 36.33 / −58.63 / 16.35°（相对 Idle_Hover 第 0 帧，绕骨骼本地 X）；夹爪开度 0.321（扫描：下爪齿接触、上爪齿 0.07 mm，爪身 2.05 / 3.36 mm；开大到 0.43 起下爪齿离开握杆，所以不改） |
| 原落架位置 | 父对象 `Unit07ServiceDock`，本地位置 (0.57, −0.19, 0.785)，本地旋转 identity，缩放 1（与原托盘相同） |
| 预制体组件 | 从原托盘按值复制：`DockPartProperties`、`DockInteractable`、`DockPickable`（`holdPoint` 留空，场景里指向挂点）；`BoxCollider` 按新包围盒 |

## 4. 动作验证（隔离验证场景，程序播放）

路线按变体重算（旧的“离原位 8 mm / 右后 76 mm / 搬运高度 60 mm”不再沿用）：

| 参数（建议，程序在发布场景重算） | 值 |
|---|---|
| 放盘 | **托盘直接放到原位**（离原位 0 mm；夹住时右手 ↔ 环境 41.8 mm，下爪不再碰托盘架） |
| 放手 | 上爪张开后沿七号“后”方向滑出 20 mm（爪齿离开握杆），再退 40 mm 后升起 |
| 搬运高度 | 悬停位上方 60 mm |
| 其它 | 端着左倾 4°（检查到 5°）、放盘前用 Root / Body 稳定（`TrayCarryOverlay.SteadyWeight`，与 72838e9 相同） |

逐帧检测（`motion_play.md`，固定步长 1/60 s，889 帧，每 3 帧一次，含 Animator 浮动与权重过渡）：

| 阶段 | 七号 / 托盘 ↔ 环境 | 手上托盘 ↔ 七号其余 | 两臂 ↔ 机身 | **右手非爪齿 ↔ 托盘全部** | **爪齿 ↔ 盘体 / 立柱** | 爪齿 ↔ 握杆（允许） |
|---|---|---|---|---|---|---|
| 端着 carry | ≥ 50 | 49.0 | 9.0 | 2.1 | 12.8 | 0 |
| 左倾回正 lean | ≥ 50 | 49.0 | 9.0 | 2.1 | 12.8 | 0 |
| 移到托盘架、落座、开上爪 return | 8.8 | 49.0 | 9.0 | 2.1 | 12.8 | 0 |
| 手退出 release | 8.8 | — | 9.0 | 2.2 | 4.1 | 0 |
| 退开升高 away | 8.8 | — | 9.0 | 5.2 | 4.7 | 2.1 |
| 两臂交还 arms_out | 8.8 | — | 9.0 | ≥ 50 | ≥ 50 | ≥ 50 |
| 落回悬停位 descend | 8.8 | — | 9.0 | ≥ 50 | ≥ 50 | ≥ 50 |

（mm）环境 8.8 mm 是托盘落在原位后，远端（不被握）提手立柱与旁边磁性盒的距离；两臂 ↔ 机身 9.0 mm 是静止姿态本身的值（审计已记）。演出后：托盘父对象恢复 `Unit07ServiceDock`、离原位 0.000 mm、朝向差 0.000°，机身倾角 0°，覆盖层权重归 0（Animator 接管）；随后程序点击停靠 → 夹紧 → 断电 → 停稳 → 登记，安全六项成立，夜末存档 Ok、现金 800。

## 5. 图（同机位前后对比）

- **Unity（隔离验证，编辑器相机渲染，不读桌面）**：`Renders/Unity/` 每张都有 `_before`（72838e9 发布场景、原盘、旧挂点）和 `_after`（变体、新挂点），七号同一位置、同一姿态、同一机位：
  - `U01_on_shelf` 落架使用状态；`U02_grip_front`、`U03_grip_right`、`U04_grip_back_right`（原盘下爪压进端壁最清楚）、`U05_grip_below` 握持近景；`U06_carry_wide` 端盘全景；
  - `U07_section_at_grip`、`U08_section_grip_plus25mm` 剖视：正交相机，近裁剪面放在过握点、垂直于握杆的平面上（第二张再往铰链方向进 25 mm）。剖视是“裁掉一侧”的显示，不是填充截面，读图时注意。
- **Blender（Cycles，无七号）**：`Renders/B01–B03` 原盘（左）与变体（右）同尺度同机位对比；`B04–B06` 变体单独。
- 第一版失败方案没有留图（已被最终方案覆盖）；失败数据在 `jaw_detail_probe.md` 第 3 节和本 README 第 2 节。

## 6. 检测方法与局限

- 测距：`MeshClearance`（集成线修正版）——双向点到三角形 + **双面 Möller–Trumbore** 边穿三角形（0 = 穿插或接触）。正反方向的单元测试在集成线 `TwoNightCoreTests.MeshClearance_SegmentCrossing_IsTwoSided_AndNoMirrorFalsePositive`。
- **局限**：不算边到边最近距离（两条边擦身而过时结果偏大，最多约一个三角形边长，这里约 2–5 mm）；判不出共面重叠（两个面贴在同一平面、没有边穿过对方）。所以 2–5 mm 级的数字是“上限估计”，不是精确安全间隙；关键处另看剖视图。
- 分类：托盘按三角面分成“盘体 / 握杆直段 / 立柱与安装座”（握杆直段 = 离握杆轴 ≤ 6.5 mm 且在 `GripBarStart`…`GripBarEnd` 之间）；右手按网格名分成爪齿（`*_Teeth`）和其余。**只豁免爪齿 ↔ 握杆直段。**
- PlayMode 里不可读网格 / 静态合批：进入播放前只读附加打开验证场景缓存原始网格（`VariantMeshCache`），不改导入设置。

## 7. 审计结论定向复核（`Reports/audit_recheck_one_hand.md`）

复制审计 `0c1c906` 的单手求解代码（原文件不动），同一批 59 个姿态分别用旧 / 新“线段穿三角形”测：

- **改变的结论**：左手镜像右手的纵握（关节 −3/36/−59/−17°，握点偏 +30 mm）旧写法记为 0 mm，新写法 **3.5 mm**；另一组左手 2.9 mm。也就是说“左手单手都碰前臂（0 mm）”不成立，左手镜像握法与右手同级。
- **不变的结论**：其余左手姿态（握点偏 −30 / 0 mm）新写法下仍是 0 mm；右手 3.6 mm、两臂 ↔ 机身 9.0 mm 不变。59 个姿态里只有 2 个数值变化。
- 按任务卡，本单仍按右手方案做。审计里“盘↔机身”的定义不含握盘那只手的爪，所以审计 3.6 mm 也看不到下爪压壁——这一项要用本单的分类检测。

## 8. 写入清单与未改动核对

- 新写入只有：`ArtSource/Unit07TrayVariant/**`、`Assets/BorderRepair/Art/Unit07TrayVariant/**` 和 `Assets/BorderRepair/Art/Unit07TrayVariant.meta`。
- 与 `72838e9` 比较：`Assets/RobotV4`、`Assets/BorderRepair/Prefabs`（维修座预制体）、`Assets/BorderRepair/Art/Unit07ServiceDock`（原 FBX、共享材质）、`ArtSource/Unit07ServiceDock`、`Assets/BorderRepair/Scenes`、`Assets/BorderRepair/FirstOrder/Scenes`、`Assets/Settings`、`ProjectSettings` **无差异**；RobotV4 FBX MD5 `80b94f89c3dfbd62e03214e7487e5d96`。Unity 运行时改写的 `ProjectSettings.asset` 换行已还原。
- 本工作树的 `Library` 从切片工作树复制，复制后三个维修座脚本解析不到类、维修座 FBX 的导入结果带着失效的脚本引用；用 `TrayVariantDiag.Reimport` / `ReimportModels` 只在本工作树强制重新导入脚本和维修座 FBX 修复（源文件和 .meta 都没变）。

## 9. 程序接入交接（不要照搬旧握点）

1. 在最新集成线上，把发布场景 `Unit07_Night` 里的 `Dock_PartsTray` 换成 `Dock_PartsTray_HandleRaised`：父对象、本地位姿用原托盘的；不要改维修座预制体本身（例如在构建脚本里像隔离验证场景那样停用原托盘 + 实例化变体）。
2. `TwoNight_TrayHoldPoint（右手端盘挂点）` 用第 3 节的新挂点；`DockPickable.holdPoint` 指向它；`TrayIncident.tray` 指向变体的 `DockPickable`。
3. 路线按第 4 节重算（`TwoNightBuilder.PlanIncident` 需要支持多 Renderer 托盘、按本单分类豁免）；`TrayIncident` 不用改代码（放盘高度 0 时“落盘”自然为 0）。
4. 代码里假设托盘只有一个 `Renderer` / `MeshFilter` 的地方（`TwoNightBuilder.PlanIncident`、`TwoNightMotionProbe`、`TwoNightMeshCache`）要改成遍历子物体。
5. 逐帧复核要包含爪身 ↔ 盘壁：可直接参考 `VariantMotionTests` 的 D1 / D2。
6. 回归：停靠、夜末存档、退出后第二晚继续、七号鼠标核心、Windows 两进程自检。

## 10. 未验证 / 待办

- 真人观看端盘（悬臂提手观感、倾斜幅度、放手动作）、真人鼠标试玩：**未做**。
- 发布场景接入、Windows Player 运行：**未做**（程序窗口负责）。
- 帧时间 / 目标硬件、VR：未验证。
- 测距局限（第 6 节）：2–5 mm 级间隙是估计值。
- 外伸 52 mm 方案：未做（抬高方案已通过）。
