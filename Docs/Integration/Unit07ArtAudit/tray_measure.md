# 端盘实测（Unity，布局 B 场景只读打开，未保存）

- 托盘：`Unit07ServiceDock/Dock_PartsTray`，源 `Assets/BorderRepair/Art/Unit07ServiceDock/UNIT07_ServiceDock.fbx`，1440 三角面，单一网格（盘体 + 两端提手合并），材质 M_Dock_Ivory
- 组件：MeshFilter、MeshRenderer、DockPartProperties、BoxCollider、DockInteractable、DockPickable
- 原点（世界）(0.3390, 0.7850, 1.4248)，朝向 X 轴 (0.2588, 0.0000, 0.9659)；本地包围盒 (-0.1568, -0.0900, 0.0000) … (0.1568, 0.0900, 0.0518)（含提手）
- 盘体（不含提手）本地 (-0.1300, -0.0900, 0.0000) … (0.1300, 0.0900, 0.0396) → 260.0 × 180.0 mm、盘壁高 39.6 mm（导入后托盘本地 Z 向上、X 为长轴、Y 为前后）
- 提手横杆 0：中心（本地）(0.1517, 0.0000, 0.0469)，方向（本地）(0.00, 1.00, 0.00)，外包长度 92.7 mm，管径约 9.7 mm，世界 (0.3783, 0.8319, 1.5713)
- 提手横杆 1：中心（本地）(-0.1517, 0.0000, 0.0469)，方向（本地）(0.00, 1.00, 0.00)，外包长度 92.7 mm，管径约 9.7 mm，世界 (0.2997, 0.8319, 1.2782)
- 两横杆中心距 **303.4 mm**；原点在盘底中心，离两横杆中心连线 46.9 mm

## 夹爪开度（真实齿网格最近距离）

| 侧 | 姿态 | 上下齿最近距离 mm | 上 / 下爪相对 Closed |
|---|---|---|---|
| L | Closed | 5.3 | 0.0° / 0.0° |
| L | Half | 21.5 | 17.0° / -17.0° |
| L | Open | 41.0 | 36.0° / -36.0° |
| L | 夹 10 mm 横杆 | 10.0（开度参数 0.32，0 = Closed、1 = Half） | 咬合中心（腕骨本地）(-0.0824, -0.0007, -0.0284) |
| R | Closed | 5.3 | 0.0° / 0.0° |
| R | Half | 21.5 | 17.0° / -17.0° |
| R | Open | 41.0 | 36.0° / -36.0° |
| R | 夹 10 mm 横杆 | 10.0（开度参数 0.32，0 = Closed、1 = Half） | 咬合中心（腕骨本地）(-0.0824, -0.0007, 0.0284) |

- 静止姿态（Idle_Hover 第 0 帧）两侧咬合中心相距 **528.8 mm**（世界 L (0.0813, 0.7551, 0.6300)，R (0.2182, 0.7551, 1.1407)）；七号本体坐标 L (-0.2644, -0.3849, 0.2332)，R (0.2644, -0.3849, 0.2332)
- 夹爪指向（世界）L (-0.872, -0.430, 0.234)；爪轴 L (-0.259, 0.000, -0.966)；七号前向（Body +Z）(-0.966, 0.000, 0.259)、左右（Body +X = 七号右侧）(0.259, 0.000, 0.966)

## 已有动画里的关节角范围（绕各骨骼本地 X，相对静止姿态）

| 侧 | 关节 | 最小 | 最大 | 离轴最大 |
|---|---|---|---|---|
| L | pivot | -10.0° | 0.0° | 0.00° |
| L | shoulder | 0.0° | 154.3° | 0.00° |
| L | elbow | -171.3° | 0.0° | 0.00° |
| L | wrist | 0.0° | 0.0° | 0.00° |
| R | pivot | 0.0° | 10.0° | 0.00° |
| R | shoulder | -26.0° | 154.3° | 0.00° |
| R | elbow | -171.3° | 22.0° | 0.00° |
| R | wrist | 0.0° | 25.0° | 0.00° |

## 双手端盘求解

盘长轴沿七号左右、横杆沿前后（唯一能让左右两只手各握一端的摆法）。扫描盘心：前后 −0.05…0.40 m、上下（相对 Body 原点）−0.50…−0.10 m，步长 25 mm；两种握法。
咬合中心到横杆中心误差 < 3 mm、握持轴与横杆夹角 < 10° 的解：**18 个**（纵握 0，横握 18）。

