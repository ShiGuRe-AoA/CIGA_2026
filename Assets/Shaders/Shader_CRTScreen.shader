Shader "Hidden/PostProcessing/CRTScreen"
{
    Properties
    {
        [Header(Screen Resolution)]
        _ScreenWidth("Virtual Screen Width", Float) = 512
        _ScreenHeight("Virtual Screen Height", Float) = 288

        [Header(Screen Distortion)]
        _Distortion("Distortion", Range(0, 0.2)) = 0.04
        _ScreenScale("Screen Scale", Range(1, 1.5)) = 1.05

        [Header(RGB Separation)]
        _RGBSeparationStrength(
            "RGB Separation Strength",
            Range(0, 0.03)
        ) = 0.002

        _RGBSeparationDirection(
            "RGB Separation Direction",
            Vector
        ) = (1, 0, 0, 0)

        _RGBSeparationEdgeInfluence(
            "RGB Separation Edge Influence",
            Range(0, 2)
        ) = 0.75

        [Header(RGB Mask)]
        _RGBMaskStrength("RGB Mask Strength", Range(0, 1)) = 0.25

        [Header(Scan Lines)]
        _ScanLineStrength("Scan Line Strength", Range(0, 1)) = 0.35
        _ScanLineSpeed("Scan Line Speed", Float) = 1
        _ScanLineSharpness("Scan Line Sharpness", Range(0.1, 10)) = 3

        [Header(Rolling Scan)]
        _RollingLineStrength("Rolling Line Strength", Range(0, 1)) = 0.12
        _RollingLineSpeed("Rolling Line Speed", Float) = 0.15
        _RollingLineWidth("Rolling Line Width", Range(1, 50)) = 12

        [Header(Noise)]
        _NoiseStrength("Noise Strength", Range(0, 0.2)) = 0.015
        _NoiseSpeed("Noise Speed", Float) = 1
        _NoiseScale("Noise Scale", Float) = 1

        [Header(Vignette)]
        _VignetteStrength("Vignette Strength", Range(0, 1)) = 0.45
        _VignettePower("Vignette Power", Range(0.1, 8)) = 2

        [Header(Output)]
        _Brightness("Brightness", Range(0, 2)) = 1
        _Intensity("Effect Intensity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "CRT Post Process"

            HLSLPROGRAM

            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ScreenWidth;
                float _ScreenHeight;

                float _Distortion;
                float _ScreenScale;

                float _RGBSeparationStrength;
                float4 _RGBSeparationDirection;
                float _RGBSeparationEdgeInfluence;

                float _RGBMaskStrength;

                float _ScanLineStrength;
                float _ScanLineSpeed;
                float _ScanLineSharpness;

                float _RollingLineStrength;
                float _RollingLineSpeed;
                float _RollingLineWidth;

                float _NoiseStrength;
                float _NoiseSpeed;
                float _NoiseScale;

                float _VignetteStrength;
                float _VignettePower;

                float _Brightness;
                float _Intensity;
            CBUFFER_END

            float RandomNoise(float2 position)
            {
                position = frac(
                    position *
                    float2(123.34, 456.21)
                );

                position += dot(
                    position,
                    position + 45.32
                );

                return frac(
                    position.x *
                    position.y
                );
            }

            float GenerateNoise(float2 uv)
            {
                float2 virtualResolution = float2(
                    max(_ScreenWidth, 1.0),
                    max(_ScreenHeight, 1.0)
                );

                virtualResolution *= max(
                    _NoiseScale,
                    0.01
                );

                float animationFrame = floor(
                    _Time.y *
                    max(_NoiseSpeed, 0.0) *
                    30.0
                );

                float2 noiseCell = floor(
                    uv *
                    virtualResolution
                );

                return RandomNoise(
                    noiseCell +
                    animationFrame *
                    float2(17.0, 31.0)
                );
            }

            float2 DistortUV(float2 uv)
            {
                float2 centeredUV =
                    uv * 2.0 - 1.0;

                /*
                 * Smaller source UV range means that the final
                 * image is visually enlarged.
                 */
                centeredUV /= max(
                    _ScreenScale,
                    0.0001
                );

                float radiusSquared = dot(
                    centeredUV,
                    centeredUV
                );

                centeredUV *=
                    1.0 +
                    radiusSquared *
                    _Distortion;

                return
                    centeredUV *
                    0.5 +
                    0.5;
            }

            float IsInsideScreen(float2 uv)
            {
                float2 lowerBounds =
                    step(0.0, uv);

                float2 upperBounds =
                    step(uv, 1.0);

                return
                    lowerBounds.x *
                    lowerBounds.y *
                    upperBounds.x *
                    upperBounds.y;
            }

            float GenerateScanLine(float2 uv)
            {
                float scanPosition =
                    uv.y *
                    max(_ScreenHeight, 1.0) *
                    PI;

                float scanLine =
                    sin(scanPosition) *
                    0.5 +
                    0.5;

                return pow(
                    saturate(scanLine),
                    max(_ScanLineSharpness, 0.01)
                );
            }

            float GenerateRollingLine(float2 uv)
            {
                float rollingPosition = frac(
                    uv.y -
                    _Time.y *
                    _RollingLineSpeed
                );

                float distanceFromCenter =
                    rollingPosition -
                    0.5;

                return exp(
                    -distanceFromCenter *
                    distanceFromCenter *
                    max(_RollingLineWidth, 0.01)
                );
            }

            float3 GenerateRGBMask(float2 uv)
            {
                float pixelPosition =
                    uv.x *
                    max(_ScreenWidth, 1.0);

                float rgbCell = frac(
                    pixelPosition /
                    3.0
                );

                float3 mask;

                mask.r =
                    1.0 -
                    step(
                        1.0 / 3.0,
                        rgbCell
                    );

                mask.g =
                    step(
                        1.0 / 3.0,
                        rgbCell
                    ) -
                    step(
                        2.0 / 3.0,
                        rgbCell
                    );

                mask.b =
                    step(
                        2.0 / 3.0,
                        rgbCell
                    );

                return lerp(
                    float3(1.0, 1.0, 1.0),
                    mask * 1.5 + 0.5,
                    _RGBMaskStrength
                );
            }

            float GenerateVignette(float2 uv)
            {
                float2 centeredUV =
                    abs(uv - 0.5) *
                    2.0;

                float vignette =
                    saturate(
                        1.0 -
                        centeredUV.x *
                        centeredUV.x
                    );

                vignette *= saturate(
                    1.0 -
                    centeredUV.y *
                    centeredUV.y
                );

                vignette = pow(
                    max(vignette, 0.0001),
                    max(_VignettePower, 0.01)
                );

                return lerp(
                    1.0,
                    vignette,
                    _VignetteStrength
                );
            }

            /*
             * Samples the source independently for red, green and blue.
             *
             * Separation becomes stronger near the screen edges,
             * which resembles chromatic aberration on curved CRT glass.
             */
            half4 SampleRGBSeparated(
                float2 uv,
                float edgeFactor
            )
            {
                float2 direction =
                    _RGBSeparationDirection.xy;

                float directionLength =
                    length(direction);

                direction =
                    directionLength > 0.0001
                    ? direction / directionLength
                    : float2(1.0, 0.0);

                float separation =
                    _RGBSeparationStrength *
                    lerp(
                        1.0,
                        edgeFactor,
                        _RGBSeparationEdgeInfluence
                    );

                float2 redUV =
                    uv +
                    direction *
                    separation;

                float2 blueUV =
                    uv -
                    direction *
                    separation;

                /*
                 * Clamp separately to avoid wrapping at screen edges.
                 */
                redUV = saturate(redUV);
                blueUV = saturate(blueUV);

                half red = SAMPLE_TEXTURE2D_X(
                    _BlitTexture,
                    sampler_LinearClamp,
                    redUV
                ).r;

                half green = SAMPLE_TEXTURE2D_X(
                    _BlitTexture,
                    sampler_LinearClamp,
                    uv
                ).g;

                half blue = SAMPLE_TEXTURE2D_X(
                    _BlitTexture,
                    sampler_LinearClamp,
                    blueUV
                ).b;

                half alpha = SAMPLE_TEXTURE2D_X(
                    _BlitTexture,
                    sampler_LinearClamp,
                    uv
                ).a;

                return half4(
                    red,
                    green,
                    blue,
                    alpha
                );
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 originalUV =
                    input.texcoord;

                half4 originalColor =
                    SAMPLE_TEXTURE2D_X(
                        _BlitTexture,
                        sampler_LinearClamp,
                        originalUV
                    );

                float2 distortedUV =
                    DistortUV(originalUV);

                float insideScreen =
                    IsInsideScreen(
                        distortedUV
                    );

                float noiseValue =
                    GenerateNoise(
                        distortedUV
                    );

                float rowNoise =
                    RandomNoise(
                        float2(
                            floor(
                                distortedUV.y *
                                max(_ScreenHeight, 1.0)
                            ),
                            floor(
                                _Time.y *
                                max(_NoiseSpeed, 0.0) *
                                24.0
                            )
                        )
                    );

                float noiseOffset =
                    (noiseValue - 0.5) *
                    _NoiseStrength;

                noiseOffset +=
                    (rowNoise - 0.5) *
                    _NoiseStrength *
                    0.5;

                /*
                 * Small analog horizontal jitter.
                 */
                float horizontalWave =
                    sin(
                        distortedUV.y *
                        max(_ScreenHeight, 1.0) *
                        0.25 +
                        _Time.y *
                        _ScanLineSpeed
                    );

                distortedUV.x +=
                    noiseOffset +
                    horizontalWave *
                    _NoiseStrength *
                    0.05;

                insideScreen *=
                    IsInsideScreen(
                        distortedUV
                    );

                /*
                 * Distance from screen center.
                 *
                 * Value is approximately zero in the center and
                 * becomes larger toward corners.
                 */
                float2 centeredUV =
                    distortedUV * 2.0 - 1.0;

                float edgeFactor =
                    saturate(
                        length(centeredUV)
                    );

                half4 crtColor =
                    SampleRGBSeparated(
                        distortedUV,
                        edgeFactor
                    );

                float brightnessNoise =
                    lerp(
                        1.0,
                        0.88 +
                        noiseValue *
                        0.24,
                        saturate(
                            _NoiseStrength *
                            5.0
                        )
                    );

                crtColor.rgb *=
                    brightnessNoise;

                crtColor.rgb *=
                    GenerateRGBMask(
                        distortedUV
                    );

                float scanLine =
                    GenerateScanLine(
                        distortedUV
                    );

                float scanBrightness =
                    lerp(
                        1.0,
                        lerp(
                            0.55,
                            1.0,
                            scanLine
                        ),
                        _ScanLineStrength
                    );

                crtColor.rgb *=
                    scanBrightness;

                float rollingLine =
                    GenerateRollingLine(
                        distortedUV
                    );

                crtColor.rgb +=
                    crtColor.rgb *
                    rollingLine *
                    _RollingLineStrength;

                crtColor.rgb *=
                    GenerateVignette(
                        distortedUV
                    );

                crtColor.rgb *=
                    _Brightness;

                crtColor.rgb *=
                    insideScreen;

                half4 finalColor =
                    lerp(
                        originalColor,
                        crtColor,
                        _Intensity
                    );

                finalColor.a =
                    originalColor.a;

                return finalColor;
            }

            ENDHLSL
        }
    }

    Fallback Off
}