# ShaderLab：磨损表面与诊断高亮原型

独立测试入口，不改默认场景与叙事场景的风格，不修改三个正式 prefab。

## 打开

菜单 **Border Repair > ShaderLab > Open ShaderLab Scene**（未生成时先执行 **Build ShaderLab (create missing)**），然后按 Play。

- 鼠标悬停部位 → 悬停高亮；左键单击 → 在普通、已扫描、异常之间循环
- 右键拖动 → 环绕；滚轮 → 推拉
- **H** → 在“诊断叠加层”和“原来的底色染色”之间切换，便于对比；鼠标下的部位保持它正在显示的状态（悬停或刚点出的状态）

## 1. 磨损表面 `BorderRepair/WornSurface`

- 手写 URP 前向光照 shader，光照走 `UniversalFragmentPBR`：主光阴影（级联 / 软阴影）、附加光、Forward+、SSAO、雾。
- ShadowCaster / DepthOnly / DepthNormals 复用 URP 自带 pass，因此能投射和接收阴影。
- 所有 pass 共用一个 `UnityPerMaterial` 常量缓冲，兼容 SRP Batcher。
- 参数：底色（贴图 × 颜色）、金属度、粗糙度；磨损遮罩 `T_WearMask`（R = 磨穿露出底层，G = 污渍，可平铺）；磨损程度、边缘柔和度；磨穿处的颜色、金属度、粗糙度；污渍颜色与强度；可选的自发光（关键字 `_EMISSION`）。
- 三个材质实例（`Art/ShaderLab/Materials`）：`M_Worn_PaintedMetal`（烤漆金属）、`M_Worn_OldCeramic`（旧白色陶瓷，带一条细的自发光指示条）、`M_Worn_BareMetal`（裸露金属，划痕更亮更光滑）。

## 2. 诊断高亮 `BorderRepair/DiagnosticOverlay`

- 高亮时由 `InspectionPoint` 给本部位的渲染器**追加一个叠加材质槽**；参数（颜色 / 提亮 / 边缘光 / 扫描线 / 脉冲 / 斜纹）写在**该槽位的 per-material-index MaterialPropertyBlock** 上。底层材质资产、渲染器级属性块都不改；恢复普通时移除这个槽。
- 滤色混合（只提亮不覆盖），`ZTest LEqual`、不写深度、`Offset -1,-1`：只画在部位自己可见的表面上，被挡住的部分不会透出；没有阴影和深度 pass。
- 状态：悬停（青色 + 移动扫描线）、已扫描（绿色轻提亮）、异常（橙色均匀提亮 + 轮廓边缘光 + 脉冲），在 `DiagnosticHighlightProfile.asset` 中配置。异常以可读性为先：屏幕空间斜纹会切断封条文字和主板丝印，默认关闭（`hatch` = 0，仍可调）；边缘光指数调高到 3.5，斜看主板时不会整块被染橙。菜单 **Reset Highlight Profile To Defaults** 可以恢复为代码默认值。
- 启用方式：场景里放一个启用的 `DiagnosticHighlightSettings`。默认场景和叙事场景没有它，因此仍然使用原来的底色染色；“异常”在旧路径下按“已扫描”显示。
- 接入扫描流程：`ItemScanner.MarkAnomaly`；控制器在扫描到关键异常部位或会解锁线索的部位时调用。
- 旧路径的修正：只改写 `_BaseColor`；染色前对整个属性块做快照，恢复时以快照为底，再合入染色期间其他代码改过的 shader 属性。原本没有 `_BaseColor` 就恢复为没有，原本没有属性块就恢复为没有，不再清掉其他属性（`MaterialPropertyBlock` 不能删除单个属性，所以采用这种重建方式；染色期间新写入的非 shader 属性不会保留）；嵌套在子检查点下的渲染器交给子检查点自己管理（修复了工人义手中盖板覆盖封条状态的问题）。

### 已知限制

- 叠加材质槽只对单子网格的渲染器完整有效（多子网格时额外材质只会画在最后一个子网格上）。项目里现有物品都是单子网格。
- 被高亮的渲染器因为带有属性块，会暂时退出 SRP Batcher 合批；每个高亮渲染器多一次绘制。
- 边缘光按视角计算：悬停和已扫描的 `rimPower` 仍是 2.5，在很斜的平面上会铺满整个面；异常已调到 3.5。
- 异常带脉冲，截图时统一等到脉冲峰值；真实观感（闪烁是否刺眼）只能由人实际看。
- 场景没有反射探针：金属度为 1 的磨穿斑块偏暗，可以在材质上调低 `_WearMetallic`。
- 没有做全屏像素化、顶点抖动，也没有做任何 VR 验证。

## 测试与截图

- 双击 `Docs/ShaderLab/run_shaderlab.bat`：生成（只补缺失）→ EditMode → PlayMode → 截图、可读性和性能粗测。任一步失败即停止，并以非零退出码结束（Unity 本身的退出码，或者：5 = XML 显示有失败 / 没有通过项，6 = 没写出 XML，7 = 没写出数值文件，8 = 旧 XML 删不掉，9 = 找不到 Unity）。每一步开始前先删掉旧的 XML 和数值文件，不会把上一次的结果当成这次的。脚本调用时加参数 `nopause` 可以跳过最后的暂停。
- 可读性：`Docs/ShaderLab/readability_batchmode.txt`，记录每个状态与“普通”截图在部位区域内的细节相关系数、对比度保留和平均色差；另存上一版带斜纹的异常截图（`*_5_anomaly_previous_hatch.png`）作对照。
- 截图：`Docs/ShaderLab/Screenshots/20260924_ShaderLab_*.png`，由 batchmode 测试代码驱动生成（Canvas 已隐藏），**不是真人试玩截图**。
- 性能：`Docs/ShaderLab/perf_batchmode.txt`（离屏渲染粗测，波动较大，见报告）。
