# 第一晚端盘演出：真实播放检查（PlayMode 动作探针，发布场景）

- 2026-10-08 17:00，Unity 6000.0.84f1。托盘：`Dock_PartsTray_HandleRaised`（提手抬高 45 mm 悬臂变体；原托盘停用）。固定步长 1/60 s，共 889 帧（约 14.8 s）。程序播放，不是真人观看。
- 采样：关键接触段（return / release / away）每一帧量右手 ↔ 托盘、托盘 ↔ 环境（共 409 帧）；七号整体 ↔ 环境、手上托盘 ↔ 七号、两臂 ↔ 机身每 3 帧一次。
- 局限：测距不算边到边、判不出共面重叠；帧与帧之间不插值。结论是“这些采样里没有测到穿插”，不是连续几何证明。
- 托盘离原位 20 mm 内不计它原位就坐着的件：Dock_TrayShelf。

| 阶段 | 3 帧采样 | 每帧采样 | A 七号 / 托盘 ↔ 环境 | 最近 | B 手上托盘 ↔ 七号其余 | C 两臂 ↔ 机身 | D1 右手非爪齿 ↔ 托盘全部 | 最近 | D2 爪齿 ↔ 盘体 / 立柱 | D3 爪齿 ↔ 握杆（允许） |
|---|---|---|---|---|---|---|---|---|---|---|
| carry | 24 | 0 | ≥ 50 mm |  | 49.0 mm | 9.0 mm | 2.1 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 | 12.8 mm | 0.0 mm |
| lean | 111 | 0 | ≥ 50 mm |  | 49.0 mm | 9.0 mm | 2.1 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 | 12.8 mm | 0.0 mm |
| return | 69 | 206 | 8.8 mm | Tray_Handle_PX·立柱 / 安装座 ↔ Dock_MagneticBox | 49.0 mm | 9.0 mm | 2.1 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 | 12.8 mm | 0.0 mm |
| release | 8 | 26 | 8.8 mm | Tray_Handle_PX·立柱 / 安装座 ↔ Dock_MagneticBox | — | 9.0 mm | 2.1 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 | 4.0 mm | 0.0 mm |
| away | 59 | 177 | 8.8 mm | Tray_Handle_PX·立柱 / 安装座 ↔ Dock_MagneticBox | — | 9.0 mm | 5.2 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 | 3.5 mm | 2.1 mm |
| arms_out | 19 | 0 | 8.8 mm | Tray_Handle_PX·立柱 / 安装座 ↔ Dock_MagneticBox | — | 9.0 mm | ≥ 50 mm |  | ≥ 50 mm | ≥ 50 mm |
| descend | 6 | 0 | 8.8 mm | Tray_Handle_PX·立柱 / 安装座 ↔ Dock_MagneticBox | — | 9.0 mm | ≥ 50 mm |  | ≥ 50 mm | ≥ 50 mm |

- 演出结束：托盘父对象 `Unit07ServiceDock`、离原位 0.000 mm、朝向差 0.000°；机身倾角 0.000°；覆盖层权重全部为 0（Animator 接管）。
- **程序检测结论**：A / B / C / D1 / D2 各阶段都 > 0；只有 D3（爪齿 ↔ 握杆）为握持接触。两臂 ↔ 机身 9.0 mm 是静止姿态本身的值。
