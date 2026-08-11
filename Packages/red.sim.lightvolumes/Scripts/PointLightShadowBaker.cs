#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCLightVolumes {

    public static class PointLightShadowBaker {
        private const string ShadowDepthEncodeShaderName = "Hidden/VRCLV/PointLightShadowDepthEncode";
        private const string ShadowBlurShaderName = "Hidden/VRCLV/PointLightShadowRuntimeBlur";
        private const string EditorShadowBlurQualityKeyword = "VRCLV_EDITOR_SHADOW_BLUR_QUALITY";
        private const int ShadowTextureFormatHalf = 0;
        private const int EditorShadowBlurSamplePreset = 3;

        private static readonly int[] _oppositeCubemapFaceIndices = { 1, 0, 3, 2, 5, 4 };
        private static readonly bool[] _arrayToCubemapHorizontalFlip = { false, false, true, true, false, false };

        // Bakes this light through PointLightVolumeInstance, saves the result as an asset and assigns it back to the authoring light.
        public static bool BakeShadowMap(PointLightVolumeInstance pointLightVolume, string infoString, bool regenerateArray) {
            if (pointLightVolume == null) return false;

            PointLightVolumeInstance pointLightInstance = pointLightVolume;
            LightVolumeManager manager = pointLightVolume.LightVolumeManager;
            if (manager == null) {
                Debug.LogError("[LightVolumes] Point Light Volume has no Light Volume Manager.", pointLightVolume);
                return false;
            }

            bool customTexturesChanged = pointLightVolume.HasEditorCustomTextureChanges();
            bool shadowTexturesChanged = pointLightVolume.HasEditorShadowTextureChanges();
            pointLightVolume.EditorApplyAuthoringData(customTexturesChanged, shadowTexturesChanged, false);
            if (!EnsureRuntimeShadowBakeDependencies(manager, pointLightVolume)) return false;

            bool cubemapShadows = pointLightVolume.ShouldBakeCubemapShadows();
            int resolution = ResolveShadowBakeResolution(pointLightVolume, manager);
            TextureFormat textureFormat = GetManagerShadowMapBakeFormat(manager);
            TextureFormat safeTextureFormat = GetSafeShadowMomentFormat(textureFormat);

            int oldShadowTextureFormat = manager.ShadowTextureFormat;
            bool oldPointLightInstanceEnabled = pointLightInstance.enabled;
            int oldRuntimeShadowResolution = pointLightInstance.RuntimeShadowResolution;
            int oldRuntimeShadowBlurSamplePreset = pointLightInstance.RuntimeShadowBlurSamplePreset;
            int oldRuntimeShadowFacesPerFrame = pointLightInstance.RuntimeShadowFacesPerFrame;
            bool oldRuntimeShadowDirectOutput = pointLightInstance.RuntimeShadowDirectOutput;
            RenderTexture oldActive = RenderTexture.active;
            RenderTexture runtimeShadowTexture = null;
            bool baked = false;

            try {
                manager.ShadowTextureFormat = GetShadowTextureFormatValue(textureFormat);
                ResetManagerRuntimeShadowBlurState(manager);

                pointLightInstance.enabled = true;
                pointLightInstance.LightVolumeManager = manager;
                pointLightInstance.RuntimeShadowCamera = manager.RuntimeShadowCamera;
                pointLightInstance.RuntimeShadowDepthEncodeMaterial = manager.RuntimeShadowDepthEncodeMaterial;
                pointLightInstance.RuntimeShadowBlurMaterial = manager.RuntimeShadowBlurMaterial;
                pointLightInstance.RuntimeShadowResolution = resolution;
                pointLightInstance.RuntimeShadowBlurSamplePreset = EditorShadowBlurSamplePreset;
                pointLightInstance.RuntimeShadowFacesPerFrame = 6;
                pointLightInstance.RuntimeShadowDirectOutput = false;
                pointLightInstance.ShadowMapTexture = null;
                pointLightInstance.ShadowMapMaterial = null;
                pointLightInstance.AutoUpdateShadowMap = false;
                pointLightInstance.ShadowMapID = -1f;
                pointLightInstance.ShadowMapTextureIsCubemap = false;
                pointLightInstance.ShadowMapTextureHasDepthSlices = false;
                pointLightInstance.ShadowMapUsesCubemap = cubemapShadows;
                pointLightInstance.LayerMask = pointLightVolume.LayerMask;
                pointLightInstance.Bias = pointLightVolume.Bias;
                pointLightInstance.Blur = pointLightVolume.Blur;
                pointLightInstance.ContactHardening = pointLightVolume.ContactHardening;
                if (pointLightInstance.Intensity == 0f) pointLightInstance.Intensity = 1f;
                if (pointLightInstance.Color == Color.black) pointLightInstance.Color = Color.white;

                pointLightInstance.BakeShadows();
                runtimeShadowTexture = pointLightInstance.ShadowMapTexture as RenderTexture;
                int sliceCount = cubemapShadows ? 6 : 1;
                if (!IsRuntimeShadowTextureValid(runtimeShadowTexture, resolution, sliceCount)) {
                    Debug.LogError($"[LightVolumes] PointLightVolumeInstance did not produce a valid editor shadow output {infoString}.", pointLightVolume);
                    return false;
                }

                UnityEngine.Object shadowAsset = cubemapShadows ? CreateCubemapShadowAsset(runtimeShadowTexture, safeTextureFormat) : CreateSingleShadowAsset(runtimeShadowTexture, safeTextureFormat);
                if (shadowAsset == null) return false;

                UnityEngine.Object savedShadowAsset = SaveShadowAsset(pointLightVolume, shadowAsset);
                if (savedShadowAsset == null) return false;

                pointLightVolume.ShadowMap = savedShadowAsset;
                LVUtils.MarkDirty(pointLightVolume);
                baked = true;
                return true;
            } finally {
                pointLightVolume.EditorRestoreExclusionMask();
                manager.ShadowTextureFormat = oldShadowTextureFormat;
                ResetManagerRuntimeShadowBlurState(manager);
                pointLightVolume.EditorApplyAuthoringData(false, true, false);
                pointLightInstance.RuntimeShadowResolution = oldRuntimeShadowResolution;
                pointLightInstance.RuntimeShadowBlurSamplePreset = oldRuntimeShadowBlurSamplePreset;
                pointLightInstance.RuntimeShadowFacesPerFrame = oldRuntimeShadowFacesPerFrame;
                pointLightInstance.RuntimeShadowDirectOutput = oldRuntimeShadowDirectOutput;
                pointLightInstance.enabled = oldPointLightInstanceEnabled;
                if (regenerateArray) {
                    if (baked) manager.ReinitializeShadowTextures();
                    else manager.UpdateVolumes();
                }
                ReleaseTemporaryRenderTexture(runtimeShadowTexture);
                RenderTexture.active = oldActive;
            }
        }

        // Ensures the manager has the shared camera and materials required by PointLightVolumeInstance.BakeShadows().
        private static bool EnsureRuntimeShadowBakeDependencies(LightVolumeManager manager, PointLightVolumeInstance pointLightVolume) {
            Shader shadowDepthEncodeShader = Shader.Find(ShadowDepthEncodeShaderName);
            if (shadowDepthEncodeShader == null) {
                Debug.LogError($"[LightVolumes] Failed to find shadow depth encode shader '{ShadowDepthEncodeShaderName}'.", pointLightVolume);
                return false;
            }

            manager.EnsureRuntimeShadowCamera();
            if (manager.RuntimeShadowDepthEncodeMaterial == null || manager.RuntimeShadowDepthEncodeMaterial.shader != shadowDepthEncodeShader) manager.RuntimeShadowDepthEncodeMaterial = new Material(shadowDepthEncodeShader) { hideFlags = HideFlags.HideAndDontSave };

            Shader shadowBlurShader = Shader.Find(ShadowBlurShaderName);
            if (shadowBlurShader == null) {
                if (pointLightVolume.Blur > 0.0001f) Debug.LogWarning($"[LightVolumes] Failed to find shadow blur shader '{ShadowBlurShaderName}'. Baking without blur.", pointLightVolume);
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
        private static UnityEngine.Object SaveShadowAsset(PointLightVolumeInstance pointLightVolume, UnityEngine.Object shadowAsset) {
            UnityEngine.SceneManagement.Scene scene = pointLightVolume.gameObject.scene;
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) {
                Debug.LogError("[LightVolumes] Save the scene before baking a persistent shadow asset.", pointLightVolume);
                DestroyTransientShadowAsset(shadowAsset);
                return null;
            }

            try {
                string scenePath = scene.path;
                string escapedName = LVUtils.EscapeFileName(pointLightVolume.gameObject.name);
                string defaultPath = $"{System.IO.Path.GetDirectoryName(scenePath)}/{scene.name}/VRCLightVolumes/Temp/{escapedName}_shadows.asset";
                string path = ResolveShadowAssetPath(pointLightVolume, defaultPath);
                UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(path);

                // A light can change between a single-slice and cubemap shadow layout. Those assets have
                // different Unity types/local file IDs, so keep the old shared asset intact and allocate a
                // new path instead of breaking any other references to it.
                if (existingAsset != null && (existingAsset != pointLightVolume.ShadowMap || existingAsset.GetType() != shadowAsset.GetType())) {
                    path = AssetDatabase.GenerateUniqueAssetPath(defaultPath);
                }

                return SaveShadowAssetAtPath(shadowAsset, path);
            } catch (System.Exception exception) {
                Debug.LogError($"[LightVolumes] Failed to save the shadow asset: {exception.Message}", pointLightVolume);
                DestroyTransientShadowAsset(shadowAsset);
                return null;
            }
        }

        // Resolves the authoring override shared by editor and in-game shadow bakes.
        public static int ResolveShadowBakeResolution(PointLightVolumeInstance pointLightVolume, LightVolumeManager manager) {
            int authoredResolution = pointLightVolume != null ? pointLightVolume.ShadowBakeResolution : 0;
            if (authoredResolution > 0) return Mathf.Clamp(authoredResolution, 16, 2048);
            if (manager != null) return Mathf.Clamp(manager.ShadowTexturesWidth, 16, 2048);
            return pointLightVolume != null ? Mathf.Clamp(pointLightVolume.RuntimeShadowResolution, 16, 2048) : 16;
        }

        // Keeps a light's existing generated bake path stable while preventing identically named
        // lights from overwriting each other's shadow assets.
        private static string ResolveShadowAssetPath(PointLightVolumeInstance pointLightVolume, string defaultPath) {
            UnityEngine.Object currentShadow = pointLightVolume.ShadowMap;
            if (currentShadow != null) {
                string currentPath = AssetDatabase.GetAssetPath(currentShadow);
                string currentDirectory = System.IO.Path.GetDirectoryName(currentPath)?.Replace('\\', '/');
                string targetDirectory = System.IO.Path.GetDirectoryName(defaultPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(currentPath)
                    && AssetDatabase.IsMainAsset(currentShadow)
                    && string.Equals(System.IO.Path.GetExtension(currentPath), ".asset", System.StringComparison.OrdinalIgnoreCase)
                    && AssetDatabase.IsOpenForEdit(currentShadow)
                    && string.Equals(currentDirectory, targetDirectory, System.StringComparison.OrdinalIgnoreCase))
                    return currentPath;
            }
            return AssetDatabase.GenerateUniqueAssetPath(defaultPath);
        }

        // Replaces serialized shadow data on an existing compatible asset so its GUID/local file ID
        // and every reference to it remain stable. New paths still create an ordinary native asset.
        private static UnityEngine.Object SaveShadowAssetAtPath(UnityEngine.Object shadowAsset, string path) {
            if (shadowAsset == null) return null;
            if (string.IsNullOrEmpty(path)) {
                DestroyTransientShadowAsset(shadowAsset);
                return null;
            }

            UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(path);
            if (existingAsset == null) {
                LVUtils.SaveAsAsset(shadowAsset, path);
                UnityEngine.Object savedAsset = AssetDatabase.LoadMainAssetAtPath(path);
                if (savedAsset == null) DestroyTransientShadowAsset(shadowAsset);
                return savedAsset;
            }
            if (existingAsset.GetType() != shadowAsset.GetType()) {
                DestroyTransientShadowAsset(shadowAsset);
                return null;
            }

            string existingName = existingAsset.name;
            try {
                EditorUtility.CopySerialized(shadowAsset, existingAsset);
                existingAsset.name = existingName;
                EditorUtility.SetDirty(existingAsset);
                AssetDatabase.SaveAssetIfDirty(existingAsset);
                return existingAsset;
            } catch (System.Exception exception) {
                Debug.LogError($"[LightVolumes] Failed to update shadow asset '{path}' in place: {exception.Message}");
                return null;
            } finally {
                DestroyTransientShadowAsset(shadowAsset);
            }
        }

        // Releases failed or copied bake outputs without touching persistent source assets.
        private static void DestroyTransientShadowAsset(UnityEngine.Object shadowAsset) {
            if (shadowAsset != null && !AssetDatabase.Contains(shadowAsset)) UnityEngine.Object.DestroyImmediate(shadowAsset);
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

        // Resolves the persistent editor shadow format from the manager runtime format.
        private static TextureFormat GetManagerShadowMapBakeFormat(LightVolumeManager manager) {
            return manager != null && manager.ShadowTextureFormat == ShadowTextureFormatHalf ? TextureFormat.RGBAHalf : TextureFormat.RGBAFloat;
        }

        // Resolves the manager texture format value used by PointLightVolumeInstance.BakeShadows().
        private static int GetShadowTextureFormatValue(TextureFormat textureFormat) {
            return textureFormat == TextureFormat.RGBAHalf ? ShadowTextureFormatHalf : 1;
        }
    }
}
#endif
