#if !UDONSHARP && (UNITY_EDITOR || COMPILER_UDONSHARP)
#define UDONSHARP
#endif

using UnityEngine;
using UnityEngine.Rendering;

#if COMPILER_UDONSHARP
using VRCGraphics = VRC.SDKBase.VRCGraphics;
using VRCShader = VRC.SDKBase.VRCShader;
#else
using VRCGraphics = UnityEngine.Graphics;
using VRCShader = UnityEngine.Shader;
#endif

namespace VRCLightVolumes {
    
    // Runtime shadow baking companion for the primary PointLightVolumeInstance type. It must not receive a separate UdonSharpProgramAsset
    public partial class PointLightVolumeInstance {

        // Releases runtime shadow resources owned by this point light
        private void OnDestroy() {
            if (ShadowMapTexture == _runtimeShadowTexture) ShadowMapTexture = null;
            ReleaseIdleRuntimeShadowTextures();
            ReleaseRuntimeShadowRenderTexture(_runtimeShadowTexture);
            _runtimeShadowTexture = null;
            _runtimeShadowSourceInitialized = false;
        }

        // Hides the excluded renderers from the shadow camera
        private void ApplyExclusionMask() {
            int rendererCount = ExclusionMask != null ? ExclusionMask.Length : 0;
            if (rendererCount == 0) return;

            if (_shadowExclusionRendererStates == null || _shadowExclusionRendererStates.Length < rendererCount) _shadowExclusionRendererStates = new bool[rendererCount];

            _appliedExclusionMask = ExclusionMask;
            for (int i = 0; i < rendererCount; i++) {
                Renderer renderer = _appliedExclusionMask[i];
                if (renderer == null) continue;
                _shadowExclusionRendererStates[i] = renderer.forceRenderingOff;
                renderer.forceRenderingOff = true;
            }
        }

        // Restores renderer states captured by ApplyExclusionMask
        private void RestoreExclusionMask() {
            if (_appliedExclusionMask == null) return;

            for (int i = _appliedExclusionMask.Length - 1; i >= 0; i--) {
                Renderer renderer = _appliedExclusionMask[i];
                if (renderer != null) renderer.forceRenderingOff = _shadowExclusionRendererStates[i];
            }
            _appliedExclusionMask = null;
        }

