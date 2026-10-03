# 整台左引擎替换实测（Unity，布局 B 场景只读打开，未保存）

- 七号实例 `UNIT07_RobotV4_DockReady`（源 `Assets/RobotV4/Model/robot-final.fbx`），停靠在维修座锚点上（场景默认：落座、夹具闭合）。坐标：Unity 世界，米，Y 向上。七号前向 = Body +Z = (-0.966, 0.000, 0.259)；七号自己的左 = Body −X = (-0.259, 0.000, -0.966)。
- `Engine_L_Hinge` 骨骼原点 (0.2904, 1.1350, 0.6251)，转轴（骨骼本地 X）(0.2588, 0.0000, 0.9659)；`Engine_L_Rotor` 原点 (0.2564, 1.0280, 0.4375)，转轴 (-0.0566, -0.9890, -0.1369)

## 接口：随引擎走的件 ↔ 留在机身的件（真实网格最近距离 ≤ 2 mm）

| 随引擎走 | 留在机身 | 最近距离 | 接触点（世界） |
|---|---|---|---|
| ConnectionKey_L | BodySocket_L | 0.0 mm | (0.2999, 1.1249, 0.6153) |

共 1 对。没有出现在这张表里的机身件和引擎没有直接接触。

## 蒙皮线缆与引线

- `UNIT07_RobotV4_DockReady/ExternalCable_L`：SkinnedMeshRenderer，327 顶点，骨骼 [Root, Body, Engine_R_Hinge, Engine_R_Rotor, Arm_R_RootPivot, Arm_R_Shoulder, Arm_R_Elbow, Arm_R_Wrist, Arm_R_JawUpper, Arm_R_JawLower, Engine_L_Hinge, Engine_L_Rotor, Arm_L_RootPivot, Arm_L_Shoulder, Arm_L_Elbow, Arm_L_Wrist, Arm_L_JawUpper, Arm_L_JawLower]，根骨骼 Root；材质 M_Engine
  - 机身端（权重全在 Body）中心 (0.2505, 1.0688, 0.6227)，最近插头 `ExternalCable_L_PlugBody` 距离 0.0 mm；引擎端（权重全在 Engine_L_Hinge）中心 (0.2345, 1.0701, 0.5693)，最近插头 `ExternalCable_L_PlugEngine` 距离 0.0 mm；两端直线距离 55.7 mm
  - 混合权重顶点 259/327；沿线缆从机身端到引擎端 5 段的平均 Hinge 权重：0.03 → 0.23 → 0.53 → 0.81 → 0.98
  - 引擎沿七号左向外移 50 mm：两端间距 +49.8 mm；外移 400 mm：+398.5 mm（蒙皮只有两根骨骼，线缆被直线拉长，不会下垂）
  - 插头拆卸方向（FBX 自定义属性 remove_dir，Blender 坐标）：机身端 1,0,0，引擎端 -1,0,0

- `UNIT07_RobotV4_DockReady/AuxCable_L`：SkinnedMeshRenderer，306 顶点，骨骼 [Root, Body, Engine_R_Hinge, Engine_R_Rotor, Arm_R_RootPivot, Arm_R_Shoulder, Arm_R_Elbow, Arm_R_Wrist, Arm_R_JawUpper, Arm_R_JawLower, Engine_L_Hinge, Engine_L_Rotor, Arm_L_RootPivot, Arm_L_Shoulder, Arm_L_Elbow, Arm_L_Wrist, Arm_L_JawUpper, Arm_L_JawLower]，根骨骼 Root；材质 M_Engine
  - 机身端（权重全在 Body）中心 (0.3691, 1.0925, 0.5900)，最近插头 `AuxCable_L_PlugBody` 距离 0.0 mm；引擎端（权重全在 Engine_L_Hinge）中心 (0.3557, 1.0906, 0.5438)，最近插头 `AuxCable_L_PlugEngine` 距离 0.0 mm；两端直线距离 48.1 mm
  - 混合权重顶点 242/306；沿线缆从机身端到引擎端 5 段的平均 Hinge 权重：0.02 → 0.20 → 0.49 → 0.77 → 0.97
  - 引擎沿七号左向外移 50 mm：两端间距 +49.8 mm；外移 400 mm：+398.5 mm（蒙皮只有两根骨骼，线缆被直线拉长，不会下垂）
  - 插头拆卸方向（FBX 自定义属性 remove_dir，Blender 坐标）：机身端 1,0,0，引擎端 -1,0,0

