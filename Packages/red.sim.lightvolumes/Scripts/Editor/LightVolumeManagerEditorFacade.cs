using System;
using UnityEngine;

namespace VRCLightVolumes.Editor {
    // Opt-in editor operations for LightVolumeManager. These methods only become instance-like calls after importing VRCLightVolumes.Editor.
    public static class LightVolumeManagerEditorExtensions {
        // Returns a detached snapshot of the Manager's registered atlas post-processors.
        public static AtlasPostProcessor[] GetPostProcessors(this LightVolumeManagerEditorContext editor) {
            LightVolumeManager manager = editor.Manager;
            if (manager == null) return Array.Empty<AtlasPostProcessor>();

            AtlasPostProcessor[] source = manager.EditorGetAtlasPostProcessors();
            AtlasPostProcessor[] result = new AtlasPostProcessor[source.Length];
            Array.Copy(source, result, source.Length);
            return result;
        }

        // Checks for a processor with the requested output target and optional material.
        public static bool ContainsPostProcessor(this LightVolumeManagerEditorContext editor, RenderTexture target, Material material = null) {
            LightVolumeManager manager = editor.Manager;
            if (manager == null || target == null) return false;

            AtlasPostProcessor[] processors = manager.EditorGetAtlasPostProcessors();
            for (int i = 0; i < processors.Length; i++) {
                AtlasPostProcessor processor = processors[i];
                if (processor.Target == target && (material == null || processor.Material == material)) return true;
            }
            return false;
        }

        // Checks whether any non-null identity member of the descriptor matches a registered processor.
        public static bool ContainsPostProcessor(this LightVolumeManagerEditorContext editor, AtlasPostProcessor requested) {
            LightVolumeManager manager = editor.Manager;
            if (manager == null) return false;

            AtlasPostProcessor[] processors = manager.EditorGetAtlasPostProcessors();
            for (int i = 0; i < processors.Length; i++) {
                AtlasPostProcessor processor = processors[i];
                if (requested.Target != null && processor.Target == requested.Target ||
                    requested.Update != null && processor.Update == requested.Update ||
                    requested.UpdateWithInput != null && processor.UpdateWithInput == requested.UpdateWithInput)
                    return true;
            }
            return false;
        }

        // Adds or updates one atlas post-processor and refreshes the resulting atlas chain.
        public static void RegisterPostProcessor(this LightVolumeManagerEditorContext editor, AtlasPostProcessor processor) {
            LightVolumeManager manager = editor.Manager;
            if (manager != null) manager.EditorRegisterPostProcessor(processor);
        }

        // Registers a Custom Render Texture as a self-updating atlas post-processor.
        public static void RegisterPostProcessor(this LightVolumeManagerEditorContext editor, CustomRenderTexture texture) {
            LightVolumeManager manager = editor.Manager;
            if (manager != null) manager.EditorRegisterPostProcessorCRT(texture);
        }

        // Removes every registered processor that writes to the requested target texture.
        public static void UnregisterPostProcessor(this LightVolumeManagerEditorContext editor, RenderTexture texture) {
            LightVolumeManager manager = editor.Manager;
            if (manager != null) manager.EditorUnregisterPostProcessor(texture);
        }

        // Removes every processor matching the descriptor's target or update callback identity.
        public static void UnregisterPostProcessor(this LightVolumeManagerEditorContext editor, AtlasPostProcessor processor) {
            LightVolumeManager manager = editor.Manager;
            if (manager != null) manager.EditorUnregisterPostProcessor(processor);
        }

        // Re-runs the current post-processing chain from the Manager's base atlas.
        public static void RefreshPostProcessors(this LightVolumeManagerEditorContext editor) {
            LightVolumeManager manager = editor.Manager;
            if (manager != null) manager.EditorRefreshAtlasPostProcessors();
        }

        // Rebuilds and packs the registered Light Volumes into the Manager's shared 3D atlas.
        public static void GenerateAtlas(this LightVolumeManagerEditorContext editor) {
            LightVolumeManager manager = editor.Manager;
            if (manager != null) LightVolumeManagerEditorBackend.GenerateAtlas(manager);
        }

        // Bakes point-light shadows marked for rebaking.
        public static void BakeShadowMaps(this LightVolumeManagerEditorContext editor) {
            LightVolumeManager manager = editor.Manager;
            if (manager != null) LightVolumeManagerEditorBackend.BakeShadowMaps(manager);
        }

        // Returns the number of active bake-enabled volumes exposed to a custom lightmapper.
        public static int GetCustomProbesCount(this LightVolumeManagerEditorContext editor) {
            LightVolumeManager manager = editor.Manager;
            return manager != null ? LightVolumeManagerEditorBackend.GetCustomProbesCount(manager) : 0;
        }

        // Returns world-space voxel probe positions for one custom-lightmapper volume ID.
        public static Vector3[] GetCustomProbes(this LightVolumeManagerEditorContext editor, int id) {
            LightVolumeManager manager = editor.Manager;
            return manager != null ? LightVolumeManagerEditorBackend.GetCustomProbes(manager, id) : Array.Empty<Vector3>();
        }

        // Stores custom SH output without validity data and uses the Manager's denoising setting.
        public static void SetCustomProbesBaked(this LightVolumeManagerEditorContext editor, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b) {
            LightVolumeManager manager = editor.Manager;
            if (manager != null) LightVolumeManagerEditorBackend.SetCustomProbesBaked(manager, id, l0, l1r, l1g, l1b);
        }

        // Stores custom SH output without validity data and uses the requested denoising mode.
        public static void SetCustomProbesBaked(this LightVolumeManagerEditorContext editor, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, bool denoise) {
            LightVolumeManager manager = editor.Manager;
            if (manager != null) LightVolumeManagerEditorBackend.SetCustomProbesBaked(manager, id, l0, l1r, l1g, l1b, denoise);
        }

        // Stores custom SH and validity data and uses the Manager's denoising setting.
        public static void SetCustomProbesBaked(this LightVolumeManagerEditorContext editor, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity) {
            LightVolumeManager manager = editor.Manager;
            if (manager != null) LightVolumeManagerEditorBackend.SetCustomProbesBaked(manager, id, l0, l1r, l1g, l1b, validity);
        }

        // Stores complete custom-lightmapper output and queues shadow and atlas finalization.
        public static void SetCustomProbesBaked(this LightVolumeManagerEditorContext editor, int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity, bool denoise) {
            LightVolumeManager manager = editor.Manager;
            if (manager != null) LightVolumeManagerEditorBackend.SetCustomProbesBaked(manager, id, l0, l1r, l1g, l1b, validity, denoise);
        }

    }
}
