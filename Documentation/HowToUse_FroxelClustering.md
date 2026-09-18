[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Froxel Clustering

| Menu |
| --- |
| [Overview](./HowToUse.md) |
| [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) |
| [Point Light Volumes](./HowToUse_PointLightVolumes.md) |
| **Froxel Clustering**<br />• [Start With The Defaults](#start-with-the-defaults)<br />• [Tune The Lights Before The Grid](#tune-the-lights-before-the-grid)<br />• [Debug Views](#debug-views)<br />• [Shadow-Assisted Culling](#shadow-assisted-culling)<br />• [VR, Mirrors And Other Cameras](#vr-mirrors-and-other-cameras)<br />• [Limits And Fallbacks](#limits-and-fallbacks)<br />• [For Shader Developers](#for-shader-developers) |
| [Shadows](./HowToUse_Shadows.md) |
| [Material Sources](./HowToUse_PointLightMaterialSources.md) |
| [Area Light Emission](./HowToUse_AreaLightEmission.md) |
| [AudioLink](./HowToUse_AudioLinkIntegration.md) |
| [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) |
| [Debugging](./HowToUse_Debugging.md) |
| [How It Works](./HowToUse_HowItWorks.md) |

Froxel Clustering skips lights that can't reach a surface. It divides the view into small 3D cells called **froxels**.

Use it for lights spread across different areas. It helps less when most lights overlap, and doesn't affect baked Regular Light Volumes.

## Start With The Defaults

On the **Light Volume Manager**, find the **Froxel Clustering** section. New Managers use:

| Setting | Default | What it changes |
| --- | --- | --- |
| **Clustering Enabled** | On | Enables the optimization. |
| **Min Lights Count** | `8` | Start clustering at this many active Point Light Volumes. |
| **Angular Density** | `1` | How finely the grid divides the view horizontally and vertically. |
| **Slices Count** | `100` | How finely it divides near-to-far depth. |
| **Coarse Reduction** | `4x` | Size of the simpler grid used to prepare the final grid. |
| **Shadow Culling** | Off | Additionally skip lights in cells completely hidden by their shadows. |

Current compatible shaders use clustering automatically. Compare it on and off in the same view, then test a build on your target device.

In a perspective Scene view, open the Manager's **Debug** foldout. **Clustering Status** should show **Active** once **Active Point Lights** reaches **Min Lights Count**. Fewer than eight lights won't use clustering with the defaults.

The lighting should look the same. Compare frame time to decide whether clustering helps your scene.

## Tune The Lights Before The Grid

1. Inspect each light's **Debug Range**. Reduce unnecessary reach and overlap first.
2. Profile a busy view with the default grid. Also check a quiet area; clustering has its own setup cost.
3. Try a higher **Angular Density** to separate lights across the view more finely.
4. Try a higher **Slices Count** to separate lights at different depths.
5. Keep a change only if it improves frame time. A finer grid can also make the scene slower.

Leave **Coarse Reduction** at `4x` unless a measured comparison shows another value works better.

## Debug Views

In the Scene view draw-mode menu, open **Light Volumes Debug** and select:

- **VRCLV Fine Clustering:** groups of lights in the final grid.
- **VRCLV Coarse Clustering:** groups of lights in the larger cells.

Colors identify different groups of possible lights, **not their cost or brightness**. Black means no lights in the cell, a point outside the grid, or inactive clustering.

If most surfaces have the same color, check light ranges before increasing grid detail. Coarse cells are larger. **Shadow Culling** changes only the Fine view.

The same view with 12 Point Light Volumes:

| Shaded | Fine Clustering | Coarse Clustering |
| --- | --- | --- |
| ![Twelve separated lights on a floor in the normal Shaded view](./Images/clustering-shaded.png) | ![Fine clustering view showing smaller regions with different candidate-light lists](./Images/clustering-fine.png) | ![Coarse clustering view showing larger regions with different candidate-light lists](./Images/clustering-coarse.png) |

Compare the larger Coarse blocks with the smaller Fine regions. Click an image to inspect it at full size.

## Shadow-Assisted Culling

Try **Shadow Culling** (Hi-Z) when shadows cover large parts of a light's range. It uses the shadow maps to skip light calculations in fully shadowed cells. Keep it on only if it improves frame time.

Use baked or one-shot runtime shadows with **Shading Strength = 1**. Continuously updated shadows don't use this extra optimization. Tune **Shadow Bleed Reduction** for the visible result first.

## VR, Mirrors And Other Cameras

VR needs no extra setup. Check mirrors and other cameras when comparing performance: they can get less benefit than the main view. Use a perspective Scene view to preview clustering.

## Limits And Fallbacks

Clustering doesn't raise the **128 active Point Light Volumes** limit or **Additive Max Overdraw**. Reduce overlap if lights disappear at those limits.

Lights still work when clustering is unavailable. Check **Clustering Status** and your [shader's support](./CompatibleShaders.md) if it never becomes active.

If a script enables clustering after startup, make sure the Manager's **Shader Stripping** settings retain it. Automatic stripping follows the authored scene settings.

## For Shader Developers

See [Shader Integration](./ForDevelopers.md) for the public lighting calls and [clustering technical details](./TechnicalDetails.md#froxel-clustering) for shader requirements and opt-out settings.
