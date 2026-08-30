#if !UDONSHARP && COMPILER_UDONSHARP
#define UDONSHARP
#endif

using UnityEngine;
using UnityEngine.Rendering;
using System;

#if UDONSHARP
using VRCGraphics = VRC.SDKBase.VRCGraphics;
#if COMPILER_UDONSHARP
using VRC.SDK3.Rendering;
using VRC.Udon.Common.Interfaces;
using VRCShader = VRC.SDKBase.VRCShader;
#else
using VRCShader = UnityEngine.Shader;
#endif
#else
using System.Collections;
using VRCGraphics = UnityEngine.Graphics;
using VRCShader = UnityEngine.Shader;
#endif

namespace VRCLightVolumes {
    public partial class LightVolumeManager {
#region Runtime Texture Caches

        private const int CubemapResampleMaterialPass = 1;

        // Rebuilds the runtime cookie texture array and assigns stable shader-side IDs to all point light instances
        public void ReinitializeCustomTextures() {
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!Application.isPlaying) CaptureEditorCustomSourceState();
#endif
            BuildCustomTextureSourceCache();
            if (_customTextureArrayDepth <= 0) {
                if (CustomTextures != null) {
                    ReleaseRuntimeRenderTexture(CustomTextures);
                    CustomTextures = null;
                }
                _customTexturesInitialized = true;
                return;
            }
            if (!EnsureRuntimeCustomTextures(CustomTexturesWidth, CustomTexturesHeight, _customTextureArrayDepth)) return;
            TryInitialize();
            VRCShader.SetGlobalTexture(_pointLightTextureID, CustomTextures);
            VRCShader.SetGlobalFloat(_pointLightTextureTexelCountID, CustomTextures.width * CustomTextures.height);
            VRCShader.SetGlobalFloat(_pointLightTextureMaxMipID, Mathf.Max(CustomTextures.mipmapCount - 1, 0));
            BlitCustomTextures(false);
            _customTexturesInitialized = true;
            if (AutoUpdateTextures && HasAutoCustomTextureUpdates) ScheduleUpdateProcess();
        }

        // Updates animated render texture and material sources in the final cookie array
        public void UpdateAutoCustomTextures() {
            if (CustomTextures == null) {
                ReinitializeCustomTextures();
                return;
            }
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (_customTexturesUseMipMap && !Application.isPlaying && (!CustomTextures.useMipMap || CustomTextures.autoGenerateMips)) {
                ReinitializeCustomTextures();
                return;
            }
#endif
            BlitCustomTextures(true);
        }

        // Builds deduplicated source arrays and per-instance shader IDs for the runtime cookie texture array
        private void BuildCustomTextureSourceCache() {

            PointLightVolumeInstance[] pointInstances = PointLightVolumeInstances;
            int count = pointInstances.Length;

            // Prepare reusable custom texture source cache arrays for a full rebuild
            if (_pointLightCustomIDs.Length < count || _customCubemapTextureAutoUpdates.Length < count || _customSingleTextureAutoUpdates.Length < count || _customSingleAreaCookieReceivers.Length < count || _customSingleAreaCookieReceiverIndices.Length < count || _pointLightAreaCookieAverageColors.Length < count) {
                _customCubemapTextures = new Texture[count];
                _customCubemapMaterials = new Material[count];
                _customSingleTextures = new Texture[count];
                _customSingleMaterials = new Material[count];
                _customCubemapTextureAutoUpdates = new bool[count];
                _customSingleTextureAutoUpdates = new bool[count];
                _customSingleAreaCookieReceivers = new PointLightVolumeInstance[count];
                _customSingleAreaCookieReceiverIndices = new int[count];
                _pointLightCustomIDs = new int[count];
                _pointLightAreaCookieAverageColors = new Color[count];
            } else {
                for (int i = 0; i < _customCubemapTextureCount; i++) _customCubemapTextures[i] = null;
                for (int i = 0; i < _customCubemapMaterialCount; i++) _customCubemapMaterials[i] = null;
                for (int i = 0; i < _customSingleTextureCount; i++) _customSingleTextures[i] = null;
                for (int i = 0; i < _customSingleMaterialCount; i++) _customSingleMaterials[i] = null;
            }
            // These registry-index mappings are grow-only. Clear the entire retained capacity so a later source-less append cannot inherit the ID that occupied its index before a shrink.
            for (int i = 0; i < _pointLightCustomIDs.Length; i++) {
                _pointLightCustomIDs[i] = -1;
            }
            // The registry can be compacted or reordered independently of this reusable array. Rebuild its index view from the per-instance cache below so a removed light's fallback color can never leak into the light that takes over its old index.
            for (int i = 0; i < _pointLightAreaCookieAverageColors.Length; i++) _pointLightAreaCookieAverageColors[i] = Color.clear;
            int previousSingleSourceCount = _customSingleTextureCount + _customSingleMaterialCount;
            for (int i = 0; i < previousSingleSourceCount; i++) {
                _customSingleAreaCookieReceivers[i] = null;
                _customSingleAreaCookieReceiverIndices[i] = -1;
            }
            HasAutoCustomTextureUpdates = false;
            _customTexturesUseMipMap = false;

            // Projection source counters
            int cubemapTextureCount = 0;
            int cubemapMaterialCount = 0;
            int singleTextureCount = 0;
            int singleMaterialCount = 0;
            bool pointLutUsesFirstSingleTexture = false;
            bool pointLutUsesFirstSingleMaterial = false;

            // Iterate through registry and collect unique texture/material sources in reusable arrays
            for (int i = 0; i < count; i++) {

                PointLightVolumeInstance instance = pointInstances[i];
                if (instance == null || !instance.IsActive) continue;

                int lightType = instance.LightType;

                int projectionMode = instance.ProjectionMode;
                if (projectionMode == 0) continue; // 0: parametric projection has no custom source

                bool usesCubemapProjection = lightType == 0 && projectionMode == 2; // 0: point, 2: custom cookie or cubemap
                bool usesAreaCookieProjection = lightType == 2 && projectionMode == 2; // 2: area, 2: custom cookie
                bool usesPointLutProjection = lightType == 0 && projectionMode == 1; // 0: point, 1: LUT

                Texture textureSource = instance.CustomTexture;
                if (textureSource != null) { // STATIC OR ANIMATED TEXTURE PROJECTION

                    bool autoUpdate = typeof(RenderTexture).IsInstanceOfType(textureSource); // RenderTexture and CustomRenderTexture sources update directly in the final array
                    if (usesAreaCookieProjection) _customTexturesUseMipMap = true;

                    if (usesCubemapProjection) { // TEXTURE CUBEMAP PROJECTION

                        int index = -1;
                        for (int j = 0; j < cubemapTextureCount; j++) {
                            if (_customCubemapTextures[j] == textureSource) {
                                index = j;
                                break;
                            }
                        }
                        if (index < 0) { // Append each unique source once so matching lights share the same texture ID
                            index = cubemapTextureCount;
                            _customCubemapTextures[cubemapTextureCount] = textureSource;
                            _customCubemapTextureAutoUpdates[cubemapTextureCount] = autoUpdate;
                            cubemapTextureCount++;
                        }
                        _pointLightCustomIDs[i] = index << 2; // Pack the local index and source block until final IDs can be assigned.

                    } else { // TEXTURE COOKIE PROJECTION

                        int index = -1;
                        for (int j = 0; j < singleTextureCount; j++) {
                            if (_customSingleTextures[j] == textureSource) {
                                index = j;
                                break;
                            }
                        }
                        if (index < 0) { // Append each unique source once so matching lights share the same texture ID
                            index = singleTextureCount;
                            _customSingleTextures[singleTextureCount] = textureSource;
                            _customSingleTextureAutoUpdates[singleTextureCount] = autoUpdate;
                            singleTextureCount++;
                        }
                        if (usesPointLutProjection && index == 0) pointLutUsesFirstSingleTexture = true;
                        _pointLightCustomIDs[i] = index << 2 | 2;

                    }
                    if (autoUpdate) HasAutoCustomTextureUpdates = true;

                } else { // MATERIAL PROJECTION

                    Material materialSource = instance.CustomTextureMaterial;
                    if (materialSource == null) continue;
                    if (usesAreaCookieProjection) _customTexturesUseMipMap = true;

                    if (usesCubemapProjection) { // MATERIAL CUBEMAP PROJECTION

                        int index = -1;
                        for (int j = 0; j < cubemapMaterialCount; j++) {
                            if (_customCubemapMaterials[j] == materialSource) {
                                index = j;
                                break;
                            }
                        }
                        if (index < 0) { // Append each unique material once so matching lights share the same texture ID
                            index = cubemapMaterialCount;
                            _customCubemapMaterials[cubemapMaterialCount] = materialSource;
                            cubemapMaterialCount++;
                        }
                        _pointLightCustomIDs[i] = index << 2 | 1;

                    } else { // MATERIAL SINGLE SLICE PROJECTION

                        int index = -1;
                        for (int j = 0; j < singleMaterialCount; j++) {
                            if (_customSingleMaterials[j] == materialSource) {
                                index = j;
                                break;
                            }
                        }
                        if (index < 0) { // Append each unique material once so matching lights share the same texture ID
                            index = singleMaterialCount;
                            _customSingleMaterials[singleMaterialCount] = materialSource;
                            singleMaterialCount++;
                        }
                        if (usesPointLutProjection && index == 0) pointLutUsesFirstSingleMaterial = true;
                        _pointLightCustomIDs[i] = index << 2 | 3;

                    }
                    HasAutoCustomTextureUpdates = true;

                }

            }

            _customCubemapTextureCount = cubemapTextureCount;
            _customCubemapMaterialCount = cubemapMaterialCount;
            _customSingleTextureCount = singleTextureCount;
            _customSingleMaterialCount = singleMaterialCount;
            int cubemapsCount = cubemapTextureCount + cubemapMaterialCount;
            CubemapsCount = cubemapsCount;
            int singleSourceIDOffset = cubemapsCount == 0 && (pointLutUsesFirstSingleTexture || singleTextureCount == 0 && pointLutUsesFirstSingleMaterial) ? 1 : 0; // v2 point LUT shaders treat custom ID 0 as parametric.
            _customTextureArrayDepth = cubemapsCount * 6 + singleSourceIDOffset + singleTextureCount + singleMaterialCount;

            // Convert local source indices into final texture-array source IDs and refresh area-cookie fallback source cache after final counts are known
            for (int i = 0; i < count; i++) {
                PointLightVolumeInstance instance = pointInstances[i];
                if (instance == null) continue;
                if (!instance.IsActive) continue;

                int packedSource = _pointLightCustomIDs[i];
                if (packedSource < 0) {
                    if (instance.AreaCookieAverageReadbackPending) {
                        instance.AreaCookieAverageCustomId = -1;
                        instance.AreaCookieAverageReadbackDirty = true;
                    }
                    continue;
                }
                int sourceType = packedSource & 3;
                int index = packedSource >> 2;
                // Cubemap textures already use their final ID. Other blocks follow cubemap textures, all cubemaps, or single textures respectively.
                if (sourceType == 1) index += cubemapTextureCount;
                else if (sourceType == 2) index += cubemapsCount + singleSourceIDOffset;
                else if (sourceType == 3) index += cubemapsCount + singleSourceIDOffset + singleTextureCount;
                _pointLightCustomIDs[i] = index;

                if (index < cubemapsCount || instance.LightType != 2 || instance.ProjectionMode != 2) { // Area cookies always use the single-slice block after all cubemap IDs.
                    if (instance.AreaCookieAverageReadbackPending) {
                        instance.AreaCookieAverageCustomId = -1;
                        instance.AreaCookieAverageReadbackDirty = true;
                    }
                    continue;
                }

                int singleSourceIndex = index - cubemapsCount - singleSourceIDOffset;
                if (singleSourceIndex >= 0 && singleSourceIndex < _customSingleAreaCookieReceivers.Length && _customSingleAreaCookieReceivers[singleSourceIndex] == null) {
                    _customSingleAreaCookieReceivers[singleSourceIndex] = instance;
                    _customSingleAreaCookieReceiverIndices[singleSourceIndex] = i;
                }

                _pointLightAreaCookieAverageColors[i] = instance.AreaLightFallbackColor;
                instance.AreaCookieAverageReadbackDirty = true;
            }

        }

