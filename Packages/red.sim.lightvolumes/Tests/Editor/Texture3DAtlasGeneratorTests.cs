using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class Texture3DAtlasGeneratorTests {
        private const float Epsilon = 0.001f;
        private const int MaxAtlasSize = 2048;
        private static readonly BindingFlags _staticPrivateFlags = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly BindingFlags _instancePrivateFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags _nestedPrivateFlags = BindingFlags.NonPublic;

        private readonly List<UnityEngine.Object> _createdObjects = new List<UnityEngine.Object>();

        // Destroys all temporary scene and texture objects created by a test case.
        [TearDown]
        public void TearDown() {
            for (int i = _createdObjects.Count - 1; i >= 0; i--) {
                if (_createdObjects[i] != null) UnityEngine.Object.DestroyImmediate(_createdObjects[i]);
            }
            _createdObjects.Clear();
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
        [Test]
        public void CreateAtlasDeduplicatesIdenticalBakedTextures() {
            LightVolumeInstance first = CreateBakedLightVolume("Dedup Volume A", new Color(0.2f, 0.3f, 0.4f, 0), new Color(0.05f, 0.01f, 0.02f, 0), new Color(0.03f, 0.04f, 0.01f, 0), 2, 2, 2);
            LightVolumeInstance second = CreateBakedLightVolume("Dedup Volume B", new Color(0.2f, 0.3f, 0.4f, 0), new Color(0.05f, 0.01f, 0.02f, 0), new Color(0.03f, 0.04f, 0.01f, 0), 2, 2, 2);

            Atlas3D atlas = RunAtlas(new[] { first, second });

            AssertVectorClose(atlas.BoundsUvwMin[0], atlas.BoundsUvwMin[3]);
            AssertVectorClose(atlas.BoundsUvwMax[0], atlas.BoundsUvwMax[3]);
            AssertVectorClose(atlas.BoundsUvwMin[1], atlas.BoundsUvwMin[4]);
            AssertVectorClose(atlas.BoundsUvwMin[2], atlas.BoundsUvwMin[5]);
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
            Assert.That(atlas.BoundsUvwMax[0].x - atlas.BoundsUvwMin[0].x, Is.GreaterThan(0));
        }

        // Verifies null or empty inputs fail without invoking the completion callback.
        [Test]
        public void CreateAtlasRejectsNullAndEmptyVolumeLists() {
            LogAssert.Expect(LogType.Error, "[LightVolumes] No light volumes were provided for atlas generation!");
            Assert.That(RunAtlasExpectingNoResult(null), Is.False);

            LogAssert.Expect(LogType.Error, "[LightVolumes] No light volumes were provided for atlas generation!");
            Assert.That(RunAtlasExpectingNoResult(new LightVolumeInstance[0]), Is.False);
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
            LogAssert.Expect(LogType.Error, $"[LightVolumes] Light volume \"{volume.gameObject.name}\" texture dimensions are too large for the atlas.");

            Assert.That(RunAtlasExpectingNoResult(new[] { volume }), Is.False);
        }

        // Verifies oversized reserved UV space fails before creating a managed voxel buffer.
        [Test]
        public void CreateAtlasRejectsTooLargeReservedVolumeDimensions() {
            LightVolumeInstance volume = CreateReservedLightVolume("Too Large Reserved Volume", new Vector3Int(MaxAtlasSize - 1, 1, 1));
            LogAssert.Expect(LogType.Error, $"[LightVolumes] Reserved UV space for light volume \"{volume.gameObject.name}\" is too large!");

            Assert.That(RunAtlasExpectingNoResult(new[] { volume }), Is.False);
        }

        // Verifies the private packer rejects blocks whose padded size exceeds the atlas maximum without allocating texture data.
        [Test]
        public void PackTextureBlocksRejectsTooLargePaddedBlock() {
            object result = InvokePackTextureBlocks(MaxAtlasSize - 1, 1, 1, TexturePackingStrategy.MinimumVRAM);

            Assert.That(GetField<bool>(result, "Success"), Is.False);
        }

        // Verifies the private packer accepts a block that exactly reaches the atlas maximum after padding.
        [Test]
        public void PackTextureBlocksAcceptsExactMaximumPaddedBlock() {
            object result = InvokePackTextureBlocks(MaxAtlasSize - 2, 1, 1, TexturePackingStrategy.MinimumVRAM);

            Assert.That(GetField<bool>(result, "Success"), Is.True);
            Assert.That(GetField<int>(result, "AtlasWidth"), Is.EqualTo(MaxAtlasSize));
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

        // Runs a coroutine-like enumerator synchronously with a guard against accidental infinite loops.
        private static void RunEnumerator(IEnumerator routine) {
            int guard = 20000;
            while (routine.MoveNext()) {
                guard--;
                if (guard < 0) Assert.Fail("Atlas generation coroutine did not finish.");
            }
        }

        // Creates a baked volume from three solid textures.
        private LightVolumeInstance CreateBakedLightVolume(string name, Color tex0Color, Color tex1Color, Color tex2Color, int width, int height, int depth) {
            return CreateBakedLightVolume(name, CreateSolidTexture3D(name + " Tex0", width, height, depth, TextureFormat.RGBAHalf, tex0Color), CreateSolidTexture3D(name + " Tex1", width, height, depth, TextureFormat.RGBAHalf, tex1Color), CreateSolidTexture3D(name + " Tex2", width, height, depth, TextureFormat.RGBAHalf, tex2Color));
        }

        // Creates a baked volume from explicit textures.
        private LightVolumeInstance CreateBakedLightVolume(string name, Texture3D tex0, Texture3D tex1, Texture3D tex2) {
            LightVolumeInstance volume = CreateSceneLightVolume(name);
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
        private LightVolumeInstance CreateSceneLightVolume(string name) {
            GameObject gameObject = new GameObject(name);
            _createdObjects.Add(gameObject);
            return gameObject.AddComponent<LightVolumeInstance>();
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

        // Invokes the private packer with one block.
        private static object InvokePackTextureBlocks(int width, int height, int depth, TexturePackingStrategy strategy) {
            Type generatorType = typeof(Texture3DAtlasGenerator);
            Type blockType = generatorType.GetNestedType("AtlasBlock", _nestedPrivateFlags);
            Type progressType = generatorType.GetNestedType("ThreadProgress", _nestedPrivateFlags);
            Assert.That(blockType, Is.Not.Null);
            Assert.That(progressType, Is.Not.Null);

            Array blocks = Array.CreateInstance(blockType, 1);
            object block = Activator.CreateInstance(blockType);
            SetField(block, "Index", 0);
            SetField(block, "Width", width);
            SetField(block, "Height", height);
            SetField(block, "Depth", depth);
            blocks.SetValue(block, 0);

            object progress = Activator.CreateInstance(progressType);
            SetField(progress, "Total", 1);

            MethodInfo method = generatorType.GetMethod("PackTextureBlocks", _staticPrivateFlags);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { blocks, 1, strategy, progress, CancellationToken.None });
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
