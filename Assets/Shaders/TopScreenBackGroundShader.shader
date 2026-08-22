Shader "Unlit/TopScreenBackGroundShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // URPにおける標準的なタグ設定（Opaque）
        Tags 
        { 
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent" 
            "Queue" = "Transparent"
        }
        LOD 100
        
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "UnlitPass"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 vertex   : POSITION;
                float2 uv       : TEXCOORD0;
            };

            struct Varyings
            {
                float4 vertex   : SV_POSITION;
                float2 uv       : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            Texture2D _MainTex;
            SamplerState sampler_MainTex;

            Varyings vert (Attributes input)
            {
                Varyings o;
                o.vertex = TransformObjectToHClip(input.vertex.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 col = _MainTex.Sample(sampler_MainTex, i.uv);
                col.w = 0.3;
                return col;
            }
            ENDHLSL
        }
    }
}