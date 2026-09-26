# 资产评价记录：导航信标（NavBeacon）

> 按 `AssetEvaluation_Template.md` 填写。未进行的项目写“未进行”。规格见 `Docs/AssetSpecs/NavBeacon_Spec.md`。

## 1. 项目条件

| 字段 | 内容 |
| --- | --- |
| 记录编号 | AE-002 |
| 记录日期 | 2026-09-24 |
| 记录人 | 项目开发者（Claude 辅助） |
| Unity 版本 / 渲染管线 | 6000.0.84f1 / URP 17.0.4 |
| 目标用途 | 替换案例 `Case_02_NavBeacon` 使用的占位 prefab |
| 被替换的占位资产路径 | `Assets/BorderRepair/Prefabs/Items/Item_NavBeacon.prefab`（保留未删；案例改为引用新 prefab，只改了 `itemPrefab` 一个字段） |
| 技术约束 | pointId 必须是 `seal`、`port`、`lamp`、`mast`；seal、port 为关键证据，表现异常；lamp、mast 必须表现正常；运行时会被归一化到包围球半径 0.22，检查台只能沿视线方向缩放 |
| 美术约束 | 原创；寒地边境导航设备；整体看起来“外观完好”，异常只在近距离检查时才看得出 |

### 试验性预算（制作前写入规格页）与实际数值

实际数值来源：Blender `ArtSource/NavBeacon/stats.json`（修改器求值后）以及 Unity 导入日志 `ArtSource/NavBeacon/unity_logs/1_import.log`。

| 项目 | 预算 | Blender 导出 | Unity 导入后 | 是否在预算内 |
| --- | --- | --- | --- | --- |
| 三角面：机身 | ≤ 5,000 | 1,964 | 1,954 | 是 |
| 三角面：seal | ≤ 800 | 500 | 500 | 是 |
| 三角面：port | ≤ 2,000 | 1,350 | 1,350 | 是 |
| 三角面：lamp | ≤ 2,000 | 1,608 | 1,608 | 是 |
| 三角面：mast | ≤ 800 | 720 | 720 | 是（余量 10%） |
| 三角面：合计（同屏） | ≤ 10,000 | 6,142 | 6,132 | 是 |
| 材质数 | ≤ 8 | 8 | 8（URP Lit） | 是（已用满） |
| 贴图 | ≤ 3 张，最大 1024² | 3 张：T_Beacon_Grime 1024×1024、T_Beacon_Seal 512×256、T_Beacon_PCB 512×512 | 同左 | 是 |

网格对象共 41 个（Body 10、Seal 7、Port 12、Lamp 7、Mast 5），没有合并。

## 2. 资产 / 生成工具及来源

| 字段 | 内容 |
| --- | --- |
| 资产名称 | 导航信标（NavBeacon） |
| 资产类型 | 模型 + 材质 + 贴图 |
| 生成工具与版本 | Blender 5.2.2 LTS；生成方式：**Claude 辅助编写 Blender 建模脚本**（程序化建模 + 用 numpy 程序化绘制贴图）。未使用任何图生 3D / 文生 3D 服务，也没有使用外部模型、贴图或字体文件（贴图上的文字是脚本内置的 5×7 点阵） |
| 提示词或输入（原文） | 规格页 `Docs/AssetSpecs/NavBeacon_Spec.md`；脚本 `ArtSource/NavBeacon/build_navbeacon.py` 与共用工具 `ArtSource/Common/br_hardsurface.py` 即完整输入 |
| 来源链接 / 获取方式 | 本项目原创，脚本生成 |
| 许可 / 使用条款 | 项目自有 |
| 原始文件存放路径 | `ArtSource/NavBeacon/NavBeacon.blend`、`build_navbeacon.py`；运行：`run_blender.bat`；接入 Unity 与测试：`run_unity.bat` |
| Unity 资产路径 | 模型、贴图、材质在 `Assets/BorderRepair/Art/NavBeacon/`；最终 prefab 为 `Assets/BorderRepair/Prefabs/Items/Item_NavBeacon_Final.prefab`；构建器为 `NavBeaconAssetBuilder` + `ItemAssetPipeline` |

## 3. 问题截图路径

