// A searchlight beam far over the skyline (the LampVolume cone without the scene fog, which would swallow it at this
// range): additive, brightest seen through its middle, fading along its length and by a fixed _Haze.
Shader "BorderRepair/Exterior/SkyBeam"
{
    Properties
    {
        _Color ("Colour", Color) = (.7, .8, .95, 1)
        _Intensity ("Intensity", Float) = .12
        _Haze ("Fixed haze", Range(0, 1)) = .4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Intensity, _Haze;
            CBUFFER_END
            struct A { float4 p : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float3 w : TEXCOORD0; float3 n : TEXCOORD1; float along : TEXCOORD2; };
            V vert(A a)
            {
                V o;
                o.w = TransformObjectToWorld(a.p.xyz);
                o.p = TransformWorldToHClip(o.w);
                o.n = TransformObjectToWorldNormal(a.n);
                o.along = a.uv.y;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 v = normalize(_WorldSpaceCameraPos - i.w);
                half edge = saturate(abs(dot(normalize(i.n), v)));
                edge *= edge;
                half fall = pow(saturate(1 - i.along), 1.2);
                return half4(_Color.rgb * _Intensity * edge * fall * (1 - _Haze), 1);
            }
            ENDHLSL
        }
    }
}
