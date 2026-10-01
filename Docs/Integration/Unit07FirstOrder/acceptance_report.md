# 七号首单 · 可玩原型 · 程序验收报告

- 运行：2026-10-01 16:49，Unity 6000.0.84f1，带界面的编辑器 Play 模式，Game 视图 1471×886，NVIDIA GeForce RTX 3070
- 场景：`Assets/BorderRepair/FirstOrder/Scenes/Unit07FirstOrder_Test.unity`
- 点击方式：每一步切到该步镜头，在目标点选范围内找一个**真实点选规则（含遮挡）会选中它**的屏幕点，再从这个屏幕点点击；找不到就判失败，不直接调用接口。
- 结果：**32/32 步通过**，全部通过；被拒绝的操作共 9 次（都是有意穿插的错误操作）；控制台警告 / 错误 0 条
- 离座复测（占位判定）：通过——悬停 3 s、1005 帧：左右引擎高度差最大 0.0 mm；轴承已更换 True；上盖和锁扣复位 True。（占位判定：模型没有失衡模拟）
- 这是程序走查，不是真人操作；没有验证 VR 和目标硬件。

| # | 步骤 | 镜头 | 目标 | 实际对象路径 | 预期 | 实际 | 结果 | 反馈 | 之后的步骤 / 维修座状态 |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 拒绝：通电悬停时碰左上盖 | 左引擎 | 左上盖总成（上盖 + 进气唇口 + 护栅 + 风道） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_UpperCover_L` | 拒绝 | 拒绝 | ✅ 通过 | 七号还在供电或叶轮还在转。先停靠、夹紧、断电，等涡轮停稳。 | SeatRobot / Hovering |
| 2 | 1 七号入座：点夹具握把（张开 → 落座） | 维修座 | 维修座：Dock_Clamp_L_Grip | `Unit07ServiceDock/Dock_Clamp_L/Dock_Clamp_L_Grip` | 接受 | 接受 | ✅ 通过 | 夹具张开，七号准备落座。 | SeatRobot / ClampsOpening |
| 3 | 等待：七号落座（SeatedOpen） | 维修座 | （等待） | `-` | 条件成立 | 成立 | ✅ 通过 | 七号已落座。再点夹具：夹紧。 | ClampRobot / SeatedOpen |
| 4 | 拒绝：没夹紧就断电 | 维修座 | 维修座：Dock_PowerSwitch_LeverGrip | `Unit07ServiceDock/Dock_PowerSwitch/Dock_PowerSwitch_Lever/Dock_PowerSwitch_LeverGrip` | 拒绝 | 拒绝 | ✅ 通过 | 先夹紧夹具再断电。 | ClampRobot / SeatedOpen |
| 5 | 2 夹具固定：点夹具握把（夹紧） | 维修座 | 维修座：Dock_Clamp_L_Grip | `Unit07ServiceDock/Dock_Clamp_L/Dock_Clamp_L_Grip` | 接受 | 接受 | ✅ 通过 | 夹具合拢。 | ClampRobot / Clamping |
| 6 | 等待：已夹紧（Clamped） | 维修座 | （等待） | `-` | 条件成立 | 成立 | ✅ 通过 | 已夹紧。点断电开关：OFF。 | PowerOff / Clamped |
| 7 | 3 断电：点断电开关手柄 | 维修座 | 维修座：Dock_PowerSwitch_LeverGrip | `Unit07ServiceDock/Dock_PowerSwitch/Dock_PowerSwitch_Lever/Dock_PowerSwitch_LeverGrip` | 接受 | 接受 | ✅ 通过 | 断电。涡轮减速中，等叶轮停稳。 | PowerOff / SpinningDown |
| 8 | 拒绝：涡轮减速中碰外侧锁扣 | 左引擎 | 左上盖外侧锁扣 | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_CoverLatch_Outer_L` | 拒绝 | 拒绝 | ✅ 通过 | 七号还在供电或叶轮还在转。先停靠、夹紧、断电，等涡轮停稳。 | PowerOff / SpinningDown |
| 9 | 等待：涡轮停转（RotorsStopped，转速 0） | 左引擎 | （等待） | `-` | 条件成立 | 成立 | ✅ 通过 | 涡轮已停转。点左引擎开始检查。 | InspectLeftEngine / RotorsStopped |
| 10 | 4 检查左引擎：点左上盖 | 左引擎 | 左上盖总成（上盖 + 进气唇口 + 护栅 + 风道） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_UpperCover_L` | 接受 | 接受 | ✅ 通过 | 左引擎：上盖由外侧和后侧两个锁扣固定。先扳开两个锁扣（后侧那个要转到背面，按 3）。 | ReleaseLatches / RotorsStopped |
| 11 | 拒绝：要拆右引擎（右引擎对照镜头） | 右引擎（对照） | 右引擎上盖（正常，不拆） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_R_Hinge/Engine_UpperCover_R` | 拒绝 | 拒绝 | ✅ 通过 | 右引擎的手感和进气口都正常，不用拆。 | ReleaseLatches / RotorsStopped |
| 12 | 拒绝：锁扣没扳开就取上盖 | 左引擎 | 左上盖总成（上盖 + 进气唇口 + 护栅 + 风道） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_UpperCover_L` | 拒绝 | 拒绝 | ✅ 通过 | 还有锁扣没扳开。后侧那个要转到背面（按 3）。 | ReleaseLatches / RotorsStopped |
| 13 | 5a 拆：扳开外侧锁扣 | 左引擎 | 左上盖外侧锁扣 | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_CoverLatch_Outer_L` | 接受 | 接受 | ✅ 通过 | 左上盖外侧锁扣已扳开。还有一个锁扣。 | ReleaseLatches / RotorsStopped |
| 14 | 5b 拆：扳开后侧锁扣（背面镜头） | 左引擎背面 | 左上盖后侧锁扣 | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_CoverLatch_Rear_L` | 接受 | 接受 | ✅ 通过 | 两个锁扣都扳开了。点上盖，沿引擎轴线取下上盖总成。 | RemoveCover / RotorsStopped |
| 15 | 拒绝：维修中途通电 | 维修座 | 维修座：Dock_PowerSwitch_LeverGrip | `Unit07ServiceDock/Dock_PowerSwitch/Dock_PowerSwitch_Lever/Dock_PowerSwitch_LeverGrip` | 拒绝 | 拒绝 | ✅ 通过 | 锁扣还没扣回，不能通电。 | RemoveCover / RotorsStopped |
| 16 | 5c 拆：取下左上盖总成 | 左引擎 | 左上盖总成（上盖 + 进气唇口 + 护栅 + 风道） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_UpperCover_L` | 接受 | 接受 | ✅ 通过 | 取下上盖总成（上盖 + 进气唇口 + 护栅 + 风道）。 | PlaceCover / RotorsStopped |
| 17 | 5d 去向：上盖总成放到工作台操作垫 | 总览 | 落点：工作台操作垫 | `WorkbenchArea/WB_Mat/Bench_Mat（落点 FO_Drop_Mat）` | 接受 | 接受 | ✅ 通过 | 左上盖总成（上盖 + 进气唇口 + 护栅 + 风道）放到工作台操作垫。 | LocateBearing / RotorsStopped |
| 18 | 6 定位故障轴承：点左上轴承 | 左引擎 | 左上轴承（故障件） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_BearingTop_L` | 接受 | 接受 | ✅ 通过 | 找到了：左上轴承 Engine_BearingTop_L 磨损（占位诊断：只用红色标出）。再点它，取下旧轴承。 | RemoveBearing / RotorsStopped |
| 19 | 7a 更换：取下旧轴承 | 左引擎 | 左上轴承（故障件） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_BearingTop_L` | 接受 | 接受 | ✅ 通过 | 沿转轴取下旧轴承。 | PlaceOldBearing / RotorsStopped |
| 20 | 拒绝：把旧轴承放到上盖的落点 | 总览 | 落点：工作台操作垫 | `WorkbenchArea/WB_Mat/Bench_Mat（落点 FO_Drop_Mat）` | 拒绝 | 拒绝 | ✅ 通过 | 工作台操作垫放的不是左上轴承（故障件）。 | PlaceOldBearing / RotorsStopped |
| 21 | 7b 去向：旧轴承放进工作台托盘 | 总览 | 落点：螺钉托盘前格 | `WorkbenchArea/WB_Trays/Tray_Screws（落点 FO_Drop_PartsTray）` | 接受 | 接受 | ✅ 通过 | 左上轴承（故障件）放到螺钉托盘前格。 | FetchNewBearing / RotorsStopped |
| 22 | 拒绝：上盖没装回就通电 | 维修座 | 维修座：Dock_PowerSwitch_LeverGrip | `Unit07ServiceDock/Dock_PowerSwitch/Dock_PowerSwitch_Lever/Dock_PowerSwitch_LeverGrip` | 拒绝 | 拒绝 | ✅ 通过 | 上盖没装好，不能通电。 | FetchNewBearing / RotorsStopped |
| 23 | 7c 更换：从工作台轴承盒取新轴承装上（占位） | 工作台 | 【占位】新轴承 | `（无美术资产，程序生成的占位圆柱）放在 WorkbenchArea/WB_Trays/Box_Bearings 上` | 接受 | 接受 | ✅ 通过 | 从轴承盒取出新轴承（占位），装到左上轴承位。 | ReinstallCover / RotorsStopped |
| 24 | 8a 装回：上盖总成装回左引擎 | 工作台 | 左上盖总成（上盖 + 进气唇口 + 护栅 + 风道） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_UpperCover_L` | 接受 | 接受 | ✅ 通过 | 把上盖总成从操作垫装回左引擎。 | CloseLatches / RotorsStopped |
| 25 | 拒绝：锁扣没扣回就通电 | 维修座 | 维修座：Dock_PowerSwitch_LeverGrip | `Unit07ServiceDock/Dock_PowerSwitch/Dock_PowerSwitch_Lever/Dock_PowerSwitch_LeverGrip` | 拒绝 | 拒绝 | ✅ 通过 | 锁扣还没扣回，不能通电。 | CloseLatches / RotorsStopped |
| 26 | 8b 装回：扣回外侧锁扣 | 左引擎 | 左上盖外侧锁扣 | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_CoverLatch_Outer_L` | 接受 | 接受 | ✅ 通过 | 左上盖外侧锁扣已扣回。还有一个锁扣（后侧按 3 转到背面）。 | CloseLatches / RotorsStopped |
| 27 | 8c 装回：扣回后侧锁扣（背面镜头） | 左引擎背面 | 左上盖后侧锁扣 | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_CoverLatch_Rear_L` | 接受 | 接受 | ✅ 通过 | 两个锁扣都扣回了。点断电开关：通电。 | PowerOn / RotorsStopped |
| 28 | 9 通电：点断电开关手柄 | 维修座 | 维修座：Dock_PowerSwitch_LeverGrip | `Unit07ServiceDock/Dock_PowerSwitch/Dock_PowerSwitch_Lever/Dock_PowerSwitch_LeverGrip` | 接受 | 接受 | ✅ 通过 | 恢复供电，涡轮重新转动。点夹具握把：松开，让七号离座。 | ReleaseAndLift / Clamped |
| 29 | 等待：涡轮恢复转动 | 维修座 | （等待） | `-` | 条件成立 | 成立 | ✅ 通过 | 恢复供电，涡轮重新转动。点夹具握把：松开，让七号离座。 | ReleaseAndLift / Clamped |
| 30 | 10 离座：点夹具握把（松开 → 上浮） | 维修座 | 维修座：Dock_Clamp_L_Grip | `Unit07ServiceDock/Dock_Clamp_L/Dock_Clamp_L_Grip` | 接受 | 接受 | ✅ 通过 | 夹具张开，七号离座上浮。 | Done / SeatedOpen |
| 31 | 等待：离座复测结束（Done） | 维修座 | （等待） | `-` | 条件成立 | 成立 | ✅ 通过 | 复测通过（占位判定）：左右等高，转子同速。首单原型流程结束。 | Done / SeatedOpen |
| 32 | 复测结果（占位判定）：悬停 3 s、1005 帧：左右引擎高度差最大 0.0 mm；轴承已更换 True；上盖和锁扣复位 True。（占位判定：模型没有失衡模拟） | 维修座 | （等待） | `-` | 条件成立 | 成立 | ✅ 通过 | 复测通过（占位判定）：左右等高，转子同速。首单原型流程结束。 | Done / SeatedOpen |

