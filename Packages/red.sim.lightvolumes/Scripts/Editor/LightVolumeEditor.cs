using System.Collections.Generic;
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
        private const float MinBoundsSize = 0.01f;
        private const float MinDivisorSize = 0.0001f;

        private bool _isEditMode;
        private bool _debugExpanded;
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

        // Caches the selected volume, serialized fields and editor-only preview state.
        private void OnEnable() {
            _volume = target as LightVolumeInstance;
            _debugExpanded = SessionState.GetBool(DebugFoldoutSessionKey, false);
            _previousTool = Tools.current;
            CacheProperties();
            CacheAtlasStates();
            LightVolumePreviewSceneRenderer.RequestRefresh();
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        // Draws authoring controls and synchronizes explicit inspector changes to runtime data.
        public override void OnInspectorGUI() {
            if (_volume == null) return;

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

        // Resolves every serialized authoring property used by the custom inspector.
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

        // Draws Scene View bounds editing and voxel preview toggles.
        private void DrawToolbar() {
            GUIContent editBounds = EditorGUIUtility.IconContent("EditCollider");
            editBounds.text = " Edit Bounds";
            editBounds.tooltip = "Edit the Light Volume bounds directly in the Scene view.";

            GUIContent previewVoxels = EditorGUIUtility.IconContent("LightProbeGroup Gizmo");
            previewVoxels.text = " Preview Voxels";
            previewVoxels.tooltip = "Preview Light Volume voxels in the Scene view.";

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

        // Displays estimated memory and compressed bundle cost for the current resolution.
        private void DrawDataSize() {
            int voxelCount = LightVolumeTools.GetVoxelCount(_volume, 1);
            GUILayout.Space(10f);
            if (voxelCount < 0) {
                EditorGUILayout.HelpBox("Volume density is too high and impossible to calculate and store! Consider using lower density.", MessageType.Error);
                return;
            }

            GUIStyle dataStyle = new GUIStyle(EditorStyles.label) { richText = true };
            GUILayout.Label(
                new GUIContent(
                    $"Size in VRAM: <b>{SizeInVRAM(voxelCount)} MB</b>",
                    "Estimated GPU memory used by this volume's three SH textures before atlas packing."),
                dataStyle);
            GUILayout.Label(
                new GUIContent(
                    $"Size in bundle: <b>{SizeInBundle(voxelCount)} MB (Approximately)</b>",
                    "Estimated compressed build size of this volume's three SH textures before atlas packing."),
                dataStyle);
        }

        // Warns when the installed Bakery version cannot represent the volume's rotation.
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

        // Draws volume lighting, baked data, correction and resolution properties.
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

        // Draws a serialized field while preserving its field-level tooltip for custom labels.
        private static void DrawProperty(SerializedProperty property, string label = null) {
            if (property == null) return;
            if (label == null) EditorGUILayout.PropertyField(property);
            else EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip));
        }

        // Draws the action that opens the Light Probe placement window.
        private void DrawProbeButton() {
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button) {
                fixedHeight = 20f,
                fixedWidth = ActionButtonWidth
            };

            GUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Generate Light Probes", "Opens the probe placer for this Light Volume."), buttonStyle) && _probePlacerWindow == null) {
                    _probePlacerWindow = LightProbePlacerWindow.Show(_volume);
                }
                GUILayout.FlexibleSpace();
            }
        }

        // Draws read-only registration, atlas and derived-transform diagnostics.
        private void DrawDebugSection() {
            GUILayout.Space(InspectorSectionSpacing);
            EditorGUI.BeginChangeCheck();
            _debugExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(
                _debugExpanded,
                new GUIContent("Debug", "Shows read-only live Light Volume data for troubleshooting."));
            if (EditorGUI.EndChangeCheck()) {
                SessionState.SetBool(DebugFoldoutSessionKey, _debugExpanded);
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

        // Exits bounds edit mode when the user selects another Unity transform tool.
        private void HandleEditModeState() {
            if (!_isEditMode || _previousTool == Tools.current) return;
            _previousTool = Tools.current;
            _isEditMode = false;
            Tools.hidden = false;
            RepaintAll();
        }

        // Enters or exits bounds edit mode while preserving the user's previous Unity tool.
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
        // Captures dependency-affecting values before the inspector applies a multi-object edit.
        private int[] CaptureBakeryDependencyStates() {
            int[] states = new int[targets.Length];
            for (int i = 0; i < targets.Length; i++) states[i] = GetBakeryDependencyState(targets[i] as LightVolumeInstance);
            return states;
        }

        // Updates Bakery helpers only for volumes whose relevant inspector settings changed.
        private void ApplyExplicitBakeryDependencyChanges(int[] previousStates) {
            for (int i = 0; i < targets.Length; i++) {
                LightVolumeInstance volume = targets[i] as LightVolumeInstance;
                if (volume == null || previousStates == null || i >= previousStates.Length || previousStates[i] == GetBakeryDependencyState(volume)) continue;
                LightVolumeManager manager = volume.LightVolumeManager;
                if (manager == null) continue;
                LightVolumeTools.SetupBakeryDependencies(volume, manager.IsBakeryMode && volume.Bake);
            }
        }

        // Hashes Manager, baking mode and volume bake state for explicit-change detection.
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

        // Rebuilds derived data and previews after an Undo or Redo operation.
        private void OnUndoRedoPerformed() {
            serializedObject.UpdateIfRequiredOrScript();
            SyncTargets(false, false);
            Repaint();
        }

        // Synchronizes all selected volumes, queues atlas changes and refreshes their Managers.
        private void SyncTargets(bool recordUndo, bool refreshRuntimeImmediately = true) {
            EnsureAtlasStateCache();
            HashSet<LightVolumeManager> runtimeManagers = null;
            for (int i = 0; i < targets.Length; i++) {
                LightVolumeInstance volume = targets[i] as LightVolumeInstance;
                if (volume == null) continue;

                int previousAtlasState = _atlasStateHashes[i];
                if (recordUndo) Undo.RecordObject(volume, "Update Light Volume");
                LightVolumeTools.ApplyRuntimeState(volume, true);

                LightVolumeManagerTools.CopyProxyToUdon(volume);
                if (volume.LightVolumeManager != null) {
                    if (runtimeManagers == null) runtimeManagers = new HashSet<LightVolumeManager>();
                    runtimeManagers.Add(volume.LightVolumeManager);
                }
                EditorUtility.SetDirty(volume);

                if (previousAtlasState != GetAtlasStateHash(volume) && volume.LightVolumeManager != null) {
                    LightVolumeManagerTools.QueueAtlasGeneration(volume.LightVolumeManager);
                }
            }

            if (runtimeManagers != null) {
                foreach (LightVolumeManager manager in runtimeManagers) {
                    if (refreshRuntimeImmediately) LightVolumeManagerTools.RefreshRuntimeManagerImmediately(manager);
                    else LightVolumeManagerTools.QueueRuntimeManagerRefresh(manager);
                }
            }

            CacheAtlasStates();
            LightVolumePreviewSceneRenderer.RequestRefresh();
            SceneView.RepaintAll();
        }

        // Draws visible and occluded wireframes for one Light Volume's oriented bounds.
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

        // Draws selected volume bounds and interactive face handles in the Scene View.
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

        // Draws six axis-aligned face handles that resize bounds around the dragged face.
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

                // Option (Alt) grows both faces at once, Shift scales every axis by the same ratio.
                // Control and Command snapping is already handled by the snap argument of Handles.Slider above.
                EventModifiers modifiers = Event.current.modifiers;
                bool symmetric = (modifiers & EventModifiers.Alt) != 0;
                bool uniform = (modifiers & EventModifiers.Shift) != 0;

                float delta = Vector3.Dot(movedPosition - handlePosition, worldDirection);
                float oldSize = scale[axis];
                float newSize = Mathf.Max(oldSize + (symmetric ? delta * 2f : delta), MinBoundsSize);

                Vector3 modifiedScale = scale;
                if (uniform && oldSize > MinDivisorSize) {
                    // Clamp through the ratio so the proportions survive the minimum size floor.
                    float ratio = newSize / oldSize;
                    float smallestAxis = Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z));
                    if (smallestAxis > MinDivisorSize) ratio = Mathf.Max(ratio, MinBoundsSize / smallestAxis);
                    modifiedScale = scale * ratio;
                } else {
                    modifiedScale[axis] = newSize;
                }

                Undo.RecordObject(volumeTransform, "Scale Bounds Size");
                Undo.RecordObject(_volume, "Scale Bounds Size");
                // Derived from the clamped scale so the opposite face never drifts.
                if (!symmetric) volumeTransform.position += worldDirection * (modifiedScale[axis] - oldSize) * 0.5f;
                LVUtils.SetLossyScale(volumeTransform, modifiedScale);
                SyncSingleTarget(_volume);
            }
        }

        // Immediately synchronizes one volume changed through a Scene View handle.
        private static void SyncSingleTarget(LightVolumeInstance volume) {
            LightVolumeTools.ApplyRuntimeState(volume, true);
            LightVolumeManagerTools.CopyProxyToUdon(volume);
            LightVolumeManagerTools.RefreshRuntimeManagerImmediately(volume.LightVolumeManager);
            EditorUtility.SetDirty(volume);
            LightVolumePreviewSceneRenderer.RequestRefresh();
        }

        // Ensures atlas-affecting state hashes match the current multi-object selection.
        private void EnsureAtlasStateCache() {
            if (_atlasStateHashes != null && _atlasStateHashes.Length == targets.Length) return;
            _atlasStateHashes = new int[targets.Length];
            CacheAtlasStates();
        }

        // Captures atlas-affecting state for every selected volume.
        private void CacheAtlasStates() {
            if (_atlasStateHashes == null || _atlasStateHashes.Length != targets.Length) {
                _atlasStateHashes = new int[targets.Length];
            }
            for (int i = 0; i < targets.Length; i++) {
                _atlasStateHashes[i] = GetAtlasStateHash(targets[i] as LightVolumeInstance);
            }
        }

        // Hashes baked textures and correction settings that require atlas regeneration.
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

        // Releases editor hooks and restores the Unity tool hidden by bounds editing.
        private void OnDisable() {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            LightVolumePreviewSceneRenderer.RequestRefresh();
            Tools.hidden = false;
            if (!_isEditMode) return;
            Tools.current = _savedTool;
            _previousTool = _savedTool;
        }

        // Formats the uncompressed three-texture GPU cost for a voxel count.
        private static string SizeInVRAM(int voxelCount) {
            double megabytes = (ulong)(voxelCount * 3f) * 8d / (1024d * 1024d);
            return megabytes.ToString("0.00");
        }

        // Formats the estimated compressed bundle cost for a voxel count.
        private static string SizeInBundle(int voxelCount) {
            double megabytes = (ulong)(voxelCount * 3f) * 8d * 0.315d / (1024d * 1024d);
            return megabytes.ToString("0.00");
        }

        // Schedules Scene View and editor-loop repaint work for the next update.
        private static void RepaintAll() {
            EditorApplication.update += ForceRepaintNextFrame;
        }

        // Performs the queued repaint once and removes its editor update callback.
        private static void ForceRepaintNextFrame() {
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
            EditorApplication.update -= ForceRepaintNextFrame;
        }

        // Keeps live debug values updating while their foldout is visible in play mode.
        public override bool RequiresConstantRepaint() {
            return _debugExpanded && EditorApplication.isPlaying;
        }
    }
}
