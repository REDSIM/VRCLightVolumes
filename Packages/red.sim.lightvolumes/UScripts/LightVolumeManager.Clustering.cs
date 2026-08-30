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
                if (_shadowCullPyramid != null && !ShadowCulling) RefreshShadowCullReceiverParameters();
                return;
            }
            if (!_isInitialized) TryInitialize();
            if (!_shadowCullSettingsInitialized || _shadowCullSettingsEnabled != ShadowCulling || _shadowCullAuthoredBleedReduction != ShadowBleedReduction || _shadowCullAuthoredMinVariance != ShadowMinVariance) RefreshShadowCullReceiverParameters();
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
            BuildClustering(position, rotation, right, up, forward, verticalFov, aspect, nearClip, farClip, horizontalPadding, verticalPadding, null);
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
                if (_shadowCullPyramid != null && !ShadowCulling) RefreshShadowCullReceiverParameters();
                return;
            }
            if (!_isInitialized) TryInitialize();
            if (!_shadowCullSettingsInitialized || _shadowCullSettingsEnabled != ShadowCulling || _shadowCullAuthoredBleedReduction != ShadowBleedReduction || _shadowCullAuthoredMinVariance != ShadowMinVariance) RefreshShadowCullReceiverParameters();
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
            BuildClustering(position, rotation, right, up, forward, verticalFov, aspect, nearClip, farClip, horizontalPadding, verticalPadding, camera);
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
        private void BuildClustering(Vector3 position, Quaternion rotation, Vector3 right, Vector3 up, Vector3 forward, float verticalFov, float aspect, float nearClip, float farClip, float horizontalPadding, float verticalPadding, Camera renderCamera) {
            verticalFov = Mathf.Clamp(verticalFov, 1f, 179f);
            if (aspect < 0.001f) aspect = DefaultFroxelAspect;
            if (nearClip < 0.001f) nearClip = 0.001f;
            if (farClip < nearClip + 0.001f) farClip = nearClip + 0.001f;

            float density = Mathf.Clamp(FroxelDensity, 0.05f, 3f);
            int depthSlices = Mathf.Clamp(FroxelSlices, 1, MaxFroxelSize);
            int requestedCoarse = FroxelCoarse;
            int coarseFactor = requestedCoarse <= 2 ? 2 : (requestedCoarse <= 5 ? 4 : 8);
            float currentTanHalfVertical = _froxelSourceTanHalfVertical;
            float currentTanHalfHorizontal = _froxelSourceTanHalfHorizontal;
            float horizontalFov = _froxelLayoutHorizontalFov;

            bool layoutChanged = true;
            if (_froxelLayoutValid && _froxelLayoutFov == verticalFov && _froxelLayoutAspect == aspect && _froxelLayoutDensity == density && _froxelLayoutSlices == depthSlices && _froxelLayoutCoarse == coarseFactor) layoutChanged = false;
            if (layoutChanged) {
                _froxelLayoutValid = true;
                _froxelLayoutFov = verticalFov;
                _froxelLayoutAspect = aspect;
                _froxelLayoutDensity = density;
                _froxelLayoutSlices = depthSlices;
                _froxelLayoutCoarse = coarseFactor;
                _clusteringAllocationFailed = false;
                float halfVerticalRadians = verticalFov * (0.5f * Mathf.Deg2Rad);
                currentTanHalfVertical = Mathf.Tan(halfVerticalRadians);
                currentTanHalfHorizontal = currentTanHalfVertical * aspect;
                horizontalFov = Mathf.Atan(currentTanHalfHorizontal) * (2f * Mathf.Rad2Deg);
                _froxelSourceTanHalfVertical = currentTanHalfVertical;
                _froxelSourceTanHalfHorizontal = currentTanHalfHorizontal;
                _froxelLayoutHorizontalFov = horizontalFov;
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
                _froxelCameraAnchorValid = false;
                _clusterMaskDirty = true;
                _clusterMaskValid = false;
            }

            if (_clusteringAllocationFailed) {
                DisableClustering();
                return;
            }

            Material clusteringMaterial = ClusteringMaterial;
#if !COMPILER_UDONSHARP
            if (clusteringMaterial == null) clusteringMaterial = _generatedClusteringMaterial;
#endif
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
                clusteringMaterial = ClusteringMaterial;
#if !COMPILER_UDONSHARP
                if (clusteringMaterial == null) clusteringMaterial = _generatedClusteringMaterial;
#endif
            }

            // Materials can be replaced after play-mode dependency self-healing without changing any texture or layout. Republish every material-local binding before its first draw.
            bool materialChanged = _boundClusteringMaterial != clusteringMaterial;
            if (layoutChanged || resourcesMissing || materialChanged) {

                VRCShader.SetGlobalVector(_froxelGridID, _fineGridParams);
                VRCShader.SetGlobalTexture(_clusterMaskID, _clusterMask);
                VRCShader.SetGlobalTexture(_coarseClusterMaskID, _coarseClusterMask);
                VRCShader.SetGlobalVector(_froxelCoarseGridID, _coarseGridParams);
                VRCShader.SetGlobalVector(_froxelCoarseID, _coarseReductionParams);
                clusteringMaterial.SetVector(_froxelFineGridID, _fineGridParams);
                clusteringMaterial.SetVector(_froxelCoarseGridID, _coarseGridParams);
                clusteringMaterial.SetVector(_froxelCoarseID, _coarseReductionParams);
                clusteringMaterial.SetVector(_froxelGridInverseID, new Vector4(1f / _fineGridParams.x, 1f / _fineGridParams.y, 1f / _coarseGridParams.x, 1f / _coarseGridParams.y));
                clusteringMaterial.SetTexture(_coarseClusterMaskID, _coarseClusterMask);
                if (materialChanged) {
                    _boundClusteringMaterial = clusteringMaterial;
                    _shadowCullMaterialBindingDirty = true;
                    _froxelDepthValid = false;
                }
                _clusterMaskDirty = true;
            }

            if (ShadowCulling && (HasAutoShadowTextureUpdates || _shadowCullPyramidSuspendedForAutoUpdates)) RefreshShadowCullAutoUpdateState();
            bool cameraGuardEnabled = ShadowCulling && _activeShadowCullCount > 0 && !_shadowCullPyramidSuspendedForAutoUpdates && !_shadowCullPyramidUnsupported && !_shadowCullPyramidAllocationFailed && verticalFov <= 150f && horizontalFov <= 150f;
            bool anchorSourceChanged = !_froxelCameraAnchorValid
                || _froxelCameraAnchorGuarded != cameraGuardEnabled
                || _froxelAnchorSourceNearClip != nearClip || _froxelAnchorSourceFarClip != farClip
                || Mathf.Abs(_froxelAnchorSourceHorizontalPadding - horizontalPadding) > 0.0001f
                || Mathf.Abs(_froxelAnchorSourceVerticalPadding - verticalPadding) > 0.0001f;
            bool cameraOutsideAnchor = anchorSourceChanged;
            if (!cameraOutsideAnchor) {
                if (cameraGuardEnabled) {
                    Vector3 anchorOffset = position - _froxelCameraPosition;
                    float rotationDot = Mathf.Abs(rotation.x * _froxelCameraRotation.x + rotation.y * _froxelCameraRotation.y + rotation.z * _froxelCameraRotation.z + rotation.w * _froxelCameraRotation.w);
                    cameraOutsideAnchor = anchorOffset.sqrMagnitude > FroxelCameraGuardRadius * FroxelCameraGuardRadius || rotationDot < FroxelCameraGuardRotationDot;
                } else {
                    cameraOutsideAnchor = !_froxelCameraPosition.Equals(position) || !_froxelCameraRight.Equals(right) || !_froxelCameraUp.Equals(up) || !_froxelCameraForward.Equals(forward);
                }
            }

            bool anchorParametersChanged = anchorSourceChanged || !_froxelDepthValid || !_froxelProjectionValid;
            bool anchorChanged = cameraOutsideAnchor || anchorParametersChanged;
            if (anchorChanged) {
                _froxelCameraAnchorValid = true;
                _froxelCameraAnchorGuarded = cameraGuardEnabled;
                _froxelCameraPosition = position;
                _froxelCameraRotation = rotation;
                _froxelCameraRight = right;
                _froxelCameraUp = up;
                _froxelCameraForward = forward;

                if (anchorParametersChanged) {
                    _froxelAnchorSourceNearClip = nearClip;
                    _froxelAnchorSourceFarClip = farClip;
                    _froxelAnchorSourceHorizontalPadding = horizontalPadding;
                    _froxelAnchorSourceVerticalPadding = verticalPadding;
                    _froxelTanHalfHorizontal = currentTanHalfHorizontal;
                    _froxelTanHalfVertical = currentTanHalfVertical;
                    _froxelHorizontalPadding = horizontalPadding;
                    _froxelVerticalPadding = verticalPadding;
                    _froxelNearClip = nearClip;
                    _froxelFarClip = farClip;
                    if (cameraGuardEnabled) {
                        float guardRadians = FroxelCameraGuardAngleDegrees * Mathf.Deg2Rad;
                        float guardSine = Mathf.Sin(guardRadians);
                        float guardCosine = Mathf.Cos(guardRadians);
                        float viewCornerLength = Mathf.Sqrt(1f + currentTanHalfHorizontal * currentTanHalfHorizontal + currentTanHalfVertical * currentTanHalfVertical);
                        float horizontalExtra = Mathf.Asin(Mathf.Clamp(viewCornerLength * guardSine / Mathf.Sqrt(1f + currentTanHalfHorizontal * currentTanHalfHorizontal), 0f, 0.9999f));
                        float verticalExtra = Mathf.Asin(Mathf.Clamp(viewCornerLength * guardSine / Mathf.Sqrt(1f + currentTanHalfVertical * currentTanHalfVertical), 0f, 0.9999f));
                        _froxelTanHalfHorizontal = Mathf.Tan(Mathf.Atan(currentTanHalfHorizontal) + horizontalExtra);
                        _froxelTanHalfVertical = Mathf.Tan(Mathf.Atan(currentTanHalfVertical) + verticalExtra);

                        // A translation sphere also encloses stereo XY padding. Plane support gives the exact multiplier for x - tan(FOV) * z and its vertical counterpart.
                        float stereoRadius = Mathf.Sqrt(horizontalPadding * horizontalPadding + verticalPadding * verticalPadding);
                        float sweepRadius = FroxelCameraGuardRadius + stereoRadius + FroxelCameraGuardCoordinateEpsilon;
                        _froxelHorizontalPadding = sweepRadius * Mathf.Sqrt(1f + _froxelTanHalfHorizontal * _froxelTanHalfHorizontal);
                        _froxelVerticalPadding = sweepRadius * Mathf.Sqrt(1f + _froxelTanHalfVertical * _froxelTanHalfVertical);
                        float diagonalTangent = Mathf.Sqrt(currentTanHalfHorizontal * currentTanHalfHorizontal + currentTanHalfVertical * currentTanHalfVertical);
                        _froxelNearClip = Mathf.Max(0.001f, nearClip * Mathf.Max(guardCosine - diagonalTangent * guardSine, 0f) - sweepRadius);
                        _froxelFarClip = Mathf.Max(farClip * (guardCosine + diagonalTangent * guardSine) + sweepRadius, _froxelNearClip + 0.001f);
                    }

                    _froxelDepthValid = true;
                    float logDepthRange = Mathf.Log(_froxelFarClip / _froxelNearClip) * 1.4426950409f;
                    if (logDepthRange < 0.000001f) logDepthRange = 0.000001f;
                    float logDepthStep = logDepthRange / depthSlices;
#if !COMPILER_UDONSHARP
                    _editorFroxelDepthParams = new Vector4(_froxelNearClip, _froxelFarClip, 1f / _froxelNearClip, depthSlices / logDepthRange);
                    VRCShader.SetGlobalVector(_froxelDepthID, _editorFroxelDepthParams);
#else
                    VRCShader.SetGlobalVector(_froxelDepthID, new Vector4(_froxelNearClip, _froxelFarClip, 1f / _froxelNearClip, depthSlices / logDepthRange));
#endif
                    float fineDepthRatio = Mathf.Pow(2f, logDepthStep);
                    float coarseDepthRatio = Mathf.Pow(2f, logDepthStep * coarseFactor);
                    clusteringMaterial.SetVector(_froxelDepthStepID, new Vector4(logDepthStep, fineDepthRatio, coarseDepthRatio, 0f));

                    _froxelProjectionValid = true;
                    VRCShader.SetGlobalVector(_froxelProjectionID, new Vector4(_froxelTanHalfHorizontal, _froxelTanHalfVertical, _froxelHorizontalPadding, _froxelVerticalPadding));
                }
                VRCShader.SetGlobalVector(_froxelRightID, new Vector4(_froxelCameraRight.x, _froxelCameraRight.y, _froxelCameraRight.z, _froxelCameraPosition.x));
                VRCShader.SetGlobalVector(_froxelUpID, new Vector4(_froxelCameraUp.x, _froxelCameraUp.y, _froxelCameraUp.z, _froxelCameraPosition.y));
                VRCShader.SetGlobalVector(_froxelForwardID, new Vector4(_froxelCameraForward.x, _froxelCameraForward.y, _froxelCameraForward.z, _froxelCameraPosition.z));
                _clusterMaskDirty = true;
            }

