Shader "Hidden/VRCLV/FroxelClusteringBuild" {
    Properties {
        [HideInInspector] _UdonCoarseClusterMask ("Coarse Cluster Mask", 2D) = "black" {}
        [HideInInspector] _UdonShadowCullHierarchy ("Shadow Cull Hierarchy", 2D) = "black" {}
    }

    SubShader {
        Tags { "RenderType" = "Opaque" }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        CGINCLUDE
            #include "UnityCG.cginc"

            #define VRCLV_MAX_POINT_LIGHTS 128
            #define VRCLV_FROXEL_AXIS_ERROR 0.02
            #define VRCLV_SHADOW_FACE_SQRT_TWO 1.4142135623730951
            #define VRCLV_SHADOW_REPROJECTION_GUARD 0.01
            #define VRCLV_SHADOW_UV_EPSILON 0.00001
            #define VRCLV_SHADOW_DEPTH_EPSILON 0.00001

            float _UdonLightVolumeVersion;
            float _UdonPointLightVolumeCount;
            float4 _UdonPointLightVolumePosition[VRCLV_MAX_POINT_LIGHTS];
            float4 _UdonPointLightVolumeExtraData[VRCLV_MAX_POINT_LIGHTS];
            float4 _UdonPointLightVolumeShadowReprojectionData[VRCLV_MAX_POINT_LIGHTS];
            float4 _UdonPointLightVolumeShadowRotationData[VRCLV_MAX_POINT_LIGHTS];
            float4 _UdonFroxelShadowMetadata[VRCLV_MAX_POINT_LIGHTS];
            float4 _UdonClusteringLights[VRCLV_MAX_POINT_LIGHTS / 2];
            Texture2D<int4> _UdonCoarseClusterMask;
            Texture2D<float> _UdonShadowCullHierarchy;
            float4 _UdonFroxelFineGrid;
            float4 _UdonFroxelCoarseGrid;
            float4 _UdonFroxelGridInverse; // xy: reciprocal Fine columns/depth, zw: reciprocal Coarse columns/depth
            float4 _UdonFroxelDepth;
            float4 _UdonFroxelDepthStep;
            float4 _UdonFroxelProjection;
            float4 _UdonFroxelRight;
            float4 _UdonFroxelUp;
            float4 _UdonFroxelForward;
            float4 _UdonFroxelCoarse; // xy: factor/log2(factor), zw: reciprocal Fine columns/rows
            float4 _UdonFroxelShadowCull; // xy: log2(shadow resolution)/first retained level, z: slice count, w: log2(atlas row pitch)

            struct MeshData {
                float4 vertex : POSITION;
            };

            struct Varyings {
                float4 position : SV_POSITION;
            };

            Varyings Vertex(MeshData input) {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                return output;
            }

            // Atlas layout keeps every logical X row contiguous while tiling Y rows across X/Y in powers of two.
            bool DecodeAtlasCell(float2 pixelPosition, float4 gridParams, float2 inverseGridDimensions, out uint3 cell) {
                uint2 pixel = (uint2)pixelPosition;
                uint columns = (uint)gridParams.x;
                uint depthSlices = (uint)gridParams.y;
                uint tileShift = (uint)gridParams.w;
                // Pixel centers stay half a texel away from tile boundaries, so reciprocal multiply is exact for the supported <= 4096 atlas and avoids integer divides.
                uint2 tile = (uint2)(pixelPosition * inverseGridDimensions);
                uint tileX = tile.x;
                uint tileY = tile.y;
                cell = uint3(pixel.x - tileX * columns, (tileY << tileShift) + tileX, pixel.y - tileY * depthSlices);
                return cell.y < (uint)gridParams.z;
            }

            int2 FroxelCellToAtlas(uint3 cell, float4 gridParams) {
                uint tileShift = (uint)gridParams.w;
                uint tileX = cell.y & ((1u << tileShift) - 1u);
                uint tileY = cell.y >> tileShift;
                return int2(tileX * (uint)gridParams.x + cell.x, tileY * (uint)gridParams.y + cell.z);
            }

            // Reconstructs the ordinary broad-phase sphere and, only while shadow culling is enabled, the two exact near/far end rectangles. Perspective froxel edges are linear between those planes, so their convex hull is the complete froxel. 
            // Keeping the rectangles avoids the artificial depth thickness and sqrt(2) transverse expansion introduced by circumscribed endpoint spheres. Fine boundaries keep a partial last Coarse cell nested.
            void BuildFroxelBounds(uint3 cell, uint childScale, bool buildShadowHull, out float3 center, out float radius, out float3 nearCenter, out float2 nearHalfSize, out float3 farCenter, out float2 farHalfSize) {
                uint3 fineCount = uint3((uint)_UdonFroxelFineGrid.x, (uint)_UdonFroxelFineGrid.z, (uint)_UdonFroxelFineGrid.y);
                uint3 firstFine = cell * childScale;
                uint3 endFine = min(firstFine + childScale, fineCount);

                float2 inverseFineXY = _UdonFroxelCoarse.zw;
                float2 normalizedMin = float2(firstFine.xy) * inverseFineXY * 2.0 - 1.0;
                float2 normalizedMax = float2(endFine.xy) * inverseFineXY * 2.0 - 1.0;
                float nearDepth = _UdonFroxelDepth.x * exp2(_UdonFroxelDepthStep.x * (float)firstFine.z);
                float farDepth;
                [branch] if (childScale == 1u) {
                    // Fine is the large pass: reuse the precomputed one-slice ratio and save one SFU per froxel.
                    farDepth = nearDepth * _UdonFroxelDepthStep.y;
                } else {
                    uint childDepthCount = endFine.z - firstFine.z;
                    [branch] if (childDepthCount == childScale) {
                        farDepth = nearDepth * _UdonFroxelDepthStep.z;
                    } else {
                        // Only the last non-divisible Coarse slice needs its own exact ratio.
                        farDepth = nearDepth * exp2(_UdonFroxelDepthStep.x * (float)childDepthCount);
                    }
                }
                float2 nearExtent = nearDepth * _UdonFroxelProjection.xy + _UdonFroxelProjection.zw;
                float2 farExtent = farDepth * _UdonFroxelProjection.xy + _UdonFroxelProjection.zw;
                float4 nearRect = float4(normalizedMin * nearExtent, normalizedMax * nearExtent);
                float4 farRect = float4(normalizedMin * farExtent, normalizedMax * farExtent);
                float2 boundsMin = min(nearRect.xy, farRect.xy);
                float2 boundsMax = max(nearRect.zw, farRect.zw);

                float3 localCenter = float3((boundsMin + boundsMax) * 0.5, (nearDepth + farDepth) * 0.5);
                float3 halfSize = float3((boundsMax - boundsMin) * 0.5, (farDepth - nearDepth) * 0.5);
                radius = length(halfSize) * 1.000001 + 0.001;

                float3 cameraPosition = float3(_UdonFroxelRight.w, _UdonFroxelUp.w, _UdonFroxelForward.w);
                center = cameraPosition + _UdonFroxelRight.xyz * localCenter.x + _UdonFroxelUp.xyz * localCenter.y + _UdonFroxelForward.xyz * localCenter.z;

                nearCenter = 0.0;
                farCenter = 0.0;
                nearHalfSize = 0.0;
                farHalfSize = 0.0;
                [branch] if (buildShadowHull) {
                    float2 nearCenterXY = (nearRect.xy + nearRect.zw) * 0.5;
                    float2 farCenterXY = (farRect.xy + farRect.zw) * 0.5;
                    nearHalfSize = (nearRect.zw - nearRect.xy) * 0.5;
                    farHalfSize = (farRect.zw - farRect.xy) * 0.5;
                    nearCenter = cameraPosition + _UdonFroxelRight.xyz * nearCenterXY.x + _UdonFroxelUp.xyz * nearCenterXY.y + _UdonFroxelForward.xyz * nearDepth;
                    farCenter = cameraPosition + _UdonFroxelRight.xyz * farCenterXY.x + _UdonFroxelUp.xyz * farCenterXY.y + _UdonFroxelForward.xyz * farDepth;
                }
            }

            float3 DecodeClusterShapeAxis(uint packedShape) {
                float2 oct = float2(packedShape & 255u, (packedShape >> 8u) & 255u) * (2.0 / 255.0) - 1.0;
                float3 axis = float3(oct, 1.0 - abs(oct.x) - abs(oct.y));
                [flatten] if (axis.z < 0.0) {
                    float unfoldedX = axis.x;
                    axis.x = (1.0 - abs(axis.y)) * (unfoldedX >= 0.0 ? 1.0 : -1.0);
                    axis.y = (1.0 - abs(unfoldedX)) * (axis.y >= 0.0 ? 1.0 : -1.0);
                }
                return axis * rsqrt(max(dot(axis, axis), 0.000001));
            }

            float3 RotateShadowVector(float3 value, float4 rotation) {
                float3 doubledCross = 2.0 * cross(rotation.xyz, value);
                return value + rotation.w * doubledCross + cross(rotation.xyz, doubledCross);
            }

            // Matches LV_CubemapUvFace and returns only the selected signed-permutation face.
            uint SelectShadowCubeFace(float3 direction) {
                float3 absoluteDirection = abs(direction);
                [flatten] if (absoluteDirection.x >= absoluteDirection.y && absoluteDirection.x >= absoluteDirection.z) {
                    return direction.x > 0.0 ? 0u : 1u;
                } else [flatten] if (absoluteDirection.y >= absoluteDirection.z) {
                    return direction.y > 0.0 ? 2u : 3u;
                }
                return direction.z > 0.0 ? 4u : 5u;
            }

            // Projects a direction through one already-selected cubemap face using the exact signed permutation from LV_CubemapUvFace.
            void ProjectShadowCubeFace(float3 direction, uint face, out float2 numerator, out float major) {
                [flatten] if (face == 0u) {
                    numerator = float2(-direction.z, -direction.y);
                    major = direction.x;
                } else [flatten] if (face == 1u) {
                    numerator = float2(direction.z, -direction.y);
                    major = -direction.x;
                } else [flatten] if (face == 2u) {
                    numerator = float2(direction.x, direction.z);
                    major = direction.y;
                } else [flatten] if (face == 3u) {
                    numerator = float2(direction.x, -direction.z);
                    major = -direction.y;
                } else [flatten] if (face == 4u) {
                    numerator = float2(direction.x, -direction.y);
                    major = direction.z;
                } else {
                    numerator = float2(-direction.x, -direction.y);
                    major = -direction.z;
                }
            }

            // Exact perspective bounds of one oriented endpoint rectangle. With positive major, a projected ratio over a convex polygon is a denominator-weighted combination of its vertex ratios, so its extrema occur at these four vertices. 
            // The projected unit axes are shared by both endpoints to avoid repeating face-selection branches.
            bool BuildShadowRectangleUvBounds(float3 shadowCenter, float2 rightNumeratorUnit, float rightMajorUnit, float2 upNumeratorUnit, float upMajorUnit, float2 halfSize, bool singleShadow, float projectionScale, float planeNormalLength, uint cubeFace, float worldEpsilon, out float2 uvMin, out float2 uvMax) {
                float2 centerNumerator;
                float centerMajor;
                [branch] if (singleShadow) {
                    centerNumerator = -shadowCenter.xy;
                    centerMajor = -shadowCenter.z;
                } else {
                    ProjectShadowCubeFace(shadowCenter, cubeFace, centerNumerator, centerMajor);
                }

                float2 rightNumerator = rightNumeratorUnit * halfSize.x;
                float rightMajor = rightMajorUnit * halfSize.x;
                float2 upNumerator = upNumeratorUnit * halfSize.y;
                float upMajor = upMajorUnit * halfSize.y;
                const float4 rightSigns = float4(-1.0, 1.0, -1.0, 1.0);
                const float4 upSigns = float4(-1.0, -1.0, 1.0, 1.0);
                float4 major = centerMajor + rightMajor * rightSigns + upMajor * upSigns;
                float4 numeratorX = centerNumerator.x + rightNumerator.x * rightSigns + upNumerator.x * upSigns;
                float4 numeratorY = centerNumerator.y + rightNumerator.y * rightSigns + upNumerator.y * upSigns;
                float4 faceMargin = major * projectionScale - max(abs(numeratorX), abs(numeratorY));
                float minimumMajor = min(min(major.x, major.y), min(major.z, major.w));
                float minimumFaceMargin = min(min(faceMargin.x, faceMargin.y), min(faceMargin.z, faceMargin.w));
                if (!(minimumMajor > worldEpsilon) || !(minimumFaceMargin > worldEpsilon * planeNormalLength)) return false;

                float4 inverseMajorScale = rcp(major * projectionScale);
                float4 projectedX = numeratorX * inverseMajorScale;
                float4 projectedY = numeratorY * inverseMajorScale;
                float2 projectedMin = float2(min(min(projectedX.x, projectedX.y), min(projectedX.z, projectedX.w)), min(min(projectedY.x, projectedY.y), min(projectedY.z, projectedY.w)));
                float2 projectedMax = float2(max(max(projectedX.x, projectedX.y), max(projectedX.z, projectedX.w)), max(max(projectedY.x, projectedY.y), max(projectedY.z, projectedY.w)));
                // For |delta numerator|, |delta major| <= epsilon this bounds the ratio change. Face containment gives |numerator| <= projectionScale * major.
                float projectedError = worldEpsilon * (1.0 + projectionScale) * rcp(projectionScale * (minimumMajor - worldEpsilon));
                float uvError = projectedError * 0.5 + VRCLV_SHADOW_UV_EPSILON;
                uvMin = projectedMin * 0.5 + 0.5 - uvError;
                uvMax = projectedMax * 0.5 + 0.5 + uvError;
                return all(uvMin <= uvMax);
            }

            // Cheap necessary-condition probe for a point known to lie inside the froxel hull. Unlike the full rectangle projection this needs no endpoint basis transforms.
            bool BuildShadowPointUv(float3 shadowPoint, bool singleShadow, float shadowTangent, out float2 uv, out uint cubeFace) {
                float tangent = max(shadowTangent, 0.0001);
                cubeFace = singleShadow ? 0u : SelectShadowCubeFace(shadowPoint);
                float2 numerator;
                float major;
                [branch] if (singleShadow) {
                    numerator = -shadowPoint.xy;
                    major = -shadowPoint.z;
                } else {
                    ProjectShadowCubeFace(shadowPoint, cubeFace, numerator, major);
                }
                float projectionScale = singleShadow ? tangent : 1.0;
                float projectionDenominator = major * projectionScale;
                if (!(projectionDenominator > 0.0) || !(max(abs(numerator.x), abs(numerator.y)) <= projectionDenominator)) return false;
                uv = numerator * rcp(projectionDenominator) * 0.5 + 0.5;
                return all(uv >= 0.0) && all(uv <= 1.0);
            }

            float LoadShadowCullDepth(uint linearIndex, uint atlasWidthShift) {
                uint atlasX = linearIndex & ((1u << atlasWidthShift) - 1u);
                uint atlasY = linearIndex >> atlasWidthShift;
                return _UdonShadowCullHierarchy.Load(int3((int)atlasX, (int)atlasY, 0));
            }

            uint CeilLog2Small(uint value) {
                if (value <= 1u) return 0u;
                // Exact for the supported power-of-two shadow resolutions (<= 4096).
                return ((asuint((float)(value - 1u)) >> 23u) & 255u) - 126u;
            }

            // Includes every mip-0 bilinear contributor, then covers it with at most four aligned hierarchy nodes.
            bool QueryShadowCullDepth(float2 uvMin, float2 uvMax, uint shadowSlice, float nearestShadowDepth, bool refineFailedFineQuery) {
                uint resolutionShift = (uint)_UdonFroxelShadowCull.x;
                uint firstStoredLevel = min((uint)_UdonFroxelShadowCull.y, 12u);
                uint sliceCount = (uint)_UdonFroxelShadowCull.z;
                uint atlasWidthShift = (uint)_UdonFroxelShadowCull.w;
                if (shadowSlice >= sliceCount) return false;
                uint resolution = 1u << resolutionShift;
                uint maximumStoredLevel = resolution > 2u ? resolutionShift - 1u : 1u;

                float2 baseResolution = (float)resolution;
                int2 baseMin = (int2)floor(uvMin * baseResolution - 0.5);
                int2 baseMax = (int2)floor(uvMax * baseResolution - 0.5) + 1;
                int maximumBaseIndex = (int)resolution - 1;
                baseMin = min(max(baseMin, 0), maximumBaseIndex);
                baseMax = min(max(baseMax, 0), maximumBaseIndex);

                uint2 contributorSpan = (uint2)(baseMax - baseMin + 1);
                // The 1x1 root is redundant: a full face is exactly covered by the four 2x2 nodes. Clamp that one exceptional request instead of storing another level.
                uint level = min(maximumStoredLevel, max(firstStoredLevel, CeilLog2Small(max(contributorSpan.x, contributorSpan.y))));

                uint2 nodeMin = (uint2)baseMin >> level;
                uint2 nodeMax = (uint2)baseMax >> level;

                uint levelResolution = resolution >> level;
                uint levelNodeCount = levelResolution * levelResolution;
                uint resolutionSquared = resolution * resolution;
                // Exact geometric-series prefix for power-of-four level sizes. The bit pattern avoids integer division on mobile shader compilers: 1 + 4 + ... + 4^(n-1).
                uint levelResolutionShift = resolutionShift - level;
                uint levelPrefix = (0x55555555u << ((levelResolutionShift + 1u) * 2u)) & (resolutionSquared - 1u);
                uint firstLevelResolutionShift = resolutionShift - firstStoredLevel;
                uint firstLevelPrefix = (0x55555555u << ((firstLevelResolutionShift + 1u) * 2u)) & (resolutionSquared - 1u);
                uint sliceBase = sliceCount * (levelPrefix - firstLevelPrefix) + shadowSlice * levelNodeCount;

                uint firstNode = sliceBase + nodeMin.y * levelResolution + nodeMin.x;
                float criticalDepth = LoadShadowCullDepth(firstNode, atlasWidthShift);
                bool levelPassed = nearestShadowDepth > criticalDepth + VRCLV_SHADOW_DEPTH_EPSILON;
                [branch] if (levelPassed && nodeMax.x != nodeMin.x) {
                    criticalDepth = LoadShadowCullDepth(sliceBase + nodeMin.y * levelResolution + nodeMax.x, atlasWidthShift);
                    levelPassed = nearestShadowDepth > criticalDepth + VRCLV_SHADOW_DEPTH_EPSILON;
                }
                [branch] if (levelPassed && nodeMax.y != nodeMin.y) {
                    criticalDepth = LoadShadowCullDepth(sliceBase + nodeMax.y * levelResolution + nodeMin.x, atlasWidthShift);
                    levelPassed = nearestShadowDepth > criticalDepth + VRCLV_SHADOW_DEPTH_EPSILON;
                    [branch] if (levelPassed && nodeMax.x != nodeMin.x) {
                        criticalDepth = LoadShadowCullDepth(sliceBase + nodeMax.y * levelResolution + nodeMax.x, atlasWidthShift);
                        levelPassed = nearestShadowDepth > criticalDepth + VRCLV_SHADOW_DEPTH_EPSILON;
                    }
                }
                if (levelPassed) return true;

                // Fine already paid for a finest-node midpoint witness, so a failed parent is often just a max-reduction aliasing a nearby penumbra into the froxel. Retry at the finest level that still covers the exact footprint with at most six nodes.
                // This only subdivides the same conservative contributor set; it cannot remove a light that the full-resolution EVSM proof would keep.
                [branch] if (!refineFailedFineQuery || level <= firstStoredLevel) return false;
                uint refinedLevel = level - 1u;
                uint2 refinedNodeMin = (uint2)baseMin >> refinedLevel;
                uint2 refinedNodeMax = (uint2)baseMax >> refinedLevel;
                uint2 refinedSpan = refinedNodeMax - refinedNodeMin + 1u;
                uint refinedNodeCount = refinedSpan.x * refinedSpan.y;
                if (refinedNodeCount > 6u) return false;

                [branch] if (refinedLevel > firstStoredLevel) {
                    uint candidateLevel = refinedLevel - 1u;
                    uint2 candidateNodeMin = (uint2)baseMin >> candidateLevel;
                    uint2 candidateNodeMax = (uint2)baseMax >> candidateLevel;
                    uint2 candidateSpan = candidateNodeMax - candidateNodeMin + 1u;
                    uint candidateNodeCount = candidateSpan.x * candidateSpan.y;
                    [branch] if (candidateNodeCount <= 6u) {
                        refinedLevel = candidateLevel;
                        refinedNodeMin = candidateNodeMin;
                        refinedNodeMax = candidateNodeMax;
                        refinedNodeCount = candidateNodeCount;
                    }
                }

                uint refinedResolution = resolution >> refinedLevel;
                uint refinedResolutionShift = resolutionShift - refinedLevel;
                uint refinedPrefix = (0x55555555u << ((refinedResolutionShift + 1u) * 2u)) & (resolutionSquared - 1u);
                uint refinedSliceBase = sliceCount * (refinedPrefix - firstLevelPrefix) + shadowSlice * refinedResolution * refinedResolution;
                uint2 refinedNode = refinedNodeMin;
                uint refinedRowBase = refinedSliceBase + refinedNode.y * refinedResolution;
                [loop] for (uint refinedIndex = 0u; refinedIndex < refinedNodeCount; refinedIndex++) {
                    criticalDepth = LoadShadowCullDepth(refinedRowBase + refinedNode.x, atlasWidthShift);
                    if (!(nearestShadowDepth > criticalDepth + VRCLV_SHADOW_DEPTH_EPSILON)) return false;
                    refinedNode.x++;
                    [flatten] if (refinedNode.x > refinedNodeMax.x) {
                        refinedNode.x = refinedNodeMin.x;
                        refinedNode.y++;
                        refinedRowBase += refinedResolution;
                    }
                }
                return true;
            }

            // Fine candidates have already survived the coarse proof and are strongly biased toward lit/penumbra cells. A single finest-level necessary-condition lookup lets those cells stop before the two exact endpoint-rectangle projections.
            // Coarse deliberately skips this extra read because deeply shadowed cells dominate its useful work. Invalid metadata returns the fail-open 2.0 depth consumed by the caller's existing range check.
            float QueryShadowCullProbeDepth(float2 uv, uint shadowSlice) {
                uint resolutionShift = (uint)_UdonFroxelShadowCull.x;
                uint firstStoredLevel = min((uint)_UdonFroxelShadowCull.y, 12u);
                uint sliceCount = (uint)_UdonFroxelShadowCull.z;
                uint atlasWidthShift = (uint)_UdonFroxelShadowCull.w;
                if (shadowSlice >= sliceCount || firstStoredLevel > resolutionShift) return 2.0;
                uint resolution = 1u << resolutionShift;
                int maximumBaseIndex = (int)resolution - 1;
                int2 basePixel = (int2)floor(uv * (float)resolution - 0.5);
                basePixel = min(max(basePixel, 0), maximumBaseIndex);
                uint2 node = (uint2)basePixel >> firstStoredLevel;
                uint levelResolution = resolution >> firstStoredLevel;
                uint levelNodeCount = levelResolution * levelResolution;
                uint linearIndex = shadowSlice * levelNodeCount + node.y * levelResolution + node.x;
                return LoadShadowCullDepth(linearIndex, atlasWidthShift);
            }

            // Returns true only after the convex hull of the two exact end rectangles is behind the conservative EVSM threshold for every mip-0 texel that bilinear filtering may consume.
            // The projection of a convex combination is a denominator-weighted combination of its endpoint projections, so combining the two rectangle bounds covers the complete hull.
            bool FroxelIsFullyShadowed(uint lightId, float3 lightPosition, float3 froxelNearCenter, float2 froxelNearHalfSize, float3 froxelFarCenter, float2 froxelFarHalfSize, float froxelCoordinateMagnitude, bool useEarlyProbe) {
                // A negative clustering radius selects only entries written by the matching CPU encoder, so the inner loop needs one predecoded load instead of reconstructing these values from four unrelated receiver arrays for every froxel.
                float4 shadowMetadata = _UdonFroxelShadowMetadata[lightId];
                uint shadowBaseSlice = (uint)abs(shadowMetadata.x) - 1u;
                bool localShadow = shadowMetadata.x < 0.0;
                bool singleShadow = shadowMetadata.y < 0.0;
                bool useCurrentLightOrigin = shadowMetadata.z < 0.0;
                bool identityShadowRotation = shadowMetadata.w < 0.0;
                float shadowNearClip = abs(shadowMetadata.y);
                float inverseDepthRange = abs(shadowMetadata.z);
                float depthRange = abs(shadowMetadata.w);

                float4 reprojectionData = 0.0;
                float3 shadowOrigin = lightPosition;
                [branch] if (!useCurrentLightOrigin) {
                    reprojectionData = _UdonPointLightVolumeShadowReprojectionData[lightId];
                    shadowOrigin = reprojectionData.xyz;
                }

                float3 nearReceiverToOrigin = shadowOrigin - froxelNearCenter;
                float3 farReceiverToOrigin = shadowOrigin - froxelFarCenter;
                float3 centerSegment = farReceiverToOrigin - nearReceiverToOrigin;
                float3 centerMidpoint = (nearReceiverToOrigin + farReceiverToOrigin) * 0.5;
                float maximumExtent = max(froxelNearHalfSize.x + froxelNearHalfSize.y, froxelFarHalfSize.x + froxelFarHalfSize.y);
                // Include absolute-coordinate precision: subtracting two large world positions can lose more bits than a distance-only epsilon accounts for. The L1 lengths are
                // cheap upper bounds on the endpoint Euclidean lengths and let the Fine witness run before the closest-segment calculation.
                float coordinateMagnitude = max(max(max(abs(shadowOrigin.x), abs(shadowOrigin.y)), abs(shadowOrigin.z)), froxelCoordinateMagnitude);
                float3 absoluteNearReceiver = abs(nearReceiverToOrigin);
                float3 absoluteFarReceiver = abs(farReceiverToOrigin);
                float maximumCenterDistanceUpper = max(absoluteNearReceiver.x + absoluteNearReceiver.y + absoluteNearReceiver.z, absoluteFarReceiver.x + absoluteFarReceiver.y + absoluteFarReceiver.z);
                float worldEpsilon = max(0.0001, (coordinateMagnitude + maximumCenterDistanceUpper + maximumExtent) * 0.00001);

                float4 shadowRotation = float4(0.0, 0.0, 0.0, 1.0);
                [branch] if (!identityShadowRotation) shadowRotation = _UdonPointLightVolumeShadowRotationData[lightId];
                float shadowTangent = 1.0;
                [branch] if (singleShadow) {
                    shadowTangent = useCurrentLightOrigin ? _UdonPointLightVolumeExtraData[lightId].y : reprojectionData.w;
                }
                float distanceSafety = max(worldEpsilon, (shadowNearClip + depthRange) * 0.000002);

                // Any interior point is a necessary condition for a complete-hull shadow proof. Use the midpoint first: if its finest node fails, the final rectangle (which
                // contains that node and has no greater nearest depth) cannot pass either. This keeps the expensive closest-segment support work out of the common Fine failure.
                float3 shadowMidpoint = centerMidpoint;
                [branch] if (useEarlyProbe) {
                    [branch] if (!identityShadowRotation)
                        shadowMidpoint = RotateShadowVector(shadowMidpoint, shadowRotation);
                    float2 probeUv;
                    uint probeCubeFace;
                    if (!BuildShadowPointUv(shadowMidpoint, singleShadow, shadowTangent, probeUv, probeCubeFace)) return false;
                    uint probeShadowSlice = singleShadow ? shadowBaseSlice : shadowBaseSlice + probeCubeFace;
                    float probeCriticalDepth = QueryShadowCullProbeDepth(probeUv, probeShadowSlice);
                    // Move the monotonic normalized-depth comparison back into physical distance and square it. This is exactly equivalent while criticalDepth < 1, and avoids an SFU square root in every Fine candidate that stops at the probe.
                    float criticalDepthWithEpsilon = probeCriticalDepth + VRCLV_SHADOW_DEPTH_EPSILON;
                    if (!(criticalDepthWithEpsilon < 1.0)) return false;
                    float requiredReceiverDistance = shadowNearClip + depthRange * saturate(criticalDepthWithEpsilon * 0.5 + 0.5) + distanceSafety;
                    if (!(dot(centerMidpoint, centerMidpoint) > requiredReceiverDistance * requiredReceiverDistance)) return false;
                }

                float centerSegmentLengthSq = dot(centerSegment, centerSegment);
                float closestSegmentT = centerSegmentLengthSq > 1.0e-12 ? saturate(-dot(nearReceiverToOrigin, centerSegment) * rcp(centerSegmentLengthSq)) : 0.0;
                float3 closestCenterToOrigin = nearReceiverToOrigin + centerSegment * closestSegmentT;
                float closestCenterDistanceSq = dot(closestCenterToOrigin, closestCenterToOrigin);
                float3 supportDirection = closestCenterToOrigin * rsqrt(max(closestCenterDistanceSq, 1.0e-12));
                float rightSupport = abs(dot(supportDirection, _UdonFroxelRight.xyz));
                float upSupport = abs(dot(supportDirection, _UdonFroxelUp.xyz));
                // Exact support of the two endpoint rectangles along the closest-centerline direction. This removes the fake longitudinal radius of the former spheres.
                float supportDistanceLower = min( dot(supportDirection, nearReceiverToOrigin) - rightSupport * froxelNearHalfSize.x - upSupport * froxelNearHalfSize.y, dot(supportDirection, farReceiverToOrigin) - rightSupport * froxelFarHalfSize.x - upSupport * froxelFarHalfSize.y);
                // The actual end rectangles have zero camera-depth thickness. This slab bound is the safe part of the intuitive "front plane" test: it tightens distance when the
                // shadow origin is in front of the near plane or behind the far plane, while the hull still handles side directions and the complete UV footprint.
                float depthSlabDistanceLower = max(-dot(nearReceiverToOrigin, _UdonFroxelForward.xyz), dot(farReceiverToOrigin, _UdonFroxelForward.xyz));
                float geometricDistanceLower = max(supportDistanceLower, depthSlabDistanceLower);
                if (geometricDistanceLower <= 0.0) return false;

                // Non-local receivers switch to the legacy fallback vector inside a 0.01-unit sphere. A froxel touching that branch boundary cannot be represented by one projection/origin proof.
                if (!localShadow && geometricDistanceLower <= VRCLV_SHADOW_REPROJECTION_GUARD + distanceSafety) return false;

                // Rotation is linear: rotate the centerline midpoint and the segment to recover both endpoints, then rotate the two shared rectangle axes once.
                float3 shadowCenterSegment = centerSegment;
                float3 shadowRight = _UdonFroxelRight.xyz;
                float3 shadowUp = _UdonFroxelUp.xyz;
                [branch] if (!identityShadowRotation) {
                    [branch] if (!useEarlyProbe) shadowMidpoint = RotateShadowVector(shadowMidpoint, shadowRotation);
                    shadowCenterSegment = RotateShadowVector(shadowCenterSegment, shadowRotation);
                    shadowRight = RotateShadowVector(shadowRight, shadowRotation);
                    shadowUp = RotateShadowVector(shadowUp, shadowRotation);
                }
                float3 shadowNearCenter = shadowMidpoint - shadowCenterSegment * 0.5;
                float3 shadowFarCenter = shadowMidpoint + shadowCenterSegment * 0.5;
                float tangent = max(shadowTangent, 0.0001);
                float projectionScale = singleShadow ? tangent : 1.0;
                float planeNormalLength = VRCLV_SHADOW_FACE_SQRT_TWO;
                [branch] if (singleShadow) planeNormalLength = sqrt(1.0 + tangent * tangent);
                uint cubeFace = singleShadow ? 0u : SelectShadowCubeFace(shadowMidpoint);
                float2 rightNumerator;
                float rightMajor;
                float2 upNumerator;
                float upMajor;
                [branch] if (singleShadow) {
                    rightNumerator = -shadowRight.xy;
                    rightMajor = -shadowRight.z;
                    upNumerator = -shadowUp.xy;
                    upMajor = -shadowUp.z;
                } else {
                    ProjectShadowCubeFace(shadowRight, cubeFace, rightNumerator, rightMajor);
                    ProjectShadowCubeFace(shadowUp, cubeFace, upNumerator, upMajor);
                }
                float2 nearUvMin;
                float2 nearUvMax;
                float2 farUvMin;
                float2 farUvMax;
                if (!BuildShadowRectangleUvBounds(shadowNearCenter, rightNumerator, rightMajor, upNumerator, upMajor, froxelNearHalfSize, singleShadow, projectionScale, planeNormalLength, cubeFace, worldEpsilon, nearUvMin, nearUvMax)) return false;
                if (!BuildShadowRectangleUvBounds(shadowFarCenter, rightNumerator, rightMajor, upNumerator, upMajor, froxelFarHalfSize, singleShadow, projectionScale, planeNormalLength, cubeFace, worldEpsilon, farUvMin, farUvMax)) return false;
                float2 uvMin = min(nearUvMin, farUvMin);
                float2 uvMax = max(nearUvMax, farUvMax);

                float nearestReceiverDistance = geometricDistanceLower - distanceSafety;
                if (nearestReceiverDistance <= 0.0) return false;
                float nearestNormalizedDepth = saturate((nearestReceiverDistance - shadowNearClip) * inverseDepthRange);
                float nearestShadowDepth = nearestNormalizedDepth * 2.0 - 1.0;

                uint shadowSlice = singleShadow ? shadowBaseSlice : shadowBaseSlice + cubeFace;
                return QueryShadowCullDepth(uvMin, uvMax, shadowSlice, nearestShadowDepth, useEarlyProbe);
            }

            // Range rejection is performed before this shape-specific path, so point lights pay none of this cost.
            bool IntersectsFroxelLightShape(float3 lightToFroxel, float lightDistanceSq, float froxelRadius, float combinedRadius, uint packedShape) {
                uint shapeCode = packedShape >> 16u;
                bool intersects = true;
                [branch] if (shapeCode != 0u) {
                    float3 shapeAxis = DecodeClusterShapeAxis(packedShape);
                    float axialDistance = dot(lightToFroxel, shapeAxis);
                    [branch] if (shapeCode == 1u) {
                        intersects = axialDistance + froxelRadius + combinedRadius * VRCLV_FROXEL_AXIS_ERROR >= 0.0;
                    } else {
                        float encodedTangent = (float)(shapeCode - 1u) * (1.0 / 255.0);
                        float coneTangent = encodedTangent * rcp(1.0 - encodedTangent);
                        float conservativeSecant = max(coneTangent, 1.0) + min(coneTangent, 1.0) * 0.5;
                        float radialLimit = max(axialDistance, 0.0) * coneTangent + froxelRadius * conservativeSecant;
                        float radialDistanceSq = max(lightDistanceSq - axialDistance * axialDistance, 0.0);
                        intersects = axialDistance + froxelRadius >= 0.0 && radialDistanceSq <= radialLimit * radialLimit;
                    }
                }
                return intersects;
            }

            bool LightIntersectsFroxel(uint lightId, float3 froxelCenter, float froxelRadius, out bool shadowCullEligible, out float3 lightPosition) {
                float4 packedData = _UdonClusteringLights[lightId >> 1u];
                float2 lightData = (lightId & 1u) == 0u ? packedData.xy : packedData.zw;
                uint packedShape = (uint)lightData.y;
                shadowCullEligible = lightData.x < 0.0;
                float combinedRadius = abs(lightData.x) + froxelRadius;
                lightPosition = _UdonPointLightVolumePosition[lightId].xyz;
                float3 lightToFroxel = froxelCenter - lightPosition;
                float lightDistanceSq = dot(lightToFroxel, lightToFroxel);
                bool intersects = lightDistanceSq <= combinedRadius * combinedRadius;
                [branch] if (intersects) intersects = IntersectsFroxelLightShape(lightToFroxel, lightDistanceSq, froxelRadius, combinedRadius, packedShape);
                return intersects;
            }

            uint SelectMaskWord(uint4 mask, uint wordIndex) {
                uint value = mask.w;
                [branch] if (wordIndex == 0u) value = mask.x;
                else [branch] if (wordIndex == 1u) value = mask.y;
                else [branch] if (wordIndex == 2u) value = mask.z;
                return value;
            }

            void StoreMaskWord(inout uint4 mask, uint wordIndex, uint value) {
                [branch] if (wordIndex == 0u) mask.x = value;
                else [branch] if (wordIndex == 1u) mask.y = value;
                else [branch] if (wordIndex == 2u) mask.z = value;
                else mask.w = value;
            }

            // Coarse shadow rejection is a cheap hierarchical early-out: every removed light is absent from all child fine candidate lists, while ambiguous coarse cells still get the full-resolution proof below. One rolled call site avoids FXC graph cloning.
            uint4 BuildClusterMask(uint pointLightCount, float3 froxelCenter, float froxelRadius, float3 froxelNearCenter, float2 froxelNearHalfSize, float3 froxelFarCenter, float2 froxelFarHalfSize, float froxelCoordinateMagnitude, bool shadowCullEnabled) {
                uint4 result = 0u;
                [fastopt] for (uint wordIndex = 0u; wordIndex < 4u; wordIndex++) {
                    uint firstLightId = wordIndex << 5u;
                    if (firstLightId >= pointLightCount) break;
                    uint resultWord = 0u;
                    [fastopt] for (uint bitIndex = 0u; bitIndex < 32u; bitIndex++) {
                        uint lightId = firstLightId + bitIndex;
                        if (lightId >= pointLightCount) break;
                        bool shadowCullEligible;
                        float3 lightPosition;
                        if (LightIntersectsFroxel(lightId, froxelCenter, froxelRadius, shadowCullEligible, lightPosition)) {
                            bool keepLight = true;
                            [branch] if (shadowCullEnabled && shadowCullEligible)
                                keepLight = !FroxelIsFullyShadowed(lightId, lightPosition, froxelNearCenter, froxelNearHalfSize, froxelFarCenter, froxelFarHalfSize, froxelCoordinateMagnitude, false);
                            if (keepLight) resultWord |= 1u << bitIndex;
                        }
                    }
                    StoreMaskWord(result, wordIndex, resultWord);
                }
                return result;
            }

            uint LowestBitIndex(uint lowestBit) {
                #if SHADER_TARGET >= 45
                    return (uint)firstbitlow(lowestBit);
                #else
                    // Exact for a power-of-two uint and available on SM3.5 / GLES3.0.
                    return ((asuint((float)lowestBit) >> 23u) & 255u) - 127u;
                #endif
            }

            // Fine keeps the original bits and has one source call site for the complete Hi-Z proof.
            uint4 RefineClusterMask(uint4 candidateWords, float3 froxelCenter, float froxelRadius, float3 froxelNearCenter, float2 froxelNearHalfSize, float3 froxelFarCenter, float2 froxelFarHalfSize, float froxelCoordinateMagnitude, bool shadowCullEnabled) {
                uint4 result = 0u;
                [fastopt] for (uint wordIndex = 0u; wordIndex < 4u; wordIndex++) {
                    uint candidates = SelectMaskWord(candidateWords, wordIndex);
                    uint resultWord = 0u;
                    [fastopt] while (candidates != 0u) {
                        uint lowestBit = candidates & (0u - candidates);
                        candidates &= candidates - 1u;
                        uint lightId = (wordIndex << 5u) + LowestBitIndex(lowestBit);
                        bool shadowCullEligible;
                        float3 lightPosition;
                        [branch] if (LightIntersectsFroxel(lightId, froxelCenter, froxelRadius, shadowCullEligible, lightPosition)) {
                            bool keepLight = true;
                            [branch] if (shadowCullEnabled && shadowCullEligible)
                                keepLight = !FroxelIsFullyShadowed(lightId, lightPosition, froxelNearCenter, froxelNearHalfSize, froxelFarCenter, froxelFarHalfSize, froxelCoordinateMagnitude, true);
                            if (keepLight) resultWord |= lowestBit;
                        }
                    }
                    StoreMaskWord(result, wordIndex, resultWord);
                }
                return result;
            }

            int4 FragmentCoarse(Varyings input) : SV_Target {
                uint3 cell;
                [branch] if (!DecodeAtlasCell(input.position.xy, _UdonFroxelCoarseGrid, _UdonFroxelGridInverse.zw, cell)) return int4(0, 0, 0, 0);

                bool shadowCullEnabled = _UdonLightVolumeVersion >= 3.0 && _UdonFroxelShadowCull.x >= 1.0;
                float3 froxelCenter;
                float froxelRadius;
                float3 froxelNearCenter;
                float2 froxelNearHalfSize;
                float3 froxelFarCenter;
                float2 froxelFarHalfSize;
                BuildFroxelBounds(cell, (uint)_UdonFroxelCoarse.x, shadowCullEnabled, froxelCenter, froxelRadius, froxelNearCenter, froxelNearHalfSize, froxelFarCenter, froxelFarHalfSize);
                float3 absoluteFroxelCenter = abs(froxelCenter);
                float froxelCoordinateMagnitude = max(absoluteFroxelCenter.x, max(absoluteFroxelCenter.y, absoluteFroxelCenter.z));
                uint pointLightCount = min((uint)_UdonPointLightVolumeCount, (uint)VRCLV_MAX_POINT_LIGHTS);
                return asint(BuildClusterMask(pointLightCount, froxelCenter, froxelRadius, froxelNearCenter, froxelNearHalfSize, froxelFarCenter, froxelFarHalfSize, froxelCoordinateMagnitude, shadowCullEnabled));
            }

            int4 FragmentFine(Varyings input) : SV_Target {
                uint3 cell;
                [branch] if (!DecodeAtlasCell(input.position.xy, _UdonFroxelFineGrid, _UdonFroxelGridInverse.xy, cell)) return int4(0, 0, 0, 0);

                uint reductionShift = (uint)_UdonFroxelCoarse.y;
                uint3 coarseCell = cell >> reductionShift;
                uint4 candidates = asuint(_UdonCoarseClusterMask.Load(int3(FroxelCellToAtlas(coarseCell, _UdonFroxelCoarseGrid), 0)));
                [branch] if ((candidates.x | candidates.y | candidates.z | candidates.w) == 0u) return int4(0, 0, 0, 0);

                bool shadowCullEnabled = _UdonLightVolumeVersion >= 3.0 && _UdonFroxelShadowCull.x >= 1.0;
                float3 froxelCenter;
                float froxelRadius;
                float3 froxelNearCenter;
                float2 froxelNearHalfSize;
                float3 froxelFarCenter;
                float2 froxelFarHalfSize;
                BuildFroxelBounds(cell, 1u, shadowCullEnabled, froxelCenter, froxelRadius, froxelNearCenter, froxelNearHalfSize, froxelFarCenter, froxelFarHalfSize);
                float3 absoluteFroxelCenter = abs(froxelCenter);
                float froxelCoordinateMagnitude = max(absoluteFroxelCenter.x, max(absoluteFroxelCenter.y, absoluteFroxelCenter.z));
                return asint(RefineClusterMask(candidates, froxelCenter, froxelRadius, froxelNearCenter, froxelNearHalfSize, froxelFarCenter, froxelFarHalfSize, froxelCoordinateMagnitude, shadowCullEnabled));
            }
        ENDCG

        Pass {
            Name "Coarse"
            CGPROGRAM
            #pragma target 3.5
            #pragma only_renderers d3d11 glcore vulkan gles3 metal
            #pragma vertex Vertex
            #pragma fragment FragmentCoarse
            #pragma require integers
            ENDCG
        }

        Pass {
            Name "Fine"
            CGPROGRAM
            #pragma target 3.5
            #pragma only_renderers d3d11 glcore vulkan gles3 metal
            #pragma vertex Vertex
            #pragma fragment FragmentFine
            #pragma require integers
            ENDCG
        }
    }
    Fallback Off
}
