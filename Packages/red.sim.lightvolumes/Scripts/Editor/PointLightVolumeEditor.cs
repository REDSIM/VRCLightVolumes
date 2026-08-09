using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace VRCLightVolumes {
    [CanEditMultipleObjects]
    [CustomEditor(typeof(PointLightVolumeInstance))]
    public class PointLightVolumeEditor : UnityEditor.Editor {
        private const string DebugFoldoutSessionKey = "VRCLightVolumes.PointLightVolumeEditor.DebugFoldout";
        private PointLightVolumeInstance PointLightVolume;

        private static readonly GUIContent _bakeShadowsButtonContent = new GUIContent("Bake Shadows", "Bakes or re-bakes shadow maps for all selected lights with Shadows enabled.");
        private static readonly GUIContent _clearShadowsButtonContent = new GUIContent("Clear Shadows", "Removes the assigned shadow maps from all selected lights without deleting their source assets.");
        private static readonly GUIContent _emptyContent = GUIContent.none;
        private static readonly string _textureMaterialHint = "None (Texture/Material)";
        private static readonly string _cubemapMaterialHint = "None (Texture/Material)";
        private static readonly string _projectionSourceObjectPickerFilter = "t:Texture t:Material";
        private static readonly string[] _lightTypeNames = { "Point Light", "Spot Light", "Area Light" };
        private static readonly string[] _projectionNames = { "Parametric", "LUT", "Custom" };
        private const float ObjectSelectorButtonWidth = 19f;
        private const float InspectorSectionSpacing = 10f;
        private const float ShadowGroupSpacing = 6f;
        private const float ShadowButtonSpacing = 6f;
        private static readonly Color _shadowClipVisibleColor = new Color(0.2f, 0.65f, 1f, 0.75f);
        private static readonly Color _shadowClipHiddenColor = new Color(0.2f, 0.65f, 1f, 0.18f);
        private static GUIStyle _projectionSourceHintStyle;
        private bool _debugExpanded;

        // Caches the inspected light and restores its live debug foldout state.
        private void OnEnable() {
            PointLightVolume = target as PointLightVolumeInstance;
            _debugExpanded = SessionState.GetBool(DebugFoldoutSessionKey, false);
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        // Removes the Undo callback owned by this inspector.
        private void OnDisable() {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        // Draws type-specific authoring controls and synchronizes explicit changes.
        public override void OnInspectorGUI() {
            serializedObject.Update();
            int undoGroup = Undo.GetCurrentGroup();
            SerializedProperty lightTypeProperty = serializedObject.FindProperty("LightType");
            SerializedProperty projectionProperty = serializedObject.FindProperty("Projection");
            int lightType = Mathf.Clamp(lightTypeProperty.intValue, 0, 2);
            int projection = Mathf.Clamp(projectionProperty.intValue, 0, 2);
            DrawSectionHeader("Light", false);
            DrawPopup(lightTypeProperty, new GUIContent("Type", lightTypeProperty.tooltip), _lightTypeNames);
            lightType = Mathf.Clamp(lightTypeProperty.intValue, 0, 2);
            DrawProperty("IsDynamic", "Dynamic");
            DrawProperty("Color");
            DrawProperty("Intensity");
            DrawProperty("ShadingStrength", "Shading Strength");
            DrawProperty("BakeIntoProbes", "Bake Into Probes");
            DrawProperty("DebugRange", "Debug Range");

            DrawSectionHeader("Projection", true);
            if (lightType != 2) {
                DrawPopup(projectionProperty, new GUIContent("Projection", projectionProperty.tooltip), _projectionNames);
                projection = Mathf.Clamp(projectionProperty.intValue, 0, 2);
                if (projection == 1) DrawProperty("Range");
                else DrawProperty("LightSourceSize", "Light Source Size");
            }
            if (lightType == 1) DrawAngleDegrees();
            if (lightType == 1 && projection == 0) DrawProperty("Falloff");
            if (lightType == 1 && projection == 2) DrawProperty("SpotCookieAspect", "Spot Cookie Aspect");
            DrawActiveProjectionSourceField(lightType, projection);

            DrawSectionHeader("Shadows", true);
            SerializedProperty shadowsProperty = serializedObject.FindProperty("Shadows");
            EditorGUILayout.PropertyField(shadowsProperty, new GUIContent("Enabled", shadowsProperty.tooltip));
            bool drawShadowFields = shadowsProperty.hasMultipleDifferentValues || shadowsProperty.boolValue;
            bool propertiesChanged = serializedObject.ApplyModifiedProperties();

            if (drawShadowFields) {
                DrawProperty("WorldSpaceShadows", "Use World Space");
                GUILayout.Space(ShadowGroupSpacing);
                DrawLayerMask();
                DrawProperty("ExclusionMask", "Exclusion Mask");

                GUILayout.Space(ShadowGroupSpacing);
                DrawProperty("NearClip", "Near Plane");
                DrawProperty("FarClip", "Far Plane");
                if (lightType == 1) DrawProperty("ForceCubemapShadows", "Force Cubemap Shadows");
                DrawProperty("DebugClipPlanes", "Debug Clip Planes");

                GUILayout.Space(ShadowGroupSpacing);
                DrawProperty("Bias");
                DrawProperty("Blur");
                DrawProperty("ContactHardening", "Contact Hardening");

                GUILayout.Space(ShadowGroupSpacing);
                DrawTextureMaterialField("ShadowMap", _cubemapMaterialHint, true);

                DrawSectionHeader("Shadow Baking", true);
                DrawProperty("BakeInGame", "Bake In Game");
                DrawProperty("RebakeShadows", "Rebake Shadows");

                SerializedProperty shadowMapProperty = serializedObject.FindProperty("ShadowMap");
                GUILayout.Space(ShadowButtonSpacing);
                using (new EditorGUILayout.HorizontalScope()) {
                    if (GUILayout.Button(_bakeShadowsButtonContent)) {
                        propertiesChanged |= serializedObject.ApplyModifiedProperties();
                        BakeSelectedShadowMaps();
                        serializedObject.Update();
                        shadowMapProperty = serializedObject.FindProperty("ShadowMap");
                    }
                    using (new EditorGUI.DisabledScope(!shadowMapProperty.hasMultipleDifferentValues && shadowMapProperty.objectReferenceValue == null)) {
                        if (GUILayout.Button(_clearShadowsButtonContent)) shadowMapProperty.objectReferenceValue = null;
                    }
                }
            }

            DrawDebugSection();
            propertiesChanged |= serializedObject.ApplyModifiedProperties();
            if (!propertiesChanged) return;

            SyncTargets(true);
            Undo.CollapseUndoOperations(undoGroup);
        }

        // Draws a bold inspector section title with optional leading spacing.
        private static void DrawSectionHeader(string title, bool addTopSpacing) {
            if (addTopSpacing) GUILayout.Space(InspectorSectionSpacing);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        // Draws resolved light, projection, shadow and runtime-bake diagnostics.
        private void DrawDebugSection() {
            GUILayout.Space(InspectorSectionSpacing);
            EditorGUI.BeginChangeCheck();
            _debugExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(_debugExpanded, new GUIContent("Debug", "Shows read-only live Point Light Volume data for troubleshooting."));
            if (EditorGUI.EndChangeCheck()) SessionState.SetBool(DebugFoldoutSessionKey, _debugExpanded);

            if (_debugExpanded && PointLightVolume != null) {
                if (!EditorApplication.isPlaying) EditorGUILayout.HelpBox("Live values are populated in Play Mode. Resolved light, projection and shadow values show the current editor state.", MessageType.Info);
                if (targets.Length > 1) EditorGUILayout.HelpBox("Debug values are shown for the first selected Point Light Volume.", MessageType.Info);

                LightVolumeDebugGUI.DrawGroupHeader("Registration", false, "Shows which Manager owns this light and its registry priority.");
                LightVolumeDebugGUI.DrawObject(serializedObject, nameof(PointLightVolumeInstance.LightVolumeManager), PointLightVolume.LightVolumeManager, typeof(LightVolumeManager), "Manager");
                LightVolumeDebugGUI.DrawBool("Registered", PointLightVolume.RegisteredWithManagerPreview, "Whether this light is currently in a Manager registry.");
                LightVolumeDebugGUI.DrawBool("Active", PointLightVolume.IsActive, "Whether this light is currently eligible for rendering.");
                LightVolumeDebugGUI.DrawInt(serializedObject, nameof(PointLightVolumeInstance.RegistryOrder), PointLightVolume.RegistryOrder);
                LightVolumeDebugGUI.DrawFloat(serializedObject, nameof(PointLightVolumeInstance.RegistryWeight), PointLightVolume.RegistryWeight);

                LightVolumeDebugGUI.DrawGroupHeader("Resolved Light Data", true, "Values calculated from the Transform and light settings for shaders.");
                LightVolumeDebugGUI.DrawVector3(serializedObject, nameof(PointLightVolumeInstance.Position), PointLightVolume.Position);
                if (PointLightVolume.LightType != 2) LightVolumeDebugGUI.DrawVector3(serializedObject, nameof(PointLightVolumeInstance.Direction), PointLightVolume.Direction);
                LightVolumeDebugGUI.DrawQuaternion(serializedObject, nameof(PointLightVolumeInstance.Rotation), PointLightVolume.Rotation);
                LightVolumeDebugGUI.DrawFloat(serializedObject, nameof(PointLightVolumeInstance.SquaredRange), PointLightVolume.SquaredRange);
                LightVolumeDebugGUI.DrawFloat(serializedObject, nameof(PointLightVolumeInstance.SquaredScale), PointLightVolume.SquaredScale);
                LightVolumeDebugGUI.DrawBool("Range Dirty", PointLightVolume.IsRangeDirty, "Whether the Manager still needs to recalculate the effective range.");

                LightVolumeDebugGUI.DrawGroupHeader("Resolved Projection", true, "Resolved runtime source and layout for this light's projection.");
                LightVolumeDebugGUI.DrawText(serializedObject, nameof(PointLightVolumeInstance.ProjectionMode), GetProjectionModeName(PointLightVolume.ProjectionMode));
                LightVolumeDebugGUI.DrawText(serializedObject, nameof(PointLightVolumeInstance.ProjectionType), GetSourceTypeName(PointLightVolume.ProjectionType), "Source Type");
                LightVolumeDebugGUI.DrawObject(serializedObject, nameof(PointLightVolumeInstance.CustomTexture), PointLightVolume.CustomTexture, typeof(Texture), "Texture");
                LightVolumeDebugGUI.DrawObject(serializedObject, nameof(PointLightVolumeInstance.CustomTextureMaterial), PointLightVolume.CustomTextureMaterial, typeof(Material), "Material");
                LightVolumeDebugGUI.DrawBool("Cubemap Source", PointLightVolume.CustomTextureIsCubemap, "Whether the resolved texture is a cubemap.");
                LightVolumeDebugGUI.DrawBool("Depth Slices", PointLightVolume.CustomTextureHasDepthSlices, "Whether the resolved texture already contains array slices.");
                LightVolumeDebugGUI.DrawBool(serializedObject, nameof(PointLightVolumeInstance.AutoUpdateCustomTexture), PointLightVolume.AutoUpdateCustomTexture, "Dynamic Source");

                if (PointLightVolume.LightType == 2) {
                    LightVolumeDebugGUI.DrawGroupHeader("Area Cookie", true, "Live fallback color and GPU readback state for an Area Light cookie.");
                    LightVolumeDebugGUI.DrawText("Fallback Color", "#" + ColorUtility.ToHtmlStringRGBA(PointLightVolume.AreaLightFallbackColor), "Average cookie color used before detailed projection data is ready.");
                    LightVolumeDebugGUI.DrawFloat("Mirror", PointLightVolume.AreaCookieMirror, "Sign used to keep the Area Light cookie orientation correct.");
                    LightVolumeDebugGUI.DrawInt("Average Custom ID", PointLightVolume.AreaCookieAverageCustomId, "Runtime cookie-array source used for average-color readback.");
                    LightVolumeDebugGUI.DrawBool("Readback Pending", PointLightVolume.AreaCookieAverageReadbackPending, "Whether an average-color GPU readback is currently pending.");
                    LightVolumeDebugGUI.DrawBool("Readback Dirty", PointLightVolume.AreaCookieAverageReadbackDirty, "Whether the cookie average must be read again.");
                }

                if (PointLightVolume.Shadows) {
                    LightVolumeDebugGUI.DrawGroupHeader("Resolved Shadows", true, "Resolved shadow source and bake pose used by shaders.");
                    LightVolumeDebugGUI.DrawObject(serializedObject, nameof(PointLightVolumeInstance.ShadowMapTexture), PointLightVolume.ShadowMapTexture, typeof(Texture), "Texture");
                    LightVolumeDebugGUI.DrawObject(serializedObject, nameof(PointLightVolumeInstance.ShadowMapMaterial), PointLightVolume.ShadowMapMaterial, typeof(Material), "Material");
                    LightVolumeDebugGUI.DrawFloat(serializedObject, nameof(PointLightVolumeInstance.ShadowMapID), PointLightVolume.ShadowMapID);
                    LightVolumeDebugGUI.DrawBool("Uses Cubemap", PointLightVolume.ShadowMapUsesCubemap, "Whether this light samples a six-face shadow.");
                    LightVolumeDebugGUI.DrawBool("Cubemap Source", PointLightVolume.ShadowMapTextureIsCubemap, "Whether the assigned shadow texture is a cubemap.");
                    LightVolumeDebugGUI.DrawBool("Depth Slices", PointLightVolume.ShadowMapTextureHasDepthSlices, "Whether the assigned texture already contains array slices.");
                    LightVolumeDebugGUI.DrawBool(serializedObject, nameof(PointLightVolumeInstance.AutoUpdateShadowMap), PointLightVolume.AutoUpdateShadowMap, "Dynamic Source");
                    LightVolumeDebugGUI.DrawFloat("Baked Far Clip", PointLightVolume.BakedFarClip, "Far clipping plane used to encode the current shadow map.");
                    LightVolumeDebugGUI.DrawVector3(serializedObject, nameof(PointLightVolumeInstance.ShadowBakePosition), PointLightVolume.ShadowBakePosition, "Bake Position");
                    LightVolumeDebugGUI.DrawQuaternion(serializedObject, nameof(PointLightVolumeInstance.ShadowBakeRotation), PointLightVolume.ShadowBakeRotation, "Bake Rotation");
                }

                if (PointLightVolume.BakeInGame) {
                    LightVolumeDebugGUI.DrawGroupHeader("Runtime Shadow Baking", true, "Live state and temporary resources used while baking shadows in-game.");
                    LightVolumeDebugGUI.DrawBool("Bake Started", PointLightVolume.RuntimeShadowBakeStartedPreview, "Whether this light has started its runtime shadow bake.");
                    LightVolumeDebugGUI.DrawBool("Source Initialized", PointLightVolume.RuntimeShadowSourceInitializedPreview, "Whether the runtime shadow source is ready for the Manager.");
                    LightVolumeDebugGUI.DrawInt("Current Face", PointLightVolume.RuntimeShadowFaceIndexPreview, "Next cubemap face to render; non-cubemap shadows use one face.");
                    LightVolumeDebugGUI.DrawFloat("Receiver Near Plane", PointLightVolume.RuntimeShadowReceiverNearClipPreview, "Near clipping plane used by the runtime shadow receiver.");
                    LightVolumeDebugGUI.DrawFloat("Receiver Far Plane", PointLightVolume.RuntimeShadowReceiverFarClipPreview, "Far clipping plane used by the runtime shadow receiver.");
                    LightVolumeDebugGUI.DrawObject("Depth Texture", PointLightVolume.RuntimeShadowDepthTexturePreview, typeof(RenderTexture), "Temporary camera-depth render target.");
                    LightVolumeDebugGUI.DrawObject("Output Texture", PointLightVolume.RuntimeShadowTexturePreview, typeof(RenderTexture), "Runtime shadow result generated by this light.");
                    LightVolumeDebugGUI.DrawObject("Registered Texture", PointLightVolume.RuntimeShadowRegistrationTexturePreview, typeof(RenderTexture), "Texture currently registered in the Manager's shadow array.");
                    LightVolumeDebugGUI.DrawObject("Depth Material", PointLightVolume.RuntimeShadowDepthEncodeMaterial, typeof(Material), "Material that converts camera depth into shadow data.");
                    LightVolumeDebugGUI.DrawObject("Blur Material", PointLightVolume.RuntimeShadowBlurMaterial, typeof(Material), "Material that filters the runtime shadow result.");
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // Converts a packed runtime projection mode to a readable inspector label.
        private static string GetProjectionModeName(int value) {
            if (value == 1) return "LUT";
            if (value == 2) return "Custom";
            return "Parametric";
        }

        // Converts a packed projection source type to a readable inspector label.
        private static string GetSourceTypeName(int value) {
            if (value == 1) return "Texture";
            if (value == 2) return "Material";
            return "None";
        }

        // Draws a serialized field while preserving its field-level tooltip.
        private void DrawProperty(string propertyName, string label = null) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (label == null) EditorGUILayout.PropertyField(property, true);
            else EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip), true);
        }

        // Draws an integer-backed popup with correct mixed-selection handling.
        private static void DrawPopup(SerializedProperty property, GUIContent label, string[] names) {
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.Popup(label, Mathf.Clamp(property.intValue, 0, names.Length - 1), names);
            if (EditorGUI.EndChangeCheck()) property.intValue = value;
            EditorGUI.showMixedValue = false;
        }

        // Presents the runtime half-angle radians field as a full cone angle in degrees.
        private void DrawAngleDegrees() {
            SerializedProperty angleProperty = serializedObject.FindProperty("Angle");
            float angleDegrees = angleProperty.floatValue * Mathf.Rad2Deg * 2f;
            EditorGUI.showMixedValue = angleProperty.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            angleDegrees = EditorGUILayout.Slider(new GUIContent("Angle", "Angle of a spotlight cone in degrees."), angleDegrees, 0.1f, 360f);
            if (EditorGUI.EndChangeCheck()) angleProperty.floatValue = angleDegrees * Mathf.Deg2Rad * 0.5f;
            EditorGUI.showMixedValue = false;
        }

        // Draws the serialized shadow layer mask using Unity's named layers.
        private void DrawLayerMask() {
            SerializedProperty layerMaskProperty = serializedObject.FindProperty("LayerMask");
            EditorGUI.showMixedValue = layerMaskProperty.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.MaskField(new GUIContent("Layer Mask", layerMaskProperty.tooltip), layerMaskProperty.intValue, InternalEditorUtility.layers);
            if (EditorGUI.EndChangeCheck()) layerMaskProperty.intValue = value;
            EditorGUI.showMixedValue = false;
        }

        // Bakes selected lights and rebuilds the primary Manager's shadow array once.
        private void BakeSelectedShadowMaps() {
            LightVolumeManager manager = LightVolumeManagerEditorBackend.GetPrimaryManager();
            bool rebuildShadowTextures = false;
            bool synchronized = false;
            for (int i = 0; i < targets.Length; i++) {
                PointLightVolumeInstance pointLightVolume = targets[i] as PointLightVolumeInstance;
                if (pointLightVolume == null || !pointLightVolume.Shadows) continue;
                PointLightVolumeEditorUtility.Sync(pointLightVolume, false, false);
                if (manager != null && pointLightVolume.LightVolumeManager == manager) synchronized = true;
                if (!PointLightShadowBaker.BakeShadowMap(pointLightVolume, $"| {pointLightVolume.gameObject.name} ({i + 1}/{targets.Length})", false)) continue;
                PointLightVolumeEditorUtility.Sync(pointLightVolume, false, false);
                if (manager != null && pointLightVolume.LightVolumeManager == manager) rebuildShadowTextures = true;
            }
            if (rebuildShadowTextures) LightVolumeManagerEditorBackend.ReinitializeShadowTextures(manager);
            else if (synchronized) LightVolumeManagerEditorBackend.RefreshManagerOnce(manager, true);
        }

        // Applies all selected proxies first, then rebuilds each shared array at most once.
        private void SyncTargets(bool recordUndo, bool reinitializeTextures = false, bool refreshRuntimeImmediately = true) {
            LightVolumeManager manager = LightVolumeManagerEditorBackend.GetPrimaryManager();
            int managerChanges = 0;
            bool refreshManager = false;
            for (int i = 0; i < targets.Length; i++) {
                PointLightVolumeInstance pointLightVolume = targets[i] as PointLightVolumeInstance;
                if (pointLightVolume == null) continue;
                int changes = PointLightVolumeEditorUtility.Sync(pointLightVolume, recordUndo, false);
                if (reinitializeTextures) changes |= PointLightVolumeEditorUtility.CustomTexturesChanged | PointLightVolumeEditorUtility.ShadowTexturesChanged;
                if (manager == null || pointLightVolume.LightVolumeManager != manager) continue;
                refreshManager = true;
                managerChanges |= changes;
            }

            if (managerChanges != 0) {
                if (recordUndo) Undo.RecordObject(manager, "Sync Point Light Volume Textures");
                LightVolumeManagerEditorBackend.ReinitializeTextures(manager, (managerChanges & PointLightVolumeEditorUtility.CustomTexturesChanged) != 0, (managerChanges & PointLightVolumeEditorUtility.ShadowTexturesChanged) != 0);
            } else if (refreshManager) {
                LightVolumeManagerEditorBackend.RefreshManagerOnce(manager, refreshRuntimeImmediately);
            }
        }

        // Rebuilds derived data and both texture arrays after an Undo or Redo operation.
        private void OnUndoRedoPerformed() {
            // Undo also restores hidden derived source fields, which makes ordinary source-change detection intentionally inconclusive. Rebuild both shared arrays once per manager.
            SyncTargets(false, true, false);
            Repaint();
        }

        // Draws only the texture or material source relevant to the selected projection mode.
        private void DrawActiveProjectionSourceField(int lightType, int projection) {
            if (lightType == 2) {
                DrawTextureMaterialField("Cookie", _textureMaterialHint, false);
                return;
            }
            if (projection == 0) return;
            if (projection == 1) DrawTextureMaterialField("FalloffLUT", _textureMaterialHint, false);
            else if (lightType == 0) DrawTextureMaterialField("Cubemap", _cubemapMaterialHint, false);
            else if (lightType == 1) DrawTextureMaterialField("Cookie", _textureMaterialHint, false);
        }

        // Resolves and draws a named texture-or-material serialized property.
        private void DrawTextureMaterialField(string propertyName, string acceptedTypesHint, bool isShadowSource) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            DrawTextureMaterialField(property, EditorGUIUtility.TrTextContent(property.displayName, property.tooltip), acceptedTypesHint, isShadowSource);
        }

        // Draws a filtered object field with mixed values, picker support and an empty-state hint.
        private void DrawTextureMaterialField(SerializedProperty property, GUIContent label, string acceptedTypesHint, bool isShadowSource) {
            Rect rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginProperty(rect, label, property);
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            int controlID = GUIUtility.GetControlID(FocusType.Keyboard, rect);
            Rect fieldRect = EditorGUI.PrefixLabel(rect, controlID, label);
            bool drawHint = !property.hasMultipleDifferentValues && property.objectReferenceValue == null;
            bool hideNativeEmptyText = drawHint && Event.current.type == EventType.Repaint;
            Color contentColor = GUI.contentColor;
            if (hideNativeEmptyText) GUI.contentColor = new Color(contentColor.r, contentColor.g, contentColor.b, 0f);

            ShowProjectionSourcePickerOnSelectorClick(property, fieldRect, controlID);
            EditorGUI.BeginChangeCheck();
            UnityEngine.Object value = EditorGUI.ObjectField(fieldRect, _emptyContent, property.objectReferenceValue, typeof(UnityEngine.Object), false);
            if (hideNativeEmptyText) GUI.contentColor = contentColor;
            if (EditorGUI.EndChangeCheck()) property.objectReferenceValue = IsSupportedTextureMaterialSource(value, isShadowSource) ? value : null;
            UpdateProjectionSourceFromPicker(property, controlID, isShadowSource);
            if (drawHint) DrawProjectionSourceHint(fieldRect, acceptedTypesHint);
            EditorGUI.showMixedValue = false;
            EditorGUI.EndProperty();
        }

        // Opens a filtered Unity object picker when the field's selector button is clicked.
        private void ShowProjectionSourcePickerOnSelectorClick(SerializedProperty property, Rect fieldRect, int controlID) {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0) return;
            Rect selectorRect = fieldRect;
            selectorRect.xMin = selectorRect.xMax - ObjectSelectorButtonWidth;
            if (!selectorRect.Contains(currentEvent.mousePosition)) return;
            EditorGUIUtility.ShowObjectPicker<UnityEngine.Object>(property.objectReferenceValue, false, _projectionSourceObjectPickerFilter, controlID);
            currentEvent.Use();
        }

        // Accepts supported picker selections and rejects incompatible projection sources.
        private void UpdateProjectionSourceFromPicker(SerializedProperty property, int controlID, bool isShadowSource) {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.ExecuteCommand) return;
            string commandName = currentEvent.commandName;
            if (commandName != "ObjectSelectorUpdated" && commandName != "ObjectSelectorClosed") return;
            if (EditorGUIUtility.GetObjectPickerControlID() != controlID) return;
            if (commandName == "ObjectSelectorUpdated") {
                UnityEngine.Object value = EditorGUIUtility.GetObjectPickerObject();
                property.objectReferenceValue = IsSupportedTextureMaterialSource(value, isShadowSource) ? value : null;
            }
            currentEvent.Use();
        }

        // Draws accepted source types inside an otherwise empty object field.
        private void DrawProjectionSourceHint(Rect fieldRect, string acceptedTypesHint) {
            if (Event.current.type != EventType.Repaint) return;
            if (_projectionSourceHintStyle == null) {
                _projectionSourceHintStyle = new GUIStyle(EditorStyles.label);
                RectOffset objectFieldPadding = EditorStyles.objectField.padding;
                _projectionSourceHintStyle.padding = new RectOffset(objectFieldPadding.left, 0, objectFieldPadding.top, objectFieldPadding.bottom);
                _projectionSourceHintStyle.alignment = EditorStyles.objectField.alignment;
                _projectionSourceHintStyle.normal.textColor = EditorStyles.objectField.normal.textColor;
                _projectionSourceHintStyle.clipping = TextClipping.Clip;
            }
            Rect hintRect = fieldRect;
            hintRect.xMax -= ObjectSelectorButtonWidth;
            GUI.Label(hintRect, acceptedTypesHint, _projectionSourceHintStyle);
        }

        // Validates a texture or material against the current projection and shadow requirements.
        private bool IsSupportedTextureMaterialSource(UnityEngine.Object value, bool isShadowSource) {
            if (value == null) return true;
            if (isShadowSource) return value is Texture2DArray || value is Cubemap || value is RenderTexture || value is Material;
            if (value is RenderTexture || value is Material) return true;
            if (!(value is Texture)) return false;
            int lightType = Mathf.Clamp(serializedObject.FindProperty("LightType").intValue, 0, 2);
            int projection = Mathf.Clamp(serializedObject.FindProperty("Projection").intValue, 0, 2);
            return lightType == 2 || projection == 1 || projection == 2 && (lightType == 0 || lightType == 1);
        }

        // Returns the owning Manager's culling cutoff or the package default.
        private static float GetBrightnessCutoff(PointLightVolumeInstance pointLightVolume) {
            return pointLightVolume.LightVolumeManager != null ? pointLightVolume.LightVolumeManager.LightsBrightnessCutoff : 0.35f;
        }

        // Draws the Scene View shape, range and optional debug bounds for one light.
        private void DrawVolumeGUI(PointLightVolumeInstance pointLightVolume) {

            Transform t = pointLightVolume.transform;
            Vector3 origin = t.position;
            Vector3 lscale = pointLightVolume.transform.lossyScale;
            float scale = (lscale.x + lscale.y + lscale.z) / 3;
            float range = pointLightVolume.LightType != 2 && (pointLightVolume.Projection != 1 || pointLightVolume.FalloffLUT == null) ? pointLightVolume.LightSourceSize : pointLightVolume.Range;
            range *= scale;

            if (pointLightVolume.LightType == 0) { // Point Light Visualization

                // Calculating
                float bounds = 0;
                bool isDebug = pointLightVolume.DebugRange && (pointLightVolume.Projection != 1 || pointLightVolume.FalloffLUT == null);
                if (isDebug) bounds = Mathf.Sqrt(ComputePointLightSquaredBoundingSphere(pointLightVolume.Color, pointLightVolume.Intensity, range, GetBrightnessCutoff(pointLightVolume)));

                // Drawing
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawPointLight(origin, range);
                if (isDebug) DrawPointLight(origin, bounds);
                DrawShadowClipGUI(pointLightVolume, origin, t);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawPointLight(origin, range);
                if (isDebug) DrawPointLight(origin, bounds);
                DrawShadowClipGUI(pointLightVolume, origin, t);

            } else if (pointLightVolume.LightType == 1) { // Spot Light Visualization

                // Calculating
                Vector3 forward = t.forward;
                Vector3 right = t.right;
                Vector3 up = t.up;
                float halfAngleRad = Mathf.Clamp(pointLightVolume.Angle, 0.05f * Mathf.Deg2Rad, Mathf.PI);
                Vector3[] dirs = new Vector3[] { right, -right, up, -up };
                float bounds = 0;
                bool isDebug = pointLightVolume.DebugRange && (pointLightVolume.Projection != 1 || pointLightVolume.FalloffLUT == null);
                if (isDebug) bounds = Mathf.Sqrt(ComputePointLightSquaredBoundingSphere(pointLightVolume.Color, pointLightVolume.Intensity, range, GetBrightnessCutoff(pointLightVolume)));

                // Drawing
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawSpotLight(origin, forward, halfAngleRad, range, dirs);

                if (isDebug) DrawSpotLight(origin, forward, halfAngleRad, bounds, dirs);
                DrawShadowClipGUI(pointLightVolume, origin, t);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawSpotLight(origin, forward, halfAngleRad, range, dirs);

                if (isDebug) DrawSpotLight(origin, forward, halfAngleRad, bounds, dirs);
                DrawShadowClipGUI(pointLightVolume, origin, t);

            } else { // Area light

                float x = Mathf.Max(Mathf.Abs(pointLightVolume.transform.lossyScale.x), 0.001f);
                float y = Mathf.Max(Mathf.Abs(pointLightVolume.transform.lossyScale.y), 0.001f);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawAreaLight(origin, t.rotation, x, y);

                if(pointLightVolume.DebugRange) DrawAreaLightDebug(origin, t.rotation, x, y, pointLightVolume.Color, pointLightVolume.Intensity, GetBrightnessCutoff(pointLightVolume));
                DrawShadowClipGUI(pointLightVolume, origin, t);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawAreaLight(origin, t.rotation, x, y);

                if (pointLightVolume.DebugRange) DrawAreaLightDebug(origin, t.rotation, x, y, pointLightVolume.Color, pointLightVolume.Intensity, GetBrightnessCutoff(pointLightVolume));
                DrawShadowClipGUI(pointLightVolume, origin, t);

            }

        }

        // Draws Scene View gizmos for every selected Point Light Volume.
        void OnSceneGUI() {
            foreach (var obj in Selection.gameObjects) {
                var volume = obj.GetComponent<PointLightVolumeInstance>();
                if (volume != null) DrawVolumeGUI(volume);
            }
        }

        // Draws a spotlight visualization using precalculated values
        private void DrawSpotLight(Vector3 origin, Vector3 forward, float halfAngleRad, float range, Vector3[] dirs) {

            float centerOffset = range * Mathf.Cos(halfAngleRad);
            Vector3 diskCenter = origin + forward * centerOffset;
            float radius = Mathf.Abs(range) * Mathf.Sin(halfAngleRad);
            float angleDeg = Mathf.Rad2Deg * halfAngleRad;

            Handles.DrawWireDisc(diskCenter, forward, radius);

            foreach (var dir in dirs) {
                Vector3 edge = diskCenter + dir * radius;
                Handles.DrawLine(origin, edge);
                Handles.DrawWireArc(origin, dir, forward, angleDeg, range);
            }
        }

        // Draws a pointlight visualization
        private void DrawPointLight(Vector3 center, float radius) {
            Handles.DrawWireArc(center, Vector3.right, Vector3.up, 360, radius);
            Handles.DrawWireArc(center, Vector3.up, Vector3.forward, 360, radius);
            Handles.DrawWireArc(center, Vector3.forward, Vector3.right, 360, radius);
        }

        // Draws the manually controlled shadow bake near-far space.
        private void DrawShadowClipGUI(PointLightVolumeInstance pointLightVolume, Vector3 origin, Transform transform) {
            if (!pointLightVolume.Shadows || !pointLightVolume.DebugClipPlanes) return;

            Handles.color = Handles.zTest == UnityEngine.Rendering.CompareFunction.LessEqual ? _shadowClipVisibleColor : _shadowClipHiddenColor;
            float nearClip = pointLightVolume.GetShadowNearClip();
            float farClip = pointLightVolume.GetShadowFarClip();
            bool drawSpotFrustum = pointLightVolume.LightType == 1 && !pointLightVolume.ShouldBakeCubemapShadows();
            if (!drawSpotFrustum) {
                DrawPointLight(origin, nearClip);
                DrawPointLight(origin, farClip);
                return;
            }

            float halfAngleRad = Mathf.Clamp(pointLightVolume.Angle, 0.05f * Mathf.Deg2Rad, 89.95f * Mathf.Deg2Rad);
            DrawSpotShadowClip(origin, transform.forward, transform.right, transform.up, halfAngleRad, nearClip, farClip);
        }

        // Draws a truncated spotlight shadow frustum between the near and far clip planes.
        private void DrawSpotShadowClip(Vector3 origin, Vector3 forward, Vector3 right, Vector3 up, float halfAngleRad, float nearClip, float farClip) {
            float tanHalfAngle = Mathf.Tan(halfAngleRad);
            Vector3 nearCenter = origin + forward * nearClip;
            Vector3 farCenter = origin + forward * farClip;
            float nearRadius = nearClip * tanHalfAngle;
            float farRadius = farClip * tanHalfAngle;

            Handles.DrawWireDisc(nearCenter, forward, nearRadius);
            Handles.DrawWireDisc(farCenter, forward, farRadius);
            Handles.DrawLine(nearCenter + right * nearRadius, farCenter + right * farRadius);
            Handles.DrawLine(nearCenter - right * nearRadius, farCenter - right * farRadius);
            Handles.DrawLine(nearCenter + up * nearRadius, farCenter + up * farRadius);
            Handles.DrawLine(nearCenter - up * nearRadius, farCenter - up * farRadius);
        }

        // Draws an Area Light emitter rectangle and its forward direction.
        private void DrawAreaLight(Vector3 center, Quaternion rotation, float width, float height) {
            Vector3 right = rotation * Vector3.right * (width * 0.5f);
            Vector3 up = rotation * Vector3.up * (height * 0.5f);

            Vector3[] corners = new Vector3[4];
            corners[0] = center + right + up; // Top Right
            corners[1] = center - right + up; // Top Left
            corners[2] = center - right - up; // Bottom Left
            corners[3] = center + right - up; // Bottom Right

            // Draw the rectangle
            Handles.DrawLine(corners[0], corners[1]);
            Handles.DrawLine(corners[1], corners[2]);
            Handles.DrawLine(corners[2], corners[3]);
            Handles.DrawLine(corners[3], corners[0]);
            
            // Draw forward vector
            Handles.DrawLine(center, center + rotation * Vector3.forward * 0.5f);
        }

        // Draws the estimated culling sphere of an Area Light.
        private void DrawAreaLightDebug(Vector3 center, Quaternion rotation, float width, float height, Color color, float intensity, float cutoff) {

            // Light normal
            Vector3 up = rotation * Vector3.up;
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;

            // Calculate the bounding sphere of the area light given the cutoff irradiance
            float minSolidAngle = Mathf.Clamp(cutoff / (Mathf.Max(color.r, Mathf.Max(color.g, color.b)) * intensity * Mathf.PI), -Mathf.PI * 2f, Mathf.PI * 2);
            float sqMaxDist = ComputeAreaLightSquaredBoundingSphere(width, height, minSolidAngle);
            float radius = Mathf.Sqrt(sqMaxDist);

            Handles.DrawWireDisc(center, forward, radius);
            Handles.DrawWireArc(center, right, up * radius, 180f, radius);
            Handles.DrawWireArc(center, up, -right * radius, 180f, radius);

        }

        // Calculates squared Area Light range from emitter dimensions and minimum solid angle.
        float ComputeAreaLightSquaredBoundingSphere(float width, float height, float minSolidAngle) {
            float A = width * height;
            float w2 = width * width;
            float h2 = height * height;
            float B = 0.25f * (w2 + h2);
            float t = Mathf.Tan(0.25f * minSolidAngle);
            float T = t * t;
            float TB = T * B;
            float discriminant = Mathf.Sqrt(TB * TB + 4.0f * T * A * A);
            float d2 = (discriminant - TB) * 0.125f / T;
            return d2;
        }

        // Calculates squared Point Light range from brightness, source size and cutoff.
        float ComputePointLightSquaredBoundingSphere(Color color, float intensity, float size, float cutoff) {
            float L = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            return Mathf.Max(Mathf.PI * 2 * L * Mathf.Abs(intensity) / (cutoff * cutoff) - 1, 0) * size * size;
        }

        // Keeps live debug values updating while their foldout is visible in play mode.
        public override bool RequiresConstantRepaint() {
            return _debugExpanded && EditorApplication.isPlaying;
        }

    }

}
