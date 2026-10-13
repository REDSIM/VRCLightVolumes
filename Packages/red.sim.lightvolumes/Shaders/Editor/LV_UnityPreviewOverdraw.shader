Shader "Hidden/LV_DebugDisplayOverdraw"
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma only_renderers d3d11 glcore vulkan gles3 metal
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma require integers

            #include "UnityCG.cginc"
            #define VRCLV_FORCE_FULL_FEATURES 1
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

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.position = UnityObjectToClipPos(input.vertex);
                return output;
            }

            // Count the same volume samples as LV_LightVolumeRegularSH, including boundary fallbacks.
            uint CountRegularVolumes(float3 worldPosition)
            {
                uint volumeCount = min((uint)_UdonLightVolumeCount, VRCLV_MAX_VOLUMES_COUNT);
                uint additiveCount = min((uint)_UdonLightVolumeAdditiveCount, volumeCount);
                uint count = 0u;
                [branch] if (volumeCount > additiveCount)
                {
                    uint volumeA = additiveCount;
                    float3 localPosition = 0;
                    VRCLV_DYNAMIC_LOOP for (; volumeA < volumeCount; volumeA++)
                    {
                        localPosition = LV_LocalFromVolume(volumeA, worldPosition);
                        if (LV_PointLocalAABB(localPosition)) break;
                    }

                    [branch] if (volumeA == volumeCount)
                        count = _UdonLightVolumeProbesBlend != 0 ? 0u : 1u;
                    else
                    {
                        count = 1u;
                        float mask = LV_BoundsMask(localPosition, _UdonLightVolumeInvLocalEdgeSmooth[volumeA]);
                        [branch] if (mask != 1)
                        {
                            uint volumeB = volumeA + 1u;
                            VRCLV_DYNAMIC_LOOP for (; volumeB < volumeCount; volumeB++)
                            {
                                if (LV_PointLocalAABB(LV_LocalFromVolume(volumeB, worldPosition))) break;
                            }
                            if (volumeB < volumeCount || (_UdonLightVolumeSharpBounds == 0 && _UdonLightVolumeProbesBlend == 0)) count = 2u;
                        }
                    }
                }
                return count;
            }

            uint CountAdditiveVolumes(float3 worldPosition)
            {
                uint additiveCount = min((uint)_UdonLightVolumeAdditiveCount, VRCLV_MAX_VOLUMES_COUNT);
                uint maxOverdraw = min((uint)_UdonLightVolumeAdditiveMaxOverdraw, additiveCount);
                uint count = 0u;
                VRCLV_DYNAMIC_LOOP for (uint id = 0u; id < additiveCount && count < maxOverdraw; id++)
                {
                    if (LV_PointLocalAABB(LV_LocalFromVolume(id, worldPosition))) count++;
                }
                return count;
            }

            uint CountPointLights(float3 worldPosition)
            {
                uint pointCount = min((uint)_UdonPointLightVolumeCount, VRCLV_MAX_LIGHTS_COUNT);
                uint maxOverdraw = min((uint)_UdonLightVolumeAdditiveMaxOverdraw, pointCount);
                uint4 clusterMask = 0u;
                bool useClustering = false;
                LV_LoadClusterMask(worldPosition, clusterMask, useClustering);
                uint traversalIndex = 0u;
                uint maskBits = clusterMask.x;
                uint sequentialEnd = useClustering ? 0u : pointCount;
                uint count = 0u;
                VRCLV_DYNAMIC_LOOP while (count < maxOverdraw)
                {
                    uint id;
                    if (traversalIndex < sequentialEnd) id = traversalIndex++;
                    else if (!LV_NextClusteredLight(clusterMask, traversalIndex, maskBits, id)) break;

                    float3 l0, l1, lightDirection;
                    float specularSpread, shadow;
                    // Match the lighting evaluation boundary, independent of brightness and surface normal.
                    if (LV_PointLightVolumeContribution(id, worldPosition, 0, -1, l0, l1, lightDirection, specularSpread, shadow)) count++;
                }
                return count;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                [branch] if (LightVolumesEnabled() == 0) return half4(0, 0, 0, 1);

                uint count = CountRegularVolumes(input.worldPosition)
                    + CountAdditiveVolumes(input.worldPosition)
                    + CountPointLights(input.worldPosition);
                // Fixed reference: 128 lights + 32 additive volumes + 2 regular-volume samples.
                float load = saturate((float)count / (VRCLV_MAX_LIGHTS_COUNT + VRCLV_MAX_VOLUMES_COUNT + 2.0));
                // Solve (1 - exp2(-k * 40/162)) / (1 - exp2(-k)) = 0.8.
                // This normalized exponential reaches 80% at 40 samples and 100% at 162.
                const float rampExponent = 9.3685586;
                float intensity = (1.0 - exp2(-rampExponent * load)) / (1.0 - exp2(-rampExponent));
                float3 color = intensity * lerp(float3(1.0, 0.4, 0.2), float3(1.0, 1.0, 1.0), load * load);
                return half4(color, 1);
            }
            ENDCG
        }
    }
}
