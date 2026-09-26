# 边境维修站 · 第一阶段原型

## 试玩

1. 用 Unity 6000.0.84f1 打开本项目。
2. 菜单 **Border Repair > Open Prototype Scene**（或打开 `Assets/BorderRepair/Scenes/RepairStation_Prototype.unity`）。
3. 按 Play。建议把 Game 视图设为 16:9。

操作：左键或右键拖动旋转；滚轮缩放；R 复位视角；空格切换扫描模式；扫描模式下单击部位进行扫描。

## 目录

| 路径 | 内容 |
| --- | --- |
| `Assets/BorderRepair/Scripts/Runtime/Core/RepairSession.cs` | 流程状态机（不依赖场景） |
| `Assets/BorderRepair/Scripts/Runtime/Data/` | 案例数据结构 `RepairCaseData`、营业数据 `RepairShiftData` |
| `Assets/BorderRepair/Scripts/Runtime/Inspection/` | 旋转/缩放 `ItemInspector`、扫描 `ItemScanner`、检查点 `InspectionPoint` |
| `Assets/BorderRepair/Scripts/Runtime/UI/RepairUIView.cs` | UI 显示 |
| `Assets/BorderRepair/Scripts/Editor/` | 生成材质、占位 prefab、案例数据、场景和 UI 的工具 |
| `Assets/BorderRepair/Data/` | 三个案例和营业数据（可以直接在 Inspector 中编辑） |
| `Assets/BorderRepair/Prefabs/Items/` | 三个占位物品 |
| `Assets/BorderRepair/Tests/` | EditMode / PlayMode 测试 |
| `Docs/AssetEvaluation/` | 开发用的 AI 资产评价模板（与游戏数据无关） |

## 替换内容，不改代码

- **物品模型**：做一个新 prefab，在需要扫描的部位挂 `InspectionPoint`，并填写与案例数据相同的 `pointId`。部位下的网格必须带 Collider。如果要换件，给该部位的 `brokenVisual` / `repairedVisual` 各指定一个子物体（修好的那个初始隐藏）。最后把案例资产的 `itemPrefab` 指向新 prefab。尺寸会自动归一化，镜头缩放上下限会自动重新计算。
- **文本、检查点、诊断、结果、成本**：直接编辑 `Data/Cases/*.asset`。进入 Play 时会校验数据，出错会输出到 Console，EditMode 测试也会检查。
- **增减案例或调整顺序**：编辑 `Data/Shift_Prototype.asset` 的 `cases` 列表。

## 生成工具

- `Border Repair > Build Prototype (create missing)`：只补齐缺失的资产，不改动已有资产。
- `Border Repair > Rebuild Prototype (overwrite generated)`：覆盖工具生成的材质、prefab、案例数据和场景，手动修改会丢失。
