# 资产评价记录：通讯器（Communicator）

> 按 `AssetEvaluation_Template.md` 填写。未进行的项目写“未进行”。

## 1. 项目条件

| 字段 | 内容 |
| --- | --- |
| 记录编号 | AE-001 |
| 记录日期 | 2026-09-24 |
| 记录人 | 项目开发者（Claude 辅助） |
| Unity 版本 / 渲染管线 | 6000.0.84f1 / URP 17.0.4 |
| 目标用途 | 替换案例 `Case_01_Communicator` 使用的占位 prefab `Item_Communicator` |
| 被替换的占位资产路径 | `Assets/BorderRepair/Prefabs/Items/Item_Communicator.prefab`（保留不删，案例改为引用新 prefab） |
| 技术约束 | 必须提供 `antenna`、`battery`、`screen` 三个可扫描部位；`antenna` 需有“损坏 / 修好”两个可切换外观；运行时会被 `ItemInspector` 归一化到包围球半径 0.22，固定机位近距离观看 |
| 美术约束 | 原创硬表面造型；寒地边境维修设备气质（戴手套可操作的大按键和旋钮、防寒电池、醒目配色）；不模仿现有游戏物品 |

### 试验性预算（制作前填写）

| 项目 | 预算 | 说明 |
| --- | --- | --- |
| 三角面（机身 + 屏幕 + 电池仓） | ≤ 7,000 | 近景主角物品，但一次只显示一件 |
| 三角面（单个天线外观） | ≤ 1,500 | 损坏 / 修好两套各自计算，同一时间只显示一套 |
| 三角面（同屏最大值） | ≤ 8,500 | 机身 + 一套天线 |
| 材质数 | ≤ 6 | 外壳、橡胶、醒目色、金属、屏幕、铜线 |
| 贴图数量 | ≤ 3 | |
| 贴图尺寸 | 最大 1024×1024 | 通用磨损贴图 1024²；屏幕贴图 ≤ 512×256 |

### 实际数值（导出后填写）

数据来源：Blender `stats.json`（修改器求值后的三角面）以及 Unity 导入日志 `ArtSource/Communicator/unity_logs/1_import.log`。

| 项目 | Blender 导出 | Unity 导入后 | 是否在预算内 |
| --- | --- | --- | --- |
| 三角面（机身） | 5,240 | 5,226 | — |
| 三角面（屏幕） | 2 | 2 | — |
| 三角面（电池仓） | 1,536 | 1,536 | — |
| 三角面（机身 + 屏幕 + 电池仓） | 6,778 | 6,764 | 是（≤ 7,000，余量约 3%） |
| 三角面（损坏天线） | 520 | 520 | 是（≤ 1,500） |
| 三角面（修好天线） | 724 | 724 | 是（≤ 1,500） |
| 三角面（同屏最大） | 7,502 | 7,488 | 是（≤ 8,500） |
| 材质数 | 6 | 6（URP Lit） | 是（≤ 6，已用满） |
| 贴图数量 / 尺寸 | 2：T_Comm_Grime 1024×1024，T_Comm_Screen 512×256 | 同左 | 是 |

说明：机身三角面已接近预算，主要消耗在 12 个带倒角的按键、滚花旋钮和 9 块保温软垫上。如需再加细节，可先把按键倒角段数从 1 降到 0，或把旋钮齿数从 18 降到 12。

## 2. 资产 / 生成工具及来源

| 字段 | 内容 |
| --- | --- |
| 资产名称 | 通讯器（Communicator） |
| 资产类型 | 模型 + 材质 + 贴图 |
| 生成工具与版本 | Blender 5.2.2 LTS；生成方式：**Claude 辅助编写 Blender 建模脚本**，由脚本程序化建模并生成贴图。未使用任何图生 3D / 文生 3D 服务，也没有使用外部模型或贴图 |
| 提示词或输入（原文） | 用户需求原文见本次开发会话；建模脚本 `ArtSource/Communicator/build_communicator.py` 即完整输入 |
| 来源链接 / 获取方式 | 本项目原创，脚本生成 |
| 许可 / 使用条款 | 项目自有 |
| 原始文件存放路径 | `ArtSource/Communicator/Communicator.blend`、`ArtSource/Communicator/build_communicator.py`（运行：`run_blender.bat`；接入 Unity 与测试：`run_unity.bat`） |

## 3. 问题截图路径

| 序号 | 截图路径 | 问题说明 |
| --- | --- | --- |
| 1 | `Screenshots/20260924_Communicator_01_front.png`（第一版，已被覆盖） | 第一版正面、背面渲染图裁掉了天线顶部 |
| 2 | `Screenshots/20260924_Communicator_03_damage_closeup.png`（第一版，已被覆盖） | 第一版外露铜芯约 1.5 cm，比残段还长，四散伸出，不像“馈线外露” |
| 3 | 无截图（首次运行的测试日志，已被复测结果覆盖） | 旋转 180° 后，射线命中旋转前位置的机身碰撞体（BodyCollider_08），没有命中电池仓 |

最终截图：

| 内容 | 路径 |
| --- | --- |
| 正面（Blender Cycles） | `Screenshots/20260924_Communicator_01_front.png` |
| 背面（Blender Cycles） | `Screenshots/20260924_Communicator_02_back.png` |
| 损坏部位特写（Blender Cycles） | `Screenshots/20260924_Communicator_03_damage_closeup.png` |
| 修好天线特写（Blender Cycles） | `Screenshots/20260924_Communicator_04_repaired_closeup.png` |
| Unity 游戏内：接收阶段正面 | `Screenshots/20260924_Communicator_05_unity_intake_front.png` |
| Unity 游戏内：背面扫描电池仓（悬停高亮） | `Screenshots/20260924_Communicator_06_unity_back_scan_battery.png` |
| Unity 游戏内：损坏天线放大 | `Screenshots/20260924_Communicator_07_unity_damage_closeup.png` |
| Unity 游戏内：换件后结果 | `Screenshots/20260924_Communicator_08_unity_repaired_result.png` |

