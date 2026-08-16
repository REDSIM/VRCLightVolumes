#if !UDONSHARP && (UNITY_EDITOR || COMPILER_UDONSHARP)
#define UDONSHARP
#endif

using UnityEngine;
using System;

#if UDONSHARP && COMPILER_UDONSHARP
using VRCShader = VRC.SDKBase.VRCShader;
#else
using VRCShader = UnityEngine.Shader;
#endif

namespace VRCLightVolumes {
    public partial class LightVolumeManager {
#region Shared Data Helpers

        // Precomputes the normalized EVSM receiver constants used by current shaders.
        private Vector4 GetPointLightShadowReceiverParams() {
            float varianceBias = Mathf.Max(ShadowMinVariance, 0f) * 0.01f;
            float bleedReduction = Mathf.Min(Mathf.Clamp01(ShadowBleedReduction), 0.999f);
            float bleedScale = 1f / (1f - bleedReduction);
            return new Vector4(varianceBias * 5.54f, -bleedReduction * bleedScale, bleedScale, varianceBias * 5f);
        }

        // Octahedrally packs a shape axis and 8-bit shape code into one exactly representable 24-bit float integer.
        private float EncodeClusterShape(Vector3 axis, int shapeCode) {
            float axisLengthSq = axis.sqrMagnitude;
            if (axisLengthSq < 0.000001f) axis = Vector3.forward;

            // Oct projection is scale invariant; L1-normalize directly and avoid an Udon sqrt.
            float inverseL1Length = 1f / (Mathf.Abs(axis.x) + Mathf.Abs(axis.y) + Mathf.Abs(axis.z));
            float octX = axis.x * inverseL1Length;
            float octY = axis.y * inverseL1Length;
            float octZ = axis.z * inverseL1Length;
            if (octZ < 0f) {
                float unfoldedX = octX;
                octX = (1f - Mathf.Abs(octY)) * (unfoldedX >= 0f ? 1f : -1f);
                octY = (1f - Mathf.Abs(unfoldedX)) * (octY >= 0f ? 1f : -1f);
            }

            int encodedX = Mathf.Clamp(Mathf.RoundToInt((octX * 0.5f + 0.5f) * ClusterAxisScale), 0, ClusterAxisScale);
            int encodedY = Mathf.Clamp(Mathf.RoundToInt((octY * 0.5f + 0.5f) * ClusterAxisScale), 0, ClusterAxisScale);
            return encodedX + encodedY * ClusterAxisStride + shapeCode * ClusterShapeStride;
        }

        // Packs two lights per vector as radius + shape. Shape 0 is point, 1 is one-sided area and 2..255 is a conservative spot cone.
        private void WriteClusteringLight(int shaderIndex, float squaredRange, int lightType, float outerTangent, Vector3 shapeAxis) {
            int shapeCode = 0;
            if (lightType == 1 && outerTangent > 0f) { // 1: spot; wider-than-hemisphere cones fall back to their range sphere tan(angle + padding) avoids two Udon transcendental calls while covering the packed-axis error.
                float paddingDenominator = 1f - outerTangent * ClusterAxisPad;
                if (paddingDenominator > 0f) {
                    float expandedTangent = (outerTangent + ClusterAxisPad) / paddingDenominator;
                    if (expandedTangent <= ClusterMaxTangent) {
                        int tangentLevel = Mathf.Clamp(Mathf.CeilToInt(expandedTangent / (1f + expandedTangent) * 255f), 1, 254);
                        shapeCode = tangentLevel + 1;
                    }
                }
            } else if (lightType == 2) { // 2: one-sided area
                shapeCode = 1;
            }

            float packedShape = shapeCode == 0 ? 0f : EncodeClusterShape(shapeAxis, shapeCode);
            float range = Mathf.Sqrt(Mathf.Max(squaredRange, 0f));
            int packedIndex = shaderIndex >> 1;
            Vector4 packedData = _clusteringLights[packedIndex];
            if ((shaderIndex & 1) == 0) {
                if (packedData.x == range && packedData.y == packedShape) return;
                packedData.x = range;
                packedData.y = packedShape;
            } else {
                if (packedData.z == range && packedData.w == packedShape) return;
                packedData.z = range;
                packedData.w = packedShape;
            }
            _clusteringLights[packedIndex] = packedData;
            _clusteringLightsDirty = true;
            _clusterGeometryUploadPending = true;
        }

