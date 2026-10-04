# UNIT 07 左引擎替换补件：两根“机身端拔下”线缆 + 替换引擎材质

- **分支 / 工作树**：`art/unit07-replacement-kit`，`C:\Users\Administrator\UnityProjects\BorderRepairStation_replkit`，从 `integ/two-night-slice` 的 `72838e9` 开出（开工时核对：集成线仍为 `72838e9`，没有已有的同等补件分支）。未合并、未推送 main。
- **范围**：只做黄 `ExternalCable_L`、灰 `AuxCable_L` 的断开状态（含必要的小型连接器补件）和替换左引擎的独立材质 / 贴图。托盘（`art/unit07-tray-variant / 658f8f7`）、端盘演出、两种维修路线的程序逻辑都不在本单。
- **验证范围**：Blender 实际生成 / 渲染；Unity 编辑器隔离场景（布局 B 副本）里导入、坐标标定、显示切换计数、间距、镜头遮挡、渲染。**没有**在运行时由程序切换状态、没有真人操作、没有 VR / 目标硬件验证。

## 1. 交付物

| 类别 | 路径 |
|---|---|
| 连接器契约 | `connector_contract.md`（含证据表、拔出方向、挂点、各状态显示） |
| 状态显示表 | `state_visibility.csv` |
| 生成脚本 | `build_replacement_kit.py`（`run_blender.bat`）；连接器核对 `analyze_connectors.py`；渲染 `render_kit.py` |
| Blender 源文件 | `Unit07ReplacementKit.blend`（补件 + 只读导入的 RobotV4 参考，参考集合不导出） |
| FBX | `Export/UNIT07_ReplacementKit_Cables.fbx` = `Assets/BorderRepair/Art/Unit07Replacement/Models/UNIT07_ReplacementKit_Cables.fbx` |
| 贴图 | `Textures/T_Engine_BaseColor_ReplacementL.png`、`T_Internal_BaseColor_ReplacementL.png`（2048²，Unity 里在 `.../Unit07Replacement/Textures/`，导入设置照原贴图） |
| URP 材质 | `Assets/BorderRepair/Art/Unit07Replacement/Materials/M_Engine_ReplacementL.mat`、`M_Internal_ReplacementL.mat`（原材质的复制，只换 BaseMap；其余贴图、参数沿用） |
| 预制体 | `.../Prefabs/Kit_{ExternalCable_L,AuxCable_L}_{Unplugged,BodySocketFixed}.prefab` |
| 隔离验证场景 | `.../Verification/ReplacementKit_Verify.unity`（布局 B 副本；补件实例已按位姿放好、默认停用；`_Calibration` 标定件也停用） |
| 构建 / 检查脚本 | `.../Editor/ReplacementKitBuild.cs`（菜单 `Border Repair/Unit07 Replacement Kit/Build All`） |
| 数据 / 报告 | `Reports/connector_geometry.{md,json}`、`kit_stats.json`、`anchors.csv`、`material_map.csv`、`unity_verify.md` |
| 图 | Blender：`Renders/K01–K06`（契约标注图、各状态）、`M01a/b`（旧 / 新同灯光）；Unity：`Renders/Unity/U_{1..4}_*`（四个状态 × 近景前 / 后、游戏镜头左引擎 / 左引擎背面）、`M_old/new_engine_scene_light.png` |

## 2. 怎么做的

