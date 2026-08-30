#if !UDONSHARP && COMPILER_UDONSHARP
#define UDONSHARP
#endif

using UnityEngine;
using System;

#if !UDONSHARP
using System.Collections;
#endif
#if UDONSHARP && COMPILER_UDONSHARP
using VRCShader = VRC.SDKBase.VRCShader;
#else
using VRCShader = UnityEngine.Shader;
#endif

namespace VRCLightVolumes {
    public partial class LightVolumeManager {

#region Update Process

        // Packs each notified Point Light slot once, using the final values written during the frame. Returns false when compact-buffer topology changed and a full rebuild is required.
        private bool FlushPendingPointLightChanges() {
            int dirtyCount = _dirtyPointLightCount;
            if (dirtyCount == 0) return true;
            if (PointLightVolumeInstances == null) {
                ResetPendingPointLightChanges();
                return false;
            }

            for (int i = 0; i < dirtyCount; i++) {
                int shaderIndex = _dirtyPointLightShaderIndices[i];
                if (shaderIndex < 0 || shaderIndex >= _pointLightCount) {
                    ResetPendingPointLightChanges();
                    return false;
                }
                int updateFlags = _dirtyPointLightUpdateFlags[shaderIndex];
                _dirtyPointLightUpdateFlags[shaderIndex] = 0;

                int registryIndex = _enabledPointIDs[shaderIndex];
                if (registryIndex < 0 || registryIndex >= PointLightVolumeInstances.Length) {
                    ResetPendingPointLightChanges();
                    return false;
                }
                PointLightVolumeInstance instance = PointLightVolumeInstances[registryIndex];
                if (instance == null || !instance.IsActive) {
                    ResetPendingPointLightChanges();
                    return false;
                }

                float packedShadowIdAbs = Mathf.Abs(_pointLightCustomId[shaderIndex].y);
                bool hasActiveShadow = packedShadowIdAbs >= 1f && packedShadowIdAbs < DisabledShadingShadowId;
                bool useBasicColorRangePack = updateFlags == PointLightUpdateColorRange && instance.LightType == 0 && instance.ProjectionMode == 0 && !hasActiveShadow;

                if (useBasicColorRangePack) {
                    Vector4 previousPosition = _pointLightPosition[shaderIndex];
                    Vector4 previousColor = _pointLightColor[shaderIndex];
                    Vector4 previousExtraData = _pointLightExtraData[shaderIndex];
                    Vector4 previousCustomId = _pointLightCustomId[shaderIndex];
                    if (instance.IsRangeDirty) ComputePointLightRange(instance);
                    float squaredScale = instance.SquaredScale;
                    float squaredRange = instance.SquaredRange;
                    float lightSourceSize = instance.LightSourceSize;

                    Vector4 position = _pointLightPosition[shaderIndex];
                    position.w = lightSourceSize * lightSourceSize * squaredScale;
                    _pointLightPosition[shaderIndex] = position;

                    Vector4 lightColor = instance.Color.linear * instance.Intensity;
                    Vector4 color = lightColor;
                    color.w = instance.OuterAngleCos;
                    _pointLightColor[shaderIndex] = color;
                    lightColor.w = 0f;
                    _pointLightExtraData[shaderIndex] = lightColor;

                    Vector4 customId = _pointLightCustomId[shaderIndex];
                    customId.z = squaredRange;
                    _pointLightCustomId[shaderIndex] = customId;
                    WriteClusteringLight(shaderIndex, squaredRange, 0, 0f, Vector3.forward, false);

                    int uploadMask = 0;
                    if (PackedVectorChanged(previousPosition, _pointLightPosition[shaderIndex])) uploadMask |= PointLightUploadPosition;
                    if (PackedVectorChanged(previousColor, _pointLightColor[shaderIndex])) uploadMask |= PointLightUploadColor;
                    if (PackedVectorChanged(previousExtraData, _pointLightExtraData[shaderIndex])) uploadMask |= PointLightUploadExtraData;
                    if (PackedVectorChanged(previousCustomId, _pointLightCustomId[shaderIndex])) uploadMask |= PointLightUploadCustomId;
                    MarkPointLightArrayUploads(uploadMask);
                } else {
                    WritePointLightShaderDataTracked(shaderIndex, registryIndex, instance, null);
                }
            }

            _dirtyPointLightCount = 0;
            return true;
        }

        // Clears queued update masks during a full compact rebuild.
        private void ResetPendingPointLightChanges() {
            for (int i = 0; i < _dirtyPointLightCount; i++) {
                int shaderIndex = _dirtyPointLightShaderIndices[i];
                if (shaderIndex >= 0 && shaderIndex < MaxPointLightCount) {
                    _dirtyPointLightUpdateFlags[shaderIndex] = 0;
                }
            }
            _dirtyPointLightCount = 0;
        }

        // Requests to update volumes next frame
        public void RequestUpdateVolumes() {
#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
            // Udon delayed events are not dispatched for the edit-mode C# proxy.
            if (!Application.isPlaying) {
                UpdateVolumes();
                return;
            }
#endif
            if (_isUpdatingVolumes) return;
            _volumeDataUpdateRequested = true;
            ScheduleUpdateProcess();
        }

        // Schedules the unified delayed update process when it is not already running
        private void ScheduleUpdateProcess() {
#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
            if (!Application.isPlaying) {
                UpdateVolumes();
                return;
            }
#endif
#if UDONSHARP
            if (_isUpdateProcessRunning) return;
            _isUpdateProcessRunning = true;
            SendCustomEventDelayedFrames(nameof(UpdateProcess), 1);
#else
            if (_updateCoroutine != null || !isActiveAndEnabled) return;
            _updateCoroutine = StartCoroutine(UpdateCoroutine());
#endif
        }

        // Consumes at most one whole-light request per update frame.
        private bool ProcessBakeInGameQueueStep() {
            if (_bakeInGameQueueCount <= 0) return false;

            PointLightVolumeInstance pointLightVolume = _bakeInGameQueue[_bakeInGameQueueHead];
            _bakeInGameQueue[_bakeInGameQueueHead] = null;
            _bakeInGameQueueHead++;
            if (_bakeInGameQueueHead >= MaxPointLightCount) _bakeInGameQueueHead = 0;
            _bakeInGameQueueCount--;

            if (pointLightVolume != null) {
                pointLightVolume.RuntimeShadowResolution = pointLightVolume.ShadowBakeResolution > 0 ? Mathf.Clamp(pointLightVolume.ShadowBakeResolution, 16, 2048) : Mathf.Clamp(ShadowTexturesWidth, 16, 2048);
                pointLightVolume.RuntimeShadowBlurSamplePreset = Mathf.Clamp(pointLightVolume.RuntimeShadowBlurSamplePreset, 0, 2);
                pointLightVolume.RuntimeShadowDirectOutput = false;
                pointLightVolume.BakeShadows();
            }
            return _bakeInGameQueueCount > 0;
        }

