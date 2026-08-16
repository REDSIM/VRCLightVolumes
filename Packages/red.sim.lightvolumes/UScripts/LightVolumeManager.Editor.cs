#if !UDONSHARP && (UNITY_EDITOR || COMPILER_UDONSHARP)
#define UDONSHARP
#endif

#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System;
using UnityEngine;
using UnityEngine.Rendering;
using AtlasPostProcessor = VRCLightVolumes.Editor.AtlasPostProcessor;

namespace VRCLightVolumes {
    // Editor-only companion for the LightVolumeManager proxy. This remains part of the primary partial type and must not receive a separate UdonSharpProgramAsset.
    public partial class LightVolumeManager {
#region Editor State And Atlas Post Processors

        // Lets the editor assembly persist proxy changes without making this Udon assembly depend on editor tools.
        internal static event Action<LightVolumeManager> EditorAtlasPostProcessorsChanged;

        internal bool EditorIsBakeryMode => BakingMode == BakingModeBakery;

        // Deliberate boundary between the runtime/Udon surface and opt-in editor tooling. The handle and this property are excluded from both Udon compilation and player builds.
        public global::VRCLightVolumes.Editor.LightVolumeManagerEditorContext Editor => new global::VRCLightVolumes.Editor.LightVolumeManagerEditorContext(this);

        // UdonSharp's play-mode proxy formatter reflects every C# instance field, including non-serialized fields, while COMPILER_UDONSHARP excludes this state from the Udon heap.
        // Keep all editor-only caches outside the behaviour instance layout so proxy copies stay exact.
        private sealed class EditorState {
            public PointLightVolumeInstance[] CustomSourceOwners = Array.Empty<PointLightVolumeInstance>();
            public Texture[] CustomSourceTextures = Array.Empty<Texture>();
            public Material[] CustomSourceMaterials = Array.Empty<Material>();
            public int[] CustomSourceStates = Array.Empty<int>();
            public int CustomTextureWidth = -1;
            public int CustomTextureHeight = -1;
            public PointLightVolumeInstance[] ShadowSourceOwners = Array.Empty<PointLightVolumeInstance>();
            public Texture[] ShadowSourceTextures = Array.Empty<Texture>();
            public Material[] ShadowSourceMaterials = Array.Empty<Material>();
            public int[] ShadowSourceStates = Array.Empty<int>();
            public int ShadowTextureWidth = -1;
            public int ShadowTextureHeight = -1;
            public int ShadowTextureFormat = -1;
            public Material GeneratedClusteringMaterial;
            public Vector4 FroxelDepthParams;
            public AtlasPostProcessor[] AtlasPostProcessors;
            public RenderTexture[] PostProcessorProjectionTargets;
            public Material[] PostProcessorProjectionMaterials;
            public string[] PostProcessorProjectionTextureNames;
            public Action AtlasPostProcessorsChanged;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LightVolumeManager, EditorState> EditorStates
            = new System.Runtime.CompilerServices.ConditionalWeakTable<LightVolumeManager, EditorState>();

        private EditorState EditorData => EditorStates.GetOrCreateValue(this);

        // Preserve the existing call sites while keeping these names out of the reflected instance-field layout.
        private Material _generatedClusteringMaterial {
            get => EditorData.GeneratedClusteringMaterial;
            set => EditorData.GeneratedClusteringMaterial = value;
        }

        private Vector4 _editorFroxelDepthParams {
            get => EditorData.FroxelDepthParams;
            set => EditorData.FroxelDepthParams = value;
        }

        // Registers a Custom Render Texture post processor for the Light Volume 3D atlas.
        internal void EditorRegisterPostProcessorCRT(CustomRenderTexture texture) {
            if (texture == null) return;
            EditorRegisterPostProcessor(new AtlasPostProcessor {
                Target = texture,
                Material = texture.material,
                InputTextureProperty = "_MainTex",
                Update = texture.Update
            });
        }

        internal void EditorUnregisterPostProcessor(RenderTexture texture) {
            if (texture == null) return;
            EditorUnregisterPostProcessor(new AtlasPostProcessor { Target = texture });
        }

