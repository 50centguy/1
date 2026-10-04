# 第一晚端盘演出：路径与静态间隙（编辑器构建时检查）

- 生成时间 2026-10-04 15:36；场景 `Assets/BorderRepair/Scenes/Slice/Unit07_Night.unity`（布局 B 副本）。坐标为 Unity 世界坐标（米）。
- 右臂端盘姿态：美术审计 0c1c906 `tray_handoff.md` 第 3 节，关节（相对 Idle_Hover 第 0 帧，绕骨骼本地 X）3.17 / 36.33 / -58.63 / 16.35°，夹爪开度 0.321（Closed→Half）。审计的挂点原样使用（不滚转腕部）。
- **收窄动作**：不从托盘架上夹起托盘。实测审计握法在托盘原位时下爪压进托盘架边沿 3.3 mm（`grip_vs_shelf.md`），换握角（`grip_roll_probe.md`）也不行。所以七号一开场就端着盘（第一帧渲染前就位），放回时把盘降到离原位 8 mm、上爪张开、下爪滑出横杆，托盘落下最后这段。真正的取 / 放要补件（见 README「美术最小修正」）。
- 左臂：收纳姿态 = 现有片段 `Arm_Deploy_L` 第 0 帧（不新建动画）。原因：右手在托盘架上放盘时，静止姿态的左夹爪正好压在托盘架上。
- 托盘挂点 `…/Arm_R_Wrist/TwoNight_TrayHoldPoint（右手端盘挂点）`，本地位置 (-0.1231, -0.0861, -0.1026)、本地旋转 -0.5911, -0.5396, -0.4257, 0.4221；`Dock_PartsTray.DockPickable.holdPoint` 已设为它。挂点与托盘原位朝向相差 0.00°（落下时过渡、PutBack 恢复原位姿）。
- 悬停位 H (0.3750, 0.8400, 0.8250)；放盘位 P (0.5546, 0.7730, 1.2635)（相对 H (180, -67, 438) mm）；放手位 R = P + 右后 76 mm；退出终点 W = P + 96 mm；搬运高度 dh = **60 mm**（满足阈值的最小一档；都不满足时是最后一档）。
- 托盘（在手上）↔ 七号（不含右爪）：3.6 mm（Dock_PartsTray ↔ Arm_R_GripperBracket），手臂姿态固定，演出全程不变。
- 在 P 张开上爪（夹住 → 张开，各爪 ↔ 托盘）：Arm_R_JawLower 0.0 → 0.0 mm；Arm_R_JawLower_Teeth 0.0 → 0.0 mm；Arm_R_JawUpper 2.8 → 4.8 mm；Arm_R_JawUpper_Teeth 0.0 → 5.1 mm；
- 带盘夹住停在 P：七号 ↔ 环境 4.7 mm（Arm_R_JawLower ↔ Dock_TrayShelf）。

**审计握法本身（静态，托盘在手里）**：右爪各零件 ↔ 托盘。托盘是单一网格，这里按托盘本地 X 拆成两端提手和盘体（|x| ≤ 133 mm）。“穿进深度”= 盘体沿托盘向下移多少毫米才与该零件分开。

| 零件 | ↔ 提手 | ↔ 盘体 | 穿进盘体深度 |
|---|---|---|---|
| Arm_R_JawLower | 0.0 mm | 0.0 mm | 20 mm |
| Arm_R_JawLower_Teeth | 0.0 mm | 11.1 mm | — |
| Arm_R_JawUpper | 2.8 mm | 2.8 mm | — |
| Arm_R_JawUpper_Teeth | 0.0 mm | 7.5 mm | — |

- 右手（两爪 + 爪架 + 铰销）离盘体 ≥ 2 mm 需要：提手横杆整体再往外伸 **52 mm**，或再抬高 **39 mm**（只动提手，盘体不动；用移动盘体网格求出，精度 1 mm）。

上爪张开后，各滑出方向（每 4 mm 一档，最多 100 mm）。“新接触”= 开始时离托盘 ≥ 2 mm 的零件途中最近；开始就贴着的爪要分开到 ≥ 2 mm 且不再碰回：

