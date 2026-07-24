#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace VRCLightVolumes {
    // Transitional 2.x/early-3.x migration payload. The original script GUID is intentionally
    // retained so Unity can deserialize legacy scene and prefab data before the migrator removes it.
    [AddComponentMenu("")]
    [Obsolete("Legacy authoring data. Use LightVolumeManager; this component is consumed by the 3.x migration.")]
    public sealed class LightVolumeSetup : MonoBehaviour {
        [SerializeField] public List<LightVolume> LightVolumes = new List<LightVolume>();
        [SerializeField] public List<float> LightVolumesWeights = new List<float>();
        [SerializeField] public List<PointLightVolume> PointLightVolumes = new List<PointLightVolume>();

        [FormerlySerializedAs("Resolution")]
        public TextureArrayResolution CookieResolution = TextureArrayResolution._512x512;
        [FormerlySerializedAs("LightsBrightnessCutoff")]
        public float BrightnessCutoff = 0.35f;
        public TextureArrayResolution ShadowResolution = TextureArrayResolution._256x256;
        public ShadowTexturePrecision ShadowTextureFormat = ShadowTexturePrecision.Float;
        public float ShadowBleedReduction = 0.2f;
        public float ShadowMinVariance;
        public float ShadowMinVarianceMobile = 1f;

        public bool Clustering;
        public float FroxelDensity = 0.25f;
        public int FroxelSlices = 32;
        public int FroxelCoarse = 4;
        public int ClusteringMinLights = 16;

#if BAKERY_INCLUDED
        public Baking BakingMode = Baking.Bakery;
#else
        public Baking BakingMode = Baking.Progressive;
#endif
        [FormerlySerializedAs("BakeryBitmask")]
        public int VolumeBitmask = 1;
        public int ProbeBitmask = 1;
        public bool Denoise = true;
        public bool DilateInvalidProbes = true;
        public int DilationIterations = 1;
        public float DilationBackfaceBias = 0.1f;
        public bool FixLightProbesL1 = true;
        public Downscale DownscaleVolumes = Downscale.None;

        public bool LightProbesBlending = true;
        public bool SharpBounds = true;
        public bool AutoUpdateVolumes = true;
        public bool AutoUpdateTextures = true;
        public int AdditiveMaxOverdraw = 4;
        public bool ForceSceneLighting;
        public bool DestroyInPlayMode;

        [SerializeField] public List<LightVolumeData> LightVolumeDataList = new List<LightVolumeData>();

        [Serializable]
        public struct PostProcessor {
            public RenderTexture RT;
            public Material Mat;
            public string TextureName;
            public Action Update;
            public Action<Texture> UpdateWithInput;
        }

        public PostProcessor[] AtlasPostProcessors;
        public LightVolumeManager LightVolumeManager;
        public Baking _bakingModePrev;


        public enum Baking {
            Progressive,
            Bakery
        }

        public enum TextureArrayResolution {
            _16x16 = 16,
            _32x32 = 32,
            _64x64 = 64,
            _128x128 = 128,
            _256x256 = 256,
            _512x512 = 512,
            _1024x1024 = 1024,
            _2048x2048 = 2048
        }

        public enum ShadowTexturePrecision {
            Half = 0,
            Float = 1
        }

        public enum Downscale {
            None = 0,
            x2 = 1,
            x4 = 2,
            x8 = 3
        }
    }

    [Serializable]
    public struct LightVolumeData {
        public float Weight;
        public LightVolumeInstance LightVolumeInstance;


    }
}
#endif
