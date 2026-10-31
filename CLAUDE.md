# 边境维修站 / Border Repair Station

地下义体维修处的**经营 + 维修解谜**游戏。Unity 6 + URP。

---

## 游戏设定

- **地点**：北坡港务（NSP）边境港区的地下维修铺。不是军事地堡——老式电梯剪刀式活动铁门、通风格栅、管线、生活痕迹；地面不是整片金属甲板。
- **时间**：夜间班次。房租 1200，**第 3 晚到期**，有付款压力。
- **布局**：入口对面是工作台，中央是义体手术台及环形下挂工具，一侧机器人维修区，另一侧交易区。交易区椅子在柜台内部，显示器朝向内部操作员及墙壁方向。交易区负责领取待维修物品、送出修好的物品。
- **顾客**：港区装卸工罗亚、夜班管线巡检员、巡线员老周、货运司机阿坎、夜班护理员、街区照护站、回收站小梅、二手义体商、收藏家。
- **维修对象**：信号接收器、记忆卡匣、维修电源、赛博御守、通讯器、导航信标、回收无人机、工人义手、UNIT 07 维修机器人。
- **技术水平**：卡带未来主义（cassette futurism）——卡带/卡匣、接收器、CRT 屏一代。东西都是租来的、修过的、捡来的。
- **制度痕迹**：租赁义体贴着封条（`NSP PORT LEASED / VOID IF OPENED`）；边境导航设备的检修封签必须完整；顾客反复要求保留通话记录、录音、原身份和校准参数。
- **UNIT 07**：既是维修对象，未来也可升级为助手（提示、拿工具、屏幕信息）。**这些未来功能不要当成已经实现。**

---

## 技术栈

- Unity 6 + URP
- **Input System**（新输入系统，用 `Keyboard.current`，不是旧 `Input.GetKey`）
- XR Interaction Toolkit（VR 版是独立场景与独立包 `com.borderrepair.repairprops.vr`）

---

## 关键场景

| 路径 | 用途 |
|---|---|
| `Assets/RepairProps/Scenes/RepairProps_Clinic.unity` | **当前手术台 / 诊所主场景** |
| `Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic.unity` | 统一诊所主场景 |
| `Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic_Menu.unity` | 统一诊所菜单入口 |
| `Assets/BorderRepair/Scenes/UnifiedClinic/UnifiedClinic_ArtPreview.unity` | 美术预览 |
| `Assets/BorderRepair/Scenes/Slice/Night1_Counter.unity` | 第一晚柜台 |
| `Assets/BorderRepair/Scenes/Slice/TwoNight_Menu.unity` | 两晚切片菜单 |
| `Assets/BorderRepair/Scenes/Slice/Unit07_Night.unity` | UNIT 07 夜班 |
| `Assets/RepairPropsVR/Scenes/RepairProps_Clinic_VR.unity` | VR 版诊所 |

> **场景是构建器生成的**：`UnifiedClinic` 由两晚切片和原型的构建器生成，`RepairProps_Clinic` 是从 `UnifiedClinic` 复制来的。相关构建器在 `Assets/BorderRepair/Scripts/Editor/` 下（`TwoNight/UnifiedClinicBuilder.cs`、`TwoNight/TwoNightBuilder.cs`、`Unit07Dock/Unit07DockBuilder.cs` 等）。**删构建器会断链**，改场景前先确认是要改构建器还是直接改场景。

---

## 编辑器里的操作

- **按 `-` 键**：召唤病人罗亚进诊所（仅编辑器与开发版；实现见 `Assets/RepairProps/Runtime/SurgeryPatientRuntime.cs:152`，`Keyboard.current.minusKey`）。
- 病人流程：束带扣着时先用准星对准束带、左键解开 → 他自己坐上台躺下 → 扣上三条束带 → 准星对准**右前臂**进入近景手术。
- 近景手术要求：**束带扣紧 + 手术灯打开 + 机械臂展开**，缺哪项面板会写明原因。
- 中途退出近景**不丢进度**，再点手臂可继续。
- 正式游戏流程里还没有"叫病人进来"的正常入口，目前只能靠 `-` 键。这是待办项。

---

## 美术约束（重要）

1. **台面上的仪器屏幕挂在实体设备上**：CRT、单色荧光屏、段码 LCD、指针表头、指示灯。仪器、终端、诊断设备一律如此。
2. **房间导视是全息投影**（2026-11 起的方向变更）。门头、营业时间、受付窗口、价目表、作业台、柜台收/发 pad 这些**房间级标识**用悬浮全息面板，不做实体灯箱。要点见下节。
3. 赛博朋克感来自：**二手义体、混用品牌、补线、旧胶带、手写编号、过期耗材**。
4. **不依赖**：全屏故障特效、强闪烁、静态扫描线。
5. 参考方向：卡带未来主义医疗设备、复古工业诊断屏。
6. 流程里出现的设备要在台上**看得见**——例如"外部测试连接/负载"和"测试供电"对应桌上的测试座：指针表、开关、香蕉插孔、夹线。

## 全息投影的具体做法

实现集中在 `Assets/RepairProps/Runtime/ClinicSignageRuntime.cs` 和 `Assets/RepairProps/Cyber/Resources/BorderRepairCyber/HoloPanel.shader`。

- 面板离墙漂浮 0.10–0.24 m，**不贴墙面**（贴墙会被管线、层板挡住）。
- shader 用 `ZTest Always` 画在场景几何体之上，所以场景里的东西不会遮挡文字。
- 动效参照 `reference-library`（Arasaka 脑机终端 / CP2077 加密传输 / Destiny 星门与诊断报告 / 未来交通导视）：**静止时文字锐利、几乎无色散**；色散只出现在故障的那一条带上；扫描亮带很淡（增益 0.15、周期 9 s）；撕裂罕见（全房间合计约 0.27 次/秒）。
- 可读性第一：任何时候文字都要能读。不做全屏噪点、不做持续强闪烁。
- 层次靠**细边框 + 四角 L 形括号**，不靠把底板调亮。
- 注意：全息面板是加法混合，背后有强光源时会掉对比度。遇到这种情况优先把面板往外挪，或单独调高该面板的 `_Intensity`。

---

## 开发规则

1. **先只读审查**，区分"已完成 / 仅编写 / 未验证 / 已知问题"，不要一上手就大规模修改。
2. **不要自动**做这些事：提交、推送、合并 main、删除旧项目、重做模型。
3. **Unity 编辑器开着时不要跑 batch 测试**，等关闭后再跑。
4. 未经确认不要执行大规模修改。
5. Tripo 网页版由用户本人生成，**不要自动操作或消耗积分**。
6. 描述代码完整程度时必须**实际读文件**，不要仅凭之前的描述认定验收通过。

---

## 分支约定

| 分支 | 说明 |
|---|---|
| `main` | 主线（注意：`main` 里**没有** UnifiedClinic 场景） |
| `integ/repair-props-20261017` | 当前开发线 |
| `backup/repair-props-wip-20261031` | 2026-10-31 的手术台开发 WIP 快照 |

提交信息末尾加：

```
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

---

## 相关文档

- `Docs/Integration/RepairProps20261017/README.md` — 四件道具集成说明
- `Docs/Integration/RepairPropsCyberBench/ART_DIRECTION.md` — 美术方向
- `Docs/Integration/RepairPropsCyberBench/PERFORMANCE.md` — 性能
- `Docs/Integration/RepairPropsCosmetics/README.md` — 外观/手感
- `Docs/Narrative/WorkerHand_Slice.md` — 叙事切片
- `Docs/ArtProduction/TRIPO_WORKFLOW.md` — Tripo 模型流程