        // Bakes shadows for current light
        public void BakeShadows() {
            bool activeInHierarchy = gameObject.activeInHierarchy;
            LightVolumeManager manager = LightVolumeManager;
            Material depthEncodeMaterial = RuntimeShadowDepthEncodeMaterial;
            Camera runtimeShadowCamera = RuntimeShadowCamera;
            bool blurRequested = Blur > 0.0001f;
            // Abort before deriving bake settings or allocating resources.
            if (!activeInHierarchy || manager == null || runtimeShadowCamera == null || depthEncodeMaterial == null || (blurRequested && RuntimeShadowBlurMaterial == null)) {
                ReleaseIdleRuntimeShadowTextures();
                return;
            }

            // Baking is a complete public operation: synchronize shader-facing transform data from the current GameObject before deriving range and receiver metadata.
            UpdateTransformCore();
            bool rangeChanged = IsRangeDirty;
            int bakeResolution = Mathf.Max(RuntimeShadowResolution, 16);
            bool useCubemapShadow = LightType != 1 || ShadowMapUsesCubemap; // 1: spot
            int bakeSliceCount = useCubemapShadow ? 6 : 1;
            bool useSphericalBlur = RuntimeShadowSphericalBlur;
            Color lightColor = Color;
            bool hasRuntimeEmission = Intensity != 0f && (lightColor.r != 0f || lightColor.g != 0f || lightColor.b != 0f);
            bool useDirectOutput = RuntimeShadowDirectOutput && IsActive && enabled && hasRuntimeEmission && manager.ShadowTexturesWidth == bakeResolution && manager.ShadowTexturesHeight == bakeResolution;
            Transform runtimeShadowCameraTransform = runtimeShadowCamera.transform;
            if (!_runtimeShadowShaderPropertiesInitialized) InitializeRuntimeShadowShaderProperties();
            if (rangeChanged) manager.RecalculatePointLightRange(this);

            RenderTextureFormat format = manager.ShadowTextureFormat == ShadowTextureFormatHalf ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGBFloat;
            Transform lightTransform = transform;
            Vector3 bakePosition = lightTransform.position;
            Quaternion bakeRotation = lightTransform.rotation;
            float bakeNearClip = Mathf.Max(NearClip, 0.0001f);
            float bakeFarClip = FarClip > 0f ? FarClip : Mathf.Sqrt(Mathf.Max(SquaredRange, 0.000001f));
            if (!hasRuntimeEmission && FarClip <= 0f && BakedFarClip > bakeNearClip) bakeFarClip = BakedFarClip;
            bakeFarClip = Mathf.Max(bakeFarClip, bakeNearClip + 0.0001f);
            bool receiverClipChanged = _runtimeShadowReceiverNearClip != bakeNearClip || _runtimeShadowReceiverFarClip != bakeFarClip || BakedFarClip != bakeFarClip;
            float bakeFieldOfView;
            float bakeTanHalfFov;
            if (useCubemapShadow) {
                bakeFieldOfView = 90f;
                bakeTanHalfFov = 1f;
            } else {
                bakeFieldOfView = Mathf.Clamp(Angle * Mathf.Rad2Deg * 2f, 0.1f, 179.9f);
                bakeTanHalfFov = Mathf.Tan(bakeFieldOfView * 0.5f * Mathf.Deg2Rad);
            }
            bool blurUsesUniformRadius = ContactHardening <= 0f;

            // Prepare RuntimeShadowDepthTexture
            if (!EnsureRuntimeShadowDepthTexture(bakeResolution)) {
                ReleaseIdleRuntimeShadowTextures();
                return;
            }
            if (blurRequested) { // Prepare blur texture and material
                RenderTexture scratchTexture = EnsureRuntimeShadowOwnedArrayTexture(_runtimeShadowBlurTempTexture, format, bakeResolution, bakeSliceCount);
                if (scratchTexture == null) {
                    ReleaseIdleRuntimeShadowTextures();
                    return;
                }
                _runtimeShadowBlurTempTexture = scratchTexture;
                PrepareRuntimeShadowBlurMaterial(blurUsesUniformRadius, bakeTanHalfFov, bakeResolution, useCubemapShadow, useSphericalBlur);
            } else if (_runtimeShadowBlurTempTexture != null) { // Release blur texture if not needed
                ReleaseRuntimeShadowRenderTexture(_runtimeShadowBlurTempTexture);
                _runtimeShadowBlurTempTexture = null;
            }
#if COMPILER_UDONSHARP
            if (_runtimeShadowMaterialBlitInputTexture == null) {
                RenderTexture inputTexture = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                if (!inputTexture.Create()) {
                    ReleaseRuntimeShadowRenderTexture(inputTexture);
                    ReleaseIdleRuntimeShadowTextures();
                    return;
                }
                _runtimeShadowMaterialBlitInputTexture = inputTexture;
            }
#endif

            // Select either the final Manager atlas range or a persistent normal source
            RenderTexture outputTexture;
            int outputBaseSlice = 0;
            if (useDirectOutput) { // Reserve direct output in the Manager atlas
                // Manager temporarily clears the old source reference to reserve a direct atlas range. The old source texture remains alive until the direct bake succeeds.
                outputBaseSlice = manager.PreparePointLightDirectShadowOutput(this);
                outputTexture = manager.ShadowTextures;
                if (outputTexture == null || outputBaseSlice < 0) {
                    _runtimeShadowSourceInitialized = false; // Keep valid scratch for the next realtime tick instead of reallocating it while the Manager rejects atlas retries.
                    return;
                }
            } else { // Prepare not direct mode
                outputTexture = EnsureRuntimeShadowOwnedArrayTexture(_runtimeShadowTexture, format, bakeResolution, bakeSliceCount);
                if (outputTexture == null) {
                    ReleaseIdleRuntimeShadowTextures();
                    return;
                }
                _runtimeShadowTexture = outputTexture;
            }
            // Preparing camera
            runtimeShadowCamera.fieldOfView = bakeFieldOfView;
            runtimeShadowCamera.nearClipPlane = bakeNearClip;
            runtimeShadowCamera.farClipPlane = bakeFarClip;
            runtimeShadowCamera.cullingMask = LayerMask;
            runtimeShadowCameraTransform.position = bakePosition;
            runtimeShadowCamera.targetTexture = _runtimeShadowDepthTexture;
            
            // Preparing depth encode material
            depthEncodeMaterial.SetFloat(_runtimeShadowFarClipID, bakeFarClip);
            depthEncodeMaterial.SetFloat(_runtimeShadowNearClipID, bakeNearClip);
            depthEncodeMaterial.SetFloat(_runtimeShadowBiasID, Mathf.Max(Bias, 0f));
            depthEncodeMaterial.SetFloat(_runtimeShadowTanHalfFovID, bakeTanHalfFov);
            depthEncodeMaterial.SetTexture(_runtimeShadowDepthTextureID, _runtimeShadowDepthTexture, RenderTextureSubElement.Depth);
            
            ApplyExclusionMask();

            if (useCubemapShadow) { // Bake shadow cubemap
                for (int face = 0; face < bakeSliceCount; face++) {
                    if (face == 0) runtimeShadowCameraTransform.rotation = bakeRotation * _runtimeShadowFaceRotation0;
                    else if (face == 1) runtimeShadowCameraTransform.rotation = bakeRotation * _runtimeShadowFaceRotation1;
                    else if (face == 2) runtimeShadowCameraTransform.rotation = bakeRotation * _runtimeShadowFaceRotation2;
                    else if (face == 3) runtimeShadowCameraTransform.rotation = bakeRotation * _runtimeShadowFaceRotation3;
                    else if (face == 4) runtimeShadowCameraTransform.rotation = bakeRotation * _runtimeShadowFaceRotation4;
                    else runtimeShadowCameraTransform.rotation = bakeRotation;
                    runtimeShadowCamera.Render();
                    BlitRuntimeShadowMaterialToSlice(_runtimeShadowDepthTexture, depthEncodeMaterial, outputTexture, outputBaseSlice + face);
                }
            } else { // Bake single shadow slice
                runtimeShadowCameraTransform.rotation = bakeRotation;
                runtimeShadowCamera.Render();
                BlitRuntimeShadowMaterialToSlice(_runtimeShadowDepthTexture, depthEncodeMaterial, outputTexture, outputBaseSlice);
            }

            RestoreExclusionMask();
            runtimeShadowCamera.targetTexture = null;
            
            // Blur shadows
            if (blurRequested) BlurRuntimeShadowSlices(bakeSliceCount, outputTexture, outputBaseSlice, blurUsesUniformRadius, useSphericalBlur);

            _runtimeShadowReceiverNearClip = bakeNearClip;
            _runtimeShadowReceiverFarClip = bakeFarClip;
            BakedFarClip = bakeFarClip;
            bool shadowDataChanged = ApplyRuntimeShadowSourceInternal(bakePosition, bakeRotation, rangeChanged || receiverClipChanged, useDirectOutput, useCubemapShadow);
            if (useDirectOutput) { // Publish the completed direct bake
                _runtimeShadowSourceInitialized = true;
                if (_runtimeShadowTexture != null) { // A normal source from an earlier mode is no longer needed once direct output owns a valid final range
                    ReleaseRuntimeShadowRenderTexture(_runtimeShadowTexture);
                    _runtimeShadowTexture = null;
                }
                if (shadowDataChanged) manager.UpdateVolumes();
            } else { // Publish the completed normal bake
                if (IsActive) {
                    bool rebuiltShadowArray = !_runtimeShadowSourceInitialized || manager.ShadowTextures == null || manager.ShadowMapsCount <= 0 || ShadowMapID < 0f;
                    _runtimeShadowSourceInitialized = manager.UpdatePointLightShadowTexture(this);
                    if (!rebuiltShadowArray && shadowDataChanged) manager.UpdateVolumes();
                } else { // Keep the source unpublished while inactive
                    _runtimeShadowSourceInitialized = false;
                }
                // A realtime direct request falls back to this path when its bake resolution differs from the atlas. Keep reusable scratch until the baker explicitly stops.
                if (!RuntimeShadowDirectOutput) ReleaseIdleRuntimeShadowTextures();
            }
        }

