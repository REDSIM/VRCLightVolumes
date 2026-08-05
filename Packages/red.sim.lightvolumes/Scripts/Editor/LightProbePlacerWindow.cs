using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {
    public class LightProbePlacerWindow : EditorWindow {
        private LightVolumeInstance _lightVolume;
        private bool _adaptiveResolution = true;
        private float _voxelsPerUnit = 2f;
        private Vector3Int _resolution = new Vector3Int(16, 16, 16);
        private Vector3[] _probePositions = new Vector3[0];
        private bool _isWindowActive;
        private LightVolumePreviewRenderer _previewRenderer;

        // Opens a probe-placement window initialized from the selected Light Volume.
        public static LightProbePlacerWindow Show(LightVolumeInstance volume) {
            if (volume == null) return null;

            LightProbePlacerWindow window = CreateInstance<LightProbePlacerWindow>();
            window._lightVolume = volume;
            window._resolution = new Vector3Int(Mathf.Max(volume.Resolution.x / 4, 1), Mathf.Max(volume.Resolution.y / 4, 1), Mathf.Max(volume.Resolution.z / 4, 1));
            window._voxelsPerUnit = Mathf.Max(volume.VoxelsPerUnit / 4f, 0f);
            window._adaptiveResolution = volume.AdaptiveResolution;
            window.titleContent = new GUIContent("Generate Light Probes");
            window.position = new Rect(Screen.width * 0.5f, Screen.height * 0.5f, 220f, 150f);
            window.minSize = new Vector2(220f, 150f);
            window.Show();
            return window;
        }

        // Centers the window and begins drawing the Scene View probe preview.
        private void OnEnable() {
            const float width = 220f;
            const float height = 150f;
            Vector2 center = new Vector2(Screen.currentResolution.width * 0.5f - width * 0.5f, Screen.currentResolution.height * 0.5f - height * 0.5f);

            position = new Rect(center, new Vector2(width, height));
            SceneView.duringSceneGui += OnSceneGUI;
            _isWindowActive = true;
        }

        // Unsubscribes Scene View drawing and releases preview resources.
        private void OnDisable() {
            SceneView.duringSceneGui -= OnSceneGUI;
            ReleasePreviewRenderer();
            _isWindowActive = false;
        }

        // Draws the current probe grid preview into the active Scene View.
        private void OnSceneGUI(SceneView sceneView) {
            if (!_isWindowActive || Event.current.type != EventType.Repaint || _lightVolume == null) return;

            if (_previewRenderer == null) {
                _previewRenderer = new LightVolumePreviewRenderer();
            }
            _previewRenderer.DrawProbeGrid(_lightVolume, _resolution, sceneView.camera);
        }

        // Disposes the temporary probe-grid renderer.
        private void ReleasePreviewRenderer() {
            if (_previewRenderer == null) return;
            _previewRenderer.Dispose();
            _previewRenderer = null;
        }

        // Draws probe density controls and the creation action.
        private void OnGUI() {
            if (_lightVolume == null) {
                Close();
                return;
            }

            const float padding = 10f;
            Rect paddedRect = new Rect(padding, padding, position.width - padding * 2f, position.height - padding * 2f);

            GUILayout.BeginArea(paddedRect);
            EditorGUILayout.LabelField(_lightVolume.gameObject.name, EditorStyles.boldLabel);

            _adaptiveResolution = EditorGUILayout.Toggle("Adaptive Resolution", _adaptiveResolution);
            if (_adaptiveResolution) {
                _voxelsPerUnit = Mathf.Max(EditorGUILayout.FloatField("Voxels Per Unit", _voxelsPerUnit), 0f);
            }

            _resolution = EditorGUILayout.Vector3IntField("Resolution", _resolution);
            ClampResolution();
            Recalculate();

            GUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(LightVolumeTools.GetVoxelCount(_resolution) < 0)) {
                if (GUILayout.Button("Create Light Probe Group")) {
                    CreateLightProbeGroup();
                    Close();
                }
            }

            GUILayout.EndArea();
            SceneView.RepaintAll();
        }

        // Creates an Undo-aware LightProbeGroup containing the previewed positions.
        private void CreateLightProbeGroup() {
            Recalculate();
            if (!LightVolumeTools.TryCalculateProbePositions(_lightVolume, _resolution, out _probePositions)) return;

            GameObject probeObject = new GameObject("Light Probes - " + _lightVolume.gameObject.name);
            Undo.RegisterCreatedObjectUndo(probeObject, "Create Light Probe Group");
            Undo.SetTransformParent(probeObject.transform, _lightVolume.transform, "Parent Light Probe Group");
            LightProbeGroup probeGroup = probeObject.AddComponent<LightProbeGroup>();
            probeGroup.probePositions = _probePositions;
            EditorGUIUtility.PingObject(probeObject);
            Selection.activeObject = probeObject;
        }

        // Derives probe resolution from world size when adaptive mode is enabled.
        private void Recalculate() {
            if (!_adaptiveResolution) return;

            Vector3 scale = LightVolumeTools.GetScale(_lightVolume);
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 count = scale * _voxelsPerUnit;
            _resolution = new Vector3Int(Mathf.Max(Mathf.RoundToInt(count.x), 1), Mathf.Max(Mathf.RoundToInt(count.y), 1), Mathf.Max(Mathf.RoundToInt(count.z), 1));
        }

        // Keeps every probe-grid dimension at one or greater.
        private void ClampResolution() {
            _resolution = new Vector3Int(Mathf.Max(_resolution.x, 1), Mathf.Max(_resolution.y, 1), Mathf.Max(_resolution.z, 1));
        }
    }
}
