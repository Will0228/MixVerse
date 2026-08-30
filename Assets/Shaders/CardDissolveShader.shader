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

        // 残像（CardAfterImage が作る複製）用。カード本体では 0 のまま。
        _TrailGhost ("Is Trail Ghost", Float) = 0
        _TrailAlpha ("Trail Alpha", Range(0, 1)) = 0
        _TrailColor ("Trail Color (A = Tint Weight)", Color) = (0.35, 0.85, 1, 0.7)
    }
    SubShader
    {
        // clip() で抜くだけで半透明合成はしないため、Transparent ではなく AlphaTest に置く。
        // カードは全枚数がこのシェーダーで描かれるので、Transparent キューに入れると
        // 手札のように重なり合う板の描画順が距離ソート任せになり、ちらつく。
        //
        // 残像だけは半透明なので、CardAfterImage 側でマテリアルの renderQueue を
        // Transparent へ上書きし、カードを全部描き終えたあとに重ねる。
        Tags
        {
            "RenderType"="TransparentCutout"
            "RenderPipeline"="UniversalPipeline"
            "Queue" = "AlphaTest"
        }
        LOD 100

        HLSLINCLUDE
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

        // SRP Batcher の要求どおり、どのパスでも同じ内容にしておく。
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _Threshold;
            float _EdgeStepOffset;
            float4 _EmissionColor;
            float _EmissionPower;
            float _TrailGhost;
            float _TrailAlpha;
            float4 _TrailColor;
        CBUFFER_END

        /// 本体と残像は同じマテリアルの複製を使うため、担当しない側は
        /// 頂点を 1 点に集めて面積 0 の三角形にし、フラグメントまで走らせない。
        float4 CollapseUnless(float4 clipPosition, float enabled)
        {
            float keep = step(0.5, enabled);
            return float4(clipPosition.xyz * keep, lerp(1.0, clipPosition.w, keep));
        }
        ENDHLSL

        Pass
        {
            Name "Card"

            // URP は LightMode タグ 1 つにつきパスを 1 つしか選ばないので、
            // 本体と残像でタグを分ける必要がある。本体はタグ無しのとき（＝これまで）と
            // 同じ扱いになる SRPDefaultUnlit のままにして、描かれ方を変えない。
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            Varyings vert (Attribute input)
            {
                Varyings o;
                o.vertex = CollapseUnless(TransformObjectToHClip(input.vertex.xyz), 1.0 - _TrailGhost);
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

        // 残像。過去の姿勢に置いた複製を、速度に応じた濃さで薄く重ねる。
        Pass
        {
            Name "AfterImage"

            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            Varyings vert (Attribute input)
            {
                Varyings o;
                o.vertex = CollapseUnless(TransformObjectToHClip(input.vertex.xyz), _TrailGhost);
                o.uv = input.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _BaseColor;

                // 絵柄をそのまま薄くしただけだと「置き去りのカード」に見えるので、
                // 残像の色へ寄せて発光している尾に見せる。
                half3 trailColor = lerp(col.rgb, _TrailColor.rgb, _TrailColor.a);

                return half4(trailColor, col.a * _TrailAlpha);
            }
            ENDHLSL
        }
    }
}
