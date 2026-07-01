using UnityEngine;
#if UDONSHARP
using UdonSharp;
#endif
#if COMPILER_UDONSHARP
using VRC.SDK3.Rendering;
#else
using UnityEngine.Rendering;
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
        [Tooltip("Multiplies the point light volume’s color by this value.")]
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;
        [Tooltip("Brightness of the point light volume.")]
        public float Intensity = 1f;
        [Tooltip("Controls per-surface Point Light shading and shadow opacity based on surface normal. 0 disables this extra shading and shadows for this light; 1 applies them fully. Modern individual speculars use the same light mask.")]
        [Range(0, 1)] public float ShadingStrength = 1f;

        [Header("Position Data")]
        [Tooltip("World-space position used by this point light volume.")]
        public Vector3 Position = Vector3.zero;
        [Tooltip("Light source size used by parametric Point Lights, parametric Spot Lights, cookies and cubemap projections. It affects calculated range and broadens size-aware specular highlights in modern compatible shaders.")]
        [Min(0.0001f)] public float LightSourceSize = 1f;
        [Tooltip("Inverse squared range used by LUT projection.")]
        [Min(0)] public float InverseSquaredRange = 1f;
        [Tooltip("Area Light width in meters. Affects textured Area Light emission and size-aware Area Light speculars in modern compatible shaders.")]
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
        [Tooltip("Width / height aspect used by custom spotlight cookie projection. 1 keeps a square projector; values above 1 compress projected height.")]
        [Min(0.001f)] public float SpotCookieAspect = 1f;
        [Tooltip("Area Light height in meters. Affects textured Area Light emission and size-aware Area Light speculars in modern compatible shaders.")]
        [Min(0.001f)] public float Height = 1f;

        [Header("Runtime State")]
        [Tooltip("Squared range after which light will be culled. Recalculated by the Light Volume Manager.")]
        public float SquaredRange = 1f;
        [Tooltip("Average squared lossy scale of the light. Light Source Size uses this for range and size-aware specular calculations. Updates with UpdateTransform() method.")]
        public float SquaredScale = 1f;
        [Tooltip("Reference to the Light Volume Manager. Needed for runtime initialization.")]
        public LightVolumeManager LightVolumeManager;
        [Tooltip("Internal stable manager registry tie-breaker used when this point light volume is enabled at runtime. Use SetWeight(float weight) to change priority.")]
        [HideInInspector] public int RegistryOrder = 2147483647;
        [Tooltip("Manager registry sort weight. Higher weights are uploaded to shaders first.")]
        [HideInInspector] public float RegistryWeight = 0f;
        [HideInInspector] public bool IsActive = true;

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
        [Tooltip("Use it if you don't want to move baked shadows together with their light. Attaches shadows to the world space basically. Less optimized when turned on.")]
        public bool WorldSpaceShadows = false;
        [Tooltip("World-space position where the shadow map was baked.")]
        public Vector3 ShadowBakePosition = Vector3.zero;
        [Tooltip("World-space rotation where the shadow map was baked.")]
        public Quaternion ShadowBakeRotation = Quaternion.identity;

        [Header("Shadow Bake Settings")]
        [Tooltip("Layers that can cast shadows.")]
        public int LayerMask = -1;
        [Tooltip("Near clip plane used by the shadow bake camera. Higher values can clip nearby occluders.")]
        [Min(0.0001f)] public float NearClip = 0.01f;
        [Tooltip("World-space bias in meters applied while baking this light's shadow map. Larger values reduce self-shadow artifacts, but can detach contact edges. Requires rebaking.")]
        [Min(0)] public float Bias = 0.03f;
        [Tooltip("Far clip distance used when the shadow map was baked. 0 falls back to this light's current culling range.")]
        [Min(0)] public float FarClip = 0f;
        [Tooltip("Shadow blur radius applied after baking, normalized to 128x128 shadow resolution. Editor baking uses spherical shadow-space blur to reduce visible cubemap and Spot Light projection seams. Runtime baking uses Planar Blur unless Spherical Blur is enabled on the runtime baker. 0 keeps the baked shadow map unblurred. Requires rebaking.")]
        [Min(0)] public float Blur = 1f;
        [Tooltip("Hardens shadows near the contact areas. Can produce artefacts, so use with caution. Requires rebaking. More performant when set to 0 in realtime mode. Runtime baker Spherical Blur also applies to contact hardening samples.")]
        [Range(0, 1)] public float ContactHardening = 0f;

        // Internal projection metadata copied from the authoring PointLightVolume
        [HideInInspector] public bool CustomTextureIsCubemap = false;
        [HideInInspector] public bool CustomTextureHasDepthSlices = false;

        // Internal shadow source metadata copied from the authoring PointLightVolume
        [HideInInspector] public bool ShadowMapTextureIsCubemap = false;
        [HideInInspector] public bool ShadowMapTextureHasDepthSlices = false;
        [HideInInspector] public bool ShadowMapUsesCubemap = true;

        // Internal dirty flag consumed by LightVolumeManager to recalculate this light's range
        [HideInInspector] public bool IsRangeDirty = false;

        private Vector3 _prevPosition = Vector3.zero;
        private Quaternion _prevRotation = Quaternion.identity;
        private Vector3 _prevScale = Vector3.one;

        private Color _old_Color = Color.white;
        private float _old_Intensity = 1;
        private float _old_ShadingStrength = 1;
        private bool _isRegisteredWithManager = false;
        [HideInInspector] public int AreaCookieAverageCustomId = -1;
