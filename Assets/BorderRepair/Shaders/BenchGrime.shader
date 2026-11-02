// 工作台上的助焊剂残迹：一小滩、一道往下淌的。全部在着色器里解析生成，不占任何贴图。
// 一块 Quad 就是一处残迹：_Mode 0 是平放的池子，1 是从台沿垂下来的一道。
// 加法混合会让它发亮，脏东西应该是压暗的，所以走普通的 alpha 混合、不写深度。
Shader "BorderRepair/BenchGrime"
{
    Properties
    {
        _GrimeColor("残迹颜色 (sRGB)", Color) = (0.13, 0.095, 0.05, 1)
        _Opacity("不透明度", Range(0, 1)) = 0.55
        [Enum(Pool,0,Run,1)] _Mode("形态（0 滩 / 1 淌）", Float) = 0
        _Seed("随机种子", Float) = 0
        _Edge("边缘柔度", Range(0.02, 0.5)) = 0.18
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-1" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GrimeVert
            #pragma fragment GrimeFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GrimeColor;
                half  _Opacity;
                half  _Mode;
                half  _Seed;
                half  _Edge;
            CBUFFER_END

            float GrimeHash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float GrimeNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = GrimeHash(i);
                float b = GrimeHash(i + float2(1.0, 0.0));
                float c = GrimeHash(i + float2(0.0, 1.0));
                float d = GrimeHash(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings GrimeVert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            half4 GrimeFrag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                half alpha;

                if (_Mode < 0.5)
                {
                    // 平放的小滩：轮廓用两层噪声打散，中间厚、边上薄到没有
                    float2 p = (uv - 0.5) * 2.0;
                    float n = GrimeNoise(uv * 4.0 + _Seed) * 0.6 + GrimeNoise(uv * 11.0 + _Seed * 1.7) * 0.4;
                    float r = length(p) + (n - 0.5) * 0.9;
                    alpha = 1.0 - smoothstep(0.55 - _Edge, 0.55 + _Edge, r);
                    alpha *= saturate(0.35 + n);
                    // 滩里再压几道深一点的积存
                    alpha *= 0.75 + 0.25 * GrimeNoise(uv * 7.0 + _Seed * 3.1);
                }
                else
                {
                    // 往下淌的一道：越往下越窄越淡，沿着长度方向摆动
                    float wobble = (GrimeNoise(float2(uv.y * 5.0 + _Seed, _Seed * 1.3)) - 0.5) * 0.16;
                    float across = abs(uv.x - 0.5 + wobble);
                    float width = lerp(0.10, 0.26, 1.0 - uv.y);   // uv.y = 1 在台沿，往下（0）变宽后散开
                    alpha = 1.0 - smoothstep(width * 0.65, width, across);
                    alpha *= smoothstep(0.0, 0.42, uv.y);          // 流到底就没了
                    alpha *= 0.8 + 0.2 * GrimeNoise(float2(uv.x * 6.0, uv.y * 18.0 + _Seed));
                }

                alpha *= _Opacity;
                if (alpha < 0.004) discard;
                return half4(_GrimeColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
