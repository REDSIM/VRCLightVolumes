using UnityEngine;
using UnityEngine.Serialization;

#if UDONSHARP
using VRC.Udon;
#endif

namespace VRCLightVolumes {

    [ExecuteAlways]
    public class PointLightVolume : MonoBehaviour {

        [Tooltip("Defines whether this point light volume can be moved in runtime. Disabling this option slightly improves performance. Don't forget to enable \"Auto Update Volumes\" in your Light Volumes Setup to have this dynamic updates!")]
        public bool Dynamic = false;
        [Tooltip("Point light is the most performant type. For static lighting, it's recommended to bake regular additive light volumes instead.")]
        public LightType Type = LightType.PointLight;
        [Tooltip("Physical radius of the light source for Point and Spot Lights. Larger size emits more light without increasing overall intensity, increases calculated range, and broadens size-aware specular highlights in modern compatible shaders.")]
        [Min(0.0001f)] public float LightSourceSize = 0.25f;
        [Tooltip("Radius in meters beyond which light is culled. Fewer overlapping lights result in better performance.")]
        [Min(0.0001f)] public float Range = 10f;
        [Tooltip("Multiplies the point light volume’s color by this value.")]
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;
        [Tooltip("Brightness of the point light volume.")]
        public float Intensity = 1f;
        [Tooltip("Controls per-surface Point Light shading and shadow opacity based on surface normal. 0 disables this extra shading and shadows for this light; 1 applies them fully. Modern individual speculars use the same light mask.")]
        [Range(0, 1)] public float ShadingStrength = 1f;
        [Tooltip("Parametric uses settings to compute light falloff. LUT uses a texture: X - cone falloff, Y - attenuation (Y only for point lights). Cookie projects a texture for spot lights. Cubemap projects a cubemap for point lights.")]
        [FormerlySerializedAs("Shape")] public LightProjection Projection = LightProjection.Parametric;
        [Tooltip("Angle of a spotlight cone in degrees.")]
        [Range(0.1f, 360)] public float Angle = 60f;
        [Tooltip("Cone falloff.")]
        [Range(0.001f, 1)] public float Falloff = 1f;
        [Tooltip("X - cone falloff, Y - attenuation. No compression and RGBA Float or RGBA Half format is recommended.")]
        public UnityEngine.Object FalloffLUT = null;
        [Tooltip("Projects a texture for Spot Light cookies, or a textured emitter surface for Area Lights. Modern compatible shaders sample Area Light cookies directly and use their source size for softer speculars.")]
        public UnityEngine.Object Cookie = null;
        [Tooltip("Width / height aspect used by custom spotlight cookie projection. 1 keeps a square projector; values above 1 compress projected height.")]
        [Min(0.001f)] public float SpotCookieAspect = 1f;
        [Tooltip("Projects a cubemap for point lights.")]
        public UnityEngine.Object Cubemap = null;
#if UNITY_EDITOR
        [Tooltip("Bakes light from this Point Light Volume into light probes. Useful for static Point Light Volumes to make them affect objects with no Light Volumes support.")]
        public bool BakeIntoProbes = false;
#endif
        [Tooltip("Shows overdrawing range gizmo. Less point light volumes intersections - more performance!")]
        public bool DebugRange = false;
        [Space]
        [Tooltip("Enables baked shadow map rendering for this light. This shadows are baked, but can affect dynamic objects in runtime, like avatars. It's more performant not to use shadows.")]
        public bool Shadows = false;
        [Tooltip("Rebakes shadows for this point light automatically when you click \"Bake Shadows\" in Light Volume Setup. Alternatively, you can bake it manually pressing the \"Bake Shadows\" button here.")]
        public bool RebakeShadows = false;
        [Tooltip("Layers that can cast shadows.")]
        public LayerMask LayerMask = 270849;
        [Tooltip("If empty, all objects in the scene will cast shadows. If not empty, only children of the listed objects cast shadows during bake.")]
        public GameObject[] ObjectMask = new GameObject[0];
        [Tooltip("World-space bias in meters applied while baking this light's shadow map. Larger values reduce self-shadow artifacts, but can detach contact edges. Requires rebaking.")]
        [Min(0)] public float Bias = 0.1f;
        [Tooltip("Near clip plane used by the shadow bake camera. Higher values can clip nearby occluders.")]
        [Min(0.0001f)] public float NearPlane = 0.01f;
        [Tooltip("Shadow blur radius applied after baking, normalized to 128x128 shadow resolution. Editor baking uses spherical shadow-space blur to reduce visible cubemap and Spot Light projection seams. Runtime baking uses Planar Blur unless Spherical Blur is enabled on the runtime baker. 0 keeps the baked shadow map unblurred. Requires rebaking.")]
        [Min(0)] public float Blur = 1f;
        [Tooltip("Hardens shadows near the contact areas. Can produce artefacts, so use with caution. Requires rebaking. More performant when set to 0 in realtime mode. Runtime baker Spherical Blur also applies to contact hardening samples.")]
        [Range(0, 1)] public float ContactHardening = 0f;
        [Tooltip("Use it if you don't want to move baked shadows together with their light. Attaches shadows to the world space basically. Less optimized when turned on.")]
        public bool UseWorldSpace = false;
        [Tooltip("Forces spotlight shadows to bake and store as a cubemap even when the spot angle is below 180 degrees.")]
        public bool ForceCubemapShadows = false;

