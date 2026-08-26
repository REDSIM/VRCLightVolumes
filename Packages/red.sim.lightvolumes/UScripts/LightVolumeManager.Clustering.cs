#if !UDONSHARP && COMPILER_UDONSHARP
#define UDONSHARP
#endif

using UnityEngine;
using UnityEngine.Rendering;

#if UDONSHARP
using VRCGraphics = VRC.SDKBase.VRCGraphics;
#if COMPILER_UDONSHARP
using VRC.SDK3.Rendering;
using VRCShader = VRC.SDKBase.VRCShader;
#else
using VRCShader = UnityEngine.Shader;
#endif
#else
using VRCGraphics = UnityEngine.Graphics;
using VRCShader = UnityEngine.Shader;
#endif

namespace VRCLightVolumes {
    public partial class LightVolumeManager {
#region Froxel Clustering

        // Updates screen-camera froxel clustering in VRChat and safely disables it when camera data is unavailable.
        private void UpdateClustering() {
            if (!Clustering) {
                if (_clusteringActive) DisableClustering();
                return;
            }
            TryInitialize();
            int minLightCount = Mathf.Clamp(ClusteringMinLights, 1, MaxPointLightCount);
            if (_clusterGeometryUploadPending || _pointLightCount < minLightCount || _clusteringUnsupported) {
                DisableClustering();
                return;
            }

#if COMPILER_UDONSHARP
            VRCCameraSettings camera = VRCCameraSettings.ScreenCamera;
            if (camera == null || !camera.Active) {
                DisableClustering();
                return;
            }

            Vector3 position = camera.Position;
            Vector3 stereoLeftPosition = position;
            Vector3 stereoRightPosition = position;
            bool stereoEnabled = camera.StereoEnabled;
            if (stereoEnabled) {
                stereoLeftPosition = VRCCameraSettings.GetEyePosition(Camera.StereoscopicEye.Left);
                stereoRightPosition = VRCCameraSettings.GetEyePosition(Camera.StereoscopicEye.Right);
                position = (stereoLeftPosition + stereoRightPosition) * 0.5f;
            }

            float cameraFov = camera.FieldOfView;
            float cameraAspect = camera.Aspect;
            int pixelHeight = camera.PixelHeight;
            float verticalFov = cameraFov > 0.001f ? cameraFov : DefaultFroxelFov;
            float aspect = cameraAspect > 0.001f ? cameraAspect : (pixelHeight > 0 ? Mathf.Max((float)camera.PixelWidth / pixelHeight, 0.001f) : DefaultFroxelAspect);
            float rawFarClip = Mathf.Max(camera.FarClipPlane, 0.01f);

            Quaternion rotation = camera.Rotation;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            float horizontalPadding = 0f;
            float verticalPadding = 0f;
            float depthPadding = 0f;

            if (stereoEnabled) {
                Vector3 leftEyeOffset = stereoLeftPosition - position;
                Vector3 rightEyeOffset = stereoRightPosition - position;
                horizontalPadding = Mathf.Max(Mathf.Abs(Vector3.Dot(leftEyeOffset, right)), Mathf.Abs(Vector3.Dot(rightEyeOffset, right)));
                verticalPadding = Mathf.Max(Mathf.Abs(Vector3.Dot(leftEyeOffset, up)), Mathf.Abs(Vector3.Dot(rightEyeOffset, up)));
                depthPadding = Mathf.Max(Mathf.Abs(Vector3.Dot(leftEyeOffset, forward)), Mathf.Abs(Vector3.Dot(rightEyeOffset, forward)));
            }

            float nearClip = Mathf.Max(camera.NearClipPlane - depthPadding, 0.001f);
            float farClip = Mathf.Max(rawFarClip + depthPadding, nearClip + 0.001f);
            BuildClustering(position, right, up, forward, verticalFov, aspect, nearClip, farClip, horizontalPadding, verticalPadding, null);
#else
            Camera camera = Camera.main;
            if (camera == null) camera = Camera.current;
            UpdateClusteringFromCamera(camera);
#endif
        }

#if !COMPILER_UDONSHARP
        // Updates froxel clustering from an explicit Unity camera for standalone play mode and Scene View preview.
        internal void UpdateClusteringFromCamera(Camera camera) {
            if (!Clustering) {
                if (_clusteringActive) DisableClustering();
                return;
            }
            TryInitialize();
            int minLightCount = Mathf.Clamp(ClusteringMinLights, 1, MaxPointLightCount);
            if (_clusterGeometryUploadPending || _pointLightCount < minLightCount || camera == null || camera.orthographic || !ClusteringSupported()) {
                DisableClustering();
                return;
            }

            Transform cameraTransform = camera.transform;
            Vector3 position = cameraTransform.position;
            Vector3 stereoLeftPosition = position;
            Vector3 stereoRightPosition = position;
            bool stereoEnabled = camera.stereoEnabled;
            if (stereoEnabled) {
                Matrix4x4 leftEyeMatrix = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse;
                Matrix4x4 rightEyeMatrix = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse;
                Vector4 leftEyeColumn = leftEyeMatrix.GetColumn(3);
                Vector4 rightEyeColumn = rightEyeMatrix.GetColumn(3);
                stereoLeftPosition = new Vector3(leftEyeColumn.x, leftEyeColumn.y, leftEyeColumn.z);
                stereoRightPosition = new Vector3(rightEyeColumn.x, rightEyeColumn.y, rightEyeColumn.z);
                position = (stereoLeftPosition + stereoRightPosition) * 0.5f;
            }

            float verticalFov = camera.fieldOfView > 0.001f ? camera.fieldOfView : DefaultFroxelFov;
            float aspect = camera.aspect > 0.001f ? camera.aspect : DefaultFroxelAspect;
            float rawFarClip = Mathf.Max(camera.farClipPlane, 0.01f);

            Quaternion rotation = cameraTransform.rotation;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            float horizontalPadding = 0f;
            float verticalPadding = 0f;
            float depthPadding = 0f;

            if (stereoEnabled) {
                Vector3 leftEyeOffset = stereoLeftPosition - position;
                Vector3 rightEyeOffset = stereoRightPosition - position;
                horizontalPadding = Mathf.Max(Mathf.Abs(Vector3.Dot(leftEyeOffset, right)), Mathf.Abs(Vector3.Dot(rightEyeOffset, right)));
                verticalPadding = Mathf.Max(Mathf.Abs(Vector3.Dot(leftEyeOffset, up)), Mathf.Abs(Vector3.Dot(rightEyeOffset, up)));
                depthPadding = Mathf.Max(Mathf.Abs(Vector3.Dot(leftEyeOffset, forward)), Mathf.Abs(Vector3.Dot(rightEyeOffset, forward)));
            }

            float nearClip = Mathf.Max(camera.nearClipPlane - depthPadding, 0.001f);
            float farClip = Mathf.Max(rawFarClip + depthPadding, nearClip + 0.001f);
            BuildClustering(position, right, up, forward, verticalFov, aspect, nearClip, farClip, horizontalPadding, verticalPadding, camera);
        }

