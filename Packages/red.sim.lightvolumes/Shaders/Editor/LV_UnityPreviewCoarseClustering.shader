Shader "Hidden/LV_DebugDisplayCoarseClustering"
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
            #define VRCLV_PREVIEW_COARSE_CLUSTERING 1
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

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.position = UnityObjectToClipPos(input.vertex);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                uint4 mask = VRCLVPreviewLoadCoarseClusterMask(input.worldPosition);
                [branch] if ((mask.x | mask.y | mask.z | mask.w) != 0u)
                    return half4(VRCLVPreviewClusterMaskColor(VRCLVPreviewHashClusterMask(mask)), 1.0h);

                return half4(0.0h, 0.0h, 0.0h, 1.0h);
            }
            ENDCG
        }
    }
}