        // Resolves the Area Cookie X/Y reflection relative to the quaternion frame sent to shaders.
        private float GetAreaCookieMirror(Matrix4x4 localToWorldMatrix, Quaternion transformRotation) {
            Vector3 matrixXAxis = new Vector3(localToWorldMatrix.m00, localToWorldMatrix.m10, localToWorldMatrix.m20);
            Vector3 matrixYAxis = new Vector3(localToWorldMatrix.m01, localToWorldMatrix.m11, localToWorldMatrix.m21);
            bool flipCookieX = Vector3.Dot(matrixXAxis, transformRotation * Vector3.right) < 0f;
            bool flipCookieY = Vector3.Dot(matrixYAxis, transformRotation * Vector3.up) < 0f;
            return (flipCookieY ? 2f : 1f) * (flipCookieX ? -1f : 1f);
        }

        // Computes a bounding sphere radius squared for area lights
        private float ComputeAreaLightSquaredBoundingSphere(float width, float height, Color color, float intensity, float cutoff) {
            float minSolidAngle = Mathf.Clamp(cutoff / (Mathf.Max(color.r, Mathf.Max(color.g, color.b)) * intensity), -Mathf.PI * 2f, Mathf.PI * 2);
            float A = width * height;
            float w2 = width * width;
            float h2 = height * height;
            float B = 0.25f * (w2 + h2);
            float t = Mathf.Tan(0.25f * minSolidAngle);
            float T = t * t;
            float TB = T * B;
            float discriminant = Mathf.Sqrt(TB * TB + 4.0f * T * A * A);
            return (discriminant - TB) * 0.125f / T;
        }

        // Computes a bounding sphere radius squared for point and spot lights
        private float ComputePointLightSquaredBoundingSphere(Color color, float intensity, float sqSize, float cutoff) {
            float L = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            return Mathf.Max(Mathf.PI * 2 * L * Mathf.Abs(intensity) / (cutoff * cutoff) - 1, 0) * sqSize;
        }

        // Recalculates point light culling range using manager-side math
        private void ComputePointLightRange(PointLightVolumeInstance instance) {
            if (instance == null) return;
            float cutoff = LightsBrightnessCutoff;
            if (instance.LightType == 2) { // 2: area
                instance.SquaredRange = ComputeAreaLightSquaredBoundingSphere(Mathf.Abs(instance.SquaredScale / instance.Width), instance.Height, instance.Color, instance.Intensity * Mathf.PI, cutoff);
            } else if (instance.ProjectionMode == 1) { // 1: LUT
                instance.SquaredRange = Mathf.Abs(instance.SquaredScale / instance.InverseSquaredRange);
            } else {
                instance.SquaredRange = ComputePointLightSquaredBoundingSphere(instance.Color, instance.Intensity, Mathf.Abs(instance.SquaredScale * instance.LightSourceSize * instance.LightSourceSize), cutoff);
            }
            instance.IsRangeDirty = false;
        }

        // Invalidates runtime atlas caches as one unit. Shadow invalidation must also clear the allocation-failure latch so an explicit source or registry change can retry the build.
        private void InvalidateTextureCaches(bool customTexturesChanged, bool shadowTexturesChanged) {
            if (customTexturesChanged) _customTexturesInitialized = false;
            if (!shadowTexturesChanged) return;
            _shadowTexturesInitialized = false;
            _shadowTextureAllocationFailed = false;
        }

        // Makes the manager's canonical range math available to runtime shadow bakers before they encode depth.
        public void RecalculatePointLightRange(PointLightVolumeInstance instance) {
            ComputePointLightRange(instance);
        }

        // Updates one regular volume's public mirrors and optionally writes its shader slot from the same locals.
        private void UpdateLightVolumeTransformData(LightVolumeInstance instance, Matrix4x4 localToWorldMatrix, int shaderIndex) {
            if (instance == null) return;
            Matrix4x4 invWorldMatrix = localToWorldMatrix.inverse;
            Quaternion transformRotation = localToWorldMatrix.rotation;
            Quaternion relativeRotation = transformRotation * instance.InvBakedRotation;
            bool isRotated = Mathf.Abs(relativeRotation.w) < 0.999999f;
            Vector3 lossyScale = localToWorldMatrix.lossyScale;
            float safeSmoothing = Mathf.Max(instance.SmoothBlending, 0.00001f);
            Vector4 invLocalEdgeSmoothing = new Vector4(
                lossyScale.x / safeSmoothing,
                lossyScale.y / safeSmoothing,
                lossyScale.z / safeSmoothing,
                0f);
            Vector3 relativeRotationRow0 = new Vector3(1, 0, 0);
            Vector3 relativeRotationRow1 = new Vector3(0, 1, 0);
            if (isRotated) {
                Matrix4x4 rotationMatrix = Matrix4x4.Rotate(relativeRotation);
                relativeRotationRow0 = rotationMatrix.GetRow(0);
                relativeRotationRow1 = rotationMatrix.GetRow(1);
            }

            instance.InvWorldMatrix = invWorldMatrix;
            instance.InvLocalEdgeSmoothing = invLocalEdgeSmoothing;
            instance.IsRotated = isRotated;
            instance.RelativeRotationRow0 = relativeRotationRow0;
            instance.RelativeRotationRow1 = relativeRotationRow1;

            if (shaderIndex < 0) return;
            int rotationIndex = shaderIndex * 2;
            _invWorldMatrix[shaderIndex] = invWorldMatrix;
            if (!_invLocalEdgeSmooth[shaderIndex].Equals(invLocalEdgeSmoothing)) {
                _invLocalEdgeSmooth[shaderIndex] = invLocalEdgeSmoothing;
                _updateLightVolumeEdgeBuffer = true;
            }
            Vector4 color = _colors[shaderIndex];
            color.w = isRotated ? 1 : 0;
            _colors[shaderIndex] = color;
            _relativeRotation[rotationIndex] = relativeRotationRow0;
            _relativeRotation[rotationIndex + 1] = relativeRotationRow1;
        }

