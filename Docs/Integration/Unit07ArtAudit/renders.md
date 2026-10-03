# 参考图与标注图例

全部是 Unity 相机渲染（布局 B 场景只读打开，未保存；后处理、FXAA 与切片相同），1600×900。位置 / 姿态取自 `tray_measure.json` 与 `engine_measure.json`。标注颜色：
- 黄：提手横杆中心 / 线缆端点；绿：夹爪咬合中心；红：引擎铰轴（Engine_L_Hinge 本地 X）；橙：转子轴；青：引擎与机身的接触点；白：尺寸线；紫：搬运路线。

### `img/T01_tray_on_shelf.png`

`Unit07ServiceDock/Dock_PartsTray` 在托盘架上（原位）。1、2 = 两端提手横杆中心（间距 303.4 mm，横杆沿前后、管径约 10 mm）；3 = 网格原点（盘底中心）。


### `img/T02_two_hand_not_reachable.png`

正面看：在已有动画用过的关节角（±5°）内，两手咬合中心（绿）最接近提手横杆（黄）的一组。红线是还差的距离（两侧各约 41 mm、握持轴偏 15°）。现有托盘的提手太近（303 mm），两手够不到。


### `img/T03_one_hand_normal.png`

单手端盘 · 正常：盘面水平（R 手纵握靠近自己这侧的提手横杆，握点在横杆中心前方 30 mm；托盘挂在 `UNIT07_RobotV4_DockReady/Robot_Rig/Root/Body/Arm_R_RootPivot/Arm_R_Shoulder/Arm_R_Elbow/Arm_R_Wrist` 下）。绿点 = 咬合中心。


### `img/T04_one_hand_left_sink_6deg.png`

单手端盘 · 左侧下沉 6°：预制体根绕 Body 原点、七号前向轴转 6°，关节不变（R 手纵握靠近自己这侧的提手横杆，握点在横杆中心前方 30 mm；托盘挂在 `UNIT07_RobotV4_DockReady/Robot_Rig/Root/Body/Arm_R_RootPivot/Arm_R_Shoulder/Arm_R_Elbow/Arm_R_Wrist` 下）。绿点 = 咬合中心。


### `img/T05_one_hand_tray_tilt_4deg.png`

单手端盘 · 盘面倾斜 4°：只转握盘手的腕骨 4°（R 手纵握靠近自己这侧的提手横杆，握点在横杆中心前方 30 mm；托盘挂在 `UNIT07_RobotV4_DockReady/Robot_Rig/Root/Body/Arm_R_RootPivot/Arm_R_Shoulder/Arm_R_Elbow/Arm_R_Wrist` 下）。绿点 = 咬合中心。


### `img/T06_view_Dock.png`

游戏镜头 `Dock`（FirstOrderCameraRig 原机位、原 FOV 55°），七号悬停、单手端盘。托盘中心在画面 (0.25, 0.49)，包围盒在 1600×900 画面里约 446 px 宽。


### `img/T06_view_Overview.png`

游戏镜头 `Overview`（FirstOrderCameraRig 原机位、原 FOV 62°），七号悬停、单手端盘。托盘中心在画面 (0.22, 0.26)，包围盒在 1600×900 画面里约 300 px 宽。


### `img/T07_view_player_eye_B.png`

布局 B 玩家站位 (-0.40, 0.00, 0.45)，眼高 1.60 m，FOV 55°，看七号机身下方。托盘包围盒约 511 px 宽。


### `img/E01a_engine_xray_front.png`

