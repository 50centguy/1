# 地下义体医生的维修工作台区域 · 资产与验收报告

分支 `art/workbench-area`（基于 `origin/main` 314ac87，未并入主分支）。
美术源文件只在 `ArtSource/WorkbenchArea/`，Unity 测试内容只在 `Assets/WorkbenchArea/`。
没有改动正式维修场景、工单代码或 RobotV4。

参考图：`station-zones-retro-lived-in-v3-2026-10-01/02-prosthetic-repair-closeup.png`（氛围）与 `03-diagnostics-nook.png`（空间 / 诊断角）。参考图只定氛围，没有照抄。Pinterest 参考只作灵感，未复刻任何具体场景（包括 SIGNALIS）。

---

## 1. 交付内容

| 类别 | 路径 | 说明 |
|---|---|---|
| 生成脚本 | `build_workbench_area.py` | 全部模型、贴图、材质、FBX、检查、统计、渲染都由此脚本生成，可重复运行 |
| 运行入口 | `run_blender.bat`、`run_unity.bat` | Blender 构建；Unity 导入 + 测试 + 采集 |
| Blender 源文件 | `WorkbenchArea.blend` | 可编辑；集合 `WB_Room / Bench / Mat / Placeholder / Trays / Toolbox / Diagnostic / Lamp / Storage / Records / Clutter` |
| FBX | `Export/WorkbenchArea.fbx` | 102 个对象，带自定义属性（`wb_role`、`grab_point`、`hinge_axis_local`、`slide_axis_local`、`rot_axis_local`、`note`） |
| 贴图 | `Textures/` | 5 张低分辨率点采样贴图：`T_WB_Grain` 128²、`T_WB_Floor` 128²、`T_WB_Paper` 512²（工单 / 便签 / 标签 / 记录本图集）、`T_WB_Screen` 128×96、`T_WB_Mat` 320×184 |
| 材质表 | `materials.json` | 15 个材质（Unity 构建器按此生成） |
| 资产清单 | `asset_inventory.csv` | 每个对象：组、角色、三角面、材质、原点位置与含义、属性 |
| 统计 / 检查 | `stats.json`、`checks.json` | Blender 端几何检查（15 项全部通过） |
| Blender 渲染 | `Renders/R01–R06` | 见第 5 节 |
| Unity 报告 | `Reports/Unity/` | `build_log.txt`、`playmode_measurements.txt`、`play_capture.txt`、`Screenshots/U00–U09` |
| Unity 内容 | `Assets/WorkbenchArea/` | `Art/`（FBX、贴图、材质）、`Prefabs/WorkbenchArea.prefab`、`Scenes/WorkbenchArea_Test.unity`、`Scripts/`、`Tests/` |

重新生成：先运行 `run_blender.bat`（约 3 分钟，含 Cycles 渲染），再运行 `run_unity.bat`。
也可以在 Unity 菜单中用 `Workbench Area > Build Test Scene` 构建，用 `Workbench Area > Play Capture` 采集。

## 2. 区域内容（全部为真实建模，不是渲染贴片）

坐标约定：Blender Z 向上，玩家在 −Y，工作台靠 +Y 墙。导入 Unity 后对应 (−x, z, −y)，玩家面向 −Z。

| 部分 | 对象 | 原点 / 可动信息 |
|---|---|---|
| 主工作台 | `Bench_Top`（台面 0.90 m）、`Bench_TopEdge`、`Bench_Frame`、`Bench_DrawerCase`、`Bench_Drawer_1_Probes / 2 / 3`、`Bench_Cabinet`、`Bench_CabinetDoor` | 抽屉原点在拉手，`slide_axis_local -Y`。1 号抽屉半开 22 cm，内有探针。柜门原点在铰链，`hinge_axis_local Z`，现虚掩 15° |
| 耐磨操作垫 | `Bench_Mat`、`Bench_ArmCradles` | 垫上印网格、刻度和 COVER 停放框（x 0.15–0.37，y 0.24–0.36） |
| 待修义肢占位件 | `Placeholder_Prosthetic_Forearm / BayMotor / Cover / Screw_1–4 / Hand / Connector / FaultLED / Tag` | 灰蓝色占位材质，明确不是 RobotV4。盖板原点在顶面中心（抓取点）。螺钉原点在钉头顶面。故障灯在接口朝玩家的一面 |
| 可取放托盘 | `Tray_Screws`（四格螺钉托盘）、`Tray_OldParts`（旧件托盘）、`Box_Bearings` | 原点在前沿拉手。托盘内容 `*_Contents` 在 Unity 中挂在托盘下，跟着托盘一起移动 |
| 分层工具箱 | `Toolbox_Base`、`Toolbox_Tier1 / Tier2`（悬臂展开）、`Toolbox_Arms`、`Toolbox_Lid`、`Toolbox_LidLabel`（手写 “DO NOT USE”） | 盖子原点在铰链 |
| 实体诊断仪 | `Diag_Body`、`Diag_Screen`（波形，自发光）、`Diag_Knob_Gain / Time / Offset`、`Diag_Switch_Power`、`Diag_LED_OK`、`Diag_LED_Fault`、`Diag_ProbeJacks`、`Diag_PrintoutStrip`、`Diag_Probe`、`Diag_ProbeCable`、`Diag_CablePatchTape` | 旋钮原点在面板轴心，`rot_axis_local Y`。探头握持点在 (−0.48, 0.33, 0.912) |
| 工作灯 | `Lamp_Base`、`Lamp_ArmLower`（肩关节）、`Lamp_ArmUpper`（肘关节）、`Lamp_Head`、`Lamp_Bulb` | 原点均在关节处。灯头指向操作垫中心 |
| 墙面收纳 | `Wall_Pegboard`、`Wall_ShelfUpper`、`Storage_PartsBins`、`Storage_BinLabel_1–6`、`Storage_Shelving` | 零件盒标签为手写 |
| 补过的线 | `Clutter_PatchedWallCable`、`Diag_CablePatchTape` | 胶带补线 |
| 纸质维修记录 | `Records_InboxStack`（积压工单）、`Records_RepairLog`（摊开的记录本）、`Records_Clipboard` + `Sheet`、`Records_WallNote_*`、`Records_PegNote_*` | 工单号、手写字、便签都画在纸张图集里 |
| 生活痕迹（合并的杂物） | 洞洞板工具、活页夹、纸箱、旧义肢外壳、旧 CRT、马克杯笔筒、保温壶、胶带焊锡、抹布、外套、收音机、地上零件箱，以及 `Furniture_Stool` | 只放在台边、墙面和收纳区，中央拆装区保持空出 |

