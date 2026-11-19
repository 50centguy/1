// 3D TextMesh labels that respect walls. Unity's default font material (GUI/Text Shader) draws on top of everything, so
// a sign outside showed through the basement wall, mirrored. This one depth-tests, does not write depth, and culls the
// back so a label read from behind disappears instead of showing mirrored. Fogged like the scene.
Shader "BorderRepair/Interior/DepthText"
{
    Properties
    {
        _MainTex ("Font texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            CBUFFER_END
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; half4 c : COLOR; };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; half4 c : COLOR; half fog : TEXCOORD1; };
            V vert(A a)
            {
                V o;
                o.cs = TransformObjectToHClip(a.p.xyz);
                o.uv = TRANSFORM_TEX(a.uv, _MainTex);
                o.c = a.c * _Color;
                o.fog = ComputeFogFactor(o.cs.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 c = i.c;
                c.a *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                c.rgb = MixFog(c.rgb, i.fog);
                return c;
            }
            ENDHLSL
        }
    }
}