        // Releases editor preview textures while leaving the shared material available for play-mode preparation.
        internal void ReleaseClusteringPreview() {
            TryInitialize();
            DisableClustering();
#if UNITY_EDITOR
            // Shader globals outlive managed proxy state across play-mode transitions. Reset them even when the restored edit-mode manager reports a stale _clusteringActive == false.
            VRCShader.SetGlobalFloat(_clusteringEnabledID, 0f);
            VRCShader.SetGlobalTexture(_clusterMaskID, null);
            VRCShader.SetGlobalTexture(_coarseClusterMaskID, null);
#endif
            _clusteringUnsupported = false;
            _clusteringAllocationFailed = false;
            _froxelLayoutValid = false;
            _froxelDepthValid = false;
            _froxelProjectionValid = false;
            ReleaseClusteringTextures();
        }

#endif

#if !COMPILER_UDONSHARP
        // Returns whether the active renderer can build and sample the packed integer mask atlas.
        private bool ClusteringSupported() {
            if (_clusteringUnsupported) return false;
            return SystemInfo.graphicsShaderLevel >= 35 && SystemInfo.SupportsRenderTextureFormat(ClusterMaskFormat);
        }
#endif

        // Resolves the camera grid, publishes its world-space transform, and builds both clustering masks.
        private void BuildClustering(Vector3 position, Vector3 right, Vector3 up, Vector3 forward, float verticalFov, float aspect, float nearClip, float farClip, float horizontalPadding, float verticalPadding, Camera renderCamera) {
            verticalFov = Mathf.Clamp(verticalFov, 1f, 179f);
            if (aspect < 0.001f) aspect = DefaultFroxelAspect;
            if (nearClip < 0.001f) nearClip = 0.001f;
            if (farClip < nearClip + 0.001f) farClip = nearClip + 0.001f;

            float density = Mathf.Clamp(FroxelDensity, 0.05f, 3f);
            int depthSlices = Mathf.Clamp(FroxelSlices, 1, MaxFroxelSize);
            int requestedCoarse = FroxelCoarse;
            int coarseFactor = requestedCoarse <= 2 ? 2 : (requestedCoarse <= 5 ? 4 : 8);

            bool layoutChanged = true;
            if (_froxelLayoutValid && _froxelLayoutFov == verticalFov && _froxelLayoutAspect == aspect && _froxelLayoutDensity == density
                && _froxelLayoutSlices == depthSlices && _froxelLayoutCoarse == coarseFactor) layoutChanged = false;
            if (layoutChanged) {
                _froxelLayoutValid = true;
                _froxelLayoutFov = verticalFov;
                _froxelLayoutAspect = aspect;
                _froxelLayoutDensity = density;
                _froxelLayoutSlices = depthSlices;
                _froxelLayoutCoarse = coarseFactor;
                _clusteringAllocationFailed = false;

                float halfVerticalRadians = verticalFov * (0.5f * Mathf.Deg2Rad);
                _froxelTanHalfVertical = Mathf.Tan(halfVerticalRadians);
                _froxelTanHalfHorizontal = _froxelTanHalfVertical * aspect;
                float horizontalFov = Mathf.Atan(_froxelTanHalfHorizontal) * (2f * Mathf.Rad2Deg);
                int columns = Mathf.Clamp(Mathf.CeilToInt(horizontalFov * density), 1, MaxFroxelSize);
                int rows = Mathf.Clamp(Mathf.CeilToInt(verticalFov * density), 1, MaxFroxelSize);

                // Use the first layout that fits: power-of-two rounding can only add padding as the shift grows.
                int atlasTileShift = ResolveFroxelAtlasTileShift(rows, depthSlices);
                int atlasTileColumns = 1 << atlasTileShift;
                int atlasTileRows = (rows + atlasTileColumns - 1) >> atlasTileShift;
                _fineAtlasWidth = columns * atlasTileColumns;
                _fineAtlasHeight = depthSlices * atlasTileRows;

                int coarseShift = coarseFactor == 2 ? 1 : (coarseFactor == 4 ? 2 : 3);
                int coarseColumns = (columns + coarseFactor - 1) >> coarseShift;
                int coarseRows = (rows + coarseFactor - 1) >> coarseShift;
                int coarseDepthSlices = (depthSlices + coarseFactor - 1) >> coarseShift;
                int coarseAtlasTileShift = ResolveFroxelAtlasTileShift(coarseRows, coarseDepthSlices);
                int coarseAtlasTileColumns = 1 << coarseAtlasTileShift;
                int coarseAtlasTileRows = (coarseRows + coarseAtlasTileColumns - 1) >> coarseAtlasTileShift;
                _coarseAtlasWidth = coarseColumns * coarseAtlasTileColumns;
                _coarseAtlasHeight = coarseDepthSlices * coarseAtlasTileRows;

                _fineGridParams = new Vector4(columns, depthSlices, rows, atlasTileShift);
                _coarseGridParams = new Vector4(coarseColumns, coarseDepthSlices, coarseRows, coarseAtlasTileShift);
                _coarseReductionParams = new Vector4(coarseFactor, coarseShift, 1f / columns, 1f / rows);
                _froxelDepthValid = false;
                _froxelProjectionValid = false;
                _clusterMaskDirty = true;
                _clusterMaskValid = false;
            }

            if (_clusteringAllocationFailed) {
                DisableClustering();
                return;
            }

            Material clusteringMaterial = GetClusteringMaterial();
            bool materialMissing = clusteringMaterial == null;
            bool resourcesMissing = materialMissing || _clusterMask == null || _coarseClusterMask == null || _clusteringSource == null;
#if !COMPILER_UDONSHARP
            resourcesMissing |= (_clusterMask != null && !_clusterMask.IsCreated()) || (_coarseClusterMask != null && !_coarseClusterMask.IsCreated()) || (_clusteringSource != null && !_clusteringSource.IsCreated());
#endif
            if (layoutChanged || resourcesMissing) {
                if (!EnsureClusteringResources(_fineAtlasWidth, _fineAtlasHeight, _coarseAtlasWidth, _coarseAtlasHeight)) {
                    DisableClustering();
                    return;
                }
                clusteringMaterial = GetClusteringMaterial();

                VRCShader.SetGlobalVector(_froxelGridID, _fineGridParams);
                VRCShader.SetGlobalTexture(_clusterMaskID, _clusterMask);
                VRCShader.SetGlobalTexture(_coarseClusterMaskID, _coarseClusterMask);
                VRCShader.SetGlobalVector(_froxelCoarseGridID, _coarseGridParams);
                VRCShader.SetGlobalVector(_froxelCoarseID, _coarseReductionParams);
                clusteringMaterial.SetVector(_froxelFineGridID, _fineGridParams);
                clusteringMaterial.SetVector(_froxelCoarseGridID, _coarseGridParams);
                clusteringMaterial.SetVector(_froxelCoarseID, _coarseReductionParams);
                clusteringMaterial.SetTexture(_coarseClusterMaskID, _coarseClusterMask);
                if (materialMissing) _froxelDepthValid = false;
                _clusterMaskDirty = true;
            }

            bool depthChanged = true;
            if (_froxelDepthValid && _froxelNearClip == nearClip && _froxelFarClip == farClip) depthChanged = false;
            if (depthChanged) {
                _froxelDepthValid = true;
                _froxelNearClip = nearClip;
                _froxelFarClip = farClip;
                float logDepthRange = Mathf.Log(farClip / nearClip) * 1.4426950409f;
                if (logDepthRange < 0.000001f) logDepthRange = 0.000001f;
                float logDepthStep = logDepthRange / depthSlices;
#if !COMPILER_UDONSHARP
                _editorFroxelDepthParams = new Vector4(nearClip, farClip, 1f / nearClip, depthSlices / logDepthRange);
                VRCShader.SetGlobalVector(_froxelDepthID, _editorFroxelDepthParams);
#else
                VRCShader.SetGlobalVector(_froxelDepthID, new Vector4(nearClip, farClip, 1f / nearClip, depthSlices / logDepthRange));
#endif
                float fineDepthRatio = Mathf.Pow(2f, logDepthStep);
                float coarseDepthRatio = Mathf.Pow(2f, logDepthStep * coarseFactor);
                clusteringMaterial.SetVector(_froxelDepthStepID, new Vector4(logDepthStep, fineDepthRatio, coarseDepthRatio, 0f));
                _clusterMaskDirty = true;
            }

            bool projectionChanged = true;
            if (_froxelProjectionValid && _froxelHorizontalPadding == horizontalPadding && _froxelVerticalPadding == verticalPadding) projectionChanged = false;
            if (projectionChanged) {
                _froxelProjectionValid = true;
                _froxelHorizontalPadding = horizontalPadding;
                _froxelVerticalPadding = verticalPadding;
                VRCShader.SetGlobalVector(_froxelProjectionID, new Vector4(_froxelTanHalfHorizontal, _froxelTanHalfVertical, horizontalPadding, verticalPadding));
                _clusterMaskDirty = true;
            }

            bool cameraChanged = true;
            if (_clusterMaskValid && _froxelCameraPosition.Equals(position) && _froxelCameraRight.Equals(right) && _froxelCameraUp.Equals(up) && _froxelCameraForward.Equals(forward)) cameraChanged = false;
            if (cameraChanged) {
                _froxelCameraPosition = position;
                _froxelCameraRight = right;
                _froxelCameraUp = up;
                _froxelCameraForward = forward;
                VRCShader.SetGlobalVector(_froxelRightID, new Vector4(right.x, right.y, right.z, position.x));
                VRCShader.SetGlobalVector(_froxelUpID, new Vector4(up.x, up.y, up.z, position.y));
                VRCShader.SetGlobalVector(_froxelForwardID, new Vector4(forward.x, forward.y, forward.z, position.z));
            }

#if !COMPILER_UDONSHARP
            bool publishForEditorCamera = !Application.isPlaying;
            if (publishForEditorCamera) {
                // Shader globals are process-wide and may be reset or overwritten without invalidating this manager's caches.
                VRCShader.SetGlobalVector(_froxelGridID, _fineGridParams);
                VRCShader.SetGlobalVector(_froxelDepthID, _editorFroxelDepthParams);
                VRCShader.SetGlobalVector(_froxelCoarseGridID, _coarseGridParams);
                VRCShader.SetGlobalVector(_froxelCoarseID, _coarseReductionParams);
                VRCShader.SetGlobalVector(_froxelProjectionID, new Vector4(_froxelTanHalfHorizontal, _froxelTanHalfVertical, horizontalPadding, verticalPadding));
                VRCShader.SetGlobalVector(_froxelRightID, new Vector4(right.x, right.y, right.z, position.x));
                VRCShader.SetGlobalVector(_froxelUpID, new Vector4(up.x, up.y, up.z, position.y));
                VRCShader.SetGlobalVector(_froxelForwardID, new Vector4(forward.x, forward.y, forward.z, position.z));
                VRCShader.SetGlobalTexture(_clusterMaskID, _clusterMask);
                VRCShader.SetGlobalTexture(_coarseClusterMaskID, _coarseClusterMask);
                VRCShader.SetGlobalVectorArray(_clusteringLightsID, _clusteringLights);
            }
#endif

            bool maskNeedsBuild = _clusterMaskDirty || cameraChanged;
            if (_clusteringLightsDirty) {
                VRCShader.SetGlobalVectorArray(_clusteringLightsID, _clusteringLights);
                _clusteringLightsDirty = false;
                maskNeedsBuild = true;
            }
            if (maskNeedsBuild) {
                BuildClusterMasks(renderCamera, _fineGridParams, _coarseGridParams);
                _clusterMaskDirty = false;
                _clusterMaskValid = true;
            }

#if COMPILER_UDONSHARP
            bool publishForEditorCamera = false;
#endif
            if (!_clusteringActive || publishForEditorCamera) VRCShader.SetGlobalFloat(_clusteringEnabledID, 1f);
            _clusteringActive = true;
        }