红色只用于真实故障：`Placeholder_Prosthetic_FaultLED`、`Diag_LED_Fault`，以及工单 WO-1139 上的“故障”印章。EditMode 测试会检查用了红色材质的对象是否全部是 `fault_indicator`。

## 3. 统计

| 项目 | 数值 |
|---|---|
| 三角面（模型） | 7,140 |
| 网格 / 对象 | 102 |
| 材质 | 15（URP Simple Lit，无高光、无金属；自发光仅限屏幕、灯泡、灯管、指示灯） |
| 贴图 | 5（全部点采样，最大 512²） |
| Unity 静态碰撞 | 27 个 MeshCollider：墙地、台体、抽屉、柜门、货架、洞洞板、诊断仪机身、工具箱底座 |
| Unity 可检查对象 | 50 个。没有实体碰撞的对象各配一个占位触发盒（每边补 6 mm，最小边 22 mm）；有实体碰撞的直接用实体表面点选 |

Unity 渲染统计（编辑器 Play 模式，Game 视图 1471×886，D3D11，RTX 3070；各取 120 帧中位数）：

| 情况 | Draw calls | Batches | SetPass | 渲染三角面（含阴影） | 帧时间中位 / P95 |
|---|---|---|---|---|---|
| 游戏镜头 · 完整场景 | 199 | 155 | 22 | 16,348 | 2.04 / 3.29 ms |
| 游戏镜头 · 去掉杂物 | 170 | 147 | 22 | 12,552 | 2.47 / 4.20 ms |
| 近距维修镜头 · 完整场景 | 126 | 104 | 20 | 8,570 | 1.99 / 3.30 ms |

两种情况的帧时间差在编辑器噪声范围内。杂物大约带来 29 个 draw call。

## 4. 测量与验证

### 镜头

- **固定游戏镜头**：位于 (0, −0.80, 1.62)，看向 (0, 0.48, 0.92)，竖直视场 50°。
- **近距维修镜头**：位于 (0.05, −0.12, 1.36)，看向操作垫上的义肢，竖直视场 38°。
- 测试场景中按 Tab 切换。

### 台高与操作空间（Blender 三角面级检查，Unity 中再用 MeshCollider 复核一遍）

| 检查 | 结果 |
|---|---|
| 台面高度 | 0.90 m（垫面 0.904 m） |
| 中央拆装区 x −0.34…0.20、y 0.24…0.66、垫面以上 30 cm | 空（只允许占位件、垫子和托架） |
| 左手接近区 / 右手接近区（义肢两侧，从台前到义肢，垫上 −2…+16 cm） | 空 |
| 义肢上方工具空间（义肢后方 14 cm 深、上方 6–32 cm） | 空（工作灯灯头已抬高到台面以上 58 cm） |
| 站立与膝部空间（台前 45 cm 深、宽 0.94 m、0.01–1.60 m 高） | 空 |

以上手部空间是为将来 VR 双手操作预留的。

### 镜头遮挡与小件可读性（1920×1080 参考分辨率）

| 目标 | 游戏镜头 | 近距镜头 |
|---|---|---|
| 螺钉头（直径 7 mm） | 全部可见，**5.7 px** | 全部可见，**15.6 px** |
| 盖板 | 可见，192 px | 可见，542 px |
| 接口 | 87.5% 可见（其余被义肢本体挡住） | 87.5% 可见 |
| 故障灯 | 可见，9 px | 可见，25 px |
| 螺钉托盘 / 旧件托盘 | 100% / 88% 可见 | — |
| 探头、诊断仪屏幕与旋钮 | 全部可见 | — |
| 杂物或道具挡住工作目标 | 无 | 无 |

