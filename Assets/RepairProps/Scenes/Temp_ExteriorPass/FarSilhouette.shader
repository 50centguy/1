// Skyline masses far past the fog distance (the corporate towers): a flat colour pulled toward the fog colour by a fixed
// _Haze, so they read as darker shapes in the snow haze instead of vanishing into it. Optional window texture adds a
// faint lit grid, recoloured cold by _WindowColor (amber belongs to the shop). _HazeLow sinks the lower part into the
// fog: extra haze toward the fog colour below _HazeLowTop (world height), none above it (0 = off).
Shader "BorderRepair/Exterior/FarSilhouette"
{
    Properties
    {
        _Color ("Mass colour", Color) = (.06, .09, .1, 1)
        _Haze ("Haze toward fog colour", Range(0, 1)) = .55
        _HazeLow ("Extra haze low down", Range(0, 1)) = 0
        _HazeLowTop ("Height where the extra haze ends", Float) = 40
        _Windows ("Window grid (rgb)", 2D) = "black" {}
        _WindowGain ("Window gain", Float) = .5
        _WindowColor ("Window colour (applied to grid brightness)", Color) = (.55, .7, .75, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_Windows); SAMPLER(sampler_Windows);
            CBUFFER_START(UnityPerMaterial)
            float4 _Windows_ST;
            half4 _Color, _WindowColor;
            half _Haze, _WindowGain, _HazeLow;
            float _HazeLowTop;
            CBUFFER_END
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; float wy : TEXCOORD1; };
            V vert(A a) { V o; o.cs = TransformObjectToHClip(a.p.xyz); o.uv = TRANSFORM_TEX(a.uv, _Windows); o.wy = TransformObjectToWorld(a.p.xyz).y; return o; }
            half4 frag(V i) : SV_Target
            {
                half lum = dot(SAMPLE_TEXTURE2D(_Windows, sampler_Windows, i.uv).rgb, half3(.3, .59, .11));
                half3 c = _Color.rgb + lum * _WindowColor.rgb * _WindowGain;
                half low = _HazeLow * (1 - saturate(i.wy / max(_HazeLowTop, .01)));
                return half4(lerp(c, unity_FogColor.rgb, saturate(_Haze + (1 - _Haze) * low)), 1);
            }
            ENDHLSL
        }
    }
}
