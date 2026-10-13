#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System;
using UnityEngine;
using AtlasPostProcessor = VRCLightVolumes.Editor.AtlasPostProcessor;

namespace VRCLightVolumes {
    // Temporary compatibility surface for integrations written before manager.Editor.
    // Delete this file together with LightVolumeManagerTools.Legacy.cs to remove the supported legacy integration surface.
    public partial class LightVolumeManager {
        [Serializable]
        public struct PostProcessor {
            public RenderTexture RT;
            public Material Mat;
            public string TextureName;
            public Action Update;
            public Action<Texture> UpdateWithInput;
        }

        public static event Action<LightVolumeManager> AtlasPostProcessorsChanged {
            add => EditorAtlasPostProcessorsChanged += value;
            remove => EditorAtlasPostProcessorsChanged -= value;
        }

        public bool IsBakeryMode => EditorIsBakeryMode;

        private sealed class LegacyPostProcessorState {
            public PostProcessor[] View;
            public AtlasPostProcessor[] Source;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LightVolumeManager, LegacyPostProcessorState> LegacyPostProcessorStates
            = new System.Runtime.CompilerServices.ConditionalWeakTable<LightVolumeManager, LegacyPostProcessorState>();

        // Preserves the legacy live-array workflow while the supported facade exposes detached snapshots.
        public PostProcessor[] AtlasPostProcessors {
            get => GetLegacyPostProcessors();
            set => SetLegacyPostProcessors(value);
        }

        public void RegisterPostProcessorCRT(CustomRenderTexture texture) {
            ApplyLegacyPostProcessorChanges();
            EditorRegisterPostProcessorCRT(texture);
            SynchronizeLegacyPostProcessors();
        }

        public void UnregisterPostProcessorCRT(CustomRenderTexture texture) {
            ApplyLegacyPostProcessorChanges();
            EditorUnregisterPostProcessor(texture);
            SynchronizeLegacyPostProcessors();
        }

        public void UnregisterPostProcessor(RenderTexture texture) {
            ApplyLegacyPostProcessorChanges();
            EditorUnregisterPostProcessor(texture);
            SynchronizeLegacyPostProcessors();
        }

        public void UnregisterPostProcessor(PostProcessor processor) {
            ApplyLegacyPostProcessorChanges();
            EditorUnregisterPostProcessor(ToAtlasPostProcessor(processor));
            SynchronizeLegacyPostProcessors();
        }

        public void RegisterPostProcessor(PostProcessor processor) {
            ApplyLegacyPostProcessorChanges();
            EditorRegisterPostProcessor(ToAtlasPostProcessor(processor));
            SynchronizeLegacyPostProcessors();
        }

        public void RefreshAtlasPostProcessors() {
            ApplyLegacyPostProcessorChanges();
            EditorRefreshAtlasPostProcessors();
        }

        private PostProcessor[] GetLegacyPostProcessors() {
            LegacyPostProcessorState state = LegacyPostProcessorStates.GetOrCreateValue(this);
            AtlasPostProcessor[] source = EditorGetAtlasPostProcessors();
            if (!ReferenceEquals(state.Source, source)) SynchronizeLegacyPostProcessors(state, source);
            return state.View;
        }

        private void SetLegacyPostProcessors(PostProcessor[] processors) {
            LegacyPostProcessorState state = LegacyPostProcessorStates.GetOrCreateValue(this);
            state.View = processors ?? Array.Empty<PostProcessor>();
            EditorSetAtlasPostProcessors(ToAtlasPostProcessors(state.View));
            state.Source = EditorGetAtlasPostProcessors();
        }

        private void ApplyLegacyPostProcessorChanges() {
            if (!LegacyPostProcessorStates.TryGetValue(this, out LegacyPostProcessorState state) || state.View == null) return;
            AtlasPostProcessor[] source = EditorGetAtlasPostProcessors();
            if (!ReferenceEquals(state.Source, source)) {
                SynchronizeLegacyPostProcessors(state, source);
                return;
            }
            EditorSetAtlasPostProcessors(ToAtlasPostProcessors(state.View));
            state.Source = EditorGetAtlasPostProcessors();
        }

        private void SynchronizeLegacyPostProcessors() {
            if (!LegacyPostProcessorStates.TryGetValue(this, out LegacyPostProcessorState state)) return;
            SynchronizeLegacyPostProcessors(state, EditorGetAtlasPostProcessors());
        }

        private static void SynchronizeLegacyPostProcessors(LegacyPostProcessorState state, AtlasPostProcessor[] source) {
            if (state.View == null || state.View.Length != source.Length) state.View = new PostProcessor[source.Length];
            for (int i = 0; i < source.Length; i++) state.View[i] = FromAtlasPostProcessor(source[i]);
            state.Source = source;
        }

        private static AtlasPostProcessor[] ToAtlasPostProcessors(PostProcessor[] processors) {
            AtlasPostProcessor[] result = new AtlasPostProcessor[processors.Length];
            for (int i = 0; i < processors.Length; i++) result[i] = ToAtlasPostProcessor(processors[i]);
            return result;
        }

        private static AtlasPostProcessor ToAtlasPostProcessor(PostProcessor processor) {
            return new AtlasPostProcessor {
                Target = processor.RT,
                Material = processor.Mat,
                InputTextureProperty = processor.TextureName,
                Update = processor.Update,
                UpdateWithInput = processor.UpdateWithInput
            };
        }

        private static PostProcessor FromAtlasPostProcessor(AtlasPostProcessor processor) {
            return new PostProcessor {
                RT = processor.Target,
                Mat = processor.Material,
                TextureName = processor.InputTextureProperty,
                Update = processor.Update,
                UpdateWithInput = processor.UpdateWithInput
            };
        }
    }
}
#endif
