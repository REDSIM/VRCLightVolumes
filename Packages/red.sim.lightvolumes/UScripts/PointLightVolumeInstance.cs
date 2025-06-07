
using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;
#endif

namespace VRCLightVolumes {
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PointLightVolumeInstance : UdonSharpBehaviour
#else
    public class PointLightVolumeInstance : MonoBehaviour
#endif
    {
        public bool IsDynamic = false;
        public Vector4 PositionData;
        public Vector4 ColorData;
        public Vector4 DirectionData;
        public float CustomID;
        public float angle;

        // Checks if it's a spotlight
        public bool IsSpotLight() {
            return PositionData.w < 0;
        }
        
        // Checks if it's a point light
        public bool IsPointLight() {
            return PositionData.w >= 0 && ColorData.w <= 1.5;
        }

        // Checks if it's an area light
        public bool IsAreaLight() {
            return PositionData.w >= 0 && ColorData.w > 1.5;
        }

        // Checks if uses custom texture
        public bool IsCustomTexture() {
            return CustomID < 0;
        }

        // Checks if uses LUT
        public bool IsLut() {
            return CustomID > 0;
        }

        // Checks if uses Parametric mode
        public bool IsParametric() {
            return CustomID == 0;
        }

        // Sets range data which is actually an inverted squared range
        public void SetRange(float range) {
            PositionData.w = Mathf.Sign(PositionData.w) / (range * range); // Saving the sign that was here before
        }

        // Sets LUT ID
        public void SetLut(int id) {
            CustomID = id + 1;
            ColorData.w = Mathf.Cos(angle);
        }

        // Sets Cubemap or a Cookie ID
        public void SetCustomTexture(int id) {
            CustomID = - id - 1;
            if(IsSpotLight()) { // If it's spotlight
                ColorData.w = Mathf.Tan(angle);
            }
        }

        // Sets light into parametric mode
        public void SetParametric() {
            CustomID = 0;
            ColorData.w = Mathf.Cos(angle);
        }

        // Sets light into the point light type
        public void SetPointLight() {
            PositionData.w = Mathf.Abs(PositionData.w);
        }

        // Sets light into the spot light type with both angle and falloff because angle required to determine falloff anyway
        public void SetSpotLight(float angleDeg, float falloff) {
            angle = angleDeg * Mathf.Deg2Rad * 0.5f;
            if (IsCustomTexture()) {
                ColorData.w = Mathf.Tan(angle); // Using Custom Tex
            } else {
                ColorData.w = Mathf.Cos(angle);
                DirectionData.w = 1 / (Mathf.Cos(angle * (1.0f - Mathf.Clamp01(falloff))) - ColorData.w);
            }
            PositionData.w = - Mathf.Abs(PositionData.w);
        }

        // Sets light into the spot light type with angle specified
        public void SetSpotLight(float angleDeg) {
            angle = angleDeg * Mathf.Deg2Rad * 0.5f;
            if (IsCustomTexture()) {
                ColorData.w = Mathf.Tan(angle); // Using Custom Tex
            } else {
                ColorData.w = Mathf.Cos(angle);
            }
            PositionData.w = - Mathf.Abs(PositionData.w);
        }
        
        // Sets light into the area light type
        public void SetAreaLight(float width, float height) {
            PositionData.w = width;
            ColorData.w = 2 + height; // Add 2 to get out of [-1; 1] codomain of cosine
        }

        // Sets color
        public void SetColor(Color color, float intensity) {
            Vector4 c = color * intensity;
            ColorData = new Vector4(c.x, c.y, c.z, ColorData.w);
        }

        // Updates data required for shader
        public void UpdateTransform() {

            Vector3 pos = transform.position;
            PositionData = new Vector4(pos.x, pos.y, pos.z, PositionData.w);

            if (IsAreaLight()) {
                Quaternion rot = transform.rotation;
                DirectionData = new Vector4(rot.x, rot.y, rot.z, rot.w);
            } if (IsSpotLight() && !IsCustomTexture()) { // If Spot Light with no cookie
                Vector3 dir = transform.forward;
                DirectionData = new Vector4(dir.x, dir.y, dir.z, DirectionData.w);
            } else if (!IsParametric()) { // If Point Light with a cubemap or a spot light with cookie
                Quaternion rot = Quaternion.Inverse(transform.rotation);
                DirectionData = new Vector4(rot.x, rot.y, rot.z, rot.w);
            }

        }

    }

}