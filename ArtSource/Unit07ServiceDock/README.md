# UNIT 07（七号）维修座资产包

七号是悬浮维修助手。这个维修座用于维修时停靠：机器人落座后夹紧、断电，涡轮停转；玩家从正面能接近两侧引擎。资产用 Blender 5.2.2 脚本生成，可以完整重建。

- **原创**：由 Claude 辅助编写建模脚本，程序化建模、程序化绘制贴图，没有使用外部模型、贴图或字体。
- **与 V4 模型的关系**：
  - 没有读取、复制或修改 V4 模型的网格。
  - 七号的占位体只用了 V4 交接件的**包围盒测量数字**（只读测量，写在脚本的 `ROBOT_BLOCKS` 里）。
- **还没做的**：
  - 没有碰撞体，也没有动画。只提供了独立对象、原点、旋转轴和角度属性，方便程序后续接入。
  - 没有在正式 Unity 项目中导入。只在一个测试副本里做了导入检查，见第 6 节。

## 1. 文件

| 文件 | 内容 |
| --- | --- |
| `build_unit07_dock.py` | 建模脚本：几何、贴图、材质、检查、统计、导出、渲染。依赖 `ArtSource/Common/br_hardsurface.py`，只在运行时补了共用字库缺的 W、F 两个字母，没有改共用库文件 |
| `run_blender.bat` | 双击运行；加参数 `norender` 只生成、不渲染 |
| `Unit07ServiceDock.blend` | 源文件，含占位体（`_Placeholder_UNIT07`）和只用于渲染的旧轴承道具（`_RenderProps`） |
| `Export/UNIT07_ServiceDock.fbx` | 维修座（带层级、原点和自定义属性；不含占位体） |
| `Export/UNIT07_RobotPlaceholder.fbx` | 七号尺寸占位体。**只供程序摆位和检查，不是美术资产** |
| `Textures/T_Dock_Grime.png`（1024²） | 轻微旧化磨损，用于象牙白、工业灰、钢件 |
| `Textures/T_Dock_Warning.png`（512×128） | 黄黑警示斜纹，带褪色和划痕 |
| `Textures/T_Dock_Labels.png`（512²） | 贴图集：开关铭牌（POWER / ON / OFF）、UNIT 07 铭牌、零件盒标签（OLD BRG） |
| `materials.json` | 7 种导出材质的颜色、金属度、光滑度和贴图，用来在 URP 中手动建材质 |
| `stats.json` | 三角面、材质、贴图统计，以及每个对象的原点、旋转、角色和角度属性 |
| `clearance_report.json` | 空间检查结果（第 4 节） |
| `Renders/R01–R11.png` | Cycles 渲染（第 5 节） |
| `unity_import_check.txt` | Unity 测试副本中的导入检查输出（第 6 节） |
| `blender_run.log` | 生成日志：只在本地生成，被仓库的 `.gitignore`（`*.log`）忽略，不提交 |

## 2. 尺寸与布置

坐标系：Blender Z 向上，**机器人正面朝 −Y**（与 V4 模型一致），玩家站在 −Y 一侧，单位米。

- **七号占位体**：总尺寸 1.0196 × 0.4775 × 0.6429 m（宽 × 深 × 高），与 V4 交接件一致。
  - 由机身、前框、下舱、电源托盘、下巴盖、手臂硬点板、背部安装轨、背部维修盒、机背螺栓与通风口、顶盖、两台引擎、两臂等包围盒组成。
  - 两臂有工作姿态和收起姿态两套，检查时两套都用。
- **停靠高度**：七号的根原点（模型最低点）在 z = 0.72 m。
  - 机身底面 0.913 m，引擎中心约 1.14 m，站姿双手都够得到。
- **维修座包围盒**：x −0.727 … 0.500，y −0.334 … 0.520，z −0.010 … 1.030（约 1.23 × 0.85 × 1.04 m）。
- **支撑方式**：
  - **承重**：两块橡胶接触垫托住七号机底两条手臂硬点板的后段（y 0.100…0.186）。
  - **防前倾、左右限位**：七号的重心粗估在 y ≈ −0.034，落在接触垫前方，所以不能只靠接触垫。机背两根安装轨各被一块固定导板（内侧导片 + 后挡板）和一个摆动夹具夹住，由它们承担防前倾和左右限位。
  - **留空区域**：机腹中间（电源托盘下放区）、前方和两侧都不放任何结构。
- **位置安排**：
  - 断电开关在右前立柱上，高 0.66…0.83 m。
  - 零件盘和磁性零件盒在左前托盘架上，高 0.785 m。
  - 两者都低于引擎底面（0.98 m），也在两臂外侧，不挡正面接近，也不挡引擎向两侧拆出。
- **视觉风格**：旧象牙白（立柱护板、零件盘）+ 工业灰（框架、开关盒），少量黄黑警示条（支腿端、底座前沿、托盘架前沿、开关铭牌、手柄握把），整体轻微旧化，没有发光展台效果。唯一的发光件是开关盒顶上的状态灯。

