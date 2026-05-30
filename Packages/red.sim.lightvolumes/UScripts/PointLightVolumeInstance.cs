using UnityEngine;
using UnityEngine.Serialization;
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
        [Header("Light Setup")]
        [Tooltip("Defines whether this point light volume can be moved in runtime. Disabling this option slightly improves performance. Don't forget to enable \"Auto Update Volumes\" in your Light Volumes Setup to have this dynamic updates!")]
        public bool IsDynamic = false;
        [Tooltip("Point light volume shape. 0 = point, 1 = spot, 2 = area.")]
        public int LightType = 0; // 0: point, 1: spot, 2: area
        [Tooltip("Multiplies the point light volume's color by this value.")]
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;
        [Tooltip("Brightness of the point light volume.")]
        public float Intensity = 1f;

        [Header("Position Data")]
        [Tooltip("World-space position used by this point light volume.")]
        public Vector3 Position = Vector3.zero;
        [Tooltip("Light source size used by parametric point lights, parametric spot lights, cookies and cubemap projections.")]
        [Min(0.0001f)] public float LightSourceSize = 1f;
        [Tooltip("Inverse squared range used by LUT projection.")]
        [Min(0)] public float InverseSquaredRange = 1f;
        [Tooltip("Area light width in meters.")]
        [Min(0.001f)] public float Width = 1f;

        [Header("Direction Data")]
        [Tooltip("World-space spotlight direction used by parametric and LUT spot lights.")]
        public Vector3 Direction = Vector3.forward;
        [Tooltip("Rotation used by area lights, cubemap projections and cookie projections.")]
        public Quaternion Rotation = Quaternion.identity;
        [Tooltip("Spotlight cone falloff multiplier used by parametric spot lights.")]
        public float ConeFalloff = 1f;

        [Header("Angle Data")]
        [Tooltip("Half-angle of the spotlight cone, in radians.")]
        public float Angle;
        [Tooltip("Cosine of the spotlight outer angle used by parametric and LUT spot lights.")]
        public float OuterAngleCos = 1f;
        [Tooltip("Tangent of the spotlight outer angle used by cookie projection.")]
        public float OuterAngleTan = 0f;
        [Tooltip("Area light height in meters.")]
        [Min(0.001f)] public float Height = 1f;

        [Header("Runtime State")]
        [Tooltip("Squared range after which light will be culled. Should be recalculated by executing UpdateRange() method.")]
        public float SquaredRange = 1f;
        [Tooltip("Average squared lossy scale of the light. Light Source Size gets multiplied by it at the end. Updates with UpdateTransform() method.")]
        public float SquaredScale = 1f;
        [Tooltip("Reference to the Light Volume Manager. Needed for runtime initialization.")]
        public LightVolumeManager LightVolumeManager;

        [Header("Projection Source")]
        [Tooltip("Texture source used by this light's active LUT, cookie or cubemap projection.")]
        public Texture CustomTexture;
        [Tooltip("Material source used by this light's active LUT, cookie or cubemap projection.")]
        public Material CustomTextureMaterial;
        [Tooltip("Projection source type used by this light. 0 = none, 1 = texture, 2 = material.")]
        public int ProjectionType = 0; // 0: none, 1: texture, 2: material
        [Tooltip("Projection mode used by this light. 0 = parametric, 1 = LUT, 2 = custom cookie or cubemap.")]
        public int ProjectionMode = 0; // 0: parametric, 1: LUT, 2: custom cookie or cubemap
        [Tooltip("Updates this light's custom texture slice every frame.")]
        public bool AutoUpdateCustomTexture = false;

        [Header("Shadow Source")]
        [Tooltip("Texture source used by this light's shadow map.")]
        public Texture ShadowMapTexture;
        [Tooltip("Material source used by this light's shadow map.")]
        public Material ShadowMapMaterial;
        [Tooltip("Updates this light's shadow map texture every frame.")]
        public bool AutoUpdateShadowMap = false;
        [Tooltip("Index of the shadow map used by this light. -1 means no shadow.")]
        public float ShadowMapID = -1f;
        [Tooltip("Enables World Space Shadows using the bake position. Disable for Local Space Shadows that move and rotate with this light.")]
        public bool WorldSpaceShadows = false;
        [Tooltip("World-space position where the shadow map was baked.")]
        public Vector3 ShadowBakePosition = Vector3.zero;

        [Header("Shadow Bake Settings")]
        [Tooltip("Layer mask used by the shadow bake camera. Only these layers can write into the shadow depth pass.")]
        [FormerlySerializedAs("ShadowCullingMask")]
        public int LayerMask = -1;
        [Tooltip("Near clip plane used by the shadow bake camera. Higher values improve depth precision but clip nearby occluders.")]
        [FormerlySerializedAs("ShadowNearClip")]
        [Min(0.0001f)] public float NearClip = 0.01f;
        [Tooltip("World-space bias in meters applied while baking this light's shadow map. Larger values reduce self-shadow artifacts, but can detach contact edges. Requires rebaking.")]
        [FormerlySerializedAs("ShadowBias")]
        [Min(0)] public float Bias = 0.03f;
        [Tooltip("Far clip distance used when the EVSM shadow map was baked. 0 falls back to this light's current culling range.")]
        [FormerlySerializedAs("ShadowFarClip")]
        [Min(0)] public float FarClip = 0f;
        [Tooltip("Editor-only Gaussian blur radius in shadow texels applied after baking. 0 keeps the baked shadow map unblurred.")]
        [FormerlySerializedAs("ShadowBlur")]
        [Min(0)] public float Blur = 1f;
        [Tooltip("Hardens shadows near the contact areas. Can produce artefacts, so use with caution! Requires rebaking.")]
        [FormerlySerializedAs("ShadowBlurDepth")]
        [Range(0, 1)] public float ContactHardening = 0f;

        // Internal projection metadata copied from the authoring PointLightVolume
        [HideInInspector] public bool CustomTextureIsCubemap = false;
        [HideInInspector] public bool CustomTextureHasDepthSlices = false;

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
            if (LightVolumeManager != null) {
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
            return LightType == 1; // 1: spot
        }
        
        // Checks whether this instance is a point light
        public bool IsPointLight() {
            return LightType == 0; // 0: point
        }

        // Checks whether this instance is an area light
        public bool IsAreaLight() {
            return LightType == 2; // 2: area
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
            float safeSize = Mathf.Max(Mathf.Abs(size), 0.0001f);
            LightSourceSize = safeSize;
            InverseSquaredRange = 1f / (safeSize * safeSize);
            MarkRangeDirtyAndUpdateVolumes();
        }

        // Sets LUT mode
        public void SetLut() {
            ProjectionMode = 1; // 1: LUT
            OuterAngleCos = Mathf.Cos(Angle);
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
            CustomTextureIsCubemap = false;
            CustomTextureHasDepthSlices = false;

            if (texture != null) {
                ProjectionType = 1; // 1: texture
                AutoUpdateCustomTexture = autoUpdate;

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
            LightType = 0; // 0: point
            Position = transform.position;
            UpdateRotation();
            MarkRangeDirtyAndUpdateVolumes();
        }

        // Sets the light into the spotlight type with both angle and falloff because angle is required to determine falloff
        public void SetSpotLight(float angleDeg, float falloff) {
            LightType = 1; // 1: spot
            Angle = angleDeg * Mathf.Deg2Rad * 0.5f;
            if (IsCustomTexture()) {
                OuterAngleTan = Mathf.Tan(Angle);
            } else {
                OuterAngleCos = Mathf.Cos(Angle);
                ConeFalloff = 1f / (Mathf.Cos(Angle * (1.0f - Mathf.Clamp01(falloff))) - OuterAngleCos);
            }
            Position = transform.position;
            UpdateRotation();
            MarkRangeDirtyAndUpdateVolumes();
        }

        // Sets the light into the spotlight type with a specified angle
        public void SetSpotLight(float angleDeg) {
            LightType = 1; // 1: spot
            Angle = angleDeg * Mathf.Deg2Rad * 0.5f;
            if (IsCustomTexture()) {
                OuterAngleTan = Mathf.Tan(Angle);
            } else {
                OuterAngleCos = Mathf.Cos(Angle);
            }
            Position = transform.position;
            UpdateRotation();
            MarkRangeDirtyAndUpdateVolumes();
        }
        
        // Sets the light into the area light type
        public void SetAreaLight() {
            LightType = 2; // 2: area
            Position = transform.position;
            Width = Mathf.Max(Mathf.Abs(transform.lossyScale.x), 0.001f);
            Height = Mathf.Max(Mathf.Abs(transform.lossyScale.y), 0.001f);
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
            if (IsSpotLight()) {
                OuterAngleTan = Mathf.Tan(Angle);
            }
            UpdateRotation();
        }

        // Applies the internal parametric projection mode without touching texture source fields
        private void SetParametricMode() {
            ProjectionMode = 0; // 0: parametric
            OuterAngleCos = Mathf.Cos(Angle);
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
            Position = transform.position;
        }
        
        // Force update rotation
        public void UpdateRotation() {
            Quaternion rot = transform.rotation;
            if (LightType == 2) { // 2: area
                Rotation = rot;
            } else if (LightType == 1 && ProjectionMode != 2) { // 1: spot, 2: custom cookie
                Direction = transform.forward;
            } else if (ProjectionMode != 0) { // 0: parametric; non-parametric point/cookie uses inverse rotation
                rot = Quaternion.Inverse(rot);
                Rotation = rot;
            }
        }

        // Force update scale
        public void UpdateScale() {
            Vector3 lscale = transform.lossyScale;
            if (LightType == 2) { // 2: area
                Width = Mathf.Max(Mathf.Abs(lscale.x), 0.001f);
                Height = Mathf.Max(Mathf.Abs(lscale.y), 0.001f);
                UpdateRotation();
            }
            SquaredScale = (lscale.x + lscale.y + lscale.z) / 3;
            SquaredScale *= SquaredScale;
            MarkRangeDirtyAndUpdateVolumes();
        }

        // Recalculates squared culling range for the light
        public void UpdateRange() {
            float cutoff = LightVolumeManager != null ? LightVolumeManager.LightsBrightnessCutoff : 0.35f;
            if (LightType == 2) { // 2: area
                SquaredRange = ComputeAreaLightSquaredBoundingSphere(Mathf.Abs(SquaredScale / Width), Height, Color, Intensity * Mathf.PI, cutoff);
            } else if (ProjectionMode == 1) { // 1: LUT
                SquaredRange = Mathf.Abs(SquaredScale / InverseSquaredRange);
            } else { // Spot and Point light squared distance math
                SquaredRange = ComputePointLightSquaredBoundingSphere(Color, Intensity, Mathf.Abs(SquaredScale * LightSourceSize * LightSourceSize), cutoff);
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
