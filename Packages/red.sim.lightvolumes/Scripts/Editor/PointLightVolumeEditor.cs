using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
#if UDONSHARP
using UdonSharpEditor;
#endif
using System.Collections.Generic;

namespace VRCLightVolumes {
    public static class PointLightVolumeEditorUtility {
        public const int CustomTexturesChanged = 1;
        public const int ShadowTexturesChanged = 2;

        // Applies derived data once, copies the proxy once, and optionally rebuilds shared arrays once.
        public static int Sync(PointLightVolumeInstance pointLightVolume, bool recordUndo = false, bool rebuildTextureArrays = true) {
            if (pointLightVolume == null) return 0;

            bool customTexturesChanged = pointLightVolume.HasEditorCustomTextureChanges();
            bool shadowTexturesChanged = pointLightVolume.HasEditorShadowTextureChanges();
            if (recordUndo) Undo.RecordObject(pointLightVolume, "Sync Point Light Volume");

            pointLightVolume.EditorApplyAuthoringData(customTexturesChanged, shadowTexturesChanged);
            LightVolumeManagerTools.CopyProxyToUdon(pointLightVolume);

            int changes = (customTexturesChanged ? CustomTexturesChanged : 0)
                | (shadowTexturesChanged ? ShadowTexturesChanged : 0);
            LightVolumeManager manager = pointLightVolume.LightVolumeManager;
            if (rebuildTextureArrays && manager != null) {
                if (customTexturesChanged) manager.ReinitializeCustomTextures();
                if (shadowTexturesChanged) manager.ReinitializeShadowTextures();
            }
            return changes;
        }
    }

    [CanEditMultipleObjects]
    [CustomEditor(typeof(PointLightVolumeInstance))]
    public class PointLightVolumeEditor : Editor {
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
        private const double RuntimeDebugRefreshInterval = 0.2d;
        private static readonly Color _shadowClipVisibleColor = new Color(0.2f, 0.65f, 1f, 0.75f);
        private static readonly Color _shadowClipHiddenColor = new Color(0.2f, 0.65f, 1f, 0.18f);
        private static GUIStyle _projectionSourceHintStyle;
        private bool _debugExpanded;
        private double _nextRuntimeDebugRefresh;

