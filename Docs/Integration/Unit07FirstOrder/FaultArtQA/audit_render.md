# 渲染设置核查（质量档 PC）
- URP 资源 `Assets/Settings/PC_RPAsset.asset`：MSAA 1x，HDR True，渲染缩放 1，主光阴影 True，附加光阴影 True，阴影距离 50，附加光 PerPixel 上限 4

## 场景 `Assets/BorderRepair/FirstOrder/Scenes/LayoutAB/Unit07FirstOrder_LayoutB.unity`
- 环境光 Trilight：天 RGBA(0.190, 0.210, 0.200, 1.000) 中 RGBA(0.140, 0.150, 0.140, 1.000) 地 RGBA(0.070, 0.070, 0.065, 1.000)；环境反射来源 Skybox，强度 1，自定义反射 无
- 灯 `Room_Fill (cool, no shadow)`：Directional 强度 0.15 颜色 RGBA(0.720, 0.800, 0.780, 1.000) 范围 10 角 30/21.80208 阴影 None 位置 (0.00, 0.00, 0.00)
- 灯 `Ceiling_Fluo (cool)`：Point 强度 0.85 颜色 RGBA(0.800, 0.900, 0.860, 1.000) 范围 3.4 角 30/21.80208 阴影 None 位置 (0.00, 2.30, -0.25)
- 灯 `Bench_WorkLamp_Spot (warm)`：Spot 强度 2.3 颜色 RGBA(1.000, 0.740, 0.460, 1.000) 范围 1.7 角 82/34 阴影 Soft 位置 (0.11, 1.41, -0.54)
- 灯 `Dock_ServiceLight (warm-neutral)`：Spot 强度 3 颜色 RGBA(1.000, 0.860, 0.700, 1.000) 范围 2.6 角 70/35 阴影 Soft 位置 (-0.10, 2.30, 0.59)
- 反射探针：0 个
- 后处理 `PostProcess (shared WB profile)`（Assets/WorkbenchArea/Art/WB_PostProfile.asset）：
  - Tonemapping：mode=Neutral，neutralHDRRangeReductionMode=BT2390，acesPreset=ACES1000Nits，hueShiftAmount=0，detectPaperWhite=False，paperWhite=300，detectBrightnessLimits=True，minNits=0.005，maxNits=1000
  - ColorAdjustments：postExposure=0.25，contrast=12，colorFilter=RGBA(1.000, 1.000, 1.000, 1.000)，hueShift=0，saturation=4
  - Vignette：color=RGBA(0.020, 0.020, 0.015, 1.000)，center=(0.50, 0.50)，intensity=0.22，smoothness=0.45，rounded=False
- 主相机：后处理 True，抗锯齿 None（High），allowMSAA True，HDR True
- 渲染器 `UNIT07_FK_IntakeClog_L_Fibers` → 材质 `Assets/BorderRepair/Art/Unit07FaultKit/Materials/M_FK_IntakeClog.mat`（Universal Render Pipeline/Lit）关键字 [_NORMALMAP]
 _BaseColor=RGBA(1.000, 1.000, 1.000, 1.000) _Metallic=0 _Smoothness=0.05 _SmoothnessTextureChannel=0 _BumpScale=1 _OcclusionStrength=1 _SpecularHighlights=1 _EnvironmentReflections=1 _Surface=0 _AlphaClip=0 _Cull=2
    _BaseMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_IntakeClog_BaseColor.png`
    _BumpMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_IntakeClog_Normal.png`
- 渲染器 `UNIT07_FK_IntakeClog_L_GuardDust` → 材质 `Assets/BorderRepair/Art/Unit07FaultKit/Materials/M_FK_IntakeClog.mat`（Universal Render Pipeline/Lit）关键字 [_NORMALMAP]
 _BaseColor=RGBA(1.000, 1.000, 1.000, 1.000) _Metallic=0 _Smoothness=0.05 _SmoothnessTextureChannel=0 _BumpScale=1 _OcclusionStrength=1 _SpecularHighlights=1 _EnvironmentReflections=1 _Surface=0 _AlphaClip=0 _Cull=2
    _BaseMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_IntakeClog_BaseColor.png`
    _BumpMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_IntakeClog_Normal.png`
- 渲染器 `UNIT07_FK_IntakeClog_L_RimDust` → 材质 `Assets/BorderRepair/Art/Unit07FaultKit/Materials/M_FK_IntakeClog.mat`（Universal Render Pipeline/Lit）关键字 [_NORMALMAP]
 _BaseColor=RGBA(1.000, 1.000, 1.000, 1.000) _Metallic=0 _Smoothness=0.05 _SmoothnessTextureChannel=0 _BumpScale=1 _OcclusionStrength=1 _SpecularHighlights=1 _EnvironmentReflections=1 _Surface=0 _AlphaClip=0 _Cull=2
    _BaseMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_IntakeClog_BaseColor.png`
    _BumpMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_IntakeClog_Normal.png`
