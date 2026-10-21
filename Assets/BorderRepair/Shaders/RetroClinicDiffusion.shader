Shader "Hidden/BorderRepair/RetroClinicDiffusion"
{
    Properties
    {
        _DiffusionBlend("Soft diffusion blend", Range(0, 0.35)) = 0.16
        _RadiusPixels("Radius in pixels", Range(0.25, 4)) = 1.3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always
        Pass
        {
            Name "SoftDiffusion"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _DiffusionBlend;
                float _RadiusPixels;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 stepUV = _BlitTexture_TexelSize.xy * _RadiusPixels;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                // Normalized nine-tap kernel: softens rather than adding exposure or doubling bloom.
                half3 soft = source.rgb * 4.0h;
                soft += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(stepUV.x, 0)).rgb * 2.0h;
                soft += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(stepUV.x, 0)).rgb * 2.0h;
                soft += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0, stepUV.y)).rgb * 2.0h;
                soft += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0, stepUV.y)).rgb * 2.0h;
                soft += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + stepUV).rgb;
                soft += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - stepUV).rgb;
                soft += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(stepUV.x, -stepUV.y)).rgb;
                soft += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-stepUV.x, stepUV.y)).rgb;
                return half4(lerp(source.rgb, soft / 16.0h, saturate(_DiffusionBlend)), source.a);
            }
            ENDHLSL
        }
    }
}
