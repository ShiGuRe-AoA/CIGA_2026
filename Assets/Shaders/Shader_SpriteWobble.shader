Shader "Custom/SpriteWobble"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        _Color ("Tint", Color) = (1, 1, 1, 1)

        [Header(Animation)]
        _JitterFPS ("Jitter FPS", Range(1, 20)) = 8
        _FrameOffset ("Frame Offset", Float) = 0

        [Header(UV Distortion)]
        _UVStrength ("UV Distortion Strength", Range(0, 0.02)) = 0.002
        _UVFrequency ("UV Distortion Frequency", Range(1, 30)) = 8
        _UVDetailFrequency ("UV Detail Frequency", Range(1, 60)) = 21

        [Header(Vertex Distortion)]
        _VertexStrength ("Vertex Distortion Strength", Range(0, 0.1)) = 0.01
        _VertexFrequency ("Vertex Distortion Frequency", Range(0.1, 20)) = 3

        [Header(Alpha)]
        _AlphaClip ("Alpha Clip", Range(0, 1)) = 0.001

        [Toggle(PIXELSNAP_ON)]
        _PixelSnap ("Pixel Snap", Float) = 0

        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
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

            float _AlphaClip;

            // Returns a stable pseudo-random value in [0, 1].
            float Hash11(float value)
            {
                return frac(sin(value * 127.1) * 43758.5453123);
            }

            // Returns a stable pseudo-random 2D value in [-1, 1].
            float2 Hash21(float value)
            {
                float2 result;

                result.x = Hash11(value + 17.13);
                result.y = Hash11(value + 83.71);

                return result * 2.0 - 1.0;
            }

            // The frame index is always 0, 1 or 2.
            float GetAnimationFrame()
            {
                float rawFrame = floor(
                    _Time.y * max(_JitterFPS, 0.001) +
                    _FrameOffset
                );

                return fmod(rawFrame, 3.0);
            }

            /*
             * Generates a three-frame hand-drawn UV distortion.
             *
             * Horizontal displacement mainly depends on Y.
             * Vertical displacement mainly depends on X.
             *
             * This keeps the sprite recognizable and avoids a liquid-like
             * continuous noise appearance.
             */
            float2 GetUVDistortion(float2 uv, float frame)
            {
                float frameSeed = frame * 41.73 + 3.17;

                float2 phaseA = Hash21(frameSeed) * 6.2831853;
                float2 phaseB = Hash21(frameSeed + 19.37) * 6.2831853;

                float horizontalWave =
                    sin(uv.y * _UVFrequency * 6.2831853 + phaseA.x);

                float verticalWave =
                    sin(uv.x * _UVFrequency * 6.2831853 + phaseA.y);

                float horizontalDetail =
                    sin(
                        uv.y * _UVDetailFrequency * 6.2831853 +
                        uv.x * 3.7 +
                        phaseB.x
                    );

                float verticalDetail =
                    sin(
                        uv.x * _UVDetailFrequency * 6.2831853 +
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
             * Applies a small object-space vertex deformation.
             *
             * A normal Sprite quad only has four vertices, so this mainly
             * creates small skewing and scaling changes. A Tight or subdivided
             * sprite mesh produces richer silhouette deformation.
             */
            float2 GetVertexDistortion(
                float2 positionOS,
                float2 uv,
                float frame
            )
            {
                float frameSeed = frame * 59.41 + 11.73;
                float2 randomDirection = Hash21(frameSeed);

                float waveX = sin(
                    uv.y * _VertexFrequency * 6.2831853 +
                    frameSeed
                );

                float waveY = sin(
                    uv.x * _VertexFrequency * 6.2831853 +
                    frameSeed * 1.37
                );

                float2 localWave = float2(waveX, waveY);

                // Keep the center more stable while allowing edges to wobble.
                float2 centerDistance = abs(uv - 0.5) * 2.0;
                float edgeWeight = saturate(
                    max(centerDistance.x, centerDistance.y)
                );

                float2 distortion =
                    localWave * 0.65 +
                    randomDirection * 0.35;

                return distortion *
                       _VertexStrength *
                       lerp(0.35, 1.0, edgeWeight);
            }

            v2f vert(appdata input)
            {
                v2f output;

                float frame = GetAnimationFrame();

                float4 positionOS = input.vertex;

                positionOS.xy += GetVertexDistortion(
                    positionOS.xy,
                    input.uv,
                    frame
                );

                output.vertex = UnityObjectToClipPos(positionOS);
                output.uv = input.uv;
                output.color = input.color * _Color;

                #ifdef PIXELSNAP_ON
                    output.vertex = UnityPixelSnap(output.vertex);
                #endif

                return output;
            }

            fixed4 SampleSpriteTexture(float2 uv)
            {
                fixed4 color = tex2D(_MainTex, uv);

                #if ETC1_EXTERNAL_ALPHA
                    fixed4 alphaColor = tex2D(_AlphaTex, uv);

                    color.a = lerp(
                        color.a,
                        alphaColor.r,
                        _EnableExternalAlpha
                    );
                #endif

                return color;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float frame = GetAnimationFrame();

                float2 distortedUV =
                    input.uv +
                    GetUVDistortion(input.uv, frame);

                /*
                 * Avoid sampling completely outside the sprite texture.
                 * The half-texel margin reduces transparent-edge bleeding.
                 */
                float2 halfTexel = _MainTex_TexelSize.xy * 0.5;

                distortedUV = clamp(
                    distortedUV,
                    halfTexel,
                    1.0 - halfTexel
                );

                fixed4 color =
                    SampleSpriteTexture(distortedUV) *
                    input.color;

                clip(color.a - _AlphaClip);

                // Premultiplied alpha for Blend One OneMinusSrcAlpha.
                color.rgb *= color.a;

                return color;
            }

            ENDCG
        }
    }

    Fallback "Sprites/Default"
}