## 3. 对象、原点和旋转轴（供程序接碰撞体与动画）

根对象 `UNIT07_ServiceDock` 在地面原点。所有 Dock_* 对象都是它的子对象。**角度以 FBX 导入 Unity 后实测的方向为准**，见第 6 节。

| 对象 | 作用 | 原点（Blender 世界坐标） | 转动 / 属性 |
| --- | --- | --- | --- |
| `Dock_RobotAnchor`（空物体） | 七号模型根（V4 的 Root 骨骼 = 最低点）放在这里 | (0, 0, 0.72) | Unity 中为 (0, 0.72, 0)，机器人正面朝 Unity +Z |
| `Dock_ContactPad_L` / `_R` | 机身接触点（橡胶垫） | 接触面中心 (±0.157, 0.143, 0.913) | 接触法线为本地 +Z；贴住七号的 `Chassis_ArmHardpoint` 底面 |
| `Dock_Clamp_L` / `_R` | 左右限位夹具（摆动夹） | 铰轴 (±0.182, 0.214, 0.910) | 绕**本地 Y** 转。0° = 夹紧。Blender 中张开 = `open_deg`（L +30°，R −30°）。**Unity 中张开 = `unity_open_deg`（L −30°，R +30°，因为导入时 X 轴镜像）** |
| `Dock_Clamp_*_Grip`、`_JawPad` | 黄色握把、橡胶夹面（子对象，随夹具转动） | — | — |
| `Dock_PowerSwitch` | 断电开关盒（静止） | (0.44, −0.21, 0.66) | 子对象：铭牌、手柄、状态灯 |
| `Dock_PowerSwitch_Lever` | 断电开关手柄 | 手柄轴 (0.465, −0.27, 0.74) | 绕**本地 X** 转。+35° = 手柄向下 = **OFF**（默认，停靠维修状态）；−35° = 向上 = ON。Unity 中方向相同（`unity_off_deg` / `unity_on_deg`） |
| `Dock_PowerSwitch_LeverGrip` | 手柄握把（子对象） | — | — |
| `Dock_PowerSwitch_Lamp` | 状态灯（发光占位） | (0.475, −0.235, 0.82) | 由程序切换发光 |
| `Dock_PartsTray` | 可取下的零件盘 | 盘底中心 (−0.57, −0.19, 0.785) | 两端各有一个提手（横杆 20 mm），适合玩家或七号的夹爪抓取（夹爪全开 94.9 mm） |
| `Dock_MagneticBox` | 放旧轴承的磁性零件盒 | 盒底中心 (−0.3625, −0.22, 0.785) | 子对象：底部磁铁、标签 |
| 其余 Dock_*（底座、支腿、立柱、托架、鞍座、导轨座、导板、铰耳、立柱、托盘架、线管、警示条、调平脚） | 静止结构 | 各自中心或底面 | — |

以上属性以 FBX 自定义属性写入（`dock_role`、`rot_axis_local`、`open_deg` / `unity_open_deg`、`on_deg` / `off_deg` / `unity_on_deg` / `unity_off_deg`、`removable` 等）。在 Unity 中可以用 `AssetPostprocessor.OnPostprocessGameObjectWithUserProperties` 读取，测试副本里 11 组都读到了。

## 4. 空间检查（`clearance_report.json`，全部通过）

脚本用占位体和各类检查体做网格相交测试。相交的判定是：表面相交，或顶点落进检查体；检查体先内缩 0.5 mm，贴面接触不算穿插。

| 检查 | 结果 |
| --- | --- |
| 占位体（工作姿态 / 双臂收起姿态）与维修座不穿插，夹具处于夹紧位置 | 通过 |
| 接触垫顶面 = 硬点板底面 = 0.913 m | 通过（贴合） |
| 导板、夹具与安装轨的间隙 | 通过：都是 2 mm |
| 拆装通道：顶盖上取；前框上提；前框、玻璃、屏幕盒、主板托板前抽；电源托盘下放、再从两臂之间前取；背部维修盒后抽；左右引擎向两侧拔出 | 通过：夹具夹紧、张开两种状态都查了。最小间隙是电源托盘下放 9 mm、引擎侧拔 74 mm（夹具张开时） |
| 竖直落座：夹具张开，占位体从上方 12 cm 落到位 | 通过 |
| 夹具开合、开关手柄 ON ↔ OFF 的转动范围不碰占位体（包括机背螺栓）和开关盒 | 通过 |
| 手部接近区：两台引擎的正面与外侧、开关握把前方、零件盘和零件盒上方、两个夹具握把外侧 | 通过（这些区域既没有维修座结构，也没有占位体） |

- **局限**：
  - 占位体是长方体包络，比真实外形大，所以检查结果偏保守。
  - 手部接近区是按成年人手掌尺寸估的空间，**没有用 VR 设备或手部模型实测**。
  - 重心是按均匀密度粗估的。

## 5. 渲染（`Renders/`，Blender Cycles，OptiX，1400 × 1050）

图中蓝灰色的方块是七号**占位体**，不是七号的美术模型。

