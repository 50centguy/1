# Quest PC VR 原型

独立入口：`Assets/RepairPropsVR/Scenes/RepairProps_Clinic_VR.unity`。菜单：`Border Repair > Repair Props VR > Open VR Test Scene`。仅四件道具，不是第二晚机器人或正式主项目合并。

先在头显进入Quest Link，再按Play。握持键近手抓取整件，扳机点击面板/零件。交易区接收后放工作台吸附；面板选工具与当前零件执行原有维修动作，完整复测后到交件区交付。传送使用工作台/交易区按钮，转身默认30度分段。误放或手柄失追踪会回收件位，不提供抛掷或任意地面落物。

当前安装XRI3.0.11、OpenXR1.16.1、XR Management4.5.4、Core Utils2.6.0。自动随所有场景启动XR为关闭，只由VR场景初始化；原鼠标场景与正式构建场景列表保留。

实际相关测试：EditMode49/49，PlayMode7/7（含原鼠标4项和新VR3项）。OpenXR必需配置0问题、缺失脚本0；2项可选输入模型迁移提示保留。技术细节见configuration.json。

VR运行验证使用编辑器无显示模式、虚拟追踪/握持输入、公开XRI和维修接口，不是Quest真机验收。头显出图、双眼、追踪恢复、UI射线优先级、每个小零件、舒适度、碰撞和帧率仍待真人；未宣称原有9项全项目EditMode失败已修复。

拆装是流程驱动的预设动作，不是自由手势/物理螺丝刀。完工仍自动移到交件台，暂无修后再抓取搬回交件区。暂不支持不重载场景的VR热切换，重新试请停止Play。

证据与可靠启动脚本：`C:/Users/Administrator/Documents/Codex/2026-09-29/border-repair-station-art/work/repair-props-vr-2026-10-18/`。旧Unity全局缓存拒绝写入，本次只用进程环境设置独立缓存；重开可运行该目录Run-UnityVR.ps1的Interactive模式，不改系统OpenXR runtime或缓存ACL。
