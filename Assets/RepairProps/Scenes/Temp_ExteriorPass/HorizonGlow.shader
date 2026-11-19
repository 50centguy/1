// Light the city throws up into the snow cloud behind the skyline: an additive band on a ring past every building,
// warm at the horizon, cooler and fading upward, brighter in a few lobes (the busy quarters). Drawn after the sky, so
// the skyline masses (opaque, nearer) cover it. No fog: it is the haze.
Shader "BorderRepair/Exterior/HorizonGlow"
{
    Properties
    {
        _Low ("Horizon colour", Color) = (.42, .27, .16, 1)
        _High ("Upper colour", Color) = (.12, .2, .24, 1)
        _Intensity ("Intensity", Float) = .35
        _Lobes ("Lobes round the ring", Float) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-50" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Low, _High;
            half _Intensity, _Lobes;
            CBUFFER_END
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a) { V o; o.p = TransformObjectToHClip(a.p.xyz); o.uv = a.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                half y = saturate(i.uv.y);
                half fade = (1 - y) * (1 - y) * saturate(y * 12);
                half lobe = .55 + .45 * sin(i.uv.x * 6.2832 * _Lobes + 1.1) * sin(i.uv.x * 6.2832 * 1.7 + .4);
                half3 c = lerp(_Low.rgb, _High.rgb, saturate(y * 1.6)) * _Intensity * fade * (.6 + .4 * lobe);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