- **断开线缆**：复制原蒙皮线（静止姿态）的网格，按 17 个顶点圈逐圈“弯”到一条**等弧长**新曲线上（黄 115.2 mm、灰 80.3 mm，与原线相同，不拉长；顶点、UV、三角面数都不变）。曲线从引擎端那一圈出发（与原线完全重合），先直行一小段再匀速弯；弯曲总角度和弯曲平面在 40–180° × ±60° 里搜索，取离七号所有网格最远、且满足两条约束的：自由插头离开法兰 ≥ 15 mm；全线高于引擎件最低点 + 15 mm（旧总成直立放在操作垫上时不压进垫子）。结果：黄 120°、灰 40°。
- **自由插头**：原 `*_PlugBody` 里的插头网格岛（188 三角面）按原来“线端 ↔ 插头”的相对位姿跟到新线端。
- **卡箍**：`ExternalCable_Clip_L` 原挂在 Body、不随线变形；复制一份，按它所在那一圈（第 12 圈，弧长 77 mm）的标架跟着弯。
- **固定插座补件**：原法兰是实心圆片，拔掉插头后会像空白按钮；按原外径 / 厚度 / 颜色重做成带孔法兰（孔径 = 原插头外径），孔后加插座内腔（座底 + 3 根针脚）。内腔完全落在原插头圆筒占过的空间里，法兰在原法兰的外包范围内，所以不会碰到原插头没碰到的东西。颜色用原插头 / 法兰的贴图像素（同一材质 M_Engine，不加新材质）。
- **新件贴图**：在原 BaseColor 上只提亮“比局部均值暗、对比度低”的像素（细划痕、轻污渍；文字、孔、面板线等高对比细节保留），象牙白外壳再向局部均值收 30%、饱和度降 10%、提亮 2.5%（新漆少一点泛黄）。UV、网格、“L-03” 安装位标记（左右分别是 L-03 / R-03，判断为位置 / 型号码而非单件序号，保留；若策划认为是单件编号，可在脚本里去掉）都不变。故障包（积尘、纤维、磨损轴承、碎屑）和旧保养贴纸是单独物体，不在贴图里，新件按映射不显示。

## 3. 统计

| 件 | 三角面 | 材质 | 说明 |
|---|---|---|---|
| `ExternalCable_L_Unplugged` | 404 | M_Engine（原，引用） | = 原线 404 |
| `ExternalCable_L_FreePlug` | 188 | M_Engine | 复用原插头网格岛 |
| `ExternalCable_L_Clip_Unplugged` | 384 | M_Engine | 复用原卡箍 |
| `AuxCable_L_Unplugged` / `AuxCable_L_FreePlug` | 404 / 188 | M_Engine | |
| `*_SocketFlange`（每根线一个） | 384 | M_Engine | 新做（带孔） |
| `*_SocketInsert`（每根线一个） | 252 | M_Engine | 新做（内腔 + 针脚） |
| 合计新增显示网格 | 断开状态 2,840（其中新几何 1,272，其余是原网格复制）；同时隐藏原线 2×404、原机身端插头 2×472、原卡箍 384 | | |
| 材质 | +2（`M_Engine_ReplacementL`、`M_Internal_ReplacementL`）；线缆补件不加材质 | | 原材质未改 |
| 贴图 | +2 张 2048² PNG（只换 BaseColor；金属度 / 粗糙度 / AO 沿用原贴图） | | BaseColor 约 60% / Internal 约 41% 像素有变化，最大提亮 0.081（线性 0–1） |
| Renderer | 断开状态：黄 3 + 2、灰 2 + 2 | | 未实测帧时间 |

## 4. 检查结果（Unity 隔离场景，`Reports/unity_verify.md`）

- **坐标标定**：补件里带着的原插头完整复制，放到原物体本地位姿后与原物体顶点最大偏差 **0.000 mm**（4 件）。
- **引擎端连续**：断开线缆在引擎端与原蒙皮线（烘焙）偏差 **0.00 mm**（编辑默认、落座、悬停 4 个时刻都一样）。
- **断开状态间距**（MeshClearance 修正版）：断开线 + 自由插头 + 卡箍 ↔ 七号其余部分最小 **9.1 mm**（灰线 ↔ `Engine_LowerCover_L`）；两根断开线之间 ≥ 50 mm；线身 ↔ 自己的引擎端插头不计（连接处本来就接着）。落座和悬停摆动全程一致。
- **固定插座补件 ↔ 机身外壳**：0.0 mm，与原插头的同项基线（0.0 mm）相同——法兰本来就贴在外壳上；补件在原件外包范围内（见第 2 节），这一项用测距分不出“贴合”和“穿插”，以几何构造为准。
- **显示计数**（落座）：连接 / 拔下 / 取出（机身侧）/ 重新连接四个状态里，每根线 1 根、每个机身端 1 个、卡箍 1 个；自由插头只在拔下状态出现（2 个）。
- **旧总成在工作台**（按审计 engine_path 的摆放：绕竖直轴 90°、平移）：断开线缆 ↔ 工作台 **19.0 mm**。注意：审计起点是编辑默认姿态，落座姿态引擎件中心差 11 mm，这样摆时引擎件 ↔ 操作垫只剩 1.1 mm——摆放以程序实际路线为准，线缆始终比引擎最低点高 ≥ 15 mm。
- **镜头可见**（落座、拔下；采样顶点能否直接看到，临时网格碰撞体做遮挡）：「维修座」黄线固定插座 69%、自由插头 100%；「左引擎」61% / 100%；「左引擎背面」灰线固定插座 75%、自由插头 94%；「总览」自由插头 75–90%；审计 E01b 机位黄线一侧 0%（被外壳挡住，只是寻位参考）。
- **材质映射**：`Reports/material_map.csv`（Engine_L_Hinge 下全部 Renderer + 两根线）：M_Engine → M_Engine_ReplacementL，M_Internal → M_Internal_ReplacementL；连接键 / 螺栓不变（留机身）；故障包积尘 4 件、磨损轴承 2 件、旧保养贴纸 → 新件不显示；正常轴承 `Engine_BearingTop_L` 新件显示。右引擎、机身件不在映射里，材质不变。

