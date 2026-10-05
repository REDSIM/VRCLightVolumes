Shader "Light Volume Samples/Light Volume Debugger Clustering" {
    SubShader {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" "DisableBatching" = "True" "VRCFallback" = "Hidden" }
        Pass {
            Cull Front
            ZWrite Off
            ZTest Always
            Blend Off

            CGPROGRAM
            #pragma target 3.5
            #pragma require integers
            #pragma only_renderers d3d11 glcore vulkan gles3 metal
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #define VRCLV_PREVIEW_FINE_CLUSTERING
            #include "../../Shaders/Editor/LV_UnityPreviewClusteringCommon.cginc"

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float _UdonLightVolumeEnabled;
            float _UdonLightVolumeVersion;

            struct appdata {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f {
                float4 position : SV_POSITION;
                float4 screen : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 center = unity_ObjectToWorld._m03_m13_m23;
                float3 cameraPosition = _WorldSpaceCameraPos;
                #if defined(USING_STEREO_MATRICES)
                cameraPosition = (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]) * 0.5;
                #endif
                float3 delta = cameraPosition - center;
                // Collapse the cube before rasterization when this camera cannot use the overlay.
                bool visible = dot(delta, delta) <= 9.0 && _UdonLightVolumeEnabled > 0.0
                    && _UdonLightVolumeVersion >= 3.0 && _UdonClusteringEnabled >= 0.5;
                if (!visible) {
                    o.position = float4(0.0, 0.0, 0.0, 1.0);
                    return o;
                }

                // Keep the 3 m camera radius independent of avatar rotation and scale.
                o.position = UnityWorldToClipPos(center + v.vertex.xyz);
                o.screen = ComputeNonStereoScreenPos(o.position);
                // Only the cube's screen coverage matters, including at the camera's clipping planes.
                o.position.z = o.position.w * 0.5;
                return o;
            }

            half4 frag(v2f i) : SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 eyeUV = i.screen.xy / i.screen.w;
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, UnityStereoTransformScreenSpaceTex(eyeUV));
                // Sky and unbound depth textures must leave the normal view visible.
                clip(min(rawDepth, 1.0 - rawDepth) - 0.000001);

                float deviceDepth = rawDepth;
                #if !defined(UNITY_REVERSED_Z)
                deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif
                float2 ndc = eyeUV * 2.0 - 1.0;
                ndc.y *= _ProjectionParams.x;

                // Solve the current projection, including asymmetric stereo and oblique depth rows.
                float4x4 p = UNITY_MATRIX_P;
                float2 invDiagonal = rcp(float2(p._m00, p._m11));
                float2 zFactor = (ndc * p._m32 - float2(p._m02, p._m12)) * invDiagonal;
                float2 offset = (ndc * p._m33 - float2(p._m03, p._m13)) * invDiagonal;
                float2 depthRow = float2(p._m20, p._m21);
                float denominator = deviceDepth * p._m32 - dot(depthRow, zFactor) - p._m22;
                float viewZ = (dot(depthRow, offset) + p._m23 - deviceDepth * p._m33) / denominator;
                float3 viewPosition = float3(zFactor * viewZ + offset, viewZ);
                float3 worldPosition = mul(UNITY_MATRIX_I_V, float4(viewPosition, 1.0)).xyz;

                uint4 mask = 0u;
                bool loaded = false;
                VRCLVPreviewLoadFineClusterMask(worldPosition, mask, loaded);
                clip(loaded ? 1.0 : -1.0);
                half3 color = any(mask != 0u) ? VRCLVPreviewClusterMaskColor(VRCLVPreviewHashClusterMask(mask)) : 0.0h;
                return half4(color, 1.0h);
            }
            ENDCG
        }
    }
    Fallback Off
}
