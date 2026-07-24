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

        private sealed class ProgressiveVolumeBake {
            public LightVolumeManager Manager;
            public LightVolumeInstance Volume;
            public int AdditionalProbeId;
        }

        private static readonly List<ProgressiveVolumeBake> _progressiveVolumes = new List<ProgressiveVolumeBake>();
        private static LightVolumeManager[] _unityManagers = Array.Empty<LightVolumeManager>();
        private static int _nextAdditionalProbeId = AdditionalProbeIdStart;
        private static bool _unityBakeCompleted;
        private static bool _unityProbePostProcessAttempted;
        private static bool _unityManagersFinalized;
        private static bool _progressiveCleanupQueued;
        private static Texture3D _probeBakeDummyVolumeTexture;
        private static Texture2DArray _probeBakeDummyTextureArray;

#if BAKERY_INCLUDED
        private static readonly System.Reflection.FieldInfo _bakeryLightProbeGroupField = typeof(ftBuildGraphics).GetField("lightProbeLMGroup", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        private static readonly System.Reflection.FieldInfo _bakeryVolumeGroupField = typeof(ftBuildGraphics).GetField("volumeLMGroup", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        private static LightVolumeManager[] _bakeryManagers = Array.Empty<LightVolumeManager>();
        private static bool _bakeryWasBaking;
        private static bool _bakeryBitmaskPending;
        private static bool _bakeryProbePostProcessAttempted;
        private static bool _bakeryCompletionHandled;
        private static bool _bakeryBitmaskConflictWarned;
        private static bool _bakeryWatcherRefreshQueued;
        private static bool _bakeryWatcherSubscribed;
#endif

        static LightVolumeBaker() {
            Subscribe();
        }

        private static void Subscribe() {
#pragma warning disable CS0618
            UnityEditor.Experimental.Lightmapping.additionalBakedProbesCompleted -= OnAdditionalBakedProbesCompleted;
            UnityEditor.Experimental.Lightmapping.additionalBakedProbesCompleted += OnAdditionalBakedProbesCompleted;
#pragma warning restore CS0618
            Lightmapping.bakeStarted -= OnUnityBakeStarted;
            Lightmapping.bakeStarted += OnUnityBakeStarted;
            Lightmapping.bakeCompleted -= OnUnityBakeCompleted;
            Lightmapping.bakeCompleted += OnUnityBakeCompleted;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;

#if BAKERY_INCLUDED
            ftRenderLightmap.OnFinishedFullRender -= OnBakeryFinished;
            ftRenderLightmap.OnFinishedFullRender += OnBakeryFinished;
            EditorApplication.hierarchyChanged -= QueueBakeryWatcherRefresh;
            EditorApplication.hierarchyChanged += QueueBakeryWatcherRefresh;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            QueueBakeryWatcherRefresh();
#endif
        }

        private static void Shutdown() {
#pragma warning disable CS0618
            UnityEditor.Experimental.Lightmapping.additionalBakedProbesCompleted -= OnAdditionalBakedProbesCompleted;
#pragma warning restore CS0618
            Lightmapping.bakeStarted -= OnUnityBakeStarted;
            Lightmapping.bakeCompleted -= OnUnityBakeCompleted;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.delayCall -= CleanupProgressiveBakeAfterCallbacks;
            CleanupProgressiveProbeRegistrations();
            _unityManagers = Array.Empty<LightVolumeManager>();

#if BAKERY_INCLUDED
            ftRenderLightmap.OnFinishedFullRender -= OnBakeryFinished;
            EditorApplication.hierarchyChanged -= QueueBakeryWatcherRefresh;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorApplication.delayCall -= RefreshBakeryWatcher;
            EditorApplication.update -= WatchBakeryBake;
            _bakeryWatcherSubscribed = false;
            _bakeryManagers = Array.Empty<LightVolumeManager>();
#endif

            if (_probeBakeDummyVolumeTexture != null)
                UnityEngine.Object.DestroyImmediate(_probeBakeDummyVolumeTexture);
            if (_probeBakeDummyTextureArray != null)
                UnityEngine.Object.DestroyImmediate(_probeBakeDummyTextureArray);
            _probeBakeDummyVolumeTexture = null;
            _probeBakeDummyTextureArray = null;
        }

        private static void OnUnityBakeStarted() {
            if (Application.isPlaying) return;
            EditorApplication.delayCall -= CleanupProgressiveBakeAfterCallbacks;
            _progressiveCleanupQueued = false;
            CleanupProgressiveProbeRegistrations();

            _unityManagers = GetActiveManagers(0);
            _unityBakeCompleted = false;
            _unityProbePostProcessAttempted = false;
            _unityManagersFinalized = false;
            HashSet<LightVolumeInstance> registeredVolumes = new HashSet<LightVolumeInstance>();
            for (int managerIndex = 0; managerIndex < _unityManagers.Length; managerIndex++) {
                LightVolumeManager manager = _unityManagers[managerIndex];
                LightVolumeInstance[] volumes = manager.LightVolumeInstances;
                if (volumes == null) continue;
                for (int i = 0; i < volumes.Length; i++) {
                    LightVolumeInstance volume = volumes[i];
                    if (!IsBakeVolume(manager, volume) || !registeredVolumes.Add(volume)) continue;
                    if (LightVolumeTools.GetVoxelCount(volume) < 0) {
                        Debug.LogError($"[LightVolume] Can't add {volume.gameObject.name} to the Progressive bake. Resolution is invalid or the voxel count is too large!", volume);
                        continue;
                    }

                    int additionalProbeId = AllocateAdditionalProbeId();
                    try {
                        SetAdditionalProbes(volume, additionalProbeId);
                        _progressiveVolumes.Add(new ProgressiveVolumeBake {
                            Manager = manager,
                            Volume = volume,
                            AdditionalProbeId = additionalProbeId
                        });
                        Debug.Log($"[LightVolume] Added Progressive probes for \"{volume.gameObject.name}\" (group {additionalProbeId}).", volume);
                    } catch (Exception exception) {
                        RemoveAdditionalProbes(additionalProbeId);
                        Debug.LogException(exception, volume);
                    }
                }
            }
        }

        private static void OnAdditionalBakedProbesCompleted() {
            if (Application.isPlaying || _unityManagers.Length == 0) return;
            EditorApplication.delayCall -= CleanupProgressiveBakeAfterCallbacks;
            _progressiveCleanupQueued = false;

            for (int i = 0; i < _progressiveVolumes.Count; i++) {
                ProgressiveVolumeBake bake = _progressiveVolumes[i];
                try {
                    LightVolumeInstance volume = bake.Volume;
                    if (volume == null || bake.Manager == null) continue;
                    Save3DTexturesProgressive(volume, bake.AdditionalProbeId);
                    volume.InvBakedRotation = Quaternion.Inverse(LightVolumeTools.GetRotation(volume));
                    LVUtils.MarkDirty(volume);
                    LightVolumeManagerTools.CopyProxyToUdon(volume);
                } catch (Exception exception) {
                    Debug.LogException(exception, bake.Volume);
                } finally {
                    RemoveAdditionalProbes(bake.AdditionalProbeId);
                }
            }
            _progressiveVolumes.Clear();

            PostProcessUnityProbesOnce();
            FinalizeUnityManagersOnce();
            if (_unityBakeCompleted) _unityManagers = Array.Empty<LightVolumeManager>();
        }

        private static void OnUnityBakeCompleted() {
            if (Application.isPlaying || _unityManagers.Length == 0) return;
            _unityBakeCompleted = true;
            PostProcessUnityProbesOnce();
            if (_progressiveVolumes.Count == 0) {
                FinalizeUnityManagersOnce();
                _unityManagers = Array.Empty<LightVolumeManager>();
                return;
            }

            // Unity normally invokes additionalBakedProbesCompleted first. Deferring cleanup by one
            // editor tick also handles versions that invoke bakeCompleted first in the same cycle.
            if (_progressiveCleanupQueued) return;
            _progressiveCleanupQueued = true;
            EditorApplication.delayCall += CleanupProgressiveBakeAfterCallbacks;
        }

        private static void CleanupProgressiveBakeAfterCallbacks() {
            EditorApplication.delayCall -= CleanupProgressiveBakeAfterCallbacks;
            _progressiveCleanupQueued = false;
            if (_progressiveVolumes.Count > 0) {
                Debug.LogWarning("[LightVolume] Progressive baking completed without an additional-probe result. Temporary probe registrations were removed; no atlas was generated from them.");
                CleanupProgressiveProbeRegistrations();
            }
            _unityManagers = Array.Empty<LightVolumeManager>();
        }

        private static void CleanupProgressiveProbeRegistrations() {
            for (int i = 0; i < _progressiveVolumes.Count; i++) {
                try {
                    RemoveAdditionalProbes(_progressiveVolumes[i].AdditionalProbeId);
                } catch (Exception exception) {
                    Debug.LogException(exception);
                }
            }
            _progressiveVolumes.Clear();
        }

        private static void PostProcessUnityProbesOnce() {
            if (_unityProbePostProcessAttempted) return;
            _unityProbePostProcessAttempted = true;
            PostProcessLightProbes(_unityManagers, false);
        }

        private static bool IsBakeVolume(LightVolumeManager manager, LightVolumeInstance volume) {
            return volume != null && volume.LightVolumeManager == manager && volume.Bake && volume.gameObject.activeInHierarchy && !volume.CompareTag("EditorOnly");
        }

        private static int AllocateAdditionalProbeId() {
            if (_nextAdditionalProbeId == int.MaxValue)
                _nextAdditionalProbeId = AdditionalProbeIdStart;
            return _nextAdditionalProbeId++;
        }

        private static void FinalizeUnityManagersOnce() {
            if (_unityManagersFinalized) return;
            _unityManagersFinalized = true;
            FinalizeManagers(_unityManagers);
            Debug.Log("[LightVolume] Progressive Light Volume atlas generation queued.");
        }

        private static void FinalizeManagers(LightVolumeManager[] managers) {
            for (int i = 0; i < managers.Length; i++) {
                LightVolumeManager manager = managers[i];
                if (manager == null) continue;
                LightVolumeManagerTools.BakeShadowMaps(manager);
                LightVolumeManagerTools.GenerateAtlas(manager);
            }
        }

        private static void SetAdditionalProbes(LightVolumeInstance volume, int id) {
            if (volume == null) return;
            LightVolumeTools.Recalculate(volume);
            if (!LightVolumeTools.TryCalculateProbePositions(volume, volume.Resolution, out Vector3[] positions)) return;
#pragma warning disable CS0618
            UnityEditor.Experimental.Lightmapping.SetAdditionalBakedProbes(id, positions);
#pragma warning restore CS0618
        }

        private static void RemoveAdditionalProbes(int id) {
#pragma warning disable CS0618
            UnityEditor.Experimental.Lightmapping.SetAdditionalBakedProbes(id, new Vector3[0]);
#pragma warning restore CS0618
        }

        private static void Save3DTexturesProgressive(LightVolumeInstance volume, int id) {
            if (volume == null) return;

            int voxelCount = LightVolumeTools.GetVoxelCount(volume);
            if (voxelCount < 0) {
                Debug.LogError($"[LightVolume] Can't save light volume {volume.gameObject.name} 3D texture. Resolution is invalid or the voxel count is too large!", volume);
                return;
            }

            LightVolumeManager manager = volume.LightVolumeManager;
            if (manager == null) return;

            using (NativeArray<SphericalHarmonicsL2> probes = new NativeArray<SphericalHarmonicsL2>(voxelCount, Allocator.Temp))
            using (NativeArray<float> validity = new NativeArray<float>(voxelCount, Allocator.Temp)) {
#pragma warning disable CS0618
                if (!UnityEditor.Experimental.Lightmapping.GetAdditionalBakedProbes(id, probes, validity)) {
                    Debug.LogError("[LightVolume] Can't grab light volume data. No additional baked probes found!", volume);
                    return;
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
                SaveCustomProbesBaked(volume, l0, l1r, l1g, l1b, probeValidity, manager.Denoise);
            }
        }

        internal static bool SaveCustomProbesBaked(LightVolumeInstance volume, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity, bool denoise) {
            if (volume == null || volume.LightVolumeManager == null) return false;

            LightVolumeManager manager = volume.LightVolumeManager;
            int width = volume.Resolution.x;
            int height = volume.Resolution.y;
            int depth = volume.Resolution.z;
            if (!LVUtils.TryPrepareLightVolumeProbeData(l0, l1r, l1g, l1b, validity, width, height, depth, manager.DilationIterations, manager.DilationBackfaceBias, denoise, out Color[][] textureColors, out string error)) {
                Debug.LogError($"[LightVolume] Can't save custom bake for light volume {volume.gameObject.name}. {error}", volume);
                return false;
            }

            Scene scene = volume.gameObject.scene;
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) {
                Debug.LogError($"[LightVolume] Can't save custom bake for light volume {volume.gameObject.name}. Save the containing scene first!", volume);
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

        private static Texture3D CreateTexture(int width, int height, int depth) {
            return new Texture3D(width, height, depth, TextureFormat.RGBAHalf, false) {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }

#if BAKERY_INCLUDED
        internal static void QueueBakeryWatcherRefresh() {
            if (_bakeryWatcherRefreshQueued) return;
            _bakeryWatcherRefreshQueued = true;
            EditorApplication.delayCall += RefreshBakeryWatcher;
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode) {
            QueueBakeryWatcherRefresh();
        }

        private static void OnSceneClosed(Scene scene) {
            QueueBakeryWatcherRefresh();
        }

        private static void RefreshBakeryWatcher() {
            EditorApplication.delayCall -= RefreshBakeryWatcher;
            _bakeryWatcherRefreshQueued = false;
            bool required = GetActiveManagers(1).Length > 0 || _bakeryWasBaking;
            if (required == _bakeryWatcherSubscribed) return;
            EditorApplication.update -= WatchBakeryBake;
            if (required) EditorApplication.update += WatchBakeryBake;
            _bakeryWatcherSubscribed = required;
        }

        // This is the only per-frame editor callback. It performs one Bakery state read and scans
        // scene components only on the transition into a bake.
        private static void WatchBakeryBake() {
            bool baking = ftRenderLightmap.bakeInProgress;
            if (baking && !_bakeryWasBaking) BeginBakeryBake();
            if (baking && _bakeryBitmaskPending) TryApplyBakeryRuntimeBitmasks();
            _bakeryWasBaking = baking;
        }

        private static void BeginBakeryBake() {
            _bakeryManagers = GetActiveManagers(1);
            _bakeryProbePostProcessAttempted = false;
            _bakeryCompletionHandled = false;
            _bakeryBitmaskConflictWarned = false;
            _bakeryBitmaskPending = false;
            if (_bakeryManagers.Length == 0) return;

            ResolveBakeryBitmasks(_bakeryManagers, out int volumeBitmask, out int probeBitmask);
            ConfigureExistingBakeryVolumes(_bakeryManagers);
            BeginBakeryBitmaskOverride(volumeBitmask, probeBitmask);
            _bakeryBitmaskPending = true;
        }

        private static void OnBakeryFinished(object sender, EventArgs args) {
            if (Application.isPlaying || _bakeryCompletionHandled) return;
            _bakeryCompletionHandled = true;
            LightVolumeManager[] managers = _bakeryManagers.Length > 0 ? _bakeryManagers : GetActiveManagers(1);
            _bakeryBitmaskPending = false;
            _bakeryWasBaking = false;
            if (managers.Length == 0) {
                _bakeryManagers = Array.Empty<LightVolumeManager>();
                QueueBakeryWatcherRefresh();
                return;
            }

            for (int managerIndex = 0; managerIndex < managers.Length; managerIndex++) {
                LightVolumeManager manager = managers[managerIndex];
                LightVolumeInstance[] volumes = manager.LightVolumeInstances;
                if (volumes == null) continue;
                for (int i = 0; i < volumes.Length; i++) {
                    LightVolumeInstance volume = volumes[i];
                    if (!IsBakeVolume(manager, volume)) continue;
                    LightVolumeTools.Recalculate(volume);
                    volume.InvBakedRotation = Quaternion.Inverse(LightVolumeTools.GetRotation(volume));
                    LightVolumeTools.TryImportBakeryTextures(volume);
                    LVUtils.MarkDirty(volume);
                    LightVolumeManagerTools.CopyProxyToUdon(volume);
                }
            }

            if (!_bakeryProbePostProcessAttempted) {
                _bakeryProbePostProcessAttempted = true;
                bool dering = false;
                for (int i = 0; i < managers.Length; i++)
                    dering |= managers[i] != null && managers[i].FixLightProbesL1;
                PostProcessLightProbes(managers, dering);
            }
            FinalizeManagers(managers);
            Debug.Log("[LightVolume] Bakery Light Volume atlas generation queued.");
            _bakeryManagers = Array.Empty<LightVolumeManager>();
            QueueBakeryWatcherRefresh();
        }

        private static void ConfigureExistingBakeryVolumes(LightVolumeManager[] managers) {
            for (int managerIndex = 0; managerIndex < managers.Length; managerIndex++) {
                LightVolumeManager manager = managers[managerIndex];
                LightVolumeInstance[] volumes = manager.LightVolumeInstances;
                if (volumes == null) continue;
                for (int i = 0; i < volumes.Length; i++) {
                    LightVolumeInstance volume = volumes[i];
                    if (!IsBakeVolume(manager, volume)) continue;
                    LightVolumeTools.SetupBakeryDependencies(volume, false);
                }
            }
        }

        private static void ResolveBakeryBitmasks(LightVolumeManager[] managers, out int volumeBitmask, out int probeBitmask) {
            LightVolumeManager selected = managers[0];
            volumeBitmask = selected.VolumeBitmask;
            probeBitmask = selected.ProbeBitmask;
            bool conflict = false;
            for (int i = 1; i < managers.Length; i++) {
                LightVolumeManager manager = managers[i];
                if (manager.VolumeBitmask != volumeBitmask || manager.ProbeBitmask != probeBitmask) conflict = true;
            }
            if (!conflict || _bakeryBitmaskConflictWarned) return;
            _bakeryBitmaskConflictWarned = true;

            Debug.LogWarning($"[LightVolume] Active Bakery managers use different global bitmasks. Using Volume={volumeBitmask}, Probes={probeBitmask} from \"{GetManagerSortKey(selected)}\" for this bake.", selected);
        }

        private static void TryApplyBakeryRuntimeBitmasks() {
            if (_bakeryManagers.Length == 0) {
                _bakeryBitmaskPending = false;
                return;
            }
            ResolveBakeryBitmasks(_bakeryManagers, out int volumeBitmask, out int probeBitmask);
            if (!TryApplyBakeryBitmaskOverride(volumeBitmask, probeBitmask)) return;
            _bakeryBitmaskPending = false;
        }

        private static void BeginBakeryBitmaskOverride(int volumeBitmask, int probeBitmask) {
            ApplyBakeryBitmasksToStoredGroups(volumeBitmask, probeBitmask);
            _bakeryLightProbeGroupField?.SetValue(null, null);
            _bakeryVolumeGroupField?.SetValue(null, null);
        }

        private static bool TryApplyBakeryBitmaskOverride(int volumeBitmask, int probeBitmask) {
            BakeryLightmapGroup lightProbeGroup = _bakeryLightProbeGroupField?.GetValue(null) as BakeryLightmapGroup;
            BakeryLightmapGroup volumeGroup = _bakeryVolumeGroupField?.GetValue(null) as BakeryLightmapGroup;
            if (lightProbeGroup == null && volumeGroup == null) return false;
            if (lightProbeGroup != null) lightProbeGroup.bitmask = probeBitmask;
            if (volumeGroup != null) volumeGroup.bitmask = volumeBitmask;
            ApplyBakeryBitmasksToStoredGroups(volumeBitmask, probeBitmask);
            return true;
        }

        private static void ApplyBakeryBitmasksToStoredGroups(int volumeBitmask, int probeBitmask) {
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                GameObject storageObject = ftLightmaps.FindInScene("!ftraceLightmaps", scene);
                if (storageObject == null) continue;
                ftLightmapsStorage storage = storageObject.GetComponent<ftLightmapsStorage>();
                if (storage == null || storage.implicitGroups == null) continue;
                for (int j = 0; j < storage.implicitGroups.Count; j++) {
                    BakeryLightmapGroup group = storage.implicitGroups[j] as BakeryLightmapGroup;
                    if (group == null || !group.isImplicit || !group.probes) continue;
                    group.bitmask = group.name == "volumes" ? volumeBitmask : probeBitmask;
                }
            }
        }
#endif

        private static bool PostProcessLightProbes(LightVolumeManager[] managers, bool dering) {
            bool bakePointLights = HasProbeBakedPointLights(managers);
            if (!dering && !bakePointLights) return false;

            LightProbes probes = LightmapSettings.lightProbes;
            if (probes == null || probes.count == 0) {
                Debug.LogWarning("[LightVolume] No Light Probes found to postprocess.");
                return false;
            }
            SphericalHarmonicsL2[] sh = probes.bakedProbes;
            Vector3[] positions = probes.positions;
            if (sh == null || sh.Length == 0 || positions == null) return false;

            bool didDering = dering && !LVUtils.CheckSHL2(sh[0]);
            if (dering && !didDering)
                Debug.Log("[LightVolume] L2 Light Probes detected; Bakery L1 fix was skipped.");
            if (didDering) {
                for (int i = 0; i < sh.Length; i++) sh[i] = LVUtils.DeringSH(sh[i]);
            }

            int bakedPointLightCount = 0;
            if (bakePointLights) {
                int remainingLightCapacity = MaxProbeBakedPointLightCount;
                for (int i = 0; i < managers.Length; i++) {
                    LightVolumeManager manager = managers[i];
                    if (manager == null) continue;
                    try {
                        int managerLightCount = BakePointLightsIntoProbes(manager, sh, positions, remainingLightCapacity);
                        bakedPointLightCount += managerLightCount;
                        remainingLightCapacity -= managerLightCount;
                    } catch (Exception exception) {
                        Debug.LogException(exception, manager);
                    }
                }
            }
            if (!didDering && bakedPointLightCount == 0) return false;

            probes.bakedProbes = sh;
            EditorUtility.SetDirty(probes);
            AssetDatabase.SaveAssets();
            string deringLog = didDering ? $"{sh.Length} Light Probes fixed" : "";
            string pointLightLog = bakedPointLightCount > 0 ? $"{bakedPointLightCount} Point Light Volumes baked into Light Probes" : "";
            Debug.Log($"[LightVolume] {deringLog}{(didDering && bakedPointLightCount > 0 ? ", " : "")}{pointLightLog}.");
            return true;
        }

        private static bool HasProbeBakedPointLights(LightVolumeManager[] managers) {
            for (int managerIndex = 0; managerIndex < managers.Length; managerIndex++) {
                LightVolumeManager manager = managers[managerIndex];
                if (manager == null || manager.PointLightVolumeInstances == null) continue;
                PointLightVolumeInstance[] pointLights = manager.PointLightVolumeInstances;
                for (int i = 0; i < pointLights.Length; i++) {
                    PointLightVolumeInstance pointLight = pointLights[i];
                    if (pointLight != null && pointLight.LightVolumeManager == manager && pointLight.BakeIntoProbes && pointLight.isActiveAndEnabled && !pointLight.CompareTag("EditorOnly") && pointLight.Intensity != 0f && pointLight.Color != Color.black) return true;
                }
            }
            return false;
        }

        private static int BakePointLightsIntoProbes(LightVolumeManager manager, SphericalHarmonicsL2[] sh, Vector3[] probePositions, int lightCapacity) {
            int probeCount = Mathf.Min(sh.Length, probePositions.Length);
            if (probeCount == 0) return 0;
            if (!SystemInfo.supportsComputeShaders) {
                Debug.LogError("[LightVolume] Compute shaders are unavailable. Point Light Volumes were not baked into Light Probes.", manager);
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
                Debug.LogWarning($"[LightVolume] Skipped {missingProjectionCount} Point Light Volumes because their projection texture is unavailable in the manager array.", manager);
            }
            if (overflowCount > 0) {
                Debug.LogWarning($"[LightVolume] Skipped {overflowCount} Point Light Volumes. Probe baking supports at most {MaxProbeBakedPointLightCount} active lights across all managers.", manager);
            }
            if (pointLightCount == 0) return 0;

            ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ProbeBakeComputePath);
            if (compute == null || !compute.HasKernel(ProbeBakeKernelName)) {
                Debug.LogError($"[LightVolume] Missing or invalid probe bake compute shader at {ProbeBakeComputePath}.", manager);
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

        private static Texture GetProbeBakeCustomTexture(LightVolumeManager manager) {
            RenderTexture texture = manager.CustomTextures;
            return texture != null && texture.volumeDepth > 0 && texture.IsCreated() ? texture : GetProbeBakeDummyTextureArray();
        }

        private static Texture3D GetProbeBakeDummyVolumeTexture() {
            if (_probeBakeDummyVolumeTexture != null)
                return _probeBakeDummyVolumeTexture;
            _probeBakeDummyVolumeTexture = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            _probeBakeDummyVolumeTexture.Apply(false, true);
            return _probeBakeDummyVolumeTexture;
        }

        private static Texture2DArray GetProbeBakeDummyTextureArray() {
            if (_probeBakeDummyTextureArray != null)
                return _probeBakeDummyTextureArray;
            _probeBakeDummyTextureArray = new Texture2DArray(1, 1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            _probeBakeDummyTextureArray.Apply(false, true);
            return _probeBakeDummyTextureArray;
        }

        private static void PackProbeSH(SphericalHarmonicsL2[] sh, Vector4[] packed, int count) {
            for (int i = 0; i < count; i++) {
                int index = i * 3;
                packed[index] = new Vector4(sh[i][0, 3], sh[i][0, 1], sh[i][0, 2], sh[i][0, 0]);
                packed[index + 1] = new Vector4(sh[i][1, 3], sh[i][1, 1], sh[i][1, 2], sh[i][1, 0]);
                packed[index + 2] = new Vector4(sh[i][2, 3], sh[i][2, 1], sh[i][2, 2], sh[i][2, 0]);
            }
        }

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

        private static LightVolumeManager[] GetActiveManagers(int bakingMode) {
            LightVolumeManager[] all = Resources.FindObjectsOfTypeAll<LightVolumeManager>();
            List<LightVolumeManager> result = new List<LightVolumeManager>();
            for (int i = 0; i < all.Length; i++) {
                LightVolumeManager manager = all[i];
                if (manager == null || manager.BakingMode != bakingMode || !manager.enabled || !manager.gameObject.activeInHierarchy || manager.CompareTag("EditorOnly") || LVUtils.IsInPrefabAsset(manager)) continue;
                Scene scene = manager.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded) continue;
                result.Add(manager);
            }
            LightVolumeManager[] managers = result.ToArray();
            Array.Sort(managers, CompareManagers);
            return managers;
        }

        private static int CompareManagers(LightVolumeManager first, LightVolumeManager second) {
            return string.CompareOrdinal(GetManagerSortKey(first), GetManagerSortKey(second));
        }

        private static string GetManagerSortKey(LightVolumeManager manager) {
            if (manager == null) return string.Empty;
            Transform current = manager.transform;
            string hierarchy = string.Empty;
            while (current != null) {
                hierarchy = $"/{current.GetSiblingIndex():D6}:{current.name}{hierarchy}";
                current = current.parent;
            }
            return $"{manager.gameObject.scene.path}{hierarchy}";
        }
    }
}
