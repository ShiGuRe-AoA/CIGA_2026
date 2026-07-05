Shader "Custom/Vision/Darkness"
{
    Properties
    {
        _DarknessColor(
            "Darkness Color",
            Color
        ) = (0.01, 0.015, 0.04, 0.88)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "VisionDarkness"

            ZWrite Off
            ZTest Always
            Cull Off

            Blend SrcAlpha OneMinusSrcAlpha

            /*
             * Draw darkness only where the vision mesh
             * has not written stencil value 1.
             */
            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Keep
            }

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DarknessColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                output.positionCS =
                    TransformObjectToHClip(
                        input.positionOS.xyz
                    );

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _DarknessColor;
            }

            ENDHLSL
        }
    }
}