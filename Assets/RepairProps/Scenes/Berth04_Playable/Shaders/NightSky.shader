// The Berth 04 playable's night sky: a cold scatter glow at the horizon (#2A3942) fading up to a dark slate zenith
// (#0E1519), so the tops of the towers and cranes cut out against it instead of sinking into black. The overcast
// cloud texture of the district's sky (Temp_WinterNight/OvercastSky) is kept, faint.
Shader "BorderRepair/Exterior/NightSky"
{
    Properties
    {
        _Horizon ("Horizon", Color) = (.165, .224, .259, 1)
        _Zenith ("Zenith", Color) = (.055, .082, .098, 1)
        _Height ("Glow height (direction y)", Range(.02, 1)) = .32
        _Cloud ("Cloud variation", Range(0, .5)) = .12
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Horizon, _Zenith;
            half _Height, _Cloud;
            CBUFFER_END
            struct A { float4 p : POSITION; };
            struct V { float4 p : SV_POSITION; float3 ray : TEXCOORD0; };
            V vert(A a) { V o; o.p = TransformObjectToHClip(a.p.xyz); o.ray = a.p.xyz; return o; }
            float h(float2 p) { return frac(sin(dot(p, float2(41.1, 285.7))) * 43758.5); }
            float n(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f); return lerp(lerp(h(i), h(i + float2(1, 0)), f.x), lerp(h(i + float2(0, 1)), h(i + 1), f.x), f.y); }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.ray);
                float t = pow(saturate(max(d.y, 0) / _Height), .7);
                float2 uv = d.xz / (max(d.y, 0) + .16);
                float cloud = .55 * n(uv * 1.1) + .30 * n(uv * 2.7) + .15 * n(uv * 6);
                half3 c = lerp(_Horizon.rgb, _Zenith.rgb, t) * (1 + (cloud - .5) * 2 * _Cloud);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
