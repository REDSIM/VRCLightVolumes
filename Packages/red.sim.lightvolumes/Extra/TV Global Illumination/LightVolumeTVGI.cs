#if !UDONSHARP && COMPILER_UDONSHARP
#define UDONSHARP
#endif

using UnityEngine;

#if UDONSHARP
using VRC.SDKBase;
using UdonSharp;
using VRC.SDK3.Rendering;
using VRC.Udon.Common.Interfaces;
#endif

namespace VRCLightVolumes {
#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LightVolumeTVGI : UdonSharpBehaviour
#else
    public class LightVolumeTVGI : MonoBehaviour
#endif
    {
        [Tooltip("Assign your video player texture or a static image. Its average color tints the target lights.")]
        public Texture TargetRenderTexture;
        [Tooltip("Smooths changes in the video color to reduce flicker.")]
        public bool AntiFlickering = true;
        [Space]
        [Tooltip("Baked volumes to tint with the video color.")]
        public LightVolumeInstance[] TargetLightVolumes;
        [Tooltip("Point, Spot and Area lights to tint with the video color.")]
        public PointLightVolumeInstance[] TargetPointLightVolumes;
        
#if UDONSHARP
        private Color32[] _pixels;
#endif
        private Color _prevColor;
        private float _timePrev;
        private RenderTexture _downsampledTex;
        private int _downsampledMipLevel;
        private bool _readbackPending;

        // Creates the mipmapped reduction texture used to estimate the video's average color.
        private void Start() {
            _timePrev = Time.time;
            _prevColor = Color.black;
            CreateDownsampledTexture();
#if UDONSHARP
            _pixels = new Color32[1];
#endif
        }

        // Creates and validates the owned reduction target. Assign ownership before configuration so every successfully constructed Unity object remains reachable by lifecycle cleanup.
        private void CreateDownsampledTexture() {
            if (_downsampledTex != null) return;
            _downsampledTex = new RenderTexture(64, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            _downsampledTex.useMipMap = true;
            _downsampledTex.autoGenerateMips = true;
            if (_downsampledTex.Create()) _downsampledMipLevel = _downsampledTex.mipmapCount - 1;
            else ReleaseDownsampledTexture();
        }

        // Releases the runtime reduction texture when this component is destroyed.
        private void OnDestroy() {
            _readbackPending = false;
            ReleaseDownsampledTexture();
        }

        // Releases the owned reduction target in both ordinary Unity and UdonSharp builds.
        private void ReleaseDownsampledTexture() {
            RenderTexture texture = _downsampledTex;
            _downsampledTex = null;
            if (texture == null) return;
#if COMPILER_UDONSHARP
            Destroy(texture);
#else
            if (RenderTexture.active == texture) RenderTexture.active = null;
            texture.Release();
            if (Application.isPlaying) Destroy(texture);
            else DestroyImmediate(texture);
#endif
        }

#if UDONSHARP
        // Blits the current video frame and requests its smallest mip through the VRChat readback API.
        void Update() {
            if (_readbackPending || TargetRenderTexture == null || _downsampledTex == null) return;
            VRCGraphics.Blit(TargetRenderTexture, _downsampledTex);
            _readbackPending = true;
            VRCAsyncGPUReadback.Request(_downsampledTex, _downsampledMipLevel, (IUdonEventReceiver)this);
        }

        // Receives the reduced video color from the VRChat GPU readback request.
        public override void OnAsyncGpuReadbackComplete(VRCAsyncGPUReadbackRequest request) {
            _readbackPending = false;
            if (_downsampledTex == null) return;
            if (request.TryGetData(_pixels)) {
                SetColor(_pixels[0]);
            }
        }

#else
        // Blits the current video frame and requests its smallest mip through Unity's readback API.
        void Update() {
            if (_readbackPending || TargetRenderTexture == null || _downsampledTex == null) return;
            Graphics.Blit(TargetRenderTexture, _downsampledTex);
            _readbackPending = true;
            UnityEngine.Rendering.AsyncGPUReadback.Request(_downsampledTex, _downsampledMipLevel, OnUnityAsyncGpuReadbackComplete);
        }

        // Receives the reduced video color from Unity's GPU readback request.
        private void OnUnityAsyncGpuReadbackComplete(UnityEngine.Rendering.AsyncGPUReadbackRequest request) {
            _readbackPending = false;
            if (_downsampledTex == null || request.hasError) return;
            Unity.Collections.NativeArray<Color32> pixels = request.GetData<Color32>();
            if (pixels.Length > 0) SetColor(pixels[0]);
        }
#endif

        // Smooths and applies the sampled video color to all configured light targets.
        private void SetColor(Color color) {

            // Measure the time since the last completed readback.
            float time = Time.time;
            float dTime = time - _timePrev;
            _timePrev = time;

            if (AntiFlickering) {
                float rmean = (color.r + _prevColor.r) * 0.5f;
                float r = color.r - _prevColor.r;
                float g = color.g - _prevColor.g;
                float b = color.b - _prevColor.b;
                float diff = Mathf.Sqrt((2f + rmean) * r * r + 4f * g * g + (3f - rmean) * b * b) / 3;
                float smoothing = dTime / Mathf.Lerp(0.25f, 1e-05f, Mathf.Pow(diff * 1.5f, 0.1f)); // Smoothing speed depends on the color difference
                _prevColor = Color.Lerp(_prevColor, color, smoothing);
            } else {
                _prevColor = color;
            }

            // Applying all colors
            Color targetColor = _prevColor;
            LightVolumeInstance[] targetLightVolumes = TargetLightVolumes;
            int lightVolumeCount = targetLightVolumes.Length;
            for (int i = 0; i < lightVolumeCount; i++) {
                targetLightVolumes[i].SetColor(targetColor);
            }

            PointLightVolumeInstance[] targetPointLightVolumes = TargetPointLightVolumes;
            int pointLightVolumeCount = targetPointLightVolumes.Length;
            for (int i = 0; i < pointLightVolumeCount; i++) {
                targetPointLightVolumes[i].SetColor(targetColor);
            }

        }
    }
}
