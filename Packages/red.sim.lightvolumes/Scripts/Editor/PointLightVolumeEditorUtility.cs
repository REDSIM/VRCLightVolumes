using UnityEditor;

namespace VRCLightVolumes {
    // Synchronizes Point Light Volume authoring state for inspectors and other editor workflows.
    internal static class PointLightVolumeEditorUtility {
        internal const int CustomTexturesChanged = 1;
        internal const int ShadowTexturesChanged = 2;

        // Applies derived data once and copies the proxy without rebuilding Manager-owned caches.
        internal static int Sync(PointLightVolumeInstance pointLightVolume, bool recordUndo = false, bool notifyManager = true) {
            if (pointLightVolume == null) return 0;

            bool customTexturesChanged = pointLightVolume.HasEditorCustomTextureChanges();
            bool shadowTexturesChanged = pointLightVolume.HasEditorShadowTextureChanges();
            if (recordUndo) Undo.RecordObject(pointLightVolume, "Sync Point Light Volume");

            pointLightVolume.EditorApplyAuthoringData(customTexturesChanged, shadowTexturesChanged, notifyManager);
            LightVolumeManagerEditorBackend.CopyProxyToUdon(pointLightVolume);

            int changes = (customTexturesChanged ? CustomTexturesChanged : 0)
                | (shadowTexturesChanged ? ShadowTexturesChanged : 0);
            return changes;
        }
    }
}
