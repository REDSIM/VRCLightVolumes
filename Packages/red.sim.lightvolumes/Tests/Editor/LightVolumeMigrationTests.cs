using System;
using System.Reflection;
using NUnit.Framework;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Udon;
using VRC.Udon.Common;

#pragma warning disable CS0618 // Migration coverage intentionally creates the obsolete 2.x authoring component.

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeMigrationTests {
        private Scene _scene;
        private string _prefabAssetPath;
        private string _sceneAssetPath;
        private UnityEngine.Object _temporaryObject;

        [TearDown]
        public void TearDown() {
            if (_scene.IsValid() && _scene.isLoaded) EditorSceneManager.CloseScene(_scene, true);
            if (!string.IsNullOrEmpty(_sceneAssetPath)) AssetDatabase.DeleteAsset(_sceneAssetPath);
            if (!string.IsNullOrEmpty(_prefabAssetPath)) AssetDatabase.DeleteAsset(_prefabAssetPath);
            if (_temporaryObject != null) UnityEngine.Object.DestroyImmediate(_temporaryObject);
        }

        // Removing an inherited legacy component must become an instance override, never a prefab edit.
        [Test]
        public void MigrationRemovesInheritedLegacyComponentWithoutChangingPrefabAsset() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _prefabAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesMigrationTest.prefab");

            GameObject source = new GameObject("Legacy Point Light Prefab");
            SceneManager.MoveGameObjectToScene(source, _scene);
            PointLightVolume sourceLegacy = source.AddComponent<PointLightVolume>();
            sourceLegacy.Intensity = 321f;
            UdonSharpUndo.AddComponent<PointLightVolumeInstance>(source);
            PrefabUtility.SaveAsPrefabAsset(source, _prefabAssetPath);
            UnityEngine.Object.DestroyImmediate(source);

            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabAssetPath);
            GameObject instanceRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, _scene);
            PointLightVolume legacy = instanceRoot.GetComponent<PointLightVolume>();
            PointLightVolumeInstance point = instanceRoot.GetComponent<PointLightVolumeInstance>();
            legacy.Intensity = 654f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(legacy);

            CreateManager(out LightVolumeSetup setup, out LightVolumeManager manager);
            setup.PointLightVolumes.Add(legacy);
            manager.PointLightVolumeInstances = new[] { point };
            point.LightVolumeManager = manager;

            int removed = MigrateScene(_scene, out int blocked);

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(blocked, Is.Zero);
            Assert.That(instanceRoot.GetComponent<PointLightVolume>(), Is.Null);
            Assert.That(instanceRoot.GetComponent<PointLightVolumeInstance>(), Is.SameAs(point));
            Assert.That(point.Intensity, Is.EqualTo(654f));
            Assert.That(PrefabUtility.GetPrefabInstanceStatus(instanceRoot), Is.EqualTo(PrefabInstanceStatus.Connected));

            var removedComponents = PrefabUtility.GetRemovedComponents(instanceRoot);
            Assert.That(removedComponents, Has.Count.EqualTo(1));
            Assert.That(removedComponents[0].assetComponent, Is.TypeOf<PointLightVolume>());

            GameObject unchangedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabAssetPath);
            Assert.That(unchangedAsset.GetComponent<PointLightVolume>(), Is.Not.Null);
            Assert.That(unchangedAsset.GetComponent<PointLightVolume>().Intensity, Is.EqualTo(321f));

            _sceneAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesMigrationPrefabTest.unity");
            Assert.That(EditorSceneManager.SaveScene(_scene, _sceneAssetPath), Is.True);
            EditorSceneManager.CloseScene(_scene, true);
            _scene = EditorSceneManager.OpenScene(_sceneAssetPath, OpenSceneMode.Single);

            GameObject reopenedRoot = Array.Find(_scene.GetRootGameObjects(), root => root.GetComponent<PointLightVolumeInstance>() != null);
            Assert.That(reopenedRoot, Is.Not.Null);
            Assert.That(reopenedRoot.GetComponent<PointLightVolume>(), Is.Null);
            Assert.That(reopenedRoot.GetComponent<PointLightVolumeInstance>().Intensity, Is.EqualTo(654f));
            Assert.That(PrefabUtility.GetPrefabInstanceStatus(reopenedRoot), Is.EqualTo(PrefabInstanceStatus.Connected));
            Assert.That(PrefabUtility.GetRemovedComponents(reopenedRoot), Has.Count.EqualTo(1));
        }

        // A proxy paired with another behaviour's Udon program must fail preflight before heap access.
        [Test]
        public void MigrationRejectsProxyWithWrongUdonProgram() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject managerObject = new GameObject("Cross-Wired Manager");
            GameObject pointObject = new GameObject("Cross-Wired Point");
            SceneManager.MoveGameObjectToScene(managerObject, _scene);
            SceneManager.MoveGameObjectToScene(pointObject, _scene);
            LightVolumeManager manager = UdonSharpUndo.AddComponent<LightVolumeManager>(managerObject);
            PointLightVolumeInstance point = UdonSharpUndo.AddComponent<PointLightVolumeInstance>(pointObject);
            var managerBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            var pointBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(point);
            Assert.That(managerBacking, Is.Not.Null);
            Assert.That(pointBacking, Is.Not.Null);

            var originalProgram = managerBacking.programSource;
            try {
                managerBacking.programSource = pointBacking.programSource;
                MethodInfo isReadyProxy = typeof(LightVolumeMigration).GetMethod(
                    "IsReadyProxy",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(isReadyProxy, Is.Not.Null);
                Assert.That((bool)isReadyProxy.Invoke(null, new object[] { manager }), Is.False);
            } finally {
                managerBacking.programSource = originalProgram;
            }
        }

        [Test]
        public void MigrationLeavesSavedUnifiedSceneClean() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject managerObject = new GameObject("Unified Manager");
            SceneManager.MoveGameObjectToScene(managerObject, _scene);
            LightVolumeManager manager = UdonSharpUndo.AddComponent<LightVolumeManager>(managerObject);
            manager.LightVolumeInstances = Array.Empty<LightVolumeInstance>();
            manager.PointLightVolumeInstances = Array.Empty<PointLightVolumeInstance>();
            UdonSharpEditorUtility.CopyProxyToUdon(manager);

            _sceneAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesMigrationCleanTest.unity");
            Assert.That(EditorSceneManager.SaveScene(_scene, _sceneAssetPath), Is.True);
            Assert.That(_scene.isDirty, Is.False);

            int removed = MigrateScene(_scene, out int blocked);

            Assert.That(removed, Is.Zero);
            Assert.That(blocked, Is.Zero);
            Assert.That(_scene.isDirty, Is.False);
        }

        [Test]
        public void ValidationAcceptsHealthyExactBidirectionalUdonPairsWithoutDirtyingScene() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject managerObject = new GameObject("Healthy Manager");
            GameObject volumeObject = new GameObject("Healthy Light Volume");
            GameObject pointObject = new GameObject("Healthy Point Light Volume");
            SceneManager.MoveGameObjectToScene(managerObject, _scene);
            SceneManager.MoveGameObjectToScene(volumeObject, _scene);
            SceneManager.MoveGameObjectToScene(pointObject, _scene);

            LightVolumeManager manager = UdonSharpUndo.AddComponent<LightVolumeManager>(managerObject);
            LightVolumeInstance volume = UdonSharpUndo.AddComponent<LightVolumeInstance>(volumeObject);
            PointLightVolumeInstance point = UdonSharpUndo.AddComponent<PointLightVolumeInstance>(pointObject);
            volume.LightVolumeManager = manager;
            point.LightVolumeManager = manager;
            manager.LightVolumeInstances = new[] { volume };
            manager.PointLightVolumeInstances = new[] { point };
            UdonSharpEditorUtility.CopyProxyToUdon(volume);
            UdonSharpEditorUtility.CopyProxyToUdon(point);
            UdonSharpEditorUtility.CopyProxyToUdon(manager);

            _sceneAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesHealthyPairsTest.unity");
            Assert.That(EditorSceneManager.SaveScene(_scene, _sceneAssetPath), Is.True);
            Assert.That(_scene.isDirty, Is.False);

            UdonBehaviour managerBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            UdonBehaviour volumeBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(volume);
            UdonBehaviour pointBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(point);
            Assert.That(managerBacking, Is.Not.Null);
            Assert.That(volumeBacking, Is.Not.Null);
            Assert.That(pointBacking, Is.Not.Null);
            Assert.That(UdonSharpEditorUtility.GetProxyBehaviour(managerBacking), Is.SameAs(manager));
            Assert.That(UdonSharpEditorUtility.GetProxyBehaviour(volumeBacking), Is.SameAs(volume));
            Assert.That(UdonSharpEditorUtility.GetProxyBehaviour(pointBacking), Is.SameAs(point));

            bool valid = LightVolumeMigration.ValidateLoadedSceneUdonPairs(out int issueCount, out string issueSummary);

            Assert.That(valid, Is.True);
            Assert.That(issueCount, Is.Zero);
            Assert.That(issueSummary, Is.Empty);
            Assert.That(_scene.isDirty, Is.False);
        }

        [Test]
        public void ValidationReportsDuplicateManagerRegistryEntryWithoutDirtyingScene() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject managerObject = new GameObject("Duplicate Registry Manager");
            GameObject volumeObject = new GameObject("Duplicate Registry Volume");
            SceneManager.MoveGameObjectToScene(managerObject, _scene);
            SceneManager.MoveGameObjectToScene(volumeObject, _scene);
            LightVolumeManager manager = UdonSharpUndo.AddComponent<LightVolumeManager>(managerObject);
            LightVolumeInstance volume = UdonSharpUndo.AddComponent<LightVolumeInstance>(volumeObject);
            volume.LightVolumeManager = manager;
            manager.LightVolumeInstances = new[] { volume, volume };
            manager.PointLightVolumeInstances = Array.Empty<PointLightVolumeInstance>();

            _sceneAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesDuplicateRegistryTest.unity");
            Assert.That(EditorSceneManager.SaveScene(_scene, _sceneAssetPath), Is.True);
            Assert.That(_scene.isDirty, Is.False);

            bool valid = LightVolumeMigration.ValidateLoadedSceneUdonPairs(out int issueCount, out string issueSummary);

            Assert.That(valid, Is.False);
            Assert.That(issueCount, Is.EqualTo(1));
            Assert.That(issueSummary, Does.Contain("contains duplicate LightVolumeInstance"));
            Assert.That(issueSummary, Does.Contain(volumeObject.name));
            Assert.That(_scene.isDirty, Is.False);
        }

        [Test]
        public void ValidationReportsCrossManagerRegistryAndMismatchedOwnershipWithoutDirtyingScene() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject firstManagerObject = new GameObject("Registry Manager A");
            GameObject secondManagerObject = new GameObject("Registry Manager B");
            GameObject volumeObject = new GameObject("Cross Registered Volume");
            SceneManager.MoveGameObjectToScene(firstManagerObject, _scene);
            SceneManager.MoveGameObjectToScene(secondManagerObject, _scene);
            SceneManager.MoveGameObjectToScene(volumeObject, _scene);
            LightVolumeManager firstManager = UdonSharpUndo.AddComponent<LightVolumeManager>(firstManagerObject);
            LightVolumeManager secondManager = UdonSharpUndo.AddComponent<LightVolumeManager>(secondManagerObject);
            LightVolumeInstance volume = UdonSharpUndo.AddComponent<LightVolumeInstance>(volumeObject);
            volume.LightVolumeManager = firstManager;
            firstManager.LightVolumeInstances = new[] { volume };
            firstManager.PointLightVolumeInstances = Array.Empty<PointLightVolumeInstance>();
            secondManager.LightVolumeInstances = new[] { volume };
            secondManager.PointLightVolumeInstances = Array.Empty<PointLightVolumeInstance>();

            _sceneAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesCrossRegistryTest.unity");
            Assert.That(EditorSceneManager.SaveScene(_scene, _sceneAssetPath), Is.True);
            Assert.That(_scene.isDirty, Is.False);

            bool valid = LightVolumeMigration.ValidateLoadedSceneUdonPairs(out int issueCount, out string issueSummary);

            Assert.That(valid, Is.False);
            Assert.That(issueCount, Is.EqualTo(3));
            Assert.That(issueSummary, Does.Contain("contains 2 Light Volume Managers"));
            Assert.That(issueSummary, Does.Contain("references manager 'Registry Manager A'"));
            Assert.That(issueSummary, Does.Contain("is registered by both"));
            Assert.That(issueSummary, Does.Contain(volumeObject.name));
            Assert.That(_scene.isDirty, Is.False);
        }

        [Test]
        public void MigrationPreservesEmptyUnownedBackingAndValidationReportsItWithoutDirtyingScene() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject managerObject = new GameObject("Manager With Empty Orphan Backing");
            SceneManager.MoveGameObjectToScene(managerObject, _scene);
            LightVolumeManager manager = UdonSharpUndo.AddComponent<LightVolumeManager>(managerObject);
            manager.LightVolumeInstances = Array.Empty<LightVolumeInstance>();
            manager.PointLightVolumeInstances = Array.Empty<PointLightVolumeInstance>();
            UdonSharpEditorUtility.CopyProxyToUdon(manager);

            UdonBehaviour healthyBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            Assert.That(healthyBacking, Is.Not.Null);
            UdonBehaviour orphanBacking = managerObject.AddComponent<UdonBehaviour>();
            orphanBacking.programSource = healthyBacking.programSource;

            _sceneAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesMigrationOrphanTest.unity");
            Assert.That(EditorSceneManager.SaveScene(_scene, _sceneAssetPath), Is.True);
            Assert.That(_scene.isDirty, Is.False);

            int removed = MigrateScene(_scene, out int blocked);

            Assert.That(removed, Is.Zero);
            Assert.That(blocked, Is.Zero);
            Assert.That(orphanBacking == null, Is.False);
            Assert.That(UdonSharpEditorUtility.GetBackingUdonBehaviour(manager), Is.SameAs(healthyBacking));
            Assert.That(managerObject.GetComponents<UdonBehaviour>(), Has.Length.EqualTo(2));
            Assert.That(_scene.isDirty, Is.False);

            bool valid = LightVolumeMigration.ValidateLoadedSceneUdonPairs(out int issueCount, out string issueSummary);

            Assert.That(valid, Is.False);
            Assert.That(issueCount, Is.EqualTo(1));
            Assert.That(issueSummary, Does.Contain("unowned LightVolumeManager backing UdonBehaviour"));
            Assert.That(issueSummary, Does.Contain(managerObject.name));
            Assert.That(_scene.isDirty, Is.False);
        }

        [Test]
        public void MigrationPreservesCustomPayloadOrphanAndValidationReportsItWithoutDirtyingScene() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject managerObject = new GameObject("Manager With Custom Orphan Backing");
            SceneManager.MoveGameObjectToScene(managerObject, _scene);
            LightVolumeManager manager = UdonSharpUndo.AddComponent<LightVolumeManager>(managerObject);
            manager.LightVolumeInstances = Array.Empty<LightVolumeInstance>();
            manager.PointLightVolumeInstances = Array.Empty<PointLightVolumeInstance>();
            UdonSharpEditorUtility.CopyProxyToUdon(manager);

            UdonBehaviour healthyBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            Assert.That(healthyBacking, Is.Not.Null);
            UdonBehaviour orphanBacking = managerObject.AddComponent<UdonBehaviour>();
            orphanBacking.programSource = healthyBacking.programSource;
            Assert.That(orphanBacking.publicVariables.TryAddVariable(new UdonVariable<int>("CustomPayload", 42)), Is.True);

            _sceneAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesMigrationCustomOrphanTest.unity");
            Assert.That(EditorSceneManager.SaveScene(_scene, _sceneAssetPath), Is.True);
            Assert.That(_scene.isDirty, Is.False);

            int removed = MigrateScene(_scene, out int blocked);

            Assert.That(removed, Is.Zero);
            Assert.That(blocked, Is.Zero);
            Assert.That(orphanBacking == null, Is.False);
            Assert.That(managerObject.GetComponents<UdonBehaviour>(), Has.Length.EqualTo(2));
            Assert.That(_scene.isDirty, Is.False);

            bool valid = LightVolumeMigration.ValidateLoadedSceneUdonPairs(out int issueCount, out string issueSummary);

            Assert.That(valid, Is.False);
            Assert.That(issueCount, Is.EqualTo(1));
            Assert.That(issueSummary, Does.Contain("unowned LightVolumeManager backing UdonBehaviour"));
            Assert.That(issueSummary, Does.Contain(managerObject.name));
            Assert.That(_scene.isDirty, Is.False);
        }

        // A coherent v2 graph has no Udon components; the one-time migration creates the complete graph.
        [Test]
        public void MigrationCreatesAndMigratesPureLegacyGraph() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject managerObject = new GameObject("Pure Legacy Setup");
            SceneManager.MoveGameObjectToScene(managerObject, _scene);
            LightVolumeSetup setup = managerObject.AddComponent<LightVolumeSetup>();

            GameObject volumeObject = new GameObject("Pure Legacy Volume");
            SceneManager.MoveGameObjectToScene(volumeObject, _scene);
            LightVolume legacyVolume = volumeObject.AddComponent<LightVolume>();
            legacyVolume.LightVolumeSetup = setup;
            legacyVolume.Intensity = 2.5f;
            setup.LightVolumes.Add(legacyVolume);
            setup.LightVolumesWeights.Add(4f);

            GameObject pointObject = new GameObject("Pure Legacy Point Light");
            SceneManager.MoveGameObjectToScene(pointObject, _scene);
            PointLightVolume legacyPoint = pointObject.AddComponent<PointLightVolume>();
            legacyPoint.LightVolumeSetup = setup;
            legacyPoint.Intensity = 654f;
            legacyPoint.Type = PointLightVolume.LightType.SpotLight;
            legacyPoint.Projection = PointLightVolume.LightProjection.Custom;
            legacyPoint.Cookie = _temporaryObject = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            setup.PointLightVolumes.Add(legacyPoint);

            int removed = MigrateScene(_scene, out int blocked);

            Assert.That(removed, Is.EqualTo(3));
            Assert.That(blocked, Is.Zero);
            Assert.That(managerObject.GetComponent<LightVolumeSetup>(), Is.Null);
            Assert.That(volumeObject.GetComponent<LightVolume>(), Is.Null);
            Assert.That(pointObject.GetComponent<PointLightVolume>(), Is.Null);

            LightVolumeManager manager = managerObject.GetComponent<LightVolumeManager>();
            LightVolumeInstance volume = volumeObject.GetComponent<LightVolumeInstance>();
            PointLightVolumeInstance point = pointObject.GetComponent<PointLightVolumeInstance>();
            Assert.That(manager, Is.Not.Null);
            Assert.That(volume.Intensity, Is.EqualTo(2.5f));
            Assert.That(volume.RegistryWeight, Is.EqualTo(4f));
            Assert.That(point.Intensity, Is.EqualTo(654f));
            Assert.That(point.LightType, Is.EqualTo(1));
            Assert.That(point.Projection, Is.EqualTo(2));
            Assert.That(point.ProjectionMode, Is.EqualTo(2));
            Assert.That(point.Cookie, Is.SameAs(_temporaryObject));
            Assert.That(point.CustomTexture, Is.SameAs(_temporaryObject));
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] { volume }));
            Assert.That(manager.PointLightVolumeInstances, Is.EqualTo(new[] { point }));
            Assert.That(UdonSharpEditorUtility.GetBackingUdonBehaviour(manager), Is.Not.Null);
            Assert.That(UdonSharpEditorUtility.GetBackingUdonBehaviour(volume), Is.Not.Null);
            Assert.That(UdonSharpEditorUtility.GetBackingUdonBehaviour(point), Is.Not.Null);
        }

        // Once any counterpart exists, migration validates it but never creates a duplicate or repairs backing state.
        [Test]
        public void MigrationDoesNotRepairBrokenExistingProxy() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject managerObject = new GameObject("Legacy Setup With Broken Proxy");
            SceneManager.MoveGameObjectToScene(managerObject, _scene);
            LightVolumeSetup setup = managerObject.AddComponent<LightVolumeSetup>();

            GameObject pointObject = new GameObject("Legacy Point With Broken Proxy");
            SceneManager.MoveGameObjectToScene(pointObject, _scene);
            PointLightVolume legacy = pointObject.AddComponent<PointLightVolume>();
            legacy.LightVolumeSetup = setup;
            setup.PointLightVolumes.Add(legacy);
            PointLightVolumeInstance broken = UdonSharpUndo.AddComponent<PointLightVolumeInstance>(pointObject);
            UnityEngine.Object.DestroyImmediate(UdonSharpEditorUtility.GetBackingUdonBehaviour(broken));
            Assert.That(UdonSharpEditorUtility.GetBackingUdonBehaviour(broken) == null, Is.True);

            int removed = MigrateScene(_scene, out int blocked);

            Assert.That(removed, Is.Zero);
            Assert.That(blocked, Is.EqualTo(2));
            Assert.That(managerObject.GetComponents<LightVolumeManager>(), Is.Empty);
            Assert.That(pointObject.GetComponent<PointLightVolume>(), Is.SameAs(legacy));
            Assert.That(pointObject.GetComponents<PointLightVolumeInstance>(), Has.Length.EqualTo(1));
            Assert.That(pointObject.GetComponent<PointLightVolumeInstance>(), Is.SameAs(broken));
            Assert.That(UdonSharpEditorUtility.GetBackingUdonBehaviour(broken) == null, Is.True);
        }

        private void CreateManager(out LightVolumeSetup setup, out LightVolumeManager manager) {
            GameObject managerObject = new GameObject("Light Volume Manager");
            SceneManager.MoveGameObjectToScene(managerObject, _scene);
            setup = managerObject.AddComponent<LightVolumeSetup>();
            manager = UdonSharpUndo.AddComponent<LightVolumeManager>(managerObject);
            setup.LightVolumeManager = manager;
            manager.LightVolumeInstances = Array.Empty<LightVolumeInstance>();
            manager.PointLightVolumeInstances = Array.Empty<PointLightVolumeInstance>();
        }

        private static int MigrateScene(Scene scene, out int blocked) {
            MethodInfo method = typeof(LightVolumeMigration).GetMethod(
                "MigrateScene",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] arguments = { scene, 0 };
            int removed = (int)method.Invoke(null, arguments);
            blocked = (int)arguments[1];
            return removed;
        }
    }
}

#pragma warning restore CS0618