        // Generated shadow Texture2DArray, cubemap, RenderTexture, CustomRenderTexture or Material used by the shared shadow texture array.
        [HideInInspector] public UnityEngine.Object ShadowMap = null;

        public PointLightVolumeInstance PointLightVolumeInstance;
        public LightVolumeSetup LightVolumeSetup;
#if UDONSHARP
        // UdonBehaviour is a real udon VM script. We need it to change public variables in play mode
        private UdonBehaviour _pointLightVolumeBehaviour = null;
#endif

#if UNITY_EDITOR
        private UnityEngine.Object _shadowMapPrev = null;
        private bool _shadowsPrev = false;
        private bool _forceCubemapShadowsPrev = false;
        private UnityEngine.Object _projectionSourcePrev = null;
        private LightType _typePrev = LightType.PointLight;
        private LightProjection _projectionPrev = LightProjection.Parametric;
        private bool _dynamicPrev = false;
        private float _lightSourceSizePrev = 0.25f;
        private float _rangePrev = 10f;
        private Color _colorPrev = Color.white;
        private float _intensityPrev = 1f;
        private float _shadingStrengthPrev = 1f;
        private float _anglePrev = 60f;
        private float _falloffPrev = 1f;
        private float _spotCookieAspectPrev = 1f;
        private bool _useWorldSpacePrev = false;
        private int _layerMaskPrev = 270849;
        private float _nearPlanePrev = 0.01f;
        private float _biasPrev = 0.1f;
        private float _blurPrev = 1f;
        private float _contactHardeningPrev = 0f;

        // To check if object was edited this frame
        private Vector3 _prevPos = Vector3.zero;
        private Quaternion _prevRot = Quaternion.identity;
        private Vector3 _prevScl = Vector3.one;

        // Was it changed on Validate?
        private bool _isValidated = false;
#endif

        // Looks for LightVolumeSetup and LightVolumeInstance udon script and setups them if needed
        public void SetupDependencies() {
            if (PointLightVolumeInstance == null && !TryGetComponent(out PointLightVolumeInstance)) {
                PointLightVolumeInstance = gameObject.AddComponent<PointLightVolumeInstance>();
            }
#if UDONSHARP
            if (_pointLightVolumeBehaviour == null) {
                TryGetComponent(out _pointLightVolumeBehaviour);
            }
#endif
            if (LightVolumeSetup == null) {
                LightVolumeSetup = FindObjectOfType<LightVolumeSetup>();
                if (LightVolumeSetup == null) {
                    var go = new GameObject("Light Volume Manager");
                    LightVolumeSetup = go.AddComponent<LightVolumeSetup>();
                    LightVolumeSetup.SyncUdonScript();
                }
            }
        }

        // Returns currently used projection object depending on the light parameters
        public UnityEngine.Object GetProjectionSource() {
            if (Type == LightType.AreaLight) return Cookie;
            if (Projection == LightProjection.Parametric) return null;
            if (Projection == LightProjection.LUT) return FalloffLUT;
            if (Type == LightType.PointLight) return Cubemap;
            if (Type == LightType.SpotLight) return Cookie;
            return null;
        }

        // Returns the projection texture source that should be copied into the shared runtime texture array
        public Texture GetCustomTexture() {
            UnityEngine.Object source = GetProjectionSource();
            return source as Texture;
        }

        // Returns the projection material source when this light uses material rendering
        public Material GetCustomTextureMaterial() {
            return GetProjectionSource() as Material;
        }

        // Returns the projection source type stored by the runtime instance
        public int GetProjectionType() {
            UnityEngine.Object source = GetProjectionSource();
            if (!HasProjectionSource()) return 0; // 0: none
            if (source is Material) return 2; // 2: material
            if (source is Texture) return 1; // 1: texture
            return 0; // 0: none
        }

        // Returns true when the active projection source type is supported by this light type
        public bool HasProjectionSource() {
            UnityEngine.Object source = GetProjectionSource();
            if (source == null) return false;
            if (source is Material) return true;
            if (Type == LightType.AreaLight) return source is Texture;
            if (Projection == LightProjection.LUT) return source is Texture;
            if (Projection == LightProjection.Custom && Type == LightType.PointLight) return source is Texture;
            if (Projection == LightProjection.Custom && Type == LightType.SpotLight) return source is Texture;
            return false;
        }

        // Returns true when the active projection source needs a per-frame copy into the runtime texture array
        public bool ShouldAutoUpdateCustomTexture() {
            return IsAnimatedProjectionSource(GetProjectionSource());
        }

        // Checks if a projection source is runtime-rendered instead of a static imported texture
        private static bool IsAnimatedProjectionSource(UnityEngine.Object source) {
            return source is RenderTexture || source is Material;
        }

