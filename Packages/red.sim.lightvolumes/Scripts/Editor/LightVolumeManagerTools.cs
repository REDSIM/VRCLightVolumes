using System;
using System.Collections.Generic;
using System.IO;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using PostProcessor = VRCLightVolumes.LightVolumeManager.PostProcessor;
#if UDONSHARP
using UdonSharpEditor;
#endif

namespace VRCLightVolumes {
    // Central editor backend for the unified Udon manager. It owns transient state only;
    // every persistent setting remains on LightVolumeManager or its runtime instances.
    public static class LightVolumeManagerTools {
        private const float ShadowMinVarianceValueMin = 0.0001f;
        private const float ShadowMinVarianceValueMax = 1f;

        private static readonly Dictionary<int, EditorCoroutine> _atlasCoroutines = new Dictionary<int, EditorCoroutine>();
        private static readonly HashSet<LightVolumeManager> _queuedCustomProbeManagers = new HashSet<LightVolumeManager>();
        private static bool _customProbeFinalizeQueued;
        private static readonly HashSet<LightVolumeManager> _queuedAtlasManagers = new HashSet<LightVolumeManager>();
        private static bool _atlasGenerationQueued;
#if UDONSHARP
        private static readonly HashSet<LightVolumeManager> _queuedRuntimeManagerRefreshes = new HashSet<LightVolumeManager>();
        private static readonly HashSet<LightVolumeManager> _queuedRuntimeCustomTextureReinitializations = new HashSet<LightVolumeManager>();
        private static readonly HashSet<LightVolumeManager> _queuedRuntimeShadowTextureReinitializations = new HashSet<LightVolumeManager>();
#endif

        // Applies target-dependent authoring values and optional texture-cache rebuilds.
        // Custom Inspectors can leave the final Play Mode proxy copy to UdonSharp's own wrapper.
        public static void ApplySettings(LightVolumeManager manager, bool markDirty = true, bool reinitializeCustomTextures = false, bool reinitializeShadowTextures = false, bool updateVolumes = true, bool copyProxyToUdon = true) {
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
            if (updateVolumes && !runtimeRefreshQueued) manager.UpdateVolumes();
        }

        // Bakery helpers are created or removed only as a direct result of an explicit mode edit.
        public static void HandleBakingModeChanged(LightVolumeManager manager, int previousBakingMode) {
#if BAKERY_INCLUDED
            if (manager == null || manager.BakingMode == previousBakingMode) return;
            LightVolumeInstance[] volumes = manager.LightVolumeInstances ?? Array.Empty<LightVolumeInstance>();
            bool createIfMissing = manager.BakingMode == 1;
            for (int i = 0; i < volumes.Length; i++) {
                LightVolumeInstance volume = volumes[i];
                if (volume == null || volume.LightVolumeManager != manager) continue;
                LightVolumeTools.SetupBakeryDependencies(volume, createIfMissing);
            }
            LightVolumeBaker.QueueBakeryWatcherRefresh();
#endif
        }

