Shader "Unlit/CardDissolveShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _NoiseTex ("Noise Texture", 2D) = "white" {}
        _Threshold ("Threshold", Range(0, 1)) = 0.5
        _EdgeStepOffset ("Edge Step Offset", Float) = 0.01
        _EmissionColor ("Emission Color", Color) = (1, 0.5, 0, 1)
        _EmissionPower ("Emission Power", Float) = 3.0
    }
    SubShader
    {
        // clip() で抜くだけで半透明合成はしないため、Transparent ではなく AlphaTest に置く。
        // カードは全枚数がこのシェーダーで描かれるので、Transparent キューに入れると
        // 手札のように重なり合う板の描画順が距離ソート任せになり、ちらつく。
        Tags
        {
            "RenderType"="TransparentCutout"
            "RenderPipeline"="UniversalPipeline"
            "Queue" = "AlphaTest"
        }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attribute
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Threshold;
                float _EdgeStepOffset;
                float4 _EmissionColor;
                float _EmissionPower;
            CBUFFER_END

            Varyings vert (Attribute input)
            {
                Varyings o;
                o.vertex = TransformObjectToHClip(input.vertex);
                o.uv = input.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _BaseColor;
                half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.uv).r;
                clip(noise.r - _Threshold);
                
                float edge = step(noise.r, _Threshold + _EdgeStepOffset);

                // _Threshold が 0（実体）のままでも、ノイズの暗い部分は _EdgeStepOffset の
                // 範囲に入って光ってしまう。カードは常時このシェーダーで描かれるので、
                // 溶けている最中だけ縁を光らせる。
                float dissolving = step(0.001, _Threshold);
                half3 emissionColor = _EmissionColor.rgb * (edge * _EmissionPower * dissolving);
                
                return half4(col.rgb + emissionColor, col.a);
            }
            ENDHLSL
        }
    }
}