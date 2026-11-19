Shader "HarbourStudy/FallingSnow" {
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
 Pass { Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct V {float4 p:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
 struct F {float4 p:SV_POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;float distance:TEXCOORD1;};
 F vert(V v){F o;float3 w=TransformObjectToWorld(v.p.xyz);o.p=TransformWorldToHClip(w);o.color=v.color;o.uv=v.uv;o.distance=length(w-_WorldSpaceCameraPos);return o;}
 half4 frag(F i):SV_Target{
 float2 p=(i.uv-.5)*2;float a=1-smoothstep(.13,1,length(p*float2(1.10,.9)));
 a*=smoothstep(.28,1.4,i.distance)*(1-.8*smoothstep(10,40,i.distance));
 return half4(i.color.rgb,i.color.a*a);}
 ENDHLSL
 } } }