        // Removes a matching atlas post processor and refreshes the remaining chain.
        internal void EditorUnregisterPostProcessor(AtlasPostProcessor processor) {
            AtlasPostProcessor[] processors = EditorGetAtlasPostProcessors();
            int removeCount = 0;
            RenderTexture removedTarget = processor.Target;
            for (int i = 0; i < processors.Length; i++) {
                if (!IsSamePostProcessor(processors[i], processor)) continue;
                if (removedTarget == null) removedTarget = processors[i].Target;
                removeCount++;
            }
            if (removeCount == 0) return;

            AtlasPostProcessor[] remaining = new AtlasPostProcessor[processors.Length - removeCount];
            for (int i = 0, write = 0; i < processors.Length; i++) {
                if (IsSamePostProcessor(processors[i], processor)) continue;
                remaining[write++] = processors[i];
            }
            EditorSetAtlasPostProcessors(remaining);
            Debug.Log($"[LightVolumes] Unregistered post processor: {(removedTarget != null ? removedTarget.name : "")}");
            EditorRefreshAtlasPostProcessors();
        }

        // Adds or updates an atlas post processor and removes duplicate registrations.
        internal void EditorRegisterPostProcessor(AtlasPostProcessor processor) {
            if (processor.Target == null || processor.Material == null && processor.Update == null && processor.UpdateWithInput == null) return;
            if (string.IsNullOrEmpty(processor.InputTextureProperty)) processor.InputTextureProperty = "_MainTex";

            AtlasPostProcessor[] processors = EditorGetAtlasPostProcessors();
            int index = FindPostProcessor(processors, processor);
            if (index < 0) {
                Array.Resize(ref processors, processors.Length + 1);
                processors[processors.Length - 1] = processor;
                EditorSetAtlasPostProcessors(processors);
                Debug.Log($"[LightVolumes] Registered post processor: {processor.Target.name}");
                EditorRefreshAtlasPostProcessors();
                return;
            }

            bool changed = !HasSamePostProcessorValues(processors[index], processor);
            processors[index] = processor;
            int duplicateCount = 0;
            for (int i = 0; i < processors.Length; i++)
                if (i != index && IsSamePostProcessor(processors[i], processor)) duplicateCount++;
            if (!changed && duplicateCount == 0) return;

            if (duplicateCount > 0) {
                AtlasPostProcessor[] unique = new AtlasPostProcessor[processors.Length - duplicateCount];
                for (int i = 0, write = 0; i < processors.Length; i++) {
                    if (i != index && IsSamePostProcessor(processors[i], processor)) continue;
                    unique[write++] = processors[i];
                }
                processors = unique;
            }
            EditorSetAtlasPostProcessors(processors);
            Debug.Log($"[LightVolumes] Updated post processor: {processor.Target.name}");
            EditorRefreshAtlasPostProcessors();
        }

        internal void EditorRefreshAtlasPostProcessors() {
            Texture output = UpdatePostProcessorChain(EditorGetAtlasPostProcessors(), LightVolumeAtlasBase);
            LightVolumeAtlas = output;
            EditorAtlasPostProcessorsChanged?.Invoke(this);
            EditorData.AtlasPostProcessorsChanged?.Invoke();
            UpdateVolumes();
        }

        internal void EditorAddAtlasPostProcessorsChanged(Action callback) {
            EditorData.AtlasPostProcessorsChanged += callback;
        }

        internal void EditorRemoveAtlasPostProcessorsChanged(Action callback) {
            EditorData.AtlasPostProcessorsChanged -= callback;
        }

