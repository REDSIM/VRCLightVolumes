using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {

    [InitializeOnLoad]
    static class SceneLightVolumeDebugModes {
        const string Section = "Light Volumes Debug";

        static class Shaders {
            internal static readonly Shader L1 = Shader.Find("Hidden/LV_DebugDisplayL1");
            internal static readonly Shader L0 = Shader.Find("Hidden/LV_DebugDisplayL0");
            internal static readonly Shader Fine = Shader.Find("Hidden/LV_DebugDisplayFineClustering");
            internal static readonly Shader Coarse = Shader.Find("Hidden/LV_DebugDisplayCoarseClustering");
        }

        static readonly SceneView.CameraMode L1Mode;
        static readonly SceneView.CameraMode L0Mode;
        static readonly SceneView.CameraMode FineMode;
        static readonly SceneView.CameraMode CoarseMode;
        static readonly Dictionary<SceneView, Action<SceneView.CameraMode>> ModeHandlers = new Dictionary<SceneView, Action<SceneView.CameraMode>>();

        // Registers Light Volumes Scene View camera modes and begins configuring opened views.
        static SceneLightVolumeDebugModes() {
            L1Mode = SceneView.AddCameraMode("VRCLV SH L1", Section);
            L0Mode = SceneView.AddCameraMode("VRCLV SH L0", Section);
            FineMode = SceneView.AddCameraMode("VRCLV Fine Clustering", Section);
            CoarseMode = SceneView.AddCameraMode("VRCLV Coarse Clustering", Section);

            SceneView.beforeSceneGui += SetupSceneView;
            EditorApplication.update += PruneClosedSceneViews;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
        }

        // Installs one camera-mode callback for a newly encountered Scene View.
        static void SetupSceneView(SceneView view) {
            PruneClosedSceneViews();
            if (view == null) return;
            if (ModeHandlers.ContainsKey(view)) return;
            Action<SceneView.CameraMode> handler = mode => ApplyMode(view, mode);
            ModeHandlers.Add(view, handler);
            view.onCameraModeChanged += handler;
            ApplyMode(view, view.cameraMode);
        }

        // Drops destroyed editor windows so their managed wrappers and captured callbacks are not retained until the next domain reload. Closed SceneViews compare equal to null while the dictionary still holds their original wrapper as a key.
        static void PruneClosedSceneViews() {
            List<SceneView> closedViews = null;
            foreach (KeyValuePair<SceneView, Action<SceneView.CameraMode>> pair in ModeHandlers) {
                if (pair.Key != null) continue;
                if (closedViews == null) closedViews = new List<SceneView>();
                closedViews.Add(pair.Key);
            }
            if (closedViews == null) return;
            for (int i = 0; i < closedViews.Count; i++) ModeHandlers.Remove(closedViews[i]);
        }

        // Removes the global hook and every per-view callback before this editor domain ends.
        static void Shutdown() {
            SceneView.beforeSceneGui -= SetupSceneView;
            EditorApplication.update -= PruneClosedSceneViews;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
            foreach (KeyValuePair<SceneView, Action<SceneView.CameraMode>> pair in ModeHandlers) {
                if (pair.Key != null) pair.Key.onCameraModeChanged -= pair.Value;
            }
            ModeHandlers.Clear();
        }

        // Applies the replacement shader associated with a Light Volumes debug camera mode.
        static void ApplyMode(SceneView view, SceneView.CameraMode mode) {
            Shader shader;

            if (mode == L1Mode) shader = Shaders.L1;
            else if (mode == L0Mode) shader = Shaders.L0;
            else if (mode == FineMode) shader = Shaders.Fine;
            else if (mode == CoarseMode) shader = Shaders.Coarse;
            else {
                if (mode.drawMode != DrawCameraMode.Textured) return;
                shader = null;
            }

            view.SetSceneViewShaderReplace(shader, string.Empty);
        }
    }
}
