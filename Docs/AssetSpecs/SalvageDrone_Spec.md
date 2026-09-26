# 资产规格：回收无人机（SalvageDrone）

> 建模前编写（2026-09-24）。对应案例 `Case_03_SalvageDrone`，评价记录 AE-003。
> 生成方式：Claude 辅助编写 Blender 建模脚本（程序化建模 + 程序化贴图），不使用图生 3D 服务或外部模型/贴图。

## 1. 剧情要求（来自案例数据，不得画反）

| pointId | 显示名 | 案例发现文本 | 关键证据 | 视觉必须表现 |
| --- | --- | --- | --- | --- |
| `motor_fl` | 左前电机 | 电机线圈烧毁，需要连同电调一起更换 | 是 | **异常**：外壳发黑并带热变色，线圈焦黑，臂上有熏黑痕迹，电调热缩管熔化鼓包 |
| `mainboard` | 主控板舱 | 主控板大面积进水腐蚀，多处焊点氧化，需要整板更换 | 是 | **异常**：舱盖已打开；电路板上有绿白色腐蚀结晶、褐色锈斑和干涸的泥水线；焊点发暗 |
| `rotor` | 桨叶 | 桨叶有轻微磨损，还能继续用 | 否 | **正常（轻微磨损）**：完整、平直、没有断裂或弯折；只有桨尖涂装略有磨损 |
| `camera` | 云台相机 | 云台和镜头正常 | 否 | **正常**：镜头干净完整，云台没有变形 |

案例的判断核心是“技术上能修，但维修费 1850 超过上限 1200”。所以损坏应当看起来**严重但可修**：部件损坏，机架和其余三台电机完好，不要画成整机粉碎。机身来自河滩，下部有少量干泥痕，只是环境叙事，不是扫描点。

## 2. 造型

X 形四旋翼的回收/巡检无人机：圆角浅灰机身；顶部前半是主控板舱（舱盖向后翻开），后半是电池；碳纤维色机臂末端有橙色航灯；机头下方前伸出一个两轴云台相机；底部有两根起落架滑橇。不参照任何现有游戏或商业产品。

## 3. 朝向与 pivot

- Blender：单位米，Z 向上，**正面（机头）朝 -Y**。原点在机身中心。
- 5 个 FBX 共用同一原点：`SalvageDrone_Body`、`SalvageDrone_MotorFL`、`SalvageDrone_Mainboard`、`SalvageDrone_Rotor`、`SalvageDrone_Camera`。
- 导出与 Unity 约定和前两件相同：-Z forward、Y up、Apply Transform；包装 prefab 内绕 Y 轴旋转 180°，机头朝向检查台镜头。
- “左前”指从机头看过去的左前方，即 Blender 的 (-X, -Y) 象限；在检查台上默认显示在画面左前方。
- 运行时归一化到包围球半径 0.22；这次不修改检查台参数。

## 4. 扫描点布局

| pointId | 位置（Blender） | 默认视角下是否可见 | 碰撞体 |
| --- | --- | --- | --- |
| `motor_fl` | 左前臂末端 (-0.12, -0.12)，含电调熔化处 | 是（左前） | 包住电机和电调的 BoxCollider，外扩 3 mm |
| `mainboard` | 机身顶部前半的开放舱内 | 是（从前上方俯视） | 包住电路板和板上元件（不含舱盖）的 BoxCollider，外扩 2 mm |
| `rotor` | 右后电机上方的桨叶 (+0.12, +0.12) | 是（右后） | 包住这一副桨叶的 BoxCollider（较薄），外扩 3 mm |
| `camera` | 机头下方前伸的云台 | 是（机头前下方，伸出机身轮廓） | 包住云台和相机的 BoxCollider，外扩 2 mm |

其余三副桨叶和三台正常电机属于机身，点到时提示“未见异常”。舱盖也属于机身，避免它的碰撞体挡住电池。

## 5. 预算（试验性）

| 项目 | 预算 |
| --- | --- |
| 三角面：机身（含其余 3 台电机、3 副桨、电池、舱盖、起落架） | ≤ 7,000 |
| 三角面：motor_fl | ≤ 1,200 |
| 三角面：mainboard | ≤ 1,500 |
| 三角面：rotor | ≤ 400 |
| 三角面：camera | ≤ 1,000 |
| 三角面：合计（同屏） | ≤ 11,000 |
| 材质数 | ≤ 8（机身外壳、深色碳纤维/橡胶、金属、电路板、损伤贴图集、正常铜线圈、镜头玻璃、醒目色） |
| 贴图 | ≤ 3 张，最大 1024²：磨损 1024²、腐蚀电路板 512²、损伤贴图集（熏黑/泥痕）512×256 |

## 6. 交付物

- `ArtSource/SalvageDrone/build_salvagedrone.py`、`SalvageDrone.blend`、`stats.json`、`run_blender.bat`、`run_unity.bat`
- `Assets/BorderRepair/Art/SalvageDrone/{Models,Textures,Materials}`、`SalvageDrone_Materials.json`
- `Assets/BorderRepair/Prefabs/Items/Item_SalvageDrone_Final.prefab`（保留原占位 `Item_SalvageDrone.prefab`）
- 截图：`Docs/AssetEvaluation/Screenshots/20260924_SalvageDrone_*.png`
- 评价记录：`Docs/AssetEvaluation/20260924_SalvageDrone.md`（AE-003）