        // Copies custom projection sources into the runtime array. Auto-update passes skip immutable texture assets; material and render texture sources always update.
        private void BlitCustomTextures(bool autoUpdatePass) {
            RenderTexture destination = CustomTextures;
            // Blit each cubemap texture source into 6 array slices
            int cubemapTextureCount = _customCubemapTextureCount;
            for (int i = 0; i < cubemapTextureCount; i++) {
                if (autoUpdatePass && !_customCubemapTextureAutoUpdates[i]) continue;
                // Custom source layout is resolved from the actual texture inside BlitCubemapTexture.
                BlitCubemapTexture(_customCubemapTextures[i], 0, i * 6, destination);
            }

            // Blit each cubemap material source into 6 array slices
            int cubemapMaterialCount = _customCubemapMaterialCount;
            for (int i = 0; i < cubemapMaterialCount; i++) {
                BlitCubemapMaterial(_customCubemapMaterials[i], (cubemapTextureCount + i) * 6, destination);
            }

            // Blit each 1-slice texture source into 1 array slice after cubemap sources
            int singleTextureCount = _customSingleTextureCount;
            int singleMaterialCount = _customSingleMaterialCount;
            int singleBaseSlice = _customTextureArrayDepth - singleTextureCount - singleMaterialCount;
            for (int i = 0; i < singleTextureCount; i++) {
                if (autoUpdatePass && !_customSingleTextureAutoUpdates[i]) continue;
                Texture sourceTexture = _customSingleTextures[i];
                if (sourceTexture == null) continue;
                int targetSlice = singleBaseSlice + i;
                VRCGraphics.Blit(sourceTexture, destination, 0, targetSlice);
            }

            // Blit each 1-slice material source into 1 array slice after texture sources
            for (int i = 0; i < singleMaterialCount; i++) {
                Material sourceMaterial = _customSingleMaterials[i];
                if (sourceMaterial == null) continue;
                int targetSlice = singleBaseSlice + singleTextureCount + i;
                BlitSingleMaterial(sourceMaterial, targetSlice, destination);
            }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
            // Edit-mode fallback readbacks need the freshly blitted last mip immediately.
            if (_customTexturesUseMipMap && CustomTextures != null && !Application.isPlaying && !CustomTextures.autoGenerateMips) CustomTextures.GenerateMips();
            if (!Application.isPlaying) {
                RequestAreaCookieAverageReadbacks(autoUpdatePass);
                return;
            }
#endif
            if (!_customTexturesUseMipMap || CustomTextures == null) return;
            if (!autoUpdatePass) _areaCookieAverageReadbackForceAll = true;
            if (_areaCookieAverageReadbackScheduled) return;
            _areaCookieAverageReadbackScheduled = true;
#if UDONSHARP
            SendCustomEventDelayedFrames(nameof(_RequestAreaCookieAverageReadbacks), 1);
#else
            StartCoroutine(DelayedAreaCookieAverageReadbacks());
#endif
        }

#if !UDONSHARP
        // Delays runtime readbacks by one frame in regular MonoBehaviour builds.
        private IEnumerator DelayedAreaCookieAverageReadbacks() {
            yield return null;
            _RequestAreaCookieAverageReadbacks();
        }
#endif

        // Runs delayed area-cookie fallback readbacks.
        public void _RequestAreaCookieAverageReadbacks() {
            _areaCookieAverageReadbackScheduled = false;
            bool autoUpdatePass = !_areaCookieAverageReadbackForceAll;
            _areaCookieAverageReadbackForceAll = false;
            RequestAreaCookieAverageReadbacks(autoUpdatePass);
        }

        // Requests area-cookie fallback readbacks for all slices touched by the last custom texture blit pass.
        private void RequestAreaCookieAverageReadbacks(bool autoUpdatePass) {
            int singleTextureCount = _customSingleTextureCount;
            int singleMaterialCount = _customSingleMaterialCount;
            for (int i = 0; i < singleTextureCount; i++) {
                if (autoUpdatePass && !_customSingleTextureAutoUpdates[i]) continue;
                PointLightVolumeInstance receiver = _customSingleAreaCookieReceivers[i];
                if (receiver != null) RequestAreaCookieAverageReadback(i, receiver, _customSingleAreaCookieReceiverIndices[i], autoUpdatePass);
            }

            for (int i = 0; i < singleMaterialCount; i++) {
                int sourceIndex = singleTextureCount + i;
                PointLightVolumeInstance receiver = _customSingleAreaCookieReceivers[sourceIndex];
                if (receiver != null) RequestAreaCookieAverageReadback(sourceIndex, receiver, _customSingleAreaCookieReceiverIndices[sourceIndex], autoUpdatePass);
            }
        }

