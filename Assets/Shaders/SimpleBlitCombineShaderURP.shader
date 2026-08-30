Shader "Unlit/SimpleBlitCombineShaderURP"
{
    Properties
    {
        // 1. 通常の表面画像（削る前の背景、例えば服を着たキャラ）
        [MainTexture] _MainTex ("Normal Texture (A)", 2D) = "white" {}

        // 2. C#からセットされる、蓄積されたパターンテクスチャ（R=削れ、G=縁）
        _PatternTex ("Pattern Texture (RT)", 2D) = "black" {}

        // 3. 赤色の部分（削れたところ）に表示したい「隠し画像」（例えば別衣装）
        _SecretTex ("Secret Texture (B)", 2D) = "white" {}

        // 削れた部分の見え方を決めるつまみ。
        // 0 = 完全に透過して後ろ（3Dの背景など）が見える。_SecretTex は使われない。
        // 1 = 透過せず _SecretTex が現れる（従来のスクラッチカード的な挙動）。
        _ScratchedAlpha ("Scratched Alpha", Range(0, 1)) = 0

        _ShadowDelta ("Shadow Delta", float) = 0.0035
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent" // 削れた部分を透過させるため半透明として描く
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            // UI（Canvas）上で他の要素と正しく重なるようにアルファブレンドで描く
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR; // CanvasGroup のアルファはここに乗ってくる
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            // 各テクスチャとサンプラーの宣言
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_PatternTex);
            SAMPLER(sampler_PatternTex);

            TEXTURE2D(_SecretTex);
            SAMPLER(sampler_SecretTex);

            // パラメータ
            float _ScratchedAlpha;
            float _ShadowDelta;

            Varyings vert (Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS);
                o.uv = input.uv; // 通常はinput.uvをそのまま使います
                o.color = input.color;
                return o;
            }

            half3 GetAroundPatternColor(float2 uv)
            {
                half3 leftTop = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, float2(uv.x - _ShadowDelta, uv.y + _ShadowDelta)).rgb;
                half3 top = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, float2(uv.x, uv.y + _ShadowDelta)).rgb;
                half3 rightTop = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, float2(uv.x + _ShadowDelta, uv.y + _ShadowDelta)).rgb;
                half3 left = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, float2(uv.x - _ShadowDelta, uv.y)).rgb;
                half3 right = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, float2(uv.x + _ShadowDelta, uv.y)).rgb;
                half3 leftBottom = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, float2(uv.x - _ShadowDelta, uv.y - _ShadowDelta)).rgb;
                half3 bottom = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, float2(uv.x, uv.y - _ShadowDelta)).rgb;
                half3 rightBottom = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, float2(uv.x + _ShadowDelta, uv.y - _ShadowDelta)).rgb;
                return leftTop + top + rightTop + left + right + leftBottom + bottom + rightBottom;
            }

            half4 frag (Varyings input) : SV_Target
            {
                half4 colorA = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 colorB = SAMPLE_TEXTURE2D(_SecretTex, sampler_SecretTex, input.uv);
                half4 pattern = SAMPLE_TEXTURE2D(_PatternTex, sampler_PatternTex, input.uv);

                // 赤色の濃さを「削れの度合い」として扱う
                half scratch = pattern.r;

                half4 finalColor;

                // _ScratchedAlpha が 0（透過）のときは色を隠し画像に寄せない。
                // 寄せてしまうと、削れかけの半透明な部分に隠し画像の色（既定では白）が
                // にじんで縁取りのように見えてしまうため。
                finalColor.rgb = lerp(colorA.rgb, colorB.rgb, scratch * _ScratchedAlpha);

                // 削れるほど _ScratchedAlpha に近づく。0 なら削れた部分が抜けて後ろが見える
                finalColor.a = lerp(colorA.a, _ScratchedAlpha, scratch);

                // 赤色で完全に削られていない部分で、緑色で塗られる縁の部分は白色に近づける
                if (pattern.r != 1.0)
                {
                    finalColor.rgb = lerp(finalColor.rgb, 1, pattern.g);
                }

                // まずは周囲8方向のサンプリング結果を変数に保存する
                half3 aroundColor = GetAroundPatternColor(input.uv);

                // 「周囲のピクセルに赤色が存在する」 かつ 「現在のピクセルは完全に黒（削られていない）」
                // ※完全な0.0fだとノイズで反応しないことがあるため、0.01f未満という書き方にするとより安全です
                bool isShadowFlag = (aroundColor.r > 0.0f) && ((pattern.r + pattern.g + pattern.b) < 0.01f);

                if (isShadowFlag)
                {
                    // 影の計算。
                    // 元の aroundColor.g / 8 だと、緑のフチが薄い時に影が全く見えなくなってしまうので、
                    // 割る数を小さくする（または掛け算する）ことで影の濃さを強調できます。
                    half shadowPower = min(1.0, aroundColor.g / 4.0); // 8を4に変更して少し濃くしました

                    finalColor.rgb = max(0, finalColor.rgb - shadowPower);
                }

                // 頂点カラーを掛ける。CanvasGroup のアルファはここに入っているので、
                // これを掛けないとフェードアウトしても削り跡だけが残ってしまう。
                return finalColor * input.color;
            }
            ENDHLSL
        }
    }
}