        // Returns true when this light needs six cubemap slots in the shared cookie texture array
        public bool UsesCubemapProjection() {
            return Type == LightType.PointLight && Projection == LightProjection.Custom && HasProjectionSource();
        }

        // Returns internal runtime projection mode. 0 = parametric, 1 = LUT, 2 = cookie or cubemap
        private int GetProjectionMode() {
            if (!HasProjectionSource()) return 0; // 0: parametric
            if (Type == LightType.AreaLight) return 2; // 2: custom cookie or cubemap
            if (Projection == LightProjection.LUT) return 1; // 1: LUT
            if (Projection == LightProjection.Custom) return 2; // 2: custom cookie or cubemap
            return 0; // 0: parametric
        }

        // Returns true when the assigned projection texture is a real cubemap
        private bool IsProjectionTextureCubemap() {
            return IsCubemapTexture(GetProjectionSource() as Texture);
        }

        // Returns true when the assigned projection texture has independent array slices
        private bool ProjectionTextureHasDepthSlices() {
            UnityEngine.Object source = GetProjectionSource();
            RenderTexture renderTexture = source as RenderTexture;
            if (renderTexture != null) return renderTexture.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray && renderTexture.volumeDepth > 1;
            return source is Texture2DArray;
        }

        // Returns the shadow texture source that should be copied into the shared runtime shadow texture array
        public Texture GetShadowMapTexture() {
            return ShadowMap as Texture;
        }

        // Returns the shadow material source when this light uses material rendering
        public Material GetShadowMapMaterial() {
            return ShadowMap as Material;
        }

        // Returns true when the assigned shadow source can be used by runtime shadows
        public bool HasShadowMapSource() {
            if (ShadowMap == null) return false;
            if (ShadowMap is Material) return true;
            return ShadowMap is Texture;
        }

        // Returns true when the assigned shadow texture is a real cubemap
        private bool IsShadowMapTextureCubemap() {
            return IsCubemapTexture(ShadowMap as Texture);
        }

        // Returns true when this light should bake a six-face cubemap shadow map
        public bool ShouldBakeCubemapShadows() {
            return Type != LightType.SpotLight || ForceCubemapShadows || Angle >= 180f;
        }

        // Returns true when the assigned shadow source occupies six cubemap slices in the runtime shadow array
        public bool UsesCubemapShadows() {
            if (IsShadowMapTextureCubemap() || ShadowMapTextureHasDepthSlices()) return true;
            return ShouldBakeCubemapShadows();
        }

        // Returns true when a texture should be unfolded as six cubemap faces
        private static bool IsCubemapTexture(Texture texture) {
            if (texture is Cubemap) return true;
            RenderTexture renderTexture = texture as RenderTexture;
            return renderTexture != null && renderTexture.dimension == UnityEngine.Rendering.TextureDimension.Cube;
        }

        // Returns true when the assigned shadow texture has independent cubemap face slices
        private bool ShadowMapTextureHasDepthSlices() {
            RenderTexture renderTexture = ShadowMap as RenderTexture;
            if (renderTexture != null) return renderTexture.dimension == UnityEngine.Rendering.TextureDimension.Tex2DArray && renderTexture.volumeDepth > 1;
            return ShadowMap is Texture2DArray;
        }

        // Returns true when the shadow source needs a per-frame copy into the runtime texture array
        private bool ShouldAutoUpdateShadowMap() {
            return Shadows && IsAnimatedShadowSource(ShadowMap);
        }

        // Checks if a shadow map object should be rendered every frame by LightVolumeManager
        public static bool IsAnimatedShadowSource(UnityEngine.Object source) {
            return source is RenderTexture || source is Material;
        }

        private void Update() {
            if (gameObject == null) return;
            SetupDependencies();
#if UNITY_EDITOR
            // Regenerate texture arrays after Undo/Redo or serialized changes outside the inspector path
            bool shadowTexturesChanged = HasEditorShadowTextureChanges();
            bool customTexturesChanged = HasEditorCustomTextureChanges();
            // Sync udon script
            if (_prevPos != transform.position || _prevRot != transform.rotation || _prevScl != transform.localScale) {
                _prevPos = transform.position;
                _prevRot = transform.rotation;
                _prevScl = transform.localScale;
                if (!Application.isPlaying) LightVolumeSetup.SyncUdonScript();
            }

            if (_isValidated || customTexturesChanged || shadowTexturesChanged) {
                SyncEditorChanges(customTexturesChanged, shadowTexturesChanged);
                if (customTexturesChanged && LightVolumeSetup != null) LightVolumeSetup.ReinitializeCustomTextures();
                if (shadowTexturesChanged && LightVolumeSetup != null) LightVolumeSetup.ReinitializeShadowTextures();
            }
#endif
        }

