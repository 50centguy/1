# 连接器几何核对（Blender 只读导入 robot-final.fbx）

- FBX：`C:\Users\Administrator\UnityProjects\BorderRepairStation_replkit\Assets\RobotV4\Model\robot-final.fbx`（未修改）。坐标为 Blender 世界（米，Z 上）；物体本地 X = 审计记录的拔出轴（PlugBody 记为 +X，PlugEngine 记为 −X）。

## ExternalCable_L_PlugBody

- 类型 MESH，父对象 Robot_Rig / 骨骼 Body，240 顶点，472 三角面，材质 [{'name': 'M_Engine', 'image': 'T_Engine_BaseColor.png', 'image_size': [2048, 2048]}]

| 岛 | 三角面 | 本地尺寸 mm (X,Y,Z) | 本地范围 mm | 沿本地 X 十段截面 Y×Z mm | 贴图取色 |
|---|---|---|---|---|---|
| 0 | 284 | [3.2, 31.4, 31.4] | [[1.6, -15.7, -15.7], [4.9, 15.7, 15.7]] | [[29.0, 19.1], [30.7, 29.3], [26.6, 7.7], [27.2, 7.9], [31.4, 19.8], [31.4, 15.7], [30.3, 11.6], [21.7, 4.5], [30.7, 29.3], [29.0, 25.6]] | [0.308, 0.308, 0.308] |
| 1 | 188 | [25.5, 16.4, 16.4] | [[-11.3, -8.2, -8.2], [14.1, 8.2, 8.2]] | [[16.4, 16.4], None, None, None, None, None, None, None, None, [16.4, 16.4]] | [0.315, 0.315, 0.315] |

## ExternalCable_L_PlugEngine

- 类型 MESH，父对象 Robot_Rig / 骨骼 Engine_L_Hinge，96 顶点，188 三角面，材质 [{'name': 'M_Engine', 'image': 'T_Engine_BaseColor.png', 'image_size': [2048, 2048]}]

| 岛 | 三角面 | 本地尺寸 mm (X,Y,Z) | 本地范围 mm | 沿本地 X 十段截面 Y×Z mm | 贴图取色 |
|---|---|---|---|---|---|
| 0 | 188 | [24.0, 16.4, 16.4] | [[-16.0, -8.2, -8.2], [8.0, 8.2, 8.2]] | [[16.4, 16.4], None, None, None, None, None, None, None, None, [16.4, 16.4]] | [0.314, 0.314, 0.314] |

## AuxCable_L_PlugBody

- 类型 MESH，父对象 Robot_Rig / 骨骼 Body，240 顶点，472 三角面，材质 [{'name': 'M_Engine', 'image': 'T_Engine_BaseColor.png', 'image_size': [2048, 2048]}]

| 岛 | 三角面 | 本地尺寸 mm (X,Y,Z) | 本地范围 mm | 沿本地 X 十段截面 Y×Z mm | 贴图取色 |
|---|---|---|---|---|---|
| 0 | 284 | [3.1, 28.2, 28.2] | [[2.0, -14.1, -14.1], [5.1, 14.1, 14.1]] | [[25.8, 25.8], [27.5, 23.5], [13.7, 1.8], [24.4, 7.1], [28.2, 17.7], [28.2, 14.1], [27.2, 10.5], None, [27.5, 27.5], [25.8, 25.8]] | [0.308, 0.308, 0.308] |
| 1 | 188 | [25.3, 13.2, 13.2] | [[-11.0, -6.6, -6.6], [14.2, 6.6, 6.6]] | [[13.2, 13.2], None, None, None, None, None, None, None, None, [13.2, 13.2]] | [0.315, 0.315, 0.315] |

## AuxCable_L_PlugEngine

- 类型 MESH，父对象 Robot_Rig / 骨骼 Engine_L_Hinge，96 顶点，188 三角面，材质 [{'name': 'M_Engine', 'image': 'T_Engine_BaseColor.png', 'image_size': [2048, 2048]}]

| 岛 | 三角面 | 本地尺寸 mm (X,Y,Z) | 本地范围 mm | 沿本地 X 十段截面 Y×Z mm | 贴图取色 |
|---|---|---|---|---|---|
| 0 | 188 | [24.0, 13.2, 13.2] | [[-16.0, -6.6, -6.6], [8.0, 6.6, 6.6]] | [[13.2, 13.2], None, None, None, None, None, None, None, None, [13.2, 13.2]] | [0.314, 0.314, 0.314] |

## Harness_Engine_L_PlugA

- 类型 MESH，父对象 Robot_Rig / 骨骼 Body，56 顶点，108 三角面，材质 [{'name': 'M_Internal', 'image': 'T_Internal_BaseColor.png', 'image_size': [2048, 2048]}]

| 岛 | 三角面 | 本地尺寸 mm (X,Y,Z) | 本地范围 mm | 沿本地 X 十段截面 Y×Z mm | 贴图取色 |
|---|---|---|---|---|---|
| 0 | 108 | [12.2, 12.0, 16.0] | [[-5.3, -6.0, -2.0], [6.9, 6.0, 14.0]] | [[12.0, 15.6], [12.0, 1.2], None, None, None, None, None, None, [12.0, 1.2], [12.0, 15.6]] | [0.0, 0.0, 0.0] |

