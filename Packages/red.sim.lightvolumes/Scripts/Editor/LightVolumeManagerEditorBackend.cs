using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UDONSHARP
using UdonSharp;
using UdonSharp.Serialization;
using UdonSharpEditor;
using VRC.Udon;
#endif

namespace VRCLightVolumes {

    // Internal editor backend for the unified Udon manager. Public integrations enter through the Editor facade or the removable Legacy compatibility files.
    internal static class LightVolumeManagerEditorBackend {
        private const float ShadowMinVarianceValueMin = 0.0001f;
        private const float ShadowMinVarianceValueMax = 1f;

        private static EditorCoroutine _atlasCoroutine;
        private static IEnumerator _atlasRoutine;
        private static LightVolumeManager _atlasCoroutineOwner;
        private static int _atlasGenerationVersion;
        private static Texture3D _ownedTransientAtlas;
        private static LightVolumeManager _ownedTransientAtlasOwner;
        private static bool _customProbeFinalizeQueued;
        private static bool _atlasGenerationQueued;
#if UDONSHARP
        private static bool _queuedRuntimeCustomTextureReinitialization;
        private static bool _queuedRuntimeShadowTextureReinitialization;
        private static bool _queuedRuntimeManagerUpdate;
        private static bool _queuedRuntimeSettingsApply;
        private static readonly List<int> _queuedRuntimeShadowBakeInstanceIds = new List<int>();
        private static bool _runtimeInspectorFlushScheduled;
#endif

        // Owns deferred atlas work and transient textures across editor lifecycle boundaries.
        static LightVolumeManagerEditorBackend() {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.hierarchyChanged += CleanupDestroyedOwners;
            EditorSceneManager.sceneClosing += OnSceneClosing;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
        }

        // Cancels work that cannot safely cross into Play Mode when domain reload is disabled.
        private static void OnPlayModeStateChanged(PlayModeStateChange state) {
            if (state == PlayModeStateChange.ExitingEditMode) {
                CancelPendingAtlasWork();
                ReleaseOwnedTransientAtlas();
            }
#if UDONSHARP
            if (state == PlayModeStateChange.ExitingPlayMode) CancelRuntimeInspectorCommands();
#endif
        }

        // Deleting a Manager does not close its scene. Release generator state and a completed
        // unsaved atlas as soon as Unity publishes the corresponding hierarchy change.
        private static void CleanupDestroyedOwners() {
            if (_atlasCoroutine != null && _atlasCoroutineOwner == null) StopActiveAtlasCoroutine();
            if (!ReferenceEquals(_ownedTransientAtlas, null) && (_ownedTransientAtlas == null || _ownedTransientAtlasOwner == null)) ReleaseOwnedTransientAtlas();
        }

        // Stops work and releases a non-persistent atlas owned by the scene being unloaded.
        private static void OnSceneClosing(Scene scene, bool removingScene) {
            if (_atlasCoroutine != null && (_atlasCoroutineOwner == null || _atlasCoroutineOwner.gameObject.scene == scene)) StopActiveAtlasCoroutine();
            if (!ReferenceEquals(_ownedTransientAtlas, null) && (_ownedTransientAtlasOwner == null || _ownedTransientAtlasOwner.gameObject.scene == scene)) ReleaseOwnedTransientAtlas();
        }

        // Removes every deferred callback and native object owned by this editor domain.
        private static void Shutdown() {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.hierarchyChanged -= CleanupDestroyedOwners;
            EditorSceneManager.sceneClosing -= OnSceneClosing;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
            CancelPendingAtlasWork();
#if UDONSHARP
            CancelRuntimeInspectorCommands();
#endif
            ReleaseOwnedTransientAtlas();
        }

        // Returns the single Manager allowed to own global Light Volumes state. Invalid duplicate setups consistently use the first scene/hierarchy entry until the extras are removed.
        internal static LightVolumeManager GetPrimaryManager() {
            return GetPrimaryManager(out _);
        }

        // Returns the primary Manager and the total number of eligible Managers in loaded scenes.
        internal static LightVolumeManager GetPrimaryManager(out int managerCount) {
            ReleaseUnreferencedTransientAtlas();
            LightVolumeManager[] managers = UnityEngine.Object.FindObjectsByType<LightVolumeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            LightVolumeManager primary = null;
            string primaryKey = null;
            managerCount = 0;
            for (int i = 0; i < managers.Length; i++) {
                LightVolumeManager manager = managers[i];
                if (!IsLoadedManager(manager)) continue;
                managerCount++;
                if (primary == null) {
                    primary = manager;
                    continue;
                }
                if (primaryKey == null) primaryKey = GetManagerOrderKey(primary);
                string key = GetManagerOrderKey(manager);
                if (string.CompareOrdinal(key, primaryKey) >= 0) continue;
                primary = manager;
                primaryKey = key;
            }
            return primary;
        }

        // Excludes prefab/preview data and EditorOnly fixtures from the global Manager invariant.
        private static bool IsLoadedManager(LightVolumeManager manager) {
            return manager != null && !manager.CompareTag("EditorOnly") && LightVolumeSceneSetup.IsMainStageSceneObject(manager.gameObject);
        }

        // Mirrors Unity's Hierarchy order: first loaded scene, then first hierarchy entry.
        private static string GetManagerOrderKey(LightVolumeManager manager) {
            Transform current = manager.transform;
            string hierarchy = string.Empty;
            while (current != null) {
                hierarchy = $"/{current.GetSiblingIndex():D6}{hierarchy}";
                current = current.parent;
            }
            Scene scene = manager.gameObject.scene;
            int sceneIndex = 0;
            while (sceneIndex < SceneManager.sceneCount && SceneManager.GetSceneAt(sceneIndex) != scene) sceneIndex++;
            return $"{sceneIndex:D6}" + hierarchy;
        }

        // Applies target-dependent authoring values and optional texture-cache rebuilds. Custom Inspectors can leave the final Play Mode proxy copy to UdonSharp's own wrapper.
        internal static void ApplySettings(LightVolumeManager manager, bool markDirty = true, bool reinitializeCustomTextures = false, bool reinitializeShadowTextures = false, bool updateVolumes = true, bool copyProxyToUdon = true) {
            if (manager == null) return;

            string previousState = markDirty ? LVUtils.GetSerializedState(manager) : null;
            bool mobileBuildTarget = IsMobileBuildTarget();
            manager.CustomTexturesWidth = Mathf.Clamp(manager.CustomTexturesWidth, 16, 2048);
            manager.CustomTexturesHeight = manager.CustomTexturesWidth;
            manager.ShadowTexturesWidth = Mathf.Clamp(manager.ShadowTexturesWidth, 16, 2048);
            manager.ShadowTexturesHeight = manager.ShadowTexturesWidth;
            manager.ShadowTextureFormat = mobileBuildTarget ? 0 : 1;
            float varianceSlider = mobileBuildTarget ? manager.ShadowMinVarianceMobile : manager.ShadowMinVarianceDesktop;
            manager.ShadowMinVariance = GetShadowMinVarianceValue(varianceSlider);
            manager.FroxelDensity = Mathf.Clamp(manager.FroxelDensity, 0.05f, 3f);
            manager.FroxelSlices = Mathf.Clamp(manager.FroxelSlices, 8, 256);
            manager.FroxelCoarse = ResolveCoarseFactor(manager.FroxelCoarse);
            manager.ClusteringMinLights = Mathf.Clamp(manager.ClusteringMinLights, 1, 128);
            manager.DilationIterations = Mathf.Clamp(manager.DilationIterations, 1, 8);
            manager.DilationBackfaceBias = Mathf.Clamp01(manager.DilationBackfaceBias);
            manager.AdditiveMaxOverdraw = Mathf.Max(manager.AdditiveMaxOverdraw, 1);
            manager.SanitizeRegistries();
            SynchronizeRegistryMetadata(manager);
            bool runtimeRefreshQueued = updateVolumes && QueueRuntimeManagerRefresh(manager, reinitializeCustomTextures, reinitializeShadowTextures);
            if (!runtimeRefreshQueued) {
                if (reinitializeCustomTextures) manager.ReinitializeCustomTextures();
                if (reinitializeShadowTextures) manager.ReinitializeShadowTextures();
            }
            // In Play Mode UdonSharp's wrapper can still target a storage adapter cached before
            // the live heap existed. Publish canonicalized Manager fields explicitly even when the
            // caller normally leaves the final copy to that wrapper.
            if (copyProxyToUdon || Application.isPlaying) CopyProxyToUdon(manager);
            if (markDirty) LVUtils.MarkDirtyIfSerializedStateChanged(manager, previousState);
            if (updateVolumes && !runtimeRefreshQueued && !reinitializeShadowTextures) manager.UpdateVolumes();
        }