- `UNIT07_RobotV4_DockReady/Robot_Rig/Root/Body/Engine_L_Hinge/Engine_InternalLead_L`（随引擎，MeshRenderer，材质 M_Internal）：端点 A (0.2301, 1.0785, 0.5526) 附近：ExternalCable_L_PlugEngine 2.1 mm、Engine_LowerCover_L 6.5 mm、Arm_L_RootBolts 30.0 mm；端点 B (0.1989, 1.1054, 0.4116) 附近：Engine_Windings_L 2.5 mm、Engine_MotorHousing_L 13.5 mm、Arm_L_RootBolts 30.0 mm
- `UNIT07_RobotV4_DockReady/Robot_Rig/Root/Body/Harness_Engine_L`（机身，MeshRenderer，材质 M_Internal）：端点 A (0.3421, 1.0350, 0.6957) 附近：PCB_Main_Connector_Engine_L 0.0 mm、Harness_Engine_L_PlugA 2.0 mm、PCB_Main 12.0 mm；端点 B (0.2541, 1.0762, 0.6464) 附近：ExternalCable_L_PlugBody 0.0 mm、Harness_Engine_L_PlugB 2.0 mm、Body_Shell 10.6 mm
- `UNIT07_RobotV4_DockReady/Robot_Rig/Root/Body/Harness_Engine_L_PlugA`（机身，MeshRenderer，材质 M_Internal）：端点 A (0.3331, 1.0485, 0.6898) 附近：Harness_Engine_L 1.9 mm、PCB_Main_Connector_Engine_L 13.5 mm、PCB_Main 25.5 mm；端点 B (0.3466, 1.0335, 0.6978) 附近：PCB_Main_Connector_Engine_L 0.5 mm、Harness_Engine_L 5.3 mm、PCB_Main 10.5 mm
- `UNIT07_RobotV4_DockReady/Robot_Rig/Root/Body/Harness_Engine_L_PlugB`（机身，MeshRenderer，材质 M_Internal）：端点 A (0.2509, 1.0805, 0.6457) 附近：ExternalCable_L_PlugBody 0.4 mm、Harness_Engine_L 5.3 mm、Body_Shell 9.1 mm；端点 B (0.2654, 1.0695, 0.6576) 附近：Harness_Engine_L 3.5 mm、ExternalCable_L_PlugBody 13.6 mm、Body_Shell 22.0 mm
- `UNIT07_RobotV4_DockReady/Robot_Rig/Root/Body/PCB_Main_Connector_Engine_L`（机身，MeshRenderer，材质 M_Internal）：端点 A (0.3315, 1.0346, 0.6841) 附近：Harness_Engine_L_PlugA 5.7 mm、Harness_Engine_L 10.2 mm、PCB_Main 11.6 mm；端点 B (0.3483, 1.0234, 0.7038) 附近：PCB_Main 0.4 mm、PCB_Sled 11.4 mm、Harness_Engine_L_PlugA 11.7 mm
- `UNIT07_RobotV4_DockReady/Robot_Rig/Root/Body/ExternalCable_Clip_L`（机身，MeshRenderer，材质 M_Engine）：端点 A (0.2549, 1.0350, 0.6091) 附近：Body_Shell 23.5 mm、Arm_L_RootBolts 30.0 mm、Arm_L_RootRing 30.0 mm；端点 B (0.2390, 1.0430, 0.6104) 附近：Body_Shell 26.2 mm、ExternalCable_L_PlugBody 28.8 mm、Arm_L_RootBolts 30.0 mm
- `UNIT07_RobotV4_DockReady/Robot_Rig/Root/Body/BodySocket_L`（机身，MeshRenderer，材质 M_Body）：端点 A (0.2610, 1.0796, 0.6290) 附近：ExternalCable_L_PlugBody 0.9 mm、Body_Shell 1.9 mm、Harness_Engine_L_PlugB 13.5 mm；端点 B (0.3176, 1.1604, 0.6132) 附近：Body_Shell 1.8 mm、ConnectionKey_L 23.4 mm、EngineAnchor_L 27.5 mm
- `UNIT07_RobotV4_DockReady/Robot_Rig/Root/Body/BodySocket_Knob_L`（机身，MeshRenderer，材质 M_Body）：端点 A (0.2812, 1.1083, 0.6190) 附近：BodySocket_L 2.3 mm、EngineAnchor_L 17.1 mm、ConnectionKey_L 17.9 mm；端点 B (0.2965, 1.0977, 0.6198) 附近：BodySocket_L 2.3 mm、Body_Shell 13.8 mm、EngineAnchor_L 27.4 mm

