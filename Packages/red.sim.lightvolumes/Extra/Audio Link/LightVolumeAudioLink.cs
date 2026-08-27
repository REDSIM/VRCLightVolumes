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
        [Tooltip("Reference to your Audio Link manager that should control Light Volumes")]
#if UDONSHARP
        public UdonSharpBehaviour AudioLink;
#else
        public MonoBehaviour AudioLink;
#endif
        [Tooltip("Defines which audio band will be used to control Light Volumes. Four bands available: Bass, Low Mid, High Mid, Treble")]
        public AudioLinkBand AudioBand = AudioLinkBand.Bass;
        [Tooltip("Defines how many samples back in history we're getting data from. Can be a value from 0 to 127. Zero means no delay at all")]
        [Range(0, 127)] public int Delay = 0;
        [Tooltip("Enables a smoothing algorithm that tries to smooth out flickering that can usually be a problem")]
        public bool SmoothingEnabled = true;
        [Tooltip("Value from 0 to 1 that defines how much smoothing should be applied. Zero usually applies just a little bit of smoothing. One smooths out almost all fast blinks and makes intensity changes very slow")]
        [Range(0, 1)] public float Smoothing = 0.25f;

        [Tooltip("Inverts Audio Link data to dim the color based on the band, instead of lighting it up.")]
        public bool Invert = false;

        [Tooltip("Value added to intensity at AudioLink minimum")]
        public float MinimumAdd = 0f;
        [Tooltip("Value added to intensity at AudioLink maximum")]
        public float MaximumAdd = 0f;

        [Tooltip("Value multiplied with intensity at AudioLink minimum")]
        public float MinimumMultiply = 1f;
        [Tooltip("Value multiplied with intensity at AudioLink maximum")]
        public float MaximumMultiply = 1f;

        [Space]
        [Tooltip("Auto uses Theme Colors 0, 1, 2, 3 for Bass, LowMid, HighMid, Treble. Override Color allows you to set the static color value")]
        public AudioLinkColor ColorMode = AudioLinkColor.Auto;

        [Tooltip("Makes color fully saturated and fully bright before applying Audio Link effect. AudioLink already affects auto theme colors at runtime for some reason, so it prevents doubling the animation, which is especially visible when using Delay")]
        public bool NormalizeColors = true;

        [Tooltip("Color that will be used when Override Color is enabled")]
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;

        [Tooltip("Enable to set the base color of the material to the light color")]
        public bool SetBaseColor = false;
        [Tooltip("Brightness multiplier of the materials that should change color based on AudioLink. Intensity for Light Volumes and Point Light Volumes should be setup in their components")]
        public float MaterialsIntensity = 2f;

        [Space]
        [Tooltip("List of the Light Volumes that should be affected by AudioLink")]
        public LightVolumeInstance[] TargetLightVolumes;
        [Tooltip("List of the Point Light Volumes that should be affected by AudioLink")]
        public PointLightVolumeInstance[] TargetPointLightVolumes;
        [Tooltip("List of the Mesh Renderers that have materials that should change color based on AudioLink")]
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
                if (targetLightVolumes[i] != null) targetLightVolumes[i].SetColor(lightColor);
            }

            PointLightVolumeInstance[] targetPointLightVolumes = TargetPointLightVolumes;
            _count = targetPointLightVolumes != null ? targetPointLightVolumes.Length : 0;
            for (int i = 0; i < _count; i++) {
                if (targetPointLightVolumes[i] != null) targetPointLightVolumes[i].SetColor(lightColor);
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

        // Gets color with max brightness and saturation. Applies on top of the color chord color because AL dims the brightness of this color by default, which makes no sense to use with smoothing, delayed effects, etc.
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

            // Actually smoothing the value
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
