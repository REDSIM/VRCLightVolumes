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
        #endif

        // Approximate exp for blur weights.
        float VRCLV_FastExp(float x) {
            x *= 0.25f;
            float y = 1.0f + x * (1.0f + x * (0.5f + x * (0.16666667f + x * (0.04166667f + x * (0.00833333f + x * 0.00138889f)))));
            y *= y;
            return y * y;
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
                #define VRCLV_CONTRAST_SAMPLE_COUNT 512
                #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.001953125f
            #elif defined(VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL)
                #if defined(VRCLV_RUNTIME_SHADOW_QUALITY_HIGH)
                    #define VRCLV_CONTRAST_SAMPLE_COUNT 48
                    #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.0208333333f
                #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_LOW)
                    #define VRCLV_CONTRAST_SAMPLE_COUNT 16
                    #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.0625f
                #else
                    #define VRCLV_CONTRAST_SAMPLE_COUNT 32
                    #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.03125f
                #endif
            #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_HIGH)
                #define VRCLV_CONTRAST_SAMPLE_COUNT 16
                #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.0625f
            #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_LOW)
                #define VRCLV_CONTRAST_SAMPLE_COUNT 4
                #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.25f
            #else
                #define VRCLV_CONTRAST_SAMPLE_COUNT 8
                #define VRCLV_CONTRAST_INV_SAMPLE_COUNT 0.125f
            #endif
            #define VRCLV_CONTRAST_MAX_RADIUS 1.0f
        #endif

        #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
            #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                #define VRCLV_SPHERICAL_BLUR_RADIUS_SCALE 1.0547f
                #define VRCLV_SPHERICAL_BLUR_SAMPLE_COUNT 2048
                #define VRCLV_SPHERICAL_BLUR_INV_SAMPLE_COUNT 0.00048828125f
            #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_HIGH)
                #define VRCLV_SPHERICAL_BLUR_RADIUS_SCALE 1.0475f
                #define VRCLV_SPHERICAL_BLUR_SAMPLE_COUNT 128
                #define VRCLV_SPHERICAL_BLUR_INV_SAMPLE_COUNT 0.0078125f
            #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_LOW)
                #define VRCLV_SPHERICAL_BLUR_RADIUS_SCALE 1.0000f
                #define VRCLV_SPHERICAL_BLUR_SAMPLE_COUNT 32
                #define VRCLV_SPHERICAL_BLUR_INV_SAMPLE_COUNT 0.03125f
            #else
                #define VRCLV_SPHERICAL_BLUR_RADIUS_SCALE 1.0313f
                #define VRCLV_SPHERICAL_BLUR_SAMPLE_COUNT 64
                #define VRCLV_SPHERICAL_BLUR_INV_SAMPLE_COUNT 0.015625f
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
            #define VRCLV_DISK_KERNEL_DIRECTION_COUNT 128
            // Direction LUT is sampled with a near-golden-angle stride; radius is derived from the active sample count.
            static const float2 diskKernelDirections[128] = {
                float2( 1.0000f,  0.0000f), float2( 0.9988f,  0.0491f), float2( 0.9952f,  0.0980f), float2( 0.9892f,  0.1467f), float2( 0.9808f,  0.1951f), float2( 0.9700f,  0.2430f), float2( 0.9569f,  0.2903f), float2( 0.9415f,  0.3369f), float2( 0.9239f,  0.3827f), float2( 0.9040f,  0.4276f), float2( 0.8819f,  0.4714f), float2( 0.8577f,  0.5141f), float2( 0.8315f,  0.5556f), float2( 0.8032f,  0.5957f), float2( 0.7730f,  0.6344f), float2( 0.7410f,  0.6716f),
                float2( 0.7071f,  0.7071f), float2( 0.6716f,  0.7410f), float2( 0.6344f,  0.7730f), float2( 0.5957f,  0.8032f), float2( 0.5556f,  0.8315f), float2( 0.5141f,  0.8577f), float2( 0.4714f,  0.8819f), float2( 0.4276f,  0.9040f), float2( 0.3827f,  0.9239f), float2( 0.3369f,  0.9415f), float2( 0.2903f,  0.9569f), float2( 0.2430f,  0.9700f), float2( 0.1951f,  0.9808f), float2( 0.1467f,  0.9892f), float2( 0.0980f,  0.9952f), float2( 0.0491f,  0.9988f),
                float2( 0.0000f,  1.0000f), float2(-0.0491f,  0.9988f), float2(-0.0980f,  0.9952f), float2(-0.1467f,  0.9892f), float2(-0.1951f,  0.9808f), float2(-0.2430f,  0.9700f), float2(-0.2903f,  0.9569f), float2(-0.3369f,  0.9415f), float2(-0.3827f,  0.9239f), float2(-0.4276f,  0.9040f), float2(-0.4714f,  0.8819f), float2(-0.5141f,  0.8577f), float2(-0.5556f,  0.8315f), float2(-0.5957f,  0.8032f), float2(-0.6344f,  0.7730f), float2(-0.6716f,  0.7410f),
                float2(-0.7071f,  0.7071f), float2(-0.7410f,  0.6716f), float2(-0.7730f,  0.6344f), float2(-0.8032f,  0.5957f), float2(-0.8315f,  0.5556f), float2(-0.8577f,  0.5141f), float2(-0.8819f,  0.4714f), float2(-0.9040f,  0.4276f), float2(-0.9239f,  0.3827f), float2(-0.9415f,  0.3369f), float2(-0.9569f,  0.2903f), float2(-0.9700f,  0.2430f), float2(-0.9808f,  0.1951f), float2(-0.9892f,  0.1467f), float2(-0.9952f,  0.0980f), float2(-0.9988f,  0.0491f),
                float2(-1.0000f,  0.0000f), float2(-0.9988f, -0.0491f), float2(-0.9952f, -0.0980f), float2(-0.9892f, -0.1467f), float2(-0.9808f, -0.1951f), float2(-0.9700f, -0.2430f), float2(-0.9569f, -0.2903f), float2(-0.9415f, -0.3369f), float2(-0.9239f, -0.3827f), float2(-0.9040f, -0.4276f), float2(-0.8819f, -0.4714f), float2(-0.8577f, -0.5141f), float2(-0.8315f, -0.5556f), float2(-0.8032f, -0.5957f), float2(-0.7730f, -0.6344f), float2(-0.7410f, -0.6716f),
                float2(-0.7071f, -0.7071f), float2(-0.6716f, -0.7410f), float2(-0.6344f, -0.7730f), float2(-0.5957f, -0.8032f), float2(-0.5556f, -0.8315f), float2(-0.5141f, -0.8577f), float2(-0.4714f, -0.8819f), float2(-0.4276f, -0.9040f), float2(-0.3827f, -0.9239f), float2(-0.3369f, -0.9415f), float2(-0.2903f, -0.9569f), float2(-0.2430f, -0.9700f), float2(-0.1951f, -0.9808f), float2(-0.1467f, -0.9892f), float2(-0.0980f, -0.9952f), float2(-0.0491f, -0.9988f),
                float2( 0.0000f, -1.0000f), float2( 0.0491f, -0.9988f), float2( 0.0980f, -0.9952f), float2( 0.1467f, -0.9892f), float2( 0.1951f, -0.9808f), float2( 0.2430f, -0.9700f), float2( 0.2903f, -0.9569f), float2( 0.3369f, -0.9415f), float2( 0.3827f, -0.9239f), float2( 0.4276f, -0.9040f), float2( 0.4714f, -0.8819f), float2( 0.5141f, -0.8577f), float2( 0.5556f, -0.8315f), float2( 0.5957f, -0.8032f), float2( 0.6344f, -0.7730f), float2( 0.6716f, -0.7410f),
                float2( 0.7071f, -0.7071f), float2( 0.7410f, -0.6716f), float2( 0.7730f, -0.6344f), float2( 0.8032f, -0.5957f), float2( 0.8315f, -0.5556f), float2( 0.8577f, -0.5141f), float2( 0.8819f, -0.4714f), float2( 0.9040f, -0.4276f), float2( 0.9239f, -0.3827f), float2( 0.9415f, -0.3369f), float2( 0.9569f, -0.2903f), float2( 0.9700f, -0.2430f), float2( 0.9808f, -0.1951f), float2( 0.9892f, -0.1467f), float2( 0.9952f, -0.0980f), float2( 0.9988f, -0.0491f)
            };

            float2 DiskKernelSampleOffset(int sampleIndex, float invSampleCount, out float radiusSq) {
                radiusSq = (sampleIndex + 0.5f) * invSampleCount;
                int directionIndex = (sampleIndex * 49) % VRCLV_DISK_KERNEL_DIRECTION_COUNT;
                return diskKernelDirections[directionIndex] * sqrt(radiusSq);
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
            float DecodeDepth01(float depth) {
                return saturate(depth);
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

        float2 SampleSource(float2 uv) {
            float3 address = ArrayAddress(uv);
            address.z += _SourceBaseSlice;
            return UNITY_SAMPLE_TEX2DARRAY(_SourceArrayTex, address).xy;
        }

        #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
            #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                float3 SphericalArrayAddress(float2 uv, float2 faceUvOffset) {
                    return DirectionToArrayUv(FaceUvToSphericalDirection(uv, faceUvOffset));
                }
            #endif

            float2 SampleSourceSpherical(float2 uv, float2 faceUvOffset) {
                #if defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                    return UNITY_SAMPLE_TEX2DARRAY(_SourceArrayTex, float3(SphericalSpotUv(uv, faceUvOffset), _SourceBaseSlice + _FaceIndex)).xy;
                #else
                    float3 address = SphericalArrayAddress(uv, faceUvOffset);
                    address.z += _SourceBaseSlice;
                    return UNITY_SAMPLE_TEX2DARRAY(_SourceArrayTex, address).xy;
                #endif
            }
        #endif

        float2 SampleSourceDirect(float2 uv) {
            return UNITY_SAMPLE_TEX2DARRAY(_SourceArrayTex, float3(uv, _SourceBaseSlice + _FaceIndex)).xy;
        }

        #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            float SampleDepth(float2 uv) {
                float3 address = ArrayAddress(uv);
                address.z += _DepthBaseSlice;
                return UNITY_SAMPLE_TEX2DARRAY(_DepthArrayTex, address).x;
            }

            float SampleDepthDirect(float2 uv) {
                return UNITY_SAMPLE_TEX2DARRAY(_DepthArrayTex, float3(uv, _DepthBaseSlice + _FaceIndex)).x;
            }

            #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
                float SampleDepthSpherical(float2 uv, float2 faceUvOffset) {
                    #if defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                        return UNITY_SAMPLE_TEX2DARRAY(_DepthArrayTex, float3(SphericalSpotUv(uv, faceUvOffset), _DepthBaseSlice + _FaceIndex)).x;
                    #else
                        float3 address = SphericalArrayAddress(uv, faceUvOffset);
                        address.z += _DepthBaseSlice;
                        return UNITY_SAMPLE_TEX2DARRAY(_DepthArrayTex, address).x;
                    #endif
                }
            #endif

            float AverageDepthDifference(float2 uv, float centerDepth, float2 sampleScale) {
                float depthDifference = 0.0f;
                VRCLV_BLUR_LOOP for (int sampleIndex = 0; sampleIndex < VRCLV_CONTRAST_SAMPLE_COUNT; sampleIndex++) {
                    float radiusSq;
                    float2 diskOffset = DiskKernelSampleOffset(sampleIndex, VRCLV_CONTRAST_INV_SAMPLE_COUNT, radiusSq);
                    depthDifference += abs(DecodeDepth01(SampleDepth(uv + diskOffset * sampleScale)) - centerDepth);
                }
                return depthDifference * VRCLV_CONTRAST_INV_SAMPLE_COUNT;
            }

            #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
                float AverageDepthDifferenceSpherical(float2 uv, float centerDepth, float2 sampleScale) {
                    float depthDifference = 0.0f;
                    VRCLV_BLUR_LOOP for (int sampleIndex = 0; sampleIndex < VRCLV_CONTRAST_SAMPLE_COUNT; sampleIndex++) {
                        float radiusSq;
                        float2 diskOffset = DiskKernelSampleOffset(sampleIndex, VRCLV_CONTRAST_INV_SAMPLE_COUNT, radiusSq);
                        depthDifference += abs(DecodeDepth01(SampleDepthSpherical(uv, diskOffset * (sampleScale * 2.0f))) - centerDepth);
                    }
                    return depthDifference * VRCLV_CONTRAST_INV_SAMPLE_COUNT;
                }
            #endif

            float AverageDepthDifferenceDirect(float2 uv, float centerDepth, float2 sampleScale) {
                float depthDifference = 0.0f;
                VRCLV_BLUR_LOOP for (int sampleIndex = 0; sampleIndex < VRCLV_CONTRAST_SAMPLE_COUNT; sampleIndex++) {
                    float radiusSq;
                    float2 diskOffset = DiskKernelSampleOffset(sampleIndex, VRCLV_CONTRAST_INV_SAMPLE_COUNT, radiusSq);
                    depthDifference += abs(DecodeDepth01(SampleDepthDirect(uv + diskOffset * sampleScale)) - centerDepth);
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
                    float2 contrastExtent = contrastSampleScale * VRCLV_CONTRAST_MAX_RADIUS;
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

        float2 BlurArrayDirect(float2 uv, float2 sampleStep) {
            float2 color = 0.0f;
            float weightSum = 0.0f;
            VRCLV_BLUR_LOOP for (int sampleIndex = -VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex <= VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex++) {
                float sampleDistance = sampleIndex * VRCLV_BLUR_INV_SAMPLE_RADIUS;
                float weight = GaussianWeight(sampleDistance);
                color += SampleSourceDirect(uv + sampleStep * sampleIndex) * weight;
                weightSum += weight;
            }
            return color * rcp(weightSum);
        }

        float2 BlurArraySeamAware(float2 uv, float2 sampleStep) {
            float2 color = 0.0f;
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
            float2 BlurArraySpherical(float2 uv) {
                float2 color = SampleSourceSpherical(uv, float2(0.0f, 0.0f));
                float weightSum = 1.0f;
                float blurRadius = _InvResolution * RuntimeBlurRadius(uv) * (4.0f * VRCLV_SPHERICAL_BLUR_RADIUS_SCALE);
                VRCLV_BLUR_LOOP for (int sampleIndex = 0; sampleIndex < VRCLV_SPHERICAL_BLUR_SAMPLE_COUNT; sampleIndex++) {
                    float radiusSq;
                    float2 diskOffset = DiskKernelSampleOffset(sampleIndex, VRCLV_SPHERICAL_BLUR_INV_SAMPLE_COUNT, radiusSq);
                    float weight = VRCLV_FastExp(-2.0f * radiusSq);
                    color += SampleSourceSpherical(uv, diskOffset * blurRadius) * weight;
                    weightSum += weight;
                }
                return color * rcp(weightSum);
            }
        #endif

        float2 BlurArray(float2 uv) {
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
            return float4(BlurArraySpherical(i.uv), 0.0f, 0.0f);
#elif defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
            return float4(BlurArrayDirect(i.uv, RuntimeBlurStep(i.uv)), 0.0f, 0.0f);
#else
            return float4(BlurArray(i.uv), 0.0f, 0.0f);
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
