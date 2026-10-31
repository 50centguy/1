// Floating hologram panel for world-space UI (Image / legacy Text): additive light that ignores the depth buffer, so
// pipes, shelves and machines between the panel and the camera never cut into the lettering. Vertex colour carries
// the Graphic colour and the CanvasGroup alpha; _TextureSampleAdd turns Alpha8 font atlases into white glyphs.
//
// Motion is all in here (no CPU mesh work): one material per panel carries its half-size in canvas units (mm) and a
// phase seed in _Panel, and _Reveal (0..1) while it materialises. The clock is the global _BRHoloTime (unscaled time,
// set once per frame by ClinicSignageRuntime), so a paused game never freezes a panel mid-glitch.
//   * hot core: the solid body of a glyph runs a little whiter than its tint, the soft halo stays saturated
//     (amber CRT / Destiny diagnostic references: yellow-white strokes, orange-red rim)
//   * glyph-edge RGB split (red one side, cyan the other): under a pixel at rest, a little wider towards the panel
//     edges; it only opens up wide inside a tear band (CP2077 transmission reference: fringes sit on the glitched
//     block, stable text stays clean)
//   * a soft, wide, weak brightness swell sweeping top to bottom every ~9 s (not static scan lines)
//   * slow per-panel breathing, a few percent
//   * a rare tear: one 33 ms slot every ~15-50 s per panel (rate set per sign), one horizontal band slides sideways
//   * materialise: rows jitter, blocks drop in, chroma wide, converging to clean text in under half a second
// Readability first: at rest the split is <= ~1 px and nothing moves the glyphs.
Shader "BorderRepair/HoloPanel"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        [HDR] _Color("Tint", Color) = (1, 1, 1, 1)
        _Intensity("Intensity", Float) = 1.3
        _Panel("Panel (half width mm, half height mm, seed, -)", Vector) = (500, 200, 0, 0)
        _Reveal("Reveal", Range(0, 1)) = 1
        _Chroma("Chroma Split (px)", Range(0, 4)) = 1
        _Core("Hot Core (whiten glyph body)", Range(0, 1)) = 0.3
        _Scan("Scan Band Gain", Range(0, 1)) = 0.15
        _Breath("Breathing Depth", Range(0, 0.2)) = 0.03
        _GlitchRate("Tears Per Second", Range(0, 1)) = 0.06
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Intensity;
                float4 _Panel;
                float _Reveal;
                float _Chroma;
                float _Core;
                float _Scan;
                float _Breath;
                float _GlitchRate;
            CBUFFER_END
            half4 _TextureSampleAdd;
            float4 _MainTex_TexelSize;
            float _BRHoloTime;

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 panel : TEXCOORD1;   // canvas-space position (mm), before any glitch offset
                float glitch : TEXCOORD2;   // 1 during a tear slot
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Dave Hoskins' sine-free hashes: stable on every GPU, no precision blow-up for large inputs.
            float Hash11(float p) { p = frac(p * .1031); p *= p + 33.33; p *= p + p; return frac(p); }
            float Hash12(float2 p) { float3 p3 = frac(p.xyx * .1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }

            // 30 Hz slots: one slot = two frames at 60 fps.
            float Slot() { return floor(_BRHoloTime * 30.0); }
            float Seed() { return _Panel.z; }
            float Glitch(float slot) { return step(Hash11(slot * 1.37 + Seed() * 17.13), _GlitchRate / 30.0); }
            // 1 inside this slot's tear band (n.y in -1..1 panel space); vertex and fragment agree on the band.
            float TearBand(float ny, float slot, float seed)
            {
                float bandCentre = Hash11(slot + seed * 3.1) * 2.0 - 1.0;
                float bandHalf = .10 + .15 * Hash11(slot * .71 + seed);
                return step(abs(ny - bandCentre), bandHalf);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p = input.positionOS.xyz;
                float2 halfSize = max(_Panel.xy, 1.0);
                float2 n = p.xy / halfSize;
                float slot = Slot(), seed = Seed();

                // Rare tear: one horizontal band slides sideways a few mm, the rest nudges 1.5 mm the other way.
                float glitch = Glitch(slot);
                float dir = Hash11(slot * 2.3 + seed) > .5 ? 1.0 : -1.0;
                float inBand = TearBand(n.y, slot, seed);
                float shift = glitch * dir * (inBand * 7.0 - 1.5);

                // Materialise: each row jitters sideways, settling quadratically as _Reveal reaches 1.
                float settle = 1.0 - saturate(_Reveal);
                settle *= settle;
                float row = floor((n.y + 1.0) * 12.0);
                shift += (Hash11(row * 7.3 + slot * 1.9 + seed) * 2.0 - 1.0) * settle * .06 * halfSize.x;

                o.panel = p.xy;
                o.glitch = glitch;
                p.x += shift;
                o.positionCS = TransformObjectToHClip(p);
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                o.color = input.color * _Color;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _BRHoloTime, slot = Slot(), seed = Seed();
                float2 halfSize = max(_Panel.xy, 1.0);
                float2 n = input.panel / halfSize;
                float reveal = saturate(_Reveal);
                float settle = 1.0 - reveal;
                float2 du = ddx(input.uv);   // one screen pixel along x, in atlas uv

                // Materialise: rows slip a few pixels inside their glyphs.
                float2 uv = input.uv;
                float rowF = floor((n.y + 1.0) * 30.0);
                uv += du * (Hash11(rowF * 3.7 + slot * .9 + seed) * 2.0 - 1.0) * settle * 6.0;

                // RGB split: centre sample for the body, side samples only add a thin fringe where they stick out.
                float edge = saturate(max(abs(n.x), abs(n.y)));
                // Tears are local: only the band that slid gets the wide fringe and the dip (stable text stays clean).
                float tear = input.glitch * TearBand(n.y, slot, seed);
                // The at-rest split is capped at 1.5 atlas texels so small, distant text never pulls in a neighbouring
                // glyph from the font atlas; only the brief tear / materialise extras may overshoot.
                float2 rest = du * _Chroma * (.4 + .6 * edge * edge);
                float restLen = length(rest / max(_MainTex_TexelSize.xy, 1e-6));
                rest *= min(1.0, 1.5 / max(restLen, 1e-4));
                float2 off = rest + du * (tear * 3.0 + settle * 4.0);
                half4 s = (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv) + _TextureSampleAdd) * input.color;
                half aR = (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + off) + _TextureSampleAdd).a;
                half aB = (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - off) + _TextureSampleAdd).a;
                half aC = (s.a / max(input.color.a, 1e-4));
                half3 rgb = s.rgb * s.a;
                // Hot core: whiten the solid body of full-strength glyphs. Weighted by s.a squared, so the 8 % glow
                // plate and the 25 % Outline halo copies stay at their saturated tint (core vs rim hue separation).
                rgb += (s.a - rgb) * (_Core * aC * s.a * s.a);
                half fringe = lerp(.30, .60, saturate(tear + settle));
                rgb += (half3(1.0, .22, .32) * saturate(aR - aC) + half3(.22, .78, 1.0) * saturate(aB - aC)) * input.color.a * fringe;

                // Scan swell: wide, weak, sweeps top to bottom over ~9 s, each panel at its own phase.
                float bandPos = 1.3 - frac(t / 9.0 + seed * .137) * 2.6;
                float bd = (n.y - bandPos) / .22;
                float band = exp(-bd * bd);

                // Breathing, a few percent, phase-offset per panel.
                float breathe = 1.0 + _Breath * sin(t * .8 + seed * 1.7) + _Breath * .4 * sin(t * 2.3 + seed);

                // Materialise: blocks drop in at random, brightness stutters, all done by _Reveal = 1.
                float keep = 1.0, stutter = 1.0;
                if (reveal < 1.0)
                {
                    float2 block = floor(input.panel / float2(14.0, 9.0));
                    keep = step(Hash12(block + float2(slot * .37, seed)), reveal * 1.25 - .02);
                    stutter = lerp(.35 + .65 * step(.4, Hash11(slot * 3.1 + seed)), 1.0, reveal * reveal);
                }

                float gain = _Intensity * breathe * (1.0 + _Scan * band) * (1.0 - .2 * tear) * keep * stutter;
                return half4(rgb * gain, 1.0);
            }
            ENDHLSL
        }
    }
}
