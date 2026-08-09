#if !UDONSHARP && (UNITY_EDITOR || COMPILER_UDONSHARP)
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

        // Updates only custom texture sources marked for per-frame refresh
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

            int count = PointLightVolumeInstances.Length;

            // Prepare reusable custom texture source cache arrays for a full rebuild
            if (_pointLightCustomIDs.Length < count || _customSourceTypes.Length < count || _customSingleAreaCookieReceivers.Length < count || _customSingleAreaCookieReceiverIndices.Length < count || _pointLightAreaCookieAverageColors.Length < count) {
                _customCubemapTextures = new Texture[count];
                _customCubemapMaterials = new Material[count];
                _customSingleTextures = new Texture[count];
                _customSingleMaterials = new Material[count];
                _customCubemapTextureModes = new int[count];
                _customCubemapTextureAutoUpdates = new bool[count];
                _customCubemapMaterialAutoUpdates = new bool[count];
                _customSingleTextureAutoUpdates = new bool[count];
                _customSingleMaterialAutoUpdates = new bool[count];
                _customSingleAreaCookieReceivers = new PointLightVolumeInstance[count];
                _customSingleAreaCookieReceiverIndices = new int[count];
                _pointLightCustomIDs = new int[count];
                _customSourceTypes = new int[count];
                _pointLightAreaCookieAverageColors = new Color[count];
            } else {
                for (int i = 0; i < _customCubemapTextureCount; i++) _customCubemapTextures[i] = null;
                for (int i = 0; i < _customCubemapMaterialCount; i++) _customCubemapMaterials[i] = null;
                for (int i = 0; i < _customSingleTextureCount; i++) _customSingleTextures[i] = null;
                for (int i = 0; i < _customSingleMaterialCount; i++) _customSingleMaterials[i] = null;
            }
            // These registry-index mappings are grow-only. Clear the entire retained capacity so a
            // later source-less append cannot inherit the ID that occupied its index before a shrink.
            for (int i = 0; i < _pointLightCustomIDs.Length; i++) {
                _pointLightCustomIDs[i] = -1;
                _customSourceTypes[i] = 0;
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

                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null || !instance.IsActive) continue;

                int projectionType = instance.ProjectionType;
                if (projectionType == 0) continue; // 0: parametric projection has no custom source

                int lightType = instance.LightType;

                int projectionMode = instance.ProjectionMode;
                if (projectionMode == 0) continue; // 0: parametric projection has no custom source

                bool usesCubemapProjection = lightType == 0 && projectionMode == 2; // 0: point, 2: custom cookie or cubemap
                bool usesAreaCookieProjection = lightType == 2 && projectionMode == 2; // 2: area, 2: custom cookie
                bool usesPointLutProjection = lightType == 0 && projectionMode == 1; // 0: point, 1: LUT

                if (projectionType == 1) { // TEXTURE PROJECTION

                    Texture textureSource = instance.CustomTexture;
                    if (textureSource == null) continue;
                    bool autoUpdate = instance.AutoUpdateCustomTexture;
                    if (usesAreaCookieProjection) _customTexturesUseMipMap = true;

                    if (usesCubemapProjection) { // TEXTURE CUBEMAP PROJECTION

                        int index = -1;
                        for (int j = 0; j < cubemapTextureCount; j++) {
                            if (_customCubemapTextures[j] == textureSource && _customCubemapTextureAutoUpdates[j] == autoUpdate) {
                                index = j;
                                break;
                            }
                        }
                        if (index < 0) { // Append each unique source/update-mode pair once so matching lights share the same texture ID
                            index = cubemapTextureCount;
                            _customCubemapTextures[cubemapTextureCount] = textureSource;
                            _customCubemapTextureModes[cubemapTextureCount] = instance.CustomTextureIsCubemap ? 2 : (instance.CustomTextureHasDepthSlices ? 1 : 0); // Texture layout: 0 = single 2D texture, 1 = Texture2DArray face slices, 2 = native Cubemap.
                            _customCubemapTextureAutoUpdates[cubemapTextureCount] = autoUpdate;
                            cubemapTextureCount++;
                        }
                        _pointLightCustomIDs[i] = index;
                        _customSourceTypes[i] = 1; // 1: cubemap texture source, already indexed from the start of the cubemap source block

                    } else { // TEXTURE COOKIE PROJECTION

                        int index = -1;
                        for (int j = 0; j < singleTextureCount; j++) {
                            if (_customSingleTextures[j] == textureSource && _customSingleTextureAutoUpdates[j] == autoUpdate) {
                                index = j;
                                break;
                            }
                        }
                        if (index < 0) { // Append each unique source/update-mode pair once so matching lights share the same texture ID
                            index = singleTextureCount;
                            _customSingleTextures[singleTextureCount] = textureSource;
                            _customSingleTextureAutoUpdates[singleTextureCount] = autoUpdate;
                            singleTextureCount++;
                        }
                        if (usesPointLutProjection && index == 0) pointLutUsesFirstSingleTexture = true;
                        _pointLightCustomIDs[i] = index;
                        _customSourceTypes[i] = 3; // 3: single texture source, offset after all cubemap sources during final ID assignment

                    }
                    if (autoUpdate) HasAutoCustomTextureUpdates = true;

                } else if (projectionType == 2) { // MATERIAL PROJECTION

                    Material materialSource = instance.CustomTextureMaterial;
                    if (materialSource == null) continue;
                    bool autoUpdate = instance.AutoUpdateCustomTexture;
                    if (usesAreaCookieProjection) _customTexturesUseMipMap = true;

                    if (usesCubemapProjection) { // MATERIAL CUBEMAP PROJECTION

                        int index = -1;
                        for (int j = 0; j < cubemapMaterialCount; j++) {
                            if (_customCubemapMaterials[j] == materialSource && _customCubemapMaterialAutoUpdates[j] == autoUpdate) {
                                index = j;
                                break;
                            }
                        }
                        if (index < 0) { // Append each unique material/update-mode pair once so matching lights share the same texture ID
                            index = cubemapMaterialCount;
                            _customCubemapMaterials[cubemapMaterialCount] = materialSource;
                            _customCubemapMaterialAutoUpdates[cubemapMaterialCount] = autoUpdate;
                            cubemapMaterialCount++;
                        }
                        _pointLightCustomIDs[i] = index;
                        _customSourceTypes[i] = 2; // 2: cubemap material source, offset after cubemap texture sources during final ID assignment

                    } else { // MATERIAL SINGLE SLICE PROJECTION

                        int index = -1;
                        for (int j = 0; j < singleMaterialCount; j++) {
                            if (_customSingleMaterials[j] == materialSource && _customSingleMaterialAutoUpdates[j] == autoUpdate) {
                                index = j;
                                break;
                            }
                        }
                        if (index < 0) { // Append each unique material/update-mode pair once so matching lights share the same texture ID
                            index = singleMaterialCount;
                            _customSingleMaterials[singleMaterialCount] = materialSource;
                            _customSingleMaterialAutoUpdates[singleMaterialCount] = autoUpdate;
                            singleMaterialCount++;
                        }
                        if (usesPointLutProjection && index == 0) pointLutUsesFirstSingleMaterial = true;
                        _pointLightCustomIDs[i] = index;
                        _customSourceTypes[i] = 4; // 4: single material source, offset after cubemap and single texture sources during final ID assignment

                    }
                    if (autoUpdate) HasAutoCustomTextureUpdates = true;

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
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null) continue;
                if (!instance.IsActive) continue;

                int index = _pointLightCustomIDs[i];
                if (index < 0) {
                    if (instance.AreaCookieAverageReadbackPending) {
                        instance.AreaCookieAverageCustomId = -1;
                        instance.AreaCookieAverageReadbackDirty = true;
                    }
                    continue;
                }
                int sourceType = _customSourceTypes[i];
                // SourceType 1 already uses the local cubemap texture index as the final ID; 2/3/4 need offsets.
                if (sourceType == 2) index += cubemapTextureCount; // 2: cubemap materials follow cubemap textures
                else if (sourceType == 3) index += cubemapsCount + singleSourceIDOffset; // 3: single textures follow every six-slice cubemap source
                else if (sourceType == 4) index += cubemapsCount + singleSourceIDOffset + singleTextureCount; // 4: single materials follow single textures
                _pointLightCustomIDs[i] = index;

                if ((sourceType != 3 && sourceType != 4) || instance.LightType != 2 || instance.ProjectionMode != 2) { // 2: area light, 2: custom cookie, 3/4: single texture/material
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

        // Copies custom projection sources into the runtime array. autoUpdatePass copies only sources cached for Auto Update Textures
        private void BlitCustomTextures(bool autoUpdatePass) {
            // Blit each cubemap texture source into 6 array slices
            int cubemapTextureCount = _customCubemapTextureCount;
            for (int i = 0; i < cubemapTextureCount; i++) {
                if (autoUpdatePass && !_customCubemapTextureAutoUpdates[i]) continue;
                BlitCubemapTexture(_customCubemapTextures[i], _customCubemapTextureModes[i], i * 6, CustomTextures);
            }

            // Blit each cubemap material source into 6 array slices
            int cubemapMaterialCount = _customCubemapMaterialCount;
            for (int i = 0; i < cubemapMaterialCount; i++) {
                if (autoUpdatePass && !_customCubemapMaterialAutoUpdates[i]) continue;
                BlitCubemapMaterial(_customCubemapMaterials[i], (cubemapTextureCount + i) * 6, CustomTextures);
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
                VRCGraphics.Blit(sourceTexture, CustomTextures, 0, targetSlice);
            }

            // Blit each 1-slice material source into 1 array slice after texture sources
            for (int i = 0; i < singleMaterialCount; i++) {
                if (autoUpdatePass && !_customSingleMaterialAutoUpdates[i]) continue;
                Material sourceMaterial = _customSingleMaterials[i];
                if (sourceMaterial == null) continue;
                int targetSlice = singleBaseSlice + singleTextureCount + i;
                BlitMaterialSlice(sourceMaterial, 0, targetSlice, false, CustomTextures);
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
                if (autoUpdatePass && !_customSingleMaterialAutoUpdates[i]) continue;
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
            if (customId < 0) return false;

            float alpha = color.a;
            color.r *= alpha;
            color.g *= alpha;
            color.b *= alpha;
            color.a = 1f;

            PointLightVolumeInstance[] pointInstances = PointLightVolumeInstances;
            if (pointInstances == null) return false;
            int sourceCount = _pointLightCustomIDs.Length;
            if (_customSourceTypes.Length < sourceCount) sourceCount = _customSourceTypes.Length;
            if (_pointLightAreaCookieAverageColors.Length < sourceCount) sourceCount = _pointLightAreaCookieAverageColors.Length;
            if (pointInstances.Length < sourceCount) sourceCount = pointInstances.Length;
            for (int i = 0; i < sourceCount; i++) {
                if (_pointLightCustomIDs[i] != customId || _customSourceTypes[i] < 3) continue;
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
                if (sourceIndex >= _customSourceTypes.Length || _customSourceTypes[sourceIndex] < 3) continue; // 3/4: single texture/material cookie sources
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
            // Calling this method is an explicit retry point. The automatic update loop skips it while the allocation-failure latch is set, so a failed Create cannot thrash every frame.
            _shadowTextureAllocationFailed = false;
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!Application.isPlaying) CaptureEditorShadowSourceState();
#endif
            // A full atlas copy replaces every direct baker's finished faces with its tiny registration texture, so those bakers must restart their face cycle.
            int directOwnerCount = PointLightVolumeInstances.Length;
            for (int i = 0; i < directOwnerCount; i++) {
                PointLightVolumeInstance directOwner = PointLightVolumeInstances[i];
                if (directOwner != null) directOwner.InvalidateRuntimeDirectShadowAtlas();
            }
            BuildShadowTextureSourceCache();
            if (_shadowTextureArrayDepth <= 0) { // No shadow sources are active, so release the stale runtime texture array
                if (ShadowTextures != null) {
                    ReleaseRuntimeRenderTexture(ShadowTextures);
                    ShadowTextures = null;
                }
                _shadowTexturesInitialized = true;
                _shadowTextureAllocationFailed = false;
                return;
            }
            if (!EnsureRuntimeShadowTextures(ShadowTexturesWidth, ShadowTexturesHeight, _shadowTextureArrayDepth)) return;
            TryInitialize();
            VRCShader.SetGlobalTexture(_pointLightShadowTextureID, ShadowTextures);
            BlitShadowTextures(false);
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
        }

        // Updates one shadow texture-array slice for runtime bakers that already manage their own refresh loop
        public void UpdatePointLightShadowTextureSlice(PointLightVolumeInstance instance, int sourceSlice) {
            UpdatePointLightShadowTextureRange(instance, sourceSlice, 1);
        }

        // Copies a contiguous runtime-baker face range with one cross-behaviour call.
        public void UpdatePointLightShadowTextureRange(PointLightVolumeInstance instance, int firstSourceSlice, int sourceSliceCount) {
            if (instance == null) return;
            Texture sourceTexture = instance.ShadowMapTexture;
            if (sourceTexture == null || sourceSliceCount <= 0) return;

            if (!_shadowTexturesInitialized || ShadowTextures == null || _shadowTextureArrayDepth <= 0) ReinitializeShadowTextures();
            if (ShadowTextures == null || _shadowTextureArrayDepth <= 0) return;

            int shadowId = (int)instance.ShadowMapID;
            if (shadowId < 0) return;

            bool isCubemapShadow = shadowId < ShadowCubemapsCount;
            firstSourceSlice = isCubemapShadow ? Mathf.Clamp(firstSourceSlice, 0, 5) : 0;
            sourceSliceCount = isCubemapShadow ? Mathf.Clamp(sourceSliceCount, 1, 6 - firstSourceSlice) : 1;
            int firstTargetSlice = isCubemapShadow ? shadowId * 6 + firstSourceSlice : ShadowCubemapsCount * 6 + shadowId - ShadowCubemapsCount;
            if (firstTargetSlice < 0 || firstTargetSlice + sourceSliceCount > _shadowTextureArrayDepth) return;

            for (int i = 0; i < sourceSliceCount; i++) {
                int sourceSlice = firstSourceSlice + i;
                int targetSlice = firstTargetSlice + i;
                if (instance.ShadowMapTextureIsCubemap) {
                    BlitCubemapFace(sourceTexture, ShadowTextures, sourceSlice, targetSlice);
                } else {
                    VRCGraphics.Blit(sourceTexture, ShadowTextures, instance.ShadowMapTextureHasDepthSlices ? sourceSlice : 0, targetSlice);
                }
            }
        }

        // Builds deduplicated source arrays and per-instance shader IDs for the runtime shadow texture array
        private void BuildShadowTextureSourceCache() {

            int count = PointLightVolumeInstances.Length;

            // Prepare reusable shadow texture source cache arrays for a full rebuild
            if (_pointLightShadowIDs.Length < count || _shadowSourceTypes.Length < count) {
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
            } else {
                for (int i = 0; i < _shadowCubemapTextureCount; i++) _shadowCubemapTextures[i] = null;
                for (int i = 0; i < _shadowCubemapMaterialCount; i++) _shadowCubemapMaterials[i] = null;
                for (int i = 0; i < _shadowSingleTextureCount; i++) _shadowSingleTextures[i] = null;
                for (int i = 0; i < _shadowSingleMaterialCount; i++) _shadowSingleMaterials[i] = null;
            }
            for (int i = 0; i < _pointLightShadowIDs.Length; i++) {
                _pointLightShadowIDs[i] = -1;
                _shadowSourceTypes[i] = 0;
            }

            int cubemapTextureCount = 0;
            int cubemapMaterialCount = 0;
            int singleTextureCount = 0;
            int singleMaterialCount = 0;
            HasAutoShadowTextureUpdates = false;

            // Iterate the registry once and collect unique shadow sources in reusable arrays
            for (int i = 0; i < count; i++) {

                // Start every point light unresolved. Only valid shadow sources receive a shadow texture ID
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                if (instance == null || !instance.IsActive) continue;
                if (instance.ShadowMapID < 0 || (instance.ShadowMapTexture == null && instance.ShadowMapMaterial == null)) {
                    instance.ShadowMapID = -1;
                    continue;
                }

                Texture textureSource = instance.ShadowMapTexture;
                // Point and area emitters are omnidirectional shadow receivers in the shader ABI. Canonicalize low-level/runtime data here so only spot lights may occupy a single slice.
                bool usesCubemapShadow = instance.LightType != 1 || instance.ShadowMapUsesCubemap;
                instance.ShadowMapUsesCubemap = usesCubemapShadow;

                if (textureSource != null) { // Texture shadows mode

                    bool autoUpdate = instance.AutoUpdateShadowMap;
                    if (usesCubemapShadow) { // TEXTURE CUBEMAP SHADOW

                        int index = Array.IndexOf((Array)_shadowCubemapTextures, textureSource, 0, cubemapTextureCount);
                        if (index < 0) { // First use of this texture: append it and reset this source's auto-update flag for the new cache build
                            index = cubemapTextureCount;
                            _shadowCubemapTextures[cubemapTextureCount] = textureSource;
                            _shadowCubemapTextureModes[cubemapTextureCount] = instance.ShadowMapTextureIsCubemap ? 2 : (instance.ShadowMapTextureHasDepthSlices ? 1 : 0); // Texture layout: 0 = single 2D texture, 1 = Texture2DArray face slices, 2 = native Cubemap.
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

                } else if (instance.ShadowMapMaterial != null) { // Material shadows mode

                    Material materialSource = instance.ShadowMapMaterial;
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
            int cubemapsCount = cubemapTextureCount + cubemapMaterialCount;
            ShadowCubemapsCount = cubemapsCount;
            ShadowMapsCount = cubemapsCount + singleTextureCount + singleMaterialCount;
            _shadowTextureArrayDepth = cubemapsCount * 6 + singleTextureCount + singleMaterialCount;

            // Convert local source indices into final shadow-map IDs after final counts are known
            for (int i = 0; i < count; i++) {
                int index = _pointLightShadowIDs[i];
                if (index < 0) continue;
                int sourceType = _shadowSourceTypes[i];
                // SourceType 1 already uses the local cubemap texture index as the final ID; 2/3/4 need offsets.
                if (sourceType == 2) _pointLightShadowIDs[i] = cubemapTextureCount + index; // 2: cubemap materials follow cubemap textures
                else if (sourceType == 3) _pointLightShadowIDs[i] = cubemapsCount + index; // 3: single textures follow every six-slice cubemap source
                else if (sourceType == 4) _pointLightShadowIDs[i] = cubemapsCount + singleTextureCount + index; // 4: single materials follow single textures
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                instance.ShadowMapID = _pointLightShadowIDs[i];
            }

        }

        // Copies shadow sources into the runtime array. autoUpdatePass copies only sources cached for Auto Update Textures
        private void BlitShadowTextures(bool autoUpdatePass) {
            // Shadow texture sources occupy the first shadow slices, six slices per cubemap
            int cubemapTextureCount = _shadowCubemapTextureCount;
            for (int i = 0; i < cubemapTextureCount; i++) {
                if (autoUpdatePass && !_shadowCubemapTextureAutoUpdates[i]) continue;
                BlitCubemapTexture(_shadowCubemapTextures[i], _shadowCubemapTextureModes[i], i * 6, ShadowTextures);
            }
            // Shadow material sources follow texture sources and are rendered as six generated slices
            int cubemapMaterialCount = _shadowCubemapMaterialCount;
            for (int i = 0; i < cubemapMaterialCount; i++) {
                if (autoUpdatePass && !_shadowCubemapMaterialAutoUpdates[i]) continue;
                int shadowId = cubemapTextureCount + i;
                int firstSlice = shadowId * 6;
                BlitCubemapMaterial(_shadowCubemapMaterials[i], firstSlice, ShadowTextures);
            }
            // Single shadow textures follow cubemap sources and occupy one array slice each
            int singleBaseSlice = ShadowCubemapsCount * 6;
            int singleTextureCount = _shadowSingleTextureCount;
            for (int i = 0; i < singleTextureCount; i++) {
                if (autoUpdatePass && !_shadowSingleTextureAutoUpdates[i]) continue;
                Texture sourceTexture = _shadowSingleTextures[i];
                if (sourceTexture == null) continue;
                int targetSlice = singleBaseSlice + i;
                VRCGraphics.Blit(sourceTexture, ShadowTextures, 0, targetSlice);
            }
            // Single shadow materials follow single texture sources and occupy one array slice each
            int singleMaterialCount = _shadowSingleMaterialCount;
            for (int i = 0; i < singleMaterialCount; i++) {
                if (autoUpdatePass && !_shadowSingleMaterialAutoUpdates[i]) continue;
                Material sourceMaterial = _shadowSingleMaterials[i];
                if (sourceMaterial == null) continue;
                int targetSlice = singleBaseSlice + singleTextureCount + i;
                BlitMaterialSlice(sourceMaterial, 0, targetSlice, false, ShadowTextures);
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
            if (!recreate) return CustomTextures != null;
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
            if (!recreate) {
                _shadowTextureAllocationFailed = ShadowTextures == null;
                return ShadowTextures != null;
            }
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

        // Releases a runtime render texture before replacing it
        private void ReleaseRuntimeRenderTexture(RenderTexture texture) {
            if (texture == null) return;
#if COMPILER_UDONSHARP
            Destroy(texture);
#else
            RenderTexture.active = null;
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

        // Copies one cubemap face into one texture array slice using the shared face unwrap shader
        private void BlitCubemapFace(Texture sourceTexture, RenderTexture destination, int sourceFace, int targetSlice) {
            if (!EnsureCubemapFaceMaterial()) return;
            CubemapFaceMaterial.SetTexture(_cubemapSourceTexID, sourceTexture);
            CubemapFaceMaterial.SetInt(_cubemapFaceIndexID, Mathf.Clamp(sourceFace, 0, 5));
            BlitMaterialToSlice(null, CubemapFaceMaterial, destination, targetSlice);
        }

        // Writes a six-face cubemap texture source into consecutive destination array slices
        private void BlitCubemapTexture(Texture sourceTexture, int textureMode, int firstSlice, RenderTexture destination) {
            if (sourceTexture == null) return;
            for (int i = 0; i < 6; i++) {
                int targetSlice = firstSlice + i;
                if (textureMode == 2) { // Native Cubemap: unwrap the matching cubemap face into this destination slice
                    BlitCubemapFace(sourceTexture, destination, i, targetSlice);
                } else {
                    int sourceSlice = 0;
                    if (textureMode == 1) sourceSlice = i; // Texture2DArray: slices 0..5 already contain the cubemap faces
                    VRCGraphics.Blit(sourceTexture, destination, sourceSlice, targetSlice);
                }
            }
        }

        // Writes a six-face cubemap material source into consecutive destination array slices
        private void BlitCubemapMaterial(Material sourceMaterial, int firstSlice, RenderTexture destination) {
            if (sourceMaterial == null) return;
            for (int i = 0; i < 6; i++) BlitMaterialSlice(sourceMaterial, i, firstSlice + i, true, destination);
        }

        // Runs a material-only update into one texture array slice
        private void BlitMaterialSlice(Material sourceMaterial, int faceIndex, int targetSlice, bool isCubemapUpdate, RenderTexture destination) {
            if (sourceMaterial == null || destination == null) return;
            float infoSlice = targetSlice;
            float infoDepth = destination.volumeDepth;
            if (isCubemapUpdate) {
                infoSlice = Mathf.Clamp(faceIndex, 0, 5);
                infoDepth = 1.0f;
            }
            Vector4 customRenderTextureInfo = new Vector4(destination.width, destination.height, infoDepth, infoSlice);
            sourceMaterial.SetVector("_CustomRenderTextureInfo", customRenderTextureInfo);
#if UDONSHARP
            Texture blitSource = sourceMaterial.HasTexture(_cubemapMainTexID) ? sourceMaterial.GetTexture(_cubemapMainTexID) : null;
#else
            Texture blitSource = null;
#endif
            BlitMaterialToSlice(blitSource, sourceMaterial, destination, targetSlice);
        }

        // Renders one material pass into a destination texture-array slice using the active runtime API
        private void BlitMaterialToSlice(Texture sourceTexture, Material material, RenderTexture destination, int targetSlice) {
#if UDONSHARP
#if !COMPILER_UDONSHARP
            RenderTexture previousRenderTexture = RenderTexture.active;
#endif
            // Udon VRCGraphics needs a separate destination-binding blit before rendering the material into the selected slice
            if (_dummyRT == null) {
                _dummyRT = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                _dummyRT.dimension = TextureDimension.Tex2D;
                _dummyRT.useMipMap = false;
                _dummyRT.autoGenerateMips = false;
                _dummyRT.Create();
            }
            VRCGraphics.Blit(_dummyRT, destination, 0, targetSlice);
            VRCGraphics.Blit(sourceTexture, material, 0, targetSlice);
#if !COMPILER_UDONSHARP
            RenderTexture.active = previousRenderTexture == destination ? null : previousRenderTexture;
#endif
#else
            // Unity Graphics can bind the target slice directly, so the material pass can render in one blit
            RenderTexture previousRenderTexture = RenderTexture.active;
            VRCGraphics.SetRenderTarget(destination, 0, CubemapFace.Unknown, targetSlice);
            VRCGraphics.Blit(sourceTexture, material, 0);
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

        // Destroys the editor/standalone clustering material created outside the build preprocessor.
        private void DestroyClusteringMaterial() {
#if !COMPILER_UDONSHARP
            if (_generatedClusteringMaterial != null) {
                if (Application.isPlaying) Destroy(_generatedClusteringMaterial);
                else DestroyImmediate(_generatedClusteringMaterial);
                _generatedClusteringMaterial = null;
            }
#endif
            if (ClusteringMaterial != null && ClusteringMaterial.hideFlags == HideFlags.HideAndDontSave) {
                if (Application.isPlaying) Destroy(ClusteringMaterial);
                else DestroyImmediate(ClusteringMaterial);
                ClusteringMaterial = null;
            }
        }

#endif

#endregion
    }
}
