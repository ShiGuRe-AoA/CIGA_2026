Shader "Custom/SpriteEffects"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        _Color ("Tint", Color) = (1, 1, 1, 1)

        [Header(Animation)]
        _JitterFPS ("Jitter FPS", Range(1, 20)) = 8
        _FrameOffset ("Frame Offset", Float) = 0

        [Header(UV Distortion)]
        _UVStrength (
            "UV Distortion Strength",
            Range(0, 0.02)
        ) = 0.002

        _UVFrequency (
            "UV Distortion Frequency",
            Range(1, 30)
        ) = 8

        _UVDetailFrequency (
            "UV Detail Frequency",
            Range(1, 60)
        ) = 21

        [Header(Vertex Distortion)]
        _VertexStrength (
            "Vertex Distortion Strength",
            Range(0, 0.1)
        ) = 0.01

        _VertexFrequency (
            "Vertex Distortion Frequency",
            Range(0.1, 20)
        ) = 3

        [Header(Outline)]
        [Toggle]
        _OutlineEnabled (
            "Outline Enabled",
            Float
        ) = 0

        _OutlineColor (
            "Outline Color",
            Color
        ) = (1, 1, 0, 1)

        _OutlineWidth (
            "Outline Width",
            Range(0, 10)
        ) = 2

        [Header(Alpha)]
        _AlphaClip (
            "Alpha Clip",
            Range(0, 1)
        ) = 0.001

        [Toggle(PIXELSNAP_ON)]
        _PixelSnap (
            "Pixel Snap",
            Float
        ) = 0

        [PerRendererData]
        _AlphaTex (
            "External Alpha",
            2D
        ) = "white" {}

        [PerRendererData]
        _EnableExternalAlpha (
            "Enable External Alpha",
            Float
        ) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off

        // The shader outputs premultiplied-alpha colors.
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #pragma target 2.0
            #pragma multi_compile _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            sampler2D _AlphaTex;
            float _EnableExternalAlpha;

            fixed4 _Color;

            float _JitterFPS;
            float _FrameOffset;

            float _UVStrength;
            float _UVFrequency;
            float _UVDetailFrequency;

            float _VertexStrength;
            float _VertexFrequency;

            float _OutlineEnabled;
            fixed4 _OutlineColor;
            float _OutlineWidth;

            float _AlphaClip;

            // Returns a stable pseudo-random value in [0, 1].
            float Hash11(float value)
            {
                return frac(
                    sin(value * 127.1) *
                    43758.5453123
                );
            }

            // Returns a stable pseudo-random 2D value in [-1, 1].
            float2 Hash21(float value)
            {
                float2 result;

                result.x = Hash11(value + 17.13);
                result.y = Hash11(value + 83.71);

                return result * 2.0 - 1.0;
            }

            // The current animation frame is always 0, 1 or 2.
            float GetAnimationFrame()
            {
                float rawFrame = floor(
                    _Time.y *
                    max(_JitterFPS, 0.001) +
                    _FrameOffset
                );

                return fmod(rawFrame, 3.0);
            }

            /*
             * Generates a discrete three-frame UV deformation.
             * Horizontal displacement mainly depends on Y,
             * while vertical displacement mainly depends on X.
             */
            float2 GetUVDistortion(
                float2 uv,
                float frame
            )
            {
                float frameSeed =
                    frame * 41.73 +
                    3.17;

                float2 phaseA =
                    Hash21(frameSeed) *
                    6.2831853;

                float2 phaseB =
                    Hash21(frameSeed + 19.37) *
                    6.2831853;

                float horizontalWave = sin(
                    uv.y *
                    _UVFrequency *
                    6.2831853 +
                    phaseA.x
                );

                float verticalWave = sin(
                    uv.x *
                    _UVFrequency *
                    6.2831853 +
                    phaseA.y
                );

                float horizontalDetail = sin(
                    uv.y *
                    _UVDetailFrequency *
                    6.2831853 +
                    uv.x * 3.7 +
                    phaseB.x
                );

                float verticalDetail = sin(
                    uv.x *
                    _UVDetailFrequency *
                    6.2831853 +
                    uv.y * 4.3 +
                    phaseB.y
                );

                float2 distortion;

                distortion.x =
                    horizontalWave * 0.7 +
                    horizontalDetail * 0.3;

                distortion.y =
                    verticalWave * 0.7 +
                    verticalDetail * 0.3;

                return distortion * _UVStrength;
            }

            /*
             * Applies object-space vertex deformation.
             * A normal four-vertex Sprite mainly shows skewing;
             * a subdivided mesh produces richer silhouette motion.
             */
            float2 GetVertexDistortion(
                float2 positionOS,
                float2 uv,
                float frame
            )
            {
                float frameSeed =
                    frame * 59.41 +
                    11.73;

                float2 randomDirection =
                    Hash21(frameSeed);

                float waveX = sin(
                    uv.y *
                    _VertexFrequency *
                    6.2831853 +
                    frameSeed
                );

                float waveY = sin(
                    uv.x *
                    _VertexFrequency *
                    6.2831853 +
                    frameSeed * 1.37
                );

                float2 localWave =
                    float2(waveX, waveY);

                // Keep the center steadier than the outer region.
                float2 centerDistance =
                    abs(uv - 0.5) *
                    2.0;

                float edgeWeight = saturate(
                    max(
                        centerDistance.x,
                        centerDistance.y
                    )
                );

                float2 distortion =
                    localWave * 0.65 +
                    randomDirection * 0.35;

                return distortion *
                       _VertexStrength *
                       lerp(
                           0.35,
                           1.0,
                           edgeWeight
                       );
            }

            v2f vert(appdata input)
            {
                v2f output;

                float frame =
                    GetAnimationFrame();

                float4 positionOS =
                    input.vertex;

                positionOS.xy +=
                    GetVertexDistortion(
                        positionOS.xy,
                        input.uv,
                        frame
                    );

                output.vertex =
                    UnityObjectToClipPos(positionOS);

                output.uv =
                    input.uv;

                output.color =
                    input.color *
                    _Color;

                #ifdef PIXELSNAP_ON
                    output.vertex =
                        UnityPixelSnap(output.vertex);
                #endif

                return output;
            }

            fixed4 SampleSpriteTexture(float2 uv)
            {
                fixed4 color =
                    tex2D(_MainTex, uv);

                #if ETC1_EXTERNAL_ALPHA
                    fixed4 alphaColor =
                        tex2D(_AlphaTex, uv);

                    color.a = lerp(
                        color.a,
                        alphaColor.r,
                        _EnableExternalAlpha
                    );
                #endif

                return color;
            }

            /*
             * Clamp UVs to the texture bounds.
             * This prevents the wobble and outline samples
             * from wrapping to the opposite side of the texture.
             */
            float2 ClampTextureUV(float2 uv)
            {
                float2 halfTexel =
                    _MainTex_TexelSize.xy *
                    0.5;

                return clamp(
                    uv,
                    halfTexel,
                    1.0 - halfTexel
                );
            }

            /*
             * Samples alpha at one neighboring UV position.
             */
            float SampleNeighbourAlpha(
                float2 uv,
                float2 offset
            )
            {
                return SampleSpriteTexture(
                    ClampTextureUV(
                        uv + offset
                    )
                ).a;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float frame =
                    GetAnimationFrame();

                float2 distortedUV =
                    input.uv +
                    GetUVDistortion(
                        input.uv,
                        frame
                    );

                distortedUV =
                    ClampTextureUV(distortedUV);

                /*
                 * Keep the raw texture alpha separately.
                 * Outline shape detection should use texture alpha,
                 * not the already-tinted body alpha.
                 */
                fixed4 textureColor =
                    SampleSpriteTexture(distortedUV);

                fixed4 bodyColor =
                    textureColor *
                    input.color;

                float bodyAlpha =
                    bodyColor.a;

                float outlineAlpha = 0.0;

                if (_OutlineEnabled > 0.5)
                {
                    float2 offset =
                        _MainTex_TexelSize.xy *
                        _OutlineWidth;

                    float neighbourAlpha = 0.0;

                    // Horizontal and vertical directions.
                    neighbourAlpha = max(
                        neighbourAlpha,
                        SampleNeighbourAlpha(
                            distortedUV,
                            float2(offset.x, 0.0)
                        )
                    );

                    neighbourAlpha = max(
                        neighbourAlpha,
                        SampleNeighbourAlpha(
                            distortedUV,
                            float2(-offset.x, 0.0)
                        )
                    );

                    neighbourAlpha = max(
                        neighbourAlpha,
                        SampleNeighbourAlpha(
                            distortedUV,
                            float2(0.0, offset.y)
                        )
                    );

                    neighbourAlpha = max(
                        neighbourAlpha,
                        SampleNeighbourAlpha(
                            distortedUV,
                            float2(0.0, -offset.y)
                        )
                    );

                    // Diagonal directions.
                    neighbourAlpha = max(
                        neighbourAlpha,
                        SampleNeighbourAlpha(
                            distortedUV,
                            float2(
                                offset.x,
                                offset.y
                            )
                        )
                    );

                    neighbourAlpha = max(
                        neighbourAlpha,
                        SampleNeighbourAlpha(
                            distortedUV,
                            float2(
                                offset.x,
                                -offset.y
                            )
                        )
                    );

                    neighbourAlpha = max(
                        neighbourAlpha,
                        SampleNeighbourAlpha(
                            distortedUV,
                            float2(
                                -offset.x,
                                offset.y
                            )
                        )
                    );

                    neighbourAlpha = max(
                        neighbourAlpha,
                        SampleNeighbourAlpha(
                            distortedUV,
                            float2(
                                -offset.x,
                                -offset.y
                            )
                        )
                    );

                    /*
                     * Keep only neighboring opaque pixels that extend
                     * beyond the current Sprite body.
                     */
                    outlineAlpha = saturate(
                        neighbourAlpha -
                        textureColor.a
                    );

                    outlineAlpha *=
                        _OutlineColor.a *
                        input.color.a;
                }

                /*
                 * Prevent the outline from covering semi-transparent
                 * pixels belonging to the body.
                 */
                float visibleOutlineAlpha =
                    outlineAlpha *
                    (1.0 - bodyAlpha);

                fixed4 finalColor;

                finalColor.a =
                    bodyAlpha +
                    visibleOutlineAlpha;

                /*
                 * Premultiplied-alpha output:
                 *
                 * body RGB      ¡Á body alpha
                 * outline RGB   ¡Á visible outline alpha
                 */
                finalColor.rgb =
                    bodyColor.rgb *
                    bodyAlpha +
                    _OutlineColor.rgb *
                    visibleOutlineAlpha;

                clip(
                    finalColor.a -
                    _AlphaClip
                );

                return finalColor;
            }

            ENDCG
        }
    }

    Fallback "Sprites/Default"
}