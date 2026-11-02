// Erasable dirt drawn over a surface that keeps its own material: CleanableSurface's overlay form.
// Alpha comes only from the cleaning mask, so a wiped texel leaves the part exactly as it looks without this pass.
Shader "BorderRepair/DirtOverlay"
{
    Properties
    {
        _CleaningMask("Erasable dirt mask (unique UV1)", 2D) = "black" {}
        _DirtColor("Dirt color", Color) = (.32,.215,.105,1)
        _DirtAmount("Dirt opacity", Range(0,1)) = .95
        _DirtSmoothness("Dirt smoothness", Range(0,1)) = .07
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-1" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back ZWrite Off ZTest LEqual
            Offset -1, -1
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OverlayVert
            #pragma fragment OverlayFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _DirtColor;
                half _DirtAmount, _DirtSmoothness;
            CBUFFER_END
            TEXTURE2D(_CleaningMask); SAMPLER(sampler_CleaningMask);
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 cleanUV:TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1;
                float2 cleanUV:TEXCOORD2; half fog:TEXCOORD3;
            };
            Varyings OverlayVert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.cleanUV = i.cleanUV; o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 OverlayFrag(Varyings i):SV_Target
            {
                half alpha = saturate(SAMPLE_TEXTURE2D(_CleaningMask, sampler_CleaningMask, i.cleanUV).r * _DirtAmount);
                if (alpha < .004h) discard;
                SurfaceData s = (SurfaceData)0;
                s.albedo = _DirtColor.rgb; s.metallic = 0; s.smoothness = _DirtSmoothness;
                s.normalTS = half3(0,0,1); s.occlusion = 1; s.alpha = 1;
                half3 normal = NormalizeNormalPerPixel(i.normalWS);
                InputData d = (InputData)0;
                d.positionWS = i.positionWS; d.positionCS = i.positionCS; d.normalWS = normal;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS); d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fog; d.vertexLighting = VertexLighting(i.positionWS, normal); d.bakedGI = SampleSH(normal);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS); d.shadowMask = half4(1,1,1,1);
                half4 color = UniversalFragmentPBR(d, s);
                color.rgb = MixFog(color.rgb, i.fog); color.a = alpha;
                return color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
