# 整台左引擎：轴向核对、连接键、搬运路线（补测）

## Blender remove_dir 在 Unity 七号 Body 坐标里的方向（实测）

| 件 | Blender remove_dir | 件中心相对 Engine_L_Hinge（Body 本地） | 结论 |
|---|---|---|---|
| Engine_CoverLatch_Outer_L | +X | (-0.274, 0.098, -0.020) | 件在铰点的 Body −X（七号左 / 外侧） |
| Engine_CoverLatch_Rear_L | +Y | (-0.170, 0.073, -0.141) | 件在铰点的 Body −X（七号左 / 外侧） |
| Engine_RearCap_L | +Y | (-0.170, -0.018, -0.137) | 件在铰点的 Body −X（七号左 / 外侧） |
| Engine_UpperCover_L | ≈+Z | (-0.159, 0.107, -0.020) | 件在铰点的 Body −X（七号左 / 外侧） |

由此：Blender +X（外侧）= Unity Body −X（七号左）；Blender +Y（后）= Unity Body −Z；Blender +Z = Unity Body +Y。即 Unity Body = (−x, z, −y)。

## 连接键留在机身插座里，引擎沿七号左向外滑

| 外滑 | 引擎件 ↔ 连接键 | 引擎件 ↔ 机身其它 / 环境 |
|---|---|---|
| 5.0 mm | 0.0 mm（EngineAnchor_L） | 17.1 mm（EngineAnchor_L ↔ BodySocket_L） |
| 10.0 mm | 0.0 mm（EngineAnchor_L） | 22.1 mm（EngineAnchor_L ↔ BodySocket_L） |
| 15.0 mm | 0.0 mm（EngineAnchor_L） | 27.1 mm（EngineAnchor_L ↔ BodySocket_L） |
| 20.0 mm | 2.2 mm（EngineAnchor_L） | 32.1 mm（EngineAnchor_L ↔ BodySocket_L） |
| 30.0 mm | 10.9 mm（EngineAnchor_L） | 42.1 mm（EngineAnchor_L ↔ BodySocket_L） |
| 40.0 mm | 20.5 mm（EngineAnchor_L） | 50.0 mm（- ↔ -） |

- 连接键不拔、引擎直接外滑：滑 **20.0 mm** 后离开连接键。

## 搬运路线（引擎最低点高度 h，侧拔 → 升降 → 水平 → 落下）

- 试了 12 个落点（工作台现状、离垫上物件最远的前 12 个）× h = 0.92…1.60 m（步长 40 mm）。
- 最好的一条：h = **1.08 m**（引擎最低点），落点中心 (0.070, 1.059, -0.350)、绕竖直轴转 90°，全程最小间距 **27.7 mm**（Engine_NozzleRing_L ↔ Bench_Top）；落下后离垫上物件 36.2 mm。
- 路线不经过七号上方（侧拔后直接在七号左侧升降），水平段从维修座一侧移到操作垫。
