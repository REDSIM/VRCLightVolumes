Shader "Hidden/VRCLV/PointLightShadowRuntimeBlur" {
    Properties {
        _SourceArrayTex("Source Texture Array", 2DArray) = "" {}
        _DepthArrayTex("Depth Texture Array", 2DArray) = "" {}
        _FaceIndex("Face Index", Int) = 0
        _SourceBaseSlice("Source Base Slice", Float) = 0
        _DepthBaseSlice("Depth Base Slice", Float) = 0
        _BlurDirection("Blur Direction", Vector) = (1,0,0,0)
        _BlurRadius("Blur Radius", Float) = 0
        _BlurDepth("Blur Depth", Float) = 0.1
        _InvResolution("Inv Resolution", Float) = 0.0078125
        _ShadowTanHalfFov("Shadow Tan Half FOV", Float) = 1
    }

    SubShader {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        UNITY_DECLARE_TEX2DARRAY(_SourceArrayTex);
        #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            UNITY_DECLARE_TEX2DARRAY(_DepthArrayTex);
        #endif
        int _FaceIndex;
        float _SourceBaseSlice;
        float2 _BlurDirection;
        float _BlurRadius;
        float _InvResolution;
        float _ShadowTanHalfFov;

        #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            float _DepthBaseSlice;
            float _BlurDepth;
            #define VRCLV_EVSM_NEGATIVE_EXPONENT 5.0f
        #endif

        // Approximate exp for blur weights and EVSM-related ranges.
        float VRCLV_FastExp(float x) {
            x *= 0.25f;
            float y = 1.0f + x * (1.0f + x * (0.5f + x * (0.16666667f + x * (0.04166667f + x * (0.00833333f + x * 0.00138889f)))));
            y *= y;
            return y * y;
        }

        // Approximate natural log for positive values using frexp and a quadratic log2 mantissa fit.
        float VRCLV_FastLogPositive(float x) {
            float exponent = 0;
            float mantissa = frexp(max(x, 0.000001f), exponent);
            float y = mantissa + mantissa - 1.0f;
            return (exponent - 1.0f + y * (1.3465554f - 0.3465554f * y)) * 0.69314718056f;
        }

        #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY) || defined(VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL)
            #define VRCLV_SHADOW_BLUR_SPHERICAL
        #endif

        #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
            #define VRCLV_BLUR_LOOP [loop]
        #else
            #define VRCLV_BLUR_LOOP [unroll]
        #endif

        #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
            #define VRCLV_BLUR_SAMPLE_RADIUS 63
            #define VRCLV_BLUR_INV_SAMPLE_RADIUS 0.0158730159f
        #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_HIGH)
            #define VRCLV_BLUR_SAMPLE_RADIUS 31
            #define VRCLV_BLUR_INV_SAMPLE_RADIUS 0.0322580645f
        #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_LOW)
            #define VRCLV_BLUR_SAMPLE_RADIUS 7
            #define VRCLV_BLUR_INV_SAMPLE_RADIUS 0.1428571429f
        #else
            #define VRCLV_BLUR_SAMPLE_RADIUS 15
            #define VRCLV_BLUR_INV_SAMPLE_RADIUS 0.0666666667f
        #endif

        #if defined(VRCLV_SHADOW_BLUR_SPHERICAL) || !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                #define VRCLV_CONTRAST_RING_COUNT 32
                #define VRCLV_CONTRAST_DIRECTION_COUNT 16
                #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.001953125f
            #elif defined(VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL)
                #if defined(VRCLV_RUNTIME_SHADOW_QUALITY_HIGH)
                    #define VRCLV_CONTRAST_RING_COUNT 3
                    #define VRCLV_CONTRAST_DIRECTION_COUNT 16
                    #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.0208333333f
                #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_LOW)
                    #define VRCLV_CONTRAST_RING_COUNT 1
                    #define VRCLV_CONTRAST_DIRECTION_COUNT 16
                    #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.0625f
                #else
                    #define VRCLV_CONTRAST_RING_COUNT 2
                    #define VRCLV_CONTRAST_DIRECTION_COUNT 16
                    #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.03125f
                #endif
            #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_HIGH)
                #define VRCLV_CONTRAST_RING_COUNT 2
                #define VRCLV_CONTRAST_DIRECTION_COUNT 8
                #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.0625f
            #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_LOW)
                #define VRCLV_CONTRAST_RING_COUNT 1
                #define VRCLV_CONTRAST_DIRECTION_COUNT 4
                #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.25f
            #else
                #define VRCLV_CONTRAST_RING_COUNT 2
                #define VRCLV_CONTRAST_DIRECTION_COUNT 4
                #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.125f
            #endif
            #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                #define VRCLV_CONTRAST_MAX_RING_RADIUS 1.0f
            #elif VRCLV_CONTRAST_RING_COUNT > 1
                #define VRCLV_CONTRAST_MAX_RING_RADIUS 0.8660254f
            #else
                #define VRCLV_CONTRAST_MAX_RING_RADIUS 0.5f
            #endif
        #endif

        #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
            #define VRCLV_SPHERICAL_BLUR_DIRECTION_COUNT 16
            #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                #define VRCLV_SPHERICAL_BLUR_RING_COUNT 128
                #define VRCLV_SPHERICAL_BLUR_RADIUS_SCALE 1.0547f
            #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_HIGH)
                #define VRCLV_SPHERICAL_BLUR_RING_COUNT 8
                #define VRCLV_SPHERICAL_BLUR_RADIUS_SCALE 1.0475f
            #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_LOW)
                #define VRCLV_SPHERICAL_BLUR_RING_COUNT 2
                #define VRCLV_SPHERICAL_BLUR_RADIUS_SCALE 1.0000f
            #else
                #define VRCLV_SPHERICAL_BLUR_RING_COUNT 4
                #define VRCLV_SPHERICAL_BLUR_RADIUS_SCALE 1.0313f
            #endif
        #endif

        struct appdata {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct v2f {
            float2 uv : TEXCOORD0;
            float4 vertex : SV_POSITION;
        };

        v2f vert(appdata v) {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            o.uv = v.uv;
            return o;
        }

        #if defined(VRCLV_SHADOW_BLUR_SPHERICAL) || !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            static const float contrastRingRadii[4] = {
                0.25f, 0.5f, 0.75f, 1.0f
            };

            static const float2 contrastRingDirections[64] = {
                float2( 1.0000f,  0.0000f), float2( 0.9239f,  0.3827f), float2( 0.7071f,  0.7071f), float2( 0.3827f,  0.9239f), float2( 0.0000f,  1.0000f), float2(-0.3827f,  0.9239f), float2(-0.7071f,  0.7071f), float2(-0.9239f,  0.3827f), float2(-1.0000f,  0.0000f), float2(-0.9239f, -0.3827f), float2(-0.7071f, -0.7071f), float2(-0.3827f, -0.9239f), float2( 0.0000f, -1.0000f), float2( 0.3827f, -0.9239f), float2( 0.7071f, -0.7071f), float2( 0.9239f, -0.3827f),
                float2( 0.9808f,  0.1951f), float2( 0.8315f,  0.5556f), float2( 0.5556f,  0.8315f), float2( 0.1951f,  0.9808f), float2(-0.1951f,  0.9808f), float2(-0.5556f,  0.8315f), float2(-0.8315f,  0.5556f), float2(-0.9808f,  0.1951f), float2(-0.9808f, -0.1951f), float2(-0.8315f, -0.5556f), float2(-0.5556f, -0.8315f), float2(-0.1951f, -0.9808f), float2( 0.1951f, -0.9808f), float2( 0.5556f, -0.8315f), float2( 0.8315f, -0.5556f), float2( 0.9808f, -0.1951f),
                float2( 0.9239f,  0.3827f), float2( 0.7071f,  0.7071f), float2( 0.3827f,  0.9239f), float2( 0.0000f,  1.0000f), float2(-0.3827f,  0.9239f), float2(-0.7071f,  0.7071f), float2(-0.9239f,  0.3827f), float2(-1.0000f,  0.0000f), float2(-0.9239f, -0.3827f), float2(-0.7071f, -0.7071f), float2(-0.3827f, -0.9239f), float2( 0.0000f, -1.0000f), float2( 0.3827f, -0.9239f), float2( 0.7071f, -0.7071f), float2( 0.9239f, -0.3827f), float2( 1.0000f,  0.0000f),
                float2( 0.8315f,  0.5556f), float2( 0.5556f,  0.8315f), float2( 0.1951f,  0.9808f), float2(-0.1951f,  0.9808f), float2(-0.5556f,  0.8315f), float2(-0.8315f,  0.5556f), float2(-0.9808f,  0.1951f), float2(-0.9808f, -0.1951f), float2(-0.8315f, -0.5556f), float2(-0.5556f, -0.8315f), float2(-0.1951f, -0.9808f), float2( 0.1951f, -0.9808f), float2( 0.5556f, -0.8315f), float2( 0.8315f, -0.5556f), float2( 0.9808f, -0.1951f), float2( 0.9808f,  0.1951f)
            };

            float ContrastRingRadius(int ringIndex) {
                #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                    return (ringIndex + 1.0f) * (1.0f / VRCLV_CONTRAST_RING_COUNT);
                #else
                    return contrastRingRadii[ringIndex];
                #endif
            }

            float2 ContrastRingDirection(int ringIndex, int sampleIndex) {
                #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                    return contrastRingDirections[(int)((((uint)ringIndex) & 3u) * 16u + (uint)sampleIndex)];
                #else
                    return contrastRingDirections[ringIndex * 16 + sampleIndex];
                #endif
            }
        #endif

        #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
            #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                float SphericalBlurRingRadius(int ringIndex) {
                    return (ringIndex + 1.0f) * (1.0f / VRCLV_SPHERICAL_BLUR_RING_COUNT);
                }

                float SphericalBlurRingWeight(int ringIndex) {
                    float ringRadius = SphericalBlurRingRadius(ringIndex);
                    return ringRadius * VRCLV_FastExp(-2.0f * ringRadius * ringRadius);
                }
            #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_HIGH)
                static const float sphericalBlurRingRadii[8] = {
                    0.1250000000f, 0.2500000000f, 0.3750000000f, 0.5000000000f,
                    0.6250000000f, 0.7500000000f, 0.8750000000f, 1.0000000000f
                };

                static const float sphericalBlurRingWeights[8] = {
                    0.1211541543f, 0.2206242256f, 0.2830648507f, 0.3032653299f,
                    0.2861458511f, 0.2434893505f, 0.1892320210f, 0.1353352832f
                };
            #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_LOW)
                static const float sphericalBlurRingRadii[2] = {
                    0.5000000000f, 1.0000000000f
                };

                static const float sphericalBlurRingWeights[2] = {
                    0.3032653299f, 0.1353352832f
                };
            #else
                static const float sphericalBlurRingRadii[4] = {
                    0.2500000000f, 0.5000000000f, 0.7500000000f, 1.0000000000f
                };

                static const float sphericalBlurRingWeights[4] = {
                    0.2206242256f, 0.3032653299f, 0.2434893505f, 0.1353352832f
                };
            #endif

            #if !defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                float SphericalBlurRingRadius(int ringIndex) {
                    return sphericalBlurRingRadii[ringIndex];
                }

                float SphericalBlurRingWeight(int ringIndex) {
                    return sphericalBlurRingWeights[ringIndex];
                }
            #endif

            static const float2 sphericalBlurDirections[256] = {
                float2( 1.0000f,  0.0000f), float2( 0.9239f,  0.3827f), float2( 0.7071f,  0.7071f), float2( 0.3827f,  0.9239f), float2( 0.0000f,  1.0000f), float2(-0.3827f,  0.9239f), float2(-0.7071f,  0.7071f), float2(-0.9239f,  0.3827f), float2(-1.0000f,  0.0000f), float2(-0.9239f, -0.3827f), float2(-0.7071f, -0.7071f), float2(-0.3827f, -0.9239f), float2( 0.0000f, -1.0000f), float2( 0.3827f, -0.9239f), float2( 0.7071f, -0.7071f), float2( 0.9239f, -0.3827f),
                float2( 0.9808f,  0.1951f), float2( 0.8315f,  0.5556f), float2( 0.5556f,  0.8315f), float2( 0.1951f,  0.9808f), float2(-0.1951f,  0.9808f), float2(-0.5556f,  0.8315f), float2(-0.8315f,  0.5556f), float2(-0.9808f,  0.1951f), float2(-0.9808f, -0.1951f), float2(-0.8315f, -0.5556f), float2(-0.5556f, -0.8315f), float2(-0.1951f, -0.9808f), float2( 0.1951f, -0.9808f), float2( 0.5556f, -0.8315f), float2( 0.8315f, -0.5556f), float2( 0.9808f, -0.1951f),
                float2( 0.9239f,  0.3827f), float2( 0.7071f,  0.7071f), float2( 0.3827f,  0.9239f), float2( 0.0000f,  1.0000f), float2(-0.3827f,  0.9239f), float2(-0.7071f,  0.7071f), float2(-0.9239f,  0.3827f), float2(-1.0000f,  0.0000f), float2(-0.9239f, -0.3827f), float2(-0.7071f, -0.7071f), float2(-0.3827f, -0.9239f), float2( 0.0000f, -1.0000f), float2( 0.3827f, -0.9239f), float2( 0.7071f, -0.7071f), float2( 0.9239f, -0.3827f), float2( 1.0000f,  0.0000f),
                float2( 0.8315f,  0.5556f), float2( 0.5556f,  0.8315f), float2( 0.1951f,  0.9808f), float2(-0.1951f,  0.9808f), float2(-0.5556f,  0.8315f), float2(-0.8315f,  0.5556f), float2(-0.9808f,  0.1951f), float2(-0.9808f, -0.1951f), float2(-0.8315f, -0.5556f), float2(-0.5556f, -0.8315f), float2(-0.1951f, -0.9808f), float2( 0.1951f, -0.9808f), float2( 0.5556f, -0.8315f), float2( 0.8315f, -0.5556f), float2( 0.9808f, -0.1951f), float2( 0.9808f,  0.1951f),
                float2( 0.7071f,  0.7071f), float2( 0.3827f,  0.9239f), float2( 0.0000f,  1.0000f), float2(-0.3827f,  0.9239f), float2(-0.7071f,  0.7071f), float2(-0.9239f,  0.3827f), float2(-1.0000f,  0.0000f), float2(-0.9239f, -0.3827f), float2(-0.7071f, -0.7071f), float2(-0.3827f, -0.9239f), float2( 0.0000f, -1.0000f), float2( 0.3827f, -0.9239f), float2( 0.7071f, -0.7071f), float2( 0.9239f, -0.3827f), float2( 1.0000f,  0.0000f), float2( 0.9239f,  0.3827f),
                float2( 0.5556f,  0.8315f), float2( 0.1951f,  0.9808f), float2(-0.1951f,  0.9808f), float2(-0.5556f,  0.8315f), float2(-0.8315f,  0.5556f), float2(-0.9808f,  0.1951f), float2(-0.9808f, -0.1951f), float2(-0.8315f, -0.5556f), float2(-0.5556f, -0.8315f), float2(-0.1951f, -0.9808f), float2( 0.1951f, -0.9808f), float2( 0.5556f, -0.8315f), float2( 0.8315f, -0.5556f), float2( 0.9808f, -0.1951f), float2( 0.9808f,  0.1951f), float2( 0.8315f,  0.5556f),
                float2( 0.3827f,  0.9239f), float2( 0.0000f,  1.0000f), float2(-0.3827f,  0.9239f), float2(-0.7071f,  0.7071f), float2(-0.9239f,  0.3827f), float2(-1.0000f,  0.0000f), float2(-0.9239f, -0.3827f), float2(-0.7071f, -0.7071f), float2(-0.3827f, -0.9239f), float2( 0.0000f, -1.0000f), float2( 0.3827f, -0.9239f), float2( 0.7071f, -0.7071f), float2( 0.9239f, -0.3827f), float2( 1.0000f,  0.0000f), float2( 0.9239f,  0.3827f), float2( 0.7071f,  0.7071f),
                float2( 0.1951f,  0.9808f), float2(-0.1951f,  0.9808f), float2(-0.5556f,  0.8315f), float2(-0.8315f,  0.5556f), float2(-0.9808f,  0.1951f), float2(-0.9808f, -0.1951f), float2(-0.8315f, -0.5556f), float2(-0.5556f, -0.8315f), float2(-0.1951f, -0.9808f), float2( 0.1951f, -0.9808f), float2( 0.5556f, -0.8315f), float2( 0.8315f, -0.5556f), float2( 0.9808f, -0.1951f), float2( 0.9808f,  0.1951f), float2( 0.8315f,  0.5556f), float2( 0.5556f,  0.8315f),
                float2( 0.0000f,  1.0000f), float2(-0.3827f,  0.9239f), float2(-0.7071f,  0.7071f), float2(-0.9239f,  0.3827f), float2(-1.0000f,  0.0000f), float2(-0.9239f, -0.3827f), float2(-0.7071f, -0.7071f), float2(-0.3827f, -0.9239f), float2( 0.0000f, -1.0000f), float2( 0.3827f, -0.9239f), float2( 0.7071f, -0.7071f), float2( 0.9239f, -0.3827f), float2( 1.0000f,  0.0000f), float2( 0.9239f,  0.3827f), float2( 0.7071f,  0.7071f), float2( 0.3827f,  0.9239f),
                float2(-0.1951f,  0.9808f), float2(-0.5556f,  0.8315f), float2(-0.8315f,  0.5556f), float2(-0.9808f,  0.1951f), float2(-0.9808f, -0.1951f), float2(-0.8315f, -0.5556f), float2(-0.5556f, -0.8315f), float2(-0.1951f, -0.9808f), float2( 0.1951f, -0.9808f), float2( 0.5556f, -0.8315f), float2( 0.8315f, -0.5556f), float2( 0.9808f, -0.1951f), float2( 0.9808f,  0.1951f), float2( 0.8315f,  0.5556f), float2( 0.5556f,  0.8315f), float2( 0.1951f,  0.9808f),
                float2(-0.3827f,  0.9239f), float2(-0.7071f,  0.7071f), float2(-0.9239f,  0.3827f), float2(-1.0000f,  0.0000f), float2(-0.9239f, -0.3827f), float2(-0.7071f, -0.7071f), float2(-0.3827f, -0.9239f), float2( 0.0000f, -1.0000f), float2( 0.3827f, -0.9239f), float2( 0.7071f, -0.7071f), float2( 0.9239f, -0.3827f), float2( 1.0000f,  0.0000f), float2( 0.9239f,  0.3827f), float2( 0.7071f,  0.7071f), float2( 0.3827f,  0.9239f), float2( 0.0000f,  1.0000f),
                float2(-0.5556f,  0.8315f), float2(-0.8315f,  0.5556f), float2(-0.9808f,  0.1951f), float2(-0.9808f, -0.1951f), float2(-0.8315f, -0.5556f), float2(-0.5556f, -0.8315f), float2(-0.1951f, -0.9808f), float2( 0.1951f, -0.9808f), float2( 0.5556f, -0.8315f), float2( 0.8315f, -0.5556f), float2( 0.9808f, -0.1951f), float2( 0.9808f,  0.1951f), float2( 0.8315f,  0.5556f), float2( 0.5556f,  0.8315f), float2( 0.1951f,  0.9808f), float2(-0.1951f,  0.9808f),
                float2(-0.7071f,  0.7071f), float2(-0.9239f,  0.3827f), float2(-1.0000f,  0.0000f), float2(-0.9239f, -0.3827f), float2(-0.7071f, -0.7071f), float2(-0.3827f, -0.9239f), float2( 0.0000f, -1.0000f), float2( 0.3827f, -0.9239f), float2( 0.7071f, -0.7071f), float2( 0.9239f, -0.3827f), float2( 1.0000f,  0.0000f), float2( 0.9239f,  0.3827f), float2( 0.7071f,  0.7071f), float2( 0.3827f,  0.9239f), float2( 0.0000f,  1.0000f), float2(-0.3827f,  0.9239f),
                float2(-0.8315f,  0.5556f), float2(-0.9808f,  0.1951f), float2(-0.9808f, -0.1951f), float2(-0.8315f, -0.5556f), float2(-0.5556f, -0.8315f), float2(-0.1951f, -0.9808f), float2( 0.1951f, -0.9808f), float2( 0.5556f, -0.8315f), float2( 0.8315f, -0.5556f), float2( 0.9808f, -0.1951f), float2( 0.9808f,  0.1951f), float2( 0.8315f,  0.5556f), float2( 0.5556f,  0.8315f), float2( 0.1951f,  0.9808f), float2(-0.1951f,  0.9808f), float2(-0.5556f,  0.8315f),
                float2(-0.9239f,  0.3827f), float2(-1.0000f,  0.0000f), float2(-0.9239f, -0.3827f), float2(-0.7071f, -0.7071f), float2(-0.3827f, -0.9239f), float2( 0.0000f, -1.0000f), float2( 0.3827f, -0.9239f), float2( 0.7071f, -0.7071f), float2( 0.9239f, -0.3827f), float2( 1.0000f,  0.0000f), float2( 0.9239f,  0.3827f), float2( 0.7071f,  0.7071f), float2( 0.3827f,  0.9239f), float2( 0.0000f,  1.0000f), float2(-0.3827f,  0.9239f), float2(-0.7071f,  0.7071f),
                float2(-0.9808f,  0.1951f), float2(-0.9808f, -0.1951f), float2(-0.8315f, -0.5556f), float2(-0.5556f, -0.8315f), float2(-0.1951f, -0.9808f), float2( 0.1951f, -0.9808f), float2( 0.5556f, -0.8315f), float2( 0.8315f, -0.5556f), float2( 0.9808f, -0.1951f), float2( 0.9808f,  0.1951f), float2( 0.8315f,  0.5556f), float2( 0.5556f,  0.8315f), float2( 0.1951f,  0.9808f), float2(-0.1951f,  0.9808f), float2(-0.5556f,  0.8315f), float2(-0.8315f,  0.5556f)
            };

            float2 SphericalBlurRingDirection(int ringIndex, int sampleIndex) {
                #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                    return sphericalBlurDirections[(int)((((uint)ringIndex) & 15u) * 16u + (uint)sampleIndex)];
                #else
                    return sphericalBlurDirections[ringIndex * 16 + sampleIndex];
                #endif
            }
        #endif

        static const float3 faceDirs[6][3] = {
            { float3( 1,  0,  0), float3( 0,  0, -1), float3(0, -1, 0) },
            { float3(-1,  0,  0), float3( 0,  0,  1), float3(0, -1, 0) },
            { float3( 0,  1,  0), float3( 1,  0,  0), float3(0,  0, 1) },
            { float3( 0, -1,  0), float3( 1,  0,  0), float3(0,  0, -1) },
            { float3( 0,  0,  1), float3( 1,  0,  0), float3(0, -1, 0) },
            { float3( 0,  0, -1), float3(-1,  0,  0), float3(0, -1, 0) }
        };

        float GaussianWeight(float normalizedDistance) {
            return VRCLV_FastExp(-2.0f * normalizedDistance * normalizedDistance);
        }

        bool KernelFitsFace(float2 uv, float2 absExtent) {
            float2 edgeDistance = min(uv, 1.0f - uv);
            return edgeDistance.x >= absExtent.x && edgeDistance.y >= absExtent.y;
        }

        #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            float DecodeDepth01(float4 moments) {
                float negativeMagnitude = max(-moments.y, 0.000001f);
                float depth = -VRCLV_FastLogPositive(negativeMagnitude) * rcp(VRCLV_EVSM_NEGATIVE_EXPONENT);
                return saturate(depth * 0.5f + 0.5f);
            }
        #endif

        float3 FaceUvToDirection(float2 uv) {
            float2 faceUv = uv * 2.0f - 1.0f;
            return normalize(faceDirs[_FaceIndex][0] + faceUv.x * faceDirs[_FaceIndex][1] + faceUv.y * faceDirs[_FaceIndex][2]);
        }

        #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
            #if defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                float3 SpotUvToDirection(float2 uv) {
                    float tanHalfFov = max(_ShadowTanHalfFov, 0.000001f);
                    float2 projectedUv = (uv * 2.0f - 1.0f) * tanHalfFov;
                    return normalize(float3(projectedUv.x, projectedUv.y, 1.0f));
                }

                float2 DirectionToSpotUv(float3 dir) {
                    float tanHalfFov = max(_ShadowTanHalfFov, 0.000001f);
                    float safeZ = max(dir.z, 0.000001f);
                    float2 projectedUv = dir.xy * rcp(safeZ * tanHalfFov);
                    return saturate(projectedUv * 0.5f + 0.5f);
                }

                float3 SpotUvToSphericalDirection(float2 uv, float2 faceUvOffset) {
                    float3 centerDir = SpotUvToDirection(uv);
                    float offsetLength = length(faceUvOffset);
                    float2 offsetDir = faceUvOffset * rcp(max(offsetLength, 0.000001f));
                    float3 planeAxis = float3(offsetDir.x, offsetDir.y, 0.0f);
                    float3 tangentDir = planeAxis - centerDir * dot(planeAxis, centerDir);
                    tangentDir *= rsqrt(max(dot(tangentDir, tangentDir), 0.000001f));
                    return normalize(centerDir + tangentDir * offsetLength);
                }

                float2 SphericalSpotUv(float2 uv, float2 faceUvOffset) {
                    return DirectionToSpotUv(SpotUvToSphericalDirection(uv, faceUvOffset));
                }
            #else
                float3 FaceUvToSphericalDirection(float2 uv, float2 faceUvOffset) {
                    float3 centerDir = FaceUvToDirection(uv);
                    float offsetLength = length(faceUvOffset);
                    float2 offsetDir = faceUvOffset * rcp(max(offsetLength, 0.000001f));
                    float3 faceAxis = faceDirs[_FaceIndex][1] * offsetDir.x + faceDirs[_FaceIndex][2] * offsetDir.y;
                    float3 tangentDir = faceAxis - centerDir * dot(faceAxis, centerDir);
                    tangentDir *= rsqrt(max(dot(tangentDir, tangentDir), 0.000001f));
                    return normalize(centerDir + tangentDir * offsetLength);
                }
            #endif
        #endif

        float3 DirectionToArrayUv(float3 dir) {
            float2 uv;
            float face;
            float3 absDir = abs(dir);
            if (absDir.x >= absDir.y && absDir.x >= absDir.z) {
                face = dir.x > 0 ? 0.0f : 1.0f;
                uv = float2((dir.x > 0 ? -dir.z : dir.z), -dir.y) * rcp(absDir.x);
            } else if (absDir.y >= absDir.z) {
                face = dir.y > 0 ? 2.0f : 3.0f;
                uv = float2(dir.x, (dir.y > 0 ? dir.z : -dir.z)) * rcp(absDir.y);
            } else {
                face = dir.z > 0 ? 4.0f : 5.0f;
                uv = float2((dir.z > 0 ? dir.x : -dir.x), -dir.y) * rcp(absDir.z);
            }
            return float3(uv * 0.5f + 0.5f, face);
        }

        float3 ArrayAddress(float2 uv) {
            return DirectionToArrayUv(FaceUvToDirection(uv));
        }

        float4 SampleSource(float2 uv) {
            float3 address = ArrayAddress(uv);
            address.z += _SourceBaseSlice;
            return UNITY_SAMPLE_TEX2DARRAY(_SourceArrayTex, address);
        }

        #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
            #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                float3 SphericalArrayAddress(float2 uv, float2 faceUvOffset) {
                    return DirectionToArrayUv(FaceUvToSphericalDirection(uv, faceUvOffset));
                }
            #endif

            float4 SampleSourceSpherical(float2 uv, float2 faceUvOffset) {
                #if defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                    return UNITY_SAMPLE_TEX2DARRAY(_SourceArrayTex, float3(SphericalSpotUv(uv, faceUvOffset), _SourceBaseSlice + _FaceIndex));
                #else
                    float3 address = SphericalArrayAddress(uv, faceUvOffset);
                    address.z += _SourceBaseSlice;
                    return UNITY_SAMPLE_TEX2DARRAY(_SourceArrayTex, address);
                #endif
            }
        #endif

        float4 SampleSourceDirect(float2 uv) {
            return UNITY_SAMPLE_TEX2DARRAY(_SourceArrayTex, float3(uv, _SourceBaseSlice + _FaceIndex));
        }

        #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            float4 SampleDepth(float2 uv) {
                float3 address = ArrayAddress(uv);
                address.z += _DepthBaseSlice;
                return UNITY_SAMPLE_TEX2DARRAY(_DepthArrayTex, address);
            }

            float4 SampleDepthDirect(float2 uv) {
                return UNITY_SAMPLE_TEX2DARRAY(_DepthArrayTex, float3(uv, _DepthBaseSlice + _FaceIndex));
            }

            #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
                float4 SampleDepthSpherical(float2 uv, float2 faceUvOffset) {
                    #if defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                        return UNITY_SAMPLE_TEX2DARRAY(_DepthArrayTex, float3(SphericalSpotUv(uv, faceUvOffset), _DepthBaseSlice + _FaceIndex));
                    #else
                        float3 address = SphericalArrayAddress(uv, faceUvOffset);
                        address.z += _DepthBaseSlice;
                        return UNITY_SAMPLE_TEX2DARRAY(_DepthArrayTex, address);
                    #endif
                }
            #endif

            float DepthDifferenceRing(float2 uv, float centerDepth, float2 sampleScale, int ringIndex) {
                float depthDifference = 0.0f;
                float ringRadius = ContrastRingRadius(ringIndex);
                VRCLV_BLUR_LOOP for (int sampleIndex = 0; sampleIndex < VRCLV_CONTRAST_DIRECTION_COUNT; sampleIndex++) {
                    float2 rotatedDir = ContrastRingDirection(ringIndex, sampleIndex);
                    depthDifference += abs(DecodeDepth01(SampleDepth(uv + rotatedDir * (sampleScale * ringRadius))) - centerDepth);
                }
                return depthDifference;
            }

            #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
                float DepthDifferenceRingSpherical(float2 uv, float centerDepth, float2 sampleScale, int ringIndex) {
                    float depthDifference = 0.0f;
                    float ringRadius = ContrastRingRadius(ringIndex);
                    VRCLV_BLUR_LOOP for (int sampleIndex = 0; sampleIndex < VRCLV_CONTRAST_DIRECTION_COUNT; sampleIndex++) {
                        float2 rotatedDir = ContrastRingDirection(ringIndex, sampleIndex);
                        depthDifference += abs(DecodeDepth01(SampleDepthSpherical(uv, rotatedDir * (sampleScale * ringRadius * 2.0f))) - centerDepth);
                    }
                    return depthDifference;
                }
            #endif

            float DepthDifferenceRingDirect(float2 uv, float centerDepth, float2 sampleScale, int ringIndex) {
                float depthDifference = 0.0f;
                float ringRadius = ContrastRingRadius(ringIndex);
                VRCLV_BLUR_LOOP for (int sampleIndex = 0; sampleIndex < VRCLV_CONTRAST_DIRECTION_COUNT; sampleIndex++) {
                    float2 rotatedDir = ContrastRingDirection(ringIndex, sampleIndex);
                    depthDifference += abs(DecodeDepth01(SampleDepthDirect(uv + rotatedDir * (sampleScale * ringRadius))) - centerDepth);
                }
                return depthDifference;
            }

            float AverageDepthDifference(float2 uv, float centerDepth, float2 sampleScale) {
                float depthDifference = 0.0f;
                VRCLV_BLUR_LOOP for (int ringIndex = 0; ringIndex < VRCLV_CONTRAST_RING_COUNT; ringIndex++) {
                    depthDifference += DepthDifferenceRing(uv, centerDepth, sampleScale, ringIndex);
                }
                return depthDifference * VRCLV_CONTRAST_INV_SAMPLE_COUNT;
            }

            #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
                float AverageDepthDifferenceSpherical(float2 uv, float centerDepth, float2 sampleScale) {
                    float depthDifference = 0.0f;
                    VRCLV_BLUR_LOOP for (int ringIndex = 0; ringIndex < VRCLV_CONTRAST_RING_COUNT; ringIndex++) {
                        depthDifference += DepthDifferenceRingSpherical(uv, centerDepth, sampleScale, ringIndex);
                    }
                    return depthDifference * VRCLV_CONTRAST_INV_SAMPLE_COUNT;
                }
            #endif

            float AverageDepthDifferenceDirect(float2 uv, float centerDepth, float2 sampleScale) {
                float depthDifference = 0.0f;
                VRCLV_BLUR_LOOP for (int ringIndex = 0; ringIndex < VRCLV_CONTRAST_RING_COUNT; ringIndex++) {
                    depthDifference += DepthDifferenceRingDirect(uv, centerDepth, sampleScale, ringIndex);
                }
                return depthDifference * VRCLV_CONTRAST_INV_SAMPLE_COUNT;
            }
        #endif

        float RuntimeBlurRadius(float2 uv) {
            float radius = max(_BlurRadius, 0.0f);
            #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
                float centerDepth = DecodeDepth01(SampleDepthDirect(uv));
                float2 contrastSampleScale = _InvResolution * max(radius, 0.0001f) * 2.0f;
                float depthDifference;
                #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
                    depthDifference = AverageDepthDifferenceSpherical(uv, centerDepth, contrastSampleScale * VRCLV_SPHERICAL_BLUR_RADIUS_SCALE);
                #elif defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                    depthDifference = AverageDepthDifferenceDirect(uv, centerDepth, contrastSampleScale * rcp(max(_ShadowTanHalfFov, 0.000001f)));
                #else
                    float2 contrastExtent = contrastSampleScale * VRCLV_CONTRAST_MAX_RING_RADIUS;
                    [branch] if (KernelFitsFace(uv, contrastExtent)) depthDifference = AverageDepthDifferenceDirect(uv, centerDepth, contrastSampleScale);
                    else depthDifference = AverageDepthDifference(uv, centerDepth, contrastSampleScale);
                #endif
                radius *= saturate(depthDifference * rcp(_BlurDepth));
            #endif
            return radius;
        }

        float2 RuntimeBlurStep(float2 uv) {
            float radius = RuntimeBlurRadius(uv);
            float spotScale = 1.0f;
            #if defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                spotScale = rcp(max(_ShadowTanHalfFov, 0.000001f));
            #endif
            return _BlurDirection * (_InvResolution * radius * (2.0f * VRCLV_BLUR_INV_SAMPLE_RADIUS) * spotScale);
        }

        float4 BlurArrayDirect(float2 uv, float2 sampleStep) {
            float4 color = 0.0f;
            float weightSum = 0.0f;
            VRCLV_BLUR_LOOP for (int sampleIndex = -VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex <= VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex++) {
                float sampleDistance = sampleIndex * VRCLV_BLUR_INV_SAMPLE_RADIUS;
                float weight = GaussianWeight(sampleDistance);
                color += SampleSourceDirect(uv + sampleStep * sampleIndex) * weight;
                weightSum += weight;
            }
            return color * rcp(weightSum);
        }

        float4 BlurArraySeamAware(float2 uv, float2 sampleStep) {
            float4 color = 0.0f;
            float weightSum = 0.0f;
            VRCLV_BLUR_LOOP for (int sampleIndex = -VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex <= VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex++) {
                float sampleDistance = sampleIndex * VRCLV_BLUR_INV_SAMPLE_RADIUS;
                float weight = GaussianWeight(sampleDistance);
                color += SampleSource(uv + sampleStep * sampleIndex) * weight;
                weightSum += weight;
            }
            return color * rcp(weightSum);
        }

        #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
            float4 BlurArraySpherical(float2 uv) {
                float4 color = SampleSourceSpherical(uv, float2(0.0f, 0.0f));
                float weightSum = 1.0f;
                float blurRadius = _InvResolution * RuntimeBlurRadius(uv) * (4.0f * VRCLV_SPHERICAL_BLUR_RADIUS_SCALE);
                VRCLV_BLUR_LOOP for (int ringIndex = 0; ringIndex < VRCLV_SPHERICAL_BLUR_RING_COUNT; ringIndex++) {
                    float ringRadius = SphericalBlurRingRadius(ringIndex);
                    float weight = SphericalBlurRingWeight(ringIndex);
                    VRCLV_BLUR_LOOP for (int sampleIndex = 0; sampleIndex < VRCLV_SPHERICAL_BLUR_DIRECTION_COUNT; sampleIndex++) {
                        float2 dir = SphericalBlurRingDirection(ringIndex, sampleIndex);
                        color += SampleSourceSpherical(uv, dir * (blurRadius * ringRadius)) * weight;
                        weightSum += weight;
                    }
                }
                return color * rcp(weightSum);
            }
        #endif

        float4 BlurArray(float2 uv) {
            float2 sampleStep = RuntimeBlurStep(uv);
            float2 blurExtent = abs(sampleStep) * VRCLV_BLUR_SAMPLE_RADIUS;
            [branch] if (KernelFitsFace(uv, blurExtent)) {
                return BlurArrayDirect(uv, sampleStep);
            } else {
                return BlurArraySeamAware(uv, sampleStep);
            }
        }

        float4 fragArray(v2f i) : SV_Target {
#if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
            return BlurArraySpherical(i.uv);
#elif defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
            return BlurArrayDirect(i.uv, RuntimeBlurStep(i.uv));
#else
            return BlurArray(i.uv);
#endif
        }
        ENDCG

        Pass {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment fragArray
            #pragma multi_compile_local_fragment VRCLV_RUNTIME_SHADOW_QUALITY_LOW VRCLV_RUNTIME_SHADOW_QUALITY_MEDIUM VRCLV_RUNTIME_SHADOW_QUALITY_HIGH
            #pragma multi_compile_local_fragment __ VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM
            #pragma shader_feature_local_fragment __ VRCLV_RUNTIME_SHADOW_BLUR_DIRECT
            #pragma shader_feature_local_fragment __ VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL
            #pragma shader_feature_local_fragment __ VRCLV_EDITOR_SHADOW_BLUR_QUALITY
            ENDCG
        }
    }
}
