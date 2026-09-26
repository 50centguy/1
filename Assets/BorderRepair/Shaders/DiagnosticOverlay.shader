// 零件诊断高亮叠加层：由 InspectionPoint 追加到部位渲染器的额外材质槽，参数通过该槽位的 MaterialPropertyBlock 写入。
// - 只覆盖对应部位：画的就是该部位自己的网格；
// - 不透墙：ZTest LEqual + 不写深度 + 微小负偏移，被遮挡的部分深度测试失败；
// - 不遮挡细节：滤色混合（Blend OneMinusDstColor One），只提亮不覆盖，主体亮度放在轮廓边缘光上；
// - 不产生阴影、不参与深度预渲染（没有 ShadowCaster / DepthOnly pass）。
Shader "BorderRepair/DiagnosticOverlay"
{
    Properties
    {
        [HDR] _DiagColor("颜色", Color) = (0.3, 0.9, 1, 1)
        _DiagParams("x 提亮, y 边缘光, z 边缘光指数, w 扫描线", Vector) = (0.08, 0.9, 2.5, 0)
        _DiagParams2("x 扫描线密度(条/米), y 脉冲速度, z 斜纹强度, w 斜纹间距(像素)", Vector) = (140, 0, 0, 16)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "DiagnosticOverlay"
            Tags { "LightMode" = "UniversalForward" }
            Blend OneMinusDstColor One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OverlayVert
            #pragma fragment OverlayFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DiagColor;
                float4 _DiagParams;
                float4 _DiagParams2;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings OverlayVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 OverlayFrag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half ndv = saturate(dot(n, v));
                half rim = pow(1.0h - ndv, max(_DiagParams.z, 0.5)) * _DiagParams.y;

                half pulse = _DiagParams2.y > 0 ? 0.65h + 0.35h * sin(_Time.y * _DiagParams2.y) : 1.0h;

                half scan = 0;
                if (_DiagParams.w > 0)
                {
                    float s = frac(input.positionWS.y * _DiagParams2.x - _Time.y * 0.6);
                    scan = smoothstep(0.0, 0.05, s) * (1.0 - smoothstep(0.05, 0.14, s)) * _DiagParams.w;
                }

                half hatch = 0;
                if (_DiagParams2.z > 0)
                {
                    float2 px = input.positionCS.xy;   // 屏幕像素坐标：斜纹在任何角度都保持同样的间距
                    float h = frac((px.x + px.y) / max(_DiagParams2.w, 2.0));
                    hatch = step(h, 0.28) * _DiagParams2.z;
                }

                half intensity = (_DiagParams.x + rim + scan + hatch) * pulse;
                return half4(saturate(_DiagColor.rgb * intensity), 1.0h);
            }
            ENDHLSL
        }
    }
}
