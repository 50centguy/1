Shader "BorderRepair/CosmeticLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base",2D)="white"{}
        [MainColor] _BaseColor("Color",Color)=(1,1,1,1)
        _CosmeticLayer("Paint",2D)="black"{}
        _WorkflowMode("Workflow",Float)=1
        _Metallic("Metallic",Range(0,1))=0
        _MetallicGlossMap("Metallic",2D)="white"{}
        _SpecColor("Specular",Color)=(.2,.2,.2,1)
        _SpecGlossMap("Specular",2D)="white"{}
        _Smoothness("Smoothness",Range(0,1))=.5
        _SmoothnessTextureChannel("Smoothness Channel",Float)=0
        _BumpMap("Normal",2D)="bump"{}
        _BumpScale("Normal Scale",Float)=1
        _OcclusionMap("Occlusion",2D)="white"{}
        _OcclusionStrength("Occlusion Strength",Range(0,1))=1
        _EmissionMap("Emission",2D)="white"{}
        [HDR] _EmissionColor("Emission Color",Color)=(0,0,0,1)
        _DetailMask("Detail Mask",2D)="white"{}
        _DetailAlbedoMap("Detail",2D)="linearGrey"{}
        _DetailNormalMap("Detail Normal",2D)="bump"{}
        _DetailAlbedoMapScale("Detail Scale",Float)=1
        _DetailNormalMapScale("Detail Normal Scale",Float)=1
        _Cutoff("Cutoff",Float)=.5
        _Cull("Cull",Float)=2
        _Surface("Surface",Float)=0
        _AlphaClip("AlphaClip",Float)=0
        _ParallaxMap("Height",2D)="black"{}
        _Parallax("Parallax",Float)=.005
        _ClearCoatMask("Coat",Float)=0
        _ClearCoatSmoothness("Coat Smoothness",Float)=0
        _SpecularHighlights("Specular Highlights",Float)=1
        _EnvironmentReflections("Reflections",Float)=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "UniversalMaterialType"="Lit" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull [_Cull] ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _SPECULAR_SETUP
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #define InitializeStandardLitSurfaceData InitializeUnpaintedSurfaceData
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #undef InitializeStandardLitSurfaceData
            TEXTURE2D(_CosmeticLayer); SAMPLER(sampler_CosmeticLayer);
            void InitializeStandardLitSurfaceData(float2 uv,out SurfaceData surface)
            {
                InitializeUnpaintedSurfaceData(uv,surface);
                half4 paint=SAMPLE_TEXTURE2D(_CosmeticLayer,sampler_CosmeticLayer,uv);
                surface.albedo=paint.rgb+surface.albedo*(1-paint.a);
            }
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
