using System;
using System.IO;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UDONSHARP
using UdonSharpEditor;
#endif

namespace VRCLightVolumes {

    // Internal editor backend for the unified Udon manager. Public integrations enter through the Editor facade or the removable Legacy compatibility files.
    internal static class LightVolumeManagerEditorBackend {
        private const float ShadowMinVarianceValueMin = 0.0001f;
        private const float ShadowMinVarianceValueMax = 1f;

        private static EditorCoroutine _atlasCoroutine;
        private static bool _customProbeFinalizeQueued;
        private static bool _atlasGenerationQueued;
#if UDONSHARP
        private static bool _queuedRuntimeCustomTextureReinitialization;
        private static bool _queuedRuntimeShadowTextureReinitialization;
        private static bool _runtimeManagerRefreshQueued;
#endif

        // Returns the single Manager allowed to own global Light Volumes state. Invalid duplicate setups consistently use the first scene/hierarchy entry until the extras are removed.
        internal static LightVolumeManager GetPrimaryManager() {
            return GetPrimaryManager(out _);
        }

        // Returns the primary Manager and the total number of eligible Managers in loaded scenes.
        internal static LightVolumeManager GetPrimaryManager(out int managerCount) {
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
            if (copyProxyToUdon) CopyProxyToUdon(manager);
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

            if (_atlasCoroutine != null) EditorCoroutineUtility.StopCoroutine(_atlasCoroutine);

            // Post-processed atlases are commonly updated slice by slice, so minimizing depth reduces per-frame draw calls even when that costs a little more VRAM.
            TexturePackingStrategy strategy = ResolveAtlasPackingStrategy(manager);
            _atlasCoroutine = EditorCoroutineUtility.StartCoroutine(Texture3DAtlasGenerator.CreateAtlas(volumes, atlas => CompleteAtlas(manager, volumes, atlas), manager.DownscaleVolumes, strategy), manager);
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
            _atlasCoroutine = null;
            if (manager == null) return;
            if (atlas.Texture == null) return;

            manager.LightVolumeAtlasBase = atlas.Texture;
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
                LVUtils.SaveAsAssetDelayed(atlas.Texture, $"{directory}/{scene.name}/VRCLightVolumes/LightVolumeAtlas.asset");
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
            if (manager == null || Application.isPlaying || manager != GetPrimaryManager()) return;
            BakeShadowMapsCore(manager);
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
            UdonSharp.UdonSharpBehaviour behaviour = proxy as UdonSharp.UdonSharpBehaviour;
            if (behaviour != null && UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour) != null)
                UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
#endif
        }

        // Applies one Manager rebuild directly in Edit Mode or through the appropriate UdonSharp play-mode path.
        internal static void RefreshManagerOnce(LightVolumeManager manager, bool immediate) {
            if (manager == null) return;
            bool handledByUdon = immediate ? RefreshRuntimeManagerImmediately(manager) : QueueRuntimeManagerRefresh(manager);
            if (!handledByUdon) manager.UpdateVolumes();
        }

        // The volume and manager are separate Udon behaviours, so UdonSharp's final volume proxy copy cannot overwrite this synchronous manager refresh.
        internal static bool RefreshRuntimeManagerImmediately(LightVolumeManager manager) {
#if UDONSHARP
            if (!Application.isPlaying || manager == null) return false;
            if (manager != GetPrimaryManager()) return true;
            var backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            if (backingBehaviour == null) return false;
            backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.UpdateVolumes));
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
            UdonSharpEditorUtility.CopyProxyToUdon(manager, ProxySerializationPolicy.All);
            if (reinitializeCustomTextures) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.ReinitializeCustomTextures));
            if (reinitializeShadowTextures) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.ReinitializeShadowTextures));
            if (!reinitializeShadowTextures) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.UpdateVolumes));
            UdonSharpEditorUtility.CopyUdonToProxy(manager, ProxySerializationPolicy.All);
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
            if (!_runtimeManagerRefreshQueued) {
                _runtimeManagerRefreshQueued = true;
                EditorApplication.delayCall += FlushRuntimeManagerRefreshes;
            }
            EditorApplication.QueuePlayerLoopUpdate();
            return true;
#else
            return false;
#endif
        }

        // Applies lightweight Manager settings without rebuilding registries or texture arrays.
        internal static void ApplyRuntimeManagerSettings(LightVolumeManager manager) {
            if (manager == null) return;
            manager._ApplyEditorSettings();
        }

#if UDONSHARP
        // Applies coalesced runtime cache rebuilds directly to Manager backing behaviours.
        private static void FlushRuntimeManagerRefreshes() {
            EditorApplication.delayCall -= FlushRuntimeManagerRefreshes;
            _runtimeManagerRefreshQueued = false;
            bool reinitializeCustomTextures = _queuedRuntimeCustomTextureReinitialization;
            bool reinitializeShadowTextures = _queuedRuntimeShadowTextureReinitialization;
            _queuedRuntimeCustomTextureReinitialization = false;
            _queuedRuntimeShadowTextureReinitialization = false;
            LightVolumeManager manager = GetPrimaryManager();
            if (!Application.isPlaying || manager == null) return;
            var backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            if (backingBehaviour == null) return;
            if (reinitializeCustomTextures) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.ReinitializeCustomTextures));
            if (reinitializeShadowTextures) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.ReinitializeShadowTextures));
            if (!reinitializeShadowTextures) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.UpdateVolumes));
        }
#endif
    }
}