        // Requests one area cookie average from the final texture array slice used for old-shader fallback
        private void RequestAreaCookieAverageReadback(int sourceIndex, PointLightVolumeInstance receiver, int receiverIndex, bool forceReadback) {
            if (!_customTexturesUseMipMap || CustomTextures == null || receiver == null) return;
            int singleBaseSlice = _customTextureArrayDepth - _customSingleTextureCount - _customSingleMaterialCount;
            int singleSourceIDOffset = singleBaseSlice - CubemapsCount * 6;
            int targetSlice = singleBaseSlice + sourceIndex;
            int mipIndex = CustomTextures.mipmapCount - 1;
            int customId = CubemapsCount + singleSourceIDOffset + sourceIndex;

            if (!forceReadback && !receiver.AreaCookieAverageReadbackDirty) {
                if (receiverIndex >= 0 && receiverIndex < _pointLightAreaCookieAverageColors.Length && _pointLightAreaCookieAverageColors[receiverIndex].a > 0f) {
                    UploadAreaCookieAverageColor(customId, _pointLightAreaCookieAverageColors[receiverIndex]);
                    return;
                }
            }

            if (receiver.AreaCookieAverageReadbackPending) {
                if (receiver.AreaCookieAverageCustomId == customId) return;
                receiver.AreaCookieAverageReadbackDirty = true;
                return;
            }

            receiver.AreaCookieAverageCustomId = customId;
            receiver.AreaCookieAverageReadbackPending = true;
            receiver.AreaCookieAverageReadbackDirty = false;
#if COMPILER_UDONSHARP
            VRCAsyncGPUReadback.Request(CustomTextures, mipIndex, 0, 1, 0, 1, targetSlice, 1, TextureFormat.RGBA32, (IUdonEventReceiver)receiver);
#else
#if UNITY_EDITOR
            if (!Application.isPlaying) {
                AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(CustomTextures, mipIndex, 0, 1, 0, 1, targetSlice, 1, TextureFormat.RGBA32);
                request.WaitForCompletion();
                receiver.OnUnityAsyncGpuReadbackComplete(request);
                return;
            }
#endif
            AsyncGPUReadback.Request(CustomTextures, mipIndex, 0, 1, 0, 1, targetSlice, 1, TextureFormat.RGBA32, receiver.OnUnityAsyncGpuReadbackComplete);
#endif
        }

        // Completes one area-cookie average readback and retries if the source cache changed while it was in flight.
        public void CompleteAreaCookieAverageReadback(PointLightVolumeInstance receiver, bool success, Color color) {
            if (receiver == null) return;
            int customId = receiver.AreaCookieAverageCustomId;
            bool retry = receiver.AreaCookieAverageReadbackDirty;
            receiver.AreaCookieAverageReadbackPending = false;
            receiver.AreaCookieAverageReadbackDirty = false;
            receiver.AreaCookieAverageCustomId = -1;

            if (success && customId >= 0 && !UploadAreaCookieAverageColor(customId, color)) RequestUpdateVolumes();
            if (retry && enabled && gameObject.activeInHierarchy) ReinitializeCustomTextures();
        }

        // Caches the readback color and patches the live shader buffer. Returns true when a live shader slot was found.
        private bool UploadAreaCookieAverageColor(int customId, Color color) {
            if (customId < CubemapsCount) return false;

            float alpha = color.a;
            color.r *= alpha;
            color.g *= alpha;
            color.b *= alpha;
            color.a = 1f;

            PointLightVolumeInstance[] pointInstances = PointLightVolumeInstances;
            if (pointInstances == null) return false;
            int sourceCount = _pointLightCustomIDs.Length;
            if (_pointLightAreaCookieAverageColors.Length < sourceCount) sourceCount = _pointLightAreaCookieAverageColors.Length;
            if (pointInstances.Length < sourceCount) sourceCount = pointInstances.Length;
            for (int i = 0; i < sourceCount; i++) {
                if (_pointLightCustomIDs[i] != customId) continue;
                PointLightVolumeInstance instance = pointInstances[i];
                if (instance == null || instance.LightType != 2 || instance.ProjectionMode != 2) continue;
                _pointLightAreaCookieAverageColors[i] = color;
                instance.AreaLightFallbackColor = color;
            }

            int pointLightCount = _pointLightCount;
            int pointInstanceCount = pointInstances.Length;
            bool foundLiveTarget = false;
            bool updatedColor = false;
            for (int shaderIndex = 0; shaderIndex < pointLightCount; shaderIndex++) {
                int sourceIndex = _enabledPointIDs[shaderIndex];
                if (sourceIndex < 0 || sourceIndex >= _pointLightCustomIDs.Length || _pointLightCustomIDs[sourceIndex] != customId) continue;
                if (sourceIndex >= pointInstanceCount) continue;
                PointLightVolumeInstance sourceInstance = pointInstances[sourceIndex];
                if (sourceInstance == null || sourceInstance.LightType != 2 || sourceInstance.ProjectionMode != 2) continue; // 2: area light, 2: custom cookie
                foundLiveTarget = true;
                Vector4 shaderColor = _pointLightColor[shaderIndex];
                Vector4 extraData = _pointLightExtraData[shaderIndex];
                float fallbackR = extraData.x * color.r;
                float fallbackG = extraData.y * color.g;
                float fallbackB = extraData.z * color.b;
                if (shaderColor.x == fallbackR && shaderColor.y == fallbackG && shaderColor.z == fallbackB) continue;
                shaderColor.x = fallbackR;
                shaderColor.y = fallbackG;
                shaderColor.z = fallbackB;
                _pointLightColor[shaderIndex] = shaderColor;
                updatedColor = true;
            }
            if (updatedColor) VRCShader.SetGlobalVectorArray(_pointLightColorID, _pointLightColor);
            return foundLiveTarget;
        }

        // Rebuilds the runtime shadow texture array and assigns stable shader-side IDs to all shadowed point light instances
        public void ReinitializeShadowTextures() {
            RebuildShadowTextures();
            // Rebuilding the atlas changes the meaning of every packed shadow ID. Publish the matching counts/IDs in the same call so shaders can never observe a torn layout.
            // UpdateVolumes uses RebuildShadowTextures directly, which keeps the Udon call graph non-recursive while this public API remains atomic for external callers.
            if (!_isUpdatingVolumes) UpdateVolumes();
        }

