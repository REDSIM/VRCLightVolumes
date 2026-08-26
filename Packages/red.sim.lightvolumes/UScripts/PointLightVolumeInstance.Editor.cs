#if UNITY_EDITOR && !COMPILER_UDONSHARP
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCLightVolumes {
    // Editor-only companion for authoring, previews and source-change detection. This is the same proxy type and must not receive a separate UdonSharpProgramAsset.
    public partial class PointLightVolumeInstance {
#region Editor Preview And Proxy State

        // Editor-only views of existing runtime state; no backing fields are added.
        internal bool RegisteredWithManagerPreview => _isRegisteredWithManager;
        internal bool RuntimeShadowSourceInitializedPreview => _runtimeShadowSourceInitialized;
        internal float RuntimeShadowReceiverNearClipPreview => _runtimeShadowReceiverNearClip;
        internal float RuntimeShadowReceiverFarClipPreview => _runtimeShadowReceiverFarClip;
        internal RenderTexture RuntimeShadowDepthTexturePreview => _runtimeShadowDepthTexture;
        internal RenderTexture RuntimeShadowTexturePreview => _runtimeShadowTexture;

        // UdonSharp's Play Mode inspector serializer reads fields by their declared heap type.
        // A runtime-created RenderTexture stored in the Texture-typed ShadowMapTexture field is
        // therefore read as null, even though the exact RenderTexture-typed owner below survives.
        // Restore both the runtime bridge and the ordinary Shadow Map proxy field between the
        // wrapper's Udon-to-proxy and proxy-to-Udon passes. This keeps the standard ObjectField
        // interactive in Play Mode without adding a second read-only Inspector control.
        internal void RestoreRuntimeShadowSourceForInspector() {
            if (!Application.isPlaying) return;
            Texture runtimeSource = GetRuntimeShadowSourceForInspector();
            if (runtimeSource == null) return;
            ShadowMap = runtimeSource;
            if (!RuntimeShadowDirectOutput && _runtimeShadowTexture != null) {
                ShadowMapTexture = _runtimeShadowTexture;
                ShadowMapMaterial = null;
            }
        }

        // Returns the source represented by the normal Shadow Map field in Play Mode.
        private Texture GetRuntimeShadowSourceForInspector() {
            if (!_runtimeShadowSourceInitialized) return null;
            if (RuntimeShadowDirectOutput && LightVolumeManager != null && LightVolumeManager.ShadowTextures != null)
                return LightVolumeManager.ShadowTextures;
            return _runtimeShadowTexture != null ? _runtimeShadowTexture : ShadowMapTexture;
        }

        // Drops an effective runtime source when the user clears or replaces Shadow Map in Play Mode.
        private void DiscardRuntimeShadowSourceFromInspector() {
            Texture runtimeSource = GetRuntimeShadowSourceForInspector();
            if (ShadowMap == runtimeSource) ShadowMap = null;
            if (_runtimeShadowTexture != null) {
                if (ShadowMapTexture == _runtimeShadowTexture) ShadowMapTexture = null;
                ReleaseRuntimeShadowRenderTexture(_runtimeShadowTexture);
                _runtimeShadowTexture = null;
            }
            _runtimeShadowSourceInitialized = false;
        }

        // Caches editor-observed scalar values after the editor coordinator mirrors them without proxy polling.
        internal void CacheEditorObservedValues() {
            _old_Color = Color;
            _old_Intensity = Intensity;
            _old_ShadingStrength = ShadingStrength;
        }

#endregion

#region Editor Shadow Safety

        // Editor safety net used by the asset baker's finally block if rendering throws unexpectedly.
        internal void EditorRestoreExclusionMask() {
            RestoreExclusionMask();
        }

#endregion

#region Editor Authoring Projection

        // Returns the persistent source selected by the authoring projection mode.
        internal UnityEngine.Object GetProjectionSource() {
            if (LightType == 2) return Cookie; // 2: area
            if (Projection == 1) return FalloffLUT; // 1: LUT
            if (Projection != 2) return null; // 2: custom
            if (LightType == 0) return Cubemap; // 0: point
            if (LightType == 1) return Cookie; // 1: spot
            return null;
        }

        // Returns the active authoring projection source when it is a texture.
        internal Texture GetCustomTexture() {
            return GetProjectionSource() as Texture;
        }

        // Returns the active authoring projection source when it is a material.
        internal Material GetCustomTextureMaterial() {
            return GetProjectionSource() as Material;
        }

        // Checks whether the selected source is valid for the current light and projection type.
        internal bool HasProjectionSource() {
            UnityEngine.Object source = GetProjectionSource();
            return source is Texture || source is Material;
        }

        // Resolves authoring settings to the projection mode consumed at runtime.
        private int GetAuthoringProjectionMode() {
            if (!HasProjectionSource()) return 0;
            if (LightType == 2) return 2;
            return Projection == 1 ? 1 : Projection == 2 ? 2 : 0;
        }

        // Checks whether an editor source may change without replacing its object reference.
        private static bool IsAnimatedEditorSource(UnityEngine.Object source) {
            return source is RenderTexture || source is Material;
        }

        // Checks whether a texture is a Cubemap or cube-dimension RenderTexture.
        private static bool IsEditorCubemapTexture(Texture texture) {
            if (texture is Cubemap) return true;
            RenderTexture renderTexture = texture as RenderTexture;
            return renderTexture != null && renderTexture.dimension == TextureDimension.Cube;
        }

        // Checks whether a texture stores independent array or cubemap-face slices.
        private static bool EditorTextureHasDepthSlices(Texture texture) {
            RenderTexture renderTexture = texture as RenderTexture;
            if (renderTexture != null) return renderTexture.dimension == TextureDimension.Tex2DArray && renderTexture.volumeDepth > 1;
            return texture is Texture2DArray;
        }

        // Returns the persistent shadow source when it is a texture.
        internal Texture GetShadowMapTexture() {
            return ShadowMap as Texture;
        }

        // Returns the persistent shadow source when it is a material.
        internal Material GetShadowMapMaterial() {
            return ShadowMap as Material;
        }

        // Resolves whether the current light settings require a six-face shadow bake.
        internal bool ShouldBakeCubemapShadows() {
            return LightType != 1 || ForceCubemapShadows || Angle >= Mathf.PI; // 1: spot
        }

        // Checks whether the assigned or generated shadow source occupies six atlas slices.
        internal bool UsesCubemapShadows() {
            Texture texture = GetShadowMapTexture();
            if (IsEditorCubemapTexture(texture) || EditorTextureHasDepthSlices(texture)) return true;
            return ShouldBakeCubemapShadows();
        }

        // Returns a positive near clip distance safe for the shadow camera.
        internal float GetShadowNearClip() {
            return Mathf.Max(NearClip, 0.0001f);
        }

        // Returns a far clip beyond the near plane, using calculated range when no override is set.
        internal float GetShadowFarClip() {
            float nearClip = GetShadowNearClip();
            float farClip = FarClip > 0f ? FarClip : GetCalculatedShadowFarClip();
            return Mathf.Max(farClip, nearClip + 0.0001f);
        }

        // Calculates shadow-camera range from the current projection, size and brightness cutoff.
        private float GetCalculatedShadowFarClip() {
            Vector3 lossyScale = transform.lossyScale;
            float averageScale = (Mathf.Abs(lossyScale.x) + Mathf.Abs(lossyScale.y) + Mathf.Abs(lossyScale.z)) / 3f;
            float cutoff = LightVolumeManager != null ? LightVolumeManager.LightsBrightnessCutoff : 0.35f;

            if (LightType == 2) {
                float width = Mathf.Max(Mathf.Abs(lossyScale.x), 0.001f);
                float height = Mathf.Max(Mathf.Abs(lossyScale.y), 0.001f);
                return Mathf.Max(Mathf.Sqrt(ComputeEditorAreaLightSquaredRange(width, height, Color, Intensity * Mathf.PI, cutoff)), 0.0001f);
            }
            if (Projection == 1 && HasProjectionSource()) return Mathf.Max(Range * averageScale, 0.0001f);

            float size = Mathf.Max(LightSourceSize * averageScale, 0.0001f);
            float luminance = Mathf.Max(Color.r, Mathf.Max(Color.g, Color.b));
            float squaredRange = Mathf.Max(Mathf.PI * 2f * luminance * Mathf.Abs(Intensity) / (cutoff * cutoff) - 1f, 0f) * size * size;
            return Mathf.Max(Mathf.Sqrt(squaredRange), 0.0001f);
        }

        // Estimates the squared culling range of an Area Light for editor shadow baking.
        private static float ComputeEditorAreaLightSquaredRange(float width, float height, Color color, float intensity, float cutoff) {
            float luminance = Mathf.Max(color.r, Mathf.Max(color.g, color.b)) * Mathf.Abs(intensity);
            if (luminance <= 0.000001f) return 0f;

            float minSolidAngle = cutoff / luminance;
            if (minSolidAngle >= Mathf.PI * 2f - 0.0001f) return 0f;
            minSolidAngle = Mathf.Max(minSolidAngle, 0.000001f);

            float area = width * height;
            float shape = 0.25f * (width * width + height * height);
            float tangent = Mathf.Tan(0.25f * minSolidAngle);
            float tangentSquared = Mathf.Max(tangent * tangent, 0.000001f);
            float scaledShape = tangentSquared * shape;
            float discriminant = Mathf.Sqrt(scaledShape * scaledShape + 4f * tangentSquared * area * area);
            return Mathf.Max((discriminant - scaledShape) * 0.125f / tangentSquared, 0f);
        }

        // Prevents editor synchronization from replacing a live runtime-generated shadow source.
        private bool PreserveRuntimeShadowSourceInEditor() {
            if (!Application.isPlaying || !Shadows) return false;
            Texture runtimeSource = GetRuntimeShadowSourceForInspector();
            if (runtimeSource != null) return ShadowMap == runtimeSource;
            Texture sourceTexture = GetShadowMapTexture();
            if (BakeInGame) return ShadowMapTexture != sourceTexture;
            return ShadowMapTexture != null && ShadowMapTexture != sourceTexture;
        }

        // Compares authoring projection state with the runtime fields mirrored to this instance.
        internal bool HasEditorCustomTextureChanges() {
            Texture texture = GetCustomTexture();
            Material material = GetCustomTextureMaterial();
            int mode = GetAuthoringProjectionMode();
            return CustomTexture != texture || CustomTextureMaterial != material || ProjectionMode != mode;
        }

        // Compares authoring shadow state with the runtime fields mirrored to this instance.
        internal bool HasEditorShadowTextureChanges() {
            if (PreserveRuntimeShadowSourceInEditor()) return ShadowMapUsesCubemap != ShouldBakeCubemapShadows();
            if (Application.isPlaying && _runtimeShadowSourceInitialized) return true;

            Texture texture = Shadows ? GetShadowMapTexture() : null;
            Material material = Shadows ? GetShadowMapMaterial() : null;
            bool usesCubemap = Shadows && UsesCubemapShadows();
            return ShadowMapTexture != texture || ShadowMapMaterial != material || ShadowMapUsesCubemap != usesCubemap || ShadowMapTextureIsCubemap != IsEditorCubemapTexture(texture)
                || ShadowMapTextureHasDepthSlices != EditorTextureHasDepthSlices(texture) || AutoUpdateShadowMap != (Shadows && IsAnimatedEditorSource(ShadowMap));
        }

        // Rebuilds all shader-facing data from the single serialized authoring/runtime component. The editor coordinator calls this only for explicit or coalesced changes; it never polls per object.
        internal void EditorApplyAuthoringData(bool customTexturesChanged, bool shadowTexturesChanged, bool notifyManager = true) {
            int safeLightType = Mathf.Clamp(LightType, 0, 2);
            int projectionMode = GetAuthoringProjectionMode();
            float safeSourceSize = Mathf.Max(Mathf.Abs(LightSourceSize), 0.0001f);
            float safeRange = Mathf.Max(Mathf.Abs(Range), 0.0001f);
            float safeAngle = Mathf.Clamp(Angle, 0.05f * Mathf.Deg2Rad, Mathf.PI);
            float safeFalloff = Mathf.Clamp(Falloff, 0.001f, 1f);
            float safeAspect = Mathf.Max(Mathf.Abs(SpotCookieAspect), 0.001f);
            Transform instanceTransform = transform;
            Vector3 transformPosition = instanceTransform.position;
            Quaternion transformRotation = instanceTransform.rotation;
            Vector3 lossyScale = instanceTransform.lossyScale;
            Matrix4x4 localToWorldMatrix = safeLightType == 2 ? instanceTransform.localToWorldMatrix : Matrix4x4.identity;

            LightType = safeLightType;
            ProjectionMode = projectionMode;
            LightSourceSize = safeSourceSize;
            InverseSquaredRange = 1f / ((projectionMode == 1 ? safeRange : safeSourceSize) * (projectionMode == 1 ? safeRange : safeSourceSize));
            Angle = safeAngle;
            SpotCookieAspect = safeAspect;
            ShadingStrength = Mathf.Clamp01(ShadingStrength);

            Texture customTexture = GetCustomTexture();
            Material customMaterial = GetCustomTextureMaterial();
            CustomTexture = customTexture;
            CustomTextureMaterial = customMaterial;

            bool preserveRuntimeShadow = PreserveRuntimeShadowSourceInEditor();
            if (!preserveRuntimeShadow && Application.isPlaying && _runtimeShadowSourceInitialized)
                DiscardRuntimeShadowSourceFromInspector();
            if (!preserveRuntimeShadow) {
                Texture shadowTexture = Shadows ? GetShadowMapTexture() : null;
                Material shadowMaterial = Shadows ? GetShadowMapMaterial() : null;
                bool shadowSourceChanged = ShadowMapTexture != shadowTexture || ShadowMapMaterial != shadowMaterial;
                ShadowMapTexture = shadowTexture;
                ShadowMapMaterial = shadowMaterial;
                AutoUpdateShadowMap = Shadows && IsAnimatedEditorSource(ShadowMap);
                ShadowMapTextureIsCubemap = IsEditorCubemapTexture(shadowTexture);
                ShadowMapTextureHasDepthSlices = EditorTextureHasDepthSlices(shadowTexture);
                ShadowMapUsesCubemap = Shadows && UsesCubemapShadows();
                ShadowMapID = Shadows && (shadowTexture != null || shadowMaterial != null) ? 0f : -1f;
                if (shadowSourceChanged) {
                    ShadowBakePosition = transformPosition;
                    ShadowBakeRotation = transformRotation;
                }
            }

            if (ShadowBakeResolution < 0) ShadowBakeResolution = 0;
            else if (ShadowBakeResolution > 0) ShadowBakeResolution = Mathf.Clamp(ShadowBakeResolution, 16, 2048);
            RuntimeShadowResolution = ShadowBakeResolution > 0 ? ShadowBakeResolution : LightVolumeManager != null ? Mathf.Clamp(LightVolumeManager.ShadowTexturesWidth, 16, 2048) : Mathf.Clamp(RuntimeShadowResolution, 16, 2048);
            RuntimeShadowBlurSamplePreset = Mathf.Clamp(RuntimeShadowBlurSamplePreset, 0, 2);

            Position = transformPosition;
            float averageScale = (Mathf.Abs(lossyScale.x) + Mathf.Abs(lossyScale.y) + Mathf.Abs(lossyScale.z)) / 3f;
            SquaredScale = averageScale * averageScale;
            if (safeLightType == 2) {
                Rotation = transformRotation;
                Width = Mathf.Max(Mathf.Abs(lossyScale.x), 0.001f);
                Height = Mathf.Max(Mathf.Abs(lossyScale.y), 0.001f);
                RefreshAreaCookieMirror(transformRotation, localToWorldMatrix);
                ShadowMapUsesCubemap = Shadows;
            } else if (safeLightType == 1) {
                OuterAngleTan = Mathf.Tan(safeAngle);
                OuterAngleCos = Mathf.Cos(safeAngle);
                float denominator = Mathf.Cos(safeAngle * (1f - safeFalloff)) - OuterAngleCos;
                ConeFalloff = 1f / Mathf.Max(denominator, 0.000001f);
                if (projectionMode == 2) Rotation = Quaternion.Inverse(transformRotation);
                else Direction = transformRotation * Vector3.forward;
            } else if (projectionMode != 0) {
                Rotation = Quaternion.Inverse(transformRotation);
                ShadowMapUsesCubemap = Shadows;
            }

            _prevPosition = transformPosition;
            _prevRotation = transformRotation;
            _prevScale = lossyScale;
            _old_Color = Color;
            _old_Intensity = Intensity;
            _old_ShadingStrength = ShadingStrength;
            IsRangeDirty = true;
            if (notifyManager) NotifyManager(true, customTexturesChanged, shadowTexturesChanged);
            // NotifyManager owns transition detection when requested. The explicit assignment also
            // canonicalizes build/editor paths that intentionally suppress the manager callback
            // before copying this proxy to its backing UdonBehaviour.
            IsActive = isActiveAndEnabled && Intensity != 0f && Color != Color.black;
        }

#endregion
    }
}
#endif