| 握法 | 盘心（Body 本地 前, 上） | 左臂 pivot/肩/肘/腕 | 右臂 | 盘↔机身最小 | 机械臂↔机身最小 | 关节角在已有动画范围内 |
|---|---|---|---|---|---|---|
| 横握 | (-0.050, -0.500) | 65 / -220 / -83 / -205 | -65 / 140 / -83 / 206 | 20.7 mm（Dock_PartsTray↔Arm_R_HingePin_Lower） | 9.0 mm（Arm_L_ShoulderYoke↔Chassis_ArmHardpoint_L） | 否：L.pivot 65°（动画 -10…0°），L.shoulder -220°（动画 0…154°），L.wrist -205°（动画 0…0°），R.pivot -65°（动画 0…10°），R.wrist 206°（动画 0…25°） |
| 横握 | (0.075, -0.500) | 301 / -28 / 99 / -149 | 59 / 332 / 99 / 149 | 21.4 mm（Dock_PartsTray↔Arm_L_HingePin_Lower） | 9.0 mm（Arm_L_ShoulderYoke↔Chassis_ArmHardpoint_L） | 否：L.pivot 301°（动画 -10…0°），L.shoulder -28°（动画 0…154°），L.elbow 99°（动画 -171…0°），L.wrist -149°（动画 0…0°），R.pivot 59°（动画 0…10°），R.shoulder 332°（动画 -26…154°），R.elbow 99°（动画 -171…22°），R.wrist 149°（动画 0…25°） |
| 横握 | (0.100, -0.500) | -43 / 329 / -270 / -134 | 43 / -31 / 90 / 134 | 22.1 mm（Dock_PartsTray↔Arm_L_HingePin_Lower） | 9.0 mm（Arm_L_ShoulderYoke↔Chassis_ArmHardpoint_L） | 否：L.pivot -43°（动画 -10…0°），L.shoulder 329°（动画 0…154°），L.elbow -270°（动画 -171…0°），L.wrist -134°（动画 0…0°），R.pivot 43°（动画 0…10°），R.shoulder -31°（动画 -26…154°），R.elbow 90°（动画 -171…22°），R.wrist 134°（动画 0…25°） |
| 横握 | (0.075, -0.475) | 301 / -49 / 118 / -148 | 59 / 311 / 118 / 148 | 21.9 mm（Dock_PartsTray↔Arm_R_HingePin_Lower） | 6.6 mm（Arm_R_ForearmHub↔ChinFascia） | 否：L.pivot 301°（动画 -10…0°），L.shoulder -49°（动画 0…154°），L.elbow 118°（动画 -171…0°），L.wrist -148°（动画 0…0°），R.pivot 59°（动画 0…10°），R.shoulder 311°（动画 -26…154°），R.elbow 118°（动画 -171…22°），R.wrist 148°（动画 0…25°） |
| 横握 | (0.125, -0.475) | -67 / -50 / 123 / 21 | 67 / -50 / 123 / 339 | 22.5 mm（Dock_PartsTray↔Arm_L_HingePin_Lower） | 5.3 mm（Arm_R_ForearmHub↔ChinFascia） | 否：L.pivot -67°（动画 -10…0°），L.shoulder -50°（动画 0…154°），L.elbow 123°（动画 -171…0°），L.wrist 21°（动画 0…0°），R.pivot 67°（动画 0…10°），R.shoulder -50°（动画 -26…154°），R.elbow 123°（动画 -171…22°），R.wrist 339°（动画 0…25°） |
| 横握 | (0.100, -0.475) | -43 / 309 / -251 / -134 | 43 / -51 / 109 / 134 | 21.9 mm（Dock_PartsTray↔Arm_L_HingePin_Lower） | 3.9 mm（Arm_L_ForearmHub↔ChinFascia） | 否：L.pivot -43°（动画 -10…0°），L.shoulder 309°（动画 0…154°），L.elbow -251°（动画 -171…0°），L.wrist -134°（动画 0…0°），R.pivot 43°（动画 0…10°），R.shoulder -51°（动画 -26…154°），R.elbow 109°（动画 -171…22°），R.wrist 134°（动画 0…25°） |
| 横握 | (-0.050, -0.475) | 65 / -200 / -103 / -206 | -65 / 160 / -103 / 206 | 20.7 mm（Dock_PartsTray↔Arm_R_HingePin_Lower） | 0.1 mm（Arm_L_ForearmHub↔Chassis_ArmHardpoint_L） | 否：L.pivot 65°（动画 -10…0°），L.shoulder -200°（动画 0…154°），L.wrist -206°（动画 0…0°），R.pivot -65°（动画 0…10°），R.shoulder 160°（动画 -26…154°），R.wrist 206°（动画 0…25°） |
| 横握 | (-0.050, -0.450) | 65 / -176 / -123 / -206 | -65 / 184 / -123 / 206 | 22.2 mm（Dock_PartsTray↔Arm_R_HingePin_Lower） | 0.0 mm（Arm_L_ElbowWasher_B↔Body_LowerModule） | 否：L.pivot 65°（动画 -10…0°），L.shoulder -176°（动画 0…154°），L.wrist -206°（动画 0…0°），R.pivot -65°（动画 0…10°），R.shoulder 184°（动画 -26…154°），R.wrist 206°（动画 0…25°） |

