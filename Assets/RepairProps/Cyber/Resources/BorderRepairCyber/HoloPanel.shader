// Floating hologram panel for world-space UI (Image / legacy Text / HoloWidget): additive light that respects the depth
// buffer, so NPCs, hands and carried items pass in front of it. Panels float 10-24 cm off their surface, which clears
// the wall's pipes and shelves without ignoring depth. Vertex colour carries the Graphic colour and the CanvasGroup
// alpha; _TextureSampleAdd turns Alpha8 font atlases into white glyphs.
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
//   * procedural motion graphics on HoloWidget quads (see the Widget section): edges one screen pixel wide, feature
//     sizes chosen to survive the PS2 pass, levels at least two colour steps apart
//   * glyph strokes spread to about one PS2 virtual pixel: that pass takes one sample per virtual pixel with no
//     prefilter, so a stroke thinner than the cell (DotGothic16's is 1/16 em) simply drops out of some cells
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
        _Stroke("Stroke Spread (PS2 virtual pixels)", Range(0, 1)) = 0.45
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth Test", Float) = 4
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
            ZTest [_ZTest]
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
                float _Stroke;
            CBUFFER_END
            half4 _TextureSampleAdd;
            float4 _MainTex_TexelSize;
            float _BRHoloTime;
            float _BRHoloLines;     // the PS2 pass's virtual lines, 0 when it is off (set by ClinicSignageRuntime)

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 uv1 : TEXCOORD1;     // HoloWidget: kind + 1000, seed, a, b (zero on Image / Text)
                float4 uv2 : TEXCOORD2;     // HoloWidget: width mm, height mm, c, d
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 panel : TEXCOORD1;   // canvas-space position (mm), before any glitch offset
                float glitch : TEXCOORD2;   // 1 during a tear slot
                float4 widgetA : TEXCOORD3;
                float4 widgetB : TEXCOORD4;
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
            // Sideways slip (mm) of a point at height ny: the tear band, and each row's jitter while materialising.
            float RowShift(float ny, float halfWidth, float slot, float seed)
            {
                float glitch = Glitch(slot);
                float dir = Hash11(slot * 2.3 + seed) > .5 ? 1.0 : -1.0;
                float shift = glitch * dir * (TearBand(ny, slot, seed) * 7.0 - 1.5);
                float settle = 1.0 - saturate(_Reveal);
                settle *= settle;
                float row = floor((ny + 1.0) * 12.0);
                return shift + (Hash11(row * 7.3 + slot * 1.9 + seed) * 2.0 - 1.0) * settle * .06 * halfWidth;
            }

            float WidgetKind(float encoded) { return round(encoded) - 1000.0; }
            static const float KindBracket = 10.0;
            // The corner brackets' slow breath, 0..1: they ease out a few mm and brighten together.
            float BracketBreath(float period, float seed) { return .5 + .5 * sin(_BRHoloTime * 6.28318 / period + seed); }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p = input.positionOS.xyz;
                float2 halfSize = max(_Panel.xy, 1.0);
                float2 n = p.xy / halfSize;
                float slot = Slot(), seed = Seed();
                float kind = WidgetKind(input.uv1.x);

                o.panel = p.xy;
                o.glitch = Glitch(slot);
                // Widgets slip per fragment instead (a quad's corners would only shear it).
                if (kind < .5) p.x += RowShift(n.y, halfSize.x, slot, seed);
                else if (abs(kind - KindBracket) < .5) p.xy += input.uv1.w * input.uv2.zw * BracketBreath(input.uv1.z, seed);
                o.positionCS = TransformObjectToHClip(p);
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                o.color = input.color * _Color;
                o.widgetA = input.uv1;
                o.widgetB = input.uv2;
                return o;
            }

            // ---- Widget ----------------------------------------------------------------------------------------------
            // p is the position inside the widget in mm from its lower left corner, px one screen pixel in mm. Everything
            // returns a level 0..1 multiplied into the widget's tint. Hard() is a threshold one screen pixel wide: crisp
            // up close, and the PS2 pass does the rest. Levels stay at .25 or above so they clear two of its colour steps.
            float Hard(float d, float px) { return saturate(d / px + .5); }
            float Box(float2 p, float2 centre, float2 halfSize, float px)
            {
                float2 q = halfSize - abs(p - centre);
                return Hard(min(q.x, q.y), px);
            }
            // A chevron whose tip points to +x, tip at x = tip.
            float Chevron(float2 q, float halfHeight, float slope, float tip, float stroke, float px)
            {
                float d = abs(q.x + abs(q.y) * slope - tip) / sqrt(1.0 + slope * slope);
                return Hard(stroke - d, px) * Hard(halfHeight - abs(q.y), px);
            }
            // Repeating line across coord; a line thinner than a pixel dims rather than twinkling as it drifts.
            float GridLine(float coord, float period, float halfWidth, float px)
            {
                float d = abs(frac(coord / period + .5) - .5) * period;
                return Hard(halfWidth - d, px) * min(1.0, 2.0 * halfWidth / px);
            }

            // Segmented level bars: a = bars, b = segments, c = changes per second, d = level of an unlit segment.
            float WSpectrum(float2 p, float2 size, float px, float seed, float bars, float segments, float rate, float track)
            {
                float2 cells = float2(bars, segments);
                float2 f = p / size * cells;
                float2 index = floor(f), cell = f - index - .5;
                float2 pitch = size / cells;
                float solid = Hard((.36 - abs(cell.x)) * pitch.x, px) * Hard((.34 - abs(cell.y)) * pitch.y, px);
                float q = _BRHoloTime * rate + index.x * .61 + seed;
                float i = floor(q), s = q - i;
                s = s * s * (3.0 - 2.0 * s);
                float value = lerp(Hash12(float2(index.x + seed * 7.0, i)), Hash12(float2(index.x + seed * 7.0, i + 1.0)), s);
                float envelope = .45 + .55 * sin(3.14159 * (index.x + .5) / bars);
                float level = .12 + .85 * value * envelope;
                return solid * lerp(track, 1.0, step(index.y + .5, level * segments));
            }

            float ScopeTrace(float x, float width, float height, float seed)
            {
                float t = _BRHoloTime, u = x * .012;
                float y = .50 * sin(u + t * .9 + seed) + .26 * sin(u * 2.7 - t * 1.4 + seed * 2.0) + .10 * sin(u * 7.3 + t * 2.1);
                // A pulse crosses the screen every ~7 s (the diagnostic read-out's heartbeat).
                float d = (x - (frac(t * .14 + seed * .1) * 1.3 - .15) * width) / 22.0;
                y += 1.1 * exp(-d * d) * sin(d * 2.6);
                return height * (.5 + .36 * clamp(y, -1.25, 1.25));
            }
            // Oscilloscope over a drifting graticule: a = grid mm, b = drift mm/s, c = trace half width mm.
            float WScope(float2 p, float2 size, float px, float seed, float grid, float drift, float halfWidth)
            {
                float gx = GridLine(p.x + _BRHoloTime * drift, grid, 2.5, px);
                float gy = GridLine(p.y, grid, 2.5, px);
                float y = ScopeTrace(p.x, size.x, size.y, seed);
                float slope = (ScopeTrace(p.x + 2.0, size.x, size.y, seed) - ScopeTrace(p.x - 2.0, size.x, size.y, seed)) * .25;
                float trace = Hard(halfWidth - abs(p.y - y) / sqrt(1.0 + slope * slope), px);
                return max(trace, max(gx, gy) * .28);
            }

            // Tick ruler: a = tick period mm, b = drift mm/s, c = a major tick every c ticks, d = 1 for vertical.
            float WRuler(float2 p, float2 size, float px, float period, float drift, float major, float vertical)
            {
                float along = vertical > .5 ? p.y : p.x, across = vertical > .5 ? p.x : p.y;
                float span = vertical > .5 ? size.x : size.y;
                float s = (along + _BRHoloTime * drift) / period;
                float index = floor(s);
                float isMajor = step(fmod(index, major), .5);
                float tick = Hard(max(period * .16, 2.5) - abs(s - index - .5) * period, px);
                tick *= Hard(lerp(.5, 1.0, isMajor) * span - across, px) * lerp(.55, 1.0, isMajor);
                return max(tick, Hard(2.5 - across, px) * .6);
            }

            // Filling segment bar: a = segments, b = seconds per fill, c = fraction of the cycle spent full, d = level of
            // an empty segment. The front segment sits at a middle level.
            float WSegmentFill(float2 p, float2 size, float px, float seed, float segments, float period, float hold, float track)
            {
                float fill = saturate(frac(_BRHoloTime / period + seed * .13) / max(1.0 - hold, .01)) * segments;
                float f = p.x / size.x * segments, index = floor(f), pitch = size.x / segments;
                float solid = Hard((.38 - abs(f - index - .5)) * pitch, px) * Hard(size.y * .5 - abs(p.y - size.y * .5), px);
                float level = index + 1.0 <= fill ? 1.0 : (index < fill ? .65 : track);
                return solid * level;
            }

            // Marching chevrons: a = count, b = direction (+1 / -1), c = cells per second.
            float WChevrons(float2 p, float2 size, float px, float count, float direction, float rate)
            {
                float pitch = size.x / count, f = p.x / pitch, index = floor(f);
                float2 q = float2((f - index - .5) * pitch * direction, p.y - size.y * .5);
                float halfHeight = size.y * .5;
                float slope = pitch * .34 / halfHeight;
                float shape = Chevron(q, halfHeight, slope, pitch * .17, max(pitch * .11, 3.5), px);
                float k = frac((index * direction - _BRHoloTime * rate) / count);
                float one = 1.0 / count;
                return shape * (k < one ? 1.0 : (k > 1.0 - 2.0 * one ? .6 : .3));
            }

            // Status dial: outer ring, rotating ticks lit by a sweep, slow blink at the hub. a = ticks, b = revolutions
            // per second, c = fraction of the ticks lit by the sweep.
            float WRing(float2 p, float2 size, float px, float seed, float ticks, float revolutions, float sweep)
            {
                float radius = min(size.x, size.y) * .5;
                float2 q = p - size * .5;
                float r = length(q) / radius;
                float angle = atan2(q.y, q.x) / 6.28318 + .5;
                float outer = Hard((.10 - abs(r - .88)) * radius, px);
                float cell = abs(frac((angle + _BRHoloTime * revolutions) * ticks) - .5);
                float spoke = Hard((.30 - cell) * 6.28318 * r * radius / ticks, px) * Hard((.15 - abs(r - .58)) * radius, px);
                float swept = step(frac(angle - _BRHoloTime * revolutions * 2.0), sweep);
                float hub = Hard((.22 - r) * radius, px) * lerp(.3, 1.0, step(frac(_BRHoloTime / 2.4 + seed), .7));
                return max(outer * .8, max(spoke * lerp(.35, 1.0, swept), hub));
            }

            // A chevron stepping down a list, in the margin beside the rows: a = rows, b = seconds per row.
            float WRowCursor(float2 p, float2 size, float px, float seed, float rows, float dwell)
            {
                float f = (size.y - p.y) / size.y * rows, index = floor(f);
                float clock = _BRHoloTime / dwell + seed * .19;
                if (abs(index - fmod(floor(clock), rows)) > .5) return 0.0;
                float pitch = size.y / rows, inRow = (f - index) * pitch;
                float halfHeight = min(size.x * .75, pitch * .28);
                float2 q = float2(p.x - size.x * .5, pitch * .42 - inRow);
                return Chevron(q, halfHeight, size.x * .6 / halfHeight, size.x * .3, 4.5, px);
            }

            // A rule with a node at each end and packets running along it: a = packet length mm, b = mm per second,
            // c = packet spacing mm. The line is about a virtual pixel thick at 3 m, so it holds under the PS2 pass.
            float WFlowRule(float2 p, float2 size, float px, float seed, float packet, float speed, float spacing)
            {
                float mid = size.y * .5;
                float line_ = Hard(5.0 - abs(p.y - mid), px) * .35;
                float nodes = max(Box(p, float2(mid, mid), mid.xx, px), Box(p, float2(size.x - mid, mid), mid.xx, px));
                float s = frac((p.x - _BRHoloTime * speed) / spacing + seed) * spacing;
                float running = Hard(packet * .5 - abs(s - packet * .5), px) * Hard(8.0 - abs(p.y - mid), px);
                return max(line_, max(nodes, running));
            }

            // How far a glyph stroke spreads, in screen pixels: a fraction of one virtual pixel, 0 when the PS2 pass is off
            // or does not downsample. Follows the screen, so a near sign keeps its fine strokes and a far one holds together.
            float StrokeSpread()
            {
                float cell = _BRHoloLines > .5 ? _ScreenParams.y / _BRHoloLines : 1.0;
                return cell > 1.0 ? cell * _Stroke : 0.0;
            }
            // The strongest glyph coverage in a 3x3 around uv: a dilation, so a stroke keeps one even brightness (stacked
            // additive Outline copies would blow out its core). LOD 0 because it runs inside a branch; the atlas has no mips.
            half SpreadInk(float2 uv, float2 x, float2 y)
            {
                half ink = 0.0;
                for (int i = -1; i <= 1; i++)
                    for (int j = -1; j <= 1; j++)
                        ink = max(ink, (SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv + x * i + y * j, 0) + _TextureSampleAdd).a);
                return ink;
            }

            float Widget(float kind, float2 p, float2 size, float px, float seed, float a, float b, float c, float d)
            {
                if (kind < 2.5) return WSpectrum(p, size, px, seed, a, b, c, d);
                if (kind < 3.5) return WScope(p, size, px, seed, a, b, c);
                if (kind < 4.5) return WRuler(p, size, px, a, b, c, d);
                if (kind < 5.5) return WSegmentFill(p, size, px, seed, a, b, c, d);
                if (kind < 6.5) return WChevrons(p, size, px, a, b, c);
                if (kind < 7.5) return WRing(p, size, px, seed, a, b, c);
                if (kind < 8.5) return WRowCursor(p, size, px, seed, a, b);
                if (kind < 9.5) return WFlowRule(p, size, px, seed, a, b, c);
                return .72 + .4 * BracketBreath(a, Seed());   // Bracket: a is the breath period, b its reach in mm
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _BRHoloTime, slot = Slot(), seed = Seed();
                float2 halfSize = max(_Panel.xy, 1.0);
                float2 n = input.panel / halfSize;
                float reveal = saturate(_Reveal);
                float settle = 1.0 - reveal;
                float2 du = ddx(input.uv);   // one screen pixel along x, in atlas uv
                float2 dv = ddy(input.uv);

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
                float kind = WidgetKind(input.widgetA.x);
                // Only full-strength lettering spreads: the plate, its rim and the Outline halo are all under half alpha.
                float spread = StrokeSpread();
                if (kind < .5 && input.color.a > .5 && spread > 0.0)
                    s.a = max(s.a, SpreadInk(uv, du * spread, dv * spread) * input.color.a);
                half aR = (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + off) + _TextureSampleAdd).a;
                half aB = (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - off) + _TextureSampleAdd).a;
                half aC = (s.a / max(input.color.a, 1e-4));
                half3 rgb = s.rgb * s.a;
                // Hot core: whiten the solid body of full-strength glyphs. Weighted by s.a squared, so the 8 % glow
                // plate and the 25 % Outline halo copies stay at their saturated tint (core vs rim hue separation).
                rgb += (s.a - rgb) * (_Core * aC * s.a * s.a);
                half fringe = lerp(.30, .60, saturate(tear + settle));
                rgb += (half3(1.0, .22, .32) * saturate(aR - aC) + half3(.22, .78, 1.0) * saturate(aB - aC)) * input.color.a * fringe;

                // Procedural graphic instead of a sprite / glyph. Derivatives are taken above and passed in, so these
                // branches stay real branches and only widget quads pay for them.
                if (kind > .5)
                {
                    float2 size = max(input.widgetB.xy, 1.0);
                    float2 mm = input.uv * size;
                    mm.x += RowShift(n.y, halfSize.x, slot, seed);
                    float px = max(max(length(du * size), length(dv * size)), 1e-3);
                    float inside = step(0.0, mm.x) * step(mm.x, size.x);
                    float level = Widget(kind, mm, size, px, input.widgetA.y, input.widgetA.z, input.widgetA.w, input.widgetB.z, input.widgetB.w);
                    rgb = input.color.rgb * input.color.a * level * inside;
                }

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
