# 左引擎固定件 / 插头在游戏镜头里的可见比例

七号停在维修座上（落座、夹具闭合，上盖装着）。每格 = 可见采样点比例 / 包围盒在 1600×900 画面里的宽度（px）。“—” = 不在画面内。

| 目标 | Dock（维修座） | EngineL（左引擎） | EngineRear（左引擎背面） | EngineClose（左引擎近看（进气口 / 轴承位）） | Overview（总览） |
|---|---|---|---|---|---|
| `ConnectionKey_L`（随引擎） | 0% / 56 px | 0% / 87 px | 0% / 77 px | 0% / 215 px | 0% / 28 px |
| `ConnectionKey_Bolt_L`（随引擎） | 0% / 49 px | 0% / 77 px | 0% / 77 px | 0% / 193 px | 51% / 23 px |
| `EngineAnchor_L`（随引擎） | 0% / 66 px | 0% / 104 px | 0% / 93 px | 0% / 265 px | 57% / 33 px |
| `BodySocket_L`（机身） | 22% / 56 px | 0% / 94 px | 0% / 93 px | 0% / 264 px | 38% / 29 px |
| `ExternalCable_L_PlugEngine`（随引擎） | 0% / 35 px | 0% / 54 px | 0% / 44 px | 0% / 126 px | 50% / 17 px |
| `ExternalCable_L_PlugBody`（机身） | 56% / 45 px | 45% / 73 px | 0% / 61 px | 0% / 177 px | 21% / 23 px |
| `AuxCable_L_PlugEngine`（随引擎） | 0% / 30 px | 0% / 45 px | 37% / 44 px | 0% / 100 px | 0% / 15 px |
| `AuxCable_L_PlugBody`（机身） | 0% / 39 px | 0% / 60 px | 53% / 64 px | 0% / 138 px | 4% / 21 px |
| `Engine_CoverLatch_Outer_L`（随引擎） | 53% / 33 px | 52% / 49 px | 54% / 57 px | 47% / 184 px | 0% / 13 px |
| `Engine_CoverLatch_Rear_L`（随引擎） | 0% / 34 px | 0% / 50 px | 63% / 48 px | 0% / 153 px | 0% / 15 px |

限制：蒙皮线缆没有碰撞体、不挡射线；只看七号停在维修座上、上盖装着的状态。