        // Synchronizes registry ownership and stable authoring order without rearranging the list.
        public static void SynchronizeRegistryMetadata(LightVolumeManager manager) {
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

        private static bool ComesBeforeByWeightAndResolution(LightVolumeInstance volume, LightVolumeInstance previous) {
            if (volume == null) return false;
            if (previous == null) return true;
            if (volume.RegistryWeight != previous.RegistryWeight)
                return volume.RegistryWeight > previous.RegistryWeight;
            if (volume.AdaptiveResolution != previous.AdaptiveResolution)
                return !volume.AdaptiveResolution;
            return volume.AdaptiveResolution && volume.VoxelsPerUnit > previous.VoxelsPerUnit;
        }

        // Keeps authoring weights authoritative and only resolves equal-weight groups by resolution settings.
        public static void SortLightVolumesByVoxelsPerUnit(LightVolumeManager manager) {
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

        public static void ReinitializeCustomTextures(LightVolumeManager manager) {
            ReinitializeTextures(manager, true, false);
        }

        public static void ReinitializeShadowTextures(LightVolumeManager manager) {
            ReinitializeTextures(manager, false, true);
        }

        internal static void ReinitializeTextures(LightVolumeManager manager, bool customTextures, bool shadowTextures) {
            if (manager == null || !customTextures && !shadowTextures) return;
            if (QueueRuntimeManagerRefresh(manager, customTextures, shadowTextures)) return;
            if (customTextures) manager.ReinitializeCustomTextures();
            if (shadowTextures) manager.ReinitializeShadowTextures();
            CopyProxyToUdon(manager);
            manager.UpdateVolumes();
        }

        // Coalesces a burst of Inspector edits into one atlas pack without adding Update polling.
        public static void QueueAtlasGeneration(LightVolumeManager manager) {
            if (manager == null || Application.isPlaying) return;
            _queuedAtlasManagers.Add(manager);
            if (_atlasGenerationQueued) return;
            _atlasGenerationQueued = true;
            EditorApplication.delayCall += GenerateQueuedAtlases;
        }

        private static void GenerateQueuedAtlases() {
            EditorApplication.delayCall -= GenerateQueuedAtlases;
            _atlasGenerationQueued = false;
            LightVolumeManager[] managers = new LightVolumeManager[_queuedAtlasManagers.Count];
            _queuedAtlasManagers.CopyTo(managers);
            _queuedAtlasManagers.Clear();
            for (int i = 0; i < managers.Length; i++) {
                LightVolumeManager manager = managers[i];
                if (manager == null || !CanGenerateAtlas(manager)) continue;
                GenerateAtlas(manager);
            }
        }

        // Packs every explicitly registered Light Volume; no scene scanning or implicit component creation occurs.
        public static void GenerateAtlas(this LightVolumeManager manager) {
            if (manager == null || Application.isPlaying || LVUtils.IsInPrefabAsset(manager)) return;
            LightVolumeInstance[] volumes = GetAtlasVolumes(manager);
            if (volumes.Length == 0) return;

            int managerId = manager.GetInstanceID();
            if (_atlasCoroutines.TryGetValue(managerId, out EditorCoroutine running) && running != null)
                EditorCoroutineUtility.StopCoroutine(running);

            TexturePackingStrategy strategy = ResolveAtlasPackingStrategy(manager);
            EditorCoroutine coroutine = EditorCoroutineUtility.StartCoroutine(Texture3DAtlasGenerator.CreateAtlas(volumes, atlas => CompleteAtlas(manager, volumes, atlas), manager.DownscaleVolumes, strategy), manager);
            _atlasCoroutines[managerId] = coroutine;
        }

        private static TexturePackingStrategy ResolveAtlasPackingStrategy(LightVolumeManager manager) {
            PostProcessor[] postProcessors = manager != null ? manager.AtlasPostProcessors : null;
            // Post-processed 3D textures are commonly updated slice by slice, so minimizing atlas
            // depth reduces per-frame draw calls even when that costs a little more VRAM.
            return postProcessors != null && postProcessors.Length > 0
                ? TexturePackingStrategy.MinimumDepth
                : TexturePackingStrategy.MinimumVRAM;
        }

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

        private static void CompleteAtlas(LightVolumeManager manager, LightVolumeInstance[] volumes, Atlas3D atlas) {
            if (manager == null) return;
            _atlasCoroutines.Remove(manager.GetInstanceID());
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
                if (!volume.Bake && volume.ReserveUVSpace)
                    volume.InvBakedRotation = Quaternion.Inverse(LightVolumeTools.GetRotation(volume));
                LVUtils.MarkDirty(volume);
                CopyProxyToUdon(volume);
            }

            RefreshAtlasOutput(manager);
            Scene scene = manager.gameObject.scene;
            string scenePath = scene.path;
            if (!string.IsNullOrEmpty(scenePath)) {
                string directory = Path.GetDirectoryName(scenePath);
                LVUtils.SaveAsAssetDelayed(atlas.Texture, $"{directory}/{scene.name}/VRCLightVolumes/LightVolumeAtlas.asset");
            }
        }

        public static int GetCustomProbesCount(this LightVolumeManager manager) {
            if (manager == null || Application.isPlaying) return 0;
            int count = 0;
            LightVolumeInstance[] volumes = manager.LightVolumeInstances;
            for (int i = 0; i < volumes.Length; i++) if (IsCustomProbeVolume(volumes[i])) count++;
            return count;
        }

        public static Vector3[] GetCustomProbes(this LightVolumeManager manager, int id) {
            LightVolumeInstance volume = GetCustomProbeVolume(manager, id);
            return volume != null ? LightVolumeTools.GetCustomProbes(volume) : Array.Empty<Vector3>();
        }

        public static void SetCustomProbesBaked(this LightVolumeManager manager, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b) {
            SetCustomProbesBaked(manager, id, l0, l1r, l1g, l1b, null, manager != null && manager.Denoise);
        }

        public static void SetCustomProbesBaked(this LightVolumeManager manager, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, bool denoise) {
            SetCustomProbesBaked(manager, id, l0, l1r, l1g, l1b, null, denoise);
        }

        public static void SetCustomProbesBaked(this LightVolumeManager manager, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity) {
            SetCustomProbesBaked(manager, id, l0, l1r, l1g, l1b, validity, manager != null && manager.Denoise);
        }

        public static void SetCustomProbesBaked(this LightVolumeManager manager, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity, bool denoise) {
            LightVolumeInstance volume = GetCustomProbeVolume(manager, id);
            if (volume == null || !LightVolumeBaker.SaveCustomProbesBaked(volume, l0, l1r, l1g, l1b, validity, denoise)) return;
            volume.InvBakedRotation = Quaternion.Inverse(LightVolumeTools.GetRotation(volume));
            LVUtils.MarkDirty(volume);
            CopyProxyToUdon(volume);
            QueueCustomProbeAtlasGeneration(manager);
        }

        private static bool IsCustomProbeVolume(LightVolumeInstance volume) {
            return volume != null && volume.Bake && volume.gameObject.activeInHierarchy && !volume.CompareTag("EditorOnly");
        }

        private static LightVolumeInstance GetCustomProbeVolume(LightVolumeManager manager, int id) {
            if (manager == null || Application.isPlaying) return null;
            int customId = 0;
            LightVolumeInstance[] volumes = manager.LightVolumeInstances;
            for (int i = 0; i < volumes.Length; i++) {
                LightVolumeInstance volume = volumes[i];
                if (!IsCustomProbeVolume(volume)) continue;
                if (customId == id) return volume;
                customId++;
            }
            Debug.LogError($"[LightVolumeManager] Custom probe Light Volume ID {id} is invalid. Available volume count: {customId}.", manager);
            return null;
        }

        private static void QueueCustomProbeAtlasGeneration(LightVolumeManager manager) {
            if (manager == null) return;
            _queuedCustomProbeManagers.Add(manager);
            if (_customProbeFinalizeQueued) return;
            _customProbeFinalizeQueued = true;
            EditorApplication.delayCall += FinalizeCustomProbeAtlases;
        }

        private static void FinalizeCustomProbeAtlases() {
            EditorApplication.delayCall -= FinalizeCustomProbeAtlases;
            _customProbeFinalizeQueued = false;
            LightVolumeManager[] managers = new LightVolumeManager[_queuedCustomProbeManagers.Count];
            _queuedCustomProbeManagers.CopyTo(managers);
            _queuedCustomProbeManagers.Clear();
            for (int i = 0; i < managers.Length; i++) {
                LightVolumeManager manager = managers[i];
                if (manager == null || !CanGenerateAtlas(manager)) continue;
                BakeShadowMaps(manager, false);
                GenerateAtlas(manager);
            }
        }

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

        public static void BakeShadowMaps(this LightVolumeManager manager) {
            BakeShadowMaps(manager, false);
        }

        public static bool BakeShadowMaps(LightVolumeManager manager, bool forceAll) {
            if (manager == null || Application.isPlaying) return false;
            bool rebaked = false;
            PointLightVolumeInstance[] pointLights = manager.PointLightVolumeInstances;
            for (int i = 0; i < pointLights.Length; i++) {
                PointLightVolumeInstance pointLight = pointLights[i];
                if (pointLight == null || !pointLight.Shadows || (!forceAll && !pointLight.RebakeShadows)) continue;
                PointLightVolumeEditorUtility.Sync(pointLight, false, false);
                if (PointLightShadowBaker.BakeShadowMap(pointLight, $"| {pointLight.gameObject.name} ({i}/{pointLights.Length})", false)) rebaked = true;
            }
            if (rebaked) ReinitializeShadowTextures(manager);
            return rebaked;
        }

        public static void RegisterPostProcessorCRT(this LightVolumeManager manager, CustomRenderTexture texture) {
            if (manager != null) manager.RegisterPostProcessorCRT(texture);
        }

        public static void RegisterPostProcessor(this LightVolumeManager manager, PostProcessor processor) {
            if (manager != null) manager.RegisterPostProcessor(processor);
        }

        public static void UnregisterPostProcessorCRT(this LightVolumeManager manager, CustomRenderTexture texture) {
            if (manager != null) manager.UnregisterPostProcessorCRT(texture);
        }

        public static void UnregisterPostProcessor(this LightVolumeManager manager, RenderTexture texture) {
            if (manager != null) manager.UnregisterPostProcessor(texture);
        }

        public static void UnregisterPostProcessor(this LightVolumeManager manager, PostProcessor processor) {
            if (manager != null) manager.UnregisterPostProcessor(processor);
        }

        private static void RefreshAtlasOutput(LightVolumeManager manager) {
            if (manager != null) manager.RefreshAtlasPostProcessors();
        }

        public static float GetShadowMinVarianceValue(float slider) {
            return ShadowMinVarianceValueMin * Mathf.Pow(ShadowMinVarianceValueMax / ShadowMinVarianceValueMin, Mathf.Clamp01(slider));
        }

        public static bool IsMobileBuildTarget() {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            return target == BuildTarget.Android || target == BuildTarget.iOS;
        }

        public static int ResolveCoarseFactor(int value) {
            return value <= 2 ? 2 : value <= 5 ? 4 : 8;
        }

        // Serializes explicit authoring edits into the existing backing UdonBehaviour.
        public static void CopyProxyToUdon(Component proxy) {
#if UDONSHARP
            UdonSharp.UdonSharpBehaviour behaviour = proxy as UdonSharp.UdonSharpBehaviour;
            if (behaviour != null && UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour) != null)
                UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
#endif
        }

