# 道具涂装原型

独立项目：`D:/uetounity/UnityProjects/BorderRepairStation_repairprops`。

入口仍是鼠标 `Assets/RepairProps/Scenes/RepairProps_Clinic.unity` 和 VR `Assets/RepairPropsVR/Scenes/RepairProps_Clinic_VR.unity`。功能由运行时附加，不重新生成场景，也不更改正式构建入口。

## 操作

1. 接收道具并放到维修台。关机、断开测试连接，外壳复装完整时才能涂装。
2. 涂装面板选择喷漆、贴花或换图。喷漆为按住左键/右手扳机，贴花和所选外壳换图为单次点击。右键仍可旋转鼠标检视。
3. 面板提供色块、半径、浓度、图案与旋转；换图也可点“整件外壳”。不改屏幕、按钮、接口、内部槽或核心。
4. 选择“维修”退出涂装，再进行原有维修、复测和交件。涂装不影响损坏状态、报价或工单队列。
5. 撤销/重做针对当前物品；清除需确认，清除本身也能撤销。

## 自定义图片

面板的图片目录按钮打开当前游戏的 `Application.persistentDataPath/CosmeticImages`，重载图案后选择图片。同一图库供鼠标与 PC VR 使用。当前实际目录是 `C:/Users/Administrator/AppData/LocalLow/DefaultCompany/BorderRepairStation/CosmeticImages`。支持 PNG、JPG/JPEG，单张文件最多8MB，宽高各不超过2048。目录中不递归扫描、不接受链接文件，文件头先检查尺寸再解码。显示最多16图案（含3内置），本次会话最多32次不同版本导入，导入图片合计最多16777216像素，旧版本保留给撤销记录使用，不无限累积。

提供三个内置位图图案，无外部图片时也可测试。

## 边界

- 涂装属于当前工单物品的运行时实例，退出Play或重新加载场景不会保存；没有写入正式存档格式。自定义图库文件会保留。
- 仅外观图层，不是修改模型UV或永久烘焙资产。每个编辑过的外壳材质槽一个512方形ARGB图层，原始基色、法线、金属和遮蔽贴图保持原分辨率。
- 喷漆和贴花按命中表面附近的世界空间投射再烘焙到原UV，不是任意几何贴纸模型。原UV若重叠，同一槽可能出现镜像印迹；跨接缝、大幅弯曲和很细小的表面需要手动检查。
- 每段喷涂最多256采样点，连续绘制以30Hz为上限；每次清除之间64个编辑动作，历史采用有上限的清除分段记录，不无限累积贴图快照。
- 原模型、导入材质、场景与构建设置不修改；仅独立项目增加运行时代码和VR适配。未合并主开发项目。
- 真实VR可读性、双眼渲染和帧率必须戴头显验收；自动模拟不能替代。

## 验证

实际 Unity 相关EditMode54/54、PlayMode17/17通过，涵盖GPU喷漆像素、透明贴花和旋转、换图/撤销/复原、实际鼠标与UI遮挡、四单隔离、虚拟VR扳机和失追踪退出。见同目录PROGRESS.md及Evidence。

桌面面板预览是编辑器离屏诊断图，VR图是单目编辑器模拟，不是人工试玩。真实头显和帧率、独立Player构建、正式存档持久化未验收；没有重跑全项目，也没有宣称修复之前9个不相关编辑测试失败。

重新打开独立项目可运行本目录 `Open-Cosmetics.ps1`（默认鼠标场景），或加 `-VR`；它使用进程级独立UPM缓存，既不更改全局配置，也不启动第二个编辑器。鼠标场景还可通过Unity菜单 `Border Repair > Repair Props Cosmetics > Open Desktop Test Scene` 打开。不会自动进入Play。

着色器实现核对了项目已安装的URP17源码，以及Unity官方 [ShaderVariantCollection](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ShaderVariantCollection.html) 与 [运行时变体裁剪说明](https://docs.unity3d.com/6000.0/Documentation/Manual/shader-variant-stripping.html)。仅新增模块本地变体集合，不修改全局图形设置。
