using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace VRCLightVolumes.Tests {
    [Category("Udon")]
    public class LightVolumeTextureCacheOptimizationTests {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<Object> _createdObjects = new List<Object>();

        // Releases only the inactive fixture objects; these tests do not run texture blits or asynchronous readbacks.
        [TearDown]
        public void TearDown() {
            for (int i = _createdObjects.Count - 1; i >= 0; i--) {
                Object target = _createdObjects[i];
                if (target != null) Object.DestroyImmediate(target);
            }
            _createdObjects.Clear();
        }

        // An independent pair-key oracle checks distinct sources, sharing, live/snapshot aliases, reordering and retained-capacity reuse in all four source blocks.
        [TestCase(false, false, 0)]
        [TestCase(false, true, 0)]
        [TestCase(true, false, 0)]
        [TestCase(true, true, 0)]
        [TestCase(false, false, 1)]
        [TestCase(false, true, 1)]
        [TestCase(true, false, 1)]
        [TestCase(true, true, 1)]
        [TestCase(false, false, 2)]
        [TestCase(false, true, 2)]
        [TestCase(true, false, 2)]
        [TestCase(true, true, 2)]
        public void CustomSourcePairLookupPreservesFirstOccurrenceIds(bool cubemap, bool material, int shape) {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>("Custom Pair Cache Manager");
            int sourceCount = shape == 0 ? 128 : shape == 1 ? 1 : 17;
            Object[] sources = new Object[sourceCount];
            for (int i = 0; i < sourceCount; i++) sources[i] = CreateSource(material);
            PointLightVolumeInstance[] points = new PointLightVolumeInstance[128];
            for (int i = 0; i < points.Length; i++) {
                PointLightVolumeInstance point = CreateComponent<PointLightVolumeInstance>("Custom Pair " + i);
                point.IsActive = true;
                point.LightType = cubemap ? 0 : 1;
                point.ProjectionMode = 2;
                point.AutoUpdateCustomTexture = (i & 1) != 0;
                Object source = sources[shape == 0 ? i : (i * 7) % sourceCount];
                if (material) point.CustomTextureMaterial = (Material)source;
                else point.CustomTexture = (Texture)source;
                points[i] = point;
            }

            manager.PointLightVolumeInstances = points;
            AssertCustomCacheMatchesPairOracle(manager, cubemap, material);
            int[] retainedIds = GetField<int[]>(manager, "_pointLightCustomIDs");
            System.Array.Reverse(points);
            points[4].IsActive = false;
            points[7].ProjectionMode = 0;
            points[11] = null;
            points[13].CustomTexture = null;
            points[13].CustomTextureMaterial = null;
            AssertCustomCacheMatchesPairOracle(manager, cubemap, material);
            Assert.That(GetField<int[]>(manager, "_pointLightCustomIDs"), Is.SameAs(retainedIds));

            manager.PointLightVolumeInstances = new[] { points[0] };
            AssertCustomCacheMatchesPairOracle(manager, cubemap, material);
            for (int i = 1; i < retainedIds.Length; i++) Assert.That(retainedIds[i], Is.EqualTo(-1), "Shrinking must clear stale ID capacity.");
            manager.PointLightVolumeInstances = points;
            AssertCustomCacheMatchesPairOracle(manager, cubemap, material);
            Assert.That(GetField<int[]>(manager, "_pointLightCustomIDs"), Is.SameAs(retainedIds));
        }

        // Mixed blocks retain their cube-first offsets, and unusual public type/mode values retain the existing single-source fallback rather than acquiring a new validation policy.
        [Test]
        public void CustomSourcePairLookupKeepsMixedBlockOffsetsAndUnusualModes() {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>("Mixed Pair Cache Manager");
            Texture texture = (Texture)CreateSource(false);
            Material material = (Material)CreateSource(true);
            int[] lightTypes = { 0, 0, 0, 1, 0, 1, -9, 2, 123, 1 };
            int[] projectionModes = { 2, 2, 2, 2, 2, 2, 37, 2, -1, 0 };
            bool[] materials = { false, false, true, false, true, false, false, true, true, false };
            bool[] autoUpdates = { false, true, false, false, true, true, false, false, true, true };
            PointLightVolumeInstance[] points = new PointLightVolumeInstance[lightTypes.Length];
            for (int i = 0; i < points.Length; i++) {
                PointLightVolumeInstance point = CreateComponent<PointLightVolumeInstance>("Mixed Pair " + i);
                point.IsActive = true;
                point.LightType = lightTypes[i];
                point.ProjectionMode = projectionModes[i];
                point.AutoUpdateCustomTexture = autoUpdates[i];
                if (materials[i]) point.CustomTextureMaterial = material;
                else point.CustomTexture = texture;
                points[i] = point;
            }
            manager.PointLightVolumeInstances = points;
            typeof(LightVolumeManager).GetMethod("BuildCustomTextureSourceCache", PrivateInstance).Invoke(manager, null);
            Assert.That(GetField<int[]>(manager, "_pointLightCustomIDs"), Is.EqualTo(new[] { 0, 1, 2, 4, 3, 5, 4, 6, 7, -1 }));
            Assert.That(manager.CubemapsCount, Is.EqualTo(4));
            Assert.That(GetField<int>(manager, "_customTextureArrayDepth"), Is.EqualTo(28));
            Assert.That(GetField<bool>(manager, "_customTexturesUseMipMap"), Is.True);
        }

        // Shared cookie users may include unselected areas and non-area profiles; stale reverse hints must never redirect a readback to the wrong packed slot.
        [TestCase(false)]
        [TestCase(true)]
        public void AreaCookieReadbackJoinsOnlyMatchingAreaUsersAndRepairsStaleReverseHints(bool shortFallbackCache) {
            LightVolumeManager manager = CreateComponent<LightVolumeManager>("Readback Join Manager");
            PointLightVolumeInstance[] points = new PointLightVolumeInstance[128];
            int[] customIds = new int[128];
            for (int i = 0; i < points.Length; i++) {
                points[i] = CreateComponent<PointLightVolumeInstance>("Readback Join " + i);
                points[i].LightType = 2;
                points[i].ProjectionMode = 2;
                points[i].AreaLightFallbackColor = Color.magenta;
                customIds[i] = -1;
            }
            customIds[2] = customIds[33] = customIds[71] = customIds[100] = customIds[127] = 5;
            points[33].LightType = 1;
            points[127].ProjectionMode = 1;
            manager.PointLightVolumeInstances = points;
            SetField(manager, "_pointLightCustomIDs", customIds);
            Color[] averageColors = new Color[shortFallbackCache ? 64 : 128];
            SetField(manager, "_pointLightAreaCookieAverageColors", averageColors);
            int[] shaderSources = GetField<int[]>(manager, "_enabledPointIDs");
            shaderSources[0] = 71;
            shaderSources[1] = 33;
            shaderSources[2] = 100;
            SetField(manager, "_pointLightCount", 3);
            int[] reverseMap = new int[128];
            reverseMap[71] = 2; // Valid index, wrong source: the canonical ID check must reject it.
            reverseMap[100] = 2;
            SetField(manager, "_pointLightRegistryToShaderIndex", reverseMap);
            Vector4[] packedColors = GetField<Vector4[]>(manager, "_pointLightColor");
            Vector4[] extraData = GetField<Vector4[]>(manager, "_pointLightExtraData");
            for (int i = 0; i < 3; i++) {
                extraData[i] = new Vector4(i + 1f, i + 2f, i + 3f, 0f);
                packedColors[i] = new Vector4(8f, 9f, 10f, 20f + i);
            }
            Vector4 spotBefore = packedColors[1];
            MethodInfo upload = typeof(LightVolumeManager).GetMethod("UploadAreaCookieAverageColor", PrivateInstance);
            Color sourceColor = new Color(0.2f, 0.4f, 0.8f, 0.5f);
            Color expected = new Color(sourceColor.r * sourceColor.a, sourceColor.g * sourceColor.a, sourceColor.b * sourceColor.a, 1f);

            Assert.That((bool)upload.Invoke(manager, new object[] { 5, sourceColor }), Is.True);
            Assert.That(points[2].AreaLightFallbackColor, Is.EqualTo(expected), "An unselected area still needs its retained fallback.");
            Assert.That(averageColors[2], Is.EqualTo(expected));
            Assert.That(points[33].AreaLightFallbackColor, Is.EqualTo(Color.magenta));
            Assert.That(points[127].AreaLightFallbackColor, Is.EqualTo(Color.magenta));
            Assert.That(packedColors[1], Is.EqualTo(spotBefore), "A shared Spot cookie must never receive Area fallback modulation.");
            Assert.That(reverseMap[71], Is.EqualTo(0), "The validated fallback repairs the stale hint.");
            Assert.That(points[71].AreaLightFallbackColor, Is.EqualTo(shortFallbackCache ? Color.magenta : expected));
            for (int shaderIndex = 0; shaderIndex <= 2; shaderIndex += 2) {
                Vector4 extra = extraData[shaderIndex];
                Vector4 expectedPacked = new Vector4(extra.x * expected.r, extra.y * expected.g, extra.z * expected.b, 20f + shaderIndex);
                Assert.That(packedColors[shaderIndex].Equals(expectedPacked), Is.True, "A short fallback cache must not prevent valid packed-slot publication.");
            }

            SetField(manager, "_pointLightCount", 0);
            Assert.That((bool)upload.Invoke(manager, new object[] { 5, sourceColor }), Is.False, "A source without any shader-visible Area target still requests the canonical rebuild.");
            Assert.That((bool)upload.Invoke(manager, new object[] { 99, sourceColor }), Is.False);
        }

        // Builds the real cache and compares every mapping with a simple independently maintained list of source/update-mode pairs.
        private static void AssertCustomCacheMatchesPairOracle(LightVolumeManager manager, bool cubemap, bool material) {
            typeof(LightVolumeManager).GetMethod("BuildCustomTextureSourceCache", PrivateInstance).Invoke(manager, null);
            int[] actualIds = GetField<int[]>(manager, "_pointLightCustomIDs");
            List<Object> uniqueSources = new List<Object>();
            List<bool> uniqueModes = new List<bool>();
            bool hasAutoUpdates = false;
            for (int i = 0; i < manager.PointLightVolumeInstances.Length; i++) {
                PointLightVolumeInstance point = manager.PointLightVolumeInstances[i];
                Object source = point == null ? null : material ? (Object)point.CustomTextureMaterial : point.CustomTexture;
                if (point == null || !point.IsActive || point.ProjectionMode == 0 || source == null) {
                    Assert.That(actualIds[i], Is.EqualTo(-1));
                    continue;
                }
                int expectedId = -1;
                for (int j = 0; j < uniqueSources.Count; j++) {
                    if (uniqueSources[j] != source || uniqueModes[j] != point.AutoUpdateCustomTexture) continue;
                    expectedId = j;
                    break;
                }
                if (expectedId < 0) {
                    expectedId = uniqueSources.Count;
                    uniqueSources.Add(source);
                    uniqueModes.Add(point.AutoUpdateCustomTexture);
                }
                hasAutoUpdates |= point.AutoUpdateCustomTexture;
                Assert.That(actualIds[i], Is.EqualTo(expectedId), "Registry index " + i);
            }
            string block = cubemap ? "Cubemap" : "Single";
            string kind = material ? "Material" : "Texture";
            Assert.That(GetField<int>(manager, "_custom" + block + kind + "Count"), Is.EqualTo(uniqueSources.Count));
            bool[] actualModes = GetField<bool[]>(manager, "_custom" + block + kind + "AutoUpdates");
            for (int i = 0; i < uniqueModes.Count; i++) Assert.That(actualModes[i], Is.EqualTo(uniqueModes[i]));
            Assert.That(manager.CubemapsCount, Is.EqualTo(cubemap ? uniqueSources.Count : 0));
            Assert.That(GetField<int>(manager, "_customTextureArrayDepth"), Is.EqualTo(uniqueSources.Count * (cubemap ? 6 : 1)));
            Assert.That(manager.HasAutoCustomTextureUpdates, Is.EqualTo(hasAutoUpdates));
        }

        // Keeps lifecycle callbacks out of cache-only fixtures while retaining genuine Unity source references.
        private T CreateComponent<T>(string name) where T : Component {
            GameObject gameObject = new GameObject(name);
            gameObject.SetActive(false);
            _createdObjects.Add(gameObject);
            return gameObject.AddComponent<T>();
        }

        // Source layout is deliberately irrelevant to pair identity; physical layout is validated by the separate blit tests.
        private Object CreateSource(bool material) {
            Object source;
            if (material) {
                Shader shader = Shader.Find("Unlit/Color");
                Assert.That(shader, Is.Not.Null);
                source = new Material(shader);
            } else source = new Texture2D(1, 1);
            _createdObjects.Add(source);
            return source;
        }

        // Reads private derived state without widening the runtime or Udon API.
        private static T GetField<T>(LightVolumeManager manager, string name) {
            return (T)typeof(LightVolumeManager).GetField(name, PrivateInstance).GetValue(manager);
        }

        // Supplies a controlled derived-state fixture without invoking render-target allocation or editor authoring coordination.
        private static void SetField<T>(LightVolumeManager manager, string name, T value) {
            typeof(LightVolumeManager).GetField(name, PrivateInstance).SetValue(manager, value);
        }
    }
}
