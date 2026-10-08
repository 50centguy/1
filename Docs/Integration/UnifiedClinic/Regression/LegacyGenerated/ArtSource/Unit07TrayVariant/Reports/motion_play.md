# 托盘变体 · 完整动作逐帧检查（PlayMode，隔离验证场景）

- 2026-10-08 16:52，Unity 6000.0.84f1。固定步长 1/60 s，共 889 帧（约 14.8 s），每 3 帧量一次。程序播放，不是真人观看。
- 托盘原位就贴着的件（离原位 20 mm 内不计）：Dock_TrayShelf。阶段：carry 端着 / lean 左倾回正 / return 移到托盘架、落座、张上爪 / release 手退出、（落盘） / away 退开升高 / arms_out 两臂交还 / descend 落回悬停位。

| 阶段 | 采样 | A 七号 / 托盘 ↔ 环境 | 最近 | B 手上托盘 ↔ 七号其余 | C 两臂 ↔ 机身 | D1 右手非爪齿 ↔ 托盘全部 | 最近 | D2 爪齿 ↔ 盘体 / 立柱 | D3 爪齿 ↔ 握杆（允许接触） |
|---|---|---|---|---|---|---|---|---|---|
| carry | 24 | ≥ 50 mm |  | 49.0 mm | 9.0 mm | 2.1 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 | 12.8 mm | 0.0 mm |
| lean | 111 | ≥ 50 mm |  | 49.0 mm | 9.0 mm | 2.1 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 | 12.8 mm | 0.0 mm |
| return | 69 | 8.8 mm | Tray_Handle_PX·立柱 / 安装座 ↔ Dock_MagneticBox | 49.0 mm | 9.0 mm | 2.1 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 | 12.8 mm | 0.0 mm |
| release | 8 | 8.8 mm | Tray_Handle_PX·立柱 / 安装座 ↔ Dock_MagneticBox | — | 9.0 mm | 2.2 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 | 4.1 mm | 0.0 mm |
| away | 59 | 8.8 mm | Tray_Handle_PX·立柱 / 安装座 ↔ Dock_MagneticBox | — | 9.0 mm | 5.2 mm | Arm_R_JawLower ↔ Tray_Handle_NX·握杆直段 | 4.7 mm | 2.1 mm |
| arms_out | 19 | 8.8 mm | Tray_Handle_PX·立柱 / 安装座 ↔ Dock_MagneticBox | — | 9.0 mm | ≥ 50 mm |  | ≥ 50 mm | ≥ 50 mm |
| descend | 6 | 8.8 mm | Tray_Handle_PX·立柱 / 安装座 ↔ Dock_MagneticBox | — | 9.0 mm | ≥ 50 mm |  | ≥ 50 mm | ≥ 50 mm |

- 演出结束：托盘父对象 `Unit07ServiceDock`，离原位 0.000 mm、朝向差 0.000°；机身倾角 0.000°；覆盖层权重 0（Animator 接管）。
- 之后程序点击停靠 → 夹紧 → 断电 → 停稳 → 登记：安全六项全部成立，夜末存档 Ok，现金 800，托盘在原位 True。

**结论（程序检测）**：A / B / C / D1 / D2 各阶段都 > 0；只有 D3（爪齿 ↔ 握杆）为握持接触。受测距局限（不算边到边、共面）约束，见剖视图。