        // The volume and manager are separate Udon behaviours, so UdonSharp's final volume proxy
        // copy cannot overwrite this synchronous manager refresh.
        public static bool RefreshRuntimeManagerImmediately(LightVolumeManager manager) {
#if UDONSHARP
            if (!Application.isPlaying || manager == null) return false;
            var backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            if (backingBehaviour == null) return false;
            backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.UpdateVolumes));
            return true;
#else
            return false;
#endif
        }

        // Manager Inspector edits originate on the proxy. Mirror UdonSharp's custom-event round trip
        // so the synchronous event result survives the Inspector wrapper's final proxy serialization.
        public static bool RefreshRuntimeManagerFromProxyImmediately(
            LightVolumeManager manager,
            bool reinitializeCustomTextures = false,
            bool reinitializeShadowTextures = false) {
#if UDONSHARP
            if (!Application.isPlaying || manager == null) return false;
            var backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            if (backingBehaviour == null) return false;
            UdonSharpEditorUtility.CopyProxyToUdon(manager, ProxySerializationPolicy.All);
            if (reinitializeCustomTextures)
                backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.ReinitializeCustomTextures));
            if (reinitializeShadowTextures)
                backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.ReinitializeShadowTextures));
            backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.UpdateVolumes));
            UdonSharpEditorUtility.CopyUdonToProxy(manager, ProxySerializationPolicy.All);
            return true;