        // Updates one point light instance from its current transform data
        private void UpdatePointLightTransformData(PointLightVolumeInstance instance, Transform instanceTransform, Matrix4x4 localToWorldMatrix, bool forceRangeUpdate) {
            if (instance == null) return;
            int lightType = instance.LightType;
            int projectionMode = instance.ProjectionMode;
            float oldSquaredScale = forceRangeUpdate ? 0 : instance.SquaredScale;
            Vector3 scale = localToWorldMatrix.lossyScale;
            float scaleX = Mathf.Abs(scale.x);
            float scaleY = Mathf.Abs(scale.y);
            float scaleZ = Mathf.Abs(scale.z);
            instance.Position = localToWorldMatrix.GetPosition();

            if (lightType != 0 || projectionMode != 0) { // 0: point, 0: parametric
                // A reflected matrix has no quaternion representation. Keep the physical light rotation and carry Area Cookie-only X/Y reflection in its custom projection descriptor.
                Quaternion transformRotation = instanceTransform.rotation;
                if (lightType == 2) { // 2: area
                    instance.Rotation = transformRotation;
                    instance.Width = Mathf.Max(scaleX, 0.001f);
                    instance.Height = Mathf.Max(scaleY, 0.001f);
                    instance.AreaCookieMirror = GetAreaCookieMirror(localToWorldMatrix, transformRotation);
                } else if (lightType == 1 && projectionMode != 2) { // 1: spot, 2: custom cookie
                    instance.Direction = transformRotation * Vector3.forward;
                } else {
                    instance.Rotation = Quaternion.Inverse(transformRotation);
                }
            }

            float averageScale = (scaleX + scaleY + scaleZ) * 0.3333333333f;
            float squaredScale = averageScale * averageScale;
            instance.SquaredScale = squaredScale;
            if (forceRangeUpdate || lightType == 2 || Mathf.Abs(oldSquaredScale - squaredScale) > 0.001f) ComputePointLightRange(instance);
        }

        // Finds the current compact shader slot for one registered regular volume
        private int FindLightVolumeFinalIndex(int registryIndex) {
            if (registryIndex < 0) return -1;
            return Array.IndexOf((Array)_enabledIDs, registryIndex, 0, _enabledCount);
        }

        // Grows manager-local selection keys only when the registry itself outgrows them. The fused selection rewrites every accepted ID before it can be compared, so old values do not need copying.
        private void EnsureLightVolumeSelectionCapacity(int requiredCapacity) {
            int weightCapacity = _selectionLightVolumeWeights == null ? 0 : _selectionLightVolumeWeights.Length;
            int orderCapacity = _selectionLightVolumeOrders == null ? 0 : _selectionLightVolumeOrders.Length;
            if (weightCapacity >= requiredCapacity && orderCapacity >= requiredCapacity) return;

            int currentCapacity = Mathf.Min(weightCapacity, orderCapacity);
            int grownCapacity = currentCapacity > 0 ? currentCapacity * 2 : MaxLightVolumeCount;
            int newCapacity = Mathf.Max(requiredCapacity, grownCapacity);
            _selectionLightVolumeWeights = new float[newCapacity];
            _selectionLightVolumeOrders = new int[newCapacity];
        }