        private void OnEnable() {
            PointLightVolume = target as PointLightVolumeInstance;
            _debugExpanded = SessionState.GetBool(DebugFoldoutSessionKey, false);
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable() {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        public override void OnInspectorGUI() {
            RefreshRuntimeDebugProxy();
            serializedObject.Update();
            int undoGroup = Undo.GetCurrentGroup();
            SerializedProperty lightTypeProperty = serializedObject.FindProperty("LightType");
            SerializedProperty projectionProperty = serializedObject.FindProperty("Projection");
            int lightType = Mathf.Clamp(lightTypeProperty.intValue, 0, 2);
            int projection = Mathf.Clamp(projectionProperty.intValue, 0, 2);

            DrawSectionHeader("Light", false);
            DrawPopup(lightTypeProperty, new GUIContent("Type", "Point Light is the most performant type. For static lighting, prefer baked additive Light Volumes."), _lightTypeNames);
            lightType = Mathf.Clamp(lightTypeProperty.intValue, 0, 2);
            DrawProperty("IsDynamic", "Dynamic");
            DrawProperty("Color");
            DrawProperty("Intensity");
            DrawProperty("ShadingStrength", "Shading Strength");
            DrawProperty("BakeIntoProbes", "Bake Into Probes");
            DrawProperty("DebugRange", "Debug Range");

            DrawSectionHeader("Projection", true);
            if (lightType != 2) {
                DrawPopup(projectionProperty, new GUIContent("Projection", "Parametric computes falloff; LUT, Cookie and Cubemap use a texture or material source."), _projectionNames);
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
                        propertiesChanged |= BakeSelectedShadowMaps();
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

        private static void DrawSectionHeader(string title, bool addTopSpacing) {
            if (addTopSpacing) GUILayout.Space(InspectorSectionSpacing);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private void DrawDebugSection() {
            GUILayout.Space(InspectorSectionSpacing);
            EditorGUI.BeginChangeCheck();
            _debugExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(
                _debugExpanded,
                new GUIContent("Debug", "Shows read-only live Point Light Volume data for troubleshooting."));
            if (EditorGUI.EndChangeCheck()) {
                SessionState.SetBool(DebugFoldoutSessionKey, _debugExpanded);
                _nextRuntimeDebugRefresh = 0d;
            }

            if (_debugExpanded && PointLightVolume != null) {
                if (!EditorApplication.isPlaying)
                    EditorGUILayout.HelpBox("Live values are populated in Play Mode. Resolved light, projection and shadow values show the current editor state.", MessageType.Info);
                if (targets.Length > 1)
                    EditorGUILayout.HelpBox("Debug values are shown for the first selected Point Light Volume.", MessageType.Info);

                LightVolumeDebugGUI.DrawGroupHeader(
                    "Registration",
                    false,
                    "Shows which Manager owns this light and its registry priority.");
                LightVolumeDebugGUI.DrawObject("Manager", PointLightVolume.LightVolumeManager, typeof(LightVolumeManager), "The scene Manager used by this light.");
                LightVolumeDebugGUI.DrawBool("Registered", PointLightVolume.RegisteredWithManagerPreview, "Whether this light is currently in a Manager registry.");
                LightVolumeDebugGUI.DrawBool("Active", PointLightVolume.IsActive, "Whether this light is currently eligible for rendering.");
                LightVolumeDebugGUI.DrawInt("Registry Order", PointLightVolume.RegistryOrder, "Stable tie-breaker used when registry weights are equal.");
                LightVolumeDebugGUI.DrawFloat("Registry Weight", PointLightVolume.RegistryWeight, "Higher weights are uploaded to shaders first.");

                LightVolumeDebugGUI.DrawGroupHeader(
                    "Resolved Light Data",
                    true,
                    "Values calculated from the Transform and light settings for shaders.");
                LightVolumeDebugGUI.DrawVector3("Position", PointLightVolume.Position, "World-space light position sent to shaders.");
                if (PointLightVolume.LightType != 2)
                    LightVolumeDebugGUI.DrawVector3("Direction", PointLightVolume.Direction, "World-space light direction sent to shaders.");
                LightVolumeDebugGUI.DrawQuaternion("Rotation", PointLightVolume.Rotation, "Rotation used by projected and Area Light calculations.");
                LightVolumeDebugGUI.DrawFloat("Squared Range", PointLightVolume.SquaredRange, "Squared effective light range used by shaders.");
                LightVolumeDebugGUI.DrawFloat("Squared Scale", PointLightVolume.SquaredScale, "Squared average Transform scale.");
                LightVolumeDebugGUI.DrawBool("Range Dirty", PointLightVolume.IsRangeDirty, "Whether the Manager still needs to recalculate the effective range.");

                LightVolumeDebugGUI.DrawGroupHeader(
                    "Resolved Projection",
                    true,
                    "Resolved runtime source and layout for this light's projection.");
                LightVolumeDebugGUI.DrawText("Projection Mode", GetProjectionModeName(PointLightVolume.ProjectionMode), "Projection method currently sent to shaders.");
                LightVolumeDebugGUI.DrawText("Source Type", GetSourceTypeName(PointLightVolume.ProjectionType), "Whether the resolved source is a texture, a material, or none.");
                LightVolumeDebugGUI.DrawObject("Texture", PointLightVolume.CustomTexture, typeof(Texture), "Resolved texture sampled by this projection.");
                LightVolumeDebugGUI.DrawObject("Material", PointLightVolume.CustomTextureMaterial, typeof(Material), "Resolved material rendered into the runtime cookie array.");
                LightVolumeDebugGUI.DrawBool("Cubemap Source", PointLightVolume.CustomTextureIsCubemap, "Whether the resolved texture is a cubemap.");
                LightVolumeDebugGUI.DrawBool("Depth Slices", PointLightVolume.CustomTextureHasDepthSlices, "Whether the resolved texture already contains array slices.");
                LightVolumeDebugGUI.DrawBool("Dynamic Source", PointLightVolume.AutoUpdateCustomTexture, "Whether the projection source is copied again at runtime.");

                if (PointLightVolume.LightType == 2) {
                    LightVolumeDebugGUI.DrawGroupHeader(
                        "Area Cookie",
                        true,
                        "Live fallback color and GPU readback state for an Area Light cookie.");
                    LightVolumeDebugGUI.DrawText("Fallback Color", "#" + ColorUtility.ToHtmlStringRGBA(PointLightVolume.AreaLightFallbackColor), "Average cookie color used before detailed projection data is ready.");
                    LightVolumeDebugGUI.DrawFloat("Mirror", PointLightVolume.AreaCookieMirror, "Sign used to keep the Area Light cookie orientation correct.");
                    LightVolumeDebugGUI.DrawInt("Average Custom ID", PointLightVolume.AreaCookieAverageCustomId, "Runtime cookie-array source used for average-color readback.");
                    LightVolumeDebugGUI.DrawBool("Readback Pending", PointLightVolume.AreaCookieAverageReadbackPending, "Whether an average-color GPU readback is currently pending.");
                    LightVolumeDebugGUI.DrawBool("Readback Dirty", PointLightVolume.AreaCookieAverageReadbackDirty, "Whether the cookie average must be read again.");
                }

                if (PointLightVolume.Shadows) {
                    LightVolumeDebugGUI.DrawGroupHeader(
                        "Resolved Shadows",
                        true,
                        "Resolved shadow source and bake pose used by shaders.");
                    LightVolumeDebugGUI.DrawObject("Texture", PointLightVolume.ShadowMapTexture, typeof(Texture), "Resolved texture packed into the runtime shadow array.");
                    LightVolumeDebugGUI.DrawObject("Material", PointLightVolume.ShadowMapMaterial, typeof(Material), "Resolved material rendered into the runtime shadow array.");
                    LightVolumeDebugGUI.DrawFloat("Shadow Map ID", PointLightVolume.ShadowMapID, "First slice assigned in the Manager's shadow array; -1 means none.");
                    LightVolumeDebugGUI.DrawBool("Uses Cubemap", PointLightVolume.ShadowMapUsesCubemap, "Whether this light samples a six-face shadow.");
                    LightVolumeDebugGUI.DrawBool("Cubemap Source", PointLightVolume.ShadowMapTextureIsCubemap, "Whether the assigned shadow texture is a cubemap.");
                    LightVolumeDebugGUI.DrawBool("Depth Slices", PointLightVolume.ShadowMapTextureHasDepthSlices, "Whether the assigned texture already contains array slices.");
                    LightVolumeDebugGUI.DrawBool("Dynamic Source", PointLightVolume.AutoUpdateShadowMap, "Whether the shadow source is copied again at runtime.");
                    LightVolumeDebugGUI.DrawVector3("Bake Position", PointLightVolume.ShadowBakePosition, "World-space position used when the current shadow was baked.");
                    LightVolumeDebugGUI.DrawQuaternion("Bake Rotation", PointLightVolume.ShadowBakeRotation, "World-space rotation used when the current shadow was baked.");
                }

                if (PointLightVolume.BakeInGame) {
                    LightVolumeDebugGUI.DrawGroupHeader(
                        "Runtime Shadow Baking",
                        true,
                        "Live state and temporary resources used while baking shadows in-game.");
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

        private void RefreshRuntimeDebugProxy() {
#if UDONSHARP
            if (!_debugExpanded || !EditorApplication.isPlaying || PointLightVolume == null) return;
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextRuntimeDebugRefresh) return;
            _nextRuntimeDebugRefresh = now + RuntimeDebugRefreshInterval;
            if (UdonSharpEditorUtility.GetBackingUdonBehaviour(PointLightVolume) != null)
                UdonSharpEditorUtility.CopyUdonToProxy(PointLightVolume);
#endif
        }

        private static string GetProjectionModeName(int value) {
            if (value == 1) return "LUT";
            if (value == 2) return "Custom";
            return "Parametric";
        }

        private static string GetSourceTypeName(int value) {
            if (value == 1) return "Texture";
            if (value == 2) return "Material";
            return "None";
        }

        private void DrawProperty(string propertyName, string label = null) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            GUIContent content = label == null ? EditorGUIUtility.TrTextContent(property.displayName, property.tooltip) : new GUIContent(label, property.tooltip);
            EditorGUILayout.PropertyField(property, content, true);
        }

        private static void DrawPopup(SerializedProperty property, GUIContent label, string[] names) {
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.Popup(label, Mathf.Clamp(property.intValue, 0, names.Length - 1), names);
            if (EditorGUI.EndChangeCheck()) property.intValue = value;
            EditorGUI.showMixedValue = false;
        }

        private void DrawAngleDegrees() {
            SerializedProperty angleProperty = serializedObject.FindProperty("Angle");
            float angleDegrees = angleProperty.floatValue * Mathf.Rad2Deg * 2f;
            EditorGUI.showMixedValue = angleProperty.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            angleDegrees = EditorGUILayout.Slider(new GUIContent("Angle", "Angle of a spotlight cone in degrees."), angleDegrees, 0.1f, 360f);
            if (EditorGUI.EndChangeCheck()) angleProperty.floatValue = angleDegrees * Mathf.Deg2Rad * 0.5f;
            EditorGUI.showMixedValue = false;
        }

        private void DrawLayerMask() {
            SerializedProperty layerMaskProperty = serializedObject.FindProperty("LayerMask");
            EditorGUI.showMixedValue = layerMaskProperty.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.MaskField(new GUIContent("Layer Mask", "Layers that can cast shadows."), layerMaskProperty.intValue, InternalEditorUtility.layers);
            if (EditorGUI.EndChangeCheck()) layerMaskProperty.intValue = value;
            EditorGUI.showMixedValue = false;
        }

        // Bakes selected lights and rebuilds each affected manager shadow array once.
        private bool BakeSelectedShadowMaps() {
            bool bakedAny = false;
            HashSet<LightVolumeManager> managers = null;
            for (int i = 0; i < targets.Length; i++) {
                PointLightVolumeInstance pointLightVolume = targets[i] as PointLightVolumeInstance;
                if (pointLightVolume == null || !pointLightVolume.Shadows) continue;

                PointLightVolumeEditorUtility.Sync(pointLightVolume, false, false);
                if (!PointLightShadowBaker.BakeShadowMap(pointLightVolume, $"| {pointLightVolume.gameObject.name} ({i + 1}/{targets.Length})", false)) continue;

                bakedAny = true;
                PointLightVolumeEditorUtility.Sync(pointLightVolume, false, false);
                if (pointLightVolume.LightVolumeManager == null) continue;
                if (managers == null) managers = new HashSet<LightVolumeManager>();
                managers.Add(pointLightVolume.LightVolumeManager);
            }
            if (managers != null) {
                foreach (LightVolumeManager manager in managers) {
                    if (manager != null) manager.ReinitializeShadowTextures();
                }
            }
            return bakedAny;
        }

        // Applies all selected proxies first, then rebuilds each shared array at most once.
        private void SyncTargets(bool recordUndo) {
            Dictionary<LightVolumeManager, int> managerChanges = null;
            for (int i = 0; i < targets.Length; i++) {
                PointLightVolumeInstance pointLightVolume = targets[i] as PointLightVolumeInstance;
                if (pointLightVolume == null) continue;

                int changes = PointLightVolumeEditorUtility.Sync(pointLightVolume, recordUndo, false);
                LightVolumeManager manager = pointLightVolume.LightVolumeManager;
                if (manager == null || changes == 0) continue;
                if (managerChanges == null) managerChanges = new Dictionary<LightVolumeManager, int>();
                int previous;
                managerChanges.TryGetValue(manager, out previous);
                managerChanges[manager] = previous | changes;
            }

            if (managerChanges == null) return;
            foreach (KeyValuePair<LightVolumeManager, int> entry in managerChanges) {
                LightVolumeManager manager = entry.Key;
                if (manager == null) continue;
                if (recordUndo) Undo.RecordObject(manager, "Sync Point Light Volume Textures");
                if ((entry.Value & PointLightVolumeEditorUtility.CustomTexturesChanged) != 0) manager.ReinitializeCustomTextures();
                if ((entry.Value & PointLightVolumeEditorUtility.ShadowTexturesChanged) != 0) manager.ReinitializeShadowTextures();
            }
        }

        private void OnUndoRedoPerformed() {
            SyncTargets(false);
            Repaint();
        }

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

        private void DrawTextureMaterialField(string propertyName, string acceptedTypesHint, bool isShadowSource) {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            DrawTextureMaterialField(property, EditorGUIUtility.TrTextContent(property.displayName, property.tooltip), acceptedTypesHint, isShadowSource);
        }

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

        private void ShowProjectionSourcePickerOnSelectorClick(SerializedProperty property, Rect fieldRect, int controlID) {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0) return;
            Rect selectorRect = fieldRect;
            selectorRect.xMin = selectorRect.xMax - ObjectSelectorButtonWidth;
            if (!selectorRect.Contains(currentEvent.mousePosition)) return;
            EditorGUIUtility.ShowObjectPicker<UnityEngine.Object>(property.objectReferenceValue, false, _projectionSourceObjectPickerFilter, controlID);
            currentEvent.Use();
        }

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

        private bool IsSupportedTextureMaterialSource(UnityEngine.Object value, bool isShadowSource) {
            if (value == null) return true;
            if (isShadowSource) return value is Texture2DArray || value is Cubemap || value is RenderTexture || value is Material;
            if (value is RenderTexture || value is Material) return true;
            if (!(value is Texture)) return false;

            int lightType = Mathf.Clamp(serializedObject.FindProperty("LightType").intValue, 0, 2);
            int projection = Mathf.Clamp(serializedObject.FindProperty("Projection").intValue, 0, 2);
            return lightType == 2 || projection == 1 || projection == 2 && (lightType == 0 || lightType == 1);
        }

        private static float GetBrightnessCutoff(PointLightVolumeInstance pointLightVolume) {
            return pointLightVolume.LightVolumeManager != null ? pointLightVolume.LightVolumeManager.LightsBrightnessCutoff : 0.35f;
        }

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

                if (isDebug) {
                    bounds = Mathf.Sqrt(ComputePointLightSquaredBoundingSphere(pointLightVolume.Color, pointLightVolume.Intensity, range, GetBrightnessCutoff(pointLightVolume)));
                }

                // Drawing

                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawPointLight(origin, range);
                if (isDebug) {
                    DrawPointLight(origin, bounds);
                }
                DrawShadowClipGUI(pointLightVolume, origin, t);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawPointLight(origin, range);
                if (isDebug) {
                    DrawPointLight(origin, bounds);
                }
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

                if (isDebug) {
                    bounds = Mathf.Sqrt(ComputePointLightSquaredBoundingSphere(pointLightVolume.Color, pointLightVolume.Intensity, range, GetBrightnessCutoff(pointLightVolume)));
                }

                // Drawing

                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawSpotLight(origin, forward, halfAngleRad, range, dirs);

                if (isDebug)
                    DrawSpotLight(origin, forward, halfAngleRad, bounds, dirs);
                DrawShadowClipGUI(pointLightVolume, origin, t);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawSpotLight(origin, forward, halfAngleRad, range, dirs);

                if (isDebug) {
                    DrawSpotLight(origin, forward, halfAngleRad, bounds, dirs);
                }
                DrawShadowClipGUI(pointLightVolume, origin, t);

            } else { // Area light

                float x = Mathf.Max(Mathf.Abs(pointLightVolume.transform.lossyScale.x), 0.001f);
                float y = Mathf.Max(Mathf.Abs(pointLightVolume.transform.lossyScale.y), 0.001f);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawAreaLight(origin, t.rotation, x, y);

                if(pointLightVolume.DebugRange)
                    DrawAreaLightDebug(origin, t.rotation, x, y, pointLightVolume.Color, pointLightVolume.Intensity, GetBrightnessCutoff(pointLightVolume));
                DrawShadowClipGUI(pointLightVolume, origin, t);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawAreaLight(origin, t.rotation, x, y);

                if (pointLightVolume.DebugRange)
                    DrawAreaLightDebug(origin, t.rotation, x, y, pointLightVolume.Color, pointLightVolume.Intensity, GetBrightnessCutoff(pointLightVolume));
                DrawShadowClipGUI(pointLightVolume, origin, t);

            }

        }

        void OnSceneGUI() {
            foreach (var obj in Selection.gameObjects) {
                var volume = obj.GetComponent<PointLightVolumeInstance>();
                if (volume != null) {
                    DrawVolumeGUI(volume);
                }
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

        float ComputePointLightSquaredBoundingSphere(Color color, float intensity, float size, float cutoff) {
            float L = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            return Mathf.Max(Mathf.PI * 2 * L * Mathf.Abs(intensity) / (cutoff * cutoff) - 1, 0) * size * size;
        }

        public override bool RequiresConstantRepaint() {
            return _debugExpanded && EditorApplication.isPlaying;
        }

    }

}