## 限制在已有动画用过的角度（±5°）

范围：根环 -15…15°，肩 -31…159°，肘 -176…27°，腕 -30…30°（根环、腕部左右镜像取并集）。

- **双手握两端提手：在这个范围内做不到。** 最接近的一组：纵握，盘心 Body 本地 (0, -0.300, 0.275)，左手离横杆 40.8 mm、右手 40.8 mm，握持轴与横杆夹角 15° / 15°。
- 范围内两侧咬合中心最近能靠到 **404.9 mm**（随机 400 组镜像姿态取最小，左臂 -14/-19/-28/2）；提手横杆中心距是 303.4 mm。

## 单手握一端提手（限制在已有动画角度 ±5°）

| 手 | 握法 | 盘心（Body 本地 左右, 上, 前） | 关节角 | 盘↔机身最小（不含握盘那只手的爪） | 盘↔另一只手 | 机械臂↔机身 |
|---|---|---|---|---|---|---|
| R | 纵握 | (0.10, -0.40, 0.25) | 3/36/-59/16 | 3.6 mm（Dock_PartsTray↔Arm_R_GripperBracket（近侧横杆，握点偏 30 mm）） | 100.0 mm | 9.0 mm |
| L | 纵握 | (-0.15, -0.40, 0.15) | 12/44/-67/-15 | 0.0 mm（Dock_PartsTray↔Arm_L_ForearmShell（近侧横杆，握点偏 -30 mm）） | 100.0 mm | 9.0 mm |
| L | 纵握 | (-0.15, -0.35, 0.20) | 9/1/-29/-12 | 0.0 mm（Dock_PartsTray↔Arm_L_ForearmDrive（近侧横杆，握点偏 -30 mm）） | 100.0 mm | 9.0 mm |
| L | 纵握 | (-0.10, -0.40, 0.10) | -4/115/-129/-30 | 0.0 mm（Dock_PartsTray↔Arm_L_GripperBracket（近侧横杆，握点偏 -30 mm）） | 100.0 mm | 9.0 mm |
| L | 纵握 | (-0.10, -0.35, 0.10) | -4/126/-152/-30 | 0.0 mm（Dock_PartsTray↔Arm_L_ForearmShell（近侧横杆，握点偏 -30 mm）） | 91.0 mm | 9.0 mm |
| L | 纵握 | (-0.10, -0.30, 0.10) | -5/115/-153/26 | 0.0 mm（Dock_PartsTray↔Arm_L_ForearmDrive（近侧横杆，握点偏 -30 mm）） | 84.1 mm | 9.0 mm |
| L | 纵握 | (-0.10, -0.40, 0.15) | -4/70/-88/14 | 0.0 mm（Dock_PartsTray↔Arm_L_ForearmDrive（近侧横杆，握点偏 -30 mm）） | 100.0 mm | 9.0 mm |
| L | 纵握 | (-0.10, -0.35, 0.15) | -3/67/-99/29 | 0.0 mm（Dock_PartsTray↔Arm_L_ForearmDrive（近侧横杆，握点偏 -30 mm）） | 100.0 mm | 9.0 mm |
| L | 纵握 | (-0.10, -0.40, 0.20) | -3/27/-48/-17 | 0.0 mm（Dock_PartsTray↔Arm_L_ForearmShell（近侧横杆，握点偏 -30 mm）） | 100.0 mm | 9.0 mm |
| L | 纵握 | (-0.10, -0.35, 0.20) | -2/25/-55/30 | 0.0 mm（Dock_PartsTray↔Arm_L_ForearmDrive（近侧横杆，握点偏 -30 mm）） | 100.0 mm | 9.0 mm |

单手可行解共 59 个（抽样测间距）。