        // Selects the highest-priority active volumes without changing their authoring registry.
        private int SelectLightVolumesByWeight() {
            int selectedCount = 0;
            int registryCount = LightVolumeInstances.Length;

            // Read every active source once and insert it immediately. This keeps direct public field writes visible on the next rebuild without a persistent cache, active snapshot or second registry pass.
            for (int registryIndex = 0; registryIndex < registryCount; registryIndex++) {
                LightVolumeInstance instance = LightVolumeInstances[registryIndex];
                if (instance == null || !instance.IsActive) continue;
                float candidateWeight = instance.RegistryWeight;
                int candidateOrder = instance.RegistryOrder;

                int insertIndex = selectedCount;
                for (int selectedIndex = 0; selectedIndex < selectedCount; selectedIndex++) {
                    int selectedRegistryIndex = _selectedLightVolumeIDs[selectedIndex];
                    float selectedWeight = _selectionLightVolumeWeights[selectedRegistryIndex];
                    bool higherWeight = candidateWeight > selectedWeight;
                    bool earlierEqualWeight = candidateWeight == selectedWeight && candidateOrder < _selectionLightVolumeOrders[selectedRegistryIndex];
                    if (!higherWeight && !earlierEqualWeight) continue;
                    insertIndex = selectedIndex;
                    break;
                }
                if (insertIndex >= MaxLightVolumeCount) continue;

                // Only accepted IDs can become selected records and be compared later.
                _selectionLightVolumeWeights[registryIndex] = candidateWeight;
                _selectionLightVolumeOrders[registryIndex] = candidateOrder;
                int shiftStart = selectedCount < MaxLightVolumeCount ? selectedCount : MaxLightVolumeCount - 1;
                int shiftCount = shiftStart - insertIndex;
                if (shiftCount > 0) Array.Copy(_selectedLightVolumeIDs, insertIndex, _selectedLightVolumeIDs, insertIndex + 1, shiftCount);
                _selectedLightVolumeIDs[insertIndex] = registryIndex;
                if (selectedCount < MaxLightVolumeCount) selectedCount++;
            }
            return selectedCount;
        }

        // Finds the current compact shader slot for one registered point light
        private int FindPointLightFinalIndex(int registryIndex) {
            if (registryIndex < 0) return -1;
            if (registryIndex < _pointLightRegistryToShaderIndex.Length) {
                int shaderIndex = _pointLightRegistryToShaderIndex[registryIndex];
                if (shaderIndex >= 0 && shaderIndex < _pointLightCount && _enabledPointIDs[shaderIndex] == registryIndex)
                    return shaderIndex;
            }

            // Registry mutations request a rebuild, but validate-and-fallback keeps notifications correct even if an integration changes the public registry array directly.
            int fallbackIndex = Array.IndexOf((Array)_enabledPointIDs, registryIndex, 0, _pointLightCount);
            if (fallbackIndex >= 0 && registryIndex < _pointLightRegistryToShaderIndex.Length) _pointLightRegistryToShaderIndex[registryIndex] = fallbackIndex;
            return fallbackIndex;
        }

#endregion

#region Change Notifications

        // Used by LightVolumeInstance runtime methods
        public void NotifyLightVolumeChanged(LightVolumeInstance lightVolume, bool rebuildFinalData) {
#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
#endif
            // Checking, initializing...
            if (lightVolume == null) return;
            if (LightVolumeInstances == null) LightVolumeInstances = new LightVolumeInstance[0];
            int registryIndex = FindLightVolumeRegistryIndex(lightVolume);
            if (registryIndex < 0) {
                if (!lightVolume.IsActive) return;
                InitializeLightVolume(lightVolume);
                registryIndex = FindLightVolumeRegistryIndex(lightVolume);
                if (registryIndex < 0) return;
            }
            int shaderIndex = FindLightVolumeFinalIndex(registryIndex);
            bool isActive = lightVolume.IsActive;
            if (isActive != (shaderIndex >= 0) || rebuildFinalData) {
                RequestUpdateVolumes();
                return;
            }
            if (!isActive) return;

            // Update shader data
            WriteLightVolumeShaderData(shaderIndex, lightVolume);
            _lightVolumeArraysDirty = true;

#if !COMPILER_UDONSHARP
            // PostLateUpdate/LateUpdate is the single runtime consumer. Edit Mode has no frame consumer, so preserve the historical synchronous editor update.
            if (!Application.isPlaying) UpdateVolumes();
#endif
        }

        // Narrow notification used only when a LightVolumeInstance changed Color and/or Intensity. It is public because the source and manager are separate Udon behaviours. the generic compatibility API above remains the safe fallback when any other field may have changed.
        public void NotifyLightVolumeColorChanged(LightVolumeInstance lightVolume) {
#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
#endif
            if (lightVolume == null) return;
            if (LightVolumeInstances == null) LightVolumeInstances = new LightVolumeInstance[0];
            int registryIndex = FindLightVolumeRegistryIndex(lightVolume);
            if (registryIndex < 0) {
                if (!lightVolume.IsActive) return;
                InitializeLightVolume(lightVolume);
                registryIndex = FindLightVolumeRegistryIndex(lightVolume);
                if (registryIndex < 0) return;
            }

            int shaderIndex = FindLightVolumeFinalIndex(registryIndex);
            bool isActive = lightVolume.IsActive;
            if (isActive != (shaderIndex >= 0)) {
                RequestUpdateVolumes();
                return;
            }
            if (!isActive) return;

            Vector4 color = lightVolume.Color.linear * lightVolume.Intensity;
            color.w = lightVolume.IsRotated ? 1 : 0;
            _colors[shaderIndex] = color;
            _lightVolumeArraysDirty = true;

#if !COMPILER_UDONSHARP
            // PostLateUpdate/LateUpdate is the single runtime consumer. Edit Mode has no frame consumer, so apply synchronously just as the generic notification does.
            if (!Application.isPlaying) UpdateVolumes();
#endif
        }

