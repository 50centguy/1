Shader "WinterNight/StreetSurface" {
Properties { _WeatherTex("Surface",2D)="white"{} _Scale("Metres per tile",Float)=3 _Panel("Sign",Float)=0 _Kind("Surface type",Float)=0 }
SubShader { Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Pass {
Cull Off
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
struct V {float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;};
struct F {float4 p:SV_POSITION;float3 w:TEXCOORD0;float3 n:TEXCOORD1;float2 uv:TEXCOORD2;};
TEXTURE2D(_WeatherTex); SAMPLER(sampler_WeatherTex);float _Scale,_Panel,_Kind;
F vert(V i){F o;o.w=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(i.n);o.uv=i.uv;return o;}
float pool(float3 a,float3 b,float s){return exp(-dot(a-b,a-b)/s);}
float hash2(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.545);}
float noise2(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash2(a),hash2(a+float2(1,0)),f.x),lerp(hash2(a+float2(0,1)),hash2(a+1),f.x),f.y);}
half4 frag(F i):SV_Target{
float3 an=abs(normalize(i.n));float2 uv=an.y>max(an.x,an.z)?i.w.xz:(an.x>an.z?i.w.zy:i.w.xy);
float3 albedo=SAMPLE_TEXTURE2D(_WeatherTex,sampler_WeatherTex,_Panel>.5?i.uv:uv/_Scale).rgb;
float2 p=i.w.xz;
float broad=noise2(p*.63),med=noise2(p*3.1),fine=noise2(p*13.0);
if(_Kind>0.5 && _Kind<2.5){
 float pathX=-1.49+.13*sin(p.y*1.6);
 float worn=1-smoothstep(.33,1.04,abs(p.x-pathX));
 float lane=p.y+4.15+.13*sin(p.x*.53);
 float drift=lerp(-.08,.09,med);float tire=(1-smoothstep(.095,.28,min(abs(lane-.68+drift),abs(lane+.68+drift))))*(.44+.48*noise2(p*float2(.9,2.1))+.08*sin(p.x*42));
 float wornArea=_Kind<1.5?worn:max(tire,.27*(1-smoothstep(.4,1.4,abs(lane))));
 float coverage=smoothstep(.30,.61,.18+.49*broad+.23*med+.10*fine-.58*wornArea);
 if(i.n.y<.65)coverage*=.06;
 float3 wet=float3(.065,.076,.08)*(.71+.37*med+.13*fine);
 float3 powder=float3(.47,.54,.60)*(.67+.20*broad+.15*med+.12*fine);
 albedo=lerp(wet,powder,coverage);
 }
float3 illum=float3(.31,.375,.47)*(.72+.44*saturate(i.n.y)+.20*abs(i.n.z));
float lamp=pool(i.w,float3(-1.48,2.95,-.6),2.8)+pool(i.w,float3(-.64,1.4,-.7),1.2);
for(int j=0;j<3;j++)lamp+=2.65*pool(i.w,float3(-23-9*j,1.42,-5.42),5.6);
illum+=float3(.96,.50,.19)*lamp;
float3 rgb=albedo*(_Panel>.5?float3(.99,.98,.82):illum);
float dist=length(_WorldSpaceCameraPos-i.w);float fog=1-exp(-dist*dist*.00039);
return half4(lerp(rgb,float3(.043,.059,.081),fog),1);}
ENDHLSL
} } }


