using UnityEngine;
using System;

namespace VRCLightVolumes {
    public partial class LightVolumeManager {
#region Runtime Registries

        // Removes stale serialized registry slots left by older manager versions. New runtime deinitialization keeps both registries dense, so this normally exits without allocating.
        public bool SanitizeRegistries() {
            bool changed = false;

            if (LightVolumeInstances == null) {
                LightVolumeInstances = new LightVolumeInstance[0];
                changed = true;
            } else {
                int count = LightVolumeInstances.Length;
                int validCount = 0;
                for (int i = 0; i < count; i++) {
                    if (LightVolumeInstances[i] != null) validCount++;
                }
                if (validCount != count) {
                    LightVolumeInstance[] targetArray = new LightVolumeInstance[validCount];
                    int targetIndex = 0;
                    for (int i = 0; i < count; i++) {
                        LightVolumeInstance instance = LightVolumeInstances[i];
                        if (instance == null) continue;
                        targetArray[targetIndex++] = instance;
                    }
                    LightVolumeInstances = targetArray;
                    changed = true;
                }
            }

            bool pointLightRegistryChanged = false;
            if (PointLightVolumeInstances == null) {
                PointLightVolumeInstances = new PointLightVolumeInstance[0];
                pointLightRegistryChanged = true;
            } else {
                int count = PointLightVolumeInstances.Length;
                int validCount = 0;
                for (int i = 0; i < count; i++) {
                    if (PointLightVolumeInstances[i] != null) validCount++;
                }
                if (validCount != count) {
                    PointLightVolumeInstance[] targetArray = new PointLightVolumeInstance[validCount];
                    int targetIndex = 0;
                    for (int i = 0; i < count; i++) {
                        PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                        if (instance == null) continue;
                        targetArray[targetIndex++] = instance;
                    }
                    PointLightVolumeInstances = targetArray;
                    pointLightRegistryChanged = true;
                }
            }

            if (pointLightRegistryChanged) {
                changed = true;
                InvalidateTextureCaches(_customTextureArrayDepth > 0, _shadowTextureArrayDepth > 0);
            }
            EnsureLightVolumeSelectionCapacity(LightVolumeInstances.Length);
            return changed;
        }

        // Uses the stable authoring order as an O(1) hint and falls back after runtime compaction.
        private int FindLightVolumeRegistryIndex(LightVolumeInstance lightVolume) {
            if (lightVolume == null || LightVolumeInstances == null) return -1;
            int count = LightVolumeInstances.Length;
            int hint = lightVolume.RegistryOrder;
            if (hint >= 0 && hint < count && LightVolumeInstances[hint] == lightVolume) return hint;
            return Array.IndexOf((Array)LightVolumeInstances, lightVolume, 0, count);
        }

        // Uses the stable authoring order as an O(1) hint and falls back after runtime reordering.
        private int FindPointLightRegistryIndex(PointLightVolumeInstance pointLightVolume) {
            if (pointLightVolume == null || PointLightVolumeInstances == null) return -1;
            int count = PointLightVolumeInstances.Length;
            int hint = pointLightVolume.RegistryOrder;
            if (hint >= 0 && hint < count && PointLightVolumeInstances[hint] == pointLightVolume) return hint;
            return Array.IndexOf((Array)PointLightVolumeInstances, pointLightVolume, 0, count);
        }

        // Appends one Start-time Bake In Game request to the runtime queue.
        public void EnqueueBakeInGameLight(PointLightVolumeInstance pointLightVolume) {
            if (pointLightVolume == null) return;
            if (_bakeInGameQueueCount >= MaxPointLightCount) {
                Debug.LogWarning("[LightVolumes] Bake In Game queue is full. The light will not be baked.");
                return;
            }

            int tail = _bakeInGameQueueHead + _bakeInGameQueueCount;
            if (tail >= MaxPointLightCount) tail -= MaxPointLightCount;
            _bakeInGameQueue[tail] = pointLightVolume;
            _bakeInGameQueueCount++;
            ScheduleUpdateProcess();
        }

