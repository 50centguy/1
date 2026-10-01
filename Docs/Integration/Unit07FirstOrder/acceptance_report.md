# 七号首单 · 可玩原型（接入故障美术包）· 程序验收报告

- 运行：2026-10-02 04:14，Unity 6000.0.84f1，带界面的编辑器 Play 模式，Game 视图 1471×886，NVIDIA GeForce RTX 3070
- 场景：`Assets/BorderRepair/FirstOrder/Scenes/Unit07FirstOrder_Test.unity`
- 点击方式：每一步切到该步镜头，在目标点选范围内找一个**真实点选规则（含遮挡）会选中它**的屏幕点，再从这个屏幕点点击；找不到就判失败，不直接调用接口。“查看”步骤只切镜头、核对画面条件并截图。
- 结果：**45/45 步通过**，全部通过；被拒绝的操作共 13 次（都是有意穿插的错误操作）；控制台警告 / 错误 0 条
- 离座复测（占位判定）：通过——悬停 3 s、31 帧：左右引擎高度差最大 0.0 mm；轴承已更换 True；进气口已清理 True；上盖和锁扣复位 True。（占位判定：模型没有失衡模拟）
- **这是程序点击走查，不是真人鼠标试玩**；没有验证 VR 和目标硬件。截图是相机渲染到贴图，不读屏幕。

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
| 10 | 故障原位：进气口堵塞（断电停转） | 左引擎近看（进气口 / 轴承位） | （查看） | `-` | 画面条件成立 | 成立 | ✅ 通过 | 进气口堵塞 4 层都在画面里：UNIT07_FK_IntakeClog_L_Fibers、UNIT07_FK_IntakeClog_L_GuardDust、UNIT07_FK_IntakeClog_L_RimDust、UNIT07_FK_IntakeClog_L_DustMat | InspectLeftEngine / RotorsStopped |
| 11 | 4 检查左引擎：点左上盖 | 左引擎 | 左上盖总成（上盖 + 进气唇口 + 护栅 + 风道） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_UpperCover_L` | 接受 | 接受 | ✅ 通过 | 左引擎：进气口护栅上糊着积尘和纤维（堵塞），现在已断电停转，可以点进气口清理。上盖由外侧和后侧两个锁扣固定，拆上盖要先扳开两个锁扣（后侧那个要转到背面，按 3）。 | ReleaseLatches / RotorsStopped |
| 12 | 拒绝：要拆右引擎（右引擎对照镜头） | 右引擎（对照） | 右引擎上盖（正常，不拆） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_R_Hinge/Engine_UpperCover_R` | 拒绝 | 拒绝 | ✅ 通过 | 右引擎的手感和进气口都正常，不用拆。 | ReleaseLatches / RotorsStopped |
| 13 | 4b 清理：点进气口堵塞（断电停转后允许） | 左引擎 | 左进气口堵塞（积尘 + 纤维） | `Assets/BorderRepair/Art/Unit07FaultKit/Models/UNIT07_FK_IntakeClog.fbx（故障美术包）挂在 Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_IntakeGuard_L` | 接受 | 接受 | ✅ 通过 | 清理进气口：拨掉缠住护栅的纤维，刷掉护栅和唇口上的积尘。 | ReleaseLatches / RotorsStopped |
| 14 | 等待：清理完成（积尘、纤维逐层清掉） | 左引擎 | （等待） | `-` | 条件成立 | 成立 | ✅ 通过 | 进气口清理干净了。但只清理不算修好：拆下上盖，检查下面的左上轴承。 | ReleaseLatches / RotorsStopped |
| 15 | 清理后的进气口 | 左引擎近看（进气口 / 轴承位） | （查看） | `-` | 画面条件成立 | 成立 | ✅ 通过 | 进气口清理干净了。但只清理不算修好：拆下上盖，检查下面的左上轴承。 | ReleaseLatches / RotorsStopped |
| 16 | 拒绝：只清理就尝试复测（点夹具握把松开） | 维修座 | 维修座：Dock_Clamp_L_Grip | `Unit07ServiceDock/Dock_Clamp_L/Dock_Clamp_L_Grip` | 拒绝 | 拒绝 | ✅ 通过 | 只清理了进气口，不能算修好，还不能离座复测：上盖下面的轴承还没检查（左上轴承有磨损要处理）。夹具保持锁紧。 | ReleaseLatches / RotorsStopped |
| 17 | 拒绝：只清理就尝试复测（点断电开关通电） | 维修座 | 维修座：Dock_PowerSwitch_LeverGrip | `Unit07ServiceDock/Dock_PowerSwitch/Dock_PowerSwitch_Lever/Dock_PowerSwitch_LeverGrip` | 拒绝 | 拒绝 | ✅ 通过 | 只清理了进气口，不能算修好，还不能通电复测：上盖下面的轴承还没检查（左上轴承有磨损要处理）。 | ReleaseLatches / RotorsStopped |
| 18 | 拒绝：锁扣没扳开就取上盖 | 左引擎 | 左上盖总成（上盖 + 进气唇口 + 护栅 + 风道） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_UpperCover_L` | 拒绝 | 拒绝 | ✅ 通过 | 还有锁扣没扳开。后侧那个要转到背面（按 3）。 | ReleaseLatches / RotorsStopped |
| 19 | 5a 拆：扳开外侧锁扣 | 左引擎 | 左上盖外侧锁扣 | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_CoverLatch_Outer_L` | 接受 | 接受 | ✅ 通过 | 左上盖外侧锁扣已扳开。还有一个锁扣。 | ReleaseLatches / RotorsStopped |
| 20 | 5b 拆：扳开后侧锁扣（背面镜头） | 左引擎背面 | 左上盖后侧锁扣 | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_CoverLatch_Rear_L` | 接受 | 接受 | ✅ 通过 | 两个锁扣都扳开了。点上盖，沿引擎轴线取下上盖总成。 | RemoveCover / RotorsStopped |
| 21 | 拒绝：维修中途通电 | 维修座 | 维修座：Dock_PowerSwitch_LeverGrip | `Unit07ServiceDock/Dock_PowerSwitch/Dock_PowerSwitch_Lever/Dock_PowerSwitch_LeverGrip` | 拒绝 | 拒绝 | ✅ 通过 | 只清理了进气口，不能算修好，还不能通电复测：上盖下面的轴承还没检查（左上轴承有磨损要处理）；锁扣还没扣回。 | RemoveCover / RotorsStopped |
| 22 | 5c 拆：取下左上盖总成 | 左引擎 | 左上盖总成（上盖 + 进气唇口 + 护栅 + 风道） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_UpperCover_L` | 接受 | 接受 | ✅ 通过 | 取下上盖总成（上盖 + 进气唇口 + 护栅 + 风道）。 | PlaceCover / RotorsStopped |
| 23 | 5d 去向：上盖总成翻面放到工作台操作垫 | 总览 | 落点：工作台操作垫 | `WorkbenchArea/WB_Mat/Bench_Mat（落点 FO_Drop_Mat）` | 接受 | 接受 | ✅ 通过 | 左上盖总成（上盖 + 进气唇口 + 护栅 + 风道）放到工作台操作垫。 | LocateBearing / RotorsStopped |
| 24 | 翻盖读保养记录：上盖内侧朝上 | 保养记录（翻盖内侧） | （查看） | `-` | 画面条件成立 | 成立 | ✅ 通过 | 保养标记朝向与竖直向上夹角 1.8° | LocateBearing / RotorsStopped |
| 25 | 故障原位：上盖拆下后的磨损轴承 | 左引擎近看（进气口 / 轴承位） | （查看） | `-` | 画面条件成立 | 成立 | ✅ 通过 | 磨损轴承在原位，原轴承渲染器关闭 | LocateBearing / RotorsStopped |
| 26 | 6 定位故障轴承：点左上轴承 | 左引擎 | 左上轴承（故障件） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_BearingTop_L` | 接受 | 接受 | ✅ 通过 | 找到了：左上轴承 Engine_BearingTop_L 磨损——外圈有磨痕、锈斑，旁边有金属碎屑。光清理进气口修不好它。再点它，取下旧轴承。 | RemoveBearing / RotorsStopped |
| 27 | 7a 更换：取下旧轴承 | 左引擎 | 左上轴承（故障件） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_BearingTop_L` | 接受 | 接受 | ✅ 通过 | 沿转轴取下旧轴承。 | PlaceOldBearing / RotorsStopped |
| 28 | 拒绝：把旧轴承放到上盖的落点 | 总览 | 落点：工作台操作垫 | `WorkbenchArea/WB_Mat/Bench_Mat（落点 FO_Drop_Mat）` | 拒绝 | 拒绝 | ✅ 通过 | 工作台操作垫放的不是左上轴承（故障件）。 | PlaceOldBearing / RotorsStopped |
| 29 | 7b 去向：旧轴承平放进工作台托盘 | 总览 | 落点：螺钉托盘前格 | `WorkbenchArea/WB_Trays/Tray_Screws（落点 FO_Drop_PartsTray）` | 接受 | 接受 | ✅ 通过 | 左上轴承（故障件）放到螺钉托盘前格。 | FetchNewBearing / RotorsStopped |
| 30 | 新旧轴承对比：托盘里的旧件 / 轴承盒上的新件 | 新旧轴承对比 | （查看） | `-` | 画面条件成立 | 成立 | ✅ 通过 | 旧轴承 (-0.507, 0.910, -0.251)，新轴承 (-0.650, 0.956, -0.264)，相距 151 mm | FetchNewBearing / RotorsStopped |
| 31 | 拒绝：新轴承还没装就通电 | 维修座 | 维修座：Dock_PowerSwitch_LeverGrip | `Unit07ServiceDock/Dock_PowerSwitch/Dock_PowerSwitch_Lever/Dock_PowerSwitch_LeverGrip` | 拒绝 | 拒绝 | ✅ 通过 | 只清理了进气口，不能算修好，还不能通电复测：新轴承还没装上；上盖没装好；锁扣还没扣回。 | FetchNewBearing / RotorsStopped |
| 32 | 7c 更换：从工作台轴承盒取新轴承装上 | 工作台 | 新轴承 | `Assets/BorderRepair/Art/Unit07FaultKit/Models/UNIT07_FK_BearingNew.fbx（故障美术包），放在 WorkbenchArea/WB_Trays/Box_Bearings 上` | 接受 | 接受 | ✅ 通过 | 从轴承盒取出新轴承，装到左上轴承位。 | ReinstallCover / RotorsStopped |
| 33 | 装回：新轴承装在原位（上盖装回前） | 左引擎近看（进气口 / 轴承位） | （查看） | `-` | 画面条件成立 | 成立 | ✅ 通过 | 新轴承与原轴承原位距离 0.00 mm；旧轴承在螺钉托盘前格 | ReinstallCover / RotorsStopped |
| 34 | 拒绝：新轴承装上了但上盖没装回就通电 | 维修座 | 维修座：Dock_PowerSwitch_LeverGrip | `Unit07ServiceDock/Dock_PowerSwitch/Dock_PowerSwitch_Lever/Dock_PowerSwitch_LeverGrip` | 拒绝 | 拒绝 | ✅ 通过 | 还不能通电：上盖没装好；锁扣还没扣回。 | ReinstallCover / RotorsStopped |
| 35 | 8a 装回：上盖总成翻回来装回左引擎 | 工作台 | 左上盖总成（上盖 + 进气唇口 + 护栅 + 风道） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_UpperCover_L` | 接受 | 接受 | ✅ 通过 | 把上盖总成从操作垫上翻回来，装回左引擎。 | CloseLatches / RotorsStopped |
| 36 | 拒绝：锁扣没扣回就通电 | 维修座 | 维修座：Dock_PowerSwitch_LeverGrip | `Unit07ServiceDock/Dock_PowerSwitch/Dock_PowerSwitch_Lever/Dock_PowerSwitch_LeverGrip` | 拒绝 | 拒绝 | ✅ 通过 | 还不能通电：锁扣还没扣回。 | CloseLatches / RotorsStopped |
| 37 | 8b 装回：扣回外侧锁扣 | 左引擎 | 左上盖外侧锁扣 | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_CoverLatch_Outer_L` | 接受 | 接受 | ✅ 通过 | 左上盖外侧锁扣已扣回。还有一个锁扣（后侧按 3 转到背面）。 | CloseLatches / RotorsStopped |
| 38 | 拒绝：只扣回一个锁扣就通电 | 维修座 | 维修座：Dock_PowerSwitch_LeverGrip | `Unit07ServiceDock/Dock_PowerSwitch/Dock_PowerSwitch_Lever/Dock_PowerSwitch_LeverGrip` | 拒绝 | 拒绝 | ✅ 通过 | 还不能通电：锁扣还没扣回。 | CloseLatches / RotorsStopped |
| 39 | 8c 装回：扣回后侧锁扣（背面镜头） | 左引擎背面 | 左上盖后侧锁扣 | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_CoverLatch_Rear_L` | 接受 | 接受 | ✅ 通过 | 两个锁扣都扣回了。点断电开关：通电。 | PowerOn / RotorsStopped |
| 40 | 装回：新轴承、上盖总成、锁扣都在原位 | 左引擎近看（进气口 / 轴承位） | （查看） | `-` | 画面条件成立 | 成立 | ✅ 通过 | 两个锁扣都扣回了。点断电开关：通电。 | PowerOn / RotorsStopped |
| 41 | 9 通电：点断电开关手柄 | 维修座 | 维修座：Dock_PowerSwitch_LeverGrip | `Unit07ServiceDock/Dock_PowerSwitch/Dock_PowerSwitch_Lever/Dock_PowerSwitch_LeverGrip` | 接受 | 接受 | ✅ 通过 | 恢复供电，涡轮重新转动。点夹具握把：松开，让七号离座。 | ReleaseAndLift / Clamped |
| 42 | 等待：涡轮恢复转动 | 维修座 | （等待） | `-` | 条件成立 | 成立 | ✅ 通过 | 恢复供电，涡轮重新转动。点夹具握把：松开，让七号离座。 | ReleaseAndLift / Clamped |
| 43 | 10 离座：点夹具握把（松开 → 上浮） | 维修座 | 维修座：Dock_Clamp_L_Grip | `Unit07ServiceDock/Dock_Clamp_L/Dock_Clamp_L_Grip` | 接受 | 接受 | ✅ 通过 | 夹具张开，七号离座上浮。 | Done / SeatedOpen |
| 44 | 等待：离座复测结束（Done） | 维修座 | （等待） | `-` | 条件成立 | 成立 | ✅ 通过 | 复测通过（占位判定）：维修项都做完，左右等高，转子同速。首单原型流程结束。 | Done / SeatedOpen |
| 45 | 复测结果（占位判定）：悬停 3 s、31 帧：左右引擎高度差最大 0.0 mm；轴承已更换 True；进气口已清理 True；上盖和锁扣复位 True。（占位判定：模型没有失衡模拟） | 维修座 | （等待） | `-` | 条件成立 | 成立 | ✅ 通过 | 复测通过（占位判定）：维修项都做完，左右等高，转子同速。首单原型流程结束。 | Done / SeatedOpen |

## 零件最终去向

| 零件 | 实际对象路径 | 最终位置 | 世界坐标 |
|---|---|---|---|
| 左上盖外侧锁扣 | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_CoverLatch_Outer_L` | 七号左引擎原位（已扣回） | (0.486, 1.349, 1.134) |
| 左上盖后侧锁扣 | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_CoverLatch_Rear_L` | 七号左引擎原位（已扣回） | (0.385, 1.332, 1.254) |
| 左上盖总成（上盖 + 进气唇口 + 护栅 + 风道） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_UpperCover_L` | 七号左引擎原位 | (0.374, 1.355, 1.134) |
| 左进气口堵塞（积尘 + 纤维） | `Assets/BorderRepair/Art/Unit07FaultKit/Models/UNIT07_FK_IntakeClog.fbx（故障美术包）挂在 Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_IntakeGuard_L` | 已清理（纤维、积尘已清掉） | (0.372, 1.369, 1.133) |
| 左上轴承（故障件） | `Robot_V4（UNIT07_RobotV4_DockReady）/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_BearingTop_L` | 螺钉托盘前格（WorkbenchArea/WB_Trays/Tray_Screws） | (-0.507, 0.905, -0.251) |
| 新轴承 | `Assets/BorderRepair/Art/Unit07FaultKit/Models/UNIT07_FK_BearingNew.fbx（故障美术包），放在 WorkbenchArea/WB_Trays/Box_Bearings 上` | 七号左上轴承位（新轴承） | (0.380, 1.310, 1.136) |