## Harness_Engine_L_PlugB

- 类型 MESH，父对象 Robot_Rig / 骨骼 Body，56 顶点，108 三角面，材质 [{'name': 'M_Internal', 'image': 'T_Internal_BaseColor.png', 'image_size': [2048, 2048]}]

| 岛 | 三角面 | 本地尺寸 mm (X,Y,Z) | 本地范围 mm | 沿本地 X 十段截面 Y×Z mm | 贴图取色 |
|---|---|---|---|---|---|
| 0 | 108 | [16.3, 12.0, 12.0] | [[-12.4, -6.0, -6.0], [3.8, 6.0, 6.0]] | [[12.0, 12.0], None, None, None, None, None, None, None, None, [12.0, 12.0]] | [0.0, 0.0, 0.0] |

## ExternalCable_L

- 类型 MESH，父对象 Robot_Rig / 骨骼 ，204 顶点，404 三角面，材质 [{'name': 'M_Engine', 'image': 'T_Engine_BaseColor.png', 'image_size': [2048, 2048]}]

| 岛 | 三角面 | 本地尺寸 mm (X,Y,Z) | 本地范围 mm | 沿本地 X 十段截面 Y×Z mm | 贴图取色 |
|---|---|---|---|---|---|
| 0 | 404 | [64.2, 10.4, 54.7] | [[-3.5, -5.2, -53.2], [60.6, 5.2, 1.5]] | [[10.4, 12.9], [10.4, 33.0], [10.4, 25.3], [10.4, 15.5], [10.4, 12.1], [10.4, 14.0], [10.4, 21.9], [10.4, 34.8], [10.4, 33.0], [10.4, 12.9]] | [0.0, 0.0, 0.0] |

## AuxCable_L

- 类型 MESH，父对象 Robot_Rig / 骨骼 ，204 顶点，404 三角面，材质 [{'name': 'M_Engine', 'image': 'T_Engine_BaseColor.png', 'image_size': [2048, 2048]}]

| 岛 | 三角面 | 本地尺寸 mm (X,Y,Z) | 本地范围 mm | 沿本地 X 十段截面 Y×Z mm | 贴图取色 |
|---|---|---|---|---|---|
| 0 | 404 | [54.1, 7.2, 35.0] | [[-1.8, -3.6, -33.6], [52.3, 3.6, 1.4]] | [[7.2, 9.3], [7.2, 21.7], [7.2, 17.0], [7.2, 11.0], [7.2, 8.5], [7.2, 7.9], [7.2, 11.0], [7.2, 17.0], [7.2, 21.7], [7.2, 9.3]] | [0.0, 0.0, 0.0] |

## ExternalCable_L 两端

- 端到端 58.3 mm；顶点组 ['Body', 'Engine_L_Hinge']；整根贴图取色 [0.0, 0.0, 0.0]

- Body 端：权重最高 1.0 的一圈 12 个顶点，端面半径 7.01 mm，中心 [0.22654, -0.068, 0.34933]；最近：[{'plug': 'ExternalCable_L_PlugBody', 'island': 1, 'dist_mm': 2.47}, {'plug': 'ExternalCable_L_PlugBody', 'island': 0, 'dist_mm': 6.93}, {'plug': 'Harness_Engine_L_PlugB', 'island': 0, 'dist_mm': 20.85}]
- Engine_L_Hinge 端：权重最高 1.0 的一圈 10 个顶点，端面半径 6.51 mm，中心 [0.28482, -0.068, 0.3506]；最近：[{'plug': 'ExternalCable_L_PlugEngine', 'island': 0, 'dist_mm': 2.51}, {'plug': 'ExternalCable_L_PlugBody', 'island': 1, 'dist_mm': 55.8}, {'plug': 'ExternalCable_L_PlugBody', 'island': 0, 'dist_mm': 65.19}]

## AuxCable_L 两端

- 端到端 50.1 mm；顶点组 ['Body', 'Engine_L_Hinge']；整根贴图取色 [0.0, 0.0, 0.0]

- Body 端：权重最高 1.0 的一圈 10 个顶点，端面半径 4.31 mm，中心 [0.22714, 0.055, 0.37194]；最近：[{'plug': 'AuxCable_L_PlugBody', 'island': 1, 'dist_mm': 2.05}, {'plug': 'AuxCable_L_PlugBody', 'island': 0, 'dist_mm': 7.25}, {'plug': 'AuxCable_L_PlugEngine', 'island': 0, 'dist_mm': 44.85}]
- Engine_L_Hinge 端：权重最高 1.0 的一圈 10 个顶点，端面半径 4.35 mm，中心 [0.2772, 0.055, 0.37194]；最近：[{'plug': 'AuxCable_L_PlugEngine', 'island': 0, 'dist_mm': 5.08}, {'plug': 'AuxCable_L_PlugBody', 'island': 1, 'dist_mm': 48.0}, {'plug': 'AuxCable_L_PlugBody', 'island': 0, 'dist_mm': 57.3}]
