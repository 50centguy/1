# 两晚切片 N1N2 稳定化（2026-10-05）

分支 `integ/two-night-slice`（基于 72838e9）。托盘美术 `art/unit07-tray-variant` 658f8f7 以 `--no-ff` 整条合入（c9642c7，含初版 2fe22e5 新增的全部文件及 .meta / GUID；没有搬 Library / 缓存）。没有合并 main 或整条 workorder06；共享主工作区（robot-v4-import，34 条未提交）MD5 对照未变；RobotV4 FBX MD5 仍为 80b94f89…。

**下面所有“程序点击 / 程序鼠标事件”都不是真人试玩；隔离美术场景的检查也不能代替发布包验证。**

## 交付

| 项 | 内容 |
|---|---|
| 新试玩包 | `Builds\TwoNightSlice_N1N2_20261005-n1n2s\TwoNightSlice.exe`（02:50 构建，0 错误 0 警告；`Builds/` 不进 git） |
| 保留的旧包 | `Builds\TwoNightSlice\`（8dc8bb8 基准）、`Builds\TwoNightSlice_N1N2_20261004-n1n2\` |
| 中间构建（可删） | `Builds\TwoNightSlice_N1N2_20261005-n1n2s-pre`（02:10）、`-pre2`（02:45）：各自的检查记录在 `pre_build/` |
| EditMode | `EditMode_results.xml`：118 / 118 通过 |
| PlayMode | `PlayMode_results.xml`：107 个，100 通过，0 失败，7 跳过（7 个都是原有的 Explicit 截图采集测试） |

## 任务一：托盘

- 发布场景 `Unit07_Night` 里在原托盘同一父对象、同一本地位姿放 `Dock_PartsTray_HandleRaised`，原托盘 `SetActive(false)`；原维修座 prefab、FBX、共享材质都没改。挂点直接用交付的 `Unit07TrayGripData_HandleRaised`。右手单手。
- `TrayGeometry`：只取启用、激活的 Renderer / MeshFilter（隐藏旧盘不参与），按 GripBarStart/End 标记把提手拆成“握杆直段”和“立柱 / 安装座”。`PlanIncident`、`TwoNightMotionProbe` 用它；`TwoNightMeshCache` 本来就遍历全部子 MeshFilter。
- 路线按新件重算（`../incident_motion.md`）：放盘位 = 原位 + 0 mm，上爪张开后向后滑出 20 mm 放手、退到 80 mm 升起，搬运高度 60 mm；旧托盘常量不再使用。
- 归位检测：位置、朝向、**父对象**都要回到原位。
- 真实播放（`../incident_motion_play.md`）：return / release / away 三段每帧量（409 帧），其余每 3 帧。右手非爪齿 ↔ 托盘全部 ≥ 2.1 mm；爪齿 ↔ 盘体 / 立柱 ≥ 3.5 mm；只有爪齿 ↔ 握杆为 0（允许的握持接触）。结束时托盘父对象、位置、朝向回到原位，机身回正，覆盖层权重 0（Animator 接管）。
- **测距局限**：点到三角形 + 边穿三角形，不算边到边距离、判不出共面重叠，帧间不插值。结论是“这些采样里没有测到穿插”，不是连续无穿插的证明。

## 任务二：界面

- 手册展开时 `FirstOrderInput.ModalBlocked`：不悬停、不点后方 3D、悬停提示隐藏；备注框和手册按钮照常；关上恢复。手册底色改为接近不透明（原来后面反馈条的文字透出来）。
- 悬停提示：高度随文字；依次试光标右下 / 左下 / 右上 / 左上，避开状态栏、反馈条、镜头栏、右下工具按钮（含“手册”“调试”）和观察面板；四个方向都不行就不显示。
- 诊断记录：开场列“左上轴承（原位）”，不写“磨损”；“新旧轴承对比”在旧轴承实际拆下后才出现（PlayMode `TwoNightUiTests` 与 `../night2_ui_check.md`）。
- Player 三种窗口（第 2 段自检里的 `LayoutCheck`，虚拟鼠标设备）：
  - **02:10 构建（-pre），显示器 2160×3840**：1600×900、1920×1080、1280×960 都实际达到；三种尺寸下按钮 15 个，出画面 0、互相重叠 0、截断文字 0；手册展开后方 3D 不悬停 / 不点、点“关闭”恢复；悬停提示扫点显示 34 / 48 / 51 次，压按钮 0、出画面 0。`pre_build/two_process/selfcheck_phase2.txt` 及截图。
  - **最终构建**：运行时显示器已变为 1024×768（本机桌面状态变了），三种请求尺寸都达不到，自检如实记为 3 项失败；在 1024×768 下其余项都通过（按钮、手册遮挡、关闭恢复、悬停提示）。`final/two_process/selfcheck_phase2.txt`。最终构建与 -pre 的界面代码相同（之后只改了验收驱动的等待预算和这一尺寸判定），但**最终二进制上没有在 1600×900 / 1920×1080 / 1280×960 实测**。

## 任务三：核心自检首次超时

新增记录：`selfcheck_timeline.txt`（每个等待的开始 / 每秒 / 结束：实时、游戏时间、帧号、维修座状态、转速、焦点；`OnApplicationFocus` / `OnApplicationPause`；后台线程每秒心跳，帧不前进时标出）；`foreground_windows.txt`（外部每 0.5 秒记前台窗口所属进程名，只记进程名，不读屏幕）；进程提前退出时也写出已有的 `selfcheck.txt`。

运行策略分开：
- **真人版本**：项目设置 `runInBackground = false` 不变（失焦时暂停）。
- **自检**：命令行 `-selfCheckRunInBackground` 只在自检进程里把 runInBackground 打开，日志第一行写明“自检参数打开，玩家版本不是这样”。

等待预算：原来按实时 60 秒算，Player 被暂停时实时照走。现在按 Player 实际运行时间算（每帧最多计 0.25 秒），暂停不扣预算，暂停本身仍记在时间线里。**没有加大 60 秒上限。**

| 记录 | 包 | runInBackground | 结果 |
|---|---|---|---|
| `first_failure_20261004.md` | 20261004-n1n2 | false | 首次失败（事后重建，原文件被覆盖） |
| `pre_build/core_selfcheck/new_human_run1` | -pre | false | 16 s 时失焦 → `PAUSE True` → 帧停在 9557；176 s 后短暂获得焦点前进 ~960 帧又失焦；被外部 600 s 超时杀掉（无结果行） |
| `…/new_human_run2` | -pre | false | 9 s 时失焦 → `PAUSE True` → 帧停住；手动结束 |
| `…/new_human_run3` | -pre | false | 启动时就没焦点，一直停在第 1 帧；240 s 被杀 |
| `…/new_human_run4` | -pre | false | 全程有焦点（Unity 报告），45 步全通过。外部前台记录同时只看到另一个进程，两者不一致，原因未查明 |
| `…/new_selfcheckBG_run1–3` | -pre | true（自检） | 3 次全通过；run1 在 6.9 s 失焦但帧继续前进 |
| `…/baseline_8dc8bb8_human_run1–3` | 8dc8bb8 基准 | false | run1 有焦点 16 s 后前台被别的进程拿走，停住、150 s 被杀；run2 通过；run3 中途失去前台 52 s 又回来，通过（82 s） |
| `pre_build/pre2_0245/core_selfcheck/*` | -pre2 | 3×true / 3×false | 6 次全通过，全程有焦点 |
| **`final/core_selfcheck/selfcheckBG_run1–3`** | **最终** | true（自检） | **3 次连续全通过**（45 步，失败 0） |
| **`final/core_selfcheck/human_run1–3`** | **最终** | false（真人设置） | **3 次连续全通过**，全程有焦点 |

结论（按证据强弱）：
- **已在本机直接观察到**：runInBackground = false 时，Player 一失焦就收到 `OnApplicationPause(true)`，帧停止前进，实时照走；基准包同样会停住，不是这次改动引入的。自检参数打开后，失焦时帧继续、自检通过。
- **推断，未证实**：10-04 首次失败就是这个原因。它的 102 秒空档、恢复后第一张截图即失败、叶轮仍在转，都符合“暂停期间实时超过 60 秒，恢复后的第一帧等待即判超时”；但当时没有焦点和帧记录。
- 新的等待预算在“失焦暂停 → 恢复”的 Player 场景下**没有实测**。失焦的几次都没有再拿回焦点，而为了测它需要抢前台窗口，会打断正在使用这台电脑的人，所以没有做。

## 两个独立 exe 进程（最终包）

`final/two_process/`：第 1 段 22 项全通过（菜单 → 柜台 → 账本 800 / 还差 400 → 端盘演出（新托盘）→ 停靠、夹紧、断电、减速中拒绝、停稳后拒绝拆盖 → 登记 → 夜末存档）→ 进程退出 → 第 2 段新进程：继续 → 第 2 晚、现金 800、房租尚差 400、工单 WO-U07-N1；维修座 RotorsStopped、断电、转速 0；七号未修；不重放事故；可开始检查；继续没有改存档（逐字节相同）。存档里仍只有两笔第一晚流水（+400、−100），`unit07Repaired=false`，`trayStowed=true`。第 2 段 3 项失败仅为上文的窗口尺寸项。

## 未验证

- 真人试玩（全部是程序事件）；VR；目标机器。
- 最终二进制在 1600×900 / 1920×1080 / 1280×960 的布局（-pre 已在这三种尺寸实测）。
- 新等待预算在 Player 里“失焦暂停 → 恢复”这一情形。
- 10-04 首次失败的确切原因（见上，推断）。
- 测距局限见任务一。

截图只用 ScreenCapture / Unity 相机。全部截图都留在本机；git 里只放最终两进程那一套和 `final/core_selfcheck/human_run1` 那一套（其余见同目录 `.gitignore`）。