        // Releases retained bake scratch without dropping the persistent normal source or the last direct atlas result
        public void _ReleaseRuntimeShadowBakeResources() {
            ReleaseIdleRuntimeShadowTextures();
        }

        // Creates or validates the camera depth render target
        private bool EnsureRuntimeShadowDepthTexture(int resolution) {
            if (_runtimeShadowDepthTexture != null && _runtimeShadowDepthTexture.width == resolution && _runtimeShadowDepthTexture.height == resolution) return true;

            RenderTexture replacementTexture = new RenderTexture(resolution, resolution, 32, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);
            replacementTexture.dimension = TextureDimension.Tex2D;
            replacementTexture.useMipMap = false;
            replacementTexture.autoGenerateMips = false;
            replacementTexture.wrapMode = TextureWrapMode.Clamp;
            replacementTexture.filterMode = FilterMode.Point;
            replacementTexture.anisoLevel = 0;
#if !COMPILER_UDONSHARP
            replacementTexture.hideFlags = HideFlags.HideAndDontSave;
#endif
            if (!replacementTexture.Create()) {
                ReleaseRuntimeShadowRenderTexture(replacementTexture);
                return false;
            }
            ReleaseRuntimeShadowRenderTexture(_runtimeShadowDepthTexture);
            _runtimeShadowDepthTexture = replacementTexture;
            return true;
        }