        // Initializes a Light Volume by adding it to the light volume registry. Called automatically at runtime when the object spawns
        public void InitializeLightVolume(LightVolumeInstance lightVolume) {
            if (lightVolume == null) return;
            if (LightVolumeInstances == null) LightVolumeInstances = new LightVolumeInstance[0];
            int count = LightVolumeInstances.Length;
            int existingIndex = -1;
            if (lightVolume.RegistryOrder != DefaultRegistryOrder) {
                existingIndex = FindLightVolumeRegistryIndex(lightVolume);
                if (existingIndex >= 0) {
                    lightVolume.LightVolumeManager = this;
                    RequestUpdateVolumes();
                    return;
                }
            }
            int nextRegistryOrder = -1;
            for (int i = 0; i < count; i++) {
                LightVolumeInstance existingLightVolume = LightVolumeInstances[i];
                if (existingLightVolume == null) continue;
                if (existingLightVolume.RegistryOrder == DefaultRegistryOrder) existingLightVolume.RegistryOrder = i;
                if (existingLightVolume.RegistryOrder > nextRegistryOrder) nextRegistryOrder = existingLightVolume.RegistryOrder;
                if (existingLightVolume == lightVolume) existingIndex = i;
            }
            if (lightVolume.RegistryOrder == DefaultRegistryOrder) lightVolume.RegistryOrder = nextRegistryOrder + 1;

            // Reuse an existing slot so repeated OnEnable calls do not duplicate the same volume
            if (existingIndex >= 0) {
                lightVolume.LightVolumeManager = this;
                RequestUpdateVolumes();
                return;
            }
            // Keep the runtime registry in stable authoring order; shader priority is resolved separately.
            int targetOrder = lightVolume.RegistryOrder;
            int firstEmptyIndex = -1;
            int lastFilledIndex = -1;
            int insertIndex = count;
            for (int i = 0; i < count; i++) {
                LightVolumeInstance existingLightVolume = LightVolumeInstances[i];
                if (existingLightVolume == null) {
                    if (firstEmptyIndex < 0) firstEmptyIndex = i;
                    continue;
                }
                lastFilledIndex = i;
                if (insertIndex == count && existingLightVolume.RegistryOrder > targetOrder) insertIndex = i;
            }
            if (firstEmptyIndex >= 0) {
                if (insertIndex == count) {
                    if (firstEmptyIndex < lastFilledIndex) {
                        int shiftCount = lastFilledIndex - firstEmptyIndex;
                        Array.Copy(LightVolumeInstances, firstEmptyIndex + 1, LightVolumeInstances, firstEmptyIndex, shiftCount);
                        LightVolumeInstances[lastFilledIndex] = lightVolume;
                    } else {
                        LightVolumeInstances[firstEmptyIndex] = lightVolume;
                    }
                } else if (firstEmptyIndex < insertIndex) {
                    int shiftCount = insertIndex - firstEmptyIndex - 1;
                    if (shiftCount > 0) Array.Copy(LightVolumeInstances, firstEmptyIndex + 1, LightVolumeInstances, firstEmptyIndex, shiftCount);
                    LightVolumeInstances[insertIndex - 1] = lightVolume;
                } else {
                    int shiftCount = firstEmptyIndex - insertIndex;
                    if (shiftCount > 0) Array.Copy(LightVolumeInstances, insertIndex, LightVolumeInstances, insertIndex + 1, shiftCount);
                    LightVolumeInstances[insertIndex] = lightVolume;
                }
                lightVolume.LightVolumeManager = this;
                RequestUpdateVolumes();
                return;
            }
            // No empty slot exists, so grow the registry array and insert by stable authoring order.
            LightVolumeInstance[] targetArray = new LightVolumeInstance[count + 1];
            if (insertIndex > 0) Array.Copy(LightVolumeInstances, 0, targetArray, 0, insertIndex);
            targetArray[insertIndex] = lightVolume;
            int suffixCount = count - insertIndex;
            if (suffixCount > 0) Array.Copy(LightVolumeInstances, insertIndex, targetArray, insertIndex + 1, suffixCount);
            lightVolume.LightVolumeManager = this;
            LightVolumeInstances = targetArray;
            RequestUpdateVolumes();
        }

        // Deinitializes a Light Volume and keeps the serialized registry dense.
        public void DeinitializeLightVolume(LightVolumeInstance lightVolume) {
            if (lightVolume == null || LightVolumeInstances == null) return;
            int index = FindLightVolumeRegistryIndex(lightVolume);
            if (index < 0) return;
            int count = LightVolumeInstances.Length;
            LightVolumeInstance[] targetArray = new LightVolumeInstance[count - 1];
            if (index > 0) Array.Copy(LightVolumeInstances, 0, targetArray, 0, index);
            int suffixCount = count - index - 1;
            if (suffixCount > 0) Array.Copy(LightVolumeInstances, index + 1, targetArray, index, suffixCount);
            LightVolumeInstances = targetArray;
            if (enabled && gameObject.activeInHierarchy) RequestUpdateVolumes();
        }

        // Refreshes shader selection after a runtime weight change without reordering the registry.
        public void ReorderLightVolume(LightVolumeInstance lightVolume) {
            if (lightVolume == null) return;
            int index = FindLightVolumeRegistryIndex(lightVolume);
            if (index < 0) {
                if (lightVolume.IsActive) InitializeLightVolume(lightVolume);
                return;
            }
            RequestUpdateVolumes();
        }