        // Returns the first packing that fits vertically. Inputs are capped at 256, so shift four also guarantees a <= 4096 width.
        private static int ResolveFroxelAtlasTileShift(int rows, int depthSlices) {
            if (depthSlices * rows <= MaxFroxelAtlasSize) return 0;
            if (depthSlices * ((rows + 1) >> 1) <= MaxFroxelAtlasSize) return 1;
            if (depthSlices * ((rows + 3) >> 2) <= MaxFroxelAtlasSize) return 2;
            if (depthSlices * ((rows + 7) >> 3) <= MaxFroxelAtlasSize) return 3;
            return MaxFroxelTileShift;
        }

        // Ensures the hidden build material, both packed integer targets and one-pixel blit source all exist.
        private bool EnsureClusteringResources(int atlasWidth, int atlasHeight, int coarseAtlasWidth, int coarseAtlasHeight) {
            if (_clusteringUnsupported) return false;
            bool ready = EnsureClusteringMaterial() && EnsureClusterMask(atlasWidth, atlasHeight) && EnsureCoarseClusterMask(coarseAtlasWidth, coarseAtlasHeight) && EnsureClusteringSource();
            if (ready) return true;

            // Do not retain the largest allocation after a later resource failed under memory pressure.
            ReleaseClusteringTextures();
            return false;
        }

