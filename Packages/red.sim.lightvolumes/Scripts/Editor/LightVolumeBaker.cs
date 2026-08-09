using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes {
    // Owns global lightmapper callbacks for unified Udon authoring components.
    // All persistent bake settings remain on LightVolumeManager and its registered instances.
    [InitializeOnLoad]
    internal static class LightVolumeBaker {
        private const int MaxProbeBakedPointLightCount = 128;
        private const int ProbeBakeThreadGroupSize = 64;
        private const int AdditionalProbeIdStart = 0x4C560000;
        private const string ProbeBakeComputePath = "Packages/red.sim.lightvolumes/Scripts/Editor/PointLightProbeBake.compute";
        private const string ProbeBakeKernelName = "BakePointLightVolumesIntoProbes";

        // List order defines Unity's additional-probe IDs for the lifetime of the active bake.
        private static readonly List<LightVolumeInstance> _progressiveVolumes = new List<LightVolumeInstance>();
        private static LightVolumeManager _unityManager;
        private static bool _unityBakeCompleted;
        private static bool _unityProbePostProcessAttempted;
        private static bool _unityManagerFinalized;
        private static bool _progressiveCleanupQueued;
        private static Texture3D _probeBakeDummyVolumeTexture;
        private static Texture2DArray _probeBakeDummyTextureArray;

        private static LightVolumeManager _bakeryManager;
        private static bool _bakeryFullRenderActive;
        private static bool _bakeryWasBaking;
        private static bool _bakeryBitmaskPending;
        private static bool _bakeryCompletionQueued;
        private static bool _bakeryWatcherRefreshQueued;
        private static bool _bakeryWatcherSubscribed;

        // Installs lightmapper lifecycle callbacks once per editor domain.
        static LightVolumeBaker() {
#pragma warning disable CS0618
            UnityEditor.Experimental.Lightmapping.additionalBakedProbesCompleted += OnAdditionalBakedProbesCompleted;
#pragma warning restore CS0618
            Lightmapping.bakeStarted += OnUnityBakeStarted;
            Lightmapping.bakeCompleted += OnUnityBakeCompleted;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;

            if (BakeryEditorBridge.SupportsFullRenderLifecycle) {
                BakeryEditorBridge.SubscribePreFullRender(OnBakeryStarted);
                BakeryEditorBridge.SubscribeFinished(OnBakeryFinished);
                EditorApplication.hierarchyChanged += QueueBakeryWatcherRefresh;
                EditorSceneManager.sceneOpened += OnSceneOpened;
                EditorSceneManager.sceneClosed += OnSceneClosed;
                QueueBakeryWatcherRefresh();
            }
        }

        // Removes global callbacks, temporary probe groups and compute fallback textures.
        private static void Shutdown() {
#pragma warning disable CS0618
            UnityEditor.Experimental.Lightmapping.additionalBakedProbesCompleted -= OnAdditionalBakedProbesCompleted;
#pragma warning restore CS0618
            Lightmapping.bakeStarted -= OnUnityBakeStarted;
            Lightmapping.bakeCompleted -= OnUnityBakeCompleted;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
            ResetUnityBakeState();

            BakeryEditorBridge.UnsubscribePreFullRender(OnBakeryStarted);
            BakeryEditorBridge.UnsubscribeFinished(OnBakeryFinished);
            EditorApplication.hierarchyChanged -= QueueBakeryWatcherRefresh;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorApplication.delayCall -= RefreshBakeryWatcher;
            EditorApplication.update -= WatchBakeryBake;
            ResetBakeryBakeState();
            _bakeryWatcherRefreshQueued = false;
            _bakeryWatcherSubscribed = false;

            if (_probeBakeDummyVolumeTexture != null) UnityEngine.Object.DestroyImmediate(_probeBakeDummyVolumeTexture);
            if (_probeBakeDummyTextureArray != null) UnityEngine.Object.DestroyImmediate(_probeBakeDummyTextureArray);
            _probeBakeDummyVolumeTexture = null;
            _probeBakeDummyTextureArray = null;
        }

        // Registers one additional Progressive probe group for every eligible Light Volume.
        private static void OnUnityBakeStarted() {
            if (Application.isPlaying) return;
            EditorApplication.delayCall -= CleanupProgressiveBakeAfterCallbacks;
            _progressiveCleanupQueued = false;
            CleanupProgressiveProbeRegistrations();

            _unityManager = GetActiveManager(0);
            _unityBakeCompleted = false;
            _unityProbePostProcessAttempted = false;
            _unityManagerFinalized = false;
            if (_unityManager == null) return;

            HashSet<LightVolumeInstance> registeredVolumes = new HashSet<LightVolumeInstance>();
            LightVolumeInstance[] volumes = _unityManager.LightVolumeInstances;
            if (volumes == null) return;
            for (int i = 0; i < volumes.Length; i++) {
                LightVolumeInstance volume = volumes[i];
                if (!IsBakeVolume(_unityManager, volume) || !registeredVolumes.Add(volume)) continue;
                if (LightVolumeTools.GetVoxelCount(volume) < 0) {
                    Debug.LogError($"[LightVolumes] Can't add {volume.gameObject.name} to the Progressive bake. Resolution is invalid or the voxel count is too large!", volume);
                    continue;
                }
                int additionalProbeId = GetAdditionalProbeId(_progressiveVolumes.Count);
                try {
                    if (!SetAdditionalProbes(volume, additionalProbeId)) continue;
                    _progressiveVolumes.Add(volume);
                    Debug.Log($"[LightVolumes] Added Progressive probes for \"{volume.gameObject.name}\" (group {additionalProbeId}).", volume);
                } catch (Exception exception) {
                    RemoveAdditionalProbes(additionalProbeId);
                    Debug.LogException(exception, volume);
                }
            }
        }

        // Converts completed Progressive probe groups into per-volume 3D textures.
        private static void OnAdditionalBakedProbesCompleted() {
            if (Application.isPlaying || _unityManager == null) {
                ResetUnityBakeState();
                return;
            }
            EditorApplication.delayCall -= CleanupProgressiveBakeAfterCallbacks;
            _progressiveCleanupQueued = false;

            for (int i = 0; i < _progressiveVolumes.Count; i++) {
                LightVolumeInstance volume = _progressiveVolumes[i];
                int additionalProbeId = GetAdditionalProbeId(i);
                try {
                    if (volume == null || volume.LightVolumeManager != _unityManager) continue;
                    if (!Save3DTexturesProgressive(volume, additionalProbeId)) continue;
                    volume.InvBakedRotation = Quaternion.Inverse(LightVolumeTools.GetRotation(volume));
                    LVUtils.MarkDirty(volume);
                    LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
                } catch (Exception exception) {
                    Debug.LogException(exception, volume);
                } finally {
                    RemoveAdditionalProbes(additionalProbeId);
                }
            }
            _progressiveVolumes.Clear();
            PostProcessUnityProbesOnce();
            FinalizeUnityManagerOnce();
            if (_unityBakeCompleted) _unityManager = null;
        }

        // Post-processes Unity probes and finalizes the primary Manager after Progressive baking completes.
        private static void OnUnityBakeCompleted() {
            if (Application.isPlaying || _unityManager == null) {
                ResetUnityBakeState();
                return;
            }
            _unityBakeCompleted = true;
            PostProcessUnityProbesOnce();
            if (_progressiveVolumes.Count == 0) {
                FinalizeUnityManagerOnce();
                _unityManager = null;
                return;
            }
            // Unity normally invokes additionalBakedProbesCompleted first. Deferring cleanup by one editor tick also handles versions that invoke bakeCompleted first in the same cycle.
            if (_progressiveCleanupQueued) return;
            _progressiveCleanupQueued = true;
            EditorApplication.delayCall += CleanupProgressiveBakeAfterCallbacks;
        }

        // Removes unresolved additional probes when Unity omits their completion callback.
        private static void CleanupProgressiveBakeAfterCallbacks() {
            EditorApplication.delayCall -= CleanupProgressiveBakeAfterCallbacks;
            _progressiveCleanupQueued = false;
            if (_progressiveVolumes.Count > 0) {
                Debug.LogWarning("[LightVolumes] Progressive baking completed without an additional-probe result. Temporary probe registrations were removed; no atlas was generated from them.");
                CleanupProgressiveProbeRegistrations();
            }
            _unityManager = null;
        }

        // Unregisters every temporary Progressive probe group still owned by this bake.
        private static void CleanupProgressiveProbeRegistrations() {
            for (int i = 0; i < _progressiveVolumes.Count; i++) {
                try {
                    RemoveAdditionalProbes(GetAdditionalProbeId(i));
                } catch (Exception exception) {
                    Debug.LogException(exception);
                }
            }
            _progressiveVolumes.Clear();
        }

        // Cancels the active Progressive bake and removes every temporary additional-probe registration.
        private static void ResetUnityBakeState() {
            EditorApplication.delayCall -= CleanupProgressiveBakeAfterCallbacks;
            _progressiveCleanupQueued = false;
            CleanupProgressiveProbeRegistrations();
            _unityManager = null;
            _unityBakeCompleted = false;
            _unityProbePostProcessAttempted = false;
            _unityManagerFinalized = false;
        }

        // Runs light-probe post-processing at most once for the current Progressive bake.
        private static void PostProcessUnityProbesOnce() {
            if (_unityProbePostProcessAttempted) return;
            _unityProbePostProcessAttempted = true;
            try {
                PostProcessLightProbes(_unityManager, false);
            } catch (Exception exception) {
                Debug.LogException(exception, _unityManager);
            }
        }

        // Checks Manager ownership, bake state and scene eligibility for one Light Volume.
        private static bool IsBakeVolume(LightVolumeManager manager, LightVolumeInstance volume) {
            return volume != null && volume.LightVolumeManager == manager && volume.Bake && volume.gameObject.activeInHierarchy && !volume.CompareTag("EditorOnly");
        }

        // Maps stable list order to this package's additional-probe ID namespace.
        private static int GetAdditionalProbeId(int index) {
            return AdditionalProbeIdStart + index;
        }

        // Queues shadows and atlas generation once after a Progressive bake.
        private static void FinalizeUnityManagerOnce() {
            if (_unityManagerFinalized) return;
            _unityManagerFinalized = true;
            FinalizeManager(_unityManager);
            Debug.Log("[LightVolumes] Progressive Light Volume atlas generation queued.");
        }

        // Queues shadow baking and atlas packing for the completed Manager.
        private static void FinalizeManager(LightVolumeManager manager) {
            if (manager == null) return;
            LightVolumeManagerEditorBackend.BakeShadowMaps(manager);
            LightVolumeManagerEditorBackend.QueueAtlasGeneration(manager);
        }

        // Registers one Light Volume's voxel centers with Unity's Progressive lightmapper.
        private static bool SetAdditionalProbes(LightVolumeInstance volume, int id) {
            if (volume == null) return false;
            LightVolumeTools.Recalculate(volume);
            if (!LightVolumeTools.TryCalculateProbePositions(volume, volume.Resolution, out Vector3[] positions)) return false;
#pragma warning disable CS0618
            UnityEditor.Experimental.Lightmapping.SetAdditionalBakedProbes(id, positions);
#pragma warning restore CS0618
            return true;
        }

        // Removes one temporary group from Unity's additional baked probes API.
        private static void RemoveAdditionalProbes(int id) {
#pragma warning disable CS0618
            UnityEditor.Experimental.Lightmapping.SetAdditionalBakedProbes(id, Array.Empty<Vector3>());
#pragma warning restore CS0618
        }

        // Reads a Progressive probe group and saves its L0/L1 data as volume textures.
        private static bool Save3DTexturesProgressive(LightVolumeInstance volume, int id) {
            if (volume == null) return false;

            int voxelCount = LightVolumeTools.GetVoxelCount(volume);
            if (voxelCount < 0) {
                Debug.LogError($"[LightVolumes] Can't save light volume {volume.gameObject.name} 3D texture. Resolution is invalid or the voxel count is too large!", volume);
                return false;
            }

            LightVolumeManager manager = volume.LightVolumeManager;
            if (manager == null) return false;

            using (NativeArray<SphericalHarmonicsL2> probes = new NativeArray<SphericalHarmonicsL2>(voxelCount, Allocator.Temp))
            using (NativeArray<float> validity = new NativeArray<float>(voxelCount, Allocator.Temp)) {
#pragma warning disable CS0618
                if (!UnityEditor.Experimental.Lightmapping.GetAdditionalBakedProbes(id, probes, validity)) {
                    Debug.LogError("[LightVolumes] Can't grab light volume data. No additional baked probes found!", volume);
                    return false;
                }
#pragma warning restore CS0618

                Vector3[] l0 = new Vector3[voxelCount];
                Vector3[] l1r = new Vector3[voxelCount];
                Vector3[] l1g = new Vector3[voxelCount];
                Vector3[] l1b = new Vector3[voxelCount];
                for (int i = 0; i < voxelCount; i++) {
                    l0[i] = new Vector3(probes[i][0, 0], probes[i][1, 0], probes[i][2, 0]);
                    l1r[i] = new Vector3(probes[i][0, 3], probes[i][0, 1], probes[i][0, 2]);
                    l1g[i] = new Vector3(probes[i][1, 3], probes[i][1, 1], probes[i][1, 2]);
                    l1b[i] = new Vector3(probes[i][2, 3], probes[i][2, 1], probes[i][2, 2]);
                }

                float[] probeValidity = manager.DilateInvalidProbes ? validity.ToArray() : null;
                return SaveCustomProbesBaked(volume, l0, l1r, l1g, l1b, probeValidity, manager.Denoise);
            }
        }

        // Validates, processes and persists custom-lightmapper SH output for one Light Volume.
        internal static bool SaveCustomProbesBaked(LightVolumeInstance volume, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity, bool denoise) {
            if (volume == null || volume.LightVolumeManager == null) return false;

            LightVolumeManager manager = volume.LightVolumeManager;
            int width = volume.Resolution.x;
            int height = volume.Resolution.y;
            int depth = volume.Resolution.z;
            if (!LVUtils.TryPrepareLightVolumeProbeData(l0, l1r, l1g, l1b, validity, width, height, depth, manager.DilationIterations, manager.DilationBackfaceBias, denoise, out Color[][] textureColors, out string error)) {
                Debug.LogError($"[LightVolumes] Can't save custom bake for light volume {volume.gameObject.name}. {error}", volume);
                return false;
            }

            Scene scene = volume.gameObject.scene;
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) {
                Debug.LogError($"[LightVolumes] Can't save custom bake for light volume {volume.gameObject.name}. Save the containing scene first!", volume);
                return false;
            }

            Texture3D texture0 = CreateTexture(width, height, depth);
            Texture3D texture1 = CreateTexture(width, height, depth);
            Texture3D texture2 = CreateTexture(width, height, depth);
            if (!LVUtils.Apply3DTextureData(texture0, textureColors[0]) || !LVUtils.Apply3DTextureData(texture1, textureColors[1]) || !LVUtils.Apply3DTextureData(texture2, textureColors[2])) {
                UnityEngine.Object.DestroyImmediate(texture0);
                UnityEngine.Object.DestroyImmediate(texture1);
                UnityEngine.Object.DestroyImmediate(texture2);
                return false;
            }

            string path = $"{Path.GetDirectoryName(scene.path)}/{scene.name}/VRCLightVolumes/Temp";
            string escapedName = LVUtils.EscapeFileName(volume.gameObject.name);
            LVUtils.SaveAsAsset(texture0, $"{path}/{escapedName}_0.asset");
            LVUtils.SaveAsAsset(texture1, $"{path}/{escapedName}_1.asset");
            LVUtils.SaveAsAsset(texture2, $"{path}/{escapedName}_2.asset");
            volume.Texture0 = texture0;
            volume.Texture1 = texture1;
            volume.Texture2 = texture2;
            LVUtils.MarkDirty(volume);
            return true;
        }

        // Creates a clamp-wrapped half-float 3D texture suitable for baked SH coefficients.
        private static Texture3D CreateTexture(int width, int height, int depth) {
            return new Texture3D(width, height, depth, TextureFormat.RGBAHalf, false) {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }

        // Coalesces hierarchy and scene changes into one Bakery watcher refresh.
        internal static void QueueBakeryWatcherRefresh() {
            if (!BakeryEditorBridge.SupportsFullRenderLifecycle || Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (_bakeryWatcherRefreshQueued) return;
            _bakeryWatcherRefreshQueued = true;
            EditorApplication.delayCall += RefreshBakeryWatcher;
        }

        // Re-evaluates Bakery monitoring after a scene is opened.
        private static void OnSceneOpened(Scene scene, OpenSceneMode mode) {
            QueueBakeryWatcherRefresh();
        }

        // Re-evaluates Bakery monitoring after a scene is closed.
        private static void OnSceneClosed(Scene scene) {
            QueueBakeryWatcherRefresh();
        }

        // Enables per-frame Bakery state polling only while a Bakery Manager exists or a bake is active.
        private static void RefreshBakeryWatcher() {
            EditorApplication.delayCall -= RefreshBakeryWatcher;
            _bakeryWatcherRefreshQueued = false;
            bool required = GetActiveManager(1) != null || _bakeryWasBaking || _bakeryFullRenderActive;
            if (required == _bakeryWatcherSubscribed) return;
            if (required) EditorApplication.update += WatchBakeryBake;
            else EditorApplication.update -= WatchBakeryBake;
            _bakeryWatcherSubscribed = required;
        }

        // This is the only per-frame editor callback. Full-render lifecycle comes from Bakery's dedicated events; polling only applies live bitmasks and detects cancellation.
        private static void WatchBakeryBake() {
            bool baking = BakeryEditorBridge.IsBaking;
            bool wasBaking = _bakeryWasBaking;
            if (baking && _bakeryBitmaskPending) TryApplyBakeryRuntimeBitmasks();
            _bakeryWasBaking = baking;
            if (baking || !wasBaking || !_bakeryFullRenderActive) return;
            if (_bakeryCompletionQueued) return;
            // A confirmed success always arrives through OnFinishedFullRender. Any other falling edge is cancellation or an interrupted render and must not import stale output.
            CancelBakeryCompletion();
        }

        // Begins tracking only Bakery's full scene render, excluding probes, APV, reflection-only and selected-group operations that share bakeInProgress.
        private static void OnBakeryStarted(object sender, EventArgs args) {
            if (Application.isPlaying) return;
            BeginBakeryBake();
        }

        // Captures the primary Bakery Manager, prepares helpers and starts the global bitmask override.
        private static void BeginBakeryBake() {
            EditorApplication.delayCall -= CompleteBakeryBake;
            _bakeryCompletionQueued = false;
            _bakeryManager = GetActiveManager(1);
            _bakeryBitmaskPending = false;
            _bakeryFullRenderActive = _bakeryManager != null;
            if (!_bakeryFullRenderActive) return;

            ConfigureExistingBakeryVolumes(_bakeryManager);
            BakeryEditorBridge.ApplyStoredBitmasks(_bakeryManager.VolumeBitmask, _bakeryManager.ProbeBitmask);
            BakeryEditorBridge.ClearImplicitProbeGroups();
            _bakeryBitmaskPending = true;
        }

        // Defers Bakery completion until its outer render loop has fully stopped.
        private static void OnBakeryFinished(object sender, EventArgs args) {
            if (Application.isPlaying || !_bakeryFullRenderActive) return;
            QueueBakeryCompletion();
        }

        // Coalesces Bakery completion callbacks into one delayed finalization.
        private static void QueueBakeryCompletion() {
            if (_bakeryCompletionQueued || !_bakeryFullRenderActive) return;
            _bakeryCompletionQueued = true;
            EditorApplication.delayCall += CompleteBakeryBake;
        }

        // Imports Bakery textures, post-processes probes and finalizes the restored primary Manager.
        private static void CompleteBakeryBake() {
            EditorApplication.delayCall -= CompleteBakeryBake;
            _bakeryCompletionQueued = false;
            if (Application.isPlaying) {
                ResetBakeryBakeState();
                return;
            }
            if (!_bakeryFullRenderActive) return;
            // Bakery fires OnFinishedFullRender before its outer update clears bakeInProgress.
            if (BakeryEditorBridge.IsBaking) {
                QueueBakeryCompletion();
                return;
            }
            if (BakeryEditorBridge.WasCanceled) {
                CancelBakeryCompletion();
                return;
            }
            _bakeryFullRenderActive = false;
            LightVolumeManager manager = ResolveBakeryCompletionManager();
            _bakeryBitmaskPending = false;
            _bakeryWasBaking = false;
            try {
                if (manager == null) return;

                LightVolumeInstance[] volumes = manager.LightVolumeInstances;
                if (volumes != null) {
                    for (int i = 0; i < volumes.Length; i++) {
                        LightVolumeInstance volume = volumes[i];
                        if (!IsBakeVolume(manager, volume)) continue;
                        try {
                            LightVolumeTools.Recalculate(volume);
                            volume.InvBakedRotation = Quaternion.Inverse(LightVolumeTools.GetRotation(volume));
                            LightVolumeTools.TryImportBakeryTextures(volume);
                            LVUtils.MarkDirty(volume);
                            LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
                        } catch (Exception exception) {
                            Debug.LogException(exception, volume);
                        }
                    }
                }

                try {
                    PostProcessLightProbes(manager, manager.FixLightProbesL1);
                } catch (Exception exception) {
                    Debug.LogException(exception, manager);
                }
                FinalizeManager(manager);
                Debug.Log("[LightVolumes] Bakery Light Volume atlas generation queued.");
            } finally {
                _bakeryManager = null;
                QueueBakeryWatcherRefresh();
            }
        }

        // Bakery can unload and recreate the scene in deferred mode. Always resolve the completion
        // target from the restored scene instead of using the pre-bake object cache.
        private static LightVolumeManager ResolveBakeryCompletionManager() {
            return GetActiveManager(1);
        }

        // Clears all Bakery completion state after a canceled or interrupted full render.
        private static void CancelBakeryCompletion() {
            ResetBakeryBakeState();
            QueueBakeryWatcherRefresh();
        }

        // Clears transient Bakery state without treating an interrupted render as a successful bake.
        private static void ResetBakeryBakeState() {
            EditorApplication.delayCall -= CompleteBakeryBake;
            _bakeryCompletionQueued = false;
            _bakeryFullRenderActive = false;
            _bakeryBitmaskPending = false;
            _bakeryWasBaking = false;
            _bakeryManager = null;
        }

        // Bake callbacks are editor-only; clear transient registrations before crossing play-mode boundaries.
        private static void OnPlayModeStateChanged(PlayModeStateChange state) {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode) {
                ResetUnityBakeState();
                ResetBakeryBakeState();
                EditorApplication.delayCall -= RefreshBakeryWatcher;
                _bakeryWatcherRefreshQueued = false;
                EditorApplication.update -= WatchBakeryBake;
                _bakeryWatcherSubscribed = false;
            } else if (state == PlayModeStateChange.EnteredEditMode) {
                QueueBakeryWatcherRefresh();
            }
        }

        // Synchronizes already-created Bakery Volume helpers before rendering begins.
        private static void ConfigureExistingBakeryVolumes(LightVolumeManager manager) {
            LightVolumeInstance[] volumes = manager.LightVolumeInstances;
            if (volumes == null) return;
            for (int i = 0; i < volumes.Length; i++) {
                LightVolumeInstance volume = volumes[i];
                if (!IsBakeVolume(manager, volume)) continue;
                LightVolumeTools.SetupBakeryDependencies(volume, false);
            }
        }

        // Applies bitmasks once Bakery creates its live implicit probe groups.
        private static void TryApplyBakeryRuntimeBitmasks() {
            if (_bakeryManager == null) {
                _bakeryBitmaskPending = false;
                return;
            }
            if (!BakeryEditorBridge.TryApplyRuntimeBitmasks(_bakeryManager.VolumeBitmask, _bakeryManager.ProbeBitmask)) return;
            _bakeryBitmaskPending = false;
        }

        // Optionally derings L1 probes and accumulates eligible Point Light Volumes into them.
        private static void PostProcessLightProbes(LightVolumeManager manager, bool dering) {
            bool bakePointLights = HasProbeBakedPointLights(manager);
            if (!dering && !bakePointLights) return;

            LightProbes probes = LightmapSettings.lightProbes;
            if (probes == null || probes.count == 0) {
                Debug.LogWarning("[LightVolumes] No Light Probes found to postprocess.");
                return;
            }
            SphericalHarmonicsL2[] sh = probes.bakedProbes;
            Vector3[] positions = probes.positions;
            if (sh == null || sh.Length == 0 || positions == null) return;

            bool didDering = dering && !LVUtils.CheckSHL2(sh[0]);
            if (dering && !didDering)
                Debug.Log("[LightVolumes] L2 Light Probes detected. Bakery L1 fix was skipped.");
            if (didDering) {
                for (int i = 0; i < sh.Length; i++) sh[i] = LVUtils.DeringSH(sh[i]);
            }

            int bakedPointLightCount = 0;
            if (bakePointLights) {
                try {
                    bakedPointLightCount = BakePointLightsIntoProbes(manager, sh, positions, MaxProbeBakedPointLightCount);
                } catch (Exception exception) {
                    Debug.LogException(exception, manager);
                }
            }
            if (!didDering && bakedPointLightCount == 0) return;

            probes.bakedProbes = sh;
            EditorUtility.SetDirty(probes);
            AssetDatabase.SaveAssets();
            string deringLog = didDering ? $"{sh.Length} Light Probes fixed" : "";
            string pointLightLog = bakedPointLightCount > 0 ? $"{bakedPointLightCount} Point Light Volumes baked into Light Probes" : "";
            Debug.Log($"[LightVolumes] {deringLog}{(didDering && bakedPointLightCount > 0 ? ", " : "")}{pointLightLog}.");
        }

        // Checks whether any active registered Point Light Volume requests probe baking.
        private static bool HasProbeBakedPointLights(LightVolumeManager manager) {
            if (manager == null || manager.PointLightVolumeInstances == null) return false;
            PointLightVolumeInstance[] pointLights = manager.PointLightVolumeInstances;
            for (int i = 0; i < pointLights.Length; i++) {
                PointLightVolumeInstance pointLight = pointLights[i];
                if (pointLight != null && pointLight.LightVolumeManager == manager && pointLight.BakeIntoProbes && pointLight.isActiveAndEnabled && !pointLight.CompareTag("EditorOnly") && pointLight.Intensity != 0f && pointLight.Color != Color.black) return true;
            }
            return false;
        }

        // Uses the runtime-compatible compute path to add one Manager's lights to baked probes.
        private static int BakePointLightsIntoProbes(LightVolumeManager manager, SphericalHarmonicsL2[] sh, Vector3[] probePositions, int lightCapacity) {
            int probeCount = Mathf.Min(sh.Length, probePositions.Length);
            if (probeCount == 0) return 0;
            if (!SystemInfo.supportsComputeShaders) {
                Debug.LogError("[LightVolumes] Compute shaders are unavailable. Point Light Volumes were not baked into Light Probes.", manager);
                return 0;
            }

            lightCapacity = Mathf.Clamp(lightCapacity, 0, MaxProbeBakedPointLightCount);
            Vector4[] pointPositions = new Vector4[lightCapacity];
            Vector4[] pointColors = new Vector4[lightCapacity];
            Vector4[] pointExtraData = new Vector4[lightCapacity];
            Vector4[] pointDirections = new Vector4[lightCapacity];
            Vector4[] pointCustomIds = new Vector4[lightCapacity];
            int pointLightCount = manager.GetEditorProbeBakePointLightData(pointPositions, pointColors, pointExtraData, pointDirections, pointCustomIds, out int missingProjectionCount, out int overflowCount);
            if (missingProjectionCount > 0) {
                Debug.LogWarning($"[LightVolumes] Skipped {missingProjectionCount} Point Light Volumes because their projection texture is unavailable in the manager array.", manager);
            }
            if (overflowCount > 0) {
                Debug.LogWarning($"[LightVolumes] Skipped {overflowCount} Point Light Volumes. Probe baking supports at most {MaxProbeBakedPointLightCount} active registered lights.", manager);
            }
            if (pointLightCount == 0) return 0;

            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ProbeBakeComputePath);
            if (compute == null || !compute.HasKernel(ProbeBakeKernelName)) {
                Debug.LogError($"[LightVolumes] Missing or invalid probe bake compute shader at {ProbeBakeComputePath}.", manager);
                return 0;
            }

            Vector4[] probeSh = new Vector4[probeCount * 3];
            PackProbeSH(sh, probeSh, probeCount);
            ComputeBuffer positionsBuffer = null;
            ComputeBuffer shBuffer = null;
            try {
                positionsBuffer = new ComputeBuffer(probeCount, 12);
                shBuffer = new ComputeBuffer(probeSh.Length, 16);
                positionsBuffer.SetData(probePositions, 0, 0, probeCount);
                shBuffer.SetData(probeSh);

                int kernel = compute.FindKernel(ProbeBakeKernelName);
                compute.SetInt("_ProbeCount", probeCount);
                compute.SetFloat("_UdonLightVolumeVersion", 3f);
                compute.SetFloat("_UdonPointLightVolumeCount", pointLightCount);
                compute.SetFloat("_UdonPointLightVolumeCubeCount", manager.CubemapsCount);
                compute.SetFloat("_UdonPointLightVolumeShadowCubeCount", 0f);
                compute.SetFloat("_UdonPointLightVolumeShadowCount", 0f);
                compute.SetFloat("_UdonLightVolumeOcclusionCount", 0f);
                compute.SetTexture(kernel, "_UdonLightVolume", GetProbeBakeDummyVolumeTexture());
                compute.SetTexture(kernel, "_UdonPointLightVolumeTexture", GetProbeBakeCustomTexture(manager));
                compute.SetTexture(kernel, "_UdonPointLightVolumeShadowTexture", GetProbeBakeDummyTextureArray());
                compute.SetVectorArray("_UdonPointLightVolumePosition", pointPositions);
                compute.SetVectorArray("_UdonPointLightVolumeColor", pointColors);
                compute.SetVectorArray("_UdonPointLightVolumeExtraData", pointExtraData);
                compute.SetVectorArray("_UdonPointLightVolumeDirection", pointDirections);
                compute.SetVectorArray("_UdonPointLightVolumeCustomID", pointCustomIds);
                compute.SetBuffer(kernel, "_ProbePositions", positionsBuffer);
                compute.SetBuffer(kernel, "_ProbeSH", shBuffer);
                compute.Dispatch(kernel, Mathf.CeilToInt(probeCount / (float)ProbeBakeThreadGroupSize), 1, 1);
                shBuffer.GetData(probeSh);
            } finally {
                positionsBuffer?.Release();
                shBuffer?.Release();
            }

            UnpackProbeSH(probeSh, sh, probeCount);
            return pointLightCount;
        }

        // Returns the Manager projection array or a valid one-slice fallback texture.
        private static Texture GetProbeBakeCustomTexture(LightVolumeManager manager) {
            RenderTexture texture = manager.CustomTextures;
            return texture != null && texture.volumeDepth > 0 && texture.IsCreated() ? texture : GetProbeBakeDummyTextureArray();
        }

        // Lazily creates the dummy 3D texture required by the shared lighting compute shader.
        private static Texture3D GetProbeBakeDummyVolumeTexture() {
            if (_probeBakeDummyVolumeTexture != null) return _probeBakeDummyVolumeTexture;
            _probeBakeDummyVolumeTexture = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            _probeBakeDummyVolumeTexture.Apply(false, true);
            return _probeBakeDummyVolumeTexture;
        }

        // Lazily creates the dummy texture array required for unused projection and shadow inputs.
        private static Texture2DArray GetProbeBakeDummyTextureArray() {
            if (_probeBakeDummyTextureArray != null) return _probeBakeDummyTextureArray;
            _probeBakeDummyTextureArray = new Texture2DArray(1, 1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            _probeBakeDummyTextureArray.Apply(false, true);
            return _probeBakeDummyTextureArray;
        }

        // Packs L0/L1 probe coefficients into the compute shader's three-vector layout.
        private static void PackProbeSH(SphericalHarmonicsL2[] sh, Vector4[] packed, int count) {
            for (int i = 0; i < count; i++) {
                int index = i * 3;
                packed[index] = new Vector4(sh[i][0, 3], sh[i][0, 1], sh[i][0, 2], sh[i][0, 0]);
                packed[index + 1] = new Vector4(sh[i][1, 3], sh[i][1, 1], sh[i][1, 2], sh[i][1, 0]);
                packed[index + 2] = new Vector4(sh[i][2, 3], sh[i][2, 1], sh[i][2, 2], sh[i][2, 0]);
            }
        }

        // Writes compute-shader L0/L1 output back into Unity probe coefficients.
        private static void UnpackProbeSH(Vector4[] packed, SphericalHarmonicsL2[] sh, int count) {
            for (int i = 0; i < count; i++) {
                int index = i * 3;
                sh[i][0, 3] = packed[index].x;
                sh[i][0, 1] = packed[index].y;
                sh[i][0, 2] = packed[index].z;
                sh[i][0, 0] = packed[index].w;
                sh[i][1, 3] = packed[index + 1].x;
                sh[i][1, 1] = packed[index + 1].y;
                sh[i][1, 2] = packed[index + 1].z;
                sh[i][1, 0] = packed[index + 1].w;
                sh[i][2, 3] = packed[index + 2].x;
                sh[i][2, 1] = packed[index + 2].y;
                sh[i][2, 2] = packed[index + 2].z;
                sh[i][2, 0] = packed[index + 2].w;
            }
        }

        // Returns the primary Manager only when it is active and uses the requested lightmapper.
        private static LightVolumeManager GetActiveManager(int bakingMode) {
            LightVolumeManager manager = LightVolumeManagerEditorBackend.GetPrimaryManager();
            return manager != null && manager.BakingMode == bakingMode && manager.enabled && manager.gameObject.activeInHierarchy ? manager : null;
        }
    }
}
