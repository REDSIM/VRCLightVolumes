using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VRC.Udon;

#pragma warning disable CS0618

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeSceneSetupTests {
        private Scene _scene;
        private string _prefabAssetPath;
        private string _sceneAssetPath;
        private readonly List<ObjectChangeKind> _capturedObjectChanges = new List<ObjectChangeKind>();

        [TearDown]
        public void TearDown() {
            LightVolumeEditorUpdater.FlushPendingSceneChanges();
            if (_scene.IsValid() && _scene.isLoaded) EditorSceneManager.CloseScene(_scene, true);
            if (!string.IsNullOrEmpty(_sceneAssetPath)) AssetDatabase.DeleteAsset(_sceneAssetPath);
            if (!string.IsNullOrEmpty(_prefabAssetPath)) AssetDatabase.DeleteAsset(_prefabAssetPath);
        }

        [Test]
        public void UnifiedPrefabCreatesOneManagerAndRegistersBothVolumeTypes() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject prefabAsset = CreatePrefab(includeLegacyHelpers: false, includeUnifiedComponents: true);
            Hash128 assetHash = AssetDatabase.GetAssetDependencyHash(_prefabAssetPath);

            GameObject instanceRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, _scene);
            QueueAndFlush(instanceRoot);

            LightVolumeManager manager = GetSingleSceneComponent<LightVolumeManager>();
            LightVolumeInstance volume = instanceRoot.GetComponentInChildren<LightVolumeInstance>(true);
            PointLightVolumeInstance pointLight = instanceRoot.GetComponentInChildren<PointLightVolumeInstance>(true);
            AssertRegistered(manager, volume, pointLight);
            AssertBackingManager(volume, manager);
            AssertBackingManager(pointLight, manager);

            QueueAndFlush(instanceRoot);
            Assert.That(GetSceneComponents<LightVolumeManager>(), Has.Count.EqualTo(1));
            AssertRegistered(manager, volume, pointLight);

            AssetDatabase.SaveAssets();
            Assert.That(AssetDatabase.GetAssetDependencyHash(_prefabAssetPath), Is.EqualTo(assetHash));
            GameObject unchangedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabAssetPath);
            Assert.That(unchangedAsset.GetComponentInChildren<LightVolumeInstance>(true).LightVolumeManager, Is.Null);
            Assert.That(unchangedAsset.GetComponentInChildren<PointLightVolumeInstance>(true).LightVolumeManager, Is.Null);

            SaveAndReopenScene();
            instanceRoot = FindPrefabInstanceRoot();
            manager = GetSingleSceneComponent<LightVolumeManager>();
            volume = instanceRoot.GetComponentInChildren<LightVolumeInstance>(true);
            pointLight = instanceRoot.GetComponentInChildren<PointLightVolumeInstance>(true);
            Assert.That(PrefabUtility.GetPrefabInstanceStatus(instanceRoot), Is.EqualTo(PrefabInstanceStatus.Connected));
            AssertRegistered(manager, volume, pointLight);
            AssertBackingManager(volume, manager);
            AssertBackingManager(pointLight, manager);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnifiedSingleTypePrefabRegistersOnlyItsPresentType(bool pointLightOnly) {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject prefabAsset = CreatePrefab(
                includeLegacyHelpers: false,
                includeUnifiedComponents: true,
                includeVolume: !pointLightOnly,
                includePointLight: pointLightOnly);
            GameObject instanceRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, _scene);

            QueueAndFlush(instanceRoot);

            LightVolumeManager manager = GetSingleSceneComponent<LightVolumeManager>();
            LightVolumeInstance volume = instanceRoot.GetComponentInChildren<LightVolumeInstance>(true);
            PointLightVolumeInstance pointLight = instanceRoot.GetComponentInChildren<PointLightVolumeInstance>(true);
            if (pointLightOnly) {
                Assert.That(manager.LightVolumeInstances ?? Array.Empty<LightVolumeInstance>(), Is.Empty);
                Assert.That(manager.PointLightVolumeInstances, Is.EqualTo(new[] { pointLight }));
            } else {
                Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] { volume }));
                Assert.That(manager.PointLightVolumeInstances ?? Array.Empty<PointLightVolumeInstance>(), Is.Empty);
            }
            AssertBackingManager(pointLightOnly ? (Component)pointLight : volume, manager);
        }

        [Test]
        public void RecreatedManagerRegistersEveryExistingSceneVolume() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject oldVolumeObject = new GameObject("Existing Volume");
            SceneManager.MoveGameObjectToScene(oldVolumeObject, _scene);
            LightVolumeInstance oldVolume = UdonSharpUndo.AddComponent<LightVolumeInstance>(oldVolumeObject);
            GameObject oldPointObject = new GameObject("Existing Point Light");
            SceneManager.MoveGameObjectToScene(oldPointObject, _scene);
            PointLightVolumeInstance oldPoint = UdonSharpUndo.AddComponent<PointLightVolumeInstance>(oldPointObject);
            oldPointObject.SetActive(false);

            QueueAndFlush(oldVolumeObject);
            QueueAndFlush(oldPointObject);
            LightVolumeManager previousManager = GetSingleSceneComponent<LightVolumeManager>();
            Undo.DestroyObjectImmediate(previousManager.gameObject);
            Assert.That(GetSceneComponents<LightVolumeManager>(), Is.Empty);
            Assert.That(oldVolume.LightVolumeManager == null, Is.True);
            Assert.That(oldPoint.LightVolumeManager == null, Is.True);

            GameObject newVolumeObject = new GameObject("New Volume");
            SceneManager.MoveGameObjectToScene(newVolumeObject, _scene);
            LightVolumeInstance newVolume = UdonSharpUndo.AddComponent<LightVolumeInstance>(newVolumeObject);
            QueueAndFlush(newVolumeObject);

            LightVolumeManager manager = GetSingleSceneComponent<LightVolumeManager>();
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] { oldVolume, newVolume }));
            Assert.That(manager.PointLightVolumeInstances, Is.EqualTo(new[] { oldPoint }));
            Assert.That(oldVolume.RegistryOrder, Is.Zero);
            Assert.That(newVolume.RegistryOrder, Is.EqualTo(1));
            Assert.That(oldPoint.RegistryOrder, Is.Zero);
            Assert.That(oldVolume.LightVolumeManager, Is.SameAs(manager));
            Assert.That(newVolume.LightVolumeManager, Is.SameAs(manager));
            Assert.That(oldPoint.LightVolumeManager, Is.SameAs(manager));
            AssertBackingManager(oldVolume, manager);
            AssertBackingManager(newVolume, manager);
            AssertBackingManager(oldPoint, manager);

            QueueAndFlush(newVolumeObject);
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] { oldVolume, newVolume }));
            Assert.That(manager.PointLightVolumeInstances, Is.EqualTo(new[] { oldPoint }));
        }

