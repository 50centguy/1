// 旧医疗设备 / 义体表面：底色、金属度、粗糙度、磨损遮罩（R = 磨穿露出的底层，G = 污渍）、少量自发光。
// 光照走 URP 的 UniversalFragmentPBR：主光阴影（含级联/软阴影）、附加光、Forward+、SSAO、雾。
// ShadowCaster / DepthOnly / DepthNormals 复用 URP 自带 pass，保证投射阴影与深度预渲染正常。
Shader "BorderRepair/WornSurface"
{
    Properties
    {
        [MainTexture] _BaseMap("底色贴图", 2D) = "white" {}
        [MainColor] _BaseColor("底色", Color) = (0.8, 0.8, 0.8, 1)
        _Metallic("金属度", Range(0, 1)) = 0
        _Roughness("粗糙度", Range(0.02, 1)) = 0.5

        [Header(Wear)]
        _WearMask("磨损遮罩 (R 磨穿, G 污渍)", 2D) = "black" {}
        _WearAmount("磨损程度", Range(0, 1)) = 0.3
        _WearSoftness("磨损边缘柔和度", Range(0.005, 0.3)) = 0.05
        _WearColor("磨穿后露出的颜色", Color) = (0.62, 0.64, 0.66, 1)
        _WearMetallic("磨穿处金属度", Range(0, 1)) = 1
        _WearRoughness("磨穿处粗糙度", Range(0.02, 1)) = 0.3
        _GrimeAmount("污渍强度", Range(0, 1)) = 0.3
        _GrimeColor("污渍颜色（相乘）", Color) = (0.55, 0.5, 0.42, 1)

        [Header(Emission)]
        [Toggle(_EMISSION)] _UseEmission("启用自发光", Float) = 0
        _EmissionMap("自发光贴图", 2D) = "white" {}
        [HDR] _EmissionColor("自发光颜色", Color) = (0, 0, 0, 1)

        // URP 自带 pass 需要的属性（本 shader 不做透明裁切）
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [HideInInspector] _BumpScale("Normal Scale", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "UniversalMaterialType" = "Lit" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WornVert
            #pragma fragment WornFrag

            #pragma shader_feature_local_fragment _EMISSION

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "WornSurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings WornVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = nrm.normalWS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 WornFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uvBase = TRANSFORM_TEX(input.uv, _BaseMap);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvBase).rgb * _BaseColor.rgb;

                // 磨损：R 通道超过阈值的地方露出底层（颜色、金属度、粗糙度一起切换）；G 通道为污渍
                half4 mask = SAMPLE_TEXTURE2D(_WearMask, sampler_WearMask, TRANSFORM_TEX(input.uv, _WearMask));
                half threshold = 1.0h - _WearAmount;
                half wear = smoothstep(threshold - _WearSoftness, threshold + _WearSoftness, mask.r) * step(0.001h, _WearAmount);
                half grime = saturate(mask.g * _GrimeAmount);

                albedo = lerp(albedo, _WearColor.rgb, wear);
                albedo = lerp(albedo, albedo * _GrimeColor.rgb, grime);
                half metallic = lerp(_Metallic, _WearMetallic, wear);
                half roughness = saturate(lerp(_Roughness, _WearRoughness, wear) + grime * 0.25h);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = metallic;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = 1.0h - roughness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0h;
                surface.alpha = 1.0h;
                #if defined(_EMISSION)
                    surface.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uvBase).rgb * _EmissionColor.rgb;
                #endif

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "WornSurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "WornSurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #include "WornSurfaceInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
