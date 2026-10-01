# 七号第二晚维修 · 左引擎故障美术包

**只做资源，不改维修流程**：

- 没有修改 RobotV4 原 FBX（`robot-final.fbx` MD5 `80b94f89…`，构建前后核对一致）。
- 没有修改现有场景、预制体、维修逻辑、其它分支。
- 新文件只在两处：本目录 `ArtSource/Unit07FaultKit/`（源文件、导出、记录、渲染）和 `Assets/BorderRepair/Art/Unit07FaultKit/`（给 Unity 用的副本：FBX、贴图、URP 材质）。
- **分支**：`art/unit07-fault-kit`，从 `integ/unit07-first-order`（`8de5a69`）开出，独立工作树 `BorderRepairStation_faultkit`。
- **原创**：Blender 5.2.2 脚本程序化建模、程序化绘制贴图；没有外部模型、贴图或字体。由 Claude 辅助编写。

## 1. 先核对的真实位置

依据 `Docs/Integration/Unit07FirstOrder/README.md`，并在 Blender 里只读导入 RobotV4 原 FBX 实测。脚本 `measure/measure_robotv4_left_engine.py`，结果 `measure/robotv4_left_engine.json`。

**姿态**：用 **`Idle_Hover` 第 0 帧**（= Unity 里七号的静态姿态，由 `RobotV4ModelPostprocessor` 采样）。不用骨骼 Rest 姿态：两者的引擎铰链角度略有不同，用 Rest 做出来的轴承在 Unity 里会偏 1.6 mm。

| 对象 | 真实路径（七号根下） | 实测 |
|---|---|---|
| 左上轴承 | `Robot_Rig/Root/Body/Engine_L_Hinge/Engine_BearingTop_L` | 外径 **52.0 mm**、内径 **18.0 mm**、宽 **10.0 mm**；576 三角，材质 `M_Internal`；转轴偏离竖直 **8.5°**（倾斜写在网格里，对象自身旋转为 0），原点不在中心（沿轴偏 5 mm）。首单文档里的“53 × 19 mm”是倾斜后的包围盒，不是实际尺寸。轴承套在转子毂上 |
| 进气口护栅 | `…/Engine_L_Hinge/Engine_IntakeGuard_L` | 与轴承同轴；护栅在轴承中心上方 54.9–66.9 mm；4 根辐条 + 中心毂 + 外圈；俯视时条占开口的 32.5% |
| 进气唇口 | `…/Engine_L_Hinge/Engine_IntakeLip_L` | 内径 r 34.0 mm，轴向 48.9–74.9 mm |
| 进气风道 | `…/Engine_L_Hinge/Engine_IntakeDuct_L` | r 35.5–39.5 mm，轴向 19.9–49.9 mm |
| 左上盖 | `…/Engine_L_Hinge/Engine_UpperCover_L` | 229 × 240 × 95 mm；中间是进气口开孔；内表面在轴承中心上方 53–84 mm、朝向引擎内部 |

## 2. 资源

所有资源都按七号身上的真实位置建模，与原件对位。

| 交付 | FBX（`Export/` 与 `Assets/…/Models/`） | 对象 | 尺寸 | 三角面 | 材质 / 贴图 |
|---|---|---|---|---|---|
| ① 左上轴承磨损件 | `UNIT07_FK_BearingWorn.fbx` | `UNIT07_FK_BearingTop_L_Worn` + 子对象 `…_Worn_Chips`（12 片金属屑） | 外径 52.0 / 内径 18.0 / 宽 10.0 mm | 2880 + 200 | `M_FK_BearingWorn`：BaseColor / MetallicSmoothness / Normal 各 1024²；`M_FK_MetalChips`（纯色） |
| ② 新轴承 | `UNIT07_FK_BearingNew.fbx` | `UNIT07_FK_BearingTop_L_New` | 同上（同一网格、同一 UV） | 2880 | `M_FK_BearingNew`：三张 1024² |
| ③ 进气口积尘 / 纤维堵塞（可移除） | `UNIT07_FK_IntakeClog.fbx` | 根 `UNIT07_FK_IntakeClog_L_DustMat`，子对象 `…_Fibers`、`…_GuardDust`、`…_RimDust` | 毡层直径 67 mm、厚 2–7 mm | 4608 + 3412 + 2876 + 768 = 11 664 | `M_FK_IntakeClog`：BaseColor / Normal 512² |
| ④ 上盖内侧旧保养标记 | `UNIT07_FK_CoverLabel.fbx` | `UNIT07_FK_CoverInnerLabel_L` | 46 × 30 mm 贴纸，贴合内表面 | 768 | `M_FK_CoverLabel`：BaseColor 640×416 |
| 合计 | — | — | — | **18 392** | 9 张贴图、5 个材质 |

