using UnityEditor;

namespace VRCLightVolumes {
    // Synchronizes Point Light Volume authoring state for inspectors and other editor workflows.
    public static class PointLightVolumeEditorUtility {
        public const int CustomTexturesChanged = 1;
        public const int ShadowTexturesChanged = 2;

        // Applies derived data once, copies the proxy once, and optionally refreshes shared runtime data.
        public static int Sync(PointLightVolumeInstance pointLightVolume, bool recordUndo = false, bool rebuildTextureArrays = true, bool refreshRuntime = true) {
            if (pointLightVolume == null) return 0;

            bool customTexturesChanged = pointLightVolume.HasEditorCustomTextureChanges();
            bool shadowTexturesChanged = pointLightVolume.HasEditorShadowTextureChanges();
            if (recordUndo) Undo.RecordObject(pointLightVolume, "Sync Point Light Volume");

            pointLightVolume.EditorApplyAuthoringData(customTexturesChanged, shadowTexturesChanged);
            LightVolumeManagerTools.CopyProxyToUdon(pointLightVolume);

            int changes = (customTexturesChanged ? CustomTexturesChanged : 0)
                | (shadowTexturesChanged ? ShadowTexturesChanged : 0);
            LightVolumeManager manager = pointLightVolume.LightVolumeManager;
            if (refreshRuntime) LightVolumeManagerTools.RefreshRuntimeManagerImmediately(manager);
            if (rebuildTextureArrays) LightVolumeManagerTools.ReinitializeTextures(manager, customTexturesChanged, shadowTexturesChanged);
            return changes;
        }
    }
}