        // Releases all froxel clustering render textures and invalidates the current mask.
        private void ReleaseClusteringTextures() {
            if (_clusterMask != null) ReleaseRuntimeRenderTexture(_clusterMask);
            if (_coarseClusterMask != null) ReleaseRuntimeRenderTexture(_coarseClusterMask);
            if (_clusteringSource != null) ReleaseRuntimeRenderTexture(_clusteringSource);
            _clusterMask = null;
            _coarseClusterMask = null;
            _clusteringSource = null;
            _clusterMaskDirty = true;
            _clusterMaskValid = false;
        }

        // Creates the build material outside Udon; runtime Udon receives the same dependency from the build preprocessor.
        private bool EnsureClusteringMaterial() {
            if (ClusteringMaterial != null) return true;
#if COMPILER_UDONSHARP
            _clusteringUnsupported = true;
            return false;
#else
            if (_generatedClusteringMaterial != null) return true;
            Shader shader = Shader.Find(ClusteringShaderName);
            if (shader == null || !shader.isSupported) {
                _clusteringUnsupported = true;
                return false;
            }
            _generatedClusteringMaterial = new Material(shader);
            _generatedClusteringMaterial.name = gameObject.name + "_ClusteringRuntime";
            _generatedClusteringMaterial.hideFlags = HideFlags.HideAndDontSave;
            return true;
#endif
        }

