# 左引擎替换补件 · Unity 隔离验证

- 2026-10-04 22:46，Unity 6000.0.84f1，场景 `Assets/BorderRepair/Art/Unit07Replacement/Verification/ReplacementKit_Verify.unity`（布局 B `Assets/BorderRepair/FirstOrder/Scenes/LayoutAB/Unit07FirstOrder_LayoutB.unity` 的副本；编辑模式，检查时的显示切换不保存）。
- 测距：集成线修正后的 MeshClearance（双向点到三角形 + 双面 Möller–Trumbore 边穿三角形，0 = 接触或穿插）；不算边到边最近距离、判不出共面重叠，2–5 mm 级数字是估计。

## 1. 坐标标定（补件里的原插头完整复制 vs 场景里的原插头）

| 复制件 | 对应原物体 | 顶点最大偏差 |
|---|---|---|
| Calib_AuxCable_L_PlugEngine | AuxCable_L_PlugEngine | 0.000 mm |
| Calib_AuxCable_L_PlugBody | AuxCable_L_PlugBody | 0.000 mm |
| Calib_ExternalCable_L_PlugBody | ExternalCable_L_PlugBody | 0.000 mm |
| Calib_ExternalCable_L_PlugEngine | ExternalCable_L_PlugEngine | 0.000 mm |

## 2. 显示状态与间距

| 姿态 | 断开线缆（含自由插头 / 卡箍）↔ 七号其余 | 最近的两件 | 两根断开线缆之间 | 固定插座补件 ↔ 机身（原插头同项基线） | 引擎端：断开线缆 ↔ 原线引擎端偏差 |
|---|---|---|---|---|---|
| 编辑默认（布局 B 存档姿态） | 9.1 mm | AuxCable_L_Unplugged ↔ Engine_LowerCover_L | ≥ 50 mm | ExternalCable_L: 0.0（ExternalCable_L_SocketFlange ↔ Body_Shell；原插头基线 0.0）；AuxCable_L: 0.0（AuxCable_L_SocketFlange ↔ Body_Shell；原插头基线 0.0） | 0.00 mm |
| 落座（Pose_Gripper_Closed，维修座拔线时的姿态） | 9.1 mm | AuxCable_L_Unplugged ↔ Engine_LowerCover_L | ≥ 50 mm | ExternalCable_L: 0.0（ExternalCable_L_SocketFlange ↔ Body_Shell；原插头基线 0.0）；AuxCable_L: 0.0（AuxCable_L_SocketFlange ↔ Body_Shell；原插头基线 0.0） | 0.00 mm |
| 悬停 Idle_Hover t=0.00s（引擎摆动） | 9.1 mm | AuxCable_L_Unplugged ↔ Engine_LowerCover_L | ≥ 50 mm | ExternalCable_L: 0.0（ExternalCable_L_SocketFlange ↔ Body_Shell；原插头基线 0.0）；AuxCable_L: 0.0（AuxCable_L_SocketFlange ↔ Body_Shell；原插头基线 0.0） | 0.00 mm |
| 悬停 Idle_Hover t=0.50s（引擎摆动） | 9.1 mm | AuxCable_L_Unplugged ↔ Engine_LowerCover_L | ≥ 50 mm | ExternalCable_L: 0.0（ExternalCable_L_SocketFlange ↔ Body_Shell；原插头基线 0.0）；AuxCable_L: 0.0（AuxCable_L_SocketFlange ↔ Body_Shell；原插头基线 0.0） | 0.00 mm |
| 悬停 Idle_Hover t=1.00s（引擎摆动） | 9.1 mm | AuxCable_L_Unplugged ↔ Engine_LowerCover_L | ≥ 50 mm | ExternalCable_L: 0.0（ExternalCable_L_SocketFlange ↔ Body_Shell；原插头基线 0.0）；AuxCable_L: 0.0（AuxCable_L_SocketFlange ↔ Body_Shell；原插头基线 0.0） | 0.00 mm |
| 悬停 Idle_Hover t=1.50s（引擎摆动） | 9.1 mm | AuxCable_L_Unplugged ↔ Engine_LowerCover_L | ≥ 50 mm | ExternalCable_L: 0.0（ExternalCable_L_SocketFlange ↔ Body_Shell；原插头基线 0.0）；AuxCable_L: 0.0（AuxCable_L_SocketFlange ↔ Body_Shell；原插头基线 0.0） | 0.00 mm |

