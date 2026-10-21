// Projector beam: additive, fades from the lens (uv.y = 0) to the screen (uv.y = 1), with faint moving bands.
Shader "BorderRepair/CyberBeam"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (1, .6, .2, .08)
        _Seed("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Seed;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half along = saturate(input.uv.y);
                half fade = along * along * (1.0 - smoothstep(.92, 1.0, along)) + .15 * (1.0 - along);
                half bands = .75 + .25 * sin(input.uv.y * 90.0 - _Time.y * 6.0 + _Seed);
                half stutter = 1.0 - .4 * step(.97, frac(sin(floor(_Time.y * 12.0) * 12.9898 + _Seed) * 43758.5453));
                return half4(_Color.rgb * _Color.a * fade * bands * stutter, 1.0);
            }
            ENDHLSL
        }
    }
}