#if COMPILER_UDONSHARP
        private Color32[] _areaCookieAveragePixels = new Color32[0];
#endif

#if UDONSHARP
        // Works only when changing values directly on UdonBehaviour
        // Low level Udon hacks:
        // _old_(Name) variables are the old values of the variables
        // _onVarChange_(Name) methods (events) are called when the variable changes
        public void _onVarChange_Color() {
            if (_old_Color != Color) {
                _old_Color = Color;
                MarkRangeDirtyAndNotify(false, false, false);
            }
        }
        public void _onVarChange_Intensity() {
            if (_old_Intensity != Intensity) {
                _old_Intensity = Intensity;
                MarkRangeDirtyAndNotify(false, false, false);
            }
        }
        public void _onVarChange_ShadingStrength() {
            if (_old_ShadingStrength != ShadingStrength) {
                float oldStrength = _old_ShadingStrength;
                _old_ShadingStrength = ShadingStrength;
                NotifyManager((Mathf.Clamp01(oldStrength) <= 0) != (Mathf.Clamp01(ShadingStrength) <= 0), false, false);
            }
        }
#endif

#if UDONSHARP || UNITY_EDITOR
        // Registers this instance after the manager reference is assigned at runtime.
        public void _onVarChange_LightVolumeManager() {
            RegisterWithManager();
        }
#endif

#if !UDONSHARP || UNITY_EDITOR
        // To make it work when changing values on UdonSharpBehaviour in editor
        private void Update() {
            if (_old_Color != Color || _old_Intensity != Intensity) {
                _old_Color = Color;
                _old_Intensity = Intensity;
                MarkRangeDirtyAndNotify(false, false, false);
            }
            if (_old_ShadingStrength != ShadingStrength) {
                float oldStrength = _old_ShadingStrength;
                _old_ShadingStrength = ShadingStrength;
                NotifyManager((Mathf.Clamp01(oldStrength) <= 0) != (Mathf.Clamp01(ShadingStrength) <= 0), false, false);
            }
        }
