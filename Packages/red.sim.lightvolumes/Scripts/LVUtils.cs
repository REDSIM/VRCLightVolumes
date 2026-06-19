using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using System.IO;
using UnityEditor.SceneManagement;
#endif

namespace VRCLightVolumes {
    public class LVUtils {

        // Transforms a point with the specified position, rotation, and scale
        public static Vector3 TransformPoint(Vector3 point, Vector3 position, Quaternion rotation, Vector3 scale) {
            return rotation * Vector3.Scale(point, scale) + position;
        }

        // Sets lossy scale on the specified transform
        public static void SetLossyScale(Transform transform, Vector3 targetLossyScale, int maxIterations = 20) {
            Vector3 guess = transform.localScale;
            for (int i = 0; i < maxIterations; i++) {
                transform.localScale = guess;
                Vector3 currentLossy = transform.lossyScale;
                Vector3 ratio = new Vector3(
                    currentLossy.x != 0 ? targetLossyScale.x / currentLossy.x : 1f,
                    currentLossy.y != 0 ? targetLossyScale.y / currentLossy.y : 1f,
                    currentLossy.z != 0 ? targetLossyScale.z / currentLossy.z : 1f
                );
                guess = new Vector3(guess.x * ratio.x, guess.y * ratio.y, guess.z * ratio.z);
            }
        }

        // Returns plane vertices for drawing a square
        public static Vector3[] GetPlaneVertices(Vector3 center, Quaternion rotation, float size) {
            Vector3 right = rotation * Vector3.right * size;
            Vector3 up = rotation * Vector3.up * size;
            return new Vector3[] { center - right - up, center - right + up, center + right + up, center + right - up };
        }

        // Checks whether this object is previewed as a prefab or is part of a scene
        public static bool IsInPrefabAsset(Object obj) {
#if UNITY_EDITOR
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            var prefabType = PrefabUtility.GetPrefabAssetType(obj);
            var prefabStatus = PrefabUtility.GetPrefabInstanceStatus(obj);
            return prefabStatus == PrefabInstanceStatus.NotAPrefab && prefabType != PrefabAssetType.NotAPrefab && prefabStage == null;
#else
            return false;
#endif
        }

        public static void MarkDirty(Object obj) {
#if UNITY_EDITOR
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorUtility.SetDirty(obj);
            if (PrefabUtility.IsPartOfPrefabInstance(obj))
                PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
#endif
        }

        // Applies voxels to a 3D texture
        public static bool Apply3DTextureData(Texture3D texture, Color[] colors) {
            try {
                texture.SetPixels(colors);
                texture.Apply(updateMipmaps: false);
                return true;
            } catch (UnityException ex) {
                Debug.LogError($"[LightVolumeUtils] Failed to SetPixels in the Texture3D. Error: {ex.Message}");
                return false;
            }
        }

        // Remaps a value
        public static float Remap(float value, float MinOld, float MaxOld, float MinNew, float MaxNew) {
            return MinNew + (value - MinOld) * (MaxNew - MinNew) / (MaxOld - MinOld);
        }

        // Remaps value to 01 range
        public static float RemapTo01(float value, float MinOld, float MaxOld) {
            return (value - MinOld) / (MaxOld - MinOld);
        }

