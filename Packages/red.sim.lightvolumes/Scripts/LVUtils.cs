using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

#if UNITY_EDITOR
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("red.sim.LightVolumesUdon.EditorTests")]
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

        // Marks an editor object and its prefab instance overrides dirty without affecting play mode.
        public static void MarkDirty(Object obj) {
#if UNITY_EDITOR
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorUtility.SetDirty(obj);
            if (PrefabUtility.IsPartOfPrefabInstance(obj))
                PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
#endif
        }

        // Captures serialized editor state so no-op synchronization does not mark scenes dirty.
        public static string GetSerializedState(Object obj) {
#if UNITY_EDITOR
            if (obj == null || EditorApplication.isPlayingOrWillChangePlaymode) return null;
            return EditorJsonUtility.ToJson(obj);
#else
            return null;
#endif
        }

        // Marks an object dirty only when its serialized editor state actually changed.
        public static void MarkDirtyIfSerializedStateChanged(Object obj, string previousState) {
#if UNITY_EDITOR
            if (obj == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (previousState == null || previousState != EditorJsonUtility.ToJson(obj)) MarkDirty(obj);
#endif
        }

        // Applies voxels to a 3D texture
        public static bool Apply3DTextureData(Texture3D texture, Color[] colors) {
            try {
                texture.SetPixels(colors);
                texture.Apply(updateMipmaps: false);
                return true;
            } catch (UnityException ex) {
                Debug.LogError($"[LightVolumes] Failed to SetPixels in the Texture3D. Error: {ex.Message}");
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

        // Schedules asset creation for the next editor update and reports whether it succeeded.
        public static void SaveAsAssetDelayed(Object asset, string assetPath, System.Action<bool> callback = null) {
#if UNITY_EDITOR
            if (asset == null || string.IsNullOrEmpty(assetPath)) {
                Debug.LogError("[LightVolumes] Invalid input for saving asset.");
                callback?.Invoke(false);
                return;
            }
            // Creates the asset after the current editor callback has completed.
            void DelayedSave() {
                EditorApplication.update -= DelayedSave;
                try {
                    assetPath = EscapeAssetPathFileName(assetPath);
                    string dir = Path.GetDirectoryName(assetPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    AssetDatabase.CreateAsset(asset, assetPath);
                    EditorUtility.SetDirty(asset);
                    callback?.Invoke(true);
                } catch (System.Exception e) {
                    Debug.LogError($"[LightVolumes] Save failed: {e.Message}");
                    callback?.Invoke(false);
                }
            }
            EditorApplication.update += DelayedSave;
#else
            Debug.LogError($"[LightVolumes] You can only save assets in the editor!");
#endif
        }

        // Escapes unsupported characters in the file-name segment of a Unity asset path.
        public static string EscapeAssetPathFileName(string assetPath) {
            if (string.IsNullOrEmpty(assetPath)) return assetPath;

            int separatorIndex = assetPath.LastIndexOf('/');
            string fileName = assetPath.Substring(separatorIndex + 1);
            string escapedFileName = EscapeFileName(fileName);
            if (escapedFileName == fileName) return assetPath;

            return separatorIndex < 0 ? escapedFileName : assetPath.Substring(0, separatorIndex + 1) + escapedFileName;
        }

        // Escapes only characters that cannot be stored inside a file name.
        public static string EscapeFileName(string fileName) {
            if (string.IsNullOrEmpty(fileName)) return fileName;

            System.Text.StringBuilder builder = null;
            for (int i = 0; i < fileName.Length; i++) {
                char character = fileName[i];
                if (!IsInvalidFileNameCharacter(character)) {
                    if (builder != null) builder.Append(character);
                    continue;
                }

                if (builder == null) {
                    builder = new System.Text.StringBuilder(fileName.Length + 8);
                    builder.Append(fileName, 0, i);
                }
                builder.Append('%');
                builder.Append(((int)character).ToString("X2"));
            }

            return builder == null ? fileName : builder.ToString();
        }

        // Checks if the character is not supported by Windows file names or Unity asset path separators.
        private static bool IsInvalidFileNameCharacter(char character) {
            return character < 32 ||
                character == '<' ||
                character == '>' ||
                character == ':' ||
                character == '"' ||
                character == '/' ||
                character == '\\' ||
                character == '|' ||
                character == '?' ||
                character == '*';
        }

        // Saves an object as a Unity asset after sanitizing and creating its destination path.
        public static void SaveAsAsset(Object asset, string assetPath) {
#if UNITY_EDITOR
            if (asset == null || string.IsNullOrEmpty(assetPath)) {
                Debug.LogError("[LightVolumes] Invalid input for saving asset.");
                return;
            }
            try {
                assetPath = EscapeAssetPathFileName(assetPath);
                string dir = Path.GetDirectoryName(assetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                AssetDatabase.CreateAsset(asset, assetPath);
                EditorUtility.SetDirty(asset);
            } catch (System.Exception e) {
                Debug.LogError($"[LightVolumes] Save failed: {e.Message}");
            }
#else
            Debug.LogError($"[LightVolumes] You can only save assets in the editor!");
#endif
        }

        // Simple 3D denoiser
        public static Vector3[] BilateralDenoise3D(Vector3[] input, int w, int h, int d, float sigmaSpatial = 1f, float sigmaRange = 0.1f) {
            Vector3[] output = new Vector3[input.Length];
            int r = Mathf.CeilToInt(2f * sigmaSpatial);
            float spatialDivisor = 2f * sigmaSpatial * sigmaSpatial;
            float rangeDivisor = 2f * sigmaRange * sigmaRange;
            int sliceSize = w * h;

            System.Threading.Tasks.Parallel.For(0, input.Length, centerIdx => {
                int z = centerIdx / sliceSize;
                int sliceIndex = centerIdx - z * sliceSize;
                int y = sliceIndex / w;
                int x = sliceIndex - y * w;
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

                            int nIdx = xx + yy * w + zz * sliceSize;
                            Vector3 neighbor = input[nIdx];

                            float spatialDist2 = dx * dx + dy * dy + dz * dz;
                            float rangeDist2 = (neighbor - center).sqrMagnitude;

                            float spatialWeight = Mathf.Exp(-spatialDist2 / spatialDivisor);
                            float rangeWeight = Mathf.Exp(-rangeDist2 / rangeDivisor);

                            float weight = spatialWeight * rangeWeight;
                            sum += neighbor * weight;
                            weightSum += weight;
                        }

                output[centerIdx] = weightSum > 0f ? sum / weightSum : center;
            });

            return output;
        }

#if UNITY_EDITOR
        // Validates, optionally postprocesses and packs external or Progressive L0/L1 probe arrays into Light Volume texture channels.
        public static bool TryPrepareLightVolumeProbeData(Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity, int w, int h, int d, int dilationIterations, float dilationBackfaceBias, bool denoise, out Color[][] textureColors, out string error) {
            textureColors = null;
            error = null;

            int voxelCount = GetVoxelCount(w, h, d);
            if (voxelCount < 0) {
                error = "Resolution is invalid or the voxel count is too large.";
                return false;
            }
            if (l0 == null || l1r == null || l1g == null || l1b == null) {
                error = "SH arrays cannot be null.";
                return false;
            }
            if (l0.Length != voxelCount || l1r.Length != voxelCount || l1g.Length != voxelCount || l1b.Length != voxelCount) {
                error = $"Every SH array must contain exactly {voxelCount} elements.";
                return false;
            }
            if (validity != null && validity.Length != voxelCount) {
                error = $"The validity array must contain exactly {voxelCount} elements.";
                return false;
            }

            Vector3[] processedL0 = l0;
            Vector3[] processedL1r = l1r;
            Vector3[] processedL1g = l1g;
            Vector3[] processedL1b = l1b;
            if (validity != null && dilationIterations > 0) DilateLightVolumeProbes(ref processedL0, ref processedL1r, ref processedL1g, ref processedL1b, validity, w, h, d, dilationIterations, dilationBackfaceBias);
            if (denoise) {
                processedL0 = BilateralDenoise3D(processedL0, w, h, d, 1f, 0.05f);
                processedL1r = BilateralDenoise3D(processedL1r, w, h, d, 1f, 0.05f);
                processedL1g = BilateralDenoise3D(processedL1g, w, h, d, 1f, 0.05f);
                processedL1b = BilateralDenoise3D(processedL1b, w, h, d, 1f, 0.05f);
            }

            const float coeff = 1.65f; // Preserves the existing Bakery-compatible L1 scale used by Progressive.
            textureColors = new[] { new Color[voxelCount], new Color[voxelCount], new Color[voxelCount] };
            for (int i = 0; i < voxelCount; i++) {
                textureColors[0][i] = new Color(processedL0[i].x, processedL0[i].y, processedL0[i].z, processedL1r[i].z * coeff);
                textureColors[1][i] = new Color(processedL1r[i].x * coeff, processedL1g[i].x * coeff, processedL1b[i].x * coeff, processedL1g[i].z * coeff);
                textureColors[2][i] = new Color(processedL1r[i].y * coeff, processedL1g[i].y * coeff, processedL1b[i].y * coeff, processedL1b[i].z * coeff);
            }
            return true;
        }

        // Calculates a safe positive voxel count for probe array validation.
        private static int GetVoxelCount(int w, int h, int d) {
            if (w <= 0 || h <= 0 || d <= 0 || w > int.MaxValue / h) return -1;
            int sliceSize = w * h;
            return sliceSize > int.MaxValue / d ? -1 : sliceSize * d;
        }

        // Dilates valid L0/L1 values into invalid voxels without mutating caller-owned arrays.
        private static void DilateLightVolumeProbes(ref Vector3[] l0, ref Vector3[] l1r, ref Vector3[] l1g, ref Vector3[] l1b, float[] sourceValidity, int w, int h, int d, int iterations, float backfaceBias) {
            int voxelCount = l0.Length;
            int sliceSize = w * h;
            float[] validity = (float[])sourceValidity.Clone();
            float[] validityDilated = (float[])sourceValidity.Clone();
            Vector3[] processedL0 = (Vector3[])l0.Clone();
            Vector3[] processedL1r = (Vector3[])l1r.Clone();
            Vector3[] processedL1g = (Vector3[])l1g.Clone();
            Vector3[] processedL1b = (Vector3[])l1b.Clone();
            Vector3[] l0Dilated = (Vector3[])l0.Clone();
            Vector3[] l1rDilated = (Vector3[])l1r.Clone();
            Vector3[] l1gDilated = (Vector3[])l1g.Clone();
            Vector3[] l1bDilated = (Vector3[])l1b.Clone();

            for (int iteration = 0; iteration < iterations; iteration++) {
                System.Threading.Tasks.Parallel.For(0, voxelCount, centerIndex => {
                    if (validity[centerIndex] < backfaceBias) return;

                    int voxelZ = centerIndex / sliceSize;
                    int sliceIndex = centerIndex - voxelZ * sliceSize;
                    int voxelY = sliceIndex / w;
                    int voxelX = sliceIndex - voxelY * w;
                    Vector3 l0Sum = Vector3.zero;
                    Vector3 l1rSum = Vector3.zero;
                    Vector3 l1gSum = Vector3.zero;
                    Vector3 l1bSum = Vector3.zero;
                    int validCount = 0;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++) {
                                int x = voxelX + dx;
                                int y = voxelY + dy;
                                int z = voxelZ + dz;
                                if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= d) continue;

                                int neighborIndex = x + y * w + z * sliceSize;
                                if (validity[neighborIndex] >= backfaceBias) continue;
                                validCount++;
                                l0Sum += processedL0[neighborIndex];
                                l1rSum += processedL1r[neighborIndex];
                                l1gSum += processedL1g[neighborIndex];
                                l1bSum += processedL1b[neighborIndex];
                            }

                    if (validCount == 0) return;
                    l0Dilated[centerIndex] = l0Sum / validCount;
                    l1rDilated[centerIndex] = l1rSum / validCount;
                    l1gDilated[centerIndex] = l1gSum / validCount;
                    l1bDilated[centerIndex] = l1bSum / validCount;
                    validityDilated[centerIndex] = 0f;
                });

                System.Array.Copy(validityDilated, validity, voxelCount);
                System.Array.Copy(l0Dilated, processedL0, voxelCount);
                System.Array.Copy(l1rDilated, processedL1r, voxelCount);
                System.Array.Copy(l1gDilated, processedL1g, voxelCount);
                System.Array.Copy(l1bDilated, processedL1b, voxelCount);
            }

            l0 = processedL0;
            l1r = processedL1r;
            l1g = processedL1g;
            l1b = processedL1b;
        }
#endif

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

        // Changes a texture asset's Read/Write import setting only when necessary.
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

        // Creates a half-resolution 3D texture by averaging each source voxel block.
        public static Texture3D DownscaleTexture3D(Texture3D source) {

            if (source == null) {
                return null;
            }

            int newWidth = Mathf.Max(1, source.width / 2);
            int newHeight = Mathf.Max(1, source.height / 2);
            int newDepth = Mathf.Max(1, source.depth / 2);

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

            Texture3D result = new Texture3D(newWidth, newHeight, newDepth, source.format, source.mipmapCount > 1);
            try {
                result.wrapMode = source.wrapMode;
                result.filterMode = FilterMode.Trilinear;
                result.anisoLevel = source.anisoLevel;
                result.SetPixels(resultPixels);
                result.Apply();
                return result;
            } catch {
                if (Application.isPlaying) Object.Destroy(result);
                else Object.DestroyImmediate(result);
                throw;
            }

        }

    }

}