        // Bakery helpers are created or removed only as a direct result of an explicit mode edit.
        internal static void HandleBakingModeChanged(LightVolumeManager manager, int previousBakingMode) {
            if (!BakeryEditorBridge.IsAvailable) return;
            if (manager == null || manager.BakingMode == previousBakingMode) return;
            LightVolumeInstance[] volumes = manager.LightVolumeInstances ?? Array.Empty<LightVolumeInstance>();
            bool createIfMissing = manager.BakingMode == 1;
            for (int i = 0; i < volumes.Length; i++) {
                LightVolumeInstance volume = volumes[i];
                if (volume == null || volume.LightVolumeManager != manager) continue;
                LightVolumeTools.SetupBakeryDependencies(volume, createIfMissing);
            }
            LightVolumeBaker.QueueBakeryWatcherRefresh();
        }

        // Synchronizes registry ownership and stable authoring order without rearranging the list.
        internal static void SynchronizeRegistryMetadata(LightVolumeManager manager) {
            if (manager == null) return;

            LightVolumeInstance[] volumes = manager.LightVolumeInstances;
            SynchronizeLightVolumeMetadata(manager, volumes);

            PointLightVolumeInstance[] pointLights = manager.PointLightVolumeInstances;
            for (int i = 0; i < pointLights.Length; i++) {
                PointLightVolumeInstance pointLight = pointLights[i];
                if (pointLight == null) continue;
                bool changed = false;
                if (pointLight.LightVolumeManager != manager) {
                    pointLight.LightVolumeManager = manager;
                    changed = true;
                }
                if (pointLight.RegistryOrder != i) {
                    pointLight.RegistryOrder = i;
                    changed = true;
                }
                if (!changed) continue;
                LVUtils.MarkDirty(pointLight);
                CopyProxyToUdon(pointLight);
            }
        }

        // Registers an authoring component without invoking its runtime OnEnable path. This is used both by hierarchy creation and by scene-instance prefab onboarding. Success and mutation are separate so callers cannot mistake an already-correct registration for a new change.
        internal static bool EnsureRegistered(LightVolumeManager manager, LightVolumeInstance volume, string undoName, out bool changed) {
            changed = false;
            if (manager == null || volume == null) return false;

            LightVolumeInstance[] volumes = manager.LightVolumeInstances ?? Array.Empty<LightVolumeInstance>();
            int index = Array.IndexOf(volumes, volume);
            bool managerChanged = false;
            if (index < 0) {
                Undo.RecordObject(manager, undoName);
                index = volumes.Length;
                Array.Resize(ref volumes, index + 1);
                volumes[index] = volume;
                manager.LightVolumeInstances = volumes;
                LVUtils.MarkDirty(manager);
                changed = true;
                managerChanged = true;
            }

            if (volume.LightVolumeManager != manager || volume.RegistryOrder != index) {
                Undo.RecordObject(volume, undoName);
                volume.LightVolumeManager = manager;
                volume.RegistryOrder = index;
                LVUtils.MarkDirty(volume);
                CopyProxyToUdon(volume);
                changed = true;
            }

            if (managerChanged) CopyProxyToUdon(manager);
            return true;
        }

        // Registers a Point Light Volume without invoking runtime lifecycle callbacks and reports actual mutation.
        internal static bool EnsureRegistered(LightVolumeManager manager, PointLightVolumeInstance pointLight, string undoName, out bool changed) {
            changed = false;
            if (manager == null || pointLight == null) return false;

            PointLightVolumeInstance[] pointLights = manager.PointLightVolumeInstances ?? Array.Empty<PointLightVolumeInstance>();
            int index = Array.IndexOf(pointLights, pointLight);
            bool managerChanged = false;
            if (index < 0) {
                Undo.RecordObject(manager, undoName);
                index = pointLights.Length;
                Array.Resize(ref pointLights, index + 1);
                pointLights[index] = pointLight;
                manager.PointLightVolumeInstances = pointLights;
                LVUtils.MarkDirty(manager);
                changed = true;
                managerChanged = true;
            }

            if (pointLight.LightVolumeManager != manager || pointLight.RegistryOrder != index) {
                Undo.RecordObject(pointLight, undoName);
                pointLight.LightVolumeManager = manager;
                pointLight.RegistryOrder = index;
                LVUtils.MarkDirty(pointLight);
                CopyProxyToUdon(pointLight);
                changed = true;
            }

            if (managerChanged) CopyProxyToUdon(manager);
            return true;
        }

        // Synchronizes Manager ownership and stable registry order for regular Light Volumes.
        private static void SynchronizeLightVolumeMetadata(LightVolumeManager manager, LightVolumeInstance[] volumes) {
            for (int i = 0; i < volumes.Length; i++) {
                LightVolumeInstance volume = volumes[i];
                if (volume == null) continue;
                bool changed = false;
                if (volume.LightVolumeManager != manager) {
                    volume.LightVolumeManager = manager;
                    changed = true;
                }
                if (volume.RegistryOrder != i) {
                    volume.RegistryOrder = i;
                    changed = true;
                }
                if (!changed) continue;
                LVUtils.MarkDirty(volume);
                CopyProxyToUdon(volume);
            }
        }

        // Explicit menu sorting is stable; automatic metadata sync never changes authoring order.
        private static bool SortLightVolumeRegistryByWeightAndResolution(LightVolumeInstance[] volumes) {
            bool changed = false;
            for (int i = 1; i < volumes.Length; i++) {
                LightVolumeInstance volume = volumes[i];
                int insertIndex = i;
                while (insertIndex > 0 && ComesBeforeByWeightAndResolution(volume, volumes[insertIndex - 1])) {
                    volumes[insertIndex] = volumes[insertIndex - 1];
                    insertIndex--;
                }
                if (insertIndex == i) continue;
                volumes[insertIndex] = volume;
                changed = true;
            }
            return changed;
        }

