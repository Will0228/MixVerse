Shader "UI/URP/SimpleOutlineShader"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint Color", Color) = (1,1,1,1)
        _OutlineColor ("Outline Color", Color) = (0,1,1,1)
        
        // アウトラインの太さ
        _OutlineWidth ("Outline Width", Range(0.01, 0.5)) = 0.1

        // UI用標準プロパティ（Canvas Rendererからの制御用）
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags 
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "RenderPipeline" = "UniversalPipeline"
        }

        Stencil
        {
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        ZWrite Off
        Lighting Off
        Fog { Mode Off }
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UI Simple Outline Pass"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
                float4 color    : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _OutlineColor;
                float4 _MainTex_ST;
                float _OutlineWidth;
            CBUFFER_END

            Texture2D _MainTex;
            SamplerState sampler_MainTex;

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.vertex);
                o.color = v.color * _Color;
                o.uv = v.texcoord;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // テクスチャのアルファ値をサンプリング
                half4 texColor = _MainTex.Sample(sampler_MainTex, i.uv);
                half alpha = texColor.a;

                // 境界線（エッジ）部分だけを抽出し、内部を透明にする
                half edge = step(alpha, 0.5 + _OutlineWidth) ;

                half4 finalColor = _OutlineColor * i.color;
                finalColor.a *= edge;

                return finalColor;
            }
            ENDHLSL
        }
    }
}