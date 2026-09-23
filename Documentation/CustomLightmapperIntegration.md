[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | **Scripting API** | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Custom Lightmapper Integration

| Menu |
| --- |
| [UdonSharp API](./ScriptingAPI.md) |
| [Unity Editor API](./UnityEditorAPI.md) |
| **Custom Lightmapper Integration**<br />• [Setup](#setup-and-workflow)<br />• [GetCustomProbesCount](#getcustomprobescount)<br />• [GetCustomProbes](#getcustomprobesint-id)<br />• [SetCustomProbesBaked](#setcustomprobesbaked)<br />• [Probe Order](#volume-ids-and-probe-order)<br />• [SH Layout](#sh-layout)<br />• [Validity And Denoising](#validity-dilation-and-denoising)<br />• [Saving](#saving-and-finalization)<br />• [Failures](#handling-failures) |

Use these methods to get voxel positions for your lightmapper and submit its L0/L1 results. Light Volumes saves the source textures and updates the atlas.

## Setup and workflow

Use an Editor-only assembly with direct references to `red.sim.LightVolumesUdon` and `red.sim.LightVolumesEditor`. See [Unity Editor API setup](./UnityEditorAPI.md#setup) for the asmdef.

Add these imports at the top of your Editor script:

```csharp
using UnityEngine;
using VRCLightVolumes;
using VRCLightVolumes.Editor;
```

The snippets belong inside your Editor tool's methods. `manager` is the world's non-null primary `LightVolumeManager`, with **Baking Mode** set to **Custom Lightmapper**. Keep one Manager across loaded scenes.

Call every `manager.Editor` method on Unity's main thread, outside Play Mode. Save each volume's scene before you submit results. Your lightmapper can calculate lighting asynchronously, but return to the main thread for submission.

> [!IMPORTANT]
> Keep volume activation, Manager registration, transforms, resolution, bake settings and registry order unchanged from query through submission. The IDs don't preserve a snapshot of the original state.

## `GetCustomProbesCount()`

Returns the number of volumes your baker can process. Check it before requesting positions. This snippet belongs in a `void` method:

```csharp
int count = manager.Editor.GetCustomProbesCount();
if (count == 0) return;
```

Process IDs from `0` to `count - 1`. Queries include registered, non-null volumes with **Bake** enabled, an active GameObject and no `EditorOnly` tag. Hierarchy selection doesn't limit this set.

## `GetCustomProbes(int id)`

Returns a `Vector3[]` of world-space voxel centers and recalculates adaptive resolution. For a valid `id`, request the positions and allocate one SH entry per position:

```csharp
Vector3[] positions = manager.Editor.GetCustomProbes(id);
Vector3[] l0 = new Vector3[positions.Length];
Vector3[] l1r = new Vector3[positions.Length];
Vector3[] l1g = new Vector3[positions.Length];
Vector3[] l1b = new Vector3[positions.Length];
```

Fill these arrays with your lightmapper's results at `positions[i]`, using the [SH layout](#sh-layout) below. Keep the returned order.

<a id="first-test-submit-a-known-color"></a>

## `SetCustomProbesBaked(...)`

Submit the completed `Vector3[]` arrays for the same `id`. This replaces that volume's baked source data:

```csharp
manager.Editor.SetCustomProbesBaked(id, l0, l1r, l1g, l1b);
```

This form uses `manager.Denoise` and skips validity-based dilation. To choose denoising for this submission, pass the `bool denoise` argument:

```csharp
manager.Editor.SetCustomProbesBaked(id, l0, l1r, l1g, l1b, denoise: false);
```

Both calls return `void`. Submission processes the arrays before returning and leaves them unchanged. Your tool retains ownership. Check the Console and saved assets for errors.

A successful submission queues atlas finalization and eligible shadow bakes. Submit finished volumes in one Editor callback when practical so they share that work. You don't need to call `GenerateAtlas()` after each volume. See [saving and finalization](#saving-and-finalization) before using the result.

## Volume IDs and probe order

IDs refer to the filtered bake list, rather than the raw `LightVolumeInstances` array. Registry changes can make an ID point to a different volume.

**X changes fastest, then Y, then Z**:

```text
index = x + y * resolutionX + z * resolutionX * resolutionY
```

Supply exactly one entry per position in every SH array and any validity array. If your baker reorders positions, restore this order before submission.

## SH layout

| Array | Contents at probe `i` |
| --- | --- |
| `l0[i]` | Ambient RGB coefficients. |
| `l1r[i]` | X, Y and Z directional coefficients for red. |
| `l1g[i]` | X, Y and Z directional coefficients for green. |
| `l1b[i]` | X, Y and Z directional coefficients for blue. |

For a `UnityEngine.Rendering.SphericalHarmonicsL2 sh` result at probe `i`, copy its L0/L1 coefficients:

```csharp
l0[i] = new Vector3(sh[0, 0], sh[1, 0], sh[2, 0]);
l1r[i] = new Vector3(sh[0, 3], sh[0, 1], sh[0, 2]);
l1g[i] = new Vector3(sh[1, 3], sh[1, 1], sh[1, 2]);
l1b[i] = new Vector3(sh[2, 3], sh[2, 1], sh[2, 2]);
```

Supply linear, Unity-compatible L0/L1 coefficients. Omit L2. Keep L1 in world-space X/Y/Z axes with its original magnitude. The API handles texture packing and its `1.65` L1 multiplier, so don't apply either yourself or rotate L1 into volume-local space.

## Validity, dilation and denoising

### `SetCustomProbesBaked(..., float[] validity)`

Pass validity when your baker can identify bad samples, such as probes inside walls. The API can replace them with nearby valid lighting. This overload uses `manager.Denoise`:

```csharp
manager.Editor.SetCustomProbesBaked(id, l0, l1r, l1g, l1b, validity);
```

### `SetCustomProbesBaked(..., float[] validity, bool denoise)`

Choose denoising independently of the Manager while also supplying validity:

```csharp
manager.Editor.SetCustomProbesBaked(id, l0, l1r, l1g, l1b, validity, denoise: true);
```

Both overloads return `void` and use the same SH arrays as the calls above. `validity` is a `float[]` in the same probe order.

Values use Unity Progressive's **backface-hit fraction** convention:

- Below `manager.DilationBackfaceBias`: valid.
- Equal to or above it: invalid.

For example, with a bias of `0.25`, validity `0.1` is valid and `0.8` is invalid. This is not a mask where `1` means valid. Omit validity when your lightmapper cannot provide compatible values.

With validity data and `DilationIterations > 0`, the API dilates using the Manager's iteration count and backface bias. The Progressive **Dilate Invalid Probes** toggle doesn't affect these calls. To skip dilation, omit validity or set the iteration count to zero. Denoising runs afterward if requested.

## Saving and finalization

Each submission saves three `RGBAHalf` Texture3D assets under:

```text
<Scene Folder>/<Scene Name>/VRCLightVolumes/Temp/
```

Give volumes unique names, which become their source filenames. A successful submission assigns the textures, records the baked rotation and queues finalization through `EditorApplication.delayCall`.

The atlas update needs all three source textures on every registered volume, except those with **Bake** off and **Reserve UV Space** on. Keep source textures for volumes outside your bake set.

The API checks whether textures exist, rather than waiting for new results from every ID. Separate submissions can therefore produce intermediate atlases with both old and new data.

For an asynchronous bake, collect results and submit them together when practical. Save the scene after finalization to keep its texture references and baked rotation.

## Handling failures

Submission methods return `void`. Check Console errors and the saved assets to confirm success.

| Condition | Result |
| --- | --- |
| Default context, Play Mode or a non-primary Manager | Queries return zero/empty. Submissions do nothing. |
| Invalid ID on the primary Manager | An error is logged. The query returns an empty array or the submission is skipped. |
| Null SH arrays or incorrect lengths | An error is logged and the submission is skipped. A supplied validity array must also match the voxel count. |
| Unsaved containing scene | An error is logged. No texture assets are submitted. |
| Failure while saving texture assets | An error is logged. Some texture channels may already be assigned. Fix the save failure and resubmit the whole volume. |
| Authoring state changed during a bake | A stale ID may still pass length checks and target the wrong volume. Prevent those edits until submission is complete. |

`manager.Editor.IsValid` only checks that the Manager is non-null. Check duplicate Managers, IDs and bake completion in your own tool.