| 序号 | 截图路径 | 问题说明 |
| --- | --- | --- |
| 1 | 无单独截图（第一版渲染已被覆盖） | 螺丝划痕条比螺丝头长，看起来像插着小棍；而且全金属材质在没有反射探针的场景里偏暗 |
| 2 | 无单独截图（同上） | 残胶颜色偏白，看不出是胶；电路板上的 “J3 DBG” 丝印被排针底座挡住 |
| 3 | `Screenshots/20260924_NavBeacon_07_unity_seal_zoom.png`（第一版，已被覆盖） | 第一版封签和维护舱位于机身下部。放大时检查台对准的是包围盒中心（因为灯和天线而偏高），证据被推到画面底部，被操作提示挡住 |
| 4 | `Screenshots/20260924_NavBeacon_06_unity_intake_front.png` | 接收阶段顶部仍显示上一件通讯器的反馈“诊断确认：天线模块断裂”。这是反馈提示在换件时没有清除，属于 UI 问题，不属于本资产 |

最终截图：

| 内容 | 路径 |
| --- | --- |
| 整体正面（Blender Cycles） | `Screenshots/20260924_NavBeacon_01_front.png` |
| 整体背面（Blender Cycles） | `Screenshots/20260924_NavBeacon_02_back.png` |
| 封签特写（Blender Cycles） | `Screenshots/20260924_NavBeacon_03_seal_closeup.png` |
| 调试接口特写（Blender Cycles） | `Screenshots/20260924_NavBeacon_04_port_closeup.png` |
| 灯组与外置天线（正常状态，Blender Cycles） | `Screenshots/20260924_NavBeacon_05_lamp_mast_normal.png` |
| Unity 游戏内：接收阶段正面 | `Screenshots/20260924_NavBeacon_06_unity_intake_front.png` |
| Unity 游戏内：封签（最大放大，俯仰 +20°） | `Screenshots/20260924_NavBeacon_07_unity_seal_zoom.png` |
| Unity 游戏内：调试接口（旋转 180°，最大放大，俯仰 +20°） | `Screenshots/20260924_NavBeacon_08_unity_port_zoom.png` |
| Unity 游戏内：“拒收并登记”结果 | `Screenshots/20260924_NavBeacon_09_unity_refused_result.png` |

Unity 截图由 batchmode 下的 PlayMode 测试 `NavBeaconCaptureTests` 生成：用代码调用旋转、缩放和流程接口，Canvas 临时切换为相机模式渲染。**这些截图不是真人试玩，也不是交互式编辑器 Game 视图的截图。**

## 4. 检查维度

| 维度 | 结论 | 说明 |
| --- | --- | --- |
| 版权与许可是否清晰 | 通过 | 几何体、贴图、文字全部由项目脚本生成 |
| 与其他作品是否过于相似 | 通过（主观判断） | 通用的八棱柱加灯笼式信标造型；没有对照特定游戏物品，也没有做相似度比对 |
| 剧情证据方向是否正确 | 通过 | seal：撕口处文字错位（INSP\|ECTION、SEA\|AL），右半张下移并歪斜，接缝露出 VOID 底纹，残胶外露，右侧两颗螺丝有亮色刮痕。port：两根红色飞线、亮银新焊点（对比暗灰的出厂焊点），无标识芯片歪粘在丝印标着 “NC” 的位置。lamp：灯罩完整、稳定发光，护笼没有变形。mast：笔直、接头完整。测试 `CaseCompletesWithRefuseAndRegister` 也断言了数据中 seal/port 为关键证据、lamp/mast 为非关键 |
| 结构是否正确（比例、部件完整、无破面） | 通过 | 5 张渲染图中未见破面或穿插；电路板比舱底高 0.2 mm，避免 Z-fighting |
| 能否拆出维修所需的检查部位 | 通过 | 按功能拆成 5 个 FBX；包装 prefab 只引用 FBX 根对象，InspectionPoint 和碰撞体挂在包装层自己的物体上 |
| 拓扑 / 面数是否在预算内 | 通过 | 见上表 |
| UV / 贴图是否正确 | 通过 | 通用部件用立方体投影；封签、残胶、VOID 底纹、电路板用平面 UV 映射到贴图集中的对应区域，从渲染图和游戏内截图看文字方向都正确 |
| 在 URP 下材质表现是否正常 | 通过 | 8 个 URP Lit 材质由 `NavBeacon_Materials.json` 生成并重映射，导入日志中没有“未重映射材质”的错误；灯罩自发光正常。灯罩在 Unity 里偏淡黄白，比 Blender 渲染更亮 |
| 尺寸、pivot、朝向是否符合规范 | 通过 | 测试 `FrontFacesCameraAndBackAfterRotation`：接收时封签比接口更靠近镜头，旋转 180° 后接口更靠近镜头 |
| 扫描点可点击性（自动测试） | 通过 | 测试 `AllFourPointsPickableFromScreen`：四个点按屏幕坐标经 `ItemScanner` 拾取都命中正确的 pointId。测试 `PickImmediatelyAfterRotation`：旋转后不等下一帧立即拾取，也命中正确 |
| 与场景风格是否一致 | 部分通过 | 比工作间的纯色几何体精细得多，风格差距明显；这是工作间尚未替换造成的 |
| 在固定机位下的可读性 | 部分通过 | 上移后，把物品放到最大并向上拖动约 20°，封签和接口都位于画面中部，证据能辨认。但放到最大时，封签在 1080p 画面里只有约 110 像素高，飞线和芯片约 20–30 像素。检查台的最小距离按整件物品计算，这次按要求没有改取景 |
| 性能（Draw Call、贴图内存） | 未检查 | 41 个网格对象、8 个材质，没有在 Profiler 中测量 |
| 真实鼠标交互 | **未验证** | 悬停高亮、点击命中、拖动和缩放的手感都没有人工测试 |