        // Flushes direct parameter changes and polls cached Dynamic entries in the transform-safe frame phase shared with clustering. Point data must reach the shader before clustering
        // consumes the corresponding geometry; otherwise continuous Color/Intensity animation can leave _clusterGeometryUploadPending set and force the sequential-light fallback every frame.
        private void UpdateDynamicVolumeTransforms() {
            if (_isUpdatingVolumes || _volumeDataUpdateRequested) return;
            int previousActiveShadowCullCount = _activeShadowCullCount;

            // Incremental packers below set exact per-array bits. Reset before flushing queued notifications so their tracked changes are not overwritten by frame setup.
            ResetPointLightArrayUploadState();
            bool flushedPointLightChanges = _dirtyPointLightCount != 0;
            if (flushedPointLightChanges && !FlushPendingPointLightChanges()) {
                // The failed batch may already have packed earlier slots. Keep the published eligibility count transactional; the requested full rebuild will recalculate it.
                _activeShadowCullCount = previousActiveShadowCullCount;
                ResetPointLightArrayUploadState();
                RequestUpdateVolumes();
                return;
            }

            bool hasPendingParameterChanges = _lightVolumeArraysDirty || flushedPointLightChanges;
            bool hasDynamicTransforms = AutoUpdateVolumes && (_dynamicLightVolumeCount != 0 || _dynamicPointLightVolumeCount != 0);
            if (!hasPendingParameterChanges && !hasDynamicTransforms) return;

            // Generic notifications already packed their final one-slot data. Merge those dirty groups with transform changes so one upload observes the final state for this frame.
            _updateAllLightVolumeBuffers = _lightVolumeArraysDirty;
            _updateLightVolumeBuffers = false;
            _updateLightVolumeEdgeBuffer = false;
            _updateNeedsVolumeRebuild = false;
            _lightVolumeArraysDirty = false;

            if (hasDynamicTransforms) UpdateAutoUpdatedVolumeChanges();
            if (_updateNeedsVolumeRebuild) {
                _activeShadowCullCount = previousActiveShadowCullCount;
                _updateAllLightVolumeBuffers = false;
                _updateLightVolumeBuffers = false;
                _updateLightVolumeEdgeBuffer = false;
                ResetPointLightArrayUploadState();
                RequestUpdateVolumes();
                return;
            }
            if (previousActiveShadowCullCount != _activeShadowCullCount) ApplyShadowCullEligibilityCountTransition(previousActiveShadowCullCount);
            if (_updateAllLightVolumeBuffers || _updateLightVolumeBuffers || _updateLightVolumeEdgeBuffer || _pointLightArrayUploadMask != 0 || _clusterGeometryUploadPending) UploadAutoUpdatedVolumeChanges();
        }

        // Updates moved dynamic volumes in-place and marks which shader buffer groups need uploading.
        private void UpdateAutoUpdatedVolumeChanges() {
            int enabledCount = _enabledCount;
            int pointLightCount = _pointLightCount;

            // Regular Light Volumes
            for (int i = 0; i < _dynamicLightVolumeCount; i++) {
                LightVolumeInstance instance = _dynamicLightVolumeInstances[i];
                Transform instanceTransform = _dynamicLightVolumeTransforms[i];
                int shaderIndex = _dynamicLightVolumeShaderIndices[i];
                if (instance == null || instanceTransform == null || shaderIndex >= enabledCount) {
                    _updateNeedsVolumeRebuild = true;
                    return;
                }

                Matrix4x4 localToWorldMatrix = instanceTransform.localToWorldMatrix;
                Matrix4x4 previousMatrix = _dynamicLightVolumeMatrices[i];
                if (localToWorldMatrix.Equals(previousMatrix)) continue;

                UpdateLightVolumeTransformData(instance, localToWorldMatrix, shaderIndex);
                _dynamicLightVolumeMatrices[i] = localToWorldMatrix;
                _updateLightVolumeBuffers = true;
            }

            // Point Light Volumes
            for (int i = 0; i < _dynamicPointLightVolumeCount; i++) {
                PointLightVolumeInstance instance = _dynamicPointLightVolumeInstances[i];
                Transform instanceTransform = _dynamicPointLightVolumeTransforms[i];
                int shaderIndex = _dynamicPointLightVolumeShaderIndices[i];
                if (instance == null || instanceTransform == null || shaderIndex >= pointLightCount) {
                    _updateNeedsVolumeRebuild = true;
                    return;
                }

                Matrix4x4 localToWorldMatrix = instanceTransform.localToWorldMatrix;
                Matrix4x4 previousMatrix = _dynamicPointLightVolumeMatrices[i];
                if (localToWorldMatrix.Equals(previousMatrix)) continue;

                float packedShadowIdAbs = Mathf.Abs(_pointLightCustomId[shaderIndex].y);
                bool hasActiveShadow = packedShadowIdAbs >= 1f && packedShadowIdAbs < DisabledShadingShadowId;
                bool basisUnchanged = localToWorldMatrix.m00 == previousMatrix.m00 && localToWorldMatrix.m01 == previousMatrix.m01 && localToWorldMatrix.m02 == previousMatrix.m02
                    && localToWorldMatrix.m10 == previousMatrix.m10 && localToWorldMatrix.m11 == previousMatrix.m11 && localToWorldMatrix.m12 == previousMatrix.m12
                    && localToWorldMatrix.m20 == previousMatrix.m20 && localToWorldMatrix.m21 == previousMatrix.m21 && localToWorldMatrix.m22 == previousMatrix.m22;
                if (basisUnchanged) {
                    // Translation-only motion is the common case. Preserve all static light data and avoid repeated cross-Udon reads. Shadowed Point/Spot lights only need the exact-origin marker in CustomID.W refreshed; their reprojection basis remains unchanged.
                    Vector3 position = localToWorldMatrix.GetPosition();
                    instance.Position = position;
                    Vector4 positionData = _pointLightPosition[shaderIndex];
                    if (positionData.x != position.x || positionData.y != position.y || positionData.z != position.z) {
                        positionData.x = position.x;
                        positionData.y = position.y;
                        positionData.z = position.z;
                        _pointLightPosition[shaderIndex] = positionData;
                        _clusterMaskDirty = true;
                        _clusterGeometryUploadPending = true;
                        MarkPointLightArrayUploads(PointLightUploadPosition);
                    }
                    if (hasActiveShadow) {
                        if (instance.LightType != 2) { // 2: area keeps its cookie-mirror payload in CustomID.W
                            float nearClip = Mathf.Max(instance.NearClip, 0.0001f);
                            float requestedFarClip = instance.BakedFarClip > 0f ? instance.BakedFarClip : instance.FarClip;
                            float resolvedFarClip = requestedFarClip > 0f ? Mathf.Max(requestedFarClip, nearClip + 0.0001f) : Mathf.Sqrt(Mathf.Max(instance.SquaredRange, 0.000001f));
                            float inverseDepthRange = 1f / Mathf.Max(resolvedFarClip - nearClip, 0.0001f);
                            Vector3 bakePosition = instance.ShadowBakePosition;
                            bool reuseWorldShadowOrigin = instance.WorldSpaceShadows && bakePosition.x == position.x && bakePosition.y == position.y && bakePosition.z == position.z;
                            Vector4 customId = _pointLightCustomId[shaderIndex];
                            float customIdW = reuseWorldShadowOrigin ? -inverseDepthRange : inverseDepthRange;
                            if (customId.w != customIdW) {
                                customId.w = customIdW;
                                _pointLightCustomId[shaderIndex] = customId;
                                MarkPointLightArrayUploads(PointLightUploadCustomId);
                                Vector4 shadowMetadata = _froxelShadowMetadata[shaderIndex];
                                if (shadowMetadata.x != 0f) {
                                    float encodedInverseDepthRange = reuseWorldShadowOrigin ? -Mathf.Abs(shadowMetadata.z) : Mathf.Abs(shadowMetadata.z);
                                    shadowMetadata.z = encodedInverseDepthRange;
                                    _froxelShadowMetadata[shaderIndex] = shadowMetadata;
                                    MarkPointLightArrayUploads(PointLightUploadFroxelShadowMetadata);
                                    _clusterMaskDirty = true;
                                }
                            }
                        }
                    }
                } else {
                    UpdatePointLightTransformData(instance, instanceTransform, localToWorldMatrix, false);
                    WritePointLightShaderDataTracked(shaderIndex, _enabledPointIDs[shaderIndex], instance, instanceTransform);
                }
                _dynamicPointLightVolumeMatrices[i] = localToWorldMatrix;
            }
        }

