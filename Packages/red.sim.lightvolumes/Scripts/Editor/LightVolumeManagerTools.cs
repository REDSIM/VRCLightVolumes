using System;
using System.Collections.Generic;
using System.IO;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
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

        private static readonly Dictionary<int, PostProcessor[]> _atlasPostProcessors = new Dictionary<int, PostProcessor[]>();
        private static readonly Dictionary<int, LightVolumeManager> _atlasPostProcessorOwners = new Dictionary<int, LightVolumeManager>();
        private static readonly Dictionary<int, EditorCoroutine> _atlasCoroutines = new Dictionary<int, EditorCoroutine>();
        private static readonly HashSet<LightVolumeManager> _queuedCustomProbeManagers = new HashSet<LightVolumeManager>();
        private static bool _customProbeFinalizeQueued;
        private static readonly HashSet<LightVolumeManager> _queuedAtlasManagers = new HashSet<LightVolumeManager>();
        private static bool _atlasGenerationQueued;

        // Applies target-dependent authoring values, optional texture-cache rebuilds and one final proxy/runtime sync.
        public static void ApplySettings(LightVolumeManager manager, bool markDirty = true, bool reinitializeCustomTextures = false, bool reinitializeShadowTextures = false, bool updateVolumes = true) {
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
            if (reinitializeCustomTextures) manager.ReinitializeCustomTextures();
            if (reinitializeShadowTextures) manager.ReinitializeShadowTextures();
            CopyProxyToUdon(manager);
            if (markDirty) LVUtils.MarkDirtyIfSerializedStateChanged(manager, previousState);
            if (updateVolumes) manager.UpdateVolumes();
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

        // Canonicalizes registry priority and synchronizes only metadata that actually changed.
        public static void SynchronizeRegistryMetadata(LightVolumeManager manager) {
            if (manager == null) return;

            LightVolumeInstance[] volumes = manager.LightVolumeInstances;
            SortLightVolumeRegistry(volumes);
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

        // Stable insertion sort keeps equal-weight authoring order and moves empty slots to the tail.
        private static void SortLightVolumeRegistry(LightVolumeInstance[] volumes) {
            for (int i = 1; i < volumes.Length; i++) {
                LightVolumeInstance volume = volumes[i];
                int insertIndex = i;
                while (insertIndex > 0 && ComesBefore(volume, volumes[insertIndex - 1])) {
                    volumes[insertIndex] = volumes[insertIndex - 1];
                    insertIndex--;
                }
                if (insertIndex != i) volumes[insertIndex] = volume;
            }
        }

        private static bool ComesBefore(LightVolumeInstance volume, LightVolumeInstance previous) {
            if (volume == null) return false;
            if (previous == null) return true;
            return volume.RegistryWeight > previous.RegistryWeight;
        }

        public static void ReinitializeCustomTextures(LightVolumeManager manager) {
            if (manager == null) return;
            manager.ReinitializeCustomTextures();
            CopyProxyToUdon(manager);
            manager.UpdateVolumes();
        }

        public static void ReinitializeShadowTextures(LightVolumeManager manager) {
            if (manager == null) return;
            manager.ReinitializeShadowTextures();
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

            TexturePackingStrategy strategy = HasPostProcessors(manager) ? TexturePackingStrategy.MinimumDepth : TexturePackingStrategy.MinimumVRAM;
            EditorCoroutine coroutine = EditorCoroutineUtility.StartCoroutine(Texture3DAtlasGenerator.CreateAtlas(volumes, atlas => CompleteAtlas(manager, volumes, atlas), manager.DownscaleVolumes, strategy), manager);
            _atlasCoroutines[managerId] = coroutine;
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
            LVUtils.MarkDirty(manager);
            CopyProxyToUdon(manager);
            manager.UpdateVolumes();
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
            if (texture == null) return;
            RegisterPostProcessor(manager, new PostProcessor {
                RT = texture,
                Mat = texture.material,
                TextureName = "_MainTex",
                Update = texture.Update
            });
        }

        public static void RegisterPostProcessor(this LightVolumeManager manager, PostProcessor processor) {
            if (manager == null || processor.RT == null || processor.Mat == null && processor.Update == null && processor.UpdateWithInput == null) return;
            if (string.IsNullOrEmpty(processor.TextureName)) processor.TextureName = "_MainTex";
            if (!TryGetAtlasPostProcessors(manager, out PostProcessor[] processors)) processors = Array.Empty<PostProcessor>();

            bool persistentChanged = UpsertPersistentPostProcessor(manager, processor, processors);
            bool transientChanged = UpsertTransientPostProcessor(ref processors, processor);
            if (transientChanged) SetAtlasPostProcessors(manager, processors);
            if (persistentChanged || transientChanged) RefreshAtlasOutput(manager, persistentChanged);
        }

        private static bool UpsertPersistentPostProcessor(LightVolumeManager manager, PostProcessor requested, PostProcessor[] transientProcessors) {
            RenderTexture[] targets = manager.AtlasPostProcessorTargets ?? Array.Empty<RenderTexture>();
            Material[] materials = manager.AtlasPostProcessorMaterials;
            string[] textureNames = manager.AtlasPostProcessorTextureNames;
            int matchCount = 0;
            int firstMatch = -1;
            for (int i = 0; i < targets.Length; i++) {
                if (!IsPersistentPostProcessorMatch(targets[i], requested, transientProcessors)) continue;
                if (firstMatch < 0) firstMatch = i;
                matchCount++;
            }

            bool arraysAligned = materials != null && materials.Length == targets.Length && textureNames != null && textureNames.Length == targets.Length;
            bool changed = matchCount != 1 || !arraysAligned;
            if (!changed) {
                changed = targets[firstMatch] != requested.RT || materials[firstMatch] != requested.Mat || textureNames[firstMatch] != requested.TextureName;
            }
            if (!changed) return false;

            int resultCount = targets.Length - matchCount + 1;
            RenderTexture[] resultTargets = new RenderTexture[resultCount];
            Material[] resultMaterials = new Material[resultCount];
            string[] resultNames = new string[resultCount];
            bool inserted = false;
            int write = 0;
            for (int i = 0; i < targets.Length; i++) {
                if (IsPersistentPostProcessorMatch(targets[i], requested, transientProcessors)) {
                    if (!inserted) {
                        resultTargets[write] = requested.RT;
                        resultMaterials[write] = requested.Mat;
                        resultNames[write] = requested.TextureName;
                        write++;
                        inserted = true;
                    }
                    continue;
                }
                resultTargets[write] = targets[i];
                if (materials != null && i < materials.Length) resultMaterials[write] = materials[i];
                resultNames[write] = textureNames != null && i < textureNames.Length && !string.IsNullOrEmpty(textureNames[i]) ? textureNames[i] : "_MainTex";
                write++;
            }
            if (!inserted) {
                resultTargets[write] = requested.RT;
                resultMaterials[write] = requested.Mat;
                resultNames[write] = requested.TextureName;
            }
            manager.AtlasPostProcessorTargets = resultTargets;
            manager.AtlasPostProcessorMaterials = resultMaterials;
            manager.AtlasPostProcessorTextureNames = resultNames;
            return true;
        }

        private static bool IsPersistentPostProcessorMatch(RenderTexture target, PostProcessor requested, PostProcessor[] transientProcessors) {
            if (target == requested.RT) return true;
            for (int i = 0; i < transientProcessors.Length; i++) {
                PostProcessor existing = transientProcessors[i];
                if (existing.RT == target && IsSamePostProcessor(existing, requested)) return true;
            }
            return false;
        }

        private static bool UpsertTransientPostProcessor(ref PostProcessor[] processors, PostProcessor requested) {
            int index = FindPostProcessor(processors, requested);
            if (index < 0) {
                Array.Resize(ref processors, processors.Length + 1);
                processors[processors.Length - 1] = requested;
                return true;
            }

            int duplicateCount = 0;
            for (int i = 0; i < processors.Length; i++)
                if (i != index && IsSamePostProcessor(processors[i], requested)) duplicateCount++;
            if (duplicateCount == 0 && HasSamePostProcessorValues(processors[index], requested)) return false;

            PostProcessor[] result = new PostProcessor[processors.Length - duplicateCount];
            for (int i = 0, write = 0; i < processors.Length; i++) {
                if (i == index) result[write++] = requested;
                else if (!IsSamePostProcessor(processors[i], requested)) result[write++] = processors[i];
            }
            processors = result;
            return true;
        }

        private static bool HasSamePostProcessorValues(PostProcessor first, PostProcessor second) {
            return first.RT == second.RT && first.Mat == second.Mat && first.TextureName == second.TextureName && first.Update == second.Update && first.UpdateWithInput == second.UpdateWithInput;
        }

        public static void UnregisterPostProcessorCRT(this LightVolumeManager manager, CustomRenderTexture texture) {
            UnregisterPostProcessor(manager, texture);
        }

        public static void UnregisterPostProcessor(this LightVolumeManager manager, RenderTexture texture) {
            if (manager == null || texture == null) return;
            UnregisterPostProcessor(manager, new PostProcessor { RT = texture });
        }

        public static void UnregisterPostProcessor(this LightVolumeManager manager, PostProcessor processor) {
            if (manager == null || processor.RT == null && processor.Update == null && processor.UpdateWithInput == null) return;
            int id = manager.GetInstanceID();
            TryGetAtlasPostProcessors(manager, out PostProcessor[] processors);
            int targetCapacity = (processors != null ? processors.Length : 0) + 1;
            RenderTexture[] removalTargets = new RenderTexture[targetCapacity];
            int removalTargetCount = 0;
            AddUniqueRenderTarget(removalTargets, ref removalTargetCount, processor.RT);
            if (processors != null) {
                for (int i = 0; i < processors.Length; i++) {
                    if (IsSamePostProcessor(processors[i], processor))
                        AddUniqueRenderTarget(removalTargets, ref removalTargetCount, processors[i].RT);
                }
            }
            bool persistentChanged = RemovePersistentPostProcessors(manager, removalTargets, removalTargetCount);
            bool transientChanged = false;
            if (processors != null) {
                int removeCount = 0;
                for (int i = 0; i < processors.Length; i++) if (IsSamePostProcessor(processors[i], processor)) removeCount++;
                if (removeCount > 0) {
                    PostProcessor[] remaining = new PostProcessor[processors.Length - removeCount];
                    for (int i = 0, write = 0; i < processors.Length; i++) {
                        if (IsSamePostProcessor(processors[i], processor)) continue;
                        remaining[write++] = processors[i];
                    }
                    if (remaining.Length == 0) RemoveAtlasPostProcessors(id);
                    else SetAtlasPostProcessors(manager, remaining);
                    transientChanged = true;
                }
            }
            if (persistentChanged || transientChanged) RefreshAtlasOutput(manager, persistentChanged);
        }

        private static void AddUniqueRenderTarget(RenderTexture[] targets, ref int count, RenderTexture target) {
            if (target == null) return;
            for (int i = 0; i < count; i++) if (targets[i] == target) return;
            targets[count++] = target;
        }

        private static bool RemovePersistentPostProcessors(LightVolumeManager manager, RenderTexture[] removalTargets, int removalTargetCount) {
            if (removalTargetCount == 0) return false;
            RenderTexture[] targets = manager.AtlasPostProcessorTargets;
            if (targets == null || targets.Length == 0) return false;
            int removeCount = 0;
            for (int i = 0; i < targets.Length; i++)
                if (ContainsRenderTarget(removalTargets, removalTargetCount, targets[i])) removeCount++;
            if (removeCount == 0) return false;

            Material[] materials = manager.AtlasPostProcessorMaterials;
            string[] textureNames = manager.AtlasPostProcessorTextureNames;
            int remainingCount = targets.Length - removeCount;
            RenderTexture[] remainingTargets = new RenderTexture[remainingCount];
            Material[] remainingMaterials = new Material[remainingCount];
            string[] remainingNames = new string[remainingCount];
            for (int i = 0, write = 0; i < targets.Length; i++) {
                if (ContainsRenderTarget(removalTargets, removalTargetCount, targets[i])) continue;
                remainingTargets[write] = targets[i];
                if (materials != null && i < materials.Length) remainingMaterials[write] = materials[i];
                remainingNames[write] = textureNames != null && i < textureNames.Length && !string.IsNullOrEmpty(textureNames[i]) ? textureNames[i] : "_MainTex";
                write++;
            }
            manager.AtlasPostProcessorTargets = remainingTargets;
            manager.AtlasPostProcessorMaterials = remainingMaterials;
            manager.AtlasPostProcessorTextureNames = remainingNames;
            return true;
        }

        private static bool ContainsRenderTarget(RenderTexture[] targets, int count, RenderTexture target) {
            for (int i = 0; i < count; i++) if (targets[i] == target) return true;
            return false;
        }

        private static int FindPostProcessor(PostProcessor[] processors, PostProcessor requested) {
            for (int i = 0; i < processors.Length; i++) {
                if (IsSamePostProcessor(processors[i], requested)) return i;
            }
            return -1;
        }

        private static bool IsSamePostProcessor(PostProcessor existing, PostProcessor requested) {
            return requested.RT != null && existing.RT == requested.RT || requested.Update != null && existing.Update == requested.Update || requested.UpdateWithInput != null && existing.UpdateWithInput == requested.UpdateWithInput;
        }

        private static bool TryGetAtlasPostProcessors(LightVolumeManager manager, out PostProcessor[] processors) {
            processors = null;
            if (manager == null) return false;
            int id = manager.GetInstanceID();
            if (!_atlasPostProcessorOwners.TryGetValue(id, out LightVolumeManager owner) || owner != manager) {
                RemoveAtlasPostProcessors(id);
                return false;
            }
            if (_atlasPostProcessors.TryGetValue(id, out processors)) return true;
            _atlasPostProcessorOwners.Remove(id);
            return false;
        }

        private static void SetAtlasPostProcessors(LightVolumeManager manager, PostProcessor[] processors) {
            int id = manager.GetInstanceID();
            _atlasPostProcessorOwners[id] = manager;
            _atlasPostProcessors[id] = processors;
        }

        private static void RemoveAtlasPostProcessors(int id) {
            _atlasPostProcessors.Remove(id);
            _atlasPostProcessorOwners.Remove(id);
        }

        private static bool HasPostProcessors(LightVolumeManager manager) {
            if (manager == null) return false;
            if (manager.AtlasPostProcessorTargets != null && manager.AtlasPostProcessorTargets.Length > 0) return true;
            return TryGetAtlasPostProcessors(manager, out PostProcessor[] processors) && processors.Length > 0;
        }

        private static void RefreshAtlasOutput(LightVolumeManager manager, bool serializedStateChanged = false) {
            if (manager == null) return;
            Texture output = manager.LightVolumeAtlasBase;
            TryGetAtlasPostProcessors(manager, out PostProcessor[] processors);
            bool[] transientProcessorUsed = processors != null ? new bool[processors.Length] : null;
            RenderTexture[] persistentTargets = manager.AtlasPostProcessorTargets;
            Material[] persistentMaterials = manager.AtlasPostProcessorMaterials;
            string[] persistentNames = manager.AtlasPostProcessorTextureNames;
            int persistentCount = persistentTargets != null && persistentMaterials != null ? Mathf.Min(persistentTargets.Length, persistentMaterials.Length) : 0;
            for (int i = 0; output != null && i < persistentCount; i++) {
                RenderTexture target = persistentTargets[i];
                Material material = persistentMaterials[i];
                if (target == null) continue;
                int transientIndex = processors != null ? FindPostProcessor(processors, new PostProcessor { RT = target }) : -1;
                if (transientIndex >= 0) {
                    transientProcessorUsed[transientIndex] = ApplyPostProcessor(ref output, processors[transientIndex]);
                    continue;
                }
                if (material == null) continue;
                string textureName = persistentNames != null && i < persistentNames.Length && !string.IsNullOrEmpty(persistentNames[i]) ? persistentNames[i] : "_MainTex";
                ApplyPostProcessor(ref output, new PostProcessor {
                    RT = target,
                    Mat = material,
                    TextureName = textureName,
                    Update = target is CustomRenderTexture customTarget ? (Action)customTarget.Update : null
                });
            }

            if (processors != null) {
                for (int i = 0; i < processors.Length; i++) {
                    if (transientProcessorUsed[i]) continue;
                    ApplyPostProcessor(ref output, processors[i]);
                }
            }
            bool atlasChanged = manager.LightVolumeAtlas != output;
            if (!atlasChanged && !serializedStateChanged) return;
            if (atlasChanged) manager.LightVolumeAtlas = output;
            LVUtils.MarkDirty(manager);
            CopyProxyToUdon(manager);
            if (atlasChanged) manager.UpdateVolumes();
        }

        private static bool ApplyPostProcessor(ref Texture output, PostProcessor processor) {
            if (output == null || processor.RT == null || processor.Mat == null && processor.Update == null && processor.UpdateWithInput == null) return false;
            SetupPostProcessorTexture(processor.RT, output);
            string textureName = string.IsNullOrEmpty(processor.TextureName) ? "_MainTex" : processor.TextureName;
            if (processor.Mat != null) processor.Mat.SetTexture(textureName, output);
            Texture input = output;
            output = processor.RT;
            if (processor.UpdateWithInput != null) processor.UpdateWithInput(input);
            else processor.Update?.Invoke();
            return true;
        }

        private static void SetupPostProcessorTexture(RenderTexture texture, Texture source) {
            RenderTexture.active = null;
            texture.Release();
            texture.dimension = TextureDimension.Tex3D;
            texture.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            texture.enableRandomWrite = false;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 0;
            texture.width = Mathf.Max(source.width, 1);
            texture.height = Mathf.Max(source.height, 1);
            texture.volumeDepth = source is Texture3D texture3D ? Mathf.Max(texture3D.depth, 1) : source is RenderTexture renderTexture ? Mathf.Max(renderTexture.volumeDepth, 1) : 1;
            if (texture is CustomRenderTexture customTexture) customTexture.updateMode = CustomRenderTextureUpdateMode.Realtime;
            texture.Create();
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

        internal static bool RequiresFullManagerRefresh(ObjectChangeKind kind) {
            switch (kind) {
                case ObjectChangeKind.ChangeScene:
                case ObjectChangeKind.ChangeGameObjectStructure:
                case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                case ObjectChangeKind.DestroyGameObjectHierarchy:
                case ObjectChangeKind.UpdatePrefabInstances:
                    return true;
                default:
                    return false;
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
