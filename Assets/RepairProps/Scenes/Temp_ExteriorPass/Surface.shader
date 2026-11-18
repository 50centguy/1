// The district's large surfaces (walls, ground snow, the packed-snow relief, harbour water), lit by the scene's real
// lights and fogged with the scene fog. Textures (CC0 scans, see Docs/Art/EXTERIOR_BERTH04_PASS.md) are mapped in world
// space on the dominant axis, so a 54 m wall and a 0.3 m sill share one texel density, with a matching normal map.
// Ground is three layers blended by a snow-depth field: full snow, a thin speckled layer, and the wet ground under it.
// Feet and tyres thin the snow; the edges are broken by the thin layer's own grain so they fade instead of cutting.
// Blinn-Phong (Simple Lit) to stay in the PS2 register.
// _Kind: 0 solid (texture x tint, optional snow on upward faces), 1 trodden pavement and steps, 2 road and quay with
//        tyre tracks, 3 water, 4 packed-snow relief (vertex colour R = clean snow).
Shader "BorderRepair/Exterior/Surface"
{
    Properties
    {
        _BaseMap ("Texture (world mapped, mid-grey mean)", 2D) = "grey" {}
        [Normal] _NormalMap ("Normal map", 2D) = "bump" {}
        _NormalScale ("Normal strength", Float) = 1
        _BaseColor ("Tint (albedo at texture mean)", Color) = (.4, .45, .48, 1)
        _Scale ("Metres per tile", Float) = 3
        _Kind ("Surface kind", Float) = 0
        _SnowColor ("Snow", Color) = (.6, .66, .7, 1)
        _SnowMap ("Snow texture", 2D) = "grey" {}
        [Normal] _SnowNormal ("Snow normal", 2D) = "bump" {}
        _SnowScale ("Snow metres per tile", Float) = 2.2
        _SnowDetail ("Snow texture strength", Range(0, 1)) = .7
        _ThinMap ("Thin snow over ground", 2D) = "grey" {}
        _ThinScale ("Thin metres per tile", Float) = 1.6
        _GroundMap ("Ground under the snow", 2D) = "grey" {}
        [Normal] _GroundNormal ("Ground normal", 2D) = "bump" {}
        _GroundColor ("Wet ground", Color) = (.12, .13, .14, 1)
        _GroundScale ("Ground metres per tile", Float) = 2.5
        _Cover ("Snow cover (higher = less ground shows)", Range(0, 1.5)) = .7
        _TopSnow ("Snow on upward faces (solid)", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = .2
        _Specular ("Specular strength", Range(0, 1)) = .15
        _WetSmoothness ("Wet ground smoothness", Range(0, 1)) = .5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST, _NormalMap_ST, _SnowMap_ST, _SnowNormal_ST, _ThinMap_ST, _GroundMap_ST, _GroundNormal_ST;
        half4 _BaseColor, _SnowColor, _GroundColor;
        float _Scale, _Kind, _SnowScale, _ThinScale, _GroundScale;
        half _NormalScale, _SnowDetail, _Cover, _TopSnow, _Smoothness, _Specular, _WetSmoothness;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
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
            TEXTURE2D(_NormalMap); TEXTURE2D(_SnowMap); TEXTURE2D(_SnowNormal);
            TEXTURE2D(_ThinMap); TEXTURE2D(_GroundMap); TEXTURE2D(_GroundNormal);
            #define SAMPLE(t, uv) SAMPLE_TEXTURE2D(t, sampler_BaseMap, uv)

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

            float3 Perturb(float3 nt, float3 T, float3 B, float3 N) { return normalize(T * nt.x + B * nt.y + N * nt.z); }

            half4 frag(V i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 N = normalize(i.n) * (IS_FRONT_VFACE(face, true, false) ? 1 : -1);
                float3 an = abs(N);
                float2 uv; float3 T;
                if (an.y > max(an.x, an.z)) { uv = i.w.xz; T = float3(1, 0, 0); }
                else if (an.x > an.z) { uv = i.w.zy; T = float3(0, 0, 1); }
                else { uv = i.w.xy; T = float3(1, 0, 0); }
                T = normalize(T - N * dot(T, N));
                float3 B = cross(N, T);

                half3 albedo = SAMPLE(_BaseMap, uv / _Scale).rgb * _BaseColor.rgb * 2;
                float3 n = Perturb(UnpackNormalScale(SAMPLE(_NormalMap, uv / _Scale), _NormalScale), T, B, N);
                float2 p = i.w.xz;
                // Snow, thin snow and ground use the same dominant-axis mapping as the base, so kerb faces and slab edges
                // are not smeared by a top-down projection.
                half3 snowTex = lerp(half3(.5, .5, .5), SAMPLE(_SnowMap, uv / _SnowScale).rgb, _SnowDetail);
                half3 snow = _SnowColor.rgb * snowTex * 2 * (.95 + .08 * noise2(p * .17));
                float3 nSnow = Perturb(UnpackNormalScale(SAMPLE(_SnowNormal, uv / _SnowScale), _SnowDetail), T, B, N);
                half up = saturate((N.y - .6) / .25);
                half smooth = _Smoothness;

                if (_Kind < .5)
                {
                    half s = up * _TopSnow;
                    albedo = lerp(albedo, snow, s);
                    n = normalize(lerp(n, nSnow, s));
                }
                else if (_Kind < 2.5 || _Kind > 3.5)
                {
                    // Where people and tyres went.
                    float worn;
                    if (_Kind < 1.5)
                    {
                        float pathX = -1.49 + .13 * sin(p.y * 1.6);
                        worn = .75 * (1 - smoothstep(.3, 1.0, abs(p.x - pathX)));
                        worn = max(worn, .4 * (1 - smoothstep(.25, .8, abs(p.y + 6.05 + .12 * sin(p.x * .7)))));   // the walking line along the near pavement
                    }
                    else if (_Kind < 2.5)
                    {
                        float med = noise2(p * 1.3);
                        float drift = lerp(-.08, .09, med);
                        float quay = p.y + 4.15 + .13 * sin(p.x * .53);
                        float road = p.y + 8.15 + .1 * sin(p.x * .37);
                        float tq = min(abs(quay - .68 + drift), abs(quay + .68 + drift));
                        float tr = min(abs(road - .72 + drift), abs(road + .72 + drift));
                        worn = .62 * (1 - smoothstep(.08, .38, min(tq, tr))) * (.75 + .25 * noise2(p * float2(.9, 2.1)));
                    }
                    else
                    {
                        worn = .55 * (1 - smoothstep(.018, .31, i.c.r));
                        up = max(up, .6);
                    }
                    // Snow depth: broad drifts and smaller scallops, thinned where worn; the thin layer's grain breaks the edges.
                    half3 thinTex = SAMPLE(_ThinMap, uv / _ThinScale).rgb;
                    half grit = dot(thinTex, half3(.333, .333, .333)) - .5;
                    float depth = _Cover + .34 * (noise2(p * .19) - .5) + .22 * (noise2(p * .63 + 7.3) - .5) + .1 * (noise2(p * 1.9 + 3.1) - .5);
                    depth -= worn * .95;
                    float d = depth + grit * .45;
                    half wThin = smoothstep(.06, .38, d);
                    half wSnow = smoothstep(.34, .68, d);
                    half3 groundTex = SAMPLE(_GroundMap, uv / _GroundScale).rgb;
                    half3 ground = _GroundColor.rgb * groundTex * 2;
                    half3 thin = lerp(_GroundColor.rgb, _SnowColor.rgb, .55) * thinTex * 2;
                    half3 top = lerp(lerp(ground, thin, wThin), snow, wSnow);
                    float3 nGround = Perturb(UnpackNormalScale(SAMPLE(_GroundNormal, uv / _GroundScale), 1), T, B, N);
                    float3 nTop = normalize(lerp(nGround, nSnow, wSnow));
                    half3 side = ground * 1.4;                         // kerbs and slab edges: wet, darker than the snow
                    albedo = lerp(_Kind > 3.5 ? lerp(thin, snow, .5) : side, top, up);
                    n = normalize(lerp(n, nTop, up));
                    smooth = lerp(smooth, lerp(_WetSmoothness, .35, wThin), (1 - wSnow) * up);
                }
                else
                {
                    smooth = _Smoothness;
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
