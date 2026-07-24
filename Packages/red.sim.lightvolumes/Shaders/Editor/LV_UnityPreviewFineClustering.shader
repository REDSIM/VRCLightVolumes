Shader "Hidden/LV_DebugDisplayFineClustering"
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

            struct Attributes
            {
                float4 vertex : POSITION;
            };

            struct Varyings
            {
                float3 worldPosition : TEXCOORD0;
                float4 position : SV_POSITION;
            };

            // Places all 128 mask bits into one well-distributed deterministic color key.
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

            // Converts a hash to a bright, highly saturated RGB color.
            half3 ClusterMaskColor(uint hash)
            {
                float hue = (float)(hash & 16777215u) * (1.0 / 16777216.0);
                float3 hueRgb = saturate(abs(frac(hue + float3(0.0, 0.6666667, 0.3333333)) * 6.0 - 3.0) - 1.0);
                half saturation = 0.84h + (half)((hash >> 24u) & 3u) * 0.04h;
                return lerp(half3(1.0h, 1.0h, 1.0h), (half3)hueRgb, saturation);
            }

            // Supplies the world position used to address the camera-aligned froxel atlas.
            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.position = UnityObjectToClipPos(input.vertex);
                return output;
            }

            // Displays empty or unavailable froxels as black and hashes every populated mask to a vivid color.
            half4 Fragment(Varyings input) : SV_Target
            {
                #if VRCLV_CLUSTERING_SUPPORTED
                uint4 mask = 0u;
                bool insideFroxelVolume = false;
                LV_LoadClusterMask(input.worldPosition, mask, insideFroxelVolume);
                [branch] if (insideFroxelVolume && (mask.x | mask.y | mask.z | mask.w) != 0u)
                    return half4(ClusterMaskColor(HashClusterMask(mask)), 1.0h);
                #endif

                return half4(0.0h, 0.0h, 0.0h, 1.0h);
            }
            ENDCG
        }
    }
}
