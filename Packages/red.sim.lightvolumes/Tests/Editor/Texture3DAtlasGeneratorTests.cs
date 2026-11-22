using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace VRCLightVolumes.Tests {
    [AddComponentMenu("")]
    public sealed class AtlasDeduplicationOptOutTestVolume : LightVolumeInstance {
        public override bool AllowAtlasDeduplication => false;
    }

    [Category("Editor")]
    public class Texture3DAtlasGeneratorTests {
        private const float Epsilon = 0.001f;
        private const int MaxAtlasSize = 2048;
        private const int AtlasTimeoutSeconds = 30;
        private static readonly BindingFlags _staticPrivateFlags = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly BindingFlags _instancePrivateFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags _nestedPrivateFlags = BindingFlags.NonPublic;

        private readonly List<UnityEngine.Object> _createdObjects = new List<UnityEngine.Object>();
        private bool _hadGpuRunning;
        private int _savedGpuRunning;
        private bool _hadGpuDisabled;
        private bool _savedGpuDisabled;

        // Destroys all temporary scene and texture objects created by a test case and restores the GPU crash guard's EditorPrefs.
        [TearDown]
        public void TearDown() {
            for (int i = _createdObjects.Count - 1; i >= 0; i--) {
                if (_createdObjects[i] != null) UnityEngine.Object.DestroyImmediate(_createdObjects[i]);
            }
            _createdObjects.Clear();
            RestoreCrashGuardPrefs();
        }

        // Clears the GPU crash guard's EditorPrefs for this project, so a real crash on this computer cannot change the results.
        [SetUp]
        public void SetUp() {
            _hadGpuRunning = EditorPrefs.HasKey(GpuRunningKey);
            _savedGpuRunning = EditorPrefs.GetInt(GpuRunningKey, 0);
            _hadGpuDisabled = EditorPrefs.HasKey(GpuDisabledKey);
            _savedGpuDisabled = EditorPrefs.GetBool(GpuDisabledKey, false);
            EditorPrefs.DeleteKey(GpuRunningKey);
            EditorPrefs.DeleteKey(GpuDisabledKey);
            // Each test sees the once-per-session logs.
            ((HashSet<string>)typeof(Texture3DAtlasGenerator).GetField("gpuPackingLoggedReasons", _staticPrivateFlags).GetValue(null)).Clear();
        }

        private void RestoreCrashGuardPrefs() {
            EditorPrefs.DeleteKey(GpuRunningKey);
            EditorPrefs.DeleteKey(GpuDisabledKey);
            if (_hadGpuRunning) EditorPrefs.SetInt(GpuRunningKey, _savedGpuRunning);
            if (_hadGpuDisabled) EditorPrefs.SetBool(GpuDisabledKey, _savedGpuDisabled);
        }

        // Verifies a single valid baked volume produces an atlas and readable bounds for all three texture slots.
        [Test]
        public void CreateAtlasPacksValidBakedVolume() {
            LightVolumeInstance volume = CreateBakedLightVolume("Valid Baked Volume", new Color(0.25f, 0.5f, 0.75f, 0), Color.clear, Color.clear, 2, 2, 2);

            Atlas3D atlas = RunAtlas(new[] { volume });

            Assert.That(atlas.Texture, Is.Not.Null);
            Assert.That(atlas.Texture.format, Is.EqualTo(TextureFormat.RGBAHalf));
            Assert.That(atlas.BoundsUvwMin, Has.Length.EqualTo(3));
            Assert.That(atlas.BoundsUvwMax, Has.Length.EqualTo(3));
            AssertBoundsInsideAtlas(atlas.BoundsUvwMin[0], atlas.BoundsUvwMax[0]);
            AssertColorClose(new Color(0.25f, 0.5f, 0.75f, 0), SampleAtlasPixel(atlas.Texture, atlas.BoundsUvwMin[0]));
        }

        // Verifies downscale uses the expected 2x2x2 box average before atlas packing.
        [Test]
        public void CreateAtlasDownscaleAveragesSourceVoxels() {
            Color[] tex0Pixels = {
                new Color(1, 0, 0, 0), new Color(0, 1, 0, 0), new Color(0, 0, 1, 0), new Color(1, 1, 0, 0),
                new Color(1, 0, 1, 0), new Color(0, 1, 1, 0), new Color(1, 1, 1, 0), new Color(0, 0, 0, 0)
            };
            LightVolumeInstance volume = CreateBakedLightVolume("Downscale Volume", CreateTexture3D("Downscale Tex0", 2, 2, 2, TextureFormat.RGBAHalf, tex0Pixels), CreateSolidTexture3D("Downscale Tex1", 2, 2, 2, TextureFormat.RGBAHalf, Color.clear), CreateSolidTexture3D("Downscale Tex2", 2, 2, 2, TextureFormat.RGBAHalf, Color.clear));

            Atlas3D atlas = RunAtlas(new[] { volume }, 1);

            AssertColorClose(new Color(0.5f, 0.5f, 0.5f, 0), SampleAtlasPixel(atlas.Texture, atlas.BoundsUvwMin[0]));
        }

        // Verifies oversized baked source textures can still pass when downscale makes the packed island valid.
        [Test]
        public void CreateAtlasDownscalesLargeBakedTextureBeforeSizeValidation() {
            LightVolumeInstance volume = CreateBakedLightVolume("Downscaled Large Baked Texture", new Color(0.25f, 0.25f, 0.25f, 0), Color.clear, Color.clear, MaxAtlasSize - 1, 1, 1);

            Atlas3D atlas = RunAtlas(new[] { volume }, 1);

            Assert.That(atlas.Texture, Is.Not.Null);
            AssertBoundsInsideAtlas(atlas.BoundsUvwMin[0], atlas.BoundsUvwMax[0]);
            AssertColorClose(new Color(0.25f, 0.25f, 0.25f, 0), SampleAtlasPixel(atlas.Texture, atlas.BoundsUvwMin[0]));
        }

        // Verifies identical baked texture data shares atlas bounds across different volumes.
        [TestCase(false)]
        [TestCase(true)]
        public void CreateAtlasDeduplicatesIdenticalBakedTextures(bool additive) {
            LightVolumeInstance first = CreateBakedLightVolume("Dedup Volume A", new Color(0.2f, 0.3f, 0.4f, 0), new Color(0.05f, 0.01f, 0.02f, 0), new Color(0.03f, 0.04f, 0.01f, 0), 2, 2, 2);
            LightVolumeInstance second = CreateBakedLightVolume("Dedup Volume B", new Color(0.2f, 0.3f, 0.4f, 0), new Color(0.05f, 0.01f, 0.02f, 0), new Color(0.03f, 0.04f, 0.01f, 0), 2, 2, 2);
            first.IsAdditive = additive;
            second.IsAdditive = additive;

            Atlas3D atlas = RunAtlas(new[] { first, second });

            AssertVectorClose(atlas.BoundsUvwMin[0], atlas.BoundsUvwMin[3]);
            AssertVectorClose(atlas.BoundsUvwMax[0], atlas.BoundsUvwMax[3]);
            AssertVectorClose(atlas.BoundsUvwMin[1], atlas.BoundsUvwMin[4]);
            AssertVectorClose(atlas.BoundsUvwMin[2], atlas.BoundsUvwMin[5]);
        }

        // Keeps opted-out textures separate while ordinary volumes share identical data.
        [TestCase(false)]
        [TestCase(true)]
        public void CreateAtlasKeepsOptedOutTexturesUniqueAmongDeduplicatedVolumes(bool additive) {
            LightVolumeInstance[] volumes = new LightVolumeInstance[4];
            for (int i = 0; i < volumes.Length; i++) {
                volumes[i] = CreateBakedLightVolume("Mixed Dedup Volume " + i, new Color(0.2f, 0.3f, 0.4f, 0), Color.clear, Color.clear, 2, 2, 2, i % 2 == 0 ? typeof(AtlasDeduplicationOptOutTestVolume) : null);
                volumes[i].IsAdditive = additive;
            }

            Atlas3D atlas = RunAtlas(volumes);

            Assert.That(atlas.BoundsUvwMin, Has.Length.EqualTo(12));
            Assert.That(atlas.BoundsUvwMax, Has.Length.EqualTo(12));
            int[] uniqueIslands = { 0, 1, 2, 6, 7, 8 };
            foreach (int island in uniqueIslands) {
                AssertBoundsInsideAtlas(atlas.BoundsUvwMin[island], atlas.BoundsUvwMax[island]);
                for (int other = 0; other < atlas.BoundsUvwMin.Length; other++) {
                    if (other == island) continue;
                    Assert.That(BoundsDiffer(atlas.BoundsUvwMin[island], atlas.BoundsUvwMin[other]), Is.True, $"Opted-out island {island} shares storage with island {other}.");
                }
            }
            for (int slot = 0; slot < 3; slot++) {
                AssertVectorClose(atlas.BoundsUvwMin[3 + slot], atlas.BoundsUvwMin[9 + slot]);
                AssertVectorClose(atlas.BoundsUvwMax[3 + slot], atlas.BoundsUvwMax[9 + slot]);
            }
            AssertVectorClose(atlas.BoundsUvwMin[4], atlas.BoundsUvwMin[5]);
        }

        // Verifies different texture data remains unique instead of being merged by dimensions alone.
        [Test]
        public void CreateAtlasKeepsDifferentBakedTexturesUnique() {
            LightVolumeInstance first = CreateBakedLightVolume("Unique Volume A", new Color(0.2f, 0.3f, 0.4f, 0), Color.clear, Color.clear, 2, 2, 2);
            LightVolumeInstance second = CreateBakedLightVolume("Unique Volume B", new Color(0.6f, 0.3f, 0.4f, 0), Color.clear, Color.clear, 2, 2, 2);

            Atlas3D atlas = RunAtlas(new[] { first, second });

            Assert.That(BoundsDiffer(atlas.BoundsUvwMin[0], atlas.BoundsUvwMin[3]), Is.True);
            AssertColorClose(new Color(0.2f, 0.3f, 0.4f, 0), SampleAtlasPixel(atlas.Texture, atlas.BoundsUvwMin[0]));
            AssertColorClose(new Color(0.6f, 0.3f, 0.4f, 0), SampleAtlasPixel(atlas.Texture, atlas.BoundsUvwMin[3]));
        }

        // Verifies reserved UV space is intentionally force-unique even when two reserved volumes have identical data.
        [Test]
        public void CreateAtlasDoesNotDeduplicateReservedUvSpace() {
            LightVolumeInstance first = CreateReservedLightVolume("Reserved Volume A", new Vector3Int(2, 2, 2));
            LightVolumeInstance second = CreateReservedLightVolume("Reserved Volume B", new Vector3Int(2, 2, 2));

            Atlas3D atlas = RunAtlas(new[] { first, second });

            Assert.That(BoundsDiffer(atlas.BoundsUvwMin[0], atlas.BoundsUvwMin[3]), Is.True);
            Assert.That(BoundsDiffer(atlas.BoundsUvwMin[1], atlas.BoundsUvwMin[4]), Is.True);
            Assert.That(BoundsDiffer(atlas.BoundsUvwMin[2], atlas.BoundsUvwMin[5]), Is.True);
            AssertColorClose(new Color(1, 1, 1, 0), SampleAtlasPixel(atlas.Texture, atlas.BoundsUvwMin[0]));
            AssertColorClose(Color.clear, SampleAtlasPixel(atlas.Texture, atlas.BoundsUvwMin[1]));
        }

        // Verifies invalid reserved dimensions are clamped to one voxel instead of producing zero-sized islands.
        [Test]
        public void CreateAtlasClampsInvalidReservedResolution() {
            LightVolumeInstance volume = CreateReservedLightVolume("Invalid Reserved Resolution", new Vector3Int(-4, 0, 2));

            Atlas3D atlas = RunAtlas(new[] { volume }, 1);

            Assert.That(atlas.Texture, Is.Not.Null);
            Assert.That(atlas.BoundsUvwMin, Has.Length.EqualTo(3));
            AssertBoundsInsideAtlas(atlas.BoundsUvwMin[0], atlas.BoundsUvwMax[0]);
        }

        // Verifies null or empty inputs fail without invoking the completion callback.
        [Test]
        public void CreateAtlasRejectsNullAndEmptyVolumeLists() {
            LogAssert.Expect(LogType.Error, "[LightVolumes] No light volumes were provided for atlas generation!");
            Assert.That(RunAtlasExpectingNoResult(null), Is.False);

            LogAssert.Expect(LogType.Error, "[LightVolumes] No light volumes were provided for atlas generation!");
            Assert.That(RunAtlasExpectingNoResult(new LightVolumeInstance[0]), Is.False);
        }

        // Verifies an abandoned coroutine releases the completed native atlas before ownership reaches the callback.
        [Test]
        public void CreateAtlasCancellationDestroysPendingAtlasTexture() {
            LightVolumeInstance volume = CreateBakedLightVolume("Cancelled Atlas Volume", Color.white, Color.clear, Color.clear, 1, 1, 1);
            HashSet<int> existingTextureIds = CaptureTexture3DInstanceIds();
            bool completed = false;
            Texture3D pendingAtlas = null;
            IEnumerator routine = Texture3DAtlasGenerator.CreateAtlas(new[] { volume }, atlas => {
                completed = true;
                pendingAtlas = atlas.Texture;
            });

            try {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (routine.MoveNext()) {
                    pendingAtlas = FindNewTexture3D(existingTextureIds);
                    if (pendingAtlas != null) break;
                    if (timer.Elapsed.TotalSeconds > AtlasTimeoutSeconds)
                        Assert.Fail("Atlas generation did not reach its ownership-transfer yield within the timeout.");
                }

                Assert.That(pendingAtlas, Is.Not.Null, "The generator completed without exposing its pending atlas allocation.");
                Assert.That(completed, Is.False, "The callback ran before the cancellation point.");
            } finally {
                IDisposable disposable = routine as IDisposable;
                Assert.That(disposable, Is.Not.Null, "The atlas iterator must support deterministic cancellation cleanup.");
                disposable.Dispose();
            }

            Assert.That(pendingAtlas == null, Is.True, "Cancelling the generator must destroy its untransferred native Texture3D.");
            Assert.That(completed, Is.False);
        }

        // Verifies a callback failure does not strand the generated native texture in the editor process.
        [Test]
        public void CreateAtlasCallbackFailureDestroysGeneratedTexture() {
            LightVolumeInstance volume = CreateBakedLightVolume("Throwing Atlas Callback Volume", Color.white, Color.clear, Color.clear, 1, 1, 1);
            Texture3D deliveredTexture = null;
            IEnumerator routine = Texture3DAtlasGenerator.CreateAtlas(new[] { volume }, atlas => {
                deliveredTexture = atlas.Texture;
                throw new InvalidOperationException("Expected atlas callback failure.");
            });

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => RunEnumerator(routine));

            Assert.That(exception.Message, Is.EqualTo("Expected atlas callback failure."));
            Assert.That(deliveredTexture == null, Is.True, "A failed ownership callback must not leak its generated Texture3D.");
        }

        // Verifies callers cannot generate an unowned atlas by omitting the ownership callback.
        [Test]
        public void CreateAtlasRejectsMissingCompletionCallback() {
            LightVolumeInstance volume = CreateBakedLightVolume("Missing Atlas Callback Volume", Color.white, Color.clear, Color.clear, 1, 1, 1);
            LogAssert.Expect(LogType.Error, "[LightVolumes] Atlas generation requires a completion callback to receive ownership of the generated texture.");

            RunEnumerator(Texture3DAtlasGenerator.CreateAtlas(new[] { volume }, null));
        }

        // Verifies missing baked textures fail cleanly before worker tasks are started.
        [Test]
        public void CreateAtlasRejectsMissingBakedTextures() {
            LightVolumeInstance volume = CreateSceneLightVolume("Missing Baked Textures");
            volume.Bake = true;
            LogAssert.Expect(LogType.Error, $"[LightVolumes] Light volume \"{volume.gameObject.name}\" is not baked!");

            Assert.That(RunAtlasExpectingNoResult(new[] { volume }), Is.False);
        }

        // Verifies mismatched Texture3D dimensions fail cleanly with a deterministic error.
        [Test]
        public void CreateAtlasRejectsMismatchedTextureDimensions() {
            LightVolumeInstance volume = CreateBakedLightVolume("Mismatched Dimensions", CreateSolidTexture3D("Mismatch Tex0", 2, 2, 2, TextureFormat.RGBAHalf, Color.white), CreateSolidTexture3D("Mismatch Tex1", 1, 2, 2, TextureFormat.RGBAHalf, Color.clear), CreateSolidTexture3D("Mismatch Tex2", 2, 2, 2, TextureFormat.RGBAHalf, Color.clear));
            LogAssert.Expect(LogType.Error, $"[LightVolumes] Light volume \"{volume.gameObject.name}\" has mismatched Texture3D dimensions.");

            Assert.That(RunAtlasExpectingNoResult(new[] { volume }), Is.False);
        }

        // Verifies unsupported source formats fail before GetPixels can produce ambiguous packed SH data.
        [Test]
        public void CreateAtlasRejectsUnsupportedTextureFormats() {
            LightVolumeInstance volume = CreateBakedLightVolume("Unsupported Format", CreateEmptyTexture3D("Unsupported RGB24 Tex0", 2, 2, 2, TextureFormat.RGB24), CreateSolidTexture3D("Unsupported Tex1", 2, 2, 2, TextureFormat.RGBAHalf, Color.clear), CreateSolidTexture3D("Unsupported Tex2", 2, 2, 2, TextureFormat.RGBAHalf, Color.clear));
            LogAssert.Expect(LogType.Error, $"[LightVolumes] Light volume \"{volume.gameObject.name}\" has unsupported texture format. Light Volume textures must use RGBAHalf, RGBAFloat, RGBA32 or ARGB32.");

            Assert.That(RunAtlasExpectingNoResult(new[] { volume }), Is.False);
        }

        // Verifies oversized baked textures fail before pixel processing or atlas allocation.
        [Test]
        public void CreateAtlasRejectsTooLargeBakedTextureDimensions() {
            LightVolumeInstance volume = CreateBakedLightVolume("Too Large Baked Texture", CreateSolidTexture3D("Too Large Tex0", MaxAtlasSize - 1, 1, 1, TextureFormat.RGBAHalf, Color.clear), CreateSolidTexture3D("Too Large Tex1", MaxAtlasSize - 1, 1, 1, TextureFormat.RGBAHalf, Color.clear), CreateSolidTexture3D("Too Large Tex2", MaxAtlasSize - 1, 1, 1, TextureFormat.RGBAHalf, Color.clear));
            LogAssert.Expect(LogType.Error, $"[LightVolumes] Light volume \"{volume.gameObject.name}\" texture is too large for the atlas: {MaxAtlasSize - 1} x 1 x 1 plus 1 voxel padding per side " +
                $"exceeds the atlas limit of {MaxAtlasSize} on an axis. No new atlas was made; the previous atlas is kept. Lower this volume's resolution or increase Downscale Volumes on the Light Volume Manager.");

            Assert.That(RunAtlasExpectingNoResult(new[] { volume }), Is.False);
        }

        // Verifies oversized reserved UV space fails before creating a managed voxel buffer.
        [Test]
        public void CreateAtlasRejectsTooLargeReservedVolumeDimensions() {
            LightVolumeInstance volume = CreateReservedLightVolume("Too Large Reserved Volume", new Vector3Int(MaxAtlasSize - 1, 1, 1));
            LogAssert.Expect(LogType.Error, $"[LightVolumes] Reserved UV space for light volume \"{volume.gameObject.name}\" is too large: {MaxAtlasSize - 1} x 1 x 1 plus 1 voxel padding per side " +
                $"exceeds the atlas limit of {MaxAtlasSize} on an axis. No new atlas was made; the previous atlas is kept. Lower the volume's reserved resolution.");

            Assert.That(RunAtlasExpectingNoResult(new[] { volume }), Is.False);
        }

        // Verifies the private packer rejects blocks whose padded size exceeds the atlas maximum without allocating texture data.
        [Test]
        public void PackTextureBlocksRejectsTooLargePaddedBlock() {
            object result = InvokePackTextureBlocks(new[] { new[] { MaxAtlasSize - 1, 1, 1 } }, TexturePackingStrategy.MinimumVRAM, AtlasPackingSettings.Default);

            Assert.That(GetField<bool>(result, "Success"), Is.False);
        }

        // Verifies the private packer accepts a block that exactly reaches the atlas maximum after padding.
        [Test]
        public void PackTextureBlocksAcceptsExactMaximumPaddedBlock() {
            object result = InvokePackTextureBlocks(new[] { new[] { MaxAtlasSize - 2, 1, 1 } }, TexturePackingStrategy.MinimumVRAM, AtlasPackingSettings.Default);

            Assert.That(GetField<bool>(result, "Success"), Is.True);
            Assert.That(GetField<int>(result, "AtlasWidth"), Is.EqualTo(MaxAtlasSize));
        }

        // Verifies a real 150-island scene packs every island exactly once, padded, inside the atlas and without overlaps.
        [TestCase(TexturePackingStrategy.MinimumVRAM)]
        [TestCase(TexturePackingStrategy.MinimumDepth)]
        public void PackTextureBlocksPlacesEveryBlockOnceWithoutOverlap(TexturePackingStrategy strategy) {
            int[][] sizes = CreateRealSceneBlockSizes();
            object result = InvokePackTextureBlocks(sizes, strategy, FastSettings(true));

            AssertValidLayout(result, sizes);
        }

        // Verifies the default settings search at the Default level, stop early and have no time limit, on the struct and on a new Manager.
        [Test]
        public void PackingSettingsDefaultToDefaultSearchWithStopEarlyAndNoTimeLimit() {
            AtlasPackingSettings settings = AtlasPackingSettings.Default;
            Assert.That(settings.Search, Is.EqualTo(AtlasPackingSearch.Default));
            Assert.That(settings.StopEarly, Is.True);
            Assert.That(settings.MaxSeconds, Is.EqualTo(0f));
            Assert.That(settings.UseCpu, Is.False);
            Assert.That(settings.SkipGpuReason, Is.Null);

            GameObject managerObject = new GameObject("Packing Settings Manager");
            _createdObjects.Add(managerObject);
            LightVolumeManager manager = managerObject.AddComponent<LightVolumeManager>();
            Assert.That((AtlasPackingSearch)manager.AtlasPackingSearch, Is.EqualTo(AtlasPackingSearch.Default));
            Assert.That(manager.AtlasPackingStopEarly, Is.True);
            Assert.That(manager.AtlasPackingMaxTime, Is.EqualTo(0f));
            Assert.That(manager.AtlasPackingUseCpu, Is.False);
        }

        // Verifies Stop Early ends the search after fewer packings than the full budget and still returns a valid layout.
        [Test]
        public void PackTextureBlocksStopEarlyTriesFewerPackings() {
            int[][] sizes = CreateRealSceneBlockSizes();
            object full = InvokePackTextureBlocks(sizes, TexturePackingStrategy.MinimumVRAM, FastSettings(false));
            object stopped = InvokePackTextureBlocks(sizes, TexturePackingStrategy.MinimumVRAM, FastSettings(true));

            AssertValidLayout(full, sizes);
            AssertValidLayout(stopped, sizes);
            Assert.That(GetField<int>(stopped, "Packings"), Is.LessThan(GetField<int>(full, "Packings")));
        }

        // Verifies a time limit ends the search early with a valid layout.
        [Test]
        public void PackTextureBlocksTimeLimitEndsSearchEarly() {
            int[][] sizes = CreateRealSceneBlockSizes();
            AtlasPackingSettings limited = FastSettings(false);
            limited.MaxSeconds = 0.05f;
            object full = InvokePackTextureBlocks(sizes, TexturePackingStrategy.MinimumVRAM, FastSettings(false));
            object stopped = InvokePackTextureBlocks(sizes, TexturePackingStrategy.MinimumVRAM, limited);

            AssertValidLayout(stopped, sizes);
            Assert.That(GetField<int>(stopped, "Packings"), Is.LessThan(GetField<int>(full, "Packings")));
        }

        // Verifies Use CPU never touches the GPU, packs a valid atlas and logs one line that says why.
        [Test]
        public void PackTextureBlocksAsyncUseCpuNeverUsesGpu() {
            int[][] sizes = CreateRealSceneBlockSizes();
            AtlasPackingSettings settings = AtlasPackingSettings.Default;
            settings.UseCpu = true;
            LogAssert.Expect(LogType.Log, new Regex(PackedOnCpuPattern + @" \(Use CPU is on\)\.$"));

            object output = RunPackTextureBlocksAsync(sizes, settings, out bool sawGpuRunning);

            Assert.That(sawGpuRunning, Is.False);
            Assert.That(GetField<bool>(output, "UsedGpu"), Is.False);
            Assert.That(GetField<string>(output, "CpuReason"), Is.EqualTo("Use CPU is on."));
            AssertValidLayout(GetField<object>(output, "Result"), sizes);
        }

        // Verifies a skip reason from the caller (a running Bakery bake) keeps packing on the CPU and appears in the log line.
        [Test]
        public void PackTextureBlocksAsyncSkipGpuReasonPacksOnCpu() {
            int[][] sizes = CreateRealSceneBlockSizes();
            AtlasPackingSettings settings = FastSettings(true);
            settings.SkipGpuReason = "Bakery is baking";
            LogAssert.Expect(LogType.Log, new Regex(PackedOnCpuPattern + @" \(Bakery is baking\)\.$"));

            object output = RunPackTextureBlocksAsync(sizes, settings, out bool sawGpuRunning);

            Assert.That(sawGpuRunning, Is.False);
            Assert.That(GetField<bool>(output, "UsedGpu"), Is.False);
            AssertValidLayout(GetField<object>(output, "Result"), sizes);
        }

        // Verifies a GPU pack marks the GPU as running while it searches, clears the mark when it ends and logs the GPU, its graphics API,
        // the time and the atlas size; without a usable GPU the CPU packs and the line says why.
        [Test]
        public void PackTextureBlocksAsyncClearsCrashGuardAndLogsPack() {
            int[][] sizes = CreateRealSceneBlockSizes();
            bool gpu = GpuPackingAvailable();
            string device = gpu ? $@"GPU \({Regex.Escape(SystemInfo.graphicsDeviceName)}, {SystemInfo.graphicsDeviceType}\)" : "CPU";
            LogAssert.Expect(LogType.Log, new Regex($@"^\[LightVolumes\] Atlas packed on the {device} in \d+\.\d s: \d+ x \d+ x \d+, \d+\.\d\d MB" + (gpu ? @"\.$" : @" \(.+\)\.$")));

            object output = RunPackTextureBlocksAsync(sizes, AtlasPackingSettings.Default, out bool sawGpuRunning);

            Assert.That(sawGpuRunning, Is.EqualTo(gpu));
            Assert.That(EditorPrefs.HasKey(GpuRunningKey), Is.False);
            Assert.That(Texture3DAtlasGenerator.GpuPackingDisabledAfterCrash, Is.False);
            Assert.That(GetField<bool>(output, "UsedGpu"), Is.EqualTo(gpu));
        }

        // Verifies that a GPU mark left by another editor process (a crash or driver reset during GPU packing) turns the GPU off with one
        // warning, packs on the CPU with the reason in the log line, and that EnableGpuPackingAgain turns it back on.
        [Test]
        public void PackTextureBlocksAsyncSkipsGpuAfterCrashUntilEnabledAgain() {
            int[][] sizes = CreateRealSceneBlockSizes();
            int processId = (int)typeof(Texture3DAtlasGenerator).GetField("editorProcessId", _staticPrivateFlags).GetValue(null);
            int otherProcess = processId == int.MaxValue ? 1 : processId + 1;
            EditorPrefs.SetInt(GpuRunningKey, otherProcess);
            // LogAssert rebuilds the Regex from its pattern and drops RegexOptions, so multi-line patterns use the inline (?s).
            LogAssert.Expect(LogType.Warning, new Regex(@"(?s)^\[LightVolumes\] Atlas packing: Unity closed while the atlas was being packed on the GPU, so GPU packing is turned off\.\n" +
                $@"Reason: The previous Unity session \(process {otherProcess}\) ended during GPU packing.*What to do: To use the GPU again, click Try GPU again.*turn on Packing Settings > Use CPU"));
            LogAssert.Expect(LogType.Log, new Regex(PackedOnCpuPattern + @" \(GPU packing is off after a crash; click Try GPU again on the Light Volume Manager to turn it back on\)\.$"));

            object output = RunPackTextureBlocksAsync(sizes, FastSettings(true), out bool sawGpuRunning);
            object second = RunPackTextureBlocksAsync(sizes, FastSettings(true), out _);

            Assert.That(sawGpuRunning, Is.False);
            Assert.That(GetField<bool>(output, "UsedGpu"), Is.False);
            Assert.That(GetField<bool>(second, "UsedGpu"), Is.False);
            Assert.That(GetField<string>(output, "CpuReason"), Does.Contain("Try GPU again"));
            AssertValidLayout(GetField<object>(output, "Result"), sizes);
            Assert.That(Texture3DAtlasGenerator.GpuPackingDisabledAfterCrash, Is.True);

            Texture3DAtlasGenerator.EnableGpuPackingAgain();
            Assert.That(Texture3DAtlasGenerator.GpuPackingDisabledAfterCrash, Is.False);
            object third = RunPackTextureBlocksAsync(sizes, FastSettings(true), out _);
            Assert.That(GetField<bool>(third, "UsedGpu"), Is.EqualTo(GpuPackingAvailable()));
        }

        // Verifies the GPU search packs the real scene, with a layout that passes the check and beats the previous greedy atlas. Without
        // compute shader support (for example with -nographics) the packer must fall back to the CPU instead.
        [Test]
        public void PackTextureBlocksAsyncPacksOnGpuWhenSupported() {
            int[][] sizes = CreateRealSceneBlockSizes();
            object output = RunPackTextureBlocksAsync(sizes, AtlasPackingSettings.Default);
            object result = GetField<object>(output, "Result");

            AssertValidLayout(result, sizes);
            Assert.That(GetField<bool>(output, "UsedGpu"), Is.EqualTo(GpuPackingAvailable()));
            long voxels = (long)GetField<int>(result, "AtlasWidth") * GetField<int>(result, "AtlasHeight") * GetField<int>(result, "AtlasDepth");
            Assert.That(voxels, Is.LessThan(2663424L));
        }

        // Verifies every GPU fault ends in a valid CPU atlas with a log that names the fault, the stage, the blocks and the GPU: missing
        // compute support (an info log) and a missing kernel (a warning, the package is broken) are found before any dispatch; a failed
        // readback and results no packing can produce are found while the GPU search runs.
        [TestCase("NoComputeSupport", LogType.Log, false, @"the GPU cannot run the packer \(no compute shaders\), so the atlas is packed on the CPU\..*does not support compute shaders\..*What to do: Nothing is needed\..*stage: checking the GPU before packing; 150 unique volume textures")]
        [TestCase("MissingKernel", LogType.Warning, false, @"the GPU cannot run the packer \(packing shader did not compile\), so the atlas is packed on the CPU\..*has no PackAtlasCandidates kernel.*Unity reports.*reinstall or update the Light Volumes package.*stage: checking the GPU before packing")]
        [TestCase("ReadbackError", LogType.Warning, true, @"packing on the GPU failed \(readback error\), so the atlas is packed on the CPU instead\..*AsyncGPUReadback reported an error.*Result: The atlas is packed on the CPU instead\. It will be correct.*turn on Light Volume Manager > Packing Settings > Use CPU.*stage: cap grid; 150 unique volume textures")]
        [TestCase("InvalidResults", LogType.Warning, true, @"packing on the GPU failed \(invalid results\), so the atlas is packed on the CPU instead\..*impossible result for candidate 0 of \d+: status 7.*turn on Light Volume Manager > Packing Settings > Use CPU.*stage: cap grid; 150 unique volume textures")]
        public void PackTextureBlocksAsyncFallsBackToCpuOnGpuFault(string fault, LogType logType, bool duringSearch, string message) {
            int[][] sizes = CreateRealSceneBlockSizes();
            AtlasPackingSettings settings = FastSettings(true);
            FieldInfo faultField = typeof(Texture3DAtlasGenerator).GetField("gpuPackingSimulatedFault", _staticPrivateFlags);
            Assert.That(faultField, Is.Not.Null);
            // Faults are simulated on a device that runs the kernel; without one the packer never reaches them.
            bool simulated = GpuPackingAvailable();
            bool gpuFailed = simulated && duringSearch;
            if (simulated) {
                LogAssert.Expect(logType, new Regex($@"(?s)^\[LightVolumes\] Atlas packing: {message}.*Graphics: {Regex.Escape(SystemInfo.graphicsDeviceName)}.*Unity "));
            }
            // The atlas line after a GPU failure points back to the warning.
            if (gpuFailed) LogAssert.Expect(LogType.Log, new Regex(PackedOnCpuPattern + @" \(the GPU failed: (readback error|invalid results); see the warning above\)\.$"));
            faultField.SetValue(null, Enum.Parse(faultField.FieldType, fault));
            object output;
            try {
                output = RunPackTextureBlocksAsync(sizes, settings, out _);
            } finally {
                faultField.SetValue(null, Enum.ToObject(faultField.FieldType, 0));
            }

            Assert.That(GetField<bool>(output, "UsedGpu"), Is.False);
            Assert.That(GetField<bool>(output, "GpuFailed"), Is.EqualTo(gpuFailed));
            Assert.That(EditorPrefs.HasKey(GpuRunningKey), Is.False);
            AssertValidLayout(GetField<object>(output, "Result"), sizes);
            if (gpuFailed) {
                Assert.That(GetField<string>(output, "CpuReasonShort"), Is.EqualTo(fault == "ReadbackError" ? "readback error" : "invalid results"));
            }
        }

        // Verifies volume textures that cannot fit in one atlas end without an atlas and with an error that says why and what to do: no
        // layout fits within the size limit (nine 1000-voxel cubes), or the best atlas has more voxels than one texture holds (eight).
        [TestCase(9, "No layout of the 9 unique volume textures fits within 2048 voxels per axis\\. Even a perfect packing needs")]
        [TestCase(8, @"The best atlas found is \d+ x \d+ x \d+ = .* voxels, more than one atlas texture can hold")]
        public void PackTextureBlocksAsyncReportsAtlasThatDoesNotFit(int count, string reason) {
            int[][] sizes = new int[count][];
            for (int i = 0; i < count; i++) sizes[i] = new[] { 1000, 1000, 1000 };
            LogAssert.Expect(LogType.Error, new Regex($@"(?s)^\[LightVolumes\] Atlas packing: no atlas could be made: the volume textures do not fit in one atlas\.\nReason: {reason}.*" +
                @"Result: No new atlas was made\. The previous atlas is kept\.\nWhat to do: Increase Downscale Volumes.*stage: CPU search; " + count + " unique volume textures"));

            object output = RunPackTextureBlocksAsync(sizes, FastSettings(true), out _);

            Assert.That(GetField<bool>(GetField<object>(output, "Result"), "Success"), Is.False);
        }

        // Start of the one-line log of an atlas packed on the CPU.
        private const string PackedOnCpuPattern = @"^\[LightVolumes\] Atlas packed on the CPU in \d+\.\d s: \d+ x \d+ x \d+, \d+\.\d\d MB";

        // Verifies more blocks than the kernel holds pack on the CPU.
        [Test]
        public void PackTextureBlocksAsyncPacksTooManyBlocksOnCpu() {
            int[][] sizes = new int[300][];
            for (int i = 0; i < sizes.Length; i++) sizes[i] = new[] { 4 + i % 7, 4 + i * 3 % 5, 4 + i * 5 % 9 };

            object output = RunPackTextureBlocksAsync(sizes, FastSettings(true));

            Assert.That(GetField<bool>(output, "UsedGpu"), Is.False);
            AssertValidLayout(GetField<object>(output, "Result"), sizes);
        }

        // Verifies no blocks and a single block pack on the CPU.
        [TestCase(0)]
        [TestCase(1)]
        public void PackTextureBlocksAsyncPacksTinyInputsOnCpu(int count) {
            int[][] sizes = new int[count][];
            for (int i = 0; i < count; i++) sizes[i] = new[] { 30, 20, 10 };

            object output = RunPackTextureBlocksAsync(sizes, AtlasPackingSettings.Default);
            object result = GetField<object>(output, "Result");

            Assert.That(GetField<bool>(output, "UsedGpu"), Is.False);
            Assert.That(GetField<string>(output, "CpuReason"), Does.Contain($"The scene has {count} unique volume textures. Below 64"));
            Assert.That(GetField<string>(output, "CpuReasonShort"), Is.EqualTo($"{count} {(count == 1 ? "texture" : "textures")}; the GPU is used from 64"));
            Assert.That(GetField<bool>(result, "Success"), Is.True);
            if (count > 0) AssertValidLayout(result, sizes);
        }

        // Verifies a block that fills the maximum atlas size on one axis packs validly next to many small ones, on the GPU when
        // supported.
        [Test]
        public void PackTextureBlocksAsyncPacksBlockAtMaximumSize() {
            int[][] sizes = new int[64][];
            sizes[0] = new[] { MaxAtlasSize - 2, 6, 6 };
            for (int i = 1; i < sizes.Length; i++) sizes[i] = new[] { 6 + i % 3, 6, 6 + i % 5 };

            object output = RunPackTextureBlocksAsync(sizes, FastSettings(true));

            Assert.That(GetField<bool>(output, "UsedGpu"), Is.EqualTo(GpuPackingAvailable()));
            AssertValidLayout(GetField<object>(output, "Result"), sizes);
        }

        // Whether this editor's graphics device runs the packing kernel, decided the same way as the packer.
        private static bool GpuPackingAvailable() {
            MethodInfo method = typeof(Texture3DAtlasGenerator).GetMethod("GetGpuPackingUnsupportedReason", _staticPrivateFlags);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { null, -1, null, false }) == null;
        }

        // EditorPrefs keys of the GPU crash guard for this project.
        private static string GpuRunningKey => GetCrashGuardKey("gpuPackingRunningKey");
        private static string GpuDisabledKey => GetCrashGuardKey("gpuPackingDisabledKey");

        private static string GetCrashGuardKey(string fieldName) {
            typeof(Texture3DAtlasGenerator).GetMethod("InitGpuPackingKeys", _staticPrivateFlags).Invoke(null, null);
            return (string)typeof(Texture3DAtlasGenerator).GetField(fieldName, _staticPrivateFlags).GetValue(null);
        }

        // Verifies the layout check used before any packing is accepted rejects overlaps, blocks outside the atlas and wrong sizes.
        [Test]
        public void IsValidPackingRejectsBrokenLayouts() {
            Type generatorType = typeof(Texture3DAtlasGenerator);
            object problem = CreatePackingProblem(new[] { new[] { 2, 2, 2 }, new[] { 2, 2, 2 } });
            MethodInfo isValid = generatorType.GetMethod("IsValidPacking", _staticPrivateFlags);
            Assert.That(isValid, Is.Not.Null);

            Assert.That((bool)isValid.Invoke(null, new[] { CreatePackingResult(8, 4, 4, new[] { 0, 0, 0, 4, 4, 4, 0 }, new[] { 4, 0, 0, 4, 4, 4, 1 }), problem }), Is.True);
            Assert.That((bool)isValid.Invoke(null, new[] { CreatePackingResult(8, 4, 4, new[] { 0, 0, 0, 4, 4, 4, 0 }, new[] { 3, 0, 0, 4, 4, 4, 1 }), problem }), Is.False, "overlap");
            Assert.That((bool)isValid.Invoke(null, new[] { CreatePackingResult(7, 4, 4, new[] { 0, 0, 0, 4, 4, 4, 0 }, new[] { 4, 0, 0, 4, 4, 4, 1 }), problem }), Is.False, "outside the atlas");
            Assert.That((bool)isValid.Invoke(null, new[] { CreatePackingResult(8, 4, 4, new[] { 0, 0, 0, 4, 4, 4, 0 }, new[] { 4, 0, 0, 3, 4, 4, 1 }), problem }), Is.False, "wrong size");
            Assert.That((bool)isValid.Invoke(null, new[] { CreatePackingResult(8, 4, 4, new[] { 0, 0, 0, 4, 4, 4, 0 }, new[] { 4, 0, 0, 4, 4, 4, 0 }), problem }), Is.False, "block placed twice");
        }

        // Verifies packing gives the same layout on every run, so the same settings give the same atlas on every machine.
        [Test]
        public void PackTextureBlocksIsDeterministic() {
            int[][] sizes = CreateRealSceneBlockSizes();
            object first = InvokePackTextureBlocks(sizes, TexturePackingStrategy.MinimumVRAM, FastSettings(true));
            object second = InvokePackTextureBlocks(sizes, TexturePackingStrategy.MinimumVRAM, FastSettings(true));

            Assert.That(GetField<int>(second, "AtlasWidth"), Is.EqualTo(GetField<int>(first, "AtlasWidth")));
            Assert.That(GetField<int>(second, "AtlasHeight"), Is.EqualTo(GetField<int>(first, "AtlasHeight")));
            Assert.That(GetField<int>(second, "AtlasDepth"), Is.EqualTo(GetField<int>(first, "AtlasDepth")));
            int[][] firstBoxes = GetPlacedBoxes(first);
            int[][] secondBoxes = GetPlacedBoxes(second);
            Assert.That(secondBoxes.Length, Is.EqualTo(firstBoxes.Length));
            for (int i = 0; i < firstBoxes.Length; i++) Assert.That(secondBoxes[i], Is.EqualTo(firstBoxes[i]));
        }

        // Verifies the real 150-island scene packs smaller than the previous greedy packer's 102 x 32 x 816 atlas (2,663,424 voxels).
        [Test]
        public void PackTextureBlocksBeatsPreviousGreedyAtlasOnRealScene() {
            object result = InvokePackTextureBlocks(CreateRealSceneBlockSizes(), TexturePackingStrategy.MinimumVRAM, FastSettings(true));

            Assert.That(GetField<bool>(result, "Success"), Is.True);
            long voxels = (long)GetField<int>(result, "AtlasWidth") * GetField<int>(result, "AtlasHeight") * GetField<int>(result, "AtlasDepth");
            Assert.That(voxels, Is.LessThan(2663424L));
        }

        // Runs atlas generation and returns the generated atlas.
        private Atlas3D RunAtlas(LightVolumeInstance[] volumes, int downscaleCount = 0, TexturePackingStrategy packingStrategy = TexturePackingStrategy.MinimumVRAM) {
            bool completed = false;
            Atlas3D result = new Atlas3D();
            IEnumerator routine = Texture3DAtlasGenerator.CreateAtlas(volumes, atlas => {
                completed = true;
                result = atlas;
                if (atlas.Texture != null) _createdObjects.Add(atlas.Texture);
            }, downscaleCount, packingStrategy);
            RunEnumerator(routine);
            Assert.That(completed, Is.True);
            return result;
        }

        // Runs atlas generation and returns whether the completion callback was invoked.
        private bool RunAtlasExpectingNoResult(LightVolumeInstance[] volumes) {
            bool completed = false;
            IEnumerator routine = Texture3DAtlasGenerator.CreateAtlas(volumes, atlas => {
                completed = true;
                if (atlas.Texture != null) _createdObjects.Add(atlas.Texture);
            });
            RunEnumerator(routine);
            return completed;
        }

        // Allow worker tasks time to finish and dispose the iterator if an assertion fails.
        private static void RunEnumerator(IEnumerator routine) {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try {
                while (routine.MoveNext()) {
                    if (timer.Elapsed.TotalSeconds > AtlasTimeoutSeconds)
                        Assert.Fail("Atlas generation did not finish within the timeout.");
                }
            } finally {
                (routine as IDisposable)?.Dispose();
            }
        }

        // Captures all loaded Texture3D instance IDs so a later native allocation can be identified without retaining it.
        private static HashSet<int> CaptureTexture3DInstanceIds() {
            Texture3D[] textures = Resources.FindObjectsOfTypeAll<Texture3D>();
            HashSet<int> instanceIds = new HashSet<int>();
            for (int i = 0; i < textures.Length; i++) {
                if (textures[i] != null) instanceIds.Add(textures[i].GetInstanceID());
            }
            return instanceIds;
        }

        // Finds a Texture3D allocated since the supplied instance-ID snapshot.
        private static Texture3D FindNewTexture3D(HashSet<int> existingTextureIds) {
            Texture3D[] textures = Resources.FindObjectsOfTypeAll<Texture3D>();
            for (int i = 0; i < textures.Length; i++) {
                Texture3D texture = textures[i];
                if (texture != null && !existingTextureIds.Contains(texture.GetInstanceID())) return texture;
            }
            return null;
        }

        // Creates a baked volume from three solid textures.
        private LightVolumeInstance CreateBakedLightVolume(string name, Color tex0Color, Color tex1Color, Color tex2Color, int width, int height, int depth, Type componentType = null) {
            return CreateBakedLightVolume(name, CreateSolidTexture3D(name + " Tex0", width, height, depth, TextureFormat.RGBAHalf, tex0Color), CreateSolidTexture3D(name + " Tex1", width, height, depth, TextureFormat.RGBAHalf, tex1Color), CreateSolidTexture3D(name + " Tex2", width, height, depth, TextureFormat.RGBAHalf, tex2Color), componentType);
        }

        // Creates a baked volume from explicit textures.
        private LightVolumeInstance CreateBakedLightVolume(string name, Texture3D tex0, Texture3D tex1, Texture3D tex2, Type componentType = null) {
            LightVolumeInstance volume = CreateSceneLightVolume(name, componentType);
            volume.Bake = true;
            volume.Texture0 = tex0;
            volume.Texture1 = tex1;
            volume.Texture2 = tex2;
            volume.Exposure = 0;
            volume.Shadows = 0;
            volume.Highlights = 0;
            return volume;
        }

        // Creates a reserve-only volume with a requested resolution.
        private LightVolumeInstance CreateReservedLightVolume(string name, Vector3Int resolution) {
            LightVolumeInstance volume = CreateSceneLightVolume(name);
            volume.Bake = false;
            volume.ReserveUVSpace = true;
            volume.Resolution = resolution;
            volume.Exposure = 2;
            volume.Shadows = -1;
            volume.Highlights = 1;
            return volume;
        }

        // Creates a scene Light Volume component tracked by teardown.
        private LightVolumeInstance CreateSceneLightVolume(string name, Type componentType = null) {
            GameObject gameObject = new GameObject(name);
            _createdObjects.Add(gameObject);
            if (componentType != null) gameObject.SetActive(false);
            return (LightVolumeInstance)gameObject.AddComponent(componentType ?? typeof(LightVolumeInstance));
        }

        // Creates a solid Texture3D tracked by teardown.
        private Texture3D CreateSolidTexture3D(string name, int width, int height, int depth, TextureFormat format, Color color) {
            Color[] pixels = new Color[width * height * depth];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            return CreateTexture3D(name, width, height, depth, format, pixels);
        }

        // Creates a Texture3D with explicit pixels tracked by teardown.
        private Texture3D CreateTexture3D(string name, int width, int height, int depth, TextureFormat format, Color[] pixels) {
            Texture3D texture = new Texture3D(width, height, depth, format, false);
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false);
            _createdObjects.Add(texture);
            return texture;
        }

        // Creates an empty Texture3D for validation tests that should fail before pixel reads.
        private Texture3D CreateEmptyTexture3D(string name, int width, int height, int depth, TextureFormat format) {
            Texture3D texture = new Texture3D(width, height, depth, format, false);
            texture.name = name;
            _createdObjects.Add(texture);
            return texture;
        }

        // Unpadded island sizes of a real 150-volume scene (width, height, depth, count).
        private static int[][] CreateRealSceneBlockSizes() {
            int[,] groups = {
                { 89, 9, 75, 3 }, { 48, 12, 38, 6 }, { 36, 12, 48, 3 }, { 23, 25, 34, 3 }, { 46, 12, 35, 3 }, { 32, 17, 34, 3 }, { 76, 12, 19, 3 }, { 32, 12, 39, 6 },
                { 32, 12, 35, 3 }, { 38, 12, 29, 3 }, { 36, 10, 36, 3 }, { 46, 6, 46, 3 }, { 28, 28, 16, 3 }, { 27, 25, 18, 3 }, { 27, 20, 22, 3 }, { 32, 12, 28, 3 },
                { 42, 12, 20, 3 }, { 25, 18, 22, 3 }, { 20, 18, 24, 3 }, { 19, 18, 24, 3 }, { 28, 12, 24, 3 }, { 20, 12, 32, 3 }, { 46, 6, 27, 3 }, { 27, 15, 17, 3 },
                { 28, 12, 20, 3 }, { 14, 24, 19, 3 }, { 18, 16, 22, 3 }, { 32, 12, 16, 3 }, { 15, 11, 34, 3 }, { 20, 12, 23, 3 }, { 9, 30, 20, 3 }, { 14, 11, 34, 3 },
                { 14, 12, 23, 3 }, { 18, 12, 16, 3 }, { 7, 24, 20, 3 }, { 14, 12, 20, 3 }, { 15, 14, 15, 3 }, { 31, 7, 14, 3 }, { 14, 14, 15, 3 }, { 18, 25, 6, 3 },
                { 10, 15, 17, 3 }, { 17, 25, 6, 3 }, { 30, 8, 10, 3 }, { 21, 7, 14, 3 }, { 9, 17, 13, 3 }, { 8, 12, 20, 3 }, { 7, 12, 20, 3 }, { 7, 12, 8, 3 }
            };
            List<int[]> sizes = new List<int[]>();
            for (int g = 0; g < groups.GetLength(0); g++) {
                for (int n = 0; n < groups[g, 3]; n++) sizes.Add(new[] { groups[g, 0], groups[g, 1], groups[g, 2] });
            }
            return sizes.ToArray();
        }

        // Fast search settings, with or without Stop Early.
        private static AtlasPackingSettings FastSettings(bool stopEarly) {
            AtlasPackingSettings settings = AtlasPackingSettings.Default;
            settings.Search = AtlasPackingSearch.Fast;
            settings.StopEarly = stopEarly;
            return settings;
        }

        // Creates private packer blocks of the given unpadded sizes (width, height, depth).
        private static Array CreateBlocks(int[][] sizes) {
            Type blockType = typeof(Texture3DAtlasGenerator).GetNestedType("AtlasBlock", _nestedPrivateFlags);
            Assert.That(blockType, Is.Not.Null);
            Array blocks = Array.CreateInstance(blockType, sizes.Length);
            for (int i = 0; i < sizes.Length; i++) {
                object block = Activator.CreateInstance(blockType);
                SetField(block, "Index", i);
                SetField(block, "Width", sizes[i][0]);
                SetField(block, "Height", sizes[i][1]);
                SetField(block, "Depth", sizes[i][2]);
                blocks.SetValue(block, i);
            }
            return blocks;
        }

        // Invokes the private CPU packer with blocks of the given unpadded sizes (width, height, depth) and padding 1.
        private static object InvokePackTextureBlocks(int[][] sizes, TexturePackingStrategy strategy, AtlasPackingSettings settings) {
            Type generatorType = typeof(Texture3DAtlasGenerator);
            Type progressType = generatorType.GetNestedType("ThreadProgress", _nestedPrivateFlags);
            Assert.That(progressType, Is.Not.Null);
            MethodInfo method = generatorType.GetMethod("PackTextureBlocks", _staticPrivateFlags);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { CreateBlocks(sizes), 1, strategy, settings, Activator.CreateInstance(progressType), CancellationToken.None });
        }

        // Runs the private GPU-or-CPU packing coroutine to the end and returns its output (Done, UsedGpu, CpuReason, GpuFailed, Result).
        private static object RunPackTextureBlocksAsync(int[][] sizes, AtlasPackingSettings settings) {
            return RunPackTextureBlocksAsync(sizes, settings, out _);
        }

        // Same, and reports whether the crash guard marked the GPU as running between any two steps.
        private static object RunPackTextureBlocksAsync(int[][] sizes, AtlasPackingSettings settings, out bool sawGpuRunning) {
            Type generatorType = typeof(Texture3DAtlasGenerator);
            Type outputType = generatorType.GetNestedType("PackingOutput", _nestedPrivateFlags);
            Assert.That(outputType, Is.Not.Null);
            MethodInfo method = generatorType.GetMethod("PackTextureBlocksAsync", _staticPrivateFlags);
            Assert.That(method, Is.Not.Null);
            object output = Activator.CreateInstance(outputType);
            IEnumerator routine = (IEnumerator)method.Invoke(null, new object[] { CreateBlocks(sizes), 1, TexturePackingStrategy.MinimumVRAM, settings, null, CancellationToken.None, output });
            string runningKey = GpuRunningKey;
            sawGpuRunning = false;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try {
                while (routine.MoveNext()) {
                    sawGpuRunning |= EditorPrefs.HasKey(runningKey);
                    if (timer.Elapsed.TotalSeconds > AtlasTimeoutSeconds) Assert.Fail("Packing did not finish within the timeout.");
                }
            } finally {
                (routine as IDisposable)?.Dispose();
            }
            Assert.That(GetField<bool>(output, "Done"), Is.True);
            return output;
        }

        // Creates the private packing problem for blocks of the given unpadded sizes and padding 1.
        private static object CreatePackingProblem(int[][] sizes) {
            MethodInfo method = typeof(Texture3DAtlasGenerator).GetMethod("TryCreatePackingProblem", _staticPrivateFlags);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { CreateBlocks(sizes), 1, null };
            Assert.That((bool)method.Invoke(null, arguments), Is.True);
            return arguments[2];
        }

        // Creates a private packing result from boxes { x, y, z, width, height, depth, index }.
        private static object CreatePackingResult(int width, int height, int depth, params int[][] boxes) {
            Type generatorType = typeof(Texture3DAtlasGenerator);
            Type resultType = generatorType.GetNestedType("PackingResult", _nestedPrivateFlags);
            Type placedType = generatorType.GetNestedType("AtlasPlacedBlock", _nestedPrivateFlags);
            Assert.That(resultType, Is.Not.Null);
            Assert.That(placedType, Is.Not.Null);
            Array placed = Array.CreateInstance(placedType, boxes.Length);
            for (int i = 0; i < boxes.Length; i++) {
                object box = Activator.CreateInstance(placedType);
                SetField(box, "X", boxes[i][0]);
                SetField(box, "Y", boxes[i][1]);
                SetField(box, "Z", boxes[i][2]);
                SetField(box, "Width", boxes[i][3]);
                SetField(box, "Height", boxes[i][4]);
                SetField(box, "Depth", boxes[i][5]);
                SetField(box, "Index", boxes[i][6]);
                placed.SetValue(box, i);
            }
            object result = Activator.CreateInstance(resultType);
            SetField(result, "Success", true);
            SetField(result, "AtlasWidth", width);
            SetField(result, "AtlasHeight", height);
            SetField(result, "AtlasDepth", depth);
            SetField(result, "Placed", placed);
            return result;
        }

        // Asserts a packing result places every block exactly once, padded, inside an atlas within the size limit and without overlaps.
        private static void AssertValidLayout(object result, int[][] sizes) {
            Assert.That(GetField<bool>(result, "Success"), Is.True);
            int atlasWidth = GetField<int>(result, "AtlasWidth");
            int atlasHeight = GetField<int>(result, "AtlasHeight");
            int atlasDepth = GetField<int>(result, "AtlasDepth");
            Assert.That(Math.Max(atlasWidth, Math.Max(atlasHeight, atlasDepth)), Is.LessThanOrEqualTo(MaxAtlasSize));
            int[][] boxes = GetPlacedBoxes(result);
            Assert.That(boxes.Length, Is.EqualTo(sizes.Length));

            bool[] seen = new bool[sizes.Length];
            for (int i = 0; i < boxes.Length; i++) {
                int[] box = boxes[i];
                int index = box[6];
                Assert.That(seen[index], Is.False, "Block placed twice: " + index);
                seen[index] = true;
                Assert.That(box[3], Is.EqualTo(sizes[index][0] + 2));
                Assert.That(box[4], Is.EqualTo(sizes[index][1] + 2));
                Assert.That(box[5], Is.EqualTo(sizes[index][2] + 2));
                Assert.That(box[0] >= 0 && box[1] >= 0 && box[2] >= 0, Is.True);
                Assert.That(box[0] + box[3] <= atlasWidth && box[1] + box[4] <= atlasHeight && box[2] + box[5] <= atlasDepth, Is.True, "Block outside the atlas: " + index);
                for (int j = 0; j < i; j++) {
                    int[] other = boxes[j];
                    bool overlaps = box[0] < other[0] + other[3] && other[0] < box[0] + box[3]
                        && box[1] < other[1] + other[4] && other[1] < box[1] + box[4]
                        && box[2] < other[2] + other[5] && other[2] < box[2] + box[5];
                    Assert.That(overlaps, Is.False, "Blocks overlap: " + index + " and " + other[6]);
                }
            }
        }

        // Reads placed blocks as { x, y, z, width, height, depth, index }, in result order.
        private static int[][] GetPlacedBoxes(object result) {
            Array placed = GetField<Array>(result, "Placed");
            int[][] boxes = new int[placed.Length][];
            for (int i = 0; i < placed.Length; i++) {
                object box = placed.GetValue(i);
                boxes[i] = new[] {
                    GetField<int>(box, "X"), GetField<int>(box, "Y"), GetField<int>(box, "Z"),
                    GetField<int>(box, "Width"), GetField<int>(box, "Height"), GetField<int>(box, "Depth"), GetField<int>(box, "Index")
                };
            }
            return boxes;
        }

        // Sets a public field on a private nested test target.
        private static void SetField(object target, string fieldName, object value) {
            FieldInfo field = target.GetType().GetField(fieldName, _instancePrivateFlags);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        // Reads a field from a private nested test target.
        private static T GetField<T>(object target, string fieldName) {
            FieldInfo field = target.GetType().GetField(fieldName, _instancePrivateFlags);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(target);
        }

        // Samples the first voxel inside a packed atlas island.
        private static Color SampleAtlasPixel(Texture3D atlas, Vector3 boundsMin) {
            int x = Mathf.Clamp(Mathf.RoundToInt(boundsMin.x * atlas.width), 0, atlas.width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(boundsMin.y * atlas.height), 0, atlas.height - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(boundsMin.z * atlas.depth), 0, atlas.depth - 1);
            Color[] pixels = atlas.GetPixels();
            return pixels[x + y * atlas.width + z * atlas.width * atlas.height];
        }

        // Checks whether two atlas bounds point to different islands.
        private static bool BoundsDiffer(Vector3 a, Vector3 b) {
            return Mathf.Abs(a.x - b.x) > Epsilon || Mathf.Abs(a.y - b.y) > Epsilon || Mathf.Abs(a.z - b.z) > Epsilon;
        }

        // Asserts a UVW island lies inside the atlas and has positive size.
        private static void AssertBoundsInsideAtlas(Vector3 min, Vector3 max) {
            Assert.That(min.x, Is.InRange(0f, 1f));
            Assert.That(min.y, Is.InRange(0f, 1f));
            Assert.That(min.z, Is.InRange(0f, 1f));
            Assert.That(max.x, Is.InRange(0f, 1f));
            Assert.That(max.y, Is.InRange(0f, 1f));
            Assert.That(max.z, Is.InRange(0f, 1f));
            Assert.That(max.x - min.x, Is.GreaterThan(0));
            Assert.That(max.y - min.y, Is.GreaterThan(0));
            Assert.That(max.z - min.z, Is.GreaterThan(0));
        }

        // Asserts colors with the shared test tolerance.
        private static void AssertColorClose(Color expected, Color actual) {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(Epsilon));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(Epsilon));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(Epsilon));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(Epsilon));
        }

        // Asserts vectors with the shared test tolerance.
        private static void AssertVectorClose(Vector3 expected, Vector3 actual) {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Epsilon));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Epsilon));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Epsilon));
        }
    }
}