        // Syncs this authoring component into the runtime instance, optionally refreshing projection texture references
        public void SyncUdonScript(bool syncTextureSources = true) {
            if (gameObject == null) return;
            SetupDependencies();
#if UDONSHARP
            if (Application.isPlaying) {
                // To sync variables in play-mode, we need to do it directly to the UdonBehaviour
                _pointLightVolumeBehaviour.SetProgramVariable("IsDynamic", Dynamic);
                _pointLightVolumeBehaviour.SetProgramVariable("Color", Color);
                _pointLightVolumeBehaviour.SetProgramVariable("Intensity", Intensity);
                _pointLightVolumeBehaviour.SetProgramVariable("ShadingStrength", Mathf.Clamp01(ShadingStrength));
                _pointLightVolumeBehaviour.SetProgramVariable("SpotCookieAspect", Mathf.Max(Mathf.Abs(SpotCookieAspect), 0.001f));
                _pointLightVolumeBehaviour.SetProgramVariable("IsRangeDirty", true);
                _pointLightVolumeBehaviour.SetProgramVariable("ShadowMapID", (float)GetShadowRuntimeID());
                _pointLightVolumeBehaviour.SetProgramVariable("WorldSpaceShadows", UseWorldSpace);
                _pointLightVolumeBehaviour.SetProgramVariable("Bias", Bias);
                _pointLightVolumeBehaviour.SetProgramVariable("LayerMask", LayerMask.value);
                _pointLightVolumeBehaviour.SetProgramVariable("NearClip", GetShadowNearClip());
                _pointLightVolumeBehaviour.SetProgramVariable("Blur", Mathf.Max(Blur, 0));
                _pointLightVolumeBehaviour.SetProgramVariable("ContactHardening", Mathf.Clamp01(ContactHardening));
                _pointLightVolumeBehaviour.SetProgramVariable("ShadowBakePosition", PointLightVolumeInstance.ShadowBakePosition);
                _pointLightVolumeBehaviour.SetProgramVariable("ShadowBakeRotation", PointLightVolumeInstance.ShadowBakeRotation);
                if (syncTextureSources) SyncTextureSourcesToUdon();
                // Udon does not support parameterized methods, so the values are passed through temporary program variables
                // Set the parameters first, then execute a parameterless method
                bool hasProjectionSource = HasProjectionSource();
                if (Type == LightType.AreaLight) {
                    _pointLightVolumeBehaviour.SendCustomEvent("SetAreaLight");
                } else {
                    bool usesLut = Projection == LightProjection.LUT && hasProjectionSource;
                    bool usesCustom = Projection == LightProjection.Custom && hasProjectionSource;
                    _pointLightVolumeBehaviour.SetProgramVariable("__0_size__param", usesLut ? Range : LightSourceSize);
                    _pointLightVolumeBehaviour.SendCustomEvent("__0_SetLightSourceSize");
                    if (usesCustom) _pointLightVolumeBehaviour.SendCustomEvent("SetCustomTexture");
                    else if (usesLut) _pointLightVolumeBehaviour.SendCustomEvent("SetLut");
                    else _pointLightVolumeBehaviour.SendCustomEvent("SetParametric");
                    if (Type == LightType.PointLight) {
                        _pointLightVolumeBehaviour.SendCustomEvent("SetPointLight");
                    } else if (Type == LightType.SpotLight) {
                        _pointLightVolumeBehaviour.SetProgramVariable("__0_angleDeg__param", Angle);
                        _pointLightVolumeBehaviour.SetProgramVariable("__0_falloff__param", Falloff);
                        _pointLightVolumeBehaviour.SendCustomEvent("__0_SetSpotLight");
                    }
                }

            } else {
#endif
                PointLightVolumeInstance.LightVolumeManager = LightVolumeSetup.LightVolumeManager;

                PointLightVolumeInstance.IsDynamic = Dynamic;
                PointLightVolumeInstance.Color = Color;
                PointLightVolumeInstance.Intensity = Intensity;
                PointLightVolumeInstance.ShadingStrength = Mathf.Clamp01(ShadingStrength);
                PointLightVolumeInstance.SpotCookieAspect = Mathf.Max(Mathf.Abs(SpotCookieAspect), 0.001f);
                PointLightVolumeInstance.IsRangeDirty = true;
                PointLightVolumeInstance.ShadowMapID = GetShadowRuntimeID();
                PointLightVolumeInstance.WorldSpaceShadows = UseWorldSpace;
                PointLightVolumeInstance.Bias = Bias;
                PointLightVolumeInstance.LayerMask = LayerMask.value;
                PointLightVolumeInstance.NearClip = GetShadowNearClip();
                PointLightVolumeInstance.Blur = Mathf.Max(Blur, 0);
                PointLightVolumeInstance.ContactHardening = Mathf.Clamp01(ContactHardening);
                if (syncTextureSources) SyncTextureSourcesToInstance();

                bool hasProjectionSource = HasProjectionSource();
                if (Type == LightType.AreaLight) {
                    PointLightVolumeInstance.SetAreaLight();
                } else {
                    bool usesLut = Projection == LightProjection.LUT && hasProjectionSource;
                    bool usesCustom = Projection == LightProjection.Custom && hasProjectionSource;
                    PointLightVolumeInstance.SetLightSourceSize(usesLut ? Range : LightSourceSize);
                    if (usesCustom) PointLightVolumeInstance.SetCustomTexture();
                    else if (usesLut) PointLightVolumeInstance.SetLut();
                    else PointLightVolumeInstance.SetParametric();
                    if (Type == LightType.PointLight) PointLightVolumeInstance.SetPointLight();
                    else if (Type == LightType.SpotLight) PointLightVolumeInstance.SetSpotLight(Angle, Falloff);
                }

#if UNITY_EDITOR
                // Mark changes to ensure prefab modifications are recorded
                LVUtils.MarkDirty(PointLightVolumeInstance);
#endif

#if UDONSHARP
            }
#endif
        }

#if UNITY_EDITOR
        // Stores texture source state after editor code has already rebuilt the matching shared arrays.
        public void CacheEditorTextureSourceState(bool customTextures, bool shadowTextures) {
            if (shadowTextures) {
                _shadowMapPrev = ShadowMap;
                _shadowsPrev = Shadows;
                _forceCubemapShadowsPrev = ForceCubemapShadows;
            }
            if (customTextures) {
                _projectionSourcePrev = GetProjectionSource();
                _typePrev = Type;
                _projectionPrev = Projection;
            }
        }