详细数字见 `stats.json`（每个对象的顶点、三角面、尺寸、原点）、`checks.json`（对位与穿插检查）、`materials.json`（URP Lit 参数）。

### ① 磨损轴承：近看能认出，不靠红色高亮

- **划痕**：
  - 端面有 22 道同心刮痕弧段和 12 道径向划痕，亮金属痕带浅刻痕（法线贴图）；
  - 外圈有 40 道周向刮痕，是从座孔里拆装时刮出来的。
- **油污**：
  - 一侧扇区有深褐近黑的油膜，从防尘盖与外圈的缝里渗出，边缘略带琥珀色；
  - 油膜那里更光滑、金属感低；
  - 外圈侧面有 9 道往下流的油迹。
- **金属屑**：
  - 12 片 0.8–2.2 mm 的不规则薄片几何，嵌在油里，大多在缝边，3 片挂在外圈的油迹上；
  - 另有 70 个贴图里的亮屑点。
- **其它**：
  - 防尘盖与套圈缝里的黑垢；
  - 内圈低饱和的热变色（草黄 → 灰蓝，说明过热，不夸张）；
  - 少量点蚀；外圈下沿两处很小的锈点。
- **压印**：防尘盖上压印规格 `B18-52 2Z` 与 `07`。划痕避开压印字，近看能读。规格是虚构的，内径 18 mm 不是标准轴承尺寸。

### ② 新轴承

- 同一网格、同一 UV，转轴、尺寸与原件一致。
- 磨削纹细、压印清楚、表面干净缎面，没有油污和划痕。

### ③ 进气口堵塞：整件可移除

“清理后”= 把 `UNIT07_FK_IntakeClog` 整个隐藏或删掉，原护栅保持不动。

| 部件 | 内容 |
|---|---|
| 积尘毡层 | 护栅下方 0.6 mm 起往下 2–7 mm 起伏的灰褐毡层，有 7 处变薄下陷的“透气薄点”；从护栅缝里看得到 |
| 护栅积尘 | 贴着辐条 / 中心毂朝上表面（离表面 0.15–0.5 mm）的斑驳浅灰积尘，外圈和一侧积得多 |
| 纤维 | 11 根 0.14–0.24 mm 粗的纤维搭在护栅条上、在宽缝里稍下垂，外加中心毂上的一小团短纤维；灰白为主，少量褪色蓝 / 暗红 |
| 唇口内壁积尘 | 贴内壁（离壁 0.25 mm）的一圈薄层，上沿不齐 |

- **穿插**：与护栅、唇口、风道、上盖等 10 个邻近零件的三角面穿插检查结果全部为 0。
- **几何来源**：护栅积尘的几何取自原护栅朝上的面（只读取，细分后偏移），以保证贴合；原 FBX 没有改。

### ④ 上盖内侧保养标记

- **位置**：在上盖内表面自动找最平的一块，离进气口轴线 66 mm、朝机身一侧，避开唇口；内表面起伏 3.8 mm，贴纸逐点贴合，离表面 0.35 mm。
- **内容**：发旧的纸贴纸，印刷表头 `L-03 SERVICE` + 格线，三行手写记录。字迹逐字轻微抖动、深浅不一：

```
03.11  BRG CHECK   OK
07.02  INTAKE CLEAN
11.20  BRG NOISE - WATCH
```

  最后一行下面有一道手画的提示线（伏笔：上次就记下了轴承异响）。
