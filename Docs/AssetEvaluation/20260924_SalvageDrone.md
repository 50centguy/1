# 资产评价记录：回收无人机（SalvageDrone）

> 按 `AssetEvaluation_Template.md` 填写。未进行的项目写“未进行”。规格见 `Docs/AssetSpecs/SalvageDrone_Spec.md`。

## 1. 项目条件

| 字段 | 内容 |
| --- | --- |
| 记录编号 | AE-003 |
| 记录日期 | 2026-09-24 |
| 记录人 | 项目开发者（Claude 辅助） |
| Unity 版本 / 渲染管线 | 6000.0.84f1 / URP 17.0.4 |
| 目标用途 | 替换案例 `Case_03_SalvageDrone` 使用的占位 prefab |
| 被替换的占位资产路径 | `Assets/BorderRepair/Prefabs/Items/Item_SalvageDrone.prefab`（保留未删；案例只改了 `itemPrefab` 一个字段，文本与生成器初始内容一致，没有手工修改需要保留） |
| 技术约束 | pointId 必须是 `motor_fl`、`mainboard`、`rotor`、`camera`；motor_fl、mainboard 为关键证据，表现异常；rotor、camera 必须表现正常；损坏应当“严重但可修”，以支撑“费用超限、建议更换”的判断 |
| 美术约束 | 原创；河滩回收的巡检无人机；不参照商业产品 |

### 试验性预算（制作前写入规格页）与实际数值

来源：Blender `ArtSource/SalvageDrone/stats.json` 以及 Unity 导入日志 `ArtSource/SalvageDrone/unity_logs/1_import.log`。两边数值一致。

| 项目 | 预算 | Blender 导出 | Unity 导入后 | 是否在预算内 |
| --- | --- | --- | --- | --- |
| 三角面：机身 | ≤ 7,000 | 3,752 | 3,752 | 是 |
| 三角面：motor_fl | ≤ 1,200 | 768 | 768 | 是 |
| 三角面：mainboard | ≤ 1,500 | 1,018 | 1,018 | 是 |
| 三角面：rotor | ≤ 400 | 204 | 204 | 是 |
| 三角面：camera | ≤ 1,000 | 684 | 684 | 是 |
| 三角面：合计（同屏） | ≤ 11,000 | 6,426 | 6,426 | 是 |
| 材质数 | ≤ 8 | 8 | 8（URP Lit） | 是（已用满） |
| 贴图 | ≤ 3 张，最大 1024² | 3 张：T_Drone_Grime 1024×1024、T_Drone_Board 512×512、T_Drone_Damage 512×256 | 同左 | 是 |

网格对象共 71 个（Body 44、MotorFL 10、Mainboard 9、Rotor 2、Camera 6），没有合并。

## 2. 资产 / 生成工具及来源

| 字段 | 内容 |
| --- | --- |
| 资产名称 | 回收无人机（SalvageDrone） |
| 资产类型 | 模型 + 材质 + 贴图 |
| 生成工具与版本 | Blender 5.2.2 LTS；生成方式：**Claude 辅助编写 Blender 建模脚本**（程序化建模 + 用 numpy 程序化绘制贴图）。未使用任何图生 3D / 文生 3D 服务，也没有使用外部模型、贴图或字体 |
| 提示词或输入（原文） | 规格页 `Docs/AssetSpecs/SalvageDrone_Spec.md`；脚本 `ArtSource/SalvageDrone/build_salvagedrone.py` 与共用工具 `ArtSource/Common/br_hardsurface.py` 即完整输入 |
| 来源链接 / 获取方式 | 本项目原创，脚本生成 |
| 许可 / 使用条款 | 项目自有 |
| 原始文件存放路径 | `ArtSource/SalvageDrone/SalvageDrone.blend`、`build_salvagedrone.py`；运行：`run_blender.bat`；接入 Unity 与测试：`run_unity.bat` |
| Unity 资产路径 | 模型、贴图、材质在 `Assets/BorderRepair/Art/SalvageDrone/`；最终 prefab 为 `Assets/BorderRepair/Prefabs/Items/Item_SalvageDrone_Final.prefab`；构建器为 `SalvageDroneAssetBuilder` + `ItemAssetPipeline` |

## 3. 问题截图路径

| 序号 | 截图路径 | 问题说明 |
| --- | --- | --- |
| 1 | 无单独截图（第一版渲染已被覆盖） | 机臂末端的橙色航灯悬空：机臂只到电机中心，航灯在中心外 2.4 cm |
| 2 | 无单独截图（同上） | 腐蚀结晶渲染成纯白圆片，像白漆；它借用的贴图小块放在电路板贴图右下角，结果这一角也作为方块显示在板面上 |
| 3 | 无单独截图（同上） | 机臂熏黑痕的 UV 越界，采到了泥痕区域的颜色 |
| 4 | 无单独截图（第二版渲染已被覆盖） | 机身侧面的两块泥痕位置随机，发生共面重叠，Cycles 渲染出黑斑 |
| 5 | `Screenshots/20260924_SalvageDrone_07_unity_intake_front.png` | 接收阶段顶部仍显示上一件信标的反馈。这是反馈提示在换件时没有清除，属于 UI 问题，不属于本资产（AE-002 已记录） |