        // Returns true when projection texture array metadata needs rebuilding.
        public bool HasEditorCustomTextureChanges() {
            return _projectionSourcePrev != GetProjectionSource() || _typePrev != Type || _projectionPrev != Projection;
        }

        // Returns true when shadow texture array metadata needs rebuilding.
        public bool HasEditorShadowTextureChanges() {
            return _shadowMapPrev != ShadowMap || _shadowsPrev != Shadows || _forceCubemapShadowsPrev != ForceCubemapShadows;
        }

        // Caches editor-facing fields used to detect granular inspector changes.
        private void CacheEditorState() {
            _dynamicPrev = Dynamic;
            _lightSourceSizePrev = LightSourceSize;
            _rangePrev = Range;
            _colorPrev = Color;
            _intensityPrev = Intensity;
            _shadingStrengthPrev = ShadingStrength;
            _anglePrev = Angle;
            _falloffPrev = Falloff;
            _spotCookieAspectPrev = SpotCookieAspect;
            _useWorldSpacePrev = UseWorldSpace;
            _layerMaskPrev = LayerMask.value;
            _nearPlanePrev = NearPlane;
            _biasPrev = Bias;
            _blurPrev = Blur;
            _contactHardeningPrev = ContactHardening;
            CacheEditorTextureSourceState(true, true);
        }