        // Reuses a matching array or replaces and releases it after the new allocation succeeds
        private RenderTexture EnsureRuntimeShadowOwnedArrayTexture(RenderTexture texture, RenderTextureFormat format, int resolution, int sliceCount) {
            if (texture != null && texture.width == resolution && texture.height == resolution && texture.volumeDepth == sliceCount) return texture;

            RenderTexture replacementTexture = new RenderTexture(resolution, resolution, 0, format, RenderTextureReadWrite.Linear);
            replacementTexture.dimension = TextureDimension.Tex2DArray;
            replacementTexture.volumeDepth = sliceCount;
            replacementTexture.useMipMap = false;
            replacementTexture.autoGenerateMips = false;
            replacementTexture.wrapMode = TextureWrapMode.Clamp;
            replacementTexture.filterMode = FilterMode.Bilinear;
            replacementTexture.anisoLevel = 0;
#if !COMPILER_UDONSHARP
            replacementTexture.hideFlags = HideFlags.HideAndDontSave;
#endif
            if (!replacementTexture.Create()) {
                ReleaseRuntimeShadowRenderTexture(replacementTexture);
                return null;
            }
            ReleaseRuntimeShadowRenderTexture(texture);
            return replacementTexture;
        }

        // Updates this light's runtime shadow source and returns whether shader metadata changed
        private bool ApplyRuntimeShadowSourceInternal(Vector3 bakePosition, Quaternion bakeRotation, bool rangeChanged, bool useDirectOutput, bool useCubemapShadow) {
            
            Texture sourceTexture = useDirectOutput ? null : _runtimeShadowTexture;
            bool sourceHasSlices = !useDirectOutput && sourceTexture != null && useCubemapShadow;
            bool sourceChanged = ShadowMapID < 0 || ShadowMapTexture != sourceTexture || ShadowMapMaterial != null || AutoUpdateShadowMap || ShadowMapTextureIsCubemap || ShadowMapTextureHasDepthSlices != sourceHasSlices || ShadowMapUsesCubemap != useCubemapShadow;
            
            // Runtime shadow metadata must match the exact transform used by this bake. Unity's Vector3/Quaternion operators are approximate, which can otherwise retain a nearby stale origin/rotation and prevent the exact same-origin receiver path from engaging.
            bool bakePositionChanged = ShadowBakePosition.x != bakePosition.x || ShadowBakePosition.y != bakePosition.y || ShadowBakePosition.z != bakePosition.z;
            bool bakeRotationChanged = ShadowBakeRotation.x != bakeRotation.x || ShadowBakeRotation.y != bakeRotation.y || ShadowBakeRotation.z != bakeRotation.z || ShadowBakeRotation.w != bakeRotation.w;
            bool metadataChanged = sourceChanged || rangeChanged || (WorldSpaceShadows && (bakePositionChanged || bakeRotationChanged));

            if (ShadowMapID < 0) ShadowMapID = 0f;
            if (sourceChanged) {
                ShadowMapTexture = sourceTexture;
                ShadowMapMaterial = null;
                AutoUpdateShadowMap = false;
                ShadowMapTextureIsCubemap = false;
                ShadowMapTextureHasDepthSlices = sourceHasSlices;
                ShadowMapUsesCubemap = useCubemapShadow;
                _runtimeShadowSourceInitialized = false;
            }
            if (bakePositionChanged) ShadowBakePosition = bakePosition;
            if (bakeRotationChanged) ShadowBakeRotation = bakeRotation;
            return metadataChanged;
        }