        // Internal atlas rebuild used by UpdateVolumes while it already owns the atomic publish
        private void RebuildShadowTextures() {
            // Calling this method is an explicit retry point. The automatic update loop skips it while the allocation-failure latch is set, so a failed Create cannot thrash every frame.
            _shadowTextureAllocationFailed = false;
            _shadowCullPyramidAllocationFailed = false;
            InvalidateShadowCullPyramid();
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!Application.isPlaying) CaptureEditorShadowSourceState();
#endif
            // Source-less direct shadows (for example realtime flashlight shadows) live only in the current atlas. Preserve those ranges before rebuilding IDs or reallocating the atlas, then remap them after every ordinary source has populated the new layout.
            // A failed main-atlas allocation keeps the only copy of source-less direct shadows in scratch. Reuse it on the explicit retry instead of trying to capture a released atlas.
            bool hasPendingDirectPreservation = _preservedDirectShadowCount > 0 && _directShadowPreservationTexture != null;
            if (!hasPendingDirectPreservation && !CaptureDirectShadowOutputsForRebuild()) {
                _shadowTexturesInitialized = false;
                _shadowTextureAllocationFailed = true;
                return;
            }
            BuildShadowTextureSourceCache();
            // Hi-Z rebuilds lazily from clustering. Release an incompatible cached allocation immediately so deleting shadow sources still returns its VRAM when the remaining light count falls below ClusteringMinLights and no later clustering build runs.
            if (_shadowCullPyramid != null && (_shadowCullPyramidResolution != ShadowTexturesWidth || _shadowCullPyramidResolution != ShadowTexturesHeight || _shadowCullPyramidSliceCount != _shadowTextureArrayDepth))
                ReleaseShadowCullPyramidTextures();
            if (_shadowTextureArrayDepth <= 0) { // No shadow sources are active, so release the stale runtime texture array
                if (ShadowTextures != null) {
                    ReleaseRuntimeRenderTexture(ShadowTextures);
                    ShadowTextures = null;
                }
                ReleaseDirectShadowPreservation();
                _shadowTexturesInitialized = true;
                _shadowTextureAllocationFailed = false;
                return;
            }
            if (!EnsureRuntimeShadowTextures(ShadowTexturesWidth, ShadowTexturesHeight, _shadowTextureArrayDepth)) {
                // Keep staged direct ranges for the next explicit retry. Ordinary sources can be reconstructed from their texture/material references, but direct output cannot.
                return;
            }
            TryInitialize();
            BlitShadowTextures(false);
            RestorePreservedDirectShadowOutputs();
            _shadowTexturesInitialized = true;
            if (AutoUpdateTextures && HasAutoShadowTextureUpdates) ScheduleUpdateProcess();
        }

        // Updates only shadow cubemap sources marked for per-frame refresh
        public void UpdateAutoShadowTextures() {
            if (ShadowTextures == null) {
                ReinitializeShadowTextures();
                return;
            }
            BlitShadowTextures(true);
            if (AutoUpdateTextures && HasAutoShadowTextureUpdates) RefreshShadowCullAutoUpdateState();
            else InvalidateShadowCullPyramid();
        }

        // Resolves one direct light to its current final atlas base slice and synchronously publishes a rebuilt layout before the caller renders into it.
        public int PreparePointLightDirectShadowOutput(PointLightVolumeInstance instance) {
            if (instance == null || !instance.IsActive || !instance.RuntimeShadowDirectOutput) return -1;
            int resolution = Mathf.Max(instance.RuntimeShadowResolution, 16);
            if (resolution != ShadowTexturesWidth || resolution != ShadowTexturesHeight) return -1;
            if (_shadowTextureAllocationFailed) return -1;
            int registryIndex = FindPointLightRegistryIndex(instance);
            if (registryIndex < 0) return -1;
            bool usesCubemapShadow = instance.LightType != 1 || instance.ShadowMapUsesCubemap;
            int expectedSourceType = usesCubemapShadow ? 5 : 6;
            bool layoutReady = _shadowTexturesInitialized && ShadowTextures != null && _shadowTextureArrayDepth > 0
                && registryIndex < _pointLightShadowIDs.Length && registryIndex < _shadowSourceTypes.Length && _shadowSourceTypes[registryIndex] == expectedSourceType && (int)instance.ShadowMapID == _pointLightShadowIDs[registryIndex];
            if (!layoutReady) {
                // BuildShadowTextureSourceCache recognizes direct slots by their missing source. Hide the previous source only while the source-less slot is rebuilt and published. BakeShadows clears it permanently after every face and optional blur pass succeeds.
                Texture previousTexture = instance.ShadowMapTexture;
                Material previousMaterial = instance.ShadowMapMaterial;
                bool previousAutoUpdate = instance.AutoUpdateShadowMap;
                instance.ShadowMapTexture = null;
                instance.ShadowMapMaterial = null;
                instance.AutoUpdateShadowMap = false;
                RebuildShadowTextures();
                // Publish the rebuilt layout immediately. On failure this also stops shaders from advertising a released atlas. A retained normal source can repack on a later retry.
                UpdateVolumes();
                instance.ShadowMapTexture = previousTexture;
                instance.ShadowMapMaterial = previousMaterial;
                instance.AutoUpdateShadowMap = previousAutoUpdate;
                registryIndex = FindPointLightRegistryIndex(instance); // UpdateVolumes may compact a registry containing null entries.
                if (registryIndex < 0) return -1;
            }
            if (!_shadowTexturesInitialized || ShadowTextures == null || _shadowTextureArrayDepth <= 0) return -1;
            if (registryIndex >= _pointLightShadowIDs.Length || registryIndex >= _shadowSourceTypes.Length) return -1;
            if (_shadowSourceTypes[registryIndex] != expectedSourceType) return -1;
            int shadowId = _pointLightShadowIDs[registryIndex];
            if (shadowId < 0 || (int)instance.ShadowMapID != shadowId) return -1;

            int baseSlice = ResolveDirectShadowBaseSlice(expectedSourceType, shadowId, ShadowCubemapsCount);
            int sliceCount = usesCubemapShadow ? 6 : 1;
            if (baseSlice < 0 || baseSlice + sliceCount > _shadowTextureArrayDepth) return -1;
            return baseSlice;
        }

        // Copies one complete runtime-baked source into only this light's resolved atlas range. A latched allocation failure is left for an explicit retry or a real layout invalidation.
        public bool UpdatePointLightShadowTexture(PointLightVolumeInstance instance) {
            if (instance == null) return false;
            Texture sourceTexture = instance.ShadowMapTexture;
            if (sourceTexture == null || _shadowTextureAllocationFailed) return false;
            int sourceTextureMode = GetTextureMode(sourceTexture);

            int registryIndex = FindPointLightRegistryIndex(instance);
            if (registryIndex < 0) return false;
            bool usesCubemapShadow = instance.LightType != 1 || instance.ShadowMapUsesCubemap;
            instance.ShadowMapTextureIsCubemap = sourceTextureMode == 2;
            instance.ShadowMapTextureHasDepthSlices = sourceTextureMode == 1 && usesCubemapShadow;
            int expectedSourceType = usesCubemapShadow ? 1 : 3;
            bool layoutReady = _shadowTexturesInitialized && ShadowTextures != null && _shadowTextureArrayDepth > 0 && IsPointLightShadowTextureCacheMatch(instance, registryIndex, expectedSourceType, sourceTexture);
            if (!layoutReady) {
                ReinitializeShadowTextures();
                registryIndex = FindPointLightRegistryIndex(instance); // ReinitializeShadowTextures publishes through UpdateVolumes, which may compact the registry.
                return _shadowTexturesInitialized && ShadowTextures != null && _shadowTextureArrayDepth > 0 && IsPointLightShadowTextureCacheMatch(instance, registryIndex, expectedSourceType, sourceTexture);
            }

            int shadowId = (int)instance.ShadowMapID;
            if (shadowId < 0) return false;

            RenderTexture destination = ShadowTextures;
            int sourceSliceCount = usesCubemapShadow ? 6 : 1;
            int firstTargetSlice = usesCubemapShadow ? shadowId * 6 : ShadowCubemapsCount * 6 + shadowId - ShadowCubemapsCount;
            if (firstTargetSlice < 0 || firstTargetSlice + sourceSliceCount > _shadowTextureArrayDepth) return false;

            if (sourceTextureMode == 2) {
                if (!EnsureCubemapFaceMaterial()) return false;
                Material cubemapFaceMaterial = CubemapFaceMaterial;
                cubemapFaceMaterial.SetTexture(_cubemapSourceTexID, sourceTexture);
                for (int sourceFace = 0; sourceFace < sourceSliceCount; sourceFace++) {
                    cubemapFaceMaterial.SetInt(_cubemapFaceIndexID, sourceFace);
                    BlitMaterialToSlice(null, cubemapFaceMaterial, destination, firstTargetSlice + sourceFace, 0);
                }
                if (!instance.RuntimeShadowDirectOutput) InvalidateShadowCullPyramid();
                return true;
            }
            bool resampleCubemapArray = usesCubemapShadow && sourceTextureMode == 1 && CubemapArrayNeedsResampling(sourceTexture, destination);
            Material resampleMaterial = resampleCubemapArray ? PrepareCubemapArrayResampleMaterial(sourceTexture) : null;
            for (int sourceSlice = 0; sourceSlice < sourceSliceCount; sourceSlice++) {
                int targetSlice = firstTargetSlice + sourceSlice;
                if (resampleMaterial != null)
                    BlitCubemapArraySliceSeamless(resampleMaterial, sourceSlice, destination, targetSlice);
                else VRCGraphics.Blit(sourceTexture, destination, instance.ShadowMapTextureHasDepthSlices ? sourceSlice : 0, targetSlice);
            }
            if (!instance.RuntimeShadowDirectOutput) InvalidateShadowCullPyramid();
            return true;
        }

        // Confirms that this registry slot still owns the exact runtime texture source
        private bool IsPointLightShadowTextureCacheMatch(PointLightVolumeInstance instance, int registryIndex, int expectedSourceType, Texture sourceTexture) {
            if (instance == null || registryIndex < 0 || registryIndex >= _pointLightShadowIDs.Length || registryIndex >= _shadowSourceTypes.Length) return false;
            if (_shadowSourceTypes[registryIndex] != expectedSourceType) return false;
            int shadowId = _pointLightShadowIDs[registryIndex];
            if (shadowId < 0 || (int)instance.ShadowMapID != shadowId) return false;

            if (expectedSourceType == 1) return shadowId < _shadowCubemapTextureCount && _shadowCubemapTextures[shadowId] == sourceTexture;
            int localIndex = shadowId - ShadowCubemapsCount;
            return expectedSourceType == 3 && localIndex >= 0 && localIndex < _shadowSingleTextureCount && _shadowSingleTextures[localIndex] == sourceTexture;
        }

        // Copies every direct range from the last published layout into a compact scratch array. Ordinary texture/material sources do not need preservation because BlitShadowTextures can reconstruct them directly after the rebuild.
        private bool CaptureDirectShadowOutputsForRebuild() {
            ReleaseDirectShadowPreservation();
            RenderTexture sourceAtlas = ShadowTextures;
            // Invalidation deliberately clears _shadowTexturesInitialized before the delayed rebuild, but the last published atlas and its cache mappings are still valid until this method replaces them.
            if (sourceAtlas == null) return true;

            int cachedOwnerCount = _shadowSourceOwners.Length;
            cachedOwnerCount = Mathf.Min(cachedOwnerCount, _pointLightShadowIDs.Length);
            cachedOwnerCount = Mathf.Min(cachedOwnerCount, _shadowSourceTypes.Length);
            if (cachedOwnerCount <= 0) return true;

            int requiredScratchSlices = 0;
            for (int i = 0; i < cachedOwnerCount; i++) {
                if (_shadowSourceOwners[i] == null) continue;
                int sourceType = _shadowSourceTypes[i];
                if (sourceType == 5) requiredScratchSlices += 6;
                else if (sourceType == 6) requiredScratchSlices++;
            }
            if (requiredScratchSlices <= 0) return true;
            if (!CreateDirectShadowPreservationTexture(sourceAtlas, requiredScratchSlices)) return false;

            if (_preservedDirectShadowOwners.Length < cachedOwnerCount) {
                _preservedDirectShadowOwners = new PointLightVolumeInstance[cachedOwnerCount];
                _preservedDirectShadowSliceCounts = new int[cachedOwnerCount];
            }

            int scratchBaseSlice = 0;
            int sourceAtlasDepth = sourceAtlas.volumeDepth;
            for (int i = 0; i < cachedOwnerCount; i++) {
                PointLightVolumeInstance owner = _shadowSourceOwners[i];
                if (owner == null) continue;
                int sourceType = _shadowSourceTypes[i];
                int sliceCount = sourceType == 5 ? 6 : sourceType == 6 ? 1 : 0;
                if (sliceCount <= 0) continue;

                int sourceBaseSlice = ResolveDirectShadowBaseSlice(sourceType, _pointLightShadowIDs[i], ShadowCubemapsCount);
                if (sourceBaseSlice < 0 || sourceBaseSlice + sliceCount > sourceAtlasDepth) continue;

                _preservedDirectShadowOwners[_preservedDirectShadowCount] = owner;
                _preservedDirectShadowSliceCounts[_preservedDirectShadowCount] = sliceCount;
                _preservedDirectShadowCount++;

                for (int slice = 0; slice < sliceCount; slice++)
                    VRCGraphics.Blit(sourceAtlas, _directShadowPreservationTexture, sourceBaseSlice + slice, scratchBaseSlice + slice);
                scratchBaseSlice += sliceCount;
            }
            return true;
        }

        // Restores surviving direct owners into their newly resolved ranges. A separate scratch source makes this safe even when IDs overlap or the atlas keeps the same total depth.
        private void RestorePreservedDirectShadowOutputs() {
            RenderTexture destinationAtlas = ShadowTextures;
            RenderTexture scratchTexture = _directShadowPreservationTexture;
            if (destinationAtlas == null || scratchTexture == null) {
                ReleaseDirectShadowPreservation();
                return;
            }

            int destinationDepth = destinationAtlas.volumeDepth;
            int scratchBaseSlice = 0;
            for (int i = 0; i < _preservedDirectShadowCount; i++) {
                int sliceCount = _preservedDirectShadowSliceCounts[i];
                int sourceBaseSlice = scratchBaseSlice;
                scratchBaseSlice += sliceCount;
                PointLightVolumeInstance owner = _preservedDirectShadowOwners[i];
                if (owner == null) continue;
                int registryIndex = FindPointLightRegistryIndex(owner);
                if (registryIndex < 0 || registryIndex >= _shadowSourceTypes.Length || registryIndex >= _pointLightShadowIDs.Length) continue;

                int sourceType = _shadowSourceTypes[registryIndex];
                bool validCubemapRange = sourceType == 5 && sliceCount == 6;
                bool validSingleRange = sourceType == 6 && sliceCount == 1;
                if (!validCubemapRange && !validSingleRange) continue;

                int destinationBaseSlice = ResolveDirectShadowBaseSlice(sourceType, _pointLightShadowIDs[registryIndex], ShadowCubemapsCount);
                if (destinationBaseSlice < 0 || destinationBaseSlice + sliceCount > destinationDepth) continue;
                if (sourceBaseSlice + sliceCount > scratchTexture.volumeDepth) continue;

                for (int slice = 0; slice < sliceCount; slice++)
                    VRCGraphics.Blit(scratchTexture, destinationAtlas, sourceBaseSlice + slice, destinationBaseSlice + slice);
            }
            ReleaseDirectShadowPreservation();
        }

        private int ResolveDirectShadowBaseSlice(int sourceType, int shadowId, int cubemapCount) {
            if (shadowId < 0) return -1;
            if (sourceType == 5) return shadowId * 6;
            if (sourceType == 6) return cubemapCount * 6 + shadowId - cubemapCount;
            return -1;
        }

        private bool CreateDirectShadowPreservationTexture(RenderTexture sourceAtlas, int requiredDepth) {
            if (sourceAtlas == null || requiredDepth <= 0) return false;
            _directShadowPreservationTexture = CreateRuntimeTextureArray(sourceAtlas.width, sourceAtlas.height, requiredDepth, sourceAtlas.format, FilterMode.Bilinear, false, false);
#if !COMPILER_UDONSHARP
            if (_directShadowPreservationTexture != null) _directShadowPreservationTexture.name = "DirectShadowPreservation";
#endif
            return _directShadowPreservationTexture != null;
        }

        private void ReleaseDirectShadowPreservation() {
            for (int i = 0; i < _preservedDirectShadowCount; i++) _preservedDirectShadowOwners[i] = null;
            _preservedDirectShadowCount = 0;
            if (_directShadowPreservationTexture == null) return;
            ReleaseRuntimeRenderTexture(_directShadowPreservationTexture);
            _directShadowPreservationTexture = null;
        }

        // Builds deduplicated source arrays and per-instance shader IDs for the runtime shadow texture array
        private void BuildShadowTextureSourceCache() {

            PointLightVolumeInstance[] pointInstances = PointLightVolumeInstances;
            int count = pointInstances.Length;

            // Prepare reusable shadow texture source cache arrays for a full rebuild
            if (_pointLightShadowIDs.Length < count || _shadowSourceTypes.Length < count || _shadowSourceOwners.Length < count) {
                _shadowCubemapTextures = new Texture[count];
                _shadowCubemapMaterials = new Material[count];
                _shadowSingleTextures = new Texture[count];
                _shadowSingleMaterials = new Material[count];
                _shadowCubemapTextureModes = new int[count];
                _shadowCubemapTextureAutoUpdates = new bool[count];
                _shadowCubemapMaterialAutoUpdates = new bool[count];
                _shadowSingleTextureAutoUpdates = new bool[count];
                _shadowSingleMaterialAutoUpdates = new bool[count];
                _pointLightShadowIDs = new int[count];
                _shadowSourceTypes = new int[count];
                _shadowSourceOwners = new PointLightVolumeInstance[count];
            } else {
                for (int i = 0; i < _shadowCubemapTextureCount; i++) _shadowCubemapTextures[i] = null;
                for (int i = 0; i < _shadowCubemapMaterialCount; i++) _shadowCubemapMaterials[i] = null;
                for (int i = 0; i < _shadowSingleTextureCount; i++) _shadowSingleTextures[i] = null;
                for (int i = 0; i < _shadowSingleMaterialCount; i++) _shadowSingleMaterials[i] = null;
            }
            for (int i = 0; i < _pointLightShadowIDs.Length; i++) {
                _pointLightShadowIDs[i] = -1;
                _shadowSourceTypes[i] = 0;
                _shadowSourceOwners[i] = null;
            }

            int cubemapTextureCount = 0;
            int cubemapMaterialCount = 0;
            int cubemapDirectCount = 0;
            int singleTextureCount = 0;
            int singleMaterialCount = 0;
            int singleDirectCount = 0;
            int atlasWidth = ShadowTexturesWidth;
            int atlasHeight = ShadowTexturesHeight;
            HasAutoShadowTextureUpdates = false;

            // Iterate the registry once and collect unique shadow sources in reusable arrays
            for (int i = 0; i < count; i++) {

                // Start every point light unresolved. Only valid shadow sources receive a shadow texture ID
                PointLightVolumeInstance instance = pointInstances[i];
                if (instance == null || !instance.IsActive) continue;
                Texture textureSource = instance.ShadowMapTexture;
                Material materialSource = instance.ShadowMapMaterial;
                bool hasSource = textureSource != null || materialSource != null;
                // The runtime flag requests direct output, but an existing normal source remains authoritative until BakeShadows switches this light to source-less state. This keeps inactive/resolution-fallback results valid up to the first direct bake.
                bool directSource = false;
                if (instance.RuntimeShadowDirectOutput && !hasSource) {
                    int directResolution = Mathf.Max(instance.RuntimeShadowResolution, 16);
                    directSource = directResolution == atlasWidth && directResolution == atlasHeight;
                }
                if (!directSource && (instance.ShadowMapID < 0 || !hasSource)) {
                    instance.ShadowMapID = -1;
                    continue;
                }

                // Point and area emitters are omnidirectional shadow receivers in the shader ABI. Canonicalize low-level/runtime data here so only spot lights may occupy a single slice.
                bool usesCubemapShadow = instance.LightType != 1 || instance.ShadowMapUsesCubemap;
                instance.ShadowMapUsesCubemap = usesCubemapShadow;

                if (directSource) { // SOURCE-LESS DIRECT SHADOW

                    // Direct outputs are unique per light and occupy final atlas ranges that normal source blits deliberately skip.
                    _shadowSourceOwners[i] = instance;
                    if (usesCubemapShadow) {
                        _pointLightShadowIDs[i] = cubemapDirectCount;
                        _shadowSourceTypes[i] = 5;
                        cubemapDirectCount++;
                    } else {
                        _pointLightShadowIDs[i] = singleDirectCount;
                        _shadowSourceTypes[i] = 6;
                        singleDirectCount++;
                    }

                } else if (textureSource != null) { // Texture shadows mode

                    bool autoUpdate = instance.AutoUpdateShadowMap;
                    int textureMode = GetTextureMode(textureSource);
                    instance.ShadowMapTextureIsCubemap = textureMode == 2;
                    instance.ShadowMapTextureHasDepthSlices = textureMode == 1 && usesCubemapShadow;
                    if (usesCubemapShadow) { // TEXTURE CUBEMAP SHADOW

                        int index = Array.IndexOf((Array)_shadowCubemapTextures, textureSource, 0, cubemapTextureCount);
                        if (index < 0) { // First use of this texture: append it and reset this source's auto-update flag for the new cache build
                            index = cubemapTextureCount;
                            _shadowCubemapTextures[cubemapTextureCount] = textureSource;
                            _shadowCubemapTextureModes[cubemapTextureCount] = textureMode;
                            _shadowCubemapTextureAutoUpdates[cubemapTextureCount] = autoUpdate;
                            cubemapTextureCount++;
                        } else if (autoUpdate) { // Shared texture source: at least one auto-updated user already makes the shared source auto-updated
                            _shadowCubemapTextureAutoUpdates[index] = true;
                        }
                        _pointLightShadowIDs[i] = index;
                        _shadowSourceTypes[i] = 1; // 1: cubemap texture source, already indexed from the start of the cubemap source block

                    } else { // TEXTURE SINGLE SHADOW

                        int index = Array.IndexOf((Array)_shadowSingleTextures, textureSource, 0, singleTextureCount);
                        if (index < 0) { // First use of this texture: append it and reset this source's auto-update flag for the new cache build
                            index = singleTextureCount;
                            _shadowSingleTextures[singleTextureCount] = textureSource;
                            _shadowSingleTextureAutoUpdates[singleTextureCount] = autoUpdate;
                            singleTextureCount++;
                        } else if (autoUpdate) { // Shared texture source: at least one auto-updated user already makes the shared source auto-updated
                            _shadowSingleTextureAutoUpdates[index] = true;
                        }
                        _pointLightShadowIDs[i] = index;
                        _shadowSourceTypes[i] = 3; // 3: single texture source, offset after all cubemap sources during final ID assignment

                    }
                    if (autoUpdate) HasAutoShadowTextureUpdates = true;

                } else if (materialSource != null) { // Material shadows mode

                    bool autoUpdate = instance.AutoUpdateShadowMap;
                    if (usesCubemapShadow) { // MATERIAL CUBEMAP SHADOW

                        int index = Array.IndexOf((Array)_shadowCubemapMaterials, materialSource, 0, cubemapMaterialCount);
                        if (index < 0) { // First use of this material: append it and reset this source's auto-update flag for the new cache build
                            index = cubemapMaterialCount;
                            _shadowCubemapMaterials[cubemapMaterialCount] = materialSource;
                            _shadowCubemapMaterialAutoUpdates[cubemapMaterialCount] = autoUpdate;
                            cubemapMaterialCount++;
                        } else if (autoUpdate) { // Shared material source: at least one auto-updated user already makes the shared source auto-updated
                            _shadowCubemapMaterialAutoUpdates[index] = true;
                        }
                        _pointLightShadowIDs[i] = index;
                        _shadowSourceTypes[i] = 2; // 2: cubemap material source, offset after cubemap texture sources during final ID assignment

                    } else { // MATERIAL SINGLE SHADOW

                        int index = Array.IndexOf((Array)_shadowSingleMaterials, materialSource, 0, singleMaterialCount);
                        if (index < 0) { // First use of this material: append it and reset this source's auto-update flag for the new cache build
                            index = singleMaterialCount;
                            _shadowSingleMaterials[singleMaterialCount] = materialSource;
                            _shadowSingleMaterialAutoUpdates[singleMaterialCount] = autoUpdate;
                            singleMaterialCount++;
                        } else if (autoUpdate) { // Shared material source: at least one auto-updated user already makes the shared source auto-updated
                            _shadowSingleMaterialAutoUpdates[index] = true;
                        }
                        _pointLightShadowIDs[i] = index;
                        _shadowSourceTypes[i] = 4; // 4: single material source, offset after cubemap and single texture sources during final ID assignment

                    }
                    if (autoUpdate) HasAutoShadowTextureUpdates = true;

                }

            }

            // Updating counts
            _shadowCubemapTextureCount = cubemapTextureCount;
            _shadowCubemapMaterialCount = cubemapMaterialCount;
            _shadowSingleTextureCount = singleTextureCount;
            _shadowSingleMaterialCount = singleMaterialCount;
            int cubemapsCount = cubemapTextureCount + cubemapMaterialCount + cubemapDirectCount;
            ShadowCubemapsCount = cubemapsCount;
            ShadowMapsCount = cubemapsCount + singleTextureCount + singleMaterialCount + singleDirectCount;
            _shadowTextureArrayDepth = cubemapsCount * 6 + singleTextureCount + singleMaterialCount + singleDirectCount;

            // Convert local source indices into final shadow-map IDs after final counts are known
            for (int i = 0; i < count; i++) {
                int index = _pointLightShadowIDs[i];
                if (index < 0) continue;
                int sourceType = _shadowSourceTypes[i];
                // SourceType 1 already uses the local cubemap texture index; every later group needs its final offset.
                if (sourceType == 2) _pointLightShadowIDs[i] = cubemapTextureCount + index; // 2: cubemap materials follow cubemap textures
                else if (sourceType == 5) _pointLightShadowIDs[i] = cubemapTextureCount + cubemapMaterialCount + index; // 5: direct cubemaps follow sourced cubemaps
                else if (sourceType == 3) _pointLightShadowIDs[i] = cubemapsCount + index; // 3: single textures follow every six-slice cubemap source
                else if (sourceType == 4) _pointLightShadowIDs[i] = cubemapsCount + singleTextureCount + index; // 4: single materials follow single textures
                else if (sourceType == 6) _pointLightShadowIDs[i] = cubemapsCount + singleTextureCount + singleMaterialCount + index; // 6: direct singles follow sourced singles
                PointLightVolumeInstance instance = pointInstances[i];
                instance.ShadowMapID = _pointLightShadowIDs[i];
            }

        }

        // Copies shadow sources into the runtime array. autoUpdatePass copies only sources cached for Auto Update Textures
        private void BlitShadowTextures(bool autoUpdatePass) {
            RenderTexture destination = ShadowTextures;
            // Shadow texture sources occupy the first shadow slices, six slices per cubemap
            int cubemapTextureCount = _shadowCubemapTextureCount;
            for (int i = 0; i < cubemapTextureCount; i++) {
                if (autoUpdatePass && !_shadowCubemapTextureAutoUpdates[i]) continue;
                BlitCubemapTexture(_shadowCubemapTextures[i], _shadowCubemapTextureModes[i], i * 6, destination);
            }
            // Shadow material sources follow texture sources and are rendered as six generated slices
            int cubemapMaterialCount = _shadowCubemapMaterialCount;
            for (int i = 0; i < cubemapMaterialCount; i++) {
                if (autoUpdatePass && !_shadowCubemapMaterialAutoUpdates[i]) continue;
                int shadowId = cubemapTextureCount + i;
                int firstSlice = shadowId * 6;
                BlitCubemapMaterial(_shadowCubemapMaterials[i], firstSlice, destination);
            }
            // Single shadow textures follow cubemap sources and occupy one array slice each
            int singleBaseSlice = ShadowCubemapsCount * 6;
            int singleTextureCount = _shadowSingleTextureCount;
            for (int i = 0; i < singleTextureCount; i++) {
                if (autoUpdatePass && !_shadowSingleTextureAutoUpdates[i]) continue;
                Texture sourceTexture = _shadowSingleTextures[i];
                if (sourceTexture == null) continue;
                int targetSlice = singleBaseSlice + i;
                VRCGraphics.Blit(sourceTexture, destination, 0, targetSlice);
            }
            // Single shadow materials follow single texture sources and occupy one array slice each
            int singleMaterialCount = _shadowSingleMaterialCount;
            for (int i = 0; i < singleMaterialCount; i++) {
                if (autoUpdatePass && !_shadowSingleMaterialAutoUpdates[i]) continue;
                Material sourceMaterial = _shadowSingleMaterials[i];
                if (sourceMaterial == null) continue;
                int targetSlice = singleBaseSlice + singleTextureCount + i;
                BlitSingleMaterial(sourceMaterial, targetSlice, destination);
            }
        }