        // Runtime Udon receives a serialized build dependency; editor preview owns an unsaved material instead.
        private Material GetClusteringMaterial() {
#if COMPILER_UDONSHARP
            return ClusteringMaterial;
#else
            return ClusteringMaterial != null ? ClusteringMaterial : _generatedClusteringMaterial;
#endif
        }

        // Ensures the Fine mask while retaining its field as the stable shader-global object.
        private bool EnsureClusterMask(int atlasWidth, int atlasHeight) {
            _clusterMask = EnsureClusterMaskTexture(_clusterMask, atlasWidth, atlasHeight, false);
            return _clusterMask != null;
        }

        // Ensures the Coarse mask used only as the Fine builder's conservative input.
        private bool EnsureCoarseClusterMask(int atlasWidth, int atlasHeight) {
            _coarseClusterMask = EnsureClusterMaskTexture(_coarseClusterMask, atlasWidth, atlasHeight, true);
            return _coarseClusterMask != null;
        }

        // Creates or recreates either point-filtered RGBA32I Texture2D atlas. Both masks have the same lifetime and descriptor. Keeping one implementation prevents their formats drifting.
        private RenderTexture EnsureClusterMaskTexture(RenderTexture mask, int atlasWidth, int atlasHeight, bool coarseMask) {
            bool matches = mask != null && mask.width == atlasWidth && mask.height == atlasHeight && mask.dimension == TextureDimension.Tex2D
                && mask.format == ClusterMaskFormat && mask.filterMode == FilterMode.Point && !mask.useMipMap;
#if !COMPILER_UDONSHARP
            if (matches) matches = mask.IsCreated();
#endif
            if (matches) return mask;

            ReleaseRuntimeRenderTexture(mask);
            mask = new RenderTexture(atlasWidth, atlasHeight, 0, ClusterMaskFormat, RenderTextureReadWrite.Linear);
            mask.dimension = TextureDimension.Tex2D;
            mask.useMipMap = false;
            mask.autoGenerateMips = false;
            mask.enableRandomWrite = false;
            mask.wrapMode = TextureWrapMode.Clamp;
            mask.filterMode = FilterMode.Point;
            mask.anisoLevel = 0;
#if !COMPILER_UDONSHARP
            mask.name = coarseMask ? "Coarse Froxel Cluster Mask" : "Fine Froxel Cluster Mask";
            mask.hideFlags = HideFlags.HideAndDontSave;
#endif
            if (mask.Create()) return mask;
            ReleaseRuntimeRenderTexture(mask);
            _clusteringAllocationFailed = true;
            _clusterMaskValid = false;
            return null;
        }