#endif

        // Sends this instance change to the manager when it is active.
        private void NotifyManager(bool rebuildFinalData, bool customTexturesChanged, bool shadowTexturesChanged) {
            bool wasActive = IsActive;
            IsActive = gameObject.activeInHierarchy && Intensity != 0 && Color != Color.black;
            if (LightVolumeManager == null || !gameObject.activeInHierarchy) return;
            if (wasActive != IsActive) {
                if (CustomTexture != null || CustomTextureMaterial != null) customTexturesChanged = true;
                if (ShadowMapID >= 0) shadowTexturesChanged = true;
            }
            LightVolumeManager.NotifyPointLightVolumeChanged(this, rebuildFinalData, customTexturesChanged, shadowTexturesChanged);
        }

        // Registers this instance once for the current active lifecycle.
        private void RegisterWithManager() {
            IsActive = gameObject.activeInHierarchy && Intensity != 0 && Color != Color.black;
            if (LightVolumeManager == null || _isRegisteredWithManager) return;
            LightVolumeManager.InitializePointLightVolume(this);
            _isRegisteredWithManager = true;
        }

        private void Start() {
#if !UDONSHARP
            if (LightVolumeManager == null) {
                LightVolumeManager = FindObjectOfType<LightVolumeManager>();
            }
#endif
            RegisterWithManager();
        }

        private void OnEnable() {
            RegisterWithManager();
        }

        private void OnDisable() {
            bool customTexturesChanged = IsActive && (CustomTexture != null || CustomTextureMaterial != null);
            bool shadowTexturesChanged = IsActive && ShadowMapID >= 0;
            IsActive = false;
            if (LightVolumeManager != null) {
                LightVolumeManager.DeinitializePointLightVolume(this, customTexturesChanged, shadowTexturesChanged);
            }
            _isRegisteredWithManager = false;
        }

#if COMPILER_UDONSHARP
        // Receives the area-cookie fallback average and sends it back to the manager
        public override void OnAsyncGpuReadbackComplete(VRCAsyncGPUReadbackRequest request) {
            if (request.hasError) return;
            if (_areaCookieAveragePixels.Length != 1) _areaCookieAveragePixels = new Color32[1];
            if (!request.TryGetData(_areaCookieAveragePixels)) return;
            LightVolumeManager.UploadAreaCookieAverageColor(AreaCookieAverageCustomId, _areaCookieAveragePixels[0]);
        }
#else
        // Receives the area-cookie fallback average and sends it back to the manager
        public void OnAsyncGpuReadbackComplete(AsyncGPUReadbackRequest request) {
            if (request.hasError) return;
            Unity.Collections.NativeArray<Color32> pixels = request.GetData<Color32>();
            if (pixels.Length <= 0) return;
            LightVolumeManager.UploadAreaCookieAverageColor(AreaCookieAverageCustomId, pixels[0]);
#if UNITY_EDITOR
            if (!Application.isPlaying) {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                UnityEditor.SceneView.RepaintAll();
            }
#endif
        }