        // Applies the selected blur to every slice of one complete normal source or direct range
        private void BlurRuntimeShadowSlices(int sliceCount, RenderTexture outputTexture, int outputBaseSlice, bool blurUsesUniformRadius, bool useSphericalBlur) {
            Material blurMaterial = RuntimeShadowBlurMaterial;

            blurMaterial.SetTexture(_runtimeShadowSourceArrayID, outputTexture);
            blurMaterial.SetFloat(_runtimeShadowSourceBaseSliceID, outputBaseSlice);
            if (!blurUsesUniformRadius) {
                blurMaterial.SetTexture(_runtimeShadowDepthArrayID, outputTexture);
                blurMaterial.SetFloat(_runtimeShadowDepthBaseSliceID, outputBaseSlice);
            }
            if (!useSphericalBlur) blurMaterial.SetVector(_runtimeShadowBlurDirectionID, Vector2.right);

            // Both blur modes write their first pass into zero-based scratch slices.
            for (int face = 0; face < sliceCount; face++) {
                blurMaterial.SetInt(_runtimeShadowFaceIndexID, face);
                BlitRuntimeShadowMaterialToSlice(outputTexture, blurMaterial, _runtimeShadowBlurTempTexture, face);
            }

            if (useSphericalBlur) { // Spherical blur copies its seam-aware single pass back to the output

                for (int face = 0; face < sliceCount; face++) {
                    VRCGraphics.Blit(_runtimeShadowBlurTempTexture, outputTexture, face, outputBaseSlice + face);
                }
            } else { // Planar blur is cheaper: horizontal pass into scratch, then vertical pass back to output
                blurMaterial.SetTexture(_runtimeShadowSourceArrayID, _runtimeShadowBlurTempTexture);
                blurMaterial.SetFloat(_runtimeShadowSourceBaseSliceID, 0);
                blurMaterial.SetVector(_runtimeShadowBlurDirectionID, Vector2.up);
                if (!blurUsesUniformRadius) {
                    blurMaterial.SetTexture(_runtimeShadowDepthArrayID, _runtimeShadowBlurTempTexture);
                    blurMaterial.SetFloat(_runtimeShadowDepthBaseSliceID, 0);
                }

                for (int face = 0; face < sliceCount; face++) {
                    blurMaterial.SetInt(_runtimeShadowFaceIndexID, face);
                    BlitRuntimeShadowMaterialToSlice(_runtimeShadowBlurTempTexture, blurMaterial, outputTexture, outputBaseSlice + face);
                }
            }
        }

