using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace VRCLightVolumes {

    [CanEditMultipleObjects]
    [CustomEditor(typeof(PointLightVolume))]
    public class PointLightVolumeEditor : Editor {

        PointLightVolume PointLightVolume;
        private static readonly GUIContent _shadowMapContent = new GUIContent("Shadow Map", "Generated EVSM Texture2DArray, cubemap, RenderTexture, CustomRenderTexture or Material used by the shared shadow texture array.");
        private static readonly GUIContent _shadowLayerMaskContent = new GUIContent("Layer Mask", "Layer mask used by the shadow bake camera. Only these layers can write into the shadow depth pass.");
        private static readonly GUIContent _shadowNearPlaneContent = new GUIContent("Near Plane", "Near clip plane used by the shadow bake camera. Higher values improve depth precision but clip nearby occluders.");
        private static readonly GUIContent _useWorldSpaceContent = new GUIContent("Use World Space", "Enables World Space Shadows using the bake position. Disable for Local Space Shadows that move and rotate with this light.");
        private static readonly GUIContent _bakeShadowsButtonContent = new GUIContent("Bake Shadows", "Bakes or re-bakes a shadow map for this light.");
        private static readonly GUIContent _falloffLutContent = new GUIContent("Falloff LUT", "Texture2D, RenderTexture, CustomRenderTexture or Material used by LUT projection.");
        private static readonly GUIContent _cookieContent = new GUIContent("Cookie", "Texture2D, RenderTexture, CustomRenderTexture or Material used by spot cookie projection.");
        private static readonly GUIContent _cubemapContent = new GUIContent("Cubemap", "Cubemap, RenderTexture, CustomRenderTexture or Material used by point cubemap projection.");
        private static readonly GUIContent _emptyContent = GUIContent.none;
        private static readonly string _textureMaterialHint = "None (Texture/Material)";
        private static readonly string _cubemapMaterialHint = "None (Texture/Material)";
        private static readonly string _projectionSourceObjectPickerFilter = "t:Texture t:Material";
        private const float ObjectSelectorButtonWidth = 19f;
        private static GUIStyle _projectionSourceHintStyle = null;

        private void OnEnable() {
            PointLightVolume = (PointLightVolume)target;
        }

        public override void OnInspectorGUI() {

            serializedObject.Update();

            List<string> hiddenFields = new List<string> { "m_Script", "PointLightVolumeInstance", "LightVolumeSetup" };
            hiddenFields.Add("ShadowID");
            hiddenFields.Add("ShadowMap");
            hiddenFields.Add("RebakeShadows");
            hiddenFields.Add("Bias");
            hiddenFields.Add("LayerMask");
            hiddenFields.Add("ObjectMask");
            hiddenFields.Add("NearPlane");
            hiddenFields.Add("Blur");
            hiddenFields.Add("ContactHardening");
            hiddenFields.Add("UseWorldSpace");
            hiddenFields.Add("FalloffLUT");
            hiddenFields.Add("Cubemap");
            hiddenFields.Add("Cookie");
            hiddenFields.Add("Shadows");
            
            if(PointLightVolume.Type == PointLightVolume.LightType.PointLight) {
                hiddenFields.Add("Angle");
                hiddenFields.Add("Falloff");
            }

            if (PointLightVolume.Type == PointLightVolume.LightType.AreaLight) {
                hiddenFields.Add("Angle");
                hiddenFields.Add("Falloff");
                hiddenFields.Add("Projection");
                hiddenFields.Add("Range");
                hiddenFields.Add("FalloffLUT");
                hiddenFields.Add("Cubemap");
                hiddenFields.Add("Cookie");
                hiddenFields.Add("LightSourceSize");
            }

            if (PointLightVolume.Projection == PointLightVolume.LightProjection.Parametric) {
                hiddenFields.Add("Range");
            } else if (PointLightVolume.Projection == PointLightVolume.LightProjection.Custom) {
                hiddenFields.Add("Falloff");
                hiddenFields.Add("Range");
            } else if (PointLightVolume.Projection == PointLightVolume.LightProjection.LUT) {
                hiddenFields.Add("Falloff");
                hiddenFields.Add("LightSourceSize");
            }

            DrawPropertiesExcluding(serializedObject, hiddenFields.ToArray());
            DrawActiveProjectionSourceField();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Shadows"));

            bool propertiesChanged = serializedObject.ApplyModifiedProperties();

            SerializedProperty shadowsProperty = serializedObject.FindProperty("Shadows");
            bool drawShadowFields = shadowsProperty.hasMultipleDifferentValues || shadowsProperty.boolValue;
            if (drawShadowFields) {
                SerializedProperty shadowMapProperty = serializedObject.FindProperty("ShadowMap");
                DrawTextureMaterialField(shadowMapProperty, _shadowMapContent, _cubemapMaterialHint, true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("LayerMask"), _shadowLayerMaskContent);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ObjectMask"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("NearPlane"), _shadowNearPlaneContent);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Bias"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Blur"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ContactHardening"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("UseWorldSpace"), _useWorldSpaceContent);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("RebakeShadows"));

                if (GUILayout.Button(_bakeShadowsButtonContent)) {
                    propertiesChanged |= serializedObject.ApplyModifiedProperties();
                    for (int i = 0; i < targets.Length; i++) {
                        PointLightVolume pointLightVolume = targets[i] as PointLightVolume;
                        if (pointLightVolume != null && pointLightVolume.Shadows) {
                            pointLightVolume.BakeShadowMap();
                        }
                    }
                    serializedObject.Update();
                }
            }

            propertiesChanged |= serializedObject.ApplyModifiedProperties();
            if (propertiesChanged) {
                SyncTargets();
            }

        }

        // Syncs changed inspector values into runtime instances and shader globals immediately.
        private void SyncTargets() {
            for (int i = 0; i < targets.Length; i++) {
                PointLightVolume pointLightVolume = targets[i] as PointLightVolume;
                if (pointLightVolume == null) continue;
                pointLightVolume.SyncUdonScript();
                if (pointLightVolume.LightVolumeSetup != null) {
                    pointLightVolume.LightVolumeSetup.ReinitializeCustomTextures();
                    pointLightVolume.LightVolumeSetup.ReinitializeShadowTextures();
                }
            }
        }

        // Draws the projection source that matches the selected projection and light type.
        private void DrawActiveProjectionSourceField() {
            if (PointLightVolume.Type == PointLightVolume.LightType.AreaLight || PointLightVolume.Projection == PointLightVolume.LightProjection.Parametric) return;
            if (PointLightVolume.Projection == PointLightVolume.LightProjection.LUT) {
                DrawTextureMaterialField(serializedObject.FindProperty("FalloffLUT"), _falloffLutContent, _textureMaterialHint, false);
            } else if (PointLightVolume.Type == PointLightVolume.LightType.PointLight) {
                DrawTextureMaterialField(serializedObject.FindProperty("Cubemap"), _cubemapContent, _cubemapMaterialHint, false);
            } else if (PointLightVolume.Type == PointLightVolume.LightType.SpotLight) {
                DrawTextureMaterialField(serializedObject.FindProperty("Cookie"), _cookieContent, _textureMaterialHint, false);
            }
        }

        // Draws and validates a compact texture/material source object field.
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
            if (EditorGUI.EndChangeCheck()) {
                property.objectReferenceValue = IsSupportedTextureMaterialSource(value, isShadowSource) ? value : null;
            }
            UpdateProjectionSourceFromPicker(property, controlID, isShadowSource);
            if (drawHint) DrawProjectionSourceHint(fieldRect, acceptedTypesHint);
            EditorGUI.showMixedValue = false;
            EditorGUI.EndProperty();
        }

        // Opens a filtered native object picker when the selector button is clicked.
        private void ShowProjectionSourcePickerOnSelectorClick(SerializedProperty property, Rect fieldRect, int controlID) {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0) return;
            Rect selectorRect = fieldRect;
            selectorRect.xMin = selectorRect.xMax - ObjectSelectorButtonWidth;
            if (!selectorRect.Contains(currentEvent.mousePosition)) return;
            EditorGUIUtility.ShowObjectPicker<UnityEngine.Object>(property.objectReferenceValue, false, _projectionSourceObjectPickerFilter, controlID);
            currentEvent.Use();
        }

        // Applies a valid value selected through the filtered projection source picker.
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

        // Draws an accepted-types hint over the native empty ObjectField text without covering the native frame or focus state.
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

        // Checks if an object can be used by the selected texture/material source field.
        private bool IsSupportedTextureMaterialSource(UnityEngine.Object value, bool isShadowSource) {
            if (value == null) return true;
            if (isShadowSource) return value is Texture2DArray || value is Cubemap || value is RenderTexture || value is Material;
            if (value is RenderTexture || value is Material) return true;
            if (PointLightVolume.Projection == PointLightVolume.LightProjection.LUT) return value is Texture;
            if (PointLightVolume.Projection == PointLightVolume.LightProjection.Custom && PointLightVolume.Type == PointLightVolume.LightType.PointLight) return value is Texture;
            if (PointLightVolume.Projection == PointLightVolume.LightProjection.Custom && PointLightVolume.Type == PointLightVolume.LightType.SpotLight) return value is Texture;
            return false;
        }

        private void DrawVolumeGUI(PointLightVolume pointLightVolume) {

            Transform t = pointLightVolume.transform;
            Vector3 origin = t.position;
            Vector3 lscale = pointLightVolume.transform.lossyScale;
            float scale = (lscale.x + lscale.y + lscale.z) / 3;
            float range = pointLightVolume.Type != PointLightVolume.LightType.AreaLight && (pointLightVolume.Projection != PointLightVolume.LightProjection.LUT || pointLightVolume.FalloffLUT == null) ? pointLightVolume.LightSourceSize : pointLightVolume.Range;
            range *= scale;

            if (pointLightVolume.Type == PointLightVolume.LightType.PointLight) { // Point Light Visualization

                // Calculating

                float bounds = 0;

                bool isDebug = pointLightVolume.DebugRange && (pointLightVolume.Projection != PointLightVolume.LightProjection.LUT || pointLightVolume.FalloffLUT == null);

                if (isDebug) {
                    bounds = Mathf.Sqrt(ComputePointLightSquaredBoundingSphere(pointLightVolume.Color, pointLightVolume.Intensity, range, pointLightVolume.LightVolumeSetup.BrightnessCutoff));
                }

                // Drawing

                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawPointLight(origin, range);
                if (isDebug) {
                    DrawPointLight(origin, bounds);
                }

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawPointLight(origin, range);
                if (isDebug) {
                    DrawPointLight(origin, bounds);
                }

            } else if (pointLightVolume.Type == PointLightVolume.LightType.SpotLight) { // Spot Light Visualization

                // Calculating

                Vector3 forward = t.forward;
                Vector3 right = t.right;
                Vector3 up = t.up;

                float spotAngle = Mathf.Clamp(pointLightVolume.Angle, 0f, 360f);
                float halfAngleRad = spotAngle * 0.5f * Mathf.Deg2Rad;
                
                Vector3[] dirs = new Vector3[] { right, -right, up, -up };
                float bounds = 0;

                bool isDebug = pointLightVolume.DebugRange && (pointLightVolume.Projection != PointLightVolume.LightProjection.LUT || pointLightVolume.FalloffLUT == null);

                if (isDebug) {
                    bounds = Mathf.Sqrt(ComputePointLightSquaredBoundingSphere(pointLightVolume.Color, pointLightVolume.Intensity, range, pointLightVolume.LightVolumeSetup.BrightnessCutoff));
                }

                // Drawing

                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawSpotLight(origin, forward, halfAngleRad, range, dirs);

                if (isDebug)
                    DrawSpotLight(origin, forward, halfAngleRad, bounds, dirs);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawSpotLight(origin, forward, halfAngleRad, range, dirs);

                if (isDebug) {
                    DrawSpotLight(origin, forward, halfAngleRad, bounds, dirs);
                }

            } else { // Area light

                float x = Mathf.Max(Mathf.Abs(pointLightVolume.transform.lossyScale.x), 0.001f);
                float y = Mathf.Max(Mathf.Abs(pointLightVolume.transform.lossyScale.y), 0.001f);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawAreaLight(origin, t.rotation, x, y);

                if(pointLightVolume.DebugRange)
                    DrawAreaLightDebug(origin, t.rotation, x, y, pointLightVolume.Color, pointLightVolume.Intensity, pointLightVolume.LightVolumeSetup.BrightnessCutoff);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawAreaLight(origin, t.rotation, x, y);

                if (pointLightVolume.DebugRange)
                    DrawAreaLightDebug(origin, t.rotation, x, y, pointLightVolume.Color, pointLightVolume.Intensity, pointLightVolume.LightVolumeSetup.BrightnessCutoff);

            }

        }

        void OnSceneGUI() {
            foreach (var obj in Selection.gameObjects) {
                var volume = obj.GetComponent<PointLightVolume>();
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