## 3. 显示核对（落座姿态；数“看得见的”对象）

| 状态 | 黄线 | 灰线 | 黄线机身端（原插头 / 固定插座） | 灰线机身端 | 自由插头 | 卡箍 | 引擎件 |
|---|---|---|---|---|---|---|---|
| Connected | 1 根 | 1 根 | 1 / 0 | 1 / 0 | 0 | 1 | 38 / 38 |
| Unplugged | 1 根 | 1 根 | 0 / 1 | 0 / 1 | 2 | 1 | 38 / 38 |
| EngineRemoved | 0 根 | 0 根 | 0 / 1 | 0 / 1 | 0 | 0 | 0 / 38 |
| Reconnected | 1 根 | 1 根 | 1 / 0 | 1 / 0 | 0 | 1 | 38 / 38 |

（EngineRemoved 是“机身一侧”：旧总成连同断开线缆已经搬走，这里把它们隐藏；Reconnected 用原线和原插头，引擎件换新材质。）

## 4. 旧总成暂存（工作台）

- 引擎件（落座姿态）包围盒中心 (0.2635, 1.1369, 0.4565)，审计记录的起点 (0.2671, 1.1368, 0.4670)（相差 11.0 mm；这里按“编辑默认姿态”的审计值摆放，落座姿态略有不同时按程序实际路线为准）。
- 断开线缆（随旧总成）↔ 工作台：**19.0 mm**（ExternalCable_L_Unplugged ↔ Bench_Mat）；旧引擎件 ↔ 工作台 1.1 mm（Engine_NozzleRing_L ↔ Bench_Mat）。

## 5. 镜头可见性（落座、拔下状态；采样顶点直接可见的比例，射线只打可见网格）

| 镜头 | 黄线固定插座 | 黄线自由插头 | 灰线固定插座 | 灰线自由插头 |
|---|---|---|---|---|
| 维修座 | 69% | 100% | 0% | 0% |
| 左引擎 | 61% | 100% | 0% | 0% |
| 左引擎背面 | 0% | 0% | 75% | 94% |
| 工作台 | 不在画面 | 不在画面 | 不在画面 | 不在画面 |
| 总览 | 0% | 90% | 0% | 75% |
| 右引擎（对照） | 0% | 0% | 0% | 0% |
| 保养记录（翻盖内侧） | 不在画面 | 不在画面 | 不在画面 | 不在画面 |
| 新旧轴承对比 | 不在画面 | 不在画面 | 不在画面 | 不在画面 |
| 左引擎近看（进气口 / 轴承位） | 0% | 0% | 0% | 0% |
| 审计 E01b 机位（寻位参考） | 0% | 0% | 48% | 93% |

## 6. 逐对象材质映射（新左引擎；右引擎与机身不变）