        // Prepares blur material constants and keyword state
        private void PrepareRuntimeShadowBlurMaterial(bool blurUsesUniformRadius, float tanHalfFov, int bakeResolution, bool useCubemapShadow, bool useSphericalBlur) {
            Material blurMaterial = RuntimeShadowBlurMaterial;

            // Convert public bake settings to local shader keyword state
            int qualityPreset = Mathf.Clamp(RuntimeShadowBlurSamplePreset, 0, 3);
            int uniformKeyword = blurUsesUniformRadius ? 1 : 0;
            int singleSliceKeyword = !useCubemapShadow ? 1 : 0;
            int sphericalKeyword = useSphericalBlur ? 1 : 0;
            LightVolumeManager sharedMaterialManager = LightVolumeManager;
            bool useSharedBlurState = sharedMaterialManager != null && blurMaterial == sharedMaterialManager.RuntimeShadowBlurMaterial;
            bool keywordStateChanged = !useSharedBlurState || sharedMaterialManager.RuntimeShadowBlurQualityPreset != qualityPreset || sharedMaterialManager.RuntimeShadowBlurUniformKeyword != uniformKeyword || sharedMaterialManager.RuntimeShadowBlurDirectKeyword != singleSliceKeyword;
            if (keywordStateChanged) {
                // Shared manager material tracks the stable quality, radius and projection variants
                blurMaterial.DisableKeyword(ShadowQualityKeywordLow);
                blurMaterial.DisableKeyword(ShadowQualityKeywordMedium);
                blurMaterial.DisableKeyword(ShadowQualityKeywordHigh);
                blurMaterial.DisableKeyword(ShadowQualityKeywordEditor);
                if (qualityPreset == 0) blurMaterial.EnableKeyword(ShadowQualityKeywordLow);
                else if (qualityPreset == 3) {
                    blurMaterial.EnableKeyword(ShadowQualityKeywordHigh);
                    blurMaterial.EnableKeyword(ShadowQualityKeywordEditor);
                }
                else if (qualityPreset == 2) blurMaterial.EnableKeyword(ShadowQualityKeywordHigh);
                else blurMaterial.EnableKeyword(ShadowQualityKeywordMedium);

                if (blurUsesUniformRadius) blurMaterial.EnableKeyword(ShadowBlurKeywordUniform);
                else blurMaterial.DisableKeyword(ShadowBlurKeywordUniform);

                if (!useCubemapShadow) blurMaterial.EnableKeyword(ShadowBlurKeywordDirect);
                else blurMaterial.DisableKeyword(ShadowBlurKeywordDirect);

                if (useSharedBlurState) {
                    sharedMaterialManager.RuntimeShadowBlurQualityPreset = qualityPreset;
                    sharedMaterialManager.RuntimeShadowBlurUniformKeyword = uniformKeyword;
                    sharedMaterialManager.RuntimeShadowBlurDirectKeyword = singleSliceKeyword;
                }
            }

            // Spherical mode also changes the CPU-side pass count. Apply it for every bake instead of trusting serialized Manager cache state that may outlive or be restored separately from the material.
            if (useSphericalBlur) blurMaterial.EnableKeyword(ShadowBlurKeywordSpherical);
            else blurMaterial.DisableKeyword(ShadowBlurKeywordSpherical);
            if (useSharedBlurState) sharedMaterialManager.RuntimeShadowBlurSphericalKeyword = sphericalKeyword;

            // Upload blur constants after keywords select planar, spherical and single-slice projection code
            blurMaterial.SetFloat(_runtimeShadowBlurRadiusID, Mathf.Max(Blur, 0f) * (bakeResolution / ShadowBlurBaseResolution));
            if (blurUsesUniformRadius) blurMaterial.SetFloat(_runtimeShadowBlurDepthID, 0f);
            // Contact hardening is exponential so low values stay subtle while high values expand quickly
            else blurMaterial.SetFloat(_runtimeShadowBlurDepthID, (Mathf.Pow(10f, Mathf.Clamp01(ContactHardening)) - 1f) * 0.1111111111f);

            blurMaterial.SetFloat(_runtimeShadowInvResolutionID, 1f / bakeResolution);
            // Single-slice spot blur needs projection scale compensation; cubemap blur does not
            if (!useCubemapShadow) blurMaterial.SetFloat(_runtimeShadowTanHalfFovID, tanHalfFov);
        }