        // Creates the one-pixel Texture2D source required by Graphics/VRCGraphics.Blit.
        private bool EnsureClusteringSource() {
            bool matches = _clusteringSource != null && _clusteringSource.dimension == TextureDimension.Tex2D;
#if !COMPILER_UDONSHARP
            if (matches) matches = _clusteringSource.IsCreated();
#endif
            if (matches) return true;

            ReleaseRuntimeRenderTexture(_clusteringSource);
            _clusteringSource = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            _clusteringSource.dimension = TextureDimension.Tex2D;
            _clusteringSource.useMipMap = false;
            _clusteringSource.autoGenerateMips = false;
            _clusteringSource.wrapMode = TextureWrapMode.Clamp;
            _clusteringSource.filterMode = FilterMode.Point;
#if !COMPILER_UDONSHARP
            _clusteringSource.name = "Froxel Clustering Source";
            _clusteringSource.hideFlags = HideFlags.HideAndDontSave;
#endif
            bool created = _clusteringSource.Create();
            if (created) return true;
            ReleaseRuntimeRenderTexture(_clusteringSource);
            _clusteringSource = null;
            _clusteringAllocationFailed = true;
            _clusterMaskValid = false;
            return false;
        }

        // Builds Coarse first, then filters it into Fine. Both draws are immediate and complete in the current frame.
        private void BuildClusterMasks(Camera renderCamera, Vector4 fineGridParams, Vector4 coarseGridParams) {
            Material clusteringMaterial = GetClusteringMaterial();
#if !COMPILER_UDONSHARP
            Camera previousCamera = Camera.current;
            RenderTexture previousRenderTexture = RenderTexture.active;
            if (renderCamera != null) Camera.SetupCurrent(renderCamera);
#endif
            // Never bind the Coarse destination as its own sampler: read/write feedback is undefined on GLES3.
            clusteringMaterial.SetTexture(_coarseClusterMaskID, _clusterMask);
            clusteringMaterial.SetFloat(_froxelPassID, 0f);
            clusteringMaterial.SetVector(_froxelGridID, coarseGridParams);
            VRCGraphics.Blit(_clusteringSource, _coarseClusterMask, clusteringMaterial);
            clusteringMaterial.SetTexture(_coarseClusterMaskID, _coarseClusterMask);
            clusteringMaterial.SetFloat(_froxelPassID, 1f);
            clusteringMaterial.SetVector(_froxelGridID, fineGridParams);
            VRCGraphics.Blit(_clusteringSource, _clusterMask, clusteringMaterial);
#if !COMPILER_UDONSHARP
            RenderTexture.active = previousRenderTexture;
            Camera.SetupCurrent(previousCamera);
#endif
        }

        // Publishes only the availability flag; all Point Light Volume globals remain untouched.
        private void DisableClustering() {
            if (_clusteringActive) VRCShader.SetGlobalFloat(_clusteringEnabledID, 0f);
            _clusteringActive = false;
        }

#endregion
    }
}
