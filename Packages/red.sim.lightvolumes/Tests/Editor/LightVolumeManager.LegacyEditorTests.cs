using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRCLightVolumes.Editor;

namespace VRCLightVolumes.Tests {
    // Compatibility tests live beside the removable compatibility implementation.
    // Delete this file with the two production *.Legacy*.cs files when the migration window closes.
    [Category("Editor")]
    public class LightVolumeManagerLegacyEditorTests {
        [Test]
        public void LegacyEditorApiKeepsItsPreviousSignatures() {
            Type manager = typeof(LightVolumeManager);
            Type api = typeof(LightVolumeManagerTools);
            Type vectorArray = typeof(Vector3[]);
            Type processor = typeof(LightVolumeManager.PostProcessor);

            Assert.That(manager.GetEvent("AtlasPostProcessorsChanged"), Is.Not.Null);
            Assert.That(manager.GetProperty("IsBakeryMode"), Is.Not.Null);
            Assert.That(manager.GetProperty("AtlasPostProcessors"), Is.Not.Null);
            Assert.That(manager.GetMethod("RegisterPostProcessorCRT", new[] { typeof(CustomRenderTexture) }), Is.Not.Null);
            Assert.That(manager.GetMethod("RegisterPostProcessor", new[] { processor }), Is.Not.Null);
            Assert.That(manager.GetMethod("UnregisterPostProcessorCRT", new[] { typeof(CustomRenderTexture) }), Is.Not.Null);
            Assert.That(manager.GetMethod("UnregisterPostProcessor", new[] { typeof(RenderTexture) }), Is.Not.Null);
            Assert.That(manager.GetMethod("UnregisterPostProcessor", new[] { processor }), Is.Not.Null);
            Assert.That(manager.GetMethod("RefreshAtlasPostProcessors", Type.EmptyTypes), Is.Not.Null);

            Assert.That(api.GetMethod("GenerateAtlas", new[] { manager }), Is.Not.Null);
            Assert.That(api.GetMethod("GetCustomProbesCount", new[] { manager }), Is.Not.Null);
            Assert.That(api.GetMethod("GetCustomProbes", new[] { manager, typeof(int) }), Is.Not.Null);
            Assert.That(api.GetMethod("SetCustomProbesBaked", new[] { manager, typeof(int), vectorArray, vectorArray, vectorArray, vectorArray }), Is.Not.Null);
            Assert.That(api.GetMethod("SetCustomProbesBaked", new[] { manager, typeof(int), vectorArray, vectorArray, vectorArray, vectorArray, typeof(bool) }), Is.Not.Null);
            Assert.That(api.GetMethod("SetCustomProbesBaked", new[] { manager, typeof(int), vectorArray, vectorArray, vectorArray, vectorArray, typeof(float[]) }), Is.Not.Null);
            Assert.That(api.GetMethod("SetCustomProbesBaked", new[] { manager, typeof(int), vectorArray, vectorArray, vectorArray, vectorArray, typeof(float[]), typeof(bool) }), Is.Not.Null);

            MethodInfo bakeShadowMaps = api.GetMethod("BakeShadowMaps", new[] { manager });
            Assert.That(bakeShadowMaps, Is.Not.Null);
            Assert.That(bakeShadowMaps.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(api.GetMethod("BakeShadowMaps", new[] { manager, typeof(bool) }), Is.Null);
        }

        [Test]
        public void LegacyAndEditorFacadePostProcessorApisShareManagerState() {
            GameObject gameObject = new GameObject("Legacy Editor API Manager");
            RenderTexture legacyTarget = new RenderTexture(2, 2, 0);
            RenderTexture facadeTarget = new RenderTexture(2, 2, 0);
            Material legacyMaterial = null;
            Material facadeMaterial = null;
            try {
                Shader shader = Shader.Find("Hidden/InternalErrorShader");
                Assert.That(shader, Is.Not.Null);
                legacyMaterial = new Material(shader);
                facadeMaterial = new Material(shader);
                LightVolumeManager manager = gameObject.AddComponent<LightVolumeManager>();
                Action legacyUpdate = () => { };
                Action<Texture> facadeUpdate = _ => { };

                manager.AtlasPostProcessors = new[] {
                    new LightVolumeManager.PostProcessor {
                        RT = legacyTarget,
                        Mat = legacyMaterial,
                        TextureName = "_LegacyInput",
                        Update = legacyUpdate
                    }
                };

                AtlasPostProcessor[] facadeView = manager.Editor.GetPostProcessors();
                Assert.That(facadeView, Has.Length.EqualTo(1));
                Assert.That(facadeView[0].Target, Is.SameAs(legacyTarget));
                Assert.That(facadeView[0].Material, Is.SameAs(legacyMaterial));
                Assert.That(facadeView[0].InputTextureProperty, Is.EqualTo("_LegacyInput"));
                Assert.That(facadeView[0].Update, Is.SameAs(legacyUpdate));

                AtlasPostProcessor facadeProcessor = new AtlasPostProcessor(facadeTarget, facadeMaterial, "_FacadeInput") {
                    UpdateWithInput = facadeUpdate
                };
                manager.Editor.RegisterPostProcessor(facadeProcessor);

                LightVolumeManager.PostProcessor[] legacyView = manager.AtlasPostProcessors;
                Assert.That(legacyView, Has.Length.EqualTo(2));
                Assert.That(legacyView[1].RT, Is.SameAs(facadeTarget));
                Assert.That(legacyView[1].Mat, Is.SameAs(facadeMaterial));
                Assert.That(legacyView[1].TextureName, Is.EqualTo("_FacadeInput"));
                Assert.That(legacyView[1].UpdateWithInput, Is.SameAs(facadeUpdate));

                manager.UnregisterPostProcessor(legacyTarget);
                Assert.That(manager.Editor.GetPostProcessors(), Has.Length.EqualTo(1));
                Assert.That(manager.Editor.ContainsPostProcessor(facadeProcessor), Is.True);

                manager.Editor.UnregisterPostProcessor(facadeTarget);
                Assert.That(manager.AtlasPostProcessors, Is.Empty);
                Assert.That(manager.AtlasPostProcessorTargets, Is.Empty);
                Assert.That(manager.AtlasPostProcessorMaterials, Is.Empty);
                Assert.That(manager.AtlasPostProcessorTextureNames, Is.Empty);
            } finally {
                legacyTarget.Release();
                facadeTarget.Release();
                UnityEngine.Object.DestroyImmediate(legacyTarget);
                UnityEngine.Object.DestroyImmediate(facadeTarget);
                UnityEngine.Object.DestroyImmediate(legacyMaterial);
                UnityEngine.Object.DestroyImmediate(facadeMaterial);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void LegacyPostProcessorArrayKeepsItsLiveMutationWorkflow() {
            GameObject gameObject = new GameObject("Legacy Live Post Processor Manager");
            RenderTexture target = new RenderTexture(2, 2, 0);
            Material material = null;
            try {
                Shader shader = Shader.Find("Hidden/InternalErrorShader");
                Assert.That(shader, Is.Not.Null);
                material = new Material(shader);
                LightVolumeManager manager = gameObject.AddComponent<LightVolumeManager>();
                LightVolumeManager.PostProcessor[] processors = {
                    new LightVolumeManager.PostProcessor {
                        RT = target,
                        Mat = material,
                        TextureName = "_Initial"
                    }
                };
                manager.AtlasPostProcessors = processors;

                Assert.That(manager.AtlasPostProcessors, Is.SameAs(processors));
                processors[0].TextureName = "_ChangedInPlace";
                manager.RefreshAtlasPostProcessors();

                AtlasPostProcessor[] supportedView = manager.Editor.GetPostProcessors();
                Assert.That(supportedView, Has.Length.EqualTo(1));
                Assert.That(supportedView[0].InputTextureProperty, Is.EqualTo("_ChangedInPlace"));
            } finally {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
    }
}
