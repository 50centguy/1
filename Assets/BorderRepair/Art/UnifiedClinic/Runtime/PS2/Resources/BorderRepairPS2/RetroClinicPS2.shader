Shader "Hidden/BorderRepair/RetroClinicPS2"
{
    Properties
    {
        _PS2Strength("Effect strength", Range(0, 1)) = 0.85
        _PS2Lines("Virtual vertical resolution", Range(120, 1080)) = 448
        _PS2PixelSharpness("Virtual pixel edge sharpness", Range(0, 1)) = 0.35
        _PS2ColorLevels("Color levels per channel", Range(4, 256)) = 32
        _PS2Dither("Ordered dither amount", Range(0, 1)) = 0.6
        _PS2ChromaBleed("Composite chroma bleed (virtual pixels)", Range(0, 3)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always
        Pass
        {
            Name "PS2Look"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _PS2Strength;
                float _PS2Lines;
                float _PS2PixelSharpness;
                float _PS2ColorLevels;
                float _PS2Dither;
                float _PS2ChromaBleed;
            CBUFFER_END
            // Visor glass curvature, set globally by the desktop clinic's HUD (RepairProps HudLens); 0 elsewhere.
            // Output pixel q shows source q * (1 + k |q|^2) in [-1, 1] screen space: a CRT-style barrel, the
            // inverse of the warp the HUD applies to its own vertices, so overlay marks stay on their objects.
            float _VisorBarrel;

            // 4x4 ordered (Bayer) threshold, the same family of pattern the PS2 GS used when dithering to
            // 16-bit. Built arithmetically from two 2x2 levels to avoid indexed local arrays. Mean is ~0.
            float Bayer4(float2 cell)
            {
                float2 c = fmod(cell, 4.0);
                float2 lo = fmod(c, 2.0);
                float2 hi = floor(c * 0.5);
                float b2lo = 2.0 * abs(lo.x - lo.y) + lo.y;
                float b2hi = 2.0 * abs(hi.x - hi.y) + hi.y;
                return (4.0 * b2lo + b2hi) / 16.0 - 0.46875;
            }

            float3 SampleGrid(float2 cellCenter, float2 grid)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, cellCenter / grid).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float glass = 1.0;
                if (_VisorBarrel > 0.0001)
                {
                    float2 q = uv * 2.0 - 1.0;
                    uv = q * (1.0 + _VisorBarrel * dot(q, q)) * 0.5 + 0.5;
                    float2 edge = min(uv, 1.0 - uv);
                    glass = saturate(min(edge.x, edge.y) * 120.0);
                    uv = saturate(uv);
                }
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);

                // Square virtual pixels keep the aspect ratio; never upsample beyond the real target.
                float2 size = _BlitTexture_TexelSize.zw;
                float cell = max(1.0, size.y / max(_PS2Lines, 1.0));
                float2 grid = size / cell;

                // Reconstruct from the 4 nearest virtual pixel centres. Sharpness 0 = soft bilinear (console
                // output filter), 1 = near-nearest blocks. At cell 1 this reproduces the source exactly.
                float2 p = uv * grid - 0.5;
                float2 i0 = floor(p);
                float2 f = p - i0;
                float slope = lerp(1.0, 10.0, saturate(_PS2PixelSharpness));
                f = saturate((f - 0.5) * slope + 0.5);
                float3 a = SampleGrid(i0 + float2(0.5, 0.5), grid);
                float3 b = SampleGrid(i0 + float2(1.5, 0.5), grid);
                float3 c = SampleGrid(i0 + float2(0.5, 1.5), grid);
                float3 d = SampleGrid(i0 + float2(1.5, 1.5), grid);
                float3 color = lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);

                // Composite-video output: luma keeps the virtual pixel, chroma smears horizontally (the colour
                // fringing of a PS2 on a TV). YCoCg keeps it cheap; zero bleed leaves the colour untouched.
                if (_PS2ChromaBleed > 0.001)
                {
                    float2 step = float2(_PS2ChromaBleed / grid.x, 0.0);
                    float3 l = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - step).rgb;
                    float3 r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + step).rgb;
                    float3 blur = (l + color * 2.0 + r) * 0.25;
                    float y = dot(color, float3(0.25, 0.5, 0.25));
                    float co = 0.5 * (blur.r - blur.b);
                    float cg = -0.25 * blur.r + 0.5 * blur.g - 0.25 * blur.b;
                    color = max(0.0, float3(y + co - cg, y + cg, y - co - cg));
                }

                // Quantize only the displayable range in display (sRGB) space; HDR excess above 1 is kept so
                // bright screens and bloom cores are not flattened into one band.
                float levels = _PS2ColorLevels - 1.0;
                if (levels < 254.5)
                {
                    float3 over = max(color - 1.0, 0.0);
                    float3 ldr = saturate(color);
                    #if !defined(UNITY_COLORSPACE_GAMMA)
                    ldr = LinearToSRGB(ldr);
                    #endif
                    float threshold = Bayer4(floor(uv * grid)) * saturate(_PS2Dither);
                    ldr = saturate(floor(ldr * levels + 0.5 + threshold) / levels);
                    #if !defined(UNITY_COLORSPACE_GAMMA)
                    ldr = SRGBToLinear(ldr);
                    #endif
                    color = ldr + over;
                }

                return half4(lerp(source.rgb, (half3)color, saturate(_PS2Strength)) * glass, source.a);
            }
            ENDHLSL
        }
    }
}