        // Used by PointLightVolumeInstance runtime methods
        public void NotifyPointLightVolumeChanged(PointLightVolumeInstance pointLightVolume, bool rebuildFinalData, bool customTexturesChanged, bool shadowTexturesChanged) {
#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
#endif
            // Checking, initializing...
            if (pointLightVolume == null) return;
            // This is the preserved generic public API: callers may have changed any record field.
            if (PointLightVolumeInstances == null) PointLightVolumeInstances = new PointLightVolumeInstance[0];
            int registryIndex = FindPointLightRegistryIndex(pointLightVolume);
            if (registryIndex < 0) {
                if (!pointLightVolume.IsActive) return;
                InitializePointLightVolume(pointLightVolume);
                registryIndex = FindPointLightRegistryIndex(pointLightVolume);
                if (registryIndex < 0) return;
            }

            int shaderIndex = FindPointLightFinalIndex(registryIndex);
            bool isActive = pointLightVolume.IsActive;
            if (isActive != (shaderIndex >= 0) || rebuildFinalData) {
                InvalidateTextureCaches(customTexturesChanged, shadowTexturesChanged);
                RequestUpdateVolumes();
                return;
            }
            if (!isActive) return;
            if (customTexturesChanged || shadowTexturesChanged) {
                InvalidateTextureCaches(customTexturesChanged, shadowTexturesChanged);
                RequestUpdateVolumes();
                return;
            }

            // Preserve the historical API contract: public SquaredRange is current when Notify returns, even though compact buffer packing is now coalesced until the end of the frame.
            if (pointLightVolume.IsRangeDirty) ComputePointLightRange(pointLightVolume);
            int updateFlags = _dirtyPointLightUpdateFlags[shaderIndex];

            // Queue the compact slot once. Packing at the end of the frame preserves last-write-wins semantics and avoids repeating the expensive cross-behaviour reads for Color/Intensity.
            if (updateFlags == 0) {
                if (_dirtyPointLightCount >= MaxPointLightCount) {
                    RequestUpdateVolumes();
                    return;
                }
                _dirtyPointLightShaderIndices[_dirtyPointLightCount] = shaderIndex;
                _dirtyPointLightCount++;
            }
            _dirtyPointLightUpdateFlags[shaderIndex] = updateFlags | PointLightUpdateFull;
            // Queued Point record changes are consumed once in PostLateUpdate. Scheduling the maintenance event here would only wake an empty UpdateProcess on the next frame.
#if !COMPILER_UDONSHARP
            // Edit Mode has no PostLate consumer; retain the historical synchronous proxy update.
            if (!Application.isPlaying) UpdateVolumes();
#endif
        }

        // Narrow notification used only by PointLightVolumeInstance Color/Intensity callbacks. It is public solely because the instance and manager are separate Udon behaviours.
        public void NotifyPointLightColorRangeChanged(PointLightVolumeInstance pointLightVolume) {
#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
#endif
            if (pointLightVolume == null) return;
            if (PointLightVolumeInstances == null) PointLightVolumeInstances = new PointLightVolumeInstance[0];
            int registryIndex = FindPointLightRegistryIndex(pointLightVolume);
            if (registryIndex < 0) {
                if (!pointLightVolume.IsActive) return;
                InitializePointLightVolume(pointLightVolume);
                registryIndex = FindPointLightRegistryIndex(pointLightVolume);
                if (registryIndex < 0) return;
            }

            int shaderIndex = FindPointLightFinalIndex(registryIndex);
            bool isActive = pointLightVolume.IsActive;
            if (isActive != (shaderIndex >= 0)) {
                // A registered source can exist before selection/compact buffers are rebuilt. Its basic-Point callback may already have calculated an exact local range, but the
                // historical structural contract keeps that value dirty until the full rebuild commits source membership and canonical data together.
                pointLightVolume.IsRangeDirty = true;
                InvalidateTextureCaches(pointLightVolume.CustomTexture != null || pointLightVolume.CustomTextureMaterial != null,
                    pointLightVolume.ShadowMapID >= 0 || pointLightVolume.ShadowMapTexture != null || pointLightVolume.ShadowMapMaterial != null);
                RequestUpdateVolumes();
                return;
            }
            if (!isActive) return;

            // SetColor/SetIntensity historically published the derived range synchronously. The measured stable basic-Point path may already have done the exact calculation source-side. Every structural or richer profile reaches this canonical fallback with IsRangeDirty still set.
            if (pointLightVolume.IsRangeDirty) ComputePointLightRange(pointLightVolume);
            int updateFlags = _dirtyPointLightUpdateFlags[shaderIndex];

            if (updateFlags == 0) {
                if (_dirtyPointLightCount >= MaxPointLightCount) {
                    RequestUpdateVolumes();
                    return;
                }
                _dirtyPointLightShaderIndices[_dirtyPointLightCount] = shaderIndex;
                _dirtyPointLightCount++;
            }
            _dirtyPointLightUpdateFlags[shaderIndex] = updateFlags | PointLightUpdateColorRange;
            // PostLateUpdate owns this queue. Structural fallbacks above still schedule their normal rebuild through RequestUpdateVolumes().
#if !COMPILER_UDONSHARP
            // Edit Mode has no PostLate consumer; retain the historical synchronous proxy update.
            if (!Application.isPlaying) UpdateVolumes();
#endif
        }

