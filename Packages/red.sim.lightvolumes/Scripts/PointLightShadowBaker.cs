#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCLightVolumes {

    public static class PointLightShadowBaker {
        private struct RendererState {
            public Renderer Renderer;
            public bool PreviousForceRenderingOff;
        }

        private const string ShadowDepthEncodeShaderName = "Hidden/VRCLV/PointLightShadowDepthEncode";
        private const string ShadowBlurShaderName = "Hidden/VRCLV/PointLightShadowRuntimeBlur";
        private const string EditorShadowBlurQualityKeyword = "VRCLV_EDITOR_SHADOW_BLUR_QUALITY";
        private const int ShadowTextureFormatHalf = 0;
        private const int EditorShadowBlurSamplePreset = 3;

        private static readonly int[] _oppositeCubemapFaceIndices = { 1, 0, 3, 2, 5, 4 };
        private static readonly bool[] _arrayToCubemapHorizontalFlip = { false, false, true, true, false, false };

        // Bakes this light through PointLightVolumeInstance, saves the result as an asset and assigns it back to the authoring light.
        public static bool BakeShadowMap(PointLightVolume pointLightVolume, string infoString, bool regenerateArray) {
            if (pointLightVolume == null) return false;

            pointLightVolume.SetupDependencies();
            pointLightVolume.SyncUdonScript(false);

            PointLightVolumeInstance pointLightInstance = pointLightVolume.PointLightVolumeInstance;
            LightVolumeSetup lightVolumeSetup = pointLightVolume.LightVolumeSetup;
            LightVolumeManager manager = lightVolumeSetup != null ? lightVolumeSetup.LightVolumeManager : null;
            if (pointLightInstance == null || manager == null) {
                Debug.LogError("[PointLightShadowBaker] Failed to resolve PointLightVolumeInstance or LightVolumeManager for editor shadow bake.", pointLightVolume);
                return false;
            }
            if (!EnsureRuntimeShadowBakeDependencies(manager, pointLightVolume)) return false;

            bool cubemapShadows = pointLightVolume.ShouldBakeCubemapShadows();
            int resolution = lightVolumeSetup != null ? Mathf.Max((int)lightVolumeSetup.ShadowResolution, 16) : 128;
            TextureFormat textureFormat = lightVolumeSetup != null ? lightVolumeSetup.GetShadowMapBakeFormat() : TextureFormat.RGBAFloat;
            TextureFormat safeTextureFormat = GetSafeShadowMomentFormat(textureFormat);
            float nearClip = pointLightVolume.GetShadowNearClip();
            float farClip = pointLightVolume.GetShadowFarClip();
            if (nearClip >= farClip) nearClip = farClip * 0.5f;

            int oldShadowTextureFormat = manager.ShadowTextureFormat;
            bool oldPointLightInstanceEnabled = pointLightInstance.enabled;
            RenderTexture oldActive = RenderTexture.active;
            RendererState[] objectMaskRendererStates = null;
            RenderTexture runtimeShadowTexture = null;
            bool baked = false;

            try {
                objectMaskRendererStates = ApplyObjectMaskFilter(pointLightVolume);

                manager.ShadowTextureFormat = GetShadowTextureFormatValue(textureFormat);
                ResetManagerRuntimeShadowBlurState(manager);

                pointLightInstance.enabled = true;
                pointLightInstance.LightVolumeManager = manager;
                pointLightInstance.RuntimeShadowCamera = manager.RuntimeShadowCamera;
                pointLightInstance.RuntimeShadowDepthEncodeMaterial = manager.RuntimeShadowDepthEncodeMaterial;
                pointLightInstance.RuntimeShadowBlurMaterial = manager.RuntimeShadowBlurMaterial;
                pointLightInstance.RuntimeShadowResolution = resolution;
                pointLightInstance.RuntimeShadowBlurSamplePreset = EditorShadowBlurSamplePreset;
                pointLightInstance.RuntimeShadowSphericalBlur = true;
                pointLightInstance.RuntimeShadowFacesPerFrame = 6;
                pointLightInstance.RuntimeShadowDirectOutput = false;
                pointLightInstance.ShadowMapTexture = null;
                pointLightInstance.ShadowMapMaterial = null;
                pointLightInstance.AutoUpdateShadowMap = false;
                pointLightInstance.ShadowMapID = -1f;
                pointLightInstance.ShadowMapTextureIsCubemap = false;
                pointLightInstance.ShadowMapTextureHasDepthSlices = false;
                pointLightInstance.ShadowMapUsesCubemap = cubemapShadows;
                pointLightInstance.LayerMask = pointLightVolume.LayerMask.value;
                pointLightInstance.NearClip = nearClip;
                pointLightInstance.FarClip = farClip;
                pointLightInstance.Bias = pointLightVolume.Bias;
                pointLightInstance.Blur = pointLightVolume.Blur;
                pointLightInstance.ContactHardening = pointLightVolume.ContactHardening;
                if (pointLightInstance.Intensity == 0f) pointLightInstance.Intensity = 1f;
                if (pointLightInstance.Color == Color.black) pointLightInstance.Color = Color.white;

                pointLightInstance.BakeShadows();
                runtimeShadowTexture = pointLightInstance.ShadowMapTexture as RenderTexture;
                int sliceCount = cubemapShadows ? 6 : 1;
                if (!IsRuntimeShadowTextureValid(runtimeShadowTexture, resolution, sliceCount)) {
                    Debug.LogError($"[PointLightShadowBaker] PointLightVolumeInstance did not produce a valid editor shadow output {infoString}.", pointLightVolume);
                    return false;
                }

                UnityEngine.Object shadowAsset = cubemapShadows ? CreateCubemapShadowAsset(runtimeShadowTexture, safeTextureFormat) : CreateSingleShadowAsset(runtimeShadowTexture, safeTextureFormat);
                if (shadowAsset == null) return false;

                SaveShadowAsset(pointLightVolume, shadowAsset);
                pointLightVolume.ShadowMap = shadowAsset;
                LVUtils.MarkDirty(pointLightVolume);
                LVUtils.MarkDirty(pointLightInstance);
                baked = true;
                return true;
            } finally {
                RestoreObjectMaskFilter(objectMaskRendererStates);
                manager.ShadowTextureFormat = oldShadowTextureFormat;
                ResetManagerRuntimeShadowBlurState(manager);
                pointLightVolume.SyncEditorChanges(false, true);
                pointLightInstance.enabled = oldPointLightInstanceEnabled;
                if (baked && regenerateArray && pointLightVolume.LightVolumeSetup != null) pointLightVolume.LightVolumeSetup.ReinitializeShadowTextures();
                ReleaseTemporaryRenderTexture(runtimeShadowTexture);
                RenderTexture.active = oldActive;
            }
        }

        // Ensures the manager has the shared camera and materials required by PointLightVolumeInstance.BakeShadows().
        private static bool EnsureRuntimeShadowBakeDependencies(LightVolumeManager manager, PointLightVolume pointLightVolume) {
            Shader shadowDepthEncodeShader = Shader.Find(ShadowDepthEncodeShaderName);
            if (shadowDepthEncodeShader == null) {
                Debug.LogError($"[PointLightShadowBaker] Failed to find shadow depth encode shader '{ShadowDepthEncodeShaderName}'.", pointLightVolume);
                return false;
            }

            manager.EnsureRuntimeShadowCamera();
            if (manager.RuntimeShadowDepthEncodeMaterial == null || manager.RuntimeShadowDepthEncodeMaterial.shader != shadowDepthEncodeShader) manager.RuntimeShadowDepthEncodeMaterial = new Material(shadowDepthEncodeShader) { hideFlags = HideFlags.HideAndDontSave };

            Shader shadowBlurShader = Shader.Find(ShadowBlurShaderName);
            if (shadowBlurShader == null) {
                if (pointLightVolume.Blur > 0.0001f) Debug.LogWarning($"[PointLightShadowBaker] Failed to find shadow blur shader '{ShadowBlurShaderName}'. Baking without blur.", pointLightVolume);
                manager.RuntimeShadowBlurMaterial = null;
            } else if (manager.RuntimeShadowBlurMaterial == null || manager.RuntimeShadowBlurMaterial.shader != shadowBlurShader) {
                manager.RuntimeShadowBlurMaterial = new Material(shadowBlurShader) { hideFlags = HideFlags.HideAndDontSave };
            }

            return manager.RuntimeShadowCamera != null && manager.RuntimeShadowDepthEncodeMaterial != null;
        }

        // Resets cached blur keyword state so the shared material is configured by the next bake.
        private static void ResetManagerRuntimeShadowBlurState(LightVolumeManager manager) {
            if (manager == null) return;
            manager.RuntimeShadowBlurQualityPreset = -1;
            manager.RuntimeShadowBlurUniformKeyword = -1;
            manager.RuntimeShadowBlurDirectKeyword = -1;
            manager.RuntimeShadowBlurSphericalKeyword = -1;
            if (manager.RuntimeShadowBlurMaterial != null) manager.RuntimeShadowBlurMaterial.DisableKeyword(EditorShadowBlurQualityKeyword);
        }

        // Checks that BakeShadows returned the local texture-array layout expected by the editor asset copy.
        private static bool IsRuntimeShadowTextureValid(RenderTexture texture, int resolution, int sliceCount) {
            return texture != null && texture.dimension == TextureDimension.Tex2DArray && texture.width == resolution && texture.height == resolution && texture.volumeDepth >= sliceCount;
        }

        // Creates a persistent cubemap asset from the runtime texture-array shadow output.
        private static Cubemap CreateCubemapShadowAsset(RenderTexture source, TextureFormat textureFormat) {
            Cubemap cubemap = new Cubemap(source.width, textureFormat, false) {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0
            };
            ReadRenderTextureToCubemap(source, cubemap, source.width, textureFormat);
            return cubemap;
        }

        // Creates a persistent single-slice texture asset from the runtime texture-array shadow output.
        private static Texture2D CreateSingleShadowAsset(RenderTexture source, TextureFormat textureFormat) {
            Texture2D texture = new Texture2D(source.width, source.height, textureFormat, false, true) {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0
            };
            ReadRenderTextureToTexture2D(source, texture);
            return texture;
        }

        // Saves the baked shadow asset into the scene-local VRCLightVolumes temp folder.
        private static void SaveShadowAsset(PointLightVolume pointLightVolume, UnityEngine.Object shadowAsset) {
            string scenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            string escapedName = LVUtils.EscapeFileName(pointLightVolume.gameObject.name);
            string path = $"{System.IO.Path.GetDirectoryName(scenePath)}/{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}/VRCLightVolumes/Temp/{escapedName}_shadows.asset";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null) AssetDatabase.DeleteAsset(path);
            LVUtils.SaveAsAsset(shadowAsset, path);
        }

        // Temporarily hides renderers outside the point light's object mask for this editor bake.
        private static RendererState[] ApplyObjectMaskFilter(PointLightVolume pointLightVolume) {
            if (!HasObjectMask(pointLightVolume)) return null;

            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            RendererState[] rendererStates = new RendererState[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) {
                Renderer renderer = renderers[i];
                rendererStates[i].Renderer = renderer;
                rendererStates[i].PreviousForceRenderingOff = renderer.forceRenderingOff;
                if (!IsRendererInsideObjectMask(renderer, pointLightVolume.ObjectMask)) renderer.forceRenderingOff = true;
            }
            return rendererStates;
        }

        // Restores renderer visibility changed by ApplyObjectMaskFilter().
        private static void RestoreObjectMaskFilter(RendererState[] rendererStates) {
            if (rendererStates == null) return;
            for (int i = 0; i < rendererStates.Length; i++) {
                Renderer renderer = rendererStates[i].Renderer;
                if (renderer != null) renderer.forceRenderingOff = rendererStates[i].PreviousForceRenderingOff;
            }
        }

        // Checks whether this point light has at least one valid object-mask root.
        private static bool HasObjectMask(PointLightVolume pointLightVolume) {
            if (pointLightVolume.ObjectMask == null) return false;
            for (int i = 0; i < pointLightVolume.ObjectMask.Length; i++) {
                if (pointLightVolume.ObjectMask[i] != null) return true;
            }
            return false;
        }

        // Checks whether a renderer belongs to one of the object-mask roots.
        private static bool IsRendererInsideObjectMask(Renderer renderer, GameObject[] objectMask) {
            if (renderer == null || objectMask == null) return false;
            Transform rendererTransform = renderer.transform;
            for (int i = 0; i < objectMask.Length; i++) {
                GameObject root = objectMask[i];
                if (root == null) continue;
                Transform rootTransform = root.transform;
                if (rendererTransform == rootTransform || rendererTransform.IsChildOf(rootTransform)) return true;
            }
            return false;
        }

        // Copies the runtime texture-array shadow output into a serializable cubemap asset.
        private static void ReadRenderTextureToCubemap(RenderTexture source, Cubemap destination, int resolution, TextureFormat textureFormat) {
            RenderTexture oldActive = RenderTexture.active;
            Texture2D temp = new Texture2D(resolution, resolution, textureFormat, false, true);
            RenderTexture sourceFace = CreateTemporaryFaceTexture(source, resolution);
            RenderTexture transformedFace = CreateTemporaryFaceTexture(source, resolution);
            try {
                for (int i = 0; i < 6; i++) {
                    int destinationFaceIndex = _oppositeCubemapFaceIndices[i];
                    bool horizontalFlip = _arrayToCubemapHorizontalFlip[i];
                    Vector2 scale = horizontalFlip ? new Vector2(-1f, 1f) : new Vector2(1f, -1f);
                    Vector2 offset = horizontalFlip ? new Vector2(1f, 0f) : new Vector2(0f, 1f);

                    Graphics.CopyTexture(source, i, 0, sourceFace, 0, 0);
                    Graphics.Blit(sourceFace, transformedFace, scale, offset);
                    RenderTexture.active = transformedFace;
                    temp.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                    temp.Apply(false);
                    destination.SetPixelData(temp.GetRawTextureData<byte>(), 0, (CubemapFace)destinationFaceIndex);
                }
                destination.Apply(false);
            } finally {
                RenderTexture.active = oldActive;
                ReleaseTemporaryRenderTexture(sourceFace);
                ReleaseTemporaryRenderTexture(transformedFace);
                UnityEngine.Object.DestroyImmediate(temp);
            }
        }

        // Creates a temporary 2D render target used while converting runtime array slices to cubemap faces.
        private static RenderTexture CreateTemporaryFaceTexture(RenderTexture source, int resolution) {
            RenderTexture texture = new RenderTexture(resolution, resolution, 0, source.format, RenderTextureReadWrite.Linear) {
                dimension = TextureDimension.Tex2D,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();
            return texture;
        }

        // Copies the first runtime texture-array slice into a serializable Texture2D asset.
        private static void ReadRenderTextureToTexture2D(RenderTexture source, Texture2D destination) {
            RenderTexture oldActive = RenderTexture.active;
            try {
                Graphics.SetRenderTarget(source, 0, CubemapFace.Unknown, 0);
                destination.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                destination.Apply(false);
            } finally {
                RenderTexture.active = oldActive;
            }
        }

        // Releases a temporary render texture used by editor shadow baking.
        private static void ReleaseTemporaryRenderTexture(RenderTexture texture) {
            if (texture == null || AssetDatabase.Contains(texture)) return;
            if (RenderTexture.active == texture) RenderTexture.active = null;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }

        // Resolves the persistent texture format used to store baked EVSM moments.
        private static TextureFormat GetSafeShadowMomentFormat(TextureFormat textureFormat) {
            return textureFormat == TextureFormat.RGBAHalf ? TextureFormat.RGBAHalf : TextureFormat.RGBAFloat;
        }

        // Resolves the manager texture format value used by PointLightVolumeInstance.BakeShadows().
        private static int GetShadowTextureFormatValue(TextureFormat textureFormat) {
            return textureFormat == TextureFormat.RGBAHalf ? ShadowTextureFormatHalf : 1;
        }
    }
}
#endif