结论：在游戏镜头下，螺钉只有约 6 px，只适合辨认位置；拧螺钉、看故障灯这类操作应切到近距镜头。

### 点选（Unity PlayMode 测试，射线对准 9 个点）

- 近距镜头：全部 15 个关键目标都能点到（螺钉、盖板、接口、故障灯、两个托盘、探头、旋钮、拨杆、工具箱上层、记录本、灯头）。
- 游戏镜头：只有 `Toolbox_Tier1`（工具箱下层）点不到，因为悬臂展开后被上层 `Tier2` 盖住，近距镜头只能点到 2/9。这符合实物结构；如果以后要从游戏镜头直接取下层，需要把上层再展开一些。
- 点选规则：沿射线取最先命中的检查区之后 3 cm 内体积最小的那个。这样螺钉、旋钮、指示灯不会被盖板或机身的检查区吞掉。

### 托盘取放（占位）

两个托盘都先竖直抬起 8 cm，再水平拉到台沿外（螺钉托盘拉出 45 cm，旧件托盘 65 cm），然后原路放回。全程用所有网格的碰撞体逐帧检查：没有碰到任何东西，放回后误差小于 0.1 mm。

### 占位拆装路径

1. 4 颗螺钉依次：抬起 5 cm，平移到螺钉托盘上方，落进托盘。
2. 盖板：抬起 5 cm，平移到 COVER 停放框，平放在垫上。停放后包围盒 x −0.370…−0.150（Unity），正好贴住停放框两端：盖板长 22 cm，框宽也是 22 cm，余量为 0。
3. 全程没有碰到任何东西。装回后全部回到原位（误差小于 0.1 mm）。

### 自动化测试

- EditMode 8/8：导入数量、点采样、无高光、高光色为黑、`wb_role` 与关键属性、台高、红色只用于故障、碰撞与检查区、螺钉检查区互不重叠、中央区与手部空间、场景镜头与灯光、占位标注。
- PlayMode 5/5：两台镜头点选与小件尺寸、镜头切换、托盘取放、拆装路径与可逆、运行无控制台错误。

## 5. 图像

### Blender（Cycles）`Renders/`

- `R01_front`：正面
- `R02_oblique_top`：斜俯视
- `R03_player_closeup`：玩家近景（近距镜头）
- `R04_side_cutaway`：侧面剖切（隐去右墙和右侧货架，用来看台高、站位和手部空间）
- `R05_clutter_hidden_work_area`：去杂物看工作区
- `R06_game_camera`：游戏镜头

### Unity `Reports/Unity/Screenshots/`

- `U01–U06`：与 R01–R06 同机位
- `U00`：带 HUD 的 Game 视图
- `U07`：占位拆下后的近景
- `U08`：取出旧件托盘（游戏镜头）
- `U09`：取出旧件托盘（侧面）

## 6. 占位交互（不是正式维修流程）

测试场景中的交互全部是占位，只用于展示和路径验证。
场景对象名为 `PLACEHOLDER_Demo (占位交互，非正式维修流程)`，HUD 上也标着“占位”。

- 按键：Tab 切换镜头；1 / 2 取放两个托盘；D 拆下或装回义肢占位件。
- 鼠标悬停时，HUD 显示指向的对象名和角色。
- 没有实现正式维修流程，也没有接入工单系统。

## 7. 构建中发现并修正的问题

- **Simple Lit 材质高光**：用脚本新建的 Simple Lit 材质自带 `_SPECULAR_COLOR` 关键字和 0.5 灰的高光色。光滑度很低时，它会变成一整片偏冷的高光，把深色件洗得和墙一样亮：洞洞板工具“消失”，整个画面也发灰。
  - 修正：高光色清零，关键字关闭，并用测试锁住。
- **自发光被截成白色**：测试场景没有 Bloom 或 HDR 后处理，自发光强度大于 1 会被截成近白色，红色故障灯因此不再是红色。
  - 修正：Unity 端的自发光强度封顶为 1。
- **左墙便签镜像**：UV 方向反了，已修正。
- **侧视图全黑**：机位在墙外。已改为剖切侧视。

## 8. 尚未验证的项目

1. **VR 设备实测：未做。** 双手空间只做了几何留白检查（第 4 节）。没有在头显中验证以下内容：真人臂长与姿势、手柄或手部追踪、近距离文字清晰度、晕动、灯光亮度。
2. 鼠标交互只在编辑器里用代码射线验证过，没有做真人可用性测试（悬停反馈、点选手感）。
3. 性能只在编辑器 + RTX 3070 上测过，没有在目标硬件或独立构建（Player）中测试。
4. 工具箱下层在游戏镜头下点不到（结构如此，见第 4 节），尚未决定是否调整。
5. 盖板停放框余量为 0，以后换成真实义肢盖板需要重新核对。
6. 与正式维修场景、工单系统、RobotV4 的接入：按要求没有做。
7. 光照为实时灯光 + 平光环境光，没有烘焙光照，没有后处理。最终画面风格还需要美术确认。