## 取出与搬运（真实网格，夹具闭合、落座状态）

- 随引擎整体移动的网格 37 件（不含先拔出的连接键 / 螺栓），整体包围盒 258.8 × 307.4 × 282.5 mm（世界轴），中心 (0.2671, 1.1368, 0.4670)，最低点 y 0.983

| 固定件 | 方向（七号 Body） | 拔多远脱开配合件 | 途中离其它件最小 | 最近的其它件 |
|---|---|---|---|---|
| ConnectionKey_L | +X（世界 (0.26, 0.00, 0.97)） | 55.0 mm | 4.2 mm | Body_Shell |
| ConnectionKey_L | -X（世界 (-0.26, 0.00, -0.97)） | 65.0 mm | 0.0 mm | Engine_LowerCover_L |
| ConnectionKey_L | +Y（世界 (0.00, 1.00, 0.00)） | 40.0 mm | 0.0 mm | Body_Shell |
| ConnectionKey_L | -Y（世界 (0.00, -1.00, 0.00)） | 65.0 mm | 0.0 mm | BodySocket_Knob_L |
| ConnectionKey_L | +Z（世界 (-0.97, 0.00, 0.26)） | 45.0 mm | 1.5 mm | Body_Shell |
| ConnectionKey_L | -Z（世界 (0.97, 0.00, -0.26)） | 45.0 mm | 0.0 mm | Body_Shell |
| ConnectionKey_Bolt_L | +X（世界 (0.26, 0.00, 0.97)） | 40.0 mm | 0.0 mm | BodySocket_L |
| ConnectionKey_Bolt_L | -X（世界 (-0.26, 0.00, -0.97)） | 45.0 mm | 0.0 mm | Engine_LowerCover_L |
| ConnectionKey_Bolt_L | +Y（世界 (0.00, 1.00, 0.00)） | 25.0 mm | 12.0 mm | Engine_LowerCover_L |
| ConnectionKey_Bolt_L | -Y（世界 (0.00, -1.00, 0.00)） | 25.0 mm | 17.7 mm | BodySocket_L |
| ConnectionKey_Bolt_L | +Z（世界 (-0.97, 0.00, 0.26)） | 45.0 mm | 17.7 mm | Engine_LowerCover_L |
| ConnectionKey_Bolt_L | -Z（世界 (0.97, 0.00, -0.26)） | 45.0 mm | 16.3 mm | Engine_LowerCover_L |

