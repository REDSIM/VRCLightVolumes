[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [UdonSharp API](./UdonSharpAPI.md) | [Unity Editor API](./UnityEditorAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Froxel Clustering

**Guides:** [Overview](./HowToUse.md) · [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) · [Point Light Volumes](./HowToUse_PointLightVolumes.md) · **Froxel Clustering** · [Shadows](./HowToUse_Shadows.md) · [Material Sources](./HowToUse_PointLightMaterialSources.md) · [Area Light Emission](./HowToUse_AreaLightEmission.md) · [AudioLink](./HowToUse_AudioLinkIntegration.md) · [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) · [Debugging](./HowToUse_Debugging.md) · [How It Works](./HowToUse_HowItWorks.md)

Froxel Clustering helps shaders skip Point Light Volumes that cannot reach a surface. It divides the camera's view into small 3D cells, called **froxels**, and makes a light list for each cell. A material checks that short list instead of every active light.

It helps most when you have many small lights spread across a scene. It helps less when only a few lights are active or most lights overlap the same area. Regular baked Light Volumes are unaffected.

## Start With The Defaults

On the **Light Volume Manager**, find the **Froxel Clustering** section. New Managers use:

| Setting | Default | What it changes |
| --- | --- | --- |
| **Clustering Enabled** | On | Enables the optimization. |
| **Min Lights Count** | `8` | Below this many active Point Light Volumes, use the ordinary light loop. |
| **Angular Density** | `1` | How finely the grid divides the view horizontally and vertically. |
| **Slices Count** | `100` | How finely it divides near-to-far depth. |
| **Coarse Reduction** | `4x` | Size of the simpler grid used to prepare the final grid. |
| **Shadow Culling** | Off | Additionally skip lights in cells completely hidden by their shadows. |

Current compatible shaders use clustering automatically. No material toggle is required. Test a perspective Scene view, then compare frame time with clustering on and off in a representative target build.

Open the Manager's **Debug** foldout and check **Clustering Status**. It should show **Active** in a supported perspective view once **Active Point Lights** reaches **Min Lights Count**. Below the default threshold of eight lights, the ordinary light loop is expected.

Turning clustering on should preserve the lighting. Judge the change by frame time; it does not make lights brighter or shadows more detailed.

Keep one primary Light Volume Manager across the loaded world scenes. Its light lists and clustering textures are shared by the shaders.

## Tune The Lights Before The Grid

1. Inspect each light's **Debug Range**. Reduce unnecessary reach and overlap first.
2. Profile a busy view with the default grid. Also check a quiet area; clustering has its own setup cost.
3. Increase **Angular Density** only if finer horizontal/vertical separation is useful. Both dimensions grow, so doubling it uses roughly four times as many cells.
4. Increase **Slices Count** only if lights at different depths need better separation. Doubling it roughly doubles grid memory and build work.
5. Compare the new result with the original. A finer grid is only worthwhile when the skipped lighting work pays for it.

**Coarse Reduction** is an advanced tuning control. `2x` spends more work preparing the grid but can give the final pass fewer candidate lights. `8x` makes preparation cheaper but leaves more candidates to check. Keep `4x` unless profiling shows a reason to change it.

The Manager shows estimated **Fine** and **Coarse** grid dimensions. It also includes their allocations in **Data size in VRAM**. Avoid maximum settings: the maximum 256 × 256 × 256 Fine grid alone needs about 256 MiB, before the Coarse grid and shadow-culling data.

## Debug Views

In the Scene view draw-mode menu, open **Light Volumes Debug** and select:

- **VRCLV Fine Clustering:** the final candidate-light lists used by materials.
- **VRCLV Coarse Clustering:** the larger cells used to prepare those lists.

The colors identify different light lists. **They are not a heat map:** red is not slower than blue, and brightness does not show the number of lights. Matching colors usually mean matching candidate lists. Black means an empty list, a point outside the grid, or unavailable clustering.

If most surfaces have the same color, many of them may share the same candidates. Check light ranges before raising grid resolution. Coarse cells are deliberately larger; extra candidates in that view are expected. Shadow Culling changes only the Fine view.

The same view with 12 Point Light Volumes:

| Shaded | Fine Clustering | Coarse Clustering |
| --- | --- | --- |
| ![Twelve separated lights on a floor in the normal Shaded view](./Images/clustering-shaded.png) | ![Fine clustering view showing smaller regions with different candidate-light lists](./Images/clustering-fine.png) | ![Coarse clustering view showing larger regions with different candidate-light lists](./Images/clustering-coarse.png) |

Colors identify candidate lists, **not cost**. The Coarse view has larger blocks; the Fine pass narrows down the candidates. Click an image to inspect it at full size.

## Shadow-Assisted Culling

Try **Shadow Culling** for scenes where walls and other large blockers hide many shadowed lights. It uses existing shadow maps to remove a light from a cell only when the whole cell can be treated as shadowed. It does not bake extra shadows.

This needs additional GPU work and texture memory. Leave it off when shadows rarely hide complete cells or profiling shows no gain.

- The light needs a valid shadow map and **Shading Strength = 1**. Lower strengths keep some unshadowed light, so that light cannot be removed this way.
- Baked and one-shot runtime shadows can use this optimization.
- Continuously auto-updated shadow sources temporarily use geometry-only clustering, avoiding a new shadow hierarchy every frame.
- **Shadow Bleed Reduction** affects both visible shadows and the culling threshold. Tune it for the image first.

The shadow hierarchy's resolution is chosen automatically. There is no separate Hi-Z resolution setting.

## VR, Mirrors And Other Cameras

The runtime grid follows VRChat's primary screen camera, centered between the eyes in VR. Eye offsets are included automatically.

Mirrors and other cameras can reuse the grid for positions inside it. For positions outside it, shaders fall back to the full light list. This keeps those views lit correctly, though they may get less speedup. Orthographic Scene views do not preview clustering.

Masks are reused when the existing grid still covers the camera and lights safely. When a rebuild is needed, its Coarse and Fine passes finish in that frame; there is no intentionally delayed lighting update.

## Limits And Fallbacks

Clustering does not raise the **128 active Point Light Volumes** limit or **Additive Max Overdraw**. The latter still caps expensive light evaluation per pixel, including some lights whose sampled cookie or shadow ultimately contributes no visible light.

Unsupported shaders/devices, a missing perspective camera, an unavailable grid or a point outside its bounds use the ordinary light loop. If only Shadow Culling is unavailable, geometry-based clustering can continue.

If a script enables clustering after startup, make sure the Manager's **Shader Stripping** settings retain it. Automatic stripping follows the authored scene settings.

## For Shader Developers

The current integration supports clustering on shader target 3.5+ with its D3D11, GLCore, Vulkan, GLES3 and Metal capability guard. Target 3.5 consumers use a bit-iteration fallback; target 4.5+ can use `firstbitlow`. The build shader targets 3.5, including for Quest/GLES3.

Each cell stores a 128-bit candidate mask. The Coarse pass finds possible lights; the Fine pass refines that result. Range, Spot cone and Area front-side tests are conservative: extra candidates are allowed, then normal per-pixel lighting rejects them precisely.

A custom shader that never needs clustering can opt out before including the library:

```hlsl
#define VRCLV_DISABLE_CLUSTERING 1
#include "Packages/red.sim.lightvolumes/Shaders/LightVolumes.cginc"
```

This is a compile-time choice for that shader. The Manager cannot turn clustering back on for it. Keep it as an include-time option, rather than adding a material keyword and extra variants. The public lighting API remains the same.
