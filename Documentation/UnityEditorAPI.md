[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | **Scripting API** | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Unity Editor API

<a id="in-the-editor"></a>

| Menu |
| --- |
| [UdonSharp API](./ScriptingAPI.md) |
| **Unity Editor API**<br />• [Setup](#setup)<br />• [Manager Context](#manager-context)<br />• [Authoring Operations](#authoring-operations)<br />• [Atlas Post-Processors](#atlas-post-processors)<br />• [Custom Lightmapper Operations](#custom-lightmapper-operations) |
| [Custom Lightmapper Integration](./CustomLightmapperIntegration.md) |

Use `manager.Editor` to bake shadows, repack volumes, process the atlas or connect a custom lightmapper.

## Setup

Put your code in an Editor-only assembly with direct references to both package assemblies:

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

`red.sim.LightVolumesUdon` supplies the Manager and context types; `red.sim.LightVolumesEditor` supplies the extension methods. `red.sim.LightVolumes` alone isn't enough.

Add these imports:

```csharp
using UnityEngine;
using VRCLightVolumes;
using VRCLightVolumes.Editor;
```

The snippets below belong inside your Editor tool's methods. They assume `manager` is a non-null `LightVolumeManager`; other inputs are described beside each snippet.

These APIs exist under `UNITY_EDITOR && !COMPILER_UDONSHARP`. Call them on Unity's main thread. Save the scene before writing baked assets.

## Manager context

### `manager.Editor`, `IsValid` and `IsBakeryMode`

`manager.Editor` returns a `LightVolumeManagerEditorContext`. Read its flags when your tool needs to check the context or the Manager's baking mode:

```csharp
var editor = manager.Editor;
bool hasManager = editor.IsValid;
bool usesBakery = editor.IsBakeryMode;
```

`IsValid` only checks that the Manager is non-null. Check `manager` before reading `manager.Editor`. A default context returns `false`, `0` or empty arrays from queries; other calls do nothing.

Keep one Manager across the world's loaded scenes. Atlas generation, shadow baking and custom probe calls use the primary Manager, so remove duplicates.

### `AtlasPostProcessorsChanged`

Use this event to refresh your Editor UI after the atlas processing chain changes. Here, `onAtlasChanged` is a `System.Action` owned by your tool, such as an EditorWindow's `Repaint` method. Subscribe when the tool is enabled:

```csharp
var editor = manager.Editor;
editor.AtlasPostProcessorsChanged += onAtlasChanged;
```

Unsubscribe from the same Manager with the same handler when the tool is disabled:

```csharp
var editor = manager.Editor;
editor.AtlasPostProcessorsChanged -= onAtlasChanged;
```

Manual refreshes also raise this event. It does not confirm that an atlas asset has been saved.

## Authoring operations

### `GenerateAtlas()`

Repack the Regular Light Volumes and run their registered post-processors after your tool changes baked data. Normal bakes already do this automatically.

```csharp
manager.Editor.GenerateAtlas();
```

This `void` method only works outside Play Mode. It starts an Editor coroutine and returns before the atlas is ready. Don't read `manager.LightVolumeAtlas` immediately afterward as the new result. Check the Console for errors.

### `BakeShadowMaps()`

Bake lights with both **Shadows** and **Rebake Shadows** enabled. Use it for a batch rebake after your tool changes the scene:

```csharp
manager.Editor.BakeShadowMaps();
```

This `void` method also works in Play Mode, where it schedules runtime bakes on the live lights instead of saving Editor-baked assets. It doesn't force a bake of every light. Check the Console for errors.

## Atlas post-processors

Each stage reads the previous 3D lighting atlas and writes a replacement. Stages run in registration order; the last output becomes `manager.LightVolumeAtlas`. The chain needs a base atlas before it can run.

### Operations

#### `RegisterPostProcessor(CustomRenderTexture texture)`

Register a Custom Render Texture whose material processes packed 3D lighting data. Here, `customRenderTexture` is a configured Custom Render Texture asset:

```csharp
manager.Editor.RegisterPostProcessor(customRenderTexture);
```

The material receives the previous atlas in `_MainTex`. Light Volumes recreates the target as a matching 3D half-float texture and sets its **Update Mode** to **Realtime**. Treat the input as packed lighting data, rather than a 2D color image.

For an ordinary RenderTexture with your own callback, use the descriptor overload under [Custom callbacks](#custom-callbacks). Both registration overloads return `void`, add or update a stage, and refresh the chain when its registration changes. Matching target or callback identities identify the same stage; duplicate matches are collapsed.

#### `GetPostProcessors()`

Get the registered stages for an Inspector or other Editor UI. The return value is an `AtlasPostProcessor[]` copy; changing the array doesn't edit the chain.

```csharp
AtlasPostProcessor[] stages = manager.Editor.GetPostProcessors();
```

#### `ContainsPostProcessor(...)`

Check whether a stage is already registered. Supply a `RenderTexture target`, optionally its `Material material`, or an `AtlasPostProcessor processor`:

```csharp
bool hasTarget = manager.Editor.ContainsPostProcessor(target);
bool hasMaterial = manager.Editor.ContainsPostProcessor(target, material);
bool hasStage = manager.Editor.ContainsPostProcessor(processor);
```

The signatures are `bool ContainsPostProcessor(RenderTexture target, Material material = null)` and `bool ContainsPostProcessor(AtlasPostProcessor processor)`. The descriptor form matches any non-null target or callback identity.

#### `RefreshPostProcessors()`

Run the chain again from `LightVolumeAtlasBase` after changing a material input. This doesn't repack the baked volumes:

```csharp
manager.Editor.RefreshPostProcessors();
```

The method returns `void`. Each target is recreated before its stage runs, so its previous pixels are not preserved.

#### `UnregisterPostProcessor(...)`

Remove a stage when your integration is disabled. Pass the same output target you registered:

```csharp
manager.Editor.UnregisterPostProcessor(target);
```

Both `void UnregisterPostProcessor(RenderTexture target)` and `void UnregisterPostProcessor(AtlasPostProcessor processor)` refresh the remaining chain. The target form removes every stage writing to that target; the descriptor form removes matches by target or callback identity.

### Custom callbacks

#### `RegisterPostProcessor(AtlasPostProcessor processor)`

Use a descriptor when your tool writes the output itself. This Editor-only callback copies the incoming atlas into a `RenderTexture target` asset:

```csharp
var processor = new AtlasPostProcessor(target, null) {
    UpdateWithInput = input => Graphics.CopyTexture(input, target)
};
manager.Editor.RegisterPostProcessor(processor);
```

The constructor is `AtlasPostProcessor(RenderTexture target, Material material, string inputTextureProperty = "_MainTex")`. It sets those three fields. The registration method returns `void`.

| Field | What to supply |
| --- | --- |
| `RenderTexture Target` | Required output. Each run recreates it as Clamp/Trilinear, half-float 3D data matching the base atlas. |
| `Material Material` | Optional material receiving the input texture. Assigning it alone does **not** render the stage. |
| `string InputTextureProperty` | Input property on the material; defaults to `_MainTex`. |
| `Action Update` | Callback that writes the output without an input argument. |
| `Action<Texture> UpdateWithInput` | Callback receiving the previous atlas. Takes priority over `Update` when both are assigned. |

A stage needs a target and a material or callback. A callback or Custom Render Texture must fill the output; an ordinary material alone only receives the input.

For the copy above, match the source and target formats and mip counts. The platform must support 3D texture copies; see Unity's [Graphics.CopyTexture requirements](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Graphics.CopyTexture.html). Your own processor must write every required slice.

The Manager saves target, material and input-property references. Register callbacks again after a domain reload. Use a target asset if the registration must survive scene reloads, and keep the input and target separate.

World builds keep the final atlas reference but remove post-processor registrations. These C# callbacks won't run in the world. For runtime output, use a Custom Render Texture that can regenerate from included source assets, or another runtime writer. A RenderTexture asset doesn't save the pixels rendered in the Editor; see Unity's [RenderTexture lifetime notes](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/RenderTexture.html).

## Custom lightmapper operations

### `GetCustomProbesCount()` and `GetCustomProbes(int id)`

Get the number of eligible Regular Volumes and their world-space voxel positions. For example, get the first volume in a `void` method:

```csharp
int count = manager.Editor.GetCustomProbesCount();
if (count == 0) return;
Vector3[] positions = manager.Editor.GetCustomProbes(0);
```

`GetCustomProbesCount()` returns an `int`. It includes registered, non-null volumes with **Bake** enabled, an active GameObject and no `EditorOnly` tag. `GetCustomProbes(id)` returns a `Vector3[]` for an ID from `0` to `count - 1`.

Use these calls in Edit Mode. IDs can change, so keep volume activation, Manager registration, transforms, resolution and bake settings unchanged until you submit results.

### `SetCustomProbesBaked(...)`

Submit the lighting your baker calculated at those positions. Each SH input below is a `Vector3[]` in the returned probe order:

```csharp
manager.Editor.SetCustomProbesBaked(id, l0, l1r, l1g, l1b);
```

All overloads return `void`:

| Method | Processing |
| --- | --- |
| `SetCustomProbesBaked(int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b)` | No validity; use `manager.Denoise`. |
| `SetCustomProbesBaked(int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, bool denoise)` | No validity; use the supplied denoising choice. |
| `SetCustomProbesBaked(int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity)` | Use validity and `manager.Denoise`. |
| `SetCustomProbesBaked(int id, Vector3[] l0, Vector3[] l1r, Vector3[] l1g, Vector3[] l1b, float[] validity, bool denoise)` | Use validity and the supplied denoising choice. |

Submission processes the arrays before returning; atlas finalization happens later. See [Custom Lightmapper Integration](./CustomLightmapperIntegration.md) for SH conversion, validity, saving and failure handling.