## 渲染开销：故障美术包开 / 关（Game 视图实测）

“关”= 接入前的样子：美术件全部隐藏、RobotV4 原轴承渲染器打开。数值取 Game 视图 Stats 同一来源（UnityStats），每种状态 10 帧取最大值；含阴影、深度等所有渲染通道，所以一个网格可能计入多次。

| 时刻 | 镜头 | 显示中的美术件 | 三角面 关 → 开 | 三角面增量 | Draw Call 关 → 开 | Draw Call 增量 | Batches 关 → 开 | SetPass 关 → 开 |
|---|---|---|---|---|---|---|---|---|
| 开始（七号悬停，堵塞在进气口，新轴承在轴承盒上） | 左引擎 | 8 | 621,512 → 669,104 | **+47,592** | 881 → 900 | **+19** | 867 → 886 | 73 → 77 |
| 开始（七号悬停，堵塞在进气口，新轴承在轴承盒上） | 工作台 | 8 | 21,245 → 29,885 | **+8,640** | 256 → 259 | **+3** | 189 → 192 | 60 → 64 |
| 开始（七号悬停，堵塞在进气口，新轴承在轴承盒上） | 总览 | 8 | 651,338 → 704,690 | **+53,352** | 1137 → 1158 | **+21** | 1045 → 1066 | 79 → 84 |
| 结束（新轴承在原位，旧轴承在托盘，进气口已清理） | 左引擎 | 4 | 620,404 → 633,820 | **+13,416** | 886 → 893 | **+7** | 872 → 879 | 80 → 83 |
| 结束（新轴承在原位，旧轴承在托盘，进气口已清理） | 工作台 | 4 | 23,017 → 30,433 | **+7,416** | 266 → 269 | **+3** | 199 → 202 | 71 → 73 |
| 结束（新轴承在原位，旧轴承在托盘，进气口已清理） | 总览 | 4 | 651,382 → 669,742 | **+18,360** | 1144 → 1153 | **+9** | 1052 → 1061 | 86 → 89 |