最终截图：

| 内容 | 路径 |
| --- | --- |
| 整体正面（Blender Cycles） | `Screenshots/20260924_SalvageDrone_01_front.png` |
| 整体背面（Blender Cycles） | `Screenshots/20260924_SalvageDrone_02_back.png` |
| 左前电机特写（Blender Cycles） | `Screenshots/20260924_SalvageDrone_03_motor_fl_closeup.png` |
| 主控板特写（Blender Cycles） | `Screenshots/20260924_SalvageDrone_04_mainboard_closeup.png` |
| 云台相机（正常，Blender Cycles） | `Screenshots/20260924_SalvageDrone_05_camera_normal.png` |
| 右后桨叶（正常，Blender Cycles） | `Screenshots/20260924_SalvageDrone_06_rotor_normal.png` |
| Unity 游戏内：接收阶段 | `Screenshots/20260924_SalvageDrone_07_unity_intake_front.png` |
| Unity 游戏内：左前电机（转 45°、最大放大、俯仰 +20°） | `Screenshots/20260924_SalvageDrone_08_unity_motor_zoom.png` |
| Unity 游戏内：主控板（最大放大、俯仰 -15°） | `Screenshots/20260924_SalvageDrone_09_unity_mainboard_zoom.png` |
| Unity 游戏内：决定面板（费用 1850 超过上限 1200） | `Screenshots/20260924_SalvageDrone_10_unity_decide_cost.png` |
| Unity 游戏内：“建议更换或拆件回收”结果 | `Screenshots/20260924_SalvageDrone_11_unity_result.png` |

Unity 截图由 batchmode 下的 PlayMode 测试 `SalvageDroneCaptureTests` 生成：用代码调用旋转、缩放和流程接口，Canvas 临时切换为相机模式渲染。**这些截图不是真人试玩，也不是交互式编辑器 Game 视图的截图。**

## 4. 检查维度

| 维度 | 结论 | 说明 |
| --- | --- | --- |
| 版权与许可是否清晰 | 通过 | 几何体、贴图、文字全部由项目脚本生成 |
| 与其他作品是否过于相似 | 通过（主观判断） | 通用的 X 形四旋翼结构；没有对照特定产品，也没有做相似度比对 |
| 剧情证据方向是否正确 | 通过 | motor_fl：钟罩和线圈焦黑并带热变色，钟罩上沿轻微受热变形，机臂下方的电调热缩管熔化鼓包，机臂上有熏黑痕；其余三台电机为银色钟罩加铜色线圈，对比明显。mainboard：舱盖翻开，板面有绿白色腐蚀斑、褐色锈斑、斜向干涸泥水线，板上鼓起 7 块结晶，焊盘发暗。rotor：桨叶完整平直，只有桨尖涂装偏淡。camera：镜头干净，云台没有变形。测试 `CaseCompletesWithRecommendReplacement` 也断言了数据中关键证据和费用超限 |
| “严重但可修”的表达 | 通过 | 机架、另外三台电机、相机、桨叶都完好；损坏集中在一台电机和主控板 |
| 结构是否正确（比例、部件完整、无破面） | 通过 | 最终 6 张渲染图中未见破面、穿插或悬空部件（见第 3 节第 1、4 项的修正） |
| 能否拆出维修所需的检查部位 | 通过 | 按功能拆成 5 个 FBX；包装 prefab 只引用 FBX 根对象，InspectionPoint 和碰撞体挂在包装层自己的物体上 |
| 拓扑 / 面数是否在预算内 | 通过 | 见上表 |
| UV / 贴图是否正确 | 通过 | 损伤贴图集分为烧焦、干泥、腐蚀结晶三个区域，各区域之间留有边距；电路板用平面 UV，丝印方向正确 |
| 在 URP 下材质表现是否正常 | 通过 | 8 个 URP Lit 材质，导入日志中没有“未重映射材质”的错误；金属度控制在 0.8 以下，在没有反射探针的场景里也不发黑 |
| 尺寸、pivot、朝向是否符合规范 | 通过 | 测试 `NoseFacesCameraAndTailAfterRotation`：接收时相机和左前电机比右后桨叶更靠近镜头，旋转 180° 后反过来 |
| 扫描点可点击性（自动测试） | 通过 | `AllFourPointsPickableFromScreen`：默认视角下四个点都能通过 `ItemScanner` 按屏幕坐标拾取。`PickImmediatelyAfterRotation`：旋转后立即拾取右后桨叶，转回后立即拾取电机、相机和主控板 |
| 遮挡关系 | 注意 | 舱盖向后翻起，机尾朝向镜头时，舱盖和电池会挡住电路板。这是有意的设计，但玩家必须从机头方向俯视才能看到主控板。测试第一版错误地假设从机尾也能拾取到，已按实际几何修正 |
| 与场景风格是否一致 | 部分通过 | 比工作间的纯色几何体精细得多，风格差距明显 |
| 在固定机位下的可读性 | 部分通过 | 无人机扁而宽，按包围球归一化后，接收阶段整机只占画面宽度约 30%。放到最大后，主控板的腐蚀和烧毁电机的焦黑都能辨认。机臂上的熏黑痕在低角度下不明显，烧毁主要靠颜色对比来表现 |
| 性能（Draw Call、贴图内存） | 未检查 | 71 个网格对象、8 个材质，是三件物品中最多的，没有在 Profiler 中测量；合并网格是后续的优化项 |
| 真实鼠标交互 | **未验证** | 悬停高亮、点击命中、拖动和缩放的手感都没有人工测试 |