#if !COMPILER_UDONSHARP
            bool publishForEditorCamera = !Application.isPlaying;
            if (publishForEditorCamera) {
                // Shader globals are process-wide and may be reset or overwritten without invalidating this manager's caches.
                VRCShader.SetGlobalVector(_froxelGridID, _fineGridParams);
                VRCShader.SetGlobalVector(_froxelDepthID, _editorFroxelDepthParams);
                VRCShader.SetGlobalVector(_froxelCoarseGridID, _coarseGridParams);
                VRCShader.SetGlobalVector(_froxelCoarseID, _coarseReductionParams);
                VRCShader.SetGlobalVector(_froxelProjectionID, new Vector4(_froxelTanHalfHorizontal, _froxelTanHalfVertical, _froxelHorizontalPadding, _froxelVerticalPadding));
                VRCShader.SetGlobalVector(_froxelRightID, new Vector4(_froxelCameraRight.x, _froxelCameraRight.y, _froxelCameraRight.z, _froxelCameraPosition.x));
                VRCShader.SetGlobalVector(_froxelUpID, new Vector4(_froxelCameraUp.x, _froxelCameraUp.y, _froxelCameraUp.z, _froxelCameraPosition.y));
                VRCShader.SetGlobalVector(_froxelForwardID, new Vector4(_froxelCameraForward.x, _froxelCameraForward.y, _froxelCameraForward.z, _froxelCameraPosition.z));
                VRCShader.SetGlobalTexture(_clusterMaskID, _clusterMask);
                VRCShader.SetGlobalTexture(_coarseClusterMaskID, _coarseClusterMask);
                VRCShader.SetGlobalVectorArray(_clusteringLightsID, _clusteringLights);
            }
