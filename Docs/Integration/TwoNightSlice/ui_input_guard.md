# 两晚切片 · 界面点击不穿透 3D、文字输入时快捷键不生效（程序鼠标 / 键盘事件）

- 2026-10-04 01:11，Unity 6000.0.84f1，屏幕 640×480，场景 `Assets/BorderRepair/FirstOrder/Scenes/Slice/TwoNightSlice.unity`
- Input System 虚拟鼠标设备的事件经 InputSystemUIInputModule（界面）和 FirstOrderInput（3D）处理；镜头只经界面镜头栏切换。**程序生成的鼠标事件，不是真人试玩。**

1. 手册面板盖住 维修座：Dock_Clamp_R_Grip：左键、右键都没穿透
2. 备注框有焦点：数字键 2、F3 都没有生效
3. 关闭手册后：数字键 2 切到左引擎；调试按钮开 / 关 HUD
