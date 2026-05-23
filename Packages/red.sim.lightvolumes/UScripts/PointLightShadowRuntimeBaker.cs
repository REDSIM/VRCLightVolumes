using UnityEngine;
using UnityEngine.Rendering;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
#endif

namespace VRCLightVolumes {
    [DisallowMultipleComponent]
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PointLightShadowRuntimeBaker : UdonSharpBehaviour
#else
    public class PointLightShadowRuntimeBaker : MonoBehaviour
#endif
    {
        [Tooltip("Point Light Volume instance that will receive the runtime-baked shadow cubemap.")]
        public PointLightVolumeInstance TargetPointLightVolume;
        [Tooltip("Camera used to render the shadow cubemap. Must be assigned in the inspector.")]
        public Camera ShadowCamera;
        [Tooltip("Replacement shader that writes radial depth. Use Hidden/VRCLV/PointLightShadow.")]
        public Shader ShadowShader;
        [Tooltip("Cubemap RenderTexture written by this baker. If empty, a runtime cubemap is created automatically.")]
        public RenderTexture ShadowMapTexture;
        [Tooltip("Bake once during Start.")]
        public bool BakeOnStart = true;
        [Tooltip("Bake every frame using a delayed Udon event loop.")]
        public bool Realtime = false;
        [Tooltip("Resolution used for a runtime-created shadow cubemap.")]
        [Min(16)] public int Resolution = 128;
        [Tooltip("Near clip distance forced on the shadow bake camera.")]
        [Min(0.0001f)] public float NearClip = 0.01f;
        [Tooltip("Far clip distance and empty-shadow depth value used by the bake camera.")]
        [Min(0.0001f)] public float FarClip = 16f;
        [Tooltip("Layer mask used by the bake camera.")]
        public LayerMask CullingMask = -1;

        private bool _shadowSourceInitialized = false;
        private bool _realtimeBakeScheduled = false;
        private bool _old_Realtime = false;
        private RenderTexture _createdShadowMapTexture;

        // Initializes camera defaults before deferred bake events run.
        private void Start() {
#if !COMPILER_UDONSHARP
            ResolveReferences();
#endif
            ConfigureCamera();
        }

        // Starts or restarts deferred baking after this behaviour or its GameObject becomes active.
        private void OnEnable() {
            _realtimeBakeScheduled = false;
            _shadowSourceInitialized = false;
#if UDONSHARP
            if (Realtime) ScheduleRealtimeBake();
            else if (BakeOnStart) SendCustomEventDelayedFrames(nameof(BakeShadows), 1);
#else
            if (BakeOnStart) TriggerBake();
#endif
        }

        // Invalidates queued realtime bake state when this behaviour stops receiving events.
        private void OnDisable() {
            _realtimeBakeScheduled = false;
        }

#if !UDONSHARP || UNITY_EDITOR
        // Applies editor-side realtime toggles like LightVolumeManager auto-update toggles.
        private void Update() {
            if (_old_Realtime != Realtime) {
                _old_Realtime = Realtime;
                if (Realtime) BakeShadows();
            }
        }
#endif

#if !COMPILER_UDONSHARP
        // Resolves editor defaults when the component is added.
        private void Reset() {
            ResolveReferences();
        }

        // Keeps editor defaults populated for serialized Udon fields.
        private void OnValidate() {
            ResolveReferences();
        }
#endif

        // Triggers one immediate shadow map bake and updates the target point light.
        public void BakeShadows() {
#if !COMPILER_UDONSHARP
            ResolveReferences();
#endif
            if (Realtime) ScheduleRealtimeBake();
            if (!EnsureBakeResources()) return;

            Vector3 bakePosition = TargetPointLightVolume.transform.position;
            Quaternion bakeRotation = TargetPointLightVolume.transform.rotation;

            ShadowCamera.transform.position = bakePosition;
            ShadowCamera.transform.rotation = Quaternion.identity;
            ConfigureCamera();

            if (!ShadowCamera.RenderToCubemap(ShadowMapTexture)) {
                return;
            }

            ApplyTargetShadowSource(bakePosition, bakeRotation);
            RefreshManagerShadowTexture();
        }

        // Internal delayed event loop used for realtime shadow baking.
        public void _RealtimeBakeLoop() {
            _realtimeBakeScheduled = false;
            if (!enabled || !gameObject.activeInHierarchy) return;
            if (!Realtime) return;
            BakeShadows();
            ScheduleRealtimeBake();
        }

#if !COMPILER_UDONSHARP
        // Resolves editor-only defaults without touching scene components at runtime.
        private void ResolveReferences() {
            if (ShadowShader == null) ShadowShader = Shader.Find("Hidden/VRCLV/PointLightShadow");
        }
#endif

        // Validates or creates resources required for a bake.
        private bool EnsureBakeResources() {
            if (TargetPointLightVolume == null || ShadowCamera == null || ShadowShader == null) {
                return false;
            }
            if (!TargetPointLightVolume.enabled || !TargetPointLightVolume.gameObject.activeInHierarchy || TargetPointLightVolume.Intensity == 0 || TargetPointLightVolume.Color == Color.black) {
                return false;
            }

            if (ShadowMapTexture == null) {
                int safeResolution = Mathf.Clamp(Resolution, 16, 2048);
                ShadowMapTexture = new RenderTexture(safeResolution, safeResolution, 24, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
                ShadowMapTexture.dimension = TextureDimension.Cube;
                ShadowMapTexture.useMipMap = false;
                ShadowMapTexture.autoGenerateMips = false;
                ShadowMapTexture.wrapMode = TextureWrapMode.Clamp;
                ShadowMapTexture.filterMode = FilterMode.Bilinear;
                ShadowMapTexture.anisoLevel = 0;
            }

            if (_createdShadowMapTexture != ShadowMapTexture) {
                ShadowMapTexture.Create();
                _createdShadowMapTexture = ShadowMapTexture;
            }
            return true;
        }

        // Configures the bake camera for replacement-shader cubemap rendering.
        private void ConfigureCamera() {
            if (ShadowCamera == null) return;

            float safeNearClip = Mathf.Max(NearClip, 0.0001f);
            float safeFarClip = Mathf.Max(FarClip, 0.0001f);
            if (safeNearClip >= safeFarClip) safeNearClip = safeFarClip * 0.5f;

            ShadowCamera.enabled = false;
            ShadowCamera.clearFlags = CameraClearFlags.SolidColor;
            ShadowCamera.backgroundColor = new Color(safeFarClip, safeFarClip, safeFarClip, safeFarClip);
            ShadowCamera.nearClipPlane = safeNearClip;
            ShadowCamera.farClipPlane = safeFarClip;
            ShadowCamera.fieldOfView = 90f;
            ShadowCamera.aspect = 1f;
            ShadowCamera.renderingPath = RenderingPath.Forward;
            ShadowCamera.allowHDR = true;
            ShadowCamera.allowMSAA = false;
            ShadowCamera.useOcclusionCulling = false;
            ShadowCamera.cullingMask = CullingMask.value;
            ShadowCamera.stereoTargetEye = StereoTargetEyeMask.None;
            if (ShadowShader != null) ShadowCamera.SetReplacementShader(ShadowShader, "RenderType");
        }

        // Assigns the rendered cubemap as the target point light's active shadow source.
        private void ApplyTargetShadowSource(Vector3 bakePosition, Quaternion bakeRotation) {
            if (TargetPointLightVolume == null) return;

            if (TargetPointLightVolume.ShadowMapID < 0 || TargetPointLightVolume.ShadowMapTexture != ShadowMapTexture || TargetPointLightVolume.ShadowMapMaterial != null || TargetPointLightVolume.AutoUpdateShadowMap != Realtime || !TargetPointLightVolume.ShadowMapTextureIsCubemap || TargetPointLightVolume.ShadowMapTextureHasDepthSlices) _shadowSourceInitialized = false;

            if (TargetPointLightVolume.ShadowMapID < 0) TargetPointLightVolume.ShadowMapID = 0;
            TargetPointLightVolume.ShadowMapTexture = ShadowMapTexture;
            TargetPointLightVolume.ShadowMapMaterial = null;
            TargetPointLightVolume.AutoUpdateShadowMap = Realtime;
            TargetPointLightVolume.ShadowMapTextureIsCubemap = true;
            TargetPointLightVolume.ShadowMapTextureHasDepthSlices = false;
            if (TargetPointLightVolume.ShadowBakePosition != bakePosition || TargetPointLightVolume.ShadowBakeRotation != bakeRotation) {
                TargetPointLightVolume.ShadowBakePosition = bakePosition;
                TargetPointLightVolume.ShadowBakeRotation = bakeRotation;
            }
        }

        // Registers the target's shadow source in the manager when IDs can change.
        private void RefreshManagerShadowTexture() {
            if (TargetPointLightVolume == null) return;
            LightVolumeManager lightVolumeManager = TargetPointLightVolume.LightVolumeManager;
            if (lightVolumeManager == null) return;

            if (_shadowSourceInitialized && Realtime) return;

            lightVolumeManager.InitializePointLightVolume(TargetPointLightVolume);
            lightVolumeManager.ReinitializeShadowTextures();
            _shadowSourceInitialized = true;
            lightVolumeManager.RequestUpdateVolumes();
        }

        // Schedules the next realtime bake if it is not already queued.
        private void ScheduleRealtimeBake() {
#if UDONSHARP
            if (_realtimeBakeScheduled || !Realtime || !enabled || !gameObject.activeInHierarchy) return;
            _realtimeBakeScheduled = true;
            SendCustomEventDelayedFrames(nameof(_RealtimeBakeLoop), 1);
#endif
        }
    }
}
