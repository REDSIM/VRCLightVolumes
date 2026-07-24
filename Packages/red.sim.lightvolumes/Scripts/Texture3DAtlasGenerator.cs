using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace VRCLightVolumes {
    public struct Atlas3D {
        public Texture3D Texture;
        public Vector3[] BoundsUvwMin;
        public Vector3[] BoundsUvwMax;
    }

    public enum TexturePackingStrategy {
        MinimumVRAM,
        MinimumDepth,
    }

    public static class Texture3DAtlasGenerator {

        private const int maxAtlasSize = 2048;
        private const float vectorNormalizeEpsilon = 0.00001f;
        private const int progressStepsCount = 6;
        private const int downscaleProgressStep = 0;
        private const int colorProgressStep = 1;
        private const int deduplicateProgressStep = 2;
        private const int packingProgressStep = 3;
        private const int writingProgressStep = 4;
        private const int savingProgressStep = 5;

        public static event Action<LightVolumeInstance[]> OnPreAtlasCreate;

        // Generates a packed 3D atlas for all baked Light Volumes and reports completion through the callback.
        public static IEnumerator CreateAtlas(LightVolumeInstance[] volumes, Action<Atlas3D> onComplete, int downscaleCount = 0, TexturePackingStrategy packingStrategy = TexturePackingStrategy.MinimumVRAM) {

            List<Texture3D> temporaryTextures = new List<Texture3D>();
            CancellationTokenSource cancellationSource = new CancellationTokenSource();
            const int padding = 1;

#if UNITY_EDITOR
            int progressId = Progress.Start("Generating 3D Atlas", "Generating Light Volumes 3D Atlas", Progress.Options.Sticky);
#endif

            try {

                if (volumes == null || volumes.Length == 0) {
                    Debug.LogError("[LightVolume] No light volumes were provided for atlas generation!");
                    yield break;
                }

                if (downscaleCount < 0) downscaleCount = 0;

                OnPreAtlasCreate?.Invoke(volumes);

                int volumeCount = volumes.Length;
                int textureCount = volumeCount * 3;
                AtlasTextureData[] texs = new AtlasTextureData[textureCount];
                bool[] forceUniqueTextures = new bool[textureCount];

                // Collect source textures, apply requested downscale, and run SH postprocess before atlas packing.
                for (int i = 0; i < volumeCount; i++) {

                    LightVolumeInstance volume = volumes[i];
                    if (volume == null) {
                        Debug.LogError("[LightVolume] One of the light volumes is not setuped!");
                        yield break;
                    }

                    int textureIndex = i * 3;
                    bool reserveUVSpace = !volume.Bake && volume.ReserveUVSpace;

                    if (reserveUVSpace) {
                        int w = GetReservedTextureSize(volume.Resolution.x, downscaleCount);
                        int h = GetReservedTextureSize(volume.Resolution.y, downscaleCount);
                        int d = GetReservedTextureSize(volume.Resolution.z, downscaleCount);
                        if (IsTextureSizeTooLargeForAtlas(w, h, d, padding) || GetReservedVoxelCount(w, h, d) < 0) {
                            Debug.LogError($"[LightVolume] Reserved UV space for light volume \"{volume.gameObject.name}\" is too large!");
                            yield break;
                        }

                        texs[textureIndex] = CreateReservedTextureData(w, h, d, new Color(1, 1, 1, 0));
                        texs[textureIndex + 1] = CreateReservedTextureData(w, h, d, Color.clear);
                        texs[textureIndex + 2] = CreateReservedTextureData(w, h, d, Color.clear);
                        forceUniqueTextures[textureIndex] = true;
                        forceUniqueTextures[textureIndex + 1] = true;
                        forceUniqueTextures[textureIndex + 2] = true;
                        continue;
                    }

                    if (volume.Texture0 == null || volume.Texture1 == null || volume.Texture2 == null) {
                        Debug.LogError($"[LightVolume] Light volume \"{volume.gameObject.name}\" is not baked!");
                        yield break;
                    }

                    Texture3D tex0 = volume.Texture0;
                    Texture3D tex1 = volume.Texture1;
                    Texture3D tex2 = volume.Texture2;
                    if (!ValidateTextureBundle(volume, tex0, tex1, tex2)) yield break;

                    for (int j = 0; j < downscaleCount; j++) {
                        ThreadProgress downscaleProgress0 = new ThreadProgress { Total = Mathf.Max(tex0.depth / 2, 1) };
                        ThreadProgress downscaleProgress1 = new ThreadProgress { Total = Mathf.Max(tex1.depth / 2, 1) };
                        ThreadProgress downscaleProgress2 = new ThreadProgress { Total = Mathf.Max(tex2.depth / 2, 1) };
                        Task<DownscaleTextureResult> downscaleTask0 = StartDownscaleTextureTask(tex0, downscaleProgress0, cancellationSource.Token);
                        Task<DownscaleTextureResult> downscaleTask1 = StartDownscaleTextureTask(tex1, downscaleProgress1, cancellationSource.Token);
                        Task<DownscaleTextureResult> downscaleTask2 = StartDownscaleTextureTask(tex2, downscaleProgress2, cancellationSource.Token);
                        Task downscaleTask = Task.WhenAll(downscaleTask0, downscaleTask1, downscaleTask2);

                        int downscaleWorkCount = Mathf.Max(volumeCount * downscaleCount, 1);
                        int downscaleWorkIndex = i * downscaleCount + j;
                        IEnumerator waitForDownscaleTask = WaitForTask(downscaleTask, () => (downscaleWorkIndex + GetCombinedProgress(downscaleProgress0, downscaleProgress1, downscaleProgress2)) / downscaleWorkCount,
#if UNITY_EDITOR
                            progress => ReportProgress(progressId, downscaleProgressStep, progress, $"Downscaling volumes {i + 1}/{volumeCount}")
#else
                            null
#endif
                        );
                        while (waitForDownscaleTask.MoveNext()) yield return waitForDownscaleTask.Current;
                        if (downscaleTask.IsCanceled) yield break;

                        tex0 = CreateDownscaledTexture3D(tex0, downscaleTask0.Result);
                        tex1 = CreateDownscaledTexture3D(tex1, downscaleTask1.Result);
                        tex2 = CreateDownscaledTexture3D(tex2, downscaleTask2.Result);
                        temporaryTextures.Add(tex0);
                        temporaryTextures.Add(tex1);
                        temporaryTextures.Add(tex2);

                        yield return null;
                    }

                    if (IsTextureSizeTooLargeForAtlas(tex0.width, tex0.height, tex0.depth, padding)) {
                        Debug.LogError($"[LightVolume] Light volume \"{volume.gameObject.name}\" texture dimensions are too large for the atlas.");
                        yield break;
                    }

                    ThreadProgress postprocessProgress = new ThreadProgress { Total = tex0.depth };
                    float dark = -volume.Shadows * 0.5f;
                    float bright = 1 - volume.Highlights * 0.5f;
                    Task<PostprocessTextureResult> postprocessTask = StartPostprocessSphericalHarmonicsTask(tex0, tex1, tex2, dark, bright, volume.Exposure, postprocessProgress, cancellationSource.Token);
                    IEnumerator waitForPostprocessTask = WaitForTask(postprocessTask, () => (i + GetProgress(postprocessProgress)) / Mathf.Max(volumeCount, 1),
#if UNITY_EDITOR
                        progress => ReportProgress(progressId, colorProgressStep, progress, $"Volumes color correction {i + 1}/{volumeCount}")
#else
                        null
#endif
                    );
                    while (waitForPostprocessTask.MoveNext()) yield return waitForPostprocessTask.Current;
                    if (postprocessTask.IsCanceled) yield break;

                    PostprocessTextureResult postprocessResult = postprocessTask.Result;
                    texs[textureIndex] = postprocessResult.Texture0;
                    texs[textureIndex + 1] = postprocessResult.Texture1;
                    texs[textureIndex + 2] = postprocessResult.Texture2;
                }

                int count = texs.Length;

                // Deduplicate by final RGBAHalf-equivalent data so identical stored islands share atlas space.
                AtlasTextureKey[] textureKeys = new AtlasTextureKey[count];
                ThreadProgress deduplicateProgress = new ThreadProgress { Total = count };
                Task deduplicateTask = Task.Run(() => CalculateTextureKeys(texs, forceUniqueTextures, textureKeys, deduplicateProgress, cancellationSource.Token), cancellationSource.Token);
                IEnumerator waitForDeduplicateTask = WaitForTask(deduplicateTask, () => GetProgress(deduplicateProgress),
#if UNITY_EDITOR
                    progress => ReportProgress(progressId, deduplicateProgressStep, progress, $"Finding unique volumes ({Volatile.Read(ref deduplicateProgress.Processed)}/{count})")
#else
                    null
#endif
                );
                while (waitForDeduplicateTask.MoveNext()) yield return waitForDeduplicateTask.Current;
                if (deduplicateTask.IsCanceled) yield break;

                Dictionary<AtlasTextureKey, int> keyToUnique = new Dictionary<AtlasTextureKey, int>();
                List<AtlasTextureData> uniqueTexs = new List<AtlasTextureData>();
                int[] origToUnique = new int[count];

                for (int i = 0; i < count; ++i) {
                    AtlasTextureData texture = texs[i];
                    if (texture == null) {
                        origToUnique[i] = -1;
                        continue;
                    }

                    if (forceUniqueTextures[i]) {
                        origToUnique[i] = uniqueTexs.Count;
                        uniqueTexs.Add(texture);
                        continue;
                    }

                    AtlasTextureKey key = textureKeys[i];
                    if (!keyToUnique.TryGetValue(key, out int uniqueIndex)) {
                        uniqueIndex = uniqueTexs.Count;
                        uniqueTexs.Add(texture);
                        keyToUnique.Add(key, uniqueIndex);
                    }

                    origToUnique[i] = uniqueIndex;
                }

                texs = null;
                int uniqueCount = uniqueTexs.Count;
                yield return null;

                // Pack unique islands with one voxel of padding on every side.
                AtlasBlock[] blocks = new AtlasBlock[uniqueCount];
                for (int i = 0; i < uniqueCount; ++i) {
                    AtlasTextureData texture = uniqueTexs[i];
                    blocks[i] = new AtlasBlock { Index = i, Width = texture.Width, Height = texture.Height, Depth = texture.Depth };
                }

                ThreadProgress packingProgress = new ThreadProgress { Total = blocks.Length };
                Task<PackingResult> packingTask = Task.Run(() => PackTextureBlocks(blocks, padding, packingStrategy, packingProgress, cancellationSource.Token), cancellationSource.Token);
                IEnumerator waitForPackingTask = WaitForTask(packingTask, () => GetProgress(packingProgress),
#if UNITY_EDITOR
                    progress => ReportProgress(progressId, packingProgressStep, progress, $"Packing light volume islands ({Volatile.Read(ref packingProgress.Processed)}/{blocks.Length})")
#else
                    null
#endif
                );
                while (waitForPackingTask.MoveNext()) yield return waitForPackingTask.Current;
                if (packingTask.IsCanceled) yield break;

                PackingResult packingResult = packingTask.Result;
                if (!packingResult.Success) {
                    Debug.LogError("[LightVolume] Light Volume atlas is too large to fit in the maximum texture size!");
                    yield break;
                }

                int atlasW = packingResult.AtlasWidth;
                int atlasH = packingResult.AtlasHeight;
                int atlasD = packingResult.AtlasDepth;

                ulong vCount = (ulong)atlasW * (ulong)atlasH * (ulong)atlasD;
                if (vCount > int.MaxValue) {
                    Debug.LogError($"[LightVolume] Light Volume voxel count is too large and can't be saved!");
                    yield break;
                }

                // Fill the atlas pixels on worker threads and map deduplicated bounds back to every source texture slot.
                Color[] atlasPixels = new Color[(int)vCount];
                Vector3[] uniqueBoundsMin = new Vector3[uniqueCount];
                Vector3[] uniqueBoundsMax = new Vector3[uniqueCount];
                ThreadProgress writingProgress = new ThreadProgress { Total = packingResult.Placed.Length };
                AtlasTextureData[] uniqueTextureArray = uniqueTexs.ToArray();
                Task writingTask = Task.Run(() => WriteAtlasPixels(packingResult.Placed, uniqueTextureArray, atlasPixels, uniqueBoundsMin, uniqueBoundsMax, atlasW, atlasH, atlasD, padding, writingProgress, cancellationSource.Token), cancellationSource.Token);
                IEnumerator waitForWritingTask = WaitForTask(writingTask, () => GetProgress(writingProgress),
#if UNITY_EDITOR
                    progress => ReportProgress(progressId, writingProgressStep, progress, $"Writing light volumes data ({Volatile.Read(ref writingProgress.Processed)}/{packingResult.Placed.Length})")
#else
                    null
#endif
                );
                while (waitForWritingTask.MoveNext()) yield return waitForWritingTask.Current;
                if (writingTask.IsCanceled) yield break;

                yield return null;

                Vector3[] boundsMin = new Vector3[count];
                Vector3[] boundsMax = new Vector3[count];
                for (int i = 0; i < count; ++i) {
                    int uniqueIndex = origToUnique[i];
                    if (uniqueIndex < 0) continue;

                    boundsMin[i] = uniqueBoundsMin[uniqueIndex];
                    boundsMax[i] = uniqueBoundsMax[uniqueIndex];
                }

                yield return null;

#if UNITY_EDITOR
                ReportProgress(progressId, savingProgressStep, 1f, "Saving light volumes");
#endif

                Texture3D atlasTexture = new Texture3D(atlasW, atlasH, atlasD, TextureFormat.RGBAHalf, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                if (!LVUtils.Apply3DTextureData(atlasTexture, atlasPixels)) {
                    UnityEngine.Object.DestroyImmediate(atlasTexture);
                    yield break;
                }

                yield return null;

                onComplete?.Invoke(new Atlas3D { Texture = atlasTexture, BoundsUvwMin = boundsMin, BoundsUvwMax = boundsMax });

            } finally {
                cancellationSource.Cancel();
#if UNITY_EDITOR
                Progress.Finish(progressId);
                Progress.Remove(progressId);
                DestroyTemporaryTextures(temporaryTextures);
#endif
            }

        }

        // Returns the reserved texture dimension after atlas downscaling.
        private static int GetReservedTextureSize(int size, int downscaleCount) {
            size = Mathf.Max(size, 1);
            for (int i = 0; i < downscaleCount; i++) size = Mathf.Max(1, size / 2);
            return size;
        }

        // Returns voxel count for a reserved texture, or -1 if it cannot fit in a managed array.
        private static int GetReservedVoxelCount(int width, int height, int depth) {
            if (width <= 0 || height <= 0 || depth <= 0) return -1;
            if (width > int.MaxValue / height) return -1;
            int sliceSize = width * height;
            if (depth > int.MaxValue / sliceSize) return -1;
            return sliceSize * depth;
        }

        // Creates managed data used only to reserve an atlas island.
        private static AtlasTextureData CreateReservedTextureData(int width, int height, int depth, Color color) {
            int voxelCount = GetReservedVoxelCount(width, height, depth);
            Color[] colors = new Color[voxelCount];
            if (color != Color.clear) {
                for (int i = 0; i < voxelCount; i++) colors[i] = color;
            }
            return new AtlasTextureData(width, height, depth, colors);
        }

        // Validates baked texture bundle dimensions and channel format before worker threads read pixel data.
        private static bool ValidateTextureBundle(LightVolumeInstance volume, Texture3D tex0, Texture3D tex1, Texture3D tex2) {
            if (!IsSupportedSourceFormat(tex0.format) || !IsSupportedSourceFormat(tex1.format) || !IsSupportedSourceFormat(tex2.format)) {
                Debug.LogError($"[LightVolume] Light volume \"{volume.gameObject.name}\" has unsupported texture format. Light Volume textures must use RGBAHalf, RGBAFloat, RGBA32 or ARGB32.");
                return false;
            }

            if (tex0.width != tex1.width || tex0.width != tex2.width || tex0.height != tex1.height || tex0.height != tex2.height || tex0.depth != tex1.depth || tex0.depth != tex2.depth) {
                Debug.LogError($"[LightVolume] Light volume \"{volume.gameObject.name}\" has mismatched Texture3D dimensions.");
                return false;
            }

            return true;
        }

        // Checks whether a texture island can fit in the atlas after padding is added.
        private static bool IsTextureSizeTooLargeForAtlas(int width, int height, int depth, int padding) {
            int padded = padding * 2;
            return width <= 0 || height <= 0 || depth <= 0 || width > maxAtlasSize - padded || height > maxAtlasSize - padded || depth > maxAtlasSize - padded;
        }

        // Checks whether a source texture format can safely represent packed RGBA SH data.
        private static bool IsSupportedSourceFormat(TextureFormat format) {
            return format == TextureFormat.RGBAHalf || format == TextureFormat.RGBAFloat || format == TextureFormat.RGBA32 || format == TextureFormat.ARGB32;
        }

#if UNITY_EDITOR
        // Destroys non-persistent temporary 3D textures created during atlas generation.
        private static void DestroyTemporaryTextures(List<Texture3D> textures) {
            if (textures == null) return;
            for (int i = 0; i < textures.Count; i++) {
                Texture3D texture = textures[i];
                if (texture != null && !EditorUtility.IsPersistent(texture)) {
                    UnityEngine.Object.DestroyImmediate(texture);
                    textures[i] = null;
                }
            }
        }

        // Reports progress for a single atlas generation stage.
        private static void ReportProgress(int progressId, int step, float progress, string message) {
            if (progress < 0f) progress = 0f;
            if (progress > 1f) progress = 1f;
            Progress.Report(progressId, (step + progress) / progressStepsCount, message);
        }
#endif

        // Starts the threaded pixel downscale job after reading Unity texture data on the main thread.
        private static Task<DownscaleTextureResult> StartDownscaleTextureTask(Texture3D source, ThreadProgress progress, CancellationToken cancellationToken) {
            Color[] sourcePixels = source.GetPixels();
            int sourceWidth = source.width;
            int sourceHeight = source.height;
            int sourceDepth = source.depth;
            return Task.Run(() => DownscaleTexturePixels(sourcePixels, sourceWidth, sourceHeight, sourceDepth, progress, cancellationToken), cancellationToken);
        }

        // Creates a Unity Texture3D from threaded downscale output on the main thread.
        private static Texture3D CreateDownscaledTexture3D(Texture3D source, DownscaleTextureResult result) {
            Texture3D texture = new Texture3D(result.Width, result.Height, result.Depth, source.format, source.mipmapCount > 1);
            texture.wrapMode = source.wrapMode;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = source.anisoLevel;
            texture.SetPixels(result.Pixels);
            texture.Apply();
            return texture;
        }

        // Downscales texture pixels with the same 8-sample box filter as the previous implementation.
        private static DownscaleTextureResult DownscaleTexturePixels(Color[] sourcePixels, int sourceWidth, int sourceHeight, int sourceDepth, ThreadProgress progress, CancellationToken cancellationToken) {
            int newWidth = Math.Max(1, sourceWidth / 2);
            int newHeight = Math.Max(1, sourceHeight / 2);
            int newDepth = Math.Max(1, sourceDepth / 2);
            Color[] resultPixels = new Color[newWidth * newHeight * newDepth];
            int sourceSliceSize = sourceWidth * sourceHeight;
            int resultSliceSize = newWidth * newHeight;
            ParallelOptions parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };

            Parallel.For(0, newDepth, parallelOptions, z => {
                int sz = z * 2;
                int sz1 = Math.Min(sz + 1, sourceDepth - 1);
                int sourceSlice0 = sz * sourceSliceSize;
                int sourceSlice1 = sz1 * sourceSliceSize;
                int resultSlice = z * resultSliceSize;

                for (int y = 0; y < newHeight; y++) {
                    int sy = y * 2;
                    int sy1 = Math.Min(sy + 1, sourceHeight - 1);
                    int sourceRow00 = sourceSlice0 + sy * sourceWidth;
                    int sourceRow01 = sourceSlice0 + sy1 * sourceWidth;
                    int sourceRow10 = sourceSlice1 + sy * sourceWidth;
                    int sourceRow11 = sourceSlice1 + sy1 * sourceWidth;
                    int resultRow = resultSlice + y * newWidth;

                    for (int x = 0; x < newWidth; x++) {
                        int sx = x * 2;
                        int sx1 = Math.Min(sx + 1, sourceWidth - 1);

                        Color c000 = sourcePixels[sourceRow00 + sx];
                        Color c100 = sourcePixels[sourceRow00 + sx1];
                        Color c010 = sourcePixels[sourceRow01 + sx];
                        Color c110 = sourcePixels[sourceRow01 + sx1];
                        Color c001 = sourcePixels[sourceRow10 + sx];
                        Color c101 = sourcePixels[sourceRow10 + sx1];
                        Color c011 = sourcePixels[sourceRow11 + sx];
                        Color c111 = sourcePixels[sourceRow11 + sx1];

                        resultPixels[resultRow + x] = new Color(
                            (c000.r + c100.r + c010.r + c110.r + c001.r + c101.r + c011.r + c111.r) * 0.125f,
                            (c000.g + c100.g + c010.g + c110.g + c001.g + c101.g + c011.g + c111.g) * 0.125f,
                            (c000.b + c100.b + c010.b + c110.b + c001.b + c101.b + c011.b + c111.b) * 0.125f,
                            (c000.a + c100.a + c010.a + c110.a + c001.a + c101.a + c011.a + c111.a) * 0.125f);
                    }
                }

                Interlocked.Increment(ref progress.Processed);
            });

            return new DownscaleTextureResult { Width = newWidth, Height = newHeight, Depth = newDepth, Pixels = resultPixels };
        }

        // Starts the threaded spherical harmonics postprocess after reading Unity texture data on the main thread.
        private static Task<PostprocessTextureResult> StartPostprocessSphericalHarmonicsTask(Texture3D tex0, Texture3D tex1, Texture3D tex2, float dark, float bright, float expo, ThreadProgress progress, CancellationToken cancellationToken) {
            int width = tex0.width;
            int height = tex0.height;
            int depth = tex0.depth;
            Color[] colors0 = tex0.GetPixels();
            Color[] colors1 = tex1.GetPixels();
            Color[] colors2 = tex2.GetPixels();
            float exposureMultiplier = Mathf.Pow(2, expo);

            return Task.Run(() => {
                PostProcessSphericalHarmonics(colors0, colors1, colors2, width, height, depth, dark, bright, exposureMultiplier, progress, cancellationToken);
                return new PostprocessTextureResult {
                    Texture0 = new AtlasTextureData(width, height, depth, colors0),
                    Texture1 = new AtlasTextureData(width, height, depth, colors1),
                    Texture2 = new AtlasTextureData(width, height, depth, colors2)
                };
            }, cancellationToken);
        }

        // Applies SH deringing and color correction to three texture channels.
        private static void PostProcessSphericalHarmonics(Color[] colors0, Color[] colors1, Color[] colors2, int width, int height, int depth, float dark, float bright, float exposureMultiplier, ThreadProgress progress, CancellationToken cancellationToken) {
            int sliceSize = width * height;
            ParallelOptions parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };

            Parallel.For(0, depth, parallelOptions, z => {
                int sliceOffset = z * sliceSize;
                for (int y = 0; y < height; y++) {
                    int rowOffset = sliceOffset + y * width;
                    for (int x = 0; x < width; x++) {
                        int index = rowOffset + x;

                        Color tex0 = colors0[index];
                        Color tex1 = colors1[index];
                        Color tex2 = colors2[index];

                        float l0x = tex0.r;
                        float l0y = tex0.g;
                        float l0z = tex0.b;
                        float l1rx = tex1.r;
                        float l1ry = tex2.r;
                        float l1rz = tex0.a;
                        float l1gx = tex1.g;
                        float l1gy = tex2.g;
                        float l1gz = tex1.a;
                        float l1bx = tex1.b;
                        float l1by = tex2.b;
                        float l1bz = tex2.a;

                        DeringSingleSH(l0x, ref l1rx, ref l1ry, ref l1rz);
                        DeringSingleSH(l0y, ref l1gx, ref l1gy, ref l1gz);
                        DeringSingleSH(l0z, ref l1bx, ref l1by, ref l1bz);

                        CorrectVector(ref l1rx, ref l1ry, ref l1rz, dark, bright, exposureMultiplier);
                        CorrectVector(ref l1gx, ref l1gy, ref l1gz, dark, bright, exposureMultiplier);
                        CorrectVector(ref l1bx, ref l1by, ref l1bz, dark, bright, exposureMultiplier);
                        CorrectVector(ref l0x, ref l0y, ref l0z, dark, bright, exposureMultiplier);

                        colors0[index] = new Color(l0x, l0y, l0z, l1rz);
                        colors1[index] = new Color(l1rx, l1gx, l1bx, l1gz);
                        colors2[index] = new Color(l1ry, l1gy, l1by, l1bz);
                    }
                }

                Interlocked.Increment(ref progress.Processed);
            });
        }

        // Applies the same single-channel SH deringing used by LVUtils without allocating Vector3 values.
        private static void DeringSingleSH(float l0, ref float x, ref float y, ref float z) {
            x *= 0.5f;
            y *= 0.5f;
            z *= 0.5f;

            float length = (float)Math.Sqrt(x * x + y * y + z * z);
            if (length > 0.0f && l0 > 0.0f) {
                float scale = l0 / length;
                if (scale > 1.13f) scale = 1.13f;
                x *= scale;
                y *= scale;
                z *= scale;
            }
        }

        // Applies color correction to a vector without allocating Vector3 values.
        private static void CorrectVector(ref float x, ref float y, ref float z, float dark, float bright, float exposureMultiplier) {
            float magnitude = (float)Math.Sqrt(x * x + y * y + z * z);
            if (magnitude <= vectorNormalizeEpsilon) {
                x = 0f;
                y = 0f;
                z = 0f;
                return;
            }

            float remapped = (magnitude - dark) / (bright - dark);
            float correctedMagnitude = remapped * exposureMultiplier;
            if (correctedMagnitude < 0f || float.IsNaN(correctedMagnitude)) correctedMagnitude = 0f;
            float scale = correctedMagnitude / magnitude;
            x *= scale;
            y *= scale;
            z *= scale;
        }

        // Calculates content keys used for atlas island deduplication.
        private static void CalculateTextureKeys(AtlasTextureData[] textures, bool[] forceUniqueTextures, AtlasTextureKey[] keys, ThreadProgress progress, CancellationToken cancellationToken) {
            ParallelOptions parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };
            Parallel.For(0, textures.Length, parallelOptions, i => {
                AtlasTextureData texture = textures[i];
                if (texture != null && !forceUniqueTextures[i]) keys[i] = ComputeTextureKey(texture);
                Interlocked.Increment(ref progress.Processed);
            });
        }

        // Computes a hash over RGBAHalf-equivalent pixel data so dedupe matches final atlas storage precision.
        private static AtlasTextureKey ComputeTextureKey(AtlasTextureData texture) {
            unchecked {
                ulong hashA = 1469598103934665603UL;
                ulong hashB = 1099511628211UL;
                AddIntToHash(ref hashA, texture.Width);
                AddIntToHash(ref hashA, texture.Height);
                AddIntToHash(ref hashA, texture.Depth);
                AddIntToHash(ref hashB, texture.Pixels.Length);

                Color[] pixels = texture.Pixels;
                for (int i = 0; i < pixels.Length; i++) {
                    Color color = pixels[i];
                    AddHalfToHash(ref hashA, FloatToHalf(color.r));
                    AddHalfToHash(ref hashA, FloatToHalf(color.g));
                    AddHalfToHash(ref hashA, FloatToHalf(color.b));
                    AddHalfToHash(ref hashA, FloatToHalf(color.a));
                    AddHalfToHash(ref hashB, FloatToHalf(color.a));
                    AddHalfToHash(ref hashB, FloatToHalf(color.b));
                    AddHalfToHash(ref hashB, FloatToHalf(color.g));
                    AddHalfToHash(ref hashB, FloatToHalf(color.r));
                }

                return new AtlasTextureKey {
                    HashA = hashA,
                    HashB = hashB,
                    Width = texture.Width,
                    Height = texture.Height,
                    Depth = texture.Depth,
                    PixelCount = pixels.Length
                };
            }
        }

        // Adds an integer to a simple deterministic FNV-style hash.
        private static void AddIntToHash(ref ulong hash, int value) {
            unchecked {
                hash ^= (uint)value;
                hash *= 1099511628211UL;
                hash ^= (uint)(value >> 16);
                hash *= 1099511628211UL;
            }
        }

        // Adds a half-precision channel to a simple deterministic FNV-style hash.
        private static void AddHalfToHash(ref ulong hash, ushort value) {
            unchecked {
                hash ^= (byte)value;
                hash *= 1099511628211UL;
                hash ^= (byte)(value >> 8);
                hash *= 1099511628211UL;
            }
        }

        // Converts a float to IEEE 754 half bits for storage-precision hashing.
        private static ushort FloatToHalf(float value) {
            FloatUIntUnion union = new FloatUIntUnion { FloatValue = value };
            uint floatBits = union.UIntValue;
            uint sign = (floatBits >> 16) & 0x8000u;
            int exponent = (int)((floatBits >> 23) & 0xffu);
            uint mantissa = floatBits & 0x007fffffu;

            if (exponent == 255) {
                if (mantissa == 0) return (ushort)(sign | 0x7c00u);
                mantissa >>= 13;
                return (ushort)(sign | 0x7c00u | mantissa | (mantissa == 0 ? 1u : 0u));
            }

            exponent = exponent - 127 + 15;
            if (exponent >= 31) return (ushort)(sign | 0x7c00u);
            if (exponent <= 0) {
                if (exponent < -10) return (ushort)sign;
                mantissa = (mantissa | 0x00800000u) >> (1 - exponent);
                if ((mantissa & 0x00001000u) != 0) mantissa += 0x00002000u;
                return (ushort)(sign | (mantissa >> 13));
            }

            if ((mantissa & 0x00001000u) != 0) {
                mantissa += 0x00002000u;
                if ((mantissa & 0x00800000u) != 0) {
                    mantissa = 0;
                    exponent++;
                    if (exponent >= 31) return (ushort)(sign | 0x7c00u);
                }
            }

            return (ushort)(sign | ((uint)exponent << 10) | (mantissa >> 13));
        }

        // Packs unique texture blocks into the smallest valid atlas according to the selected strategy.
        private static PackingResult PackTextureBlocks(AtlasBlock[] blocks, int padding, TexturePackingStrategy packingStrategy, ThreadProgress progress, CancellationToken cancellationToken) {
            Array.Sort(blocks, CompareBlocksByVolumeDescending);
            List<AtlasPlacedBlock> placed = new List<AtlasPlacedBlock>(blocks.Length);
            int atlasW = 0;
            int atlasH = 0;
            int atlasD = 0;

            for (int i = 0; i < blocks.Length; i++) {
                cancellationToken.ThrowIfCancellationRequested();
                AtlasBlock block = blocks[i];
                if (IsTextureSizeTooLargeForAtlas(block.Width, block.Height, block.Depth, padding)) return new PackingResult { Success = false };

                int bw = block.Width + padding * 2;
                int bh = block.Height + padding * 2;
                int bd = block.Depth + padding * 2;

                AtlasPlacedBlock[] placedArray = placed.ToArray();
                List<int> xCandidates = new List<int>(placedArray.Length + 1) { 0 };
                List<int> yCandidates = new List<int>(placedArray.Length + 1) { 0 };
                List<int> zCandidates = new List<int>(placedArray.Length + 1) { 0 };
                for (int placedIndex = 0; placedIndex < placedArray.Length; placedIndex++) {
                    AtlasPlacedBlock placedBlock = placedArray[placedIndex];
                    AddUniqueCandidate(xCandidates, placedBlock.X + placedBlock.Width);
                    AddUniqueCandidate(yCandidates, placedBlock.Y + placedBlock.Height);
                    AddUniqueCandidate(zCandidates, placedBlock.Z + placedBlock.Depth);
                }

                PlacementSearchResult best = FindBestPlacement(placedArray, xCandidates, yCandidates, zCandidates, bw, bh, bd, atlasW, atlasH, atlasD, packingStrategy, cancellationToken);
                if (!best.Valid) return new PackingResult { Success = false };

                AtlasPlacedBlock newBlock = new AtlasPlacedBlock {
                    X = best.X,
                    Y = best.Y,
                    Z = best.Z,
                    Width = bw,
                    Height = bh,
                    Depth = bd,
                    Index = block.Index
                };
                placed.Add(newBlock);
                atlasW = Math.Max(atlasW, best.X + bw);
                atlasH = Math.Max(atlasH, best.Y + bh);
                atlasD = Math.Max(atlasD, best.Z + bd);
                Interlocked.Increment(ref progress.Processed);
            }

            return new PackingResult { Success = true, AtlasWidth = atlasW, AtlasHeight = atlasH, AtlasDepth = atlasD, Placed = placed.ToArray() };
        }

        // Compares atlas blocks by padded voxel volume in descending order.
        private static int CompareBlocksByVolumeDescending(AtlasBlock a, AtlasBlock b) {
            long volumeA = (long)a.Width * a.Height * a.Depth;
            long volumeB = (long)b.Width * b.Height * b.Depth;
            return volumeB.CompareTo(volumeA);
        }

        // Adds a packing candidate once while preserving first-seen order.
        private static void AddUniqueCandidate(List<int> candidates, int value) {
            for (int i = 0; i < candidates.Count; i++) {
                if (candidates[i] == value) return;
            }
            candidates.Add(value);
        }

        // Searches all candidate positions for the next block using worker threads.
        private static PlacementSearchResult FindBestPlacement(AtlasPlacedBlock[] placed, List<int> xCandidates, List<int> yCandidates, List<int> zCandidates, int blockWidth, int blockHeight, int blockDepth, int atlasW, int atlasH, int atlasD, TexturePackingStrategy packingStrategy, CancellationToken cancellationToken) {
            PlacementSearchResult best = new PlacementSearchResult { Order = int.MaxValue, Volume = long.MaxValue, Depth = int.MaxValue };
            object bestLock = new object();
            int yCount = yCandidates.Count;
            int zCount = zCandidates.Count;
            ParallelOptions parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };

            Parallel.For(0, xCandidates.Count, parallelOptions, () => new PlacementSearchResult { Order = int.MaxValue, Volume = long.MaxValue, Depth = int.MaxValue }, (xIndex, loopState, localBest) => {
                int x = xCandidates[xIndex];
                for (int yIndex = 0; yIndex < yCount; yIndex++) {
                    int y = yCandidates[yIndex];
                    for (int zIndex = 0; zIndex < zCount; zIndex++) {
                        int z = zCandidates[zIndex];
                        int order = xIndex * yCount * zCount + yIndex * zCount + zIndex;

                        if (Collides(placed, x, y, z, blockWidth, blockHeight, blockDepth)) continue;

                        int newW = Math.Max(atlasW, x + blockWidth);
                        int newH = Math.Max(atlasH, y + blockHeight);
                        int newD = Math.Max(atlasD, z + blockDepth);
                        if (newW > maxAtlasSize || newH > maxAtlasSize || newD > maxAtlasSize) continue;

                        PlacementSearchResult candidate = new PlacementSearchResult {
                            Valid = true,
                            X = x,
                            Y = y,
                            Z = z,
                            Volume = (long)newW * newH * newD,
                            Depth = newD,
                            Order = order
                        };
                        if (IsBetterPlacement(candidate, localBest, packingStrategy)) localBest = candidate;
                    }
                }
                return localBest;
            }, localBest => {
                lock (bestLock) {
                    if (IsBetterPlacement(localBest, best, packingStrategy)) best = localBest;
                }
            });

            return best;
        }

        // Checks whether a candidate block overlaps any already placed block.
        private static bool Collides(AtlasPlacedBlock[] placed, int x, int y, int z, int width, int height, int depth) {
            for (int i = 0; i < placed.Length; i++) {
                AtlasPlacedBlock block = placed[i];
                if (x < block.X + block.Width && x + width > block.X && y < block.Y + block.Height && y + height > block.Y && z < block.Z + block.Depth && z + depth > block.Z) return true;
            }
            return false;
        }

        // Compares placement candidates while preserving first-candidate tie behavior.
        private static bool IsBetterPlacement(PlacementSearchResult candidate, PlacementSearchResult best, TexturePackingStrategy packingStrategy) {
            if (!candidate.Valid) return false;
            if (!best.Valid) return true;

            switch (packingStrategy) {
                case TexturePackingStrategy.MinimumDepth:
                    if (candidate.Depth != best.Depth) return candidate.Depth < best.Depth;
                    if (candidate.Volume != best.Volume) return candidate.Volume < best.Volume;
                    return candidate.Order < best.Order;
                default:
                    if (candidate.Volume != best.Volume) return candidate.Volume < best.Volume;
                    return candidate.Order < best.Order;
            }
        }

        // Writes all unique island pixels and padding into the final atlas pixel buffer.
        private static void WriteAtlasPixels(AtlasPlacedBlock[] placed, AtlasTextureData[] uniqueTexs, Color[] atlasPixels, Vector3[] uniqueBoundsMin, Vector3[] uniqueBoundsMax, int atlasW, int atlasH, int atlasD, int padding, ThreadProgress progress, CancellationToken cancellationToken) {
            int atlasSliceSize = atlasW * atlasH;
            ParallelOptions parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };

            Parallel.For(0, placed.Length, parallelOptions, i => {
                AtlasPlacedBlock placedBlock = placed[i];
                AtlasTextureData texture = uniqueTexs[placedBlock.Index];
                Color[] sourcePixels = texture.Pixels;
                int width = texture.Width;
                int height = texture.Height;
                int depth = texture.Depth;
                int sourceSliceSize = width * height;

                for (int z = 0; z < depth; z++) {
                    int sourceSlice = z * sourceSliceSize;
                    int atlasSlice = (placedBlock.Z + padding + z) * atlasSliceSize;
                    for (int y = 0; y < height; y++) {
                        int sourceIndex = sourceSlice + y * width;
                        int atlasIndex = placedBlock.X + padding + (placedBlock.Y + padding + y) * atlasW + atlasSlice;
                        Array.Copy(sourcePixels, sourceIndex, atlasPixels, atlasIndex, width);
                    }
                }

                for (int z = 0; z < depth; z++) {
                    int atlasSlice = (placedBlock.Z + padding + z) * atlasSliceSize;
                    for (int y = 0; y < height; y++) {
                        int row = (placedBlock.Y + padding + y) * atlasW + atlasSlice;
                        atlasPixels[placedBlock.X + row] = atlasPixels[placedBlock.X + padding + row];
                        atlasPixels[placedBlock.X + padding + width + row] = atlasPixels[placedBlock.X + padding + width - 1 + row];
                    }
                }

                for (int z = 0; z < depth; z++) {
                    int atlasSlice = (placedBlock.Z + padding + z) * atlasSliceSize;
                    int yMinRow = placedBlock.Y * atlasW + atlasSlice;
                    int ySourceMinRow = (placedBlock.Y + padding) * atlasW + atlasSlice;
                    int yMaxRow = (placedBlock.Y + padding + height) * atlasW + atlasSlice;
                    int ySourceMaxRow = (placedBlock.Y + padding + height - 1) * atlasW + atlasSlice;
                    for (int x = 0; x < width; x++) {
                        int xOffset = placedBlock.X + padding + x;
                        atlasPixels[xOffset + yMinRow] = atlasPixels[xOffset + ySourceMinRow];
                        atlasPixels[xOffset + yMaxRow] = atlasPixels[xOffset + ySourceMaxRow];
                    }
                }

                int zMinSlice = placedBlock.Z * atlasSliceSize;
                int zSourceMinSlice = (placedBlock.Z + padding) * atlasSliceSize;
                int zMaxSlice = (placedBlock.Z + padding + depth) * atlasSliceSize;
                int zSourceMaxSlice = (placedBlock.Z + padding + depth - 1) * atlasSliceSize;
                for (int y = 0; y < height; y++) {
                    int row = (placedBlock.Y + padding + y) * atlasW;
                    for (int x = 0; x < width; x++) {
                        int xOffset = placedBlock.X + padding + x;
                        atlasPixels[xOffset + row + zMinSlice] = atlasPixels[xOffset + row + zSourceMinSlice];
                        atlasPixels[xOffset + row + zMaxSlice] = atlasPixels[xOffset + row + zSourceMaxSlice];
                    }
                }

                uniqueBoundsMin[placedBlock.Index] = new Vector3(
                    (float)(placedBlock.X + padding) / atlasW,
                    (float)(placedBlock.Y + padding) / atlasH,
                    (float)(placedBlock.Z + padding) / atlasD);

                uniqueBoundsMax[placedBlock.Index] = new Vector3(
                    (float)(placedBlock.X + padding + width) / atlasW,
                    (float)(placedBlock.Y + padding + height) / atlasH,
                    (float)(placedBlock.Z + padding + depth) / atlasD);

                Interlocked.Increment(ref progress.Processed);
            });
        }

        // Waits for a worker task while allowing the editor coroutine to update progress.
        private static IEnumerator WaitForTask(Task task, Func<float> getProgress, Action<float> reportProgress) {
            while (!task.IsCompleted) {
                if (reportProgress != null) reportProgress(getProgress != null ? getProgress() : 0f);
                Thread.Yield();
                yield return null;
            }

            if (reportProgress != null) reportProgress(1f);
            if (task.IsFaulted) {
                Exception exception = task.Exception != null && task.Exception.InnerException != null ? task.Exception.InnerException : task.Exception;
                throw exception;
            }
        }

        // Returns thread-safe progress from a worker counter.
        private static float GetProgress(ThreadProgress progress) {
            if (progress == null || progress.Total <= 0) return 1f;
            int processed = Volatile.Read(ref progress.Processed);
            return processed >= progress.Total ? 1f : (float)processed / progress.Total;
        }

        // Returns combined thread-safe progress from three worker counters.
        private static float GetCombinedProgress(ThreadProgress a, ThreadProgress b, ThreadProgress c) {
            int total = a.Total + b.Total + c.Total;
            if (total <= 0) return 1f;
            int processed = Volatile.Read(ref a.Processed) + Volatile.Read(ref b.Processed) + Volatile.Read(ref c.Processed);
            return processed >= total ? 1f : (float)processed / total;
        }

        private class AtlasTextureData {
            public int Width;
            public int Height;
            public int Depth;
            public Color[] Pixels;

            // Stores dimensions and managed voxel data for one atlas island.
            public AtlasTextureData(int width, int height, int depth, Color[] pixels) {
                Width = width;
                Height = height;
                Depth = depth;
                Pixels = pixels;
            }
        }

        private class ThreadProgress {
            public int Processed;
            public int Total;
        }

        private struct DownscaleTextureResult {
            public int Width;
            public int Height;
            public int Depth;
            public Color[] Pixels;
        }

        private struct PostprocessTextureResult {
            public AtlasTextureData Texture0;
            public AtlasTextureData Texture1;
            public AtlasTextureData Texture2;
        }

        private struct AtlasBlock {
            public int Index;
            public int Width;
            public int Height;
            public int Depth;
        }

        private struct AtlasPlacedBlock {
            public int X;
            public int Y;
            public int Z;
            public int Width;
            public int Height;
            public int Depth;
            public int Index;
        }

        private struct PlacementSearchResult {
            public bool Valid;
            public int X;
            public int Y;
            public int Z;
            public long Volume;
            public int Depth;
            public int Order;
        }

        private struct PackingResult {
            public bool Success;
            public int AtlasWidth;
            public int AtlasHeight;
            public int AtlasDepth;
            public AtlasPlacedBlock[] Placed;
        }

        private struct AtlasTextureKey : IEquatable<AtlasTextureKey> {
            public ulong HashA;
            public ulong HashB;
            public int Width;
            public int Height;
            public int Depth;
            public int PixelCount;

            // Compares all fields that define texture content identity.
            public bool Equals(AtlasTextureKey other) {
                return HashA == other.HashA && HashB == other.HashB && Width == other.Width && Height == other.Height && Depth == other.Depth && PixelCount == other.PixelCount;
            }

            // Compares boxed atlas texture keys.
            public override bool Equals(object obj) {
                return obj is AtlasTextureKey other && Equals(other);
            }

            // Returns a combined dictionary hash for the texture key.
            public override int GetHashCode() {
                unchecked {
                    int hash = (int)HashA;
                    hash = (hash * 397) ^ (int)(HashA >> 32);
                    hash = (hash * 397) ^ (int)HashB;
                    hash = (hash * 397) ^ (int)(HashB >> 32);
                    hash = (hash * 397) ^ Width;
                    hash = (hash * 397) ^ Height;
                    hash = (hash * 397) ^ Depth;
                    hash = (hash * 397) ^ PixelCount;
                    return hash;
                }
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatUIntUnion {
            [FieldOffset(0)] public float FloatValue;
            [FieldOffset(0)] public uint UIntValue;
        }
    }
}