        // Uploads either all regular arrays for a generic change or only transform groups for auto-movement.
        private void UploadAutoUpdatedVolumeChanges() {
            if ((_updateAllLightVolumeBuffers || _updateLightVolumeBuffers || _updateLightVolumeEdgeBuffer) && _enabledCount != 0) {
                if (_updateAllLightVolumeBuffers) {
                    VRCShader.SetGlobalVectorArray(_lightVolumeInvLocalEdgeSmoothID, _invLocalEdgeSmooth);
                    VRCShader.SetGlobalVectorArray(_lightVolumeUvwScaleID, _boundsUvwScale);
                    VRCShader.SetGlobalVectorArray(_lightVolumeUvwID, _boundsUvw);
                } else if (_updateLightVolumeEdgeBuffer) {
                    VRCShader.SetGlobalVectorArray(_lightVolumeInvLocalEdgeSmoothID, _invLocalEdgeSmooth);
                }
                VRCShader.SetGlobalMatrixArray(_lightVolumeInvWorldMatrixID, _invWorldMatrix);
                VRCShader.SetGlobalVectorArray(_lightVolumeRotationID, _relativeRotation);
                VRCShader.SetGlobalVectorArray(_lightVolumeColorID, _colors);
            }
            int pointLightUploadMask = _pointLightArrayUploadMask;
            if (pointLightUploadMask != 0 && _pointLightCount != 0) {
                if ((pointLightUploadMask & PointLightUploadPosition) != 0) VRCShader.SetGlobalVectorArray(_pointLightPositionID, _pointLightPosition);
                if ((pointLightUploadMask & PointLightUploadColor) != 0) VRCShader.SetGlobalVectorArray(_pointLightColorID, _pointLightColor);
                if ((pointLightUploadMask & PointLightUploadExtraData) != 0) VRCShader.SetGlobalVectorArray(_pointLightExtraDataID, _pointLightExtraData);
                if ((pointLightUploadMask & PointLightUploadDirection) != 0) VRCShader.SetGlobalVectorArray(_pointLightDirectionID, _pointLightDirection);
                if ((pointLightUploadMask & PointLightUploadCustomId) != 0) VRCShader.SetGlobalVectorArray(_pointLightCustomIdID, _pointLightCustomId);
                if ((pointLightUploadMask & PointLightUploadShadowReprojection) != 0) VRCShader.SetGlobalVectorArray(_pointLightShadowReprojectionDataID, _pointLightShadowReprojectionData);
                if ((pointLightUploadMask & PointLightUploadShadowRotation) != 0) VRCShader.SetGlobalVectorArray(_pointLightShadowRotationDataID, _pointLightShadowRotationData);
                if ((pointLightUploadMask & PointLightUploadFroxelShadowMetadata) != 0) VRCShader.SetGlobalVectorArray(_froxelShadowMetadataID, _froxelShadowMetadata);
            }
            if (_clusterGeometryUploadPending) _clusterMaskDirty = true;
            _clusterGeometryUploadPending = false;
            _updateAllLightVolumeBuffers = false;
            _updateLightVolumeBuffers = false;
            _updateLightVolumeEdgeBuffer = false;
            ResetPointLightArrayUploadState();
        }

#if UDONSHARP
        // Internal method to auto update volume data and runtime textures every frame while needed
        public void UpdateProcess() {
            if (!enabled || !gameObject.activeInHierarchy) {
                _isUpdateProcessRunning = false;
                return;
            }
            bool keepUpdating;
#else
        // Internal coroutine to auto update volume data and runtime textures every frame while needed
        private IEnumerator UpdateCoroutine() {
            bool keepUpdating;
            do {
                yield return null;
#endif

            // Structural rebuilds remain on the delayed maintenance path. Both regular and Point incremental records have exactly one consumer: PostLateUpdate. Letting a persistent
            // texture loop consume regular dirty data here would reintroduce the same phase race previously removed from the Point queue.
            bool updateVolumes = _volumeDataUpdateRequested;
            _volumeDataUpdateRequested = false;
            if (updateVolumes) UpdateVolumes();

            // Texture section: auto-updates only cached texture sources, without touching point light components
            if (AutoUpdateTextures) {
                bool rebuiltCustomTextures = !_customTexturesInitialized;
                bool rebuiltShadowTextures = !_shadowTexturesInitialized && !_shadowTextureAllocationFailed;
                if (rebuiltCustomTextures) ReinitializeCustomTextures();
                if (rebuiltShadowTextures) ReinitializeShadowTextures();
                // A full rebuild already copied every animated source in this tick.
                if (!rebuiltCustomTextures && HasAutoCustomTextureUpdates) UpdateAutoCustomTextures();
                if (!rebuiltShadowTextures && !_shadowTextureAllocationFailed && HasAutoShadowTextureUpdates) UpdateAutoShadowTextures();
            }

            bool bakeInGameQueueActive = ProcessBakeInGameQueueStep();
            keepUpdating = _volumeDataUpdateRequested || bakeInGameQueueActive || AutoUpdateTextures && (HasAutoCustomTextureUpdates || (HasAutoShadowTextureUpdates && !_shadowTextureAllocationFailed));

            // Keep the delayed loop alive only for continuous monitoring; one-shot requests schedule their own tick.
#if UDONSHARP
            if (keepUpdating) SendCustomEventDelayedFrames(nameof(UpdateProcess), 1);
            else _isUpdateProcessRunning = false;
#else
            } while (isActiveAndEnabled && keepUpdating);
            _updateCoroutine = null;
#endif
        }

#endregion

#region Shader Buffer Rebuild And Upload

        // Writes one regular Light Volume into the compact shader upload buffers
        private void WriteLightVolumeShaderData(int shaderIndex, LightVolumeInstance instance) {

            int i2 = shaderIndex * 2;
            int i3 = shaderIndex * 3;
            int i6 = shaderIndex * 6;

            _invWorldMatrix[shaderIndex] = instance.InvWorldMatrix;
            _invLocalEdgeSmooth[shaderIndex] = instance.InvLocalEdgeSmoothing;

            Vector4 c = instance.Color.linear * instance.Intensity;
            c.w = instance.IsRotated ? 1 : 0;
            _colors[shaderIndex] = c;

            _relativeRotation[i2] = instance.RelativeRotationRow0;
            _relativeRotation[i2 + 1] = instance.RelativeRotationRow1;

            Vector4 uvwMin0 = instance.BoundsUvwMin0;
            Vector4 uvwMin1 = instance.BoundsUvwMin1;
            Vector4 uvwMin2 = instance.BoundsUvwMin2;
            _boundsUvwScale[i3] = uvwMin0;
            _boundsUvwScale[i3 + 1] = uvwMin1;
            _boundsUvwScale[i3 + 2] = uvwMin2;

            Vector4 uvwScale = new Vector4(uvwMin0.w, uvwMin1.w, uvwMin2.w, 0);
            uvwMin0.w = 0;
            uvwMin1.w = 0;
            uvwMin2.w = 0;

            _boundsUvw[i6] = uvwMin0;
            _boundsUvw[i6 + 1] = uvwMin0 + uvwScale;
            _boundsUvw[i6 + 2] = uvwMin1;
            _boundsUvw[i6 + 3] = uvwMin1 + uvwScale;
            _boundsUvw[i6 + 4] = uvwMin2;
            _boundsUvw[i6 + 5] = uvwMin2 + uvwScale;
        }

        // Uses exact component comparisons for packed GPU data. Unity's Vector4 == operator is approximate and could hide a small value change that still needs to reach the shader.
        private static bool PackedVectorChanged(Vector4 previous, Vector4 current) {
            return previous.x != current.x || previous.y != current.y || previous.z != current.z || previous.w != current.w;
        }

        // One mask avoids redundant 128-element SetGlobal copies without adding parallel Udon state.
        private void MarkPointLightArrayUploads(int uploadMask) {
            if (uploadMask == 0) return;
            _pointLightArrayUploadMask |= uploadMask;
        }

        private void ResetPointLightArrayUploadState() {
            _pointLightArrayUploadMask = 0;
        }

        // Incremental updates compare the final packed slot and upload only arrays whose values changed. Full rebuilds call WritePointLightShaderData directly and avoid this bookkeeping.
        private void WritePointLightShaderDataTracked(int shaderIndex, int sourceIndex, PointLightVolumeInstance instance, Transform instanceTransform) {
            Vector4 previousPosition = _pointLightPosition[shaderIndex];
            Vector4 previousColor = _pointLightColor[shaderIndex];
            Vector4 previousExtraData = _pointLightExtraData[shaderIndex];
            Vector4 previousDirection = _pointLightDirection[shaderIndex];
            Vector4 previousCustomId = _pointLightCustomId[shaderIndex];
            Vector4 previousShadowReprojection = _pointLightShadowReprojectionData[shaderIndex];
            Vector4 previousShadowRotation = _pointLightShadowRotationData[shaderIndex];
            Vector4 previousFroxelShadowMetadata = _froxelShadowMetadata[shaderIndex];
            bool previousShadowCullEligible = previousFroxelShadowMetadata.x != 0f;

            WritePointLightShaderData(shaderIndex, sourceIndex, instance, instanceTransform, false);
            bool shadowCullEligible = _froxelShadowMetadata[shaderIndex].x != 0f;
            if (previousShadowCullEligible != shadowCullEligible) {
                if (shadowCullEligible) _activeShadowCullCount++;
                else if (_activeShadowCullCount > 0) _activeShadowCullCount--;
            }

            int uploadMask = 0;
            if (PackedVectorChanged(previousPosition, _pointLightPosition[shaderIndex])) uploadMask |= PointLightUploadPosition;
            if (PackedVectorChanged(previousColor, _pointLightColor[shaderIndex])) uploadMask |= PointLightUploadColor;
            if (PackedVectorChanged(previousExtraData, _pointLightExtraData[shaderIndex])) uploadMask |= PointLightUploadExtraData;
            if (PackedVectorChanged(previousDirection, _pointLightDirection[shaderIndex])) uploadMask |= PointLightUploadDirection;
            if (PackedVectorChanged(previousCustomId, _pointLightCustomId[shaderIndex])) uploadMask |= PointLightUploadCustomId;
            if (PackedVectorChanged(previousShadowReprojection, _pointLightShadowReprojectionData[shaderIndex])) uploadMask |= PointLightUploadShadowReprojection;
            if (PackedVectorChanged(previousShadowRotation, _pointLightShadowRotationData[shaderIndex])) uploadMask |= PointLightUploadShadowRotation;
            if (PackedVectorChanged(previousFroxelShadowMetadata, _froxelShadowMetadata[shaderIndex])) uploadMask |= PointLightUploadFroxelShadowMetadata;
            MarkPointLightArrayUploads(uploadMask);
        }

        // Applies one aggregate resource transition after an incremental batch or full compact rebuild. This preserves a valid hierarchy when eligible lights merely exchange slots in the same frame and the batch's final count is unchanged.
        private void ApplyShadowCullEligibilityCountTransition(int previousCount) {
            if (previousCount == 0 && _activeShadowCullCount > 0) {
                InvalidateShadowCullPyramid();
            } else if (previousCount > 0 && _activeShadowCullCount == 0) {
                ReleaseShadowCullPyramidTextures();
                _shadowCullPyramidDirty = false;
                _clusterMaskDirty = true;
            }
        }

        // Writes one Point Light Volume into the compact shader upload buffers
        private void WritePointLightShaderData(int shaderIndex, int sourceIndex, PointLightVolumeInstance instance, Transform instanceTransform, bool countActiveShadow) {
            if (instance.IsRangeDirty) ComputePointLightRange(instance);

            // Caching point light instance data
            int lightType = instance.LightType;
            int projectionMode = instance.ProjectionMode;
            float squaredScale = instance.SquaredScale;
            float squaredRange = instance.SquaredRange;
            Vector4 pos = instance.Position;
            // Point light type
            bool isSpot = lightType == 1; // 1: spot light
            bool isArea = lightType == 2; // 2: area light
            bool isLut = projectionMode == 1; // 1: LUT projection
            bool isCustomCookie = projectionMode == 2; // 2: custom cookie or cubemap projection
            float spotOuterTangent = 0f;
            float clusterOuterTangent = 0f;
            float spotOuterCosine = 1f;
            float spotCookieAspect = 1f;
            Vector3 clusterAxis = Vector3.forward;
            Vector4 directionData = Vector4.zero;
            if (isSpot) {
                spotOuterTangent = instance.OuterAngleTan;
                clusterOuterTangent = spotOuterTangent;
                if (isCustomCookie) {
                    spotCookieAspect = Mathf.Max(Mathf.Abs(instance.SpotCookieAspect), 0.001f);
                    // The cookie is a rectangular pyramid. Cluster against its circumscribed cone to avoid false negatives.
                    float inverseAspect = 1f / spotCookieAspect;
                    clusterOuterTangent *= Mathf.Sqrt(1f + inverseAspect * inverseAspect);
                    Quaternion rotation = instance.Rotation;
                    directionData = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
                    clusterAxis = Quaternion.Inverse(rotation) * Vector3.forward;
                } else {
                    Vector3 direction = instance.Direction;
                    spotOuterCosine = instance.OuterAngleCos;
                    clusterAxis = direction;
                    directionData = new Vector4(direction.x, direction.y, direction.z, instance.ConeFalloff);
                }
            } else if (isArea || isCustomCookie) {
                Quaternion rotation = instance.Rotation;
                directionData = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
                if (isArea) clusterAxis = rotation * Vector3.forward;
            }
            _pointLightDirection[shaderIndex] = directionData;
            int resolvedCustomId = sourceIndex < _pointLightCustomIDs.Length ? _pointLightCustomIDs[sourceIndex] : -1;
            bool hasAreaCookie = isArea && isCustomCookie && resolvedCustomId >= 0;

            float angleData;
            if (isArea) {
                float height = Mathf.Max(Mathf.Abs(instance.Height), 0.001f);
                pos.w = Mathf.Max(Mathf.Abs(instance.Width), 0.001f);
                angleData = 2f + height;
            } else {
                float typeSign = isSpot ? -1f : 1f;
                if (isLut) pos.w = typeSign * instance.InverseSquaredRange / Mathf.Max(squaredScale, 0.000001f);
                else {
                    float lightSourceSize = instance.LightSourceSize;
                    pos.w = typeSign * lightSourceSize * lightSourceSize * squaredScale;
                }
                if (isSpot && isCustomCookie) angleData = spotOuterTangent;
                else angleData = isSpot ? spotOuterCosine : instance.OuterAngleCos;
            }
            Vector4 previousPosition = _pointLightPosition[shaderIndex];
            Vector4 previousExtraData = _pointLightExtraData[shaderIndex];
            Vector4 previousShadowReprojection = _pointLightShadowReprojectionData[shaderIndex];
            Vector4 previousShadowRotation = _pointLightShadowRotationData[shaderIndex];
            Vector4 previousFroxelShadowMetadata = _froxelShadowMetadata[shaderIndex];
            if (previousPosition.x != pos.x || previousPosition.y != pos.y || previousPosition.z != pos.z) {
                _clusterMaskDirty = true;
                _clusterGeometryUploadPending = true;
            }
            _pointLightPosition[shaderIndex] = pos;

            Vector4 lightColor = instance.Color.linear * instance.Intensity;
            Vector4 extraData = lightColor;
            if (isSpot && isCustomCookie) extraData.x = spotCookieAspect;
            extraData.w = 0f;
            Vector4 color = lightColor;
            if (isArea && isCustomCookie && resolvedCustomId >= CubemapsCount) {
                Color averageColor = sourceIndex < _pointLightAreaCookieAverageColors.Length ? _pointLightAreaCookieAverageColors[sourceIndex] : Color.clear;
                if (averageColor.a <= 0f) averageColor = Color.white;
                color.x = extraData.x * averageColor.r;
                color.y = extraData.y * averageColor.g;
                color.z = extraData.z * averageColor.b;
            }
            color.w = angleData;
            _pointLightColor[shaderIndex] = color;

            float shaderCustomId = 0;
            if (resolvedCustomId >= 0) {
                // Match the v2 shader ABI: point LUT uses the positive ID directly, while spot LUT subtracts one in shader.
                if (isLut) shaderCustomId = isSpot ? resolvedCustomId + 1 : resolvedCustomId;
                else if (isCustomCookie) shaderCustomId = -resolvedCustomId - 1;
            }
            int resolvedShadowId = sourceIndex < _pointLightShadowIDs.Length ? _pointLightShadowIDs[sourceIndex] : -1;
            int shadowSourceType = sourceIndex < _shadowSourceTypes.Length ? _shadowSourceTypes[sourceIndex] : 0;
            float shadingStrength = Mathf.Clamp01(instance.ShadingStrength);
            bool hasShading = shadingStrength > 0f;
            bool hasShadow = instance.Shadows && hasShading && ShadowTextures != null && !_shadowTextureAllocationFailed && ShadowMapsCount > 0 && resolvedShadowId >= 0 && resolvedShadowId < ShadowMapsCount;
            bool shadowCullEligible = hasShadow && shadowSourceType > 0 && shadowSourceType < 5 && !instance.RuntimeShadowDirectOutput && shadingStrength >= 1f && squaredRange > 0f;
            if (countActiveShadow) {
                if (hasShadow) _activeShadowCount++;
                if (shadowCullEligible) _activeShadowCullCount++;
            }
            float shadowNearClip = 0f;
            float shadowInvDepthRange = 0f;
            float shadowDepthRange = 0f;
            bool useLocalSpaceShadows = false;
            if (hasShadow) {
                shadowNearClip = Mathf.Max(instance.NearClip, 0.0001f);
                float requestedFarClip = instance.BakedFarClip > 0f ? instance.BakedFarClip : instance.FarClip;
                float resolvedFarClip = requestedFarClip > 0f ? Mathf.Max(requestedFarClip, shadowNearClip + 0.0001f) : Mathf.Sqrt(Mathf.Max(squaredRange, 0.000001f));
                if (shadowNearClip >= resolvedFarClip) resolvedFarClip = shadowNearClip + 0.0001f;
                // Far is needed by the bake/encoder, but the receiver only needs its precomputed reciprocal range.
                shadowDepthRange = Mathf.Max(resolvedFarClip - shadowNearClip, 0.0001f);
                shadowInvDepthRange = 1f / shadowDepthRange;
                useLocalSpaceShadows = !instance.WorldSpaceShadows;
            }
            extraData.w = shadowNearClip;
            float shadowMapID = DisabledShadingShadowId;
            if (hasShading) {
                shadowMapID = hasShadow ? (useLocalSpaceShadows ? -resolvedShadowId - 1f : resolvedShadowId + 1f) : 0f;
                float shadingFade = 1f - shadingStrength;
                if (shadingFade > 0f) shadowMapID += shadowMapID < 0f ? -shadingFade : shadingFade;
            }
            WriteClusteringLight(shaderIndex, squaredRange, lightType, clusterOuterTangent, clusterAxis, shadowCullEligible);

            float customDataW = 0f;
            bool usesCubemapShadow = false;
            bool useCurrentLightOrigin = false;
            Quaternion shadowRotation = Quaternion.identity;
            if (hasAreaCookie) {
                float areaCookieMirror = instance.AreaCookieMirror;
                customDataW = Mathf.Abs(areaCookieMirror) >= 0.5f ? areaCookieMirror : 1f;
            }
            if (hasShadow) {
                usesCubemapShadow = resolvedShadowId < ShadowCubemapsCount;
                Vector3 shadowBakePosition = instance.ShadowBakePosition;
                // A negative reciprocal range is a v3-only fast-path marker: the baked world-space shadow origin exactly matches the current Point/Spot origin, so the receiver can
                // reuse its raw light vector and distance. Compare components directly. Unity's Vector3 == is approximate and could incorrectly select this exact path.
                bool reuseWorldShadowOrigin = !isArea && !useLocalSpaceShadows && shadowInvDepthRange > 0f && shadowBakePosition.x == pos.x && shadowBakePosition.y == pos.y && shadowBakePosition.z == pos.z;
                useCurrentLightOrigin = useLocalSpaceShadows || reuseWorldShadowOrigin;
                // V2 declares CustomID as float3 and ignores W. Keep the full reciprocal range for every v3 Point/Spot shadow; abs(W) is the value and sign(W) is the fast-path marker.
                if (!isArea) customDataW = reuseWorldShadowOrigin ? -shadowInvDepthRange : shadowInvDepthRange;

                float shadowTanAngle = spotOuterTangent;
                // Local single-slice Spot receivers fetch the tangent from otherwise unused ExtraData.Y.
                if (isSpot && !usesCubemapShadow) extraData.y = shadowTanAngle;
                float shadowReprojectionW = usesCubemapShadow ? -shadowInvDepthRange : shadowTanAngle;
                _pointLightShadowReprojectionData[shaderIndex] = new Vector4(shadowBakePosition.x, shadowBakePosition.y, shadowBakePosition.z, shadowReprojectionW);

                if (useLocalSpaceShadows) {
                    Transform shadowTransform = instanceTransform;
                    if (shadowTransform == null) shadowTransform = instance.transform;
                    shadowRotation = Quaternion.Inverse(shadowTransform.rotation);
                } else {
                    shadowRotation = Quaternion.Inverse(instance.ShadowBakeRotation);
                }
                // Both the receiver and conservative froxel projection use the quaternion-vector shortcut that assumes a unit quaternion. Normalize authored/scripted bake data once during upload so the shadow transform cannot stretch a froxel sphere.
                float shadowRotationLengthSq = shadowRotation.x * shadowRotation.x + shadowRotation.y * shadowRotation.y + shadowRotation.z * shadowRotation.z + shadowRotation.w * shadowRotation.w;
                if (shadowRotationLengthSq > 0.000000000001f && shadowRotationLengthSq < 1000000000000f) {
                    float inverseShadowRotationLength = 1f / Mathf.Sqrt(shadowRotationLengthSq);
                    shadowRotation = new Quaternion(shadowRotation.x * inverseShadowRotationLength, shadowRotation.y * inverseShadowRotationLength, shadowRotation.z * inverseShadowRotationLength, shadowRotation.w * inverseShadowRotationLength);
                } else {
                    shadowRotation = Quaternion.identity;
                }
                _pointLightShadowRotationData[shaderIndex] = new Vector4(shadowRotation.x, shadowRotation.y, shadowRotation.z, shadowRotation.w);
            }
            if (shadowCullEligible) {
                int shadowBaseSlice = usesCubemapShadow ? resolvedShadowId * 6 : resolvedShadowId + ShadowCubemapsCount * 5;
                float encodedShadowBaseSlice = shadowBaseSlice + 1f;
                if (useLocalSpaceShadows) encodedShadowBaseSlice = -encodedShadowBaseSlice;
                float encodedNearClip = usesCubemapShadow ? shadowNearClip : -shadowNearClip;
                float encodedInverseDepthRange = useCurrentLightOrigin ? -shadowInvDepthRange : shadowInvDepthRange;
                bool identityShadowRotation = shadowRotation.x == 0f && shadowRotation.y == 0f && shadowRotation.z == 0f;
                float encodedDepthRange = identityShadowRotation ? -shadowDepthRange : shadowDepthRange;
                _froxelShadowMetadata[shaderIndex] = new Vector4(encodedShadowBaseSlice, encodedNearClip, encodedInverseDepthRange, encodedDepthRange);
            } else {
                _froxelShadowMetadata[shaderIndex] = Vector4.zero;
            }
            _pointLightCustomId[shaderIndex] = new Vector4(shaderCustomId, shadowMapID, squaredRange, customDataW);
            _pointLightExtraData[shaderIndex] = extraData;

            if (ShadowCulling) {
                Vector4 currentFroxelShadowMetadata = _froxelShadowMetadata[shaderIndex];
                bool shadowMetadataChanged = PackedVectorChanged(previousFroxelShadowMetadata, currentFroxelShadowMetadata);
                if (!shadowMetadataChanged && currentFroxelShadowMetadata.x != 0f) {
                    if (currentFroxelShadowMetadata.z > 0f) shadowMetadataChanged = PackedVectorChanged(previousShadowReprojection, _pointLightShadowReprojectionData[shaderIndex]);
                    if (!shadowMetadataChanged && currentFroxelShadowMetadata.w > 0f) shadowMetadataChanged = PackedVectorChanged(previousShadowRotation, _pointLightShadowRotationData[shaderIndex]);
                    if (!shadowMetadataChanged && currentFroxelShadowMetadata.y < 0f && currentFroxelShadowMetadata.z < 0f) shadowMetadataChanged = previousExtraData.y != extraData.y;
                }
                if (shadowMetadataChanged) _clusterMaskDirty = true;
            }

        }

        // Reconciles serialized activity once when the Manager is enabled. Objects below an inactive parent do not receive an initial OnDisable callback.
        private void ReconcileRegistryActiveStates() {
            int lightVolumeCount = LightVolumeInstances.Length;
            for (int i = 0; i < lightVolumeCount; i++) {
                LightVolumeInstance instance = LightVolumeInstances[i];
                if (instance == null) continue;
                instance.IsActive = instance.enabled && instance.gameObject.activeInHierarchy && instance.Intensity != 0f && instance.Color != Color.black;
            }

            bool customTexturesChanged = false;
            bool shadowTexturesChanged = false;
            int pointLightCount = PointLightVolumeInstances.Length;
            for (int i = 0; i < pointLightCount; i++) {
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null) continue;
                bool isActive = instance.enabled && instance.gameObject.activeInHierarchy && instance.Intensity != 0f && instance.Color != Color.black;
                if (instance.IsActive == isActive) continue;

                instance.IsActive = isActive;
                if (instance.CustomTexture != null || instance.CustomTextureMaterial != null) customTexturesChanged = true;
                if (instance.ShadowMapID >= 0f || instance.ShadowMapTexture != null || instance.ShadowMapMaterial != null) shadowTexturesChanged = true;
            }
            InvalidateTextureCaches(customTexturesChanged, shadowTexturesChanged);
        }