## 零件最终去向

| 零件 | 实际对象路径 | 最终位置 | 世界坐标 |
|---|---|---|---|
| 左上盖外侧锁扣 | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_CoverLatch_Outer_L` | 七号左引擎原位（已扣回） | (0.486, 1.356, 1.141) |
| 左上盖后侧锁扣 | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_CoverLatch_Rear_L` | 七号左引擎原位（已扣回） | (0.385, 1.331, 1.259) |
| 左上盖总成（上盖 + 进气唇口 + 护栅 + 风道） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_UpperCover_L` | 七号左引擎原位 | (0.374, 1.362, 1.141) |
| 左上轴承（故障件） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_BearingTop_L` | 螺钉托盘前格（WorkbenchArea/WB_Trays/Tray_Screws） | (-0.505, 0.909, -0.251) |
| 【占位】新轴承 | `（无美术资产，程序生成的占位圆柱）放在 WorkbenchArea/WB_Trays/Box_Bearings 上` | 七号左上轴承位（占位新轴承） | (0.380, 1.317, 1.139) |

## 占位与未实现

- 锁扣扳开 / 扣回：模型没有锁扣动画，用沿外法线移出 6 mm 表示已扳开（占位表现）
- 上盖总成：上盖 + 进气唇口 + 护栅 + 风道是同级网格，由程序编成一组一起移动（分组待美术确认）
- 取下 / 搬运 / 装回：程序直线插值移动，没有手部动作或拆卸动画（占位表现）
- 故障轴承定位：只用红色着色和文字标出，没有磨损外观、手转、晃动或声音（占位诊断）
- 新轴承：没有新轴承美术资产，用同尺寸圆柱体代替，名字带【占位】
- 离座复测：模型没有失衡模拟，只核对左右引擎等高、转子转速一致（占位判定）
