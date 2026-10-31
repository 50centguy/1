#ifndef BORDER_REPAIR_CLINIC_SHELL_INPUT_INCLUDED
#define BORDER_REPAIR_CLINIC_SHELL_INPUT_INCLUDED

// 诊所外壳（地面 / 墙面 / 天花板）共用输入。
// 所有 pass 共用同一个 UnityPerMaterial 常量缓冲（SRP Batcher 要求：每个 pass 布局一致）。
// _BaseMap / _BumpMap / _EmissionMap 及采样器由 URP 的 SurfaceInput.hlsl 声明，
// 这样 ShadowCaster / DepthOnly / DepthNormals 可以直接复用 URP 自带 pass（与 WornSurface 相同做法）。
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

CBUFFER_START(UnityPerMaterial)
    // URP 自带 pass 需要的
    float4 _BaseMap_ST;
    half4  _BaseColor;
    half   _Cutoff;
    half   _BumpScale;

    // 公共
    float  _ShellKind;          // 0 地面 / 1 墙面 / 2 天花板
    float4 _RoomCenter;         // xyz：房间中心（y = 地面高度），世界坐标
    float  _RoomApothem;        // 八边形内壁边心距（中心 -> 墙内表面），米
    float  _Seed;

    // 地面：旧环氧涂层混凝土
    half4  _FloorColor;
    half4  _FloorNewColor;
    float  _FloorSlabSize;
    float  _FloorJointWidth;
    half   _FloorJointDark;
    half   _FloorSlabJitter;
    half   _FloorHueJitter;
    half   _FloorNewRatio;
    half   _FloorTiltDeg;
    half   _FloorDetail;
    half   _FloorWearScale;
    half   _FloorWearLight;
    half   _FloorStainScale;
    half   _FloorStainDark;
    half   _FloorStainDark2;
    half   _FloorFootDark;
    half   _FloorFootDark2;
    half   _FloorSmoothness;
    half   _FloorNewSmoothness;

    // 墙面：两道色 + 腰线
    half4  _WallLowerColor;
    half4  _WallUpperColor;
    half4  _WallStripeColor;
    half4  _WallPrimerColor;
    half4  _WallScratchColor;
    float  _WallPanelWidth;
    float  _WallPanelHeight;
    float  _WallSplitHeight;
    float  _WallStripeWidth;
    half   _WallPanelJitter;
    half   _WallPrimerRatio;
    half   _WallRepaintRatio;
    half   _WallRepaintShift;
    half   _WallBulgeMinDeg;
    half   _WallBulgeMaxDeg;
    half   _WallScratchAmount;
    half   _WallFootDark;
    half   _WallFootDark2;
    half   _WallDustDark;
    half   _WallDustDark2;
    half   _WallLeakRatio;
    half   _WallLeakDark;
    half   _WallRustChroma;
    half   _WallDetail;
    half   _WallLowerSmoothness;
    half   _WallUpperSmoothness;

    // 天花板
    half4  _CeilColor;
    half   _CeilChroma;
    half   _CeilDetail;
    half   _CeilPanelJitter;
    half   _CeilSmokeScaleA;
    half   _CeilSmokeScaleB;
    half   _CeilSmokeDark;
    half   _CeilSmokeDark2;
    half   _CeilSmoothness;
CBUFFER_END

#endif