#endif

        // Sets dynamic mode and rebuilds the manager light list only when it changes
        public void SetDynamic(bool isDynamic) {
            if (IsDynamic == isDynamic) return;
            IsDynamic = isDynamic;
            NotifyManager(true, false, false);
        }

        // Sets runtime registry weight and reorders this point light volume in the manager registry
        public void SetWeight(float weight) {
            if (RegistryWeight == weight) return;
            RegistryWeight = weight;
            if (LightVolumeManager == null) return;
            LightVolumeManager.ReorderPointLightVolume(this);
        }

        // Sets light source size or range data for LUT mode
        public void SetLightSourceSize(float size) {
            float safeSize = Mathf.Max(Mathf.Abs(size), 0.0001f);
            float inverseSquaredRange = 1f / (safeSize * safeSize);
            if (LightSourceSize == safeSize && InverseSquaredRange == inverseSquaredRange) return;
            LightSourceSize = safeSize;
            InverseSquaredRange = inverseSquaredRange;
            MarkRangeDirtyAndNotify(false, false, false);
        }

        // Sets LUT mode
        public void SetLut() {
            ProjectionMode = 1; // 1: LUT
            if (LightType == 1) OuterAngleTan = Mathf.Tan(Angle); // 1: spot
            OuterAngleCos = Mathf.Cos(Angle);
            UpdateRotation();
            MarkRangeDirtyAndNotify(true, CustomTexture != null || CustomTextureMaterial != null, false);
        }

        // Sets cubemap or cookie projection mode
        public void SetCustomTexture() {
            SetCustomProjectionMode();
            MarkRangeDirtyAndNotify(true, CustomTexture != null || CustomTextureMaterial != null, false);
        }

        // Sets a texture source for this light's custom projection and schedules manager runtime texture cache refresh
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
            MarkRangeDirtyAndNotify(true, true, false);
        }

        // Sets a material source for this light's custom projection and schedules manager runtime texture cache refresh
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
            MarkRangeDirtyAndNotify(true, true, false);
        }

        // Sets the light into parametric mode
        public void SetParametric() {
            if (ProjectionMode == 0) return;
            SetParametricMode();
            MarkRangeDirtyAndNotify(true, CustomTexture != null || CustomTextureMaterial != null, false);
        }

        // Sets the light into the point light type
        public void SetPointLight() {
            Vector3 position = transform.position;
            if (LightType == 0 && Position == position) return;
            LightType = 0; // 0: point
            Position = position;
            UpdateRotation();
            MarkRangeDirtyAndNotify(false, false, false);
        }

        // Sets the light into the spotlight type with both angle and falloff because angle is required to determine falloff
        public void SetSpotLight(float angleDeg, float falloff) {
            float angle = angleDeg * Mathf.Deg2Rad * 0.5f;
            float outerAngleTan = Mathf.Tan(angle);
            float outerAngleCos = Mathf.Cos(angle);
            float coneFalloff = 1f / (Mathf.Cos(angle * (1.0f - Mathf.Clamp01(falloff))) - outerAngleCos);
            Vector3 position = transform.position;
            Vector3 direction = transform.forward;
            Quaternion rotation = Quaternion.Inverse(transform.rotation);
            if (LightType == 1 && Angle == angle && OuterAngleTan == outerAngleTan && Position == position && (ProjectionMode == 2 ? Rotation == rotation : Direction == direction && OuterAngleCos == outerAngleCos && ConeFalloff == coneFalloff)) return;
            LightType = 1; // 1: spot
            Angle = angle;
            OuterAngleTan = outerAngleTan;
            if (ProjectionMode != 2) { // 2: custom cookie or cubemap
                OuterAngleCos = outerAngleCos;
                ConeFalloff = coneFalloff;
            }
            Position = position;
            UpdateRotation();
            MarkRangeDirtyAndNotify(false, false, false);
        }

        // Sets the light into the spotlight type with a specified angle
        public void SetSpotLight(float angleDeg) {
            float angle = angleDeg * Mathf.Deg2Rad * 0.5f;
            float outerAngleTan = Mathf.Tan(angle);
            float outerAngleCos = Mathf.Cos(angle);
            Vector3 position = transform.position;
            Vector3 direction = transform.forward;
            Quaternion rotation = Quaternion.Inverse(transform.rotation);
            if (LightType == 1 && Angle == angle && OuterAngleTan == outerAngleTan && Position == position && (ProjectionMode == 2 ? Rotation == rotation : Direction == direction && OuterAngleCos == outerAngleCos)) return;
            LightType = 1; // 1: spot
            Angle = angle;
            OuterAngleTan = outerAngleTan;
            if (ProjectionMode != 2) { // 2: custom cookie or cubemap
                OuterAngleCos = outerAngleCos;
            }
            Position = position;
            UpdateRotation();
            MarkRangeDirtyAndNotify(false, false, false);
        }
        
        // Sets the light into the area light type
        public void SetAreaLight() {
            LightType = 2; // 2: area
            Position = transform.position;
            Width = Mathf.Max(Mathf.Abs(transform.lossyScale.x), 0.001f);
            Height = Mathf.Max(Mathf.Abs(transform.lossyScale.y), 0.001f);
            UpdateRotation();
            MarkRangeDirtyAndNotify(true, CustomTexture != null || CustomTextureMaterial != null, false);
        }

        // Sets light source color
        public void SetColor(Color color) {
            if (Color == color) return;
            Color = color;
            _old_Color = color;
            MarkRangeDirtyAndNotify(false, false, false);
        }

        // Sets light source intensity
        public void SetIntensity(float intensity) {
            if (Intensity == intensity) return;
            Intensity = intensity;
            _old_Intensity = intensity;
            MarkRangeDirtyAndNotify(false, false, false);
        }

        // Sets Normal Masking and shadow strength
        public void SetShadingStrength(float shadingStrength) {
            float strength = Mathf.Clamp01(shadingStrength);
            if (ShadingStrength == strength) return;
            float oldStrength = ShadingStrength;
            ShadingStrength = strength;
            _old_ShadingStrength = strength;
            NotifyManager((Mathf.Clamp01(oldStrength) <= 0) != (strength <= 0), false, false);
        }

        // Sets custom spotlight cookie projection aspect
        public void SetSpotCookieAspect(float aspect) {
            float safeAspect = Mathf.Max(Mathf.Abs(aspect), 0.001f);
            if (SpotCookieAspect == safeAspect) return;
            SpotCookieAspect = safeAspect;
            NotifyManager(false, false, false);
        }

        // Sets shadow bake and projection parameters without rebuilding unrelated light data
        public void SetShadowSettings(float shadowMapID, bool worldSpaceShadows, int layerMask, float nearClip, float farClip, float bias, float blur, float contactHardening) {
            float safeNearClip = Mathf.Max(nearClip, 0.0001f);
            float safeFarClip = farClip > 0f ? Mathf.Max(farClip, safeNearClip + 0.0001f) : 0f;
            float safeBias = Mathf.Max(bias, 0f);
            float safeBlur = Mathf.Max(blur, 0f);
            float safeContactHardening = Mathf.Clamp01(contactHardening);
            bool shaderDataChanged = ShadowMapID != shadowMapID || WorldSpaceShadows != worldSpaceShadows || NearClip != safeNearClip || FarClip != safeFarClip;
            if (!shaderDataChanged && LayerMask == layerMask && Bias == safeBias && Blur == safeBlur && ContactHardening == safeContactHardening) return;
            ShadowMapID = shadowMapID;
            WorldSpaceShadows = worldSpaceShadows;
            LayerMask = layerMask;
            NearClip = safeNearClip;
            FarClip = safeFarClip;
            Bias = safeBias;
            Blur = safeBlur;
            ContactHardening = safeContactHardening;
            if (shaderDataChanged) NotifyManager(false, false, false);
        }

        // Marks this light range dirty and tells the manager which runtime data needs rebuilding.
        private void MarkRangeDirtyAndNotify(bool rebuildFinalData, bool customTexturesChanged, bool shadowTexturesChanged) {
            IsRangeDirty = true;
            NotifyManager(rebuildFinalData, customTexturesChanged, shadowTexturesChanged);
        }

        // Applies the internal custom projection mode without touching texture source fields
        private void SetCustomProjectionMode() {
            ProjectionMode = 2; // 2: custom cookie or cubemap
            if (LightType == 1) OuterAngleTan = Mathf.Tan(Angle); // 1: spot
            UpdateRotation();
        }

        // Applies the internal parametric projection mode without touching texture source fields
        private void SetParametricMode() {
            ProjectionMode = 0; // 0: parametric
            if (LightType == 1) OuterAngleTan = Mathf.Tan(Angle); // 1: spot
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
            NotifyManager(false, false, false);
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
            NotifyManager(false, false, false);
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
            MarkRangeDirtyAndNotify(false, false, false);
        }

    }

}