        // Applies only editor fields that actually changed to the runtime instance.
        public void SyncEditorChanges(bool customTexturesChanged, bool shadowTexturesChanged, bool recordUndo = false) {
            if (gameObject == null) return;
            SetupDependencies();
#if UDONSHARP
            if (Application.isPlaying) {
                SyncUdonScript(customTexturesChanged || shadowTexturesChanged);
                CacheEditorState();
                _isValidated = false;
                return;
            }
#endif
            PointLightVolumeInstance.LightVolumeManager = LightVolumeSetup.LightVolumeManager;

            bool dynamicChanged = _dynamicPrev != Dynamic;
            bool colorChanged = _colorPrev != Color;
            bool intensityChanged = _intensityPrev != Intensity;
            bool shadingStrengthChanged = _shadingStrengthPrev != ShadingStrength;
            bool spotCookieAspectChanged = _spotCookieAspectPrev != SpotCookieAspect;
            UnityEngine.Object projectionSource = GetProjectionSource();
            bool typeChanged = _typePrev != Type;
            bool projectionChanged = _projectionPrev != Projection;
            bool sourceChanged = _projectionSourcePrev != projectionSource;
            customTexturesChanged = customTexturesChanged || typeChanged || projectionChanged || sourceChanged;
            shadowTexturesChanged = shadowTexturesChanged || HasEditorShadowTextureChanges();
            bool sizeChanged = _lightSourceSizePrev != LightSourceSize || _rangePrev != Range;
            bool spotShapeChanged = _anglePrev != Angle || _falloffPrev != Falloff;
            bool shadowSettingsChanged = shadowTexturesChanged || _useWorldSpacePrev != UseWorldSpace || _layerMaskPrev != LayerMask.value || _nearPlanePrev != NearPlane || _biasPrev != Bias || _blurPrev != Blur || _contactHardeningPrev != ContactHardening;

            if (recordUndo && (dynamicChanged || colorChanged || intensityChanged || shadingStrengthChanged || spotCookieAspectChanged || customTexturesChanged || sizeChanged || spotShapeChanged || shadowSettingsChanged)) UnityEditor.Undo.RecordObject(PointLightVolumeInstance, "Sync Point Light Volume Instance");

            if (dynamicChanged) {
                PointLightVolumeInstance.SetDynamic(Dynamic);
                _dynamicPrev = Dynamic;
            }
            if (colorChanged) {
                PointLightVolumeInstance.SetColor(Color);
                _colorPrev = Color;
            }
            if (intensityChanged) {
                PointLightVolumeInstance.SetIntensity(Intensity);
                _intensityPrev = Intensity;
            }
            if (shadingStrengthChanged) {
                PointLightVolumeInstance.SetShadingStrength(ShadingStrength);
                _shadingStrengthPrev = ShadingStrength;
            }
            if (spotCookieAspectChanged) {
                PointLightVolumeInstance.SetSpotCookieAspect(SpotCookieAspect);
                _spotCookieAspectPrev = SpotCookieAspect;
            }

            if (customTexturesChanged) SyncTextureSourcesToInstance();

            bool hasProjectionSource = HasProjectionSource();
            if (customTexturesChanged || sizeChanged || spotShapeChanged) {
                if (Type == LightType.AreaLight) {
                    if (customTexturesChanged) PointLightVolumeInstance.SetAreaLight();
                } else {
                    bool usesLut = Projection == LightProjection.LUT && hasProjectionSource;
                    bool usesCustom = Projection == LightProjection.Custom && hasProjectionSource;
                    if (sizeChanged || customTexturesChanged) PointLightVolumeInstance.SetLightSourceSize(usesLut ? Range : LightSourceSize);
                    if (customTexturesChanged) {
                        if (usesCustom) PointLightVolumeInstance.SetCustomTexture();
                        else if (usesLut) PointLightVolumeInstance.SetLut();
                        else PointLightVolumeInstance.SetParametric();
                    }
                    if (typeChanged || spotShapeChanged || projectionChanged) {
                        if (Type == LightType.PointLight) PointLightVolumeInstance.SetPointLight();
                        else if (Type == LightType.SpotLight) PointLightVolumeInstance.SetSpotLight(Angle, Falloff);
                    }
                }
                _typePrev = Type;
                _projectionPrev = Projection;
                _projectionSourcePrev = projectionSource;
                _lightSourceSizePrev = LightSourceSize;
                _rangePrev = Range;
                _anglePrev = Angle;
                _falloffPrev = Falloff;
            }

            if (shadowSettingsChanged) {
                if (shadowTexturesChanged) SyncTextureSourcesToInstance();
                PointLightVolumeInstance.SetShadowSettings(GetShadowRuntimeID(), UseWorldSpace, LayerMask.value, GetShadowNearClip(), Bias, Blur, ContactHardening);
                _useWorldSpacePrev = UseWorldSpace;
                _layerMaskPrev = LayerMask.value;
                _nearPlanePrev = NearPlane;
                _biasPrev = Bias;
                _blurPrev = Blur;
                _contactHardeningPrev = ContactHardening;
                CacheEditorTextureSourceState(false, shadowTexturesChanged);
            }

            if (customTexturesChanged) CacheEditorTextureSourceState(true, false);
            _isValidated = false;
        }
#endif

#if UDONSHARP
        // Copies projection texture sources into the Udon behaviour proxy in play mode
        private void SyncTextureSourcesToUdon() {
            Texture customTexture = GetCustomTexture();
            Material customTextureMaterial = GetCustomTextureMaterial();
            int projectionType = GetProjectionType();
            int projectionMode = GetProjectionMode();
            bool customSourceChanged = CustomTextureSourceChanged(_pointLightVolumeBehaviour, customTexture, customTextureMaterial, projectionType, projectionMode);
            _pointLightVolumeBehaviour.SetProgramVariable("CustomTexture", customTexture);
            _pointLightVolumeBehaviour.SetProgramVariable("CustomTextureMaterial", customTextureMaterial);
            _pointLightVolumeBehaviour.SetProgramVariable("ProjectionType", projectionType);
            _pointLightVolumeBehaviour.SetProgramVariable("ProjectionMode", projectionMode);
            if (customSourceChanged) _pointLightVolumeBehaviour.SetProgramVariable("AutoUpdateCustomTexture", ShouldAutoUpdateCustomTexture());
            _pointLightVolumeBehaviour.SetProgramVariable("CustomTextureIsCubemap", IsProjectionTextureCubemap());
            _pointLightVolumeBehaviour.SetProgramVariable("CustomTextureHasDepthSlices", ProjectionTextureHasDepthSlices());
            _pointLightVolumeBehaviour.SetProgramVariable("ShadowMapTexture", GetShadowMapTexture());
            _pointLightVolumeBehaviour.SetProgramVariable("ShadowMapMaterial", ShadowMap as Material);
            _pointLightVolumeBehaviour.SetProgramVariable("AutoUpdateShadowMap", ShouldAutoUpdateShadowMap());
            _pointLightVolumeBehaviour.SetProgramVariable("ShadowMapTextureIsCubemap", IsShadowMapTextureCubemap());
            _pointLightVolumeBehaviour.SetProgramVariable("ShadowMapTextureHasDepthSlices", ShadowMapTextureHasDepthSlices());
            _pointLightVolumeBehaviour.SetProgramVariable("ShadowMapUsesCubemap", UsesCubemapShadows());
        }
#endif