#if BAKERY_INCLUDED
        [Test]
        public void UnifiedPrefabCreatesItsBakeryDependencyWhenTheSceneManagerUsesBakery() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject managerObject = new GameObject("Existing Light Volume Manager");
            SceneManager.MoveGameObjectToScene(managerObject, _scene);
            LightVolumeManager manager = UdonSharpUndo.AddComponent<LightVolumeManager>(managerObject);
            manager.BakingMode = 1;
            LightVolumeManagerTools.CopyProxyToUdon(manager);
            GameObject prefabAsset = CreatePrefab(
                includeLegacyHelpers: false,
                includeUnifiedComponents: true,
                includePointLight: false);
            GameObject instanceRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, _scene);

            QueueAndFlush(instanceRoot);
            QueueAndFlush(instanceRoot);

            LightVolumeInstance volume = instanceRoot.GetComponentInChildren<LightVolumeInstance>(true);
            Assert.That(GetSceneComponents<LightVolumeManager>(), Is.EqualTo(new[] { manager }));
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] { volume }));
            Transform bakeryHelper = volume.transform.Find($"Bakery Volume - {volume.gameObject.name}");
            Assert.That(bakeryHelper, Is.Not.Null);
            Assert.That(bakeryHelper.CompareTag("EditorOnly"), Is.True);
            Assert.That(bakeryHelper.GetComponent("BakeryVolume"), Is.Not.Null);
        }

        [Test]
        public void ProgressiveOnboardingPreservesAnInheritedBakeryDependency() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreatePrefab(
                includeLegacyHelpers: false,
                includeUnifiedComponents: true,
                includePointLight: false,
                includeBakeryDependency: true);
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabAssetPath);
            GameObject instanceRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, _scene);
            LightVolumeInstance volume = instanceRoot.GetComponentInChildren<LightVolumeInstance>(true);
            Component inheritedDependency = volume.BakeryVolume;

            QueueAndFlush(instanceRoot);

            LightVolumeManager manager = GetSingleSceneComponent<LightVolumeManager>();
            Assert.That(manager.IsBakeryMode, Is.False);
            Assert.That(volume.BakeryVolume, Is.SameAs(inheritedDependency));
            Assert.That(inheritedDependency, Is.Not.Null);
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(inheritedDependency), Is.Not.Null);
            Assert.That(PrefabUtility.GetRemovedComponents(instanceRoot), Is.Empty);
        }