- 渲染器 `UNIT07_FK_IntakeClog` → 材质 `Assets/BorderRepair/Art/Unit07FaultKit/Materials/M_FK_IntakeClog.mat`（Universal Render Pipeline/Lit）关键字 [_NORMALMAP]
 _BaseColor=RGBA(1.000, 1.000, 1.000, 1.000) _Metallic=0 _Smoothness=0.05 _SmoothnessTextureChannel=0 _BumpScale=1 _OcclusionStrength=1 _SpecularHighlights=1 _EnvironmentReflections=1 _Surface=0 _AlphaClip=0 _Cull=2
    _BaseMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_IntakeClog_BaseColor.png`
    _BumpMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_IntakeClog_Normal.png`
- 渲染器 `UNIT07_FK_BearingWorn` → 材质 `Assets/BorderRepair/Art/Unit07FaultKit/Materials/M_FK_BearingWorn.mat`（Universal Render Pipeline/Lit）关键字 [_METALLICSPECGLOSSMAP _NORMALMAP]
 _BaseColor=RGBA(1.000, 1.000, 1.000, 1.000) _Metallic=0 _Smoothness=1 _SmoothnessTextureChannel=0 _BumpScale=1 _OcclusionStrength=1 _SpecularHighlights=1 _EnvironmentReflections=1 _Surface=0 _AlphaClip=0 _Cull=2
    _BaseMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_BearingWorn_BaseColor.png`
    _MetallicGlossMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_BearingWorn_MetallicSmoothness.png`
    _BumpMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_BearingWorn_Normal.png`
- 渲染器 `UNIT07_FK_BearingTop_L_Worn_Chips` → 材质 `Assets/BorderRepair/Art/Unit07FaultKit/Materials/M_FK_MetalChips.mat`（Universal Render Pipeline/Lit）关键字 []
 _BaseColor=RGBA(0.851, 0.843, 0.816, 1.000) _Metallic=1 _Smoothness=0.78 _SmoothnessTextureChannel=0 _BumpScale=1 _OcclusionStrength=1 _SpecularHighlights=1 _EnvironmentReflections=1 _Surface=0 _AlphaClip=0 _Cull=2
- 渲染器 `UNIT07_FK_BearingNew` → 材质 `Assets/BorderRepair/Art/Unit07FaultKit/Materials/M_FK_BearingNew.mat`（Universal Render Pipeline/Lit）关键字 [_METALLICSPECGLOSSMAP _NORMALMAP]
 _BaseColor=RGBA(1.000, 1.000, 1.000, 1.000) _Metallic=0 _Smoothness=1 _SmoothnessTextureChannel=0 _BumpScale=1 _OcclusionStrength=1 _SpecularHighlights=1 _EnvironmentReflections=1 _Surface=0 _AlphaClip=0 _Cull=2
    _BaseMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_BearingNew_BaseColor.png`
    _MetallicGlossMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_BearingNew_MetallicSmoothness.png`
    _BumpMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_BearingNew_Normal.png`
- 渲染器 `UNIT07_FK_CoverLabel` → 材质 `Assets/BorderRepair/Art/Unit07FaultKit/Materials/M_FK_CoverLabel.mat`（Universal Render Pipeline/Lit）关键字 []
 _BaseColor=RGBA(1.000, 1.000, 1.000, 1.000) _Metallic=0 _Smoothness=0.22 _SmoothnessTextureChannel=0 _BumpScale=1 _OcclusionStrength=1 _SpecularHighlights=1 _EnvironmentReflections=1 _Surface=0 _AlphaClip=0 _Cull=2
    _BaseMap = `Assets/BorderRepair/Art/Unit07FaultKit/Textures/T_FK_CoverLabel_BaseColor.png`
- 渲染器 `Engine_UpperCover_L` → 材质 `Assets/RobotV4/Materials/M_Engine.mat`（Universal Render Pipeline/Lit）关键字 [_METALLICSPECGLOSSMAP _OCCLUSIONMAP]
 _BaseColor=RGBA(1.000, 1.000, 1.000, 1.000) _Metallic=1 _Smoothness=1 _SmoothnessTextureChannel=0 _BumpScale=1 _OcclusionStrength=1 _SpecularHighlights=1 _EnvironmentReflections=1 _Surface=0 _AlphaClip=0 _Cull=2
    _BaseMap = `Assets/RobotV4/Model/textures/T_Engine_BaseColor.png`
    _MetallicGlossMap = `Assets/RobotV4/Model/textures/T_Engine_MetallicSmoothness.png`
    _OcclusionMap = `Assets/RobotV4/Model/textures/T_Engine_AO.png`
- 渲染器 `Engine_BearingTop_L` → 材质 `Assets/RobotV4/Materials/M_Internal.mat`（Universal Render Pipeline/Lit）关键字 [_METALLICSPECGLOSSMAP _OCCLUSIONMAP]
 _BaseColor=RGBA(1.000, 1.000, 1.000, 1.000) _Metallic=1 _Smoothness=1 _SmoothnessTextureChannel=0 _BumpScale=1 _OcclusionStrength=1 _SpecularHighlights=1 _EnvironmentReflections=1 _Surface=0 _AlphaClip=0 _Cull=2
    _BaseMap = `Assets/RobotV4/Model/textures/T_Internal_BaseColor.png`
    _MetallicGlossMap = `Assets/RobotV4/Model/textures/T_Internal_MetallicSmoothness.png`
    _OcclusionMap = `Assets/RobotV4/Model/textures/T_Internal_AO.png`
