Shader "Custom/URP/SharpenPosterize"
{
    Properties
    {
        _SharpenStrength("Sharpen Strength", Range(0, 0.5)) = 0.2
        _PosterizeSteps("Posterize Steps", Range(8, 512)) = 32
        _LightWeight("Posterize Light Weight", Range(0, 1)) = 0.5
        _ColorWeight("Posterize Color Weight", Range(0, 0.3)) = 0.2
        _EnableSharpen("Enable Sharpen", Float) = 1
        _EnablePosterize("Enable Posterize", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }

        Pass
        {
            Name "SharpenPosterize"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            float _SharpenStrength;
            float _PosterizeSteps;
            float _LightWeight;
            float _ColorWeight;
            float _EnableSharpen;
            float _EnablePosterize;

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
                output.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                output.uv = uv;

                #if UNITY_UV_STARTS_AT_TOP
                output.uv.y = 1.0 - output.uv.y;
                #endif

                return output;
            }

            float3 ApplySharpen(float2 uv)
            {
                float2 texelSize = 1.0 / _ScreenParams.xy;

                float3 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv).rgb;
                float3 up     = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv + float2(0,  texelSize.y)).rgb;
                float3 down   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv + float2(0, -texelSize.y)).rgb;
                float3 left   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv + float2(-texelSize.x, 0)).rgb;
                float3 right  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv + float2( texelSize.x, 0)).rgb;

                float k = _SharpenStrength;
                float3 result = center * (1.0 + 4.0 * k) - (up + down + left + right) * k;
                return saturate(result);
            }

            float3 RGB2OKLAB(float3 c)
            {
                float l = 0.41222147*c.r + 0.53633254*c.g + 0.05144599*c.b;
                float m = 0.21190350*c.r + 0.68069955*c.g + 0.10739696*c.b;
                float s = 0.08830246*c.r + 0.28171884*c.g + 0.62997870*c.b;
                float3 lms = pow(float3(l,m,s), 1.0/3.0);
                return float3(
                    0.21045426*lms.x + 0.79361778*lms.y - 0.00407205*lms.z,
                    1.97799850*lms.x - 2.42859221*lms.y + 0.45059371*lms.z,
                    0.02590404*lms.x + 0.78277177*lms.y - 0.80867577*lms.z);
            }

            float3 OKLAB2RGB(float3 lab)
            {
                float L = lab.x, a = lab.y, b = lab.z;

                float l_ = L + 0.3963377774*a + 0.2158037573*b;
                float m_ = L - 0.1055613458*a - 0.0638541728*b;
                float s_ = L - 0.0894841775*a - 1.2914855480*b;

                float l = l_*l_*l_;   // cube
                float m = m_*m_*m_;
                float s = s_*s_*s_;

                float3 rgb;
                rgb.r =  4.0767416621*l - 3.3077115913*m + 0.2309699292*s;
                rgb.g = -1.2684380046*l + 2.6097574011*m - 0.3413193965*s;
                rgb.b = -0.0041960863*l - 0.7034186147*m + 1.7076147010*s;
                return rgb;
            }

            float3 ApplyPosterize(float3 color)
            {
                float steps = max(_PosterizeSteps, 2.0);

                //============
                // float luminance = dot(color, float3(0.299, 0.587, 0.114));
                // float l = floor(luminance * steps) / steps;
                // color *= (l / max(luminance, 1e-5));
                // return saturate(color);
                //============

                //============ OKLAB 2 RGB
                float3 originLab = RGB2OKLAB(color);
                float3 lab = originLab;
                lab.x = lerp(originLab.x, floor(lab.x * (steps - 1)) / (steps - 1), _LightWeight);
                float3 colorLAB = saturate(OKLAB2RGB(lab));
                //============

                //============ Linear RGB
                float3 rgb = color;
                rgb = lerp(rgb, floor(rgb * (steps - 1)) / (steps - 1), _LightWeight);
                float3 colorRGB = saturate(rgb);
                //============

                float3 resultColor = lerp(colorLAB, colorRGB, _ColorWeight);
                return saturate(resultColor);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, input.uv).rgb;

                if (_EnableSharpen > 0.5)
                {
                    color = ApplySharpen(input.uv);
                }

                if (_EnablePosterize > 0.5)
                {
                    color = ApplyPosterize(color);
                }

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}