Unity 截图由 batchmode 下的 PlayMode 测试生成（Canvas 临时切换为相机模式渲染），不是交互式编辑器 Game 视图的截图。

## 4. 检查维度

| 维度 | 结论 | 说明 |
| --- | --- | --- |
| 版权与许可是否清晰 | 通过 | 全部几何体和贴图由项目内脚本生成，没有使用外部模型、贴图或图生 3D 服务 |
| 与其他作品是否过于相似 | 通过（主观判断） | 造型是通用的加固型手持电台；未对照任何特定游戏物品，也没有做逐项相似度比对 |
| 结构是否正确（比例、部件完整、无破面） | 通过 | 渲染图中未见破面或穿插。机身 7.4×3.6×15 cm；天线根部碎裂套筒、弯折残段、外露屏蔽层和铜芯清楚可辨 |
| 能否拆出维修所需的检查部位 | 通过 | 按功能拆成 5 个 FBX；screen / battery / antenna 各自独立，天线的损坏与修好两套外观可以切换 |
| 拓扑 / 面数是否在预算内 | 通过 | 见上表；机身接近上限 |
| UV / 贴图是否正确 | 通过 | 按面法线做立方体投影（每 6 cm 平铺一次），磨损贴图无明显拉伸；屏幕单独使用 0–1 UV，文字方向正确 |
| 在 URP 下材质表现是否正常 | 通过 | 6 个 URP Lit 材质由 `Communicator_Materials.json` 生成并重映射，屏幕自发光正常；导入日志中没有“未重映射材质”的错误 |
| 尺寸、pivot、朝向是否符合规范 | 通过 | 5 个 FBX 共用原点；包装 prefab 内把模型转 180° 后正面朝向镜头，已由测试 `FrontFacesCameraAndPointsAreRaycastable` 的朝向断言和截图确认 |
| 与场景风格是否一致 | 部分通过 | 比工作间的纯色几何体精细得多，风格差距明显；这是其余占位资产尚未替换造成的 |
| 在固定机位下的可读性 | 部分通过 | 屏幕文字、橙色防撞块、按键可读。但包围盒包含了修好的长天线，接收阶段整机只占画面高度约 45%；损坏天线在默认距离下较小，需要拉近才能看清铜芯 |
| 性能（Draw Call、贴图内存） | 未检查 | 共 36 个网格对象，未合批，也没有在 Profiler 中测量 Draw Call |

## 5. 判定

| 字段 | 内容 |
| --- | --- |
| 判定 | 返修（暂定，等待真实鼠标验收后定稿） |
| 理由 | 模型、材质、扫描点、换件切换均可用，面数在预算内；旋转后射线命中旧位置的问题已修复，并已通过 batchmode 自动测试复测（见第 7 节）。仍待处理的有两项：一是固定机位下的可读性（接收阶段整机约占画面高度 45%，损坏细节要拉近才看得清），是否调整 `targetItemRadius` 或取景由开发者验收后决定；二是真实鼠标交互尚未验证。 |

## 6. 返修记录

| 字段 | 内容 |
| --- | --- |
| 实际返修时间 | 2026-09-24，Claude 与开发者在同一会话中完成，没有单独计时 |
| 返修人 | 开发者（本机运行 Blender / Unity）+ Claude（修改脚本） |

返修步骤：

1. 建模脚本在给倒角权重打标记时引用了已失效的 BMesh 边，导致 Blender 报错中止。开发者改为直接从封盖面取边后跑通。
2. 正面、背面渲染图拉远机位（0.42 m → 0.62 m），天线完整入画；修好天线的特写重新取景。
3. 外露铜芯从约 1.5 cm 缩短到 5–8 mm，并卷曲在断口附近。
4. 色彩映射从 AgX 改为 Standard，让渲染图的颜色更接近 URP 游戏内的显示。
5. `ItemInspector` 计算包围盒时包含隐藏的修好天线，换件后新天线不再超出画面。
6. `ItemScanner` 在射线检测前调用 `Physics.SyncTransforms()`，修复旋转后命中旧碰撞体位置的问题；测试中的射线检测也做了同样处理。

## 7. 返修后结果

| 字段 | 内容 |
| --- | --- |
| 返修后截图路径 | 见第 3 节“最终截图” |
| 复检结论 | 自动测试复测通过（2026-09-24 13:06，由开发者在本机运行 `run_unity.bat`；修复代码 13:02 保存，早于这次运行）：导入退出码 0；EditMode 11/11 通过（`unity_logs/2_editmode.xml`）；PlayMode 共 5 项，4 项通过、0 项失败、1 项 `[Explicit]` 截图测试按设计跳过（`unity_logs/3_playmode.xml`），其中 `FrontFacesCameraAndPointsAreRaycastable` 覆盖旋转 180° 后的电池仓射线；截图测试 1/1 通过（`unity_logs/4_capture.xml`）。**真实鼠标操作：未验证。** 本资产尚未最终采用 |
| 最终资产路径 | `Assets/BorderRepair/Prefabs/Items/Item_Communicator_Final.prefab`（模型在 `Assets/BorderRepair/Art/Communicator/`） |
| 备注 | 还需要用真实鼠标，在交互式 Unity 编辑器里复测悬停高亮、各部位边缘的点击命中、旋转缩放不穿镜头、换件前后的观感 |