        // Compares two volumes by descending weight and then explicit resolution priority.
        private static bool ComesBeforeByWeightAndResolution(LightVolumeInstance volume, LightVolumeInstance previous) {
            if (volume == null) return false;
            if (previous == null) return true;
            if (volume.RegistryWeight != previous.RegistryWeight) return volume.RegistryWeight > previous.RegistryWeight;
            if (volume.AdaptiveResolution != previous.AdaptiveResolution) return !volume.AdaptiveResolution;
            return volume.AdaptiveResolution && volume.VoxelsPerUnit > previous.VoxelsPerUnit;
        }

        // Keeps authoring weights authoritative and only resolves equal-weight groups by resolution settings.
        internal static void SortLightVolumesByVoxelsPerUnit(LightVolumeManager manager) {
            if (manager == null) return;

            const string undoName = "Sort Light Volumes by Voxels Per Unit";
            Undo.RecordObject(manager, undoName);
            bool managerChanged = manager.SanitizeRegistries();

            LightVolumeInstance[] volumes = manager.LightVolumeInstances;
            if (volumes.Length > 0) Undo.RecordObjects(volumes, undoName);
            if (volumes.Length > 1)
                managerChanged |= SortLightVolumeRegistryByWeightAndResolution(volumes);
            SynchronizeLightVolumeMetadata(manager, volumes);

            if (!managerChanged) return;
            LVUtils.MarkDirty(manager);
            CopyProxyToUdon(manager);
            manager.UpdateVolumes();
        }

        // Rebuilds the Manager's shared projection texture array.
        internal static void ReinitializeCustomTextures(LightVolumeManager manager) {
            ReinitializeTextures(manager, true, false);
        }

        // Rebuilds the Manager's shared shadow texture array.
        internal static void ReinitializeShadowTextures(LightVolumeManager manager) {
            ReinitializeTextures(manager, false, true);
        }

        // Rebuilds selected runtime texture caches immediately or through the play-mode Udon queue.
        internal static void ReinitializeTextures(LightVolumeManager manager, bool customTextures, bool shadowTextures) {
            if (manager == null || !customTextures && !shadowTextures) return;
            if (QueueRuntimeManagerRefresh(manager, customTextures, shadowTextures)) return;
            if (customTextures) manager.ReinitializeCustomTextures();
            if (shadowTextures) manager.ReinitializeShadowTextures();
            CopyProxyToUdon(manager);
            if (!shadowTextures) manager.UpdateVolumes();
        }

        // Coalesces a burst of Inspector edits into one atlas pack without adding Update polling.
        internal static void QueueAtlasGeneration(LightVolumeManager manager) {
            if (manager == null || Application.isPlaying || _atlasGenerationQueued || manager != GetPrimaryManager()) return;
            _atlasGenerationQueued = true;
            EditorApplication.delayCall += GenerateQueuedAtlases;
        }

        // Packs the world's single primary Manager after the coalesced edit batch.
        private static void GenerateQueuedAtlases() {
            EditorApplication.delayCall -= GenerateQueuedAtlases;
            _atlasGenerationQueued = false;
            LightVolumeManager manager = GetPrimaryManager();
            if (manager != null && CanGenerateAtlas(manager)) GenerateAtlasCore(manager);
        }

        // Packs every explicitly registered Light Volume; no scene scanning or implicit component creation occurs.
        internal static void GenerateAtlas(LightVolumeManager manager) {
            if (manager == null || Application.isPlaying || manager != GetPrimaryManager()) return;
            GenerateAtlasCore(manager);
        }

        // Starts atlas generation for the accepted primary Manager.
        private static void GenerateAtlasCore(LightVolumeManager manager) {
            LightVolumeInstance[] volumes = GetAtlasVolumes(manager);
            if (volumes.Length == 0) return;

            StopActiveAtlasCoroutine();

            // Post-processed atlases are commonly updated slice by slice, so minimizing depth reduces per-frame draw calls even when that costs a little more VRAM.
            TexturePackingStrategy strategy = ResolveAtlasPackingStrategy(manager);
            int generationVersion = ++_atlasGenerationVersion;
            _atlasCoroutineOwner = manager;
            IEnumerator routine = RunAtlasGeneration(manager, volumes, strategy, generationVersion);
            _atlasRoutine = routine;
            EditorCoroutine coroutine = EditorCoroutineUtility.StartCoroutine(routine, manager);
            // Editor Coroutines 1.0.0 starts on the next update, but keep the assignment correct if a future implementation advances and completes the iterator inside StartCoroutine.
            if (ReferenceEquals(_atlasRoutine, routine)) _atlasCoroutine = coroutine;
        }

        // Wraps the generator so success, validation failure, exceptions and explicit cancellation all clear the static coroutine handle and dispose its iterator-owned resources.
        private static IEnumerator RunAtlasGeneration(LightVolumeManager manager, LightVolumeInstance[] volumes, TexturePackingStrategy strategy, int generationVersion) {
            IEnumerator generator = Texture3DAtlasGenerator.CreateAtlas(volumes, atlas => CompleteAtlas(manager, volumes, atlas), manager.DownscaleVolumes, strategy);
            try {
                while (generator.MoveNext()) yield return generator.Current;
            } finally {
                try {
                    (generator as IDisposable)?.Dispose();
                } finally {
                    if (_atlasGenerationVersion == generationVersion) {
                        _atlasCoroutine = null;
                        _atlasRoutine = null;
                        _atlasCoroutineOwner = null;
                    }
                }
            }
        }

        // Cancels queued finalization and the active generator without leaving static handles behind.
        private static void CancelPendingAtlasWork() {
            EditorApplication.delayCall -= GenerateQueuedAtlases;
            EditorApplication.delayCall -= FinalizeCustomProbeAtlases;
            _atlasGenerationQueued = false;
            _customProbeFinalizeQueued = false;
            StopActiveAtlasCoroutine();
        }

        // Invalidates the current generation before stopping it so its finally block cannot clear a subsequently-started coroutine handle.
        private static void StopActiveAtlasCoroutine() {
            _atlasGenerationVersion++;
            EditorCoroutine coroutine = _atlasCoroutine;
            IEnumerator routine = _atlasRoutine;
            _atlasCoroutine = null;
            _atlasRoutine = null;
            _atlasCoroutineOwner = null;
            try {
                if (coroutine != null) EditorCoroutineUtility.StopCoroutine(coroutine);
            } finally {
                // Editor Coroutines 1.0.0 does not dispose the stopped IEnumerator itself.
                (routine as IDisposable)?.Dispose();
            }
        }

        // Tracks an atlas that has no AssetDatabase owner so scene/domain teardown can destroy it.
        private static void TrackTransientAtlas(LightVolumeManager manager, Texture3D texture) {
            if (texture == null || AssetDatabase.Contains(texture)) return;
            if (manager == null) {
                DestroyTransientTexture(texture);
                return;
            }
            if (!ReferenceEquals(_ownedTransientAtlas, null) && !ReferenceEquals(_ownedTransientAtlas, texture)) ReleaseOwnedTransientAtlas();
            _ownedTransientAtlas = texture;
            _ownedTransientAtlasOwner = manager;
        }

