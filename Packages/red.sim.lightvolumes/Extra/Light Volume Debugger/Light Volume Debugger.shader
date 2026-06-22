Shader "Light Volume Samples/Light Volume Debugger" {
    Properties {
        [Header(Rendering)]
        [Enum(Selected Volume,0,All Volume Bounds,1,All Lights,2)] _DebugDrawMode("Draw Mode", Int) = 0
        _DebugMeshCardCount("Mesh Card Count", Float) = 16383

        [Header(Light Volumes)]
        [IntRange] _DebugVolumeID("Volume ID", Range(0, 31)) = 0
        [Toggle] _DebugAutoVolumeID("Auto Volume ID", Float) = 0
        _DebugSphereRadius("Sphere size", Range(0, 1)) = 0.7
        _DebugVisibleFraction("Draw Amount", Range(0, 1)) = 1
        _DebugBoundsThickness("Bounds Thickness", Float) = 0.02
        _DebugRegularBoundsColor("Regular Bounds Color", Color) = (0, 1, 1, 1)
        _DebugAdditiveBoundsColor("Additive Bounds Color", Color) = (1, 0.5, 0, 1)

        [Header(Point Light Volumes)]
        [NoScaleOffset] _DebugLightIcon("Light Icon", 2D) = "white" {}
        _DebugLightIconSize("Light Icon Size", Float) = 0.25
        _DebugLightIconCutoff("Light Icon Cutout", Range(0, 1)) = 0.5
        _DebugAreaLightRectColor("Area Light Rect Color", Color) = (1, 1, 0, 1)
    }

    SubShader {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" "DisableBatching" = "True" }

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"

        #define LVDEBUG_BINARY_SEARCH_STEPS 12
        #define LVDEBUG_BOUNDS_EDGE_COUNT 12u
        #define LVDEBUG_LIGHT_CARD_STRIDE 5u
        #define LVDEBUG_INVALID_POSITION float4(0.0, 0.0, 0.0, -1.0)
        #define LVDEBUG_MODE_SELECTED_VOLUME 0
        #define LVDEBUG_MODE_ALL_VOLUME_BOUNDS 1
        #define LVDEBUG_MODE_ALL_LIGHTS 2
        #define LVDEBUG_LIGHT_TYPE_POINT 0u
        #define LVDEBUG_LIGHT_TYPE_SPOT 1u
        #define LVDEBUG_LIGHT_TYPE_AREA 2u
        #define LVDEBUG_OVERLAY_LINE 0.0
        #define LVDEBUG_OVERLAY_ICON 1.0

        int _DebugDrawMode;
        float _DebugVolumeID;
        float _DebugAutoVolumeID;
        float _DebugSphereRadius;
        float _DebugVisibleFraction;
        float _DebugMeshCardCount;
        float _DebugBoundsThickness;
        float _DebugLightIconSize;
        float _DebugLightIconCutoff;
        float4 _DebugRegularBoundsColor;
        float4 _DebugAdditiveBoundsColor;
        float4 _DebugAreaLightRectColor;
        sampler2D _DebugLightIcon;
        uniform float3 _UdonLightVolumeUvw[VRCLV_MAX_VOLUMES_COUNT * 6];

        // Returns true when the active Light Volumes data uses the legacy v1 atlas layout.
        bool LVDebugUseLegacyVolumeLayout() {
            return _UdonLightVolumeVersion < VRCLV_MIN_SUPPORTED_VERSION;
        }

        // Inverts a 3x3 matrix stored as rows.
        float3x3 LVDebugInverse3x3(float3x3 m) {
            float3 row0 = m[0];
            float3 row1 = m[1];
            float3 row2 = m[2];
            float3 invRow0 = cross(row1, row2);
            float3 invRow1 = cross(row2, row0);
            float3 invRow2 = cross(row0, row1);
            float determinant = dot(row0, invRow0);
            float invDeterminant = rcp(max(abs(determinant), 1e-8)) * (determinant < 0.0 ? -1.0 : 1.0);
            return transpose(float3x3(invRow0, invRow1, invRow2)) * invDeterminant;
        }

        // Extracts the linear world-to-volume matrix from the runtime Light Volume transform.
        float3x3 LVDebugVolumeWorldToLocal3x3(float4x4 worldToLocal) {
            return float3x3(
                worldToLocal._m00_m01_m02,
                worldToLocal._m10_m11_m12,
                worldToLocal._m20_m21_m22
            );
        }

        // Converts a volume-local point back into world space using a precomputed inverse matrix.
        float3 LVDebugVolumeLocalToWorld(float3 localUVW, float3x3 localToWorld3x3, float3 worldToLocalOffset) {
            return mul(localToWorld3x3, localUVW - worldToLocalOffset);
        }

        // Returns a normalized vector and falls back when the source vector is too small.
        float3 LVDebugSafeNormalize(float3 value, float3 fallback) {
            float lengthSq = dot(value, value);
            return lengthSq > 1e-8 ? value * rsqrt(lengthSq) : fallback;
        }

        // Calculates atlas UVW coordinates for legacy v1 volume data.
        void LVDebugLegacyVolumeAtlasUVW(uint volumeID, float3 localUVW, out float3 uvw0, out float3 uvw1, out float3 uvw2) {
            uint uvwID = volumeID * 6u;
            float3 uvwScaled = saturate(localUVW + 0.5) * (_UdonLightVolumeUvw[uvwID + 1u].xyz - _UdonLightVolumeUvw[uvwID].xyz);
            uvw0 = _UdonLightVolumeUvw[uvwID].xyz + uvwScaled;
            uvw1 = _UdonLightVolumeUvw[uvwID + 2u].xyz + uvwScaled;
            uvw2 = _UdonLightVolumeUvw[uvwID + 4u].xyz + uvwScaled;
        }

        // Samples only the selected legacy v1 baked volume, excluding point lights and shadow volumes.
        void LVDebugSampleLegacyVolume(uint volumeID, float3 localUVW, inout float3 L0, inout float3 L1r, inout float3 L1g, inout float3 L1b) {
            float3 uvw0;
            float3 uvw1;
            float3 uvw2;
            LVDebugLegacyVolumeAtlasUVW(volumeID, localUVW, uvw0, uvw1, uvw2);

            float3 l0;
            float3 l1r;
            float3 l1g;
            float3 l1b;
            LV_SampleLightVolumeTex(uvw0, uvw1, uvw2, l0, l1r, l1g, l1b);

            float4 color = _UdonLightVolumeColor[volumeID];
            L0 += l0 * color.rgb;
            l1r *= color.r;
            l1g *= color.g;
            l1b *= color.b;

            if (color.a != 0.0) {
                uint rotationID = volumeID * 2u;
                float3 r0 = _UdonLightVolumeRotation[rotationID].xyz;
                float3 r1 = _UdonLightVolumeRotation[rotationID + 1u].xyz;
                float3 r2 = cross(r0, r1);
                L1r += LV_MultiplyVectorByMatrix3x3(l1r, r0, r1, r2);
                L1g += LV_MultiplyVectorByMatrix3x3(l1g, r0, r1, r2);
                L1b += LV_MultiplyVectorByMatrix3x3(l1b, r0, r1, r2);
            } else {
                L1r += l1r;
                L1g += l1g;
                L1b += l1b;
            }
        }

        // Clamps the material draw mode to a valid enum value.
        int LVDebugResolveDrawMode() {
            if (_DebugDrawMode < LVDEBUG_MODE_SELECTED_VOLUME) return LVDEBUG_MODE_SELECTED_VOLUME;
            if (_DebugDrawMode > LVDEBUG_MODE_ALL_LIGHTS) return LVDEBUG_MODE_ALL_LIGHTS;
            return _DebugDrawMode;
        }

        // Clamps the manual material volume ID to the available runtime volume range.
        uint LVDebugClampVolumeID(float volumeID, uint volumeCount) {
            if (volumeCount == 0u) return 0u;

            float maxVolumeID = (float)(volumeCount - 1u);
            return (uint)floor(clamp(volumeID, 0.0, maxVolumeID) + 0.5);
        }

        // Returns true when a world-space point is inside the selected Light Volume bounds.
        bool LVDebugIsPointInsideVolume(uint volumeID, float3 worldPos) {
            float3 localUVW = LV_LocalFromVolume(volumeID, worldPos);
            return LV_PointLocalAABB(localUVW);
        }

        // Resolves the manually selected volume or the highest-weight volume containing the camera.
        uint LVDebugResolveVolumeID(uint volumeCount) {
            uint manualID = LVDebugClampVolumeID(_DebugVolumeID, volumeCount);
            if (volumeCount == 0u || _DebugAutoVolumeID <= 0.5) return manualID;

            uint additiveCount = min((uint)_UdonLightVolumeAdditiveCount, volumeCount);
            float3 cameraPosition = _WorldSpaceCameraPos;

            [loop] for (uint id = additiveCount; id < volumeCount; id++) {
                if (LVDebugIsPointInsideVolume(id, cameraPosition)) return id;
            }

            [loop] for (uint additiveID = 0u; additiveID < additiveCount; additiveID++) {
                if (LVDebugIsPointInsideVolume(additiveID, cameraPosition)) return additiveID;
            }

            return manualID;
        }
        ENDCG

        Pass {
            ZWrite On
            ZTest LEqual
            Cull Off
            Blend Off

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex VolumeVert
            #pragma fragment VolumeFrag
            #pragma multi_compile_instancing

            struct VolumeAttributes {
                float4 posOS : POSITION;
                float2 cardData : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VolumeVaryings {
                float4 posCS : SV_Position;
                float2 uv : TEXCOORD0;
                float3 centerWS : TEXCOORD1;
                float3 L0 : TEXCOORD2;
                float3 L1r : TEXCOORD3;
                float3 L1g : TEXCOORD4;
                float3 L1b : TEXCOORD5;
                half valid : TEXCOORD6;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Marks a vertex as discarded without relying only on clip in the fragment stage.
            void DisableVolumeVertex(inout VolumeVaryings o) {
                o.posCS = LVDEBUG_INVALID_POSITION;
                o.valid = 0.0;
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

            // Sorts axes from largest absolute value to smallest.
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

            // Counts voxels inside an integer AABB.
            uint BoundsVoxelCount(int3 boundsMin, int3 boundsMax) {
                return RangeCount(boundsMin.x, boundsMax.x) * RangeCount(boundsMin.y, boundsMax.y) * RangeCount(boundsMin.z, boundsMax.z);
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

                [unroll] for (int i = 0; i < LVDEBUG_BINARY_SEARCH_STEPS; i++) {
                    int mid = (low + high) >> 1;
                    if (cardId < ShellVolumeCount(origin, boundsMin, boundsMax, mid)) high = mid;
                    else low = mid + 1;
                }

                return low;
            }

            // Finds the largest complete shell radius that fits the available card budget.
            int FindBudgetShellRadius(uint cardBudget, int3 origin, int3 boundsMin, int3 boundsMax) {
                if (cardBudget == 0u) return -1;

                int low = 0;
                int high = MaxShellRadius(origin, boundsMin, boundsMax);

                [unroll] for (int i = 0; i < LVDEBUG_BINARY_SEARCH_STEPS; i++) {
                    int mid = (low + high + 1) >> 1;
                    if (ShellVolumeCount(origin, boundsMin, boundsMax, mid) <= cardBudget) low = mid;
                    else high = mid - 1;
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

            // Clamps the camera position to drawable voxel bounds and uses it as the shell center.
            int3 GetShellOrigin(float3 cameraVoxel, int3 boundsMin, int3 boundsMax) {
                return (int3)round(clamp(cameraVoxel, (float3)boundsMin, (float3)boundsMax));
            }

            // Converts a 0..1 visible fraction into a whole-shell voxel count.
            uint GetVisibleShellVoxelCount(float visibleFraction, uint cardBudget, int3 origin, int3 boundsMin, int3 boundsMax) {
                float fraction = saturate(visibleFraction);
                if (fraction <= 0.0) return 0u;

                int budgetRadius = FindBudgetShellRadius(cardBudget, origin, boundsMin, boundsMax);
                int visibleShellCount = (int)ceil((float)(budgetRadius + 1) * fraction);
                if (visibleShellCount <= 0) return 0u;
                return ShellVolumeCount(origin, boundsMin, boundsMax, visibleShellCount - 1);
            }

            // Converts a compact card id into a bounded voxel coordinate by expanding whole shells.
            int3 DecodeShellVoxel(uint cardId, int3 origin, int3 boundsMin, int3 boundsMax, float3 cameraVoxel) {
                int radius = FindShellRadius(cardId, origin, boundsMin, boundsMax);
                if (radius <= 0) return origin;

                uint shellIndex = cardId - ShellVolumeCount(origin, boundsMin, boundsMax, radius - 1);
                float3 cameraDelta = cameraVoxel - (float3)origin;

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
                if (TryTakeShellFace(shellIndex, coord, axis0, ShellFaceCoord(origin, axis0, radius, cameraDelta, true), axis1, axis1Min, axis1Max, axis2, axis2Min, axis2Max, boundsMin, boundsMax)) return coord;
                if (TryTakeShellFace(shellIndex, coord, axis0, ShellFaceCoord(origin, axis0, radius, cameraDelta, false), axis1, axis1Min, axis1Max, axis2, axis2Min, axis2Max, boundsMin, boundsMax)) return coord;
                if (TryTakeShellFace(shellIndex, coord, axis1, ShellFaceCoord(origin, axis1, radius, cameraDelta, true), axis0, axis0InnerMin, axis0InnerMax, axis2, axis2Min, axis2Max, boundsMin, boundsMax)) return coord;
                if (TryTakeShellFace(shellIndex, coord, axis1, ShellFaceCoord(origin, axis1, radius, cameraDelta, false), axis0, axis0InnerMin, axis0InnerMax, axis2, axis2Min, axis2Max, boundsMin, boundsMax)) return coord;
                if (TryTakeShellFace(shellIndex, coord, axis2, ShellFaceCoord(origin, axis2, radius, cameraDelta, true), axis0, axis0InnerMin, axis0InnerMax, axis1, axis1InnerMin, axis1InnerMax, boundsMin, boundsMax)) return coord;
                if (TryTakeShellFace(shellIndex, coord, axis2, ShellFaceCoord(origin, axis2, radius, cameraDelta, false), axis0, axis0InnerMin, axis0InnerMax, axis1, axis1InnerMin, axis1InnerMax, boundsMin, boundsMax)) return coord;
                return origin;
            }

            // Calculates a normalized world-space sphere radius where 1.0 touches neighbors in a uniform voxel grid.
            float GetSphereRadiusWS(float3x3 localToWorld3x3, int3 resolution) {
                float3 invResolution = rcp(max((float3)resolution, float3(1.0, 1.0, 1.0)));
                float xSize = length(mul(localToWorld3x3, float3(invResolution.x, 0.0, 0.0)));
                float ySize = length(mul(localToWorld3x3, float3(0.0, invResolution.y, 0.0)));
                float zSize = length(mul(localToWorld3x3, float3(0.0, 0.0, invResolution.z)));
                return min(xSize, min(ySize, zSize)) * 0.5 * saturate(_DebugSphereRadius);
            }

            // Samples only the selected baked volume, excluding light probes and point light volumes.
            void SampleDebugVolume(uint volumeID, float3 localUVW, out float3 L0, out float3 L1r, out float3 L1g, out float3 L1b) {
                L0 = 0.0;
                L1r = 0.0;
                L1g = 0.0;
                L1b = 0.0;
                if (LVDebugUseLegacyVolumeLayout()) LVDebugSampleLegacyVolume(volumeID, localUVW, L0, L1r, L1g, L1b);
                else LV_SampleVolume(volumeID, localUVW, L0, L1r, L1g, L1b);
            }

            // Reconstructs baked voxel resolution from the global atlas size and this volume's atlas island scale.
            int3 GetVolumeResolution(uint volumeID) {
                uint atlasWidth = 1u;
                uint atlasHeight = 1u;
                uint atlasDepth = 1u;
                _UdonLightVolume.GetDimensions(atlasWidth, atlasHeight, atlasDepth);

                float3 islandScale = float3(1.0, 1.0, 1.0);
                if (LVDebugUseLegacyVolumeLayout()) {
                    uint uvwID = volumeID * 6u;
                    islandScale = _UdonLightVolumeUvw[uvwID + 1u].xyz - _UdonLightVolumeUvw[uvwID].xyz;
                } else {
                    uint uvwID = volumeID * 3u;
                    islandScale = float3(_UdonLightVolumeUvwScale[uvwID].w, _UdonLightVolumeUvwScale[uvwID + 1u].w, _UdonLightVolumeUvwScale[uvwID + 2u].w);
                }

                float3 atlasResolution = float3((float)atlasWidth, (float)atlasHeight, (float)atlasDepth);
                return (int3)max(round(abs(islandScale) * atlasResolution), float3(1.0, 1.0, 1.0));
            }

            // Places each generated mesh card on a near-to-far voxel center of the selected runtime Light Volume.
            VolumeVaryings VolumeVert(VolumeAttributes v) {
                VolumeVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(VolumeVaryings, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                DisableVolumeVertex(o);

                if (LVDebugResolveDrawMode() != LVDEBUG_MODE_SELECTED_VOLUME) return o;
                if (_DebugSphereRadius <= 0.0 || _DebugVisibleFraction <= 0.0) return o;

                uint volumeCount = min((uint)_UdonLightVolumeCount, (uint)VRCLV_MAX_VOLUMES_COUNT);
                if (_UdonLightVolumeEnabled <= 0.0 || volumeCount == 0u) return o;

                uint volumeID = LVDebugResolveVolumeID(volumeCount);
                if (volumeID >= volumeCount) return o;

                uint cardId = (uint)floor(v.cardData.x + 0.5);
                uint meshCardCount = (uint)floor(max(max(v.cardData.y, _DebugMeshCardCount), 1.0) + 0.5);

                int3 resolution = GetVolumeResolution(volumeID);
                float4x4 worldToLocal = _UdonLightVolumeInvWorldMatrix[volumeID];
                float3x3 worldToLocal3x3 = LVDebugVolumeWorldToLocal3x3(worldToLocal);
                float3 localCamera = mul(worldToLocal, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                float3 cameraVoxel = (localCamera + 0.5) * (float3)resolution - 0.5;

                int3 boundsMin = int3(0, 0, 0);
                int3 boundsMax = resolution - int3(1, 1, 1);
                uint volumeVoxelCount = BoundsVoxelCount(boundsMin, boundsMax);
                int3 origin = GetShellOrigin(cameraVoxel, boundsMin, boundsMax);
                uint visibleCount = GetVisibleShellVoxelCount(_DebugVisibleFraction, min(volumeVoxelCount, meshCardCount), origin, boundsMin, boundsMax);
                if (cardId >= visibleCount) return o;

                float3 voxelCoord = (float3)DecodeShellVoxel(cardId, origin, boundsMin, boundsMax, cameraVoxel);
                float3 localUVW = (voxelCoord + 0.5) / (float3)resolution - 0.5;

                float3x3 localToWorld3x3 = LVDebugInverse3x3(worldToLocal3x3);
                float3 worldToLocalOffset = float3(worldToLocal._m03, worldToLocal._m13, worldToLocal._m23);
                float sphereRadiusWS = GetSphereRadiusWS(localToWorld3x3, resolution);
                float3 centerWS = LVDebugVolumeLocalToWorld(localUVW, localToWorld3x3, worldToLocalOffset);
                if (sphereRadiusWS <= 0.0 || mul(UNITY_MATRIX_V, float4(centerWS, 1.0)).z > sphereRadiusWS) return o;

                float2 disc = v.posOS.xy;
                float3 cameraRightWS = unity_CameraToWorld._m00_m10_m20;
                float3 cameraUpWS = unity_CameraToWorld._m01_m11_m21;
                float3 posWS = centerWS + (cameraRightWS * disc.x + cameraUpWS * disc.y) * sphereRadiusWS;

                o.posCS = UnityWorldToClipPos(posWS);
                o.uv = disc * 0.5 + 0.5;
                o.centerWS = centerWS;
                o.valid = 1.0;
                SampleDebugVolume(volumeID, localUVW, o.L0, o.L1r, o.L1g, o.L1b);
                return o;
            }

            // Cuts the card into a sphere impostor and evaluates the selected volume SH at its voxel center.
            half4 VolumeFrag(VolumeVaryings i) : SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(i.valid - 0.5);

                float2 disc = i.uv * 2.0 - 1.0;
                float radiusSq = dot(disc, disc);
                clip(1.0 - radiusSq);

                half sphereZ = (half)sqrt(saturate(1.0 - radiusSq));
                half3 cameraRightWS = (half3)unity_CameraToWorld._m00_m10_m20;
                half3 cameraUpWS = (half3)unity_CameraToWorld._m01_m11_m21;
                half3 viewDirWS = (half3)normalize(UnityWorldSpaceViewDir(i.centerWS));
                half3 normalWS = normalize(cameraRightWS * (half)disc.x + cameraUpWS * (half)disc.y + viewDirWS * sphereZ);

                float3 color = LightVolumeEvaluate((float3)normalWS, i.L0, i.L1r, i.L1g, i.L1b);
                return half4((half3)max(color, float3(0.0, 0.0, 0.0)), 1.0);
            }
            ENDCG
        }

        Pass {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex OverlayVert
            #pragma fragment OverlayFrag
            #pragma multi_compile_instancing

            struct OverlayAttributes {
                float4 posOS : POSITION;
                float2 cardData : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct OverlayVaryings {
                float4 posCS : SV_Position;
                float2 uv : TEXCOORD0;
                half valid : TEXCOORD1;
                half4 color : TEXCOORD2;
                half overlayType : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Marks an overlay vertex as discarded.
            void DisableOverlayVertex(inout OverlayVaryings o) {
                o.posCS = LVDEBUG_INVALID_POSITION;
                o.uv = 0.0;
                o.valid = 0.0;
                o.color = half4(0.0, 0.0, 0.0, 0.0);
                o.overlayType = LVDEBUG_OVERLAY_LINE;
            }

            // Returns one of the twelve local cuboid edges in -0.5..0.5 volume space.
            void GetBoundsEdgeLocal(uint edgeID, out float3 edgeStart, out float3 edgeEnd) {
                uint edgeAxis = edgeID < 4u ? 0u : (edgeID < 8u ? 1u : 2u);
                uint edgeInAxis = edgeID - edgeAxis * 4u;
                float fixed0 = (edgeInAxis == 0u || edgeInAxis == 2u) ? -0.5 : 0.5;
                float fixed1 = edgeInAxis < 2u ? -0.5 : 0.5;

                if (edgeAxis == 0u) {
                    edgeStart = float3(-0.5, fixed0, fixed1);
                    edgeEnd = float3(0.5, fixed0, fixed1);
                } else if (edgeAxis == 1u) {
                    edgeStart = float3(fixed0, -0.5, fixed1);
                    edgeEnd = float3(fixed0, 0.5, fixed1);
                } else {
                    edgeStart = float3(fixed0, fixed1, -0.5);
                    edgeEnd = float3(fixed0, fixed1, 0.5);
                }
            }

            // Classifies one packed point light volume entry.
            uint GetPackedLightType(uint lightID) {
                float positionW = _UdonPointLightVolumePosition[lightID].w;
                float colorW = _UdonPointLightVolumeColor[lightID].w;
                if (positionW < 0.0) return LVDEBUG_LIGHT_TYPE_SPOT;
                if (colorW <= 1.5) return LVDEBUG_LIGHT_TYPE_POINT;
                return LVDEBUG_LIGHT_TYPE_AREA;
            }

            // Maps a fixed-stride all-lights card id to one packed light and one draw part.
            bool TryResolveLightCard(uint cardID, out uint lightID, out uint lightType, out uint lightPart) {
                lightID = cardID / LVDEBUG_LIGHT_CARD_STRIDE;
                lightPart = cardID - lightID * LVDEBUG_LIGHT_CARD_STRIDE;
                if (LVDebugUseLegacyVolumeLayout()) {
                    lightType = LVDEBUG_LIGHT_TYPE_POINT;
                    return false;
                }

                uint lightCount = min((uint)_UdonPointLightVolumeCount, (uint)VRCLV_MAX_LIGHTS_COUNT);
                if (lightID >= lightCount) {
                    lightType = LVDEBUG_LIGHT_TYPE_POINT;
                    return false;
                }

                lightType = GetPackedLightType(lightID);
                return lightPart == 0u || lightType == LVDEBUG_LIGHT_TYPE_AREA;
            }

            // Expands a camera-facing quad centered in world space.
            float3 BuildBillboardQuadPosition(float3 centerWS, float2 quad) {
                float halfSize = max(_DebugLightIconSize, 0.0) * 0.5;
                float3 cameraRightWS = unity_CameraToWorld._m00_m10_m20;
                float3 cameraUpWS = unity_CameraToWorld._m01_m11_m21;
                return centerWS + (cameraRightWS * quad.x + cameraUpWS * quad.y) * halfSize;
            }

            // Expands a thick camera-facing line segment in world space.
            float3 BuildBillboardLinePosition(float3 edgeStartWS, float3 edgeEndWS, float2 quad) {
                float3 edgeVectorWS = edgeEndWS - edgeStartWS;
                float edgeLength = max(length(edgeVectorWS), 1e-6);
                float3 edgeDirectionWS = edgeVectorWS / edgeLength;
                float3 edgeCenterWS = (edgeStartWS + edgeEndWS) * 0.5;

                float3 cameraRightWS = unity_CameraToWorld._m00_m10_m20;
                float3 cameraUpWS = unity_CameraToWorld._m01_m11_m21;
                float3 viewDirectionWS = LVDebugSafeNormalize(_WorldSpaceCameraPos - edgeCenterWS, -unity_CameraToWorld._m02_m12_m22);
                float3 sideDirectionWS = cross(viewDirectionWS, edgeDirectionWS);
                float sideLengthSq = dot(sideDirectionWS, sideDirectionWS);
                if (sideLengthSq > 1e-8) {
                    sideDirectionWS *= rsqrt(sideLengthSq);
                } else {
                    sideDirectionWS = LVDebugSafeNormalize(cross(cameraUpWS, edgeDirectionWS), cameraRightWS);
                }

                float halfThickness = max(_DebugBoundsThickness, 0.0) * 0.5;
                float halfLength = edgeLength * 0.5 + halfThickness;
                return edgeCenterWS + edgeDirectionWS * quad.x * halfLength + sideDirectionWS * quad.y * halfThickness;
            }

            // Builds one selected-volume or all-volume bounds edge.
            bool TryBuildVolumeBoundsCard(uint cardID, float2 quad, int drawMode, out float3 posWS, out half4 color) {
                posWS = 0.0;
                color = 0.0;
                if (_DebugBoundsThickness <= 0.0) return false;

                uint volumeCount = min((uint)_UdonLightVolumeCount, (uint)VRCLV_MAX_VOLUMES_COUNT);
                if (_UdonLightVolumeEnabled <= 0.0 || volumeCount == 0u) return false;

                uint volumeID = 0u;
                uint edgeID = cardID;
                if (drawMode == LVDEBUG_MODE_ALL_VOLUME_BOUNDS) {
                    volumeID = cardID / LVDEBUG_BOUNDS_EDGE_COUNT;
                    edgeID = cardID - volumeID * LVDEBUG_BOUNDS_EDGE_COUNT;
                } else {
                    volumeID = LVDebugResolveVolumeID(volumeCount);
                }

                if (volumeID >= volumeCount || edgeID >= LVDEBUG_BOUNDS_EDGE_COUNT) return false;

                uint additiveCount = min((uint)_UdonLightVolumeAdditiveCount, volumeCount);
                color = (half4)(volumeID < additiveCount ? _DebugAdditiveBoundsColor : _DebugRegularBoundsColor);
                if (color.a <= 0.0) return false;

                float4x4 worldToLocal = _UdonLightVolumeInvWorldMatrix[volumeID];
                float3x3 worldToLocal3x3 = LVDebugVolumeWorldToLocal3x3(worldToLocal);
                float3x3 localToWorld3x3 = LVDebugInverse3x3(worldToLocal3x3);
                float3 worldToLocalOffset = float3(worldToLocal._m03, worldToLocal._m13, worldToLocal._m23);

                float3 edgeStartLocal;
                float3 edgeEndLocal;
                GetBoundsEdgeLocal(edgeID, edgeStartLocal, edgeEndLocal);

                float3 edgeStartWS = LVDebugVolumeLocalToWorld(edgeStartLocal, localToWorld3x3, worldToLocalOffset);
                float3 edgeEndWS = LVDebugVolumeLocalToWorld(edgeEndLocal, localToWorld3x3, worldToLocalOffset);
                posWS = BuildBillboardLinePosition(edgeStartWS, edgeEndWS, quad);
                return true;
            }

            // Returns one rectangle edge for an area light surface.
            void GetAreaLightRectEdge(uint edgeID, float3 centerWS, float4 rotation, float2 size, out float3 edgeStartWS, out float3 edgeEndWS) {
                float3 xAxis = float3(1.0, 0.0, 0.0);
                float3 yAxis = float3(0.0, 1.0, 0.0);
                float3 normal = float3(0.0, 0.0, 1.0);
                LV_QuaternionAxes(rotation, xAxis, yAxis, normal);

                float2 halfSize = max(size, float2(0.0, 0.0)) * 0.5;
                float2 startXY;
                float2 endXY;

                if (edgeID == 0u) {
                    startXY = float2(-halfSize.x, -halfSize.y);
                    endXY = float2(halfSize.x, -halfSize.y);
                } else if (edgeID == 1u) {
                    startXY = float2(halfSize.x, -halfSize.y);
                    endXY = halfSize;
                } else if (edgeID == 2u) {
                    startXY = halfSize;
                    endXY = float2(-halfSize.x, halfSize.y);
                } else {
                    startXY = float2(-halfSize.x, halfSize.y);
                    endXY = -halfSize;
                }

                edgeStartWS = centerWS + xAxis * startXY.x + yAxis * startXY.y;
                edgeEndWS = centerWS + xAxis * endXY.x + yAxis * endXY.y;
            }

            // Builds either a shared light icon or one of an area light's four rectangle outline edges.
            bool TryBuildLightCard(uint cardID, float2 quad, out float3 posWS, out half4 color, out half overlayType) {
                posWS = 0.0;
                color = 0.0;
                overlayType = LVDEBUG_OVERLAY_LINE;

                uint lightID;
                uint lightType;
                uint lightPart;
                if (!TryResolveLightCard(cardID, lightID, lightType, lightPart)) return false;

                float4 position = _UdonPointLightVolumePosition[lightID];
                if (lightPart == 0u) {
                    if (_DebugLightIconSize <= 0.0) return false;
                    posWS = BuildBillboardQuadPosition(position.xyz, quad);
                    color = half4(1.0, 1.0, 1.0, 1.0);
                    overlayType = LVDEBUG_OVERLAY_ICON;
                    return true;
                }

                if (lightType != LVDEBUG_LIGHT_TYPE_AREA) return false;
                if (_DebugBoundsThickness <= 0.0 || _DebugAreaLightRectColor.a <= 0.0) return false;

                float4 lightColor = _UdonPointLightVolumeColor[lightID];
                float3 edgeStartWS;
                float3 edgeEndWS;
                GetAreaLightRectEdge(lightPart - 1u, position.xyz, _UdonPointLightVolumeDirection[lightID], float2(position.w, lightColor.w - 2.0), edgeStartWS, edgeEndWS);
                posWS = BuildBillboardLinePosition(edgeStartWS, edgeEndWS, quad);
                color = (half4)_DebugAreaLightRectColor;
                return true;
            }

            // Expands mesh cards into the currently selected overlay debug mode.
            OverlayVaryings OverlayVert(OverlayAttributes v) {
                OverlayVaryings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(OverlayVaryings, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                DisableOverlayVertex(o);

                uint cardID = (uint)floor(v.cardData.x + 0.5);
                float2 quad = v.posOS.xy;
                float3 posWS = 0.0;
                half4 color = half4(1.0, 1.0, 1.0, 1.0);
                half overlayType = LVDEBUG_OVERLAY_LINE;
                bool valid = false;
                int drawMode = LVDebugResolveDrawMode();

                if (drawMode == LVDEBUG_MODE_SELECTED_VOLUME || drawMode == LVDEBUG_MODE_ALL_VOLUME_BOUNDS) {
                    valid = TryBuildVolumeBoundsCard(cardID, quad, drawMode, posWS, color);
                } else if (drawMode == LVDEBUG_MODE_ALL_LIGHTS) {
                    valid = TryBuildLightCard(cardID, quad, posWS, color, overlayType);
                }

                if (!valid) return o;

                o.posCS = UnityWorldToClipPos(posWS);
                o.uv = quad * 0.5 + 0.5;
                o.valid = 1.0;
                o.color = color;
                o.overlayType = overlayType;
                return o;
            }

            // Draws overlay lines directly and light icons through the material cutout texture.
            half4 OverlayFrag(OverlayVaryings i) : SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(i.valid - 0.5);
                if (i.overlayType > 0.5) {
                    half4 icon = tex2D(_DebugLightIcon, i.uv);
                    icon *= i.color;
                    clip(icon.a - (half)_DebugLightIconCutoff);
                    return icon;
                }

                return i.color;
            }
            ENDCG
        }
    }
}