        // Rehydrates the transient post-processor chain from its serialized editor projection.
        internal AtlasPostProcessor[] EditorGetAtlasPostProcessors() {
            EditorState state = EditorData;
            if (state.AtlasPostProcessors != null &&
                state.PostProcessorProjectionTargets == AtlasPostProcessorTargets &&
                state.PostProcessorProjectionMaterials == AtlasPostProcessorMaterials &&
                state.PostProcessorProjectionTextureNames == AtlasPostProcessorTextureNames)
                return state.AtlasPostProcessors;

            RenderTexture[] targets = AtlasPostProcessorTargets ?? Array.Empty<RenderTexture>();
            Material[] materials = AtlasPostProcessorMaterials;
            string[] textureNames = AtlasPostProcessorTextureNames;
            AtlasPostProcessor[] processors = new AtlasPostProcessor[targets.Length];
            for (int i = 0; i < processors.Length; i++) {
                RenderTexture target = targets[i];
                processors[i] = new AtlasPostProcessor {
                    Target = target,
                    Material = materials != null && i < materials.Length ? materials[i] : null,
                    InputTextureProperty = textureNames != null && i < textureNames.Length && !string.IsNullOrEmpty(textureNames[i]) ? textureNames[i] : "_MainTex"
                };
            }
            state.AtlasPostProcessors = processors;
            CapturePostProcessorProjection(state);
            return processors;
        }

        // Stores the transient post-processor chain and its serializable texture/material projection.
        internal void EditorSetAtlasPostProcessors(AtlasPostProcessor[] processors) {
            processors = processors ?? Array.Empty<AtlasPostProcessor>();
            EditorState state = EditorData;
            if (processors.Length > 0 && ReferenceEquals(processors, state.AtlasPostProcessors))
                processors = (AtlasPostProcessor[])processors.Clone();
            RenderTexture[] targets = new RenderTexture[processors.Length];
            Material[] materials = new Material[processors.Length];
            string[] textureNames = new string[processors.Length];
            for (int i = 0; i < processors.Length; i++) {
                targets[i] = processors[i].Target;
                materials[i] = processors[i].Material;
                textureNames[i] = string.IsNullOrEmpty(processors[i].InputTextureProperty) ? "_MainTex" : processors[i].InputTextureProperty;
            }

            AtlasPostProcessorTargets = targets;
            AtlasPostProcessorMaterials = materials;
            AtlasPostProcessorTextureNames = textureNames;
            state.AtlasPostProcessors = processors;
            CapturePostProcessorProjection(state);
        }

        // Records which serialized arrays produced the cached post-processor chain.
        private void CapturePostProcessorProjection(EditorState state) {
            state.PostProcessorProjectionTargets = AtlasPostProcessorTargets;
            state.PostProcessorProjectionMaterials = AtlasPostProcessorMaterials;
            state.PostProcessorProjectionTextureNames = AtlasPostProcessorTextureNames;
        }

        // Finds the first processor that shares the requested target or callback identity.
        private static int FindPostProcessor(AtlasPostProcessor[] processors, AtlasPostProcessor requested) {
            for (int i = 0; i < processors.Length; i++)
                if (IsSamePostProcessor(processors[i], requested)) return i;
            return -1;
        }

        // Compares post processors by their target texture or update delegate identity.
        private static bool IsSamePostProcessor(AtlasPostProcessor existing, AtlasPostProcessor requested) {
            return requested.Target != null && existing.Target == requested.Target || requested.Update != null && existing.Update == requested.Update || requested.UpdateWithInput != null && existing.UpdateWithInput == requested.UpdateWithInput;
        }

        // Compares every configurable value of two post processors.
        private static bool HasSamePostProcessorValues(AtlasPostProcessor first, AtlasPostProcessor second) {
            return first.Target == second.Target && first.Material == second.Material && first.InputTextureProperty == second.InputTextureProperty && first.Update == second.Update && first.UpdateWithInput == second.UpdateWithInput;
        }

