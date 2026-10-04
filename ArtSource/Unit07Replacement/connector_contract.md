# 左引擎外露线缆 · 连接器契约（connector_contract）

结论：**采用“机身端拔下”**。几何证据支持：原 `*_PlugBody` 是“固定法兰 + 活动插头”合在一个物体里的两块网格；线缆机身端接在插头那块上。机身留下法兰（固定插座），插头随线走；引擎端保持连接。
因为原物体把插座和插头合成一件、RobotV4 不改，所以在新资源里补了“固定插座”（带孔法兰 + 内腔）和“自由插头”（原插头网格岛的复制），原物体在断开状态里整体隐藏。

证据：`Reports/connector_geometry.md` / `.json`（Blender 只读导入 robot-final.fbx 的网格岛分析）、`Reports/unity_verify.md`（Unity 隔离场景）。图：`Renders/K01*`、`K02*`、`K06`（Blender）与 `Renders/Unity/U_*`（Unity）。

## 1. 原对象是什么（实测，不靠名字）

| 原对象（Unity 路径） | 父对象 | 网格岛 | 判定 | 依据 |
|---|---|---|---|---|
| `…/Body/ExternalCable_L_PlugBody` | Body | ① 284 三角面：Ø31.4 × 3.2 mm 圆片（本地 X 1.6…4.9 mm）<br>② 188 三角面：Ø16.4 × 25.5 mm 圆筒（本地 X −11.3…+14.1 mm），穿过①的位置 | ① = **固定法兰（机身插座面板）**；② = **活动插头** | 线缆机身端那一圈中心离②2.5 mm、离①6.9 mm，落在插头外端（本地 +11.5 mm，法兰外侧）；②与引擎端插头三角面数、直径同一规格；①是薄圆片，正面只有一圈贴图，没有孔 |
| `…/Body/AuxCable_L_PlugBody` | Body | ① 284：Ø28.2 × 3.1 mm 圆片；② 188：Ø13.2 × 25.3 mm 圆筒 | 同上 | 线端离②2.1 mm（本地 +12.1 mm） |
| `…/Engine_L_Hinge/ExternalCable_L_PlugEngine` | Engine_L_Hinge | 一块：Ø16.4 × 24 mm 圆筒 | **引擎端插头（插在引擎上，保持连接）** | 线缆引擎端离它 2.5 mm |
| `…/Engine_L_Hinge/AuxCable_L_PlugEngine` | Engine_L_Hinge | 一块：Ø13.2 × 24 mm | 同上 | 线端离它 5.1 mm |
| `UNIT07_RobotV4_DockReady/ExternalCable_L`（黄，贴图取色 0.86 / 0.64 / 0.10） | 机器人根（蒙皮：Body ↔ Engine_L_Hinge） | 17 圈 × 12 顶点，404 三角面 | 跳线，两端各接一个插头 | 权重两端各为 1 |
| `UNIT07_RobotV4_DockReady/AuxCable_L`（灰，0.32 / 0.32 / 0.33） | 同上 | 404 三角面 | 同上 | |
| `…/Body/ExternalCable_Clip_L` | **Body**（没有蒙皮） | 384 三角面，套在黄线上 | **线上的卡箍**：连接状态里它刚好套在线上；线一动它就会悬空 | 渲染 K01 / K02 第一版里看到它留在原处，所以本契约把它当作随线走的件 |
| `Harness_Engine_L_PlugA/B`（M_Internal） | Body | — | 机身内部线束，与外露线无关 | 审计：整条留在机身，不用处理 |

## 2. 拔出方向与挂点

- **拔出方向**：机身端插头沿 `*_PlugBody` 的本地轴，从法兰拔出朝引擎一侧（Blender 里是本地 +X；**Unity 导入后是本地 −X**，导入时 X 取了反，见 `Reports/anchors.csv` 里固定插座面在 −4.9 mm）。世界方向 ≈ 七号左（与引擎外滑方向一致）。原插头插入深度约 16 mm（插头最深处到法兰外面）。
- **挂点**（`Reports/anchors.csv`，单位 mm，相对 Kit 根 = 相对对应原物体的本地坐标）：