| 阶段 | 位移 | 与机身 / 维修座 / 工作台最小间距 | 最近的对象（引擎件 ↔ 环境件） |
|---|---|---|---|
| 侧拔 | 10.0 mm | 22.1 mm | EngineAnchor_L ↔ BodySocket_L |
| 侧拔 | 20.0 mm | 32.1 mm | EngineAnchor_L ↔ BodySocket_L |
| 侧拔 | 30.0 mm | 42.1 mm | EngineAnchor_L ↔ BodySocket_L |
| 侧拔 | 40.0 mm | 52.1 mm | EngineAnchor_L ↔ BodySocket_L |
| 侧拔 | 50.0 mm | 62.1 mm | EngineAnchor_L ↔ BodySocket_L |
| 侧拔 | 100.0 mm | 100.0 mm | - ↔ - |
| 侧拔 | 150.0 mm | 100.0 mm | - ↔ - |
| 侧拔 | 200.0 mm | 100.0 mm | - ↔ - |
| 侧拔 | 250.0 mm | 100.0 mm | - ↔ - |
| 侧拔 | 300.0 mm | 100.0 mm | - ↔ - |
| 侧拔 | 350.0 mm | 100.0 mm | - ↔ - |
| 侧拔 | 400.0 mm | 100.0 mm | - ↔ - |

- 操作垫 `WorkbenchArea/WB_Mat/Bench_Mat`：800.0 × 460.0 mm（世界 x × z），顶面 y 0.904；垫面上方的工作台物件 7 件：Diag_Probe、Lamp_ArmUpper、Lamp_Bulb、Lamp_Head、Bench_ArmCradles、Room_Conduits、Tray_OldParts
- 引擎直立（原安装朝向）、整台都在垫子上（30 mm 网格，绕竖直轴 0/90/180/270°），共试 454 个位置：
  - 工作台现状：离其它物件 > 5 mm 的落点 **53 个**；最好的位置离 Bench_ArmCradles 只有 41.6 mm
  - 移走诊断探针（Diag_Probe）后：**53 个**；最好的位置离 Bench_ArmCradles 41.6 mm
- 两台引擎（旧件落点 + 新件展示）同时直立放在垫子上、互相间隔 ≥ 20 mm：可以，例如旧件中心 (0.070, 1.059, -0.530)（转 0°）、新件中心 (-0.230, 1.059, -0.350)（转 90°）
- 搬运路线：侧拔 400 mm → 竖直抬高 541.7 mm（沿途最高障碍顶 y 1.495）→ 水平移到落点上方 → 竖直落到垫面（落点 (0.010, 1.059, -0.530)，转 270°，落下后离最近物件 41.6 mm：Bench_ArmCradles）
- 搬运全程（不含最后贴垫面）最小间距 **0.0 mm**（Engine_NozzleRing_L ↔ Lamp_Head）；侧拔 0–400 mm 段最小 **22.1 mm**

| 容器 | 尺寸（世界包围盒 x × y × z，mm） | 能否容纳整台引擎（258.8 × 307.4 × 282.5） |
|---|---|---|
| `WorkbenchArea/WB_Mat/Bench_Mat` | 800.0 × 4.0 × 460.0 | 可以（见上） |
| `WorkbenchArea/WB_Trays/Tray_OldParts` | 180.0 × 35.0 × 198.0 | 不能 |
| `WorkbenchArea/WB_Trays/Tray_Screws` | 140.0 × 22.0 × 160.0 | 不能 |
| `WorkbenchArea/WB_Trays/Box_Bearings` | 100.0 × 110.0 × 100.0 | 不能 |
| `Unit07ServiceDock/Dock_PartsTray` | 255.0 × 51.8 × 349.4 | 可以（见上） |
| `Unit07ServiceDock/Dock_MagneticBox` | 80.0 × 34.0 × 97.6 | 不能 |
| `Unit07ServiceDock/Dock_TrayShelf` | 335.3 × 30.0 × 448.5 | 可以（见上） |