- **老化**：边缘泛黄、一枚油指印。没有红字、没有发光。
- **朝向**：从引擎内部或把上盖翻过来看时，字是正的（已在 Blender 和 Unity 两边看过，不是镜像）。

**风格**：旧诊所长期使用的工业件——钢件偏暗、油污近黑褐、积尘灰褐、纸张泛黄；不用高饱和色，不发光。

## 3. 对比渲染（`Renders/`，Cycles）

| 状态 | 渲染图 |
|---|---|
| **装在引擎里（故障）** | `R01` 进气口堵塞（上盖装着）· `R01b` 进气口近看 · `R02` 拆上盖后看到原位的磨损轴承 · `R02b` 磨损轴承近看 |
| **拆下放工作台** | `R03` 工作台总览（磨损件、新件、取出的积尘团、翻过来的上盖总成）· `R04` 磨损件 / 新件近看 · `R05` 上盖内侧保养标记 |
| **清理后** | `R06` 进气口清理后 · `R06b` 近看 · `R07` 新轴承装在原位 · `R07b` 近看 |
| **左右对比** | `C01` 进气口：堵塞 / 清理后 · `C02` 原位轴承：磨损 / 新 · `C03` 轴承近看：磨损 / 新 · `C04` 进气口近看：堵塞 / 清理后 |

关于这些渲染：

- **七号本体**：只读导入作参考，贴图从 `Assets/RobotV4/Model/textures` 只读加载。
- **工作台**：是渲染时临时搭的操作垫，不是工作台资产。
- **灯光**：暖色检修灯 + 冷色顶光，只为看清资源，不代表游戏内灯光。

## 4. Unity 导入与核对

Unity 6000.0.84f1，URP；记录见 `unity_check/unity_check.txt`，截图见 `unity_check/U01–U05.png`。

**导入设置**（只对本包资源）：

- **贴图**：法线贴图设为 Normal Map，MetallicSmoothness 设为线性（sRGB 关、Alpha 来自贴图）。
- **材质**：建了 5 个 URP Lit 材质（`Materials/`），在 FBX 导入设置里把材质名映射过去，不生成嵌入材质。
- **网格**：网格不可 CPU 读取，切线由 Unity 计算（MikkTSpace）。

**数值核对**（实测）：在不保存的临时场景里，把原 FBX 和本包 FBX 都按导入时的变换放到原点，用网格顶点比较：

| 核对项 | 结果 |
|---|---|
| 磨损件 / 新件 vs 原 `Engine_BearingTop_L` | 质心差 **0.134 mm**，转轴夹角 **0.071°**，宽 10.00 mm |
| 积尘毡层 | 在护栅质心下方 9.05 mm，与护栅同轴（径向偏差 0.03 mm） |
| 贴纸 | 在上盖内侧 |
| 挂载回代 | 按第 5 节的本地位姿挂到父对象下，位置与原位相同（差 0.000 mm） |

**URP 截图（U01–U05）**：

- 资源在 URP 下能显示：油污、金属屑、划痕、纤维、积尘、贴纸文字都在。
- 截图是临时灯光、没有反射探针，裸金属显得很暗。**游戏内的实际观感未验证。**

## 5. 建议挂载的真实对象

父对象都在七号身上；场景里七号实例名为 `UNIT07_RobotV4_DockReady`，以下路径相对于它。把 FBX 实例作为子对象挂上，并设成下表的本地位姿；本地位姿在七号静态姿态下实测，挂上后跟随父对象移动，与姿态无关。

