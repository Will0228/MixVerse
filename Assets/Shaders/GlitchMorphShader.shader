Shader "Unlit/GlitchMorphShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        // 0 が実体、1 が完全に消えた状態。CardDissolveShader の _Threshold と同じ向き。
        // 変身元は 0→1、変身先は 1→0 へ動かす。
        _Progress ("Progress", Range(0, 1)) = 0

        [Header(Glitch)]
        // 横帯の細かさ。ワールド空間の高さを基準にするので、
        // 2 つのオブジェクトで同じ値にすると帯の位置がそろって「乗り移った」ように見える。
        _BlockSize ("Block Size", Float) = 12.0
        // 帯ごとの横ズレの最大量（ワールド単位）
        _GlitchIntensity ("Glitch Intensity", Float) = 0.15
        // 乱れが切り替わる速さ。大きいほどパラパラする
        _GlitchSpeed ("Glitch Speed", Range(1, 60)) = 24.0
        // 色ズレ（UV 単位）
        _RgbSplit ("RGB Split", Float) = 0.02

        [Header(Edge)]
        _EdgeWidth ("Edge Width", Range(0, 0.5)) = 0.08
        _EmissionColor ("Emission Color", Color) = (0, 0.9, 1, 1)
        _EmissionPower ("Emission Power", Float) = 3.0

        // 0 で完全なアンリット。3D モデルに使うときは 1 のままで軽いライティングが乗る。
        _LightingStrength ("Lighting Strength", Range(0, 1)) = 1.0

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        // clip() で抜くだけで半透明合成はしないため、CardDissolveShader と同じく AlphaTest に置く。
        Tags
        {
            "RenderType"="TransparentCutout"
            "RenderPipeline"="UniversalPipeline"
            "Queue" = "AlphaTest"
        }
        LOD 100

        Pass
        {
            Name "GlitchMorphForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                // 乱す前のワールド座標。帯の ID をフラグメントでも同じ基準で求めるために渡す。
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor;
                float _Progress;
                float _BlockSize;
                float _GlitchIntensity;
                float _GlitchSpeed;
                float _RgbSplit;
                float _EdgeWidth;
                float4 _EmissionColor;
                float _EmissionPower;
                float _LightingStrength;
                float _Cull;
            CBUFFER_END

            float Random(float2 st)
            {
                return frac(sin(dot(st.xy, float2(12.9898, 78.233))) * 43758.5453123);
            }

            // 乱れの強さ。実体（0）と消滅（1）では 0 で、変身の途中で最大になる。
            // 変身元・変身先の双方がこの同じカーブを通るので、入れ替わる瞬間が一番荒れる。
            float GlitchEnvelope(float progress)
            {
                return sin(saturate(progress) * PI);
            }

            // 帯 ID。ワールドの高さで切るので、位置が重なった 2 つのオブジェクトでは帯もそろう。
            float BandId(float worldPositionY)
            {
                return floor(worldPositionY * _BlockSize);
            }

            float TimeKey()
            {
                return floor(_Time.y * _GlitchSpeed);
            }

            Varyings vert (Attributes input)
            {
                Varyings o;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                float glitch = GlitchEnvelope(_Progress);
                float band = BandId(positionWS.y);
                float timeKey = TimeKey();
                float randomValue = Random(float2(band, timeKey));

                // 一部の帯だけ大きく飛ばす。全帯が同じ幅で揺れると規則的に見えてしまう。
                float burst = step(0.75, Random(float2(band * 1.37, timeKey * 0.71)));
                float offset = (randomValue - 0.5) * 2.0 * glitch * _GlitchIntensity * (1.0 + burst * 3.0);

                // オブジェクトの向きに関係なく画面の横方向へずらしたいので、
                // ビュー行列の 1 行目（＝ワールド空間でのカメラの右方向）に沿って動かす。
                float3 cameraRight = normalize(UNITY_MATRIX_V._m00_m01_m02);

                o.positionCS = TransformWorldToHClip(positionWS + cameraRight * offset);
                o.uv = TRANSFORM_TEX(input.uv, _MainTex);
                o.positionWS = positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float glitch = GlitchEnvelope(_Progress);
                float band = BandId(i.positionWS.y);
                float timeKey = TimeKey();

                // 帯ごとに絵柄そのものを横へ流す。頂点の横ズレと合わさって「走査線が壊れた」感じになる。
                float slide = (Random(float2(band, timeKey + 3.1)) - 0.5) * glitch * 0.2;
                float2 uv = float2(i.uv.x + slide, i.uv.y);

                // 色ズレ。R と B だけ左右にずらしてサンプリングする。
                float split = _RgbSplit * glitch;
                half4 center = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                half r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(split, 0)).r;
                half b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(split, 0)).b;

                half4 col = half4(r, center.g, b, center.a) * _BaseColor;

                // 消え方。帯 × 横方向のブロックでちぎれつつ、細かいノイズでざらつかせる。
                float cell = Random(float2(band, floor(i.uv.x * _BlockSize)));
                float fine = Random(i.uv * 97.0 + timeKey);
                float mask = lerp(cell, fine, 0.3);

                // Random は [0, 1) なので、両端で完全な実体・完全な消滅になるよう少し外側まで動かす。
                float cutoff = lerp(-0.01, 1.01, _Progress);
                clip(mask - cutoff);

                // ライティング。ハーフランバート + 環境光だけの軽いもの。
                half3 normalWS = normalize(i.normalWS);
                Light mainLight = GetMainLight();
                half halfLambert = saturate(dot(normalWS, mainLight.direction)) * 0.5 + 0.5;
                half3 lit = mainLight.color * halfLambert + SampleSH(normalWS);
                col.rgb *= lerp(half3(1, 1, 1), lit, _LightingStrength);

                // 消えかけの縁を光らせる。CardDissolveShader と同じく、
                // 実体のまま（_Progress = 0）のときは光らせない。
                float morphing = step(0.001, _Progress);
                float edge = step(mask, cutoff + _EdgeWidth) * morphing;

                // 乱れている間、一部の帯を暗く落として明滅させる。
                float flicker = 1.0 - glitch * 0.4 * step(0.85, Random(float2(band * 0.13, timeKey)));
                col.rgb *= flicker;

                half3 emission = _EmissionColor.rgb * (edge * _EmissionPower);

                return half4(col.rgb + emission, col.a);
            }
            ENDHLSL
        }
    }
}
