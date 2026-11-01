// 诊所外壳：地面（旧环氧涂层混凝土）/ 墙面（两道色 + 腰线）/ 天花板（去饱和 + 烟熏）。
// 同一个 shader，用 _ShellKind（材质常量，统一分支）区分三种表面。
// 图层全部在世界坐标里解析生成（相对 _RoomCenter），不依赖网格 UV；_BaseMap 只作为细节来源。
// 光照走 URP 的 UniversalFragmentPBR（主光阴影/附加光/Forward+/SSAO/雾），
// ShadowCaster / DepthOnly / DepthNormals 复用 URP 自带 pass —— 四段 pass 结构与 WornSurface.shader 相同。
// 颜色属性按 sRGB 填写；图层细节见 ClinicShellSurface.hlsl。
Shader "BorderRepair/ClinicShell"
{
    Properties
    {
        [Enum(Floor,0,Wall,1,Ceiling,2)] _ShellKind("外壳类型", Float) = 1
        [MainTexture] _BaseMap("细节贴图（T_UC_Floor / T_UC_Wall，1.2 m 一张）", 2D) = "grey" {}
        [MainColor] _BaseColor("整体色调（线性相乘，默认白）", Color) = (1, 1, 1, 1)
        _RoomCenter("房间中心（世界坐标，y = 地面高度）", Vector) = (0, 0, 0, 0)
        _RoomApothem("八边形内壁边心距 (m)", Float) = 3.4
        _Seed("随机种子", Float) = 0

        [Header(Floor)]
        _FloorColor("地面：旧环氧底色 (sRGB)", Color) = (0.12, 0.128, 0.138, 1)
        _FloorNewColor("地面：新替换板颜色 (sRGB)", Color) = (0.17, 0.18, 0.192, 1)
        _FloorSlabSize("地面：板尺寸 (m，贴图一板一张)", Float) = 1.2
        _FloorJointWidth("地面：切缝宽 (m)", Float) = 0.008
        _FloorJointDark("地面：切缝亮度倍数", Range(0, 1)) = 0.55
        _FloorSlabJitter("F1 逐板明暗档 (sRGB)", Range(0, 0.15)) = 0.065
        _FloorHueJitter("F1 逐板冷暖", Range(0, 0.1)) = 0.03
        _FloorNewRatio("F1 新替换板比例", Range(0, 0.5)) = 0.10
        _FloorTiltDeg("F2 逐板法线倾斜 (度)", Range(0, 6)) = 2
        _FloorDetail("贴图高频叠加强度", Range(0, 1)) = 0.5
        _FloorWearScale("F3 磨穿噪声尺度 (m)", Float) = 2.5
        _FloorWearLight("F3 磨穿提亮 (sRGB)", Range(0, 0.2)) = 0.07
        _FloorStainScale("F3 油污噪声尺度 (m)", Float) = 4.0
        _FloorStainDark("F3 油污压暗", Range(0, 0.6)) = 0.22
        _FloorStainDark2("F3 重油污再压暗", Range(0, 0.6)) = 0.25
        _FloorFootDark("F4 墙脚 0~0.09 m 压暗", Range(0, 0.8)) = 0.40
        _FloorFootDark2("F4 墙脚 0.09~0.25 m 压暗", Range(0, 0.8)) = 0.20
        _FloorSmoothness("地面光滑度", Range(0, 1)) = 0.42
        _FloorNewSmoothness("新板光滑度", Range(0, 1)) = 0.50

        [Header(Wall)]
        _WallLowerColor("墙：下段深绿半光漆 (sRGB)", Color) = (0.165, 0.225, 0.185, 1)
        _WallUpperColor("墙：上段浅灰绿亚光 (sRGB)", Color) = (0.245, 0.285, 0.265, 1)
        _WallStripeColor("墙：腰线 (sRGB)", Color) = (0.09, 0.11, 0.10, 1)
        _WallPrimerColor("墙：底漆灰 (sRGB)", Color) = (0.34, 0.34, 0.32, 1)
        _WallScratchColor("墙：刮痕露底 (sRGB)", Color) = (0.27, 0.29, 0.27, 1)
        _WallPanelWidth("墙板宽 (m)", Float) = 0.6
        _WallPanelHeight("墙板高 (m)", Float) = 0.6
        _WallSplitHeight("W8 分色高度 (m)", Float) = 1.1
        _WallStripeWidth("W8 腰线宽 (m)", Float) = 0.03
        _WallPanelJitter("W2 逐板明暗档 (sRGB)", Range(0, 0.15)) = 0.06
        _WallPrimerRatio("W2 底漆灰板比例", Range(0, 0.5)) = 0.125
        _WallRepaintRatio("W2 补漆矩形比例", Range(0, 0.5)) = 0.083
        _WallRepaintShift("W2 补漆色差 (sRGB)", Range(0, 0.2)) = 0.07
        _WallBulgeMinDeg("W3 鼓面最小角 (度)", Range(0, 10)) = 3
        _WallBulgeMaxDeg("W3 鼓面最大角 (度)", Range(0, 10)) = 5
        _WallScratchAmount("W8 下段刮痕量", Range(0, 0.4)) = 0.16
        _WallFootDark("W4 墙脚 0~0.22 m 压暗", Range(0, 0.8)) = 0.52
        _WallFootDark2("W4 墙脚 0.22~0.35 m 压暗", Range(0, 0.8)) = 0.26
        _WallDustDark("W5 2.5 m 以上积灰压暗", Range(0, 0.6)) = 0.18
        _WallDustDark2("W5 2.8 m 以上再压暗", Range(0, 0.6)) = 0.15
        _WallLeakRatio("W6 渗漏板比例", Range(0, 1)) = 0.25
        _WallLeakDark("W6 渗漏痕亮度倍数", Range(0, 1)) = 0.72
        _WallRustChroma("W7 锈保留色度（0.4 = 去饱和 60%）", Range(0, 1)) = 0.4
        _WallDetail("墙：贴图细节强度", Range(0, 1)) = 0.9
        _WallLowerSmoothness("墙：下段光滑度", Range(0, 1)) = 0.42
        _WallUpperSmoothness("墙：上段光滑度", Range(0, 1)) = 0.12

        [Header(Ceiling)]
        _CeilColor("天花板底色 (sRGB，约墙面 0.8x)", Color) = (0.165, 0.18, 0.175, 1)
        _CeilChroma("天花板贴图保留色度（0.2 = 去饱和 80%）", Range(0, 1)) = 0.2
        _CeilDetail("天花板贴图细节强度", Range(0, 1)) = 0.7
        _CeilPanelJitter("天花板逐板明暗档 (sRGB)", Range(0, 0.15)) = 0.06
        _CeilSmokeScaleA("烟熏噪声尺度 A (m)", Float) = 1.6
        _CeilSmokeScaleB("烟熏噪声尺度 B (m)", Float) = 0.7
        _CeilSmokeDark("烟熏压暗", Range(0, 0.6)) = 0.24
        _CeilSmokeDark2("重烟熏再压暗", Range(0, 0.6)) = 0.24
        _CeilSmoothness("天花板光滑度", Range(0, 1)) = 0.08

        // URP 自带 pass 需要的属性（本 shader 不做透明裁切）
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [HideInInspector] _BumpScale("Normal Scale", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "UniversalMaterialType" = "Lit" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShellVert
            #pragma fragment ShellFrag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "ClinicShellInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ClinicShellSurface.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ShellVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = nrm.normalWS;
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 ShellFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 导数在任何分支之前算好（图层函数内部只用 GRAD / LOD 采样）
                ShellCtx ctx;
                ctx.posWS = input.positionWS;
                ctx.nGeo = normalize(input.normalWS);
                ctx.dPdx = ddx(input.positionWS);
                ctx.dPdy = ddy(input.positionWS);
                ctx.pixelM = max(max(length(ctx.dPdx), length(ctx.dPdy)), 1e-5);

                ShellSurface shell;
                shell.albedo = float3(0.5, 0.5, 0.5);
                shell.smoothness = 0.2;
                shell.normalWS = ctx.nGeo;

                // _ShellKind 是材质常量：统一分支
                if (_ShellKind < 0.5)
                    ShellFloor(ctx, shell);
                else if (_ShellKind < 1.5)
                    ShellWall(ctx, shell);
                else
                    ShellCeiling(ctx, shell);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = ShellToLinear(saturate(shell.albedo)) * _BaseColor.rgb;
                surface.metallic = 0.0h;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = saturate(shell.smoothness);
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0h;
                surface.alpha = 1.0h;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(shell.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
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
            #include "ClinicShellInput.hlsl"
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
            #include "ClinicShellInput.hlsl"
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
            #include "ClinicShellInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
