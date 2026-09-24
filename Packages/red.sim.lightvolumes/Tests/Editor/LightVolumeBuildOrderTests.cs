using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UdonSharpEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRCLightVolumes.Tests {
    [Category("Editor")]
    public class LightVolumeBuildOrderTests {
        // Run the real SDK proxy-stripping step in callback order. If priorities tie,
        // test the SDK-first order that Unity is also allowed to choose.
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ShadowDependenciesSurviveUdonSharpBuildProcessing(bool cubemap, bool bakeInGame) {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            Type preprocessor = typeof(LightVolumePreprocessor);
            Type sdk = typeof(UdonSharpEditorUtility).Assembly.GetType("UdonSharpEditor.UdonSharpEditorManager");
            MethodInfo prepare = preprocessor.GetMethod("PrepareRuntimeDependencies", flags);
            MethodInfo findManager = preprocessor.GetMethod("FindPrimaryManager", flags);
            MethodInfo ownCallback = preprocessor.GetMethod("OnPostProcessScene", flags);
            MethodInfo sdkCallback = sdk?.GetMethod("OnSceneBuild", flags);
            MethodInfo strip = sdk?.GetMethod("OnSceneBuildInternal", flags);
            Assert.That(prepare, Is.Not.Null);
            Assert.That(findManager, Is.Not.Null);
            Assert.That(ownCallback, Is.Not.Null);
            Assert.That(sdkCallback, Is.Not.Null);
            Assert.That(strip, Is.Not.Null);

            Scene scene = EditorSceneManager.NewPreviewScene();
            var materials = new List<Material>();
            Texture source = null;
            try {
                GameObject root = new GameObject("Shadow Build Order Test");
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                GameObject managerObject = new GameObject("Manager");
                managerObject.transform.SetParent(root.transform);
                GameObject pointObject = new GameObject("Light");
                pointObject.transform.SetParent(root.transform);
                LightVolumeManager manager = managerObject.AddUdonSharpComponent<LightVolumeManager>();
                PointLightVolumeInstance point = pointObject.AddUdonSharpComponent<PointLightVolumeInstance>();
                source = cubemap
                    ? (Texture)new Cubemap(2, TextureFormat.RGBAHalf, false)
                    : new Texture2D(2, 2, TextureFormat.RGBAHalf, false, true);
                point.LightVolumeManager = manager;
                point.LightType = cubemap ? 0 : 1;
                point.Shadows = true;
                point.BakeInGame = bakeInGame;
                point.ShadowMap = source;
                point.ShadowMapTexture = source;
                point.ShadowMapTextureIsCubemap = cubemap;
                point.ShadowMapUsesCubemap = cubemap;
                manager.LightVolumeInstances = Array.Empty<LightVolumeInstance>();
                manager.PointLightVolumeInstances = new[] { point };
                UdonSharpEditorUtility.CopyProxyToUdon(point);
                UdonSharpEditorUtility.CopyProxyToUdon(manager);
                var managerBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
                var pointBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(point);
                GameObject[] roots = { root };

                Action prepareScene = () => {
                    prepare.Invoke(null, new object[] { roots, false, findManager.Invoke(null, new object[] { roots }) });
                    if (manager == null) return;
                    materials.AddRange(new[] {
                        manager.CubemapFaceMaterial, manager.RuntimeShadowDepthEncodeMaterial,
                        manager.RuntimeShadowBlurMaterial, manager.ClusteringMaterial, manager.ShadowCullingMaterial
                    });
                };
                bool prepareFirst = GetCallbackOrder(ownCallback) < GetCallbackOrder(sdkCallback);
                if (prepareFirst) prepareScene();
                strip.Invoke(null, new object[] { true, roots });
                if (!prepareFirst) prepareScene();

                Assert.That(managerObject.GetComponent<LightVolumeManager>(), Is.Null);
                Assert.That(pointObject.GetComponent<PointLightVolumeInstance>(), Is.Null);
                Assert.That(managerBacking.publicVariables.TryGetVariableValue("CubemapFaceMaterial", out object cubeMaterial), Is.True);
                Assert.That(cubeMaterial as Material, Is.Not.Null,
                    "Prepare Light Volumes before UdonSharp removes the Manager proxy; otherwise cubemap extraction has no material.");
                Assert.That(((Material)cubeMaterial).hideFlags, Is.EqualTo(HideFlags.None));
                Assert.That(pointBacking.publicVariables.TryGetVariableValue("ShadowMapTexture", out object shadowSource), Is.True);
                if (bakeInGame) {
                    Assert.That(shadowSource, Is.Null, "Bake In Game must discard the editor-baked source.");
                    Assert.That(pointBacking.publicVariables.TryGetVariableValue("RuntimeShadowCamera", out object camera), Is.True);
                    Assert.That(camera as Camera, Is.Not.Null);
                    Assert.That(pointBacking.publicVariables.TryGetVariableValue("RuntimeShadowDepthEncodeMaterial", out object depth), Is.True);
                    Assert.That(depth as Material, Is.Not.Null);
                    Assert.That(pointBacking.publicVariables.TryGetVariableValue("RuntimeShadowBlurMaterial", out object blur), Is.True);
                    Assert.That(blur as Material, Is.Not.Null);
                } else {
                    Assert.That(shadowSource, Is.SameAs(source), "An editor-baked shadow source must survive SDK processing.");
                }
            } finally {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (Material material in materials) {
                    if (material != null) UnityEngine.Object.DestroyImmediate(material);
                }
                if (source != null) UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static int GetCallbackOrder(MethodInfo callback) {
            foreach (CustomAttributeData attribute in callback.GetCustomAttributesData()) {
                if (attribute.AttributeType != typeof(PostProcessSceneAttribute)) continue;
                return attribute.ConstructorArguments.Count == 0 ? 0 : (int)attribute.ConstructorArguments[0].Value;
            }
            throw new InvalidOperationException(callback.Name + " is no longer a scene build callback.");
        }
    }
}
