using System;
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
        private static LightVolumeManager[] _managers = Array.Empty<LightVolumeManager>();
        private static bool _refreshPending;

        // Installs editor lifecycle hooks and initializes clustering preview globals after domain reload.
        static LightVolumeClusteringPreview() {
            Shader.SetGlobalFloat(ClusteringEnabledID, 0f);
            RefreshManagers();
            Camera.onPreCull -= OnCameraPreCull;
            Camera.onPreCull += OnCameraPreCull;
            Camera.onPostRender -= OnCameraPostRender;
            Camera.onPostRender += OnCameraPostRender;
            EditorApplication.hierarchyChanged -= RefreshManagers;
            EditorApplication.hierarchyChanged += RefreshManagers;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened -= OnSceneOpened;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += OnSceneOpened;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += OnSceneSaving;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= OnSceneSaved;
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= ReleasePreviewResourcesBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += ReleasePreviewResourcesBeforeAssemblyReload;
            EditorApplication.quitting -= ReleasePreviewResources;
            EditorApplication.quitting += ReleasePreviewResources;
            RequestPreviewRefresh();
        }

        // Refreshes the cached set of Managers that can provide Scene View clustering data.
        private static void RefreshManagers() {
            _managers = UnityEngine.Object.FindObjectsByType<LightVolumeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        // Defers a clustering preview rebuild after a scene is opened.
        private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, UnityEditor.SceneManagement.OpenSceneMode mode) {
            RequestPreviewRefresh();
        }

        // Releases transient preview resources before Unity serializes a scene.
        private static void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path) {
            _refreshPending = true;
            RefreshManagers();
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
            // ObjectChangeEvents already coalesces edits. Consume that batch at the last safe
            // point before rendering so light buffers and froxel masks use current transforms.
            LightVolumeEditorUpdater.FlushPendingSceneChanges();
            LightVolumeManager manager = null;
            for (int i = 0; i < _managers.Length; i++) {
                LightVolumeManager candidate = _managers[i];
                if (candidate == null || !candidate.isActiveAndEnabled || !candidate.Clustering) continue;
                manager = candidate;
                break;
            }
            if (manager == null || camera.orthographic) {
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

        // Releases transient clustering resources for every cached Manager.
        private static void ReleasePreviewResources(bool releaseGeneratedMaterials) {
            ClusteringEnabledStack.Clear();
            Shader.SetGlobalFloat(ClusteringEnabledID, 0f);
            for (int i = 0; i < _managers.Length; i++) {
                LightVolumeManager manager = _managers[i];
                if (manager == null) continue;
                if (releaseGeneratedMaterials) manager.ReleaseClusteringPreviewForAssemblyReload();
                else manager.ReleaseClusteringPreview();
            }
        }

        // Fully releases preview resources that cannot survive an assembly reload.
        private static void ReleasePreviewResourcesBeforeAssemblyReload() {
            _refreshPending = false;
            RefreshManagers();
            ReleasePreviewResources(true);
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

        // Asset refreshes may restore serialized Udon proxy fields. Rebuild their derived editor
        // state exactly once, immediately before the next Scene View render.
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
            RefreshManagers();
            RecoverLoadedManagers();
        }

        // Rebuilds one stable manager snapshot without serializing or dirtying scene objects.
        private static void RecoverLoadedManagers() {
            ClusteringEnabledStack.Clear();
            Shader.SetGlobalFloat(ClusteringEnabledID, 0f);
            bool hasActiveManager = false;
            for (int i = 0; i < _managers.Length; i++) {
                LightVolumeManager manager = _managers[i];
                if (manager == null) continue;
                if (!manager.isActiveAndEnabled) {
                    manager.ReleaseClusteringPreview();
                    continue;
                }
                manager.RebuildClusteringPreviewState();
                hasActiveManager = true;
            }

            if (!hasActiveManager) SetDisabledShaderState();
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
    // Any AssetDatabase refresh may restore serialized Udon proxy state. The actual rebuild is
    // deferred to the next Scene View render, so multiple imports collapse into one operation.
    internal sealed class LightVolumeClusteringImportPostprocessor : AssetPostprocessor {
        // Requests a deferred preview rebuild after any AssetDatabase refresh.
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload) {
            LightVolumeClusteringPreview.RequestPreviewRefresh();
        }
    }
}
