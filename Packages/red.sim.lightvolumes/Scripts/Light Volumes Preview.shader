Shader "Hidden/LightVolumesPreview" {

    Properties {
        [HideInInspector] _PreviewZWrite ("Depth Write", Float) = 1
    }

    SubShader {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }

        Pass {
            ZWrite [_PreviewZWrite]
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            #define LVPREVIEW_BINARY_SEARCH_STEPS 12

            UNITY_DECLARE_TEX3D(_PreviewTexture0);
            UNITY_DECLARE_TEX3D(_PreviewTexture1);
            UNITY_DECLARE_TEX3D(_PreviewTexture2);

            float4x4 _PreviewLocalToWorld;
            float4 _PreviewResolution;
            int _PreviewVoxelCount;
            float _PreviewVoxelRadius;
            int _PreviewCardsPerInstance;
            int _PreviewInstancesPerDrawCall;
            int _PreviewDrawCallId;
            int _PreviewHasTextureData;
            float4 _PreviewColor;
            float4 _PreviewCorrection;
            float4 _PreviewRotation;
            int _PreviewIsRotated;
            float4 _PreviewShellOrigin;
            float4 _PreviewCameraVoxel;
            float4 _PreviewCameraRight;
            float4 _PreviewCameraUp;
            float4 _PreviewCameraPosition;

            struct Attributes {
                float4 posOS : POSITION;
                float2 cardData : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 posCS : SV_Position;
                float2 uv : TEXCOORD0;
                float3 centerWS : TEXCOORD1;
                float3 L0 : TEXCOORD2;
                float3 L1r : TEXCOORD3;
                float3 L1g : TEXCOORD4;
                float3 L1b : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Returns the current Unity instance id, or zero when instancing is disabled.
            uint GetInstanceID() {
                #if defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    return unity_InstanceID;
                #else
                    return 0u;
                #endif
            }

            // Returns one float component by dynamic axis index.
            float AxisValue(float3 value, uint axis) {
                if (axis == 0u) return value.x;
                if (axis == 1u) return value.y;
                return value.z;
            }

            // Returns one integer component by dynamic axis index.
            int AxisValueInt(int3 value, uint axis) {
                if (axis == 0u) return value.x;
                if (axis == 1u) return value.y;
                return value.z;
            }

            // Writes one integer component by dynamic axis index.
            void SetAxisValue(inout int3 value, uint axis, int axisValue) {
                if (axis == 0u) value.x = axisValue;
                else if (axis == 1u) value.y = axisValue;
                else value.z = axisValue;
            }

            // Swaps two axis ids.
            void SwapAxes(inout uint a, inout uint b) {
                uint tmp = a;
                a = b;
                b = tmp;
            }

            // Sorts axes from largest absolute camera offset to smallest.
            void SortAxesDescending(float3 values, out uint axis0, out uint axis1, out uint axis2) {
                axis0 = 0u;
                axis1 = 1u;
                axis2 = 2u;

                if (AxisValue(values, axis0) < AxisValue(values, axis1)) SwapAxes(axis0, axis1);
                if (AxisValue(values, axis1) < AxisValue(values, axis2)) SwapAxes(axis1, axis2);
                if (AxisValue(values, axis0) < AxisValue(values, axis1)) SwapAxes(axis0, axis1);
            }

            // Counts an inclusive integer range, returning zero for empty ranges.
            uint RangeCount(int rangeMin, int rangeMax) {
                return (uint)max(rangeMax - rangeMin + 1, 0);
            }

            // Returns the clipped minimum coordinate of one Chebyshev shell axis.
            int ShellMin(int3 origin, int3 boundsMin, uint axis, int radius) {
                return max(AxisValueInt(origin, axis) - radius, AxisValueInt(boundsMin, axis));
            }

            // Returns the clipped maximum coordinate of one Chebyshev shell axis.
            int ShellMax(int3 origin, int3 boundsMax, uint axis, int radius) {
                return min(AxisValueInt(origin, axis) + radius, AxisValueInt(boundsMax, axis));
            }

            // Counts all voxels inside a clipped Chebyshev radius around the shell origin.
            uint ShellVolumeCount(int3 origin, int3 boundsMin, int3 boundsMax, int radius) {
                if (radius < 0) return 0u;

                uint xCount = RangeCount(ShellMin(origin, boundsMin, 0u, radius), ShellMax(origin, boundsMax, 0u, radius));
                uint yCount = RangeCount(ShellMin(origin, boundsMin, 1u, radius), ShellMax(origin, boundsMax, 1u, radius));
                uint zCount = RangeCount(ShellMin(origin, boundsMin, 2u, radius), ShellMax(origin, boundsMax, 2u, radius));
                return xCount * yCount * zCount;
            }

            // Returns the farthest complete shell radius needed to cover the clipped bounds.
            int MaxShellRadius(int3 origin, int3 boundsMin, int3 boundsMax) {
                int3 farCorner = max(origin - boundsMin, boundsMax - origin);
                return max(farCorner.x, max(farCorner.y, farCorner.z));
            }

            // Finds the shell radius containing the requested compact voxel index.
            int FindShellRadius(uint cardId, int3 origin, int3 boundsMin, int3 boundsMax) {
                int low = 0;
                int high = MaxShellRadius(origin, boundsMin, boundsMax);

                [unroll] for (int i = 0; i < LVPREVIEW_BINARY_SEARCH_STEPS; i++) {
                    int mid = (low + high) >> 1;
                    if (cardId < ShellVolumeCount(origin, boundsMin, boundsMax, mid)) high = mid;
                    else low = mid + 1;
                }

                return low;
            }

            // Counts one face of a clipped Chebyshev shell.
            uint ShellFaceCount(uint fixedAxis, int fixedCoord, int3 boundsMin, int3 boundsMax, int range0Min, int range0Max, int range1Min, int range1Max) {
                if (fixedCoord < AxisValueInt(boundsMin, fixedAxis) || fixedCoord > AxisValueInt(boundsMax, fixedAxis)) return 0u;
                return RangeCount(range0Min, range0Max) * RangeCount(range1Min, range1Max);
            }

            // Returns a shell face coordinate, choosing the camera-facing face before the opposite face.
            int ShellFaceCoord(int3 origin, uint axis, int radius, float3 cameraDelta, bool nearFace) {
                int signToCamera = AxisValue(cameraDelta, axis) >= 0.0 ? 1 : -1;
                int direction = nearFace ? signToCamera : -signToCamera;
                return AxisValueInt(origin, axis) + direction * radius;
            }

            // Decodes a 2D face-local index into a 3D voxel coordinate.
            int3 BuildShellFaceCoord(uint fixedAxis, int fixedCoord, uint rangeAxis0, int range0Min, int range0Max, uint rangeAxis1, int range1Min, uint faceIndex) {
                uint range0Count = max(RangeCount(range0Min, range0Max), 1u);
                uint range1Offset = faceIndex / range0Count;
                uint range0Offset = faceIndex - range1Offset * range0Count;
                int3 coord = int3(0, 0, 0);
                SetAxisValue(coord, fixedAxis, fixedCoord);
                SetAxisValue(coord, rangeAxis0, range0Min + (int)range0Offset);
                SetAxisValue(coord, rangeAxis1, range1Min + (int)range1Offset);
                return coord;
            }

            // Tries to consume one shell face from the compact shell index.
            bool TryTakeShellFace(inout uint shellIndex, out int3 coord, uint fixedAxis, int fixedCoord, uint rangeAxis0, int range0Min, int range0Max, uint rangeAxis1, int range1Min, int range1Max, int3 boundsMin, int3 boundsMax) {
                uint faceCount = ShellFaceCount(fixedAxis, fixedCoord, boundsMin, boundsMax, range0Min, range0Max, range1Min, range1Max);
                if (shellIndex >= faceCount) {
                    shellIndex -= faceCount;
                    coord = int3(0, 0, 0);
                    return false;
                }

                coord = BuildShellFaceCoord(fixedAxis, fixedCoord, rangeAxis0, range0Min, range0Max, rangeAxis1, range1Min, shellIndex);
                return true;
            }

            // Converts a compact card id into a near-to-far voxel coordinate by expanding whole shells from the camera.
            float3 DecodeVoxelCoord(uint cardId) {
                int3 resolution = max((int3)round(_PreviewResolution.xyz), int3(1, 1, 1));
                int3 boundsMin = int3(0, 0, 0);
                int3 boundsMax = resolution - int3(1, 1, 1);
                int3 origin = (int3)round(clamp(_PreviewShellOrigin.xyz, (float3)boundsMin, (float3)boundsMax));

                int radius = FindShellRadius(cardId, origin, boundsMin, boundsMax);
                if (radius <= 0) return (float3)origin;

                uint shellIndex = cardId - ShellVolumeCount(origin, boundsMin, boundsMax, radius - 1);
                float3 cameraDelta = _PreviewCameraVoxel.xyz - (float3)origin;

                uint axis0;
                uint axis1;
                uint axis2;
                SortAxesDescending(abs(cameraDelta), axis0, axis1, axis2);

                int axis0Min = ShellMin(origin, boundsMin, axis0, radius);
                int axis0Max = ShellMax(origin, boundsMax, axis0, radius);
                int axis1Min = ShellMin(origin, boundsMin, axis1, radius);
                int axis1Max = ShellMax(origin, boundsMax, axis1, radius);
                int axis2Min = ShellMin(origin, boundsMin, axis2, radius);
                int axis2Max = ShellMax(origin, boundsMax, axis2, radius);
                int axis0InnerMin = ShellMin(origin, boundsMin, axis0, radius - 1);
                int axis0InnerMax = ShellMax(origin, boundsMax, axis0, radius - 1);
                int axis1InnerMin = ShellMin(origin, boundsMin, axis1, radius - 1);
                int axis1InnerMax = ShellMax(origin, boundsMax, axis1, radius - 1);

                int3 coord;
                if (TryTakeShellFace(shellIndex, coord, axis0, ShellFaceCoord(origin, axis0, radius, cameraDelta, true), axis1, axis1Min, axis1Max, axis2, axis2Min, axis2Max, boundsMin, boundsMax)) return (float3)coord;
                if (TryTakeShellFace(shellIndex, coord, axis0, ShellFaceCoord(origin, axis0, radius, cameraDelta, false), axis1, axis1Min, axis1Max, axis2, axis2Min, axis2Max, boundsMin, boundsMax)) return (float3)coord;
                if (TryTakeShellFace(shellIndex, coord, axis1, ShellFaceCoord(origin, axis1, radius, cameraDelta, true), axis0, axis0InnerMin, axis0InnerMax, axis2, axis2Min, axis2Max, boundsMin, boundsMax)) return (float3)coord;
                if (TryTakeShellFace(shellIndex, coord, axis1, ShellFaceCoord(origin, axis1, radius, cameraDelta, false), axis0, axis0InnerMin, axis0InnerMax, axis2, axis2Min, axis2Max, boundsMin, boundsMax)) return (float3)coord;
                if (TryTakeShellFace(shellIndex, coord, axis2, ShellFaceCoord(origin, axis2, radius, cameraDelta, true), axis0, axis0InnerMin, axis0InnerMax, axis1, axis1InnerMin, axis1InnerMax, boundsMin, boundsMax)) return (float3)coord;
                if (TryTakeShellFace(shellIndex, coord, axis2, ShellFaceCoord(origin, axis2, radius, cameraDelta, false), axis0, axis0InnerMin, axis0InnerMax, axis1, axis1InnerMin, axis1InnerMax, boundsMin, boundsMax)) return (float3)coord;
                return (float3)origin;
            }

            // Rotates a vector by a quaternion.
            float3 MultiplyVectorByQuaternion(float3 v, float4 q) {
                float3 t = 2.0 * cross(q.xyz, v);
                return v + q.w * t + cross(q.xyz, t);
            }

            // Evaluates first-order spherical harmonics.
            float EvaluateSH(float L0, float3 L1, float3 normalWS) {
                return L0 + dot(L1, normalWS);
            }

            // Applies the same first-order SH dering used by atlas generation.
            float3 DeringSingleSH(float L0, float3 L1) {
                L1 *= 0.5;
                float L1Length = length(L1);
                if (L1Length > 1e-6 && L0 > 0.0) L1 *= min(L0 / L1Length, 1.13);
                return L1;
            }

            // Applies the same vector color correction used by atlas generation.
            float3 CorrectVector(float3 value) {
                float valueLength = length(value);
                if (valueLength <= 1e-6) return float3(0.0, 0.0, 0.0);

                float range = max(_PreviewCorrection.y - _PreviewCorrection.x, 1e-6);
                float correctedLength = max(((valueLength - _PreviewCorrection.x) / range) * _PreviewCorrection.z, 0.0);
                return value * (correctedLength / valueLength);
            }

            // Samples the three Light Volume textures at voxel center UVW.
            void SamplePreviewSH(float3 uvw, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b) {
                if (_PreviewHasTextureData == 0) {
                    L0 = float3(1.0, 1.0, 1.0);
                    L1r = float3(0.0, 0.0, 0.0);
                    L1g = float3(0.0, 0.0, 0.0);
                    L1b = float3(0.0, 0.0, 0.0);
                    return;
                }

                float4 tex0 = UNITY_SAMPLE_TEX3D_LOD(_PreviewTexture0, uvw, 0);
                float4 tex1 = UNITY_SAMPLE_TEX3D_LOD(_PreviewTexture1, uvw, 0);
                float4 tex2 = UNITY_SAMPLE_TEX3D_LOD(_PreviewTexture2, uvw, 0);

                float3 l0 = tex0.rgb;
                float3 l1r = float3(tex1.r, tex2.r, tex0.a);
                float3 l1g = float3(tex1.g, tex2.g, tex1.a);
                float3 l1b = float3(tex1.b, tex2.b, tex2.a);

                l1r = DeringSingleSH(l0.r, l1r);
                l1g = DeringSingleSH(l0.g, l1g);
                l1b = DeringSingleSH(l0.b, l1b);

                l0 = CorrectVector(l0);
                l1r = CorrectVector(l1r);
                l1g = CorrectVector(l1g);
                l1b = CorrectVector(l1b);

                L0 = l0 * _PreviewColor.rgb;
                L1r = l1r * _PreviewColor.r;
                L1g = l1g * _PreviewColor.g;
                L1b = l1b * _PreviewColor.b;

                if (_PreviewIsRotated != 0) {
                    L1r = MultiplyVectorByQuaternion(L1r, _PreviewRotation);
                    L1g = MultiplyVectorByQuaternion(L1g, _PreviewRotation);
                    L1b = MultiplyVectorByQuaternion(L1b, _PreviewRotation);
                }
            }

            Varyings vert(Attributes v) {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(Varyings, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                uint meshCardId = (uint)floor(v.cardData.x + 0.5);
                uint drawInstanceId = (uint)max(_PreviewDrawCallId, 0) * (uint)max(_PreviewInstancesPerDrawCall, 1) + GetInstanceID();
                uint cardId = drawInstanceId * (uint)max(_PreviewCardsPerInstance, 1) + meshCardId;
                uint voxelCount = (uint)max(_PreviewVoxelCount, 0);

                if (cardId >= voxelCount) {
                    o.posCS = float4(0.0, 0.0, 0.0, -1.0);
                    return o;
                }

                // Reverse the shell order for transparent cards.
                if (_PreviewColor.a < 1.0) cardId = voxelCount - 1u - cardId;

                float3 voxelCoord = DecodeVoxelCoord(cardId);
                float3 resolution = max(_PreviewResolution.xyz, float3(1.0, 1.0, 1.0));
                float3 uvw = (voxelCoord + 0.5) / resolution;
                float3 localPos = uvw - 0.5;
                float3 centerWS = mul(_PreviewLocalToWorld, float4(localPos, 1.0)).xyz;

                float2 disc = v.posOS.xy;
                float2 uv = disc * 0.5 + 0.5;
                float3 cameraRightWS = normalize(_PreviewCameraRight.xyz);
                float3 cameraUpWS = normalize(_PreviewCameraUp.xyz);
                float3 posWS = centerWS + (cameraRightWS * disc.x + cameraUpWS * disc.y) * _PreviewVoxelRadius;

                o.posCS = UnityObjectToClipPos(float4(posWS, 1.0));
                o.uv = uv;
                o.centerWS = centerWS;
                SamplePreviewSH(uvw, o.L0, o.L1r, o.L1g, o.L1b);
                return o;
            }

            half4 frag(Varyings i) : SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 disc = i.uv * 2.0 - 1.0;
                float radiusSq = dot(disc, disc);
                clip(1.0 - radiusSq);

                half sphereZ = (half)sqrt(saturate(1.0 - radiusSq));
                half3 cameraRightWS = (half3)normalize(_PreviewCameraRight.xyz);
                half3 cameraUpWS = (half3)normalize(_PreviewCameraUp.xyz);
                half3 viewDirWS = (half3)normalize(_PreviewCameraPosition.xyz - i.centerWS);
                half3 normalWS = normalize(cameraRightWS * (half)disc.x + cameraUpWS * (half)disc.y + viewDirWS * sphereZ);

                // Unbaked volumes are shown as neutral shaded cards.
                if (_PreviewHasTextureData == 0) {
                    half shade = 0.28 + 0.72 * sphereZ;
                    return half4((half3)i.L0 * shade, _PreviewColor.a);
                }

                // Baked volumes show evaluated SH only, without editor ambient or fill light.
                float3 normalWSFloat = (float3)normalWS;
                float3 color = float3(EvaluateSH(i.L0.r, i.L1r, normalWSFloat), EvaluateSH(i.L0.g, i.L1g, normalWSFloat), EvaluateSH(i.L0.b, i.L1b, normalWSFloat));
                return half4((half3)max(color, float3(0.0, 0.0, 0.0)), _PreviewColor.a);
            }
            ENDCG
        }
    }
}
