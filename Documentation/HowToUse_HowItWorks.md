[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | [Best Practices](./BestPractices.md) | [UdonSharp API](./UdonSharpAPI.md) | [Unity Editor API](./UnityEditorAPI.md) | [Shader Integration](./ForDevelopers.md) | [Compatible Shaders](./CompatibleShaders.md)

# How VRC Light Volumes Work

**Guides:** [Overview](./HowToUse.md) · [Regular Light Volumes](./HowToUse_RegularLightVolumes.md) · [Point Light Volumes](./HowToUse_PointLightVolumes.md) · [Froxel Clustering](./HowToUse_FroxelClustering.md) · [Shadows](./HowToUse_Shadows.md) · [Material Sources](./HowToUse_PointLightMaterialSources.md) · [Area Light Emission](./HowToUse_AreaLightEmission.md) · [AudioLink](./HowToUse_AudioLinkIntegration.md) · [TV Screens (Older Workflow)](./HowToUse_TVScreensIntegration.md) · [Debugging](./HowToUse_Debugging.md) · **How It Works**

A **Regular Light Volume** remembers the lighting at many points in a room. A **Point Light Volume** calculates light from a Point, Spot or Area source while the scene is rendered. Compatible shaders combine these contributions to shade a surface.

The **Light Volume Manager** shares that data with world and avatar shaders. An avatar needs a compatible shader, but no Light Volume component.

## Why A Grid Helps

Unity Light Probes store baked lighting at scattered points. An ordinary probe-lit Renderer uses one interpolated set of lighting data for the whole object. A Regular Light Volume stores the lighting on a 3D grid instead; each cell is called a **voxel**. Its shader samples nearby voxels at the position of each shaded pixel, so a character can have one side in a brightly lit doorway and the other in a dark room.

![A Light Volume's grid of lighting samples inside a room](./Preview_3.png)

More voxels capture smaller changes in lighting. They also take more memory and time to bake. A small, dense volume around a detailed area is usually more useful than increasing the density of the entire world. See [placement and resolution](./HowToUse_RegularLightVolumes.md).

Moving a baked volume moves its stored lighting. It does not calculate new light bouncing off the surroundings. Re-bake after changing the room, its lights or other objects that should affect the baked result.

## Remembering Color And Direction

Each voxel stores **spherical harmonics (SH)**: a compact approximation of the light arriving from different directions. Light Volumes uses first-order SH, often written **L0 + L1**.

- **L0** is the average light color. It looks the same from every surface direction.
- **L1** records how that color changes with direction. It lets a surface facing the light look brighter than a surface facing away.

![Lighting represented as an ambient color and directional components](./SH_01.png)

The same baked data in two [debug views](./HowToUse_Debugging.md):

| L0: average color | L0 + L1: color and direction |
| --- | --- |
| ![L0 debug view: spheres keep their local color but look flat without directional shading](./Images/debug-sh-l0.png) | ![L1 debug view: directional lighting gives the same spheres rounded shading](./Images/debug-sh-l1.png) |

Look at the spheres: L0 keeps their local lighting color, while L1 adds the change in brightness around their surfaces. The black walls are outside this volume.

This is an approximation. It cannot preserve every sharp shadow or reflection. Keep lightmaps for detailed static surfaces and reflection probes for reflections. The SH data can also provide a simple specular highlight, but it does not replace a reflection of the room.

## Overlapping Volumes

At each surface position, the shader finds the containing Regular Light Volume with the highest **Weight**. Near its edge, **Smooth Blending** allows a transition to the next containing volume. This lets a small, detailed room volume take priority over a larger background volume.

If no Regular Light Volume contains the surface, **Light Probes Blending** selects Unity Light Probes as the fallback. When it is off, the lowest-weight Regular Light Volume supplies the fallback instead. **Sharp Bounds** controls blending at edges without another containing volume; it does not disable all blending between overlapping volumes.

An **Additive Light Volume** adds its baked lighting on top. Use one for a separately baked lighting state, such as a lamp that can turn on and off. Regular and Additive volumes share the limit of 32 active volumes.

## Point, Spot And Area Lights

Point Light Volumes do not use a voxel grid. A shader calculates their contribution from the surface position, light shape, size, color and intensity. That is why they can move without re-baking the room's lighting.

![Area, Point and Spot Light Volumes lighting nearby surfaces](./Preview_4.png)

Point and Spot lights in **Parametric** mode get dimmer with distance using inverse-square falloff. A **cookie** changes the emitted pattern: a cubemap for Point lights, or a 2D image for Spot and Area lights. An Area light emits from one side of a rectangle. Its textured emission keeps more detail close to the rectangle and blends toward the average color farther away.

A moving light does not automatically update its shadows. Shadows are a separate capture of the objects around that light. Use an Editor bake for fixed surroundings, **Bake In Game** for a startup capture, or a [runtime shadow baker](./HowToUse_Shadows.md) when blockers need to move.

## Why Overlap Costs Performance

The shader must do work for every light that reaches a surface. Ten small lights spread across separate rooms can be cheaper than ten lights reaching the same wall.

**Froxel Clustering** divides the camera's view into 3D cells, called froxels, and builds a list of possible lights for each cell. Surfaces then skip lights that cannot reach their cell. Exact light and shadow tests still decide the final result. Positions outside this grid use the ordinary light loop, which also keeps mirrors and other cameras working.

**Additive Max Overdraw** limits how many Additive Light Volumes and Point Light Volumes a pixel processes. Each group has its own counter with the same limit. Lower values can omit visible lights; this setting is a quality tradeoff. Clustering does not raise the active-light or overlap limits.

Use [Best Practices](./BestPractices.md) for practical tuning and the [clustering guide](./HowToUse_FroxelClustering.md) for its settings.

## For Developers

The baked L0/L1 coefficients are stored across three 3D textures per volume, then packed into one shared atlas. Projection sources and shadows use separate texture arrays. The Manager uploads transforms and light settings to global shader data.

Shaders read that data through the functions in [Shader Integration](./ForDevelopers.md). Scripts should use the [UdonSharp API](./UdonSharpAPI.md) to change lights and the [Unity Editor API](./UnityEditorAPI.md) to bake or pack data.
