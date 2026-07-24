using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeEditorPipelineTests {
        private const float Epsilon = 0.0001f;

        private GameObject _legacyObject;
        private GameObject _unifiedObject;

        [TearDown]
        public void TearDown() {
            if (_legacyObject != null) UnityEngine.Object.DestroyImmediate(_legacyObject);
            if (_unifiedObject != null) UnityEngine.Object.DestroyImmediate(_unifiedObject);
        }

        // The unified Udon component owns every persistent Light Volume authoring value.
        [Test]
        public void MigrationCopiesLegacyLightVolumePersistentData() {
            _legacyObject = new GameObject("Legacy Light Volume");
            _unifiedObject = new GameObject("Unified Light Volume");
            LightVolume legacy = _legacyObject.AddComponent<LightVolume>();
            LightVolumeInstance unified = _unifiedObject.AddComponent<LightVolumeInstance>();
            Texture3D texture0 = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false);
            Texture3D texture1 = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false);
            Texture3D texture2 = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false);

            legacy.Dynamic = true;
            legacy.Additive = true;
            legacy.Color = new Color(0.25f, 0.5f, 0.75f);
            legacy.Intensity = 2.5f;
            legacy.SmoothBlending = 0.4f;
            legacy.Texture0 = texture0;
            legacy.Texture1 = texture1;
            legacy.Texture2 = texture2;
            legacy.Exposure = 1.25f;
            legacy.Shadows = -0.2f;
            legacy.Highlights = 0.3f;
            legacy.Bake = false;
            legacy.ReserveUVSpace = true;
            legacy.AdaptiveResolution = false;
            legacy.VoxelsPerUnit = 4.5f;
            legacy.Resolution = new Vector3Int(7, 8, 9);

            MethodInfo copy = typeof(LightVolumeMigration).GetMethod("CopyLegacyLightVolume", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(copy, Is.Not.Null);
            copy.Invoke(null, new object[] { legacy, unified });

            Assert.That(unified.IsDynamic, Is.True);
            Assert.That(unified.IsAdditive, Is.True);
            Assert.That(unified.Color, Is.EqualTo(legacy.Color));
            Assert.That(unified.Intensity, Is.EqualTo(legacy.Intensity).Within(Epsilon));
            Assert.That(unified.SmoothBlending, Is.EqualTo(legacy.SmoothBlending).Within(Epsilon));
            Assert.That(unified.Texture0, Is.SameAs(texture0));
            Assert.That(unified.Texture1, Is.SameAs(texture1));
            Assert.That(unified.Texture2, Is.SameAs(texture2));
            Assert.That(unified.Exposure, Is.EqualTo(legacy.Exposure).Within(Epsilon));
            Assert.That(unified.Shadows, Is.EqualTo(legacy.Shadows).Within(Epsilon));
            Assert.That(unified.Highlights, Is.EqualTo(legacy.Highlights).Within(Epsilon));
            Assert.That(unified.Bake, Is.False);
            Assert.That(unified.ReserveUVSpace, Is.True);
            Assert.That(unified.AdaptiveResolution, Is.False);
            Assert.That(unified.VoxelsPerUnit, Is.EqualTo(legacy.VoxelsPerUnit).Within(Epsilon));
            Assert.That(unified.Resolution, Is.EqualTo(legacy.Resolution));

            UnityEngine.Object.DestroyImmediate(texture0);
            UnityEngine.Object.DestroyImmediate(texture1);
            UnityEngine.Object.DestroyImmediate(texture2);
        }

        [Test]
        public void UnifiedComponentInspectorTitlesUsePublicNames() {
            AddComponentMenu volume = typeof(LightVolumeInstance).GetCustomAttribute<AddComponentMenu>();
            AddComponentMenu point = typeof(PointLightVolumeInstance).GetCustomAttribute<AddComponentMenu>();
            AddComponentMenu manager = typeof(LightVolumeManager).GetCustomAttribute<AddComponentMenu>();

            Assert.That(volume?.componentMenu, Is.EqualTo("VRC Light Volumes/Light Volume (U# Script)"));
            Assert.That(point?.componentMenu, Is.EqualTo("VRC Light Volumes/Point Light Volume (U# Script)"));
            Assert.That(manager?.componentMenu, Is.EqualTo("VRC Light Volumes/Light Volume Manager (U# Script)"));
        }

        // A unique co-located Udon component is authoritative even when an obsolete serialized link points elsewhere.
        [Test]
        public void MigrationResolverUsesUniqueCoLocatedDestination() {
            _legacyObject = new GameObject("Legacy Pair Source");
            _unifiedObject = new GameObject("Foreign Unified Destination");
            LightVolume legacy = _legacyObject.AddComponent<LightVolume>();
            LightVolumeInstance attached = _legacyObject.AddComponent<LightVolumeInstance>();
            LightVolumeInstance foreign = _unifiedObject.AddComponent<LightVolumeInstance>();
            MethodInfo resolve = typeof(LightVolumeMigration).GetMethod(
                "ResolveLightVolumeInstance",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(resolve, Is.Not.Null);

            legacy.LightVolumeInstance = foreign;
            Assert.That(resolve.Invoke(null, new object[] { legacy }), Is.SameAs(attached));

            legacy.LightVolumeInstance = attached;
            Assert.That(resolve.Invoke(null, new object[] { legacy }), Is.SameAs(attached));
        }

        // Resolving an incomplete old payload is read-only and must never create a replacement Udon component.
        [Test]
        public void MigrationResolverDoesNotCreateMissingDestination() {
            _legacyObject = new GameObject("Incomplete Migration Source");
            LightVolume legacy = _legacyObject.AddComponent<LightVolume>();
            MethodInfo resolve = typeof(LightVolumeMigration).GetMethod(
                "ResolveLightVolumeInstance",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(resolve, Is.Not.Null);
            Assert.That(resolve.Invoke(null, new object[] { legacy }), Is.Null);
            Assert.That(_legacyObject.GetComponents<LightVolumeInstance>(), Is.Empty);
        }

        // Applying the same compatibility payload twice must produce the same unified serialized state.
        [Test]
        public void MigrationPointLightPayloadCopyIsIdempotent() {
            _legacyObject = new GameObject("Point Light Migration Source");
            _unifiedObject = new GameObject("Point Light Migration Destination");
            PointLightVolume source = _legacyObject.AddComponent<PointLightVolume>();
            PointLightVolumeInstance destination = _unifiedObject.AddComponent<PointLightVolumeInstance>();
            source.Dynamic = true;
            source.Type = PointLightVolume.LightType.SpotLight;
            source.Projection = PointLightVolume.LightProjection.Custom;
            source.Range = 17f;
            source.Intensity = 3.5f;
            source.Angle = 42f;
            source.Falloff = 0.6f;
            source.Shadows = true;
            source.BakeInGame = true;
            MethodInfo copy = typeof(LightVolumeMigration).GetMethod(
                "CopyLegacyPointLight",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(copy, Is.Not.Null);
            copy.Invoke(null, new object[] { source, destination });
            string firstState = JsonUtility.ToJson(destination);
            copy.Invoke(null, new object[] { source, destination });

            Assert.That(JsonUtility.ToJson(destination), Is.EqualTo(firstState));
            Assert.That(destination.LightType, Is.EqualTo(1));
            Assert.That(destination.Intensity, Is.EqualTo(3.5f).Within(Epsilon));
        }

        // Resolution validation belongs to unified authoring and rejects invalid or overflowing grids.
        [Test]
        public void UnifiedVoxelCountRejectsInvalidAndOverflowingResolution() {
            Assert.That(LightVolumeTools.GetVoxelCount(new Vector3Int(2, 3, 4)), Is.EqualTo(24));
            Assert.That(LightVolumeTools.GetVoxelCount(new Vector3Int(0, 3, 4)), Is.EqualTo(-1));
            Assert.That(LightVolumeTools.GetVoxelCount(new Vector3Int(int.MaxValue, 2, 2)), Is.EqualTo(-1));
        }
    }
}
