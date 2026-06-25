using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace VRCLightVolumes {
    [CanEditMultipleObjects]
    [CustomEditor(typeof(LightVolume))]
    public class LightVolumeEditor : Editor {

        private bool _isEditMode = false;
        private Tool _savedTool;
        private Tool _previousTool;
        private LightProbePlacerWindow _previousProbePlacerWindow;

        LightVolume LightVolume;

        private void OnEnable() {
            _previousTool = Tools.current;
            LightVolume = (LightVolume)target;
            LightVolumePreviewSceneRenderer.RequestRefresh();
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        public override void OnInspectorGUI() {

            LightVolume.SetupDependencies();

            serializedObject.Update();
            int undoGroup = Undo.GetCurrentGroup();

            GUIContent editBoundsContent = EditorGUIUtility.IconContent("EditCollider");
            editBoundsContent.text = " Edit Bounds";

            GUIContent previewProbesContent = EditorGUIUtility.IconContent("LightProbeGroup Gizmo");
            previewProbesContent.text = " Preview Voxels";

            GUIStyle toggleStyle = new GUIStyle(GUI.skin.button);
            toggleStyle.imagePosition = ImagePosition.ImageLeft;
            toggleStyle.fixedHeight = 20;
            toggleStyle.fixedWidth = 150;

            GUILayout.Space(10);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            bool newIsEditMode = GUILayout.Toggle(_isEditMode, editBoundsContent, toggleStyle);
            GUILayout.Space(10);
            bool previewVoxels = LightVolumePreviewSceneRenderer.IsPreviewModeActive;
            bool newPreviewProbes = GUILayout.Toggle(previewVoxels, previewProbesContent, toggleStyle);
            if (newPreviewProbes != previewVoxels) {
                LightVolumePreviewSceneRenderer.SetPreviewMode(newPreviewProbes);
                RepaintAll();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            int vCount = LightVolume.GetVoxelCount(1);

            GUILayout.Space(10);

            if (vCount < 0) {
                EditorGUILayout.HelpBox("Volume density is too high and impossible to calculate and store! Consider using lower density.", MessageType.Error);
            } else {
                GUILayout.Label($"Size in VRAM: {SizeInVRAM(vCount)} MB");
                GUILayout.Label($"Size in bundle: {SizeInBundle(vCount)} MB (Approximately)");
            }

#if BAKERY_INCLUDED

            Vector3 rotEuler = LightVolume.transform.rotation.eulerAngles;

            bool isFullRotationSupported = typeof(BakeryVolume).GetField("_rotateAroundXYZ") != null;
            if (!isFullRotationSupported) {
                bool isYRotationSupported = typeof(BakeryVolume).GetField("rotateAroundY") != null;
                if (isYRotationSupported) {
                    if ((rotEuler.x != 0 || rotEuler.z != 0) && LightVolume.LightVolumeSetup.IsBakeryMode) {
                        GUILayout.Space(10);
                        EditorGUILayout.HelpBox("With your Bakery version, only Y-axis rotation is supported in the editor. Apply the Bakery latest patch to have full rotation support. Free rotation will still work at runtime.", MessageType.Warning);
                    }
                } else {
                    if ((rotEuler.x != 0 || rotEuler.z != 0 || rotEuler.y != 0) && LightVolume.LightVolumeSetup.IsBakeryMode) {
                        GUILayout.Space(10);
                        EditorGUILayout.HelpBox("With your Bakery version, volume rotation is not supported in the editor. Apply the Bakery latest patch to have full rotation support. Free rotation will still work at runtime.", MessageType.Warning);
                    }
                }
            }

#else
        if (LightVolume.LightVolumeSetup.BakingMode == LightVolumeSetup.Baking.Bakery) {
            GUILayout.Space(10);
            EditorGUILayout.HelpBox("To use Bakery mode, please include Bakery into your project!", MessageType.Error);
        }
#endif

            // Clicking on Edit Bounds button
            if (newIsEditMode != _isEditMode) {

                if (newIsEditMode) {
                    // Went to edit mode
                    _savedTool = Tools.current;
                    _previousTool = Tool.None;
                    Tools.current = Tool.None;
                } else {
                    // Went from edit mode
                    Tools.current = _savedTool;
                    _previousTool = _savedTool;
                }

                _isEditMode = newIsEditMode;
                Tools.hidden = false;
                RepaintAll();
            }

            // Clicking on a new tool
            if (_isEditMode && _previousTool != Tools.current) {
                // Went from edit mode
                _previousTool = Tools.current;
                _isEditMode = false;
                Tools.hidden = false;
                RepaintAll();
            }

            List<string> hiddenFields = new List<string> { "m_Script", "LightVolumeInstance", "LightVolumeSetup" };

#if BAKERY_INCLUDED
            hiddenFields.Add("BakeryVolume");
#endif

            if (LightVolume.Bake) {
                hiddenFields.Add("ReserveUVSpace");
            } else if (!LightVolume.ReserveUVSpace) {
                hiddenFields.Add("AdaptiveResolution");
                hiddenFields.Add("Resolution");
                hiddenFields.Add("VoxelsPerUnit");
            }
            if (LightVolume.AdaptiveResolution) {

            } else {
                hiddenFields.Add("VoxelsPerUnit");
            }

            DrawPropertiesExcluding(serializedObject, hiddenFields.ToArray());

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fixedHeight = 20;
            buttonStyle.fixedWidth = 170;

            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Generate Light Probes", buttonStyle)) {
                if (_previousProbePlacerWindow == null) {
                    _previousProbePlacerWindow = LightProbePlacerWindow.Show(LightVolume);
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (serializedObject.ApplyModifiedProperties()) {
                SyncTargets(true);
                Undo.CollapseUndoOperations(undoGroup);
            }

        }

        // Restores runtime mirror data after Unity applies Undo or Redo to the authoring component.
        private void OnUndoRedoPerformed() {
            SyncTargets(false);
            Repaint();
        }

        // Syncs changed inspector values into runtime instances and shader globals immediately.
        private void SyncTargets(bool recordUndo) {
            for (int i = 0; i < targets.Length; i++) {
                LightVolume volume = targets[i] as LightVolume;
                if (volume != null) volume.SyncEditorChanges(recordUndo);
            }
        }

        // Drowing bounds for a light volume
        private void DrawVolumeBounds(LightVolume volume) {
            Handles.matrix = volume.GetMatrixTRS();
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.color = Color.white;
            Handles.DrawWireCube(Vector3.zero, Vector3.one);
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
            Handles.color = new Color(1, 1, 1, 0.2f);
            Handles.DrawWireCube(Vector3.zero, Vector3.one);
            Handles.matrix = Matrix4x4.identity;
        }

        protected void OnSceneGUI() {

            // Drawing bounds for each of selected light volumes
            foreach (var obj in Selection.gameObjects) {
                var volume = obj.GetComponent<LightVolume>();
                if (volume != null) {
                    DrawVolumeBounds(volume);
                }
            }

            Transform transform = LightVolume.transform;

            if (!_isEditMode) return;

            // Hide tools
            Tools.hidden = true;

            // Volume transform
            var position = LightVolume.GetPosition();
            var rotation = LightVolume.GetRotation();
            var scale = LightVolume.GetScale();

            // Axis colors
            Color colorX = Handles.xAxisColor;
            Color colorY = Handles.yAxisColor;
            Color colorZ = Handles.zAxisColor;

            for (int i = 0; i < 6; i++) {

                Vector3 worldDirection = Vector3.zero;
                Vector3 worldUpDirection = Vector3.zero;

                int axisIndex = i / 2;
                bool isPositive = (i % 2 == 0);

                switch (axisIndex) {
                    case 0: // X
                        worldDirection = rotation * (isPositive ? Vector3.right : Vector3.left);
                        worldUpDirection = rotation * Vector3.up;
                        Handles.color = colorX;
                        break;
                    case 1: // Y
                        worldDirection = rotation * (isPositive ? Vector3.up : Vector3.down);
                        worldUpDirection = rotation * Vector3.right;
                        Handles.color = colorY;
                        break;
                    case 2: // Z
                        worldDirection = rotation * (isPositive ? Vector3.forward : Vector3.back);
                        worldUpDirection = rotation * Vector3.up;
                        Handles.color = colorZ;
                        break;
                }

                // Handle parameters
                Vector3 handlePos = position + worldDirection * scale[axisIndex] * 0.5f;
                float handleSize = HandleUtility.GetHandleSize(handlePos) * 0.2f;
                Vector3 handleOffset = handleSize * worldDirection * 0.1f / 0.2f;

                EditorGUI.BeginChangeCheck();

                // Drawing Cone handle
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                Vector3 newHandleLocalPos = Handles.Slider(handlePos + handleOffset, worldDirection, handleSize, Handles.ConeHandleCap, 0.25f) - handleOffset;

                // Drawing X-Ray square
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.DrawSolidRectangleWithOutline(LVUtils.GetPlaneVertices(handlePos, Quaternion.LookRotation(worldDirection, worldUpDirection), handleSize), new Color(1, 1, 1, 0.15f), Color.white);
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.DrawSolidRectangleWithOutline(LVUtils.GetPlaneVertices(handlePos, Quaternion.LookRotation(worldDirection, worldUpDirection), handleSize), Color.clear, new Color(1, 1, 1, 0.25f));

                // Applying position and rotation
                if (EditorGUI.EndChangeCheck()) {
                    Undo.RecordObject(transform, "Scale Bounds Size");
                    float delta = Vector3.Dot(newHandleLocalPos - handlePos, worldDirection);
                    Vector3 modifiedScale = scale;
                    modifiedScale[axisIndex] += delta;
                    transform.position += worldDirection * delta / 2;
                    LVUtils.SetLossyScale(transform, modifiedScale);
                    LightVolume.Recalculate();
                }
            }

        }

        // Bring back tools
        void OnDisable() {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            LightVolumePreviewSceneRenderer.RequestRefresh();
            Tools.hidden = false;
            if (_isEditMode) {
                // Went from edit mode
                Tools.current = _savedTool;
                _previousTool = _savedTool;
            }
        }

        // Real size in VRAM
        string SizeInVRAM(int vCount) {
            double mb = (ulong)(vCount * 3f) * 8 / (double)(1024 * 1024);
            return mb.ToString("0.00");
        }

        // Approximate size in Asset bundle
        string SizeInBundle(int vCount) {
            double mb = (ulong)(vCount * 3f) * 8 * 0.315f / (double)(1024 * 1024);
            return mb.ToString("0.00");
        }

        // Little hack to force repaint scene in some edge cases
        private void RepaintAll() {
            EditorApplication.update += ForceRepaintNextFrame;
        }
        private static void ForceRepaintNextFrame() {
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
            EditorApplication.update -= ForceRepaintNextFrame;
        }

    }
}
