// Obstruction and status lamps (crane tops, the port camera): an unlit dot that blinks on its own phase, taken from
// its world position so neighbouring lamps never blink together. _Rate 0 keeps it steady.
Shader "BorderRepair/Exterior/Beacon"
{
    Properties
    {
        _Color ("Colour", Color) = (.86, .41, .35, 1)
        _Intensity ("Intensity", Float) = 2.2
        _Rate ("Blinks per second", Float) = .5
        _Duty ("Lit fraction", Range(0.05, 1)) = .3
        _Floor ("Unlit level", Range(0, 1)) = .12
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Intensity, _Rate, _Duty, _Floor;
            CBUFFER_END
            struct A { float4 p : POSITION; };
            struct V { float4 p : SV_POSITION; float fog : TEXCOORD0; float phase : TEXCOORD1; };
            V vert(A a)
            {
                V o;
                float3 w = TransformObjectToWorld(a.p.xyz);
                o.p = TransformWorldToHClip(w);
                o.fog = ComputeFogFactor(o.p.z);
                float3 origin = GetObjectToWorldMatrix()._m03_m13_m23;
                o.phase = frac(sin(dot(floor(origin * 4), float3(12.9898, 78.233, 37.719))) * 43758.5453);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half on = _Rate <= 0 ? 1 : step(frac(_Time.y * _Rate + i.phase), _Duty);
                half3 c = _Color.rgb * _Intensity * lerp(_Floor, 1, on);
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }
    }
}
