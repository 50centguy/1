# 零件盘提手抬高变体 · Unity 隔离验证（静态 + 路线）

- 生成 2026-10-04 19:27，Unity 6000.0.84f1，场景 `Assets/BorderRepair/Art/Unit07TrayVariant/Verification/TrayVariant_Verify.unity`（发布场景 `Assets/BorderRepair/Scenes/Slice/Unit07_Night.unity` 的副本；原托盘在副本里停用，换成变体）。
- 坐标系核对：原托盘隔板中心（原托盘本地）(0.0, 0.0, 0.0) mm；变体隔板中心（变体根本地）(0.0, 0.0, 0.0) mm。两者相同说明变体根的本地坐标就是原托盘的本地坐标。
- 握法：手臂关节角不变（审计 3.17 / 36.33 / -58.63 / 16.35°，夹爪 0.321）；托盘轴相对腕骨的朝向不变。
- 旧握点（原托盘本地）(151.6, 30.2, 47.0) mm → 新握点 `GripPoint_R`（变体本地）(151.6, 30.2, 92.0) mm：握杆上同一位置（离握杆中心 +30.2 mm），高度 +45.0 mm。
- 新挂点（腕骨本地）(-125.27, -129.25, -89.90) mm，旋转 -0.59110, -0.53961, -0.42572, 0.42211；旧挂点 (-123.13, -86.09, -102.57) mm（不再使用）。

## 1. 静态握持（七号在悬停位、右手夹住；托盘在新挂点上）

| 右手零件 | 规则 | ↔ 盘体 | ↔ 立柱 / 安装座 | ↔ 握杆直段 |
|---|---|---|---|---|
| Arm_R_GripperBracket | 不可接触 | 36.6 | ≥ 50 | 19.8 |
| Arm_R_HingePin_Lower | 不可接触 | ≥ 50 | ≥ 50 | 27.5 |
| Arm_R_HingePin_Upper | 不可接触 | ≥ 50 | ≥ 50 | 27.1 |
| Arm_R_JawLower | 不可接触 | 28.4 | 18.5 | 2.1 |
| Arm_R_JawLower_Teeth | 爪齿（可接触握杆） | 48.7 | 12.8 | 0.0（握持接触，允许） |
| Arm_R_JawUpper | 不可接触 | ≥ 50 | 3.4 | 3.4 |
| Arm_R_JawUpper_Teeth | 爪齿（可接触握杆） | ≥ 50 | 12.0 | 0.1 |
| Arm_R_Wrist | 不可接触 | 41.2 | ≥ 50 | ≥ 50 |

- 右手非爪齿零件 ↔ 托盘全部：**2.1 mm**（Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段）；爪齿 ↔ 盘体 / 立柱：**12.8 mm**（Arm_R_JawLower_Teeth ↔ Tray_Handle_NX·立柱 / 安装座）；爪齿 ↔ 握杆：0.0 mm（握持接触，允许）。
- 七号其余部分（不含右手）↔ 手上的托盘：**49.0 mm**（Arm_R_ForearmShell ↔ Tray_Body）。挂点朝向与托盘原位朝向差 0.00°。
- 原位（托盘架上）：变体 ↔ 环境（不含它本来就坐着的 Dock_TrayShelf）8.8 mm（Tray_Handle_PX·立柱 / 安装座 ↔ Dock_MagneticBox）。

## 2. 演出路线（刚体平移的静态检查；逐帧真实播放见 motion_play.md）

- 悬停位 H (0.3750, 0.8400, 0.8250)；放盘位 P = 托盘原位 + 0 mm（在 P 夹住时右手 ↔ 环境 41.8 mm，Arm_R_JawLower ↔ Dock_TrayShelf）；放手：沿“后”20 mm；退出终点再多 20 mm；搬运高度 dh = **60 mm**。
- 旧路线常量（离原位 8 mm、右后 76 mm、dh 60 mm）是旧托盘的结果，这里全部按变体重算。

上爪张开后各滑出方向（每 4 mm 一档，最多 120 mm）：

| 方向 | 爪齿离开握杆 | 新接触最近（全部托盘几何） | 七号 ↔ 环境 | 放手后 ↔ 原位托盘 | 最紧处 / 原因 |
|---|---|---|---|---|---|
| 外（离开盘中心） | 68 mm | 0.0 mm | 42.8 mm | 1.8 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |
| 外后 | 56 mm | 0.0 mm | 42.8 mm | 6.8 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |
| 外前 | 92 mm | 0.0 mm | 42.8 mm | 0.0 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |
| 右 | 68 mm | 0.0 mm | 42.8 mm | 1.8 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |
| 右后 | 56 mm | 0.0 mm | 42.8 mm | 6.8 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |
| 后 | 20 mm | 2.1 mm | 9.8 mm | 3.4 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |
| 前 | — | 0.0 mm | 42.8 mm | — | 爪齿始终没离开握杆 |
| 下外 | 24 mm | 2.1 mm | 42.8 mm | 2.3 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |

| 段 | 最小间隙 | 阈值 | 最近的两件 |
|---|---|---|---|
| 开场带盘停在 T1，左倾 0–5° | ≥ 50 mm | 10 mm | - |
| 带盘：T1 平移到托盘架上方 T2 | 50.0 mm | 10 mm | Tray_Body ↔ Dock_MagneticBox |
| 带盘：T2 下降到放盘位 P（托盘离原位 0 mm；最后 20 mm 托盘 ↔ 它原位就贴着的件不计） | 25.7 mm | 10 mm | Tray_Handle_PX·握杆直段 ↔ Dock_MagneticBox |
| 带盘：最后 20 mm 落座（托盘 ↔ 旁边的件，原位关系；不含它坐着的托盘架） | 8.8 mm | 5 mm | Tray_Handle_PX·握杆直段 ↔ Dock_MagneticBox |
| 夹住：七号（含右爪，不含托盘）↔ 环境，T2 下降到 P | 41.8 mm | 2 mm | Arm_R_JawLower ↔ Dock_TrayShelf |
| 在 P 张开上爪：右手非爪齿零件 ↔ 托盘全部 | 2.1 mm | 2 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 |
| 在 P 张开上爪：爪齿 ↔ 盘体 / 立柱 | 12.8 mm | 2 mm | Arm_R_JawLower_Teeth ↔ Tray_Handle_NX·立柱 / 安装座 |
| 上爪开、沿“后”滑出（新接触、环境、放手后原位托盘） | 2.1 mm | 2 mm | - |
| 空手：W 升到搬运高度 | 14.3 mm | 10 mm | Arm_L_ForearmHub ↔ Dock_Clamp_R |
| 空手：平移回 T1 | 23.7 mm | 10 mm | Arm_L_ForearmHub ↔ Dock_RailGuide_R |
| 两臂交还 Animator（T1；不计根环与根座铰接面） | 9.0 mm | 10 mm ⚠ 未达到 | Arm_L_ShoulderYoke ↔ Chassis_ArmHardpoint_L |
| 空手、两臂原姿态：T1 竖直落回悬停位 H（导板内） | 7.7 mm | 2 mm | Chassis_MountRail_L ↔ Dock_RailGuide_L |

测距局限：不算边到边最近距离（两条边擦过时结果偏大，最多约一个三角形边长，这里的网格边长约 2–5 mm）；共面重叠判不出穿插。近距离处看剖视图。