#else
            return false;
#endif
        }

        // UdonSharp performs its recursive Inspector serialization after the custom Inspector returns.
        // Rebuild manager-owned caches once that final copy has completed.
        public static bool QueueRuntimeManagerRefresh(
            LightVolumeManager manager,
            bool reinitializeCustomTextures = false,
            bool reinitializeShadowTextures = false) {
#if UDONSHARP
            if (!Application.isPlaying || manager == null) return false;
            var backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            if (backingBehaviour == null) return false;

            _queuedRuntimeManagerRefreshes.Add(manager);
            if (reinitializeCustomTextures) _queuedRuntimeCustomTextureReinitializations.Add(manager);
            if (reinitializeShadowTextures) _queuedRuntimeShadowTextureReinitializations.Add(manager);
            QueueRuntimeRefreshFlush();
            return true;
#else
            return false;
#endif
        }

        public static void ApplyRuntimeManagerSettings(LightVolumeManager manager) {
            if (manager == null) return;
            manager._ApplyEditorSettings();
        }

#if UDONSHARP
        private static void QueueRuntimeRefreshFlush() {
            EditorApplication.delayCall -= FlushRuntimeManagerRefreshes;
            EditorApplication.delayCall += FlushRuntimeManagerRefreshes;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void FlushRuntimeManagerRefreshes() {
            EditorApplication.delayCall -= FlushRuntimeManagerRefreshes;
            LightVolumeManager[] queued = new LightVolumeManager[_queuedRuntimeManagerRefreshes.Count];
            _queuedRuntimeManagerRefreshes.CopyTo(queued);
            _queuedRuntimeManagerRefreshes.Clear();

            for (int i = 0; i < queued.Length; i++) {
                LightVolumeManager manager = queued[i];
                bool reinitializeCustomTextures = _queuedRuntimeCustomTextureReinitializations.Remove(manager);
                bool reinitializeShadowTextures = _queuedRuntimeShadowTextureReinitializations.Remove(manager);
                if (!Application.isPlaying || manager == null) continue;
                var backingBehaviour = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
                if (backingBehaviour == null) continue;
                if (reinitializeCustomTextures) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.ReinitializeCustomTextures));
                if (reinitializeShadowTextures) backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.ReinitializeShadowTextures));
                backingBehaviour.SendCustomEvent(nameof(LightVolumeManager.UpdateVolumes));
            }
        }
