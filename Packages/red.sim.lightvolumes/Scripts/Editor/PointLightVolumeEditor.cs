using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UdonSharpEditor;
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
            UdonSharpEditorUtility.CopyProxyToUdon(pointLightVolume);

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
        private PointLightVolumeInstance PointLightVolume;

        private static readonly GUIContent _bakeShadowsButtonContent = new GUIContent("Bake Shadows", "Bakes or re-bakes shadow maps for all selected lights with Shadows enabled.");
        private static readonly GUIContent _emptyContent = GUIContent.none;
        private static readonly string _textureMaterialHint = "None (Texture/Material)";
        private static readonly string _cubemapMaterialHint = "None (Texture/Material)";
        private static readonly string _projectionSourceObjectPickerFilter = "t:Texture t:Material";
        private static readonly string[] _lightTypeNames = { "Point Light", "Spot Light", "Area Light" };
        private static readonly string[] _projectionNames = { "Parametric", "LUT", "Custom" };
        private const float ObjectSelectorButtonWidth = 19f;
        private static readonly Color _shadowClipVisibleColor = new Color(0.2f, 0.65f, 1f, 0.75f);
        private static readonly Color _shadowClipHiddenColor = new Color(0.2f, 0.65f, 1f, 0.18f);
        private static GUIStyle _projectionSourceHintStyle;

        private void OnEnable() {
            PointLightVolume = target as PointLightVolumeInstance;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable() {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();
            int undoGroup = Undo.GetCurrentGroup();
            SerializedProperty lightTypeProperty = serializedObject.FindProperty("LightType");
            SerializedProperty projectionProperty = serializedObject.FindProperty("Projection");
            int lightType = Mathf.Clamp(lightTypeProperty.intValue, 0, 2);
            int projection = Mathf.Clamp(projectionProperty.intValue, 0, 2);

            DrawProperty("IsDynamic", "Dynamic");
            DrawPopup(lightTypeProperty, new GUIContent("Type", "Point Light is the most performant type. For static lighting, prefer baked additive Light Volumes."), _lightTypeNames);
            lightType = Mathf.Clamp(lightTypeProperty.intValue, 0, 2);

            if (lightType != 2) {
                if (projection == 1) DrawProperty("Range");
                else DrawProperty("LightSourceSize", "Light Source Size");
            }
            DrawProperty("Color");
            DrawProperty("Intensity");
            DrawProperty("ShadingStrength", "Shading Strength");

            if (lightType != 2) {
                DrawPopup(projectionProperty, new GUIContent("Projection", "Parametric computes falloff; LUT, Cookie and Cubemap use a texture or material source."), _projectionNames);
                projection = Mathf.Clamp(projectionProperty.intValue, 0, 2);
            }
            if (lightType == 1) DrawAngleDegrees();
            if (lightType == 1 && projection == 0) DrawProperty("Falloff");
            if (lightType == 1 && projection == 2) DrawProperty("SpotCookieAspect", "Spot Cookie Aspect");

            DrawProperty("BakeIntoProbes", "Bake Into Probes");
            DrawProperty("DebugRange", "Debug Range");
            DrawActiveProjectionSourceField(lightType, projection);

            SerializedProperty shadowsProperty = serializedObject.FindProperty("Shadows");
            EditorGUILayout.PropertyField(shadowsProperty);
            bool drawShadowFields = shadowsProperty.hasMultipleDifferentValues || shadowsProperty.boolValue;
            bool propertiesChanged = serializedObject.ApplyModifiedProperties();

            if (drawShadowFields) {
                DrawTextureMaterialField("ShadowMap", _cubemapMaterialHint, true);
                DrawProperty("BakeInGame", "Bake In Game");
                DrawLayerMask();
                DrawProperty("ObjectMask", "Object Mask");
                DrawProperty("NearClip", "Near Plane");
                DrawProperty("FarClip", "Far Plane");
                DrawProperty("DebugClipPlanes", "Debug Clip Planes");
                DrawProperty("Bias");
                DrawProperty("Blur");
                DrawProperty("ContactHardening", "Contact Hardening");
                DrawProperty("WorldSpaceShadows", "Use World Space");
                if (lightType == 1) DrawProperty("ForceCubemapShadows", "Force Cubemap Shadows");
                DrawProperty("RebakeShadows", "Rebake Shadows");

                if (GUILayout.Button(_bakeShadowsButtonContent)) {
                    propertiesChanged |= serializedObject.ApplyModifiedProperties();
                    propertiesChanged |= BakeSelectedShadowMaps();
                    serializedObject.Update();
                }
            }

            propertiesChanged |= serializedObject.ApplyModifiedProperties();
            if (!propertiesChanged) return;

            SyncTargets(true);
            Undo.CollapseUndoOperations(undoGroup);
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

    }

}
