# 带上 RobotV4 新模型（2026-10-08）

来源：`BorderRepairStation_slice` 工作树里未提交的 11 个文件（`Assets/RobotV4/Model/robot-final.fbx` 与 `textures/` 下 10 张 T_Body_* / T_Engine_* 贴图，修改时间 10-08 02:49–03:17），原样复制（MD5 逐个核对）。`.meta` 没变（GUID、导入设置相同）。slice 工作树里同时期的其他改动（工作台 / 通讯器 / 场景重新生成、渲染设置、04:36 的一个包）不是模型本身，没有带过来，也没动 slice。

FBX MD5：80b94f89… → **b47c9c43006cbef29e820d022d37560b**。

## 新模型改了什么（`robotv4_structure_diff.txt`，前后结构见同目录两个文件）

- 不变：263 个节点的名字、层级、本地位姿；材质分配；全部动画片段（名字、时长、曲线数）。七号两条手臂、机身主体、托盘相关零件不变 → 端盘路线和所有间隙数字与旧模型完全相同（`../../incident_motion.md` 只有时间变了）。
- 变了 19 个网格：左右引擎上盖、下盖重新建模（顶点数约翻倍，上盖变薄约 6 mm）；外侧 / 后侧锁扣网格偏移 5–8 mm；后盖帽、铭牌、机身外壳、前框、下颌、底部模块、后格栅细节；10 张贴图。

## 新模型带来的问题和处理

| 问题 | 发现方式 | 处理 |
|---|---|---|
| 首单场景里的点选盒、落点、镜头是生成场景时按网格算好写进去的，全部过期（上盖翻面放到操作垫上悬空 11.4 mm） | EditMode `BenchPlacements_DoNotPenetrate` / `DropZones_SitOnRealBenchObjects_InClearSpace` 失败 | 按原有生成链重新生成：首单测试场景 → 布局 A/B → 七号核心场景 → 两晚场景。上盖翻面占地 241×230 → 242×232 mm、高 95 → 88 mm；落点移动约 1 mm；“保养记录”镜头对准点下移约 1 cm |
| **上盖内侧保养标记整片埋进新上盖内壁 3.1–7.0 mm，保养记录看不见**（诊断必看项） | 程序检查全部通过，是看截图发现的（“观察”只查距离，不查遮挡） | 场景生成时按美术说明的同一方法（逐点沿贴纸法线贴到内表面、离表面 0.35 mm）重新贴合 441 个顶点，生成 `Assets/BorderRepair/FirstOrder/Art/Generated/UNIT07_FK_CoverInnerLabel_L_RefitToCover.asset`（原地更新，GUID 固定，5 个场景引用同一份）；**不改故障美术包源 FBX**。新增 EditMode 检查 `CoverLabel_SitsOnCoverInnerSurface`（每个顶点离内表面 0.2–0.5 mm） |
| `SourceArt_NotModified` 报 RobotV4 FBX 被改 | EditMode | 这次是有意换模型：基准 MD5 更新为新值，注释写明原值 |
| `FaultKit_MountedOnRealParts` 要求标签网格直接来自美术包 FBX | EditMode | 改为接受美术包 FBX 或由它重贴生成的网格 |

另外，这次重新生成也带上了 10-02 之后合入、但之前没有重新生成进场景的故障美术包更新（c60b768：积尘偏暖、纤维减少加粗，三角面 11664 → 9886），以及生成器现有的落点标记材质（开自发光）。

**过渡做法要注意**：保养标记是程序按新上盖重贴的，贴纸形状随新内表面变化（起伏约 3.9 mm），字迹没有重新排版。正式版应由美术按新上盖重新导出 `UNIT07_FK_CoverLabel.fbx`；导出后重新生成场景，贴合偏差在 0.05 mm 内时生成器会自动改回用美术包原网格。

## 验证（新模型包 `Builds\TwoNightSlice_N1N2_20261008-fpv-newmodel`）

| 项 | 结果 |
|---|---|
| EditMode | 121 / 121（多 3 个标签检查，三个首单场景各一） |
| PlayMode | 108 个：101 通过、0 失败、7 跳过（原有截图采集测试）；含逐帧端盘探针、完整 45 步首单、第一人称测试 |
| 两个独立进程 | `two_process/`：第 1 段 23 / 0，第 2 段 32 / 0（三种窗口尺寸实际达到）；继续不改存档；+400 / −100 两笔，七号未修，托盘归位 |
| 核心自检 | `core_selfcheckBG_run1–3`：3 次 45 步全过 |
| 看图 | `core_selfcheckBG_run1/08_…保养记录…png`：标记可读，与旧模型一致；`two_process/p2_09_first_person_aim_cover.png`：新上盖、新贴图 |

`pre_build/` 是标记重贴之前那次构建的记录（程序检查全过，但保养记录被挡住——保留作为“程序检查没发现”的证据）。`-fpv-newmodel-pre` 包对应它，可删。

没验证：真人看新模型的观感；新贴图在目标机器上的表现。
