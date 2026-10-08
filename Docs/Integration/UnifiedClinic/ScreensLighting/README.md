# 屏幕没画面 + 整体打光（2026-10-09）

分支 `feat/clinic-carry-to-bench`（接在 3ff3240 “拿到维修台上修” 之后），工作树 `clinic-bench`。

**下面都是程序拍图 / 程序输入，不是真人试玩；观感判断是我看截图做的，需要你在真实屏幕上看一下。**

## 原因

美术说明（`ArtSource/UnifiedClinic/README.md`）写明：“灯光只在 Blender 渲染里做，Unity 里的灯没有制作；交易台状态灯和屏幕需要程序驱动发光”，以及每个台面有朝房间的状态灯（EMPTY 灭、CARGO 亮、READY 闪）。统一诊所接入时这几件都没做：

1. **屏幕 / 灯管不发光**：`M_UC_Screen`、`M_UC_LampWarm` 的全局光照标记是“发光视为黑色”（`m_LightmapFlags: 4`），URP 校验材质时把 `_EMISSION` 关掉了——导入脚本虽然调用了 EnableKeyword，但没设标记。结果：维修台机械臂显示器、收件台显示器、状态灯、墙上灯管、门灯、七号笼灯、手术灯面都只是被灯照着的普通表面。
2. **状态灯没有程序控制**。
3. **没有灯具光源**：房间只有生成器加的三盏点光（手术台、交易台、门口）+ 原有的台灯 / 维修座聚光 + 0.15 的方向补光，环境光很暗；光圈外的墙、柜子、货架、门近乎纯黑（维修座视角 52%、房间后左 41% 的像素接近全黑）。
4. 上一步（3ff3240）把检视舞台挪到维修台时，两盏检视灯（2.5 / 1.5，半径 3 m）也跟着到了台上且常亮，维修台比房间其他地方亮两三倍；台上维修用的检视相机没开后处理，色调和房间不一致。

## 改动

| 项 | 做法 |
|---|---|
| 发光材质 | `ClinicLookTuning.FixEmissiveMaterials`：美术目录下有发光颜色的材质设为实时发光 + `_EMISSION`；`ClinicArtImporter` 同步加上标记，以后重新导入不会再丢 |
| 灯具光源 | `ClinicLookTuning.Apply`：按 `Shell_LampTubes` 网格的三角形连通分出 5 根灯管（四个斜角 + 东墙），每根下方一盏柔和暖色点光（0.75，半径 3.2 m，无阴影）；门灯、七号笼灯各一盏；维修台上方一盏补光（0.9，2.3 m）。都放在 `ClinicFixtureLights（灯具补光，程序生成）` 下 |
| 环境光 | Trilight 天空 0.08→0.13、中间 0.04→0.075、地面 0.025→0.04（只托起纯黑，保留暗调） |
| 检视相机 | 后处理与房间相机一致 |
| 检视灯（运行时） | 只在台上维修时开，强度降到 40% |
| 状态灯（运行时） | 收件台：有物件等着接收时亮，拿起后灭；交件台：修好 / 待退回的物件等着交出时闪（1.2 s 周期）；第二晚和其他时候灭。用 MaterialPropertyBlock，不改共享材质 |
| 生成器 | `ClinicGameplayIntegrator` 最后调用 `ClinicLookTuning.Apply`，以后重建诊所自动带上；菜单 `Border Repair/Unified Clinic/Apply Lighting And Emission Tuning` 可单独执行（可重复执行） |

收件台显示器 `Trade_CRT` 的屏幕朝向柜台后面的操作员座位、背面散热格栅朝房间——这是美术说明里的设计（“Its ventilated back faces the room”），没有改；从房间这侧看不到它的屏幕。如果希望玩家在房间这侧看到它，需要美术调整朝向。

## 亮度对比（运行时、房间相机含后处理，玩家眼高；`look/look_before.txt` → `look/look_after2.txt`）

| 视角 | 平均亮度 改前 → 改后 | 接近全黑像素 改前 → 改后 |
|---|---|---|
| 门口 | 0.10 → 0.10 | 33% → 28% |
| 手术台 | 0.14 → 0.11 | 27% → 25% |
| 维修座 / 七号 | 0.13 → 0.15 | 52% → 33% |
| 维修台 | 0.33（检视灯常亮）→ 0.15 | 0.2% → 26% |
| 收件台前 | 0.15 → 0.17 | 36% → 29% |
| 房间后左 | 0.08 → 0.09 | 41% → 25% |
| 房间后右 | 0.13 → 0.11 | 24% → 25% |
| 台上维修 | 0.41（1.7% 过曝）→ 0.32（无过曝） | 0.1% → 0.1% |

截图：`look/before_*.png` / `look/after_*.png`，实机 `Native/p1_night1_room.png`、`Native/p2_night2_room.png`。采集用的是 PlayMode 测试 `UnifiedClinicLookCapture`（Explicit，点名才跑；环境变量 `CLINIC_LOOK_TAG` 指定文件名前缀）。

## 验证

| 项 | 结果 |
|---|---|
| EditMode | 154：145 通过，9 失败——仍是那 9 个新模型首单场景问题，没有新增；新增 `ClinicScreenAndLampMaterials_ActuallyEmit` 通过 |
| PlayMode | 130：122 通过、0 失败、8 跳过（7 个原有截图采集 + 新的观感采集）；状态灯亮 / 灭 / 闪的检查加在 `Walking_ReceiveCarriesItem_PlaceOnBench_RepairInRoom` 里 |
| Player 包 | `clinic-bench\Builds\UnifiedClinic-20261009-ScreensLighting\BorderRepairClinic.exe`（0 错误 0 警告；上一版 `-CarryToBench` 保留） |
| 两个独立进程 | `Native/`：第 1 段、第 2 段都通过 |

## 没验证

- 真人在真实显示器上的观感（亮度、色调、灯管是否太抢眼）。
- 性能：多了 8 盏实时点光（无阴影），没有重新测帧时间。
- 状态灯闪烁的节奏是我定的。