        // Initializes all shader property IDs used by runtime shadow materials.
        private void InitializeRuntimeShadowShaderProperties() {
            _runtimeShadowDepthTextureID = VRCShader.PropertyToID("_ShadowDepthTex");
            _runtimeShadowFarClipID = VRCShader.PropertyToID("_ShadowFarClip");
            _runtimeShadowNearClipID = VRCShader.PropertyToID("_ShadowNearClip");
            _runtimeShadowBiasID = VRCShader.PropertyToID("_ShadowBakeBias");
            _runtimeShadowTanHalfFovID = VRCShader.PropertyToID("_ShadowTanHalfFov");
            _runtimeShadowSourceArrayID = VRCShader.PropertyToID("_SourceArrayTex");
            _runtimeShadowDepthArrayID = VRCShader.PropertyToID("_DepthArrayTex");
            _runtimeShadowFaceIndexID = VRCShader.PropertyToID("_FaceIndex");
            _runtimeShadowSourceBaseSliceID = VRCShader.PropertyToID("_SourceBaseSlice");
            _runtimeShadowDepthBaseSliceID = VRCShader.PropertyToID("_DepthBaseSlice");
            _runtimeShadowBlurDirectionID = VRCShader.PropertyToID("_BlurDirection");
            _runtimeShadowBlurRadiusID = VRCShader.PropertyToID("_BlurRadius");
            _runtimeShadowBlurDepthID = VRCShader.PropertyToID("_BlurDepth");
            _runtimeShadowInvResolutionID = VRCShader.PropertyToID("_InvResolution");
            _runtimeShadowShaderPropertiesInitialized = true;
        }

        // Renders one material pass into a destination texture-array slice
        private void BlitRuntimeShadowMaterialToSlice(Texture sourceTexture, Material material, RenderTexture destination, int targetSlice) {
#if COMPILER_UDONSHARP
            Texture blitSource = _runtimeShadowMaterialBlitInputTexture;
            VRCGraphics.Blit(blitSource, destination, 0, targetSlice);
            VRCGraphics.Blit(blitSource, material, 0, targetSlice);
#else
            RenderTexture previousRenderTexture = RenderTexture.active;
            VRCGraphics.SetRenderTarget(destination, 0, CubemapFace.Unknown, targetSlice);
            VRCGraphics.Blit(sourceTexture, material, 0);
            RenderTexture.active = previousRenderTexture == destination ? null : previousRenderTexture;
#endif
        }

        // Releases one owned runtime shadow render texture
        private void ReleaseRuntimeShadowRenderTexture(RenderTexture texture) {
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

        // Releases temporary bake buffers while keeping the persistent normal source or direct atlas result alive
        private void ReleaseIdleRuntimeShadowTextures() {
            ReleaseRuntimeShadowRenderTexture(_runtimeShadowDepthTexture);
            ReleaseRuntimeShadowRenderTexture(_runtimeShadowBlurTempTexture);
            ReleaseRuntimeShadowRenderTexture(_runtimeShadowMaterialBlitInputTexture);
            _runtimeShadowDepthTexture = null;
            _runtimeShadowBlurTempTexture = null;
            _runtimeShadowMaterialBlitInputTexture = null;
        }
    }
}
