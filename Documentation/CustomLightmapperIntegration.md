[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [UdonSharp API](./UdonSharpAPI.md) | [Unity Editor API](./UnityEditorAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Custom Lightmapper Integration

Use `manager.Editor` to bake Regular Light Volumes with your own lightmapper. It provides each volume's world-space voxel centers. Your lightmapper returns lighting at those same positions; Light Volumes saves and packs the result into its atlas.

This is an Editor API. Call it on Unity's main thread, outside Play Mode. Save every containing scene before submitting results.

Start with the [known-color test](#first-test-submit-a-known-color), then check [probe order](#volume-ids-and-probe-order) and [SH layout](#sh-layout) before connecting your baker. [Validity](#validity-dilation-and-denoising) is optional; [saving and finalization](#saving-and-finalization) applies to every integration.

## Setup and workflow

Use the assembly references and namespaces from [Unity Editor API](./UnityEditorAPI.md#setup). Your Editor asmdef must directly reference both `red.sim.LightVolumesUdon` and `red.sim.LightVolumesEditor`.

1. Resolve the world's single `LightVolumeManager`. Do not create a Manager for every additive scene.
2. Select **Custom Lightmapper** as its **Baking Mode** for this workflow.
3. Call `manager.Editor.GetCustomProbesCount()`.
4. For every ID from `0` to `count - 1`, get positions with `manager.Editor.GetCustomProbes(id)`.
5. Bake one L0 vector and three L1 vectors for each position.
6. Submit the arrays with `manager.Editor.SetCustomProbesBaked(id, ...)`.

Keep volume activation, Manager registration, transforms, resolution, bake settings and registry ordering unchanged from query through submission. The API has no snapshot or persistent bake-job ID.

```csharp
int count = manager.Editor.GetCustomProbesCount();
Vector3[] positions = manager.Editor.GetCustomProbes(id);

manager.Editor.SetCustomProbesBaked(id, l0, l1r, l1g, l1b);
manager.Editor.SetCustomProbesBaked(id, l0, l1r, l1g, l1b, denoise);
manager.Editor.SetCustomProbesBaked(id, l0, l1r, l1g, l1b, validity);
manager.Editor.SetCustomProbesBaked(id, l0, l1r, l1g, l1b, validity, denoise);
```

Successful submissions queue atlas and eligible shadow finalization automatically. Submit completed volumes from one Editor callback when possible, so they share that work. Do not generate the atlas after every volume.

## First test: submit a known color

Before connecting your actual lightmapper, verify the submission path with flat ambient lighting. This example writes the supplied **linear RGB** color into every voxel and leaves directional lighting at zero:

```csharp
using UnityEngine;
using VRCLightVolumes;
using VRCLightVolumes.Editor;

public static class CustomLightmapperTest {
    // Writes test lighting to every eligible volume with Bake enabled.
    public static void WriteUniformLighting(LightVolumeManager manager, Vector3 linearRgb) {
        if (manager == null) return;

        int volumeCount = manager.Editor.GetCustomProbesCount();
        for (int id = 0; id < volumeCount; id++) {
            Vector3[] positions = manager.Editor.GetCustomProbes(id);
            int count = positions.Length;
            Vector3[] l0 = new Vector3[count];
            Vector3[] l1r = new Vector3[count];
            Vector3[] l1g = new Vector3[count];
            Vector3[] l1b = new Vector3[count];

            for (int i = 0; i < count; i++) l0[i] = linearRgb;
            manager.Editor.SetCustomProbesBaked(id, l0, l1r, l1g, l1b, false);
        }
    }
}
```

Call it from your Editor tool with a color such as `new Vector3(0.2f, 0.05f, 0.01f)`. After the delayed atlas update, an unlightmapped object with a compatible shader inside the volume should receive that warm ambient light. Test on a copy of your scene: this replaces the baked source data of every registered volume with an active GameObject and **Bake** enabled, except `EditorOnly` objects. It does not use the Hierarchy selection.

For the real integration, replace the constant values with your lightmapper's results at `positions[i]`. An asynchronous lightmapper may run its own computation elsewhere, but return to Unity's main thread for every `manager.Editor` call. Submission reads and processes the arrays synchronously without modifying them; the caller retains ownership.

## Volume IDs and probe order

The queried set consists of registered, non-null volumes with **Bake** enabled, an active GameObject and no `EditorOnly` tag. IDs index this filtered set, not the raw `LightVolumeInstances` array. They can refer to a different volume after registry changes.

`GetCustomProbes(id)` recalculates adaptive resolution and returns voxel centers in world space. **X changes fastest, then Y, then Z**:

```text
index = x + y * resolutionX + z * resolutionX * resolutionY
```

Every submitted SH array, and any validity array, must contain exactly one entry per position in this order. Do not reorder positions for your baker without restoring the original order before submission.

## SH layout

| Array | Contents at probe `i` |
| --- | --- |
| `l0[i]` | Ambient RGB coefficients. |
| `l1r[i]` | X, Y and Z directional coefficients for red. |
| `l1g[i]` | X, Y and Z directional coefficients for green. |
| `l1b[i]` | X, Y and Z directional coefficients for blue. |

For Unity `SphericalHarmonicsL2 sh`, use:

```csharp
l0[i] = new Vector3(sh[0, 0], sh[1, 0], sh[2, 0]);
l1r[i] = new Vector3(sh[0, 3], sh[0, 1], sh[0, 2]);
l1g[i] = new Vector3(sh[1, 3], sh[1, 1], sh[1, 2]);
l1b[i] = new Vector3(sh[2, 3], sh[2, 1], sh[2, 2]);
```

Only L0/L1 is stored; omit L2. Supply linear, Unity-compatible coefficients with the L1 directions expressed in world-space X/Y/Z axes. Do not rotate them into the volume's local frame. Do not normalize L1, pre-pack the texture channels or apply the package's internal `1.65` L1 packing multiplier yourself.

## Validity, dilation and denoising

Validity helps replace bad samples, such as probes inside walls, with nearby valid lighting. The values follow Unity Progressive's **backface-hit fraction** convention:

- Below `manager.DilationBackfaceBias`: valid.
- Equal to or above it: invalid.

For example, with a bias of `0.25`, validity `0.1` is valid and `0.8` is invalid. This is not a mask where `1` means valid. Omit validity when your lightmapper cannot provide compatible values.

| Overload suffix | Dilation | Denoising |
| --- | --- | --- |
| None | Off | `manager.Denoise` |
| `bool denoise` | Off | Supplied value |
| `float[] validity` | Uses validity | `manager.Denoise` |
| `float[] validity, bool denoise` | Uses validity | Supplied value |

With a validity array, `DilationIterations > 0` enables dilation using the Manager's iteration count and backface bias. The Progressive **Dilate Invalid Probes** toggle does not control this custom-API path. Set the iteration count to zero or omit validity to skip it. Denoising, if requested, runs afterward.

## Saving and finalization

Each submission saves three `RGBAHalf` Texture3D assets under:

```text
<Scene Folder>/<Scene Name>/VRCLightVolumes/Temp/
```

Keep volume names unique: the generated source filenames use them. A successful submission assigns those textures to the volume, records its baked rotation, and queues finalization with `EditorApplication.delayCall`.

The final atlas is generated only when every registered volume has all three source textures, except volumes with **Bake** off and **Reserve UV Space** on. Existing textures on volumes outside your bake set are therefore still required. The API does not wait for a new result from each queried ID; it checks that source textures exist. If old sources remain, staggered submissions can generate intermediate atlases using a mixture of old and new data.

For a multi-volume asynchronous bake, collect finished results and submit them together when practical. After finalization, save the scene so its texture references and bake rotation are retained.

## Handling failures

The submission methods return `void`. Check Console errors and the resulting assets rather than treating a return from the call as proof of success.

| Condition | Result |
| --- | --- |
| Default context, Play Mode or a non-primary Manager | Queries return zero/empty; submissions do nothing. |
| Invalid ID on the primary Manager | An error is logged. The query returns an empty array or the submission is skipped. |
| Null SH arrays or incorrect lengths | An error is logged and the submission is skipped. A supplied validity array must also match the voxel count. |
| Unsaved containing scene | An error is logged; no texture assets are submitted. |
| Failure while saving texture assets | An error is logged. Successfully saved texture channels can already be assigned; the operation is not an all-or-nothing transaction. Fix the save failure and resubmit the full volume. |
| Authoring state changed during a bake | A stale ID may still pass length checks and target the wrong volume. Prevent those edits until submission is complete. |

`manager.Editor.IsValid` only checks for a non-null Manager. It does not validate duplicate Managers, outstanding IDs or whether all bake results have arrived.