| 资源（FBX 实例） | 建议父对象 | localPosition (m) | localRotation（四元数 x, y, z, w） | 说明 |
|---|---|---|---|---|
| `UNIT07_FK_BearingWorn` | `Robot_Rig/Root/Body/Engine_L_Hinge/Engine_BearingTop_L` | (0.00073, −0.00003, 0.00495) | (0.70700, 0.05222, −0.05196, 0.70337) | 同时关掉 `Engine_BearingTop_L` 自己的 MeshRenderer（由磨损件替代显示）。挂在原轴承下面，首单流程搬动原轴承时磨损件跟着走 |
| `UNIT07_FK_BearingNew` | 同上（装回后的位置）；或首单场景里的 `FO_Placeholder_NewBearing (占位：无美术资产)` | 同上 | 同上 | 装回原位时与原轴承完全重合。放在工作台轴承盒上时的摆放（平放、朝向）**未验证**：占位圆柱自身的旋转与轴承坐标系不同，接入时需要另配 |
| `UNIT07_FK_IntakeClog` | `Robot_Rig/Root/Body/Engine_L_Hinge/Engine_IntakeGuard_L` | (−0.00088, 0.00003, −0.00593) | 同上 | 跟着上盖总成一起拆下。清理后整件隐藏 / 删除 |
| `UNIT07_FK_CoverLabel` | `Robot_Rig/Root/Body/Engine_L_Hinge/Engine_UpperCover_L` | (0.06842, −0.00006, 0.01115) | 同上 | 一直挂着；上盖拆下翻过来时可读 |

`localScale` 均为 1。

## 6. 未验证 / 待确认（如实列出）

- **游戏内观感**：真实场景灯光、反射探针、阴影下的效果没有看过。只有一组临时灯光的 URP 离屏截图。
- **接入**：没有把资源挂进任何场景，也没有改流程。以下都是接入时的事，本包没做：
  - 何时显示磨损件 / 新件；
  - 清理进气口的交互；
  - 关闭原轴承渲染器。
- **贴纸能不能被看到**：现在首单原型把上盖“正着”放到操作垫上，内侧朝下，贴纸看不到；要让玩家读到，需要设计上让上盖翻过来，或用镜头从下往上看。这是流程 / 镜头问题，本包没改。
- **新轴承放在轴承盒上时的姿态**：见第 5 节，未验证。
- **VR 下的可读性**：没有验证。贴纸字高约 2.0–2.5 mm、轴承压印字高约 1.5 mm，近看清楚，但 VR 头显里能不能读没有试过。
- **目标硬件性能**：没有测。堵塞件 11.7k 三角对一个小零件偏多，主要在护栅积尘层；如果性能紧张，可以只保留毡层 + 纤维（约 8k）。没有做 LOD。
- **规格字**：`B18-52 2Z` 是虚构的。
- **磨损分布**：磨损主要做在上端面（拆上盖后能看到的一面）和外圈，下端面较干净。
- **护栅积尘依赖原护栅几何**：RobotV4 的 FBX 改了以后，要重新运行脚本。

## 7. 文件

| 文件 | 内容 |
|---|---|
| `build_unit07_fault_kit.py` | 建模、贴图、材质、检查、统计、导出 |
| `render_unit07_fault_kit.py` | 对比渲染（由建模脚本调用） |
| `run_blender.bat` | 双击运行；参数 `norender` 只生成不渲染 |
| `Unit07FaultKit.blend` | 源文件。保存时去掉了只读参考的七号，脚本运行时会重新导入 |
| `Export/*.fbx`、`Textures/*.png` | 导出；`Assets/BorderRepair/Art/Unit07FaultKit/` 里是同样的文件（逐字节相同）加 Unity 的 `.meta` 和材质 |
| `stats.json`、`checks.json`、`materials.json` | 尺寸与面数、对位与穿插检查、材质参数 |
| `measure/` | 只读测量脚本与结果 |
| `Renders/` | Cycles 对比渲染 |
| `unity_check/` | Unity 导入设置与核对脚本、记录、URP 截图。脚本运行时临时复制进 `Assets/…/Editor`，跑完已删除，不在项目里 |

**重建**：

1. 运行 `run_blender.bat`。
2. 把 `Export/`、`Textures/` 复制到 `Assets/BorderRepair/Art/Unit07FaultKit/Models|Textures/`。
3. 把 `unity_check/Unit07FaultKitUnityCheck.cs` 临时放进 `Assets/…/Editor`，用 `-executeMethod Unit07FaultKitUnityCheck.Run` 运行，然后删掉。
