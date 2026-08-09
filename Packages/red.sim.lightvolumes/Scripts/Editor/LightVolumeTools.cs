using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {
    public static class LightVolumeTools {
        // Initializes a Light Volume's world-space bounds from a Reflection Probe on its parent.
        public static void ResetFromParentReflectionProbe(LightVolumeInstance volume) {
            if (volume == null || volume.transform.parent == null) return;
            if (!volume.transform.parent.TryGetComponent(out ReflectionProbe probe)) return;

            Undo.RecordObject(volume.transform, "Initialize Light Volume Bounds");
            volume.transform.SetPositionAndRotation(probe.bounds.center, Quaternion.identity);
            LVUtils.SetLossyScale(volume.transform, probe.bounds.size);
            ApplyRuntimeState(volume, true);
        }

        // Returns the current world-space center of a Light Volume.
        public static Vector3 GetPosition(LightVolumeInstance volume) {
            return volume == null ? Vector3.zero : volume.transform.position;
        }

        // Returns the current world-space size of a Light Volume, including parent scaling.
        public static Vector3 GetScale(LightVolumeInstance volume) {
            return volume == null ? Vector3.zero : volume.transform.lossyScale;
        }

        // Returns the rotation supported by the active lightmapper and Bakery version.
        public static Quaternion GetRotation(LightVolumeInstance volume) {
            if (volume == null) return Quaternion.identity;
            LightVolumeManager manager = volume.LightVolumeManager;
            if (manager == null || !manager.EditorIsBakeryMode || Application.isPlaying || !volume.Bake) return volume.transform.rotation;
            if (BakeryEditorBridge.SupportsFullRotation) return volume.transform.rotation;
            if (BakeryEditorBridge.SupportsYRotation) return Quaternion.Euler(0f, volume.transform.rotation.eulerAngles.y, 0f);
            return Quaternion.identity;
        }

        // Builds the world-space transform matrix used to place this Light Volume.
        public static Matrix4x4 GetMatrixTRS(LightVolumeInstance volume) {
            return Matrix4x4.TRS(GetPosition(volume), GetRotation(volume), GetScale(volume));
        }

        // Calculates the padded voxel count for a Light Volume, or -1 when it would overflow.
        public static int GetVoxelCount(LightVolumeInstance volume, int padding = 0) {
            return volume == null ? -1 : GetVoxelCount(volume.Resolution, padding);
        }

        // Calculates a padded voxel count from a resolution, or -1 for invalid or overflowing dimensions.
        public static int GetVoxelCount(Vector3Int resolution, int padding = 0) {
            long width = (long)resolution.x + padding * 2L;
            long height = (long)resolution.y + padding * 2L;
            long depth = (long)resolution.z + padding * 2L;
            if (width <= 0L || height <= 0L || depth <= 0L || width > int.MaxValue / height) return -1;
            long sliceSize = width * height;
            return sliceSize > int.MaxValue / depth ? -1 : (int)(sliceSize * depth);
        }

        // Updates a Light Volume's resolution from its world size and voxel density.
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

        // Recalculates resolution when adaptive resolution is enabled and reports whether it changed.
        public static bool Recalculate(LightVolumeInstance volume) {
            return volume != null && volume.AdaptiveResolution && RecalculateAdaptiveResolution(volume);
        }

        // Synchronizes derived transform, blending and active state and optionally notifies the Manager.
        public static bool ApplyRuntimeState(LightVolumeInstance volume, bool notifyManager) {
            if (volume == null) return false;

            bool changed = Recalculate(volume);
            Vector3 scale = volume.transform.lossyScale;
            float safeRadius = Mathf.Max(volume.SmoothBlending, 0.00001f);
            Vector4 edgeSmoothing = new Vector4(scale.x / safeRadius, scale.y / safeRadius, scale.z / safeRadius, 0f);
            Quaternion transformRotation = volume.transform.rotation;
            Matrix4x4 inverseWorld = Matrix4x4.TRS(volume.transform.position, transformRotation, scale).inverse;
            Quaternion relativeRotation = transformRotation * volume.InvBakedRotation;
            bool isRotated = Mathf.Abs(Quaternion.Dot(relativeRotation, Quaternion.identity)) < 0.999999f;
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
            bool isActive = volume.isActiveAndEnabled && volume.Intensity != 0f && volume.Color != Color.black;
            if (volume.IsActive != isActive) {
                volume.IsActive = isActive;
                changed = true;
            }

            if (notifyManager && volume.LightVolumeManager != null && volume.gameObject.activeInHierarchy) volume.LightVolumeManager.NotifyLightVolumeChanged(volume, true);
            return changed;
        }

        // Generates world-space probe positions at the center of every voxel in the requested grid.
        public static bool TryCalculateProbePositions(LightVolumeInstance volume, Vector3Int resolution, out Vector3[] positions) {
            int voxelCount = GetVoxelCount(resolution);
            if (volume == null || voxelCount < 0) {
                positions = new Vector3[0];
                if (volume != null) Debug.LogError($"[LightVolumes] Can't calculate probes for light volume {volume.gameObject.name}. Resolution is invalid or the voxel count is too large!", volume);
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

        // Recalculates a Light Volume and returns its current world-space voxel probe positions.
        public static Vector3[] GetCustomProbes(LightVolumeInstance volume) {
            if (volume == null) return new Vector3[0];
            Recalculate(volume);
            TryCalculateProbePositions(volume, volume.Resolution, out Vector3[] positions);
            return positions;
        }

        // Creates, removes or synchronizes the hidden Bakery Volume owned by a Light Volume.
        public static void SetupBakeryDependencies(LightVolumeInstance volume, bool createIfMissing = true) {
            BakeryEditorBridge.SetupVolume(volume, createIfMissing);
        }

        // Imports newly baked Bakery SH textures into their owning Light Volume.
        public static bool TryImportBakeryTextures(LightVolumeInstance volume) {
            return BakeryEditorBridge.TryImportTextures(volume);
        }
    }
}