## 占位与未实现

- 锁扣扳开 / 扣回：模型没有锁扣动画，用沿外法线移出 6 mm 表示已扳开（占位表现）
- 上盖总成：上盖 + 进气唇口 + 护栅 + 风道是同级网格，由程序编成一组一起移动（分组待美术确认）
- 取下 / 搬运 / 装回 / 上盖翻面：程序插值移动——竖直抬高越过七号机身，平移途中绕零件中心转向（翻面 / 放平），上盖落点在工作台台灯下方，所以最后低空水平钻进去；没有手部动作或拆卸动画（占位表现）
- 清理进气口：没有清理工具和手部动作，按 纤维 → 护栅积尘 → 唇口积尘 → 积尘垫 逐层隐藏（占位表现）
- 故障轴承定位：磨损轴承美术件（磨痕、锈斑、碎屑）+ 文字指出；没有手转、晃动或异响（诊断仍是占位）
- 轴承盒：工作台 Box_Bearings 是实心块，没有盒内空间；新轴承平放在盒体顶面、开着的盒盖前面（美术没有内腔）
- 保养记录：上盖内侧的贴纸只能看（镜头 7），没有可交互的记录内容；没有接入正式工单系统
- 离座复测：模型没有失衡模拟，只核对维修项、左右引擎等高、转子转速一致（占位判定）