        // Drops tracking once external editor code replaces both Manager references to an unsaved atlas; the backend is then the only remaining native-object owner.
        private static void ReleaseUnreferencedTransientAtlas() {
            if (ReferenceEquals(_ownedTransientAtlas, null)) return;
            if (_ownedTransientAtlas == null) {
                ReleaseOwnedTransientAtlas();
                return;
            }
            LightVolumeManager owner = _ownedTransientAtlasOwner;
            if (owner != null && (owner.LightVolumeAtlasBase == _ownedTransientAtlas || owner.LightVolumeAtlas == _ownedTransientAtlas)) return;
            ReleaseOwnedTransientAtlas();
        }

        // Relinquishes the one completed non-persistent atlas currently owned by the backend.
        private static void ReleaseOwnedTransientAtlas() {
            Texture3D texture = _ownedTransientAtlas;
            LightVolumeManager owner = _ownedTransientAtlasOwner;
            _ownedTransientAtlas = null;
            _ownedTransientAtlasOwner = null;
            if (texture == null || AssetDatabase.Contains(texture)) return;
            if (owner != null) {
                if (owner.LightVolumeAtlasBase == texture) owner.LightVolumeAtlasBase = null;
                if (owner.LightVolumeAtlas == texture) owner.LightVolumeAtlas = null;
            }
            UnityEngine.Object.DestroyImmediate(texture);
        }

        // Destroys only generated objects that were never adopted by the AssetDatabase.
        private static void DestroyTransientTexture(Texture3D texture) {
            if (texture == null || AssetDatabase.Contains(texture)) return;
            UnityEngine.Object.DestroyImmediate(texture);
        }

        // Post-processing is commonly slice-driven, so prefer minimum depth whenever a processor is present.
        private static TexturePackingStrategy ResolveAtlasPackingStrategy(LightVolumeManager manager) {
            return manager.EditorGetAtlasPostProcessors().Length > 0 ? TexturePackingStrategy.MinimumDepth : TexturePackingStrategy.MinimumVRAM;
        }

        // Returns a dense snapshot of non-null Light Volumes in registry order.
        private static LightVolumeInstance[] GetAtlasVolumes(LightVolumeManager manager) {
            LightVolumeInstance[] source = manager.LightVolumeInstances;
            int count = 0;
            for (int i = 0; i < source.Length; i++) if (source[i] != null) count++;
            LightVolumeInstance[] result = new LightVolumeInstance[count];
            for (int i = 0, write = 0; i < source.Length; i++) {
                if (source[i] == null) continue;
                result[write++] = source[i];
            }
            return result;
        }

        // Assigns a completed atlas, writes per-volume UVW bounds and schedules asset persistence.
        private static void CompleteAtlas(LightVolumeManager manager, LightVolumeInstance[] volumes, Atlas3D atlas) {
            if (atlas.Texture == null) return;
            Texture3D texture = atlas.Texture;
            if (manager == null || Application.isPlaying || manager != GetPrimaryManager() || volumes == null || atlas.BoundsUvwMin == null || atlas.BoundsUvwMax == null) {
                DestroyTransientTexture(texture);
                return;
            }

            Texture3D previousAtlas = manager.LightVolumeAtlasBase;
            bool ownedPreviousAtlas = ReferenceEquals(_ownedTransientAtlas, previousAtlas);
            if (!ReferenceEquals(_ownedTransientAtlas, null) && !ownedPreviousAtlas && !ReferenceEquals(_ownedTransientAtlas, texture)) ReleaseOwnedTransientAtlas();
            manager.LightVolumeAtlasBase = texture;
            _ownedTransientAtlas = texture;
            _ownedTransientAtlasOwner = manager;
            bool completionAccepted = false;
            try {
                int count = Mathf.Min(volumes.Length, Mathf.Min(atlas.BoundsUvwMin.Length / 3, atlas.BoundsUvwMax.Length / 3));
                for (int i = 0; i < count; i++) {
                    LightVolumeInstance volume = volumes[i];
                    if (volume == null) continue;
                    int atlasIndex = i * 3;
                    Vector3 scale = atlas.BoundsUvwMax[atlasIndex] - atlas.BoundsUvwMin[atlasIndex];
                    Vector3 uvw0 = atlas.BoundsUvwMin[atlasIndex];
                    Vector3 uvw1 = atlas.BoundsUvwMin[atlasIndex + 1];
                    Vector3 uvw2 = atlas.BoundsUvwMin[atlasIndex + 2];
                    volume.BoundsUvwMin0 = new Vector4(uvw0.x, uvw0.y, uvw0.z, scale.x);
                    volume.BoundsUvwMin1 = new Vector4(uvw1.x, uvw1.y, uvw1.z, scale.y);
                    volume.BoundsUvwMin2 = new Vector4(uvw2.x, uvw2.y, uvw2.z, scale.z);
                    if (!volume.Bake && volume.ReserveUVSpace) volume.InvBakedRotation = Quaternion.Inverse(LightVolumeTools.GetRotation(volume));
                    LVUtils.MarkDirty(volume);
                    CopyProxyToUdon(volume);
                }

                manager.EditorRefreshAtlasPostProcessors();
                Scene scene = manager.gameObject.scene;
                string scenePath = scene.path;
                if (!string.IsNullOrEmpty(scenePath)) {
                    string directory = Path.GetDirectoryName(scenePath);
                    LVUtils.SaveAsAssetDelayed(texture, $"{directory}/{scene.name}/VRCLightVolumes/LightVolumeAtlas.asset",
                        saved => OnAtlasPersistenceCompleted(manager, texture, previousAtlas, saved));
                }
                completionAccepted = true;
            } finally {
                if (completionAccepted) {
                    if (ownedPreviousAtlas && previousAtlas != texture) DestroyTransientTexture(previousAtlas);
                } else {
                    // Immediate callback failures roll back to the prior atlas and relinquish the
                    // newly-created native texture before the exception leaves the coroutine.
                    try {
                        if (manager != null) {
                            manager.LightVolumeAtlasBase = previousAtlas;
                            if (manager.LightVolumeAtlas == texture) manager.LightVolumeAtlas = previousAtlas;
                        }
                    } finally {
                        try {
                            if (ReferenceEquals(_ownedTransientAtlas, texture)) {
                                _ownedTransientAtlas = null;
                                _ownedTransientAtlasOwner = null;
                            }
                            DestroyTransientTexture(texture);
                        } finally {
                            if (ownedPreviousAtlas) TrackTransientAtlas(manager, previousAtlas);
                        }
                    }
                }
            }
        }

        // Finalizes delayed persistence. A failed CreateAsset operation must not leave the generated
        // native texture rooted by the Manager; a still-valid persistent previous atlas is restored.
        private static void OnAtlasPersistenceCompleted(LightVolumeManager manager, Texture3D texture, Texture3D previousAtlas, bool saved) {
            bool persisted = saved && texture != null && AssetDatabase.Contains(texture);
            if (persisted) {
                if (ReferenceEquals(_ownedTransientAtlas, texture)) {
                    _ownedTransientAtlas = null;
                    _ownedTransientAtlasOwner = null;
                }
                return;
            }

            Texture3D fallback = previousAtlas;
            try {
                if (manager != null && manager.LightVolumeAtlasBase == texture) {
                    manager.LightVolumeAtlasBase = fallback;
                    manager.EditorRefreshAtlasPostProcessors();
                }
            } finally {
                if (manager != null && manager.LightVolumeAtlas == texture) manager.LightVolumeAtlas = fallback;
                if (ReferenceEquals(_ownedTransientAtlas, texture)) {
                    _ownedTransientAtlas = null;
                    _ownedTransientAtlasOwner = null;
                }
                DestroyTransientTexture(texture);
            }
        }

