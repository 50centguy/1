// A cheap stand-in for the light a street or wharf lamp throws through falling snow: an additive cone, brightest where
// the eye looks through its middle and near the lamp, fading toward the ground, into the fog and when walked through.
Shader "BorderRepair/Exterior/LampVolume"
{
    Properties
    {
        _Color ("Colour", Color) = (1, .75, .48, 1)
        _Intensity ("Intensity", Float) = .25
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Intensity;
            CBUFFER_END
            struct A { float4 p : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float3 w : TEXCOORD0; float3 n : TEXCOORD1; float along : TEXCOORD2; float fog : TEXCOORD3; };
            V vert(A a)
            {
                V o;
                o.w = TransformObjectToWorld(a.p.xyz);
                o.p = TransformWorldToHClip(o.w);
                o.n = TransformObjectToWorldNormal(a.n);
                o.along = a.uv.y;
                o.fog = ComputeFogFactor(o.p.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 v = normalize(_WorldSpaceCameraPos - i.w);
                half edge = saturate(abs(dot(normalize(i.n), v)));
                edge *= edge;
                half fall = pow(saturate(1 - i.along), 1.5);
                half grain = .86 + .14 * sin(i.w.y * 6.0 + _Time.y * 1.1 + i.w.x * 2.3);
                half near = saturate(distance(_WorldSpaceCameraPos, i.w) / 2.5);
                half3 c = _Color.rgb * _Intensity * edge * fall * grain * near;
                c = MixFogColor(c, half3(0, 0, 0), i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
