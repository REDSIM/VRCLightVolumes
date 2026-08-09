#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCLightVolumes {
    public sealed class LightVolumePreviewRenderer : IDisposable {

        // 16,383 cards use 65,532 vertices, keeping the generated mesh under UInt16 index limits.
        private const int CardsPerInstance = 16383;
        private const int InstancesPerDrawCall = 511;
        private const int PreviewLayer = 0;
        private const float PreviewBoundsSize = 1000000f;
        private const float VoxelRadiusScale = 0.33333334f * 0.7f;
        private const string PreviewShaderName = "Hidden/LightVolumesPreview";

        private static readonly int _previewTexture0ID = Shader.PropertyToID("_PreviewTexture0");
        private static readonly int _previewTexture1ID = Shader.PropertyToID("_PreviewTexture1");
        private static readonly int _previewTexture2ID = Shader.PropertyToID("_PreviewTexture2");
        private static readonly int _previewHasTextureDataID = Shader.PropertyToID("_PreviewHasTextureData");
        private static readonly int _previewLocalToWorldID = Shader.PropertyToID("_PreviewLocalToWorld");
        private static readonly int _previewResolutionID = Shader.PropertyToID("_PreviewResolution");
        private static readonly int _previewVoxelCountID = Shader.PropertyToID("_PreviewVoxelCount");
        private static readonly int _previewVoxelRadiusID = Shader.PropertyToID("_PreviewVoxelRadius");
        private static readonly int _previewCardsPerInstanceID = Shader.PropertyToID("_PreviewCardsPerInstance");
        private static readonly int _previewInstancesPerDrawCallID = Shader.PropertyToID("_PreviewInstancesPerDrawCall");
        private static readonly int _previewDrawCallIdID = Shader.PropertyToID("_PreviewDrawCallId");
        private static readonly int _previewColorID = Shader.PropertyToID("_PreviewColor");
        private static readonly int _previewCorrectionID = Shader.PropertyToID("_PreviewCorrection");
        private static readonly int _previewRotationID = Shader.PropertyToID("_PreviewRotation");
        private static readonly int _previewIsRotatedID = Shader.PropertyToID("_PreviewIsRotated");
        private static readonly int _previewShellOriginID = Shader.PropertyToID("_PreviewShellOrigin");
        private static readonly int _previewCameraVoxelID = Shader.PropertyToID("_PreviewCameraVoxel");
        private static readonly int _previewCameraRightID = Shader.PropertyToID("_PreviewCameraRight");
        private static readonly int _previewCameraUpID = Shader.PropertyToID("_PreviewCameraUp");
        private static readonly int _previewCameraPositionID = Shader.PropertyToID("_PreviewCameraPosition");

        private Mesh _cardMesh;
        private Material _material;
        private MaterialPropertyBlock _propertyBlock;
        private Matrix4x4[] _matrices;
        private bool _reportedMissingShader;
        private bool _reportedMissingInstancing;

        // Compact per-volume payload sent to the shared card renderer.
        private struct PreviewDrawData {
            public Vector3Int Resolution;
            public Texture3D Texture0;
            public Texture3D Texture1;
            public Texture3D Texture2;
            public Color Color;
            public Vector4 Correction;
            public Quaternion VolumeRotation;
            public Quaternion ShRotation;
            public bool IsRotated;
            public Vector3 Position;
            public Vector3 Scale;
        }

        // Draws baked Light Volume data as camera-facing cards.
        public void DrawVolume(LightVolumeInstance volume, Camera camera = null) {
            if (volume == null) return;
            PreviewDrawData data = CreateVolumeDrawData(volume);
            Draw(volume, data, camera);
        }

        // Draws a neutral probe placement grid with the same card renderer.
        public void DrawProbeGrid(LightVolumeInstance volume, Vector3Int resolution, Camera camera = null) {
            if (volume == null) return;
            PreviewDrawData data = CreateProbeGridDrawData(volume, resolution);
            Draw(volume, data, camera);
        }

        // Releases per-renderer editor objects.
        public void Dispose() {
            if (_material != null) {
                UnityEngine.Object.DestroyImmediate(_material);
                _material = null;
            }

            if (_cardMesh != null) {
                UnityEngine.Object.DestroyImmediate(_cardMesh);
                _cardMesh = null;
            }

            _propertyBlock = null;
            _matrices = null;
        }

        // Builds draw data for a baked Light Volume preview.
        private static PreviewDrawData CreateVolumeDrawData(LightVolumeInstance volume) {
            Quaternion volumeRotation = LightVolumeTools.GetRotation(volume);
            Quaternion shRotation = volumeRotation * volume.InvBakedRotation;

            return new PreviewDrawData {
                Resolution = volume.Resolution,
                Texture0 = volume.Texture0,
                Texture1 = volume.Texture1,
                Texture2 = volume.Texture2,
                Color = volume.Color.linear * volume.Intensity,
                Correction = new Vector4(-volume.Shadows * 0.5f, 1f - volume.Highlights * 0.5f, Mathf.Pow(2f, volume.Exposure), 0f),
                VolumeRotation = volumeRotation,
                ShRotation = shRotation,
                IsRotated = Mathf.Abs(Quaternion.Dot(shRotation, Quaternion.identity)) < 0.999999f,
                Position = LightVolumeTools.GetPosition(volume),
                Scale = LightVolumeTools.GetScale(volume)
            };
        }

        // Builds draw data for the light probe placement preview.
        private static PreviewDrawData CreateProbeGridDrawData(LightVolumeInstance volume, Vector3Int resolution) {
            return new PreviewDrawData {
                Resolution = resolution,
                Texture0 = null,
                Texture1 = null,
                Texture2 = null,
                Color = Color.white,
                Correction = new Vector4(0f, 1f, 1f, 0f),
                VolumeRotation = LightVolumeTools.GetRotation(volume),
                ShRotation = Quaternion.identity,
                IsRotated = false,
                Position = LightVolumeTools.GetPosition(volume),
                Scale = LightVolumeTools.GetScale(volume)
            };
        }

        // Draws a card grid using optional Light Volume textures.
        private void Draw(LightVolumeInstance volume, PreviewDrawData data, Camera camera) {
            if (volume == null || volume.gameObject == null) return;

            int voxelCount = GetVoxelCount(data.Resolution);
            if (voxelCount <= 0) return;
            if (!SystemInfo.supportsInstancing) {
                if (!_reportedMissingInstancing) {
                    Debug.LogError("[LightVolumes] GPU instancing is not supported by the current graphics device. Voxel preview cannot be rendered.");
                    _reportedMissingInstancing = true;
                }

                return;
            }

            EnsureResources();
            if (_material == null || _propertyBlock == null || _cardMesh == null) return;

            Camera resolvedCamera = ResolveCamera(camera);
            Matrix4x4 localToWorld = Matrix4x4.TRS(data.Position, data.VolumeRotation, data.Scale);
            float voxelRadius = CalculateVoxelRadius(data.Scale, data.Resolution);
            bool hasTextureData = data.Texture0 != null && data.Texture1 != null && data.Texture2 != null;

            _propertyBlock.Clear();
            if (hasTextureData) {
                _propertyBlock.SetTexture(_previewTexture0ID, data.Texture0);
                _propertyBlock.SetTexture(_previewTexture1ID, data.Texture1);
                _propertyBlock.SetTexture(_previewTexture2ID, data.Texture2);
            }

            _propertyBlock.SetInt(_previewHasTextureDataID, hasTextureData ? 1 : 0);
            _propertyBlock.SetMatrix(_previewLocalToWorldID, localToWorld);
            _propertyBlock.SetVector(_previewResolutionID, new Vector4(data.Resolution.x, data.Resolution.y, data.Resolution.z, 0f));
            _propertyBlock.SetInt(_previewVoxelCountID, voxelCount);
            _propertyBlock.SetFloat(_previewVoxelRadiusID, voxelRadius);
            _propertyBlock.SetInt(_previewCardsPerInstanceID, CardsPerInstance);
            _propertyBlock.SetInt(_previewInstancesPerDrawCallID, InstancesPerDrawCall);
            _propertyBlock.SetVector(_previewColorID, new Vector4(data.Color.r, data.Color.g, data.Color.b, 1f));
            _propertyBlock.SetVector(_previewCorrectionID, data.Correction);
            _propertyBlock.SetVector(_previewRotationID, new Vector4(data.ShRotation.x, data.ShRotation.y, data.ShRotation.z, data.ShRotation.w));
            _propertyBlock.SetInt(_previewIsRotatedID, data.IsRotated ? 1 : 0);
            SetShellData(localToWorld, data.Resolution, resolvedCamera);
            SetCameraData(data.Position, resolvedCamera);

            int instanceCount = (int)(((long)voxelCount + CardsPerInstance - 1L) / CardsPerInstance);
            int fullDrawCallCount = instanceCount / InstancesPerDrawCall;
            int extraDrawCount = instanceCount % InstancesPerDrawCall;
            for (int i = 0; i < fullDrawCallCount; i++) DrawChunk(i, InstancesPerDrawCall, resolvedCamera);

            if (extraDrawCount > 0) DrawChunk(fullDrawCallCount, extraDrawCount, resolvedCamera);
        }

        // Draws one Unity instancing chunk.
        private void DrawChunk(int drawCallId, int instanceCount, Camera camera) {
            if (instanceCount <= 0) return;
            _propertyBlock.SetInt(_previewDrawCallIdID, drawCallId);
            Graphics.DrawMeshInstanced(_cardMesh, 0, _material, _matrices, instanceCount, _propertyBlock, ShadowCastingMode.Off, false, PreviewLayer, camera, LightProbeUsage.Off);
        }

        // Creates reusable mesh/material/property block resources.
        private void EnsureResources() {
            if (_cardMesh == null) _cardMesh = GenerateCardMesh(CardsPerInstance);

            if (_material == null) {
                Shader shader = Shader.Find(PreviewShaderName);
                if (shader != null) {
                    _material = new Material(shader);
                    _material.enableInstancing = true;
                    _material.hideFlags = HideFlags.HideAndDontSave;
                    if (!shader.isSupported && !_reportedMissingShader) {
                        Debug.LogError($"[LightVolumes] Shader '{PreviewShaderName}' is not supported by the current graphics API. Voxel preview cannot be rendered.");
                        _reportedMissingShader = true;
                    }
                } else if (!_reportedMissingShader) {
                    Debug.LogError($"[LightVolumes] Shader '{PreviewShaderName}' was not found. Voxel preview cannot be rendered.");
                    _reportedMissingShader = true;
                }
            }

            if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();

            if (_matrices == null || _matrices.Length != InstancesPerDrawCall) {
                _matrices = new Matrix4x4[InstancesPerDrawCall];
                for (int i = 0; i < _matrices.Length; i++) _matrices[i] = Matrix4x4.identity;
            }
        }

        // Generates a UInt16-indexed card mesh where uv0 stores card id for GLES-safe decoding.
        private static Mesh GenerateCardMesh(int cardCount) {
            int vertexCount = cardCount * 4;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] cardData = new Vector2[vertexCount];
            int[] indices = new int[cardCount * 6];

            for (int i = 0; i < cardCount; i++) {
                int baseVertex = i * 4;
                int baseIndex = i * 6;
                Vector2 cardId = new Vector2(i, 0f);
                cardData[baseVertex] = cardId;
                cardData[baseVertex + 1] = cardId;
                cardData[baseVertex + 2] = cardId;
                cardData[baseVertex + 3] = cardId;

                vertices[baseVertex] = new Vector3(-1f, -1f, 0f);
                vertices[baseVertex + 1] = new Vector3(1f, -1f, 0f);
                vertices[baseVertex + 2] = new Vector3(-1f, 1f, 0f);
                vertices[baseVertex + 3] = new Vector3(1f, 1f, 0f);

                indices[baseIndex] = baseVertex;
                indices[baseIndex + 1] = baseVertex + 1;
                indices[baseIndex + 2] = baseVertex + 3;
                indices[baseIndex + 3] = baseVertex;
                indices[baseIndex + 4] = baseVertex + 2;
                indices[baseIndex + 5] = baseVertex + 3;
            }

            Mesh mesh = new Mesh {
                name = "LightVolumePreview_Cards",
                indexFormat = IndexFormat.UInt16,
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.SetVertices(vertices);
            mesh.uv = cardData;
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * PreviewBoundsSize);
            return mesh;
        }

        // Calculates the voxel-space camera position used by the shader shell traversal.
        private void SetShellData(Matrix4x4 localToWorld, Vector3Int resolution, Camera camera) {
            Vector3 localCamera = Vector3.zero;

            if (camera != null) {
                Matrix4x4 worldToLocal = localToWorld.inverse;
                localCamera = worldToLocal.MultiplyPoint3x4(camera.transform.position);
            }

            Vector3 cameraVoxel = LocalToVoxelCoordinate(localCamera, resolution);
            Vector3 shellOrigin = new Vector3(
                Mathf.Round(Mathf.Clamp(cameraVoxel.x, 0f, Mathf.Max(resolution.x - 1, 0))),
                Mathf.Round(Mathf.Clamp(cameraVoxel.y, 0f, Mathf.Max(resolution.y - 1, 0))),
                Mathf.Round(Mathf.Clamp(cameraVoxel.z, 0f, Mathf.Max(resolution.z - 1, 0)))
            );

            _propertyBlock.SetVector(_previewShellOriginID, new Vector4(shellOrigin.x, shellOrigin.y, shellOrigin.z, 0f));
            _propertyBlock.SetVector(_previewCameraVoxelID, new Vector4(cameraVoxel.x, cameraVoxel.y, cameraVoxel.z, 0f));
        }

        // Sends explicit SceneView camera vectors because editor preview draw timing cannot rely on Unity camera globals.
        private void SetCameraData(Vector3 fallbackPosition, Camera camera) {
            Vector3 cameraRight = Vector3.right;
            Vector3 cameraUp = Vector3.up;
            Vector3 cameraPosition = fallbackPosition - Vector3.forward * 10f;

            if (camera != null) {
                Transform cameraTransform = camera.transform;
                cameraRight = cameraTransform.right;
                cameraUp = cameraTransform.up;
                cameraPosition = cameraTransform.position;
            }

            _propertyBlock.SetVector(_previewCameraRightID, new Vector4(cameraRight.x, cameraRight.y, cameraRight.z, 0f));
            _propertyBlock.SetVector(_previewCameraUpID, new Vector4(cameraUp.x, cameraUp.y, cameraUp.z, 0f));
            _propertyBlock.SetVector(_previewCameraPositionID, new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1f));
        }

        // Converts local volume coordinates from -0.5..0.5 space into continuous voxel coordinates.
        private static Vector3 LocalToVoxelCoordinate(Vector3 localPosition, Vector3Int resolution) {
            return new Vector3(
                (localPosition.x + 0.5f) * Mathf.Max(resolution.x, 1) - 0.5f,
                (localPosition.y + 0.5f) * Mathf.Max(resolution.y, 1) - 0.5f,
                (localPosition.z + 0.5f) * Mathf.Max(resolution.z, 1) - 0.5f
            );
        }

        // Calculates a conservative impostor radius that fits inside one voxel.
        private static float CalculateVoxelRadius(Vector3 scale, Vector3Int resolution) {
            float x = Mathf.Abs(scale.x) / Mathf.Max(resolution.x, 1);
            float y = Mathf.Abs(scale.y) / Mathf.Max(resolution.y, 1);
            float z = Mathf.Abs(scale.z) / Mathf.Max(resolution.z, 1);
            return Mathf.Min(z, Mathf.Min(x, y)) * VoxelRadiusScale;
        }

        // Returns voxel count, or -1 if it cannot fit an int.
        private static int GetVoxelCount(Vector3Int resolution) {
            if (resolution.x <= 0 || resolution.y <= 0 || resolution.z <= 0) return -1;
            ulong count = (ulong)resolution.x * (ulong)resolution.y * (ulong)resolution.z;
            if (count > int.MaxValue) return -1;
            return (int)count;
        }

        // Resolves the camera used for view sorting and optional camera-specific drawing.
        private static Camera ResolveCamera(Camera camera) {
            if (camera != null) return camera;
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null) return sceneView.camera;
            if (Camera.current != null) return Camera.current;
            return Camera.main;
        }

    }

    [InitializeOnLoad]
    internal static class LightVolumePreviewSceneRenderer {

        private const double RepaintInterval = 1.0 / 30.0;

        private static LightVolumePreviewRenderer _renderer;
        private static LightVolumeInstance[] _selectedVolumes = new LightVolumeInstance[8];
        private static int _selectedVolumeCount;
        private static double _nextRepaintTime;
        private static bool _previewModeActive;

        // Returns true when the current Light Volume selection is previewing voxels.
        public static bool IsPreviewModeActive => HasActivePreview();

        // Registers editor render hooks after every domain reload.
        static LightVolumePreviewSceneRenderer() {
            Selection.selectionChanged += RequestRefresh;
            EditorApplication.update += Update;
            Camera.onPreCull += OnCameraPreCull;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
            RequestRefresh();
        }

        // Removes every global render callback and releases preview resources before this editor domain ends.
        private static void Shutdown() {
            Selection.selectionChanged -= RequestRefresh;
            EditorApplication.update -= Update;
            Camera.onPreCull -= OnCameraPreCull;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
            _previewModeActive = false;
            _selectedVolumeCount = 0;
            DisposeRenderer();
        }

        // Refreshes selected preview volumes and repaints SceneView.
        public static void RequestRefresh() {
            RefreshSelection();
            SceneView.RepaintAll();
        }

        // Enables or disables voxel preview for the current Light Volume selection.
        public static void SetPreviewMode(bool enabled) {
            _previewModeActive = enabled;
            RefreshSelection();
            SceneView.RepaintAll();
        }

        // Keeps SceneView repainting while any selected volume preview is enabled.
        private static void Update() {
            if (!HasActivePreview()) {
                DisposeRenderer();
                return;
            }

            double time = EditorApplication.timeSinceStartup;
            if (time < _nextRepaintTime) return;

            _nextRepaintTime = time + RepaintInterval;
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        // Draws enabled previews before the SceneView camera culling step.
        private static void OnCameraPreCull(Camera camera) {
            if (!IsSceneViewCamera(camera)) return;
            if (!HasActivePreview()) return;

            DrawActivePreviews(camera);
        }

        // Draws all active selected previews for one SceneView camera.
        private static void DrawActivePreviews(Camera camera) {
            if (camera == null) return;
            if (_renderer == null) _renderer = new LightVolumePreviewRenderer();

            for (int i = 0; i < _selectedVolumeCount; i++) {
                LightVolumeInstance volume = _selectedVolumes[i];
                if (volume == null) continue;
                _renderer.DrawVolume(volume, camera);
            }
        }

        // Returns true if the camera belongs to any SceneView.
        private static bool IsSceneViewCamera(Camera camera) {
            if (camera == null) return false;
            if (camera.cameraType == CameraType.SceneView) return true;

            if (SceneView.sceneViews == null || SceneView.sceneViews.Count == 0) return false;
            for (int i = 0; i < SceneView.sceneViews.Count; i++) {
                SceneView sceneView = SceneView.sceneViews[i] as SceneView;
                if (sceneView != null && sceneView.camera == camera) return true;
            }

            return false;
        }

        // Caches selected LightVolume components without per-frame selection allocations.
        private static void RefreshSelectedVolumes() {
            _selectedVolumeCount = 0;

            GameObject[] gameObjects = Selection.gameObjects;
            for (int i = 0; i < gameObjects.Length; i++) {
                GameObject go = gameObjects[i];
                if (go == null) continue;
                LightVolumeInstance volume = go.GetComponent<LightVolumeInstance>();
                if (volume == null) continue;

                EnsureSelectedVolumeCapacity(_selectedVolumeCount + 1);
                _selectedVolumes[_selectedVolumeCount] = volume;
                _selectedVolumeCount++;
            }
        }

        // Expands selected volume cache only when selection contains more LightVolumes than before.
        private static void EnsureSelectedVolumeCapacity(int capacity) {
            if (_selectedVolumes.Length >= capacity) return;

            int newSize = _selectedVolumes.Length * 2;
            while (newSize < capacity) newSize *= 2;

            LightVolumeInstance[] volumes = new LightVolumeInstance[newSize];
            for (int i = 0; i < _selectedVolumeCount; i++) volumes[i] = _selectedVolumes[i];
            _selectedVolumes = volumes;
        }

        // Returns true when voxel preview mode has a valid Light Volume selection.
        private static bool HasActivePreview() {
            return _previewModeActive && _selectedVolumeCount > 0;
        }

        // Refreshes the selected volume cache and keeps preview mode transient to Light Volume selections.
        private static void RefreshSelection() {
            RefreshSelectedVolumes();
            if (_selectedVolumeCount == 0) {
                _previewModeActive = false;
                return;
            }

            if (!_previewModeActive) return;

            for (int i = 0; i < _selectedVolumeCount; i++) {
                LightVolumeInstance volume = _selectedVolumes[i];
                if (volume != null) LightVolumeTools.Recalculate(volume);
            }
        }

        // Releases hidden preview resources.
        private static void DisposeRenderer() {
            if (_renderer == null) return;
            _renderer.Dispose();
            _renderer = null;
        }

    }
}
#endif