        // Returns the number of active bake-enabled volumes exposed to a custom lightmapper.
        internal static int GetCustomProbesCount(LightVolumeManager manager) {
            if (manager == null || Application.isPlaying || manager != GetPrimaryManager()) return 0;
            int count = 0;
            LightVolumeInstance[] volumes = manager.LightVolumeInstances;
            for (int i = 0; i < volumes.Length; i++) if (IsCustomProbeVolume(volumes[i])) count++;
            return count;
        }

        // Returns world-space voxel probe positions for one custom-lightmapper volume ID.
        internal static Vector3[] GetCustomProbes(LightVolumeManager manager, int id) {
            LightVolumeInstance volume = GetCustomProbeVolume(manager, id);
            return volume != null ? LightVolumeTools.GetCustomProbes(volume) : Array.Empty<Vector3>();
        }

        // Stores custom-lightmapper SH data using Manager denoising and no validity channel.
        internal static void SetCustomProbesBaked(LightVolumeManager manager, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b) {
            SetCustomProbesBaked(manager, id, l0, l1r, l1g, l1b, null, manager != null && manager.Denoise);
        }

        // Stores custom-lightmapper SH data with an explicit denoising choice.
        internal static void SetCustomProbesBaked(LightVolumeManager manager, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, bool denoise) {
            SetCustomProbesBaked(manager, id, l0, l1r, l1g, l1b, null, denoise);
        }

        // Stores custom-lightmapper SH and validity data using the Manager's denoising setting.
        internal static void SetCustomProbesBaked(LightVolumeManager manager, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity) {
            SetCustomProbesBaked(manager, id, l0, l1r, l1g, l1b, validity, manager != null && manager.Denoise);
        }

        // Saves complete custom-lightmapper output and queues shadow and atlas finalization.
        internal static void SetCustomProbesBaked(LightVolumeManager manager, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity, bool denoise) {
            LightVolumeInstance volume = GetCustomProbeVolume(manager, id);
            if (volume == null || !LightVolumeBaker.SaveCustomProbesBaked(volume, l0, l1r, l1g, l1b, validity, denoise)) return;
            volume.InvBakedRotation = Quaternion.Inverse(LightVolumeTools.GetRotation(volume));
            LVUtils.MarkDirty(volume);
            CopyProxyToUdon(volume);
            QueueCustomProbeAtlasGeneration();
        }

        // Checks whether a volume participates in the custom lightmapper API.
        private static bool IsCustomProbeVolume(LightVolumeInstance volume) {
            return volume != null && volume.Bake && volume.gameObject.activeInHierarchy && !volume.CompareTag("EditorOnly");
        }

        // Resolves a compact custom-lightmapper ID to its registered Light Volume.
        private static LightVolumeInstance GetCustomProbeVolume(LightVolumeManager manager, int id) {
            if (manager == null || Application.isPlaying || manager != GetPrimaryManager()) return null;
            int customId = 0;
            LightVolumeInstance[] volumes = manager.LightVolumeInstances;
            for (int i = 0; i < volumes.Length; i++) {
                LightVolumeInstance volume = volumes[i];
                if (!IsCustomProbeVolume(volume)) continue;
                if (customId == id) return volume;
                customId++;
            }
            Debug.LogError($"[LightVolumes] Custom probe Light Volume ID {id} is invalid. Available volume count: {customId}.", manager);
            return null;
        }

        // Coalesces completed custom probe volumes into one deferred Manager finalization.
        private static void QueueCustomProbeAtlasGeneration() {
            if (_customProbeFinalizeQueued) return;
            _customProbeFinalizeQueued = true;
            EditorApplication.delayCall += FinalizeCustomProbeAtlases;
        }

        // Bakes eligible shadows and regenerates the primary atlas after custom probe output is complete.
        private static void FinalizeCustomProbeAtlases() {
            EditorApplication.delayCall -= FinalizeCustomProbeAtlases;
            _customProbeFinalizeQueued = false;
            LightVolumeManager manager = GetPrimaryManager();
            if (manager == null || Application.isPlaying || !CanGenerateAtlas(manager)) return;
            BakeShadowMapsCore(manager);
            GenerateAtlasCore(manager);
        }

        // Checks whether every non-reserved registered volume has all three baked textures.
        private static bool CanGenerateAtlas(LightVolumeManager manager) {
            LightVolumeInstance[] volumes = manager.LightVolumeInstances;
            if (volumes == null || volumes.Length == 0) return false;
            for (int i = 0; i < volumes.Length; i++) {
                LightVolumeInstance volume = volumes[i];
                if (volume == null) continue;
                if (!volume.Bake && volume.ReserveUVSpace) continue;
                if (volume.Texture0 == null || volume.Texture1 == null || volume.Texture2 == null) return false;
            }
            return true;
        }

        // Batch-bakes shadows marked for rebaking on the requested Manager.
        internal static void BakeShadowMaps(LightVolumeManager manager) {
            if (manager == null || manager != GetPrimaryManager()) return;
            if (Application.isPlaying) {
                BakeRuntimeShadowMaps(manager);
                return;
            }
            BakeShadowMapsCore(manager);
        }

        // Re-bakes eligible lights on their live UdonBehaviours. Play Mode must not run the
        // persistent editor baker on managed proxies because its temporary textures and private
        // bake state would be recursively serialized back over the running Udon graph.
        private static void BakeRuntimeShadowMaps(LightVolumeManager manager) {
            PointLightVolumeInstance[] pointLights = manager.PointLightVolumeInstances;
            if (pointLights == null) return;
            for (int i = 0; i < pointLights.Length; i++) {
                PointLightVolumeInstance pointLight = pointLights[i];
                if (pointLight == null || !pointLight.Shadows || !pointLight.RebakeShadows) continue;
                PointLightVolumeEditorUtility.Sync(pointLight, false, false);
                QueueRuntimeShadowBake(pointLight);
            }
        }

        // Bakes dirty shadows for the accepted primary Manager.
        private static void BakeShadowMapsCore(LightVolumeManager manager) {
            bool rebaked = false;
            bool synchronized = false;
            PointLightVolumeInstance[] pointLights = manager.PointLightVolumeInstances;
            for (int i = 0; i < pointLights.Length; i++) {
                PointLightVolumeInstance pointLight = pointLights[i];
                if (pointLight == null || !pointLight.Shadows || !pointLight.RebakeShadows) continue;
                PointLightVolumeEditorUtility.Sync(pointLight, false, false);
                synchronized = true;
                if (PointLightShadowBaker.BakeShadowMap(pointLight, $"| {pointLight.gameObject.name} ({i}/{pointLights.Length})", false)) rebaked = true;
            }
            if (rebaked) ReinitializeShadowTextures(manager);
            else if (synchronized) RefreshManagerOnce(manager, true);
            if (synchronized) LightVolumeEditorUpdater.QueueManagerRecovery();
        }

        // Converts the normalized inspector slider to a logarithmic EVSM variance value.
        internal static float GetShadowMinVarianceValue(float slider) {
            return ShadowMinVarianceValueMin * Mathf.Pow(ShadowMinVarianceValueMax / ShadowMinVarianceValueMin, Mathf.Clamp01(slider));
        }

