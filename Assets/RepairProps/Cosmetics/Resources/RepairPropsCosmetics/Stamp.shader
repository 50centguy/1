Shader "Hidden/RepairProps/CosmeticStamp"
{
    Properties { _Stamp("Stamp",2D)="white"{} }
    SubShader
    {
        Pass
        {
            Cull Off ZTest Always ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _Stamp;
            float4 _BrushColor, _UVTransform;
            float3 _Center, _Normal, _Right, _Up;
            float _Radius, _Opacity, _Decal;
            struct Input { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct Varying { float4 pos:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; };
            Varying vert(Input v)
            {
                Varying o;
                float2 uv=v.uv*_UVTransform.xy+_UVTransform.zw;
                o.pos=float4(uv*2-1,0,1);
                #if UNITY_UV_STARTS_AT_TOP
                o.pos.y=-o.pos.y;
                #endif
                o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                o.normal=UnityObjectToWorldNormal(v.normal);
                return o;
            }
            float4 frag(Varying i):SV_Target
            {
                float3 delta=i.world-_Center;
                clip(dot(normalize(i.normal),_Normal)-.55);
                clip(_Radius*.2-abs(dot(delta,_Normal)));
                float2 p=float2(dot(delta,_Right),dot(delta,_Up))/_Radius;
                float4 color=_BrushColor;
                if(_Decal>.5)
                {
                    clip(1-max(abs(p.x),abs(p.y)));
                    color=tex2D(_Stamp,p*.5+.5);
                }
                else
                {
                    clip(1-length(p));
                    color.a*=1-smoothstep(.6,1,length(p));
                }
                color.a*=_Opacity;
                return color;
            }
            ENDHLSL
        }
    }
}
