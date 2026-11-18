// Projected signage and UI (shop hours, the port patrol frame, the berth readout, the court address, the far corporate
// band): white glyphs on transparent, coloured by _Color, added on top. Scanlines crawl upward, the image dips now and
// then on its own phase, and it fades with the fog (to black, since it adds light). _Haze >= 0 replaces the scene fog with
// a fixed fade for skyline pieces that sit far beyond the fog distance. _Spin turns the quad about the world vertical
// through its origin (radians per second), for the shop's turning arm. Renderers using this must stay out of static
// batching: batching moves every origin to the world origin.
Shader "BorderRepair/Exterior/Hologram"
{
    Properties
    {
        _MainTex ("Glyphs (alpha)", 2D) = "white" {}
        _Color ("Colour", Color) = (.42, .88, .91, 1)
        _Intensity ("Intensity", Float) = 1.3
        _Scan ("Scanlines per metre", Float) = 90
        _Flicker ("Flicker", Range(0, 1)) = .25
        _Haze ("Fixed haze (-1 = scene fog)", Float) = -1
        _Spin ("Spin (rad/s)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            half _Intensity, _Scan, _Flicker, _Haze, _Cull;
            float _Spin;
            CBUFFER_END
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; float3 w : TEXCOORD1; float fog : TEXCOORD2; float phase : TEXCOORD3; };
            V vert(A a)
            {
                V o;
                float3 origin = GetObjectToWorldMatrix()._m03_m13_m23;
                o.phase = frac(sin(dot(floor(origin * 3), float3(12.9898, 78.233, 37.719))) * 43758.5453);
                o.w = TransformObjectToWorld(a.p.xyz);
                if (_Spin != 0)
                {
                    float s, c;
                    sincos(_Time.y * _Spin + o.phase * 6.2832, s, c);
                    float2 r = o.w.xz - origin.xz;
                    o.w.xz = origin.xz + float2(r.x * c - r.y * s, r.x * s + r.y * c);
                }
                float glitch = step(.985, frac(_Time.y * .37 + o.phase * 7.1));
                o.w.x += glitch * .012 * sin(_Time.y * 90 + o.w.y * 40);
                o.cs = TransformWorldToHClip(o.w);
                o.uv = TRANSFORM_TEX(a.uv, _MainTex);
                o.fog = ComputeFogFactor(o.cs.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                half scan = .7 + .3 * step(.45, frac(i.w.y * _Scan - _Time.y * 1.7));
                half band = .86 + .14 * sin(i.w.y * 2.5 - _Time.y * 2.2 + i.phase * 6.28);
                half dip = 1 - _Flicker * step(.9, frac(sin(floor(_Time.y * 11 + i.phase * 50)) * 43758.5453));
                half3 c = _Color.rgb * _Intensity * a * scan * band * dip;
                if (_Haze >= 0) c *= 1 - saturate(_Haze);
                else c = MixFogColor(c, half3(0, 0, 0), i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
