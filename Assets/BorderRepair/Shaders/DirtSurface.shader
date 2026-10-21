Shader "BorderRepair/DirtSurface"
{
    Properties
    {
        [MainTexture] _BaseMap("Clean base texture", 2D) = "white" {}
        [MainColor] _BaseColor("Clean base color", Color) = (1,1,1,1)
        _Metallic("Base metallic", Range(0,1)) = 0
        _Roughness("Base roughness", Range(.02,1)) = .65
        _BumpMap("Base normal", 2D) = "bump" {}
        _BumpScale("Normal strength", Float) = 1
        _UseNormal("Use normal", Float) = 0
        _MetallicGlossMap("Metallic map", 2D) = "white" {}
        _UseMetallicMap("Use metallic map", Float) = 0
        _EmissionMap("Emission map", 2D) = "white" {}
        [HDR] _EmissionColor("Emission", Color) = (0,0,0,1)
        _WearMask("Permanent wear (R wear / G grime)", 2D) = "black" {}
        _WearAmount("Permanent wear amount", Range(0,1)) = 0
        _WearSoftness("Wear edge softness", Range(.005,.3)) = .05
        _WearColor("Wear color", Color) = (.62,.64,.66,1)
        _WearMetallic("Wear metallic", Range(0,1)) = 1
        _WearRoughness("Wear roughness", Range(.02,1)) = .3
        _GrimeAmount("Baked-in permanent grime", Range(0,1)) = 0
        _GrimeColor("Permanent grime tint", Color) = (.55,.5,.42,1)
        _CleaningMask("Erasable dirt mask (unique UV1)", 2D) = "black" {}
        _DirtColor("Erasable dirt color", Color) = (.32,.215,.105,1)
        _DirtAmount("Dirt opacity", Range(0,1)) = .95
        _DirtSmoothness("Dirt smoothness", Range(0,1)) = .07
        [HideInInspector] _Cutoff("Cutoff", Range(0,1)) = .5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" "UniversalMaterialType"="Lit" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DirtVert
            #pragma fragment DirtFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "DirtSurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes
            {
                float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; float2 cleanUV:TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 positionWS:TEXCOORD1;
                half3 normalWS:TEXCOORD2; half3 tangentWS:TEXCOORD3; half3 bitangentWS:TEXCOORD4; half fog:TEXCOORD5;
                float2 cleanUV:TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings DirtVert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz);
                VertexNormalInputs n=GetVertexNormalInputs(i.normalOS,i.tangentOS);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS; o.uv=i.uv; o.cleanUV=i.cleanUV;
                o.normalWS=n.normalWS; o.tangentWS=n.tangentWS; o.bitangentWS=n.bitangentWS;
                o.fog=ComputeFogFactor(p.positionCS.z); return o;
            }
            half4 DirtFrag(Varyings i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv=TRANSFORM_TEX(i.uv,_BaseMap);
                half3 baseColor=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).rgb*_BaseColor.rgb;
                half4 wearMap=SAMPLE_TEXTURE2D(_WearMask,sampler_WearMask,TRANSFORM_TEX(i.uv,_WearMask));
                half wear=smoothstep(1-_WearAmount-_WearSoftness,1-_WearAmount+_WearSoftness,wearMap.r)*step(.001h,_WearAmount);
                half permanentGrime=saturate(wearMap.g*_GrimeAmount);
                baseColor=lerp(baseColor,_WearColor.rgb,wear);
                baseColor=lerp(baseColor,baseColor*_GrimeColor.rgb,permanentGrime);
                half dirt=saturate(SAMPLE_TEXTURE2D(_CleaningMask,sampler_CleaningMask,i.cleanUV).r*_DirtAmount);
                half metallic=_Metallic;
                if (_UseMetallicMap>.5h) metallic*=SAMPLE_TEXTURE2D(_MetallicGlossMap,sampler_MetallicGlossMap,uv).r;
                metallic=lerp(metallic,_WearMetallic,wear);
                half roughness=saturate(lerp(_Roughness,_WearRoughness,wear)+permanentGrime*.25h);
                SurfaceData s=(SurfaceData)0;
                s.albedo=lerp(baseColor,_DirtColor.rgb,dirt);
                s.metallic=lerp(metallic,0,dirt); s.smoothness=lerp(1-roughness,_DirtSmoothness,dirt);
                s.normalTS=half3(0,0,1); s.occlusion=1; s.alpha=1;
                s.emission=SAMPLE_TEXTURE2D(_EmissionMap,sampler_EmissionMap,uv).rgb*_EmissionColor.rgb*(1-dirt);
                half3 normal=NormalizeNormalPerPixel(i.normalWS);
                if (_UseNormal>.5h)
                {
                    half3 n=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv),_BumpScale);
                    normal=NormalizeNormalPerPixel(TransformTangentToWorld(n,half3x3(i.tangentWS,i.bitangentWS,i.normalWS)));
                }
                InputData d=(InputData)0;
                d.positionWS=i.positionWS; d.positionCS=i.positionCS; d.normalWS=normal;
                d.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS); d.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord=i.fog; d.vertexLighting=VertexLighting(i.positionWS,normal); d.bakedGI=SampleSH(normal);
                d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS); d.shadowMask=half4(1,1,1,1);
                half4 color=UniversalFragmentPBR(d,s); color.rgb=MixFog(color.rgb,i.fog); color.a=1; return color;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "DirtSurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "DirtSurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #include "DirtSurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
