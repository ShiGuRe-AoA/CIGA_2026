Shader "Custom/URP/ColorAdjustment"
{
    Properties
    {
        [Header(Global)]
        _GlobalLift("Global Lift", Range(-0.15, 0.15)) = 0
        _GlobalTint("Global Tint", Color) = (0.5, 0.5, 0.5, 1)
        _GlobalTintStrength("Global Tint Strength", Range(0, 1)) = 0
        _GlobalSaturation("Global Saturation", Range(0, 2)) = 1

        [Header(Shadows)]
        _ShadowLift("Shadow Lift", Range(-0.3, 0.3)) = 0
        _ShadowsTint("Shadows Tint", Color) = (0.5, 0.5, 0.5, 1)
        _ShadowTintStrength("Shadow Tint Strength", Range(0, 1)) = 0
        _ShadowSaturation("Shadow Saturation", Range(0, 2)) = 1

        [Header(Midtones)]
        _MidtoneLift("Midtone Lift", Range(-0.3, 0.3)) = 0
        _MidtonesTint("Midtones Tint", Color) = (0.5, 0.5, 0.5, 1)
        _MidtoneTintStrength("Midtone Tint Strength", Range(0, 1)) = 0
        _MidtoneSaturation("Midtone Saturation", Range(0, 2)) = 1

        [Header(Highlights)]
        _HighlightLift("Highlight Lift", Range(-0.3, 0.3)) = 0
        _HighlightsTint("Highlights Tint", Color) = (0.5, 0.5, 0.5, 1)
        _HighlightTintStrength("Highlight Tint Strength", Range(0, 1)) = 0
        _HighlightSaturation("Highlight Saturation", Range(0, 2)) = 1

        [Header(Range)]
        _ShadowEnd("Shadow End", Range(0, 1)) = 0.33
        _HighlightStart("Highlight Start", Range(0, 1)) = 0.66
        _RangeSoftness("Range Softness", Range(0.001, 0.5)) = 0.15
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ColorAdjustment"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            float _GlobalLift;
            float4 _GlobalTint;
            float _GlobalTintStrength;
            float _GlobalSaturation;

            float _ShadowLift;
            float4 _ShadowsTint;
            float _ShadowTintStrength;
            float _ShadowSaturation;

            float _MidtoneLift;
            float4 _MidtonesTint;
            float _MidtoneTintStrength;
            float _MidtoneSaturation;

            float _HighlightLift;
            float4 _HighlightsTint;
            float _HighlightTintStrength;
            float _HighlightSaturation;

            float _ShadowEnd;
            float _HighlightStart;
            float _RangeSoftness;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                output.positionCS = float4(uv * 2.0 - 1.0, 0, 1);
                output.uv = uv;

            #if UNITY_UV_STARTS_AT_TOP
                output.uv.y = 1.0 - output.uv.y;
            #endif

                return output;
            }

            float cbrt_safe(float x)
            {
                return sign(x) * pow(abs(x), 1.0 / 3.0);
            }

            float3 RGB2OKLAB(float3 c)
            {
                float l = 0.41222147*c.r + 0.53633254*c.g + 0.05144599*c.b;
                float m = 0.21190350*c.r + 0.68069955*c.g + 0.10739696*c.b;
                float s = 0.08830246*c.r + 0.28171884*c.g + 0.62997870*c.b;

                float3 lms = float3(
                    cbrt_safe(l),
                    cbrt_safe(m),
                    cbrt_safe(s)
                );

                return float3(
                    0.21045426*lms.x + 0.79361778*lms.y - 0.00407205*lms.z,
                    1.97799850*lms.x - 2.42859221*lms.y + 0.45059371*lms.z,
                    0.02590404*lms.x + 0.78277177*lms.y - 0.80867577*lms.z
                );
            }

            float3 OKLAB2RGB(float3 lab)
            {
                float L = lab.x;
                float a = lab.y;
                float b = lab.z;

                float l_ = L + 0.3963377774*a + 0.2158037573*b;
                float m_ = L - 0.1055613458*a - 0.0638541728*b;
                float s_ = L - 0.0894841775*a - 1.2914855480*b;

                float l = l_*l_*l_;
                float m = m_*m_*m_;
                float s = s_*s_*s_;

                float3 rgb;
                rgb.r =  4.0767416621*l - 3.3077115913*m + 0.2309699292*s;
                rgb.g = -1.2684380046*l + 2.6097574011*m - 0.3413193965*s;
                rgb.b = -0.0041960863*l - 0.7034186147*m + 1.7076147010*s;
                return rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, input.uv).rgb;

                float3 lab = RGB2OKLAB(color);

                float L0 = saturate(lab.x);

                // ===== 区域划分 =====
                float shadowMask =
                    1.0 - smoothstep(
                        _ShadowEnd - _RangeSoftness,
                        _ShadowEnd + _RangeSoftness,
                        L0);

                float highlightMask =
                    smoothstep(
                        _HighlightStart - _RangeSoftness,
                        _HighlightStart + _RangeSoftness,
                        L0);

                float midtoneMask =
                    saturate(1.0 - shadowMask - highlightMask);

                // ===== Lift =====
                float lift =
                    _GlobalLift +
                    shadowMask * _ShadowLift +
                    midtoneMask * _MidtoneLift +
                    highlightMask * _HighlightLift;

                lab.x = saturate(lab.x + lift);

                // ===== 染色 =====
                float2 neutralAB =
                    RGB2OKLAB(float3(0.5, 0.5, 0.5)).yz;

                float2 globalAB =
                    RGB2OKLAB(_GlobalTint.rgb).yz - neutralAB;

                float2 shadowAB =
                    RGB2OKLAB(_ShadowsTint.rgb).yz - neutralAB;

                float2 midAB =
                    RGB2OKLAB(_MidtonesTint.rgb).yz - neutralAB;

                float2 highAB =
                    RGB2OKLAB(_HighlightsTint.rgb).yz - neutralAB;

                // ===============================
                // 分区饱和度（先作用在原始 ab 上）
                // 1 = 不变，<1 去饱和，>1 增饱和
                // ===============================
                float sat =
                    _GlobalSaturation +
                    shadowMask * (_ShadowSaturation - 1.0) +
                    midtoneMask * (_MidtoneSaturation - 1.0) +
                    highlightMask * (_HighlightSaturation - 1.0);

                lab.yz *= max(sat, 0.0);

                // ===============================
                // 分区染色（再叠加偏色）
                // ===============================
                lab.yz += globalAB * _GlobalTintStrength;
                lab.yz += shadowAB * shadowMask * _ShadowTintStrength;
                lab.yz += midAB * midtoneMask * _MidtoneTintStrength;
                lab.yz += highAB * highlightMask * _HighlightTintStrength;

                float3 finalColor = saturate(OKLAB2RGB(lab));

                return half4(finalColor, 1);
            }

            ENDHLSL
        }
    }
}