        // Copies projection texture sources into the runtime point light instance
        private void SyncTextureSourcesToInstance() {
            Texture customTexture = GetCustomTexture();
            Material customTextureMaterial = GetCustomTextureMaterial();
            int projectionType = GetProjectionType();
            int projectionMode = GetProjectionMode();
            bool customSourceChanged = CustomTextureSourceChanged(customTexture, customTextureMaterial, projectionType, projectionMode);
            PointLightVolumeInstance.CustomTexture = customTexture;
            PointLightVolumeInstance.CustomTextureMaterial = customTextureMaterial;
            PointLightVolumeInstance.ProjectionType = projectionType;
            PointLightVolumeInstance.ProjectionMode = projectionMode;
            if (customSourceChanged) PointLightVolumeInstance.AutoUpdateCustomTexture = ShouldAutoUpdateCustomTexture();
            PointLightVolumeInstance.CustomTextureIsCubemap = IsProjectionTextureCubemap();
            PointLightVolumeInstance.CustomTextureHasDepthSlices = ProjectionTextureHasDepthSlices();
            Texture shadowMapTexture = GetShadowMapTexture();
            Material shadowMapMaterial = ShadowMap as Material;
            bool shadowSourceChanged = PointLightVolumeInstance.ShadowMapTexture != shadowMapTexture || PointLightVolumeInstance.ShadowMapMaterial != shadowMapMaterial;
            PointLightVolumeInstance.ShadowMapTexture = shadowMapTexture;
            PointLightVolumeInstance.ShadowMapMaterial = shadowMapMaterial;
            PointLightVolumeInstance.AutoUpdateShadowMap = ShouldAutoUpdateShadowMap();
            PointLightVolumeInstance.ShadowMapTextureIsCubemap = IsShadowMapTextureCubemap();
            PointLightVolumeInstance.ShadowMapTextureHasDepthSlices = ShadowMapTextureHasDepthSlices();
            PointLightVolumeInstance.ShadowMapUsesCubemap = UsesCubemapShadows();
            if (shadowSourceChanged) {
                PointLightVolumeInstance.ShadowBakePosition = transform.position;
                PointLightVolumeInstance.ShadowBakeRotation = transform.rotation;
            }
        }

        // Checks whether editor sync is assigning a new projection source that should reset the auto-update default.
        private bool CustomTextureSourceChanged(Texture customTexture, Material customTextureMaterial, int projectionType, int projectionMode) {
            return PointLightVolumeInstance == null || PointLightVolumeInstance.CustomTexture != customTexture || PointLightVolumeInstance.CustomTextureMaterial != customTextureMaterial || PointLightVolumeInstance.ProjectionType != projectionType || PointLightVolumeInstance.ProjectionMode != projectionMode;
        }

#if UDONSHARP
        // Checks whether Udon proxy sync is assigning a new projection source that should reset the auto-update default.
        private bool CustomTextureSourceChanged(UdonBehaviour behaviour, Texture customTexture, Material customTextureMaterial, int projectionType, int projectionMode) {
            if (behaviour == null) return true;
            Texture oldTexture = behaviour.GetProgramVariable("CustomTexture") as Texture;
            Material oldMaterial = behaviour.GetProgramVariable("CustomTextureMaterial") as Material;
            object currentProjectionType = behaviour.GetProgramVariable("ProjectionType");
            object currentProjectionMode = behaviour.GetProgramVariable("ProjectionMode");
            int oldProjectionType = currentProjectionType is int ? (int)currentProjectionType : -1;
            int oldProjectionMode = currentProjectionMode is int ? (int)currentProjectionMode : -1;
            return oldTexture != customTexture || oldMaterial != customTextureMaterial || oldProjectionType != projectionType || oldProjectionMode != projectionMode;
        }
#endif

        private void Reset() {
            SetupDependencies();
            SyncUdonScript();
            LightVolumeSetup.RefreshVolumesList();
            LightVolumeSetup.SyncUdonScript();
#if UNITY_EDITOR
            CacheEditorState();
#endif
        }

        private void OnEnable() {
            SetupDependencies();
            LightVolumeSetup.RefreshVolumesList();
            LightVolumeSetup.SyncUdonScript();
#if UNITY_EDITOR
            CacheEditorState();
#endif
        }

        private void OnDisable() {
            if (LightVolumeSetup != null) {
                LightVolumeSetup.RefreshVolumesList();
                LightVolumeSetup.SyncUdonScript();
            }
        }

        private void OnDestroy() {
            if (LightVolumeSetup != null) {
                FalloffLUT = null;
                Cookie = null;
                Cubemap = null;
                ShadowMap = null;
#if UNITY_EDITOR
                LightVolumeSetup.ReinitializeCustomTextures();
                LightVolumeSetup.ReinitializeShadowTextures();
#endif
                LightVolumeSetup.RefreshVolumesList();
                LightVolumeSetup.SyncUdonScript();
            }
        }

#if UNITY_EDITOR
        private void OnValidate() {
            _isValidated = true;
        }
#endif

        // Returns a valid shadow map ID or disables the shadow for runtime
        private int GetShadowRuntimeID() {
            return Shadows && HasShadowMapSource() ? 0 : -1;
        }