## 5. 判定

| 字段 | 内容 |
| --- | --- |
| 判定 | **暂定：采用（有条件）**，等真实鼠标验收后定稿 |
| 理由 | 证据：①面数、材质、贴图都在预算内；②剧情证据方向正确，“严重但可修”表达清楚，Blender 特写和游戏内放大截图都能辨认；③自动测试覆盖朝向、四点屏幕拾取、旋转后立即拾取、“建议更换或拆件回收”流程以及三件全部完成后进入营业总结，最终运行全部通过。成立条件：真实鼠标验收时，玩家能想到从机头方向俯视主控舱，并能点中较细的桨叶。若验收发现接收阶段整机太小、找不到证据，应转为“返修”（调整检查台对扁平物品的取景，或加强机臂熏黑痕的对比）。另外，网格对象数量多，正式版本前需要做一次性能检查 |

## 6. 返修记录

| 字段 | 内容 |
| --- | --- |
| 实际返修时间 | 同一会话内完成，没有单独计时 |
| 返修人 | Claude（修改脚本并在本机运行 Blender / Unity batchmode） |

返修步骤：

1. 机臂延长 3 cm 伸到电机外侧，航灯改为挂在臂端下方。
2. 损伤贴图集重新分区（烧焦 / 干泥 / 腐蚀结晶），各区域留边距。腐蚀结晶改为铜绿底色加白色盐晶和暗色杂质，结晶网格表面加入随机起伏；电路板贴图不再夹带贴图集小块。
3. 机臂熏黑痕的 UV 改为以斑块中心为基准映射，不再越界。
4. 烧焦贴图改为以近黑积碳为主，局部加上钢材热变色（黄褐→紫→蓝）和焦红裂纹。
5. 机身两侧的泥痕改为固定且不重叠的位置，并前后错开 0.1 mm，消除 Cycles 黑斑。
6. 测试修正：`PickImmediatelyAfterRotation` 第一次运行失败，原因是我在测试里假设从机尾也能拾取到主控板，而实际上它被向后翻起的舱盖挡住，模型本身没有问题。已改为机尾朝向镜头时只拾取桨叶，转回机头方向后再拾取主控板。截图测试里电机特写的旋转方向写反了（-45° 转到的是右前臂），已改为 +45°。

## 7. 返修后结果（batchmode 自动验证，本机 2026-09-24）

| 阶段 | 退出码 | 结果 | 证据 |
| --- | --- | --- | --- |
| Blender 建模 + 导出 + 渲染 | 0 | 5 个 FBX、3 张贴图、材质清单、6 张渲染图 | `ArtSource/SalvageDrone/blender_run.log`、`stats.json` |
| Unity 导入 + 生成 prefab | 0 | 无编译错误或警告，无未重映射材质 | `unity_logs/1_import.log` |
| EditMode 测试 | 0 | 11/11 通过 | `unity_logs/2_editmode.xml` |
| PlayMode 测试 | 0 | 共 15 项：12 项通过，0 项失败，3 项 `[Explicit]` 截图测试按设计跳过 | `unity_logs/3_playmode.xml` |
| 游戏内截图 | 0 | 1/1 通过 | `unity_logs/4_capture.xml` |

| 字段 | 内容 |
| --- | --- |
| 返修后截图路径 | 见第 3 节“最终截图” |
| 复检结论 | 自动检查全部通过；**真实鼠标操作：未验证**；暂定采用（有条件），见第 5 节 |
| 最终资产路径 | `Assets/BorderRepair/Prefabs/Items/Item_SalvageDrone_Final.prefab` |
| 备注 | 通讯器与导航信标资产、原占位 prefab、`ItemInspector` 的 `targetItemRadius` 与取景参数均未修改。这次没有重复做“去掉物理同步”的变异检查，扫描器代码与 AE-002 验证时相同 |
