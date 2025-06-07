using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace VRCLightVolumes {

    [CanEditMultipleObjects]
    [CustomEditor(typeof(PointLightVolume))]
    public class PointLightVolumeEditor : Editor {

        PointLightVolume PointLightVolume;

        const int TEX_SIZE = 128;
        Texture2D lutTexture;

        private void OnEnable() {
            PointLightVolume = (PointLightVolume)target;
        }

        public override void OnInspectorGUI() {

            serializedObject.Update();

            List<string> hiddenFields = new List<string> { "m_Script", "CustomID", "PointLightVolumeInstance", "LightVolumeSetup" };

            if(PointLightVolume.Type == PointLightVolume.LightType.PointLight) {
                hiddenFields.Add("Angle");
                hiddenFields.Add("Falloff");
            }

            if (PointLightVolume.Type == PointLightVolume.LightType.AreaLight) {
                hiddenFields.Add("Angle");
                hiddenFields.Add("Falloff");
                hiddenFields.Add("Shape");
                hiddenFields.Add("Range");
            } else {
                hiddenFields.Add("AreaLightWidth");
                hiddenFields.Add("AreaLightHeight");
            }

            if (PointLightVolume.Shape == PointLightVolume.LightShape.Parametric) {
                hiddenFields.Add("FalloffLUT");
                hiddenFields.Add("Cubemap");
                hiddenFields.Add("Cookie");
            } else if (PointLightVolume.Shape == PointLightVolume.LightShape.Custom) {
                hiddenFields.Add("Falloff");
                if(PointLightVolume.Type == PointLightVolume.LightType.PointLight) {
                    hiddenFields.Add("FalloffLUT");
                    hiddenFields.Add("Cookie");
                } else if (PointLightVolume.Type == PointLightVolume.LightType.SpotLight) {
                    hiddenFields.Add("FalloffLUT");
                    hiddenFields.Add("Cubemap");
                }
            } else if (PointLightVolume.Shape == PointLightVolume.LightShape.LUT) {
                hiddenFields.Add("Falloff");
                hiddenFields.Add("Cubemap");
                hiddenFields.Add("Cookie");
            }

            DrawPropertiesExcluding(serializedObject, hiddenFields.ToArray());

            serializedObject.ApplyModifiedProperties();

        }

        private void DrawVolumeGUI(PointLightVolume pointLightVolume) {

            Transform t = pointLightVolume.transform;
            Vector3 origin = t.position;
            float range = pointLightVolume.Range;

            if (pointLightVolume.Type == PointLightVolume.LightType.PointLight) { // Point Light Visualization

                // Drawing

                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawPointLight(origin, range);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawPointLight(origin, range);

            } else if (pointLightVolume.Type == PointLightVolume.LightType.SpotLight) { // Spot Light Visualization

                // Calculating

                Vector3 forward = t.forward;
                Vector3 right = t.right;
                Vector3 up = t.up;

                float spotAngle = Mathf.Clamp(pointLightVolume.Angle, 0f, 360f);
                float halfAngleRad = spotAngle * 0.5f * Mathf.Deg2Rad;
                float radius = Mathf.Abs(range) * Mathf.Sin(halfAngleRad);
                float centerOffset = range * Mathf.Cos(halfAngleRad);
                Vector3 diskCenter = origin + forward * centerOffset;
                Vector3[] dirs = new Vector3[] { right, -right, up, -up };

                // Drawing

                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawSpotLight(origin, diskCenter, forward, radius, dirs);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawSpotLight(origin, diskCenter, forward, radius, dirs);

            } else { // Area light
                
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(1f, 1f, 0f, 0.6f);
                DrawAreaLight(origin, t.rotation, pointLightVolume.AreaLightWidth, pointLightVolume.AreaLightHeight);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                Handles.color = new Color(1f, 1f, 0f, 0.15f);
                DrawAreaLight(origin, t.rotation, pointLightVolume.AreaLightWidth, pointLightVolume.AreaLightHeight);
                
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
        private void DrawSpotLight(Vector3 origin, Vector3 diskCenter, Vector3 forward, float radius, Vector3[] dirs) {
            Handles.DrawWireDisc(diskCenter, forward, radius);
            foreach (var dir in dirs) {
                Vector3 edge = diskCenter + dir * radius;
                Handles.DrawLine(origin, edge);
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

    }
}