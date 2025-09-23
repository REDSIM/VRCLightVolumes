using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
#else
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
#if AUDIOLINK
        [Tooltip("Reference to your Audio Link manager that should control Light Volumes")]
        public AudioLink.AudioLink AudioLink;
#endif
        [Tooltip("Defines which audio band will be used to control Light Volumes. Four bands available: Bass, Low Mid, High Mid, Treble")]
        public AudioLinkBand AudioBand = AudioLinkBand.Bass;
        [Tooltip("Defines how many samples back in history we're getting data from. Can be a value from 0 to 127. Zero means no delay at all")]
        [Range(0, 127)] public int Delay = 0;
        [Tooltip("Enables smoothing algorithm that tries to smooth out flickering that can usually be a problem")]
        public bool SmoothingEnabled = true;
        [Tooltip("Value from 0 to 1 that defines how much smoothing should be applied. Zero usually applies just a little bit of smoothing. One smoothes out almost all the fast blinks and makes intensity changing very slow")]
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
        [Tooltip("Color that will be used when Override Color is enabled")]
        [ColorUsage(showAlpha: false)] public Color Color = Color.white;

        [Tooltip("How much to normalize the saturation of the AudioLink theme color. 0 keeps original saturation, 1 makes it fully saturated.")]
        [Range(0, 1)] public float NormalizeALColorSaturation = 1f;
        [Tooltip("How much to normalize the brightness of the AudioLink theme color. 0 keeps original brightness, 1 makes it full brightness.")]
        [Range(0, 1)] public float NormalizeALColorBrightness = 1f;
        
        [Tooltip("Enable to set the base color of the material to the light color")]
        public bool SetBaseColor = false;
        [Tooltip("Brightness multiplier of the materials that should change color based on AudioLink. Intensity for Light Volumes and Point Light Volumes should be setup in their components")]
        public float MaterialsIntensity = 2f;

        [Space]
        [Tooltip("List of the Light Volumes that should be affected by AudioLink")]
        public LightVolumeInstance[] TargetLightVolumes;
        [Tooltip("List of the Point Light Volumes that should be affected by AudioLink")]
        public PointLightVolumeInstance[] TargetPointLightVolumes;
        [Tooltip("List of the Mesh Renderers that has materials that should change color based on AudioLink")]
        public Renderer[] TargetMeshRenderers;

#if AUDIOLINK

        // shader property IDs
        private int _colorID;
        private int _emissionColorID;

        private MaterialPropertyBlock _block;
        private float _prevData = 0f;

        private void InitIDs() {
            _colorID = VRCShader.PropertyToID("_Color");
            _emissionColorID = VRCShader.PropertyToID("_EmissionColor");
        }

        private void Start() {
            _block = new MaterialPropertyBlock();
            InitIDs();

            if (AudioLink != null) {
                AudioLink.EnableReadback();
            }
        }

        private void Update() {
            int band = (int)AudioBand;

            // choose color
            Color _color = Color.black;
            switch (ColorMode) {
                case AudioLinkColor.NoChange:
                    break;
                case AudioLinkColor.Auto:
                    // wrap this around because of the size mismatch between number
                    // of bands and number of colors
                    _color = NormalizeColor(AudioLink.GetDataAtPixel(band % 4, 23));
                    break;
                case AudioLinkColor.OverrideColor:
                    _color = Color;
                    break;
                default:
                    _color = NormalizeColor(AudioLink.GetDataAtPixel((int)ColorMode, 23));
                    break;
            }

            float alData = SampleALData(Delay, band);

            float alFactors = ApplyALFactors(alData);

            int _count = TargetLightVolumes.Length;
            for (int i = 0; i < _count; i++) {
                if (ColorMode != AudioLinkColor.NoChange) {
                    TargetLightVolumes[i].Color = _color * alFactors;
                }
            }

            _count = TargetPointLightVolumes.Length;
            for (int i = 0; i < _count; i++) {
                TargetPointLightVolumes[i].IsRangeDirty = true;

                if (ColorMode != AudioLinkColor.NoChange) {
                    TargetPointLightVolumes[i].Color = _color * alFactors;
                }
            }

            _count = TargetMeshRenderers.Length;
            for (int i = 0; i < _count; i++) {
                TargetMeshRenderers[i].GetPropertyBlock(_block, 0);

                Color color = _color * alFactors * MaterialsIntensity;

                if (ColorMode != AudioLinkColor.NoChange) {
                    _block.SetColor(_emissionColorID, color);
                    if (SetBaseColor) {
                        _block.SetColor(_colorID, color);
                    }
                }

                TargetMeshRenderers[i].SetPropertyBlock(_block);
            }
        }

        // Gets color with max brightness and saturation. Applies on top of the color chord color because AL dims the brightness of this color by dafault, which makes it no sense to use with smoothing, delayed effects, etc.
        private Color NormalizeColor(Color color) {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            s = Mathf.Lerp(s, 1f, NormalizeALColorSaturation);
            v = Mathf.Lerp(v, 1f, NormalizeALColorBrightness);
            return Color.HSVToRGB(h, s, v);
        }

        private float ApplyALFactors(float alData) {
            return (Invert ? (1 - alData) : alData) * Mathf.Lerp(MinimumMultiply, MaximumMultiply, alData) + Mathf.Lerp(MinimumAdd, MaximumAdd, alData);
        }

        private float SampleALData(int delay, int band) {
            float alData = 0f;

            // sample from ALPASS_GENERALVU + (8, 0) to get volume (RMS Left)
            // note that we don't get delay here.
            if (band == (int)AudioLinkBand.Volume) {
                alData = AudioLink.GetDataAtPixel(8, 22).x;
            } else {
                // sample the audiolink band data from ALPASS_AUDIOLINK
                // when delay is 0 or ALPASS_AUDIOLINKHISTORY when > 0
                alData = AudioLink.GetDataAtPixel(delay, band).x;
            }

            if (SmoothingEnabled) {
                float diff = Mathf.Abs(Mathf.Abs(alData) - Mathf.Abs(_prevData));

                // Smoothing speed depends on the color difference
                float smoothing = Time.deltaTime / Mathf.Lerp(Mathf.Lerp(0.25f, 1f, Smoothing), Mathf.Lerp(1e-05f, 0.1f, Smoothing), Mathf.Pow(diff * 1.5f, 0.1f));

                // Actually smoothing the value
                _prevData = Mathf.Lerp(_prevData, alData, smoothing);
            }
            return alData;
        }

        private float ColorDifference(Color colorA, Color colorB) {
            float rmean = (colorA.r + colorB.r) * 0.5f;
            float r = colorA.r - colorB.r;
            float g = colorA.g - colorB.g;
            float b = colorA.b - colorB.b;
            return Mathf.Sqrt((2f + rmean) * r * r + 4f * g * g + (3f - rmean) * b * b) / 3;
        }

        private void OnValidate() {
            if (AudioLink != null) {
                AudioLink.EnableReadback();
            }
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