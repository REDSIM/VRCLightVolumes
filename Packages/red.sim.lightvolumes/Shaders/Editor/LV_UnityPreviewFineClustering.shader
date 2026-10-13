Shader "Hidden/LV_DebugDisplayFineClustering"
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
            #define VRCLV_PREVIEW_FINE_CLUSTERING 1
            #include "Packages/red.sim.lightvolumes/Shaders/Editor/LV_UnityPreviewClusteringCommon.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
            };

            struct Varyings
            {
                float3 worldPosition : TEXCOORD0;
                float4 position : SV_POSITION;
            };

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
                uint4 mask = 0u;
                bool insideFroxelVolume = false;
                VRCLVPreviewLoadFineClusterMask(input.worldPosition, mask, insideFroxelVolume);
                [branch] if (insideFroxelVolume && (mask.x | mask.y | mask.z | mask.w) != 0u)
                    return half4(VRCLVPreviewClusterMaskColor(VRCLVPreviewHashClusterMask(mask)), 1.0h);

                return half4(0.0h, 0.0h, 0.0h, 1.0h);
            }
            ENDCG
        }
    }
}
