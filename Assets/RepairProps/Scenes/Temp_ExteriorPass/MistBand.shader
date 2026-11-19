// A ring of thin mist standing round the district (Berth 04 playable): the fog colour, thickest at the ground and thinning
// upward, drifting slowly in broad lobes so it does not read as a wall. Alpha blended over everything opaque, so the gaps
// between the far blocks fill with haze rather than bare sky. One draw per ring, no texture.
Shader "BorderRepair/Exterior/MistBand"
{
    Properties
    {
        _Density ("Density at the ground", Float) = .4
        _Falloff ("Thinning upward (power)", Float) = 1.6
        _Tint ("Tint over the fog colour", Color) = (1, 1, 1, 1)
        _Drift ("Drift speed", Float) = .015
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-40" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            half _Density, _Falloff, _Drift;
            half4 _Tint;
            CBUFFER_END
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a) { V o; o.p = TransformObjectToHClip(a.p.xyz); o.uv = a.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                half y = saturate(i.uv.y);
                float x = i.uv.x * 6.2832;
                float t = _Time.y * _Drift;
                half lobes = .62 + .2 * sin(x * 5 + t * 6.2832) + .18 * sin(x * 13 - t * 9 + 1.7);
                half a = _Density * pow(1 - y, _Falloff) * saturate(y * 30 + .2) * lobes;
                return half4(unity_FogColor.rgb * _Tint.rgb, saturate(a));
            }
            ENDHLSL
        }
    }
}
