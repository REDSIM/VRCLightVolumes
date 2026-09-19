[VRC Light Volumes](../README.md) | **How to Use** | [Best Practices](./BestPractices.md) | [Scripting API](./ScriptingAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# How to Use

| Menu |
| --- |
| **Overview**<br />• [VRC Light Volumes System](#vrc-light-volumes-system)<br />• [Light Volumes for Avatars](#light-volumes-for-avatars)<br />• [Setup Regular Light Volumes](#setup-regular-light-volumes)<br />• [Setup Point Light Volumes](#setup-point-light-volumes)<br />• [Keep It Performant](#keep-it-performant) |
| [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) |
| [Point Light Volumes](./HowToUse_PointLightVolumes.md) |
| [Froxel Clustering](./HowToUse_FroxelClustering.md) |
| [Shadows](./HowToUse_Shadows.md) |
| [Material Sources](./HowToUse_PointLightMaterialSources.md) |
| [AudioLink](./HowToUse_AudioLinkIntegration.md) |
| [TV Screens](./HowToUse_TVScreensIntegration.md) |
| [Debugging](./HowToUse_Debugging.md) |
| [How It Works](./HowToUse_HowItWorks.md) |

<a id="choose-what-you-need"></a>

## VRC Light Volumes System

![Light Volumes placed throughout the example scene](./Preview_1.png)

VRC Light Volumes has two main parts:

[**Regular Light Volumes**](#setup-regular-light-volumes) complement Unity Light Probes with per-pixel baked lighting stored in a 3D voxel grid. Place box-shaped volumes around rooms, much like Reflection Probes, then bake them with a [supported lightmapper](./TechnicalDetails.md#supported-lightmappers). You still bake lightmaps for walls and floors, but use Light Volumes to light up avatars, moving props and tiny static details in your world.

[**Point Light Volumes**](#setup-point-light-volumes) are custom realtime Point, Spot and Area lights, similar to Unity's built-in lights. They work separately from Regular Light Volumes and do not store lighting in voxels. Use them for lamps, flashlights and screens that move or change in game.

> [!IMPORTANT]
> World surfaces and props need a [shader that supports VRC Light Volumes](./CompatibleShaders.md), with Light Volumes enabled if the shader has an option for it. Unity's Standard shader does not support Light Volumes.

## Light Volumes for Avatars

Use an [avatar shader that supports VRC Light Volumes](./CompatibleShaders.md) and enable its Light Volumes option if it has one. No avatar component is needed.

> [!NOTE]
> Light Volumes are set up in worlds. Avatars can receive their lighting, but cannot act as Light Volume light sources.

<a id="your-first-baked-room"></a>

## Setup Regular Light Volumes

![A Light Volume covering a room, with its lighting grid visible](./Preview_3.png)

### 1. Prepare The Scene Lighting

Set up the scene's lights, materials and geometry for your chosen [lightmapper](./TechnicalDetails.md#supported-lightmappers).

<a id="2-cover-the-room-with-a-light-volume"></a>

### 2. Place Light Volumes

1. Right-click in the Hierarchy and choose **Light Volume**. Give each volume you bake a unique name. To copy an existing Reflection Probe's bounds, right-click that probe and choose **Light Volume**.
2. Click **Edit Bounds** in its Inspector and resize the box to cover the space that needs lighting, like a Reflection Probe. You can also use the Scale tool.
3. Enable **Bake** and **Adaptive Resolution**. As a starting point, set **Voxels Per Unit** to `1` for large open-world spaces, `3` for medium-sized areas or `6` for small rooms.
4. Click **Preview Voxels** to see the lighting grid. Higher density captures finer lighting details. Doubling **Voxels Per Unit** creates roughly **8 times as many voxels**, increasing bake time and memory use.

Add more volumes to cover the areas that need lighting. The **Light Volume Manager** is created automatically and lists the volumes in the scene. Where Regular Light Volumes overlap, a higher **Weight** gives a volume priority.

Keep the Manager in the same scene as the volumes. Close other world scenes while setting up or baking, so their Managers do not conflict.

### 3. Keep Lighting For Other Avatars

The recommended setup includes both Light Volumes and ordinary Unity Light Probes. Probes keep avatars and materials without Light Volume support lit. If the scene has no Light Probe Group, create one before the first bake.

Select a Light Volume and click **Generate Light Probes**. Choose the probe density, then click **Create Light Probe Group**. Inspect the new group's points and move any that are inside walls or floors into open space.

### 4. Bake And Check The Result

1. Save the scene and select the **Light Volume Manager**. Set **Baking Mode** to **Progressive** for Unity Progressive, **Bakery** for Bakery, or **Custom Lightmapper** for other supported lightmappers.
2. Run a bake in your lightmapper. Wait for the bake and Light Volume processing to finish.
3. Select the volume. Its **Texture 0**, **Texture 1** and **Texture 2** fields should now be filled.
4. Check the baked lighting on objects using a shader that supports VRC Light Volumes.
5. Check the lighting in Play Mode and in a VRChat build on your target platform.
6. Save the scene again.

The bake saves the volume textures beside the scene. **Pack Light Volumes** runs automatically when the bake finishes.

See [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) for density, overlap and blending settings.

<a id="add-a-movable-light"></a>

## Setup Point Light Volumes

![A Spot Light Volume illuminating the floor, with its cone shown in the Scene view](./Preview_2.png)

1. Right-click in the Hierarchy and choose **Point Light Volume**.
2. Set **Type** to **Point Light**, **Spot Light** or **Area Light**.

3. For Point and Spot lights, keep **Projection** set to **Parametric** for ordinary lighting. Use **Custom** to project a cookie or cubemap.
4. Place the light. For Point and Spot lights, set **Light Source Size** to the emitter's radius. For Area lights, set the width and height with the Transform's X and Y scale. Then adjust **Color** and **Intensity**.

   Small sources may need **Intensity** values in the hundreds or thousands. A smaller source needs higher intensity to produce the same lighting. These values use a different scale from Unity Lights.

> [!TIP]
> Scaling the light's GameObject also scales its light source. Set its size before adjusting **Intensity**.

5. Enable **Debug Range** to see how far the light reaches. Raise **Brightness Cutoff** on the Manager to shorten light ranges and reduce overlap. Keep it low enough to avoid visible cutoffs.
6. If the light will move, rotate or scale in game, enable **Dynamic** on it and **Auto Update Volumes** on the Manager.
7. To add shadows, enable **Shadows > Enabled** and click **Bake Shadows**. Rebake after moving the light or shadow-casting geometry. Use a [runtime shadow baker](./HowToUse_Shadows.md#realtime-shadows) for real-time shadow updates.

A light can change color or intensity without **Dynamic**. That setting updates its position, rotation and scale. See [Point Light Volumes](./HowToUse_PointLightVolumes.md) for light types and projection settings.

## Keep It Performant

Light Volumes are designed to run efficiently, but too many lights, excessive overlap or constant shadow updates can still reduce frame rate.

- For groups of stationary lights, use your lightmapper's lights and bake them into Regular Light Volumes. Don't use hundreds of Point Light Volumes for lighting that can stay baked.
- Use Point Light Volumes where you need separate control. Disable lights in unused areas.
- Keep light ranges local. Avoid stacking many Point Light Volumes over the same visible surfaces; use **Debug Range** to check their overlap.
- Use [**Froxel Clustering**](./HowToUse_FroxelClustering.md) for many lights spread across different areas. It helps less when they all illuminate the same surface.
- Prefer baked shadows. Capture changes only when needed, and reserve continuous shadow updates for lights that need them.
- Test the busiest areas on the intended device, especially Quest. Check performance as you add lights, including in mirrors.

> [!IMPORTANT]
> Up to **32 Regular and Additive Light Volumes combined** and **128 Point/Spot/Area lights** can be active at once. The scene can contain more if you disable unused volumes and enable them when needed. These are capacity limits, not performance targets; a scene can slow down well below them.