        // Executes valid atlas post processors in order and returns the final output texture.
        private static Texture UpdatePostProcessorChain(AtlasPostProcessor[] processors, Texture baseTexture) {
            if (baseTexture == null || processors == null || processors.Length == 0) return baseTexture;

            Texture output = baseTexture;
            bool hasValidProcessor = false;
            for (int i = 0; i < processors.Length; i++) {
                AtlasPostProcessor processor = processors[i];
                if (processor.Target == null || processor.Material == null && processor.Update == null && processor.UpdateWithInput == null) continue;

                SetupPostProcessorTexture(processor.Target, baseTexture);
                Texture input = output;
                if (processor.Material != null)
                    processor.Material.SetTexture(string.IsNullOrEmpty(processor.InputTextureProperty) ? "_MainTex" : processor.InputTextureProperty, input);
                output = processor.Target;
                hasValidProcessor = true;
                if (processor.UpdateWithInput != null) processor.UpdateWithInput(input);
                else processor.Update?.Invoke();
            }
            return hasValidProcessor ? output : baseTexture;
        }

        // Recreates a post-processor target as a half-float 3D texture matching its source.
        private static void SetupPostProcessorTexture(RenderTexture texture, Texture source) {
            RenderTexture.active = null;
            texture.Release();
            texture.dimension = TextureDimension.Tex3D;
            texture.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            texture.enableRandomWrite = false;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 0;
            texture.width = Mathf.Max(source.width, 1);
            texture.height = Mathf.Max(source.height, 1);
            texture.volumeDepth = Mathf.Max(GetTextureDepth(source), 1);
            if (texture is CustomRenderTexture customTexture) customTexture.updateMode = CustomRenderTextureUpdateMode.Realtime;
            texture.Create();
        }

        // Returns the depth or face count represented by a supported texture type.
        private static int GetTextureDepth(Texture texture) {
            if (texture is Texture3D texture3D) return texture3D.depth;
            if (texture is Texture2DArray textureArray) return textureArray.depth;
            if (texture is RenderTexture renderTexture) return renderTexture.volumeDepth;
            if (texture is Cubemap) return 6;
            return 1;
        }

#endregion

#region Editor Proxy And Source State

        // Returns true when the editor C# proxy must not write runtime shader data while backed Udon drives Play Mode.
        private bool ShouldSkipEditorProxyRuntimeUpdate() {
            return Application.isPlaying && GetComponent("VRC.Udon.UdonBehaviour") != null;
        }

        // Captures the effective custom source state and reports direct edits that bypassed the normal notify API.
        private bool CaptureEditorCustomSourceState() {
            EditorState editorState = EditorData;
            int count = PointLightVolumeInstances.Length;
            bool changed = editorState.CustomSourceOwners.Length != count || editorState.CustomTextureWidth != CustomTexturesWidth || editorState.CustomTextureHeight != CustomTexturesHeight;
            editorState.CustomTextureWidth = CustomTexturesWidth;
            editorState.CustomTextureHeight = CustomTexturesHeight;
            if (changed) {
                editorState.CustomSourceOwners = new PointLightVolumeInstance[count];
                editorState.CustomSourceTextures = new Texture[count];
                editorState.CustomSourceMaterials = new Material[count];
                editorState.CustomSourceStates = new int[count];
            }

            for (int i = 0; i < count; i++) {
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                Texture texture = null;
                Material material = null;
                int state = 0;
                if (instance != null && instance.IsActive && instance.ProjectionMode != 0) {
                    if (instance.ProjectionType == 1 && instance.CustomTexture != null) texture = instance.CustomTexture;
                    else if (instance.ProjectionType == 2 && instance.CustomTextureMaterial != null) material = instance.CustomTextureMaterial;
                    if (texture != null || material != null) {
                        state = 1 | (instance.LightType & 3) << 1 | (instance.ProjectionMode & 3) << 3 | (instance.AutoUpdateCustomTexture ? 1 << 5 : 0);
                        if (texture != null) {
                            state |= 1 << 6;
                            if (instance.CustomTextureIsCubemap) state |= 1 << 7;
                            if (instance.CustomTextureHasDepthSlices) state |= 1 << 8;
                        }
                    }
                }
                if (editorState.CustomSourceOwners[i] != instance || editorState.CustomSourceTextures[i] != texture || editorState.CustomSourceMaterials[i] != material || editorState.CustomSourceStates[i] != state) changed = true;
                editorState.CustomSourceOwners[i] = instance;
                editorState.CustomSourceTextures[i] = texture;
                editorState.CustomSourceMaterials[i] = material;
                editorState.CustomSourceStates[i] = state;
            }
            return changed;
        }

