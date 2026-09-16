[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [UdonSharp API](./UdonSharpAPI.md) | **Unity Editor API** | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Unity Editor API

Use this API for atlas post-processors, custom lightmappers and authoring tools. Import `VRCLightVolumes.Editor` to access the operations through `manager.Editor`. Runtime world scripts use the [UdonSharp API](./UdonSharpAPI.md) instead.

After [setup](#setup), choose [authoring operations](#authoring-operations), [atlas post-processors](#atlas-post-processors), or the [custom lightmapper workflow](./CustomLightmapperIntegration.md).

## Setup

Put integration code in an Editor-only assembly. Reference both assemblies directly:

```json
{
  "name": "MyLightingIntegration.Editor",
  "references": [
    "red.sim.LightVolumesUdon",
    "red.sim.LightVolumesEditor"
  ],
  "includePlatforms": ["Editor"]
}
```

`red.sim.LightVolumesUdon` contains the Manager and context types; `red.sim.LightVolumesEditor` provides the extension methods. Referencing only `red.sim.LightVolumes` is not enough.

```csharp
using VRCLightVolumes;
using VRCLightVolumes.Editor;

public static class MyLightVolumesTools {
    // Repack after changing baked volume data in an authoring tool.
    public static void RebuildAtlas(LightVolumeManager manager) {
        if (manager == null) return;
        manager.Editor.GenerateAtlas();
    }
}
```

The context exists only under `UNITY_EDITOR && !COMPILER_UDONSHARP`. Keep calls on Unity's main thread. Save the scene before an operation that writes baked assets.

## Manager context

A world uses one Manager across loaded scenes. Atlas generation, shadow baking and custom probe operations apply to the primary Manager. Remove duplicates rather than relying on which one is selected.

| Member | Meaning |
| --- | --- |
| `manager.Editor` | Returns a `LightVolumeManagerEditorContext` value. |
| `bool IsValid` | The context contains a non-null Manager. This does not check primary-Manager status or scene validity. |
| `bool IsBakeryMode` | The Manager uses Bakery authoring. |
| `event Action AtlasPostProcessorsChanged` | Raised after this Manager's atlas processing chain is refreshed. Unsubscribe when your integration is disabled. |

Check `manager` before evaluating `manager.Editor`: a null Manager cannot return a context. A default context's queries return `false`, `0` or an empty array, and its operations do nothing.

## Authoring operations

| Method | Effect |
| --- | --- |
| `void GenerateAtlas()` | Repack Regular Light Volumes, then run the registered post-processors. Use after external baked-data changes; normal bakes already finalize automatically. |
| `void BakeShadowMaps()` | Bake Point Light Volumes with both **Shadows** and **Rebake Shadows** enabled. This does not force a bake of every light. |

Use `GenerateAtlas()` outside Play Mode. `BakeShadowMaps()` also supports Play Mode: it schedules runtime shadow bakes on the live lights instead of saving Editor-baked assets. Both methods return `void`; inspect Console errors if an operation fails.

`GenerateAtlas()` starts an Editor coroutine and returns before packing finishes. Do not read `LightVolumeAtlas` immediately afterward as the new result. `AtlasPostProcessorsChanged` lets an Editor view react when the chain is refreshed, but it is also raised by manual refreshes and does not certify that the atlas asset has been saved.

## Atlas post-processors

A post-processor takes the previous 3D lighting atlas and writes a new one. Use it for custom color adjustments or generated volume data. Stages run in registration order; the final output becomes `manager.LightVolumeAtlas`.

The easiest starting point is a **Custom Render Texture** with a material that processes a 3D atlas:

```csharp
// Register once when your Editor integration is enabled.
manager.Editor.RegisterPostProcessor(customRenderTexture);

// Re-run after changing material inputs, without repacking the base atlas.
manager.Editor.RefreshPostProcessors();

// Remove when your integration is disabled.
manager.Editor.UnregisterPostProcessor(customRenderTexture);
```

The texture's material receives the previous atlas in `_MainTex`. It must process the packed lighting data, not a regular 2D color image. Light Volumes configures the target as a 3D half-float texture matching the base atlas and sets a Custom Render Texture's **Update Mode** to **Realtime**.

### Operations

| Method | Effect |
| --- | --- |
| `AtlasPostProcessor[] GetPostProcessors()` | Return a copy of the registered descriptors. Editing the returned array does not edit the chain. |
| `bool ContainsPostProcessor(RenderTexture target, Material material = null)` | Find a stage by output target, and optionally its material. |
| `bool ContainsPostProcessor(AtlasPostProcessor processor)` | Find a stage by any non-null target or callback identity in the descriptor. |
| `void RegisterPostProcessor(AtlasPostProcessor processor)` | Add or update a stage and refresh the chain. Duplicate target/callback identities are collapsed. |
| `void RegisterPostProcessor(CustomRenderTexture texture)` | Register the texture's material and `Update()` callback. |
| `void UnregisterPostProcessor(RenderTexture target)` | Remove all stages writing to that target and refresh the chain. |
| `void UnregisterPostProcessor(AtlasPostProcessor processor)` | Remove matching target/callback identities and refresh the chain. |
| `void RefreshPostProcessors()` | Run the chain again, starting from `LightVolumeAtlasBase`. |

### Custom callbacks

`AtlasPostProcessor` has these fields:

| Field | Contract |
| --- | --- |
| `RenderTexture Target` | Required output. Before each run it is recreated as Clamp/Trilinear, half-float 3D data matching the base atlas. Previous target pixels are not preserved. |
| `Material Material` | Optional material receiving the input texture. Assigning it alone does **not** render the stage. |
| `string InputTextureProperty` | Input property on the material, defaulting to `_MainTex`. |
| `Action Update` | Callback that writes the output. |
| `Action<Texture> UpdateWithInput` | Callback receiving the previous atlas. Takes priority over `Update` when both are assigned. |

The constructor `AtlasPostProcessor(RenderTexture target, Material material, string inputTextureProperty = "_MainTex")` initializes the three matching fields. A stage needs a target and at least one material or callback. Ensure that a callback or Custom Render Texture actually fills the target; an ordinary material alone only receives the input.

For an Editor-only test of callback registration, this stage copies the incoming 3D atlas without changing it:

```csharp
using UnityEngine;
using VRCLightVolumes;
using VRCLightVolumes.Editor;

public static class CopyLightVolumeAtlas {
    // Target should be a RenderTexture asset owned by this integration.
    public static void Register(LightVolumeManager manager, RenderTexture target) {
        if (manager == null || target == null) return;
        manager.Editor.RegisterPostProcessor(new AtlasPostProcessor {
            Target = target,
            UpdateWithInput = input => Graphics.CopyTexture(input, target)
        });
    }

    // Remove the stage using the same target identity.
    public static void Unregister(LightVolumeManager manager, RenderTexture target) {
        if (manager == null) return;
        manager.Editor.UnregisterPostProcessor(target);
    }
}
```

This copy example requires matching source/target formats, mip counts and platform support for copying 3D textures; see Unity's [Graphics.CopyTexture requirements](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Graphics.CopyTexture.html). Custom processing must write every required slice. The chain never runs when the base atlas is missing.

Target, material and input-property references are serialized with the Manager. Delegate callbacks are not: register them again after a domain reload. Keep your target as a persistent asset when the registration must survive scene reloads. Avoid a target that is also its own input.

World builds retain the final atlas reference and remove the Manager's post-processor registration data. The C# callbacks above do not run in the world. A RenderTexture asset does not preserve its Editor-rendered pixels across loading; a runtime output needs a Custom Render Texture that can regenerate from included source assets, or another runtime writer. See Unity's [RenderTexture lifetime notes](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/RenderTexture.html).

## Custom lightmapper operations

| Method | Effect |
| --- | --- |
| `int GetCustomProbesCount()` | Count active, bake-enabled Regular Volumes in the Manager's filtered registry. |
| `Vector3[] GetCustomProbes(int id)` | Get one volume's world-space voxel centers. |
| `void SetCustomProbesBaked(int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b)` | Submit L0/L1 using the Manager's denoising setting. |
| `void SetCustomProbesBaked(int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, bool denoise)` | Submit with explicit denoising. |
| `void SetCustomProbesBaked(int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity)` | Submit validity data too, using Manager denoising. |
| `void SetCustomProbesBaked(int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity, bool denoise)` | Submit validity with explicit denoising. |

These calls are for Edit Mode. IDs are temporary indices, not persistent object IDs. Keep volume activation, Manager registration, transforms, resolution and bake settings unchanged between query and submission.

See [Custom Lightmapper Integration](./CustomLightmapperIntegration.md) for a working submission example, SH conversion, validity rules and failure handling.