#endif

            bool maskNeedsBuild = _clusterMaskDirty;
            if (!maskNeedsBuild && _shadowCullPyramidDirty && CanRetryShadowCullPyramidBuild()) maskNeedsBuild = true;
            if (_clusteringLightsDirty) {
                VRCShader.SetGlobalVectorArray(_clusteringLightsID, _clusteringLights);
                _clusteringLightsDirty = false;
                maskNeedsBuild = true;
            }
            if (maskNeedsBuild) {
                BuildClusterMasks(renderCamera, clusteringMaterial);
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

        // Detects direct public-field writes with a cheap raw-value fast path. The hierarchy uses one fixed 1/256 probability floor at zero receiver bleed, so there is no second authoring threshold whose effect is normally hidden by Shadow Bleed Reduction.
        private void RefreshShadowCullReceiverParameters() {
            bool settingsEnabled = ShadowCulling;
            bool settingsInitialized = _shadowCullSettingsInitialized;
            if (settingsInitialized && _shadowCullSettingsEnabled == settingsEnabled && _shadowCullAuthoredBleedReduction == ShadowBleedReduction && _shadowCullAuthoredMinVariance == ShadowMinVariance) return;

            bool previousEnabled = _shadowCullSettingsEnabled;
            bool enabledChanged = !settingsInitialized || previousEnabled != settingsEnabled;
            _shadowCullSettingsInitialized = true;
            _shadowCullSettingsEnabled = settingsEnabled;
            _shadowCullAuthoredBleedReduction = ShadowBleedReduction;
            _shadowCullAuthoredMinVariance = ShadowMinVariance;

            float bleedReduction = Mathf.Min(Mathf.Clamp01(ShadowBleedReduction), 0.999f);
            float varianceBias = Mathf.Max(ShadowMinVariance, 0f) * 0.01f;
            float positiveVarianceScale = varianceBias * 5.54f;
            float negativeVarianceScale = varianceBias * 5f;
            bool receiverChanged = _shadowCullBleedReduction != bleedReduction || _shadowCullPositiveVarianceScale != positiveVarianceScale || _shadowCullNegativeVarianceScale != negativeVarianceScale;
            _shadowCullBleedReduction = bleedReduction;
            _shadowCullPositiveVarianceScale = positiveVarianceScale;
            _shadowCullNegativeVarianceScale = negativeVarianceScale;

            // Keep the main EVSM receiver and the culling proof atomic even when runtime code writes the public authoring fields directly instead of calling UpdateVolumes.
            if (receiverChanged) VRCShader.SetGlobalVector(_pointLightShadowReceiverParamsID, GetPointLightShadowReceiverParams());

            if (!settingsEnabled) {
                if (enabledChanged || previousEnabled || _shadowCullPyramid != null) {
                    _shadowCullPyramidSuspendedForAutoUpdates = false;
                    _shadowCullPyramidUnsupported = false;
                    _shadowCullPyramidAllocationFailed = false;
                    ReleaseShadowCullPyramidTextures();
                    _shadowCullPyramidDirty = false;
                    _clusterMaskDirty = true;
                    _shadowCullMaterialBindingDirty = true;
                }
                return;
            }

            if (enabledChanged) {
                _shadowCullPyramidUnsupported = false;
                _shadowCullPyramidAllocationFailed = false;
                _shadowCullPyramidSuspendedForAutoUpdates = false;
            }
            if (enabledChanged || receiverChanged) InvalidateShadowCullPyramid();
        }

        // Invalidates both the light-space hierarchy and every camera mask that may have consumed it.
        private void InvalidateShadowCullPyramid() {
            if (!ShadowCulling) return;
            _shadowCullPyramidDirty = true;
            _shadowCullPyramidValid = false;
            _clusterMaskDirty = true;
            _shadowCullMaterialBindingDirty = true;
        }

        // Per-frame shadow sources would otherwise rebuild every slice of the expensive statistical hierarchy every frame. Drop shadow-assisted culling once, rebuild the mask geometrically, and keep static clustering costs bounded until automatic texture updates are disabled.
        private void SuspendShadowCullPyramidForAutoUpdates() {
            if (_shadowCullPyramidValid) _clusterMaskDirty = true;
            _shadowCullPyramidValid = false;
            _shadowCullPyramidDirty = true;
            _shadowCullMaterialBindingDirty = true;
        }

        // Detects both Udon callbacks and ordinary C# writes to AutoUpdateTextures. The transition back to static sources explicitly dirties the mask so a motionless camera still re-arms Hi-Z.
        private void RefreshShadowCullAutoUpdateState() {
            if (!ShadowCulling) return;
            bool shouldSuspend = AutoUpdateTextures && HasAutoShadowTextureUpdates;
            if (_shadowCullPyramidSuspendedForAutoUpdates == shouldSuspend) return;
            _shadowCullPyramidSuspendedForAutoUpdates = shouldSuspend;
            if (shouldSuspend) SuspendShadowCullPyramidForAutoUpdates();
            else InvalidateShadowCullPyramid();
        }

        private static bool IsShadowCullPowerOfTwo(int value) {
            return value > 0 && (value & (value - 1)) == 0;
        }

        // Returns the source mip index of the finest retained hierarchy level. Level one is the exact 2x2 reduction at half shadow-map resolution. The hierarchy automatically keeps as much source detail as possible up to the fixed 128x128-per-face release ceiling.
        private static int ResolveShadowCullFirstStoredLevel(int resolution) {
            int resolutionShift = IntegerLog2PowerOfTwo(resolution);
            int finestResolution = Mathf.Min(resolution >> 1, MaxFroxelShadowCullResolution);
            if (finestResolution < 1) finestResolution = 1;
            return Mathf.Max(resolutionShift - IntegerLog2PowerOfTwo(finestResolution), 1);
        }

        // Chooses a power-of-two tile column count so shader lookup uses shifts while the packed 2D atlas stays Quest-portable.
        private static int ResolveShadowCullTileColumns(int sliceCount, int tileSize) {
            int bestColumns = 0;
            int bestLargestDimension = 2147483647;
            int columns = 1;
            while (columns <= 1024) {
                int rows = (sliceCount + columns - 1) / columns;
                int atlasWidth = columns * tileSize;
                int atlasHeight = rows * tileSize;
                if (atlasWidth <= MaxShadowCullAtlasSize && atlasHeight <= MaxShadowCullAtlasSize) {
                    int largestDimension = Mathf.Max(atlasWidth, atlasHeight);
                    if (largestDimension < bestLargestDimension) {
                        bestLargestDimension = largestDimension;
                        bestColumns = columns;
                    }
                }
                if (columns >= sliceCount) break;
                columns <<= 1;
            }
            return bestColumns;
        }

        private static int IntegerLog2PowerOfTwo(int value) {
            int result = 0;
            while (value > 1) {
                value >>= 1;
                result++;
            }
            return result;
        }

        // Resolves a power-of-two row pitch for the finished linear mip-tail. Levels before firstStoredLevel are build-only and consume no persistent texels.
        // The normal path stops at 2x2: a full-face query takes the max of those four nodes, so a 1x1 root is redundant. There is no per-level or per-slice padding beyond the final physical atlas row.
        private static bool ResolveShadowCullPackedAtlas(int resolution, int sliceCount, int firstStoredLevel, out int atlasWidth, out int atlasHeight, out int nodeCount) {
            atlasWidth = 0;
            atlasHeight = 0;
            nodeCount = 0;
            if (resolution < 2 || resolution > (1 << MaxShadowCullMipCount) || !IsShadowCullPowerOfTwo(resolution) || sliceCount <= 0) return false;

            int resolutionShift = IntegerLog2PowerOfTwo(resolution);
            int lastStoredLevel = resolution > 2 ? resolutionShift - 1 : 1;
            if (firstStoredLevel < 1 || firstStoredLevel > lastStoredLevel) return false;
            long resolutionSquared = (long)resolution * resolution;
            long beforeFirstLevelNodeCount = resolutionSquared >> ((firstStoredLevel - 1) * 2);
            long finalLevelNodeCount = resolutionSquared >> (lastStoredLevel * 2);
            long nodesPerSlice = (beforeFirstLevelNodeCount - finalLevelNodeCount) / 3L;
            long totalNodes = nodesPerSlice * sliceCount;
            long maximumNodes = (long)MaxShadowCullAtlasSize * MaxShadowCullAtlasSize;
            if (totalNodes <= 0L || totalNodes > maximumNodes) return false;

            int width = 1;
            while ((long)width * width < totalNodes && width < MaxShadowCullAtlasSize) width <<= 1;
            int height = (int)((totalNodes + width - 1L) / width);
            if (width > MaxShadowCullAtlasSize || height > MaxShadowCullAtlasSize) return false;

            atlasWidth = width;
            atlasHeight = height;
            nodeCount = (int)totalNodes;
            return true;
        }

        // Allocates the exact intermediate level sizes used by the conservative 2x2 max chain. These are build scratch only; the persistent clustering representation is one texture.
        private bool EnsureShadowCullBuildResources(int resolution, int sliceCount, int firstBuildLevel, int levelCount, int tileColumns) {
            if (_shadowCullBuildLevels == null || _shadowCullBuildLevels.Length != MaxShadowCullBuildLevelCount) _shadowCullBuildLevels = new RenderTexture[MaxShadowCullBuildLevelCount];

            int tileRows = (sliceCount + tileColumns - 1) / tileColumns;
            for (int levelIndex = 0; levelIndex < levelCount; levelIndex++) {
                int sourceLevel = firstBuildLevel + levelIndex;
                int tileSize = resolution >> sourceLevel;
                int atlasWidth = tileColumns * tileSize;
                int atlasHeight = tileRows * tileSize;
                RenderTexture texture = _shadowCullBuildLevels[levelIndex];
                bool matches = texture != null && texture.width == atlasWidth && texture.height == atlasHeight && texture.dimension == TextureDimension.Tex2D && texture.format == ShadowCullPyramidFormat && texture.filterMode == FilterMode.Point && !texture.useMipMap;
#if !COMPILER_UDONSHARP
                if (matches) matches = texture.IsCreated();
#endif
                if (matches) continue;

                ReleaseRuntimeRenderTexture(texture);
                texture = new RenderTexture(atlasWidth, atlasHeight, 0, ShadowCullPyramidFormat, RenderTextureReadWrite.Linear);
                texture.dimension = TextureDimension.Tex2D;
                texture.useMipMap = false;
                texture.autoGenerateMips = false;
                texture.enableRandomWrite = false;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Point;
                texture.anisoLevel = 0;
#if !COMPILER_UDONSHARP
                texture.name = "Froxel Shadow Cull Build Level " + sourceLevel;
                texture.hideFlags = HideFlags.HideAndDontSave;
#endif
                if (!texture.Create()) {
                    ReleaseRuntimeRenderTexture(texture);
                    _shadowCullBuildLevels[levelIndex] = null;
                    return false;
                }
                _shadowCullBuildLevels[levelIndex] = texture;
            }

            for (int levelIndex = levelCount; levelIndex < MaxShadowCullBuildLevelCount; levelIndex++) {
                RenderTexture texture = _shadowCullBuildLevels[levelIndex];
                if (texture == null) continue;
                ReleaseRuntimeRenderTexture(texture);
                _shadowCullBuildLevels[levelIndex] = null;
            }
            return true;
        }

        private bool EnsureShadowCullPackedPyramid(int atlasWidth, int atlasHeight) {
            RenderTexture texture = _shadowCullPyramid;
            bool matches = texture != null && texture.width == atlasWidth && texture.height == atlasHeight && texture.dimension == TextureDimension.Tex2D && texture.format == ShadowCullPyramidFormat && texture.filterMode == FilterMode.Point && !texture.useMipMap;
#if !COMPILER_UDONSHARP
            if (matches) matches = texture.IsCreated();
#endif
            if (matches) return true;

            ReleaseRuntimeRenderTexture(texture);
            texture = new RenderTexture(atlasWidth, atlasHeight, 0, ShadowCullPyramidFormat, RenderTextureReadWrite.Linear);
            texture.dimension = TextureDimension.Tex2D;
            texture.useMipMap = false;
            texture.autoGenerateMips = false;
            texture.enableRandomWrite = false;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            texture.anisoLevel = 0;
#if !COMPILER_UDONSHARP
            texture.name = "Froxel Shadow Cull Hierarchy";
            texture.hideFlags = HideFlags.HideAndDontSave;
#endif
            if (!texture.Create()) {
                ReleaseRuntimeRenderTexture(texture);
                _shadowCullPyramid = null;
                return false;
            }
            _shadowCullPyramid = texture;
            return true;
        }

#if !COMPILER_UDONSHARP
        // Detects native texture loss before reusing a cached hierarchy. Unity can retain the managed RenderTexture object after its underlying GPU allocation has been released.
        private bool ShadowCullPackedPyramidIsValid() {
            RenderTexture texture = _shadowCullPyramid;
            if (texture == null || texture.dimension != TextureDimension.Tex2D || texture.format != ShadowCullPyramidFormat || texture.filterMode != FilterMode.Point || texture.useMipMap) return false;
            if (!texture.IsCreated()) return false;
            return true;
        }
#endif

        // Builds exact 2x2-max levels across all shadow slices, packs every slice's complete mip-tail into one dense R32F texture, then releases the temporary per-level render textures.
#if !COMPILER_UDONSHARP
        private bool BuildShadowCullPyramid() {
            RefreshShadowCullAutoUpdateState();
            return BuildShadowCullPyramidCore();
        }
#endif

        // A runtime build dependency can be published after a geometry-only mask has already consumed the camera dirty flag. Retry exactly once when that dependency becomes usable.
        // Keeping the material gate here avoids two mask blits per frame while it is still absent.
        private bool CanRetryShadowCullPyramidBuild() {
            if (!ShadowCulling || !_shadowCullPyramidDirty || _shadowCullPyramidSuspendedForAutoUpdates || _shadowCullPyramidUnsupported || _shadowCullPyramidAllocationFailed || ShadowTextures == null || ShadowMapsCount <= 0 || _activeShadowCullCount <= 0) return false;
            return GetShadowCullingMaterial() != null;
        }

        // Production clustering has already refreshed the auto-update transition before deciding whether the camera mask is dirty. Keeping this core separate avoids doing that Udon work twice.
        private bool BuildShadowCullPyramidCore() {
            if (!ShadowCulling) return false;
            if (_shadowCullPyramidSuspendedForAutoUpdates) return false;
            if (!_shadowCullPyramidDirty && _shadowCullPyramidValid) {
#if COMPILER_UDONSHARP
                // Every Udon-owned write into the shadow atlas explicitly invalidates this cache. Avoid recomputing the packed layout and descriptor on every moving-camera mask.
                return true;
#else
                bool descriptorMatches = ShadowTextures != null && ShadowTextures == _shadowCullPyramidSource && ShadowTextures.dimension == TextureDimension.Tex2DArray
                    && ShadowTextures.width == _shadowCullPyramidResolution && ShadowTextures.height == _shadowCullPyramidResolution
                    && ShadowTextures.volumeDepth == _shadowCullPyramidSliceCount
                    && ShadowCullPackedPyramidIsValid();
                if (descriptorMatches) return true;
                InvalidateShadowCullPyramid();
#endif
            }
            if (!_shadowCullPyramidDirty) return false;
            _shadowCullPyramidDirty = false;
            _shadowCullPyramidValid = false;
            if (_shadowCullPyramidUnsupported || _shadowCullPyramidAllocationFailed) return false;
            if (ShadowTextures == null || ShadowMapsCount <= 0 || _activeShadowCullCount <= 0) return false;
            float cullProbability = Mathf.Max(_shadowCullBleedReduction, ShadowCullProbabilityFloor);

            int resolution = ShadowTextures.width;
            int sliceCount = ShadowTextures.volumeDepth;
            if (resolution < 2 || resolution != ShadowTextures.height || resolution > (1 << MaxShadowCullMipCount) || !IsShadowCullPowerOfTwo(resolution) || sliceCount <= 0 || ShadowTextures.dimension != TextureDimension.Tex2DArray) return false;

            int fullLevelCount = IntegerLog2PowerOfTwo(resolution);
            // A full-face query is still covered by the four 2x2 nodes, so the redundant 1x1 root is omitted except at the degenerate 2x2 source resolution.
            int lastStoredLevel = resolution > 2 ? fullLevelCount - 1 : 1;
            int firstStoredLevel = ResolveShadowCullFirstStoredLevel(resolution);
            int packedAtlasWidth = 0;
            int packedAtlasHeight = 0;
            int packedNodeCount = 0;
            // Preserve the one-atlas/one-pack-pass architecture by walking to the next coarser exact level only when all active slices would exceed the fixed 4K storage budget.
            while (firstStoredLevel <= lastStoredLevel && !ResolveShadowCullPackedAtlas(resolution, sliceCount, firstStoredLevel, out packedAtlasWidth, out packedAtlasHeight, out packedNodeCount)) firstStoredLevel++;
            if (firstStoredLevel > lastStoredLevel) return false;
            // Fuse leading reductions into the expensive EVSM pass so the largest scratch level respects the retained-resolution cap even when the source shadow map is much larger.
            int firstBuildLevel = Mathf.Min(firstStoredLevel, 4);
            int firstTileSize = resolution >> firstBuildLevel;
            int tileColumns = ResolveShadowCullTileColumns(sliceCount, firstTileSize);
            while (tileColumns <= 0 && firstBuildLevel < firstStoredLevel && firstBuildLevel < 5) {
                firstBuildLevel++;
                firstTileSize >>= 1;
                tileColumns = ResolveShadowCullTileColumns(sliceCount, firstTileSize);
            }
            // The pack pass fuses the last three cheap max reductions from a <=16x16 anchor.
            int anchorLevel = Mathf.Max(firstBuildLevel, lastStoredLevel - 3);
            int buildLevelCount = anchorLevel - firstBuildLevel + 1;
            if (tileColumns <= 0 || firstBuildLevel > 5 || lastStoredLevel > MaxShadowCullMipCount || buildLevelCount > MaxShadowCullBuildLevelCount) return false;
            int tileColumnShift = IntegerLog2PowerOfTwo(tileColumns);
            int packedAtlasWidthShift = IntegerLog2PowerOfTwo(packedAtlasWidth);

            if (!EnsureShadowCullingMaterial()) {
#if !COMPILER_UDONSHARP
                _shadowCullPyramidUnsupported = true;
#else
                // Build dependencies may reach a live Udon heap one frame after enable. Keep this transient prerequisite retryable instead of consuming dirty state.
                _shadowCullPyramidDirty = true;
#endif
                return false;
            }
            if (!EnsureShadowCullPackedPyramid(packedAtlasWidth, packedAtlasHeight) || !EnsureShadowCullBuildResources(resolution, sliceCount, firstBuildLevel, buildLevelCount, tileColumns)) {
                _shadowCullPyramidAllocationFailed = true;
                ReleaseShadowCullPyramidTextures();
                _shadowCullPyramidDirty = false;
                return false;
            }

            Material shadowCullingMaterial = GetShadowCullingMaterial();
            shadowCullingMaterial.SetTexture(_pointLightShadowTextureID, ShadowTextures);
            shadowCullingMaterial.SetVector(_shadowCullReceiverParamsID, new Vector4(cullProbability, 0f, 0f, firstBuildLevel));
            float inverseCullProbability = 1f / cullProbability;
            float chebyshevScale = Mathf.Sqrt((1f - cullProbability) * inverseCullProbability);
            float positiveDenominator = 1f - chebyshevScale * _shadowCullPositiveVarianceScale;
            float positiveDenominatorReciprocal = positiveDenominator > 0f ? 1f / positiveDenominator : 0f;
            float negativeDenominatorReciprocal = 1f / (1f + chebyshevScale * _shadowCullNegativeVarianceScale);
            // Build First consumes these proof constants; the same vector is overwritten with packed-layout metadata immediately before Pack.
            shadowCullingMaterial.SetVector(_shadowCullPackParamsID, new Vector4(inverseCullProbability, chebyshevScale, positiveDenominatorReciprocal, negativeDenominatorReciprocal));
            int tileSize = firstTileSize;
            int tileShift = fullLevelCount - firstBuildLevel;
            for (int levelIndex = 0; levelIndex < buildLevelCount; levelIndex++) {
                if (levelIndex > 0) shadowCullingMaterial.SetTexture(_shadowCullPreviousID, _shadowCullBuildLevels[levelIndex - 1]);
                shadowCullingMaterial.SetVector(_shadowCullBuildParamsID, new Vector4(tileSize, tileShift, tileColumnShift, sliceCount));
                VRCGraphics.Blit(_clusteringSource, _shadowCullBuildLevels[levelIndex], shadowCullingMaterial, levelIndex == 0 ? 0 : 1);
                tileSize >>= 1;
                tileShift--;
            }

            // The pack pass is intentionally separate from reduction. Fragment invocations cannot synchronize a dependency chain, and reading the active destination would be undefined.
            for (int levelIndex = 0; levelIndex < buildLevelCount; levelIndex++) shadowCullingMaterial.SetTexture(_shadowCullMipIDs[levelIndex], _shadowCullBuildLevels[levelIndex]);
            // Pack's fused tail reads the final anchor directly, avoiding a sampler selector in its hot loop.
            shadowCullingMaterial.SetTexture(_shadowCullPreviousID, _shadowCullBuildLevels[buildLevelCount - 1]);
            shadowCullingMaterial.SetVector(_shadowCullBuildParamsID, new Vector4(buildLevelCount, fullLevelCount - firstBuildLevel, tileColumnShift, sliceCount));
            shadowCullingMaterial.SetVector(_shadowCullPackParamsID, new Vector4(resolution, firstStoredLevel, packedAtlasWidthShift, packedNodeCount));
            VRCGraphics.Blit(_clusteringSource, _shadowCullPyramid, shadowCullingMaterial, 2);
            // Do not let the persistent material retain references to build-only allocations.
            shadowCullingMaterial.SetTexture(_shadowCullPreviousID, _clusteringSource);
            for (int levelIndex = 0; levelIndex < buildLevelCount; levelIndex++) shadowCullingMaterial.SetTexture(_shadowCullMipIDs[levelIndex], _clusteringSource);
            ReleaseShadowCullBuildTextures();

            _shadowCullPyramidResolution = resolution;
            _shadowCullPyramidSliceCount = sliceCount;
            _shadowCullPyramidFirstLevel = firstStoredLevel;
            _shadowCullPyramidAtlasWidthShift = packedAtlasWidthShift;
            _shadowCullPyramidLevelCount = lastStoredLevel - firstStoredLevel + 1;
            _shadowCullPyramidNodeCount = packedNodeCount;
#if !COMPILER_UDONSHARP
            _shadowCullPyramidSource = ShadowTextures;
#endif
            _shadowCullPyramidValid = true;
            _shadowCullMaterialBindingDirty = true;
#if !COMPILER_UDONSHARP
            _shadowCullPyramidBuildCount++;
            _shadowCullPyramidBlitCount += buildLevelCount + 1;
#endif
            return true;
        }

        private void ReleaseShadowCullBuildTextures() {
            if (_shadowCullBuildLevels != null) {
                int textureCount = _shadowCullBuildLevels.Length;
                for (int i = 0; i < textureCount; i++) {
                    RenderTexture texture = _shadowCullBuildLevels[i];
                    if (texture == null) continue;
                    ReleaseRuntimeRenderTexture(texture);
                    _shadowCullBuildLevels[i] = null;
                }
            }
        }

        private void ReleaseShadowCullPyramidTextures() {
            ReleaseShadowCullBuildTextures();
            if (_shadowCullPyramid != null) ReleaseRuntimeRenderTexture(_shadowCullPyramid);
            _shadowCullPyramid = null;
            _shadowCullPyramidResolution = 0;
            _shadowCullPyramidSliceCount = 0;
            _shadowCullPyramidFirstLevel = 0;
            _shadowCullPyramidAtlasWidthShift = 0;
            _shadowCullPyramidLevelCount = 0;
            _shadowCullPyramidNodeCount = 0;
#if !COMPILER_UDONSHARP
            _shadowCullPyramidSource = null;
#endif
            _shadowCullPyramidValid = false;
            _shadowCullMaterialBindingDirty = true;
        }

        private bool EnsureShadowCullingMaterial() {
            if (ShadowCullingMaterial != null) return true;
#if COMPILER_UDONSHARP
            return false;
#else
            if (_generatedShadowCullingMaterial != null) return true;
            Shader shader = Shader.Find(ShadowCullingShaderName);
            if (shader == null || !shader.isSupported) return false;
            _generatedShadowCullingMaterial = new Material(shader);
            _generatedShadowCullingMaterial.name = gameObject.name + "_ShadowCullingRuntime";
            _generatedShadowCullingMaterial.hideFlags = HideFlags.HideAndDontSave;
            return true;
#endif
        }

        private Material GetShadowCullingMaterial() {
#if COMPILER_UDONSHARP
            return ShadowCullingMaterial;
#else
            return ShadowCullingMaterial != null ? ShadowCullingMaterial : _generatedShadowCullingMaterial;
#endif
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
            ReleaseShadowCullPyramidTextures();
            _clusterMask = null;
            _coarseClusterMask = null;
            _clusteringSource = null;
            _clusterMaskDirty = true;
            _clusterMaskValid = false;
            _froxelCameraAnchorValid = false;
            _shadowCullPyramidDirty = true;
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
            bool matches = mask != null && mask.width == atlasWidth && mask.height == atlasHeight && mask.dimension == TextureDimension.Tex2D && mask.format == ClusterMaskFormat && mask.filterMode == FilterMode.Point && !mask.useMipMap;
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
        private void BuildClusterMasks(Camera renderCamera, Material clusteringMaterial) {
#if !COMPILER_UDONSHARP
            Camera previousCamera = Camera.current;
            RenderTexture previousRenderTexture = RenderTexture.active;
            if (renderCamera != null) Camera.SetupCurrent(renderCamera);
#endif
            bool shadowCullReady = BuildShadowCullPyramidCore();
            PublishShadowCullMaterialState(clusteringMaterial, shadowCullReady);
            VRCGraphics.Blit(_clusteringSource, _coarseClusterMask, clusteringMaterial, 0);
            VRCGraphics.Blit(_clusteringSource, _clusterMask, clusteringMaterial, 1);
#if !COMPILER_UDONSHARP
            _clusterMaskBuildCount++;
#endif
#if !COMPILER_UDONSHARP
            RenderTexture.active = previousRenderTexture;
            Camera.SetupCurrent(previousCamera);
#endif
        }

        // Material bindings change only when the hierarchy generation or clustering material changes, not on every camera-relative mask rebuild.
        private void PublishShadowCullMaterialState(Material clusteringMaterial, bool shadowCullReady) {
            if (_boundClusteringMaterial != clusteringMaterial) {
                _boundClusteringMaterial = clusteringMaterial;
                _shadowCullMaterialBindingDirty = true;
            }
            if (!_shadowCullMaterialBindingDirty) return;

            if (shadowCullReady) {
                clusteringMaterial.SetTexture(_shadowCullHierarchyID, _shadowCullPyramid);
                clusteringMaterial.SetVector(_froxelShadowCullID, new Vector4(IntegerLog2PowerOfTwo(_shadowCullPyramidResolution), _shadowCullPyramidFirstLevel, _shadowCullPyramidSliceCount, _shadowCullPyramidAtlasWidthShift));
            } else {
                clusteringMaterial.SetTexture(_shadowCullHierarchyID, _clusteringSource);
                clusteringMaterial.SetVector(_froxelShadowCullID, Vector4.zero);
            }
            _shadowCullMaterialBindingDirty = false;
        }

        // Publishes only the availability flag; all Point Light Volume globals remain untouched.
        private void DisableClustering() {
            if (_clusteringActive) VRCShader.SetGlobalFloat(_clusteringEnabledID, 0f);
            _clusteringActive = false;
        }

#endregion
    }
}