| 对象（Engine_L_Hinge 下） | 原材质 | 新件材质 | 说明 |
|---|---|---|---|
| AuxCable_L_PlugEngine | M_Engine | M_Engine_ReplacementL |  |
| ConnectionKey_Bolt_L | M_Engine | M_Engine | 连接键 / 螺栓留在机身（不随总成），材质不变 |
| ConnectionKey_L | M_Engine | M_Engine | 连接键 / 螺栓留在机身（不随总成），材质不变 |
| Engine_Badge_L | M_Engine | M_Engine_ReplacementL |  |
| Engine_BearingBottom_L | M_Internal | M_Internal_ReplacementL |  |
| Engine_BearingTop_L | M_Internal | M_Internal_ReplacementL | 正常轴承：新件显示 |
| UNIT07_FK_BearingWorn | M_FK_BearingWorn | （不显示） | 磨损轴承（故障件）：新件不显示 |
| UNIT07_FK_BearingTop_L_Worn_Chips | M_FK_MetalChips | （不显示） | 磨损轴承（故障件）：新件不显示 |
| Engine_CoolingFins_L | M_Internal | M_Internal_ReplacementL |  |
| Engine_CoverLatch_Outer_L | M_Engine | M_Engine_ReplacementL |  |
| Engine_CoverLatch_Rear_L | M_Engine | M_Engine_ReplacementL |  |
| Engine_IntakeDuct_L | M_Engine | M_Engine_ReplacementL |  |
| Engine_IntakeGuard_L | M_Engine | M_Engine_ReplacementL |  |
| UNIT07_FK_IntakeClog | M_FK_IntakeClog | （不显示） | 故障包积尘 / 纤维：只属于旧件，新件不显示 |
| UNIT07_FK_IntakeClog_L_Fibers | M_FK_IntakeClog | （不显示） | 故障包积尘 / 纤维：只属于旧件，新件不显示 |
| UNIT07_FK_IntakeClog_L_GuardDust | M_FK_IntakeClog | （不显示） | 故障包积尘 / 纤维：只属于旧件，新件不显示 |
| UNIT07_FK_IntakeClog_L_RimDust | M_FK_IntakeClog | （不显示） | 故障包积尘 / 纤维：只属于旧件，新件不显示 |
| Engine_IntakeLip_L | M_Engine | M_Engine_ReplacementL |  |
| Engine_InternalLead_L | M_Internal | M_Internal_ReplacementL |  |
| Engine_Fan_L | M_Internal | M_Internal_ReplacementL |  |
| Engine_FanNut_L | M_Internal | M_Internal_ReplacementL |  |
| Engine_Shaft_L | M_Internal | M_Internal_ReplacementL |  |
| Engine_LowerCover_L | M_Engine | M_Engine_ReplacementL |  |
| Engine_MotorBracket_L | M_Internal | M_Internal_ReplacementL |  |
| Engine_MotorBracket_Screw_L_1 | M_Internal | M_Internal_ReplacementL |  |
| Engine_MotorBracket_Screw_L_2 | M_Internal | M_Internal_ReplacementL |  |
| Engine_MotorBracket_Screw_L_3 | M_Internal | M_Internal_ReplacementL |  |
| Engine_MotorHousing_L | M_Internal | M_Internal_ReplacementL |  |
| Engine_MountBase_L | M_Internal | M_Internal_ReplacementL |  |
| Engine_MountPost_L_1 | M_Internal | M_Internal_ReplacementL |  |
| Engine_MountPost_L_2 | M_Internal | M_Internal_ReplacementL |  |
| Engine_MountPost_L_3 | M_Internal | M_Internal_ReplacementL |  |
| Engine_MountPost_L_4 | M_Internal | M_Internal_ReplacementL |  |
| Engine_NozzleRing_L | M_Engine | M_Engine_ReplacementL |  |
| Engine_RearCap_L | M_Engine | M_Engine_ReplacementL |  |
| Engine_UpperCover_L | M_Engine | M_Engine_ReplacementL |  |
| UNIT07_FK_CoverLabel | M_FK_CoverLabel | （不显示） | 旧保养贴纸：随旧上盖留在旧总成，新件不显示 |
| Engine_Windings_L | M_Internal | M_Internal_ReplacementL |  |
| EngineAnchor_L | M_Engine | M_Engine_ReplacementL |  |
| ExternalCable_L_PlugEngine | M_Engine | M_Engine_ReplacementL |  |
| ExternalCable_L（机器人根下，蒙皮线） | M_Engine | M_Engine_ReplacementL | 新引擎重新连接时显示的线（原线复用）；断开线缆补件仍用原 M_Engine（随旧总成） |
| AuxCable_L（机器人根下，蒙皮线） | M_Engine | M_Engine_ReplacementL | 新引擎重新连接时显示的线（原线复用）；断开线缆补件仍用原 M_Engine（随旧总成） |
