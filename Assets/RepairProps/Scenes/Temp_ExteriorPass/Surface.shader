// The district's large surfaces (precast concrete, painted iron, ground snow, the packed-snow relief, harbour water),
// lit by the scene's real lights and fogged with the scene fog. Textures are mapped in world space on the dominant axis,
// so a 54 m wall and a 0.3 m sill share one texel density. Ground snow is one surface: a near-flat snow colour with
// compacted slush only where feet and tyres went. Blinn-Phong (Simple Lit) to stay in the PS2 register.
// _Kind: 0 solid (texture x tint, optional snow on upward faces), 1 trodden steps, 2 road/quay snow with tyre lanes,
//        3 water, 4 packed-snow relief (vertex colour R = clean snow).
Shader "BorderRepair/Exterior/Surface"
{
    Properties
    {
        _BaseMap ("Texture (world mapped, neutral grey)", 2D) = "grey" {}
        _BaseColor ("Tint (albedo at texture mean)", Color) = (.4, .45, .48, 1)
        _Scale ("Metres per tile", Float) = 3
        _Kind ("Surface kind", Float) = 0
        _SnowColor ("Snow", Color) = (.74, .8, .84, 1)
        _SlushColor ("Compacted slush", Color) = (.17, .2, .22, 1)
        _TopSnow ("Snow on upward faces", Range(0, 1)) = 0
        _GrainMap ("Snow grain", 2D) = "grey" {}
        _GrainScale ("Grain metres per tile", Float) = 1.5
        _Smoothness ("Smoothness", Range(0, 1)) = .2
        _Specular ("Specular strength", Range(0, 1)) = .15
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST, _GrainMap_ST;
        half4 _BaseColor, _SnowColor, _SlushColor;
        float _Scale, _Kind, _GrainScale;
        half _TopSnow, _Smoothness, _Specular;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #define _SPECULAR_COLOR 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_GrainMap); SAMPLER(sampler_GrainMap);

            struct A { float4 p : POSITION; float3 n : NORMAL; float4 c : COLOR; };
            struct V { float4 cs : SV_POSITION; float3 w : TEXCOORD0; float3 n : TEXCOORD1; half4 c : COLOR; half fog : TEXCOORD2; };

            V vert(A a)
            {
                V o;
                o.w = TransformObjectToWorld(a.p.xyz);
                o.cs = TransformWorldToHClip(o.w);
                o.n = TransformObjectToWorldNormal(a.n);
                o.c = a.c;
                o.fog = ComputeFogFactor(o.cs.z);
                return o;
            }

            float hash2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.545); }
            float noise2(float2 p)
            {
                float2 a = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash2(a), hash2(a + float2(1, 0)), f.x), lerp(hash2(a + float2(0, 1)), hash2(a + 1), f.x), f.y);
            }

            half4 frag(V i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 n = normalize(i.n) * (IS_FRONT_VFACE(face, true, false) ? 1 : -1);
                float3 an = abs(n);
                float2 uv = an.y > max(an.x, an.z) ? i.w.xz : (an.x > an.z ? i.w.zy : i.w.xy);
                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv / _Scale).rgb;
                half3 albedo = tex * _BaseColor.rgb * 2;
                float2 p = i.w.xz;
                half grain = SAMPLE_TEXTURE2D(_GrainMap, sampler_GrainMap, p / _GrainScale).r;
                half up = saturate((n.y - .6) / .25);
                half3 snow = _SnowColor.rgb * (.96 + .05 * noise2(p * .17) + .5 * (grain - .5));
                half3 slush = _SlushColor.rgb * (.86 + .28 * noise2(p * 1.3)) * (.9 + .4 * (grain - .5));
                half smooth = _Smoothness;

                if (_Kind < .5)
                {
                    albedo = lerp(albedo, snow * .97, up * _TopSnow);
                }
                else if (_Kind < 2.5)
                {
                    float worn;
                    if (_Kind < 1.5)
                    {
                        float pathX = -1.49 + .13 * sin(p.y * 1.6);
                        worn = 1 - smoothstep(.33, 1.04, abs(p.x - pathX));
                    }
                    else
                    {
                        float med = noise2(p * 1.3);
                        float lane = p.y + 4.15 + .13 * sin(p.x * .53);
                        float drift = lerp(-.08, .09, med);
                        float tyre = (1 - smoothstep(.095, .28, min(abs(lane - .68 + drift), abs(lane + .68 + drift)))) * (.55 + .4 * noise2(p * float2(.9, 2.1)));
                        worn = max(tyre, .15 * (1 - smoothstep(.4, 1.4, abs(lane))));
                    }
                    half3 top = lerp(snow, slush, saturate(worn) * .7);
                    albedo = lerp(albedo * .8, top, up);   // kerb and slab sides keep their own (wet) surface
                    smooth = lerp(smooth, .45, saturate(worn) * up);
                }
                else if (_Kind < 3.5)
                {
                    smooth = _Smoothness;
                }
                else
                {
                    half clean = smoothstep(.018, .31, i.c.r);
                    albedo = lerp(lerp(slush, snow, .45), snow, clean);   // trodden reads as packed grey, not a hole
                    smooth = lerp(.45, _Smoothness, clean);
                }

                InputData id = (InputData)0;
                id.positionWS = i.w;
                id.positionCS = i.cs;
                id.normalWS = n;
                id.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.w);
                id.shadowCoord = TransformWorldToShadowCoord(i.w);
                id.fogCoord = i.fog;
                id.bakedGI = SampleSH(n);
                id.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.cs);
                id.shadowMask = half4(1, 1, 1, 1);

                SurfaceData sd = (SurfaceData)0;
                sd.albedo = albedo;
                sd.specular = _Specular.xxx;
                sd.smoothness = smooth;
                sd.occlusion = 1;
                sd.alpha = 1;
                half4 c = UniversalFragmentBlinnPhong(id, sd);
                c.rgb = MixFog(c.rgb, id.fogCoord);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex sv
            #pragma fragment sf
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct A { float4 p : POSITION; float3 n : NORMAL; };
            float4 sv(A a) : SV_POSITION
            {
                float3 w = TransformObjectToWorld(a.p.xyz);
                float3 n = TransformObjectToWorldNormal(a.n);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 l = normalize(_LightPosition - w);
            #else
                float3 l = _LightDirection;
            #endif
                float4 c = TransformWorldToHClip(ApplyShadowBias(w, n, l));
            #if UNITY_REVERSED_Z
                c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
            #else
                c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return c;
            }
            half4 sf() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma vertex dv
            #pragma fragment df
            float4 dv(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half df() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
