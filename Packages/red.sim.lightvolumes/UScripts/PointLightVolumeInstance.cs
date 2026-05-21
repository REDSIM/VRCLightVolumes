
using UnityEngine;
#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;
#endif

namespace VRCLightVolumes {
    [DisallowMultipleComponent]
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PointLightVolumeInstance : UdonSharpBehaviour
#else
    public class PointLightVolumeInstance : MonoBehaviour
#endif
    {
        [Tooltip("Point light volume color")]
        [ColorUsage(showAlpha: false)] public Color Color;
        [Tooltip("Color multiplies by this value.")]
        public float Intensity = 1;
        [Tooltip("Defines whether this point light volume can be moved in runtime. Disabling this option slightly improves performance. Don't forget to enable \"Auto Update Volumes\" in your Light Volumes Setup to have this dynamic updates!")]
        public bool IsDynamic = false;
        [Tooltip("For point light: XYZ = Position, W = Inverse squared range.\nFor spot light: XYZ = Position, W = Inverse squared range, negated.\nFor area light: XYZ = Position, W = Width.")]
        public Vector4 PositionData;
        [Tooltip("For point light: XYZW = Rotation quaternion.\nFor spot light: XYZ = Direction, W = Cone falloff.\nFor area light: XYZW = Rotation quaternion.")]
        public Vector4 DirectionData;
        [Tooltip("Half-angle of the spotlight cone, in radians.")]
        public float Angle;
        [Tooltip("For point light: unused.\nFor spot light: Cos of outer angle if no custom texture, tan of outer angle otherwise.\nFor area light: 2 + Height.")]
        public float AngleData;
        [Tooltip("Index of the shadow map used by this light. -1 means no shadow.")]
        public float ShadowMapID = -1;
        [Tooltip("Enables World Space Shadows using the bake position. Disable for Local Space Shadows that move and rotate with this light.")]
        public bool WorldSpaceShadows = false;
        [Tooltip("World-space bias in meters applied when comparing shaded points against this light's shadow map.")]
        [Min(0)] public float ShadowBias = 0.03f;
        [Tooltip("World-space smoothing radius in meters around this light's shadow bias threshold.")]
        [Min(0)] public float ShadowBiasSmoothness = 0.02f;
        [Tooltip("Multiplier for shadow PCF sampling sharpness. 1 keeps native shadow map sharpness, lower values make shadows softer.")]
        [Range(0, 1)] public float ShadowSharpness = 1f;
        [Tooltip("World-space position where the shadow map was baked.")]
        public Vector3 ShadowBakePosition = Vector3.zero;
        [Tooltip("World-space rotation where the shadow map was baked.")]
        public Quaternion ShadowBakeRotation = Quaternion.identity;
        [Tooltip("True if this Point Light Volume is registered in the Light Volume Manager array. Disabled objects can be unregistered and will register again on enable.")]
        public bool IsInitialized = false;
        [Tooltip("Squared range after which light will be culled. Should be recalculated by executing UpdateRange() method.")]
        public float SquaredRange = 1;
        [Tooltip("Average squared lossy scale of the light. Light Source Size gets multiplied by it at the end. Updates with UpdateTransform() method.")]
        public float SquaredScale = 1;
        [Tooltip("Reference to the Light Volume Manager. Needed for runtime initialization.")]
        public LightVolumeManager LightVolumeManager;
        [Tooltip("Texture source used by this light's active LUT, cookie or cubemap projection.")]
        public Texture CustomTexture;
        [Tooltip("Material source used by this light's active LUT, cookie or cubemap projection.")]
        public Material CustomTextureMaterial;
        [Tooltip("Projection source type used by this light. 0 = none, 1 = texture, 2 = material.")]
        public int ProjectionType = 0; // 0: none, 1: texture, 2: material
        [Tooltip("Updates this light's custom texture slice every frame.")]
        public bool AutoUpdateCustomTexture = false;

        // Internal projection metadata copied from the authoring PointLightVolume
        [HideInInspector] public bool CustomTextureIsCubemap = false;
        [HideInInspector] public bool CustomTextureIsRenderTexture = false;
        [HideInInspector] public bool CustomTextureHasDepthSlices = false;
        [HideInInspector] public int ProjectionMode = 0; // 0: parametric, 1: LUT, 2: custom cookie or cubemap

        [Tooltip("Texture source used by this light's shadow map.")]
        public Texture ShadowMapTexture;
        [Tooltip("Material source used by this light's shadow map.")]
        public Material ShadowMapMaterial;
        [Tooltip("Updates this light's shadow map cubemap every frame.")]
        public bool AutoUpdateShadowMap = false;

        // Internal shadow source metadata copied from the authoring PointLightVolume
        [HideInInspector] public bool ShadowMapTextureIsCubemap = false;
        [HideInInspector] public bool ShadowMapTextureHasDepthSlices = false;

        // Internal dirty flag consumed by LightVolumeManager to recalculate this light's range
        [HideInInspector] public bool IsRangeDirty = false;
        private Vector3 _prevPosition = Vector3.zero;
        private Quaternion _prevRotation = Quaternion.identity;
        private Vector3 _prevScale = Vector3.one;

        private Color _old_Color = Color.white;
        private float _old_Intensity = 1;

#if UDONSHARP
        // Works only when changing values directly on UdonBehaviour
        // Low level Udon hacks:
        // _old_(Name) variables are the old values of the variables
        // _onVarChange_(Name) methods (events) are called when the variable changes
        public void _onVarChange_Color() {
            if (_old_Color != Color) MarkRangeDirtyAndUpdateVolumes();
        }
        public void _onVarChange_Intensity() {
            if (_old_Intensity != Intensity) MarkRangeDirtyAndUpdateVolumes();
        }
#endif

#if !UDONSHARP || UNITY_EDITOR
        // To make it work when changing values on UdonSharpBehaviour in editor
        private void Update() {
            if (_old_Color != Color || _old_Intensity != Intensity) {
                _old_Color = Color;
                _old_Intensity = Intensity;
                if (LightVolumeManager != null) LightVolumeManager.RequestUpdateVolumes();
            }
        }
#endif

        private void Start() {
#if !UDONSHARP
            if (LightVolumeManager == null) {
                LightVolumeManager = FindObjectOfType<LightVolumeManager>();
            }
#endif
            if (!IsInitialized && LightVolumeManager != null) {
                LightVolumeManager.InitializePointLightVolume(this);
            }
        }

        private void OnEnable() {
            if (LightVolumeManager != null) {
                LightVolumeManager.InitializePointLightVolume(this);
            }
            if (LightVolumeManager != null) LightVolumeManager.RequestUpdateVolumes();
        }

        private void OnDisable() {
            if (LightVolumeManager != null) {
                LightVolumeManager.UnregisterPointLightVolume(this);
            }
            if (LightVolumeManager != null) LightVolumeManager.RequestUpdateVolumes();
        }

        // Checks whether this instance is a spotlight
        public bool IsSpotLight() {
            return PositionData.w < 0;
        }
        
        // Checks whether this instance is a point light
        public bool IsPointLight() {
            return PositionData.w >= 0 && AngleData <= 1.5;
        }

        // Checks whether this instance is an area light
        public bool IsAreaLight() {
            return PositionData.w >= 0 && AngleData > 1.5;
        }

        // Checks whether this instance uses a custom texture
        public bool IsCustomTexture() {
            return ProjectionMode == 2; // 2: custom cookie or cubemap
        }

        // Checks whether this instance uses a LUT
        public bool IsLut() {
            return ProjectionMode == 1; // 1: LUT
        }

        // Checks whether this instance uses parametric mode
        public bool IsParametric() {
            return ProjectionMode == 0; // 0: parametric
        }

        // Sets light source size or range data for LUT mode
        public void SetLightSourceSize(float size) {
            if (IsLut()) {
                PositionData.w = Mathf.Sign(PositionData.w) / (size * size); // Preserve the previous sign. Inverse squared range
            } else {
                PositionData.w = Mathf.Sign(PositionData.w) * size * size; // Preserve the previous sign. Squared light size
            }
            MarkRangeDirtyAndUpdateVolumes();
        }

        // Sets LUT mode
        public void SetLut() {
            ProjectionMode = 1; // 1: LUT
            AngleData = Mathf.Cos(Angle);
            UpdateRotation();
            MarkRangeDirtyAndUpdateVolumes();
        }

        // Sets cubemap or cookie projection mode
        public void SetCustomTexture() {
            SetCustomProjectionMode();
            MarkRangeDirtyAndUpdateVolumes();
        }

        // Sets a texture source for this light's custom projection and refreshes manager runtime texture caches
        public void SetCustomTexture(Texture texture, bool isCubemap, bool autoUpdate) {
            CustomTexture = texture;
            CustomTextureMaterial = null;
            ProjectionType = 0; // 0: none
            AutoUpdateCustomTexture = false;
            CustomTextureIsRenderTexture = false;
            CustomTextureIsCubemap = false;
            CustomTextureHasDepthSlices = false;

            if (texture != null) {
                ProjectionType = 1; // 1: texture
                AutoUpdateCustomTexture = autoUpdate;

                CustomTextureIsRenderTexture = autoUpdate;
                if (isCubemap) {
                    int textureDimension = (int)texture.dimension;
                    if (textureDimension == 4) CustomTextureIsCubemap = true; // 4: TextureDimension.Cube
                    else if (textureDimension == 5) CustomTextureHasDepthSlices = true; // 5: TextureDimension.Tex2DArray
                }

                SetCustomProjectionMode();
            } else {
                SetParametricMode();
            }
            ReinitializeCustomTexturesAndUpdateVolumes();
        }

        // Sets a material source for this light's custom projection and refreshes manager runtime texture caches
        public void SetCustomMaterial(Material material, bool autoUpdate) {
            CustomTexture = null;
            CustomTextureMaterial = material;
            ProjectionType = 0; // 0: none
            AutoUpdateCustomTexture = false;
            CustomTextureIsRenderTexture = false;
            CustomTextureIsCubemap = false;
            CustomTextureHasDepthSlices = false;

            if (material != null) {
                ProjectionType = 2; // 2: material
                AutoUpdateCustomTexture = autoUpdate;
                SetCustomProjectionMode();
            } else {
                SetParametricMode();
            }
            ReinitializeCustomTexturesAndUpdateVolumes();
        }

        // Sets the light into parametric mode
        public void SetParametric() {
            SetParametricMode();
            MarkRangeDirtyAndUpdateVolumes();
        }

        // Sets the light into the point light type
        public void SetPointLight() {
            PositionData.w = Mathf.Abs(PositionData.w);
            UpdateRotation();
            MarkRangeDirtyAndUpdateVolumes();
        }

        // Sets the light into the spotlight type with both angle and falloff because angle is required to determine falloff
        public void SetSpotLight(float angleDeg, float falloff) {
            Angle = angleDeg * Mathf.Deg2Rad * 0.5f;
            if (IsCustomTexture()) {
                AngleData = Mathf.Tan(Angle); // Use custom texture projection
            } else {
                AngleData = Mathf.Cos(Angle);
                DirectionData.w = 1 / (Mathf.Cos(Angle * (1.0f - Mathf.Clamp01(falloff))) - AngleData);
            }
            PositionData.w = - Mathf.Abs(PositionData.w);
            UpdateRotation();
            MarkRangeDirtyAndUpdateVolumes();
        }

        // Sets the light into the spotlight type with a specified angle
        public void SetSpotLight(float angleDeg) {
            Angle = angleDeg * Mathf.Deg2Rad * 0.5f;
            if (IsCustomTexture()) {
                AngleData = Mathf.Tan(Angle); // Use custom texture projection
            } else {
                AngleData = Mathf.Cos(Angle);
            }
            PositionData.w = - Mathf.Abs(PositionData.w);
            UpdateRotation();
            MarkRangeDirtyAndUpdateVolumes();
        }
        
        // Sets the light into the area light type
        public void SetAreaLight() {
            PositionData.w = Mathf.Max(Mathf.Abs(transform.lossyScale.x), 0.001f);
            AngleData = 2 + Mathf.Max(Mathf.Abs(transform.lossyScale.y), 0.001f); // Add 2 to move outside the [-1; 1] cosine codomain
            UpdateRotation();
            MarkRangeDirtyAndUpdateVolumes();
        }

        // Sets light source color
        public void SetColor(Color color) {
            Color = color;
            MarkRangeDirtyAndUpdateVolumes();
        }

        // Sets light source intensity
        public void SetIntensity(float intensity) {
            Intensity = intensity;
            MarkRangeDirtyAndUpdateVolumes();
        }

        // Marks this light range dirty and immediately refreshes the manager shader data
        private void MarkRangeDirtyAndUpdateVolumes() {
            IsRangeDirty = true;
            if (LightVolumeManager != null) LightVolumeManager.RequestUpdateVolumes();
        }

        // Marks projection source caches dirty by rebuilding them before the shader data refresh
        private void ReinitializeCustomTexturesAndUpdateVolumes() {
            IsRangeDirty = true;
            if (LightVolumeManager == null) return;
            LightVolumeManager.ReinitializeCustomTextures();
            LightVolumeManager.RequestUpdateVolumes();
        }

        // Applies the internal custom projection mode without touching texture source fields
        private void SetCustomProjectionMode() {
            ProjectionMode = 2; // 2: custom cookie or cubemap
            if(IsSpotLight()) AngleData = Mathf.Tan(Angle);
            UpdateRotation();
        }

        // Applies the internal parametric projection mode without touching texture source fields
        private void SetParametricMode() {
            ProjectionMode = 0; // 0: parametric
            AngleData = Mathf.Cos(Angle);
            UpdateRotation();
        }

        // Updates data required for shader
        public void UpdateTransform() {

            // Position Update
            Vector3 pos = transform.position;
            if (_prevPosition != pos) {
                _prevPosition = pos;
                UpdatePosition();
            }

            // Rotation Update
            Quaternion rot = transform.rotation;
            if (_prevRotation != rot) {
                _prevRotation = rot;
                UpdateRotation();
            }

            // Scale Update
            Vector3 lscale = transform.lossyScale;
            if (_prevScale != lscale) {
                _prevScale = lscale;
                UpdateScale();
            }
              
        }

        // Force update position
        public void UpdatePosition() {
            Vector3 pos = transform.position;
            PositionData = new Vector4(pos.x, pos.y, pos.z, PositionData.w);
        }
        
        // Force update rotation
        public void UpdateRotation() {
            Quaternion rot = transform.rotation;
            bool isAreaLight = PositionData.w >= 0 && AngleData > 1.5f;
            bool isSpotLight = PositionData.w < 0;
            if (isAreaLight) {
                DirectionData = new Vector4(rot.x, rot.y, rot.z, rot.w);
            } else if (isSpotLight && ProjectionMode != 2) { // Spot light without a cookie
                Vector3 dir = transform.forward;
                DirectionData = new Vector4(dir.x, dir.y, dir.z, DirectionData.w);
            } else if (ProjectionMode != 0) { // If Point Light with a cubemap or a spot light with cookie
                rot = Quaternion.Inverse(rot);
                DirectionData = new Vector4(rot.x, rot.y, rot.z, rot.w);
            }
        }

        // Force update scale
        public void UpdateScale() {
            Vector3 lscale = transform.lossyScale;
            if (PositionData.w >= 0 && AngleData > 1.5f) {
                PositionData.w = Mathf.Max(Mathf.Abs(lscale.x), 0.001f);
                AngleData = 2 + Mathf.Max(Mathf.Abs(lscale.y), 0.001f);
                UpdateRotation();
            }
            SquaredScale = (lscale.x + lscale.y + lscale.z) / 3;
            SquaredScale *= SquaredScale;
            MarkRangeDirtyAndUpdateVolumes();
        }

        // Recalculates squared culling range for the light
        public void UpdateRange() {
            float cutoff = LightVolumeManager != null ? LightVolumeManager.LightsBrightnessCutoff : 0.35f;
            if (PositionData.w >= 0 && AngleData > 1.5f) { // Area light squared distance math
                SquaredRange = ComputeAreaLightSquaredBoundingSphere(Mathf.Abs(SquaredScale / PositionData.w), AngleData - 2, Color, Intensity * Mathf.PI, cutoff);
            } else if(ProjectionMode == 1) { // LUT uses regular squared range
                SquaredRange = Mathf.Abs(SquaredScale / PositionData.w);
            } else { // Spot and Point light squared distance math
                SquaredRange = ComputePointLightSquaredBoundingSphere(Color, Intensity, Mathf.Abs(SquaredScale * PositionData.w), cutoff);
            }
            IsRangeDirty = false;
        }

        private float ComputeAreaLightSquaredBoundingSphere(float width, float height, Color color, float intensity, float cutoff) {
            float minSolidAngle = Mathf.Clamp(cutoff / (Mathf.Max(color.r, Mathf.Max(color.g, color.b)) * intensity), -Mathf.PI * 2f, Mathf.PI * 2);
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

        private float ComputePointLightSquaredBoundingSphere(Color color, float intensity, float sqSize, float cutoff) {
            float L = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            return Mathf.Max(Mathf.PI * 2 * L * Mathf.Abs(intensity) / (cutoff * cutoff) - 1, 0) * sqSize;
        }

    }

}
