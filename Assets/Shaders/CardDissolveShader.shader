Shader "Unlit/CardDissolveShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _NoiseTex ("Noise Texture", 2D) = "white" {}
        _Threshold ("Threshold", Range(0, 1)) = 0.5
        _EdgeStepOffset ("Edge Step Offset", Float) = 0.01
        _EmissionColor ("Emission Color", Color) = (1, 0.5, 0, 1)
        _EmissionPower ("Emission Power", Float) = 3.0
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "Queue" = "Transparent"
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
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.uv).r;
                clip(noise.r - _Threshold);
                
                float edge = step(noise.r, _Threshold + _EdgeStepOffset);
                half3 emissionColor = _EmissionColor.rgb * (edge * _EmissionPower);
                
                return half4(col.rgb + emissionColor, col.a);
            }
            ENDHLSL
        }
    }
}