using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {
    public partial class PointLightVolumeEditor {
        private const float MinAreaSize = 0.001f;
        private static readonly Vector2[] AreaHandleSides = {
            new Vector2(-1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f),
            new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(0f, 1f)
        };
        private static readonly Vector3[] SpotHandleDirections = { Vector3.right, Vector3.left, Vector3.up, Vector3.down };
        private static readonly Vector3[] SizeHandleDirections = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
        private static readonly GUIContent EditLightContent = new GUIContent(" Edit Light", "Adjust the light's shape and brightness in the Scene view.");
        private bool _isLightEditMode;
        private Tool _lightEditPreviousTool;
        private GUIStyle _lightEditButtonStyle;
        private int _lightEditUndoGroup = -1;
        private int _areaDragControl;
        private Rect _areaDragRect;
        private Matrix4x4 _areaDragFrame;
        private int _intensityDragControl;
        private float _intensityDragSign;

        // Matches the existing Edit Bounds button.
        private void DrawLightEditToolbar() {
            HandleLightEditModeState();
            if (_lightEditButtonStyle == null) {
                _lightEditButtonStyle = new GUIStyle(GUI.skin.button) { fixedHeight = 20f, fixedWidth = 150f };
                EditLightContent.image = EditorGUIUtility.IconContent("EditCollider").image;
            }
            GUILayout.Space(10f);
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();
                bool enabled = GUILayout.Toggle(_isLightEditMode, EditLightContent, _lightEditButtonStyle);
                if (enabled != _isLightEditMode) SetLightEditMode(enabled);
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(10f);
        }

        // Restores the previous tool only when this Inspector still owns the edit mode.
        private void SetLightEditMode(bool enabled) {
            if (_isLightEditMode == enabled) return;
            if (enabled) {
                _lightEditPreviousTool = Tools.current;
                Tools.current = Tool.None;
            } else {
                if (_lightEditUndoGroup >= 0) {
                    GUIUtility.hotControl = 0;
                    EditorGUIUtility.SetWantsMouseJumping(0);
                }
                FinishLightEditDrag();
                if (Tools.current == Tool.None) Tools.current = _lightEditPreviousTool;
            }
            _isLightEditMode = enabled;
            Repaint();
            SceneView.RepaintAll();
        }

        // A transform-tool shortcut or Escape returns control to Unity.
        private void HandleLightEditModeState() {
            if (!_isLightEditMode) return;
            if (Tools.current != Tool.None) SetLightEditMode(false);
            else if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) {
                SetLightEditMode(false);
                Event.current.Use();
            }
        }

        private void FinishLightEditDrag() {
            if (_lightEditUndoGroup >= 0) Undo.CollapseUndoOperations(_lightEditUndoGroup);
            _lightEditUndoGroup = -1;
            _areaDragControl = 0;
            _intensityDragControl = 0;
        }

        // Each drag changes one active light and forms one Undo operation.
        private void DrawLightEditHandles(PointLightVolumeInstance light) {
            int previousControl = GUIUtility.hotControl;
            Handles.matrix = Matrix4x4.identity;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = Color.yellow;
            if (light.LightType == 2) DrawAreaEditHandles(light);
            else {
                DrawSourceSizeHandle(light);
                if (light.LightType == 1) DrawSpotAngleHandles(light);
            }
            DrawIntensityHandles(light);
            if (previousControl == 0 && GUIUtility.hotControl != 0) {
                _lightEditUndoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Edit Light");
            } else if (previousControl != 0 && GUIUtility.hotControl == 0) FinishLightEditDrag();
        }

        private void ApplyLightHandleChange(PointLightVolumeInstance light, bool transformChanged = false) {
            int changes = PointLightVolumeEditorUtility.Sync(light, false, false);
            LightVolumeManager manager = light.LightVolumeManager;
            if (manager != null && manager == LightVolumeManagerEditorBackend.GetPrimaryManager()) {
                if (changes != 0) LightVolumeManagerEditorBackend.ReinitializeTextures(manager,
                    (changes & PointLightVolumeEditorUtility.CustomTexturesChanged) != 0,
                    (changes & PointLightVolumeEditorUtility.ShadowTexturesChanged) != 0);
                else LightVolumeManagerEditorBackend.RefreshManagerOnce(manager, true);
            }
            LVUtils.MarkDirty(light);
            if (transformChanged) LVUtils.MarkDirty(light.transform);
            Repaint();
            SceneView.RepaintAll();
        }

        // Matches the runtime's average absolute scale, including mirrored objects.
        private static float GetLightHandleScale(Transform transform) {
            Vector3 scale = transform.lossyScale;
            return Mathf.Max((Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f, 0.0001f);
        }

        // Assigned LUTs use Range. Other lights use the emitter radius.
        private void DrawSourceSizeHandle(PointLightVolumeInstance light) {
            bool usesRange = light.Projection == 1 && light.FalloffLUT != null;
            float scale = GetLightHandleScale(light.transform);
            float radius = Mathf.Max(usesRange ? light.Range : light.LightSourceSize, 0.0001f) * scale;
            int count = light.LightType == 1 ? 1 : SizeHandleDirections.Length;
            using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one))) {
                for (int i = 0; i < count; i++) {
                    Vector3 direction = SizeHandleDirections[i];
                    Vector3 position = direction * radius;
                    float size = HandleUtility.GetHandleSize(position) * 0.05f;
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.Slider(position, direction, size, Handles.DotHandleCap, 0f);
                    if (!EditorGUI.EndChangeCheck()) continue;
                    float editedRadius = Mathf.Max(Handles.SnapValue(Vector3.Dot(moved, direction), EditorSnapSettings.move.x) / scale, 0.0001f);
                    Undo.RecordObject(light, "Edit Light");
                    if (usesRange) light.Range = editedRadius;
                    else light.LightSourceSize = editedRadius;
                    ApplyLightHandleChange(light);
                }
            }
        }

        // Planar handles support the complete 0.1 to 360 degree cone without a tangent singularity.
        private void DrawSpotAngleHandles(PointLightVolumeInstance light) {
            Transform t = light.transform;
            float scale = GetLightHandleScale(t);
            bool hasCalculatedRange = light.Projection != 1 || light.FalloffLUT == null;
            float radius = (hasCalculatedRange ? light.LightSourceSize : light.Range) * scale;
            using (new Handles.DrawingScope(Matrix4x4.TRS(t.position, t.rotation, Vector3.one))) {
                DrawSpotAngleRing(light, radius);
                if (hasCalculatedRange) {
                    if (TryGetIntensityHandleRange(light, GetBrightnessCutoff(light), out float range)) DrawSpotAngleRing(light, range);
                }
            }
        }

        private void DrawSpotAngleRing(PointLightVolumeInstance light, float radius) {
            for (int i = 0; i < SpotHandleDirections.Length; i++) {
                int id = GUIUtility.GetControlID(FocusType.Passive);
                if (radius <= 0f) continue;
                Vector3 radial = SpotHandleDirections[i];
                Vector3 position = (radial * Mathf.Sin(light.Angle) + Vector3.forward * Mathf.Cos(light.Angle)) * radius;
                float size = HandleUtility.GetHandleSize(position) * 0.05f;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.Slider2D(id, position, Vector3.Cross(radial, Vector3.forward), radial, Vector3.forward, size, Handles.DotHandleCap, Vector2.zero);
                if (!EditorGUI.EndChangeCheck()) continue;
                float angle = Mathf.Atan2(Mathf.Max(0f, Vector3.Dot(moved, radial)), moved.z) * Mathf.Rad2Deg * 2f;
                angle = Mathf.Clamp(Handles.SnapValue(angle, EditorSnapSettings.rotate), 0.1f, 360f);
                Undo.RecordObject(light, "Edit Light");
                light.Angle = angle * Mathf.Deg2Rad * 0.5f;
                ApplyLightHandleChange(light);
            }
        }

        private void DrawIntensityHandles(PointLightVolumeInstance light) {
            float cutoff = GetBrightnessCutoff(light);
            if (!TryGetIntensityHandleRange(light, cutoff, out float radius)) return;
            int count = light.LightType == 0 ? SizeHandleDirections.Length : 1;
            using (new Handles.DrawingScope(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one))) {
                bool showLabel = false;
                Vector3 labelPosition = Vector3.zero;
                for (int i = 0; i < count; i++) {
                    Vector3 direction = SizeHandleDirections[i];
                    Vector3 position = direction * radius;
                    float size = HandleUtility.GetHandleSize(position) * 0.065f;
                    int id = GUIUtility.GetControlID(FocusType.Passive);
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.Slider(id, position, direction, size, Handles.CubeHandleCap, 0f);
                    bool changed = EditorGUI.EndChangeCheck();
                    if (GUIUtility.hotControl == id && _intensityDragControl != id) {
                        _intensityDragControl = id;
                        _intensityDragSign = light.Intensity < 0f ? -1f : 1f;
                    }
                    if (changed) {
                        float range = Mathf.Max(0f, Handles.SnapValue(Vector3.Dot(moved, direction), EditorSnapSettings.move.x));
                        if (TryGetIntensityForRange(light, cutoff, range, out float intensity)) {
                            if (_intensityDragControl == id) intensity = Mathf.Abs(intensity) * _intensityDragSign;
                            if (intensity != light.Intensity) {
                                Undo.RecordObject(light, "Edit Light");
                                light.Intensity = intensity;
                                ApplyLightHandleChange(light);
                            }
                        }
                    }
                    if (GUIUtility.hotControl == id || HandleUtility.nearestControl == id) {
                        showLabel = true;
                        labelPosition = position + Vector3.up * size * 1.5f;
                    }
                }
                if (showLabel) Handles.Label(labelPosition, $"Intensity: {light.Intensity:0.###}");
            }
        }

        // Use the same raw RGB peak and effective dimensions as the Manager's culling calculation.
        private static bool TryGetIntensityRangeInputs(PointLightVolumeInstance light, float cutoff, out float peak, out Vector2 size) {
            peak = 0f;
            size = Vector2.zero;
            if (light == null || !IsFiniteHandleValue(cutoff) || cutoff <= 0f || !IsFiniteHandleValue(light.Intensity)) return false;
            if (light.LightType != 2 && light.Projection == 1 && light.FalloffLUT != null) return false;
            Color color = light.Color;
            if (!IsFiniteHandleValue(color.r) || !IsFiniteHandleValue(color.g) || !IsFiniteHandleValue(color.b)) return false;
            peak = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            if (peak <= 0f) return false;
            Vector3 scale = light.transform.lossyScale;
            float averageScale = (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
            if (!IsFiniteHandleValue(averageScale) || averageScale <= 0f) return false;
            if (light.LightType == 2) {
                size = new Vector2(averageScale * averageScale / Mathf.Max(Mathf.Abs(scale.x), MinAreaSize), Mathf.Max(Mathf.Abs(scale.y), MinAreaSize));
            } else {
                if (!IsFiniteHandleValue(light.LightSourceSize)) return false;
                size.x = Mathf.Max(Mathf.Abs(light.LightSourceSize), 0.0001f) * averageScale;
            }
            return IsFiniteHandleValue(size.x) && size.x > 0f && IsFiniteHandleValue(size.y);
        }

        internal static bool TryGetIntensityHandleRange(PointLightVolumeInstance light, float cutoff, out float range) {
            range = 0f;
            if (!TryGetIntensityRangeInputs(light, cutoff, out float peak, out Vector2 size)) return false;
            if (light.Intensity == 0f) return true;
            float squaredRange;
            if (light.LightType == 2) {
                float solidAngle = Mathf.Clamp(cutoff / (peak * (light.Intensity * Mathf.PI)), -Mathf.PI * 2f, Mathf.PI * 2f);
                squaredRange = ComputeAreaLightSquaredBoundingSphere(size.x, size.y, solidAngle);
            } else {
                squaredRange = ComputePointLightSquaredBoundingSphere(light.Color, light.Intensity, size.x, cutoff);
            }
            float calculatedRange = Mathf.Sqrt(Mathf.Max(squaredRange, 0f));
            if (!IsFiniteHandleValue(calculatedRange)) return false;
            range = calculatedRange;
            return true;
        }

        // Invert the existing culling formulas. A collapsed range turns the light off.
        internal static bool TryGetIntensityForRange(PointLightVolumeInstance light, float cutoff, float range, out float intensity) {
            intensity = 0f;
            if (!IsFiniteHandleValue(range) || range < 0f || !TryGetIntensityRangeInputs(light, cutoff, out float peak, out Vector2 size)) return false;
            if (range == 0f) return true;
            double distance = range;
            double magnitude;
            if (light.LightType == 2) {
                double area = (double)size.x * size.y;
                double diagonal = ((double)size.x * size.x + (double)size.y * size.y) * 0.25;
                double solidAngle = 4.0 * System.Math.Atan2(area, 2.0 * distance * System.Math.Sqrt(4.0 * distance * distance + diagonal));
                magnitude = cutoff / (Mathf.PI * (double)peak * solidAngle);
            } else {
                double relativeRange = distance / size.x;
                magnitude = (double)cutoff * cutoff * (1.0 + relativeRange * relativeRange) / (2.0 * Mathf.PI * peak);
            }
            intensity = (float)(light.Intensity < 0f ? -magnitude : magnitude);
            return IsFiniteHandleValue(intensity);
        }

        private static bool IsFiniteHandleValue(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // Keep a fixed plane during the drag so moving the emitter does not move its resize anchor.
        private void DrawAreaEditHandles(PointLightVolumeInstance light) {
            Transform t = light.transform;
            if (GUIUtility.hotControl == 0) _areaDragControl = 0;
            Matrix4x4 frame = _areaDragControl != 0 ? _areaDragFrame : Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
            Vector2 center = frame.inverse.MultiplyPoint3x4(t.position);
            Vector2 size = new Vector2(Mathf.Max(Mathf.Abs(t.lossyScale.x), MinAreaSize), Mathf.Max(Mathf.Abs(t.lossyScale.y), MinAreaSize));
            Rect rect = new Rect(center - size * 0.5f, size);
            using (new Handles.DrawingScope(frame)) {
                Handles.DrawWireCube(center, new Vector3(size.x, size.y, 0f));
                for (int i = 0; i < AreaHandleSides.Length; i++) {
                    Vector2 sides = AreaHandleSides[i];
                    Vector3 position = rect.center + Vector2.Scale(rect.size * 0.5f, sides);
                    int id = GUIUtility.GetControlID(FocusType.Passive);
                    float handleSize = HandleUtility.GetHandleSize(position) * 0.05f;
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved;
                    if (sides.x != 0f && sides.y != 0f) {
                        moved = Handles.Slider2D(id, position, Vector3.forward, Vector3.right, Vector3.up, handleSize, Handles.DotHandleCap,
                            new Vector2(EditorSnapSettings.move.x, EditorSnapSettings.move.y));
                    } else {
                        Vector3 direction = sides.x != 0f ? Vector3.right : Vector3.up;
                        moved = Handles.Slider(id, position, direction, handleSize, Handles.DotHandleCap, sides.x != 0f ? EditorSnapSettings.move.x : EditorSnapSettings.move.y);
                    }
                    bool changed = EditorGUI.EndChangeCheck();
                    if (GUIUtility.hotControl == id && _areaDragControl == 0) {
                        _areaDragControl = id;
                        _areaDragFrame = frame;
                        _areaDragRect = rect;
                    }
                    if (!changed) continue;
                    Rect edited = ResizeAreaRect(_areaDragRect, sides, moved, Event.current.alt, Event.current.shift);
                    Undo.RecordObjects(new Object[] { light, t }, "Edit Light");
                    ApplyAreaRect(t, frame, edited);
                    ApplyLightHandleChange(light, true);
                }
            }
        }

        // Rect Tool modifiers use the original rectangle throughout a drag.
        internal static Rect ResizeAreaRect(Rect start, Vector2 sides, Vector2 movedPosition, bool fromCenter, bool proportional) {
            Vector2 anchor = fromCenter ? start.center : start.center - Vector2.Scale(start.size * 0.5f, sides);
            Vector2 size = start.size;
            if (proportional) {
                Vector2 original = Vector2.Scale(start.size, sides) * (fromCenter ? 0.5f : 1f);
                float ratio = Vector2.Dot(movedPosition - anchor, original) / Mathf.Max(original.sqrMagnitude, 0.000001f);
                ratio = Mathf.Max(ratio, MinAreaSize / start.width, MinAreaSize / start.height);
                size *= ratio;
            } else {
                float multiplier = fromCenter ? 2f : 1f;
                if (sides.x != 0f) size.x = Mathf.Max(sides.x * (movedPosition.x - anchor.x) * multiplier, MinAreaSize);
                if (sides.y != 0f) size.y = Mathf.Max(sides.y * (movedPosition.y - anchor.y) * multiplier, MinAreaSize);
            }
            Vector2 center = fromCenter ? start.center : anchor + Vector2.Scale(size * 0.5f, sides);
            return new Rect(center - size * 0.5f, size);
        }

        // Area dimensions come from Transform scale. Preserve depth, mirroring and parent transforms.
        internal static void ApplyAreaRect(Transform target, Matrix4x4 frame, Rect rect) {
            Vector3 scale = target.localScale;
            if (scale.x == 0f) scale.x = 1f;
            if (scale.y == 0f) scale.y = 1f;
            target.localScale = scale;
            Vector3 worldScale = target.lossyScale;
            if (Mathf.Abs(worldScale.x) > 0.000001f) scale.x *= rect.width / Mathf.Abs(worldScale.x);
            if (Mathf.Abs(worldScale.y) > 0.000001f) scale.y *= rect.height / Mathf.Abs(worldScale.y);
            target.localScale = scale;
            target.position = frame.MultiplyPoint3x4(rect.center);
        }
    }
}