| 文件 | 内容 |
| --- | --- |
| `R01_front_docked_placeholder.png` | 正视：占位体停靠 |
| `R02_side_docked_placeholder.png` | 侧视：占位体停靠 |
| `R03_use_state_threequarter.png` | 使用状态：夹紧、开关 OFF、零件盘和磁性盒（内有旧轴承道具）在托盘架上 |
| `R04_front_empty.png` / `R05_side_empty.png` | 空维修座：正视 / 侧视 |
| `R06_rear_clamps_closed.png` / `R07_rear_clamps_open_empty.png` | 背面：夹具夹紧（有占位体）/ 张开（空座） |
| `R08_switch_off_closeup.png` / `R09_switch_on_closeup.png` | 断电开关：OFF / ON |
| `R10_tray_and_magnetic_box.png` | 零件盘与磁性零件盒 |
| `R11_mouse_view_front_high.png` | 前方鼠标视角（略高的正面视角） |

## 6. 导入 Unity

**测试副本中的导入检查**：在 Unity 6000.0.84f1 的测试副本 `BorderRepairStation_V4ImportTest` 中做了检查，没有导入正式项目。结果见 `unity_import_check.txt`，检查脚本复制在 `unity_import_check/Unit07DockImportCheck.cs`。
- 导入警告 0 条。48 个网格、8,692 个三角面、7 种材质，与 Blender 一致。
- 维修座尺寸 1.227 × 1.040 × 0.859 m。
- `Dock_RobotAnchor` 在 Unity 中为 (0, 0.72, 0)。
- 根对象带 −90° 的 X 轴旋转（270°）。这是 FBX 坐标换算的正常结果：Blender 的 −Y（正面）对应 Unity +Z，Blender 的 +X 对应 Unity −X。和之前导入 V4 模型时的换算一致：七号的左侧（`_L`）在 Unity 里位于 −X，与 `Dock_Clamp_L` 同侧。
- 转动方向：手柄绕本地 X 转，Unity 与 Blender 同号；夹具绕本地 Y 转，**Unity 中反号**。已经分别写成 `unity_*_deg` 属性。

**正式项目导入步骤（建议，未在正式项目中执行）**
1. 把 `Export/UNIT07_ServiceDock.fbx` 和 `Textures/` 复制到 `Assets/BorderRepair/Art/Unit07ServiceDock/`（或按项目约定的目录）。
2. 导入设置：Scale Factor 1，Convert Units 开启；Animation Type 选 None；Materials 选 Import via Material Description 或用下面第 3 步的材质重映射。
3. 材质：FBX 导出时去掉了贴图路径（`path_mode=STRIP`），需要按 `materials.json` 新建 7 个 URP/Lit 材质。
   - 底色贴图的颜色空间：`T_Dock_Grime`、`T_Dock_Warning`、`T_Dock_Labels` 都用 sRGB。
   - `M_Dock_Ivory` / `Gray` / `Steel` 用 `T_Dock_Grime` 乘以底色。
   - `M_Dock_Lamp` 开启 Emission。
4. 七号模型放到 `Dock_RobotAnchor` 的位置，朝向与维修座一致：机器人正面 = Unity +Z。
5. 碰撞体：模型里**没有碰撞体**。建议给以下对象加：
   - 接触垫：Box；
   - 夹具夹面：Box；
   - 开关握把、零件盘提手、零件盒：小的 Box 或 Capsule，作为点击 / 抓取代理。
   - 不要给整个底座加一个大 BoxCollider，会挡住电源托盘下放区和手部接近区。
6. 动画：没有动画片段。建议用程序直接设置 `localRotation`：
   - 夹具：0° ↔ `unity_open_deg`，绕本地 Y；
   - 手柄：`unity_off_deg` ↔ `unity_on_deg`，绕本地 X；
   - 状态灯：切换发光；
   - 断电后让七号的 `Idle_Hover` 停止播放，转子即静止。
   - 建议的停靠顺序：夹具张开 → 七号悬停到锚点上方 → 下落到接触垫 → 夹具夹紧 → 开关 OFF → 涡轮停转。

## 7. 重新生成

```bat
ArtSource\Unit07ServiceDock\run_blender.bat            :: 生成 + 渲染（约 3–4 分钟，RTX 3070 / OptiX）
ArtSource\Unit07ServiceDock\run_blender.bat norender   :: 只生成 .blend / FBX / 贴图 / 统计 / 检查
```

Blender 路径写在 `run_blender.bat` 里（`D:\steam\steamapps\common\Blender\blender.exe`），换机器时要改成本机路径。

## 8. 未完成与已知限制

- 没有碰撞体，没有动画片段，也没有接入任何工单或流程代码。
- 没有在正式 Unity 项目中导入。
- 手部接近区没有用 VR 设备实测，占位体也不是七号的真实外形。七号真实模型的细小凸起（除已补入的机背螺栓和通风口外）没有全部列进占位体。
- 共用磨损贴图在大平面上仍能看出平铺；如果需要更自然，可以改成烘焙 AO 加边缘磨损。