        // Checks whether the active Unity build target uses mobile shadow defaults.
        internal static bool IsMobileBuildTarget() {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            return target == BuildTarget.Android || target == BuildTarget.iOS;
        }

        // Snaps a requested coarse clustering reduction to the supported factors 2, 4 or 8.
        internal static int ResolveCoarseFactor(int value) {
            return value <= 2 ? 2 : value <= 5 ? 4 : 8;
        }

        // Serializes explicit authoring edits into the existing backing UdonBehaviour.
        internal static void CopyProxyToUdon(Component proxy) {
#if UDONSHARP
            UdonSharpBehaviour behaviour = proxy as UdonSharpBehaviour;
            if (behaviour == null || UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour) == null) return;
            if (Application.isPlaying && CopyProxyToLiveUdon(behaviour)) return;
            UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
#endif

        }

        // Refreshes one live proxy after an Udon event changed runtime state without consulting
        // UdonSharp's cached storage adapter.
        internal static void CopyUdonToProxy(Component proxy) {
#if UDONSHARP
            UdonSharpBehaviour behaviour = proxy as UdonSharpBehaviour;
            if (behaviour == null || UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour) == null) return;
            if (Application.isPlaying && CopyLiveUdonToProxy(behaviour)) return;
            UdonSharpEditorUtility.CopyUdonToProxy(behaviour, ProxySerializationPolicy.All);
#endif
        }

#if UDONSHARP
        // UdonSharp caches its normal Inspector storage adapter per behaviour. If that adapter is
        // first built before the live program exists, a component selected later in Play Mode can
        // keep addressing serialized publicVariables. A fresh official heap adapter avoids that
        // cache while keeping UdonSharp responsible for resolving the running program.
        private static bool TryGetLiveUdonStorage(UdonSharpBehaviour proxy, out UdonBehaviour backingBehaviour, out IDictionary fieldDefinitions, out UdonHeapStorageInterface storage) {
            backingBehaviour = proxy != null ? UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy) : null;
            object programAsset = backingBehaviour != null ? (object)backingBehaviour.programSource : null;
            FieldInfo fieldDefinitionsField = programAsset != null ? programAsset.GetType().GetField("fieldDefinitions", BindingFlags.Instance | BindingFlags.Public) : null;
            fieldDefinitions = fieldDefinitionsField != null ? fieldDefinitionsField.GetValue(programAsset) as IDictionary : null;
            storage = Application.isPlaying && backingBehaviour != null ? new UdonHeapStorageInterface(backingBehaviour) : null;
            return fieldDefinitions != null && storage != null && storage.IsValid;
        }

        // Reads every compiled field from the initialized heap into one managed proxy without using
        // UdonSharp's potentially stale storage cache.
        private static bool CopyLiveUdonToProxy(UdonSharpBehaviour proxy) {
            if (!TryGetLiveUdonStorage(proxy, out _, out IDictionary fieldDefinitions, out UdonHeapStorageInterface storage)) return false;
            Type proxyType = proxy.GetType();
            foreach (DictionaryEntry entry in fieldDefinitions) {
                string fieldName = entry.Key as string;
                if (string.IsNullOrEmpty(fieldName)) continue;
                FieldInfo field = FindInstanceField(proxyType, fieldName);
                if (field == null || field.IsStatic || field.IsInitOnly) continue;
                object heapValue = storage.GetElementValueWeak(fieldName);
                if (!TryConvertRuntimeFieldValue(heapValue, field.FieldType, true, out object proxyValue)) continue;
                field.SetValue(proxy, proxyValue);
            }
            return true;
        }

        // Writes every compiled field from one managed proxy into the initialized heap using the
        // compiler-declared system type. This also normalizes a Texture slot that currently holds a
        // concrete RenderTexture, preventing the next proxy read from resolving it as null.
        private static bool CopyProxyToLiveUdon(UdonSharpBehaviour proxy) {
            if (!TryGetLiveUdonStorage(proxy, out UdonBehaviour backingBehaviour, out IDictionary fieldDefinitions, out UdonHeapStorageInterface storage)) return false;
            Type proxyType = proxy.GetType();
            foreach (DictionaryEntry entry in fieldDefinitions) {
                string fieldName = entry.Key as string;
                if (string.IsNullOrEmpty(fieldName)) continue;
                FieldInfo field = FindInstanceField(proxyType, fieldName);
                if (field == null || field.IsStatic || field.IsInitOnly || ContainsUdonSharpBehaviourType(field.FieldType)) continue;
                PropertyInfo systemTypeProperty = entry.Value != null ? entry.Value.GetType().GetProperty("SystemType", BindingFlags.Instance | BindingFlags.Public) : null;
                Type storageType = systemTypeProperty != null ? systemTypeProperty.GetValue(entry.Value) as Type : null;
                if (storageType == null) continue;
                if (!TryConvertRuntimeFieldValue(field.GetValue(proxy), storageType, false, out object heapValue)) continue;
                // UdonHeapStorageInterface's weak setter deliberately rejects null because
                // Type.IsInstanceOfType(null) is false. Use UdonBehaviour's typed heap bridge for
                // clears; non-null writes stay on the raw storage path and cannot invoke callbacks.
                if (heapValue == null) backingBehaviour.SetProgramVariable(fieldName, null);
                else storage.SetElementValueWeak(fieldName, heapValue);
            }
            return true;
        }

        // Referenced behaviour fields are graph links, not Inspector-authored values. Leaving them
        // to UdonSharp avoids changing the concrete heap array type beneath its recursive wrapper.
        private static bool ContainsUdonSharpBehaviourType(Type type) {
            while (type != null && type.IsArray) type = type.GetElementType();
            return type != null && typeof(UdonSharpBehaviour).IsAssignableFrom(type);
        }

        // Resolves private fields declared anywhere in the user's UdonSharp inheritance chain.
        private static FieldInfo FindInstanceField(Type type, string fieldName) {
            while (type != null && type != typeof(UdonSharpBehaviour)) {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
                type = type.BaseType;
            }
            return null;
        }

        // Converts UdonBehaviour references and their arrays in the same direction as the UdonSharp
        // serializer; primitives and ordinary UnityEngine.Object references pass through unchanged.
        private static bool TryConvertRuntimeFieldValue(object source, Type targetType, bool toProxy, out object converted) {
            if (source == null) {
                converted = targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
                return true;
            }
            Type sourceType = source.GetType();
            if (targetType.IsAssignableFrom(sourceType)) {
                converted = source;
                return true;
            }
            if (targetType.IsArray && source is Array sourceArray) {
                Type targetElementType = targetType.GetElementType();
                Array targetArray = Array.CreateInstance(targetElementType, sourceArray.Length);
                for (int i = 0; i < sourceArray.Length; i++) {
                    if (!TryConvertRuntimeFieldValue(sourceArray.GetValue(i), targetElementType, toProxy, out object element)) {
                        converted = null;
                        return false;
                    }
                    targetArray.SetValue(element, i);
                }
                converted = targetArray;
                return true;
            }
            if (toProxy && typeof(UdonSharpBehaviour).IsAssignableFrom(targetType) && source is UdonBehaviour sourceBacking) {
                UdonSharpBehaviour sourceProxy = UdonSharpEditorUtility.GetProxyBehaviour(sourceBacking);
                if (sourceProxy == null || !targetType.IsInstanceOfType(sourceProxy)) {
                    converted = null;
                    return sourceProxy == null;
                }
                converted = sourceProxy;
                return true;
            }
            if (!toProxy && typeof(UdonBehaviour).IsAssignableFrom(targetType) && source is UdonSharpBehaviour sourceProxyBehaviour) {
                converted = UdonSharpEditorUtility.GetBackingUdonBehaviour(sourceProxyBehaviour);
                return converted == null || targetType.IsInstanceOfType(converted);
            }
            if (targetType.IsEnum) {
                try {
                    converted = Enum.ToObject(targetType, source);
                    return true;
                } catch {
                    converted = null;
                    return false;
                }
            }
            converted = null;
            return false;
        }