#endregion

#region Runtime Texture Rendering

        // Creates or recreates the runtime texture array so it matches an explicit texture layout
        private bool EnsureRuntimeCustomTextures(int width, int height, int depth) {
            if (width <= 0 || height <= 0 || depth <= 0) return false;
            bool useMipMap = _customTexturesUseMipMap;
            bool autoGenerateMips = useMipMap;
#if !COMPILER_UDONSHARP && UNITY_EDITOR
            if (!Application.isPlaying) autoGenerateMips = false;
#endif
            bool recreate = ShouldRecreateRuntimeTextureArray(CustomTextures, width, height, depth, FixedCustomTexturesFormat, useMipMap, autoGenerateMips, FilterMode.Trilinear);
            if (!recreate) return true;
            ReleaseRuntimeRenderTexture(CustomTextures);
            CustomTextures = CreateRuntimeTextureArray(width, height, depth, FixedCustomTexturesFormat, FilterMode.Trilinear, useMipMap, autoGenerateMips);
            if (CustomTextures == null) {
                _customTexturesInitialized = false;
                return false;
            }
#if !COMPILER_UDONSHARP
            CustomTextures.name = "CustomTextures";
#endif
            _customTextureArrayDepth = depth;
            return true;
        }

        // Creates or recreates the runtime shadow texture array so it matches an explicit texture layout
        private bool EnsureRuntimeShadowTextures(int width, int height, int depth) {
            if (width <= 0 || height <= 0 || depth <= 0) {
                _shadowTextureAllocationFailed = true;
                return false;
            }
            RenderTextureFormat renderTextureFormat = ShadowTextureFormat == ShadowTextureFormatHalf ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGBFloat;
            bool recreate = ShouldRecreateRuntimeTextureArray(ShadowTextures, width, height, depth, renderTextureFormat, false, false, FilterMode.Bilinear);
            if (!recreate) return true;
            ReleaseRuntimeRenderTexture(ShadowTextures);
            ShadowTextures = CreateRuntimeTextureArray(width, height, depth, renderTextureFormat, FilterMode.Bilinear, false, false);
            if (ShadowTextures == null) {
                _shadowTexturesInitialized = false;
                _shadowTextureAllocationFailed = true;
                return false;
            }
#if !COMPILER_UDONSHARP
            ShadowTextures.name = "ShadowTextures";
#endif
            _shadowTextureArrayDepth = depth;
            _shadowTextureAllocationFailed = false;
            return true;
        }

        // Checks if a runtime texture array must be recreated for the requested layout
        private bool ShouldRecreateRuntimeTextureArray(RenderTexture texture, int width, int height, int depth, RenderTextureFormat format, bool useMipMap, bool autoGenerateMips, FilterMode filterMode) {
            return texture == null || texture.width != width || texture.height != height || texture.volumeDepth != depth || texture.useMipMap != useMipMap || texture.autoGenerateMips != autoGenerateMips || texture.filterMode != filterMode || texture.format != format;
        }

        // Releases one Manager-owned runtime render texture
        private void ReleaseRuntimeRenderTexture(RenderTexture texture) {
            if (texture == null) return;
#if COMPILER_UDONSHARP
            Destroy(texture);
#else
            if (RenderTexture.active == texture) RenderTexture.active = null;
            texture.Release();
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
#endif
        }

        // Creates a runtime texture array with the shared Light Volumes settings
        private RenderTexture CreateRuntimeTextureArray(int width, int height, int depth, RenderTextureFormat format, FilterMode filterMode, bool useMipMap, bool autoGenerateMips) {
            RenderTexture texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear);
            texture.dimension = TextureDimension.Tex2DArray;
            texture.volumeDepth = depth;
            texture.useMipMap = useMipMap;
            texture.autoGenerateMips = autoGenerateMips;
            texture.enableRandomWrite = false;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = filterMode;
            texture.anisoLevel = 0;
