// Traffic on the far elevated road: a ribbon of head or tail lamps sliding along uv.x (one cell = one car slot,
// some slots empty), unlit, cut out between the cars. Past the scene fog, so it fades to the fog colour by a fixed
// _Haze instead. Runs on DISTRICT_TIME so a rendered clip keeps the cars at the right speed.
Shader "BorderRepair/Exterior/TrafficStream"
{
    Properties
    {
        _Color ("Lamp colour", Color) = (1, .9, .75, 1)
        _Intensity ("Intensity", Float) = 2
        _Speed ("Cells per second (sign = direction)", Float) = 4
        _Duty ("Lamp length in a cell", Range(.05, 1)) = .35
        _Density ("Share of cells with a car", Range(0, 1)) = .55
        _Seed ("Seed", Float) = 0
        _Haze ("Fixed haze toward the fog colour", Range(0, 1)) = .45
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // DistrictMotion pins this while a clip renders; otherwise the engine clock.
            float _DistrictTime, _DistrictTimeOn;
            #define DISTRICT_TIME (_DistrictTimeOn > .5 ? _DistrictTime : _Time.y)
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Intensity, _Speed, _Duty, _Density, _Seed, _Haze;
            CBUFFER_END
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a) { V o; o.p = TransformObjectToHClip(a.p.xyz); o.uv = a.uv; return o; }
            float hash1(float x) { return frac(sin(x * 12.9898 + 78.233) * 43758.5453); }
            half4 frag(V i) : SV_Target
            {
                float x = i.uv.x - DISTRICT_TIME * _Speed;
                float cell = floor(x);
                half on = step(frac(x), _Duty) * step(hash1(cell + _Seed * 31.7), _Density);
                clip(on - .5);
                half3 c = _Color.rgb * _Intensity * (.75 + .25 * hash1(cell * 1.3 + _Seed));
                return half4(lerp(c, unity_FogColor.rgb, _Haze), 1);
            }
            ENDHLSL
        }
    }
}
