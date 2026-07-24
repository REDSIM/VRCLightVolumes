Shader "Hidden/VRCLV/FroxelClusteringBuild" {
    SubShader {
        Tags { "RenderType" = "Opaque" }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass {
            CGPROGRAM
            #pragma target 3.5
            #pragma only_renderers d3d11 glcore vulkan gles3 metal
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma require integers

            #include "UnityCG.cginc"

            #define VRCLV_MAX_POINT_LIGHTS 128
            #define VRCLV_FROXEL_AXIS_ERROR 0.02

            float _UdonPointLightVolumeCount;
            float4 _UdonPointLightVolumePosition[VRCLV_MAX_POINT_LIGHTS];
            float4 _UdonClusteringLights[VRCLV_MAX_POINT_LIGHTS / 2];
            Texture2D<int4> _UdonCoarseClusterMask;
            float4 _UdonFroxelGrid;
            float4 _UdonFroxelFineGrid;
            float4 _UdonFroxelCoarseGrid;
            float4 _UdonFroxelDepth;
            float4 _UdonFroxelDepthStep;
            float4 _UdonFroxelProjection;
            float4 _UdonFroxelRight;
            float4 _UdonFroxelUp;
            float4 _UdonFroxelForward;
            float4 _UdonFroxelCoarse; // xy: factor/log2(factor), zw: reciprocal Fine columns/rows
            float _UdonFroxelPass;    // 0: Coarse, 1: Fine

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
            bool DecodeAtlasCell(float2 pixelPosition, float4 gridParams, out uint3 cell) {
                uint2 pixel = (uint2)pixelPosition;
                uint columns = (uint)gridParams.x;
                uint depthSlices = (uint)gridParams.y;
                uint tileShift = (uint)gridParams.w;
                // Pixel centers stay half a texel away from tile boundaries, so reciprocal
                // multiply is exact for the supported <= 4096 atlas and avoids integer divides.
                uint2 tile = (uint2)(pixelPosition * rcp(gridParams.xy));
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

            // Reconstructs a conservative sphere around the exact union of child Fine froxels.
            // Using Fine boundaries here keeps the last non-divisible Coarse cell nested and prevents false negatives.
            void BuildFroxelSphere(uint3 cell, uint childScale, out float3 center, out float radius) {
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
                float2 boundsMin = min(normalizedMin * nearExtent, normalizedMin * farExtent);
                float2 boundsMax = max(normalizedMax * nearExtent, normalizedMax * farExtent);

                float3 localCenter = float3((boundsMin + boundsMax) * 0.5, (nearDepth + farDepth) * 0.5);
                float3 halfSize = float3((boundsMax - boundsMin) * 0.5, (farDepth - nearDepth) * 0.5);
                radius = length(halfSize) * 1.000001 + 0.001;

                float3 cameraPosition = float3(_UdonFroxelRight.w, _UdonFroxelUp.w, _UdonFroxelForward.w);
                center = cameraPosition
                    + _UdonFroxelRight.xyz * localCenter.x
                    + _UdonFroxelUp.xyz * localCenter.y
                    + _UdonFroxelForward.xyz * localCenter.z;
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

            bool LightIntersectsFroxel(uint lightId, float3 froxelCenter, float froxelRadius) {
                float4 packedData = _UdonClusteringLights[lightId >> 1u];
                float2 lightData = (lightId & 1u) == 0u ? packedData.xy : packedData.zw;
                float combinedRadius = lightData.x + froxelRadius;
                float3 lightToFroxel = froxelCenter - _UdonPointLightVolumePosition[lightId].xyz;
                float lightDistanceSq = dot(lightToFroxel, lightToFroxel);
                bool intersects = lightDistanceSq <= combinedRadius * combinedRadius;
                [branch] if (intersects) intersects = IntersectsFroxelLightShape(lightToFroxel, lightDistanceSq, froxelRadius, combinedRadius, (uint)lightData.y);
                return intersects;
            }

            // Builds one fixed 32-light word, avoiding a dynamic uint4 write and word-selection branch per hit.
            uint BuildMaskWord(uint firstLightId, uint pointLightCount, float3 froxelCenter, float froxelRadius) {
                uint result = 0u;
                [loop] for (uint bitIndex = 0u; bitIndex < 32u; bitIndex++) {
                    uint lightId = firstLightId + bitIndex;
                    if (lightId >= pointLightCount) break;
                    if (LightIntersectsFroxel(lightId, froxelCenter, froxelRadius)) result |= 1u << bitIndex;
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

            // Fine keeps the original bit positions and never tests a light rejected by its exact parent Coarse cell.
            uint RefineMaskWord(uint candidates, uint firstLightId, float3 froxelCenter, float froxelRadius) {
                uint result = 0u;
                [loop] while (candidates != 0u) {
                    uint lowestBit = candidates & (0u - candidates);
                    candidates &= candidates - 1u;
                    uint lightId = firstLightId + LowestBitIndex(lowestBit);
                    if (LightIntersectsFroxel(lightId, froxelCenter, froxelRadius)) result |= lowestBit;
                }
                return result;
            }

            int4 Fragment(Varyings input) : SV_Target {
                uint3 cell;
                [branch] if (!DecodeAtlasCell(input.position.xy, _UdonFroxelGrid, cell)) return int4(0, 0, 0, 0);

                bool finePass = _UdonFroxelPass >= 0.5;
                uint4 candidates = 0u;
                [branch] if (finePass) {
                    uint reductionShift = (uint)_UdonFroxelCoarse.y;
                    uint3 coarseCell = cell >> reductionShift;
                    candidates = asuint(_UdonCoarseClusterMask.Load(int3(FroxelCellToAtlas(coarseCell, _UdonFroxelCoarseGrid), 0)));
                    [branch] if ((candidates.x | candidates.y | candidates.z | candidates.w) == 0u) return int4(0, 0, 0, 0);
                }

                float3 froxelCenter;
                float froxelRadius;
                uint childScale = finePass ? 1u : (uint)_UdonFroxelCoarse.x;
                BuildFroxelSphere(cell, childScale, froxelCenter, froxelRadius);

                uint pointLightCount = min((uint)_UdonPointLightVolumeCount, (uint)VRCLV_MAX_POINT_LIGHTS);
                uint4 mask = 0u;
                [branch] if (finePass) {
                    mask.x = RefineMaskWord(candidates.x, 0u, froxelCenter, froxelRadius);
                    mask.y = RefineMaskWord(candidates.y, 32u, froxelCenter, froxelRadius);
                    mask.z = RefineMaskWord(candidates.z, 64u, froxelCenter, froxelRadius);
                    mask.w = RefineMaskWord(candidates.w, 96u, froxelCenter, froxelRadius);
                } else {
                    mask.x = BuildMaskWord(0u, pointLightCount, froxelCenter, froxelRadius);
                    [branch] if (pointLightCount > 32u) mask.y = BuildMaskWord(32u, pointLightCount, froxelCenter, froxelRadius);
                    [branch] if (pointLightCount > 64u) mask.z = BuildMaskWord(64u, pointLightCount, froxelCenter, froxelRadius);
                    [branch] if (pointLightCount > 96u) mask.w = BuildMaskWord(96u, pointLightCount, froxelCenter, froxelRadius);
                }
                return asint(mask);
            }
            ENDCG
        }
    }
    Fallback Off
}