#if !COMPILER_UDONSHARP
            texture.hideFlags = HideFlags.HideAndDontSave;
#endif
            if (texture.Create()) return texture;
            ReleaseRuntimeRenderTexture(texture);
            return null;
        }

        // Writes a six-face cubemap texture source into consecutive destination array slices
        private void BlitCubemapTexture(Texture sourceTexture, int textureMode, int firstSlice, RenderTexture destination) {
            if (sourceTexture == null) return;
            // Undo can restore the texture reference and its serialized layout flags from different runtime snapshots. The actual texture dimension is authoritative before touching a Cube-only material property.
            textureMode = GetTextureMode(sourceTexture);
            if (textureMode == 2) { // Native Cubemap: unwrap each cubemap face into its destination slice
                if (!EnsureCubemapFaceMaterial()) return;
                Material cubemapFaceMaterial = CubemapFaceMaterial;
                cubemapFaceMaterial.SetTexture(_cubemapSourceTexID, sourceTexture);
                for (int face = 0; face < 6; face++) {
                    cubemapFaceMaterial.SetInt(_cubemapFaceIndexID, face);
                    BlitMaterialToSlice(null, cubemapFaceMaterial, destination, firstSlice + face, 0);
                }
                return;
            }
            bool resampleCubemapArray = textureMode == 1 && CubemapArrayNeedsResampling(sourceTexture, destination);
            Material resampleMaterial = resampleCubemapArray ? PrepareCubemapArrayResampleMaterial(sourceTexture) : null;
            for (int i = 0; i < 6; i++) {
                int targetSlice = firstSlice + i;
                int sourceSlice = textureMode == 1 ? i : 0; // Texture2DArray: slices 0..5 already contain the cubemap faces
                if (resampleMaterial != null)
                    BlitCubemapArraySliceSeamless(resampleMaterial, sourceSlice, destination, targetSlice);
                else VRCGraphics.Blit(sourceTexture, destination, sourceSlice, targetSlice);
            }
        }

        // A cubemap stored as array slices needs cross-face filtering whenever its resolution changes; an ordinary array blit clamps every face independently.
        private bool CubemapArrayNeedsResampling(Texture sourceTexture, RenderTexture destination) {
            return sourceTexture != null && destination != null && (sourceTexture.width != destination.width || sourceTexture.height != destination.height);
        }

        // Configures the shared resample material once for all six faces of one cubemap source.
        private Material PrepareCubemapArrayResampleMaterial(Texture sourceTexture) {
            Material resampleMaterial = RuntimeShadowBlurMaterial;
            if (resampleMaterial == null) return null;
            float sourceWidth = Mathf.Max(sourceTexture.width, 1);
            float sourceHeight = Mathf.Max(sourceTexture.height, 1);
            resampleMaterial.SetTexture(_cubemapArraySourceTexID, sourceTexture);
            resampleMaterial.SetFloat(_cubemapArraySourceBaseSliceID, 0f);
            resampleMaterial.SetVector(_cubemapArraySourceResolutionID, new Vector4(sourceWidth, sourceHeight, 1f / sourceWidth, 1f / sourceHeight));
            return resampleMaterial;
        }

        // Resamples one prepared face using the source texel footprint. This keeps a low-resolution bake continuous when it is expanded into a much larger Manager atlas.
        private void BlitCubemapArraySliceSeamless(Material resampleMaterial, int sourceFace, RenderTexture destination, int targetSlice) {
            resampleMaterial.SetInt(_cubemapFaceIndexID, sourceFace);
            BlitMaterialToSlice(null, resampleMaterial, destination, targetSlice, CubemapResampleMaterialPass);
        }

        // Resolves the physical source layout without trusting serialized metadata that Undo may restore independently from a runtime-generated texture reference.
        private int GetTextureMode(Texture texture) {
            if (texture == null) return 0;
            int textureDimension = (int)texture.dimension;
            if (textureDimension == 4) return 2; // 4: TextureDimension.Cube
            if (textureDimension == 5) return 1; // 5: TextureDimension.Tex2DArray
            return 0;
        }

        // Writes a six-face cubemap material source into consecutive destination array slices
        private void BlitCubemapMaterial(Material sourceMaterial, int firstSlice, RenderTexture destination) {
            if (sourceMaterial == null || destination == null) return;
#if UDONSHARP
            Texture blitSource = sourceMaterial.HasTexture(_cubemapMainTexID) ? sourceMaterial.GetTexture(_cubemapMainTexID) : null;
#else
            Texture blitSource = null;
#endif
            float width = destination.width;
            float height = destination.height;
            for (int face = 0; face < 6; face++) {
                sourceMaterial.SetVector("_CustomRenderTextureInfo", new Vector4(width, height, 1f, face));
                BlitMaterialToSlice(blitSource, sourceMaterial, destination, firstSlice + face, 0);
            }
        }

        // Renders a single-slice material source into one texture-array slice
        private void BlitSingleMaterial(Material sourceMaterial, int targetSlice, RenderTexture destination) {
            if (sourceMaterial == null || destination == null) return;
            sourceMaterial.SetVector("_CustomRenderTextureInfo", new Vector4(destination.width, destination.height, destination.volumeDepth, targetSlice));
#if UDONSHARP
            Texture blitSource = sourceMaterial.HasTexture(_cubemapMainTexID) ? sourceMaterial.GetTexture(_cubemapMainTexID) : null;
#else
            Texture blitSource = null;
#endif
            BlitMaterialToSlice(blitSource, sourceMaterial, destination, targetSlice, 0);
        }

        // Renders one material pass into a destination texture-array slice using the active runtime API
        private void BlitMaterialToSlice(Texture sourceTexture, Material material, RenderTexture destination, int targetSlice, int materialPass) {
#if UDONSHARP
#if !COMPILER_UDONSHARP
            RenderTexture previousRenderTexture = RenderTexture.active;
#endif
            // Udon VRCGraphics needs a separate destination-binding blit before rendering the material into the selected slice
            if (_dummyRT == null) {
                RenderTexture dummyTexture = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                if (!dummyTexture.Create()) {
                    ReleaseRuntimeRenderTexture(dummyTexture);
                    return;
                }
                _dummyRT = dummyTexture;
            }
            VRCGraphics.Blit(_dummyRT, destination, 0, targetSlice);
            VRCGraphics.Blit(sourceTexture, material, materialPass, targetSlice);
#if !COMPILER_UDONSHARP
            RenderTexture.active = previousRenderTexture == destination ? null : previousRenderTexture;
#endif
#else
            // Unity Graphics can bind the target slice directly, so the material pass can render in one blit
            RenderTexture previousRenderTexture = RenderTexture.active;
            VRCGraphics.SetRenderTarget(destination, 0, CubemapFace.Unknown, targetSlice);
            VRCGraphics.Blit(sourceTexture, material, materialPass);
            RenderTexture.active = previousRenderTexture == destination ? null : previousRenderTexture;
#endif
        }

        // Returns the resolved custom projection texture ID for a point light instance
        public int GetPointLightCustomID(PointLightVolumeInstance instance) {
            if (instance == null || PointLightVolumeInstances == null) return -1;
            int index = FindPointLightRegistryIndex(instance);
            if (index < 0 || index >= _pointLightCustomIDs.Length) return -1;
            return _pointLightCustomIDs[index];
        }

        // Finds or lazily creates the cubemap face material outside Udon
        private bool EnsureCubemapFaceMaterial() {
            if (CubemapFaceMaterial != null) return true;
#if !COMPILER_UDONSHARP
            Shader shader = Shader.Find("Hidden/CubeFace");
            if (shader == null) return false;
            CubemapFaceMaterial = new Material(shader);
            CubemapFaceMaterial.hideFlags = HideFlags.HideAndDontSave;
            return true;
#else
            return false;
#endif
        }

