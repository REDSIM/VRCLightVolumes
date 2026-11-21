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

    // How many packings the atlas packer tries in search of a smaller atlas.
    public enum AtlasPackingSearch {
        Fast,
        Default,
        Thorough,
    }

    public struct AtlasPackingSettings {
        public AtlasPackingSearch Search;
        // Ends each search stage once a quarter of its packings found no smaller atlas.
        public bool StopEarly;
        // Ends the search after this many seconds and keeps the best atlas found so far. 0 = no limit.
        public float MaxSeconds;
        // Packs on the CPU only. Off: the GPU searches when it supports compute shaders; the CPU packs otherwise, and whenever the GPU fails.
        public bool UseCpu;
        // When set, this pack skips the GPU and the packing log line gives this reason, in a few words (for example "Bakery is baking").
        public string SkipGpuReason;

        public static AtlasPackingSettings Default => new AtlasPackingSettings { Search = AtlasPackingSearch.Default, StopEarly = true };
    }

    public static partial class Texture3DAtlasGenerator {

        private const int maxAtlasSize = 2048;
        private const float vectorNormalizeEpsilon = 0.00001f;
        private const int progressStepsCount = 6;
        private const int downscaleProgressStep = 0;
        private const int colorProgressStep = 1;
        private const int deduplicateProgressStep = 2;
        private const int packingProgressStep = 3;
        private const int writingProgressStep = 4;
        private const int savingProgressStep = 5;
        private const int packingOrderCount = 4;
        private const int packingCapSamples = 16;
        private const int packingFineScanTops = 4;
        private const int packingFineScanRadius = 4;
        private const int packingUnboundedAxis = 3;
        private const int packingPopulation = 48;
        private const int packingElites = 10;
        private const int packingMutants = 8;
        private const float packingEliteInheritance = 0.7f;
        private const uint packingRandomSeed = 2463534242u;
        private const int packingPolishChains = 16;
        private const int packingPolishGridChains = 4;
        // Genetic search runs may end up to 1/20 above the best atlas before they stop.
        private const int packingSlackDivisor = 20;
        // With Stop Early, a search stage ends after a quarter of its budget without a smaller atlas.
        private const int packingPlateauDivisor = 4;
        // Packings the CPU search may try per search level (Fast, Default, Thorough), half in the genetic search, half in the polish.
        private static readonly int[] cpuSearchPackings = { 4800, 19200, 76800 };

        public static event Action<LightVolumeInstance[]> OnPreAtlasCreate;

        // Generates a packed 3D atlas for all baked Light Volumes and reports completion through the callback.
        public static IEnumerator CreateAtlas(LightVolumeInstance[] volumes, Action<Atlas3D> onComplete, int downscaleCount = 0, TexturePackingStrategy packingStrategy = TexturePackingStrategy.MinimumVRAM, AtlasPackingSettings? packingSettings = null) {

            List<Texture3D> temporaryTextures = new List<Texture3D>();
            CancellationTokenSource cancellationSource = new CancellationTokenSource();
            Texture3D pendingAtlasTexture = null;
            const int padding = 1;

#if UNITY_EDITOR
            int progressId = Progress.Start("Packing Light Volumes", "Preparing volume textures", Progress.Options.Sticky);
            Progress.RegisterCancelCallback(progressId, () => {
                Debug.Log("[LightVolumes] Atlas packing: cancelled. No new atlas was made; the previous atlas is kept.");
                cancellationSource.Cancel();
                return true;
            });
#endif

            try {

                if (volumes == null || volumes.Length == 0) {
                    Debug.LogError("[LightVolumes] No light volumes were provided for atlas generation!");
                    yield break;
                }
                if (onComplete == null) {
                    Debug.LogError("[LightVolumes] Atlas generation requires a completion callback to receive ownership of the generated texture.");
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
                        Debug.LogError("[LightVolumes] A Light Volume entry is missing. Check the Manager's list.");
                        yield break;
                    }

                    int textureIndex = i * 3;
                    bool reserveUVSpace = !volume.Bake && volume.ReserveUVSpace;
                    bool forceUnique = reserveUVSpace;
#if UNITY_EDITOR && !COMPILER_UDONSHARP
                    forceUnique |= !volume.AllowAtlasDeduplication;
#endif
                    if (forceUnique) {
                        forceUniqueTextures[textureIndex] = true;
                        forceUniqueTextures[textureIndex + 1] = true;
                        forceUniqueTextures[textureIndex + 2] = true;
                    }

                    if (reserveUVSpace) {
                        int w = GetReservedTextureSize(volume.Resolution.x, downscaleCount);
                        int h = GetReservedTextureSize(volume.Resolution.y, downscaleCount);
                        int d = GetReservedTextureSize(volume.Resolution.z, downscaleCount);
                        if (IsTextureSizeTooLargeForAtlas(w, h, d, padding) || GetReservedVoxelCount(w, h, d) < 0) {
                            string limit = IsTextureSizeTooLargeForAtlas(w, h, d, padding) ? $"plus {padding} voxel padding per side exceeds the atlas limit of {maxAtlasSize} on an axis"
                                : $"has more voxels than one texture can hold ({int.MaxValue:N0})";
                            Debug.LogError($"[LightVolumes] Reserved UV space for light volume \"{volume.gameObject.name}\" is too large: {w} x {h} x {d} {limit}. " +
                                "No new atlas was made; the previous atlas is kept. Lower the volume's reserved resolution.");
                            yield break;
                        }

                        texs[textureIndex] = CreateReservedTextureData(w, h, d, new Color(1, 1, 1, 0));
                        texs[textureIndex + 1] = CreateReservedTextureData(w, h, d, Color.clear);
                        texs[textureIndex + 2] = CreateReservedTextureData(w, h, d, Color.clear);
                        continue;
                    }

                    if (volume.Texture0 == null || volume.Texture1 == null || volume.Texture2 == null) {
                        Debug.LogError($"[LightVolumes] Light volume \"{volume.gameObject.name}\" is not baked!");
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

                        tex0 = CreateAndTrackDownscaledTexture3D(tex0, downscaleTask0.Result, temporaryTextures);
                        tex1 = CreateAndTrackDownscaledTexture3D(tex1, downscaleTask1.Result, temporaryTextures);
                        tex2 = CreateAndTrackDownscaledTexture3D(tex2, downscaleTask2.Result, temporaryTextures);

                        yield return null;
                    }

                    if (IsTextureSizeTooLargeForAtlas(tex0.width, tex0.height, tex0.depth, padding)) {
                        Debug.LogError($"[LightVolumes] Light volume \"{volume.gameObject.name}\" texture is too large for the atlas: {tex0.width} x {tex0.height} x {tex0.depth} " +
                            $"plus {padding} voxel padding per side exceeds the atlas limit of {maxAtlasSize} on an axis. No new atlas was made; the previous atlas is kept. " +
                            "Lower this volume's resolution or increase Downscale Volumes on the Light Volume Manager.");
                        yield break;
                    }

                    ThreadProgress postprocessProgress = new ThreadProgress { Total = tex0.depth };
                    float dark = -volume.Shadows * 0.5f;
                    float bright = 1 - volume.Highlights * 0.5f;
                    Task<PostprocessTextureResult> postprocessTask = StartPostprocessSphericalHarmonicsTask(tex0, tex1, tex2, dark, bright, volume.Exposure, postprocessProgress, cancellationSource.Token);
                    IEnumerator waitForPostprocessTask = WaitForTask(postprocessTask, () => (i + GetProgress(postprocessProgress)) / Mathf.Max(volumeCount, 1),
#if UNITY_EDITOR
                        progress => ReportProgress(progressId, colorProgressStep, progress, $"Adjusting volume colors {i + 1}/{volumeCount}")
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

                PackingOutput packing = new PackingOutput();
                IEnumerator waitForPacking = PackTextureBlocksAsync(blocks, padding, packingStrategy, packingSettings ?? AtlasPackingSettings.Default,
#if UNITY_EDITOR
                    (progress, message) => ReportProgress(progressId, packingProgressStep, progress, message),
#else
                    null,
#endif
                    cancellationSource.Token, packing);
                try {
                    while (waitForPacking.MoveNext()) yield return waitForPacking.Current;
                } finally {
                    (waitForPacking as IDisposable)?.Dispose();
                }
                // PackTextureBlocksAsync logs why no atlas could be made.
                if (!packing.Done || !packing.Result.Success) yield break;

                PackingResult packingResult = packing.Result;
                int atlasW = packingResult.AtlasWidth;
                int atlasH = packingResult.AtlasHeight;
                int atlasD = packingResult.AtlasDepth;

                // CreatePackingResult keeps the atlas within int.MaxValue voxels.
                ulong vCount = (ulong)atlasW * (ulong)atlasH * (ulong)atlasD;

                // Fill the atlas pixels on worker threads and map deduplicated bounds back to every source texture slot.
                Color[] atlasPixels = new Color[(int)vCount];
                Vector3[] uniqueBoundsMin = new Vector3[uniqueCount];
                Vector3[] uniqueBoundsMax = new Vector3[uniqueCount];
                ThreadProgress writingProgress = new ThreadProgress { Total = packingResult.Placed.Length };
                AtlasTextureData[] uniqueTextureArray = uniqueTexs.ToArray();
                Task writingTask = Task.Run(() => WriteAtlasPixels(packingResult.Placed, uniqueTextureArray, atlasPixels, uniqueBoundsMin, uniqueBoundsMax, atlasW, atlasH, atlasD, padding, writingProgress, cancellationSource.Token), cancellationSource.Token);
                IEnumerator waitForWritingTask = WaitForTask(writingTask, () => GetProgress(writingProgress),
#if UNITY_EDITOR
                    progress => ReportProgress(progressId, writingProgressStep, progress, $"Writing volume data ({Volatile.Read(ref writingProgress.Processed)}/{packingResult.Placed.Length})")
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

                pendingAtlasTexture = new Texture3D(atlasW, atlasH, atlasD, TextureFormat.RGBAHalf, false);
                pendingAtlasTexture.wrapMode = TextureWrapMode.Clamp;
                pendingAtlasTexture.filterMode = FilterMode.Trilinear;
                if (!LVUtils.Apply3DTextureData(pendingAtlasTexture, atlasPixels)) {
                    DestroyTransientTexture(pendingAtlasTexture);
                    pendingAtlasTexture = null;
                    yield break;
                }

                yield return null;

                onComplete.Invoke(new Atlas3D { Texture = pendingAtlasTexture, BoundsUvwMin = boundsMin, BoundsUvwMax = boundsMax });
                pendingAtlasTexture = null;

            } finally {
                try {
                    DestroyGeneratedTextures(pendingAtlasTexture, temporaryTextures);
                } finally {
                    try {
                        cancellationSource.Cancel();
                    } finally {
                        try {
                            cancellationSource.Dispose();
                        } finally {
#if UNITY_EDITOR
                            try {
                                Progress.Finish(progressId);
                            } finally {
                                Progress.Remove(progressId);
                            }
#endif
                        }
                    }
                }
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
                Debug.LogError($"[LightVolumes] Light volume \"{volume.gameObject.name}\" has unsupported texture format. Light Volume textures must use RGBAHalf, RGBAFloat, RGBA32 or ARGB32.");
                return false;
            }

            if (tex0.width != tex1.width || tex0.width != tex2.width || tex0.height != tex1.height || tex0.height != tex2.height || tex0.depth != tex1.depth || tex0.depth != tex2.depth) {
                Debug.LogError($"[LightVolumes] Light volume \"{volume.gameObject.name}\" has mismatched Texture3D dimensions.");
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

        // Destroys non-persistent temporary 3D textures created during atlas generation.
        private static void DestroyTemporaryTextures(List<Texture3D> textures) {
            if (textures == null) return;
            Exception firstException = null;
            for (int i = 0; i < textures.Count; i++) {
                Texture3D texture = textures[i];
                if (texture == null) continue;
                try {
                    DestroyTransientTexture(texture);
                } catch (Exception exception) {
                    if (firstException == null) firstException = exception;
                } finally {
                    textures[i] = null;
                }
            }
            if (firstException != null) throw firstException;
        }

        // Releases both the pending output and every downscale temporary even if one cleanup path fails.
        private static void DestroyGeneratedTextures(Texture3D pendingAtlasTexture, List<Texture3D> temporaryTextures) {
            try {
                DestroyTransientTexture(pendingAtlasTexture);
            } finally {
                DestroyTemporaryTextures(temporaryTextures);
            }
        }

        // Releases a generated texture according to the current Unity lifecycle without touching project assets.
        private static void DestroyTransientTexture(Texture3D texture) {
            if (texture == null || IsPersistentTexture(texture)) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }

        // Prevents atlas cleanup from destroying source assets while keeping the runtime build free of UnityEditor calls.
        private static bool IsPersistentTexture(Texture3D texture) {
#if UNITY_EDITOR
            return EditorUtility.IsPersistent(texture);
#else
            return false;
#endif
        }

#if UNITY_EDITOR
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
            try {
                texture.wrapMode = source.wrapMode;
                texture.filterMode = FilterMode.Trilinear;
                texture.anisoLevel = source.anisoLevel;
                texture.SetPixels(result.Pixels);
                texture.Apply();
                return texture;
            } catch {
                DestroyTransientTexture(texture);
                throw;
            }
        }

        // Registers native ownership atomically so a failed List growth cannot orphan the new texture.
        private static Texture3D CreateAndTrackDownscaledTexture3D(Texture3D source, DownscaleTextureResult result, List<Texture3D> temporaryTextures) {
            Texture3D texture = CreateDownscaledTexture3D(source, result);
            try {
                temporaryTextures.Add(texture);
                return texture;
            } catch {
                DestroyTransientTexture(texture);
                throw;
            }
        }

        // Downscales texture pixels with an 8-sample box filter.
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

        // Packs the blocks on the GPU when it can (see GetGpuPackingSkipReason), otherwise on the CPU; the CPU also packs if the GPU
        // fails or its layout does not pass the check. Yields while packing runs. output receives the result unless packing was
        // cancelled or the CPU packer threw. Logs one line per packed atlas (device, time, size, why not the GPU) and an error when no
        // atlas could be made.
        private static IEnumerator PackTextureBlocksAsync(AtlasBlock[] blocks, int padding, TexturePackingStrategy packingStrategy, AtlasPackingSettings packingSettings, Action<float, string> reportProgress, CancellationToken cancellationToken, PackingOutput output) {
            System.Diagnostics.Stopwatch packingTime = System.Diagnostics.Stopwatch.StartNew();
#if UNITY_EDITOR
            output.CpuReason = GetGpuPackingSkipReason(blocks, padding, packingStrategy, packingSettings, out PackingProblem problem, out ComputeShader shader, out int kernel, out output.CpuReasonShort);
            if (output.CpuReason == null) {
                IEnumerator gpuPacking = RunGpuPacking(problem, shader, kernel, blocks, padding, packingStrategy, packingSettings, reportProgress, cancellationToken, output, new GpuPackingOutcome());
                try {
                    while (gpuPacking.MoveNext()) yield return gpuPacking.Current;
                } finally {
                    (gpuPacking as IDisposable)?.Dispose();
                }
                if (cancellationToken.IsCancellationRequested) yield break;
                if (output.UsedGpu) {
                    output.Done = true;
                    LogPackedAtlas(output, packingTime.Elapsed.TotalSeconds);
                    yield break;
                }
            }
#endif
            // After a GPU attempt the CPU gets what is left of the time limit, but at least half a second to find an atlas.
            AtlasPackingSettings cpuSettings = packingSettings;
            if (cpuSettings.MaxSeconds > 0f) cpuSettings.MaxSeconds = Math.Max(0.5f, cpuSettings.MaxSeconds - (float)packingTime.Elapsed.TotalSeconds);
            ThreadProgress packingProgress = new ThreadProgress { Total = blocks.Length };
            Task<PackingResult> packingTask = Task.Run(() => PackTextureBlocks(blocks, padding, packingStrategy, cpuSettings, packingProgress, cancellationToken), cancellationToken);
            IEnumerator waitForPackingTask = WaitForTask(packingTask, () => GetProgress(packingProgress), reportProgress == null ? (Action<float>)null : progress => reportProgress(progress, $"Packing {blocks.Length} volume textures"));
            while (true) {
                bool running;
                try {
                    running = waitForPackingTask.MoveNext();
                } catch (Exception exception) {
                    Debug.LogError(FormatPackingLog("the CPU packer failed with an error, so no atlas was made.", $"{exception}",
                        "No new atlas was made. The previous atlas is kept.",
                        "Please report this log on the Light Volumes GitHub page. Changing the volumes (resolution, count or Downscale Volumes) may avoid the error until it is fixed.",
                        $"stage: CPU search; {DescribeBlocks(blocks, padding)}; strategy {packingStrategy}, search {cpuSettings.Search}, stop early {cpuSettings.StopEarly}, max time {cpuSettings.MaxSeconds:0.##} s"));
                    yield break;
                }
                if (!running) break;
                yield return waitForPackingTask.Current;
            }
            if (packingTask.IsCanceled) yield break;
            output.Result = packingTask.Result;
            output.Done = true;
            if (output.Result.Success) {
                LogPackedAtlas(output, packingTime.Elapsed.TotalSeconds);
            } else {
                Debug.LogError(FormatPackingLog("no atlas could be made: the volume textures do not fit in one atlas.", output.Result.Error,
                    "No new atlas was made. The previous atlas is kept.",
                    "Increase Downscale Volumes on the Light Volume Manager, lower the resolution of the largest volumes, or use fewer volumes.",
                    $"stage: CPU search; {DescribeBlocks(blocks, padding)}; strategy {packingStrategy}"));
            }
        }

        // Logs one line for a packed atlas: the device, the packing time, the atlas size and, after a CPU pack, why the GPU was not used.
        private static void LogPackedAtlas(PackingOutput output, double seconds) {
            PackingResult result = output.Result;
            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            string device = output.UsedGpu ? $"GPU ({SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType})" : "CPU";
            // The atlas is RGBA half: 8 bytes per voxel.
            double megabytes = (double)result.AtlasWidth * result.AtlasHeight * result.AtlasDepth * 8 / 1e6;
            string message = string.Format(invariant, "[LightVolumes] Atlas packed on the {0} in {1:0.0} s: {2} x {3} x {4}, {5:0.00} MB", device, seconds,
                result.AtlasWidth, result.AtlasHeight, result.AtlasDepth, megabytes);
            if (output.GpuFailed) message += $" (the GPU failed: {output.CpuReasonShort}; see the warning above)";
            else if (output.CpuReasonShort != null) message += $" ({output.CpuReasonShort})";
            message += ".";
            if (result.TimeLimitReached) message += " Max Packing Time ended the search, so the smallest atlas found by then is used.";
            Debug.Log(message);
        }

        // Packs unique texture blocks into the smallest atlas for the selected strategy: the 3D open-dimension packing problem,
        // without rotation (the three SH textures share one UVW transform). The search:
        //  1. Unbounded runs that keep the atlas volume smallest after every placement, one per block order. The best one bounds
        //     the capped runs, which stop as soon as they provably cannot beat it.
        //  2. Capped runs: two atlas axes get a cap between the largest block and three times it and the third (free) axis is kept
        //     shortest. Every free axis, every cap pair on a grid and every block order is tried, then every cap pair close to the
        //     best few.
        //  3. A biased random-key genetic algorithm (Goncalves & Resende 2013) seeded with the best runs. A chromosome holds one
        //     key per block for the packing order, two cap keys and a free-axis key. Runs may end up to 1/20 above the best atlas
        //     before they stop, so near misses still rank by size.
        //  4. Local search chains that swap or move blocks in the order and nudge the caps: most start from the genetic winner, a
        //     few from the best distinct capped runs, which the genetic search can miss.
        // The search level sets the number of packings steps 3 and 4 may try. With Stop Early a step also ends once a quarter of its
        // packings found no smaller atlas. Blocks go into empty maximal spaces (Lai & Chan 1997). Random numbers come from fixed seeds
        // and ties are broken by index, so the same blocks and settings give the same atlas on every machine unless a time limit ends
        // the search.
        private static PackingResult PackTextureBlocks(AtlasBlock[] blocks, int padding, TexturePackingStrategy packingStrategy, AtlasPackingSettings packingSettings, ThreadProgress progress, CancellationToken cancellationToken) {
            if (!TryCreatePackingProblem(blocks, padding, out PackingProblem problem)) return new PackingResult { Success = false, Error = DescribeOversizedBlock(blocks, padding) };
            if (problem.Count == 0) return new PackingResult { Success = true, Placed = new AtlasPlacedBlock[0] };
            PackingClock clock = new PackingClock(packingSettings.MaxSeconds);
            PackingCandidate best = SearchPacking(problem, packingStrategy, packingSettings, clock, progress, cancellationToken);
            Volatile.Write(ref progress.Processed, progress.Total);
            PackingResult result = CreatePackingResult(problem, best, packingStrategy);
            result.Packings = clock.Packings;
            result.TimeLimitReached = clock.LimitReached;
            return result;
        }

        // Collects padded block sizes, the largest size per axis and the four block orders. Fails if a padded block exceeds the atlas.
        private static bool TryCreatePackingProblem(AtlasBlock[] blocks, int padding, out PackingProblem problem) {
            int count = blocks.Length;
            problem = new PackingProblem { Count = count, Size = new int[count * 3], MaxSize = new int[3], Indices = new int[count], Orders = new int[packingOrderCount][] };
            for (int i = 0; i < count; i++) {
                AtlasBlock block = blocks[i];
                if (IsTextureSizeTooLargeForAtlas(block.Width, block.Height, block.Depth, padding)) return false;
                problem.Size[i * 3] = block.Width + padding * 2;
                problem.Size[i * 3 + 1] = block.Height + padding * 2;
                problem.Size[i * 3 + 2] = block.Depth + padding * 2;
                problem.Indices[i] = block.Index;
                problem.PaddedVolume += (long)problem.Size[i * 3] * problem.Size[i * 3 + 1] * problem.Size[i * 3 + 2];
                for (int axis = 0; axis < 3; axis++) problem.MaxSize[axis] = Math.Max(problem.MaxSize[axis], problem.Size[i * 3 + axis]);
            }
            for (int k = 0; k < packingOrderCount; k++) problem.Orders[k] = CreatePackingOrder(problem.Size, count, k);
            return true;
        }

        // Runs the CPU search and returns the best candidate; its result is invalid if no run fits the atlas.
        private static PackingCandidate SearchPacking(PackingProblem problem, TexturePackingStrategy packingStrategy, AtlasPackingSettings packingSettings, PackingClock clock, ThreadProgress progress, CancellationToken cancellationToken) {
            int level = GetPackingSearchLevel(packingSettings.Search);
            int searchPackings = cpuSearchPackings[level] / 2;
            int generations = Math.Max(1, searchPackings / (packingPopulation - packingElites));
            int polishIterations = Math.Max(1, searchPackings / packingPolishChains);
            int generationPlateau = packingSettings.StopEarly ? Math.Max(1, generations / packingPlateauDivisor) : int.MaxValue;
            int polishPlateau = packingSettings.StopEarly ? Math.Max(1, polishIterations / packingPlateauDivisor) : int.MaxValue;

            List<PackingRun> unbounded = new List<PackingRun>();
            for (int k = 0; k < packingOrderCount; k++) unbounded.Add(new PackingRun { FreeAxis = packingUnboundedAxis, Order = k, CapX = maxAtlasSize, CapY = maxAtlasSize });
            List<PackingRun> capped = CreateCappedPackingRuns(problem);
            int fineEstimate = packingFineScanTops * (2 * packingFineScanRadius + 1) * (2 * packingFineScanRadius + 1) * packingOrderCount;
            progress.Total = unbounded.Count + capped.Count + fineEstimate + packingPopulation + (generations - 1) * (packingPopulation - packingElites) + polishIterations * packingPolishChains;

            // 1. Unbounded runs give the bound used to stop hopeless capped runs early. They always run, so a time limit still
            //    leaves a valid atlas.
            PackingRunResult[] unboundedResults = EvaluatePackingRuns(unbounded, problem, packingStrategy, new PackingRunResult(), clock, true, progress, cancellationToken);
            int bestUnbounded = SelectBestPacking(unboundedResults, packingStrategy);
            if (bestUnbounded < 0) return new PackingCandidate();
            PackingRunResult bound = unboundedResults[bestUnbounded];

            // 2. Capped runs over the cap grid, then finer caps around the best capped runs.
            List<PackingRun> runs = new List<PackingRun>(unbounded);
            runs.AddRange(capped);
            List<PackingRunResult> results = new List<PackingRunResult>(unboundedResults);
            results.AddRange(EvaluatePackingRuns(capped, problem, packingStrategy, bound, clock, false, progress, cancellationToken));
            List<PackingRun> fine = CreateFinePackingRuns(runs, results.ToArray(), problem, packingStrategy);
            runs.AddRange(fine);
            results.AddRange(EvaluatePackingRuns(fine, problem, packingStrategy, results[SelectBestPacking(results.ToArray(), packingStrategy)], clock, false, progress, cancellationToken));
            Interlocked.Add(ref progress.Processed, Math.Max(0, fineEstimate - fine.Count));
            PackingRunResult[] resultArray = results.ToArray();
            int best = SelectBestPacking(resultArray, packingStrategy);

            // 3. Genetic search seeded with the best runs.
            PackingCandidate final = new PackingCandidate { Run = runs[best], Order = problem.Orders[runs[best].Order], Result = resultArray[best] };
            final = SearchRandomKeys(final, runs, resultArray, problem, packingStrategy, generations, generationPlateau, clock, progress, cancellationToken);

            // 4. Local search: most chains polish the genetic winner, the last ones the best distinct grid runs.
            List<int> top = SelectTopPackings(resultArray, runs, packingStrategy, packingPolishGridChains);
            PackingCandidate[] starts = new PackingCandidate[packingPolishChains];
            for (int chain = 0; chain < packingPolishChains; chain++) {
                int grid = chain - (packingPolishChains - packingPolishGridChains);
                starts[chain] = grid >= 0 && grid < top.Count ? new PackingCandidate { Run = runs[top[grid]], Order = problem.Orders[runs[top[grid]].Order], Result = resultArray[top[grid]] } : final;
            }
            return PolishPacking(final, starts, problem, packingStrategy, polishIterations, polishPlateau, clock, progress, cancellationToken);
        }

        // Decodes the winning candidate into atlas positions and checks the layout before it is used. On failure Error says why.
        private static PackingResult CreatePackingResult(PackingProblem problem, PackingCandidate candidate, TexturePackingStrategy packingStrategy) {
            if (!candidate.Result.Valid) return new PackingResult { Success = false, Error = DescribeNoLayout(problem) };
            EmptySpacePacker packer = new EmptySpacePacker(problem.Size, problem.Count, candidate.Run.FreeAxis);
            PackingRunResult packed = packer.Pack(candidate.Order, candidate.Run.CapX, candidate.Run.CapY, packingStrategy, new PackingRunResult());
            if (!packed.Valid) {
                return new PackingResult { Success = false, Error = $"Packing the best layout again on the CPU did not fit (free axis {candidate.Run.FreeAxis}, caps {candidate.Run.CapX} x {candidate.Run.CapY})." };
            }

            AtlasPlacedBlock[] placed = new AtlasPlacedBlock[problem.Count];
            for (int i = 0; i < problem.Count; i++) {
                placed[i] = new AtlasPlacedBlock {
                    X = packer.GetPosition(i, 0),
                    Y = packer.GetPosition(i, 1),
                    Z = packer.GetPosition(i, 2),
                    Width = problem.Size[i * 3],
                    Height = problem.Size[i * 3 + 1],
                    Depth = problem.Size[i * 3 + 2],
                    Index = problem.Indices[i]
                };
            }
            PackingResult result = new PackingResult { Success = true, AtlasWidth = packed.Width, AtlasHeight = packed.Height, AtlasDepth = packed.Depth, Placed = placed };
            string error = FindPackingError(result, problem);
            if (error != null) return new PackingResult { Success = false, Error = $"The layout check failed: {error}" };
            long voxels = (long)packed.Width * packed.Height * packed.Depth;
            if (voxels > int.MaxValue) {
                return new PackingResult { Success = false, Error = $"The best atlas found is {packed.Width} x {packed.Height} x {packed.Depth} = {voxels:N0} voxels, more than one atlas texture can hold ({int.MaxValue:N0})." };
            }
            return result;
        }

        // Checks that every block is placed once with its padded size, inside an atlas within the size limit, and that no two blocks overlap.
        private static bool IsValidPacking(PackingResult result, PackingProblem problem) {
            return FindPackingError(result, problem) == null;
        }

        // Returns the first problem of a layout (which block, where and why), or null if it passes the check of IsValidPacking.
        // Sizes include the padding, so an overlap of padded blocks also means broken padding.
        private static string FindPackingError(PackingResult result, PackingProblem problem) {
            if (!result.Success || result.Placed == null) return "the layout has no blocks.";
            if (result.Placed.Length != problem.Count) return $"the layout has {result.Placed.Length} blocks, expected {problem.Count}.";
            string atlas = $"{result.AtlasWidth} x {result.AtlasHeight} x {result.AtlasDepth}";
            if (result.AtlasWidth > maxAtlasSize || result.AtlasHeight > maxAtlasSize || result.AtlasDepth > maxAtlasSize) return $"the atlas {atlas} is larger than {maxAtlasSize} on an axis.";
            HashSet<int> indices = new HashSet<int>();
            for (int i = 0; i < result.Placed.Length; i++) {
                AtlasPlacedBlock a = result.Placed[i];
                string block = $"block {i} (texture {a.Index}, {a.Width} x {a.Height} x {a.Depth} with padding, at {a.X}, {a.Y}, {a.Z})";
                if (a.Index != problem.Indices[i] || !indices.Add(a.Index)) return $"{block} has the wrong texture index or is placed twice.";
                if (a.Width != problem.Size[i * 3] || a.Height != problem.Size[i * 3 + 1] || a.Depth != problem.Size[i * 3 + 2]) {
                    return $"{block} should be {problem.Size[i * 3]} x {problem.Size[i * 3 + 1]} x {problem.Size[i * 3 + 2]} with padding.";
                }
                if (a.X < 0 || a.Y < 0 || a.Z < 0 || a.X + a.Width > result.AtlasWidth || a.Y + a.Height > result.AtlasHeight || a.Z + a.Depth > result.AtlasDepth) {
                    return $"{block} lies outside the {atlas} atlas.";
                }
                for (int j = 0; j < i; j++) {
                    AtlasPlacedBlock b = result.Placed[j];
                    if (a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height && a.Z < b.Z + b.Depth && b.Z < a.Z + a.Depth) {
                        return $"{block} overlaps block {j} (texture {b.Index}, {b.Width} x {b.Height} x {b.Depth} at {b.X}, {b.Y}, {b.Z}).";
                    }
                }
            }
            return null;
        }

        // Describes the blocks for logs: count, padded voxels and the largest padded size per axis.
        private static string DescribeBlocks(AtlasBlock[] blocks, int padding) {
            long voxels = 0;
            int maxX = 0, maxY = 0, maxZ = 0;
            foreach (AtlasBlock block in blocks) {
                voxels += (long)(block.Width + padding * 2) * (block.Height + padding * 2) * (block.Depth + padding * 2);
                maxX = Math.Max(maxX, block.Width + padding * 2);
                maxY = Math.Max(maxY, block.Height + padding * 2);
                maxZ = Math.Max(maxZ, block.Depth + padding * 2);
            }
            return $"{blocks.Length} unique volume textures, {voxels:N0} voxels with padding, largest {maxX} x {maxY} x {maxZ} with padding";
        }

        // Explains why no layout fits: how much space the blocks need at least, against the atlas limit.
        private static string DescribeNoLayout(PackingProblem problem) {
            double cube = Math.Pow(problem.PaddedVolume, 1.0 / 3.0);
            return $"No layout of the {problem.Count} unique volume textures fits within {maxAtlasSize} voxels per axis. Even a perfect packing needs " +
                $"{problem.PaddedVolume:N0} voxels (a cube of about {cube:0} per side); the largest volume texture is {problem.MaxSize[0]} x {problem.MaxSize[1]} x " +
                $"{problem.MaxSize[2]} with padding.";
        }

        // Names the first block that is larger than the atlas on an axis with its padding.
        private static string DescribeOversizedBlock(AtlasBlock[] blocks, int padding) {
            string[] axes = { "X (width)", "Y (height)", "Z (depth)" };
            foreach (AtlasBlock block in blocks) {
                int[] size = { block.Width + padding * 2, block.Height + padding * 2, block.Depth + padding * 2 };
                for (int axis = 0; axis < 3; axis++) {
                    if (size[axis] > maxAtlasSize) return $"Volume texture {block.Index} is {size[0]} x {size[1]} x {size[2]} with padding: {size[axis]} on the {axes[axis]} axis, more than the atlas limit of {maxAtlasSize}.";
                }
            }
            return "A volume texture is larger than the atlas limit.";
        }

        // Formats an atlas packing log: a one-line headline, then the reason, what happened, what the user can do, and details for a bug
        // report (stage, blocks, graphics device and Unity version).
        private static string FormatPackingLog(string headline, string reason, string result, string action, string details) {
            return $"[LightVolumes] Atlas packing: {headline}\nReason: {reason}\nResult: {result}\nWhat to do: {action}\n" +
                $"Details: {details}. Graphics: {DescribeGraphicsDevice()}. Unity {Application.unityVersion}, {SystemInfo.operatingSystem}.";
        }

        private static string DescribeGraphicsDevice() {
            return $"{SystemInfo.graphicsDeviceName} (vendor {SystemInfo.graphicsDeviceVendor}, {SystemInfo.graphicsMemorySize} MB), {SystemInfo.graphicsDeviceType}, " +
                $"version {SystemInfo.graphicsDeviceVersion}, compute shaders {(SystemInfo.supportsComputeShaders ? "supported" : "not supported")}";
        }

        // Maps a search setting to the index of its budget.
        private static int GetPackingSearchLevel(AtlasPackingSearch search) {
            return Math.Max(0, Math.Min(2, (int)search));
        }

        // Biased random-key genetic algorithm. Elite chromosomes survive, mutants are fresh random keys and the rest are children of an
        // elite and a non-elite parent that take each gene from the elite parent with fixed probability.
        private static PackingCandidate SearchRandomKeys(PackingCandidate best, List<PackingRun> runs, PackingRunResult[] results, PackingProblem problem, TexturePackingStrategy packingStrategy, int generations, int plateau, PackingClock clock, ThreadProgress progress, CancellationToken cancellationToken) {
            int count = problem.Count;
            int genes = count + 3;
            uint state = packingRandomSeed;
            float[][] population = new float[packingPopulation][];
            List<int> seeds = SelectTopPackings(results, runs, packingStrategy, packingPopulation / 4);
            for (int p = 0; p < packingPopulation; p++) {
                float[] keys = new float[genes];
                for (int g = 0; g < genes; g++) keys[g] = NextPackingKey(ref state);
                if (p < seeds.Count) EncodePackingRun(keys, runs[seeds[p]], problem.Orders[runs[seeds[p]].Order], problem.MaxSize, count);
                population[p] = keys;
            }

            PackingCandidate[] decoded = new PackingCandidate[packingPopulation];
            PackingCandidate[] reordered = new PackingCandidate[packingPopulation];
            int[] rank = new int[packingPopulation];
            float[][] next = new float[packingPopulation][];
            int sinceImprovement = 0;
            ParallelOptions parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };
            for (int generation = 0; generation < generations; generation++) {
                if (sinceImprovement >= plateau || clock.Expired) {
                    Interlocked.Add(ref progress.Processed, (generations - generation) * (packingPopulation - packingElites));
                    break;
                }
                int first = generation == 0 ? 0 : packingElites;
                PackingRunResult generationBound = GetSlackBound(best.Result, packingStrategy);
                float[][] generationPopulation = population;
                PackingCandidate[] generationDecoded = decoded;
                Parallel.For(first, packingPopulation, parallelOptions, () => new EmptySpacePacker[3], (p, loopState, packers) => {
                    generationDecoded[p] = DecodePackingKeys(generationPopulation[p], problem, packers, packingStrategy, generationBound);
                    Interlocked.Increment(ref progress.Processed);
                    Interlocked.Increment(ref clock.Packings);
                    return packers;
                }, packers => { });

                for (int p = 0; p < packingPopulation; p++) rank[p] = p;
                Array.Sort(rank, (a, b) => {
                    if (IsBetterPacking(generationDecoded[a].Result, generationDecoded[b].Result, packingStrategy)) return -1;
                    if (IsBetterPacking(generationDecoded[b].Result, generationDecoded[a].Result, packingStrategy)) return 1;
                    return a.CompareTo(b);
                });
                sinceImprovement++;
                if (IsBetterPacking(decoded[rank[0]].Result, best.Result, packingStrategy)) {
                    best = decoded[rank[0]];
                    sinceImprovement = 0;
                }

                for (int p = 0; p < packingElites; p++) {
                    next[p] = population[rank[p]];
                    reordered[p] = decoded[rank[p]];
                }
                for (int p = packingElites; p < packingPopulation - packingMutants; p++) {
                    float[] elite = population[rank[(int)(NextPackingRandom(ref state) % packingElites)]];
                    float[] other = population[rank[packingElites + (int)(NextPackingRandom(ref state) % (uint)(packingPopulation - packingElites))]];
                    float[] child = new float[genes];
                    for (int g = 0; g < genes; g++) child[g] = NextPackingKey(ref state) < packingEliteInheritance ? elite[g] : other[g];
                    next[p] = child;
                }
                for (int p = packingPopulation - packingMutants; p < packingPopulation; p++) {
                    float[] mutant = new float[genes];
                    for (int g = 0; g < genes; g++) mutant[g] = NextPackingKey(ref state);
                    next[p] = mutant;
                }
                float[][] swapPopulation = population;
                population = next;
                next = swapPopulation;
                PackingCandidate[] swapDecoded = decoded;
                decoded = reordered;
                reordered = swapDecoded;
            }
            return best;
        }

        // Local search chains, one per start, with the start's free axis fixed. Each step nudges a cap by 1 to 3, swaps adjacent or
        // random blocks, or moves one block to another place in the order, and keeps every move that does not grow the atlas; a chain
        // ends early after plateau steps without a smaller atlas. Chains have fixed seeds; the best result wins, the lowest chain on ties.
        private static PackingCandidate PolishPacking(PackingCandidate best, PackingCandidate[] starts, PackingProblem problem, TexturePackingStrategy packingStrategy, int iterations, int plateau, PackingClock clock, ThreadProgress progress, CancellationToken cancellationToken) {
            int count = problem.Count;
            PackingCandidate[] polished = new PackingCandidate[starts.Length];
            ParallelOptions parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };
            Parallel.For(0, starts.Length, parallelOptions, chain => {
                PackingCandidate start = starts[chain];
                EmptySpacePacker packer = new EmptySpacePacker(problem.Size, count, start.Run.FreeAxis);
                int axisX, axisY, axisZ;
                GetPackingFrameAxes(start.Run.FreeAxis, out axisX, out axisY, out axisZ);
                PackingRun run = start.Run;
                int[] order = (int[])start.Order.Clone();
                int[] trialOrder = new int[count];
                PackingRunResult result = start.Result;
                uint state = packingRandomSeed ^ (uint)((chain + 1) * 2654435761u);
                int sinceImprovement = 0;
                int iteration = 0;
                for (; iteration < iterations && sinceImprovement < plateau && !clock.Expired; iteration++) {
                    cancellationToken.ThrowIfCancellationRequested();
                    Array.Copy(order, trialOrder, count);
                    PackingRun trialRun = run;
                    uint move = NextPackingRandom(ref state) % 8;
                    int first = (int)(NextPackingRandom(ref state) % (uint)count);
                    int second = move == 1 || move == 2 ? Math.Min(count - 1, first + 1) : (int)(NextPackingRandom(ref state) % (uint)count);
                    if (move == 0) {
                        int delta = (int)(NextPackingRandom(ref state) % 3) + 1;
                        if ((NextPackingRandom(ref state) & 1) != 0) delta = -delta;
                        if ((first & 1) == 0) trialRun.CapX = Math.Max(problem.MaxSize[axisX], Math.Min(maxAtlasSize, run.CapX + delta));
                        else trialRun.CapY = Math.Max(problem.MaxSize[axisY], Math.Min(maxAtlasSize, run.CapY + delta));
                    } else if (move <= 4) {
                        int swap = trialOrder[first];
                        trialOrder[first] = trialOrder[second];
                        trialOrder[second] = swap;
                    } else {
                        // Moves one block to another place in the order.
                        int moved = trialOrder[first];
                        if (first < second) Array.Copy(order, first + 1, trialOrder, first, second - first);
                        else Array.Copy(order, second, trialOrder, second + 1, first - second);
                        trialOrder[second] = moved;
                    }
                    PackingRunResult trial = packer.Pack(trialOrder, trialRun.CapX, trialRun.CapY, packingStrategy, result);
                    sinceImprovement++;
                    if (trial.Valid && !IsBetterPacking(result, trial, packingStrategy)) {
                        if (IsBetterPacking(trial, result, packingStrategy)) sinceImprovement = 0;
                        result = trial;
                        run = trialRun;
                        Array.Copy(trialOrder, order, count);
                    }
                    Interlocked.Increment(ref progress.Processed);
                    Interlocked.Increment(ref clock.Packings);
                }
                Interlocked.Add(ref progress.Processed, iterations - iteration);
                polished[chain] = new PackingCandidate { Run = run, Order = order, Result = result };
            });
            for (int chain = 0; chain < polished.Length; chain++) {
                if (IsBetterPacking(polished[chain].Result, best.Result, packingStrategy)) best = polished[chain];
            }
            return best;
        }

        // Writes a run's caps, free axis and block order into random keys.
        private static void EncodePackingRun(float[] keys, PackingRun run, int[] order, int[] maxSize, int count) {
            int axisX, axisY, axisZ;
            GetPackingFrameAxes(run.FreeAxis, out axisX, out axisY, out axisZ);
            for (int i = 0; i < count; i++) keys[order[i]] = (i + 0.5f) / count;
            keys[count] = PackingCapKey(run.CapX, maxSize[axisX]);
            keys[count + 1] = PackingCapKey(run.CapY, maxSize[axisY]);
            keys[count + 2] = (run.FreeAxis + 0.5f) / 3f;
        }

        // Maps a cap to a key in [0, 1) over the cap range [largest block, three times the largest block].
        private static float PackingCapKey(int cap, int largest) {
            float key = (cap - largest) / (2f * largest);
            return Math.Max(0f, Math.Min(0.9999f, key));
        }

        // Decodes random keys: keys 0..n-1 give the block order, then two cap keys and the free-axis key.
        private static PackingCandidate DecodePackingKeys(float[] keys, PackingProblem problem, EmptySpacePacker[] packers, TexturePackingStrategy packingStrategy, PackingRunResult bound) {
            int count = problem.Count;
            int freeAxis = Math.Min(2, (int)(keys[count + 2] * 3f));
            int axisX, axisY, axisZ;
            GetPackingFrameAxes(freeAxis, out axisX, out axisY, out axisZ);
            int capX = Math.Min(maxAtlasSize, problem.MaxSize[axisX] + (int)(keys[count] * 2f * problem.MaxSize[axisX]));
            int capY = Math.Min(maxAtlasSize, problem.MaxSize[axisY] + (int)(keys[count + 1] * 2f * problem.MaxSize[axisY]));
            int[] order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            Array.Sort(order, (a, b) => {
                int c = keys[a].CompareTo(keys[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
            EmptySpacePacker packer = packers[freeAxis] ?? (packers[freeAxis] = new EmptySpacePacker(problem.Size, count, freeAxis));
            PackingRunResult result = packer.Pack(order, capX, capY, packingStrategy, bound);
            return new PackingCandidate { Run = new PackingRun { FreeAxis = freeAxis, CapX = capX, CapY = capY }, Order = order, Result = result };
        }

        // Builds the cap grid for every free axis: caps run from the largest block to three times it on each capped axis.
        private static List<PackingRun> CreateCappedPackingRuns(PackingProblem problem) {
            int[] maxSize = problem.MaxSize;
            List<PackingRun> runs = new List<PackingRun>();
            for (int freeAxis = 0; freeAxis < 3; freeAxis++) {
                int axisX, axisY, axisZ;
                GetPackingFrameAxes(freeAxis, out axisX, out axisY, out axisZ);
                int minX = maxSize[axisX], maxX = Math.Min(maxAtlasSize, maxSize[axisX] * 3);
                int minY = maxSize[axisY], maxY = Math.Min(maxAtlasSize, maxSize[axisY] * 3);
                int stepX = Math.Max(1, (maxX - minX) / packingCapSamples);
                int stepY = Math.Max(1, (maxY - minY) / packingCapSamples);
                for (int capX = minX; capX <= maxX; capX += stepX) {
                    for (int capY = minY; capY <= maxY; capY += stepY) {
                        if ((long)capX * capY * maxAtlasSize < problem.PaddedVolume) continue;
                        for (int k = 0; k < packingOrderCount; k++) runs.Add(new PackingRun { FreeAxis = freeAxis, Order = k, CapX = capX, CapY = capY });
                    }
                }
            }
            return runs;
        }

        // Every cap pair within packingFineScanRadius of the best distinct capped runs, every block order each.
        private static List<PackingRun> CreateFinePackingRuns(List<PackingRun> runs, PackingRunResult[] results, PackingProblem problem, TexturePackingStrategy packingStrategy) {
            List<PackingRun> fine = new List<PackingRun>();
            HashSet<long> seen = new HashSet<long>();
            for (int i = 0; i < runs.Count; i++) seen.Add(GetPackingCapsKey(runs[i].FreeAxis, runs[i].CapX, runs[i].CapY));
            List<int> top = SelectTopPackings(results, runs, packingStrategy, packingFineScanTops);
            for (int t = 0; t < top.Count; t++) {
                PackingRun winner = runs[top[t]];
                int axisX, axisY, axisZ;
                GetPackingFrameAxes(winner.FreeAxis, out axisX, out axisY, out axisZ);
                for (int capX = Math.Max(problem.MaxSize[axisX], winner.CapX - packingFineScanRadius); capX <= Math.Min(maxAtlasSize, winner.CapX + packingFineScanRadius); capX++) {
                    for (int capY = Math.Max(problem.MaxSize[axisY], winner.CapY - packingFineScanRadius); capY <= Math.Min(maxAtlasSize, winner.CapY + packingFineScanRadius); capY++) {
                        if (!seen.Add(GetPackingCapsKey(winner.FreeAxis, capX, capY))) continue;
                        for (int k = 0; k < packingOrderCount; k++) fine.Add(new PackingRun { FreeAxis = winner.FreeAxis, Order = k, CapX = capX, CapY = capY });
                    }
                }
            }
            return fine;
        }

        // Identifies a free axis and cap pair.
        private static long GetPackingCapsKey(int freeAxis, int capX, int capY) {
            return ((long)freeAxis << 32) | ((long)capX << 16) | (uint)capY;
        }

        // Packs every run in parallel; results are stored by run index so the outcome does not depend on thread scheduling. Unless
        // finishAll is set, runs left when the clock expires are skipped.
        private static PackingRunResult[] EvaluatePackingRuns(List<PackingRun> runs, PackingProblem problem, TexturePackingStrategy packingStrategy, PackingRunResult bound, PackingClock clock, bool finishAll, ThreadProgress progress, CancellationToken cancellationToken) {
            PackingRunResult[] results = new PackingRunResult[runs.Count];
            ParallelOptions parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };
            Parallel.For(0, runs.Count, parallelOptions, () => new EmptySpacePacker[packingUnboundedAxis + 1], (r, loopState, packers) => {
                if (finishAll || !clock.Expired) {
                    PackingRun run = runs[r];
                    EmptySpacePacker packer = packers[run.FreeAxis] ?? (packers[run.FreeAxis] = new EmptySpacePacker(problem.Size, problem.Count, run.FreeAxis));
                    results[r] = packer.Pack(problem.Orders[run.Order], run.CapX, run.CapY, packingStrategy, bound);
                    Interlocked.Increment(ref clock.Packings);
                }
                Interlocked.Increment(ref progress.Processed);
                return packers;
            }, packers => { });
            return results;
        }

        // Returns the index of the best valid result, the lowest index on ties.
        private static int SelectBestPacking(PackingRunResult[] results, TexturePackingStrategy packingStrategy) {
            int best = -1;
            for (int i = 0; i < results.Length; i++) {
                if (!results[i].Valid) continue;
                if (best < 0 || IsBetterPacking(results[i], results[best], packingStrategy)) best = i;
            }
            return best;
        }

        // Returns the best capped results with distinct free axis and caps, in rank order.
        private static List<int> SelectTopPackings(PackingRunResult[] results, List<PackingRun> runs, TexturePackingStrategy packingStrategy, int limit) {
            List<int> top = new List<int>(limit);
            while (top.Count < limit) {
                int best = -1;
                for (int i = 0; i < results.Length; i++) {
                    if (!results[i].Valid || runs[i].FreeAxis == packingUnboundedAxis || (best >= 0 && !IsBetterPacking(results[i], results[best], packingStrategy))) continue;
                    bool taken = false;
                    for (int t = 0; t < top.Count && !taken; t++) {
                        PackingRun a = runs[top[t]];
                        PackingRun b = runs[i];
                        taken = a.FreeAxis == b.FreeAxis && a.CapX == b.CapX && a.CapY == b.CapY;
                    }
                    if (!taken) best = i;
                }
                if (best < 0) break;
                top.Add(best);
            }
            return top;
        }

        // Compares finished atlases by the selected strategy; an invalid result is never better.
        private static bool IsBetterPacking(PackingRunResult candidate, PackingRunResult best, TexturePackingStrategy packingStrategy) {
            if (!candidate.Valid) return false;
            if (!best.Valid) return true;
            if (packingStrategy == TexturePackingStrategy.MinimumDepth && candidate.Depth != best.Depth) return candidate.Depth < best.Depth;
            if (candidate.Volume != best.Volume) return candidate.Volume < best.Volume;
            return candidate.Depth < best.Depth;
        }

        // Lets genetic search runs finish slightly above the best atlas, so near misses still rank by size.
        private static PackingRunResult GetSlackBound(PackingRunResult best, TexturePackingStrategy packingStrategy) {
            if (!best.Valid) return best;
            best.Volume += best.Volume / packingSlackDivisor;
            if (packingStrategy == TexturePackingStrategy.MinimumDepth) best.Depth += Math.Max(1, best.Depth / packingSlackDivisor);
            return best;
        }

        // Builds one deterministic block order: 0 volume, 1 longest side, 2 sum of sides, 3 depth then cross-section.
        private static int[] CreatePackingOrder(int[] size, int count, int kind) {
            long[] primary = new long[count];
            long[] secondary = new long[count];
            for (int i = 0; i < count; i++) {
                long w = size[i * 3], h = size[i * 3 + 1], d = size[i * 3 + 2];
                switch (kind) {
                    case 0: primary[i] = w * h * d; secondary[i] = Math.Max(w, Math.Max(h, d)); break;
                    case 1: primary[i] = Math.Max(w, Math.Max(h, d)); secondary[i] = w * h * d; break;
                    case 2: primary[i] = w + h + d; secondary[i] = w * h * d; break;
                    default: primary[i] = d; secondary[i] = w * h; break;
                }
            }
            int[] order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            Array.Sort(order, (a, b) => {
                int c = primary[b].CompareTo(primary[a]);
                if (c != 0) return c;
                c = secondary[b].CompareTo(secondary[a]);
                if (c != 0) return c;
                for (int axis = 0; axis < 3; axis++) {
                    c = size[b * 3 + axis].CompareTo(size[a * 3 + axis]);
                    if (c != 0) return c;
                }
                return a.CompareTo(b);
            });
            return order;
        }

        // Maps a free atlas axis to the packing frame, whose z axis is the free one (unbounded runs keep the atlas frame).
        private static void GetPackingFrameAxes(int freeAxis, out int axisX, out int axisY, out int axisZ) {
            switch (freeAxis) {
                case 0: axisX = 2; axisY = 1; axisZ = 0; break;
                case 1: axisX = 0; axisY = 2; axisZ = 1; break;
                default: axisX = 0; axisY = 1; axisZ = 2; break;
            }
        }

        // Xorshift32 step of the fixed-seed packing search.
        private static uint NextPackingRandom(ref uint state) {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        // Uniform key in [0, 1) from the xorshift state.
        private static float NextPackingKey(ref uint state) {
            return (NextPackingRandom(ref state) >> 8) * (1f / 16777216f);
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

        private struct PackingRun {
            public int FreeAxis;
            public int Order;
            public int CapX;
            public int CapY;
        }

        private struct PackingRunResult {
            public bool Valid;
            public int Width;
            public int Height;
            public int Depth;
            public long Volume;
        }

        private struct PackingCandidate {
            public PackingRun Run;
            public int[] Order;
            public PackingRunResult Result;
        }

        private sealed class PackingProblem {
            public int Count;
            public int[] Size;
            public int[] MaxSize;
            public int[] Indices;
            public int[][] Orders;
            public long PaddedVolume;
        }

        // Wall clock of one packing search against the optional time limit, and the number of packings the search tried.
        private sealed class PackingClock {
            private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
            private readonly double _limitSeconds;
            public int Packings;

            // A limit of 0 or less never expires.
            public PackingClock(float maxSeconds) {
                _limitSeconds = maxSeconds > 0f ? maxSeconds : double.PositiveInfinity;
            }

            // Set once a check found the time limit reached, so the search stopped early.
            public bool LimitReached;

            public bool Expired {
                get {
                    if (_stopwatch.Elapsed.TotalSeconds < _limitSeconds) return false;
                    LimitReached = true;
                    return true;
                }
            }
        }

        private struct EmptySpace {
            public int X1;
            public int Y1;
            public int Z1;
            public int X2;
            public int Y2;
            public int Z2;
            public bool Dropped;
        }

        // Places blocks into empty maximal spaces in a frame whose z axis is the free one; positions and extents map back to atlas axes.
        private sealed class EmptySpacePacker {
            private readonly bool _minimumVolume;
            private readonly int[] _frameToAtlas = new int[3];
            private readonly int[] _sizeX, _sizeY, _sizeZ;
            private readonly int[] _posX, _posY, _posZ;
            private readonly int[] _minX, _minY, _minZ;
            private readonly List<EmptySpace> _spaces = new List<EmptySpace>();
            private readonly List<EmptySpace> _created = new List<EmptySpace>();
            private int _extentX, _extentY, _extentZ;

            // Prepares per-block sizes in the frame of the given free axis (packingUnboundedAxis = minimise the atlas volume).
            public EmptySpacePacker(int[] size, int count, int freeAxis) {
                _minimumVolume = freeAxis == packingUnboundedAxis;
                GetPackingFrameAxes(freeAxis, out _frameToAtlas[0], out _frameToAtlas[1], out _frameToAtlas[2]);
                _sizeX = new int[count];
                _sizeY = new int[count];
                _sizeZ = new int[count];
                for (int i = 0; i < count; i++) {
                    _sizeX[i] = size[i * 3 + _frameToAtlas[0]];
                    _sizeY[i] = size[i * 3 + _frameToAtlas[1]];
                    _sizeZ[i] = size[i * 3 + _frameToAtlas[2]];
                }
                _posX = new int[count];
                _posY = new int[count];
                _posZ = new int[count];
                _minX = new int[count + 1];
                _minY = new int[count + 1];
                _minZ = new int[count + 1];
            }

            // Returns a block's position on an atlas axis (0 = x, 1 = y, 2 = z) after the last Pack.
            public int GetPosition(int block, int atlasAxis) {
                if (_frameToAtlas[0] == atlasAxis) return _posX[block];
                if (_frameToAtlas[1] == atlasAxis) return _posY[block];
                return _posZ[block];
            }

            // Places blocks in order inside capX x capY, each into the space that keeps the atlas smallest. Invalid if a block does not
            // fit; stopped once even a perfect packing of the remaining blocks could not beat bound.
            public PackingRunResult Pack(int[] order, int capX, int capY, TexturePackingStrategy packingStrategy, PackingRunResult bound) {
                int count = order.Length;
                _minX[count] = _minY[count] = _minZ[count] = int.MaxValue;
                long remaining = 0;
                for (int i = count - 1; i >= 0; i--) {
                    int block = order[i];
                    _minX[i] = Math.Min(_minX[i + 1], _sizeX[block]);
                    _minY[i] = Math.Min(_minY[i + 1], _sizeY[block]);
                    _minZ[i] = Math.Min(_minZ[i + 1], _sizeZ[block]);
                    remaining += (long)_sizeX[block] * _sizeY[block] * _sizeZ[block];
                }
                long placedVolume = 0;
                long crossSection = (long)capX * capY;
                _spaces.Clear();
                _spaces.Add(new EmptySpace { X2 = capX, Y2 = capY, Z2 = maxAtlasSize });
                _extentX = _extentY = _extentZ = 0;

                for (int orderIndex = 0; orderIndex < count; orderIndex++) {
                    int block = order[orderIndex];
                    int w = _sizeX[block], h = _sizeY[block], d = _sizeZ[block];
                    int bestX = -1, bestY = 0, bestZ = 0;
                    long bestKey = long.MaxValue;
                    for (int spaceIndex = 0; spaceIndex < _spaces.Count; spaceIndex++) {
                        EmptySpace space = _spaces[spaceIndex];
                        if (space.X2 - space.X1 < w || space.Y2 - space.Y1 < h || space.Z2 - space.Z1 < d) continue;
                        int x = space.X1, y = space.Y1, z = space.Z1;
                        int newZ = Math.Max(_extentZ, z + d);
                        long key = _minimumVolume ? (long)Math.Max(_extentX, x + w) * Math.Max(_extentY, y + h) * newZ : newZ;
                        if (key > bestKey) continue;
                        if (key == bestKey && (z > bestZ || (z == bestZ && (y > bestY || (y == bestY && x >= bestX))))) continue;
                        bestKey = key;
                        bestX = x;
                        bestY = y;
                        bestZ = z;
                    }
                    if (bestX < 0) return new PackingRunResult();

                    _posX[block] = bestX;
                    _posY[block] = bestY;
                    _posZ[block] = bestZ;
                    _extentX = Math.Max(_extentX, bestX + w);
                    _extentY = Math.Max(_extentY, bestY + h);
                    _extentZ = Math.Max(_extentZ, bestZ + d);
                    Split(bestX, bestY, bestZ, bestX + w, bestY + h, bestZ + d, _minX[orderIndex + 1], _minY[orderIndex + 1], _minZ[orderIndex + 1]);
                    long blockVolume = (long)w * h * d;
                    placedVolume += blockVolume;
                    remaining -= blockVolume;

                    if (!bound.Valid) continue;
                    if (packingStrategy == TexturePackingStrategy.MinimumDepth) {
                        if (GetExtent(2) > bound.Depth) return new PackingRunResult();
                    } else {
                        long lowerZ = _extentZ;
                        if (!_minimumVolume) lowerZ = Math.Max(lowerZ, (placedVolume + remaining + crossSection - 1) / crossSection);
                        if (Math.Max((long)_extentX * _extentY * lowerZ, placedVolume + remaining) > bound.Volume) return new PackingRunResult();
                    }
                }

                return new PackingRunResult { Valid = true, Width = GetExtent(0), Height = GetExtent(1), Depth = GetExtent(2), Volume = (long)_extentX * _extentY * _extentZ };
            }

            private int GetExtent(int atlasAxis) {
                if (_frameToAtlas[0] == atlasAxis) return _extentX;
                if (_frameToAtlas[1] == atlasAxis) return _extentY;
                return _extentZ;
            }

            // Replaces every space the placed box intersects by its up to six maximal remainders, then drops spaces that another
            // space contains or that are too small for every remaining block.
            private void Split(int x1, int y1, int z1, int x2, int y2, int z2, int minWidth, int minHeight, int minDepth) {
                _created.Clear();
                for (int i = _spaces.Count - 1; i >= 0; i--) {
                    EmptySpace s = _spaces[i];
                    if (x1 >= s.X2 || x2 <= s.X1 || y1 >= s.Y2 || y2 <= s.Y1 || z1 >= s.Z2 || z2 <= s.Z1) continue;
                    _spaces[i] = _spaces[_spaces.Count - 1];
                    _spaces.RemoveAt(_spaces.Count - 1);
                    if (x1 > s.X1) _created.Add(new EmptySpace { X1 = s.X1, Y1 = s.Y1, Z1 = s.Z1, X2 = x1, Y2 = s.Y2, Z2 = s.Z2 });
                    if (x2 < s.X2) _created.Add(new EmptySpace { X1 = x2, Y1 = s.Y1, Z1 = s.Z1, X2 = s.X2, Y2 = s.Y2, Z2 = s.Z2 });
                    if (y1 > s.Y1) _created.Add(new EmptySpace { X1 = s.X1, Y1 = s.Y1, Z1 = s.Z1, X2 = s.X2, Y2 = y1, Z2 = s.Z2 });
                    if (y2 < s.Y2) _created.Add(new EmptySpace { X1 = s.X1, Y1 = y2, Z1 = s.Z1, X2 = s.X2, Y2 = s.Y2, Z2 = s.Z2 });
                    if (z1 > s.Z1) _created.Add(new EmptySpace { X1 = s.X1, Y1 = s.Y1, Z1 = s.Z1, X2 = s.X2, Y2 = s.Y2, Z2 = z1 });
                    if (z2 < s.Z2) _created.Add(new EmptySpace { X1 = s.X1, Y1 = s.Y1, Z1 = z2, X2 = s.X2, Y2 = s.Y2, Z2 = s.Z2 });
                }
                for (int i = 0; i < _created.Count; i++) {
                    EmptySpace c = _created[i];
                    if (c.X2 - c.X1 < minWidth || c.Y2 - c.Y1 < minHeight || c.Z2 - c.Z1 < minDepth) continue;
                    bool dominated = false;
                    for (int j = 0; j < _spaces.Count && !dominated; j++) dominated = Contains(_spaces[j], c);
                    for (int j = 0; j < _created.Count && !dominated; j++) {
                        if (j != i && !_created[j].Dropped && Contains(_created[j], c) && (!Contains(c, _created[j]) || j < i)) dominated = true;
                    }
                    if (dominated) {
                        c.Dropped = true;
                        _created[i] = c;
                        continue;
                    }
                    _spaces.Add(c);
                }
            }

            private static bool Contains(EmptySpace a, EmptySpace b) {
                return a.X1 <= b.X1 && a.Y1 <= b.Y1 && a.Z1 <= b.Z1 && a.X2 >= b.X2 && a.Y2 >= b.Y2 && a.Z2 >= b.Z2;
            }
        }

        private struct PackingResult {
            public bool Success;
            public int AtlasWidth;
            public int AtlasHeight;
            public int AtlasDepth;
            public AtlasPlacedBlock[] Placed;
            // Packings the search tried.
            public int Packings;
            // Why packing failed, when Success is false.
            public string Error;
            // Whether Max Packing Time ended the search.
            public bool TimeLimitReached;
        }

        private sealed class PackingOutput {
            public bool Done;
            public bool UsedGpu;
            // Why the CPU packed (a sentence for the logs) and a few words for the packing log line; null when the GPU packed.
            public string CpuReason;
            public string CpuReasonShort;
            // Whether the GPU was tried and failed.
            public bool GpuFailed;
            public PackingResult Result;
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