        // Captures only source/layout inputs; shading and receiver metadata never rebuild the shared atlas.
        private bool CaptureEditorShadowSourceState() {
            EditorState editorState = EditorData;
            int count = PointLightVolumeInstances.Length;
            bool changed = editorState.ShadowSourceOwners.Length != count || editorState.ShadowTextureWidth != ShadowTexturesWidth || editorState.ShadowTextureHeight != ShadowTexturesHeight || editorState.ShadowTextureFormat != ShadowTextureFormat;
            editorState.ShadowTextureWidth = ShadowTexturesWidth;
            editorState.ShadowTextureHeight = ShadowTexturesHeight;
            editorState.ShadowTextureFormat = ShadowTextureFormat;
            if (changed) {
                editorState.ShadowSourceOwners = new PointLightVolumeInstance[count];
                editorState.ShadowSourceTextures = new Texture[count];
                editorState.ShadowSourceMaterials = new Material[count];
                editorState.ShadowSourceStates = new int[count];
            }

            for (int i = 0; i < count; i++) {
                PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                Texture texture = null;
                Material material = null;
                int state = 0;
                if (instance != null && instance.IsActive && instance.ShadowMapID >= 0) {
                    if (instance.ShadowMapTexture != null) texture = instance.ShadowMapTexture;
                    else if (instance.ShadowMapMaterial != null) material = instance.ShadowMapMaterial;
                    if (texture != null || material != null) {
                        bool usesCubemap = instance.LightType != 1 || instance.ShadowMapUsesCubemap;
                        state = 1 | (usesCubemap ? 1 << 1 : 0) | (instance.AutoUpdateShadowMap ? 1 << 2 : 0);
                        if (texture != null) {
                            state |= 1 << 3;
                            if (instance.ShadowMapTextureIsCubemap) state |= 1 << 4;
                            if (instance.ShadowMapTextureHasDepthSlices) state |= 1 << 5;
                        }
                    }
                }
                if (editorState.ShadowSourceOwners[i] != instance || editorState.ShadowSourceTextures[i] != texture || editorState.ShadowSourceMaterials[i] != material || editorState.ShadowSourceStates[i] != state) changed = true;
                editorState.ShadowSourceOwners[i] = instance;
                editorState.ShadowSourceTextures[i] = texture;
                editorState.ShadowSourceMaterials[i] = material;
                editorState.ShadowSourceStates[i] = state;
            }
            return changed;
        }

#endregion

#region Editor Probe Baking