        // Returns the editor-only far clip used by the shadow map bake
        public float GetShadowFarClip() {
            float scale = GetAverageLossyScale();
            float cutoff = LightVolumeSetup != null ? LightVolumeSetup.BrightnessCutoff : 0.35f;
            if (Type == LightType.AreaLight) {
                Vector3 lossyScale = transform.lossyScale;
                float width = Mathf.Max(Mathf.Abs(lossyScale.x), 0.001f);
                float height = Mathf.Max(Mathf.Abs(lossyScale.y), 0.001f);
                return Mathf.Max(Mathf.Sqrt(ComputeAreaLightSquaredBoundingSphere(width, height, Color, Intensity * Mathf.PI, cutoff)), 0.0001f);
            }
            if (Projection == LightProjection.LUT && HasProjectionSource()) return Mathf.Max(Range * scale, 0.0001f);
            float size = Mathf.Max(LightSourceSize * scale, 0.0001f);
            return Mathf.Max(Mathf.Sqrt(ComputePointLightSquaredBoundingSphere(Color, Intensity, size, cutoff)), 0.0001f);
        }

        // Returns the editor-defined near clip used by the shadow map bake
        public float GetShadowNearClip() {
            return Mathf.Max(NearPlane, 0.0001f);
        }

        // Returns the same average lossy scale approximation used by PointLightVolumeInstance
        private float GetAverageLossyScale() {
            Vector3 scale = transform.lossyScale;
            return (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
        }

        // Computes the point light influence radius squared for the brightness cutoff
        private static float ComputePointLightSquaredBoundingSphere(Color color, float intensity, float size, float cutoff) {
            float l = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            return Mathf.Max(Mathf.PI * 2f * l * Mathf.Abs(intensity) / (cutoff * cutoff) - 1f, 0f) * size * size;
        }

        // Computes the area light influence radius squared for the brightness cutoff
        private static float ComputeAreaLightSquaredBoundingSphere(float width, float height, Color color, float intensity, float cutoff) {
            float l = Mathf.Max(color.r, Mathf.Max(color.g, color.b)) * Mathf.Abs(intensity);
            if (l <= 0.000001f) return 0f;
            float maxSolidAngle = Mathf.PI * 2f - 0.0001f;
            float minSolidAngle = cutoff / l;
            if (minSolidAngle >= maxSolidAngle) return 0f;
            minSolidAngle = Mathf.Max(minSolidAngle, 0.000001f);
            float a = width * height;
            float w2 = width * width;
            float h2 = height * height;
            float b = 0.25f * (w2 + h2);
            float t = Mathf.Tan(0.25f * minSolidAngle);
            float t2 = Mathf.Max(t * t, 0.000001f);
            float tb = t2 * b;
            float discriminant = Mathf.Sqrt(tb * tb + 4f * t2 * a * a);
            float d2 = (discriminant - tb) * 0.125f / t2;
            return Mathf.Max(d2, 0f);
        }

#if UNITY_EDITOR
        // Bakes or re-bakes the shadow map for this light
        public void BakeShadowMap() {
            BakeShadowMap("", true);
        }

        // Bakes or re-bakes the shadow map for this light
        public bool BakeShadowMap(string infoString, bool regenerateArray) {
            SetupDependencies();
            SyncUdonScript(false);
            float farClip = GetShadowFarClip();
            float nearClip = GetShadowNearClip();
            if (nearClip >= farClip) nearClip = farClip * 0.5f;
            int resolution = LightVolumeSetup != null ? (int)LightVolumeSetup.ShadowResolution : 128;
            TextureFormat format = LightVolumeSetup != null ? LightVolumeSetup.GetShadowMapBakeFormat() : TextureFormat.RGBAFloat;
            UnityEngine.Object shadowTexture = ShouldBakeCubemapShadows() ? (UnityEngine.Object)PointLightShadowBaker.BakeShadowMap(this, resolution, farClip, format, Blur, ContactHardening, infoString) : PointLightShadowBaker.BakeSingleShadowMap(this, resolution, farClip, format, Blur, ContactHardening, infoString);
            if (shadowTexture == null) return false;

            string scenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            string path = $"{System.IO.Path.GetDirectoryName(scenePath)}/{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}/VRCLightVolumes/Temp/{gameObject.name}_shadows.asset";
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null) {
                UnityEditor.AssetDatabase.DeleteAsset(path);
            }
            LVUtils.SaveAsAsset(shadowTexture, path);

            ShadowMap = shadowTexture;
            PointLightVolumeInstance.ShadowBakePosition = transform.position;
            PointLightVolumeInstance.ShadowBakeRotation = transform.rotation;
            PointLightVolumeInstance.FarClip = farClip;
            PointLightVolumeInstance.NearClip = nearClip;
            _shadowMapPrev = ShadowMap;
            LVUtils.MarkDirty(this);
            LVUtils.MarkDirty(PointLightVolumeInstance);

            if (regenerateArray && LightVolumeSetup != null) {
                LightVolumeSetup.ReinitializeShadowTextures();
            }
            SyncUdonScript();
            return true;
        }

#endif

        public enum LightProjection {
            Parametric,
            LUT,
            Custom
        }

        public enum LightType {
            PointLight,
            SpotLight,
            AreaLight,
        }

    }

}
