Shader "WinterNight/FlakeClusters" {SubShader {Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"} Pass { Blend SrcAlpha OneMinusSrcAlpha ZWrite Off
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct V{float4 p:POSITION;float3 n:NORMAL;float4 c:COLOR;};struct F{float4 p:SV_POSITION;float4 c:COLOR;};
F vert(V v){F o;float3 w=TransformObjectToWorld(v.p.xyz);o.p=TransformWorldToHClip(w);float l=.57+.42*saturate(dot(TransformObjectToWorldNormal(v.n),normalize(float3(-.3,.9,.4))));o.c=v.c*float4(l,l,l,.73*smoothstep(.34,1.1,length(w-_WorldSpaceCameraPos)));return o;}
half4 frag(F i):SV_Target{return i.c;}
ENDHLSL
} } }
