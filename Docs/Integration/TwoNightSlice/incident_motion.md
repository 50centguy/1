# 第一晚端盘演出：路线与静态间隙（编辑器构建时检查）

- 生成时间 2026-10-05 01:42；场景 `Assets/BorderRepair/Scenes/Slice/Unit07_Night.unity`（布局 B 副本）。托盘 = `Assets/BorderRepair/Art/Unit07TrayVariant/Prefabs/Dock_PartsTray_HandleRaised.prefab`（提手抬高 45 mm 悬臂变体；原托盘在本场景停用，原维修座预制体不改）。
- 右臂端盘姿态：审计关节 3.17 / 36.33 / -58.63 / 16.35°，夹爪 0.321；挂点用美术交付的握持数据 `Assets/BorderRepair/Art/Unit07TrayVariant/Data/Unit07TrayGripData_HandleRaised.asset`：本地 (-125.27, -129.25, -89.90) mm，旋转 -0.59110, -0.53961, -0.42572, 0.42211；挂点与托盘原位朝向差 0.00°。
- 规则：只允许爪齿 ↔ 握杆直段接触；其余右手零件 ↔ 托盘全部几何、爪齿 ↔ 盘体 / 立柱都要 ≥ 阈值。测距 MeshClearance（不算边到边、判不出共面重叠，2–5 mm 级是估计）。

## 静态握持（悬停位、夹住）

- 右手非爪齿零件 ↔ 托盘全部：**2.1 mm**（Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段）；爪齿 ↔ 盘体 / 立柱：**12.8 mm**（Arm_R_JawLower_Teeth ↔ Tray_Handle_NX·立柱 / 安装座）；爪齿 ↔ 握杆：0.0 mm（握持接触，允许）。
- 七号其余部分 ↔ 手上托盘：**49.0 mm**（Arm_R_ForearmShell ↔ Tray_Body）。托盘原位 ↔ 环境（不含它坐着的 Dock_TrayShelf）：8.8 mm（Tray_Handle_PX·立柱 / 安装座 ↔ Dock_MagneticBox）。

## 路线

- 悬停位 H (0.3750, 0.8400, 0.8250)；放盘位 P = 托盘原位 + 0 mm（夹住时右手 ↔ 环境 41.8 mm，Arm_R_JawLower ↔ Dock_TrayShelf）；上爪张开后沿“后”滑出 20 mm 放手，再退到 80 mm 升起；搬运高度 dh = **60 mm**。旧托盘的路线常量不再使用。

上爪张开后各滑出方向（每 4 mm 一档，最多 120 mm）：

| 方向 | 爪齿离开握杆 | 新接触最近（托盘全部几何） | 七号 ↔ 环境 | 放手后 ↔ 原位托盘 | 最紧处 / 原因 |
|---|---|---|---|---|---|
| 外（离开盘中心） | 68 mm | 0.0 mm | 42.8 mm | 1.8 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |
| 外后 | 56 mm | 0.0 mm | 42.8 mm | 6.8 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |
| 外前 | 92 mm | 0.0 mm | 42.8 mm | 0.0 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |
| 右 | 68 mm | 0.0 mm | 42.8 mm | 1.8 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |
| 右后 | 56 mm | 0.0 mm | 42.8 mm | 6.8 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |
| 后 | 20 mm | 2.1 mm | 9.8 mm | 3.4 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |
| 前 | — | 0.0 mm | 42.8 mm | — | 爪齿始终没离开握杆 |

| 段 | 最小间隙 | 阈值 | 最近的两件 |
|---|---|---|---|
| 开场带盘停在 T1，左倾 0–5°（比演出多 1°） | ≥ 50 mm | 10 mm | - |
| 带盘：T1 平移到托盘架上方 T2 | 50.0 mm | 10 mm | Tray_Body ↔ Dock_MagneticBox |
| 带盘：T2 下降到放盘位 P（托盘离原位 0 mm；最后 20 mm 另列） | 25.7 mm | 10 mm | Tray_Handle_PX·握杆直段 ↔ Dock_MagneticBox |
| 带盘：最后 20 mm 落座（托盘 ↔ 旁边的件，原位关系；不含它坐着的托盘架） | 8.8 mm | 5 mm | Tray_Handle_PX·握杆直段 ↔ Dock_MagneticBox |
| 夹住：七号（含右爪，不含托盘）↔ 环境，T2 下降到 P | 41.8 mm | 2 mm | Arm_R_JawLower ↔ Dock_TrayShelf |
| 在 P 张开上爪：右手非爪齿零件 ↔ 托盘全部 | 2.1 mm | 2 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |
| 在 P 张开上爪：爪齿 ↔ 盘体 / 立柱 | 12.8 mm | 2 mm | Arm_R_JawLower_Teeth ↔ Tray_Handle_NX·立柱 / 安装座 |
| 上爪开、沿“后”滑出（新接触、环境、放手后原位托盘） | 2.1 mm | 2 mm | - |
| 空手：W 升到搬运高度 | 14.3 mm | 10 mm | Arm_L_ForearmHub ↔ Dock_Clamp_R |
| 空手：平移回 T1 | 23.7 mm | 10 mm | Arm_L_ForearmHub ↔ Dock_RailGuide_R |
| 两臂交还 Animator（T1；不计根环与根座铰接面） | 9.0 mm | 10 mm ⚠ 未达到 | Arm_L_ShoulderYoke ↔ Chassis_ArmHardpoint_L |
| 空手、两臂原姿态：T1 竖直落回悬停位 H（导板内，同正常落座路径） | 7.7 mm | 2 mm | Chassis_MountRail_L ↔ Dock_RailGuide_L |

两臂过渡的 9.0 mm（左肩叉 ↔ 机身挂点）是静止姿态本身的值（美术审计已记），过渡中不变小。刚体平移的静态检查；真实播放见 `incident_motion_play.md`。