#endif

        [UnityTest]
        public IEnumerator PrefabInstantiationEventRunsAutomaticOnboarding() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject prefabAsset = CreatePrefab(includeLegacyHelpers: false, includeUnifiedComponents: true);
            yield return null;
            yield return null;
            LightVolumeEditorUpdater.FlushPendingSceneChanges();
            Assert.That(GetSceneComponents<LightVolumeManager>(), Is.Empty);
            _capturedObjectChanges.Clear();
            ObjectChangeEvents.changesPublished += CaptureObjectChanges;
            GameObject instanceRoot;
            try {
                instanceRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, _scene);
                Undo.RegisterCreatedObjectUndo(instanceRoot, "Instantiate Light Volumes Prefab");
                for (int i = 0; i < 30 && GetSceneComponents<LightVolumeManager>().Count == 0; i++) yield return null;
            } finally {
                ObjectChangeEvents.changesPublished -= CaptureObjectChanges;
            }

            List<LightVolumeManager> managers = GetSceneComponents<LightVolumeManager>();
            Assert.That(managers, Has.Count.EqualTo(1), $"Published changes: {string.Join(", ", _capturedObjectChanges)}");
            Assert.That(_capturedObjectChanges, Does.Contain(ObjectChangeKind.CreateGameObjectHierarchy));
            LightVolumeManager manager = managers[0];
            AssertRegistered(
                manager,
                instanceRoot.GetComponentInChildren<LightVolumeInstance>(true),
                instanceRoot.GetComponentInChildren<PointLightVolumeInstance>(true));
        }

        [UnityTest]
        public IEnumerator AddingComponentsRunsAutomaticOnboarding() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject volumeObject = new GameObject("Volume Target");
            GameObject pointLightObject = new GameObject("Point Light Target");
            Undo.RegisterCreatedObjectUndo(volumeObject, "Create Volume Target");
            Undo.RegisterCreatedObjectUndo(pointLightObject, "Create Point Light Target");
            yield return null;
            yield return null;
            LightVolumeEditorUpdater.FlushPendingSceneChanges();
            Assert.That(GetSceneComponents<LightVolumeManager>(), Is.Empty);

            _capturedObjectChanges.Clear();
            ObjectChangeEvents.changesPublished += CaptureObjectChanges;
            LightVolumeInstance volume = null;
            try {
                volume = UdonSharpUndo.AddComponent<LightVolumeInstance>(volumeObject);
                for (int i = 0; i < 30 && GetSceneComponents<LightVolumeManager>().Count == 0; i++) yield return null;
            } finally {
                ObjectChangeEvents.changesPublished -= CaptureObjectChanges;
            }

            Assert.That(_capturedObjectChanges, Does.Contain(ObjectChangeKind.ChangeGameObjectStructure));
            LightVolumeManager manager = GetSingleSceneComponent<LightVolumeManager>();
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] { volume }));
            Assert.That(volume.LightVolumeManager, Is.SameAs(manager));
            AssertBackingManager(volume, manager);

            _capturedObjectChanges.Clear();
            ObjectChangeEvents.changesPublished += CaptureObjectChanges;
            PointLightVolumeInstance pointLight = null;
            try {
                pointLight = UdonSharpUndo.AddComponent<PointLightVolumeInstance>(pointLightObject);
                for (int i = 0; i < 30 && pointLight.LightVolumeManager == null; i++) yield return null;
            } finally {
                ObjectChangeEvents.changesPublished -= CaptureObjectChanges;
            }

            Assert.That(_capturedObjectChanges, Does.Contain(ObjectChangeKind.ChangeGameObjectStructure));
            Assert.That(GetSceneComponents<LightVolumeManager>(), Is.EqualTo(new[] { manager }));
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] { volume }));
            Assert.That(manager.PointLightVolumeInstances, Is.EqualTo(new[] { pointLight }));
            Assert.That(pointLight.LightVolumeManager, Is.SameAs(manager));
            AssertBackingManager(pointLight, manager);
        }

        [UnityTest]
        public IEnumerator OpeningSceneReconcilesManagerlessPrefabAfterLegacyMigrationPass() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject prefabAsset = CreatePrefab(includeLegacyHelpers: false, includeUnifiedComponents: true);
            PrefabUtility.InstantiatePrefab(prefabAsset, _scene);
            _sceneAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesSceneSetupReopenTest.unity");
            Assert.That(EditorSceneManager.SaveScene(_scene, _sceneAssetPath), Is.True);
            Assert.That(GetSceneComponents<LightVolumeManager>(), Is.Empty);

            EditorSceneManager.CloseScene(_scene, true);
            _scene = EditorSceneManager.OpenScene(_sceneAssetPath, OpenSceneMode.Single);
            for (int i = 0; i < 30 && GetSceneComponents<LightVolumeManager>().Count == 0; i++) yield return null;

            GameObject instanceRoot = FindPrefabInstanceRoot();
            LightVolumeManager manager = GetSingleSceneComponent<LightVolumeManager>();
            AssertRegistered(
                manager,
                instanceRoot.GetComponentInChildren<LightVolumeInstance>(true),
                instanceRoot.GetComponentInChildren<PointLightVolumeInstance>(true));
        }

        [UnityTest]
        public IEnumerator OpeningRegisteredSceneDoesNotMarkItDirty() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject prefabAsset = CreatePrefab(includeLegacyHelpers: false, includeUnifiedComponents: true);
            GameObject instanceRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, _scene);
            QueueAndFlush(instanceRoot);

            // Drain setup object-change events before the save that establishes the clean baseline.
            yield return null;
            yield return null;
            LightVolumeEditorUpdater.FlushPendingSceneChanges();
            LightVolumeManager manager = GetSingleSceneComponent<LightVolumeManager>();
            AssertRegistered(
                manager,
                instanceRoot.GetComponentInChildren<LightVolumeInstance>(true),
                instanceRoot.GetComponentInChildren<PointLightVolumeInstance>(true));

            _sceneAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesSceneSetupCleanReopenTest.unity");
            Assert.That(EditorSceneManager.SaveScene(_scene, _sceneAssetPath), Is.True);
            Assert.That(_scene.isDirty, Is.False);
            EditorSceneManager.CloseScene(_scene, true);
            _scene = EditorSceneManager.OpenScene(_sceneAssetPath, OpenSceneMode.Single);
            Assert.That(_scene.isDirty, Is.False);

            // sceneOpened queues migration, onboarding, and any resulting object-change batch on
            // successive editor turns. Dirty is sticky, so the final assertion observes every phase.
            for (int i = 0; i < 4; i++) {
                yield return null;
                LightVolumeEditorUpdater.FlushPendingSceneChanges();
            }

            instanceRoot = FindPrefabInstanceRoot();
            manager = GetSingleSceneComponent<LightVolumeManager>();
            AssertRegistered(
                manager,
                instanceRoot.GetComponentInChildren<LightVolumeInstance>(true),
                instanceRoot.GetComponentInChildren<PointLightVolumeInstance>(true));
            Assert.That(_scene.isDirty, Is.False, "Opening an already reconciled scene must not serialize setup state again.");
        }

        [Test]
        public void LegacyPrefabHelpersMigrateAsInstanceOverridesWithoutChangingAsset() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject prefabAsset = CreatePrefab(includeLegacyHelpers: true, includeUnifiedComponents: true);
            Hash128 assetHash = AssetDatabase.GetAssetDependencyHash(_prefabAssetPath);

            GameObject instanceRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, _scene);
            LightVolume legacyVolume = instanceRoot.GetComponentInChildren<LightVolume>(true);
            PointLightVolume legacyPoint = instanceRoot.GetComponentInChildren<PointLightVolume>(true);
            legacyVolume.Dynamic = true;
            legacyVolume.Intensity = 7.25f;
            legacyVolume.AdaptiveResolution = false;
            legacyVolume.Resolution = new Vector3Int(11, 12, 13);
            legacyVolume.enabled = false;
            legacyPoint.Dynamic = true;
            legacyPoint.Type = PointLightVolume.LightType.SpotLight;
            legacyPoint.Projection = PointLightVolume.LightProjection.Custom;
            legacyPoint.Intensity = 987f;
            legacyPoint.Range = 23f;
            legacyPoint.Angle = 74f;
            legacyPoint.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(legacyVolume);
            PrefabUtility.RecordPrefabInstancePropertyModifications(legacyPoint);

            QueueAndFlush(instanceRoot);

            LightVolumeManager manager = GetSingleSceneComponent<LightVolumeManager>();
            LightVolumeInstance volume = instanceRoot.GetComponentInChildren<LightVolumeInstance>(true);
            PointLightVolumeInstance pointLight = instanceRoot.GetComponentInChildren<PointLightVolumeInstance>(true);
            Assert.That(instanceRoot.GetComponentInChildren<LightVolume>(true), Is.Null);
            Assert.That(instanceRoot.GetComponentInChildren<PointLightVolume>(true), Is.Null);
            Assert.That(volume.IsDynamic, Is.True);
            Assert.That(volume.Intensity, Is.EqualTo(7.25f));
            Assert.That(volume.AdaptiveResolution, Is.False);
            Assert.That(volume.Resolution, Is.EqualTo(new Vector3Int(11, 12, 13)));
            Assert.That(volume.enabled, Is.False);
            Assert.That(pointLight.IsDynamic, Is.True);
            Assert.That(pointLight.LightType, Is.EqualTo(1));
            Assert.That(pointLight.Projection, Is.EqualTo(2));
            Assert.That(pointLight.Intensity, Is.EqualTo(987f));
            Assert.That(pointLight.Range, Is.EqualTo(23f));
            Assert.That(pointLight.Angle, Is.EqualTo(74f * Mathf.Deg2Rad * 0.5f).Within(0.0001f));
            Assert.That(pointLight.enabled, Is.False);
            AssertRegistered(manager, volume, pointLight);
            Assert.That(UdonSharpEditorUtility.GetBackingUdonBehaviour(volume), Is.Not.Null);
            Assert.That(UdonSharpEditorUtility.GetBackingUdonBehaviour(pointLight), Is.Not.Null);
            var removedComponents = PrefabUtility.GetRemovedComponents(instanceRoot);
            Assert.That(removedComponents, Has.Count.EqualTo(2));
            Assert.That(removedComponents.Exists(item => item.assetComponent is LightVolume), Is.True);
            Assert.That(removedComponents.Exists(item => item.assetComponent is PointLightVolume), Is.True);
            Assert.That(PrefabUtility.GetPrefabInstanceStatus(instanceRoot), Is.EqualTo(PrefabInstanceStatus.Connected));

            AssetDatabase.SaveAssets();
            Assert.That(AssetDatabase.GetAssetDependencyHash(_prefabAssetPath), Is.EqualTo(assetHash));
            GameObject unchangedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabAssetPath);
            Assert.That(unchangedAsset.GetComponentInChildren<LightVolume>(true).Intensity, Is.EqualTo(2f));
            Assert.That(unchangedAsset.GetComponentInChildren<PointLightVolume>(true).Intensity, Is.EqualTo(100f));
            Assert.That(unchangedAsset.GetComponentInChildren<LightVolumeInstance>(true).LightVolumeManager, Is.Null);
            Assert.That(unchangedAsset.GetComponentInChildren<PointLightVolumeInstance>(true).LightVolumeManager, Is.Null);

            SaveAndReopenScene();
            instanceRoot = FindPrefabInstanceRoot();
            manager = GetSingleSceneComponent<LightVolumeManager>();
            volume = instanceRoot.GetComponentInChildren<LightVolumeInstance>(true);
            pointLight = instanceRoot.GetComponentInChildren<PointLightVolumeInstance>(true);
            Assert.That(instanceRoot.GetComponentInChildren<LightVolume>(true), Is.Null);
            Assert.That(instanceRoot.GetComponentInChildren<PointLightVolume>(true), Is.Null);
            Assert.That(volume.Intensity, Is.EqualTo(7.25f));
            Assert.That(pointLight.Intensity, Is.EqualTo(987f));
            AssertRegistered(manager, volume, pointLight);
            AssertBackingManager(volume, manager);
            AssertBackingManager(pointLight, manager);
            removedComponents = PrefabUtility.GetRemovedComponents(instanceRoot);
            Assert.That(removedComponents, Has.Count.EqualTo(2));
            Assert.That(removedComponents.Exists(item => item.assetComponent is LightVolume), Is.True);
            Assert.That(removedComponents.Exists(item => item.assetComponent is PointLightVolume), Is.True);
        }

        [Test]
        public void LegacySetupPrefabMigratesManagerSettingsRegistriesAndWeightsAsOverrides() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject prefabAsset = CreateLegacySetupPrefab();
            Hash128 assetHash = AssetDatabase.GetAssetDependencyHash(_prefabAssetPath);
            GameObject instanceRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, _scene);

            QueueAndFlush(instanceRoot);

            LightVolumeManager manager = instanceRoot.GetComponent<LightVolumeManager>();
            LightVolumeInstance volume = instanceRoot.GetComponentInChildren<LightVolumeInstance>(true);
            PointLightVolumeInstance pointLight = instanceRoot.GetComponentInChildren<PointLightVolumeInstance>(true);
            Assert.That(instanceRoot.GetComponentInChildren<LightVolumeSetup>(true), Is.Null);
            Assert.That(instanceRoot.GetComponentInChildren<LightVolume>(true), Is.Null);
            Assert.That(instanceRoot.GetComponentInChildren<PointLightVolume>(true), Is.Null);
            Assert.That(manager.CustomTexturesWidth, Is.EqualTo(128));
            Assert.That(manager.CustomTexturesHeight, Is.EqualTo(128));
            Assert.That(manager.LightsBrightnessCutoff, Is.EqualTo(0.71f));
            Assert.That(manager.AutoUpdateTextures, Is.False);
            Assert.That(volume.Intensity, Is.EqualTo(6.25f));
            Assert.That(volume.RegistryWeight, Is.EqualTo(4.5f));
            Assert.That(pointLight.Intensity, Is.EqualTo(321f));
            Assert.That(pointLight.Range, Is.EqualTo(18f));
            AssertRegistered(manager, volume, pointLight);
            AssertBackingManager(volume, manager);
            AssertBackingManager(pointLight, manager);
            var removedComponents = PrefabUtility.GetRemovedComponents(instanceRoot);
            Assert.That(removedComponents, Has.Count.EqualTo(3));
            Assert.That(removedComponents.Exists(item => item.assetComponent is LightVolumeSetup), Is.True);
            Assert.That(removedComponents.Exists(item => item.assetComponent is LightVolume), Is.True);
            Assert.That(removedComponents.Exists(item => item.assetComponent is PointLightVolume), Is.True);

            AssetDatabase.SaveAssets();
            Assert.That(AssetDatabase.GetAssetDependencyHash(_prefabAssetPath), Is.EqualTo(assetHash));
            GameObject unchangedAsset = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabAssetPath);
            Assert.That(unchangedAsset.GetComponentInChildren<LightVolumeSetup>(true), Is.Not.Null);
            Assert.That(unchangedAsset.GetComponentInChildren<LightVolume>(true).Intensity, Is.EqualTo(6.25f));
            Assert.That(unchangedAsset.GetComponentInChildren<PointLightVolume>(true).Intensity, Is.EqualTo(321f));

            SaveAndReopenScene();
            instanceRoot = FindPrefabInstanceRoot();
            manager = instanceRoot.GetComponent<LightVolumeManager>();
            volume = instanceRoot.GetComponentInChildren<LightVolumeInstance>(true);
            pointLight = instanceRoot.GetComponentInChildren<PointLightVolumeInstance>(true);
            Assert.That(instanceRoot.GetComponentInChildren<LightVolumeSetup>(true), Is.Null);
            Assert.That(manager.LightsBrightnessCutoff, Is.EqualTo(0.71f));
            Assert.That(manager.AutoUpdateTextures, Is.False);
            Assert.That(volume.RegistryWeight, Is.EqualTo(4.5f));
            AssertRegistered(manager, volume, pointLight);
            AssertBackingManager(volume, manager);
            AssertBackingManager(pointLight, manager);
        }

        [Test]
        public void PrefabPreviewContentsAreNeverOnboardedOrMigrated() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreatePrefab(includeLegacyHelpers: true, includeUnifiedComponents: true);
            Hash128 assetHash = AssetDatabase.GetAssetDependencyHash(_prefabAssetPath);
            GameObject persistentAsset = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabAssetPath);
            Assert.That(LightVolumeSceneSetup.OnboardHierarchy(persistentAsset, out LightVolumeManager persistentManager), Is.False);
            Assert.That(persistentManager, Is.Null);

            GameObject contentsRoot = PrefabUtility.LoadPrefabContents(_prefabAssetPath);
            try {
                Scene previewScene = contentsRoot.scene;
                Assert.That(EditorSceneManager.IsPreviewScene(previewScene), Is.True);
                bool wasDirty = previewScene.isDirty;
                Assert.That(wasDirty, Is.False);

                QueueAndFlush(contentsRoot);
                int blocked = 0;
                int removed = LightVolumeMigration.MigrateScene(previewScene, ref blocked);

                Assert.That(removed, Is.Zero);
                Assert.That(blocked, Is.Zero);
                Assert.That(contentsRoot.GetComponentInChildren<LightVolume>(true), Is.Not.Null);
                Assert.That(contentsRoot.GetComponentInChildren<PointLightVolume>(true), Is.Not.Null);
                Assert.That(GetSceneComponents<LightVolumeManager>(previewScene), Is.Empty);
                Assert.That(contentsRoot.GetComponentInChildren<LightVolumeInstance>(true).LightVolumeManager, Is.Null);
                Assert.That(contentsRoot.GetComponentInChildren<PointLightVolumeInstance>(true).LightVolumeManager, Is.Null);
                Assert.That(previewScene.isDirty, Is.EqualTo(wasDirty));
            } finally {
                PrefabUtility.UnloadPrefabContents(contentsRoot);
            }

            AssetDatabase.SaveAssets();
            Assert.That(AssetDatabase.GetAssetDependencyHash(_prefabAssetPath), Is.EqualTo(assetHash));
        }

        [Test]
        public void IncompleteHelperOnlyPrefabIsLeftIntactToPreventDataLoss() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject prefabAsset = CreatePrefab(includeLegacyHelpers: true, includeUnifiedComponents: false);
            Hash128 assetHash = AssetDatabase.GetAssetDependencyHash(_prefabAssetPath);
            GameObject instanceRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, _scene);
            _sceneAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesSceneSetupSafetyTest.unity");
            Assert.That(EditorSceneManager.SaveScene(_scene, _sceneAssetPath), Is.True);
            Assert.That(_scene.isDirty, Is.False);

            LogAssert.Expect(LogType.Warning, new Regex("Left 2 legacy helper component\\(s\\).*unchanged"));
            QueueAndFlush(instanceRoot);

            Assert.That(GetSceneComponents<LightVolumeManager>(), Is.Empty);
            Assert.That(instanceRoot.GetComponentInChildren<LightVolume>(true), Is.Not.Null);
            Assert.That(instanceRoot.GetComponentInChildren<PointLightVolume>(true), Is.Not.Null);
            Assert.That(instanceRoot.GetComponentInChildren<LightVolumeInstance>(true), Is.Null);
            Assert.That(instanceRoot.GetComponentInChildren<PointLightVolumeInstance>(true), Is.Null);
            Assert.That(PrefabUtility.GetPrefabInstanceStatus(instanceRoot), Is.EqualTo(PrefabInstanceStatus.Connected));
            Assert.That(_scene.isDirty, Is.False);
            AssetDatabase.SaveAssets();
            Assert.That(AssetDatabase.GetAssetDependencyHash(_prefabAssetPath), Is.EqualTo(assetHash));
        }

        [Test]
        public void LooseLegacySceneHierarchyCreatesUnifiedComponentsBeforeRemovingHelpers() {
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject root = new GameObject("Loose Legacy Hierarchy");
            SceneManager.MoveGameObjectToScene(root, _scene);
            GameObject volumeObject = new GameObject("Legacy Volume");
            volumeObject.transform.SetParent(root.transform, false);
            LightVolume legacyVolume = volumeObject.AddComponent<LightVolume>();
            legacyVolume.AdaptiveResolution = false;
            legacyVolume.Intensity = 4.5f;
            legacyVolume.Resolution = new Vector3Int(6, 7, 8);
            GameObject pointObject = new GameObject("Legacy Point");
            pointObject.transform.SetParent(root.transform, false);
            PointLightVolume legacyPoint = pointObject.AddComponent<PointLightVolume>();
            legacyPoint.Intensity = 456f;
            legacyPoint.Range = 17f;

            QueueAndFlush(root);

            LightVolumeManager manager = GetSingleSceneComponent<LightVolumeManager>();
            LightVolumeInstance volume = volumeObject.GetComponent<LightVolumeInstance>();
            PointLightVolumeInstance pointLight = pointObject.GetComponent<PointLightVolumeInstance>();
            Assert.That(volumeObject.GetComponent<LightVolume>(), Is.Null);
            Assert.That(pointObject.GetComponent<PointLightVolume>(), Is.Null);
            Assert.That(volume, Is.Not.Null);
            Assert.That(pointLight, Is.Not.Null);
            Assert.That(volume.Intensity, Is.EqualTo(4.5f));
            Assert.That(volume.Resolution, Is.EqualTo(new Vector3Int(6, 7, 8)));
            Assert.That(pointLight.Intensity, Is.EqualTo(456f));
            Assert.That(pointLight.Range, Is.EqualTo(17f));
            AssertRegistered(manager, volume, pointLight);
            AssertBackingManager(volume, manager);
            AssertBackingManager(pointLight, manager);
        }

        private GameObject CreatePrefab(bool includeLegacyHelpers, bool includeUnifiedComponents, bool includeVolume = true,
            bool includePointLight = true, bool includeBakeryDependency = false) {
            _prefabAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesSceneSetupTest.prefab");
            GameObject root = new GameObject("Light Volumes Prefab");
            SceneManager.MoveGameObjectToScene(root, _scene);
            GameObject volumeObject = null;
            if (includeVolume) {
                volumeObject = new GameObject("Volume");
                volumeObject.transform.SetParent(root.transform, false);
            }
            GameObject pointObject = null;
            if (includePointLight) {
                pointObject = new GameObject("Point Light");
                pointObject.transform.SetParent(root.transform, false);
            }

            if (includeLegacyHelpers) {
                if (includeVolume) {
                    LightVolume legacyVolume = volumeObject.AddComponent<LightVolume>();
                    legacyVolume.Intensity = 2f;
                    legacyVolume.Resolution = new Vector3Int(4, 5, 6);
                }
                if (includePointLight) {
                    PointLightVolume legacyPoint = pointObject.AddComponent<PointLightVolume>();
                    legacyPoint.Intensity = 100f;
                    legacyPoint.Range = 10f;
                }
            }
            LightVolumeInstance unifiedVolume = null;
            if (includeUnifiedComponents) {
                if (includeVolume) unifiedVolume = UdonSharpUndo.AddComponent<LightVolumeInstance>(volumeObject);
                if (includePointLight) UdonSharpUndo.AddComponent<PointLightVolumeInstance>(pointObject);
            }
#if BAKERY_INCLUDED
            if (includeBakeryDependency) {
                Assert.That(unifiedVolume, Is.Not.Null);
                Type bakeryVolumeType = FindBakeryVolumeType();
                Assert.That(bakeryVolumeType, Is.Not.Null);
                GameObject helper = new GameObject($"Bakery Volume - {volumeObject.name}") { tag = "EditorOnly" };
                helper.transform.SetParent(volumeObject.transform, false);
                unifiedVolume.BakeryVolume = helper.AddComponent(bakeryVolumeType);
                LightVolumeManagerTools.CopyProxyToUdon(unifiedVolume);
            }
#endif

            PrefabUtility.SaveAsPrefabAsset(root, _prefabAssetPath);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<GameObject>(_prefabAssetPath);
        }

        private GameObject CreateLegacySetupPrefab() {
            _prefabAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesLegacySetupTest.prefab");
            GameObject root = new GameObject("Legacy Light Volumes Setup");
            SceneManager.MoveGameObjectToScene(root, _scene);
            LightVolumeManager manager = UdonSharpUndo.AddComponent<LightVolumeManager>(root);
            LightVolumeSetup setup = root.AddComponent<LightVolumeSetup>();
            setup.CookieResolution = LightVolumeSetup.TextureArrayResolution._128x128;
            setup.BrightnessCutoff = 0.71f;
            setup.AutoUpdateTextures = false;
            setup.LightVolumeManager = manager;

            GameObject volumeObject = new GameObject("Volume");
            volumeObject.transform.SetParent(root.transform, false);
            LightVolume legacyVolume = volumeObject.AddComponent<LightVolume>();
            legacyVolume.Intensity = 6.25f;
            LightVolumeInstance volume = UdonSharpUndo.AddComponent<LightVolumeInstance>(volumeObject);
            legacyVolume.LightVolumeSetup = setup;
            legacyVolume.LightVolumeInstance = volume;

            GameObject pointObject = new GameObject("Point Light");
            pointObject.transform.SetParent(root.transform, false);
            PointLightVolume legacyPoint = pointObject.AddComponent<PointLightVolume>();
            legacyPoint.Intensity = 321f;
            legacyPoint.Range = 18f;
            PointLightVolumeInstance pointLight = UdonSharpUndo.AddComponent<PointLightVolumeInstance>(pointObject);
            legacyPoint.LightVolumeSetup = setup;
            legacyPoint.PointLightVolumeInstance = pointLight;

            setup.LightVolumes.Add(legacyVolume);
            setup.LightVolumesWeights.Add(4.5f);
            setup.PointLightVolumes.Add(legacyPoint);
            manager.LightVolumeInstances = new[] { volume };
            manager.PointLightVolumeInstances = new[] { pointLight };
            volume.LightVolumeManager = manager;
            pointLight.LightVolumeManager = manager;
            LightVolumeManagerTools.CopyProxyToUdon(volume);
            LightVolumeManagerTools.CopyProxyToUdon(pointLight);
            LightVolumeManagerTools.CopyProxyToUdon(manager);

            PrefabUtility.SaveAsPrefabAsset(root, _prefabAssetPath);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<GameObject>(_prefabAssetPath);
        }