        public static void SaveAsAssetDelayed(Object asset, string assetPath, System.Action<bool> callback = null) {
#if UNITY_EDITOR
            if (asset == null || string.IsNullOrEmpty(assetPath)) {
                Debug.LogError("[LightVolumeUtils] Invalid input for saving asset.");
                callback?.Invoke(false);
                return;
            }
            void DelayedSave() {
                EditorApplication.update -= DelayedSave;
                try {
                    string dir = Path.GetDirectoryName(assetPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    AssetDatabase.CreateAsset(asset, assetPath);
                    EditorUtility.SetDirty(asset);
                    callback?.Invoke(true);
                } catch (System.Exception e) {
                    Debug.LogError($"[LightVolumeUtils] Save failed: {e.Message}");
                    callback?.Invoke(false);
                }
            }
            EditorApplication.update += DelayedSave;
#else
            Debug.LogError($"[LightVolumeUtils] You can only save asset in editor!");
#endif
        }

        public static void SaveAsAsset(Object asset, string assetPath) {
#if UNITY_EDITOR
            if (asset == null || string.IsNullOrEmpty(assetPath)) {
                Debug.LogError("[LightVolumeUtils] Invalid input for saving asset.");
                return;
            }
            try {
                string dir = Path.GetDirectoryName(assetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                AssetDatabase.CreateAsset(asset, assetPath);
                EditorUtility.SetDirty(asset);
            } catch (System.Exception e) {
                Debug.LogError($"[LightVolumeUtils] Save failed: {e.Message}");
            }
#else
            Debug.LogError($"[LightVolumeUtils] You can only save asset in editor!");
#endif
        }

        // Simple 3D denoiser
        public static Vector3[] BilateralDenoise3D(Vector3[] input, int w, int h, int d, float sigmaSpatial = 1f, float sigmaRange = 0.1f) {
            Vector3[] output = new Vector3[input.Length];
            int r = Mathf.CeilToInt(2f * sigmaSpatial);

            for (int z = 0; z < d; z++)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++) {
                        int centerIdx = x + y * w + z * w * h;
                        Vector3 center = input[centerIdx];
                        Vector3 sum = Vector3.zero;
                        float weightSum = 0f;

                        for (int dz = -r; dz <= r; dz++)
                            for (int dy = -r; dy <= r; dy++)
                                for (int dx = -r; dx <= r; dx++) {
                                    int xx = x + dx;
                                    int yy = y + dy;
                                    int zz = z + dz;
                                    if (xx < 0 || yy < 0 || zz < 0 || xx >= w || yy >= h || zz >= d) continue;

                                    int nIdx = xx + yy * w + zz * w * h;
                                    Vector3 neighbor = input[nIdx];

                                    float spatialDist2 = dx * dx + dy * dy + dz * dz;
                                    float rangeDist2 = (neighbor - center).sqrMagnitude;

                                    float spatialWeight = Mathf.Exp(-spatialDist2 / (2f * sigmaSpatial * sigmaSpatial));
                                    float rangeWeight = Mathf.Exp(-rangeDist2 / (2f * sigmaRange * sigmaRange));

                                    float weight = spatialWeight * rangeWeight;
                                    sum += neighbor * weight;
                                    weightSum += weight;
                                }

                        output[centerIdx] = weightSum > 0f ? sum / weightSum : center;
                    }

            return output;
        }

        // Bounds of a transformed 1x1x1 cube
        public static Bounds BoundsFromTRS(Matrix4x4 trs) {
            Vector3 center = trs.GetColumn(3);
            Vector3 a = trs.GetColumn(0) * 0.5f;
            Vector3 b = trs.GetColumn(1) * 0.5f;
            Vector3 c = trs.GetColumn(2) * 0.5f;
            Vector3 extents = new Vector3(
                Mathf.Abs(a.x) + Mathf.Abs(b.x) + Mathf.Abs(c.x),
                Mathf.Abs(a.y) + Mathf.Abs(b.y) + Mathf.Abs(c.y),
                Mathf.Abs(a.z) + Mathf.Abs(b.z) + Mathf.Abs(c.z)
            );
            return new Bounds(center, extents * 2f);
        }

        // Fixes bakery L1 probe channel
        public static Vector3 DeringSingleSH(float L0, Vector3 L1) {
            L1 = L1 * 0.5f;
            float L1length = L1.magnitude;
            if (L1length > 0.0 && L0 > 0.0) {
                L1 *= Mathf.Min(L0 / L1length, 1.13f);
            }
            return L1;
        }

        // Fizes bakery L1 probe
        public static SphericalHarmonicsL2 DeringSH(SphericalHarmonicsL2 sh) {

            const int r = 0;
            const int g = 1;
            const int b = 2;
            const int a = 0;
            const int x = 3;
            const int y = 1;
            const int z = 2;

            Vector3 L0 = new Vector3(sh[r, a], sh[g, a], sh[b, a]);
            Vector3 L1r = new Vector3(sh[r, x], sh[r, y], sh[r, z]);
            Vector3 L1g = new Vector3(sh[g, x], sh[g, y], sh[g, z]);
            Vector3 L1b = new Vector3(sh[b, x], sh[b, y], sh[b, z]);

            L1r = DeringSingleSH(L0.x, L1r);
            L1g = DeringSingleSH(L0.y, L1g);
            L1b = DeringSingleSH(L0.z, L1b);

            sh[r, x] = L1r.x;
            sh[r, y] = L1r.y;
            sh[r, z] = L1r.z;

            sh[g, x] = L1g.x;
            sh[g, y] = L1g.y;
            sh[g, z] = L1g.z;

            sh[b, x] = L1b.x;
            sh[b, y] = L1b.y;
            sh[b, z] = L1b.z;

            return sh;
        }

        // Checks if any L2 data is provided in SphericalHarmonicsL2
        public static bool CheckSHL2(SphericalHarmonicsL2 sh) {
            for(int rgb = 0; rgb < 3; rgb++) { // Iterating RGB color components
                for(int coeff = 4; coeff < 9; coeff++) { // Iterating L1 and L2 coeffs
                    if(sh[rgb, coeff] != 0) return true;
                }
            }
            return false;
        }

        public static void TextureSetReadWrite(Texture texture, bool enabled) {
#if UNITY_EDITOR
            if (texture == null) {
                return;
            }

            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path)) {
                return;
            }

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) {
                return;
            }

