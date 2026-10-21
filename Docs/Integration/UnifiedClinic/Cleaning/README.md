# 边境维修站：污垢 Shader 与局部清洁

## 已接入范围

在现有 `UnifiedClinic` 场景第一夜的物品检查台接入可选清洁工具。通讯器、导航信标、回收无人机共 8 个外壳/按键渲染器配置了 `CleanableSurface`。第二夜整台发动机维修流程没有改动，也没有新增强制工单步骤、奖励或计费。

污垢是可擦除的棕灰色哑光油尘层，不是覆盖全屏的滤镜。擦掉后露出原贴图、颜色、金属反光和法线；原 `WornSurface` 里的磨损、锈蚀与永久污渍仍然保留。屏幕、标签、玻璃和电路板等材质不加入这层污垢。

## 试玩操作

1. 从统一诊所菜单开始第一夜，接收物品，打开检查台。
2. 按 **C** 或左下角“清洁工具”进入清洁模式。
3. 按住左键在外壳上擦拭；右键拖动转动物品，滚轮缩放，R 重置视角。
4. 按 C 退出，继续原来的扫描、诊断与维修。

清洁模式占用左键，不会在松开时误触扫描或维修；空格扫描切换与维修工具选择暂时禁用。退出清洁时恢复进入前的扫描模式。点击 UI、射线未命中清洁表面、检查台关闭或工具动画锁定时，不擦除污垢。移开鼠标、转动或退出模式会结束当前笔画。

底部百分比是各可清洁部件“移除污垢量”的平均值，不是维修完成度，也不是整个模型表面积的精确比例。单个部件达到 97% 后自动清掉少量边缘残留，方便完成清洁。

## 文件与原理

| 文件 | 用途 |
| --- | --- |
| `Assets/BorderRepair/Shaders/DirtSurface.shader` | URP PBR 污垢覆盖层、原有磨损与永久污渍、深度与阴影 Pass |
| `Assets/BorderRepair/Shaders/DirtSurfaceInput.hlsl` | 各 Pass 共享材质属性布局 |
| `Assets/BorderRepair/Scripts/Runtime/Cleaning/DirtMaskData.cs` | 程序化初始污垢、软边笔刷、UV 岛隔离、进度、遮罩导入导出 |
| `Assets/BorderRepair/Scripts/Runtime/Cleaning/CleanableSurface.cs` | 每件物品独立的材质实例、遮罩、精准网格射线探针与资源释放 |
| `Assets/BorderRepair/Scripts/Runtime/Cleaning/CleaningOccluder.cs` | 屏幕、标签及其他不清洁部件的精确遮挡，不允许擦除其背后的外壳 |
| `Assets/BorderRepair/Scripts/Runtime/TwoNight/ClinicCleaningController.cs` | C/鼠标交互、检查台门控、进度 UI |
| `Assets/BorderRepair/Art/UnifiedClinic/Editor/ClinicCleaningInstaller.cs` | 接入 3 个现有 Prefab，生成只读源模型的可读清洁网格副本 |

每个 `CleanableSurface` 使用 128×128 CPU 遮罩与一张运行时纹理，只在发生修改时上传。每个实例都有独立遮罩，不改共享材质，不进行 GPU Readback。

原模型的 UV0 有重叠和重复。安装器生成几何相同的可读网格副本，保留原 UV0，新增独立 UV1（Unity API 的 `uv2` / `RaycastHit.textureCoord2`）作为清洁图集。运行时外观网格和清洁探针都使用该副本，源 FBX 不改。笔刷只影响当前连通 UV 岛，图集边缘不循环；跨岛时不连接笔画。UV 岛边界处可能需要从相邻表面再擦一遍。

安装时还会离线烘焙表面外露遮罩：被其他几何覆盖的接触面、内部和永久遮挡区域不生成污垢、不计进度，避免不可擦的位置卡住完成度。外露检测采用三角面外法线射线，是刚性装配物件的近似检测；更换零件位置/模型后需要重新安装。该检测不在游戏每帧运行。

非凸探针只在清洁时启用，位于 IgnoreRaycast 层，由清洁控制器单独射线检测，不参与普通扫描射线。其他不清洁的实心部件使用独立几何探针遮挡，避免穿过屏幕/面板擦掉内部污垢。这个版本适用于静态/刚性 MeshRenderer，不支持蒙皮变形、透明表面或第二夜发动机。

## 美术调参

Prefab 的 `CleanableSurface` 中可修改：

- `Dirt Color`：当前 `(0.32, 0.215, 0.105)`，棕灰色油尘。
- `Dirt Amount`：当前 `0.95`，控制覆盖强度。
- `Dirt Smoothness`：当前 `0.07`，哑光污垢；增大可做湿油污。
- `Completion Threshold`：当前 `0.97`，自动扫尾阈值。

`ClinicCleaningController` 的 `brushRadiusUV=0.045` 控制图集笔刷大小，`strengthPerSecond=2.4` 控制擦除速度。它不是固定世界尺寸笔刷，不同 UV 岛上的可见宽度可能不同。初始污垢的噪声/条纹参数在 `DirtMaskData` 中。

运行时 `SetDirtAppearance(color, opacity, smoothness)` 可以调整污垢而不重建遮罩。安装菜单会重设上述默认污垢颜色、覆盖强度和光滑度，手工调参后不要随意重新运行安装。

## 事件、重置与存档边界

```csharp
surface.ProgressChanged += progress => UpdateYourUi(progress);
surface.Cleaned += () => OnThisPartCleaned();
surface.ResetDirt();      // 恢复初始污垢，允许下一轮完成事件
surface.CleanAll();       // 清空这一个部件
string mask = surface.ExportMask();
bool restored = surface.ImportMask(mask);
```

关闭检查台并重新打开时，同一物品实例保留遮罩。销毁并重新生成物品、重新开始游戏或读盘重建场景后，会重新生成污垢。**本次没有把污垢遮罩接入现有磁盘存档**，避免改变工单 checkpoint 格式。接口已留好，后续可以按“物品 ID + 表面 ID + 遮罩版本”单独保存，不应只存一个百分比。

卸载组件时恢复原材质和原网格，释放运行时材质、纹理和探针；源资源不受影响。

## 重建与撤除

- Unity 菜单 `Border Repair > Cleaning > Install Customer Item Cleaning`：在重建原物品 Prefab 后重新接入。
- `Remove Customer Item Cleaning`：从三个物品 Prefab 移除清洁组件，不删除生成的副本，便于恢复。
- `Build Preview Player`：打包到 `Builds/UnifiedClinic-20261011-Cleaning`，不会覆盖先前美术预览包。
- 控制器在统一诊所场景加载时自动创建，不需要手动往场景中添加对象。

## 验证

自动测试覆盖：局部/单调擦除、清空/重置、导入导出、无效输入、未使用 UV、图集边缘、UV 岛隔离、实例隔离、完成事件、资源释放；真实 C/鼠标左键擦拭、右键旋转、UI 门控、关闭/重开检查台、屏幕遮挡；以及已有检查台、统一诊所流程、第一人称回归。

原生 Windows Player 可用 `-clinicCleaningCheck <独立输出目录>` 执行同一输入路径的验收，并输出真实相机的 `dirty.png` / `partial.png` / `clean.png` 和 `native-cleaning.json`。验收使用独立临时存档，不影响用户游戏存档。`clean.png` 的全净状态由 `CleanAll()` 构造用于材质对照；`partial.png` 才是实际持续鼠标擦拭的结果。

最终测试数量与原生包结果见交付目录的 `验证结果.md`。
