// A layer of air between the middle distance and the skyline: a very faint fog-coloured band, densest at the ground and
// fading out upward and at its ends. Alpha blended, no depth write; nearer opaque geometry covers it, so it only lies
// over what is behind it (the towers, the cranes' far side).
Shader "BorderRepair/Exterior/AirVeil"
{
    Properties
    {
        _Color ("Colour", Color) = (.231, .294, .329, 1)
        _Alpha ("Alpha at the ground", Range(0, 1)) = .22
        _Fall ("Upward falloff", Float) = 1.6
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-45" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Alpha, _Fall;
            CBUFFER_END
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a) { V o; o.p = TransformObjectToHClip(a.p.xyz); o.uv = a.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                half y = saturate(i.uv.y);
                half a = _Alpha * pow(1 - y, _Fall) * saturate(y * 20 + .3);
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
}
