[VRC Light Volumes](../README.md) | **How to Use** | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# Regular Light Volumes

| Menu |
| --- |
| [Overview](./HowToUse.md) |
| **Regular Light Volumes**<br />• [Place Volumes Where Objects Need Lighting](#place-volumes-where-objects-need-lighting)<br />• [Choose A Useful Resolution](#choose-a-useful-resolution)<br />• [Bake And Reuse The Data](#bake-and-reuse-the-data)<br />• [Generate Fallback Light Probes](#generate-fallback-light-probes)<br />• [Additive Light Volumes](#additive-light-volumes)<br />• [Match Brightness Without Rebaking](#match-brightness-without-rebaking)<br />• [Inspector Reference](#inspector-reference) |
| [Point Light Volumes](./HowToUse_PointLightVolumes.md) |
| [Froxel Clustering](./HowToUse_FroxelClustering.md) |
| [Shadows](./HowToUse_Shadows.md) |
| [Material Sources](./HowToUse_PointLightMaterialSources.md) |
| [AudioLink](./HowToUse_AudioLinkIntegration.md) |
| [TV Screens](./HowToUse_TVScreensIntegration.md) |
| [Debugging](./HowToUse_Debugging.md) |
| [How It Works](./HowToUse_HowItWorks.md) |

![Light Volume cells showing the baked lighting around scene objects](./Preview_3.png)

A Regular Light Volume stores baked lighting in a 3D grid of voxels. Each pixel on an avatar or prop samples nearby voxels, so lighting varies across its surface instead of using one lighting sample for the whole object.

Use regular volumes for rooms, outdoor areas and other stationary lighting. Bake with a [supported lightmapper](./TechnicalDetails.md#supported-lightmappers). For setup steps, see [Setup Regular Light Volumes](./HowToUse.md#setup-regular-light-volumes).

## Place Volumes Where Objects Need Lighting

Cover areas where players can move and where props need lighting. You can leave other areas uncovered. They use Unity Light Probes when **Light Probes Blending** is enabled.

![Several volumes covering different rooms and heights](./Preview_10.png)

A practical starting layout is:

1. One large, low-density volume for an open area with soft lighting.
2. Smaller, higher-density volumes for rooms with sharper shadows or more detailed lighting.
3. Slight overlaps at doorways and other transitions.

Higher **Weight** gives a volume priority wherever the boxes overlap. Set it in the Manager's **Light Volumes** list. For example, keep a large outdoor volume at **Weight** `0` and give a detailed room volume **Weight** `1`.

**Smooth Blending** controls the width of the transition at a volume's edges, in meters. Make the overlap wider than the blend region. For example, overlap by `0.5 m` with **Smooth Blending** set to `0.25`. Check the result on a moving prop.

To blend from a volume into an uncovered area, keep **Light Probes Blending** enabled and disable **Sharp Bounds** on the Manager. This softens all outer edges, so extend the volume beyond the area that needs its full lighting.

> [!TIP]
> If you create a Light Volume as a child of a Reflection Probe, it starts with that probe's bounds. You can then resize it independently.

### Edit The Bounds

Use **Edit Bounds** and drag the face handles, or use Unity's Transform tools. While dragging a bounds handle:

- Hold **Alt** (Option on macOS) to resize both opposite faces.
- Hold **Shift** to keep the box's proportions while resizing.

Moving or resizing a baked volume moves or stretches its stored lighting. Rebake after changing the bounds when the volume should still match the stationary room.

## Choose A Useful Resolution

A **voxel** is one cell in the lighting grid. With **Adaptive Resolution** enabled, **Voxels Per Unit** sets the number of cells along one meter.

| Density | Approximate cell width | Usage |
| --- | --- | --- |
| `1` | `1 m` | Large areas with very soft lighting |
| `3` | `0.33 m` | Medium-sized rooms with soft lighting |
| `6` | `0.17 m` | Small rooms with sharp shadows and detailed lighting |

These are starting points. Bake, move a test prop through the lighting, and increase density only where the result is too coarse.

Use **Preview Voxels** to inspect the grid. For a doorway or sharp shadow, try a small, dense volume around that detail before increasing the whole world's resolution.

## Bake And Reuse The Data

Each bake saves a volume's lighting in three source 3D textures: **Texture 0**, **Texture 1** and **Texture 2** in its Inspector.

**Pack Light Volumes** combines the source textures from all volumes into one shared 3D texture, called an atlas. This is the final texture used by the world. Packing runs automatically after a successful bake. It reuses the existing baked data. It does not calculate new lighting.

Light Volumes includes the packed atlas in the VRChat build and removes its references to the source textures. Keep those source assets in your Unity project: they are needed whenever you pack the volumes again.

You can save a baked volume as a prefab and reuse it in another scene:

1. Disable **Bake** on the volume to keep its existing lighting during later scene bakes.
2. Save the volume's GameObject as a prefab. Keep its three source texture assets with it when copying it to another project.
3. Add the prefab to the destination scene. Check that it appears in the scene's **Light Volume Manager**, then click **Pack Light Volumes** to include its lighting in that scene's atlas.

## Generate Fallback Light Probes

Keep ordinary Unity Light Probes alongside Light Volumes to light avatars and materials without Light Volume support. They also supply lighting outside volume bounds when **Light Probes Blending** is enabled.

1. Select a volume and click **Generate Light Probes**.
2. Start with the lower density offered in the window.
3. Click **Create Light Probe Group**.
4. Select the new child object and edit its points. Move points inside solid walls or floors into open space. Add points around important changes in lighting.
5. Bake the scene again.

Edit the generated Light Probe Group separately if you later resize the volume or change its resolution. See Unity's [probe placement guide](https://docs.unity3d.com/2022.3/Documentation/Manual/LightProbes-Placing-Scripting.html) for placement tips.

## Additive Light Volumes

An **Additive** volume adds its baked lighting on top of the base lighting. It can also light static lightmapped surfaces when their shader supports additive volumes.

Use one for a baked group of lights that should switch, change color or move together. For a single flashlight or lamp, a Point Light Volume is usually easier to set up.

![An additive volume adding warm light to the room and props](./Preview_11.png)

### Example: A Switchable Group Of Lamps

Bake the lamps separately so their light is not already present in the main scene's lightmaps:

1. Save a copy of the scene for the additive bake. Open it on its own.
2. Keep the room geometry, but disable lights and emissive lighting that do not belong to the switchable group. Remove any unwanted environment lighting from this bake as well.
3. Place a Light Volume around the area the lamps illuminate. Let the light fade before the box ends so the edge is not visible.
4. Bake the lighting. Check that the volume contains only the lighting you want to toggle.

![An isolated light being baked for an additive volume](./Preview_12.png)

Then use that result in the main scene:

1. Preserve the baked volume as a prefab, or copy its GameObject, and bring it into the main scene. Keep the generated texture assets.
2. Enable **Additive** and disable **Bake** on this volume. Check that it appears in the main scene's Manager list.
3. Bake the main scene with the switchable lamps' bake lights disabled. Otherwise their lighting will remain visible when the additive volume is off.
4. Click **Pack Light Volumes** after importing the volume if the main scene has not been baked again.
5. Toggle the additive volume's GameObject to test the off/on result.

You can also use `SetColor()` and `SetIntensity()` through the [UdonSharp API](./ScriptingAPI.md#udonsharp-api). Enable **Dynamic** and the Manager's **Auto Update Volumes** only if the volume itself will move.

Moving an additive volume moves its baked light and shadows together. It will not cast new shadows from nearby objects. For shadows that follow changes in the scene, see [Point Light Volume shadows](./HowToUse_Shadows.md).

## Match Brightness Without Rebaking

Use **Color** and **Intensity** for a simple tint or brightness multiplier.

The **Color Correction** controls adjust the baked data:

- **Exposure** makes the whole result brighter or darker.
- **Shadows** changes the darker values.
- **Highlights** changes the brighter values.

Use these to match the room's lightmaps or remove unwanted ambient glow from an additive bake. For missing light or shadows, correct the scene lighting and rebake.

## Inspector Reference

| Parameter | What to use it for |
| --- | --- |
| **Edit Bounds** | Resize the volume with face handles in the Scene view. |
| **Preview Voxels** | Show the lighting grid and, after baking, its stored lighting. |
| **Size in VRAM / Size in bundle** | Estimate texture memory and compressed build size. |
| **Dynamic** | Allow the stored lighting to move with the Transform in game. Also enable the Manager's **Auto Update Volumes**, or update the Transform through the API. |
| **Additive** | Add this lighting on top of the base lighting. |
| **Color / Intensity** | Tint or scale the volume's lighting. |
| **Weight** (Manager list) | Give higher values to volumes that should take priority in overlaps. |
| **Smooth Blending** | Width of the edge transition, in meters. |
| **Texture 0 / 1 / 2** | The three source textures produced by a bake. Keep them for future atlas rebuilds. |
| **Exposure / Shadows / Highlights** | Adjust the brightness of the baked data. |
| **Bake** | Include this volume in the next supported lightmapper bake. Disable it to preserve existing textures. |
| **Reserve UV Space** | With **Bake** disabled, reserve a blank part of the atlas for a custom texture-processing setup. Leave off for normal baked volumes. |
| **Adaptive Resolution** | Calculate grid resolution from box size and **Voxels Per Unit**. |
| **Voxels Per Unit** | Set grid density along each meter. |
| **Resolution** | X/Y/Z cell counts. Disable **Adaptive Resolution** to control these manually. |

**Limits:** Regular and Additive Light Volumes share **32 active slots**. Disable unused zones if the scene contains more. Froxel Clustering only applies to Point Light Volumes.