            if (importer.isReadable != enabled) {
                importer.isReadable = enabled;
                importer.SaveAndReimport();
            }
#endif
        }

        // Ensures EXR projection sources keep linear HDR data when Unity imports them for Android.
        public static bool TextureSetLinearHDRAndroidImport(Texture texture) {
#if UNITY_EDITOR
            if (texture == null || texture is RenderTexture) return false;

            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".exr", System.StringComparison.OrdinalIgnoreCase)) return false;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return false;

            bool changed = false;
            if (importer.sRGBTexture) {
                importer.sRGBTexture = false;
                changed = true;
            }

            TextureImporterPlatformSettings androidSettings = importer.GetPlatformTextureSettings("Android");
            bool androidChanged = false;
            if (!androidSettings.overridden) {
                androidSettings.overridden = true;
                androidChanged = true;
            }
            if (androidSettings.format != TextureImporterFormat.RGBAHalf && androidSettings.format != TextureImporterFormat.RGBAFloat) {
                androidSettings.format = TextureImporterFormat.RGBAHalf;
                androidChanged = true;
            }
            if (androidSettings.textureCompression != TextureImporterCompression.Uncompressed) {
                androidSettings.textureCompression = TextureImporterCompression.Uncompressed;
                androidChanged = true;
            }
            if (androidChanged) {
                importer.SetPlatformTextureSettings(androidSettings);
                changed = true;
            }

            if (changed) importer.SaveAndReimport();
            return changed;
#else
            return false;
#endif
        }

        public static Texture3D DownscaleTexture3D(Texture3D source) {

            if (source == null) {
                return null;
            }

            int newWidth = Mathf.Max(1, source.width / 2);
            int newHeight = Mathf.Max(1, source.height / 2);
            int newDepth = Mathf.Max(1, source.depth / 2);

            Texture3D result = new Texture3D(newWidth, newHeight, newDepth, source.format, source.mipmapCount > 1);
            result.wrapMode = source.wrapMode;
            result.filterMode = FilterMode.Trilinear;
            result.anisoLevel = source.anisoLevel;

            Color[] sourcePixels = source.GetPixels();
            Color[] resultPixels = new Color[newWidth * newHeight * newDepth];

            int sourceWidth = source.width;
            int sourceHeight = source.height;
            int sourceDepth = source.depth;

            // Perform trilinear filtering
            for (int z = 0; z < newDepth; z++) {
                for (int y = 0; y < newHeight; y++) {
                    for (int x = 0; x < newWidth; x++) {

                        // Sample 8 pixels from source texture
                        int sx = x * 2;
                        int sy = y * 2;
                        int sz = z * 2;

                        // Clamp to bounds
                        int sx1 = Mathf.Min(sx + 1, sourceWidth - 1);
                        int sy1 = Mathf.Min(sy + 1, sourceHeight - 1);
                        int sz1 = Mathf.Min(sz + 1, sourceDepth - 1);

                        // Get 8 corner samples
                        Color c000 = sourcePixels[sx + sy * sourceWidth + sz * sourceWidth * sourceHeight];
                        Color c100 = sourcePixels[sx1 + sy * sourceWidth + sz * sourceWidth * sourceHeight];
                        Color c010 = sourcePixels[sx + sy1 * sourceWidth + sz * sourceWidth * sourceHeight];
                        Color c110 = sourcePixels[sx1 + sy1 * sourceWidth + sz * sourceWidth * sourceHeight];
                        Color c001 = sourcePixels[sx + sy * sourceWidth + sz1 * sourceWidth * sourceHeight];
                        Color c101 = sourcePixels[sx1 + sy * sourceWidth + sz1 * sourceWidth * sourceHeight];
                        Color c011 = sourcePixels[sx + sy1 * sourceWidth + sz1 * sourceWidth * sourceHeight];
                        Color c111 = sourcePixels[sx1 + sy1 * sourceWidth + sz1 * sourceWidth * sourceHeight];

                        // Average all 8 samples
                        Color averaged = (c000 + c100 + c010 + c110 + c001 + c101 + c011 + c111) * 0.125f;

                        int resultIndex = x + y * newWidth + z * newWidth * newHeight;
                        resultPixels[resultIndex] = averaged;

                    }
                }
            }

            result.SetPixels(resultPixels);
            result.Apply();

            return result;

        }

    }

}
