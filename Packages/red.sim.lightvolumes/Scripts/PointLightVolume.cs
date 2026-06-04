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
        [Tooltip("Point light is the most performant type. Area light is the heaviest and best suited for dynamic, movable sources. For static lighting, it's recommended to bake regular additive light volumes instead.")]
        public LightType Type = LightType.PointLight;
        [Tooltip("Physical radius of a light source if it was a matte glowing sphere for a point light, or a flashlight reflector for a spot light. Larger size emmits more light without increasing overall intensity.")]
        [Min(0.0001f)] public float LightSourceSize = 0.25f;
        [Tooltip("Radius in meters beyond which light is culled. Fewer overlapping lights result in better performance.")]
        [Min(0.0001f)] public float Range = 10f;
        [Tooltip("Multiplies the point light volume’s color by this value.")]
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;
        [Tooltip("Brightness of the point light volume.")]
        public float Intensity = 1f;
        [Tooltip("Parametric uses settings to compute light falloff. LUT uses a texture: X - cone falloff, Y - attenuation (Y only for point lights). Cookie projects a texture for spot lights. Cubemap projects a cubemap for point lights.")]
        [FormerlySerializedAs("Shape")] public LightProjection Projection = LightProjection.Parametric;
        [Tooltip("Angle of a spotlight cone in degrees.")]
        [Range(0.1f, 360)] public float Angle = 60f;
        [Tooltip("Cone falloff.")]
        [Range(0.001f, 1)] public float Falloff = 1f;
        [Tooltip("X - cone falloff, Y - attenuation. No compression and RGBA Float or RGBA Half format is recommended.")]
        public UnityEngine.Object FalloffLUT = null;
        [Tooltip("Projects a square texture for spot lights.")]
        public UnityEngine.Object Cookie = null;
        [Tooltip("Projects a cubemap for point lights.")]
        public UnityEngine.Object Cubemap = null;
        [Tooltip("Shows overdrawing range gizmo. Less point light volumes intersections - more performance!")]
        public bool DebugRange = false;
        [Space]
        [Tooltip("Enables baked shadow map rendering for this light. When disabled, an assigned or baked Shadow Map is kept but ignored at runtime.")]
        public bool Shadows = false;
        [Tooltip("Rebakes shadows for this point light automatically when you click \"Bake Shadows\" in Light Volume Setup. Alternatively, you can bake it manually pressing the \"Bake Shadows\" button here.")]
        public bool RebakeShadows = false;
        [Tooltip("Layer mask used by the shadow bake camera. Only these layers can write into the shadow depth pass.")]
        public LayerMask LayerMask = -1;
        [Tooltip("If empty, all objects in the scene are baked. If not empty, only children of the listed objects cast shadows during bake.")]
        public GameObject[] ObjectMask = new GameObject[0];
        [Tooltip("World-space bias in meters applied while baking this light's shadow map. Larger values reduce self-shadow artifacts, but can detach contact edges. Requires rebaking.")]
        [Min(0)] public float Bias = 0.1f;
        [Tooltip("Near clip plane used by the shadow bake camera. Higher values improve depth precision but clip nearby occluders.")]
        [Min(0.0001f)] public float NearPlane = 0.01f;
        [Tooltip("Editor-only Gaussian blur radius in shadow texels applied after baking. 0 keeps the baked shadow map unblurred.")]
        [Min(0)] public float Blur = 1f;
        [Tooltip("Hardens shadows near the contact areas. Can produce artefacts, so use with caution! Requires rebaking.")]
        [Range(0, 1)] public float ContactHardening = 0f;
        [Tooltip("Use it if you don't want to move baked shadows together with their light. Attaches shadows to the world space basically. Less optimized when turned on.")]
        public bool UseWorldSpace = false;

        [HideInInspector] public int ShadowID = -1;
        [HideInInspector] public UnityEngine.Object ShadowMap = null;

        public PointLightVolumeInstance PointLightVolumeInstance;
        public LightVolumeSetup LightVolumeSetup;
#if UDONSHARP
        // UdonBehaviour is a real udon VM script. We need it to change public variables in play mode
        private UdonBehaviour _pointLightVolumeBehaviour = null;
#endif

        private UnityEngine.Object _shadowMapPrev = null;
        private bool _shadowsPrev = false;

        // To check if object was edited this frame
        private Vector3 _prevPos = Vector3.zero;
        private Quaternion _prevRot = Quaternion.identity;
        private Vector3 _prevScl = Vector3.one;

        // Was it changed on Validate?
        private bool _isValidated = false;

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
            if (Projection == LightProjection.Parametric || Type == LightType.AreaLight) return null;
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
            // Regenerate Shadow texture array
            if (_shadowMapPrev != ShadowMap) {
                _shadowMapPrev = ShadowMap;
                LightVolumeSetup.ReinitializeShadowTextures();
            }
            if (_shadowsPrev != Shadows) {
                _shadowsPrev = Shadows;
                LightVolumeSetup.ReinitializeShadowTextures();
            }
            // Sync udon script
            if (_prevPos != transform.position || _prevRot != transform.rotation || _prevScl != transform.localScale) {
                _prevPos = transform.position;
                _prevRot = transform.rotation;
                _prevScl = transform.localScale;
                if (!Application.isPlaying) LightVolumeSetup.SyncUdonScript();
            }

            if (_isValidated) {
                _isValidated = false;
                SyncUdonScript(false);
            }
