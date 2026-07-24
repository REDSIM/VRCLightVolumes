using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {
    public static class LightVolumeTools {
        public static void ResetFromParentReflectionProbe(LightVolumeInstance volume) {
            if (volume == null || volume.transform.parent == null) return;
            if (!volume.transform.parent.TryGetComponent(out ReflectionProbe probe)) return;

            Undo.RecordObject(volume.transform, "Initialize Light Volume Bounds");
            volume.transform.SetPositionAndRotation(probe.bounds.center, Quaternion.identity);
            LVUtils.SetLossyScale(volume.transform, probe.bounds.size);
            ApplyRuntimeState(volume, true);
        }

        public static Vector3 GetPosition(LightVolumeInstance volume) {
            return volume == null ? Vector3.zero : volume.transform.position;
        }

        public static Vector3 GetScale(LightVolumeInstance volume) {
            return volume == null ? Vector3.zero : volume.transform.lossyScale;
        }

        public static Quaternion GetRotation(LightVolumeInstance volume) {
            if (volume == null) return Quaternion.identity;

            LightVolumeManager manager = volume.LightVolumeManager;
            if (manager == null || !manager.IsBakeryMode || Application.isPlaying || !volume.Bake) return volume.transform.rotation;

#if BAKERY_INCLUDED
            if (typeof(BakeryVolume).GetField("_rotateAroundXYZ") != null) return volume.transform.rotation;
            if (typeof(BakeryVolume).GetField("rotateAroundY") != null) return Quaternion.Euler(0f, volume.transform.rotation.eulerAngles.y, 0f);
#endif
            return Quaternion.identity;
        }

        public static Matrix4x4 GetMatrixTRS(LightVolumeInstance volume) {
            return Matrix4x4.TRS(GetPosition(volume), GetRotation(volume), GetScale(volume));
        }

        public static int GetVoxelCount(LightVolumeInstance volume, int padding = 0) {
            return volume == null ? -1 : GetVoxelCount(volume.Resolution, padding);
        }

        public static int GetVoxelCount(Vector3Int resolution, int padding = 0) {
            long width = (long)resolution.x + padding * 2L;
            long height = (long)resolution.y + padding * 2L;
            long depth = (long)resolution.z + padding * 2L;
            if (width <= 0L || height <= 0L || depth <= 0L || width > int.MaxValue / height) return -1;

            long sliceSize = width * height;
            return sliceSize > int.MaxValue / depth ? -1 : (int)(sliceSize * depth);
        }

        public static bool RecalculateAdaptiveResolution(LightVolumeInstance volume) {
            if (volume == null) return false;

            Vector3 scale = GetScale(volume);
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 count = scale * Mathf.Max(volume.VoxelsPerUnit, 0f);
            Vector3Int resolution = new Vector3Int(Mathf.Max(Mathf.RoundToInt(count.x), 1), Mathf.Max(Mathf.RoundToInt(count.y), 1), Mathf.Max(Mathf.RoundToInt(count.z), 1));
            if (volume.Resolution == resolution) return false;

            volume.Resolution = resolution;
            return true;
        }

        public static bool Recalculate(LightVolumeInstance volume) {
            return volume != null && volume.AdaptiveResolution && RecalculateAdaptiveResolution(volume);
        }

        public static bool ApplyRuntimeState(LightVolumeInstance volume, bool notifyManager) {
            if (volume == null) return false;

            bool changed = Recalculate(volume);
            Vector3 scale = volume.transform.lossyScale;
            float safeRadius = Mathf.Max(volume.SmoothBlending, 0.00001f);
            Vector4 edgeSmoothing = new Vector4(scale.x / safeRadius, scale.y / safeRadius, scale.z / safeRadius, 0f);
            Quaternion transformRotation = volume.transform.rotation;
            Matrix4x4 inverseWorld = Matrix4x4.TRS(volume.transform.position, transformRotation, scale).inverse;
            Quaternion relativeRotation = transformRotation * volume.InvBakedRotation;
            bool isRotated = Quaternion.Dot(relativeRotation, Quaternion.identity) < 0.999999f;
            Matrix4x4 rotationMatrix = Matrix4x4.Rotate(relativeRotation);
            Vector3 rotationRow0 = rotationMatrix.GetRow(0);
            Vector3 rotationRow1 = rotationMatrix.GetRow(1);

            if (volume.InvLocalEdgeSmoothing != edgeSmoothing) {
                volume.InvLocalEdgeSmoothing = edgeSmoothing;
                changed = true;
            }
            if (volume.InvWorldMatrix != inverseWorld) {
                volume.InvWorldMatrix = inverseWorld;
                changed = true;
            }
            if (volume.RelativeRotationRow0 != rotationRow0) {
                volume.RelativeRotationRow0 = rotationRow0;
                changed = true;
            }
            if (volume.RelativeRotationRow1 != rotationRow1) {
                volume.RelativeRotationRow1 = rotationRow1;
                changed = true;
            }
            if (volume.IsRotated != isRotated) {
                volume.IsRotated = isRotated;
                changed = true;
            }

            bool isActive = volume.gameObject.activeInHierarchy && volume.Intensity != 0f && volume.Color != Color.black;
            if (volume.IsActive != isActive) {
                volume.IsActive = isActive;
                changed = true;
            }

            if (notifyManager && volume.LightVolumeManager != null && volume.gameObject.activeInHierarchy) volume.LightVolumeManager.NotifyLightVolumeChanged(volume, true);
            return changed;
        }

        public static bool TryCalculateProbePositions(LightVolumeInstance volume, Vector3Int resolution, out Vector3[] positions) {
            int voxelCount = GetVoxelCount(resolution);
            if (volume == null || voxelCount < 0) {
                positions = new Vector3[0];
                if (volume != null) Debug.LogError($"[LightVolume] Can't calculate probes for light volume {volume.gameObject.name}. Resolution is invalid or the voxel count is too large!", volume);
                return false;
            }

            positions = new Vector3[voxelCount];
            Vector3 offset = new Vector3(0.5f, 0.5f, 0.5f);
            Vector3 position = GetPosition(volume);
            Quaternion rotation = GetRotation(volume);
            Vector3 scale = GetScale(volume);
            int index = 0;
            for (int z = 0; z < resolution.z; z++) {
                for (int y = 0; y < resolution.y; y++) {
                    for (int x = 0; x < resolution.x; x++) {
                        Vector3 localPosition = new Vector3((x + 0.5f) / resolution.x, (y + 0.5f) / resolution.y, (z + 0.5f) / resolution.z) - offset;
                        positions[index++] = LVUtils.TransformPoint(localPosition, position, rotation, scale);
                    }
                }
            }
            return true;
        }

        public static Vector3[] GetCustomProbes(LightVolumeInstance volume) {
            if (volume == null) return new Vector3[0];
            Recalculate(volume);
            TryCalculateProbePositions(volume, volume.Resolution, out Vector3[] positions);
            return positions;
        }

        public static void SetupBakeryDependencies(LightVolumeInstance volume, bool createIfMissing = true) {
#if BAKERY_INCLUDED
            if (volume == null || volume.LightVolumeManager == null) return;

            LightVolumeManager manager = volume.LightVolumeManager;
            if (!TryFindOwnedBakeryVolume(volume, out BakeryVolume bakeryVolume)) return;
            if (manager.IsBakeryMode && volume.Bake && bakeryVolume == null) {
                if (!createIfMissing) return;
                GameObject helper = new GameObject($"Bakery Volume - {volume.gameObject.name}") { tag = "EditorOnly" };
                Undo.RegisterCreatedObjectUndo(helper, "Create Bakery Volume");
                helper.transform.SetParent(volume.transform, false);
                bakeryVolume = helper.AddComponent<BakeryVolume>();
            } else if ((!manager.IsBakeryMode || !volume.Bake) && bakeryVolume != null) {
                Object target = PrefabUtility.IsPartOfPrefabInstance(bakeryVolume.gameObject) ? bakeryVolume : bakeryVolume.gameObject;
                Undo.DestroyObjectImmediate(target);
                return;
            }

            if (!manager.IsBakeryMode || bakeryVolume == null) return;

            bakeryVolume.gameObject.name = $"Bakery Volume - {volume.gameObject.name}";
            bakeryVolume.gameObject.tag = "EditorOnly";
            if (bakeryVolume.transform.parent != volume.transform) Undo.SetTransformParent(bakeryVolume.transform, volume.transform, "Parent Bakery Volume");
            bakeryVolume.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            bakeryVolume.transform.localScale = Vector3.one;
            bakeryVolume.bounds = new Bounds(GetPosition(volume), GetScale(volume));
            bakeryVolume.enableBaking = true;
            bakeryVolume.denoise = manager.Denoise;
            bakeryVolume.adaptiveRes = false;
            bakeryVolume.resolutionX = volume.Resolution.x;
            bakeryVolume.resolutionY = volume.Resolution.y;
            bakeryVolume.resolutionZ = volume.Resolution.z;
            bakeryVolume.encoding = BakeryVolume.Encoding.Half4;

            System.Reflection.FieldInfo fullRotation = typeof(BakeryVolume).GetField("_rotateAroundXYZ");
            if (fullRotation != null) {
                fullRotation.SetValue(bakeryVolume, true);
                typeof(BakeryVolume).GetField("rotateAroundY")?.SetValue(bakeryVolume, false);
            } else {
                typeof(BakeryVolume).GetField("rotateAroundY")?.SetValue(bakeryVolume, true);
            }
            LVUtils.MarkDirty(bakeryVolume);
#endif
        }

        public static bool TryImportBakeryTextures(LightVolumeInstance volume) {
#if BAKERY_INCLUDED
            if (volume == null || !volume.Bake || !TryFindOwnedBakeryVolume(volume, out BakeryVolume bakeryVolume)) return false;
            if (bakeryVolume == null || bakeryVolume.bakedTexture0 == null) return false;
            if (volume.Texture0 == bakeryVolume.bakedTexture0 && volume.Texture1 == bakeryVolume.bakedTexture1 && volume.Texture2 == bakeryVolume.bakedTexture2) return false;

            volume.Texture0 = bakeryVolume.bakedTexture0;
            volume.Texture1 = bakeryVolume.bakedTexture1;
            volume.Texture2 = bakeryVolume.bakedTexture2;
            LVUtils.MarkDirty(volume);
            return true;
#else
            return false;
#endif
        }

#if BAKERY_INCLUDED
        private static bool TryFindOwnedBakeryVolume(LightVolumeInstance volume, out BakeryVolume bakeryVolume) {
            bakeryVolume = null;
            BakeryVolume[] candidates = volume.GetComponentsInChildren<BakeryVolume>(true);
            for (int i = 0; i < candidates.Length; i++) {
                BakeryVolume candidate = candidates[i];
                if (candidate == null || candidate.transform.parent != volume.transform) continue;
                if (bakeryVolume == null) {
                    bakeryVolume = candidate;
                    continue;
                }

                Debug.LogWarning($"[LightVolume] Multiple direct Bakery Volume helpers found on {volume.gameObject.name}. Automatic Bakery setup was skipped.", volume);
                bakeryVolume = null;
                return false;
            }
            return true;
        }
#endif
    }
}