#endif

        // Hydrates a late-opened Point Light Inspector and its complete referenced Manager graph
        // from live heaps before any SerializedProperty is drawn.
        internal static void SynchronizeRuntimeInspectorGraphFromUdon(PointLightVolumeInstance pointLight) {
#if UDONSHARP
            if (!Application.isPlaying || pointLight == null) return;
            CopyLiveUdonToProxy(pointLight);
            LightVolumeManager manager = pointLight.LightVolumeManager;
            if (manager != null) SynchronizeRuntimeInspectorGraphFromUdon(manager);
#endif
        }

        // Hydrates a late-opened Light Volume Inspector and its complete referenced Manager graph.
        internal static void SynchronizeRuntimeInspectorGraphFromUdon(LightVolumeInstance volume) {
#if UDONSHARP
            if (!Application.isPlaying || volume == null) return;
            CopyLiveUdonToProxy(volume);
            LightVolumeManager manager = volume.LightVolumeManager;
            if (manager != null) SynchronizeRuntimeInspectorGraphFromUdon(manager);
#endif
        }

        // Hydrates the Manager plus all registry members without recursive cached serialization.
        internal static void SynchronizeRuntimeInspectorGraphFromUdon(LightVolumeManager manager) {
#if UDONSHARP
            if (!Application.isPlaying || manager == null) return;
            CopyLiveUdonToProxy(manager);
            LightVolumeInstance[] volumes = manager.LightVolumeInstances;
            if (volumes != null) {
                for (int i = 0; i < volumes.Length; i++) if (volumes[i] != null) CopyLiveUdonToProxy(volumes[i]);
            }
            PointLightVolumeInstance[] pointLights = manager.PointLightVolumeInstances;
            if (pointLights != null) {
                for (int i = 0; i < pointLights.Length; i++) if (pointLights[i] != null) CopyLiveUdonToProxy(pointLights[i]);
            }
            RestoreRuntimeShadowSourcesForInspector(manager);
#endif
        }

        // Hydrates every selected root, then each shared Manager graph once. Multi-object
        // Inspectors otherwise repeat the same registry-sized heap copy for every selected child.
        internal static void SynchronizeRuntimeInspectorGraphsFromUdon(UnityEngine.Object[] inspectedTargets) {
#if UDONSHARP
            if (!Application.isPlaying || inspectedTargets == null) return;
            HashSet<LightVolumeManager> managers = new HashSet<LightVolumeManager>();
            for (int i = 0; i < inspectedTargets.Length; i++) {
                if (inspectedTargets[i] is PointLightVolumeInstance pointLight) {
                    CopyLiveUdonToProxy(pointLight);
                    if (pointLight.LightVolumeManager != null) managers.Add(pointLight.LightVolumeManager);
                } else if (inspectedTargets[i] is LightVolumeInstance volume) {
                    CopyLiveUdonToProxy(volume);
                    if (volume.LightVolumeManager != null) managers.Add(volume.LightVolumeManager);
                }
            }
            foreach (LightVolumeManager manager in managers) SynchronizeRuntimeInspectorGraphFromUdon(manager);
#endif
        }

        // Repairs Texture-typed runtime shadow bridges after UdonSharp's recursive Play Mode
        // Udon-to-proxy pass. The exact RenderTexture owner survives that pass, while the base
        // Texture field is otherwise cleared and written back as null at the end of Inspector GUI.
        internal static void RestoreRuntimeShadowSourcesForInspector(LightVolumeManager manager) {
            if (!Application.isPlaying || manager == null) return;
            PointLightVolumeInstance[] pointLights = manager.PointLightVolumeInstances;
            if (pointLights == null) return;
            for (int i = 0; i < pointLights.Length; i++) {
                PointLightVolumeInstance pointLight = pointLights[i];
                if (pointLight != null) pointLight.RestoreRuntimeShadowSourceForInspector();
            }
        }

        // Runs an explicit Play Mode bake on the live point-light UdonBehaviour. The build
        // preprocessor already owns the shared camera/material lifetime; this only mirrors those
        // dependencies to the requested light and invokes the same path used by Bake In Game.
        private static void BakeRuntimeShadow(PointLightVolumeInstance pointLight) {
#if UDONSHARP
            if (!Application.isPlaying || pointLight == null || !pointLight.Shadows) return;
            LightVolumeManager manager = pointLight.LightVolumeManager;
            if (manager == null || manager != GetPrimaryManager()) return;
            var pointBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(pointLight);
            if (pointBacking == null) return;

            // The selected point may have pending Inspector-authored fields while its referenced
            // Manager proxy has not otherwise been inspected this frame.
            CopyUdonToProxy(manager);
            LightVolumePreprocessor.EnsureRuntimeDependencies(manager);

            // Publish the selected proxy before installing bake-only runtime dependencies. The
            // latter intentionally forces normal output on the live backing for this one-shot
            // Inspector bake and must therefore be the final write before the event.
            CopyProxyToUdon(pointLight);
            object previousDirectOutputValue = pointBacking.GetProgramVariable(nameof(PointLightVolumeInstance.RuntimeShadowDirectOutput));
            bool previousDirectOutput = previousDirectOutputValue is bool && (bool)previousDirectOutputValue;
            try {
                // An explicit re-bake keeps the previous live source valid until BakeShadows
                // publishes its replacement; one-shot build preparation is the only path that
                // clears it early.
                LightVolumePreprocessor.PreparePointLightRuntimeShadowDependencies(pointLight, manager, false);
                pointBacking.SendCustomEvent(nameof(PointLightVolumeInstance.BakeShadows));
            } finally {
                pointBacking.SetProgramVariable(nameof(PointLightVolumeInstance.RuntimeShadowDirectOutput), previousDirectOutput);
            }

            // BakeShadows mutates both behaviours (source textures, IDs, receiver data and atlas
            // caches). Pull the complete graph back so subsequent Inspector repaints preserve the
            // live result instead of restoring a pre-bake snapshot.
            SynchronizeRuntimeInspectorGraphFromUdon(manager);
#endif
        }

        // Applies one Manager rebuild directly in Edit Mode or through the appropriate UdonSharp play-mode path.
        internal static void RefreshManagerOnce(LightVolumeManager manager, bool immediate) {
            if (manager == null) return;
            bool handledByUdon = immediate ? RefreshRuntimeManagerImmediately(manager) : QueueRuntimeManagerRefresh(manager);
            if (!handledByUdon) manager.UpdateVolumes();
        }

        // Applies a synchronous Manager refresh and mirrors its complete Udon graph back to the
        // proxies before UdonSharp recursively serializes the inspected volume at the end of OnGUI.
        internal static bool RefreshRuntimeManagerImmediately(LightVolumeManager manager) {
#if UDONSHARP
            if (!Application.isPlaying || manager == null) return false;
            if (manager != GetPrimaryManager()) return true;
            var backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            if (backingBehaviour == null) return false;
            backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.UpdateVolumes));
            SynchronizeRuntimeInspectorGraphFromUdon(manager);
            return true;
