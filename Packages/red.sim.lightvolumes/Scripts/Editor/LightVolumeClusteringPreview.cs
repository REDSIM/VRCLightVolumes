using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {
    // Keeps Scene View-aligned froxel clustering alive in Edit Mode without adding editor state to runtime Udon.
    [InitializeOnLoad]
    internal static class LightVolumeClusteringPreview {
        private static readonly int ClusteringEnabledID = Shader.PropertyToID("_UdonClusteringEnabled");
        private static readonly int LightVolumeEnabledID = Shader.PropertyToID("_UdonLightVolumeEnabled");
        private static readonly int LightVolumeCountID = Shader.PropertyToID("_UdonLightVolumeCount");
        private static readonly int LightVolumeAdditiveCountID = Shader.PropertyToID("_UdonLightVolumeAdditiveCount");
        private static readonly int PointLightCountID = Shader.PropertyToID("_UdonPointLightVolumeCount");
        private static readonly int PointLightCubeCountID = Shader.PropertyToID("_UdonPointLightVolumeCubeCount");
        private static readonly int PointLightShadowCountID = Shader.PropertyToID("_UdonPointLightVolumeShadowCount");
        private static readonly int PointLightShadowCubeCountID = Shader.PropertyToID("_UdonPointLightVolumeShadowCubeCount");
        private static readonly Stack<float> ClusteringEnabledStack = new Stack<float>(4);
        private static LightVolumeManager _manager;
        private static bool _refreshPending;

        // Installs editor lifecycle hooks and initializes clustering preview globals after domain reload.
        static LightVolumeClusteringPreview() {
            Shader.SetGlobalFloat(ClusteringEnabledID, 0f);
            RefreshManager();
            Camera.onPreCull += OnCameraPreCull;
            Camera.onPostRender += OnCameraPostRender;
            EditorApplication.hierarchyChanged += RefreshManager;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += OnSceneOpened;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
            RequestPreviewRefresh();
        }

        // Removes every global callback and releases transient preview state before this editor domain ends.
        private static void Shutdown() {
            Camera.onPreCull -= OnCameraPreCull;
            Camera.onPostRender -= OnCameraPostRender;
            EditorApplication.hierarchyChanged -= RefreshManager;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened -= OnSceneOpened;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
            _refreshPending = false;
            ReleasePreviewResources(true);
            _manager = null;
        }

        // Refreshes the only Manager allowed to provide Scene View clustering data.
        private static void RefreshManager() {
            LightVolumeManager manager = LightVolumeManagerEditorBackend.GetPrimaryManager();
            if (_manager == manager) return;
            if (_manager != null) _manager.ReleaseClusteringPreview();
            _manager = manager;
        }

        // Defers a clustering preview rebuild after a scene is opened.
        private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, UnityEditor.SceneManagement.OpenSceneMode mode) {
            RequestPreviewRefresh();
        }

        // Releases transient preview resources before Unity serializes a scene.
        private static void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) {
            _refreshPending = true;
            RefreshManager();
            ReleasePreviewResources();
        }

        // Requests fresh preview resources after scene serialization completes.
        private static void OnSceneSaved(UnityEngine.SceneManagement.Scene scene) {
            RequestPreviewRefresh();
        }

        // Builds clustering for Scene View cameras and disables it for unrelated editor cameras.
        private static void OnCameraPreCull(Camera camera) {
            if (Application.isPlaying || camera == null) return;
            bool isSceneViewCamera = IsSceneViewCamera(camera);
            if (isSceneViewCamera) ApplyPendingPreviewRefresh();
            ClusteringEnabledStack.Push(Shader.GetGlobalFloat(ClusteringEnabledID));
            if (!isSceneViewCamera) {
                Shader.SetGlobalFloat(ClusteringEnabledID, 0f);
                return;
            }
            // ObjectChangeEvents already coalesces edits. Consume that batch at the last safe point before rendering so light buffers and froxel masks use current transforms.
            LightVolumeEditorUpdater.FlushPendingSceneChanges();
            LightVolumeManager manager = _manager;
            if (manager == null || camera.orthographic) {
                Shader.SetGlobalFloat(ClusteringEnabledID, 0f);
                return;
            }
            if (!manager.isActiveAndEnabled || !manager.Clustering) {
                Shader.SetGlobalFloat(ClusteringEnabledID, 0f);
                return;
            }
            manager.UpdateClusteringFromCamera(camera);
        }

        // Restores the clustering shader state captured before an editor camera rendered.
        private static void OnCameraPostRender(Camera camera) {
            if (Application.isPlaying || ClusteringEnabledStack.Count == 0) return;
            Shader.SetGlobalFloat(ClusteringEnabledID, ClusteringEnabledStack.Pop());
        }

        // Checks both camera type and live Scene View instances for the requested camera.
        private static bool IsSceneViewCamera(Camera camera) {
            if (camera == null) return false;
            if (camera.cameraType == CameraType.SceneView) return true;
            if (SceneView.sceneViews == null) return false;
            for (int i = 0; i < SceneView.sceneViews.Count; i++) {
                SceneView sceneView = SceneView.sceneViews[i] as SceneView;
                if (sceneView != null && sceneView.camera == camera) return true;
            }
            return false;
        }

        // Releases clustering preview textures while retaining reusable generated materials.
        private static void ReleasePreviewResources() {
            ReleasePreviewResources(false);
        }

        // Releases transient clustering resources for the cached primary Manager.
        private static void ReleasePreviewResources(bool releaseGeneratedMaterials) {
            ClusteringEnabledStack.Clear();
            Shader.SetGlobalFloat(ClusteringEnabledID, 0f);
            if (_manager == null) return;
            if (releaseGeneratedMaterials) _manager.ReleaseClusteringPreviewForAssemblyReload();
            else _manager.ReleaseClusteringPreview();
        }

        // Tears down editor previews around play mode and rebuilds them on returning to edit mode.
        private static void OnPlayModeStateChanged(PlayModeStateChange state) {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode) {
                _refreshPending = false;
                ReleasePreviewResources();
            } else if (state == PlayModeStateChange.EnteredEditMode) {
                RequestPreviewRefresh();
            }
        }

        // Asset refreshes may restore serialized Udon proxy fields. Rebuild their derived editor state exactly once, immediately before the next Scene View render.
        internal static void RequestPreviewRefresh() {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
            _refreshPending = true;
            Shader.SetGlobalFloat(ClusteringEnabledID, 0f);
            SceneView.RepaintAll();
        }

        // Consumes one coalesced preview refresh immediately before Scene View rendering.
        private static void ApplyPendingPreviewRefresh() {
            if (!_refreshPending) return;
            _refreshPending = false;
            RefreshManager();
            RecoverManager();
        }

        // Rebuilds one stable Manager snapshot without serializing or dirtying scene objects.
        private static void RecoverManager() {
            ClusteringEnabledStack.Clear();
            Shader.SetGlobalFloat(ClusteringEnabledID, 0f);
            if (_manager == null || !_manager.isActiveAndEnabled) {
                if (_manager != null) _manager.ReleaseClusteringPreview();
                SetDisabledShaderState();
                return;
            }
            _manager.RebuildClusteringPreviewState();
        }

        // Clears every Light Volumes shader count when no active Manager can populate preview data.
        private static void SetDisabledShaderState() {
            Shader.SetGlobalFloat(LightVolumeCountID, 0f);
            Shader.SetGlobalFloat(LightVolumeAdditiveCountID, 0f);
            Shader.SetGlobalFloat(PointLightCountID, 0f);
            Shader.SetGlobalFloat(PointLightCubeCountID, 0f);
            Shader.SetGlobalFloat(PointLightShadowCountID, 0f);
            Shader.SetGlobalFloat(PointLightShadowCubeCountID, 0f);
            Shader.SetGlobalFloat(ClusteringEnabledID, 0f);
            Shader.SetGlobalFloat(LightVolumeEnabledID, 0f);
        }
    }
    // Any AssetDatabase refresh may restore serialized Udon proxy state. The actual rebuild is deferred to the next Scene View render, so multiple imports collapse into one operation.
    internal sealed class LightVolumeClusteringImportPostprocessor : AssetPostprocessor {
        // Requests a deferred preview rebuild after any AssetDatabase refresh.
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload) {
            LightVolumeClusteringPreview.RequestPreviewRefresh();
        }
    }
}
