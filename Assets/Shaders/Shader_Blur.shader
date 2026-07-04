Shader "Custom/URP/PostProcess/GaussianBilateralBlur"
{
    Properties
    {
        [Header(Blur)]
        _BlurRadius("Blur Radius", Range(0, 4)) = 2
        _BlurStrength("Blur Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Overlay"
        }

        Pass
        {
            Name "GaussianBilateralBlur"

            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            /*
             * These are official URP include files.
             *
             * Core.hlsl:
             * Basic URP types, transforms and shader macros.
             *
             * Blit.hlsl:
             * Fullscreen triangle vertex shader, Varyings,
             * _BlitTexture and fullscreen sampling declarations.
             */
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _BlurRadius;
                float _BlurStrength;
            CBUFFER_END

            /*
             * Sample the source texture supplied by the
             * URP Full Screen Pass Renderer Feature.
             */
            half4 SampleSource(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(
                    _BlitTexture,
                    sampler_LinearClamp,
                    uv
                );
            }

            /*
             * Return the size of one source-screen pixel in UV space.
             *
             * For a 1920 x 1080 screen, this approximately returns:
             * (1 / 1920, 1 / 1080).
             */
            float2 GetSourceTexelSize()
            {
                return rcp(_ScreenParams.xy);
            }

            /*
             * One-dimensional Gaussian distribution weight.
             */
            float GaussianWeight(float distance, float sigma)
            {
                float sigmaSquared = sigma * sigma;

                return exp(
                    -(distance * distance) /
                    max(2.0 * sigmaSquared, 0.00001)
                );
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float2 texelSize = GetSourceTexelSize();

                half4 originalColor = SampleSource(uv);

                /*
                 * Radius zero means no blur.
                 *
                 * Handling it explicitly also avoids the center sample
                 * receiving an unstable Gaussian sigma.
                 */
                if (_BlurRadius <= 0.001 ||
                    _BlurStrength <= 0.001)
                {
                    return originalColor;
                }

                half3 colorSum = half3(
                    0.0,
                    0.0,
                    0.0
                );

                float weightSum = 0.0;

                float sigma = max(
                    _BlurRadius * 0.5,
                    0.001
                );

                /*
                 * Fixed 9 x 9 sample region.
                 *
                 * The actual circular sample area is controlled by
                 * _BlurRadius. Since the loop only extends from -4 to 4,
                 * the effective maximum radius is 4 pixels.
                 */
                [unroll]
                for (int y = -4; y <= 4; y++)
                {
                    [unroll]
                    for (int x = -4; x <= 4; x++)
                    {
                        float2 offset = float2(
                            x,
                            y
                        );

                        float distanceFromCenter =
                            length(offset);

                        if (distanceFromCenter > _BlurRadius)
                        {
                            continue;
                        }

                        float2 sampleUV =
                            uv +
                            offset *
                            texelSize;

                        /*
                         * Prevent linear filtering from sampling outside
                         * the source texture near the screen edges.
                         */
                        sampleUV = clamp(
                            sampleUV,
                            texelSize * 0.5,
                            1.0 - texelSize * 0.5
                        );

                        half3 sampleColor =
                            SampleSource(sampleUV).rgb;

                        float spatialWeight =
                            GaussianWeight(
                                distanceFromCenter,
                                sigma
                            );

                        /*
                         * Currently this is a Gaussian blur.
                         *
                         * A real bilateral blur would multiply the
                         * spatial weight by a color-difference weight.
                         */
                        float bilateralWeight = 1.0;

                        float finalWeight =
                            spatialWeight *
                            bilateralWeight;

                        colorSum +=
                            sampleColor *
                            finalWeight;

                        weightSum +=
                            finalWeight;
                    }
                }

                half3 blurredColor =
                    colorSum /
                    max(weightSum, 0.00001);

                half3 finalColor = lerp(
                    originalColor.rgb,
                    blurredColor,
                    saturate(_BlurStrength)
                );

                return half4(
                    finalColor,
                    originalColor.a
                );
            }

            ENDHLSL
        }
    }

    Fallback Off
}