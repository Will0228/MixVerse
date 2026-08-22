Shader "TextMeshPro/URP/GlitchShader"
{
    Properties
    {
        _FaceTex("Font Texture", 2D) = "white" {}
        _FaceColor("Text Color", Color) = (1,1,1,1)
        _FaceDilate("Face Dilate", Range(-1,1)) = 0

        // グリッチ専用パラメータ
        _GlitchIntensity ("Glitch Intensity", Range(0, 1)) = 0.1
        _GlitchSpeed ("Glitch Speed", Range(1, 50)) = 20.0
        _BlockSize ("Block Size", Range(1, 100)) = 20.0 // y方向のブロック（帯）の細かさ

        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _CullMode("Cull Mode", Float) = 0
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

        Cull [_CullMode]
        ZWrite Off
        Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "TextMeshPro Glitch Pass"

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
                float4 _FaceColor;
                float4 _FaceTex_ST;
                float _FaceDilate;
                float _GlitchIntensity;
                float _GlitchSpeed;
                float _BlockSize;
            CBUFFER_END

            Texture2D _FaceTex;
            SamplerState sampler_FaceTex;

            float Random(float2 st)
            {
                return frac(sin(dot(st.xy, float2(12.9898, 78.233))) * 43758.5453123);
            }

            Varyings vert (Attributes v)
            {
                Varyings o;

                // 1. y座標とブロックサイズを組み合わせて、行（水平の帯）ごとにIDを算出
                float lineID = floor(v.vertex.y * _BlockSize);

                // 2. 時間と行IDを掛け合わせたランダムな揺れを生成
                float timeKey = floor(_Time.y * _GlitchSpeed);
                float randomVal = Random(float2(lineID, timeKey));

                // 3. 一定の確率（例えば20%の確率）で大きく横にズレるようなグリッチの強弱を作る
                float glitchTrigger = step(0.8, Random(float2(timeKey, 2.0))); 
                float offsetX = (randomVal - 0.5) * _GlitchIntensity * 15.0 + (glitchTrigger * (randomVal - 0.5) * 40.0);

                // 4. x座標にのみオフセットを加算
                v.vertex.x += offsetX;

                o.positionCS = TransformObjectToHClip(v.vertex.xyz);
                o.color = v.color * _FaceColor;
                o.uv = v.texcoord;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 texColor = _FaceTex.Sample(sampler_FaceTex, i.uv);
                
                half c = texColor.a;
                half sd = 0.5 - c - _FaceDilate;
                half alpha = saturate(1.0 - sd / 0.05);

                half4 finalColor = i.color;
                finalColor.a *= alpha;

                return finalColor;
            }
            ENDHLSL
        }
    }
}