## 5. 检测方法与局限

- Blender 搜索弯曲时用的是“顶点到表面”最近距离（BVH），只用于挑形状；签收数字来自 Unity 的 MeshClearance：双向点到三角形 + 双面 Möller–Trumbore 边穿三角形（集成线修正版，不是审计 AuditCommon 的旧函数）。
- 局限：不算边到边最近距离（结果可能偏大，最多约一个三角形边长）；判不出共面重叠；贴合件（法兰 ↔ 外壳）测出来都是 0。2–5 mm 级数字是估计。
- 遮挡用临时 MeshCollider 做射线（按网格，不是像素），视锥按 ±35° 粗算；是“能不能看到”的参考，不是点选签收。
- 状态切换是检查脚本里临时做的（不保存）；程序实际的拆装、搬运、重新挂回骨骼没有在运行时验证。
- 旧审计的 XML / 数字没有拿来当本次结果。

## 6. 导入说明（程序）

1. 断开线缆：实例化 `Kit_*_Unplugged.prefab`，父对象 `Engine_L_Hinge`，localPosition / localRotation / localScale 复制同名 `*_PlugEngine`。固定插座：`Kit_*_BodySocketFixed.prefab`，父对象 `Body`，位姿复制同名 `*_PlugBody`。
2. 按 `state_visibility.csv` 切换显示；旧总成搬运时把两个 `Kit_*_Unplugged` 与引擎件一起脱开（不移动 `Engine_L_Hinge` 骨骼）。
3. 新引擎：按 `material_map.csv` 把 M_Engine / M_Internal 换成 `*_ReplacementL`（逐 Renderer 换 sharedMaterials，**不要改原材质资产**）；故障包件、旧贴纸不显示，正常轴承显示。
4. 导入设置：FBX 比例 1、不烘焙轴转换、不导入动画；材质映射到原 `Assets/RobotV4/Materials/M_Engine.mat`（引用）。Unity 导入后补件本地 X 与 Blender 相反（与原 RobotV4 一致，标定为 0 偏差）。

## 7. 写入清单与未改动核对

- 写入只有 `ArtSource/Unit07Replacement/**`、`Assets/BorderRepair/Art/Unit07Replacement/**` 与 `Assets/BorderRepair/Art/Unit07Replacement.meta`。
- 与 `72838e9` 比较：RobotV4（含 FBX MD5 `80b94f89…`、材质、贴图）、维修座、工作台、故障包、共享材质、发布场景、项目设置无差异（提交前再核对一次）。

## 8. 未验证 / 待办

- 程序运行时的显示切换、拆装搬运、重新挂回：未做（程序窗口负责）。
- 真人观感（线的下垂形状、新件“稍干净”的程度）、点选：未验证。
- 新件贴图只改了 BaseColor；如果需要更“新”，可以再给 MetallicSmoothness 做副本（本单没做）。
- “L-03” 是否为单件编号：按位置 / 型号码处理，待策划确认。
- 帧时间、VR：未验证。