        // Initializes a Point Light Volume by adding it to the point light volume registry
        public void InitializePointLightVolume(PointLightVolumeInstance pointLightVolume) {
            if (pointLightVolume == null) return;
#if !COMPILER_UDONSHARP
            if (RuntimeShadowCamera == null && pointLightVolume.BakeInGame) EnsureRuntimeShadowCamera();
#endif
            if (pointLightVolume.RuntimeShadowCamera == null) pointLightVolume.RuntimeShadowCamera = RuntimeShadowCamera;
            if (pointLightVolume.RuntimeShadowDepthEncodeMaterial == null && RuntimeShadowDepthEncodeMaterial != null) pointLightVolume.RuntimeShadowDepthEncodeMaterial = RuntimeShadowDepthEncodeMaterial;
            if (pointLightVolume.RuntimeShadowBlurMaterial == null && RuntimeShadowBlurMaterial != null) pointLightVolume.RuntimeShadowBlurMaterial = RuntimeShadowBlurMaterial;
            if (PointLightVolumeInstances == null) PointLightVolumeInstances = new PointLightVolumeInstance[0];
            int count = PointLightVolumeInstances.Length;
            bool invalidateCustomTextures = _customTexturesInitialized && pointLightVolume.IsActive && (pointLightVolume.CustomTexture != null || pointLightVolume.CustomTextureMaterial != null);
            bool invalidateShadowTextures = _shadowTexturesInitialized && pointLightVolume.IsActive && (pointLightVolume.ShadowMapTexture != null || pointLightVolume.ShadowMapMaterial != null || pointLightVolume.ShadowMapID >= 0);
            int existingIndex = -1;
            if (pointLightVolume.RegistryOrder != DefaultRegistryOrder) {
                existingIndex = FindPointLightRegistryIndex(pointLightVolume);
                if (existingIndex >= 0) {
                    pointLightVolume.LightVolumeManager = this;
                    InvalidateTextureCaches(invalidateCustomTextures, invalidateShadowTextures);
                    RequestUpdateVolumes();
                    return;
                }
            }
            int nextRegistryOrder = -1;
            for (int i = 0; i < count; i++) {
                PointLightVolumeInstance existingPointLightVolume = PointLightVolumeInstances[i];
                if (existingPointLightVolume == null) continue;
                if (existingPointLightVolume.RegistryOrder == DefaultRegistryOrder) existingPointLightVolume.RegistryOrder = i;
                if (existingPointLightVolume.RegistryOrder > nextRegistryOrder) nextRegistryOrder = existingPointLightVolume.RegistryOrder;
                if (existingPointLightVolume == pointLightVolume) existingIndex = i;
            }
            if (pointLightVolume.RegistryOrder == DefaultRegistryOrder) pointLightVolume.RegistryOrder = nextRegistryOrder + 1;

            // Reuse an existing slot so repeated OnEnable calls do not duplicate the same point light
            if (existingIndex >= 0) {
                pointLightVolume.LightVolumeManager = this;
                InvalidateTextureCaches(invalidateCustomTextures, invalidateShadowTextures);
                RequestUpdateVolumes();
                return;
            }
            // Insert by weight first and stable registry order second so enable/disable history does not change shader priority
            float targetWeight = pointLightVolume.RegistryWeight;
            int targetOrder = pointLightVolume.RegistryOrder;
            int firstEmptyIndex = -1;
            int lastFilledIndex = -1;
            int insertIndex = count;
            bool registryIndicesChanged = false;
            for (int i = 0; i < count; i++) {
                PointLightVolumeInstance existingPointLightVolume = PointLightVolumeInstances[i];
                if (existingPointLightVolume == null) {
                    if (firstEmptyIndex < 0) firstEmptyIndex = i;
                    continue;
                }
                lastFilledIndex = i;
                if (insertIndex == count && (existingPointLightVolume.RegistryWeight < targetWeight || existingPointLightVolume.RegistryWeight == targetWeight && existingPointLightVolume.RegistryOrder > targetOrder)) insertIndex = i;
            }
            if (firstEmptyIndex >= 0) {
                if (insertIndex == count) {
                    if (firstEmptyIndex < lastFilledIndex) {
                        registryIndicesChanged = true;
                        int shiftCount = lastFilledIndex - firstEmptyIndex;
                        Array.Copy(PointLightVolumeInstances, firstEmptyIndex + 1, PointLightVolumeInstances, firstEmptyIndex, shiftCount);
                        PointLightVolumeInstances[lastFilledIndex] = pointLightVolume;
                    } else {
                        PointLightVolumeInstances[firstEmptyIndex] = pointLightVolume;
                    }
                } else if (firstEmptyIndex < insertIndex) {
                    if (firstEmptyIndex < insertIndex - 1) registryIndicesChanged = true;
                    int shiftCount = insertIndex - firstEmptyIndex - 1;
                    if (shiftCount > 0) Array.Copy(PointLightVolumeInstances, firstEmptyIndex + 1, PointLightVolumeInstances, firstEmptyIndex, shiftCount);
                    PointLightVolumeInstances[insertIndex - 1] = pointLightVolume;
                } else {
                    registryIndicesChanged = true;
                    int shiftCount = firstEmptyIndex - insertIndex;
                    if (shiftCount > 0) Array.Copy(PointLightVolumeInstances, insertIndex, PointLightVolumeInstances, insertIndex + 1, shiftCount);
                    PointLightVolumeInstances[insertIndex] = pointLightVolume;
                }
                pointLightVolume.LightVolumeManager = this;
                if (registryIndicesChanged) {
                    if (_customTextureArrayDepth > 0) invalidateCustomTextures = true;
                    if (_shadowTextureArrayDepth > 0) invalidateShadowTextures = true;
                }
                InvalidateTextureCaches(invalidateCustomTextures, invalidateShadowTextures);
                RequestUpdateVolumes();
                return;
            }
            // No empty slot exists, so grow the registry array and insert by weight and stable order
            PointLightVolumeInstance[] targetArray = new PointLightVolumeInstance[count + 1];
            if (insertIndex > 0) Array.Copy(PointLightVolumeInstances, 0, targetArray, 0, insertIndex);
            targetArray[insertIndex] = pointLightVolume;
            int suffixCount = count - insertIndex;
            if (suffixCount > 0) Array.Copy(PointLightVolumeInstances, insertIndex, targetArray, insertIndex + 1, suffixCount);
            pointLightVolume.LightVolumeManager = this;
            PointLightVolumeInstances = targetArray;
            if (insertIndex < count) {
                if (_customTextureArrayDepth > 0) invalidateCustomTextures = true;
                if (_shadowTextureArrayDepth > 0) invalidateShadowTextures = true;
            }
            InvalidateTextureCaches(invalidateCustomTextures, invalidateShadowTextures);
            RequestUpdateVolumes();
        }

