#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System;
using UnityEngine;

namespace VRCLightVolumes.Editor {
    // One editor-only stage in the Light Volume atlas post-processing chain.
    [Serializable]
    public struct AtlasPostProcessor {
        public RenderTexture Target;
        public Material Material;
        public string InputTextureProperty;
        public Action Update;
        public Action<Texture> UpdateWithInput;

        public AtlasPostProcessor(RenderTexture target, Material material, string inputTextureProperty = "_MainTex") {
            Target = target;
            Material = material;
            InputTextureProperty = inputTextureProperty;
            Update = null;
            UpdateWithInput = null;
        }
    }

    // Allocation-free editor-only access handle returned by LightVolumeManager.Editor.
    // Import VRCLightVolumes.Editor to make its editor operations available.
    public readonly struct LightVolumeManagerEditorContext {
        internal global::VRCLightVolumes.LightVolumeManager Manager { get; }

        internal LightVolumeManagerEditorContext(global::VRCLightVolumes.LightVolumeManager manager) {
            Manager = manager;
        }

        public bool IsValid => Manager != null;

        // Reports whether this Manager uses Bakery authoring.
        public bool IsBakeryMode => Manager != null && Manager.EditorIsBakeryMode;

        // Raised after this Manager's atlas post-processing chain is refreshed.
        public event Action AtlasPostProcessorsChanged {
            add {
                if (Manager != null) Manager.EditorAddAtlasPostProcessorsChanged(value);
            }
            remove {
                if (Manager != null) Manager.EditorRemoveAtlasPostProcessorsChanged(value);
            }
        }
    }
}
#endif