        // Sets the Force Scene Lighting shader override explicitly for manual runtime control.
        public void SetForceSceneLighting(bool enabled) {
#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
#endif
            ForceSceneLighting = enabled;
            TryInitialize();
            // Lore: https://x.com/lil_xyzw/status/1961487430256922928?s=20
            VRCShader.SetGlobalInteger(_forceSceneLightingID, enabled ? 1 : 0);
        }

#if UDONSHARP
        // External runtime writes can enable texture auto-updates after the delayed process stopped.
        public void _onVarChange_AutoUpdateTextures() {
            if (AutoUpdateTextures) ScheduleUpdateProcess();
        }
#endif

#if UDONSHARP || UNITY_EDITOR
        // Applies Inspector-authored scalar settings without rebuilding volume registries or texture caches.
        public void _ApplyEditorSettings() {
            TryInitialize();
            VRCShader.SetGlobalFloat(_lightVolumeProbesBlendID, LightProbesBlending ? 1f : 0f);
            VRCShader.SetGlobalFloat(_lightVolumeSharpBoundsID, SharpBounds ? 1f : 0f);
            VRCShader.SetGlobalFloat(_lightVolumeAdditiveMaxOverdrawID, AdditiveMaxOverdraw);
            VRCShader.SetGlobalFloat(_lightBrightnessCutoffID, LightsBrightnessCutoff);
            VRCShader.SetGlobalVector(_pointLightShadowReceiverParamsID, GetPointLightShadowReceiverParams());
            if (AutoUpdateTextures) ScheduleUpdateProcess();
        }
#endif

        // Enables or disables camera-relative froxel clustering at runtime.
        public void SetClustering(bool enabled) {
#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
#endif
            if (Clustering == enabled) {
                // An explicit retry may recover from a layout-specific allocation failure.
                if (enabled && (_clusteringUnsupported || _clusteringAllocationFailed)) {
                    _clusteringUnsupported = false;
                    _clusteringAllocationFailed = false;
                    _froxelLayoutValid = false;
                }
                return;
            }
            Clustering = enabled;
            _clusteringUnsupported = false;
            _clusteringAllocationFailed = false;
            _froxelLayoutValid = false;
            TryInitialize();
            DisableClustering();
        }

#endregion

#region Initialization

