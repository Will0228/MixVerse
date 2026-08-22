Shader "TextMeshPro/GradationTextShader"
{
    Properties
    {
        _MainTex("Font Atlas", 2D) = "white" {}
        _Color1("Text Color 1 (Left)", Color) = (1,0,0,1)
        _Color2("Text Color 2 (Center)", Color) = (0,1,0,1)
        _Color3("Text Color 3 (Right)", Color) = (0,0,1,1)

        [Header(Gradient Angle)]
        _GradientAngle("Gradient Angle (Degrees)", Float) = 0.0

        _FaceDilate("Face Dilate", Range(-1,1)) = 0
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

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 vertex    : POSITION;
                float2 texcoord  : TEXCOORD0; // フォントアトラス(SDFテクスチャ)上の座標
                float2 texcoord1 : TEXCOORD1; // テキスト矩形内の位置 0 ~ 1
                float4 color     : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 gradUV     : TEXCOORD1;
                float4 color      : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color1;
                float4 _Color2;
                float4 _Color3;
                float _GradientAngle;
                float4 _MainTex_ST;
                float _FaceDilate;
            CBUFFER_END

            Texture2D _MainTex;
            SamplerState sampler_MainTex;

            float RemapA(float value, float from1, float to1, float from2, float to2)
            {
                return (value - from1) / (to1 - from1 + 0.0001) * (to2 - from2) + from2;
            }

            half4 srgbToOklab(half4 srgb)
            {
                half3 lrgb = max(0.0, srgb.rgb);
                lrgb = pow(lrgb, 2.2);

                float l = 0.4122214708f * lrgb.r + 0.5363325363f * lrgb.g + 0.0514459929f * lrgb.b;
                float m = 0.2119034982f * lrgb.r + 0.6806995451f * lrgb.g + 0.1073969566f * lrgb.b;
                float s = 0.0883024619f * lrgb.r + 0.2817188376f * lrgb.g + 0.6299787005f * lrgb.b;

                float l_ = pow(max(0.0, l), 1.0 / 3.0);
                float m_ = pow(max(0.0, m), 1.0 / 3.0);
                float s_ = pow(max(0.0, s), 1.0 / 3.0);

                half3 lab;
                lab.x = 0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_;
                lab.y = 1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_;
                lab.z = 0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_;

                return half4(lab, srgb.a);
            }

            half4 oklabToSRGB(half4 lab)
            {
                float l_ = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
                float m_ = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
                float s_ = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;

                float l = l_ * l_ * l_;
                float m = m_ * m_ * m_;
                float s = s_ * s_ * s_;

                half3 lrgb;
                lrgb.r = +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
                lrgb.g = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
                lrgb.b = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;

                lrgb = max(0.0, lrgb);
                lrgb = pow(lrgb, 1.0 / 2.2);

                return half4(lrgb, lab.a);
            }

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.texcoord;
                o.gradUV = v.texcoord1;
                o.color = v.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 texColor = _MainTex.Sample(sampler_MainTex, i.uv);

                half signedDistance = 0.5 - texColor.a - _FaceDilate;
                half alpha = saturate(1.0 - (signedDistance / 0.05));

                half4 color1 = srgbToOklab(_Color1);
                half4 color2 = srgbToOklab(_Color2);
                half4 color3 = srgbToOklab(_Color3);

                // 矩形の中心を原点とした -0.5〜0.5 の座標に変換し、角度方向へ射影する
                float2 centered = i.gradUV - 0.5;
                float radian = radians(_GradientAngle);
                float2 direction = float2(cos(radian), sin(radian));

                // 単位矩形をこの方向へ射影したときの全長で割り、常に 0〜1 を使い切るようにする
                float projectedLength = abs(direction.x) + abs(direction.y);
                float t = saturate(dot(centered, direction) / max(projectedLength, 0.0001) + 0.5);

                half4 gradLab;
                if (t < 0.5)
                {
                    gradLab = lerp(color1, color2, RemapA(t, 0.0, 0.5, 0.0, 1.0));
                }
                else
                {
                    gradLab = lerp(color2, color3, RemapA(t, 0.5, 1.0, 0.0, 1.0));
                }

                half4 finalColor = oklabToSRGB(gradLab);

                // TMP の頂点カラーのアルファ（テキストの alpha / CanvasGroup / <alpha> タグ）を反映
                finalColor.a *= alpha * i.color.a;

                return finalColor;
            }
            ENDHLSL
        }
    }
}
