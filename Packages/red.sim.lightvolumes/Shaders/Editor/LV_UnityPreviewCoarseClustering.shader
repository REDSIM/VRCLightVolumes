Shader "Hidden/LV_DebugDisplayCoarseClustering"
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "UnityCG.cginc"
            #include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"

            #if VRCLV_CLUSTERING_SUPPORTED
            Texture2D<int4> _UdonCoarseClusterMask;
            float4 _UdonFroxelCoarseGrid;
            float4 _UdonFroxelCoarse;
            #endif

            struct Attributes
            {
                float4 vertex : POSITION;
            };

            struct Varyings
            {
                float3 worldPosition : TEXCOORD0;
                float4 position : SV_POSITION;
            };

            uint HashClusterMask(uint4 mask)
            {
                uint hash = 2166136261u;
                hash = (hash ^ mask.x) * 16777619u;
                hash = (hash ^ mask.y) * 16777619u;
                hash = (hash ^ mask.z) * 16777619u;
                hash = (hash ^ mask.w) * 16777619u;
                hash ^= hash >> 16u;
                hash *= 2146121005u;
                hash ^= hash >> 15u;
                hash *= 2221713035u;
                hash ^= hash >> 16u;
                return hash;
            }

            half3 ClusterMaskColor(uint hash)
            {
                float hue = (float)(hash & 16777215u) * (1.0 / 16777216.0);
                float3 hueRgb = saturate(abs(frac(hue + float3(0.0, 0.6666667, 0.3333333)) * 6.0 - 3.0) - 1.0);
                half saturation = 0.84h + (half)((hash >> 24u) & 3u) * 0.04h;
                return lerp(half3(1.0h, 1.0h, 1.0h), (half3)hueRgb, saturation);
            }

            #if VRCLV_CLUSTERING_SUPPORTED
            // Converts world position to Fine coordinates first, then loads the exact parent consumed by the Fine builder.
            uint4 LoadCoarseClusterMask(float3 worldPosition)
            {
                uint4 result = 0u;
                bool valid = _UdonClusteringEnabled >= 0.5;
                [branch] if (valid) {
                    float3 cameraPosition = float3(_UdonFroxelRight.w, _UdonFroxelUp.w, _UdonFroxelForward.w);
                    float3 cameraDelta = worldPosition - cameraPosition;
                    float viewDepth = dot(cameraDelta, _UdonFroxelForward.xyz);
                    valid = viewDepth >= _UdonFroxelDepth.x && viewDepth <= _UdonFroxelDepth.y;
                    [branch] if (valid) {
                        float2 viewPosition = float2(dot(cameraDelta, _UdonFroxelRight.xyz), dot(cameraDelta, _UdonFroxelUp.xyz));
                        float2 halfExtent = viewDepth * _UdonFroxelProjection.xy + _UdonFroxelProjection.zw;
                        valid = all(abs(viewPosition) <= halfExtent);
                        [branch] if (valid) {
                            float2 screenUv = saturate(viewPosition * (0.5 / halfExtent) + 0.5);
                            float depthIndex = max(log2(viewDepth * _UdonFroxelDepth.z) * _UdonFroxelDepth.w, 0.0);
                            uint3 fineGrid = (uint3)_UdonFroxelGrid.xyz;
                            uint3 fineCell = uint3(
                                min((uint)(screenUv.x * (float)fineGrid.x), fineGrid.x - 1u),
                                min((uint)(screenUv.y * (float)fineGrid.z), fineGrid.z - 1u),
                                min((uint)depthIndex, fineGrid.y - 1u));

                            uint reductionShift = (uint)_UdonFroxelCoarse.y;
                            uint3 coarseCell = fineCell >> reductionShift;
                            uint tileShift = (uint)_UdonFroxelCoarseGrid.w;
                            uint tileX = coarseCell.y & ((1u << tileShift) - 1u);
                            uint tileY = coarseCell.y >> tileShift;
                            int2 atlasTexel = int2(
                                tileX * (uint)_UdonFroxelCoarseGrid.x + coarseCell.x,
                                tileY * (uint)_UdonFroxelCoarseGrid.y + coarseCell.z);
                            result = asuint(_UdonCoarseClusterMask.Load(int3(atlasTexel, 0)));
                        }
                    }
                }
                return result;
            }
            #endif

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.position = UnityObjectToClipPos(input.vertex);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                #if VRCLV_CLUSTERING_SUPPORTED
                uint4 mask = LoadCoarseClusterMask(input.worldPosition);
                [branch] if ((mask.x | mask.y | mask.z | mask.w) != 0u)
                    return half4(ClusterMaskColor(HashClusterMask(mask)), 1.0h);
                #endif

                return half4(0.0h, 0.0h, 0.0h, 1.0h);
            }
            ENDCG
        }
    }
}