| 方向 | 放手位（贴着的爪都分开） | 新接触最近 | 七号 ↔ 环境 | 放手后 ↔ 原位托盘 | 最紧处 / 不行的原因 |
|---|---|---|---|---|---|
| 沿横杆 + | 96 mm | 3.6 mm | 0.0 mm | 6.8 mm | 新增接触共 32 mm 路程：Arm_L_ForearmHub ↔ Dock_ClampEar_R_196、- ↔ - |
| 沿横杆 − | — | 0.0 mm | 4.7 mm | — | 滑出 100 mm 仍未分开：Arm_R_JawLower |
| 右 | 72 mm | 3.6 mm | 4.7 mm | 0.0 mm | 新增接触共 32 mm 路程：Arm_R_JawLower ↔ Dock_PartsTray（原位）、- ↔ -（原位） |
| 左 | 32 mm | 0.0 mm | 0.0 mm | 0.0 mm | Arm_R_JawLower_Teeth 在 16 mm 又碰回托盘 |
| 前 | — | 0.0 mm | 4.7 mm | — | 滑出 100 mm 仍未分开：Arm_R_JawLower |
| 后 | 96 mm | 3.6 mm | 0.0 mm | 6.8 mm | 新增接触共 32 mm 路程：Arm_L_ForearmHub ↔ Dock_ClampEar_R_196、- ↔ - |
| 右前 | — | 0.0 mm | 4.7 mm | — | 滑出 100 mm 仍未分开：Arm_R_JawLower |
| 右后 | 76 mm | 3.6 mm | 4.7 mm | 4.7 mm | Arm_R_GripperBracket ↔ 托盘 |
| 左前 | 48 mm | 0.0 mm | 4.7 mm | 0.0 mm | Arm_R_JawLower_Teeth 在 24 mm 又碰回托盘 |
| 左后 | — | 0.0 mm | 0.0 mm | — | 滑出 100 mm 仍未分开：Arm_R_JawLower |
| 右下 | 68 mm | 0.0 mm | 4.3 mm | 0.0 mm | 新增接触共 56 mm 路程：Arm_R_GripperBracket、Arm_R_JawLower ↔ Dock_PartsTray（原位）、- ↔ -（原位） |
| 外（离开盘中心） | 72 mm | 3.6 mm | 4.7 mm | 0.0 mm | 新增接触共 32 mm 路程：Arm_R_JawLower ↔ Dock_PartsTray（原位）、- ↔ -（原位） |
| 外后 | 76 mm | 3.6 mm | 4.7 mm | 4.7 mm | Arm_R_GripperBracket ↔ 托盘 |
| 外前 | — | 0.0 mm | 4.7 mm | — | 滑出 100 mm 仍未分开：Arm_R_JawLower |

| 段 | 最小间隙 | 阈值 | 最近的两件 |
|---|---|---|---|
| 开场带盘停在 T1，左倾 0–5°（比演出多 1°） | ≥ 50 mm | 10 mm | - |
| 带盘：T1 平移到托盘架上方 T2 | ≥ 50 mm | 10 mm | - |
| 带盘：T2 下降到放盘位 P（托盘离原位 8 mm；最后 20 mm 托盘进托盘架凹槽，托盘 ↔ 托盘架不计） | 25.8 mm | 10 mm | Dock_PartsTray ↔ Dock_TrayShelf |
| 夹住：七号（含右爪）↔ 环境，从 T2 下降到 P 全程（不含托盘） | 4.7 mm | 2 mm | Arm_R_JawLower ↔ Dock_TrayShelf |
| 在 P 张开上爪：原来离托盘 ≥ 2 mm 的右手零件不碰托盘 | 3.4 mm | 2 mm | Arm_R_JawUpper ↔ Dock_PartsTray |
| 上爪开、沿“右后”滑出 96 mm（贴着托盘的爪都分开后才放手；新接触 / 环境 / 放手后的原位托盘） | 3.6 mm | 2 mm | Arm_R_GripperBracket ↔ 托盘 |
| 空手：W 升到搬运高度 | 19.3 mm | 10 mm | Arm_R_JawLower ↔ Dock_PartsTray |
| 空手：平移回 T1 | 44.8 mm | 10 mm | Arm_L_ForearmHub ↔ Dock_RailGuide_R |
| 两臂交还 Animator（权重 1 → 0，在 T1；不计根环与根座的铰接面） | 9.0 mm | 10 mm ⚠ 未达到 | Arm_L_ShoulderYoke ↔ Chassis_ArmHardpoint_L |
| 空手、两臂原姿态：T1 竖直落回悬停位 H（在维修座导板里，同正常落座路径） | 7.7 mm | 2 mm | Chassis_MountRail_L ↔ Dock_RailGuide_L |

两臂过渡的 9.0 mm（左肩叉 ↔ 机身挂点）是静止姿态本身的值（美术审计已记），过渡中不变小。“≥ 50 mm”表示 50 mm 内没有任何网格。这是刚体平移的静态检查；真实播放时的逐帧间隙见 `incident_motion_play.md`。
