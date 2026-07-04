Shader "Hidden/PostProcessing/Pixelate"
{
    Properties
    {
        [Header(Pixel Resolution)]
        _PixelHeight("Pixel Height", Range(16, 1080)) = 180

        [Header(Blend)]
        _Intensity("Intensity", Range(0, 1)) = 1
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
            Name "Pixelate Post Process"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _PixelHeight;
                float _Intensity;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 sourceUV = input.texcoord;

                float screenAspect =
                    _ScreenParams.x /
                    max(_ScreenParams.y, 1.0);

                float pixelHeight = max(_PixelHeight, 1.0);
                float pixelWidth = max(
                    round(pixelHeight * screenAspect),
                    1.0
                );

                float2 pixelCount = float2(
                    pixelWidth,
                    pixelHeight
                );

                // Sample from the center of each virtual pixel.
                float2 pixelatedUV =
                    (floor(sourceUV * pixelCount) + 0.5) /
                    pixelCount;

                half4 sourceColor = SAMPLE_TEXTURE2D_X(
                    _BlitTexture,
                    sampler_LinearClamp,
                    sourceUV
                );

                half4 pixelatedColor = SAMPLE_TEXTURE2D_X(
                    _BlitTexture,
                    sampler_PointClamp,
                    pixelatedUV
                );

                return lerp(
                    sourceColor,
                    pixelatedColor,
                    _Intensity
                );
            }

            ENDHLSL
        }
    }

    Fallback Off
}