        // Recalculates all volume data immediately. Automatic runtime paths should call RequestUpdateVolumes instead
        public void UpdateVolumes() {

#if !COMPILER_UDONSHARP && UDONSHARP && UNITY_EDITOR
            if (ShouldSkipEditorProxyRuntimeUpdate()) return;
#endif
            if (_isUpdatingVolumes) return;
            _volumeDataUpdateRequested = false;
            _isUpdatingVolumes = true;
#if !COMPILER_UDONSHARP
            try {
#endif
            SanitizeRegistries();
            ResetPendingPointLightChanges();
            int pointLightRegistryCount = PointLightVolumeInstances.Length;
            // This array is a reusable capacity buffer. Shrinking the public registry must not allocate a second exact-length map that will be discarded when lights are enabled again.
            if (_pointLightRegistryToShaderIndex.Length < pointLightRegistryCount) _pointLightRegistryToShaderIndex = new int[pointLightRegistryCount];
            for (int i = 0; i < pointLightRegistryCount; i++) _pointLightRegistryToShaderIndex[i] = -1;
            TryInitialize();

            if (!enabled || !gameObject.activeInHierarchy) {
                SetDisabledShaderState();
                _updateAllLightVolumeBuffers = false;
                _updateLightVolumeBuffers = false;
                _updateLightVolumeEdgeBuffer = false;
                ResetPointLightArrayUploadState();
                _updateNeedsVolumeRebuild = false;
                _isUpdatingVolumes = false;
                return;
            }

            bool isAtlas = LightVolumeAtlas != null;
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            // Editor tests and inspector edits can change fields directly without going through instance notify methods.
            if (!Application.isPlaying) {
                ReconcileRegistryActiveStates();
                bool customTexturesChanged = CaptureEditorCustomSourceState();
                bool shadowTexturesChanged = CaptureEditorShadowSourceState();
                InvalidateTextureCaches(customTexturesChanged, shadowTexturesChanged);
            }
#endif

            // Rebuild runtime texture caches before point light shader IDs are written
            if (!_customTexturesInitialized) ReinitializeCustomTextures();
            if (!_shadowTexturesInitialized && !_shadowTextureAllocationFailed) RebuildShadowTextures();

            // Rebuild regular Light Volume shader buffers and dynamic transform cache
            _enabledCount = 0;
            _additiveCount = 0;
            _dynamicLightVolumeCount = 0;
            if (isAtlas) {
                int selectedLightVolumeCount = SelectLightVolumesByWeight();
                for (int additivePass = 0; additivePass < 2 && _enabledCount < selectedLightVolumeCount; additivePass++) {
                    bool isAdditivePass = additivePass == 0;
                    for (int i = 0; i < selectedLightVolumeCount; i++) {
                        int registryIndex = _selectedLightVolumeIDs[i];
                        LightVolumeInstance instance = LightVolumeInstances[registryIndex];
                        if (instance == null) continue;
                        if (!instance.IsActive || instance.IsAdditive != isAdditivePass) continue;
                        if (instance.IsDynamic) {
                            Transform instanceTransform = instance.transform;
                            Matrix4x4 localToWorldMatrix = instanceTransform.localToWorldMatrix;
                            UpdateLightVolumeTransformData(instance, localToWorldMatrix, -1);
                            if (_dynamicLightVolumeCount < MaxLightVolumeCount) {
                                _dynamicLightVolumeInstances[_dynamicLightVolumeCount] = instance;
                                _dynamicLightVolumeTransforms[_dynamicLightVolumeCount] = instanceTransform;
                                _dynamicLightVolumeShaderIndices[_dynamicLightVolumeCount] = _enabledCount;
                                _dynamicLightVolumeMatrices[_dynamicLightVolumeCount] = localToWorldMatrix;
                                _dynamicLightVolumeCount++;
                            }
                        }
#if !COMPILER_UDONSHARP
                        else if (!Application.isPlaying) UpdateLightVolumeTransformData(instance, instance.transform.localToWorldMatrix, -1);
#endif
                        _enabledIDs[_enabledCount] = registryIndex;
                        if (isAdditivePass) _additiveCount++;
                        WriteLightVolumeShaderData(_enabledCount, instance);
                        _enabledCount++;
                    }
                }
            }
            _lightVolumeArraysDirty = false;

            // Rebuild Point Light Volume shader buffers and dynamic transform cache
            if (_prevLightsBrightnessCutoff != LightsBrightnessCutoff) {
                _prevLightsBrightnessCutoff = LightsBrightnessCutoff;
                _isRangeDirty = true;
            }
            int previousPointLightCount = _pointLightCount;
            int previousActiveShadowCullCount = _activeShadowCullCount;
            _pointLightCount = 0;
            _activeShadowCount = 0;
            _activeShadowCullCount = 0;
            _dynamicPointLightVolumeCount = 0;
            for (int registryIndex = 0; registryIndex < pointLightRegistryCount && _pointLightCount < MaxPointLightCount; registryIndex++) {
                PointLightVolumeInstance instance = PointLightVolumeInstances[registryIndex];
                if (instance == null) continue;
                if (!instance.IsActive) continue;
                Transform instanceTransform = null;
                if (instance.IsDynamic) {
                    instanceTransform = instance.transform;
                    Matrix4x4 localToWorldMatrix = instanceTransform.localToWorldMatrix;
                    UpdatePointLightTransformData(instance, instanceTransform, localToWorldMatrix, true);
                    if (_dynamicPointLightVolumeCount < MaxPointLightCount) {
                        _dynamicPointLightVolumeInstances[_dynamicPointLightVolumeCount] = instance;
                        _dynamicPointLightVolumeTransforms[_dynamicPointLightVolumeCount] = instanceTransform;
                        _dynamicPointLightVolumeShaderIndices[_dynamicPointLightVolumeCount] = _pointLightCount;
                        _dynamicPointLightVolumeMatrices[_dynamicPointLightVolumeCount] = localToWorldMatrix;
                        _dynamicPointLightVolumeCount++;
                    }
                }
#if !COMPILER_UDONSHARP
                else if (!Application.isPlaying) {
                    instanceTransform = instance.transform;
                    UpdatePointLightTransformData(instance, instanceTransform, instanceTransform.localToWorldMatrix, true);
                }
#endif
                if (_isRangeDirty || instance.IsRangeDirty) ComputePointLightRange(instance);
                _enabledPointIDs[_pointLightCount] = registryIndex;
                _pointLightRegistryToShaderIndex[registryIndex] = _pointLightCount;
                WritePointLightShaderData(_pointLightCount, registryIndex, instance, instanceTransform, true);
                _pointLightCount++;
            }
            if (previousPointLightCount != _pointLightCount) _clusterMaskDirty = true;
            // Only exact full-strength receivers can consume the proof. Avoid both a hierarchy build and its persistent allocation when every visible shadow is partially blended.
            if (previousActiveShadowCullCount != _activeShadowCullCount) ApplyShadowCullEligibilityCountTransition(previousActiveShadowCullCount);
            _isRangeDirty = false;

            // Upload scalar shader globals and disable the system if no shader-visible data remains
            int lightVolumeCount = isAtlas ? _enabledCount : 0;
            int additiveCount = isAtlas ? _additiveCount : 0;
            VRCShader.SetGlobalFloat(_lightVolumeVersionID, Version);
            if (lightVolumeCount == 0 && _pointLightCount == 0) {
                SetDisabledShaderState();
            } else {
                if (isAtlas) VRCShader.SetGlobalTexture(_lightVolumeID, LightVolumeAtlas);

                VRCShader.SetGlobalFloat(_lightVolumeCountID, lightVolumeCount);
                VRCShader.SetGlobalFloat(_lightVolumeAdditiveCountID, additiveCount);
                VRCShader.SetGlobalFloat(_lightVolumeOcclusionCountID, 0);
                VRCShader.SetGlobalFloat(_lightVolumeProbesBlendID, LightProbesBlending ? 1 : 0);
                VRCShader.SetGlobalFloat(_lightVolumeSharpBoundsID, SharpBounds ? 1 : 0);
                VRCShader.SetGlobalFloat(_lightVolumeAdditiveMaxOverdrawID, AdditiveMaxOverdraw);

                // Upload regular Light Volume arrays
                if (lightVolumeCount != 0) {
                    VRCShader.SetGlobalVectorArray(_lightVolumeInvLocalEdgeSmoothID, _invLocalEdgeSmooth);
                    VRCShader.SetGlobalVectorArray(_lightVolumeUvwScaleID, _boundsUvwScale);
                    VRCShader.SetGlobalVectorArray(_lightVolumeUvwID, _boundsUvw);
                    VRCShader.SetGlobalMatrixArray(_lightVolumeInvWorldMatrixID, _invWorldMatrix);
                    VRCShader.SetGlobalVectorArray(_lightVolumeRotationID, _relativeRotation);
                    VRCShader.SetGlobalVectorArray(_lightVolumeColorID, _colors);
                }

                // Upload Point Light Volume arrays and runtime texture references
                VRCShader.SetGlobalFloat(_pointLightCountID, _pointLightCount);
                VRCShader.SetGlobalFloat(_pointLightCubeCountID, CubemapsCount);
                int shadowCount = _activeShadowCount > 0 ? ShadowMapsCount : 0;
                VRCShader.SetGlobalFloat(_pointLightShadowCubeCountID, _activeShadowCount > 0 ? ShadowCubemapsCount : 0);
                VRCShader.SetGlobalFloat(_pointLightShadowCountID, shadowCount);
                VRCShader.SetGlobalVector(_pointLightShadowReceiverParamsID, GetPointLightShadowReceiverParams());
                if (_pointLightCount != 0) {
                    VRCShader.SetGlobalVectorArray(_pointLightColorID, _pointLightColor);
                    VRCShader.SetGlobalVectorArray(_pointLightExtraDataID, _pointLightExtraData);
                    VRCShader.SetGlobalVectorArray(_pointLightPositionID, _pointLightPosition);
                    VRCShader.SetGlobalVectorArray(_pointLightDirectionID, _pointLightDirection);
                    VRCShader.SetGlobalVectorArray(_pointLightCustomIdID, _pointLightCustomId);
                    if (_activeShadowCount > 0) {
                        VRCShader.SetGlobalVectorArray(_pointLightShadowReprojectionDataID, _pointLightShadowReprojectionData);
                        VRCShader.SetGlobalVectorArray(_pointLightShadowRotationDataID, _pointLightShadowRotationData);
                        VRCShader.SetGlobalVectorArray(_froxelShadowMetadataID, _froxelShadowMetadata);
                    }
                    VRCShader.SetGlobalFloat(_lightBrightnessCutoffID, LightsBrightnessCutoff);
                }
                if (CustomTextures != null) {
                    VRCShader.SetGlobalTexture(_pointLightTextureID, CustomTextures);
                    VRCShader.SetGlobalFloat(_pointLightTextureTexelCountID, CustomTextures.width * CustomTextures.height);
                    VRCShader.SetGlobalFloat(_pointLightTextureMaxMipID, Mathf.Max(CustomTextures.mipmapCount - 1, 0));
                }
                if (_activeShadowCount > 0 && ShadowTextures != null) VRCShader.SetGlobalTexture(_pointLightShadowTextureID, ShadowTextures);

                VRCShader.SetGlobalFloat(_lightVolumeEnabledID, 1);
            }

            // Finish volume update state
            _updateAllLightVolumeBuffers = false;
            _updateLightVolumeBuffers = false;
            _updateLightVolumeEdgeBuffer = false;
            ResetPointLightArrayUploadState();
            _updateNeedsVolumeRebuild = false;
            _clusterGeometryUploadPending = false;
            if (AutoUpdateTextures && (HasAutoCustomTextureUpdates || HasAutoShadowTextureUpdates)) ScheduleUpdateProcess();
            _isUpdatingVolumes = false;
#if !COMPILER_UDONSHARP
            } finally {
                _isUpdatingVolumes = false;
            }
#endif
        }

#endregion
    }
}