        // Deinitializes a Point Light Volume and keeps the serialized registry dense.
        public void DeinitializePointLightVolume(PointLightVolumeInstance pointLightVolume, bool customTexturesChanged, bool shadowTexturesChanged) {
            if (pointLightVolume == null || PointLightVolumeInstances == null) return;
            int index = FindPointLightRegistryIndex(pointLightVolume);
            if (index < 0) return;
            if (pointLightVolume.AreaCookieAverageReadbackPending) {
                pointLightVolume.AreaCookieAverageCustomId = -1;
                pointLightVolume.AreaCookieAverageReadbackDirty = false;
            }
            if (pointLightVolume.LightType == 2 && pointLightVolume.ProjectionMode == 2 && (pointLightVolume.CustomTexture != null || pointLightVolume.CustomTextureMaterial != null)) {
                pointLightVolume.AreaLightFallbackColor = index < _pointLightAreaCookieAverageColors.Length ? _pointLightAreaCookieAverageColors[index] : Color.clear;
            }
            int count = PointLightVolumeInstances.Length;
            PointLightVolumeInstance[] targetArray = new PointLightVolumeInstance[count - 1];
            if (index > 0) Array.Copy(PointLightVolumeInstances, 0, targetArray, 0, index);
            int suffixCount = count - index - 1;
            if (suffixCount > 0) Array.Copy(PointLightVolumeInstances, index + 1, targetArray, index, suffixCount);
            PointLightVolumeInstances = targetArray;
            if (index < count - 1) {
                if (_customTextureArrayDepth > 0) customTexturesChanged = true;
                if (_shadowTextureArrayDepth > 0) shadowTexturesChanged = true;
            }
            InvalidateTextureCaches(customTexturesChanged, shadowTexturesChanged);
            if (enabled && gameObject.activeInHierarchy) RequestUpdateVolumes();
        }

        // Repositions a registered Point Light Volume after its runtime sort weight changes
        public void ReorderPointLightVolume(PointLightVolumeInstance pointLightVolume) {
            if (pointLightVolume == null) return;
            int index = FindPointLightRegistryIndex(pointLightVolume);
            if (index < 0) {
                if (pointLightVolume.IsActive) InitializePointLightVolume(pointLightVolume);
                return;
            }
            PointLightVolumeInstances[index] = null;
            InvalidateTextureCaches(_customTextureArrayDepth > 0, _shadowTextureArrayDepth > 0);
            InitializePointLightVolume(pointLightVolume);
        }

#endregion
    }
}