#if !COMPILER_UDONSHARP
        // Creates or reuses the one persistent hidden camera shared by all runtime shadow bakes.
        internal void EnsureRuntimeShadowCamera() {
            if (RuntimeShadowCamera == null || RuntimeShadowCamera.transform.parent != transform) {
                RuntimeShadowCamera = null;
                Camera[] cameras = GetComponentsInChildren<Camera>(true);
                for (int i = 0; i < cameras.Length; i++) {
                    Camera camera = cameras[i];
                    if (camera == null || camera.transform.parent != transform) continue;
                    if (camera.gameObject.name != RuntimeShadowCameraName) continue;
                    if (camera.hideFlags != HideFlags.HideInInspector || camera.gameObject.hideFlags != HideFlags.HideInHierarchy) continue;
                    RuntimeShadowCamera = camera;
                    break;
                }
                if (RuntimeShadowCamera == null) {
                    GameObject cameraObject = new GameObject(RuntimeShadowCameraName);
                    cameraObject.transform.SetParent(transform, false);
                    RuntimeShadowCamera = cameraObject.AddComponent<Camera>();
                }
            }

            RuntimeShadowCamera.gameObject.name = RuntimeShadowCameraName;
            RuntimeShadowCamera.gameObject.hideFlags = HideFlags.HideInHierarchy;
            RuntimeShadowCamera.hideFlags = HideFlags.HideInInspector;
            RuntimeShadowCamera.enabled = false;
            RuntimeShadowCamera.clearFlags = CameraClearFlags.Depth;
            RuntimeShadowCamera.backgroundColor = Color.white;
            RuntimeShadowCamera.orthographic = false;
            RuntimeShadowCamera.fieldOfView = 90f;
            RuntimeShadowCamera.aspect = 1f;
            RuntimeShadowCamera.depthTextureMode = DepthTextureMode.None;
            RuntimeShadowCamera.renderingPath = RenderingPath.Forward;
            RuntimeShadowCamera.allowHDR = false;
            RuntimeShadowCamera.allowMSAA = false;
            RuntimeShadowCamera.useOcclusionCulling = false;
            RuntimeShadowCamera.stereoTargetEye = StereoTargetEyeMask.None;
            RuntimeShadowCamera.ResetReplacementShader();
        }