        // Initializes shader property IDs and global shader arrays when needed
        private void TryInitialize() {
            if (_isInitialized) return;
            // Light Volumes
            _lightVolumeInvLocalEdgeSmoothID = VRCShader.PropertyToID("_UdonLightVolumeInvLocalEdgeSmooth");
            _lightVolumeInvWorldMatrixID = VRCShader.PropertyToID("_UdonLightVolumeInvWorldMatrix");
            _lightVolumeColorID = VRCShader.PropertyToID("_UdonLightVolumeColor");
            _lightVolumeCountID = VRCShader.PropertyToID("_UdonLightVolumeCount");
            _lightVolumeAdditiveCountID = VRCShader.PropertyToID("_UdonLightVolumeAdditiveCount");
            _lightVolumeAdditiveMaxOverdrawID = VRCShader.PropertyToID("_UdonLightVolumeAdditiveMaxOverdraw");
            _lightVolumeEnabledID = VRCShader.PropertyToID("_UdonLightVolumeEnabled");
            _lightVolumeVersionID = VRCShader.PropertyToID("_UdonLightVolumeVersion");
            _lightVolumeProbesBlendID = VRCShader.PropertyToID("_UdonLightVolumeProbesBlend");
            _lightVolumeSharpBoundsID = VRCShader.PropertyToID("_UdonLightVolumeSharpBounds");
            _lightVolumeID = VRCShader.PropertyToID("_UdonLightVolume");
            _lightVolumeRotationID = VRCShader.PropertyToID("_UdonLightVolumeRotation");
            _lightVolumeUvwScaleID = VRCShader.PropertyToID("_UdonLightVolumeUvwScale");
            _lightVolumeUvwID = VRCShader.PropertyToID("_UdonLightVolumeUvw");
            _lightVolumeOcclusionCountID = VRCShader.PropertyToID("_UdonLightVolumeOcclusionCount");
            // Point Lights
            _pointLightPositionID = VRCShader.PropertyToID("_UdonPointLightVolumePosition");
            _pointLightColorID = VRCShader.PropertyToID("_UdonPointLightVolumeColor");
            _pointLightExtraDataID = VRCShader.PropertyToID("_UdonPointLightVolumeExtraData");
            _pointLightDirectionID = VRCShader.PropertyToID("_UdonPointLightVolumeDirection");
            _pointLightCountID = VRCShader.PropertyToID("_UdonPointLightVolumeCount");
            _pointLightCustomIdID = VRCShader.PropertyToID("_UdonPointLightVolumeCustomID");
            _pointLightCubeCountID = VRCShader.PropertyToID("_UdonPointLightVolumeCubeCount");
            _pointLightTextureID = VRCShader.PropertyToID("_UdonPointLightVolumeTexture");
            _pointLightTextureTexelCountID = VRCShader.PropertyToID("_UdonPointLightVolumeTextureTexelCount");
            _pointLightTextureMaxMipID = VRCShader.PropertyToID("_UdonPointLightVolumeTextureMaxMip");
            _pointLightShadowReprojectionDataID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowReprojectionData");
            _pointLightShadowRotationDataID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowRotationData");
            _pointLightShadowCountID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowCount");
            _pointLightShadowCubeCountID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowCubeCount");
            _pointLightShadowTextureID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowTexture");
            _pointLightShadowReceiverParamsID = VRCShader.PropertyToID("_UdonPointLightVolumeShadowReceiverParams");
            _clusteringLightsID = VRCShader.PropertyToID("_UdonClusteringLights");
            _lightBrightnessCutoffID = VRCShader.PropertyToID("_UdonLightBrightnessCutoff");
            // Froxel Clustering
            _clusteringEnabledID = VRCShader.PropertyToID("_UdonClusteringEnabled");
            _clusterMaskID = VRCShader.PropertyToID("_UdonClusterMask");
            _froxelGridID = VRCShader.PropertyToID("_UdonFroxelGrid");
            _froxelDepthID = VRCShader.PropertyToID("_UdonFroxelDepth");
            _froxelDepthStepID = VRCShader.PropertyToID("_UdonFroxelDepthStep");
            _coarseClusterMaskID = VRCShader.PropertyToID("_UdonCoarseClusterMask");
            _froxelCoarseGridID = VRCShader.PropertyToID("_UdonFroxelCoarseGrid");
            _froxelFineGridID = VRCShader.PropertyToID("_UdonFroxelFineGrid");
            _froxelPassID = VRCShader.PropertyToID("_UdonFroxelPass");
            _froxelCoarseID = VRCShader.PropertyToID("_UdonFroxelCoarse");
            _froxelProjectionID = VRCShader.PropertyToID("_UdonFroxelProjection");
            _froxelRightID = VRCShader.PropertyToID("_UdonFroxelRight");
            _froxelUpID = VRCShader.PropertyToID("_UdonFroxelUp");
            _froxelForwardID = VRCShader.PropertyToID("_UdonFroxelForward");
            // Other
            _forceSceneLightingID = VRCShader.PropertyToID("_UdonForceSceneLighting");
            _cubemapMainTexID = VRCShader.PropertyToID("_MainTex");
            _cubemapSourceTexID = VRCShader.PropertyToID("_CubeTex");
            _cubemapFaceIndexID = VRCShader.PropertyToID("_FaceIndex");

            // Light Volumes
            VRCShader.SetGlobalVectorArray(_lightVolumeInvLocalEdgeSmoothID, _invLocalEdgeSmooth);
            VRCShader.SetGlobalVectorArray(_lightVolumeColorID, _colors);
            VRCShader.SetGlobalMatrixArray(_lightVolumeInvWorldMatrixID, _invWorldMatrix);
            VRCShader.SetGlobalVectorArray(_lightVolumeRotationID, _relativeRotation);
            VRCShader.SetGlobalVectorArray(_lightVolumeUvwScaleID, _boundsUvwScale);
            VRCShader.SetGlobalVectorArray(_lightVolumeUvwID, _boundsUvw);
            VRCShader.SetGlobalFloat(_lightVolumeOcclusionCountID, 0);
            // Point Lights
            VRCShader.SetGlobalVectorArray(_pointLightPositionID, _pointLightPosition);
            VRCShader.SetGlobalVectorArray(_pointLightColorID, _pointLightColor);
            VRCShader.SetGlobalVectorArray(_pointLightExtraDataID, _pointLightExtraData);
            VRCShader.SetGlobalVectorArray(_pointLightDirectionID, _pointLightDirection);
            VRCShader.SetGlobalVectorArray(_pointLightCustomIdID, _pointLightCustomId);
            VRCShader.SetGlobalVectorArray(_pointLightShadowReprojectionDataID, _pointLightShadowReprojectionData);
            VRCShader.SetGlobalVectorArray(_pointLightShadowRotationDataID, _pointLightShadowRotationData);
            VRCShader.SetGlobalVector(_pointLightShadowReceiverParamsID, GetPointLightShadowReceiverParams());
            _clusteringLightsDirty = true;
            VRCShader.SetGlobalFloat(_clusteringEnabledID, 0f);
            _isInitialized = true;
        }

