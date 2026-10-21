#ifndef BORDER_REPAIR_DIRT_SURFACE_INCLUDED
#define BORDER_REPAIR_DIRT_SURFACE_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor, _EmissionColor, _WearColor, _GrimeColor, _DirtColor;
    float4 _WearMask_ST;
    half _Cutoff, _BumpScale, _Metallic, _Roughness, _UseNormal, _UseMetallicMap;
    half _WearAmount, _WearSoftness, _WearMetallic, _WearRoughness, _GrimeAmount;
    half _DirtAmount, _DirtSmoothness;
CBUFFER_END
TEXTURE2D(_CleaningMask); SAMPLER(sampler_CleaningMask);
TEXTURE2D(_WearMask); SAMPLER(sampler_WearMask);
TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
#endif
