using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
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
        private const int ProgressiveCompletionProbeId = 0x4C56FFFF;
        private const string ProbeBakeComputePath = "Packages/red.sim.lightvolumes/Scripts/Editor/PointLightProbeBake.compute";
        private const string ProbeBakeKernelName = "BakePointLightVolumesIntoProbes";

        // List order defines Unity's additional-probe IDs for the lifetime of the active bake.
        private static readonly List<LightVolumeInstance> _progressiveVolumes = new List<LightVolumeInstance>();
        private static LightVolumeManager _unityManager;
        private static LightProbes _unityInitialLightProbes;
        private static int _unityInitialLightProbesDirtyCount = -1;
        private static Texture3D _probeBakeDummyVolumeTexture;
        private static Texture2DArray _probeBakeDummyTextureArray;

        private static LightVolumeManager _bakeryManager;
        private static bool _bakeryFullRenderActive;
        private static bool _bakeryBitmaskPending;
        private static bool _bakeryProbePostProcessPending;
        private static bool _bakeryL2ProbePostProcessPending;
        private static bool _bakeryCompletionQueued;

        // Installs lightmapper lifecycle callbacks once per editor domain.
        static LightVolumeBaker() {
            Lightmapping.bakeStarted += OnUnityBakeStarted;
            Lightmapping.bakeCompleted += OnUnityBakeCompleted;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;

            BakeryEditorBridge.SetLifecycleCallbacks(OnBakeryStarted, OnBakeryFinished, OnBakeryProbesFinished, true);
        }

        // Removes global callbacks, temporary probe groups and compute fallback textures.
        private static void Shutdown() {
            Lightmapping.bakeStarted -= OnUnityBakeStarted;
            Lightmapping.bakeCompleted -= OnUnityBakeCompleted;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
            ResetUnityBakeState();

            BakeryEditorBridge.SetLifecycleCallbacks(OnBakeryStarted, OnBakeryFinished, OnBakeryProbesFinished, false);
            ResetBakeryBakeState();

            if (_probeBakeDummyVolumeTexture != null) UnityEngine.Object.DestroyImmediate(_probeBakeDummyVolumeTexture);
            if (_probeBakeDummyTextureArray != null) UnityEngine.Object.DestroyImmediate(_probeBakeDummyTextureArray);
            _probeBakeDummyVolumeTexture = null;
            _probeBakeDummyTextureArray = null;
        }

        // Registers a completion marker and one Progressive probe group for every eligible Light Volume.
        private static void OnUnityBakeStarted() {
            if (Application.isPlaying) return;
            ResetUnityBakeState();

            _unityManager = GetActiveManager(0);
            if (_unityManager == null) return;
            _unityInitialLightProbes = LightmapSettings.lightProbes;
            _unityInitialLightProbesDirtyCount = _unityInitialLightProbes != null ? EditorUtility.GetDirtyCount(_unityInitialLightProbes) : -1;

            try {
                // bakeCompleted is raised for canceled jobs too. A one-probe result lets us verify the additional-probe stage even when the Manager contains only Point Lights.
                SetAdditionalProbes(ProgressiveCompletionProbeId, new[] { Vector3.zero });
            } catch (Exception exception) {
                Debug.LogError($"[LightVolumes] Can't register the Progressive completion probe. {exception}", _unityManager);
                ResetUnityBakeState();
                return;
            }

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
                    Debug.LogError($"[LightVolumes] {exception}", volume);
                }
            }
        }

        // Collects committed Progressive results after Unity has published its final Light Probes asset.
        private static void OnUnityBakeCompleted() {
            if (Application.isPlaying || _unityManager == null) {
                ResetUnityBakeState();
                return;
            }

            LightVolumeManager manager = _unityManager;
            if (!HasProgressiveCompletionProbeResult()) {
                Debug.LogWarning("[LightVolumes] Progressive baking ended without the completion-probe result. Temporary registrations were removed and Light Volume data was left unchanged.", manager);
                ResetUnityBakeState();
                return;
            }
            bool lightProbesCommitted = HaveUnityLightProbesChanged();

            for (int i = 0; i < _progressiveVolumes.Count; i++) {
                LightVolumeInstance volume = _progressiveVolumes[i];
                int additionalProbeId = GetAdditionalProbeId(i);
                try {
                    if (volume == null || volume.LightVolumeManager != manager) continue;
                    if (!Save3DTexturesProgressive(volume, additionalProbeId)) continue;
                    volume.InvBakedRotation = Quaternion.Inverse(LightVolumeTools.GetRotation(volume));
                    LVUtils.MarkDirty(volume);
                    LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
                } catch (Exception exception) {
                    Debug.LogError($"[LightVolumes] {exception}", volume);
                }
            }

            // Clear lifecycle state before post-processing or queuing editor work so re-entrant callbacks cannot apply the same non-idempotent probe additions twice.
            ResetUnityBakeState();

            if (lightProbesCommitted) {
                try {
                    PostProcessLightProbes(manager, false);
                } catch (Exception exception) {
                    Debug.LogError($"[LightVolumes] {exception}", manager);
                }
            }
            FinalizeManager(manager);
            Debug.Log("[LightVolumes] Progressive Light Volume atlas generation queued.");
        }

        // Unregisters every temporary Progressive probe group still owned by this bake.
        private static void CleanupProgressiveProbeRegistrations() {
            for (int i = 0; i < _progressiveVolumes.Count; i++) {
                try {
                    RemoveAdditionalProbes(GetAdditionalProbeId(i));
                } catch (Exception exception) {
                    Debug.LogError($"[LightVolumes] {exception}");
                }
            }
            _progressiveVolumes.Clear();
            try {
                RemoveAdditionalProbes(ProgressiveCompletionProbeId);
            } catch (Exception exception) {
                Debug.LogError($"[LightVolumes] {exception}");
            }
        }

        // Cancels the active Progressive bake and removes every temporary additional-probe registration.
        private static void ResetUnityBakeState() {
            CleanupProgressiveProbeRegistrations();
            _unityManager = null;
            _unityInitialLightProbes = null;
            _unityInitialLightProbesDirtyCount = -1;
        }

        // Confirms that Unity committed the additional-probe stage rather than canceling before it.
        private static bool HasProgressiveCompletionProbeResult() {
            using (NativeArray<SphericalHarmonicsL2> probes = new NativeArray<SphericalHarmonicsL2>(1, Allocator.Temp))
            using (NativeArray<float> validity = new NativeArray<float>(1, Allocator.Temp)) {
#pragma warning disable CS0618
                return UnityEditor.Experimental.Lightmapping.GetAdditionalBakedProbes(ProgressiveCompletionProbeId, probes, validity);
#pragma warning restore CS0618
            }
        }

        // Selected/lightmap-only jobs and late cancellation can finish additional probes without replacing classic Light Probes. Only a serialized probe change authorizes SH additions.
        private static bool HaveUnityLightProbesChanged() {
            LightProbes probes = LightmapSettings.lightProbes;
            if (probes == null) return false;
            return probes != _unityInitialLightProbes
                || EditorUtility.GetDirtyCount(probes) != _unityInitialLightProbesDirtyCount;
        }

        // Checks Manager ownership, bake state and scene eligibility for one Light Volume.
        private static bool IsBakeVolume(LightVolumeManager manager, LightVolumeInstance volume) {
            return volume != null && volume.LightVolumeManager == manager && volume.Bake && volume.gameObject.activeInHierarchy && !volume.CompareTag("EditorOnly");
        }

        // Maps stable list order to this package's additional-probe ID namespace.
        private static int GetAdditionalProbeId(int index) {
            return AdditionalProbeIdStart + index;
        }

        // Queues shadow baking and atlas packing for the completed Manager.
        private static void FinalizeManager(LightVolumeManager manager) {
            if (manager == null) return;
            LightVolumeManagerEditorBackend.BakeShadowMaps(manager);
            LightVolumeManagerEditorBackend.QueueAtlasGeneration(manager);
        }

        // Registers one additional probe group with Unity's Progressive lightmapper.
        private static void SetAdditionalProbes(int id, Vector3[] positions) {
#pragma warning disable CS0618
            UnityEditor.Experimental.Lightmapping.SetAdditionalBakedProbes(id, positions);
#pragma warning restore CS0618
        }

        // Registers one Light Volume's voxel centers with Unity's Progressive lightmapper.
        private static bool SetAdditionalProbes(LightVolumeInstance volume, int id) {
            if (volume == null) return false;
            LightVolumeTools.Recalculate(volume);
            if (!LightVolumeTools.TryCalculateProbePositions(volume, volume.Resolution, out Vector3[] positions)) return false;
            SetAdditionalProbes(id, positions);
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

            Texture3D texture0 = null;
            Texture3D texture1 = null;
            Texture3D texture2 = null;
            try {
                texture0 = CreateTexture(width, height, depth);
                texture1 = CreateTexture(width, height, depth);
                texture2 = CreateTexture(width, height, depth);
                if (!LVUtils.Apply3DTextureData(texture0, textureColors[0]) || !LVUtils.Apply3DTextureData(texture1, textureColors[1]) || !LVUtils.Apply3DTextureData(texture2, textureColors[2])) return false;

                string path = $"{Path.GetDirectoryName(scene.path)}/{scene.name}/VRCLightVolumes/Temp";
                string escapedName = LVUtils.EscapeFileName(volume.gameObject.name);
                bool saved0 = TrySaveTextureAsset(texture0, $"{path}/{escapedName}_0.asset");
                bool saved1 = TrySaveTextureAsset(texture1, $"{path}/{escapedName}_1.asset");
                bool saved2 = TrySaveTextureAsset(texture2, $"{path}/{escapedName}_2.asset");

                Texture3D previous0 = volume.Texture0;
                Texture3D previous1 = volume.Texture1;
                Texture3D previous2 = volume.Texture2;
                if (saved0) volume.Texture0 = texture0;
                if (saved1) volume.Texture1 = texture1;
                if (saved2) volume.Texture2 = texture2;
                DestroyReplacedTransientTexture(previous0, volume);
                DestroyReplacedTransientTexture(previous1, volume);
                DestroyReplacedTransientTexture(previous2, volume);

                if (!saved0 || !saved1 || !saved2) {
                    if (saved0 || saved1 || saved2) {
                        LVUtils.MarkDirty(volume);
                        LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
                    }
                    Debug.LogError($"[LightVolumes] Failed to persist every baked texture for light volume {volume.gameObject.name}. Transient texture objects were released.", volume);
                    return false;
                }

                LVUtils.MarkDirty(volume);
                return true;
            } finally {
                DestroyTransientTexture(texture0);
                DestroyTransientTexture(texture1);
                DestroyTransientTexture(texture2);
            }
        }

        // Confirms that the helper actually transferred ownership to the AssetDatabase; its public API logs and swallows CreateAsset failures, so returning from it is not sufficient.
        private static bool TrySaveTextureAsset(Texture3D texture, string path) {
            LVUtils.SaveAsAsset(texture, path);
            return texture != null && AssetDatabase.Contains(texture);
        }

        // Releases an overwritten in-memory bake while preserving imported assets and any texture still referenced by another SH channel.
        private static void DestroyReplacedTransientTexture(Texture3D texture, LightVolumeInstance volume) {
            if (texture == null || volume.Texture0 == texture || volume.Texture1 == texture || volume.Texture2 == texture) return;
            DestroyTransientTexture(texture);
        }

        // Destroys only textures that were not adopted by the AssetDatabase.
        private static void DestroyTransientTexture(Texture3D texture) {
            if (texture == null || AssetDatabase.Contains(texture)) return;
            UnityEngine.Object.DestroyImmediate(texture);
        }

        // Creates a clamp-wrapped half-float 3D texture suitable for baked SH coefficients.
        private static Texture3D CreateTexture(int width, int height, int depth) {
            Texture3D texture = new Texture3D(width, height, depth, TextureFormat.RGBAHalf, false);
            try {
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                return texture;
            } catch {
                DestroyTransientTexture(texture);
                throw;
            }
        }

        // Polls Bakery only for the lifetime of an active render or queued completion.
        private static void StartBakeryWatcher() {
            EditorApplication.update -= WatchBakeryBake;
            EditorApplication.update += WatchBakeryBake;
        }

        // Full-render lifecycle comes from Bakery's dedicated events; polling only applies live bitmasks and detects the terminal edge.
        private static void WatchBakeryBake() {
            bool baking = BakeryEditorBridge.IsBaking;
            if (baking) {
                if (_bakeryBitmaskPending) {
                    try {
                        TryApplyBakeryRuntimeBitmasks();
                    } catch (Exception exception) {
                        _bakeryBitmaskPending = false;
                        Debug.LogError($"[LightVolumes] Bakery bitmask overrides were disabled after an unexpected compatibility error. {exception}");
                    }
                }
                return;
            }
            if (_bakeryCompletionQueued) {
                CompleteBakeryBake();
                return;
            }
            if (!_bakeryFullRenderActive) return;
            // A confirmed success always arrives through OnFinishedFullRender. Any other falling edge is cancellation or an interrupted render and must not import stale output.
            CancelBakeryCompletion();
        }

        // Begins tracking only Bakery's full scene render, excluding probes, APV, reflection-only and selected-group operations that share bakeInProgress.
        private static void OnBakeryStarted(object sender, EventArgs args) {
            if (Application.isPlaying) return;
            try {
                BeginBakeryBake();
            } catch (Exception exception) {
                ResetBakeryBakeState();
                Debug.LogError($"[LightVolumes] Bakery start callback failed safely. {exception}");
            }
        }

        // Captures the primary Bakery Manager, prepares helpers and starts the global bitmask override.
        private static void BeginBakeryBake() {
            ResetBakeryBakeState();
            _bakeryManager = GetActiveManager(1);
            _bakeryFullRenderActive = _bakeryManager != null;
            if (!_bakeryFullRenderActive) return;
            StartBakeryWatcher();

            ConfigureExistingBakeryVolumes(_bakeryManager);
            _bakeryBitmaskPending = BakeryEditorBridge.SupportsRuntimeBitmasks;
            if (_bakeryBitmaskPending) {
                BakeryEditorBridge.ApplyStoredBitmasks(_bakeryManager.VolumeBitmask, _bakeryManager.ProbeBitmask);
                BakeryEditorBridge.ClearImplicitProbeGroups();
            }
        }

        // Routes both full renders and Bakery's L1/L2 probe-only command through one final SH pass.
        private static void OnBakeryFinished(object sender, EventArgs args) {
            if (Application.isPlaying) return;
            try {
                BakeryEditorBridge.ProbeRenderMode probeMode = BakeryEditorBridge.GetCompletedProbeRenderMode(sender);
                if (_bakeryFullRenderActive) {
                    _bakeryProbePostProcessPending |= probeMode != BakeryEditorBridge.ProbeRenderMode.None;
                    _bakeryL2ProbePostProcessPending |= probeMode == BakeryEditorBridge.ProbeRenderMode.L2;
                    QueueBakeryCompletion();
                    return;
                }
                if (!BakeryEditorBridge.IsProbeOnlyRender(sender) || probeMode == BakeryEditorBridge.ProbeRenderMode.None) return;
                _bakeryProbePostProcessPending = true;
                _bakeryL2ProbePostProcessPending = probeMode == BakeryEditorBridge.ProbeRenderMode.L2;
                QueueBakeryCompletion();
            } catch (Exception exception) {
                ResetBakeryBakeState();
                Debug.LogError($"[LightVolumes] Bakery completion callback failed safely. {exception}");
            }
        }

        // Bakery's Legacy probe renderer has a separate definitive completion event.
        private static void OnBakeryProbesFinished(object sender, EventArgs args) {
            if (Application.isPlaying) return;
            try {
                _bakeryProbePostProcessPending = true;
                _bakeryL2ProbePostProcessPending = false;
                QueueBakeryCompletion();
            } catch (Exception exception) {
                ResetBakeryBakeState();
                Debug.LogError($"[LightVolumes] Bakery Legacy probe callback failed safely. {exception}");
            }
        }

        // Coalesces Bakery completion callbacks into one delayed finalization.
        private static void QueueBakeryCompletion() {
            if (_bakeryCompletionQueued || (!_bakeryFullRenderActive && !_bakeryProbePostProcessPending)) return;
            _bakeryCompletionQueued = true;
            StartBakeryWatcher();
            EditorApplication.delayCall += CompleteBakeryBake;
        }

        // Applies the successful Bakery result after its outer render loop has fully stopped.
        private static void CompleteBakeryBake() {
            EditorApplication.delayCall -= CompleteBakeryBake;
            if (Application.isPlaying) {
                ResetBakeryBakeState();
                return;
            }
            if (!_bakeryFullRenderActive && !_bakeryProbePostProcessPending) {
                ResetBakeryBakeState();
                return;
            }
            // Bakery fires OnFinishedFullRender before its outer update clears bakeInProgress.
            if (BakeryEditorBridge.IsBaking) return;
            bool finalizeFullRender = _bakeryFullRenderActive;
            bool postProcessProbes = _bakeryProbePostProcessPending;
            bool l2ProbeResult = _bakeryL2ProbePostProcessPending;
            // Clear subscriptions and non-idempotent state before importing or post-processing.
            ResetBakeryBakeState();
            LightVolumeManager manager = ResolveBakeryCompletionManager();
            if (manager == null) return;

            LightVolumeInstance[] volumes = manager.LightVolumeInstances;
            if (finalizeFullRender && volumes != null) {
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
                        Debug.LogError($"[LightVolumes] {exception}", volume);
                    }
                }
            }

            if (postProcessProbes) {
                try {
                    PostProcessLightProbes(manager, manager.FixLightProbesL1, l2ProbeResult);
                } catch (Exception exception) {
                    Debug.LogError($"[LightVolumes] {exception}", manager);
                }
            }
            if (finalizeFullRender) {
                FinalizeManager(manager);
                Debug.Log("[LightVolumes] Bakery Light Volume atlas generation queued.");
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
        }

        // Clears transient Bakery state without treating an interrupted render as a successful bake.
        private static void ResetBakeryBakeState() {
            EditorApplication.delayCall -= CompleteBakeryBake;
            EditorApplication.update -= WatchBakeryBake;
            _bakeryCompletionQueued = false;
            _bakeryFullRenderActive = false;
            _bakeryBitmaskPending = false;
            _bakeryProbePostProcessPending = false;
            _bakeryL2ProbePostProcessPending = false;
            _bakeryManager = null;
        }

        // Bake callbacks are editor-only; clear transient registrations before crossing play-mode boundaries.
        private static void OnPlayModeStateChanged(PlayModeStateChange state) {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode) {
                ResetUnityBakeState();
                ResetBakeryBakeState();
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
        private static void PostProcessLightProbes(LightVolumeManager manager, bool dering, bool knownL2 = false) {
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

            bool didDering = ShouldDeringLightProbes(dering, knownL2, sh);
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
                    Debug.LogError($"[LightVolumes] {exception}", manager);
                }
            }
            if (!didDering && bakedPointLightCount == 0) return;

            probes.bakedProbes = sh;
            EditorUtility.SetDirty(probes);
            AssetDatabase.SaveAssetIfDirty(probes);
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
                if (manager.IsEditorProbeBakePointLight(pointLight)) return true;
            }
            return false;
        }

        // Detects Bakery L2 output even when its first probe is dark and has zero L2 coefficients.
        internal static bool HasL2ProbeData(SphericalHarmonicsL2[] probes) {
            if (probes == null) return false;
            for (int i = 0; i < probes.Length; i++) {
                if (LVUtils.CheckSHL2(probes[i])) return true;
            }
            return false;
        }

        // Bakery's declared mode is authoritative even when a valid L2 bake has zero higher-band coefficients.
        internal static bool ShouldDeringLightProbes(bool requested, bool knownL2, SphericalHarmonicsL2[] probes) {
            return requested && !knownL2 && !HasL2ProbeData(probes);
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
            Texture3D texture = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false);
            try {
                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.Apply(false, true);
                _probeBakeDummyVolumeTexture = texture;
                return texture;
            } catch {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }

        // Lazily creates the dummy texture array required for unused projection and shadow inputs.
        private static Texture2DArray GetProbeBakeDummyTextureArray() {
            if (_probeBakeDummyTextureArray != null) return _probeBakeDummyTextureArray;
            Texture2DArray texture = new Texture2DArray(1, 1, 1, TextureFormat.RGBA32, false);
            try {
                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.Apply(false, true);
                _probeBakeDummyTextureArray = texture;
                return texture;
            } catch {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
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