| 挂点 | 所在 Kit | 运行时父对象 | 含义 |
|---|---|---|---|
| `Anchor_ExternalCable_L_MountEngineEnd` / `Anchor_AuxCable_L_MountEngineEnd` | `Kit_*_Unplugged` | Engine_L_Hinge（随旧总成） | 安装端：线缆在引擎端插头处的那一圈中心（与原线完全重合，偏差 0.00 mm） |
| `Anchor_ExternalCable_L_FreePlugTip` / `Anchor_AuxCable_L_FreePlugTip` | `Kit_*_Unplugged` | 同上 | 自由端：自由插头的插入端中心，Z 轴沿插头（重新插回时对准固定插座面） |
| `Anchor_ExternalCable_L_SocketFace` / `Anchor_AuxCable_L_SocketFace` | `Kit_*_BodySocketFixed` | Body | 固定插座面中心，朝向 = 原插头本地轴 |

- **Kit 根的放法**：`Kit_*_Unplugged` 放在 `Engine_L_Hinge` 下，localPosition / localRotation / localScale **与同名 `*_PlugEngine` 相同**；`Kit_*_BodySocketFixed` 放在 `Body` 下，**与同名 `*_PlugBody` 相同**。Unity 里用补件里带着的原插头完整复制核对过：顶点最大偏差 0.000 mm。

## 3. 各状态显示（详表 `state_visibility.csv`）

| 状态 | 原线（黄 / 灰） | 原 `*_PlugBody`（法兰 + 插头） | 原卡箍 `ExternalCable_Clip_L` | `Kit_*_BodySocketFixed`（带孔法兰 + 内腔） | `Kit_*_Unplugged`（断开线 + 自由插头 + 卡箍复制） | 原 `*_PlugEngine` |
|---|---|---|---|---|---|---|
| 1 连接（初始） | 显示 | 显示 | 显示 | 隐藏 | 隐藏 | 显示（随引擎） |
| 2 机身端拔下（引擎仍在，断电落座） | **隐藏** | **隐藏** | **隐藏** | **显示**（Body 下） | **显示**（Engine_L_Hinge 下） | 显示 |
| 3 旧总成取出（机身一侧） | 隐藏 | 隐藏 | 隐藏 | 显示（留在 Body） | 随旧总成走（与引擎件一起搬，相对位姿不变） | 随旧总成走 |
| 3b 旧总成在工作台暂存 | — | — | — | — | 显示，随旧总成 | 随旧总成 |
| 4 新引擎装回、未插线（可选） | 隐藏 | 隐藏 | 隐藏 | 显示 | 可用第二个 `Kit_*_Unplugged` 实例、材质换 `M_Engine_ReplacementL`；或直接跳到 5 | 新引擎上的（换新材质） |
| 5 重新连接 | **显示**（原线复用，可换 `M_Engine_ReplacementL`） | **显示** | **显示** | **隐藏** | 新引擎上的隐藏；旧总成上的那份仍留在工作台 | 新引擎上的 |

任何状态里：每根线只显示一根（原线或断开线二选一），每个机身端只显示一个（原插头或固定插座二选一），卡箍只显示一个。Unity 隔离场景按这张表数过（`Reports/unity_verify.md` 第 3 节）。

## 4. 程序接入要点（只是交接，不在本单实现）

- 不移动 `Engine_L_Hinge` 骨骼来拆引擎（审计 E1）；旧总成组 = Engine_L_Hinge 下的引擎件（不含 `ConnectionKey_L` / `ConnectionKey_Bolt_L`）**加上两个 `Kit_*_Unplugged`**，整组脱开搬运。
- 两根线可以一起拔（同一步切换两根），也可分开；断开线是刚性静态网格，不做软体。
- 原 `*_PlugBody` 在断开状态整体隐藏（因为它把插头也画进去了），固定插座由补件提供；**不要同时显示两者**。
- 镜头：拔线发生在落座、断电状态。游戏镜头「维修座」「左引擎」看得到黄线固定插座（约 60–70% 采样点可见）和自由插头（100%）；灰线一侧要用「左引擎背面」（固定插座 75%、自由插头 94%）。审计 E01b 机位只作寻位参考：在那个机位黄线一侧被外壳挡住（0%），不能当点选签收。点选可见性要程序在实际镜头里再验（本单不加碰撞体、不做点选）。
