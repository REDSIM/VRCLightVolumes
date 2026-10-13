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
        _SourceResolution("Source Resolution", Vector) = (128,128,0.0078125,0.0078125)
        _ShadowTanHalfFov("Shadow Tan Half FOV", Float) = 1
    }

    SubShader {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        // Implicit gradients are required for Unity's forced anisotropic filtering, including
        // single-mip arrays. Explicit LOD 0 sampling and zero-radius early-outs alter
        // the filtering footprint at adaptive blur boundaries.
        UNITY_DECLARE_TEX2DARRAY(_SourceArrayTex);
        #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            UNITY_DECLARE_TEX2DARRAY(_DepthArrayTex);
        #endif
        int _FaceIndex;
        float _SourceBaseSlice;
        float2 _BlurDirection;
        float _BlurRadius;
        float _InvResolution;
        float4 _SourceResolution;
        float _ShadowTanHalfFov;

        #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            float _DepthBaseSlice;
            float _BlurDepth;
            #define VRCLV_EVSM_NEGATIVE_EXPONENT 5.0f

            // Quadratic log2 mantissa approximation; the clamp guarantees a normal float.
            float VRCLV_FastLogPositive(float x) {
                uint bits = asuint(max(x, 0.000001f));
                int exponent = (int)((bits >> 23u) & 255u) - 127;
                float y = asfloat((bits & 0x007fffffu) | 0x3f800000u) - 1.0f;
                return (exponent + y * (1.3465554f - 0.3465554f * y)) * 0.69314718056f;
            }
        #endif

        #if defined(VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL)
            #define VRCLV_SHADOW_BLUR_SPHERICAL
        #endif

        // Rolled loops limit shader size and first-use driver compilation cost.
        #define VRCLV_BLUR_LOOP [loop]

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

        #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
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
        #endif

        #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
            #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                #define VRCLV_SPHERICAL_BLUR_RADIUS_SCALE 1.0547f
                #define VRCLV_SPHERICAL_BLUR_SAMPLE_COUNT 2048
                #define VRCLV_SPHERICAL_BLUR_INV_SAMPLE_COUNT 0.00048828125f
            #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_HIGH)
                #define VRCLV_SPHERICAL_BLUR_RADIUS_SCALE 1.0475f
                #define VRCLV_SPHERICAL_BLUR_SAMPLE_COUNT 128
            #elif defined(VRCLV_RUNTIME_SHADOW_QUALITY_LOW)
                #define VRCLV_SPHERICAL_BLUR_RADIUS_SCALE 1.0000f
                #define VRCLV_SPHERICAL_BLUR_SAMPLE_COUNT 32
            #else
                #define VRCLV_SPHERICAL_BLUR_RADIUS_SCALE 1.0313f
                #define VRCLV_SPHERICAL_BLUR_SAMPLE_COUNT 64
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

        #include "PointLightShadowBlurKernels.cginc"

        static const float3 faceDirs[6][3] = {
            { float3( 1,  0,  0), float3( 0,  0, -1), float3(0, -1, 0) },
            { float3(-1,  0,  0), float3( 0,  0,  1), float3(0, -1, 0) },
            { float3( 0,  1,  0), float3( 1,  0,  0), float3(0,  0, 1) },
            { float3( 0, -1,  0), float3( 1,  0,  0), float3(0,  0, -1) },
            { float3( 0,  0,  1), float3( 1,  0,  0), float3(0, -1, 0) },
            { float3( 0,  0, -1), float3(-1,  0,  0), float3(0, -1, 0) }
        };

        #if !defined(VRCLV_SHADOW_BLUR_SPHERICAL) && !defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
            bool KernelFitsFace(float2 uv, float2 absExtent) {
                float2 edgeDistance = min(uv, 1.0f - uv);
                return edgeDistance.x >= absExtent.x && edgeDistance.y >= absExtent.y;
            }
        #endif

        #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            float DecodeDepth01(float4 moments) {
                float negativeMagnitude = max(-moments.y, 0.000001f);
                float depth = -VRCLV_FastLogPositive(negativeMagnitude) * rcp(VRCLV_EVSM_NEGATIVE_EXPONENT);
                return saturate(depth * 0.5f + 0.5f);
            }
        #endif

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

                float3 SpotUvToSphericalDirection(float3 centerDir, float2 offsetDir, float offsetLength) {
                    // Rescale the precomputed unit direction to match offset / max(length, 1e-6)
                    // when the scaled offset length is below 1e-6.
                    offsetDir *= saturate(offsetLength * 1000000.0f);
                    float3 planeAxis = float3(offsetDir.x, offsetDir.y, 0.0f);
                    float3 tangentDir = planeAxis - centerDir * dot(planeAxis, centerDir);
                    tangentDir *= rsqrt(max(dot(tangentDir, tangentDir), 0.000001f));
                    return normalize(centerDir + tangentDir * offsetLength);
                }

                float2 SphericalSpotUv(float3 centerDir, float2 offsetDir, float offsetLength) {
                    return DirectionToSpotUv(SpotUvToSphericalDirection(centerDir, offsetDir, offsetLength));
                }
            #else
                float3 FaceUvToSphericalDirection(float3 centerDir, float2 offsetDir, float offsetLength) {
                    // Rescale the precomputed unit direction to match offset / max(length, 1e-6)
                    // when the scaled offset length is below 1e-6.
                    offsetDir *= saturate(offsetLength * 1000000.0f);
                    float3 faceAxis = float3(offsetDir, 0.0f);
                    float3 tangentDir = faceAxis - centerDir * dot(faceAxis, centerDir);
                    tangentDir *= rsqrt(max(dot(tangentDir, tangentDir), 0.000001f));
                    return centerDir + tangentDir * offsetLength;
                }
            #endif
        #endif

        #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
            float3 SphericalCenterDirection(float2 uv) {
                #if defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                    return SpotUvToDirection(uv);
                #else
                    return normalize(float3(uv * 2.0f - 1.0f, 1.0f));
                #endif
            }
        #endif

        float3 DirectionToArrayUv(float3 dir) {
            float2 uv;
            float face;
            float3 absDir = abs(dir);
            if (absDir.x >= absDir.y && absDir.x >= absDir.z) {
                face = dir.x > 0 ? 0.0f : 1.0f;
                uv = float2(dir.x > 0 ? -dir.z : dir.z, -dir.y);
            } else if (absDir.y >= absDir.z) {
                face = dir.y > 0 ? 2.0f : 3.0f;
                uv = float2(dir.x, dir.y > 0 ? dir.z : -dir.z);
            } else {
                face = dir.z > 0 ? 4.0f : 5.0f;
                uv = float2(dir.z > 0 ? dir.x : -dir.x, -dir.y);
            }
            return float3(uv * (0.5f * rcp(max(absDir.x, max(absDir.y, absDir.z)))) + 0.5f, face);
        }

        float3 ArrayAddress(float2 uv) {
            float2 faceUv = uv * 2.0f - 1.0f;
            return DirectionToArrayUv(faceDirs[_FaceIndex][0] + faceUv.x * faceDirs[_FaceIndex][1] + faceUv.y * faceDirs[_FaceIndex][2]);
        }

        float4 SampleSource(float2 uv) {
            float3 address = ArrayAddress(uv);
            address.z += _SourceBaseSlice;
            return UNITY_SAMPLE_TEX2DARRAY(_SourceArrayTex, address);
        }

        // Texture2DArray bilinear filtering clamps inside one slice. During a size conversion,
        // preserve the source-resolution footprint and reproject taps that cross a cubemap face.
        // Half a 32px source texel spans eight destination pixels after a 32 -> 512 upscale.
        float4 SampleSourceCubemapBilinear(float2 uv) {
            float2 sourceResolution = max(_SourceResolution.xy, 1.0f);
            float2 sourceInvResolution = _SourceResolution.zw;
            float2 sourceHalfTexel = sourceInvResolution * 0.5f;
            float2 edgeDistance = min(uv, 1.0f - uv);
            float4 color;
            [branch] if (all(edgeDistance >= sourceHalfTexel)) {
                color = UNITY_SAMPLE_TEX2DARRAY(_SourceArrayTex, float3(uv, _SourceBaseSlice + _FaceIndex));
            } else {

                float2 texelPosition = uv * sourceResolution - 0.5f;
                float2 texelBase = floor(texelPosition);
                float2 texelBlend = frac(texelPosition);
                float2 tap00 = (texelBase + float2(0.5f, 0.5f)) * sourceInvResolution;
                float2 tap10 = (texelBase + float2(1.5f, 0.5f)) * sourceInvResolution;
                float2 tap01 = (texelBase + float2(0.5f, 1.5f)) * sourceInvResolution;
                float2 tap11 = (texelBase + float2(1.5f, 1.5f)) * sourceInvResolution;
                float4 row0 = lerp(SampleSource(tap00), SampleSource(tap10), texelBlend.x);
                float4 row1 = lerp(SampleSource(tap01), SampleSource(tap11), texelBlend.x);
                color = lerp(row0, row1, texelBlend.y);
            }
            return color;
        }

        #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
            #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                float3 SphericalArrayAddress(float3 centerDir, float2 offsetDir, float offsetLength) {
                    float3 dir = FaceUvToSphericalDirection(centerDir, offsetDir, offsetLength);
                    // Most taps stay on this face; only edge taps need cube remapping.
                    float3 address;
                    [branch] if (dir.z >= max(abs(dir.x), abs(dir.y))) {
                        address = float3(dir.xy * (0.5f * rcp(dir.z)) + 0.5f, _FaceIndex);
                    } else {
                        address = DirectionToArrayUv(faceDirs[_FaceIndex][0] * dir.z + faceDirs[_FaceIndex][1] * dir.x + faceDirs[_FaceIndex][2] * dir.y);
                    }
                    return address;
                }
            #endif

            float4 SampleSourceSpherical(float3 centerDir, float3 diskSample, float sampleScale) {
                #if defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                    return UNITY_SAMPLE_TEX2DARRAY(_SourceArrayTex, float3(SphericalSpotUv(centerDir, diskSample.xy, diskSample.z * sampleScale), _SourceBaseSlice + _FaceIndex));
                #else
                    float3 address = SphericalArrayAddress(centerDir, diskSample.xy, diskSample.z * sampleScale);
                    address.z += _SourceBaseSlice;
                    return UNITY_SAMPLE_TEX2DARRAY(_SourceArrayTex, address);
                #endif
            }
        #endif

        float4 SampleSourceDirect(float2 uv) {
            return UNITY_SAMPLE_TEX2DARRAY(_SourceArrayTex, float3(uv, _SourceBaseSlice + _FaceIndex));
        }

        #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
            #if !defined(VRCLV_SHADOW_BLUR_SPHERICAL) && !defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                float4 SampleDepth(float2 uv) {
                    float3 address = ArrayAddress(uv);
                    address.z += _DepthBaseSlice;
                    return UNITY_SAMPLE_TEX2DARRAY(_DepthArrayTex, address);
                }
            #endif

            float4 SampleDepthDirect(float2 uv) {
                return UNITY_SAMPLE_TEX2DARRAY(_DepthArrayTex, float3(uv, _DepthBaseSlice + _FaceIndex));
            }

            #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
                float4 SampleDepthSpherical(float3 centerDir, float3 diskSample, float sampleScale) {
                    #if defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                        return UNITY_SAMPLE_TEX2DARRAY(_DepthArrayTex, float3(SphericalSpotUv(centerDir, diskSample.xy, diskSample.z * sampleScale), _DepthBaseSlice + _FaceIndex));
                    #else
                        float3 address = SphericalArrayAddress(centerDir, diskSample.xy, diskSample.z * sampleScale);
                        address.z += _DepthBaseSlice;
                        return UNITY_SAMPLE_TEX2DARRAY(_DepthArrayTex, address);
                    #endif
                }
            #endif

            #if !defined(VRCLV_SHADOW_BLUR_SPHERICAL)
                #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                    float AverageDepthDifference(float2 uv, float centerDepth, float2 sampleScale) {
                        float depthDifference = 0.0f;
                        VRCLV_BLUR_LOOP for (int sampleIndex = 0; sampleIndex < VRCLV_CONTRAST_SAMPLE_COUNT; sampleIndex++) {
                            float2 diskOffset;
                            #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                                diskOffset = DiskKernelSampleOffset(sampleIndex, VRCLV_CONTRAST_INV_SAMPLE_COUNT);
                            #else
                                diskOffset = contrastKernel[sampleIndex];
                            #endif
                            depthDifference += abs(DecodeDepth01(SampleDepth(uv + diskOffset * sampleScale)) - centerDepth);
                        }
                        return depthDifference * VRCLV_CONTRAST_INV_SAMPLE_COUNT;
                    }
                #endif
            #endif

            #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
                float AverageDepthDifferenceSpherical(float3 centerDir, float centerDepth, float2 sampleScale) {
                    float depthDifference = 0.0f;
                    VRCLV_BLUR_LOOP for (int sampleIndex = 0; sampleIndex < VRCLV_CONTRAST_SAMPLE_COUNT; sampleIndex++) {
                        #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                            float3 diskSample = SphericalDiskSample(sampleIndex, VRCLV_CONTRAST_INV_SAMPLE_COUNT);
                        #else
                            float3 diskSample = contrastKernel[sampleIndex];
                        #endif
                        depthDifference += abs(DecodeDepth01(SampleDepthSpherical(centerDir, diskSample, sampleScale.x * 2.0f)) - centerDepth);
                    }
                    return depthDifference * VRCLV_CONTRAST_INV_SAMPLE_COUNT;
                }
            #endif

            #if !defined(VRCLV_SHADOW_BLUR_SPHERICAL)
                float AverageDepthDifferenceDirect(float2 uv, float centerDepth, float2 sampleScale) {
                    float depthDifference = 0.0f;
                    VRCLV_BLUR_LOOP for (int sampleIndex = 0; sampleIndex < VRCLV_CONTRAST_SAMPLE_COUNT; sampleIndex++) {
                        float2 diskOffset;
                        #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                            diskOffset = DiskKernelSampleOffset(sampleIndex, VRCLV_CONTRAST_INV_SAMPLE_COUNT);
                        #else
                            diskOffset = contrastKernel[sampleIndex];
                        #endif
                        depthDifference += abs(DecodeDepth01(SampleDepthDirect(uv + diskOffset * sampleScale)) - centerDepth);
                    }
                    return depthDifference * VRCLV_CONTRAST_INV_SAMPLE_COUNT;
                }
            #endif
        #endif

        float RuntimeBlurRadius(float2 uv, float spotScale) {
            float radius = max(_BlurRadius, 0.0f);
            #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM)
                float centerDepth = DecodeDepth01(SampleDepthDirect(uv));
                float2 contrastSampleScale = _InvResolution * max(radius, 0.0001f) * 2.0f;
                float depthDifference;
                #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
                    depthDifference = AverageDepthDifferenceSpherical(SphericalCenterDirection(uv), centerDepth, contrastSampleScale * VRCLV_SPHERICAL_BLUR_RADIUS_SCALE);
                #elif defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                    depthDifference = AverageDepthDifferenceDirect(uv, centerDepth, contrastSampleScale * spotScale);
                #else
                    // Every contrast sample lies inside the unit disk.
                    [branch] if (KernelFitsFace(uv, contrastSampleScale)) depthDifference = AverageDepthDifferenceDirect(uv, centerDepth, contrastSampleScale);
                    else depthDifference = AverageDepthDifference(uv, centerDepth, contrastSampleScale);
                #endif
                radius *= saturate(depthDifference * rcp(_BlurDepth));
            #endif
            return radius;
        }

        #if !defined(VRCLV_SHADOW_BLUR_SPHERICAL)
            float2 RuntimeBlurStep(float2 uv) {
                float spotScale = 1.0f;
                #if defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                    spotScale = rcp(max(_ShadowTanHalfFov, 0.000001f));
                #endif
                float radius = RuntimeBlurRadius(uv, spotScale);
                return _BlurDirection * (_InvResolution * radius * (2.0f * VRCLV_BLUR_INV_SAMPLE_RADIUS) * spotScale);
            }

            float4 BlurArrayDirect(float2 uv, float2 sampleStep) {
                float4 color = 0.0f;
                VRCLV_BLUR_LOOP for (int sampleIndex = -VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex <= VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex++) {
                    color += SampleSourceDirect(uv + sampleStep * sampleIndex) * linearWeights[abs(sampleIndex)];
                }
                return color * VRCLV_LINEAR_INV_WEIGHT_SUM;
            }

            #if !defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
                float4 BlurArraySeamAware(float2 uv, float2 sampleStep) {
                    float4 color = 0.0f;
                    #if !defined(VRCLV_RUNTIME_SHADOW_QUALITY_HIGH) || defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                    VRCLV_BLUR_LOOP for (int sampleIndex = -VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex <= VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex++) {
                        float weight = linearWeights[abs(sampleIndex)];
                        color += SampleSource(uv + sampleStep * sampleIndex) * weight;
                    }
                    #else
                    // High quality processes four consecutive taps per iteration to share loop overhead.
                    // Other qualities and editor sampling use a scalar loop to limit shader size.
                    VRCLV_BLUR_LOOP for (int block = -VRCLV_BLUR_SAMPLE_RADIUS; block <= VRCLV_BLUR_SAMPLE_RADIUS - 4; block += 4) {
                        [unroll] for (int lane = 0; lane < 4; lane++) {
                            int sampleIndex = block + lane;
                            color += SampleSource(uv + sampleStep * sampleIndex) * linearWeights[abs(sampleIndex)];
                        }
                    }
                    [unroll] for (int sampleIndex = VRCLV_BLUR_SAMPLE_RADIUS - 2; sampleIndex <= VRCLV_BLUR_SAMPLE_RADIUS; sampleIndex++) {
                        color += SampleSource(uv + sampleStep * sampleIndex) * linearWeights[abs(sampleIndex)];
                    }
                    #endif
                    return color * VRCLV_LINEAR_INV_WEIGHT_SUM;
                }

                float4 BlurArray(float2 uv) {
                    float2 sampleStep = RuntimeBlurStep(uv);
                    float2 blurExtent = abs(sampleStep) * VRCLV_BLUR_SAMPLE_RADIUS;
                    [branch] if (KernelFitsFace(uv, blurExtent)) {
                        return BlurArrayDirect(uv, sampleStep);
                    } else {
                        return BlurArraySeamAware(uv, sampleStep);
                    }
                }
            #endif
        #endif

        #if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
            float4 BlurArraySpherical(float2 uv) {
                float3 centerDir = SphericalCenterDirection(uv);
                // A zero spherical offset is exactly the center sample.
                float4 color = SampleSourceDirect(uv);
                #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                    float weightSum = 1.0f;
                #endif
                float blurRadius = _InvResolution * RuntimeBlurRadius(uv, 1.0f) * (4.0f * VRCLV_SPHERICAL_BLUR_RADIUS_SCALE);
                VRCLV_BLUR_LOOP for (int sampleIndex = 0; sampleIndex < VRCLV_SPHERICAL_BLUR_SAMPLE_COUNT; sampleIndex++) {
                    float3 diskSample;
                    float weight;
                    #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                        diskSample = SphericalDiskSample(sampleIndex, VRCLV_SPHERICAL_BLUR_INV_SAMPLE_COUNT);
                        float radiusSq = (sampleIndex + 0.5f) * VRCLV_SPHERICAL_BLUR_INV_SAMPLE_COUNT;
                        weight = VRCLV_Exp(-2.0f * radiusSq);
                    #else
                        diskSample = sphericalKernel[sampleIndex].xyz;
                        weight = sphericalKernel[sampleIndex].w;
                    #endif
                    color += SampleSourceSpherical(centerDir, diskSample, blurRadius) * weight;
                    #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                        weightSum += weight;
                    #endif
                }
                #if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY)
                    color *= rcp(weightSum);
                #else
                    color *= VRCLV_SPHERICAL_INV_WEIGHT_SUM;
                #endif
                return color;
            }
        #endif

        float4 fragArray(v2f i) : SV_Target {
#if defined(VRCLV_SHADOW_BLUR_SPHERICAL)
            return BlurArraySpherical(i.uv);
#elif defined(VRCLV_RUNTIME_SHADOW_BLUR_DIRECT)
            return BlurArrayDirect(i.uv, RuntimeBlurStep(i.uv));
#else
            return BlurArray(i.uv);
#endif
        }

        float4 fragCubemapResample(v2f i) : SV_Target {
            return SampleSourceCubemapBilinear(i.uv);
        }

        ENDCG

        Pass {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment fragArray
            #pragma multi_compile_local_fragment VRCLV_RUNTIME_SHADOW_QUALITY_LOW VRCLV_RUNTIME_SHADOW_QUALITY_MEDIUM VRCLV_RUNTIME_SHADOW_QUALITY_HIGH
            #pragma multi_compile_local_fragment __ VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM
            #pragma multi_compile_local_fragment __ VRCLV_RUNTIME_SHADOW_BLUR_DIRECT
            #pragma multi_compile_local_fragment __ VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL
            #pragma shader_feature_local_fragment __ VRCLV_EDITOR_SHADOW_BLUR_QUALITY
            ENDCG
        }

        Pass {
            Name "CubemapResample"
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment fragCubemapResample
            ENDCG
        }

    }
}
