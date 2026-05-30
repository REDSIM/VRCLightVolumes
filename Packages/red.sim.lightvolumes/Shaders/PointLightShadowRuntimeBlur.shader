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

        #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            float _DepthBaseSlice;
            float _BlurDepth;
            #define VRCLV_EVSM_NEGATIVE_EXPONENT 5.0f
        #endif

        #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
            #define VRCLV_BLUR_SAMPLE_RADIUS 32
            #define VRCLV_BLUR_INV_SAMPLE_RADIUS 0.03125f
        #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_HIGH)
            #define VRCLV_BLUR_SAMPLE_RADIUS 8
            #define VRCLV_BLUR_INV_SAMPLE_RADIUS 0.125f
        #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_LOW)
            #define VRCLV_BLUR_SAMPLE_RADIUS 3
            #define VRCLV_BLUR_INV_SAMPLE_RADIUS 0.3333333333f
        #else
            #define VRCLV_BLUR_SAMPLE_RADIUS 5
            #define VRCLV_BLUR_INV_SAMPLE_RADIUS 0.2f
        #endif

        #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                #define VRCLV_CONTRAST_RING_COUNT 4
                #define VRCLV_CONTRAST_DIRECTION_COUNT 16
                #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.015625f
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

        #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            static const float contrastRingRadii[4] = {
                0.25f, 0.5f, 0.75f, 1.0f
            };

            static const float2 contrastRingRotations[4] = {
                float2(1.0000f, 0.0000f),
                float2(0.9808f, 0.1951f),
                float2(0.9239f, 0.3827f),
                float2(0.8315f, 0.5556f)
            };

            static const float2 contrastDirections[16] = {
                float2( 1.0000f,  0.0000f), float2( 0.9239f,  0.3827f),
                float2( 0.7071f,  0.7071f), float2( 0.3827f,  0.9239f),
                float2( 0.0000f,  1.0000f), float2(-0.3827f,  0.9239f),
                float2(-0.7071f,  0.7071f), float2(-0.9239f,  0.3827f),
                float2(-1.0000f,  0.0000f), float2(-0.9239f, -0.3827f),
                float2(-0.7071f, -0.7071f), float2(-0.3827f, -0.9239f),
                float2( 0.0000f, -1.0000f), float2( 0.3827f, -0.9239f),
                float2( 0.7071f, -0.7071f), float2( 0.9239f, -0.3827f)
            };
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
            return exp(-2.0f * normalizedDistance * normalizedDistance);
        }

        bool KernelFitsFace(float2 uv, float2 absExtent) {
            float2 edgeDistance = min(uv, 1.0f - uv);
            return edgeDistance.x >= absExtent.x && edgeDistance.y >= absExtent.y;
        }

        #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            float DecodeDepth01(float4 moments) {
                float negativeMagnitude = max(-moments.y, 0.000001f);
                float depth = -log(negativeMagnitude) * rcp(VRCLV_EVSM_NEGATIVE_EXPONENT);
                return saturate(depth * 0.5f + 0.5f);
            }
        #endif

        float3 FaceUvToDirection(float2 uv) {
            float2 faceUv = uv * 2.0f - 1.0f;
            return normalize(faceDirs[_FaceIndex][0] + faceUv.x * faceDirs[_FaceIndex][1] + faceUv.y * faceDirs[_FaceIndex][2]);
        }

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

            float DepthDifferenceRing(float2 uv, float centerDepth, float2 sampleScale, int ringIndex) {
                float depthDifference = 0.0f;
                float2 ringRotation = contrastRingRotations[ringIndex];
                float ringRadius = contrastRingRadii[ringIndex];
                [unroll] for (int sampleIndex = 0; sampleIndex < VRCLV_CONTRAST_DIRECTION_COUNT; sampleIndex++) {
                    float2 dir = contrastDirections[sampleIndex];
                    float2 rotatedDir = float2(dir.x * ringRotation.x - dir.y * ringRotation.y, dir.x * ringRotation.y + dir.y * ringRotation.x);
                    depthDifference += abs(DecodeDepth01(SampleDepth(uv + rotatedDir * (sampleScale * ringRadius))) - centerDepth);
                }
                return depthDifference;
            }

            float DepthDifferenceRingDirect(float2 uv, float centerDepth, float2 sampleScale, int ringIndex) {
                float depthDifference = 0.0f;
                float2 ringRotation = contrastRingRotations[ringIndex];
                float ringRadius = contrastRingRadii[ringIndex];
                [unroll] for (int sampleIndex = 0; sampleIndex < VRCLV_CONTRAST_DIRECTION_COUNT; sampleIndex++) {
                    float2 dir = contrastDirections[sampleIndex];
                    float2 rotatedDir = float2(dir.x * ringRotation.x - dir.y * ringRotation.y, dir.x * ringRotation.y + dir.y * ringRotation.x);
                    depthDifference += abs(DecodeDepth01(SampleDepthDirect(uv + rotatedDir * (sampleScale * ringRadius))) - centerDepth);
                }
                return depthDifference;
            }

            float AverageDepthDifference(float2 uv, float centerDepth, float2 sampleScale) {
                float depthDifference = 0.0f;
                [unroll] for (int ringIndex = 0; ringIndex < VRCLV_CONTRAST_RING_COUNT; ringIndex++) {
                    depthDifference += DepthDifferenceRing(uv, centerDepth, sampleScale, ringIndex);
                }
                return depthDifference * VRCLV_CONTRAST_INV_SAMPLE_COUNT;
            }

            float AverageDepthDifferenceDirect(float2 uv, float centerDepth, float2 sampleScale) {
                float depthDifference = 0.0f;
                [unroll] for (int ringIndex = 0; ringIndex < VRCLV_CONTRAST_RING_COUNT; ringIndex++) {
                    depthDifference += DepthDifferenceRingDirect(uv, centerDepth, sampleScale, ringIndex);
                }
                return depthDifference * VRCLV_CONTRAST_INV_SAMPLE_COUNT;
            }
        #endif

        float2 RuntimeBlurStep(float2 uv) {
            float radius = max(_BlurRadius, 0.0f);
            #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
                float centerDepth = DecodeDepth01(SampleDepthDirect(uv));
                float2 contrastSampleScale = _InvResolution * max(radius, 0.0001f) * 2.0f;
                float2 contrastExtent = contrastSampleScale * VRCLV_CONTRAST_MAX_RING_RADIUS;
                float depthDifference;
                [branch] if (KernelFitsFace(uv, contrastExtent)) depthDifference = AverageDepthDifferenceDirect(uv, centerDepth, contrastSampleScale);
                else depthDifference = AverageDepthDifference(uv, centerDepth, contrastSampleScale);
                radius *= saturate(depthDifference * rcp(_BlurDepth));
            #endif
            return _BlurDirection * (_InvResolution * radius * (2.0f * VRCLV_BLUR_INV_SAMPLE_RADIUS));
        }

        float4 BlurArrayDirect(float2 uv, float2 sampleStep) {
            float4 color = 0.0f;
            float weightSum = 0.0f;
            [unroll] for (int sampleIndex = -VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex <= VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex++) {
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
            [unroll] for (int sampleIndex = -VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex <= VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex++) {
                float sampleDistance = sampleIndex * VRCLV_BLUR_INV_SAMPLE_RADIUS;
                float weight = GaussianWeight(sampleDistance);
                color += SampleSource(uv + sampleStep * sampleIndex) * weight;
                weightSum += weight;
            }
            return color * rcp(weightSum);
        }

        float4 BlurArray(float2 uv) {
            float2 sampleStep = RuntimeBlurStep(uv);
            float2 blurExtent = abs(sampleStep) * VRCLV_BLUR_SAMPLE_RADIUS;
            [branch] if (KernelFitsFace(uv, blurExtent)) return BlurArrayDirect(uv, sampleStep);
            return BlurArraySeamAware(uv, sampleStep);
        }

        float4 fragArray(v2f i) : SV_Target {
            return BlurArray(i.uv);
        }
        ENDCG

        Pass {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment fragArray
            #pragma multi_compile_local_fragment VRCLV_RUNTIME_SHADOW_QUALITY_LOW VRCLV_RUNTIME_SHADOW_QUALITY_MEDIUM VRCLV_RUNTIME_SHADOW_QUALITY_HIGH
            #pragma multi_compile_local_fragment __ VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM
            #pragma shader_feature_local_fragment __ VRCLV_EDITOR_SHADOW_BLUR_QUALITY
            ENDCG
        }
    }
}
