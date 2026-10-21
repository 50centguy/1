// Translucent PVC strip curtain: faint tinted body, brighter at grazing angles (fresnel), picks up the strongest
// nearby colour through the main light and ambient only (no per-pixel extra lights, it is a thin film).
Shader "BorderRepair/CyberCurtain"
{
    Properties
    {
        _Color("Tint", Color) = (0.55, 0.75, 0.85, 0.12)
        [HDR] _RimColor("Rim", Color) = (0.6, 0.9, 1.0, 1)
        _RimPower("Rim power", Range(0.5, 8)) = 2.5
        _Grime("Grime at the hem", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "Curtain"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _RimColor;
                half _RimPower, _Grime;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; float2 uv : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(1.0 - abs(dot(n, v)), _RimPower);
                Light main = GetMainLight();
                half3 lit = SampleSH(n) + main.color * 0.25;
                half hem = smoothstep(0.35, 0.0, input.uv.y) * _Grime;
                half3 color = _Color.rgb * lit * (1.0 - hem * 0.6) + _RimColor.rgb * rim * 0.35;
                half alpha = saturate(_Color.a + rim * 0.25 + hem * 0.25);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
