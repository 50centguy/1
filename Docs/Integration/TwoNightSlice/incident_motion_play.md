# 第一晚端盘演出：真实播放逐帧间隙（PlayMode 动作探针）

- 2026-10-04 16:09，Unity 6000.0.84f1。按固定步长 1/60 s 播放整段演出，共 929 帧（约 15.5 s），每 3 帧量一次真实网格。程序播放，不是真人观看。
- 包含 Animator 的身体浮动 / 引擎摆动（Idle_Hover）、两臂权重过渡、上爪张开、放盘滑出、托盘落下、倾斜。
- A：七号（+ 手上的托盘）↔ 维修座 / 工作台；托盘离手后，七号（不含右手）↔ 托盘也算进 A；托盘离原位 20 mm 内不计托盘 ↔ 它在原位时本来就贴着的件（Dock_TrayShelf）。
- B：手上的托盘 ↔ 七号（不含右手两爪）。C：两臂 ↔ 七号其它部分（不含两臂自身根座的铰接面）。D：右手（腕以下）↔ 托盘。
- 阶段：carry 端着停 / lean 左倾回正 / return 移到托盘架、降到离原位 8 mm、张上爪 / release 手滑出、托盘落下 / away 退开升高回到 T1 / arms_out 两臂交还 Animator / descend 落回悬停位。

| 阶段 | 采样 | A 最小 | A 最近的两件 | B 最小 | C 最小 | C 最近的两件 | D 右手 ↔ 托盘 | D 最近的两件 |
|---|---|---|---|---|---|---|---|---|
| carry | 24 | ≥ 50 mm | - | 3.6 mm | 9.0 mm | Arm_L_ShoulderYoke ↔ Chassis_ArmHardpoint_L | 0.0 mm（已知：审计握法下爪穿进盘壁） | Arm_R_JawLower ↔ Dock_PartsTray |
| lean | 111 | ≥ 50 mm | - | 3.6 mm | 9.0 mm | Arm_R_ShoulderYoke ↔ Chassis_ArmHardpoint_R | 0.0 mm（已知：审计握法下爪穿进盘壁） | Arm_R_JawLower ↔ Dock_PartsTray |
| return | 72 | 4.7 mm | Arm_R_JawLower ↔ Dock_TrayShelf | 3.6 mm | 9.0 mm | Arm_L_ShoulderYoke ↔ Chassis_ArmHardpoint_L | 0.0 mm（已知：审计握法下爪穿进盘壁） | Arm_R_JawLower ↔ Dock_PartsTray |
| release | 10 | 4.8 mm | Arm_R_JawLower ↔ Dock_TrayShelf | — | 9.0 mm | Arm_L_ShoulderYoke ↔ Chassis_ArmHardpoint_L | 0.0 mm（已知：审计握法下爪穿进盘壁） | Arm_R_JawLower ↔ Dock_PartsTray |
| away | 68 | 10.3 mm | Dock_PartsTray ↔ Dock_MagneticBox | — | 9.0 mm | Arm_L_ShoulderYoke ↔ Chassis_ArmHardpoint_L | 5.2 mm | Arm_R_JawLower ↔ Dock_PartsTray |
| arms_out | 18 | 10.3 mm | Dock_PartsTray ↔ Dock_MagneticBox | — | 9.0 mm | Arm_L_ShoulderYoke ↔ Chassis_ArmHardpoint_L | ≥ 50 mm | - |
| descend | 6 | 4.8 mm | Chassis_MountRail_L ↔ Dock_RailGuide_L | — | 9.0 mm | Arm_L_ShoulderYoke ↔ Chassis_ArmHardpoint_L | ≥ 50 mm | - |
