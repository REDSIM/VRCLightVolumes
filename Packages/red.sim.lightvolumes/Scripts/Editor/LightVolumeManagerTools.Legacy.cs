using UnityEngine;
using PostProcessor = VRCLightVolumes.LightVolumeManager.PostProcessor;

namespace VRCLightVolumes {
    // Temporary compatibility extensions for integrations written before manager.Editor.
    // Delete this file together with LightVolumeManager.LegacyEditor.cs to remove the supported legacy integration surface.
    public static class LightVolumeManagerTools {
        public static void GenerateAtlas(this LightVolumeManager manager) {
            LightVolumeManagerEditorBackend.GenerateAtlas(manager);
        }

        public static void BakeShadowMaps(this LightVolumeManager manager) {
            LightVolumeManagerEditorBackend.BakeShadowMaps(manager);
        }

        public static int GetCustomProbesCount(this LightVolumeManager manager) {
            return LightVolumeManagerEditorBackend.GetCustomProbesCount(manager);
        }

        public static Vector3[] GetCustomProbes(this LightVolumeManager manager, int id) {
            return LightVolumeManagerEditorBackend.GetCustomProbes(manager, id);
        }

        public static void SetCustomProbesBaked(this LightVolumeManager manager, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b) {
            LightVolumeManagerEditorBackend.SetCustomProbesBaked(manager, id, l0, l1r, l1g, l1b);
        }

        public static void SetCustomProbesBaked(this LightVolumeManager manager, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, bool denoise) {
            LightVolumeManagerEditorBackend.SetCustomProbesBaked(manager, id, l0, l1r, l1g, l1b, denoise);
        }

        public static void SetCustomProbesBaked(this LightVolumeManager manager, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity) {
            LightVolumeManagerEditorBackend.SetCustomProbesBaked(manager, id, l0, l1r, l1g, l1b, validity);
        }

        public static void SetCustomProbesBaked(this LightVolumeManager manager, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity, bool denoise) {
            LightVolumeManagerEditorBackend.SetCustomProbesBaked(manager, id, l0, l1r, l1g, l1b, validity, denoise);
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
    }
}