        // Exposes the exact runtime-packed light data to the editor probe baker without compiling a second copy of the Point/Spot/Area packing math into Udon or the player build.
        internal int GetEditorProbeBakePointLightData(Vector4[] positions, Vector4[] colors, Vector4[] extraData, Vector4[] directions, Vector4[] customIds, out int missingProjectionCount, out int overflowCount) {
            missingProjectionCount = 0;
            overflowCount = 0;
            if (Application.isPlaying || !enabled || !gameObject.activeInHierarchy || PointLightVolumeInstances == null || positions == null || colors == null || extraData == null || directions == null || customIds == null) return 0;

            int capacity = Mathf.Min(positions.Length, Mathf.Min(colors.Length, Mathf.Min(extraData.Length, Mathf.Min(directions.Length, customIds.Length))));

            UpdateVolumes();
            int count = 0;
            for (int shaderIndex = 0; shaderIndex < _pointLightCount; shaderIndex++) {
                int sourceIndex = _enabledPointIDs[shaderIndex];
                if (sourceIndex < 0 || sourceIndex >= PointLightVolumeInstances.Length) continue;
                PointLightVolumeInstance instance = PointLightVolumeInstances[sourceIndex];
                if (!IsEditorProbeBakePointLight(instance)) continue;

                int resolvedCustomId = sourceIndex < _pointLightCustomIDs.Length ? _pointLightCustomIDs[sourceIndex] : -1;
                if (instance.ProjectionMode != 0 && resolvedCustomId < 0) {
                    missingProjectionCount++;
                    continue;
                }
                if (count >= capacity) {
                    overflowCount++;
                    continue;
                }

                positions[count] = _pointLightPosition[shaderIndex];
                colors[count] = _pointLightColor[shaderIndex];
                Vector4 packedExtraData = _pointLightExtraData[shaderIndex];
                packedExtraData.w = 0f;
                extraData[count] = packedExtraData;
                directions[count] = _pointLightDirection[shaderIndex];
                Vector4 packedCustomId = _pointLightCustomId[shaderIndex];
                packedCustomId.y = DisabledShadingShadowId;
                if (instance.LightType != 2 || instance.ProjectionMode != 2) packedCustomId.w = 0f;
                customIds[count] = packedCustomId;
                count++;
            }

            // UpdateVolumes caps the compact shader list. Count otherwise eligible registry entries past that limit so the bake reports the same global 128-light constraint explicitly.
            if (_pointLightCount >= MaxPointLightCount) {
                for (int i = 0; i < PointLightVolumeInstances.Length; i++) {
                    PointLightVolumeInstance instance = PointLightVolumeInstances[i];
                    if (!IsEditorProbeBakePointLight(instance)) continue;
                    bool packed = false;
                    for (int j = 0; j < _pointLightCount; j++) {
                        if (_enabledPointIDs[j] != i) continue;
                        packed = true;
                        break;
                    }
                    if (!packed) overflowCount++;
                }
            }
            return count;
        }

        // Checks whether a Point Light Volume is eligible for editor light-probe baking.
        private bool IsEditorProbeBakePointLight(PointLightVolumeInstance instance) {
            return instance != null && instance.LightVolumeManager == this && instance.BakeIntoProbes && instance.isActiveAndEnabled && !instance.CompareTag("EditorOnly")
                && instance.Intensity != 0f && instance.Color != Color.black;
        }

#endregion

#region Editor Runtime Recovery And Inspector

        // Rebuilds all derived edit-mode data after scene saves, script reloads and late UdonSharp asset imports. Runtime flags can be restored independently from managed resources, so no cached gate is trusted.
        internal void RebuildEditorRuntimeState() {
            if (Application.isPlaying) return;
            _isUpdatingVolumes = false;
            _volumeDataUpdateRequested = false;
#if UDONSHARP
            _isUpdateProcessRunning = false;
#endif
            _isInitialized = false;
            _isRangeDirty = true;
            _clusteringLightsDirty = true;
            _clusterGeometryUploadPending = false;
            ReleaseClusteringPreview();
            UpdateVolumes();
        }

        // Prevents the generated HideAndDontSave material from becoming unreachable with the editor-state table during an assembly reload.
        internal void ReleaseClusteringPreviewForAssemblyReload() {
            ReleaseClusteringPreview();
            InvalidateTextureCaches(false, true);
            DestroyClusteringMaterial();
        }

        // Editor-only getters for the custom inspector. They add no serialized fields, asset references, or variables to either the Udon program or a player build.
        internal RenderTexture FineClusterMaskPreview => _clusterMask;
        internal RenderTexture CoarseClusterMaskPreview => _coarseClusterMask;
        internal bool FroxelLayoutValidPreview => _froxelLayoutValid;
        internal Vector4 FineFroxelGridParamsPreview => _fineGridParams;
        internal Vector4 CoarseFroxelGridParamsPreview => _coarseGridParams;
        internal Material ClusteringMaterialPreview => GetClusteringMaterial();
        internal bool RuntimeInitializedPreview => _isInitialized;
        internal int ActivePointLightCountPreview => _pointLightCount;
        internal int ActiveShadowCountPreview => _activeShadowCount;
        internal bool ClusteringActivePreview => _clusteringActive;
        internal bool ClusteringUnsupportedPreview => _clusteringUnsupported;
        internal bool ClusteringAllocationFailedPreview => _clusteringAllocationFailed;
        internal bool ClusterMaskValidPreview => _clusterMaskValid;

#endregion
    }
}
#endif