#endif
        }

        // Syncs all editable data into the runtime PointLightVolumeInstance
        public void SyncUdonScript() {
            SyncUdonScript(true);
        }

        // Syncs this authoring component into the runtime instance, optionally refreshing projection texture references
        private void SyncUdonScript(bool syncTextureSources) {
            if (gameObject == null) return;
            SetupDependencies();
#if UDONSHARP
            if (Application.isPlaying) {
                // To sync variables in play-mode, we need to do it directly to the UdonBehaviour
                _pointLightVolumeBehaviour.SetProgramVariable("IsDynamic", Dynamic);
                _pointLightVolumeBehaviour.SetProgramVariable("Color", Color);
                _pointLightVolumeBehaviour.SetProgramVariable("Intensity", Intensity);
                _pointLightVolumeBehaviour.SetProgramVariable("IsRangeDirty", true);
                _pointLightVolumeBehaviour.SetProgramVariable("ShadowMapID", (float)GetShadowRuntimeID());
                _pointLightVolumeBehaviour.SetProgramVariable("WorldSpaceShadows", UseWorldSpace);
                _pointLightVolumeBehaviour.SetProgramVariable("Bias", Bias);
                _pointLightVolumeBehaviour.SetProgramVariable("LayerMask", LayerMask.value);
                _pointLightVolumeBehaviour.SetProgramVariable("NearClip", GetShadowNearClip());
                _pointLightVolumeBehaviour.SetProgramVariable("Blur", Mathf.Max(Blur, 0));
                _pointLightVolumeBehaviour.SetProgramVariable("ContactHardening", Mathf.Clamp01(ContactHardening));
                _pointLightVolumeBehaviour.SetProgramVariable("ShadowBakePosition", PointLightVolumeInstance.ShadowBakePosition);
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

#if UDONSHARP
        // Copies projection texture sources into the Udon behaviour proxy in play mode
        private void SyncTextureSourcesToUdon() {
            _pointLightVolumeBehaviour.SetProgramVariable("CustomTexture", GetCustomTexture());
            _pointLightVolumeBehaviour.SetProgramVariable("CustomTextureMaterial", GetCustomTextureMaterial());
            _pointLightVolumeBehaviour.SetProgramVariable("ProjectionType", GetProjectionType());
            _pointLightVolumeBehaviour.SetProgramVariable("ProjectionMode", GetProjectionMode());
            _pointLightVolumeBehaviour.SetProgramVariable("AutoUpdateCustomTexture", ShouldAutoUpdateCustomTexture());
            _pointLightVolumeBehaviour.SetProgramVariable("CustomTextureIsCubemap", IsProjectionTextureCubemap());
            _pointLightVolumeBehaviour.SetProgramVariable("CustomTextureHasDepthSlices", ProjectionTextureHasDepthSlices());
            _pointLightVolumeBehaviour.SetProgramVariable("ShadowMapTexture", GetShadowMapTexture());
            _pointLightVolumeBehaviour.SetProgramVariable("ShadowMapMaterial", ShadowMap as Material);
            _pointLightVolumeBehaviour.SetProgramVariable("AutoUpdateShadowMap", ShouldAutoUpdateShadowMap());
            _pointLightVolumeBehaviour.SetProgramVariable("ShadowMapTextureIsCubemap", IsShadowMapTextureCubemap());
            _pointLightVolumeBehaviour.SetProgramVariable("ShadowMapTextureHasDepthSlices", ShadowMapTextureHasDepthSlices());
        }
#endif

        // Copies projection texture sources into the runtime point light instance
        private void SyncTextureSourcesToInstance() {
            PointLightVolumeInstance.CustomTexture = GetCustomTexture();
            PointLightVolumeInstance.CustomTextureMaterial = GetCustomTextureMaterial();
            PointLightVolumeInstance.ProjectionType = GetProjectionType();
            PointLightVolumeInstance.ProjectionMode = GetProjectionMode();
            PointLightVolumeInstance.AutoUpdateCustomTexture = ShouldAutoUpdateCustomTexture();
            PointLightVolumeInstance.CustomTextureIsCubemap = IsProjectionTextureCubemap();
            PointLightVolumeInstance.CustomTextureHasDepthSlices = ProjectionTextureHasDepthSlices();
            PointLightVolumeInstance.ShadowMapTexture = GetShadowMapTexture();
            PointLightVolumeInstance.ShadowMapMaterial = ShadowMap as Material;
            PointLightVolumeInstance.AutoUpdateShadowMap = ShouldAutoUpdateShadowMap();
            PointLightVolumeInstance.ShadowMapTextureIsCubemap = IsShadowMapTextureCubemap();
            PointLightVolumeInstance.ShadowMapTextureHasDepthSlices = ShadowMapTextureHasDepthSlices();
        }

        private void Reset() {
            SetupDependencies();
            SyncUdonScript();
            LightVolumeSetup.RefreshVolumesList();
            LightVolumeSetup.SyncUdonScript();
        }

        private void OnEnable() {
            SetupDependencies();
            LightVolumeSetup.RefreshVolumesList();
            LightVolumeSetup.SyncUdonScript();
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

        private void OnValidate() {
            _isValidated = true;
        }

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
        [ContextMenu("Bake Shadow Map")]
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
            Cubemap cubemap = PointLightShadowBaker.BakeShadowMap(this, resolution, farClip, format, Blur, ContactHardening, infoString);
            if (cubemap == null) return false;

            string scenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            string path = $"{System.IO.Path.GetDirectoryName(scenePath)}/{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}/VRCLightVolumes/Temp/{gameObject.name}_shadows.asset";
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null) {
                UnityEditor.AssetDatabase.DeleteAsset(path);
            }
            LVUtils.SaveAsAsset(cubemap, path);

            ShadowMap = cubemap;
            PointLightVolumeInstance.ShadowBakePosition = transform.position;
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
