#if !UDONSHARP && COMPILER_UDONSHARP
#define UDONSHARP
#endif

using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
#else
using System.Reflection;
using VRCShader = UnityEngine.Shader;
#endif

namespace VRCLightVolumes {

#if UDONSHARP
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LightVolumeAudioLink : UdonSharpBehaviour
#else
    public class LightVolumeAudioLink : MonoBehaviour
#endif
    {
        [Tooltip("Assign the AudioLink component from your scene.")]
#if UDONSHARP
        public UdonSharpBehaviour AudioLink;
#else
        public MonoBehaviour AudioLink;
#endif
        [Tooltip("Choose an audio band or the overall Volume to drive the light.")]
        public AudioLinkBand AudioBand = AudioLinkBand.Bass;
        [Tooltip("How many samples to delay the response. 0 follows the latest audio. Volume ignores this setting.")]
        [Range(0, 127)] public int Delay = 0;
        [Tooltip("Softens rapid changes in brightness.")]
        public bool SmoothingEnabled = true;
        [Tooltip("Higher values give a slower, smoother response.")]
        [Range(0, 1)] public float Smoothing = 0.25f;

        [Tooltip("Inverts the band's brightness response before the Add and Multiply adjustments.")]
        public bool Invert = false;

        [Tooltip("Brightness added when the band is quiet. Raise it to keep some light between beats.")]
        public float MinimumAdd = 0f;
        [Tooltip("Brightness added when the band reaches its maximum.")]
        public float MaximumAdd = 0f;

        [Tooltip("Audio brightness multiplier when the band is quiet.")]
        public float MinimumMultiply = 1f;
        [Tooltip("Audio brightness multiplier when the band reaches its maximum.")]
        public float MaximumMultiply = 1f;

        [Space]
        [Tooltip("Auto uses the theme color for each audio band. Override Color uses your chosen color.")]
        public AudioLinkColor ColorMode = AudioLinkColor.Auto;

        [Tooltip("Makes theme colors fully bright and saturated before the audio response. Use to avoid applying their existing brightness animation twice.")]
        public bool NormalizeColors = true;

        [Tooltip("Color used in Override Color mode.")]
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;

        [Tooltip("Also changes the material base color, alongside its emission color.")]
        public bool SetBaseColor = false;
        [Tooltip("Brightness of the affected materials. Set light brightness on each light component.")]
        public float MaterialsIntensity = 2f;

        [Space]
        [Tooltip("Baked volumes that follow the audio.")]
        public LightVolumeInstance[] TargetLightVolumes;
        [Tooltip("Point, Spot and Area lights that follow the audio.")]
        public PointLightVolumeInstance[] TargetPointLightVolumes;
        [Tooltip("Renderers whose material colors follow the audio.")]
        public Renderer[] TargetMeshRenderers;

        // shader property IDs
        private int _colorID;
        private int _emissionColorID;

        private MaterialPropertyBlock _block;
        private float _prevData = 0f;
        private Color[] _audioData;

#if UDONSHARP
        private UdonSharpBehaviour _initializedAudioLink;
#else
        private MonoBehaviour _initializedAudioLink;
#endif

        private const int AudioLinkTextureWidth = 128;
        private const string EnableReadbackEvent = "EnableReadback";
        private const string AudioDataVariable = "audioData";

        // Initializes renderer state and enables AudioLink readback.
        private void Start() {
            _block = new MaterialPropertyBlock();
            _colorID = VRCShader.PropertyToID("_Color");
            _emissionColorID = VRCShader.PropertyToID("_EmissionColor");
            EnsureAudioLinkReady();
        }

        // Samples AudioLink and applies the resulting color and intensity to every configured target.
        private void Update() {
            if (!EnsureAudioLinkReady()) return;

            int band = (int)AudioBand;

            // choose color
            Color _color = Color.black;
            switch (ColorMode) {
                case AudioLinkColor.NoChange:
                    break;
                case AudioLinkColor.Auto:
                    // wrap this around because of the size mismatch between number of bands and number of colors
                    _color = NormalizeColor(ReadAudioLinkPixel(band % 4, 23));
                    break;
                case AudioLinkColor.OverrideColor:
                    _color = Color;
                    break;
                default:
                    _color = NormalizeColor(ReadAudioLinkPixel((int)ColorMode, 23));
                    break;
            }

            float alData = SampleALData(Delay, band);
            if (ColorMode == AudioLinkColor.NoChange) return;
            float alFactors = (Invert ? (1 - alData) : alData) * Mathf.Lerp(MinimumMultiply, MaximumMultiply, alData) + Mathf.Lerp(MinimumAdd, MaximumAdd, alData);
            Color lightColor = _color * alFactors;

            LightVolumeInstance[] targetLightVolumes = TargetLightVolumes;
            int _count = targetLightVolumes != null ? targetLightVolumes.Length : 0;
            for (int i = 0; i < _count; i++) {
                LightVolumeInstance targetLightVolume = targetLightVolumes[i];
                if (targetLightVolume != null) targetLightVolume.SetColor(lightColor);
            }

            PointLightVolumeInstance[] targetPointLightVolumes = TargetPointLightVolumes;
            _count = targetPointLightVolumes != null ? targetPointLightVolumes.Length : 0;
            for (int i = 0; i < _count; i++) {
                PointLightVolumeInstance targetPointLightVolume = targetPointLightVolumes[i];
                if (targetPointLightVolume != null) targetPointLightVolume.SetColor(lightColor);
            }

            Color materialColor = lightColor * MaterialsIntensity;
            Renderer[] targetMeshRenderers = TargetMeshRenderers;
            _count = targetMeshRenderers != null ? targetMeshRenderers.Length : 0;
            for (int i = 0; i < _count; i++) {
                Renderer targetRenderer = targetMeshRenderers[i];
                if (targetRenderer == null) continue;
                targetRenderer.GetPropertyBlock(_block, 0);

                _block.SetColor(_emissionColorID, materialColor);
                if (SetBaseColor) {
                    _block.SetColor(_colorID, materialColor);
                }

                targetRenderer.SetPropertyBlock(_block);
            }
        }

        // Keeps AudioLink optional at compile time while using the same public readback buffer in both Unity and Udon.
        private bool EnsureAudioLinkReady() {
            if (AudioLink == null) {
                _initializedAudioLink = null;
                _audioData = null;
                return false;
            }

            if (_initializedAudioLink == AudioLink) return _audioData != null;

            _initializedAudioLink = AudioLink;
#if UDONSHARP
            AudioLink.SendCustomEvent(EnableReadbackEvent);
            _audioData = (Color[])AudioLink.GetProgramVariable(AudioDataVariable);
#else
            try {
                System.Type audioLinkType = AudioLink.GetType();
                MethodInfo enableReadback = audioLinkType.GetMethod(EnableReadbackEvent, BindingFlags.Instance | BindingFlags.Public);
                FieldInfo audioData = audioLinkType.GetField(AudioDataVariable, BindingFlags.Instance | BindingFlags.Public);
                if (enableReadback == null || audioData == null) {
                    _audioData = null;
                    return false;
                }

                enableReadback.Invoke(AudioLink, null);
                _audioData = audioData.GetValue(AudioLink) as Color[];
            } catch {
                _audioData = null;
            }
#endif
            return _audioData != null;
        }

        private Color ReadAudioLinkPixel(int x, int y) {
            int index = y * AudioLinkTextureWidth + x;
            if (_audioData == null || index < 0 || index >= _audioData.Length) return Color.black;
            return _audioData[index];
        }

        // Removes the theme color's brightness animation before the band response. This avoids applying brightness changes twice with delayed or smoothed effects.
        private Color NormalizeColor(Color color) {
            if (NormalizeColors) {
                Color.RGBToHSV(color, out float h, out float s, out float v);
                return Color.HSVToRGB(h, 1f, 1f);
            } else {
                return color;
            }
        }

        // Samples the selected AudioLink band and optionally smooths abrupt changes.
        private float SampleALData(int delay, int band) {
            float alData = 0f;

            // sample from ALPASS_GENERALVU + (8, 0) to get volume (RMS Left) note that we don't get delay here.
            if (band == (int)AudioLinkBand.Volume) {
                alData = ReadAudioLinkPixel(8, 22).r;
            } else {
                // sample the audiolink band data from ALPASS_AUDIOLINK when delay is 0 or ALPASS_AUDIOLINKHISTORY when > 0
                alData = ReadAudioLinkPixel(delay, band).r;
            }

            if (!SmoothingEnabled) {
                _prevData = alData;
                return alData;
            }

            float diff = Mathf.Abs(Mathf.Abs(alData) - Mathf.Abs(_prevData));

            // Smoothing speed depends on the color difference
            float smoothing = Time.deltaTime / Mathf.Lerp(Mathf.Lerp(0.25f, 1f, Smoothing), Mathf.Lerp(1e-05f, 0.1f, Smoothing), Mathf.Pow(diff * 1.5f, 0.1f));

            // Smooth the sampled value.
            _prevData = Mathf.Lerp(_prevData, alData, smoothing);
            return _prevData;
        }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        // Keeps AudioLink GPU readback enabled after inspector changes.
        private void OnValidate() {
            _initializedAudioLink = null;
            _audioData = null;
            EnsureAudioLinkReady();
        }
#endif

    }

    public enum AudioLinkBand {
        Bass = 0,
        LowMid = 1,
        HighMid = 2,
        Treble = 3,
        Volume = 4
    }

    public enum AudioLinkColor {
        Auto = -1,
        ThemeColor0 = 0,
        ThemeColor1 = 1,
        ThemeColor2 = 2,
        ThemeColor3 = 3,
        OverrideColor = 4,
        NoChange = 5
    }

}
