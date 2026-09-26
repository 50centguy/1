// 旧工业诊断设备的小屏幕（工作台检测仪用）：背光磷光屏，有限的像素栅格、很轻的扫描线、略不均匀的背光。
// - 只用面片自己的 UV 计算（不依赖屏幕坐标），近看、斜看、以后 VR 双眼都一致；
// - 栅格和扫描线是静态的，并在格子小到屏幕像素以下时自动淡出（避免摩尔纹闪动）；
// - 读数切换后 _Switch.x 秒内有一次短暂跳变（亮度抖动 + 轻微行错位），之后完全稳定，不再随时间变化；
// - 字从贴图集取（与原贴图同一张，_BaseMap_ST 选读数区域），按亮度重新上色，所以 READY / BYPASS / LOG 可以用不同的预设颜色。
// 与 WornSurface（受光表面 + 磨损）和 DiagnosticOverlay（高亮叠加层）功能不重叠：本 shader 不受场景光照，只模拟自发光屏幕。
Shader "BorderRepair/DiagnosticScreen"
{
    Properties
    {
        [MainTexture] _BaseMap("读数贴图集", 2D) = "black" {}
        [MainColor] _BaseColor("贴图乘色", Color) = (1, 1, 1, 1)
        _ScreenRect("面片烘焙的 UV 区域 (u0, v0, 宽, 高)", Vector) = (0, 0.25, 0.3333333, 0.25)

        [Header(Phosphor)]
        [HDR] _InkColor("字的颜色", Color) = (0.45, 1.1, 0.5, 1)
        _BackColor("底色", Color) = (0.015, 0.045, 0.02, 1)
        _InkThreshold("字的亮度阈值", Range(0.05, 0.8)) = 0.28

        [Header(Pixel Grid And Scanlines)]
        _Grid("栅格：x 横向格数, y 纵向格数, z 强度, w 格缝宽度", Vector) = (96, 72, 0.16, 0.14)
        _Scan("扫描线：x 条数, y 强度", Vector) = (72, 0.08, 0, 0)

        [Header(Backlight)]
        _Backlight("背光：x 暗角, y 不均匀, z 整体亮度", Vector) = (0.35, 0.12, 1.25, 0)

        [Header(Switch Glitch)]
        _Switch("切换跳变：x 持续秒数, y 强度", Vector) = (0.2, 0.5, 0, 0)
        [HideInInspector] _SwitchTime("最近一次切换时间", Float) = -100

        // URP 自带 pass 需要的属性
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [HideInInspector] _BumpScale("Normal Scale", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 100

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ScreenVert
            #pragma fragment ScreenFrag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "DiagnosticScreenInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half   fogFactor  : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ScreenVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.uv = input.uv;
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            // 固定的低频“背光不均”：只由屏幕坐标决定，不随时间变化
            half Backlight(float2 p)
            {
                float2 c = p - 0.5;
                half vignette = 1.0h - _Backlight.x * dot(c * float2(1.2, 1.6), c * float2(1.2, 1.6));
                half uneven = 1.0h + _Backlight.y * (0.6h * sin(p.x * 3.1 + 1.3) + 0.4h * sin(p.y * 4.7 + 0.4) - 0.2h);
                return saturate(vignette) * uneven * _Backlight.z;
            }

            // 栅格缝：格数越多、屏幕越远，缝越细；缝宽小于约一个屏幕像素时淡出，避免摩尔纹
            half GridMask(float2 p)
            {
                float2 cells = p * _Grid.xy;
                float2 f = frac(cells);
                float2 fw = fwidth(cells);
                float2 edge = min(f, 1.0 - f);
                float2 gap = 1.0 - smoothstep(_Grid.w * 0.5, _Grid.w * 0.5 + fw, edge);
                half lines = max(gap.x, gap.y);
                half visible = saturate(2.5 - max(fw.x, fw.y) * 6.0);
                return 1.0h - _Grid.z * lines * visible;
            }

            half ScanMask(float2 p)
            {
                float rows = p.y * _Scan.x;
                float fw = fwidth(rows);
                half s = 0.5h + 0.5h * cos(rows * 6.2831853);
                half visible = saturate(2.0 - fw * 4.0);
                return 1.0h - _Scan.y * s * visible;
            }

            half4 ScreenFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 面片自己的 0..1 坐标（贴图集里烘焙的区域换算回来）
                float2 p = (input.uv - _ScreenRect.xy) / max(_ScreenRect.zw, 1e-5);

                // 切换读数后的短暂跳变：只在 _Switch.x 秒内起作用，之后 k = 0，画面与时间无关
                float dt = _Time.y - _SwitchTime;
                half k = (dt >= 0.0 && dt < _Switch.x) ? (1.0h - dt / _Switch.x) * _Switch.y : 0.0h;
                float row = floor(p.y * 24.0);
                float tear = k * 0.03 * sin(row * 12.9898 + floor(dt * 60.0) * 7.0);
                float2 uv = input.uv + float2(tear * _ScreenRect.z, 0.0);

                half3 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv * _BaseMap_ST.xy + _BaseMap_ST.zw).rgb * _BaseColor.rgb;
                half lum = max(texel.r, max(texel.g, texel.b));
                half ink = smoothstep(_InkThreshold, _InkThreshold + 0.25h, lum);

                half3 color = lerp(_BackColor.rgb, _InkColor.rgb, ink);
                color *= Backlight(p) * GridMask(p) * ScanMask(p);
                color *= 1.0h - k * step(0.5, frac(dt * 37.0)) * 0.6h;      // 跳变期间亮度抖动

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "DiagnosticScreenInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "DiagnosticScreenInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #include "DiagnosticScreenInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
