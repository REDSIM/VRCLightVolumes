#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCLightVolumes {

    public static class PointLightShadowBaker {
        private static class ShaderConstants {
            public const string ShadowDepthEncodeShaderName = "Hidden/VRCLV/PointLightShadowDepthEncode";
            public const string ShadowBlurShaderName = "Hidden/VRCLV/PointLightShadowRuntimeBlur";
            public const string EditorShadowBlurQualityKeyword = "VRCLV_EDITOR_SHADOW_BLUR_QUALITY";
            public const string RuntimeShadowQualityHighKeyword = "VRCLV_RUNTIME_SHADOW_QUALITY_HIGH";
            public const string RuntimeShadowBlurUniformKeyword = "VRCLV_RUNTIME_SHADOW_BLUR_UNIFORM";
        }

        private struct RendererState {
            public Renderer Renderer;
            public bool PreviousForceRenderingOff;
        }

        private static readonly int _shadowDepthTexID = Shader.PropertyToID("_ShadowDepthTex");
        private static readonly int _shadowFarClipID = Shader.PropertyToID("_ShadowFarClip");
        private static readonly int _shadowNearClipID = Shader.PropertyToID("_ShadowNearClip");
        private static readonly int _shadowBakeBiasID = Shader.PropertyToID("_ShadowBakeBias");
        private static readonly int _sourceArrayTexID = Shader.PropertyToID("_SourceArrayTex");
        private static readonly int _depthArrayTexID = Shader.PropertyToID("_DepthArrayTex");
        private static readonly int _faceIndexID = Shader.PropertyToID("_FaceIndex");
        private static readonly int _sourceBaseSliceID = Shader.PropertyToID("_SourceBaseSlice");
        private static readonly int _depthBaseSliceID = Shader.PropertyToID("_DepthBaseSlice");
        private static readonly int _blurDirectionID = Shader.PropertyToID("_BlurDirection");
        private static readonly int _blurRadiusID = Shader.PropertyToID("_BlurRadius");
        private static readonly int _blurDepthID = Shader.PropertyToID("_BlurDepth");
        private static readonly int _invResolutionID = Shader.PropertyToID("_InvResolution");

        private static readonly CubemapFace[] _cubemapFaces = {
            CubemapFace.PositiveX,
            CubemapFace.NegativeX,
            CubemapFace.PositiveY,
            CubemapFace.NegativeY,
            CubemapFace.PositiveZ,
            CubemapFace.NegativeZ
        };

        private static readonly int[] _oppositeCubemapFaceIndices = { 1, 0, 3, 2, 5, 4 };
        private static readonly bool[] _arrayToCubemapHorizontalFlip = { false, false, true, true, false, false };
        private static readonly Quaternion[] _faceRotations = {
            new Quaternion(0f, -0.70710678f, 0f, 0.70710678f),
            new Quaternion(0f, 0.70710678f, 0f, 0.70710678f),
            new Quaternion(0f, -0.70710678f, 0.70710678f, 0f),
            new Quaternion(0f, 0.70710678f, 0.70710678f, 0f),
            new Quaternion(0f, 1f, 0f, 0f),
            Quaternion.identity
        };

        // Bakes an EVSM shadow cubemap from the light position.
        public static Cubemap BakeShadowMap(PointLightVolume pointLightVolume, int resolution, float farClip, string infoString = "") {
            return BakeShadowMap(pointLightVolume, resolution, farClip, TextureFormat.RGBAFloat, 0, 0.05f, infoString);
        }

        // Bakes an EVSM shadow cubemap from the light position.
        public static Cubemap BakeShadowMap(PointLightVolume pointLightVolume, int resolution, float farClip, TextureFormat textureFormat, string infoString = "") {
            return BakeShadowMap(pointLightVolume, resolution, farClip, textureFormat, 0, 0.05f, infoString);
        }

        // Bakes an EVSM shadow cubemap from the light position.
        public static Cubemap BakeShadowMap(PointLightVolume pointLightVolume, int resolution, float farClip, TextureFormat textureFormat, float blurRadius, string infoString = "") {
            return BakeShadowMap(pointLightVolume, resolution, farClip, textureFormat, blurRadius, 0.1f, infoString);
        }

        // Bakes an EVSM shadow cubemap from the light position.
        public static Cubemap BakeShadowMap(PointLightVolume pointLightVolume, int resolution, float farClip, TextureFormat textureFormat, float blurRadius, float blurDepth, string infoString = "") {
            RenderTexture cubeRT = BakeShadowMapRenderTexture(pointLightVolume, resolution, farClip, textureFormat, blurRadius, blurDepth, infoString);
            if (cubeRT == null) return null;

            TextureFormat safeTextureFormat = textureFormat == TextureFormat.RGBAFloat ? TextureFormat.RGBAFloat : TextureFormat.RGBAHalf;
            Cubemap cubemap = new Cubemap(cubeRT.width, safeTextureFormat, false) {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0
            };

            try {
                ReadRenderTextureToCubemap(cubeRT, cubemap, cubeRT.width, safeTextureFormat);
                return cubemap;
            } finally {
                if (RenderTexture.active == cubeRT) RenderTexture.active = null;
                cubeRT.Release();
                Object.DestroyImmediate(cubeRT);
            }
        }

        // Bakes an EVSM shadow texture array from the light position.
        public static Texture2DArray BakeShadowMapTextureArray(PointLightVolume pointLightVolume, int resolution, float farClip, TextureFormat textureFormat, float blurRadius, float blurDepth, string infoString = "") {
            RenderTexture arrayRT = BakeShadowMapRenderTexture(pointLightVolume, resolution, farClip, textureFormat, blurRadius, blurDepth, infoString);
            if (arrayRT == null) return null;

            TextureFormat safeTextureFormat = textureFormat == TextureFormat.RGBAFloat ? TextureFormat.RGBAFloat : TextureFormat.RGBAHalf;
            Texture2DArray textureArray = new Texture2DArray(arrayRT.width, arrayRT.height, 6, safeTextureFormat, false, true) {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0
            };

            try {
                ReadRenderTextureToTextureArray(arrayRT, textureArray, arrayRT.width, safeTextureFormat);
                return textureArray;
            } finally {
                if (RenderTexture.active == arrayRT) RenderTexture.active = null;
                arrayRT.Release();
                Object.DestroyImmediate(arrayRT);
            }
        }

        // Bakes an EVSM shadow texture into a live RenderTexture.
        public static RenderTexture BakeShadowMapRenderTexture(PointLightVolume pointLightVolume, int resolution, float farClip, TextureFormat textureFormat, float blurRadius, float blurDepth, string infoString = "") {
            if (pointLightVolume == null) return null;

            Shader shadowDepthEncodeShader = Shader.Find(ShaderConstants.ShadowDepthEncodeShaderName);
            if (shadowDepthEncodeShader == null) {
                Debug.LogError($"[PointLightShadowBaker] Failed to find shadow depth encode shader '{ShaderConstants.ShadowDepthEncodeShaderName}'.", pointLightVolume);
                return null;
            }
            Shader shadowBlurShader = Shader.Find(ShaderConstants.ShadowBlurShaderName);

            int safeResolution = Mathf.Clamp(resolution, 16, 2048);
            float safeFarClip = Mathf.Max(farClip, 0.0001f);
            float safeNearClip = pointLightVolume.GetShadowNearClip();
            float safeBias = Mathf.Max(pointLightVolume.Bias, 0);
            bool useBlur = blurRadius > 0.0001f && shadowBlurShader != null;
            RenderTextureFormat renderTextureFormat = textureFormat == TextureFormat.RGBAFloat ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGBHalf;

            RenderTexture oldActive = RenderTexture.active;
            RenderTexture depthTexture = null;
            RenderTexture rawTexture = null;
            RenderTexture blurTempTexture = null;
            RenderTexture blurTexture = null;
            RenderTexture resultTexture = null;
            GameObject bakerObject = null;
            Material shadowDepthEncodeMaterial = null;
            Material editorShadowBlurMaterial = null;
            RendererState[] objectMaskRendererStates = null;
            bool keepResultTexture = false;

            try {
                objectMaskRendererStates = ApplyObjectMaskFilter(pointLightVolume);
                bakerObject = new GameObject($"VRCLV Shadow Baker {infoString}") {
                    hideFlags = HideFlags.HideAndDontSave
                };

                Camera shadowCamera = bakerObject.AddComponent<Camera>();
                ConfigureShadowCamera(shadowCamera, safeNearClip, safeFarClip, pointLightVolume.LayerMask.value);

                depthTexture = CreateDepthTexture(safeResolution);
                rawTexture = CreateShadowArrayTexture(safeResolution, renderTextureFormat);
                shadowDepthEncodeMaterial = new Material(shadowDepthEncodeShader) { hideFlags = HideFlags.HideAndDontSave };
                BakeRawShadowFaces(pointLightVolume, shadowCamera, depthTexture, rawTexture, shadowDepthEncodeMaterial, safeBias);

                if (useBlur) {
                    blurTempTexture = CreateShadowArrayTexture(safeResolution, renderTextureFormat);
                    blurTexture = CreateShadowArrayTexture(safeResolution, renderTextureFormat);
                    editorShadowBlurMaterial = new Material(shadowBlurShader) { hideFlags = HideFlags.HideAndDontSave };
                    ConfigureEditorBlurMaterial(editorShadowBlurMaterial, Mathf.Max(blurRadius, 0), Mathf.Clamp01(blurDepth), safeResolution);
                    BlurShadowFaces(rawTexture, blurTempTexture, blurTexture, editorShadowBlurMaterial);
                    resultTexture = blurTexture;
                } else {
                    resultTexture = rawTexture;
                }

                keepResultTexture = true;
                return resultTexture;
            } finally {
                RestoreObjectMaskFilter(objectMaskRendererStates);
                ReleaseBakeTexture(depthTexture, keepResultTexture ? resultTexture : null);
                ReleaseBakeTexture(rawTexture, keepResultTexture ? resultTexture : null);
                ReleaseBakeTexture(blurTempTexture, keepResultTexture ? resultTexture : null);
                ReleaseBakeTexture(blurTexture, keepResultTexture ? resultTexture : null);
                if (shadowDepthEncodeMaterial != null) Object.DestroyImmediate(shadowDepthEncodeMaterial);
                if (editorShadowBlurMaterial != null) Object.DestroyImmediate(editorShadowBlurMaterial);
                RenderTexture.active = oldActive;
                if (bakerObject != null) Object.DestroyImmediate(bakerObject);
            }
        }

        // Configures a temporary perspective camera for point-light depth face rendering.
        private static void ConfigureShadowCamera(Camera shadowCamera, float nearClip, float farClip, int cullingMask) {
            float safeFarClip = Mathf.Max(farClip, 0.0001f);
            float safeNearClip = Mathf.Max(nearClip, 0.0001f);
            if (safeNearClip >= safeFarClip) safeNearClip = safeFarClip * 0.5f;

            shadowCamera.enabled = false;
            shadowCamera.clearFlags = CameraClearFlags.Depth;
            shadowCamera.backgroundColor = Color.white;
            shadowCamera.orthographic = false;
            shadowCamera.fieldOfView = 90f;
            shadowCamera.aspect = 1f;
            shadowCamera.nearClipPlane = safeNearClip;
            shadowCamera.farClipPlane = safeFarClip;
            shadowCamera.depthTextureMode = DepthTextureMode.None;
            shadowCamera.renderingPath = RenderingPath.Forward;
            shadowCamera.allowHDR = false;
            shadowCamera.allowMSAA = false;
            shadowCamera.useOcclusionCulling = false;
            shadowCamera.cullingMask = cullingMask;
            shadowCamera.stereoTargetEye = StereoTargetEyeMask.None;
            shadowCamera.ResetReplacementShader();
        }

        // Creates the explicit depth target sampled by the EVSM encode pass.
        private static RenderTexture CreateDepthTexture(int resolution) {
            RenderTexture texture = new RenderTexture(resolution, resolution, 32, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);
            texture.dimension = TextureDimension.Tex2D;
            texture.useMipMap = false;
            texture.autoGenerateMips = false;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            texture.anisoLevel = 0;
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.Create();
            return texture;
        }

        // Creates a six-slice EVSM texture array for editor shadow baking.
        private static RenderTexture CreateShadowArrayTexture(int resolution, RenderTextureFormat format) {
            RenderTexture texture = new RenderTexture(resolution, resolution, 0, format, RenderTextureReadWrite.Linear);
            texture.dimension = TextureDimension.Tex2DArray;
            texture.volumeDepth = 6;
            texture.useMipMap = false;
            texture.autoGenerateMips = false;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.anisoLevel = 0;
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.Create();
            return texture;
        }

        // Renders depth and encodes EVSM moments for all six point-light faces.
        private static void BakeRawShadowFaces(PointLightVolume pointLightVolume, Camera shadowCamera, RenderTexture depthTexture, RenderTexture outputTexture, Material encodeMaterial, float bias) {
            Transform cameraTransform = shadowCamera.transform;
            Transform lightTransform = pointLightVolume.transform;
            Quaternion bakeRotation = pointLightVolume.UseWorldSpace ? Quaternion.identity : lightTransform.rotation;

            encodeMaterial.SetFloat(_shadowFarClipID, shadowCamera.farClipPlane);
            encodeMaterial.SetFloat(_shadowNearClipID, shadowCamera.nearClipPlane);
            encodeMaterial.SetFloat(_shadowBakeBiasID, bias);
            encodeMaterial.SetTexture(_shadowDepthTexID, depthTexture, RenderTextureSubElement.Depth);

            RenderTexture previousTargetTexture = shadowCamera.targetTexture;
            shadowCamera.targetTexture = depthTexture;
            cameraTransform.position = lightTransform.position;
            for (int i = 0; i < 6; i++) {
                cameraTransform.rotation = bakeRotation * _faceRotations[i];
                shadowCamera.Render();
                BlitMaterialToSlice(depthTexture, encodeMaterial, 0, outputTexture, i);
            }
            shadowCamera.targetTexture = previousTargetTexture;
        }

        // Configures the high-quality editor blur material.
        private static void ConfigureEditorBlurMaterial(Material blurMaterial, float blurRadius, float blurDepth, int resolution) {
            blurMaterial.EnableKeyword(ShaderConstants.EditorShadowBlurQualityKeyword);
            blurMaterial.EnableKeyword(ShaderConstants.RuntimeShadowQualityHighKeyword);
            if (blurDepth <= 0f) blurMaterial.EnableKeyword(ShaderConstants.RuntimeShadowBlurUniformKeyword);
            else blurMaterial.DisableKeyword(ShaderConstants.RuntimeShadowBlurUniformKeyword);

            blurMaterial.SetFloat(_blurRadiusID, blurRadius);
            if (blurDepth <= 0f) blurMaterial.SetFloat(_blurDepthID, 0f);
            else {
                float normalizedBlurDepth = Mathf.Clamp01(blurDepth);
                blurMaterial.SetFloat(_blurDepthID, (Mathf.Pow(10f, normalizedBlurDepth) - 1f) * 0.1111111111f);
            }
            blurMaterial.SetFloat(_invResolutionID, 1f / Mathf.Max(resolution, 1));
        }

        // Applies two-pass seam-aware blur to a complete six-face EVSM texture array.
        private static void BlurShadowFaces(RenderTexture rawTexture, RenderTexture tempTexture, RenderTexture outputTexture, Material blurMaterial) {
            for (int i = 0; i < 6; i++) BlitShadowBlurFace(rawTexture, tempTexture, blurMaterial, i, Vector2.right, 0, 0, rawTexture, 0);
            for (int i = 0; i < 6; i++) BlitShadowBlurFace(tempTexture, outputTexture, blurMaterial, i, Vector2.up, 0, 0, rawTexture, 0);
        }

        // Applies one directional blur pass to one texture-array face.
        private static void BlitShadowBlurFace(Texture sourceTexture, RenderTexture destination, Material blurMaterial, int face, Vector2 direction, int sourceBaseSlice, int targetBaseSlice, Texture depthTexture, int depthBaseSlice) {
            blurMaterial.SetTexture(_sourceArrayTexID, sourceTexture);
            blurMaterial.SetTexture(_depthArrayTexID, depthTexture);
            blurMaterial.SetFloat(_sourceBaseSliceID, sourceBaseSlice);
            blurMaterial.SetFloat(_depthBaseSliceID, depthBaseSlice);
            blurMaterial.SetVector(_blurDirectionID, direction);
            blurMaterial.SetInt(_faceIndexID, face);
            BlitMaterialToSlice(sourceTexture, blurMaterial, 0, destination, targetBaseSlice + face);
        }

        // Renders one material pass into one texture-array slice.
        private static void BlitMaterialToSlice(Texture sourceTexture, Material material, int pass, RenderTexture destination, int targetSlice) {
            RenderTexture previousRenderTexture = RenderTexture.active;
            Graphics.SetRenderTarget(destination, 0, CubemapFace.Unknown, targetSlice);
            Graphics.Blit(sourceTexture, material, pass);
            RenderTexture.active = previousRenderTexture;
        }

        // Releases an editor bake render texture unless it is the returned result.
        private static void ReleaseBakeTexture(RenderTexture texture, RenderTexture keepTexture) {
            if (texture == null || texture == keepTexture) return;
            if (RenderTexture.active == texture) RenderTexture.active = null;
            texture.Release();
            Object.DestroyImmediate(texture);
        }

        // Temporarily hides renderers outside the point light's object mask for this editor bake.
        private static RendererState[] ApplyObjectMaskFilter(PointLightVolume pointLightVolume) {
            if (!HasObjectMask(pointLightVolume)) return null;

            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            RendererState[] rendererStates = new RendererState[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) {
                Renderer renderer = renderers[i];
                rendererStates[i].Renderer = renderer;
                rendererStates[i].PreviousForceRenderingOff = renderer.forceRenderingOff;
                if (!IsRendererInsideObjectMask(renderer, pointLightVolume.ObjectMask)) renderer.forceRenderingOff = true;
            }
            return rendererStates;
        }

        // Restores renderer visibility changed by ApplyObjectMaskFilter.
        private static void RestoreObjectMaskFilter(RendererState[] rendererStates) {
            if (rendererStates == null) return;
            for (int i = 0; i < rendererStates.Length; i++) {
                Renderer renderer = rendererStates[i].Renderer;
                if (renderer != null) renderer.forceRenderingOff = rendererStates[i].PreviousForceRenderingOff;
            }
        }

        // Checks whether this point light has at least one valid object-mask root.
        private static bool HasObjectMask(PointLightVolume pointLightVolume) {
            if (pointLightVolume == null || pointLightVolume.ObjectMask == null) return false;
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

        // Copies the baked render texture to the persistent cubemap through the CPU side so the asset is serializable.
        private static void ReadRenderTextureToCubemap(RenderTexture source, Cubemap destination, int resolution, TextureFormat textureFormat) {
            RenderTexture oldActive = RenderTexture.active;
            Texture2D temp = new Texture2D(resolution, resolution, textureFormat, false, true);
            int bytesPerPixel = GetTextureFormatBytesPerPixel(textureFormat);
            byte[] transformedFaceData = source.dimension == TextureDimension.Cube ? null : new byte[resolution * resolution * bytesPerPixel];
            try {
                for (int i = 0; i < _cubemapFaces.Length; i++) {
                    if (source.dimension == TextureDimension.Cube) Graphics.SetRenderTarget(source, 0, _cubemapFaces[i]);
                    else Graphics.SetRenderTarget(source, 0, CubemapFace.Unknown, i);
                    temp.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                    temp.Apply(false);
                    if (source.dimension == TextureDimension.Cube) destination.SetPixelData(temp.GetRawTextureData<byte>(), 0, _cubemapFaces[i]);
                    else SetArraySliceAsCubemapFace(temp, destination, i, resolution, bytesPerPixel, transformedFaceData);
                }
                destination.Apply(false);
            } finally {
                RenderTexture.active = oldActive;
                Object.DestroyImmediate(temp);
            }
        }

        // Stores one runtime-layout array slice in the cubemap orientation expected by Hidden/CubeFace.
        private static void SetArraySliceAsCubemapFace(Texture2D source, Cubemap destination, int sourceFaceIndex, int resolution, int bytesPerPixel, byte[] transformedFaceData) {
            int destinationFaceIndex = _oppositeCubemapFaceIndices[sourceFaceIndex];
            bool horizontalFlip = _arrayToCubemapHorizontalFlip[sourceFaceIndex];
            var sourceData = source.GetRawTextureData<byte>();

            for (int y = 0; y < resolution; y++) {
                int destinationY = horizontalFlip ? y : resolution - 1 - y;
                for (int x = 0; x < resolution; x++) {
                    int destinationX = horizontalFlip ? resolution - 1 - x : x;
                    int sourceOffset = (y * resolution + x) * bytesPerPixel;
                    int destinationOffset = (destinationY * resolution + destinationX) * bytesPerPixel;
                    for (int b = 0; b < bytesPerPixel; b++) transformedFaceData[destinationOffset + b] = sourceData[sourceOffset + b];
                }
            }

            destination.SetPixelData(transformedFaceData, 0, _cubemapFaces[destinationFaceIndex]);
        }

        // Returns the byte width of one RGBA EVSM texel.
        private static int GetTextureFormatBytesPerPixel(TextureFormat textureFormat) {
            return textureFormat == TextureFormat.RGBAFloat ? 16 : 8;
        }

        // Copies the baked render texture to a persistent Texture2DArray without changing face order or orientation.
        private static void ReadRenderTextureToTextureArray(RenderTexture source, Texture2DArray destination, int resolution, TextureFormat textureFormat) {
            RenderTexture oldActive = RenderTexture.active;
            Texture2D temp = new Texture2D(resolution, resolution, textureFormat, false, true);
            try {
                for (int i = 0; i < 6; i++) {
                    Graphics.SetRenderTarget(source, 0, CubemapFace.Unknown, i);
                    temp.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                    temp.Apply(false);
                    destination.SetPixelData(temp.GetRawTextureData<byte>(), 0, i);
                }
                destination.Apply(false);
            } finally {
                RenderTexture.active = oldActive;
                Object.DestroyImmediate(temp);
            }
        }

    }

}
#endif
