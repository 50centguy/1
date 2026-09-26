#ifndef BORDER_REPAIR_WORN_SURFACE_INPUT_INCLUDED
#define BORDER_REPAIR_WORN_SURFACE_INPUT_INCLUDED

// 所有 pass 共用同一个 UnityPerMaterial 常量缓冲（SRP Batcher 要求）。
// _BaseMap / _BumpMap / _EmissionMap 及其采样器由 URP 的 SurfaceInput.hlsl 声明，
// 这样可以直接复用 URP 自带的 ShadowCaster / DepthOnly / DepthNormals pass。
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Cutoff;
    half _Metallic;
    half _Roughness;
    float4 _WearMask_ST;
    half _WearAmount;
    half _WearSoftness;
    half4 _WearColor;
    half _WearMetallic;
    half _WearRoughness;
    half _GrimeAmount;
    half4 _GrimeColor;
    half4 _EmissionColor;
    half _BumpScale;
CBUFFER_END

TEXTURE2D(_WearMask);
SAMPLER(sampler_WearMask);

#endif