#if BAKERY_INCLUDED
        private static Type FindBakeryVolumeType() {
            foreach (Type componentType in TypeCache.GetTypesDerivedFrom<Component>()) {
                if (componentType.Name == "BakeryVolume") return componentType;
            }
            return null;
        }
#endif

        private static void QueueAndFlush(GameObject root) {
            LightVolumeEditorUpdater.QueueHierarchyOnboarding(root);
            LightVolumeEditorUpdater.FlushPendingSceneChanges();
        }

        private void CaptureObjectChanges(ref ObjectChangeEventStream stream) {
            for (int i = 0; i < stream.length; i++) _capturedObjectChanges.Add(stream.GetEventType(i));
        }

        private void SaveAndReopenScene() {
            _sceneAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/VRCLightVolumesSceneSetupTest.unity");
            Assert.That(EditorSceneManager.SaveScene(_scene, _sceneAssetPath), Is.True);
            EditorSceneManager.CloseScene(_scene, true);
            _scene = EditorSceneManager.OpenScene(_sceneAssetPath, OpenSceneMode.Single);
        }

        private GameObject FindPrefabInstanceRoot() {
            GameObject[] roots = _scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) {
                if (PrefabUtility.GetPrefabInstanceStatus(roots[i]) == PrefabInstanceStatus.Connected) return roots[i];
            }
            Assert.Fail("Prefab instance root was not found.");
            return null;
        }

        private T GetSingleSceneComponent<T>() where T : Component {
            List<T> components = GetSceneComponents<T>();
            Assert.That(components, Has.Count.EqualTo(1));
            return components[0];
        }

        private List<T> GetSceneComponents<T>() where T : Component {
            return GetSceneComponents<T>(_scene);
        }

        private static List<T> GetSceneComponents<T>(Scene scene) where T : Component {
            List<T> result = new List<T>();
            List<T> buffer = new List<T>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) {
                buffer.Clear();
                roots[i].GetComponentsInChildren(true, buffer);
                result.AddRange(buffer);
            }
            return result;
        }

        private static void AssertRegistered(LightVolumeManager manager, LightVolumeInstance volume, PointLightVolumeInstance pointLight) {
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] { volume }));
            Assert.That(manager.PointLightVolumeInstances, Is.EqualTo(new[] { pointLight }));
            Assert.That(volume.LightVolumeManager, Is.SameAs(manager));
            Assert.That(pointLight.LightVolumeManager, Is.SameAs(manager));
            Assert.That(volume.RegistryOrder, Is.Zero);
            Assert.That(pointLight.RegistryOrder, Is.Zero);
        }

        private static void AssertBackingManager(Component component, LightVolumeManager manager) {
            UdonBehaviour componentBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(component as UdonSharp.UdonSharpBehaviour);
            UdonBehaviour managerBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            Assert.That(componentBacking, Is.Not.Null);
            Assert.That(managerBacking, Is.Not.Null);
            Assert.That(componentBacking.publicVariables.TryGetVariableValue("LightVolumeManager", out object serializedManager), Is.True);
            Assert.That(serializedManager, Is.SameAs(managerBacking));
        }
    }
}

#pragma warning restore CS0618
