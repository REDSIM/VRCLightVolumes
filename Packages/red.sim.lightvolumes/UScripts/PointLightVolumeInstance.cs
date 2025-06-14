
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
        [Tooltip("Defines whether this point light volume can be moved in runtime. Disabling this option slightly improves performance.")]
        public bool IsDynamic = false;
        [Tooltip("For point light: XYZ = Position, W = Inverse squared range.\nFor spot light: XYZ = Position, W = Inverse squared range, negated.\nFor area light: XYZ = Position, W = Width.")]
        public Vector4 PositionData;
        [Tooltip("For point light: XYZ = Color, W = Cos of angle (for LUT).\nFor spot light: XYZ = Color, W = Cos of outer angle if no custom texture, tan of outer angle otherwise.\nFor area light: XYZ = Color, W = 2 + Height.")]
        public Vector4 ColorData;
        [Tooltip("For point light: XYZW = Rotation quaternion.\nFor spot light: XYZ = Direction, W = Cone falloff.\nFor area light: XYZW = Rotation quaternion.")]
        public Vector4 DirectionData;
        [Tooltip("If parametric: Stores 0.\nIf uses custom lut: Stores LUT ID with positive sign.\nIf uses custom texture: Stores texture ID with negative sign.")]
        public float CustomID;
        [Tooltip("Half-angle of the spotlight cone, in radians.")]
        public float angle;
        [Tooltip("Reference to the LightVolumeManager that manages this volume. Used to notify the manager about changes in this volume.")]
        public LightVolumeManager UpdateNotifier;

        private void OnEnable() {
#if UDONSHARP
            if (Utilities.IsValid(UpdateNotifier))
#else
            if (UpdateNotifier != null)
#endif
                UpdateNotifier.RequestUpdateVolumes();
        }

        private void OnDisable() {
#if UDONSHARP
            if (Utilities.IsValid(UpdateNotifier))
#else
            if (UpdateNotifier != null)
#endif
                UpdateNotifier.RequestUpdateVolumes();
        }

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
#if COMPILER_UDONSHARP
            if (Utilities.IsValid(UpdateNotifier)) UpdateNotifier.RequestUpdateVolumes();
#endif
        }

        // Sets LUT ID
        public void SetLut(int id) {
            CustomID = id + 1;
            ColorData.w = Mathf.Cos(angle);
#if COMPILER_UDONSHARP
            if (Utilities.IsValid(UpdateNotifier)) UpdateNotifier.RequestUpdateVolumes();
#endif
        }

        // Sets Cubemap or a Cookie ID
        public void SetCustomTexture(int id) {
            CustomID = - id - 1;
            if(IsSpotLight()) { // If it's spotlight
                ColorData.w = Mathf.Tan(angle);
            }
#if COMPILER_UDONSHARP
            if (Utilities.IsValid(UpdateNotifier)) UpdateNotifier.RequestUpdateVolumes();
#endif
        }

        // Sets light into parametric mode
        public void SetParametric() {
            CustomID = 0;
            ColorData.w = Mathf.Cos(angle);
#if COMPILER_UDONSHARP
            if (Utilities.IsValid(UpdateNotifier)) UpdateNotifier.RequestUpdateVolumes();
#endif
        }

        // Sets light into the point light type
        public void SetPointLight() {
            PositionData.w = Mathf.Abs(PositionData.w);
#if COMPILER_UDONSHARP
            if (Utilities.IsValid(UpdateNotifier)) UpdateNotifier.RequestUpdateVolumes();
#endif
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
#if COMPILER_UDONSHARP
            if (Utilities.IsValid(UpdateNotifier)) UpdateNotifier.RequestUpdateVolumes();
#endif
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
#if COMPILER_UDONSHARP
            if (Utilities.IsValid(UpdateNotifier)) UpdateNotifier.RequestUpdateVolumes();
#endif
        }
        
        // Sets light into the area light type
        public void SetAreaLight() {
            PositionData.w = Mathf.Max(Mathf.Abs(transform.lossyScale.x), 0.001f);
            ColorData.w = 2 + Mathf.Max(Mathf.Abs(transform.lossyScale.y), 0.001f); // Add 2 to get out of [-1; 1] codomain of cosine
#if COMPILER_UDONSHARP
            if (Utilities.IsValid(UpdateNotifier)) UpdateNotifier.RequestUpdateVolumes();
#endif
        }

        // Sets color
        public void SetColor(Color color, float intensity) {
            Vector4 c = color * intensity;
            ColorData = new Vector4(c.x, c.y, c.z, ColorData.w);
#if COMPILER_UDONSHARP
            if (Utilities.IsValid(UpdateNotifier)) UpdateNotifier.RequestUpdateVolumes();
#endif
        }

        // Updates data required for shader
        public void UpdateTransform() {

            Vector3 pos = transform.position;
            PositionData = new Vector4(pos.x, pos.y, pos.z, PositionData.w);

            if (IsAreaLight()) {
                Quaternion rot = transform.rotation;
                DirectionData = new Vector4(rot.x, rot.y, rot.z, rot.w);
                SetAreaLight();
            } else if (IsSpotLight() && !IsCustomTexture()) { // If Spot Light with no cookie
                Vector3 dir = transform.forward;
                DirectionData = new Vector4(dir.x, dir.y, dir.z, DirectionData.w);
            } else if (!IsParametric()) { // If Point Light with a cubemap or a spot light with cookie
                Quaternion rot = Quaternion.Inverse(transform.rotation);
                DirectionData = new Vector4(rot.x, rot.y, rot.z, rot.w);
            }

        }

    }

}