Shader "Unlit/StampAccumulateShaderURP"
{
    Properties
    {
        // _MainTex には「前フレームまでの描画結果(過去のスタンプの履歴)」が自動的に入ります
        [MainTexture] _MainTex ("Accumulated Texture", 2D) = "black" {}
        // C#側から個別にセットする、中心が赤・フチが緑のスタンプ画像
        _StampTex ("Stamp Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent" // ブレンドを使うのでTransparentにする
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            // BlendOp Max
            // Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_StampTex);
            SAMPLER(sampler_StampTex);

            // C#から受け取るパラメータ
            float2 _StampPos; // 中心座標 (x, y)
            float2 _StampScale; // サイズ (x, y)

            Varyings vert (Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = input.uv;
                return o;
            }

            half4 frag (Varyings input) : SV_Target
            {
                // 背景（過去の履歴）の色をサンプリング
                // これをそのまま返しつつ、BlendOp Max でスタンプを重ねます
                half4 accumulatedColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // スタンプ用のUV座標を計算
                float2 stampUV = (input.uv - _StampPos) / _StampScale + 0.5;

                // スタンプの範囲外の時だけ、過去の履歴をそのまま返す
                if (stampUV.x < 0.0 || stampUV.x > 1.0 || stampUV.y < 0.0 || stampUV.y > 1.0)
                {
                    return accumulatedColor;
                }

                // スタンプ画像の色をサンプリング
                half4 stampColor = SAMPLE_TEXTURE2D(_StampTex, sampler_StampTex, stampUV);

                // スタンプが黒の部分は「スタンプじゃない」とみなして、過去の色をそのまま維持する
                float stampedPixelMask = saturate(stampColor.r + stampColor.g);
                if (stampedPixelMask < 0.1)
                {
                    return accumulatedColor; // 過去の色をそのまま維持
                }

                // ここに来たということは「スタンプピクセル」である。
                // ここでマニュアルで「最大値合成（Max）」を計算して返します。
                // これにより、赤(R)は維持されつつ、緑(G)のフチが重なっても赤が維持される。
                half4 result;
                result.rgb = max(accumulatedColor.rgb, stampColor.rgb);
                result.a = 1.0; // RTへの書き込みなのでアルファは1固定でOK

                return result;
            }
            ENDHLSL
        }
    }
}
