// Mid-distance blocks behind the district (30-120 m): a dark mass with a window grid laid in world space (so any box
// size gets storey-sized windows), a few windows lit per facade: mostly dim amber rooms, some neutral, the odd cold
// screen. Unlit; fogged by the scene fog so the layers step back into the snow haze, or by a fixed _Haze when set.
Shader "BorderRepair/Exterior/CityBlock"
{
    Properties
    {
        _Color ("Mass", Color) = (.07, .09, .1, 1)
        _Cell ("Window cell (m): width, storey", Vector) = (1.8, 3, 0, 0)
        _Lit ("Share of windows lit", Range(0, 1)) = .1
        _Warm ("Room light", Color) = (.91, .6, .24, 1)
        _Neutral ("Neutral light", Color) = (.78, .78, .72, 1)
        _Screen ("Screen light", Color) = (.42, .88, .91, 1)
        _Gain ("Window gain", Float) = 1
        _Haze ("Fixed haze (-1 = scene fog)", Float) = -1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color, _Warm, _Neutral, _Screen;
            float4 _Cell;
            half _Lit, _Gain, _Haze;
            CBUFFER_END
            struct A { float4 p : POSITION; float3 n : NORMAL; };
            struct V { float4 cs : SV_POSITION; float3 w : TEXCOORD0; float3 n : TEXCOORD1; float fog : TEXCOORD2; };
            V vert(A a)
            {
                V o;
                o.w = TransformObjectToWorld(a.p.xyz);
                o.cs = TransformWorldToHClip(o.w);
                o.n = TransformObjectToWorldNormal(a.n);
                o.fog = ComputeFogFactor(o.cs.z);
                return o;
            }
            float hash3(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }
            half4 frag(V i) : SV_Target
            {
                float3 an = abs(i.n);
                half3 c = _Color.rgb * (.85 + .15 * saturate(i.w.y / 40));
                if (an.y > .5)
                {
                    c *= .75;
                }
                else
                {
                    bool alongZ = an.x > an.z;
                    float2 uv = float2(alongZ ? i.w.z : i.w.x, i.w.y) / _Cell.xy;
                    float2 cell = floor(uv), f = frac(uv);
                    float plane = round(alongZ ? i.w.x : i.w.z);
                    half win = step(.2, f.x) * step(f.x, .8) * step(.3, f.y) * step(f.y, .78);
                    float h = hash3(float3(cell, plane));
                    float k = hash3(float3(cell.yx + 17.1, plane));
                    half3 light = k < .68 ? _Warm.rgb * .55 : (k < .94 ? _Neutral.rgb * .5 : _Screen.rgb * .45);
                    half lit = step(h, _Lit) * (.55 + .45 * hash3(float3(cell * 1.7, plane + 3)));
                    c = lerp(c, lerp(c * 1.35 + .012, light * _Gain, lit), win);
                }
                if (_Haze >= 0) c = lerp(c, unity_FogColor.rgb, saturate(_Haze));
                else c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
