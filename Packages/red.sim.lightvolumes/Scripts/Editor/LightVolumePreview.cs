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
        static readonly HashSet<SceneView> SetupViews = new HashSet<SceneView>();

        // Registers Light Volumes Scene View camera modes and begins configuring opened views.
        static SceneLightVolumeDebugModes() {
            L1Mode = SceneView.AddCameraMode("VRCLV SH L1", Section);
            L0Mode = SceneView.AddCameraMode("VRCLV SH L0", Section);
            FineMode = SceneView.AddCameraMode("VRCLV Fine Clustering", Section);
            CoarseMode = SceneView.AddCameraMode("VRCLV Coarse Clustering", Section);

            SceneView.beforeSceneGui += SetupSceneView;
        }

        // Installs one camera-mode callback for a newly encountered Scene View.
        static void SetupSceneView(SceneView view) {
            if (!SetupViews.Add(view)) return;

            view.onCameraModeChanged += mode => ApplyMode(view, mode);
            ApplyMode(view, view.cameraMode);
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