        // Writes a fully disabled state to shader globals so stale counts do not survive after all volumes disappear
        private void SetDisabledShaderState() {
            VRCShader.SetGlobalFloat(_lightVolumeCountID, 0);
            VRCShader.SetGlobalFloat(_lightVolumeAdditiveCountID, 0);
            VRCShader.SetGlobalFloat(_pointLightCountID, 0);
            VRCShader.SetGlobalFloat(_pointLightCubeCountID, 0);
            VRCShader.SetGlobalFloat(_pointLightShadowCubeCountID, 0);
            VRCShader.SetGlobalFloat(_pointLightShadowCountID, 0);
            VRCShader.SetGlobalVector(_pointLightShadowReceiverParamsID, GetPointLightShadowReceiverParams());
            VRCShader.SetGlobalFloat(_clusteringEnabledID, 0f);
            _clusteringActive = false;
            VRCShader.SetGlobalFloat(_lightVolumeEnabledID, 0);
        }

#endregion

#region Lifecycle

        // Applies the optional global lighting override once after initial enable.
        private void Start() {
            if (ForceSceneLighting) SetForceSceneLighting(true);
        }

        // Clears runtime state and schedules a fresh shader upload.
        private void OnEnable() {
            _isInitialized = false;
            _clusteringUnsupported = false;
            _clusteringAllocationFailed = false;
            _shadowTextureAllocationFailed = false;
            _froxelLayoutValid = false;
            _froxelDepthValid = false;
            _froxelProjectionValid = false;
            _clusterMaskDirty = true;
            _clusterMaskValid = false;
            ReconcileRegistryActiveStates();
            RequestUpdateVolumes();
        }

        // Stops automatic updates and disables shader globals when this manager is disabled
        private void OnDisable() {
            TryInitialize();
#if UDONSHARP
            // Delayed Udon events cannot be cancelled. Keep the latch until the queued
            // callback observes the disabled Manager, otherwise a quick re-enable can
            // schedule a second consumer for the same frame.
#else
            if (_updateCoroutine != null) {
                StopCoroutine(_updateCoroutine);
                _updateCoroutine = null;
            }
#endif
            DisableClustering();
            SetDisabledShaderState();
        }

#if UDONSHARP
        // Updates cached dynamic transforms and camera-relative clustering after runtime motion has settled.
        public override void PostLateUpdate() {
            UpdateDynamicVolumeTransforms();
            UpdateClustering();
        }
#else
        // Updates cached dynamic transforms and camera-relative clustering after standalone motion has settled.
        private void LateUpdate() {
            if (!Application.isPlaying) return;
            UpdateDynamicVolumeTransforms();
            Camera camera = Camera.main;
            if (camera == null) camera = Camera.current;
            UpdateClusteringFromCamera(camera);
        }
#endif

#if !COMPILER_UDONSHARP && (!UDONSHARP || UNITY_EDITOR)
        // Releases generated native resources when the manager object is destroyed
        private void OnDestroy() {
            if (CustomTextures != null && CustomTextures.hideFlags == HideFlags.HideAndDontSave) {
                ReleaseRuntimeRenderTexture(CustomTextures);
                CustomTextures = null;
            }
            if (ShadowTextures != null && ShadowTextures.hideFlags == HideFlags.HideAndDontSave) {
                ReleaseRuntimeRenderTexture(ShadowTextures);
                ShadowTextures = null;
            }
            ReleaseClusteringTextures();
#if UDONSHARP
            if (_dummyRT != null) {
                ReleaseRuntimeRenderTexture(_dummyRT);
                _dummyRT = null;
            }
#endif
            DestroyCubemapFaceRuntimeMaterial();
            DestroyClusteringMaterial();
        }
#endif

#endregion
    }
}