左引擎原位（落座、夹具闭合），透视（隐藏外壳、进气件与故障包覆盖件），七号左前方。1 = 铰轴原点（Engine_L_Hinge，红线 = 本地 X 转轴）；2 = 转子轴原点（Engine_L_Rotor，橙线 = 本地 X）；3 = ConnectionKey_L（连接键，随引擎骨骼，插在机身 BodySocket_L 里）；4 = ConnectionKey_Bolt_L（连接键螺栓）；5 = BodySocket_L（机身侧插座，留在机身）；6 = EngineAnchor_L（引擎侧连接座，随引擎）；7 = ExternalCable_L 机身端（插头 ExternalCable_L_PlugBody，权重全在 Body）；8 = ExternalCable_L 引擎端（插头 ExternalCable_L_PlugEngine，权重全在 Engine_L_Hinge）；9 = AuxCable_L 机身端（插头 AuxCable_L_PlugBody，权重全在 Body）；10 = AuxCable_L 引擎端（插头 AuxCable_L_PlugEngine，权重全在 Engine_L_Hinge）；白点 = 引擎件与机身件真实网格 ≤ 2 mm 的接触点（只有连接键 ↔ 插座一处）。


### `img/E01b_interface_rear_top.png`

左引擎原位（落座、夹具闭合），引擎与机身之间的接口，七号左后上方（外壳照常显示）。1 = 铰轴原点（Engine_L_Hinge，红线 = 本地 X 转轴）；2 = 转子轴原点（Engine_L_Rotor，橙线 = 本地 X）；3 = ConnectionKey_L（连接键，随引擎骨骼，插在机身 BodySocket_L 里）；4 = ConnectionKey_Bolt_L（连接键螺栓）；5 = BodySocket_L（机身侧插座，留在机身）；6 = EngineAnchor_L（引擎侧连接座，随引擎）；7 = ExternalCable_L 机身端（插头 ExternalCable_L_PlugBody，权重全在 Body）；8 = ExternalCable_L 引擎端（插头 ExternalCable_L_PlugEngine，权重全在 Engine_L_Hinge）；9 = AuxCable_L 机身端（插头 AuxCable_L_PlugBody，权重全在 Body）；10 = AuxCable_L 引擎端（插头 AuxCable_L_PlugEngine，权重全在 Engine_L_Hinge）；白点 = 引擎件与机身件真实网格 ≤ 2 mm 的接触点（只有连接键 ↔ 插座一处）。


### `img/E02_hinge_moved_400mm_cables_stretch.png`

同机位：直接把 `Engine_L_Hinge` 骨骼沿七号左向外移 400 mm。整台引擎跟着走，两根蒙皮线缆被拉成直线（只有 Body / Hinge 两根骨骼，不会下垂、不会断开）。程序不能用移动骨骼来做取出（Idle_Hover 每帧也会写回这根骨骼）。


### `img/E03_old_engine_on_mat_benchcam.png`

游戏镜头 `Bench`（原机位、原 FOV）：旧左引擎（随引擎走的全部网格，不含连接键 / 螺栓、不含蒙皮线缆）直立放在操作垫上，绕竖直轴转 90°（engine_path 选出的落点）。黄色圆片是首单上盖落点标记（编辑模式下可见，运行时隐藏）。


### `img/E04_two_engines_on_mat_benchcam.png`

游戏镜头 `Bench`：旧件（有积尘、磨损轴承、保养贴纸）与新件同时直立放在操作垫上（engine_measure 找到的一对不重叠落点）。新件只是把 `Engine_L_Hinge` 下的网格复制一份、去掉故障包件、恢复原轴承渲染器——外观与旧件除故障包外完全相同（同材质、同 L-03 字样），用来说明可复用范围，不是补件。


### `img/E05_carry_path_overview.png`

游戏镜头 `Overview`：搬运路线（紫）= 原位 → 侧拔 400 mm → 升 / 降到引擎最低点 1.08 m → 水平移到操作垫上方 → 落下（engine_path.json）。旧引擎显示在落点。


## 注意

- 部分图里七号的屏幕和状态灯是蓝色：编辑模式下屏幕脚本（`RobotScreenFace`）没有运行，这是材质的默认显示，不是美术修改，也不代表游戏内效果。
- E03 / E04 里操作垫上的黄色圆片是首单上盖落点标记（编辑模式可见，运行时默认隐藏）。
- 所有标注（小球、细杆、编号）都是渲染时临时加的，场景没有保存。
