using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using VRCLightVolumes.Editor;
using ManagerEditorHandle = VRCLightVolumes.Editor.LightVolumeManagerEditorContext;

namespace VRCLightVolumes.Tests {
    [Category("Udon")]
    public class LightVolumeUdonEditorTests {
        private const float Epsilon = 0.0001f;
        private const int PointLightUploadPosition = 1;
        private const int PointLightUploadColor = 2;
        private const int PointLightUploadExtraData = 4;
        private const int PointLightUploadDirection = 8;
        private const int PointLightUploadCustomId = 16;
        private const int PointLightUploadShadowRotation = 64;
        private const string CustomRenderTextureInfoProperty = "_CustomRenderTextureInfo";
        private const string LightVolumesIncludePath = "Shaders/LightVolumes.cginc";
        private const string RuntimeShadowBlurShaderPath = "Shaders/Internal/PointLightShadowRuntimeBlur.shader";
        private const BindingFlags PublicInstanceDeclared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;

        private static readonly int _lightVolumeInvLocalEdgeSmoothID = Shader.PropertyToID("_UdonLightVolumeInvLocalEdgeSmooth");
        private static readonly int _lightVolumeColorID = Shader.PropertyToID("_UdonLightVolumeColor");
        private static readonly int _lightVolumeCountID = Shader.PropertyToID("_UdonLightVolumeCount");
        private static readonly int _lightVolumeAdditiveCountID = Shader.PropertyToID("_UdonLightVolumeAdditiveCount");
        private static readonly int _lightVolumeAdditiveMaxOverdrawID = Shader.PropertyToID("_UdonLightVolumeAdditiveMaxOverdraw");
        private static readonly int _lightVolumeEnabledID = Shader.PropertyToID("_UdonLightVolumeEnabled");
        private static readonly int _lightVolumeProbesBlendID = Shader.PropertyToID("_UdonLightVolumeProbesBlend");
        private static readonly int _lightVolumeSharpBoundsID = Shader.PropertyToID("_UdonLightVolumeSharpBounds");
        private static readonly int _lightVolumeRotationID = Shader.PropertyToID("_UdonLightVolumeRotation");
        private static readonly int _lightVolumeInvWorldMatrixID = Shader.PropertyToID("_UdonLightVolumeInvWorldMatrix");
        private static readonly int _lightVolumeUvwScaleID = Shader.PropertyToID("_UdonLightVolumeUvwScale");
        private static readonly int _lightVolumeUvwID = Shader.PropertyToID("_UdonLightVolumeUvw");
        private static readonly int _lightVolumeOcclusionCountID = Shader.PropertyToID("_UdonLightVolumeOcclusionCount");
        private static readonly int _pointLightPositionID = Shader.PropertyToID("_UdonPointLightVolumePosition");
        private static readonly int _pointLightColorID = Shader.PropertyToID("_UdonPointLightVolumeColor");
        private static readonly int _pointLightDirectionID = Shader.PropertyToID("_UdonPointLightVolumeDirection");
        private static readonly int _pointLightExtraDataID = Shader.PropertyToID("_UdonPointLightVolumeExtraData");
        private static readonly int _pointLightCustomIdID = Shader.PropertyToID("_UdonPointLightVolumeCustomID");
        private static readonly int _pointLightCountID = Shader.PropertyToID("_UdonPointLightVolumeCount");
        private static readonly int _pointLightCubeCountID = Shader.PropertyToID("_UdonPointLightVolumeCubeCount");
        private static readonly int _pointLightTextureID = Shader.PropertyToID("_UdonPointLightVolumeTexture");
        private static readonly int _pointLightTextureTexelCountID = Shader.PropertyToID("_UdonPointLightVolumeTextureTexelCount");
        private static readonly int _pointLightShadowReprojectionDataID = Shader.PropertyToID("_UdonPointLightVolumeShadowReprojectionData");
        private static readonly int _pointLightShadowRotationDataID = Shader.PropertyToID("_UdonPointLightVolumeShadowRotationData");
        private static readonly int _pointLightShadowCubeCountID = Shader.PropertyToID("_UdonPointLightVolumeShadowCubeCount");
        private static readonly int _pointLightShadowCountID = Shader.PropertyToID("_UdonPointLightVolumeShadowCount");
        private static readonly int _pointLightShadowTextureID = Shader.PropertyToID("_UdonPointLightVolumeShadowTexture");
        private static readonly int _pointLightShadowReceiverParamsID = Shader.PropertyToID("_UdonPointLightVolumeShadowReceiverParams");
        private static readonly int _lightBrightnessCutoffID = Shader.PropertyToID("_UdonLightBrightnessCutoff");
        private static readonly int _forceSceneLightingID = Shader.PropertyToID("_UdonForceSceneLighting");
        private static readonly BindingFlags _lifecycleMethodFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo _customTexturesDepthField = typeof(LightVolumeManager).GetField("_customTextureArrayDepth", _lifecycleMethodFlags);
        private static readonly FieldInfo _shadowTexturesDepthField = typeof(LightVolumeManager).GetField("_shadowTextureArrayDepth", _lifecycleMethodFlags);
        private static readonly FieldInfo _customCubemapTextureCountField = typeof(LightVolumeManager).GetField("_customCubemapTextureCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _customSingleTextureCountField = typeof(LightVolumeManager).GetField("_customSingleTextureCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _customSingleMaterialCountField = typeof(LightVolumeManager).GetField("_customSingleMaterialCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _customSingleMaterialAutoUpdatesField = typeof(LightVolumeManager).GetField("_customSingleMaterialAutoUpdates", _lifecycleMethodFlags);
        private static readonly FieldInfo _shadowCubemapTextureCountField = typeof(LightVolumeManager).GetField("_shadowCubemapTextureCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _shadowSingleTextureCountField = typeof(LightVolumeManager).GetField("_shadowSingleTextureCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _pointLightCustomIDsField = typeof(LightVolumeManager).GetField("_pointLightCustomIDs", _lifecycleMethodFlags);
        private static readonly FieldInfo _pointLightShadowIDsField = typeof(LightVolumeManager).GetField("_pointLightShadowIDs", _lifecycleMethodFlags);
        private static readonly FieldInfo _pointLightAreaCookieAverageColorsField = typeof(LightVolumeManager).GetField("_pointLightAreaCookieAverageColors", _lifecycleMethodFlags);
        private static readonly FieldInfo _lightVolumeArraysDirtyField = typeof(LightVolumeManager).GetField("_lightVolumeArraysDirty", _lifecycleMethodFlags);
        private static readonly FieldInfo _lightVolumeInvWorldMatricesField = typeof(LightVolumeManager).GetField("_invWorldMatrix", _lifecycleMethodFlags);
        private static readonly FieldInfo _lightVolumeInvLocalEdgeSmoothField = typeof(LightVolumeManager).GetField("_invLocalEdgeSmooth", _lifecycleMethodFlags);
        private static readonly FieldInfo _lightVolumeColorsField = typeof(LightVolumeManager).GetField("_colors", _lifecycleMethodFlags);
        private static readonly FieldInfo _lightVolumeBoundsUvwScaleField = typeof(LightVolumeManager).GetField("_boundsUvwScale", _lifecycleMethodFlags);
        private static readonly FieldInfo _lightVolumeBoundsUvwField = typeof(LightVolumeManager).GetField("_boundsUvw", _lifecycleMethodFlags);
        private static readonly FieldInfo _lightVolumeRelativeRotationField = typeof(LightVolumeManager).GetField("_relativeRotation", _lifecycleMethodFlags);
        private static readonly FieldInfo _selectedLightVolumeIDsField = typeof(LightVolumeManager).GetField("_selectedLightVolumeIDs", _lifecycleMethodFlags);
        private static readonly FieldInfo _selectionLightVolumeWeightsField = typeof(LightVolumeManager).GetField("_selectionLightVolumeWeights", _lifecycleMethodFlags);
        private static readonly FieldInfo _selectionLightVolumeOrdersField = typeof(LightVolumeManager).GetField("_selectionLightVolumeOrders", _lifecycleMethodFlags);
        private static readonly MethodInfo _selectLightVolumesByWeightMethod = typeof(LightVolumeManager).GetMethod("SelectLightVolumesByWeight", _lifecycleMethodFlags);
        private static readonly FieldInfo _clusterGeometryUploadPendingField = typeof(LightVolumeManager).GetField("_clusterGeometryUploadPending", _lifecycleMethodFlags);
        private static readonly FieldInfo _clusterMaskDirtyField = typeof(LightVolumeManager).GetField("_clusterMaskDirty", _lifecycleMethodFlags);
        private static readonly FieldInfo _clusteringLightsDirtyField = typeof(LightVolumeManager).GetField("_clusteringLightsDirty", _lifecycleMethodFlags);
        private static readonly FieldInfo _pointLightArrayUploadMaskField = typeof(LightVolumeManager).GetField("_pointLightArrayUploadMask", _lifecycleMethodFlags);
        private static readonly FieldInfo _enabledPointIDsField = typeof(LightVolumeManager).GetField("_enabledPointIDs", _lifecycleMethodFlags);
        private static readonly FieldInfo _pointLightRegistryToShaderIndexField = typeof(LightVolumeManager).GetField("_pointLightRegistryToShaderIndex", _lifecycleMethodFlags);
        private static readonly FieldInfo _dirtyPointLightCountField = typeof(LightVolumeManager).GetField("_dirtyPointLightCount", _lifecycleMethodFlags);
        private static readonly FieldInfo _dirtyPointLightUpdateFlagsField = typeof(LightVolumeManager).GetField("_dirtyPointLightUpdateFlags", _lifecycleMethodFlags);
        private static readonly FieldInfo _volumeDataUpdateRequestedField = typeof(LightVolumeManager).GetField("_volumeDataUpdateRequested", _lifecycleMethodFlags);
        private static readonly FieldInfo _isUpdatingVolumesField = typeof(LightVolumeManager).GetField("_isUpdatingVolumes", _lifecycleMethodFlags);
        private static readonly FieldInfo _isUpdateProcessRunningField = typeof(LightVolumeManager).GetField("_isUpdateProcessRunning", _lifecycleMethodFlags);
        private static readonly FieldInfo _dummyRTField = typeof(LightVolumeManager).GetField("_dummyRT", _lifecycleMethodFlags);
        private static readonly MethodInfo _uploadAreaCookieAverageColorMethod = typeof(LightVolumeManager).GetMethod("UploadAreaCookieAverageColor", _lifecycleMethodFlags);
        private static readonly MethodInfo _updateAutoUpdatedVolumeChangesMethod = typeof(LightVolumeManager).GetMethod("UpdateAutoUpdatedVolumeChanges", _lifecycleMethodFlags);
        private static readonly MethodInfo _updateDynamicVolumeTransformsMethod = typeof(LightVolumeManager).GetMethod("UpdateDynamicVolumeTransforms", _lifecycleMethodFlags);
        private static readonly MethodInfo _uploadAutoUpdatedVolumeChangesMethod = typeof(LightVolumeManager).GetMethod("UploadAutoUpdatedVolumeChanges", _lifecycleMethodFlags);
        private static readonly MethodInfo _flushPendingPointLightChangesMethod = typeof(LightVolumeManager).GetMethod("FlushPendingPointLightChanges", _lifecycleMethodFlags);
        private static readonly MethodInfo _writePointLightShaderDataMethod = typeof(LightVolumeManager).GetMethod("WritePointLightShaderData", _lifecycleMethodFlags);
        private static readonly MethodInfo _writeClusteringLightMethod = typeof(LightVolumeManager).GetMethod("WriteClusteringLight", _lifecycleMethodFlags);
        private static readonly MethodInfo _findPointLightFinalIndexMethod = typeof(LightVolumeManager).GetMethod("FindPointLightFinalIndex", _lifecycleMethodFlags);
        private static readonly BindingFlags _staticMigrationMethodFlags = BindingFlags.Static | BindingFlags.NonPublic;

        private readonly List<UnityEngine.Object> _createdObjects = new List<UnityEngine.Object>();

        // Resets process-wide shader globals before every test case.
        [SetUp]
        public void SetUp() {
            ResetShaderGlobals();
        }

        // Public fields are serialized both by Unity and by the backing UdonBehaviour public-variable table.
        [Test]
        public void ManagerSerializedPublicFieldContractRemainsAvailable() {
            object[,] expectedFields = {
                { "LightVolumeAtlasBase", typeof(Texture3D) },
                { "LightVolumeAtlas", typeof(Texture) },
                { "CustomTexturesWidth", typeof(int) },
                { "CustomTexturesHeight", typeof(int) },
                { "LightsBrightnessCutoff", typeof(float) },
                { "ShadowTexturesWidth", typeof(int) },
                { "ShadowTexturesHeight", typeof(int) },
                { "ShadowTextureFormat", typeof(int) },
                { "ShadowBleedReduction", typeof(float) },
                { "ShadowMinVariance", typeof(float) },
                { "Clustering", typeof(bool) },
                { "FroxelDensity", typeof(float) },
                { "FroxelSlices", typeof(int) },
                { "FroxelCoarse", typeof(int) },
                { "ClusteringMinLights", typeof(int) },
                { "LightProbesBlending", typeof(bool) },
                { "SharpBounds", typeof(bool) },
                { "AutoUpdateVolumes", typeof(bool) },
                { "AutoUpdateTextures", typeof(bool) },
                { "AdditiveMaxOverdraw", typeof(int) },
                { "ForceSceneLighting", typeof(bool) },
                { "BakingMode", typeof(int) },
                { "VolumeBitmask", typeof(int) },
                { "ProbeBitmask", typeof(int) },
                { "Denoise", typeof(bool) },
                { "DilateInvalidProbes", typeof(bool) },
                { "DilationIterations", typeof(int) },
                { "DilationBackfaceBias", typeof(float) },
                { "FixLightProbesL1", typeof(bool) },
                { "DownscaleVolumes", typeof(int) },
                { "ShadowMinVarianceDesktop", typeof(float) },
                { "ShadowMinVarianceMobile", typeof(float) },
                { "AtlasPostProcessorTargets", typeof(RenderTexture[]) },
                { "AtlasPostProcessorMaterials", typeof(Material[]) },
                { "AtlasPostProcessorTextureNames", typeof(string[]) },
                { "LightVolumeInstances", typeof(LightVolumeInstance[]) },
                { "PointLightVolumeInstances", typeof(PointLightVolumeInstance[]) },
                { "CustomTextures", typeof(RenderTexture) },
                { "CubemapsCount", typeof(int) },
                { "ShadowTextures", typeof(RenderTexture) },
                { "ShadowCubemapsCount", typeof(int) },
                { "ShadowMapsCount", typeof(int) },
                { "CubemapFaceMaterial", typeof(Material) },
                { "RuntimeShadowCamera", typeof(Camera) },
                { "RuntimeShadowDepthEncodeMaterial", typeof(Material) },
                { "RuntimeShadowBlurMaterial", typeof(Material) },
                { "RuntimeShadowBlurQualityPreset", typeof(int) },
                { "RuntimeShadowBlurUniformKeyword", typeof(int) },
                { "RuntimeShadowBlurDirectKeyword", typeof(int) },
                { "RuntimeShadowBlurSphericalKeyword", typeof(int) },
                { "ClusteringMaterial", typeof(Material) },
                { "HasAutoCustomTextureUpdates", typeof(bool) },
                { "HasAutoShadowTextureUpdates", typeof(bool) }
            };

            Assert.That(expectedFields.GetLength(0), Is.EqualTo(53), "Update the contract deliberately when its baseline changes.");
            FieldInfo[] declaredFields = typeof(LightVolumeManager).GetFields(PublicInstanceDeclared);
            Array.Sort(declaredFields, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            Assert.That(declaredFields, Has.Length.EqualTo(expectedFields.GetLength(0)), "Unexpected public instance field changed the serialized/Udon ABI.");
            for (int i = 0; i < expectedFields.GetLength(0); i++) {
                string fieldName = (string)expectedFields[i, 0];
                Type expectedType = (Type)expectedFields[i, 1];
                FieldInfo field = typeof(LightVolumeManager).GetField(fieldName, PublicInstanceDeclared);
                Assert.That(field, Is.Not.Null, "Missing serialized public field " + fieldName);
                Assert.That(field.FieldType, Is.EqualTo(expectedType), fieldName + " changed its serialized type");
                Assert.That(declaredFields[i].Name, Is.EqualTo(fieldName), fieldName + " changed its serialized declaration order");
            }
        }

        // These members are the supported C# and cross-UdonBehaviour facade; additional overloads remain allowed.
        [Test]
        public void ManagerRequiredPublicApiSignaturesRemainAvailable() {
            AssertPublicProperty("EnabledCount", typeof(int));
            AssertPublicProperty("EnabledIDs", typeof(int[]));
            AssertPublicProperty("Editor", typeof(ManagerEditorHandle));
            AssertPublicMethod("RecalculatePointLightRange", typeof(void), typeof(PointLightVolumeInstance));
            AssertPublicMethod("NotifyLightVolumeChanged", typeof(void), typeof(LightVolumeInstance), typeof(bool));
            AssertPublicMethod("NotifyLightVolumeColorChanged", typeof(void), typeof(LightVolumeInstance));
            AssertPublicMethod("NotifyPointLightVolumeChanged", typeof(void), typeof(PointLightVolumeInstance), typeof(bool), typeof(bool), typeof(bool));
            AssertPublicMethod("NotifyPointLightColorRangeChanged", typeof(void), typeof(PointLightVolumeInstance));
            AssertPublicMethod("SetForceSceneLighting", typeof(void), typeof(bool));
            AssertPublicMethod("SetClustering", typeof(void), typeof(bool));
            AssertPublicMethod("SanitizeRegistries", typeof(bool));
            AssertPublicMethod("InitializeLightVolume", typeof(void), typeof(LightVolumeInstance));
            AssertPublicMethod("DeinitializeLightVolume", typeof(void), typeof(LightVolumeInstance));
            AssertPublicMethod("ReorderLightVolume", typeof(void), typeof(LightVolumeInstance));
            AssertPublicMethod("InitializePointLightVolume", typeof(void), typeof(PointLightVolumeInstance));
            AssertPublicMethod("DeinitializePointLightVolume", typeof(void), typeof(PointLightVolumeInstance), typeof(bool), typeof(bool));
            AssertPublicMethod("ReorderPointLightVolume", typeof(void), typeof(PointLightVolumeInstance));
            AssertPublicMethod("EnqueueBakeInGameLight", typeof(void), typeof(PointLightVolumeInstance));
            AssertPublicMethod("UpdateAutoCustomTextures", typeof(void));
            AssertPublicMethod("CompleteAreaCookieAverageReadback", typeof(void), typeof(PointLightVolumeInstance), typeof(bool), typeof(Color));
            AssertPublicMethod("UpdateAutoShadowTextures", typeof(void));
            AssertPublicMethod("PreparePointLightDirectShadowOutput", typeof(int), typeof(PointLightVolumeInstance));
            AssertPublicMethod("UpdatePointLightShadowTexture", typeof(bool), typeof(PointLightVolumeInstance));
            AssertPublicMethod("GetPointLightCustomID", typeof(int), typeof(PointLightVolumeInstance));
            AssertPublicMethod("RequestUpdateVolumes", typeof(void));
        }

        // Baking-only per-face and staging ABI must not return after the complete-shadow simplification.
        [Test]
        public void RemovedRuntimeShadowBakingLegacyContractDoesNotReturn() {
            Assert.That(typeof(PointLightVolumeInstance).GetField("RuntimeShadowFacesPerFrame", PublicInstanceDeclared), Is.Null);
            Assert.That(typeof(PointLightVolumeInstance).GetField("RuntimeShadowRetainTemporaries", PublicInstanceDeclared), Is.Null);
            Assert.That(typeof(PointLightShadowRuntimeBaker).GetField("RealtimeFacesPerFrame", PublicInstanceDeclared), Is.Null);
            Assert.That(typeof(LightVolumeManager).GetMethod("UpdatePointLightShadowTextureSlice", PublicInstanceDeclared), Is.Null);
            Assert.That(typeof(LightVolumeManager).GetMethod("UpdatePointLightShadowTextureRange", PublicInstanceDeclared), Is.Null);
        }

        // A default handle is intentionally usable by generic editor integrations before a Manager is assigned.
        [Test]
        public void DefaultManagerEditorContextIsSafe() {
            ManagerEditorHandle editor = default;

            Assert.That(editor.IsValid, Is.False);
            Assert.That(editor.IsBakeryMode, Is.False);
            Assert.That(editor.GetPostProcessors(), Is.Empty);
            Assert.That(editor.ContainsPostProcessor((RenderTexture)null), Is.False);
            Assert.That(editor.ContainsPostProcessor(default(AtlasPostProcessor)), Is.False);
            Assert.That(editor.GetCustomProbesCount(), Is.Zero);
            Assert.That(editor.GetCustomProbes(0), Is.Empty);
            Assert.DoesNotThrow(() => {
                editor.BakeShadowMaps();
                editor.RegisterPostProcessor(default(AtlasPostProcessor));
                editor.RegisterPostProcessor((CustomRenderTexture)null);
                editor.UnregisterPostProcessor((RenderTexture)null);
                editor.UnregisterPostProcessor(default(AtlasPostProcessor));
                editor.RefreshPostProcessors();
                editor.GenerateAtlas();
                editor.SetCustomProbesBaked(0, null, null, null, null);
                editor.SetCustomProbesBaked(0, null, null, null, null, false);
                editor.SetCustomProbesBaked(0, null, null, null, null, null);
                editor.SetCustomProbesBaked(0, null, null, null, null, null, false);
            });
        }

        // The handle may live beside the Udon type, but executable editor operations must remain downstream.
        [Test]
        public void ManagerEditorFacadeAssemblyTopologyRemainsOneWay() {
            System.Reflection.Assembly udonAssembly = typeof(LightVolumeManager).Assembly;
            System.Reflection.Assembly handleAssembly = typeof(ManagerEditorHandle).Assembly;
            System.Reflection.Assembly editorAssembly = typeof(LightVolumeManagerEditorExtensions).Assembly;

            Assert.That(udonAssembly.GetName().Name, Is.EqualTo("red.sim.LightVolumesUdon"));
            Assert.That(handleAssembly, Is.SameAs(udonAssembly), "The allocation-free handle belongs to the Udon-facing assembly.");
            Assert.That(editorAssembly.GetName().Name, Is.EqualTo("red.sim.LightVolumesEditor"));
            Assert.That(editorAssembly, Is.SameAs(typeof(LightVolumeManagerEditorBackend).Assembly));
            Assert.That(editorAssembly, Is.Not.SameAs(udonAssembly), "Editor extension implementations must not enter the Udon core.");

            AssemblyName[] references = udonAssembly.GetReferencedAssemblies();
            for (int i = 0; i < references.Length; i++) {
                string referenceName = references[i].Name;
                Assert.That(referenceName, Is.Not.EqualTo(editorAssembly.GetName().Name),
                    "The Udon core must not reference the VRCLV editor assembly.");
                Assert.That(referenceName, Does.Not.EndWith(".Editor"),
                    "The Udon core must not acquire an editor-only assembly reference: " + referenceName);
            }

            const string udonAsmdefPath = "Packages/red.sim.lightvolumes/UScripts/red.sim.LightVolumesUdon.asmdef";
            const string editorAsmdefPath = "Packages/red.sim.lightvolumes/Scripts/Editor/red.sim.LightVolumesEditor.asmdef";
            string editorAsmdefGuid = AssetDatabase.AssetPathToGUID(editorAsmdefPath);
            string udonAsmdef = File.ReadAllText(udonAsmdefPath);
            Assert.That(editorAsmdefGuid, Is.Not.Empty);
            Assert.That(udonAsmdef, Does.Not.Contain(editorAsmdefGuid));
            Assert.That(udonAsmdef, Does.Not.Contain("red.sim.LightVolumesEditor"));
        }

        // Unity lifecycle methods and string-dispatched Udon events must retain their exact names and shapes.
        [Test]
        public void ManagerUdonEventEntryPointsRemainAvailable() {
            AssertDeclaredParameterlessVoidMethod("Start");
            AssertDeclaredParameterlessVoidMethod("OnEnable");
            AssertDeclaredParameterlessVoidMethod("OnDisable");
            AssertPublicParameterlessVoidMethod("PostLateUpdate");
            AssertPublicParameterlessVoidMethod("_onVarChange_AutoUpdateTextures");
            AssertPublicParameterlessVoidMethod("_ApplyEditorSettings");
            AssertPublicParameterlessVoidMethod("_RequestAreaCookieAverageReadbacks");
            AssertPublicParameterlessVoidMethod("UpdateProcess");
            AssertPublicParameterlessVoidMethod("ReinitializeCustomTextures");
            AssertPublicParameterlessVoidMethod("ReinitializeShadowTextures");
            AssertPublicParameterlessVoidMethod("UpdateVolumes");
        }

        // The script and UdonSharp program-source GUIDs are embedded in existing scene YAML and backing behaviours.
        [Test]
        public void ManagerPackageAssetGuidsRemainStable() {
            Assert.That(AssetDatabase.AssetPathToGUID("Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.cs"),
                Is.EqualTo("a4c164fbf42cf794a8edc0fd006e1b60"));
            Assert.That(AssetDatabase.AssetPathToGUID("Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.asset"),
                Is.EqualTo("d722b6db295ca634790a0beebd593b48"));
        }

        // Companion partial sources must compile into the primary manager program rather than owning Udon programs.
        [Test]
        public void ManagerPartialSourcesDoNotCreateAdditionalProgramAssets() {
            string[] partialSources = {
                "Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Buffers.cs",
                "Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Clustering.cs",
                "Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Core.cs",
                "Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Editor.cs",
                "Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Registries.cs",
                "Packages/red.sim.lightvolumes/UScripts/LightVolumeManager.Textures.cs"
            };

            for (int i = 0; i < partialSources.Length; i++) {
                string sourcePath = partialSources[i];
                Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>(sourcePath), Is.Not.Null, "Missing partial source " + sourcePath);
                string programPath = sourcePath.Substring(0, sourcePath.Length - 3) + ".asset";
                Assert.That(AssetDatabase.LoadMainAssetAtPath(programPath), Is.Null, "Partial source must not own a UdonSharpProgramAsset: " + programPath);
            }
        }

        // Point Light companions are source organization only and must share the primary Udon program asset.
        [Test]
        public void PointLightPartialSourcesDoNotCreateAdditionalProgramAssets() {
            string[] partialSources = {
                "Packages/red.sim.lightvolumes/UScripts/PointLightVolumeInstance.Editor.cs",
                "Packages/red.sim.lightvolumes/UScripts/PointLightVolumeInstance.ShadowBaking.cs"
            };

            for (int i = 0; i < partialSources.Length; i++) {
                string sourcePath = partialSources[i];
                Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>(sourcePath), Is.Not.Null, "Missing partial source " + sourcePath);
                string programPath = sourcePath.Substring(0, sourcePath.Length - 3) + ".asset";
                Assert.That(AssetDatabase.LoadMainAssetAtPath(programPath), Is.Null, "Partial source must not own a UdonSharpProgramAsset: " + programPath);
            }
        }

        private static void AssertPublicProperty(string propertyName, Type propertyType) {
            PropertyInfo property = typeof(LightVolumeManager).GetProperty(propertyName, PublicInstanceDeclared);
            Assert.That(property, Is.Not.Null, "Missing public property " + propertyName);
            Assert.That(property.PropertyType, Is.EqualTo(propertyType), propertyName + " changed type");
            Assert.That(property.GetGetMethod(), Is.Not.Null, propertyName + " must retain a public getter");
        }

        private static void AssertPublicMethod(string methodName, Type returnType, params Type[] parameterTypes) {
            MethodInfo method = typeof(LightVolumeManager).GetMethod(methodName, PublicInstanceDeclared, null, parameterTypes, null);
            Assert.That(method, Is.Not.Null, "Missing public method " + FormatMethodSignature(methodName, parameterTypes));
            Assert.That(method.ReturnType, Is.EqualTo(returnType), methodName + " changed return type");
        }

        private static void AssertPublicParameterlessVoidMethod(string methodName) {
            AssertPublicMethod(methodName, typeof(void), Type.EmptyTypes);
        }

        private static void AssertDeclaredParameterlessVoidMethod(string methodName) {
            MethodInfo method = typeof(LightVolumeManager).GetMethod(methodName, _lifecycleMethodFlags | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            Assert.That(method, Is.Not.Null, "Missing Unity/Udon lifecycle entry point " + methodName + "()");
            Assert.That(method.ReturnType, Is.EqualTo(typeof(void)), methodName + " changed return type");
        }

        private static string FormatMethodSignature(string methodName, Type[] parameterTypes) {
            string signature = methodName + "(";
            for (int i = 0; i < parameterTypes.Length; i++) {
                if (i > 0) signature += ", ";
                signature += parameterTypes[i].Name;
            }
            return signature + ")";
        }

        // The migration cache keeps only exact legacy MonoBehaviour documents in one YAML pass.
        [Test]
        public void SceneYamlLegacyRuntimeBlockExtractionFiltersDocumentsAndIds() {
            string sceneYaml =
                "--- !u!114 &12\n" +
                "MonoBehaviour:\n" +
                "  RelativeRotation: {x: 0, y: 0, z: 0, w: 1}\n" +
                "--- !u!114 &120\n" +
                "MonoBehaviour:\n" +
                "  PositionData: {x: 1, y: 2, z: 3, w: 4}\n" +
                "--- !u!114 &23\n" +
                "MonoBehaviour:\n" +
                "  Position: {x: 1, y: 2, z: 3}\n" +
                "  RelativeRotationRow0: {x: 1, y: 0, z: 0}\n" +
                "  BoundsUvwMin0: {x: 0, y: 0, z: 0, w: 1}\n" +
                "--- !u!1 &34\n" +
                "GameObject:\n" +
                "  PositionData: ignored\n" +
                "--- !u!114 &45\n" +
                "MonoBehaviour:\n" +
                "  m_Name: Current Data Only\n" +
                "--- !u!114 &56\n" +
                "MonoBehaviour:\n" +
                "  _legacyDirectionData: {x: 0, y: 0, z: 1, w: 0}\n";

            MethodInfo method = typeof(LightVolumeMigration).GetMethod("BuildLegacyRuntimeBlocks", _staticMigrationMethodFlags);
            Assert.That(method, Is.Not.Null);
            Dictionary<ulong, string> blocks = (Dictionary<ulong, string>)method.Invoke(null, new object[] { sceneYaml });

            Assert.That(blocks.Count, Is.EqualTo(3));
            Assert.That(blocks.ContainsKey(12), Is.True);
            Assert.That(blocks.ContainsKey(120), Is.True);
            Assert.That(blocks.ContainsKey(56), Is.True);
            Assert.That(blocks[12], Does.Contain("RelativeRotation:"));
            Assert.That(blocks[120], Does.Contain("PositionData:"));
            Assert.That(blocks[56], Does.Contain("_legacyDirectionData:"));
            Assert.That(blocks.ContainsKey(23), Is.False);
            Assert.That(blocks.ContainsKey(34), Is.False);
            Assert.That(blocks.ContainsKey(45), Is.False);
        }

        // Destroys all temporary scene and texture objects created by a test case.
        [TearDown]
        public void TearDown() {
            LogAssert.ignoreFailingMessages = false;
            AsyncGPUReadback.WaitAllRequests();
            ResetShaderGlobals();
            for (int i = _createdObjects.Count - 1; i >= 0; i--) {
                DestroyTestObject(_createdObjects[i]);
            }
            _createdObjects.Clear();
        }

        // Returns a private LightVolumeManager field used by focused regression tests.
        private static T GetManagerField<T>(LightVolumeManager manager, FieldInfo field) {
            return (T)field.GetValue(manager);
        }

        // Assigns a private LightVolumeManager field used by focused regression tests.
        private static void SetManagerField<T>(LightVolumeManager manager, FieldInfo field, T value) {
            field.SetValue(manager, value);
        }

        // Injects an area cookie average readback result into the manager's private callback path.
        private static void UploadAreaCookieAverageColor(LightVolumeManager manager, int customId, Color color) {
            Assert.That(_uploadAreaCookieAverageColorMethod, Is.Not.Null);
            AsyncGPUReadback.WaitAllRequests();
            _uploadAreaCookieAverageColorMethod.Invoke(manager, new object[] { customId, color });
        }

        // Returns the cached area-cookie fallback average for a point light registry index.
        private static Color GetAreaCookieAverageColor(LightVolumeManager manager, int registryIndex) {
            Color[] colors = GetManagerField<Color[]>(manager, _pointLightAreaCookieAverageColorsField);
            Assert.That(colors, Is.Not.Null);
            Assert.That(registryIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(registryIndex, Is.LessThan(colors.Length));
            return colors[registryIndex];
        }

        // Reads the runtime blur shader from either a Unity project root or this package directory.
        private static string ReadRuntimeShadowBlurShaderSource() {
            string projectPackagePath = Path.Combine("Packages", "red.sim.lightvolumes", RuntimeShadowBlurShaderPath);
            string packagePath = RuntimeShadowBlurShaderPath;
            string shaderPath = File.Exists(projectPackagePath) ? projectPackagePath : packagePath;
            Assert.That(File.Exists(shaderPath), Is.True, shaderPath + " was not found");
            return File.ReadAllText(shaderPath);
        }

        // Reads the public lighting include from either a Unity project root or this package directory.
        private static string ReadLightVolumesIncludeSource() {
            string projectPackagePath = Path.Combine("Packages", "red.sim.lightvolumes", LightVolumesIncludePath);
            string packagePath = LightVolumesIncludePath;
            string shaderPath = File.Exists(projectPackagePath) ? projectPackagePath : packagePath;
            Assert.That(File.Exists(shaderPath), Is.True, shaderPath + " was not found");
            return File.ReadAllText(shaderPath);
        }

        // Depth slice count must scale only Z; it must never silently trade away camera angular resolution.
        [Test]
        public void FroxelSlicesDoNotChangeAngularResolution() {
            LightVolumeManager manager = CreateManager("Independent Froxel Axes Manager", true);

            MethodInfo buildMethod = typeof(LightVolumeManager).GetMethod("BuildClustering", _lifecycleMethodFlags);
            FieldInfo unsupportedField = typeof(LightVolumeManager).GetField("_clusteringUnsupported", _lifecycleMethodFlags);
            FieldInfo fineGridField = typeof(LightVolumeManager).GetField("_fineGridParams", _lifecycleMethodFlags);
            Assert.That(buildMethod, Is.Not.Null);
            Assert.That(unsupportedField, Is.Not.Null);
            Assert.That(fineGridField, Is.Not.Null);

            unsupportedField.SetValue(manager, true); // Exercise layout math without allocating GPU textures.
            manager.FroxelDensity = 1f;
            object[] arguments = {
                Vector3.zero,
                Vector3.right,
                Vector3.up,
                Vector3.forward,
                60f,
                16f / 9f,
                0.3f,
                100f,
                0f,
                0f,
                null
            };

            manager.FroxelSlices = 8;
            buildMethod.Invoke(manager, arguments);
            Vector4 shallowGrid = (Vector4)fineGridField.GetValue(manager);

            manager.FroxelSlices = 200;
            buildMethod.Invoke(manager, arguments);
            Vector4 deepGrid = (Vector4)fineGridField.GetValue(manager);

            float horizontalFov = Mathf.Rad2Deg * 2f * Mathf.Atan(Mathf.Tan(30f * Mathf.Deg2Rad) * (16f / 9f));
            int expectedColumns = Mathf.CeilToInt(horizontalFov);
            int expectedRows = 60;

            Assert.That(shallowGrid.x, Is.EqualTo(expectedColumns));
            Assert.That(shallowGrid.z, Is.EqualTo(expectedRows));
            Assert.That(shallowGrid.y, Is.EqualTo(8));
            Assert.That(deepGrid.x, Is.EqualTo(expectedColumns));
            Assert.That(deepGrid.z, Is.EqualTo(expectedRows));
            Assert.That(deepGrid.y, Is.EqualTo(200));
        }

        // The first fitting shift minimizes padding and avoids extra runtime work for equal-area alternatives.
        [Test]
        public void FroxelAtlasPackingUsesFirstMinimumAreaLayout() {
            int tileShift = ResolveFroxelAtlasTileShift(96, 100);
            int tileColumns = 1 << tileShift;
            int width = 180 * tileColumns;
            int height = 100 * ((96 + tileColumns - 1) >> tileShift);

            Assert.That(tileShift, Is.EqualTo(2));
            Assert.That(width, Is.EqualTo(720));
            Assert.That(height, Is.EqualTo(2400));
            Assert.That(width * height, Is.EqualTo(180 * 96 * 100));
        }

        // A squarer candidate must never win when it introduces even one incomplete tile row.
        [Test]
        public void FroxelAtlasPackingNeverTradesAreaForAspectRatio() {
            int tileShift = ResolveFroxelAtlasTileShift(33, 29);
            int tileColumns = 1 << tileShift;
            int selectedArea = 30 * tileColumns * 29 * ((33 + tileColumns - 1) >> tileShift);
            int minimumArea = 30 * 33 * 29;

            Assert.That(tileShift, Is.Zero);
            Assert.That(selectedArea, Is.EqualTo(minimumArea));
        }

        // The largest logical grid must still resolve to a portable 4096-by-4096 atlas.
        [Test]
        public void FroxelAtlasPackingKeepsTextureDimensionsPortable() {
            int tileShift = ResolveFroxelAtlasTileShift(256, 256);
            int tileColumns = 1 << tileShift;
            int width = 256 * tileColumns;
            int height = 256 * ((256 + tileColumns - 1) >> tileShift);

            Assert.That(tileShift, Is.EqualTo(4));
            Assert.That(width, Is.LessThanOrEqualTo(4096));
            Assert.That(height, Is.LessThanOrEqualTo(4096));
        }

        // Invokes the runtime packing selector without allocating clustering render textures.
        private static int ResolveFroxelAtlasTileShift(int rows, int depthSlices) {
            MethodInfo method = typeof(LightVolumeManager).GetMethod("ResolveFroxelAtlasTileShift", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (int)method.Invoke(null, new object[] { rows, depthSlices });
        }

        // The builder and both Scene View clustering previews must compile on the active editor graphics API.
        [Test]
        public void FroxelPreviewShadersAreSupported() {
            Shader builder = Shader.Find("Hidden/VRCLV/FroxelClusteringBuild");
            Shader fine = Shader.Find("Hidden/LV_DebugDisplayFineClustering");
            Shader coarse = Shader.Find("Hidden/LV_DebugDisplayCoarseClustering");

            Assert.That(builder, Is.Not.Null);
            Assert.That(builder.isSupported, Is.True);
            Assert.That(builder.passCount, Is.EqualTo(1));
            Assert.That(fine, Is.Not.Null);
            Assert.That(fine.isSupported, Is.True);
            Assert.That(coarse, Is.Not.Null);
            Assert.That(coarse.isSupported, Is.True);
        }

        // Native editor texture loss must invalidate the C# cache even when camera and layout values stay unchanged.
        [Test]
        public void FroxelClusteringRecreatesReleasedNativeTexture() {
            if (SystemInfo.graphicsShaderLevel < 35 || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBInt)) {
                Assert.Ignore("The active graphics API does not support the packed froxel mask format.");
            }
            Shader clusteringShader = Shader.Find("Hidden/VRCLV/FroxelClusteringBuild");
            if (clusteringShader == null || !clusteringShader.isSupported) {
                Assert.Ignore("The froxel clustering build shader is unavailable on the active graphics API.");
            }

            LightVolumeManager manager = CreateManager("Released Froxel Texture Manager", false);
            manager.Clustering = true;
            manager.ClusteringMinLights = 1;
            manager.FroxelDensity = 0.1f;
            manager.FroxelSlices = 8;
            manager.FroxelCoarse = 2;
            PointLightVolumeInstance point = CreatePointLight(manager, "Released Froxel Texture Light", true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            GameObject cameraObject = CreateGameObject("Released Froxel Texture Camera", false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.aspect = 16f / 9f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

            FieldInfo clusterMaskField = typeof(LightVolumeManager).GetField("_clusterMask", _lifecycleMethodFlags);
            Assert.That(clusterMaskField, Is.Not.Null);
            manager.UpdateClusteringFromCamera(camera);
            RenderTexture clusterMask = (RenderTexture)clusterMaskField.GetValue(manager);
            Assert.That(clusterMask, Is.Not.Null);
            Assert.That(clusterMask.IsCreated(), Is.True);
            Assert.That(manager.ClusteringMaterial, Is.Null, "Scene View preview must not serialize its generated material on the manager");
            PropertyInfo generatedMaterialProperty = typeof(LightVolumeManager).GetProperty("_generatedClusteringMaterial", _lifecycleMethodFlags);
            Assert.That(generatedMaterialProperty, Is.Not.Null);
            object generatedMaterial = generatedMaterialProperty.GetValue(manager);
            Assert.That(generatedMaterial, Is.Not.Null);
            Assert.That(manager.ClusteringMaterialPreview, Is.SameAs(generatedMaterial), "The editor Debug view must expose the generated preview material");

            clusterMask.Release();
            Assert.That(clusterMask.IsCreated(), Is.False);
            manager.UpdateClusteringFromCamera(camera);

            RenderTexture recoveredMask = (RenderTexture)clusterMaskField.GetValue(manager);
            Assert.That(recoveredMask, Is.Not.Null);
            Assert.That(recoveredMask.IsCreated(), Is.True);

            int clusteringEnabledID = Shader.PropertyToID("_UdonClusteringEnabled");
            int fineMaskID = Shader.PropertyToID("_UdonClusterMask");
            int coarseMaskID = Shader.PropertyToID("_UdonCoarseClusterMask");
            Shader.SetGlobalFloat(clusteringEnabledID, 1f);
            manager.ReleaseClusteringPreview();
            Assert.That(Shader.GetGlobalFloat(clusteringEnabledID), Is.Zero);
            Assert.That(Shader.GetGlobalTexture(fineMaskID), Is.Null);
            Assert.That(Shader.GetGlobalTexture(coarseMaskID), Is.Null);
        }

        // Domain reload recovery must distrust every restored runtime gate, not only the depth cache.
        [Test]
        public void EditorRuntimeRecoveryRebuildsRestoredState() {
            LightVolumeManager manager = CreateManager("Domain Reload Recovery Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Domain Reload Recovery Light", true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.Clustering = true;
            manager.ClusteringMinLights = 1;
            manager.UpdateVolumes();

            string[] trueFields = {
                "_isUpdatingVolumes",
                "_clusterGeometryUploadPending",
                "_clusteringUnsupported",
                "_clusteringAllocationFailed",
                "_clusteringActive",
                "_froxelLayoutValid",
                "_froxelDepthValid",
                "_froxelProjectionValid",
                "_clusterMaskValid"
            };
            for (int i = 0; i < trueFields.Length; i++) {
                FieldInfo field = typeof(LightVolumeManager).GetField(trueFields[i], _lifecycleMethodFlags);
                Assert.That(field, Is.Not.Null, trueFields[i]);
                field.SetValue(manager, true);
            }

            FieldInfo initializedField = typeof(LightVolumeManager).GetField("_isInitialized", _lifecycleMethodFlags);
            FieldInfo pointCountField = typeof(LightVolumeManager).GetField("_pointLightCount", _lifecycleMethodFlags);
            FieldInfo clusteringEnabledIdField = typeof(LightVolumeManager).GetField("_clusteringEnabledID", _lifecycleMethodFlags);
            FieldInfo clusteringLightsDirtyField = typeof(LightVolumeManager).GetField("_clusteringLightsDirty", _lifecycleMethodFlags);
            FieldInfo maskDirtyField = typeof(LightVolumeManager).GetField("_clusterMaskDirty", _lifecycleMethodFlags);
            MethodInfo recover = typeof(LightVolumeManager).GetMethod("RebuildEditorRuntimeState", _lifecycleMethodFlags);
            Assert.That(initializedField, Is.Not.Null);
            Assert.That(pointCountField, Is.Not.Null);
            Assert.That(clusteringEnabledIdField, Is.Not.Null);
            Assert.That(clusteringLightsDirtyField, Is.Not.Null);
            Assert.That(maskDirtyField, Is.Not.Null);
            Assert.That(recover, Is.Not.Null);

            initializedField.SetValue(manager, true);
            pointCountField.SetValue(manager, 77);
            clusteringEnabledIdField.SetValue(manager, 0);
            clusteringLightsDirtyField.SetValue(manager, false);
            maskDirtyField.SetValue(manager, false);
            int clusteringEnabledID = Shader.PropertyToID("_UdonClusteringEnabled");
            Shader.SetGlobalFloat(_pointLightCountID, 0f);
            Shader.SetGlobalFloat(clusteringEnabledID, 1f);

            recover.Invoke(manager, null);

            Assert.That((bool)initializedField.GetValue(manager), Is.True);
            Assert.That((int)pointCountField.GetValue(manager), Is.EqualTo(1));
            Assert.That((int)clusteringEnabledIdField.GetValue(manager), Is.EqualTo(clusteringEnabledID));
            Assert.That((bool)clusteringLightsDirtyField.GetValue(manager), Is.True);
            Assert.That((bool)maskDirtyField.GetValue(manager), Is.True);
            for (int i = 0; i < trueFields.Length; i++) {
                FieldInfo field = typeof(LightVolumeManager).GetField(trueFields[i], _lifecycleMethodFlags);
                Assert.That((bool)field.GetValue(manager), Is.False, trueFields[i]);
            }
            AssertGlobalFloat(_pointLightCountID, 1f);
            AssertGlobalFloat(clusteringEnabledID, 0f);

            recover.Invoke(manager, null);
            Assert.That((int)pointCountField.GetValue(manager), Is.EqualTo(1));
            AssertGlobalFloat(_pointLightCountID, 1f);
            AssertGlobalFloat(clusteringEnabledID, 0f);
        }

        // Scene-only UdonSharp proxies can restore the persistent ShadowMap field without the
        // derived ShadowMapTexture field. Recovery must rebuild every authoring mirror before the
        // Manager enumerates sources, otherwise only prefab-authored runtime mirrors reach the atlas.
        [Test]
        public void EditorRuntimeRecoveryRestoresSceneShadowSourcesBeforeRebuildingAtlas() {
            LightVolumeManager manager = CreateManager("Scene Shadow Recovery Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;
            PointLightVolumeInstance preserved = CreatePointLight(manager, "Prefab-like Shadow Source", true);
            PointLightVolumeInstance restored = CreatePointLight(manager, "Scene-only Shadow Source", true);
            Texture2D preservedShadow = CreateTexture2D("Preserved Shadow");
            Texture2D restoredShadow = CreateTexture2D("Restored Shadow");

            preserved.Shadows = true;
            preserved.ShadowMap = preservedShadow;
            preserved.EditorApplyAuthoringData(false, true, false);
            restored.Shadows = true;
            restored.ShadowMap = restoredShadow;
            restored.EditorApplyAuthoringData(false, true, false);
            manager.PointLightVolumeInstances = new[] { preserved, restored };
            manager.UpdateVolumes();
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(2));

            // Reproduce the asymmetric restore seen after editor restart/Play Mode: prefab data
            // remains mirrored while the ordinary scene object's derived source is lost.
            restored.ShadowMapTexture = null;
            restored.ShadowMapMaterial = null;
            restored.ShadowMapID = -1f;
            manager.ReinitializeShadowTextures();
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(1));

            manager.RebuildEditorRuntimeState();

            Assert.That(restored.ShadowMapTexture, Is.SameAs(restoredShadow));
            Assert.That(restored.ShadowMapID, Is.GreaterThanOrEqualTo(0f));
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(2));
            Assert.That(manager.ShadowTextures, Is.Not.Null);
            AssertGlobalFloat(_pointLightShadowCountID, 2f);
            AssertGlobalFloat(_lightVolumeEnabledID, 1f);
        }

        // Saving a scene must queue Manager recovery without relying on a Scene View camera render.
        [Test]
        public void SceneSaveQueuesCameraIndependentManagerRecovery() {
            LightVolumeManager manager = CreateManager("Scene Save Recovery Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Scene Save Recovery Point", true);
            manager.PointLightVolumeInstances = new[] { point };
            Type updaterType = typeof(LightVolumeManagerEditorBackend).Assembly.GetType("VRCLightVolumes.LightVolumeEditorUpdater");
            Assert.That(updaterType, Is.Not.Null);
            MethodInfo onSceneSaved = updaterType.GetMethod("OnSceneSaved", _staticMigrationMethodFlags);
            MethodInfo recoverAfterSceneSave = updaterType.GetMethod("RecoverAfterSceneSave", _staticMigrationMethodFlags);
            MethodInfo flush = updaterType.GetMethod("Flush", _staticMigrationMethodFlags, null, Type.EmptyTypes, null);
            FieldInfo flushQueued = updaterType.GetField("_flushQueued", _staticMigrationMethodFlags);
            FieldInfo managerUpdateQueued = updaterType.GetField("_managerUpdateQueued", _staticMigrationMethodFlags);
            Type previewType = typeof(LightVolumeManagerEditorBackend).Assembly.GetType("VRCLightVolumes.LightVolumeClusteringPreview");
            FieldInfo previewRefreshPending = previewType?.GetField("_refreshPending", _staticMigrationMethodFlags);
            Assert.That(onSceneSaved, Is.Not.Null);
            Assert.That(recoverAfterSceneSave, Is.Not.Null);
            Assert.That(flush, Is.Not.Null);
            Assert.That(flushQueued, Is.Not.Null);
            Assert.That(managerUpdateQueued, Is.Not.Null);
            Assert.That(previewType, Is.Not.Null);
            Assert.That(previewRefreshPending, Is.Not.Null);

            EditorApplication.CallbackFunction recoveryCallback = (EditorApplication.CallbackFunction)Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), recoverAfterSceneSave);
            EditorApplication.CallbackFunction flushCallback = (EditorApplication.CallbackFunction)Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), flush);
            EditorApplication.delayCall -= recoveryCallback;
            EditorApplication.delayCall -= flushCallback;
            flushQueued.SetValue(null, true);
            managerUpdateQueued.SetValue(null, true);
            previewRefreshPending.SetValue(null, true);
            EditorApplication.delayCall += flushCallback;
            Shader.SetGlobalFloat(_pointLightCountID, 0f);
            try {
                onSceneSaved.Invoke(null, new object[] { default(UnityEngine.SceneManagement.Scene) });
                recoverAfterSceneSave.Invoke(null, null);
            } finally {
                EditorApplication.delayCall -= recoveryCallback;
                EditorApplication.delayCall -= flushCallback;
                flushQueued.SetValue(null, false);
                managerUpdateQueued.SetValue(null, false);
                previewRefreshPending.SetValue(null, false);
            }

            Assert.That((bool)flushQueued.GetValue(null), Is.False);
            Assert.That((bool)managerUpdateQueued.GetValue(null), Is.False);
            Assert.That((bool)previewRefreshPending.GetValue(null), Is.False);
            AssertGlobalFloat(_pointLightCountID, 1f);
        }

        // Edit-mode UdonSharp proxies do not dispatch delayed events, so a request must upload immediately.
        [Test]
        public void EditModeUpdateRequestPublishesPointLightsWithoutDelayedUdonEvent() {
            LightVolumeManager manager = CreateManager("Edit Mode Bootstrap Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Edit Mode Bootstrap Light", true);
            manager.PointLightVolumeInstances = new[] { point };

            Shader.SetGlobalFloat(_pointLightCountID, 0f);
            manager.RequestUpdateVolumes();

            AssertGlobalFloat(_pointLightCountID, 1f);
        }

        // Unified Udon proxies rely on one editor change coordinator instead of per-object ExecuteAlways polling.
        [Test]
        public void EditorChangeCoordinatorSynchronizesTransformAndActiveLifecycle() {
            Type coordinatorType = typeof(LightVolumeManagerEditorBackend).Assembly.GetType(
                "VRCLightVolumes.LightVolumeEditorUpdater");
            Assert.That(coordinatorType, Is.Not.Null);
            MethodInfo queueObject = coordinatorType.GetMethod("QueueObject", _staticMigrationMethodFlags);
            MethodInfo flush = coordinatorType.GetMethod("FlushPendingSceneChanges", _staticMigrationMethodFlags);
            Assert.That(queueObject, Is.Not.Null);
            Assert.That(flush, Is.Not.Null);

            // Do not let a refresh queued by the previous test update unrelated scene managers
            // after this test manager and overwrite process-wide shader globals.
            flush.Invoke(null, null);
            ResetShaderGlobals();

            LightVolumeManager manager = CreateManager("Editor Change Coordinator Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Editor Change Coordinator Point", true);
            manager.PointLightVolumeInstances = new[] { point };
            point.transform.position = new Vector3(3.5f, -2f, 7.25f);

            queueObject.Invoke(null, new object[] { point.transform });
            flush.Invoke(null, null);

            AssertVectorClose(new Vector4(3.5f, -2f, 7.25f, 0f), new Vector4(point.Position.x, point.Position.y, point.Position.z, 0f));
            AssertGlobalFloat(_pointLightCountID, 1f);
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);

            point.gameObject.SetActive(false);
            queueObject.Invoke(null, new object[] { point.gameObject });
            flush.Invoke(null, null);
            Assert.That(point.IsActive, Is.False);
            AssertGlobalFloat(_pointLightCountID, 0f);

            point.gameObject.SetActive(true);
            queueObject.Invoke(null, new object[] { point.gameObject });
            flush.Invoke(null, null);
            Assert.That(point.IsActive, Is.True);
            AssertGlobalFloat(_pointLightCountID, 1f);

            manager.enabled = false;
            Shader.SetGlobalFloat(_lightVolumeEnabledID, 1f);
            Shader.SetGlobalFloat(_pointLightCountID, 1f);
            queueObject.Invoke(null, new object[] { manager });
            flush.Invoke(null, null);
            AssertGlobalFloat(_lightVolumeEnabledID, 0f);
            AssertGlobalFloat(_pointLightCountID, 0f);

            manager.enabled = true;
            queueObject.Invoke(null, new object[] { manager });
            flush.Invoke(null, null);
            AssertGlobalFloat(_lightVolumeEnabledID, 1f);
            AssertGlobalFloat(_pointLightCountID, 1f);
        }

        // Build-scene canonicalization runs before play-mode callbacks. An initially inactive point
        // light must therefore publish the reconciled activity mirror to its backing Udon heap even
        // though Unity never sent that object an initial OnDisable event.
        [Test]
        public void BuildCanonicalizationWritesInactivePointStateToBackingUdonHeap() {
            GameObject root = CreateGameObject("Inactive Build Canonicalization Root", true);
            GameObject managerObject = CreateGameObject("Inactive Build Canonicalization Manager", true);
            GameObject pointObject = CreateGameObject("Inactive Build Canonicalization Point", false);
            managerObject.transform.SetParent(root.transform, false);
            pointObject.transform.SetParent(root.transform, false);

            LightVolumeManager manager = managerObject.AddUdonSharpComponent<LightVolumeManager>();
            PointLightVolumeInstance point = pointObject.AddUdonSharpComponent<PointLightVolumeInstance>();
            manager.LightVolumeInstances = new LightVolumeInstance[0];
            manager.PointLightVolumeInstances = new[] { point };
            manager.AutoUpdateVolumes = false;
            point.LightVolumeManager = manager;
            point.Color = Color.white;
            point.Intensity = 1f;
            point.IsActive = true;

            UdonSharpEditorUtility.CopyProxyToUdon(point);
            UdonSharpEditorUtility.CopyProxyToUdon(manager);
            var pointBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(point);
            Assert.That(pointBacking, Is.Not.Null);
            Assert.That(pointBacking.publicVariables.TryGetVariableValue("IsActive", out object staleBackingActive), Is.True);
            Assert.That(staleBackingActive, Is.EqualTo(true), "The fixture did not begin with stale serialized activity.");

            Type preprocessorType = typeof(LightVolumeManagerEditorBackend).Assembly.GetType("VRCLightVolumes.LightVolumePreprocessor");
            Assert.That(preprocessorType, Is.Not.Null);
            MethodInfo canonicalizeBuildScene = preprocessorType.GetMethod("CanonicalizeBuildScene", _staticMigrationMethodFlags);
            Assert.That(canonicalizeBuildScene, Is.Not.Null);

            canonicalizeBuildScene.Invoke(null, new object[] { new[] { root }, manager });

            Assert.That(point.IsActive, Is.False);
            Assert.That(pointBacking.publicVariables.TryGetVariableValue("IsActive", out object canonicalBackingActive), Is.True);
            Assert.That(canonicalBackingActive, Is.EqualTo(false));
        }

        // Serialized and live Udon data must store the backing UdonBehaviour, never the managed U# proxy.
        [Test]
        public void RuntimeShadowDependenciesWriteManagerBackingToUdonHeap() {
            GameObject managerObject = CreateGameObject("Runtime Heap Manager", true);
            GameObject pointObject = CreateGameObject("Runtime Heap Point", true);
            // Tests run in the user's active scene. Avoid Undo-backed setup because a later Undo
            // replay can resurrect the containers after TearDown has destroyed them.
            LightVolumeManager manager = managerObject.AddUdonSharpComponent<LightVolumeManager>();
            PointLightVolumeInstance point = pointObject.AddUdonSharpComponent<PointLightVolumeInstance>();
            GameObject excludedObject = CreateGameObject("Runtime Heap Shadow Exclusion", true);
            MeshRenderer excludedRenderer = excludedObject.AddComponent<MeshRenderer>();
            point.LightVolumeManager = manager;
            point.Shadows = true;
            point.BakeInGame = true;
            point.RuntimeShadowBlurSamplePreset = 1;
            point.RuntimeShadowSphericalBlur = false;
            point.ShadowBakeResolution = 0;
            point.ExclusionMask = new Renderer[] { excludedRenderer };
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(manager);
            UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(point);

            Type preprocessorType = typeof(LightVolumeManagerEditorBackend).Assembly.GetType("VRCLightVolumes.LightVolumePreprocessor");
            Assert.That(preprocessorType, Is.Not.Null);
            MethodInfo applyDependencies = preprocessorType.GetMethod("ApplyPointLightRuntimeShadowDependencies", _staticMigrationMethodFlags);
            Assert.That(applyDependencies, Is.Not.Null);
            point.ShadowBakeResolution = 128;
            applyDependencies.Invoke(null, new object[] { point });

            var managerBacking = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            var pointBacking = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(point);
            Assert.That(managerBacking, Is.Not.Null);
            Assert.That(pointBacking, Is.Not.Null);
            Assert.That(pointBacking.publicVariables.TryGetVariableValue("LightVolumeManager", out object serializedManager), Is.True);
            Assert.That(serializedManager, Is.SameAs(managerBacking));
            Assert.That(serializedManager, Is.Not.SameAs(manager));
            Assert.That(pointBacking.publicVariables.TryGetVariableValue("ExclusionMask", out object serializedExclusionMask), Is.True);
            Assert.That(serializedExclusionMask, Is.EqualTo(new Renderer[] { excludedRenderer }));
            Assert.That(pointBacking.publicVariables.TryGetVariableValue("RuntimeShadowBlurSamplePreset", out object serializedBakeQuality), Is.True);
            Assert.That(serializedBakeQuality, Is.EqualTo(1));
            Assert.That(pointBacking.publicVariables.TryGetVariableValue("RuntimeShadowSphericalBlur", out object serializedSphericalBlur), Is.True);
            Assert.That(serializedSphericalBlur, Is.False);
            Assert.That(pointBacking.publicVariables.TryGetVariableValue("ShadowBakeResolution", out object serializedShadowBakeResolution), Is.True);
            Assert.That(serializedShadowBakeResolution, Is.EqualTo(128));

            GameObject cameraObject = CreateGameObject("Runtime Heap Camera", true);
            Camera runtimeCamera = cameraObject.AddComponent<Camera>();
            manager.RuntimeShadowCamera = runtimeCamera;
            manager.RuntimeShadowBlurQualityPreset = 2;
            manager.RuntimeShadowBlurUniformKeyword = 1;
            manager.RuntimeShadowBlurDirectKeyword = 1;
            manager.RuntimeShadowBlurSphericalKeyword = 1;
            point.RuntimeShadowCamera = runtimeCamera;

            MethodInfo applyManagerDependencies = preprocessorType.GetMethod("ApplyManagerRuntimeDependencies", _staticMigrationMethodFlags);
            MethodInfo clearDependencies = preprocessorType.GetMethod("ClearRuntimeDependencies", _staticMigrationMethodFlags);
            Assert.That(applyManagerDependencies, Is.Not.Null);
            Assert.That(clearDependencies, Is.Not.Null);
            applyManagerDependencies.Invoke(null, new object[] { manager });
            applyDependencies.Invoke(null, new object[] { point });
            clearDependencies.Invoke(null, new object[] { new[] { managerObject, pointObject }, manager });

            Assert.That(point.RuntimeShadowCamera, Is.Null);
            Assert.That(pointBacking.publicVariables.TryGetVariableValue("RuntimeShadowCamera", out object serializedPointCamera), Is.True);
            Assert.That(serializedPointCamera, Is.Null);
            Assert.That(manager.RuntimeShadowCamera, Is.SameAs(runtimeCamera));
            Assert.That(managerBacking.publicVariables.TryGetVariableValue("RuntimeShadowCamera", out object serializedManagerCamera), Is.True);
            Assert.That(serializedManagerCamera, Is.SameAs(runtimeCamera));
            Assert.That(manager.RuntimeShadowBlurQualityPreset, Is.EqualTo(-1));
            Assert.That(managerBacking.publicVariables.TryGetVariableValue("RuntimeShadowBlurQualityPreset", out object serializedBlurPreset), Is.True);
            Assert.That(serializedBlurPreset, Is.EqualTo(-1));
        }


        // Verifies that empty light-volume and point-light families do not block each other.
        [Test]
        public void EmptyVolumeFamiliesWriteIndependentCounts() {
            LightVolumeManager emptyManager = CreateManager("Empty Families Manager", true);

            emptyManager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 0);
            AssertGlobalFloat(_lightVolumeCountID, 0);
            AssertGlobalFloat(_pointLightCountID, 0);

            LightVolumeManager pointOnlyManager = CreateManager("Point Only Manager", false);
            PointLightVolumeInstance point = CreatePointLight(pointOnlyManager, "Point Only Light", true);
            LightVolumeInstance volumeWithoutAtlas = CreateLightVolume(pointOnlyManager, "Ignored No Atlas Volume", true);
            point.transform.position = new Vector3(1, 2, 3);
            SetPointLightSquaredSize(point, 2);
            point.SetPointLight();
            pointOnlyManager.LightVolumeInstances = new[] { volumeWithoutAtlas };
            pointOnlyManager.PointLightVolumeInstances = new[] { point };

            pointOnlyManager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 0);
            AssertGlobalFloat(_lightVolumeAdditiveCountID, 0);
            AssertGlobalFloat(_pointLightCountID, 1);
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);

            LightVolumeManager volumeOnlyManager = CreateManager("Volume Only Manager", true);
            LightVolumeInstance volume = CreateLightVolume(volumeOnlyManager, "Volume Only Light Volume", true);
            volumeOnlyManager.LightVolumeInstances = new[] { volume };
            volumeOnlyManager.PointLightVolumeInstances = new PointLightVolumeInstance[0];

            volumeOnlyManager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 1);
            AssertGlobalFloat(_pointLightCountID, 0);
            AssertVectorClose(ExpectedLightVolumeColor(volume), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);
        }

        // Verifies a disabled startup option and regular volume uploads preserve another system's global override.
        [Test]
        public void DisabledForceSceneLightingDoesNotOverrideExistingGlobal() {
            LightVolumeManager manager = CreateManager("Disabled Force Scene Lighting Manager", true);
            manager.ForceSceneLighting = false;
            Shader.SetGlobalInteger(_forceSceneLightingID, 1);

            InvokeLifecycleMethod(manager, "Start");
            manager.UpdateVolumes();

            AssertGlobalInteger(_forceSceneLightingID, 1);
            Assert.That(manager.ForceSceneLighting, Is.False);
        }

        // Verifies the enabled inspector option explicitly enables the shader override on startup.
        [Test]
        public void EnabledForceSceneLightingSetsGlobalOnStart() {
            LightVolumeManager manager = CreateManager("Enabled Force Scene Lighting Manager", true);
            manager.ForceSceneLighting = true;
            Shader.SetGlobalInteger(_forceSceneLightingID, 0);

            InvokeLifecycleMethod(manager, "Start");

            AssertGlobalInteger(_forceSceneLightingID, 1);
        }

        // Verifies the public runtime API can set either state without UpdateVolumes continuously reasserting it.
        [Test]
        public void SetForceSceneLightingControlsGlobalManually() {
            LightVolumeManager manager = CreateManager("Manual Force Scene Lighting Manager", true);

            manager.SetForceSceneLighting(true);
            AssertGlobalInteger(_forceSceneLightingID, 1);
            Assert.That(manager.ForceSceneLighting, Is.True);

            Shader.SetGlobalInteger(_forceSceneLightingID, 0);
            manager.UpdateVolumes();
            AssertGlobalInteger(_forceSceneLightingID, 0);

            manager.SetForceSceneLighting(false);
            AssertGlobalInteger(_forceSceneLightingID, 0);
            Assert.That(manager.ForceSceneLighting, Is.False);
        }

        // Verifies editor update errors do not leave the manager permanently locked until domain reload.
        [Test]
        public void UpdateVolumesClearsEditorGuardAfterException() {
            LightVolumeManager manager = CreateManager("Update Guard Manager", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Update Guard Point", true);
            manager.PointLightVolumeInstances = new[] { point };
            int[] enabledPointIDs = GetManagerField<int[]>(manager, _enabledPointIDsField);
            SetManagerField<int[]>(manager, _enabledPointIDsField, null);

            Assert.Catch<System.Exception>(() => manager.UpdateVolumes());
            Assert.That(GetManagerField<bool>(manager, _isUpdatingVolumesField), Is.False);

            SetManagerField(manager, _enabledPointIDsField, enabledPointIDs);
            Assert.DoesNotThrow(() => manager.UpdateVolumes());
        }

        // Verifies null public registries are treated as empty legacy data instead of an update failure.
        [Test]
        public void UpdateVolumesSanitizesNullRegistries() {
            LightVolumeManager manager = CreateManager("Null Registry Manager", true);
            manager.LightVolumeInstances = null;
            manager.PointLightVolumeInstances = null;

            Assert.DoesNotThrow(() => manager.UpdateVolumes());
            Assert.That(manager.LightVolumeInstances, Is.Empty);
            Assert.That(manager.PointLightVolumeInstances, Is.Empty);
            Assert.That(GetManagerField<bool>(manager, _isUpdatingVolumesField), Is.False);
        }

        // Exercises real GameObject enable and disable callbacks for unregister and re-register behavior.
        [Test]
        public void LifecycleCallbacksRegisterUnregisterAndReinitializeVolumes() {
            LightVolumeManager manager = CreateManager("Lifecycle Manager", true);
            LightVolumeInstance volume = CreateLightVolume(manager, "Lifecycle Volume", true);

            manager.UpdateVolumes();
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, volume), Is.True);
            Assert.That(CountLightVolumeReferences(manager.LightVolumeInstances, volume), Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 1);

            volume.gameObject.SetActive(false);
            InvokeLifecycleMethod(volume, "OnDisable");

            manager.UpdateVolumes();
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, volume), Is.False);
            AssertGlobalFloat(_lightVolumeEnabledID, 0);
            AssertGlobalFloat(_lightVolumeCountID, 0);
            AssertGlobalFloat(_pointLightCountID, 0);

            volume.gameObject.SetActive(true);
            InvokeLifecycleMethod(volume, "OnEnable");

            manager.UpdateVolumes();
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, volume), Is.True);
            Assert.That(CountLightVolumeReferences(manager.LightVolumeInstances, volume), Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 1);

            manager.gameObject.SetActive(false);
            InvokeLifecycleMethod(manager, "OnDisable");

            AssertGlobalFloat(_lightVolumeEnabledID, 0);
            AssertGlobalFloat(_lightVolumeCountID, 0);
            AssertGlobalFloat(_pointLightCountID, 0);
        }

        // Verifies runtime-style initialization and deinitialization for both regular and point light volumes.
        [Test]
        public void RuntimeLifecycleInitializesAndDeinitializesBothVolumeTypes() {
            LightVolumeManager manager = CreateManager("Runtime Lifecycle Manager", true);
            LightVolumeInstance volume = CreateUnregisteredLightVolume(manager, "Runtime Light Volume");
            PointLightVolumeInstance point = CreateUnregisteredPointLight(manager, "Runtime Point Light Volume");

            InvokeLifecycleMethod(volume, "OnEnable");
            InvokeLifecycleMethod(point, "OnEnable");
            manager.UpdateVolumes();

            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, volume), Is.True);
            Assert.That(ContainsPointLightVolume(manager.PointLightVolumeInstances, point), Is.True);
            Assert.That(CountLightVolumeReferences(manager.LightVolumeInstances, volume), Is.EqualTo(1));
            Assert.That(CountPointLightVolumeReferences(manager.PointLightVolumeInstances, point), Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeCountID, 1);
            AssertGlobalFloat(_pointLightCountID, 1);

            InvokeLifecycleMethod(volume, "OnDisable");
            InvokeLifecycleMethod(point, "OnDisable");
            manager.UpdateVolumes();

            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, volume), Is.False);
            Assert.That(ContainsPointLightVolume(manager.PointLightVolumeInstances, point), Is.False);
            AssertGlobalFloat(_lightVolumeEnabledID, 0);
            AssertGlobalFloat(_lightVolumeCountID, 0);
            AssertGlobalFloat(_pointLightCountID, 0);
        }

        // Child volumes leave a disabled manager clean and register exactly once when it is enabled again.
        [Test]
        public void ManagerObjectDisableUnregistersAndReinitializesVolumes() {
            LightVolumeManager manager = CreateManager("Inactive Manager Registry Owner", true);
            LightVolumeInstance regular = CreateLightVolume(manager, "Manager Child Regular Volume", true);
            LightVolumeInstance additive = CreateLightVolume(manager, "Manager Child Additive Volume", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Manager Child Point Volume", true);
            additive.IsAdditive = true;
            regular.transform.SetParent(manager.transform);
            additive.transform.SetParent(manager.transform);
            point.transform.SetParent(manager.transform);

            manager.UpdateVolumes();
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, regular), Is.True);
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, additive), Is.True);
            Assert.That(ContainsPointLightVolume(manager.PointLightVolumeInstances, point), Is.True);

            manager.gameObject.SetActive(false);
            // Plain UdonSharp proxies do not receive child lifecycle callbacks automatically in
            // Edit Mode, so invoke the callbacks Unity dispatches when this happens at runtime.
            InvokeLifecycleMethod(regular, "OnDisable");
            InvokeLifecycleMethod(additive, "OnDisable");
            InvokeLifecycleMethod(point, "OnDisable");

            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, regular), Is.False);
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, additive), Is.False);
            Assert.That(ContainsPointLightVolume(manager.PointLightVolumeInstances, point), Is.False);

            manager.gameObject.SetActive(true);
            InvokeLifecycleMethod(regular, "OnEnable");
            InvokeLifecycleMethod(additive, "OnEnable");
            InvokeLifecycleMethod(point, "OnEnable");
            manager.UpdateVolumes();

            Assert.That(CountLightVolumeReferences(manager.LightVolumeInstances, regular), Is.EqualTo(1));
            Assert.That(CountLightVolumeReferences(manager.LightVolumeInstances, additive), Is.EqualTo(1));
            Assert.That(CountPointLightVolumeReferences(manager.PointLightVolumeInstances, point), Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeCountID, 2);
            AssertGlobalFloat(_pointLightCountID, 1);
        }

        // Verifies runtime Light Volume registration preserves stable setup order instead of filling the first null slot.
        [Test]
        public void RuntimeLightVolumeRegistrationPreservesRegistryOrderAcrossDisableEnable() {
            LightVolumeManager manager = CreateManager("Stable Runtime Light Volume Order Manager", true);
            LightVolumeInstance first = CreateLightVolume(manager, "Stable First Volume", true);
            LightVolumeInstance second = CreateLightVolume(manager, "Stable Second Volume", true);
            LightVolumeInstance late = CreateUnregisteredLightVolume(manager, "Stable Late Volume");
            first.RegistryOrder = 0;
            second.RegistryOrder = 1;
            late.RegistryOrder = 2;

            manager.DeinitializeLightVolume(first);

            Assert.That(manager.LightVolumeInstances, Has.Length.EqualTo(1));
            Assert.That(manager.LightVolumeInstances[0], Is.SameAs(second));

            manager.InitializeLightVolume(late);

            Assert.That(manager.LightVolumeInstances, Has.Length.EqualTo(2));
            Assert.That(manager.LightVolumeInstances[0], Is.SameAs(second));
            Assert.That(manager.LightVolumeInstances[1], Is.SameAs(late));

            manager.InitializeLightVolume(first);

            Assert.That(manager.LightVolumeInstances, Has.Length.EqualTo(3));
            Assert.That(manager.LightVolumeInstances[0], Is.SameAs(first));
            Assert.That(manager.LightVolumeInstances[1], Is.SameAs(second));
            Assert.That(manager.LightVolumeInstances[2], Is.SameAs(late));
        }

        // Verifies runtime Point Light Volume registration preserves stable setup order instead of filling the first null slot.
        [Test]
        public void RuntimePointLightVolumeRegistrationPreservesRegistryOrderAcrossDisableEnable() {
            LightVolumeManager manager = CreateManager("Stable Runtime Point Light Volume Order Manager", false);
            PointLightVolumeInstance first = CreatePointLight(manager, "Stable First Point", true);
            PointLightVolumeInstance second = CreatePointLight(manager, "Stable Second Point", true);
            PointLightVolumeInstance late = CreateUnregisteredPointLight(manager, "Stable Late Point");
            first.RegistryOrder = 0;
            second.RegistryOrder = 1;
            late.RegistryOrder = 2;

            manager.DeinitializePointLightVolume(first, false, false);

            Assert.That(manager.PointLightVolumeInstances, Has.Length.EqualTo(1));
            Assert.That(manager.PointLightVolumeInstances[0], Is.SameAs(second));

            manager.InitializePointLightVolume(late);

            Assert.That(manager.PointLightVolumeInstances, Has.Length.EqualTo(2));
            Assert.That(manager.PointLightVolumeInstances[0], Is.SameAs(second));
            Assert.That(manager.PointLightVolumeInstances[1], Is.SameAs(late));

            manager.InitializePointLightVolume(first);

            Assert.That(manager.PointLightVolumeInstances, Has.Length.EqualTo(3));
            Assert.That(manager.PointLightVolumeInstances[0], Is.SameAs(first));
            Assert.That(manager.PointLightVolumeInstances[1], Is.SameAs(second));
            Assert.That(manager.PointLightVolumeInstances[2], Is.SameAs(late));
        }

        // Covers all overlap directions used when a legacy/null registry slot can be reused in place.
        [Test]
        public void RuntimeLightVolumeRegistrationReusesNullSlotsWithStableOrder() {
            LightVolumeManager manager = CreateManager("Light Volume Hole Reuse Manager", false, false);
            LightVolumeInstance first = CreateUnregisteredLightVolume(manager, "Hole Reuse Volume 0");
            LightVolumeInstance second = CreateUnregisteredLightVolume(manager, "Hole Reuse Volume 1");
            LightVolumeInstance third = CreateUnregisteredLightVolume(manager, "Hole Reuse Volume 2");
            LightVolumeInstance fourth = CreateUnregisteredLightVolume(manager, "Hole Reuse Volume 3");
            LightVolumeInstance fifth = CreateUnregisteredLightVolume(manager, "Hole Reuse Volume 4");
            first.RegistryOrder = 0;
            second.RegistryOrder = 1;
            third.RegistryOrder = 2;
            fourth.RegistryOrder = 3;
            fifth.RegistryOrder = 4;

            manager.LightVolumeInstances = new[] { null, first, second };
            manager.InitializeLightVolume(third);
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] { first, second, third }));

            manager.LightVolumeInstances = new[] { null, first, third, fourth };
            manager.InitializeLightVolume(second);
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] { first, second, third, fourth }));

            manager.LightVolumeInstances = new[] { first, second, fourth, null, fifth };
            manager.InitializeLightVolume(third);
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] { first, second, third, fourth, fifth }));
        }

        // Mirrors the regular-volume hole layouts and also exercises point-light index invalidation branches.
        [Test]
        public void RuntimePointLightVolumeRegistrationReusesNullSlotsWithStableOrder() {
            LightVolumeManager manager = CreateManager("Point Light Hole Reuse Manager", false, false);
            PointLightVolumeInstance first = CreateUnregisteredPointLight(manager, "Hole Reuse Point 0");
            PointLightVolumeInstance second = CreateUnregisteredPointLight(manager, "Hole Reuse Point 1");
            PointLightVolumeInstance third = CreateUnregisteredPointLight(manager, "Hole Reuse Point 2");
            PointLightVolumeInstance fourth = CreateUnregisteredPointLight(manager, "Hole Reuse Point 3");
            PointLightVolumeInstance fifth = CreateUnregisteredPointLight(manager, "Hole Reuse Point 4");
            PointLightVolumeInstance[] points = { first, second, third, fourth, fifth };
            for (int i = 0; i < points.Length; i++) {
                points[i].RegistryOrder = i;
                points[i].RegistryWeight = 0;
            }

            manager.PointLightVolumeInstances = new[] { null, first, second };
            manager.InitializePointLightVolume(third);
            Assert.That(manager.PointLightVolumeInstances, Is.EqualTo(new[] { first, second, third }));

            manager.PointLightVolumeInstances = new[] { null, first, third, fourth };
            manager.InitializePointLightVolume(second);
            Assert.That(manager.PointLightVolumeInstances, Is.EqualTo(new[] { first, second, third, fourth }));

            manager.PointLightVolumeInstances = new[] { first, second, fourth, null, fifth };
            manager.InitializePointLightVolume(third);
            Assert.That(manager.PointLightVolumeInstances, Is.EqualTo(new[] { first, second, third, fourth, fifth }));
        }

        // Verifies legacy serialized null slots are compacted without changing relative registry order.
        [Test]
        public void SanitizeRegistriesRemovesLegacyNullSlots() {
            LightVolumeManager manager = CreateManager("Registry Sanitation Manager", false);
            LightVolumeInstance firstVolume = CreateLightVolume(manager, "First Sanitation Volume", false);
            LightVolumeInstance secondVolume = CreateLightVolume(manager, "Second Sanitation Volume", false);
            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "First Sanitation Point", false);
            PointLightVolumeInstance secondPoint = CreatePointLight(manager, "Second Sanitation Point", false);
            manager.LightVolumeInstances = new[] { firstVolume, null, secondVolume, null };
            manager.PointLightVolumeInstances = new[] { null, firstPoint, null, secondPoint };

            bool changed = manager.SanitizeRegistries();

            Assert.That(changed, Is.True);
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] { firstVolume, secondVolume }));
            Assert.That(manager.PointLightVolumeInstances, Is.EqualTo(new[] { firstPoint, secondPoint }));
            Assert.That(manager.SanitizeRegistries(), Is.False);
        }

        // Verifies only the highest-weight active Light Volumes are uploaded when the registry exceeds shader capacity.
        [Test]
        public void UpdateVolumesUploadsHighestWeightedLightVolumesWithinShaderLimit() {
            LightVolumeManager manager = CreateManager("Weighted Light Volume Limit Manager", true);
            LightVolumeInstance[] volumes = new LightVolumeInstance[35];
            for (int i = 0; i < volumes.Length; i++) {
                LightVolumeInstance volume = CreateUnregisteredLightVolume(manager, "Weighted Light Volume " + i);
                volume.RegistryWeight = i;
                volume.RegistryOrder = i;
                ConfigureLightVolume(volume, new Color((i + 1) / 64f, 0.1f, 0.2f, 1), 1, false, 0.1f);
                manager.InitializeLightVolume(volume);
                volumes[i] = volume;
            }

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeCountID, 32);
            Vector4[] colors = Shader.GetGlobalVectorArray(_lightVolumeColorID);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[34]), colors[0]);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[3]), colors[31]);

            manager.DeinitializeLightVolume(volumes[34]);
            manager.UpdateVolumes();

            colors = Shader.GetGlobalVectorArray(_lightVolumeColorID);
            AssertGlobalFloat(_lightVolumeCountID, 32);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[33]), colors[0]);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[2]), colors[31]);

            manager.InitializeLightVolume(volumes[34]);
            manager.UpdateVolumes();

            colors = Shader.GetGlobalVectorArray(_lightVolumeColorID);
            AssertGlobalFloat(_lightVolumeCountID, 32);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[34]), colors[0]);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[3]), colors[31]);
        }

        // Verifies fused selection reads public fields afresh and reuses its key arrays after capacity growth.
        [Test]
        public void FusedLightVolumeSelectionIsFreshAndAllocationFreeAtSteadyCapacity() {
            LightVolumeManager manager = CreateManager("Fresh Fused Selection Manager", true);
            LightVolumeInstance[] volumes = new LightVolumeInstance[40];
            for (int i = 0; i < volumes.Length; i++) {
                LightVolumeInstance volume = CreateUnregisteredLightVolume(manager, "Fresh Selection Volume " + i);
                volume.IsActive = true;
                volume.RegistryWeight = 0f;
                volume.RegistryOrder = i;
                ConfigureLightVolume(volume, new Color((i + 1) / 64f, 0.2f, 0.1f, 1), 1f, false, 0.1f);
                volumes[i] = volume;
            }
            manager.LightVolumeInstances = volumes;

            manager.UpdateVolumes();

            float[] weightKeys = GetManagerField<float[]>(manager, _selectionLightVolumeWeightsField);
            int[] orderKeys = GetManagerField<int[]>(manager, _selectionLightVolumeOrdersField);
            Assert.That(weightKeys.Length, Is.GreaterThanOrEqualTo(volumes.Length));
            Assert.That(orderKeys.Length, Is.GreaterThanOrEqualTo(volumes.Length));
            AssertVectorClose(ExpectedLightVolumeColor(volumes[0]), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);

            // These bypass SetWeight/ReorderLightVolume and normal active-state notifications intentionally.
            // A persistent key or active cache would be stale here.
            volumes[39].RegistryWeight = 100f;
            volumes[38].RegistryOrder = -1;
            volumes[0].Intensity = 0f;
            manager.UpdateVolumes();

            Assert.That(GetManagerField<float[]>(manager, _selectionLightVolumeWeightsField), Is.SameAs(weightKeys));
            Assert.That(GetManagerField<int[]>(manager, _selectionLightVolumeOrdersField), Is.SameAs(orderKeys));
            Assert.That(volumes[0].IsActive, Is.False);
            Assert.That(manager.EnabledIDs[0], Is.EqualTo(39));
            Assert.That(manager.EnabledIDs[1], Is.EqualTo(38));
            Assert.That(manager.EnabledIDs[2], Is.EqualTo(1));
            Assert.That(manager.EnabledIDs[31], Is.EqualTo(30));
            Vector4[] colors = Shader.GetGlobalVectorArray(_lightVolumeColorID);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[39]), colors[0]);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[38]), colors[1]);
        }

        // Locks the historical exact-float comparator, stable duplicate-key ordering and null/inactive filtering.
        [Test]
        public void FusedLightVolumeSelectionPreservesSpecialFloatAndStableTieSemantics() {
            LightVolumeManager manager = CreateManager("Fused Selection Semantics Manager", true);
            LightVolumeInstance[] volumes = new LightVolumeInstance[12];
            for (int i = 0; i < volumes.Length; i++) {
                if (i == 9) continue;
                LightVolumeInstance volume = CreateUnregisteredLightVolume(manager, "Fused Semantics Volume " + i);
                volume.IsActive = true;
                volume.RegistryWeight = 0f;
                volume.RegistryOrder = i;
                volumes[i] = volume;
            }

            volumes[0].RegistryWeight = float.NaN;
            volumes[1].RegistryWeight = float.PositiveInfinity;
            volumes[2].RegistryWeight = 5f;
            volumes[3].RegistryWeight = 5f;
            volumes[3].RegistryOrder = 1;
            volumes[4].RegistryWeight = 0f;
            volumes[5].RegistryWeight = -0f;
            volumes[5].RegistryOrder = 3;
            volumes[6].RegistryWeight = float.NegativeInfinity;
            volumes[7].RegistryWeight = 5f;
            volumes[7].RegistryOrder = 1;
            volumes[8].RegistryWeight = float.PositiveInfinity;
            volumes[8].IsActive = false;
            volumes[10].RegistryWeight = 100f;
            volumes[11].RegistryWeight = float.NaN;
            volumes[11].RegistryOrder = -1;
            manager.LightVolumeInstances = volumes;

            int selectedCount = (int)_selectLightVolumesByWeightMethod.Invoke(manager, null);
            int[] selectedIDs = GetManagerField<int[]>(manager, _selectedLightVolumeIDsField);
            int[] expected = { 0, 1, 10, 3, 7, 2, 5, 4, 6, 11 };

            Assert.That(selectedCount, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
                Assert.That(selectedIDs[i], Is.EqualTo(expected[i]), "Selected ID mismatch at " + i);
        }

        // Verifies the shader cap is applied by weight before additive volumes are compacted into the prefix.
        [Test]
        public void UpdateVolumesAppliesLightVolumeLimitBeforeAdditiveCompaction() {
            LightVolumeManager manager = CreateManager("Weighted Additive Limit Manager", true);
            LightVolumeInstance additive = CreateUnregisteredLightVolume(manager, "Low Weight Additive Volume");
            additive.RegistryWeight = 0f;
            additive.RegistryOrder = 0;
            ConfigureLightVolume(additive, new Color(1f, 0.2f, 0.05f, 1), 1, true, 0.1f);
            manager.InitializeLightVolume(additive);

            LightVolumeInstance[] regulars = new LightVolumeInstance[32];
            for (int i = 0; i < regulars.Length; i++) {
                LightVolumeInstance regular = CreateUnregisteredLightVolume(manager, "Higher Weight Regular Volume " + i);
                regular.RegistryWeight = i + 1;
                regular.RegistryOrder = i + 1;
                ConfigureLightVolume(regular, new Color((i + 1) / 64f, 0.3f, 0.15f, 1), 1, false, 0.2f);
                manager.InitializeLightVolume(regular);
                regulars[i] = regular;
            }

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeCountID, 32);
            AssertGlobalFloat(_lightVolumeAdditiveCountID, 0);
            Vector4[] colors = Shader.GetGlobalVectorArray(_lightVolumeColorID);
            AssertVectorClose(ExpectedLightVolumeColor(regulars[31]), colors[0]);
            AssertVectorClose(ExpectedLightVolumeColor(regulars[0]), colors[31]);
        }

        // Authoring metadata synchronization never changes the Manager list when weights differ.
        [Test]
        public void ManagerAuthoringPreservesLightVolumeRegistryOrderAcrossWeights() {
            LightVolumeManager manager = CreateManager("Stable Authoring Registry Manager", true);
            LightVolumeInstance equalFirst = CreateUnregisteredLightVolume(manager, "Equal Weight First");
            LightVolumeInstance additiveHighest = CreateUnregisteredLightVolume(manager, "Highest Additive");
            LightVolumeInstance equalSecond = CreateUnregisteredLightVolume(manager, "Equal Weight Second");
            LightVolumeInstance lowest = CreateUnregisteredLightVolume(manager, "Lowest Regular");
            equalFirst.RegistryWeight = 5f;
            additiveHighest.RegistryWeight = 20f;
            additiveHighest.IsAdditive = true;
            equalSecond.RegistryWeight = 5f;
            lowest.RegistryWeight = -1f;
            manager.LightVolumeInstances = new[] {
                equalFirst,
                lowest,
                additiveHighest,
                equalSecond
            };

            LightVolumeManagerEditorBackend.SynchronizeRegistryMetadata(manager);

            Assert.That(manager.LightVolumeInstances, Is.EqualTo(new[] {
                equalFirst,
                lowest,
                additiveHighest,
                equalSecond
            }));
            for (int i = 0; i < 4; i++) {
                Assert.That(manager.LightVolumeInstances[i].RegistryOrder, Is.EqualTo(i));
                Assert.That(manager.LightVolumeInstances[i].LightVolumeManager, Is.SameAs(manager));
            }
        }

        // The Manager menu sort keeps weights authoritative and only applies resolution ordering
        // within equal-weight groups, preserving equal density authoring order.
        [Test]
        public void ManagerAuthoringSortsLightVolumesByVoxelsPerUnit() {
            LightVolumeManager manager = CreateManager("Voxel Density Sort Manager", true);
            LightVolumeInstance adaptiveLow = CreateUnregisteredLightVolume(manager, "Adaptive Low");
            LightVolumeInstance manualFirst = CreateUnregisteredLightVolume(manager, "Manual First");
            LightVolumeInstance adaptiveHighFirst = CreateUnregisteredLightVolume(manager, "Adaptive High First");
            LightVolumeInstance manualSecond = CreateUnregisteredLightVolume(manager, "Manual Second");
            LightVolumeInstance adaptiveHighSecond = CreateUnregisteredLightVolume(manager, "Adaptive High Second");
            LightVolumeInstance adaptiveMiddle = CreateUnregisteredLightVolume(manager, "Adaptive Middle");

            adaptiveLow.VoxelsPerUnit = 1f;
            manualFirst.AdaptiveResolution = false;
            adaptiveHighFirst.VoxelsPerUnit = 8f;
            manualSecond.AdaptiveResolution = false;
            adaptiveHighSecond.VoxelsPerUnit = 8f;
            adaptiveMiddle.VoxelsPerUnit = 4f;

            adaptiveLow.RegistryWeight = 30f;
            manualFirst.RegistryWeight = 10f;
            adaptiveHighFirst.RegistryWeight = 10f;
            manualSecond.RegistryWeight = 10f;
            adaptiveHighSecond.RegistryWeight = 10f;
            adaptiveMiddle.RegistryWeight = 10f;
            manager.LightVolumeInstances = new[] {
                adaptiveLow,
                manualFirst,
                adaptiveHighFirst,
                manualSecond,
                adaptiveHighSecond,
                adaptiveMiddle
            };

            LightVolumeManagerEditorBackend.SortLightVolumesByVoxelsPerUnit(manager);

            LightVolumeInstance[] expected = {
                adaptiveLow,
                manualFirst,
                manualSecond,
                adaptiveHighFirst,
                adaptiveHighSecond,
                adaptiveMiddle
            };
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(expected));
            Assert.That(manager.LightVolumeInstances[0].RegistryWeight, Is.EqualTo(30f).Within(Epsilon));
            Assert.That(manager.LightVolumeInstances[1].RegistryWeight, Is.EqualTo(10f).Within(Epsilon));
            Assert.That(manager.LightVolumeInstances[2].RegistryWeight, Is.EqualTo(10f).Within(Epsilon));
            Assert.That(manager.LightVolumeInstances[3].RegistryWeight, Is.EqualTo(10f).Within(Epsilon));
            Assert.That(manager.LightVolumeInstances[4].RegistryWeight, Is.EqualTo(10f).Within(Epsilon));
            Assert.That(manager.LightVolumeInstances[5].RegistryWeight, Is.EqualTo(10f).Within(Epsilon));
            for (int i = 0; i < expected.Length; i++) {
                Assert.That(expected[i].RegistryOrder, Is.EqualTo(i));
                Assert.That(expected[i].LightVolumeManager, Is.SameAs(manager));
            }

            LightVolumeManagerEditorBackend.SynchronizeRegistryMetadata(manager);
            Assert.That(manager.LightVolumeInstances, Is.EqualTo(expected));
        }

        // Verifies only the highest-weight active Point Light Volumes are uploaded when the registry exceeds shader capacity.
        [Test]
        public void UpdateVolumesUploadsHighestWeightedPointLightVolumesWithinShaderLimit() {
            LightVolumeManager manager = CreateManager("Weighted Point Light Volume Limit Manager", false);
            PointLightVolumeInstance[] points = new PointLightVolumeInstance[130];
            for (int i = 0; i < points.Length; i++) {
                PointLightVolumeInstance point = CreateUnregisteredPointLight(manager, "Weighted Point Light Volume " + i);
                point.RegistryWeight = i;
                point.RegistryOrder = i;
                point.Color = new Color((i + 1) / 160f, 0.2f, 0.1f, 1);
                manager.InitializePointLightVolume(point);
                points[i] = point;
            }

            manager.UpdateVolumes();

            AssertGlobalFloat(_pointLightCountID, 128);
            Vector4[] colors = Shader.GetGlobalVectorArray(_pointLightColorID);
            AssertVectorClose(ExpectedPointLightColor(points[129]), colors[0]);
            AssertVectorClose(ExpectedPointLightColor(points[2]), colors[127]);

            manager.DeinitializePointLightVolume(points[129], false, false);
            manager.UpdateVolumes();

            colors = Shader.GetGlobalVectorArray(_pointLightColorID);
            AssertGlobalFloat(_pointLightCountID, 128);
            AssertVectorClose(ExpectedPointLightColor(points[128]), colors[0]);
            AssertVectorClose(ExpectedPointLightColor(points[1]), colors[127]);

            manager.InitializePointLightVolume(points[129]);
            manager.UpdateVolumes();

            colors = Shader.GetGlobalVectorArray(_pointLightColorID);
            AssertGlobalFloat(_pointLightCountID, 128);
            AssertVectorClose(ExpectedPointLightColor(points[129]), colors[0]);
            AssertVectorClose(ExpectedPointLightColor(points[2]), colors[127]);
        }

        // Verifies SetWeight changes shader priority without moving the Manager's authoring registry.
        [Test]
        public void SetWeightPreservesRegistryOrderAndReordersShaderUpload() {
            LightVolumeManager manager = CreateManager("Set Weight Light Volume Manager", true);
            LightVolumeInstance first = CreateLightVolume(manager, "Set Weight First Volume", true);
            LightVolumeInstance second = CreateLightVolume(manager, "Set Weight Second Volume", true);
            ConfigureLightVolume(first, new Color(0.2f, 0.4f, 0.8f, 1), 1, false, 0.1f);
            ConfigureLightVolume(second, new Color(1f, 0.25f, 0.1f, 1), 1, false, 0.2f);

            second.SetWeight(10f);

            Assert.That(second.RegistryWeight, Is.EqualTo(10f).Within(Epsilon));
            Assert.That(manager.LightVolumeInstances[0], Is.SameAs(first));
            Assert.That(manager.LightVolumeInstances[1], Is.SameAs(second));
            manager.UpdateVolumes();

            Vector4[] colors = Shader.GetGlobalVectorArray(_lightVolumeColorID);
            AssertVectorClose(ExpectedLightVolumeColor(second), colors[0]);
            AssertVectorClose(ExpectedLightVolumeColor(first), colors[1]);

            first.SetWeight(20f);
            manager.UpdateVolumes();

            Assert.That(first.RegistryWeight, Is.EqualTo(20f).Within(Epsilon));
            Assert.That(manager.LightVolumeInstances[0], Is.SameAs(first));
            Assert.That(manager.LightVolumeInstances[1], Is.SameAs(second));
            colors = Shader.GetGlobalVectorArray(_lightVolumeColorID);
            AssertVectorClose(ExpectedLightVolumeColor(first), colors[0]);
            AssertVectorClose(ExpectedLightVolumeColor(second), colors[1]);
        }

        // Verifies SetWeight reorders registered Point Light Volumes and refreshes registry-indexed texture IDs.
        [Test]
        public void SetWeightReordersRegisteredPointLightVolumesAndRefreshesTextureIds() {
            LightVolumeManager manager = CreateManager("Set Weight Point Light Volume Manager", false);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;
            PointLightVolumeInstance cookie = CreatePointLight(manager, "Set Weight Cookie Point", true);
            PointLightVolumeInstance plain = CreatePointLight(manager, "Set Weight Plain Point", true);
            cookie.Color = new Color(0.2f, 0.4f, 0.8f, 1);
            plain.Color = new Color(1f, 0.25f, 0.1f, 1);
            cookie.CustomTexture = CreateTexture2D("Set Weight Cookie Source");
            cookie.ProjectionType = 1; // 1: texture
            cookie.SetLut();
            manager.ReinitializeCustomTextures();

            plain.SetWeight(10f);
            manager.UpdateVolumes();

            Assert.That(plain.RegistryWeight, Is.EqualTo(10f).Within(Epsilon));
            Assert.That(manager.PointLightVolumeInstances[0], Is.SameAs(plain));
            Assert.That(manager.PointLightVolumeInstances[1], Is.SameAs(cookie));
            Vector4[] colors = Shader.GetGlobalVectorArray(_pointLightColorID);
            AssertVectorClose(ExpectedPointLightColor(plain), colors[0]);
            AssertVectorClose(ExpectedPointLightColor(cookie), colors[1]);
            AssertPointCustomData(0, plain, 0, 0);
            AssertPointCustomData(1, cookie, 1, 0);
        }

        // Verifies active runtime-spawned regular volumes self-register when their manager reference is assigned later.
        [Test]
        public void LateManagerAssignmentInitializesActiveLightVolumeWithoutUpdatePolling() {
            LightVolumeManager manager = CreateManager("Late Assigned Volume Manager", true);
            LightVolumeInstance volume = CreateManagerlessLightVolume("Late Assigned Runtime Volume");

            Assert.That(volume.LightVolumeManager, Is.Null);
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, volume), Is.False);

            volume.LightVolumeManager = manager;
            volume._onVarChange_LightVolumeManager();
            manager.UpdateVolumes();

            Assert.That(volume.LightVolumeManager, Is.SameAs(manager));
            Assert.That(ContainsLightVolume(manager.LightVolumeInstances, volume), Is.True);
            Assert.That(CountLightVolumeReferences(manager.LightVolumeInstances, volume), Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 1);
            AssertVectorClose(ExpectedLightVolumeColor(volume), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);

            InvokeLifecycleMethod(volume, "OnEnable");
            manager.UpdateVolumes();

            Assert.That(CountLightVolumeReferences(manager.LightVolumeInstances, volume), Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeCountID, 1);
        }

        // Verifies active runtime-spawned point lights self-register when their manager reference is assigned later.
        [Test]
        public void LateManagerAssignmentInitializesActivePointLightWithoutUpdatePolling() {
            LightVolumeManager manager = CreateManager("Late Assigned Point Manager", false);
            PointLightVolumeInstance point = CreateManagerlessPointLight("Late Assigned Runtime Point");

            Assert.That(point.LightVolumeManager, Is.Null);
            Assert.That(ContainsPointLightVolume(manager.PointLightVolumeInstances, point), Is.False);

            point.LightVolumeManager = manager;
            point._onVarChange_LightVolumeManager();
            manager.UpdateVolumes();

            Assert.That(point.LightVolumeManager, Is.SameAs(manager));
            Assert.That(ContainsPointLightVolume(manager.PointLightVolumeInstances, point), Is.True);
            Assert.That(CountPointLightVolumeReferences(manager.PointLightVolumeInstances, point), Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_pointLightCountID, 1);
            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            InvokeLifecycleMethod(point, "OnEnable");
            manager.UpdateVolumes();

            Assert.That(CountPointLightVolumeReferences(manager.PointLightVolumeInstances, point), Is.EqualTo(1));
            AssertGlobalFloat(_pointLightCountID, 1);
        }

        // Editor synchronization and external program-variable callbacks must not reactivate a disabled component.
        [Test]
        public void DisabledComponentsRemainInactiveWhenRuntimeCallbacksRun() {
            LightVolumeManager manager = CreateManager("Disabled Callback Manager", true);
            LightVolumeInstance volume = CreateLightVolume(manager, "Disabled Callback Volume", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Disabled Callback Point", true);

            volume.enabled = false;
            point.enabled = false;
            volume.IsActive = true;
            point.IsActive = true;

            LightVolumeTools.ApplyRuntimeState(volume, false);
            Assert.That(volume.IsActive, Is.False);
            volume.IsActive = true;

            volume._onVarChange_IsDynamic();
            volume._onVarChange_IsAdditive();
            point._onVarChange_IsDynamic();

            Assert.That(volume.IsActive, Is.False);
            Assert.That(point.IsActive, Is.False);
        }

        // A late-opened Inspector starts with managed proxies that can lag behind the live Udon
        // heap. Pulling the Manager graph must recover both its own runtime state and the referenced
        // point-light quality/resolution fields before the Inspector is allowed to write anything.
        [Test]
        public void RuntimeInspectorGraphRefreshRecoversReferencedPointSettings() {
            GameObject managerObject = CreateGameObject("Late Inspector Graph Manager", true);
            GameObject pointObject = CreateGameObject("Late Inspector Graph Point", true);
            LightVolumeManager manager = managerObject.AddUdonSharpComponent<LightVolumeManager>();
            PointLightVolumeInstance point = pointObject.AddUdonSharpComponent<PointLightVolumeInstance>();
            manager.LightVolumeInstances = new LightVolumeInstance[0];
            manager.PointLightVolumeInstances = new[] { point };
            point.LightVolumeManager = manager;
            point.RuntimeShadowBlurSamplePreset = 2;
            point.ShadowBakeResolution = 64;
            UdonSharpEditorUtility.CopyProxyToUdon(point);
            UdonSharpEditorUtility.CopyProxyToUdon(manager);

            var managerBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            var pointBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(point);
            Assert.That(managerBacking, Is.Not.Null);
            Assert.That(pointBacking, Is.Not.Null);
            Assert.That(pointBacking.publicVariables.TrySetVariableValue("RuntimeShadowBlurSamplePreset", 0), Is.True);
            Assert.That(pointBacking.publicVariables.TrySetVariableValue("ShadowBakeResolution", 512), Is.True);
            Assert.That(managerBacking.publicVariables.TrySetVariableValue("RuntimeShadowBlurQualityPreset", 1), Is.True);

            // Represent the uninspected managed objects that still contain their old Edit Mode data.
            point.RuntimeShadowBlurSamplePreset = 2;
            point.ShadowBakeResolution = 64;
            manager.RuntimeShadowBlurQualityPreset = -1;
            LightVolumeManagerEditorBackend.CopyUdonToProxy(manager);

            Assert.That(point.RuntimeShadowBlurSamplePreset, Is.EqualTo(0));
            Assert.That(point.ShadowBakeResolution, Is.EqualTo(512));
            Assert.That(manager.RuntimeShadowBlurQualityPreset, Is.EqualTo(1));
        }

        // Explicit Play Mode re-bakes replace the live source only after BakeShadows succeeds. The
        // dependency bridge must therefore not apply the one-shot startup clear used by Bake In Game.
        [Test]
        public void ExplicitRuntimeBakeDependencyRefreshPreservesExistingShadowSource() {
            GameObject managerObject = CreateGameObject("Explicit Runtime Bake Manager", true);
            GameObject pointObject = CreateGameObject("Explicit Runtime Bake Point", true);
            LightVolumeManager manager = managerObject.AddUdonSharpComponent<LightVolumeManager>();
            PointLightVolumeInstance point = pointObject.AddUdonSharpComponent<PointLightVolumeInstance>();
            Texture2D previousShadow = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            _createdObjects.Add(previousShadow);
            manager.LightVolumeInstances = new LightVolumeInstance[0];
            manager.PointLightVolumeInstances = new[] { point };
            point.LightVolumeManager = manager;
            point.Shadows = true;
            point.BakeInGame = true;
            point.ShadowMapTexture = previousShadow;
            point.ShadowMapID = 0f;
            point.RuntimeShadowDirectOutput = true;
            UdonSharpEditorUtility.CopyProxyToUdon(manager);
            UdonSharpEditorUtility.CopyProxyToUdon(point);

            // Simulate a component first selected after Play Mode startup: its managed Manager
            // proxy no longer carries the temporary runtime resources and must self-heal at bake.
            manager.RuntimeShadowCamera = null;
            manager.RuntimeShadowDepthEncodeMaterial = null;
            manager.RuntimeShadowBlurMaterial = null;
            manager.CubemapFaceMaterial = null;
            manager.ClusteringMaterial = null;
            LightVolumePreprocessor.EnsureRuntimeDependencies(manager);
            LightVolumePreprocessor.PreparePointLightRuntimeShadowDependencies(point, manager, false);

            var managerBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            var pointBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(point);
            Assert.That(manager.RuntimeShadowCamera, Is.Not.Null);
            Assert.That(manager.RuntimeShadowDepthEncodeMaterial, Is.Not.Null);
            Assert.That(manager.RuntimeShadowBlurMaterial, Is.Not.Null);
            Assert.That(managerBacking.publicVariables.TryGetVariableValue("RuntimeShadowCamera", out object backingCamera), Is.True);
            Assert.That(backingCamera, Is.SameAs(manager.RuntimeShadowCamera));
            Assert.That(point.ShadowMapTexture, Is.SameAs(previousShadow));
            Assert.That(point.ShadowMapID, Is.EqualTo(0f));
            Assert.That(pointBacking.publicVariables.TryGetVariableValue("ShadowMapTexture", out object backingShadow), Is.True);
            Assert.That(backingShadow, Is.SameAs(previousShadow));
            Assert.That(pointBacking.publicVariables.TryGetVariableValue("ShadowMapID", out object backingShadowId), Is.True);
            Assert.That(backingShadowId, Is.EqualTo(0f));
            Assert.That(pointBacking.publicVariables.TryGetVariableValue("RuntimeShadowDirectOutput", out object backingDirectOutput), Is.True);
            Assert.That(backingDirectOutput, Is.EqualTo(false), "The Inspector one-shot bake must use a retained normal source, not direct atlas output.");
        }

        // Reproduces the complete late-selection wrapper lifecycle: Udon-to-proxy before GUI,
        // Inspector-authored popup values, proxy-to-Udon after GUI, delayed bake, then another
        // repaint. The second repaint is essential because Texture cannot read a concrete runtime
        // RenderTexture from the Udon heap without the editor bridge repairing it.
        [UnityTest]
        public IEnumerator LateSelectedInspectorPreservesPopupEditsAndRuntimeBakeAcrossRepaint() {
            GameObject managerObject = CreateGameObject("Late Runtime Bake Manager", true);
            GameObject pointObject = CreateGameObject("Late Runtime Bake Point", true);
            GameObject volumeObject = CreateGameObject("Late Runtime Bake Volume", true);
            managerObject.transform.SetAsFirstSibling();
            LightVolumeManager manager = managerObject.AddUdonSharpComponent<LightVolumeManager>();
            PointLightVolumeInstance point = pointObject.AddUdonSharpComponent<PointLightVolumeInstance>();
            LightVolumeInstance volume = volumeObject.AddUdonSharpComponent<LightVolumeInstance>();
            manager.LightVolumeInstances = new[] { volume };
            manager.PointLightVolumeInstances = new[] { point };
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            point.LightVolumeManager = manager;
            point.Shadows = true;
            point.RebakeShadows = true;
            point.IsActive = true;
            point.Color = Color.white;
            point.Intensity = 1f;
            point.LightType = 0;
            point.Blur = 0f;
            point.LayerMask = 0;
            point.ShadowBakeResolution = 16;
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowDirectOutput = false;
            volume.LightVolumeManager = manager;
            volume.IsActive = true;
            UdonSharpEditorUtility.CopyProxyToUdon(volume);
            UdonSharpEditorUtility.CopyProxyToUdon(point);
            UdonSharpEditorUtility.CopyProxyToUdon(manager);
            Selection.activeObject = null;

            yield return new EnterPlayMode();
            yield return null;

            managerObject = GameObject.Find("Late Runtime Bake Manager");
            pointObject = GameObject.Find("Late Runtime Bake Point");
            volumeObject = GameObject.Find("Late Runtime Bake Volume");
            Assert.That(managerObject, Is.Not.Null);
            Assert.That(pointObject, Is.Not.Null);
            Assert.That(volumeObject, Is.Not.Null);
            manager = managerObject.GetComponent<LightVolumeManager>();
            point = pointObject.GetComponent<PointLightVolumeInstance>();
            volume = volumeObject.GetComponent<LightVolumeInstance>();
            Assert.That(manager, Is.Not.Null);
            Assert.That(point, Is.Not.Null);
            Assert.That(volume, Is.Not.Null);

            // Construct the same wrapper Unity creates only when the component is selected after
            // Play Mode has started, and confirm it owns our actual custom editor.
            Selection.activeObject = pointObject;
            UnityEditor.Editor wrapperEditor = UnityEditor.Editor.CreateEditor(point);
            Assert.That(wrapperEditor, Is.Not.Null);
            Assert.That(wrapperEditor.GetType().Name, Is.EqualTo("UdonSharpBehaviourOverrideEditor"));
            FieldInfo userEditorField = wrapperEditor.GetType().GetField("_userEditor", _lifecycleMethodFlags);
            Assert.That(userEditorField, Is.Not.Null);
            Assert.That(userEditorField.GetValue(wrapperEditor), Is.InstanceOf<PointLightVolumeEditor>());

            // Mirror one real wrapper GUI event around the custom editor's synchronization path.
            UdonSharpEditorUtility.CopyUdonToProxy(point, ProxySerializationPolicy.All);
            LightVolumeManagerEditorBackend.SynchronizeRuntimeInspectorGraphFromUdon(point);
            point.RuntimeShadowBlurSamplePreset = 1;
            point.ShadowBakeResolution = 32;
            PointLightVolumeEditorUtility.Sync(point, false, false);
            var pointBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(point);
            Assert.That(point.RuntimeShadowBlurSamplePreset, Is.EqualTo(1), "Editor canonicalization changed the popup value before serialization.");
            Assert.That(pointBacking.GetProgramVariable("RuntimeShadowBlurSamplePreset"), Is.EqualTo(1), "Root proxy serialization did not publish the popup value.");
            Assert.That(LightVolumeManagerEditorBackend.QueueRuntimeManagerRefresh(manager), Is.True);
            UdonSharpEditorUtility.CopyProxyToUdon(point, ProxySerializationPolicy.All);
            Assert.That(pointBacking.GetProgramVariable("RuntimeShadowBlurSamplePreset"), Is.EqualTo(1), "The wrapper's final recursive copy restored a stale point value.");

            yield return null;

            var managerBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            var volumeBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(volume);
            Assert.That(pointBacking, Is.Not.Null);
            Assert.That(managerBacking, Is.Not.Null);
            Assert.That(volumeBacking, Is.Not.Null);
            Assert.That(pointBacking.GetProgramVariable("RuntimeShadowBlurSamplePreset"), Is.EqualTo(1));
            Assert.That(pointBacking.GetProgramVariable("ShadowBakeResolution"), Is.EqualTo(32));
            Assert.That(pointBacking.GetProgramVariable("RuntimeShadowResolution"), Is.EqualTo(32));

            // Repeat the wrapper ordering for representative Manager and Light Volume dropdown /
            // checkbox fields. Their runtime events are also deferred beyond final writeback.
            UdonSharpEditorUtility.CopyUdonToProxy(manager, ProxySerializationPolicy.All);
            LightVolumeManagerEditorBackend.SynchronizeRuntimeInspectorGraphFromUdon(manager);
            manager.FroxelCoarse = 8;
            manager.ForceSceneLighting = true;
            LightVolumeManagerEditorBackend.ApplySettings(manager, false, updateVolumes: false, copyProxyToUdon: false);
            LightVolumeManagerEditorBackend.ApplyRuntimeManagerSettings(manager);
            UdonSharpEditorUtility.CopyProxyToUdon(manager, ProxySerializationPolicy.All);

            UdonSharpEditorUtility.CopyUdonToProxy(volume, ProxySerializationPolicy.All);
            LightVolumeManagerEditorBackend.SynchronizeRuntimeInspectorGraphFromUdon(volume);
            volume.IsAdditive = true;
            volume.SmoothBlending = 0f;
            LightVolumeTools.ApplyRuntimeState(volume, false);
            LightVolumeManagerEditorBackend.CopyProxyToUdon(volume);
            Assert.That(LightVolumeManagerEditorBackend.QueueRuntimeManagerRefresh(manager), Is.True);
            UdonSharpEditorUtility.CopyProxyToUdon(volume, ProxySerializationPolicy.All);

            yield return null;

            Assert.That(managerBacking.GetProgramVariable("FroxelCoarse"), Is.EqualTo(8));
            Assert.That(managerBacking.GetProgramVariable("ForceSceneLighting"), Is.EqualTo(true));
            Assert.That(volumeBacking.GetProgramVariable("IsAdditive"), Is.EqualTo(true));
            Assert.That(volumeBacking.GetProgramVariable("SmoothBlending"), Is.EqualTo(0f));

            // The button now queues its event until after the wrapper's final recursive copy.
            UdonSharpEditorUtility.CopyUdonToProxy(point, ProxySerializationPolicy.All);
            LightVolumeManagerEditorBackend.SynchronizeRuntimeInspectorGraphFromUdon(point);
            PointLightVolumeEditorUtility.Sync(point, false, false);
            Assert.That(LightVolumeManagerEditorBackend.QueueRuntimeShadowBake(point), Is.True);
            UdonSharpEditorUtility.CopyProxyToUdon(point, ProxySerializationPolicy.All);
            // EditMode UnityTests resume before EditorApplication.delayCall while Play Mode is
            // nested. Invoke the exact registered flush at the same post-wrapper boundary.
            MethodInfo flushRuntimeInspectorCommands = typeof(LightVolumeManagerEditorBackend).GetMethod("FlushRuntimeInspectorCommands", _staticMigrationMethodFlags);
            Assert.That(flushRuntimeInspectorCommands, Is.Not.Null);
            flushRuntimeInspectorCommands.Invoke(null, null);

            Assert.That(pointBacking.GetProgramVariable("ShadowMapTexture"), Is.InstanceOf<RenderTexture>());
            Assert.That(pointBacking.GetProgramVariable("RuntimeShadowDirectOutput"), Is.EqualTo(false));
            Assert.That(point.RuntimeShadowTexturePreview, Is.InstanceOf<RenderTexture>(), "The exact RenderTexture-typed private owner must survive the Udon-to-proxy pull.");
            Assert.That(point.ShadowMapTexture, Is.InstanceOf<RenderTexture>(), "The base Texture-typed public source must survive the Udon-to-proxy pull.");
            Assert.That(point.RuntimeShadowTexturePreview, Is.SameAs(point.ShadowMapTexture));
            Assert.That(point.ShadowMap, Is.SameAs(point.ShadowMapTexture), "The standard Shadow Map field must contain the effective runtime source in Play Mode.");

            // This is a real Udon/VRCGraphics mismatch bake (32 source -> 16 Manager atlas), not
            // the managed C# fallback used by ordinary Edit Mode tests. With an empty culling mask
            // every cubemap face is the same far-depth value, so any dark border is introduced by
            // the runtime material-blit/resample path itself.
            RenderTexture runtimeShadowAtlas = managerBacking.GetProgramVariable("ShadowTextures") as RenderTexture;
            Assert.That(runtimeShadowAtlas, Is.Not.Null);
            Assert.That(runtimeShadowAtlas.width, Is.EqualTo(16));
            Assert.That(runtimeShadowAtlas.height, Is.EqualTo(16));
            Color[][] runtimeAtlasPixels = ReadRenderTextureArrayPixels(runtimeShadowAtlas);
            for (int face = 0; face < 6; face++) {
                Color referencePixel = runtimeAtlasPixels[face][8 * runtimeShadowAtlas.width + 8];
                for (int edge = 0; edge < runtimeShadowAtlas.width; edge++) {
                    AssertColorClose(referencePixel, runtimeAtlasPixels[face][edge], 0.001f, "Runtime mismatch bake changed bottom border on face " + face);
                    AssertColorClose(referencePixel, runtimeAtlasPixels[face][(runtimeShadowAtlas.height - 1) * runtimeShadowAtlas.width + edge], 0.001f, "Runtime mismatch bake changed top border on face " + face);
                    AssertColorClose(referencePixel, runtimeAtlasPixels[face][edge * runtimeShadowAtlas.width], 0.001f, "Runtime mismatch bake changed left border on face " + face);
                    AssertColorClose(referencePixel, runtimeAtlasPixels[face][edge * runtimeShadowAtlas.width + runtimeShadowAtlas.width - 1], 0.001f, "Runtime mismatch bake changed right border on face " + face);
                }
            }

            // Reproduce the original Ctrl+Z failure through the real Inspector Undo callback and
            // compiled Udon heap. A runtime array paired with stale native-Cubemap flags must be
            // canonicalized before any material receives it as _CubeTex.
            FieldInfo queuedShadowRefresh = typeof(LightVolumeManagerEditorBackend).GetField("_queuedRuntimeShadowTextureReinitialization", _staticMigrationMethodFlags);
            Assert.That(queuedShadowRefresh, Is.Not.Null);
            Assert.That(queuedShadowRefresh.GetValue(null), Is.EqualTo(false));
            LogAssert.NoUnexpectedReceived();
            point.ShadowMapTextureIsCubemap = true;
            point.ShadowMapTextureHasDepthSlices = false;
            LightVolumeManagerEditorBackend.CopyProxyToUdon(point);
            RenderTexture undoSource = pointBacking.GetProgramVariable("ShadowMapTexture") as RenderTexture;
            Assert.That(undoSource, Is.Not.Null);
            Assert.That(undoSource.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(undoSource.volumeDepth, Is.EqualTo(6));
            Assert.That(pointBacking.GetProgramVariable("ShadowMapTextureIsCubemap"), Is.EqualTo(true));
            Assert.That(pointBacking.GetProgramVariable("ShadowMapTextureHasDepthSlices"), Is.EqualTo(false));

            Vector3 positionBeforeUndo = point.transform.position;
            Undo.IncrementCurrentGroup();
            int moveUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Move Runtime Point Light");
            Undo.RecordObject(point.transform, "Move Runtime Point Light");
            point.transform.position += Vector3.one;
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(moveUndoGroup);
            Undo.PerformUndo();
            Assert.That(point.transform.position, Is.EqualTo(positionBeforeUndo));

            Assert.That(queuedShadowRefresh.GetValue(null), Is.EqualTo(true), "The real Undo callback did not queue a shadow-layout rebuild.");
            flushRuntimeInspectorCommands.Invoke(null, null);
            LogAssert.NoUnexpectedReceived();

            Assert.That(pointBacking.GetProgramVariable("ShadowMapTextureIsCubemap"), Is.EqualTo(false));
            Assert.That(pointBacking.GetProgramVariable("ShadowMapTextureHasDepthSlices"), Is.EqualTo(true));
            RenderTexture undoRefreshedAtlas = managerBacking.GetProgramVariable("ShadowTextures") as RenderTexture;
            Assert.That(undoRefreshedAtlas, Is.Not.Null);
            Assert.That(undoRefreshedAtlas.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(undoRefreshedAtlas.volumeDepth, Is.EqualTo(6));
            LightVolumeManagerEditorBackend.SynchronizeRuntimeInspectorGraphFromUdon(point);
            Assert.That(point.ShadowMapTextureIsCubemap, Is.False);
            Assert.That(point.ShadowMapTextureHasDepthSlices, Is.True);
            Undo.ClearUndo(point.transform);

            // Simulate the next Inspector repaint. UdonSharp reads the recursive graph again before
            // our custom editor and writes it back afterwards.
            UdonSharpEditorUtility.CopyUdonToProxy(point, ProxySerializationPolicy.All);
            LightVolumeManagerEditorBackend.SynchronizeRuntimeInspectorGraphFromUdon(point);
            UdonSharpEditorUtility.CopyProxyToUdon(point, ProxySerializationPolicy.All);
            Assert.That(pointBacking.GetProgramVariable("ShadowMapTexture"), Is.InstanceOf<RenderTexture>());
            Assert.That(pointBacking.GetProgramVariable("RuntimeShadowBlurSamplePreset"), Is.EqualTo(1));
            Assert.That(pointBacking.GetProgramVariable("ShadowBakeResolution"), Is.EqualTo(32));
            Assert.That(point.ShadowMapTexture, Is.SameAs(point.RuntimeShadowTexturePreview));

            // Selecting either referenced root also makes UdonSharp recurse through the Manager's
            // point registry. Both custom inspectors must repair that graph before final writeback.
            UdonSharpEditorUtility.CopyUdonToProxy(manager, ProxySerializationPolicy.All);
            LightVolumeManagerEditorBackend.SynchronizeRuntimeInspectorGraphFromUdon(manager);
            UdonSharpEditorUtility.CopyProxyToUdon(manager, ProxySerializationPolicy.All);
            Assert.That(pointBacking.GetProgramVariable("ShadowMapTexture"), Is.InstanceOf<RenderTexture>());

            UdonSharpEditorUtility.CopyUdonToProxy(volume, ProxySerializationPolicy.All);
            LightVolumeManagerEditorBackend.SynchronizeRuntimeInspectorGraphFromUdon(volume);
            UdonSharpEditorUtility.CopyProxyToUdon(volume, ProxySerializationPolicy.All);
            Assert.That(pointBacking.GetProgramVariable("ShadowMapTexture"), Is.InstanceOf<RenderTexture>());

            // The standard active Shadow Map field and Clear Shadows button operate on the same
            // Play Mode proxy value. Clearing it must release the retained runtime source and
            // remove it from the live Udon heap instead of restoring it on the next repaint.
            UdonSharpEditorUtility.CopyUdonToProxy(point, ProxySerializationPolicy.All);
            LightVolumeManagerEditorBackend.SynchronizeRuntimeInspectorGraphFromUdon(point);
            Assert.That(point.ShadowMap, Is.InstanceOf<RenderTexture>());
            point.ShadowMap = null;
            int clearChanges = PointLightVolumeEditorUtility.Sync(point, false, false);
            Assert.That(clearChanges & PointLightVolumeEditorUtility.ShadowTexturesChanged, Is.Not.Zero);
            LightVolumeManagerEditorBackend.ReinitializeShadowTextures(manager);
            UdonSharpEditorUtility.CopyProxyToUdon(point, ProxySerializationPolicy.All);
            flushRuntimeInspectorCommands.Invoke(null, null);
            Assert.That(point.RuntimeShadowTexturePreview, Is.Null);
            Assert.That(point.RuntimeShadowSourceInitializedPreview, Is.False);
            Assert.That(pointBacking.GetProgramVariable("ShadowMap"), Is.Null);
            Assert.That(pointBacking.GetProgramVariable("ShadowMapTexture"), Is.Null);

            // Direct output exposes the Manager atlas through the same Shadow Map field. Disabling
            // shadows must remove that bridge and invalidate the layout instead of retaining the
            // atlas as this light's next input source.
            RenderTexture directAtlas = CreateRenderTexture("Runtime Direct Shadow Atlas", 16, 16, 6, TextureDimension.Tex2DArray, RenderTextureFormat.ARGBFloat);
            managerBacking.SetProgramVariable("ShadowTextures", directAtlas);
            pointBacking.SetProgramVariable("Shadows", true);
            pointBacking.SetProgramVariable("RuntimeShadowDirectOutput", true);
            pointBacking.SetProgramVariable("_runtimeShadowSourceInitialized", true);
            LightVolumeManagerEditorBackend.SynchronizeRuntimeInspectorGraphFromUdon(point);
            Assert.That(point.ShadowMap, Is.SameAs(directAtlas));

            point.Shadows = false;
            Assert.That(point.HasEditorShadowTextureChanges(), Is.True);
            int directClearChanges = PointLightVolumeEditorUtility.Sync(point, false, false);
            Assert.That(directClearChanges & PointLightVolumeEditorUtility.ShadowTexturesChanged, Is.Not.Zero);
            Assert.That(point.ShadowMap, Is.Null);
            Assert.That(point.RuntimeShadowSourceInitializedPreview, Is.False);
            Assert.That(pointBacking.GetProgramVariable("ShadowMap"), Is.Null);
            Assert.That(pointBacking.GetProgramVariable("_runtimeShadowSourceInitialized"), Is.EqualTo(false));
            LightVolumeManagerEditorBackend.ReinitializeShadowTextures(manager);
            flushRuntimeInspectorCommands.Invoke(null, null);
            Assert.That(managerBacking.GetProgramVariable("ShadowTextures"), Is.Null);

            UnityEngine.Object.DestroyImmediate(wrapperEditor);
            Selection.activeObject = null;

            // VRChat's editor Udon runtime can report its primitive pool and UdonManager while an
            // EditMode UnityTest closes the temporary Play scene in batch mode. It is unrelated to
            // the shadow result and is not emitted consistently when this test runs in a group.
            LogAssert.ignoreFailingMessages = true;
            yield return new ExitPlayMode();
            LogAssert.ignoreFailingMessages = false;

            // Enter/Exit Play Mode restores fresh scene-object instances. The references captured
            // in _createdObjects can therefore point at the discarded Play Mode copies and cannot
            // remove the restored fixtures during TearDown.
            DestroyTestObject(GameObject.Find("Late Runtime Bake Volume"));
            DestroyTestObject(GameObject.Find("Late Runtime Bake Point"));
            DestroyTestObject(GameObject.Find("Late Runtime Bake Manager"));
        }

        // Build canonicalization copies the point-light proxy immediately after this method. Its
        // activity mirror must therefore describe the hierarchy even when manager notification is suppressed.
        [Test]
        public void PointLightEditorCanonicalizationRefreshesActivityWithoutManagerNotification() {
            GameObject inactiveRoot = CreateGameObject("Inactive Point Canonicalization Root", false);
            PointLightVolumeInstance point = CreatePointLight(null, "Canonicalized Inactive Point", true);
            point.transform.SetParent(inactiveRoot.transform, false);
            point.IsActive = true;

            point.EditorApplyAuthoringData(false, false, false);

            Assert.That(point.IsActive, Is.False);

            inactiveRoot.SetActive(true);
            point.IsActive = false;
            point.EditorApplyAuthoringData(false, false, false);

            Assert.That(point.IsActive, Is.True);

            point.Intensity = 0f;
            point.EditorApplyAuthoringData(false, false, false);

            Assert.That(point.IsActive, Is.False);
        }

        // The Manager's one-time enable reconciliation repairs serialized active state for objects
        // below an inactive parent and invalidates their shadow cache.
        [Test]
        public void ManagerEnableReconciliationRepairsInactivePointState() {
            LightVolumeManager manager = CreateManager("Serialized Activity Manager", true);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;
            GameObject hierarchyRoot = CreateGameObject("Serialized Activity Root", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Serialized Activity Point", true);
            ConfigureShadowTexture(point, CreateCubemap("Serialized Activity Shadow"), false, true, false);
            point.transform.SetParent(hierarchyRoot.transform, false);
            manager.PointLightVolumeInstances = new[] { point };
            point.IsActive = true;

            MethodInfo reconcile = typeof(LightVolumeManager).GetMethod("ReconcileRegistryActiveStates", _lifecycleMethodFlags);
            FieldInfo shadowInitialized = typeof(LightVolumeManager).GetField("_shadowTexturesInitialized", _lifecycleMethodFlags);
            Assert.That(reconcile, Is.Not.Null);
            Assert.That(shadowInitialized, Is.Not.Null);
            shadowInitialized.SetValue(manager, true);

            reconcile.Invoke(manager, null);

            Assert.That(point.IsActive, Is.False);
            Assert.That((bool)shadowInitialized.GetValue(manager), Is.False);
        }

        // Verifies inactive, black, and zero-intensity entries are removed from the final shader-visible arrays.
        [Test]
        public void DisabledAndZeroBrightnessInstancesAreExcludedFromFinalGlobals() {
            LightVolumeManager manager = CreateManager("Filtering Manager", true);
            LightVolumeInstance validVolume = CreateLightVolume(manager, "Valid Volume", true);
            LightVolumeInstance inactiveVolume = CreateLightVolume(manager, "Inactive Volume", false);
            LightVolumeInstance blackVolume = CreateLightVolume(manager, "Black Volume", true);
            LightVolumeInstance zeroVolume = CreateLightVolume(manager, "Zero Volume", true);
            ConfigureLightVolume(validVolume, new Color(0.2f, 0.4f, 0.8f, 1), 2, false, 0.25f);
            ConfigureLightVolume(blackVolume, Color.black, 1, false, 0.5f);
            ConfigureLightVolume(zeroVolume, Color.white, 0, false, 0.75f);

            PointLightVolumeInstance validPoint = CreatePointLight(manager, "Valid Point", true);
            PointLightVolumeInstance inactivePoint = CreatePointLight(manager, "Inactive Point", false);
            PointLightVolumeInstance blackPoint = CreatePointLight(manager, "Black Point", true);
            PointLightVolumeInstance zeroPoint = CreatePointLight(manager, "Zero Point", true);
            validPoint.Color = new Color(1, 0.5f, 0.25f, 1);
            validPoint.Intensity = 3;
            SetPointLightSquaredSize(validPoint, 4);
            validPoint.SetPointLight();
            blackPoint.Color = Color.black;
            zeroPoint.Intensity = 0;
            manager.LightVolumeInstances = new[] { blackVolume, inactiveVolume, validVolume, zeroVolume };
            manager.PointLightVolumeInstances = new[] { zeroPoint, validPoint, inactivePoint, blackPoint };

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 1);
            AssertGlobalFloat(_pointLightCountID, 1);
            AssertVectorClose(ExpectedLightVolumeColor(validVolume), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);
            AssertVectorClose(ExpectedPointLightColor(validPoint), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            validVolume.Intensity = 0;
            validPoint.Intensity = 0;

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 0);
            AssertGlobalFloat(_lightVolumeCountID, 0);
            AssertGlobalFloat(_pointLightCountID, 0);
        }

        // Checks shader globals for volume order, color/intensity changes, movement, UVW data, and additive counters.
        [Test]
        public void LightVolumeGlobalsFollowOrderMovementAndParameterChanges() {
            LightVolumeManager manager = CreateManager("Volume Globals Manager", true);
            manager.LightProbesBlending = false;
            manager.SharpBounds = false;
            manager.AdditiveMaxOverdraw = 2;

            LightVolumeInstance first = CreateLightVolume(manager, "First Volume", true);
            LightVolumeInstance second = CreateLightVolume(manager, "Second Volume", true);
            ConfigureLightVolume(first, new Color(0.25f, 0.5f, 0.75f, 1), 1.5f, false, 0.1f);
            ConfigureLightVolume(second, new Color(1, 0.2f, 0.05f, 1), 0.75f, true, 0.5f);
            first.transform.position = new Vector3(-1, 0.5f, 2);
            first.transform.localScale = new Vector3(1, 2, 3);
            second.transform.position = new Vector3(2, 3, 4);
            second.transform.rotation = Quaternion.Euler(0, 45, 0);
            second.transform.localScale = new Vector3(2, 3, 4);
            second.InvBakedRotation = Quaternion.Euler(0, 15, 0);
            manager.LightVolumeInstances = new[] { second, first };

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 2);
            AssertGlobalFloat(_lightVolumeAdditiveCountID, 1);
            AssertGlobalFloat(_lightVolumeOcclusionCountID, 0);
            AssertGlobalFloat(_lightVolumeProbesBlendID, 0);
            AssertGlobalFloat(_lightVolumeSharpBoundsID, 0);
            AssertGlobalFloat(_lightVolumeAdditiveMaxOverdrawID, 2);
            AssertVectorClose(ExpectedLightVolumeColor(second), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);
            AssertVectorClose(ExpectedLightVolumeColor(first), Shader.GetGlobalVectorArray(_lightVolumeColorID)[1]);
            AssertVectorClose(second.BoundsUvwMin0, Shader.GetGlobalVectorArray(_lightVolumeUvwScaleID)[0]);
            AssertVectorClose(second.BoundsUvwMin1, Shader.GetGlobalVectorArray(_lightVolumeUvwScaleID)[1]);
            AssertVectorClose(second.BoundsUvwMin2, Shader.GetGlobalVectorArray(_lightVolumeUvwScaleID)[2]);
            Vector4[] expandedUvw = Shader.GetGlobalVectorArray(_lightVolumeUvwID);
            AssertVectorClose(ExpectedExpandedLightVolumeUvw(second, 0, false), expandedUvw[0]);
            AssertVectorClose(ExpectedExpandedLightVolumeUvw(second, 0, true), expandedUvw[1]);
            AssertVectorClose(ExpectedExpandedLightVolumeUvw(second, 1, false), expandedUvw[2]);
            AssertVectorClose(ExpectedExpandedLightVolumeUvw(second, 1, true), expandedUvw[3]);
            AssertVectorClose(ExpectedExpandedLightVolumeUvw(second, 2, false), expandedUvw[4]);
            AssertVectorClose(ExpectedExpandedLightVolumeUvw(second, 2, true), expandedUvw[5]);
            AssertVectorClose(second.RelativeRotationRow0, Shader.GetGlobalVectorArray(_lightVolumeRotationID)[0]);
            AssertVectorClose(second.RelativeRotationRow1, Shader.GetGlobalVectorArray(_lightVolumeRotationID)[1]);
            AssertMatrixClose(Matrix4x4.TRS(second.transform.position, second.transform.rotation, second.transform.lossyScale).inverse, Shader.GetGlobalMatrixArray(_lightVolumeInvWorldMatrixID)[0]);

            second.Intensity = 0;
            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeCountID, 1);
            AssertGlobalFloat(_lightVolumeAdditiveCountID, 0);
            AssertVectorClose(ExpectedLightVolumeColor(first), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);

            second.Intensity = 0.75f;
            first.SetSmoothBlending(0.5f);
            first.Color = Color.green;
            first.Intensity = 2;
            first.IsAdditive = true;
            first.transform.position = new Vector3(3, 4, 5);
            manager.LightVolumeInstances = new[] { first, second };

            manager.UpdateVolumes();

            Vector3 expectedSmooth = first.transform.lossyScale / 0.5f;
            AssertGlobalFloat(_lightVolumeCountID, 2);
            AssertGlobalFloat(_lightVolumeAdditiveCountID, 2);
            AssertVectorClose(ExpectedLightVolumeColor(first), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);
            AssertVectorClose(new Vector4(expectedSmooth.x, expectedSmooth.y, expectedSmooth.z, 0), Shader.GetGlobalVectorArray(_lightVolumeInvLocalEdgeSmoothID)[0]);
            AssertMatrixClose(Matrix4x4.TRS(first.transform.position, first.transform.rotation, first.transform.lossyScale).inverse, Shader.GetGlobalMatrixArray(_lightVolumeInvWorldMatrixID)[0]);
        }

        // Verifies additive volumes are compacted into leading shader slots even when serialized registry order is stale.
        [Test]
        public void AdditiveVolumesOccupyLeadingShaderSlotsWhenRegistryOrderIsStale() {
            LightVolumeManager manager = CreateManager("Stale Additive Order Manager", true);
            LightVolumeInstance regular = CreateLightVolume(manager, "Regular Ordered First", true);
            LightVolumeInstance additive = CreateLightVolume(manager, "Additive Ordered Second", true);
            ConfigureLightVolume(regular, new Color(0.1f, 0.4f, 0.8f, 1), 1, false, 0.15f);
            ConfigureLightVolume(additive, new Color(1, 0.2f, 0.05f, 1), 2, true, 0.55f);
            regular.transform.position = new Vector3(-2, 1, 3);
            regular.transform.localScale = new Vector3(1, 2, 3);
            additive.transform.position = new Vector3(4, 5, 6);
            additive.transform.rotation = Quaternion.Euler(0, 45, 0);
            additive.transform.localScale = new Vector3(2, 3, 4);
            manager.LightVolumeInstances = new[] { regular, additive };

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeCountID, 2);
            AssertGlobalFloat(_lightVolumeAdditiveCountID, 1);
            AssertVectorClose(ExpectedLightVolumeColor(additive), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);
            AssertVectorClose(ExpectedLightVolumeColor(regular), Shader.GetGlobalVectorArray(_lightVolumeColorID)[1]);
            AssertVectorClose(additive.BoundsUvwMin0, Shader.GetGlobalVectorArray(_lightVolumeUvwScaleID)[0]);
            AssertMatrixClose(Matrix4x4.TRS(additive.transform.position, additive.transform.rotation, additive.transform.lossyScale).inverse, Shader.GetGlobalMatrixArray(_lightVolumeInvWorldMatrixID)[0]);
        }

        // Verifies the batched public setters update their mirrors and reach the manager-owned shader buffers.
        [Test]
        public void BatchColorAndIntensitySettersUpdateInstancesAndShaderGlobals() {
            LightVolumeManager manager = CreateManager("Batch Color Manager", true);
            LightVolumeInstance leadingVolume = CreateLightVolume(manager, "Leading Batch Volume", true);
            LightVolumeInstance volume = CreateLightVolume(manager, "Batch Color Volume", true);
            PointLightVolumeInstance leadingPoint = CreatePointLight(manager, "Leading Batch Point", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Batch Color Point", true);
            manager.LightVolumeInstances = new[] { leadingVolume, volume };
            manager.PointLightVolumeInstances = new[] { leadingPoint, point };
            manager.UpdateVolumes();

            Color volumeColor = new Color(0.15f, 0.4f, 0.8f, 0.75f);
            float volumeIntensity = 2.25f;
            volume.SetColorAndIntensity(volumeColor, volumeIntensity);

            Assert.That(volume.Color, Is.EqualTo(volumeColor));
            Assert.That(volume.Intensity, Is.EqualTo(volumeIntensity).Within(Epsilon));
            AssertVectorClose(ExpectedLightVolumeColor(volume), Shader.GetGlobalVectorArray(_lightVolumeColorID)[1]);

            Color pointColor = new Color(0.9f, 0.3f, 0.1f, 0.5f);
            float pointIntensity = 1.75f;
            point.SetColorAndIntensity(pointColor, pointIntensity);

            Assert.That(point.Color, Is.EqualTo(pointColor));
            Assert.That(point.Intensity, Is.EqualTo(pointIntensity).Within(Epsilon));
            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[1]);
        }

        // The measured regular color path resolves the compact additive ordering and rewrites no non-color data.
        [Test]
        public void NarrowLightVolumeColorNotificationChangesOnlyResolvedColorSlot() {
            LightVolumeManager manager = CreateManager("Narrow Regular Color Manager", true);
            LightVolumeInstance regular = CreateLightVolume(manager, "Narrow Regular Color Volume", true);
            LightVolumeInstance additive = CreateLightVolume(manager, "Narrow Additive Color Volume", true);
            ConfigureLightVolume(regular, new Color(0.2f, 0.4f, 0.8f, 1f), 1.25f, false, 0.1f);
            ConfigureLightVolume(additive, new Color(0.9f, 0.25f, 0.1f, 1f), 0.75f, true, 0.4f);
            manager.LightVolumeInstances = new[] { regular, additive };
            manager.UpdateVolumes();

            Assert.That(manager.EnabledIDs[0], Is.EqualTo(1), "Additive volume must own compact slot zero.");
            Assert.That(manager.EnabledIDs[1], Is.EqualTo(0), "Regular volume must resolve through the compact registry map.");
            Assert.That(GetManagerField<bool>(manager, _lightVolumeArraysDirtyField), Is.False);

            Vector4[] colors = GetManagerField<Vector4[]>(manager, _lightVolumeColorsField);
            Vector4[] colorsBefore = (Vector4[])colors.Clone();
            Matrix4x4[] matricesBefore = (Matrix4x4[])GetManagerField<Matrix4x4[]>(manager, _lightVolumeInvWorldMatricesField).Clone();
            Vector4[] edgeBefore = (Vector4[])GetManagerField<Vector4[]>(manager, _lightVolumeInvLocalEdgeSmoothField).Clone();
            Vector4[] uvwScaleBefore = (Vector4[])GetManagerField<Vector4[]>(manager, _lightVolumeBoundsUvwScaleField).Clone();
            Vector4[] uvwBefore = (Vector4[])GetManagerField<Vector4[]>(manager, _lightVolumeBoundsUvwField).Clone();
            Vector4[] rotationBefore = (Vector4[])GetManagerField<Vector4[]>(manager, _lightVolumeRelativeRotationField).Clone();

            regular.Color = new Color(0.15f, 0.7f, 0.35f, 0.6f);
            regular.Intensity = 2.5f;
            regular.IsRotated = true;
            SetManagerField(manager, _isUpdatingVolumesField, true);
            manager.NotifyLightVolumeColorChanged(regular);

            Assert.That(GetManagerField<Vector4[]>(manager, _lightVolumeColorsField), Is.SameAs(colors));
            AssertVectorClose(colorsBefore[0], colors[0]);
            AssertVectorClose(ExpectedLightVolumeColor(regular), colors[1]);
            for (int i = 2; i < colors.Length; i++) AssertVectorClose(colorsBefore[i], colors[i]);
            CollectionAssert.AreEqual(matricesBefore, GetManagerField<Matrix4x4[]>(manager, _lightVolumeInvWorldMatricesField));
            CollectionAssert.AreEqual(edgeBefore, GetManagerField<Vector4[]>(manager, _lightVolumeInvLocalEdgeSmoothField));
            CollectionAssert.AreEqual(uvwScaleBefore, GetManagerField<Vector4[]>(manager, _lightVolumeBoundsUvwScaleField));
            CollectionAssert.AreEqual(uvwBefore, GetManagerField<Vector4[]>(manager, _lightVolumeBoundsUvwField));
            CollectionAssert.AreEqual(rotationBefore, GetManagerField<Vector4[]>(manager, _lightVolumeRelativeRotationField));
            Assert.That(GetManagerField<bool>(manager, _lightVolumeArraysDirtyField), Is.True,
                "The narrow pack keeps the established conservative six-array upload contract.");
            Assert.That(GetManagerField<bool>(manager, _volumeDataUpdateRequestedField), Is.False,
                "A stable active compact slot must not request a structural rebuild.");

            SetManagerField(manager, _isUpdatingVolumesField, false);
            Assert.That(_updateDynamicVolumeTransformsMethod, Is.Not.Null);
            _updateDynamicVolumeTransformsMethod.Invoke(manager, null);
            Assert.That(GetManagerField<bool>(manager, _lightVolumeArraysDirtyField), Is.False);
            AssertVectorClose(colorsBefore[0], Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);
            AssertVectorClose(ExpectedLightVolumeColor(regular), Shader.GetGlobalVectorArray(_lightVolumeColorID)[1]);
        }

        // A color/intensity transition that changes membership must rebuild compact ordering instead of patching a stale slot.
        [Test]
        public void NarrowLightVolumeColorNotificationRebuildsOnActivityMismatch() {
            LightVolumeManager manager = CreateManager("Narrow Regular Activity Manager", true);
            LightVolumeInstance volume = CreateLightVolume(manager, "Narrow Regular Activity Volume", true);
            manager.LightVolumeInstances = new[] { volume };
            manager.UpdateVolumes();

            Assert.That(manager.EnabledCount, Is.EqualTo(1));
            AssertGlobalFloat(_lightVolumeCountID, 1);
            volume.Color = Color.black;
            volume.IsActive = false;

            manager.NotifyLightVolumeColorChanged(volume);

            Assert.That(manager.EnabledCount, Is.Zero);
            AssertGlobalFloat(_lightVolumeCountID, 0);
            Assert.That(GetManagerField<bool>(manager, _lightVolumeArraysDirtyField), Is.False,
                "A structural rebuild must consume the change instead of leaving a stale direct-upload flag.");
        }

        // The measured source-local fast path must publish the exact historical synchronous range.
        [Test]
        public void BasicPointColorSettersPublishExactSourceLocalRange() {
            LightVolumeManager manager = CreateManager("Source Local Range Manager", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Source Local Range Point", true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.LightsBrightnessCutoff = 0.287f;
            manager.UpdateVolumes();

            point.SquaredScale = 2.25f;
            point.LightSourceSize = 0.4f;
            SetManagerField(manager, _isUpdatingVolumesField, true);

            point.SetColor(new Color(0.85f, 0.3f, 0.12f, 1f));
            Assert.That(point.SquaredRange, Is.EqualTo(ExpectedBasicPointSquaredRange(point, manager.LightsBrightnessCutoff)).Within(Epsilon));
            Assert.That(point.IsRangeDirty, Is.False);

            point.SetIntensity(2.75f);
            Assert.That(point.SquaredRange, Is.EqualTo(ExpectedBasicPointSquaredRange(point, manager.LightsBrightnessCutoff)).Within(Epsilon));
            Assert.That(point.IsRangeDirty, Is.False);

            point.SetColorAndIntensity(new Color(0.15f, 0.65f, 0.45f, 1f), 1.8f);
            Assert.That(point.SquaredRange, Is.EqualTo(ExpectedBasicPointSquaredRange(point, manager.LightsBrightnessCutoff)).Within(Epsilon));
            Assert.That(point.IsRangeDirty, Is.False);

            point.SetIntensity(0f);
            Assert.That(point.IsRangeDirty, Is.True,
                "Active-state transitions must stay on the manager's structural fallback path.");
            SetManagerField(manager, _isUpdatingVolumesField, false);
        }

        // Registration can precede the first compact-buffer build; structural dirty semantics survive local math.
        [Test]
        public void SourceLocalRangeSurvivesRegistrationBeforeFirstCompactBuild() {
            LightVolumeManager manager = CreateManager("Precompact Source Range Manager", true);
            manager.LightsBrightnessCutoff = 0.287f;
            SetManagerField(manager, _isUpdatingVolumesField, true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Precompact Source Range Point", true);
            // Plain UdonSharp proxies do not receive OnEnable automatically in Edit Mode. The helper
            // inserts the source into the manager registry, but only the real lifecycle callback sets
            // the source-side registration flag used by the measured fast-path guard.
            InvokeLifecycleMethod(point, "OnEnable");
            point.transform.localScale = Vector3.one * 1.25f;
            point.SquaredScale = 1.5625f;
            point.LightSourceSize = 0.35f;

            Assert.That(point.RegisteredWithManagerPreview, Is.True);
            point.SetColorAndIntensity(new Color(0.7f, 0.25f, 0.5f, 1f), 2.2f);

            float expectedRange = ExpectedBasicPointSquaredRange(point, manager.LightsBrightnessCutoff);
            Assert.That(point.SquaredRange, Is.EqualTo(expectedRange).Within(Epsilon));
            Assert.That(point.IsRangeDirty, Is.True,
                "A registered source without a compact slot must remain dirty until the structural rebuild.");

            SetManagerField(manager, _isUpdatingVolumesField, false);
            manager.UpdateVolumes();
            Assert.That(point.IsRangeDirty, Is.False);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].z, Is.EqualTo(expectedRange).Within(Epsilon));
        }

        // Profiles not represented by the measured narrow pack keep their canonical manager/full fallback.
        [Test]
        public void SourceLocalRangeDoesNotConsumeUnsupportedPrecompactProfiles() {
            LightVolumeManager manager = CreateManager("Unsupported Source Range Manager", true);
            SetManagerField(manager, _isUpdatingVolumesField, true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Unsupported Source Range Point", true);
            InvokeLifecycleMethod(point, "OnEnable");
            Assert.That(point.RegisteredWithManagerPreview, Is.True);

            point.LightType = 1; // Spot
            point.ProjectionMode = 0;
            point.IsRangeDirty = false;
            point.SetColor(new Color(0.8f, 0.2f, 0.1f, 1f));
            Assert.That(point.IsRangeDirty, Is.True);

            point.LightType = 2; // Area
            point.IsRangeDirty = false;
            point.SetColor(new Color(0.7f, 0.3f, 0.15f, 1f));
            Assert.That(point.IsRangeDirty, Is.True);

            point.LightType = 0;
            point.ProjectionMode = 1; // LUT
            point.IsRangeDirty = false;
            point.SetColor(new Color(0.6f, 0.4f, 0.2f, 1f));
            Assert.That(point.IsRangeDirty, Is.True);

            point.ProjectionMode = 2; // Custom
            point.IsRangeDirty = false;
            point.SetColor(new Color(0.5f, 0.45f, 0.25f, 1f));
            Assert.That(point.IsRangeDirty, Is.True);

            point.ProjectionMode = 0;
            point.ShadowMapID = 0f; // Active/pending shadow source
            point.IsRangeDirty = false;
            point.SetColor(new Color(0.4f, 0.5f, 0.3f, 1f));
            Assert.That(point.IsRangeDirty, Is.True);
            SetManagerField(manager, _isUpdatingVolumesField, false);
        }

        // Keep current pull semantics: a raw write after callback changes final color, not its synchronous range.
        [Test]
        public void QueuedPointPullObservesPostCallbackRawIntensityWrite() {
            LightVolumeManager manager = CreateManager("Post Callback Pull Manager", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Post Callback Pull Point", true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            SetManagerField(manager, _isUpdatingVolumesField, true);
            point.SetColor(new Color(0.25f, 0.75f, 0.4f, 1f));
            float callbackRange = point.SquaredRange;
            point.Intensity = 4.5f;
            SetManagerField(manager, _isUpdatingVolumesField, false);

            _updateDynamicVolumeTransformsMethod.Invoke(manager, null);

            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            Color expectedExtra = point.Color.linear * point.Intensity;
            AssertVectorClose(new Vector4(expectedExtra.r, expectedExtra.g, expectedExtra.b, 0f),
                Shader.GetGlobalVectorArray(_pointLightExtraDataID)[0]);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].z, Is.EqualTo(callbackRange).Within(Epsilon));
            Assert.That(point.SquaredRange, Is.EqualTo(callbackRange).Within(Epsilon));
        }

        // Validated reverse hints accelerate the common lookup without becoming a correctness oracle.
        [Test]
        public void PointLightReverseMapRebuildsAndFallsBackWhenHintIsStale() {
            LightVolumeManager manager = CreateManager("Point Reverse Map Manager", true);
            PointLightVolumeInstance first = CreatePointLight(manager, "Point Reverse First", true);
            PointLightVolumeInstance second = CreatePointLight(manager, "Point Reverse Second", true);
            PointLightVolumeInstance third = CreatePointLight(manager, "Point Reverse Third", true);
            manager.PointLightVolumeInstances = new[] { first, second, third };
            manager.UpdateVolumes();

            Assert.That(_pointLightRegistryToShaderIndexField, Is.Not.Null);
            Assert.That(_findPointLightFinalIndexMethod, Is.Not.Null);
            int[] reverseMap = GetManagerField<int[]>(manager, _pointLightRegistryToShaderIndexField);
            Assert.That(reverseMap.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(reverseMap[0], Is.Zero);
            Assert.That(reverseMap[1], Is.EqualTo(1));
            Assert.That(reverseMap[2], Is.EqualTo(2));
            Assert.That((int)_findPointLightFinalIndexMethod.Invoke(manager, new object[] { 2 }), Is.EqualTo(2));

            reverseMap[2] = 0;
            Assert.That((int)_findPointLightFinalIndexMethod.Invoke(manager, new object[] { 2 }), Is.EqualTo(2));
            Assert.That(reverseMap[2], Is.EqualTo(2), "Fallback lookup should repair a stale hint.");
        }

        // Structural shrink/regrow cycles reuse reverse-map capacity while every live prefix entry is rebuilt.
        [Test]
        public void PointLightReverseMapDoesNotReallocateWithinItsHighWaterMark() {
            LightVolumeManager manager = CreateManager("Point Reverse Map Capacity Manager", true);
            PointLightVolumeInstance first = CreatePointLight(manager, "Point Reverse Capacity First", true);
            PointLightVolumeInstance second = CreatePointLight(manager, "Point Reverse Capacity Second", true);
            PointLightVolumeInstance third = CreatePointLight(manager, "Point Reverse Capacity Third", true);
            manager.PointLightVolumeInstances = new[] { first, second, third };
            manager.UpdateVolumes();
            int[] capacity = GetManagerField<int[]>(manager, _pointLightRegistryToShaderIndexField);

            manager.PointLightVolumeInstances = new[] { first };
            manager.UpdateVolumes();
            Assert.That(GetManagerField<int[]>(manager, _pointLightRegistryToShaderIndexField), Is.SameAs(capacity));
            Assert.That(capacity[0], Is.Zero);

            manager.PointLightVolumeInstances = new[] { first, second, third };
            manager.UpdateVolumes();
            Assert.That(GetManagerField<int[]>(manager, _pointLightRegistryToShaderIndexField), Is.SameAs(capacity));
            Assert.That(capacity[0], Is.Zero);
            Assert.That(capacity[1], Is.EqualTo(1));
            Assert.That(capacity[2], Is.EqualTo(2));
        }

        // Two setters for one slot should produce one final-state pack and one upload group.
        [Test]
        public void PointLightNotificationsCoalesceAndPublishFinalBasicColorRangeState() {
            LightVolumeManager manager = CreateManager("Point Coalescing Manager", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Point Coalescing Light", true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            Assert.That(_dirtyPointLightCountField, Is.Not.Null);
            SetManagerField(manager, _isUpdatingVolumesField, true);
            point.Color = new Color(0.2f, 0.65f, 0.9f, 1f);
            point.IsRangeDirty = true;
            manager.NotifyPointLightColorRangeChanged(point);
            Assert.That(point.IsRangeDirty, Is.False);
            point.Intensity = 4.25f;
            point.IsRangeDirty = true;
            manager.NotifyPointLightColorRangeChanged(point);
            Assert.That(point.IsRangeDirty, Is.False);
            SetManagerField(manager, _isUpdatingVolumesField, false);

            Assert.That(GetManagerField<int>(manager, _dirtyPointLightCountField), Is.EqualTo(1));
            _updateDynamicVolumeTransformsMethod.Invoke(manager, null);
            Assert.That(GetManagerField<int>(manager, _dirtyPointLightCountField), Is.Zero);
            Assert.That(GetManagerField<int[]>(manager, _dirtyPointLightUpdateFlagsField)[0], Is.Zero);
            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            // The update mask is also the queue-membership marker. Clearing it during flush must
            // allow the same compact slot to be queued again on the next frame.
            point.Color = new Color(0.8f, 0.25f, 0.45f, 1f);
            point.IsRangeDirty = true;
            SetManagerField(manager, _isUpdatingVolumesField, true);
            manager.NotifyPointLightColorRangeChanged(point);
            SetManagerField(manager, _isUpdatingVolumesField, false);
            Assert.That(GetManagerField<int>(manager, _dirtyPointLightCountField), Is.EqualTo(1));

            // A structural rebuild resets the same dual-purpose masks and must not make the slot
            // look permanently queued afterwards.
            manager.UpdateVolumes();
            Assert.That(GetManagerField<int>(manager, _dirtyPointLightCountField), Is.Zero);
            Assert.That(GetManagerField<int[]>(manager, _dirtyPointLightUpdateFlagsField)[0], Is.Zero);
            SetManagerField(manager, _isUpdatingVolumesField, true);
            manager.NotifyPointLightColorRangeChanged(point);
            SetManagerField(manager, _isUpdatingVolumesField, false);
            Assert.That(GetManagerField<int>(manager, _dirtyPointLightCountField), Is.EqualTo(1));
            _updateDynamicVolumeTransformsMethod.Invoke(manager, null);
            Assert.That(GetManagerField<int[]>(manager, _dirtyPointLightUpdateFlagsField)[0], Is.Zero);

            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].z, Is.EqualTo(point.SquaredRange).Within(Epsilon));
        }

        // A shadow elsewhere in the scene must not turn an unrelated basic Point color change into
        // a position, direction, or shadow-array upload.
        [Test]
        public void BasicPointColorRangeChangeUploadsOnlyChangedArraysWhenAnotherLightHasShadow() {
            LightVolumeManager manager = CreateManager("Selective Point Upload Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;
            PointLightVolumeInstance basic = CreatePointLight(manager, "Selective Basic Point", true);
            PointLightVolumeInstance shadowed = CreatePointLight(manager, "Selective Shadowed Point", true);
            ConfigureShadowTexture(shadowed, CreateCubemap("Selective Shadow Source"), false, true, false);
            manager.PointLightVolumeInstances = new[] { basic, shadowed };
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(_flushPendingPointLightChangesMethod, Is.Not.Null);
            basic.Color = new Color(0.2f, 0.65f, 0.9f, 1f);
            basic.Intensity = 4.25f;
            basic.IsRangeDirty = true;
            SetManagerField(manager, _isUpdatingVolumesField, true);
            manager.NotifyPointLightColorRangeChanged(basic);
            SetManagerField(manager, _isUpdatingVolumesField, false);

            Assert.That((bool)_flushPendingPointLightChangesMethod.Invoke(manager, null), Is.True);
            int uploadMask = GetManagerField<int>(manager, _pointLightArrayUploadMaskField);
            Assert.That(uploadMask, Is.EqualTo(PointLightUploadColor | PointLightUploadExtraData | PointLightUploadCustomId));

            _uploadAutoUpdatedVolumeChangesMethod.Invoke(manager, null);
            AssertVectorClose(ExpectedPointLightColor(basic), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            Assert.That(GetManagerField<int>(manager, _pointLightArrayUploadMaskField), Is.Zero);
        }

        // The narrow pack is legal only for unshadowed parametric Point Lights; Spot data takes the full path.
        [Test]
        public void PointLightColorRangeQueueFallsBackToFullPackForSpotProfile() {
            LightVolumeManager manager = CreateManager("Point Queue Fallback Manager", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Point Queue Fallback Spot", true);
            point.LightType = 1;
            point.ProjectionMode = 0;
            point.Direction = Vector3.forward;
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            Vector3 changedDirection = new Vector3(0.25f, 0.5f, 0.75f).normalized;
            point.Direction = changedDirection;
            SetManagerField(manager, _isUpdatingVolumesField, true);
            manager.NotifyPointLightColorRangeChanged(point);
            SetManagerField(manager, _isUpdatingVolumesField, false);

            _updateDynamicVolumeTransformsMethod.Invoke(manager, null);
            Vector4 packedDirection = Shader.GetGlobalVectorArray(_pointLightDirectionID)[0];
            AssertVectorClose(new Vector4(changedDirection.x, changedDirection.y, changedDirection.z, point.ConeFalloff), packedDirection);
        }

        // A preserved generic notification must widen an already queued Color update to a full record update.
        [Test]
        public void GenericPointNotificationWidensQueuedColorUpdateToFullPack() {
            LightVolumeManager manager = CreateManager("Point Queue Widening Manager", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Point Queue Widening Light", true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            SetManagerField(manager, _isUpdatingVolumesField, true);
            point.Color = new Color(0.4f, 0.7f, 0.2f, 1f);
            point.IsRangeDirty = true;
            manager.NotifyPointLightColorRangeChanged(point);
            point.ShadingStrength = 0f;
            manager.NotifyPointLightVolumeChanged(point, false, false, false);
            SetManagerField(manager, _isUpdatingVolumesField, false);

            Assert.That(GetManagerField<int[]>(manager, _dirtyPointLightUpdateFlagsField)[0] & 2, Is.EqualTo(2));
            _updateDynamicVolumeTransformsMethod.Invoke(manager, null);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].y, Is.EqualTo(10000f).Within(Epsilon));
        }

        // Public registries are mutable; losing one after a notification must request a rebuild, not throw.
        [Test]
        public void QueuedPointNotificationHandlesNullRegistryWithoutHalting() {
            LightVolumeManager manager = CreateManager("Null Point Registry Queue Manager", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Null Point Registry Queue Light", true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            SetManagerField(manager, _isUpdatingVolumesField, true);
            point.Color = new Color(0.7f, 0.2f, 0.4f, 1f);
            point.IsRangeDirty = true;
            manager.NotifyPointLightColorRangeChanged(point);
            SetManagerField(manager, _isUpdatingVolumesField, false);
            manager.PointLightVolumeInstances = null;

            _updateDynamicVolumeTransformsMethod.Invoke(manager, null);
            Assert.That(GetManagerField<int>(manager, _dirtyPointLightCountField), Is.Zero);
            Assert.That(GetManagerField<int[]>(manager, _dirtyPointLightUpdateFlagsField)[0], Is.Zero);
            Assert.That(manager.PointLightVolumeInstances, Is.Not.Null.And.Empty,
                "The production PostLate wrapper must recover through a complete compact rebuild.");
            AssertGlobalFloat(_pointLightCountID, 0f);
        }

        // The delayed maintenance event must not race PostLateUpdate for ownership of the Point queue.
        [Test]
        public void UpdateProcessLeavesQueuedPointChangesForPostLateConsumer() {
            LightVolumeManager manager = CreateManager("Single Point Queue Consumer Manager", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Single Point Queue Consumer Light", true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            SetManagerField(manager, _isUpdatingVolumesField, true);
            SetManagerField(manager, _isUpdateProcessRunningField, false);
            point.Color = new Color(0.15f, 0.45f, 0.85f, 1f);
            point.Intensity = 3.25f;
            point.IsRangeDirty = true;
            manager.NotifyPointLightColorRangeChanged(point);
            SetManagerField(manager, _isUpdatingVolumesField, false);

            Assert.That(GetManagerField<int>(manager, _dirtyPointLightCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<bool>(manager, _isUpdateProcessRunningField), Is.False,
                "Parameter-only notification must not schedule an empty delayed maintenance event.");
            manager.UpdateProcess();
            Assert.That(GetManagerField<int>(manager, _dirtyPointLightCountField), Is.EqualTo(1),
                "Delayed UpdateProcess must leave the queue for the transform-safe PostLate consumer.");

            _updateDynamicVolumeTransformsMethod.Invoke(manager, null);
            Assert.That(GetManagerField<int>(manager, _dirtyPointLightCountField), Is.Zero);
            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
        }

        // Verifies public generic notification stays with the single PostLate consumer and uploads
        // every regular array, including smoothing and UVW data, after a concurrent texture tick.
        [Test]
        public void GenericLightVolumeDirtyUploadWritesAllSixRegularBufferGroups() {
            LightVolumeManager manager = CreateManager("Generic Full Regular Upload Manager", true);
            LightVolumeInstance volume = CreateLightVolume(manager, "Generic Full Regular Upload Volume", true);
            manager.LightVolumeInstances = new[] { volume };
            manager.UpdateVolumes();

            ConfigureLightVolume(volume, new Color(0.15f, 0.7f, 0.35f, 1f), 2.5f, false, 0.6f);
            volume.InvWorldMatrix = Matrix4x4.TRS(new Vector3(2f, 3f, 4f), Quaternion.Euler(10f, 20f, 30f), new Vector3(2f, 1.5f, 0.75f)).inverse;
            volume.RelativeRotationRow0 = new Vector3(0.25f, 0.5f, 0.75f);
            volume.RelativeRotationRow1 = new Vector3(-0.5f, 0.125f, 0.625f);
            volume.IsRotated = true;

            SetManagerField(manager, _isUpdatingVolumesField, true);
            SetManagerField(manager, _isUpdateProcessRunningField, false);
            manager.NotifyLightVolumeChanged(volume, false);
            SetManagerField(manager, _isUpdatingVolumesField, false);
            Assert.That(GetManagerField<bool>(manager, _lightVolumeArraysDirtyField), Is.True);
            Assert.That(GetManagerField<bool>(manager, _isUpdateProcessRunningField), Is.False,
                "A stable generic record must not schedule an empty delayed maintenance event.");

            Vector4 sentinel = new Vector4(-9f, -8f, -7f, -6f);
            Shader.SetGlobalVectorArray(_lightVolumeInvLocalEdgeSmoothID, new[] { sentinel });
            Shader.SetGlobalVectorArray(_lightVolumeUvwScaleID, new[] { sentinel });
            Shader.SetGlobalVectorArray(_lightVolumeUvwID, new[] { sentinel });
            Shader.SetGlobalMatrixArray(_lightVolumeInvWorldMatrixID, new[] { Matrix4x4.zero });
            Shader.SetGlobalVectorArray(_lightVolumeRotationID, new[] { sentinel });
            Shader.SetGlobalVectorArray(_lightVolumeColorID, new[] { sentinel });

            manager.UpdateProcess();
            Assert.That(GetManagerField<bool>(manager, _lightVolumeArraysDirtyField), Is.True,
                "The texture/maintenance loop must leave regular records for the PostLate consumer.");
            AssertVectorClose(sentinel, Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);

            _updateDynamicVolumeTransformsMethod.Invoke(manager, null);
            Assert.That(GetManagerField<bool>(manager, _lightVolumeArraysDirtyField), Is.False);

            AssertVectorClose(volume.InvLocalEdgeSmoothing, Shader.GetGlobalVectorArray(_lightVolumeInvLocalEdgeSmoothID)[0]);
            Vector4[] uvwScale = Shader.GetGlobalVectorArray(_lightVolumeUvwScaleID);
            AssertVectorClose(volume.BoundsUvwMin0, uvwScale[0]);
            AssertVectorClose(volume.BoundsUvwMin1, uvwScale[1]);
            AssertVectorClose(volume.BoundsUvwMin2, uvwScale[2]);
            Vector4[] expandedUvw = Shader.GetGlobalVectorArray(_lightVolumeUvwID);
            for (int textureIndex = 0; textureIndex < 3; textureIndex++) {
                AssertVectorClose(ExpectedExpandedLightVolumeUvw(volume, textureIndex, false), expandedUvw[textureIndex * 2]);
                AssertVectorClose(ExpectedExpandedLightVolumeUvw(volume, textureIndex, true), expandedUvw[textureIndex * 2 + 1]);
            }
            AssertMatrixClose(volume.InvWorldMatrix, Shader.GetGlobalMatrixArray(_lightVolumeInvWorldMatrixID)[0]);
            Vector4[] rotation = Shader.GetGlobalVectorArray(_lightVolumeRotationID);
            AssertVectorClose(volume.RelativeRotationRow0, rotation[0]);
            AssertVectorClose(volume.RelativeRotationRow1, rotation[1]);
            AssertVectorClose(ExpectedLightVolumeColor(volume), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);
        }

        // Verifies always-supported parameter animation flushes before clustering even when transform polling is disabled.
        [Test]
        public void PostLateIncrementalFlushPublishesPointParametersWithoutAutoTransformPolling() {
            LightVolumeManager manager = CreateManager("PostLate Parameter Flush Manager", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "PostLate Parameter Flush Point", true);
            manager.AutoUpdateVolumes = false;
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            Color animatedColor = new Color(0.2f, 0.6f, 0.9f, 1f);
            float animatedIntensity = 3.25f;
            // Hold the rebuild guard so this EditMode test can observe the runtime queue before
            // the manager's required synchronous editor fallback consumes it.
            point.Color = animatedColor;
            point.Intensity = animatedIntensity;
            point.IsRangeDirty = true;
            SetManagerField(manager, _isUpdatingVolumesField, true);
            manager.NotifyPointLightColorRangeChanged(point);
            SetManagerField(manager, _isUpdatingVolumesField, false);

            Assert.That(_updateDynamicVolumeTransformsMethod, Is.Not.Null);
            Assert.That(_clusterGeometryUploadPendingField, Is.Not.Null);
            Assert.That(GetManagerField<int>(manager, _dirtyPointLightCountField), Is.EqualTo(1));

            Vector4 sentinel = new Vector4(-8f, -7f, -6f, -5f);
            Shader.SetGlobalVectorArray(_pointLightColorID, new[] { sentinel });
            _updateDynamicVolumeTransformsMethod.Invoke(manager, null);

            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            Assert.That(GetManagerField<int>(manager, _dirtyPointLightCountField), Is.Zero);
            Assert.That(GetManagerField<bool>(manager, _clusterGeometryUploadPendingField), Is.False);
        }

        // Verifies movement and animated parameters converge into one final-state PostLate upload.
        [Test]
        public void PostLateIncrementalFlushMergesPointMovementAndAnimatedParameters() {
            LightVolumeManager manager = CreateManager("PostLate Combined Flush Manager", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "PostLate Combined Flush Point", true);
            point.IsDynamic = true;
            manager.AutoUpdateVolumes = true;
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            point.transform.position = new Vector3(7.5f, -2.25f, 11f);
            point.Color = new Color(0.85f, 0.25f, 0.55f, 1f);
            point.Intensity = 2.75f;
            point.IsRangeDirty = true;
            SetManagerField(manager, _isUpdatingVolumesField, true);
            manager.NotifyPointLightColorRangeChanged(point);
            SetManagerField(manager, _isUpdatingVolumesField, false);

            Vector4 sentinel = new Vector4(-8f, -7f, -6f, -5f);
            Shader.SetGlobalVectorArray(_pointLightPositionID, new[] { sentinel });
            Shader.SetGlobalVectorArray(_pointLightColorID, new[] { sentinel });

            Assert.That(_updateDynamicVolumeTransformsMethod, Is.Not.Null);
            _updateDynamicVolumeTransformsMethod.Invoke(manager, null);

            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);
            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            Assert.That(GetManagerField<int>(manager, _dirtyPointLightCountField), Is.Zero);
            Assert.That(GetManagerField<bool>(manager, _clusterGeometryUploadPendingField), Is.False);
        }

        // Verifies dynamic regular and point light transforms are pushed into shader globals after movement.
        [Test]
        public void DynamicInstancesWriteMovedTransformsToShaderGlobals() {
            LightVolumeManager manager = CreateManager("Dynamic Transform Manager", true);
            LightVolumeInstance volume = CreateLightVolume(manager, "Dynamic Volume", true);
            PointLightVolumeInstance point = CreatePointLight(manager, "Dynamic Point", true);

            volume.IsDynamic = true;
            volume.transform.position = new Vector3(10, 20, 30);
            volume.transform.rotation = Quaternion.Euler(15, 25, 35);
            volume.transform.localScale = new Vector3(2, 3, 4);
            point.IsDynamic = true;
            point.transform.position = new Vector3(-2, -3, -4);
            point.transform.rotation = Quaternion.Euler(0, 90, 0);
            point.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            SetPointLightSquaredSize(point, 3);
            point.SetCustomTexture();
            manager.LightVolumeInstances = new[] { volume };
            manager.PointLightVolumeInstances = new[] { point };

            manager.UpdateVolumes();

            Quaternion expectedPointRotation = Quaternion.Inverse(point.transform.rotation);
            AssertMatrixClose(Matrix4x4.TRS(volume.transform.position, volume.transform.rotation, volume.transform.lossyScale).inverse, Shader.GetGlobalMatrixArray(_lightVolumeInvWorldMatrixID)[0]);
            AssertVectorClose(volume.RelativeRotationRow0, Shader.GetGlobalVectorArray(_lightVolumeRotationID)[0]);
            AssertVectorClose(volume.RelativeRotationRow1, Shader.GetGlobalVectorArray(_lightVolumeRotationID)[1]);
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);
            AssertVectorClose(new Vector4(expectedPointRotation.x, expectedPointRotation.y, expectedPointRotation.z, expectedPointRotation.w), Shader.GetGlobalVectorArray(_pointLightDirectionID)[0]);
            Assert.That(volume.IsRotated, Is.True);
            Assert.That(Shader.GetGlobalVectorArray(_lightVolumeColorID)[0].w, Is.EqualTo(1).Within(Epsilon));

            Vector4 staticGroupSentinel = new Vector4(-5f, -4f, -3f, -2f);
            Shader.SetGlobalVectorArray(_lightVolumeInvLocalEdgeSmoothID, new[] { staticGroupSentinel });
            Shader.SetGlobalVectorArray(_lightVolumeUvwScaleID, new[] { staticGroupSentinel });
            Shader.SetGlobalVectorArray(_lightVolumeUvwID, new[] { staticGroupSentinel });

            volume.transform.position = new Vector3(-7, 8, 9);
            volume.transform.rotation = Quaternion.identity;
            volume.transform.localScale = new Vector3(3, 1.25f, 2.5f);
            Matrix4x4 movedLocalToWorld = volume.transform.localToWorldMatrix;
            Matrix4x4 expectedInverse = movedLocalToWorld.inverse;
            Quaternion expectedRelativeRotation = movedLocalToWorld.rotation * volume.InvBakedRotation;
            Matrix4x4 expectedRotationMatrix = Matrix4x4.Rotate(expectedRelativeRotation);
            Vector4 expectedRotationRow0 = expectedRotationMatrix.GetRow(0);
            Vector4 expectedRotationRow1 = expectedRotationMatrix.GetRow(1);
            Vector3 expectedScale = movedLocalToWorld.lossyScale;
            float expectedSafeSmoothing = Mathf.Max(volume.SmoothBlending, 0.00001f);
            Vector4 expectedEdgeSmoothing = new Vector4(
                expectedScale.x / expectedSafeSmoothing,
                expectedScale.y / expectedSafeSmoothing,
                expectedScale.z / expectedSafeSmoothing,
                0f);

            Assert.That(_updateAutoUpdatedVolumeChangesMethod, Is.Not.Null);
            Assert.That(_uploadAutoUpdatedVolumeChangesMethod, Is.Not.Null);
            _updateAutoUpdatedVolumeChangesMethod.Invoke(manager, null);
            _uploadAutoUpdatedVolumeChangesMethod.Invoke(manager, null);

            AssertMatrixClose(expectedInverse, volume.InvWorldMatrix);
            Assert.That(volume.IsRotated, Is.False);
            AssertVectorClose(expectedRotationRow0, volume.RelativeRotationRow0);
            AssertVectorClose(expectedRotationRow1, volume.RelativeRotationRow1);
            AssertMatrixClose(expectedInverse, Shader.GetGlobalMatrixArray(_lightVolumeInvWorldMatrixID)[0]);
            AssertVectorClose(expectedRotationRow0, Shader.GetGlobalVectorArray(_lightVolumeRotationID)[0]);
            AssertVectorClose(expectedRotationRow1, Shader.GetGlobalVectorArray(_lightVolumeRotationID)[1]);
            Assert.That(Shader.GetGlobalVectorArray(_lightVolumeColorID)[0].w, Is.Zero.Within(Epsilon));
            AssertVectorClose(expectedEdgeSmoothing, volume.InvLocalEdgeSmoothing);
            AssertVectorClose(expectedEdgeSmoothing, Shader.GetGlobalVectorArray(_lightVolumeInvLocalEdgeSmoothID)[0]);
            AssertVectorClose(staticGroupSentinel, Shader.GetGlobalVectorArray(_lightVolumeUvwScaleID)[0]);
            AssertVectorClose(staticGroupSentinel, Shader.GetGlobalVectorArray(_lightVolumeUvwID)[0]);
        }

        [Test]
        public void EquivalentNegativeIdentityBakeRotationDoesNotEnableRotationPath() {
            LightVolumeManager manager = CreateManager("Quaternion Sign Manager", true);
            LightVolumeInstance volume = CreateLightVolume(manager, "Quaternion Sign Volume", true);
            volume.InvBakedRotation = new Quaternion(0f, 0f, 0f, -1f);
            manager.LightVolumeInstances = new[] { volume };

            manager.UpdateVolumes();

            Assert.That(volume.IsRotated, Is.False);
            Assert.That(Shader.GetGlobalVectorArray(_lightVolumeColorID)[0].w, Is.Zero.Within(Epsilon));

            volume.IsRotated = true;
            volume.UpdateTransform();
            Assert.That(volume.IsRotated, Is.False);
        }

        // Verifies the manual API preserves the complete world matrix under a rotated, non-uniform parent.
        [Test]
        public void ManualLightVolumeTransformUsesLocalToWorldMatrixWithShearedParent() {
            GameObject parent = CreateGameObject("Sheared Volume Parent", true);
            parent.transform.position = new Vector3(2, -3, 4);
            parent.transform.rotation = Quaternion.Euler(20, 35, 10);
            parent.transform.localScale = new Vector3(2, 3, 0.75f);

            LightVolumeInstance volume = CreateLightVolume(null, "Sheared Manual Volume", true);
            volume.transform.SetParent(parent.transform, false);
            volume.transform.localPosition = new Vector3(1, 2, -1);
            volume.transform.localRotation = Quaternion.Euler(15, -25, 30);
            volume.transform.localScale = new Vector3(0.5f, 1.25f, 2f);

            Matrix4x4 localToWorldMatrix = volume.transform.localToWorldMatrix;
            volume.UpdateTransform();

            AssertMatrixClose(localToWorldMatrix.inverse, volume.InvWorldMatrix);
            Vector3 scale = localToWorldMatrix.lossyScale;
            float safeSmoothing = Mathf.Max(volume.SmoothBlending, 0.00001f);
            AssertVectorClose(new Vector4(scale.x / safeSmoothing, scale.y / safeSmoothing, scale.z / safeSmoothing, 0f),
                volume.InvLocalEdgeSmoothing);
        }

        // Verifies cached transform data changes only when the instance update methods run.
        [Test]
        public void StaticInstancesKeepCachedTransformDataUntilManualUpdateMethodsRun() {
            LightVolumeInstance volume = CreateLightVolume(null, "Static Cached Volume", true);
            PointLightVolumeInstance point = CreatePointLight(null, "Static Cached Point", true);

            volume.IsDynamic = false;
            volume.transform.position = Vector3.zero;
            volume.UpdateTransform();
            Matrix4x4 cachedVolumeMatrix = volume.InvWorldMatrix;
            point.IsDynamic = false;
            point.transform.position = Vector3.zero;
            SetPointLightSquaredSize(point, 2);
            point.UpdateTransform();
            Vector4 cachedPointPosition = new Vector4(point.Position.x, point.Position.y, point.Position.z, 0);

            volume.transform.position = new Vector3(5, 6, 7);
            point.transform.position = new Vector3(8, 9, 10);

            AssertMatrixClose(cachedVolumeMatrix, volume.InvWorldMatrix);
            AssertVectorClose(cachedPointPosition, new Vector4(point.Position.x, point.Position.y, point.Position.z, 0));

            volume.UpdateTransform();
            point.UpdateTransform();

            AssertMatrixClose(Matrix4x4.TRS(volume.transform.position, volume.transform.rotation, volume.transform.lossyScale).inverse, volume.InvWorldMatrix);
            AssertVectorClose(new Vector4(8, 9, 10, 0), new Vector4(point.Position.x, point.Position.y, point.Position.z, 0));
        }

        // Checks point light shader globals through point, LUT, cookie spot, area, and Shadow modes.
        [Test]
        public void PointLightGlobalsWorldSpaceShadowAndCutoffChanges() {
            LightVolumeManager manager = CreateManager("Point Globals Manager", false);
            manager.ShadowTexturesWidth = 256;
            manager.ShadowTexturesHeight = 256;
            manager.LightsBrightnessCutoff = 0.2f;

            PointLightVolumeInstance point = CreatePointLight(manager, "Point Light", true);
            point.transform.position = new Vector3(2, 3, 4);
            point.transform.rotation = Quaternion.Euler(10, 20, 30);
            point.transform.localScale = Vector3.one;
            point.Color = new Color(1, 0.5f, 0.25f, 1);
            point.Intensity = 2;
            SetPointLightSquaredSize(point, 4);
            point.Angle = 30 * Mathf.Deg2Rad;
            point.OuterAngleCos = Mathf.Cos(point.Angle);
            point.SetPointLight();

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_pointLightCountID, 1);
            AssertGlobalFloat(_pointLightCubeCountID, 0);
            AssertGlobalFloat(_lightBrightnessCutoffID, 0.2f);
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);
            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            AssertPointCustomData(point, 0, 0);

            point.ShadingStrength = 0.25f;
            manager.UpdateVolumes();
            AssertPointCustomData(point, 0, 0.75f);
            point.ShadingStrength = 1;

            point.SetColor(Color.black);
            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 0);
            AssertGlobalFloat(_pointLightCountID, 0);

            point.SetColor(new Color(1, 0.5f, 0.25f, 1));
            ConfigureShadowTexture(point, CreateCubemap("Point Globals Shadow Source"), false, true, false);
            point.WorldSpaceShadows = true;
            point.Bias = -1;
            point.ShadowBakePosition = new Vector3(5, 6, 7);
            manager.PointLightVolumeInstances = new[] { point };
            manager.ReinitializeShadowTextures();

            manager.UpdateVolumes();

            AssertGlobalFloat(_pointLightShadowCountID, 1);
            AssertPointCustomData(point, 0, 1);
            AssertVectorClose(new Vector4(5, 6, 7, ExpectedCubemapShadowInvDepthRange(point)), Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0]);
            AssertVectorClose(new Vector4(0, 0, 0, 1), Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID)[0]);

            point.ShadingStrength = 0.5f;
            manager.UpdateVolumes();
            AssertPointCustomData(point, 0, 1.5f);

            point.ShadingStrength = 0;
            manager.UpdateVolumes();
            Vector4 disabledShadingData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0];
            AssertGlobalFloat(_pointLightShadowCountID, 0);
            Assert.That(disabledShadingData.y, Is.EqualTo(10000).Within(Epsilon));
            Assert.That(disabledShadingData.w, Is.EqualTo(0).Within(Epsilon));
            point.ShadingStrength = 1;

            point.WorldSpaceShadows = false;

            manager.UpdateVolumes();

            Quaternion expectedLocalSpaceRotation = Quaternion.Inverse(point.transform.rotation);
            AssertPointCustomData(point, 0, -1);
            AssertVectorClose(new Vector4(5, 6, 7, ExpectedCubemapShadowInvDepthRange(point)), Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0]);
            AssertVectorClose(new Vector4(expectedLocalSpaceRotation.x, expectedLocalSpaceRotation.y, expectedLocalSpaceRotation.z, expectedLocalSpaceRotation.w), Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID)[0]);

            point.CustomTexture = CreateTexture2D("Point Globals LUT");
            point.ProjectionType = 1; // 1: texture
            point.SetLut();
            point.SetLightSourceSize(5);

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(point.ProjectionMode, Is.EqualTo(1)); // 1: LUT
            AssertPointCustomData(point, 1, -1);
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);

            point.SetCustomTexture();
            point.SetSpotLight(60, 0.25f);

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Quaternion expectedCookieRotation = Quaternion.Inverse(point.transform.rotation);
            Assert.That(point.LightType, Is.EqualTo(1)); // 1: spot
            AssertPointCustomData(point, -1, -1);
            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            AssertVectorClose(new Vector4(expectedCookieRotation.x, expectedCookieRotation.y, expectedCookieRotation.z, expectedCookieRotation.w), Shader.GetGlobalVectorArray(_pointLightDirectionID)[0]);

            point.SetParametric();
            point.transform.localScale = new Vector3(2, 3, 1);
            point.SetAreaLight();

            manager.UpdateVolumes();

            Quaternion expectedAreaRotation = point.transform.rotation;
            Assert.That(point.LightType, Is.EqualTo(2)); // 2: area
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);
            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            AssertVectorClose(new Vector4(expectedAreaRotation.x, expectedAreaRotation.y, expectedAreaRotation.z, expectedAreaRotation.w), Shader.GetGlobalVectorArray(_pointLightDirectionID)[0]);
            AssertPointCustomData(point, 0, -1);

            manager.LightsBrightnessCutoff = 0.5f;

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightBrightnessCutoffID, 0.5f);
            Assert.That(point.IsRangeDirty, Is.False);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].z, Is.EqualTo(point.SquaredRange).Within(Epsilon));
        }

        // Verifies all active point lights can use cubemap projection data at the same time.
        [Test]
        public void AllPointLightsWithCubemapProjectionWriteProjectionGlobals() {
            LightVolumeManager manager = CreateManager("All Cubemap Projection Manager", false);
            const int pointCount = 8;
            PointLightVolumeInstance[] points = new PointLightVolumeInstance[pointCount];

            for (int i = 0; i < pointCount; i++) {
                PointLightVolumeInstance point = CreatePointLight(manager, "Cubemap Point " + i, true);
                point.transform.position = new Vector3(i, i + 1, i + 2);
                point.transform.rotation = Quaternion.Euler(i * 5, i * 7, i * 11);
                SetPointLightSquaredSize(point, i + 1);
                point.CustomTexture = CreateCubemap("Cubemap Projection Source " + i);
                point.CustomTextureIsCubemap = true;
                point.ProjectionType = 1; // 1: texture
                point.SetCustomTexture();
                points[i] = point;
            }
            manager.PointLightVolumeInstances = points;

            manager.UpdateVolumes();

            Vector4[] positions = Shader.GetGlobalVectorArray(_pointLightPositionID);
            Vector4[] directions = Shader.GetGlobalVectorArray(_pointLightDirectionID);
            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_pointLightCountID, pointCount);
            AssertGlobalFloat(_pointLightCubeCountID, pointCount);
            for (int i = 0; i < pointCount; i++) {
                Quaternion expectedRotation = Quaternion.Inverse(points[i].transform.rotation);
                AssertVectorClose(ExpectedPointLightPosition(points[i]), positions[i]);
                AssertVectorClose(new Vector4(expectedRotation.x, expectedRotation.y, expectedRotation.z, expectedRotation.w), directions[i]);
                AssertPointCustomData(i, points[i], -i - 1, 0);
            }
        }

        // Verifies native spot cookies create a manager-owned runtime texture array and shader ID
        [Test]
        public void SpotCookieCreatesRuntimeArrayAndShaderId() {
            LightVolumeManager manager = CreateManager("Spot Cookie Runtime Manager", false);
            RenderTexture source = CreateRenderTexture("Animated Spot Cookie Source", 4, 4, 1, TextureDimension.Tex2D);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Animated Spot Cookie Light", true);
            point.SetCustomTexture();
            point.SetSpotLight(60, 0.5f);
            point.SpotCookieAspect = 1.75f;
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            point.AutoUpdateCustomTexture = true;
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(manager.CustomTextures.width, Is.EqualTo(4));
            Assert.That(manager.CustomTextures.height, Is.EqualTo(4));
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(1));
            Assert.That(manager.CustomTextures.format, Is.EqualTo(RenderTextureFormat.ARGBHalf));
            Assert.That(manager.CustomTextures.useMipMap, Is.False);
            Assert.That(manager.CustomTextures.autoGenerateMips, Is.False);
            AssertPointCustomData(point, -1, 0);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightExtraDataID)[0].x, Is.EqualTo(1.75f).Within(Epsilon));
        }

        // Verifies area lights without a cookie keep the default fast area light shader path.
        [Test]
        public void AreaLightWithoutCookieKeepsDefaultProjectionId() {
            LightVolumeManager manager = CreateManager("Area No Cookie Runtime Manager", false);

            PointLightVolumeInstance point = CreatePointLight(manager, "Area No Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.SetAreaLight();
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(point.LightType, Is.EqualTo(2)); // 2: area
            Assert.That(point.ProjectionMode, Is.EqualTo(0)); // 0: parametric, no cookie
            Assert.That(manager.CustomTextures, Is.Null);
            AssertPointCustomData(point, 0, 0);
        }

        // Verifies area light cookies use the single-slice texture cache and write a negative shader ID.
        [Test]
        public void AreaCookieCreatesRuntimeArrayAndShaderId() {
            LightVolumeManager manager = CreateManager("Area Cookie Runtime Manager", false);
            Texture2D source = CreateTexture2D("Area Cookie Source");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(point.LightType, Is.EqualTo(2)); // 2: area
            Assert.That(point.ProjectionMode, Is.EqualTo(2)); // 2: custom cookie or cubemap
            Assert.That(manager.CubemapsCount, Is.EqualTo(0));
            Assert.That(GetManagerField<int>(manager, _customSingleTextureCountField), Is.EqualTo(1));
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(1));
            Assert.That(manager.CustomTextures.useMipMap, Is.True);
            Assert.That(manager.CustomTextures.autoGenerateMips, Is.False);
            Assert.That(Shader.GetGlobalFloat(_pointLightTextureTexelCountID), Is.EqualTo(16));
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);
            AssertVectorClose(ExpectedPointLightColor(point), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            AssertPointCustomData(point, -1, 0);
        }

        // Verifies the runtime manager respects a manual auto-update override on an area RenderTexture cookie.
        [Test]
        public void AreaRenderTextureCookieRespectsManualAutoUpdateOverride() {
            LightVolumeManager manager = CreateManager("Area Render Texture Manual Auto Update Manager", false);
            RenderTexture source = CreateRenderTexture("Area Render Texture Cookie Source", 4, 4, 1, TextureDimension.Tex2D);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Render Texture Manual Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            point.AutoUpdateCustomTexture = false;
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();

            Assert.That(manager.HasAutoCustomTextureUpdates, Is.False);
        }

        // Verifies edit-mode auto updates rebuild stale auto-mip arrays before manual mip generation.
        [Test]
        public void AreaCookieAutoUpdateRebuildsStaleAutoMipArray() {
            LightVolumeManager manager = CreateManager("Area Cookie Stale Auto Mip Manager", false);
            RenderTexture source = CreateRenderTexture("Area Cookie Auto Update Source", 4, 4, 1, TextureDimension.Tex2D);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Cookie Auto Update Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            point.AutoUpdateCustomTexture = true;
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();

            RenderTexture stale = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            stale.name = "Stale Auto Mip Custom Texture Array";
            stale.dimension = TextureDimension.Tex2DArray;
            stale.volumeDepth = 1;
            stale.useMipMap = true;
            stale.autoGenerateMips = true;
            stale.filterMode = FilterMode.Trilinear;
            stale.Create();
            _createdObjects.Add(stale);
            manager.CustomTextures = stale;

            manager.UpdateAutoCustomTextures();

            Assert.That(ReferenceEquals(manager.CustomTextures, stale), Is.False);
            Assert.That(manager.CustomTextures.useMipMap, Is.True);
            Assert.That(manager.CustomTextures.autoGenerateMips, Is.False);
        }

        // Verifies area cookie fallback averages cache before shader buffers exist, patch live buffers, and keep the cached color until replacement readback.
        [Test]
        public void AreaCookieFallbackAveragePreservesCachedColorUntilReplacementReadback() {
            LightVolumeManager manager = CreateManager("Area Cookie Fallback Average Manager", false, false);
            Texture2D source = CreateTexture2D("Area Average Cookie Source");
            Texture2D replacementSource = CreateTexture2D("Area Average Cookie Replacement Source");
            Color averageColor = new Color(0.25f, 0.5f, 0.75f, 1f);
            Color liveAverageColor = new Color(0.5f, 0.25f, 0.125f, 1f);
            Color replacementAverageColor = new Color(0.125f, 0.75f, 0.375f, 1f);
            source.SetPixel(0, 0, liveAverageColor);
            source.Apply(false);
            replacementSource.SetPixel(0, 0, replacementAverageColor);
            replacementSource.Apply(false);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Average Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.Color = new Color(0.25f, 0.5f, 1f, 1f);
            point.Intensity = 2f;
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();

            Assert.That(manager.HasAutoCustomTextureUpdates, Is.False);

            point.AreaCookieAverageReadbackPending = true;
            point.AreaCookieAverageReadbackDirty = false;
            point.AreaCookieAverageCustomId = 0;
            manager.ReinitializeCustomTextures();

            Assert.That(point.AreaCookieAverageReadbackPending, Is.True);
            Assert.That(point.AreaCookieAverageReadbackDirty, Is.True);
            Assert.That(point.AreaCookieAverageCustomId, Is.EqualTo(0));

            point.AreaCookieAverageReadbackPending = false;
            point.AreaCookieAverageCustomId = -1;

            UploadAreaCookieAverageColor(manager, 0, averageColor);
            manager.gameObject.SetActive(true);
            manager.UpdateVolumes();

            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, averageColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            UploadAreaCookieAverageColor(manager, 0, liveAverageColor);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, liveAverageColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            manager.ReinitializeCustomTextures();
            point.Color = new Color(1f, 0.5f, 0.25f, 1f);
            manager.UpdateVolumes();

            Color liveReadbackColor = GetAreaCookieAverageColor(manager, 0);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, liveReadbackColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            point.CustomTexture = replacementSource;
            manager.ReinitializeCustomTextures();
            point.Color = new Color(0.5f, 1f, 0.25f, 1f);
            manager.UpdateVolumes();

            Color replacementReadbackColor = GetAreaCookieAverageColor(manager, 0);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, replacementReadbackColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            UploadAreaCookieAverageColor(manager, 0, replacementAverageColor);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, replacementAverageColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);

            point.IsActive = false;
            manager.ReinitializeCustomTextures();

            point.IsActive = true;
            manager.ReinitializeCustomTextures();

            manager.UpdateVolumes();
            Color restoredReadbackColor = GetAreaCookieAverageColor(manager, 0);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, restoredReadbackColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
        }

        // Verifies an existing area-cookie fallback average is shared with later area lights deduped to the same cookie source.
        [Test]
        public void SharedAreaCookieFallbackAveragePropagatesAfterDedupe() {
            LightVolumeManager manager = CreateManager("Shared Area Cookie Fallback Manager", false);
            Texture2D source = CreateTexture2D("Shared Area Cookie Source");
            Color averageColor = new Color(0.25f, 0.5f, 0.75f, 1f);
            source.SetPixel(0, 0, averageColor);
            source.Apply(false);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "Shared Area Cookie A", true);
            firstPoint.transform.localScale = new Vector3(2, 3, 1);
            firstPoint.Color = new Color(0.25f, 0.5f, 1f, 1f);
            firstPoint.Intensity = 2f;
            firstPoint.SetCustomTexture();
            firstPoint.SetAreaLight();
            firstPoint.CustomTexture = source;
            firstPoint.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { firstPoint };

            manager.ReinitializeCustomTextures();
            UploadAreaCookieAverageColor(manager, 0, averageColor);

            PointLightVolumeInstance secondPoint = CreatePointLight(manager, "Shared Area Cookie B", true);
            secondPoint.transform.localScale = new Vector3(2, 3, 1);
            secondPoint.Color = new Color(1f, 0.5f, 0.25f, 1f);
            secondPoint.Intensity = 2f;
            secondPoint.SetCustomTexture();
            secondPoint.SetAreaLight();
            secondPoint.CustomTexture = source;
            secondPoint.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { firstPoint, secondPoint };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(GetManagerField<int>(manager, _customSingleTextureCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, 0 }));
            Vector4[] colors = Shader.GetGlobalVectorArray(_pointLightColorID);
            Color firstReadbackColor = GetAreaCookieAverageColor(manager, 0);
            Color secondReadbackColor = GetAreaCookieAverageColor(manager, 1);
            AssertVectorClose(new Vector4(firstReadbackColor.r, firstReadbackColor.g, firstReadbackColor.b, firstReadbackColor.a), new Vector4(secondReadbackColor.r, secondReadbackColor.g, secondReadbackColor.b, secondReadbackColor.a));
            AssertVectorClose(ExpectedAreaCookieFallbackColor(firstPoint, firstReadbackColor), colors[0]);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(secondPoint, secondReadbackColor), colors[1]);
        }

        // Verifies deinitializing an area cookie light stores its fallback average on the instance for the next initialization.
        [Test]
        public void AreaCookieFallbackAverageCachesOnDeinitializeAndRestoresOnInitialize() {
            LightVolumeManager manager = CreateManager("Area Cookie Deinitialize Fallback Manager", false);
            Texture2D source = CreateTexture2D("Area Deinitialize Cookie Source");
            Color averageColor = new Color(0.25f, 0.5f, 0.75f, 1f);
            source.SetPixel(0, 0, averageColor);
            source.Apply(false);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Deinitialize Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.Color = new Color(0.25f, 0.5f, 1f, 1f);
            point.Intensity = 2f;
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            UploadAreaCookieAverageColor(manager, 0, averageColor);

            manager.DeinitializePointLightVolume(point, true, false);
            AssertVectorClose(new Vector4(averageColor.r, averageColor.g, averageColor.b, averageColor.a), point.AreaLightFallbackColor);
            manager.ReinitializeCustomTextures();
            Assert.That(GetAreaCookieAverageColor(manager, 0).a, Is.EqualTo(0f).Within(Epsilon));

            point.AreaCookieAverageReadbackPending = true;
            point.AreaCookieAverageReadbackDirty = false;
            point.AreaCookieAverageCustomId = 0;
            manager.InitializePointLightVolume(point);
            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(point.AreaCookieAverageReadbackPending, Is.True);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, averageColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            point.AreaCookieAverageReadbackPending = false;
            point.AreaCookieAverageReadbackDirty = false;
            point.AreaCookieAverageCustomId = -1;
        }

        // Verifies an invalidated async area cookie readback cannot patch the current fallback color through a reused custom ID.
        [Test]
        public void InvalidatedAreaCookieReadbackDoesNotPatchReusedCustomId() {
            LightVolumeManager manager = CreateManager("Area Cookie Invalidated Readback Manager", false);
            Texture2D source = CreateTexture2D("Area Invalidated Cookie Source");
            Color staleAverageColor = new Color(0.875f, 0.125f, 0.25f, 1f);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Invalidated Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.Color = new Color(0.25f, 0.5f, 1f, 1f);
            point.Intensity = 2f;
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            point.AreaCookieAverageReadbackPending = true;
            point.AreaCookieAverageReadbackDirty = false;
            point.AreaCookieAverageCustomId = -1;
            manager.CompleteAreaCookieAverageReadback(point, true, staleAverageColor);

            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, Color.white), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            Assert.That(point.AreaCookieAverageReadbackPending, Is.False);
            Assert.That(point.AreaCookieAverageCustomId, Is.EqualTo(-1));
        }

        // A callback owned by a removed area light must not write into a custom ID later reused by another light.
        [Test]
        public void DeinitializedAreaCookieReadbackCannotPatchReplacementLight() {
            LightVolumeManager manager = CreateManager("Area Cookie Removed Readback Manager", false);
            Texture2D oldSource = CreateTexture2D("Area Removed Cookie Source");
            Texture2D replacementSource = CreateTexture2D("Area Replacement Cookie Source");
            Color replacementAverage = new Color(0.125f, 0.375f, 0.75f, 1f);
            Color staleAverage = new Color(0.9f, 0.1f, 0.2f, 1f);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance oldPoint = CreatePointLight(manager, "Area Removed Cookie Light", true);
            oldPoint.SetCustomTexture();
            oldPoint.SetAreaLight();
            oldPoint.CustomTexture = oldSource;
            oldPoint.ProjectionType = 1;
            manager.PointLightVolumeInstances = new[] { oldPoint };
            manager.ReinitializeCustomTextures();

            oldPoint.AreaCookieAverageReadbackPending = true;
            oldPoint.AreaCookieAverageReadbackDirty = true;
            oldPoint.AreaCookieAverageCustomId = 0;
            manager.DeinitializePointLightVolume(oldPoint, true, false);

            Assert.That(oldPoint.AreaCookieAverageReadbackPending, Is.True);
            Assert.That(oldPoint.AreaCookieAverageReadbackDirty, Is.False);
            Assert.That(oldPoint.AreaCookieAverageCustomId, Is.EqualTo(-1));

            PointLightVolumeInstance replacement = CreatePointLight(manager, "Area Replacement Cookie Light", true);
            replacement.SetCustomTexture();
            replacement.SetAreaLight();
            replacement.CustomTexture = replacementSource;
            replacement.ProjectionType = 1;
            manager.PointLightVolumeInstances = new[] { replacement };
            manager.ReinitializeCustomTextures();
            UploadAreaCookieAverageColor(manager, 0, replacementAverage);

            manager.CompleteAreaCookieAverageReadback(oldPoint, true, staleAverage);

            Assert.That(oldPoint.AreaCookieAverageReadbackPending, Is.False);
            AssertVectorClose(new Vector4(replacementAverage.r, replacementAverage.g, replacementAverage.b, replacementAverage.a), GetAreaCookieAverageColor(manager, 0));
            AssertVectorClose(new Vector4(replacementAverage.r, replacementAverage.g, replacementAverage.b, replacementAverage.a), replacement.AreaLightFallbackColor);
        }

        // Verifies a dirty stale area-cookie readback completes cleanly and lets the retry path rebuild the source cache.
        [Test]
        public void DirtyAreaCookieReadbackCompletesAndQueuesRetry() {
            LightVolumeManager manager = CreateManager("Area Cookie Source Swap Readback Manager", false);
            Texture2D source = CreateTexture2D("Area Source Swap Cookie Source");
            Texture2D replacementSource = CreateTexture2D("Area Source Swap Replacement Cookie Source");
            Color staleAverageColor = new Color(0.875f, 0.125f, 0.25f, 1f);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Source Swap Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.Color = new Color(0.25f, 0.5f, 1f, 1f);
            point.Intensity = 2f;
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            point.AreaCookieAverageReadbackPending = true;
            point.AreaCookieAverageReadbackDirty = true;
            point.AreaCookieAverageCustomId = 0;
            point.CustomTexture = replacementSource;
            replacementSource.SetPixel(0, 0, Color.black);
            replacementSource.Apply(false);

            manager.CompleteAreaCookieAverageReadback(point, true, staleAverageColor);

            Assert.That(point.AreaCookieAverageReadbackPending, Is.False);
            Assert.That(point.AreaCookieAverageCustomId, Is.EqualTo(-1));
            Color retryReadbackColor = GetAreaCookieAverageColor(manager, 0);
            Assert.That(retryReadbackColor.r, Is.LessThan(0.1f));
            AssertVectorClose(ExpectedAreaCookieFallbackColor(point, retryReadbackColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
        }

        // Verifies source-cache rebuild marks a pending area-cookie readback dirty so completion retries once.
        [Test]
        public void AreaCookieSourceRebuildMarksPendingReadbackDirty() {
            LightVolumeManager manager = CreateManager("Area Cookie Pending Source Change Manager", false);
            Texture2D source = CreateTexture2D("Area Pending Cookie Source");
            Texture2D replacementSource = CreateTexture2D("Area Pending Cookie Replacement Source");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Area Pending Cookie Light", true);
            point.transform.localScale = new Vector3(2, 3, 1);
            point.SetCustomTexture();
            point.SetAreaLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();

            point.AreaCookieAverageReadbackPending = true;
            point.AreaCookieAverageReadbackDirty = false;
            point.AreaCookieAverageCustomId = 0;
            point.CustomTexture = replacementSource;

            manager.ReinitializeCustomTextures();

            Assert.That(point.AreaCookieAverageReadbackPending, Is.True);
            Assert.That(point.AreaCookieAverageReadbackDirty, Is.True);
            Assert.That(point.AreaCookieAverageCustomId, Is.EqualTo(0));
        }

        // Verifies area cookie readback does not live-patch spot lights that share the same single-slice source ID.
        [Test]
        public void AreaCookieFallbackReadbackDoesNotPatchSharedSpotCookie() {
            LightVolumeManager manager = CreateManager("Area Cookie Shared Spot Manager", false);
            Texture2D source = CreateTexture2D("Area Cookie Shared Spot Source");
            Color averageColor = new Color(0.25f, 0.5f, 0.75f, 1f);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance area = CreatePointLight(manager, "Area Cookie Shared Source Light", true);
            area.transform.localScale = new Vector3(2, 3, 1);
            area.SetCustomTexture();
            area.SetAreaLight();
            area.CustomTexture = source;
            area.ProjectionType = 1; // 1: texture

            PointLightVolumeInstance spot = CreatePointLight(manager, "Wide Spot Shared Cookie Light", true);
            spot.SetCustomTexture();
            spot.SetSpotLight(150, 0.25f);
            spot.CustomTexture = source;
            spot.ProjectionType = 1; // 1: texture

            manager.PointLightVolumeInstances = new[] { area, spot };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();
            UploadAreaCookieAverageColor(manager, 0, averageColor);

            Vector4[] colors = Shader.GetGlobalVectorArray(_pointLightColorID);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(area, averageColor), colors[0]);
            AssertVectorClose(ExpectedPointLightColor(spot), colors[1]);
        }

        // Verifies area light material cookies reuse matching material sources with mipmapped projection data.
        [Test]
        public void AreaMaterialCookieDeduplicatesRuntimeArrayAndShaderIds() {
            LightVolumeManager manager = CreateManager("Area Material Cookie Runtime Manager", false);
            Material material = CreateMaterial("Hidden/CubeFace");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "Area Material Cookie A", true);
            firstPoint.transform.localScale = new Vector3(2, 3, 1);
            firstPoint.SetCustomMaterial(material, true);
            firstPoint.SetAreaLight();

            PointLightVolumeInstance secondPoint = CreatePointLight(manager, "Area Material Cookie B", true);
            secondPoint.transform.localScale = new Vector3(2, 3, 1);
            secondPoint.Color = Color.red;
            secondPoint.SetCustomMaterial(material, true);
            secondPoint.SetAreaLight();

            manager.PointLightVolumeInstances = new[] { firstPoint, secondPoint };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(manager.CubemapsCount, Is.EqualTo(0));
            Assert.That(GetManagerField<int>(manager, _customSingleTextureCountField), Is.EqualTo(0));
            Assert.That(GetManagerField<int>(manager, _customSingleMaterialCountField), Is.EqualTo(1));
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(1));
            Assert.That(manager.CustomTextures.useMipMap, Is.True);
            Assert.That(manager.CustomTextures.autoGenerateMips, Is.False);
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, 0 }));
            AssertPointCustomData(0, firstPoint, -1, 0);
            AssertPointCustomData(1, secondPoint, -1, 0);
        }

        // Verifies deinitializing one material-cookie area light does not clear another material-cookie area's fallback average.
        [Test]
        public void AreaMaterialCookieFallbackSurvivesOtherAreaDeinitialize() {
            LightVolumeManager manager = CreateManager("Area Material Cookie Deinitialize Manager", false);
            Material firstMaterial = CreateMaterial("Hidden/CubeFace");
            Material secondMaterial = CreateMaterial("Hidden/CubeFace");
            Color firstAverageColor = new Color(0.125f, 0.25f, 0.5f, 1f);
            Color secondAverageColor = new Color(0.5f, 0.25f, 0.125f, 1f);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "Area Material Cookie Deinit A", true);
            firstPoint.transform.localScale = new Vector3(2, 3, 1);
            firstPoint.SetCustomMaterial(firstMaterial, true);
            firstPoint.SetAreaLight();

            PointLightVolumeInstance secondPoint = CreatePointLight(manager, "Area Material Cookie Deinit B", true);
            secondPoint.transform.localScale = new Vector3(2, 3, 1);
            secondPoint.Color = new Color(1f, 0.5f, 0.25f, 1f);
            secondPoint.SetCustomMaterial(secondMaterial, true);
            secondPoint.SetAreaLight();

            manager.PointLightVolumeInstances = new[] { firstPoint, secondPoint };
            manager.ReinitializeCustomTextures();
            UploadAreaCookieAverageColor(manager, 0, firstAverageColor);
            UploadAreaCookieAverageColor(manager, 1, secondAverageColor);

            secondPoint.AreaCookieAverageReadbackPending = true;
            secondPoint.AreaCookieAverageReadbackDirty = false;
            secondPoint.AreaCookieAverageCustomId = 0;
            manager.DeinitializePointLightVolume(firstPoint, true, false);
            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(secondPoint.AreaCookieAverageReadbackPending, Is.True);
            AssertVectorClose(ExpectedAreaCookieFallbackColor(secondPoint, secondAverageColor), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            secondPoint.AreaCookieAverageReadbackPending = false;
            secondPoint.AreaCookieAverageReadbackDirty = false;
            secondPoint.AreaCookieAverageCustomId = -1;
        }

        // Verifies a shared material source is split when lights need different runtime auto-update behavior.
        [Test]
        public void AreaMaterialCookieAutoUpdateMismatchUsesSeparateRuntimeSlices() {
            LightVolumeManager manager = CreateManager("Area Material Cookie Auto Update Split Manager", false);
            Material material = CreateMaterial("Hidden/CubeFace");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance livePoint = CreatePointLight(manager, "Area Material Cookie Live", true);
            livePoint.transform.localScale = new Vector3(2, 3, 1);
            livePoint.SetCustomMaterial(material, true);
            livePoint.SetAreaLight();

            PointLightVolumeInstance snapshotPoint = CreatePointLight(manager, "Area Material Cookie Snapshot", true);
            snapshotPoint.transform.localScale = new Vector3(2, 3, 1);
            snapshotPoint.SetCustomMaterial(material, false);
            snapshotPoint.SetAreaLight();

            manager.PointLightVolumeInstances = new[] { livePoint, snapshotPoint };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(GetManagerField<int>(manager, _customSingleMaterialCountField), Is.EqualTo(2));
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(2));
            Assert.That(manager.HasAutoCustomTextureUpdates, Is.True);
            Assert.That(GetManagerField<bool[]>(manager, _customSingleMaterialAutoUpdatesField), Is.EqualTo(new[] { true, false }));
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, 1 }));
            AssertPointCustomData(0, livePoint, -1, 0);
            AssertPointCustomData(1, snapshotPoint, -2, 0);
        }

        // Verifies the runtime API assigns a texture source and refreshes manager-owned projection arrays
        [Test]
        public void CustomTextureApiAssignsTextureAndRefreshesRuntimeArray() {
            LightVolumeManager manager = CreateManager("Custom Texture API Manager", false);
            RenderTexture source = CreateRenderTexture("Custom Texture API Source", 4, 4, 1, TextureDimension.Tex2D);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Custom Texture API Spot", true);
            point.SetSpotLight(60, 0.5f);
            point.SetCustomTexture(source, false, true);
            manager.UpdateVolumes();

            Assert.That(point.CustomTexture, Is.SameAs(source));
            Assert.That(point.CustomTextureMaterial, Is.Null);
            Assert.That(point.ProjectionType, Is.EqualTo(1)); // 1: texture
            Assert.That(point.ProjectionMode, Is.EqualTo(2)); // 2: custom cookie or cubemap
            Assert.That(point.AutoUpdateCustomTexture, Is.True);
            Assert.That(point.CustomTextureIsCubemap, Is.False);
            Assert.That(point.CustomTextureHasDepthSlices, Is.False);
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(Shader.GetGlobalTexture(_pointLightTextureID), Is.SameAs(manager.CustomTextures));
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(1));
            AssertPointCustomData(point, -1, 0);

            point.SetCustomTexture(null, false, false);
            manager.UpdateVolumes();

            Assert.That(point.CustomTexture, Is.Null);
            Assert.That(point.ProjectionType, Is.EqualTo(0)); // 0: none
            Assert.That(point.ProjectionMode, Is.EqualTo(0)); // 0: parametric
            Assert.That(manager.CustomTextures, Is.Null);
        }

        // Verifies the runtime API uses isCubemap to mark cubemap texture sources
        [Test]
        public void CustomTextureApiMarksCubemapSources() {
            LightVolumeManager manager = CreateManager("Custom Texture API Cubemap Manager", false);
            Cubemap source = CreateCubemap("Custom Texture API Cubemap Source");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Custom Texture API Point", true);
            point.SetCustomTexture(source, true, true);
            manager.UpdateVolumes();

            Assert.That(point.CustomTexture, Is.SameAs(source));
            Assert.That(point.CustomTextureIsCubemap, Is.True);
            Assert.That(point.CustomTextureHasDepthSlices, Is.False);
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CubemapsCount, Is.EqualTo(1));
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(6));
            AssertPointCustomData(point, -1, 0);
        }

        // Verifies the runtime API assigns a material source and refreshes manager-owned projection arrays
        [Test]
        public void CustomMaterialApiAssignsMaterialAndRefreshesRuntimeArray() {
            LightVolumeManager manager = CreateManager("Custom Material API Manager", false);
            Material material = CreateMaterial("Hidden/CubeFace");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "Material API Point A", true);
            firstPoint.SetCustomMaterial(material, true);

            PointLightVolumeInstance duplicatePoint = CreatePointLight(manager, "Material API Point B", true);
            duplicatePoint.SetCustomMaterial(material, true);

            Assert.That(firstPoint.CustomTexture, Is.Null);
            Assert.That(firstPoint.CustomTextureMaterial, Is.SameAs(material));
            Assert.That(firstPoint.ProjectionType, Is.EqualTo(2)); // 2: material
            Assert.That(firstPoint.ProjectionMode, Is.EqualTo(2)); // 2: custom cookie or cubemap
            Assert.That(firstPoint.AutoUpdateCustomTexture, Is.True);

            manager.UpdateVolumes();

            Assert.That(manager.CubemapsCount, Is.EqualTo(1));
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(6));
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, 0 }));
            AssertPointCustomData(0, firstPoint, -1, 0);
            AssertPointCustomData(1, duplicatePoint, -1, 0);
        }

        // Verifies runtime cookie size comes from the manager setting, not from the source texture
        [Test]
        public void SpotCookieRuntimeArrayUsesConfiguredSize() {
            LightVolumeManager manager = CreateManager("Spot Cookie Configured Size Manager", false);
            RenderTexture source = CreateRenderTexture("Animated Spot Cookie No Fallback Source", 8, 4, 1, TextureDimension.Tex2D);
            manager.CustomTexturesWidth = 16;
            manager.CustomTexturesHeight = 8;

            PointLightVolumeInstance point = CreatePointLight(manager, "Animated Spot Cookie No Fallback Light", true);
            point.SetCustomTexture();
            point.SetSpotLight(60, 0.5f);
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            point.AutoUpdateCustomTexture = true;
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(Shader.GetGlobalTexture(_pointLightTextureID), Is.SameAs(manager.CustomTextures));
            Assert.That(manager.CustomTextures.width, Is.EqualTo(16));
            Assert.That(manager.CustomTextures.height, Is.EqualTo(8));
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(1));
            Assert.That(manager.CustomTextures.format, Is.EqualTo(RenderTextureFormat.ARGBHalf));
            AssertPointCustomData(point, -1, 0);
        }

        // Verifies animated point cubemap cookies target a reserved six-slice cubemap range.
        [Test]
        public void AnimatedPointCubemapUsesReservedCubemapSliceRange() {
            LightVolumeManager manager = CreateManager("Animated Point Cubemap Manager", false);
            RenderTexture source = CreateRenderTexture("Animated Point Cubemap Source", 4, 4, 6, TextureDimension.Tex2DArray);
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Animated Point Cubemap Light", true);
            point.SetCustomTexture();
            point.SetPointLight();
            point.CustomTexture = source;
            point.ProjectionType = 1; // 1: texture
            point.CustomTextureHasDepthSlices = true;
            point.AutoUpdateCustomTexture = true;
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(6));
            Assert.That(manager.CustomTextures.format, Is.EqualTo(RenderTextureFormat.ARGBHalf));
            AssertGlobalFloat(_pointLightCubeCountID, 1);
            AssertPointCustomData(point, -1, 0);
        }

        // Verifies static shadow cubemaps build the same manager-owned runtime array as animated sources.
        [Test]
        public void ShadowCubemapCreatesRuntimeArray() {
            LightVolumeManager manager = CreateManager("Shadow Cubemap Runtime Manager", false);
            Cubemap source = CreateCubemap("Shadow Cubemap Source");
            manager.ShadowTexturesWidth = 8;
            manager.ShadowTexturesHeight = 8;

            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow Cubemap Light", true);
            ConfigureShadowTexture(point, source, false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(manager.ShadowTextures.width, Is.EqualTo(8));
            Assert.That(manager.ShadowTextures.height, Is.EqualTo(8));
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(6));
            Assert.That(manager.ShadowTextures.format, Is.EqualTo(RenderTextureFormat.ARGBFloat));
            Assert.That(manager.ShadowTextures.useMipMap, Is.False);
            Assert.That(manager.ShadowTextures.autoGenerateMips, Is.False);
            Assert.That(manager.ShadowTextures.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(Shader.GetGlobalTexture(_pointLightShadowTextureID), Is.SameAs(manager.ShadowTextures));
            AssertGlobalFloat(_pointLightShadowCountID, 1);
        }

        // Undo in Play Mode can restore runtime Texture2DArray and native-Cubemap flags from
        // different snapshots. Rebuild and narrow publication must trust the real texture layout.
        [Test]
        public void ShadowTextureRefreshCanonicalizesUndoRestoredRuntimeArrayLayout() {
            LightVolumeManager manager = CreateManager("Undo Shadow Layout Manager", false);
            RenderTexture source = CreateRenderTexture("Undo Runtime Shadow Array", 8, 8, 6, TextureDimension.Tex2DArray);
            manager.ShadowTexturesWidth = 8;
            manager.ShadowTexturesHeight = 8;

            PointLightVolumeInstance point = CreatePointLight(manager, "Undo Runtime Shadow Light", true);
            ConfigureShadowTexture(point, source, false, true, false);
            point.ShadowMapUsesCubemap = true;
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();

            Assert.That(point.ShadowMapTextureIsCubemap, Is.False);
            Assert.That(point.ShadowMapTextureHasDepthSlices, Is.True);
            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(6));

            point.ShadowMapTextureIsCubemap = true;
            point.ShadowMapTextureHasDepthSlices = false;
            Assert.That(manager.UpdatePointLightShadowTexture(point), Is.True);
            Assert.That(point.ShadowMapTextureIsCubemap, Is.False);
            Assert.That(point.ShadowMapTextureHasDepthSlices, Is.True);
        }

        // Verifies serialized shadow outputs cannot keep an old resolution after setup metadata changes.
        [Test]
        public void ShadowRuntimeArrayUsesConfiguredSize() {
            LightVolumeManager manager = CreateManager("Shadow Configured Size Manager", false);
            RenderTexture source = CreateRenderTexture("Shadow Configured Size Source", 8, 4, 6, TextureDimension.Tex2DArray);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 8;
            manager.ShadowTextures = CreateRenderTexture("Shadow Stale Runtime", 64, 64, 6, TextureDimension.Tex2DArray);

            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow Configured Size Light", true);
            ConfigureShadowTexture(point, source, true, false, true);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.width, Is.EqualTo(16));
            Assert.That(manager.ShadowTextures.height, Is.EqualTo(8));
            Assert.That(manager.ShadowTextures.format, Is.EqualTo(RenderTextureFormat.ARGBFloat));
            Assert.That(manager.ShadowTextures.useMipMap, Is.False);
            Assert.That(manager.ShadowTextures.autoGenerateMips, Is.False);
        }

        // Verifies destruction cleanup releases every Manager-owned runtime render texture, not
        // merely its managed references. This covers cookie/shadow arrays, froxel masks and the
        // Udon material-blit source that otherwise retain native GPU allocations.
        [Test]
        public void ManagerDestroyReleasesAllOwnedRuntimeRenderTextures() {
            LightVolumeManager manager = CreateManager("Runtime Texture Cleanup Manager", false);
            RenderTexture customTextures = CreateRenderTexture("Runtime Custom Texture Array", 32, 32, 2, TextureDimension.Tex2DArray);
            RenderTexture shadowTextures = CreateRenderTexture("Runtime Shadow Texture Array", 64, 64, 6, TextureDimension.Tex2DArray);
            RenderTexture fineClusterMask = CreateRenderTexture("Runtime Fine Cluster Mask", 16, 16, 1, TextureDimension.Tex2D);
            RenderTexture coarseClusterMask = CreateRenderTexture("Runtime Coarse Cluster Mask", 8, 8, 1, TextureDimension.Tex2D);
            RenderTexture clusteringSource = CreateRenderTexture("Runtime Clustering Source", 1, 1, 1, TextureDimension.Tex2D);
            RenderTexture dummyBlitSource = CreateRenderTexture("Runtime Dummy Blit Source", 1, 1, 1, TextureDimension.Tex2D);
            customTextures.hideFlags = HideFlags.HideAndDontSave;
            shadowTextures.hideFlags = HideFlags.HideAndDontSave;
            manager.CustomTextures = customTextures;
            manager.ShadowTextures = shadowTextures;

            FieldInfo fineClusterMaskField = typeof(LightVolumeManager).GetField("_clusterMask", _lifecycleMethodFlags);
            FieldInfo coarseClusterMaskField = typeof(LightVolumeManager).GetField("_coarseClusterMask", _lifecycleMethodFlags);
            FieldInfo clusteringSourceField = typeof(LightVolumeManager).GetField("_clusteringSource", _lifecycleMethodFlags);
            Assert.That(fineClusterMaskField, Is.Not.Null);
            Assert.That(coarseClusterMaskField, Is.Not.Null);
            Assert.That(clusteringSourceField, Is.Not.Null);
            fineClusterMaskField.SetValue(manager, fineClusterMask);
            coarseClusterMaskField.SetValue(manager, coarseClusterMask);
            clusteringSourceField.SetValue(manager, clusteringSource);
            _dummyRTField.SetValue(manager, dummyBlitSource);

            InvokeLifecycleMethod(manager, "OnDestroy");

            Assert.That(manager.CustomTextures, Is.Null);
            Assert.That(manager.ShadowTextures, Is.Null);
            Assert.That(fineClusterMaskField.GetValue(manager), Is.Null);
            Assert.That(coarseClusterMaskField.GetValue(manager), Is.Null);
            Assert.That(clusteringSourceField.GetValue(manager), Is.Null);
            Assert.That(_dummyRTField.GetValue(manager), Is.Null);
            Assert.That(customTextures == null, Is.True);
            Assert.That(shadowTextures == null, Is.True);
            Assert.That(fineClusterMask == null, Is.True);
            Assert.That(coarseClusterMask == null, Is.True);
            Assert.That(clusteringSource == null, Is.True);
            Assert.That(dummyBlitSource == null, Is.True);
        }

        // Verifies each Point Light-owned runtime shadow target is destroyed with the light.
        [Test]
        public void PointLightDestroyReleasesAllOwnedRuntimeShadowTextures() {
            PointLightVolumeInstance point = CreatePointLight(null, "Runtime Point Shadow Cleanup", false);
            RenderTexture depthTexture = CreateRenderTexture("Runtime Shadow Depth", 16, 16, 1, TextureDimension.Tex2D, RenderTextureFormat.Depth);
            RenderTexture outputTexture = CreateRenderTexture("Runtime Shadow Output", 16, 16, 6, TextureDimension.Tex2DArray);
            RenderTexture blurTexture = CreateRenderTexture("Runtime Shadow Blur", 16, 16, 6, TextureDimension.Tex2DArray);
            RenderTexture blitInputTexture = CreateRenderTexture("Runtime Shadow Blit Input", 1, 1, 1, TextureDimension.Tex2D);
            FieldInfo depthTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowDepthTexture", _lifecycleMethodFlags);
            FieldInfo outputTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            FieldInfo blurTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowBlurTempTexture", _lifecycleMethodFlags);
            FieldInfo blitInputTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowMaterialBlitInputTexture", _lifecycleMethodFlags);
            Assert.That(depthTextureField, Is.Not.Null);
            Assert.That(outputTextureField, Is.Not.Null);
            Assert.That(blurTextureField, Is.Not.Null);
            Assert.That(blitInputTextureField, Is.Not.Null);
            depthTextureField.SetValue(point, depthTexture);
            outputTextureField.SetValue(point, outputTexture);
            blurTextureField.SetValue(point, blurTexture);
            blitInputTextureField.SetValue(point, blitInputTexture);
            point.ShadowMapTexture = outputTexture;

            InvokeLifecycleMethod(point, "OnDestroy");

            Assert.That(point.ShadowMapTexture, Is.Null);
            Assert.That(depthTextureField.GetValue(point), Is.Null);
            Assert.That(outputTextureField.GetValue(point), Is.Null);
            Assert.That(blurTextureField.GetValue(point), Is.Null);
            Assert.That(blitInputTextureField.GetValue(point), Is.Null);
            Assert.That(depthTexture == null, Is.True);
            Assert.That(outputTexture == null, Is.True);
            Assert.That(blurTexture == null, Is.True);
            Assert.That(blitInputTexture == null, Is.True);
        }

        // Verifies the optional Udon TVGI integration owns and destroys its mipmapped reduction RT.
        [Test]
        public void TVGIDestroyReleasesOwnedDownsampledRenderTexture() {
            GameObject gameObject = CreateGameObject("Runtime TVGI Cleanup", false);
            LightVolumeTVGI tvgi = gameObject.AddComponent<LightVolumeTVGI>();
            tvgi.TargetLightVolumes = new LightVolumeInstance[0];
            tvgi.TargetPointLightVolumes = new PointLightVolumeInstance[0];
            FieldInfo downsampledTextureField = typeof(LightVolumeTVGI).GetField("_downsampledTex", _lifecycleMethodFlags);
            Assert.That(downsampledTextureField, Is.Not.Null);

            InvokeLifecycleMethod(tvgi, "Start");
            RenderTexture downsampledTexture = (RenderTexture)downsampledTextureField.GetValue(tvgi);
            Assert.That(downsampledTexture, Is.Not.Null);
            Assert.That(downsampledTexture.IsCreated(), Is.True);

            InvokeLifecycleMethod(tvgi, "OnDestroy");

            Assert.That(downsampledTextureField.GetValue(tvgi), Is.Null);
            Assert.That(downsampledTexture == null, Is.True);
        }

        // Verifies manager-owned runtime shadow materials do not survive their owning Manager.
        [Test]
        public void ManagerDestroyReleasesOwnedRuntimeShadowMaterials() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Material Cleanup Manager", false);
            Material depthMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            Material blurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            depthMaterial.hideFlags = HideFlags.HideAndDontSave;
            blurMaterial.hideFlags = HideFlags.HideAndDontSave;
            manager.RuntimeShadowDepthEncodeMaterial = depthMaterial;
            manager.RuntimeShadowBlurMaterial = blurMaterial;

            InvokeLifecycleMethod(manager, "OnDestroy");

            Assert.That(manager.RuntimeShadowDepthEncodeMaterial, Is.Null);
            Assert.That(manager.RuntimeShadowBlurMaterial, Is.Null);
            Assert.That(depthMaterial == null, Is.True);
            Assert.That(blurMaterial == null, Is.True);
        }

        // Verifies shadow runtime arrays use the default EVSM float format.
        [Test]
        public void ShadowRuntimeArrayUsesDefaultEVSMFloatFormat() {
            LightVolumeManager manager = CreateManager("Shadow Default Format Manager", false);
            Cubemap source = CreateCubemap("Shadow Default Format Source");
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow Fixed Format Light", true);
            ConfigureShadowTexture(point, source, false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.format, Is.EqualTo(RenderTextureFormat.ARGBFloat));
            Assert.That(manager.ShadowTextures.useMipMap, Is.False);
            Assert.That(manager.ShadowTextures.autoGenerateMips, Is.False);
        }

        // Verifies EVSM Half shadows use an ARGBHalf texture array.
        [Test]
        public void ShadowRuntimeArrayUsesConfiguredHalfFormat() {
            LightVolumeManager manager = CreateManager("Shadow EVSM Half Type Manager", false);
            Cubemap source = CreateCubemap("Shadow EVSM Half Type Source");
            manager.ShadowTextureFormat = 0;
            manager.ShadowBleedReduction = 0.35f;
            manager.ShadowMinVariance = 0.01f;
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow EVSM Half Type Light", true);
            point.WorldSpaceShadows = true;
            point.Bias = 0;
            point.NearClip = 0.25f;
            ConfigureShadowTexture(point, source, false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.format, Is.EqualTo(RenderTextureFormat.ARGBHalf));
            Assert.That(manager.ShadowTextures.useMipMap, Is.False);
            Assert.That(manager.ShadowTextures.autoGenerateMips, Is.False);
            AssertVectorClose(new Vector4(0.0001f * 5.54f, -0.35f / 0.65f, 1f / 0.65f, 0.0001f * 5f), Shader.GetGlobalVector(_pointLightShadowReceiverParamsID));
            AssertPointCustomData(point, 0, 1);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightExtraDataID)[0].w, Is.EqualTo(0.25f).Within(Epsilon));
        }

        // Keeps the precomputed EVSM constants finite even when authoring values are outside their supported range.
        [Test]
        public void DisabledManagerPublishesClampedShadowReceiverParams() {
            LightVolumeManager manager = CreateManager("Clamped Shadow Receiver Params Manager", false);
            manager.ShadowBleedReduction = 2f;
            manager.ShadowMinVariance = -4f;

            manager.UpdateVolumes();

            AssertVectorClose(new Vector4(0f, -0.999f / (1f - 0.999f), 1f / (1f - 0.999f), 0f), Shader.GetGlobalVector(_pointLightShadowReceiverParamsID));
        }

        // Verifies EVSM Float shadows use an ARGBFloat texture array.
        [Test]
        public void ShadowRuntimeArrayUsesConfiguredFloatFormat() {
            LightVolumeManager manager = CreateManager("Shadow EVSM Float Type Manager", false);
            Cubemap source = CreateCubemap("Shadow EVSM Float Type Source");
            manager.ShadowTextureFormat = 1;
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow EVSM Float Type Light", true);
            point.WorldSpaceShadows = true;
            ConfigureShadowTexture(point, source, false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.format, Is.EqualTo(RenderTextureFormat.ARGBFloat));
            Assert.That(manager.ShadowTextures.useMipMap, Is.False);
            Assert.That(manager.ShadowTextures.autoGenerateMips, Is.False);
            AssertPointCustomData(point, 0, 1);
        }

        // Verifies manager shader uploads sanitize shadow clip values without rewriting the light instance.
        [Test]
        public void PointLightShadowClipUploadClampsWithoutMutatingInstance() {
            LightVolumeManager manager = CreateManager("Shadow Clip Upload Clamp Manager", false);
            Cubemap source = CreateCubemap("Shadow Clip Upload Clamp Source");
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow Clip Upload Clamp Light", true);
            point.SquaredRange = 64f;
            point.IsRangeDirty = false;
            point.NearClip = 4f;
            point.FarClip = 1f;
            point.WorldSpaceShadows = true;
            ConfigureShadowTexture(point, source, false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            AssertPointCustomData(point, 0, 1);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightExtraDataID)[0].w, Is.EqualTo(4f).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedCustomShadowInvDepthRange(point)).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(ExpectedCubemapShadowInvDepthRange(point)).Within(Epsilon));
            Assert.That(point.NearClip, Is.EqualTo(4f).Within(Epsilon));
            Assert.That(point.FarClip, Is.EqualTo(1f).Within(Epsilon));
        }

        // Verifies a repeated runtime bake republishes the receiver depth range when only its clip planes changed.
        [Test]
        public void RuntimeShadowBakeClipChangeRefreshesReceiverMetadata() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Clip Refresh Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Clip Refresh Light", true);
            point.SquaredRange = 64f;
            point.IsRangeDirty = false;
            point.NearClip = 0.25f;
            point.FarClip = 4f;
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(point);

            point.BakeShadows();
            manager.UpdateVolumes();
            Assert.That(Shader.GetGlobalVectorArray(_pointLightExtraDataID)[0].w, Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(-1f / 3.75f).Within(Epsilon));

            point.IsRangeDirty = false;
            point.NearClip = 0.5f;
            point.FarClip = 8f;
            point.BakeShadows();

            // The edit-mode C# proxy applies RequestUpdateVolumes synchronously.
            Assert.That(Shader.GetGlobalVectorArray(_pointLightExtraDataID)[0].w, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(-1f / 7.5f).Within(Epsilon));
        }

        // Verifies runtime shadow baking uses the target light far clip data.
        [Test]
        public void RuntimeShadowBakerUsesTargetRangeForShadowFarClip() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Far Clip Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Far Clip Light", true);
            point.FarClip = 0f;
            point.Intensity = 100f;

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowResolution = 16;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            Camera shadowCamera = AddRuntimeShadowCamera(baker);

            float firstAutomaticFarClip = point.GetShadowFarClip();
            point.IsRangeDirty = true;
            baker.BakeShadows();
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(firstAutomaticFarClip).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(firstAutomaticFarClip).Within(Epsilon));

            point.Intensity = 25f;
            float secondAutomaticFarClip = point.GetShadowFarClip();
            point.IsRangeDirty = true;

            baker.BakeShadows();
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(secondAutomaticFarClip).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(secondAutomaticFarClip).Within(Epsilon));

            point.FarClip = 3;

            baker.BakeShadows();
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(3).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(3).Within(Epsilon));
        }

        // Verifies a dirty automatic range is resolved before runtime shadow depth is encoded and uploaded.
        [Test]
        public void RuntimeShadowBakerRefreshesDirtyAutomaticRangeBeforeEncoding() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Dirty Range Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Dirty Range Light", true);
            point.SetLut();
            point.SetLightSourceSize(8f);
            point.SquaredRange = 1f; // Deliberately stale value from before the LUT range change.
            point.IsRangeDirty = true;
            point.NearClip = 0.25f;
            point.FarClip = 0f;
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            Camera shadowCamera = AddRuntimeShadowCamera(point);
            const float expectedFarClip = 8f;

            point.BakeShadows();

            Assert.That(point.IsRangeDirty, Is.False);
            Assert.That(point.SquaredRange, Is.EqualTo(64f).Within(Epsilon));
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(expectedFarClip).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(expectedFarClip).Within(Epsilon));
            Assert.That(point.RuntimeShadowDepthEncodeMaterial.GetFloat("_ShadowFarClip"), Is.EqualTo(expectedFarClip).Within(Epsilon));

            manager.UpdateVolumes();
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(-1f / (expectedFarClip - 0.25f)).Within(Epsilon));
        }

        // Verifies runtime shadow baking treats FarClip as an input setting and does not publish calculated range back to the target.
        [Test]
        public void RuntimeShadowBakerDoesNotOverwriteTargetFarClip() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Published Far Clip Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Published Far Clip Light", true);
            point.FarClip = 0;
            point.Intensity = 100f;

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowResolution = 16;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            Camera shadowCamera = AddRuntimeShadowCamera(baker);

            float firstAutomaticFarClip = point.GetShadowFarClip();
            point.IsRangeDirty = true;
            baker.BakeShadows();
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(firstAutomaticFarClip).Within(Epsilon));
            Assert.That(point.FarClip, Is.EqualTo(0).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(firstAutomaticFarClip).Within(Epsilon));

            point.Intensity = 25f;
            float secondAutomaticFarClip = point.GetShadowFarClip();
            point.IsRangeDirty = true;
            baker.BakeShadows();
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(secondAutomaticFarClip).Within(Epsilon));
            Assert.That(point.FarClip, Is.EqualTo(0).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(secondAutomaticFarClip).Within(Epsilon));

            point.FarClip = 5;
            baker.BakeShadows();
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(5).Within(Epsilon));
            Assert.That(point.FarClip, Is.EqualTo(5).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(5).Within(Epsilon));
        }

        // Verifies automatic baking publishes its resolved far clip without replacing the 0 authoring mode.
        [Test]
        public void AutomaticShadowBakeKeepsFarClipSettingAtZero() {
            LightVolumeManager manager = CreateManager("Editor Shadow Far Clip Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Editor Shadow Far Clip Light", true);
            point.NearClip = 0.25f;
            point.FarClip = 0f;
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            Camera shadowCamera = AddRuntimeShadowCamera(point);
            float expectedFarClip = point.GetShadowFarClip();

            point.BakeShadows();

            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(expectedFarClip).Within(Epsilon));
            Assert.That(point.FarClip, Is.EqualTo(0f).Within(Epsilon));
            Assert.That(point.BakedFarClip, Is.EqualTo(expectedFarClip).Within(Epsilon));
            manager.UpdateVolumes();
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(-1f / (expectedFarClip - 0.25f)).Within(Epsilon));
        }

        // Verifies runtime shadow baking clamps unsafe bake inputs locally without normalizing public fields.
        [Test]
        public void RuntimeShadowBakerClampsBakeInputsWithoutMutatingTargetFields() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Clamp Inputs Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Clamp Inputs Light", true);
            point.SquaredRange = 4f;
            point.IsRangeDirty = false;
            point.NearClip = -2f;
            point.FarClip = -1f;
            point.Bias = -0.25f;
            point.Blur = -1f;
            point.ContactHardening = 2f;

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowResolution = 16;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            baker.RuntimeShadowBlurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            Camera shadowCamera = AddRuntimeShadowCamera(baker);

            baker.BakeShadows();

            Assert.That(shadowCamera.nearClipPlane, Is.EqualTo(0.0001f).Within(Epsilon));
            Assert.That(shadowCamera.farClipPlane, Is.EqualTo(2f).Within(Epsilon));
            Assert.That(baker.RuntimeShadowDepthEncodeMaterial.GetFloat("_ShadowNearClip"), Is.EqualTo(0.0001f).Within(Epsilon));
            Assert.That(baker.RuntimeShadowDepthEncodeMaterial.GetFloat("_ShadowFarClip"), Is.EqualTo(2f).Within(Epsilon));
            Assert.That(baker.RuntimeShadowDepthEncodeMaterial.GetFloat("_ShadowBakeBias"), Is.EqualTo(0f).Within(Epsilon));
            Assert.That(point.NearClip, Is.EqualTo(-2f).Within(Epsilon));
            Assert.That(point.FarClip, Is.EqualTo(-1f).Within(Epsilon));
            Assert.That(point.Bias, Is.EqualTo(-0.25f).Within(Epsilon));
            Assert.That(point.Blur, Is.EqualTo(-1f).Within(Epsilon));
            Assert.That(point.ContactHardening, Is.EqualTo(2f).Within(Epsilon));

            point.Blur = 2f;
            baker.BakeShadows();

            Assert.That(baker.RuntimeShadowBlurMaterial.GetFloat("_BlurRadius"), Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(baker.RuntimeShadowBlurMaterial.GetFloat("_BlurDepth"), Is.EqualTo(1f).Within(Epsilon));
            Assert.That(point.Blur, Is.EqualTo(2f).Within(Epsilon));
            Assert.That(point.ContactHardening, Is.EqualTo(2f).Within(Epsilon));
        }

        // Verifies runtime shadow baking uses the target light bias so it matches editor shadow bakes.
        [Test]
        public void RuntimeShadowBakerUsesTargetBakeBias() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Bias Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Bias Light", true);
            point.Bias = 0;

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowResolution = 16;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(baker);

            baker.BakeShadows();
            Assert.That(baker.RuntimeShadowDepthEncodeMaterial.GetFloat("_ShadowBakeBias"), Is.EqualTo(0).Within(Epsilon));

            point.Bias = 0.125f;

            baker.BakeShadows();
            Assert.That(baker.RuntimeShadowDepthEncodeMaterial.GetFloat("_ShadowBakeBias"), Is.EqualTo(0.125f).Within(Epsilon));
        }

        // Verifies runtime shadow baking reads camera and blur settings from the target light instance.
        [Test]
        public void RuntimeShadowBakerUsesTargetBakeSettings() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Settings Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Settings Light", true);
            point.NearClip = 0.25f;
            point.LayerMask = 1 << 7;
            point.Blur = 6.5f;
            point.ContactHardening = 0.35f;

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowSphericalBlur = true;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            baker.RuntimeShadowBlurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            Camera shadowCamera = AddRuntimeShadowCamera(baker);

            baker.BakeShadows();

            Assert.That(shadowCamera.nearClipPlane, Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(shadowCamera.cullingMask, Is.EqualTo(1 << 7));
            Assert.That(baker.RuntimeShadowBlurMaterial.GetFloat("_BlurRadius"), Is.EqualTo(6.5f).Within(Epsilon));
            Assert.That(baker.RuntimeShadowBlurMaterial.GetFloat("_BlurDepth"), Is.EqualTo((Mathf.Pow(10f, 0.35f) - 1f) * 0.1111111111f).Within(Epsilon));
            Assert.That(baker.RuntimeShadowBlurMaterial.IsKeywordEnabled("VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL"), Is.True);
        }

        // Verifies every runtime bake reconfigures the shared camera from the current light settings.
        [Test]
        public void RuntimeShadowBakerReconfiguresSharedCameraBetweenLights() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Shared Camera Config Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            manager.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            manager.EnsureRuntimeShadowCamera();
            Camera sharedCamera = manager.RuntimeShadowCamera;

            PointLightVolumeInstance first = CreatePointLight(manager, "Runtime Shadow Shared Camera First", true);
            PointLightVolumeInstance second = CreatePointLight(manager, "Runtime Shadow Shared Camera Second", true);
            manager.PointLightVolumeInstances = new[] { first, second };

            first.RuntimeShadowResolution = 16;
            first.LayerMask = 1 << 5;
            first.NearClip = 0.25f;
            first.FarClip = 4f;
            first.Blur = 0f;

            second.RuntimeShadowResolution = 16;
            second.LayerMask = 1 << 7;
            second.NearClip = 0.5f;
            second.FarClip = 8f;
            second.Blur = 0f;

            first.BakeShadows();
            Assert.That(sharedCamera.cullingMask, Is.EqualTo(1 << 5));
            Assert.That(sharedCamera.nearClipPlane, Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(sharedCamera.farClipPlane, Is.EqualTo(4f).Within(Epsilon));

            second.BakeShadows();
            Assert.That(sharedCamera.cullingMask, Is.EqualTo(1 << 7));
            Assert.That(sharedCamera.nearClipPlane, Is.EqualTo(0.5f).Within(Epsilon));
            Assert.That(sharedCamera.farClipPlane, Is.EqualTo(8f).Within(Epsilon));

            first.BakeShadows();
            Assert.That(sharedCamera.cullingMask, Is.EqualTo(1 << 5));
            Assert.That(sharedCamera.nearClipPlane, Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(sharedCamera.farClipPlane, Is.EqualTo(4f).Within(Epsilon));
        }

        // Verifies realtime baking keeps the baker resolution separate from the manager-owned final array size.
        [Test]
        public void RuntimeShadowBakerResolutionDoesNotOverrideManagerArraySize() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Resolution Manager", false);
            manager.ShadowTexturesWidth = 32;
            manager.ShadowTexturesHeight = 32;
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Resolution Light", true);

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowResolution = 96;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(baker);

            FieldInfo shadowTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            Assert.That(shadowTextureField, Is.Not.Null);

            baker.BakeShadows();

            RenderTexture shadowTexture = (RenderTexture)shadowTextureField.GetValue(baker);
            Assert.That(shadowTexture, Is.Not.Null);
            Assert.That(shadowTexture.width, Is.EqualTo(96));
            Assert.That(shadowTexture.height, Is.EqualTo(96));
            Assert.That(manager.ShadowTexturesWidth, Is.EqualTo(32));
            Assert.That(manager.ShadowTexturesHeight, Is.EqualTo(32));
        }

        // Reproduces the problematic 16x upscale ratio (32 -> 512) with a compact fixture.
        // Correct cubemap filtering uses the source texel footprint, so the adjacent face affects
        // eight destination pixels rather than only the outermost half destination texel.
        [Test]
        public void RuntimeShadowCubemapUpscaleUsesSourceTexelFootprintAcrossFaces() {
            const int sourceResolution = 4;
            const int destinationResolution = 64;
            LightVolumeManager manager = CreateManager("Runtime Shadow Source-Footprint Resample Manager", false);
            manager.ShadowTexturesWidth = destinationResolution;
            manager.ShadowTexturesHeight = destinationResolution;
            manager.RuntimeShadowBlurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");

            Color[] faceColors = { Color.red, Color.black, Color.black, Color.black, Color.black, Color.blue };
            Texture2DArray source = CreateSliceColorTextureArray("Runtime Shadow Source-Footprint Cubemap", sourceResolution, sourceResolution, faceColors);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Source-Footprint Light", true);
            ConfigureShadowTexture(point, source, false, false, true);
            point.ShadowMapUsesCubemap = true;
            point.Shadows = true;
            manager.PointLightVolumeInstances = new[] { point };

            Assert.That(manager.UpdatePointLightShadowTexture(point), Is.True);
            AssertSourceFootprintCubemapResample(ReadRenderTextureArrayPixels(manager.ShadowTextures), destinationResolution);
        }

        // Runs the same 16x upscale through compiled Udon and VRCGraphics rather than the managed
        // editor fallback, guarding the shader-pass and destination-slice calling convention.
        [UnityTest]
        public IEnumerator RuntimeUdonShadowCubemapUpscaleUsesSourceTexelFootprintAcrossFaces() {
            const int sourceResolution = 4;
            const int destinationResolution = 64;
            GameObject managerObject = CreateGameObject("Runtime Udon Source-Footprint Manager", true);
            GameObject pointObject = CreateGameObject("Runtime Udon Source-Footprint Light", true);
            LightVolumeManager manager = managerObject.AddUdonSharpComponent<LightVolumeManager>();
            PointLightVolumeInstance point = pointObject.AddUdonSharpComponent<PointLightVolumeInstance>();
            manager.ShadowTexturesWidth = destinationResolution;
            manager.ShadowTexturesHeight = destinationResolution;
            manager.RuntimeShadowBlurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");

            Color[] faceColors = { Color.red, Color.black, Color.black, Color.black, Color.black, Color.blue };
            Texture2DArray source = CreateSliceColorTextureArray("Runtime Udon Source-Footprint Cubemap", sourceResolution, sourceResolution, faceColors);
            point.LightVolumeManager = manager;
            point.IsActive = true;
            point.Intensity = 1f;
            point.ShadingStrength = 1f;
            point.LightType = 0;
            point.Shadows = true;
            ConfigureShadowTexture(point, source, false, false, true);
            point.ShadowMapUsesCubemap = true;
            manager.PointLightVolumeInstances = new[] { point };
            UdonSharpEditorUtility.CopyProxyToUdon(point);
            UdonSharpEditorUtility.CopyProxyToUdon(manager);

            yield return new EnterPlayMode();
            yield return null;

            managerObject = GameObject.Find("Runtime Udon Source-Footprint Manager");
            Assert.That(managerObject, Is.Not.Null);
            manager = managerObject.GetComponent<LightVolumeManager>();
            var managerBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(manager);
            Assert.That(managerBacking, Is.Not.Null);
            managerBacking.SendCustomEvent(nameof(LightVolumeManager.ReinitializeShadowTextures));
            yield return null;

            RenderTexture atlas = managerBacking.GetProgramVariable("ShadowTextures") as RenderTexture;
            Assert.That(atlas, Is.Not.Null);
            Assert.That(atlas.width, Is.EqualTo(destinationResolution));
            AssertSourceFootprintCubemapResample(ReadRenderTextureArrayPixels(atlas), destinationResolution);

            LogAssert.ignoreFailingMessages = true;
            yield return new ExitPlayMode();
            LogAssert.ignoreFailingMessages = false;
            DestroyTestObject(GameObject.Find("Runtime Udon Source-Footprint Light"));
            DestroyTestObject(GameObject.Find("Runtime Udon Source-Footprint Manager"));
        }

        // A normal rebake reuses its persistent source and refreshes only this light's final atlas range.
        [Test]
        public void RuntimeShadowNormalBakeReusesSourceAndUpdatesOnlyOwnedAtlasRange() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Narrow Update Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            manager.ShadowTextureFormat = 0;

            Color[] targetColors = { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.white };
            Color[] neighbourColors = { Color.black, Color.gray, Color.magenta, Color.red, Color.green, Color.blue };
            Texture2DArray targetSource = CreateSliceColorTextureArray("Runtime Shadow Previous Target", 16, 16, targetColors);
            Texture2DArray neighbourSource = CreateSliceColorTextureArray("Runtime Shadow Neighbour", 16, 16, neighbourColors);

            PointLightVolumeInstance target = CreatePointLight(manager, "Runtime Shadow Narrow Update Target", true);
            ConfigureShadowTexture(target, targetSource, true, false, true);
            target.ShadowMapUsesCubemap = true;
            target.Shadows = true;
            target.LayerMask = 0;
            target.FarClip = 4f;
            target.Blur = 0f;
            target.RuntimeShadowResolution = 16;
            target.RuntimeShadowDirectOutput = false;
            target.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(target);

            PointLightVolumeInstance neighbour = CreatePointLight(manager, "Runtime Shadow Narrow Update Neighbour", true);
            ConfigureShadowTexture(neighbour, neighbourSource, false, false, true);
            neighbour.ShadowMapUsesCubemap = true;
            neighbour.Shadows = true;
            manager.PointLightVolumeInstances = new[] { target, neighbour };
            manager.ReinitializeShadowTextures();

            FieldInfo sourceField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            Assert.That(sourceField, Is.Not.Null);
            Assert.That(manager.HasAutoShadowTextureUpdates, Is.True, "The fixture did not cache the authored source as auto-updated.");

            target.BakeShadows();

            RenderTexture persistentSource = sourceField.GetValue(target) as RenderTexture;
            RenderTexture publishedAtlas = manager.ShadowTextures;
            int targetBaseSlice = (int)target.ShadowMapID * 6;
            int neighbourBaseSlice = (int)neighbour.ShadowMapID * 6;
            Assert.That(persistentSource, Is.Not.Null);
            Assert.That(target.ShadowMapTexture, Is.SameAs(persistentSource));
            Assert.That(persistentSource.width, Is.EqualTo(16));
            Assert.That(persistentSource.height, Is.EqualTo(16));
            Assert.That(persistentSource.volumeDepth, Is.EqualTo(6));
            Assert.That(persistentSource.format, Is.EqualTo(RenderTextureFormat.ARGBHalf));
            Assert.That(publishedAtlas, Is.Not.Null);
            Assert.That(target.AutoUpdateShadowMap, Is.False);
            Assert.That(manager.HasAutoShadowTextureUpdates, Is.False, "The replaced authored auto-update source kept the runtime update loop alive.");

            for (int face = 0; face < 6; face++)
                FillRenderTextureArraySlice(publishedAtlas, targetBaseSlice + face, new Color(0.13f + face * 0.05f, 0.37f, 0.89f, 1f));
            Color[][] pixelsBeforeAutoUpdate = ReadRenderTextureArrayPixels(publishedAtlas);

            manager.UpdateAutoShadowTextures();

            Color[][] pixelsAfterAutoUpdate = ReadRenderTextureArrayPixels(publishedAtlas);
            for (int face = 0; face < 6; face++)
                AssertPixelArraysEqual(pixelsBeforeAutoUpdate[targetBaseSlice + face], pixelsAfterAutoUpdate[targetBaseSlice + face],
                    "Auto shadow refresh restored the superseded authored source face " + face);

            for (int face = 0; face < 6; face++) {
                Color targetSentinel = new Color(0.91f, 0.07f + face * 0.03f, 0.73f, 1f);
                FillRenderTextureArraySlice(persistentSource, face, targetSentinel);
                FillRenderTextureArraySlice(publishedAtlas, targetBaseSlice + face, targetSentinel);
                FillRenderTextureArraySlice(publishedAtlas, neighbourBaseSlice + face, new Color(0.03f, 0.82f, 0.19f, 1f));
            }
            Color[][] pixelsBeforeRebake = ReadRenderTextureArrayPixels(publishedAtlas);

            target.transform.position = new Vector3(2f, 3f, 4f);
            target.BakeShadows();

            Assert.That(sourceField.GetValue(target), Is.SameAs(persistentSource));
            Assert.That(target.ShadowMapTexture, Is.SameAs(persistentSource));
            Assert.That(manager.ShadowTextures, Is.SameAs(publishedAtlas));
            Color[][] pixelsAfterRebake = ReadRenderTextureArrayPixels(publishedAtlas);
            for (int face = 0; face < 6; face++) {
                Assert.That(PixelArraysDiffer(pixelsBeforeRebake[targetBaseSlice + face], pixelsAfterRebake[targetBaseSlice + face]), Is.True,
                    "The complete normal rebake did not rewrite owned face " + face);
                AssertPixelArraysEqual(pixelsBeforeRebake[neighbourBaseSlice + face], pixelsAfterRebake[neighbourBaseSlice + face],
                    "Rebaking one light changed neighbour slice " + face);
            }
        }

        // Blur is selected once for the complete shadow and leaves only the persistent normal source alive.
        [TestCase(false, 0.35f)]
        [TestCase(true, 0f)]
        public void RuntimeShadowBlurredBakePublishesCompletePersistentSource(bool sphericalBlur, float contactHardening) {
            LightVolumeManager manager = CreateManager("Complete Blurred Runtime Shadow Manager " + sphericalBlur, false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance point = CreatePointLight(manager, "Complete Blurred Runtime Shadow Light " + sphericalBlur, true);
            point.ShadowMapUsesCubemap = true;
            point.Shadows = true;
            point.LayerMask = 0;
            point.FarClip = 4f;
            point.Blur = 2f;
            point.ContactHardening = contactHardening;
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowSphericalBlur = sphericalBlur;
            point.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            point.RuntimeShadowBlurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            AddRuntimeShadowCamera(point);
            manager.PointLightVolumeInstances = new[] { point };

            FieldInfo sourceField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            Assert.That(sourceField, Is.Not.Null);

            point.BakeShadows();

            RenderTexture source = sourceField.GetValue(point) as RenderTexture;
            Assert.That(source, Is.Not.Null);
            Assert.That(point.ShadowMapTexture, Is.SameAs(source));
            Assert.That(source.volumeDepth, Is.EqualTo(6));
            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(point.RuntimeShadowBlurMaterial.IsKeywordEnabled("VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL"), Is.EqualTo(sphericalBlur));
        }

        // Missing blur dependencies fail before replacing the currently published normal source.
        [Test]
        public void RuntimeShadowMissingBlurDependencyPreservesPreviousPublishedSource() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Missing Blur Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Missing Blur Light", true);
            RenderTexture previousSource = CreateRenderTexture("Previous Shadow Before Missing Blur", 16, 16, 6, TextureDimension.Tex2DArray);
            ConfigureShadowTexture(point, previousSource, false, false, true);
            point.ShadowMapUsesCubemap = true;
            point.Shadows = true;
            point.FarClip = 4f;
            point.Blur = 2f;
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(point);
            point.RuntimeShadowBlurMaterial = null;
            manager.PointLightVolumeInstances = new[] { point };
            manager.ReinitializeShadowTextures();
            RenderTexture previousAtlas = manager.ShadowTextures;

            point.BakeShadows();

            Assert.That(point.ShadowMapTexture, Is.SameAs(previousSource));
            Assert.That(manager.ShadowTextures, Is.SameAs(previousAtlas));

            point.RuntimeShadowBlurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            point.BakeShadows();

            Assert.That(point.ShadowMapTexture, Is.Not.Null);
            Assert.That(point.ShadowMapTexture, Is.Not.SameAs(previousSource));
            Assert.That(point.ShadowMapTexture, Is.TypeOf<RenderTexture>());
        }

        // Explicit one-shot baking captures an active floor even while the component is disabled,
        // and a nonzero light still resolves its current automatic range instead of a stale bake.
        [Test]
        public void RuntimeShadowExplicitBakePreservesInvisibleLightSettings() {
            LightVolumeManager manager = CreateManager("Invisible Runtime Shadow Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance point = CreatePointLight(manager, "Invisible Runtime Shadow Light", true);
            point.FarClip = 0f;
            point.BakedFarClip = 7f;
            point.SquaredRange = 81f;
            point.IsRangeDirty = false;
            point.Blur = 0f;
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(point);
            manager.PointLightVolumeInstances = new[] { point };

            Color authoredColor = point.Color;
            float authoredIntensity = point.Intensity;
            point.enabled = false;
            Assert.That(point.gameObject.activeInHierarchy, Is.True);

            point.BakeShadows();

            Assert.That(point.enabled, Is.False);
            Assert.That(point.Color, Is.EqualTo(authoredColor));
            Assert.That(point.Intensity, Is.EqualTo(authoredIntensity));
            Assert.That(point.BakedFarClip, Is.EqualTo(9f).Within(Epsilon));
            Assert.That(point.ShadowMapTexture, Is.Not.Null);
        }

        // Zero emission has no derived automatic range, so explicit baking reuses the last usable
        // distance without temporarily changing the authored intensity or color.
        [Test]
        public void RuntimeShadowExplicitBakeReusesPriorRangeForZeroEmission() {
            LightVolumeManager manager = CreateManager("Zero Emission Runtime Shadow Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance point = CreatePointLight(manager, "Zero Emission Runtime Shadow Light", true);
            point.FarClip = 0f;
            point.BakedFarClip = 7f;
            point.Intensity = 0f;
            point.IsRangeDirty = true;
            point.Blur = 0f;
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(point);
            manager.PointLightVolumeInstances = new[] { point };

            Color authoredColor = point.Color;
            point.BakeShadows();

            Assert.That(point.Color, Is.EqualTo(authoredColor));
            Assert.That(point.Intensity, Is.Zero);
            Assert.That(point.BakedFarClip, Is.EqualTo(7f).Within(Epsilon));
            Assert.That(point.ShadowMapTexture, Is.Not.Null);
        }

        // Shadow emission is carried by RGB; transparent black must not select source-less direct output.
        [Test]
        public void RuntimeShadowTransparentBlackUsesPersistentNormalSource() {
            LightVolumeManager manager = CreateManager("Transparent Black Runtime Shadow Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance point = CreatePointLight(manager, "Transparent Black Runtime Shadow Light", true);
            point.Color = new Color(0f, 0f, 0f, 0f);
            point.Intensity = 1f;
            point.IsActive = true; // Deliberately stale to prove BakeShadows checks RGB itself.
            point.Blur = 0f;
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowDirectOutput = true;
            point.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(point);
            manager.PointLightVolumeInstances = new[] { point };

            point.BakeShadows();

            Assert.That(point.RuntimeShadowDirectOutput, Is.True);
            Assert.That(point.ShadowMapTexture, Is.InstanceOf<RenderTexture>());
            Assert.That(point.ShadowMapTextureHasDepthSlices, Is.True);
        }

        // Start queues Bake In Game work, and each manager step bakes exactly one whole light.
        [Test]
        public void BakeInGameQueueProcessesOneWholeLightPerStepFromStart() {
            LightVolumeManager manager = CreateManager("Bake In Game Whole Light Queue Manager", false, false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            manager.ShadowTextureFormat = 0;
            Material depthMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            PointLightVolumeInstance first = CreatePointLight(manager, "Bake In Game Whole Light A", true);
            PointLightVolumeInstance second = CreatePointLight(manager, "Bake In Game Whole Light B", true);
            Camera runtimeCamera = AddRuntimeShadowCamera(first);
            PointLightVolumeInstance[] points = { first, second };
            Vector3[] positions = {
                new Vector3(1f, 2f, 3f),
                new Vector3(4f, 5f, 6f)
            };
            Color[][][] queuedSourcePixels = new Color[points.Length][][];
            FieldInfo sourceField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            Assert.That(sourceField, Is.Not.Null);
            for (int i = 0; i < points.Length; i++) {
                PointLightVolumeInstance point = points[i];
                point.Shadows = true;
                point.BakeInGame = true;
                point.ShadowBakeResolution = 16;
                point.LayerMask = 0;
                point.FarClip = 4f;
                point.Blur = 0f;
                point.RuntimeShadowBlurSamplePreset = 0;
                point.RuntimeShadowSphericalBlur = false;
                point.RuntimeShadowCamera = runtimeCamera;
                point.RuntimeShadowDepthEncodeMaterial = depthMaterial;
                point.RuntimeShadowBlurMaterial = null;
                point.transform.position = positions[i];

                RenderTexture queuedSource = CreateRenderTexture("Bake In Game Face Sentinel " + i, 16, 16, 6, TextureDimension.Tex2DArray, RenderTextureFormat.ARGBHalf);
                for (int face = 0; face < 6; face++)
                    FillRenderTextureArraySlice(queuedSource, face, new Color(0.07f + face * 0.11f, 0.19f + i * 0.13f, 0.83f, 1f));
                queuedSourcePixels[i] = ReadRenderTextureArrayPixels(queuedSource);
                sourceField.SetValue(point, queuedSource);
            }
            manager.RuntimeShadowCamera = runtimeCamera;
            manager.RuntimeShadowDepthEncodeMaterial = depthMaterial;
            manager.PointLightVolumeInstances = points;

            MethodInfo start = typeof(PointLightVolumeInstance).GetMethod("Start", _lifecycleMethodFlags);
            MethodInfo onEnable = typeof(PointLightVolumeInstance).GetMethod("OnEnable", _lifecycleMethodFlags);
            MethodInfo processQueue = typeof(LightVolumeManager).GetMethod("ProcessBakeInGameQueueStep", _lifecycleMethodFlags);
            Assert.That(start, Is.Not.Null);
            Assert.That(onEnable, Is.Not.Null);
            Assert.That(processQueue, Is.Not.Null);

            onEnable.Invoke(first, null);
            Assert.That((bool)processQueue.Invoke(manager, null), Is.False, "OnEnable only registers the light; Start owns the one-shot bake request.");
            Assert.That(first.ShadowMapTexture, Is.Null);

            for (int i = 0; i < points.Length; i++) start.Invoke(points[i], null);

            Assert.That((bool)processQueue.Invoke(manager, null), Is.True);
            AssertWholeLightRuntimeShadow(first, positions[0], queuedSourcePixels[0]);
            Assert.That(second.ShadowMapTexture, Is.Null);

            Assert.That((bool)processQueue.Invoke(manager, null), Is.False);
            AssertWholeLightRuntimeShadow(second, positions[1], queuedSourcePixels[1]);
        }

        // Spot baking uses one source slice in normal mode and one isolated final slice in direct mode.
        [Test]
        public void RuntimeShadowSpotBakeUsesSingleSliceInNormalAndDirectModes() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Single Spot Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance spot = CreatePointLight(manager, "Runtime Shadow Single Spot", true);
            spot.SetSpotLight(60, 0.5f);
            spot.ShadowMapUsesCubemap = false;
            spot.Shadows = true;
            spot.LayerMask = 0;
            spot.FarClip = 4f;

            Color[] neighbourColors = { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.white };
            Texture2DArray neighbourSource = CreateSliceColorTextureArray("Runtime Shadow Spot Neighbour", 16, 16, neighbourColors);
            PointLightVolumeInstance neighbour = CreatePointLight(manager, "Runtime Shadow Spot Neighbour Light", true);
            ConfigureShadowTexture(neighbour, neighbourSource, false, false, true);
            neighbour.ShadowMapUsesCubemap = true;
            neighbour.Shadows = true;
            manager.PointLightVolumeInstances = new[] { spot, neighbour };

            PointLightVolumeInstance baker = spot;
            baker.RuntimeShadowResolution = 16;
            baker.RuntimeShadowSphericalBlur = true;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(baker);

            FieldInfo shadowTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            Assert.That(shadowTextureField, Is.Not.Null);

            baker.BakeShadows();

            RenderTexture shadowTexture = (RenderTexture)shadowTextureField.GetValue(baker);
            Assert.That(shadowTexture, Is.Not.Null);
            Assert.That(shadowTexture.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(shadowTexture.volumeDepth, Is.EqualTo(1));

            Assert.That(spot.ShadowMapTexture, Is.SameAs(shadowTexture));
            Assert.That(spot.ShadowMapUsesCubemap, Is.False);
            Assert.That(spot.ShadowMapTextureHasDepthSlices, Is.False);

            spot.RuntimeShadowDirectOutput = true;
            spot.Blur = 2f;
            spot.RuntimeShadowBlurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            spot.BakeShadows();

            RenderTexture atlas = manager.ShadowTextures;
            int spotSlice = manager.ShadowCubemapsCount * 6 + (int)spot.ShadowMapID - manager.ShadowCubemapsCount;
            int neighbourBaseSlice = (int)neighbour.ShadowMapID * 6;
            Assert.That(shadowTextureField.GetValue(spot), Is.Null);
            Assert.That(spot.ShadowMapTexture, Is.Null);
            Assert.That(atlas, Is.Not.Null);
            Assert.That(atlas.volumeDepth, Is.EqualTo(7));
            Assert.That(spot.RuntimeShadowBlurMaterial.IsKeywordEnabled("VRCLV_RUNTIME_SHADOW_BLUR_DIRECT"), Is.True);

            FillRenderTextureArraySlice(atlas, spotSlice, new Color(0.91f, 0.07f, 0.73f, 1f));
            for (int face = 0; face < 6; face++)
                FillRenderTextureArraySlice(atlas, neighbourBaseSlice + face, new Color(0.03f, 0.82f - face * 0.04f, 0.19f, 1f));
            Color[][] pixelsBeforeRebake = ReadRenderTextureArrayPixels(atlas);

            spot.transform.position = new Vector3(2f, 3f, 4f);
            spot.BakeShadows();

            Color[][] pixelsAfterRebake = ReadRenderTextureArrayPixels(atlas);
            Assert.That(PixelArraysDiffer(pixelsBeforeRebake[spotSlice], pixelsAfterRebake[spotSlice]), Is.True);
            for (int face = 0; face < 6; face++)
                AssertPixelArraysEqual(pixelsBeforeRebake[neighbourBaseSlice + face], pixelsAfterRebake[neighbourBaseSlice + face],
                    "Direct spot blur changed neighbour slice " + face);
        }

        // Verifies runtime-selected blur variants are kept in player builds instead of relying on editor-only shader_feature fallback.
        [Test]
        public void RuntimeShadowBlurShaderKeepsRuntimeSpotVariantsInBuild() {
            string shaderSource = ReadRuntimeShadowBlurShaderSource();

            Assert.That(shaderSource, Does.Contain("#pragma multi_compile_local_fragment __ VRCLV_RUNTIME_SHADOW_BLUR_DIRECT"));
            Assert.That(shaderSource, Does.Contain("#pragma multi_compile_local_fragment __ VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL"));
            Assert.That(shaderSource, Does.Contain("#if defined(VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL)"));
            Assert.That(shaderSource, Does.Not.Contain("#if defined(VRCLV_EDITOR_SHADOW_BLUR_QUALITY) || defined(VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL)"),
                "Editor sample quality must not force spherical mode when the authored toggle is disabled.");
        }

        // The SM4/GLES3.0 fallback is selected at compile time; higher targets use native bit scan without a keyword variant.
        [Test]
        public void ClusteredBitScanUsesShaderTargetInsteadOfApiVariant() {
            string shaderSource = ReadLightVolumesIncludeSource();
            int bitScanStart = shaderSource.IndexOf("inline bool LV_NextClusteredLight", StringComparison.Ordinal);
            int bitScanEnd = shaderSource.IndexOf("// Rotates vector by Quaternion", bitScanStart, StringComparison.Ordinal);

            Assert.That(shaderSource, Does.Contain("#define VRCLV_CLUSTERING_SUPPORTED 1"));
            Assert.That(shaderSource, Does.Contain("uniform Texture2D<int4> _UdonClusterMask;"));
            Assert.That(shaderSource, Does.Contain("inline void LV_LoadClusterMask"));
            Assert.That(shaderSource, Does.Contain("mask = asuint(_UdonClusterMask.Load"));
            Assert.That(shaderSource, Does.Contain("uint sequentialEnd = useClustering ? 0u : pointCount;"));
            Assert.That(shaderSource, Does.Contain("[branch] if (traversalIndex < sequentialEnd)"));
            Assert.That(bitScanStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(bitScanEnd, Is.GreaterThan(bitScanStart));
            string bitScanSource = shaderSource.Substring(bitScanStart, bitScanEnd - bitScanStart);
            Assert.That(bitScanSource, Does.Contain("#if SHADER_TARGET >= 45"));
            Assert.That(bitScanSource, Does.Not.Contain("SHADER_API_GLES3"));
            Assert.That(bitScanSource, Does.Not.Contain("#pragma multi_compile"));
        }

        // Surface-shader analysis cannot parse fastopt, while normal generated/runtime passes should retain it.
        [Test]
        public void SurfaceShaderAnalysisUsesLoopInsteadOfFastopt() {
            string shaderSource = ReadLightVolumesIncludeSource().Replace("\r\n", "\n");

            Assert.That(shaderSource, Does.Contain("#if defined(SHADER_TARGET_SURFACE_ANALYSIS)\n    #define VRCLV_DYNAMIC_LOOP [loop]\n#else\n    #define VRCLV_DYNAMIC_LOOP [fastopt]\n#endif"));
            Assert.That(shaderSource, Does.Contain("VRCLV_DYNAMIC_LOOP while"));
            Assert.That(shaderSource, Does.Contain("VRCLV_DYNAMIC_LOOP for"));
            Assert.That(shaderSource.Split(new[] { "[fastopt]" }, StringSplitOptions.None).Length - 1, Is.EqualTo(1), "fastopt must only occur in the non-surface macro branch");
        }

        // Verifies material-source blits keep _MainTex as the generator source and use a dummy texture only for Udon destination binding.
        [Test]
        public void MaterialSourceBlitPreservesMainTexInput() {
            LightVolumeManager manager = CreateManager("Material Source MainTex Manager", false);
            manager.UpdateVolumes();
            manager.CustomTextures = CreateRenderTexture("Material Source MainTex Runtime", 4, 4, 1, TextureDimension.Tex2DArray);
            Material material = CreateMaterial("Unlit/Texture");
            Texture2D mainTexture = CreateTexture2D("Material Source MainTex");
            material.SetTexture("_MainTex", mainTexture);
            MethodInfo method = typeof(LightVolumeManager).GetMethod("BlitSingleMaterial", _lifecycleMethodFlags);
            Assert.That(method, Is.Not.Null);
            Assert.That(_dummyRTField, Is.Not.Null);

            method.Invoke(manager, new object[] { material, 0, manager.CustomTextures });

            Assert.That(material.GetTexture("_MainTex"), Is.SameAs(mainTexture));
            AssertVectorClose(new Vector4(4, 4, 1, 0), material.GetVector(CustomRenderTextureInfoProperty));
            Assert.That(GetManagerField<RenderTexture>(manager, _dummyRTField), Is.Not.Null);
        }

        // Verifies runtime blur radius is normalized by resolution before shader sampling.
        [Test]
        public void RuntimeShadowBlurMaterialKeepsEffectiveRadiusStableAcrossResolution() {
            PointLightVolumeInstance baker = CreatePointLight(null, "Runtime Shadow Blur Resolution Light", false);
            Material blurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");

            MethodInfo initializeShaderPropertiesMethod = typeof(PointLightVolumeInstance).GetMethod("InitializeRuntimeShadowShaderProperties", _lifecycleMethodFlags);
            MethodInfo prepareShadowBlurMaterialMethod = typeof(PointLightVolumeInstance).GetMethod("PrepareRuntimeShadowBlurMaterial", _lifecycleMethodFlags);
            Assert.That(initializeShaderPropertiesMethod, Is.Not.Null);
            Assert.That(prepareShadowBlurMaterialMethod, Is.Not.Null);

            initializeShaderPropertiesMethod.Invoke(baker, null);
            baker.RuntimeShadowBlurMaterial = blurMaterial;
            baker.Blur = 1f;
            baker.ContactHardening = 0f;

            prepareShadowBlurMaterialMethod.Invoke(baker, new object[] { true, 0.25f, 64, false, false });
            float lowResolutionEffectiveRadius = blurMaterial.GetFloat("_BlurRadius") * blurMaterial.GetFloat("_InvResolution");
            float narrowTanHalfFov = blurMaterial.GetFloat("_ShadowTanHalfFov");
            float narrowAngleProjectedRadius = lowResolutionEffectiveRadius / narrowTanHalfFov;
            float narrowAnglePhysicalRadius = narrowAngleProjectedRadius * narrowTanHalfFov;

            prepareShadowBlurMaterialMethod.Invoke(baker, new object[] { true, 1f, 256, false, false });
            float highResolutionEffectiveRadius = blurMaterial.GetFloat("_BlurRadius") * blurMaterial.GetFloat("_InvResolution");
            float wideTanHalfFov = blurMaterial.GetFloat("_ShadowTanHalfFov");
            float wideAngleProjectedRadius = highResolutionEffectiveRadius / wideTanHalfFov;
            float wideAnglePhysicalRadius = wideAngleProjectedRadius * wideTanHalfFov;

            Assert.That(blurMaterial.IsKeywordEnabled("VRCLV_RUNTIME_SHADOW_BLUR_DIRECT"), Is.True);
            Assert.That(blurMaterial.GetFloat("_ShadowTanHalfFov"), Is.EqualTo(1f).Within(Epsilon));
            Assert.That(lowResolutionEffectiveRadius, Is.EqualTo(highResolutionEffectiveRadius).Within(Epsilon));
            Assert.That(narrowAngleProjectedRadius, Is.GreaterThan(wideAngleProjectedRadius));
            Assert.That(narrowAnglePhysicalRadius, Is.EqualTo(wideAnglePhysicalRadius).Within(Epsilon));
        }

        // Verifies shared manager-owned blur materials use manager keyword state instead of stale per-light state.
        [Test]
        public void RuntimeShadowBlurSharedMaterialReappliesKeywordsBetweenLights() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Shared Blur Manager", false);
            Material blurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            manager.RuntimeShadowBlurMaterial = blurMaterial;
            manager.RuntimeShadowBlurQualityPreset = -1;
            manager.RuntimeShadowBlurUniformKeyword = -1;
            manager.RuntimeShadowBlurDirectKeyword = -1;
            manager.RuntimeShadowBlurSphericalKeyword = -1;

            PointLightVolumeInstance highQuality = CreatePointLight(manager, "Runtime Shadow Shared Blur High", true);
            PointLightVolumeInstance lowQuality = CreatePointLight(manager, "Runtime Shadow Shared Blur Low", true);
            highQuality.RuntimeShadowBlurMaterial = blurMaterial;
            lowQuality.RuntimeShadowBlurMaterial = blurMaterial;
            highQuality.RuntimeShadowBlurSamplePreset = 2;
            lowQuality.RuntimeShadowBlurSamplePreset = 0;

            MethodInfo initializeShaderPropertiesMethod = typeof(PointLightVolumeInstance).GetMethod("InitializeRuntimeShadowShaderProperties", _lifecycleMethodFlags);
            MethodInfo prepareShadowBlurMaterialMethod = typeof(PointLightVolumeInstance).GetMethod("PrepareRuntimeShadowBlurMaterial", _lifecycleMethodFlags);
            Assert.That(initializeShaderPropertiesMethod, Is.Not.Null);
            Assert.That(prepareShadowBlurMaterialMethod, Is.Not.Null);

            ConfigureRuntimeShadowBlurReflectionState(highQuality, initializeShaderPropertiesMethod);
            ConfigureRuntimeShadowBlurReflectionState(lowQuality, initializeShaderPropertiesMethod);

            prepareShadowBlurMaterialMethod.Invoke(highQuality, new object[] { true, 1f, 128, true, false });
            Assert.That(blurMaterial.IsKeywordEnabled("VRCLV_RUNTIME_SHADOW_QUALITY_HIGH"), Is.True);
            Assert.That(manager.RuntimeShadowBlurQualityPreset, Is.EqualTo(2));

            prepareShadowBlurMaterialMethod.Invoke(lowQuality, new object[] { true, 1f, 128, true, false });
            Assert.That(blurMaterial.IsKeywordEnabled("VRCLV_RUNTIME_SHADOW_QUALITY_LOW"), Is.True);
            Assert.That(manager.RuntimeShadowBlurQualityPreset, Is.EqualTo(0));

            prepareShadowBlurMaterialMethod.Invoke(highQuality, new object[] { true, 1f, 128, true, false });
            Assert.That(blurMaterial.IsKeywordEnabled("VRCLV_RUNTIME_SHADOW_QUALITY_HIGH"), Is.True);
            Assert.That(manager.RuntimeShadowBlurQualityPreset, Is.EqualTo(2));
        }

        // The shared material and its serialized Manager cache can be restored independently across
        // Play Mode/build transitions. Spherical mode controls both the shader and the CPU pass count,
        // so every runtime bake must reconcile the real material keyword with the target light toggle.
        [TestCase(false)]
        [TestCase(true)]
        public void RuntimeShadowBlurSphericalToggleOverridesStaleSharedMaterialCache(bool sphericalBlur) {
            LightVolumeManager manager = CreateManager("Runtime Shadow Stale Spherical Cache Manager " + sphericalBlur, false);
            Material blurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            manager.RuntimeShadowBlurMaterial = blurMaterial;
            manager.RuntimeShadowBlurQualityPreset = 2;
            manager.RuntimeShadowBlurUniformKeyword = 1;
            manager.RuntimeShadowBlurDirectKeyword = 0;
            manager.RuntimeShadowBlurSphericalKeyword = sphericalBlur ? 1 : 0;

            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Stale Spherical Cache Light " + sphericalBlur, true);
            point.RuntimeShadowBlurMaterial = blurMaterial;
            point.RuntimeShadowBlurSamplePreset = 2;
            if (sphericalBlur) blurMaterial.DisableKeyword("VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL");
            else blurMaterial.EnableKeyword("VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL");

            MethodInfo initializeShaderPropertiesMethod = typeof(PointLightVolumeInstance).GetMethod("InitializeRuntimeShadowShaderProperties", _lifecycleMethodFlags);
            MethodInfo prepareShadowBlurMaterialMethod = typeof(PointLightVolumeInstance).GetMethod("PrepareRuntimeShadowBlurMaterial", _lifecycleMethodFlags);
            Assert.That(initializeShaderPropertiesMethod, Is.Not.Null);
            Assert.That(prepareShadowBlurMaterialMethod, Is.Not.Null);
            ConfigureRuntimeShadowBlurReflectionState(point, initializeShaderPropertiesMethod);

            prepareShadowBlurMaterialMethod.Invoke(point, new object[] { true, 1f, 128, true, sphericalBlur });

            Assert.That(blurMaterial.IsKeywordEnabled("VRCLV_RUNTIME_SHADOW_BLUR_SPHERICAL"), Is.EqualTo(sphericalBlur));
            Assert.That(manager.RuntimeShadowBlurSphericalKeyword, Is.EqualTo(sphericalBlur ? 1 : 0));
        }

        // Editor and runtime baking exclude only the explicitly listed renderers. Nulls and duplicates are safe,
        // while children and siblings remain untouched and every original forceRenderingOff state is restored.
        [Test]
        public void ExclusionMaskOnlyDisablesListedRenderersAndRestoresExactState() {
            PointLightVolumeInstance point = CreateManagerlessPointLight("Shadow Exclusion Light");
            GameObject hierarchyRoot = CreateGameObject("Shadow Exclusion Hierarchy", true);
            GameObject listedObject = CreateGameObject("Listed Shadow Renderer", true);
            listedObject.transform.SetParent(hierarchyRoot.transform, false);
            MeshRenderer listedRenderer = listedObject.AddComponent<MeshRenderer>();
            GameObject childObject = CreateGameObject("Unlisted Shadow Child", true);
            childObject.transform.SetParent(listedObject.transform, false);
            MeshRenderer childRenderer = childObject.AddComponent<MeshRenderer>();
            GameObject siblingObject = CreateGameObject("Unlisted Shadow Sibling", true);
            siblingObject.transform.SetParent(hierarchyRoot.transform, false);
            MeshRenderer siblingRenderer = siblingObject.AddComponent<MeshRenderer>();
            GameObject alreadyExcludedObject = CreateGameObject("Already Excluded Shadow Renderer", true);
            MeshRenderer alreadyExcludedRenderer = alreadyExcludedObject.AddComponent<MeshRenderer>();

            listedRenderer.forceRenderingOff = false;
            childRenderer.forceRenderingOff = false;
            siblingRenderer.forceRenderingOff = false;
            alreadyExcludedRenderer.forceRenderingOff = true;
            point.ExclusionMask = new Renderer[] { listedRenderer, null, listedRenderer, alreadyExcludedRenderer };

            MethodInfo applyMask = typeof(PointLightVolumeInstance).GetMethod("ApplyExclusionMask", _lifecycleMethodFlags);
            MethodInfo restoreMask = typeof(PointLightVolumeInstance).GetMethod("RestoreExclusionMask", _lifecycleMethodFlags);
            Assert.That(applyMask, Is.Not.Null);
            Assert.That(restoreMask, Is.Not.Null);

            try {
                applyMask.Invoke(point, null);
                Assert.That(listedRenderer.forceRenderingOff, Is.True);
                Assert.That(alreadyExcludedRenderer.forceRenderingOff, Is.True);
                Assert.That(childRenderer.forceRenderingOff, Is.False, "Children of a listed renderer must not be traversed.");
                Assert.That(siblingRenderer.forceRenderingOff, Is.False, "Sibling renderers must remain untouched.");
            } finally {
                restoreMask.Invoke(point, null);
            }

            Assert.That(listedRenderer.forceRenderingOff, Is.False, "A duplicated renderer must restore its first captured state.");
            Assert.That(alreadyExcludedRenderer.forceRenderingOff, Is.True);
            Assert.That(childRenderer.forceRenderingOff, Is.False);
            Assert.That(siblingRenderer.forceRenderingOff, Is.False);
        }

        // Verifies runtime shadow baking reports metadata changes so manager globals can refresh after the first bake.
        [Test]
        public void RuntimeShadowBakerDetectsRealtimeShadowMetadataChanges() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Metadata Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Metadata Light", true);
            point.WorldSpaceShadows = true;
            RenderTexture source = CreateRenderTexture("Runtime Shadow Metadata Source", 4, 4, 6, TextureDimension.Tex2DArray);

            PointLightVolumeInstance baker = point;
            AddRuntimeShadowCamera(baker);
            point.FarClip = 12f;
            point.NearClip = 0.35f;
            point.Bias = 0.25f;

            FieldInfo shadowMapTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            MethodInfo applyMethod = typeof(PointLightVolumeInstance).GetMethod("ApplyRuntimeShadowSourceInternal", _lifecycleMethodFlags);
            Assert.That(shadowMapTextureField, Is.Not.Null);
            Assert.That(applyMethod, Is.Not.Null);
            shadowMapTextureField.SetValue(baker, source);

            Vector3 bakePosition = new Vector3(1, 2, 3);
            Quaternion bakeRotation = Quaternion.Euler(0f, 45f, 0f);
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition, bakeRotation, false, false, true }), Is.True);
            Assert.That(point.FarClip, Is.EqualTo(12f).Within(Epsilon));
            Assert.That(point.NearClip, Is.EqualTo(0.35f).Within(Epsilon));
            Assert.That(point.Bias, Is.EqualTo(0.25f).Within(Epsilon));
            Assert.That(point.AutoUpdateShadowMap, Is.False);
            AssertVectorClose(new Vector4(bakePosition.x, bakePosition.y, bakePosition.z, 0), new Vector4(point.ShadowBakePosition.x, point.ShadowBakePosition.y, point.ShadowBakePosition.z, 0));
            Assert.That(Quaternion.Dot(point.ShadowBakeRotation, bakeRotation), Is.EqualTo(1f).Within(Epsilon));

            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition, bakeRotation, false, false, true }), Is.False);

            // Unity's Vector3/Quaternion operators consider these distinct bake transforms equal.
            // Receiver metadata must still match the exact transform used to render every world-space rebake.
            Vector3 preciseBakePosition = new Vector3(bakePosition.x + 0.000001f, bakePosition.y, bakePosition.z);
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { preciseBakePosition, bakeRotation, false, false, true }), Is.True);
            Assert.That(point.ShadowBakePosition.x, Is.EqualTo(preciseBakePosition.x));
            Assert.That(point.ShadowBakePosition.y, Is.EqualTo(preciseBakePosition.y));
            Assert.That(point.ShadowBakePosition.z, Is.EqualTo(preciseBakePosition.z));

            Quaternion preciseBakeRotation = Quaternion.Euler(0f, 45.01f, 0f);
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { preciseBakePosition, preciseBakeRotation, false, false, true }), Is.True);
            Assert.That(point.ShadowBakeRotation.x, Is.EqualTo(preciseBakeRotation.x));
            Assert.That(point.ShadowBakeRotation.y, Is.EqualTo(preciseBakeRotation.y));
            Assert.That(point.ShadowBakeRotation.z, Is.EqualTo(preciseBakeRotation.z));
            Assert.That(point.ShadowBakeRotation.w, Is.EqualTo(preciseBakeRotation.w));
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { preciseBakePosition, preciseBakeRotation, false, false, true }), Is.False);

            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition + Vector3.right, bakeRotation, false, false, true }), Is.True);
        }

        // Prepare is called from the realtime hot path, so a latched allocation failure must return
        // immediately instead of triggering another atlas allocation attempt every frame.
        [Test]
        public void PreparePointLightDirectShadowOutputRespectsAllocationFailureLatch() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Direct Allocation Latch Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Direct Allocation Latch Light", true);
            point.RuntimeShadowResolution = 16;
            point.RuntimeShadowDirectOutput = true;
            point.ShadowMapTexture = null;
            point.ShadowMapMaterial = null;
            point.IsActive = true;
            manager.PointLightVolumeInstances = new[] { point };

            FieldInfo allocationFailedField = typeof(LightVolumeManager).GetField("_shadowTextureAllocationFailed", _lifecycleMethodFlags);
            FieldInfo initializedField = typeof(LightVolumeManager).GetField("_shadowTexturesInitialized", _lifecycleMethodFlags);
            Assert.That(allocationFailedField, Is.Not.Null);
            Assert.That(initializedField, Is.Not.Null);
            Assert.That(manager.ShadowTextures, Is.Null);
            allocationFailedField.SetValue(manager, true);
            initializedField.SetValue(manager, false);

            int baseSlice = manager.PreparePointLightDirectShadowOutput(point);

            Assert.That(baseSlice, Is.EqualTo(-1));
            Assert.That(manager.ShadowTextures, Is.Null);
            Assert.That((bool)allocationFailedField.GetValue(manager), Is.True);
            Assert.That((bool)initializedField.GetValue(manager), Is.False);
        }

        // Whole-source publication shares the allocation-failure latch with direct preparation and
        // must not turn the per-bake hot path into an implicit atlas allocation retry.
        [Test]
        public void UpdatePointLightShadowTextureRespectsAllocationFailureLatch() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Normal Allocation Latch Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            Color[] sourceColors = { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.white };
            Texture2DArray source = CreateSliceColorTextureArray("Runtime Shadow Normal Allocation Latch Source", 16, 16, sourceColors);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Normal Allocation Latch Light", true);
            ConfigureShadowTexture(point, source, false, false, true);
            point.ShadowMapUsesCubemap = true;
            point.Shadows = true;
            point.IsActive = true;
            manager.PointLightVolumeInstances = new[] { point };

            FieldInfo allocationFailedField = typeof(LightVolumeManager).GetField("_shadowTextureAllocationFailed", _lifecycleMethodFlags);
            FieldInfo initializedField = typeof(LightVolumeManager).GetField("_shadowTexturesInitialized", _lifecycleMethodFlags);
            Assert.That(allocationFailedField, Is.Not.Null);
            Assert.That(initializedField, Is.Not.Null);
            Assert.That(point.ShadowMapTexture, Is.SameAs(source));
            Assert.That(manager.ShadowTextures, Is.Null);
            allocationFailedField.SetValue(manager, true);
            initializedField.SetValue(manager, false);

            bool published = manager.UpdatePointLightShadowTexture(point);

            Assert.That(published, Is.False);
            Assert.That(manager.ShadowTextures, Is.Null);
            Assert.That((bool)allocationFailedField.GetValue(manager), Is.True);
            Assert.That((bool)initializedField.GetValue(manager), Is.False);
        }

        // Synchronous texture publication may sanitize legacy null registry slots. Both runtime bake APIs must resolve the light again after that compaction.
        [Test]
        public void RuntimeShadowPublicationSurvivesRegistryCompaction() {
            LightVolumeManager directManager = CreateManager("Runtime Shadow Direct Registry Compaction Manager", false);
            directManager.ShadowTexturesWidth = 16;
            directManager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance direct = CreatePointLight(directManager, "Runtime Shadow Direct Registry Compaction Light", true);
            direct.RuntimeShadowResolution = 16;
            direct.RuntimeShadowDirectOutput = true;
            direct.ShadowMapTexture = null;
            direct.ShadowMapMaterial = null;
            direct.ShadowMapID = -1f;
            directManager.PointLightVolumeInstances = new[] { null, direct };

            Assert.That(directManager.PreparePointLightDirectShadowOutput(direct), Is.EqualTo(0));
            Assert.That(directManager.PointLightVolumeInstances, Is.EqualTo(new[] { direct }));

            LightVolumeManager normalManager = CreateManager("Runtime Shadow Normal Registry Compaction Manager", false);
            normalManager.ShadowTexturesWidth = 16;
            normalManager.ShadowTexturesHeight = 16;
            Texture2DArray source = CreateSliceColorTextureArray("Runtime Shadow Normal Registry Compaction Source", 16, 16,
                new[] { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.white });
            PointLightVolumeInstance normal = CreatePointLight(normalManager, "Runtime Shadow Normal Registry Compaction Light", true);
            ConfigureShadowTexture(normal, source, false, false, true);
            normal.ShadowMapUsesCubemap = true;
            normalManager.PointLightVolumeInstances = new[] { null, normal };

            Assert.That(normalManager.UpdatePointLightShadowTexture(normal), Is.True);
            Assert.That(normalManager.PointLightVolumeInstances, Is.EqualTo(new[] { normal }));
        }

        // Direct realtime output owns a source-less atlas range and never allocates a full local source.
        [Test]
        public void RuntimeShadowDirectBakeWritesOnlyOwnedAtlasRangeWithoutLocalSource() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Direct Output Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;

            PointLightVolumeInstance direct = CreatePointLight(manager, "Runtime Shadow Direct Output Light", true);
            direct.ShadowMapUsesCubemap = true;
            direct.Shadows = true;
            direct.LayerMask = 0;
            direct.FarClip = 4f;
            direct.Blur = 0f;
            direct.RuntimeShadowResolution = 16;
            direct.RuntimeShadowDirectOutput = true;
            direct.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(direct);

            Color[] neighbourColors = { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.white };
            Texture2DArray neighbourSource = CreateSliceColorTextureArray("Runtime Shadow Direct Neighbour", 16, 16, neighbourColors);
            PointLightVolumeInstance neighbour = CreatePointLight(manager, "Runtime Shadow Direct Neighbour Light", true);
            ConfigureShadowTexture(neighbour, neighbourSource, false, false, true);
            neighbour.ShadowMapUsesCubemap = true;
            neighbour.Shadows = true;
            manager.PointLightVolumeInstances = new[] { direct, neighbour };

            FieldInfo sourceField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            Assert.That(sourceField, Is.Not.Null);

            direct.BakeShadows();
            Assert.That(sourceField.GetValue(direct), Is.Null, "A direct-only bake allocated a persistent source.");

            direct.RuntimeShadowDirectOutput = false;
            direct.BakeShadows();
            RenderTexture previousNormalSource = sourceField.GetValue(direct) as RenderTexture;
            Assert.That(previousNormalSource, Is.Not.Null);

            direct.RuntimeShadowDirectOutput = true;
            direct.BakeShadows();
            Assert.That(sourceField.GetValue(direct), Is.Null);
            Assert.That(previousNormalSource == null || !previousNormalSource.IsCreated(), Is.True, "The direct transition retained its old normal source.");

            RenderTexture publishedAtlas = manager.ShadowTextures;
            int directBaseSlice = (int)direct.ShadowMapID * 6;
            int neighbourBaseSlice = (int)neighbour.ShadowMapID * 6;
            Assert.That(publishedAtlas, Is.Not.Null);
            Assert.That(publishedAtlas.volumeDepth, Is.EqualTo(12));
            Assert.That(sourceField.GetValue(direct), Is.Null);
            Assert.That(direct.ShadowMapTexture, Is.Null);
            Assert.That(direct.ShadowMapMaterial, Is.Null);
            Assert.That(direct.AutoUpdateShadowMap, Is.False);

            for (int face = 0; face < 6; face++) {
                FillRenderTextureArraySlice(publishedAtlas, directBaseSlice + face, new Color(0.91f, 0.07f, 0.73f, 1f));
                FillRenderTextureArraySlice(publishedAtlas, neighbourBaseSlice + face, new Color(0.03f, 0.82f, 0.19f, 1f));
            }
            Color[][] pixelsBeforeRebake = ReadRenderTextureArrayPixels(publishedAtlas);

            direct.transform.position = new Vector3(4f, 5f, 6f);
            direct.BakeShadows();

            Assert.That(manager.ShadowTextures, Is.SameAs(publishedAtlas));
            Assert.That(sourceField.GetValue(direct), Is.Null);
            Assert.That(direct.ShadowMapTexture, Is.Null);
            Color[][] pixelsAfterRebake = ReadRenderTextureArrayPixels(publishedAtlas);
            for (int face = 0; face < 6; face++) {
                Assert.That(PixelArraysDiffer(pixelsBeforeRebake[directBaseSlice + face], pixelsAfterRebake[directBaseSlice + face]), Is.True,
                    "The complete direct rebake did not rewrite owned face " + face);
                AssertPixelArraysEqual(pixelsBeforeRebake[neighbourBaseSlice + face], pixelsAfterRebake[neighbourBaseSlice + face],
                    "Direct rebake changed neighbour slice " + face);
            }
        }

        // A structural atlas rebuild may move a source-less direct slot; one following bake must recover its complete new range.
        [Test]
        public void RuntimeShadowDirectBakeRecoversAfterAtlasReallocation() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Direct Reallocation Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;

            PointLightVolumeInstance direct = CreatePointLight(manager, "Runtime Shadow Direct Reallocation Light", true);
            direct.ShadowMapUsesCubemap = true;
            direct.Shadows = true;
            direct.LayerMask = 0;
            direct.FarClip = 4f;
            direct.Blur = 0f;
            direct.RuntimeShadowResolution = 16;
            direct.RuntimeShadowDirectOutput = true;
            direct.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(direct);

            Color[] originalColors = { Color.red, Color.green, Color.blue, Color.yellow, Color.cyan, Color.white };
            Texture2DArray originalSource = CreateSliceColorTextureArray("Runtime Shadow Original Reallocation Source", 16, 16, originalColors);
            PointLightVolumeInstance original = CreatePointLight(manager, "Runtime Shadow Original Reallocation Neighbour", true);
            ConfigureShadowTexture(original, originalSource, false, false, true);
            original.ShadowMapUsesCubemap = true;
            original.Shadows = true;
            manager.PointLightVolumeInstances = new[] { direct, original };

            direct.BakeShadows();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(12));

            Color[] addedColors = { Color.magenta, Color.gray, Color.black, Color.blue, Color.green, Color.red };
            Texture2DArray addedSource = CreateSliceColorTextureArray("Runtime Shadow Added Reallocation Source", 16, 16, addedColors);
            PointLightVolumeInstance added = CreatePointLight(null, "Runtime Shadow Added Reallocation Neighbour", true);
            added.LightVolumeManager = manager;
            ConfigureShadowTexture(added, addedSource, false, false, true);
            added.ShadowMapUsesCubemap = true;
            added.Shadows = true;
            manager.PointLightVolumeInstances = new[] { direct, original, added };

            manager.ReinitializeShadowTextures();

            RenderTexture rebuiltAtlas = manager.ShadowTextures;
            int directBaseSlice = (int)direct.ShadowMapID * 6;
            int originalBaseSlice = (int)original.ShadowMapID * 6;
            int addedBaseSlice = (int)added.ShadowMapID * 6;
            Assert.That(rebuiltAtlas, Is.Not.Null);
            Assert.That(rebuiltAtlas.volumeDepth, Is.EqualTo(18));

            for (int face = 0; face < 6; face++) {
                FillRenderTextureArraySlice(rebuiltAtlas, directBaseSlice + face, new Color(0.91f, 0.07f, 0.73f, 1f));
                FillRenderTextureArraySlice(rebuiltAtlas, originalBaseSlice + face, new Color(0.03f, 0.82f, 0.19f, 1f));
                FillRenderTextureArraySlice(rebuiltAtlas, addedBaseSlice + face, new Color(0.11f, 0.27f, 0.88f, 1f));
            }
            Color[][] pixelsBeforeRecovery = ReadRenderTextureArrayPixels(rebuiltAtlas);

            direct.BakeShadows();

            Assert.That(manager.ShadowTextures, Is.SameAs(rebuiltAtlas));
            Assert.That((int)direct.ShadowMapID * 6, Is.EqualTo(directBaseSlice));
            Assert.That(direct.ShadowMapTexture, Is.Null);
            Color[][] pixelsAfterRecovery = ReadRenderTextureArrayPixels(rebuiltAtlas);
            for (int face = 0; face < 6; face++) {
                Assert.That(PixelArraysDiffer(pixelsBeforeRecovery[directBaseSlice + face], pixelsAfterRecovery[directBaseSlice + face]), Is.True,
                    "Direct recovery did not rewrite face " + face + " in its current atlas range.");
                AssertPixelArraysEqual(pixelsBeforeRecovery[originalBaseSlice + face], pixelsAfterRecovery[originalBaseSlice + face],
                    "Direct recovery changed the original neighbour slice " + face);
                AssertPixelArraysEqual(pixelsBeforeRecovery[addedBaseSlice + face], pixelsAfterRecovery[addedBaseSlice + face],
                    "Direct recovery changed the added neighbour slice " + face);
            }
        }

        // Verifies local-space shadows do not report metadata changes for a bake position that the shader does not read.
        [Test]
        public void RuntimeShadowBakerIgnoresLocalSpaceBakePositionMetadataChanges() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Local Metadata Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Local Metadata Light", true);
            point.WorldSpaceShadows = false;
            RenderTexture source = CreateRenderTexture("Runtime Shadow Local Metadata Source", 4, 4, 6, TextureDimension.Tex2DArray);

            PointLightVolumeInstance baker = point;
            AddRuntimeShadowCamera(baker);

            FieldInfo shadowMapTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            MethodInfo applyMethod = typeof(PointLightVolumeInstance).GetMethod("ApplyRuntimeShadowSourceInternal", _lifecycleMethodFlags);
            Assert.That(shadowMapTextureField, Is.Not.Null);
            Assert.That(applyMethod, Is.Not.Null);
            shadowMapTextureField.SetValue(baker, source);

            Vector3 bakePosition = new Vector3(1, 2, 3);
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition, Quaternion.identity, false, false, true }), Is.True);
            Assert.That((bool)applyMethod.Invoke(baker, new object[] { bakePosition + Vector3.right, Quaternion.identity, false, false, true }), Is.False);
        }

        // Verifies runtime shadow baking does not dirty manager metadata for local-space transform-only movement.
        [Test]
        public void RuntimeShadowBakerDoesNotDirtyManagerOnLocalSpaceMove() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Local Move Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Local Move Light", true);
            PointLightVolumeInstance sentinel = CreatePointLight(manager, "Runtime Shadow Local Move Sentinel", true);
            point.WorldSpaceShadows = false;
            manager.PointLightVolumeInstances = new[] { point, sentinel };
            manager.UpdateVolumes();

            PointLightVolumeInstance baker = point;
            baker.RuntimeShadowResolution = 16;
            baker.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(baker);

            baker.BakeShadows();
            manager.UpdateVolumes();
            point.IsRangeDirty = false;
            sentinel.IsRangeDirty = true;
            Assert.That(GetManagerField<int>(manager, _dirtyPointLightCountField), Is.Zero);

            point.transform.position = new Vector3(3, 4, 5);
            point.transform.rotation = Quaternion.Euler(0, 45, 0);

            baker.BakeShadows();

            Assert.That(GetManagerField<int>(manager, _dirtyPointLightCountField), Is.Zero);
            Assert.That(GetManagerField<bool>(manager, _volumeDataUpdateRequestedField), Is.False);
            Assert.That(sentinel.IsRangeDirty, Is.True, "A pixel-only local-space bake performed an unnecessary full Manager rebuild.");
        }

        // Verifies runtime blur publishes the local shadow texture array used for final blurred output.
        [Test]
        public void RuntimeShadowBakerRegistersBlurredArrayWhenApplied() {
            LightVolumeManager manager = CreateManager("Runtime Shadow Blur Metadata Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Runtime Shadow Blur Metadata Light", true);
            RenderTexture shadowSource = CreateRenderTexture("Runtime Shadow Blur Source", 4, 4, 6, TextureDimension.Tex2DArray);

            PointLightVolumeInstance baker = point;
            point.Blur = 1;
            baker.RuntimeShadowBlurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            AddRuntimeShadowCamera(baker);

            FieldInfo shadowMapTextureField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowTexture", _lifecycleMethodFlags);
            MethodInfo applyMethod = typeof(PointLightVolumeInstance).GetMethod("ApplyRuntimeShadowSourceInternal", _lifecycleMethodFlags);
            Assert.That(shadowMapTextureField, Is.Not.Null);
            Assert.That(applyMethod, Is.Not.Null);

            shadowMapTextureField.SetValue(baker, shadowSource);

            Assert.That((bool)applyMethod.Invoke(baker, new object[] { Vector3.zero, Quaternion.identity, false, false, true }), Is.True);
            Assert.That(point.ShadowMapTexture, Is.SameAs(shadowSource));
            Assert.That(point.ShadowMapTextureIsCubemap, Is.False);
            Assert.That(point.ShadowMapTextureHasDepthSlices, Is.True);
        }

        // A delayed Udon event cannot be cancelled by clearing a local flag. Keep ownership with
        // the already queued callback so a quick disable-enable cannot create a second realtime loop.
        [Test]
        public void ExternalRuntimeShadowBakerKeepsPendingLoopOwnershipAcrossDisableEnable() {
            GameObject gameObject = CreateGameObject("External Runtime Shadow Loop Baker", false);
            PointLightShadowRuntimeBaker baker = gameObject.AddComponent<PointLightShadowRuntimeBaker>();
            baker.Realtime = false;
            gameObject.SetActive(true);

            FieldInfo scheduledField = typeof(PointLightShadowRuntimeBaker).GetField("_realtimeLoopScheduled", _lifecycleMethodFlags);
            Assert.That(scheduledField, Is.Not.Null);
            scheduledField.SetValue(baker, true);

            gameObject.SetActive(false);
            Assert.That((bool)scheduledField.GetValue(baker), Is.True);
            gameObject.SetActive(true);
            Assert.That((bool)scheduledField.GetValue(baker), Is.True);

            gameObject.SetActive(false);
            baker._RealtimeBakeLoop();
            Assert.That((bool)scheduledField.GetValue(baker), Is.False,
                "The queued callback, not OnDisable/OnEnable, must release loop ownership.");
        }

        // The external trigger selects only normal/direct output and preserves target-owned bake quality.
        [Test]
        public void ExternalRuntimeShadowBakerPreservesQualityAndSelectsOutputMode() {
            GameObject bakerObject = CreateGameObject("External Runtime Shadow Settings Baker", false);
            PointLightShadowRuntimeBaker baker = bakerObject.AddComponent<PointLightShadowRuntimeBaker>();
            PointLightVolumeInstance point = CreatePointLight(null, "External Runtime Shadow Settings Light", false);
            point.RuntimeShadowResolution = 512;
            point.RuntimeShadowBlurSamplePreset = 2;
            point.RuntimeShadowSphericalBlur = true;
            point.Blur = 4.5f;
            point.ContactHardening = 0.4f;
            point.Bias = 0.125f;
            point.LayerMask = 1 << 9;
            Material depthMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            Material blurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            point.RuntimeShadowDepthEncodeMaterial = depthMaterial;
            point.RuntimeShadowBlurMaterial = blurMaterial;

            MethodInfo configureMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("ConfigureTargetBake", _lifecycleMethodFlags);
            Assert.That(configureMethod, Is.Not.Null);

            configureMethod.Invoke(baker, new object[] { point, true });
            Assert.That(point.RuntimeShadowDirectOutput, Is.True);

            Assert.That(point.RuntimeShadowResolution, Is.EqualTo(512));
            Assert.That(point.RuntimeShadowBlurSamplePreset, Is.EqualTo(2));
            Assert.That(point.RuntimeShadowSphericalBlur, Is.True);
            Assert.That(point.Blur, Is.EqualTo(4.5f).Within(Epsilon));
            Assert.That(point.ContactHardening, Is.EqualTo(0.4f).Within(Epsilon));
            Assert.That(point.Bias, Is.EqualTo(0.125f).Within(Epsilon));
            Assert.That(point.LayerMask, Is.EqualTo(1 << 9));
            Assert.That(point.RuntimeShadowDepthEncodeMaterial, Is.SameAs(depthMaterial));
            Assert.That(point.RuntimeShadowBlurMaterial, Is.SameAs(blurMaterial));

            configureMethod.Invoke(baker, new object[] { point, false });
            Assert.That(point.RuntimeShadowDirectOutput, Is.False);
            Assert.That(point.RuntimeShadowResolution, Is.EqualTo(512));
            Assert.That(point.RuntimeShadowBlurSamplePreset, Is.EqualTo(2));
            Assert.That(point.RuntimeShadowSphericalBlur, Is.True);
        }

        // Switching realtime targets releases only the old target's retained direct-mode scratch.
        [Test]
        public void ExternalRuntimeShadowBakerReleasesPreviousTargetScratchOnTargetSwitch() {
            GameObject bakerObject = CreateGameObject("External Runtime Shadow Target Switch Baker", false);
            PointLightShadowRuntimeBaker baker = bakerObject.AddComponent<PointLightShadowRuntimeBaker>();
            LightVolumeManager manager = CreateManager("External Runtime Shadow Target Switch Manager", false);
            manager.ShadowTexturesWidth = 16;
            manager.ShadowTexturesHeight = 16;
            PointLightVolumeInstance first = CreatePointLight(manager, "External Runtime Shadow First Target", true);
            PointLightVolumeInstance second = CreatePointLight(manager, "External Runtime Shadow Second Target", true);
            manager.PointLightVolumeInstances = new[] { first, second };

            first.ShadowMapUsesCubemap = true;
            first.Shadows = true;
            first.FarClip = 4f;
            first.Blur = 0f;
            first.RuntimeShadowResolution = 16;
            first.RuntimeShadowDepthEncodeMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowDepthEncode");
            AddRuntimeShadowCamera(first);

            MethodInfo configureMethod = typeof(PointLightShadowRuntimeBaker).GetMethod("ConfigureTargetBake", _lifecycleMethodFlags);
            FieldInfo depthField = typeof(PointLightVolumeInstance).GetField("_runtimeShadowDepthTexture", _lifecycleMethodFlags);
            Assert.That(configureMethod, Is.Not.Null);
            Assert.That(depthField, Is.Not.Null);

            configureMethod.Invoke(baker, new object[] { first, true });
            first.BakeShadows();
            RenderTexture publishedAtlas = manager.ShadowTextures;
            Assert.That(publishedAtlas, Is.Not.Null);
            Assert.That(first.ShadowMapTexture, Is.Null);
            Assert.That(depthField.GetValue(first), Is.Not.Null);

            configureMethod.Invoke(baker, new object[] { second, true });

            Assert.That(depthField.GetValue(first), Is.Null);
            Assert.That(first.ShadowMapTexture, Is.Null);
            Assert.That(manager.ShadowTextures, Is.SameAs(publishedAtlas));
            Assert.That(second.RuntimeShadowDirectOutput, Is.True);
        }

        // Verifies per-frame animated shadow updates recover shadow IDs after setup metadata resets the count.
        [Test]
        public void AnimatedShadowUpdateRestoresRuntimeReservedShadowCount() {
            LightVolumeManager manager = CreateManager("Animated Shadow Count Reset Manager", false);
            RenderTexture source = CreateRenderTexture("Animated Shadow Count Reset Source", 8, 8, 6, TextureDimension.Tex2DArray);
            manager.ShadowTexturesWidth = 8;
            manager.ShadowTexturesHeight = 8;

            PointLightVolumeInstance point = CreatePointLight(manager, "Animated Shadow Count Reset Light", true);
            ConfigureShadowTexture(point, source, true, false, true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.ShadowMapsCount = 0;

            manager.UpdateAutoShadowTextures();

            Assert.That(manager.ShadowMapsCount, Is.EqualTo(1));
            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(6));
            Assert.That(manager.ShadowTextures.useMipMap, Is.False);
            Assert.That(manager.ShadowTextures.autoGenerateMips, Is.False);
        }

        // Verifies multiple unique shadow cubemaps reserve independent six-slice ranges.
        [Test]
        public void ShadowRuntimeArrayReservesSixSlicesPerUniqueSource() {
            LightVolumeManager manager = CreateManager("Shadow Unique Sources Manager", false);
            Cubemap firstSource = CreateCubemap("Shadow First Source");
            Cubemap secondSource = CreateCubemap("Shadow Second Source");
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "Shadow First Light", true);
            ConfigureShadowTexture(firstPoint, firstSource, false, true, false);
            PointLightVolumeInstance secondPoint = CreatePointLight(manager, "Shadow Second Light", true);
            ConfigureShadowTexture(secondPoint, secondSource, false, true, false);
            manager.PointLightVolumeInstances = new[] { firstPoint, secondPoint };

            manager.ReinitializeShadowTextures();
            Assert.DoesNotThrow(() => manager.UpdateVolumes());

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(12));
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(2));
            AssertGlobalFloat(_pointLightShadowCountID, 2);
        }

        // ReinitializeShadowTextures is a public external API, so its return is the publication
        // boundary: atlas layout, per-light IDs and shader arrays must already describe one state.
        [Test]
        public void ReinitializeShadowTexturesPublishesCoherentShaderIdsSynchronously() {
            LightVolumeManager manager = CreateManager("Synchronous Shadow Publication Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;
            PointLightVolumeInstance first = CreatePointLight(manager, "Synchronous Shadow First", true);
            PointLightVolumeInstance second = CreatePointLight(manager, "Synchronous Shadow Second", true);
            ConfigureShadowTexture(first, CreateCubemap("Synchronous Shadow First Source"), false, true, false);
            ConfigureShadowTexture(second, CreateCubemap("Synchronous Shadow Second Source"), false, true, false);
            manager.PointLightVolumeInstances = new[] { first, second };

            manager.ReinitializeShadowTextures();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(Shader.GetGlobalTexture(_pointLightShadowTextureID), Is.SameAs(manager.ShadowTextures));
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(2));
            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(2));
            Assert.That(first.ShadowMapID, Is.EqualTo(0f).Within(Epsilon));
            Assert.That(second.ShadowMapID, Is.EqualTo(1f).Within(Epsilon));
            AssertGlobalFloat(_pointLightCountID, 2);
            AssertGlobalFloat(_pointLightShadowCountID, 2);
            AssertGlobalFloat(_pointLightShadowCubeCountID, 2);
            AssertPointCustomData(0, first, 0f, -first.ShadowMapID - 1f);
            AssertPointCustomData(1, second, 0f, -second.ShadowMapID - 1f);

            manager.PointLightVolumeInstances = new[] { second };
            manager.ReinitializeShadowTextures();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(Shader.GetGlobalTexture(_pointLightShadowTextureID), Is.SameAs(manager.ShadowTextures));
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(1));
            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(1));
            Assert.That(second.ShadowMapID, Is.EqualTo(0f).Within(Epsilon));
            AssertGlobalFloat(_pointLightCountID, 1);
            AssertGlobalFloat(_pointLightShadowCountID, 1);
            AssertGlobalFloat(_pointLightShadowCubeCountID, 1);
            AssertPointCustomData(0, second, 0f, -second.ShadowMapID - 1f);

            manager.PointLightVolumeInstances = new PointLightVolumeInstance[0];
            manager.ReinitializeShadowTextures();

            Assert.That(manager.ShadowTextures, Is.Null);
            Assert.That(manager.ShadowMapsCount, Is.Zero);
            Assert.That(manager.ShadowCubemapsCount, Is.Zero);
            AssertGlobalFloat(_pointLightCountID, 0);
            AssertGlobalFloat(_pointLightShadowCountID, 0);
            AssertGlobalFloat(_pointLightShadowCubeCountID, 0);
        }

        // A failed atlas allocation must publish the point light without advertising a usable
        // shadow, and an explicit retry must restore the atlas and shader ABI in the same call.
        [Test]
        public void ReinitializeShadowTexturesPublishesAllocationFailureAndRecoverySynchronously() {
            LightVolumeManager manager = CreateManager("Shadow Allocation Recovery Manager", false);
            manager.ShadowTexturesWidth = 0;
            manager.ShadowTexturesHeight = 4;
            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow Allocation Recovery Light", true);
            ConfigureShadowTexture(point, CreateCubemap("Shadow Allocation Recovery Source"), false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();

            Assert.That(manager.ShadowTextures, Is.Null);
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(1), "The source-cache fixture did not contain the cubemap shadow.");
            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(1));
            AssertGlobalFloat(_pointLightCountID, 1);
            AssertGlobalFloat(_pointLightShadowCountID, 0);
            AssertGlobalFloat(_pointLightShadowCubeCountID, 0);
            AssertPointCustomData(0, point, 0f, 0f);

            manager.ShadowTexturesWidth = 4;
            manager.ReinitializeShadowTextures();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(Shader.GetGlobalTexture(_pointLightShadowTextureID), Is.SameAs(manager.ShadowTextures));
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(1));
            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(1));
            Assert.That(point.ShadowMapID, Is.EqualTo(0f).Within(Epsilon));
            AssertGlobalFloat(_pointLightCountID, 1);
            AssertGlobalFloat(_pointLightShadowCountID, 1);
            AssertGlobalFloat(_pointLightShadowCubeCountID, 1);
            AssertPointCustomData(0, point, 0f, -point.ShadowMapID - 1f);
        }

        // Verifies duplicate shadow sources resolve to one manager-owned shadow ID and one reserved cubemap range.
        [Test]
        public void DuplicateShadowSourcesReuseOneShadowID() {
            LightVolumeManager manager = CreateManager("Duplicate Shadow IDs Manager", false);
            Cubemap source = CreateCubemap("Duplicate Shadow Source");
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "Duplicate Shadow A", true);
            ConfigureShadowTexture(firstPoint, source, false, true, false);
            PointLightVolumeInstance secondPoint = CreatePointLight(manager, "Duplicate Shadow B", true);
            ConfigureShadowTexture(secondPoint, source, false, true, false);
            manager.PointLightVolumeInstances = new[] { firstPoint, secondPoint };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowMapsCount, Is.EqualTo(1));
            Assert.That(GetManagerField<int>(manager, _shadowTexturesDepthField), Is.EqualTo(6));
            Assert.That(firstPoint.ShadowMapID, Is.EqualTo(0).Within(Epsilon));
            Assert.That(secondPoint.ShadowMapID, Is.EqualTo(0).Within(Epsilon));
            AssertGlobalFloat(_pointLightShadowCountID, 1);
        }

        // Verifies single spotlight shadows are stored after the cubemap shadow prefix.
        [Test]
        public void SpotSingleShadowUsesSingleSliceAfterCubemapPrefix() {
            LightVolumeManager manager = CreateManager("Spot Single Shadow Layout Manager", false);
            Cubemap cubemapSource = CreateCubemap("Spot Single Layout Cubemap Source");
            Texture2D singleSource = CreateTexture2D("Spot Single Layout Texture Source");
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Cubemap Shadow Point", true);
            point.NearClip = 0.25f;
            point.FarClip = 4f;
            ConfigureShadowTexture(point, cubemapSource, false, true, false);
            PointLightVolumeInstance spot = CreatePointLight(manager, "Single Shadow Spot", true);
            spot.SetSpotLight(60, 0.5f);
            spot.NearClip = 0.5f;
            spot.FarClip = 8f;
            ConfigureShadowTexture(spot, singleSource, false, false, false);
            spot.ShadowMapUsesCubemap = false;
            manager.PointLightVolumeInstances = new[] { point, spot };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(7));
            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(1));
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(2));
            Assert.That(GetManagerField<int>(manager, _shadowCubemapTextureCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<int>(manager, _shadowSingleTextureCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<int[]>(manager, _pointLightShadowIDsField), Is.EqualTo(new[] { 0, 1 }));
            AssertGlobalFloat(_pointLightShadowCubeCountID, 1);
            AssertGlobalFloat(_pointLightShadowCountID, 2);
            AssertPointCustomData(0, point, 0, -1);
            AssertPointCustomData(1, spot, 0, -2);
            Vector4[] customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID);
            Assert.That(customData[0].w, Is.EqualTo(ExpectedCustomShadowInvDepthRange(point)).Within(Epsilon));
            Assert.That(customData[1].w, Is.EqualTo(ExpectedCustomShadowInvDepthRange(spot)).Within(Epsilon));
            Vector4[] reprojectionData = Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID);
            AssertVectorClose(new Vector4(point.ShadowBakePosition.x, point.ShadowBakePosition.y, point.ShadowBakePosition.z, ExpectedCubemapShadowInvDepthRange(point)), reprojectionData[0]);
            AssertVectorClose(new Vector4(spot.ShadowBakePosition.x, spot.ShadowBakePosition.y, spot.ShadowBakePosition.z, spot.OuterAngleTan), reprojectionData[1]);
        }

        // Keeps registry priority stable across shadow backends; final slots drive incremental dynamic updates.
        [Test]
        public void PointLightShaderRecordsPreserveRegistryOrderAndDynamicIndices() {
            LightVolumeManager manager = CreateManager("Stable Point Registry Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance unshadowed = CreatePointLight(manager, "Registry Unshadowed Point", true);
            unshadowed.transform.position = new Vector3(30, 0, 0);

            PointLightVolumeInstance single = CreatePointLight(manager, "Registry Single Shadow Spot", true);
            single.SetSpotLight(60, 0.5f);
            single.transform.position = new Vector3(20, 0, 0);
            ConfigureShadowTexture(single, CreateTexture2D("Registry Single Shadow Source"), false, false, false);
            single.ShadowMapUsesCubemap = false;

            PointLightVolumeInstance cube = CreatePointLight(manager, "Registry Cubemap Shadow Point", true);
            cube.transform.position = new Vector3(10, 0, 0);
            ConfigureShadowTexture(cube, CreateCubemap("Registry Cubemap Shadow Source"), false, true, false);

            // Mixed shadow backends must not reorder the user-visible overdraw priority.
            manager.PointLightVolumeInstances = new[] { unshadowed, single, cube };
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            AssertGlobalFloat(_pointLightCountID, 3);
            int[] enabledPointIDs = GetManagerField<int[]>(manager, _enabledPointIDsField);
            Assert.That(enabledPointIDs[0], Is.EqualTo(0));
            Assert.That(enabledPointIDs[1], Is.EqualTo(1));
            Assert.That(enabledPointIDs[2], Is.EqualTo(2));
            AssertPointCustomData(0, unshadowed, 0, 0);
            AssertPointCustomData(1, single, 0, -2);
            AssertPointCustomData(2, cube, 0, -1);
            Vector4[] positions = Shader.GetGlobalVectorArray(_pointLightPositionID);
            Assert.That(positions[0].x, Is.EqualTo(30).Within(Epsilon));
            Assert.That(positions[1].x, Is.EqualTo(20).Within(Epsilon));
            Assert.That(positions[2].x, Is.EqualTo(10).Within(Epsilon));

            cube.transform.position = new Vector3(11, 0, 0);
            single.transform.position = new Vector3(21, 0, 0);
            unshadowed.transform.position = new Vector3(31, 0, 0);
            Assert.That(_updateAutoUpdatedVolumeChangesMethod, Is.Not.Null);
            Assert.That(_uploadAutoUpdatedVolumeChangesMethod, Is.Not.Null);
            _updateAutoUpdatedVolumeChangesMethod.Invoke(manager, null);
            _uploadAutoUpdatedVolumeChangesMethod.Invoke(manager, null);

            positions = Shader.GetGlobalVectorArray(_pointLightPositionID);
            Assert.That(positions[0].x, Is.EqualTo(31).Within(Epsilon));
            Assert.That(positions[1].x, Is.EqualTo(21).Within(Epsilon));
            Assert.That(positions[2].x, Is.EqualTo(11).Within(Epsilon));

            // The per-pixel overdraw cap must not mutate upload order either.
            manager.AdditiveMaxOverdraw = 2;
            manager.UpdateVolumes();
            enabledPointIDs = GetManagerField<int[]>(manager, _enabledPointIDsField);
            Assert.That(enabledPointIDs[0], Is.EqualTo(0));
            Assert.That(enabledPointIDs[1], Is.EqualTo(1));
            Assert.That(enabledPointIDs[2], Is.EqualTo(2));
            positions = Shader.GetGlobalVectorArray(_pointLightPositionID);
            Assert.That(positions[0].x, Is.EqualTo(31).Within(Epsilon));
            Assert.That(positions[1].x, Is.EqualTo(21).Within(Epsilon));
            Assert.That(positions[2].x, Is.EqualTo(11).Within(Epsilon));
        }

        // Keeps the common unshadowed translation path to one shader-array upload.
        [Test]
        public void TranslationOnlyUnshadowedPointUploadsOnlyPositionBuffer() {
            LightVolumeManager manager = CreateManager("Position-Only Point Upload Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Position-Only Point", true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            point.transform.position = new Vector3(4, 5, 6);
            Assert.That(_updateAutoUpdatedVolumeChangesMethod, Is.Not.Null);
            Assert.That(_uploadAutoUpdatedVolumeChangesMethod, Is.Not.Null);
            Assert.That(_pointLightArrayUploadMaskField, Is.Not.Null);
            Assert.That(_clusterMaskDirtyField, Is.Not.Null);
            Assert.That(_clusteringLightsDirtyField, Is.Not.Null);
            Assert.That(_clusterGeometryUploadPendingField, Is.Not.Null);
            SetManagerField(manager, _clusterMaskDirtyField, false);
            SetManagerField(manager, _clusteringLightsDirtyField, false);
            SetManagerField(manager, _clusterGeometryUploadPendingField, false);
            _updateAutoUpdatedVolumeChangesMethod.Invoke(manager, null);

            Assert.That(GetManagerField<int>(manager, _pointLightArrayUploadMaskField), Is.EqualTo(PointLightUploadPosition));
            Assert.That(GetManagerField<bool>(manager, _clusterMaskDirtyField), Is.True,
                "Translation changes cluster membership even though radius/shape packing is unchanged.");
            Assert.That(GetManagerField<bool>(manager, _clusteringLightsDirtyField), Is.False,
                "Translation must not republish radius/shape vectors.");
            Assert.That(GetManagerField<bool>(manager, _clusterGeometryUploadPendingField), Is.True);
            _uploadAutoUpdatedVolumeChangesMethod.Invoke(manager, null);

            Assert.That(GetManagerField<int>(manager, _pointLightArrayUploadMaskField), Is.Zero);
            Assert.That(GetManagerField<bool>(manager, _clusterGeometryUploadPendingField), Is.False);
            Assert.That(GetManagerField<bool>(manager, _clusterMaskDirtyField), Is.True,
                "Uploading positions consumes the safety gate but the next clustering pass must still rebuild the mask.");
            Vector4[] positions = Shader.GetGlobalVectorArray(_pointLightPositionID);
            AssertVectorClose(ExpectedPointLightPosition(point), positions[0]);
        }

        // Stable basic-Point range writes must not schedule clustering geometry or mask work.
        [Test]
        public void StableClusteringRadiusDoesNotInvalidateGeometryUpload() {
            LightVolumeManager manager = CreateManager("Stable Clustering Radius Manager", false);
            PointLightVolumeInstance point = CreatePointLight(manager, "Stable Clustering Radius Point", true);
            manager.PointLightVolumeInstances = new[] { point };
            manager.UpdateVolumes();

            Assert.That(_writeClusteringLightMethod, Is.Not.Null);
            Assert.That(_clusterMaskDirtyField, Is.Not.Null);
            Assert.That(_clusteringLightsDirtyField, Is.Not.Null);
            Assert.That(_clusterGeometryUploadPendingField, Is.Not.Null);
            SetManagerField(manager, _clusterMaskDirtyField, false);
            SetManagerField(manager, _clusteringLightsDirtyField, false);
            SetManagerField(manager, _clusterGeometryUploadPendingField, false);

            _writeClusteringLightMethod.Invoke(manager, new object[] { 0, point.SquaredRange, 0, 0f, Vector3.forward });

            Assert.That(GetManagerField<bool>(manager, _clusteringLightsDirtyField), Is.False);
            Assert.That(GetManagerField<bool>(manager, _clusterGeometryUploadPendingField), Is.False);
            Assert.That(GetManagerField<bool>(manager, _clusterMaskDirtyField), Is.False);

            _writeClusteringLightMethod.Invoke(manager, new object[] { 0, point.SquaredRange + 1f, 0, 0f, Vector3.forward });

            Assert.That(GetManagerField<bool>(manager, _clusteringLightsDirtyField), Is.True);
            Assert.That(GetManagerField<bool>(manager, _clusterGeometryUploadPendingField), Is.True);
            Assert.That(GetManagerField<bool>(manager, _clusterMaskDirtyField), Is.False,
                "Mask invalidation is deferred until geometry globals have been submitted.");
        }

        // Shadowed translation keeps the packed basis and refreshes only position plus world-origin reuse state.
        [Test]
        public void TranslationOnlyShadowedPointRefreshesWorldOriginMarkerWithoutFullRepack() {
            LightVolumeManager manager = CreateManager("Shadow Translation Fast Path Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;
            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow Translation Fast Path Point", true);
            point.WorldSpaceShadows = true;
            point.ShadowBakePosition = Vector3.zero;
            point.NearClip = 0.25f;
            point.FarClip = 5f;
            ConfigureShadowTexture(point, CreateCubemap("Shadow Translation Fast Path Source"), false, true, false);
            manager.PointLightVolumeInstances = new[] { point };
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w, Is.LessThan(0f));
            Vector4 reprojectionBefore = Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0];
            Vector4 rotationBefore = Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID)[0];

            point.transform.position = new Vector3(3f, -2f, 1f);
            _updateAutoUpdatedVolumeChangesMethod.Invoke(manager, null);

            Assert.That(GetManagerField<int>(manager, _pointLightArrayUploadMaskField),
                Is.EqualTo(PointLightUploadPosition | PointLightUploadCustomId));
            _uploadAutoUpdatedVolumeChangesMethod.Invoke(manager, null);

            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedCustomShadowInvDepthRange(point)).Within(Epsilon));
            AssertVectorClose(reprojectionBefore, Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0]);
            AssertVectorClose(rotationBefore, Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID)[0]);

            point.transform.position = new Vector3(4f, -2f, 1f);
            _updateAutoUpdatedVolumeChangesMethod.Invoke(manager, null);
            Assert.That(GetManagerField<int>(manager, _pointLightArrayUploadMaskField), Is.EqualTo(PointLightUploadPosition),
                "Once away from the exact bake origin, further world-shadow translation is position-only.");
            _uploadAutoUpdatedVolumeChangesMethod.Invoke(manager, null);

            point.transform.position = point.ShadowBakePosition;
            _updateAutoUpdatedVolumeChangesMethod.Invoke(manager, null);
            Assert.That(GetManagerField<int>(manager, _pointLightArrayUploadMaskField),
                Is.EqualTo(PointLightUploadPosition | PointLightUploadCustomId));
            _uploadAutoUpdatedVolumeChangesMethod.Invoke(manager, null);

            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(-ExpectedShadowInvDepthRange(point)).Within(Epsilon),
                "Returning to the exact world-space bake origin must restore the negative reuse marker.");
            AssertVectorClose(reprojectionBefore, Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0]);
            AssertVectorClose(rotationBefore, Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID)[0]);
        }

        // Local-space shadow translation preserves its basis and never uses the negative world-origin marker.
        [Test]
        public void TranslationOnlyLocalShadowKeepsPositiveOriginMarkerAndPackedBasis() {
            LightVolumeManager manager = CreateManager("Local Shadow Translation Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;
            PointLightVolumeInstance point = CreatePointLight(manager, "Local Shadow Translation Point", true);
            point.WorldSpaceShadows = false;
            point.ShadowBakePosition = Vector3.zero;
            point.NearClip = 0.2f;
            point.FarClip = 6f;
            ConfigureShadowTexture(point, CreateCubemap("Local Shadow Translation Source"), false, true, false);
            manager.PointLightVolumeInstances = new[] { point };
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Vector4 reprojectionBefore = Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0];
            Vector4 rotationBefore = Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID)[0];
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedShadowInvDepthRange(point)).Within(Epsilon));

            point.transform.position = new Vector3(-4f, 2f, 3f);
            _updateAutoUpdatedVolumeChangesMethod.Invoke(manager, null);
            Assert.That(GetManagerField<int>(manager, _pointLightArrayUploadMaskField), Is.EqualTo(PointLightUploadPosition));
            _uploadAutoUpdatedVolumeChangesMethod.Invoke(manager, null);

            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedShadowInvDepthRange(point)).Within(Epsilon),
                "Local-space shadows must keep a positive reciprocal range at every origin.");
            AssertVectorClose(reprojectionBefore, Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0]);
            AssertVectorClose(rotationBefore, Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID)[0]);
        }

        // Rotation or scale invalidates the translation-only shadow gate and must repack transform-derived data.
        [Test]
        public void ShadowedPointBasisChangeFallsBackToFullRecordPack() {
            LightVolumeManager manager = CreateManager("Shadow Basis Fallback Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;
            PointLightVolumeInstance point = CreatePointLight(manager, "Shadow Basis Fallback Point", true);
            point.WorldSpaceShadows = false;
            point.NearClip = 0.3f;
            point.FarClip = 7f;
            ConfigureShadowTexture(point, CreateCubemap("Shadow Basis Fallback Source"), false, true, false);
            manager.PointLightVolumeInstances = new[] { point };
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Vector4 rotationBefore = Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID)[0];
            point.transform.rotation = Quaternion.Euler(15f, 40f, -20f);
            point.transform.localScale = new Vector3(2f, 3f, 4f);
            _updateAutoUpdatedVolumeChangesMethod.Invoke(manager, null);

            int basisUploadMask = GetManagerField<int>(manager, _pointLightArrayUploadMaskField);
            Assert.That(basisUploadMask & PointLightUploadPosition, Is.EqualTo(PointLightUploadPosition));
            Assert.That(basisUploadMask & PointLightUploadDirection, Is.Zero,
                "Parametric Point rotation does not change its zero Direction payload.");
            Assert.That(basisUploadMask & PointLightUploadShadowRotation, Is.EqualTo(PointLightUploadShadowRotation));
            _uploadAutoUpdatedVolumeChangesMethod.Invoke(manager, null);

            Assert.That(point.SquaredScale, Is.EqualTo(9f).Within(Epsilon));
            AssertVectorClose(ExpectedPointLightPosition(point), Shader.GetGlobalVectorArray(_pointLightPositionID)[0]);
            Quaternion inverseRotation = Quaternion.Inverse(point.transform.rotation);
            Vector4 expectedRotation = new Vector4(inverseRotation.x, inverseRotation.y, inverseRotation.z, inverseRotation.w);
            Vector4 packedRotation = Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID)[0];
            AssertVectorClose(expectedRotation, packedRotation);
            Assert.That(Vector4.Distance(rotationBefore, packedRotation), Is.GreaterThan(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].z,
                Is.EqualTo(point.SquaredRange).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedShadowInvDepthRange(point)).Within(Epsilon));
        }

        // Verifies spot lights can still force a six-slice cubemap shadow layout.
        [Test]
        public void SpotForceCubemapShadowReservesSixSlices() {
            LightVolumeManager manager = CreateManager("Spot Force Cubemap Shadow Manager", false);
            Texture2D source = CreateTexture2D("Spot Force Cubemap Texture Source");
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance spot = CreatePointLight(manager, "Forced Cubemap Shadow Spot", true);
            spot.SetSpotLight(60, 0.5f);
            spot.NearClip = 0.5f;
            spot.FarClip = 8f;
            ConfigureShadowTexture(spot, source, false, false, false);
            spot.ShadowMapUsesCubemap = true;
            manager.PointLightVolumeInstances = new[] { spot };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(6));
            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(1));
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(1));
            Assert.That(GetManagerField<int>(manager, _shadowCubemapTextureCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<int>(manager, _shadowSingleTextureCountField), Is.EqualTo(0));
            AssertGlobalFloat(_pointLightShadowCubeCountID, 1);
            AssertGlobalFloat(_pointLightShadowCountID, 1);
            AssertPointCustomData(spot, 0, -1);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(ExpectedCubemapShadowInvDepthRange(spot)).Within(Epsilon));
        }

        // Verifies CustomID.W keeps the Spot reciprocal range while reprojection W preserves the backend payload.
        [Test]
        public void SpotShadowBackendSwitchKeepsInvDepthRangeInCustomData() {
            LightVolumeManager manager = CreateManager("Spot Shadow Backend Switch Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance spot = CreatePointLight(manager, "Switchable Shadow Spot", true);
            spot.SetSpotLight(60, 0.5f);
            spot.NearClip = 0.5f;
            spot.FarClip = 8f;
            ConfigureShadowTexture(spot, CreateTexture2D("Switchable Spot Shadow Source"), false, false, false);
            spot.ShadowMapUsesCubemap = false;
            manager.PointLightVolumeInstances = new[] { spot };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(0));
            AssertPointCustomData(spot, 0, -1);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedCustomShadowInvDepthRange(spot)).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(spot.OuterAngleTan).Within(Epsilon));

            spot.ShadowMapUsesCubemap = true;
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(1));
            AssertPointCustomData(spot, 0, -1);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedCustomShadowInvDepthRange(spot)).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(ExpectedCubemapShadowInvDepthRange(spot)).Within(Epsilon));

            spot.ShadowMapUsesCubemap = false;
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(0));
            AssertPointCustomData(spot, 0, -1);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedCustomShadowInvDepthRange(spot)).Within(Epsilon));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(spot.OuterAngleTan).Within(Epsilon));
        }

        // The v3 fast-path marker must require exact component equality and follow incremental dynamic moves.
        [Test]
        public void WorldShadowSameOriginMarkerTracksExactDynamicPosition() {
            LightVolumeManager manager = CreateManager("Exact Same Origin Shadow Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Exact Same Origin Shadow Point", true);
            Vector3 bakePosition = new Vector3(2, 3, 4);
            point.transform.position = bakePosition;
            point.WorldSpaceShadows = true;
            point.ShadowBakePosition = bakePosition;
            point.ShadowBakeRotation = Quaternion.Euler(17, 29, 43); // Rotation may differ; the receiver keeps baked rotation.
            ConfigureShadowTexture(point, CreateCubemap("Exact Same Origin Shadow Source"), false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Vector4 customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0];
            Assert.That(customData.y, Is.EqualTo(1f).Within(Epsilon)); // World-space shadow ID semantics stay positive.
            Assert.That(customData.w, Is.EqualTo(-ExpectedShadowInvDepthRange(point)).Within(Epsilon));
            Quaternion expectedBakeRotation = Quaternion.Inverse(point.ShadowBakeRotation);
            AssertVectorClose(new Vector4(expectedBakeRotation.x, expectedBakeRotation.y, expectedBakeRotation.z, expectedBakeRotation.w),
                Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID)[0]);

            // Moving only the live light makes the origins distinct; the incremental writer must clear the marker.
            point.transform.position = new Vector3(2.25f, 3, 4);
            Assert.That(_updateAutoUpdatedVolumeChangesMethod, Is.Not.Null);
            Assert.That(_uploadAutoUpdatedVolumeChangesMethod, Is.Not.Null);
            _updateAutoUpdatedVolumeChangesMethod.Invoke(manager, null);
            _uploadAutoUpdatedVolumeChangesMethod.Invoke(manager, null);
            customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0];
            Assert.That(customData.y, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(customData.w, Is.EqualTo(ExpectedShadowInvDepthRange(point)).Within(Epsilon));

            // Publishing a bake at the new exact origin restores the marker.
            point.ShadowBakePosition = point.transform.position;
            manager.UpdateVolumes();
            customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0];
            Assert.That(customData.w, Is.EqualTo(-ExpectedShadowInvDepthRange(point)).Within(Epsilon));

            // Even a sub-Epsilon but representable component difference must not take the exact path.
            point.ShadowBakePosition = new Vector3(point.Position.x + 0.000001f, point.Position.y, point.Position.z);
            manager.UpdateVolumes();
            customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0];
            Assert.That(customData.w, Is.EqualTo(ExpectedShadowInvDepthRange(point)).Within(Epsilon));
        }

        // Spot single/cubemap backends share the sign marker while retaining their reprojection W payloads.
        [Test]
        public void SpotSameOriginMarkerPreservesBackendReprojectionPayloads() {
            LightVolumeManager manager = CreateManager("Same Origin Spot Backend Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance spot = CreatePointLight(manager, "Same Origin Shadow Spot", true);
            spot.SetSpotLight(60, 0.5f);
            spot.transform.position = new Vector3(6, 7, 8);
            spot.WorldSpaceShadows = true;
            spot.ShadowBakePosition = spot.transform.position;
            spot.ShadowBakeRotation = Quaternion.Euler(11, 23, 37);
            spot.NearClip = 0.5f;
            spot.FarClip = 8f;
            ConfigureShadowTexture(spot, CreateTexture2D("Same Origin Spot Shadow Source"), false, false, false);
            spot.ShadowMapUsesCubemap = false;
            manager.PointLightVolumeInstances = new[] { spot };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Vector4 customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0];
            Vector4 extraData = Shader.GetGlobalVectorArray(_pointLightExtraDataID)[0];
            Vector4 reprojectionData = Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0];
            Assert.That(customData.y, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(customData.w, Is.EqualTo(-ExpectedShadowInvDepthRange(spot)).Within(Epsilon));
            Assert.That(extraData.y, Is.EqualTo(spot.OuterAngleTan).Within(Epsilon)); // Packed tangent used by the no-fetch arm.
            Assert.That(reprojectionData.w, Is.EqualTo(spot.OuterAngleTan).Within(Epsilon)); // Manager/shader ABI remains populated.

            spot.ShadowMapUsesCubemap = true;
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0];
            reprojectionData = Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0];
            Assert.That(customData.y, Is.EqualTo(1f).Within(Epsilon));
            Assert.That(customData.w, Is.EqualTo(-ExpectedShadowInvDepthRange(spot)).Within(Epsilon));
            Assert.That(reprojectionData.w, Is.EqualTo(ExpectedCubemapShadowInvDepthRange(spot)).Within(Epsilon));

            spot.ShadowBakePosition = new Vector3(spot.Position.x, spot.Position.y, spot.Position.z + 0.000001f);
            manager.UpdateVolumes();
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedShadowInvDepthRange(spot)).Within(Epsilon));
        }

        // Verifies malformed low-level Point/Area metadata cannot enter the single-slice shadow block used only by spots.
        [Test]
        public void PointAndAreaShadowMetadataIsCanonicalizedToCubemaps() {
            LightVolumeManager manager = CreateManager("Non Spot Shadow Canonicalization Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Malformed Point Shadow", true);
            ConfigureShadowTexture(point, CreateTexture2D("Malformed Point Shadow Source"), false, false, false);
            point.ShadowMapUsesCubemap = false;

            PointLightVolumeInstance area = CreatePointLight(manager, "Malformed Area Shadow", true);
            area.SetCustomTexture();
            area.SetAreaLight();
            area.CustomTexture = CreateTexture2D("Area Shadow Cookie");
            area.ProjectionType = 1; // 1: texture
            area.transform.localScale = new Vector3(-2, 3, 1);
            area.UpdateScale();
            ConfigureShadowTexture(area, CreateTexture2D("Malformed Area Shadow Source"), false, false, false);
            area.ShadowMapUsesCubemap = false;
            manager.PointLightVolumeInstances = new[] { point, area };

            manager.ReinitializeCustomTextures();
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(point.ShadowMapUsesCubemap, Is.True);
            Assert.That(area.ShadowMapUsesCubemap, Is.True);
            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(2));
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(2));
            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(12));
            Assert.That(GetManagerField<int>(manager, _shadowSingleTextureCountField), Is.EqualTo(0));
            Assert.That(GetManagerField<int[]>(manager, _pointLightShadowIDsField), Is.EqualTo(new[] { 0, 1 }));
            Vector4 areaCustomData = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[1];
            Assert.That(areaCustomData.x, Is.LessThan(0));
            Assert.That(areaCustomData.w, Is.EqualTo(-1f).Within(Epsilon)); // X mirror tag replaces unused Area far clip.
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[1].w,
                Is.EqualTo(ExpectedCubemapShadowInvDepthRange(area)).Within(Epsilon));
        }

        // Verifies a runtime Spot(single) -> Point transition rebuilds the atlas before the typed cubemap shader path is used.
        [Test]
        public void SpotSingleShadowSwitchingToPointRebuildsAsCubemap() {
            LightVolumeManager manager = CreateManager("Spot To Point Shadow Transition Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance light = CreatePointLight(manager, "Spot To Point Shadow", true);
            light.SetSpotLight(60, 0.5f);
            light.NearClip = 0.5f;
            light.FarClip = 8f;
            ConfigureShadowTexture(light, CreateTexture2D("Spot To Point Shadow Source"), false, false, false);
            light.ShadowMapUsesCubemap = false;
            manager.PointLightVolumeInstances = new[] { light };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();
            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(0));
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(1));
            Assert.That(Shader.GetGlobalVectorArray(_pointLightCustomIdID)[0].w,
                Is.EqualTo(ExpectedCustomShadowInvDepthRange(light)).Within(Epsilon));

            light.SetPointLight();
            manager.UpdateVolumes();

            Assert.That(light.ShadowMapUsesCubemap, Is.True);
            Assert.That(manager.ShadowCubemapsCount, Is.EqualTo(1));
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(1));
            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(6));
            Assert.That(GetManagerField<int>(manager, _shadowSingleTextureCountField), Is.EqualTo(0));
            AssertPointCustomData(light, 0, -1);
            Assert.That(Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID)[0].w,
                Is.EqualTo(ExpectedCubemapShadowInvDepthRange(light)).Within(Epsilon));
        }

        // Verifies enabling an already-registered shadowed light invalidates stale empty shadow caches.
        [Test]
        public void ReenabledRegisteredShadowLightRebuildsShadowTextures() {
            LightVolumeManager manager = CreateManager("Reenabled Shadow Manager", false);
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;

            PointLightVolumeInstance point = CreatePointLight(manager, "Reenabled Shadow Light", true);
            ConfigureShadowTexture(point, CreateCubemap("Reenabled Shadow Source"), false, true, false);
            manager.PointLightVolumeInstances = new[] { point };

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            AssertGlobalFloat(_pointLightShadowCountID, 1);
            AssertPointCustomData(point, 0, -1);

            point.gameObject.SetActive(false);
            point.IsActive = false;
            manager.PointLightVolumeInstances = new[] { point };
            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Null);
            AssertGlobalFloat(_pointLightShadowCountID, 0);

            point.gameObject.SetActive(true);
            point.IsActive = true;
            manager.InitializePointLightVolume(point);
            manager.UpdateVolumes();

            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(manager.ShadowTextures.volumeDepth, Is.EqualTo(6));
            AssertGlobalFloat(_pointLightShadowCountID, 1);
            AssertPointCustomData(point, 0, -1);
            Assert.That(GetManagerField<int[]>(manager, _pointLightShadowIDsField)[0], Is.EqualTo(0));
        }

        // Verifies material-only cubemap updates receive Light Volumes per-face target info.
        [Test]
        public void AnimatedPointCubemapMaterialReceivesPerFaceBlitInfo() {
            LightVolumeManager manager = CreateManager("Animated Point Cubemap Material Manager", false);
            manager.CustomTextures = CreateRenderTexture("Animated Point Cubemap Material Runtime", 16, 8, 12, TextureDimension.Tex2DArray);
            SetManagerField(manager, _customTexturesDepthField, 12);
            Material material = CreateMaterial("Hidden/CubeFace");
            MethodInfo method = typeof(LightVolumeManager).GetMethod("BlitCubemapMaterial", _lifecycleMethodFlags);
            Assert.That(method, Is.Not.Null);

            method.Invoke(manager, new object[] { material, 6, manager.CustomTextures });

            AssertVectorClose(new Vector4(16, 8, 1, 5), material.GetVector(CustomRenderTextureInfoProperty));
        }

        // Verifies manager-owned custom IDs stay compatible with shader slice formulas when cubemap inputs are deduplicated.
        [Test]
        public void RuntimeCustomTextureIdsStayCompatibleAfterDuplicateCubemaps() {
            LightVolumeManager manager = CreateManager("Duplicate Cookie IDs Manager", false);
            Cubemap cubemap = CreateCubemap("Duplicate Cubemap Cookie");
            Texture2D cookieA = CreateTexture2D("Cookie A");
            Texture2D cookieB = CreateTexture2D("Cookie B");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance cubemapPointA = CreatePointLight(manager, "Duplicate Cubemap Point A", true);
            cubemapPointA.SetPointLight();
            cubemapPointA.SetCustomTexture();
            cubemapPointA.CustomTexture = cubemap;
            cubemapPointA.CustomTextureIsCubemap = true;
            cubemapPointA.ProjectionType = 1; // 1: texture

            PointLightVolumeInstance cubemapPointB = CreatePointLight(manager, "Duplicate Cubemap Point B", true);
            cubemapPointB.SetPointLight();
            cubemapPointB.SetCustomTexture();
            cubemapPointB.CustomTexture = cubemap;
            cubemapPointB.CustomTextureIsCubemap = true;
            cubemapPointB.ProjectionType = 1; // 1: texture

            PointLightVolumeInstance cookieSpotA = CreatePointLight(manager, "Duplicate Cookie Spot A", true);
            cookieSpotA.SetCustomTexture();
            cookieSpotA.SetSpotLight(60, 0.5f);
            cookieSpotA.CustomTexture = cookieA;
            cookieSpotA.ProjectionType = 1; // 1: texture

            PointLightVolumeInstance cookieSpotB = CreatePointLight(manager, "Duplicate Cookie Spot B", true);
            cookieSpotB.SetCustomTexture();
            cookieSpotB.SetSpotLight(60, 0.5f);
            cookieSpotB.CustomTexture = cookieB;
            cookieSpotB.ProjectionType = 1; // 1: texture

            PointLightVolumeInstance cookieSpotADuplicate = CreatePointLight(manager, "Duplicate Cookie Spot A Duplicate", true);
            cookieSpotADuplicate.SetCustomTexture();
            cookieSpotADuplicate.SetSpotLight(60, 0.5f);
            cookieSpotADuplicate.CustomTexture = cookieA;
            cookieSpotADuplicate.ProjectionType = 1; // 1: texture

            manager.PointLightVolumeInstances = new[] { cubemapPointA, cubemapPointB, cookieSpotA, cookieSpotB, cookieSpotADuplicate };
            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(8));
            Assert.That(manager.CubemapsCount, Is.EqualTo(1));
            Assert.That(GetManagerField<int>(manager, _customCubemapTextureCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<int>(manager, _customSingleTextureCountField), Is.EqualTo(2));
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, 0, 1, 2, 1 }));
            AssertPointCustomData(0, cubemapPointA, -1, 0);
            AssertPointCustomData(1, cubemapPointB, -1, 0);
            AssertPointCustomData(2, cookieSpotA, -2, 0);
            AssertPointCustomData(3, cookieSpotB, -3, 0);
            AssertPointCustomData(4, cookieSpotADuplicate, -2, 0);
        }

        // Verifies point LUTs use the 2.x-compatible positive ID convention without overlapping later cookie slices.
        [Test]
        public void PointLutSourceUsesV2IdBeforeLaterCookies() {
            LightVolumeManager manager = CreateManager("Point LUT V2 Compatibility Manager", false);
            Texture2D lut = CreateTexture2D("Shared Point LUT");
            Material areaCookie = CreateMaterial("Hidden/CubeFace");
            manager.CustomTexturesWidth = 4;
            manager.CustomTexturesHeight = 4;

            PointLightVolumeInstance area = CreatePointLight(manager, "Area Material Cookie", true);
            area.transform.localScale = new Vector3(2, 3, 1);
            area.SetCustomTexture();
            area.SetAreaLight();
            area.CustomTextureMaterial = areaCookie;
            area.ProjectionType = 2; // 2: material
            area.AutoUpdateCustomTexture = true;

            PointLightVolumeInstance spot = CreatePointLight(manager, "Spot LUT", true);
            spot.SetLut();
            spot.SetSpotLight(60, 0.5f);
            spot.CustomTexture = lut;
            spot.ProjectionType = 1; // 1: texture

            PointLightVolumeInstance point = CreatePointLight(manager, "Point LUT", true);
            point.SetLut();
            point.CustomTexture = lut;
            point.ProjectionType = 1; // 1: texture

            manager.PointLightVolumeInstances = new[] { area, spot, point };
            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(manager.CustomTextures.volumeDepth, Is.EqualTo(3));
            Assert.That(GetManagerField<int>(manager, _customSingleTextureCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<int>(manager, _customSingleMaterialCountField), Is.EqualTo(1));
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 2, 1, 1 }));
            AssertPointCustomData(0, area, -3, 0);
            AssertPointCustomData(1, spot, 2, 0);
            AssertPointCustomData(2, point, 1, 0);
        }

        // Verifies Area Cookie mirror flags live only in the cookie descriptor while size and rotation stay canonical.
        [Test]
        public void AreaCookieNegativeScaleKeepsCanonicalDataPositiveAndPacksCookieFlags() {
            LightVolumeManager manager = CreateManager("Area Cookie Mirror Manager", false);
            Texture2D cookie = CreateTexture2D("Mirrored Area Cookie");
            Vector3[] scales = {
                new Vector3(2, 3, 1),
                new Vector3(-2, 3, 1),
                new Vector3(2, -3, 1),
                new Vector3(-2, -3, -1)
            };
            PointLightVolumeInstance[] areas = new PointLightVolumeInstance[scales.Length];

            for (int i = 0; i < areas.Length; i++) {
                PointLightVolumeInstance area = CreatePointLight(manager, "Mirrored Area " + i, true);
                area.transform.rotation = Quaternion.Euler(17, 31, 43);
                area.transform.localScale = scales[i];
                area.SetCustomTexture();
                area.SetAreaLight();
                area.CustomTexture = cookie;
                area.ProjectionType = 1; // 1: texture
                area.UpdateScale();
                areas[i] = area;
            }

            manager.PointLightVolumeInstances = areas;
            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, 0, 0, 0 }));
            Vector4[] directions = Shader.GetGlobalVectorArray(_pointLightDirectionID);
            Vector4[] colors = Shader.GetGlobalVectorArray(_pointLightColorID);
            float expectedSquaredScale = 4f; // Average abs scale is 2 for every sign combination.
            float expectedRange = areas[0].SquaredRange;
            for (int i = 0; i < areas.Length; i++) {
                AssertPointCustomData(i, areas[i], -1f, 0);
                Assert.That(areas[i].Width, Is.EqualTo(2).Within(Epsilon));
                Assert.That(areas[i].Height, Is.EqualTo(3).Within(Epsilon));
                Assert.That(areas[i].SquaredScale, Is.EqualTo(expectedSquaredScale).Within(Epsilon));
                Assert.That(areas[i].SquaredRange, Is.EqualTo(expectedRange).Within(Epsilon));
                Vector4 encoded = directions[i];
                Assert.That(colors[i].w, Is.EqualTo(5f).Within(Epsilon));
                Quaternion expectedRotation = areas[i].transform.rotation;
                Quaternion encodedRotation = new Quaternion(encoded.x, encoded.y, encoded.z, encoded.w);
                Assert.That(Mathf.Abs(Quaternion.Dot(encodedRotation, expectedRotation)), Is.EqualTo(1f).Within(Epsilon));
            }
        }

        // Verifies Area Cookie mirrors also follow aligned negative scale inherited from a parent transform.
        [Test]
        public void AreaCookieParentNegativeScalePacksCookieFlags() {
            LightVolumeManager manager = CreateManager("Parent Area Cookie Mirror Manager", false);
            Texture2D cookie = CreateTexture2D("Parent Mirrored Area Cookie");
            Vector3[] parentScales = {
                new Vector3(-1, 1, 1),
                new Vector3(1, -1, 1),
                new Vector3(-1, -1, 1)
            };
            float[] expectedMirrorTags = { -1f, 2f, -2f };
            PointLightVolumeInstance[] areas = new PointLightVolumeInstance[parentScales.Length];

            for (int i = 0; i < areas.Length; i++) {
                GameObject parent = CreateGameObject("Mirrored Area Parent " + i, true);
                parent.transform.rotation = Quaternion.Euler(13, 29, 41);
                parent.transform.localScale = parentScales[i];
                PointLightVolumeInstance area = CreatePointLight(manager, "Parent Mirrored Area " + i, true);
                area.transform.SetParent(parent.transform, false);
                area.transform.localRotation = Quaternion.identity;
                area.transform.localScale = new Vector3(2, 3, 1);
                area.SetCustomTexture();
                area.SetAreaLight();
                area.CustomTexture = cookie;
                area.ProjectionType = 1; // 1: texture
                areas[i] = area;
            }

            manager.PointLightVolumeInstances = areas;
            manager.ReinitializeCustomTextures();
            manager.UpdateVolumes();

            Vector4[] customData = Shader.GetGlobalVectorArray(_pointLightCustomIdID);
            for (int i = 0; i < areas.Length; i++) {
                Assert.That(customData[i].w, Is.EqualTo(expectedMirrorTags[i]).Within(Epsilon));
                Assert.That(areas[i].Width, Is.EqualTo(2f).Within(Epsilon));
                Assert.That(areas[i].Height, Is.EqualTo(3f).Within(Epsilon));
            }
        }

        // Verifies the explicit transform refresh API updates Area Cookie mirrors without a dynamic-manager pass.
        [Test]
        public void AreaCookieManualUpdateScaleRefreshesParentAndLocalMirrorTag() {
            GameObject parent = CreateGameObject("Manual Area Cookie Mirror Parent", true);
            parent.transform.rotation = Quaternion.Euler(13, 29, 41);
            parent.transform.localScale = new Vector3(-1, 1, 1);
            PointLightVolumeInstance area = CreatePointLight(null, "Manual Area Cookie Mirror", true);
            area.IsDynamic = false;
            area.transform.SetParent(parent.transform, false);
            area.transform.localRotation = Quaternion.identity;
            area.transform.localScale = new Vector3(2, 3, 1);
            area.SetCustomTexture();
            area.SetAreaLight();

            Assert.That(area.AreaCookieMirror, Is.EqualTo(-1f).Within(Epsilon));

            parent.transform.localScale = new Vector3(1, -1, 1);
            area.UpdateScale();
            Assert.That(area.AreaCookieMirror, Is.EqualTo(2f).Within(Epsilon));

            area.transform.localScale = new Vector3(-2, 3, 1);
            area.UpdateScale();
            Assert.That(area.AreaCookieMirror, Is.EqualTo(-2f).Within(Epsilon));
        }

        // Verifies inactive point lights do not keep custom projection sources allocated.
        [Test]
        public void InactiveCustomTextureUsersReleaseRuntimeArrayUntilReactivated() {
            LightVolumeManager manager = CreateManager("Inactive Cookie Users Manager", false);
            Cubemap cubemap = CreateCubemap("Inactive Cookie Cubemap");
            PointLightVolumeInstance firstPoint = ConfigurePointCubemapSource(CreatePointLight(manager, "Inactive Cookie Point A", true), cubemap, true);
            PointLightVolumeInstance secondPoint = ConfigurePointCubemapSource(CreatePointLight(manager, "Inactive Cookie Point B", true), cubemap, true);
            manager.PointLightVolumeInstances = new[] { firstPoint, secondPoint };

            manager.UpdateVolumes();
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, 0 }));

            firstPoint.Intensity = 0;
            manager.UpdateVolumes();
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { -1, 0 }));

            secondPoint.Intensity = 0;
            manager.UpdateVolumes();
            Assert.That(manager.CustomTextures, Is.Null);
            Assert.That(manager.CubemapsCount, Is.EqualTo(0));

            firstPoint.Intensity = 1;
            manager.UpdateVolumes();
            Assert.That(manager.CustomTextures, Is.Not.Null);
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField), Is.EqualTo(new[] { 0, -1 }));
        }

        // Verifies inactive point lights do not keep shadow sources allocated.
        [Test]
        public void InactiveShadowUsersReleaseRuntimeArrayUntilReactivated() {
            LightVolumeManager manager = CreateManager("Inactive Shadow Users Manager", false);
            Cubemap cubemap = CreateCubemap("Inactive Shadow Cubemap");
            PointLightVolumeInstance firstPoint = CreatePointLight(manager, "Inactive Shadow Point A", true);
            ConfigureShadowTexture(firstPoint, cubemap, true, true, false);
            PointLightVolumeInstance secondPoint = CreatePointLight(manager, "Inactive Shadow Point B", true);
            ConfigureShadowTexture(secondPoint, cubemap, true, true, false);
            manager.PointLightVolumeInstances = new[] { firstPoint, secondPoint };

            manager.UpdateVolumes();
            Assert.That(GetManagerField<int[]>(manager, _pointLightShadowIDsField), Is.EqualTo(new[] { 0, 0 }));
            AssertGlobalFloat(_pointLightCountID, 2);
            AssertGlobalFloat(_pointLightShadowCountID, 1);

            firstPoint.ShadingStrength = 0;
            manager.UpdateVolumes();
            AssertGlobalFloat(_pointLightCountID, 2);
            AssertGlobalFloat(_pointLightShadowCountID, 1);

            firstPoint.ShadingStrength = 1;
            manager.UpdateVolumes();

            firstPoint.Intensity = 0;
            manager.UpdateVolumes();
            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(GetManagerField<int[]>(manager, _pointLightShadowIDsField), Is.EqualTo(new[] { -1, 0 }));

            secondPoint.Intensity = 0;
            manager.UpdateVolumes();
            Assert.That(manager.ShadowTextures, Is.Null);
            Assert.That(manager.ShadowMapsCount, Is.EqualTo(0));

            firstPoint.Intensity = 1;
            manager.UpdateVolumes();
            Assert.That(manager.ShadowTextures, Is.Not.Null);
            Assert.That(GetManagerField<int[]>(manager, _pointLightShadowIDsField), Is.EqualTo(new[] { 0, -1 }));
        }

        // Verifies grow-only source mappings clear indices outside a temporarily shrunken registry.
        [Test]
        public void SourceLessAppendDoesNotInheritTextureIdsFromRetainedCacheCapacity() {
            LightVolumeManager manager = CreateManager("Retained Texture Mapping Manager", false);
            Cubemap customSource = CreateCubemap("Retained Custom Source");
            Cubemap shadowSource = CreateCubemap("Retained Shadow Source");
            PointLightVolumeInstance first = ConfigurePointCubemapSource(CreatePointLight(manager, "Retained Source A", true), customSource, false);
            PointLightVolumeInstance removed = ConfigurePointCubemapSource(CreatePointLight(manager, "Retained Source B", true), customSource, false);
            ConfigureShadowTexture(first, shadowSource, false, true, false);
            ConfigureShadowTexture(removed, shadowSource, false, true, false);
            manager.PointLightVolumeInstances = new[] { first, removed };

            manager.ReinitializeCustomTextures();
            manager.ReinitializeShadowTextures();
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField)[1], Is.GreaterThanOrEqualTo(0));
            Assert.That(GetManagerField<int[]>(manager, _pointLightShadowIDsField)[1], Is.GreaterThanOrEqualTo(0));

            manager.PointLightVolumeInstances = new[] { first };
            manager.ReinitializeCustomTextures();
            manager.ReinitializeShadowTextures();

            PointLightVolumeInstance sourceLess = CreatePointLight(manager, "Source-less Append", true);
            Assert.That(manager.PointLightVolumeInstances, Is.EqualTo(new[] { first, sourceLess }));
            Assert.That(GetManagerField<int[]>(manager, _pointLightCustomIDsField)[1], Is.EqualTo(-1));
            Assert.That(GetManagerField<int[]>(manager, _pointLightShadowIDsField)[1], Is.EqualTo(-1));
        }

        // Verifies all active point lights can write Shadow data together.
        [Test]
        public void AllPointLightsWithShadowWriteShadowGlobals() {
            LightVolumeManager manager = CreateManager("All Shadow Manager", false);
            const int pointCount = 6;
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;
            PointLightVolumeInstance[] points = new PointLightVolumeInstance[pointCount];

            for (int i = 0; i < pointCount; i++) {
                PointLightVolumeInstance point = CreatePointLight(manager, "Shadow Point " + i, true);
                ConfigureShadowTexture(point, CreateCubemap("Shadow Source " + i), false, true, false);
                point.Bias = i == 0 ? 0 : 0.01f * (i + 1);
                point.WorldSpaceShadows = i % 2 == 0;
                point.ShadowBakePosition = new Vector3(i + 3, i + 4, i + 5);
                point.ShadowBakeRotation = Quaternion.Euler(i * 3, i * 5, i * 7);
                point.transform.rotation = Quaternion.Euler(i * 11, i * 13, i * 17);
                points[i] = point;
            }
            manager.PointLightVolumeInstances = points;

            manager.ReinitializeShadowTextures();
            manager.UpdateVolumes();

            Vector4[] reprojectionData = Shader.GetGlobalVectorArray(_pointLightShadowReprojectionDataID);
            Vector4[] rotationData = Shader.GetGlobalVectorArray(_pointLightShadowRotationDataID);
            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_pointLightCountID, pointCount);
            AssertGlobalFloat(_pointLightShadowCountID, pointCount);
            for (int i = 0; i < pointCount; i++) {
                float expectedShadowState = points[i].WorldSpaceShadows ? i + 1 : -i - 1;
                AssertPointCustomData(i, points[i], 0, expectedShadowState);
                AssertVectorClose(new Vector4(points[i].ShadowBakePosition.x, points[i].ShadowBakePosition.y, points[i].ShadowBakePosition.z, ExpectedCubemapShadowInvDepthRange(points[i])), reprojectionData[i]);
                if (points[i].WorldSpaceShadows) {
                    Quaternion expectedRotation = Quaternion.Inverse(points[i].ShadowBakeRotation);
                    AssertVectorClose(new Vector4(expectedRotation.x, expectedRotation.y, expectedRotation.z, expectedRotation.w), rotationData[i]);
                } else {
                    Quaternion expectedRotation = Quaternion.Inverse(points[i].transform.rotation);
                    AssertVectorClose(new Vector4(expectedRotation.x, expectedRotation.y, expectedRotation.z, expectedRotation.w), rotationData[i]);
                }
            }
        }

        // Ensures shader array caps are enforced for oversized runtime registries.
        [Test]
        public void ShaderCountsClampToSupportedUdonArraySizes() {
            LightVolumeManager manager = CreateManager("Caps Manager", true);
            LightVolumeInstance[] volumes = new LightVolumeInstance[35];
            PointLightVolumeInstance[] points = new PointLightVolumeInstance[130];

            for (int i = 0; i < volumes.Length; i++) {
                LightVolumeInstance volume = CreateLightVolume(manager, "Clamped Volume " + i, true);
                ConfigureLightVolume(volume, Color.white, 1, false, i * 0.01f);
                volumes[i] = volume;
            }
            for (int i = 0; i < points.Length; i++) {
                PointLightVolumeInstance point = CreatePointLight(manager, "Clamped Point " + i, true);
                SetPointLightSquaredSize(point, 1);
                point.SetPointLight();
                points[i] = point;
            }

            manager.LightVolumeInstances = volumes;
            manager.PointLightVolumeInstances = points;
            manager.AdditiveMaxOverdraw = 128;
            manager.ShadowTexturesWidth = 4;
            manager.ShadowTexturesHeight = 4;
            // A shadowed record outside the selected prefix must not be promoted into the first 128.
            ConfigureShadowTexture(points[129], CreateCubemap("Excluded Clamped Shadow"), false, true, false);
            manager.ReinitializeShadowTextures();

            manager.UpdateVolumes();

            AssertGlobalFloat(_lightVolumeEnabledID, 1);
            AssertGlobalFloat(_lightVolumeCountID, 32);
            AssertGlobalFloat(_pointLightCountID, 128);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[0]), Shader.GetGlobalVectorArray(_lightVolumeColorID)[0]);
            AssertVectorClose(ExpectedLightVolumeColor(volumes[31]), Shader.GetGlobalVectorArray(_lightVolumeColorID)[31]);
            AssertVectorClose(ExpectedPointLightColor(points[0]), Shader.GetGlobalVectorArray(_pointLightColorID)[0]);
            AssertVectorClose(ExpectedPointLightColor(points[127]), Shader.GetGlobalVectorArray(_pointLightColorID)[127]);
        }

        // Creates a manager with deterministic defaults.
        private LightVolumeManager CreateManager(string name, bool withAtlas) {
            return CreateManager(name, withAtlas, true);
        }

        private LightVolumeManager CreateManager(string name, bool withAtlas, bool active) {
            GameObject gameObject = CreateGameObject(name, false);
            LightVolumeManager manager = gameObject.AddComponent<LightVolumeManager>();
            manager.LightVolumeAtlas = withAtlas ? CreateAtlas() : null;
            manager.LightVolumeInstances = new LightVolumeInstance[0];
            manager.PointLightVolumeInstances = new PointLightVolumeInstance[0];
            manager.LightProbesBlending = true;
            manager.SharpBounds = true;
            manager.AutoUpdateVolumes = false;
            manager.AdditiveMaxOverdraw = 4;
            manager.LightsBrightnessCutoff = 0.35f;
            gameObject.SetActive(active);
            return manager;
        }

        // Creates a scene light volume instance and optionally lets Unity call OnEnable.
        private LightVolumeInstance CreateLightVolume(LightVolumeManager manager, string name, bool active) {
            GameObject gameObject = CreateGameObject(name, false);
            LightVolumeInstance volume = gameObject.AddComponent<LightVolumeInstance>();
            volume.LightVolumeManager = manager;
            volume.IsDynamic = true;
            ConfigureLightVolume(volume, Color.white, 1, false, 0);
            gameObject.SetActive(active);
            if (active && manager != null) manager.InitializeLightVolume(volume);
            return volume;
        }

        // Creates a scene point light volume instance and optionally lets Unity call OnEnable.
        private PointLightVolumeInstance CreatePointLight(LightVolumeManager manager, string name, bool active) {
            GameObject gameObject = CreateGameObject(name, false);
            PointLightVolumeInstance point = gameObject.AddComponent<PointLightVolumeInstance>();
            point.LightVolumeManager = manager;
            point.Color = Color.white;
            point.Intensity = 1;
            point.IsDynamic = true;
            SetPointLightSquaredSize(point, 1);
            point.Direction = Vector3.forward;
            point.ConeFalloff = 1;
            point.Angle = 30 * Mathf.Deg2Rad;
            point.OuterAngleCos = Mathf.Cos(point.Angle);
            point.OuterAngleTan = Mathf.Tan(point.Angle);
            gameObject.SetActive(active);
            if (active && manager != null) manager.InitializePointLightVolume(point);
            return point;
        }

        // Creates an active light volume that has a manager reference but is not registered yet.
        private LightVolumeInstance CreateUnregisteredLightVolume(LightVolumeManager manager, string name) {
            GameObject gameObject = CreateGameObject(name, true);
            LightVolumeInstance volume = gameObject.AddComponent<LightVolumeInstance>();
            volume.LightVolumeManager = manager;
            volume.IsDynamic = true;
            ConfigureLightVolume(volume, Color.white, 1, false, 0);
            return volume;
        }

        // Creates an active point light volume that has a manager reference but is not registered yet.
        private PointLightVolumeInstance CreateUnregisteredPointLight(LightVolumeManager manager, string name) {
            GameObject gameObject = CreateGameObject(name, true);
            PointLightVolumeInstance point = gameObject.AddComponent<PointLightVolumeInstance>();
            point.LightVolumeManager = manager;
            point.Color = Color.white;
            point.Intensity = 1;
            point.IsDynamic = true;
            SetPointLightSquaredSize(point, 1);
            point.Direction = Vector3.forward;
            point.ConeFalloff = 1;
            point.Angle = 30 * Mathf.Deg2Rad;
            point.OuterAngleCos = Mathf.Cos(point.Angle);
            return point;
        }

        // Creates an active light volume that has no manager reference after its initial enable pass.
        private LightVolumeInstance CreateManagerlessLightVolume(string name) {
            GameObject gameObject = CreateGameObject(name, true);
            LightVolumeInstance volume = gameObject.AddComponent<LightVolumeInstance>();
            volume.IsDynamic = true;
            ConfigureLightVolume(volume, Color.white, 1, false, 0);
            return volume;
        }

        // Creates an active point light volume that has no manager reference after its initial enable pass.
        private PointLightVolumeInstance CreateManagerlessPointLight(string name) {
            GameObject gameObject = CreateGameObject(name, true);
            PointLightVolumeInstance point = gameObject.AddComponent<PointLightVolumeInstance>();
            point.Color = Color.white;
            point.Intensity = 1;
            point.IsDynamic = true;
            SetPointLightSquaredSize(point, 1);
            point.Direction = Vector3.forward;
            point.ConeFalloff = 1;
            point.Angle = 30 * Mathf.Deg2Rad;
            point.OuterAngleCos = Mathf.Cos(point.Angle);
            point.OuterAngleTan = Mathf.Tan(point.Angle);
            return point;
        }

        // Creates a temporary GameObject tracked by teardown.
        private GameObject CreateGameObject(string name, bool active) {
            GameObject gameObject = new GameObject(name);
            _createdObjects.Add(gameObject);
            gameObject.SetActive(active);
            return gameObject;
        }

        // Configures the private runtime blur state needed by reflection-based blur material tests.
        private static void ConfigureRuntimeShadowBlurReflectionState(PointLightVolumeInstance point, MethodInfo initializeShaderPropertiesMethod) {
            initializeShaderPropertiesMethod.Invoke(point, null);
            point.Blur = 1f;
            point.ContactHardening = 0f;
        }

        // Confirms a queue step completed all six cubemap faces in one BakeShadows invocation.
        private static void AssertWholeLightRuntimeShadow(PointLightVolumeInstance point, Vector3 expectedBakePosition, Color[][] pixelsBeforeBake) {
            RenderTexture shadowTexture = point.ShadowMapTexture as RenderTexture;
            Assert.That(shadowTexture, Is.Not.Null);
            Assert.That(shadowTexture.width, Is.EqualTo(16));
            Assert.That(shadowTexture.height, Is.EqualTo(16));
            Assert.That(shadowTexture.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            Assert.That(shadowTexture.volumeDepth, Is.EqualTo(6));
            Assert.That(point.ShadowBakePosition, Is.EqualTo(expectedBakePosition));
            Assert.That(point.RuntimeShadowDirectOutput, Is.False);
            Color[][] pixelsAfterBake = ReadRenderTextureArrayPixels(shadowTexture);
            for (int face = 0; face < 6; face++)
                Assert.That(PixelArraysDiffer(pixelsBeforeBake[face], pixelsAfterBake[face]), Is.True,
                    "The queued whole-light bake did not rewrite face " + face);
        }

        // Adds the hidden camera that the editor preprocessor normally injects before Play Mode or build
        private Camera AddRuntimeShadowCamera(PointLightVolumeInstance point) {
            GameObject cameraObject = CreateGameObject(point.name + " Runtime Shadow Camera", true);
            cameraObject.transform.SetParent(point.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            point.RuntimeShadowCamera = camera;
            // Build preparation always injects the shared blur dependency when authored blur is
            // requested. Keep generic runtime-bake fixtures representative of that contract.
            if (point.Blur > 0f && point.RuntimeShadowBlurMaterial == null)
                point.RuntimeShadowBlurMaterial = CreateMaterial("Hidden/VRCLV/PointLightShadowRuntimeBlur");
            return camera;
        }

        // Creates a temporary 3D atlas texture tracked by teardown.
        private Texture3D CreateAtlas() {
            Texture3D texture = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false);
            texture.name = "Runtime Test Light Volume Atlas";
            _createdObjects.Add(texture);
            return texture;
        }

        // Creates a temporary 2D texture for texture global assignment checks.
        private Texture2D CreateTexture2D(string name) {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false);
            _createdObjects.Add(texture);
            return texture;
        }

        // Creates a deterministic readable array source with one solid color per slice.
        private Texture2DArray CreateSliceColorTextureArray(string name, int width, int height, Color[] sliceColors) {
            Assert.That(sliceColors, Is.Not.Null);
            Assert.That(sliceColors.Length, Is.GreaterThan(0));
            Texture2DArray texture = new Texture2DArray(width, height, sliceColors.Length, TextureFormat.RGBA32, false, true);
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            Color[] pixels = new Color[width * height];
            for (int slice = 0; slice < sliceColors.Length; slice++) {
                for (int pixel = 0; pixel < pixels.Length; pixel++) pixels[pixel] = sliceColors[slice];
                texture.SetPixels(pixels, slice, 0);
            }
            texture.Apply(false, false);
            _createdObjects.Add(texture);
            return texture;
        }

        // Replaces one atlas slice with a sentinel color so narrow updates can be distinguished from full repacks.
        private static void FillRenderTextureArraySlice(RenderTexture texture, int slice, Color color) {
            RenderTexture previousActive = RenderTexture.active;
            Graphics.SetRenderTarget(texture, 0, CubemapFace.Unknown, slice);
            GL.Clear(false, true, color);
            RenderTexture.active = previousActive;
        }

        // Reads every slice synchronously so a test can detect in-place mutation of a global atlas.
        private static Color[][] ReadRenderTextureArrayPixels(RenderTexture texture) {
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.dimension, Is.EqualTo(TextureDimension.Tex2DArray));
            RenderTexture previousActive = RenderTexture.active;
            Color[][] slices = new Color[texture.volumeDepth][];
            Texture2D readback = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false, true);
            try {
                for (int slice = 0; slice < texture.volumeDepth; slice++) {
                    Graphics.SetRenderTarget(texture, 0, CubemapFace.Unknown, slice);
                    readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                    readback.Apply(false, false);
                    slices[slice] = readback.GetPixels();
                }
            } finally {
                RenderTexture.active = previousActive;
                DestroyTestObject(readback);
            }
            return slices;
        }

        // Checks the exact source-texel transition shared by the managed and compiled-Udon paths.
        private static void AssertSourceFootprintCubemapResample(Color[][] atlasPixels, int destinationResolution) {
            int row = destinationResolution / 2;
            Color firstCrossFacePixel = atlasPixels[0][row * destinationResolution + destinationResolution - 8];
            Color lastInteriorPixel = atlasPixels[0][row * destinationResolution + destinationResolution - 9];
            Color edgePixel = atlasPixels[0][row * destinationResolution + destinationResolution - 1];

            Assert.That(firstCrossFacePixel.b, Is.GreaterThan(0.015f), "The source half-texel footprint collapsed to destination resolution.");
            Assert.That(lastInteriorPixel.b, Is.LessThan(0.005f), "Cross-face filtering extended beyond the source bilinear footprint.");
            Assert.That(edgePixel.r, Is.EqualTo(0.53125f).Within(0.02f));
            Assert.That(edgePixel.b, Is.EqualTo(0.46875f).Within(0.02f));
        }

        // Uses a small float tolerance because source and destination formats may differ.
        private static void AssertPixelArraysEqual(Color[] expected, Color[] actual, string message) {
            Assert.That(actual.Length, Is.EqualTo(expected.Length), message + " (pixel count)");
            for (int pixel = 0; pixel < expected.Length; pixel++) {
                Color expectedPixel = expected[pixel];
                Color actualPixel = actual[pixel];
                Assert.That(actualPixel.r, Is.EqualTo(expectedPixel.r).Within(Epsilon), message + " (R pixel " + pixel + ")");
                Assert.That(actualPixel.g, Is.EqualTo(expectedPixel.g).Within(Epsilon), message + " (G pixel " + pixel + ")");
                Assert.That(actualPixel.b, Is.EqualTo(expectedPixel.b).Within(Epsilon), message + " (B pixel " + pixel + ")");
                Assert.That(actualPixel.a, Is.EqualTo(expectedPixel.a).Within(Epsilon), message + " (A pixel " + pixel + ")");
            }
        }

        // Compares one float shadow texel with an explicit tolerance and finite-value diagnostics.
        private static void AssertColorClose(Color expected, Color actual, float tolerance, string message) {
            Assert.That(float.IsNaN(actual.r) || float.IsInfinity(actual.r), Is.False, message + " (R finite)");
            Assert.That(float.IsNaN(actual.g) || float.IsInfinity(actual.g), Is.False, message + " (G finite)");
            Assert.That(float.IsNaN(actual.b) || float.IsInfinity(actual.b), Is.False, message + " (B finite)");
            Assert.That(float.IsNaN(actual.a) || float.IsInfinity(actual.a), Is.False, message + " (A finite)");
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance), message + " (R)");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance), message + " (G)");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance), message + " (B)");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(tolerance), message + " (A)");
        }

        // Reports whether an atlas slice changed after a complete normal or direct bake.
        private static bool PixelArraysDiffer(Color[] expected, Color[] actual) {
            if (expected == null || actual == null || expected.Length != actual.Length) return true;
            for (int pixel = 0; pixel < expected.Length; pixel++) {
                Color expectedPixel = expected[pixel];
                Color actualPixel = actual[pixel];
                if (Mathf.Abs(actualPixel.r - expectedPixel.r) > Epsilon
                    || Mathf.Abs(actualPixel.g - expectedPixel.g) > Epsilon
                    || Mathf.Abs(actualPixel.b - expectedPixel.b) > Epsilon
                    || Mathf.Abs(actualPixel.a - expectedPixel.a) > Epsilon) return true;
            }
            return false;
        }

        // Creates a temporary cubemap tracked by teardown.
        private Cubemap CreateCubemap(string name) {
            Cubemap cubemap = new Cubemap(1, TextureFormat.RGBA32, false);
            cubemap.name = name;
            cubemap.SetPixel(CubemapFace.PositiveX, 0, 0, Color.white);
            cubemap.SetPixel(CubemapFace.NegativeX, 0, 0, Color.white);
            cubemap.SetPixel(CubemapFace.PositiveY, 0, 0, Color.white);
            cubemap.SetPixel(CubemapFace.NegativeY, 0, 0, Color.white);
            cubemap.SetPixel(CubemapFace.PositiveZ, 0, 0, Color.white);
            cubemap.SetPixel(CubemapFace.NegativeZ, 0, 0, Color.white);
            cubemap.Apply(false);
            _createdObjects.Add(cubemap);
            return cubemap;
        }

        // Creates a temporary render texture source tracked by teardown.
        private RenderTexture CreateRenderTexture(string name, int width, int height, int depth, TextureDimension dimension, RenderTextureFormat format = RenderTextureFormat.ARGB32) {
            RenderTexture texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear);
            texture.name = name;
            texture.dimension = dimension;
            texture.volumeDepth = Mathf.Max(depth, 1);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            texture.Create();
            _createdObjects.Add(texture);
            return texture;
        }

        // Assigns a shadow texture source in the same shape as point-light authoring sync.
        private static void ConfigureShadowTexture(PointLightVolumeInstance point, Texture source, bool autoUpdate, bool isCubemap, bool hasDepthSlices) {
            point.ShadowMapID = 0;
            point.ShadowMapTexture = source;
            point.ShadowMapMaterial = null;
            point.AutoUpdateShadowMap = autoUpdate;
            point.ShadowMapTextureIsCubemap = isCubemap;
            point.ShadowMapTextureHasDepthSlices = hasDepthSlices;
        }

        // Assigns a point cubemap projection source in the same shape as point-light authoring sync.
        private static PointLightVolumeInstance ConfigurePointCubemapSource(PointLightVolumeInstance point, Texture source, bool autoUpdate) {
            point.SetPointLight();
            point.SetCustomTexture();
            point.CustomTexture = source;
            point.CustomTextureMaterial = null;
            point.ProjectionType = 1; // 1: texture
            point.CustomTextureIsCubemap = true;
            point.CustomTextureHasDepthSlices = false;
            point.AutoUpdateCustomTexture = autoUpdate;
            return point;
        }

        // Creates a temporary material tracked by teardown.
        private Material CreateMaterial(string shaderName) {
            Shader shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, shaderName + " shader was not found");
            Material material = new Material(shader);
            material.name = "Runtime Test Material";
            _createdObjects.Add(material);
            return material;
        }

        // Assigns deterministic volume data used by shader global assertions.
        private static void ConfigureLightVolume(LightVolumeInstance volume, Color color, float intensity, bool isAdditive, float offset) {
            volume.Color = color;
            volume.Intensity = intensity;
            volume.IsAdditive = isAdditive;
            volume.InvBakedRotation = Quaternion.identity;
            volume.BoundsUvwMin0 = new Vector4(offset + 0.01f, offset + 0.02f, offset + 0.03f, offset + 0.04f);
            volume.BoundsUvwMin1 = new Vector4(offset + 0.05f, offset + 0.06f, offset + 0.07f, offset + 0.08f);
            volume.BoundsUvwMin2 = new Vector4(offset + 0.09f, offset + 0.1f, offset + 0.11f, offset + 0.12f);
            volume.InvLocalEdgeSmoothing = new Vector4(offset + 0.29f, offset + 0.3f, offset + 0.31f, offset + 0.32f);
        }

        // Converts a light volume color exactly like LightVolumeManager does.
        private static Vector4 ExpectedLightVolumeColor(LightVolumeInstance instance) {
            Color color = instance.Color.linear * instance.Intensity;
            return new Vector4(color.r, color.g, color.b, instance.IsRotated ? 1 : 0);
        }

        // Expands the compact UVW layout into the min/max records consumed by shaders.
        private static Vector4 ExpectedExpandedLightVolumeUvw(LightVolumeInstance instance, int textureIndex, bool max) {
            Vector4 uvwMin = textureIndex == 0 ? instance.BoundsUvwMin0 : textureIndex == 1 ? instance.BoundsUvwMin1 : instance.BoundsUvwMin2;
            if (!max) return new Vector4(uvwMin.x, uvwMin.y, uvwMin.z, 0);
            return new Vector4(uvwMin.x + instance.BoundsUvwMin0.w, uvwMin.y + instance.BoundsUvwMin1.w, uvwMin.z + instance.BoundsUvwMin2.w, 0);
        }

        // Converts a point light color exactly like LightVolumeManager does.
        private static Vector4 ExpectedPointLightColor(PointLightVolumeInstance instance) {
            Color color = instance.Color.linear * instance.Intensity;
            return new Vector4(color.r, color.g, color.b, ExpectedPointLightAngleData(instance));
        }

        // Mirrors the production formula used by both the manager fallback and basic source-local fast path.
        private static float ExpectedBasicPointSquaredRange(PointLightVolumeInstance instance, float cutoff) {
            float luminance = Mathf.Max(instance.Color.r, Mathf.Max(instance.Color.g, instance.Color.b));
            float squaredSize = Mathf.Abs(instance.SquaredScale * instance.LightSourceSize * instance.LightSourceSize);
            return Mathf.Max(Mathf.PI * 2f * luminance * Mathf.Abs(instance.Intensity) / (cutoff * cutoff) - 1f, 0f)
                * squaredSize;
        }

        // Converts an area light color with a cookie fallback average exactly like LightVolumeManager does.
        private static Vector4 ExpectedAreaCookieFallbackColor(PointLightVolumeInstance instance, Color averageColor) {
            Color color = instance.Color.linear * instance.Intensity;
            float alpha = averageColor.a;
            return new Vector4(color.r * averageColor.r * alpha, color.g * averageColor.g * alpha, color.b * averageColor.b * alpha, ExpectedPointLightAngleData(instance));
        }

        // Assigns the readable point light size field using the old packed squared-size value used by previous tests.
        private static void SetPointLightSquaredSize(PointLightVolumeInstance point, float squaredSize) {
            point.LightSourceSize = Mathf.Sqrt(Mathf.Max(Mathf.Abs(squaredSize), 0.0001f));
            point.InverseSquaredRange = 1f / Mathf.Max(point.LightSourceSize * point.LightSourceSize, 0.0001f);
        }

        // Converts readable point light position data exactly like LightVolumeManager does.
        private static Vector4 ExpectedPointLightPosition(PointLightVolumeInstance instance) {
            if (instance.LightType == 2) return new Vector4(instance.Position.x, instance.Position.y, instance.Position.z, Mathf.Max(Mathf.Abs(instance.Width), 0.001f)); // 2: area
            float typeSign = instance.LightType == 1 ? -1f : 1f; // 1: spot
            float w = instance.ProjectionMode == 1 ? typeSign * instance.InverseSquaredRange / Mathf.Max(instance.SquaredScale, 0.000001f) : typeSign * instance.LightSourceSize * instance.LightSourceSize * instance.SquaredScale; // 1: LUT
            return new Vector4(instance.Position.x, instance.Position.y, instance.Position.z, w);
        }

        // Computes the positive reciprocal shadow depth range precomputed by current managers.
        private static float ExpectedShadowInvDepthRange(PointLightVolumeInstance instance) {
            float nearClip = Mathf.Max(instance.NearClip, 0.0001f);
            float requestedFarClip = instance.BakedFarClip > 0f ? instance.BakedFarClip : instance.FarClip;
            float farClip = requestedFarClip > 0f ? Mathf.Max(requestedFarClip, nearClip + 0.0001f) : Mathf.Sqrt(Mathf.Max(instance.SquaredRange, 0.000001f));
            if (nearClip >= farClip) farClip = nearClip + 0.0001f;
            return 1f / Mathf.Max(farClip - nearClip, 0.0001f);
        }

        // CustomID.W stores the reciprocal magnitude for every Point/Spot shadow. Its sign marks
        // only exact same-origin world shadows; local and shifted-world shadows remain positive.
        private static float ExpectedCustomShadowInvDepthRange(PointLightVolumeInstance instance) {
            float invDepthRange = ExpectedShadowInvDepthRange(instance);
            Vector3 bakePosition = instance.ShadowBakePosition;
            Vector4 lightPosition = instance.Position;
            bool reuseWorldShadowOrigin = instance.WorldSpaceShadows
                && bakePosition.x == lightPosition.x
                && bakePosition.y == lightPosition.y
                && bakePosition.z == lightPosition.z;
            return reuseWorldShadowOrigin ? -invDepthRange : invDepthRange;
        }

        // Cubemap reprojection stores the reciprocal depth range with a negative sign.
        private static float ExpectedCubemapShadowInvDepthRange(PointLightVolumeInstance instance) {
            return -ExpectedShadowInvDepthRange(instance);
        }

        // Converts readable point light angle data exactly like LightVolumeManager does.
        private static float ExpectedPointLightAngleData(PointLightVolumeInstance instance) {
            if (instance.LightType == 2) return 2f + Mathf.Max(Mathf.Abs(instance.Height), 0.001f); // 2: area
            if (instance.LightType == 1 && instance.ProjectionMode == 2) return instance.OuterAngleTan; // 1: spot, 2: custom cookie
            return instance.OuterAngleCos;
        }

        // Asserts the packed point custom data vector written to the shader.
        private static void AssertPointCustomData(PointLightVolumeInstance point, float customId, float shadowId) {
            AssertPointCustomData(0, point, customId, shadowId);
        }

        // Asserts the packed point custom data vector at a specific shader array index.
        private static void AssertPointCustomData(int index, PointLightVolumeInstance point, float customId, float shadowId) {
            Vector4 data = Shader.GetGlobalVectorArray(_pointLightCustomIdID)[index];
            Assert.That(data.x, Is.EqualTo(customId).Within(Epsilon));
            Assert.That(data.y, Is.EqualTo(shadowId).Within(Epsilon));
            Assert.That(data.z, Is.EqualTo(point.SquaredRange).Within(Epsilon));
            float expectedCustomDataW = 0f;
            if (point.LightType == 2) { // Area uses the v2-invisible W padding only as an optional Cookie mirror tag.
                if (customId < 0) expectedCustomDataW = point.AreaCookieMirror;
            } else {
                float shadowIdAbs = Mathf.Abs(shadowId);
                bool hasShadow = shadowIdAbs >= 1f && shadowIdAbs < 10000f;
                if (hasShadow) expectedCustomDataW = ExpectedCustomShadowInvDepthRange(point);
            }
            Assert.That(data.w, Is.EqualTo(expectedCustomDataW).Within(Epsilon));
        }

        // Asserts a global float with the shared tolerance.
        private static void AssertGlobalFloat(int propertyId, float expected) {
            Assert.That(Shader.GetGlobalFloat(propertyId), Is.EqualTo(expected).Within(Epsilon));
        }

        // Asserts an integer shader global without relying on float/int global coercion.
        private static void AssertGlobalInteger(int propertyId, int expected) {
            Assert.That(Shader.GetGlobalInteger(propertyId), Is.EqualTo(expected));
        }

        // Asserts a Vector4 with the shared tolerance.
        private static void AssertVectorClose(Vector4 expected, Vector4 actual) {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Epsilon));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Epsilon));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(Epsilon));
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(Epsilon));
        }

        // Asserts a Matrix4x4 with the shared tolerance.
        private static void AssertMatrixClose(Matrix4x4 expected, Matrix4x4 actual) {
            for (int i = 0; i < 16; i++) {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(Epsilon), "Matrix index " + i);
            }
        }

        // Finds a light volume reference without relying on LINQ.
        private static bool ContainsLightVolume(LightVolumeInstance[] instances, LightVolumeInstance target) {
            if (instances == null) return false;
            for (int i = 0; i < instances.Length; i++) {
                if (instances[i] == target) return true;
            }
            return false;
        }

        // Finds a point light volume reference without relying on LINQ.
        private static bool ContainsPointLightVolume(PointLightVolumeInstance[] instances, PointLightVolumeInstance target) {
            if (instances == null) return false;
            for (int i = 0; i < instances.Length; i++) {
                if (instances[i] == target) return true;
            }
            return false;
        }

        // Counts light volume references without relying on LINQ.
        private static int CountLightVolumeReferences(LightVolumeInstance[] instances, LightVolumeInstance target) {
            if (instances == null) return 0;
            int count = 0;
            for (int i = 0; i < instances.Length; i++) {
                if (instances[i] == target) count++;
            }
            return count;
        }

        // Counts point light volume references without relying on LINQ.
        private static int CountPointLightVolumeReferences(PointLightVolumeInstance[] instances, PointLightVolumeInstance target) {
            if (instances == null) return 0;
            int count = 0;
            for (int i = 0; i < instances.Length; i++) {
                if (instances[i] == target) count++;
            }
            return count;
        }

        // Invokes private Unity lifecycle methods because EditMode tests do not run normal MonoBehaviour lifecycle for these scripts.
        private static void InvokeLifecycleMethod(MonoBehaviour behaviour, string methodName) {
            MethodInfo method = behaviour.GetType().GetMethod(methodName, _lifecycleMethodFlags);
            Assert.That(method, Is.Not.Null, methodName + " method was not found on " + behaviour.GetType().Name);
            method.Invoke(behaviour, null);
        }

        // Resets scalar shader globals that can affect later tests.
        private static void ResetShaderGlobals() {
            Shader.SetGlobalFloat(_lightVolumeEnabledID, 0);
            Shader.SetGlobalFloat(_lightVolumeCountID, 0);
            Shader.SetGlobalFloat(_lightVolumeAdditiveCountID, 0);
            Shader.SetGlobalFloat(_lightVolumeOcclusionCountID, 0);
            Shader.SetGlobalFloat(_pointLightCountID, 0);
            Shader.SetGlobalFloat(_pointLightCubeCountID, 0);
            Shader.SetGlobalFloat(_pointLightShadowCubeCountID, 0);
            Shader.SetGlobalFloat(_pointLightShadowCountID, 0);
            Shader.SetGlobalVector(_pointLightShadowReceiverParamsID, Vector4.zero);
            Shader.SetGlobalFloat(_lightBrightnessCutoffID, 0);
            Shader.SetGlobalInteger(_forceSceneLightingID, 0);
        }

        // Destroys a temporary Unity object immediately when the editor runtime allows it.
        private static void DestroyTestObject(UnityEngine.Object target) {
            if (target == null) return;
            if (Application.isEditor) UnityEngine.Object.DestroyImmediate(target);
            else UnityEngine.Object.Destroy(target);
        }
    }

}