## 5. 判定

| 字段 | 内容 |
| --- | --- |
| 判定 | **暂定：采用（有条件）**，等真实鼠标验收后定稿 |
| 理由 | 证据：①面数、材质、贴图都在预算内；②剧情证据方向正确，Blender 特写和游戏内放大截图都能辨认；③自动测试覆盖正反面朝向、四点屏幕拾取、旋转后立即拾取、“拒收并登记”流程，最终运行全部通过；另外做了一次变异检查（临时去掉扫描器的物理同步），证明“旋转后立即拾取”这个测试确实能发现回归。成立条件：真实鼠标验收时，玩家能在不借助提示的情况下放大、转动，看清封签和接口；四个部位的边缘点击命中符合预期。如果验收发现证据太小、看不清，应转为“返修”，改为调整检查台的最小距离，或放大封签和维护舱（后者会影响面数和比例） |

## 6. 返修记录

| 字段 | 内容 |
| --- | --- |
| 实际返修时间 | 同一会话内完成，没有单独计时 |
| 返修人 | Claude（修改脚本并在本机运行 Blender / Unity batchmode） |

返修步骤：

1. 螺丝划痕缩短到螺丝头直径以内并贴平；“新焊锡 / 刮痕”材质金属度从 1.0 降到 0.6，避免在没有反射探针的场景里发黑。
2. 残胶贴图改为灰黄色，加上灰尘点和纸纤维；“J3 DBG” 丝印移到排针底座上方。
3. 封签面板和背面维护舱整体上移到接近包围盒中心的高度（封签 z：-0.020 → +0.018；维护舱 z：-0.040 → +0.012），中间加强环下移到 z = -0.050。原因见第 3 节第 3 项。
4. 测试代码修正：`NavBeaconFinalTests` 首次运行有 2 项失败，原因是测试用 `Collider.bounds` 取位置，旋转后要等物理同步才会更新，测试读到了旧位置。改为用 `Renderer.bounds`（随 Transform 立即更新），这样检验的是扫描器自己的同步逻辑。修正后，另做了一次变异检查：临时去掉 `ItemScanner` 里的 `Physics.SyncTransforms()`，`PickImmediatelyAfterRotation` 如预期失败；随后恢复代码（`unity_logs/mutation_check.xml`）。

## 7. 返修后结果（batchmode 自动验证，本机 2026-09-24）

| 阶段 | 退出码 | 结果 | 证据 |
| --- | --- | --- | --- |
| Blender 建模 + 导出 + 渲染 | 0 | 5 个 FBX、3 张贴图、材质清单、5 张渲染图 | `ArtSource/NavBeacon/blender_run.log`、`stats.json` |
| Unity 导入 + 生成 prefab | 0 | 无编译错误或警告，无未重映射材质 | `unity_logs/1_import.log` |
| EditMode 测试 | 0 | 11/11 通过 | `unity_logs/2_editmode.xml` |
| PlayMode 测试 | 0 | 共 10 项：8 项通过，0 项失败，2 项 `[Explicit]` 截图测试按设计跳过 | `unity_logs/3_playmode.xml` |
| 游戏内截图 | 0 | 1/1 通过 | `unity_logs/4_capture.xml` |

| 字段 | 内容 |
| --- | --- |
| 返修后截图路径 | 见第 3 节“最终截图” |
| 复检结论 | 自动检查全部通过；**真实鼠标操作：未验证**；暂定采用（有条件），见第 5 节 |
| 最终资产路径 | `Assets/BorderRepair/Prefabs/Items/Item_NavBeacon_Final.prefab` |
| 备注 | 通讯器资产、原占位 prefab 均未修改；`ItemInspector` 的 `targetItemRadius` 与取景参数未修改 |