#endif
    }

    // Mirrors scene-object edits into unified Udon proxies without restoring per-object ExecuteAlways polling.
    [InitializeOnLoad]
    internal static class LightVolumeEditorUpdater {
        private static readonly HashSet<LightVolumeManager> _managers = new HashSet<LightVolumeManager>();
        private static readonly HashSet<LightVolumeInstance> _volumes = new HashSet<LightVolumeInstance>();
        private static readonly HashSet<PointLightVolumeInstance> _pointLights = new HashSet<PointLightVolumeInstance>();
        private static readonly HashSet<GameObject> _hierarchyRoots = new HashSet<GameObject>();
        private static readonly List<LightVolumeManager> _managerBuffer = new List<LightVolumeManager>();
        private static readonly List<LightVolumeInstance> _volumeBuffer = new List<LightVolumeInstance>();
        private static readonly List<PointLightVolumeInstance> _pointLightBuffer = new List<PointLightVolumeInstance>();
        private static bool _refreshAllManagers;
        private static bool _flushQueued;
        private static bool _isFlushing;

        static LightVolumeEditorUpdater() {
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            LightVolumeManager.AtlasPostProcessorsChanged -= OnAtlasPostProcessorsChanged;
            LightVolumeManager.AtlasPostProcessorsChanged += OnAtlasPostProcessorsChanged;
        }

        private static void OnAtlasPostProcessorsChanged(LightVolumeManager manager) {
            if (manager == null) return;
            LVUtils.MarkDirty(manager);
            LightVolumeManagerTools.CopyProxyToUdon(manager);
            LightVolumeManagerTools.QueueRuntimeManagerRefresh(manager);
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream) {
            if (_isFlushing || EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < stream.length; i++) {
                ObjectChangeKind kind = stream.GetEventType(i);
                switch (kind) {
                    case ObjectChangeKind.ChangeScene:
                        QueueAllManagers();
                        break;
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                        stream.GetCreateGameObjectHierarchyEvent(i, out CreateGameObjectHierarchyEventArgs createData);
                        QueueHierarchy(GetGameObject(createData.instanceId));
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        stream.GetChangeGameObjectStructureEvent(i, out ChangeGameObjectStructureEventArgs structureData);
                        QueueHierarchy(GetGameObject(structureData.instanceId));
                        QueueAllManagers();
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out ChangeGameObjectStructureHierarchyEventArgs structureHierarchyData);
                        QueueHierarchy(GetGameObject(structureHierarchyData.instanceId));
                        QueueAllManagers();
                        break;
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out ChangeGameObjectOrComponentPropertiesEventArgs propertyData);
                        QueueObject(EditorUtility.InstanceIDToObject(propertyData.instanceId));
                        break;
                    case ObjectChangeKind.ChangeGameObjectParent:
                        stream.GetChangeGameObjectParentEvent(i, out ChangeGameObjectParentEventArgs parentData);
                        QueueHierarchy(GetGameObject(parentData.instanceId));
                        QueueHierarchy(GetGameObject(parentData.previousParentInstanceId));
                        QueueHierarchy(GetGameObject(parentData.newParentInstanceId));
                        break;
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                        stream.GetDestroyGameObjectHierarchyEvent(i, out DestroyGameObjectHierarchyEventArgs destroyData);
                        QueueHierarchy(GetGameObject(destroyData.parentInstanceId));
                        QueueAllManagers();
                        break;
                    case ObjectChangeKind.UpdatePrefabInstances:
                        stream.GetUpdatePrefabInstancesEvent(i, out UpdatePrefabInstancesEventArgs prefabData);
                        for (int instanceIndex = 0; instanceIndex < prefabData.instanceIds.Length; instanceIndex++)
                            QueueHierarchy(GetGameObject(prefabData.instanceIds[instanceIndex]));
                        QueueAllManagers();
                        break;
                }
            }
        }

        private static GameObject GetGameObject(int instanceId) {
            return EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        }

        private static void QueueObject(UnityEngine.Object changedObject) {
            if (changedObject == null) return;
            if (changedObject is GameObject gameObject) QueueHierarchy(gameObject);
            else if (changedObject is Transform transform) QueueHierarchy(transform.gameObject);
            else if (changedObject is LightVolumeManager manager) QueueManager(manager);
            else if (changedObject is LightVolumeInstance volume) QueueVolume(volume);
            else if (changedObject is PointLightVolumeInstance pointLight) QueuePointLight(pointLight);
        }

        private static void QueueHierarchy(GameObject root) {
            if (!IsEditableSceneObject(root) || !_hierarchyRoots.Add(root)) return;
            _managerBuffer.Clear();
            root.GetComponentsInChildren(true, _managerBuffer);
            _volumeBuffer.Clear();
            root.GetComponentsInChildren(true, _volumeBuffer);
            _pointLightBuffer.Clear();
            root.GetComponentsInChildren(true, _pointLightBuffer);
            bool hasRelevant = _managerBuffer.Count != 0 || _volumeBuffer.Count != 0 || _pointLightBuffer.Count != 0;
            if (!hasRelevant) {
                _hierarchyRoots.Remove(root);
                return;
            }
            for (int i = 0; i < _managerBuffer.Count; i++) QueueManager(_managerBuffer[i]);
            for (int i = 0; i < _volumeBuffer.Count; i++) QueueVolume(_volumeBuffer[i]);
            for (int i = 0; i < _pointLightBuffer.Count; i++) QueuePointLight(_pointLightBuffer[i]);
        }

        private static void QueueVolume(LightVolumeInstance volume) {
            if (!IsEditableSceneObject(volume)) return;
            _volumes.Add(volume);
            QueueManager(volume.LightVolumeManager);
        }

        private static void QueuePointLight(PointLightVolumeInstance pointLight) {
            if (!IsEditableSceneObject(pointLight)) return;
            _pointLights.Add(pointLight);
            QueueManager(pointLight.LightVolumeManager);
        }

        private static void QueueManager(LightVolumeManager manager) {
            if (IsEditableSceneObject(manager)) _managers.Add(manager);
            QueueFlush();
        }

        private static void QueueAllManagers() {
            _refreshAllManagers = true;
            QueueFlush();
        }

        private static void QueueFlush() {
            if (_flushQueued) return;
            _flushQueued = true;
            EditorApplication.delayCall += Flush;
        }
        // Applies the coalesced object-change batch before Scene View renders, avoiding one stale
        // camera frame while retaining delayCall as a fallback when no Scene View is visible.
        internal static void FlushPendingSceneChanges() {
            if (_flushQueued && !_isFlushing) Flush();
        }

        private static void Flush() {
            EditorApplication.delayCall -= Flush;
            _flushQueued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                Clear();
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || Undo.isProcessing) {
                QueueFlush();
                return;
            }

            _isFlushing = true;
            try {
                foreach (LightVolumeInstance volume in _volumes) {
                    if (!IsEditableSceneObject(volume)) continue;
                    LightVolumeTools.ApplyRuntimeState(volume, false);
                    volume.IsActive = volume.isActiveAndEnabled && volume.Intensity != 0f && volume.Color != Color.black;
                    LightVolumeManagerTools.CopyProxyToUdon(volume);
                }
                foreach (PointLightVolumeInstance pointLight in _pointLights) {
                    if (!IsEditableSceneObject(pointLight)) continue;
                    bool customTexturesChanged = pointLight.HasEditorCustomTextureChanges();
                    bool shadowTexturesChanged = pointLight.HasEditorShadowTextureChanges();
                    pointLight.EditorApplyAuthoringData(customTexturesChanged, shadowTexturesChanged, false);
                    pointLight.IsActive = pointLight.isActiveAndEnabled && pointLight.Intensity != 0f && pointLight.Color != Color.black;
                    LightVolumeManagerTools.CopyProxyToUdon(pointLight);
                }
                if (_refreshAllManagers) {
                    LightVolumeManager[] managers = UnityEngine.Object.FindObjectsByType<LightVolumeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    for (int i = 0; i < managers.Length; i++) {
                        if (IsEditableSceneObject(managers[i])) _managers.Add(managers[i]);
                    }
                }
                foreach (LightVolumeManager manager in _managers) {
                    if (IsEditableSceneObject(manager) && !manager.isActiveAndEnabled) manager.UpdateVolumes();
                }
                foreach (LightVolumeManager manager in _managers) {
                    if (IsEditableSceneObject(manager) && manager.isActiveAndEnabled) manager.UpdateVolumes();
                }
            } finally {
                Clear();
                _isFlushing = false;
            }
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        private static bool IsEditableSceneObject(Component component) {
            return component != null && IsEditableSceneObject(component.gameObject);
        }

        private static bool IsEditableSceneObject(GameObject gameObject) {
            return gameObject != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded && !EditorUtility.IsPersistent(gameObject);
        }

        private static void Clear() {
            _managers.Clear();
            _volumes.Clear();
            _pointLights.Clear();
            _hierarchyRoots.Clear();
            _managerBuffer.Clear();
            _volumeBuffer.Clear();
            _pointLightBuffer.Clear();
            _refreshAllManagers = false;
        }
    }
}