#endif

#if !COMPILER_UDONSHARP && (!UDONSHARP || UNITY_EDITOR)
        // Destroys the editor/runtime material instance used by non-Udon execution
        private void DestroyCubemapFaceRuntimeMaterial() {
            if (CubemapFaceMaterial == null) return;
            if (CubemapFaceMaterial.hideFlags != HideFlags.HideAndDontSave) return;
            if (Application.isPlaying) Destroy(CubemapFaceMaterial);
            else DestroyImmediate(CubemapFaceMaterial);
            CubemapFaceMaterial = null;
        }

        // Destroys only editor-generated shadow materials owned by this Manager. Persistent project assets and build-scene material dependencies are serialized ownership inputs and must never be destroyed by the component lifecycle.
        private void DestroyRuntimeShadowMaterials() {
            if (RuntimeShadowDepthEncodeMaterial != null && RuntimeShadowDepthEncodeMaterial.hideFlags == HideFlags.HideAndDontSave) {
                if (Application.isPlaying) Destroy(RuntimeShadowDepthEncodeMaterial);
                else DestroyImmediate(RuntimeShadowDepthEncodeMaterial);
                RuntimeShadowDepthEncodeMaterial = null;
            }
            if (RuntimeShadowBlurMaterial != null && RuntimeShadowBlurMaterial.hideFlags == HideFlags.HideAndDontSave) {
                if (Application.isPlaying) Destroy(RuntimeShadowBlurMaterial);
                else DestroyImmediate(RuntimeShadowBlurMaterial);
                RuntimeShadowBlurMaterial = null;
            }
        }

        // Destroys editor/standalone clustering materials created outside the build preprocessor.
        private void DestroyClusteringMaterial() {
#if !COMPILER_UDONSHARP
            if (_generatedClusteringMaterial != null) {
                if (Application.isPlaying) Destroy(_generatedClusteringMaterial);
                else DestroyImmediate(_generatedClusteringMaterial);
                _generatedClusteringMaterial = null;
            }
            if (_generatedShadowCullingMaterial != null) {
                if (Application.isPlaying) Destroy(_generatedShadowCullingMaterial);
                else DestroyImmediate(_generatedShadowCullingMaterial);
                _generatedShadowCullingMaterial = null;
            }
#endif
            if (ClusteringMaterial != null && ClusteringMaterial.hideFlags == HideFlags.HideAndDontSave) {
                if (Application.isPlaying) Destroy(ClusteringMaterial);
                else DestroyImmediate(ClusteringMaterial);
                ClusteringMaterial = null;
            }
            if (ShadowCullingMaterial != null && ShadowCullingMaterial.hideFlags == HideFlags.HideAndDontSave) {
                if (Application.isPlaying) Destroy(ShadowCullingMaterial);
                else DestroyImmediate(ShadowCullingMaterial);
                ShadowCullingMaterial = null;
            }
        }

#endif

#endregion
    }
}