## 提手间距需求（假想横杆，不用现有托盘网格）

| 横杆中心距 | 可行解数（误差 < 3 mm、夹角 < 10°） | 最好的一组：握法、横杆高度 / 前后（Body 本地）、左臂关节 | 机械臂↔机身最小 |
|---|---|---|---|
| 380.0 mm | 0 | — | — |
| 420.0 mm | 0 | — | — |
| 460.0 mm | 3 | 纵握，上 -0.350 / 前 0.200，-9/60/-87/-30 | 9.0 mm |
| 500.0 mm | 9 | 纵握，上 -0.325 / 前 0.150，-4/98/-127/-30 | 9.0 mm |
| 530.0 mm | 10 | 纵握，上 -0.350 / 前 0.150，1/88/-110/-18 | 9.0 mm |
| 560.0 mm | 6 | 纵握，上 -0.350 / 前 0.150，8/77/-102/-29 | 9.0 mm |

## 单手端盘参考姿态（推荐：R 手纵握）

- 托盘挂到 `UNIT07_RobotV4_DockReady/Robot_Rig/Root/Body/Arm_R_RootPivot/Arm_R_Shoulder/Arm_R_Elbow/Arm_R_Wrist` 下的本地位姿：位置 (-0.1231, -0.0861, -0.1026)，旋转（四元数）-0.5911, -0.5396, -0.4257, 0.4221；夹爪开度参数 0.32（Closed→Half 之间，齿间距 10 mm）

| 姿态 | 怎么摆 | 盘面与水平夹角 | 盘↔机身最小 | 盘↔另一只手 | 关节角 |
|---|---|---|---|---|---|
| 正常 | 盘面水平 | 0.0° | 3.6 mm（Dock_PartsTray↔Arm_R_GripperBracket） | 100.0 mm | 3.2/36.3/-58.6/16.3 |
| 左侧下沉 3° | 预制体根绕 Body 原点、七号前向轴转 3°，关节不变 | 3.0° | 3.6 mm（Dock_PartsTray↔Arm_R_GripperBracket） | 100.0 mm | 3.2/36.3/-58.6/16.3 |
| 左侧下沉 6° | 预制体根绕 Body 原点、七号前向轴转 6°，关节不变 | 6.0° | 3.6 mm（Dock_PartsTray↔Arm_R_GripperBracket） | 100.0 mm | 3.2/36.3/-58.6/16.3 |
| 盘面倾斜 -4° | 只转腕骨 -4°（盘随手腕横滚） | 4.0° | 3.6 mm（Dock_PartsTray↔Arm_R_GripperBracket） | 100.0 mm | 3.2/36.3/-58.6/12.3 |
| 盘面倾斜 +4° | 只转腕骨 +4°（盘随手腕横滚） | 4.0° | 3.6 mm（Dock_PartsTray↔Arm_R_GripperBracket） | 100.0 mm | 3.2/36.3/-58.6/20.3 |

## 维修座上方：悬停 → 落座，手里的盘与维修座（推荐姿态）

| 七号根离锚点高度 | 盘↔维修座最小 | 最近的维修座对象 |
|---|---|---|
| 300.0 mm | 233.7 mm | Dock_MagneticBox |
| 280.0 mm | 215.4 mm | Dock_MagneticBox |
| 260.0 mm | 197.4 mm | Dock_MagneticBox |
| 240.0 mm | 179.9 mm | Dock_MagneticBox |
| 220.0 mm | 162.9 mm | Dock_MagneticBox |
| 200.0 mm | 146.7 mm | Dock_MagneticBox |
| 180.0 mm | 131.5 mm | Dock_MagneticBox |
| 160.0 mm | 117.8 mm | Dock_MagneticBox |
| 140.0 mm | 106.1 mm | Dock_MagneticBox |
| 120.0 mm | 97.1 mm | Dock_MagneticBox |
| 100.0 mm | 87.7 mm | Dock_MagneticBox |
| 80.0 mm | 75.5 mm | Dock_MagneticBox |
| 60.0 mm | 67.0 mm | Dock_MagneticBox |
| 40.0 mm | 63.4 mm | Dock_MagneticBox |
| 20.0 mm | 63.2 mm | Dock_MagneticBox |
| 0.0 mm | 63.2 mm | Dock_TrayShelf |

- 维修座初始悬停高度为锚点上方 120 mm（`Unit07DockController.hoverHeight`）。盘第一次碰到维修座的高度：整个落座过程都没碰到