#else
            return false;
#endif
        }

        // Manager Inspector edits originate on the proxy. Mirror UdonSharp's custom-event round trip so the synchronous event result survives the Inspector wrapper's final proxy serialization.
        internal static bool RefreshRuntimeManagerFromProxyImmediately(
            LightVolumeManager manager,
            bool reinitializeCustomTextures = false,
            bool reinitializeShadowTextures = false) {
#if UDONSHARP
            if (!Application.isPlaying || manager == null) return false;
            if (manager != GetPrimaryManager()) return true;
            var backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            if (backingBehaviour == null) return false;
            CopyProxyToUdon(manager);
            if (reinitializeCustomTextures) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.ReinitializeCustomTextures));
            if (reinitializeShadowTextures) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.ReinitializeShadowTextures));
            if (!reinitializeShadowTextures) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.UpdateVolumes));
            SynchronizeRuntimeInspectorGraphFromUdon(manager);
            return true;
#else
            return false;
#endif
        }

        // UdonSharp performs its recursive Inspector serialization after the custom Inspector returns. Rebuild manager-owned caches once that final copy has completed.
        internal static bool QueueRuntimeManagerRefresh(
            LightVolumeManager manager,
            bool reinitializeCustomTextures = false,
            bool reinitializeShadowTextures = false) {
#if UDONSHARP
            if (!Application.isPlaying || manager == null) return false;
            if (manager != GetPrimaryManager()) return true;
            var backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            if (backingBehaviour == null) return false;

            _queuedRuntimeCustomTextureReinitialization |= reinitializeCustomTextures;
            _queuedRuntimeShadowTextureReinitialization |= reinitializeShadowTextures;
            _queuedRuntimeManagerUpdate = true;
            ScheduleRuntimeInspectorFlush();
            return true;
#else
            return false;
#endif
        }

        // Inspector buttons must execute after UdonSharp's wrapper has completed its final
        // recursive proxy-to-Udon copy for the current GUI event.
        internal static bool QueueRuntimeShadowBake(PointLightVolumeInstance pointLight) {
#if UDONSHARP
            if (!Application.isPlaying || pointLight == null || !pointLight.Shadows) return false;
            LightVolumeManager manager = pointLight.LightVolumeManager;
            if (manager == null || manager != GetPrimaryManager()) return false;
            if (UdonSharpEditorUtility.GetBackingUdonBehaviour(pointLight) == null) return false;
            int instanceId = pointLight.GetInstanceID();
            if (!_queuedRuntimeShadowBakeInstanceIds.Contains(instanceId)) _queuedRuntimeShadowBakeInstanceIds.Add(instanceId);
            ScheduleRuntimeInspectorFlush();
            return true;
#else
            return false;
#endif
        }

        // Applies lightweight Manager settings without rebuilding registries or texture arrays.
        internal static void ApplyRuntimeManagerSettings(LightVolumeManager manager) {
            if (manager == null) return;
#if UDONSHARP
            if (Application.isPlaying) {
                if (manager != GetPrimaryManager()) return;
                var backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
                if (backingBehaviour != null) {
                    // ApplySettings has already published the canonicalized Inspector values. Only
                    // the runtime event itself must wait for UdonSharp's final wrapper copy.
                    _queuedRuntimeSettingsApply = true;
                    ScheduleRuntimeInspectorFlush();
                    return;
                }
            }
#endif
            manager._ApplyEditorSettings();
        }

#if UDONSHARP
        // Coalesces all runtime work requested from custom inspectors into the first editor turn
        // after UdonSharp has serialized the current IMGUI event.
        private static void ScheduleRuntimeInspectorFlush() {
            if (!_runtimeInspectorFlushScheduled) {
                _runtimeInspectorFlushScheduled = true;
                EditorApplication.delayCall += FlushRuntimeInspectorCommands;
            }
            EditorApplication.QueuePlayerLoopUpdate();
        }

        // Drops delayed Inspector commands at Play Mode and editor-domain boundaries.
        private static void CancelRuntimeInspectorCommands() {
            EditorApplication.delayCall -= FlushRuntimeInspectorCommands;
            _runtimeInspectorFlushScheduled = false;
            _queuedRuntimeCustomTextureReinitialization = false;
            _queuedRuntimeShadowTextureReinitialization = false;
            _queuedRuntimeManagerUpdate = false;
            _queuedRuntimeSettingsApply = false;
            _queuedRuntimeShadowBakeInstanceIds.Clear();
        }

        // Applies coalesced runtime commands directly to the live backing behaviours.
        private static void FlushRuntimeInspectorCommands() {
            EditorApplication.delayCall -= FlushRuntimeInspectorCommands;
            _runtimeInspectorFlushScheduled = false;
            bool reinitializeCustomTextures = _queuedRuntimeCustomTextureReinitialization;
            bool reinitializeShadowTextures = _queuedRuntimeShadowTextureReinitialization;
            bool updateManager = _queuedRuntimeManagerUpdate;
            bool applySettings = _queuedRuntimeSettingsApply;
            int[] shadowBakeInstanceIds = _queuedRuntimeShadowBakeInstanceIds.ToArray();
            _queuedRuntimeCustomTextureReinitialization = false;
            _queuedRuntimeShadowTextureReinitialization = false;
            _queuedRuntimeManagerUpdate = false;
            _queuedRuntimeSettingsApply = false;
            _queuedRuntimeShadowBakeInstanceIds.Clear();
            LightVolumeManager manager = GetPrimaryManager();
            if (!Application.isPlaying || manager == null) return;
            var backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            if (backingBehaviour == null) return;
            if (applySettings) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager._ApplyEditorSettings));
            if (reinitializeCustomTextures) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.ReinitializeCustomTextures));
            if (reinitializeShadowTextures) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.ReinitializeShadowTextures));
            else if (updateManager) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.UpdateVolumes));

            for (int i = 0; i < shadowBakeInstanceIds.Length; i++) {
                PointLightVolumeInstance pointLight = EditorUtility.InstanceIDToObject(shadowBakeInstanceIds[i]) as PointLightVolumeInstance;
                if (pointLight == null || pointLight.LightVolumeManager != manager) continue;
                BakeRuntimeShadow(pointLight);
            }

            if (shadowBakeInstanceIds.Length > 0) {
                // BakeRuntimeShadow pulls the recursive graph once per light. Publish every normal
                // source bridge afterwards while no Inspector wrapper can overwrite it.
                RestoreRuntimeShadowSourcesForInspector(manager);
                PointLightVolumeInstance[] pointLights = manager.PointLightVolumeInstances;
                if (pointLights != null) {
                    for (int i = 0; i < pointLights.Length; i++) {
                        PointLightVolumeInstance pointLight = pointLights[i];
                        if (pointLight == null || pointLight.RuntimeShadowTexturePreview == null || !pointLight.RuntimeShadowSourceInitializedPreview) continue;
                        CopyProxyToUdon(pointLight);
                    }
                }
            }
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
#endif
    }
}
