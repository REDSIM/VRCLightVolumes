using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes {
    [CanEditMultipleObjects]
    [CustomEditor(typeof(LightVolumeInstance))]
    public class LightVolumeEditor : Editor {
        private const string DebugFoldoutSessionKey = "VRCLightVolumes.LightVolumeEditor.DebugFoldout";
        private const float ToolbarButtonWidth = 150f;
        private const float ActionButtonWidth = 170f;
        private const float InspectorSectionSpacing = 10f;
        private const double RuntimeDebugRefreshInterval = 0.2d;

        private bool _isEditMode;
        private bool _debugExpanded;
        private double _nextRuntimeDebugRefresh;
        private Tool _savedTool;
        private Tool _previousTool;
        private LightProbePlacerWindow _probePlacerWindow;
        private LightVolumeInstance _volume;

        private int[] _atlasStateHashes;

        private SerializedProperty _isDynamic;
        private SerializedProperty _isAdditive;
        private SerializedProperty _color;
        private SerializedProperty _intensity;
        private SerializedProperty _smoothBlending;
        private SerializedProperty _texture0;
        private SerializedProperty _texture1;
        private SerializedProperty _texture2;
        private SerializedProperty _exposure;
        private SerializedProperty _shadows;
        private SerializedProperty _highlights;
        private SerializedProperty _bake;
        private SerializedProperty _reserveUvSpace;
        private SerializedProperty _adaptiveResolution;
        private SerializedProperty _voxelsPerUnit;
        private SerializedProperty _resolution;

        private void OnEnable() {
            _volume = target as LightVolumeInstance;
            _debugExpanded = SessionState.GetBool(DebugFoldoutSessionKey, false);
            _previousTool = Tools.current;
            CacheProperties();
            CacheAtlasStates();
            LightVolumePreviewSceneRenderer.RequestRefresh();
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        public override void OnInspectorGUI() {
            if (_volume == null) return;

            RefreshRuntimeDebugProxy();
            serializedObject.UpdateIfRequiredOrScript();
            int undoGroup = Undo.GetCurrentGroup();
#if BAKERY_INCLUDED
            int[] previousBakeryStates = CaptureBakeryDependencyStates();
#endif

            DrawToolbar();
            DrawDataSize();
            DrawBakeryWarning();
            HandleEditModeState();
            DrawAuthoringProperties();
            DrawProbeButton();
            DrawDebugSection();

            if (!serializedObject.ApplyModifiedProperties()) return;

#if BAKERY_INCLUDED
            ApplyExplicitBakeryDependencyChanges(previousBakeryStates);
#endif
            SyncTargets(true);
            Undo.CollapseUndoOperations(undoGroup);
        }

        private void CacheProperties() {
            _isDynamic = serializedObject.FindProperty("IsDynamic");
            _isAdditive = serializedObject.FindProperty("IsAdditive");
            _color = serializedObject.FindProperty("Color");
            _intensity = serializedObject.FindProperty("Intensity");
            _smoothBlending = serializedObject.FindProperty("SmoothBlending");
            _texture0 = serializedObject.FindProperty("Texture0");
            _texture1 = serializedObject.FindProperty("Texture1");
            _texture2 = serializedObject.FindProperty("Texture2");
            _exposure = serializedObject.FindProperty("Exposure");
            _shadows = serializedObject.FindProperty("Shadows");
            _highlights = serializedObject.FindProperty("Highlights");
            _bake = serializedObject.FindProperty("Bake");
            _reserveUvSpace = serializedObject.FindProperty("ReserveUVSpace");
            _adaptiveResolution = serializedObject.FindProperty("AdaptiveResolution");
            _voxelsPerUnit = serializedObject.FindProperty("VoxelsPerUnit");
            _resolution = serializedObject.FindProperty("Resolution");
        }

        private void DrawToolbar() {
            GUIContent editBounds = EditorGUIUtility.IconContent("EditCollider");
            editBounds.text = " Edit Bounds";

            GUIContent previewVoxels = EditorGUIUtility.IconContent("LightProbeGroup Gizmo");
            previewVoxels.text = " Preview Voxels";

            GUIStyle toggleStyle = new GUIStyle(GUI.skin.button) {
                imagePosition = ImagePosition.ImageLeft,
                fixedHeight = 20f,
                fixedWidth = ToolbarButtonWidth
            };

            GUILayout.Space(10f);
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();
                bool newEditMode = GUILayout.Toggle(_isEditMode, editBounds, toggleStyle);
                if (newEditMode != _isEditMode) SetEditMode(newEditMode);

                GUILayout.Space(10f);
                bool previewActive = LightVolumePreviewSceneRenderer.IsPreviewModeActive;
                bool newPreviewActive = GUILayout.Toggle(previewActive, previewVoxels, toggleStyle);
                if (newPreviewActive != previewActive) {
                    LightVolumePreviewSceneRenderer.SetPreviewMode(newPreviewActive);
                    RepaintAll();
                }
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawDataSize() {
            int voxelCount = LightVolumeTools.GetVoxelCount(_volume, 1);
            GUILayout.Space(10f);
            if (voxelCount < 0) {
                EditorGUILayout.HelpBox("Volume density is too high and impossible to calculate and store! Consider using lower density.", MessageType.Error);
                return;
            }

            GUIStyle dataStyle = new GUIStyle(EditorStyles.label) { richText = true };
            GUILayout.Label($"Size in VRAM: <b>{SizeInVRAM(voxelCount)} MB</b>", dataStyle);
            GUILayout.Label($"Size in bundle: <b>{SizeInBundle(voxelCount)} MB (Approximately)</b>", dataStyle);
        }

        private void DrawBakeryWarning() {
            LightVolumeManager manager = _volume.LightVolumeManager;
            if (manager == null || !manager.IsBakeryMode) return;

#if BAKERY_INCLUDED
            Vector3 euler = _volume.transform.rotation.eulerAngles;
            bool fullRotation = typeof(BakeryVolume).GetField("_rotateAroundXYZ") != null;
            if (fullRotation) return;

            bool yRotation = typeof(BakeryVolume).GetField("rotateAroundY") != null;
            bool unsupportedRotation = yRotation ? euler.x != 0f || euler.z != 0f : euler.x != 0f || euler.y != 0f || euler.z != 0f;
            if (!unsupportedRotation) return;

            GUILayout.Space(10f);
            EditorGUILayout.HelpBox(
                yRotation
                    ? "With your Bakery version, only Y-axis rotation is supported in the " +
                      "editor. Apply the latest Bakery patch to have full rotation support. " +
                      "Free rotation will still work at runtime."
                    : "With your Bakery version, volume rotation is not supported in the " +
                      "editor. Apply the latest Bakery patch to have full rotation support. " +
                      "Free rotation will still work at runtime.",
                MessageType.Warning);
#else
            GUILayout.Space(10f);
            EditorGUILayout.HelpBox("To use Bakery mode, please include Bakery into your project!", MessageType.Error);
#endif
        }

        private void DrawAuthoringProperties() {
            DrawProperty(_isDynamic, "Dynamic");
            DrawProperty(_isAdditive, "Additive");
            DrawProperty(_color);
            DrawProperty(_intensity);
            DrawProperty(_smoothBlending);

            DrawProperty(_texture0);
            DrawProperty(_texture1);
            DrawProperty(_texture2);

            DrawProperty(_exposure);
            DrawProperty(_shadows);
            DrawProperty(_highlights);

            DrawProperty(_bake);
            bool showReserve = _bake.hasMultipleDifferentValues || !_bake.boolValue;
            if (showReserve) DrawProperty(_reserveUvSpace);

            bool showResolution = _bake.hasMultipleDifferentValues || _bake.boolValue || _reserveUvSpace.hasMultipleDifferentValues || _reserveUvSpace.boolValue;
            if (!showResolution) return;

            DrawProperty(_adaptiveResolution);
            if (_adaptiveResolution.hasMultipleDifferentValues || _adaptiveResolution.boolValue) {
                DrawProperty(_voxelsPerUnit);
            }
            DrawProperty(_resolution);
        }

        private static void DrawProperty(SerializedProperty property, string label = null) {
            if (property == null) return;
            if (label == null) EditorGUILayout.PropertyField(property);
            else EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip));
        }

        private void DrawProbeButton() {
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button) {
                fixedHeight = 20f,
                fixedWidth = ActionButtonWidth
            };

            GUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Generate Light Probes", buttonStyle) && _probePlacerWindow == null) {
                    _probePlacerWindow = LightProbePlacerWindow.Show(_volume);
                }
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawDebugSection() {
            GUILayout.Space(InspectorSectionSpacing);
            EditorGUI.BeginChangeCheck();
            _debugExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(
                _debugExpanded,
                new GUIContent("Debug", "Shows read-only live Light Volume data for troubleshooting."));
            if (EditorGUI.EndChangeCheck()) {
                SessionState.SetBool(DebugFoldoutSessionKey, _debugExpanded);
                _nextRuntimeDebugRefresh = 0d;
            }

            if (_debugExpanded) {
                if (!EditorApplication.isPlaying)
                    EditorGUILayout.HelpBox("Live values are populated in Play Mode. Derived atlas and transform values show the current editor state.", MessageType.Info);
                if (targets.Length > 1)
                    EditorGUILayout.HelpBox("Debug values are shown for the first selected Light Volume.", MessageType.Info);

                LightVolumeDebugGUI.DrawGroupHeader(
                    "Registration",
                    false,
                    "Shows which Manager owns this volume and its registry priority.");
                LightVolumeDebugGUI.DrawObject("Manager", _volume.LightVolumeManager, typeof(LightVolumeManager), "The scene Manager used by this volume.");
                LightVolumeDebugGUI.DrawBool("Registered", _volume.RegisteredWithManagerPreview, "Whether this volume is currently in a Manager registry.");
                LightVolumeDebugGUI.DrawBool("Active", _volume.IsActive, "Whether this volume is currently eligible for rendering.");
                LightVolumeDebugGUI.DrawInt("Registry Order", _volume.RegistryOrder, "Stable tie-breaker used when registry weights are equal.");
                LightVolumeDebugGUI.DrawFloat("Registry Weight", _volume.RegistryWeight, "Higher weights are uploaded to shaders first.");

                LightVolumeDebugGUI.DrawGroupHeader(
                    "Atlas Placement",
                    true,
                    "Shows this volume's resolution and packed location in the shared 3D atlas.");
                LightVolumeDebugGUI.DrawVector3Int("Resolution", _volume.Resolution, "Voxel resolution used for this volume.");
                LightVolumeDebugGUI.DrawVector4("Texture 0 UVW", _volume.BoundsUvwMin0, "Packed atlas position for texture 0; W stores its X scale.");
                LightVolumeDebugGUI.DrawVector4("Texture 1 UVW", _volume.BoundsUvwMin1, "Packed atlas position for texture 1; W stores its Y scale.");
                LightVolumeDebugGUI.DrawVector4("Texture 2 UVW", _volume.BoundsUvwMin2, "Packed atlas position for texture 2; W stores its Z scale.");

                LightVolumeDebugGUI.DrawGroupHeader(
                    "Derived Transform",
                    true,
                    "Values calculated from the volume bounds and rotation for shaders.");
                LightVolumeDebugGUI.DrawVector4("Edge Smoothing", _volume.InvLocalEdgeSmoothing, "Inverse edge-blending distances used by shaders.");
                LightVolumeDebugGUI.DrawBool("Rotated", _volume.IsRotated, "Whether the volume is rotated relative to its baked pose.");
                LightVolumeDebugGUI.DrawQuaternion("Inverse Baked Rotation", _volume.InvBakedRotation, "Inverse rotation of the pose used when this volume was baked.");
                LightVolumeDebugGUI.DrawVector3("Relative Rotation Row 0", _volume.RelativeRotationRow0, "First row of the relative rotation used for directional lighting.");
                LightVolumeDebugGUI.DrawVector3("Relative Rotation Row 1", _volume.RelativeRotationRow1, "Second row of the relative rotation used for directional lighting.");
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void RefreshRuntimeDebugProxy() {
#if UDONSHARP
            if (!_debugExpanded || !EditorApplication.isPlaying || _volume == null) return;
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextRuntimeDebugRefresh) return;
            _nextRuntimeDebugRefresh = now + RuntimeDebugRefreshInterval;
            if (UdonSharpEditorUtility.GetBackingUdonBehaviour(_volume) != null)
                UdonSharpEditorUtility.CopyUdonToProxy(_volume);
#endif
        }

        private void HandleEditModeState() {
            if (!_isEditMode || _previousTool == Tools.current) return;
            _previousTool = Tools.current;
            _isEditMode = false;
            Tools.hidden = false;
            RepaintAll();
        }

        private void SetEditMode(bool enabled) {
            if (enabled) {
                _savedTool = Tools.current;
                _previousTool = Tool.None;
                Tools.current = Tool.None;
            } else {
                Tools.current = _savedTool;
                _previousTool = _savedTool;
            }

            _isEditMode = enabled;
            Tools.hidden = false;
            RepaintAll();
        }

#if BAKERY_INCLUDED
        private int[] CaptureBakeryDependencyStates() {
            int[] states = new int[targets.Length];
            for (int i = 0; i < targets.Length; i++) states[i] = GetBakeryDependencyState(targets[i] as LightVolumeInstance);
            return states;
        }

        private void ApplyExplicitBakeryDependencyChanges(int[] previousStates) {
            for (int i = 0; i < targets.Length; i++) {
                LightVolumeInstance volume = targets[i] as LightVolumeInstance;
                if (volume == null || previousStates == null || i >= previousStates.Length || previousStates[i] == GetBakeryDependencyState(volume)) continue;
                LightVolumeManager manager = volume.LightVolumeManager;
                if (manager == null) continue;
                LightVolumeTools.SetupBakeryDependencies(volume, manager.IsBakeryMode && volume.Bake);
            }
        }

        private static int GetBakeryDependencyState(LightVolumeInstance volume) {
            if (volume == null) return 0;
            LightVolumeManager manager = volume.LightVolumeManager;
            unchecked {
                int state = manager == null ? 0 : manager.GetInstanceID();
                state = state * 31 + (manager != null && manager.IsBakeryMode ? 1 : 0);
                state = state * 31 + (volume.Bake ? 1 : 0);
                return state;
            }
        }
#endif

        private void OnUndoRedoPerformed() {
            serializedObject.UpdateIfRequiredOrScript();
            SyncTargets(false);
            Repaint();
        }

        private void SyncTargets(bool recordUndo) {
            EnsureAtlasStateCache();
            for (int i = 0; i < targets.Length; i++) {
                LightVolumeInstance volume = targets[i] as LightVolumeInstance;
                if (volume == null) continue;

                int previousAtlasState = _atlasStateHashes[i];
                if (recordUndo) Undo.RecordObject(volume, "Update Light Volume");
                LightVolumeTools.ApplyRuntimeState(volume, true);

                if (UdonSharpEditorUtility.GetBackingUdonBehaviour(volume) != null) {
                    UdonSharpEditorUtility.CopyProxyToUdon(volume);
                }
                EditorUtility.SetDirty(volume);

                if (previousAtlasState != GetAtlasStateHash(volume) && volume.LightVolumeManager != null) {
                    LightVolumeManagerTools.QueueAtlasGeneration(volume.LightVolumeManager);
                }
            }

            CacheAtlasStates();
            LightVolumePreviewSceneRenderer.RequestRefresh();
            SceneView.RepaintAll();
        }

        private static void DrawVolumeBounds(LightVolumeInstance volume) {
            Handles.matrix = LightVolumeTools.GetMatrixTRS(volume);
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            Handles.color = UnityEngine.Color.white;
            Handles.DrawWireCube(Vector3.zero, Vector3.one);
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
            Handles.color = new UnityEngine.Color(1f, 1f, 1f, 0.2f);
            Handles.DrawWireCube(Vector3.zero, Vector3.one);
            Handles.matrix = Matrix4x4.identity;
        }

        protected void OnSceneGUI() {
            if (_volume == null) return;

            GameObject[] selection = Selection.gameObjects;
            for (int i = 0; i < selection.Length; i++) {
                LightVolumeInstance volume = selection[i].GetComponent<LightVolumeInstance>();
                if (volume != null) DrawVolumeBounds(volume);
            }

            if (!_isEditMode) return;
            Tools.hidden = true;
            DrawBoundsHandles();
        }

        private void DrawBoundsHandles() {
            Transform volumeTransform = _volume.transform;
            Vector3 position = LightVolumeTools.GetPosition(_volume);
            Quaternion rotation = LightVolumeTools.GetRotation(_volume);
            Vector3 scale = LightVolumeTools.GetScale(_volume);

            Color[] axisColors = {
                Handles.xAxisColor,
                Handles.yAxisColor,
                Handles.zAxisColor
            };

            for (int i = 0; i < 6; i++) {
                int axis = i / 2;
                bool positive = i % 2 == 0;
                Vector3 localDirection = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
                if (!positive) localDirection = -localDirection;

                Vector3 worldDirection = rotation * localDirection;
                Vector3 worldUp = rotation * (axis == 1 ? Vector3.right : Vector3.up);
                Handles.color = axisColors[axis];

                Vector3 handlePosition = position + worldDirection * scale[axis] * 0.5f;
                float handleSize = HandleUtility.GetHandleSize(handlePosition) * 0.2f;
                Vector3 handleOffset = handleSize * worldDirection * 0.5f;

                EditorGUI.BeginChangeCheck();
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                Vector3 movedPosition = Handles.Slider(handlePosition + handleOffset, worldDirection, handleSize, Handles.ConeHandleCap, 0.25f) - handleOffset;

                Quaternion planeRotation = Quaternion.LookRotation(worldDirection, worldUp);
                Vector3[] plane = LVUtils.GetPlaneVertices(handlePosition, planeRotation, handleSize);
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.DrawSolidRectangleWithOutline(plane, new UnityEngine.Color(1f, 1f, 1f, 0.15f), UnityEngine.Color.white);
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.DrawSolidRectangleWithOutline(plane, UnityEngine.Color.clear, new UnityEngine.Color(1f, 1f, 1f, 0.25f));

                if (!EditorGUI.EndChangeCheck()) continue;

                Undo.RecordObject(volumeTransform, "Scale Bounds Size");
                Undo.RecordObject(_volume, "Scale Bounds Size");
                float delta = Vector3.Dot(movedPosition - handlePosition, worldDirection);
                Vector3 modifiedScale = scale;
                modifiedScale[axis] += delta;
                volumeTransform.position += worldDirection * delta * 0.5f;
                LVUtils.SetLossyScale(volumeTransform, modifiedScale);
                SyncSingleTarget(_volume);
            }
        }

        private static void SyncSingleTarget(LightVolumeInstance volume) {
            LightVolumeTools.ApplyRuntimeState(volume, true);
            if (UdonSharpEditorUtility.GetBackingUdonBehaviour(volume) != null) {
                UdonSharpEditorUtility.CopyProxyToUdon(volume);
            }
            EditorUtility.SetDirty(volume);
            LightVolumePreviewSceneRenderer.RequestRefresh();
        }

        private void EnsureAtlasStateCache() {
            if (_atlasStateHashes != null && _atlasStateHashes.Length == targets.Length) return;
            _atlasStateHashes = new int[targets.Length];
            CacheAtlasStates();
        }

        private void CacheAtlasStates() {
            if (_atlasStateHashes == null || _atlasStateHashes.Length != targets.Length) {
                _atlasStateHashes = new int[targets.Length];
            }
            for (int i = 0; i < targets.Length; i++) {
                _atlasStateHashes[i] = GetAtlasStateHash(targets[i] as LightVolumeInstance);
            }
        }

        private static int GetAtlasStateHash(LightVolumeInstance volume) {
            if (volume == null) return 0;
            unchecked {
                int hash = 17;
                hash = hash * 31 + (volume.Texture0 == null ? 0 : volume.Texture0.GetInstanceID());
                hash = hash * 31 + (volume.Texture1 == null ? 0 : volume.Texture1.GetInstanceID());
                hash = hash * 31 + (volume.Texture2 == null ? 0 : volume.Texture2.GetInstanceID());
                hash = hash * 31 + volume.Exposure.GetHashCode();
                hash = hash * 31 + volume.Shadows.GetHashCode();
                hash = hash * 31 + volume.Highlights.GetHashCode();
                hash = hash * 31 + (volume.Bake ? 1 : 0);
                hash = hash * 31 + (volume.ReserveUVSpace ? 1 : 0);
                return hash;
            }
        }

        private void OnDisable() {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            LightVolumePreviewSceneRenderer.RequestRefresh();
            Tools.hidden = false;
            if (!_isEditMode) return;
            Tools.current = _savedTool;
            _previousTool = _savedTool;
        }

        private static string SizeInVRAM(int voxelCount) {
            double megabytes = (ulong)(voxelCount * 3f) * 8d / (1024d * 1024d);
            return megabytes.ToString("0.00");
        }

        private static string SizeInBundle(int voxelCount) {
            double megabytes = (ulong)(voxelCount * 3f) * 8d * 0.315d / (1024d * 1024d);
            return megabytes.ToString("0.00");
        }

        private static void RepaintAll() {
            EditorApplication.update += ForceRepaintNextFrame;
        }

        private static void ForceRepaintNextFrame() {
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
            EditorApplication.update -= ForceRepaintNextFrame;
        }

        public override bool RequiresConstantRepaint() {
            return _debugExpanded && EditorApplication.isPlaying;
        }
    }
}
