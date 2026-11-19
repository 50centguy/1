Shader "WinterNight/OvercastSky" {
SubShader { Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" } Cull Off ZWrite Off Pass {
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct V{float4 p:POSITION;}; struct F{float4 p:SV_POSITION;float3 ray:TEXCOORD0;};
F vert(V v){F o;o.p=TransformObjectToHClip(v.p.xyz);o.ray=v.p.xyz;return o;}
float h(float2 p){return frac(sin(dot(p,float2(41.1,285.7)))*43758.5);}
float n(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(h(i),h(i+float2(1,0)),f.x),lerp(h(i+float2(0,1)),h(i+1),f.x),f.y);}
half4 frag(F f):SV_Target {float3 d=normalize(f.ray);float t=smoothstep(0,.58,d.y);float2 uv=d.xz/(max(d.y,0)+.16);float cloud=.55*n(uv*1.1)+.30*n(uv*2.7)+.15*n(uv*6);return half4(lerp(float3(.044,.060,.081),float3(.017,.025,.043),t)*lerp(.86,1.12,cloud),1);}
ENDHLSL
} } }
